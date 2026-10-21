using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Item = MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor, MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's Before Start flow, its action group and its read-back (the aircraft's "Before engine
/// start" page up to its first ground start), pinned by structure and by behaviour: the step ids and their order; the
/// ground guard and the two Stop waits that open it, so external power goes off only once the APU generator is on
/// line; every write's label, keys and targets word for word, with its "Already set" on its own state; the doors
/// prompt while the door light is lit or unread; each INS unit turned to NAV only once it is aligned or already at
/// NAV, its Captain line otherwise ([FO-20]); the navigating check; the cabin barometric setting copied from the
/// captain's altimeter, with its Captain fallback; the fleet's Before Start tail; the lines each step delivers
/// ([FO-6], [FO-7]); the pack 2 tick left alone off the ground ([FO-25]); and that every key is one the definition
/// takes, a toggle-carrying key only ever written over its own position read ([L1011-4]).
/// </summary>
public class L1011FoBeforeStartTests
{
    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> BeforeStart => Flows.Single(f => f.Id == "BEFORE_START");

    private static Step Of(string id) => BeforeStart.Steps.Single(s => s.Id == id);
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

    private const string ExtPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";
    private const string CabinBaroReadout = "L1011_RO_CABIN_BARO";
    private const string CaptainAltimeter = "L1011_RO_ALTIMETER_1";
    private const string DoorLamp = "LAMP_LX_MIP_DOOR_OPEN";

