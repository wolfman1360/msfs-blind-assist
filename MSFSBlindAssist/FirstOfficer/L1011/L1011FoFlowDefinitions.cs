using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.L1011;

using Flow = Models.FlowDefinition<L1011FoStateEvaluator>;
using Step = Models.FlowStep<L1011FoStateEvaluator>;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer flows, following the aircraft's manual checklist: Power Up, Preflight,
/// Before Start, Engine Start, After Start, Before Takeoff, After Takeoff, Descent, Before Landing, After Landing,
/// Parking and Shutdown. Every switch step but the standby power check and Preflight's three tests carries a
/// SkipCondition on its own state ("Already set"; a check or a test is the point, so it always runs), and
/// the executor re-reads before acting anyway. Captain steps are spoken reminders, never automated.
///
/// Power Up is a cold-and-dark flow that switches the fuel pumps and hydraulic pumps off and, in its
/// standby power check, turns the battery off for a moment ([FO-25]): it opens with BOTH guards, engines
/// stopped and on the ground, each a read-only Stop that links no line and fails unread, so a flow started
/// with an engine turning or in flight stops whole instead of working through its steps one by one. The
/// guards stay first; the standby power check stays linked to its own two lines and never to the electrical
/// meter selector lines, which are the pilot's ([FO-7]); the battery on and standby power armed follow it as
/// steps and lines of their own, so the action group ticked line by line still turns the battery on.
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
/// guards ([FO-25]); its packs step is also left alone off the ground, as its tick is. A master that does not read on
/// stops the flow at once, aloud, instead of a two-minute wait for an APU that cannot start. Its toggle-carrying writes
/// (APU bleed, anti-skid, no smoking) are single switches whose "Already set" reads their own position: the executor
/// reads first, and from an unknown position the definition refuses the write.
///
/// Before Start opens with the ground guard (it switches packs 1 and 3 off) and two read-only Stop waits, the APU
/// running and its generator on line, so external power comes off only once the APU carries the buses, as the
/// MD-11's does. It turns each INS unit to NAV only from ALIGN once that unit reads aligned (or leaves one already
/// at NAV there), never a unit the pilot left at OFF or STBY, and copies
/// the captain's altimeter into the cabin barometric setting; a unit not ready for NAV (still aligning, or left
/// at OFF or STBY), or a standard or unread altimeter, gives no value, so nothing is sent or spoken and a Captain
/// step asks instead ([FO-20]). Its doors, cabin baro and INS Captain steps speak only while the aircraft does not
/// show the state, so they are flow-only; the doors step reads the four stock door positions, because the main
/// panel's door light does not follow the doors.
/// Its toggle-carrying writes (seat belts, APU bleed) are single switches that read their own position first.
///
/// Engine Start starts engines 2, 1 and 3 on APU air, the MD-11's shape: the ground guard and four read-only Stop
/// waits (engine data, the APU running, its bleed and the crossbleeds on), then per engine its own tank pump and tank
/// valve, its isolation valve read on (the engine before it has its own turned off first), the ground start, the
/// light-off watch to 20 % N3, the fuel and ignition, the ground start released by the aircraft and a stable idle,
/// each a Stop step; then the engine generators on line. The ground start step links no line: its line latches on the
/// engine running. A flow stopped with a ground start on has it released by the executor over fuel and ignition read
/// off, never by a fuel write. The fuel and ignition switch carries a toggle: its single-switch step reads its own
/// position first.
///
/// After Start sets the aircraft up for taxi on engine air and power: bleeds, packs, the ATMs on then to auto, galley,
/// read-only electrical checks, the meters, all fuel pumps, the 1A and 3A transfers only with fuel in both tanks (left
/// alone aloud otherwise), probe heat, ignition on both, the take-off flaps and the taxi light. The flap index is the
/// tablet's export, else SimBrief's; with neither nothing is sent or spoken and a Captain step asks ([FO-20]); the flaps
/// move on the ground only. The stabilizer trim is the Captain's, never automated: its reminder names the tablet's
/// exported trim. The APU is shut down last, and only once all three engine generators read on line: a Skip wait that,
/// timed out, skips the APU steps, each saying the APU stays running. The stop holds until the APU reads stopped, and the
/// two-minute cool-down follows it only when this run sent it; the master goes off only once the stop is done or found
/// done, and otherwise stays on, saying the APU did not stop. The APU bleed carries a toggle: its single-switch step
/// reads its own position first.
///
/// Before Takeoff is the manual's line-up: pack 2 only, anti-skid on, the take-off autobrake ([FO-14] leaves it to the
/// First Officer), strobe and landing lights, continuous ignition, probe heat, the transponder read at TA/RA and the
/// weather radar on. Its one cut, packs 1 and 3 off, is left alone off the ground, as its tick is ([FO-25]). The
/// anti-skid carries a toggle: its single-switch step reads its own position first.
///
/// After Takeoff puts the gear lever up, only with the aircraft read airborne: on the ground, or with the on-ground flag
/// unread, the step and its tick are left alone, aloud (the gauge's script refuses gear up on the ground, but no live
/// check has seen it). It reminds the Captain of the flap schedule, sets the climb (autobrake, nose, taxi and turnoff
/// lights, continuous ignition off, packs on), waits up to five minutes, read only, for the Captain's flaps up, and
/// confirms the gear up last ([FO-13]): a read-only 20 s wait on the lever and the legs that never writes.
///
/// Descent puts the seat belt signs on, reminds the Captain of the landing data, the landing autobrake (the panel named,
/// no setting: [FO-14]) and the QNH, and turns the cabin altitude selector to the plan's destination elevation, clamped
/// to the selector's end stops; with no plan nothing is sent or spoken and a Captain step asks instead ([FO-20]). The
/// seat belt switch carries a toggle: its single-switch step reads its own position first.
///
/// Before Landing is the manual's approach: the cabin barometric setting copied from the captain's altimeter, by now the
/// destination QNH (a standard or unread altimeter gives no value, and a Captain step asks: [FO-20]; the aircraft moves
/// the cabin baro with the captain's altimeter, so the step usually reads already set, and still catches a cabin baro
/// moved on its own), the fuel set for landing, the nose, taxi and turnoff lights and continuous ignition on, the gear
/// lever down, the Captain's flap schedule, and the gear confirmed down last ([FO-13]). The landing autobrake stays the
/// Captain's ([FO-14]).
///
/// After Landing is the manual's after landing, cleared off the runway. It opens with the ground guard (read only, Stop):
/// started in the air by mistake it would turn the air data heat and anti-skid off, zero the trim and stow the speed
/// brake lever, so it stops whole. Then anti-skid and autobrake off, the landing and
/// strobe lights, continuous ignition and the air data, windshield and side window heat off, the stabilizer trim zeroed
/// (verified on its readout), the speed brake lever forward, the flaps up (on the ground only, left alone otherwise, as
/// After Start's take-off flaps are), the weather radar and the 1A and 3A transfers off, then the APU master on and the
/// APU started: one press, done once it turns; Parking waits for it to run. It cuts no fuel, pump or engine: the manual's
/// tank pumps and crossfeeds wait for Parking, once the engines are cut. The anti-skid carries a toggle: its
/// single-switch step reads its own position first.
///
/// Parking opens with the aircraft read stopped (the stock ground speed below a knot, read only, Stop: the MD-11's
/// <c>PK_STOPPED</c>), sets the parking brake and turns the ground lights off, waits up to two minutes for the APU After
/// Landing started (left alone at once when it does not turn), puts its generator on line, or external power on when the
/// APU generator is not on line and ground power is there, and confirms one of the two on line, read only, Stop, before
/// the fuel and ignition go off (the MD-11's <c>PK_POWER_SOURCE</c>: never drop to the battery). Then the engines are
/// read stopped, read only, up to two minutes, Stop, before anything else: the anti-collision lights (the ground crew's
/// sign that engines turn, off only once they stop, as the MD-11's beacon), the seat belt signs, and the INS, the ATMs,
/// the AC pumps and the tank pumps ([FO-25]). An engine that does not stop ends the flow with the beacon on and nothing
/// cut, and no tank pump is ever switched off under a running engine. The fuel panel leaves the tank 2
/// left pump and the engine 2 tank valve on for the APU, the crossfeeds closed. The APU bleed goes on and the Captain is
/// reminded of ground power. The seat belts, the APU bleed and the three fuel and ignition switches carry a toggle: each
/// step's "Already set" reads its own keys, and the executor reads each key before it writes it.
///
/// Shutdown is the manual's securing. It cuts the pumps and the battery, so it opens with both guards, on the ground and
/// engines stopped ([FO-25]). The emergency lights and galley go off; the APU is shut down in the manual's order: the
/// generator breaker tripped, the bleed off, the stop pressed and held until the APU reads stopped (below 5 %; live, about
/// a minute from 100 to 0), then a wait of up to 150 s for it to a standstill (below 1 %), already set when it reads so,
/// so an APU found coasting is waited out too, and the master off only once the stop is done or found done. The packs go
/// off on the ground only, external power off when it was used (a breaker read open is "Already set"), then the fuel
/// pumps, left alone while the APU turns (the tank 2 left pump feeds it); standby power and the radio masters come last,
/// then the battery, only once the APU master went off or was found off (the PMDG 777's battery waits on its APU too),
/// and left alone, as its tick is, while the APU still reads turning.
/// The APU bleed carries a toggle: its single-switch step reads its own position first.
/// </summary>
public static class L1011FoFlowDefinitions
{
    public static List<Flow> Build() => new()
    {
        BuildPowerUp(),
        BuildPreflight(),
        BuildBeforeStart(),
        BuildEngineStart(),
        BuildAfterStart(),
        BuildBeforeTakeoff(),
        BuildAfterTakeoff(),
        BuildDescent(),
        BuildBeforeLanding(),
        BuildAfterLanding(),
        BuildParking(),
        BuildShutdown(),
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
            // The aircraft's cockpit safety inspection has no galley line, so POWER_UP_CL has none either and
            // none is invented: this step completes its action line only.
            Done(Skip(Multi("PU_GALLEY", "Galley power: off",
                    ("SWITCH_GALLEY_BUS_1", 0), ("SWITCH_GALLEY_BUS_2", 0), ("SWITCH_GALLEY_BUS_3", 0)),
                s => s.IsOn("FO_GALLEY_OFF")), "PU_GALLEY"),

            // The aircraft's checklist lines 18 to 32 in one composite: battery off, standby power On, the
            // lights read, battery on, standby power armed, the lights read again. No SkipCondition: the check
            // is the point. Linked to its own two lines (FO-7, never the meter selectors): the battery and the
            // armed standby power are the next two steps', so a pilot ticking the action group line by line,
            // without the check, still turns the battery on.
            Also(Done(SW("PU_STBY_CHECK", "Standby power check", X.StandbyPowerCheck, 1), "PU_STBY_CHECK"),
                "PUC_STBY_CHECK"),
            Also(Done(Skip(SW("PU_BATTERY", "Battery: on", "TOGGLE_BATTERY", 1),
                s => s.IsPosition("TOGGLE_BATTERY", 1)), "PU_BATTERY"), "PUC_BATTERY"),
            // Armed is the switch at Arm under its closed guard, in the check's order: the guard opened (already
            // open, nothing is sent), the switch to Arm, the guard closed. Armed by hand with the guard left open,
            // only the guard is written.
            Also(Done(Skip(Multi("PU_STBY_ARMED", "Standby power: armed",
                    ("GUARD_STBY_POWER", 1), ("TOGGLE_STBY_POWER", 1), ("GUARD_STBY_POWER", 0)),
                s => s.IsOn("FO_STBY_ARMED")), "PU_STBY_ARMED"), "PUC_STBY_ARMED"),
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
            // A master that does not read on stops the flow at once, saying so: everything after it runs on the APU, and
            // the start presses nothing without it.
            Also(Done(Stop(Skip(SW("PF_APU_MASTER", "APU master: on", "TOGGLE_APU_POWER", 1),
                s => s.IsPosition("TOGGLE_APU_POWER", 1))), "PF_APU_MASTER"), "PFC_APU_MASTER"),
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

    // ------------------------------------------------------------------ Before Start (the manual's before engine start)

    /// <summary>The transponder mode selector's TA/RA position (1 is standby).</summary>
    private const int TransponderTaRa = 5;

    /// <summary>How long Before Start waits for the APU to read running: Preflight started it.</summary>
    private const int ApuRunningCheckSeconds = 10;

    /// <summary>How long Before Start waits for the APU generator's breaker to read closed before external power
    /// comes off.</summary>
    private const int ApuGenOnLineSeconds = 60;

    /// <summary>How long the navigating check waits: a unit turned to NAV reads aligned for about a second before it
    /// reads navigating.</summary>
    private const int InsNavigatingCheckSeconds = 15;

    private static readonly string[] GalleyKeys = { "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3" };

    /// <summary>The three engine isolation valves, then the two crossbleeds: the keys <c>FO_START_AIR_VALVES_ON</c>
    /// reads, in its order.</summary>
    private static readonly string[] StartAirKeys =
        { "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3" };

    private static Flow BuildBeforeStart() => new()
    {
        Id = "BEFORE_START", Name = "Before Start",
        Description = "External power off once the APU generator is on line; seat belts, anti-collision lights, doors, "
            + "galley; APU air for the start; cabin barometric setting; INS to NAV; transponder TA/RA.",
        RelatedChecklistGroupIds = new[] { "BEFORE_START", "BEFORE_START_CL" },
        Steps = new()
        {
            // The flow switches packs 1 and 3 off: never airborne (FO-25). Links no line.
            OnGroundGuard("BS_ON_GROUND"),
            // Preflight started the APU and put its generator on line. External power comes off only once the APU
            // generator is on line: nothing interlocks them, and pulled first it leaves the buses on the battery.
            WaitForField("BS_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, ApuRunningCheckSeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            WaitForField("BS_APU_GEN_ON_LINE", "APU generator on line", "FO_APU_GEN_ON_LINE", On, ApuGenOnLineSeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            Done(Skip(SW("BS_EXT_OFF", "External power: off", X.ExternalPower, 0), s => s.GetValue("FO_EXT_POWER_ON") < 0.5),
                "BS_EXT_OFF"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("BS_SEAT_BELTS", "Seat belt signs: on", "SWITCH_SEATBELT_LIGHT", 1),
                s => s.IsPosition("SWITCH_SEATBELT_LIGHT", 1)), "BS_SEAT_BELTS"), "BSC_SEAT_BELTS"),
            Also(Done(Skip(SW("BS_ANTICOL", "Anti-collision lights: on", "SWITCH_ANTICOL_LIGHT", 1),
                s => s.IsPosition("SWITCH_ANTICOL_LIGHT", 1)), "BS_ANTICOL"), "BSC_ANTICOL"),
            // The doors are the tablet's: the Captain is asked while any door position reads open or unread. The
            // stock door positions are read because the main panel's door light does not follow the doors, so the
            // check is worded on the doors, never the light.
            Skip(Captain("BS_DOORS_CPT", "Doors", "Close the doors on the tablet."), s => s.IsOn("FO_DOORS_CLOSED")),
            Check("BS_CHK_DOORS", "Doors: closed", "FO_DOORS_CLOSED", On, CheckSeconds, "BSC_DOORS"),
            Also(Done(Skip(Multi("BS_GALLEY", "Galley power: off", AllTo(0, GalleyKeys)),
                s => s.IsOn("FO_GALLEY_OFF")), "BS_GALLEY"), "BSC_GALLEY"),
            Check("BS_CHK_FUEL_PUMP", "Fuel pump: at least one on", "FO_ANY_FUEL_PUMP_ON", On, CheckSeconds, "BSC_FUEL_PUMP"),

            // ---- APU air for the start: minimum compressor load, pack 2 only, the valves open, APU bleed on ----
            Also(Done(Skip(SW("BS_APU_MODE_MIN", "APU compressor mode: minimum", "SWITCH_APU_MODE", 0),
                s => s.IsPosition("SWITCH_APU_MODE", 0)), "BS_APU_MODE_MIN"), "BSC_APU_MODE"),
            Also(Done(Skip(Multi("BS_PACK_2", "Packs: pack 2 on, packs 1 and 3 off",
                    (PackKeys[0], 0), (PackKeys[1], 1), (PackKeys[2], 0)),
                s => s.IsOn("FO_PACK_2_ONLY")), "BS_PACK_2"), "BSC_PACK_2"),
            Also(Done(Skip(Multi("BS_ISLN_XBLEEDS", "Engine isolation and crossbleed valves: on", AllTo(1, StartAirKeys)),
                s => s.IsOn("FO_START_AIR_VALVES_ON")), "BS_ISLN_XBLEEDS"), "BSC_ISLN_VALVES"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Done(Skip(SW("BS_APU_BLEED", "APU bleed: on", "SWITCH_APU_BLEED", 1),
                s => s.IsPosition("SWITCH_APU_BLEED", 1)), "BS_APU_BLEED"),

            // The captain's altimeter holds the QNH the Captain set in Preflight. Standard or unread: nothing is sent
            // or spoken (FO-20), and the Captain step below asks for it.
            Done(Skip(Provider("BS_CABIN_BARO", "Cabin barometric setting: captain's altimeter", X.CabinBaro,
                    s => s.CabinBaroTargetHundredths()),
                s => s.IsOn("FO_CABIN_BARO_SET")), "BS_CABIN_BARO"),
            // Keyed on the setting, never on the altimeter: without a value, or with a write that did not take, the
            // cabin baro does not read the captain's setting and the Captain is asked.
            Skip(Captain("BS_CABIN_BARO_CPT", "Cabin barometric setting",
                    "Set the cabin barometric setting to the QNH on the engineer's pressurization panel."),
                s => s.IsOn("FO_CABIN_BARO_SET")),
            // Preflight set system A or B: read only.
            Check("BS_CHK_IGNITION", "Ignition selector: system A or B", "FO_IGNITION_SELECTED", On, CheckSeconds, "BSC_IGNITION"),

            // ---- INS to NAV, unit by unit, then all three navigating ----
            InsToNav(1), InsNotInNav(1),
            InsToNav(2), InsNotInNav(2),
            InsToNav(3), InsNotInNav(3),
            Check("BS_CHK_INS_NAV", "INS 1, 2 and 3: navigating", "FO_INS_NAVIGATING", On, InsNavigatingCheckSeconds,
                "BSC_INS_NAV"),

            // The manual's place for TA/RA: before start.
            Also(Done(Skip(SW("BS_XPDR", "Transponder: TA/RA", "ROTARY_TCAS_MODE", TransponderTaRa),
                s => s.IsPosition("ROTARY_TCAS_MODE", TransponderTaRa)), "BS_XPDR"), "BSC_XPDR"),

            // ---- the fleet's tail: each ticks its own reminder line ----
            Done(Captain("BS_ACARS", "Start ACARS"), "BS_ACARS"),
            Done(Captain("BS_CLEARANCE", "Obtain pushback and start clearance"), "BS_CLEARANCE"),
        }
    };

