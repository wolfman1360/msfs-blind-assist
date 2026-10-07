using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.PMDG;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The glareshield autopilot (AFCS), on the A300's autoflight-window pattern: typed values
/// (<see cref="L1011Afcs"/>) from the panel's text boxes and from one value box per axis (Ctrl+S, H,
/// A, V) that carries that axis's mode buttons; the Ctrl+P window (the shared
/// <see cref="PMDGAutopilotWindow"/>) with a live status list, the engage paddles and the rest of the
/// cluster; the input-mode toggle keys; and the output-mode readouts. Each control sits in one window
/// only (<see cref="L1011AutoflightWindows"/>), and every press goes through the panel row's write
/// path, so the refusals and the commanded-position hold match the panels. A label, a combo and a
/// status line show the position the aircraft REPORTS, never the one just asked for. A value box button
/// and a toggle key read their result back (queued, so the call-outs the press set off are heard
/// first), because no control of theirs speaks it; Ctrl+P's labels update in place and say nothing.
/// </summary>
public partial class IniL1011Definition
{
    private readonly L1011AfcsAnnouncer _afcs = new();

    /// <summary>How long a value box button or a toggle key waits before reading its result back:
    /// one batch period plus the aircraft applying it (the A300's value).</summary>
    public const int ToggleReadBackMs = 1200;

    /// <summary>The wait before a read-back; tests replace it.</summary>
    internal Func<int, Task> ReadBackDelay { get; set; } = Task.Delay;

    /// <summary>Reads a key fresh (<see cref="SimConnectManager.ReadFreshAsync"/>); tests replace it.</summary>
    internal Func<SimConnectManager, string, int, Task<double?>> ReadFresh { get; set; } =
        (sim, key, timeoutMs) => sim.ReadFreshAsync(key, timeoutMs);

    /// <summary>Sets one typed autopilot value; refused aloud when out of range, when its mode would
    /// overwrite it, or when it cannot land.</summary>
    public void SetAfcsValue(string key, double typed, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        if (L1011Afcs.ValueFor(key) is not L1011AfcsValue kind)
            return;
        string name = L1011Afcs.Name(kind);
        if (L1011Afcs.Normalise(kind, typed) is not int value)
        {
            announcer.Announce($"{name}: enter {L1011Afcs.RangeText(kind)}");
            return;
        }
        if (kind == L1011AfcsValue.VerticalSpeed && AfcsPosition("SWITCH_AFCS_VS", sim) is double vs && vs < 0.5)
        {
            announcer.Announce($"{name}: {L1011Afcs.VerticalSpeedModeOffRefusal}");
            return;
        }
        SendCustom(key, name, L1011Afcs.Rpn(kind, value), sim, announcer, L1011Afcs.Confirmation(kind, value));
    }

    /// <summary>
    /// Disconnects the autopilot as the yoke button does (<see cref="L1011Afcs.DisconnectRpn"/>).
    /// Nothing is confirmed here: the engage paddles going to Off are announced when the aircraft
    /// moves them, which is the confirmation that the disconnect happened.
    /// </summary>
    public void DisconnectAutopilot(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        SendCustom(L1011Afcs.DisconnectKey, "Autopilot disconnect", L1011Afcs.DisconnectRpn, sim, announcer);
    }

    /// <summary>
    /// An autopilot control's position: what MSFSBA just commanded, else the cache, else the last value
    /// delivered through ProcessSimVarUpdate. Null when none is known.
    /// </summary>
    private double? AfcsPosition(string key, SimConnectManager sim) => CurrentPosition(key, sim) ?? _afcs.Last(key);

    /// <summary>
    /// A control's position as the aircraft reports it: the cache, else the last value delivered. Never
    /// what MSFSBA just commanded: a label, a combo or a status line must not say "On" for a switch that
    /// has not moved (the PMDG windows' rule). The commanded position (<see cref="AfcsPosition"/>) only
    /// decides which way a toggle goes and whether a refusal applies.
    /// </summary>
    private double? ReportedPosition(string key, SimConnectManager sim) => sim.GetCachedVariableValue(key) ?? _afcs.Last(key);

    /// <summary>
    /// Why a press must not be sent, or null: a pitch or lateral mode switched ON while the autopilot
    /// is known to be off and both flight directors are known to be off would be turned straight back
    /// off by the aircraft, with nothing to tell the pilot why.
    /// </summary>
    private string? AfcsRefusal(L1011PlacedRow row, double value, SimConnectManager sim) =>
        row.Action == L1011RowAction.Set && value >= 0.5
        && L1011AfcsModes.NeedFlightDirectorOrAutopilot.Contains(row.Key)
        && L1011AfcsModes.KnownNoDirectorOrAutopilot(key => AfcsPosition(key, sim))
            ? L1011AfcsModes.NeedsDirectorRefusal
            : null;

