using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The five units the INS window drives: which Coherent view each is read from, and which keys
/// typed text presses (a press is the cockpit's own click, so an id must be a control in the map).</summary>
public class L1011InsDevicesTests
{
    [Fact]
    public void Each_device_its_view_unit_and_seat()
    {
        Assert.Equal(new[] { "INS 1", "INS 2", "INS 3", "Captain PMS", "First officer PMS" },
            L1011InsDevices.All.Select(L1011InsDevices.Name));
        Assert.Equal("L1011_INS_1", L1011InsDevices.ViewNeedle(L1011InsDevice.Ins1));
        Assert.Equal("L1011_INS_1", L1011InsDevices.ViewNeedle(L1011InsDevice.CaptainPms));
        Assert.Equal("L1011_INS_2", L1011InsDevices.ViewNeedle(L1011InsDevice.FirstOfficerPms));
        Assert.Equal("L1011_INS_3", L1011InsDevices.ViewNeedle(L1011InsDevice.Ins3));
        Assert.Equal(2, L1011InsDevices.Unit(L1011InsDevice.Ins2));
        Assert.Null(L1011InsDevices.Unit(L1011InsDevice.CaptainPms));
        Assert.Equal("FO", L1011InsDevices.Seat(L1011InsDevice.FirstOfficerPms));
        Assert.Null(L1011InsDevices.Seat(L1011InsDevice.Ins3));
    }

    [Fact]
    public void Window_keys_round_trip()
    {
        foreach (var device in L1011InsDevices.All)
            Assert.Equal(device, L1011InsDevices.FromWindowKey(L1011InsDevices.WindowKey(device)));
        Assert.Null(L1011InsDevices.FromWindowKey("SWITCH_AFCS_HDG"));
        Assert.Equal(5, L1011InsDevices.All.Select(L1011InsDevices.WindowKey).Distinct().Count());
    }

    [Fact]
    public void Typing_a_latitude_into_an_ins()
    {
        var typed = L1011InsDevices.Keys(L1011InsDevice.Ins1, "n 36 05.1");
        Assert.Null(typed.Error);
        Assert.Equal(new[] { "INS_1_KEY_2", "INS_1_KEY_3", "INS_1_KEY_6", "INS_1_KEY_0", "INS_1_KEY_5", "INS_1_KEY_1" },
            typed.ControlIds);
    }

    [Fact]
    public void A_character_with_no_ins_key_sends_nothing()
    {
        var typed = L1011InsDevices.Keys(L1011InsDevice.Ins2, "N36X");
        Assert.Empty(typed.ControlIds);
        Assert.Equal(L1011InsDevices.InsKeysError, typed.Error);
    }

    [Fact]
    public void Typing_into_a_pms()
    {
        Assert.Equal(new[] { "PMS_CPT_1", "PMS_CPT_5", "PMS_CPT_7", "PMS_CPT_3" },
            L1011InsDevices.Keys(L1011InsDevice.CaptainPms, "157.3").ControlIds);
        Assert.Equal(new[] { "PMS_FO_2", "PMS_FO_SP", "PMS_FO_4" },
            L1011InsDevices.Keys(L1011InsDevice.FirstOfficerPms, "2 4").ControlIds);
        var minus = L1011InsDevices.Keys(L1011InsDevice.CaptainPms, "-12");
        Assert.Empty(minus.ControlIds);
        Assert.Equal(L1011InsDevices.PmsKeysError, minus.Error);
    }

    [Fact]
    public void The_insert_and_clear_keys()
    {
        Assert.Equal("INS_3_KEY_INSERT", L1011InsDevices.InsertKey(L1011InsDevice.Ins3));
        Assert.Equal("PMS_FO_INSERT", L1011InsDevices.InsertKey(L1011InsDevice.FirstOfficerPms));
        Assert.Equal("INS_1_KEY_CLEAR", L1011InsDevices.ClearKey(L1011InsDevice.Ins1));
        Assert.Equal("PMS_CPT_CLR", L1011InsDevices.ClearKey(L1011InsDevice.CaptainPms));
    }

    [Fact]
    public void Lines_for_an_ins_a_pms_and_nothing_read_yet()
    {
        Assert.Equal(new[] { "Reading INS 2..." }, L1011InsDevices.Lines(L1011InsDevice.Ins2, null));
        Assert.Equal(new[] { "Reading the captain PMS..." }, L1011InsDevices.Lines(L1011InsDevice.CaptainPms, null));

        var ins = new L1011InsScreen(1, false, 3, 0, "", "", "", new HashSet<int>(), null, "", Array.Empty<string>());
        var blank = new[] { "", "", "", "", "", "" };
        var dark = new L1011InsScrapeResult(ins, new L1011PmsScreen("Captain", "", blank, blank, false, 0),
            Array.Empty<L1011InsWaypoint?>(), 0, 0, null);
        Assert.Equal(new[] { "INS 1: off" }, L1011InsDevices.Lines(L1011InsDevice.Ins1, dark));
        Assert.Equal(new[] { "Captain PMS: off" }, L1011InsDevices.Lines(L1011InsDevice.CaptainPms, dark));

        var redrawing = dark with { Ins = ins with { Powered = true } };
        Assert.Null(L1011InsDevices.Lines(L1011InsDevice.CaptainPms, redrawing));   // keep the page shown
    }

