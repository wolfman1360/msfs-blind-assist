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
/// L1011_AFCS.js and L1011_INS.js write (ANN_VOR_ACTIVE is the INS gauge's, and counts only while VOR or
/// back course is on: <see cref="FlagModeOn"/>). The gauge's own 0/50/100 annunciator variables (AFCS_CPT_*) are not used:
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

    /// <summary>
    /// Flags that mean what their words say only while one of their modes is on. L1011_INS.js sets
    /// ANN_VOR_ACTIVE whenever its radio routine receives a navaid with no localizer, ILS or back-course
    /// mode on (its VOR branch checks no switch), so it reads 1 at the gate with VOR mode off, and it is
    /// left set when the signal goes. It is the VOR or back-course capture only while one of those is on.
    /// </summary>
    private static readonly Dictionary<string, string[]> FlagModes = new(StringComparer.Ordinal)
    {
        ["ANN_VOR_ACTIVE"] = new[] { "SWITCH_AFCS_VOR", "SWITCH_AFCS_BC" },
    };

    public static bool IsGatedFlag(string key) => FlagModes.ContainsKey(key);

    /// <summary>The modes a gated flag waits for; none for any other key.</summary>
    public static IReadOnlyList<string> ModesOf(string key) =>
        FlagModes.TryGetValue(key, out var modes) ? modes : Array.Empty<string>();

    /// <summary>The gated flags a mode button decides.</summary>
    public static IEnumerable<string> FlagsGatedBy(string modeKey) =>
        FlagModes.Where(kv => kv.Value.Contains(modeKey, StringComparer.Ordinal)).Select(kv => kv.Key);

    /// <summary>
    /// Whether a flag's mode is on: always for a flag with no mode gate; for a gated one only while
    /// one of its modes is KNOWN on, so a position not read yet never claims a capture.
    /// </summary>
    public static bool FlagModeOn(string key, Func<string, double?> read) =>
        !FlagModes.TryGetValue(key, out var modes) || modes.Any(m => read(m) is double v && v >= 0.5);

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
/// recorded, never spoken, so a flight load or a reconnect does not read the panel out. A gated flag
/// (<see cref="L1011AfcsModes.IsGatedFlag"/>) speaks on its value as its modes see it: a VOR received
/// with VOR mode off is silent, and VOR mode coming on over one speaks the capture after the mode. Pure.
/// </summary>
public sealed class L1011AfcsAnnouncer
{
    private readonly Dictionary<string, double> _last = new(StringComparer.Ordinal);

    /// <summary>
    /// The words to speak for this delivery, in order: the key's own change, then any gated flag it
    /// decides. <paramref name="ownCommand"/>: MSFSBA itself commanded this control a moment ago (a
    /// dialog or window button), so its own change is recorded but not spoken (the button's label or
    /// the dialog already says it); a capture the press sets off is the aircraft's and still speaks.
    /// </summary>
    public IReadOnlyList<string> Observe(string key, double value, bool ownCommand = false)
    {
        if (!L1011AfcsModes.IsAnnounced(key))
            return Array.Empty<string>();
        bool gatedFlag = L1011AfcsModes.IsGatedFlag(key);
        string[] flags = gatedFlag ? new[] { key } : L1011AfcsModes.FlagsGatedBy(key).ToArray();
        double?[] before = flags.Select(Effective).ToArray();

        bool known = _last.TryGetValue(key, out double last);
        _last[key] = value;

        var spoken = new List<string>();
        if (!gatedFlag && known && !ownCommand && Math.Abs(last - value) >= 0.01
            && L1011AfcsModes.Words(key, value) is string words)
            spoken.Add(words);
        for (int i = 0; i < flags.Length; i++)
            if (before[i] is double was && Effective(flags[i]) is double now && Math.Abs(now - was) >= 0.01
                && L1011AfcsModes.Words(flags[i], now) is string flagWords)
                spoken.Add(flagWords);
        return spoken;
    }

    /// <summary>
    /// A gated flag's value as its modes see it: the flag while one of them is on, 0 while all are
    /// known off, null until the flag and enough of its modes have been seen.
    /// </summary>
    private double? Effective(string flag)
    {
        if (!_last.TryGetValue(flag, out double raw))
            return null;
        var modes = L1011AfcsModes.ModesOf(flag);
        if (modes.Any(m => _last.TryGetValue(m, out double v) && v >= 0.5))
            return raw;
        return modes.All(m => _last.ContainsKey(m)) ? 0 : null;
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
