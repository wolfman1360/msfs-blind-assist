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
        Assert.Equal($"L:INI_ENG{engine}_N3", Readout($"L1011_RO_N3_{engine}").SimVar);
        Assert.Equal($"Engine {engine} N3", Readout($"L1011_RO_N3_{engine}").Name);
    }

    [Fact]
    public void The_rb211_has_no_n2_gauge_on_the_panel()
    {
        Assert.DoesNotContain(L1011Readouts.All, r => r.Name.EndsWith(" N2", StringComparison.Ordinal));
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