    /// <summary>
    /// INS <paramref name="unit"/> to NAV once its selector reads ALIGN and it reads aligned, or its selector already
    /// reads NAV. While it still aligns, or sits at OFF, STBY or ATT, the provider gives no value: nothing is sent or
    /// spoken ([FO-20]) and <see cref="InsNotInNav"/> asks the Captain.
    /// </summary>
    private static Step InsToNav(int unit)
    {
        string key = L1011InsAlignmentAnnouncer.MsuKey(unit);
        string n = unit.ToString(CultureInfo.InvariantCulture);
        string id = $"BS_INS_{n}_NAV";
        return Done(Skip(Provider(id, $"INS {n}: nav", key, s => s.InsNavTarget(unit)),
            s => s.IsPosition(key, L1011FoStateEvaluator.InsNav)), id);
    }

    /// <summary>The Captain's line for a unit the First Officer left where it was: keyed on the selector, so it
    /// speaks whenever the unit is not in NAV (still aligning, or left at OFF or STBY), and a write that did not
    /// take asks too.</summary>
    private static Step InsNotInNav(int unit)
    {
        string key = L1011InsAlignmentAnnouncer.MsuKey(unit);
        string n = unit.ToString(CultureInfo.InvariantCulture);
        return Skip(Captain($"BS_INS_{n}_CPT", $"INS {n} not in NAV",
                $"INS {n} is not in NAV: set it to NAV once READY NAV lights."),
            s => s.IsPosition(key, L1011FoStateEvaluator.InsNav));
    }

