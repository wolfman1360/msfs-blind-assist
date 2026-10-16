using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// One INS unit's display as coherent-l1011-ins-agent.js read it.
/// </summary>
/// <param name="Unit">1, 2 or 3.</param>
/// <param name="Powered"><c>#customElectricity@state</c> is "on".</param>
/// <param name="Mode">The display selector, <c>L:INS_n_ROTARY_MODE</c> (0 to 8).</param>
/// <param name="Wheel">The waypoint thumbwheel, <c>L:INS_n_ROTARY_WPT</c> (0 to 9).</param>
/// <param name="Left">The six left-window characters exactly as the gauge's spans hold them (blank = space).</param>
/// <param name="Right">The seven right-window characters.</param>
/// <param name="FromTo">The two FROM/TO digits.</param>
/// <param name="Dots">The lit dots, 1 to 6 (5 = left decimal point, 6 = right decimal point).</param>
/// <param name="Status">The alignment status, <c>L:INS_n_ALIGNMENT_STATUS</c>, or null if unknown.</param>
/// <param name="Entry">"left" or "right" while a position or offset entry is open, "leg" during a track-change entry, else "".</param>
/// <param name="Lights">Lit lamps: READY, ALERT, WARN, BAT, HOLD, INSERT, TKCHG, TEST.</param>
/// <param name="Msu">The overhead mode selector as the aircraft REPORTS it (<c>L:ROTARY_INS_MODE_n</c>: 0 Off,
/// 1 Standby, 2 Align, 3 Nav, 4 Attitude), or null when unknown. Not in the INS view: the reader leaves it
/// null and the INS window fills it from the Overhead row ([L1011-11]).</param>
public sealed record L1011InsScreen(int Unit, bool Powered, int Mode, int Wheel, string Left, string Right, string FromTo,
    IReadOnlySet<int> Dots, double? Status, string Entry, IReadOnlyList<string> Lights, int? Msu = null);

/// <summary>
/// The INS display as lines a screen reader and a braille display read one at a time: the unit's state,
/// the overhead mode selector, the display selector, the two windows as labelled values, the leg and the
/// lit lamps. How each window is drawn comes from L1011_INS.js (package 1.0.8) and was confirmed live: the
/// left decimal point sits before the left window's fifth slot, the right one before the right window's
/// sixth; positions are DDMMt plus a hemisphere letter; "8888" and "----" are the gauge's "no data" fills.
/// The aircraft labels the first page TK/GS although it shows the true heading there; the line keeps the
/// cockpit's label. In Attitude the unit is on but its display is dark, so an unpowered screen whose MSU
/// reports Attitude is "attitude mode", not "off". Pure: numbers are invariant culture.
/// </summary>
public static class L1011InsDisplay
{
    private static readonly string[] ModeNames =
    {
        "track and ground speed", "heading and drift angle", "cross track and track error", "position", "waypoint",
        "distance and time", "wind", "desired track and status", "test",
    };

    /// <summary>The overhead mode selector's positions (the manual's OFF, STBY, ALIGN, NAV, ATT).</summary>
    private static readonly string[] MsuNames = { "off", "standby", "align", "nav", "attitude" };

    private const int MsuAttitude = 4;

