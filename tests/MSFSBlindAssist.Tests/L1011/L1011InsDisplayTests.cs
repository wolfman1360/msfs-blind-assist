using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// The INS windows as lines for speech and braille. Window strings are exactly what the gauge puts in
/// its one-character spans (a blank slot is a space): the live captures (2026-10-02 and 2026-10-03) and
/// values replayed through the aircraft's own display code.
/// </summary>
public class L1011InsDisplayTests
{
    // Lines: [0] the unit's state, [1] the overhead mode selector, [2] the display selector, [3..] the page's
    // values (or the open entry), the leg, and last the lights.
    private static L1011InsScreen Screen(int mode, string left, string right, string fromTo = "01", string dots = "",
        double? status = 1, string entry = "", int wheel = 0, string lights = "", bool powered = true, int? msu = null) =>
        new(1, powered, mode, wheel, left, right, fromTo,
            dots.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet(),
            status, entry, lights.Split(',', StringSplitOptions.RemoveEmptyEntries), msu);

    [Fact]
    public void Position_while_navigating()
    {
        var lines = L1011InsDisplay.Lines(Screen(3, "36051N", "115080W", dots: "1,2,3,4,5,6", lights: "READY", msu: 3));
        Assert.Equal(new[]
        {
            "INS 1: navigating",
            "Mode selector: nav",
            "Display selector: position",
            "Present position: N 36 05.1, W 115 08.0",
            "Leg: 0 to 1",
            "Lights: ready to navigate",
        }, lines);
    }

    [Theory]
    [InlineData(0, "off")]
    [InlineData(1, "standby")]
    [InlineData(2, "align")]
    [InlineData(3, "nav")]
    [InlineData(4, "attitude")]
    [InlineData(null, "unknown")]
    public void The_mode_selector_line_follows_the_overhead_msu(int? msu, string word)
    {
        // The overhead MSU (L:ROTARY_INS_MODE_n) is not in the INS view; the window fills it from the Overhead
        // row's reported position, and the manual's "All INS NAV before movement" step reads it here.
        Assert.Equal("Mode selector: " + word, L1011InsDisplay.Lines(Screen(3, "00000N", "072  0 ", status: 90, msu: msu))[1]);
    }

    [Fact]
    public void The_second_live_capture_reads_the_same_way()
    {
        Assert.Equal("Present position: N 43 40.3, W 079 37.0",
            L1011InsDisplay.Lines(Screen(3, "43403N", "079370W"))[3]);
    }

    [Theory]
    [InlineData("00000N", "072  0 ", "Present position: not entered")]
    [InlineData("36051N", "000000E", "Present position: N 36 05.1, longitude not entered")]
    [InlineData("00000N", "115080W", "Present position: latitude not entered, W 115 08.0")]
    public void A_position_not_yet_entered_says_so(string left, string right, string expected)
    {
        Assert.Equal(expected, L1011InsDisplay.Lines(Screen(3, left, right, status: 90))[3]);
    }

    [Fact]
    public void Track_and_ground_speed()
    {
        var lines = L1011InsDisplay.Lines(Screen(0, " 0874 ", "  0123 ", dots: "2,5"));
        Assert.Equal("Track: 087.4", lines[3]);
        Assert.Equal("Ground speed: 123 knots", lines[4]);
    }

    [Fact]
    public void One_mile_and_one_knot_read_in_the_singular()
    {
        Assert.Equal("Distance: 1 mile", L1011InsDisplay.Lines(Screen(5, "  0001", "  0124 ", dots: "6"))[3]);
        Assert.Equal("Ground speed: 1 knot", L1011InsDisplay.Lines(Screen(0, " 0874 ", "  0001 ", dots: "2,5"))[4]);
        Assert.Equal("Distance: 0 miles", L1011InsDisplay.Lines(Screen(5, "  0000", "  0124 ", dots: "6"))[3]);
    }

    [Theory]
    [InlineData(0, "Track: no data", "Ground speed: no data")]
    [InlineData(5, "Distance: no data", "Time: no data")]
    [InlineData(7, "Desired track: no data", "Status: no data")]
    public void A_freshly_powered_units_placeholder_reads_as_no_data(int mode, string first, string second)
    {
        // Live (2026-10-16): INS 2 powered by its MSU with its display selector never turned since the flight
        // loaded; the spans still held the gauge's power-up text "00000N" and "072  0 " and the window read
        // "Track: no data | Ground speed: 720 knots". The same pair would read "Time: 72.0 minutes" on Distance
        // and time and "Status: 0720, aligning" on Desired track and status.
        var lines = L1011InsDisplay.Lines(Screen(mode, "00000N", "072  0 ", status: 90));
        Assert.Equal(first, lines[3]);
        Assert.Equal(second, lines[4]);
    }

