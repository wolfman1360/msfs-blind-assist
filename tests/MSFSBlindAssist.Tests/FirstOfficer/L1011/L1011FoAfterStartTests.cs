using System.Globalization;
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
/// The TriStar First Officer's After Start flow, its action group and its two read-backs (the aircraft's after start
/// page, with the engine start page's APU shutdown lines, and its taxi page), pinned by structure and by behaviour:
/// the step ids and their order; every write's label, keys and targets word for word, with its "Already set" on its
/// own state; the ATMs to on, then to auto; the read-only checks, each completing only lines that read its field; the
/// take-off flaps from the tablet, else SimBrief, else the Captain ([FO-20]), left alone off the ground; the 1A and 3A
/// transfers only with fuel in both tanks; the stabilizer trim reminder naming the tablet's value; the APU shutdown
/// only once every engine generator is on line, its cool-down only after this run's stop; the lines each step delivers
/// ([FO-6], [FO-7]); and that every key is one the definition takes, a toggle-carrying key only ever written over its
/// own position read ([L1011-4]).
/// </summary>
public class L1011FoAfterStartTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> AfterStart => Flows.Single(f => f.Id == "AFTER_START");

    private static Step Of(string id) => AfterStart.Steps.Single(s => s.Id == id);
    private static ChecklistGroup<X, L1011FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);
    private static Item Line(string id) => Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    private static bool IsSwitch(Step s) =>
        s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple;

    /// <summary>The (key, target) pairs a switch step writes; a provider step's target is null.</summary>
    private static (string Key, int? Target)[] Actions(Step s) => s.ActionType switch
    {
        FlowStepActionType.SetSwitch => new[] { (s.EventName!, s.TargetValue) },
        FlowStepActionType.SetSwitchMultiple => s.MultiActions.Select(a => (a.EventName, a.TargetValue)).ToArray(),
        _ => Array.Empty<(string, int?)>(),
    };

    private static (string, int?)[] All(int target, params string[] keys) => keys.Select(k => (k, (int?)target)).ToArray();

    private const string TabletFlaps = IniL1011Definition.PerfSelectedFlapsKey;
    private const string TabletStabTrim = IniL1011Definition.PerfStabTrimKey;
    private const string Tank1A = "L1011_RO_TANK_4";
    private const string Tank3A = "L1011_RO_TANK_5";
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuGenField = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";
    private const string Ground = "SIM_ON_GROUND";

    private const string FlapsText = "Flaps left as they are: not read on the ground.";
    private const string TransfersText = "1A and 3A transfers left off: tanks 1A and 3A are empty or unread.";
    private const string GeneratorsText = "The engine generators are not all on line, so the APU stays running.";

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

    private static L1011FoStateEvaluator Connected(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.ConnectedWith(values));
        return evaluator;
    }

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void AfterStart_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("After Start", AfterStart.Name);
        Assert.Equal(new[] { "AFTER_START", "AFTER_START_CL", "TAXI_CL" }, AfterStart.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(AfterStart.Description));
    }

    private static readonly string[] StepIds =
    {
        "AS_HI_PRESS", "AS_ISLN_VALVES", "AS_APU_AIR", "AS_PACKS_ON", "AS_ATMS_ON", "AS_ATMS_AUTO", "AS_GALLEY_ON",
        "AS_CHK_GENERATORS", "AS_CHK_IDG", "AS_CHK_BUS_FAIL", "AS_AC_METER", "AS_DC_METER", "AS_CHK_AC_ESS",
        "AS_FUEL_PUMPS", "AS_CROSSFEEDS", "AS_TRANSFERS", "AS_PITOT_HEAT", "AS_IGNITION_BOTH", "AS_FLAPS", "AS_FLAPS_CPT",
        "AS_TAXI_LIGHT", "AS_CPT_STAB_TRIM", "AS_CPT_ANTI_ICE", "AS_CPT_CONTROLS",
        "AS_GENS_ON_LINE", "AS_APU_GEN_TRIP", "AS_APU_BLEED_OFF", "AS_APU_STOP", "AS_APU_COOLDOWN", "AS_APU_MASTER_OFF",
    };

    [Fact]
    public void AfterStart_StepIdsAreInOrder()
    {
        Assert.Equal(30, StepIds.Length);
        Assert.Equal(StepIds, AfterStart.Steps.Select(s => s.Id));
    }

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("AS_HI_PRESS", "High pressure bleed valves: on", All(1, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3")),
        ("AS_ISLN_VALVES", "Engine isolation valves: on", All(1, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3")),
        ("AS_APU_AIR", "Fuselage isolation and crossbleed valves: on", All(1, "SWITCH_APU_ISLN_VALVE", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3")),
        ("AS_PACKS_ON", "Packs: on", All(1, "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3")),
        ("AS_ATMS_ON", "ATM switches: on", All(2, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE")),
        ("AS_ATMS_AUTO", "ATM switches: auto", All(1, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE")),
        ("AS_GALLEY_ON", "Galley power: on", All(1, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3")),
        ("AS_AC_METER", "AC meter: generator 3", All(3, "ROTARY_PWR_SOURCE")),
        ("AS_DC_METER", "DC meter: battery", All(4, "ROTARY_STBY_DC_BUS")),
        ("AS_FUEL_PUMPS", "Fuel pumps: all on", All(1, FuelPumps)),
        ("AS_CROSSFEEDS", "Crossfeed valves: closed", All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED")),
        ("AS_TRANSFERS", "1A and 3A transfers: on", All(1, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
        ("AS_PITOT_HEAT", "Pitot and alpha probe heat: on", All(1, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_ALPHA_HEAT")),
        ("AS_IGNITION_BOTH", "Ignition selector: both", All(1, "ROTARY_ENG_START_SEL")),
        // The provider's step: its target is resolved when the flow runs.
        ("AS_FLAPS", "Flaps: take-off setting", new (string, int?)[] { (L1011Levers.FlapHandleKey, null) }),
        ("AS_TAXI_LIGHT", "Nose taxi light: on", All(1, "TOGGLE_TAXI_LIGHT")),
        ("AS_APU_GEN_TRIP", "APU generator breaker: trip", All(0, X.ApuGenerator)),
        ("AS_APU_BLEED_OFF", "APU bleed: off", All(0, "SWITCH_APU_BLEED")),
        ("AS_APU_STOP", "APU: stop", All(1, "APU_STOP")),
        ("AS_APU_MASTER_OFF", "APU master: off", All(0, "TOGGLE_APU_POWER")),
    };

    [Fact]
    public void TheWrites_HaveTheirLabelKeysAndTargetsWordForWord()
    {
        foreach (var (id, label, actions) in Writes)
        {
            var s = Of(id);
            Assert.Equal(label, s.Label);
            Assert.Equal(actions, Actions(s));
            Assert.Equal(actions.Length == 1 ? FlowStepActionType.SetSwitch : FlowStepActionType.SetSwitchMultiple, s.ActionType);
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.Equal(id == "AS_FLAPS", s.TargetValueProvider != null);
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            AfterStart.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Every write says "Already set" on its own state but the ATMs to on: their position on the way to auto
    /// is the point of the step, so it always runs.</summary>
    [Fact]
    public void Every_write_but_the_ATMs_on_has_a_skip_condition()
    {
        foreach (var s in AfterStart.Steps.Where(IsSwitch))
            Assert.True((s.SkipCondition != null) == (s.Id != "AS_ATMS_ON"), $"{s.Id}");
    }

    /// <summary>Every step's lines, from the table: a write its action line then the read-back lines it sets, a check
    /// the read-back lines it reads, an unconditional Captain step its reminder line; the conditional flaps prompt,
    /// the generator wait and the cool-down link nothing.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["AS_HI_PRESS"] = new[] { "AS_HI_PRESS", "ASC_HI_PRESS" },
        ["AS_ISLN_VALVES"] = new[] { "AS_ISLN_VALVES", "ASC_ISLN_VALVES" },
        ["AS_APU_AIR"] = new[] { "AS_APU_AIR", "ASC_FUSELAGE_ISLN", "ASC_XBLEEDS" },
        ["AS_PACKS_ON"] = new[] { "AS_PACKS_ON", "ASC_PACKS" },
        ["AS_ATMS_ON"] = new[] { "AS_ATMS_ON" },
        ["AS_ATMS_AUTO"] = new[] { "AS_ATMS_AUTO", "ASC_ATMS" },
        ["AS_GALLEY_ON"] = new[] { "AS_GALLEY_ON", "ASC_GALLEY" },
        ["AS_CHK_GENERATORS"] = new[] { "ASC_GEN_FIELDS", "ASC_GEN_BREAKERS" },
        ["AS_CHK_IDG"] = new[] { "ASC_IDG" },
        ["AS_CHK_BUS_FAIL"] = new[] { "ASC_BUS_FAIL" },
        ["AS_AC_METER"] = new[] { "AS_AC_METER", "ASC_AC_METER" },
        ["AS_DC_METER"] = new[] { "AS_DC_METER", "ASC_DC_METER" },
        ["AS_CHK_AC_ESS"] = new[] { "ASC_AC_ESS" },
        ["AS_FUEL_PUMPS"] = new[] { "AS_FUEL_PUMPS", "ASC_FUEL_PUMPS" },
        ["AS_CROSSFEEDS"] = new[] { "AS_CROSSFEEDS", "ASC_CROSSFEEDS" },
        ["AS_TRANSFERS"] = new[] { "AS_TRANSFERS", "ASC_TRANSFERS" },
        ["AS_PITOT_HEAT"] = new[] { "AS_PITOT_HEAT", "ASC_PITOT_HEAT", "ASC_ALPHA_HEAT" },
        ["AS_IGNITION_BOTH"] = new[] { "AS_IGNITION_BOTH", "ASC_IGNITION" },
        ["AS_FLAPS"] = new[] { "AS_FLAPS", "ASC_FLAPS" },
        ["AS_FLAPS_CPT"] = Array.Empty<string>(),
        ["AS_TAXI_LIGHT"] = new[] { "AS_TAXI_LIGHT", "TXC_TAXI_LIGHT" },
        ["AS_CPT_STAB_TRIM"] = new[] { "AS_CPT_STAB_TRIM" },
        ["AS_CPT_ANTI_ICE"] = new[] { "AS_CPT_ANTI_ICE" },
        ["AS_CPT_CONTROLS"] = new[] { "AS_CPT_CONTROLS" },
        ["AS_GENS_ON_LINE"] = Array.Empty<string>(),
        ["AS_APU_GEN_TRIP"] = new[] { "AS_APU_GEN_TRIP", "ASC_APU_GEN_TRIP" },
        ["AS_APU_BLEED_OFF"] = new[] { "AS_APU_BLEED_OFF", "ASC_APU_BLEED" },
        ["AS_APU_STOP"] = new[] { "AS_APU_STOP", "ASC_APU_STOP" },
        ["AS_APU_COOLDOWN"] = Array.Empty<string>(),
        ["AS_APU_MASTER_OFF"] = new[] { "AS_APU_MASTER_OFF", "ASC_APU_MASTER" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in AfterStart.Steps)
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
        var linked = AfterStart.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    /// <summary>What the aircraft reads when a write's work is done, each reading with a value that is not done: the
    /// APU generator's breaker open after its trip, the APU stopped after its stop press, the flap handle at the
    /// tablet's take-off index; otherwise the step's keys at their targets.</summary>
    private static (string Key, double Done, double Wrong)[] Done(Step s)
    {
        if (s.EventName == X.ApuGenerator) return new[] { (ApuGenBreaker, 0.0, 1.0) };
        if (s.EventName == "APU_STOP") return new[] { (ApuRpm, 0.0, 100.0) };
        if (s.EventName == L1011Levers.FlapHandleKey) return new[] { (s.EventName, 2.0, 0.0), (TabletFlaps, 10.0, 0.0) };
        return Actions(s).Select(a => (a.Key, (double)a.Target!.Value, a.Target.Value == 0 ? 1.0 : 0.0)).ToArray();
    }

    [Fact]
    public void EveryWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in AfterStart.Steps.Where(x => IsSwitch(x) && x.SkipCondition != null))
        {
            n++;
            var done = Done(s);
            Assert.True(s.SkipCondition!(State(done.Select(d => (d.Key, d.Done)).ToArray())), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.Select((d, j) => (d.Key, j == i ? d.Wrong : d.Done)).ToArray();
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {done[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).Select(d => (d.Key, d.Done)).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length - 1, n);
    }

    /// <summary>The APU's stop is already done once its speed reads below 5 percent, whatever the spool-down passes on
    /// the way; the generator's trip once its breaker relay reads open.</summary>
    [Fact]
    public void The_APU_stop_and_trip_are_already_set_on_the_APU_s_own_readings()
    {
        var stop = Of("AS_APU_STOP").SkipCondition!;
        Assert.True(stop(State((ApuRpm, 4.9))));
        Assert.False(stop(State((ApuRpm, 5))));
        Assert.False(stop(State((ApuRpm, 60))));
        var trip = Of("AS_APU_GEN_TRIP").SkipCondition!;
        Assert.True(trip(State((ApuGenBreaker, 0), (ApuGenField, 1))), "the breaker open, the field closed: tripped");
        Assert.False(trip(State((ApuGenBreaker, 1), (ApuGenField, 1))));
    }

    // ------------------------------------------------------------------ the ATMs: on, then auto

    /// <summary>The manual turns the ATMs on, then to auto. Their on position is undone by the next step, so its line
    /// reads no state: ticked when its step completes, a hand tick doing its write.</summary>
    [Fact]
    public void The_ATMs_go_on_then_auto()
    {
        var ids = AfterStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf("AS_ATMS_ON") + 1, ids.IndexOf("AS_ATMS_AUTO"));

        var on = Line("AS_ATMS_ON");
        Assert.Equal(ChecklistItemType.Actionable, on.Type);
        Assert.Null(on.StateFieldName);
        Assert.NotNull(on.CheckAction);
        Assert.True(on.ManualCompletionAllowed);

        var auto = Of("AS_ATMS_AUTO");
        Assert.True(auto.SkipCondition!(State(("ROTARY_HYD_B2_MODE", 1), ("ROTARY_HYD_C2_MODE", 1))));
        Assert.False(auto.SkipCondition!(State(("ROTARY_HYD_B2_MODE", 2), ("ROTARY_HYD_C2_MODE", 2))), "on is not auto");
        Assert.Equal("FO_ATMS_AUTO", Line("AS_ATMS_AUTO").StateFieldName);
    }

    // ------------------------------------------------------------------ the read-only checks

    private static readonly (string Id, string Label, string Field, string[] Lines)[] Checks =
    {
        ("AS_CHK_GENERATORS", "Engine generators: on line", "FO_ENGINE_GENERATORS_ON", new[] { "ASC_GEN_FIELDS", "ASC_GEN_BREAKERS" }),
        ("AS_CHK_IDG", "IDG low pressure lights: out", "FO_IDG_LIGHTS_OUT", new[] { "ASC_IDG" }),
        ("AS_CHK_BUS_FAIL", "Bus fail lights: out", "FO_BUS_FAIL_LIGHTS_OUT", new[] { "ASC_BUS_FAIL" }),
        ("AS_CHK_AC_ESS", "AC essential power selector: bus 3, generator 1", "ROTARY_AC_ESS_BUS", new[] { "ASC_AC_ESS" }),
    };

    [Fact]
    public void TheChecks_AreReadOnlyWaitsOfFiveSecondsThatSkipAloud()
    {
        Assert.Equal(Checks.Select(c => c.Id), AfterStart.Steps.Where(s => s.Id.Contains("_CHK_", StringComparison.Ordinal)).Select(s => s.Id));
        foreach (var (id, label, field, _) in Checks)
        {
            var s = Of(id);
            double done = id == "AS_CHK_AC_ESS" ? 4 : 1;
            Assert.Equal(label, s.Label);
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal(field, s.ConditionFieldName);
            Assert.Equal(5, s.TimeoutSeconds);
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.False(s.Condition!(double.NaN), $"{id}: an unread state must not pass");
            Assert.False(s.Condition!(0), $"{id}: 0 must not pass");
            Assert.True(s.Condition!(done), $"{id}: {done} must pass");
            Assert.False(s.SkipCondition!(State()), $"{id}: unread must not be already set");
        }

        // Already set on the aircraft's own readings: six relays closed, three IDG pressures up, four bus fail lights
        // out, the essential selector normal.
        var shown = State(GeneratorRelays.Select(r => (r, 1.0))
            .Concat(new[] { 1, 2, 3 }.Select(n => ($"LAMP_IDG_OIL_PRESSURE_{n}", 60.0)))
            .Concat(new[] { "AC_ESS", "AC_STBY", "DC_ESS", "DC_STBY" }.Select(b => ($"LAMP_{b}_FAIL_LIGHT", 0.0)))
            .Append(("ROTARY_AC_ESS_BUS", 4.0)).ToArray());
        Assert.All(Checks, c => Assert.True(Of(c.Id).SkipCondition!(shown), $"{c.Id}: the aircraft shows it, must be already set"));
        var lowIdg = State(new[] { 1, 2, 3 }.Select(n => ($"LAMP_IDG_OIL_PRESSURE_{n}", n == 2 ? 30.0 : 60.0)).ToArray());
        Assert.False(Of("AS_CHK_IDG").SkipCondition!(lowIdg), "IDG 2 below 40 psi: its light is lit");
    }

    /// <summary>
    /// A read-only check completes only lines that read its own field under its own rule, or, for the generator check,
    /// the two lines its field is made of: all six relays closed is the three fields closed and the three breakers
    /// closed, and one relay open or unread is neither all of them.
    /// </summary>
    [Fact]
    public void Every_check_completes_only_lines_reading_its_field()
    {
        foreach (var (id, _, field, lines) in Checks)
        {
            var s = Of(id);
            Assert.Equal(lines, s.LinkedChecklistItemIds);
            foreach (var lineId in lines)
            {
                var line = Line(lineId);
                Assert.Equal("AFTER_START_CL", line.GroupId);
                if (id == "AS_CHK_GENERATORS")
                    continue;
                Assert.Equal(field, line.StateFieldName);
                foreach (double v in new[] { double.NaN, 0, 1, 2, 3, 4, 5 })
                    Assert.Equal(s.Condition!(v), line.StateCondition!(v));
            }
        }

        var check = Of("AS_CHK_GENERATORS");
        Assert.Equal("FO_GEN_FIELDS_CLOSED", Line("ASC_GEN_FIELDS").StateFieldName);
        Assert.Equal("FO_GEN_BREAKERS_CLOSED", Line("ASC_GEN_BREAKERS").StateFieldName);
        bool Reads(L1011FoStateEvaluator st, string lineId) => Line(lineId).StateCondition!(st.GetValue(Line(lineId).StateFieldName!));
        var allClosed = State(GeneratorRelays.Select(r => (r, 1.0)).ToArray());
        Assert.True(check.Condition!(allClosed.GetValue(check.ConditionFieldName!)));
        Assert.True(Reads(allClosed, "ASC_GEN_FIELDS") && Reads(allClosed, "ASC_GEN_BREAKERS"));
        foreach (var relay in GeneratorRelays)
        {
            var oneOpen = State(GeneratorRelays.Select(r => (r, r == relay ? 0.0 : 1.0)).ToArray());
            var oneUnread = State(GeneratorRelays.Where(r => r != relay).Select(r => (r, 1.0)).ToArray());
            Assert.False(check.Condition!(oneOpen.GetValue(check.ConditionFieldName!)), $"{relay} open");
            Assert.False(check.Condition!(oneUnread.GetValue(check.ConditionFieldName!)), $"{relay} unread");
        }
    }

    // ------------------------------------------------------------------ the take-off flaps ([FO-20])

    private static L1011FoStateEvaluator Flaps(double? tablet, int simBrief = -1, double? handle = null, bool? onGround = true)
    {
        var seeds = new List<(string, double)>();
        if (tablet is double t) seeds.Add((TabletFlaps, t));
        if (handle is double h) seeds.Add((L1011Levers.FlapHandleKey, h));
        if (onGround is bool g) seeds.Add((Ground, g ? 1 : 0));
        var state = State(seeds.ToArray());
        state.SetTakeoffFlaps(simBrief);
        return state;
    }

    /// <summary>
    /// The take-off flap handle index comes from the tablet's export (10 degrees is index 2), else from SimBrief's
    /// take-off flaps (14 degrees is index 3), else there is none: nothing is sent or spoken and the Captain is asked
    /// ([FO-20]). The Captain is not asked once the handle reads the take-off index. Off the ground or with the ground
    /// unread, the step and its tick are left alone with the reason spoken.
    /// </summary>
    [Fact]
    public void Take_off_flaps_come_from_the_tablet_then_simbrief_then_the_captain()
    {
        var step = Of("AS_FLAPS");
        var captain = Of("AS_FLAPS_CPT");
        Assert.Equal(L1011Levers.FlapHandleKey, step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Take-off flaps", captain.Label);
        Assert.Equal("Set the take-off flaps: enter the take-off data on the tablet, or move the flap handle yourself.",
            captain.ReminderText);
        Assert.Empty(captain.LinkedChecklistItemIds);
        var ids = AfterStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(step.Id) + 1, ids.IndexOf(captain.Id));

        // The tablet: 10 degrees is handle index 2; at index 2 neither the step nor the Captain has anything to do.
        Assert.Equal(2, step.TargetValueProvider!(Flaps(10)));
        Assert.Equal(2, step.TargetValueProvider!(Flaps(10, simBrief: 22)));
        Assert.True(step.SkipCondition!(Flaps(10, handle: 2)));
        Assert.True(captain.SkipCondition!(Flaps(10, handle: 2)));
        Assert.False(step.SkipCondition!(Flaps(10, handle: 0)));
        Assert.False(captain.SkipCondition!(Flaps(10, handle: 0)), "the handle up: the Captain is asked");

        // No tablet export (0 before one): SimBrief's 14 degrees is index 3.
        Assert.Equal(3, step.TargetValueProvider!(Flaps(0, simBrief: 14)));
        Assert.Equal(3, step.TargetValueProvider!(Flaps(null, simBrief: 14)));
        Assert.True(captain.SkipCondition!(Flaps(0, simBrief: 14, handle: 3)));

        // Neither: no value, and the Captain is asked whatever the handle reads.
        Assert.Null(step.TargetValueProvider!(Flaps(0)));
        Assert.Null(step.TargetValueProvider!(Flaps(null)));
        Assert.False(captain.SkipCondition!(Flaps(0, handle: 2)));
        Assert.False(captain.SkipCondition!(Flaps(null, handle: 0)));

        // Flaps on the ground only: the step and its tick are left alone airborne or with the ground unread.
        var tick = Line("AS_FLAPS");
        foreach (var leaves in new Func<L1011FoStateEvaluator, bool>[] { step.LeaveAloneWhen!, tick.LeaveAloneWhen! })
        {
            Assert.True(leaves(Flaps(10, onGround: false)), "airborne");
            Assert.True(leaves(Flaps(10, onGround: null)), "the ground unread");
            Assert.False(leaves(Flaps(10, onGround: true)), "on the ground");
        }
        Assert.Equal(FlapsText, step.LeaveAloneText);
        Assert.Equal(FlapsText, tick.LeaveAloneText);
    }

    /// <summary>The flaps tick through the real checklist manager: refused at once, aloud, airborne.</summary>
    [Fact]
    public void The_flaps_tick_through_the_checklist_manager_is_refused_aloud_airborne()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected((Ground, 0), (TabletFlaps, 10)), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "AS_FLAPS");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(FlapsText, spoken);
        Assert.False(line.IsChecked);
    }

    // ------------------------------------------------------------------ the 1A and 3A transfers

    /// <summary>The transfers go on only with fuel in both tank 1A and tank 3A (above 50 lb): with either empty or
    /// unread the step and its tick are left alone, saying what stays off.</summary>
    [Fact]
    public void Transfers_go_on_only_with_fuel_in_1A_and_3A()
    {
        var step = Of("AS_TRANSFERS");
        var tick = Line("AS_TRANSFERS");
        Assert.Equal(TransfersText, step.LeaveAloneText);
        Assert.Equal(TransfersText, tick.LeaveAloneText);
        foreach (var leaves in new Func<L1011FoStateEvaluator, bool>[] { step.LeaveAloneWhen!, tick.LeaveAloneWhen! })
        {
            Assert.False(leaves(State((Tank1A, 500), (Tank3A, 500))), "both tanks hold fuel");
            Assert.True(leaves(State((Tank1A, 10), (Tank3A, 500))), "tank 1A empty");
            Assert.True(leaves(State((Tank1A, 500), (Tank3A, 0))), "tank 3A empty");
            Assert.True(leaves(State((Tank1A, 500))), "tank 3A unread");
            Assert.True(leaves(State()), "both unread");
        }
    }

    [Fact]
    public void The_transfers_tick_through_the_checklist_manager_is_refused_aloud_with_the_tanks_empty()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected((Tank1A, 0), (Tank3A, 0)), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "AS_TRANSFERS");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(TransfersText, spoken);
        Assert.False(line.IsChecked);
    }

    /// <summary>Only the flaps and the transfers are left alone, in the flow and in the action group.</summary>
    [Fact]
    public void Only_the_flaps_and_the_transfers_are_left_alone()
    {
        Assert.Equal(new[] { "AS_TRANSFERS", "AS_FLAPS" }, AfterStart.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));
        Assert.Equal(new[] { "AS_TRANSFERS", "AS_FLAPS" },
            Group("AFTER_START").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
    }

    // ------------------------------------------------------------------ the stabilizer trim reminder

    /// <summary>
    /// The stabilizer trim is the Captain's, never automated: its reminder names the tablet's exported trim when it
    /// reads above 0, in invariant figures under a comma-decimal culture too, and is the static sentence otherwise. The
    /// action line's live value is the same figure.
    /// </summary>
    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_stab_trim_reminder_names_the_tablets_value(string culture)
    {
        var step = Of("AS_CPT_STAB_TRIM");
        Assert.Equal(FlowStepActionType.CaptainReminder, step.ActionType);
        Assert.Equal("Set the stabilizer trim for take-off", step.Label);
        Assert.Equal(step.Label, step.ReminderText);
        Assert.Null(step.SkipCondition);
        var line = Line("AS_CPT_STAB_TRIM");
        Assert.Equal(ChecklistItemType.CaptainReminder, line.Type);

        var (text, none, unread, live, noLive) = TestCulture.Run(culture, () => (
            step.ReminderTextProvider!(State((TabletStabTrim, 2.38))),
            step.ReminderTextProvider!(State((TabletStabTrim, 0))),
            step.ReminderTextProvider!(State()),
            line.ReadLiveValue(State((TabletStabTrim, 2.38))),
            line.ReadLiveValue(State((TabletStabTrim, 0)))));
        Assert.Equal("Set stabilizer trim for take-off: 2.38 units", text);
        Assert.Null(none);
        Assert.Null(unread);
        Assert.Equal("2.38 units", live);
        Assert.Null(noLive);
        Assert.Equal("4.00 units", TestCulture.Run(culture, () => line.ReadLiveValue(State((TabletStabTrim, 4)))));
    }

    /// <summary>Through the real flow manager, under a comma-decimal culture: the Captain hears the tablet's value, or
    /// the static sentence before an export.</summary>
    [Theory]
    [InlineData(2.38, "Set stabilizer trim for take-off: 2.38 units")]
    [InlineData(0.0, "Set the stabilizer trim for take-off")]
    public async Task The_stab_trim_reminder_is_spoken_with_the_tablets_value(double exported, string expected)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var state = State((TabletStabTrim, exported));
            var groups = L1011FoChecklistDefinitions.Build();
            var speech = new GatedSpeechCapture();
            var flows = new FlowManager<X, L1011FoStateEvaluator>(state, new X(),
                new ChecklistManager<X, L1011FoStateEvaluator>(state, new X(), groups), speech);
            flows.StartFlow(new FlowDefinition<L1011FoStateEvaluator>
            {
                Id = "T", Name = "T", Steps = new() { Of("AS_CPT_STAB_TRIM") },
            });
            var deadline = DateTime.UtcNow + Hang;
            while (flows.IsRunning && DateTime.UtcNow < deadline)
                await Task.Delay(20);

            Assert.Equal(new[] { "T flow started", $"Captain action required: {expected}", "T flow complete" }, speech.All);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    /// <summary>The three Captain steps, unconditional, each ticking its own reminder line.</summary>
    [Fact]
    public void The_three_captain_steps_follow_the_taxi_light()
    {
        var expected = new[]
        {
            ("AS_CPT_STAB_TRIM", "Set the stabilizer trim for take-off"),
            ("AS_CPT_ANTI_ICE", "Engine anti-ice as required"),
            ("AS_CPT_CONTROLS", "Check the speed brake and the flight controls"),
        };
        var ids = AfterStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(expected.Select(e => e.Item1), ids.Skip(ids.IndexOf("AS_TAXI_LIGHT") + 1).Take(3));
        foreach (var (id, label) in expected)
        {
            var s = Of(id);
            Assert.Equal(FlowStepActionType.CaptainReminder, s.ActionType);
            Assert.Equal(label, s.Label);
            Assert.Equal(label, s.ReminderText);
            Assert.Null(s.SkipCondition);
            Assert.Equal(id == "AS_CPT_STAB_TRIM", s.ReminderTextProvider != null);
        }
    }

    // ------------------------------------------------------------------ the APU shutdown tail

    private static readonly string[] ApuTail = { "AS_APU_GEN_TRIP", "AS_APU_BLEED_OFF", "AS_APU_STOP", "AS_APU_MASTER_OFF" };

    /// <summary>
    /// The APU is shut down only once all three engine generators read on line: a 30 s wait that, timed out, skips
    /// every APU step with one sentence saying the APU stays running. The two-minute cool-down follows the stop only
    /// when this run sent the stop.
    /// </summary>
    [Fact]
    public void The_APU_tail_waits_for_the_engine_generators()
    {
        var wait = Of("AS_GENS_ON_LINE");
        Assert.Equal("Engine generators on line", wait.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal("FO_ENGINE_GENERATORS_ON", wait.ConditionFieldName);
        Assert.Equal(30, wait.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, wait.FailurePolicy);
        Assert.Null(wait.SkipCondition);
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.False(wait.Condition!(double.NaN), "unread must not pass");
        Assert.False(wait.Condition!(0));
        Assert.True(wait.Condition!(1));

        var ids = AfterStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(new[] { "AS_GENS_ON_LINE" }.Concat(ApuTail.Take(3)).Append("AS_APU_COOLDOWN").Append("AS_APU_MASTER_OFF"),
            ids.Skip(ids.IndexOf("AS_GENS_ON_LINE")));
        foreach (var id in ApuTail)
        {
            Assert.Equal("AS_GENS_ON_LINE", Of(id).RequiresStepId);
            Assert.Equal(GeneratorsText, Of(id).RequiresStepSkipText);
        }
        Assert.Equal(ApuTail, AfterStart.Steps.Where(s => s.RequiresStepId != null).Select(s => s.Id));

        var cooldown = Of("AS_APU_COOLDOWN");
        Assert.Equal("APU cool-down: two minutes", cooldown.Label);
        Assert.Equal(FlowStepActionType.WaitSeconds, cooldown.ActionType);
        Assert.Equal(120, cooldown.WaitSeconds);
        Assert.Equal("AS_APU_STOP", cooldown.RunOnlyIfStepSent);
        Assert.Null(cooldown.RequiresStepId);
        Assert.Empty(cooldown.LinkedChecklistItemIds);
        Assert.Equal(new[] { "AS_APU_COOLDOWN" }, AfterStart.Steps.Where(s => s.RunOnlyIfStepSent != null).Select(s => s.Id));
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = AfterStart.Steps.Select(s => s.ConditionFieldName).OfType<string>()
            .Concat(new[] { TabletFlaps, TabletStabTrim, Tank1A, Tank3A, ApuRpm, ApuGenBreaker, L1011Levers.FlapHandleKey, Ground })
            .Concat(Groups.Where(g => g.Id is "AFTER_START" or "AFTER_START_CL" or "TAXI_CL").SelectMany(g => g.Items)
                .Select(i => i.StateFieldName).OfType<string>())
            .Distinct().ToList();
        foreach (var field in fields)
        {
            if (synthetic.Contains(field)) continue;
            Assert.True(vars.TryGetValue(field, out var def), $"{field} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
            if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.True(polled.Contains(field), $"OnRequest field {field} is not polled");
        }
        Assert.True(fields.Count >= 25, $"only {fields.Count} fields enumerated");
    }

    // ------------------------------------------------------------------ a run on the fake TriStar

    /// <summary>The fake TriStar as Engine Start leaves it: on the ground, all three engines at idle
    /// with their generators on line and their IDG pressure up, the bus fail lights out; the APU running on line with
    /// its bleed on; Preflight's high pressure valves, fuselage isolation and crossbleeds on; pack 2 only; engines 1
    /// and 2's isolation valves off; the ATMs and the galley off; the AC meter on the APU generator, the DC meter on the
    /// battery, the essential selector normal; each engine's own tank pump on and the others off, crossfeeds closed,
    /// transfers off, tanks 1A and 3A empty; air data heat off; ignition A; the flaps up with the tablet's take-off
    /// export at 10 degrees and 2.38 units; the taxi light off.</summary>
    private static FakeL1011Aircraft EngineStartLeaves()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(Ground, 1);
        foreach (int n in new[] { 1, 2, 3 })
        {
            ac.Set($"L1011_RO_N3_{n}", FakeL1011Aircraft.IdleN3Reading);
            ac.Set($"TOGGLE_CUTOFF_ENG_{n}", 1);
            ac.Set($"SWITCH_GND_START_ENG_{n}", 0);
            ac.Set($"LAMP_IDG_OIL_PRESSURE_{n}", 60);
        }
        foreach (var relay in GeneratorRelays)
            ac.Set(relay, 1);
        // The fake lights the essential bus fail lights off external power, as the standby power check saw them: here
        // the engine generators carry the buses.
        foreach (var lamp in new[] { "LAMP_AC_ESS_FAIL_LIGHT", "LAMP_DC_ESS_FAIL_LIGHT" })
        {
            ac.NeverDeliver(lamp);
            ac.Set(lamp, 0);
        }
        ac.Set("TOGGLE_APU_POWER", 1);
        ac.Set(ApuRpm, 100);
        ac.Set("SWITCH_APU_BLEED", 1);
        ac.Set(ApuGenField, 1);
        ac.Set(ApuGenBreaker, 1);
        foreach (var key in new[] { "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3",
                     "SWITCH_APU_ISLN_VALVE", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_ISLN_VALVE_ENG_3" })
            ac.Set(key, 1);
        foreach (var key in new[] { "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_3", "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2",
                     "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE", "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3",
                     "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED", "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A",
                     "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_ALPHA_HEAT", "ROTARY_ENG_START_SEL", "TOGGLE_TAXI_LIGHT",
                     L1011Levers.FlapHandleKey, Tank1A, Tank3A })
            ac.Set(key, 0);
        foreach (var pump in FuelPumps)
            ac.Set(pump, pump is "SWITCH_FUEL_PUMP_1_1" or "SWITCH_FUEL_PUMP_2L_1" or "SWITCH_FUEL_PUMP_3_1" ? 1 : 0);
        ac.Set("ROTARY_PWR_SOURCE", 4);
        ac.Set("ROTARY_STBY_DC_BUS", 4);
        ac.Set("ROTARY_AC_ESS_BUS", 4);
        ac.Set(TabletFlaps, 10);
        ac.Set(TabletStabTrim, 2.38);
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
    /// The steps run in order on the fake TriStar as Engine Start leaves it, as FlowManager runs them: "Already set" and
    /// a leave-alone read from what the aircraft shows, a step whose required step was skipped skipped too, the cool-down
    /// only after a stop this run sent, each wait polled and the cool-down counted on the aircraft's clock, each write
    /// through the executor, a provider's target resolved first. Nothing fails or times out; the transfers are left
    /// alone (tanks 1A and 3A empty), the flaps go to the tablet's index 2 and the Captain is not asked for them, the
    /// ATMs end at auto by way of on, and the APU is shut down in order, its master off two minutes after the stop.
    /// </summary>
    [Fact]
    public async Task A_run_on_the_fake_TriStar_sets_up_for_taxi_and_shuts_the_APU_down()
    {
        var ac = EngineStartLeaves();
        var exec = new X();
        exec.SetTransport(ac);
        using var stop = new CancellationTokenSource();
        var outcome = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new HashSet<string>(StringComparer.Ordinal);
        var sent = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in L1011FoFlowDefinitions.Build().Single(f => f.Id == "AFTER_START").Steps)
        {
            var s = StateOf(ac);
            if (step.SkipCondition?.Invoke(s) == true) { outcome[step.Id] = "already set"; continue; }
            if (step.LeaveAloneWhen?.Invoke(s) == true) { outcome[step.Id] = "left alone"; skipped.Add(step.Id); continue; }
            if (step.RequiresStepId is string required && skipped.Contains(required))
            {
                outcome[step.Id] = "skipped";
                skipped.Add(step.Id);
                continue;
            }
            if (step.RunOnlyIfStepSent is string after && !sent.Contains(after)) { outcome[step.Id] = "passed"; continue; }
            switch (step.ActionType)
            {
                case FlowStepActionType.CaptainReminder:
                    outcome[step.Id] = "captain";
                    break;
                case FlowStepActionType.WaitSeconds:
                    await ac.DelayAsync(step.WaitSeconds * 1000);
                    outcome[step.Id] = "waited";
                    break;
                case FlowStepActionType.WaitForCondition:
                    outcome[step.Id] = await WaitOn(ac, step).WaitAsync(Hang) ? "done" : "timed out";
                    break;
                default:
                    if (step.TargetValueProvider is { } provider)
                        step.TargetValue = provider(s) ?? throw new InvalidOperationException($"{step.Id}: no value");
                    Assert.True(await exec.ExecuteStepAsync(step, stop.Token).WaitAsync(Hang), $"{step.Id} was not done");
                    sent.Add(step.Id);
                    outcome[step.Id] = "done";
                    break;
            }
        }

        Assert.DoesNotContain(outcome, o => o.Value is "timed out" or "skipped");
        Assert.Equal("left alone", outcome["AS_TRANSFERS"]);
        Assert.Equal("already set", outcome["AS_FLAPS_CPT"]);
        Assert.Equal("waited", outcome["AS_APU_COOLDOWN"]);
        Assert.Equal(new[] { "AS_CPT_STAB_TRIM", "AS_CPT_ANTI_ICE", "AS_CPT_CONTROLS" },
            outcome.Where(o => o.Value == "captain").Select(o => o.Key));
        Assert.Equal("done", outcome["AS_GENS_ON_LINE"]);

        // Set up for taxi.
        foreach (var key in new[] { "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_3", "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2",
                     "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3", "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT",
                     "SWITCH_ALPHA_HEAT", "ROTARY_ENG_START_SEL", "TOGGLE_TAXI_LIGHT", "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE" })
            Assert.Equal(1, ac.Get(key));
        Assert.All(FuelPumps, pump => Assert.Equal(1, ac.Get(pump)));
        Assert.Equal(3, ac.Get("ROTARY_PWR_SOURCE"));
        Assert.Equal(2, ac.Get(L1011Levers.FlapHandleKey));
        Assert.DoesNotContain(ac.Writes, w => w.Key.StartsWith("SWITCH_FUEL_XFR_", StringComparison.Ordinal));
        int Index(string key, double value) => ac.Writes.IndexOf((key, value));
        Assert.True(Index("ROTARY_HYD_B2_MODE", 2) >= 0 && Index("ROTARY_HYD_B2_MODE", 2) < Index("ROTARY_HYD_B2_MODE", 1), "the ATMs on, then auto");

        // The APU shut down: the breaker tripped, the bleed off, the stop pressed, the master off, in that order.
        Assert.Equal(0, ac.Get(ApuGenBreaker));
        Assert.Equal(0, ac.Get("SWITCH_APU_BLEED"));
        Assert.Equal(0, ac.Get(ApuRpm));
        Assert.Equal(0, ac.Get("TOGGLE_APU_POWER"));
        int trip = Index(X.ApuGenBreakerTrip, 1), bleed = Index("SWITCH_APU_BLEED", 0), press = Index("APU_STOP", 1),
            master = Index("TOGGLE_APU_POWER", 0);
        Assert.True(trip >= 0 && trip < bleed && bleed < press && press < master, $"trip {trip}, bleed {bleed}, stop {press}, master {master}");
        var writes = ac.Io.Where(e => e.Kind == FakeL1011IoKind.Write).ToList();
        Assert.True(writes[master].AtMs - writes[press].AtMs >= 120_000, "the master off two minutes after the stop");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheAfterStartGroups_FollowEngineStart_Named()
    {
        Assert.Equal(new[] { "AFTER_START", "AFTER_START_CL", "TAXI_CL" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "AFTER_START").Take(3));
        Assert.Equal("ENGINE_START_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "AFTER_START").Last());
        Assert.Equal("After Start", Group("AFTER_START").Name);
        Assert.Equal("After Start", Group("AFTER_START_CL").Name);
        Assert.Equal("Taxi", Group("TAXI_CL").Name);
    }

    [Fact]
    public void EveryAfterStartGroup_IsDeparture_AndNeverArrival()
    {
        foreach (var id in AfterStart.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Departure.Contains(id), $"{id} is not a departure group");
            Assert.False(FoDepartureGroups.Arrival.Contains(id), $"{id} is an arrival group too");
        }
    }

    [Fact]
    public void The_read_backs_have_no_check_action()
        => Assert.All(Group("AFTER_START_CL").Items.Concat(Group("TAXI_CL").Items),
            i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
    {
        "AS_HI_PRESS", "AS_ISLN_VALVES", "AS_APU_AIR", "AS_PACKS_ON", "AS_ATMS_ON", "AS_ATMS_AUTO", "AS_GALLEY_ON",
        "AS_AC_METER", "AS_DC_METER", "AS_FUEL_PUMPS", "AS_CROSSFEEDS", "AS_TRANSFERS", "AS_PITOT_HEAT", "AS_IGNITION_BOTH",
        "AS_FLAPS", "AS_TAXI_LIGHT", "AS_CPT_STAB_TRIM", "AS_CPT_ANTI_ICE", "AS_CPT_CONTROLS",
        "AS_APU_GEN_TRIP", "AS_APU_BLEED_OFF", "AS_APU_STOP", "AS_APU_MASTER_OFF",
    };

    private static readonly string[] Reminders = { "AS_CPT_STAB_TRIM", "AS_CPT_ANTI_ICE", "AS_CPT_CONTROLS" };

    /// <summary>The conditional flaps prompt, spoken only while the handle is not at the take-off setting, is flow-only.</summary>
    [Fact]
    public void AfterStart_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("AFTER_START").Items.Select(i => i.Id));
        Assert.NotNull(Of("AS_FLAPS_CPT").SkipCondition);
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == "AS_FLAPS_CPT");
    }

    /// <summary>Ticked or run, a line says the same thing.</summary>
    [Fact]
    public void AfterStart_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("AFTER_START").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void AfterStart_LineKinds()
    {
        foreach (var i in Group("AFTER_START").Items)
        {
            if (Reminders.Contains(i.Id))
            {
                Assert.Equal(ChecklistItemType.CaptainReminder, i.Type);
                Assert.Null(i.CheckAction);
                Assert.Equal(Of(i.Id).ReminderText, i.ReminderText);
                Assert.Equal(i.Id == "AS_CPT_STAB_TRIM", i.LiveValue != null);
                continue;
            }
            Assert.NotNull(i.CheckAction);
            if (i.Id == "AS_ATMS_ON")
            {
                Assert.Equal(ChecklistItemType.Actionable, i.Type);
                continue;
            }
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each Auto line reads the state its step delivers: a single switch its key, a multi switch its
    /// synthetic, the generator trip its breaker open, the stop the APU stopped, the flaps the take-off index.</summary>
    [Theory]
    [InlineData("AS_HI_PRESS", "FO_HI_PRESS_ON", 1)]
    [InlineData("AS_ISLN_VALVES", "FO_ISLN_VALVES_ON", 1)]
    [InlineData("AS_APU_AIR", "FO_APU_AIR_ON", 1)]
    [InlineData("AS_PACKS_ON", "FO_PACKS_ON", 1)]
    [InlineData("AS_ATMS_AUTO", "FO_ATMS_AUTO", 1)]
    [InlineData("AS_GALLEY_ON", "FO_GALLEY_ON", 1)]
    [InlineData("AS_AC_METER", "ROTARY_PWR_SOURCE", 3)]
    [InlineData("AS_DC_METER", "ROTARY_STBY_DC_BUS", 4)]
    [InlineData("AS_FUEL_PUMPS", "FO_FUEL_PUMPS_ON", 1)]
    [InlineData("AS_CROSSFEEDS", "FO_CROSSFEEDS_CLOSED", 1)]
    [InlineData("AS_TRANSFERS", "FO_TRANSFERS_ON", 1)]
    [InlineData("AS_PITOT_HEAT", "FO_PITOT_ALPHA_HEAT_ON", 1)]
    [InlineData("AS_IGNITION_BOTH", "ROTARY_ENG_START_SEL", 1)]
    [InlineData("AS_FLAPS", "FO_FLAPS_TAKEOFF", 1)]
    [InlineData("AS_TAXI_LIGHT", "TOGGLE_TAXI_LIGHT", 1)]
    [InlineData("AS_APU_GEN_TRIP", "FO_APU_GEN_OFF_LINE", 1)]
    [InlineData("AS_APU_BLEED_OFF", "SWITCH_APU_BLEED", 0)]
    [InlineData("AS_APU_STOP", "FO_APU_OFF", 1)]
    [InlineData("AS_APU_MASTER_OFF", "TOGGLE_APU_POWER", 0)]
    public void AfterStart_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
    {
        var line = Line(id);
        Assert.Equal(field, line.StateFieldName);
        Assert.True(line.StateCondition!(done), $"{id}: {field} at {done} must read done");
        Assert.False(line.StateCondition!(double.NaN), $"{id}: {field} unread must not read done");
        var others = L1011FoStateEvaluator.SyntheticKeys.Contains(field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
        foreach (double other in others.Where(v => v != done))
            Assert.False(line.StateCondition!(other), $"{id}: {field} at {other} must not read done");
    }

    // ---- a hand tick does what the flow step does ----------------------------------------------------------------

    /// <summary>The evaluator the providers read: the tablet's take-off export at 10 degrees and 2.38 units.</summary>
    private static L1011FoStateEvaluator Ready() => State((TabletFlaps, 10), (TabletStabTrim, 2.38));

    /// <summary>An aircraft set up so the step has work to do: each key the step writes the other way from its target
    /// (the flap handle up), the APU generator on line for its trip, and the APU running for its stop.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key) && a.Key != "APU_STOP"))
            ac.Set(key, target is int t && t == 0 ? 1 : 0);
        if (step.EventName == X.ApuGenerator)
        {
            ac.Set(ApuGenField, 1);
            ac.Set(ApuGenBreaker, 1);
        }
        if (step.EventName == "APU_STOP")
        {
            ac.Set("TOGGLE_APU_POWER", 1);
            ac.Set(ApuRpm, 100);
        }
        return ac;
    }

    public static IEnumerable<object[]> AfterStartLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "AFTER_START").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft; the
    /// flaps step with the target the flow resolves from the same readings.</summary>
    [Theory]
    [MemberData(nameof(AfterStartLinesWithACheckAction))]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var ready = Ready();
        var step = L1011FoFlowDefinitions.Build().Single(f => f.Id == "AFTER_START").Steps.Single(s => s.Id == id);
        if (step.TargetValueProvider is { } provider)
            step.TargetValue = provider(ready);

        var byStep = AircraftFor(step);
        var stepExec = new X();
        stepExec.SetTransport(byStep);
        Assert.True(await stepExec.ExecuteStepAsync(step).WaitAsync(Hang), $"{id}: the flow step was not done");

        var byTick = AircraftFor(step);
        var tickExec = new X();
        tickExec.SetTransport(byTick);
        await Line(id).CheckAction!(tickExec, ready).WaitAsync(Hang);

        static List<(FakeL1011IoKind, string, double?)> Sent(FakeL1011Aircraft ac) =>
            ac.Io.Where(e => e.Kind != FakeL1011IoKind.FreshRead).Select(e => (e.Kind, e.Key, e.Value)).ToList();
        Assert.NotEmpty(Sent(byStep));
        Assert.Equal(Sent(byStep), Sent(byTick));
    }

    /// <summary>With no take-off flaps from the tablet or SimBrief, the flaps tick writes nothing, as its step sends
    /// nothing.</summary>
    [Fact]
    public async Task The_flaps_tick_with_no_value_writes_nothing()
    {
        var ac = AircraftFor(Of("AS_FLAPS"));
        var exec = new X();
        exec.SetTransport(ac);
        await Line("AS_FLAPS").CheckAction!(exec, State((TabletFlaps, 0))).WaitAsync(Hang);
        Assert.DoesNotContain(ac.Io, e => e.Kind != FakeL1011IoKind.FreshRead);
    }

    // ---- the read-backs, word for word -----------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value, or a Captain's reminder.</summary>
    private sealed record ReadBack(string Id, string Label, string? Field = null, double Done = 1, bool Reminder = false);

    private static readonly ReadBack[] AfterStartReadBack =
    {
        new("ASC_HI_PRESS", "All engines hi press switches: latched", "FO_HI_PRESS_ON"),
        new("ASC_ISLN_VALVES", "All eng isol valve: latch", "FO_ISLN_VALVES_ON"),
        new("ASC_FUSELAGE_ISLN", "Fuselage isolation valve switch: latched", "SWITCH_APU_ISLN_VALVE"),
        new("ASC_XBLEEDS", "All cross bleed switches: latched", "FO_CROSSBLEEDS_ON"),
        new("ASC_PACKS", "Pack flow control switches: latch", "FO_PACKS_ON"),
        new("ASC_ATMS", "ATMs: auto", "FO_ATMS_AUTO"),
        new("ASC_GALLEY", "Galley power: on", "FO_GALLEY_ON"),
        new("ASC_GEN_FIELDS", "Engine generator field: close", "FO_GEN_FIELDS_CLOSED"),
        new("ASC_GEN_BREAKERS", "Engine generator breaker: close", "FO_GEN_BREAKERS_CLOSED"),
        new("ASC_IDG", "IDG low pressure lights: out", "FO_IDG_LIGHTS_OUT"),
        new("ASC_BUS_FAIL", "All bus fail lights: out", "FO_BUS_FAIL_LIGHTS_OUT"),
        new("ASC_AC_METER", "AC meter selector: gen 3", "ROTARY_PWR_SOURCE", 3),
        new("ASC_DC_METER", "DC meter selector: bat", "ROTARY_STBY_DC_BUS", 4),
        new("ASC_AC_ESS", "Essential power selector: normal b3 (g1)", "ROTARY_AC_ESS_BUS", 4),
        new("ASC_FUEL_PUMPS", "Fuel tank pumps: all on", "FO_FUEL_PUMPS_ON"),
        new("ASC_CROSSFEEDS", "Crossfeed valves: closed", "FO_CROSSFEEDS_CLOSED"),
        new("ASC_TRANSFERS", "If there is fuel in tanks 1A and 3A: latch xfr", "FO_TRANSFERS_ON"),
        new("ASC_PITOT_HEAT", "Capt and F/O pitot heat: on", "FO_PITOT_ALPHA_HEAT_ON"),
        new("ASC_ALPHA_HEAT", "Alpha probe heat: on", "FO_PITOT_ALPHA_HEAT_ON"),
        new("ASC_IGNITION", "Engine ignition: both", "ROTARY_ENG_START_SEL"),
        new("ASC_STAB_TRIM", "Stabilizer trim: set for take off", Reminder: true),
        new("ASC_SPEED_BRAKES", "Speed brakes: check", Reminder: true),
        new("ASC_FLAPS", "Flaps: set for take off", "FO_FLAPS_TAKEOFF"),
        new("ASC_CONTROLS", "Flight controls: check", Reminder: true),
        new("ASC_APU_GEN_TRIP", "APU generator breaker: trip", "FO_APU_GEN_OFF_LINE"),
        new("ASC_APU_BLEED", "APU bleed air shut off switch: unlatch", "SWITCH_APU_BLEED", 0),
        new("ASC_APU_STOP", "APU stop button: press", "FO_APU_OFF"),
        new("ASC_APU_MASTER", "APU master power: off", "TOGGLE_APU_POWER", 0),
    };

    private static readonly ReadBack[] TaxiReadBack =
    {
        new("TXC_TAXI_LIGHT", "Nose wheel taxi light: on", "TOGGLE_TAXI_LIGHT"),
        new("TXC_PARKING_BRAKE", "Parking brake: release", Reminder: true),
        new("TXC_BRAKES", "Brakes: check", Reminder: true),
    };

    [Fact]
    public void The_after_start_read_back_is_word_for_word()
        => Assert.Equal(AfterStartReadBack.Select(l => (l.Id, l.Label)), Group("AFTER_START_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void The_taxi_read_back_is_word_for_word()
        => Assert.Equal(TaxiReadBack.Select(l => (l.Id, l.Label)), Group("TAXI_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in AfterStartReadBack.Concat(TaxiReadBack))
        {
            var line = Line(rb.Id);
            Assert.Null(line.CheckAction);
            Assert.Empty(line.AdditionalStateFields);
            if (rb.Reminder)
            {
                Assert.Equal(ChecklistItemType.CaptainReminder, line.Type);
                Assert.Equal(rb.Label, line.ReminderText);
                continue;
            }
            Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
            Assert.Equal(RevertBehavior.RevertToState, line.RevertBehavior);
            Assert.Equal(rb.Field, line.StateFieldName);
            Assert.True(line.StateCondition!(rb.Done), $"{rb.Id}: {rb.Field} at {rb.Done} must read done");
            Assert.False(line.StateCondition!(double.NaN), $"{rb.Id}: unread must not read done");
            var others = L1011FoStateEvaluator.SyntheticKeys.Contains(rb.Field!) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
            foreach (double other in others.Where(v => v != rb.Done))
                Assert.False(line.StateCondition!(other), $"{rb.Id}: {rb.Field} at {other} must not read done");
        }
    }

    /// <summary>A finished run latches the action group and both read-backs, and nothing else.</summary>
    [Fact]
    public void AfterStart_LatchesItsThreeGroups()
        => Assert.Equal(AfterStart.RelatedChecklistGroupIds, AfterStart.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The spoken labels with a figure read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_hold_under_a_comma_decimal_culture(string culture)
    {
        var labels = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "AFTER_START");
            return flow.Steps.Select(s => s.Label).Concat(flow.Steps.Select(s => s.ReminderText ?? ""))
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
        });
        Assert.Contains("AC meter: generator 3", labels);
        Assert.Contains("1A and 3A transfers: on", labels);
        Assert.Contains("Essential power selector: normal b3 (g1)", labels);
    }

    // ================================================================== what After Start writes

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

    private static IEnumerable<(string Step, string Key, int? Target)> EveryWrittenKey()
        => AfterStart.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(44, EveryWrittenKey().Count());
    }

    /// <summary>The one composite is the APU generator's breaker trip; the one lever the flap handle (the flaps step);
    /// the one press the APU stop button.</summary>
    [Fact]
    public void The_composite_the_lever_and_the_press()
    {
        Assert.Equal(new[] { X.ApuGenerator }, EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct());
        Assert.Equal(new[] { ("AS_FLAPS", L1011Levers.FlapHandleKey) },
            EveryWrittenKey().Where(w => L1011Levers.IsLever(w.Key)).Select(w => (w.Step, w.Key)));
        Assert.Equal(new[] { ("AS_APU_STOP", "APU_STOP") },
            EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && Rows.Value[w.Key].Action == L1011RowAction.Press)
                .Select(w => (w.Step, w.Key)));
    }

    /// <summary>Every other key is a plain switch row, and its target one of the control's positions.</summary>
    [Fact]
    public void EveryOtherKey_IsAPlainSwitchRow_AtOneOfItsPositions()
    {
        foreach (var (step, key, target) in PlainSwitchWrites())
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target, current: target == 0 ? 1 : 0);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused ({plan.Refusal})");
        }
    }

    private static IEnumerable<(string Step, string Key, int Target)> PlainSwitchWrites()
        => EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !L1011Levers.IsLever(w.Key) && w.Key != "APU_STOP")
            .Select(w => (w.Step, w.Key, w.Target!.Value));

    /// <summary>The keys whose click replay carries a TOGGLE K: event, refused from an unknown position ([L1011-4]).</summary>
    private static readonly string[] ToggleCarrying = { "SWITCH_APU_BLEED" };

    /// <summary>
    /// A toggle-carrying key is written only by a single-switch step whose "Already set" reads that key at the target:
    /// the executor reads it first, and from an unknown position the definition refuses the write, which the flow
    /// speaks as "Skipping". Planned from nothing read, every other key must still be a plan.
    /// </summary>
    [Fact]
    public void No_step_writes_a_toggle_carrying_key_from_an_unread_position_except_through_a_skip()
    {
        var toggles = new List<string>();
        foreach (var (step, key, target) in PlainSwitchWrites())
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
        Assert.Equal(ToggleCarrying, toggles);
    }
}
