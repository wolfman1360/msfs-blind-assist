using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300PfdTextTests
{
    private const double Degree = Math.PI / 180;

    [Theory]
    [InlineData(-5.0, "5.0 degrees up")]   // SimConnect pitch is positive nose DOWN
    [InlineData(2.5, "2.5 degrees down")]
    [InlineData(0.3, "Level")]
    public void Pitch_reads_up_or_down(double degrees, string text)
    {
        Assert.Equal(text, A300PfdText.Pitch(degrees * Degree));
    }

    [Theory]
    [InlineData(10.0, "10.0 degrees left")]   // SimConnect bank is positive LEFT
    [InlineData(-25.0, "25.0 degrees right")]
    [InlineData(-0.2, "Wings level")]
    public void Bank_reads_left_or_right(double degrees, string text)
    {
        Assert.Equal(text, A300PfdText.Bank(degrees * Degree));
    }

    [Theory]
    [InlineData(1520, "1,520 feet per minute up")]
    [InlineData(-804, "800 feet per minute down")]
    [InlineData(30, "Level")]
    [InlineData(-49, "Level")]
    public void Vertical_speed_reads_in_tens_with_its_direction(double fpm, string text)
    {
        Assert.Equal(text, A300PfdText.VerticalSpeed(fpm));
    }

    [Theory]
    [InlineData(179.76, "180 knots")]
    [InlineData(0, "not available")]
    [InlineData(-1, "not available")]
    public void A_speed_reads_whole_knots_or_not_available(double knots, string text)
    {
        Assert.Equal(text, A300PfdText.Speed(knots));
    }

    [Theory]
    [InlineData(A300PfdSpeed.GreenDot, 0, "187 knots")]
    [InlineData(A300PfdSpeed.GreenDot, 1, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.S, 1, "187 knots")]
    [InlineData(A300PfdSpeed.S, 0, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.S, 2, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.F, 2, "187 knots")]
    [InlineData(A300PfdSpeed.F, 3, "187 knots")]
    [InlineData(A300PfdSpeed.F, 1, "not shown at this flap setting")]
    [InlineData(A300PfdSpeed.F, 4, "not shown at this flap setting")]
    public void Green_dot_s_and_f_read_only_at_the_flap_lever_positions_the_speed_tape_shows_them(A300PfdSpeed speed, double flapLever, string text)
    {
        // PFD::drawSpeedTape: green dot at lever 0, S at lever 1, F at levers 2 and 3.
        Assert.Equal(text, A300PfdText.FlapSpeed(speed, 187.4, flapLever));
    }

    [Fact]
    public void A_flap_speed_with_no_flap_lever_known_reads_not_available()
    {
        Assert.Equal("not available", A300PfdText.FlapSpeed(A300PfdSpeed.GreenDot, 187.4, null));
    }

    [Theory]
    [InlineData(200, "200 feet")]
    [InlineData(0, "not set")]
    public void Minimums_read_in_feet_or_not_set(double feet, string text)
    {
        Assert.Equal(text, A300PfdText.Minimums(feet));
    }

    [Theory]
    [InlineData(12000.4, "12,000 feet")]
    [InlineData(-30, "-30 feet")]
    public void Altitudes_read_with_a_thousands_separator(double feet, string text)
    {
        Assert.Equal(text, A300PfdText.Feet(feet));
    }

    [Fact]
    public void Heading_reads_three_digits_from_radians_and_wraps()
    {
        Assert.Equal("000", A300PfdText.HeadingFromRadians(2 * Math.PI));
        Assert.Equal("090", A300PfdText.HeadingFromRadians(Math.PI / 2));
        Assert.Equal("270", A300PfdText.HeadingFromRadians(-Math.PI / 2));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Numbers_read_the_same_in_a_comma_decimal_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("5.0 degrees up", A300PfdText.Pitch(-5 * Degree));
            Assert.Equal("10.5 degrees left", A300PfdText.Bank(10.5 * Degree));
            Assert.Equal("1,520 feet per minute up", A300PfdText.VerticalSpeed(1520));
            Assert.Equal("12,000 feet", A300PfdText.Feet(12000));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
