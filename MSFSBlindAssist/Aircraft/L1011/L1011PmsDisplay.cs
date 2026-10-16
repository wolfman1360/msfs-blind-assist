using System.Globalization;
using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One PMS unit's page as coherent-l1011-ins-agent.js read it.</summary>
/// <param name="Seat">"Captain" or "First officer".</param>
/// <param name="Title"><c>#pms_title</c>.</param>
/// <param name="Left">The six left cells, <c>#pms_left_1..6</c>, exactly as drawn.</param>
/// <param name="Right">The six right cells, <c>#pms_right_1..6</c>.</param>
/// <param name="EntryOpen">An entry is open (the page global <c>pms_is_input</c>).</param>
/// <param name="ActiveLine">The line the entry is on, 1 to 6 (<c>pms_line_active</c>), or 0.</param>
/// <param name="Lights">The unit's lit lamps by L:var suffix: CLIMB, CLIMB_1, CLIMB_2, CRUISE, DESCENT
/// (<c>L:INI_&lt;CPT|FO&gt;_PMS_*</c>) and INSERT (<c>L:PMS_n_INSERT_LIGHT</c>).</param>
public sealed record L1011PmsScreen(string Seat, string Title, IReadOnlyList<string> Left, IReadOnlyList<string> Right,
    bool EntryOpen, int ActiveLine, IReadOnlyList<string>? Lights = null);

/// <summary>
/// A PMS page as lines: "Captain PMS, page: TAKEOFF", then one line per row, numbered so the line
/// select key is obvious ("1: GMT 0755Z"), then the lit lamps ("Lights: climb, insert"). The gauge
/// pads with "_" and writes "---" for a value not yet known (L1011_INS.js, package 1.0.8): runs of "_"
/// read as one space, "---" reads "blank". A page the gauge is redrawing (it blanks everything for
/// 200 ms) is null, so the window keeps the page it has.
/// </summary>
public static class L1011PmsDisplay
{
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, string> LightWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["CLIMB"] = "climb", ["CLIMB_1"] = "climb 1", ["CLIMB_2"] = "climb 2", ["CRUISE"] = "cruise",
        ["DESCENT"] = "descent", ["INSERT"] = "insert",
    };

    public static IReadOnlyList<string>? Lines(L1011PmsScreen s)
    {
        string title = Clean(s.Title);
        bool anyCell = s.Left.Concat(s.Right).Any(c => Clean(c).Length > 0);
        if (title.Length == 0 && !anyCell)
            return null;

        var lines = new List<string> { $"{s.Seat} PMS, page: {(title.Length == 0 ? "no title" : title)}" };
        for (int i = 0; i < 6; i++)
        {
            string left = Clean(At(s.Left, i)), rawRight = At(s.Right, i), right = Clean(rawRight);
            string n = (i + 1).ToString(CultureInfo.InvariantCulture);
            if (s.EntryOpen && s.ActiveLine == i + 1)
            {
                if (rawRight.Length == 0)
                {
                    lines.Add($"{n}: {left}, entering");
                }
                else
                {
                    string typed = rawRight.Replace("_", string.Empty).Trim();
                    bool nothing = typed.Length == 0 || typed == "Z";
                    lines.Add($"{n}: {left}, entering: {(nothing ? "nothing typed yet" : right)}");
                }
                continue;
            }
            var parts = new[] { left, right }.Where(p => p.Length > 0).ToList();
            lines.Add($"{n}: {(parts.Count == 0 ? "blank" : string.Join(" ", parts))}");
        }
        var lit = (s.Lights ?? Array.Empty<string>())
            .Select(l => LightWords.TryGetValue(l.Trim(), out var w) ? w : null).Where(w => w != null).ToList();
        lines.Add("Lights: " + (lit.Count == 0 ? "none" : string.Join(", ", lit)));
        return lines;
    }

    /// <summary>"_" padding as one space, surrounding space trimmed, "---" read as "blank".</summary>
    public static string Clean(string? cell)
    {
        string s = Spaces.Replace((cell ?? string.Empty).Replace('_', ' '), " ").Trim();
        return s == "---" ? "blank" : s;
    }

    private static string At(IReadOnlyList<string> cells, int i) => i < cells.Count ? cells[i] ?? string.Empty : string.Empty;
}

/// <summary>One of an INS unit's nine waypoint slots, as the gauge holds it (<c>gpsPoints[k]</c>):
/// ident (the fix's first five letters for an imported route, USER&lt;k&gt; for one typed in, or empty)
/// and decimal degrees; 0, 0 is an empty slot.</summary>
public sealed record L1011InsWaypoint(string Ident, double Latitude, double Longitude);

/// <summary>
/// An INS unit's waypoints 1 to 9 as lines ("Waypoint 1, KENNO: N 37 17.9, W 117 18.6", "Waypoint 3:
/// not set"), then "Route: batch 1 of 2" while an imported route is loaded (and a slot is set). Read from the slots the gauge
/// navigates by rather than the DataStore table it rewrites once a second only while powered (empty
/// until then, live 2026-10-03). Positions use the keypad's own rounding (<see cref="L1011InsKeys"/>), so
/// the list, the INS window and a typed entry agree to the tenth of a minute. Pure, invariant culture.
/// </summary>
public static class L1011InsWaypoints
{
    private static readonly Regex TypedIdent = new(@"^USER\d$", RegexOptions.CultureInvariant);

    public static IReadOnlyList<string> Lines(IReadOnlyList<L1011InsWaypoint?> slots, int activeBatch, int batchCount)
    {
        var lines = new List<string>();
        bool anySet = false;
        for (int k = 1; k <= 9; k++)
        {
            var p = k - 1 < slots.Count ? slots[k - 1] : null;
            string n = k.ToString(CultureInfo.InvariantCulture);
            if (p == null || (p.Latitude == 0 && p.Longitude == 0))
            {
                lines.Add($"Waypoint {n}: not set");
                continue;
            }
            anySet = true;
            string ident = p.Ident.Trim();
            string label = ident.Length == 0 || TypedIdent.IsMatch(ident) ? $"Waypoint {n}" : $"Waypoint {n}, {ident}";
            lines.Add($"{label}: {Position(p.Latitude, p.Longitude)}");
        }
        // The gauge keeps a route's batch numbers after a power loss has emptied every slot (live).
        if (batchCount > 0 && anySet)
            lines.Add($"Route: batch {activeBatch.ToString(CultureInfo.InvariantCulture)} of {batchCount.ToString(CultureInfo.InvariantCulture)}");
        return lines;
    }

    /// <summary>"N 37 17.9, W 117 18.6".</summary>
    public static string Position(double latitude, double longitude)
    {
        string lat = L1011InsKeys.LatitudeKeys(latitude), lon = L1011InsKeys.LongitudeKeys(longitude);
        return $"{lat[0]} {lat.Substring(1, 2)} {lat.Substring(3, 2)}.{lat[5]}, {lon[0]} {lon.Substring(1, 3)} {lon.Substring(4, 2)}.{lon[6]}";
    }
}
