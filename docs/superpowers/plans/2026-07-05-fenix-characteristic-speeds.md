# Fenix Characteristic Speeds + TOD Hotkeys Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fenix A319/A320/A321 output-mode hotkeys Shift+1/2/3 (Green Dot / S / F), Shift+4 (VLS+VAPP), Shift+D (top of descent) read from the copilot's MCDU PERF pages via the Fenix GraphQL API.

**Architecture:** A new dependency-light `Services/FenixPerfReader.cs` does one-shot HTTP GraphQL reads/writes against `http://localhost:8083/graphql` — presses MCDU 2 keys, reads `aircraft.mcdu2.display` XML, navigates PERF phase pages content-first, parses values with format-code-tolerant regexes. `FenixA320Definition.HandleHotkeyAction` gains five cases that call it fire-and-forget and announce results. Pure parse logic is probe-tested offline against real captured MCDU XML in a standalone `tools/FenixPerfProbe` console app (CDUTest pattern: builds alone, NOT in the solution, links the service source).

**Tech Stack:** .NET 10 / C# 13, System.Text.Json (in-box — deliberately NOT Newtonsoft so the probe needs no packages), System.Net.Http.

**Spec:** `docs/superpowers/specs/2026-07-05-fenix-characteristic-speeds-design.md`. One deviation from the spec, decided at planning: the reader's `HttpClient` is `static readonly` (standard shared-client practice), so no disposal wiring on aircraft swap is needed — the reader holds no other unmanaged state.

## Global Constraints

- Repo convention: NO unit-test project; pure logic is verified by console probes under `tools/` (run with `dotnet run --project tools/<Name>`); everything else live-in-sim (CLAUDE.md "Testing").
- Build the SOLUTION, never the csproj alone: `dotnet build MSFSBlindAssist.sln -c Debug`; verify `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` timestamp updates. Exe is file-locked while the app runs (MSB3021) — close app first.
- Only touch: `MSFSBlindAssist/Services/FenixPerfReader.cs` (new), `MSFSBlindAssist/Aircraft/FenixA320Definition.cs`, `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`, `tools/FenixPerfProbe/*` (new). Nothing else.
- Do NOT handle `ReadSpeedVS` / `ReadSpeedVFE` on the Fenix (user decision: no dead keys).
- Screen-reader rules: user-initiated hotkey readouts announce via `AnnounceImmediate`.
- Work on branch `feature/fenix-characteristic-speeds`.

---

### Task 1: FenixPerfReader + offline probe

**Files:**
- Create: `MSFSBlindAssist/Services/FenixPerfReader.cs`
- Create: `tools/FenixPerfProbe/FenixPerfProbe.csproj`
- Create: `tools/FenixPerfProbe/Program.cs`
- Create: `tools/FenixPerfProbe/fixtures/perf_to.xml`, `fixtures/perf_crz.xml`, `fixtures/perf_appr.xml`

**Interfaces (produced, used by Task 2):**
```csharp
namespace MSFSBlindAssist.Services;
public sealed class FenixPerfReader
{
    public enum PerfPage { TakeOff = 0, Climb = 1, Cruise = 2, Descent = 3, Approach = 4, GoAround = 5 }
    public sealed record PerfSpeeds(int? F, int? S, int? O, int? Vls, int? Vapp, PerfPage Page);
    public sealed record TodInfo(string? Utc, int? DistanceNm);
    public Task<string?> ReadFlightPhaseAsync();          // e.g. "PreFlight"; null if unreadable
    public Task<PerfSpeeds?> ReadSpeedsAsync(bool approachPage); // null = page walk failed; throws HttpRequestException/TaskCanceledException on connection failure
    public Task<TodInfo?> ReadTodAsync();                 // null = page walk failed; Utc/DistanceNm null = not computed yet
    public static PerfPage? IdentifyPage(string xml);     // pure, probe-tested
    public static PerfSpeeds ParseSpeeds(string xml, PerfPage page);
    public static TodInfo ParseTod(string xml);
}
```

- [ ] **Step 1: Write the three fixture files** (real MCDU 2 captures, Fenix A319, 2026-07-05 — copy VERBATIM)

