using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The rows coherent-l1011-ins-agent.js returns, turned back into the INS and PMS screens. The row
/// set is the agent's output for the live INS 1 capture (POS page, navigating, at a gate, 2026-10-03), the same rows
/// tools/l1011-display-test pins on the JavaScript side.</summary>
public class L1011InsScrapeTests
{
    private static readonly string[] Live =
    {
        "unit|1", "power|on", "mode|3", "wheel|0", "left|36051N", "right|115080W", "tk|00", "dots|123456",
        "status|1", "entry|", "lights|READY,HOLD", "auto_batch|1", "batch|1|1",
        "wpt_1|KENNO|37.2982|-117.3102", "wpt_2|RUSME|37.4942|-117.52", "wpt_3||0|0", "wpt_4||0|0", "wpt_5||0|0",
        "wpt_6||0|0", "wpt_7||0|0", "wpt_8||0|0", "wpt_9||0|0",
        "pms_seat|CPT", "pms_title|TAKEOFF",
        "pms_left_1|GMT", "pms_right_1|0755Z", "pms_left_2|EPR", "pms_right_2|---", "pms_left_3|PR ALT", "pms_right_3|1969",
        "pms_left_4|GW", "pms_right_4|---", "pms_left_5|FUEL", "pms_right_5|---", "pms_left_6|SAT", "pms_right_6|---",
        "pms_entry|0", "pms_line|0", "pms_lights|INSERT",
    };

    [Fact]
    public void The_live_ins_1_rows()
    {
        var r = L1011InsScrape.Parse(Live)!;
        Assert.Equal(1, r.Ins.Unit);
        Assert.True(r.Ins.Powered);
        Assert.Equal(3, r.Ins.Mode);
        Assert.Equal(0, r.Ins.Wheel);
        Assert.Equal("36051N", r.Ins.Left);
        Assert.Equal("115080W", r.Ins.Right);
        Assert.Equal("00", r.Ins.FromTo);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, r.Ins.Dots.OrderBy(d => d));
        Assert.Equal(1.0, r.Ins.Status);
        Assert.Equal("", r.Ins.Entry);
        Assert.Equal(new[] { "READY", "HOLD" }, r.Ins.Lights);
        Assert.Null(r.Ins.Msu);   // the overhead MSU is not in the view: the window fills it from its panel row
        Assert.Equal("Present position: N 36 05.1, W 115 08.0", L1011InsDisplay.Lines(r.Ins)[3]);

        Assert.True(r.AutoBatch);
        Assert.Equal(1, r.ActiveBatch);
        Assert.Equal(1, r.BatchCount);
        Assert.Equal(9, r.Waypoints.Count);
        Assert.Equal(new L1011InsWaypoint("KENNO", 37.2982, -117.3102), r.Waypoints[0]);
        Assert.Equal(new L1011InsWaypoint("", 0, 0), r.Waypoints[8]);

        var pms = r.Pms!;
        Assert.Equal("Captain", pms.Seat);
        Assert.Equal("TAKEOFF", pms.Title);
        Assert.Equal("0755Z", pms.Right[0]);
        Assert.Equal("PR ALT", pms.Left[2]);
        Assert.False(pms.EntryOpen);
        Assert.Equal(new[] { "INSERT" }, pms.Lights);
    }

    [Fact]
    public void The_first_officer_pms_rides_on_ins_2_and_ins_3_has_none()
    {
        var two = Live.Select(r => r == "unit|1" ? "unit|2" : r == "pms_seat|CPT" ? "pms_seat|FO" : r).ToArray();
        Assert.Equal("First officer", L1011InsScrape.Parse(two)!.Pms!.Seat);
        var three = Live.Select(r => r == "unit|1" ? "unit|3" : r == "pms_seat|CPT" ? "pms_seat|" : r).ToArray();
        Assert.Null(L1011InsScrape.Parse(three)!.Pms);
    }

    [Fact]
    public void Rows_from_a_page_that_is_not_an_ins_are_refused()
    {
        Assert.Null(L1011InsScrape.Parse(new[] { "power|on" }));
        Assert.Null(L1011InsScrape.Parse(new[] { "unit|7" }));
        Assert.Null(L1011InsScrape.Parse(Array.Empty<string>()));
    }

    [Fact]
    public void Blank_slots_survive_and_a_cell_may_hold_a_bar()
    {
        var rows = Live.Select(r => r == "left|36051N" ? "left|   43N" : r == "pms_left_1|GMT" ? "pms_left_1|A|B" : r).ToArray();
        var parsed = L1011InsScrape.Parse(rows)!;
        Assert.Equal("   43N", parsed.Ins.Left);
        Assert.Equal("A|B", parsed.Pms!.Left[0]);
    }

    [Fact]
    public void An_empty_slot_the_gauge_names_user_k_is_not_set()
    {
        // 2026-10-15, live: at load the gauge names every empty slot USER<k> at 0, 0 ("wpt_3|USER3|0|0"); a typed
        // waypoint reads "wpt_1|USER1|43.833333333333336|-79.5". The ident does not make a slot set; the position does.
        var rows = Live.Select(r => r == "wpt_3||0|0" ? "wpt_3|USER3|0|0" : r).ToArray();
        var parsed = L1011InsScrape.Parse(rows)!;
        Assert.Equal(new L1011InsWaypoint("USER3", 0, 0), parsed.Waypoints[2]);
        Assert.Equal("Waypoint 3: not set", L1011InsWaypoints.Lines(parsed.Waypoints, parsed.ActiveBatch, parsed.BatchCount)[2]);
    }

    [Fact]
    public void Unknown_values_read_as_unknown()
    {
        var rows = Live.Select(r => r.StartsWith("mode|") ? "mode|" : r.StartsWith("status|") ? "status|" :
            r.StartsWith("auto_batch|") ? "auto_batch|" : r.StartsWith("wpt_2|") ? "wpt_2|RUSME||" : r).ToArray();
        var parsed = L1011InsScrape.Parse(rows)!;
        Assert.Equal(-1, parsed.Ins.Mode);
        Assert.Null(parsed.Ins.Status);
        Assert.Null(parsed.AutoBatch);
        Assert.Null(parsed.Waypoints[1]);
    }

    [Fact]
    public void An_open_entry_and_a_dark_unit()
    {
        var rows = Live.Select(r => r == "power|on" ? "power|off" : r == "entry|" ? "entry|left" :
            r == "pms_entry|0" ? "pms_entry|1" : r == "pms_line|0" ? "pms_line|4" : r).ToArray();
        var parsed = L1011InsScrape.Parse(rows)!;
        Assert.False(parsed.Ins.Powered);
        Assert.Equal("left", parsed.Ins.Entry);
        Assert.True(parsed.Pms!.EntryOpen);
        Assert.Equal(4, parsed.Pms.ActiveLine);
    }

    [Fact]
    public void Numbers_parse_the_same_under_a_comma_decimal_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            var parsed = L1011InsScrape.Parse(Live.Select(r => r == "status|1" ? "status|45.5" : r).ToArray())!;
            Assert.Equal(45.5, parsed.Ins.Status);
            Assert.Equal(37.2982, parsed.Waypoints[0]!.Latitude);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
