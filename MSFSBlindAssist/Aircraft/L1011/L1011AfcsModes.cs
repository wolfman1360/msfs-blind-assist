namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One autopilot state variable and the words spoken when it changes.</summary>
/// <param name="Key">The variable key: a map control id (the mode buttons and engage switches) or an
/// L:var the AFCS gauge drives.</param>
/// <param name="Name">The spoken name.</param>
/// <param name="Words">Value → what is said; a value with no entry is recorded silently.</param>
public sealed record L1011AfcsState(string Key, string Name, IReadOnlyDictionary<double, string> Words);

/// <summary>
/// The autopilot states the TriStar announces, all read from the variables the aircraft's own gauges
/// use (package 1.0.8): the engage switches, the mode buttons, and the armed/captured flags that
/// L1011_AFCS.js and L1011_INS.js write (ANN_VOR_ACTIVE is the INS gauge's, in VOR and back course). The gauge's own 0/50/100 annunciator variables (AFCS_CPT_*) are not used:
/// which legend each value shows is a texture detail the code does not name.
/// </summary>
public static class L1011AfcsModes
{
    private static IReadOnlyDictionary<double, string> OnOff(string name) =>
        new Dictionary<double, string> { [0] = name + " off", [1] = name + " on" };

    private static IReadOnlyDictionary<double, string> Engage(string name) =>
        new Dictionary<double, string> { [0] = name + " command", [1] = name + " CWS", [2] = name + " off" };

    private static IReadOnlyDictionary<double, string> When(string words) =>
        new Dictionary<double, string> { [1] = words };

    /// <summary>The mode buttons, in panel order, with the names the window and the guide use.</summary>
    public static IReadOnlyList<(string Key, string Name)> ModeButtons { get; } = new[]
    {
        ("SWITCH_AFCS_IAS", "IAS hold"),
        ("SWITCH_AFCS_MACH", "Mach hold"),
        ("SWITCH_AFCS_VNAV", "VNAV"),
        ("SWITCH_AFCS_VS", "Vertical speed"),
        ("SWITCH_AFCS_ALT", "Altitude hold"),
        ("SWITCH_AFCS_HDG", "Heading"),
        ("SWITCH_AFCS_INS", "INS"),
        ("SWITCH_AFCS_VOR", "VOR"),
        ("SWITCH_AFCS_LOC", "Localizer"),
        ("SWITCH_AFCS_ILS", "ILS"),
        ("SWITCH_AFCS_BC", "Back course"),
        ("SWITCH_AFCS_TURB", "Turbulence"),
    };

    /// <summary>Everything announced, in the order a change is spoken when several land at once.</summary>
    public static IReadOnlyList<L1011AfcsState> All { get; } = Build();

    private static IReadOnlyList<L1011AfcsState> Build()
    {
        var list = new List<L1011AfcsState>
        {
            new("SWITCH_AFCS_AP_A", "Autopilot A", Engage("Autopilot A")),
            new("SWITCH_AFCS_AP_B", "Autopilot B", Engage("Autopilot B")),
            new("SWITCH_AFCS_FD_A", "Flight director A", OnOff("Flight director A")),
            new("SWITCH_AFCS_FD_B", "Flight director B", OnOff("Flight director B")),
            new("SWITCH_AFCS_AT", "Autothrottle", OnOff("Autothrottle")),
            new("SWITCH_AFCS_TM", "Thrust management", OnOff("Thrust management")),
        };
        foreach (var (key, name) in ModeButtons)
            list.Add(new(key, name, OnOff(name)));
        list.Add(new("ANN_LOC_ARM", "Localizer armed", When("Localizer armed")));
        list.Add(new("ANN_LOC_ACTIVE", "Localizer captured", When("Localizer captured")));
        list.Add(new("ANN_VOR_ACTIVE", "VOR captured", When("VOR captured")));
        list.Add(new("ANN_ILS_ARM", "Glideslope armed", When("Glideslope armed")));
        list.Add(new("ANN_ILS_ACTIVE", "Glideslope captured", When("Glideslope captured")));
        list.Add(new("ANN_ALT_ARM", "Altitude armed", When("Altitude armed")));
        list.Add(new("FLARE_ACTIVE", "Flare", When("Flare")));
        list.Add(new("ROLLOUT_ACTIVE", "Rollout", When("Rollout")));
        return list;
    }

