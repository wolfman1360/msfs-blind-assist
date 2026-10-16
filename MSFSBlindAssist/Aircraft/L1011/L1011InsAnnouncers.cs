using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// Says "INS 1 navigating" when a unit starts navigating by itself: its mode selector was already in NAV
/// while it aligned, so it went from aligning to navigating on its own (L1011_INS.js; live: the status rests
/// at 2 for one update, about a second, and READY NAV lights for that one update, below the lamp gate's
/// settle time, so the lamp is not spoken; then the status goes to 1). With the selector in ALIGN the READY
/// NAV light stays on and is the announcement (<see cref="L1011Annunciators"/>), and the step to navigating
/// is the pilot turning the knob to NAV, so it stays silent. Status: <c>L:INS_n_ALIGNMENT_STATUS</c>, 90 not
/// aligned, falling while aligning, 2 aligned, 1 navigating. Baseline-first per unit. Pure.
/// </summary>
public sealed class L1011InsAlignmentAnnouncer
{
    private enum State { Aligning, Aligned, Navigating }

    private const double MsuNav = 3;
    private static readonly Regex StatusPattern = new(@"^INS_([1-3])_ALIGNMENT_STATUS$", RegexOptions.CultureInvariant);

    private readonly Dictionary<int, State> _last = new();

    public static string StatusKey(int unit) => $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_ALIGNMENT_STATUS";

    /// <summary>The unit's mode selector (overhead MSU), a panel row: 0 OFF, 1 STBY, 2 ALIGN, 3 NAV, 4 ATT.</summary>
    public static string MsuKey(int unit) => $"ROTARY_INS_MODE_{unit.ToString(CultureInfo.InvariantCulture)}";

    public static int? UnitOf(string key)
    {
        var m = StatusPattern.Match(key);
        return m.Success ? m.Groups[1].Value[0] - '0' : null;
    }

    public string? Observe(int unit, double status, double? msu)
    {
        if (Classify(status, msu) is not State now)
            return null;   // not a status the gauge writes: keep what was known
        bool known = _last.TryGetValue(unit, out var before);
        _last[unit] = now;
        if (known && now == State.Navigating && before == State.Aligning)
            return $"INS {unit.ToString(CultureInfo.InvariantCulture)} navigating";
        return null;
    }

    /// <summary>A baseline from the cache after a context reset; true when this unit had none.</summary>
    public bool Seed(int unit, double status, double? msu) =>
        Classify(status, msu) is State now && _last.TryAdd(unit, now);

    public void Reset() => _last.Clear();

    private static State? Classify(double status, double? msu)
    {
        int shown = (int)Math.Ceiling(status);
        return shown switch
        {
            1 => State.Navigating,
            2 => msu == MsuNav ? State.Aligning : State.Aligned,   // in NAV a 2 is the one update before 1
            >= 3 => State.Aligning,                               // aligning, or not aligned (90)
            _ => null,
        };
    }
}

/// <summary>
/// Says "PMS climb", "PMS cruise" or "PMS descent" when the PMS changes mode by itself (the aircraft
/// detects the flight phase and presses PERF MGT for the crew). The words follow the PMS mode lights,
/// which light for phases 2 and 3 (CLIMB), 4 (CRUISE) and 5 (DESCENT) of <c>L:PMS_FLIGHT_PHASE</c>
/// (L1011_INS.js); a phase that lights none (the ground, a go-around) is silent. Baseline-first. Pure.
/// </summary>
public sealed class L1011PmsPhaseAnnouncer
{
    public const string Key = "PMS_FLIGHT_PHASE";

    private bool _known;
    private string? _last;

    public static string? Mode(double phase) => (int)Math.Round(phase) switch
    {
        2 or 3 => "climb",
        4 => "cruise",
        5 => "descent",
        _ => null,
    };

    public string? Observe(double phase)
    {
        string? mode = Mode(phase);
        bool speak = _known && mode != null && mode != _last;
        _known = true;
        _last = mode;
        return speak ? "PMS " + mode : null;
    }

    public bool Seed(double phase)
    {
        if (_known)
            return false;
        _known = true;
        _last = Mode(phase);
        return true;
    }

    public void Reset()
    {
        _known = false;
        _last = null;
    }
}
