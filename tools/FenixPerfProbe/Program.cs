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
    var to = await reader.ReadSpeedsAsync(forceApproach: false);
    Console.WriteLine($"speeds:    F={to?.F} S={to?.S} O={to?.O} (from page {to?.Page})");
    var ap = await reader.ReadSpeedsAsync(forceApproach: true);
    Console.WriteLine($"APPR page: F={ap?.F} S={ap?.S} O={ap?.O} VLS={ap?.Vls} VAPP={ap?.Vapp}");
    var td = await reader.ReadTodAsync();
    Console.WriteLine($"TOD: utc={td?.Utc ?? "(null)"} dist={td?.DistanceNm}");
    return;
}

int failures = 0;
void Check(string name, bool ok) { Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}"); if (!ok) failures++; }
string Fix(string f) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", f));

string to_ = Fix("perf_to.xml"), crz = Fix("perf_crz.xml"), appr = Fix("perf_appr.xml");

Check("identify TO", R.IdentifyPage(to_) == R.PerfPage.TakeOff);
Check("identify CRZ", R.IdentifyPage(crz) == R.PerfPage.Cruise);
Check("identify APPR", R.IdentifyPage(appr) == R.PerfPage.Approach);
Check("identify junk is null", R.IdentifyPage("<root><title>DOORS</title></root>") == null);

var t = R.ParseSpeeds(to_, R.PerfPage.TakeOff);
Check("TO F=150", t.F == 150); Check("TO S=194", t.S == 194); Check("TO O=220", t.O == 220);
Check("TO has no VLS", t.Vls == null && t.Vapp == null);

var a = R.ParseSpeeds(appr, R.PerfPage.Approach);
Check("APPR F=141", a.F == 141); Check("APPR S=185", a.S == 185); Check("APPR O=220", a.O == 220);
Check("APPR VAPP=131", a.Vapp == 131); Check("APPR VLS=126", a.Vls == 126);

// dashed (uncomputed) variants — token-exact replaces so the asserts don't
// depend on inter-column spacing; the Fenix renders "O=---" pre-weights
// (observed live earlier the same day).
string dashedTo = to_.Replace("F=g150w", "F=---").Replace("S=g194w", "S=---").Replace("O=g220w", "O=---");
var d = R.ParseSpeeds(dashedTo, R.PerfPage.TakeOff);
Check("dashed TO F null", d.F == null); Check("dashed TO S null", d.S == null); Check("dashed TO O null", d.O == null);

var td2 = R.ParseTod(crz);
Check("TOD utc=2231", td2.Utc == "2231"); Check("TOD dist=1209", td2.DistanceNm == 1209);
string dashedCrz = crz.Replace("g2231", "g----").Replace("1209w", "----w");
var td3 = R.ParseTod(dashedCrz);
Check("dashed TOD utc null", td3.Utc == null); Check("dashed TOD dist null", td3.DistanceNm == null);

// Forward-only navigation safety (the 2026-07-05 in-flight incident):
// LSK6R may only be pressed when the display actually renders "PHASE>".
Check("TO page renders NEXT PHASE key", R.HasNextPhaseKey(to_));
Check("CRZ page renders NEXT PHASE key", R.HasNextPhaseKey(crz));
Check("APPR page renders NEXT PHASE key", R.HasNextPhaseKey(appr));
Check("PREV-only page has no NEXT key",
    !R.HasNextPhaseKey("<root><title>GO AROUND</title><line>s PREV              </line><line>&lt;PHASE                  </line></root>"));
Check("TodInfo PastTod defaults false", new R.TodInfo("2231", 1209).PastTod == false);
Check("TodInfo PastTod settable", new R.TodInfo(null, null, true).PastTod);

Console.WriteLine(failures == 0 ? "\nALL PASS" : $"\n{failures} FAILURES");
Environment.Exit(failures == 0 ? 0 : 1);
