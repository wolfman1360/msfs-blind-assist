using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011ReadoutsTests
{
    [Fact]
    public void Keys_are_unique()
    {
        Assert.Equal(L1011Readouts.All.Count, L1011Readouts.All.Select(r => r.Key).Distinct().Count());
    }

    [Fact]
    public void Values_format_invariantly_in_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var fuel = L1011Readouts.All.Single(r => r.Key == "L1011_RO_TANK_3");
            var epr = L1011Readouts.All.Single(r => r.Key == "L1011_RO_EPR_1");
            Assert.Equal("Tank 1 fuel", fuel.Name);
            Assert.Equal("12,400 pounds", L1011Readouts.FormatValue(fuel, 12400.4));
            var altimeter = L1011Readouts.All.Single(r => r.Key == "L1011_RO_ALTIMETER_1");
            Assert.Equal("1013 hectopascals", L1011Readouts.FormatValue(altimeter, 1013.2));
            Assert.Equal("1.421", L1011Readouts.FormatValue(epr, 1.4214));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("L1011_RO_AILERON_TRIM", -0.3, "0 percent")]
    [InlineData("L1011_RO_CABIN_DIFF", -0.04, "0.0 psi")]
    [InlineData("L1011_RO_CABIN_RATE", -0.4, "0 feet per minute")]
    [InlineData("L1011_RO_AILERON_TRIM", -1, "-1 percent")]
    [InlineData("L1011_RO_AILERON_TRIM", -0.6, "-1 percent")]
    [InlineData("L1011_RO_CABIN_DIFF", -0.06, "-0.1 psi")]
    public void A_negative_that_rounds_to_zero_never_reads_minus_zero(string key, double value, string expected)
    {
        var readout = L1011Readouts.All.Single(r => r.Key == key);
        Assert.Equal(expected, L1011Readouts.FormatValue(readout, value));
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(expected, L1011Readouts.FormatValue(readout, value));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("-0", "0")]
    [InlineData("-0.0", "0.0")]
    [InlineData("-0.00", "0.00")]
    [InlineData("-1", "-1")]
    [InlineData("-0.1", "-0.1")]
    [InlineData("-1,000", "-1,000")]
    [InlineData("0", "0")]
    [InlineData("12", "12")]
    public void Minus_zero_loses_its_sign_and_nothing_else_changes(string formatted, string expected)
    {
        Assert.Equal(expected, L1011Readouts.WithoutNegativeZero(formatted));
    }

    private static L1011Readout Readout(string key) => L1011Readouts.All.Single(r => r.Key == key);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_engine_gauges_read_what_the_cockpit_gauges_read(int engine)
    {
        // L1011_COCKPIT.behavior.xml: the EPR needle and drums read L:ENGn_EPR_CALC, the N1 and N3
        // gauges L:INI_ENGn_N1 and L:INI_ENGn_N3. The stock TURB ENG PRESSURE RATIO read 1.34 at idle.
        Assert.Equal($"L:ENG{engine}_EPR_CALC", Readout($"L1011_RO_EPR_{engine}").SimVar);
        Assert.Equal($"L:INI_ENG{engine}_N1", Readout($"L1011_RO_N1_{engine}").SimVar);
        Assert.Equal($"L:INI_ENG{engine}_N2", Readout($"L1011_RO_N2_{engine}").SimVar);
        Assert.Equal($"L:INI_ENG{engine}_N3", Readout($"L1011_RO_N3_{engine}").SimVar);
        Assert.Equal($"Engine {engine} N2", Readout($"L1011_RO_N2_{engine}").Name);
        Assert.Equal($"Engine {engine} N3", Readout($"L1011_RO_N3_{engine}").Name);
    }

    [Fact]
    public void N2_reads_the_engineers_n2_gauge_variable_never_the_stock_one()
    {
        // The flight engineer's engine status panel has N2 gauges (manual 2.8); the stock TURB ENG
        // variables are not what the TriStar's gauges show.
        Assert.DoesNotContain(L1011Readouts.All, r => r.SimVar.StartsWith("TURB ENG", StringComparison.Ordinal));
        var n2 = Readout("L1011_RO_N2_1");
        Assert.Equal("Engine Instruments", n2.Panel);
        Assert.Equal("1.3 percent", L1011Readouts.FormatValue(n2, 1.26));   // live, engine off
    }

    [Fact]
    public void The_cabin_barometric_setting_reads_kohlsman_5_as_the_baro_set_readout_reads_kohlsman_4()
    {
        var cabin = Readout("L1011_RO_CABIN_BARO_SETTING");
        Assert.Equal(("Cabin barometric setting", "KOHLSMAN SETTING MB:5", "Pressurization"), (cabin.Name, cabin.SimVar, cabin.Panel));
        var baro = Readout("L1011_RO_CABIN_BARO");
        Assert.Equal((baro.Units, baro.Format, baro.Suffix), (cabin.Units, cabin.Format, cabin.Suffix));
        Assert.Equal("1013 hectopascals", L1011Readouts.FormatValue(cabin, 1013.25));
    }

    [Theory]
    [InlineData("L1011_RO_OUTFLOW_FWD", "Forward outflow valve", "L:OUTFLOW_VALVE_FWD")]
    [InlineData("L1011_RO_OUTFLOW_AFT", "Aft outflow valve", "L:OUTFLOW_VALVE_AFT")]
    public void The_outflow_valves_read_percent_open(string key, string name, string simVar)
    {
        var valve = Readout(key);
        Assert.Equal((name, simVar, "Pressurization"), (valve.Name, valve.SimVar, valve.Panel));
        Assert.Equal("100 percent open", L1011Readouts.FormatValue(valve, 100));   // live at the gate
        Assert.Equal("37 percent open", L1011Readouts.FormatValue(valve, 36.6));
    }

    [Theory]
    [InlineData("L1011_RO_DH_CPT", "Captain decision height", "L:ROTARY_RA_CPT_DH_SET", "Captain Instruments", 200, "200 feet")]
    [InlineData("L1011_RO_DH_FO", "First officer decision height", "L:ROTARY_RA_FO_DH_SET", "First Officer Instruments", 499, "499 feet")]
    [InlineData("L1011_RO_REF_BUG_CPT", "Captain reference speed bug", "L:BUG_CPT_REF_SPEED", "Captain Instruments", 60, "60 knots")]
    [InlineData("L1011_RO_REF_BUG_FO", "First officer reference speed bug", "L:BUG_FO_REF_SPEED", "First Officer Instruments", 137.4, "137 knots")]
    public void Each_pilot_reads_the_decision_height_and_reference_bug_the_knobs_set(string key, string name, string simVar,
        string panel, double value, string spoken)
    {
        // L1011_INSTRUMENTS.js: the DH knobs set L:ROTARY_RA_CPT_DH_SET and L:ROTARY_RA_FO_DH_SET (0 to 499 feet),
        // the reference bug knobs L:BUG_CPT_REF_SPEED and L:BUG_FO_REF_SPEED (knots).
        var readout = Readout(key);
        Assert.Equal((name, simVar, panel), (readout.Name, readout.SimVar, readout.Panel));
        Assert.Equal(spoken, L1011Readouts.FormatValue(readout, value));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Each_ins_panel_reads_the_distance_and_time_to_the_waypoint(int unit)
    {
        var distance = Readout($"L1011_RO_INS_{unit}_DISTANCE");
        var time = Readout($"L1011_RO_INS_{unit}_TIME");
        Assert.Equal(($"INS {unit} distance to waypoint", $"L:CPT_HSI_INS_{unit}_DISTANCE_NM", $"INS {unit}"),
            (distance.Name, distance.SimVar, distance.Panel));
        Assert.Equal(($"INS {unit} time to waypoint", $"L:CPT_HSI_INS_{unit}_TIME_MIN", $"INS {unit}"),
            (time.Name, time.SimVar, time.Panel));
        Assert.Equal("123.4 miles", L1011Readouts.FormatValue(distance, 123.44));
        Assert.Equal("0.0 minutes", L1011Readouts.FormatValue(time, 0));   // below 100 knots ground speed
    }

    [Theory]
    [InlineData("L1011_RO_SET_TEMP_FLT_STA", "Flight station set temperature", "L:TEMP_SET_FLT_STA")]
    [InlineData("L1011_RO_SET_TEMP_FWD_CAB", "Forward cabin set temperature", "L:TEMP_SET_FWD_CAB")]
    [InlineData("L1011_RO_SET_TEMP_MID_CAB", "Mid cabin set temperature", "L:TEMP_SET_MID_CAB")]
    [InlineData("L1011_RO_SET_TEMP_AFT_CAB", "Aft cabin set temperature", "L:TEMP_SET_AFT_CAB")]
    public void The_zone_temperature_knobs_read_the_temperature_they_set(string key, string name, string simVar)
    {
        var set = Readout(key);
        Assert.Equal((name, simVar, "Air Conditioning"), (set.Name, set.SimVar, set.Panel));
        Assert.Equal("24 degrees", L1011Readouts.FormatValue(set, 24));   // live at the gate
    }

    [Theory]
    [InlineData(1, "Flight station zone temperature")]
    [InlineData(2, "Forward cabin zone temperature")]
    [InlineData(3, "Mid cabin zone temperature")]
    [InlineData(4, "Aft cabin zone temperature")]
    [InlineData(5, "Galley zone temperature")]
    public void The_zone_temperatures_read_in_the_zone_display_order(int area, string name)
    {
        // L1011_ENGINEER_PANEL.js reads Area_1 to Area_5 for the zone temperature display (systems.cfg:
        // cockpit, forward cabin, mid cabin, rear cabin, galley).
        var zone = Readout($"L1011_RO_ZONE_TEMP_{area}");
        Assert.Equal((name, $"PNEUMATICS AREA TEMPERATURE:{area}", "Celsius", "Air Conditioning"),
            (zone.Name, zone.SimVar, zone.Units, zone.Panel));
        Assert.Equal("19 degrees", L1011Readouts.FormatValue(zone, 19.0));   // live, area 1
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_pack_temperatures_read_each_pack(int pack)
    {
        var temperature = Readout($"L1011_RO_PACK_TEMP_{pack}");
        Assert.Equal(($"Pack {pack} temperature", $"PNEUMATICS PACK TEMPERATURE:{pack}", "Celsius", "Air Conditioning"),
            (temperature.Name, temperature.SimVar, temperature.Units, temperature.Panel));
        Assert.Equal("25 degrees", L1011Readouts.FormatValue(temperature, 24.7));   // live
    }

    [Theory]
    [InlineData("L1011_RO_PACK_FLOW_1", "Pack 1 valve flow", "L:SWITCH_PACK_FLOW_ENG_1_FLOWBAR", "Air Conditioning")]
    [InlineData("L1011_RO_PACK_FLOW_2", "Pack 2 valve flow", "L:SWITCH_PACK_FLOW_ENG_2_FLOWBAR", "Air Conditioning")]
    [InlineData("L1011_RO_PACK_FLOW_3", "Pack 3 valve flow", "L:SWITCH_PACK_FLOW_ENG_3_FLOWBAR", "Air Conditioning")]
    [InlineData("L1011_RO_HI_PRESS_FLOW_1", "Engine 1 high pressure valve flow", "L:SWITCH_HI_PRESS_ENG_1_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_HI_PRESS_FLOW_2", "Engine 2 high pressure valve flow", "L:SWITCH_HI_PRESS_ENG_2_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_HI_PRESS_FLOW_3", "Engine 3 high pressure valve flow", "L:SWITCH_HI_PRESS_ENG_3_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_ISLN_FLOW_1", "Engine 1 isolation valve flow", "L:SWITCH_ISLN_VALVE_ENG_1_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_ISLN_FLOW_2", "Engine 2 isolation valve flow", "L:SWITCH_ISLN_VALVE_ENG_2_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_ISLN_FLOW_3", "Engine 3 isolation valve flow", "L:SWITCH_ISLN_VALVE_ENG_3_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_XBLEED_1_FLOW", "Crossbleed valve 1 flow", "L:SWITCH_XBLEED_1_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_XBLEED_3_FLOW", "Crossbleed valve 3 flow", "L:SWITCH_XBLEED_3_FLOWBAR", "Bleed Air")]
    [InlineData("L1011_RO_FUSELAGE_ISLN_FLOW", "Fuselage isolation valve flow", "L:SWITCH_APU_ISLN_VALVE_FLOWBAR", "Bleed Air")]
    public void The_valve_flowbars_read_on_while_the_valve_is_open_with_pressure(string key, string name, string simVar, string panel)
    {
        // L1011_ENGINEER_PANEL.js: the flowbar in each switch is 1 while its valve is open with pressure.
        var flow = Readout(key);
        Assert.Equal((name, simVar, panel), (flow.Name, flow.SimVar, flow.Panel));
        Assert.Equal("on", L1011Readouts.FormatValue(flow, 1));
        Assert.Equal("off", L1011Readouts.FormatValue(flow, 0));
    }

    [Theory]
    [InlineData("L1011_RO_WING_ANTI_ICE_LEFT", "Left wing anti-ice valve", "L:SWITCH_ANTI_ICE_LEFT_MAN_OPEN")]
    [InlineData("L1011_RO_WING_ANTI_ICE_RIGHT", "Right wing anti-ice valve", "L:SWITCH_ANTI_ICE_RIGHT_MAN_OPEN")]
    public void The_wing_anti_ice_valves_read_open_or_closed(string key, string name, string simVar)
    {
        var valve = Readout(key);
        Assert.Equal((name, simVar, "Anti-Ice"), (valve.Name, valve.SimVar, valve.Panel));
        Assert.Equal("open", L1011Readouts.FormatValue(valve, 1));
        Assert.Equal("closed", L1011Readouts.FormatValue(valve, 0));
    }

    [Fact]
    public void A_flow_or_temperature_line_never_takes_the_name_of_a_switch_or_knob_beside_it()
    {
        // The "Flight station temperature" knob sets the zone, and the "Pack 1 flow" switch is the valve's
        // position, not its flowbar: a status line with the same name would read as that control's value.
        var rows = L1011PanelLayout.Place(L1011ControlMap.Load(), L1011Levers.Keys).RowsByPanel;
        foreach (var panel in new[] { "Air Conditioning", "Bleed Air", "Anti-Ice" })
        {
            var names = rows[panel].Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(L1011Readouts.All.Where(r => r.Panel == panel), r => Assert.DoesNotContain(r.Name, names));
        }
    }

    [Fact]
    public void The_new_decimal_readouts_keep_their_decimal_point_in_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("1.3 percent", L1011Readouts.FormatValue(Readout("L1011_RO_N2_2"), 1.26));
            Assert.Equal("123.4 miles", L1011Readouts.FormatValue(Readout("L1011_RO_INS_1_DISTANCE"), 123.44));
            Assert.Equal("12.5 minutes", L1011Readouts.FormatValue(Readout("L1011_RO_INS_3_TIME"), 12.46));
            Assert.Equal("1013 hectopascals", L1011Readouts.FormatValue(Readout("L1011_RO_CABIN_BARO_SETTING"), 1013.25));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData("A", 7)]
    [InlineData("B", 8)]
    [InlineData("C", 9)]
    [InlineData("D", 10)]
    public void Each_hydraulic_system_reads_its_own_pressure_checker(string system, int actuator)
    {
        // systems.cfg: SYSTEM_1_CHECKER to SYSTEM_4_CHECKER (actuators 7 to 10) sit on the junctions of
        // systems A to D, and the cockpit's pressure gauges read those four actuators.
        var readout = L1011Readouts.All.Single(r => r.Name == $"System {system} hydraulic pressure");
        Assert.Equal($"HYDRAULIC ACTUATOR PRESSURE:{actuator}", readout.SimVar);
        Assert.Equal("Hydraulics", readout.Panel);
    }

    [Theory]
    [InlineData("AC bus 1 voltage", "ELECTRICAL BUS VOLTAGE:9")]
    [InlineData("AC bus 2 voltage", "ELECTRICAL BUS VOLTAGE:12")]
    [InlineData("AC bus 3 voltage", "ELECTRICAL BUS VOLTAGE:13")]
    [InlineData("AC essential bus voltage", "ELECTRICAL BUS VOLTAGE:23")]
    [InlineData("DC essential bus voltage", "ELECTRICAL BUS VOLTAGE:21")]
    public void The_electrical_panel_reads_the_aircrafts_own_buses(string name, string simVar)
    {
        // The modular electrical system (systems.cfg bus.9 AC_BUS_1 ... bus.23 AC_ESS_BUS) never feeds
        // the legacy ELECTRICAL MAIN BUS VOLTAGE, which read 0.0 with every bus powered.
        var readout = L1011Readouts.All.Single(r => r.Name == name);
        Assert.Equal(simVar, readout.SimVar);
        Assert.Equal("Electrical", readout.Panel);
        Assert.DoesNotContain(L1011Readouts.All, r => r.SimVar == "ELECTRICAL MAIN BUS VOLTAGE");
    }

    [Theory]
    [InlineData("Generator 1 field", 1)]
    [InlineData("Generator 2 field", 2)]
    [InlineData("Generator 3 field", 3)]
    [InlineData("APU generator field", 4)]
    [InlineData("Generator 1 breaker", 5)]
    [InlineData("Generator 2 breaker", 8)]
    [InlineData("Generator 3 breaker", 10)]
    [InlineData("APU generator breaker", 12)]
    [InlineData("External power breaker", 13)]
    [InlineData("AC tie breaker 1", 7)]
    [InlineData("AC tie breaker 2", 9)]
    [InlineData("AC tie breaker 3", 11)]
    public void The_generator_and_tie_breakers_read_their_relays(string name, int relay)
    {
        // Their CLOSE and TRIP buttons are latches the aircraft resets, so the state is the relay
        // L1011_ENGINEER_PANEL.js switches (systems.cfg relay.1 FR_1_RELAY ... relay.13 EXT_PWR_RELAY).
        var readout = L1011Readouts.All.Single(r => r.Name == name);
        Assert.Equal($"ELECTRICAL RELAY CONNECTION ON:{relay}", readout.SimVar);
        Assert.Equal("Electrical", readout.Panel);
        Assert.Equal("closed", L1011Readouts.FormatValue(readout, 1));
        Assert.Equal("open", L1011Readouts.FormatValue(readout, 0));
    }

    [Fact]
    public void The_pressurization_panel_reads_its_selectors()
    {
        // L1011_ENGINEER_PANEL.js maps the ALT SEL knob's step (L:FLT_CAB) to L:TARGET_CABIN_ALTITUDE, the
        // cabin altitude L1011_PRESSURIZATION.js flies; the BARO SET knob steps Kohlsman setting 4.
        var cabin = Readout("L1011_RO_CABIN_ALT_SELECTED");
        Assert.Equal(("Selected cabin altitude", "L:TARGET_CABIN_ALTITUDE", "Pressurization"), (cabin.Name, cabin.SimVar, cabin.Panel));
        // An L:var reaches MSFSBA in its stored unit (every L:var is registered as "number"): the gauge
        // writes this one in feet, so it arrives in metres. Live: 2,220 here for 7,284 feet.
        Assert.Equal("7,284 feet", L1011Readouts.FormatValue(cabin, 2220.2));
        var baro = Readout("L1011_RO_CABIN_BARO");
        Assert.Equal(("Barometric pressure setting", "KOHLSMAN SETTING MB:4", "Pressurization"), (baro.Name, baro.SimVar, baro.Panel));
        Assert.Equal("1013 hectopascals", L1011Readouts.FormatValue(baro, 1013.25));
    }

    [Fact]
    public void The_stabilizer_trim_reads_to_the_hundredth_as_the_tablet_gives_it()
    {
        Assert.Equal("2.44 degrees", L1011Readouts.FormatValue(Readout("L1011_RO_ELEVATOR_TRIM"), 2.4416));
    }

    [Fact]
    public void The_anti_ice_panel_reads_the_ice_on_the_airframe()
    {
        var ice = Readout("L1011_RO_STRUCTURAL_ICE");
        Assert.Equal(("Structural ice", "STRUCTURAL ICE PCT", "Anti-Ice"), (ice.Name, ice.SimVar, ice.Panel));
        Assert.Equal("12 percent", L1011Readouts.FormatValue(ice, 12.3));
    }

    [Fact]
    public void The_squawk_is_decoded_from_bcd()
    {
        Assert.Equal("2000", L1011Readouts.DecodeSquawk(0x2000));
        Assert.Equal("7421", L1011Readouts.DecodeSquawk(0x7421));
    }
}
