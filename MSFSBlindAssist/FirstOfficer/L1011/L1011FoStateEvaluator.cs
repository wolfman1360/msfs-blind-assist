using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.Generic;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer's state evaluator. Reads the SimConnect cache by the
/// definition's own variable keys: switch rows and levers ride the batches, gauge readouts and the
/// tablet's take-off exports are OnRequest (polled here each second by the First Officer window), lamps
/// ride the batch. NaN until every input of a field is read, never 0: a guessed 0 reads "engines
/// stopped" or "switch off" and lets a guard or a line pass over a turning engine.
///
/// The engine reads are what the live aircraft shows. A cold engine's N3 gauge reads 1.4 to 1.9,
/// never 0, and idles near 59, so <see cref="EngineStoppedN3"/> and <see cref="EngineRunningN3"/> sit
/// well clear of both. The fuel-and-ignition truth is the <c>TOGGLE_CUTOFF_ENG_n</c> row, which reads the
/// stock fuel valve; the L:var of that name reads 1 on a cold aircraft and is never read. Engine 3's N2
/// arrives through <see cref="IFoEngine3N2Sink"/> and is stored, but no TriStar field uses it.
///
/// A few fields compare the cache against the plan: the take-off flaps (the tablet's export, else
/// SimBrief's), the cruise cabin altitude and the destination elevation (SimBrief, through
/// <see cref="SetPlannedPressurizationAltitudes"/>). Without a plan value they read NaN, never a guessed
/// target.
/// </summary>
public sealed class L1011FoStateEvaluator : LVarStateEvaluator, IFoEngine3N2Sink
{
    /// <summary>An engine's N3 below this is stopped (a cold gauge reads up to 1.9).</summary>
    public const double EngineStoppedN3 = 5;

    /// <summary>An engine's N3 at or above this is running (idle is about 59).</summary>
    public const double EngineRunningN3 = 50;

    /// <summary>The flap handle's detent and the flap angle agree within this many degrees.</summary>
    public const double FlapAngleToleranceDegrees = 1;

    /// <summary>The gear lever below this is down (0 down, 50 off, 100 up).</summary>
    public const double GearLeverDownBelow = 25;

    /// <summary>The gear lever above this is up (0 down, 50 off, 100 up).</summary>
    public const double GearLeverUpAbove = 75;

    /// <summary>The speed brake handle below this is forward (stowed).</summary>
    public const double SpeedBrakeForwardBelow = 5;

    /// <summary>The APU's speed at or above this is running.</summary>
    public const double ApuRunningRpm = 95;

    /// <summary>The APU's speed at or above this is starting or running.</summary>
    public const double ApuStartingRpm = 1;

    /// <summary>The APU's speed below this is stopped.</summary>
    public const double ApuStoppedRpm = 5;

    /// <summary>Tanks 1A and 3A hold fuel to transfer above this many pounds.</summary>
    public const double TransferTankMinLbs = 50;

    /// <summary>The stabilizer trim closer to 0 than this many degrees is zeroed.</summary>
    public const double StabTrimZeroDegrees = 0.2;

    /// <summary>An INS alignment status within this of 1 is navigating.</summary>
    public const double InsNavigatingTolerance = 0.1;

    /// <summary>The selected cabin altitude agrees with its target within this many feet.</summary>
    public const double CabinAltitudeToleranceFt = 150;

    /// <summary>The cabin baro agrees with the captain's altimeter closer than this many hectopascals.</summary>
    public const double CabinBaroToleranceHpa = 0.5;

    /// <summary>The captain's altimeter within this of 1013.25 is standard, which gives the cabin baro no target.</summary>
    public const double StandardMillibarsTolerance = 0.2;

    /// <summary>The selected cabin altitude is written in feet and delivered in metres (<c>L1011Readouts</c>' Scale).</summary>
    public const double MetresPerFoot = 0.3048;

    /// <summary>An INS mode selector's NAV position (0 OFF, 1 STBY, 2 ALIGN, 3 NAV, 4 ATT).</summary>
    public const int InsNav = 3;

    /// <summary>An INS alignment status of aligned: 90 not aligned, falling while aligning, 2 aligned, 1 navigating.</summary>
    public const int InsAligned = 2;

    /// <summary>An INS alignment status of navigating.</summary>
    private const double InsNavigatingStatus = 1;

    /// <summary>A take-off flap value in degrees within this of a detent is that detent.</summary>
    private const double FlapDetentToleranceDegrees = 0.5;

    private const string FlapsAngleKey = "L1011_RO_FLAPS_ANGLE";
    private const string ExtPowerBreakerKey = "L1011_RO_EXT_PWR_BREAKER";
    private const string ExtPowerAvailableKey = "LAMP_EXTERNAL_POWER_AVAILABLE";
    private const string ApuRpmKey = "L1011_RO_APU_RPM";
    private const string ApuGenFieldKey = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreakerKey = "L1011_RO_APU_GEN_BREAKER";
    private const string Tank1AKey = "L1011_RO_TANK_4";
    private const string Tank3AKey = "L1011_RO_TANK_5";
    private const string CabinAltSelectedKey = "L1011_RO_CABIN_ALT_SELECTED";
    private const string CabinBaroKey = "L1011_RO_CABIN_BARO";
    private const string CaptainAltimeterKey = "L1011_RO_ALTIMETER_1";
    private const string ElevatorTrimKey = "L1011_RO_ELEVATOR_TRIM";

