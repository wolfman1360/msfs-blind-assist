using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.L1011;

using Flow = Models.FlowDefinition<L1011FoStateEvaluator>;
using Step = Models.FlowStep<L1011FoStateEvaluator>;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer flows, following the aircraft's manual checklist: Power Up,
/// then Preflight. Every switch step but the standby power check and Preflight's three tests carries a
/// SkipCondition on its own state ("Already set"; a check or a test is the point, so it always runs), and
/// the executor re-reads before acting anyway. Captain steps are spoken reminders, never automated.
///
/// Power Up is a cold-and-dark flow that switches the fuel pumps and hydraulic pumps off and, in its
/// standby power check, turns the battery off for a moment ([FO-25]): it opens with BOTH guards, engines
/// stopped and on the ground, each a read-only Stop that links no line and fails unread, so a flow started
/// with an engine turning or in flight stops whole instead of working through its steps one by one. The
/// guards stay first; the standby power check stays linked to the four lines it delivers and never to the
/// electrical meter selector lines, which are the pilot's ([FO-7]).
///
/// Every read-back (PUC_*) line the flow latches has a delivering step ([FO-6], [FO-7]): a write completes
/// its own action line and the read-back line it sets (<see cref="Also"/>), and a read-only
/// <see cref="Check"/> completes a line no write of the flow sets. Power Up writes no lever, no typed value
/// and no control whose click replay carries a toggle K: event, which the definition refuses from an
/// unknown position ([L1011-4]); the tests pin all three.
///
/// Preflight starts the APU, puts the bleed air and packs on it, runs the fire, wheel well and hydraulic
/// tests and sets the engineer's, overhead, main panel and console switches for departure. It switches the
/// packs, the AC hydraulic pumps and the power transfer units off on the way, so it opens with the same two
/// guards ([FO-25]); its packs step is also left alone off the ground, as its tick is. Its toggle-carrying
/// writes (APU bleed, anti-skid, no smoking) are single switches whose "Already set" reads their own
/// position: the executor reads first, and from an unknown position the definition refuses the write.
/// </summary>
public static class L1011FoFlowDefinitions
{
    public static List<Flow> Build() => new()
    {
        BuildPowerUp(),
        BuildPreflight(),
    };

