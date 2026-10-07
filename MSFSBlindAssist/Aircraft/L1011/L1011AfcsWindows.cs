using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The eight windows of the TriStar's glareshield autopilot panel as lines a screen reader and a
/// braille display read one at a time. The rows come from coherent-l1011-afcs-agent.js:
/// <c>power|on</c>, then <c>N|text</c> for <c>#screen_1</c> … <c>#screen_8</c> exactly as drawn.
///
/// What each window is (L1011_AFCS.js, package 1.0.8): 7 is the speed mode label (IAS, EPR, " M ",
/// AOA) and 1 its value — an EPR window shows the three digits after "1." and a Mach window the three
/// after the point; 8 is the pitch mode label (T/O, IAS, M, VS, ALT, or CAP while capturing an
/// altitude) and 2 its value — a sign for a vertical speed, and a Mach target as "0.82"; 3 is the
/// selected heading; 4 and 5 are the two course windows; 6 is the selected altitude, five digits. The
/// gauge pads blank digit positions with "X", which its font draws as nothing; those are dropped
/// here. Mode labels that read badly aloud are spelled out ("Takeoff", "Mach", "Capture"); a window
/// that shows nothing reads "blank".
/// </summary>
public static class L1011AfcsWindows
{
    public const string Unpowered = "Autopilot windows unpowered";
    public const string Blank = "blank";

    public static IReadOnlyList<string> Format(IReadOnlyList<string> rows)
    {
        var screens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows ?? Array.Empty<string>())
        {
            int bar = row.IndexOf('|');
            if (bar > 0)
                screens[row.Substring(0, bar)] = row.Substring(bar + 1);
        }
        if (screens.TryGetValue("power", out var power) && power == "off")
            return new[] { Unpowered };

        string Get(int n) => screens.TryGetValue(n.ToString(CultureInfo.InvariantCulture), out var t) ? Clean(t) : string.Empty;

        return new[]
        {
            "Speed: " + Speed(Get(7), Get(1)),
            "Pitch: " + Join(PitchLabel(Get(8)), Get(2)),
            "Heading: " + OrBlank(Get(3)),
            "Course 1: " + OrBlank(Get(4)),
            "Course 2: " + OrBlank(Get(5)),
            "Altitude: " + OrBlank(Altitude(Get(6))),
        };
    }

    /// <summary>Drops the font's blank-digit "X" and every space.</summary>
    public static string Clean(string raw) =>
        new string((raw ?? string.Empty).Where(c => c != 'X' && !char.IsWhiteSpace(c)).ToArray());

    private static string Speed(string label, string value)
    {
        if (value.Length == 0)
            return Join(label, value);
        return label switch
        {
            "EPR" => "EPR 1." + value,
            "M" => "Mach ." + value,
            _ => Join(label, value),
        };
    }

    private static string PitchLabel(string label) => label switch
    {
        "T/O" => "Takeoff",
        "M" => "Mach",
        "CAP" => "Capture",
        _ => label,
    };

    private static string Join(string label, string value) =>
        OrBlank(string.Join(" ", new[] { label, value }.Where(p => p.Length > 0)));

    private static string OrBlank(string text) => text.Length == 0 ? Blank : text;

    /// <summary>"35000" from "35000", "0" from "00000": the leading zeros are padding.</summary>
    private static string Altitude(string digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v)
            ? v.ToString(CultureInfo.InvariantCulture)
            : digits;
}
