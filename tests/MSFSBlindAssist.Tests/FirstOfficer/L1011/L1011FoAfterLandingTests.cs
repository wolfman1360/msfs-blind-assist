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
/// The TriStar First Officer's After Landing flow and its group (the after landing page's lines all read the same state
/// as the group's, so it adds none), pinned by structure and by behaviour: the step ids and their order, the ground guard first, read only, Stop;
/// every write's label, keys and targets word for word, with its "Already set" on its own state; the stabilizer trim
/// zeroed and verified on its readout; the flaps up on the ground only, the step and its tick left alone otherwise, and
/// the anti-skid, air data heat, stabilizer trim and speed brake ticks too; the APU master on and the APU started to its
/// spin-up; no fuel, pump, INS, battery or pack cut; the lines each step delivers ([FO-6], [FO-7]); the groups arrival
/// groups ([FO-22]); and that every key is one the definition takes, the anti-skid's toggle-carrying key only ever
/// written over its own position read ([L1011-4]).
/// </summary>
public class L1011FoAfterLandingTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> AfterLanding => Flows.Single(f => f.Id == "AFTER_LANDING");

    private static Step Of(string id) => AfterLanding.Steps.Single(s => s.Id == id);
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

    private const string StabTrim = L1011Levers.StabTrimKey;
    private const string ElevatorTrim = "L1011_RO_ELEVATOR_TRIM";
    private const string SpeedBrake = L1011Levers.SpeedBrakeKey;
    private const string FlapHandle = L1011Levers.FlapHandleKey;
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuMaster = "TOGGLE_APU_POWER";
    private const string Ground = "SIM_ON_GROUND";

    private const string FlapsText = "Flaps left as they are: not read on the ground.";
    private const string AntiSkidText = "Anti-skid left on: not read on the ground.";
    private const string AirDataText = "Air data heat left on: not read on the ground.";
    private const string StabTrimText = "Stabilizer trim left as it is: not read on the ground.";
    private const string SpeedBrakeText = "Speed brake lever left as it is: not read on the ground.";

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
    public void AfterLanding_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("After Landing", AfterLanding.Name);
        Assert.Equal(new[] { "AFTER_LANDING" }, AfterLanding.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(AfterLanding.Description));
    }

    private static readonly string[] StepIds =
    {
        "AL_ON_GROUND",
        "AL_ANTISKID", "AL_AUTOBRAKE", "AL_LANDING_LIGHTS", "AL_STROBES", "AL_CONT_IGN", "AL_AIR_DATA", "AL_WINDSHIELD",
        "AL_SIDE_WINDOWS", "AL_STAB_TRIM", "AL_SPEED_BRAKE", "AL_FLAPS_UP", "AL_RADAR", "AL_TRANSFERS_OFF", "AL_APU_MASTER",
        "AL_APU_START",
    };

    /// <summary>The action group's lines: every step but the guard.</summary>
    private static readonly string[] ActionLineIds = StepIds.Skip(1).ToArray();

    [Fact]
    public void AfterLanding_StepIdsAreInOrder()
        => Assert.Equal(StepIds, AfterLanding.Steps.Select(s => s.Id));

    /// <summary>Started airborne (a mis-start on the approach), the flow would turn the air data heat and anti-skid off,
    /// zero the trim and stow the speed brake lever: it opens with the fleet's ground guard, a read-only Stop of 5 s that
    /// fails unread and links no line, so it stops whole.</summary>
    [Fact]
    public void The_ground_guard_comes_first_a_read_only_stop_that_links_nothing()
    {
        var guard = AfterLanding.Steps[0];
        Assert.Equal(("AL_ON_GROUND", "On the ground", "SIM_ON_GROUND"), (guard.Id, guard.Label, guard.ConditionFieldName));
        Assert.Equal(FlowStepActionType.WaitForCondition, guard.ActionType);
        Assert.Equal(FlowStepFailurePolicy.Stop, guard.FailurePolicy);
        Assert.Equal(5, guard.TimeoutSeconds);
        Assert.Null(guard.SkipCondition);
        Assert.Null(guard.LeaveAloneWhen);
        Assert.Empty(guard.LinkedChecklistItemIds);
        Assert.False(guard.Condition!(double.NaN), "unread must fail");
        Assert.False(guard.Condition!(0));
        Assert.True(guard.Condition!(1));
        Assert.Equal(1, AfterLanding.Steps.Count(s => s.ActionType == FlowStepActionType.WaitForCondition));
    }

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("AL_ANTISKID", "Anti-skid: off", All(0, "SWITCH_ANTISKID_ON")),
        ("AL_AUTOBRAKE", "Autobrake: off", All(1, "ROTARY_AUTO_BRAKE_MODE")),
        ("AL_LANDING_LIGHTS", "Landing lights: off", All(0, "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT")),
        ("AL_STROBES", "Strobe lights: off", All(0, "SWITCH_STROBE_LIGHT")),
        ("AL_CONT_IGN", "Continuous ignition: off", All(0, "SWITCH_CONT_IGNITION")),
        ("AL_AIR_DATA", "Air data heat: off",
            All(0, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT")),
        ("AL_WINDSHIELD", "Windshield heat: idle", All(0, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO")),
        ("AL_SIDE_WINDOWS", "Side window heat: off",
            All(0, "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR")),
        ("AL_STAB_TRIM", "Stabilizer trim: zero", All(0, StabTrim)),
        ("AL_SPEED_BRAKE", "Speed brake lever: forward", All(0, SpeedBrake)),
        ("AL_FLAPS_UP", "Flaps: up", All(0, FlapHandle)),
        ("AL_RADAR", "Weather radar: off", All(1, "SWITCH_WX_OFF")),
        ("AL_TRANSFERS_OFF", "1A and 3A transfers: off", All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
        ("AL_APU_MASTER", "APU master: on", All(1, ApuMaster)),
        ("AL_APU_START", "APU: start", All(1, X.ApuStart)),
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
            AfterLanding.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Every step's lines, from the table: each write its own line, the guard none.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["AL_ON_GROUND"] = Array.Empty<string>(),
        ["AL_ANTISKID"] = new[] { "AL_ANTISKID" },
        ["AL_AUTOBRAKE"] = new[] { "AL_AUTOBRAKE" },
        ["AL_LANDING_LIGHTS"] = new[] { "AL_LANDING_LIGHTS" },
        ["AL_STROBES"] = new[] { "AL_STROBES" },
        ["AL_CONT_IGN"] = new[] { "AL_CONT_IGN" },
        ["AL_AIR_DATA"] = new[] { "AL_AIR_DATA" },
        ["AL_WINDSHIELD"] = new[] { "AL_WINDSHIELD" },
        ["AL_SIDE_WINDOWS"] = new[] { "AL_SIDE_WINDOWS" },
        ["AL_STAB_TRIM"] = new[] { "AL_STAB_TRIM" },
        ["AL_SPEED_BRAKE"] = new[] { "AL_SPEED_BRAKE" },
        ["AL_FLAPS_UP"] = new[] { "AL_FLAPS_UP" },
        ["AL_RADAR"] = new[] { "AL_RADAR" },
        ["AL_TRANSFERS_OFF"] = new[] { "AL_TRANSFERS_OFF" },
        ["AL_APU_MASTER"] = new[] { "AL_APU_MASTER" },
        ["AL_APU_START"] = new[] { "AL_APU_START" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in AfterLanding.Steps)
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
        var linked = AfterLanding.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    /// <summary>A reading the other way from <paramref name="done"/>: the speed brake's other way is half out, since 1 %
    /// still reads forward.</summary>
    private static double Wrong(string key, double done) => key == SpeedBrake ? 50 : done == 0 ? 1 : 0;

    /// <summary>Every write whose own keys are read (all but the stabilizer trim, verified on its readout, and the APU
    /// start, judged on its speed) is "Already set" only when every key reads its target, never with one wrong or
    /// unread.</summary>
    [Fact]
    public void EveryReadableWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in AfterLanding.Steps.Where(x => IsSwitch(x) && x.Id is not ("AL_STAB_TRIM" or "AL_APU_START")))
        {
            n++;
            var done = Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, Wrong(wrong[i].Key, wrong[i].Item2));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length - 2, n);
    }

    /// <summary>The stabilizer trim's typed entry is write-only: its "Already set" and its lines read the trim readout,
    /// zero within 0.2 degrees either way.</summary>
    [Fact]
    public void The_stab_trim_is_already_set_on_its_readout_within_a_fifth_of_a_degree()
    {
        var skip = Of("AL_STAB_TRIM").SkipCondition!;
        Assert.True(skip(State((ElevatorTrim, 0))), "zero");
        Assert.True(skip(State((ElevatorTrim, 0.19))), "0.19 nose up");
        Assert.True(skip(State((ElevatorTrim, -0.19))), "0.19 nose down");
        Assert.False(skip(State((ElevatorTrim, 0.2))), "0.2");
        Assert.False(skip(State((ElevatorTrim, 2.38))), "the take-off trim");
        Assert.False(skip(State((StabTrim, 0))), "the typed entry alone is not the trim");
        Assert.False(skip(State()), "unread");
    }

    /// <summary>The APU start is "Already set" once the APU turns (1 %), starting or running, never at a standstill or
    /// unread; the step presses once and waits for the spin-up, the wait to running being Parking's.</summary>
    [Fact]
    public void The_APU_start_is_already_set_once_the_APU_turns()
    {
        var skip = Of("AL_APU_START").SkipCondition!;
        Assert.True(skip(State((ApuRpm, 1))), "turning");
        Assert.True(skip(State((ApuRpm, 100))), "running");
        Assert.False(skip(State((ApuRpm, 0.9))), "not turning");
        Assert.False(skip(State()), "unread");
        var ids = AfterLanding.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf("AL_APU_MASTER") + 1, ids.IndexOf("AL_APU_START"));
        Assert.Equal("AL_APU_START", ids[^1]);
    }

    // ------------------------------------------------------------------ the flaps up, on the ground only

    /// <summary>The flaps move on the ground only: the step and its tick are left alone airborne or with the ground
    /// unread, saying the flaps stay; on the ground (parked or rolling) they go ahead.</summary>
    [Fact]
    public void Flaps_up_is_left_alone_off_the_ground()
    {
        var step = Of("AL_FLAPS_UP");
        var tick = Line("AL_FLAPS_UP");
        Assert.Equal(FlapsText, step.LeaveAloneText);
        Assert.Equal(FlapsText, tick.LeaveAloneText);
        foreach (var leaves in new Func<L1011FoStateEvaluator, bool>[] { step.LeaveAloneWhen!, tick.LeaveAloneWhen! })
        {
            Assert.True(leaves(State((Ground, 0), (FlapHandle, 4))), "airborne");
            Assert.True(leaves(State((FlapHandle, 4))), "the ground unread");
            Assert.False(leaves(State((Ground, 1), (FlapHandle, 4))), "on the ground");
            Assert.False(leaves(State((Ground, 1), (FlapHandle, 4), ("L1011_RO_N3_1", 1.5), ("L1011_RO_N3_2", 1.5),
                ("L1011_RO_N3_3", 1.5), ("TOGGLE_CUTOFF_ENG_1", 0), ("TOGGLE_CUTOFF_ENG_2", 0), ("TOGGLE_CUTOFF_ENG_3", 0))),
                "parked");
        }
    }

    [Fact]
    public void The_flaps_tick_through_the_checklist_manager_is_refused_aloud_airborne()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected((Ground, 0), (FlapHandle, 4)), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "AL_FLAPS_UP");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(FlapsText, spoken);
        Assert.False(line.IsChecked);
    }

    /// <summary>In the flow only the flaps are left alone (the ground guard stands before every step); in the action group
    /// the anti-skid, air data heat, stabilizer trim, speed brake and flaps ticks are, each off the ground.</summary>
    [Fact]
    public void Only_the_ground_only_lines_are_left_alone()
    {
        Assert.Equal(new[] { "AL_FLAPS_UP" }, AfterLanding.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));
        Assert.Equal(new[] { "AL_ANTISKID", "AL_AIR_DATA", "AL_STAB_TRIM", "AL_SPEED_BRAKE", "AL_FLAPS_UP" },
            Group("AFTER_LANDING").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
        Assert.DoesNotContain(AfterLanding.Steps, s => s.RequiresStepId != null || s.RunOnlyIfStepSent != null);
    }

    /// <summary>
    /// A tick is the pilot's own and needs no flow, so it carries the guard itself: in the air the anti-skid off, the air
    /// data heat off, the trim zeroed or the speed brake lever stowed (disarming an armed speed brake) would each hurt the
    /// landing. Each is left alone airborne or with the ground unread, saying what stays as it is, and goes ahead on the
    /// ground.
    /// </summary>
    [Theory]
    [InlineData("AL_ANTISKID", AntiSkidText)]
    [InlineData("AL_AIR_DATA", AirDataText)]
    [InlineData("AL_STAB_TRIM", StabTrimText)]
    [InlineData("AL_SPEED_BRAKE", SpeedBrakeText)]
    public void A_ground_only_tick_is_left_alone_off_the_ground(string id, string text)
    {
        var tick = Line(id);
        Assert.Equal(text, tick.LeaveAloneText);
        Assert.True(tick.LeaveAloneWhen!(State((Ground, 0))), "airborne");
        Assert.True(tick.LeaveAloneWhen!(State()), "the ground unread");
        Assert.False(tick.LeaveAloneWhen!(State((Ground, 1))), "on the ground");
    }

    [Theory]
    [InlineData("AL_ANTISKID", AntiSkidText)]
    [InlineData("AL_AIR_DATA", AirDataText)]
    [InlineData("AL_STAB_TRIM", StabTrimText)]
    [InlineData("AL_SPEED_BRAKE", SpeedBrakeText)]
    public void A_ground_only_tick_through_the_checklist_manager_is_refused_aloud_airborne(string id, string text)
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var ac = AircraftFor(Of(id));
        var exec = new X();
        exec.SetTransport(ac);
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected((Ground, 0)), exec, groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == id);

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(text, spoken);
        Assert.False(line.IsChecked);
        Assert.DoesNotContain(ac.Io, e => e.Kind == FakeL1011IoKind.Write);
    }

    // ------------------------------------------------------------------ nothing cut ([FO-25])

    /// <summary>After Landing cuts no fuel, pump, INS, battery, engine or pack: the fleet's cut sweep sees none in any of
    /// its labels, the flow's or the group's. The manual's tank pumps and crossfeeds wait for Parking, once the engines
    /// are cut.</summary>
    [Fact]
    public void No_step_in_After_Landing_cuts_fuel_or_pumps()
    {
        var labels = AfterLanding.Steps.Select(s => s.Label)
            .Concat(Group("AFTER_LANDING").Items.Select(i => i.Label)).ToList();
        Assert.True(labels.Count >= 30, $"only {labels.Count} labels enumerated");
        foreach (var label in labels)
            Assert.Null(FoCutGuardSweepTests.CutKind(label));
        Assert.DoesNotContain(AfterLanding.Steps.Where(IsSwitch).SelectMany(Actions),
            a => a.Key.StartsWith("SWITCH_FUEL_PUMP_", StringComparison.Ordinal)
                 || a.Key.StartsWith("TOGGLE_CUTOFF_ENG_", StringComparison.Ordinal)
                 || a.Key.StartsWith("ROTARY_INS_MODE_", StringComparison.Ordinal)
                 || a.Key.StartsWith("SWITCH_PACK_FLOW_ENG_", StringComparison.Ordinal)
                 || a.Key.Contains("XFEED", StringComparison.Ordinal)
                 || a.Key == "TOGGLE_BATTERY");
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = new[] { ElevatorTrim, ApuRpm, Ground, SpeedBrake, FlapHandle }
            .Concat(Groups.Where(g => g.Id is "AFTER_LANDING").SelectMany(g => g.Items)
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
        Assert.True(fields.Count >= 15, $"only {fields.Count} fields enumerated");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheAfterLandingGroup_FollowsBeforeLanding_Named()
    {
        Assert.Equal(new[] { "BEFORE_LANDING", "AFTER_LANDING", "PARKING" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "BEFORE_LANDING").Take(3));
        Assert.Equal("After Landing", Group("AFTER_LANDING").Name);
    }

    /// <summary>[FO-22]: the arrival, never marked by an in-flight start.</summary>
    [Fact]
    public void TheAfterLandingGroup_IsArrival_AndNeverDeparture()
    {
        Assert.True(FoDepartureGroups.Arrival.Contains("AFTER_LANDING"));
        Assert.False(FoDepartureGroups.Departure.Contains("AFTER_LANDING"));
    }

    /// <summary>The group is the fifteen writes; the ground guard is flow-only.</summary>
    [Fact]
    public void AfterLanding_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("AFTER_LANDING").Items.Select(i => i.Id));
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == "AL_ON_GROUND");
    }

    [Fact]
    public void AfterLanding_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("AFTER_LANDING").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void AfterLanding_LineKinds()
    {
        foreach (var i in Group("AFTER_LANDING").Items)
        {
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each line reads the state its step delivers: a single switch its key, a multi switch its synthetic, the
    /// stabilizer trim its readout at zero, the speed brake forward, the flaps up, the APU turning.</summary>
    [Theory]
    [InlineData("AL_ANTISKID", "SWITCH_ANTISKID_ON", 0)]
    [InlineData("AL_AUTOBRAKE", "ROTARY_AUTO_BRAKE_MODE", 1)]
    [InlineData("AL_LANDING_LIGHTS", "FO_LANDING_LIGHTS_OFF", 1)]
    [InlineData("AL_STROBES", "SWITCH_STROBE_LIGHT", 0)]
    [InlineData("AL_CONT_IGN", "SWITCH_CONT_IGNITION", 0)]
    [InlineData("AL_AIR_DATA", "FO_AIR_DATA_HEAT_OFF", 1)]
    [InlineData("AL_WINDSHIELD", "FO_WINDSHIELD_IDLE", 1)]
    [InlineData("AL_SIDE_WINDOWS", "FO_SIDE_WINDOWS_OFF", 1)]
    [InlineData("AL_STAB_TRIM", "FO_STAB_TRIM_ZERO", 1)]
    [InlineData("AL_SPEED_BRAKE", "FO_SPEED_BRAKE_FORWARD", 1)]
    [InlineData("AL_FLAPS_UP", "FO_FLAPS_UP", 1)]
    [InlineData("AL_RADAR", "SWITCH_WX_OFF", 1)]
    [InlineData("AL_TRANSFERS_OFF", "FO_TRANSFERS_OFF", 1)]
    [InlineData("AL_APU_MASTER", "TOGGLE_APU_POWER", 1)]
    [InlineData("AL_APU_START", "FO_APU_STARTING_OR_RUNNING", 1)]
    public void AfterLanding_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
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

    /// <summary>An aircraft with work for the step: each key the step writes the other way from its target (the speed
    /// brake half out, the flap handle at 4), the stabilizer trim at the take-off setting with its readout following the
    /// typed entry, and for the APU start the master on with the APU at a standstill.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, key == SpeedBrake ? 50 : key == FlapHandle ? 4 : target == 0 ? 1 : 0);
        if (step.Id == "AL_STAB_TRIM")
        {
            ac.Set(ElevatorTrim, 2.38);
            ac.OnClock = a =>
            {
                if (a.Get(StabTrim) is double typed)
                    a.Set(ElevatorTrim, typed);
            };
        }
        if (step.EventName == X.ApuStart)
        {
            ac.Set(ApuMaster, 1);
            ac.Set(ApuRpm, 0);
        }
        return ac;
    }

    public static IEnumerable<object[]> AfterLandingLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "AFTER_LANDING").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft.</summary>
    [Theory]
    [MemberData(nameof(AfterLandingLinesWithACheckAction))]
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

    /// <summary>The APU start presses once with the master on, and is done once the APU turns.</summary>
    [Fact]
    public async Task The_APU_start_presses_once_and_is_done_once_the_APU_turns()
    {
        var ac = AircraftFor(Of("AL_APU_START"));
        var exec = new X();
        exec.SetTransport(ac);

        Assert.True(await exec.ExecuteStepAsync(Of("AL_APU_START")).WaitAsync(Hang));

        Assert.Equal(new[] { ("SWITCH_APU_START", 1.0) }, ac.Writes);
        Assert.True(ac.Get(ApuRpm) >= L1011FoStateEvaluator.ApuStartingRpm);
    }

    /// <summary>A finished run latches its own group, and nothing else.</summary>
    [Fact]
    public void AfterLanding_LatchesItsOwnGroup()
        => Assert.Equal(new[] { "AFTER_LANDING" }, AfterLanding.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The labels with a figure, and the stabilizer trim's zero test, read the same under a comma-decimal
    /// culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_and_the_trim_test_hold_under_a_comma_decimal_culture(string culture)
    {
        var (labels, zero) = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "AFTER_LANDING");
            var all = flow.Steps.Select(s => s.Label)
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
            return (all, flow.Steps.Single(s => s.Id == "AL_STAB_TRIM").SkipCondition!(State((ElevatorTrim, 0.15))));
        });
        Assert.Contains("1A and 3A transfers: off", labels);
        Assert.True(zero);
    }

    // ================================================================== what After Landing writes

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
        => AfterLanding.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(24, EveryWrittenKey().Count());
    }

    /// <summary>The one composite is the APU start; the levers are the speed brake and the flap handle, each written to
    /// 0; the one typed entry is the stabilizer trim, written to 0.</summary>
    [Fact]
    public void The_composite_the_levers_and_the_trim()
    {
        Assert.Equal(new[] { X.ApuStart }, EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct());
        Assert.Equal(new[] { ("AL_SPEED_BRAKE", SpeedBrake, (int?)0), ("AL_FLAPS_UP", FlapHandle, (int?)0) },
            EveryWrittenKey().Where(w => L1011Levers.IsLever(w.Key)));
        Assert.Equal(new[] { ("AL_STAB_TRIM", StabTrim, (int?)0) }, EveryWrittenKey().Where(w => w.Key == StabTrim));
    }

    private static IEnumerable<(string Step, string Key, int Target)> PlainSwitchWrites()
        => EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !L1011Levers.IsLever(w.Key) && w.Key != StabTrim)
            .Select(w => (w.Step, w.Key, w.Target!.Value));

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

    /// <summary>
    /// A toggle-carrying key is written only by a single-switch step whose "Already set" reads that key at the target:
    /// the executor reads it first, and from an unknown position the definition refuses the write. Planned from nothing
    /// read, every other key must still be a plan. Only the anti-skid carries a toggle here.
    /// </summary>
    [Fact]
    public void Only_the_anti_skid_carries_a_toggle_and_it_is_written_over_its_own_position_read()
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
        Assert.Equal(new[] { "SWITCH_ANTISKID_ON" }, toggles);
    }
}