    /// <summary>A row's REPORTED position in its own words ("On", "Command"), or "" while it is not
    /// known: what the aircraft says (<see cref="ReportedPosition"/>), never what was just asked for.</summary>
    internal string PositionWord(string key, SimConnectManager sim) =>
        _rows.TryGetValue(key, out var row) && ReportedPosition(key, sim) is double v
        && row.Positions.TryGetValue(Math.Round(v), out var word)
            ? word
            : "";

    /// <summary>
    /// Moves a control to its other position (<see cref="L1011AutoflightWindows.ToggleTarget"/>)
    /// through its row's write path. Refused aloud while its position is unknown: a toggle needs to
    /// know which way to go. True when the write was sent; false when it was refused (the refusal has
    /// been spoken).
    /// </summary>
    public bool ToggleControl(string key, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.TryGetValue(key, out var row))
            return false;
        _sim = sim;
        if (AfcsPosition(key, sim) is not double now)
        {
            announcer.Announce($"{row.Name}: {L1011WritePlan.UnknownPositionRefusal}");
            return false;
        }
        double target = L1011AutoflightWindows.ToggleTarget(key, now);
        HandleUIVariableSet(key, target, GetVariables()[key], sim, announcer);
        return CurrentPosition(key, sim) == target;   // Execute records the target only when it sends
    }

    /// <summary>The keys a value box button or a toggle key is about to read back, so the general
    /// call-out stays quiet for them until the read-back has spoken: the result is heard once.</summary>
    private readonly HashSet<string> _readBackPending = new(StringComparer.Ordinal);

    /// <summary>A value box button or a toggle key: the toggle, then its result read back fresh.</summary>
    private void ToggleAndReadBack(string key, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!ToggleControl(key, sim, announcer))
            return;
        _readBackPending.Add(key);
        _ = ReadBackAsync(key, sim, announcer);
    }

    /// <summary>Reads a control fresh once the press has landed and speaks it in the announcements'
    /// words ("Heading on", "Autopilot A command"). Fresh, not the cache: the aircraft turns some
    /// presses straight back (thrust management without an autopilot in command), and the 1 Hz cache
    /// could still hold the old position. Queued, not interrupting, so it never cuts off the call-outs
    /// the press set off ("Localizer armed"); the key leaves <see cref="_readBackPending"/> on every
    /// path, so the general call-out speaks for it again.</summary>
    private async Task ReadBackAsync(string key, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        try
        {
            await ReadBackDelay(ToggleReadBackMs);
            if (_disposed)
                return;
            if (await ReadFresh(sim, key, BatchReadoutTimeoutMs) is double value && L1011AfcsModes.Words(key, value) is string words)
                announcer.Announce(words);
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"Read-back of {key} failed: {ex.Message}");
        }
        finally
        {
            _readBackPending.Remove(key);
        }
    }

    // =================================================================================
    // Value boxes (Ctrl+S, H, A, V; Ctrl+B shares the slot)
    // =================================================================================

    /// <summary>The open value box: every TriStar value box is one window type, which the tracked-window
    /// registry keys on, so a different box replaces it rather than re-showing it.</summary>
    private ValueInputForm? _valueBox;

    /// <summary>Shows a value box, closing an open one with another title first. Tracked, so an aircraft
    /// switch closes it: its writes would otherwise reach whatever aircraft is loaded next.</summary>
    private void ShowValueBox(string title, string parameter, string range, ScreenReaderAnnouncer announcer,
        Func<string, (bool, string)> validate, List<ToggleButtonDef> buttons, Action<string> onSet, Form parentForm)
    {
        if (L1011AutoflightWindows.ReplacesOpenBox(_valueBox is { IsDisposed: false } open ? open.Text : null, title))
            _valueBox!.Close();
        ShowTrackedWindow(
            () => _valueBox = new ValueInputForm(title, parameter, range, announcer, validate, buttons, onSet)
            {
                ShowCancelButton = false,
            },
            ShowOrActivate<ValueInputForm>(parentForm));
    }

    /// <summary>Ctrl+S, H, A or V: the typed value and its axis's mode buttons. Refused before it opens
    /// when the calculator path cannot land.</summary>
    private void ShowAfcsBox(string key, IReadOnlyList<L1011WindowButton> buttons, SimConnectManager sim,
        ScreenReaderAnnouncer announcer, Form parentForm)
    {
        var kind = L1011Afcs.ValueFor(key)!.Value;
        string name = L1011Afcs.Name(kind);
        if (!CanLand(sim))
        {
            announcer.AnnounceImmediate($"{name} unavailable");
            return;
        }
        string range = L1011Afcs.RangeText(kind);
        ShowValueBox($"Autopilot {name}", name.ToLowerInvariant(), range, announcer,
            input => L1011Afcs.Parse(kind, input) != null ? (true, "") : (false, $"{name}: enter {range}"),
            ValueBoxButtons(buttons, sim, announcer),
            input =>
            {
                if (!_disposed && L1011Afcs.Parse(kind, input) is int value)
                    SetAfcsValue(key, value, sim, announcer);
            },
            parentForm);
    }

    /// <summary>A value box's mode buttons, labelled with their panel names and showing their positions.
    /// A press toggles through the row's write path and reads the result back fresh, so the box's own
    /// echo, which reads the 1 Hz cache, is off.</summary>
    internal List<ToggleButtonDef> ValueBoxButtons(IReadOnlyList<L1011WindowButton> buttons, SimConnectManager sim,
        ScreenReaderAnnouncer announcer)
    {
        var defs = new List<ToggleButtonDef>();
        foreach (var button in buttons)
        {
            if (!_rows.TryGetValue(button.RowKey, out var row))
                continue;
            string key = button.RowKey;
            defs.Add(new ToggleButtonDef(PMDGAutopilotRowBinder.ApplyMnemonic(row.Name, button.Mnemonic),
                () => PositionWord(key, sim),
                () => ToggleAndReadBack(key, sim, announcer))
            {
                SuppressStateAnnounce = () => true,
            });
        }
        return defs;
    }

    // =================================================================================
    // Ctrl+P
    // =================================================================================

    /// <summary>The Coherent view the glareshield windows are drawn in.</summary>
    public const string AfcsViewNeedle = "L1011_AFCS";

    /// <summary>How often the reader scrapes the windows while Ctrl+P is shown.</summary>
    public const int AfcsReadIntervalMs = 500;

    private CoherentDisplayClient? _afcsReader;

    /// <summary>The glareshield windows as last read, or null while no reader has delivered.</summary>
    private List<string>? _afcsWindowRows;

    /// <summary>Ctrl+P: the shared autopilot window on a live status list.</summary>
    private void ShowAutopilotWindow(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        ShowTrackedWindow(() =>
        {
            var window = new PMDGAutopilotWindow("TriStar Autopilot", AutopilotButtons(sim, announcer),
                AutopilotSelectors(sim, announcer), () => AutopilotStatusLines(sim), "Autopilot status");
            // The reader holds the L1011_AFCS view's one inspector socket only while the window is
            // shown: the window hides on close and is reused, so it follows Visible, not the form's life.
            window.VisibleChanged += (_, _) => SetAfcsReader(window.Visible);
            window.Disposed += (_, _) => SetAfcsReader(false);
            return window;
        }, w => w.ShowForm());
    }

    /// <summary>Starts the glareshield reader, or stops it and forgets what it read.</summary>
    private void SetAfcsReader(bool on)
    {
        if (on && _afcsReader == null && !_disposed)
        {
            var reader = new CoherentDisplayClient(AfcsViewNeedle, AfcsReadIntervalMs, "coherent-l1011-afcs-agent.js");
            reader.RowsUpdated += rows => _afcsWindowRows = rows;
            _afcsReader = reader;
            reader.Start();
        }
        else if (!on && _afcsReader != null)
        {
            _afcsReader.Dispose();
            _afcsReader = null;
            _afcsWindowRows = null;
        }
    }

    /// <summary>Ctrl+P's status lines (<see cref="L1011AutopilotStatus"/>).</summary>
    internal IReadOnlyList<string> AutopilotStatusLines(SimConnectManager sim) =>
        L1011AutopilotStatus.Lines(_afcsWindowRows, key => ReportedPosition(key, sim));

    /// <summary>Ctrl+P's buttons (<see cref="L1011AutoflightWindows.AutopilotButtons"/>), each through its
    /// row's write path. Nothing is said when a press works: the labels update in place.</summary>
    internal List<ToggleButtonDef> AutopilotButtons(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var defs = new List<ToggleButtonDef>();
        foreach (var button in L1011AutoflightWindows.AutopilotButtons)
        {
            if (!_rows.TryGetValue(button.RowKey, out var row))
                continue;
            string key = button.RowKey;
            string label = PMDGAutopilotRowBinder.ApplyMnemonic(row.Name, button.Mnemonic);
            defs.Add(button.State == L1011ButtonState.Switch
                ? new ToggleButtonDef(label, () => PositionWord(key, sim), () => ToggleControl(key, sim, announcer))
                : new ToggleButtonDef(label, () => "", () => HandleUIVariableSet(key, 1, GetVariables()[key], sim, announcer)));
        }
        return defs;
    }

    /// <summary>Ctrl+P's combos: the two engage paddles, through their rows.</summary>
    internal List<SelectorRowDef> AutopilotSelectors(SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var defs = new List<SelectorRowDef>();
        foreach (var selector in L1011AutoflightWindows.AutopilotSelectors)
        {
            if (!_rows.TryGetValue(selector.RowKey, out var row))
                continue;
            string key = selector.RowKey;
            defs.Add(new SelectorRowDef(selector.Label, row.Positions, () => ReportedPosition(key, sim),
                v => HandleUIVariableSet(key, v, GetVariables()[key], sim, announcer), selector.Mnemonic));
        }
        return defs;
    }

    // =================================================================================
    // Hotkeys
    // =================================================================================

    /// <summary>The autopilot cases of <see cref="HandleHotkeyAction"/>; false for any other action.</summary>
    private bool HandleAutopilotHotkey(HotkeyAction action, SimConnectManager sim, ScreenReaderAnnouncer announcer,
        Form parentForm, HotkeyManager hotkeyManager)
    {
        switch (action)
        {
            // Input mode: the value boxes and the autopilot window.
            case HotkeyAction.FCUSetSpeed:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAfcsBox(L1011Afcs.SpeedKey, L1011AutoflightWindows.Speed, sim, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetHeading:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAfcsBox(L1011Afcs.HeadingKey, L1011AutoflightWindows.Heading, sim, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetAltitude:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAfcsBox(L1011Afcs.AltitudeKey, L1011AutoflightWindows.Altitude, sim, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetVS:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAfcsBox(L1011Afcs.VerticalSpeedKey, L1011AutoflightWindows.VerticalSpeed, sim, announcer, parentForm);
                return true;
            case HotkeyAction.FCUSetAutopilot:
                hotkeyManager.ExitInputHotkeyMode();
                ShowAutopilotWindow(sim, announcer);
                return true;

            // Input mode: toggles, each read back once it has landed.
            case HotkeyAction.ToggleAutopilot1:
                ToggleAndReadBack("SWITCH_AFCS_AP_A", sim, announcer);
                return true;
            case HotkeyAction.ToggleAutopilot2:
                ToggleAndReadBack("SWITCH_AFCS_AP_B", sim, announcer);
                return true;
            case HotkeyAction.ToggleAutothrust:
                ToggleAndReadBack("SWITCH_AFCS_AT", sim, announcer);
                return true;
            case HotkeyAction.ToggleLocalizer:
                ToggleAndReadBack("SWITCH_AFCS_LOC", sim, announcer);
                return true;
            case HotkeyAction.ToggleApproachMode:
                ToggleAndReadBack("SWITCH_AFCS_ILS", sim, announcer);
                return true;

            // Output mode: the targets, read fresh.
            case HotkeyAction.ReadHeading:
                _ = SpeakAsync(sim, announcer, "Selected heading",
                    v => "Selected heading " + L1011Afcs.Display(L1011AfcsValue.Heading, (int)Math.Round(v[0])), "L1011_RO_AFCS_HEADING");
                return true;
            case HotkeyAction.ReadSpeed:
                _ = SpeakAsync(sim, announcer, "Speed target",
                    v => $"Speed target {Math.Round(v[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)}", "L1011_RO_AFCS_SPEED");
                return true;
            case HotkeyAction.ReadAltitude:
                _ = SpeakAsync(sim, announcer, "Selected altitude",
                    v => $"Selected altitude {Math.Round(v[0]).ToString(System.Globalization.CultureInfo.InvariantCulture)}", "L1011_RO_AFCS_ALTITUDE");
                return true;
            case HotkeyAction.ReadFCUVerticalSpeedFPA:
                _ = SpeakAsync(sim, announcer, "Vertical speed target",
                    v => "Vertical speed target " + L1011Afcs.Display(L1011AfcsValue.VerticalSpeed, (int)Math.Round(v[0])), "L1011_RO_AFCS_VS");
                return true;
        }
        return false;
    }
}
