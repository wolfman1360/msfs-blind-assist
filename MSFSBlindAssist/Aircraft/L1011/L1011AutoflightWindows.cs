namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>Where an autoflight window button's state comes from.</summary>
public enum L1011ButtonState
{
    /// <summary>A one-shot press with no state: the disconnects, TO/GA.</summary>
    None,
    /// <summary>A two-position switch or mode button, shown by its own position in its row's words.</summary>
    Switch,
}

/// <summary>One button of an autoflight window: the panel row it presses, its Alt-key letter ('\0' for
/// none), and where its state comes from. Its label is the row's name.</summary>
public sealed record L1011WindowButton(string RowKey, char Mnemonic, L1011ButtonState State);

/// <summary>One combo of the autopilot window: a multi-position switch row.</summary>
public sealed record L1011WindowSelector(string RowKey, char Mnemonic, string Label);

/// <summary>
/// The TriStar's input-mode autoflight windows, the A300's pattern: the value boxes (Ctrl+S, H, A,
/// V) carry the mode buttons of their own axis, and Ctrl+P's window carries the engage cluster, the
/// radio navigation and approach modes, the disconnects and TO/GA. Each control appears in one window
/// only. Every label is the control's panel row name (<see cref="L1011PanelLayout"/>), so one control
/// has one name across the app. Pure.
/// </summary>
public static class L1011AutoflightWindows
{
    /// <summary>Ctrl+S: the AUTOTHROTTLE block, and the pitch block's two speed holds.</summary>
    public static readonly IReadOnlyList<L1011WindowButton> Speed = new[]
    {
        new L1011WindowButton("SWITCH_AFCS_AT", 'A', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_TM", 'T', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_IAS", 'I', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_MACH", 'M', L1011ButtonState.Switch),
    };

    /// <summary>Ctrl+H: the HDG button and INS, the lateral mode that follows the INS route.</summary>
    public static readonly IReadOnlyList<L1011WindowButton> Heading = new[]
    {
        new L1011WindowButton("SWITCH_AFCS_HDG", 'H', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_INS", 'N', L1011ButtonState.Switch),
    };

    /// <summary>Ctrl+A: altitude hold and VNAV.</summary>
    public static readonly IReadOnlyList<L1011WindowButton> Altitude = new[]
    {
        new L1011WindowButton("SWITCH_AFCS_ALT", 'A', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_VNAV", 'V', L1011ButtonState.Switch),
    };

    /// <summary>Ctrl+V: the vertical speed mode, which must be on before a vertical speed is typed.</summary>
    public static readonly IReadOnlyList<L1011WindowButton> VerticalSpeed = new[]
    {
        new L1011WindowButton("SWITCH_AFCS_VS", 'V', L1011ButtonState.Switch),
    };

    /// <summary>Ctrl+P's buttons: flight directors, turbulence, the radio navigation and approach
    /// modes, the disconnects and TO/GA.</summary>
    public static readonly IReadOnlyList<L1011WindowButton> AutopilotButtons = new[]
    {
        new L1011WindowButton("SWITCH_AFCS_FD_A", 'F', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_FD_B", 'R', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_TURB", 'U', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_ILS", 'I', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_LOC", 'L', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_VOR", 'V', L1011ButtonState.Switch),
        new L1011WindowButton("SWITCH_AFCS_BC", 'K', L1011ButtonState.Switch),
        new L1011WindowButton(L1011Afcs.DisconnectKey, 'D', L1011ButtonState.None),
        new L1011WindowButton("SWITCH_CPT_AT_DISCO", 'H', L1011ButtonState.None),
        new L1011WindowButton("YOKE_CPT_TOGA", 'G', L1011ButtonState.None),
    };

    /// <summary>Ctrl+P's combos: the two engage paddles, Command, CWS and Off.</summary>
    public static readonly IReadOnlyList<L1011WindowSelector> AutopilotSelectors = new[]
    {
        new L1011WindowSelector("SWITCH_AFCS_AP_A", 'A', "Autopilot A"),
        new L1011WindowSelector("SWITCH_AFCS_AP_B", 'B', "Autopilot B"),
    };

    /// <summary>Whether a key is an autopilot engage paddle (0 Command, 1 CWS, 2 Off).</summary>
    public static bool IsEngagePaddle(string key) => key is "SWITCH_AFCS_AP_A" or "SWITCH_AFCS_AP_B";

    /// <summary>Where a toggle key sends a control from its known position: an engage paddle goes from
    /// Off to Command and from Command or CWS to Off; a two-position switch flips.</summary>
    public static double ToggleTarget(string key, double now) =>
        IsEngagePaddle(key) ? (now >= 1.5 ? 0 : 2) : (now >= 0.5 ? 0 : 1);

    /// <summary>Whether the open value box must close before <paramref name="requestedTitle"/> opens.
    /// Every TriStar value box is one window type (ValueInputForm), and the tracked-window registry keys
    /// on the type, so without this a second hotkey re-showed the first box ([A300-17] on the A300).</summary>
    public static bool ReplacesOpenBox(string? openTitle, string requestedTitle) =>
        openTitle != null && !string.Equals(openTitle, requestedTitle, StringComparison.Ordinal);
}