    // The window's read-back of what was typed (the fleet's scratchpad read-back): the line an open entry
    // shows, "" when none is open, null when it cannot be judged (nothing read, a dark unit, a PMS redrawing).
    private static L1011InsScrapeResult Read(string entry = "", bool pmsEntry = false, int pmsLine = 0,
        string title = "TAKEOFF", bool powered = true)
    {
        var ins = new L1011InsScreen(1, powered, 3, 0, "   36N", "072  0 ", "00", new HashSet<int>(), 90, entry, Array.Empty<string>());
        var left = new[] { "GMT", "EPR", "PR ALT", "GW", "FUEL", "SAT" };
        var right = new[] { "0755Z", "---", "1969", "47", "---", "---" };
        var blank = new[] { "", "", "", "", "", "" };
        var pms = title.Length == 0
            ? new L1011PmsScreen("Captain", "", blank, blank, pmsEntry, pmsLine)
            : new L1011PmsScreen("Captain", title, left, right, pmsEntry, pmsLine);
        return new L1011InsScrapeResult(ins, pms, Array.Empty<L1011InsWaypoint?>(), 0, 0, null);
    }

    [Fact]
    public void The_entry_line_of_an_ins()
    {
        Assert.Equal("Entry, left window: 36N", L1011InsDevices.EntryLine(L1011InsDevice.Ins1, Read(entry: "left")));
        Assert.Equal("", L1011InsDevices.EntryLine(L1011InsDevice.Ins1, Read()));
    }

    [Fact]
    public void The_entry_line_of_a_pms_is_its_active_line()
    {
        Assert.Equal("4: GW, entering: 47", L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, Read(pmsEntry: true, pmsLine: 4)));
        Assert.Equal("", L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, Read()));
    }

    [Fact]
    public void An_entry_that_cannot_be_judged_is_null()
    {
        Assert.Null(L1011InsDevices.EntryLine(L1011InsDevice.Ins2, null));
        Assert.Null(L1011InsDevices.EntryLine(L1011InsDevice.Ins1, Read(entry: "left", powered: false)));
        Assert.Null(L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, Read(pmsEntry: true, pmsLine: 1, title: "")));
    }

    [Fact]
    public void An_ins_entry_is_not_read_from_a_pms_and_back()
    {
        // The PMS shares the view: an INS entry is not the PMS's, and a PMS entry is not the INS's.
        Assert.Equal("", L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, Read(entry: "left")));
        Assert.Equal("", L1011InsDevices.EntryLine(L1011InsDevice.Ins1, Read(pmsEntry: true, pmsLine: 4)));
    }

    [Fact]
    public void A_pms_entry_on_no_line_is_none()
    {
        Assert.Equal("", L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, Read(pmsEntry: true, pmsLine: 0)));
    }

    [Fact]
    public void A_leg_entry_reads_as_its_line()
    {
        Assert.Equal("Entry, leg: 00", L1011InsDevices.EntryLine(L1011InsDevice.Ins1, Read(entry: "leg")));
    }

    // The fleet's scratchpad read-back waits for two stable polls. A PMS line the unit erases within about a
    // second would be missed, so the window reads it on the FIRST poll that sees it ([CORE-7]: a validation
    // error always speaks): the gauge's ERROR word in the value cell after a refused GMT, GW, FUEL or SAT
    // (page 1, facts 6.3), or "---" drawn inside the label cell while the entry is open (a refused ALT on the
    // cruise and descent pages; the climb page's empty ALT prompt has the same shape and reads at once too).
    [Fact]
    public void A_pms_line_the_unit_erases_within_a_second_reads_at_once()
    {
        var error = Read(pmsEntry: true, pmsLine: 1);
        error = error with { Pms = error.Pms! with { Right = new[] { "ERROR", "---", "1969", "47", "---", "---" } } };
        Assert.Equal("1: GMT, entering: ERROR", L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, error));
        Assert.True(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.CaptainPms, error));

        var refusedAlt = Read(pmsEntry: true, pmsLine: 3, title: "3ENG CRUISE STD");
        refusedAlt = refusedAlt with
        {
            Pms = refusedAlt.Pms! with
            {
                Left = new[] { "CRZ______0.830/285", "FUEL REM", "ALT ---  OPT 350", "STEP", "TOD____000NM 0000Z", "SAT_-45____TAS 480" },
                Right = new[] { "", "157.3", "", "----Z", "", "" },
            },
        };
        Assert.Equal("3: ALT --- OPT 350, entering", L1011InsDevices.EntryLine(L1011InsDevice.CaptainPms, refusedAlt));
        Assert.True(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.CaptainPms, refusedAlt));

        // An ordinary open entry, a closed one and a dark unit wait for the stable polls as the fleet does.
        Assert.False(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.CaptainPms, Read(pmsEntry: true, pmsLine: 4)));
        Assert.False(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.CaptainPms, Read()));
        Assert.False(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.CaptainPms, error with { Ins = error.Ins with { Powered = false } }));
        Assert.False(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.CaptainPms, null));
    }

    [Fact]
    public void An_ins_entry_the_unit_refused_stays_and_is_never_read_at_once()
    {
        // An INS INSERT with too few digits is ignored by the gauge: nothing changes on the display, the entry
        // line stays as it was, and the read-back has nothing new to say. The ERROR word belongs to the PMS
        // sharing the view, never to the INS.
        var refused = Read(entry: "left", pmsEntry: true, pmsLine: 1);
        refused = refused with { Pms = refused.Pms! with { Right = new[] { "ERROR", "---", "1969", "47", "---", "---" } } };
        Assert.Equal("Entry, left window: 36N", L1011InsDevices.EntryLine(L1011InsDevice.Ins1, refused));
        Assert.False(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.Ins1, refused));
        Assert.False(L1011InsDevices.EntryReadsAtOnce(L1011InsDevice.Ins1, Read(entry: "left")));
    }
}