    // ------------------------------------------------------------------ Power Up (the manual's cockpit entry and power-up)
    private static Flow BuildPowerUp() => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Description = "Fire handle, gear, speed brake, flap and fuel checks; radios, transponder, radar, pumps, jettison, "
            + "passenger oxygen and galley switches; standby power check; essential bus selector; external power; INS align.",
        RelatedChecklistGroupIds = new[] { "POWER_UP", "POWER_UP_CL" },
        Steps = new()
        {
            // A cold-and-dark flow: started with an engine turning, the next steps would switch pumps off
            // under it. Stop instead...
            EnginesStoppedGuard("PU_ENGINES_STOPPED"),
            // ...and never airborne (FO-25). Links no line.
            OnGroundGuard("PU_ON_GROUND"),

            // What the First Officer finds and never moves: read only. Each completes only its read-back line.
            Check("PU_CHK_FIRE_HANDLES", "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", On, CheckSeconds, "PUC_FIRE_HANDLES"),
            Check("PU_CHK_GEAR", "Gear lever: down", "FO_GEAR_LEVER_DOWN", On, CheckSeconds, "PUC_GEAR"),
            Check("PU_CHK_SPEED_BRAKE", "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", On, CheckSeconds, "PUC_SPEED_BRAKE"),
            Check("PU_CHK_FLAPS", "Flaps and flap handle: agree", "FO_FLAPS_AGREE", On, CheckSeconds, "PUC_FLAPS"),
            Check("PU_CHK_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On, CheckSeconds, "PUC_FUEL_IGN"),

            Also(Done(Skip(SW("PU_CONT_IGN", "Continuous ignition: off", "SWITCH_CONT_IGNITION", 0),
                s => s.IsPosition("SWITCH_CONT_IGNITION", 0)), "PU_CONT_IGN"), "PUC_CONT_IGN"),
            Also(Done(Skip(Multi("PU_RADIO_MASTERS", "Radio master switches: on",
                    ("SWITCH_MASTER_RADIO_ESS", 1), ("SWITCH_MASTER_RADIO_NO2", 1)),
                s => s.IsOn("FO_RADIO_MASTERS_ON")), "PU_RADIO_MASTERS"), "PUC_RADIO_MASTERS"),
            Also(Done(Skip(SW("PU_XPDR", "Transponder: standby", "ROTARY_TCAS_MODE", 1),
                s => s.IsPosition("ROTARY_TCAS_MODE", 1)), "PU_XPDR"), "PUC_XPDR"),
            Also(Done(Skip(SW("PU_RADAR", "Weather radar: off", "SWITCH_WX_OFF", 1),
                s => s.IsPosition("SWITCH_WX_OFF", 1)), "PU_RADAR"), "PUC_RADAR"),
            Also(Done(Skip(Multi("PU_ATMS", "ATM switches: off", ("ROTARY_HYD_B2_MODE", 0), ("ROTARY_HYD_C2_MODE", 0)),
                s => s.IsOn("FO_ATMS_OFF")), "PU_ATMS"), "PUC_ATMS"),
            Also(Done(Skip(Multi("PU_AC_PUMPS", "AC hydraulic pumps: off", ("SWITCH_HYD_AC_PUMP_B3", 0), ("SWITCH_HYD_AC_PUMP_C3", 0)),
                s => s.IsOn("FO_AC_PUMPS_OFF")), "PU_AC_PUMPS"), "PUC_AC_PUMPS"),
            // The tank 2 left pump feeds the APU from the moment it starts turning, not only once it is up to
            // speed (about 50 s after the start press). An APU the pilot started by hand, running or still
            // spooling up, keeps the pumps as they are: the step is left alone, aloud, and its lines stay
            // out of the latch. An unread APU speed does not hold the step back.
            Also(Done(Skip(LeaveAlone(Multi("PU_FUEL_PUMPS", "Fuel pumps: all off",
                        ("SWITCH_FUEL_PUMP_1_1", 0), ("SWITCH_FUEL_PUMP_1_2", 0),
                        ("SWITCH_FUEL_PUMP_2L_1", 0), ("SWITCH_FUEL_PUMP_2L_2", 0),
                        ("SWITCH_FUEL_PUMP_2R_1", 0), ("SWITCH_FUEL_PUMP_2R_2", 0),
                        ("SWITCH_FUEL_PUMP_3_1", 0), ("SWITCH_FUEL_PUMP_3_2", 0)),
                    s => s.IsOn("FO_APU_STARTING_OR_RUNNING"), "Fuel pumps left on: the APU is running."),
                s => s.IsOn("FO_FUEL_PUMPS_OFF")), "PU_FUEL_PUMPS"), "PUC_FUEL_PUMPS"),
            // The master and the four dump switches off, then their five guards closed: the keys
            // FO_JETTISON_SAFE reads, in its order.
            Also(Done(Skip(Multi("PU_JETTISON", "Jettison switches: off and guarded",
                    ("SWITCH_FUEL_DUMP_MASTER", 0), ("SWITCH_FUEL_DUMP_2L", 0), ("SWITCH_FUEL_DUMP_1", 0),
                    ("SWITCH_FUEL_DUMP_3", 0), ("SWITCH_FUEL_DUMP_2R", 0),
                    ("GUARD_FUEL_DUMP_MASTER", 0), ("GUARD_FUEL_DUMP_2L", 0), ("GUARD_FUEL_DUMP_1", 0),
                    ("GUARD_FUEL_DUMP_3", 0), ("GUARD_FUEL_DUMP_2R", 0)),
                s => s.IsOn("FO_JETTISON_SAFE")), "PU_JETTISON"), "PUC_JETTISON"),
            Also(Done(Skip(Multi("PU_PAX_OXY", "Passenger oxygen: off and guarded",
                    ("SWITCH_PAX_OXY_MANUAL", 0), ("GUARD_PAX_OXY_MANUAL", 0)),
                s => s.IsOn("FO_PAX_OXY_SAFE")), "PU_PAX_OXY"), "PUC_PAX_OXY"),
            // The aircraft's cockpit safety inspection has no galley line, so POWER_UP_CL has no PUC_GALLEY
            // (Ruling R3): this step completes its action line only.
            Done(Skip(Multi("PU_GALLEY", "Galley power: off",
                    ("SWITCH_GALLEY_BUS_1", 0), ("SWITCH_GALLEY_BUS_2", 0), ("SWITCH_GALLEY_BUS_3", 0)),
                s => s.IsOn("FO_GALLEY_OFF")), "PU_GALLEY"),

            // The aircraft's checklist lines 18 to 32 in one composite: battery off, standby power On, the
            // lights read, battery on, standby power armed, the lights read again. No SkipCondition: the check
            // is the point. Linked to the four lines it delivers and nothing else (FO-7, never the meter selectors).
            Also(Done(SW("PU_STBY_CHECK", "Standby power check", X.StandbyPowerCheck, 1), "PU_STBY_CHECK"),
                "PUC_STBY_CHECK", "PUC_BATTERY", "PUC_STBY_ARMED"),
            Also(Done(Skip(SW("PU_AC_ESS", "AC essential bus selector: bus 3, generator 1", "ROTARY_AC_ESS_BUS", 4),
                s => s.IsPosition("ROTARY_AC_ESS_BUS", 4)), "PU_AC_ESS"), "PUC_AC_ESS"),
            Done(Skip(SW("PU_EXT_PWR", "External power: on", X.ExternalPower, 1), s => s.IsOn("FO_EXT_POWER_ON")), "PU_EXT_PWR"),
            // A skipped step is announced "Already set: <label>", so the label is a short noun phrase and
            // the instruction lives in the reminder text.
            Skip(Captain("PU_EXT_PWR_CPT", "Ground power",
                    "Connect ground power on the tablet's Ground page, or the Preflight flow starts the APU."),
                s => s.IsOn("FO_EXT_POWER_ON")),
            // One composite that goes unit by unit: a unit already at ALIGN or NAV is never rewritten.
            Also(Done(Skip(SW("PU_INS_ALIGN", "INS 1, 2 and 3: align", X.InsAlign, 1),
                s => s.IsOn("FO_INS_ALIGN_OR_NAV")), "PU_INS_ALIGN"), "PUC_INS_ALIGN"),
            Captain("PU_INS_POSITION", "Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit."),
        }
    };

    // ------------------------------------------------------------------ Preflight (the engineer's station, overhead, main panel, console)

    private static readonly string[] PackKeys = { "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3" };

    /// <summary>The hot air valves of engines 1 and 3 and the two hot air isolation valves, the keys
    /// <c>FO_HOT_AIR_OFF</c> and <c>FO_HOT_AIR_ON</c> read.</summary>
    private static readonly string[] HotAirKeys =
        { "SWITCH_HOT_AIR_ENG_1", "SWITCH_HOT_AIR_ENG_3", "SWITCH_ISLN_VALVE_OVHT_2A", "SWITCH_ISLN_VALVE_OVHT_2B" };