`tools/FenixPerfProbe/fixtures/perf_to.xml`:
```xml
<root>
  <title>    TAKE OFF RWY g29R    </title>
  <line>s V1  FLP RETR           </line>
  <line>c131     wF=g150w           </line>
  <line>s VR  SLT RETR  TO SHIFT </line>
  <line>c131     wS=g194w   [sMl]c[  ]*w</line>
  <line>s V2     CLEAN  FLAPS/THS</line>
  <line>c135     wO=g220w    c1/[   ]w</line>
  <line>sTRANS ALT   FLEX TO TEMP</line>
  <line>cs5000lw                 c55°w</line>
  <line>sTHR RED/ACC  ENG OUT ACC</line>
  <line>c s2270l/s2270lw          cs2270lw</line>
  <line>s                   NEXT </line>
  <line>                  PHASE&gt;</line>
  <scratchpad>NAV ACCUR UPGRAD        </scratchpad>
</root>
```

`tools/FenixPerfProbe/fixtures/perf_crz.xml`:
```xml
<root>
  <title>          CRZ           </title>
  <line>sACT MODE   UTC DEST aEFOBw</line>
  <line>gMANAGEDw   g0312w       a4.6w</line>
  <line>s CI                     </line>
  <line>c6w               sTOl g(T/D)w</line>
  <line>s MANAGED   UTC      DIST</line>
  <line>g s.77lw      g2231      1209w</line>
  <line>s PRESEL                 </line>
  <line>c*[ ]w                    </line>
  <line>s          DES CABIN RATE</line>
  <line>               sc-350wFT/MNl</line>
  <line>s PREV              NEXT </line>
  <line>&lt;PHASE            PHASE&gt;</line>
  <scratchpad>NAV ACCUR UPGRAD        </scratchpad>
</root>
```

`tools/FenixPerfProbe/fixtures/perf_appr.xml`:
```xml
<root>
  <title>sDESTl     APPR           </title>
  <line>s QNH    FLP RETR  FINAL </line>
  <line>c[  ]w     F=g141w   gILS22-Zw</line>
  <line>s TEMP   SLT RETR   BARO </line>
  <line>c[ ]°     wS=g185w       c[ ]w</line>
  <line>sMAG WIND   CLEAN  RADIO </line>
  <line>c235°/8   wO=g220w       c[ ]w</line>
  <line>sTRANS FL       LDG CONF </line>
  <line>csFL115lw             csCONF3l*w</line>
  <line>s VAPP      VLS          </line>
  <line>cs131lw        g126w     cFULL w</line>
  <line>s PREV              NEXT </line>
  <line>&lt;PHASE            PHASE&gt;</line>
  <scratchpad>NAV ACCUR UPGRAD        </scratchpad>
</root>
```

- [ ] **Step 2: Write the probe with asserts (will not compile yet — reader missing)**

`tools/FenixPerfProbe/FenixPerfProbe.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>FenixPerfProbe</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="..\..\MSFSBlindAssist\Services\FenixPerfReader.cs" Link="FenixPerfReader.cs" />
    <None Update="fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

`tools/FenixPerfProbe/Program.cs`:
```csharp
// Offline parser asserts against real Fenix A319 MCDU captures (2026-07-05),
// plus an optional --live end-to-end walk against a running Fenix.
// Run: dotnet run --project tools/FenixPerfProbe            (fixtures, no sim)
//      dotnet run --project tools/FenixPerfProbe -- --live  (needs sim + Fenix)
using MSFSBlindAssist.Services;
using R = MSFSBlindAssist.Services.FenixPerfReader;

if (args.Contains("--live"))
{
    var reader = new R();
    Console.WriteLine($"flightPhase = {await reader.ReadFlightPhaseAsync() ?? "(null)"}");
    var to = await reader.ReadSpeedsAsync(approachPage: false);
    Console.WriteLine($"TO page:   F={to?.F} S={to?.S} O={to?.O} (page {to?.Page})");
    var ap = await reader.ReadSpeedsAsync(approachPage: true);
    Console.WriteLine($"APPR page: F={ap?.F} S={ap?.S} O={ap?.O} VLS={ap?.Vls} VAPP={ap?.Vapp}");
    var td = await reader.ReadTodAsync();
    Console.WriteLine($"TOD: utc={td?.Utc ?? "(null)"} dist={td?.DistanceNm}");
    return;
}

