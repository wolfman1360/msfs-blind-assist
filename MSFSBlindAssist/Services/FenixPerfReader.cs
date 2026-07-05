using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MSFSBlindAssist.Services;

/// <summary>
/// One-shot reader for FMS values that the Fenix only renders on MCDU PERF
/// pages (characteristic speeds F/S/O, VLS/VAPP, top of descent). Drives the
/// FIRST OFFICER's MCDU (MCDU 2) via the Fenix GraphQL API on localhost:8083
/// so the pilot's MCDU 1 — and MSFSBA's MCDU window, which defaults to it —
/// are never disturbed. Plain HTTP POST per query; no WebSocket, so it cannot
/// collide with FenixMCDUService's graphql-transport-ws subscription.
/// Dependency-light ON PURPOSE (System.Text.Json, no WinForms, no SimConnect):
/// tools/FenixPerfProbe links this file and asserts the parsers offline.
/// </summary>
public sealed class FenixPerfReader
{
    private const string GraphQlUrl = "http://localhost:8083/graphql";
    private const int KeySettleMs = 80;       // press->release gap (FenixMCDUService uses 50)
    private const int PageSettleMs = 550;     // release->display repaint (verified live at 500-600)
    private const int MaxPhasePresses = 6;    // TO..GA is 5 hops; 6th press = give up

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    public enum PerfPage { TakeOff = 0, Climb = 1, Cruise = 2, Descent = 3, Approach = 4, GoAround = 5 }
    public sealed record PerfSpeeds(int? F, int? S, int? O, int? Vls, int? Vapp, PerfPage Page);
    public sealed record TodInfo(string? Utc, int? DistanceNm);

    public async Task<string?> ReadFlightPhaseAsync()
        => await QueryDataRefAsync("aircraft.fms.flightPhase");

    /// <summary>Walk MCDU 2 to PERF TAKE OFF (or APPR) and parse F/S/O (+VLS/VAPP on APPR).</summary>
    public async Task<PerfSpeeds?> ReadSpeedsAsync(bool approachPage)
    {
        var xml = await NavigateToPerfPageAsync(approachPage ? PerfPage.Approach : PerfPage.TakeOff);
        if (xml == null) return null;
        return ParseSpeeds(xml, approachPage ? PerfPage.Approach : PerfPage.TakeOff);
    }

    /// <summary>Walk MCDU 2 to PERF CRZ and parse the "TO (T/D)" UTC/DIST field.</summary>
    public async Task<TodInfo?> ReadTodAsync()
    {
        var xml = await NavigateToPerfPageAsync(PerfPage.Cruise);
        if (xml == null) return null;
        return ParseTod(xml);
    }

    // ---------- navigation ----------

    private async Task<string?> NavigateToPerfPageAsync(PerfPage target)
    {
        // PERF always recalls the current-phase PERF page, wherever MCDU 2 was
        // (it repaints on its own at phase changes / Fenix automations, so
        // navigation is content-driven, never press-count-assumed).
        await PressKeyAsync("PERF");
        var xml = await ReadDisplayAsync();
        var page = xml == null ? null : IdentifyPage(xml);
        if (page == null)
        {
            // one retry — a Fenix repaint can race the first press
            await PressKeyAsync("PERF");
            xml = await ReadDisplayAsync();
            page = xml == null ? null : IdentifyPage(xml);
            if (page == null) return null;
        }
        for (int i = 0; i < MaxPhasePresses && page != target; i++)
        {
            await PressKeyAsync(page < target ? "LSK6R" : "LSK6L"); // NEXT PHASE / PREV PHASE
            xml = await ReadDisplayAsync();
            page = xml == null ? null : IdentifyPage(xml);
            if (page == null) return null;
        }
        return page == target ? xml : null;
    }

    // ---------- pure parsers (probe-tested against real captures) ----------

    /// <summary>Identify which PERF phase page this display XML shows, else null.</summary>
    public static PerfPage? IdentifyPage(string xml)
    {
        var title = ExtractTitle(xml);
        if (title.Contains("TAKE OFF")) return PerfPage.TakeOff;
        if (title.Contains("GO AROUND")) return PerfPage.GoAround;
        // order matters: the APPR title line also carries a "DEST" column
        // header ("sDESTl     APPR"), and "DEST" contains "DES".
        if (title.Contains("APPR")) return PerfPage.Approach;
        if (title.Contains("CRZ")) return PerfPage.Cruise;
        if (title.Contains("CLB")) return PerfPage.Climb;
        if (title.Contains("DES")) return PerfPage.Descent;
        return null;
    }