    /// <summary>The 19 primary flight control switches, in the order <c>FO_PFCS_ON</c> reads them.</summary>
    private static readonly string[] PfcsKeys =
    {
        "SWITCH_PFCS_PITCH_1", "SWITCH_PFCS_PITCH_2", "SWITCH_PFCS_ROLL_1", "SWITCH_PFCS_ROLL_2", "SWITCH_PFCS_RUDDER",
        "SWITCH_STABILIZER_A", "SWITCH_STABILIZER_B", "SWITCH_STABILIZER_C", "SWITCH_STABILIZER_D",
        "SWITCH_AILERON_L_OUTBD", "SWITCH_AILERON_L_INBD", "SWITCH_AILERON_R_INBD", "SWITCH_AILERON_R_OUTBD",
        "SWITCH_SPOILER_L56", "SWITCH_SPOILER_LR4", "SWITCH_SPOILER_LR1", "SWITCH_SPOILER_LR2", "SWITCH_SPOILER_LR3",
        "SWITCH_SPOILER_R56",
    };

    /// <summary>The 14 flight control electronics switches, in the order <c>FO_FCES_ON</c> reads them.</summary>
    private static readonly string[] FcesKeys =
    {
        "SWITCH_ACS_1", "SWITCH_ACS_2", "SWITCH_YAW_SAS_1", "SWITCH_YAW_SAS_2", "SWITCH_STALL_WARN_1", "SWITCH_STALL_WARN_2",
        "SWITCH_PITCH_TRIM_1", "SWITCH_PITCH_TRIM_2", "SWITCH_DLC_AUTO_1", "SWITCH_DLC_AUTO_2", "SWITCH_ATS_1", "SWITCH_ATS_2",
        "SWITCH_MACH_TRIM_1", "SWITCH_MACH_TRIM_2",
    };

    /// <summary>The captain's six instrument source selectors, then the first officer's, in the order
    /// <c>FO_INSTR_SOURCES_NORMAL</c> reads them.</summary>
    private static readonly string[] InstrumentSourceKeys =
        new[] { "NAV", "HDG", "ATT", "ALT", "IAS", "FLT_DIR" }.Select(s => "SWITCH_INSTR_SRC_CPT_" + s)
            .Concat(new[] { "NAV", "HDG", "ATT", "ALT", "IAS", "FLT_DIR" }.Select(s => "SWITCH_INSTR_SRC_FO_" + s))
            .ToArray();

    /// <summary>The AC essential power selector's normal position: bus 3 (generator 1).</summary>
    private const int AcEssNormal = 4;

    private static (string EventName, int? TargetValue)[] AllTo(int target, params string[] keys)
        => keys.Select(k => (k, (int?)target)).ToArray();

