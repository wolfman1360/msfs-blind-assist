using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>Which autopilot states speak, with what words, and the announcer's baseline-first rule.</summary>
public class L1011AfcsModesTests
{
    [Fact]
    public void Engage_switches_name_all_three_positions()
    {
        Assert.Equal("Autopilot A command", L1011AfcsModes.Words("SWITCH_AFCS_AP_A", 0));
        Assert.Equal("Autopilot B CWS", L1011AfcsModes.Words("SWITCH_AFCS_AP_B", 1));
        Assert.Equal("Autopilot A off", L1011AfcsModes.Words("SWITCH_AFCS_AP_A", 2));
    }

    [Fact]
    public void Mode_buttons_say_on_and_off()
    {
        Assert.Equal("Heading on", L1011AfcsModes.Words("SWITCH_AFCS_HDG", 1));
        Assert.Equal("Altitude hold off", L1011AfcsModes.Words("SWITCH_AFCS_ALT", 0));
        Assert.Equal("Autothrottle on", L1011AfcsModes.Words("SWITCH_AFCS_AT", 1));
    }

    [Fact]
    public void Armed_and_captured_flags_speak_only_when_they_come_on()
    {
        Assert.Equal("Glideslope captured", L1011AfcsModes.Words("ANN_ILS_ACTIVE", 1));
        Assert.Null(L1011AfcsModes.Words("ANN_ILS_ACTIVE", 0));
        Assert.Equal("VOR captured", L1011AfcsModes.Words("ANN_VOR_ACTIVE", 1));
        Assert.Equal(new[] { "ANN_LOC_ARM", "ANN_LOC_ACTIVE", "ANN_VOR_ACTIVE", "ANN_ILS_ARM", "ANN_ILS_ACTIVE", "ANN_ALT_ARM", "FLARE_ACTIVE", "ROLLOUT_ACTIVE" },
            L1011AfcsModes.Flags.Select(f => f.Key));
    }

    [Fact]
    public void Silent_controls_are_not_announced()
    {
        Assert.False(L1011AfcsModes.IsAnnounced("SWITCH_AFCS_ALT_MODE"));
        Assert.False(L1011AfcsModes.IsAnnounced("SWITCH_AFCS_CPT_RNAV"));
        Assert.Null(L1011AfcsModes.Words("SWITCH_AFCS_ALT_MODE", 1));
    }

    [Fact]
    public void Engaged_lists_the_lit_mode_buttons_in_panel_order()
    {
        var values = new Dictionary<string, double> { ["SWITCH_AFCS_HDG"] = 1, ["SWITCH_AFCS_ALT"] = 1, ["SWITCH_AFCS_VS"] = 0 };
        Assert.Equal("Altitude hold, Heading", L1011AfcsModes.Engaged(k => values.TryGetValue(k, out var v) ? v : null));
        Assert.Equal("none", L1011AfcsModes.Engaged(_ => null));
    }

    [Fact]
    public void The_first_value_is_a_silent_baseline_and_a_change_speaks()
    {
        var a = new L1011AfcsAnnouncer();
        Assert.Null(a.Observe("SWITCH_AFCS_HDG", 0));
        Assert.Equal("Heading on", a.Observe("SWITCH_AFCS_HDG", 1));
        Assert.Null(a.Observe("SWITCH_AFCS_HDG", 1));     // unchanged
        Assert.Equal("Heading off", a.Observe("SWITCH_AFCS_HDG", 0));
    }