    /// <summary>The armed/captured flags: L:vars the gauge drives, not panel controls.</summary>
    public static IReadOnlyList<L1011AfcsState> Flags { get; } =
        All.Where(s => !s.Key.StartsWith("SWITCH_AFCS_", StringComparison.Ordinal)).ToList();

    private static readonly Dictionary<string, L1011AfcsState> ByKey = All.ToDictionary(s => s.Key, StringComparer.Ordinal);

    public static bool IsAnnounced(string key) => ByKey.ContainsKey(key);

    /// <summary>The words for a value, or null when the change is recorded silently.</summary>
    public static string? Words(string key, double value) =>
        ByKey.TryGetValue(key, out var s) && s.Words.TryGetValue(Math.Round(value), out var w) ? w : null;

    /// <summary>
    /// The pitch and lateral mode buttons L1011_INS.js turns straight back off when the autopilot is
    /// not engaged and both flight directors are off (each case starts with that check; seen live at
    /// the gate on 2026-10-03). Turbulence, autothrottle and thrust management have no such check.
    /// </summary>
    public static IReadOnlySet<string> NeedFlightDirectorOrAutopilot { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "SWITCH_AFCS_IAS", "SWITCH_AFCS_MACH", "SWITCH_AFCS_VNAV", "SWITCH_AFCS_VS", "SWITCH_AFCS_ALT",
        "SWITCH_AFCS_HDG", "SWITCH_AFCS_INS", "SWITCH_AFCS_VOR", "SWITCH_AFCS_LOC", "SWITCH_AFCS_ILS", "SWITCH_AFCS_BC",
    };

    public const string NeedsDirectorRefusal = "turn on a flight director or the autopilot first";

    /// <summary>
    /// True only when it is KNOWN that both flight directors are off and both autopilot engage
    /// switches are at Off (2): a position not known yet is never a reason to refuse a press.
    /// </summary>
    public static bool KnownNoDirectorOrAutopilot(Func<string, double?> read)
    {
        static bool Off(double? v, double offValue) => v is double d && Math.Abs(d - offValue) < 0.5;
        return Off(read("SWITCH_AFCS_FD_A"), 0) && Off(read("SWITCH_AFCS_FD_B"), 0)
            && Off(read("SWITCH_AFCS_AP_A"), 2) && Off(read("SWITCH_AFCS_AP_B"), 2);
    }

    /// <summary>The engaged mode buttons, by name ("Heading, Altitude hold"), or "none".</summary>
    public static string Engaged(Func<string, double?> read)
    {
        var on = ModeButtons.Where(m => read(m.Key) is double v && v >= 0.5).Select(m => m.Name).ToList();
        return on.Count == 0 ? "none" : string.Join(", ", on);
    }
}

/// <summary>
/// Speaks an autopilot state when it changes. BASELINE FIRST: the first value after a reset is
/// recorded, never spoken, so a flight load or a reconnect does not read the panel out. Pure.
/// </summary>
public sealed class L1011AfcsAnnouncer
{
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>
    /// The words to speak for this delivery, or null. <paramref name="ownCommand"/>: MSFSBA itself
    /// commanded this control a moment ago (a dialog or window button), so the change is recorded
    /// but not spoken — the button's label or the dialog already says it.
    /// </summary>
    public string? Observe(string key, double value, bool ownCommand = false)
    {
        if (!L1011AfcsModes.IsAnnounced(key))
            return null;
        bool known = _last.TryGetValue(key, out double last);
        _last[key] = value;
        if (!known || ownCommand || Math.Abs(last - value) < 0.01)
            return null;
        return L1011AfcsModes.Words(key, value);
    }

    /// <summary>The context-reset seed: a baseline for a state a flight load did not re-deliver.</summary>
    public bool Seed(string key, double value)
    {
        if (!L1011AfcsModes.IsAnnounced(key) || _last.ContainsKey(key))
            return false;
        _last[key] = value;
        return true;
    }

    /// <summary>The last value seen for a key since the last reset, or null.</summary>
    public double? Last(string key) => _last.TryGetValue(key, out var v) ? v : null;

    public void Reset() => _last.Clear();
}
