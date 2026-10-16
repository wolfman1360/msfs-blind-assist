using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>What the INS window says on its own while it is open: the new page when a PMS changes page (the
/// pilot's FWD/BACK, or the aircraft's own PERF MGT press on a phase change), and the new leg when an INS
/// sequences to the next waypoint. The first read of a unit is a baseline.</summary>
public class L1011InsWindowAnnouncerTests
{
    private static readonly string[] Blank = { "", "", "", "", "", "" };

    private static L1011InsScrapeResult Read(string title = "TAKEOFF", string fromTo = "01", bool powered = true, string entry = "")
    {
        var ins = new L1011InsScreen(1, powered, 3, 0, "36051N", "115080W", fromTo, new HashSet<int>(), 1, entry, Array.Empty<string>());
        var pms = new L1011PmsScreen("Captain", title, Blank, Blank, false, 0);
        return new L1011InsScrapeResult(ins, pms, Array.Empty<L1011InsWaypoint?>(), 0, 0, null);
    }

    [Fact]
    public void A_pms_page_change_names_the_new_page()
    {
        var a = new L1011InsWindowAnnouncer();
        Assert.Null(a.Observe(L1011InsDevice.CaptainPms, Read("TAKEOFF")));
        Assert.Null(a.Observe(L1011InsDevice.CaptainPms, Read("TAKEOFF")));
        Assert.Equal("FLIGHT SETUP", a.Observe(L1011InsDevice.CaptainPms, Read("FLIGHT SETUP")));
        Assert.Equal("3ENG CRUISE STD", a.Observe(L1011InsDevice.CaptainPms, Read("3ENG____CRUISE STD")));
    }

    [Fact]
    public void A_pms_page_being_redrawn_is_not_a_change()
    {
        var a = new L1011InsWindowAnnouncer();
        a.Observe(L1011InsDevice.CaptainPms, Read("TAKEOFF"));
        Assert.Null(a.Observe(L1011InsDevice.CaptainPms, Read("")));
        Assert.Null(a.Observe(L1011InsDevice.CaptainPms, Read("TAKEOFF")));
    }

    [Fact]
    public void An_ins_leg_change_is_spoken_but_not_a_leg_being_typed_or_the_test_pattern()
    {
        var a = new L1011InsWindowAnnouncer();
        Assert.Null(a.Observe(L1011InsDevice.Ins1, Read(fromTo: "01")));
        Assert.Equal("Leg 1 to 2", a.Observe(L1011InsDevice.Ins1, Read(fromTo: "12")));
        Assert.Null(a.Observe(L1011InsDevice.Ins1, Read(fromTo: "00", entry: "leg")));
        Assert.Null(a.Observe(L1011InsDevice.Ins1, Read(fromTo: "88")));
        Assert.Equal("Leg 2 to 3", a.Observe(L1011InsDevice.Ins1, Read(fromTo: "23")));
        Assert.Null(a.Observe(L1011InsDevice.Ins1, Read(fromTo: "34", powered: false)));
    }

    [Fact]
    public void Switching_units_starts_a_new_baseline()
    {
        var a = new L1011InsWindowAnnouncer();
        a.Observe(L1011InsDevice.Ins1, Read(fromTo: "01"));
        Assert.Null(a.Observe(L1011InsDevice.Ins2, Read(fromTo: "45")));
        Assert.Null(a.Observe(L1011InsDevice.CaptainPms, Read("CLB OPTIMUM")));
        Assert.Null(a.Observe(L1011InsDevice.Ins2, Read(fromTo: "56")));   // back on INS 2: a baseline again
        Assert.Equal("Leg 6 to 7", a.Observe(L1011InsDevice.Ins2, Read(fromTo: "67")));
    }
}
