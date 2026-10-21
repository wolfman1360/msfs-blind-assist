using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's state evaluator: every synthetic field is NaN until all of its inputs are
/// read (never 0 for unread), and each field's true and false case through the pure <c>Compute</c>.
/// The live findings of 2026-10-21 pin the engine reads: a cold engine's N3 gauge reads 1.4 to 1.9
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

    /// <summary>Live 2026-10-21: a cold TriStar's N3 gauge reads 1.4 to 1.9, never 0; that is stopped.</summary>
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
            "FO_AC_PUMPS_OFF", "FO_ATMS_OFF", "FO_ENG1_RUNNING", "FO_ENG2_RUNNING", "FO_ENG3_RUNNING",
            "FO_ENGINES_STOPPED", "FO_EXT_POWER_AVAILABLE", "FO_EXT_POWER_ON", "FO_FIRE_HANDLES_IN",
            "FO_FLAPS_AGREE", "FO_FUEL_IGNITION_OFF", "FO_FUEL_PUMPS_OFF", "FO_GALLEY_OFF", "FO_GEAR_LEVER_DOWN",
            "FO_INS_ALIGN_OR_NAV", "FO_JETTISON_SAFE", "FO_PAX_OXY_SAFE", "FO_RADIO_MASTERS_ON",
            "FO_SPEED_BRAKE_FORWARD", "FO_STBY_ARMED",
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
    /// the same name (live 2026-10-21: the L:var reads 1 on a cold aircraft).
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
}