    private static Flow BuildPreflight() => new()
    {
        Id = "PREFLIGHT", Name = "Preflight",
        Description = "APU start and generator, bleed air and packs, fire, wheel well and hydraulic tests, fuel, "
            + "pressurization, overhead, main panel and console set for departure.",
        RelatedChecklistGroupIds = new[] { "PREFLIGHT", "PREFLIGHT_FE_CL", "PREFLIGHT_OVHD_CL", "PREFLIGHT_MAIN_CL", "PREFLIGHT_CONSOLE_CL" },
        Steps = new()
        {
            // The flow switches the packs, the AC hydraulic pumps and the power transfer units off: never with
            // an engine turning, never airborne (FO-25). Each guard links no line.
            EnginesStoppedGuard("PF_ENGINES_STOPPED"),
            OnGroundGuard("PF_ON_GROUND"),

            // ---- the APU: master, minimum compressor load, fuel, start, generator, ties ----
            Also(Done(Skip(SW("PF_APU_MASTER", "APU master: on", "TOGGLE_APU_POWER", 1),
                s => s.IsPosition("TOGGLE_APU_POWER", 1)), "PF_APU_MASTER"), "PFC_APU_MASTER"),
            Done(Skip(SW("PF_APU_MODE_MIN", "APU compressor mode: minimum", "SWITCH_APU_MODE", 0),
                s => s.IsPosition("SWITCH_APU_MODE", 0)), "PF_APU_MODE_MIN"),
            Also(Done(Skip(Multi("PF_APU_FUEL", "APU fuel: tank 2 left pump and engine 2 tank valve on",
                    ("SWITCH_FUEL_PUMP_2L_1", 1), ("SWITCH_ENG_2_TANK_VALVE", 1), ("GUARD_ENG_2_TANK_VALVE", 0)),
                s => s.IsOn("FO_ENG2_FUEL_FEED")), "PF_APU_FUEL"), "PFC_APU_FUEL"),
            // One press, done once the APU turns: a press while it already turns would be a second start.
            Done(Skip(SW("PF_APU_START", "APU: start", X.ApuStart, 1), s => s.IsOn("FO_APU_STARTING_OR_RUNNING")), "PF_APU_START"),
            // About 50 s from the press to 100 %. An APU that never runs stops the flow: everything after it
            // runs on its air and its generator.
            Done(WaitForField("PF_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, ApuRunningSeconds,
                onTimeout: FlowStepFailurePolicy.Stop), "PFC_APU_START"),
            Also(Done(Skip(SW("PF_APU_GEN", "APU generator: on line", X.ApuGenerator, 1), s => s.IsOn("FO_APU_GEN_CLOSED")),
                "PF_APU_GEN"), "PFC_APU_GEN"),
            Also(Done(Skip(SW("PF_AC_TIES", "AC bus ties: closed", X.AcTies, 1), s => s.IsOn("FO_AC_TIES_CLOSED")),
                "PF_AC_TIES"), "PFC_AC_TIES"),

            // ---- bleed air onto the APU: the packs off while the valves are set, then on. The packs off, the hot
            // air off and the APU's minimum compressor mode are undone later in this flow, so their action lines are
            // ticked by these steps and read no state.
            Also(Done(Skip(Multi("PF_APU_AIR", "Fuselage isolation and crossbleed valves: on",
                    ("SWITCH_APU_ISLN_VALVE", 1), ("SWITCH_XBLEED_1", 1), ("SWITCH_XBLEED_3", 1)),
                s => s.IsOn("FO_APU_AIR_ON")), "PF_APU_AIR"), "PFC_FUSELAGE_ISLN", "PFC_XBLEEDS"),
            // Behind the ground guard already; left alone off the ground anyway, as its tick is.
            Done(Skip(LeaveAlone(Multi("PF_PACKS_OFF", "Packs: off", AllTo(0, PackKeys)),
                    L1011FoChecklistDefinitions.NotOnGround, L1011FoChecklistDefinitions.PacksStayText),
                s => s.IsOn("FO_PACKS_OFF")), "PF_PACKS_OFF"),
            Also(Done(Skip(Multi("PF_PACKS_AUTO", "Pack control: auto",
                    AllTo(1, "SWITCH_BLEED_PLENUM_ENG_1", "SWITCH_BLEED_PLENUM_ENG_2", "SWITCH_BLEED_PLENUM_ENG_3")),
                s => s.IsOn("FO_PACKS_AUTO")), "PF_PACKS_AUTO"), "PFC_PACK_AUTO"),
            Done(Skip(Multi("PF_HOT_AIR_OFF", "Hot air and hot air isolation valves: off", AllTo(0, HotAirKeys)),
                s => s.IsOn("FO_HOT_AIR_OFF")), "PF_HOT_AIR_OFF"),
            Also(Done(Skip(Multi("PF_ZONE_TRIMS", "Zone trim switches: on",
                    AllTo(1, "SWITCH_FLT_STA_CLOSE", "SWITCH_FWD_CAB_CLOSE", "SWITCH_MID_CAB_CLOSE", "SWITCH_AFT_CAB_CLOSE")),
                s => s.IsOn("FO_ZONE_TRIMS_ON")), "PF_ZONE_TRIMS"), "PFC_ZONE_TRIMS"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("PF_APU_BLEED", "APU bleed: on", "SWITCH_APU_BLEED", 1),
                s => s.IsPosition("SWITCH_APU_BLEED", 1)), "PF_APU_BLEED"), "PFC_APU_BLEED"),
            Also(Done(Skip(Multi("PF_PACKS_ON", "Packs: on", AllTo(1, PackKeys)),
                s => s.IsOn("FO_PACKS_ON")), "PF_PACKS_ON"), "PFC_PACKS"),
            Also(Done(Skip(SW("PF_APU_MODE_NORM", "APU compressor mode: normal", "SWITCH_APU_MODE", 1),
                s => s.IsPosition("SWITCH_APU_MODE", 1)), "PF_APU_MODE_NORM"), "PFC_APU_MODE_NORM"),
            Also(Done(Skip(Multi("PF_HOT_AIR_ON", "Hot air and hot air isolation valves: on", AllTo(1, HotAirKeys)),
                s => s.IsOn("FO_HOT_AIR_ON")), "PF_HOT_AIR_ON"), "PFC_HOT_AIR"),

            // ---- the tests: each composite speaks one verdict and puts every switch back; a failed test
            // never stops the flow (Skip). No SkipCondition: the test is the point. The fire test leaves the
            // loop selectors at Both, which its read-back line reads.
            Also(Done(SW("PF_FIRE_TEST", "Engine and APU fire test", X.FireTest, 1), "PF_FIRE_TEST"),
                "PFC_FIRE_TEST", "PFC_LOOP_SELECTORS"),
            Also(Done(SW("PF_WHEEL_WELL_TEST", "Wheel well fire test", X.WheelWellFireTest, 1), "PF_WHEEL_WELL_TEST"),
                "PFC_WHEEL_WELL_TEST"),

            // ---- hydraulics ----
            Also(Done(Skip(Multi("PF_HYD_ENGINE_PUMPS", "Engine hydraulic pumps: on and guarded",
                    AllTo(1, "SWITCH_HYD_PUMP_A1", "SWITCH_HYD_PUMP_B1", "SWITCH_HYD_PUMP_C1", "SWITCH_HYD_PUMP_D1")
                        .Concat(AllTo(0, "GUARD_HYD_PUMP_A1", "GUARD_HYD_PUMP_B1", "GUARD_HYD_PUMP_C1", "GUARD_HYD_PUMP_D1")).ToArray()),
                s => s.IsOn("FO_HYD_ENGINE_PUMPS_ON")), "PF_HYD_ENGINE_PUMPS"), "PFC_HYD_ENGINE_PUMPS"),
            // Power Up left the ATMs off: read only.
            Check("PF_CHK_ATMS", "ATM switches: off", "FO_ATMS_OFF", On, CheckSeconds, "PFC_ATMS"),
            Also(Done(Skip(Multi("PF_AC_PUMPS_PTUS_OFF", "AC hydraulic pumps and PTUs: off",
                    AllTo(0, "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3", "SWITCH_PTU_AB", "SWITCH_PTU_CD")),
                s => s.IsOn("FO_AC_PUMPS_OFF") && s.IsOn("FO_PTUS_OFF")), "PF_AC_PUMPS_PTUS_OFF"), "PFC_PTUS"),
            Also(Done(SW("PF_HYD_CHECK", "Hydraulic check", X.HydraulicCheck, 1), "PF_HYD_CHECK"), "PFC_HYD_CHECK"),