int failures = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); if (!ok) failures++; }
string Fix(string f) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", f));

string to_ = Fix("perf_to.xml"), crz = Fix("perf_crz.xml"), appr = Fix("perf_appr.xml");

Check("identify TO",   R.IdentifyPage(to_) == R.PerfPage.TakeOff);
Check("identify CRZ",  R.IdentifyPage(crz) == R.PerfPage.Cruise);
Check("identify APPR", R.IdentifyPage(appr) == R.PerfPage.Approach);
Check("identify junk is null", R.IdentifyPage("<root><title>DOORS</title></root>") == null);

var t = R.ParseSpeeds(to_, R.PerfPage.TakeOff);
Check("TO F=150", t.F == 150); Check("TO S=194", t.S == 194); Check("TO O=220", t.O == 220);
Check("TO has no VLS", t.Vls == null && t.Vapp == null);

var a = R.ParseSpeeds(appr, R.PerfPage.Approach);
Check("APPR F=141", a.F == 141); Check("APPR S=185", a.S == 185); Check("APPR O=220", a.O == 220);
Check("APPR VAPP=131", a.Vapp == 131); Check("APPR VLS=126", a.Vls == 126);

// dashed (uncomputed) variants — same layout, values replaced the way the
// Fenix renders them pre-weights (observed live earlier the same day: "O=---")
string dashedTo = to_.Replace("F=g150w", "F=---").Replace("S=g194w", "S=---").Replace("O=g220w", "O=---");
var d = R.ParseSpeeds(dashedTo, R.PerfPage.TakeOff);
Check("dashed TO F null", d.F == null); Check("dashed TO S null", d.S == null); Check("dashed TO O null", d.O == null);

