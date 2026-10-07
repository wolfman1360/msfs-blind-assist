using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The glareshield windows as readable lines, from rows the agent returns. The first case is
/// the panel as captured live on the runway at CYYZ (tools/l1011-display-test/fixtures/afcs.html).</summary>
public class L1011AfcsWindowsTests
{
    private static IReadOnlyList<string> Rows(params string[] screens)
    {
        var rows = new List<string> { "power|on" };
        for (int i = 0; i < screens.Length; i++)
            rows.Add($"{i + 1}|{screens[i]}");
        return rows;
    }

    [Fact]
    public void The_captured_panel_reads_line_by_line()
    {
        var lines = L1011AfcsWindows.Format(Rows("100", "", "000", "000", "000", "00000", "AOA", "T/O"));
        Assert.Equal(new[]
        {
            "Speed: AOA 100",
            "Pitch: Takeoff",
            "Heading: 000",
            "Course 1: 000",
            "Course 2: 000",
            "Altitude: 0",
        }, lines);
    }

    [Fact]
    public void An_epr_target_shows_its_leading_one()
    {
        var lines = L1011AfcsWindows.Format(Rows("035", "", "090", "", "", "35000", "EPR", "ALT"));
        Assert.Equal("Speed: EPR 1.035", lines[0]);
        Assert.Equal("Altitude: 35000", lines[5]);
    }

    [Fact]
    public void A_mach_target_reads_as_mach()
    {
        var lines = L1011AfcsWindows.Format(Rows("820", "0.82", "", "", "", "", " M ", "M"));
        Assert.Equal("Speed: Mach .820", lines[0]);
        Assert.Equal("Pitch: Mach 0.82", lines[1]);
    }

    [Fact]
    public void The_gauge_blank_digit_padding_is_dropped()
    {
        // The gauge pads with "X" (drawn as nothing): "+X1500" is a climb of 1500, "XXX250" is 250 knots.
        var lines = L1011AfcsWindows.Format(Rows("X99", "+X1500", "090", "", "", "X5000", "IAS", "VS"));
        Assert.Equal("Speed: IAS 99", lines[0]);
        Assert.Equal("Pitch: VS +1500", lines[1]);
        Assert.Equal("Altitude: 5000", lines[5]);
        Assert.Equal("Pitch: IAS 250", L1011AfcsWindows.Format(Rows("250", "XXX250", "", "", "", "", "IAS", "IAS"))[1]);
    }

    [Fact]
    public void A_capture_reads_as_capture()
    {
        Assert.Equal("Pitch: Capture 35000", L1011AfcsWindows.Format(Rows("250", "X35000", "", "", "", "", "IAS", "CAP"))[1]);
    }

    [Fact]
    public void An_empty_window_reads_blank()
    {
        var lines = L1011AfcsWindows.Format(Rows("", "", "", "", "", "", "", ""));
        Assert.All(lines, l => Assert.EndsWith(": blank", l));
    }

    [Fact]
    public void An_unpowered_panel_is_one_line()
    {
        Assert.Equal(new[] { L1011AfcsWindows.Unpowered },
            L1011AfcsWindows.Format(new[] { "power|off", "1|100", "3|000" }));
    }

    [Fact]
    public void No_rows_still_gives_the_six_lines()
    {
        Assert.Equal(6, L1011AfcsWindows.Format(Array.Empty<string>()).Count);
    }
}
