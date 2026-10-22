using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.Generic;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's state evaluator: every synthetic field is NaN until all of its inputs are
/// read (never 0 for unread), and each field's true and false case through the pure <c>Compute</c>.
/// What the live aircraft shows pins the engine reads: a cold engine's N3 gauge reads 1.4 to 1.9
/// (never 0) and idles near 59, and the fuel-and-ignition truth is the stock fuel valve behind the
/// <c>TOGGLE_CUTOFF_ENG_n</c> row, never the L:var of that name (it reads 1 on a cold aircraft).
/// </summary>
public class L1011FoStateEvaluatorTests
{
    private static Func<string, double> Read(Dictionary<string, double> d)
        => k => d.TryGetValue(k, out var v) ? v : double.NaN;

    private static double Compute(string field, Dictionary<string, double> d)
        => L1011FoStateEvaluator.Compute(field, Read(d));

    private static string[] FuelPumps { get; } =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    private static string[] DumpSwitches { get; } =
    {
        "SWITCH_FUEL_DUMP_MASTER", "SWITCH_FUEL_DUMP_2L", "SWITCH_FUEL_DUMP_1", "SWITCH_FUEL_DUMP_3", "SWITCH_FUEL_DUMP_2R",
    };

    private static string[] DumpGuards { get; } =
    {
        "GUARD_FUEL_DUMP_MASTER", "GUARD_FUEL_DUMP_2L", "GUARD_FUEL_DUMP_1", "GUARD_FUEL_DUMP_3", "GUARD_FUEL_DUMP_2R",
    };

    /// <summary>Every key at one value.</summary>
    private static Dictionary<string, double> All(double value, params string[] keys)
        => keys.ToDictionary(k => k, _ => value);

    /// <summary>
    /// The shared shape of an all-keys-agree field: <paramref name="good"/> reads true, any one key at
    /// <paramref name="bad"/> reads false, and any one key unread reads unknown, for every key.
    /// </summary>
    private static void AssertAllOf(string field, double good, double bad, params string[] keys)
    {
        Assert.Equal(1, Compute(field, All(good, keys)));
        foreach (var key in keys)
        {
            var wrong = All(good, keys);
            wrong[key] = bad;
            Assert.True(Compute(field, wrong) == 0, $"{field}: {key} at {bad} must read false");

            var unread = All(good, keys);
            unread.Remove(key);
            Assert.True(double.IsNaN(Compute(field, unread)), $"{field}: {key} unread must read unknown");
        }
        Assert.True(double.IsNaN(Compute(field, new Dictionary<string, double>())), $"{field}: nothing read must be unknown");
    }

    // ------------------------------------------------------------------ engines

    private static Dictionary<string, double> Engines(double n3, double fuelSwitch = 0) => new()
    {
        ["L1011_RO_N3_1"] = n3, ["L1011_RO_N3_2"] = n3, ["L1011_RO_N3_3"] = n3,
        ["TOGGLE_CUTOFF_ENG_1"] = fuelSwitch, ["TOGGLE_CUTOFF_ENG_2"] = fuelSwitch, ["TOGGLE_CUTOFF_ENG_3"] = fuelSwitch,
    };

    [Fact]
    public void EnginesStopped_ReadsTrueWhenEveryN3IsLowAndEveryFuelSwitchIsOff()
        => Assert.Equal(1, Compute("FO_ENGINES_STOPPED", Engines(n3: 0.5)));

    /// <summary>On the live aircraft a cold TriStar's N3 gauge reads 1.4 to 1.9, never 0; that is stopped.</summary>
    [Theory]
    [InlineData(1.4)]
    [InlineData(1.9)]
    [InlineData(4.99)]
    public void EnginesStopped_CountsAColdN3ReadingAsStopped(double coldN3)
        => Assert.Equal(1, Compute("FO_ENGINES_STOPPED", Engines(coldN3)));

    [Fact]
    public void EnginesStopped_ThresholdsArePinned()
    {
        Assert.Equal(5, L1011FoStateEvaluator.EngineStoppedN3);
        Assert.Equal(50, L1011FoStateEvaluator.EngineRunningN3);
    }

    [Theory]
    [InlineData(5.0)]
    [InlineData(20.0)]
    [InlineData(59.0)]
    public void EnginesStopped_IsFalseWhileAnyEngineTurns(double n3)
    {
        for (int e = 1; e <= 3; e++)
        {
            var d = Engines(n3: 1.9);
            d[$"L1011_RO_N3_{e}"] = n3;
            Assert.Equal(0, Compute("FO_ENGINES_STOPPED", d));
        }
    }

    [Fact]
    public void EnginesStopped_IsFalseWithOneFuelSwitchOn()
    {
        for (int e = 1; e <= 3; e++)
        {
            var d = Engines(n3: 1.9);
            d[$"TOGGLE_CUTOFF_ENG_{e}"] = 1;
            Assert.Equal(0, Compute("FO_ENGINES_STOPPED", d));
        }
    }

    [Fact]
    public void EnginesStopped_IsUnknownWithAnyInputUnread()
    {
        foreach (var key in Engines(1.9).Keys)
        {
            var d = Engines(n3: 1.9);
            d.Remove(key);
            Assert.True(double.IsNaN(Compute("FO_ENGINES_STOPPED", d)), $"{key} unread must read unknown");
        }
        Assert.True(double.IsNaN(Compute("FO_ENGINES_STOPPED", new Dictionary<string, double>())));
    }

    [Fact]
    public void EngineRunning_IsTheEnginesOwnN3AtOrAboveFifty()
    {
        for (int e = 1; e <= 3; e++)
        {
            string field = $"FO_ENG{e}_RUNNING";
            string n3 = $"L1011_RO_N3_{e}";
            Assert.Equal(1, Compute(field, new() { [n3] = 59.0 }));
            Assert.Equal(1, Compute(field, new() { [n3] = 50.0 }));
            Assert.Equal(0, Compute(field, new() { [n3] = 49.9 }));
            Assert.Equal(0, Compute(field, new() { [n3] = 1.9 }));
            Assert.True(double.IsNaN(Compute(field, new Dictionary<string, double>())));
            // Another engine's N3 never answers for this one.
            var others = Enumerable.Range(1, 3).Where(o => o != e).ToDictionary(o => $"L1011_RO_N3_{o}", _ => 59.0);
            Assert.True(double.IsNaN(Compute(field, others)));
        }
    }

    /// <summary>
    /// A ground start released after a start: the engine's own switch off AND its N3 running. A cold aircraft has the
    /// switch off too, so the switch alone would read released before any start; a switch still on reads not
    /// released; unknown until both are read.
    /// </summary>
    [Fact]
    public void StartReleased_IsTheSwitchOffWithTheEngineRunning()
    {
        for (int e = 1; e <= 3; e++)
        {
            string field = $"FO_ENG{e}_START_RELEASED";
            string sw = $"SWITCH_GND_START_ENG_{e}";
            string n3 = $"L1011_RO_N3_{e}";
            Assert.Equal(1, Compute(field, new() { [sw] = 0, [n3] = 59.2 }));
            Assert.Equal(1, Compute(field, new() { [sw] = 0, [n3] = 50.0 }));
            Assert.Equal(0, Compute(field, new() { [sw] = 1, [n3] = 59.2 }));
            Assert.Equal(0, Compute(field, new() { [sw] = 1, [n3] = 30.0 }));
            Assert.Equal(0, Compute(field, new() { [sw] = 0, [n3] = 49.9 }));
            Assert.Equal(0, Compute(field, new() { [sw] = 0, [n3] = 1.6 }));
            Assert.True(double.IsNaN(Compute(field, new() { [n3] = 59.2 })), $"{field}: the switch unread");
            Assert.True(double.IsNaN(Compute(field, new() { [sw] = 0 })), $"{field}: N3 unread");
            Assert.True(double.IsNaN(Compute(field, new Dictionary<string, double>())));
            // Another engine's readings never answer for this one.
            var others = Enumerable.Range(1, 3).Where(o => o != e)
                .SelectMany(o => new[] { ($"SWITCH_GND_START_ENG_{o}", 0.0), ($"L1011_RO_N3_{o}", 59.2) })
                .ToDictionary(x => x.Item1, x => x.Item2);
            Assert.True(double.IsNaN(Compute(field, others)));
        }
    }

    [Fact]
    public void FuelIgnitionOff_NeedsAllThreeSwitchesReadAndOff()
    {
        string[] keys = { "TOGGLE_CUTOFF_ENG_1", "TOGGLE_CUTOFF_ENG_2", "TOGGLE_CUTOFF_ENG_3" };
        AssertAllOf("FO_FUEL_IGNITION_OFF", good: 0, bad: 1, keys);
    }

    // ------------------------------------------------------------------ levers

