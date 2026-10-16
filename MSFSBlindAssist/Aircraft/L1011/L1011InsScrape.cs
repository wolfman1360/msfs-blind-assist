using System.Globalization;
using MSFSBlindAssist.Utils;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>Everything one INS view shows: the unit's display, the PMS drawn in the same view (the
/// captain's in view 1, the first officer's in view 2, none in view 3), its nine waypoint slots, and the
/// imported route's batch and automatic batch loading.</summary>
public sealed record L1011InsScrapeResult(L1011InsScreen Ins, L1011PmsScreen? Pms,
    IReadOnlyList<L1011InsWaypoint?> Waypoints, int ActiveBatch, int BatchCount, bool? AutoBatch);

/// <summary>
/// Reads the "key|value" rows coherent-l1011-ins-agent.js returns. A row is split at its first bar only,
/// so a value may itself hold one; a waypoint row is "wpt_k|ident|latitude|longitude" and the batch row
/// "batch|active|count". Numbers are invariant culture. Rows without a unit 1 to 3 are not an INS
/// view, and the whole read is refused. Pure.
/// </summary>
public static class L1011InsScrape
{
    public static L1011InsScrapeResult? Parse(IReadOnlyList<string> rows)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            int bar = row.IndexOf('|');
            if (bar > 0)
                values[row.Substring(0, bar)] = row.Substring(bar + 1);
        }
        string Get(string key) => values.TryGetValue(key, out var v) ? v : string.Empty;

        if (Number(Get("unit")) is not double u || u < 1 || u > 3)
            return null;
        int unit = (int)u;

        var dots = new HashSet<int>(Get("dots").Where(c => c >= '1' && c <= '6').Select(c => c - '0'));
        var ins = new L1011InsScreen(
            unit,
            Get("power") != "off",
            Number(Get("mode")) is double mode ? (int)Math.Round(mode) : -1,
            Number(Get("wheel")) is double wheel ? (int)Math.Round(wheel) : 0,
            Get("left"),
            Get("right"),
            Get("tk"),
            dots,
            Number(Get("status")),
            Get("entry"),
            List(Get("lights")));

        var slots = new List<L1011InsWaypoint?>();
        for (int k = 1; k <= 9; k++)
        {
            var parts = Get("wpt_" + k.ToString(CultureInfo.InvariantCulture)).Split('|');
            slots.Add(parts.Length == 3 && Number(parts[1]) is double lat && Number(parts[2]) is double lon
                ? new L1011InsWaypoint(parts[0], lat, lon)
                : null);
        }

        var batch = Get("batch").Split('|');
        int active = batch.Length > 0 && Number(batch[0]) is double a ? (int)a : 0;
        int count = batch.Length > 1 && Number(batch[1]) is double c ? (int)c : 0;
        bool? autoBatch = Get("auto_batch") switch { "1" => true, "0" => false, _ => null };

        return new L1011InsScrapeResult(ins, Pms(Get), slots, active, count, autoBatch);
    }

    private static L1011PmsScreen? Pms(Func<string, string> get)
    {
        string? seat = get("pms_seat") switch { "CPT" => "Captain", "FO" => "First officer", _ => null };
        if (seat == null)
            return null;
        var left = new List<string>();
        var right = new List<string>();
        for (int i = 1; i <= 6; i++)
        {
            string n = i.ToString(CultureInfo.InvariantCulture);
            left.Add(get("pms_left_" + n));
            right.Add(get("pms_right_" + n));
        }
        int line = Number(get("pms_line")) is double l ? (int)Math.Round(l) : 0;
        return new L1011PmsScreen(seat, get("pms_title"), left, right, get("pms_entry") == "1", line, List(get("pms_lights")));
    }

    private static IReadOnlyList<string> List(string csv) =>
        csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A number the gauge wrote, parsed as the repo parses another program's numbers ([CORE-17]);
    /// null for an empty row, NaN or infinity.</summary>
    private static double? Number(string text) => NumberParsing.TryParseExternal(text, out double v) ? v : null;
}
