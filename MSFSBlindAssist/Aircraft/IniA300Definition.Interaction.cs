using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Writes: every panel row is claimed here, so MainForm's generic paths (SetLVar's data-definition
/// write, "pressed" announcements) never touch an A300 control. Nothing the pilot does on a panel
/// is spoken back; only errors are.
/// </summary>
public partial class IniA300Definition
{
    private readonly A300CommandedState _commanded = new();

    /// <summary>Steps still owed after a wait that has not finished (a hold button's release, a
    /// spring switch's return), keyed by run, so <see cref="Dispose"/> can let go first.</summary>
    private readonly Dictionary<int, IReadOnlyList<A300Step>> _owed = new();
    private int _nextRunId;

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(varKey, out var row))
            return false;   // a base variable: MainForm's generic path
        _sim = simConnect;

        if (row.Action == A300RowAction.Custom)
        {
            HandleLeverSet(row, value, simConnect, announcer);
            return true;
        }
        if (row.Action == A300RowAction.Typed)
        {
            SetTyped(row.Key, value, simConnect, announcer, row.Name);
            return true;
        }

        var control = row.Control!;
        var plan = row.Action switch
        {
            A300RowAction.Set => A300WritePlan.ForSet(control, value, CurrentValue(row.Key, simConnect)),
            A300RowAction.Press => A300WritePlan.ForPress(control),
            A300RowAction.Increase => A300WritePlan.ForStep(control, increase: true),
            A300RowAction.Decrease => A300WritePlan.ForStep(control, increase: false),
            _ => A300Plan.Refused(A300WritePlan.NotSettableRefusal),
        };
        // While the tablet's IDC option is on, the IDC rewrites the transponder mode every frame: the
        // write would be undone, so it is refused aloud and the combo put back ([A300-19]).
        if (A300Idc.Refuses(row.Key, Cached(simConnect, A300Idc.OptionKey)))
            plan = A300Plan.Refused(A300Idc.Refusal);
        // A spring switch returns to its rest, so the position to remember is the rest.
        double? commanded = row.Action != A300RowAction.Set ? null
            : control.Kind == A300Kinds.Spring ? control.StateForPosition(control.Rest ?? 1)
            : value;
        double? autobrakeBefore = Cached(simConnect, A300Autobrake.LevelKey);
        bool sent = Execute(row, plan, simConnect, announcer, commanded);

        // An autobrake press meant to arm that the aircraft refused says so once it has had its
        // chance; the label shows every other result ([A300-20]).
        if (sent && A300Autobrake.ByButton.TryGetValue(row.Key, out var autobrake))
            _ = CheckAutobrakeArmedAsync(autobrake.Level, autobrakeBefore, simConnect, announcer);

        // An FCU knob step is read back once it lands ("Heading 271"): a numeric confirmation, as a
        // typed value's is. The altitude window's own call-out is told it is an echo.
        if (sent && row.Action is A300RowAction.Increase or A300RowAction.Decrease
            && A300FcuWindows.ReadoutByKnob.TryGetValue(control.Key, out var readoutKey))
        {
            if (readoutKey == A300Readouts.AltitudeKey)
                _altitudeWindow.SuppressEcho(Clock());
            _ = ReadBackAsync(simConnect, announcer, readoutKey, v => A300FcuWindows.Phrase(readoutKey, v, IsMach()));
        }
        // A flex temperature knob step, the same way ("Flex temperature 46 degrees", [A300-21]).
        if (sent && row.Action is A300RowAction.Increase or A300RowAction.Decrease && control.Key == A300Trp.FlexKnobKey)
            _ = ReadBackAsync(simConnect, announcer, A300Readouts.FlexTemperatureKey, A300Trp.FlexPhrase);
        return true;
    }

    /// <summary>
    /// After an autobrake press, waits <see cref="ToggleReadBackMs"/> (the aircraft takes the button's
    /// command on its next update) and reads the level fresh: a press meant to arm that left its level
    /// unarmed says "Autobrake did not arm" (<see cref="A300Autobrake.DidNotArm"/>). Arming, and the
    /// active level's own disarm, are silent: the button's label shows them.
    /// </summary>
    private async Task CheckAutobrakeArmedAsync(int level, double? before, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            await TypedDelay(ToggleReadBackMs);
            if (_disposed)
                return;
            double? after = await ReadFresh(sim, A300Autobrake.LevelKey, ReadoutTimeoutMs);
            if (!_disposed && A300Autobrake.DidNotArm(level, before, after))
                announcer.AnnounceImmediate(A300Autobrake.DidNotArmMessage);
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"Autobrake check failed: {ex.Message}");
        }
    }

    /// <summary>The value to plan from: what MSFSBA just commanded while it is fresh, else the cache.</summary>
    private double? CurrentValue(string key, SimConnectManager sim) =>
        _commanded.Resolve(key, Cached(sim, key), Clock());

    /// <summary>Runs a plan, or refuses it aloud; true when its steps were sent.</summary>
    private bool Execute(A300PlacedRow row, A300Plan plan, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        double? commandedValue)
    {
        if (plan.Refusal != null)
        {
            announcer.Announce($"{row.Name}: {plan.Refusal}");
            ReRead(row.Key, sim);
            return false;
        }
        if (plan.IsEmpty)
            return false;   // already there; the screen reader has said so
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            ReRead(row.Key, sim);
            return false;
        }
        if (commandedValue is double target)
            _commanded.Record(row.Key, target, Clock());
        _ = RunAsync(plan.Steps, sim);
        return true;
    }

    /// <summary>
    /// Runs a plan's steps on the UI thread (each await resumes there, so SimConnect is never used
    /// from a pool thread). The steps after a wait are recorded as owed until they have run.
    /// </summary>
    private async Task RunAsync(IReadOnlyList<A300Step> steps, SimConnectManager sim)
    {
        int run = ++_nextRunId;
        try
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (_disposed)
                    return;
                switch (steps[i])
                {
                    case A300CalcStep calc:
                        Send(sim, calc.Rpn);
                        break;
                    case A300DelayStep delay:
                        _owed[run] = steps.Skip(i + 1).ToList();
                        await Delay(delay.Milliseconds);
                        _owed.Remove(run);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"A control write failed: {ex.Message}");
        }
        finally
        {
            _owed.Remove(run);
        }
    }

    /// <summary>Sends every owed step at once (the aircraft is switched away or the app is closing).</summary>
    private void ReleaseOwedSteps()
    {
        var sim = _sim;
        if (sim == null)
            return;
        foreach (var steps in _owed.Values.ToList())
            foreach (var step in steps)
                if (step is A300CalcStep calc)
                    Send(sim, calc.Rpn);
        _owed.Clear();
    }

    /// <summary>The three hand-written levers: refused aloud when the write cannot land, and the
    /// combo or slider put back.</summary>
    private void HandleLeverSet(A300PlacedRow row, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        string rpn = row.Key switch
        {
            A300Levers.FlapsKey => A300Levers.FlapsRpn((int)Math.Round(value)),
            A300Levers.SpoilersArmKey => A300Levers.SpoilersArmRpn(value),
            _ => A300Levers.SpeedBrakeRpn(value),
        };
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            ReRead(row.Key, sim);
            return;
        }
        Send(sim, rpn);
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        if (action == HotkeyAction.MonitorManager)
        {
            hotkeyManager.ExitOutputHotkeyMode();
            (parentForm as MainForm)?.ShowA300MonitorManagerDialog();
            return true;
        }
        if (TryHandleAutoflightHotkey(action, simConnect, announcer, parentForm, hotkeyManager))
            return true;
        if (TryHandleDisplayHotkey(action, simConnect, announcer))
            return true;
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }
}