            // ---- electrical meters ----
            Also(Done(Skip(SW("PF_AC_METER", "AC meter: APU generator", "ROTARY_PWR_SOURCE", 4),
                s => s.IsPosition("ROTARY_PWR_SOURCE", 4)), "PF_AC_METER"), "PFC_AC_METER"),
            Also(Done(Skip(SW("PF_DC_METER", "DC meter: battery", "ROTARY_STBY_DC_BUS", 4),
                s => s.IsPosition("ROTARY_STBY_DC_BUS", 4)), "PF_DC_METER"), "PFC_DC_METER"),
            // Power Up set it: read only.
            Check("PF_CHK_AC_ESS", "AC essential power selector: bus 3, generator 1", "ROTARY_AC_ESS_BUS",
                v => Math.Abs(v - AcEssNormal) < 0.5, CheckSeconds, "PFC_AC_ESS"),

            // ---- fuel ----
            Also(Done(Skip(Multi("PF_TRANSFERS_OFF", "1A and 3A transfers: off", AllTo(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
                s => s.IsOn("FO_TRANSFERS_OFF")), "PF_TRANSFERS_OFF"), "PFC_TRANSFERS"),
            Also(Done(Skip(Multi("PF_CROSSFEEDS", "Crossfeed valves: closed",
                    AllTo(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED")),
                s => s.IsOn("FO_CROSSFEEDS_CLOSED")), "PF_CROSSFEEDS"), "PFC_CROSSFEEDS"),
            Also(Done(Skip(Multi("PF_TANK_VALVES", "Engine tank valves: on and guarded",
                    AllTo(1, "SWITCH_ENG_1_TANK_VALVE", "SWITCH_ENG_2_TANK_VALVE", "SWITCH_ENG_3_TANK_VALVE")
                        .Concat(AllTo(0, "GUARD_ENG_1_TANK_VALVE", "GUARD_ENG_2_TANK_VALVE", "GUARD_ENG_3_TANK_VALVE")).ToArray()),
                s => s.IsOn("FO_TANK_VALVES_ON")), "PF_TANK_VALVES"), "PFC_TANK_VALVES"),
            // Only the pump the APU needs: read only.
            Check("PF_CHK_APU_PUMP", "Fuel pumps: tank 2 left pump only", "FO_ONLY_APU_PUMP_ON", On, CheckSeconds, "PFC_APU_PUMP_ONLY"),
            Check("PF_CHK_JETTISON", "Jettison switches: off and guarded", "FO_JETTISON_SAFE", On, CheckSeconds, "PFC_JETTISON"),
            Also(Done(Skip(Multi("PF_FUEL_CTRL", "Fuel control amplifier switches: off",
                    AllTo(0, "SWITCH_FUEL_CTRL_OVRD_ENG_1", "SWITCH_FUEL_CTRL_OVRD_ENG_2", "SWITCH_FUEL_CTRL_OVRD_ENG_3")),
                s => s.IsOn("FO_FUEL_CTRL_OVRD_OFF")), "PF_FUEL_CTRL"), "PFC_FUEL_CTRL"),

            // ---- the rest of the engineer's station. The drain mast heaters are left to the aircraft: it puts both
            // switches back to off within seconds, by hand too, so a step could never be done.
            Also(Done(Skip(Multi("PF_MOVIE", "Movie interrupt: off and guarded", AllTo(0, "MOVIES_OFF", "GUARD_MOVIES_OFF")),
                s => s.IsOn("FO_MOVIE_OFF")), "PF_MOVIE"), "PFC_MOVIE"),
            Also(Done(Skip(SW("PF_SLAT_MONITOR", "Slat monitor: off", "SWITCH_SLAT_MONITOR", 0),
                s => s.IsPosition("SWITCH_SLAT_MONITOR", 0)), "PF_SLAT_MONITOR"), "PFC_SLAT_MONITOR"),
            Also(Done(Skip(Multi("PF_HI_PRESS", "High pressure bleed valves: on",
                    AllTo(1, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3")),
                s => s.IsOn("FO_HI_PRESS_ON")), "PF_HI_PRESS"), "PFC_HI_PRESS"),
            Also(Done(Skip(Multi("PF_ISLN_VALVES", "Engine isolation valves: on",
                    AllTo(1, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3")),
                s => s.IsOn("FO_ISLN_VALVES_ON")), "PF_ISLN_VALVES"), "PFC_ISLN_VALVES"),
            Also(Done(Skip(Multi("PF_ECS", "Overboard cooling and cargo heat: on",
                    AllTo(1, "SWITCH_COOL_AIR_OVBD", "SWITCH_CARGO_HEAT_FWD", "SWITCH_CARGO_HEAT_MID", "SWITCH_CARGO_HEAT_AFT")),
                s => s.IsOn("FO_ECS_ON")), "PF_ECS"), "PFC_ECS"),
            Also(Done(Skip(SW("PF_PRESS_MODE", "Pressurization mode: normal", "ROTARY_CAB_PRESS_MODE", 1),
                s => s.IsPosition("ROTARY_CAB_PRESS_MODE", 1)), "PF_PRESS_MODE"), "PFC_PRESS_MODE"),
            // The cabin altitude that holds the maximum differential at the plan's cruise. No plan: nothing is
            // sent or spoken (FO-20), and the Captain step below asks for it.
            Also(Done(Skip(Provider("PF_CABIN_ALT", "Cabin altitude selector: cruise cabin altitude", X.CabinAltitudeSelect,
                    s => s.CruiseCabinAltitudeFt()),
                s => s.IsOn("FO_CABIN_ALT_AT_CRUISE")), "PF_CABIN_ALT"), "PFC_CABIN_ALT"),
            // Keyed on the selector, never on the plan: without a plan, or with a write that did not take, the
            // selector does not read the cruise setting and the Captain is asked.
            Skip(Captain("PF_CABIN_ALT_CPT", "Cabin altitude selector",
                    "Set the cabin altitude selector for the cruise on the engineer's pressurization panel."),
                s => s.IsOn("FO_CABIN_ALT_AT_CRUISE")),

            // ---- overhead ----
            Also(Done(Skip(Multi("PF_COMPASSES", "Compasses: magnetic", AllTo(1, "SWITCH_COMPASS_1_MAG", "SWITCH_COMPASS_2_MAG")),
                s => s.IsOn("FO_COMPASSES_MAG")), "PF_COMPASSES"), "PFO_COMPASSES"),
            Also(Done(Skip(Multi("PF_WIPERS", "Wipers: off", AllTo(0, "ROTARY_CPT_WIPER", "ROTARY_FO_WIPER")),
                s => s.IsOn("FO_WIPERS_OFF")), "PF_WIPERS"), "PFO_WIPERS"),
            Also(Done(Skip(Multi("PF_ANTI_ICE", "Engine and wing anti-ice: off",
                    AllTo(0, "SWITCH_ANTI_ICE_ENG_1", "SWITCH_ANTI_ICE_ENG_2", "SWITCH_ANTI_ICE_ENG_3", "SWITCH_ANTI_ICE_LEFT_AUTO",
                        "SWITCH_ANTI_ICE_LEFT_MAN", "SWITCH_ANTI_ICE_RIGHT_AUTO", "SWITCH_ANTI_ICE_RIGHT_MAN")),
                s => s.IsOn("FO_ANTI_ICE_OFF")), "PF_ANTI_ICE"), "PFO_ANTI_ICE"),
            Also(Done(Skip(Multi("PF_WINDSHIELD", "Windshield heat: normal", AllTo(1, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO")),
                s => s.IsOn("FO_WINDSHIELD_NORMAL")), "PF_WINDSHIELD"), "PFO_WINDSHIELD"),
            Also(Done(Skip(Multi("PF_SIDE_WINDOWS", "Side window heat: on",
                    AllTo(1, "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR")),
                s => s.IsOn("FO_SIDE_WINDOWS_ON")), "PF_SIDE_WINDOWS"), "PFO_SIDE_WINDOWS"),
            Also(Done(Skip(Multi("PF_AIR_DATA", "Air data heat: off",
                    AllTo(0, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT")),
                s => s.IsOn("FO_AIR_DATA_HEAT_OFF")), "PF_AIR_DATA"), "PFO_AIR_DATA"),
            Also(Done(Skip(Multi("PF_MACH_FEEL", "Mach feel and rudder limiter: on",
                    AllTo(1, "SWITCH_MACH_FEEL_1", "SWITCH_MACH_FEEL_2", "SWITCH_RUDDER_LIMITER")),
                s => s.IsOn("FO_MACH_FEEL_RUDDER_ON")), "PF_MACH_FEEL"), "PFO_MACH_FEEL"),
            // System A or B is already set: the selector is turned to A only off both systems.
            Also(Done(Skip(SW("PF_IGNITION", "Ignition selector: A or B", "ROTARY_ENG_START_SEL", 0),
                s => s.IsOn("FO_IGNITION_SELECTED")), "PF_IGNITION"), "PFO_IGNITION"),
            Also(Done(Skip(SW("PF_STBY_LIGHTS", "Instrument standby lights: bright", "TOGGLE_INSTR_STBY_LIGHTS", 1),
                s => s.IsPosition("TOGGLE_INSTR_STBY_LIGHTS", 1)), "PF_STBY_LIGHTS"), "PFO_STBY_LIGHTS"),
            Also(Done(Skip(Multi("PF_PFCS", "Primary flight control switches: on", AllTo(1, PfcsKeys)),
                s => s.IsOn("FO_PFCS_ON")), "PF_PFCS"), "PFO_PFCS"),
            Also(Done(Skip(Multi("PF_FCES", "Flight control electronics switches: on", AllTo(1, FcesKeys)),
                s => s.IsOn("FO_FCES_ON")), "PF_FCES"), "PFO_FCES"),
            Also(Done(Skip(SW("PF_BRAKE_SEL", "Brake system: B", "TOGGLE_BRAKE_SYS_SEL", 0),
                s => s.IsPosition("TOGGLE_BRAKE_SYS_SEL", 0)), "PF_BRAKE_SEL"), "PFO_BRAKE_SEL", "PFM_BRAKE_SYS"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("PF_ANTISKID_OFF", "Anti-skid: off", "SWITCH_ANTISKID_ON", 0),
                s => s.IsPosition("SWITCH_ANTISKID_ON", 0)), "PF_ANTISKID_OFF"), "PFO_ANTISKID"),
            Also(Done(Skip(SW("PF_PARKING_BRAKE", "Parking brake: set", L1011Levers.ParkingBrakeKey, 1),
                s => s.IsPosition(L1011Levers.ParkingBrakeKey, 1)), "PF_PARKING_BRAKE"), "PFO_PARKING_BRAKE", "PFX_PARKING_BRAKE"),
            Also(Done(Skip(SW("PF_AUTOBRAKE", "Autobrake: off", "ROTARY_AUTO_BRAKE_MODE", 1),
                s => s.IsPosition("ROTARY_AUTO_BRAKE_MODE", 1)), "PF_AUTOBRAKE"), "PFO_AUTOBRAKE"),
            // The wing (flood) and wheel well lights read 1 OFF.
            Also(Done(Skip(Multi("PF_EXT_LIGHTS", "Exterior lights: logo and position on, the rest off",
                    ("TOGGLE_LOGO_LIGHT", 1), ("SWITCH_POSITION_LIGHT", 1),
                    ("TOGGLE_LANDING_LEFT", 0), ("TOGGLE_LANDING_RIGHT", 0), ("TOGGLE_LANDING_NOSE", 0), ("TOGGLE_RWY_TURNOFF", 0),
                    ("SWITCH_STROBE_LIGHT", 0), ("SWITCH_ANTICOL_LIGHT", 0),
                    ("SWITCH_FLOOD_LIGHT", 1), ("SWITCH_WHEEL_LIGHT", 1)),
                s => s.IsOn("FO_EXT_LIGHTS_PREFLIGHT")), "PF_EXT_LIGHTS"), "PFO_EXT_LIGHTS"),
            Also(Done(Skip(Multi("PF_EMER_LIGHTS", "Emergency lights: armed", ("TOGGLE_EMER_LIGHTING", 1), ("GUARD_EMER_LIGHTING", 0)),
                s => s.IsOn("FO_EMER_LIGHTS_ARMED")), "PF_EMER_LIGHTS"), "PFO_EMER_LIGHTS"),
            // Power Up's standby power check left it armed: read only.
            Check("PF_CHK_STBY_ARMED", "Standby power: armed", "FO_STBY_ARMED", On, CheckSeconds, "PFO_STBY_POWER"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("PF_NO_SMOKING", "No smoking signs: on", "SWITCH_SMOKING_LIGHT", 1),
                s => s.IsPosition("SWITCH_SMOKING_LIGHT", 1)), "PF_NO_SMOKING"), "PFO_NO_SMOKING"),

            // ---- main panel ----
            Also(Done(Skip(Multi("PF_INSTR_SOURCES", "Instrument source selectors: normal", AllTo(0, InstrumentSourceKeys)),
                s => s.IsOn("FO_INSTR_SOURCES_NORMAL")), "PF_INSTR_SOURCES"), "PFM_INSTR_SOURCE"),
            Also(Done(Skip(Multi("PF_DME", "DME 1 and 2: normal", AllTo(1, "ROTARY_NAV_1_MODE", "ROTARY_NAV_2_MODE")),
                s => s.IsOn("FO_DME_NORMAL")), "PF_DME"), "PFM_DME"),
            Also(Done(Skip(Multi("PF_FLIGHT_DIRECTORS", "Flight directors: on", AllTo(1, "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B")),
                s => s.IsOn("FO_FLIGHT_DIRECTORS_ON")), "PF_FLIGHT_DIRECTORS"), "PFM_FLIGHT_DIRECTORS"),
            // An engage paddle reads 2 OFF.
            Also(Done(Skip(Multi("PF_PADDLES_OFF", "Autopilot paddles: off", AllTo(2, "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B")),
                s => s.IsOn("FO_PADDLES_OFF")), "PF_PADDLES_OFF"), "PFM_AUTOPILOT_MASTERS"),

            // ---- centre console ----
            Check("PF_CHK_DISCONNECT", "Pitch and roll disconnect handles: stowed", "FO_DISCONNECT_HANDLES_STOWED", On,
                CheckSeconds, "PFX_DISCONNECT"),
            Also(Done(Skip(SW("PF_SPEED_BRAKE", "Speed brake lever: forward", L1011Levers.SpeedBrakeKey, 0),
                s => s.IsOn("FO_SPEED_BRAKE_FORWARD")), "PF_SPEED_BRAKE"), "PFX_SPEED_BRAKE"),
            Check("PF_CHK_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On, CheckSeconds, "PFX_FUEL_IGN"),
            Also(Done(Skip(SW("PF_XPDR", "Transponder: standby", "ROTARY_TCAS_MODE", 1),
                s => s.IsPosition("ROTARY_TCAS_MODE", 1)), "PF_XPDR"), "PFX_XPDR"),

            // ---- what Power Up left, read only, each completing only its read-back line ----
            Check("PF_CHK_INS", "INS 1, 2 and 3: aligning or navigating", "FO_INS_ALIGN_OR_NAV", On, CheckSeconds, "PFO_INS"),
            Check("PF_CHK_FIRE_HANDLES", "Fire handles: in", "FO_FIRE_HANDLES_IN", On, CheckSeconds, "PFO_FIRE_HANDLES"),
            Check("PF_CHK_RADIO_MASTERS", "Radio master switches: on", "FO_RADIO_MASTERS_ON", On, CheckSeconds, "PFO_RADIO_MASTERS"),
            Check("PF_CHK_START_LIGHTS", "Engine start lights: out", "FO_START_LIGHTS_OUT", On, CheckSeconds, "PFO_START_LIGHTS"),
            // The lever down and every leg out: read only, never a gear write.
            Check("PF_CHK_GEAR", "Landing gear: down, three green", L1011GearConfirmation.DownField, On, CheckSeconds, "PFM_GEAR"),

            // ---- the Captain's: each ticks its own reminder line ----
            Done(Captain("PF_CPT_INS_ROUTE", "Load the route and set the initial track on INS 1, 2 and 3"), "PF_CPT_INS_ROUTE"),
            Done(Captain("PF_CPT_PMS", "Set the PMS"), "PF_CPT_PMS"),
            Done(Captain("PF_CPT_NAV", "Set NAV 1 and 2 and the courses: Ctrl+N"), "PF_CPT_NAV"),
            Done(Captain("PF_CPT_ALTIMETERS", "Set the altimeters to QNH: Ctrl+B"), "PF_CPT_ALTIMETERS"),
            Done(Captain("PF_CPT_AFCS", "Set the speed, heading and altitude on the autopilot"), "PF_CPT_AFCS"),
            Done(Captain("PF_CPT_TAKEOFF_DATA", "Enter the take-off data on the tablet"), "PF_CPT_TAKEOFF_DATA"),
            Done(Captain("PF_CPT_RADIOS", "Set the radios"), "PF_CPT_RADIOS"),
        }
    };

    // ------------------------------------------------------------------ step builders (the MD-11's, retyped)

    /// <summary>A read-only check of a switch the flow finds: 5 s, then skipped aloud.</summary>
    private const int CheckSeconds = 5;

    /// <summary>The read-only gear checks (FO-13): 20 s, then skipped aloud.</summary>
    private const int GearCheckSeconds = 20;

    /// <summary>How long a flow waits for the Captain's flaps to come up after take-off.</summary>
    private const int FlapsUpCheckSeconds = 300;

    /// <summary>How long Preflight waits for the APU to run once it turns: it reaches 100 % about 50 s after
    /// its start press.</summary>
    private const int ApuRunningSeconds = 120;

    private static bool On(double v) => v > 0.5;

    /// <summary>
    /// A read-only check (FO-6, FO-7): "Already set" when <paramref name="field"/> already satisfies
    /// <paramref name="ok"/>, else a wait of <paramref name="timeoutSec"/> that is skipped aloud. It
    /// never writes, and it completes the read-back lines that read the same state, so a line no
    /// write of the flow sets is latched only when the aircraft shows it; a timed-out check keeps
    /// its lines out of the latch, mirroring the aircraft.
    /// </summary>
    private static Step Check(string id, string label, string field, Func<double, bool> ok, int timeoutSec,
        string readBackItemId, params string[] moreReadBackItemIds)
        => Also(Done(Skip(WaitForField(id, label, field, ok, timeoutSec), s => ok(s.GetValue(field))), readBackItemId),
            moreReadBackItemIds);

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Provider(string id, string label, string eventName, Func<L1011FoStateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
    };

    /// <summary>
    /// Stops a flow that switches pumps off or turns the battery off unless the aircraft reads on the
    /// ground (the stock SIM_ON_GROUND; unread fails the guard).
    /// </summary>
    private static Step OnGroundGuard(string id)
        => WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, CheckSeconds,
            onTimeout: FlowStepFailurePolicy.Stop);

    /// <summary>
    /// Stops a cold-and-dark flow unless every engine reads stopped (<c>FO_ENGINES_STOPPED</c>: every N3 low
    /// and every fuel-and-ignition switch off). An unread state is NaN, which fails the guard rather than
    /// letting a destructive step through.
    /// </summary>
    private static Step EnginesStoppedGuard(string id)
        => WaitForField(id, "Engines stopped", "FO_ENGINES_STOPPED", v => v > 0.5, CheckSeconds,
            onTimeout: FlowStepFailurePolicy.Stop);

    /// <summary>A fixed wait, spoken with its length; it reads nothing and links no line.</summary>
    private static Step WaitSecondsStep(string id, string label, int seconds) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitSeconds,
        WaitSeconds = seconds,
    };

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
    };

    private static Step Skip(Step step, Func<L1011FoStateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    private static Step Stop(Step step)
    {
        step.FailurePolicy = FlowStepFailurePolicy.Stop;
        return step;
    }

    /// <summary>A step the First Officer leaves alone while <paramref name="when"/> holds: nothing is sent,
    /// <paramref name="text"/> is spoken (what stays as it is), and its lines stay out of the latch.</summary>
    private static Step LeaveAlone(Step step, Func<L1011FoStateEvaluator, bool> when, string text)
    {
        step.LeaveAloneWhen = when;
        step.LeaveAloneText = text;
        return step;
    }

    /// <summary>A step that does not run when <paramref name="requiredStepId"/>, an earlier step of the same run,
    /// was skipped: <paramref name="skipText"/> is spoken instead of the plain "Skipping", and says what the flow
    /// leaves as it is (<see cref="FlowStep{TState}.RequiresStepId"/>).</summary>
    private static Step Requires(Step step, string requiredStepId, string skipText)
    {
        step.RequiresStepId = requiredStepId;
        step.RequiresStepSkipText = skipText;
        return step;
    }

    /// <summary>A step that runs only when <paramref name="sentStepId"/>, an earlier switch step of this run,
    /// actually sent its write; otherwise it passes silently as done (<see cref="FlowStep{TState}.RunOnlyIfStepSent"/>).</summary>
    private static Step Sent(Step step, string sentStepId)
    {
        step.RunOnlyIfStepSent = sentStepId;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    /// <summary>The read-back line this step also delivers: ticked with it, and kept unticked when it is skipped (FO-6).</summary>
    private static Step Also(Step step, params string[] readBackItemIds)
    {
        step.AlsoCompletesChecklistItemIds = readBackItemIds;
        return step;
    }
}
