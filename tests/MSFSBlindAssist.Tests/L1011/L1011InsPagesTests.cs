using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>An INS's pages are its display selector's nine positions. Page Up and Page Down in the INS window
/// turn it one position, the way they change a PMS page; the knob stops at both ends, as the cockpit's does.</summary>
public class L1011InsPagesTests
{
    private static readonly IReadOnlyDictionary<double, string> Positions = new Dictionary<double, string>
    {
        [0] = "Track and ground speed", [1] = "Heading and drift angle", [2] = "Cross track and track error",
        [3] = "Position", [4] = "Waypoint", [5] = "Distance and time", [6] = "Wind", [7] = "Desired track and status",
        [8] = "Test",
    };

    [Fact]
    public void A_page_key_turns_the_selector_one_position()
    {
        Assert.Equal(new L1011InsPages.Step(4, null), L1011InsPages.Next(Positions, 3, +1));
        Assert.Equal(new L1011InsPages.Step(2, null), L1011InsPages.Next(Positions, 3, -1));
    }

    [Fact]
    public void At_either_end_the_knob_stops_and_says_where_it_is()
    {
        Assert.Equal(new L1011InsPages.Step(null, "Last position: Test"), L1011InsPages.Next(Positions, 8, +1));
        Assert.Equal(new L1011InsPages.Step(null, "First position: Track and ground speed"), L1011InsPages.Next(Positions, 0, -1));
    }

    [Fact]
    public void A_selector_whose_position_is_not_reported_is_not_turned()
    {
        Assert.Equal(new L1011InsPages.Step(null, "Display selector position unknown"), L1011InsPages.Next(Positions, null, +1));
        Assert.Equal(new L1011InsPages.Step(null, "Display selector position unknown"), L1011InsPages.Next(Positions, 3.5, +1));
    }

    [Fact]
    public void The_new_page_is_said_once_the_aircraft_reports_it()
    {
        // The screen reader cannot say it: the selector combo is not where the cursor is. Said from the
        // position the aircraft reports, never the one asked for ([L1011-11]), and only once.
        var readBack = new L1011InsPages.ReadBack();
        readBack.Arm(4, "Waypoint", nowMs: 1_000);
        Assert.Null(readBack.OnPoll(3, nowMs: 1_500));
        Assert.Equal("Waypoint", readBack.OnPoll(4, nowMs: 2_000));
        Assert.Null(readBack.OnPoll(4, nowMs: 2_500));
    }

    [Fact]
    public void A_page_the_aircraft_never_reports_is_not_said()
    {
        var readBack = new L1011InsPages.ReadBack();
        readBack.Arm(4, "Waypoint", nowMs: 1_000);
        Assert.Null(readBack.OnPoll(3, nowMs: 1_000 + L1011InsPages.ReadBack.WaitMs + 1));
        Assert.Null(readBack.OnPoll(4, nowMs: 1_000 + L1011InsPages.ReadBack.WaitMs + 2));
    }

    [Fact]
    public void A_second_page_key_replaces_the_first()
    {
        var readBack = new L1011InsPages.ReadBack();
        readBack.Arm(4, "Waypoint", nowMs: 1_000);
        readBack.Arm(5, "Distance and time", nowMs: 1_200);
        Assert.Null(readBack.OnPoll(4, nowMs: 1_400));
        Assert.Equal("Distance and time", readBack.OnPoll(5, nowMs: 1_900));
    }
}
