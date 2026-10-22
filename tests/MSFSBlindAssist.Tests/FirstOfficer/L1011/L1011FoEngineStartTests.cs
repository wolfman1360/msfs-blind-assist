using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Item = MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor, MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's Engine Start flow, its action group and its read-back, pinned by structure and by
/// behaviour: engines 2, 1 and 3 on APU air, each fed by its own tank pump and tank valve, the previous engine's
/// isolation valve off before engines 1 and 3, the ground start turned on and watched to 20 percent N3, the fuel and
/// ignition on, the ground start released by the aircraft, a stable idle, then the engine generators on line. The
/// ground guard and four read-only waits that open it stop a flow with no engine data, no APU or no start air, and
/// every start step stops the flow when it is not done. The three "ground start switch: on" lines are the profile's
/// only StayComplete lines, latched on the engine running and linked by no step ([FOB-1]); every other line mirrors
/// the aircraft. Every key is one the definition takes, and the toggle-carrying fuel and ignition switches are written
/// only over their own position read ([L1011-4]).
/// </summary>
public class L1011FoEngineStartTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> EngineStart => Flows.Single(f => f.Id == "ENGINE_START");

    private static Step Of(string id) => EngineStart.Steps.Single(s => s.Id == id);
    private static ChecklistGroup<X, L1011FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);
    private static Item Line(string id) => Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    private static bool IsSwitch(Step s) =>
        s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple;

    /// <summary>The (key, target) pairs a switch step writes.</summary>
    private static (string Key, int Target)[] Actions(Step s) => s.ActionType switch
    {
        FlowStepActionType.SetSwitch => new[] { (s.EventName!, s.TargetValue!.Value) },
        FlowStepActionType.SetSwitchMultiple => s.MultiActions.Select(a => (a.EventName, a.TargetValue!.Value)).ToArray(),
        _ => Array.Empty<(string, int)>(),
    };

    /// <summary>The start order.</summary>
    public static IEnumerable<object[]> StartOrder() => new[] { new object[] { 2 }, new object[] { 1 }, new object[] { 3 } };

    /// <summary>The engine started before <paramref name="n"/>: none before engine 2.</summary>
    private static int? Prev(int n) => n switch { 1 => 2, 3 => 1, _ => null };

    private static string N3(int n) => $"L1011_RO_N3_{n}";
    private static string FuelIgnition(int n) => $"TOGGLE_CUTOFF_ENG_{n}";
    private static string GroundStartSwitch(int n) => $"SWITCH_GND_START_ENG_{n}";
    private static string Isolation(int n) => $"SWITCH_ISLN_VALVE_ENG_{n}";

    /// <summary>A cold N3 gauge (it never reads 0), and a stable idle as the gauge read it live.</summary>
    private const double Cold = 1.6;
    private const double Idle = 59.2;

    private static readonly string[] GeneratorRelays = new[] { 1, 2, 3 }
        .SelectMany(n => new[] { $"L1011_RO_GEN_{n}_FIELD", $"L1011_RO_GEN_{n}_BREAKER" }).ToArray();

    private static readonly string[] FuelPumps =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        return evaluator;
    }

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void EngineStart_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Engine Start", EngineStart.Name);
        Assert.Equal(new[] { "ENGINE_START", "ENGINE_START_CL" }, EngineStart.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(EngineStart.Description));
    }

    private static readonly string[] StepIds =
    {
        "ES_ON_GROUND", "ES_DATA", "ES_APU_RUNNING", "ES_APU_BLEED", "ES_CROSSBLEEDS",
        "ES_E2_FUEL_FEED", "ES_E2_ISLN", "ES_E2_GND_START", "ES_E2_N3_20", "ES_E2_FUEL_IGN", "ES_E2_RELEASED", "ES_E2_IDLE",
        "ES_E1_PREV_ISLN_OFF", "ES_E1_FUEL_FEED", "ES_E1_ISLN", "ES_E1_GND_START", "ES_E1_N3_20", "ES_E1_FUEL_IGN",
        "ES_E1_RELEASED", "ES_E1_IDLE",
        "ES_E3_PREV_ISLN_OFF", "ES_E3_FUEL_FEED", "ES_E3_ISLN", "ES_E3_GND_START", "ES_E3_N3_20", "ES_E3_FUEL_IGN",
        "ES_E3_RELEASED", "ES_E3_IDLE",
        "ES_GENERATORS",
    };

    [Fact]
    public void EngineStart_StepIdsAreInOrder()
    {
        Assert.Equal(29, StepIds.Length);
        Assert.Equal(StepIds, EngineStart.Steps.Select(s => s.Id));
    }

    // ------------------------------------------------------------------ the opening: the ground guard and four waits

    /// <summary>
    /// The flow opens with the ground guard and four read-only Stop waits, each linking no line and failing unread:
    /// engine 2's N3 read (the MD-11's engine data wait), the APU running, its bleed on and both crossbleeds on. Before
    /// Start leaves all of them so; a flow started without them stops before anything is written.
    /// </summary>
    [Theory]
    [InlineData("ES_ON_GROUND", "On the ground", "SIM_ON_GROUND", 5)]
    [InlineData("ES_DATA", "Engine data", "L1011_RO_N3_2", 10)]
    [InlineData("ES_APU_RUNNING", "APU running", "FO_APU_RUNNING", 10)]
    [InlineData("ES_APU_BLEED", "APU bleed: on", "SWITCH_APU_BLEED", 10)]
    [InlineData("ES_CROSSBLEEDS", "Crossbleed valves: on", "FO_CROSSBLEEDS_ON", 10)]
    public void The_five_opening_waits_are_read_only_Stops_with_unread_failing(string id, string label, string field, int seconds)
    {
        var wait = Of(id);
        Assert.Contains(id, StepIds.Take(5));
        Assert.Equal(Array.IndexOf(StepIds, id), EngineStart.Steps.FindIndex(s => s.Id == id));
        Assert.Equal(label, wait.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal(field, wait.ConditionFieldName);
        Assert.Equal(seconds, wait.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, wait.FailurePolicy);
        Assert.Null(wait.SkipCondition);
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.False(wait.Condition!(double.NaN), $"{id}: unread must fail the wait");
        if (id == "ES_DATA")
        {
            // Read is enough: a cold gauge reads about 1.6, never 0.
            Assert.True(wait.Condition!(Cold));
            Assert.True(wait.Condition!(Idle));
        }
        else
        {
            Assert.False(wait.Condition!(0), $"{id}: 0 must fail the wait");
            Assert.True(wait.Condition!(1), $"{id}: 1 must pass the wait");
        }
    }

    [Fact]
    public void Nothing_is_written_before_the_five_opening_waits()
        => Assert.Equal(5, EngineStart.Steps.FindIndex(IsSwitch));

    // ------------------------------------------------------------------ each engine

    /// <summary>
    /// Each engine, 2 then 1 then 3: the previous engine's isolation valve off (engines 1 and 3), its own fuel feed,
    /// its isolation valve read on, the ground start, the light-off watch to 20 percent N3, the fuel and ignition, the
    /// ground start released by the aircraft, and a stable idle. Every step from the isolation valve on stops the flow
    /// when it is not done; the ground start is skipped only for an engine already running (N3 at 50 or more), never
    /// for one turning below that.
    /// </summary>
    [Theory]
    [MemberData(nameof(StartOrder))]
    public void Each_engine_follows_feed_start_light_off_fuel_release_idle(int n)
    {
        string e = $"ES_E{n}_";
        var expected = (Prev(n) is null ? Array.Empty<string>() : new[] { e + "PREV_ISLN_OFF" })
            .Concat(new[] { "FUEL_FEED", "ISLN", "GND_START", "N3_20", "FUEL_IGN", "RELEASED", "IDLE" }.Select(x => e + x))
            .ToArray();
        var ids = EngineStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(expected, ids.Skip(ids.IndexOf(expected[0])).Take(expected.Length));
        Assert.Equal(expected.Length, ids.Count(id => id.StartsWith(e, StringComparison.Ordinal)));

        Assert.Equal(FlowStepFailurePolicy.Skip, Of(e + "FUEL_FEED").FailurePolicy);

        // The isolation valve: Before Start turned it on; read only.
        var isolation = Of(e + "ISLN");
        Assert.Equal($"Engine {n} isolation valve: on", isolation.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, isolation.ActionType);
        Assert.Equal(Isolation(n), isolation.ConditionFieldName);
        Assert.Equal(10, isolation.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, isolation.FailurePolicy);
        Assert.Null(isolation.SkipCondition);
        Assert.Empty(isolation.LinkedChecklistItemIds);
        Assert.False(isolation.Condition!(double.NaN));
        Assert.False(isolation.Condition!(0));
        Assert.True(isolation.Condition!(1));

        // The ground start: skipped only for an engine already running.
        var start = Of(e + "GND_START");
        Assert.Equal($"Engine {n} ground start switch: on", start.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, start.ActionType);
        Assert.Equal(X.GroundStart(n), start.EventName);
        Assert.Equal(1, start.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Stop, start.FailurePolicy);
        Assert.True(start.SkipCondition!(State((N3(n), 50))), "N3 50: running, already set");
        Assert.True(start.SkipCondition!(State((N3(n), Idle))), "idle: running, already set");
        Assert.False(start.SkipCondition!(State((N3(n), 49.9))), "N3 49.9: not running");
        Assert.False(start.SkipCondition!(State((N3(n), 20))), "turning below running: the composite decides");
        Assert.False(start.SkipCondition!(State((N3(n), Cold))), "cold");
        Assert.False(start.SkipCondition!(State()), "unread");
        var others = new[] { 1, 2, 3 }.Where(x => x != n).Select(x => (N3(x), Idle)).Append((N3(n), Cold)).ToArray();
        Assert.False(start.SkipCondition!(State(others)), "the other engines running never skip this one");

        // The light-off watch: N3 up to 20 percent, never skipped.
        var lightOff = Of(e + "N3_20");
        Assert.Equal($"Engine {n} N3 twenty percent", lightOff.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, lightOff.ActionType);
        Assert.Equal(X.EngineLightOff(n), lightOff.EventName);
        Assert.Equal(1, lightOff.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Stop, lightOff.FailurePolicy);
        Assert.Null(lightOff.SkipCondition);
        Assert.Empty(lightOff.LinkedChecklistItemIds);

        // The fuel and ignition: a toggle-carrying switch, already set only over its own position read on.
        var fuel = Of(e + "FUEL_IGN");
        Assert.Equal($"Engine {n} fuel and ignition: on", fuel.Label);
        Assert.Equal(new[] { (FuelIgnition(n), 1) }, Actions(fuel));
        Assert.Equal(FlowStepActionType.SetSwitch, fuel.ActionType);
        Assert.Equal(FlowStepFailurePolicy.Stop, fuel.FailurePolicy);
        Assert.True(fuel.SkipCondition!(State((FuelIgnition(n), 1))));
        Assert.False(fuel.SkipCondition!(State((FuelIgnition(n), 0))));
        Assert.False(fuel.SkipCondition!(State()));
        Assert.Equal(new[] { e + "FUEL_IGN", $"ESC_E{n}_FUEL_IGN" }, fuel.LinkedChecklistItemIds);

        // The release: the aircraft's own, as N3 passes about 55 percent.
        var released = Of(e + "RELEASED");
        Assert.Equal($"Engine {n} ground start switch released", released.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, released.ActionType);
        Assert.Equal(GroundStartSwitch(n), released.ConditionFieldName);
        Assert.Equal(60, released.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, released.FailurePolicy);
        Assert.Null(released.SkipCondition);
        Assert.True(released.Condition!(0));
        Assert.False(released.Condition!(1));
        Assert.False(released.Condition!(double.NaN), "unread must fail the wait");
        Assert.Equal(new[] { $"ESC_E{n}_RELEASED" }, released.LinkedChecklistItemIds);

        // The idle wait.
        var idle = Of(e + "IDLE");
        Assert.Equal($"Engine {n}: stable idle", idle.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, idle.ActionType);
        Assert.Equal(X.EngineIdle(n), idle.EventName);
        Assert.Equal(1, idle.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Stop, idle.FailurePolicy);
        Assert.Null(idle.SkipCondition);
        Assert.Equal(new[] { $"ESC_E{n}_RUNNING" }, idle.LinkedChecklistItemIds);
    }

    /// <summary>A ground start marked its line, a failed light-off stopped the flow, and the line stayed ticked over an
    /// engine that never ran (the MD-11's start switch lines): no ground start step links anything, and no step links a
    /// ground start line.</summary>
    [Fact]
    public void The_ground_start_step_links_nothing()
    {
        foreach (int n in new[] { 2, 1, 3 })
        {
            var start = Of($"ES_E{n}_GND_START");
            Assert.Null(start.CompletesChecklistItemId);
            Assert.Empty(start.LinkedChecklistItemIds);
        }
        Assert.DoesNotContain(Flows.SelectMany(f => f.Steps).SelectMany(s => s.LinkedChecklistItemIds),
            id => id.EndsWith("_GND_START", StringComparison.Ordinal));
    }

    /// <summary>Engine 1 starts with engine 2's isolation valve off and engine 3 with engine 1's, each right after the
    /// engine before it reads a stable idle; engine 2, the first, has no such step.</summary>
    [Fact]
    public void The_isolation_valve_of_the_previous_engine_goes_off_before_engines_one_and_three()
    {
        var ids = EngineStart.Steps.Select(s => s.Id).ToList();
        foreach (var (n, prev) in new[] { (1, 2), (3, 1) })
        {
            var s = Of($"ES_E{n}_PREV_ISLN_OFF");
            Assert.Equal($"Engine {prev} isolation valve: off", s.Label);
            Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
            Assert.Equal(new[] { (Isolation(prev), 0) }, Actions(s));
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.True(s.SkipCondition!(State((Isolation(prev), 0))), "already off");
            Assert.False(s.SkipCondition!(State((Isolation(prev), 1))));
            Assert.False(s.SkipCondition!(State()), "unread: not already set");
            Assert.Equal(new[] { $"ES_E{n}_PREV_ISLN_OFF", $"ESC_E{prev}_ISLN_OFF" }, s.LinkedChecklistItemIds);
            Assert.Equal(ids.IndexOf($"ES_E{prev}_IDLE") + 1, ids.IndexOf(s.Id));
            Assert.Equal(ids.IndexOf(s.Id) + 1, ids.IndexOf($"ES_E{n}_FUEL_FEED"));
        }
        Assert.Equal(new[] { "ES_E1_PREV_ISLN_OFF", "ES_E3_PREV_ISLN_OFF" },
            ids.Where(id => id.EndsWith("_PREV_ISLN_OFF", StringComparison.Ordinal)));
    }

    /// <summary>Each engine's own feed: its tank pump (1, 2 left and 3, each the first pump of its tank), its engine
    /// tank valve on and the valve's guard closed, already set only when all three read so. Engine 2's also delivers
    /// the read-back's "minimum one pump on" line.</summary>
    [Theory]
    [InlineData(1, "SWITCH_FUEL_PUMP_1_1")]
    [InlineData(2, "SWITCH_FUEL_PUMP_2L_1")]
    [InlineData(3, "SWITCH_FUEL_PUMP_3_1")]
    public void The_fuel_feed_keys_per_engine(int n, string pump)
    {
        var s = Of($"ES_E{n}_FUEL_FEED");
        Assert.Equal($"Engine {n} fuel: tank pump and tank valve on", s.Label);
        Assert.Equal(FlowStepActionType.SetSwitchMultiple, s.ActionType);
        Assert.Equal(new[] { (pump, 1), ($"SWITCH_ENG_{n}_TANK_VALVE", 1), ($"GUARD_ENG_{n}_TANK_VALVE", 0) }, Actions(s));
        Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);

        var done = Actions(s).Select(a => (Key: a.Key, Value: (double)a.Target)).ToArray();
        Assert.True(s.SkipCondition!(State(done)), "every part reads done: already set");
        Assert.False(s.SkipCondition!(State()), "unread: not already set");
        for (int i = 0; i < done.Length; i++)
        {
            var wrong = done.ToArray();
            wrong[i] = (wrong[i].Key, wrong[i].Value == 0 ? 1 : 0);
            Assert.False(s.SkipCondition!(State(wrong)), $"{wrong[i].Key} reads wrong");
            Assert.False(s.SkipCondition!(State(done.Where((_, j) => j != i).ToArray())), $"{done[i].Key} unread");
        }

        Assert.Equal(n == 2 ? new[] { "ES_E2_FUEL_FEED", "ESC_FUEL_PUMP" } : new[] { $"ES_E{n}_FUEL_FEED" },
            s.LinkedChecklistItemIds);
    }

    /// <summary>The generators last: every field relay closed, then every breaker relay. Already set only when all six
    /// read closed; one that does not close does not stop the flow.</summary>
    [Fact]
    public void ES_GENERATORS_skips_when_all_six_relays_read_closed()
    {
        var s = Of("ES_GENERATORS");
        Assert.Equal("ES_GENERATORS", EngineStart.Steps[^1].Id);
        Assert.Equal("Engine generator fields and breakers: closed", s.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
        Assert.Equal(X.EngineGenerators, s.EventName);
        Assert.Equal(1, s.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);

        var closed = GeneratorRelays.Select(k => (k, 1.0)).ToArray();
        Assert.True(s.SkipCondition!(State(closed)), "all six closed: already set");
        Assert.False(s.SkipCondition!(State()), "unread: not already set");
        for (int i = 0; i < closed.Length; i++)
        {
            var open = closed.ToArray();
            open[i] = (open[i].Item1, 0.0);
            Assert.False(s.SkipCondition!(State(open)), $"{closed[i].Item1} open");
            Assert.False(s.SkipCondition!(State(closed.Where((_, j) => j != i).ToArray())), $"{closed[i].Item1} unread");
        }
        Assert.Equal(new[] { "ES_GENERATORS", "ESC_GEN_FIELDS", "ESC_GEN_BREAKERS" }, s.LinkedChecklistItemIds);
    }

    /// <summary>Every step's lines, from the table: a write its action line then the read-back lines it sets; the
    /// release wait and the idle wait the read-back line each delivers; the guard, the waits, the ground start and the
    /// light-off watch nothing.</summary>
    private static readonly Dictionary<string, string[]> Links = BuildLinks();

    private static Dictionary<string, string[]> BuildLinks()
    {
        var links = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ES_ON_GROUND"] = Array.Empty<string>(),
            ["ES_DATA"] = Array.Empty<string>(),
            ["ES_APU_RUNNING"] = Array.Empty<string>(),
            ["ES_APU_BLEED"] = Array.Empty<string>(),
            ["ES_CROSSBLEEDS"] = Array.Empty<string>(),
            ["ES_GENERATORS"] = new[] { "ES_GENERATORS", "ESC_GEN_FIELDS", "ESC_GEN_BREAKERS" },
        };
        foreach (int n in new[] { 2, 1, 3 })
        {
            string e = $"ES_E{n}_";
            if (Prev(n) is int prev)
                links[e + "PREV_ISLN_OFF"] = new[] { e + "PREV_ISLN_OFF", $"ESC_E{prev}_ISLN_OFF" };
            links[e + "FUEL_FEED"] = n == 2 ? new[] { e + "FUEL_FEED", "ESC_FUEL_PUMP" } : new[] { e + "FUEL_FEED" };
            links[e + "ISLN"] = Array.Empty<string>();
            links[e + "GND_START"] = Array.Empty<string>();
            links[e + "N3_20"] = Array.Empty<string>();
            links[e + "FUEL_IGN"] = new[] { e + "FUEL_IGN", $"ESC_E{n}_FUEL_IGN" };
            links[e + "RELEASED"] = new[] { $"ESC_E{n}_RELEASED" };
            links[e + "IDLE"] = new[] { $"ESC_E{n}_RUNNING" };
        }
        return links;
    }

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in EngineStart.Steps)
        {
            Assert.True(Links.TryGetValue(s.Id, out var expected), $"{s.Id} is not in the table");
            Assert.Equal(expected, s.LinkedChecklistItemIds);
            Assert.Equal(expected!.FirstOrDefault(), s.CompletesChecklistItemId);
        }
        Assert.Equal(StepIds.Length, Links.Count);
    }

    [Fact]
    public void EveryChecklistLine_IsDeliveredByOneStepOnly()
    {
        var linked = EngineStart.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    /// <summary>Every field a wait or a step's "Already set" reads is one the evaluator serves: synthetic, or a
    /// registered variable that is polled when it is read on request. An unserved field reads unread for good, and
    /// every wait here stops the flow on it.</summary>
    [Fact]
    public void Every_wait_reads_a_field_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        int n = 0;
        foreach (var field in EngineStart.Steps.Select(s => s.ConditionFieldName).OfType<string>()
                     .Concat(new[] { 1, 2, 3 }.Select(N3)))
        {
            n++;
            if (synthetic.Contains(field)) continue;
            Assert.True(vars.TryGetValue(field, out var def), $"{field} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
            if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.True(polled.Contains(field), $"OnRequest field {field} is not polled");
        }
        Assert.True(n >= 14, $"only {n} fields enumerated");
    }

    // ------------------------------------------------------------------ a run on the fake TriStar

    /// <summary>The fake TriStar as Before Start leaves it: on the ground, the APU running with its bleed on, the three
    /// isolation valves and both crossbleeds on, Preflight's fuel (engine 2's feed for the APU, every tank valve on and
    /// guarded, the other pumps off), every engine cold with its fuel and ignition and ground start off, and every
    /// engine generator relay open.</summary>
    private static FakeL1011Aircraft BeforeStartLeaves()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set("SIM_ON_GROUND", 1);
        ac.Set("L1011_RO_APU_RPM", 100);
        ac.Set("SWITCH_APU_BLEED", 1);
        foreach (var key in new[] { "SWITCH_XBLEED_1", "SWITCH_XBLEED_3", Isolation(1), Isolation(2), Isolation(3) })
            ac.Set(key, 1);
        foreach (var pump in FuelPumps)
            ac.Set(pump, pump == "SWITCH_FUEL_PUMP_2L_1" ? 1 : 0);
        foreach (int n in new[] { 1, 2, 3 })
        {
            ac.Set($"SWITCH_ENG_{n}_TANK_VALVE", 1);
            ac.Set($"GUARD_ENG_{n}_TANK_VALVE", 0);
            ac.Set(N3(n), Cold);
            ac.Set(FuelIgnition(n), 0);
            ac.Set(GroundStartSwitch(n), 0);
        }
        foreach (var relay in GeneratorRelays)
            ac.Set(relay, 0);
        return ac;
    }

    /// <summary>The evaluator over what the fake TriStar reads now.</summary>
    private static L1011FoStateEvaluator StateOf(FakeL1011Aircraft ac)
        => State(ac.Vars.Select(kv => (kv.Key, kv.Value)).ToArray());

    /// <summary>A wait, as the flow runs it: its condition read each poll on the aircraft's clock, until its time-out.</summary>
    private static async Task<bool> WaitOn(FakeL1011Aircraft ac, Step step)
    {
        for (int ms = 0; ; ms += X.PollMs)
        {
            if (step.Condition!(StateOf(ac).GetValue(step.ConditionFieldName!)))
                return true;
            if (ms >= step.TimeoutSeconds * 1000)
                return false;
            await ac.DelayAsync(X.PollMs);
        }
    }

    /// <summary>
    /// The steps run in order on the fake TriStar as Before Start leaves it: each "Already set" read from what the
    /// aircraft shows, each wait polled on its clock, each write through the executor with a flow's Stop. Every step is
    /// done or already set, and the aircraft ends with engines 2, 1 and 3 started in that order and at idle, each
    /// ground start released by the aircraft itself, engines 2 and 1's isolation valves off, and the generators on line.
    /// </summary>
    [Fact]
    public async Task A_run_on_the_fake_TriStar_starts_engines_two_one_and_three_and_puts_the_generators_on_line()
    {
        var ac = BeforeStartLeaves();
        var exec = new X();
        exec.SetTransport(ac);
        using var stop = new CancellationTokenSource();
        var alreadySet = new List<string>();

        foreach (var step in EngineStart.Steps)
        {
            if (step.SkipCondition?.Invoke(StateOf(ac)) == true)
            {
                alreadySet.Add(step.Id);
                continue;
            }
            bool done = step.ActionType == FlowStepActionType.WaitForCondition
                ? await WaitOn(ac, step).WaitAsync(Hang)
                : await exec.ExecuteStepAsync(step, stop.Token).WaitAsync(Hang);
            Assert.True(done, $"{step.Id} was not done");
        }

        // Engine 2's feed is Preflight's (the APU's pump and engine 2's tank valve): already set.
        Assert.Equal(new[] { "ES_E2_FUEL_FEED" }, alreadySet);

        int[] Order(string prefix) => ac.Writes.Where(w => w.Key.StartsWith(prefix, StringComparison.Ordinal) && w.Value == 1)
            .Select(w => int.Parse(w.Key[^1..], System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        Assert.Equal(new[] { 2, 1, 3 }, Order("SWITCH_GND_START_ENG_"));
        Assert.Equal(new[] { 2, 1, 3 }, Order("TOGGLE_CUTOFF_ENG_"));
        Assert.DoesNotContain(ac.Writes, w => w.Key.StartsWith("SWITCH_GND_START_ENG_", StringComparison.Ordinal) && w.Value == 0);

        int WriteIndex(string key, double value) => ac.Writes.IndexOf((key, value));
        Assert.True(WriteIndex(Isolation(2), 0) < WriteIndex(GroundStartSwitch(1), 1), "engine 2's isolation valve off before engine 1 starts");
        Assert.True(WriteIndex(Isolation(1), 0) < WriteIndex(GroundStartSwitch(3), 1), "engine 1's isolation valve off before engine 3 starts");

        foreach (int n in new[] { 1, 2, 3 })
        {
            Assert.True(ac.Get(N3(n)) >= L1011FoActionExecutor.IdleN3, $"engine {n} at idle");
            Assert.Equal(1, ac.Get(FuelIgnition(n)));
            Assert.Equal(0, ac.Get(GroundStartSwitch(n)));
        }
        Assert.Equal(0, ac.Get(Isolation(1)));
        Assert.Equal(0, ac.Get(Isolation(2)));
        Assert.Equal(1, ac.Get(Isolation(3)));
        Assert.All(GeneratorRelays, relay => Assert.Equal(1, ac.Get(relay)));
        Assert.Equal(1, StateOf(ac).GetValue("FO_ENGINE_GENERATORS_ON"));
    }

    /// <summary>
    /// The fuel and ignition step stops the flow when it is not done: from an unread position its toggle is refused
    /// ([L1011-4]) and nothing is sent, so the flow stops with the ground start on. The flow's Stop clean-up then
    /// releases that ground start once the fuel and ignition read off, never by a fuel write.
    /// </summary>
    [Fact]
    public async Task A_refused_fuel_and_ignition_stops_the_flow_and_the_Stop_releases_the_ground_start_over_fuel_off()
    {
        Assert.Equal(FlowStepFailurePolicy.Stop, Of("ES_E2_FUEL_IGN").FailurePolicy);
        var ac = BeforeStartLeaves();
        ac.Unset(FuelIgnition(2));
        var exec = new X();
        exec.SetTransport(ac);
        using var stop = new CancellationTokenSource();

        Assert.True(await exec.ExecuteStepAsync(Of("ES_E2_GND_START"), stop.Token).WaitAsync(Hang));
        Assert.True(await exec.ExecuteStepAsync(Of("ES_E2_N3_20"), stop.Token).WaitAsync(Hang));
        Assert.False(await exec.ExecuteStepAsync(Of("ES_E2_FUEL_IGN"), stop.Token).WaitAsync(Hang), "unread: refused");
        Assert.DoesNotContain(ac.Writes, w => w.Key == FuelIgnition(2));
        Assert.Equal(1, ac.Get(GroundStartSwitch(2)));

        // The flow stops; the fuel and ignition now read off.
        stop.Cancel();
        ac.Set(FuelIgnition(2), 0);
        await exec.OnFlowStoppedAsync().WaitAsync(Hang);

        Assert.Contains((GroundStartSwitch(2), 0.0), ac.Writes);
        Assert.Equal(0, ac.Get(GroundStartSwitch(2)));
        Assert.DoesNotContain(ac.Writes, w => w.Key == FuelIgnition(2));
    }

    // ================================================================== the checklists

    [Fact]
    public void TheEngineStartGroups_FollowBeforeStart_Named()
    {
        Assert.Equal(new[] { "ENGINE_START", "ENGINE_START_CL" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "ENGINE_START").Take(2));
        Assert.Equal("BEFORE_START_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "ENGINE_START").Last());
        Assert.Equal("Engine Start", Group("ENGINE_START").Name);
        Assert.Equal("Engine Start", Group("ENGINE_START_CL").Name);
    }

    [Fact]
    public void EveryEngineStartGroup_IsDeparture_AndNeverArrival()
    {
        foreach (var id in EngineStart.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Departure.Contains(id), $"{id} is not a departure group");
            Assert.False(FoDepartureGroups.Arrival.Contains(id), $"{id} is an arrival group too");
        }
    }

    [Fact]
    public void The_read_back_has_no_check_action()
        => Assert.All(Group("ENGINE_START_CL").Items, i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
    {
        "ES_E2_FUEL_FEED", "ES_E2_GND_START", "ES_E2_FUEL_IGN",
        "ES_E1_PREV_ISLN_OFF", "ES_E1_FUEL_FEED", "ES_E1_GND_START", "ES_E1_FUEL_IGN",
        "ES_E3_PREV_ISLN_OFF", "ES_E3_FUEL_FEED", "ES_E3_GND_START", "ES_E3_FUEL_IGN",
        "ES_GENERATORS",
    };

    private static readonly string[] GroundStartLines = { "ES_E2_GND_START", "ES_E1_GND_START", "ES_E3_GND_START" };

    [Fact]
    public void EngineStart_ActionLines_InTheFlowsOrder()
        => Assert.Equal(ActionLineIds, Group("ENGINE_START").Items.Select(i => i.Id));

    /// <summary>Ticked or run, a line says the same thing.</summary>
    [Fact]
    public void EngineStart_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("ENGINE_START").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    /// <summary>Every line is a state line with a tick that does its step's write; the ground start lines latch on the
    /// engine running (N3 at 50), every other line mirrors the aircraft. None is left alone: nothing here cuts.</summary>
    [Fact]
    public void EngineStart_LineKinds()
    {
        foreach (var i in Group("ENGINE_START").Items)
        {
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Empty(i.AdditionalStateFields);
            Assert.Null(i.LeaveAloneWhen);
            if (GroundStartLines.Contains(i.Id))
            {
                Assert.Equal(RevertBehavior.StayComplete, i.RevertBehavior);
                Assert.Equal(N3(int.Parse(i.Id["ES_E".Length..][..1], System.Globalization.CultureInfo.InvariantCulture)), i.StateFieldName);
            }
            else
                Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
        }
    }

    /// <summary>Each mirroring action line reads the state its step delivers: a single switch its key, a multi switch
    /// its synthetic, the generators the six relays.</summary>
    [Theory]
    [InlineData("ES_E2_FUEL_FEED", "FO_ENG2_FUEL_FEED", 1)]
    [InlineData("ES_E2_FUEL_IGN", "TOGGLE_CUTOFF_ENG_2", 1)]
    [InlineData("ES_E1_PREV_ISLN_OFF", "SWITCH_ISLN_VALVE_ENG_2", 0)]
    [InlineData("ES_E1_FUEL_FEED", "FO_ENG1_FUEL_FEED", 1)]
    [InlineData("ES_E1_FUEL_IGN", "TOGGLE_CUTOFF_ENG_1", 1)]
    [InlineData("ES_E3_PREV_ISLN_OFF", "SWITCH_ISLN_VALVE_ENG_1", 0)]
    [InlineData("ES_E3_FUEL_FEED", "FO_ENG3_FUEL_FEED", 1)]
    [InlineData("ES_E3_FUEL_IGN", "TOGGLE_CUTOFF_ENG_3", 1)]
    [InlineData("ES_GENERATORS", "FO_ENGINE_GENERATORS_ON", 1)]
    public void EngineStart_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
    {
        var line = Line(id);
        Assert.Equal(field, line.StateFieldName);
        Assert.True(line.StateCondition!(done), $"{id}: {field} at {done} must read done");
        Assert.False(line.StateCondition!(double.NaN), $"{id}: {field} unread must not read done");
        var others = L1011FoStateEvaluator.SyntheticKeys.Contains(field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4 };
        foreach (double other in others.Where(v => v != done))
            Assert.False(line.StateCondition!(other), $"{id}: {field} at {other} must not read done");
    }

    /// <summary>[FOB-1]: in the whole TriStar profile, the three ground start lines are the only StayComplete state
    /// lines.</summary>
    [Fact]
    public void Only_the_three_ground_start_lines_are_StayComplete()
        => Assert.Equal(new[] { "ES_E1_GND_START", "ES_E2_GND_START", "ES_E3_GND_START" },
            Groups.SelectMany(g => g.Items)
                .Where(i => i.IsAutoDetectable && i.RevertBehavior == RevertBehavior.StayComplete)
                .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal));

    // ---- a hand tick does what the flow step does ----------------------------------------------------------------

    /// <summary>An aircraft with work for the step: every engine cold with its fuel and ignition and ground start off,
    /// each key the step writes the other way from its target, and for the generators every engine at idle and every
    /// relay open.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (int n in new[] { 1, 2, 3 })
        {
            ac.Set(N3(n), Cold);
            ac.Set(FuelIgnition(n), 0);
            ac.Set(GroundStartSwitch(n), 0);
        }
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, target == 0 ? 1 : 0);
        if (step.EventName == X.EngineGenerators)
        {
            foreach (int n in new[] { 1, 2, 3 })
                ac.Set(N3(n), Idle);
            foreach (var relay in GeneratorRelays)
                ac.Set(relay, 0);
        }
        return ac;
    }

    public static IEnumerable<object[]> EngineStartLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "ENGINE_START").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft: the
    /// ground start lines included (a tick's start is watched as the step's is).</summary>
    [Theory]
    [MemberData(nameof(EngineStartLinesWithACheckAction))]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var step = Of(id);
        var byStep = AircraftFor(step);
        var stepExec = new X();
        stepExec.SetTransport(byStep);
        Assert.True(await stepExec.ExecuteStepAsync(step).WaitAsync(Hang), $"{id}: the flow step was not done");

        var byTick = AircraftFor(step);
        var tickExec = new X();
        tickExec.SetTransport(byTick);
        await Line(id).CheckAction!(tickExec, State()).WaitAsync(Hang);

        static List<(FakeL1011IoKind, string, double?)> Sent(FakeL1011Aircraft ac) =>
            ac.Io.Where(e => e.Kind != FakeL1011IoKind.FreshRead).Select(e => (e.Kind, e.Key, e.Value)).ToList();
        Assert.NotEmpty(Sent(byStep));
        Assert.Equal(Sent(byStep), Sent(byTick));
    }

    // ---- the read-back, word for word ------------------------------------------------------------------------------

    private sealed record ReadBack(string Id, string Label, string Field, double Done);

    private static readonly ReadBack[] EngineStartReadBack =
    {
        new("ESC_FUEL_PUMP", "Fuel pump: minimum one on", "FO_ANY_FUEL_PUMP_ON", 1),
        new("ESC_E2_FUEL_IGN", "Engine 2 at 20 percent N3: fuel and ignition on", "TOGGLE_CUTOFF_ENG_2", 1),
        new("ESC_E2_RELEASED", "Engine 2 at 55 percent N3: ground start switch released", "SWITCH_GND_START_ENG_2", 0),
        new("ESC_E2_RUNNING", "Engine 2: running", "FO_ENG2_RUNNING", 1),
        new("ESC_E2_ISLN_OFF", "Engine 2 isln valve: unlatch", "SWITCH_ISLN_VALVE_ENG_2", 0),
        new("ESC_E1_FUEL_IGN", "Engine 1 at 20 percent N3: fuel and ignition on", "TOGGLE_CUTOFF_ENG_1", 1),
        new("ESC_E1_RELEASED", "Engine 1 at 55 percent N3: ground start switch released", "SWITCH_GND_START_ENG_1", 0),
        new("ESC_E1_RUNNING", "Engine 1: running", "FO_ENG1_RUNNING", 1),
        new("ESC_E1_ISLN_OFF", "Engine 1 isln valve: unlatch", "SWITCH_ISLN_VALVE_ENG_1", 0),
        new("ESC_E3_FUEL_IGN", "Engine 3 at 20 percent N3: fuel and ignition on", "TOGGLE_CUTOFF_ENG_3", 1),
        new("ESC_E3_RELEASED", "Engine 3 at 55 percent N3: ground start switch released", "SWITCH_GND_START_ENG_3", 0),
        new("ESC_E3_RUNNING", "Engine 3: running", "FO_ENG3_RUNNING", 1),
        new("ESC_GEN_FIELDS", "Engine generator field: close", "FO_GEN_FIELDS_CLOSED", 1),
        new("ESC_GEN_BREAKERS", "Engine generator breaker: close", "FO_GEN_BREAKERS_CLOSED", 1),
    };

    [Fact]
    public void The_read_back_is_word_for_word()
        => Assert.Equal(EngineStartReadBack.Select(l => (l.Id, l.Label)), Group("ENGINE_START_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in EngineStartReadBack)
        {
            var line = Line(rb.Id);
            Assert.Null(line.CheckAction);
            Assert.Empty(line.AdditionalStateFields);
            Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
            Assert.Equal(RevertBehavior.RevertToState, line.RevertBehavior);
            Assert.Equal(rb.Field, line.StateFieldName);
            Assert.True(line.StateCondition!(rb.Done), $"{rb.Id}: {rb.Field} at {rb.Done} must read done");
            Assert.False(line.StateCondition!(double.NaN), $"{rb.Id}: unread must not read done");
            var others = L1011FoStateEvaluator.SyntheticKeys.Contains(rb.Field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4 };
            foreach (double other in others.Where(v => v != rb.Done))
                Assert.False(line.StateCondition!(other), $"{rb.Id}: {rb.Field} at {other} must not read done");
        }
    }

    /// <summary>A finished run latches the action group and the read-back, and nothing else.</summary>
    [Fact]
    public void EngineStart_LatchesItsTwoGroups()
        => Assert.Equal(EngineStart.RelatedChecklistGroupIds, EngineStart.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The spoken labels with an engine number or a percentage read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_hold_under_a_comma_decimal_culture(string culture)
    {
        var labels = TestCulture.Run(culture, () =>
            L1011FoFlowDefinitions.Build().Single(f => f.Id == "ENGINE_START").Steps.Select(s => s.Label)
                .Concat(L1011FoChecklistDefinitions.Build().Where(g => g.Id.StartsWith("ENGINE_START", StringComparison.Ordinal))
                    .SelectMany(g => g.Items).Select(i => i.Label))
                .ToList());
        Assert.Contains("Engine 2 N3 twenty percent", labels);
        Assert.Contains("Engine 2 isolation valve: off", labels);
        Assert.Contains("Engine 1 at 20 percent N3: fuel and ignition on", labels);
        Assert.Contains("Engine 3 at 55 percent N3: ground start switch released", labels);
        Assert.Contains("Engine 3 fuel: tank pump and tank valve on", labels);
    }

    // ================================================================== what Engine Start writes

    private static readonly Lazy<IniL1011Definition> Definition = new(() =>
    {
        var def = new IniL1011Definition();
        def.GetVariables();
        return def;
    });

    private static readonly Lazy<Dictionary<string, L1011PlacedRow>> Rows = new(() =>
    {
        var rows = new Dictionary<string, L1011PlacedRow>(StringComparer.Ordinal);
        foreach (var panel in L1011PanelLayout.Place(L1011ControlMap.Load(), L1011Levers.Keys).RowsByPanel.Values)
            foreach (var row in panel)
                rows[row.Key] = row;
        return rows;
    });

    private static IEnumerable<(string Step, string Key, int Target)> EveryWrittenKey()
        => EngineStart.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(24, EveryWrittenKey().Count());
    }

    /// <summary>The composites are the executor's engine start keys, and no lever is written.</summary>
    [Fact]
    public void TheCompositesArePseudoKeys_AndNoLeverIsWritten()
    {
        var composites = new[] { 1, 2, 3 }.SelectMany(n => new[] { X.GroundStart(n), X.EngineLightOff(n), X.EngineIdle(n) })
            .Append(X.EngineGenerators);
        Assert.Equal(composites.OrderBy(k => k, StringComparer.Ordinal),
            EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct().OrderBy(k => k, StringComparer.Ordinal));
        Assert.DoesNotContain(EveryWrittenKey(), w => L1011Levers.IsLever(w.Key));
    }

    /// <summary>Every other key is a plain switch row, and its target one of the control's positions.</summary>
    [Fact]
    public void EveryOtherKey_IsAPlainSwitchRow_AtOneOfItsPositions()
    {
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key)))
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target, current: target == 0 ? 1 : 0);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused ({plan.Refusal})");
        }
    }

    /// <summary>The fuel and ignition switches' click replay carries a TOGGLE K: event, refused from an unknown
    /// position ([L1011-4]).</summary>
    private static readonly string[] ToggleCarrying = { "TOGGLE_CUTOFF_ENG_1", "TOGGLE_CUTOFF_ENG_2", "TOGGLE_CUTOFF_ENG_3" };

    /// <summary>
    /// A toggle-carrying key is written only by a single-switch step whose "Already set" reads that key at the target:
    /// the executor reads it first, and from an unknown position the definition refuses the write, which stops the
    /// flow. Planned from nothing read, every other key must still be a plan.
    /// </summary>
    [Fact]
    public void No_step_writes_a_toggle_carrying_key_from_an_unread_position_except_through_a_skip()
    {
        var toggles = new List<string>();
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key)))
        {
            var plan = L1011WritePlan.ForSet(Rows.Value[key].Control!, target, current: null);
            Assert.NotEqual(L1011WritePlan.NotAPositionRefusal, plan.Refusal);
            if (plan.Refusal is null) continue;
            Assert.Equal(L1011WritePlan.UnknownPositionRefusal, plan.Refusal);
            toggles.Add(key);

            var s = Of(step);
            Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
            int other = target == 0 ? 1 : 0;
            Assert.True(s.SkipCondition!(State((key, target))), $"{step}: {key} at its target must be already set");
            Assert.False(s.SkipCondition!(State((key, other))), $"{step}: {key} the other way must not be already set");
            Assert.False(s.SkipCondition!(State()), $"{step}: {key} unread must not be already set");
        }
        Assert.Equal(ToggleCarrying, toggles.OrderBy(k => k, StringComparer.Ordinal));
    }
}
