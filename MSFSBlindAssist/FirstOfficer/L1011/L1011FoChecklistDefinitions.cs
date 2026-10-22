using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.L1011;

using Item = Models.ChecklistItem<L1011FoActionExecutor, L1011FoStateEvaluator>;
using Group = Models.ChecklistGroup<L1011FoActionExecutor, L1011FoStateEvaluator>;
using CheckFn = System.Func<L1011FoActionExecutor, L1011FoStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer checklists: one group per flow, with the flow's id and name, and no
/// read-back group: Power Up, Preflight, Before Start, Engine Start, After Start, Before Takeoff, After Takeoff,
/// Descent, Before Landing, After Landing, Parking and Shutdown.
///
/// Each group lists its flow's lines in the flow's order ([FO-6]): the writes and the unconditional Captain steps,
/// with the flow's labels (ticking a write's line runs the same write as its flow step, through the executor, and a
/// state line ticks itself from the aircraft: <c>RevertToState</c>, [FOB-1]), and the lines of the aircraft's own
/// checklist pages that no write of the flow reads: each a read-only line under the id of the flow step that
/// completes it (a <c>Check</c>, a wait, or for Power Up's loop selectors the fire test), with the page's words, or a
/// Captain reminder with a step of its own. A page line that read the same state as one of the group's lines went:
/// no two lines in a group read the same thing. Conditional Captain prompts (ground power, cabin altitude, cabin baro,
/// doors, INS not in NAV, take-off flaps) stay flow-only, as the MD-11's do.
///
/// Power Up opens with the cockpit safety inspection's five read-only checks (fire handles, gear lever, speed brake,
/// flaps agree, fuel and ignition off) and keeps its loop selectors line after the fire test. The inspection has no
/// galley line, so none is invented. Five of its hand ticks are left alone unless the aircraft reads on the ground with
/// every engine stopped ([FO-25]): the ATM switches, the AC hydraulic pumps and the fuel pumps off, the standby power
/// check, which turns the battery off for a moment, and the engine and APU fire test, which lights the fire warnings.
/// Unread refuses, as the flow's guards do. The fuel pump tick is also left alone while the APU runs or is still
/// spooling up, as its flow step is: the tank 2 left pump feeds the APU from the moment it starts turning.
///
/// Preflight keeps, from the engineer station, overhead, main panel and console pages, the APU running (its
/// "APU start switch: press and release", which the APU running wait completes) and the read-only checks: ATMs off,
/// AC essential selector, the APU's one tank pump, jettison, standby power armed, the disconnect handles, fuel and
/// ignition off, INS aligning, fire handles, radio masters, start lights and the gear down. The two tests and the
/// hydraulic cut are parked-only ticks, as Power Up's pump lines are; the packs line is left alone off the ground.
///
/// Before Start (the flow's writes and the fleet's tail, "Start ACARS" then the clearance) keeps the "Before engine
/// start" page's doors, one fuel pump on, ignition A or B and all INS navigating, each a read-only check. The cabin
/// baro and INS lines write their provider's value, or nothing without one, as their steps do. The pack 2 line switches
/// packs 1 and 3 off, so its tick is left alone off the ground, as Preflight's packs line is; the external power line is
/// left alone until the APU generator reads on line, as its flow step waits for it.
///
/// Engine Start lists per engine, 2, 1 then 3, the previous engine's isolation valve off, its feed, its ground start,
/// its fuel and ignition and the aircraft's release of its ground start switch (the engine start page's line, which
/// the release wait completes); then the generators. The three ground start lines are the profile's only
/// <c>StayComplete</c> lines ([FOB-1]): the aircraft releases the switch itself at about 55 % N3, so each latches on its
/// engine running and no flow step links it. No other line reads an engine running.
///
/// After Start keeps the after start page's generator fields and breakers, IDG and bus fail lights and essential power
/// selector, each read only, and ends with the taxi page's two Captain lines, the parking brake released and the brakes
/// checked. The flaps line writes the take-off index (the tablet's, else SimBrief's) or nothing without one, and is left
/// alone off the ground; the transfers line is left alone unless tanks 1A and 3A both hold fuel. The generator trip,
/// bleed and stop lines are left alone until all three engine generators read on line, as the flow's APU steps wait for
/// them, and the stop line's tick holds until the APU reads stopped; the master line is left alone until the APU reads
/// stopped, as the flow's master waits on its stop. The ATMs-on line reads no state: the flow puts them to auto right
/// after. The stabilizer trim line shows the tablet's exported trim as its live value.
///
/// Before Takeoff (the flow's eight writes) keeps the line-up page's transponder at TA/RA, read only. The pack 2 line
/// switches packs 1 and 3 off, so its tick is left alone off the ground, as Before Start's is.
///
/// After Takeoff (the gear lever up, the Captain's flap schedule, the climb's writes) ends with the take-off page's flaps
/// up and gear up (the lever and every leg), read only, the gear last ([FO-13]). The gear lever line is left alone
/// unless the aircraft reads airborne, as its step is.
///
/// Descent (the seat belt signs, the Captain's landing data, landing autobrake and QNH, and the cabin altitude for the
/// destination) adds nothing from its page. The cabin altitude line writes the plan's destination elevation, clamped to
/// the selector's end stops, or nothing without a plan, as its step does.
///
/// Before Landing (the flow's writes and the Captain's flap schedule) keeps the approach page's "Autobrake: set", a
/// Captain reminder that names the panel and no setting ([FO-14]), after the gear lever, and its gear line, last, read
/// only ([FO-13]). The lever's own line reads "Gear lever: down" so the two gear lines are told apart. The cabin baro line
/// writes the captain's altimeter, or nothing when it reads standard or unread.
///
/// After Landing (the flow's fifteen writes) adds nothing from its page. The stabilizer trim line reads the trim readout
/// at zero, the typed entry being write-only; the flaps line is left alone off the ground, as After Start's is, and so
/// are the anti-skid, air data heat, stabilizer trim and speed brake lines, which the flow's ground guard covers; the
/// APU start line holds until the APU turns ([FO-11]).
///
/// Parking (the flow's writes and the Captain's ground power line) keeps the parking page's "APU generator or external
/// power: on", which the power source wait completes. The APU generator line is left alone unless the APU runs, and the
/// external power line while the APU generator is on line or no ground power is available, as their steps are. The fuel
/// and ignition line is an engine cut that takes the engine generators off the buses: left alone unless the aircraft
/// reads on the ground with the APU generator or external power on line, as the flow's power source wait (and the
/// MD-11's fuel line) refuses it; the INS, the ATMs and AC pumps and the fuel panel lines are left alone unless the
/// aircraft reads on the ground with every engine stopped ([FO-25]).
///
/// Shutdown (the flow's writes) adds nothing from the securing page. The APU stop line's tick holds until the APU reads
/// stopped ([FO-11]), and the master line is left alone until it does. The packs line is left alone off the ground; the
/// fuel pump and battery lines unless the aircraft reads parked with every engine stopped and the APU not turning (the
/// pumps as Power Up's, the battery as the PMDG 777's waits on its APU); the standby power line unless parked ([FO-25]).
/// </summary>
public static class L1011FoChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        PowerUp(), Preflight(), BeforeStart(), EngineStart(), AfterStart(), BeforeTakeoff(), AfterTakeoff(),
        Descent(), BeforeLanding(), AfterLanding(), Parking(), Shutdown(),
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

    // ================================================================== Power Up

    private static Group PowerUp()
    {
        const string G = "POWER_UP";
        return new Group
        {
            Id = G, Name = "Power Up",
            Items = new()
            {
                // The cockpit safety inspection's checks: what the First Officer finds and never moves, read only, each
                // completed by its own flow check.
                Read("PU_CHK_FIRE_HANDLES", G, "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", On),
                Read("PU_CHK_GEAR", G, "Gear lever: down", "FO_GEAR_LEVER_DOWN", On),
                Read("PU_CHK_SPEED_BRAKE", G, "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", On),
                Read("PU_CHK_FLAPS", G, "Flaps and flap handle: agree", "FO_FLAPS_AGREE", On),
                Read("PU_CHK_FUEL_IGN", G, "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On),
                Auto("PU_CONT_IGN", G, "Continuous ignition: off", ContIgnition, v => Is(v, 0), Set(ContIgnition, 0)),
                Auto("PU_RADIO_MASTERS", G, "Radio master switches: on", "FO_RADIO_MASTERS_ON", On,
                    SetAll(("SWITCH_MASTER_RADIO_ESS", 1), ("SWITCH_MASTER_RADIO_NO2", 1))),
                Auto("PU_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1), Set(Transponder, 1)),
                Auto("PU_RADAR", G, "Weather radar: off", RadarOff, v => Is(v, 1), Set(RadarOff, 1)),
                // Cold-and-dark cuts ([FO-25]): never airborne, never with an engine turning, never unread.
                // The ATMs are the air-turbine hydraulic pumps of systems B and C, left in AUTO through the
                // flight: off there cuts backup hydraulic power, and the fleet's cut sweep cannot see them by
                // their label, so the line carries the guard itself.
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
                // The check's own end state as lines a pilot can tick without the check. Neither is a cut.
                Auto("PU_BATTERY", G, "Battery: on", Battery, v => Is(v, 1), Set(Battery, 1)),
                Auto("PU_STBY_ARMED", G, "Standby power: armed", "FO_STBY_ARMED", On,
                    SetAll(("GUARD_STBY_POWER", 1), ("TOGGLE_STBY_POWER", 1), ("GUARD_STBY_POWER", 0))),
                Auto("PU_AC_ESS", G, "AC essential bus selector: bus 3, generator 1", AcEssBus, v => Is(v, AcEssNormal),
                    Set(AcEssBus, AcEssNormal)),
                Auto("PU_EXT_PWR", G, "External power: on", "FO_EXT_POWER_ON", On, Set(X.ExternalPower, 1)),
                // The test lights the fire warnings: parked only (FO-25), as it was in Preflight.
                LeaveAlone(ActionManual("PU_FIRE_TEST", G, "Engine and APU fire test", Set(X.FireTest, 1)),
                    NotParked, ParkedOnlyText),
                // The inspection's loop selectors line, read only: the fire test leaves them at Both and delivers it.
                Read("PU_LOOP_SELECTORS", G, "Detection loop selectors: both", "FO_FIRE_LOOPS_BOTH", On),
                // One composite, unit by unit: a unit already at ALIGN or NAV is left alone, never rewritten.
                Auto("PU_INS_ALIGN", G, "INS 1, 2 and 3: align", "FO_INS_ALIGN_OR_NAV", On, Set(X.InsAlign, 1)),
                Reminder("PU_INS_POSITION", G,
                    "Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit."),
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
    /// The Preflight flow's writes, the read-only lines it completes and its seven unconditional Captain steps, in the
    /// flow's order: a single switch reads its key at the target (the ignition selector, A or B, as its step's
    /// "Already set" does), a multi-switch line its synthetic, a composite the state it delivers. The two
    /// tests have no state and run every tick; the cabin altitude writes the plan's value, or nothing without one.
    /// The packs off, the hot air off and the APU's minimum compressor mode read no state either: the flow undoes
    /// each later (packs on, hot air on, compressor mode normal), so a state line would untick itself after a good
    /// run. Each is ticked when its step completes, and a hand tick does its step's write. The read-only lines keep the
    /// aircraft's preparation pages' words, under the ids of the steps that complete them.
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
                // The engineer station page's start line reads the APU running, which the flow's wait completes.
                Read("PF_APU_RUNNING", G, "APU start switch: press and release", "FO_APU_RUNNING", On),
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
                // The tests light a fire warning and run hydraulic pumps: parked only (FO-25).
                LeaveAlone(ActionManual("PF_WHEEL_WELL_TEST", G, "Wheel well fire test", Set(X.WheelWellFireTest, 1)),
                    NotParked, ParkedOnlyText),
                Auto("PF_HYD_ENGINE_PUMPS", G, "Engine hydraulic pumps: on and guarded", "FO_HYD_ENGINE_PUMPS_ON", On,
                    SetAll(All(1, "SWITCH_HYD_PUMP_A1", "SWITCH_HYD_PUMP_B1", "SWITCH_HYD_PUMP_C1", "SWITCH_HYD_PUMP_D1")
                        .Concat(All(0, "GUARD_HYD_PUMP_A1", "GUARD_HYD_PUMP_B1", "GUARD_HYD_PUMP_C1", "GUARD_HYD_PUMP_D1")).ToArray())),
                Read("PF_CHK_ATMS", G, "ATM switches: off", "FO_ATMS_OFF", On),
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
                Read("PF_CHK_AC_ESS", G, "AC ESS power selector: norm b3 (g1)", AcEssBus, v => Is(v, AcEssNormal)),
                Auto("PF_TRANSFERS_OFF", G, "1A and 3A transfers: off", "FO_TRANSFERS_OFF", On,
                    SetAll(All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"))),
                Auto("PF_CROSSFEEDS", G, "Crossfeed valves: closed", "FO_CROSSFEEDS_CLOSED", On,
                    SetAll(All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"))),
                Auto("PF_TANK_VALVES", G, "Engine tank valves: on and guarded", "FO_TANK_VALVES_ON", On,
                    SetAll(All(1, "SWITCH_ENG_1_TANK_VALVE", "SWITCH_ENG_2_TANK_VALVE", "SWITCH_ENG_3_TANK_VALVE")
                        .Concat(All(0, "GUARD_ENG_1_TANK_VALVE", "GUARD_ENG_2_TANK_VALVE", "GUARD_ENG_3_TANK_VALVE")).ToArray())),
                Read("PF_CHK_APU_PUMP", G, "Tank pump switches: one on for the APU", "FO_ONLY_APU_PUMP_ON", On),
                Read("PF_CHK_JETTISON", G, "Master and tank jettison switches: unlatched", "FO_JETTISON_SAFE", On),
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
                Read("PF_CHK_STBY_ARMED", G, "Standby power: armed", "FO_STBY_ARMED", On),
                Auto("PF_NO_SMOKING", G, "No smoking signs: on", NoSmoking, v => Is(v, 1), Set(NoSmoking, 1)),
                Auto("PF_INSTR_SOURCES", G, "Instrument source selectors: normal", "FO_INSTR_SOURCES_NORMAL", On,
                    SetAll(All(0, InstrumentSourceKeys))),
                Auto("PF_DME", G, "DME 1 and 2: normal", "FO_DME_NORMAL", On, SetAll(All(1, "ROTARY_NAV_1_MODE", "ROTARY_NAV_2_MODE"))),
                Auto("PF_FLIGHT_DIRECTORS", G, "Flight directors: on", "FO_FLIGHT_DIRECTORS_ON", On,
                    SetAll(All(1, "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B"))),
                Auto("PF_PADDLES_OFF", G, "Autopilot paddles: off", "FO_PADDLES_OFF", On,
                    SetAll(All(2, "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B"))),
                Read("PF_CHK_DISCONNECT", G, "Pitch and roll disconnect handles: normal", "FO_DISCONNECT_HANDLES_STOWED", On),
                Auto("PF_SPEED_BRAKE", G, "Speed brake lever: forward", "FO_SPEED_BRAKE_FORWARD", On, Set(L1011Levers.SpeedBrakeKey, 0)),
                Read("PF_CHK_FUEL_IGN", G, "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On),
                Auto("PF_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1), Set(Transponder, 1)),
                // What Power Up left, read only.
                Read("PF_CHK_INS", G, "INS 1, 2 and 3: check alignment", "FO_INS_ALIGN_OR_NAV", On),
                Read("PF_CHK_FIRE_HANDLES", G, "Fire pull handles: in", "FO_FIRE_HANDLES_IN", On),
                Read("PF_CHK_RADIO_MASTERS", G, "Master radio switches: on", "FO_RADIO_MASTERS_ON", On),
                Read("PF_CHK_START_LIGHTS", G, "Engine start lights: all out", "FO_START_LIGHTS_OUT", On),
                Read("PF_CHK_GEAR", G, "Landing gear: down, 3 green", L1011GearConfirmation.DownField, On),
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
    /// The Before Start flow's writes, the "Before engine start" page's read-only checks and the fleet's tail, in the
    /// flow's order: a single switch reads its key at the target, a multi-switch line its synthetic, external power its
    /// breaker read open. The cabin baro and each INS line write their provider's value (the captain's altimeter; NAV
    /// for a unit at ALIGN read aligned, or already at NAV) and nothing without one, and read the state their step sets.
    /// The page's "Start ACARS" is the tail's own line, so it is there once.
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
                // The page's "Door lights ... OUT": the line reads the four door positions, never the light (it does not
                // follow the doors), so it is worded on what it reads.
                Read("BS_CHK_DOORS", G, "Doors: closed", "FO_DOORS_CLOSED", On),
                Auto("BS_GALLEY", G, "Galley power: off", "FO_GALLEY_OFF", On, SetAll(All(0, GalleyBuses))),
                Read("BS_CHK_FUEL_PUMP", G, "Fuel pump: minimum one on", "FO_ANY_FUEL_PUMP_ON", On),
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
                Read("BS_CHK_IGNITION", G, "Start ignition: A or B", "FO_IGNITION_SELECTED", On),
                InsNav(G, 1), InsNav(G, 2), InsNav(G, 3),
                Read("BS_CHK_INS_NAV", G, "All INS: NAV", "FO_INS_NAVIGATING", On),
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
    /// as the flow step does. Each engine's release line, the engine start page's, reads the switch off with the engine
    /// running (the switch alone reads off before any start too), and the flow's release wait completes it.
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
            items.Add(Read(e + "RELEASED", G, $"Engine {Num(n)} at 55 percent N3: ground start switch released",
                $"FO_ENG{Num(n)}_START_RELEASED", On));
        }
        items.Add(Auto("ES_GENERATORS", G, "Engine generator fields and breakers: closed", "FO_ENGINE_GENERATORS_ON", On,
            Set(X.EngineGenerators, 1)));
        return new Group { Id = G, Name = "Engine Start", Items = items };
    }

    // ================================================================== After Start

    private const string AtmB = "ROTARY_HYD_B2_MODE";
    private const string AtmC = "ROTARY_HYD_C2_MODE";
    private const string IgnitionSelector = "ROTARY_ENG_START_SEL";
    private const string TaxiLight = "TOGGLE_TAXI_LIGHT";

    /// <summary>The ATM switches' positions: 1 AUTO, 2 ON (0 is OFF).</summary>
    private const int AtmAuto = 1, AtmOn = 2;

    /// <summary>The AC meter's generator 3 position, the ignition selector's both systems.</summary>
    private const int AcMeterGen3 = 3, IgnitionBoth = 1;

    /// <summary>The flaps line's reason, and its flow step's: the flaps move on the ground only.</summary>
    internal const string FlapsStayText = "Flaps left as they are: not read on the ground.";

    /// <summary>The transfers line's reason, and its flow step's.</summary>
    internal const string TransfersStayText = "1A and 3A transfers left off: tanks 1A and 3A are empty or unread.";

    /// <summary>The APU shutdown lines' reason: the APU keeps the buses until the engine generators carry them.</summary>
    internal const string ApuStaysTickText = "APU left running: the engine generators are not all on line.";

    /// <summary>The APU master's reason, step and tick, in After Start and Shutdown: the master stays on over an APU that
    /// did not stop.</summary>
    internal const string ApuMasterStaysText = "APU master left on: the APU did not stop.";

    /// <summary>The APU not read stopped (5 % or more, or unread): its master stays on. The master ticks share it, as the
    /// flows' masters wait on their stops.</summary>
    internal static bool ApuNotStopped(L1011FoStateEvaluator s) => !s.IsOn("FO_APU_OFF");

    /// <summary>Not all six engine generator relays read closed (one open, or any unread): the APU shutdown ticks are
    /// left alone, as the flow's APU steps wait for the same field.</summary>
    internal static bool EngineGeneratorsNotOnLine(L1011FoStateEvaluator s) => !s.IsOn("FO_ENGINE_GENERATORS_ON");

    /// <summary>Tanks 1A and 3A do not both read fuel (either empty or unread): the transfers stay off. The transfers
    /// step and its tick share it.</summary>
    internal static bool TransferTanksNotFueled(L1011FoStateEvaluator s) => !s.IsOn("FO_TANKS_1A_3A_FUELED");

    /// <summary>The tablet's take-off stabilizer trim as it is spoken ("2.38 units", invariant), null before an
    /// export. The Captain's reminder and the action line's live value share it.</summary>
    internal static string? StabTrimUnits(L1011FoStateEvaluator s)
        => s.TakeoffStabTrimUnits() is double units ? $"{units.ToString("0.00", CultureInfo.InvariantCulture)} units" : null;

    /// <summary>
    /// The After Start flow's writes, the after start page's read-only checks, its three unconditional Captain lines and
    /// the taxi page's two, in the flow's order: a single switch reads its key at the target, a multi-switch line its
    /// synthetic, the APU generator line its breaker read open, the stop line the APU stopped, the flaps line the handle
    /// at the take-off index. The ATMs-on line reads no state, the flow putting them to auto right after; a tick does its
    /// step's write. The generator check completes the page's two generator lines, the fields and the breakers.
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
                // What Engine Start left, read only.
                Read("AS_GEN_FIELDS", G, "Engine generator field: close", "FO_GEN_FIELDS_CLOSED", On),
                Read("AS_GEN_BREAKERS", G, "Engine generator breaker: close", "FO_GEN_BREAKERS_CLOSED", On),
                Read("AS_CHK_IDG", G, "IDG low pressure lights: out", "FO_IDG_LIGHTS_OUT", On),
                Read("AS_CHK_BUS_FAIL", G, "All bus fail lights: out", "FO_BUS_FAIL_LIGHTS_OUT", On),
                Auto("AS_AC_METER", G, "AC meter: generator 3", AcMeter, v => Is(v, AcMeterGen3), Set(AcMeter, AcMeterGen3)),
                Auto("AS_DC_METER", G, "DC meter: battery", DcMeter, v => Is(v, DcMeterBattery), Set(DcMeter, DcMeterBattery)),
                Read("AS_CHK_AC_ESS", G, "Essential power selector: normal b3 (g1)", AcEssBus, v => Is(v, AcEssNormal)),
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
                // The APU off only once the engine generators carry the buses, as the flow waits for them: tripped
                // or stopped with one off line, the aircraft would be left on the battery. Unread refuses too.
                LeaveAlone(Auto("AS_APU_GEN_TRIP", G, "APU generator breaker: trip", "FO_APU_GEN_OFF_LINE", On,
                        Set(X.ApuGenerator, 0)),
                    EngineGeneratorsNotOnLine, ApuStaysTickText),
                LeaveAlone(Auto("AS_APU_BLEED_OFF", G, "APU bleed: off", ApuBleed, v => Is(v, 0), Set(ApuBleed, 0)),
                    EngineGeneratorsNotOnLine, ApuStaysTickText),
                // The composite holds until the APU reads stopped, a slow condition (FO-11): about a minute.
                LeaveAlone(Auto("AS_APU_STOP", G, "APU: stop", "FO_APU_OFF", On, Set(X.ApuStop, 1)),
                    EngineGeneratorsNotOnLine, ApuStaysTickText),
                // The master only over an APU read stopped, as the flow's master waits on its stop.
                LeaveAlone(Auto("AS_APU_MASTER_OFF", G, "APU master: off", ApuMaster, v => Is(v, 0), Set(ApuMaster, 0)),
                    ApuNotStopped, ApuMasterStaysText),
                // The taxi page's Captain lines; its nose taxi light is the flow's own taxi light line.
                Reminder("AS_CPT_PARKING_BRAKE", G, "Parking brake: release"),
                Reminder("AS_CPT_BRAKES", G, "Brakes: check"),
            }
        };
    }

    // ================================================================== Before Takeoff

    private const string StrobeLights = "SWITCH_STROBE_LIGHT";
    private const string ProbeHeat = "SWITCH_PROBE_HEAT";
    private const string RadarOn = "SWITCH_WX_WX";

    /// <summary>The autobrake selector's TAKEOFF position.</summary>
    private const int AutobrakeTakeoff = 0;

    /// <summary>The Before Takeoff flow's eight writes and the line-up page's transponder check, in its order: a single
    /// switch reads its key at the target, a multi-switch line its synthetic, the transponder TA/RA read only.</summary>
    private static Group BeforeTakeoff()
    {
        const string G = "BEFORE_TAKEOFF";
        return new Group
        {
            Id = G, Name = "Before Takeoff",
            Items = new()
            {
                // Packs 1 and 3 off in the air cut two of the three packs: on the ground only (FO-25).
                LeaveAlone(Auto("BT_PACK_2", G, "Packs: pack 2 on, packs 1 and 3 off", "FO_PACK_2_ONLY", On,
                        SetAll((PackKeys[0], 0), (PackKeys[1], 1), (PackKeys[2], 0))),
                    NotOnGround, PacksStayText),
                Auto("BT_ANTISKID", G, "Anti-skid: on", AntiSkid, v => Is(v, 1), Set(AntiSkid, 1)),
                Auto("BT_AUTOBRAKE", G, "Autobrake: takeoff", Autobrake, v => Is(v, AutobrakeTakeoff), Set(Autobrake, AutobrakeTakeoff)),
                Auto("BT_STROBES", G, "Strobe lights: on", StrobeLights, v => Is(v, 1), Set(StrobeLights, 1)),
                Auto("BT_LANDING_LIGHTS", G, "Landing lights: on", "FO_LANDING_LIGHTS_ON", On,
                    SetAll(All(1, "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT"))),
                Auto("BT_CONT_IGN", G, "Continuous ignition: on", ContIgnition, v => Is(v, 1), Set(ContIgnition, 1)),
                Auto("BT_PROBE_HEAT", G, "Temperature probe heat: on", ProbeHeat, v => Is(v, 1), Set(ProbeHeat, 1)),
                // Before Start set TA/RA: read only.
                Read("BT_CHK_XPDR", G, "Transponder: TA/RA", Transponder, v => Is(v, TransponderTaRa)),
                Auto("BT_RADAR", G, "Weather radar: on", RadarOn, v => Is(v, 1), Set(RadarOn, 1)),
            }
        };
    }

    // ================================================================== After Takeoff

    private static readonly string[] GroundLights = { "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF" };

    /// <summary>The gear lever's up position (0 down, 50 off).</summary>
    private const int GearUp = 100;

    /// <summary>The gear line's reason, and its flow step's.</summary>
    internal const string GearStaysDownText = "Gear left down: on the ground.";

    /// <summary>Not read airborne (on the ground, or the stock SIM_ON_GROUND unread): the gear stays down. The After
    /// Takeoff gear step and its tick share it.</summary>
    internal static bool NotAirborne(L1011FoStateEvaluator s) => !s.IsPosition("SIM_ON_GROUND", 0);

    /// <summary>
    /// The After Takeoff flow's writes, its Captain's flap schedule and the take-off page's flaps and gear checks, in its
    /// order: the gear lever line reads the lever up, a single switch its key at the target, a multi-switch line its
    /// synthetic; the flaps line the handle up and the gear line, LAST ([FO-13]), the lever and every leg, read only.
    /// </summary>
    private static Group AfterTakeoff()
    {
        const string G = "AFTER_TAKEOFF";
        return new Group
        {
            Id = G, Name = "After Takeoff",
            Items = new()
            {
                // Gear up only with the aircraft read airborne, as the flow's step.
                LeaveAlone(Auto("AT_GEAR", G, "Landing gear: up", "FO_GEAR_LEVER_UP", On, Set(L1011Levers.GearLeverKey, GearUp)),
                    NotAirborne, GearStaysDownText),
                Reminder("AT_CPT_FLAPS", G,
                    "Retract the flaps on schedule: 10 at V2 plus 10, 4 at V2 plus 20, up at V2 plus 60.",
                    label: "Flaps up on schedule"),
                Auto("AT_AUTOBRAKE", G, "Autobrake: off", Autobrake, v => Is(v, AutobrakeOff), Set(Autobrake, AutobrakeOff)),
                Auto("AT_GROUND_LIGHTS", G, "Nose, taxi and runway turnoff lights: off", "FO_GROUND_LIGHTS_OFF", On,
                    SetAll(All(0, GroundLights))),
                Auto("AT_CONT_IGN", G, "Continuous ignition: off", ContIgnition, v => Is(v, 0), Set(ContIgnition, 0)),
                Auto("AT_PACKS_ON", G, "Packs: on", "FO_PACKS_ON", On, SetAll(All(1, PackKeys))),
                // The take-off page's lines, read only: the Captain's flaps up, then the gear up, lever and every leg, last.
                Read("AT_CHK_FLAPS_UP", G, "At V2 plus 60: set flaps up", "FO_FLAPS_UP", On),
                Read("AT_GEAR_UP_CHECK", G, "Positive rate of climb: landing gear up", L1011GearConfirmation.UpField, On),
            }
        };
    }

    // ================================================================== Descent

    /// <summary>The Descent flow's writes and its three Captain reminders, in its order and with its labels: the seat
    /// belt line reads its key on, the cabin altitude line the selector at the destination.</summary>
    private static Group Descent()
    {
        const string G = "DESCENT";
        return new Group
        {
            Id = G, Name = "Descent",
            Items = new()
            {
                Auto("DS_SEAT_BELTS", G, "Seat belt signs: on", SeatBelts, v => Is(v, 1), Set(SeatBelts, 1)),
                Reminder("DS_CPT_LANDING_DATA", G, "Prepare the landing data on the tablet"),
                // The landing autobrake is the Captain's (FO-14): the panel named, no setting.
                Reminder("DS_CPT_AUTOBRAKE", G, "Set the landing autobrake on the overhead Antiskid and Autobrake panel"),
                Reminder("DS_CPT_QNH", G, "Set the altimeters to QNH at the transition level: Ctrl+B"),
                // No plan, no value: the tick writes nothing and the line stays as the selector reads (FO-20).
                Auto("DS_CABIN_ALT", G, "Cabin altitude selector: destination elevation", "FO_CABIN_ALT_AT_DEST", On,
                    Provided(X.CabinAltitudeSelect, s => s.DestinationCabinAltitudeFt())),
            }
        };
    }

    // ================================================================== Before Landing

    /// <summary>The gear lever's down position (50 off, 100 up).</summary>
    private const int GearDown = 0;

    /// <summary>The landing autobrake reminder: the Captain's ([FO-14]), the panel named, no setting.</summary>
    internal const string LandingAutobrakeCheckText = "Check the landing autobrake on the overhead Antiskid and Autobrake panel";

    /// <summary>
    /// The Before Landing flow's writes, the approach page's landing autobrake, the Captain's flap schedule and the
    /// approach page's gear line, in its order: the cabin baro line reads the setting at the captain's altimeter, a
    /// multi-switch line its synthetic, a single switch its key at the target, the gear lever line the lever down, and
    /// the gear line, LAST ([FO-13]), the lever and every leg, read only.
    /// </summary>
    private static Group BeforeLanding()
    {
        const string G = "BEFORE_LANDING";
        return new Group
        {
            Id = G, Name = "Before Landing",
            Items = new()
            {
                // A standard or unread captain's altimeter gives no value: the tick writes nothing (FO-20).
                Auto("BL_CABIN_BARO", G, "Cabin barometric setting: captain's altimeter", "FO_CABIN_BARO_SET", On,
                    Provided(X.CabinBaro, s => s.CabinBaroTargetHundredths())),
                Auto("BL_TRANSFERS_OFF", G, "1A and 3A transfers: off", "FO_TRANSFERS_OFF", On,
                    SetAll(All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"))),
                Auto("BL_FUEL_PUMPS", G, "Fuel pumps: all on", "FO_FUEL_PUMPS_ON", On, SetAll(All(1, FuelPumps))),
                Auto("BL_CROSSFEEDS", G, "Crossfeed valves: closed", "FO_CROSSFEEDS_CLOSED", On,
                    SetAll(All(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"))),
                Auto("BL_GROUND_LIGHTS", G, "Nose, taxi and runway turnoff lights: on", "FO_GROUND_LIGHTS_ON", On,
                    SetAll(All(1, GroundLights))),
                Auto("BL_CONT_IGN", G, "Continuous ignition: on", ContIgnition, v => Is(v, 1), Set(ContIgnition, 1)),
                // The lever's own words: "Landing gear: down" is the gear line below, the lever and every leg.
                Auto("BL_GEAR", G, "Gear lever: down", "FO_GEAR_LEVER_DOWN", On, Set(L1011Levers.GearLeverKey, GearDown)),
                // The approach page's "Autobrake: set", after the gear as on the page: the Captain's (FO-14).
                Reminder("BL_CPT_AUTOBRAKE", G, LandingAutobrakeCheckText, label: "Autobrake: set"),
                Reminder("BL_CPT_FLAPS", G,
                    "Extend the flaps on schedule: 4 at Vref plus 60, 10 at Vref plus 40, 22 at Vref plus 30, 33 at Vref plus 20.",
                    label: "Flaps on schedule"),
                // The approach page's gear line, read only, LAST (FO-13): the gear check completes it.
                Read("BL_GEAR_DOWN_CHECK", G, "Landing gear: down", L1011GearConfirmation.DownField, On),
            }
        };
    }

    // ================================================================== After Landing

    private static readonly string[] LandingLights = { "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT" };

    private static readonly string[] AirDataHeat =
        { "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT" };

    private static readonly string[] SideWindows =
        { "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR" };

    /// <summary>The ground-only After Landing lines' reasons: each says what stays as it is.</summary>
    private const string AntiSkidStaysText = "Anti-skid left on: not read on the ground.";
    private const string AirDataHeatStaysText = "Air data heat left on: not read on the ground.";
    private const string StabTrimStaysText = "Stabilizer trim left as it is: not read on the ground.";
    private const string SpeedBrakeStaysText = "Speed brake lever left as it is: not read on the ground.";

    /// <summary>
    /// The After Landing flow's fifteen writes, in its order and with its labels: a single switch reads its key at the
    /// target, a multi-switch line its synthetic, the stabilizer trim its readout at zero, the speed brake forward, the
    /// flaps up, the APU start the APU turning. A tick is the pilot's own and has no flow guard, so the anti-skid, air data
    /// heat, stabilizer trim, speed brake and flaps ticks are each left alone off the ground; the APU start tick holds until
    /// the APU turns, a slow condition ([FO-11]).
    /// </summary>
    private static Group AfterLanding()
    {
        const string G = "AFTER_LANDING";
        return new Group
        {
            Id = G, Name = "After Landing",
            Items = new()
            {
                // In the air these four hurt the landing: anti-skid off, probe heat off, the trim zeroed, an armed speed
                // brake stowed. On the ground only, as the flow's guard has it.
                LeaveAlone(Auto("AL_ANTISKID", G, "Anti-skid: off", AntiSkid, v => Is(v, 0), Set(AntiSkid, 0)),
                    NotOnGround, AntiSkidStaysText),
                Auto("AL_AUTOBRAKE", G, "Autobrake: off", Autobrake, v => Is(v, AutobrakeOff), Set(Autobrake, AutobrakeOff)),
                Auto("AL_LANDING_LIGHTS", G, "Landing lights: off", "FO_LANDING_LIGHTS_OFF", On, SetAll(All(0, LandingLights))),
                Auto("AL_STROBES", G, "Strobe lights: off", StrobeLights, v => Is(v, 0), Set(StrobeLights, 0)),
                Auto("AL_CONT_IGN", G, "Continuous ignition: off", ContIgnition, v => Is(v, 0), Set(ContIgnition, 0)),
                LeaveAlone(Auto("AL_AIR_DATA", G, "Air data heat: off", "FO_AIR_DATA_HEAT_OFF", On, SetAll(All(0, AirDataHeat))),
                    NotOnGround, AirDataHeatStaysText),
                Auto("AL_WINDSHIELD", G, "Windshield heat: idle", "FO_WINDSHIELD_IDLE", On,
                    SetAll(All(0, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO"))),
                Auto("AL_SIDE_WINDOWS", G, "Side window heat: off", "FO_SIDE_WINDOWS_OFF", On, SetAll(All(0, SideWindows))),
                // The typed entry is write-only: the line reads the trim readout, which the executor verifies the write on.
                LeaveAlone(Auto("AL_STAB_TRIM", G, "Stabilizer trim: zero", "FO_STAB_TRIM_ZERO", On, Set(L1011Levers.StabTrimKey, 0)),
                    NotOnGround, StabTrimStaysText),
                LeaveAlone(Auto("AL_SPEED_BRAKE", G, "Speed brake lever: forward", "FO_SPEED_BRAKE_FORWARD", On,
                        Set(L1011Levers.SpeedBrakeKey, 0)),
                    NotOnGround, SpeedBrakeStaysText),
                // The flaps move on the ground only, as the flow's step.
                LeaveAlone(Auto("AL_FLAPS_UP", G, "Flaps: up", "FO_FLAPS_UP", On, Set(L1011Levers.FlapHandleKey, 0)),
                    NotOnGround, FlapsStayText),
                Auto("AL_RADAR", G, "Weather radar: off", RadarOff, v => Is(v, 1), Set(RadarOff, 1)),
                Auto("AL_TRANSFERS_OFF", G, "1A and 3A transfers: off", "FO_TRANSFERS_OFF", On,
                    SetAll(All(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"))),
                Auto("AL_APU_MASTER", G, "APU master: on", ApuMaster, v => Is(v, 1), Set(ApuMaster, 1)),
                // The composite holds until the APU turns, a slow condition (FO-11): the 10 s grace would not cover it.
                Auto("AL_APU_START", G, "APU: start", "FO_APU_STARTING_OR_RUNNING", On, Set(X.ApuStart, 1)),
            }
        };
    }

    // ================================================================== Parking

    /// <summary>The APU generator line's reason, and its flow step's.</summary>
    internal const string ApuGenStaysOffText = "APU generator left off: the APU is not running.";

    /// <summary>The APU not read running (stopped, still spooling up, or unread): its generator stays off line. The
    /// Parking step and its tick share it.</summary>
    internal static bool ApuNotRunning(L1011FoStateEvaluator s) => !s.IsOn("FO_APU_RUNNING");

    /// <summary>The external power line's reason, and its flow step's.</summary>
    internal const string ExtPowerStaysOffText =
        "External power left off: the APU generator is on line, or no ground power is available.";

    /// <summary>The APU generator reads on line, or the external power available light does not read lit (out or
    /// unread): external power stays off. The Parking step and its tick share it.</summary>
    internal static bool ExtPowerNotWanted(L1011FoStateEvaluator s) =>
        s.IsOn("FO_APU_GEN_ON_LINE") || !s.IsOn("FO_EXT_POWER_AVAILABLE");

    /// <summary>The fuel and ignition line's reason: an engine cut, so on the ground only, and with a source to take the
    /// buses from the engine generators it cuts.</summary>
    private const string FuelIgnitionStaysText =
        "Fuel and ignition left on: not on the ground, or no APU generator or external power on line.";

    /// <summary>Not read on the ground, or neither the APU generator nor external power read on line (both open, or
    /// unread): the fuel and ignition stay on, or the aircraft would drop to its battery.</summary>
    private static bool FuelCutNotAllowed(L1011FoStateEvaluator s) => NotOnGround(s) || !s.IsOn("FO_POWER_SOURCE_ON");

    private static readonly string[] FuelIgnition = { "TOGGLE_CUTOFF_ENG_1", "TOGGLE_CUTOFF_ENG_2", "TOGGLE_CUTOFF_ENG_3" };

    private static readonly string[] Crossfeeds = { "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED" };

    /// <summary>The fuel panel for the APU, the flow's order: the tank 2 left pump on, the other seven off, the engine 2
    /// tank valve on under its closed guard, the crossfeeds closed.</summary>
    private static readonly (string Key, int Target)[] ApuFuelPanel =
        new[] { ("SWITCH_FUEL_PUMP_2L_1", 1) }
            .Concat(All(0, FuelPumps.Where(p => p != "SWITCH_FUEL_PUMP_2L_1").ToArray()))
            .Concat(new[] { ("SWITCH_ENG_2_TANK_VALVE", 1), ("GUARD_ENG_2_TANK_VALVE", 0) })
            .Concat(All(0, Crossfeeds))
            .ToArray();

    /// <summary>
    /// The Parking flow's writes, the parking page's power source line and the Captain's ground power line, in its order:
    /// a single switch reads its key at the target, a multi-switch line its synthetic, the APU generator and external
    /// power their breakers closed, the power source line either of them closed (read only: the flow's power source wait
    /// completes it), the parking brake the lever set, the fuel panel the only pump on with the engine 2 feed and the
    /// crossfeeds closed. The other waits are flow-only.
    /// </summary>
    private static Group Parking()
    {
        const string G = "PARKING";
        return new Group
        {
            Id = G, Name = "Parking",
            Items = new()
            {
                Auto("PK_GROUND_LIGHTS", G, "Nose, taxi and runway turnoff lights: off", "FO_GROUND_LIGHTS_OFF", On,
                    SetAll(All(0, GroundLights))),
                Auto("PK_PARKING_BRAKE", G, "Parking brake: set", L1011Levers.ParkingBrakeKey, v => Is(v, 1),
                    Set(L1011Levers.ParkingBrakeKey, 1)),
                LeaveAlone(Auto("PK_APU_GEN", G, "APU generator: on line", "FO_APU_GEN_ON_LINE", On, Set(X.ApuGenerator, 1)),
                    ApuNotRunning, ApuGenStaysOffText),
                LeaveAlone(Auto("PK_EXT_PWR", G, "External power: on", "FO_EXT_POWER_ON", On, Set(X.ExternalPower, 1)),
                    ExtPowerNotWanted, ExtPowerStaysOffText),
                Read("PK_POWER_SOURCE", G, "APU generator or external power: on", "FO_POWER_SOURCE_ON", On),
                // An engine cut: never airborne, never unread, and never with no power source on line to take the buses
                // (the flow's power source wait, the MD-11's fuel line); on the ground with a source on line it goes ahead
                // with the engines running.
                LeaveAlone(Auto("PK_FUEL_IGN_OFF", G, "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On,
                        SetAll(All(0, FuelIgnition))),
                    FuelCutNotAllowed, FuelIgnitionStaysText),
                Auto("PK_ANTICOL", G, "Anti-collision lights: off", AntiCollision, v => Is(v, 0), Set(AntiCollision, 0)),
                Auto("PK_SEAT_BELTS", G, "Seat belt signs: off", SeatBelts, v => Is(v, 0), Set(SeatBelts, 0)),
                // The INS and the pumps: parked only, with every engine stopped (FO-25).
                LeaveAlone(Auto("PK_INS_OFF", G, "INS: all off", "FO_INS_OFF", On,
                        SetAll(All(0, "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3"))),
                    NotParked, ParkedOnlyText),
                Auto("PK_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1), Set(Transponder, 1)),
                LeaveAlone(Auto("PK_ATMS_AC_PUMPS", G, "ATMs and AC hydraulic pumps: off", "FO_ATMS_AC_PUMPS_OFF", On,
                        SetAll(All(0, AtmB, AtmC, "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3"))),
                    NotParked, ParkedOnlyText),
                LeaveAlone(Auto("PK_FUEL_PANEL", G, "Fuel pumps: off except tank 2 left for the APU", "FO_ONLY_APU_PUMP_ON", On,
                        SetAll(ApuFuelPanel), additionalFields: new[] { "FO_ENG2_FUEL_FEED", "FO_CROSSFEEDS_CLOSED" }),
                    NotParked, ParkedOnlyText),
                Auto("PK_APU_BLEED", G, "APU bleed: on", ApuBleed, v => Is(v, 1), Set(ApuBleed, 1)),
                Reminder("PK_CPT_GROUND_POWER", G, "Connect ground power on the tablet's Ground page if required"),
            }
        };
    }

    // ================================================================== Shutdown

    private const string StandbyPowerSwitch = "TOGGLE_STBY_POWER";

    /// <summary>
    /// The Shutdown flow's writes, in its order and with its labels: a single switch reads its key at the target, a
    /// multi-switch line its synthetic, the emergency lights and standby power their switch alone (the guard is left
    /// open), the APU generator trip its breaker open, the stop the APU stopped, external power its breaker open. The
    /// stop's tick holds until the APU reads stopped, a slow condition ([FO-11]).
    /// </summary>
    private static Group Shutdown()
    {
        const string G = "SHUTDOWN";
        return new Group
        {
            Id = G, Name = "Shutdown",
            Items = new()
            {
                Auto("SD_EMER_LIGHTS", G, "Emergency lights: off", "FO_EMER_LIGHTS_OFF", On,
                    SetAll(("GUARD_EMER_LIGHTING", 1), ("TOGGLE_EMER_LIGHTING", 0))),
                Auto("SD_GALLEY", G, "Galley power: off", "FO_GALLEY_OFF", On, SetAll(All(0, GalleyBuses))),
                Auto("SD_APU_GEN_TRIP", G, "APU generator breaker: trip", "FO_APU_GEN_OFF_LINE", On, Set(X.ApuGenerator, 0)),
                Auto("SD_APU_BLEED_OFF", G, "APU bleed: off", ApuBleed, v => Is(v, 0), Set(ApuBleed, 0)),
                // The composite holds until the APU reads stopped, a slow condition (FO-11): about a minute.
                Auto("SD_APU_STOP", G, "APU: stop", "FO_APU_OFF", On, Set(X.ApuStop, 1)),
                // The master only over an APU read stopped, as the flow's master waits on its stop.
                LeaveAlone(Auto("SD_APU_MASTER_OFF", G, "APU master: off", ApuMaster, v => Is(v, 0), Set(ApuMaster, 0)),
                    ApuNotStopped, ApuMasterStaysText),
                // Off in the air depressurizes the cabin: on the ground only (FO-25).
                LeaveAlone(Auto("SD_PACKS_OFF", G, "Packs: off", "FO_PACKS_OFF", On, SetAll(All(0, PackKeys))),
                    NotOnGround, PacksStayText),
                Auto("SD_EXT_PWR_OFF", G, "External power: off", "FO_EXT_POWER_OFF", On, Set(X.ExternalPower, 0)),
                // Parked only, and kept while the APU turns: the tank 2 left pump feeds it (FO-25).
                LeaveAlone(Auto("SD_FUEL_PUMPS", G, "Fuel pumps: all off", "FO_FUEL_PUMPS_OFF", On, SetAll(All(0, FuelPumps))),
                    s => NotParked(s) || s.IsOn("FO_APU_STARTING_OR_RUNNING"), ParkedApuOffText),
                LeaveAlone(Auto("SD_STBY_POWER_OFF", G, "Standby power: off", StandbyPowerSwitch, v => Is(v, 0),
                        SetAll(("GUARD_STBY_POWER", 1), (StandbyPowerSwitch, 0))),
                    NotParked, ParkedOnlyText),
                Auto("SD_RADIO_MASTERS_OFF", G, "Radio master switches: off", "FO_RADIO_MASTERS_OFF", On,
                    SetAll(All(0, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2"))),
                // Parked only, and kept while the APU turns, as the flow's battery waits on the APU master (the PMDG
                // 777's battery waits on its APU too).
                LeaveAlone(Auto("SD_BATTERY_OFF", G, "Battery: off", Battery, v => Is(v, 0), Set(Battery, 0)),
                    s => NotParked(s) || s.IsOn("FO_APU_STARTING_OR_RUNNING"), ParkedApuOffText),
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

    /// <summary>A read-only line: auto-detected, action-free, ticked by the aircraft or by the flow step that reads it
    /// (a check or a wait), never a write.</summary>
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

    /// <summary>A Captain's line; <paramref name="live"/>, when given, is the value the First Officer reads for it,
    /// spoken on a tick and shown on the status line, never in the tree (the A380's shape). <paramref name="label"/>,
    /// when given, is its flow step's short label, the reminder being longer.</summary>
    private static Item Reminder(string id, string groupId, string text, Func<L1011FoStateEvaluator, string?>? live = null,
        string? label = null) => new()
    {
        Id = id, GroupId = groupId, Label = label ?? text,
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

    /// <summary>The fuel pump lines' and Shutdown's battery line's reason: each also stays while the APU runs. Shutdown's
    /// battery step speaks it too.</summary>
    internal const string ParkedApuOffText = "Left alone: the aircraft must be on the ground with the engines stopped and the APU shut down.";

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
