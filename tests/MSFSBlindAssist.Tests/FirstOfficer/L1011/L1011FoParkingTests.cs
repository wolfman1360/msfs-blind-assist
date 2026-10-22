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
/// The TriStar First Officer's Parking flow, its action group and its read-back (the aircraft's parking page), pinned
/// by structure and by behaviour: the step ids and their order; the aircraft stopped first, read only, Stop; the APU
/// waited for, its generator on line, or external power on when the APU generator is not and ground power is there; a
/// power source confirmed on line, read only, Stop, before the fuel and ignition go off; the engines read stopped, read
/// only, Stop, before the INS, the ATMs, the AC pumps and the tank pumps come off ([FO-25]); the fuel panel left with the
/// tank 2 left pump and the engine 2 tank valve on for the APU and the crossfeeds closed; every cut tick left alone
/// unless it may go ahead; the lines each step delivers ([FO-6], [FO-7]); the groups arrival groups ([FO-22]); and that
/// every key is one the definition takes, a toggle-carrying key only ever written over its own position read
/// ([L1011-4]).
/// </summary>
public class L1011FoParkingTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> Parking => Flows.Single(f => f.Id == "PARKING");

    private static Step Of(string id) => Parking.Steps.Single(s => s.Id == id);
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

    private const string GroundSpeed = "GROUND_VELOCITY";
    private const string Ground = "SIM_ON_GROUND";
    private const string ParkingBrake = L1011Levers.ParkingBrakeKey;
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuGenField = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";
    private const string ExtPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";
    private const string ExtPowerAvailable = "LAMP_EXTERNAL_POWER_AVAILABLE";

    private const string ApuNotStartedText = "APU not started: external power will be used.";
    private const string ApuGenText = "APU generator left off: the APU is not running.";
    private const string ExtPowerText = "External power left off: the APU generator is on line, or no ground power is available.";
    private const string FuelIgnitionText =
        "Fuel and ignition left on: not on the ground, or no APU generator or external power on line.";
    private const string ParkedOnlyText = "Left alone: the aircraft must be on the ground with the engines stopped.";

    private static readonly string[] FuelPumps =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    private static readonly string[] FuelIgnition = { "TOGGLE_CUTOFF_ENG_1", "TOGGLE_CUTOFF_ENG_2", "TOGGLE_CUTOFF_ENG_3" };

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

    /// <summary>Every engine's N3 gauge and fuel-and-ignition switch: stopped (a cold gauge reads 1.5) or running.</summary>
    private static IEnumerable<(string, double)> Engines(bool running)
    {
        for (int n = 1; n <= 3; n++)
        {
            yield return ($"L1011_RO_N3_{n}", running ? 59 : 1.5);
            yield return ($"TOGGLE_CUTOFF_ENG_{n}", running ? 1 : 0);
        }
    }

    private static L1011FoStateEvaluator Airborne() => State(Engines(running: true).Append((Ground, 0)).ToArray());
    private static L1011FoStateEvaluator RunningOnTheGround() => State(Engines(running: true).Append((Ground, 1)).ToArray());
    private static L1011FoStateEvaluator ParkedStopped() => State(Engines(running: false).Append((Ground, 1)).ToArray());

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void Parking_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Parking", Parking.Name);
        Assert.Equal(new[] { "PARKING", "PARKING_CL" }, Parking.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(Parking.Description));
    }

    /// <summary>The manual's order, with the engines-stopped wait directly after the fuel cut: the beacon and every step
    /// after it, every cut of the INS, the ATMs, the AC pumps and the tank pumps among them, wait for the engines to read
    /// stopped ([FO-25]), as the MD-11's beacon does.</summary>
    private static readonly string[] StepIds =
    {
        "PK_STOPPED", "PK_GROUND_LIGHTS", "PK_PARKING_BRAKE", "PK_APU_RUNNING", "PK_APU_GEN", "PK_EXT_PWR", "PK_POWER_SOURCE",
        "PK_FUEL_IGN_OFF", "PK_SPOOL_DOWN", "PK_ANTICOL", "PK_SEAT_BELTS", "PK_INS_OFF", "PK_XPDR", "PK_ATMS_AC_PUMPS",
        "PK_FUEL_PANEL", "PK_APU_BLEED", "PK_CPT_GROUND_POWER",
    };

    [Fact]
    public void Parking_StepIdsAreInOrder()
        => Assert.Equal(StepIds, Parking.Steps.Select(s => s.Id));

    // ------------------------------------------------------------------ the writes

    /// <summary>The fuel panel for the APU: the tank 2 left pump on first, the other seven off, the engine 2 tank valve
    /// on under its closed guard, the three crossfeeds closed.</summary>
    private static readonly (string, int?)[] FuelPanel =
        new[] { ("SWITCH_FUEL_PUMP_2L_1", (int?)1) }
            .Concat(All(0, FuelPumps.Where(p => p != "SWITCH_FUEL_PUMP_2L_1").ToArray()))
            .Concat(new[] { ("SWITCH_ENG_2_TANK_VALVE", (int?)1), ("GUARD_ENG_2_TANK_VALVE", (int?)0) })
            .Concat(All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"))
            .ToArray();

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("PK_GROUND_LIGHTS", "Nose, taxi and runway turnoff lights: off",
            All(0, "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF")),
        ("PK_PARKING_BRAKE", "Parking brake: set", All(1, ParkingBrake)),
        ("PK_APU_GEN", "APU generator: on line", All(1, X.ApuGenerator)),
        ("PK_EXT_PWR", "External power: on", All(1, X.ExternalPower)),
        ("PK_FUEL_IGN_OFF", "Fuel and ignition switches: off", All(0, FuelIgnition)),
        ("PK_ANTICOL", "Anti-collision lights: off", All(0, "SWITCH_ANTICOL_LIGHT")),
        ("PK_SEAT_BELTS", "Seat belt signs: off", All(0, "SWITCH_SEATBELT_LIGHT")),
        ("PK_INS_OFF", "INS: all off", All(0, "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3")),
        ("PK_XPDR", "Transponder: standby", All(1, "ROTARY_TCAS_MODE")),
        ("PK_ATMS_AC_PUMPS", "ATMs and AC hydraulic pumps: off",
            All(0, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE", "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3")),
        ("PK_FUEL_PANEL", "Fuel pumps: off except tank 2 left for the APU", FuelPanel),
        ("PK_APU_BLEED", "APU bleed: on", All(1, "SWITCH_APU_BLEED")),
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
            Parking.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Every step's lines, from the table: a write its action line then the read-back line it sets (external
    /// power its action line only: the read-back's power line is the APU generator's), the Captain's ground power line
    /// its own, the waits none.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["PK_STOPPED"] = Array.Empty<string>(),
        ["PK_GROUND_LIGHTS"] = new[] { "PK_GROUND_LIGHTS", "PKC_GROUND_LIGHTS" },
        ["PK_PARKING_BRAKE"] = new[] { "PK_PARKING_BRAKE", "PKC_PARKING_BRAKE" },
        ["PK_APU_RUNNING"] = Array.Empty<string>(),
        ["PK_APU_GEN"] = new[] { "PK_APU_GEN", "PKC_POWER" },
        ["PK_EXT_PWR"] = new[] { "PK_EXT_PWR" },
        ["PK_POWER_SOURCE"] = Array.Empty<string>(),
        ["PK_FUEL_IGN_OFF"] = new[] { "PK_FUEL_IGN_OFF", "PKC_FUEL_IGN" },
        ["PK_ANTICOL"] = new[] { "PK_ANTICOL", "PKC_ANTICOL" },
        ["PK_SEAT_BELTS"] = new[] { "PK_SEAT_BELTS", "PKC_SEAT_BELTS" },
        ["PK_SPOOL_DOWN"] = Array.Empty<string>(),
        ["PK_INS_OFF"] = new[] { "PK_INS_OFF", "PKC_INS" },
        ["PK_XPDR"] = new[] { "PK_XPDR", "PKC_XPDR" },
        ["PK_ATMS_AC_PUMPS"] = new[] { "PK_ATMS_AC_PUMPS", "PKC_ATMS_PUMPS" },
        ["PK_FUEL_PANEL"] = new[] { "PK_FUEL_PANEL", "PKC_TANK_PUMPS" },
        ["PK_APU_BLEED"] = new[] { "PK_APU_BLEED", "PKC_APU_BLEED" },
        ["PK_CPT_GROUND_POWER"] = new[] { "PK_CPT_GROUND_POWER" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in Parking.Steps)
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
        var linked = Parking.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static double Wrong(double done) => done == 0 ? 1 : 0;

    /// <summary>Every plain write is "Already set" only when every key reads its target, never with one wrong or
    /// unread; the composites are pinned below.</summary>
    [Fact]
    public void EveryPlainWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in Parking.Steps.Where(x => IsSwitch(x) && !Actions(x).Any(a => X.IsPseudoKey(a.Key))))
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
        Assert.Equal(Writes.Length - 2, n);
    }

    /// <summary>The APU generator is "Already set" on its breaker reading closed; external power on its breaker reading
    /// closed; neither unread.</summary>
    [Fact]
    public void The_power_composites_are_already_set_on_their_breakers()
    {
        var apu = Of("PK_APU_GEN").SkipCondition!;
        Assert.True(apu(State((ApuGenBreaker, 1))));
        Assert.False(apu(State((ApuGenBreaker, 0))));
        Assert.False(apu(State()));

        var ext = Of("PK_EXT_PWR").SkipCondition!;
        Assert.True(ext(State((ExtPowerBreaker, 1))));
        Assert.False(ext(State((ExtPowerBreaker, 0))));
        Assert.False(ext(State()));
    }

    // ------------------------------------------------------------------ the waits

    /// <summary>The aircraft stopped first: read only, the stock ground speed below 1 knot within two minutes, Stop,
    /// unread failing it, no skip and no line. It is the sweep's ground guard for the fuel and ignition cut.</summary>
    [Fact]
    public void The_aircraft_stopped_comes_first_read_only_and_stops_the_flow()
    {
        var stopped = Parking.Steps[0];
        Assert.Equal("PK_STOPPED", stopped.Id);
        Assert.Equal("Aircraft stopped", stopped.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, stopped.ActionType);
        Assert.Equal(GroundSpeed, stopped.ConditionFieldName);
        Assert.Equal(120, stopped.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, stopped.FailurePolicy);
        Assert.Null(stopped.SkipCondition);
        Assert.Empty(stopped.LinkedChecklistItemIds);
        Assert.True(stopped.Condition!(0));
        Assert.True(stopped.Condition!(0.9));
        Assert.False(stopped.Condition!(1));
        Assert.False(stopped.Condition!(15));
        Assert.False(stopped.Condition!(double.NaN), "unread must fail");
    }

    /// <summary>The ground speed is the stock base variable, registered for the TriStar and delivered continuously, never
    /// a synthetic: the evaluator reads it straight from the cache.</summary>
    [Fact]
    public void The_ground_speed_is_a_registered_continuous_variable_the_evaluator_reads()
    {
        var vars = new IniL1011Definition().GetVariables();
        Assert.True(vars.TryGetValue(GroundSpeed, out var def), $"{GroundSpeed} is not registered");
        Assert.Equal(UpdateFrequency.Continuous, def!.UpdateFrequency);
        Assert.DoesNotContain(GroundSpeed, L1011FoStateEvaluator.SyntheticKeys);
        Assert.Equal(0.4, State((GroundSpeed, 0.4)).GetValue(GroundSpeed));
        Assert.True(double.IsNaN(State().GetValue(GroundSpeed)));
    }

    /// <summary>After Landing started the APU; Parking waits up to two minutes for it to run, then goes on (Skip). An APU
    /// that does not turn is left alone at once, saying external power will be used; one already running is already
    /// set.</summary>
    [Fact]
    public void The_APU_running_wait_skips_and_is_left_alone_when_the_APU_is_not_turning()
    {
        var wait = Of("PK_APU_RUNNING");
        Assert.Equal("APU running", wait.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal("FO_APU_RUNNING", wait.ConditionFieldName);
        Assert.Equal(120, wait.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, wait.FailurePolicy);
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.False(wait.Condition!(double.NaN));
        Assert.True(wait.Condition!(1));

        Assert.True(wait.SkipCondition!(State((ApuRpm, 100))), "running: already set");
        Assert.False(wait.SkipCondition!(State((ApuRpm, 50))), "spooling up: waited for");
        Assert.False(wait.SkipCondition!(State()), "unread");

        Assert.Equal(ApuNotStartedText, wait.LeaveAloneText);
        Assert.True(wait.LeaveAloneWhen!(State((ApuRpm, 0))), "not turning");
        Assert.True(wait.LeaveAloneWhen!(State()), "unread");
        Assert.False(wait.LeaveAloneWhen!(State((ApuRpm, 50))), "spooling up");
        Assert.False(wait.LeaveAloneWhen!(State((ApuRpm, 100))), "running");
    }

    /// <summary>
    /// The APU generator goes on line only with the APU running; external power goes on only when the APU generator is
    /// not on line and ground power is available. Each is left alone otherwise, saying what stays off, and each tick
    /// carries its step's rule and text.
    /// </summary>
    [Fact]
    public void The_APU_generator_and_external_power_steps_leave_alone_as_specified()
    {
        foreach (var (leaves, text) in new[] { (Of("PK_APU_GEN").LeaveAloneWhen!, Of("PK_APU_GEN").LeaveAloneText),
                     (Line("PK_APU_GEN").LeaveAloneWhen!, Line("PK_APU_GEN").LeaveAloneText) })
        {
            Assert.Equal(ApuGenText, text);
            Assert.False(leaves(State((ApuRpm, 100))), "APU running: the generator goes on line");
            Assert.True(leaves(State((ApuRpm, 0))), "APU stopped");
            Assert.True(leaves(State((ApuRpm, 60))), "APU still spooling up");
            Assert.True(leaves(State()), "APU unread");
        }

        foreach (var (leaves, text) in new[] { (Of("PK_EXT_PWR").LeaveAloneWhen!, Of("PK_EXT_PWR").LeaveAloneText),
                     (Line("PK_EXT_PWR").LeaveAloneWhen!, Line("PK_EXT_PWR").LeaveAloneText) })
        {
            Assert.Equal(ExtPowerText, text);
            Assert.False(leaves(State((ApuGenBreaker, 0), (ExtPowerAvailable, 1))), "no APU generator, ground power there");
            Assert.False(leaves(State((ExtPowerAvailable, 1))), "APU generator unread, ground power there");
            Assert.True(leaves(State((ApuGenBreaker, 1), (ExtPowerAvailable, 1))), "APU generator on line");
            Assert.True(leaves(State((ApuGenBreaker, 0), (ExtPowerAvailable, 0))), "no ground power");
            Assert.True(leaves(State((ApuGenBreaker, 0))), "ground power unread");
        }
    }

    /// <summary>The fuel cut takes the engine generators off the buses: the APU generator or external power is
    /// confirmed on line first, read only, up to two minutes, Stop (never drop to the battery), "Already set" when one
    /// already is, and the fuel and ignition go off next.</summary>
    [Fact]
    public void The_fuel_cut_follows_the_power_source_wait()
    {
        var steps = Parking.Steps;
        int wait = steps.FindIndex(s => s.Id == "PK_POWER_SOURCE"), cut = steps.FindIndex(s => s.Id == "PK_FUEL_IGN_OFF");
        Assert.True(wait >= 0 && cut == wait + 1, $"PK_POWER_SOURCE at {wait}, PK_FUEL_IGN_OFF at {cut}");
        var source = steps[wait];
        Assert.Equal("APU generator or external power on", source.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, source.ActionType);
        Assert.Equal("FO_POWER_SOURCE_ON", source.ConditionFieldName);
        Assert.Equal(120, source.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, source.FailurePolicy);
        Assert.False(source.Condition!(double.NaN));
        Assert.Empty(source.LinkedChecklistItemIds);
        Assert.NotNull(source.SkipCondition);
        Assert.True(source.SkipCondition!(State((ApuGenBreaker, 1))), "APU generator on line");
        Assert.True(source.SkipCondition!(State((ExtPowerBreaker, 1))), "external power on line");
        Assert.False(source.SkipCondition!(State((ApuGenBreaker, 0), (ExtPowerBreaker, 0))), "neither");
        Assert.False(source.SkipCondition!(State()), "unread");
        // Nothing before the wait cuts anything.
        Assert.All(steps.Take(wait), s => Assert.Null(FoCutGuardSweepTests.CutKind(s.Label)));
    }

    /// <summary>The engines read stopped, read only, up to two minutes, Stop, directly after the fuel cut: the beacon, the
    /// seat belt signs, the INS, the ATMs, the AC pumps and the tank pumps come off only after it, so an engine that does
    /// not stop ends the flow with the beacon still on and nothing cut ([FO-25]).</summary>
    [Fact]
    public void The_beacon_the_INS_and_the_pumps_come_off_only_after_the_engines_stop()
    {
        var steps = Parking.Steps;
        int spool = steps.FindIndex(s => s.Id == "PK_SPOOL_DOWN");
        var wait = steps[spool];
        Assert.Equal("Engines stopped", wait.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal("FO_ENGINES_STOPPED", wait.ConditionFieldName);
        Assert.Equal(120, wait.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, wait.FailurePolicy);
        Assert.Null(wait.SkipCondition);
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.False(wait.Condition!(double.NaN));
        Assert.False(wait.Condition!(0));
        Assert.True(wait.Condition!(1));

        Assert.Equal(steps.FindIndex(s => s.Id == "PK_FUEL_IGN_OFF") + 1, spool);
        foreach (var id in new[] { "PK_ANTICOL", "PK_SEAT_BELTS", "PK_INS_OFF", "PK_XPDR", "PK_ATMS_AC_PUMPS", "PK_FUEL_PANEL", "PK_APU_BLEED" })
            Assert.True(steps.FindIndex(s => s.Id == id) > spool, $"{id} before the engines read stopped");
    }

    /// <summary>The fuel panel leaves the APU its feed: the tank 2 left pump written on before any pump goes off, the
    /// engine 2 tank valve on under its closed guard, the crossfeeds closed. "Already set" needs all three: the only pump
    /// on, the engine 2 feed, the crossfeeds closed.</summary>
    [Fact]
    public void The_fuel_panel_keeps_the_APU_feed()
    {
        var step = Of("PK_FUEL_PANEL");
        Assert.Equal(("SWITCH_FUEL_PUMP_2L_1", (int?)1), Actions(step)[0]);
        var done = FuelPanel.Select(a => (a.Item1, (double)a.Item2!.Value)).ToArray();
        Assert.True(step.SkipCondition!(State(done)));
        Assert.True(L1011FoStateEvaluator.SyntheticKeys.Contains("FO_ONLY_APU_PUMP_ON"));
        Assert.Equal(1, State(done).GetValue("FO_ONLY_APU_PUMP_ON"));
        Assert.Equal(1, State(done).GetValue("FO_ENG2_FUEL_FEED"));
        Assert.Equal(1, State(done).GetValue("FO_CROSSFEEDS_CLOSED"));
    }

    // ------------------------------------------------------------------ the cut ticks ([FO-25])

    /// <summary>The APU generator's breaker or external power's breaker read closed, the other open: a power source on
    /// line.</summary>
    private static (string, double)[] Source(bool apu) =>
        new[] { (ApuGenBreaker, apu ? 1.0 : 0.0), (ExtPowerBreaker, apu ? 0.0 : 1.0) };

    private static readonly (string, double)[] NoSource = { (ApuGenBreaker, 0.0), (ExtPowerBreaker, 0.0) };

    /// <summary>The fuel and ignition tick is an engine cut, and cuts the engine generators off the buses with it: refused
    /// airborne or unread, and on the ground with neither the APU generator nor external power read on line, as the flow's
    /// power source wait refuses it. On the ground with a source on line it goes ahead with the engines running, which is
    /// its point.</summary>
    [Fact]
    public void The_fuel_and_ignition_tick_needs_the_ground_and_a_power_source()
    {
        var tick = Line("PK_FUEL_IGN_OFF");
        Assert.Equal(FuelIgnitionText, tick.LeaveAloneText);
        Assert.True(tick.LeaveAloneWhen!(State(Engines(running: true).Append((Ground, 0)).Concat(Source(apu: true)).ToArray())),
            "airborne, a source on line");
        Assert.True(tick.LeaveAloneWhen!(State()), "unread");
        Assert.True(tick.LeaveAloneWhen!(State(Engines(running: true).Concat(Source(apu: true)).ToArray())), "the ground unread");
        Assert.True(tick.LeaveAloneWhen!(State(Engines(running: true).Append((Ground, 1)).Concat(NoSource).ToArray())),
            "on the ground, engines running, no source on line");
        Assert.True(tick.LeaveAloneWhen!(RunningOnTheGround()), "on the ground, engines running, the sources unread");
        Assert.False(tick.LeaveAloneWhen!(State(Engines(running: true).Append((Ground, 1)).Concat(Source(apu: true)).ToArray())),
            "on the ground, engines running, the APU generator on line");
        Assert.False(tick.LeaveAloneWhen!(State(Engines(running: true).Append((Ground, 1)).Concat(Source(apu: false)).ToArray())),
            "on the ground, engines running, external power on line");
        Assert.False(tick.LeaveAloneWhen!(State(Engines(running: false).Append((Ground, 1)).Concat(Source(apu: false)).ToArray())),
            "parked, external power on line");
        Assert.Equal("Engine", FoCutGuardSweepTests.CutKind(tick.Label));
    }

    /// <summary>Through the real checklist manager: with the engines running on the ground and no power source on line,
    /// the tick is refused at once, aloud, and nothing is written.</summary>
    [Fact]
    public void The_fuel_and_ignition_tick_through_the_checklist_manager_is_refused_aloud_with_no_power_source()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var ac = new FakeL1011Aircraft();
        foreach (var key in FuelIgnition)
            ac.Set(key, 1);
        var exec = new X();
        exec.SetTransport(ac);
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected(Engines(running: true).Append((Ground, 1)).Concat(NoSource).ToArray()), exec, groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "PK_FUEL_IGN_OFF");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(FuelIgnitionText, spoken);
        Assert.False(line.IsChecked);
        Assert.Empty(ac.Io);
    }

    public static IEnumerable<object[]> ParkedOnlyTicks() =>
        new[] { "PK_INS_OFF", "PK_ATMS_AC_PUMPS", "PK_FUEL_PANEL" }.Select(id => new object[] { id });

    /// <summary>The INS, ATM and AC pump, and fuel panel ticks cut the INS or pumps: refused airborne, unread or with an
    /// engine running, and going ahead parked with every engine stopped. The fleet's cut sweep sees each by its label.</summary>
    [Theory]
    [MemberData(nameof(ParkedOnlyTicks))]
    public void The_INS_ATM_and_pump_ticks_go_ahead_only_parked(string id)
    {
        var tick = Line(id);
        Assert.Equal(ParkedOnlyText, tick.LeaveAloneText);
        Assert.True(tick.LeaveAloneWhen!(Airborne()), "airborne");
        Assert.True(tick.LeaveAloneWhen!(State()), "unread");
        Assert.True(tick.LeaveAloneWhen!(RunningOnTheGround()), "engines running");
        Assert.True(tick.LeaveAloneWhen!(State((Ground, 1))), "engines unread");
        Assert.False(tick.LeaveAloneWhen!(ParkedStopped()), "parked, engines stopped");
        Assert.Equal("Pumps", FoCutGuardSweepTests.CutKind(tick.Label));
    }

    [Theory]
    [MemberData(nameof(ParkedOnlyTicks))]
    public void A_parked_only_tick_through_the_checklist_manager_is_refused_aloud_with_the_engines_running(string id)
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected(Engines(running: true).Append((Ground, 1)).ToArray()), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == id);

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(ParkedOnlyText, spoken);
        Assert.False(line.IsChecked);
    }

    /// <summary>In the flow only the APU wait, the APU generator and external power are left alone; in the action group
    /// those two and the four cut ticks.</summary>
    [Fact]
    public void Only_the_power_steps_and_the_cut_ticks_are_left_alone()
    {
        Assert.Equal(new[] { "PK_APU_RUNNING", "PK_APU_GEN", "PK_EXT_PWR" },
            Parking.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));
        Assert.Equal(new[] { "PK_APU_GEN", "PK_EXT_PWR", "PK_FUEL_IGN_OFF", "PK_INS_OFF", "PK_ATMS_AC_PUMPS", "PK_FUEL_PANEL" },
            Group("PARKING").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
        Assert.DoesNotContain(Parking.Steps, s => s.RequiresStepId != null || s.RunOnlyIfStepSent != null);
    }

    // ------------------------------------------------------------------ the Captain's ground power

    /// <summary>Last, unconditional, the Captain's reminder: ground power is the tablet's.</summary>
    [Fact]
    public void The_Captain_is_reminded_of_ground_power_last()
    {
        var captain = Parking.Steps[^1];
        Assert.Equal("PK_CPT_GROUND_POWER", captain.Id);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Connect ground power on the tablet's Ground page if required", captain.Label);
        Assert.Equal(captain.Label, captain.ReminderText);
        Assert.Null(captain.SkipCondition);
        var line = Line("PK_CPT_GROUND_POWER");
        Assert.Equal(ChecklistItemType.CaptainReminder, line.Type);
        Assert.Equal(captain.ReminderText, line.ReminderText);
        Assert.Null(line.CheckAction);
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = Parking.Steps.Select(s => s.ConditionFieldName).OfType<string>()
            .Concat(new[] { ApuRpm, ApuGenBreaker, ExtPowerBreaker, ExtPowerAvailable, Ground })
            .Concat(Groups.Where(g => g.Id is "PARKING" or "PARKING_CL").SelectMany(g => g.Items)
                .SelectMany(i => i.AdditionalStateFields.Prepend(i.StateFieldName)).OfType<string>())
            .Distinct().ToList();
        foreach (var field in fields)
        {
            if (synthetic.Contains(field)) continue;
            Assert.True(vars.TryGetValue(field, out var def), $"{field} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
            if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.True(polled.Contains(field), $"OnRequest field {field} is not polled");
        }
        Assert.True(fields.Count >= 20, $"only {fields.Count} fields enumerated");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheParkingGroups_FollowAfterLanding_Named()
    {
        Assert.Equal(new[] { "PARKING", "PARKING_CL" }, Groups.Select(g => g.Id).SkipWhile(id => id != "PARKING").Take(2));
        Assert.Equal("AFTER_LANDING_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "PARKING").Last());
        Assert.Equal("Parking", Group("PARKING").Name);
        Assert.Equal("Parking", Group("PARKING_CL").Name);
    }

    /// <summary>[FO-22]: the arrival, never marked by an in-flight start.</summary>
    [Fact]
    public void EveryParkingGroup_IsArrival_AndNeverDeparture()
    {
        foreach (var id in Parking.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Arrival.Contains(id), $"{id} is not an arrival group");
            Assert.False(FoDepartureGroups.Departure.Contains(id), $"{id} is a departure group too");
        }
    }

    [Fact]
    public void The_read_back_has_no_check_action()
        => Assert.All(Group("PARKING_CL").Items, i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
    {
        "PK_GROUND_LIGHTS", "PK_PARKING_BRAKE", "PK_APU_GEN", "PK_EXT_PWR", "PK_FUEL_IGN_OFF", "PK_ANTICOL", "PK_SEAT_BELTS",
        "PK_INS_OFF", "PK_XPDR", "PK_ATMS_AC_PUMPS", "PK_FUEL_PANEL", "PK_APU_BLEED", "PK_CPT_GROUND_POWER",
    };

    /// <summary>The waits are flow-only.</summary>
    [Fact]
    public void Parking_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("PARKING").Items.Select(i => i.Id));
        foreach (var id in new[] { "PK_STOPPED", "PK_APU_RUNNING", "PK_POWER_SOURCE", "PK_SPOOL_DOWN" })
            Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == id);
    }

    [Fact]
    public void Parking_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("PARKING").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void Parking_LineKinds()
    {
        foreach (var i in Group("PARKING").Items)
        {
            if (i.Id == "PK_CPT_GROUND_POWER")
                continue;
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            if (i.Id == "PK_FUEL_PANEL")
                Assert.Equal(new[] { "FO_ENG2_FUEL_FEED", "FO_CROSSFEEDS_CLOSED" }, i.AdditionalStateFields);
            else
                Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each line reads the state its step's "Already set" reads: a single switch its key, a multi switch its
    /// synthetic, the APU generator its breaker closed, external power its breaker closed, the parking brake the lever,
    /// the fuel panel the only pump on (with the engine 2 feed and the crossfeeds closed beside it).</summary>
    [Theory]
    [InlineData("PK_GROUND_LIGHTS", "FO_GROUND_LIGHTS_OFF", 1)]
    [InlineData("PK_PARKING_BRAKE", ParkingBrake, 1)]
    [InlineData("PK_APU_GEN", "FO_APU_GEN_ON_LINE", 1)]
    [InlineData("PK_EXT_PWR", "FO_EXT_POWER_ON", 1)]
    [InlineData("PK_FUEL_IGN_OFF", "FO_FUEL_IGNITION_OFF", 1)]
    [InlineData("PK_ANTICOL", "SWITCH_ANTICOL_LIGHT", 0)]
    [InlineData("PK_SEAT_BELTS", "SWITCH_SEATBELT_LIGHT", 0)]
    [InlineData("PK_INS_OFF", "FO_INS_OFF", 1)]
    [InlineData("PK_XPDR", "ROTARY_TCAS_MODE", 1)]
    [InlineData("PK_ATMS_AC_PUMPS", "FO_ATMS_AC_PUMPS_OFF", 1)]
    [InlineData("PK_FUEL_PANEL", "FO_ONLY_APU_PUMP_ON", 1)]
    [InlineData("PK_APU_BLEED", "SWITCH_APU_BLEED", 1)]
    public void Parking_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
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

    /// <summary>An aircraft with work for the step: each key the step writes the other way from its target; for the APU
    /// generator the APU running with both relays open; for external power ground power available, the switch off, the
    /// breaker open and the ties closed.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, target == 0 ? 1 : 0);
        if (step.EventName == X.ApuGenerator)
        {
            ac.Set(ApuRpm, 100);
            ac.Set(ApuGenField, 0);
            ac.Set(ApuGenBreaker, 0);
        }
        if (step.EventName == X.ExternalPower)
        {
            ac.Set(ExtPowerAvailable, 1);
            ac.Set("TOGGLE_EXT_PWR", 0);
            ac.Set(ExtPowerBreaker, 0);
            foreach (int n in new[] { 1, 2, 3 })
                ac.Set($"L1011_RO_AC_TIE_{n}", 1);
        }
        return ac;
    }

    public static IEnumerable<object[]> ParkingLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "PARKING").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft.</summary>
    [Theory]
    [MemberData(nameof(ParkingLinesWithACheckAction))]
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

    // ---- the read-back, word for word ------------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value.</summary>
    private sealed record ReadBack(string Id, string Label, string Field, double Done = 1);

    private static readonly ReadBack[] ParkingReadBack =
    {
        new("PKC_GROUND_LIGHTS", "Nose wheel landing, taxi and runway turnoff lights: off", "FO_GROUND_LIGHTS_OFF"),
        new("PKC_PARKING_BRAKE", "Parking brake: set", ParkingBrake, 1),
        new("PKC_POWER", "APU generator or external power: on", "FO_POWER_SOURCE_ON"),
        new("PKC_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF"),
        new("PKC_ANTICOL", "Anti coll light: off", "SWITCH_ANTICOL_LIGHT", 0),
        new("PKC_SEAT_BELTS", "Seatbelts sign: off", "SWITCH_SEATBELT_LIGHT", 0),
        new("PKC_INS", "INS: all off", "FO_INS_OFF"),
        new("PKC_XPDR", "Transponder: STBY", "ROTARY_TCAS_MODE", 1),
        new("PKC_ATMS_PUMPS", "ATMs and AC hyd pumps: off", "FO_ATMS_AC_PUMPS_OFF"),
        new("PKC_TANK_PUMPS", "Tank pumps: one on", "FO_ONLY_APU_PUMP_ON"),
        new("PKC_APU_BLEED", "APU bleed air shut off valve: on", "SWITCH_APU_BLEED", 1),
    };

    [Fact]
    public void The_read_back_is_word_for_word()
        => Assert.Equal(ParkingReadBack.Select(l => (l.Id, l.Label)), Group("PARKING_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in ParkingReadBack)
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

    /// <summary>A finished run latches the action group and the read-back, and nothing else.</summary>
    [Fact]
    public void Parking_LatchesItsTwoGroups()
        => Assert.Equal(Parking.RelatedChecklistGroupIds, Parking.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The labels with a figure, and the stopped test's knot, read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_and_the_stopped_test_hold_under_a_comma_decimal_culture(string culture)
    {
        var (labels, stopped) = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "PARKING");
            var all = flow.Steps.Select(s => s.Label)
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
            return (all, flow.Steps[0].Condition!(0.5));
        });
        Assert.Contains("Fuel pumps: off except tank 2 left for the APU", labels);
        Assert.Contains("ATMs and AC hyd pumps: off", labels);
        Assert.True(stopped);
    }

    // ================================================================== what Parking writes

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
        => Parking.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(33, EveryWrittenKey().Count());
    }

    /// <summary>The composites are the APU generator and external power, each on; the one lever is the parking brake,
    /// set.</summary>
    [Fact]
    public void The_composites_and_the_lever()
    {
        Assert.Equal(new[] { X.ApuGenerator, X.ExternalPower }, EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct());
        Assert.Equal(new[] { ("PK_PARKING_BRAKE", ParkingBrake, (int?)1) }, EveryWrittenKey().Where(w => L1011Levers.IsLever(w.Key)));
    }

    private static IEnumerable<(string Step, string Key, int Target)> PlainSwitchWrites()
        => EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !L1011Levers.IsLever(w.Key))
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

    /// <summary>The keys whose click replay carries a TOGGLE K: event, refused from an unknown position ([L1011-4]).</summary>
    private static readonly string[] ToggleCarrying =
        { "SWITCH_APU_BLEED", "SWITCH_SEATBELT_LIGHT", "TOGGLE_CUTOFF_ENG_1", "TOGGLE_CUTOFF_ENG_2", "TOGGLE_CUTOFF_ENG_3" };

    /// <summary>
    /// A toggle-carrying key is written only by a step whose "Already set" reads that key at the target, so a key read
    /// the other way or unread is never taken for done: the executor reads each key first, and from an unknown position
    /// the definition refuses the write. The seat belts and the APU bleed are single switches; the three fuel and
    /// ignition switches are one step, already set only with all three read off. Planned from nothing read, every other
    /// key must still be a plan.
    /// </summary>
    [Fact]
    public void No_step_writes_a_toggle_carrying_key_from_an_unread_position_except_through_a_skip()
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
            var done = Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
            int other = target == 0 ? 1 : 0;
            Assert.True(s.SkipCondition!(State(done)), $"{step}: every key at its target must be already set");
            Assert.False(s.SkipCondition!(State(done.Select(d => d.Key == key ? (key, (double)other) : d).ToArray())),
                $"{step}: {key} the other way must not be already set");
            Assert.False(s.SkipCondition!(State(done.Where(d => d.Key != key).ToArray())), $"{step}: {key} unread must not be already set");
        }
        Assert.Equal(ToggleCarrying.OrderBy(k => k, StringComparer.Ordinal), toggles.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(FlowStepActionType.SetSwitch, Of("PK_SEAT_BELTS").ActionType);
        Assert.Equal(FlowStepActionType.SetSwitch, Of("PK_APU_BLEED").ActionType);
    }

    /// <summary>The fuel and ignition step, from a fuel and ignition switch unread, writes that one not at all and still
    /// cuts the others: each key is read before it is written.</summary>
    [Fact]
    public async Task An_unread_fuel_and_ignition_switch_is_never_written()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set("TOGGLE_CUTOFF_ENG_1", 1);
        ac.Set("TOGGLE_CUTOFF_ENG_3", 1);
        var exec = new X();
        exec.SetTransport(ac);

        Assert.False(await exec.ExecuteStepAsync(Of("PK_FUEL_IGN_OFF")).WaitAsync(Hang));

        Assert.Equal(new[] { ("TOGGLE_CUTOFF_ENG_1", 0.0), ("TOGGLE_CUTOFF_ENG_3", 0.0) }, ac.Writes);
        Assert.Null(ac.Get("TOGGLE_CUTOFF_ENG_2"));
    }
}
