using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's flows (part 1: Power Up), pinned by structure and by behaviour: the step
/// ids and their order, the two guards that must come first ([FO-25]: the standby power check turns the
/// battery off and the fuel pumps and hydraulic pumps are switched off here), every label word for word,
/// every write's SkipCondition on its own state ("Already set"; the standby power check is the one write
/// with none, the check is the point), the checklist lines each step delivers
/// ([FO-6], [FO-7]), and that Power Up writes nothing the First Officer must not: no lever, no typed value
/// and no toggle-carrying control, whose write is refused from an unknown position ([L1011-4]).
/// </summary>
public class L1011FoFlowStructureTests
{
    private static readonly List<FlowDefinition<L1011FoStateEvaluator>> Flows = L1011FoFlowDefinitions.Build();
    private static readonly FlowDefinition<L1011FoStateEvaluator> PowerUp = Flows.Single(f => f.Id == "POWER_UP");

    private const string ExtPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";

    private static Step Of(string id) => PowerUp.Steps.Single(s => s.Id == id);

    /// <summary>The (key, target) pairs a switch step writes, whatever its action type.</summary>
    private static (string Key, int Target)[] Actions(Step s) => s.ActionType switch
    {
        FlowStepActionType.SetSwitch => new[] { (s.EventName!, s.TargetValue!.Value) },
        FlowStepActionType.SetSwitchMultiple => s.MultiActions.Select(a => (a.EventName, a.TargetValue!.Value)).ToArray(),
        _ => Array.Empty<(string, int)>(),
    };

    private static bool IsSwitch(Step s) =>
        s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple;

    private static string[] FuelPumpKeys { get; } =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    // ------------------------------------------------------------------ the flow

    /// <summary>The flows built so far, in the order they are flown.</summary>
    [Fact]
    public void TheFlows_AreInFlightOrder()
        => Assert.Equal(new[] { "POWER_UP", "PREFLIGHT" }, Flows.Select(f => f.Id));