    public static PerfSpeeds ParseSpeeds(string xml, PerfPage page)
    {
        var lines = ExtractLines(xml);
        var all = string.Join("\n", lines);
        // Values render like "F=g150w" (single-letter color codes hug the
        // digits); uncomputed renders "F=---". Tolerate 0-2 code letters.
        int? f = MatchSpeed(all, "F"), s = MatchSpeed(all, "S"), o = MatchSpeed(all, "O");
        int? vls = null, vapp = null;
        if (page == PerfPage.Approach)
        {
            // label row "s VAPP      VLS", value row "cs131lw   g126w   cFULL w"
            // — the value row's only digit runs are VAPP then VLS, in that order.
            for (int i = 0; i < lines.Count - 1; i++)
            {
                if (Regex.IsMatch(lines[i], @"\bVAPP\b") && Regex.IsMatch(lines[i], @"\bVLS\b"))
                {
                    var nums = Regex.Matches(lines[i + 1], @"\d+");
                    if (nums.Count >= 2)
                    {
                        vapp = int.Parse(nums[0].Value);
                        vls = int.Parse(nums[1].Value);
                    }
                    break;
                }
            }
        }
        return new PerfSpeeds(f, s, o, vls, vapp, page);
    }

    public static TodInfo ParseTod(string xml)
    {
        var lines = ExtractLines(xml);
        // Marker row contains "(T/D)"; the row after the following
        // "UTC ... DIST" header carries "g s.77lw   g2231      1209w"
        // (managed mach + UTC + DIST). Uncomputed: dashes instead of digits.
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].Contains("(T/D)")) continue;
            for (int j = i + 1; j < Math.Min(i + 4, lines.Count); j++)
            {
                var m = Regex.Match(lines[j], @"(\d{4})\s+(\d+)");
                if (m.Success)
                    return new TodInfo(m.Groups[1].Value, int.Parse(m.Groups[2].Value));
            }
            return new TodInfo(null, null); // (T/D) present but values dashed
        }
        return new TodInfo(null, null);
    }

    private static int? MatchSpeed(string text, string letter)
    {
        var m = Regex.Match(text, letter + @"=\s*[a-z]{0,2}(\d+|-+)");
        if (!m.Success || m.Groups[1].Value.StartsWith('-')) return null;
        return int.Parse(m.Groups[1].Value);
    }

    private static string ExtractTitle(string xml)
    {
        try { return XDocument.Parse(xml).Root?.Element("title")?.Value ?? ""; }
        catch { return ""; }
    }

    private static List<string> ExtractLines(string xml)
    {
        try
        {
            var root = XDocument.Parse(xml).Root;
            return root == null ? new List<string>()
                 : root.Elements("line").Select(e => e.Value).ToList();
        }
        catch { return new List<string>(); }
    }

    // ---------- GraphQL transport ----------

    private async Task PressKeyAsync(string key)
    {
        await WriteIntAsync($"system.switches.S_CDU2_KEY_{key}", 1);
        await Task.Delay(KeySettleMs);
        await WriteIntAsync($"system.switches.S_CDU2_KEY_{key}", 0);
        await Task.Delay(PageSettleMs);
    }

    private Task<string?> ReadDisplayAsync() => QueryDataRefAsync("aircraft.mcdu2.display");

    private async Task<string?> QueryDataRefAsync(string name)
    {
        var payload = JsonSerializer.Serialize(new
        {
            query = "query ($n: String!) { dataRef { dataRef(name: $n) { value } } }",
            variables = new { n = name }
        });
        using var resp = await Http.PostAsync(GraphQlUrl,
            new StringContent(payload, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var value = doc.RootElement.GetProperty("data").GetProperty("dataRef")
                       .GetProperty("dataRef").GetProperty("value");
        return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
    }

    private async Task WriteIntAsync(string name, int value)
    {
        var payload = JsonSerializer.Serialize(new
        {
            query = "mutation ($n: String!, $v: Int!) { dataRef { writeInt(name: $n, value: $v) } }",
            variables = new { n = name, v = value }
        });
        using var resp = await Http.PostAsync(GraphQlUrl,
            new StringContent(payload, Encoding.UTF8, "application/json"));
        resp.EnsureSuccessStatusCode();
    }
}