    [Fact]
    public void FireHandlesIn_NeedsThreeEngineHandlesAndTheApuHandleReadAndIn()
        => AssertAllOf("FO_FIRE_HANDLES_IN", good: 0, bad: 1,
            "HANDLE_FIRE_1", "HANDLE_FIRE_2", "HANDLE_FIRE_3", "HANDLE_FIRE_APU");

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(24.9, 1)]
    [InlineData(25.0, 0)]
    [InlineData(50.0, 0)]
    [InlineData(100.0, 0)]
    public void GearLeverDown_IsBelowTwentyFive(double lever, double want)
        => Assert.Equal(want, Compute("FO_GEAR_LEVER_DOWN", new() { ["L1011_GEAR_LEVER"] = lever }));

    [Theory]
    [InlineData(0.0, 1)]
    [InlineData(4.9, 1)]
    [InlineData(5.0, 0)]
    [InlineData(60.0, 0)]
    [InlineData(100.0, 0)]
    public void SpeedBrakeForward_IsBelowFive(double handle, double want)
        => Assert.Equal(want, Compute("FO_SPEED_BRAKE_FORWARD", new() { ["L1011_SPEED_BRAKE"] = handle }));

    [Fact]
    public void LeverFields_AreUnknownUnread()
    {
        var none = new Dictionary<string, double>();
        Assert.True(double.IsNaN(Compute("FO_GEAR_LEVER_DOWN", none)));
        Assert.True(double.IsNaN(Compute("FO_SPEED_BRAKE_FORWARD", none)));
    }

    /// <summary>The handle's index maps to the detents 0, 4, 10, 14, 18, 22, 33 degrees.</summary>
    [Theory]
    [InlineData(0, 0.0, 1)]
    [InlineData(0, 0.9, 1)]
    [InlineData(0, 1.2, 0)]
    [InlineData(1, 4.0, 1)]
    [InlineData(1, 4.9, 1)]
    [InlineData(1, 5.1, 0)]
    [InlineData(1, 2.9, 0)]
    [InlineData(2, 10.0, 1)]
    [InlineData(3, 14.0, 1)]
    [InlineData(4, 18.0, 1)]
    [InlineData(5, 22.0, 1)]
    [InlineData(6, 33.0, 1)]
    [InlineData(6, 22.0, 0)]
    [InlineData(2, 4.0, 0)]
    [InlineData(7, 33.0, 0)]
    [InlineData(-1, 0.0, 0)]
    public void FlapsAgree_ComparesTheHandleDetentToTheFlapAngle(double handle, double angle, double want)
        => Assert.Equal(want, Compute("FO_FLAPS_AGREE",
            new() { ["L1011_FLAP_HANDLE"] = handle, ["L1011_RO_FLAPS_ANGLE"] = angle }));

    [Fact]
    public void FlapsAgree_IsUnknownWithEitherInputUnread()
    {
        Assert.True(double.IsNaN(Compute("FO_FLAPS_AGREE", new() { ["L1011_FLAP_HANDLE"] = 0 })));
        Assert.True(double.IsNaN(Compute("FO_FLAPS_AGREE", new() { ["L1011_RO_FLAPS_ANGLE"] = 0 })));
        Assert.True(double.IsNaN(Compute("FO_FLAPS_AGREE", new Dictionary<string, double>())));
    }

    // ------------------------------------------------------------------ switch groups

    [Fact]
    public void RadioMastersOn_NeedsBothMastersReadAndOn()
        => AssertAllOf("FO_RADIO_MASTERS_ON", good: 1, bad: 0, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2");

    [Fact]
    public void AtmsOff_NeedsBothSelectorsReadAndAtOff()
        => AssertAllOf("FO_ATMS_OFF", good: 0, bad: 1, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE");

    [Fact]
    public void AtmsOff_IsFalseAtEveryOtherSelectorPosition()
    {
        foreach (double position in new[] { 1.0, 2.0, 3.0 })
            Assert.Equal(0, Compute("FO_ATMS_OFF", new() { ["ROTARY_HYD_B2_MODE"] = 0, ["ROTARY_HYD_C2_MODE"] = position }));
    }

    [Fact]
    public void AcPumpsOff_NeedsBothPumpsReadAndOff()
        => AssertAllOf("FO_AC_PUMPS_OFF", good: 0, bad: 1, "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3");

    [Fact]
    public void FuelPumpsOff_NeedsAllEightPumpsReadAndOff()
        => AssertAllOf("FO_FUEL_PUMPS_OFF", good: 0, bad: 1, FuelPumps);

    /// <summary>One of the eight tank pumps on is enough; every pump must be read, so one unread is unknown even with
    /// another reading on.</summary>
    [Theory]
    [InlineData("SWITCH_FUEL_PUMP_2L_1", null, 1.0)]                    // one pump on, the rest off
    [InlineData(null, null, 0.0)]                                       // all eight off
    [InlineData("SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_3_2", double.NaN)] // one on, another unread
    public void AnyFuelPumpOn_IsOnePumpOn_AndUnknownWithAnyPumpUnread(string? on, string? unread, double want)
    {
        var d = All(0, FuelPumps);
        if (on is not null) d[on] = 1;
        if (unread is not null) d.Remove(unread);
        double got = Compute("FO_ANY_FUEL_PUMP_ON", d);
        if (double.IsNaN(want))
            Assert.True(double.IsNaN(got), $"{unread} unread must read unknown, read {got}");
        else
            Assert.Equal(want, got);
    }

    [Fact]
    public void JettisonSafe_NeedsTheMasterAndFourDumpSwitchesOffAndTheirFiveGuardsClosed()
    {
        var keys = DumpSwitches.Concat(DumpGuards).ToArray();
        Assert.Equal(10, keys.Length);
        AssertAllOf("FO_JETTISON_SAFE", good: 0, bad: 1, keys);
    }

    [Fact]
    public void PaxOxySafe_NeedsTheSwitchOffAndTheGuardClosed()
        => AssertAllOf("FO_PAX_OXY_SAFE", good: 0, bad: 1, "SWITCH_PAX_OXY_MANUAL", "GUARD_PAX_OXY_MANUAL");

    [Fact]
    public void GalleyOff_NeedsAllThreeGalleyBusesReadAndOff()
        => AssertAllOf("FO_GALLEY_OFF", good: 0, bad: 1, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3");

    [Theory]
    [InlineData(1.0, 0.0, 1)]    // Arm, guard closed
    [InlineData(0.0, 0.0, 0)]    // Off
    [InlineData(2.0, 0.0, 0)]    // On
    [InlineData(1.0, 1.0, 0)]    // Arm, guard lifted
    public void StbyArmed_IsArmWithTheGuardClosed(double standby, double guard, double want)
        => Assert.Equal(want, Compute("FO_STBY_ARMED",
            new() { ["TOGGLE_STBY_POWER"] = standby, ["GUARD_STBY_POWER"] = guard }));

    [Fact]
    public void StbyArmed_IsUnknownWithEitherInputUnread()
    {
        Assert.True(double.IsNaN(Compute("FO_STBY_ARMED", new() { ["TOGGLE_STBY_POWER"] = 1 })));
        Assert.True(double.IsNaN(Compute("FO_STBY_ARMED", new() { ["GUARD_STBY_POWER"] = 0 })));
    }

    // ------------------------------------------------------------------ external power and INS

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(0.0, 0)]
    public void ExtPowerOn_IsTheBreakerClosed(double breaker, double want)
        => Assert.Equal(want, Compute("FO_EXT_POWER_ON", new() { ["L1011_RO_EXT_PWR_BREAKER"] = breaker }));

    [Fact]
    public void ExtPowerOn_IsUnknownUnread()
        => Assert.True(double.IsNaN(Compute("FO_EXT_POWER_ON", new Dictionary<string, double>())));

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(0.0, 0)]
    public void ExtPowerAvailable_IsTheAvailableLampLit(double lamp, double want)
        => Assert.Equal(want, Compute("FO_EXT_POWER_AVAILABLE", new() { ["LAMP_EXTERNAL_POWER_AVAILABLE"] = lamp }));

    [Fact]
    public void ExtPowerAvailable_IsUnknownUnread()
        => Assert.True(double.IsNaN(Compute("FO_EXT_POWER_AVAILABLE", new Dictionary<string, double>())));

    private static Dictionary<string, double> Ins(double one, double two, double three) => new()
    {
        ["ROTARY_INS_MODE_1"] = one, ["ROTARY_INS_MODE_2"] = two, ["ROTARY_INS_MODE_3"] = three,
    };

    [Theory]
    [InlineData(2.0, 2.0, 2.0, 1)]   // all align
    [InlineData(3.0, 3.0, 3.0, 1)]   // all nav
    [InlineData(2.0, 3.0, 2.0, 1)]   // a mix of the two
    [InlineData(0.0, 2.0, 2.0, 0)]   // one off
    [InlineData(2.0, 1.0, 2.0, 0)]   // one standby
    [InlineData(2.0, 2.0, 4.0, 0)]   // one attitude
    public void InsAlignOrNav_NeedsEachUnitAtAlignOrNav(double one, double two, double three, double want)
        => Assert.Equal(want, Compute("FO_INS_ALIGN_OR_NAV", Ins(one, two, three)));

    [Fact]
    public void InsAlignOrNav_IsUnknownWithAnyUnitUnread()
    {
        foreach (var key in Ins(2, 2, 2).Keys)
        {
            var d = Ins(2, 2, 2);
            d.Remove(key);
            Assert.True(double.IsNaN(Compute("FO_INS_ALIGN_OR_NAV", d)), $"{key} unread must read unknown");
        }
    }

    // ------------------------------------------------------------------ the evaluator itself

    [Fact]
    public void EveryField_IsPinned()
        => Assert.Equal(new[]
        {
            "FO_AC_PUMPS_OFF", "FO_AC_TIES_CLOSED", "FO_AIR_DATA_HEAT_OFF", "FO_ANTI_ICE_OFF", "FO_ANY_FUEL_PUMP_ON",
            "FO_APU_AIR_ON", "FO_APU_GEN_CLOSED", "FO_APU_GEN_FIELD_CLOSED", "FO_APU_GEN_OFF_LINE",
            "FO_APU_GEN_ON_LINE", "FO_APU_OFF", "FO_APU_RUNNING", "FO_APU_STARTING_OR_RUNNING",
            "FO_ATMS_AC_PUMPS_OFF", "FO_ATMS_AUTO", "FO_ATMS_OFF", "FO_BUS_FAIL_LIGHTS_OUT",
            "FO_CABIN_ALT_AT_CRUISE", "FO_CABIN_ALT_AT_DEST", "FO_CABIN_BARO_SET", "FO_COMPASSES_MAG",
            "FO_CROSSBLEEDS_ON", "FO_CROSSFEEDS_CLOSED", "FO_DISCONNECT_HANDLES_STOWED", "FO_DME_NORMAL",
            "FO_DOORS_CLOSED", "FO_ECS_ON", "FO_EMER_LIGHTS_ARMED", "FO_EMER_LIGHTS_OFF",
            "FO_ENG1_FUEL_FEED", "FO_ENG1_RUNNING", "FO_ENG1_START_RELEASED", "FO_ENG2_FUEL_FEED", "FO_ENG2_RUNNING",
            "FO_ENG2_START_RELEASED", "FO_ENG3_FUEL_FEED", "FO_ENG3_RUNNING", "FO_ENG3_START_RELEASED", "FO_ENGINES_STOPPED", "FO_ENGINE_GENERATORS_ON", "FO_EXT_LIGHTS_PREFLIGHT",
            "FO_EXT_POWER_AVAILABLE", "FO_EXT_POWER_OFF", "FO_EXT_POWER_ON", "FO_FCES_ON", "FO_FIRE_HANDLES_IN",
            "FO_FIRE_LOOPS_BOTH", "FO_FLAPS_AGREE", "FO_FLAPS_TAKEOFF", "FO_FLAPS_UP", "FO_FLIGHT_DIRECTORS_ON",
            "FO_FUEL_CTRL_OVRD_OFF", "FO_FUEL_IGNITION_OFF", "FO_FUEL_PUMPS_OFF", "FO_FUEL_PUMPS_ON",
            "FO_GALLEY_OFF", "FO_GALLEY_ON", "FO_GEAR_DOWN_CONFIRMED", "FO_GEAR_LEVER_DOWN", "FO_GEAR_LEVER_UP",
            "FO_GEAR_UP_CONFIRMED", "FO_GEN_BREAKERS_CLOSED", "FO_GEN_FIELDS_CLOSED", "FO_GROUND_LIGHTS_OFF",
            "FO_GROUND_LIGHTS_ON", "FO_HI_PRESS_ON", "FO_HOT_AIR_OFF", "FO_HOT_AIR_ON", "FO_HYD_ENGINE_PUMPS_ON",
            "FO_IDG_LIGHTS_OUT", "FO_IGNITION_BOTH", "FO_IGNITION_SELECTED", "FO_INSTR_SOURCES_NORMAL",
            "FO_INS_ALIGN_OR_NAV", "FO_INS_NAVIGATING", "FO_INS_NAV_MODE", "FO_INS_OFF", "FO_ISLN_VALVES_ON",
            "FO_JETTISON_SAFE", "FO_LANDING_LIGHTS_OFF", "FO_LANDING_LIGHTS_ON", "FO_MACH_FEEL_RUDDER_ON",
            "FO_MOVIE_OFF", "FO_ONLY_APU_PUMP_ON", "FO_PACKS_AUTO", "FO_PACKS_OFF", "FO_PACKS_ON", "FO_PACK_2_ONLY",
            "FO_PADDLES_OFF", "FO_PAX_OXY_SAFE", "FO_PFCS_ON", "FO_PITOT_ALPHA_HEAT_ON", "FO_POWER_SOURCE_ON",
            "FO_PTUS_OFF", "FO_RADIO_MASTERS_OFF", "FO_RADIO_MASTERS_ON", "FO_SIDE_WINDOWS_OFF",
            "FO_SIDE_WINDOWS_ON", "FO_SPEED_BRAKE_FORWARD", "FO_STAB_TRIM_ZERO", "FO_START_AIR_VALVES_ON",
            "FO_START_LIGHTS_OUT", "FO_STBY_ARMED", "FO_TANKS_1A_3A_FUELED", "FO_TANK_VALVES_ON", "FO_TRANSFERS_OFF",
            "FO_TRANSFERS_ON", "FO_WINDSHIELD_IDLE", "FO_WINDSHIELD_NORMAL", "FO_WIPERS_OFF", "FO_ZONE_TRIMS_ON",
        }, L1011FoStateEvaluator.SyntheticKeys.OrderBy(k => k, StringComparer.Ordinal).ToArray());

    [Fact]
    public void AnUnknownField_ComputesToNaN()
        => Assert.True(double.IsNaN(Compute("FO_NOT_A_FIELD", new() { ["L1011_RO_N3_1"] = 59 })));

    /// <summary>Not connected, nothing is read: every field is unknown, never a guessed 0.</summary>
    [Fact]
    public void WithoutAConnection_EveryFieldIsNaN()
    {
        var e = new L1011FoStateEvaluator();
        foreach (var field in L1011FoStateEvaluator.SyntheticKeys)
            Assert.True(double.IsNaN(e.GetValue(field)), $"{field} must be unknown without a connection");
    }

    [Fact]
    public void Engine3N2_IsStoredButNoTriStarFieldReadsIt()
    {
        var e = new L1011FoStateEvaluator();
        Assert.IsAssignableFrom<IFoEngine3N2Sink>(e);
        Assert.True(double.IsNaN(e.Engine3N2));
        e.SetEngine3N2(22);
        Assert.Equal(22, e.Engine3N2);
        Assert.DoesNotContain(L1011FoStateEvaluator.SyntheticInputs, k => k.Contains("N2", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the definition agrees

    /// <summary>Every input is a variable the definition registers and the sim delivers.</summary>
    [Fact]
    public void Every_synthetic_input_is_a_registered_variable()
    {
        var vars = new IniL1011Definition().GetVariables();
        foreach (var k in L1011FoStateEvaluator.SyntheticInputs)
        {
            Assert.True(vars.TryGetValue(k, out var def), $"synthetic input {k} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
        }
    }

    /// <summary>
    /// The engines' fuel-and-ignition truth is the stock fuel valve behind the row, never the L:var of
    /// the same name (the L:var reads 1 on a cold aircraft).
    /// </summary>
    [Fact]
    public void The_fuel_and_ignition_input_is_the_stock_fuel_valve()
    {
        var vars = new IniL1011Definition().GetVariables();
        for (int e = 1; e <= 3; e++)
        {
            var def = vars[$"TOGGLE_CUTOFF_ENG_{e}"];
            Assert.Equal(SimVarType.SimVar, def.Type);
            Assert.StartsWith("FUELSYSTEM VALVE SWITCH", def.Name, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Every_on_request_input_is_polled()
    {
        var vars = new IniL1011Definition().GetVariables();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        foreach (var k in L1011FoStateEvaluator.SyntheticInputs)
            if (vars.TryGetValue(k, out var def) && def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.Contains(k, polled);
    }

    [Fact]
    public void Every_poll_field_is_registered_on_request()
    {
        var vars = new IniL1011Definition().GetVariables();
        foreach (var k in new L1011FoStateEvaluator().OnRequestPollFields)
        {
            Assert.True(vars.TryGetValue(k, out var def), $"poll field {k} is not registered");
            Assert.Equal(UpdateFrequency.OnRequest, def!.UpdateFrequency);
        }
    }

    /// <summary>The readouts the window polls each second: the three N3 gauges, the flap angle, the
    /// external power breaker and the three AC tie breakers (the executor's closed-loop reads).</summary>
    [Fact]
    public void The_poll_list_covers_the_readouts_the_flows_wait_on()
    {
        var polled = new L1011FoStateEvaluator().OnRequestPollFields;
        foreach (var k in new[]
        {
            "L1011_RO_N3_1", "L1011_RO_N3_2", "L1011_RO_N3_3", "L1011_RO_FLAPS_ANGLE", "L1011_RO_EXT_PWR_BREAKER",
            "L1011_RO_AC_TIE_1", "L1011_RO_AC_TIE_2", "L1011_RO_AC_TIE_3",
        })
            Assert.Contains(k, polled);
        Assert.Equal(polled.Count, polled.Distinct().Count());
    }

    // ------------------------------------------------------------------ the tablet's take-off exports

    /// <summary>The tablet's "Set bugs and EPR" exports, polled with the readouts: the selected take-off
    /// flaps and the take-off stabilizer trim.</summary>
    [Fact]
    public void The_poll_list_covers_the_tablets_take_off_exports()
    {
        var polled = new L1011FoStateEvaluator().OnRequestPollFields;

        Assert.Contains(IniL1011Definition.PerfSelectedFlapsKey, polled);
        Assert.Contains(IniL1011Definition.PerfStabTrimKey, polled);
        Assert.Equal(polled.Count, polled.Distinct().Count());
    }

    /// <summary>Live: the tablet's export read 2.38; it reads 0 before any export.</summary>
    [Theory]
    [InlineData(2.38, 2.38)]
    [InlineData(0.0, null)]
    [InlineData(-1.0, null)]
    [InlineData(double.NaN, null)]
    public void Takeoff_stab_trim_units_is_the_tablets_export_above_zero(double cached, double? want)
    {
        var e = new L1011FoStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With((IniL1011Definition.PerfStabTrimKey, cached)));

        Assert.Equal(want, e.TakeoffStabTrimUnits());
    }

    [Fact]
    public void Takeoff_stab_trim_units_is_null_unread()
    {
        var e = new L1011FoStateEvaluator();
        Assert.Null(e.TakeoffStabTrimUnits());   // no connection at all

        e.SetSimConnect(SeededSimConnectCache.With((IniL1011Definition.PerfSelectedFlapsKey, 10)));
        Assert.Null(e.TakeoffStabTrimUnits());   // connected, the trim never delivered
    }

    // ================================================================== the flows' fields

    /// <summary>A NaN want reads unknown; any other want is that value.</summary>
    private static void AssertReads(double want, double actual, string because)
    {
        if (double.IsNaN(want))
            Assert.True(double.IsNaN(actual), $"{because}: want unknown, read {actual}");
        else
            Assert.True(want == actual, $"{because}: want {want}, read {actual}");
    }

    /// <summary>The given readings, a NaN one left unread.</summary>
    private static Dictionary<string, double> Readings(params (string Key, double Value)[] values)
        => values.Where(v => !double.IsNaN(v.Value)).ToDictionary(v => v.Key, v => v.Value);

    private static L1011FoStateEvaluator Evaluator(params (string Key, double Value)[] cached)
    {
        var e = new L1011FoStateEvaluator();
        e.SetSimConnect(SeededSimConnectCache.With(cached.Where(c => !double.IsNaN(c.Value)).ToArray()));
        return e;
    }

    [Fact]
    public void Part_2_thresholds_are_pinned()
    {
        Assert.Equal(95, L1011FoStateEvaluator.ApuRunningRpm);
        Assert.Equal(1, L1011FoStateEvaluator.ApuStartingRpm);
        Assert.Equal(5, L1011FoStateEvaluator.ApuStoppedRpm);
        Assert.Equal(50, L1011FoStateEvaluator.TransferTankMinLbs);
        Assert.Equal(0.2, L1011FoStateEvaluator.StabTrimZeroDegrees);
        Assert.Equal(0.1, L1011FoStateEvaluator.InsNavigatingTolerance);
        Assert.Equal(75, L1011FoStateEvaluator.GearLeverUpAbove);
        Assert.Equal(150, L1011FoStateEvaluator.CabinAltitudeToleranceFt);
        Assert.Equal(0.5, L1011FoStateEvaluator.CabinBaroToleranceHpa);
        Assert.Equal(0.2, L1011FoStateEvaluator.StandardMillibarsTolerance);
        Assert.Equal(0.3048, L1011FoStateEvaluator.MetresPerFoot);
        Assert.Equal(3, L1011FoStateEvaluator.InsNav);
        Assert.Equal(2, L1011FoStateEvaluator.InsAligned);
    }

    // ------------------------------------------------------------------ fields where every input reads one value

    private static readonly string[] PfcsSwitches =
    {
        "SWITCH_PFCS_PITCH_1", "SWITCH_PFCS_PITCH_2", "SWITCH_PFCS_ROLL_1", "SWITCH_PFCS_ROLL_2", "SWITCH_PFCS_RUDDER",
        "SWITCH_STABILIZER_A", "SWITCH_STABILIZER_B", "SWITCH_STABILIZER_C", "SWITCH_STABILIZER_D",
        "SWITCH_AILERON_L_OUTBD", "SWITCH_AILERON_L_INBD", "SWITCH_AILERON_R_INBD", "SWITCH_AILERON_R_OUTBD",
        "SWITCH_SPOILER_L56", "SWITCH_SPOILER_LR4", "SWITCH_SPOILER_LR1", "SWITCH_SPOILER_LR2", "SWITCH_SPOILER_LR3",
        "SWITCH_SPOILER_R56",
    };

    private static readonly string[] FcesSwitches =
    {
        "SWITCH_ACS_1", "SWITCH_ACS_2", "SWITCH_YAW_SAS_1", "SWITCH_YAW_SAS_2", "SWITCH_STALL_WARN_1", "SWITCH_STALL_WARN_2",
        "SWITCH_PITCH_TRIM_1", "SWITCH_PITCH_TRIM_2", "SWITCH_DLC_AUTO_1", "SWITCH_DLC_AUTO_2", "SWITCH_ATS_1", "SWITCH_ATS_2",
        "SWITCH_MACH_TRIM_1", "SWITCH_MACH_TRIM_2",
    };

    private static readonly string[] InstrumentSources =
    {
        "SWITCH_INSTR_SRC_CPT_NAV", "SWITCH_INSTR_SRC_CPT_HDG", "SWITCH_INSTR_SRC_CPT_ATT", "SWITCH_INSTR_SRC_CPT_ALT",
        "SWITCH_INSTR_SRC_CPT_IAS", "SWITCH_INSTR_SRC_CPT_FLT_DIR",
        "SWITCH_INSTR_SRC_FO_NAV", "SWITCH_INSTR_SRC_FO_HDG", "SWITCH_INSTR_SRC_FO_ATT", "SWITCH_INSTR_SRC_FO_ALT",
        "SWITCH_INSTR_SRC_FO_IAS", "SWITCH_INSTR_SRC_FO_FLT_DIR",
    };

    private static readonly string[] AntiIce =
    {
        "SWITCH_ANTI_ICE_ENG_1", "SWITCH_ANTI_ICE_ENG_2", "SWITCH_ANTI_ICE_ENG_3",
        "SWITCH_ANTI_ICE_LEFT_AUTO", "SWITCH_ANTI_ICE_LEFT_MAN", "SWITCH_ANTI_ICE_RIGHT_AUTO", "SWITCH_ANTI_ICE_RIGHT_MAN",
    };

    private static object[] Row(string field, double good, double bad, params string[] keys)
        => new object[] { field, good, bad, keys };

    /// <summary>(field, the reading that makes it true, a reading that makes it false, its inputs).</summary>
    public static IEnumerable<object[]> AllAtFields() => new[]
    {
        // APU and electrical: relays read 1 closed, 0 open; a lamp field is true with every lamp out.
        Row("FO_APU_GEN_CLOSED", 1, 0, "L1011_RO_APU_GEN_FIELD", "L1011_RO_APU_GEN_BREAKER"),
        Row("FO_APU_GEN_OFF_LINE", 0, 1, "L1011_RO_APU_GEN_BREAKER"),
        Row("FO_GEN_FIELDS_CLOSED", 1, 0, "L1011_RO_GEN_1_FIELD", "L1011_RO_GEN_2_FIELD", "L1011_RO_GEN_3_FIELD"),
        Row("FO_GEN_BREAKERS_CLOSED", 1, 0, "L1011_RO_GEN_1_BREAKER", "L1011_RO_GEN_2_BREAKER", "L1011_RO_GEN_3_BREAKER"),
        Row("FO_ENGINE_GENERATORS_ON", 1, 0, "L1011_RO_GEN_1_FIELD", "L1011_RO_GEN_2_FIELD", "L1011_RO_GEN_3_FIELD",
            "L1011_RO_GEN_1_BREAKER", "L1011_RO_GEN_2_BREAKER", "L1011_RO_GEN_3_BREAKER"),
        Row("FO_AC_TIES_CLOSED", 1, 0, "L1011_RO_AC_TIE_1", "L1011_RO_AC_TIE_2", "L1011_RO_AC_TIE_3"),
        Row("FO_EXT_POWER_OFF", 0, 1, "L1011_RO_EXT_PWR_BREAKER"),
        // The IDG lights are lit BELOW 40 psi of IDG oil pressure: 60 is out, 20 is lit.
        Row("FO_IDG_LIGHTS_OUT", 60, 20, "LAMP_IDG_OIL_PRESSURE_1", "LAMP_IDG_OIL_PRESSURE_2", "LAMP_IDG_OIL_PRESSURE_3"),
        Row("FO_BUS_FAIL_LIGHTS_OUT", 0, 1, "LAMP_AC_ESS_FAIL_LIGHT", "LAMP_AC_STBY_FAIL_LIGHT",
            "LAMP_DC_ESS_FAIL_LIGHT", "LAMP_DC_STBY_FAIL_LIGHT"),
        Row("FO_RADIO_MASTERS_OFF", 0, 1, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2"),
        Row("FO_GALLEY_ON", 1, 0, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3"),
        Row("FO_EMER_LIGHTS_OFF", 0, 1, "TOGGLE_EMER_LIGHTING"),

        // Bleeds and air.
        Row("FO_CROSSBLEEDS_ON", 1, 0, "SWITCH_XBLEED_1", "SWITCH_XBLEED_3"),
        Row("FO_APU_AIR_ON", 1, 0, "SWITCH_APU_ISLN_VALVE", "SWITCH_XBLEED_1", "SWITCH_XBLEED_3"),
        Row("FO_ISLN_VALVES_ON", 1, 0, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3"),
        Row("FO_START_AIR_VALVES_ON", 1, 0, "SWITCH_ISLN_VALVE_ENG_1", "SWITCH_ISLN_VALVE_ENG_2", "SWITCH_ISLN_VALVE_ENG_3",
            "SWITCH_XBLEED_1", "SWITCH_XBLEED_3"),
        Row("FO_HI_PRESS_ON", 1, 0, "SWITCH_HI_PRESS_ENG_1", "SWITCH_HI_PRESS_ENG_2", "SWITCH_HI_PRESS_ENG_3"),
        Row("FO_PACKS_ON", 1, 0, "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3"),
        Row("FO_PACKS_OFF", 0, 1, "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3"),
        Row("FO_PACKS_AUTO", 1, 0, "SWITCH_BLEED_PLENUM_ENG_1", "SWITCH_BLEED_PLENUM_ENG_2", "SWITCH_BLEED_PLENUM_ENG_3"),
        Row("FO_HOT_AIR_OFF", 0, 1, "SWITCH_HOT_AIR_ENG_1", "SWITCH_HOT_AIR_ENG_3", "SWITCH_ISLN_VALVE_OVHT_2A", "SWITCH_ISLN_VALVE_OVHT_2B"),
        Row("FO_HOT_AIR_ON", 1, 0, "SWITCH_HOT_AIR_ENG_1", "SWITCH_HOT_AIR_ENG_3", "SWITCH_ISLN_VALVE_OVHT_2A", "SWITCH_ISLN_VALVE_OVHT_2B"),
        Row("FO_ZONE_TRIMS_ON", 1, 0, "SWITCH_FLT_STA_CLOSE", "SWITCH_FWD_CAB_CLOSE", "SWITCH_MID_CAB_CLOSE", "SWITCH_AFT_CAB_CLOSE"),
        Row("FO_ECS_ON", 1, 0, "SWITCH_COOL_AIR_OVBD", "SWITCH_CARGO_HEAT_FWD", "SWITCH_CARGO_HEAT_MID", "SWITCH_CARGO_HEAT_AFT"),
        Row("FO_FIRE_LOOPS_BOTH", 2, 1, "ROTARY_FIRE_DETECT_TEST_ENG_1", "ROTARY_FIRE_DETECT_TEST_ENG_2",
            "ROTARY_FIRE_DETECT_TEST_ENG_3", "ROTARY_FIRE_DETECT_TEST_APU"),

        // Hydraulics.
        Row("FO_PTUS_OFF", 0, 1, "SWITCH_PTU_AB", "SWITCH_PTU_CD"),
        Row("FO_ATMS_AUTO", 1, 0, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE"),
        Row("FO_ATMS_AC_PUMPS_OFF", 0, 1, "ROTARY_HYD_B2_MODE", "ROTARY_HYD_C2_MODE", "SWITCH_HYD_AC_PUMP_B3", "SWITCH_HYD_AC_PUMP_C3"),

        // Fuel: a crossfeed reads 0 closed (confirmed live).
        Row("FO_FUEL_PUMPS_ON", 1, 0, FuelPumps),
        Row("FO_CROSSFEEDS_CLOSED", 0, 1, "SWITCH_FUEL_LEFT_XFEED", "SWITCH_FUEL_CTR_XFEED", "SWITCH_FUEL_RIGHT_XFEED"),
        Row("FO_TRANSFERS_OFF", 0, 1, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"),
        Row("FO_TRANSFERS_ON", 1, 0, "SWITCH_FUEL_XFR_1A", "SWITCH_FUEL_XFR_3A"),
        Row("FO_FUEL_CTRL_OVRD_OFF", 0, 1, "SWITCH_FUEL_CTRL_OVRD_ENG_1", "SWITCH_FUEL_CTRL_OVRD_ENG_2", "SWITCH_FUEL_CTRL_OVRD_ENG_3"),

        // Engines, INS and doors.
        Row("FO_IGNITION_BOTH", 1, 0, "ROTARY_ENG_START_SEL"),
        Row("FO_START_LIGHTS_OUT", 0, 1, "LAMP_SWITCH_GND_START_ENG_1_LIGHT", "LAMP_SWITCH_GND_START_ENG_2_LIGHT",
            "LAMP_SWITCH_GND_START_ENG_3_LIGHT"),
        Row("FO_INS_NAV_MODE", 3, 2, "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3"),
        Row("FO_INS_OFF", 0, 1, "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3"),

        // Overhead.
        Row("FO_COMPASSES_MAG", 1, 0, "SWITCH_COMPASS_1_MAG", "SWITCH_COMPASS_2_MAG"),
        Row("FO_WIPERS_OFF", 0, 1, "ROTARY_CPT_WIPER", "ROTARY_FO_WIPER"),
        Row("FO_ANTI_ICE_OFF", 0, 1, AntiIce),
        Row("FO_WINDSHIELD_NORMAL", 1, 0, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO"),
        Row("FO_WINDSHIELD_IDLE", 0, 1, "SWITCH_ANTI_ICE_CPT", "SWITCH_ANTI_ICE_FO"),
        Row("FO_SIDE_WINDOWS_ON", 1, 0, "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR"),
        Row("FO_SIDE_WINDOWS_OFF", 0, 1, "SWITCH_ANTI_ICE_L_FWD", "SWITCH_ANTI_ICE_L_REAR", "SWITCH_ANTI_ICE_R_FWD", "SWITCH_ANTI_ICE_R_REAR"),
        Row("FO_AIR_DATA_HEAT_OFF", 0, 1, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_PROBE_HEAT", "SWITCH_ALPHA_HEAT"),
        Row("FO_PITOT_ALPHA_HEAT_ON", 1, 0, "SWITCH_CPT_PITOT_HEAT", "SWITCH_FO_PITOT_HEAT", "SWITCH_ALPHA_HEAT"),
        Row("FO_MACH_FEEL_RUDDER_ON", 1, 0, "SWITCH_MACH_FEEL_1", "SWITCH_MACH_FEEL_2", "SWITCH_RUDDER_LIMITER"),
        Row("FO_PFCS_ON", 1, 0, PfcsSwitches),
        Row("FO_FCES_ON", 1, 0, FcesSwitches),
        Row("FO_LANDING_LIGHTS_ON", 1, 0, "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT"),
        Row("FO_LANDING_LIGHTS_OFF", 0, 1, "TOGGLE_LANDING_LEFT", "TOGGLE_LANDING_RIGHT"),
        Row("FO_GROUND_LIGHTS_ON", 1, 0, "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF"),
        Row("FO_GROUND_LIGHTS_OFF", 0, 1, "TOGGLE_LANDING_NOSE", "TOGGLE_TAXI_LIGHT", "TOGGLE_RWY_TURNOFF"),
        Row("FO_MOVIE_OFF", 0, 1, "MOVIES_OFF", "GUARD_MOVIES_OFF"),

        // Main panel and console: an engage paddle reads 0 command, 1 CWS, 2 off.
        Row("FO_INSTR_SOURCES_NORMAL", 0, 1, InstrumentSources),
        Row("FO_DME_NORMAL", 1, 0, "ROTARY_NAV_1_MODE", "ROTARY_NAV_2_MODE"),
        Row("FO_FLIGHT_DIRECTORS_ON", 1, 0, "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B"),
        Row("FO_PADDLES_OFF", 2, 0, "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B"),
        Row("FO_DISCONNECT_HANDLES_STOWED", 0, 1, "LEVER_PITCH_DISC", "LEVER_ROLL_DISC"),
    };

    [Theory]
    [MemberData(nameof(AllAtFields))]
    public void A_part_2_field_needs_every_input_read_and_at_its_reading(string field, double good, double bad, string[] keys)
    {
        AssertAllOf(field, good, bad, keys);
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), InputsOf(field));
    }

    /// <summary>The inputs the evaluator declares for a field, in ordinal order.</summary>
    private static IEnumerable<string> InputsOf(string field)
        => L1011FoStateEvaluator.SyntheticInputsOf(field).OrderBy(k => k, StringComparer.Ordinal);

    // ------------------------------------------------------------------ fields where the inputs read different values

    private static object[] Mixed(string field, params (string Key, double Good, double Bad)[] inputs)
        => new object[] { field, inputs.Select(i => i.Key).ToArray(), inputs.Select(i => i.Good).ToArray(), inputs.Select(i => i.Bad).ToArray() };

    /// <summary>(field, its inputs, each input's reading that makes it true, each one's reading that makes it false).</summary>
    public static IEnumerable<object[]> MixedFields() => new[]
    {
        Mixed("FO_EMER_LIGHTS_ARMED", ("TOGGLE_EMER_LIGHTING", 1, 0), ("GUARD_EMER_LIGHTING", 0, 1)),
        Mixed("FO_PACK_2_ONLY", ("SWITCH_PACK_FLOW_ENG_1", 0, 1), ("SWITCH_PACK_FLOW_ENG_2", 1, 0), ("SWITCH_PACK_FLOW_ENG_3", 0, 1)),
        Mixed("FO_HYD_ENGINE_PUMPS_ON",
            ("SWITCH_HYD_PUMP_A1", 1, 0), ("SWITCH_HYD_PUMP_B1", 1, 0), ("SWITCH_HYD_PUMP_C1", 1, 0), ("SWITCH_HYD_PUMP_D1", 1, 0),
            ("GUARD_HYD_PUMP_A1", 0, 1), ("GUARD_HYD_PUMP_B1", 0, 1), ("GUARD_HYD_PUMP_C1", 0, 1), ("GUARD_HYD_PUMP_D1", 0, 1)),
        Mixed("FO_ONLY_APU_PUMP_ON",
            ("SWITCH_FUEL_PUMP_1_1", 0, 1), ("SWITCH_FUEL_PUMP_1_2", 0, 1), ("SWITCH_FUEL_PUMP_2L_1", 1, 0), ("SWITCH_FUEL_PUMP_2L_2", 0, 1),
            ("SWITCH_FUEL_PUMP_2R_1", 0, 1), ("SWITCH_FUEL_PUMP_2R_2", 0, 1), ("SWITCH_FUEL_PUMP_3_1", 0, 1), ("SWITCH_FUEL_PUMP_3_2", 0, 1)),
        Mixed("FO_TANK_VALVES_ON",
            ("SWITCH_ENG_1_TANK_VALVE", 1, 0), ("SWITCH_ENG_2_TANK_VALVE", 1, 0), ("SWITCH_ENG_3_TANK_VALVE", 1, 0),
            ("GUARD_ENG_1_TANK_VALVE", 0, 1), ("GUARD_ENG_2_TANK_VALVE", 0, 1), ("GUARD_ENG_3_TANK_VALVE", 0, 1)),
        Mixed("FO_ENG1_FUEL_FEED", ("SWITCH_FUEL_PUMP_1_1", 1, 0), ("SWITCH_ENG_1_TANK_VALVE", 1, 0), ("GUARD_ENG_1_TANK_VALVE", 0, 1)),
        Mixed("FO_ENG2_FUEL_FEED", ("SWITCH_FUEL_PUMP_2L_1", 1, 0), ("SWITCH_ENG_2_TANK_VALVE", 1, 0), ("GUARD_ENG_2_TANK_VALVE", 0, 1)),
        Mixed("FO_ENG3_FUEL_FEED", ("SWITCH_FUEL_PUMP_3_1", 1, 0), ("SWITCH_ENG_3_TANK_VALVE", 1, 0), ("GUARD_ENG_3_TANK_VALVE", 0, 1)),
        // The flood and wheel well lights read 1 off.
        Mixed("FO_EXT_LIGHTS_PREFLIGHT",
            ("TOGGLE_LOGO_LIGHT", 1, 0), ("SWITCH_POSITION_LIGHT", 1, 0),
            ("TOGGLE_LANDING_LEFT", 0, 1), ("TOGGLE_LANDING_RIGHT", 0, 1), ("TOGGLE_LANDING_NOSE", 0, 1), ("TOGGLE_RWY_TURNOFF", 0, 1),
            ("SWITCH_STROBE_LIGHT", 0, 1), ("SWITCH_ANTICOL_LIGHT", 0, 1),
            ("SWITCH_FLOOD_LIGHT", 1, 0), ("SWITCH_WHEEL_LIGHT", 1, 0)),
    };

    [Theory]
    [MemberData(nameof(MixedFields))]
    public void A_part_2_field_needs_each_input_read_and_at_its_own_reading(string field, string[] keys, double[] good, double[] bad)
    {
        Dictionary<string, double> Good() => keys.Select((k, i) => (k, good[i])).ToDictionary(p => p.k, p => p.Item2);

        Assert.Equal(1, Compute(field, Good()));
        for (int i = 0; i < keys.Length; i++)
        {
            var wrong = Good();
            wrong[keys[i]] = bad[i];
            Assert.True(Compute(field, wrong) == 0, $"{field}: {keys[i]} at {bad[i]} must read false");

            var unread = Good();
            unread.Remove(keys[i]);
            Assert.True(double.IsNaN(Compute(field, unread)), $"{field}: {keys[i]} unread must read unknown");
        }
        Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), InputsOf(field));
    }

    // ------------------------------------------------------------------ one input against a threshold

    [Theory]
    [InlineData("FO_APU_RUNNING", "L1011_RO_APU_RPM", 100.0, 1)]
    [InlineData("FO_APU_RUNNING", "L1011_RO_APU_RPM", 95.0, 1)]
    [InlineData("FO_APU_RUNNING", "L1011_RO_APU_RPM", 94.9, 0)]
    [InlineData("FO_APU_STARTING_OR_RUNNING", "L1011_RO_APU_RPM", 1.0, 1)]
    [InlineData("FO_APU_STARTING_OR_RUNNING", "L1011_RO_APU_RPM", 100.0, 1)]
    [InlineData("FO_APU_STARTING_OR_RUNNING", "L1011_RO_APU_RPM", 0.9, 0)]
    [InlineData("FO_APU_OFF", "L1011_RO_APU_RPM", 0.0, 1)]
    [InlineData("FO_APU_OFF", "L1011_RO_APU_RPM", 4.9, 1)]
    [InlineData("FO_APU_OFF", "L1011_RO_APU_RPM", 5.0, 0)]
    [InlineData("FO_APU_GEN_FIELD_CLOSED", "L1011_RO_APU_GEN_FIELD", 1.0, 1)]
    [InlineData("FO_APU_GEN_FIELD_CLOSED", "L1011_RO_APU_GEN_FIELD", 0.0, 0)]
    [InlineData("FO_APU_GEN_ON_LINE", "L1011_RO_APU_GEN_BREAKER", 1.0, 1)]
    [InlineData("FO_APU_GEN_ON_LINE", "L1011_RO_APU_GEN_BREAKER", 0.0, 0)]
    [InlineData("FO_IGNITION_SELECTED", "ROTARY_ENG_START_SEL", 0.0, 1)]   // A
    [InlineData("FO_IGNITION_SELECTED", "ROTARY_ENG_START_SEL", 2.0, 1)]   // B
    [InlineData("FO_IGNITION_SELECTED", "ROTARY_ENG_START_SEL", 1.0, 0)]   // both
    [InlineData("FO_IGNITION_BOTH", "ROTARY_ENG_START_SEL", 2.0, 0)]
    [InlineData("FO_FLAPS_UP", "L1011_FLAP_HANDLE", 0.0, 1)]
    [InlineData("FO_FLAPS_UP", "L1011_FLAP_HANDLE", 0.4, 1)]
    [InlineData("FO_FLAPS_UP", "L1011_FLAP_HANDLE", 1.0, 0)]
    [InlineData("FO_FLAPS_UP", "L1011_FLAP_HANDLE", 6.0, 0)]
    [InlineData("FO_GEAR_LEVER_UP", "L1011_GEAR_LEVER", 100.0, 1)]
    [InlineData("FO_GEAR_LEVER_UP", "L1011_GEAR_LEVER", 75.1, 1)]
    [InlineData("FO_GEAR_LEVER_UP", "L1011_GEAR_LEVER", 75.0, 0)]
    [InlineData("FO_GEAR_LEVER_UP", "L1011_GEAR_LEVER", 50.0, 0)]
    [InlineData("FO_GEAR_LEVER_UP", "L1011_GEAR_LEVER", 0.0, 0)]
    [InlineData("FO_STAB_TRIM_ZERO", "L1011_RO_ELEVATOR_TRIM", 0.0, 1)]
    [InlineData("FO_STAB_TRIM_ZERO", "L1011_RO_ELEVATOR_TRIM", 0.19, 1)]
    [InlineData("FO_STAB_TRIM_ZERO", "L1011_RO_ELEVATOR_TRIM", -0.19, 1)]
    [InlineData("FO_STAB_TRIM_ZERO", "L1011_RO_ELEVATOR_TRIM", 0.2, 0)]
    [InlineData("FO_STAB_TRIM_ZERO", "L1011_RO_ELEVATOR_TRIM", -0.2, 0)]
    [InlineData("FO_STAB_TRIM_ZERO", "L1011_RO_ELEVATOR_TRIM", 2.38, 0)]
    public void A_single_input_field_reads_its_threshold(string field, string key, double value, double want)
    {
        Assert.Equal(want, Compute(field, new() { [key] = value }));
        Assert.True(double.IsNaN(Compute(field, new Dictionary<string, double>())), $"{field} unread must read unknown");
        Assert.Equal(new[] { key }, InputsOf(field));
    }

    // ------------------------------------------------------------------ named cases

    [Theory]
    [InlineData(1.0, double.NaN, 1)]
    [InlineData(double.NaN, 1.0, 1)]
    [InlineData(1.0, 0.0, 1)]
    [InlineData(0.0, 1.0, 1)]
    [InlineData(0.0, 0.0, 0)]
    [InlineData(0.0, double.NaN, double.NaN)]
    [InlineData(double.NaN, 0.0, double.NaN)]
    [InlineData(double.NaN, double.NaN, double.NaN)]
    public void Power_source_is_either_breaker_closed(double apuBreaker, double extBreaker, double want)
        => AssertReads(want, Compute("FO_POWER_SOURCE_ON",
                Readings(("L1011_RO_APU_GEN_BREAKER", apuBreaker), ("L1011_RO_EXT_PWR_BREAKER", extBreaker))),
            $"APU {apuBreaker}, external {extBreaker}");

    [Theory]
    [InlineData(51.0, 51.0, 1)]
    [InlineData(50.0, 500.0, 0)]
    [InlineData(500.0, 50.0, 0)]
    [InlineData(500.0, double.NaN, double.NaN)]
    [InlineData(double.NaN, 500.0, double.NaN)]
    public void Tanks_1A_and_3A_are_fueled_above_fifty_pounds(double tank1A, double tank3A, double want)
        => AssertReads(want, Compute("FO_TANKS_1A_3A_FUELED",
                Readings(("L1011_RO_TANK_4", tank1A), ("L1011_RO_TANK_5", tank3A))),
            $"tank 1A {tank1A}, tank 3A {tank3A}");

    [Theory]
    [InlineData(1.0, 1.0, 1.05, 1)]
    [InlineData(1.0, 0.95, 1.0, 1)]
    [InlineData(1.0, 2.0, 1.0, 0)]     // one unit aligned, not navigating
    [InlineData(90.0, 1.0, 1.0, 0)]    // one unit not aligned
    [InlineData(1.0, 1.0, double.NaN, double.NaN)]
    [InlineData(double.NaN, 1.0, 1.0, double.NaN)]
    public void Ins_navigating_needs_every_status_at_one(double one, double two, double three, double want)
        => AssertReads(want, Compute("FO_INS_NAVIGATING", Readings(
                (L1011InsAlignmentAnnouncer.StatusKey(1), one),
                (L1011InsAlignmentAnnouncer.StatusKey(2), two),
                (L1011InsAlignmentAnnouncer.StatusKey(3), three))),
            $"statuses {one}, {two}, {three}");

    // ------------------------------------------------------------------ the doors

    private static readonly string[] DoorPositions =
    {
        "L1011_FO_EXIT_OPEN_0", "L1011_FO_EXIT_OPEN_1", "L1011_FO_EXIT_OPEN_2", "L1011_FO_EXIT_OPEN_3",
    };

    private const string DoorLight = "LAMP_LX_MIP_DOOR_OPEN";

    /// <summary>The four exits' positions in percent, a NaN one left unread.</summary>
    private static Dictionary<string, double> Doors(double d0, double d1, double d2, double d3)
        => Readings((DoorPositions[0], d0), (DoorPositions[1], d1), (DoorPositions[2], d2), (DoorPositions[3], d3));

    /// <summary>
    /// The doors are closed when every one of the four stock door positions reads below 5 percent. Live, the
    /// sim's door event opened the main door (exit 0 read 100) while the main panel's door light stayed out, so
    /// the light does not follow the doors and decides nothing.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0.0, 0.0, 0.0, 1)]
    [InlineData(100.0, 0.0, 0.0, 0.0, 0)]    // the main door open: the live finding
    [InlineData(0.0, 100.0, 0.0, 0.0, 0)]
    [InlineData(0.0, 0.0, 100.0, 0.0, 0)]
    [InlineData(0.0, 0.0, 0.0, 100.0, 0)]
    [InlineData(4.0, 0.0, 0.0, 0.0, 1)]      // under 5 percent is closed
    [InlineData(4.9, 4.9, 4.9, 4.9, 1)]
    [InlineData(5.0, 0.0, 0.0, 0.0, 0)]      // at 5 percent a door is open
    [InlineData(0.0, 0.0, 0.0, 5.0, 0)]
    [InlineData(50.0, 50.0, 50.0, 50.0, 0)]
    public void Doors_closed_needs_every_door_position_below_five_percent(
        double d0, double d1, double d2, double d3, double want)
        => AssertReads(want, Compute("FO_DOORS_CLOSED", Doors(d0, d1, d2, d3)), $"doors {d0}, {d1}, {d2}, {d3}");

    /// <summary>NaN when any door position is unread, an open door elsewhere included: never a guessed closed.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Doors_closed_is_unknown_when_any_door_position_is_unread(int unread)
    {
        var closed = Doors(0, 0, 0, 0);
        closed.Remove(DoorPositions[unread]);
        Assert.True(double.IsNaN(Compute("FO_DOORS_CLOSED", closed)), $"{DoorPositions[unread]} unread must read unknown");

        var oneOpen = Doors(0, 0, 0, 0);
        oneOpen.Remove(DoorPositions[unread]);
        oneOpen[DoorPositions[(unread + 1) % 4]] = 100;
        Assert.True(double.IsNaN(Compute("FO_DOORS_CLOSED", oneOpen)), "unread, with another door open, still reads unknown");
    }

    [Fact]
    public void Doors_closed_is_unknown_with_nothing_read()
        => Assert.True(double.IsNaN(Compute("FO_DOORS_CLOSED", new Dictionary<string, double>())));

    /// <summary>The main panel's door light no longer decides: out with a door open reads open, lit with every
    /// door closed reads closed, and its reading is not an input.</summary>
    [Fact]
    public void Doors_closed_reads_the_door_positions_never_the_door_light()
    {
        var open = Doors(100, 0, 0, 0);
        open[DoorLight] = 0;
        Assert.Equal(0, Compute("FO_DOORS_CLOSED", open));

        var closed = Doors(0, 0, 0, 0);
        closed[DoorLight] = 1;
        Assert.Equal(1, Compute("FO_DOORS_CLOSED", closed));

        Assert.Equal(DoorPositions, InputsOf("FO_DOORS_CLOSED"));
        Assert.DoesNotContain(DoorLight, L1011FoStateEvaluator.SyntheticInputs);
    }

    /// <summary>Through the evaluator's cache: the live finding (door open, light out) reads open.</summary>
    [Fact]
    public void Doors_closed_reads_the_cache_by_the_door_positions()
    {
        Assert.Equal(0, Evaluator((DoorPositions[0], 100), (DoorPositions[1], 0), (DoorPositions[2], 0), (DoorPositions[3], 0),
            (DoorLight, 0)).GetValue("FO_DOORS_CLOSED"));
        Assert.Equal(1, Evaluator((DoorPositions[0], 0), (DoorPositions[1], 0), (DoorPositions[2], 0), (DoorPositions[3], 0))
            .GetValue("FO_DOORS_CLOSED"));
        Assert.True(double.IsNaN(Evaluator((DoorPositions[0], 0)).GetValue("FO_DOORS_CLOSED")));
    }

    /// <summary>The door positions are read on request, so the window polls them each second.</summary>
    [Fact]
    public void The_poll_list_covers_the_door_positions()
    {
        var polled = new L1011FoStateEvaluator().OnRequestPollFields;

        foreach (var key in DoorPositions)
            Assert.Contains(key, polled);
        Assert.Equal(polled.Count, polled.Distinct().Count());
    }

    [Fact]
    public void Plan_values_are_null_without_a_plan_and_stored_as_given()
    {
        var e = new L1011FoStateEvaluator();
        Assert.Null(e.PlannedCruiseFt);
        Assert.Null(e.PlannedDestElevFt);

        e.SetPlannedPressurizationAltitudes(35000, 1234);
        Assert.Equal(35000, e.PlannedCruiseFt);
        Assert.Equal(1234, e.PlannedDestElevFt);

        // No rounding to a knob step, and a field below sea level is kept.
        e.SetPlannedPressurizationAltitudes(37123, -11);
        Assert.Equal(37123, e.PlannedCruiseFt);
        Assert.Equal(-11, e.PlannedDestElevFt);

        e.SetPlannedPressurizationAltitudes(null, null);
        Assert.Null(e.PlannedCruiseFt);
        Assert.Null(e.PlannedDestElevFt);
    }

    /// <summary>The window hands the plan over through the interface: the override must be the one called.</summary>
    [Fact]
    public void The_plan_lands_through_the_interface_and_the_base_class()
    {
        var e = new L1011FoStateEvaluator();
        ((IFoStateEvaluator)e).SetPlannedPressurizationAltitudes(35000, 1500);
        Assert.Equal(35000, e.PlannedCruiseFt);
        Assert.Equal(1500, e.PlannedDestElevFt);

        ((LVarStateEvaluator)e).SetPlannedPressurizationAltitudes(31000, null);
        Assert.Equal(31000, e.PlannedCruiseFt);
        Assert.Null(e.PlannedDestElevFt);
    }

    private const string CabinAltSelected = "L1011_RO_CABIN_ALT_SELECTED";

    [Fact]
    public void Cabin_altitude_selected_compares_in_feet_from_a_metres_readout()
    {
        // The destination: the readout is delivered in metres.
        double Dest(double feet, int? destElev = 1500)
        {
            var e = Evaluator((CabinAltSelected, feet * 0.3048));
            e.SetPlannedPressurizationAltitudes(null, destElev);
            return e.GetValue("FO_CABIN_ALT_AT_DEST");
        }
        Assert.Equal(1, Dest(1500));
        Assert.Equal(1, Dest(1500 + 149));
        Assert.Equal(0, Dest(1500 + 151));
        Assert.Equal(0, Dest(1500 - 151));
        Assert.Equal(1, Dest(-11, destElev: -11));
        Assert.True(double.IsNaN(Dest(1500, destElev: null)), "no plan must read unknown");
        // The selector stops at -250 and 8,000 ft: a destination beyond either is compared at that stop.
        Assert.Equal(1, Dest(8000, destElev: 9000));
        Assert.Equal(0, Dest(9000, destElev: 9000));
        Assert.Equal(1, Dest(-250, destElev: -500));
        Assert.Equal(0, Dest(-500, destElev: -500));

        var unread = Evaluator();
        unread.SetPlannedPressurizationAltitudes(null, 1500);
        Assert.True(double.IsNaN(unread.GetValue("FO_CABIN_ALT_AT_DEST")), "the readout unread must read unknown");

        // The cruise: the cabin altitude that holds the maximum differential at the planned cruise.
        int cabin = L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(35000);
        double Cruise(double feet, int? cruise = 35000)
        {
            var e = Evaluator((CabinAltSelected, feet * 0.3048));
            e.SetPlannedPressurizationAltitudes(cruise, null);
            return e.GetValue("FO_CABIN_ALT_AT_CRUISE");
        }
        Assert.Equal(1, Cruise(cabin));
        Assert.Equal(0, Cruise(cabin + 151));
        Assert.Equal(0, Cruise(cabin - 151));
        Assert.True(double.IsNaN(Cruise(cabin, cruise: null)), "no plan must read unknown");

        var noDest = Evaluator((CabinAltSelected, cabin * 0.3048));
        noDest.SetPlannedPressurizationAltitudes(35000, null);
        Assert.True(double.IsNaN(noDest.GetValue("FO_CABIN_ALT_AT_DEST")), "a cruise alone gives the destination nothing");

        Assert.Equal(new[] { CabinAltSelected }, InputsOf("FO_CABIN_ALT_AT_CRUISE"));
        Assert.Equal(new[] { CabinAltSelected }, InputsOf("FO_CABIN_ALT_AT_DEST"));
    }

    /// <summary>The destination cabin altitude is the plan's destination elevation, clamped to the selector's end stops
    /// (-250 to 8,000 ft), and null without a plan.</summary>
    [Fact]
    public void Destination_cabin_altitude_is_the_plan_clamped_to_the_selector_or_null()
    {
        var e = new L1011FoStateEvaluator();
        Assert.Null(e.DestinationCabinAltitudeFt());

        e.SetPlannedPressurizationAltitudes(null, 1234);
        Assert.Equal(1234, e.DestinationCabinAltitudeFt());
        e.SetPlannedPressurizationAltitudes(35000, -11);
        Assert.Equal(-11, e.DestinationCabinAltitudeFt());
        e.SetPlannedPressurizationAltitudes(35000, 8000);
        Assert.Equal(8000, e.DestinationCabinAltitudeFt());
        e.SetPlannedPressurizationAltitudes(35000, 9000);
        Assert.Equal(8000, e.DestinationCabinAltitudeFt());
        e.SetPlannedPressurizationAltitudes(35000, -400);
        Assert.Equal(-250, e.DestinationCabinAltitudeFt());
        Assert.Equal(-400, e.PlannedDestElevFt);   // the plan itself is kept as given

        e.SetPlannedPressurizationAltitudes(35000, null);
        Assert.Null(e.DestinationCabinAltitudeFt());
    }

    [Fact]
    public void Cruise_cabin_altitude_is_the_pressurization_modules_answer_or_null()
    {
        var e = new L1011FoStateEvaluator();
        Assert.Null(e.CruiseCabinAltitudeFt());

        e.SetPlannedPressurizationAltitudes(35000, 1500);
        Assert.Equal(L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(35000), e.CruiseCabinAltitudeFt());

        e.SetPlannedPressurizationAltitudes(null, 1500);
        Assert.Null(e.CruiseCabinAltitudeFt());
    }

    private const string CabinBaro = "L1011_RO_CABIN_BARO";
    private const string Altimeter1 = "L1011_RO_ALTIMETER_1";

    [Theory]
    [InlineData(1002.0, 1002.3, 1)]
    [InlineData(1002.0, 1001.7, 1)]
    [InlineData(1002.0, 1002.6, 0)]
    [InlineData(1002.0, 1001.4, 0)]
    [InlineData(1013.25, 1013.25, double.NaN)]   // standard is "no value"
    [InlineData(1013.4, 1013.4, double.NaN)]     // within 0.2 of standard
    [InlineData(1013.6, 1013.6, 1)]
    [InlineData(double.NaN, 1002.0, double.NaN)]
    [InlineData(1002.0, double.NaN, double.NaN)]
    public void Cabin_baro_set_needs_a_captain_altimeter_off_standard(double altimeter, double cabinBaro, double want)
    {
        AssertReads(want, Compute("FO_CABIN_BARO_SET", Readings((Altimeter1, altimeter), (CabinBaro, cabinBaro))),
            $"altimeter {altimeter}, cabin baro {cabinBaro}");
        AssertReads(want, Evaluator((Altimeter1, altimeter), (CabinBaro, cabinBaro)).GetValue("FO_CABIN_BARO_SET"),
            $"the instance: altimeter {altimeter}, cabin baro {cabinBaro}");
    }

    [Theory]
    [InlineData(1002.37, 100237)]
    [InlineData(1013.3, null)]
    [InlineData(1013.25, null)]
    [InlineData(1013.6, 101360)]
    [InlineData(1012.9, 101290)]
    [InlineData(double.NaN, null)]
    public void Cabin_baro_target_is_the_captain_altimeter_in_hundredths_off_standard(double altimeter, int? want)
        => Assert.Equal(want, Evaluator((Altimeter1, altimeter)).CabinBaroTargetHundredths());

    [Fact]
    public void Cabin_baro_target_is_null_without_a_connection()
        => Assert.Null(new L1011FoStateEvaluator().CabinBaroTargetHundredths());

    [Theory]
    [InlineData(4.0, 1)]
    [InlineData(10.0, 2)]
    [InlineData(14.0, 3)]
    [InlineData(18.0, 4)]
    [InlineData(22.0, 5)]
    [InlineData(33.0, 6)]
    [InlineData(10.4, 2)]
    [InlineData(9.6, 2)]
    [InlineData(10.6, null)]
    [InlineData(12.0, null)]      // between detents
    [InlineData(0.0, null)]       // no export
    [InlineData(-1.0, null)]      // SimBrief's "no plan"
    [InlineData(40.0, null)]
    [InlineData(double.NaN, null)]
    public void Flap_index_for_degrees_is_the_handle_detent(double degrees, int? want)
        => Assert.Equal(want, L1011FoStateEvaluator.FlapIndexForDegrees(degrees));

    [Fact]
    public void Takeoff_flap_index_prefers_the_tablet_then_simbrief()
    {
        const string tablet = IniL1011Definition.PerfSelectedFlapsKey;

        Assert.Equal(3, Evaluator((tablet, 14)).TakeoffFlapIndex());

        var simBrief10 = Evaluator((tablet, 0));
        simBrief10.SetTakeoffFlaps(10);
        Assert.Equal(2, simBrief10.TakeoffFlapIndex());

        var neither = Evaluator((tablet, 0));
        neither.SetTakeoffFlaps(0);
        Assert.Null(neither.TakeoffFlapIndex());

        Assert.Null(Evaluator((tablet, 12)).TakeoffFlapIndex());   // not a detent

        var tabletUnread = Evaluator();
        tabletUnread.SetTakeoffFlaps(22);
        Assert.Equal(5, tabletUnread.TakeoffFlapIndex());

        var both = Evaluator((tablet, 14));
        both.SetTakeoffFlaps(10);
        Assert.Equal(3, both.TakeoffFlapIndex());                  // the tablet wins

        Assert.Null(new L1011FoStateEvaluator().TakeoffFlapIndex()); // nothing at all
    }

    [Fact]
    public void Flaps_takeoff_is_nan_without_a_target()
    {
        const string tablet = IniL1011Definition.PerfSelectedFlapsKey;
        const string handle = L1011Levers.FlapHandleKey;

        Assert.True(double.IsNaN(Evaluator((handle, 3)).GetValue("FO_FLAPS_TAKEOFF")), "no tablet, no plan");
        Assert.True(double.IsNaN(Compute("FO_FLAPS_TAKEOFF", new() { [handle] = 3 })), "the pure field has no plan");

        Assert.Equal(1, Evaluator((tablet, 14), (handle, 3)).GetValue("FO_FLAPS_TAKEOFF"));
        Assert.Equal(1, Evaluator((tablet, 14), (handle, 3.2)).GetValue("FO_FLAPS_TAKEOFF"));
        Assert.Equal(0, Evaluator((tablet, 14), (handle, 2)).GetValue("FO_FLAPS_TAKEOFF"));
        Assert.True(double.IsNaN(Evaluator((tablet, 14)).GetValue("FO_FLAPS_TAKEOFF")), "the handle unread");

        var simBrief = Evaluator((handle, 2));
        simBrief.SetTakeoffFlaps(10);
        Assert.Equal(1, simBrief.GetValue("FO_FLAPS_TAKEOFF"));

        Assert.Equal(1, Compute("FO_FLAPS_TAKEOFF", new() { [tablet] = 14, [handle] = 3 }));
    }

    /// <summary>
    /// NAV only for a unit whose selector reads ALIGN and whose status reads aligned, or whose selector already reads
    /// NAV. A unit the pilot left at OFF, STBY or ATT, or whose selector is unread, gets no value, whatever its status
    /// reads: the First Officer never switches a unit on, and its Captain line speaks instead.
    /// </summary>
    [Fact]
    public void Ins_nav_target_is_three_only_for_an_aligned_or_navigating_unit()
    {
        string status = L1011InsAlignmentAnnouncer.StatusKey(1);
        string mode = L1011InsAlignmentAnnouncer.MsuKey(1);

        Assert.Equal(3, Evaluator((status, 2), (mode, 2)).InsNavTarget(1));
        Assert.Equal(3, Evaluator((status, 90), (mode, 3)).InsNavTarget(1));
        Assert.Equal(3, Evaluator((status, 1), (mode, 3)).InsNavTarget(1));
        Assert.Null(Evaluator((status, 90), (mode, 2)).InsNavTarget(1));
        Assert.Null(Evaluator((status, 50), (mode, 2)).InsNavTarget(1));
        Assert.Null(Evaluator().InsNavTarget(1));
        Assert.Null(new L1011FoStateEvaluator().InsNavTarget(1));

        // Aligned, but the selector off ALIGN and NAV, or unread: left where it is.
        Assert.Null(Evaluator((status, 2), (mode, 0)).InsNavTarget(1));
        Assert.Null(Evaluator((status, 2), (mode, 1)).InsNavTarget(1));
        Assert.Null(Evaluator((status, 2), (mode, 4)).InsNavTarget(1));
        Assert.Null(Evaluator((status, 2)).InsNavTarget(1));

        // A unit reads its own status and selector, never another unit's.
        Assert.Null(Evaluator((status, 2), (mode, 3)).InsNavTarget(2));
        Assert.Equal(3, Evaluator((L1011InsAlignmentAnnouncer.StatusKey(3), 2), (L1011InsAlignmentAnnouncer.MsuKey(3), 2)).InsNavTarget(3));
    }

    /// <summary>The readouts the flows' fields read, polled each second; never the total fuel, which the
    /// fuel hotkey's readout speaks on every delivery.</summary>
    [Fact]
    public void The_poll_list_covers_the_part_2_readouts()
    {
        var polled = new L1011FoStateEvaluator().OnRequestPollFields;
        foreach (var k in new[]
        {
            "L1011_RO_APU_RPM",
            "L1011_RO_GEN_1_FIELD", "L1011_RO_GEN_2_FIELD", "L1011_RO_GEN_3_FIELD",
            "L1011_RO_GEN_1_BREAKER", "L1011_RO_GEN_2_BREAKER", "L1011_RO_GEN_3_BREAKER",
            "L1011_RO_APU_GEN_FIELD", "L1011_RO_APU_GEN_BREAKER",
            "L1011_RO_TANK_4", "L1011_RO_TANK_5",
            "L1011_RO_CABIN_ALT_SELECTED", "L1011_RO_CABIN_BARO", "L1011_RO_ALTIMETER_1",
            "L1011_RO_GEAR_LEFT", "L1011_RO_GEAR_NOSE", "L1011_RO_GEAR_RIGHT",
            "L1011_RO_ELEVATOR_TRIM", IniL1011Definition.PerfSelectedFlapsKey,
        })
            Assert.Contains(k, polled);
        Assert.DoesNotContain("L1011_RO_FUEL_TOTAL", polled);
        Assert.Equal(polled.Count, polled.Distinct().Count());
    }
}
