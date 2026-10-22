using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Item = MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor, MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's checklists: twelve groups, one per flow, with the flow's id and name, and no
/// read-back group. Each group lists its flow's lines in the flow's order: the writes, the unconditional
/// Captain steps and the read-only checks the flow completes (a conditional Captain prompt, the ground power
/// one, stays flow-only), and no two lines in a group read the same state. Every state line mirrors the
/// aircraft live (<c>RevertToState</c>, [FOB-1]); a hand tick of a line that switches the fuel or hydraulic
/// pumps off, or runs the standby power check (which turns the battery off), is left alone unless the aircraft
/// reads on the ground with every engine stopped ([FO-25]).
/// </summary>
public class L1011FoChecklistStructureTests
{
    private static readonly List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups = L1011FoChecklistDefinitions.Build();
    private static readonly List<FlowDefinition<L1011FoStateEvaluator>> Flows = L1011FoFlowDefinitions.Build();
    private static readonly FlowDefinition<L1011FoStateEvaluator> PowerUpFlow = Flows.Single(f => f.Id == "POWER_UP");

    private static ChecklistGroup<X, L1011FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);
    private static Item Line(string id) => Groups.SelectMany(g => g.Items).Single(i => i.Id == id);
    private static Step FlowStep(string id) => PowerUpFlow.Steps.Single(s => s.Id == id);

    private const string LeftAloneText = "Left alone: the aircraft must be on the ground with the engines stopped.";
    private const string LeftAloneApuText = "Left alone: the aircraft must be on the ground with the engines stopped and the APU shut down.";

    /// <summary>The fuel pump line also keeps its pumps while the APU runs or spools up, and says so; the other three guarded lines do not.</summary>
    private static string LeftAloneTextOf(string id) => id == "PU_FUEL_PUMPS" ? LeftAloneApuText : LeftAloneText;

    // ------------------------------------------------------------------ the groups

    /// <summary>One group per flow, in the flow's order, with its id: the MD-11's action groups, and no read-back.</summary>
    [Fact]
    public void GroupIds_AreTheTwelveFlows_InFlightOrder()
    {
        Assert.Equal(new[]
        {
            "POWER_UP", "PREFLIGHT", "BEFORE_START", "ENGINE_START", "AFTER_START", "BEFORE_TAKEOFF", "AFTER_TAKEOFF",
            "DESCENT", "BEFORE_LANDING", "AFTER_LANDING", "PARKING", "SHUTDOWN",
        }, Groups.Select(g => g.Id));
        Assert.Equal(Flows.Select(f => f.Id), Groups.Select(g => g.Id));
    }

    [Fact]
    public void EveryGroup_IsNamedAsItsFlow()
    {
        foreach (var f in Flows)
            Assert.Equal(f.Name, Group(f.Id).Name);
        Assert.Equal("Power Up", Group("POWER_UP").Name);
    }

    /// <summary>No read-back group, and no line of one: each read-back line was dropped, as a line of its flow's group
    /// read the same state, or moved into its flow's group under that flow's prefix.</summary>
    [Fact]
    public void There_is_no_read_back_group_or_line()
    {
        Assert.DoesNotContain(Groups, g => g.Id.EndsWith("_CL", StringComparison.Ordinal));
        var readBackPrefixes = new[]
        {
            "PUC_", "PFC_", "PFO_", "PFM_", "PFX_", "BSC_", "ESC_", "ASC_", "TXC_", "BTC_", "ATC_", "CLC_", "DSC_", "BLC_",
            "ALC_", "PKC_", "SDC_",
        };
        foreach (var i in Groups.SelectMany(g => g.Items))
            Assert.False(readBackPrefixes.Any(p => i.Id.StartsWith(p, StringComparison.Ordinal)), $"{i.Id} is a read-back line");
    }

    /// <summary>Each line carries its flow's prefix: PU_, PF_, BS_, ES_, AS_, BT_, AT_, DS_, BL_, AL_, PK_, SD_.</summary>
    [Fact]
    public void EveryLine_CarriesItsFlowsPrefix()
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["POWER_UP"] = "PU_", ["PREFLIGHT"] = "PF_", ["BEFORE_START"] = "BS_", ["ENGINE_START"] = "ES_",
            ["AFTER_START"] = "AS_", ["BEFORE_TAKEOFF"] = "BT_", ["AFTER_TAKEOFF"] = "AT_", ["DESCENT"] = "DS_",
            ["BEFORE_LANDING"] = "BL_", ["AFTER_LANDING"] = "AL_", ["PARKING"] = "PK_", ["SHUTDOWN"] = "SD_",
        };
        foreach (var g in Groups)
            Assert.All(g.Items, i => Assert.StartsWith(prefixes[g.Id], i.Id, StringComparison.Ordinal));
    }

    /// <summary>[FO-6]: a finished flow latches its own group and nothing else.</summary>
    [Fact]
    public void EveryFlow_NamesAndLatchesOnlyItsOwnGroup()
    {
        foreach (var f in Flows)
        {
            Assert.Equal(new[] { f.Id }, f.RelatedChecklistGroupIds);
            Assert.Equal(new[] { f.Id }, f.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));
        }
    }

    /// <summary>
    /// Never two lines in one group that read the same state: a line reading the same field (and the same further
    /// fields) as another line of its group, done at the same values, is one line too many. Compared over every
    /// position a switch or synthetic reads, and unread.
    /// </summary>
    [Fact]
    public void NoTwoLinesInAGroup_ReadTheSameState()
    {
        var probes = new[] { double.NaN, 0, 1, 2, 3, 4, 5, 10, 24.9, 50, 59.2, 100 };
        foreach (var g in Groups)
        {
            var state = g.Items.Where(i => i.StateFieldName != null).ToList();
            for (int a = 0; a < state.Count; a++)
                for (int b = a + 1; b < state.Count; b++)
                {
                    var (x, y) = (state[a], state[b]);
                    if (x.StateFieldName != y.StateFieldName || !x.AdditionalStateFields.SequenceEqual(y.AdditionalStateFields))
                        continue;
                    bool same = probes.All(v => x.StateCondition!(v) == y.StateCondition!(v));
                    Assert.False(same, $"{g.Id}: {x.Id} and {y.Id} read the same state ({x.StateFieldName})");
                }
        }
    }

    /// <summary>[FO-22]: an in-flight start marks the departure lists, through the climb; Descent onwards is the
    /// arrival. Every group sits in exactly one set.</summary>
    [Fact]
    public void EveryGroup_IsDepartureThroughClimb_AndArrivalFromDescent()
    {
        var ids = Groups.Select(g => g.Id).ToList();
        int descent = ids.IndexOf("DESCENT");
        Assert.True(descent > 0, "no Descent group");
        foreach (var id in ids.Take(descent))
        {
            Assert.True(FoDepartureGroups.Departure.Contains(id), $"{id} is not a departure group");
            Assert.False(FoDepartureGroups.Arrival.Contains(id), $"{id} is an arrival group too");
        }
        foreach (var id in ids.Skip(descent))
        {
            Assert.True(FoDepartureGroups.Arrival.Contains(id), $"{id} is not an arrival group");
            Assert.False(FoDepartureGroups.Departure.Contains(id), $"{id} is a departure group too");
        }
    }

    [Fact]
    public void ItemIds_AreUnique()
    {
        var ids = Groups.SelectMany(g => g.Items).Select(i => i.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void LabelsAreUniqueWithinEachGroup()
    {
        foreach (var g in Groups)
        {
            var labels = g.Items.Select(i => i.Label).ToList();
            Assert.True(labels.Count == labels.Distinct().Count(), $"{g.Id} repeats a label");
        }
    }

    [Fact]
    public void EveryItem_BelongsToItsGroup()
    {
        foreach (var g in Groups)
            Assert.All(g.Items, i => Assert.Equal(g.Id, i.GroupId));
    }

    /// <summary>[FOB-1]: every state line is a live mirror but the three ground start lines, which latch on the
    /// engine running (the one sanctioned StayComplete).</summary>
    [Fact]
    public void EveryAutoAndReadLine_UsesRevertToState()
    {
        var latched = new[] { "ES_E1_GND_START", "ES_E2_GND_START", "ES_E3_GND_START" };
        var auto = Groups.SelectMany(g => g.Items).Where(i => i.Type == ChecklistItemType.AutoDetectable).ToList();
        Assert.NotEmpty(auto);
        Assert.All(auto.Where(i => !latched.Contains(i.Id)), i => Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior));
        Assert.Equal(latched.Length, auto.Count(i => latched.Contains(i.Id)));
        Assert.All(auto, i => Assert.True(i.IsAutoDetectable, $"{i.Id} is AutoDetectable but cannot auto-detect"));
    }

    [Fact]
    public void EveryItemField_IsSyntheticOrRegistered()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        int n = 0;
        foreach (var i in Groups.SelectMany(g => g.Items).Where(i => i.StateFieldName != null))
            foreach (var field in i.AdditionalStateFields.Prepend(i.StateFieldName!))
            {
                n++;
                if (synthetic.Contains(field)) continue;
                Assert.True(vars.TryGetValue(field, out var def), $"{i.Id}: field {field} not registered");
                Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
                if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                    Assert.True(polled.Contains(field), $"{i.Id}: OnRequest field {field} is not polled");
            }
        Assert.True(n > 25, $"only {n} fields enumerated");
    }

    // ------------------------------------------------------------------ the flow and its groups

    [Fact]
    public void EveryFlowRelatedGroup_Exists()
    {
        var ids = Groups.Select(g => g.Id).ToHashSet();
        foreach (var f in Flows)
            foreach (var gid in f.RelatedChecklistGroupIds)
                Assert.Contains(gid, ids);
    }

    [Fact]
    public void EveryFlowTick_NamesAnItemOfOneOfTheFlowsOwnGroups()
    {
        foreach (var f in Flows)
        {
            var related = f.RelatedChecklistGroupIds.ToHashSet();
            foreach (var s in f.Steps.Where(s => s.CompletesChecklistItemId != null))
                Assert.True(Groups.Any(g => related.Contains(g.Id) && g.Items.Any(i => i.Id == s.CompletesChecklistItemId)),
                    $"{f.Id}.{s.Id} ticks {s.CompletesChecklistItemId}, not an item of the flow's own groups");
        }
    }

    /// <summary>[FO-7]: an action line with a CheckAction and no delivering step would be latched with its switch
    /// untouched. The one exception is the ground start lines: StayComplete state lines on the engine running, they are
    /// never linked to the ground start, and tick only from the engine running (L1011FoFlowChecklistLinkTests). A manual
    /// action line also carries StayComplete, as the model's default, and is never exempted.</summary>
    [Fact]
    public void EveryActionableItemInAnActionGroup_HasADeliveringFlowStep()
    {
        foreach (var f in Flows)
        {
            var delivered = f.Steps.Select(s => s.CompletesChecklistItemId).Where(id => id != null).ToHashSet();
            var group = Groups.SingleOrDefault(g => g.Id == f.Id);
            Assert.NotNull(group);
            foreach (var i in group!.Items.Where(i => i.CheckAction != null
                                                     && !(i.IsAutoDetectable && i.RevertBehavior == RevertBehavior.StayComplete)))
                Assert.True(delivered.Contains(i.Id), $"{group.Id}/{i.Id} has a CheckAction but no flow step delivers it");
        }
    }

    /// <summary>
    /// A flow and its group are one procedure in two forms, so they list the same lines in the same order (the
    /// MD-11's shape): every line a step links, its own first, then those it also delivers. Only flow-only steps
    /// fall outside: the guards, the waits that link no line and the Captain prompts that speak only when needed (no
    /// ground power).
    /// </summary>
    [Fact]
    public void EveryActionGroup_ListsTheSameLinesInTheSameOrderAsItsFlow()
    {
        foreach (var f in Flows)
        {
            var group = Groups.Single(g => g.Id == f.Id);
            var itemIds = group.Items.Select(i => i.Id).ToHashSet();
            var fromFlow = f.Steps.SelectMany(s => s.LinkedChecklistItemIds.DefaultIfEmpty(s.Id)).Where(itemIds.Contains).ToList();
            Assert.Equal(group.Items.Select(i => i.Id), fromFlow);
        }
    }

    /// <summary>The group's lines, in the flow's order: the five read-only checks first, the loop selectors after the
    /// fire test that leaves them at Both. The conditional ground power prompt stays flow-only.</summary>
    [Fact]
    public void PowerUp_Lines_InTheFlowsOrder()
    {
        Assert.Equal(new[]
        {
            "PU_CHK_FIRE_HANDLES", "PU_CHK_GEAR", "PU_CHK_SPEED_BRAKE", "PU_CHK_FLAPS", "PU_CHK_FUEL_IGN",
            "PU_CONT_IGN", "PU_RADIO_MASTERS", "PU_XPDR", "PU_RADAR", "PU_ATMS", "PU_AC_PUMPS", "PU_FUEL_PUMPS",
            "PU_JETTISON", "PU_PAX_OXY", "PU_GALLEY", "PU_STBY_CHECK", "PU_BATTERY", "PU_STBY_ARMED", "PU_AC_ESS",
            "PU_EXT_PWR", "PU_FIRE_TEST", "PU_LOOP_SELECTORS", "PU_INS_ALIGN", "PU_INS_POSITION",
        }, Group("POWER_UP").Items.Select(i => i.Id));
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == "PU_EXT_PWR_CPT");
    }

    /// <summary>Ticked or run, a line says the same thing: each action line's label is its flow step's. The loop
    /// selectors line has no step of its own: the fire test delivers it.</summary>
    [Fact]
    public void PowerUp_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("POWER_UP").Items.Where(i => i.Id != "PU_LOOP_SELECTORS"))
            Assert.Equal(FlowStep(i.Id).Label, i.Label);
        Assert.Contains("PU_LOOP_SELECTORS", FlowStep("PU_FIRE_TEST").LinkedChecklistItemIds);
    }

    [Fact]
    public void PowerUp_LineKinds()
    {
        var group = Group("POWER_UP");
        foreach (var i in group.Items)
        {
            switch (i.Id)
            {
                case "PU_STBY_CHECK":
                case "PU_FIRE_TEST":
                    // A test composite: a manual line that runs the test, judged by no state.
                    Assert.Equal(ChecklistItemType.Actionable, i.Type);
                    Assert.Null(i.StateFieldName);
                    Assert.NotNull(i.CheckAction);
                    Assert.True(i.ManualCompletionAllowed);
                    break;
                case "PU_INS_POSITION":
                    Assert.Equal(ChecklistItemType.CaptainReminder, i.Type);
                    Assert.Null(i.CheckAction);
                    Assert.Equal(FlowStep(i.Id).ReminderText, i.ReminderText);
                    Assert.Equal("Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit.", i.ReminderText);
                    break;
                case var _ when ReadOnlyLines.Any(r => r.Id == i.Id):
                    // What the First Officer finds and never moves: read only, ticked by the aircraft.
                    Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
                    Assert.Null(i.CheckAction);
                    Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
                    break;
                default:
                    Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
                    Assert.NotNull(i.CheckAction);
                    Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
                    break;
            }
        }
    }

    /// <summary>Each action line reads the same field its flow step's "Already set" reads.</summary>
    [Theory]
    [InlineData("PU_CONT_IGN", "SWITCH_CONT_IGNITION")]
    [InlineData("PU_RADIO_MASTERS", "FO_RADIO_MASTERS_ON")]
    [InlineData("PU_XPDR", "ROTARY_TCAS_MODE")]
    [InlineData("PU_RADAR", "SWITCH_WX_OFF")]
    [InlineData("PU_ATMS", "FO_ATMS_OFF")]
    [InlineData("PU_AC_PUMPS", "FO_AC_PUMPS_OFF")]
    [InlineData("PU_FUEL_PUMPS", "FO_FUEL_PUMPS_OFF")]
    [InlineData("PU_JETTISON", "FO_JETTISON_SAFE")]
    [InlineData("PU_PAX_OXY", "FO_PAX_OXY_SAFE")]
    [InlineData("PU_GALLEY", "FO_GALLEY_OFF")]
    [InlineData("PU_BATTERY", "TOGGLE_BATTERY")]
    [InlineData("PU_STBY_ARMED", "FO_STBY_ARMED")]
    [InlineData("PU_AC_ESS", "ROTARY_AC_ESS_BUS")]
    [InlineData("PU_EXT_PWR", "FO_EXT_POWER_ON")]
    [InlineData("PU_INS_ALIGN", "FO_INS_ALIGN_OR_NAV")]
    public void PowerUp_ActionLine_ReadsItsStepsField(string id, string field)
        => Assert.Equal(field, Line(id).StateFieldName);

    // ------------------------------------------------------------------ the inspection's lines Power Up keeps

    /// <summary>
    /// The cockpit safety inspection's lines the action lines did not already read, moved into Power Up with their
    /// labels: the five read-only checks under their steps' ids, and the loop selectors, which the fire test leaves at
    /// Both. Every other inspection line read the same state as an action line, and went.
    /// </summary>
    private static readonly (string Id, string Label, string Field, double Done)[] ReadOnlyLines =
    {
        ("PU_CHK_FIRE_HANDLES", "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", 1),
        ("PU_CHK_GEAR", "Gear lever: down", "FO_GEAR_LEVER_DOWN", 1),
        ("PU_CHK_SPEED_BRAKE", "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", 1),
        ("PU_CHK_FLAPS", "Flaps and flap handle: agree", "FO_FLAPS_AGREE", 1),
        ("PU_CHK_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", 1),
        ("PU_LOOP_SELECTORS", "Detection loop selectors: both", "FO_FIRE_LOOPS_BOTH", 1),
    };

    /// <summary>Each moved line keeps its label and reads its field at its value, action-free, a live mirror.</summary>
    [Fact]
    public void TheMovedInspectionLines_KeepTheirLabels_AndReadTheirFieldAtTheirValue()
    {
        foreach (var (id, label, field, done) in ReadOnlyLines)
        {
            var line = Line(id);
            Assert.Equal("POWER_UP", line.GroupId);
            Assert.Equal(label, line.Label);
            Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
            Assert.Equal(RevertBehavior.RevertToState, line.RevertBehavior);
            Assert.Equal(field, line.StateFieldName);
            Assert.Empty(line.AdditionalStateFields);
            Assert.Null(line.CheckAction);
            Assert.Null(line.LeaveAloneWhen);
            Assert.True(line.StateCondition!(done), $"{id}: {field} at {done} must read done");
            Assert.False(line.StateCondition!(double.NaN), $"{id}: {field} unread must not read done");
            Assert.False(line.StateCondition!(0), $"{id}: {field} at 0 must not read done");
        }
    }

    /// <summary>Each read-only check completes its own line, which reads the field it waits on.</summary>
    [Fact]
    public void EachCheck_CompletesItsOwnLine()
    {
        foreach (var (id, _, field, _) in ReadOnlyLines.Where(r => r.Id.StartsWith("PU_CHK_", StringComparison.Ordinal)))
        {
            Assert.Equal(new[] { id }, FlowStep(id).LinkedChecklistItemIds);
            Assert.Equal(field, FlowStep(id).ConditionFieldName);
        }
    }

    // ------------------------------------------------------------------ the guarded hand ticks ([FO-25])

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.ConnectedWith(values));
        return evaluator;
    }

    /// <summary>Every engine's N3 gauge, fuel-and-ignition switch and ground start switch (off): stopped (a cold gauge
    /// reads 1.5) or running.</summary>
    private static IEnumerable<(string, double)> Engines(bool running)
    {
        for (int n = 1; n <= 3; n++)
        {
            yield return ($"L1011_RO_N3_{n}", running ? 59 : 1.5);
            yield return ($"TOGGLE_CUTOFF_ENG_{n}", running ? 1 : 0);
            yield return ($"SWITCH_GND_START_ENG_{n}", 0);
        }
    }

    private static (string, double) Ground(bool onGround) => ("SIM_ON_GROUND", onGround ? 1 : 0);

    public static IEnumerable<object[]> GuardedLines() => new[]
    {
        new object[] { "PU_ATMS" }, new object[] { "PU_AC_PUMPS" }, new object[] { "PU_FUEL_PUMPS" },
        new object[] { "PU_STBY_CHECK" }, new object[] { "PU_FIRE_TEST" },
    };

    /// <summary>
    /// The four lines that cut hydraulic or fuel pumps or the battery carry the guard, and the fire test, which
    /// lights the fire warnings, as it did in Preflight; no other line does. The ATM switches are the air-turbine
    /// hydraulic pumps of systems B and C, left in AUTO through the flight: off in the air or with an engine turning
    /// cuts backup hydraulic power, and the fleet's cut sweep cannot see them by their label, so the line carries
    /// the guard itself.
    /// </summary>
    [Fact]
    public void OnlyThePumpStandbyCheckAndFireTestLines_AreLeftAlone()
    {
        // Power Up's own lines: Preflight's are pinned in L1011FoPreflightTests.
        var guarded = Group("POWER_UP").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id).ToArray();
        Assert.Equal(new[] { "PU_ATMS", "PU_AC_PUMPS", "PU_FUEL_PUMPS", "PU_STBY_CHECK", "PU_FIRE_TEST" }, guarded);
    }

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_SpeaksWhyItIsLeftAlone(string id)
        => Assert.Equal(LeftAloneTextOf(id), Line(id).LeaveAloneText);

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_IsLeftAloneAirborne(string id)
        => Assert.True(Line(id).LeaveAloneWhen!(State(Engines(running: false).Append(Ground(false)).ToArray())));

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_IsLeftAloneWithTheGroundFlagUnread(string id)
        => Assert.True(Line(id).LeaveAloneWhen!(State(Engines(running: false).ToArray())));

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_IsLeftAloneWithTheEnginesRunning(string id)
        => Assert.True(Line(id).LeaveAloneWhen!(State(Engines(running: true).Append(Ground(true)).ToArray())));

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_IsLeftAloneWithTheEnginesUnread(string id)
    {
        Assert.True(Line(id).LeaveAloneWhen!(State(Ground(true))), "nothing of the engines read");
        // One engine's N3 unread is enough to leave it alone.
        var oneUnread = Engines(running: false).Where(e => e.Item1 != "L1011_RO_N3_2").Append(Ground(true)).ToArray();
        Assert.True(Line(id).LeaveAloneWhen!(State(oneUnread)), "engine 2's N3 unread");
    }

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_IsLeftAloneWithNothingRead(string id)
        => Assert.True(Line(id).LeaveAloneWhen!(State()));

    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_GoesAheadParkedWithTheEnginesStopped(string id)
        => Assert.False(Line(id).LeaveAloneWhen!(State(Engines(running: false).Append(Ground(true)).ToArray())));

    /// <summary>Through the real checklist manager: the tick is refused at once, aloud, and the line stays unticked.</summary>
    [Theory]
    [MemberData(nameof(GuardedLines))]
    public void GuardedTick_ThroughTheChecklistManager_IsRefusedAloudAirborne(string id)
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var state = State(Engines(running: false).Append(Ground(false)).ToArray());
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(state, new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == id);

        Assert.False(mgr.ToggleItem(line.GroupId, id, out var spoken));
        Assert.Equal(LeftAloneTextOf(id), spoken);
        Assert.False(line.IsChecked);
        Assert.False(line.AwaitingActionConfirmation);
    }

    // ---- the APU keeps the fuel pumps ----------------------------------------------------------------------------

    private const string ApuRpm = "L1011_RO_APU_RPM";

    private static (string, double)[] ParkedWithTheApu(double? rpm)
    {
        var seeds = Engines(running: false).Append(Ground(true));
        return (rpm is double r ? seeds.Append((ApuRpm, r)) : seeds).ToArray();
    }

    /// <summary>
    /// The tank 2 left pump feeds the APU from the moment it starts turning, not only once it is up to speed:
    /// a pilot who started it by hand must not have that pump cut while it runs or is still spooling up.
    /// </summary>
    [Fact]
    public void The_fuel_pump_tick_refuses_with_the_APU_running_or_spooling_up()
    {
        var pumps = Line("PU_FUEL_PUMPS");
        Assert.True(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(100))), "APU at 100: pumps stay");
        Assert.True(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(95))), "APU at 95: pumps stay");
        Assert.True(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(94.9))), "APU still spooling up: pumps stay");
        Assert.True(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(3))), "APU just started turning: pumps stay");
        Assert.True(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(1))), "APU at 1: pumps stay");

        Assert.False(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(0.9))), "APU not turning: the tick goes ahead");
        Assert.False(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(0))), "APU stopped: the tick goes ahead");
        Assert.False(pumps.LeaveAloneWhen!(State(ParkedWithTheApu(null))), "APU unread: the tick goes ahead");

        // Not parked still refuses, with or without the APU.
        Assert.True(pumps.LeaveAloneWhen!(State(Engines(running: false).Append(Ground(false)).Append((ApuRpm, 0.0)).ToArray())));
    }

    [Fact]
    public void The_fuel_pump_tick_speaks_the_new_text_through_the_checklist_manager_with_the_APU_running()
    {
        foreach (double rpm in new[] { 100.0, 3.0 })
        {
            var groups = L1011FoChecklistDefinitions.Build();
            var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(State(ParkedWithTheApu(rpm)), new X(), groups);
            var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "PU_FUEL_PUMPS");

            Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken), $"APU at {rpm}");
            Assert.Equal(LeftAloneApuText, spoken);
            Assert.False(line.IsChecked);
            Assert.False(line.AwaitingActionConfirmation);
        }
    }

    /// <summary>The other three guarded lines stay on the parked-only rule: a running or spooling APU is not their business.</summary>
    [Theory]
    [InlineData("PU_ATMS")]
    [InlineData("PU_AC_PUMPS")]
    [InlineData("PU_STBY_CHECK")]
    [InlineData("PU_FIRE_TEST")]
    public void The_other_guarded_ticks_ignore_the_APU(string id)
    {
        Assert.False(Line(id).LeaveAloneWhen!(State(ParkedWithTheApu(100))));
        Assert.False(Line(id).LeaveAloneWhen!(State(ParkedWithTheApu(3))));
    }

    // ------------------------------------------------------------------ a hand tick does what the flow step does

    /// <summary>
    /// A cold aircraft for the fake: every key the flow writes reads the other way from its target, the
    /// battery on with standby power Off under its closed guard, and ground power available but not
    /// connected (its ties closed, as on a cold aircraft). For the battery's own line
    /// (<paramref name="line"/> PU_BATTERY) the battery is off, so that line has something to do.
    /// </summary>
    private static FakeL1011Aircraft ColdAircraft(string? line = null)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var step in PowerUpFlow.Steps)
        {
            var actions = step.ActionType switch
            {
                FlowStepActionType.SetSwitch => new[] { (step.EventName!, step.TargetValue!.Value) },
                FlowStepActionType.SetSwitchMultiple => step.MultiActions.Select(a => (a.EventName, a.TargetValue!.Value)).ToArray(),
                _ => Array.Empty<(string, int)>(),
            };
            foreach (var (key, target) in actions.Where(a => !X.IsPseudoKey(a.Item1)))
                ac.Set(key, target == 0 ? 1 : 0);
        }
        // The INS align composite reads each mode selector itself: all three off.
        foreach (int n in new[] { 1, 2, 3 })
            ac.Set($"ROTARY_INS_MODE_{n}", 0);
        ac.Set("TOGGLE_BATTERY", line == "PU_BATTERY" ? 0 : 1);
        ac.Set("TOGGLE_STBY_POWER", 0);
        ac.Set("GUARD_STBY_POWER", 0);
        ac.Set("LAMP_EXTERNAL_POWER_AVAILABLE", 1);
        ac.Set("TOGGLE_EXT_PWR", 0);
        ac.Set("L1011_RO_EXT_PWR_BREAKER", 0);
        foreach (int n in new[] { 1, 2, 3 })
            ac.Set($"L1011_RO_AC_TIE_{n}", 1);
        return ac;
    }

    /// <summary>Power Up's action lines: the cold aircraft above is Power Up's. Preflight's lines are pinned the
    /// same way in L1011FoPreflightTests, each over an aircraft set up for its own step.</summary>
    public static IEnumerable<object[]> ActionLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "POWER_UP").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>
    /// Each action line's tick runs the same write as its flow step: the same keys and values reach the
    /// aircraft, in the same order, from the same cold aircraft. The composites (standby power check,
    /// external power) included.
    /// </summary>
    [Theory]
    [MemberData(nameof(ActionLinesWithACheckAction))]
    public async Task HandTick_WritesExactlyWhatItsFlowStepWrites(string id)
    {
        var byStep = ColdAircraft(id);
        var stepExec = new X();
        stepExec.SetTransport(byStep);
        Assert.True(await stepExec.ExecuteStepAsync(FlowStep(id)).WaitAsync(TimeSpan.FromSeconds(10)), $"{id}: the flow step was not done");

        var byTick = ColdAircraft(id);
        var tickExec = new X();
        tickExec.SetTransport(byTick);
        await Line(id).CheckAction!(tickExec, new L1011FoStateEvaluator()).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotEmpty(byStep.Writes);
        Assert.Equal(byStep.Writes, byTick.Writes);
    }

    /// <summary>
    /// INS align goes unit by unit, in the step and in the tick: a unit already at ALIGN or NAV is never
    /// rewritten (a unit navigating stays navigating), and only the others are turned to ALIGN.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task INS_align_writes_only_the_units_not_already_at_align_or_nav(bool byTick)
    {
        var ac = ColdAircraft();
        ac.Set("ROTARY_INS_MODE_1", 1);   // standby
        ac.Set("ROTARY_INS_MODE_2", 2);   // align
        ac.Set("ROTARY_INS_MODE_3", 3);   // nav
        var exec = new X();
        exec.SetTransport(ac);

        if (byTick)
            await Line("PU_INS_ALIGN").CheckAction!(exec, new L1011FoStateEvaluator()).WaitAsync(TimeSpan.FromSeconds(10));
        else
            Assert.True(await exec.ExecuteStepAsync(FlowStep("PU_INS_ALIGN")).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(new[] { ("ROTARY_INS_MODE_1", 2.0) }, ac.Writes);
        Assert.Equal(2, ac.Get("ROTARY_INS_MODE_1"));
        Assert.Equal(2, ac.Get("ROTARY_INS_MODE_2"));
        Assert.Equal(3, ac.Get("ROTARY_INS_MODE_3"));
    }
}
