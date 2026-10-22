using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// [FO-6] and [FO-7] on the TriStar: every line in a group a flow latches has a step behind it, a write
/// that sets it, a read-only check that reads it or a wait for it, so a finished flow never ticks and latches
/// a line the aircraft does not show. The lines no state judges (the standby power check's and each test's)
/// are delivered by their composite step; the Captain's reminder is delivered by the reminder itself.
/// </summary>
public class L1011FoFlowChecklistLinkTests
{
    private static readonly List<FlowDefinition<L1011FoStateEvaluator>> Flows = L1011FoFlowDefinitions.Build();
    private static readonly List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups = L1011FoChecklistDefinitions.Build();

    private static List<(FlowDefinition<L1011FoStateEvaluator> Flow, List<ChecklistItem<X, L1011FoStateEvaluator>> Items)> Latched()
        => FlowChecklistLinkAudit.FlowsWithItems(Flows, Groups,
            f => f.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    [Fact]
    public void EveryFlow_LatchesOnlyItsOwnGroup()
    {
        foreach (var f in Flows)
            Assert.Equal(new[] { f.Id }, f.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));
    }

    /// <summary>The three ground start lines are the only lines latched with no step behind them: they latch on the
    /// engine running and no step links them (the MD-11's start switch lines).</summary>
    private static readonly string[] GroundStartLines = { "ES_E1_GND_START", "ES_E2_GND_START", "ES_E3_GND_START" };

    [Fact]
    public void Only_the_ground_start_lines_are_latched_with_no_step_behind_them()
        => Assert.Equal(GroundStartLines.Select(id => $"ENGINE_START/{id}"), FlowChecklistLinkAudit.UndeliveredLines(Latched()));

