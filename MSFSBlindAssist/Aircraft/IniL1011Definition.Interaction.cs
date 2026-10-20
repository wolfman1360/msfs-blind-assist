using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Writes: every panel row is claimed here, so MainForm's generic paths (SetLVar's data-definition
/// write, "pressed" announcements) never touch a TriStar control. Nothing the pilot does on a panel
/// is spoken back; only errors and typed-value confirmations are.
/// </summary>
public partial class IniL1011Definition
{
    private readonly L1011CommandedState _commanded = new();

    /// <summary>Sends one calculator string (<see cref="SimConnectManager.ExecuteCalculatorCodeUnique"/>); tests
    /// replace it to see what was sent, and in what order.</summary>
    internal Action<SimConnectManager, string> SendCalc { get; set; } = (sim, rpn) => sim.ExecuteCalculatorCodeUnique(rpn);

    /// <summary>The wait inside a plan (a button's hold, the pause between key strings); tests replace it.</summary>
    internal Func<int, Task> StepDelay { get; set; } = Task.Delay;

    /// <summary>Steps still owed after a hold that has not finished (a button's release), keyed by
    /// run, so <see cref="Dispose"/> can let go of a held button before the definition goes away.</summary>
    private readonly Dictionary<int, IReadOnlyList<L1011Step>> _owed = new();
    private int _nextRunId;

    /// <summary>Per Set row, how many writes have been sent: a settle re-read goes ahead only while its
    /// write is still the row's latest. Touched on the UI thread only.</summary>
    private readonly Dictionary<string, int> _setGeneration = new(StringComparer.Ordinal);

    public override bool HandleUIVariableSet(string varKey, double value, SimVarDefinition varDef,
        SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(varKey, out var row))
            return false;   // a base variable: MainForm's generic path
        _sim = simConnect;

        if (row.Action == L1011RowAction.Custom)
        {
            HandleCustomSet(row, value, simConnect, announcer);
            return true;
        }

