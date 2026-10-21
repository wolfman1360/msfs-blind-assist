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
/// The engine reads are the live findings of 2026-10-21. A cold engine's N3 gauge reads 1.4 to 1.9,
/// never 0, and idles near 59, so <see cref="EngineStoppedN3"/> and <see cref="EngineRunningN3"/> sit
/// well clear of both. The fuel-and-ignition truth is the <c>TOGGLE_CUTOFF_ENG_n</c> row, which reads the
/// stock fuel valve; the L:var of that name reads 1 on a cold aircraft and is never read. Engine 3's N2
/// arrives through <see cref="IFoEngine3N2Sink"/> and is stored, but no TriStar field uses it.
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

    /// <summary>The speed brake handle below this is forward (stowed).</summary>
    public const double SpeedBrakeForwardBelow = 5;

    private const string FlapsAngleKey = "L1011_RO_FLAPS_ANGLE";
    private const string ExtPowerBreakerKey = "L1011_RO_EXT_PWR_BREAKER";
    private const string ExtPowerAvailableKey = "LAMP_EXTERNAL_POWER_AVAILABLE";

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
    };

    public override IReadOnlyList<string> OnRequestPollFields => PollFields;

    // ------------------------------------------------------------------ plan values

    /// <summary>The take-off stabilizer trim the tablet exported, in its own units (live: 2.38): null
    /// while unread, and while it reads 0 or below (0 before any export).</summary>
    public double? TakeoffStabTrimUnits() =>
        Raw(IniL1011Definition.PerfStabTrimKey) is var units && units > 0 ? units : null;

    // ------------------------------------------------------------------ synthetic fields

    private delegate double Fn(Func<string, double> read);
    private sealed record Synth(string[] Inputs, Fn Compute);

    private static double B(bool b) => b ? 1 : 0;
    private static bool Known(IEnumerable<double> v) => v.All(x => !double.IsNaN(x));

    /// <summary>A switch or selector reads this position (positions are whole numbers).</summary>
    private static bool At(double v, double position) => Math.Abs(v - position) < 0.5;

    /// <summary>A field computed from switch, lever, readout or lamp values: NaN unless every one was read.</summary>
    private static Synth Vars(Func<double[], bool> ok, params string[] keys)
        => new(keys, r =>
        {
            var v = keys.Select(r).ToArray();
            return Known(v) ? B(ok(v)) : double.NaN;
        });

    /// <summary>A field true when every key reads <paramref name="position"/>.</summary>
    private static Synth AllAt(double position, params string[] keys)
        => Vars(v => v.All(x => At(x, position)), keys);

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

    /// <summary>The jettison master and the four dump switches, then their five guards (0 closed).</summary>
    private static readonly string[] JettisonKeys =
    {
        "SWITCH_FUEL_DUMP_MASTER", "SWITCH_FUEL_DUMP_2L", "SWITCH_FUEL_DUMP_1", "SWITCH_FUEL_DUMP_3", "SWITCH_FUEL_DUMP_2R",
        "GUARD_FUEL_DUMP_MASTER", "GUARD_FUEL_DUMP_2L", "GUARD_FUEL_DUMP_1", "GUARD_FUEL_DUMP_3", "GUARD_FUEL_DUMP_2R",
    };

    private static readonly string[] InsModeKeys = Numbered("ROTARY_INS_MODE_{0}", 3);

    /// <summary>The ALIGN and NAV positions of an INS mode selector.</summary>
    private const double InsAlign = 2;
    private const double InsNav = 3;

    private static readonly L1011Lamp ExternalPowerLamp =
        L1011Annunciators.All.Single(l => L1011Annunciators.KeyFor(l) == ExtPowerAvailableKey);

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
    };

    internal static IReadOnlyCollection<string> SyntheticKeys { get; } = Synthetics.Keys.ToArray();

    internal static IReadOnlyCollection<string> SyntheticInputs { get; } =
        Synthetics.Values.SelectMany(s => s.Inputs).Distinct().ToArray();

    /// <summary>Pure computation of one synthetic field (tests drive it with a dictionary).</summary>
    internal static double Compute(string field, Func<string, double> read)
        => Synthetics.TryGetValue(field, out var s) ? s.Compute(read) : double.NaN;

    private double Raw(string key) => Sc?.GetCachedVariableValue(key) ?? double.NaN;

    protected override bool TryGetSyntheticValue(string field, out double value)
    {
        if (Synthetics.ContainsKey(field))
        {
            value = Compute(field, Raw);
            return true;
        }
        value = double.NaN;
        return false;
    }
}