    [Fact]
    public void A_change_msfsba_commanded_is_recorded_without_a_word()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("SWITCH_AFCS_HDG", 0);
        Assert.Null(a.Observe("SWITCH_AFCS_HDG", 1, ownCommand: true));
        Assert.Null(a.Observe("SWITCH_AFCS_HDG", 1));        // already the baseline
        Assert.Equal("Heading off", a.Observe("SWITCH_AFCS_HDG", 0));
    }

    [Fact]
    public void A_flag_going_out_is_recorded_silently()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("ANN_LOC_ARM", 0);
        Assert.Equal("Localizer armed", a.Observe("ANN_LOC_ARM", 1));
        Assert.Null(a.Observe("ANN_LOC_ARM", 0));
        Assert.Equal("Localizer armed", a.Observe("ANN_LOC_ARM", 1));
    }

    [Fact]
    public void Seed_fills_only_a_missing_baseline_and_reset_clears_them()
    {
        var a = new L1011AfcsAnnouncer();
        Assert.True(a.Seed("SWITCH_AFCS_HDG", 1));
        Assert.False(a.Seed("SWITCH_AFCS_HDG", 0));
        Assert.False(a.Seed("SWITCH_AFCS_ALT_MODE", 1));
        Assert.Null(a.Observe("SWITCH_AFCS_HDG", 1));
        a.Reset();
        Assert.Null(a.Observe("SWITCH_AFCS_HDG", 0));
    }

    [Fact]
    public void Pitch_and_lateral_modes_need_a_flight_director_or_the_autopilot()
    {
        // L1011_INS.js turns each of these straight back off when the autopilot is off and both
        // flight directors are off (checked live at the gate, 2026-10-03, with vertical speed).
        foreach (var key in new[] { "SWITCH_AFCS_IAS", "SWITCH_AFCS_MACH", "SWITCH_AFCS_VNAV", "SWITCH_AFCS_VS", "SWITCH_AFCS_ALT",
                                     "SWITCH_AFCS_HDG", "SWITCH_AFCS_INS", "SWITCH_AFCS_VOR", "SWITCH_AFCS_LOC", "SWITCH_AFCS_ILS", "SWITCH_AFCS_BC" })
            Assert.Contains(key, L1011AfcsModes.NeedFlightDirectorOrAutopilot);
        Assert.DoesNotContain("SWITCH_AFCS_TURB", L1011AfcsModes.NeedFlightDirectorOrAutopilot);
        Assert.DoesNotContain("SWITCH_AFCS_AT", L1011AfcsModes.NeedFlightDirectorOrAutopilot);
        Assert.DoesNotContain("SWITCH_AFCS_TM", L1011AfcsModes.NeedFlightDirectorOrAutopilot);
    }

    [Theory]
    [InlineData(0.0, 0.0, 2.0, 2.0, true)]     // both directors off, both autopilots off
    [InlineData(1.0, 0.0, 2.0, 2.0, false)]    // flight director A on
    [InlineData(0.0, 1.0, 2.0, 2.0, false)]    // flight director B on
    [InlineData(0.0, 0.0, 0.0, 2.0, false)]    // autopilot A in command
    [InlineData(0.0, 0.0, 2.0, 1.0, false)]    // autopilot B in CWS
    [InlineData(null, 0.0, 2.0, 2.0, false)]   // a position not known yet is never a reason to refuse
    public void No_director_or_autopilot_only_when_every_position_is_known(double? fdA, double? fdB, double? apA, double? apB, bool expected)
    {
        var read = new Dictionary<string, double?>
        {
            ["SWITCH_AFCS_FD_A"] = fdA, ["SWITCH_AFCS_FD_B"] = fdB, ["SWITCH_AFCS_AP_A"] = apA, ["SWITCH_AFCS_AP_B"] = apB,
        };
        Assert.Equal(expected, L1011AfcsModes.KnownNoDirectorOrAutopilot(k => read.TryGetValue(k, out var v) ? v : null));
    }

    [Fact]
    public void The_announcer_remembers_the_last_value_it_saw()
    {
        var a = new L1011AfcsAnnouncer();
        Assert.Null(a.Last("SWITCH_AFCS_VS"));
        a.Observe("SWITCH_AFCS_VS", 1);
        Assert.Equal(1, a.Last("SWITCH_AFCS_VS"));
        a.Reset();
        Assert.Null(a.Last("SWITCH_AFCS_VS"));
    }

    [Fact]
    public void Every_announced_key_is_unique()
    {
        var keys = L1011AfcsModes.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }
}