    [Fact]
    public void A_window_whose_digits_are_separated_by_a_blank_is_not_a_number()
    {
        Assert.Equal("Track: no data", L1011InsDisplay.Lines(Screen(0, " 08 74", "  0123 ", dots: "2,5"))[3]);
        Assert.Equal("Ground speed: no data", L1011InsDisplay.Lines(Screen(0, " 0874 ", "  01 23", dots: "2,5"))[4]);
        Assert.Equal("Heading: no data", L1011InsDisplay.Lines(Screen(1, "0 874 ", "R 0080 ", dots: "2,4,5,6"))[3]);
        Assert.Equal("Drift angle: no data", L1011InsDisplay.Lines(Screen(1, " 0874 ", "R 00 80", dots: "2,4,5,6"))[4]);
        Assert.Equal("Cross track: no data", L1011InsDisplay.Lines(Screen(2, "R01 23", "L 0045 ", dots: "4,5,6"))[3]);
        Assert.Equal("Track error: no data", L1011InsDisplay.Lines(Screen(2, "R0123 ", "L 0 045", dots: "4,5,6"))[4]);
        Assert.Equal("Distance: no data", L1011InsDisplay.Lines(Screen(5, " 00 35", "  0124 ", dots: "6"))[3]);
        Assert.Equal("Time: no data", L1011InsDisplay.Lines(Screen(5, "  0035", " 01 24 ", dots: "6"))[4]);
        Assert.Equal("Status: no data", L1011InsDisplay.Lines(Screen(7, " 0875 ", "   4 5 ", dots: "2,5", status: 44.2))[4]);
        Assert.Equal("Desired track: no data", L1011InsDisplay.Lines(Screen(7, " 08 75", "    45 ", dots: "2,5", status: 44.2))[3]);
        Assert.Equal("Wind: no data", L1011InsDisplay.Lines(Screen(6, "  270 ", "  01 5 ", dots: "2"))[3]);
        Assert.Equal("Wind: no data", L1011InsDisplay.Lines(Screen(6, " 2 70 ", "   015 ", dots: "2"))[3]);
    }

    [Fact]
    public void Blanks_around_a_number_and_after_a_side_letter_still_read_as_before()
    {
        // The live shapes: leading and trailing blanks, a side letter then a blank ("R 0123 "), the 8888 and ---- fills.
        Assert.Equal("Cross track: right 12.3 miles", L1011InsDisplay.Lines(Screen(2, "R 0123 ", "L 0045 ", dots: "4,5,6"))[3]);
        Assert.Equal("Track error: left 4.5 degrees", L1011InsDisplay.Lines(Screen(2, "R0123 ", "L 0045 ", dots: "4,5,6"))[4]);
        Assert.Equal("Ground speed: 123 knots", L1011InsDisplay.Lines(Screen(0, " 0874 ", "  0123 ", dots: "2,5"))[4]);
        Assert.Equal("Ground speed: 123 knots", L1011InsDisplay.Lines(Screen(0, " 0874 ", "0123", dots: "2,5"))[4]);
        Assert.Equal("Status: 45, aligning", L1011InsDisplay.Lines(Screen(7, " 0875 ", "    45 ", dots: "2,5", status: 44.2))[4]);
        Assert.Equal("Drift angle: no data", L1011InsDisplay.Lines(Screen(1, " 8888 ", "R 8888 ", status: 90))[4]);
        Assert.Equal("Time: no data", L1011InsDisplay.Lines(Screen(5, "  ----", "   ----"))[4]);
    }

    [Fact]
    public void Eights_are_no_data_while_the_unit_is_not_navigating()
    {
        var lines = L1011InsDisplay.Lines(Screen(0, " 8888 ", "  8888 ", dots: "2,5", status: 90, fromTo: "00"));
        Assert.Equal("INS 1: not aligned", lines[0]);
        Assert.Equal("Track: no data", lines[3]);
        Assert.Equal("Ground speed: no data", lines[4]);
        Assert.Equal("Drift angle: no data", L1011InsDisplay.Lines(Screen(1, " 8888 ", "R 8888 ", status: 90))[4]);
    }