var td2 = R.ParseTod(crz);
Check("TOD utc=2231", td2.Utc == "2231"); Check("TOD dist=1209", td2.DistanceNm == 1209);
string dashedCrz = crz.Replace("g s.77lw      g2231      1209w", "g s.77lw      g----      ----w");
var td3 = R.ParseTod(dashedCrz);
Check("dashed TOD utc null", td3.Utc == null); Check("dashed TOD dist null", td3.DistanceNm == null);

Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILURES");
Environment.Exit(failures == 0 ? 0 : 1);
```

- [ ] **Step 3: Run the probe to verify it FAILS to build (reader doesn't exist)**

Run: `dotnet run --project tools/FenixPerfProbe`
Expected: build error CS0246 — `FenixPerfReader` not found.

- [ ] **Step 4: Implement the reader**

`MSFSBlindAssist/Services/FenixPerfReader.cs`:
```csharp
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
```

- [ ] **Step 5: Run the probe — all asserts pass**

Run: `dotnet run --project tools/FenixPerfProbe`
Expected: every line `PASS`, final line `ALL PASS`, exit code 0.

- [ ] **Step 6: Optional live sanity (sim running with Fenix)**

Run: `dotnet run --project tools/FenixPerfProbe -- --live`
Expected: real phase string, real F/S/O numbers from both pages, real TOD (or nulls before descent computed). MCDU 1 untouched.

- [ ] **Step 7: Commit**

```bash
git add MSFSBlindAssist/Services/FenixPerfReader.cs tools/FenixPerfProbe
git commit -m "feat(fenix): add FenixPerfReader — MCDU 2 PERF-page reader over the Fenix GraphQL API"
```

---

### Task 2: Wire hotkeys into FenixA320Definition + guide + build

**Files:**
- Modify: `MSFSBlindAssist/Aircraft/FenixA320Definition.cs` (switch in `HandleHotkeyAction` ~line 13157; helper methods immediately before it)
- Modify: `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`

**Interfaces:**
- Consumes: `FenixPerfReader` exactly as defined in Task 1.
- Produces: user-visible hotkey behavior only.

- [ ] **Step 1: Add reader field + helpers before `HandleHotkeyAction`**

Anchor: insert immediately BEFORE the line `public override bool HandleHotkeyAction(HotkeyAction action,`:

```csharp
    // ---- Characteristic speeds + TOD via MCDU 2 (Fenix GraphQL) ----
    // The Fenix publishes GD/S/F/VLS/VAPP/TOD ONLY on MCDU PERF pages (its
    // 481-dataRef public API has no direct values — verified 2026-07-05), so
    // these hotkeys walk the F/O's MCDU there and parse the page. Shift+5/6
    // (stall / VFE next) are deliberately NOT handled: the Fenix renders them
    // nowhere, and a permanently-dead "not available" key was rejected.
    private Services.FenixPerfReader? _perfReader;
    private int _perfReadBusy; // Interlocked latch — one MCDU walk at a time

    private enum FenixCharSpeed { GreenDot, SSpeed, FSpeed, Vls }

    private void ReadFenixPerfValue(ScreenReaderAnnouncer announcer,
        System.Windows.Forms.Form parentForm, FenixCharSpeed? speed, bool tod = false)
    {
        if (System.Threading.Interlocked.CompareExchange(ref _perfReadBusy, 1, 0) != 0)
        {
            announcer.AnnounceImmediate("MCDU read already in progress");
            return;
        }
        _perfReader ??= new Services.FenixPerfReader();
        var reader = _perfReader;
        _ = Task.Run(async () =>
        {
            string msg;
            try
            {
                if (tod)
                {
                    var td = await reader.ReadTodAsync();
                    msg = td == null ? "Could not read the MCDU"
                        : td.Utc == null || td.DistanceNm == null
                            ? "Top of descent not computed yet"
                            : $"{td.DistanceNm} miles to top of descent, at {td.Utc} Zulu";
                }
                else
                {
                    // VLS/VAPP live only on the APPR page; F/S/O follow the
                    // flight phase (takeoff-weight values while departing,
                    // landing-weight values from cruise onward).
                    bool appr = speed == FenixCharSpeed.Vls || await PerfPhaseWantsApproachAsync(reader);
                    var sp = await reader.ReadSpeedsAsync(appr);
                    msg = sp == null ? "Could not read the MCDU" : speed switch
                    {
                        FenixCharSpeed.GreenDot => sp.O == null ? "Green Dot not computed yet" : $"Green Dot {sp.O} knots",
                        FenixCharSpeed.SSpeed => sp.S == null ? "S speed not computed yet" : $"S speed {sp.S} knots",
                        FenixCharSpeed.FSpeed => sp.F == null ? "F speed not computed yet" : $"F speed {sp.F} knots",
                        _ => sp.Vls == null ? "VLS not computed yet"
                             : sp.Vapp == null ? $"VLS {sp.Vls} knots"
                             : $"VLS {sp.Vls} knots, VAPP {sp.Vapp} knots"
                    };
                }
            }
            catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
            {
                msg = "Fenix connection not available";
            }
            catch
            {
                msg = "Could not read the MCDU";
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _perfReadBusy, 0);
            }
            try
            {
                if (parentForm is { IsDisposed: false })
                    parentForm.BeginInvoke(() => announcer.AnnounceImmediate(msg));
            }
            catch (InvalidOperationException) { /* form torn down mid-read */ }
        });
    }

    private static async Task<bool> PerfPhaseWantsApproachAsync(Services.FenixPerfReader reader)
    {
        var phase = await reader.ReadFlightPhaseAsync() ?? "";
        // Observed live: "PreFlight". Departure-side phases read takeoff-weight
        // F/S/O from PERF TO; everything else (Cruise/Descent/Approach/GoAround/
        // Done/unknown) reads landing-weight values from PERF APPR.
        return !(phase.Contains("PreFlight", StringComparison.OrdinalIgnoreCase)
              || phase.Contains("Take", StringComparison.OrdinalIgnoreCase)
              || phase.Contains("Climb", StringComparison.OrdinalIgnoreCase));
    }

