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

    [Fact]
    public void The_autobrake_panel_reads_the_brake_gauge()
    {
        // The triple brake indicator's needles (ACCU PRESS 0 to 4, BRAKES 0 to 3, PSI x 1000).
        var brakes = A300Readouts.All.Where(r => r.Panel == "Autobrake").ToArray();
        Assert.Equal(new[]
        {
            ("Brake accumulator pressure", "INI_BRAKE_PRESSURE_ACCU_PRESS"),
            ("Left brake pressure", "INI_BRAKE_PRESSURE_LEFT"),
            ("Right brake pressure", "INI_BRAKE_PRESSURE_RIGHT"),
        }, brakes.Select(r => (r.Name, r.Var)).ToArray());
        Assert.Equal("3,000 psi", brakes[0].Format(2.9999992847442627));
        Assert.Equal("2,300 psi", brakes[1].Format(2.2999987602233887));
    }

    [Theory]
    // The tilt knob's slider runs 0 to 100; the antenna tilt it sets, INI_WXR_TILT, runs 15 down to 15 up
    // (knob 40 is 3 down, measured 2026-10-10). Up is positive.
    [InlineData(-3, "3 degrees down")]
    [InlineData(15, "15 degrees up")]
    [InlineData(0, "0 degrees")]
    [InlineData(-0.4, "0 degrees")]
    public void The_weather_radar_panel_reads_its_antenna_tilt(double tilt, string text)
    {
        var readout = A300Readouts.All.Single(r => r.Panel == "Weather Radar");
        Assert.Equal(("Antenna tilt", "INI_WXR_TILT"), (readout.Name, readout.Var));
        Assert.Equal(text, readout.Format(tilt));
    }

    [Theory]
    // TRANSPONDER CODE:1 read as BCO16, one digit a nibble, as the FBW A320 and A380 read it.
    [InlineData(0x7000, "7000")]
    [InlineData(0x1200, "1200")]
    [InlineData(0x0042, "0042")]
    public void The_transponder_panel_reads_the_squawk_code(int bcd, string code)
    {
        var readout = A300Readouts.All.Single(r => r.Panel == "Transponder");
        Assert.Equal(("Squawk code", "TRANSPONDER CODE:1", "BCO16"), (readout.Name, readout.Var, readout.Units));
        Assert.Equal(code, readout.Format(bcd));
    }

    [Theory]
    [InlineData("A300_TCAS_BIG")]
    [InlineData("A300_TCAS_SMALL")]
    public void A_transponder_code_knob_step_reads_the_code_back(string knob)
    {
        // A step changes two digits at a time (the large knob 7000 to 7100, the small 7000 to 7001, 2026-10-10).
        var (readout, phrase) = A300Readouts.KnobReadBacks[knob];
        Assert.Equal("Transponder", A300Readouts.All.Single(r => r.Key == readout).Panel);
        Assert.Equal("Squawk 7100", phrase(0x7100));
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
