using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// [FO-6] and [FO-7] on the TriStar: every line in a group the Power Up flow latches has a step behind it,
/// a write that sets it or a read-only check that reads it, so a finished flow never ticks and latches a
/// line the aircraft does not show. The standby power check's own line, which no state judges, is
/// delivered by the composite step; the Captain's reminder is delivered by the reminder itself.
/// </summary>
public class L1011FoFlowChecklistLinkTests
{
    private static readonly List<FlowDefinition<L1011FoStateEvaluator>> Flows = L1011FoFlowDefinitions.Build();
    private static readonly List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups = L1011FoChecklistDefinitions.Build();

    private static List<(FlowDefinition<L1011FoStateEvaluator> Flow, List<ChecklistItem<X, L1011FoStateEvaluator>> Items)> Latched()
        => FlowChecklistLinkAudit.FlowsWithItems(Flows, Groups,
            f => f.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    [Fact]
    public void PowerUp_LatchesItsActionGroupAndTheCockpitSafetyInspection()
        => Assert.Equal(new[] { "POWER_UP", "POWER_UP_CL" },
            Flows.Single(f => f.Id == "POWER_UP").CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    [Fact]
    public void No_line_is_latched_with_no_step_behind_it()
        => Assert.Empty(FlowChecklistLinkAudit.UndeliveredLines(Latched()));

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

    [Fact]
    public void No_step_links_a_StayComplete_line()
    {
        var stay = Groups.SelectMany(g => g.Items)
            .Where(i => i.RevertBehavior == RevertBehavior.StayComplete && i.IsAutoDetectable)
            .Select(i => i.Id).ToList();
        Assert.Empty(stay);
    }

    // A read-only check never writes: every link of a check step is to a line it reads.
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
                    Assert.True(items[id].StateFieldName == step.ConditionFieldName,
                        $"{flow.Id}/{step.Id} reads {step.ConditionFieldName} but completes {id}, which reads {items[id].StateFieldName}");
                }
        Assert.Equal(5, n);
    }

    // ------------------------------------------------------------------ a write completes only lines it sets

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        return evaluator;
    }

    /// <summary>What the aircraft reads once the step's work is done: its keys at their targets; the external
    /// power breaker closed for the external power composite; the battery on and standby power armed under
    /// its closed guard after the standby power check.</summary>
    private static (string Key, double Value)[] Done(Step s)
    {
        if (s.EventName == X.ExternalPower) return new[] { ("L1011_RO_EXT_PWR_BREAKER", 1.0) };
        if (s.EventName == X.StandbyPowerCheck)
            return new[] { ("TOGGLE_BATTERY", 1.0), ("TOGGLE_STBY_POWER", 1.0), ("GUARD_STBY_POWER", 0.0) };
        return s.ActionType == FlowStepActionType.SetSwitch
            ? new[] { (s.EventName!, (double)s.TargetValue!.Value) }
            : s.MultiActions.Select(a => (a.EventName, (double)a.TargetValue!.Value)).ToArray();
    }

    /// <summary>
    /// Every line a write step links reads done once the step's work reads done, and does not with nothing
    /// read: a write never ticks a line it does not set. A line no state judges (the standby power check's
    /// own two) is the composite's alone.
    /// </summary>
    [Fact]
    public void Every_line_a_write_links_reads_done_when_the_write_is_done()
    {
        var items = Groups.SelectMany(g => g.Items).ToDictionary(i => i.Id, StringComparer.Ordinal);
        var writes = Flows.SelectMany(f => f.Steps)
            .Where(s => s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple).ToList();
        Assert.Equal(14, writes.Count);
        foreach (var step in writes)
        {
            var done = State(Done(step));
            var unread = State();
            Assert.NotEmpty(step.LinkedChecklistItemIds);
            foreach (var id in step.LinkedChecklistItemIds)
            {
                var line = items[id];
                if (line.StateFieldName is not string field)
                {
                    Assert.Equal(X.StandbyPowerCheck, step.EventName);
                    Assert.Equal(ChecklistItemType.Actionable, line.Type);
                    continue;
                }
                Assert.True(line.StateCondition!(done.GetValue(field)), $"{step.Id} done, but {id} ({field}) does not read done");
                Assert.False(line.StateCondition!(unread.GetValue(field)), $"{step.Id}: {id} ({field}) reads done with nothing read");
            }
        }
    }
}