```

- [ ] **Step 2: Add the five switch cases**

Anchor: insert immediately BEFORE `case HotkeyAction.MonitorManager:` inside `HandleHotkeyAction`:

```csharp
            // Characteristic speeds + TOD (output mode Shift+1/2/3/4 and
            // Shift+D) — read from the F/O MCDU PERF pages. ReadSpeedVS and
            // ReadSpeedVFE are intentionally ABSENT (no Fenix data source).
            case HotkeyAction.ReadSpeedGD:
                ReadFenixPerfValue(announcer, parentForm, FenixCharSpeed.GreenDot);
                return true;

            case HotkeyAction.ReadSpeedS:
                ReadFenixPerfValue(announcer, parentForm, FenixCharSpeed.SSpeed);
                return true;

            case HotkeyAction.ReadSpeedF:
                ReadFenixPerfValue(announcer, parentForm, FenixCharSpeed.FSpeed);
                return true;

            case HotkeyAction.ReadSpeedVLS:
                ReadFenixPerfValue(announcer, parentForm, FenixCharSpeed.Vls);
                return true;

            case HotkeyAction.ReadDistanceToTOD:
                ReadFenixPerfValue(announcer, parentForm, null, tod: true);
                return true;

```

- [ ] **Step 3: Build the solution**

Run: `dotnet build MSFSBlindAssist.sln -c Debug`
Expected: Build succeeded; `MSFSBlindAssist\bin\x64\Debug\net10.0-windows\MSFSBlindAssist.exe` LastWriteTime is now. If MSB3021 (exe locked): the app is running — close it and rebuild.

- [ ] **Step 4: Update the hotkey guide**

In `MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt`, insert AFTER the "Navigation:" block (the line `  N          Read NAV 1 and NAV 2 radio info (frequency, ident, localizer, glideslope, DME)`):

```
Characteristic Speeds (read live from the copilot's MCDU PERF pages —
takeoff page while departing, approach page from cruise onward; the
pilot's own MCDU is never touched):
  Shift+1    Read Green Dot Speed (clean config)
  Shift+2    Read S Speed (slat retraction)
  Shift+3    Read F Speed (flap retraction)
  Shift+4    Read VLS and VAPP (approach page values)
  Note: Shift+5 (stall speed) and Shift+6 (VFE Next) are not available
  on the Fenix — the aircraft does not publish those values anywhere.
```

And insert AFTER the "Location:" block (the line `  C          Read Nearest City`):

```
Distance:
  Shift+D    Read distance and UTC time to Top of Descent (from the
             cruise PERF page; announces "not computed yet" until the
             FMS has computed the descent)
```

- [ ] **Step 5: Commit**

```bash
git add MSFSBlindAssist/Aircraft/FenixA320Definition.cs MSFSBlindAssist/HotkeyGuides/Fenix_A320_Hotkeys.txt
git commit -m "feat(fenix): characteristic-speed + TOD hotkeys (Shift+1-4, Shift+D) via MCDU 2"
```

---

### Task 3: Live verification (user + agent, sim running)

No code. Checklist executed with the running Fenix A319 (current flight VIDP→VCBI):

- [ ] `dotnet run --project tools/FenixPerfProbe -- --live` prints real values for both pages + TOD (agent-driven, before user testing).
- [ ] User restarts MSFSBlindAssist (new build), selects Fenix, presses `]` then Shift+3 → hears "F speed 150 knots" (or current recomputed value).
- [ ] Shift+1, Shift+2, Shift+4 → Green Dot / S speed / VLS+VAPP announcements.
- [ ] Shift+D on the ground → "1209 miles to top of descent, at 2231 Zulu" (values current) — TOD is already computed in this flight.
- [ ] Shift+5 / Shift+6 → silent (unhandled), by design.
- [ ] MSFSBA's MCDU window (input `[` Shift+M) still tracks MCDU 1 correctly after several reads.
- [ ] Double-press race: hammer Shift+1 twice quickly → second press announces "MCDU read already in progress", no interleaved page walk.
- [ ] After the flight / sim closed: any of the new keys → "Fenix connection not available".
