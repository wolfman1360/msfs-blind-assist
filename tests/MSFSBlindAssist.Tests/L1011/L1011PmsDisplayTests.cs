using System.Globalization;
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

    // The PMS line keys name the line they press and their shortcut, as the PMDG CDU window's buttons do
    // ("Init Ref (Alt+I)"), from the line's label words only: a value changes every second (GMT, PR ALT), and
    // a button whose name keeps changing is read again and again.
    [Fact]
    public void A_line_key_names_its_line_and_its_shortcut()
    {
        var page = Pms("TAKEOFF", Page1Left, new[] { "2147Z", ".632", "-482", "412", "116.6", "---" });
        Assert.Equal(new[]
        {
            "Line 1, GMT (Ctrl+1)", "Line 2, EPR (Ctrl+2)", "Line 3, PR ALT (Ctrl+3)",
            "Line 4, GW (Ctrl+4)", "Line 5, FUEL (Ctrl+5)", "Line 6, SAT (Ctrl+6)",
        }, Enumerable.Range(1, 6).Select(n => L1011PmsDisplay.LineKeyLabel(page, n, alternateKeys: false)));
        Assert.Equal("Line 1, GMT (F1)", L1011PmsDisplay.LineKeyLabel(page, 1, alternateKeys: true));
    }

    [Fact]
    public void A_line_key_takes_the_label_words_and_leaves_the_values_out()
    {
        var climb = Pms("CLB OPTIMUM",
            new[] { "CLB 250/320/820", "EPR   .716/.788", "ALT 430  OPT 350", "TOC  ---NM ----Z", "CLB MANUAL ?", "SAT -45   TAS 480" },
            new[] { "", "", "", "", "", "" });
        Assert.Equal(new[] { "CLB", "EPR", "ALT", "TOC", "CLB MANUAL", "SAT" },
            Enumerable.Range(1, 6).Select(n => L1011PmsDisplay.LineKeyLabel(climb, n, false).Split(" (")[0].Replace($"Line {n}, ", "")));
        var cruise = Pms("3ENG CRUISE STD",
            new[] { "CRZ______0.830/285", "FUEL REM", "ALT_430____OPT 350", "STEP", "TOD____000NM 0000Z", "SAT_-45____TAS 480" },
            new[] { "", "157.3", "", "----Z", "", "" });
        Assert.Equal("Line 1, CRZ (Ctrl+1)", L1011PmsDisplay.LineKeyLabel(cruise, 1, false));
        Assert.Equal("Line 2, FUEL REM (Ctrl+2)", L1011PmsDisplay.LineKeyLabel(cruise, 2, false));
    }

    [Fact]
    public void A_line_with_no_label_words_is_named_by_its_number_alone()
    {
        var setup = Pms("FLIGHT SETUP",
            new[] { "CLB OPTIMUM/250", "3 ENG CRZ STD", "DES OPTIMUM/250", "BOD DIS", "---", "" },
            new[] { "_", "_", "_", "0", "0", "" });
        Assert.Equal("Line 1, CLB (Ctrl+1)", L1011PmsDisplay.LineKeyLabel(setup, 1, false));
        Assert.Equal("Line 2, 3 ENG CRZ STD (Ctrl+2)", L1011PmsDisplay.LineKeyLabel(setup, 2, false));   // a label that starts with a digit
        Assert.Equal("Line 5 (Ctrl+5)", L1011PmsDisplay.LineKeyLabel(setup, 5, false));
        Assert.Equal("Line 6 (Ctrl+6)", L1011PmsDisplay.LineKeyLabel(setup, 6, false));
        Assert.Equal("Line 3 (Ctrl+3)", L1011PmsDisplay.LineKeyLabel(null, 3, false));
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
    public void An_epr_reads_with_its_leading_one_as_the_tablet_reader_says_it()
    {
        // The gauge stores epr / 1000 + 1 and draws the thousandths only (".642" is EPR 1.642); the tablet
        // reader already restores the 1 ("Takeoff EPR: 1.643").
        var takeoff = L1011PmsDisplay.Lines(Pms("TAKEOFF", Page1Left, new[] { "0755Z", ".642", "1969", "418", "122.6", "19" }))!;
        Assert.Equal("2: EPR 1.642", takeoff[2]);
        Assert.Equal("5: FUEL 122.6", takeoff[5]);   // only the EPR row
        string[] climb = { "CLB 250/320/820", "EPR   .522/.574", "ALT_430____OPT 350", "TOC__059NM 1632Z", "CLB MANUAL ?", "SAT 019   TAS 006" };
        Assert.Equal("2: EPR 1.522/1.574", L1011PmsDisplay.Lines(Pms("CLB OPTIMUM", climb, new[] { "", "", "", "", "", "" }))![2]);
    }

    [Fact]
    public void An_epr_being_typed_is_shown_as_typed()
    {
        var right = new[] { "0755Z", ".6_", "1969", "---", "---", "---" };
        Assert.Equal("2: EPR, entering: .6", L1011PmsDisplay.Lines(Pms("TAKEOFF", Page1Left, right, entry: true, line: 2))![2]);
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
    public void A_comma_decimal_culture_changes_nothing()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var cruise = L1011PmsDisplay.Lines(Pms("3ENG CRUISE STD",
                new[] { "CRZ______0.830/285", "FUEL REM", "ALT_430____OPT 350", "STEP", "TOD____000NM 0000Z", "SAT_-45____TAS 480" },
                new[] { "", "157.3", "", "----Z", "", "" }))!;
            Assert.Equal("1: CRZ 0.830/285", cruise[1]);
            Assert.Equal("2: FUEL REM 157.3", cruise[2]);
            Assert.Equal("6: SAT -45 TAS 480", cruise[6]);

            var entry = L1011PmsDisplay.Lines(Pms("TAKEOFF", Page1Left, new[] { "12Z", "---", "1969", "---", "---", "---" },
                entry: true, line: 1))!;
            Assert.Equal("1: GMT, entering: 12Z", entry[1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void An_unknown_light_name_is_left_out()
    {
        var screen = Pms("TAKEOFF", Page1Left, new[] { "", "", "", "", "", "" }) with { Lights = new[] { "ODD" } };
        Assert.Equal("Lights: none", L1011PmsDisplay.Lines(screen)![7]);
    }
}
