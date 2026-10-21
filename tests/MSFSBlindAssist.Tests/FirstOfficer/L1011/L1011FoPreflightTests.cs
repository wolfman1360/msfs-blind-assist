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
/// The TriStar First Officer's Preflight flow, its action group and its four read-back checklists (the flight
/// engineer station, the overhead, the main panel and the centre console), pinned by structure and by
/// behaviour: the step ids and their order, the two guards first ([FO-25]: the flow switches the packs, the
/// AC hydraulic pumps and the power transfer units off), every label, key and target word for word, every
/// write's "Already set" on its own state (the three tests excepted: the test is the point), the lines each
/// step delivers ([FO-6], [FO-7]), the cabin altitude provider and its Captain fallback ([FO-20]), the packs
/// left alone off the ground, and that every key is one the definition takes, a toggle-carrying key only ever
/// written over its own position read ([L1011-4]).
/// </summary>
public class L1011FoPreflightTests
{
    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> Preflight => Flows.Single(f => f.Id == "PREFLIGHT");

    private static Step Of(string id) => Preflight.Steps.Single(s => s.Id == id);
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

    private static readonly string[] PfcsKeys =
    {
        "SWITCH_PFCS_PITCH_1", "SWITCH_PFCS_PITCH_2", "SWITCH_PFCS_ROLL_1", "SWITCH_PFCS_ROLL_2", "SWITCH_PFCS_RUDDER",
        "SWITCH_STABILIZER_A", "SWITCH_STABILIZER_B", "SWITCH_STABILIZER_C", "SWITCH_STABILIZER_D",
        "SWITCH_AILERON_L_OUTBD", "SWITCH_AILERON_L_INBD", "SWITCH_AILERON_R_INBD", "SWITCH_AILERON_R_OUTBD",
        "SWITCH_SPOILER_L56", "SWITCH_SPOILER_LR4", "SWITCH_SPOILER_LR1", "SWITCH_SPOILER_LR2", "SWITCH_SPOILER_LR3",
        "SWITCH_SPOILER_R56",
    };

    private static readonly string[] FcesKeys =
    {
        "SWITCH_ACS_1", "SWITCH_ACS_2", "SWITCH_YAW_SAS_1", "SWITCH_YAW_SAS_2", "SWITCH_STALL_WARN_1", "SWITCH_STALL_WARN_2",
        "SWITCH_PITCH_TRIM_1", "SWITCH_PITCH_TRIM_2", "SWITCH_DLC_AUTO_1", "SWITCH_DLC_AUTO_2", "SWITCH_ATS_1", "SWITCH_ATS_2",
        "SWITCH_MACH_TRIM_1", "SWITCH_MACH_TRIM_2",
    };

    private static readonly string[] InstrumentSourceKeys =
    {
        "SWITCH_INSTR_SRC_CPT_NAV", "SWITCH_INSTR_SRC_CPT_HDG", "SWITCH_INSTR_SRC_CPT_ATT", "SWITCH_INSTR_SRC_CPT_ALT",
        "SWITCH_INSTR_SRC_CPT_IAS", "SWITCH_INSTR_SRC_CPT_FLT_DIR",
        "SWITCH_INSTR_SRC_FO_NAV", "SWITCH_INSTR_SRC_FO_HDG", "SWITCH_INSTR_SRC_FO_ATT", "SWITCH_INSTR_SRC_FO_ALT",
        "SWITCH_INSTR_SRC_FO_IAS", "SWITCH_INSTR_SRC_FO_FLT_DIR",
    };

