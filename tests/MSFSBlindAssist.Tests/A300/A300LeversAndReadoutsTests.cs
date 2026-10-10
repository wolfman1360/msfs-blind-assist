using System.Globalization;
using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

public class A300LeversAndReadoutsTests
{
    [Theory]
    [InlineData(0, "0 (>K:FLAPS_SET)")]
    [InlineData(2, "8192 (>K:FLAPS_SET)")]
    [InlineData(4, "16384 (>K:FLAPS_SET)")]
    [InlineData(9, "16384 (>K:FLAPS_SET)")]
    [InlineData(-1, "0 (>K:FLAPS_SET)")]
    public void Flaps_go_to_a_detent_by_index(int index, string rpn) => Assert.Equal(rpn, A300Levers.FlapsRpn(index));

    [Theory]
    [InlineData(1, "1 (>K:SPOILERS_ARM_SET)")]
    [InlineData(0, "0 (>K:SPOILERS_ARM_SET)")]
    public void Ground_spoilers_arm_and_disarm(double value, string rpn) => Assert.Equal(rpn, A300Levers.SpoilersArmRpn(value));

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_speed_brake_is_written_in_whole_units_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("8192 (>K:SPOILERS_SET)", A300Levers.SpeedBrakeRpn(0.5));
            Assert.Equal("16384 (>K:SPOILERS_SET)", A300Levers.SpeedBrakeRpn(1.7));
            Assert.Equal("0 (>K:SPOILERS_SET)", A300Levers.SpeedBrakeRpn(-0.2));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    private static A300Readout Readout(string key) => A300Readouts.All.Single(r => r.Key == key);

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Readouts_read_the_same_in_any_culture(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1013 hectopascals, 29.91 inches", Readout("A300_RO_BARO_CPT").Format(1013));
            Assert.Equal("1013 hectopascals, 29.92 inches", A300Readouts.Altimeter(1013.25));
            Assert.Equal("35,000 feet", Readout("A300_RO_FCU_ALTITUDE").Format(35000));
            Assert.Equal("250 knots", Readout("A300_RO_FCU_SPEED").Format(250.4));
            Assert.Equal("-1500 feet per minute", Readout("A300_RO_FCU_VS").Format(-1500));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(0, "000")]
    [InlineData(360, "000")]
    [InlineData(5.9, "005")]
    [InlineData(-10, "350")]
    [InlineData(359.99, "359")]
    public void Headings_read_as_the_window_shows_them(double degrees, string text) =>
        Assert.Equal(text, A300Readouts.Heading(degrees));

    [Fact]
    public void A_negative_zero_reads_as_zero() => Assert.Equal("0 feet per minute", Readout("A300_RO_FCU_VS").Format(-0.3));

    [Fact]
    public void The_oxygen_panel_reads_its_six_gauges_named_from_what_they_measure()
    {
        // The needles' own animation code, on the overhead oxygen panel. The courier gauge's variable is
        // iniBuilds' "HIGH_PRESSURE_CURRENT", but it rises with the courier low pressure supply (live, 2026-10-09).
        Assert.Equal(new[]
        {
            ("Crew oxygen low pressure", "INI_OXYGEN_LOW_PRESSURE_CURRENT"),
            ("Courier oxygen low pressure", "INI_OXYGEN_HIGH_PRESSURE_CURRENT"),
            ("Crew oxygen high pressure 1", "INI_OXYGEN_HIGH_PRESSURE_CURRENT1"),
            ("Crew oxygen high pressure 2", "INI_OXYGEN_HIGH_PRESSURE_CURRENT2"),
            ("Crew oxygen high pressure 3", "INI_OXYGEN_HIGH_PRESSURE_CURRENT3"),
            ("Crew oxygen high pressure 4", "INI_OXYGEN_HIGH_PRESSURE_CURRENT4"),
        }, A300Readouts.All.Where(r => r.Panel == "Oxygen").Select(r => (r.Name, r.Var)).ToArray());
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void The_oxygen_gauges_read_in_psi_as_their_faces_show_them(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var oxygen = A300Readouts.All.Where(r => r.Panel == "Oxygen").ToArray();
            // The LP face reads PSI; the HP face reads "PSI x 1000", 0 to 2.
            Assert.Equal("70 psi", oxygen[0].Format(69.99998474121094));
            Assert.Equal("1,500 psi", oxygen[2].Format(1.5));
            Assert.Equal("1,850 psi", oxygen[5].Format(1.85));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
