using System.Globalization;

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