    [Fact]
    public void Heading_and_drift_keep_the_aircraft_number_and_side()
    {
        var lines = L1011InsDisplay.Lines(Screen(1, " 0874 ", "R 0080 ", dots: "2,4,5,6"));
        Assert.Equal("Heading: 087.4", lines[3]);
        Assert.Equal("Drift angle: right 8.0 degrees", lines[4]);
        // The aircraft does not wrap the angle: desired 010 with heading 350 reads L 340.0.
        Assert.Equal("Drift angle: left 340.0 degrees", L1011InsDisplay.Lines(Screen(1, " 3500 ", "L 3400 ", dots: "2,4,5,6"))[4]);
    }

    [Fact]
    public void Cross_track_and_track_error()
    {
        var lines = L1011InsDisplay.Lines(Screen(2, "R0123 ", "L 0045 ", dots: "4,5,6"));
        Assert.Equal("Cross track: right 12.3 miles", lines[3]);
        Assert.Equal("Track error: left 4.5 degrees", lines[4]);
        Assert.Equal("Cross track: right 0.0 miles", L1011InsDisplay.Lines(Screen(2, "R0000 ", "R 0000 ", dots: "4,5,6"))[3]);
    }

    [Fact]
    public void Waypoints_by_thumbwheel()
    {
        Assert.Equal("Waypoint 1: N 43 40.3, W 079 37.0", L1011InsDisplay.Lines(Screen(4, "43403N", "079370W", wheel: 1))[3]);
        Assert.Equal("Waypoint 2: not set", L1011InsDisplay.Lines(Screen(4, "00000N", "000000E", wheel: 2))[3]);
        Assert.Equal("Waypoint 0, present position: N 36 05.1, W 115 08.0",
            L1011InsDisplay.Lines(Screen(4, "36051N", "115080W", wheel: 0))[3]);
    }

    [Fact]
    public void Distance_and_time()
    {
        var lines = L1011InsDisplay.Lines(Screen(5, "  0035", "  0124 ", dots: "6"));
        Assert.Equal("Distance: 35 miles", lines[3]);
        Assert.Equal("Time: 12.4 minutes", lines[4]);
        var none = L1011InsDisplay.Lines(Screen(5, "  ----", "   ----"));
        Assert.Equal("Distance: no data", none[3]);
        Assert.Equal("Time: no data", none[4]);
    }

    [Fact]
    public void Wind_reads_as_one_line()
    {
        Assert.Equal("Wind: 270 degrees at 15 knots", L1011InsDisplay.Lines(Screen(6, "  270 ", "   015 ", dots: "2"))[3]);
    }

    [Theory]
    [InlineData("    01 ", 1, "INS 1: navigating", "Status: 01, navigating")]
    [InlineData("    02 ", 2, "INS 1: aligned, ready to navigate", "Status: 02, aligned")]
    [InlineData("    90 ", 90, "INS 1: not aligned", "Status: 90, not aligned")]
    [InlineData("    45 ", 44.2, "INS 1: aligning, status 45", "Status: 45, aligning")]
    public void Desired_track_and_status(string right, double status, string stateLine, string statusLine)
    {
        var lines = L1011InsDisplay.Lines(Screen(7, " 0875 ", right, dots: "2,5", status: status));
        Assert.Equal(stateLine, lines[0]);
        Assert.Equal("Desired track: 087.5", lines[3]);
        Assert.Equal(statusLine, lines[4]);
    }

    [Fact]
    public void A_status_a_hair_above_two_reads_as_aligned()
    {
        // Live (2026-10-16): the status lands on 2.0000000000000053 for about a second before it is exactly 2.
        // The state line must say aligned the way it does for 2, and the status window shows 02 either way.
        var lines = L1011InsDisplay.Lines(Screen(7, " 0875 ", "    02 ", dots: "2,5", status: 2.0000000000000053));
        Assert.Equal("INS 1: aligned, ready to navigate", lines[0]);
        Assert.Equal("Status: 02, aligned", lines[4]);
        Assert.Equal("aligned, ready to navigate", L1011InsDisplay.StateText(2.0000000000000053));
        Assert.Equal("navigating", L1011InsDisplay.StateText(1.0000000000000002));
        Assert.Equal("not aligned", L1011InsDisplay.StateText(90.00000000000001));
        Assert.Equal("aligning, status 17", L1011InsDisplay.StateText(16.666666666666668));   // a real fraction still rounds up
        Assert.Equal("aligning, status 3", L1011InsDisplay.StateText(2.01));
    }

