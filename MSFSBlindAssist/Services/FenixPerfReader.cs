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
    public sealed record TodInfo(string? Utc, int? DistanceNm, bool PastTod = false);

    public async Task<string?> ReadFlightPhaseAsync()
        => await QueryDataRefAsync("aircraft.fms.flightPhase");

    /// <summary>
    /// Read F/S/O (+VLS/VAPP when the APPR page is used). FORWARD-ONLY:
    /// if PERF opens on TAKE OFF (PreFlight/TakeOff phase) the takeoff
    /// values are read directly; on GO AROUND the GA page's own F/S/O are
    /// read; otherwise the reader walks forward to APPR. When
    /// <paramref name="forceApproach"/> is true (VLS/VAPP) only APPR
    /// qualifies — null if APPR is behind the current page (GA active).
    /// </summary>
    public async Task<PerfSpeeds?> ReadSpeedsAsync(bool forceApproach)
    {
        var (xml, page) = await OpenPerfAsync();
        if (xml == null || page == null) return null;
        if (!forceApproach && page == PerfPage.TakeOff)
            return ParseSpeeds(xml, PerfPage.TakeOff);
        if (!forceApproach && page == PerfPage.GoAround)
            return ParseSpeeds(xml, PerfPage.GoAround); // GA page carries its own F/S/O
        if (page.Value > PerfPage.Approach)
            return null; // APPR lies behind (GA active) — never navigate backward
        xml = page == PerfPage.Approach ? xml
            : await WalkForwardAsync(page.Value, PerfPage.Approach, xml);
        return xml == null ? null : ParseSpeeds(xml, PerfPage.Approach);
    }

    /// <summary>
    /// Read the CRZ page "TO (T/D)" UTC/DIST field. FORWARD-ONLY: when the
    /// FMS is already past cruise (DES/APPR/GA page is current), returns
    /// PastTod without pressing anything.
    /// </summary>
    public async Task<TodInfo?> ReadTodAsync()
    {
        var (xml, page) = await OpenPerfAsync();
        if (xml == null || page == null) return null;
        if (page.Value > PerfPage.Cruise)
            return new TodInfo(null, null, PastTod: true);
        xml = page == PerfPage.Cruise ? xml
            : await WalkForwardAsync(page.Value, PerfPage.Cruise, xml);
        return xml == null ? null : ParseTod(xml);
    }

    // ---------- navigation ----------
    //
    // ⚠️ SAFETY INVARIANT — NEVER press LSK6L (the left line-6 key) on a
    // PERF page. On the ground it is "PREV PHASE", but IN FLIGHT the active
    // phase's PERF page renders "ACTIVATE APPR PHASE" there, and two presses
    // (press + re-identify loop) ACTIVATE AND CONFIRM the approach phase —
    // this happened live mid-climb on 2026-07-05 and forced the user's FMS
    // into approach mode. All navigation is forward-only via LSK6R, and only
    // after HasNextPhaseKey confirms the label "PHASE>" is actually rendered.

    private async Task<(string?, PerfPage?)> OpenPerfAsync()
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
        }
        return (xml, page);
    }

    private async Task<string?> WalkForwardAsync(PerfPage current, PerfPage target, string xml)
    {
        for (int i = 0; i < MaxPhasePresses && current != target; i++)
        {
            if (!HasNextPhaseKey(xml)) return null; // no NEXT PHASE rendered — do not press blind
            await PressKeyAsync("LSK6R");
            var next = await ReadDisplayAsync();
            var page = next == null ? null : IdentifyPage(next);
            if (next == null || page == null) return null;
            if (page.Value <= current) return null; // did not advance — abort, never retry backward
            current = page.Value;
            xml = next;
        }
        return current == target ? xml : null;
    }

    /// <summary>True when the display renders the right-aligned "NEXT PHASE"
    /// soft key (its label ends "PHASE>"; the PREV label is "&lt;PHASE" and
    /// never matches). Pure, probe-tested.</summary>
    public static bool HasNextPhaseKey(string xml)
    {
        foreach (var line in ExtractLines(xml))
            if (line.Contains("PHASE>")) return true;
        return false;
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