    private static readonly Dictionary<string, string> LightWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["READY"] = "ready to navigate", ["ALERT"] = "alert", ["WARN"] = "warning", ["BAT"] = "battery",
        ["HOLD"] = "hold", ["INSERT"] = "insert", ["TKCHG"] = "track change", ["TEST"] = "test",
    };

    private static readonly Regex Lat = new(@"^(\d{2})(\d{2})(\d)([NS])$", RegexOptions.CultureInvariant);
    private static readonly Regex Lon = new(@"^(\d{3})(\d{2})(\d)([EW])$", RegexOptions.CultureInvariant);

    public static string ModeName(int mode) => mode >= 0 && mode < ModeNames.Length ? ModeNames[mode] : "unknown";

    /// <summary>"off", "standby", "align", "nav", "attitude"; "unknown" for null or a value the MSU never reports.</summary>
    public static string MsuName(int? msu) => msu is int m && m >= 0 && m < MsuNames.Length ? MsuNames[m] : "unknown";

    /// <summary>How far above a whole number a status may sit and still be that number. The countdown lands on
    /// 2.0000000000000053 for about a second before it is exactly 2 (measured live with a 40 ms sampler, 2026-10-16),
    /// and the app's variable cache ignores a change of 0.001 or less, so the definition never receives the exact 2.</summary>
    public const double StatusTolerance = 1e-6;

    /// <summary>The status as the gauge shows it: the whole number the countdown is rounded up to, a hair above one
    /// (<see cref="StatusTolerance"/>) counting as that number. 16.666... is still 17.</summary>
    public static int ShownStatus(double status) => (int)Math.Ceiling(status - StatusTolerance);

    /// <summary>"navigating", "aligned, ready to navigate", "aligning, status 45", "not aligned".</summary>
    public static string StateText(double status)
    {
        int shown = ShownStatus(status);
        return shown switch
        {
            1 => "navigating",
            2 => "aligned, ready to navigate",
            >= 90 => "not aligned",
            _ => $"aligning, status {shown.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    public static IReadOnlyList<string> Lines(L1011InsScreen s)
    {
        string unit = "INS " + s.Unit.ToString(CultureInfo.InvariantCulture);
        if (!s.Powered)
            return new[] { unit + (s.Msu == MsuAttitude ? ": attitude mode" : ": off") };

        // Fixed places: [0] state, [1] mode selector, [2] display selector, [3] the first value or the open
        // entry (L1011InsDevices.EntryLine reads [3]).
        var lines = new List<string>
        {
            s.Status is double st ? $"{unit}: {StateText(st)}" : unit,
            "Mode selector: " + MsuName(s.Msu),
            "Display selector: " + ModeName(s.Mode),
        };
        if (s.Entry == "left" || s.Entry == "right")
        {
            string window = s.Entry == "left" ? s.Left : s.Right;
            bool offset = s.Mode == 2 && s.Entry == "left" && s.Dots.Contains(5);
            lines.Add($"Entry, {s.Entry} window: {(offset ? OffsetEntry(window) : Collapse(window))}");
        }
        else if (s.Entry == "leg")
            lines.Add("Entry, leg: " + Collapse(s.FromTo));
        else
            lines.AddRange(ModeLines(s));

        string fromTo = Collapse(s.FromTo);
        if (s.Entry != "leg" && fromTo.Length == 2)
            lines.Add(fromTo == "88" ? "Leg: test pattern" : $"Leg: {fromTo[0]} to {fromTo[1]}");

        var lit = s.Lights.Select(l => LightWords.TryGetValue(l.Trim(), out var w) ? w : null).Where(w => w != null).ToList();
        lines.Add("Lights: " + (lit.Count == 0 ? "none" : string.Join(", ", lit)));
        return lines;
    }

    private static IEnumerable<string> ModeLines(L1011InsScreen s)
    {
        bool leftDot = s.Dots.Contains(5), rightDot = s.Dots.Contains(6);
        switch (s.Mode)
        {
            case 0:
                yield return "Track: " + Angle(s.Left, leftDot);
                yield return "Ground speed: " + Whole(s.Right, "knots");
                break;
            case 1:
                yield return "Heading: " + Angle(s.Left, leftDot);
                yield return "Drift angle: " + Signed(s.Right, "degrees");
                break;
            case 2:
                yield return "Cross track: " + Signed(s.Left, "miles");
                yield return "Track error: " + Signed(s.Right, "degrees");
                break;
            case 3:
                yield return "Present position: " + PositionText(s.Left, s.Right);
                break;
            case 4:
            {
                string label = s.Wheel == 0
                    ? "Waypoint 0, present position"
                    : "Waypoint " + s.Wheel.ToString(CultureInfo.InvariantCulture);
                string value = Latitude(s.Left) == null && Longitude(s.Right) == null && s.Wheel != 0
                    ? "not set"
                    : PositionText(s.Left, s.Right);
                yield return $"{label}: {value}";
                break;
            }
            case 5:
                yield return "Distance: " + Whole(s.Left, "miles");
                yield return "Time: " + Tenths(s.Right, "minutes");
                break;
            case 6:
            {
                string? dir = NumberText(s.Left), speed = NumberText(s.Right);
                yield return dir != null && speed != null
                    ? $"Wind: {dir} degrees at {int.Parse(speed, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)} knots"
                    : "Wind: no data";
                break;
            }
            case 7:
            {
                yield return "Desired track: " + Angle(s.Left, leftDot);
                string? status = NumberText(s.Right);
                yield return status != null
                    ? $"Status: {status}, {StatusWords(int.Parse(status, CultureInfo.InvariantCulture))}"
                    : "Status: no data";
                break;
            }
            case 8:
                yield return "Test: every segment lit";
                break;
        }
    }

    private static string StatusWords(int status) => status switch
    {
        1 => "navigating",
        2 => "aligned",
        >= 90 => "not aligned",
        _ => "aligning",
    };

    /// <summary>"N 36 05.1" from "36051N", or null when the window holds no latitude (or holds 00000N).</summary>
    public static string? Latitude(string window)
    {
        var m = Lat.Match(Collapse(window));
        if (!m.Success || m.Value == "00000N" || m.Value == "00000S")
            return null;
        return $"{m.Groups[4].Value} {m.Groups[1].Value} {m.Groups[2].Value}.{m.Groups[3].Value}";
    }

    /// <summary>"W 115 08.0" from "115080W", or null when the window holds no longitude.</summary>
    public static string? Longitude(string window)
    {
        var m = Lon.Match(Collapse(window));
        if (!m.Success || m.Value == "000000E" || m.Value == "000000W")
            return null;
        return $"{m.Groups[4].Value} {m.Groups[1].Value} {m.Groups[2].Value}.{m.Groups[3].Value}";
    }

    private static string PositionText(string left, string right)
    {
        string? lat = Latitude(left), lon = Longitude(right);
        if (lat == null && lon == null)
            return "not entered";
        return $"{lat ?? "latitude not entered"}, {lon ?? "longitude not entered"}";
    }

    /// <summary>A heading or track: three digits and a tenth, "087.4".</summary>
    private static string Angle(string window, bool dot)
    {
        string? d = NumberText(window);
        if (d == null || d == "8888")
            return "no data";
        return dot && d.Length > 1 ? d[..^1] + "." + d[^1] : d;
    }

    /// <summary>A whole number with its unit, leading zeros dropped: "123 knots".</summary>
    private static string Whole(string window, string unit)
    {
        string? d = NumberText(window);
        if (d == null || d == "8888")
            return "no data";
        return $"{long.Parse(d, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)} {unit}";
    }

    /// <summary>A number whose last digit is a tenth: "12.4 minutes".</summary>
    private static string Tenths(string window, string unit)
    {
        string? d = NumberText(window);
        if (d == null || d == "8888")
            return "no data";
        double v = long.Parse(d, CultureInfo.InvariantCulture) / 10.0;
        return $"{v.ToString("0.0", CultureInfo.InvariantCulture)} {unit}";
    }

    /// <summary>A side letter (R or L) then a number with a tenth: "right 12.3 miles". Blanks after the letter
    /// are the gauge's padding ("R 0123 "); blanks between the digits make it no number.</summary>
    private static string Signed(string window, string unit)
    {
        string w = (window ?? string.Empty).TrimStart();
        if (w.Length < 2 || (w[0] != 'R' && w[0] != 'L'))
            return "no data";
        string side = w[0] == 'R' ? "right" : "left";
        string? rest = NumberText(w[1..]);
        if (rest == null || rest == "8888")
            return "no data";
        return $"{side} {Tenths(rest, unit)}";
    }

    /// <summary>
    /// The digits of a window that holds ONE number, or null. Blanks before and after the digits are the
    /// gauge's padding and are dropped; a blank BETWEEN digits means the slots are not one number, which is
    /// what a freshly powered unit shows before its display selector is turned: the gauge's power-up text
    /// "00000N" and "072  0 " (live, 2026-10-16), which collapsed read as 720 knots, 72.0 minutes or a status
    /// of 0720. The "8888" and "----" fills are the caller's.
    /// </summary>
    private static string? NumberText(string window)
    {
        string trimmed = (window ?? string.Empty).Trim();
        return trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit) ? trimmed : null;
    }

    /// <summary>
    /// An open cross-track (parallel offset) entry as the cockpit shows it: the side letter, then the miles
    /// with the decimal point <c>#dot_5</c> draws before the left window's fifth slot. The gauge shows
    /// "R  50 " for the manual's right 5 nm offset (keyed 1, 9, 5, 0), which reads "R 5.0"; the digits in
    /// slots 2 to 4 are whole miles, those after the dot the tenth.
    /// </summary>
    private static string OffsetEntry(string window)
    {
        string w = (window ?? string.Empty).PadRight(6);
        string side = w[0] == 'R' || w[0] == 'L' ? w[0].ToString() : string.Empty;
        string whole = Collapse(w.Substring(side.Length, 4 - side.Length));
        string tenths = Collapse(w.Substring(4));
        string miles = tenths.Length == 0 ? whole : $"{(whole.Length == 0 ? "0" : whole)}.{tenths}";
        return string.Join(" ", new[] { side, miles }.Where(p => p.Length > 0));
    }

    /// <summary>The window without its blank slots.</summary>
    private static string Collapse(string window) => new string((window ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray());
}
