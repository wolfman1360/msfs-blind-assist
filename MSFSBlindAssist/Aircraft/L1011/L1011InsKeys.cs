using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The INS and PMS keypads as map control ids, so every press is the cockpit's own click
/// (<see cref="L1011WritePlan.ForPress"/>). The INS has no letter keys: N, S, E and W are the 2, 8, 6
/// and 4 keys, L and R the 7 and 9 keys (L1011_INS.js; the manual's keypad). The PMS takes digits and
/// a space only. Pure.
/// </summary>
public static class L1011InsKeys
{
    public static string? ControlFor(int unit, char typed)
    {
        char c = char.ToUpperInvariant(typed);
        char? digit = c switch
        {
            >= '0' and <= '9' => c,
            'N' => '2',
            'S' => '8',
            'E' => '6',
            'W' => '4',
            'L' => '7',
            'R' => '9',
            _ => null,
        };
        return digit is char d ? $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_KEY_{d}" : null;
    }

    public static string Insert(int unit) => $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_KEY_INSERT";
    public static string Clear(int unit) => $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_KEY_CLEAR";
    public static string TrackChange(int unit) => $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_KEY_CHG";
    public static string Hold(int unit) => $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_KEY_HOLD";

    /// <summary>"N36051": hemisphere, two degree digits, two minute digits and a tenth, as the keypad takes it.</summary>
    public static string LatitudeKeys(double latitude) => Sexagesimal(latitude, 2, latitude < 0 ? 'S' : 'N');

    /// <summary>"W115080": hemisphere, three degree digits, two minute digits and a tenth.</summary>
    public static string LongitudeKeys(double longitude) => Sexagesimal(longitude, 3, longitude < 0 ? 'W' : 'E');

    /// <summary>
    /// The keys that enter a present position on the POS page: latitude, INSERT, longitude, INSERT
    /// (typed into INS 1 live and it aligned).
    /// </summary>
    public static IReadOnlyList<string> PresentPositionControls(int unit, double latitude, double longitude)
    {
        var keys = new List<string>();
        foreach (char c in LatitudeKeys(latitude))
            keys.Add(ControlFor(unit, c)!);
        keys.Add(Insert(unit));
        foreach (char c in LongitudeKeys(longitude))
            keys.Add(ControlFor(unit, c)!);
        keys.Add(Insert(unit));
        return keys;
    }

    /// <summary>
    /// The calculator string for automatic waypoint loading (the NAVIGATION screen's AUTO BATCH switch,
    /// L1011_NAVIGATION.js toggleAutoBatch): on sets <c>L:INS_n_AUTO_BATCH_ENABLED</c> and resumes the
    /// imported route (<c>H:INS_n_RESUME_AUTOBATCH</c>), off only clears the flag. The unit is formatted
    /// with the invariant culture.
    /// </summary>
    public static string AutoBatchRpn(int unit, bool on)
    {
        string n = unit.ToString(CultureInfo.InvariantCulture);
        return on
            ? $"1 (>L:INS_{n}_AUTO_BATCH_ENABLED) (>H:INS_{n}_RESUME_AUTOBATCH)"
            : $"0 (>L:INS_{n}_AUTO_BATCH_ENABLED)";
    }

    /// <summary>The longest calculator string a run of keys is sent as: the MobiFlight command channel holds
    /// 1,024 characters, terminator included, and <c>ExecuteCalculatorCodeUnique</c> adds a "&lt;seq&gt; 0 * " prefix
    /// (<see cref="L1011WritePlan.MaxRpnLength"/>, the same bound a control's own write is cut at).</summary>
    public const int MaxRunLength = L1011WritePlan.MaxRpnLength;

    /// <summary>The pause after each string of keys, so one string is never sent right on top of the next and
    /// the gauge has taken it before the next arrives.</summary>
    public const int RunGapMs = 50;

    /// <summary>
    /// One key's click as calculator code: the control's own press effects then its own release effects
    /// (<see cref="L1011WritePlan.ForPress"/>), in that order and with its hold dropped. The INS takes a run
    /// of keys in one string with each key's press and release back to back (measured live, 2026-10-15), so
    /// the hold that was once slept between them is not needed.
    /// </summary>
    public static string ClickRpn(L1011Plan press) =>
        string.Join(' ', press.Steps.OfType<L1011CalcStep>().Select(step => step.Rpn));

    /// <summary>
    /// A run of keys as the steps that send it: every key's click in order, as ONE calculator string, or as
    /// consecutive strings cut only BETWEEN keys when one would pass <see cref="MaxRunLength"/> (a single key
    /// longer than that goes alone, never cut), each followed by <see cref="RunGapMs"/>. Pure.
    /// </summary>
    public static IReadOnlyList<L1011Step> RunSteps(IReadOnlyList<string> clicks)
    {
        var steps = new List<L1011Step>();
        var current = new StringBuilder();
        void Flush()
        {
            if (current.Length == 0)
                return;
            steps.Add(new L1011CalcStep(current.ToString()));
            steps.Add(new L1011DelayStep(RunGapMs));
            current.Clear();
        }
        foreach (var click in clicks)
        {
            if (current.Length > 0 && current.Length + 1 + click.Length > MaxRunLength)
                Flush();
            if (current.Length > 0)
                current.Append(' ');
            current.Append(click);
        }
        Flush();
        return steps;
    }

    private static string Sexagesimal(double value, int degreeDigits, char hemisphere)
    {
        double abs = Math.Abs(value);
        int degrees = (int)Math.Floor(abs);
        long tenths = (long)Math.Round((abs - degrees) * 600, MidpointRounding.AwayFromZero);
        if (tenths >= 600)
        {
            degrees++;
            tenths -= 600;
        }
        return hemisphere
            + degrees.ToString(new string('0', degreeDigits), CultureInfo.InvariantCulture)
            + (tenths / 10).ToString("00", CultureInfo.InvariantCulture)
            + (tenths % 10).ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A PMS key for a typed character ("CPT" or "FO"): a digit, or SP for a space.</summary>
    public static string? PmsControlFor(string seat, char typed) => typed switch
    {
        >= '0' and <= '9' => $"PMS_{seat}_{typed}",
        ' ' => $"PMS_{seat}_SP",
        _ => null,
    };

    /// <summary>A named PMS key: INSERT, CLR, FWD, BACK or PERF_MGT.</summary>
    public static string PmsKey(string seat, string key) => $"PMS_{seat}_{key}";

    public static string PmsLineKey(string seat, int line) => $"PMS_{seat}_LSK_{line.ToString(CultureInfo.InvariantCulture)}";
}