    [Fact]
    public void PowerUp_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Power Up", PowerUp.Name);
        Assert.Equal(new[] { "POWER_UP", "POWER_UP_CL" }, PowerUp.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(PowerUp.Description));
    }

    [Fact]
    public void PowerUp_StepIdsAreInTheManualsOrder()
        => Assert.Equal(new[]
        {
            "PU_ENGINES_STOPPED", "PU_ON_GROUND",
            "PU_CHK_FIRE_HANDLES", "PU_CHK_GEAR", "PU_CHK_SPEED_BRAKE", "PU_CHK_FLAPS", "PU_CHK_FUEL_IGN",
            "PU_CONT_IGN", "PU_RADIO_MASTERS", "PU_XPDR", "PU_RADAR", "PU_ATMS", "PU_AC_PUMPS", "PU_FUEL_PUMPS",
            "PU_JETTISON", "PU_PAX_OXY", "PU_GALLEY",
            "PU_STBY_CHECK", "PU_AC_ESS", "PU_EXT_PWR", "PU_EXT_PWR_CPT", "PU_INS_ALIGN", "PU_INS_POSITION",
        }, PowerUp.Steps.Select(s => s.Id));

    [Fact]
    public void StepIds_AreUnique()
    {
        var ids = Flows.SelectMany(f => f.Steps).Select(s => s.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void EveryChecklistLine_IsDeliveredByOneStepOnly()
    {
        var linked = PowerUp.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ the two guards

    [Fact]
    public void TheTwoGuardsComeFirst_EachAReadOnlyStopThatLinksNothing()
    {
        var steps = PowerUp.Steps;
        var (engines, ground) = (steps[0], steps[1]);

        Assert.Equal("PU_ENGINES_STOPPED", engines.Id);
        Assert.Equal("FO_ENGINES_STOPPED", engines.ConditionFieldName);
        Assert.Equal("PU_ON_GROUND", ground.Id);
        Assert.Equal("SIM_ON_GROUND", ground.ConditionFieldName);

        foreach (var guard in new[] { engines, ground })
        {
            Assert.Equal(FlowStepActionType.WaitForCondition, guard.ActionType);
            Assert.Equal(FlowStepFailurePolicy.Stop, guard.FailurePolicy);
            Assert.Equal(5, guard.TimeoutSeconds);
            Assert.Null(guard.SkipCondition);
            Assert.Empty(guard.LinkedChecklistItemIds);
            Assert.False(guard.Condition!(double.NaN), $"{guard.Id}: an unread state must fail the guard");
            Assert.False(guard.Condition!(0), $"{guard.Id}: 0 must fail the guard");
            Assert.True(guard.Condition!(1), $"{guard.Id}: 1 must pass the guard");
        }
    }

    [Fact]
    public void BothGuardsPrecedeEveryStepThatWritesAnything()
    {
        var steps = PowerUp.Steps;
        int firstWrite = steps.FindIndex(IsSwitch);
        Assert.True(firstWrite > 1, "a write comes before the guards");
        Assert.All(steps.Take(2), s => Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType));
    }

    // ------------------------------------------------------------------ the read-only checks

    private static readonly (string Id, string Label, string Field, string Line)[] Checks =
    {
        ("PU_CHK_FIRE_HANDLES", "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", "PUC_FIRE_HANDLES"),
        ("PU_CHK_GEAR", "Gear lever: down", "FO_GEAR_LEVER_DOWN", "PUC_GEAR"),
        ("PU_CHK_SPEED_BRAKE", "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", "PUC_SPEED_BRAKE"),
        ("PU_CHK_FLAPS", "Flaps and flap handle: agree", "FO_FLAPS_AGREE", "PUC_FLAPS"),
        ("PU_CHK_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", "PUC_FUEL_IGN"),
    };

    [Fact]
    public void TheChecks_AreReadOnlyWaitsOfFiveSecondsThatSkipAloud()
    {
        foreach (var (id, label, field, _) in Checks)
        {
            var s = Of(id);
            Assert.Equal(label, s.Label);
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal(field, s.ConditionFieldName);
            Assert.Equal(5, s.TimeoutSeconds);
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.NotNull(s.SkipCondition);
            Assert.False(s.Condition!(double.NaN), $"{id}: an unread state must not pass");
            Assert.False(s.Condition!(0), $"{id}: a state reading false must not pass");
            Assert.True(s.Condition!(1), $"{id}: a state reading true must pass");
        }
    }

    [Fact]
    public void TheChecks_CompleteOnlyTheirOwnReadBackLine()
    {
        foreach (var (id, _, _, line) in Checks)
            Assert.Equal(new[] { line }, Of(id).LinkedChecklistItemIds);
    }

    [Fact]
    public void EveryCheckLinksOnlyReadBackLines()
    {
        var checks = PowerUp.Steps.Where(s => s.Id.StartsWith("PU_CHK_", StringComparison.Ordinal)).ToList();
        Assert.Equal(Checks.Length, checks.Count);
        Assert.All(checks, s => Assert.All(s.LinkedChecklistItemIds, id => Assert.StartsWith("PUC_", id, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheChecks_AreAlreadySetOnlyWhenTheAircraftShowsIt()
    {
        // Unread: not "already set" (a check that passes unread would tick a line over nothing).
        var unread = State();
        Assert.All(Checks, c => Assert.False(Of(c.Id).SkipCondition!(unread), c.Id));

        Assert.True(Of("PU_CHK_GEAR").SkipCondition!(State((L1011Levers.GearLeverKey, 0))));
        Assert.False(Of("PU_CHK_GEAR").SkipCondition!(State((L1011Levers.GearLeverKey, 100))));
        Assert.True(Of("PU_CHK_SPEED_BRAKE").SkipCondition!(State((L1011Levers.SpeedBrakeKey, 0))));
        Assert.False(Of("PU_CHK_SPEED_BRAKE").SkipCondition!(State((L1011Levers.SpeedBrakeKey, 100))));
    }

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int Target)[] Actions)[] Writes =
    {
        ("PU_CONT_IGN", "Continuous ignition: off", new[] { ("SWITCH_CONT_IGNITION", 0) }),
        ("PU_RADIO_MASTERS", "Radio master switches: on", new[] { ("SWITCH_MASTER_RADIO_ESS", 1), ("SWITCH_MASTER_RADIO_NO2", 1) }),
        ("PU_XPDR", "Transponder: standby", new[] { ("ROTARY_TCAS_MODE", 1) }),
        ("PU_RADAR", "Weather radar: off", new[] { ("SWITCH_WX_OFF", 1) }),
        ("PU_ATMS", "ATM switches: off", new[] { ("ROTARY_HYD_B2_MODE", 0), ("ROTARY_HYD_C2_MODE", 0) }),
        ("PU_AC_PUMPS", "AC hydraulic pumps: off", new[] { ("SWITCH_HYD_AC_PUMP_B3", 0), ("SWITCH_HYD_AC_PUMP_C3", 0) }),
        ("PU_FUEL_PUMPS", "Fuel pumps: all off", FuelPumpKeys.Select(k => (k, 0)).ToArray()),
        ("PU_JETTISON", "Jettison switches: off and guarded", new[]
        {
            ("SWITCH_FUEL_DUMP_MASTER", 0), ("SWITCH_FUEL_DUMP_2L", 0), ("SWITCH_FUEL_DUMP_1", 0),
            ("SWITCH_FUEL_DUMP_3", 0), ("SWITCH_FUEL_DUMP_2R", 0),
            ("GUARD_FUEL_DUMP_MASTER", 0), ("GUARD_FUEL_DUMP_2L", 0), ("GUARD_FUEL_DUMP_1", 0),
            ("GUARD_FUEL_DUMP_3", 0), ("GUARD_FUEL_DUMP_2R", 0),
        }),
        ("PU_PAX_OXY", "Passenger oxygen: off and guarded", new[] { ("SWITCH_PAX_OXY_MANUAL", 0), ("GUARD_PAX_OXY_MANUAL", 0) }),
        ("PU_GALLEY", "Galley power: off", new[] { ("SWITCH_GALLEY_BUS_1", 0), ("SWITCH_GALLEY_BUS_2", 0), ("SWITCH_GALLEY_BUS_3", 0) }),
        ("PU_STBY_CHECK", "Standby power check", new[] { (X.StandbyPowerCheck, 1) }),
        ("PU_AC_ESS", "AC essential bus selector: bus 3, generator 1", new[] { ("ROTARY_AC_ESS_BUS", 4) }),
        ("PU_EXT_PWR", "External power: on", new[] { (X.ExternalPower, 1) }),
        // One composite: it writes ALIGN to each unit not already at ALIGN or NAV.
        ("PU_INS_ALIGN", "INS 1, 2 and 3: align", new[] { (X.InsAlign, 1) }),
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
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
    {
        var expected = Writes.Select(w => w.Id).OrderBy(x => x).ToArray();
        var actual = PowerUp.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x).ToArray();
        Assert.Equal(expected, actual);
    }

    /// <summary>Every switch step but the standby power check says "Already set" on its own state.</summary>
    [Fact]
    public void EveryWrite_HasASkipCondition_ExceptTheStandbyPowerCheck()
    {
        foreach (var s in PowerUp.Steps.Where(IsSwitch))
        {
            if (s.Id == "PU_STBY_CHECK")
                Assert.Null(s.SkipCondition);
            else
                Assert.NotNull(s.SkipCondition);
        }
    }

    [Fact]
    public void TheSwitchWrites_EachCompleteTheirActionLineAndTheirReadBackLine()
    {
        foreach (var id in new[]
        {
            "PU_CONT_IGN", "PU_RADIO_MASTERS", "PU_XPDR", "PU_RADAR", "PU_ATMS", "PU_AC_PUMPS", "PU_FUEL_PUMPS",
            "PU_JETTISON", "PU_PAX_OXY",
        })
        {
            string readBack = "PUC_" + id["PU_".Length..];
            var s = Of(id);
            Assert.Equal(id, s.CompletesChecklistItemId);
            Assert.Equal(new[] { id, readBack }, s.LinkedChecklistItemIds);
        }
    }

    /// <summary>
    /// The aircraft's cockpit safety inspection has no galley line, so <c>POWER_UP_CL</c> has no
    /// <c>PUC_GALLEY</c>: the galley step links only its action line.
    /// </summary>
    [Fact]
    public void TheGalleyStep_LinksOnlyItsActionLine()
    {
        var s = Of("PU_GALLEY");
        Assert.Equal("PU_GALLEY", s.CompletesChecklistItemId);
        Assert.Equal(new[] { "PU_GALLEY" }, s.LinkedChecklistItemIds);
        Assert.DoesNotContain(PowerUp.Steps.SelectMany(x => x.LinkedChecklistItemIds), id => id == "PUC_GALLEY");
    }

    [Fact]
    public void TheStandbyPowerCheck_LinksExactlyTheFourLinesItDelivers()
    {
        var s = Of("PU_STBY_CHECK");
        Assert.Equal(X.StandbyPowerCheck, s.EventName);
        Assert.Equal(new[] { "PU_STBY_CHECK", "PUC_STBY_CHECK", "PUC_BATTERY", "PUC_STBY_ARMED" }, s.LinkedChecklistItemIds);
        // The electrical meter selector lines are the pilot's (FO-7): no step of the flow links them.
        Assert.DoesNotContain(PowerUp.Steps.SelectMany(x => x.LinkedChecklistItemIds),
            id => id.Contains("METER", StringComparison.OrdinalIgnoreCase) || id.Contains("SELECTOR", StringComparison.OrdinalIgnoreCase));
    }

    // The tank 2 left pump feeds the APU from the moment it starts turning, not only once it is up to speed.
    // A pilot who started it by hand and then runs Power Up must not have that pump cut, whether the APU has
    // reached 100 % (about 50 s after the start press) or is still spooling up on the way there.
    private const string ApuRpm = "L1011_RO_APU_RPM";

    [Fact]
    public void The_fuel_pump_step_is_left_alone_while_the_APU_runs_or_spools_up()
    {
        var s = Of("PU_FUEL_PUMPS");
        Assert.NotNull(s.LeaveAloneWhen);
        Assert.Equal("Fuel pumps left on: the APU is running.", s.LeaveAloneText);

        Assert.True(s.LeaveAloneWhen!(State((ApuRpm, 100))), "RPM 100: running");
        Assert.True(s.LeaveAloneWhen!(State((ApuRpm, 95))), "RPM 95: running");
        Assert.True(s.LeaveAloneWhen!(State((ApuRpm, 94.9))), "RPM 94.9: still spooling up");
        Assert.True(s.LeaveAloneWhen!(State((ApuRpm, 3))), "RPM 3: just started turning");
        Assert.True(s.LeaveAloneWhen!(State((ApuRpm, 1))), "RPM 1: turning");
        Assert.False(s.LeaveAloneWhen!(State((ApuRpm, 0.9))), "RPM 0.9: not turning");
        Assert.False(s.LeaveAloneWhen!(State((ApuRpm, 0))), "RPM 0: stopped");
        Assert.False(s.LeaveAloneWhen!(State()), "unread: the guards already refuse an unread aircraft");
    }

    /// <summary>Nothing else in Power Up depends on the APU: only the pump step reads it.</summary>
    [Fact]
    public void OnlyTheFuelPumpStep_IsLeftAloneWhileTheApuRuns()
        => Assert.Equal(new[] { "PU_FUEL_PUMPS" }, PowerUp.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));

    [Fact]
    public void TheAcEssentialBusSelector_LinksItsOwnLines()
        => Assert.Equal(new[] { "PU_AC_ESS", "PUC_AC_ESS" }, Of("PU_AC_ESS").LinkedChecklistItemIds);

    [Fact]
    public void ExternalPower_IsTurnedOnUnlessItIsAlreadyOn_AndTheCaptainIsAskedForGroundPower()
    {
        var on = Of("PU_EXT_PWR");
        Assert.Equal(new[] { "PU_EXT_PWR" }, on.LinkedChecklistItemIds);
        Assert.NotNull(on.SkipCondition);

        var captain = Of("PU_EXT_PWR_CPT");
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Ground power", captain.Label);
        Assert.Equal("Connect ground power on the tablet's Ground page, or the Preflight flow starts the APU.", captain.ReminderText);
        Assert.Empty(captain.LinkedChecklistItemIds);
        Assert.NotNull(captain.SkipCondition);
        Assert.True(captain.SkipCondition!(State((ExtPowerBreaker, 1))), "ground power on: nothing to remind");
        Assert.False(captain.SkipCondition!(State((ExtPowerBreaker, 0))));
        Assert.False(captain.SkipCondition!(State()), "unread: the reminder is spoken");
        Assert.Equal(on.Id, PowerUp.Steps[PowerUp.Steps.IndexOf(captain) - 1].Id);
    }

    [Fact]
    public void TheInsAlignStep_LinksItsOwnLines_AndTheCaptainEntersThePosition()
    {
        Assert.Equal(new[] { "PU_INS_ALIGN", "PUC_INS_ALIGN" }, Of("PU_INS_ALIGN").LinkedChecklistItemIds);

        var position = Of("PU_INS_POSITION");
        Assert.Equal(FlowStepActionType.CaptainReminder, position.ActionType);
        Assert.Equal("Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit.", position.Label);
        Assert.Equal(position.Label, position.ReminderText);
        Assert.Equal("PU_INS_POSITION", PowerUp.Steps[^1].Id);
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        return evaluator;
    }

    private static readonly string[] InsModeKeys = { "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3" };

    /// <summary>What the aircraft reads when the step's work is done. The external power step reads the
    /// breaker, not the switch it pulses; the INS align step reads its three mode selectors at ALIGN.</summary>
    private static (string Key, double Value)[] Done(Step s)
    {
        if (s.EventName == X.ExternalPower) return new[] { (ExtPowerBreaker, 1.0) };
        if (s.EventName == X.InsAlign) return InsModeKeys.Select(k => (k, 2.0)).ToArray();
        return Actions(s).Select(a => (a.Key, (double)a.Target)).ToArray();
    }

    private static double Opposite(double value) => value == 0 ? 1 : 0;

    [Fact]
    public void EveryWriteWithASkipCondition_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        foreach (var s in PowerUp.Steps.Where(x => IsSwitch(x) && x.SkipCondition != null))
        {
            var done = Done(s);
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, Opposite(wrong[i].Value));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
    }

    /// <summary>The INS selectors are done at ALIGN, and a unit already at NAV is further along: not rolled back.</summary>
    [Fact]
    public void InsAlign_IsAlreadySetAtAlignOrNav_ButNotAtOffOrStandby()
    {
        var s = Of("PU_INS_ALIGN").SkipCondition!;
        var keys = new[] { "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3" };
        foreach (double mode in new[] { 2.0, 3.0 })
            Assert.True(s(State(keys.Select(k => (k, mode)).ToArray())), $"mode {mode}");
        Assert.True(s(State(("ROTARY_INS_MODE_1", 3), ("ROTARY_INS_MODE_2", 2), ("ROTARY_INS_MODE_3", 3))));
        foreach (double mode in new[] { 0.0, 1.0 })
            Assert.False(s(State(keys.Select(k => (k, mode)).ToArray())), $"mode {mode}");
    }

    // ------------------------------------------------------------------ what Power Up never writes

    /// <summary>One definition answers (never attached: it only answers questions), as the fake TriStar's does.</summary>
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
        => PowerUp.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void EverySwitchKey_IsAPseudoKeyOrOneTheDefinitionAccepts()
    {
        int n = 0;
        foreach (var (step, key, _) in EveryWrittenKey())
        {
            n++;
            Assert.True(X.IsPseudoKey(key) || Definition.Value.FoAcceptsKey(key),
                $"{step}: '{key}' would silently do nothing");
        }
        Assert.True(n > 30, $"only {n} writes enumerated");
    }

    [Fact]
    public void OnlyTheCompositesArePseudoKeys()
        => Assert.Equal(new[] { X.StandbyPowerCheck, X.ExternalPower, X.InsAlign }.OrderBy(k => k, StringComparer.Ordinal),
            EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct().OrderBy(k => k, StringComparer.Ordinal));

    /// <summary>
    /// The INS align composite writes the three mode selectors itself, so no flow step names them any more:
    /// they must still be plain switch rows that take a write from an unknown position ([L1011-4]).
    /// </summary>
    [Fact]
    public void TheInsAlignComposite_WritesOnlyPlainSwitchRowsThatTakeAnUnknownPosition()
    {
        foreach (var key in InsModeKeys)
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);

            var plan = L1011WritePlan.ForSet(row.Control!, 2, current: null);
            Assert.True(plan.Refusal is null, $"{key} -> 2 is refused from an unknown position ({plan.Refusal}): it carries a toggle");
        }
    }

    [Fact]
    public void NoStepWritesALever()
        => Assert.All(EveryWrittenKey(), w => Assert.False(L1011Levers.IsLever(w.Key), $"{w.Step}: {w.Key} is a lever"));

    [Fact]
    public void NoStepWritesATypedValue_EachKeyIsAPlainSwitchRow()
    {
        foreach (var (step, key, _) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key)))
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
        }
    }

    /// <summary>
    /// A control whose click replay for the target carries a TOGGLE K: event is refused from an unknown
    /// position ([L1011-4]): sent the wrong way it inverts the switch. Planned from nothing read, every
    /// Power Up write must still be a plan, never that refusal, and the target must be a position of the control.
    /// </summary>
    [Fact]
    public void NoStepWritesAToggleCarryingKey()
    {
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key)))
        {
            var plan = L1011WritePlan.ForSet(Rows.Value[key].Control!, target, current: null);
            Assert.NotEqual(L1011WritePlan.NotAPositionRefusal, plan.Refusal);
            Assert.True(plan.Refusal is null,
                $"{step}: {key} -> {target} is refused from an unknown position ({plan.Refusal}): it carries a toggle");
        }
    }
}
