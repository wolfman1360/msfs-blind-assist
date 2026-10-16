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

    [Fact]
    public void The_squawk_is_decoded_from_bcd()
    {
        Assert.Equal("2000", L1011Readouts.DecodeSquawk(0x2000));
        Assert.Equal("7421", L1011Readouts.DecodeSquawk(0x7421));
    }
}