    // ------------------------------------------------------------------ Engine Start (2, 1, 3, on APU air)

    /// <summary>How long Engine Start's opening waits and each isolation valve check wait: Before Start left them so.</summary>
    private const int StartReadySeconds = 10;

    /// <summary>How long the flow waits for the aircraft to release a ground start switch, which it does as N3 passes
    /// about 55 %, about 35 s after the fuel and ignition go on at 20 %.</summary>
    private const int GroundStartReleaseSeconds = 60;

    /// <summary>The start order, and the engine started before each (none before engine 2).</summary>
    private static readonly (int Engine, int? Previous)[] StartOrder = { (2, null), (1, 2), (3, 1) };

    /// <summary>Engine <paramref name="n"/>'s own tank pump: the first pump of tank 1, 2 left or 3.</summary>
    private static string TankPump(int n) => n switch
    {
        1 => "SWITCH_FUEL_PUMP_1_1",
        2 => "SWITCH_FUEL_PUMP_2L_1",
        _ => "SWITCH_FUEL_PUMP_3_1",
    };

    private static Flow BuildEngineStart()
    {
        var steps = new List<Step>
        {
            // Never airborne. Links no line.
            OnGroundGuard("ES_ON_GROUND"),
            // Confirms engine 2's N3 is read before anything is written (the MD-11's engine data wait).
            WaitForField("ES_DATA", "Engine data", X.N3Key(2), v => !double.IsNaN(v), StartReadySeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            // The start air Before Start left: the APU running with its bleed on, both crossbleeds on. Read only.
            WaitForField("ES_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, StartReadySeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            WaitForField("ES_APU_BLEED", "APU bleed: on", "SWITCH_APU_BLEED", On, StartReadySeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            WaitForField("ES_CROSSBLEEDS", "Crossbleed valves: on", "FO_CROSSBLEEDS_ON", On, StartReadySeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
        };
        foreach (var (engine, previous) in StartOrder)
            steps.AddRange(StartEngine(engine, previous));
        // Every field relay closed, then every breaker: a relay already closed is not pressed (an engine's field closed
        // by itself once it ran). One that does not close does not stop the flow.
        steps.Add(Also(Done(Skip(SW("ES_GENERATORS", "Engine generator fields and breakers: closed", X.EngineGenerators, 1),
            s => s.IsOn("FO_ENGINE_GENERATORS_ON")), "ES_GENERATORS"), "ESC_GEN_FIELDS", "ESC_GEN_BREAKERS"));
        return new Flow
        {
            Id = "ENGINE_START", Name = "Engine Start",
            Description = "Engines 2, 1 and 3 on APU air: tank pump and valve, ground start, fuel and ignition at "
                + "20 percent N3, the switch released at 55 percent, stable idle; then the engine generators on line.",
            RelatedChecklistGroupIds = new[] { "ENGINE_START", "ENGINE_START_CL" },
            Steps = steps,
        };
    }

    /// <summary>
    /// Engine <paramref name="n"/>'s start. Engines 1 and 3 first turn the isolation valve of the engine started before
    /// (<paramref name="previous"/>) off. Then the engine's own feed, its isolation valve read on, the ground start (the
    /// executor turns the switch on and watches N3 rise), the light-off watch to 20 % N3, the fuel and ignition, the
    /// ground start released by the aircraft itself past about 55 % N3, and a stable idle. From the feed on, every step
    /// stops the flow when it is not done: going on would start this engine without its fuel, or the next engine over a
    /// failed one.
    /// </summary>
    private static IEnumerable<Step> StartEngine(int n, int? previous)
    {
        string num = n.ToString(CultureInfo.InvariantCulture);
        string e = $"ES_E{num}_";
        if (previous is int p)
        {
            string prev = p.ToString(CultureInfo.InvariantCulture);
            string isolation = $"SWITCH_ISLN_VALVE_ENG_{prev}";
            yield return Also(Done(Skip(SW(e + "PREV_ISLN_OFF", $"Engine {prev} isolation valve: off", isolation, 0),
                s => s.IsPosition(isolation, 0)), e + "PREV_ISLN_OFF"), $"ESC_E{prev}_ISLN_OFF");
        }
        // Engine 2's feed is the APU's, set in Preflight: already set there. It also delivers the read-back's pump line.
        // A feed not done stops the flow: no ground start and no fuel on an engine its feed did not reach.
        var feed = Done(Stop(Skip(Multi(e + "FUEL_FEED", $"Engine {num} fuel: tank pump and tank valve on",
                (TankPump(n), 1), ($"SWITCH_ENG_{num}_TANK_VALVE", 1), ($"GUARD_ENG_{num}_TANK_VALVE", 0)),
            s => s.IsOn($"FO_ENG{num}_FUEL_FEED"))), e + "FUEL_FEED");
        yield return n == 2 ? Also(feed, "ESC_FUEL_PUMP") : feed;
        yield return WaitForField(e + "ISLN", $"Engine {num} isolation valve: on", $"SWITCH_ISLN_VALVE_ENG_{num}", On,
            StartReadySeconds, onTimeout: FlowStepFailurePolicy.Stop);
        // Never linked to its StayComplete line (latched on the engine running): a start marked it, a failed light-off
        // then stopped the flow, and the line stayed ticked over an engine that never ran. Skipped only for an engine
        // already running; one turning below that is left to the composite, which watches its rotation.
        yield return Stop(Skip(SW(e + "GND_START", $"Engine {num} ground start switch: on", X.GroundStart(n), 1),
            s => s.IsOn($"FO_ENG{num}_RUNNING")));
        // No light-off in time releases the ground start over fuel off, and Stop ends the flow before any fuel goes on.
        yield return Stop(SW(e + "N3_20", $"Engine {num} N3 twenty percent", X.EngineLightOff(n), 1));
        // A toggle-carrying switch, written only over its own position read ([L1011-4]). Refused, the flow stops with
        // the ground start on, and the Stop clean-up releases it over fuel read off.
        string fuel = X.FuelIgnitionKey(n);
        yield return Also(Done(Stop(Skip(SW(e + "FUEL_IGN", $"Engine {num} fuel and ignition: on", fuel, 1),
            s => s.IsPosition(fuel, 1))), e + "FUEL_IGN"), $"ESC_E{num}_FUEL_IGN");
        // The aircraft's own release: a switch that never releases is a hung start, the Captain's.
        yield return Done(WaitForField(e + "RELEASED", $"Engine {num} ground start switch released", X.GroundStartKey(n),
            v => v < 0.5, GroundStartReleaseSeconds, onTimeout: FlowStepFailurePolicy.Stop), $"ESC_E{num}_RELEASED");
        yield return Done(Stop(SW(e + "IDLE", $"Engine {num}: stable idle", X.EngineIdle(n), 1)), $"ESC_E{num}_RUNNING");
    }

    // ------------------------------------------------------------------ After Start (set up for taxi, then the APU off)

    /// <summary>How long After Start waits for all three engine generators to read on line before the APU is shut down.</summary>
    private const int EngineGeneratorsOnLineSeconds = 30;

    /// <summary>The APU's cool-down after its stop press: the manual's two minutes.</summary>
    private const int ApuCooldownSeconds = 120;

    /// <summary>The AC meter's generator 3 position, the DC meter's battery, the ignition selector's both systems.</summary>
    private const int AcMeterGen3 = 3, DcMeterBattery = 4, IgnitionBoth = 1;

    /// <summary>The ATM switches' positions: 1 AUTO, 2 ON (0 is OFF).</summary>
    private const int AtmAuto = 1, AtmOn = 2;

    /// <summary>What every APU step says when the engine generators were not all read on line: the APU stays.</summary>
    private const string ApuStaysText = "The engine generators are not all on line, so the APU stays running.";

    /// <summary>What the APU master says when the stop was skipped or not done, and its tick while the APU does not read
    /// stopped. After Start's and Shutdown's.</summary>
    private const string ApuMasterStaysText = L1011FoChecklistDefinitions.ApuMasterStaysText;

    private static readonly string[] AtmKeys = { "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE" };

    private static readonly string[] FuelPumpKeys =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    private static Flow BuildAfterStart() => new()
    {
        Id = "AFTER_START", Name = "After Start",
        Description = "Bleeds, packs, ATMs and galley for taxi; electrical checks; all fuel pumps on, the 1A and 3A "
            + "transfers with fuel in both tanks; probe heat; ignition both; take-off flaps; taxi light; then the APU shut "
            + "down once the engine generators are on line.",
        RelatedChecklistGroupIds = new[] { "AFTER_START", "AFTER_START_CL", "TAXI_CL" },
        Steps = new()
        {
            // ---- air: bleeds, isolation valves and crossbleeds, packs ----
            Also(Done(Skip(Multi("AS_HI_PRESS", "High pressure bleed valves: on",
                    AllTo(1, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3")),
                s => s.IsOn("FO_HI_PRESS_ON")), "AS_HI_PRESS"), "ASC_HI_PRESS"),
            Also(Done(Skip(Multi("AS_ISLN_VALVES", "Engine isolation valves: on",
                    AllTo(1, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3")),
                s => s.IsOn("FO_ISLN_VALVES_ON")), "AS_ISLN_VALVES"), "ASC_ISLN_VALVES"),
            Also(Done(Skip(Multi("AS_APU_AIR", "Fuselage isolation and crossbleed valves: on",
                    AllTo(1, "SWITCH_APU_ISLN_VALVE", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3")),
                s => s.IsOn("FO_APU_AIR_ON")), "AS_APU_AIR"), "ASC_FUSELAGE_ISLN", "ASC_XBLEEDS"),
            Also(Done(Skip(Multi("AS_PACKS_ON", "Packs: on", AllTo(1, PackKeys)),
                s => s.IsOn("FO_PACKS_ON")), "AS_PACKS_ON"), "ASC_PACKS"),

            // ---- the ATMs on, then to auto. The on position is the step, so it always runs, and its line reads no
            // state: the next step undoes it.
            Done(Multi("AS_ATMS_ON", "ATM switches: on", AllTo(AtmOn, AtmKeys)), "AS_ATMS_ON"),
            Also(Done(Skip(Multi("AS_ATMS_AUTO", "ATM switches: auto", AllTo(AtmAuto, AtmKeys)),
                s => s.IsOn("FO_ATMS_AUTO")), "AS_ATMS_AUTO"), "ASC_ATMS"),
            Also(Done(Skip(Multi("AS_GALLEY_ON", "Galley power: on", AllTo(1, GalleyKeys)),
                s => s.IsOn("FO_GALLEY_ON")), "AS_GALLEY_ON"), "ASC_GALLEY"),

            // ---- electrical: what Engine Start left, read only; the generator check reads all six relays, which is
            // both of its read-back lines.
            Check("AS_CHK_GENERATORS", "Engine generators: on line", "FO_ENGINE_GENERATORS_ON", On, CheckSeconds,
                "ASC_GEN_FIELDS", "ASC_GEN_BREAKERS"),
            Check("AS_CHK_IDG", "IDG low pressure lights: out", "FO_IDG_LIGHTS_OUT", On, CheckSeconds, "ASC_IDG"),
            Check("AS_CHK_BUS_FAIL", "Bus fail lights: out", "FO_BUS_FAIL_LIGHTS_OUT", On, CheckSeconds, "ASC_BUS_FAIL"),
            Also(Done(Skip(SW("AS_AC_METER", "AC meter: generator 3", "ROTARY_PWR_SOURCE", AcMeterGen3),
                s => s.IsPosition("ROTARY_PWR_SOURCE", AcMeterGen3)), "AS_AC_METER"), "ASC_AC_METER"),
            Also(Done(Skip(SW("AS_DC_METER", "DC meter: battery", "ROTARY_STBY_DC_BUS", DcMeterBattery),
                s => s.IsPosition("ROTARY_STBY_DC_BUS", DcMeterBattery)), "AS_DC_METER"), "ASC_DC_METER"),
            Check("AS_CHK_AC_ESS", "AC essential power selector: bus 3, generator 1", "ROTARY_AC_ESS_BUS",
                v => Math.Abs(v - AcEssNormal) < 0.5, CheckSeconds, "ASC_AC_ESS"),

            // ---- fuel ----
            Also(Done(Skip(Multi("AS_FUEL_PUMPS", "Fuel pumps: all on", AllTo(1, FuelPumpKeys)),
                s => s.IsOn("FO_FUEL_PUMPS_ON")), "AS_FUEL_PUMPS"), "ASC_FUEL_PUMPS"),
            Also(Done(Skip(Multi("AS_CROSSFEEDS", "Crossfeed valves: closed",
                    AllTo(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED")),
                s => s.IsOn("FO_CROSSFEEDS_CLOSED")), "AS_CROSSFEEDS"), "ASC_CROSSFEEDS"),
            // On only with fuel in both tanks 1A and 3A; otherwise left alone, aloud, and its lines stay out of the latch.
            Also(Done(Skip(LeaveAlone(Multi("AS_TRANSFERS", "1A and 3A transfers: on", AllTo(1, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
                    L1011FoChecklistDefinitions.TransferTanksNotFueled, L1011FoChecklistDefinitions.TransfersStayText),
                s => s.IsOn("FO_TRANSFERS_ON")), "AS_TRANSFERS"), "ASC_TRANSFERS"),

            // ---- probe heat and ignition ----
            Also(Done(Skip(Multi("AS_PITOT_HEAT", "Pitot and alpha probe heat: on",
                    AllTo(1, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_ALPHA_HEAT")),
                s => s.IsOn("FO_PITOT_ALPHA_HEAT_ON")), "AS_PITOT_HEAT"), "ASC_PITOT_HEAT", "ASC_ALPHA_HEAT"),
            Also(Done(Skip(SW("AS_IGNITION_BOTH", "Ignition selector: both", "ROTARY_ENG_START_SEL", IgnitionBoth),
                s => s.IsPosition("ROTARY_ENG_START_SEL", IgnitionBoth)), "AS_IGNITION_BOTH"), "ASC_IGNITION"),

            // ---- the take-off flaps: the tablet's export, else SimBrief's. With neither, nothing is sent or spoken
            // (FO-20) and the Captain step below asks. The flaps move on the ground only.
            Also(Done(Skip(LeaveAlone(Provider("AS_FLAPS", "Flaps: take-off setting", L1011Levers.FlapHandleKey,
                        s => s.TakeoffFlapIndex()),
                    L1011FoChecklistDefinitions.NotOnGround, L1011FoChecklistDefinitions.FlapsStayText),
                s => s.IsOn("FO_FLAPS_TAKEOFF")), "AS_FLAPS"), "ASC_FLAPS"),
            // Keyed on the handle, never on the data: with no data, or with a write that did not take, the handle does
            // not read the take-off index and the Captain is asked.
            Skip(Captain("AS_FLAPS_CPT", "Take-off flaps",
                    "Set the take-off flaps: enter the take-off data on the tablet, or move the flap handle yourself."),
                s => s.IsOn("FO_FLAPS_TAKEOFF")),
            Also(Done(Skip(SW("AS_TAXI_LIGHT", "Nose taxi light: on", "TOGGLE_TAXI_LIGHT", 1),
                s => s.IsPosition("TOGGLE_TAXI_LIGHT", 1)), "AS_TAXI_LIGHT"), "TXC_TAXI_LIGHT"),

            // ---- the Captain's: each ticks its own reminder line. The stabilizer trim is never automated; its
            // reminder names the tablet's exported trim once there is one.
            Done(Saying(Captain("AS_CPT_STAB_TRIM", "Set the stabilizer trim for take-off"),
                s => L1011FoChecklistDefinitions.StabTrimUnits(s) is string units
                    ? $"Set stabilizer trim for take-off: {units}"
                    : null), "AS_CPT_STAB_TRIM"),
            Done(Captain("AS_CPT_ANTI_ICE", "Engine anti-ice as required"), "AS_CPT_ANTI_ICE"),
            Done(Captain("AS_CPT_CONTROLS", "Check the speed brake and the flight controls"), "AS_CPT_CONTROLS"),

            // ---- the APU off, only once all three engine generators carry the buses: timed out, the wait skips every
            // APU step with one sentence, and the APU keeps running.
            WaitForField("AS_GENS_ON_LINE", "Engine generators on line", "FO_ENGINE_GENERATORS_ON", On,
                EngineGeneratorsOnLineSeconds),
            Also(Done(Skip(Requires(SW("AS_APU_GEN_TRIP", "APU generator breaker: trip", X.ApuGenerator, 0),
                    "AS_GENS_ON_LINE", ApuStaysText),
                s => s.IsOn("FO_APU_GEN_OFF_LINE")), "AS_APU_GEN_TRIP"), "ASC_APU_GEN_TRIP"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(Requires(SW("AS_APU_BLEED_OFF", "APU bleed: off", "SWITCH_APU_BLEED", 0),
                    "AS_GENS_ON_LINE", ApuStaysText),
                s => s.IsPosition("SWITCH_APU_BLEED", 0)), "AS_APU_BLEED_OFF"), "ASC_APU_BLEED"),
            // One press, then the composite holds until the APU reads stopped (about a minute). Already set once its
            // speed reads below 5 %.
            Also(Done(Skip(Requires(SW("AS_APU_STOP", "APU: stop", X.ApuStop, 1),
                    "AS_GENS_ON_LINE", ApuStaysText),
                s => s.IsOn("FO_APU_OFF")), "AS_APU_STOP"), "ASC_APU_STOP"),
            // Only after a stop this run sent: an APU found stopped costs no wait.
            Sent(WaitSecondsStep("AS_APU_COOLDOWN", "APU cool-down: two minutes", ApuCooldownSeconds), "AS_APU_STOP"),
            // Only once the stop is done or found done ("Already set" is not a skip): a stop skipped, by the generator
            // wait or because the APU never read stopped, leaves the master on.
            Also(Done(Skip(Requires(SW("AS_APU_MASTER_OFF", "APU master: off", "TOGGLE_APU_POWER", 0),
                    "AS_APU_STOP", ApuMasterStaysText),
                s => s.IsPosition("TOGGLE_APU_POWER", 0)), "AS_APU_MASTER_OFF"), "ASC_APU_MASTER"),
        }
    };

    // ------------------------------------------------------------------ Before Takeoff (the manual's line-up)

    /// <summary>The autobrake selector's TAKEOFF position (1 is OFF).</summary>
    private const int AutobrakeTakeoff = 0;

    private static Flow BuildBeforeTakeoff() => new()
    {
        Id = "BEFORE_TAKEOFF", Name = "Before Takeoff",
        Description = "Line-up: pack 2 only, anti-skid on, take-off autobrake, strobe and landing lights, continuous "
            + "ignition and probe heat on, the transponder checked at TA/RA, weather radar on.",
        RelatedChecklistGroupIds = new[] { "BEFORE_TAKEOFF", "BEFORE_TAKEOFF_CL" },
        Steps = new()
        {
            // Packs 1 and 3 off in the air would cut two of the three packs: left alone off the ground, as its tick is.
            Also(Done(Skip(LeaveAlone(Multi("BT_PACK_2", "Packs: pack 2 on, packs 1 and 3 off",
                        (PackKeys[0], 0), (PackKeys[1], 1), (PackKeys[2], 0)),
                    L1011FoChecklistDefinitions.NotOnGround, L1011FoChecklistDefinitions.PacksStayText),
                s => s.IsOn("FO_PACK_2_ONLY")), "BT_PACK_2"), "BTC_PACKS"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("BT_ANTISKID", "Anti-skid: on", "SWITCH_ANTISKID_ON", 1),
                s => s.IsPosition("SWITCH_ANTISKID_ON", 1)), "BT_ANTISKID"), "BTC_ANTISKID"),
            // The take-off setting is the First Officer's (FO-14); the landing setting is the Captain's.
            Also(Done(Skip(SW("BT_AUTOBRAKE", "Autobrake: takeoff", "ROTARY_AUTO_BRAKE_MODE", AutobrakeTakeoff),
                s => s.IsPosition("ROTARY_AUTO_BRAKE_MODE", AutobrakeTakeoff)), "BT_AUTOBRAKE"), "BTC_AUTOBRAKE"),
            Also(Done(Skip(SW("BT_STROBES", "Strobe lights: on", "SWITCH_STROBE_LIGHT", 1),
                s => s.IsPosition("SWITCH_STROBE_LIGHT", 1)), "BT_STROBES"), "BTC_STROBES"),
            Also(Done(Skip(Multi("BT_LANDING_LIGHTS", "Landing lights: on", AllTo(1, "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT")),
                s => s.IsOn("FO_LANDING_LIGHTS_ON")), "BT_LANDING_LIGHTS"), "BTC_LANDING_LIGHTS"),
            Also(Done(Skip(SW("BT_CONT_IGN", "Continuous ignition: on", "SWITCH_CONT_IGNITION", 1),
                s => s.IsPosition("SWITCH_CONT_IGNITION", 1)), "BT_CONT_IGN"), "BTC_CONT_IGN"),
            Also(Done(Skip(SW("BT_PROBE_HEAT", "Temperature probe heat: on", "SWITCH_PROBE_HEAT", 1),
                s => s.IsPosition("SWITCH_PROBE_HEAT", 1)), "BT_PROBE_HEAT"), "BTC_PROBE_HEAT"),
            // Before Start set TA/RA: read only.
            Check("BT_CHK_XPDR", "Transponder: TA/RA", "ROTARY_TCAS_MODE", v => Math.Abs(v - TransponderTaRa) < 0.5,
                CheckSeconds, "BTC_XPDR"),
            Also(Done(Skip(SW("BT_RADAR", "Weather radar: on", "SWITCH_WX_WX", 1),
                s => s.IsPosition("SWITCH_WX_WX", 1)), "BT_RADAR"), "BTC_RADAR"),
        }
    };

    // ------------------------------------------------------------------ After Takeoff (the manual's take-off and climb)

    /// <summary>The gear lever's up position (0 down, 50 off).</summary>
    private const int GearUp = 100;

    /// <summary>The autobrake selector's OFF position.</summary>
    private const int AutobrakeOff = 1;

    private static readonly string[] GroundLightKeys = { "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF" };

    private static Flow BuildAfterTakeoff() => new()
    {
        Id = "AFTER_TAKEOFF", Name = "After Takeoff",
        Description = "Gear up; the Captain's flap schedule; autobrake off, nose, taxi and turnoff lights off, continuous "
            + "ignition off, packs on; then the flaps and the gear confirmed up.",
        RelatedChecklistGroupIds = new[] { "AFTER_TAKEOFF", "AFTER_TAKEOFF_CL", "CLIMB_CL" },
        Steps = new()
        {
            // Already set once the lever reads up. Written only with the aircraft read airborne: on the ground, or with
            // the on-ground flag unread, it is left alone, aloud, as its tick is. The gauge's script refuses gear up on the
            // ground, but no live check has seen it, so nothing relies on it. Its read-back line is the legs', which
            // the gear check last delivers.
            Done(Skip(LeaveAlone(SW("AT_GEAR", "Landing gear: up", L1011Levers.GearLeverKey, GearUp),
                    L1011FoChecklistDefinitions.NotAirborne, L1011FoChecklistDefinitions.GearStaysDownText),
                s => s.IsOn("FO_GEAR_LEVER_UP")), "AT_GEAR"),
            // The flaps stay the Captain's in flight.
            Done(Captain("AT_CPT_FLAPS", "Flaps up on schedule",
                "Retract the flaps on schedule: 10 at V2 plus 10, 4 at V2 plus 20, up at V2 plus 60."), "AT_CPT_FLAPS"),
            Also(Done(Skip(SW("AT_AUTOBRAKE", "Autobrake: off", "ROTARY_AUTO_BRAKE_MODE", AutobrakeOff),
                s => s.IsPosition("ROTARY_AUTO_BRAKE_MODE", AutobrakeOff)), "AT_AUTOBRAKE"), "CLC_AUTOBRAKE"),
            Also(Done(Skip(Multi("AT_GROUND_LIGHTS", "Nose, taxi and runway turnoff lights: off", AllTo(0, GroundLightKeys)),
                s => s.IsOn("FO_GROUND_LIGHTS_OFF")), "AT_GROUND_LIGHTS"), "CLC_GROUND_LIGHTS"),
            Also(Done(Skip(SW("AT_CONT_IGN", "Continuous ignition: off", "SWITCH_CONT_IGNITION", 0),
                s => s.IsPosition("SWITCH_CONT_IGNITION", 0)), "AT_CONT_IGN"), "CLC_CONT_IGN"),
            Also(Done(Skip(Multi("AT_PACKS_ON", "Packs: on", AllTo(1, PackKeys)),
                s => s.IsOn("FO_PACKS_ON")), "AT_PACKS_ON"), "CLC_PACKS"),
            // The Captain's flaps, read only: up to five minutes, then skipped aloud.
            Check("AT_CHK_FLAPS_UP", "Flaps: up", "FO_FLAPS_UP", On, FlapsUpCheckSeconds, "ATC_FLAPS"),
            // LAST (FO-13): the gear confirmed up, the lever and every leg, read only. 20 s, then skipped aloud.
            Check("AT_GEAR_UP_CHECK", "Landing gear: up", L1011GearConfirmation.UpField, On, GearCheckSeconds, "ATC_GEAR"),
        }
    };

    // ------------------------------------------------------------------ Descent

    private static Flow BuildDescent() => new()
    {
        Id = "DESCENT", Name = "Descent",
        Description = "Seat belt signs on; the Captain's landing data, landing autobrake and QNH; the cabin altitude "
            + "selector to the destination elevation.",
        RelatedChecklistGroupIds = new[] { "DESCENT", "DESCENT_CL" },
        Steps = new()
        {
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("DS_SEAT_BELTS", "Seat belt signs: on", "SWITCH_SEATBELT_LIGHT", 1),
                s => s.IsPosition("SWITCH_SEATBELT_LIGHT", 1)), "DS_SEAT_BELTS"), "DSC_SEAT_BELTS"),
            Done(Captain("DS_CPT_LANDING_DATA", "Prepare the landing data on the tablet"), "DS_CPT_LANDING_DATA"),
            // The landing autobrake is the Captain's (FO-14): the panel named, no setting.
            Done(Captain("DS_CPT_AUTOBRAKE", "Set the landing autobrake on the overhead Antiskid and Autobrake panel"),
                "DS_CPT_AUTOBRAKE"),
            Done(Captain("DS_CPT_QNH", "Set the altimeters to QNH at the transition level: Ctrl+B"), "DS_CPT_QNH"),
            // The plan's destination elevation, clamped to the selector's end stops. No plan: nothing is sent or spoken
            // (FO-20), and the Captain step below asks for it.
            Also(Done(Skip(Provider("DS_CABIN_ALT", "Cabin altitude selector: destination elevation", X.CabinAltitudeSelect,
                    s => s.DestinationCabinAltitudeFt()),
                s => s.IsOn("FO_CABIN_ALT_AT_DEST")), "DS_CABIN_ALT"), "DSC_CABIN_ALT"),
            // Keyed on the selector, never on the plan: without a plan, or with a write that did not take, the selector
            // does not read the destination and the Captain is asked.
            Skip(Captain("DS_CABIN_ALT_CPT", "Cabin altitude selector",
                    "Set the cabin altitude selector to the destination elevation on the engineer's pressurization panel."),
                s => s.IsOn("FO_CABIN_ALT_AT_DEST")),
        }
    };

    // ------------------------------------------------------------------ Before Landing (the manual's approach)

    /// <summary>The gear lever's down position (50 off, 100 up).</summary>
    private const int GearDown = 0;

    private static Flow BuildBeforeLanding() => new()
    {
        Id = "BEFORE_LANDING", Name = "Before Landing",
        Description = "Cabin barometric setting to the captain's altimeter; 1A and 3A transfers off, all fuel pumps on, "
            + "crossfeeds closed; nose, taxi and turnoff lights and continuous ignition on; gear down; the Captain's flap "
            + "schedule; then the gear confirmed down.",
        RelatedChecklistGroupIds = new[] { "BEFORE_LANDING", "BEFORE_LANDING_CL" },
        Steps = new()
        {
            // The captain's altimeter holds the destination QNH by now. Standard or unread: nothing is sent or spoken
            // (FO-20), and the Captain step below asks for it. The aircraft moves the cabin baro with the captain's
            // altimeter, so this usually reads already set; it still catches a cabin baro moved on its own.
            Also(Done(Skip(Provider("BL_CABIN_BARO", "Cabin barometric setting: captain's altimeter", X.CabinBaro,
                    s => s.CabinBaroTargetHundredths()),
                s => s.IsOn("FO_CABIN_BARO_SET")), "BL_CABIN_BARO"), "BLC_CABIN_BARO"),
            // Keyed on the setting, never on the altimeter: without a value, or with a write that did not take, the
            // cabin baro does not read the captain's setting and the Captain is asked.
            Skip(Captain("BL_CABIN_BARO_CPT", "Cabin barometric setting",
                    "Set the cabin barometric setting to the QNH on the engineer's pressurization panel."),
                s => s.IsOn("FO_CABIN_BARO_SET")),

            // ---- fuel for landing ----
            Also(Done(Skip(Multi("BL_TRANSFERS_OFF", "1A and 3A transfers: off", AllTo(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
                s => s.IsOn("FO_TRANSFERS_OFF")), "BL_TRANSFERS_OFF"), "BLC_TRANSFERS"),
            Also(Done(Skip(Multi("BL_FUEL_PUMPS", "Fuel pumps: all on", AllTo(1, FuelPumpKeys)),
                s => s.IsOn("FO_FUEL_PUMPS_ON")), "BL_FUEL_PUMPS"), "BLC_PUMPS"),
            Also(Done(Skip(Multi("BL_CROSSFEEDS", "Crossfeed valves: closed",
                    AllTo(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED")),
                s => s.IsOn("FO_CROSSFEEDS_CLOSED")), "BL_CROSSFEEDS"), "BLC_CROSSFEEDS"),

            Also(Done(Skip(Multi("BL_GROUND_LIGHTS", "Nose, taxi and runway turnoff lights: on", AllTo(1, GroundLightKeys)),
                s => s.IsOn("FO_GROUND_LIGHTS_ON")), "BL_GROUND_LIGHTS"), "BLC_GROUND_LIGHTS"),
            Also(Done(Skip(SW("BL_CONT_IGN", "Continuous ignition: on", "SWITCH_CONT_IGNITION", 1),
                s => s.IsPosition("SWITCH_CONT_IGNITION", 1)), "BL_CONT_IGN"), "BLC_CONT_IGN"),
            // Already set once the lever reads down. Its read-back line is the legs', which the gear check last delivers.
            Done(Skip(SW("BL_GEAR", "Landing gear: down", L1011Levers.GearLeverKey, GearDown),
                s => s.IsOn("FO_GEAR_LEVER_DOWN")), "BL_GEAR"),
            // The flaps stay the Captain's.
            Done(Captain("BL_CPT_FLAPS", "Flaps on schedule",
                "Extend the flaps on schedule: 4 at Vref plus 60, 10 at Vref plus 40, 22 at Vref plus 30, 33 at Vref plus 20."),
                "BL_CPT_FLAPS"),
            // LAST (FO-13): the gear confirmed down, the lever and every leg, read only. 20 s, then skipped aloud.
            Check("BL_GEAR_DOWN_CHECK", "Landing gear: down", L1011GearConfirmation.DownField, On, GearCheckSeconds, "BLC_GEAR"),
        }
    };

    // ------------------------------------------------------------------ After Landing (the manual's after landing)

    private static readonly string[] LandingLightKeys = { "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT" };

    private static readonly string[] AirDataHeatKeys =
        { "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT" };

    private static readonly string[] WindshieldKeys = { "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO" };

    private static readonly string[] SideWindowKeys =
        { "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR" };

    private static Flow BuildAfterLanding() => new()
    {
        Id = "AFTER_LANDING", Name = "After Landing",
        Description = "Anti-skid and autobrake off; landing and strobe lights, continuous ignition and probe and window heat "
            + "off; stabilizer trim zero, speed brake forward, flaps up; weather radar and transfers off; then the APU master "
            + "on and the APU started.",
        RelatedChecklistGroupIds = new[] { "AFTER_LANDING", "AFTER_LANDING_CL" },
        Steps = new()
        {
            // Started airborne by mistake, the flow would turn the air data heat and anti-skid off, zero the trim and stow
            // the speed brake lever: never in the air. Read only, fails unread, links no line.
            OnGroundGuard("AL_ON_GROUND"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("AL_ANTISKID", "Anti-skid: off", "SWITCH_ANTISKID_ON", 0),
                s => s.IsPosition("SWITCH_ANTISKID_ON", 0)), "AL_ANTISKID"), "ALC_ANTISKID"),
            Also(Done(Skip(SW("AL_AUTOBRAKE", "Autobrake: off", "ROTARY_AUTO_BRAKE_MODE", AutobrakeOff),
                s => s.IsPosition("ROTARY_AUTO_BRAKE_MODE", AutobrakeOff)), "AL_AUTOBRAKE"), "ALC_AUTOBRAKE"),
            Also(Done(Skip(Multi("AL_LANDING_LIGHTS", "Landing lights: off", AllTo(0, LandingLightKeys)),
                s => s.IsOn("FO_LANDING_LIGHTS_OFF")), "AL_LANDING_LIGHTS"), "ALC_LANDING_LIGHTS"),
            Also(Done(Skip(SW("AL_STROBES", "Strobe lights: off", "SWITCH_STROBE_LIGHT", 0),
                s => s.IsPosition("SWITCH_STROBE_LIGHT", 0)), "AL_STROBES"), "ALC_STROBES"),
            Also(Done(Skip(SW("AL_CONT_IGN", "Continuous ignition: off", "SWITCH_CONT_IGNITION", 0),
                s => s.IsPosition("SWITCH_CONT_IGNITION", 0)), "AL_CONT_IGN"), "ALC_CONT_IGN"),
            Also(Done(Skip(Multi("AL_AIR_DATA", "Air data heat: off", AllTo(0, AirDataHeatKeys)),
                s => s.IsOn("FO_AIR_DATA_HEAT_OFF")), "AL_AIR_DATA"), "ALC_AIR_DATA"),
            Also(Done(Skip(Multi("AL_WINDSHIELD", "Windshield heat: idle", AllTo(0, WindshieldKeys)),
                s => s.IsOn("FO_WINDSHIELD_IDLE")), "AL_WINDSHIELD"), "ALC_WINDSHIELD"),
            Also(Done(Skip(Multi("AL_SIDE_WINDOWS", "Side window heat: off", AllTo(0, SideWindowKeys)),
                s => s.IsOn("FO_SIDE_WINDOWS_OFF")), "AL_SIDE_WINDOWS"), "ALC_SIDE_WINDOWS"),
            // The typed entry is write-only: the executor verifies it on the trim readout, and "Already set" reads it.
            Also(Done(Skip(SW("AL_STAB_TRIM", "Stabilizer trim: zero", L1011Levers.StabTrimKey, 0),
                s => s.IsOn("FO_STAB_TRIM_ZERO")), "AL_STAB_TRIM"), "ALC_STAB_TRIM"),
            Also(Done(Skip(SW("AL_SPEED_BRAKE", "Speed brake lever: forward", L1011Levers.SpeedBrakeKey, 0),
                s => s.IsOn("FO_SPEED_BRAKE_FORWARD")), "AL_SPEED_BRAKE"), "ALC_SPEED_BRAKE"),
            // The flaps move on the ground only: left alone, aloud, airborne or with the ground unread.
            Also(Done(Skip(LeaveAlone(SW("AL_FLAPS_UP", "Flaps: up", L1011Levers.FlapHandleKey, 0),
                    L1011FoChecklistDefinitions.NotOnGround, L1011FoChecklistDefinitions.FlapsStayText),
                s => s.IsOn("FO_FLAPS_UP")), "AL_FLAPS_UP"), "ALC_FLAPS"),
            Also(Done(Skip(SW("AL_RADAR", "Weather radar: off", "SWITCH_WX_OFF", 1),
                s => s.IsPosition("SWITCH_WX_OFF", 1)), "AL_RADAR"), "ALC_RADAR"),
            Also(Done(Skip(Multi("AL_TRANSFERS_OFF", "1A and 3A transfers: off", AllTo(0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A")),
                s => s.IsOn("FO_TRANSFERS_OFF")), "AL_TRANSFERS_OFF"), "ALC_TRANSFERS"),
            Also(Done(Skip(SW("AL_APU_MASTER", "APU master: on", "TOGGLE_APU_POWER", 1),
                s => s.IsPosition("TOGGLE_APU_POWER", 1)), "AL_APU_MASTER"), "ALC_APU_MASTER"),
            // One press, done once the APU turns: a press while it already turns would be a second start. Parking
            // waits for it to run.
            Also(Done(Skip(SW("AL_APU_START", "APU: start", X.ApuStart, 1),
                s => s.IsOn("FO_APU_STARTING_OR_RUNNING")), "AL_APU_START"), "ALC_APU"),
        }
    };

    // ------------------------------------------------------------------ Parking (the manual's parking)

    /// <summary>How long Parking waits for the aircraft to stop, the APU to run, a power source to come on line and the
    /// engines to spin down.</summary>
    private const int ParkingWaitSeconds = 120;

    /// <summary>The aircraft reads stopped below this ground speed, in knots.</summary>
    private const double StoppedBelowKnots = 1;

    /// <summary>What the APU wait says when the APU does not turn: the flow goes on to external power.</summary>
    private const string ApuNotStartedText = "APU not started: external power will be used.";

    private static readonly string[] InsModeKeys = { "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3" };

    private static readonly string[] FuelIgnitionKeys = { "TOGGLE_CUTOFF_ENG_1", "TOGGLE_CUTOFF_ENG_2", "TOGGLE_CUTOFF_ENG_3" };

    private static readonly string[] CrossfeedKeys = { "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED" };

    /// <summary>The fuel panel for the APU: the tank 2 left pump on first, so the APU never loses its feed, then the other
    /// seven pumps off, the engine 2 tank valve on under its closed guard, and the crossfeeds closed.</summary>
    private static readonly (string EventName, int? TargetValue)[] ApuFuelPanel =
        new[] { ("SWITCH_FUEL_PUMP_2L_1", (int?)1) }
            .Concat(AllTo(0, FuelPumpKeys.Where(k => k != "SWITCH_FUEL_PUMP_2L_1").ToArray()))
            .Concat(new[] { ("SWITCH_ENG_2_TANK_VALVE", (int?)1), ("GUARD_ENG_2_TANK_VALVE", (int?)0) })
            .Concat(AllTo(0, CrossfeedKeys))
            .ToArray();

    private static Flow BuildParking() => new()
    {
        Id = "PARKING", Name = "Parking",
        Description = "Aircraft stopped; ground lights off, parking brake set; the APU generator or external power on line "
            + "before the fuel and ignition go off; once the engines stop, anti-collision lights and seat belts off, INS off, "
            + "transponder standby, ATMs and AC pumps off, the fuel panel set for the APU; APU bleed on.",
        RelatedChecklistGroupIds = new[] { "PARKING", "PARKING_CL" },
        Steps = new()
        {
            // The ground guard: read only, the stock ground speed, unread failing it. Links no line.
            WaitForField("PK_STOPPED", "Aircraft stopped", "GROUND_VELOCITY", v => v < StoppedBelowKnots, ParkingWaitSeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            Also(Done(Skip(Multi("PK_GROUND_LIGHTS", "Nose, taxi and runway turnoff lights: off", AllTo(0, GroundLightKeys)),
                s => s.IsOn("FO_GROUND_LIGHTS_OFF")), "PK_GROUND_LIGHTS"), "PKC_GROUND_LIGHTS"),
            Also(Done(Skip(SW("PK_PARKING_BRAKE", "Parking brake: set", L1011Levers.ParkingBrakeKey, 1),
                s => s.IsPosition(L1011Levers.ParkingBrakeKey, 1)), "PK_PARKING_BRAKE"), "PKC_PARKING_BRAKE"),

            // ---- a power source before the fuel cut: the APU After Landing started, else external power ----
            // Up to two minutes for the APU to run; an APU not turning is not waited for.
            LeaveAlone(Skip(WaitForField("PK_APU_RUNNING", "APU running", "FO_APU_RUNNING", On, ParkingWaitSeconds),
                    s => s.IsOn("FO_APU_RUNNING")),
                s => !s.IsOn("FO_APU_STARTING_OR_RUNNING"), ApuNotStartedText),
            Also(Done(Skip(LeaveAlone(SW("PK_APU_GEN", "APU generator: on line", X.ApuGenerator, 1),
                    L1011FoChecklistDefinitions.ApuNotRunning, L1011FoChecklistDefinitions.ApuGenStaysOffText),
                s => s.IsOn("FO_APU_GEN_ON_LINE")), "PK_APU_GEN"), "PKC_POWER"),
            Done(Skip(LeaveAlone(SW("PK_EXT_PWR", "External power: on", X.ExternalPower, 1),
                    L1011FoChecklistDefinitions.ExtPowerNotWanted, L1011FoChecklistDefinitions.ExtPowerStaysOffText),
                s => s.IsOn("FO_EXT_POWER_ON")), "PK_EXT_PWR"),
            // The fuel cut takes the engine generators off the buses: the APU generator or external power must be on line
            // first, or the aircraft drops to its battery. Read only; nothing on line stops the flow with the engines running.
            Skip(WaitForField("PK_POWER_SOURCE", "APU generator or external power on", "FO_POWER_SOURCE_ON", On,
                    ParkingWaitSeconds, onTimeout: FlowStepFailurePolicy.Stop),
                s => s.IsOn("FO_POWER_SOURCE_ON")),
            // Toggle-carrying switches: each key read before it is written, and refused from an unknown position (L1011-4).
            Also(Done(Skip(Multi("PK_FUEL_IGN_OFF", "Fuel and ignition switches: off", AllTo(0, FuelIgnitionKeys)),
                s => s.IsOn("FO_FUEL_IGNITION_OFF")), "PK_FUEL_IGN_OFF"), "PKC_FUEL_IGN"),

            // ---- the engines stopped before anything else (FO-25): the beacon, the ground crew's sign that engines turn,
            // goes off only once they stop, as the MD-11's does, and an engine that does not stop ends the flow here,
            // with the beacon on and before a tank pump goes off under it. Read only; links no line.
            WaitForField("PK_SPOOL_DOWN", "Engines stopped", "FO_ENGINES_STOPPED", On, ParkingWaitSeconds,
                onTimeout: FlowStepFailurePolicy.Stop),
            Also(Done(Skip(SW("PK_ANTICOL", "Anti-collision lights: off", "SWITCH_ANTICOL_LIGHT", 0),
                s => s.IsPosition("SWITCH_ANTICOL_LIGHT", 0)), "PK_ANTICOL"), "PKC_ANTICOL"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("PK_SEAT_BELTS", "Seat belt signs: off", "SWITCH_SEATBELT_LIGHT", 0),
                s => s.IsPosition("SWITCH_SEATBELT_LIGHT", 0)), "PK_SEAT_BELTS"), "PKC_SEAT_BELTS"),
            Also(Done(Skip(Multi("PK_INS_OFF", "INS: all off", AllTo(0, InsModeKeys)),
                s => s.IsOn("FO_INS_OFF")), "PK_INS_OFF"), "PKC_INS"),
            Also(Done(Skip(SW("PK_XPDR", "Transponder: standby", "ROTARY_TCAS_MODE", 1),
                s => s.IsPosition("ROTARY_TCAS_MODE", 1)), "PK_XPDR"), "PKC_XPDR"),
            Also(Done(Skip(Multi("PK_ATMS_AC_PUMPS", "ATMs and AC hydraulic pumps: off",
                    AllTo(0, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE", "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3")),
                s => s.IsOn("FO_ATMS_AC_PUMPS_OFF")), "PK_ATMS_AC_PUMPS"), "PKC_ATMS_PUMPS"),
            // The manual's after landing fuel panel, done here once the engines are cut. The APU keeps its feed.
            Also(Done(Skip(Multi("PK_FUEL_PANEL", "Fuel pumps: off except tank 2 left for the APU", ApuFuelPanel),
                s => s.IsOn("FO_ONLY_APU_PUMP_ON") && s.IsOn("FO_ENG2_FUEL_FEED") && s.IsOn("FO_CROSSFEEDS_CLOSED")),
                "PK_FUEL_PANEL"), "PKC_TANK_PUMPS"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("PK_APU_BLEED", "APU bleed: on", "SWITCH_APU_BLEED", 1),
                s => s.IsPosition("SWITCH_APU_BLEED", 1)), "PK_APU_BLEED"), "PKC_APU_BLEED"),
            Done(Captain("PK_CPT_GROUND_POWER", "Connect ground power on the tablet's Ground page if required"),
                "PK_CPT_GROUND_POWER"),
        }
    };

    // ------------------------------------------------------------------ Shutdown (the manual's securing)

    /// <summary>How long Shutdown waits for the APU to come to a standstill.</summary>
    private const int ApuStandstillSeconds = 150;

    /// <summary>What the battery says when the APU master was left on: the battery stays with it.</summary>
    private const string BatteryStaysText = "Battery left on: the APU did not stop.";

    /// <summary>What the pump cut says while the APU turns: the tank 2 left pump feeds it.</summary>
    private const string PumpsStayForApuText = "Fuel pumps left on: the APU is running.";

    private static Flow BuildShutdown() => new()
    {
        Id = "SHUTDOWN", Name = "Shutdown",
        Description = "Emergency lights and galley off; the APU shut down: generator breaker, bleed, stop, then master; "
            + "packs and external power off; fuel pumps off with the APU stopped; standby power, radio masters and the "
            + "battery off last.",
        RelatedChecklistGroupIds = new[] { "SHUTDOWN", "SHUTDOWN_CL" },
        Steps = new()
        {
            // It cuts the pumps and the battery: never airborne, never with an engine turning (FO-25). Each guard links
            // no line and fails unread.
            OnGroundGuard("SD_ON_GROUND"),
            EnginesStoppedGuard("SD_ENGINES_STOPPED"),
            Also(Done(Skip(Multi("SD_EMER_LIGHTS", "Emergency lights: off", ("GUARD_EMER_LIGHTING", 1), ("TOGGLE_EMER_LIGHTING", 0)),
                s => s.IsOn("FO_EMER_LIGHTS_OFF")), "SD_EMER_LIGHTS"), "SDC_EMER_LIGHTS"),
            Also(Done(Skip(Multi("SD_GALLEY", "Galley power: off", AllTo(0, GalleyKeys)),
                s => s.IsOn("FO_GALLEY_OFF")), "SD_GALLEY"), "SDC_GALLEY"),

            // ---- the APU, in the manual's order ----
            Also(Done(Skip(SW("SD_APU_GEN_TRIP", "APU generator breaker: trip", X.ApuGenerator, 0),
                s => s.IsOn("FO_APU_GEN_OFF_LINE")), "SD_APU_GEN_TRIP"), "SDC_APU_GEN_TRIP"),
            // A toggle-carrying switch: written only over its own position read (L1011-4).
            Also(Done(Skip(SW("SD_APU_BLEED_OFF", "APU bleed: off", "SWITCH_APU_BLEED", 0),
                s => s.IsPosition("SWITCH_APU_BLEED", 0)), "SD_APU_BLEED_OFF"), "SDC_APU_BLEED"),
            // One press, then the composite holds until the APU reads stopped (below 5 %). Already set below 5 %.
            Also(Done(Skip(SW("SD_APU_STOP", "APU: stop", X.ApuStop, 1),
                s => s.IsOn("FO_APU_OFF")), "SD_APU_STOP"), "SDC_APU_STOP"),
            // On to a standstill, below the 1 % the pump cut keeps the pumps for, so the two agree. Already set below it,
            // so an APU found stopped costs no wait; otherwise it runs, unread included, whether or not this run sent the
            // stop: an APU found coasting between 1 and 5 % (stopped to the stop step) is waited out before the master and
            // the pump cut. Read only; timed out, skipped aloud.
            Skip(WaitForField("SD_APU_STOPPED", "APU at a standstill", "L1011_RO_APU_RPM",
                    v => v < L1011FoStateEvaluator.ApuStartingRpm, ApuStandstillSeconds),
                s => s.GetValue("L1011_RO_APU_RPM") < L1011FoStateEvaluator.ApuStartingRpm),
            // Only once the stop is done or found done ("Already set" is not a skip).
            Also(Done(Skip(Requires(SW("SD_APU_MASTER_OFF", "APU master: off", "TOGGLE_APU_POWER", 0),
                    "SD_APU_STOP", ApuMasterStaysText),
                s => s.IsPosition("TOGGLE_APU_POWER", 0)), "SD_APU_MASTER_OFF"), "SDC_APU_MASTER"),

            // Behind the ground guard already; left alone off the ground anyway, as its tick is.
            Done(Skip(LeaveAlone(Multi("SD_PACKS_OFF", "Packs: off", AllTo(0, PackKeys)),
                    L1011FoChecklistDefinitions.NotOnGround, L1011FoChecklistDefinitions.PacksStayText),
                s => s.IsOn("FO_PACKS_OFF")), "SD_PACKS_OFF"),
            // Ground power, when it was used: a breaker read open is already set.
            Also(Done(Skip(SW("SD_EXT_PWR_OFF", "External power: off", X.ExternalPower, 0),
                s => s.IsOn("FO_EXT_POWER_OFF")), "SD_EXT_PWR_OFF"), "SDC_EXT_PWR"),
            // The tank 2 left pump feeds the APU from the moment it turns: left alone, aloud, while it reads 1 % or more.
            // An unread APU speed does not hold the cut back.
            Also(Done(Skip(LeaveAlone(Multi("SD_FUEL_PUMPS", "Fuel pumps: all off", AllTo(0, FuelPumpKeys)),
                    s => s.IsOn("FO_APU_STARTING_OR_RUNNING"), PumpsStayForApuText),
                s => s.IsOn("FO_FUEL_PUMPS_OFF")), "SD_FUEL_PUMPS"), "SDC_FUEL_PUMPS"),

            // ---- last: standby power, the radio masters, the battery ----
            Also(Done(Skip(Multi("SD_STBY_POWER_OFF", "Standby power: off", ("GUARD_STBY_POWER", 1), ("TOGGLE_STBY_POWER", 0)),
                s => s.IsPosition("TOGGLE_STBY_POWER", 0)), "SD_STBY_POWER_OFF"), "SDC_STBY_POWER"),
            Also(Done(Skip(Multi("SD_RADIO_MASTERS_OFF", "Radio master switches: off",
                    AllTo(0, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2")),
                s => s.IsOn("FO_RADIO_MASTERS_OFF")), "SD_RADIO_MASTERS_OFF"), "SDC_RADIO_MASTERS"),
            // Only once the APU master went off or was found off: a master left on (the APU did not stop) leaves the
            // battery on, as the PMDG 777's battery waits on its APU. And never while the APU reads turning, as its tick:
            // a master found off over an APU still turning is "Already set", not skipped, so the dependency alone would
            // let the battery go. An unread APU speed does not hold it back.
            Also(Done(Skip(Requires(LeaveAlone(SW("SD_BATTERY_OFF", "Battery: off", "TOGGLE_BATTERY", 0),
                        s => s.IsOn("FO_APU_STARTING_OR_RUNNING"), L1011FoChecklistDefinitions.ParkedApuOffText),
                    "SD_APU_MASTER_OFF", BatteryStaysText),
                s => s.IsPosition("TOGGLE_BATTERY", 0)), "SD_BATTERY_OFF"), "SDC_BATTERY"),
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
    /// Stops a cold-and-dark flow unless every engine reads stopped (<c>FO_ENGINES_STOPPED</c>: every N3 below 15,
    /// every fuel-and-ignition switch and every ground start switch off). An unread state is NaN, which fails the
    /// guard rather than letting a destructive step through. Its label names the engines and the fuel and ignition,
    /// so a refusal over stopped engines with a fuel and ignition switch left on is spoken truly.
    /// </summary>
    private static Step EnginesStoppedGuard(string id)
        => WaitForField(id, "Engines stopped, fuel and ignition off", "FO_ENGINES_STOPPED", v => v > 0.5, CheckSeconds,
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

    /// <summary>A Captain step whose reminder names what <paramref name="text"/> reads at dispatch; with no text it
    /// speaks its static reminder (<see cref="FlowStep{TState}.ReminderTextProvider"/>).</summary>
    private static Step Saying(Step step, Func<L1011FoStateEvaluator, string?> text)
    {
        step.ReminderTextProvider = text;
        return step;
    }

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
