using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The PMS pages and the INS waypoint table as lines. Cells are exactly as the gauge writes them:
/// the live captain PMS at a gate (2026-10-03) and pages replayed through the aircraft's own code.</summary>
public class L1011PmsDisplayTests
{
    private static L1011PmsScreen Pms(string title, string[] left, string[] right, bool entry = false, int line = 0) =>
        new("Captain", title, left, right, entry, line);

    private static readonly string[] Page1Left = { "GMT", "EPR", "PR ALT", "GW", "FUEL", "SAT" };

    [Fact]
    public void The_live_takeoff_page()
    {
        var lines = L1011PmsDisplay.Lines(Pms("TAKEOFF", Page1Left, new[] { "0755Z", "---", "1969", "---", "---", "---" }));
        Assert.Equal(new[]
        {
            "Captain PMS, page: TAKEOFF",
            "1: GMT 0755Z",
            "2: EPR blank",
            "3: PR ALT 1969",
            "4: GW blank",
            "5: FUEL blank",
            "6: SAT blank",
            "Lights: none",
        }, lines);
    }

    [Fact]
    public void Underscore_padding_reads_as_one_space()
    {
        var lines = L1011PmsDisplay.Lines(Pms("3ENG CRUISE STD",
            new[] { "CRZ______0.830/285", "FUEL REM", "ALT_430____OPT 350", "STEP", "TOD____000NM 0000Z", "SAT_-45____TAS 480" },
            new[] { "", "157.3", "", "----Z", "", "" }))!;
        Assert.Equal("1: CRZ 0.830/285", lines[1]);
        Assert.Equal("2: FUEL REM 157.3", lines[2]);
        Assert.Equal("3: ALT 430 OPT 350", lines[3]);
        Assert.Equal("4: STEP ----Z", lines[4]);
        Assert.Equal("6: SAT -45 TAS 480", lines[6]);
    }

    [Fact]
    public void A_lone_underscore_is_an_empty_value_and_an_empty_row_is_blank()
    {
        var lines = L1011PmsDisplay.Lines(Pms("FLIGHT SETUP",
            new[] { "CLB OPTIMUM/250", "3 ENG CRZ STD", "DES OPTIMUM/250", "BOD DIS", "BOD ALT", "" },
            new[] { "_", "_", "_", "0", "0", "" }))!;
        Assert.Equal("1: CLB OPTIMUM/250", lines[1]);
        Assert.Equal("4: BOD DIS 0", lines[4]);
        Assert.Equal("6: blank", lines[6]);
    }

    [Fact]
    public void An_open_entry_shows_what_has_been_typed()
    {
        var right = new[] { "12Z", "---", "1969", "---", "---", "---" };
        Assert.Equal("1: GMT, entering: 12Z", L1011PmsDisplay.Lines(Pms("TAKEOFF", Page1Left, right, entry: true, line: 1))![1]);
        var empty = new[] { "_Z", "---", "1969", "---", "---", "---" };
        Assert.Equal("1: GMT, entering: nothing typed yet", L1011PmsDisplay.Lines(Pms("TAKEOFF", Page1Left, empty, entry: true, line: 1))![1]);
    }

    [Fact]
    public void The_climb_page_alt_entry_is_drawn_inside_the_left_cell()
    {
        // Pages 3, 4 and 5 draw the entry inside the LEFT cell, the right cell empty: the manual's "CLIMB PAGE
        // OPEN, ALT INSERT" step opens "ALT ---  OPT 350", and the digits replace the dashes ("ALT_430____OPT 350").
        string[] right = { "", "", "", "", "", "" };
        string[] prompt = { "CLB 250/320/820", "EPR   .522/.574", "ALT ---  OPT 350", "TOC__---NM ----Z", "CLB MANUAL ?", "SAT 019   TAS 006" };
        Assert.Equal("3: ALT --- OPT 350, entering", L1011PmsDisplay.Lines(Pms("CLB OPTIMUM", prompt, right, entry: true, line: 3))![3]);
        string[] typed = { "CLB 250/320/820", "EPR   .522/.574", "ALT_430____OPT 350", "TOC__---NM ----Z", "CLB MANUAL ?", "SAT 019   TAS 006" };
        Assert.Equal("3: ALT 430 OPT 350, entering", L1011PmsDisplay.Lines(Pms("CLB OPTIMUM", typed, right, entry: true, line: 3))![3]);
        Assert.Equal("3: ALT 430 OPT 350", L1011PmsDisplay.Lines(Pms("CLB OPTIMUM", typed, right))![3]);   // closed: a plain row
    }

    [Fact]
    public void A_page_being_redrawn_is_null_so_the_window_keeps_the_last_page()
    {
        var blank = new[] { "", "", "", "", "", "" };
        Assert.Null(L1011PmsDisplay.Lines(Pms("", blank, blank)));
    }

    [Fact]
    public void A_page_with_cells_but_no_title_says_so()
    {
        var lines = L1011PmsDisplay.Lines(Pms("", new[] { "GMT", "", "", "", "", "" }, new[] { "0755Z", "", "", "", "", "" }))!;
        Assert.Equal("Captain PMS, page: no title", lines[0]);
    }

    [Fact]
    public void The_pms_mode_lights_close_the_page()
    {
        var screen = Pms("3ENG CRUISE STD", Page1Left, new[] { "", "", "", "", "", "" }) with
        {
            Lights = new[] { "CLIMB", "CLIMB_1", "INSERT" },
        };
        Assert.Equal("Lights: climb, climb 1, insert", L1011PmsDisplay.Lines(screen)![7]);
        Assert.Equal("Lights: cruise, descent, climb 2",
            L1011PmsDisplay.Lines(screen with { Lights = new[] { "CRUISE", "DESCENT", "CLIMB_2" } })![7]);
    }

    [Fact]
    public void An_unknown_light_name_is_left_out()
    {
        var screen = Pms("TAKEOFF", Page1Left, new[] { "", "", "", "", "", "" }) with { Lights = new[] { "ODD" } };
        Assert.Equal("Lights: none", L1011PmsDisplay.Lines(screen)![7]);
    }
}
