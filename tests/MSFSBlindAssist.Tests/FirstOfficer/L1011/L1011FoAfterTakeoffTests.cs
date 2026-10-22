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
/// The TriStar First Officer's After Takeoff flow, its action group and its two read-backs (the aircraft's take-off and
/// climb pages), pinned by structure and by behaviour: the step ids and their order; the gear lever up, skipped once it
/// reads up, left alone (step and tick) unless the aircraft reads airborne, and not done when the lever does not move;
/// the flaps left to the Captain, with a read-only
/// wait of up to five minutes for them; every write's label, keys and targets word for word, with its "Already set" on
/// its own state; the gear confirmed up last, read only, 20 s, then skipped ([FO-13]); the lines each step delivers
/// ([FO-6], [FO-7]); and that every key is one the definition takes.
/// </summary>
public class L1011FoAfterTakeoffTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> AfterTakeoff => Flows.Single(f => f.Id == "AFTER_TAKEOFF");

    private static Step Of(string id) => AfterTakeoff.Steps.Single(s => s.Id == id);
    private static ChecklistGroup<X, L1011FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);
    private static Item Line(string id) => Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    private static bool IsSwitch(Step s) =>
        s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple;

    /// <summary>The (key, target) pairs a switch step writes.</summary>
    private static (string Key, int? Target)[] Actions(Step s) => s.ActionType switch
    {
        FlowStepActionType.SetSwitch => new[] { (s.EventName!, s.TargetValue) },
        FlowStepActionType.SetSwitchMultiple => s.MultiActions.Select(a => (a.EventName, a.TargetValue)).ToArray(),
        _ => Array.Empty<(string, int?)>(),
    };

    private static (string, int?)[] All(int target, params string[] keys) => keys.Select(k => (k, (int?)target)).ToArray();

    private const string GearLever = L1011Levers.GearLeverKey;
    private static readonly string[] Legs = { "L1011_RO_GEAR_LEFT", "L1011_RO_GEAR_NOSE", "L1011_RO_GEAR_RIGHT" };

    private const string FlapsReminder =
        "Retract the flaps on schedule: 10 at V2 plus 10, 4 at V2 plus 20, up at V2 plus 60.";

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

    /// <summary>The lever and the three legs: up is the lever at 100 with every leg in.</summary>
    private static (string, double)[] Gear(double lever, double legs)
        => Legs.Select(l => (l, legs)).Prepend((GearLever, lever)).ToArray();

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void AfterTakeoff_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("After Takeoff", AfterTakeoff.Name);
        Assert.Equal(new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "CLIMB_CL" }, AfterTakeoff.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(AfterTakeoff.Description));
    }

    private static readonly string[] StepIds =
    {
        "AT_GEAR", "AT_CPT_FLAPS", "AT_AUTOBRAKE", "AT_GROUND_LIGHTS", "AT_CONT_IGN", "AT_PACKS_ON",
        "AT_CHK_FLAPS_UP", "AT_GEAR_UP_CHECK",
    };

    [Fact]
    public void AfterTakeoff_StepIdsAreInOrder()
        => Assert.Equal(StepIds, AfterTakeoff.Steps.Select(s => s.Id));

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("AT_GEAR", "Landing gear: up", All(100, GearLever)),
        ("AT_AUTOBRAKE", "Autobrake: off", All(1, "ROTARY_AUTO_BRAKE_MODE")),
        ("AT_GROUND_LIGHTS", "Nose, taxi and runway turnoff lights: off",
            All(0, "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF")),
        ("AT_CONT_IGN", "Continuous ignition: off", All(0, "SWITCH_CONT_IGNITION")),
        ("AT_PACKS_ON", "Packs: on", All(1, "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3")),
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
            Assert.Null(s.TargetValueProvider);
            Assert.NotNull(s.SkipCondition);
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            AfterTakeoff.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Every step's lines, from the table: a write its action line then the read-back line it sets (the gear
    /// write only its action line: the read-back's gear line is the legs', delivered by the gear check), the Captain's
    /// flaps its reminder line, each check the read-back line it reads.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["AT_GEAR"] = new[] { "AT_GEAR" },
        ["AT_CPT_FLAPS"] = new[] { "AT_CPT_FLAPS" },
        ["AT_AUTOBRAKE"] = new[] { "AT_AUTOBRAKE", "CLC_AUTOBRAKE" },
        ["AT_GROUND_LIGHTS"] = new[] { "AT_GROUND_LIGHTS", "CLC_GROUND_LIGHTS" },
        ["AT_CONT_IGN"] = new[] { "AT_CONT_IGN", "CLC_CONT_IGN" },
        ["AT_PACKS_ON"] = new[] { "AT_PACKS_ON", "CLC_PACKS" },
        ["AT_CHK_FLAPS_UP"] = new[] { "ATC_FLAPS" },
        ["AT_GEAR_UP_CHECK"] = new[] { "ATC_GEAR" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in AfterTakeoff.Steps)
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
        var linked = AfterTakeoff.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static double Wrong(double done) => done == 0 ? 1 : 0;

    [Fact]
    public void EveryWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in AfterTakeoff.Steps.Where(IsSwitch))
        {
            n++;
            var done = Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, Wrong(wrong[i].Item2));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length, n);
    }

    // ------------------------------------------------------------------ the gear

    /// <summary>The gear write is already set once the lever reads up (above 75): at 100, not at 75, off (50), down or
    /// unread.</summary>
    [Fact]
    public void The_gear_write_skips_only_above_75()
    {
        var skip = Of("AT_GEAR").SkipCondition!;
        Assert.True(skip(State((GearLever, 100))), "lever up");
        Assert.True(skip(State((GearLever, 75.1))), "lever past 75");
        Assert.False(skip(State((GearLever, 75))), "lever at 75");
        Assert.False(skip(State((GearLever, 50))), "lever off");
        Assert.False(skip(State((GearLever, 0))), "lever down");
        Assert.False(skip(State()), "lever unread");
    }

    /// <summary>A gear lever that does not move (the fake never delivers it) reads back not done, and the step is not done
    /// (Skip, spoken by the flow).</summary>
    [Fact]
    public async Task A_gear_lever_that_does_not_move_reads_back_not_done()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(GearLever, 0);
        ac.NeverDeliver(GearLever);
        var exec = new X();
        exec.SetTransport(ac);

        Assert.False(await exec.ExecuteStepAsync(Of("AT_GEAR")).WaitAsync(Hang));
        Assert.Contains((GearLever, 100.0), ac.Writes);
        Assert.Equal(0, ac.Get(GearLever));
    }

    /// <summary>[FO-13]: the gear is confirmed up by the LAST step, a read-only wait on the lever and every leg, 20 s,
    /// then skipped aloud; it never writes, and it completes only the read-back's gear line, which reads the same field
    /// under the same rule.</summary>
    [Fact]
    public void The_gear_check_is_last_read_only_twenty_seconds_and_skips()
    {
        var check = AfterTakeoff.Steps[^1];
        Assert.Equal("AT_GEAR_UP_CHECK", check.Id);
        Assert.Equal("Landing gear: up", check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal(L1011GearConfirmation.UpField, check.ConditionFieldName);
        Assert.Equal(20, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.Null(check.EventName);
        Assert.Empty(check.MultiActions);
        Assert.False(check.Condition!(double.NaN), "unread must not pass");
        Assert.False(check.Condition!(0));
        Assert.True(check.Condition!(1));

        Assert.True(check.SkipCondition!(State(Gear(100, 0))), "lever up, every leg in: already set");
        Assert.False(check.SkipCondition!(State(Gear(100, 50))), "the legs still travelling");
        Assert.False(check.SkipCondition!(State(Gear(0, 100))), "gear down");
        Assert.False(check.SkipCondition!(State(Gear(100, 0).Take(3).ToArray())), "a leg unread");

        Assert.Equal(new[] { "ATC_GEAR" }, check.LinkedChecklistItemIds);
        var line = Line("ATC_GEAR");
        Assert.Equal(check.ConditionFieldName, line.StateFieldName);
        foreach (double v in new[] { double.NaN, 0, 1 })
            Assert.Equal(check.Condition!(v), line.StateCondition!(v));
    }

    // ------------------------------------------------------------------ the flaps: the Captain's

    /// <summary>The flaps stay the Captain's in flight: his reminder is unconditional and names the schedule, no step
    /// writes the flap handle, and a read-only check waits up to five minutes for the handle up, completing only the
    /// take-off page's flaps line.</summary>
    [Fact]
    public void The_flaps_are_the_Captains_with_a_five_minute_check()
    {
        var captain = Of("AT_CPT_FLAPS");
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Flaps up on schedule", captain.Label);
        Assert.Equal(FlapsReminder, captain.ReminderText);
        Assert.Null(captain.SkipCondition);

        Assert.DoesNotContain(AfterTakeoff.Steps.Where(IsSwitch).SelectMany(Actions), a => a.Key == L1011Levers.FlapHandleKey);

        var check = Of("AT_CHK_FLAPS_UP");
        Assert.Equal("Flaps: up", check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal("FO_FLAPS_UP", check.ConditionFieldName);
        Assert.Equal(300, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.False(check.Condition!(double.NaN));
        Assert.False(check.Condition!(0));
        Assert.True(check.Condition!(1));
        Assert.True(check.SkipCondition!(State((L1011Levers.FlapHandleKey, 0))), "the handle up: already set");
        Assert.False(check.SkipCondition!(State((L1011Levers.FlapHandleKey, 2))));
        Assert.False(check.SkipCondition!(State()));

        Assert.Equal(new[] { "ATC_FLAPS" }, check.LinkedChecklistItemIds);
        var line = Line("ATC_FLAPS");
        Assert.Equal(check.ConditionFieldName, line.StateFieldName);
        foreach (double v in new[] { double.NaN, 0, 1 })
            Assert.Equal(check.Condition!(v), line.StateCondition!(v));

        var ids = AfterTakeoff.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf("AT_GEAR_UP_CHECK") - 1, ids.IndexOf("AT_CHK_FLAPS_UP"));
    }

    [Fact]
    public void Only_the_gear_is_left_alone_and_nothing_cuts()
    {
        Assert.Equal(new[] { "AT_GEAR" }, AfterTakeoff.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));
        Assert.Equal(new[] { "AT_GEAR" }, Group("AFTER_TAKEOFF").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
        foreach (var label in AfterTakeoff.Steps.Select(s => s.Label).Concat(Group("AFTER_TAKEOFF").Items.Select(i => i.Label)))
            Assert.Null(FoCutGuardSweepTests.CutKind(label));
    }

    private const string GearText = "Gear left down: on the ground.";

    /// <summary>Gear up goes ahead only with the aircraft read airborne: the step and its tick are left alone, aloud, on
    /// the ground or with the on-ground flag unread, and nothing is written. The gauge's script refuses gear up on the
    /// ground, but no live check has seen it, so the First Officer never relies on it.</summary>
    [Fact]
    public void Gear_up_is_left_alone_unless_read_airborne()
    {
        foreach (var (leaves, text) in new[] { (Of("AT_GEAR").LeaveAloneWhen!, Of("AT_GEAR").LeaveAloneText),
                     (Line("AT_GEAR").LeaveAloneWhen!, Line("AT_GEAR").LeaveAloneText) })
        {
            Assert.Equal(GearText, text);
            Assert.True(leaves(State(("SIM_ON_GROUND", 1))), "on the ground");
            Assert.True(leaves(State()), "the on-ground flag unread");
            Assert.False(leaves(State(("SIM_ON_GROUND", 0))), "airborne");
        }
    }

    [Fact]
    public void The_gear_tick_through_the_checklist_manager_is_refused_aloud_on_the_ground()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var ac = AircraftFor(Of("AT_GEAR"));
        var exec = new X();
        exec.SetTransport(ac);
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected(("SIM_ON_GROUND", 1), (GearLever, 0)), exec, groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "AT_GEAR");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(GearText, spoken);
        Assert.False(line.IsChecked);
        Assert.DoesNotContain(ac.Io, e => e.Kind == FakeL1011IoKind.Write);
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = AfterTakeoff.Steps.Select(s => s.ConditionFieldName).OfType<string>()
            .Concat(L1011GearConfirmation.Inputs)
            .Concat(Groups.Where(g => g.Id is "AFTER_TAKEOFF" or "AFTER_TAKEOFF_CL" or "CLIMB_CL").SelectMany(g => g.Items)
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
        Assert.True(fields.Count >= 9, $"only {fields.Count} fields enumerated");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheAfterTakeoffGroups_FollowBeforeTakeoff_Named()
    {
        Assert.Equal(new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "CLIMB_CL" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "AFTER_TAKEOFF").Take(3));
        Assert.Equal("BEFORE_TAKEOFF_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "AFTER_TAKEOFF").Last());
        Assert.Equal("After Takeoff", Group("AFTER_TAKEOFF").Name);
        Assert.Equal("Take Off", Group("AFTER_TAKEOFF_CL").Name);
        Assert.Equal("Climb", Group("CLIMB_CL").Name);
    }

    /// <summary>[FO-22]: an in-flight start marks the departure lists through After Takeoff, these three included.</summary>
    [Fact]
    public void EveryAfterTakeoffGroup_IsDeparture_AndNeverArrival()
    {
        foreach (var id in AfterTakeoff.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Departure.Contains(id), $"{id} is not a departure group");
            Assert.False(FoDepartureGroups.Arrival.Contains(id), $"{id} is an arrival group too");
        }
    }

    [Fact]
    public void The_read_backs_have_no_check_action()
        => Assert.All(Group("AFTER_TAKEOFF_CL").Items.Concat(Group("CLIMB_CL").Items),
            i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
        { "AT_GEAR", "AT_CPT_FLAPS", "AT_AUTOBRAKE", "AT_GROUND_LIGHTS", "AT_CONT_IGN", "AT_PACKS_ON" };

    [Fact]
    public void AfterTakeoff_ActionLines_InTheFlowsOrder()
        => Assert.Equal(ActionLineIds, Group("AFTER_TAKEOFF").Items.Select(i => i.Id));

    /// <summary>Ticked or run, a line says the same thing; the Captain's line keeps his reminder.</summary>
    [Fact]
    public void AfterTakeoff_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("AFTER_TAKEOFF").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void AfterTakeoff_LineKinds()
    {
        foreach (var i in Group("AFTER_TAKEOFF").Items)
        {
            if (i.Id == "AT_CPT_FLAPS")
            {
                Assert.Equal(ChecklistItemType.CaptainReminder, i.Type);
                Assert.Null(i.CheckAction);
                Assert.Equal(FlapsReminder, i.ReminderText);
                continue;
            }
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each line reads the state its step delivers: the gear lever up, a single switch its key, a multi switch
    /// its synthetic.</summary>
    [Theory]
    [InlineData("AT_GEAR", "FO_GEAR_LEVER_UP", 1)]
    [InlineData("AT_AUTOBRAKE", "ROTARY_AUTO_BRAKE_MODE", 1)]
    [InlineData("AT_GROUND_LIGHTS", "FO_GROUND_LIGHTS_OFF", 1)]
    [InlineData("AT_CONT_IGN", "SWITCH_CONT_IGNITION", 0)]
    [InlineData("AT_PACKS_ON", "FO_PACKS_ON", 1)]
    public void AfterTakeoff_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
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

    /// <summary>An aircraft set up so the step has work to do: each key the step writes away from its target (the gear
    /// lever down for its up).</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step))
            ac.Set(key, target == 0 ? 1 : 0);
        return ac;
    }

    public static IEnumerable<object[]> AfterTakeoffLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "AFTER_TAKEOFF").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft.</summary>
    [Theory]
    [MemberData(nameof(AfterTakeoffLinesWithACheckAction))]
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

    // ---- the read-backs, word for word -----------------------------------------------------------------------------

    /// <summary>A read-back line: its label, its field and its done value.</summary>
    private sealed record ReadBack(string Id, string Label, string Field, double Done = 1);

    private static readonly ReadBack[] TakeOff =
    {
        new("ATC_GEAR", "Positive rate of climb: landing gear up", L1011GearConfirmation.UpField),
        new("ATC_FLAPS", "At V2 plus 60: set flaps up", "FO_FLAPS_UP"),
    };

    private static readonly ReadBack[] Climb =
    {
        new("CLC_AUTOBRAKE", "Autobrake: off", "ROTARY_AUTO_BRAKE_MODE", 1),
        new("CLC_GROUND_LIGHTS", "Nose wheel landing, taxi and runway turnoff lights: off", "FO_GROUND_LIGHTS_OFF"),
        new("CLC_CONT_IGN", "Continuous ignition: off", "SWITCH_CONT_IGNITION", 0),
        new("CLC_PACKS", "Packs: on", "FO_PACKS_ON"),
    };

    [Fact]
    public void The_take_off_read_back_is_word_for_word()
        => Assert.Equal(TakeOff.Select(l => (l.Id, l.Label)), Group("AFTER_TAKEOFF_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void The_climb_read_back_is_word_for_word()
        => Assert.Equal(Climb.Select(l => (l.Id, l.Label)), Group("CLIMB_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in TakeOff.Concat(Climb))
        {
            var line = Line(rb.Id);
            Assert.Null(line.CheckAction);
            Assert.Empty(line.AdditionalStateFields);
            Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
            Assert.Equal(RevertBehavior.RevertToState, line.RevertBehavior);
            Assert.Equal(rb.Field, line.StateFieldName);
            Assert.True(line.StateCondition!(rb.Done), $"{rb.Id}: {rb.Field} at {rb.Done} must read done");
            Assert.False(line.StateCondition!(double.NaN), $"{rb.Id}: unread must not read done");
            var others = L1011FoStateEvaluator.SyntheticKeys.Contains(rb.Field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
            foreach (double other in others.Where(v => v != rb.Done))
                Assert.False(line.StateCondition!(other), $"{rb.Id}: {rb.Field} at {other} must not read done");
        }
    }

    /// <summary>A finished run latches the action group and both read-backs, and nothing else.</summary>
    [Fact]
    public void AfterTakeoff_LatchesItsThreeGroups()
        => Assert.Equal(AfterTakeoff.RelatedChecklistGroupIds, AfterTakeoff.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The Captain's flap schedule and the read-back's speeds read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Texts_hold_under_a_comma_decimal_culture(string culture)
    {
        var texts = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "AFTER_TAKEOFF");
            return flow.Steps.Select(s => s.Label).Concat(flow.Steps.Select(s => s.ReminderText ?? ""))
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
        });
        Assert.Contains(FlapsReminder, texts);
        Assert.Contains("At V2 plus 60: set flaps up", texts);
    }

    // ================================================================== what After Takeoff writes

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
        => AfterTakeoff.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target!.Value)));

    [Fact]
    public void Every_key_is_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !Definition.Value.FoAcceptsKey(w.Key)).Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(9, EveryWrittenKey().Count());
    }

    /// <summary>No composite; the one lever is the gear lever, written up.</summary>
    [Fact]
    public void The_one_lever_is_the_gear_and_no_composite_is_written()
    {
        Assert.DoesNotContain(EveryWrittenKey(), w => X.IsPseudoKey(w.Key));
        Assert.Equal(new[] { ("AT_GEAR", GearLever, 100) }, EveryWrittenKey().Where(w => L1011Levers.IsLever(w.Key)));
    }

    /// <summary>Every other key is a plain switch row, its target one of the control's positions, and none carries a
    /// toggle: planned from nothing read, each is still a plan ([L1011-4]).</summary>
    [Fact]
    public void EveryOtherKey_IsAPlainSwitchRow_WithNoToggle()
    {
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !L1011Levers.IsLever(w.Key)))
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target, current: null);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused from an unknown position ({plan.Refusal})");
        }
    }
}
