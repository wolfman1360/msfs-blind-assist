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
/// The TriStar First Officer's Before Landing flow, its action group and its read-back (the aircraft's approach page),
/// pinned by structure and by behaviour: the step ids and their order; the cabin barometric setting copied from the
/// captain's altimeter, or a silent skip and the Captain asked ([FO-20]); the fuel set for landing, the ground lights
/// and continuous ignition on, the gear lever down; the Captain's flap schedule; the gear confirmed down last, read
/// only, 20 s, then skipped ([FO-13]); the landing autobrake left to the Captain ([FO-14]); nothing cut; the lines each
/// step delivers ([FO-6], [FO-7]); the groups arrival groups ([FO-22]); and that every key is one the definition takes.
/// </summary>
public class L1011FoBeforeLandingTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> BeforeLanding => Flows.Single(f => f.Id == "BEFORE_LANDING");

    private static Step Of(string id) => BeforeLanding.Steps.Single(s => s.Id == id);
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

    private const string GearLever = L1011Levers.GearLeverKey;
    private static readonly string[] Legs = { "L1011_RO_GEAR_LEFT", "L1011_RO_GEAR_NOSE", "L1011_RO_GEAR_RIGHT" };
    private const string CabinBaroReadout = "L1011_RO_CABIN_BARO";
    private const string CaptainAltimeter = "L1011_RO_ALTIMETER_1";

    private static readonly string[] FuelPumps =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    private const string FlapsReminder =
        "Extend the flaps on schedule: 4 at Vref plus 60, 10 at Vref plus 40, 22 at Vref plus 30, 33 at Vref plus 20.";

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        return evaluator;
    }

    /// <summary>The lever and the three legs: down is the lever at 0 with every leg out.</summary>
    private static (string, double)[] Gear(double lever, double legs)
        => Legs.Select(l => (l, legs)).Prepend((GearLever, lever)).ToArray();

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void BeforeLanding_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Before Landing", BeforeLanding.Name);
        Assert.Equal(new[] { "BEFORE_LANDING", "BEFORE_LANDING_CL" }, BeforeLanding.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(BeforeLanding.Description));
    }

    private static readonly string[] StepIds =
    {
        "BL_CABIN_BARO", "BL_CABIN_BARO_CPT", "BL_TRANSFERS_OFF", "BL_FUEL_PUMPS", "BL_CROSSFEEDS", "BL_GROUND_LIGHTS",
        "BL_CONT_IGN", "BL_GEAR", "BL_CPT_FLAPS", "BL_GEAR_DOWN_CHECK",
    };

    [Fact]
    public void BeforeLanding_StepIdsAreInOrder()
        => Assert.Equal(StepIds, BeforeLanding.Steps.Select(s => s.Id));

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        // The provider's step: its target is resolved when the flow runs.
        ("BL_CABIN_BARO", "Cabin barometric setting: captain's altimeter", new (string, int?)[] { (X.CabinBaro, null) }),
        ("BL_TRANSFERS_OFF", "1A and 3A transfers: off", All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
        ("BL_FUEL_PUMPS", "Fuel pumps: all on", All(1, FuelPumps)),
        ("BL_CROSSFEEDS", "Crossfeed valves: closed", All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED")),
        ("BL_GROUND_LIGHTS", "Nose, taxi and runway turnoff lights: on",
            All(1, "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF")),
        ("BL_CONT_IGN", "Continuous ignition: on", All(1, "SWITCH_CONT_IGNITION")),
        ("BL_GEAR", "Landing gear: down", All(0, GearLever)),
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
            Assert.Equal(id == "BL_CABIN_BARO", s.TargetValueProvider != null);
            Assert.NotNull(s.SkipCondition);
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            BeforeLanding.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Every step's lines, from the table: a write its action line then the read-back line it sets (the gear
    /// write only its action line: the read-back's gear line is the legs', delivered by the gear check), the Captain's
    /// flaps its reminder line, the gear check the read-back line it reads, the conditional cabin baro prompt nothing.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["BL_CABIN_BARO"] = new[] { "BL_CABIN_BARO", "BLC_CABIN_BARO" },
        ["BL_CABIN_BARO_CPT"] = Array.Empty<string>(),
        ["BL_TRANSFERS_OFF"] = new[] { "BL_TRANSFERS_OFF", "BLC_TRANSFERS" },
        ["BL_FUEL_PUMPS"] = new[] { "BL_FUEL_PUMPS", "BLC_PUMPS" },
        ["BL_CROSSFEEDS"] = new[] { "BL_CROSSFEEDS", "BLC_CROSSFEEDS" },
        ["BL_GROUND_LIGHTS"] = new[] { "BL_GROUND_LIGHTS", "BLC_GROUND_LIGHTS" },
        ["BL_CONT_IGN"] = new[] { "BL_CONT_IGN", "BLC_CONT_IGN" },
        ["BL_GEAR"] = new[] { "BL_GEAR" },
        ["BL_CPT_FLAPS"] = new[] { "BL_CPT_FLAPS" },
        ["BL_GEAR_DOWN_CHECK"] = new[] { "BLC_GEAR" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in BeforeLanding.Steps)
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
        var linked = BeforeLanding.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static double Wrong(double done) => done == 0 ? 1 : 0;

    [Fact]
    public void EveryPlainWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in BeforeLanding.Steps.Where(x => IsSwitch(x) && x.TargetValueProvider == null))
        {
            n++;
            var done = Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, s.Id == "BL_GEAR" ? 100 : Wrong(wrong[i].Item2));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length - 1, n);
    }

    /// <summary>The gear write is already set once the lever reads down (below 25): at 0, not at 25, off (50), up or
    /// unread.</summary>
    [Fact]
    public void The_gear_write_skips_only_below_25()
    {
        var skip = Of("BL_GEAR").SkipCondition!;
        Assert.True(skip(State((GearLever, 0))), "lever down");
        Assert.True(skip(State((GearLever, 24.9))), "lever below 25");
        Assert.False(skip(State((GearLever, 25))), "lever at 25");
        Assert.False(skip(State((GearLever, 50))), "lever off");
        Assert.False(skip(State((GearLever, 100))), "lever up");
        Assert.False(skip(State()), "lever unread");
    }

    // ------------------------------------------------------------------ the cabin barometric setting ([FO-20])

    /// <summary>
    /// As Before Start's: the captain's altimeter, by now the destination QNH, is the target in hundredths of a
    /// hectopascal; standard or unread is no value, so nothing is sent or spoken and the Captain is asked. The aircraft
    /// moves the cabin baro with the captain's altimeter, so after Ctrl+B both are usually already set; the pair still
    /// catches a cabin baro moved on its own.
    /// </summary>
    [Fact]
    public void Cabin_baro_provider_and_captain_fallback()
    {
        var step = Of("BL_CABIN_BARO");
        var captain = Of("BL_CABIN_BARO_CPT");
        Assert.Equal(X.CabinBaro, step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Cabin barometric setting", captain.Label);
        Assert.Equal("Set the cabin barometric setting to the QNH on the engineer's pressurization panel.", captain.ReminderText);
        Assert.Empty(captain.LinkedChecklistItemIds);
        var ids = BeforeLanding.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(step.Id) + 1, ids.IndexOf(captain.Id));

        // The captain's altimeter at the destination's 1002.4, the cabin baro moved elsewhere: the step writes 1002.40 hPa.
        var elsewhere = State((CaptainAltimeter, 1002.4), (CabinBaroReadout, 1013));
        Assert.Equal(100240, step.TargetValueProvider!(elsewhere));
        Assert.False(step.SkipCondition!(elsewhere));
        Assert.False(captain.SkipCondition!(elsewhere));

        // Standard: no QNH to copy, nothing sent or spoken, and the Captain is asked.
        var standard = State((CaptainAltimeter, 1013.25), (CabinBaroReadout, 1013.25));
        Assert.Null(step.TargetValueProvider!(standard));
        Assert.False(step.SkipCondition!(standard));
        Assert.False(captain.SkipCondition!(standard));

        // The cabin baro following the captain's altimeter: both already set.
        var set = State((CaptainAltimeter, 1002.4), (CabinBaroReadout, 1002.4));
        Assert.True(step.SkipCondition!(set));
        Assert.True(captain.SkipCondition!(set));

        // Unread: no value, and the Captain is asked.
        Assert.Null(step.TargetValueProvider!(State()));
        Assert.False(captain.SkipCondition!(State()));
    }

    // ------------------------------------------------------------------ the gear and the flaps

    /// <summary>[FO-13]: the gear is confirmed down by the LAST step, a read-only wait on the lever and every leg, 20 s,
    /// then skipped aloud; it never writes, and it completes only the read-back's gear line, which reads the same field
    /// under the same rule.</summary>
    [Fact]
    public void The_gear_check_is_last_read_only_twenty_seconds_and_skips()
    {
        var check = BeforeLanding.Steps[^1];
        Assert.Equal("BL_GEAR_DOWN_CHECK", check.Id);
        Assert.Equal("Landing gear: down", check.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, check.ActionType);
        Assert.Equal(L1011GearConfirmation.DownField, check.ConditionFieldName);
        Assert.Equal(20, check.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, check.FailurePolicy);
        Assert.Null(check.EventName);
        Assert.Empty(check.MultiActions);
        Assert.False(check.Condition!(double.NaN), "unread must not pass");
        Assert.False(check.Condition!(0));
        Assert.True(check.Condition!(1));

        Assert.True(check.SkipCondition!(State(Gear(0, 100))), "lever down, every leg out: already set");
        Assert.False(check.SkipCondition!(State(Gear(0, 50))), "the legs still travelling");
        Assert.False(check.SkipCondition!(State(Gear(100, 0))), "gear up");
        Assert.False(check.SkipCondition!(State(Gear(0, 100).Take(3).ToArray())), "a leg unread");

        Assert.Equal(new[] { "BLC_GEAR" }, check.LinkedChecklistItemIds);
        var line = Line("BLC_GEAR");
        Assert.Equal(check.ConditionFieldName, line.StateFieldName);
        foreach (double v in new[] { double.NaN, 0, 1 })
            Assert.Equal(check.Condition!(v), line.StateCondition!(v));
    }

    /// <summary>The flaps stay the Captain's: an unconditional reminder naming the schedule, right before the gear
    /// check, and no step writes the flap handle.</summary>
    [Fact]
    public void The_flaps_are_the_Captains()
    {
        var captain = Of("BL_CPT_FLAPS");
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Flaps on schedule", captain.Label);
        Assert.Equal(FlapsReminder, captain.ReminderText);
        Assert.Null(captain.SkipCondition);
        Assert.Equal("BL_CPT_FLAPS", BeforeLanding.Steps[^2].Id);
        Assert.DoesNotContain(BeforeLanding.Steps.Where(IsSwitch).SelectMany(Actions), a => a.Key == L1011Levers.FlapHandleKey);
    }

    /// <summary>[FO-14]: the landing autobrake is the Captain's; the flow writes no autobrake and the read-back's line is
    /// his reminder, with no setting.</summary>
    [Fact]
    public void The_landing_autobrake_is_never_automated()
    {
        Assert.DoesNotContain(BeforeLanding.Steps.Where(IsSwitch).SelectMany(Actions), a => a.Key == "ROTARY_AUTO_BRAKE_MODE");
        var line = Line("BLC_AUTOBRAKE");
        Assert.Equal(ChecklistItemType.CaptainReminder, line.Type);
        Assert.Null(line.CheckAction);
        Assert.Null(line.StateFieldName);
    }

    // ------------------------------------------------------------------ nothing cut ([FO-25])

    /// <summary>Before Landing turns pumps on and transfers off: the fleet's cut sweep sees no engine, pump, INS, battery
    /// or pack cut in any of its labels, and nothing is left alone.</summary>
    [Fact]
    public void No_step_in_Before_Landing_cuts_anything()
    {
        foreach (var label in BeforeLanding.Steps.Select(s => s.Label).Concat(Group("BEFORE_LANDING").Items.Select(i => i.Label)))
            Assert.Null(FoCutGuardSweepTests.CutKind(label));
        Assert.DoesNotContain(BeforeLanding.Steps, s => s.LeaveAloneWhen != null);
        Assert.DoesNotContain(Group("BEFORE_LANDING").Items, i => i.LeaveAloneWhen != null);
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = BeforeLanding.Steps.Select(s => s.ConditionFieldName).OfType<string>()
            .Concat(L1011GearConfirmation.Inputs).Append(CabinBaroReadout).Append(CaptainAltimeter)
            .Concat(Groups.Where(g => g.Id is "BEFORE_LANDING" or "BEFORE_LANDING_CL").SelectMany(g => g.Items)
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
        Assert.True(fields.Count >= 12, $"only {fields.Count} fields enumerated");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheBeforeLandingGroups_FollowDescent_Named()
    {
        Assert.Equal(new[] { "BEFORE_LANDING", "BEFORE_LANDING_CL" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "BEFORE_LANDING").Take(2));
        Assert.Equal("DESCENT_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "BEFORE_LANDING").Last());
        Assert.Equal("Before Landing", Group("BEFORE_LANDING").Name);
        Assert.Equal("Approach", Group("BEFORE_LANDING_CL").Name);
    }

    /// <summary>[FO-22]: the arrival, never marked by an in-flight start.</summary>
    [Fact]
    public void EveryBeforeLandingGroup_IsArrival_AndNeverDeparture()
    {
        foreach (var id in BeforeLanding.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Arrival.Contains(id), $"{id} is not an arrival group");
            Assert.False(FoDepartureGroups.Departure.Contains(id), $"{id} is a departure group too");
        }
    }

    [Fact]
    public void The_read_back_has_no_check_action()
        => Assert.All(Group("BEFORE_LANDING_CL").Items, i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
    {
        "BL_CABIN_BARO", "BL_TRANSFERS_OFF", "BL_FUEL_PUMPS", "BL_CROSSFEEDS", "BL_GROUND_LIGHTS", "BL_CONT_IGN", "BL_GEAR",
        "BL_CPT_FLAPS",
    };

    /// <summary>The conditional cabin baro prompt, spoken only while the cabin baro does not read the captain's
    /// altimeter, is flow-only.</summary>
    [Fact]
    public void BeforeLanding_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("BEFORE_LANDING").Items.Select(i => i.Id));
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == "BL_CABIN_BARO_CPT");
    }

    [Fact]
    public void BeforeLanding_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("BEFORE_LANDING").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void BeforeLanding_LineKinds()
    {
        foreach (var i in Group("BEFORE_LANDING").Items)
        {
            if (i.Id == "BL_CPT_FLAPS")
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

    /// <summary>Each line reads the state its step delivers: the cabin baro at the captain's altimeter, a multi switch
    /// its synthetic, a single switch its key, the gear lever down.</summary>
    [Theory]
    [InlineData("BL_CABIN_BARO", "FO_CABIN_BARO_SET", 1)]
    [InlineData("BL_TRANSFERS_OFF", "FO_TRANSFERS_OFF", 1)]
    [InlineData("BL_FUEL_PUMPS", "FO_FUEL_PUMPS_ON", 1)]
    [InlineData("BL_CROSSFEEDS", "FO_CROSSFEEDS_CLOSED", 1)]
    [InlineData("BL_GROUND_LIGHTS", "FO_GROUND_LIGHTS_ON", 1)]
    [InlineData("BL_CONT_IGN", "SWITCH_CONT_IGNITION", 1)]
    [InlineData("BL_GEAR", "FO_GEAR_LEVER_DOWN", 1)]
    public void BeforeLanding_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
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

    /// <summary>The evaluator the provider reads: the captain's altimeter at the destination's 1002.4.</summary>
    private static L1011FoStateEvaluator Ready() => State((CaptainAltimeter, 1002.4));

    /// <summary>An aircraft with work for the step: each key the step writes away from its target (the gear lever up
    /// for its down), the cabin baro elsewhere.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, key == GearLever ? 100 : target == 0 ? 1 : 0);
        if (step.EventName == X.CabinBaro)
            ac.Set(CabinBaroReadout, 1013);
        return ac;
    }

    public static IEnumerable<object[]> BeforeLandingLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_LANDING").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft; the
    /// cabin baro step with the target the flow resolves from the same readings.</summary>
    [Theory]
    [MemberData(nameof(BeforeLandingLinesWithACheckAction))]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var ready = Ready();
        var step = Of(id);
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

    /// <summary>With a standard captain's altimeter, the cabin baro tick writes nothing, as its step sends nothing.</summary>
    [Fact]
    public async Task The_cabin_baro_tick_with_no_value_writes_nothing()
    {
        var ac = AircraftFor(Of("BL_CABIN_BARO"));
        var exec = new X();
        exec.SetTransport(ac);
        await Line("BL_CABIN_BARO").CheckAction!(exec, State((CaptainAltimeter, 1013.25))).WaitAsync(Hang);
        Assert.DoesNotContain(ac.Io, e => e.Kind != FakeL1011IoKind.FreshRead);
    }

    // ---- the read-back, word for word ------------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value, or a Captain's reminder.</summary>
    private sealed record ReadBack(string Id, string Label, string? Field = null, double Done = 1, bool Reminder = false);

    private static readonly ReadBack[] Approach =
    {
        new("BLC_CABIN_BARO", "Baro set: set QNH", "FO_CABIN_BARO_SET"),
        new("BLC_TRANSFERS", "1A and 3A xfr switches: close", "FO_TRANSFERS_OFF"),
        new("BLC_PUMPS", "All tank pumps: on", "FO_FUEL_PUMPS_ON"),
        new("BLC_CROSSFEEDS", "All crossfeed valves: close", "FO_CROSSFEEDS_CLOSED"),
        new("BLC_GEAR", "Landing gear: down", L1011GearConfirmation.DownField),
        new("BLC_AUTOBRAKE", "Autobrake: set", Reminder: true),
        new("BLC_GROUND_LIGHTS", "Nose wheel landing, taxi and runway turnoff lights: on", "FO_GROUND_LIGHTS_ON"),
        new("BLC_CONT_IGN", "Continuous ignition: on", "SWITCH_CONT_IGNITION"),
    };

    [Fact]
    public void The_read_back_is_word_for_word()
        => Assert.Equal(Approach.Select(l => (l.Id, l.Label)), Group("BEFORE_LANDING_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in Approach)
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
            var others = L1011FoStateEvaluator.SyntheticKeys.Contains(rb.Field!) ? new[] { 0.0 } : new[] { 0.0, 2, 3, 4, 5 };
            foreach (double other in others)
                Assert.False(line.StateCondition!(other), $"{rb.Id}: {rb.Field} at {other} must not read done");
        }
    }

    /// <summary>A finished run latches the action group and the read-back, and nothing else.</summary>
    [Fact]
    public void BeforeLanding_LatchesItsTwoGroups()
        => Assert.Equal(BeforeLanding.RelatedChecklistGroupIds, BeforeLanding.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The labels and the flap schedule with their figures, and the provider's hectopascals, read the same
    /// under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Texts_and_the_cabin_baro_target_hold_under_a_comma_decimal_culture(string culture)
    {
        var (texts, target) = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "BEFORE_LANDING");
            var all = flow.Steps.Select(s => s.Label).Concat(flow.Steps.Select(s => s.ReminderText ?? ""))
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
            return (all, flow.Steps.Single(s => s.Id == "BL_CABIN_BARO").TargetValueProvider!(State((CaptainAltimeter, 1002.4))));
        });
        Assert.Contains(FlapsReminder, texts);
        Assert.Contains("1A and 3A xfr switches: close", texts);
        Assert.Equal(100240, target);
    }

    // ================================================================== what Before Landing writes

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
        => BeforeLanding.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(19, EveryWrittenKey().Count());
    }

    /// <summary>The one composite is the cabin baro; the one lever is the gear lever, written down.</summary>
    [Fact]
    public void The_composite_and_the_lever()
    {
        Assert.Equal(new[] { X.CabinBaro }, EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct());
        Assert.Equal(new[] { ("BL_GEAR", GearLever, (int?)0) }, EveryWrittenKey().Where(w => L1011Levers.IsLever(w.Key)));
    }

    /// <summary>Every other key is a plain switch row, its target one of the control's positions, and none carries a
    /// toggle: planned from nothing read, each is still a plan ([L1011-4]).</summary>
    [Fact]
    public void EveryOtherKey_IsAPlainSwitchRow_WithNoToggle()
    {
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !L1011Levers.IsLever(w.Key)))
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target!.Value, current: null);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused from an unknown position ({plan.Refusal})");
        }
    }
}