    private double _eng3N2 = double.NaN;

    public void SetEngine3N2(double eng3N2) => Volatile.Write(ref _eng3N2, eng3N2);

    /// <summary>Engine 3's N2 as the window last fed it, NaN until then. No TriStar field reads it.</summary>
    internal double Engine3N2 => Volatile.Read(ref _eng3N2);

    // ------------------------------------------------------------------ polled readouts

    private static readonly string[] PollFields =
    {
        "L1011_RO_N3_1", "L1011_RO_N3_2", "L1011_RO_N3_3",
        FlapsAngleKey, ExtPowerBreakerKey,
        // Read by the executor's closed-loop external power (ties pressed only when open, verified closed).
        "L1011_RO_AC_TIE_1", "L1011_RO_AC_TIE_2", "L1011_RO_AC_TIE_3",
        // The tablet's take-off exports ("Set bugs and EPR"): the selected flaps and the stabilizer trim.
        IniL1011Definition.PerfSelectedFlapsKey, IniL1011Definition.PerfStabTrimKey,
        // The APU, the generators' field and breaker relays, tanks 1A and 3A, the pressurization panel's
        // selected cabin altitude and cabin baro, the captain's altimeter, the gear legs and the stabilizer
        // trim. Never the total fuel: the fuel hotkey's readout speaks every delivery of it.
        ApuRpmKey,
        "L1011_RO_GEN_1_FIELD", "L1011_RO_GEN_2_FIELD", "L1011_RO_GEN_3_FIELD",
        "L1011_RO_GEN_1_BREAKER", "L1011_RO_GEN_2_BREAKER", "L1011_RO_GEN_3_BREAKER",
        ApuGenFieldKey, ApuGenBreakerKey,
        Tank1AKey, Tank3AKey,
        CabinAltSelectedKey, CabinBaroKey, CaptainAltimeterKey,
        "L1011_RO_GEAR_LEFT", "L1011_RO_GEAR_NOSE", "L1011_RO_GEAR_RIGHT",
        ElevatorTrimKey,
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    // ------------------------------------------------------------------ plan values

    /// <summary>The take-off stabilizer trim the tablet exported, in its own units (live: 2.38): null
    /// while unread, and while it reads 0 or below (0 before any export).</summary>
    public double? TakeoffStabTrimUnits() =>
        Raw(IniL1011Definition.PerfStabTrimKey) is var units && units > 0 ? units : null;

    // Written on the UI thread (SimBrief load), read on a pool thread by the checklist's auto-detect: the
    // PMDG 737's Volatile handoff, without its rounding (the TriStar's targets compare within a tolerance).
    private const int NoPlanValue = int.MinValue;
    private int _plannedCruiseFt = NoPlanValue;
    private int _plannedDestElevFt = NoPlanValue;

    /// <summary>Stores SimBrief's cruise altitude and destination elevation as given; null clears one.</summary>
    public override void SetPlannedPressurizationAltitudes(int? cruiseAltFt, int? destElevFt)
    {
        Volatile.Write(ref _plannedCruiseFt, cruiseAltFt ?? NoPlanValue);
        Volatile.Write(ref _plannedDestElevFt, destElevFt ?? NoPlanValue);
    }

    /// <summary>SimBrief's planned cruise altitude in feet, null without a plan.</summary>
    public int? PlannedCruiseFt => Planned(Volatile.Read(ref _plannedCruiseFt));

    /// <summary>SimBrief's destination elevation in feet (below sea level kept), null without a plan.</summary>
    public int? PlannedDestElevFt => Planned(Volatile.Read(ref _plannedDestElevFt));

    private static int? Planned(int stored) => stored == NoPlanValue ? null : stored;

    /// <summary>
    /// The take-off flap handle index (1 to 6 for 4, 10, 14, 18, 22 and 33 degrees): the tablet's
    /// export first, else SimBrief's take-off flaps, else null. 0, -1, an unread tablet and any value
    /// off a detent are no value (D9).
    /// </summary>
    public int? TakeoffFlapIndex() => TakeoffFlapIndexFrom(Raw, GetTakeoffFlaps());

    private static int? TakeoffFlapIndexFrom(Func<string, double> read, int simBriefFlaps)
        => FlapIndexForDegrees(read(IniL1011Definition.PerfSelectedFlapsKey)) ?? FlapIndexForDegrees(simBriefFlaps);

    /// <summary>The flap handle index of a take-off flap setting in degrees: within half a degree of
    /// 4, 10, 14, 18, 22 or 33 is 1 to 6; anything else (0 included) is null.</summary>
    public static int? FlapIndexForDegrees(double degrees)
    {
        for (int index = 1; index < L1011Levers.FlapDegrees.Length; index++)
            if (Math.Abs(degrees - L1011Levers.FlapDegrees[index]) <= FlapDetentToleranceDegrees)
                return index;
        return null;
    }

    /// <summary>The cabin altitude that holds the maximum differential at the planned cruise, null without a plan.</summary>
    public int? CruiseCabinAltitudeFt() => CruiseCabinAltitudeFor(PlannedCruiseFt);

    private static int? CruiseCabinAltitudeFor(int? cruiseFt)
        => cruiseFt is int cruise ? L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(cruise) : null;

    /// <summary>
    /// The cabin baro's target in hundredths of a hectopascal: the captain's altimeter, read and off
    /// standard. Null while it is unread or within <see cref="StandardMillibarsTolerance"/> of 1013.25,
    /// where the captain has set no local pressure to copy (D2).
    /// </summary>
    public int? CabinBaroTargetHundredths()
        => OffStandard(Raw(CaptainAltimeterKey)) is double setting ? (int)Math.Round(setting * 100) : null;

    /// <summary>The altimeter setting when read and off standard, else null.</summary>
    private static double? OffStandard(double millibars)
        => !double.IsNaN(millibars) && Math.Abs(millibars - L1011FoActionExecutor.StandardMillibars) > StandardMillibarsTolerance
            ? millibars
            : null;

    /// <summary>
    /// <see cref="InsNav"/> for a unit whose selector reads ALIGN with its status aligned (within 0.5 of
    /// <see cref="InsAligned"/>), or whose selector already reads NAV; null otherwise (unread included), so a
    /// step turning the selector to NAV is a silent skip while the unit still aligns. A unit the pilot left at
    /// OFF, STBY or ATT, or whose selector is unread, also gets null whatever its status reads: the First
    /// Officer never switches a unit on, and leaves it to the Captain.
    /// </summary>
    public int? InsNavTarget(int unit)
    {
        double selector = Raw(L1011InsAlignmentAnnouncer.MsuKey(unit));
        return At(selector, InsNav)
            || (At(selector, InsAlign) && At(Raw(L1011InsAlignmentAnnouncer.StatusKey(unit)), InsAligned))
            ? InsNav
            : null;
    }

    // ------------------------------------------------------------------ synthetic fields

    /// <summary>
    /// What a field may compare against beyond the cache: SimBrief's take-off flaps (-1 none), its
    /// cruise altitude and its destination elevation (null none). The pure computation takes
    /// <see cref="None"/> unless a test gives one.
    /// </summary>
    internal readonly record struct Plan(int SimBriefTakeoffFlaps, int? CruiseFt, int? DestElevFt)
    {
        public static Plan None => new(-1, null, null);
    }

    private Plan CurrentPlan => new(GetTakeoffFlaps(), PlannedCruiseFt, PlannedDestElevFt);

    private delegate double Fn(Func<string, double> read, Plan plan);
    private sealed record Synth(string[] Inputs, Fn Compute);

    private static double B(bool b) => b ? 1 : 0;
    private static bool Known(IEnumerable<double> v) => v.All(x => !double.IsNaN(x));

    /// <summary>A switch or selector reads this position (positions are whole numbers).</summary>
    private static bool At(double v, double position) => Math.Abs(v - position) < 0.5;

    /// <summary>A relay (the readouts' <c>ELECTRICAL RELAY CONNECTION ON</c>) reads closed.</summary>
    private static bool Closed(double relay) => relay >= 0.5;

    /// <summary>A field computed from switch, lever, readout or lamp values: NaN unless every one was read.</summary>
    private static Synth Vars(Func<double[], bool> ok, params string[] keys)
        => new(keys, (r, _) =>
        {
            var v = keys.Select(r).ToArray();
            return Known(v) ? B(ok(v)) : double.NaN;
        });

    /// <summary>A field true when every key reads <paramref name="position"/>.</summary>
    private static Synth AllAt(double position, params string[] keys)
        => Vars(v => v.All(x => At(x, position)), keys);

    /// <summary>A field true when each key reads its own position.</summary>
    private static Synth AtEach(params (string Key, double Position)[] wants)
        => Vars(v => v.Select((x, i) => At(x, wants[i].Position)).All(ok => ok), wants.Select(w => w.Key).ToArray());

    /// <summary>A field true when every relay reads closed.</summary>
    private static Synth AllClosed(params string[] relays) => Vars(v => v.All(Closed), relays);

    /// <summary>A field true when every lamp is out, each by its own rule (<see cref="L1011Annunciators.IsLit"/>).</summary>
    private static Synth LampsOut(params string[] lampKeys)
    {
        var lamps = lampKeys.Select(LampFor).ToArray();
        return Vars(v => v.Select((x, i) => !L1011Annunciators.IsLit(lamps[i], x)).All(o => o), lampKeys);
    }

    private static L1011Lamp LampFor(string key) => L1011Annunciators.All.Single(l => L1011Annunciators.KeyFor(l) == key);

    /// <summary>
    /// The selected cabin altitude, delivered in metres, against a target in feet: within
    /// <see cref="CabinAltitudeToleranceFt"/>. NaN while the readout is unread or there is no target.
    /// </summary>
    private static Synth CabinAltitudeAt(Func<Plan, int?> target)
        => new(new[] { CabinAltSelectedKey }, (r, plan) =>
        {
            double metres = r(CabinAltSelectedKey);
            return target(plan) is int feet && !double.IsNaN(metres)
                ? B(Math.Abs(metres / MetresPerFoot - feet) <= CabinAltitudeToleranceFt)
                : double.NaN;
        });

    private static string[] Numbered(string format, int count)
        => Enumerable.Range(1, count).Select(n => string.Format(format, n)).ToArray();

    private static readonly string[] N3Keys = Numbered("L1011_RO_N3_{0}", 3);
    private static readonly string[] FuelCutoffKeys = Numbered("TOGGLE_CUTOFF_ENG_{0}", 3);

    private static readonly string[] FireHandleKeys =
        Numbered("HANDLE_FIRE_{0}", 3).Append("HANDLE_FIRE_APU").ToArray();

    private static readonly string[] FuelPumpKeys =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    /// <summary>The tank 2 left pump that feeds the APU.</summary>
    private const string ApuFuelPumpKey = "SWITCH_FUEL_PUMP_2L_1";

    private static readonly string[] GenFieldKeys = Numbered("L1011_RO_GEN_{0}_FIELD", 3);
    private static readonly string[] GenBreakerKeys = Numbered("L1011_RO_GEN_{0}_BREAKER", 3);
    private static readonly string[] AcTieKeys = Numbered("L1011_RO_AC_TIE_{0}", 3);
    private static readonly string[] IsolationValveKeys = Numbered("SWITCH_ISLN_VALVE_ENG_{0}", 3);
    private static readonly string[] CrossbleedKeys = { "SWITCH_XBLEED_1", "SWITCH_XBLEED_3" };
    private static readonly string[] PackKeys = Numbered("SWITCH_PACK_FLOW_ENG_{0}", 3);
    private static readonly string[] HotAirKeys =
        { "SWITCH_HOT_AIR_ENG_1", "SWITCH_HOT_AIR_ENG_3", "SWITCH_ISLN_VALVE_OVHT_2A", "SWITCH_ISLN_VALVE_OVHT_2B" };
    private static readonly string[] AtmKeys = { "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE" };
    private static readonly string[] AcPumpKeys = { "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3" };
    private static readonly string[] TransferKeys = { "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A" };
    private static readonly string[] InsStatusKeys =
        Enumerable.Range(1, 3).Select(L1011InsAlignmentAnnouncer.StatusKey).ToArray();
    private static readonly string[] WindshieldKeys = { "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO" };
    private static readonly string[] SideWindowKeys =
        { "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR" };
    private static readonly string[] LandingLightKeys = { "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT" };
    private static readonly string[] GroundLightKeys = { "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF" };

    /// <summary>The primary flight control switches: pitch, roll, rudder, the four stabilizer, the four
    /// aileron and the six spoiler switches. The rudder's guard is not read.</summary>
    private static readonly string[] PfcsKeys =
    {
        "SWITCH_PFCS_PITCH_1", "SWITCH_PFCS_PITCH_2", "SWITCH_PFCS_ROLL_1", "SWITCH_PFCS_ROLL_2", "SWITCH_PFCS_RUDDER",
        "SWITCH_STABILIZER_A", "SWITCH_STABILIZER_B", "SWITCH_STABILIZER_C", "SWITCH_STABILIZER_D",
        "SWITCH_AILERON_L_OUTBD", "SWITCH_AILERON_L_INBD", "SWITCH_AILERON_R_INBD", "SWITCH_AILERON_R_OUTBD",
        "SWITCH_SPOILER_L56", "SWITCH_SPOILER_LR4", "SWITCH_SPOILER_LR1", "SWITCH_SPOILER_LR2", "SWITCH_SPOILER_LR3",
        "SWITCH_SPOILER_R56",
    };

    /// <summary>The flight control electronic system switches, each in its pair.</summary>
    private static readonly string[] FcesKeys =
    {
        "SWITCH_ACS_1", "SWITCH_ACS_2", "SWITCH_YAW_SAS_1", "SWITCH_YAW_SAS_2", "SWITCH_STALL_WARN_1", "SWITCH_STALL_WARN_2",
        "SWITCH_PITCH_TRIM_1", "SWITCH_PITCH_TRIM_2", "SWITCH_DLC_AUTO_1", "SWITCH_DLC_AUTO_2", "SWITCH_ATS_1", "SWITCH_ATS_2",
        "SWITCH_MACH_TRIM_1", "SWITCH_MACH_TRIM_2",
    };

    /// <summary>The captain's and first officer's instrument source selectors (0 normal).</summary>
    private static readonly string[] InstrumentSourceKeys =
        new[] { "NAV", "HDG", "ATT", "ALT", "IAS", "FLT_DIR" }.Select(s => "SWITCH_INSTR_SRC_CPT_" + s)
            .Concat(new[] { "NAV", "HDG", "ATT", "ALT", "IAS", "FLT_DIR" }.Select(s => "SWITCH_INSTR_SRC_FO_" + s))
            .ToArray();

    /// <summary>An engine's own feed: its pump, its tank valve on and the valve's guard closed.</summary>
    private static Synth FuelFeed(string pumpKey, int engine) => AtEach(
        (pumpKey, 1),
        ($"SWITCH_ENG_{engine}_TANK_VALVE", 1),
        ($"GUARD_ENG_{engine}_TANK_VALVE", 0));

    /// <summary>The jettison master and the four dump switches, then their five guards (0 closed).</summary>
    private static readonly string[] JettisonKeys =
    {
        "SWITCH_FUEL_DUMP_MASTER", "SWITCH_FUEL_DUMP_2L", "SWITCH_FUEL_DUMP_1", "SWITCH_FUEL_DUMP_3", "SWITCH_FUEL_DUMP_2R",
        "GUARD_FUEL_DUMP_MASTER", "GUARD_FUEL_DUMP_2L", "GUARD_FUEL_DUMP_1", "GUARD_FUEL_DUMP_3", "GUARD_FUEL_DUMP_2R",
    };

    private static readonly string[] InsModeKeys = Numbered("ROTARY_INS_MODE_{0}", 3);

    /// <summary>The ALIGN position of an INS mode selector (NAV is <see cref="InsNav"/>).</summary>
    private const double InsAlign = 2;

    private static readonly L1011Lamp ExternalPowerLamp = LampFor(ExtPowerAvailableKey);

    /// <summary>The flap handle's index maps to a detent angle; the flaps agree when the angle follows within a degree.</summary>
    private static bool FlapsAgree(double handleIndex, double angle)
    {
        double index = Math.Round(handleIndex);
        return index >= 0 && index < L1011Levers.FlapDegrees.Length
            && Math.Abs(angle - L1011Levers.FlapDegrees[(int)index]) <= FlapAngleToleranceDegrees;
    }

    private static readonly Dictionary<string, Synth> Synthetics = new(StringComparer.Ordinal)
    {
        // ---- engines ----
        // Stopped: every N3 below the stopped threshold AND every fuel-and-ignition switch off.
        ["FO_ENGINES_STOPPED"] = Vars(
            v => v.Take(3).All(n3 => n3 < EngineStoppedN3) && v.Skip(3).All(sw => At(sw, 0)),
            N3Keys.Concat(FuelCutoffKeys).ToArray()),
        ["FO_ENG1_RUNNING"] = Vars(v => v[0] >= EngineRunningN3, N3Keys[0]),
        ["FO_ENG2_RUNNING"] = Vars(v => v[0] >= EngineRunningN3, N3Keys[1]),
        ["FO_ENG3_RUNNING"] = Vars(v => v[0] >= EngineRunningN3, N3Keys[2]),
        ["FO_FUEL_IGNITION_OFF"] = AllAt(0, FuelCutoffKeys),

        // ---- levers and handles ----
        ["FO_FIRE_HANDLES_IN"] = AllAt(0, FireHandleKeys),
        ["FO_GEAR_LEVER_DOWN"] = Vars(v => v[0] < GearLeverDownBelow, L1011Levers.GearLeverKey),
        ["FO_SPEED_BRAKE_FORWARD"] = Vars(v => v[0] < SpeedBrakeForwardBelow, L1011Levers.SpeedBrakeKey),
        ["FO_FLAPS_AGREE"] = Vars(v => FlapsAgree(v[0], v[1]), L1011Levers.FlapHandleKey, FlapsAngleKey),

        // ---- switches ----
        ["FO_RADIO_MASTERS_ON"] = AllAt(1, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2"),
        ["FO_ATMS_OFF"] = AllAt(0, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE"),
        ["FO_AC_PUMPS_OFF"] = AllAt(0, "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3"),
        ["FO_FUEL_PUMPS_OFF"] = AllAt(0, FuelPumpKeys),
        // Master and dump switches off, their guards closed.
        ["FO_JETTISON_SAFE"] = AllAt(0, JettisonKeys),
        ["FO_PAX_OXY_SAFE"] = AllAt(0, "SWITCH_PAX_OXY_MANUAL", "GUARD_PAX_OXY_MANUAL"),
        ["FO_GALLEY_OFF"] = AllAt(0, Numbered("SWITCH_GALLEY_BUS_{0}", 3)),
        // Standby power on ARM (1) with its guard closed (0).
        ["FO_STBY_ARMED"] = Vars(v => At(v[0], 1) && At(v[1], 0), "TOGGLE_STBY_POWER", "GUARD_STBY_POWER"),

        // ---- external power and INS ----
        // The external power breaker is the relay the engineer's panel shows: 1 closed (on line).
        ["FO_EXT_POWER_ON"] = Vars(v => At(v[0], 1), ExtPowerBreakerKey),
        ["FO_EXT_POWER_AVAILABLE"] = Vars(v => L1011Annunciators.IsLit(ExternalPowerLamp, v[0]), ExtPowerAvailableKey),
        ["FO_INS_ALIGN_OR_NAV"] = Vars(v => v.All(m => At(m, InsAlign) || At(m, InsNav)), InsModeKeys),

        // ================================================================ the flows' fields

        // ---- APU: its speed and its generator's field and breaker relays ----
        ["FO_APU_RUNNING"] = Vars(v => v[0] >= ApuRunningRpm, ApuRpmKey),
        ["FO_APU_STARTING_OR_RUNNING"] = Vars(v => v[0] >= ApuStartingRpm, ApuRpmKey),
        ["FO_APU_OFF"] = Vars(v => v[0] < ApuStoppedRpm, ApuRpmKey),
        ["FO_APU_GEN_FIELD_CLOSED"] = AllClosed(ApuGenFieldKey),
        ["FO_APU_GEN_ON_LINE"] = AllClosed(ApuGenBreakerKey),
        ["FO_APU_GEN_CLOSED"] = AllClosed(ApuGenFieldKey, ApuGenBreakerKey),
        ["FO_APU_GEN_OFF_LINE"] = Vars(v => !Closed(v[0]), ApuGenBreakerKey),

        // ---- electrical ----
        ["FO_GEN_FIELDS_CLOSED"] = AllClosed(GenFieldKeys),
        ["FO_GEN_BREAKERS_CLOSED"] = AllClosed(GenBreakerKeys),
        ["FO_ENGINE_GENERATORS_ON"] = AllClosed(GenFieldKeys.Concat(GenBreakerKeys).ToArray()),
        ["FO_AC_TIES_CLOSED"] = AllClosed(AcTieKeys),
        ["FO_EXT_POWER_OFF"] = Vars(v => !Closed(v[0]), ExtPowerBreakerKey),
        // Either source on line is enough, the other unread (the base's AnyLit): 1 when either breaker reads
        // closed, 0 when both read open, NaN otherwise.
        ["FO_POWER_SOURCE_ON"] = new(new[] { ApuGenBreakerKey, ExtPowerBreakerKey },
            (r, _) => AnyLit(r(ApuGenBreakerKey), r(ExtPowerBreakerKey))),
        // The IDG lights are lit below 40 psi of IDG oil pressure (L1011Annunciators).
        ["FO_IDG_LIGHTS_OUT"] = LampsOut(Numbered("LAMP_IDG_OIL_PRESSURE_{0}", 3)),
        ["FO_BUS_FAIL_LIGHTS_OUT"] = LampsOut("LAMP_AC_ESS_FAIL_LIGHT", "LAMP_AC_STBY_FAIL_LIGHT",
            "LAMP_DC_ESS_FAIL_LIGHT", "LAMP_DC_STBY_FAIL_LIGHT"),
        ["FO_RADIO_MASTERS_OFF"] = AllAt(0, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2"),
        ["FO_GALLEY_ON"] = AllAt(1, Numbered("SWITCH_GALLEY_BUS_{0}", 3)),
        ["FO_EMER_LIGHTS_ARMED"] = AtEach(("TOGGLE_EMER_LIGHTING", 1), ("GUARD_EMER_LIGHTING", 0)),
        ["FO_EMER_LIGHTS_OFF"] = AllAt(0, "TOGGLE_EMER_LIGHTING"),

        // ---- bleeds and air ----
        ["FO_CROSSBLEEDS_ON"] = AllAt(1, CrossbleedKeys),
        ["FO_APU_AIR_ON"] = AllAt(1, new[] { "SWITCH_APU_ISLN_VALVE" }.Concat(CrossbleedKeys).ToArray()),
        ["FO_ISLN_VALVES_ON"] = AllAt(1, IsolationValveKeys),
        ["FO_START_AIR_VALVES_ON"] = AllAt(1, IsolationValveKeys.Concat(CrossbleedKeys).ToArray()),
        ["FO_HI_PRESS_ON"] = AllAt(1, Numbered("SWITCH_HI_PRESS_ENG_{0}", 3)),
        ["FO_PACKS_ON"] = AllAt(1, PackKeys),
        ["FO_PACKS_OFF"] = AllAt(0, PackKeys),
        ["FO_PACK_2_ONLY"] = AtEach((PackKeys[0], 0), (PackKeys[1], 1), (PackKeys[2], 0)),
        ["FO_PACKS_AUTO"] = AllAt(1, Numbered("SWITCH_BLEED_PLENUM_ENG_{0}", 3)),
        ["FO_HOT_AIR_OFF"] = AllAt(0, HotAirKeys),
        ["FO_HOT_AIR_ON"] = AllAt(1, HotAirKeys),
        ["FO_ZONE_TRIMS_ON"] = AllAt(1, "SWITCH_FLT_STA_CLOSE", "SWITCH_FWD_CAB_CLOSE", "SWITCH_MID_CAB_CLOSE", "SWITCH_AFT_CAB_CLOSE"),
        ["FO_ECS_ON"] = AllAt(1, "SWITCH_COOL_AIR_OVBD", "SWITCH_CARGO_HEAT_FWD", "SWITCH_CARGO_HEAT_MID", "SWITCH_CARGO_HEAT_AFT"),
        // The fire detection loop selectors at BOTH LOOPS (2).
        ["FO_FIRE_LOOPS_BOTH"] = AllAt(2, Numbered("ROTARY_FIRE_DETECT_TEST_ENG_{0}", 3).Append("ROTARY_FIRE_DETECT_TEST_APU").ToArray()),

        // ---- hydraulics ----
        ["FO_HYD_ENGINE_PUMPS_ON"] = AtEach(
            ("SWITCH_HYD_PUMP_A1", 1), ("SWITCH_HYD_PUMP_B1", 1), ("SWITCH_HYD_PUMP_C1", 1), ("SWITCH_HYD_PUMP_D1", 1),
            ("GUARD_HYD_PUMP_A1", 0), ("GUARD_HYD_PUMP_B1", 0), ("GUARD_HYD_PUMP_C1", 0), ("GUARD_HYD_PUMP_D1", 0)),
        ["FO_PTUS_OFF"] = AllAt(0, "SWITCH_PTU_AB", "SWITCH_PTU_CD"),
        ["FO_ATMS_AUTO"] = AllAt(1, AtmKeys),
        ["FO_ATMS_AC_PUMPS_OFF"] = AllAt(0, AtmKeys.Concat(AcPumpKeys).ToArray()),

        // ---- fuel ----
        ["FO_FUEL_PUMPS_ON"] = AllAt(1, FuelPumpKeys),
        ["FO_ANY_FUEL_PUMP_ON"] = Vars(v => v.Any(p => At(p, 1)), FuelPumpKeys),
        ["FO_ONLY_APU_PUMP_ON"] = AtEach(FuelPumpKeys.Select(k => (k, k == ApuFuelPumpKey ? 1.0 : 0.0)).ToArray()),
        // A crossfeed reads 0 closed (confirmed live).
        ["FO_CROSSFEEDS_CLOSED"] = AllAt(0, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"),
        ["FO_TRANSFERS_OFF"] = AllAt(0, TransferKeys),
        ["FO_TRANSFERS_ON"] = AllAt(1, TransferKeys),
        // Tanks 1A and 3A are the readouts' tanks 4 and 5 (D10).
        ["FO_TANKS_1A_3A_FUELED"] = Vars(v => v.All(lbs => lbs > TransferTankMinLbs), Tank1AKey, Tank3AKey),
        ["FO_TANK_VALVES_ON"] = AtEach(
            ("SWITCH_ENG_1_TANK_VALVE", 1), ("SWITCH_ENG_2_TANK_VALVE", 1), ("SWITCH_ENG_3_TANK_VALVE", 1),
            ("GUARD_ENG_1_TANK_VALVE", 0), ("GUARD_ENG_2_TANK_VALVE", 0), ("GUARD_ENG_3_TANK_VALVE", 0)),
        ["FO_ENG1_FUEL_FEED"] = FuelFeed("SWITCH_FUEL_PUMP_1_1", 1),
        ["FO_ENG2_FUEL_FEED"] = FuelFeed(ApuFuelPumpKey, 2),
        ["FO_ENG3_FUEL_FEED"] = FuelFeed("SWITCH_FUEL_PUMP_3_1", 3),
        ["FO_FUEL_CTRL_OVRD_OFF"] = AllAt(0, Numbered("SWITCH_FUEL_CTRL_OVRD_ENG_{0}", 3)),

        // ---- engines and start: the selector reads 0 ignition A, 1 both, 2 ignition B ----
        ["FO_IGNITION_SELECTED"] = Vars(v => At(v[0], 0) || At(v[0], 2), "ROTARY_ENG_START_SEL"),
        ["FO_IGNITION_BOTH"] = AllAt(1, "ROTARY_ENG_START_SEL"),
        ["FO_START_LIGHTS_OUT"] = LampsOut(Numbered("LAMP_SWITCH_GND_START_ENG_{0}_LIGHT", 3)),

        // ---- INS and doors ----
        ["FO_INS_NAVIGATING"] = Vars(v => v.All(s => Math.Abs(s - InsNavigatingStatus) <= InsNavigatingTolerance), InsStatusKeys),
        ["FO_INS_NAV_MODE"] = AllAt(InsNav, InsModeKeys),
        ["FO_INS_OFF"] = AllAt(0, InsModeKeys),
        ["FO_DOORS_CLOSED"] = LampsOut("LAMP_LX_MIP_DOOR_OPEN"),

        // ---- overhead ----
        ["FO_COMPASSES_MAG"] = AllAt(1, "SWITCH_COMPASS_1_MAG", "SWITCH_COMPASS_2_MAG"),
        ["FO_WIPERS_OFF"] = AllAt(0, "ROTARY_CPT_WIPER", "ROTARY_FO_WIPER"),
        ["FO_ANTI_ICE_OFF"] = AllAt(0, Numbered("SWITCH_ANTI_ICE_ENG_{0}", 3).Concat(new[]
        {
            "SWITCH_ANTI_ICE_LEFT_AUTO", "SWITCH_ANTI_ICE_LEFT_MAN", "SWITCH_ANTI_ICE_RIGHT_AUTO", "SWITCH_ANTI_ICE_RIGHT_MAN",
        }).ToArray()),
        // The windshield switches read 1 NORMAL, 0 IDLE.
        ["FO_WINDSHIELD_NORMAL"] = AllAt(1, WindshieldKeys),
        ["FO_WINDSHIELD_IDLE"] = AllAt(0, WindshieldKeys),
        ["FO_SIDE_WINDOWS_ON"] = AllAt(1, SideWindowKeys),
        ["FO_SIDE_WINDOWS_OFF"] = AllAt(0, SideWindowKeys),
        ["FO_AIR_DATA_HEAT_OFF"] = AllAt(0, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT"),
        ["FO_PITOT_ALPHA_HEAT_ON"] = AllAt(1, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_ALPHA_HEAT"),
        ["FO_MACH_FEEL_RUDDER_ON"] = AllAt(1, "SWITCH_MACH_FEEL_1", "SWITCH_MACH_FEEL_2", "SWITCH_RUDDER_LIMITER"),
        ["FO_PFCS_ON"] = AllAt(1, PfcsKeys),
        ["FO_FCES_ON"] = AllAt(1, FcesKeys),
        // Logo and position lights on, the landing, runway turnoff, strobe and anti-collision lights off,
        // and the flood and wheel well lights off, which read 1 OFF.
        ["FO_EXT_LIGHTS_PREFLIGHT"] = AtEach(
            ("TOGGLE_LOGO_LIGHT", 1), ("SWITCH_POSITION_LIGHT", 1),
            ("TOGGLE_LANDING_LEFT", 0), ("TOGGLE_LANDING_RIGHT", 0), ("TOGGLE_LANDING_NOSE", 0), ("TOGGLE_RWY_TURNOFF", 0),
            ("SWITCH_STROBE_LIGHT", 0), ("SWITCH_ANTICOL_LIGHT", 0),
            ("SWITCH_FLOOD_LIGHT", 1), ("SWITCH_WHEEL_LIGHT", 1)),
        ["FO_LANDING_LIGHTS_ON"] = AllAt(1, LandingLightKeys),
        ["FO_LANDING_LIGHTS_OFF"] = AllAt(0, LandingLightKeys),
        ["FO_GROUND_LIGHTS_ON"] = AllAt(1, GroundLightKeys),
        ["FO_GROUND_LIGHTS_OFF"] = AllAt(0, GroundLightKeys),
        ["FO_MOVIE_OFF"] = AllAt(0, "MOVIES_OFF", "GUARD_MOVIES_OFF"),

        // ---- main panel and console ----
        ["FO_INSTR_SOURCES_NORMAL"] = AllAt(0, InstrumentSourceKeys),
        ["FO_DME_NORMAL"] = AllAt(1, "ROTARY_NAV_1_MODE", "ROTARY_NAV_2_MODE"),
        ["FO_FLIGHT_DIRECTORS_ON"] = AllAt(1, "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B"),
        // An engage paddle reads 0 COMMAND, 1 CWS, 2 OFF.
        ["FO_PADDLES_OFF"] = AllAt(2, "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B"),
        ["FO_DISCONNECT_HANDLES_STOWED"] = AllAt(0, "LEVER_PITCH_DISC", "LEVER_ROLL_DISC"),

        // ---- levers ----
        ["FO_FLAPS_UP"] = Vars(v => Math.Round(v[0]) == 0, L1011Levers.FlapHandleKey),
        // The handle at the take-off index: the tablet's export, else SimBrief's. The tablet is one of the
        // two sources, so it is listed (polled, registered) but may be unread; the field is NaN only when
        // there is no index at all or the handle is unread.
        ["FO_FLAPS_TAKEOFF"] = new(new[] { L1011Levers.FlapHandleKey, IniL1011Definition.PerfSelectedFlapsKey }, (r, plan) =>
        {
            double handle = r(L1011Levers.FlapHandleKey);
            return TakeoffFlapIndexFrom(r, plan.SimBriefTakeoffFlaps) is int index && !double.IsNaN(handle)
                ? B(Math.Round(handle) == index)
                : double.NaN;
        }),
        ["FO_GEAR_LEVER_UP"] = Vars(v => v[0] > GearLeverUpAbove, L1011Levers.GearLeverKey),
        ["FO_STAB_TRIM_ZERO"] = Vars(v => Math.Abs(v[0]) < StabTrimZeroDegrees, ElevatorTrimKey),

        // ---- pressurization ----
        ["FO_CABIN_ALT_AT_CRUISE"] = CabinAltitudeAt(plan => CruiseCabinAltitudeFor(plan.CruiseFt)),
        ["FO_CABIN_ALT_AT_DEST"] = CabinAltitudeAt(plan => plan.DestElevFt),
        // The cabin baro at the captain's altimeter; a standard altimeter is no target, so NaN (D2).
        ["FO_CABIN_BARO_SET"] = new(new[] { CabinBaroKey, CaptainAltimeterKey }, (r, _) =>
        {
            double cabin = r(CabinBaroKey);
            return !double.IsNaN(cabin) && OffStandard(r(CaptainAltimeterKey)) is double setting
                ? B(Math.Abs(cabin - setting) < CabinBaroToleranceHpa)
                : double.NaN;
        }),

        // ---- gear: the lever and every leg (FO-13) ----
        [L1011GearConfirmation.UpField] = new(L1011GearConfirmation.Inputs.ToArray(), (r, _) => L1011GearConfirmation.UpValue(r)),
        [L1011GearConfirmation.DownField] = new(L1011GearConfirmation.Inputs.ToArray(), (r, _) => L1011GearConfirmation.DownValue(r)),
    };

    internal static IReadOnlyCollection<string> SyntheticKeys { get; } = Synthetics.Keys.ToArray();

    internal static IReadOnlyCollection<string> SyntheticInputs { get; } =
        Synthetics.Values.SelectMany(s => s.Inputs).Distinct().ToArray();

    /// <summary>The inputs one synthetic field reads (none for an unknown field).</summary>
    internal static IReadOnlyList<string> SyntheticInputsOf(string field)
        => Synthetics.TryGetValue(field, out var s) ? s.Inputs : Array.Empty<string>();

    /// <summary>Pure computation of one synthetic field (tests drive it with a dictionary); with no
    /// <paramref name="plan"/>, a field that compares against the plan reads NaN.</summary>
    internal static double Compute(string field, Func<string, double> read, Plan? plan = null)
        => Synthetics.TryGetValue(field, out var s) ? s.Compute(read, plan ?? Plan.None) : double.NaN;

    private double Raw(string key) => Sc?.GetCachedVariableValue(key) ?? double.NaN;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        if (Synthetics.ContainsKey(field))
        {
            value = Compute(field, Raw, CurrentPlan);
            return true;
        }
        value = double.NaN;
        return false;
    }
}