    private static readonly string[] Packs = { "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3" };
    private static readonly string[] HotAir = { "SWITCH_HOT_AIR_ENG_1", "SWITCH_HOT_AIR_ENG_3", "SWITCH_ISLN_VALVE_OVHT_2A", "SWITCH_ISLN_VALVE_OVHT_2B" };

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void Preflight_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Preflight", Preflight.Name);
        Assert.Equal(new[] { "PREFLIGHT", "PREFLIGHT_FE_CL", "PREFLIGHT_OVHD_CL", "PREFLIGHT_MAIN_CL", "PREFLIGHT_CONSOLE_CL" },
            Preflight.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(Preflight.Description));
    }

    private static readonly string[] StepIds =
    {
        "PF_ENGINES_STOPPED", "PF_ON_GROUND",
        "PF_APU_MASTER", "PF_APU_MODE_MIN", "PF_APU_FUEL", "PF_APU_START", "PF_APU_RUNNING", "PF_APU_GEN", "PF_AC_TIES",
        "PF_APU_AIR", "PF_PACKS_OFF", "PF_PACKS_AUTO", "PF_HOT_AIR_OFF", "PF_ZONE_TRIMS", "PF_APU_BLEED", "PF_PACKS_ON",
        "PF_APU_MODE_NORM", "PF_HOT_AIR_ON", "PF_FIRE_TEST", "PF_WHEEL_WELL_TEST", "PF_HYD_ENGINE_PUMPS", "PF_CHK_ATMS",
        "PF_AC_PUMPS_PTUS_OFF", "PF_HYD_CHECK", "PF_AC_METER", "PF_DC_METER", "PF_CHK_AC_ESS", "PF_TRANSFERS_OFF",
        "PF_CROSSFEEDS", "PF_TANK_VALVES", "PF_CHK_APU_PUMP", "PF_CHK_JETTISON", "PF_FUEL_CTRL",
        "PF_MOVIE", "PF_SLAT_MONITOR", "PF_HI_PRESS", "PF_ISLN_VALVES", "PF_ECS", "PF_PRESS_MODE", "PF_CABIN_ALT",
        "PF_CABIN_ALT_CPT", "PF_COMPASSES", "PF_WIPERS", "PF_ANTI_ICE", "PF_WINDSHIELD", "PF_SIDE_WINDOWS", "PF_AIR_DATA",
        "PF_MACH_FEEL", "PF_IGNITION", "PF_STBY_LIGHTS", "PF_PFCS", "PF_FCES", "PF_BRAKE_SEL", "PF_ANTISKID_OFF",
        "PF_PARKING_BRAKE", "PF_AUTOBRAKE", "PF_EXT_LIGHTS", "PF_EMER_LIGHTS", "PF_CHK_STBY_ARMED", "PF_NO_SMOKING",
        "PF_INSTR_SOURCES", "PF_DME", "PF_FLIGHT_DIRECTORS", "PF_PADDLES_OFF", "PF_CHK_DISCONNECT", "PF_SPEED_BRAKE",
        "PF_CHK_FUEL_IGN", "PF_XPDR", "PF_CHK_INS", "PF_CHK_FIRE_HANDLES", "PF_CHK_RADIO_MASTERS", "PF_CHK_START_LIGHTS",
        "PF_CHK_GEAR",
        "PF_CPT_INS_ROUTE", "PF_CPT_PMS", "PF_CPT_NAV", "PF_CPT_ALTIMETERS", "PF_CPT_AFCS", "PF_CPT_TAKEOFF_DATA",
        "PF_CPT_RADIOS",
    };

    [Fact]
    public void Preflight_StepIdsAreInOrder()
        => Assert.Equal(StepIds, Preflight.Steps.Select(s => s.Id));

    // ------------------------------------------------------------------ the two guards

    [Fact]
    public void TheTwoGuardsComeFirst_EachAReadOnlyStopThatLinksNothing()
    {
        var steps = Preflight.Steps;
        var (engines, ground) = (steps[0], steps[1]);

        Assert.Equal("PF_ENGINES_STOPPED", engines.Id);
        Assert.Equal("Engines stopped", engines.Label);
        Assert.Equal("FO_ENGINES_STOPPED", engines.ConditionFieldName);
        Assert.Equal("PF_ON_GROUND", ground.Id);
        Assert.Equal("On the ground", ground.Label);
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
    public void BothGuardsPrecedeEveryWrite()
    {
        var steps = Preflight.Steps;
        int firstWrite = steps.FindIndex(IsSwitch);
        Assert.True(firstWrite > 1, "a write comes before the guards");
        Assert.All(steps.Take(2), s => Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType));
    }

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("PF_APU_MASTER", "APU master: on", All(1, "TOGGLE_APU_POWER")),
        ("PF_APU_MODE_MIN", "APU compressor mode: minimum", All(0, "SWITCH_APU_MODE")),
        ("PF_APU_FUEL", "APU fuel: tank 2 left pump and engine 2 tank valve on",
            new (string, int?)[] { ("SWITCH_FUEL_PUMP_2L_1", 1), ("SWITCH_ENG_2_TANK_VALVE", 1), ("GUARD_ENG_2_TANK_VALVE", 0) }),
        ("PF_APU_START", "APU: start", All(1, X.ApuStart)),
        ("PF_APU_GEN", "APU generator: on line", All(1, X.ApuGenerator)),
        ("PF_AC_TIES", "AC bus ties: closed", All(1, X.AcTies)),
        ("PF_APU_AIR", "Fuselage isolation and crossbleed valves: on", All(1, "SWITCH_APU_ISLN_VALVE", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3")),
        ("PF_PACKS_OFF", "Packs: off", All(0, Packs)),
        ("PF_PACKS_AUTO", "Pack control: auto", All(1, "SWITCH_BLEED_PLENUM_ENG_1", "SWITCH_BLEED_PLENUM_ENG_2", "SWITCH_BLEED_PLENUM_ENG_3")),
        ("PF_HOT_AIR_OFF", "Hot air and hot air isolation valves: off", All(0, HotAir)),
        ("PF_ZONE_TRIMS", "Zone trim switches: on",
            All(1, "SWITCH_FLT_STA_CLOSE", "SWITCH_FWD_CAB_CLOSE", "SWITCH_MID_CAB_CLOSE", "SWITCH_AFT_CAB_CLOSE")),
        ("PF_APU_BLEED", "APU bleed: on", All(1, "SWITCH_APU_BLEED")),
        ("PF_PACKS_ON", "Packs: on", All(1, Packs)),
        ("PF_APU_MODE_NORM", "APU compressor mode: normal", All(1, "SWITCH_APU_MODE")),
        ("PF_HOT_AIR_ON", "Hot air and hot air isolation valves: on", All(1, HotAir)),
        ("PF_FIRE_TEST", "Engine and APU fire test", All(1, X.FireTest)),
        ("PF_WHEEL_WELL_TEST", "Wheel well fire test", All(1, X.WheelWellFireTest)),
        ("PF_HYD_ENGINE_PUMPS", "Engine hydraulic pumps: on and guarded",
            All(1, "SWITCH_HYD_PUMP_A1", "SWITCH_HYD_PUMP_B1", "SWITCH_HYD_PUMP_C1", "SWITCH_HYD_PUMP_D1")
                .Concat(All(0, "GUARD_HYD_PUMP_A1", "GUARD_HYD_PUMP_B1", "GUARD_HYD_PUMP_C1", "GUARD_HYD_PUMP_D1")).ToArray()),
        ("PF_AC_PUMPS_PTUS_OFF", "AC hydraulic pumps and PTUs: off",
            All(0, "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3", "SWITCH_PTU_AB", "SWITCH_PTU_CD")),
        ("PF_HYD_CHECK", "Hydraulic check", All(1, X.HydraulicCheck)),
        ("PF_AC_METER", "AC meter: APU generator", All(4, "ROTARY_PWR_SOURCE")),
        ("PF_DC_METER", "DC meter: battery", All(4, "ROTARY_STBY_DC_BUS")),
        ("PF_TRANSFERS_OFF", "1A and 3A transfers: off", All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
        ("PF_CROSSFEEDS", "Crossfeed valves: closed", All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED")),
        ("PF_TANK_VALVES", "Engine tank valves: on and guarded",
            All(1, "SWITCH_ENG_1_TANK_VALVE", "SWITCH_ENG_2_TANK_VALVE", "SWITCH_ENG_3_TANK_VALVE")
                .Concat(All(0, "GUARD_ENG_1_TANK_VALVE", "GUARD_ENG_2_TANK_VALVE", "GUARD_ENG_3_TANK_VALVE")).ToArray()),
        ("PF_FUEL_CTRL", "Fuel control amplifier switches: off",
            All(0, "SWITCH_FUEL_CTRL_OVRD_ENG_1", "SWITCH_FUEL_CTRL_OVRD_ENG_2", "SWITCH_FUEL_CTRL_OVRD_ENG_3")),
        ("PF_MOVIE", "Movie interrupt: off and guarded", All(0, "MOVIES_OFF", "GUARD_MOVIES_OFF")),
        ("PF_SLAT_MONITOR", "Slat monitor: off", All(0, "SWITCH_SLAT_MONITOR")),
        ("PF_HI_PRESS", "High pressure bleed valves: on", All(1, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3")),
        ("PF_ISLN_VALVES", "Engine isolation valves: on", All(1, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3")),
        ("PF_ECS", "Overboard cooling and cargo heat: on",
            All(1, "SWITCH_COOL_AIR_OVBD", "SWITCH_CARGO_HEAT_FWD", "SWITCH_CARGO_HEAT_MID", "SWITCH_CARGO_HEAT_AFT")),
        ("PF_PRESS_MODE", "Pressurization mode: normal", All(1, "ROTARY_CAB_PRESS_MODE")),
        // The provider's step: its target is the plan's cruise cabin altitude, resolved when the flow runs.
        ("PF_CABIN_ALT", "Cabin altitude selector: cruise cabin altitude", new (string, int?)[] { (X.CabinAltitudeSelect, null) }),
        ("PF_COMPASSES", "Compasses: magnetic", All(1, "SWITCH_COMPASS_1_MAG", "SWITCH_COMPASS_2_MAG")),
        ("PF_WIPERS", "Wipers: off", All(0, "ROTARY_CPT_WIPER", "ROTARY_FO_WIPER")),
        ("PF_ANTI_ICE", "Engine and wing anti-ice: off",
            All(0, "SWITCH_ANTI_ICE_ENG_1", "SWITCH_ANTI_ICE_ENG_2", "SWITCH_ANTI_ICE_ENG_3", "SWITCH_ANTI_ICE_LEFT_AUTO",
                "SWITCH_ANTI_ICE_LEFT_MAN", "SWITCH_ANTI_ICE_RIGHT_AUTO", "SWITCH_ANTI_ICE_RIGHT_MAN")),
        ("PF_WINDSHIELD", "Windshield heat: normal", All(1, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO")),
        ("PF_SIDE_WINDOWS", "Side window heat: on",
            All(1, "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR")),
        ("PF_AIR_DATA", "Air data heat: off", All(0, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT")),
        ("PF_MACH_FEEL", "Mach feel and rudder limiter: on", All(1, "SWITCH_MACH_FEEL_1", "SWITCH_MACH_FEEL_2", "SWITCH_RUDDER_LIMITER")),
        ("PF_IGNITION", "Ignition selector: A or B", All(0, "ROTARY_ENG_START_SEL")),
        ("PF_STBY_LIGHTS", "Instrument standby lights: bright", All(1, "TOGGLE_INSTR_STBY_LIGHTS")),
        ("PF_PFCS", "Primary flight control switches: on", All(1, PfcsKeys)),
        ("PF_FCES", "Flight control electronics switches: on", All(1, FcesKeys)),
        ("PF_BRAKE_SEL", "Brake system: B", All(0, "TOGGLE_BRAKE_SYS_SEL")),
        ("PF_ANTISKID_OFF", "Anti-skid: off", All(0, "SWITCH_ANTISKID_ON")),
        ("PF_PARKING_BRAKE", "Parking brake: set", All(1, L1011Levers.ParkingBrakeKey)),
        ("PF_AUTOBRAKE", "Autobrake: off", All(1, "ROTARY_AUTO_BRAKE_MODE")),
        ("PF_EXT_LIGHTS", "Exterior lights: logo and position on, the rest off",
            new (string, int?)[]
            {
                ("TOGGLE_LOGO_LIGHT", 1), ("SWITCH_POSITION_LIGHT", 1), ("TOGGLE_LANDING_LEFT", 0), ("TOGGLE_LANDING_RIGHT", 0),
                ("TOGGLE_LANDING_NOSE", 0), ("TOGGLE_RWY_TURNOFF", 0), ("SWITCH_STROBE_LIGHT", 0), ("SWITCH_ANTICOL_LIGHT", 0),
                ("SWITCH_FLOOD_LIGHT", 1), ("SWITCH_WHEEL_LIGHT", 1),
            }),
        ("PF_EMER_LIGHTS", "Emergency lights: armed", new (string, int?)[] { ("TOGGLE_EMER_LIGHTING", 1), ("GUARD_EMER_LIGHTING", 0) }),
        ("PF_NO_SMOKING", "No smoking signs: on", All(1, "SWITCH_SMOKING_LIGHT")),
        ("PF_INSTR_SOURCES", "Instrument source selectors: normal", All(0, InstrumentSourceKeys)),
        ("PF_DME", "DME 1 and 2: normal", All(1, "ROTARY_NAV_1_MODE", "ROTARY_NAV_2_MODE")),
        ("PF_FLIGHT_DIRECTORS", "Flight directors: on", All(1, "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B")),
        ("PF_PADDLES_OFF", "Autopilot paddles: off", All(2, "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B")),
        ("PF_SPEED_BRAKE", "Speed brake lever: forward", All(0, L1011Levers.SpeedBrakeKey)),
        ("PF_XPDR", "Transponder: standby", All(1, "ROTARY_TCAS_MODE")),
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
            Assert.Equal(id == "PF_CABIN_ALT", s.TargetValueProvider != null);
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            Preflight.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>The three tests have no state to be "already set" on: the test is the point.</summary>
    private static readonly string[] Tests = { "PF_FIRE_TEST", "PF_WHEEL_WELL_TEST", "PF_HYD_CHECK" };

    [Fact]
    public void Every_write_but_the_tests_has_a_skip_condition()
    {
        foreach (var s in Preflight.Steps.Where(IsSwitch))
        {
            if (Tests.Contains(s.Id))
                Assert.Null(s.SkipCondition);
            else
                Assert.True(s.SkipCondition != null, $"{s.Id} has no SkipCondition");
        }

        var running = Of("PF_APU_RUNNING");
        Assert.Equal("APU running", running.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, running.ActionType);
        Assert.Equal("FO_APU_RUNNING", running.ConditionFieldName);
        Assert.Equal(120, running.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Stop, running.FailurePolicy);
        Assert.Null(running.SkipCondition);
        Assert.Equal(new[] { "PFC_APU_START" }, running.LinkedChecklistItemIds);
        Assert.False(running.Condition!(double.NaN));
        Assert.False(running.Condition!(0));
        Assert.True(running.Condition!(1));
    }

    /// <summary>Every step's lines, from the table: a write its action line then the read-back lines it sets,
    /// a check or a wait the read-back line it reads, an unconditional Captain step its reminder line.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["PF_APU_MASTER"] = new[] { "PF_APU_MASTER", "PFC_APU_MASTER" },
        ["PF_APU_MODE_MIN"] = new[] { "PF_APU_MODE_MIN" },
        ["PF_APU_FUEL"] = new[] { "PF_APU_FUEL", "PFC_APU_FUEL" },
        ["PF_APU_START"] = new[] { "PF_APU_START" },
        ["PF_APU_RUNNING"] = new[] { "PFC_APU_START" },
        ["PF_APU_GEN"] = new[] { "PF_APU_GEN", "PFC_APU_GEN" },
        ["PF_AC_TIES"] = new[] { "PF_AC_TIES", "PFC_AC_TIES" },
        ["PF_APU_AIR"] = new[] { "PF_APU_AIR", "PFC_FUSELAGE_ISLN", "PFC_XBLEEDS" },
        ["PF_PACKS_OFF"] = new[] { "PF_PACKS_OFF" },
        ["PF_PACKS_AUTO"] = new[] { "PF_PACKS_AUTO", "PFC_PACK_AUTO" },
        ["PF_HOT_AIR_OFF"] = new[] { "PF_HOT_AIR_OFF" },
        ["PF_ZONE_TRIMS"] = new[] { "PF_ZONE_TRIMS", "PFC_ZONE_TRIMS" },
        ["PF_APU_BLEED"] = new[] { "PF_APU_BLEED", "PFC_APU_BLEED" },
        ["PF_PACKS_ON"] = new[] { "PF_PACKS_ON", "PFC_PACKS" },
        ["PF_APU_MODE_NORM"] = new[] { "PF_APU_MODE_NORM", "PFC_APU_MODE_NORM" },
        ["PF_HOT_AIR_ON"] = new[] { "PF_HOT_AIR_ON", "PFC_HOT_AIR" },
        ["PF_FIRE_TEST"] = new[] { "PF_FIRE_TEST", "PFC_FIRE_TEST", "PFC_LOOP_SELECTORS" },
        ["PF_WHEEL_WELL_TEST"] = new[] { "PF_WHEEL_WELL_TEST", "PFC_WHEEL_WELL_TEST" },
        ["PF_HYD_ENGINE_PUMPS"] = new[] { "PF_HYD_ENGINE_PUMPS", "PFC_HYD_ENGINE_PUMPS" },
        ["PF_CHK_ATMS"] = new[] { "PFC_ATMS" },
        ["PF_AC_PUMPS_PTUS_OFF"] = new[] { "PF_AC_PUMPS_PTUS_OFF", "PFC_PTUS" },
        ["PF_HYD_CHECK"] = new[] { "PF_HYD_CHECK", "PFC_HYD_CHECK" },
        ["PF_AC_METER"] = new[] { "PF_AC_METER", "PFC_AC_METER" },
        ["PF_DC_METER"] = new[] { "PF_DC_METER", "PFC_DC_METER" },
        ["PF_CHK_AC_ESS"] = new[] { "PFC_AC_ESS" },
        ["PF_TRANSFERS_OFF"] = new[] { "PF_TRANSFERS_OFF", "PFC_TRANSFERS" },
        ["PF_CROSSFEEDS"] = new[] { "PF_CROSSFEEDS", "PFC_CROSSFEEDS" },
        ["PF_TANK_VALVES"] = new[] { "PF_TANK_VALVES", "PFC_TANK_VALVES" },
        ["PF_CHK_APU_PUMP"] = new[] { "PFC_APU_PUMP_ONLY" },
        ["PF_CHK_JETTISON"] = new[] { "PFC_JETTISON" },
        ["PF_FUEL_CTRL"] = new[] { "PF_FUEL_CTRL", "PFC_FUEL_CTRL" },
        ["PF_MOVIE"] = new[] { "PF_MOVIE", "PFC_MOVIE" },
        ["PF_SLAT_MONITOR"] = new[] { "PF_SLAT_MONITOR", "PFC_SLAT_MONITOR" },
        ["PF_HI_PRESS"] = new[] { "PF_HI_PRESS", "PFC_HI_PRESS" },
        ["PF_ISLN_VALVES"] = new[] { "PF_ISLN_VALVES", "PFC_ISLN_VALVES" },
        ["PF_ECS"] = new[] { "PF_ECS", "PFC_ECS" },
        ["PF_PRESS_MODE"] = new[] { "PF_PRESS_MODE", "PFC_PRESS_MODE" },
        ["PF_CABIN_ALT"] = new[] { "PF_CABIN_ALT", "PFC_CABIN_ALT" },
        ["PF_CABIN_ALT_CPT"] = Array.Empty<string>(),
        ["PF_COMPASSES"] = new[] { "PF_COMPASSES", "PFO_COMPASSES" },
        ["PF_WIPERS"] = new[] { "PF_WIPERS", "PFO_WIPERS" },
        ["PF_ANTI_ICE"] = new[] { "PF_ANTI_ICE", "PFO_ANTI_ICE" },
        ["PF_WINDSHIELD"] = new[] { "PF_WINDSHIELD", "PFO_WINDSHIELD" },
        ["PF_SIDE_WINDOWS"] = new[] { "PF_SIDE_WINDOWS", "PFO_SIDE_WINDOWS" },
        ["PF_AIR_DATA"] = new[] { "PF_AIR_DATA", "PFO_AIR_DATA" },
        ["PF_MACH_FEEL"] = new[] { "PF_MACH_FEEL", "PFO_MACH_FEEL" },
        ["PF_IGNITION"] = new[] { "PF_IGNITION", "PFO_IGNITION" },
        ["PF_STBY_LIGHTS"] = new[] { "PF_STBY_LIGHTS", "PFO_STBY_LIGHTS" },
        ["PF_PFCS"] = new[] { "PF_PFCS", "PFO_PFCS" },
        ["PF_FCES"] = new[] { "PF_FCES", "PFO_FCES" },
        ["PF_BRAKE_SEL"] = new[] { "PF_BRAKE_SEL", "PFO_BRAKE_SEL", "PFM_BRAKE_SYS" },
        ["PF_ANTISKID_OFF"] = new[] { "PF_ANTISKID_OFF", "PFO_ANTISKID" },
        ["PF_PARKING_BRAKE"] = new[] { "PF_PARKING_BRAKE", "PFO_PARKING_BRAKE", "PFX_PARKING_BRAKE" },
        ["PF_AUTOBRAKE"] = new[] { "PF_AUTOBRAKE", "PFO_AUTOBRAKE" },
        ["PF_EXT_LIGHTS"] = new[] { "PF_EXT_LIGHTS", "PFO_EXT_LIGHTS" },
        ["PF_EMER_LIGHTS"] = new[] { "PF_EMER_LIGHTS", "PFO_EMER_LIGHTS" },
        ["PF_CHK_STBY_ARMED"] = new[] { "PFO_STBY_POWER" },
        ["PF_NO_SMOKING"] = new[] { "PF_NO_SMOKING", "PFO_NO_SMOKING" },
        ["PF_INSTR_SOURCES"] = new[] { "PF_INSTR_SOURCES", "PFM_INSTR_SOURCE" },
        ["PF_DME"] = new[] { "PF_DME", "PFM_DME" },
        ["PF_FLIGHT_DIRECTORS"] = new[] { "PF_FLIGHT_DIRECTORS", "PFM_FLIGHT_DIRECTORS" },
        ["PF_PADDLES_OFF"] = new[] { "PF_PADDLES_OFF", "PFM_AUTOPILOT_MASTERS" },
        ["PF_CHK_DISCONNECT"] = new[] { "PFX_DISCONNECT" },
        ["PF_SPEED_BRAKE"] = new[] { "PF_SPEED_BRAKE", "PFX_SPEED_BRAKE" },
        ["PF_CHK_FUEL_IGN"] = new[] { "PFX_FUEL_IGN" },
        ["PF_XPDR"] = new[] { "PF_XPDR", "PFX_XPDR" },
        ["PF_CHK_INS"] = new[] { "PFO_INS" },
        ["PF_CHK_FIRE_HANDLES"] = new[] { "PFO_FIRE_HANDLES" },
        ["PF_CHK_RADIO_MASTERS"] = new[] { "PFO_RADIO_MASTERS" },
        ["PF_CHK_START_LIGHTS"] = new[] { "PFO_START_LIGHTS" },
        ["PF_CHK_GEAR"] = new[] { "PFM_GEAR" },
        ["PF_CPT_INS_ROUTE"] = new[] { "PF_CPT_INS_ROUTE" },
        ["PF_CPT_PMS"] = new[] { "PF_CPT_PMS" },
        ["PF_CPT_NAV"] = new[] { "PF_CPT_NAV" },
        ["PF_CPT_ALTIMETERS"] = new[] { "PF_CPT_ALTIMETERS" },
        ["PF_CPT_AFCS"] = new[] { "PF_CPT_AFCS" },
        ["PF_CPT_TAKEOFF_DATA"] = new[] { "PF_CPT_TAKEOFF_DATA" },
        ["PF_CPT_RADIOS"] = new[] { "PF_CPT_RADIOS" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in Preflight.Steps.Skip(2))
        {
            Assert.True(Links.TryGetValue(s.Id, out var expected), $"{s.Id} is not in the table");
            Assert.Equal(expected, s.LinkedChecklistItemIds);
            Assert.Equal(expected!.FirstOrDefault(), s.CompletesChecklistItemId);
        }
        Assert.Equal(StepIds.Length - 2, Links.Count);
    }

    [Fact]
    public void EveryChecklistLine_IsDeliveredByOneStepOnly()
    {
        var linked = Preflight.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ the read-only checks

    private static readonly (string Id, string Label, string Field, string Line)[] Checks =
    {
        ("PF_CHK_ATMS", "ATM switches: off", "FO_ATMS_OFF", "PFC_ATMS"),
        ("PF_CHK_AC_ESS", "AC essential power selector: bus 3, generator 1", "ROTARY_AC_ESS_BUS", "PFC_AC_ESS"),
        ("PF_CHK_APU_PUMP", "Fuel pumps: tank 2 left pump only", "FO_ONLY_APU_PUMP_ON", "PFC_APU_PUMP_ONLY"),
        ("PF_CHK_JETTISON", "Jettison switches: off and guarded", "FO_JETTISON_SAFE", "PFC_JETTISON"),
        ("PF_CHK_STBY_ARMED", "Standby power: armed", "FO_STBY_ARMED", "PFO_STBY_POWER"),
        ("PF_CHK_DISCONNECT", "Pitch and roll disconnect handles: stowed", "FO_DISCONNECT_HANDLES_STOWED", "PFX_DISCONNECT"),
        ("PF_CHK_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", "PFX_FUEL_IGN"),
        ("PF_CHK_INS", "INS 1, 2 and 3: aligning or navigating", "FO_INS_ALIGN_OR_NAV", "PFO_INS"),
        ("PF_CHK_FIRE_HANDLES", "Fire handles: in", "FO_FIRE_HANDLES_IN", "PFO_FIRE_HANDLES"),
        ("PF_CHK_RADIO_MASTERS", "Radio master switches: on", "FO_RADIO_MASTERS_ON", "PFO_RADIO_MASTERS"),
        ("PF_CHK_START_LIGHTS", "Engine start lights: out", "FO_START_LIGHTS_OUT", "PFO_START_LIGHTS"),
        ("PF_CHK_GEAR", "Landing gear: down, three green", L1011GearConfirmation.DownField, "PFM_GEAR"),
    };

    [Fact]
    public void TheChecks_AreReadOnlyWaitsOfFiveSecondsThatSkipAloud()
    {
        Assert.Equal(Checks.Select(c => c.Id), Preflight.Steps.Where(s => s.Id.Contains("_CHK_", StringComparison.Ordinal)).Select(s => s.Id));
        foreach (var (id, label, field, _) in Checks)
        {
            var s = Of(id);
            double done = field == "ROTARY_AC_ESS_BUS" ? 4 : 1;
            Assert.Equal(label, s.Label);
            Assert.Equal(FlowStepActionType.WaitForCondition, s.ActionType);
            Assert.Equal(field, s.ConditionFieldName);
            Assert.Equal(5, s.TimeoutSeconds);
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.NotNull(s.SkipCondition);
            Assert.False(s.Condition!(double.NaN), $"{id}: an unread state must not pass");
            Assert.False(s.Condition!(0), $"{id}: a state reading 0 must not pass");
            Assert.True(s.Condition!(done), $"{id}: a state reading {done} must pass");
        }
    }

    /// <summary>A read-only check completes only the read-back line that reads its own field, under the same
    /// rule: never an action line, never a line it cannot see.</summary>
    [Fact]
    public void Every_check_completes_only_lines_reading_its_field()
    {
        var readBacks = new[] { "PREFLIGHT_FE_CL", "PREFLIGHT_OVHD_CL", "PREFLIGHT_MAIN_CL", "PREFLIGHT_CONSOLE_CL" }
            .SelectMany(g => Group(g).Items).ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var (id, _, field, line) in Checks)
        {
            var s = Of(id);
            Assert.Equal(new[] { line }, s.LinkedChecklistItemIds);
            Assert.True(readBacks.TryGetValue(line, out var item), $"{id}: {line} is not a read-back line");
            Assert.Equal(field, item!.StateFieldName);
            foreach (double v in new[] { double.NaN, 0, 1, 2, 3, 4, 5 })
                Assert.Equal(s.Condition!(v), item.StateCondition!(v));
        }
    }

    [Fact]
    public void TheChecks_AreAlreadySetOnlyWhenTheAircraftShowsIt()
    {
        var unread = State();
        Assert.All(Checks, c => Assert.False(Of(c.Id).SkipCondition!(unread), c.Id));
        Assert.True(Of("PF_CHK_AC_ESS").SkipCondition!(State(("ROTARY_AC_ESS_BUS", 4))));
        Assert.False(Of("PF_CHK_AC_ESS").SkipCondition!(State(("ROTARY_AC_ESS_BUS", 3))));
        Assert.True(Of("PF_CHK_DISCONNECT").SkipCondition!(State(("LEVER_PITCH_DISC", 0), ("LEVER_ROLL_DISC", 0))));
        Assert.False(Of("PF_CHK_DISCONNECT").SkipCondition!(State(("LEVER_PITCH_DISC", 1), ("LEVER_ROLL_DISC", 0))));
    }

    // ------------------------------------------------------------------ the Captain steps

    private static readonly (string Id, string Label)[] CaptainSteps =
    {
        ("PF_CPT_INS_ROUTE", "Load the route and set the initial track on INS 1, 2 and 3"),
        ("PF_CPT_PMS", "Set the PMS"),
        ("PF_CPT_NAV", "Set NAV 1 and 2 and the courses: Ctrl+N"),
        ("PF_CPT_ALTIMETERS", "Set the altimeters to QNH: Ctrl+B"),
        ("PF_CPT_AFCS", "Set the speed, heading and altitude on the autopilot"),
        ("PF_CPT_TAKEOFF_DATA", "Enter the take-off data on the tablet"),
        ("PF_CPT_RADIOS", "Set the radios"),
    };

    [Fact]
    public void TheCaptainSteps_AreRemindersWordForWord_AndCloseTheFlow()
    {
        foreach (var (id, label) in CaptainSteps)
        {
            var s = Of(id);
            Assert.Equal(FlowStepActionType.CaptainReminder, s.ActionType);
            Assert.Equal(label, s.Label);
            Assert.Equal(label, s.ReminderText);
            Assert.Null(s.SkipCondition);
        }
        Assert.Equal(CaptainSteps.Select(c => c.Id), Preflight.Steps.TakeLast(CaptainSteps.Length).Select(s => s.Id));
    }

    // ------------------------------------------------------------------ the cabin altitude ([FO-20])

    private const string CabinAltReadout = "L1011_RO_CABIN_ALT_SELECTED";

    private static L1011FoStateEvaluator Planned(int? cruiseFt, params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        evaluator.SetPlannedPressurizationAltitudes(cruiseFt, cruiseFt is null ? null : 120);
        return evaluator;
    }

    [Fact]
    public void The_cabin_altitude_provider_is_null_without_a_plan_and_the_captain_steps_in()
    {
        var step = Of("PF_CABIN_ALT");
        var captain = Of("PF_CABIN_ALT_CPT");
        Assert.Equal(X.CabinAltitudeSelect, step.EventName);
        Assert.Null(step.TargetValue);
        Assert.Equal(FlowStepActionType.CaptainReminder, captain.ActionType);
        Assert.Equal("Cabin altitude selector", captain.Label);
        Assert.Equal("Set the cabin altitude selector for the cruise on the engineer's pressurization panel.", captain.ReminderText);
        Assert.Empty(captain.LinkedChecklistItemIds);
        var ids = Preflight.Steps.Select(s => s.Id).ToList();
        Assert.Equal(step.Id, ids[ids.IndexOf(captain.Id) - 1]);

        // No plan: no target, nothing sent (a silent skip), and the Captain is asked.
        var noPlan = Planned(null, (CabinAltReadout, 1000));
        Assert.Null(step.TargetValueProvider!(noPlan));
        Assert.False(step.SkipCondition!(noPlan));
        Assert.False(captain.SkipCondition!(noPlan));

        // A plan at 39,000 ft, the selector already at its cabin altitude: both steps are already set.
        int cabinFt = L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(39000);
        var set = Planned(39000, (CabinAltReadout, cabinFt * L1011FoStateEvaluator.MetresPerFoot));
        Assert.Equal(cabinFt, step.TargetValueProvider!(set));
        Assert.True(step.SkipCondition!(set));
        Assert.True(captain.SkipCondition!(set));

        // A plan, the selector elsewhere: the step writes the plan's value, and the Captain is asked
        // only if that write does not take.
        var elsewhere = Planned(39000, (CabinAltReadout, 0));
        Assert.Equal(cabinFt, step.TargetValueProvider!(elsewhere));
        Assert.False(step.SkipCondition!(elsewhere));
        Assert.False(captain.SkipCondition!(elsewhere));

        // Unread: never already set.
        Assert.False(captain.SkipCondition!(Planned(39000)));
    }

    // ------------------------------------------------------------------ the packs, left alone off the ground

    private const string PacksText = "Packs left on: not read on the ground.";

    private static (string, double)[] Engines(bool running)
        => Enumerable.Range(1, 3).SelectMany(n => new (string, double)[]
            {
                ($"L1011_RO_N3_{n}", running ? 59 : 1.5), ($"TOGGLE_CUTOFF_ENG_{n}", running ? 1 : 0),
            }).ToArray();

    private static (string, double) Ground(bool onGround) => ("SIM_ON_GROUND", onGround ? 1 : 0);

    private static L1011FoStateEvaluator Connected(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.ConnectedWith(values));
        return evaluator;
    }

    [Fact]
    public void Packs_off_is_left_alone_off_the_ground()
    {
        var step = Of("PF_PACKS_OFF");
        var line = Line("PF_PACKS_OFF");
        Assert.Equal(PacksText, step.LeaveAloneText);
        Assert.Equal(PacksText, line.LeaveAloneText);

        foreach (var when in new[] { step.LeaveAloneWhen!, line.LeaveAloneWhen! })
        {
            Assert.True(when(Connected(Engines(false).Append(Ground(false)).ToArray())), "airborne");
            Assert.True(when(Connected(Engines(false))), "the ground flag unread");
            Assert.True(when(Connected()), "nothing read");
            Assert.False(when(Connected(Engines(false).Append(Ground(true)).ToArray())), "parked");
            Assert.False(when(Connected(Engines(true).Append(Ground(true)).ToArray())), "on the ground, engines running");
        }
    }

    [Fact]
    public void Packs_off_tick_through_the_checklist_manager_is_refused_aloud_airborne()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected(Engines(false).Append(Ground(false)).ToArray()), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "PF_PACKS_OFF");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(PacksText, spoken);
        Assert.False(line.IsChecked);
    }

    /// <summary>Only the packs step is left alone in the flow: every other cut sits behind the two guards.</summary>
    [Fact]
    public void OnlyThePacksStep_IsLeftAloneInTheFlow()
        => Assert.Equal(new[] { "PF_PACKS_OFF" }, Preflight.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values) => Planned(39000, values);

    private static readonly string[] Ties = { "L1011_RO_AC_TIE_1", "L1011_RO_AC_TIE_2", "L1011_RO_AC_TIE_3" };

    /// <summary>What the aircraft reads when a write's work is done: the APU turning (RPM 1) after its start;
    /// the generator's field and breaker relays closed; the three ties closed; the selected cabin altitude at
    /// the plan's cruise cabin altitude, in metres; otherwise the step's keys at their targets.</summary>
    private static (string Key, double Value)[] Done(Step s)
    {
        if (s.EventName == X.ApuStart) return new[] { ("L1011_RO_APU_RPM", 1.0) };
        if (s.EventName == X.ApuGenerator) return new[] { ("L1011_RO_APU_GEN_FIELD", 1.0), ("L1011_RO_APU_GEN_BREAKER", 1.0) };
        if (s.EventName == X.AcTies) return Ties.Select(t => (t, 1.0)).ToArray();
        if (s.EventName == X.CabinAltitudeSelect)
            return new[] { (CabinAltReadout, L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(39000) * L1011FoStateEvaluator.MetresPerFoot) };
        return Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
    }

    /// <summary>A reading the other way: the speed brake handle well aft (1 % still reads forward).</summary>
    private static double Wrong(string key, double done) => key == L1011Levers.SpeedBrakeKey ? 100 : done == 0 ? 1 : 0;

    [Fact]
    public void EveryWriteWithASkipCondition_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in Preflight.Steps.Where(x => IsSwitch(x) && x.SkipCondition != null))
        {
            n++;
            var done = Done(s);
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, Wrong(wrong[i].Key, wrong[i].Value));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length - Tests.Length, n);
    }

    /// <summary>The APU start is already set once it turns (a press would be a second start), not only once it runs.</summary>
    [Fact]
    public void TheApuStart_IsAlreadySetFromTheFirstTurn()
    {
        var s = Of("PF_APU_START").SkipCondition!;
        Assert.False(s(State(("L1011_RO_APU_RPM", 0))));
        Assert.True(s(State(("L1011_RO_APU_RPM", 1))));
        Assert.True(s(State(("L1011_RO_APU_RPM", 100))));
    }

    /// <summary>Ignition A or B is already set: the step turns the selector only off both systems.</summary>
    [Fact]
    public void TheIgnition_IsAlreadySetOnSystemAOrB()
    {
        var s = Of("PF_IGNITION").SkipCondition!;
        Assert.True(s(State(("ROTARY_ENG_START_SEL", 0))));
        Assert.True(s(State(("ROTARY_ENG_START_SEL", 2))));
        Assert.False(s(State(("ROTARY_ENG_START_SEL", 1))));
    }

    // ------------------------------------------------------------------ what Preflight writes

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
        => Preflight.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.True(EveryWrittenKey().Count() > 150, $"only {EveryWrittenKey().Count()} writes enumerated");
    }

    [Fact]
    public void TheCompositesArePseudoKeys_AndOnlyTwoLeversAreWritten()
    {
        Assert.Equal(new[] { X.ApuStart, X.ApuGenerator, X.AcTies, X.FireTest, X.WheelWellFireTest, X.HydraulicCheck, X.CabinAltitudeSelect }
                .OrderBy(k => k, StringComparer.Ordinal),
            EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct().OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(new[] { L1011Levers.ParkingBrakeKey, L1011Levers.SpeedBrakeKey }.OrderBy(k => k, StringComparer.Ordinal),
            EveryWrittenKey().Select(w => w.Key).Where(L1011Levers.IsLever).Distinct().OrderBy(k => k, StringComparer.Ordinal));
    }

    /// <summary>Every other key is a plain switch row, and its target one of the control's positions.</summary>
    [Fact]
    public void EveryOtherKey_IsAPlainSwitchRow_AtOneOfItsPositions()
    {
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !L1011Levers.IsLever(w.Key)))
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target!.Value, current: target!.Value == 0 ? 1 : 0);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused ({plan.Refusal})");
        }
    }

    /// <summary>The keys whose click replay carries a TOGGLE K: event, refused from an unknown position ([L1011-4]).</summary>
    private static readonly string[] ToggleCarrying = { "SWITCH_APU_BLEED", "SWITCH_ANTISKID_ON", "SWITCH_SMOKING_LIGHT" };

    /// <summary>
    /// A toggle-carrying key is written only by a single-switch step whose "Already set" reads that key at the
    /// target: the executor reads it first, and from an unknown position the definition refuses the write, which
    /// the flow speaks as "Skipping". Planned from nothing read, every other key must still be a plan.
    /// </summary>
    [Fact]
    public void No_step_writes_a_toggle_carrying_key_from_an_unread_position_except_through_a_skip()
    {
        var toggles = new List<string>();
        foreach (var (step, key, target) in EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !L1011Levers.IsLever(w.Key)))
        {
            var plan = L1011WritePlan.ForSet(Rows.Value[key].Control!, target!.Value, current: null);
            Assert.NotEqual(L1011WritePlan.NotAPositionRefusal, plan.Refusal);
            if (plan.Refusal is null) continue;
            Assert.Equal(L1011WritePlan.UnknownPositionRefusal, plan.Refusal);
            toggles.Add(key);

            var s = Of(step);
            Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
            Assert.NotNull(s.SkipCondition);
            int other = target.Value == 0 ? 1 : 0;
            Assert.True(s.SkipCondition!(State((key, target.Value))), $"{step}: {key} at its target must be already set");
            Assert.False(s.SkipCondition!(State((key, other))), $"{step}: {key} the other way must not be already set");
            Assert.False(s.SkipCondition!(State()), $"{step}: {key} unread must not be already set");
        }
        Assert.Equal(ToggleCarrying.OrderBy(k => k, StringComparer.Ordinal), toggles.OrderBy(k => k, StringComparer.Ordinal));
    }

    // ================================================================== the checklists

    [Fact]
    public void ThePreflightGroups_FollowPowerUp_InFlightOrder_Named()
    {
        Assert.Equal(new[] { "PREFLIGHT", "PREFLIGHT_FE_CL", "PREFLIGHT_OVHD_CL", "PREFLIGHT_MAIN_CL", "PREFLIGHT_CONSOLE_CL" },
            Groups.Select(g => g.Id).SkipWhile(id => id.StartsWith("POWER_UP", StringComparison.Ordinal)).Take(5));
        Assert.Equal("Preflight", Group("PREFLIGHT").Name);
        Assert.Equal("Flight Engineer Station Preparation", Group("PREFLIGHT_FE_CL").Name);
        Assert.Equal("Overhead Preparation", Group("PREFLIGHT_OVHD_CL").Name);
        Assert.Equal("Main Panel Preparation", Group("PREFLIGHT_MAIN_CL").Name);
        Assert.Equal("Center Console Preparation", Group("PREFLIGHT_CONSOLE_CL").Name);
    }

    [Fact]
    public void EveryPreflightGroup_IsDeparture_AndNeverArrival()
    {
        foreach (var id in Preflight.RelatedChecklistGroupIds.Append("CLIMB_CL"))
        {
            Assert.True(FoDepartureGroups.Departure.Contains(id), $"{id} is not a departure group");
            Assert.False(FoDepartureGroups.Arrival.Contains(id), $"{id} is an arrival group too");
        }
    }

    [Fact]
    public void No_read_back_has_a_check_action()
    {
        foreach (var g in Preflight.RelatedChecklistGroupIds.Skip(1))
            Assert.All(Group(g).Items, i => Assert.True(i.CheckAction == null, $"{g}/{i.Id} has a CheckAction"));
    }

    private static readonly string[] ActionLineIds =
    {
        "PF_APU_MASTER", "PF_APU_MODE_MIN", "PF_APU_FUEL", "PF_APU_START", "PF_APU_GEN", "PF_AC_TIES", "PF_APU_AIR",
        "PF_PACKS_OFF", "PF_PACKS_AUTO", "PF_HOT_AIR_OFF", "PF_ZONE_TRIMS", "PF_APU_BLEED", "PF_PACKS_ON", "PF_APU_MODE_NORM",
        "PF_HOT_AIR_ON", "PF_FIRE_TEST", "PF_WHEEL_WELL_TEST", "PF_HYD_ENGINE_PUMPS", "PF_AC_PUMPS_PTUS_OFF", "PF_HYD_CHECK",
        "PF_AC_METER", "PF_DC_METER", "PF_TRANSFERS_OFF", "PF_CROSSFEEDS", "PF_TANK_VALVES", "PF_FUEL_CTRL",
        "PF_MOVIE", "PF_SLAT_MONITOR", "PF_HI_PRESS", "PF_ISLN_VALVES", "PF_ECS", "PF_PRESS_MODE", "PF_CABIN_ALT",
        "PF_COMPASSES", "PF_WIPERS", "PF_ANTI_ICE", "PF_WINDSHIELD", "PF_SIDE_WINDOWS", "PF_AIR_DATA", "PF_MACH_FEEL",
        "PF_IGNITION", "PF_STBY_LIGHTS", "PF_PFCS", "PF_FCES", "PF_BRAKE_SEL", "PF_ANTISKID_OFF", "PF_PARKING_BRAKE",
        "PF_AUTOBRAKE", "PF_EXT_LIGHTS", "PF_EMER_LIGHTS", "PF_NO_SMOKING", "PF_INSTR_SOURCES", "PF_DME",
        "PF_FLIGHT_DIRECTORS", "PF_PADDLES_OFF", "PF_SPEED_BRAKE", "PF_XPDR",
        "PF_CPT_INS_ROUTE", "PF_CPT_PMS", "PF_CPT_NAV", "PF_CPT_ALTIMETERS", "PF_CPT_AFCS", "PF_CPT_TAKEOFF_DATA",
        "PF_CPT_RADIOS",
    };

    /// <summary>The flow's writes and its unconditional Captain steps, in the flow's order; the cabin altitude
    /// prompt, spoken only when the aircraft does not show the cruise setting, stays flow-only.</summary>
    [Fact]
    public void Preflight_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("PREFLIGHT").Items.Select(i => i.Id));
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == "PF_CABIN_ALT_CPT");
    }

    [Fact]
    public void Preflight_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("PREFLIGHT").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void Preflight_LineKinds()
    {
        foreach (var i in Group("PREFLIGHT").Items)
        {
            if (Tests.Contains(i.Id) || UndoneLater.Contains(i.Id))
            {
                // A test, or a position the flow later undoes: a manual line that runs its step's write, judged
                // by no state, ticked when its step completes.
                Assert.Equal(ChecklistItemType.Actionable, i.Type);
                Assert.Null(i.StateFieldName);
                Assert.NotNull(i.CheckAction);
                Assert.True(i.ManualCompletionAllowed);
            }
            else if (i.Id.StartsWith("PF_CPT_", StringComparison.Ordinal))
            {
                Assert.Equal(ChecklistItemType.CaptainReminder, i.Type);
                Assert.Null(i.CheckAction);
                Assert.Equal(Of(i.Id).ReminderText, i.ReminderText);
            }
            else
            {
                Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
                Assert.NotNull(i.CheckAction);
                Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
                Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            }
        }
    }

    /// <summary>Each Auto line reads the field its step's "Already set" reads: a single switch its key, a multi
    /// switch its synthetic, a composite the state it delivers.</summary>
    [Theory]
    [InlineData("PF_APU_MASTER", "TOGGLE_APU_POWER", 1)]
    [InlineData("PF_APU_FUEL", "FO_ENG2_FUEL_FEED", 1)]
    [InlineData("PF_APU_START", "FO_APU_STARTING_OR_RUNNING", 1)]
    [InlineData("PF_APU_GEN", "FO_APU_GEN_CLOSED", 1)]
    [InlineData("PF_AC_TIES", "FO_AC_TIES_CLOSED", 1)]
    [InlineData("PF_APU_AIR", "FO_APU_AIR_ON", 1)]
    [InlineData("PF_PACKS_AUTO", "FO_PACKS_AUTO", 1)]
    [InlineData("PF_ZONE_TRIMS", "FO_ZONE_TRIMS_ON", 1)]
    [InlineData("PF_APU_BLEED", "SWITCH_APU_BLEED", 1)]
    [InlineData("PF_PACKS_ON", "FO_PACKS_ON", 1)]
    [InlineData("PF_APU_MODE_NORM", "SWITCH_APU_MODE", 1)]
    [InlineData("PF_HOT_AIR_ON", "FO_HOT_AIR_ON", 1)]
    [InlineData("PF_HYD_ENGINE_PUMPS", "FO_HYD_ENGINE_PUMPS_ON", 1)]
    [InlineData("PF_AC_PUMPS_PTUS_OFF", "FO_AC_PUMPS_OFF", 1)]
    [InlineData("PF_AC_METER", "ROTARY_PWR_SOURCE", 4)]
    [InlineData("PF_DC_METER", "ROTARY_STBY_DC_BUS", 4)]
    [InlineData("PF_TRANSFERS_OFF", "FO_TRANSFERS_OFF", 1)]
    [InlineData("PF_CROSSFEEDS", "FO_CROSSFEEDS_CLOSED", 1)]
    [InlineData("PF_TANK_VALVES", "FO_TANK_VALVES_ON", 1)]
    [InlineData("PF_FUEL_CTRL", "FO_FUEL_CTRL_OVRD_OFF", 1)]
    [InlineData("PF_MOVIE", "FO_MOVIE_OFF", 1)]
    [InlineData("PF_SLAT_MONITOR", "SWITCH_SLAT_MONITOR", 0)]
    [InlineData("PF_HI_PRESS", "FO_HI_PRESS_ON", 1)]
    [InlineData("PF_ISLN_VALVES", "FO_ISLN_VALVES_ON", 1)]
    [InlineData("PF_ECS", "FO_ECS_ON", 1)]
    [InlineData("PF_PRESS_MODE", "ROTARY_CAB_PRESS_MODE", 1)]
    [InlineData("PF_CABIN_ALT", "FO_CABIN_ALT_AT_CRUISE", 1)]
    [InlineData("PF_COMPASSES", "FO_COMPASSES_MAG", 1)]
    [InlineData("PF_WIPERS", "FO_WIPERS_OFF", 1)]
    [InlineData("PF_ANTI_ICE", "FO_ANTI_ICE_OFF", 1)]
    [InlineData("PF_WINDSHIELD", "FO_WINDSHIELD_NORMAL", 1)]
    [InlineData("PF_SIDE_WINDOWS", "FO_SIDE_WINDOWS_ON", 1)]
    [InlineData("PF_AIR_DATA", "FO_AIR_DATA_HEAT_OFF", 1)]
    [InlineData("PF_MACH_FEEL", "FO_MACH_FEEL_RUDDER_ON", 1)]
    [InlineData("PF_IGNITION", "FO_IGNITION_SELECTED", 1)]
    [InlineData("PF_STBY_LIGHTS", "TOGGLE_INSTR_STBY_LIGHTS", 1)]
    [InlineData("PF_PFCS", "FO_PFCS_ON", 1)]
    [InlineData("PF_FCES", "FO_FCES_ON", 1)]
    [InlineData("PF_BRAKE_SEL", "TOGGLE_BRAKE_SYS_SEL", 0)]
    [InlineData("PF_ANTISKID_OFF", "SWITCH_ANTISKID_ON", 0)]
    [InlineData("PF_PARKING_BRAKE", "L1011_PARKING_BRAKE", 1)]
    [InlineData("PF_AUTOBRAKE", "ROTARY_AUTO_BRAKE_MODE", 1)]
    [InlineData("PF_EXT_LIGHTS", "FO_EXT_LIGHTS_PREFLIGHT", 1)]
    [InlineData("PF_EMER_LIGHTS", "FO_EMER_LIGHTS_ARMED", 1)]
    [InlineData("PF_NO_SMOKING", "SWITCH_SMOKING_LIGHT", 1)]
    [InlineData("PF_INSTR_SOURCES", "FO_INSTR_SOURCES_NORMAL", 1)]
    [InlineData("PF_DME", "FO_DME_NORMAL", 1)]
    [InlineData("PF_FLIGHT_DIRECTORS", "FO_FLIGHT_DIRECTORS_ON", 1)]
    [InlineData("PF_PADDLES_OFF", "FO_PADDLES_OFF", 1)]
    [InlineData("PF_SPEED_BRAKE", "FO_SPEED_BRAKE_FORWARD", 1)]
    [InlineData("PF_XPDR", "ROTARY_TCAS_MODE", 1)]
    public void Preflight_ActionLine_ReadsItsStepsField(string id, string field, double done)
    {
        var line = Line(id);
        Assert.Equal(field, line.StateFieldName);
        Assert.True(line.StateCondition!(done), $"{id}: {field} at {done} must read done");
        Assert.False(line.StateCondition!(double.NaN), $"{id}: {field} unread must not read done");
        var others = L1011FoStateEvaluator.SyntheticKeys.Contains(field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
        foreach (double other in others.Where(v => v != done))
            Assert.False(line.StateCondition!(other), $"{id}: {field} at {other} must not read done");
        if (id == "PF_AC_PUMPS_PTUS_OFF")
            Assert.Equal(new[] { "FO_PTUS_OFF" }, line.AdditionalStateFields);
        else
            Assert.Empty(line.AdditionalStateFields);
    }

    /// <summary>The line reads both halves of its step's "Already set": the AC pumps and the PTUs.</summary>
    [Fact]
    public void TheAcPumpsAndPtusLine_IsDoneOnlyWithBothOff()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var off = new (string, double)[] { ("SWITCH_HYD_AC_PUMP_B3", 0), ("SWITCH_HYD_AC_PUMP_C3", 0), ("SWITCH_PTU_AB", 0), ("SWITCH_PTU_CD", 0) };
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected(off), new X(), groups);
        mgr.EvaluateAutoDetection();
        Assert.True(groups.SelectMany(g => g.Items).Single(i => i.Id == "PF_AC_PUMPS_PTUS_OFF").IsChecked);

        groups = L1011FoChecklistDefinitions.Build();
        var ptuOn = off.Select(v => v.Item1 == "SWITCH_PTU_CD" ? (v.Item1, 1.0) : v).ToArray();
        mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected(ptuOn), new X(), groups);
        mgr.EvaluateAutoDetection();
        Assert.False(groups.SelectMany(g => g.Items).Single(i => i.Id == "PF_AC_PUMPS_PTUS_OFF").IsChecked);
    }

    // ---- the positions the flow later undoes -------------------------------------------------------------------

    /// <summary>Set for a while and undone later in the same flow: the packs off (later on), the hot air off (later on),
    /// the APU compressor mode at minimum (later normal), each paired with the step that undoes it.</summary>
    private static readonly (string Line, string UndoneBy)[] UndoneLaterPairs =
    {
        ("PF_APU_MODE_MIN", "PF_APU_MODE_NORM"), ("PF_PACKS_OFF", "PF_PACKS_ON"), ("PF_HOT_AIR_OFF", "PF_HOT_AIR_ON"),
    };

    private static readonly string[] UndoneLater = UndoneLaterPairs.Select(p => p.Line).ToArray();

    /// <summary>
    /// A state line would untick itself once the flow undoes its position, so a good run would end with these three
    /// unticked: each is a manual line ticked by its own step, and its tick still does its step's write. A later
    /// step of the same flow writes each key the other way.
    /// </summary>
    [Fact]
    public void The_positions_the_flow_later_undoes_are_ticked_by_their_step_not_by_state()
    {
        var ids = Preflight.Steps.Select(s => s.Id).ToList();
        foreach (var (lineId, undoneBy) in UndoneLaterPairs)
        {
            var line = Line(lineId);
            Assert.Equal(ChecklistItemType.Actionable, line.Type);
            Assert.Null(line.StateFieldName);
            Assert.NotNull(line.CheckAction);
            Assert.Equal(lineId, Of(lineId).CompletesChecklistItemId);
            Assert.True(ids.IndexOf(undoneBy) > ids.IndexOf(lineId), $"{undoneBy} must come after {lineId}");
            Assert.Equal(Actions(Of(lineId)).Select(a => (a.Key, 1 - a.Target)), Actions(Of(undoneBy)));
        }
    }

    /// <summary>Through the real checklist manager: ticked by their steps, the three stay ticked over the positions
    /// the flow left at its end.</summary>
    [Fact]
    public void The_positions_the_flow_later_undoes_stay_ticked_at_the_end_of_the_flow()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var endOfFlow = UndoneLaterPairs.SelectMany(p => Actions(Of(p.UndoneBy))).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(Connected(endOfFlow), new X(), groups);
        foreach (var id in UndoneLater)
            mgr.MarkComplete(id);

        mgr.EvaluateAutoDetection();

        Assert.All(UndoneLater, id => Assert.True(groups.SelectMany(g => g.Items).Single(i => i.Id == id).IsChecked, id));
    }

    // ---- the drain mast heaters ------------------------------------------------------------------------------------

    /// <summary>The aircraft puts both drain mast heater switches back to 0 within seconds, by hand too, so no step
    /// could ever be done: Preflight neither writes them nor holds a line for them.</summary>
    [Fact]
    public void The_drain_mast_heaters_are_left_to_the_aircraft()
    {
        Assert.DoesNotContain(EveryWrittenKey(), w => w.Key.StartsWith("SWITCH_WASTE_WATER", StringComparison.Ordinal));
        Assert.DoesNotContain(Preflight.Steps, s => s.Id.Contains("DRAIN_MAST", StringComparison.Ordinal));
        Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id.Contains("DRAIN_MAST", StringComparison.Ordinal));
        Assert.DoesNotContain("FO_DRAIN_MAST_HEAT_ON", L1011FoStateEvaluator.SyntheticKeys);
    }

    // ---- the hand ticks left alone ([FO-25]) -------------------------------------------------------------------

    private const string ParkedOnlyText = "Left alone: the aircraft must be on the ground with the engines stopped.";

    /// <summary>The three tests light fire warnings and run hydraulic pumps, and the AC pumps and PTUs line cuts
    /// hydraulic power: parked only, as Power Up's pump lines. The packs line: on the ground only.</summary>
    [Fact]
    public void ThePreflightTicksLeftAlone_AreTheTestsTheHydraulicCutAndThePacks()
    {
        Assert.Equal(new[] { "PF_PACKS_OFF", "PF_FIRE_TEST", "PF_WHEEL_WELL_TEST", "PF_AC_PUMPS_PTUS_OFF", "PF_HYD_CHECK" },
            Group("PREFLIGHT").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
        foreach (var id in new[] { "PF_FIRE_TEST", "PF_WHEEL_WELL_TEST", "PF_AC_PUMPS_PTUS_OFF", "PF_HYD_CHECK" })
        {
            var when = Line(id).LeaveAloneWhen!;
            Assert.Equal(ParkedOnlyText, Line(id).LeaveAloneText);
            Assert.True(when(Connected(Engines(false).Append(Ground(false)).ToArray())), $"{id}: airborne");
            Assert.True(when(Connected(Engines(true).Append(Ground(true)).ToArray())), $"{id}: engines running");
            Assert.True(when(Connected(Ground(true))), $"{id}: engines unread");
            Assert.True(when(Connected()), $"{id}: nothing read");
            Assert.False(when(Connected(Engines(false).Append(Ground(true)).ToArray())), $"{id}: parked");
        }
    }

    // ---- the cabin altitude line ([FO-20]) ----------------------------------------------------------------------

    /// <summary>The tick writes the plan's cruise cabin altitude, as its step does; with no plan it writes nothing.</summary>
    [Fact]
    public async Task TheCabinAltitudeTick_WritesThePlansValue_OrNothingWithoutAPlan()
    {
        int cabinFt = L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(39000);

        var planned = new FakeL1011Aircraft();
        var exec = new X();
        exec.SetTransport(planned);
        await Line("PF_CABIN_ALT").CheckAction!(exec, Planned(39000)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { (double)L1011Pressurization.KnobStep(cabinFt) },
            planned.Io.Where(e => e.Kind == FakeL1011IoKind.CabinAltitudeStep).Select(e => e.Value!.Value));

        var noPlan = new FakeL1011Aircraft();
        exec = new X();
        exec.SetTransport(noPlan);
        await Line("PF_CABIN_ALT").CheckAction!(exec, Planned(null)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.DoesNotContain(noPlan.Io, e => e.Kind != FakeL1011IoKind.FreshRead);
    }

    // ---- a hand tick does what the flow step does ----------------------------------------------------------------

    /// <summary>
    /// An aircraft set up so the step has work to do: each key the step writes the other way from its target,
    /// and for a composite what it needs to act (the APU master on and the APU stopped; the APU running with its
    /// generator off line; ground power on line with the ties open; the test switches and loop selectors off).
    /// </summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, Wrong(key, target!.Value));
        switch (step.EventName)
        {
            case X.ApuStart:
                ac.Set("TOGGLE_APU_POWER", 1);
                ac.Set("L1011_RO_APU_RPM", 0);
                break;
            case X.ApuGenerator:
                ac.Set("L1011_RO_APU_RPM", 100);
                ac.Set("L1011_RO_APU_GEN_FIELD", 0);
                ac.Set("L1011_RO_APU_GEN_BREAKER", 0);
                break;
            case X.AcTies:
                ac.Set("L1011_RO_EXT_PWR_BREAKER", 1);
                foreach (var tie in Ties) ac.Set(tie, 0);
                break;
            case X.FireTest:
                foreach (var key in new[] { "SWITCH_FIRE_DETECT_TEST_A", "SWITCH_FIRE_DETECT_TEST_B", "ROTARY_FIRE_DETECT_TEST_ENG_1",
                             "ROTARY_FIRE_DETECT_TEST_ENG_2", "ROTARY_FIRE_DETECT_TEST_ENG_3", "ROTARY_FIRE_DETECT_TEST_APU" })
                    ac.Set(key, 0);
                break;
            case X.WheelWellFireTest:
                foreach (var key in new[] { "SWITCH_WHEEL_WELL_TEST_A", "SWITCH_WHEEL_WELL_TEST_B", "ROTARY_WHEEL_WELL_TEST" })
                    ac.Set(key, 0);
                break;
            case X.HydraulicCheck:
                foreach (var key in new[] { "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3", "SWITCH_PTU_AB", "SWITCH_PTU_CD" })
                    ac.Set(key, 0);
                break;
        }
        return ac;
    }

    public static IEnumerable<object[]> PreflightLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "PREFLIGHT").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft;
    /// the cabin altitude's step with the target the flow resolves from the same plan.</summary>
    [Theory]
    [MemberData(nameof(PreflightLinesWithACheckAction))]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var plan = Planned(39000);
        var step = L1011FoFlowDefinitions.Build().Single(f => f.Id == "PREFLIGHT").Steps.Single(s => s.Id == id);
        if (step.TargetValueProvider is { } provider)
            step.TargetValue = provider(plan);

        var byStep = AircraftFor(step);
        var stepExec = new X();
        stepExec.SetTransport(byStep);
        await stepExec.ExecuteStepAsync(step).WaitAsync(TimeSpan.FromSeconds(10));

        var byTick = AircraftFor(step);
        var tickExec = new X();
        tickExec.SetTransport(byTick);
        await Line(id).CheckAction!(tickExec, plan).WaitAsync(TimeSpan.FromSeconds(10));

        static List<(FakeL1011IoKind, string, double?)> Sent(FakeL1011Aircraft ac) =>
            ac.Io.Where(e => e.Kind != FakeL1011IoKind.FreshRead).Select(e => (e.Kind, e.Key, e.Value)).ToList();
        Assert.NotEmpty(Sent(byStep));
        Assert.Equal(Sent(byStep), Sent(byTick));
    }

    // ---- the read-backs, word for word ---------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value, or null for a line judged by no state
    /// (<c>Manual</c>, delivered by the composite) or a Captain's <c>Reminder</c>.</summary>
    private sealed record ReadBack(string Id, string Label, string? Field = null, double Done = 1, bool Reminder = false);

    private static readonly ReadBack[] EngineerStation =
    {
        new("PFC_APU_MASTER", "APU master power switch: on", "TOGGLE_APU_POWER"),
        new("PFC_APU_FUEL", "One tank boost pump and engine 2 tank valve switch: on", "FO_ENG2_FUEL_FEED"),
        new("PFC_APU_START", "APU start switch: press and release", "FO_APU_RUNNING"),
        new("PFC_APU_GEN", "APU generator field and breaker switches: latch", "FO_APU_GEN_CLOSED"),
        new("PFC_AC_TIES", "Bus tie breakers: close", "FO_AC_TIES_CLOSED"),
        new("PFC_FUSELAGE_ISLN", "Fuselage isolation valve switch: latch", "SWITCH_APU_ISLN_VALVE"),
        new("PFC_XBLEEDS", "Crossbleed valve switches: latch", "FO_CROSSBLEEDS_ON"),
        new("PFC_PACK_AUTO", "Pack auto/manual control switches: auto", "FO_PACKS_AUTO"),
        new("PFC_ZONE_TRIMS", "Zone trim switches: latched", "FO_ZONE_TRIMS_ON"),
        new("PFC_APU_BLEED", "APU bleed air shut off switch: latch in", "SWITCH_APU_BLEED"),
        new("PFC_PACKS", "Pack flow control valve switches: latch in", "FO_PACKS_ON"),
        new("PFC_APU_MODE_NORM", "APU mode selector: norm", "SWITCH_APU_MODE"),
        new("PFC_HOT_AIR", "Hot air valve and hot air isolation valve switches: latch in", "FO_HOT_AIR_ON"),
        new("PFC_LOOP_SELECTORS", "Detection loop selectors: both", "FO_FIRE_LOOPS_BOTH"),
        new("PFC_FIRE_TEST", "Engine and APU fire test: complete"),
        new("PFC_WHEEL_WELL_TEST", "Wheel well fire panel check: complete"),
        new("PFC_HYD_ENGINE_PUMPS", "Engine pump shut off switches: latched", "FO_HYD_ENGINE_PUMPS_ON"),
        new("PFC_ATMS", "ATM switches: off", "FO_ATMS_OFF"),
        new("PFC_PTUS", "PTU switches: unlatched", "FO_PTUS_OFF"),
        new("PFC_HYD_CHECK", "Hydraulic panel check: complete"),
        new("PFC_AC_METER", "AC meter selector: APU", "ROTARY_PWR_SOURCE", 4),
        new("PFC_DC_METER", "DC meter: bat", "ROTARY_STBY_DC_BUS", 4),
        new("PFC_AC_ESS", "AC ESS power selector: norm b3 (g1)", "ROTARY_AC_ESS_BUS", 4),
        new("PFC_TRANSFERS", "1A and 3A transfer switches: unlatched", "FO_TRANSFERS_OFF"),
        new("PFC_CROSSFEEDS", "Crossfeed switches: close", "FO_CROSSFEEDS_CLOSED"),
        new("PFC_TANK_VALVES", "All engine tank valve switches: latched", "FO_TANK_VALVES_ON"),
        new("PFC_APU_PUMP_ONLY", "Tank pump switches: one on for the APU", "FO_ONLY_APU_PUMP_ON"),
        new("PFC_JETTISON", "Master and tank jettison switches: unlatched", "FO_JETTISON_SAFE"),
        new("PFC_FUEL_CTRL", "Fuel control amplifier switches: unlatched", "FO_FUEL_CTRL_OVRD_OFF"),
        new("PFC_MOVIE", "Movie interrupt switch: unlatched", "FO_MOVIE_OFF"),
        new("PFC_SLAT_MONITOR", "Slat monitor switch: unlatch", "SWITCH_SLAT_MONITOR", 0),
        new("PFC_HI_PRESS", "Hi pressure bleed switches: latched", "FO_HI_PRESS_ON"),
        new("PFC_ISLN_VALVES", "Engine isolation valve switches: latched", "FO_ISLN_VALVES_ON"),
        new("PFC_ECS", "Cool air overboard and cargo heat switches: latched", "FO_ECS_ON"),
        new("PFC_PRESS_MODE", "Mode sel: norm", "ROTARY_CAB_PRESS_MODE"),
        new("PFC_CABIN_ALT", "Alt sel control: set cruise altitude", "FO_CABIN_ALT_AT_CRUISE"),
    };

    private static readonly ReadBack[] Overhead =
    {
        new("PFO_INS", "INS 1, 2 and 3: check alignment", "FO_INS_ALIGN_OR_NAV"),
        new("PFO_FIRE_HANDLES", "Fire pull handles: in", "FO_FIRE_HANDLES_IN"),
        new("PFO_COMPASSES", "Compass 1 and 2: mag", "FO_COMPASSES_MAG"),
        new("PFO_RADIO_MASTERS", "Master radio switches: on", "FO_RADIO_MASTERS_ON"),
        new("PFO_WIPERS", "Captain and F/O wipers: off", "FO_WIPERS_OFF"),
        new("PFO_ANTI_ICE", "Engine and wing anti-ice switches: unlatched", "FO_ANTI_ICE_OFF"),
        new("PFO_WINDSHIELD", "Capt and F/O windshield heat switches: latched", "FO_WINDSHIELD_NORMAL"),
        new("PFO_SIDE_WINDOWS", "Side window switches: latch in", "FO_SIDE_WINDOWS_ON"),
        new("PFO_AIR_DATA", "Air data sensor heat switches: all off", "FO_AIR_DATA_HEAT_OFF"),
        new("PFO_MACH_FEEL", "Mach feel switches and rudder travel limiter: latched", "FO_MACH_FEEL_RUDDER_ON"),
        new("PFO_START_LIGHTS", "Engine start lights: all out", "FO_START_LIGHTS_OUT"),
        new("PFO_IGNITION", "Ignition: A or B", "FO_IGNITION_SELECTED"),
        new("PFO_STBY_LIGHTS", "Instrument standby lights: brt", "TOGGLE_INSTR_STBY_LIGHTS"),
        new("PFO_PFCS", "All PFCS switches: latched", "FO_PFCS_ON"),
        new("PFO_FCES", "All FCES switches: latched", "FO_FCES_ON"),
        new("PFO_BRAKE_SEL", "Brake selector switch: norm sys B", "TOGGLE_BRAKE_SYS_SEL", 0),
        new("PFO_ANTISKID", "Anti-skid switch: unlatch", "SWITCH_ANTISKID_ON", 0),
        new("PFO_PARKING_BRAKE", "Parking brake: set", "L1011_PARKING_BRAKE"),
        new("PFO_AUTOBRAKE", "Auto brake: off", "ROTARY_AUTO_BRAKE_MODE"),
        new("PFO_EXT_LIGHTS", "Exterior lights: logo and position on, the rest off", "FO_EXT_LIGHTS_PREFLIGHT"),
        new("PFO_EMER_LIGHTS", "Emergency lighting switch: arm", "FO_EMER_LIGHTS_ARMED"),
        new("PFO_STBY_POWER", "Standby power: armed", "FO_STBY_ARMED"),
        new("PFO_NO_SMOKING", "No smoking: on", "SWITCH_SMOKING_LIGHT"),
    };

    private static readonly ReadBack[] MainPanel =
    {
        new("PFM_NAV", "Nav 1 and Nav 2: set", Reminder: true),
        new("PFM_DME", "Nav 1 and Nav 2 DME: norm", "FO_DME_NORMAL"),
        new("PFM_AUTOPILOT", "Autopilot: IAS, heading, courses and altitude set", Reminder: true),
        new("PFM_FLIGHT_DIRECTORS", "Flight directors: on", "FO_FLIGHT_DIRECTORS_ON"),
        new("PFM_AUTOPILOT_MASTERS", "Autopilot masters: off", "FO_PADDLES_OFF"),
        new("PFM_ALTIMETERS", "Altimeters: set QNH", Reminder: true),
        new("PFM_INSTR_SOURCE", "Instrument source selector: normal", "FO_INSTR_SOURCES_NORMAL"),
        new("PFM_GEAR", "Landing gear: down, 3 green", L1011GearConfirmation.DownField),
        new("PFM_BRAKE_SYS", "Brake system: norm sys B", "TOGGLE_BRAKE_SYS_SEL", 0),
    };

    private static readonly ReadBack[] CentreConsole =
    {
        new("PFX_INS", "INS 1, 2 and 3: set", Reminder: true),
        new("PFX_PMS", "PMS: set", Reminder: true),
        new("PFX_DISCONNECT", "Pitch and roll disconnect handles: normal", "FO_DISCONNECT_HANDLES_STOWED"),
        new("PFX_PARKING_BRAKE", "Parking brake: set", "L1011_PARKING_BRAKE"),
        new("PFX_SPEED_BRAKE", "Speed brake lever: forward", "FO_SPEED_BRAKE_FORWARD"),
        new("PFX_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF"),
        new("PFX_RADIOS", "Communication radios: set", Reminder: true),
        new("PFX_XPDR", "Transponder: set and STBY", "ROTARY_TCAS_MODE"),
    };

    private static IEnumerable<(string Group, ReadBack Line)> AllReadBacks() =>
        EngineerStation.Select(l => ("PREFLIGHT_FE_CL", l))
            .Concat(Overhead.Select(l => ("PREFLIGHT_OVHD_CL", l)))
            .Concat(MainPanel.Select(l => ("PREFLIGHT_MAIN_CL", l)))
            .Concat(CentreConsole.Select(l => ("PREFLIGHT_CONSOLE_CL", l)));

    [Fact]
    public void The_four_read_backs_are_word_for_word()
    {
        foreach (var (group, lines) in new[]
        {
            ("PREFLIGHT_FE_CL", EngineerStation), ("PREFLIGHT_OVHD_CL", Overhead),
            ("PREFLIGHT_MAIN_CL", MainPanel), ("PREFLIGHT_CONSOLE_CL", CentreConsole),
        })
            Assert.Equal(lines.Select(l => (l.Id, l.Label)), Group(group).Items.Select(i => (i.Id, i.Label)));
    }

    /// <summary>Each read line reads its field at its value, and nothing else; a test's line is judged by no
    /// state; a Captain's line is his reminder.</summary>
    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var (group, rb) in AllReadBacks())
        {
            var line = Group(group).Items.Single(i => i.Id == rb.Id);
            Assert.Null(line.CheckAction);
            Assert.Empty(line.AdditionalStateFields);
            if (rb.Reminder)
            {
                Assert.Equal(ChecklistItemType.CaptainReminder, line.Type);
                Assert.Equal(rb.Label, line.ReminderText);
                continue;
            }
            if (rb.Field is null)
            {
                Assert.Equal(ChecklistItemType.Actionable, line.Type);
                Assert.Null(line.StateFieldName);
                Assert.True(line.ManualCompletionAllowed);
                continue;
            }
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

    /// <summary>A finished run latches the action group and the four read-backs, and nothing else.</summary>
    [Fact]
    public void Preflight_LatchesItsFiveGroups()
        => Assert.Equal(Preflight.RelatedChecklistGroupIds,
            Preflight.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));
}