    [Fact]
    public void The_test_position_and_its_leftover_88()
    {
        var lines = L1011InsDisplay.Lines(Screen(8, "88888X", "R88888Y", fromTo: "88", dots: "1,2,3,4,5,6"));
        Assert.Equal("Display selector: test", lines[2]);
        Assert.Equal("Test: every segment lit", lines[3]);
        Assert.Equal("Leg: test pattern", lines[4]);
    }

    [Fact]
    public void An_open_entry_shows_what_is_being_typed()
    {
        var lines = L1011InsDisplay.Lines(Screen(3, "   36N", "072  0 ", status: 90, entry: "left"));
        Assert.Equal("Entry, left window: 36N", lines[3]);
        Assert.Equal("Entry, right window: 11508W",
            L1011InsDisplay.Lines(Screen(3, "36051N", " 11508W", status: 90, entry: "right"))[3]);
        Assert.Equal("Entry, leg: 01", L1011InsDisplay.Lines(Screen(0, " 0003 ", "  0000 ", fromTo: "01", entry: "leg"))[3]);
    }

    [Fact]
    public void An_open_offset_entry_keeps_the_decimal_point_the_gauge_draws()
    {
        // The manual keys a right 5 nm parallel offset on the cross track page as 1, 9, 5, 0; the gauge shows
        // "R  50 " with #dot_5 lit, so the cockpit reads R 5.0. Collapsed, "R50" would be heard as fifty miles.
        Assert.Equal("Entry, left window: R 5.0", L1011InsDisplay.Lines(Screen(2, "R  50 ", "R 0000 ", dots: "4,5,6", entry: "left"))[3]);
        Assert.Equal("Entry, left window: R", L1011InsDisplay.Lines(Screen(2, "R     ", "R 0000 ", dots: "4,5,6", entry: "left"))[3]);
        // A position entry keeps its keyed digits: the dot there is a minute tick, not a decimal point.
        Assert.Equal("Entry, left window: 36N", L1011InsDisplay.Lines(Screen(3, "   36N", "072  0 ", dots: "1,2,3,4,5,6", status: 90, entry: "left"))[3]);
    }

    [Fact]
    public void Lights_are_named_in_words()
    {
        Assert.Equal("Lights: alert, insert, battery", L1011InsDisplay.Lines(Screen(3, "36051N", "115080W", lights: "ALERT,INSERT,BAT"))[^1]);
        Assert.Equal("Lights: none", L1011InsDisplay.Lines(Screen(3, "36051N", "115080W"))[^1]);
    }

    [Fact]
    public void An_unpowered_unit_is_one_line()
    {
        Assert.Equal(new[] { "INS 1: off" }, L1011InsDisplay.Lines(Screen(3, "36051N", "115080W", powered: false)));
        Assert.Equal(new[] { "INS 1: off" }, L1011InsDisplay.Lines(Screen(3, "36051N", "115080W", powered: false, msu: 1)));
    }

    [Fact]
    public void An_unpowered_unit_whose_msu_reports_attitude_is_not_off()
    {
        // In ATT the unit is on (attitude only) but its display is dark, so the reader sees no power.
        Assert.Equal(new[] { "INS 1: attitude mode" }, L1011InsDisplay.Lines(Screen(3, "36051N", "115080W", powered: false, msu: 4)));
    }

    [Fact]
    public void A_comma_decimal_culture_changes_nothing()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Mode selector: nav", L1011InsDisplay.Lines(Screen(3, "36051N", "115080W", msu: 3))[1]);
            Assert.Equal("Present position: N 36 05.1, W 115 08.0", L1011InsDisplay.Lines(Screen(3, "36051N", "115080W"))[3]);
            Assert.Equal("Time: 12.4 minutes", L1011InsDisplay.Lines(Screen(5, "  0035", "  0124 ", dots: "6"))[4]);
            Assert.Equal("Drift angle: right 8.0 degrees", L1011InsDisplay.Lines(Screen(1, " 0874 ", "R 0080 "))[4]);
            Assert.Equal("Entry, left window: R 5.0", L1011InsDisplay.Lines(Screen(2, "R  50 ", "R 0000 ", dots: "4,5,6", entry: "left"))[3]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
