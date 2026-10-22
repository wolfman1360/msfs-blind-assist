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
/// The TriStar First Officer's Before Takeoff flow and its group (its eight writes and the line-up page's transponder
/// check), pinned by structure and by behaviour: the step ids and their order; every write's label, keys and targets word for
/// word, with its "Already set" on its own state; the transponder check, read only, completing only its own line; the
/// lines each step delivers ([FO-6], [FO-7]); the take-off autobrake, which [FO-14] leaves to the First Officer; the
/// pack 2 only step and tick, the one cut here, left alone off the ground ([FO-25]); and that every key is one the
/// definition takes, the anti-skid's toggle-carrying key only ever written over its own position read ([L1011-4]).
/// </summary>
public class L1011FoBeforeTakeoffTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> BeforeTakeoff => Flows.Single(f => f.Id == "BEFORE_TAKEOFF");

    private static Step Of(string id) => BeforeTakeoff.Steps.Single(s => s.Id == id);
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

    private const string Ground = "SIM_ON_GROUND";
    private const string PacksText = "Packs left on: not read on the ground.";

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
    public void BeforeTakeoff_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Before Takeoff", BeforeTakeoff.Name);
        Assert.Equal(new[] { "BEFORE_TAKEOFF" }, BeforeTakeoff.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(BeforeTakeoff.Description));
    }

    private static readonly string[] StepIds =
    {
        "BT_PACK_2", "BT_ANTISKID", "BT_AUTOBRAKE", "BT_STROBES", "BT_LANDING_LIGHTS", "BT_CONT_IGN", "BT_PROBE_HEAT",
        "BT_CHK_XPDR", "BT_RADAR",
    };

    [Fact]
    public void BeforeTakeoff_StepIdsAreInOrder()
        => Assert.Equal(StepIds, BeforeTakeoff.Steps.Select(s => s.Id));

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("BT_PACK_2", "Packs: pack 2 on, packs 1 and 3 off",
            new (string, int?)[] { ("SWITCH_PACK_FLOW_ENG_1", 0), ("SWITCH_PACK_FLOW_ENG_2", 1), ("SWITCH_PACK_FLOW_ENG_3", 0) }),
        ("BT_ANTISKID", "Anti-skid: on", All(1, "SWITCH_ANTISKID_ON")),
        ("BT_AUTOBRAKE", "Autobrake: takeoff", All(0, "ROTARY_AUTO_BRAKE_MODE")),
        ("BT_STROBES", "Strobe lights: on", All(1, "SWITCH_STROBE_LIGHT")),
        ("BT_LANDING_LIGHTS", "Landing lights: on", All(1, "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT")),
        ("BT_CONT_IGN", "Continuous ignition: on", All(1, "SWITCH_CONT_IGNITION")),
        ("BT_PROBE_HEAT", "Temperature probe heat: on", All(1, "SWITCH_PROBE_HEAT")),
        ("BT_RADAR", "Weather radar: on", All(1, "SWITCH_WX_WX")),
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
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            BeforeTakeoff.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    [Fact]
    public void Every_write_has_a_skip_condition()
        => Assert.All(BeforeTakeoff.Steps.Where(IsSwitch), s => Assert.True(s.SkipCondition != null, $"{s.Id} has no SkipCondition"));

    /// <summary>Every step's lines, from the table: a write its own line, the transponder check its own line, which
    /// reads the transponder.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["BT_PACK_2"] = new[] { "BT_PACK_2" },
        ["BT_ANTISKID"] = new[] { "BT_ANTISKID" },
        ["BT_AUTOBRAKE"] = new[] { "BT_AUTOBRAKE" },
        ["BT_STROBES"] = new[] { "BT_STROBES" },
        ["BT_LANDING_LIGHTS"] = new[] { "BT_LANDING_LIGHTS" },
        ["BT_CONT_IGN"] = new[] { "BT_CONT_IGN" },
        ["BT_PROBE_HEAT"] = new[] { "BT_PROBE_HEAT" },
        ["BT_CHK_XPDR"] = new[] { "BT_CHK_XPDR" },
        ["BT_RADAR"] = new[] { "BT_RADAR" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in BeforeTakeoff.Steps)
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
        var linked = BeforeTakeoff.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static double Wrong(double done) => done == 0 ? 1 : 0;

    [Fact]
    public void EveryWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in BeforeTakeoff.Steps.Where(IsSwitch))
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

    /// <summary>The autobrake is already at take-off only at position 0; OFF (1) and the landing settings are not.</summary>
    [Fact]
    public void The_autobrake_is_already_set_only_at_takeoff()
    {
        var skip = Of("BT_AUTOBRAKE").SkipCondition!;
        Assert.True(skip(State(("ROTARY_AUTO_BRAKE_MODE", 0))));
        foreach (double other in new[] { 1.0, 2, 3, 4 })
            Assert.False(skip(State(("ROTARY_AUTO_BRAKE_MODE", other))), $"autobrake at {other}");
    }

    // ------------------------------------------------------------------ the transponder check

    /// <summary>Before Start set TA/RA: here it is read only, a 5 s wait that skips aloud, "Already set" at TA/RA, and
    /// it completes only its own "Transponder: TA/RA" line, which reads the same field under the same rule.</summary>
    [Fact]
    public void The_transponder_check_is_read_only_and_completes_only_its_line()
    {
        Assert.Equal(new[] { "BT_CHK_XPDR" }, BeforeTakeoff.Steps.Where(s => s.ActionType == FlowStepActionType.WaitForCondition).Select(s => s.Id));
        var s = Of("BT_CHK_XPDR");
        Assert.Equal("Transponder: TA/RA", s.Label);
        Assert.Equal("ROTARY_TCAS_MODE", s.ConditionFieldName);
        Assert.Equal(5, s.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
        Assert.True(s.Condition!(5));
        Assert.False(s.Condition!(double.NaN), "unread must not pass");
        foreach (double other in new[] { 0.0, 1, 2, 3, 4 })
            Assert.False(s.Condition!(other), $"mode {other} is not TA/RA");
        Assert.True(s.SkipCondition!(State(("ROTARY_TCAS_MODE", 5))));
        Assert.False(s.SkipCondition!(State(("ROTARY_TCAS_MODE", 1))));
        Assert.False(s.SkipCondition!(State()), "unread must not be already set");

        Assert.Equal(new[] { "BT_CHK_XPDR" }, s.LinkedChecklistItemIds);
        var line = Line("BT_CHK_XPDR");
        Assert.Equal("BEFORE_TAKEOFF", line.GroupId);
        Assert.Equal("Transponder: TA/RA", line.Label);
        Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
        Assert.Equal(RevertBehavior.RevertToState, line.RevertBehavior);
        Assert.Null(line.CheckAction);
        Assert.Equal(s.ConditionFieldName, line.StateFieldName);
        foreach (double v in new[] { double.NaN, 0, 1, 2, 3, 4, 5 })
            Assert.Equal(s.Condition!(v), line.StateCondition!(v));
    }

    // ------------------------------------------------------------------ the one cut: packs 1 and 3 ([FO-25])

    /// <summary>
    /// Nothing in Before Takeoff cuts an engine, a pump, an INS or the battery: the fleet's cut sweep sees no such label
    /// in the flow or the action group. The one line it does see is the pack 2 only line, which switches packs 1 and 3
    /// off: a packs cut, so its step and its tick are left alone off the ground (<see cref="The_pack_2_step_and_tick_are_left_alone_off_the_ground"/>).
    /// </summary>
    [Fact]
    public void No_step_in_Before_Takeoff_cuts_anything_but_packs_1_and_3()
    {
        var labels = BeforeTakeoff.Steps.Select(s => (s.Id, s.Label))
            .Concat(Group("BEFORE_TAKEOFF").Items.Select(i => (i.Id, i.Label)));
        foreach (var (id, label) in labels)
            Assert.Equal(id == "BT_PACK_2" ? "Packs" : null, FoCutGuardSweepTests.CutKind(label));
    }

    /// <summary>Packs 1 and 3 off in the air would cut two of the three packs: the step and its tick are left alone
    /// airborne or with the ground flag unread, saying the packs stay on, and go ahead on the ground, engines running.</summary>
    [Fact]
    public void The_pack_2_step_and_tick_are_left_alone_off_the_ground()
    {
        var step = Of("BT_PACK_2");
        var tick = Line("BT_PACK_2");
        Assert.Equal(PacksText, step.LeaveAloneText);
        Assert.Equal(PacksText, tick.LeaveAloneText);
        var running = Enumerable.Range(1, 3).SelectMany(n => new (string, double)[]
            { ($"L1011_RO_N3_{n}", 59), ($"TOGGLE_CUTOFF_ENG_{n}", 1) }).ToArray();
        foreach (var leaves in new Func<L1011FoStateEvaluator, bool>[] { step.LeaveAloneWhen!, tick.LeaveAloneWhen! })
        {
            Assert.True(leaves(Connected(running.Append((Ground, 0.0)).ToArray())), "airborne");
            Assert.True(leaves(Connected(running)), "the ground flag unread");
            Assert.True(leaves(Connected()), "nothing read");
            Assert.False(leaves(Connected(running.Append((Ground, 1.0)).ToArray())), "on the ground, engines running");
        }
    }

    [Fact]
    public void Only_the_pack_2_step_and_tick_are_left_alone()
    {
        Assert.Equal(new[] { "BT_PACK_2" }, BeforeTakeoff.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));
        Assert.Equal(new[] { "BT_PACK_2" }, Group("BEFORE_TAKEOFF").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
    }

    [Fact]
    public void The_pack_2_tick_through_the_checklist_manager_is_refused_aloud_airborne()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected((Ground, 0)), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "BT_PACK_2");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(PacksText, spoken);
        Assert.False(line.IsChecked);
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = BeforeTakeoff.Steps.Select(s => s.ConditionFieldName).OfType<string>().Append(Ground)
            .Concat(Groups.Where(g => g.Id is "BEFORE_TAKEOFF").SelectMany(g => g.Items)
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
    public void TheBeforeTakeoffGroup_FollowsAfterStart_Named()
    {
        Assert.Equal(new[] { "AFTER_START", "BEFORE_TAKEOFF", "AFTER_TAKEOFF" },
            Groups.Select(g => g.Id).SkipWhile(id => id != "AFTER_START").Take(3));
        Assert.Equal("Before Takeoff", Group("BEFORE_TAKEOFF").Name);
    }

    [Fact]
    public void TheBeforeTakeoffGroup_IsDeparture_AndNeverArrival()
    {
        Assert.True(FoDepartureGroups.Departure.Contains("BEFORE_TAKEOFF"));
        Assert.False(FoDepartureGroups.Arrival.Contains("BEFORE_TAKEOFF"));
    }

    /// <summary>The group is the eight writes and the line-up page's transponder check, in the flow's order. Every other
    /// line of the line-up page read the same state as a write's line, and went.</summary>
    [Fact]
    public void BeforeTakeoff_Lines_InTheFlowsOrder()
        => Assert.Equal(StepIds, Group("BEFORE_TAKEOFF").Items.Select(i => i.Id));

    /// <summary>Ticked or run, a write's line says what its step says.</summary>
    [Fact]
    public void BeforeTakeoff_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("BEFORE_TAKEOFF").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void BeforeTakeoff_LineKinds()
    {
        foreach (var i in Group("BEFORE_TAKEOFF").Items.Where(i => i.Id != "BT_CHK_XPDR"))
        {
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each line reads the state its step delivers: a single switch its key, a multi switch its synthetic.</summary>
    [Theory]
    [InlineData("BT_PACK_2", "FO_PACK_2_ONLY", 1)]
    [InlineData("BT_ANTISKID", "SWITCH_ANTISKID_ON", 1)]
    [InlineData("BT_AUTOBRAKE", "ROTARY_AUTO_BRAKE_MODE", 0)]
    [InlineData("BT_STROBES", "SWITCH_STROBE_LIGHT", 1)]
    [InlineData("BT_LANDING_LIGHTS", "FO_LANDING_LIGHTS_ON", 1)]
    [InlineData("BT_CONT_IGN", "SWITCH_CONT_IGNITION", 1)]
    [InlineData("BT_PROBE_HEAT", "SWITCH_PROBE_HEAT", 1)]
    [InlineData("BT_RADAR", "SWITCH_WX_WX", 1)]
    public void BeforeTakeoff_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
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

    /// <summary>An aircraft set up so the step has work to do: each key the step writes the other way from its target.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step))
            ac.Set(key, target == 0 ? 1 : 0);
        return ac;
    }

    public static IEnumerable<object[]> BeforeTakeoffLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "BEFORE_TAKEOFF").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft.</summary>
    [Theory]
    [MemberData(nameof(BeforeTakeoffLinesWithACheckAction))]
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

    /// <summary>A finished run latches its own group, and nothing else.</summary>
    [Fact]
    public void BeforeTakeoff_LatchesItsOwnGroup()
        => Assert.Equal(new[] { "BEFORE_TAKEOFF" }, BeforeTakeoff.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The labels with a figure read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_hold_under_a_comma_decimal_culture(string culture)
    {
        var labels = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "BEFORE_TAKEOFF");
            return flow.Steps.Select(s => s.Label)
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
        });
        Assert.Contains("Packs: pack 2 on, packs 1 and 3 off", labels);
        Assert.Contains("Transponder: TA/RA", labels);
    }

    // ================================================================== what Before Takeoff writes

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
        => BeforeTakeoff.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target!.Value)));

    [Fact]
    public void Every_key_is_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !Definition.Value.FoAcceptsKey(w.Key)).Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(11, EveryWrittenKey().Count());
    }

    [Fact]
    public void No_composite_and_no_lever_is_written()
    {
        Assert.DoesNotContain(EveryWrittenKey(), w => X.IsPseudoKey(w.Key));
        Assert.DoesNotContain(EveryWrittenKey(), w => L1011Levers.IsLever(w.Key));
    }

    /// <summary>Every key is a plain switch row, and its target one of the control's positions.</summary>
    [Fact]
    public void EveryKey_IsAPlainSwitchRow_AtOneOfItsPositions()
    {
        foreach (var (step, key, target) in EveryWrittenKey())
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target, current: target == 0 ? 1 : 0);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused ({plan.Refusal})");
        }
    }

    /// <summary>
    /// The anti-skid switch's click replay carries a TOGGLE K: event, refused from an unknown position ([L1011-4]): it
    /// is written only by its single-switch step, whose "Already set" reads its own position, so the executor reads it
    /// first. Planned from nothing read, every other key must still be a plan.
    /// </summary>
    [Fact]
    public void Only_the_anti_skid_carries_a_toggle_and_it_is_written_over_its_own_position_read()
    {
        var toggles = new List<string>();
        foreach (var (step, key, target) in EveryWrittenKey())
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