    private static string InsMode(int unit) => $"ROTARY_INS_MODE_{unit}";
    private static string InsStatus(int unit) => $"INS_{unit}_ALIGNMENT_STATUS";

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
    public void BeforeStart_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Before Start", BeforeStart.Name);
        Assert.Equal(new[] { "BEFORE_START", "BEFORE_START_CL" }, BeforeStart.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(BeforeStart.Description));
    }

    private static readonly string[] StepIds =
    {
        "BS_ON_GROUND", "BS_APU_RUNNING", "BS_APU_GEN_ON_LINE", "BS_EXT_OFF", "BS_SEAT_BELTS", "BS_ANTICOL",
        "BS_DOORS_CPT", "BS_CHK_DOORS", "BS_GALLEY", "BS_CHK_FUEL_PUMP", "BS_APU_MODE_MIN", "BS_PACK_2",
        "BS_ISLN_XBLEEDS", "BS_APU_BLEED", "BS_CABIN_BARO", "BS_CABIN_BARO_CPT", "BS_CHK_IGNITION",
        "BS_INS_1_NAV", "BS_INS_1_CPT", "BS_INS_2_NAV", "BS_INS_2_CPT", "BS_INS_3_NAV", "BS_INS_3_CPT",
        "BS_CHK_INS_NAV", "BS_XPDR", "BS_ACARS", "BS_CLEARANCE",
    };

    [Fact]
    public void BeforeStart_StepIdsAreInOrder()
        => Assert.Equal(StepIds, BeforeStart.Steps.Select(s => s.Id));

    // ------------------------------------------------------------------ the opening: the guard and the two APU waits

    [Fact]
    public void TheGroundGuardOpensTheFlow_AReadOnlyStopThatLinksNothing()
    {
        var guard = BeforeStart.Steps[0];
        Assert.Equal("BS_ON_GROUND", guard.Id);
        Assert.Equal("On the ground", guard.Label);
        Assert.Equal("SIM_ON_GROUND", guard.ConditionFieldName);
        Assert.Equal(FlowStepActionType.WaitForCondition, guard.ActionType);
        Assert.Equal(FlowStepFailurePolicy.Stop, guard.FailurePolicy);
        Assert.Equal(5, guard.TimeoutSeconds);
        Assert.Null(guard.SkipCondition);
        Assert.Empty(guard.LinkedChecklistItemIds);
        Assert.False(guard.Condition!(double.NaN), "an unread ground flag must fail the guard");
        Assert.False(guard.Condition!(0));
        Assert.True(guard.Condition!(1));
    }

    [Theory]
    [InlineData("BS_APU_RUNNING", "APU running", "FO_APU_RUNNING", 10)]
    [InlineData("BS_APU_GEN_ON_LINE", "APU generator on line", "FO_APU_GEN_ON_LINE", 60)]
    public void TheApuWaits_AreReadOnlyStops_UnreadFailing(string id, string label, string field, int seconds)
    {
        var wait = Of(id);
        Assert.Equal(label, wait.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal(field, wait.ConditionFieldName);
        Assert.Equal(seconds, wait.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, wait.FailurePolicy);
        Assert.Null(wait.SkipCondition);
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.False(wait.Condition!(double.NaN), $"{id}: unread must fail the wait");
        Assert.False(wait.Condition!(0));
        Assert.True(wait.Condition!(1));
    }

    /// <summary>
    /// The power wait comes right before the external power cut, as the fleet's engine-master waits do
    /// (FoEngineAndGroundGuardTests): there is no interlock, and external power pulled before the APU generator is on
    /// line would leave the buses on the battery. Both APU waits come before it.
    /// </summary>
    [Fact]
    public void ExternalPower_GoesOffOnlyRightAfterTheApuGeneratorIsReadOnLine()
    {
        var steps = BeforeStart.Steps;
        int running = steps.FindIndex(s => s.Id == "BS_APU_RUNNING");
        int wait = steps.FindIndex(s => s.Id == "BS_APU_GEN_ON_LINE");
        int cut = steps.FindIndex(s => s.Id == "BS_EXT_OFF");
        Assert.True(running >= 0 && running < wait, $"BS_APU_RUNNING at {running}, BS_APU_GEN_ON_LINE at {wait}");
        Assert.True(wait >= 0 && cut == wait + 1, $"BS_APU_GEN_ON_LINE at {wait}, BS_EXT_OFF at {cut}");
        Assert.Equal("FO_APU_GEN_ON_LINE", steps[wait].ConditionFieldName);
        Assert.Equal(FlowStepFailurePolicy.Stop, steps[wait].FailurePolicy);
        Assert.False(steps[wait].Condition!(double.NaN));
        Assert.Equal(-1, steps.Take(wait).ToList().FindIndex(IsSwitch));
    }

    // ------------------------------------------------------------------ the writes

    private static readonly string[] IsolationAndCrossbleeds =
        { "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3" };

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("BS_EXT_OFF", "External power: off", All(0, X.ExternalPower)),
        ("BS_SEAT_BELTS", "Seat belt signs: on", All(1, "SWITCH_SEATBELT_LIGHT")),
        ("BS_ANTICOL", "Anti-collision lights: on", All(1, "SWITCH_ANTICOL_LIGHT")),
        ("BS_GALLEY", "Galley power: off", All(0, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3")),
        ("BS_APU_MODE_MIN", "APU compressor mode: minimum", All(0, "SWITCH_APU_MODE")),
        ("BS_PACK_2", "Packs: pack 2 on, packs 1 and 3 off",
            new (string, int?)[] { ("SWITCH_PACK_FLOW_ENG_1", 0), ("SWITCH_PACK_FLOW_ENG_2", 1), ("SWITCH_PACK_FLOW_ENG_3", 0) }),
        ("BS_ISLN_XBLEEDS", "Engine isolation and crossbleed valves: on", All(1, IsolationAndCrossbleeds)),
        ("BS_APU_BLEED", "APU bleed: on", All(1, "SWITCH_APU_BLEED")),
        // The providers' steps: each target is resolved when the flow runs.
        ("BS_CABIN_BARO", "Cabin barometric setting: captain's altimeter", new (string, int?)[] { (X.CabinBaro, null) }),
        ("BS_INS_1_NAV", "INS 1: nav", new (string, int?)[] { ("ROTARY_INS_MODE_1", null) }),
        ("BS_INS_2_NAV", "INS 2: nav", new (string, int?)[] { ("ROTARY_INS_MODE_2", null) }),
        ("BS_INS_3_NAV", "INS 3: nav", new (string, int?)[] { ("ROTARY_INS_MODE_3", null) }),
        ("BS_XPDR", "Transponder: TA/RA", All(5, "ROTARY_TCAS_MODE")),
    };

    private static readonly string[] Providers = { "BS_CABIN_BARO", "BS_INS_1_NAV", "BS_INS_2_NAV", "BS_INS_3_NAV" };

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
            Assert.Equal(Providers.Contains(id), s.TargetValueProvider != null);
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            BeforeStart.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Every_write_has_a_skip_condition()
        => Assert.All(BeforeStart.Steps.Where(IsSwitch), s => Assert.True(s.SkipCondition != null, $"{s.Id} has no SkipCondition"));

    /// <summary>Every step's lines, from the table: a write its action line then the read-back lines it sets, a check
    /// the read-back line it reads, an unconditional Captain step its reminder line; the guard, the waits and the
    /// conditional Captain prompts link nothing.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["BS_ON_GROUND"] = Array.Empty<string>(),
        ["BS_APU_RUNNING"] = Array.Empty<string>(),
        ["BS_APU_GEN_ON_LINE"] = Array.Empty<string>(),
        ["BS_EXT_OFF"] = new[] { "BS_EXT_OFF" },
        ["BS_SEAT_BELTS"] = new[] { "BS_SEAT_BELTS", "BSC_SEAT_BELTS" },
        ["BS_ANTICOL"] = new[] { "BS_ANTICOL", "BSC_ANTICOL" },
        ["BS_DOORS_CPT"] = Array.Empty<string>(),
        ["BS_CHK_DOORS"] = new[] { "BSC_DOORS" },
        ["BS_GALLEY"] = new[] { "BS_GALLEY", "BSC_GALLEY" },
        ["BS_CHK_FUEL_PUMP"] = new[] { "BSC_FUEL_PUMP" },
        ["BS_APU_MODE_MIN"] = new[] { "BS_APU_MODE_MIN", "BSC_APU_MODE" },
        ["BS_PACK_2"] = new[] { "BS_PACK_2", "BSC_PACK_2" },
        ["BS_ISLN_XBLEEDS"] = new[] { "BS_ISLN_XBLEEDS", "BSC_ISLN_VALVES" },
        ["BS_APU_BLEED"] = new[] { "BS_APU_BLEED" },
        ["BS_CABIN_BARO"] = new[] { "BS_CABIN_BARO" },
        ["BS_CABIN_BARO_CPT"] = Array.Empty<string>(),
        ["BS_CHK_IGNITION"] = new[] { "BSC_IGNITION" },
        ["BS_INS_1_NAV"] = new[] { "BS_INS_1_NAV" },
        ["BS_INS_1_CPT"] = Array.Empty<string>(),
        ["BS_INS_2_NAV"] = new[] { "BS_INS_2_NAV" },
        ["BS_INS_2_CPT"] = Array.Empty<string>(),
        ["BS_INS_3_NAV"] = new[] { "BS_INS_3_NAV" },
        ["BS_INS_3_CPT"] = Array.Empty<string>(),
        ["BS_CHK_INS_NAV"] = new[] { "BSC_INS_NAV" },
        ["BS_XPDR"] = new[] { "BS_XPDR", "BSC_XPDR" },
        ["BS_ACARS"] = new[] { "BS_ACARS" },
        ["BS_CLEARANCE"] = new[] { "BS_CLEARANCE" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in BeforeStart.Steps)
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
        var linked = BeforeStart.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    /// <summary>What the aircraft reads when a write's work is done: the external power breaker open; the cabin baro
    /// readout at the captain's altimeter, off standard; an INS selector at NAV; otherwise the step's keys at their
    /// targets.</summary>
    private static (string Key, double Value)[] Done(Step s)
    {
        if (s.EventName == X.ExternalPower) return new[] { (ExtPowerBreaker, 0.0) };
        if (s.EventName == X.CabinBaro) return new[] { (CabinBaroReadout, 1002.4), (CaptainAltimeter, 1002.4) };
        if (s.TargetValueProvider != null) return new[] { (s.EventName!, (double)L1011FoStateEvaluator.InsNav) };
        return Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
    }

    private static double Wrong(double done) => done == 0 ? 1 : 0;

    [Fact]
    public void EveryWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in BeforeStart.Steps.Where(IsSwitch))
        {
            n++;
            var done = Done(s);
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, Wrong(wrong[i].Value));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length, n);
    }

    // ------------------------------------------------------------------ the read-only checks

    private static readonly (string Id, string Label, string Field, int Seconds, string Line)[] Checks =
    {
        ("BS_CHK_DOORS", "Door lights: out", "FO_DOORS_CLOSED", 5, "BSC_DOORS"),
        ("BS_CHK_FUEL_PUMP", "Fuel pump: at least one on", "FO_ANY_FUEL_PUMP_ON", 5, "BSC_FUEL_PUMP"),
        ("BS_CHK_IGNITION", "Ignition selector: system A or B", "FO_IGNITION_SELECTED", 5, "BSC_IGNITION"),
        ("BS_CHK_INS_NAV", "INS 1, 2 and 3: navigating", "FO_INS_NAVIGATING", 15, "BSC_INS_NAV"),
    };

    [Fact]
    public void TheChecks_AreReadOnlyWaitsThatSkipAloud()
    {
        Assert.Equal(Checks.Select(c => c.Id), BeforeStart.Steps.Where(s => s.Id.Contains("_CHK_", StringComparison.Ordinal)).Select(s => s.Id));
        foreach (var (id, label, field, seconds, _) in Checks)
        {
            var s = Of(id);
            Assert.Equal(label, s.Label);
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal(field, s.ConditionFieldName);
            Assert.Equal(seconds, s.TimeoutSeconds);
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.NotNull(s.SkipCondition);
            Assert.False(s.Condition!(double.NaN), $"{id}: an unread state must not pass");
            Assert.False(s.Condition!(0), $"{id}: a state reading 0 must not pass");
            Assert.True(s.Condition!(1), $"{id}: a state reading 1 must pass");
            Assert.False(s.SkipCondition!(State()), $"{id}: unread must not be already set");
        }
    }

    /// <summary>A read-only check completes only the read-back line that reads its own field, under the same rule.</summary>
    [Fact]
    public void Every_check_completes_only_the_line_reading_its_field()
    {
        var readBack = Group("BEFORE_START_CL").Items.ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var (id, _, field, _, line) in Checks)
        {
            var s = Of(id);
            Assert.Equal(new[] { line }, s.LinkedChecklistItemIds);
            Assert.True(readBack.TryGetValue(line, out var item), $"{id}: {line} is not a read-back line");
            Assert.Equal(field, item!.StateFieldName);
            foreach (double v in new[] { double.NaN, 0, 1 })
                Assert.Equal(s.Condition!(v), item.StateCondition!(v));
        }
    }

    /// <summary>A unit set to NAV reads aligned for about a second before it reads navigating: the check waits 15 s,
    /// after the three units are turned, and ticks only the read-back's nav line.</summary>
    [Fact]
    public void The_navigating_check_waits_fifteen_seconds_and_completes_the_nav_line()
    {
        var s = Of("BS_CHK_INS_NAV");
        Assert.Equal(15, s.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
        Assert.Equal(new[] { "BSC_INS_NAV" }, s.LinkedChecklistItemIds);
        var ids = BeforeStart.Steps.Select(x => x.Id).ToList();
        Assert.Equal(ids.IndexOf("BS_INS_3_CPT") + 1, ids.IndexOf("BS_CHK_INS_NAV"));

        var statuses = new[] { 1, 2, 3 }.Select(n => (InsStatus(n), 1.0)).ToArray();
        Assert.True(s.SkipCondition!(State(statuses)), "all three navigating: already set");
        var oneAligned = statuses.Select(x => x.Item1 == InsStatus(2) ? (x.Item1, 2.0) : x).ToArray();
        Assert.False(s.SkipCondition!(State(oneAligned)), "unit 2 still reads aligned");
        Assert.False(s.SkipCondition!(State(statuses.Take(2).ToArray())), "unit 3 unread");
    }

    // ------------------------------------------------------------------ the doors

    /// <summary>The doors are the tablet's: the Captain is asked while the door light reads lit or unread, never
    /// while it reads out; the check that follows ticks the read-back's door line.</summary>
    [Fact]
    public void The_doors_prompt_speaks_while_the_door_light_is_lit_or_unread()
    {
        var prompt = Of("BS_DOORS_CPT");
        Assert.Equal(FlowStepActionType.CaptainReminder, prompt.ActionType);
        Assert.Equal("Doors", prompt.Label);
        Assert.Equal("Close the doors on the tablet.", prompt.ReminderText);
        Assert.Empty(prompt.LinkedChecklistItemIds);

        Assert.False(prompt.SkipCondition!(State((DoorLamp, 1))), "the door light lit: the Captain is asked");
        Assert.False(prompt.SkipCondition!(State()), "the door light unread: the Captain is asked");
        Assert.True(prompt.SkipCondition!(State((DoorLamp, 0))), "the door light out: nothing to ask");

        var ids = BeforeStart.Steps.Select(x => x.Id).ToList();
        Assert.Equal(ids.IndexOf("BS_DOORS_CPT") + 1, ids.IndexOf("BS_CHK_DOORS"));
    }

    // ------------------------------------------------------------------ INS to NAV, unit by unit ([FO-20])

    /// <summary>
    /// A unit reading aligned (status 2), or whose selector already reads NAV, gets NAV (3); a unit still aligning,
    /// or at OFF or STBY, gets no value, so nothing is sent or spoken and its Captain line asks for it. The Captain
    /// line is already set once the selector reads NAV.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Each_INS_unit_gets_nav_only_when_aligned_or_already_nav(int unit)
    {
        var step = Of($"BS_INS_{unit}_NAV");
        var captain = Of($"BS_INS_{unit}_CPT");
        Assert.Equal(InsMode(unit), step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal($"INS {unit} still aligning", captain.Label);
        Assert.Equal($"INS {unit} still aligning: set it to NAV once READY NAV lights.", captain.ReminderText);
        Assert.Empty(captain.LinkedChecklistItemIds);
        var ids = BeforeStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(step.Id) + 1, ids.IndexOf(captain.Id));

        // Aligned at ALIGN: NAV is the target; the Captain is not needed once the selector reads NAV.
        var aligned = State((InsStatus(unit), 2), (InsMode(unit), 2));
        Assert.Equal(3, step.TargetValueProvider!(aligned));
        Assert.False(step.SkipCondition!(aligned));
        var atNav = State((InsStatus(unit), 2), (InsMode(unit), 3));
        Assert.Equal(3, step.TargetValueProvider!(atNav));
        Assert.True(step.SkipCondition!(atNav));
        Assert.True(captain.SkipCondition!(atNav));

        // Still aligning at ALIGN: no target, and the Captain is asked.
        var aligning = State((InsStatus(unit), 90), (InsMode(unit), 2));
        Assert.Null(step.TargetValueProvider!(aligning));
        Assert.False(captain.SkipCondition!(aligning));

        // At OFF, never aligned or unread: no target.
        Assert.Null(step.TargetValueProvider!(State((InsStatus(unit), 90), (InsMode(unit), 0))));
        Assert.Null(step.TargetValueProvider!(State((InsMode(unit), 0))));
        Assert.False(captain.SkipCondition!(State()), "unread: the Captain is asked");

        // Aligned, but the pilot left the selector at OFF or STBY, or it is unread: never switched on by the First
        // Officer; the Captain is asked.
        foreach (var offAlignAndNav in new[] { State((InsStatus(unit), 2), (InsMode(unit), 0)),
                     State((InsStatus(unit), 2), (InsMode(unit), 1)), State((InsStatus(unit), 2)) })
        {
            Assert.Null(step.TargetValueProvider!(offAlignAndNav));
            Assert.False(captain.SkipCondition!(offAlignAndNav));
        }

        // The other units' readings never steer this one.
        var others = new[] { 1, 2, 3 }.Where(n => n != unit)
            .SelectMany(n => new[] { (InsStatus(n), 2.0), (InsMode(n), 3.0) }).ToArray();
        Assert.Null(step.TargetValueProvider!(State(others.Append((InsStatus(unit), 90.0)).Append((InsMode(unit), 2.0)).ToArray())));
    }

    // ------------------------------------------------------------------ the cabin barometric setting ([FO-20])

    [Fact]
    public void Cabin_baro_provider_and_captain_fallback()
    {
        var step = Of("BS_CABIN_BARO");
        var captain = Of("BS_CABIN_BARO_CPT");
        Assert.Equal(X.CabinBaro, step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Cabin barometric setting", captain.Label);
        Assert.Equal("Set the cabin barometric setting to the QNH on the engineer's pressurization panel.", captain.ReminderText);
        Assert.Empty(captain.LinkedChecklistItemIds);
        var ids = BeforeStart.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(step.Id) + 1, ids.IndexOf(captain.Id));

        // The captain's altimeter at 1002.4, the cabin baro elsewhere: the step writes 1002.40 hPa.
        var elsewhere = State((CaptainAltimeter, 1002.4), (CabinBaroReadout, 1013));
        Assert.Equal(100240, step.TargetValueProvider!(elsewhere));
        Assert.False(step.SkipCondition!(elsewhere));
        Assert.False(captain.SkipCondition!(elsewhere));

        // Standard: no QNH to copy, nothing sent or spoken, and the Captain is asked.
        var standard = State((CaptainAltimeter, 1013.25), (CabinBaroReadout, 1013.25));
        Assert.Null(step.TargetValueProvider!(standard));
        Assert.False(step.SkipCondition!(standard));
        Assert.False(captain.SkipCondition!(standard));

        // The cabin baro already at the captain's altimeter: both already set.
        var set = State((CaptainAltimeter, 1002.4), (CabinBaroReadout, 1002.4));
        Assert.True(step.SkipCondition!(set));
        Assert.True(captain.SkipCondition!(set));

        // Unread: no value, and the Captain is asked.
        Assert.Null(step.TargetValueProvider!(State()));
        Assert.False(captain.SkipCondition!(State()));
    }

    /// <summary>The spoken labels with a unit number, and the provider's hectopascals, read the same under a
    /// comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_and_the_cabin_baro_target_hold_under_a_comma_decimal_culture(string culture)
    {
        var (labels, target) = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "BEFORE_START");
            var lines = L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items);
            var all = flow.Steps.Select(s => s.Label).Concat(flow.Steps.Select(s => s.ReminderText ?? ""))
                .Concat(lines.Select(i => i.Label)).ToList();
            int? t = flow.Steps.Single(s => s.Id == "BS_CABIN_BARO").TargetValueProvider!(State((CaptainAltimeter, 1002.4)));
            return (all, t);
        });
        Assert.Contains("INS 2: nav", labels);
        Assert.Contains("INS 3 still aligning: set it to NAV once READY NAV lights.", labels);
        Assert.Equal(100240, target);
    }

    // ------------------------------------------------------------------ the fleet's tail

    [Fact]
    public void The_flow_and_the_action_group_end_with_Start_ACARS_then_the_clearance()
    {
        var steps = BeforeStart.Steps;
        Assert.Equal(("BS_ACARS", "Start ACARS"), (steps[^2].Id, steps[^2].Label));
        Assert.Equal(("BS_CLEARANCE", "Obtain pushback and start clearance"), (steps[^1].Id, steps[^1].Label));
        foreach (var s in steps.TakeLast(2))
        {
            Assert.Equal(FlowStepActionType.CaptainReminder, s.ActionType);
            Assert.Equal(s.Label, s.ReminderText);
            Assert.Null(s.SkipCondition);
        }

        var items = Group("BEFORE_START").Items;
        Assert.Equal(("BS_ACARS", "Start ACARS"), (items[^2].Id, items[^2].Label));
        Assert.Equal(("BS_CLEARANCE", "Obtain pushback and start clearance"), (items[^1].Id, items[^1].Label));
        Assert.All(items.TakeLast(2), i => Assert.Equal(ChecklistItemType.CaptainReminder, i.Type));
    }

    // ================================================================== the checklists

    [Fact]
    public void TheBeforeStartGroups_FollowPreflight_Named()
    {
        Assert.Equal(new[] { "BEFORE_START", "BEFORE_START_CL" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "BEFORE_START").Take(2));
        Assert.Equal("PREFLIGHT_CONSOLE_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "BEFORE_START").Last());
        Assert.Equal("Before Start", Group("BEFORE_START").Name);
        Assert.Equal("Before Engine Start", Group("BEFORE_START_CL").Name);
    }

    [Fact]
    public void EveryBeforeStartGroup_IsDeparture_AndNeverArrival()
    {
        foreach (var id in BeforeStart.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Departure.Contains(id), $"{id} is not a departure group");
            Assert.False(FoDepartureGroups.Arrival.Contains(id), $"{id} is an arrival group too");
        }
    }

    [Fact]
    public void The_read_back_has_no_check_action()
        => Assert.All(Group("BEFORE_START_CL").Items, i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
    {
        "BS_EXT_OFF", "BS_SEAT_BELTS", "BS_ANTICOL", "BS_GALLEY", "BS_APU_MODE_MIN", "BS_PACK_2", "BS_ISLN_XBLEEDS",
        "BS_APU_BLEED", "BS_CABIN_BARO", "BS_INS_1_NAV", "BS_INS_2_NAV", "BS_INS_3_NAV", "BS_XPDR", "BS_ACARS", "BS_CLEARANCE",
    };

    /// <summary>The conditional Captain prompts (doors, cabin baro, the three INS units), spoken only when the aircraft
    /// does not show the state, are flow-only.</summary>
    private static readonly string[] FlowOnlyPrompts =
        { "BS_DOORS_CPT", "BS_CABIN_BARO_CPT", "BS_INS_1_CPT", "BS_INS_2_CPT", "BS_INS_3_CPT" };

    [Fact]
    public void BeforeStart_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("BEFORE_START").Items.Select(i => i.Id));
        foreach (var id in FlowOnlyPrompts)
        {
            Assert.NotNull(Of(id).SkipCondition);
            Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == id);
        }
    }

    [Fact]
    public void BeforeStart_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("BEFORE_START").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void BeforeStart_LineKinds()
    {
        foreach (var i in Group("BEFORE_START").Items)
        {
            if (i.Id is "BS_ACARS" or "BS_CLEARANCE")
            {
                Assert.Equal(ChecklistItemType.CaptainReminder, i.Type);
                Assert.Null(i.CheckAction);
                Assert.Equal(Of(i.Id).ReminderText, i.ReminderText);
                continue;
            }
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each Auto line reads the state its step delivers: a single switch its key, a multi switch its
    /// synthetic, a composite or a provider the state it sets.</summary>
    [Theory]
    [InlineData("BS_EXT_OFF", "FO_EXT_POWER_OFF", 1)]
    [InlineData("BS_SEAT_BELTS", "SWITCH_SEATBELT_LIGHT", 1)]
    [InlineData("BS_ANTICOL", "SWITCH_ANTICOL_LIGHT", 1)]
    [InlineData("BS_GALLEY", "FO_GALLEY_OFF", 1)]
    [InlineData("BS_APU_MODE_MIN", "SWITCH_APU_MODE", 0)]
    [InlineData("BS_PACK_2", "FO_PACK_2_ONLY", 1)]
    [InlineData("BS_ISLN_XBLEEDS", "FO_START_AIR_VALVES_ON", 1)]
    [InlineData("BS_APU_BLEED", "SWITCH_APU_BLEED", 1)]
    [InlineData("BS_CABIN_BARO", "FO_CABIN_BARO_SET", 1)]
    [InlineData("BS_INS_1_NAV", "ROTARY_INS_MODE_1", 3)]
    [InlineData("BS_INS_2_NAV", "ROTARY_INS_MODE_2", 3)]
    [InlineData("BS_INS_3_NAV", "ROTARY_INS_MODE_3", 3)]
    [InlineData("BS_XPDR", "ROTARY_TCAS_MODE", 5)]
    public void BeforeStart_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
    {
        var line = Line(id);
        Assert.Equal(field, line.StateFieldName);
        Assert.True(line.StateCondition!(done), $"{id}: {field} at {done} must read done");
        Assert.False(line.StateCondition!(double.NaN), $"{id}: {field} unread must not read done");
        var others = L1011FoStateEvaluator.SyntheticKeys.Contains(field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
        foreach (double other in others.Where(v => v != done))
            Assert.False(line.StateCondition!(other), $"{id}: {field} at {other} must not read done");
    }

    /// <summary>The external power line reads the breaker open, as its step's "Already set" does.</summary>
    [Fact]
    public void The_external_power_line_and_step_agree_on_the_breaker()
    {
        var step = Of("BS_EXT_OFF");
        foreach (double breaker in new[] { 0.0, 1.0 })
        {
            var s = State((ExtPowerBreaker, breaker));
            Assert.Equal(step.SkipCondition!(s), Line("BS_EXT_OFF").StateCondition!(s.GetValue("FO_EXT_POWER_OFF")));
        }
        Assert.False(step.SkipCondition!(State()), "unread: not already set");
    }

    // ---- the pack 2 tick ([FO-25]) --------------------------------------------------------------------------------

    private const string PacksText = "Packs left on: not read on the ground.";

    private static (string, double)[] Engines(bool running)
        => Enumerable.Range(1, 3).SelectMany(n => new (string, double)[]
            {
                ($"L1011_RO_N3_{n}", running ? 59 : 1.5), ($"TOGGLE_CUTOFF_ENG_{n}", running ? 1 : 0),
            }).ToArray();

    private static (string, double) Ground(bool onGround) => ("SIM_ON_GROUND", onGround ? 1 : 0);

    /// <summary>The two Before Start ticks left alone: external power (until the APU generator is on line) and pack 2
    /// only (off the ground). The flow leaves no step alone: its waits and its ground guard cover both.</summary>
    [Fact]
    public void Only_the_external_power_and_pack_2_ticks_are_left_alone()
    {
        Assert.Equal(new[] { "BS_EXT_OFF", "BS_PACK_2" },
            Group("BEFORE_START").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
        Assert.DoesNotContain(BeforeStart.Steps, s => s.LeaveAloneWhen != null);
    }

    /// <summary>Packs 1 and 3 off in the air cut two of the three packs: the tick is left alone off the ground, as
    /// Preflight's packs tick is.</summary>
    [Fact]
    public void The_pack_2_tick_is_left_alone_off_the_ground()
    {
        var line = Line("BS_PACK_2");
        Assert.Equal(PacksText, line.LeaveAloneText);
        var when = line.LeaveAloneWhen!;
        Assert.True(when(Connected(Engines(false).Append(Ground(false)).ToArray())), "airborne");
        Assert.True(when(Connected(Engines(false))), "the ground flag unread");
        Assert.True(when(Connected()), "nothing read");
        Assert.False(when(Connected(Engines(false).Append(Ground(true)).ToArray())), "parked");
        Assert.False(when(Connected(Engines(true).Append(Ground(true)).ToArray())), "on the ground, engines running");
    }

    [Fact]
    public void The_pack_2_tick_through_the_checklist_manager_is_refused_aloud_airborne()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected(Engines(false).Append(Ground(false)).ToArray()), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "BS_PACK_2");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(PacksText, spoken);
        Assert.False(line.IsChecked);
    }

    // ---- the external power tick --------------------------------------------------------------------------------

    private const string ExtPowerText = "External power left on: the APU generator is not on line.";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";

    /// <summary>A tick that drops external power with no other source on line leaves the aircraft on the battery: the
    /// tick waits for the APU generator, as the flow does, and an unread generator refuses too.</summary>
    [Fact]
    public void The_external_power_tick_is_left_alone_until_the_APU_generator_is_on_line()
    {
        var line = Line("BS_EXT_OFF");
        Assert.Equal(ExtPowerText, line.LeaveAloneText);
        var when = line.LeaveAloneWhen!;
        Assert.False(when(Connected((ApuGenBreaker, 1), (ExtPowerBreaker, 1))), "the APU generator on line: goes ahead");
        Assert.True(when(Connected((ApuGenBreaker, 0), (ExtPowerBreaker, 1))), "the APU generator off line");
        Assert.True(when(Connected((ExtPowerBreaker, 1))), "the APU generator unread");
        Assert.True(when(Connected()), "nothing read");
    }

    /// <summary>Through the real checklist manager: refused at once, aloud, with the generator off line or unread;
    /// with it on line the tick goes ahead and runs its write.</summary>
    [Theory]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    public void The_external_power_tick_through_the_checklist_manager_is_refused_aloud_without_the_APU_generator(double apuGen)
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var seeds = new List<(string, double)> { (ExtPowerBreaker, 1) };
        if (!double.IsNaN(apuGen)) seeds.Add((ApuGenBreaker, apuGen));
        var exec = new X();
        exec.SetTransport(new FakeL1011Aircraft());
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected(seeds.ToArray()), exec, groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "BS_EXT_OFF");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(ExtPowerText, spoken);
        Assert.False(line.IsChecked);
        Assert.False(line.AwaitingActionConfirmation);
    }

    [Fact]
    public async Task The_external_power_tick_through_the_checklist_manager_goes_ahead_with_the_APU_generator_on_line()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var ac = new FakeL1011Aircraft();
        ac.Set("TOGGLE_EXT_PWR", 1);
        ac.Set(ExtPowerBreaker, 1);
        var exec = new X();
        exec.SetTransport(ac);
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected((ApuGenBreaker, 1), (ExtPowerBreaker, 1)), exec, groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "BS_EXT_OFF");

        Assert.True(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Null(spoken);
        Assert.True(line.IsChecked);
        // The tick's write runs in the background: wait, bounded, for it to reach the aircraft.
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!ac.Writes.Contains(("TOGGLE_EXT_PWR", 0.0)) && DateTime.UtcNow < deadline)
            await Task.Delay(20);
        Assert.Contains(("TOGGLE_EXT_PWR", 0.0), ac.Writes);
    }

    // ---- a hand tick does what the flow step does ----------------------------------------------------------------

    /// <summary>The evaluator the providers read: all three units aligned at ALIGN and the captain's altimeter at 1002.4.</summary>
    private static L1011FoStateEvaluator Ready()
        => State(new[] { 1, 2, 3 }.SelectMany(n => new[] { (InsStatus(n), 2.0), (InsMode(n), 2.0) })
            .Append((CaptainAltimeter, 1002.4)).ToArray());

    /// <summary>
    /// An aircraft set up so the step has work to do: each key the step writes the other way from its target (an INS
    /// selector at ALIGN), external power on line for its cut, and the cabin baro elsewhere.
    /// </summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, target is int t ? Wrong(t) : 2);
        if (step.EventName == X.ExternalPower)
        {
            ac.Set("TOGGLE_EXT_PWR", 1);
            ac.Set(ExtPowerBreaker, 1);
        }
        if (step.EventName == X.CabinBaro)
            ac.Set(CabinBaroReadout, 1013);
        return ac;
    }

    public static IEnumerable<object[]> BeforeStartLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_START").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft; a
    /// provider's step with the target the flow resolves from the same readings.</summary>
    [Theory]
    [MemberData(nameof(BeforeStartLinesWithACheckAction))]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var ready = Ready();
        var step = L1011FoFlowDefinitions.Build().Single(f => f.Id == "BEFORE_START").Steps.Single(s => s.Id == id);
        if (step.TargetValueProvider is { } provider)
            step.TargetValue = provider(ready);

        var byStep = AircraftFor(step);
        var stepExec = new X();
        stepExec.SetTransport(byStep);
        Assert.True(await stepExec.ExecuteStepAsync(step).WaitAsync(TimeSpan.FromSeconds(10)), $"{id}: the flow step was not done");

        var byTick = AircraftFor(step);
        var tickExec = new X();
        tickExec.SetTransport(byTick);
        await Line(id).CheckAction!(tickExec, ready).WaitAsync(TimeSpan.FromSeconds(10));

        static List<(FakeL1011IoKind, string, double?)> Sent(FakeL1011Aircraft ac) =>
            ac.Io.Where(e => e.Kind != FakeL1011IoKind.FreshRead).Select(e => (e.Kind, e.Key, e.Value)).ToList();
        Assert.NotEmpty(Sent(byStep));
        Assert.Equal(Sent(byStep), Sent(byTick));
    }

    /// <summary>With no value (a unit still aligning, a standard altimeter), a tick writes nothing, as its step sends nothing.</summary>
    [Theory]
    [InlineData("BS_CABIN_BARO")]
    [InlineData("BS_INS_1_NAV")]
    [InlineData("BS_INS_2_NAV")]
    [InlineData("BS_INS_3_NAV")]
    public async Task A_provider_tick_with_no_value_writes_nothing(string id)
    {
        var none = State(new[] { 1, 2, 3 }.Select(n => (InsStatus(n), 90.0)).Append((CaptainAltimeter, 1013.25)).ToArray());
        var ac = AircraftFor(Of(id));
        var exec = new X();
        exec.SetTransport(ac);
        await Line(id).CheckAction!(exec, none).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(ac.Io, e => e.Kind != FakeL1011IoKind.FreshRead);
    }

    // ---- the read-back, word for word ------------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value, or a Captain's reminder.</summary>
    private sealed record ReadBack(string Id, string Label, string? Field = null, double Done = 1, bool Reminder = false);

    private static readonly ReadBack[] BeforeEngineStart =
    {
        new("BSC_INS_NAV", "All INS: NAV", "FO_INS_NAVIGATING"),
        new("BSC_XPDR", "Transponder: TA/RA", "ROTARY_TCAS_MODE", 5),
        new("BSC_SEAT_BELTS", "Seatbelt sign: on", "SWITCH_SEATBELT_LIGHT"),
        new("BSC_ANTICOL", "Anti coll light: on", "SWITCH_ANTICOL_LIGHT"),
        new("BSC_IGNITION", "Start ignition: A or B", "FO_IGNITION_SELECTED"),
        new("BSC_DOORS", "Door lights: out", "FO_DOORS_CLOSED"),
        new("BSC_GALLEY", "Galley power: off", "FO_GALLEY_OFF"),
        new("BSC_FUEL_PUMP", "Fuel pump: minimum one on", "FO_ANY_FUEL_PUMP_ON"),
        new("BSC_APU_MODE", "APU: min mode", "SWITCH_APU_MODE", 0),
        new("BSC_PACK_2", "Packs: pack 2 on", "FO_PACK_2_ONLY"),
        new("BSC_ISLN_VALVES", "All engine isolation valves: latched", "FO_START_AIR_VALVES_ON"),
        new("BSC_ACARS", "Start ACARS", Reminder: true),
    };

    [Fact]
    public void The_read_back_is_word_for_word_and_ends_with_Start_ACARS()
    {
        Assert.Equal(BeforeEngineStart.Select(l => (l.Id, l.Label)), Group("BEFORE_START_CL").Items.Select(i => (i.Id, i.Label)));
        Assert.Equal("BSC_ACARS", Group("BEFORE_START_CL").Items[^1].Id);
    }

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in BeforeEngineStart)
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

    /// <summary>A finished run latches the action group and the read-back, and nothing else.</summary>
    [Fact]
    public void BeforeStart_LatchesItsTwoGroups()
        => Assert.Equal(BeforeStart.RelatedChecklistGroupIds, BeforeStart.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    // ================================================================== what Before Start writes

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

    /// <summary>Every key written, with its target; an INS selector's provider target is NAV, the only value it gives.</summary>
    private static IEnumerable<(string Step, string Key, int? Target)> EveryWrittenKey()
        => BeforeStart.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a =>
            (s.Id, a.Key, a.Target ?? (a.Key.StartsWith("ROTARY_INS_MODE_", StringComparison.Ordinal) ? L1011FoStateEvaluator.InsNav : null))));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(21, EveryWrittenKey().Count());
    }

    [Fact]
    public void TheCompositesArePseudoKeys_AndNoLeverIsWritten()
    {
        Assert.Equal(new[] { X.CabinBaro, X.ExternalPower }.OrderBy(k => k, StringComparer.Ordinal),
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
            var plan = L1011WritePlan.ForSet(row.Control!, target!.Value, current: target!.Value == 0 ? 1 : 0);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused ({plan.Refusal})");
        }
    }

    /// <summary>The keys whose click replay carries a TOGGLE K: event, refused from an unknown position ([L1011-4]).</summary>
    private static readonly string[] ToggleCarrying = { "SWITCH_APU_BLEED", "SWITCH_SEATBELT_LIGHT" };

    /// <summary>
    /// A toggle-carrying key is written only by a single-switch step whose "Already set" reads that key at the target:
    /// the executor reads it first, and from an unknown position the definition refuses the write, which the flow
    /// speaks as "Skipping". Planned from nothing read, every other key must still be a plan.
    /// </summary>
    [Fact]
    public void No_step_writes_a_toggle_carrying_key_from_an_unread_position_except_through_a_skip()
    {
        var toggles = new List<string>();
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key)))
        {
            var plan = L1011WritePlan.ForSet(Rows.Value[key].Control!, target!.Value, current: null);
            Assert.NotEqual(L1011WritePlan.NotAPositionRefusal, plan.Refusal);
            if (plan.Refusal is null) continue;
            Assert.Equal(L1011WritePlan.UnknownPositionRefusal, plan.Refusal);
            toggles.Add(key);

            var s = Of(step);
            Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
            int other = target.Value == 0 ? 1 : 0;
            Assert.True(s.SkipCondition!(State((key, target.Value))), $"{step}: {key} at its target must be already set");
            Assert.False(s.SkipCondition!(State((key, other))), $"{step}: {key} the other way must not be already set");
            Assert.False(s.SkipCondition!(State()), $"{step}: {key} unread must not be already set");
        }
        Assert.Equal(ToggleCarrying.OrderBy(k => k, StringComparer.Ordinal), toggles.OrderBy(k => k, StringComparer.Ordinal));
    }
}