        var control = row.Control!;
        if (AfcsRefusal(row, value, simConnect) is string afcsRefusal)
        {
            announcer.Announce($"{row.Name}: {afcsRefusal}");
            SnapBack(row.Key, simConnect);
            return true;
        }
        var plan = row.Action switch
        {
            L1011RowAction.Set => L1011WritePlan.ForSet(control, value, PlanFrom(row, simConnect)),
            L1011RowAction.Press => L1011WritePlan.ForPress(control, row.HoldMs),
            L1011RowAction.Increase => L1011WritePlan.ForStep(control, increase: true),
            L1011RowAction.Decrease => L1011WritePlan.ForStep(control, increase: false),
            _ => L1011Plan.Refused(L1011WritePlan.NotSettableRefusal),
        };
        Execute(row, plan, simConnect, announcer, commandedValue: row.Action == L1011RowAction.Set ? value : null);
        return true;
    }

    /// <summary>The cache a write plans from (<see cref="SimConnectManager.GetCachedVariableValue"/>); tests replace it.</summary>
    internal Func<SimConnectManager, string, double?> CachedValue { get; set; } = (sim, key) => sim.GetCachedVariableValue(key);

    /// <summary>The position to plan from: what MSFSBA just commanded while it is fresh, else the cache.</summary>
    private double? CurrentPosition(string key, SimConnectManager sim) =>
        _commanded.Resolve(key, CachedValue(sim, key), Clock());

    /// <summary>The position a Set row's plan starts from. A knob is read on request (when its panel
    /// opens), never by the batch, so its cache can be stale: it trusts only what MSFSBA just commanded,
    /// and is written otherwise, so a slider moved back is never dropped as already there.</summary>
    private double? PlanFrom(L1011PlacedRow row, SimConnectManager sim) =>
        row.Control!.Kind == L1011Kinds.Knob
            ? _commanded.Resolve(row.Key, cached: null, Clock())
            : CurrentPosition(row.Key, sim);

    private void Execute(L1011PlacedRow row, L1011Plan plan, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        double? commandedValue)
    {
        if (plan.Refusal != null)
        {
            announcer.Announce($"{row.Name}: {plan.Refusal}");
            SnapBack(row.Key, sim);
            return;
        }
        if (plan.IsEmpty)
            return;   // already there; the screen reader has said so
        if (!CanLand(sim))
        {
            announcer.Announce($"{row.Name} unavailable");
            SnapBack(row.Key, sim);
            return;
        }
        if (commandedValue is double target)
            _commanded.Record(row.Key, target, Clock(), plan.Via);
        var run = RunAsync(plan.Steps, sim);
        // A Set row is read again once its write has landed, so its combo or slider shows what the
        // aircraft did: a gauge can put a switch straight back without the variable changing (no
        // delivery comes), and a knob is never refreshed by the batch. Only the LAST write of a burst
        // is read again: a slider swept by a held key writes every step, and an earlier step's answer
        // would pull the thumb, and the next write, back behind where the pilot has got to.
        if (commandedValue != null)
        {
            int generation = _setGeneration[row.Key] = _setGeneration.GetValueOrDefault(row.Key) + 1;
            _ = ReReadAfterSettleAsync(row.Key, L1011WritePlan.ReReadSettleMs, sim, run,
                stillLatest: () => _setGeneration.GetValueOrDefault(row.Key) == generation);
        }
    }

    /// <summary>A refused pick must not leave the combo on a position the aircraft never took: ask
    /// for the value again (<see cref="ReRead"/>) so the next delivery puts it back.</summary>
    private void SnapBack(string key, SimConnectManager sim) => ReRead(key, sim);

    /// <summary>
    /// Runs a plan's steps on the UI thread (each await resumes there, so SimConnect is never used
    /// from a pool thread). The steps after a hold are recorded as owed until they have run.
    /// </summary>
    private async Task RunAsync(IReadOnlyList<L1011Step> steps, SimConnectManager sim)
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
                    case L1011CalcStep calc:
                        SendCalc(sim, calc.Rpn);
                        break;
                    case L1011DelayStep delay:
                        _owed[run] = steps.Skip(i + 1).ToList();
                        await StepDelay(delay.Milliseconds);
                        _owed.Remove(run);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"A control write failed: {ex.Message}");
        }
        finally
        {
            _owed.Remove(run);
        }
    }

    /// <summary>Sends every owed release at once (the aircraft is switched away or the app is closing).</summary>
    private void ReleaseOwedSteps()
    {
        var sim = _sim;
        if (sim == null)
            return;
        foreach (var steps in _owed.Values.ToList())
            foreach (var step in steps)
                if (step is L1011CalcStep calc)
                    SendCalc(sim, calc.Rpn);
        _owed.Clear();
    }

    /// <summary>One hand-written write (<see cref="L1011Levers"/>): refused aloud when it cannot land,
    /// and the row's combo snapped back. True when the write was sent.</summary>
    private bool SendCustom(string key, string name, string rpn, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        string? confirmation = null)
    {
        if (!CanLand(sim))
        {
            announcer.Announce($"{name} unavailable");
            SnapBack(key, sim);
            return false;
        }
        sim.ExecuteCalculatorCodeUnique(rpn);
        if (confirmation != null)
            announcer.Announce(confirmation);   // a typed value: the pilot needs the exact figure back
        return true;
    }

    /// <summary>
    /// Reads a row again once its write has settled, so its combo or slider follows what the aircraft
    /// did: after <paramref name="run"/> (a plan's steps, a spring switch's return included) has
    /// finished, plus <paramref name="settleMs"/>, and only if <paramref name="stillLatest"/> says no
    /// later write has superseded this one. The awaits resume on the UI thread; nothing is asked once
    /// the definition has gone away.
    /// </summary>
    private async Task ReReadAfterSettleAsync(string key, int settleMs, SimConnectManager sim, Task? run = null,
        Func<bool>? stillLatest = null)
    {
        try
        {
            if (run != null)
                await run;
            await SettleDelay(settleMs);
            if (_disposed || stillLatest?.Invoke() == false)
                return;
            ReRead(key, sim);
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"Re-read of {key} failed: {ex.Message}");
        }
    }

    private void HandleCustomSet(L1011PlacedRow row, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (L1011InsDevices.FromWindowKey(row.Key) is L1011InsDevice device)
        {
            OpenInsWindow?.Invoke(device);
            return;
        }
        switch (row.Key)
        {
            case L1011Levers.BreakerListKey:
                OpenCircuitBreakers?.Invoke();
                return;
            case L1011Afcs.DisconnectKey:
                DisconnectAutopilot(sim, announcer);
                return;
            case L1011Levers.FlapHandleKey:
                SendCustom(row.Key, row.Name, L1011Levers.FlapRpn((int)Math.Round(value)), sim, announcer);
                return;
            case L1011Levers.GearLeverKey:
                // The lever's own gauge refuses "up" on the ground: the write lands and nothing moves.
                if (SendCustom(row.Key, row.Name, L1011Levers.GearRpn(value), sim, announcer))
                    _ = ReReadAfterSettleAsync(row.Key, L1011Levers.GearSettleMs, sim);
                return;
            case L1011Levers.SpeedBrakeKey:
                SendCustom(row.Key, row.Name, L1011Levers.SpeedBrakeRpn(value), sim, announcer);
                return;
            case L1011Levers.GroundSpoilersKey:
                SendCustom(row.Key, row.Name, L1011Levers.GroundSpoilersRpn(value), sim, announcer);
                return;
            case L1011Levers.ParkingBrakeKey:
                SendCustom(row.Key, row.Name, L1011Levers.ParkingBrakeRpn(value), sim, announcer);
                return;
            case L1011Levers.SquawkKey:
                if (L1011Levers.SquawkBcd(value) is uint bcd)
                    SendCustom(row.Key, row.Name, L1011Levers.SquawkRpn(bcd), sim, announcer, L1011Levers.SquawkConfirmation(bcd));
                else
                    announcer.Announce($"{row.Name}: {L1011Levers.SquawkError}");
                return;
            case L1011Levers.StabTrimKey:
                if (L1011Levers.StabTrimDegrees(value) is double degrees)
                    SendCustom(row.Key, row.Name, L1011Levers.StabTrimRpn(degrees), sim, announcer, L1011Levers.StabTrimConfirmation(degrees));
                else
                    announcer.Announce($"{row.Name}: {L1011Levers.StabTrimRangeError}");
                return;
        }

        if (L1011Afcs.ValueFor(row.Key) != null)
        {
            SetAfcsValue(row.Key, value, sim, announcer);
            return;
        }
        if (L1011Levers.AltimeterIndex(row.Key) is int index)
        {
            if (L1011Levers.AltimeterMillibars(value) is double mb)
                SendCustom(row.Key, row.Name, L1011Levers.AltimeterRpn(index, mb), sim, announcer, L1011Levers.AltimeterConfirmation(row.Name, mb));
            else
                announcer.Announce($"{row.Name}: {L1011Levers.AltimeterRangeError}");
            return;
        }
        if (L1011Levers.NavEntry(row.Key) is int nav)
        {
            if (L1011Levers.NavFrequencyHz(value) is uint hz)
                SendCustom(row.Key, row.Name, L1011Levers.NavFrequencyRpn(nav, hz), sim, announcer,
                    L1011Levers.FrequencyConfirmation($"NAV {nav}", value, 2));
            else
                announcer.Announce($"{row.Name}: {L1011Levers.NavRangeError}");
            return;
        }
        if (L1011Levers.ComEntry(row.Key) is { } com)
        {
            var (radio, active) = com;
            if (L1011Levers.ComFrequencyHz(value) is uint hz)
                SendCustom(row.Key, row.Name, L1011Levers.ComFrequencyRpn(radio, hz, active), sim, announcer,
                    L1011Levers.FrequencyConfirmation($"COM {radio} {(active ? "active" : "standby")}", value, 3));
            else
                announcer.Announce($"{row.Name}: {L1011Levers.ComRangeError}");
        }
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    public override bool HandleHotkeyAction(HotkeyAction action, SimConnectManager simConnect,
        ScreenReaderAnnouncer announcer, Form parentForm, HotkeyManager hotkeyManager)
    {
        _sim = simConnect;
        if (HandleAutopilotHotkey(action, simConnect, announcer, parentForm, hotkeyManager))
            return true;
        switch (action)
        {
            case HotkeyAction.MonitorManager:
                hotkeyManager.ExitOutputHotkeyMode();
                (parentForm as MainForm)?.ShowL1011MonitorManagerDialog();
                return true;
            case HotkeyAction.ReadFlaps:
                _ = SpeakAsync(simConnect, announcer, "Flaps",
                    v => L1011Speech.Flaps(v[0], v[1]), L1011Levers.FlapHandleKey, "L1011_RO_FLAPS_ANGLE");
                return true;
            case HotkeyAction.ReadGear:
                _ = SpeakAsync(simConnect, announcer, "Gear",
                    v => L1011Speech.Gear(v[0], v[1], v[2], v[3]),
                    L1011Levers.GearLeverKey, "L1011_RO_GEAR_LEFT", "L1011_RO_GEAR_NOSE", "L1011_RO_GEAR_RIGHT");
                return true;
            case HotkeyAction.ReadAltimeter:
                _ = SpeakAsync(simConnect, announcer, "Altimeter", v => L1011Speech.Altimeter(v[0]), "L1011_RO_ALTIMETER_1");
                return true;
            case HotkeyAction.ReadFuelQuantity:
                _ = SpeakAsync(simConnect, announcer, "Fuel", v => L1011Speech.Fuel(v[0], kilograms: false), "L1011_RO_FUEL_TOTAL");
                return true;
            case HotkeyAction.ReadFuelInfo:
                _ = SpeakAsync(simConnect, announcer, "Fuel", v => L1011Speech.Fuel(v[0], kilograms: true), "L1011_RO_FUEL_TOTAL");
                return true;
            // Gross weight: the stock request every other aircraft uses (W pounds, Shift+W kilograms).
            case HotkeyAction.ReadWaypointInfo:
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT");
                return true;
            case HotkeyAction.ReadGrossWeightKg:
                simConnect.RequestSingleValue((int)SimConnectManager.DATA_DEFINITIONS.DEF_GROSS_WEIGHT_KG,
                    "TOTAL WEIGHT", "pounds", "GROSS_WEIGHT_KG");
                return true;
            // Input mode Ctrl+B: one entry sets the captain's, first officer's and standby altimeters.
            case HotkeyAction.FCUSetBaro:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAltimetersDialog(simConnect, announcer, parentForm);
                return true;
            // Input mode Ctrl+N: NAV 1 and NAV 2, frequency and course.
            case HotkeyAction.SetNavRadios:
                hotkeyManager.ExitInputHotkeyMode();
                _ = ShowNavRadiosDialogAsync(simConnect, announcer, parentForm);
                return true;
        }
        return base.HandleHotkeyAction(action, simConnect, announcer, parentForm, hotkeyManager);
    }

    // =================================================================================
    // Input mode Ctrl+B and Ctrl+N
    // =================================================================================

    /// <summary>Shows a tracked dialog, or brings it forward when a second press finds it open.
    /// Tracked so an aircraft switch closes it (<see cref="BaseAircraftDefinition.DisposeTrackedWindows"/>):
    /// its writes would otherwise reach whatever aircraft is loaded next.</summary>
    private static Action<T> ShowOrActivate<T>(Form parentForm) where T : Form => form =>
    {
        if (form.Visible)
            form.Activate();
        else
            form.Show(parentForm);
    };

    /// <summary>
    /// Ctrl+B: the shared value dialog, accepting what the typed altimeter fields accept (inches or
    /// hectopascals, <see cref="L1011Levers.AltimeterMillibars"/>). One entry sets all three
    /// altimeters in one string. No STD or units buttons: the TriStar's altimeters are steam gauges.
    /// Nothing is pre-filled (the shared dialog has no initial value). Refused before the dialog
    /// opens, and again when a value is set, when the calculator path cannot land. It shares the
    /// autopilot value boxes' slot (<see cref="ShowValueBox"/>), so Ctrl+H never re-shows it.
    /// </summary>
    private void ShowAltimetersDialog(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate(L1011Levers.Unavailable(L1011Levers.AltimetersName));
            return;
        }
        ShowValueBox("Altimeter Setting", "altimeter",
            "28.20 to 31.30 inches, or 955 to 1060 hectopascals; sets captain, first officer and standby",
            announcer,
            input => L1011Levers.AltimeterEntryMillibars(input) != null
                ? (true, "")
                : (false, L1011Levers.AltimetersEntryError),
            new List<ToggleButtonDef>(),
            input => SetAllAltimeters(input, sim, announcer),
            parentForm);
    }

    /// <summary>Writes Ctrl+B's entry to the three altimeters and confirms the value ("Altimeters
    /// 1013, 29.92"); the dialog has already refused an entry it cannot use.</summary>
    private void SetAllAltimeters(string input, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (L1011Levers.AltimeterEntryMillibars(input) is not double mb)
            return;
        if (_disposed || !CanLand(sim))
        {
            announcer.AnnounceImmediate(L1011Levers.Unavailable(L1011Levers.AltimetersName));
            return;
        }
        sim.ExecuteCalculatorCodeUnique(L1011Levers.AllAltimetersRpn(mb));
        announcer.AnnounceImmediate(L1011Levers.AltimeterConfirmation(L1011Levers.AltimetersName, mb));
    }

    /// <summary>How long Ctrl+N waits for the NAV radios before opening with 108.00 and course 0.</summary>
    public const int NavPrefillTimeoutMs = 2000;

    /// <summary>
    /// Ctrl+N: the shared NAV radios dialog, pre-filled from the NAV radios as they are now through
    /// the fixed one-shot read every aircraft's N readout uses (<see cref="SimConnectManager.RequestNavRadioInfo"/>,
    /// no new definition), or 108.00 and course 0 when it does not answer in time. Every await
    /// resumes on the UI thread. Refused before the dialog opens, and again when the values are
    /// set, when the calculator path cannot land.
    /// </summary>
    private async Task ShowNavRadiosDialogAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, Form parentForm)
    {
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate(L1011Levers.Unavailable(L1011Levers.NavRadiosName));
            return;
        }
        try
        {
            SimConnectManager.NavRadioData? live = null;
            var answer = new TaskCompletionSource<SimConnectManager.NavRadioData>(TaskCreationOptions.RunContinuationsAsynchronously);
            sim.RequestNavRadioInfo(data => answer.TrySetResult(data));
            try
            {
                live = await answer.Task.WaitAsync(TimeSpan.FromMilliseconds(NavPrefillTimeoutMs));
            }
            catch (TimeoutException)
            {
                Log.Debug("L1011", "NAV radios did not answer; the dialog opens with 108.00 and course 0.");
            }
            if (_disposed || parentForm.IsDisposed)
                return;
            ShowTrackedWindow(
                () => new NavRadiosForm(announcer,
                    L1011Levers.NavPrefillMegahertz(live?.Nav1Freq), L1011Levers.NavPrefillCourse(live?.Nav1Obs),
                    L1011Levers.NavPrefillMegahertz(live?.Nav2Freq), L1011Levers.NavPrefillCourse(live?.Nav2Obs),
                    settings => SetNavRadios(settings, sim, announcer)),
                ShowOrActivate<NavRadiosForm>(parentForm));
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"NAV radios dialog failed: {ex.Message}");
        }
    }

    /// <summary>Tunes both NAV radios and sets both courses in one string, then confirms all four
    /// values in one sentence. The dialog has already checked the ranges.</summary>
    private void SetNavRadios(NavRadioSettings settings, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        string? rpn = L1011Levers.NavRadiosRpn(settings.Nav1FreqMHz, settings.Nav1Course, settings.Nav2FreqMHz, settings.Nav2Course);
        if (rpn == null)
        {
            announcer.AnnounceImmediate($"{L1011Levers.NavRadiosName}: {L1011Levers.NavRangeError}, course 0 to 359");
            return;
        }
        if (_disposed || !CanLand(sim))
        {
            announcer.AnnounceImmediate(L1011Levers.Unavailable(L1011Levers.NavRadiosName));
            return;
        }
        sim.ExecuteCalculatorCodeUnique(rpn);
        announcer.AnnounceImmediate(L1011Levers.NavRadiosConfirmation(
            settings.Nav1FreqMHz, settings.Nav1Course, settings.Nav2FreqMHz, settings.Nav2Course));
    }

    /// <summary>How long a readout key waits for each value before saying it is unavailable.</summary>
    public const int ReadoutTimeoutMs = 1500;

    /// <summary>The wait for the flap handle and the gear lever (the L and Shift+G readout keys): they ride the 1 Hz
    /// continuous batch, so a fresh read is answered by its NEXT delivery, up to a second away, and
    /// 1.5 s left little for a slow one. The value the MD-11 uses for its batch read-backs.</summary>
    public const int BatchReadoutTimeoutMs = 2500;

    /// <summary>How long a readout key waits for <paramref name="key"/>.</summary>
    internal static int ReadoutTimeoutFor(string key) =>
        key is L1011Levers.FlapHandleKey or L1011Levers.GearLeverKey ? BatchReadoutTimeoutMs : ReadoutTimeoutMs;

    /// <summary>Reads each key fresh (in order) and speaks the composed text, or "{what} unavailable".</summary>
    private static async Task SpeakAsync(SimConnectManager sim, ScreenReaderAnnouncer announcer, string what,
        Func<double[], string> compose, params string[] keys)
    {
        try
        {
            var values = new double[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                double? v = await sim.ReadFreshAsync(keys[i], ReadoutTimeoutFor(keys[i]));
                if (v == null)
                {
                    announcer.AnnounceImmediate($"{what} unavailable");
                    return;
                }
                values[i] = v.Value;
            }
            announcer.AnnounceImmediate(compose(values));
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"{what} readout failed: {ex.Message}");
        }
    }
}