    [Fact]
    public void Every_link_names_a_line_of_a_group_its_flow_latches()
    {
        foreach (var (flow, items) in Latched())
        {
            var ids = items.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var step in flow.Steps)
                foreach (var id in step.LinkedChecklistItemIds)
                    Assert.True(ids.Contains(id), $"{flow.Id}/{step.Id} links {id}, which {flow.Id} does not latch");
        }
    }

    /// <summary>The three ground start lines latch on the engine running and no step links them: a ground start
    /// marked them, a failed light-off stopped the flow, and the line stayed ticked over an engine that never ran.</summary>
    [Fact]
    public void No_step_links_a_StayComplete_line()
    {
        var stay = Groups.SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(GroundStartLines, stay);
        foreach (var flow in Flows)
            foreach (var step in flow.Steps)
                Assert.False(step.LinkedChecklistItemIds.Any(stay.Contains),
                    $"{flow.Id}/{step.Id} links a StayComplete line: a ground start is not a started engine");
    }

    /// <summary>A check field that is the conjunction of other fields, by the fields it is made of: After Start's
    /// generator check reads all six relays closed, which is the three fields closed (<c>AS_GEN_FIELDS</c>) and the
    /// three breakers closed (<c>AS_GEN_BREAKERS</c>).</summary>
    private static readonly Dictionary<string, string[]> PartsOf = new(StringComparer.Ordinal)
    {
        ["FO_ENGINE_GENERATORS_ON"] = new[] { "FO_GEN_FIELDS_CLOSED", "FO_GEN_BREAKERS_CLOSED" },
    };

    // A read-only check never writes: every link of a check step is to a line it reads, its own field or a part of it.
    [Fact]
    public void A_read_only_check_completes_only_lines_that_read_its_own_field()
    {
        var items = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, StringComparer.Ordinal);
        int n = 0;
        foreach (var flow in Flows)
            foreach (var step in flow.Steps.Where(s => s.ActionType == FlowStepActionType.WaitForCondition
                                                        && s.Id.Contains("_CHK_", StringComparison.Ordinal)))
                foreach (var id in step.LinkedChecklistItemIds)
                {
                    n++;
                    string? field = items[id].StateFieldName;
                    bool part = field is not null && PartsOf.TryGetValue(step.ConditionFieldName!, out var parts)
                        && parts.Contains(field);
                    Assert.True(field == step.ConditionFieldName || part,
                        $"{flow.Id}/{step.Id} reads {step.ConditionFieldName} but completes {id}, which reads {field}");
                }
        Assert.True(n >= 5, $"only {n} check links enumerated");
    }

    /// <summary>A check field read done reads every one of its parts done, and any part read not done (or unread)
    /// reads the whole not done: the check never ticks a part line the aircraft does not show.</summary>
    [Fact]
    public void A_check_field_read_done_reads_each_of_its_parts_done()
    {
        var relays = new[] { 1, 2, 3 }.SelectMany(n => new[] { $"L1011_RO_GEN_{n}_FIELD", $"L1011_RO_GEN_{n}_BREAKER" }).ToArray();
        var closed = State(relays.Select(r => (r, 1.0)).ToArray());
        foreach (var (whole, parts) in PartsOf)
        {
            Assert.Equal(1, closed.GetValue(whole));
            Assert.All(parts, p => Assert.Equal(1, closed.GetValue(p)));
        }
        foreach (var relay in relays)
        {
            var open = State(relays.Select(r => (r, r == relay ? 0.0 : 1.0)).ToArray());
            var unread = State(relays.Where(r => r != relay).Select(r => (r, 1.0)).ToArray());
            Assert.Equal(0, open.GetValue("FO_ENGINE_GENERATORS_ON"));
            Assert.True(double.IsNaN(unread.GetValue("FO_ENGINE_GENERATORS_ON")), $"{relay} unread");
        }
    }

    // ------------------------------------------------------------------ a write completes only lines it sets

    /// <summary>Every evaluator here carries a plan (cruise 39,000 ft, destination 120 ft) and the tablet's take-off
    /// flaps at 10: a line that compares against the plan reads done only against it.</summary>
    private const int PlannedCruiseFt = 39000;

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        evaluator.SetPlannedPressurizationAltitudes(PlannedCruiseFt, 120);
        return evaluator;
    }

    /// <summary>The evaluator a provider step's target comes from: the plan, and the tablet's take-off flaps at 10.</summary>
    private static readonly L1011FoStateEvaluator PlanState =
        State((MSFSBlindAssist.Aircraft.IniL1011Definition.PerfSelectedFlapsKey, 10));

    /// <summary>The lines no state judges, and the composites that deliver them: a test's own lines, and the
    /// standby power check's.</summary>
    private static readonly string[] ManualLineComposites =
        { X.StandbyPowerCheck, X.FireTest, X.WheelWellFireTest, X.HydraulicCheck };

    /// <summary>The action lines no state judges because the flow later undoes their position (packs on, hot air on,
    /// APU mode normal, the ATMs to auto): each is its own step's line, ticked when the step completes.</summary>
    private static readonly string[] UndoneLaterLines = { "PF_APU_MODE_MIN", "PF_PACKS_OFF", "PF_HOT_AIR_OFF", "AS_ATMS_ON" };

    /// <summary>The writes that link no line: each engine's ground start (its line latches on the engine running), its
    /// light-off watch and its stable idle watch (an engine running is what its ground start line latches on, so no
    /// second line reads it).</summary>
    private static readonly string[] UnlinkedWrites =
        new[] { 1, 2, 3 }.SelectMany(n => new[] { X.GroundStart(n), X.EngineLightOff(n), X.EngineIdle(n) }).ToArray();

    private static readonly string[] FuelPumpKeys =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    /// <summary>What the aircraft reads once the step's work is done: its keys at their targets; the external
    /// power breaker closed for the external power composite on, open for it off; the battery on and standby
    /// power armed under its closed guard after the standby power check; the three INS mode selectors at ALIGN
    /// after the INS align composite; the APU turning after its start; the APU generator's field and breaker
    /// relays and the three AC ties closed after theirs; the four fire loop selectors at Both after the fire
    /// test, the wheel well selector at Both after its test, and the AC pumps and PTUs off after the hydraulic
    /// check; an engine's N3 at idle after its idle wait, and the six engine generator relays closed after theirs; a
    /// tank pump written with every other tank pump read off; the APU generator's breaker open after its trip, and the
    /// APU stopped after its stop press; and for a provider step, the readout at the provider's value (the cabin
    /// altitude selector's, in metres; the cabin baro's, at the captain's altimeter), an INS mode selector at NAV, or
    /// the flap handle at the take-off index with the tablet's export it came from.</summary>
    private static (string Key, double Value)[] Done(Step s)
    {
        if (s.EventName == X.ApuGenerator && s.TargetValue == 0)
            return new[] { ("L1011_RO_APU_GEN_BREAKER", 0.0) };
        // The stabilizer trim's typed entry is write-only: done is its readout at the target.
        if (s.EventName == MSFSBlindAssist.Aircraft.L1011.L1011Levers.StabTrimKey)
            return new[] { ("L1011_RO_ELEVATOR_TRIM", (double)s.TargetValue!.Value) };
        if (s.EventName == X.ApuStop)
            return new[] { ("L1011_RO_APU_RPM", 0.0) };
        if (s.TargetValueProvider is { } flaps && s.EventName == MSFSBlindAssist.Aircraft.L1011.L1011Levers.FlapHandleKey)
        {
            int index = flaps(PlanState) ?? throw new InvalidOperationException($"{s.Id}: no value with the tablet's export");
            return new[] { (s.EventName, (double)index), (MSFSBlindAssist.Aircraft.IniL1011Definition.PerfSelectedFlapsKey, 10.0) };
        }
        foreach (int n in new[] { 1, 2, 3 })
            if (s.EventName == X.EngineIdle(n))
                return new[] { ($"L1011_RO_N3_{n}", 59.2) };
        if (s.EventName == X.EngineGenerators)
            return new[] { 1, 2, 3 }.SelectMany(n => new[] { ($"L1011_RO_GEN_{n}_FIELD", 1.0), ($"L1011_RO_GEN_{n}_BREAKER", 1.0) })
                .ToArray();
        if (s.ActionType == FlowStepActionType.SetSwitchMultiple && s.MultiActions.Any(a => FuelPumpKeys.Contains(a.EventName)))
        {
            var written = s.MultiActions.Select(a => (a.EventName, (double)a.TargetValue!.Value)).ToArray();
            return written.Concat(FuelPumpKeys.Where(k => written.All(w => w.EventName != k)).Select(k => (k, 0.0))).ToArray();
        }
        if (s.EventName == X.ExternalPower)
            return new[] { ("L1011_RO_EXT_PWR_BREAKER", s.TargetValue == 0 ? 0.0 : 1.0) };
        if (s.EventName == X.InsAlign)
            return new[] { ("ROTARY_INS_MODE_1", 2.0), ("ROTARY_INS_MODE_2", 2.0), ("ROTARY_INS_MODE_3", 2.0) };
        if (s.EventName == X.StandbyPowerCheck)
            return new[] { ("TOGGLE_BATTERY", 1.0), ("TOGGLE_STBY_POWER", 1.0), ("GUARD_STBY_POWER", 0.0) };
        if (s.EventName == X.ApuStart) return new[] { ("L1011_RO_APU_RPM", 1.0) };
        if (s.EventName == X.ApuGenerator)
            return new[] { ("L1011_RO_APU_GEN_FIELD", 1.0), ("L1011_RO_APU_GEN_BREAKER", 1.0) };
        if (s.EventName == X.AcTies)
            return new[] { ("L1011_RO_AC_TIE_1", 1.0), ("L1011_RO_AC_TIE_2", 1.0), ("L1011_RO_AC_TIE_3", 1.0) };
        if (s.EventName == X.FireTest)
            return new[] { "ENG_1", "ENG_2", "ENG_3", "APU" }.Select(u => ($"ROTARY_FIRE_DETECT_TEST_{u}", 2.0)).ToArray();
        if (s.EventName == X.WheelWellFireTest) return new[] { ("ROTARY_WHEEL_WELL_TEST", 1.0) };
        if (s.EventName == X.HydraulicCheck)
            return new[] { "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3", "SWITCH_PTU_AB", "SWITCH_PTU_CD" }
                .Select(k => (k, 0.0)).ToArray();
        if (s.EventName == X.CabinBaro)
            return new[] { ("L1011_RO_CABIN_BARO", 1002.4), ("L1011_RO_ALTIMETER_1", 1002.4) };
        if (s.TargetValueProvider != null && s.EventName!.StartsWith("ROTARY_INS_MODE_", StringComparison.Ordinal))
            return new[] { (s.EventName, (double)L1011FoStateEvaluator.InsNav) };
        if (s.TargetValueProvider is { } provider)
        {
            Assert.Equal(X.CabinAltitudeSelect, s.EventName);
            int feet = provider(PlanState) ?? throw new InvalidOperationException($"{s.Id}: no value with a plan");
            return new[] { ("L1011_RO_CABIN_ALT_SELECTED", feet * L1011FoStateEvaluator.MetresPerFoot) };
        }
        return s.ActionType == FlowStepActionType.SetSwitch
            ? new[] { (s.EventName!, (double)s.TargetValue!.Value) }
            : s.MultiActions.Select(a => (a.EventName, (double)a.TargetValue!.Value)).ToArray();
    }

    /// <summary>
    /// Every line a write step links reads done once the step's work reads done, and does not with nothing
    /// read: a write never ticks a line it does not set. A line no state judges (the standby power check's
    /// own two, a test's own two) is the composite's alone. Only the engine start's ground start and light-off
    /// watch link no line.
    /// </summary>
    [Fact]
    public void Every_line_a_write_links_reads_done_when_the_write_is_done()
    {
        var items = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var writes = Flows.SelectMany(f => f.Steps)
            .Where(s => s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple).ToList();
        Assert.True(writes.Count >= 14, $"only {writes.Count} writes enumerated");
        foreach (var step in writes)
        {
            if (UnlinkedWrites.Contains(step.EventName))
            {
                Assert.Empty(step.LinkedChecklistItemIds);
                continue;
            }
            var done = State(Done(step));
            var unread = State();
            Assert.NotEmpty(step.LinkedChecklistItemIds);
            foreach (var id in step.LinkedChecklistItemIds)
            {
                var line = items[id];
                if (line.StateFieldName is not string field)
                {
                    Assert.Equal(ChecklistItemType.Actionable, line.Type);
                    if (UndoneLaterLines.Contains(id))
                        Assert.Equal(id, step.CompletesChecklistItemId);
                    else
                        Assert.Contains(step.EventName, ManualLineComposites);
                    continue;
                }
                Assert.True(line.StateCondition!(done.GetValue(field)), $"{step.Id} done, but {id} ({field}) does not read done");
                Assert.False(line.StateCondition!(unread.GetValue(field)), $"{step.Id}: {id} ({field}) reads done with nothing read");
            }
        }
    }
}
