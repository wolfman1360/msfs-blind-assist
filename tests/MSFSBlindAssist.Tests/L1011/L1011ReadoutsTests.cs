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
            Assert.Equal("1.42", L1011Readouts.FormatValue(epr, 1.4214));
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

    [Fact]
    public void The_squawk_is_decoded_from_bcd()
    {
        Assert.Equal("2000", L1011Readouts.DecodeSquawk(0x2000));
        Assert.Equal("7421", L1011Readouts.DecodeSquawk(0x7421));
    }
}
