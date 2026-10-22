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
/// The TriStar First Officer's Descent flow, its action group and its read-back (the aircraft's descent page), pinned by
/// structure and by behaviour: the step ids and their order; the seat belt signs, written over their own position read
/// ([L1011-4]); the Captain's landing data, landing autobrake (the panel named, no setting: [FO-14]) and QNH; the cabin
/// altitude selector set to the plan's destination elevation, clamped to the selector's end stops, or with no plan a
/// silent skip and the Captain asked ([FO-20]); the lines each step delivers ([FO-6], [FO-7]); and that the groups are
/// arrival groups, never marked by an in-flight start ([FO-22]).
/// </summary>
public class L1011FoDescentTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> Descent => Flows.Single(f => f.Id == "DESCENT");

    private static Step Of(string id) => Descent.Steps.Single(s => s.Id == id);
    private static ChecklistGroup<X, L1011FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);
    private static Item Line(string id) => Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    private const string SeatBelts = "SWITCH_SEATBELT_LIGHT";
    private const string CabinAltReadout = "L1011_RO_CABIN_ALT_SELECTED";

    private const string AutobrakeReminder = "Set the landing autobrake on the overhead Antiskid and Autobrake panel";
    private const string CabinAltReminder =
        "Set the cabin altitude selector to the destination elevation on the engineer's pressurization panel.";

    /// <summary>An evaluator over <paramref name="values"/>, with SimBrief's destination at <paramref name="destFt"/>
    /// (none when null).</summary>
    private static L1011FoStateEvaluator State(int? destFt, params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        evaluator.SetPlannedPressurizationAltitudes(39000, destFt);
        return evaluator;
    }

    /// <summary>The selected cabin altitude readout at <paramref name="feet"/>: it arrives in metres.</summary>
    private static (string, double) CabinAlt(double feet) => (CabinAltReadout, feet * L1011FoStateEvaluator.MetresPerFoot);

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void Descent_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Descent", Descent.Name);
        Assert.Equal(new[] { "DESCENT", "DESCENT_CL" }, Descent.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(Descent.Description));
    }

    private static readonly string[] StepIds =
    {
        "DS_SEAT_BELTS", "DS_CPT_LANDING_DATA", "DS_CPT_AUTOBRAKE", "DS_CPT_QNH", "DS_CABIN_ALT", "DS_CABIN_ALT_CPT",
    };

    [Fact]
    public void Descent_StepIdsAreInOrder()
        => Assert.Equal(StepIds, Descent.Steps.Select(s => s.Id));

    /// <summary>Every step's lines: a write its action line then the read-back line it sets, an unconditional Captain
    /// step its reminder line, the conditional cabin altitude prompt nothing.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["DS_SEAT_BELTS"] = new[] { "DS_SEAT_BELTS", "DSC_SEAT_BELTS" },
        ["DS_CPT_LANDING_DATA"] = new[] { "DS_CPT_LANDING_DATA" },
        ["DS_CPT_AUTOBRAKE"] = new[] { "DS_CPT_AUTOBRAKE" },
        ["DS_CPT_QNH"] = new[] { "DS_CPT_QNH" },
        ["DS_CABIN_ALT"] = new[] { "DS_CABIN_ALT", "DSC_CABIN_ALT" },
        ["DS_CABIN_ALT_CPT"] = Array.Empty<string>(),
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in Descent.Steps)
        {
            Assert.True(Links.TryGetValue(s.Id, out var expected), $"{s.Id} is not in the table");
            Assert.Equal(expected, s.LinkedChecklistItemIds);
            Assert.Equal(expected!.FirstOrDefault(), s.CompletesChecklistItemId);
        }
        Assert.Equal(StepIds.Length, Links.Count);
    }

    // ------------------------------------------------------------------ the seat belt signs

    /// <summary>A toggle-carrying switch ([L1011-4]): one single-switch step, already set only at its own position on,
    /// never with it unread, so the executor reads it first.</summary>
    [Fact]
    public void The_seat_belt_signs_are_written_over_their_own_position_read()
    {
        var s = Of("DS_SEAT_BELTS");
        Assert.Equal("Seat belt signs: on", s.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
        Assert.Equal((SeatBelts, (int?)1), (s.EventName, s.TargetValue));
        Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
        Assert.True(s.SkipCondition!(State(null, (SeatBelts, 1))));
        Assert.False(s.SkipCondition!(State(null, (SeatBelts, 0))));
        Assert.False(s.SkipCondition!(State(null)), "unread must not be already set");

        var plan = L1011WritePlan.ForSet(Rows.Value[SeatBelts].Control!, 1, current: null);
        Assert.Equal(L1011WritePlan.UnknownPositionRefusal, plan.Refusal);
    }

    // ------------------------------------------------------------------ the Captain's

    private static readonly (string Id, string Text)[] CaptainSteps =
    {
        ("DS_CPT_LANDING_DATA", "Prepare the landing data on the tablet"),
        ("DS_CPT_AUTOBRAKE", AutobrakeReminder),
        ("DS_CPT_QNH", "Set the altimeters to QNH at the transition level: Ctrl+B"),
    };

    [Fact]
    public void The_three_captain_steps_are_unconditional_reminders()
    {
        foreach (var (id, text) in CaptainSteps)
        {
            var s = Of(id);
            Assert.Equal(FlowStepActionType.CaptainReminder, s.ActionType);
            Assert.Equal(text, s.Label);
            Assert.Equal(text, s.ReminderText);
            Assert.Null(s.SkipCondition);
        }
    }

    /// <summary>[FO-14]: the landing autobrake is the Captain's. Its reminder names the panel and suggests no setting,
    /// and no step or line of the flow writes the autobrake.</summary>
    [Fact]
    public void The_autobrake_reminder_names_the_panel_and_no_setting()
    {
        var text = Of("DS_CPT_AUTOBRAKE").ReminderText!;
        Assert.Contains("Antiskid and Autobrake", text);
        foreach (var setting in new[] { "min", "med", "max" })
            Assert.DoesNotContain(setting, text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Descent.Steps, s => s.EventName == "ROTARY_AUTO_BRAKE_MODE"
            || s.MultiActions.Any(a => a.EventName == "ROTARY_AUTO_BRAKE_MODE"));
        Assert.DoesNotContain(Group("DESCENT").Items.Concat(Group("DESCENT_CL").Items),
            i => i.StateFieldName == "ROTARY_AUTO_BRAKE_MODE");
    }

    // ------------------------------------------------------------------ the cabin altitude ([FO-20])

    /// <summary>
    /// The plan's destination elevation is the target (120 ft); once the readout reads it within 150 ft both the step
    /// and the Captain's prompt are already set. With no plan there is no value: nothing is sent or spoken by the step,
    /// and the Captain is asked whatever the selector reads.
    /// </summary>
    [Fact]
    public void The_destination_provider_is_the_plan_or_null()
    {
        var step = Of("DS_CABIN_ALT");
        var captain = Of("DS_CABIN_ALT_CPT");
        Assert.Equal("Cabin altitude selector: destination elevation", step.Label);
        Assert.Equal(FlowStepActionType.SetSwitch, step.ActionType);
        Assert.Equal(X.CabinAltitudeSelect, step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Equal(FlowStepFailurePolicy.Skip, step.FailurePolicy);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Cabin altitude selector", captain.Label);
        Assert.Equal(CabinAltReminder, captain.ReminderText);
        var ids = Descent.Steps.Select(s => s.Id).ToList();
        Assert.Equal(ids.IndexOf(step.Id) + 1, ids.IndexOf(captain.Id));

        // The plan: 120 ft.
        Assert.Equal(120, step.TargetValueProvider!(State(120)));
        var set = State(120, CabinAlt(120 + 140));
        Assert.True(step.SkipCondition!(set), "within 150 ft: already set");
        Assert.True(captain.SkipCondition!(set), "within 150 ft: nothing to ask");
        var cruise = State(120, CabinAlt(7150));
        Assert.False(step.SkipCondition!(cruise));
        Assert.False(captain.SkipCondition!(cruise), "still at the cruise setting: the Captain is asked");
        Assert.False(captain.SkipCondition!(State(120)), "the readout unread: the Captain is asked");

        // No plan: no value, and the Captain is asked even with the selector at 120.
        var noPlan = State(null, CabinAlt(120));
        Assert.Null(step.TargetValueProvider!(noPlan));
        Assert.False(step.SkipCondition!(noPlan));
        Assert.False(captain.SkipCondition!(noPlan));
    }

    /// <summary>The selector stops at -250 and 8,000 ft: a destination at 9,000 ft gives 8,000, and the step and the
    /// Captain's prompt are done once the readout shows 8,000; one below -250 gives -250.</summary>
    [Fact]
    public void A_destination_beyond_the_selector_is_set_at_its_end_stop()
    {
        var step = Of("DS_CABIN_ALT");
        var captain = Of("DS_CABIN_ALT_CPT");
        Assert.Equal(8000, step.TargetValueProvider!(State(9000)));
        Assert.True(step.SkipCondition!(State(9000, CabinAlt(8000))));
        Assert.True(captain.SkipCondition!(State(9000, CabinAlt(8000))));
        Assert.Equal(-250, step.TargetValueProvider!(State(-500)));
        Assert.True(step.SkipCondition!(State(-500, CabinAlt(-250))));
    }

    /// <summary>Through the executor on the fake TriStar: the 9,000 ft destination's 8,000 turns the knob to its end
    /// (step 100) and reads back done.</summary>
    [Fact]
    public async Task A_destination_at_9000_ft_is_set_and_read_back_at_8000()
    {
        var step = Of("DS_CABIN_ALT");
        step.TargetValue = step.TargetValueProvider!(State(9000));
        var ac = new FakeL1011Aircraft();
        ac.Set(CabinAltReadout, 7150 * L1011FoStateEvaluator.MetresPerFoot);
        var exec = new X();
        exec.SetTransport(ac);

        Assert.True(await exec.ExecuteStepAsync(step).WaitAsync(Hang));
        Assert.Contains(ac.Io, e => e.Kind == FakeL1011IoKind.CabinAltitudeStep && e.Value == 100);
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = Groups.Where(g => g.Id is "DESCENT" or "DESCENT_CL").SelectMany(g => g.Items)
            .Select(i => i.StateFieldName).OfType<string>().Append(CabinAltReadout).Distinct().ToList();
        foreach (var field in fields)
        {
            if (synthetic.Contains(field)) continue;
            Assert.True(vars.TryGetValue(field, out var def), $"{field} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
            if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.True(polled.Contains(field), $"OnRequest field {field} is not polled");
        }
        Assert.True(fields.Count >= 3, $"only {fields.Count} fields enumerated");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheDescentGroups_FollowClimb_Named()
    {
        Assert.Equal(new[] { "DESCENT", "DESCENT_CL" }, Groups.Select(g => g.Id).SkipWhile(id => id != "DESCENT").Take(2));
        Assert.Equal("CLIMB_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "DESCENT").Last());
        Assert.Equal("Descent", Group("DESCENT").Name);
        Assert.Equal("Descent", Group("DESCENT_CL").Name);
    }

    /// <summary>[FO-22]: Descent onwards is the arrival, never marked by an in-flight start.</summary>
    [Fact]
    public void EveryDescentGroup_IsArrival_AndNeverDeparture()
    {
        foreach (var id in Descent.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Arrival.Contains(id), $"{id} is not an arrival group");
            Assert.False(FoDepartureGroups.Departure.Contains(id), $"{id} is a departure group too");
        }
    }

    [Fact]
    public void The_read_back_has_no_check_action()
        => Assert.All(Group("DESCENT_CL").Items, i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
        { "DS_SEAT_BELTS", "DS_CPT_LANDING_DATA", "DS_CPT_AUTOBRAKE", "DS_CPT_QNH", "DS_CABIN_ALT" };

    /// <summary>The conditional cabin altitude prompt, spoken only while the selector does not read the destination, is
    /// flow-only.</summary>
    [Fact]
    public void Descent_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("DESCENT").Items.Select(i => i.Id));
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == "DS_CABIN_ALT_CPT");
    }

    [Fact]
    public void Descent_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("DESCENT").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void Descent_LineKinds()
    {
        foreach (var i in Group("DESCENT").Items)
        {
            if (i.Id.StartsWith("DS_CPT_", StringComparison.Ordinal))
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
        Assert.Equal("SWITCH_SEATBELT_LIGHT", Line("DS_SEAT_BELTS").StateFieldName);
        Assert.Equal("FO_CABIN_ALT_AT_DEST", Line("DS_CABIN_ALT").StateFieldName);
        Assert.True(Line("DS_SEAT_BELTS").StateCondition!(1));
        Assert.False(Line("DS_SEAT_BELTS").StateCondition!(0));
        Assert.False(Line("DS_SEAT_BELTS").StateCondition!(double.NaN));
        Assert.True(Line("DS_CABIN_ALT").StateCondition!(1));
        Assert.False(Line("DS_CABIN_ALT").StateCondition!(0));
        Assert.False(Line("DS_CABIN_ALT").StateCondition!(double.NaN));
    }

    // ---- a hand tick does what the flow step does ----------------------------------------------------------------

    /// <summary>An aircraft with work for the step: the seat belt signs off, the cabin altitude at the cruise setting.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(SeatBelts, 0);
        ac.Set(CabinAltReadout, 7150 * L1011FoStateEvaluator.MetresPerFoot);
        return ac;
    }

    [Theory]
    [InlineData("DS_SEAT_BELTS")]
    [InlineData("DS_CABIN_ALT")]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var ready = State(120);
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

    /// <summary>With no plan, the cabin altitude tick writes nothing, as its step sends nothing.</summary>
    [Fact]
    public async Task The_cabin_altitude_tick_with_no_plan_writes_nothing()
    {
        var ac = AircraftFor(Of("DS_CABIN_ALT"));
        var exec = new X();
        exec.SetTransport(ac);
        await Line("DS_CABIN_ALT").CheckAction!(exec, State(null)).WaitAsync(Hang);
        Assert.DoesNotContain(ac.Io, e => e.Kind != FakeL1011IoKind.FreshRead);
    }

    // ---- the read-back, word for word ------------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value, or a Captain's reminder.</summary>
    private sealed record ReadBack(string Id, string Label, string? Field = null, double Done = 1, bool Reminder = false);

    private static readonly ReadBack[] DescentReadBack =
    {
        new("DSC_LANDING_DATA", "Landing data: prepared", Reminder: true),
        new("DSC_SEAT_BELTS", "30 mins before landing: seatbelt signs on", SeatBelts),
        new("DSC_QNH", "At transition level, altimeters: set QNH", Reminder: true),
        new("DSC_CABIN_ALT", "At transition level, flt cab alt sel: set destination elevation", "FO_CABIN_ALT_AT_DEST"),
    };

    [Fact]
    public void The_read_back_is_word_for_word()
        => Assert.Equal(DescentReadBack.Select(l => (l.Id, l.Label)), Group("DESCENT_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in DescentReadBack)
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
            var others = L1011FoStateEvaluator.SyntheticKeys.Contains(rb.Field!) ? new[] { 0.0 } : new[] { 0.0, 2, 3 };
            foreach (double other in others)
                Assert.False(line.StateCondition!(other), $"{rb.Id}: {rb.Field} at {other} must not read done");
        }
    }

    /// <summary>A finished run latches the action group and the read-back, and nothing else.</summary>
    [Fact]
    public void Descent_LatchesItsTwoGroups()
        => Assert.Equal(Descent.RelatedChecklistGroupIds, Descent.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The read-back's 30 minutes and the provider's figure read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_and_the_destination_target_hold_under_a_comma_decimal_culture(string culture)
    {
        var (labels, target) = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "DESCENT");
            var all = flow.Steps.Select(s => s.Label)
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
            return (all, flow.Steps.Single(s => s.Id == "DS_CABIN_ALT").TargetValueProvider!(State(9000)));
        });
        Assert.Contains("30 mins before landing: seatbelt signs on", labels);
        Assert.Equal(8000, target);
    }

    // ================================================================== what Descent writes

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

    /// <summary>Two writes: the seat belt switch, which the definition takes, and the cabin altitude composite.</summary>
    [Fact]
    public void The_writes_are_the_seat_belts_and_the_cabin_altitude_composite()
    {
        var writes = Descent.Steps.Where(s => s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple)
            .Select(s => (s.Id, s.EventName)).ToList();
        Assert.Equal(new[] { ("DS_SEAT_BELTS", (string?)SeatBelts), ("DS_CABIN_ALT", (string?)X.CabinAltitudeSelect) }, writes);
        Assert.True(Definition.Value.FoAcceptsKey(SeatBelts));
        Assert.True(X.IsPseudoKey(X.CabinAltitudeSelect));
    }
}
