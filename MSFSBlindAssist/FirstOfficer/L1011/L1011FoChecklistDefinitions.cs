using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.L1011;

using Item = Models.ChecklistItem<L1011FoActionExecutor, L1011FoStateEvaluator>;
using Group = Models.ChecklistGroup<L1011FoActionExecutor, L1011FoStateEvaluator>;
using CheckFn = System.Func<L1011FoActionExecutor, L1011FoStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer checklists, the MD-11's two layers: Power Up, Preflight, then
/// Before Start.
///
/// The ACTION group (<c>POWER_UP</c>) lists the Power Up flow's writes and its unconditional Captain step,
/// in the flow's order and with the flow's labels: ticking a line runs the same write as its flow step,
/// through the executor, and a state line ticks itself from the aircraft (<c>RevertToState</c>, [FOB-1]).
/// The conditional ground power prompt stays flow-only, as the MD-11's conditional Captain prompts do.
///
/// Preflight has one action group (<c>PREFLIGHT</c>, the same shape: its conditional cabin altitude prompt
/// stays flow-only) and four read-backs in the aircraft's words: the flight engineer station (opening with
/// the safety inspection's APU start, air conditioning and fire test lines, which Preflight delivers), the
/// overhead, the main panel and the centre console. The three tests and the hydraulic cut are parked-only
/// ticks, as Power Up's pump lines are; the packs line is left alone off the ground.
///
/// The READ-BACK group (<c>POWER_UP_CL</c>, [FO-3]: action-free) is the aircraft's cockpit safety inspection
/// in its own words and order. Each line reads the field and value its delivering flow step checks or skips
/// on: a single switch its own key at the target, a multi-switch line the matching synthetic. The standby
/// power check's line is judged by no state; the flow's composite step delivers it ([FO-7]). The inspection
/// has no galley line, so none is invented: the galley step completes its action line only.
///
/// Four hand ticks are left alone unless the aircraft reads on the ground with every engine stopped
/// ([FO-25]): the ATM switches, the AC hydraulic pumps and the fuel pumps off, and the standby power check,
/// which turns the battery off for a moment. Unread refuses, as the flow's guards do. The fuel pump tick is
/// also left alone while the APU runs or is still spooling up, as its flow step is: the tank 2 left pump
/// feeds the APU from the moment it starts turning.
///
/// Before Start has one action group (<c>BEFORE_START</c>: the flow's writes and the fleet's Before Start tail; its
/// conditional doors, cabin baro and INS prompts stay flow-only) and one read-back, the aircraft's "Before engine
/// start" page up to its first ground start, ending with the fleet's "Start ACARS". The cabin baro and INS lines
/// write their provider's value, or nothing without one, as their steps do. The pack 2 line switches packs 1 and 3
/// off, so its tick is left alone off the ground, as Preflight's packs line is; the external power line is left
/// alone until the APU generator reads on line, as its flow step waits for it.
///
/// Engine Start has one action group (<c>ENGINE_START</c>: per engine, 2, 1 then 3, the previous engine's isolation
/// valve off, its feed, its ground start and its fuel and ignition; then the generators) and one read-back, the
/// aircraft's engine start page in its own words. The three ground start lines are the profile's only
/// <c>StayComplete</c> lines ([FOB-1]): the aircraft releases the switch itself at about 55 % N3, so each latches on
/// its engine running and no flow step links it. Every other line mirrors the aircraft.
///
/// After Start has one action group (<c>AFTER_START</c>: the flow's writes and its three unconditional Captain lines;
/// its conditional flaps prompt stays flow-only) and two read-backs: the aircraft's after start page, ending with the
/// engine start page's APU shutdown lines, which After Start delivers, and its taxi page. The flaps line writes the
/// take-off index (the tablet's, else SimBrief's) or nothing without one, and is left alone off the ground; the
/// transfers line is left alone unless tanks 1A and 3A both hold fuel. The ATMs-on line reads no state: the flow puts
/// them to auto right after. The stabilizer trim line shows the tablet's exported trim as its live value.
/// </summary>
public static class L1011FoChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        PowerUp(), PowerUpCL(),
        Preflight(), PreflightEngineerCL(), PreflightOverheadCL(), PreflightMainPanelCL(), PreflightConsoleCL(),
        BeforeStart(), BeforeStartCL(),
        EngineStart(), EngineStartCL(),
        AfterStart(), AfterStartCL(), TaxiCL(),
    };

    private const string ContIgnition = "SWITCH_CONT_IGNITION";
    private const string Transponder = "ROTARY_TCAS_MODE";
    private const string RadarOff = "SWITCH_WX_OFF";
    private const string AcEssBus = "ROTARY_AC_ESS_BUS";
    private const string Battery = "TOGGLE_BATTERY";

    /// <summary>The AC essential power selector's normal position: bus 3 (generator 1).</summary>
    private const int AcEssNormal = 4;

    private static readonly string[] FuelPumps =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    /// <summary>The master and the four dump switches off, then their five guards closed: the flow's order.</summary>
    private static readonly string[] Jettison =
    {
        "SWITCH_FUEL_DUMP_MASTER", "SWITCH_FUEL_DUMP_2L", "SWITCH_FUEL_DUMP_1", "SWITCH_FUEL_DUMP_3", "SWITCH_FUEL_DUMP_2R",
        "GUARD_FUEL_DUMP_MASTER", "GUARD_FUEL_DUMP_2L", "GUARD_FUEL_DUMP_1", "GUARD_FUEL_DUMP_3", "GUARD_FUEL_DUMP_2R",
    };

    private static bool Is(double v, double position) => Math.Abs(v - position) < 0.5;
    private static bool On(double v) => v > 0.5;

    private static CheckFn Set(string key, int target) => (e, _) => e.Set(key, target);

    private static CheckFn SetAll(params (string Key, int Target)[] writes) => async (e, _) =>
    {
        foreach (var (k, t) in writes) await e.Set(k, t);
    };

    private static (string Key, int Target)[] All(int target, params string[] keys)
        => keys.Select(k => (k, target)).ToArray();

    // ================================================================== action group

    private static Group PowerUp()
    {
        const string G = "POWER_UP";
        return new Group
        {
            Id = G, Name = "Power Up",
            Items = new()
            {
                Auto("PU_CONT_IGN", G, "Continuous ignition: off", ContIgnition, v => Is(v, 0), Set(ContIgnition, 0)),
                Auto("PU_RADIO_MASTERS", G, "Radio master switches: on", "FO_RADIO_MASTERS_ON", On,
                    SetAll(("SWITCH_MASTER_RADIO_ESS", 1), ("SWITCH_MASTER_RADIO_NO2", 1))),
                Auto("PU_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1), Set(Transponder, 1)),
                Auto("PU_RADAR", G, "Weather radar: off", RadarOff, v => Is(v, 1), Set(RadarOff, 1)),
                // Cold-and-dark cuts ([FO-25]): never airborne, never with an engine turning, never unread.
                // The ATMs are the air-turbine hydraulic pumps of systems B and C, left in AUTO through the
                // flight: off there cuts backup hydraulic power, and the fleet's cut sweep cannot see them by
                // their label (Ruling R9).
                LeaveAlone(Auto("PU_ATMS", G, "ATM switches: off", "FO_ATMS_OFF", On,
                    SetAll(("ROTARY_HYD_B2_MODE", 0), ("ROTARY_HYD_C2_MODE", 0))),
                    NotParked, ParkedOnlyText),
                LeaveAlone(Auto("PU_AC_PUMPS", G, "AC hydraulic pumps: off", "FO_AC_PUMPS_OFF", On,
                    SetAll(("SWITCH_HYD_AC_PUMP_B3", 0), ("SWITCH_HYD_AC_PUMP_C3", 0))),
                    NotParked, ParkedOnlyText),
                // Also left alone while the APU runs or is still spooling up: the tank 2 left pump feeds it
                // from the moment it starts turning, and a pilot who started the APU by hand must not have it cut.
                LeaveAlone(Auto("PU_FUEL_PUMPS", G, "Fuel pumps: all off", "FO_FUEL_PUMPS_OFF", On, SetAll(All(0, FuelPumps))),
                    s => NotParked(s) || s.IsOn("FO_APU_STARTING_OR_RUNNING"), ParkedApuOffText),
                Auto("PU_JETTISON", G, "Jettison switches: off and guarded", "FO_JETTISON_SAFE", On, SetAll(All(0, Jettison))),
                Auto("PU_PAX_OXY", G, "Passenger oxygen: off and guarded", "FO_PAX_OXY_SAFE", On,
                    SetAll(("SWITCH_PAX_OXY_MANUAL", 0), ("GUARD_PAX_OXY_MANUAL", 0))),
                Auto("PU_GALLEY", G, "Galley power: off", "FO_GALLEY_OFF", On,
                    SetAll(All(0, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3"))),
                // The composite turns the battery off for a moment, which the fleet's cut sweep cannot see
                // by its label: the same guard as the pumps ([FO-25]). Judged by no state; the check is the point.
                LeaveAlone(ActionManual("PU_STBY_CHECK", G, "Standby power check", Set(X.StandbyPowerCheck, 1)),
                    NotParked, ParkedOnlyText),
                Auto("PU_AC_ESS", G, "AC essential bus selector: bus 3, generator 1", AcEssBus, v => Is(v, AcEssNormal),
                    Set(AcEssBus, AcEssNormal)),
                Auto("PU_EXT_PWR", G, "External power: on", "FO_EXT_POWER_ON", On, Set(X.ExternalPower, 1)),
                // One composite, unit by unit: a unit already at ALIGN or NAV is left alone, never rewritten.
                Auto("PU_INS_ALIGN", G, "INS 1, 2 and 3: align", "FO_INS_ALIGN_OR_NAV", On, Set(X.InsAlign, 1)),
                Reminder("PU_INS_POSITION", G,
                    "Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit."),
            }
        };
    }

    // ================================================================== read-back group (the aircraft's words)

    private static Group PowerUpCL()
    {
        const string G = "POWER_UP_CL";
        return new Group
        {
            Id = G, Name = "Cockpit Safety Inspection",
            Items = new()
            {
                Read("PUC_FIRE_HANDLES", G, "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", On),
                Read("PUC_CONT_IGN", G, "Continuous ignition: unlatched", ContIgnition, v => Is(v, 0)),
                Read("PUC_RADIO_MASTERS", G, "Essential and no. 2 radio master switches: latched", "FO_RADIO_MASTERS_ON", On),
                Read("PUC_GEAR", G, "Gear lever: down", "FO_GEAR_LEVER_DOWN", On),
                Read("PUC_SPEED_BRAKE", G, "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", On),
                Read("PUC_FLAPS", G, "Flaps and flap handle: agree", "FO_FLAPS_AGREE", On),
                Read("PUC_FUEL_IGN", G, "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On),
                Read("PUC_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1)),
                Read("PUC_RADAR", G, "Radar: off", RadarOff, v => Is(v, 1)),
                Read("PUC_ATMS", G, "ATM switches: off", "FO_ATMS_OFF", On),
                Read("PUC_AC_PUMPS", G, "AC hydraulic pump switches: unlatched", "FO_AC_PUMPS_OFF", On),
                Read("PUC_FUEL_PUMPS", G, "Fuel tank pump switches: unlatched", "FO_FUEL_PUMPS_OFF", On),
                Read("PUC_JETTISON", G, "Master jettison and dump switches: unlatched", "FO_JETTISON_SAFE", On),
                Read("PUC_PAX_OXY", G, "Passenger oxygen manual switch: guarded and unlatched", "FO_PAX_OXY_SAFE", On),
                // Judged by no state: the flow's standby power check delivers it, with the two lines after it.
                Manual("PUC_STBY_CHECK", G, "Standby power check: complete"),
                Read("PUC_BATTERY", G, "Battery: on", Battery, v => Is(v, 1)),
                Read("PUC_STBY_ARMED", G, "Standby power: armed", "FO_STBY_ARMED", On),
                Read("PUC_AC_ESS", G, "AC essential power selector: normal, bus 3 (generator 1)", AcEssBus, v => Is(v, AcEssNormal)),
                Read("PUC_INS_ALIGN", G, "All INS: align", "FO_INS_ALIGN_OR_NAV", On),
            }
        };
    }

    // ================================================================== Preflight: the action group

    private const string ApuMaster = "TOGGLE_APU_POWER";
    private const string ApuMode = "SWITCH_APU_MODE";
    private const string ApuBleed = "SWITCH_APU_BLEED";
    private const string FuselageIsolation = "SWITCH_APU_ISLN_VALVE";
    private const string AcMeter = "ROTARY_PWR_SOURCE";
    private const string DcMeter = "ROTARY_STBY_DC_BUS";
    private const string SlatMonitor = "SWITCH_SLAT_MONITOR";
    private const string PressMode = "ROTARY_CAB_PRESS_MODE";
    private const string StandbyLights = "TOGGLE_INSTR_STBY_LIGHTS";
    private const string BrakeSelector = "TOGGLE_BRAKE_SYS_SEL";
    private const string AntiSkid = "SWITCH_ANTISKID_ON";
    private const string Autobrake = "ROTARY_AUTO_BRAKE_MODE";
    private const string NoSmoking = "SWITCH_SMOKING_LIGHT";

    /// <summary>The meters' APU generator and battery positions, the autobrake's OFF (0 is TAKEOFF).</summary>
    private const int AcMeterApu = 4, DcMeterBattery = 4, AutobrakeOff = 1;

    private static readonly string[] PackKeys = { "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3" };

    private static readonly string[] HotAirKeys =
        { "SWITCH_HOT_AIR_ENG_1", "SWITCH_HOT_AIR_ENG_3", "SWITCH_ISLN_VALVE_OVHT_2A", "SWITCH_ISLN_VALVE_OVHT_2B" };

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
        new[] { "NAV", "HDG", "ATT", "ALT", "IAS", "FLT_DIR" }.Select(s => "SWITCH_INSTR_SRC_CPT_" + s)
            .Concat(new[] { "NAV", "HDG", "ATT", "ALT", "IAS", "FLT_DIR" }.Select(s => "SWITCH_INSTR_SRC_FO_" + s))
            .ToArray();

    /// <summary>
    /// The Preflight flow's writes and its seven unconditional Captain steps, in the flow's order and with its
    /// labels: a single switch reads its key at the target (the ignition selector, A or B, as its step's
    /// "Already set" does), a multi-switch line its synthetic, a composite the state it delivers. The three
    /// tests have no state and run every tick; the cabin altitude writes the plan's value, or nothing without one.
    /// The packs off, the hot air off and the APU's minimum compressor mode read no state either: the flow undoes
    /// each later (packs on, hot air on, compressor mode normal), so a state line would untick itself after a good
    /// run. Each is ticked when its step completes, and a hand tick does its step's write.
    /// </summary>
    private static Group Preflight()
    {
        const string G = "PREFLIGHT";
        return new Group
        {
            Id = G, Name = "Preflight",
            Items = new()
            {
                Auto("PF_APU_MASTER", G, "APU master: on", ApuMaster, v => Is(v, 1), Set(ApuMaster, 1)),
                ActionManual("PF_APU_MODE_MIN", G, "APU compressor mode: minimum", Set(ApuMode, 0)),
                Auto("PF_APU_FUEL", G, "APU fuel: tank 2 left pump and engine 2 tank valve on", "FO_ENG2_FUEL_FEED", On,
                    SetAll(("SWITCH_FUEL_PUMP_2L_1", 1), ("SWITCH_ENG_2_TANK_VALVE", 1), ("GUARD_ENG_2_TANK_VALVE", 0))),
                // The composite holds until the APU turns, a slow condition (FO-11): the 10 s grace would not cover it.
                Auto("PF_APU_START", G, "APU: start", "FO_APU_STARTING_OR_RUNNING", On, Set(X.ApuStart, 1)),
                Auto("PF_APU_GEN", G, "APU generator: on line", "FO_APU_GEN_CLOSED", On, Set(X.ApuGenerator, 1)),
                Auto("PF_AC_TIES", G, "AC bus ties: closed", "FO_AC_TIES_CLOSED", On, Set(X.AcTies, 1)),
                Auto("PF_APU_AIR", G, "Fuselage isolation and crossbleed valves: on", "FO_APU_AIR_ON", On,
                    SetAll(All(1, FuselageIsolation, "SWITCH_XBLEED_1", "SWITCH_XBLEED_3"))),
                // Off in the air depressurizes the cabin: on the ground only (FO-25).
                LeaveAlone(ActionManual("PF_PACKS_OFF", G, "Packs: off", SetAll(All(0, PackKeys))),
                    NotOnGround, PacksStayText),
                Auto("PF_PACKS_AUTO", G, "Pack control: auto", "FO_PACKS_AUTO", On,
                    SetAll(All(1, "SWITCH_BLEED_PLENUM_ENG_1", "SWITCH_BLEED_PLENUM_ENG_2", "SWITCH_BLEED_PLENUM_ENG_3"))),
                ActionManual("PF_HOT_AIR_OFF", G, "Hot air and hot air isolation valves: off", SetAll(All(0, HotAirKeys))),
                Auto("PF_ZONE_TRIMS", G, "Zone trim switches: on", "FO_ZONE_TRIMS_ON", On,
                    SetAll(All(1, "SWITCH_FLT_STA_CLOSE", "SWITCH_FWD_CAB_CLOSE", "SWITCH_MID_CAB_CLOSE", "SWITCH_AFT_CAB_CLOSE"))),
                Auto("PF_APU_BLEED", G, "APU bleed: on", ApuBleed, v => Is(v, 1), Set(ApuBleed, 1)),
                Auto("PF_PACKS_ON", G, "Packs: on", "FO_PACKS_ON", On, SetAll(All(1, PackKeys))),
                Auto("PF_APU_MODE_NORM", G, "APU compressor mode: normal", ApuMode, v => Is(v, 1), Set(ApuMode, 1)),
                Auto("PF_HOT_AIR_ON", G, "Hot air and hot air isolation valves: on", "FO_HOT_AIR_ON", On, SetAll(All(1, HotAirKeys))),
                // The tests light fire warnings and run hydraulic pumps: parked only (FO-25).
                LeaveAlone(ActionManual("PF_FIRE_TEST", G, "Engine and APU fire test", Set(X.FireTest, 1)),
                    NotParked, ParkedOnlyText),
                LeaveAlone(ActionManual("PF_WHEEL_WELL_TEST", G, "Wheel well fire test", Set(X.WheelWellFireTest, 1)),
                    NotParked, ParkedOnlyText),
                Auto("PF_HYD_ENGINE_PUMPS", G, "Engine hydraulic pumps: on and guarded", "FO_HYD_ENGINE_PUMPS_ON", On,
                    SetAll(All(1, "SWITCH_HYD_PUMP_A1", "SWITCH_HYD_PUMP_B1", "SWITCH_HYD_PUMP_C1", "SWITCH_HYD_PUMP_D1")
                        .Concat(All(0, "GUARD_HYD_PUMP_A1", "GUARD_HYD_PUMP_B1", "GUARD_HYD_PUMP_C1", "GUARD_HYD_PUMP_D1")).ToArray())),
                // Cuts hydraulic power: parked only, as Power Up's AC pump line (FO-25). Reads both halves of
                // its step's "Already set".
                LeaveAlone(Auto("PF_AC_PUMPS_PTUS_OFF", G, "AC hydraulic pumps and PTUs: off", "FO_AC_PUMPS_OFF", On,
                        SetAll(All(0, "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3", "SWITCH_PTU_AB", "SWITCH_PTU_CD")),
                        additionalFields: new[] { "FO_PTUS_OFF" }),
                    NotParked, ParkedOnlyText),
                LeaveAlone(ActionManual("PF_HYD_CHECK", G, "Hydraulic check", Set(X.HydraulicCheck, 1)),
                    NotParked, ParkedOnlyText),
                Auto("PF_AC_METER", G, "AC meter: APU generator", AcMeter, v => Is(v, AcMeterApu), Set(AcMeter, AcMeterApu)),
                Auto("PF_DC_METER", G, "DC meter: battery", DcMeter, v => Is(v, DcMeterBattery), Set(DcMeter, DcMeterBattery)),
                Auto("PF_TRANSFERS_OFF", G, "1A and 3A transfers: off", "FO_TRANSFERS_OFF", On,
                    SetAll(All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"))),
                Auto("PF_CROSSFEEDS", G, "Crossfeed valves: closed", "FO_CROSSFEEDS_CLOSED", On,
                    SetAll(All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"))),
                Auto("PF_TANK_VALVES", G, "Engine tank valves: on and guarded", "FO_TANK_VALVES_ON", On,
                    SetAll(All(1, "SWITCH_ENG_1_TANK_VALVE", "SWITCH_ENG_2_TANK_VALVE", "SWITCH_ENG_3_TANK_VALVE")
                        .Concat(All(0, "GUARD_ENG_1_TANK_VALVE", "GUARD_ENG_2_TANK_VALVE", "GUARD_ENG_3_TANK_VALVE")).ToArray())),
                Auto("PF_FUEL_CTRL", G, "Fuel control amplifier switches: off", "FO_FUEL_CTRL_OVRD_OFF", On,
                    SetAll(All(0, "SWITCH_FUEL_CTRL_OVRD_ENG_1", "SWITCH_FUEL_CTRL_OVRD_ENG_2", "SWITCH_FUEL_CTRL_OVRD_ENG_3"))),
                Auto("PF_MOVIE", G, "Movie interrupt: off and guarded", "FO_MOVIE_OFF", On, SetAll(All(0, "MOVIES_OFF", "GUARD_MOVIES_OFF"))),
                Auto("PF_SLAT_MONITOR", G, "Slat monitor: off", SlatMonitor, v => Is(v, 0), Set(SlatMonitor, 0)),
                Auto("PF_HI_PRESS", G, "High pressure bleed valves: on", "FO_HI_PRESS_ON", On,
                    SetAll(All(1, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3"))),
                Auto("PF_ISLN_VALVES", G, "Engine isolation valves: on", "FO_ISLN_VALVES_ON", On,
                    SetAll(All(1, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3"))),
                Auto("PF_ECS", G, "Overboard cooling and cargo heat: on", "FO_ECS_ON", On,
                    SetAll(All(1, "SWITCH_COOL_AIR_OVBD", "SWITCH_CARGO_HEAT_FWD", "SWITCH_CARGO_HEAT_MID", "SWITCH_CARGO_HEAT_AFT"))),
                Auto("PF_PRESS_MODE", G, "Pressurization mode: normal", PressMode, v => Is(v, 1), Set(PressMode, 1)),
                // No plan, no value: the tick writes nothing and the line stays as the selector reads (FO-20).
                Auto("PF_CABIN_ALT", G, "Cabin altitude selector: cruise cabin altitude", "FO_CABIN_ALT_AT_CRUISE", On,
                    Provided(X.CabinAltitudeSelect, s => s.CruiseCabinAltitudeFt())),
                Auto("PF_COMPASSES", G, "Compasses: magnetic", "FO_COMPASSES_MAG", On,
                    SetAll(All(1, "SWITCH_COMPASS_1_MAG", "SWITCH_COMPASS_2_MAG"))),
                Auto("PF_WIPERS", G, "Wipers: off", "FO_WIPERS_OFF", On, SetAll(All(0, "ROTARY_CPT_WIPER", "ROTARY_FO_WIPER"))),
                Auto("PF_ANTI_ICE", G, "Engine and wing anti-ice: off", "FO_ANTI_ICE_OFF", On,
                    SetAll(All(0, "SWITCH_ANTI_ICE_ENG_1", "SWITCH_ANTI_ICE_ENG_2", "SWITCH_ANTI_ICE_ENG_3", "SWITCH_ANTI_ICE_LEFT_AUTO",
                        "SWITCH_ANTI_ICE_LEFT_MAN", "SWITCH_ANTI_ICE_RIGHT_AUTO", "SWITCH_ANTI_ICE_RIGHT_MAN"))),
                Auto("PF_WINDSHIELD", G, "Windshield heat: normal", "FO_WINDSHIELD_NORMAL", On,
                    SetAll(All(1, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO"))),
                Auto("PF_SIDE_WINDOWS", G, "Side window heat: on", "FO_SIDE_WINDOWS_ON", On,
                    SetAll(All(1, "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR"))),
                Auto("PF_AIR_DATA", G, "Air data heat: off", "FO_AIR_DATA_HEAT_OFF", On,
                    SetAll(All(0, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT"))),
                Auto("PF_MACH_FEEL", G, "Mach feel and rudder limiter: on", "FO_MACH_FEEL_RUDDER_ON", On,
                    SetAll(All(1, "SWITCH_MACH_FEEL_1", "SWITCH_MACH_FEEL_2", "SWITCH_RUDDER_LIMITER"))),
                // A or B is done, as its step's "Already set": a tick turns the selector only off both systems.
                Auto("PF_IGNITION", G, "Ignition selector: A or B", "FO_IGNITION_SELECTED", On, Set("ROTARY_ENG_START_SEL", 0)),
                Auto("PF_STBY_LIGHTS", G, "Instrument standby lights: bright", StandbyLights, v => Is(v, 1), Set(StandbyLights, 1)),
                Auto("PF_PFCS", G, "Primary flight control switches: on", "FO_PFCS_ON", On, SetAll(All(1, PfcsKeys))),
                Auto("PF_FCES", G, "Flight control electronics switches: on", "FO_FCES_ON", On, SetAll(All(1, FcesKeys))),
                Auto("PF_BRAKE_SEL", G, "Brake system: B", BrakeSelector, v => Is(v, 0), Set(BrakeSelector, 0)),
                Auto("PF_ANTISKID_OFF", G, "Anti-skid: off", AntiSkid, v => Is(v, 0), Set(AntiSkid, 0)),
                Auto("PF_PARKING_BRAKE", G, "Parking brake: set", L1011Levers.ParkingBrakeKey, v => Is(v, 1),
                    Set(L1011Levers.ParkingBrakeKey, 1)),
                Auto("PF_AUTOBRAKE", G, "Autobrake: off", Autobrake, v => Is(v, AutobrakeOff), Set(Autobrake, AutobrakeOff)),
                Auto("PF_EXT_LIGHTS", G, "Exterior lights: logo and position on, the rest off", "FO_EXT_LIGHTS_PREFLIGHT", On,
                    SetAll(("TOGGLE_LOGO_LIGHT", 1), ("SWITCH_POSITION_LIGHT", 1),
                        ("TOGGLE_LANDING_LEFT", 0), ("TOGGLE_LANDING_RIGHT", 0), ("TOGGLE_LANDING_NOSE", 0), ("TOGGLE_RWY_TURNOFF", 0),
                        ("SWITCH_STROBE_LIGHT", 0), ("SWITCH_ANTICOL_LIGHT", 0),
                        ("SWITCH_FLOOD_LIGHT", 1), ("SWITCH_WHEEL_LIGHT", 1))),
                Auto("PF_EMER_LIGHTS", G, "Emergency lights: armed", "FO_EMER_LIGHTS_ARMED", On,
                    SetAll(("TOGGLE_EMER_LIGHTING", 1), ("GUARD_EMER_LIGHTING", 0))),
                Auto("PF_NO_SMOKING", G, "No smoking signs: on", NoSmoking, v => Is(v, 1), Set(NoSmoking, 1)),
                Auto("PF_INSTR_SOURCES", G, "Instrument source selectors: normal", "FO_INSTR_SOURCES_NORMAL", On,
                    SetAll(All(0, InstrumentSourceKeys))),
                Auto("PF_DME", G, "DME 1 and 2: normal", "FO_DME_NORMAL", On, SetAll(All(1, "ROTARY_NAV_1_MODE", "ROTARY_NAV_2_MODE"))),
                Auto("PF_FLIGHT_DIRECTORS", G, "Flight directors: on", "FO_FLIGHT_DIRECTORS_ON", On,
                    SetAll(All(1, "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B"))),
                Auto("PF_PADDLES_OFF", G, "Autopilot paddles: off", "FO_PADDLES_OFF", On,
                    SetAll(All(2, "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B"))),
                Auto("PF_SPEED_BRAKE", G, "Speed brake lever: forward", "FO_SPEED_BRAKE_FORWARD", On, Set(L1011Levers.SpeedBrakeKey, 0)),
                Auto("PF_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1), Set(Transponder, 1)),
                Reminder("PF_CPT_INS_ROUTE", G, "Load the route and set the initial track on INS 1, 2 and 3"),
                Reminder("PF_CPT_PMS", G, "Set the PMS"),
                Reminder("PF_CPT_NAV", G, "Set NAV 1 and 2 and the courses: Ctrl+N"),
                Reminder("PF_CPT_ALTIMETERS", G, "Set the altimeters to QNH: Ctrl+B"),
                Reminder("PF_CPT_AFCS", G, "Set the speed, heading and altitude on the autopilot"),
                Reminder("PF_CPT_TAKEOFF_DATA", G, "Enter the take-off data on the tablet"),
                Reminder("PF_CPT_RADIOS", G, "Set the radios"),
            }
        };
    }

    // ================================================================== Preflight: the read-backs (the aircraft's words)

    /// <summary>
    /// The flight engineer station preparation, opening with the cockpit safety inspection's APU start, air
    /// conditioning and fire test lines: Preflight delivers them, and Power Up's read-back holds only lines Power
    /// Up delivers (FO-7). The tests' own lines are judged by no state; their composites deliver them. The page's
    /// drain mast heater line is left out: the aircraft puts both switches back to off within seconds, by hand too.
    /// </summary>
    private static Group PreflightEngineerCL()
    {
        const string G = "PREFLIGHT_FE_CL";
        return new Group
        {
            Id = G, Name = "Flight Engineer Station Preparation",
            Items = new()
            {
                Read("PFC_APU_MASTER", G, "APU master power switch: on", ApuMaster, v => Is(v, 1)),
                Read("PFC_APU_FUEL", G, "One tank boost pump and engine 2 tank valve switch: on", "FO_ENG2_FUEL_FEED", On),
                Read("PFC_APU_START", G, "APU start switch: press and release", "FO_APU_RUNNING", On),
                Read("PFC_APU_GEN", G, "APU generator field and breaker switches: latch", "FO_APU_GEN_CLOSED", On),
                Read("PFC_AC_TIES", G, "Bus tie breakers: close", "FO_AC_TIES_CLOSED", On),
                Read("PFC_FUSELAGE_ISLN", G, "Fuselage isolation valve switch: latch", FuselageIsolation, v => Is(v, 1)),
                Read("PFC_XBLEEDS", G, "Crossbleed valve switches: latch", "FO_CROSSBLEEDS_ON", On),
                Read("PFC_PACK_AUTO", G, "Pack auto/manual control switches: auto", "FO_PACKS_AUTO", On),
                Read("PFC_ZONE_TRIMS", G, "Zone trim switches: latched", "FO_ZONE_TRIMS_ON", On),
                Read("PFC_APU_BLEED", G, "APU bleed air shut off switch: latch in", ApuBleed, v => Is(v, 1)),
                Read("PFC_PACKS", G, "Pack flow control valve switches: latch in", "FO_PACKS_ON", On),
                Read("PFC_APU_MODE_NORM", G, "APU mode selector: norm", ApuMode, v => Is(v, 1)),
                Read("PFC_HOT_AIR", G, "Hot air valve and hot air isolation valve switches: latch in", "FO_HOT_AIR_ON", On),
                Read("PFC_LOOP_SELECTORS", G, "Detection loop selectors: both", "FO_FIRE_LOOPS_BOTH", On),
                Manual("PFC_FIRE_TEST", G, "Engine and APU fire test: complete"),
                Manual("PFC_WHEEL_WELL_TEST", G, "Wheel well fire panel check: complete"),
                Read("PFC_HYD_ENGINE_PUMPS", G, "Engine pump shut off switches: latched", "FO_HYD_ENGINE_PUMPS_ON", On),
                Read("PFC_ATMS", G, "ATM switches: off", "FO_ATMS_OFF", On),
                Read("PFC_PTUS", G, "PTU switches: unlatched", "FO_PTUS_OFF", On),
                Manual("PFC_HYD_CHECK", G, "Hydraulic panel check: complete"),
                Read("PFC_AC_METER", G, "AC meter selector: APU", AcMeter, v => Is(v, AcMeterApu)),
                Read("PFC_DC_METER", G, "DC meter: bat", DcMeter, v => Is(v, DcMeterBattery)),
                Read("PFC_AC_ESS", G, "AC ESS power selector: norm b3 (g1)", AcEssBus, v => Is(v, AcEssNormal)),
                Read("PFC_TRANSFERS", G, "1A and 3A transfer switches: unlatched", "FO_TRANSFERS_OFF", On),
                Read("PFC_CROSSFEEDS", G, "Crossfeed switches: close", "FO_CROSSFEEDS_CLOSED", On),
                Read("PFC_TANK_VALVES", G, "All engine tank valve switches: latched", "FO_TANK_VALVES_ON", On),
                Read("PFC_APU_PUMP_ONLY", G, "Tank pump switches: one on for the APU", "FO_ONLY_APU_PUMP_ON", On),
                Read("PFC_JETTISON", G, "Master and tank jettison switches: unlatched", "FO_JETTISON_SAFE", On),
                Read("PFC_FUEL_CTRL", G, "Fuel control amplifier switches: unlatched", "FO_FUEL_CTRL_OVRD_OFF", On),
                Read("PFC_MOVIE", G, "Movie interrupt switch: unlatched", "FO_MOVIE_OFF", On),
                Read("PFC_SLAT_MONITOR", G, "Slat monitor switch: unlatch", SlatMonitor, v => Is(v, 0)),
                Read("PFC_HI_PRESS", G, "Hi pressure bleed switches: latched", "FO_HI_PRESS_ON", On),
                Read("PFC_ISLN_VALVES", G, "Engine isolation valve switches: latched", "FO_ISLN_VALVES_ON", On),
                Read("PFC_ECS", G, "Cool air overboard and cargo heat switches: latched", "FO_ECS_ON", On),
                Read("PFC_PRESS_MODE", G, "Mode sel: norm", PressMode, v => Is(v, 1)),
                Read("PFC_CABIN_ALT", G, "Alt sel control: set cruise altitude", "FO_CABIN_ALT_AT_CRUISE", On),
            }
        };
    }

    /// <summary>The overhead preparation. The page's "Seat belts: on" belongs to Before Start and is left out.</summary>
    private static Group PreflightOverheadCL()
    {
        const string G = "PREFLIGHT_OVHD_CL";
        return new Group
        {
            Id = G, Name = "Overhead Preparation",
            Items = new()
            {
                Read("PFO_INS", G, "INS 1, 2 and 3: check alignment", "FO_INS_ALIGN_OR_NAV", On),
                Read("PFO_FIRE_HANDLES", G, "Fire pull handles: in", "FO_FIRE_HANDLES_IN", On),
                Read("PFO_COMPASSES", G, "Compass 1 and 2: mag", "FO_COMPASSES_MAG", On),
                Read("PFO_RADIO_MASTERS", G, "Master radio switches: on", "FO_RADIO_MASTERS_ON", On),
                Read("PFO_WIPERS", G, "Captain and F/O wipers: off", "FO_WIPERS_OFF", On),
                Read("PFO_ANTI_ICE", G, "Engine and wing anti-ice switches: unlatched", "FO_ANTI_ICE_OFF", On),
                Read("PFO_WINDSHIELD", G, "Capt and F/O windshield heat switches: latched", "FO_WINDSHIELD_NORMAL", On),
                Read("PFO_SIDE_WINDOWS", G, "Side window switches: latch in", "FO_SIDE_WINDOWS_ON", On),
                Read("PFO_AIR_DATA", G, "Air data sensor heat switches: all off", "FO_AIR_DATA_HEAT_OFF", On),
                Read("PFO_MACH_FEEL", G, "Mach feel switches and rudder travel limiter: latched", "FO_MACH_FEEL_RUDDER_ON", On),
                Read("PFO_START_LIGHTS", G, "Engine start lights: all out", "FO_START_LIGHTS_OUT", On),
                Read("PFO_IGNITION", G, "Ignition: A or B", "FO_IGNITION_SELECTED", On),
                Read("PFO_STBY_LIGHTS", G, "Instrument standby lights: brt", StandbyLights, v => Is(v, 1)),
                Read("PFO_PFCS", G, "All PFCS switches: latched", "FO_PFCS_ON", On),
                Read("PFO_FCES", G, "All FCES switches: latched", "FO_FCES_ON", On),
                Read("PFO_BRAKE_SEL", G, "Brake selector switch: norm sys B", BrakeSelector, v => Is(v, 0)),
                Read("PFO_ANTISKID", G, "Anti-skid switch: unlatch", AntiSkid, v => Is(v, 0)),
                Read("PFO_PARKING_BRAKE", G, "Parking brake: set", L1011Levers.ParkingBrakeKey, v => Is(v, 1)),
                Read("PFO_AUTOBRAKE", G, "Auto brake: off", Autobrake, v => Is(v, AutobrakeOff)),
                Read("PFO_EXT_LIGHTS", G, "Exterior lights: logo and position on, the rest off", "FO_EXT_LIGHTS_PREFLIGHT", On),
                Read("PFO_EMER_LIGHTS", G, "Emergency lighting switch: arm", "FO_EMER_LIGHTS_ARMED", On),
                Read("PFO_STBY_POWER", G, "Standby power: armed", "FO_STBY_ARMED", On),
                Read("PFO_NO_SMOKING", G, "No smoking: on", NoSmoking, v => Is(v, 1)),
            }
        };
    }

    private static Group PreflightMainPanelCL()
    {
        const string G = "PREFLIGHT_MAIN_CL";
        return new Group
        {
            Id = G, Name = "Main Panel Preparation",
            Items = new()
            {
                Reminder("PFM_NAV", G, "Nav 1 and Nav 2: set"),
                Read("PFM_DME", G, "Nav 1 and Nav 2 DME: norm", "FO_DME_NORMAL", On),
                Reminder("PFM_AUTOPILOT", G, "Autopilot: IAS, heading, courses and altitude set"),
                Read("PFM_FLIGHT_DIRECTORS", G, "Flight directors: on", "FO_FLIGHT_DIRECTORS_ON", On),
                Read("PFM_AUTOPILOT_MASTERS", G, "Autopilot masters: off", "FO_PADDLES_OFF", On),
                Reminder("PFM_ALTIMETERS", G, "Altimeters: set QNH"),
                Read("PFM_INSTR_SOURCE", G, "Instrument source selector: normal", "FO_INSTR_SOURCES_NORMAL", On),
                Read("PFM_GEAR", G, "Landing gear: down, 3 green", L1011GearConfirmation.DownField, On),
                Read("PFM_BRAKE_SYS", G, "Brake system: norm sys B", BrakeSelector, v => Is(v, 0)),
            }
        };
    }

    private static Group PreflightConsoleCL()
    {
        const string G = "PREFLIGHT_CONSOLE_CL";
        return new Group
        {
            Id = G, Name = "Center Console Preparation",
            Items = new()
            {
                Reminder("PFX_INS", G, "INS 1, 2 and 3: set"),
                Reminder("PFX_PMS", G, "PMS: set"),
                Read("PFX_DISCONNECT", G, "Pitch and roll disconnect handles: normal", "FO_DISCONNECT_HANDLES_STOWED", On),
                Read("PFX_PARKING_BRAKE", G, "Parking brake: set", L1011Levers.ParkingBrakeKey, v => Is(v, 1)),
                Read("PFX_SPEED_BRAKE", G, "Speed brake lever: forward", "FO_SPEED_BRAKE_FORWARD", On),
                Read("PFX_FUEL_IGN", G, "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On),
                Reminder("PFX_RADIOS", G, "Communication radios: set"),
                Read("PFX_XPDR", G, "Transponder: set and STBY", Transponder, v => Is(v, 1)),
            }
        };
    }

    // ================================================================== Before Start

    private const string SeatBelts = "SWITCH_SEATBELT_LIGHT";
    private const string AntiCollision = "SWITCH_ANTICOL_LIGHT";

    /// <summary>The transponder mode selector's TA/RA position (1 is standby).</summary>
    private const int TransponderTaRa = 5;

    /// <summary>The external power line's reason: it stays on until the APU generator is read on line.</summary>
    private const string ExtPowerStaysText = "External power left on: the APU generator is not on line.";

    private static readonly string[] GalleyBuses = { "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3" };

    /// <summary>The three engine isolation valves, then the two crossbleeds: the flow's order.</summary>
    private static readonly string[] StartAirValves =
        { "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3" };

    /// <summary>
    /// The Before Start flow's writes and the fleet's tail, in the flow's order and with its labels: a single switch
    /// reads its key at the target, a multi-switch line its synthetic, external power its breaker read open. The
    /// cabin baro and each INS line write their provider's value (the captain's altimeter; NAV for a unit read
    /// aligned or already at NAV) and nothing without one, and read the state their step sets.
    /// </summary>
    private static Group BeforeStart()
    {
        const string G = "BEFORE_START";
        return new Group
        {
            Id = G, Name = "Before Start",
            Items = new()
            {
                // Dropped with no other source on line, external power leaves the aircraft on the battery: the tick
                // waits for the APU generator as the flow does, and an unread generator refuses too.
                LeaveAlone(Auto("BS_EXT_OFF", G, "External power: off", "FO_EXT_POWER_OFF", On, Set(X.ExternalPower, 0)),
                    s => !s.IsOn("FO_APU_GEN_ON_LINE"), ExtPowerStaysText),
                Auto("BS_SEAT_BELTS", G, "Seat belt signs: on", SeatBelts, v => Is(v, 1), Set(SeatBelts, 1)),
                Auto("BS_ANTICOL", G, "Anti-collision lights: on", AntiCollision, v => Is(v, 1), Set(AntiCollision, 1)),
                Auto("BS_GALLEY", G, "Galley power: off", "FO_GALLEY_OFF", On, SetAll(All(0, GalleyBuses))),
                Auto("BS_APU_MODE_MIN", G, "APU compressor mode: minimum", ApuMode, v => Is(v, 0), Set(ApuMode, 0)),
                // Packs 1 and 3 off in the air cut two of the three packs: on the ground only (FO-25).
                LeaveAlone(Auto("BS_PACK_2", G, "Packs: pack 2 on, packs 1 and 3 off", "FO_PACK_2_ONLY", On,
                        SetAll((PackKeys[0], 0), (PackKeys[1], 1), (PackKeys[2], 0))),
                    NotOnGround, PacksStayText),
                Auto("BS_ISLN_XBLEEDS", G, "Engine isolation and crossbleed valves: on", "FO_START_AIR_VALVES_ON", On,
                    SetAll(All(1, StartAirValves))),
                Auto("BS_APU_BLEED", G, "APU bleed: on", ApuBleed, v => Is(v, 1), Set(ApuBleed, 1)),
                // A standard or unread captain's altimeter gives no value: the tick writes nothing (FO-20).
                Auto("BS_CABIN_BARO", G, "Cabin barometric setting: captain's altimeter", "FO_CABIN_BARO_SET", On,
                    Provided(X.CabinBaro, s => s.CabinBaroTargetHundredths())),
                InsNav(G, 1), InsNav(G, 2), InsNav(G, 3),
                Auto("BS_XPDR", G, "Transponder: TA/RA", Transponder, v => Is(v, TransponderTaRa), Set(Transponder, TransponderTaRa)),
                Reminder("BS_ACARS", G, "Start ACARS"),
                Reminder("BS_CLEARANCE", G, "Obtain pushback and start clearance"),
            }
        };
    }

    /// <summary>INS <paramref name="unit"/> to NAV: the tick turns it only from ALIGN once it reads aligned (or finds it
    /// already at NAV), and writes nothing while it still aligns or sits at OFF, STBY or ATT (FO-20), as its step does.</summary>
    private static Item InsNav(string groupId, int unit)
    {
        string key = L1011InsAlignmentAnnouncer.MsuKey(unit);
        string n = unit.ToString(CultureInfo.InvariantCulture);
        return Auto($"BS_INS_{n}_NAV", groupId, $"INS {n}: nav", key, v => Is(v, L1011FoStateEvaluator.InsNav),
            Provided(key, s => s.InsNavTarget(unit)));
    }

    /// <summary>The aircraft's "Before engine start" page, up to its first ground start, in its own words; the fleet's
    /// "Start ACARS" last.</summary>
    private static Group BeforeStartCL()
    {
        const string G = "BEFORE_START_CL";
        return new Group
        {
            Id = G, Name = "Before Engine Start",
            Items = new()
            {
                Read("BSC_INS_NAV", G, "All INS: NAV", "FO_INS_NAVIGATING", On),
                Read("BSC_XPDR", G, "Transponder: TA/RA", Transponder, v => Is(v, TransponderTaRa)),
                Read("BSC_SEAT_BELTS", G, "Seatbelt sign: on", SeatBelts, v => Is(v, 1)),
                Read("BSC_ANTICOL", G, "Anti coll light: on", AntiCollision, v => Is(v, 1)),
                Read("BSC_IGNITION", G, "Start ignition: A or B", "FO_IGNITION_SELECTED", On),
                Read("BSC_DOORS", G, "Door lights: out", "FO_DOORS_CLOSED", On),
                Read("BSC_GALLEY", G, "Galley power: off", "FO_GALLEY_OFF", On),
                Read("BSC_FUEL_PUMP", G, "Fuel pump: minimum one on", "FO_ANY_FUEL_PUMP_ON", On),
                Read("BSC_APU_MODE", G, "APU: min mode", ApuMode, v => Is(v, 0)),
                Read("BSC_PACK_2", G, "Packs: pack 2 on", "FO_PACK_2_ONLY", On),
                Read("BSC_ISLN_VALVES", G, "All engine isolation valves: latched", "FO_START_AIR_VALVES_ON", On),
                Reminder("BSC_ACARS", G, "Start ACARS"),
            }
        };
    }

    // ================================================================== Engine Start

    /// <summary>The start order, and the engine started before each (none before engine 2).</summary>
    private static readonly (int Engine, int? Previous)[] StartOrder = { (2, null), (1, 2), (3, 1) };

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string IsolationValve(int n) => $"SWITCH_ISLN_VALVE_ENG_{Num(n)}";

    /// <summary>Engine <paramref name="n"/>'s own tank pump: the first pump of tank 1, 2 left or 3.</summary>
    private static string TankPump(int n) => n switch
    {
        1 => "SWITCH_FUEL_PUMP_1_1",
        2 => "SWITCH_FUEL_PUMP_2L_1",
        _ => "SWITCH_FUEL_PUMP_3_1",
    };

    /// <summary>
    /// The Engine Start flow's writes, in its order and with its labels: a single switch reads its key at the target, the
    /// feed its synthetic, the generators all six relays. A ground start line latches on its engine running (N3 at 50 %),
    /// never on the switch, which the aircraft releases at about 55 %; its tick turns the switch on and watches N3 rise,
    /// as the flow step does.
    /// </summary>
    private static Group EngineStart()
    {
        const string G = "ENGINE_START";
        var items = new List<Item>();
        foreach (var (n, previous) in StartOrder)
        {
            string e = $"ES_E{Num(n)}_";
            if (previous is int p)
                items.Add(Auto(e + "PREV_ISLN_OFF", G, $"Engine {Num(p)} isolation valve: off", IsolationValve(p), v => Is(v, 0),
                    Set(IsolationValve(p), 0)));
            items.Add(Auto(e + "FUEL_FEED", G, $"Engine {Num(n)} fuel: tank pump and tank valve on", $"FO_ENG{Num(n)}_FUEL_FEED", On,
                SetAll((TankPump(n), 1), ($"SWITCH_ENG_{Num(n)}_TANK_VALVE", 1), ($"GUARD_ENG_{Num(n)}_TANK_VALVE", 0))));
            items.Add(AutoLatch(e + "GND_START", G, $"Engine {Num(n)} ground start switch: on", X.N3Key(n),
                v => v >= L1011FoStateEvaluator.EngineRunningN3, Set(X.GroundStart(n), 1)));
            string fuel = X.FuelIgnitionKey(n);
            items.Add(Auto(e + "FUEL_IGN", G, $"Engine {Num(n)} fuel and ignition: on", fuel, v => Is(v, 1), Set(fuel, 1)));
        }
        items.Add(Auto("ES_GENERATORS", G, "Engine generator fields and breakers: closed", "FO_ENGINE_GENERATORS_ON", On,
            Set(X.EngineGenerators, 1)));
        return new Group { Id = G, Name = "Engine Start", Items = items };
    }

    /// <summary>The aircraft's engine start page on APU air, in its own words, engines 2, 1 then 3. The page's engine
    /// parameters check and its ground-air and crossbleed alternatives are left out, and its APU shutdown lines belong to
    /// After Start.</summary>
    private static Group EngineStartCL()
    {
        const string G = "ENGINE_START_CL";
        var items = new List<Item> { Read("ESC_FUEL_PUMP", G, "Fuel pump: minimum one on", "FO_ANY_FUEL_PUMP_ON", On) };
        foreach (var (n, previous) in StartOrder)
        {
            if (previous is int p)
                items.Add(Read($"ESC_E{Num(p)}_ISLN_OFF", G, $"Engine {Num(p)} isln valve: unlatch", IsolationValve(p), v => Is(v, 0)));
            items.Add(Read($"ESC_E{Num(n)}_FUEL_IGN", G, $"Engine {Num(n)} at 20 percent N3: fuel and ignition on",
                X.FuelIgnitionKey(n), v => Is(v, 1)));
            // The switch off with the engine running: the switch alone reads off before any start too.
            items.Add(Read($"ESC_E{Num(n)}_RELEASED", G, $"Engine {Num(n)} at 55 percent N3: ground start switch released",
                $"FO_ENG{Num(n)}_START_RELEASED", On));
            items.Add(Read($"ESC_E{Num(n)}_RUNNING", G, $"Engine {Num(n)}: running", $"FO_ENG{Num(n)}_RUNNING", On));
        }
        items.Add(Read("ESC_GEN_FIELDS", G, "Engine generator field: close", "FO_GEN_FIELDS_CLOSED", On));
        items.Add(Read("ESC_GEN_BREAKERS", G, "Engine generator breaker: close", "FO_GEN_BREAKERS_CLOSED", On));
        return new Group { Id = G, Name = "Engine Start", Items = items };
    }

    // ================================================================== After Start

    private const string AtmB = "ROTARY_HYD_B2_MODE";
    private const string AtmC = "ROTARY_HYD_C2_MODE";
    private const string IgnitionSelector = "ROTARY_ENG_START_SEL";
    private const string TaxiLight = "TOGGLE_TAXI_LIGHT";

    /// <summary>The APU stop button's row: a press, no position.</summary>
    internal const string ApuStopPress = "APU_STOP";

    /// <summary>The ATM switches' positions: 1 AUTO, 2 ON (0 is OFF).</summary>
    private const int AtmAuto = 1, AtmOn = 2;

    /// <summary>The AC meter's generator 3 position, the ignition selector's both systems.</summary>
    private const int AcMeterGen3 = 3, IgnitionBoth = 1;

    /// <summary>The flaps line's reason, and its flow step's: the flaps move on the ground only.</summary>
    internal const string FlapsStayText = "Flaps left as they are: not read on the ground.";

    /// <summary>The transfers line's reason, and its flow step's.</summary>
    internal const string TransfersStayText = "1A and 3A transfers left off: tanks 1A and 3A are empty or unread.";

    /// <summary>Tanks 1A and 3A do not both read fuel (either empty or unread): the transfers stay off. The transfers
    /// step and its tick share it.</summary>
    internal static bool TransferTanksNotFueled(L1011FoStateEvaluator s) => !s.IsOn("FO_TANKS_1A_3A_FUELED");

    /// <summary>The tablet's take-off stabilizer trim as it is spoken ("2.38 units", invariant), null before an
    /// export. The Captain's reminder and the action line's live value share it.</summary>
    internal static string? StabTrimUnits(L1011FoStateEvaluator s)
        => s.TakeoffStabTrimUnits() is double units ? $"{units.ToString("0.00", CultureInfo.InvariantCulture)} units" : null;

    /// <summary>
    /// The After Start flow's writes and its three unconditional Captain lines, in the flow's order and with its labels:
    /// a single switch reads its key at the target, a multi-switch line its synthetic, the APU generator line its breaker
    /// read open, the stop line the APU stopped, the flaps line the handle at the take-off index. The ATMs-on line reads
    /// no state, the flow putting them to auto right after; a tick does its step's write.
    /// </summary>
    private static Group AfterStart()
    {
        const string G = "AFTER_START";
        return new Group
        {
            Id = G, Name = "After Start",
            Items = new()
            {
                Auto("AS_HI_PRESS", G, "High pressure bleed valves: on", "FO_HI_PRESS_ON", On,
                    SetAll(All(1, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3"))),
                Auto("AS_ISLN_VALVES", G, "Engine isolation valves: on", "FO_ISLN_VALVES_ON", On,
                    SetAll(All(1, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3"))),
                Auto("AS_APU_AIR", G, "Fuselage isolation and crossbleed valves: on", "FO_APU_AIR_ON", On,
                    SetAll(All(1, FuselageIsolation, "SWITCH_XBLEED_1", "SWITCH_XBLEED_3"))),
                Auto("AS_PACKS_ON", G, "Packs: on", "FO_PACKS_ON", On, SetAll(All(1, PackKeys))),
                ActionManual("AS_ATMS_ON", G, "ATM switches: on", SetAll(All(AtmOn, AtmB, AtmC))),
                Auto("AS_ATMS_AUTO", G, "ATM switches: auto", "FO_ATMS_AUTO", On, SetAll(All(AtmAuto, AtmB, AtmC))),
                Auto("AS_GALLEY_ON", G, "Galley power: on", "FO_GALLEY_ON", On, SetAll(All(1, GalleyBuses))),
                Auto("AS_AC_METER", G, "AC meter: generator 3", AcMeter, v => Is(v, AcMeterGen3), Set(AcMeter, AcMeterGen3)),
                Auto("AS_DC_METER", G, "DC meter: battery", DcMeter, v => Is(v, DcMeterBattery), Set(DcMeter, DcMeterBattery)),
                Auto("AS_FUEL_PUMPS", G, "Fuel pumps: all on", "FO_FUEL_PUMPS_ON", On, SetAll(All(1, FuelPumps))),
                Auto("AS_CROSSFEEDS", G, "Crossfeed valves: closed", "FO_CROSSFEEDS_CLOSED", On,
                    SetAll(All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"))),
                // On only with fuel in both tanks 1A and 3A, as its step.
                LeaveAlone(Auto("AS_TRANSFERS", G, "1A and 3A transfers: on", "FO_TRANSFERS_ON", On,
                        SetAll(All(1, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"))),
                    TransferTanksNotFueled, TransfersStayText),
                Auto("AS_PITOT_HEAT", G, "Pitot and alpha probe heat: on", "FO_PITOT_ALPHA_HEAT_ON", On,
                    SetAll(All(1, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_ALPHA_HEAT"))),
                Auto("AS_IGNITION_BOTH", G, "Ignition selector: both", IgnitionSelector, v => Is(v, IgnitionBoth),
                    Set(IgnitionSelector, IgnitionBoth)),
                // No take-off flaps from the tablet or SimBrief: the tick writes nothing (FO-20). On the ground only.
                LeaveAlone(Auto("AS_FLAPS", G, "Flaps: take-off setting", "FO_FLAPS_TAKEOFF", On,
                        Provided(L1011Levers.FlapHandleKey, s => s.TakeoffFlapIndex())),
                    NotOnGround, FlapsStayText),
                Auto("AS_TAXI_LIGHT", G, "Nose taxi light: on", TaxiLight, v => Is(v, 1), Set(TaxiLight, 1)),
                Reminder("AS_CPT_STAB_TRIM", G, "Set the stabilizer trim for take-off", StabTrimUnits),
                Reminder("AS_CPT_ANTI_ICE", G, "Engine anti-ice as required"),
                Reminder("AS_CPT_CONTROLS", G, "Check the speed brake and the flight controls"),
                Auto("AS_APU_GEN_TRIP", G, "APU generator breaker: trip", "FO_APU_GEN_OFF_LINE", On, Set(X.ApuGenerator, 0)),
                Auto("AS_APU_BLEED_OFF", G, "APU bleed: off", ApuBleed, v => Is(v, 0), Set(ApuBleed, 0)),
                Auto("AS_APU_STOP", G, "APU: stop", "FO_APU_OFF", On, Set(ApuStopPress, 1)),
                Auto("AS_APU_MASTER_OFF", G, "APU master: off", ApuMaster, v => Is(v, 0), Set(ApuMaster, 0)),
            }
        };
    }

    /// <summary>The aircraft's after start page in its own words, then the engine start page's APU shutdown lines, which
    /// After Start delivers once the engine generators are on line.</summary>
    private static Group AfterStartCL()
    {
        const string G = "AFTER_START_CL";
        return new Group
        {
            Id = G, Name = "After Start",
            Items = new()
            {
                Read("ASC_HI_PRESS", G, "All engines hi press switches: latched", "FO_HI_PRESS_ON", On),
                Read("ASC_ISLN_VALVES", G, "All eng isol valve: latch", "FO_ISLN_VALVES_ON", On),
                Read("ASC_FUSELAGE_ISLN", G, "Fuselage isolation valve switch: latched", FuselageIsolation, v => Is(v, 1)),
                Read("ASC_XBLEEDS", G, "All cross bleed switches: latched", "FO_CROSSBLEEDS_ON", On),
                Read("ASC_PACKS", G, "Pack flow control switches: latch", "FO_PACKS_ON", On),
                Read("ASC_ATMS", G, "ATMs: auto", "FO_ATMS_AUTO", On),
                Read("ASC_GALLEY", G, "Galley power: on", "FO_GALLEY_ON", On),
                Read("ASC_GEN_FIELDS", G, "Engine generator field: close", "FO_GEN_FIELDS_CLOSED", On),
                Read("ASC_GEN_BREAKERS", G, "Engine generator breaker: close", "FO_GEN_BREAKERS_CLOSED", On),
                Read("ASC_IDG", G, "IDG low pressure lights: out", "FO_IDG_LIGHTS_OUT", On),
                Read("ASC_BUS_FAIL", G, "All bus fail lights: out", "FO_BUS_FAIL_LIGHTS_OUT", On),
                Read("ASC_AC_METER", G, "AC meter selector: gen 3", AcMeter, v => Is(v, AcMeterGen3)),
                Read("ASC_DC_METER", G, "DC meter selector: bat", DcMeter, v => Is(v, DcMeterBattery)),
                Read("ASC_AC_ESS", G, "Essential power selector: normal b3 (g1)", AcEssBus, v => Is(v, AcEssNormal)),
                Read("ASC_FUEL_PUMPS", G, "Fuel tank pumps: all on", "FO_FUEL_PUMPS_ON", On),
                Read("ASC_CROSSFEEDS", G, "Crossfeed valves: closed", "FO_CROSSFEEDS_CLOSED", On),
                Read("ASC_TRANSFERS", G, "If there is fuel in tanks 1A and 3A: latch xfr", "FO_TRANSFERS_ON", On),
                Read("ASC_PITOT_HEAT", G, "Capt and F/O pitot heat: on", "FO_PITOT_ALPHA_HEAT_ON", On),
                Read("ASC_ALPHA_HEAT", G, "Alpha probe heat: on", "FO_PITOT_ALPHA_HEAT_ON", On),
                Read("ASC_IGNITION", G, "Engine ignition: both", IgnitionSelector, v => Is(v, IgnitionBoth)),
                Reminder("ASC_STAB_TRIM", G, "Stabilizer trim: set for take off"),
                Reminder("ASC_SPEED_BRAKES", G, "Speed brakes: check"),
                Read("ASC_FLAPS", G, "Flaps: set for take off", "FO_FLAPS_TAKEOFF", On),
                Reminder("ASC_CONTROLS", G, "Flight controls: check"),
                Read("ASC_APU_GEN_TRIP", G, "APU generator breaker: trip", "FO_APU_GEN_OFF_LINE", On),
                Read("ASC_APU_BLEED", G, "APU bleed air shut off switch: unlatch", ApuBleed, v => Is(v, 0)),
                Read("ASC_APU_STOP", G, "APU stop button: press", "FO_APU_OFF", On),
                Read("ASC_APU_MASTER", G, "APU master power: off", ApuMaster, v => Is(v, 0)),
            }
        };
    }

    /// <summary>The aircraft's taxi page in its own words: the nose taxi light, which After Start turns on, and the
    /// Captain's brakes.</summary>
    private static Group TaxiCL()
    {
        const string G = "TAXI_CL";
        return new Group
        {
            Id = G, Name = "Taxi",
            Items = new()
            {
                Read("TXC_TAXI_LIGHT", G, "Nose wheel taxi light: on", TaxiLight, v => Is(v, 1)),
                Reminder("TXC_PARKING_BRAKE", G, "Parking brake: release"),
                Reminder("TXC_BRAKES", G, "Brakes: check"),
            }
        };
    }

    // ================================================================== builders (the MD-11's)

    /// <summary>A state line; <paramref name="additionalFields"/> must each read done under the same condition too.</summary>
    private static Item Auto(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn? action, string[]? additionalFields = null) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.RevertToState,
        AdditionalStateFields = additionalFields ?? Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
    };

    /// <summary>A tick that writes <paramref name="provider"/>'s value to <paramref name="key"/> when it has one,
    /// and nothing when it has none (no plan): the MD-11's Dial-A-Flap line.</summary>
    private static CheckFn Provided(string key, Func<L1011FoStateEvaluator, int?> provider) => async (e, s) =>
    {
        if (provider(s) is int target)
            await e.Set(key, target);
    };

    /// <summary>A read-back (*_CL) item: auto-detected, action-free.</summary>
    private static Item Read(string id, string groupId, string label, string field, Func<double, bool> condition)
        => Auto(id, groupId, label, field, condition, action: null);

    /// <summary>The ONE sanctioned StayComplete ([FOB-1]): the ground start lines, latched on the engine running.</summary>
    private static Item AutoLatch(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.StayComplete,
        AdditionalStateFields = Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
    };

    private static Item ActionManual(string id, string groupId, string label, CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    /// <summary>A line no state judges and no tick acts on: ticked by hand, or by the flow step that delivers it.</summary>
    private static Item Manual(string id, string groupId, string label) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
    };

    /// <summary>A Captain's line; <paramref name="live"/>, when given, is the value the First Officer reads for it,
    /// spoken on a tick and shown on the status line, never in the tree (the A380's shape).</summary>
    private static Item Reminder(string id, string groupId, string text, Func<L1011FoStateEvaluator, string?>? live = null) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
        LiveValue = live,
    };

    // A line whose hand-tick the First Officer refuses in the aircraft's current state
    // (ChecklistItem.LeaveAloneWhen), speaking `text` instead: the guards the flow carries.
    private static Item LeaveAlone(Item item, Func<L1011FoStateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }

    private const string ParkedOnlyText = "Left alone: the aircraft must be on the ground with the engines stopped.";

    /// <summary>The fuel pump line's reason: it also keeps the pumps while the APU runs.</summary>
    private const string ParkedApuOffText = "Left alone: the aircraft must be on the ground with the engines stopped and the APU shut down.";

    // On the ground (the stock SIM_ON_GROUND, the flow's PU_ON_GROUND) with every engine read stopped
    // (FO_ENGINES_STOPPED, the flow's PU_ENGINES_STOPPED); airborne, an engine turning, or either unread,
    // the pumps and the battery stay as they are ([FO-25]).
    private static bool NotParked(L1011FoStateEvaluator s) =>
        !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn("FO_ENGINES_STOPPED");

    /// <summary>The packs line's reason, and its flow step's.</summary>
    internal const string PacksStayText = "Packs left on: not read on the ground.";

    /// <summary>Not read on the ground (airborne, or the stock SIM_ON_GROUND unread): the packs stay on, since off
    /// in the air depressurizes the cabin ([FO-25]). The Preflight packs step and its tick share it.</summary>
    internal static bool NotOnGround(L1011FoStateEvaluator s) => !s.IsPosition("SIM_ON_GROUND", 1);
}
