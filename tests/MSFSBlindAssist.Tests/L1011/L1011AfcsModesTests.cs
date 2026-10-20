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
    public void Alpha_mode_says_when_the_autothrottle_holds_angle_of_attack_and_when_it_stops()
    {
        // L:INI_AOA_SPEED_MODE is 1 while the autothrottle holds angle of attack instead of speed (the
        // glareshield's speed window reads AOA). It is not an armed or captured mode.
        Assert.Equal("Autothrottle alpha mode", L1011AfcsModes.Words("INI_AOA_SPEED_MODE", 1));
        Assert.Equal("Autothrottle alpha mode off", L1011AfcsModes.Words("INI_AOA_SPEED_MODE", 0));
        Assert.DoesNotContain(L1011AfcsModes.Flags, f => f.Key == "INI_AOA_SPEED_MODE");
    }

    [Fact]
    public void Alpha_mode_speaks_after_its_baseline_both_ways()
    {
        var a = new L1011AfcsAnnouncer();
        Assert.Empty(a.Observe("INI_AOA_SPEED_MODE", 0));
        Assert.Equal(new[] { "Autothrottle alpha mode" }, a.Observe("INI_AOA_SPEED_MODE", 1));
        Assert.Equal(new[] { "Autothrottle alpha mode off" }, a.Observe("INI_AOA_SPEED_MODE", 0));
    }

    [Fact]
    public void Alpha_mode_never_lists_in_the_armed_and_captured_line()
    {
        var values = new Dictionary<string, double> { ["INI_AOA_SPEED_MODE"] = 0 };
        var lines = L1011AutopilotStatus.Lines(null, k => values.TryGetValue(k, out var v) ? v : null);
        Assert.Contains("Armed and captured: none", lines);
    }

    [Theory]
    [InlineData(250.0, "Autothrottle on, speed 250")]
    [InlineData(249.6, "Autothrottle on, speed 250")]
    public void The_autothrottle_read_back_adds_the_speed_it_holds(double knots, string spoken)
    {
        Assert.Equal(spoken, L1011AfcsModes.WithSpeedTarget("Autothrottle on", knots));
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(spoken, L1011AfcsModes.WithSpeedTarget("Autothrottle on", knots));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(1.0, 1.0, "Autopilot A command")]
    [InlineData(0.0, 1.0, "Autopilot A command, not flying pitch")]
    [InlineData(1.0, 0.0, "Autopilot A command, not flying roll")]
    [InlineData(0.0, 0.0, "Autopilot A command, not flying pitch or roll")]
    [InlineData(null, 0.0, "Autopilot A command, not flying roll")]   // a loop not read is no claim
    [InlineData(null, null, "Autopilot A command")]
    public void An_engage_read_back_names_the_inner_loop_that_is_not_flying(double? pitchLoop, double? rollLoop, string spoken)
    {
        Assert.Equal(spoken, L1011AfcsModes.WithLoopHealth("Autopilot A command", pitchLoop, rollLoop));
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
        Assert.Empty(a.Observe("SWITCH_AFCS_HDG", 0));
        Assert.Equal(new[] { "Heading on" }, a.Observe("SWITCH_AFCS_HDG", 1));
        Assert.Empty(a.Observe("SWITCH_AFCS_HDG", 1));     // unchanged
        Assert.Equal(new[] { "Heading off" }, a.Observe("SWITCH_AFCS_HDG", 0));
    }

    [Fact]
    public void A_change_msfsba_commanded_is_recorded_without_a_word()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("SWITCH_AFCS_HDG", 0);
        Assert.Empty(a.Observe("SWITCH_AFCS_HDG", 1, ownCommand: true));
        Assert.Empty(a.Observe("SWITCH_AFCS_HDG", 1));        // already the baseline
        Assert.Equal(new[] { "Heading off" }, a.Observe("SWITCH_AFCS_HDG", 0));
    }

    [Fact]
    public void A_flag_going_out_is_recorded_silently()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("ANN_LOC_ARM", 0);
        Assert.Equal(new[] { "Localizer armed" }, a.Observe("ANN_LOC_ARM", 1));
        Assert.Empty(a.Observe("ANN_LOC_ARM", 0));
        Assert.Equal(new[] { "Localizer armed" }, a.Observe("ANN_LOC_ARM", 1));
    }

    [Fact]
    public void Seed_fills_only_a_missing_baseline_and_reset_clears_them()
    {
        var a = new L1011AfcsAnnouncer();
        Assert.True(a.Seed("SWITCH_AFCS_HDG", 1));
        Assert.False(a.Seed("SWITCH_AFCS_HDG", 0));
        Assert.False(a.Seed("SWITCH_AFCS_ALT_MODE", 1));
        Assert.Empty(a.Observe("SWITCH_AFCS_HDG", 1));
        a.Reset();
        Assert.Empty(a.Observe("SWITCH_AFCS_HDG", 0));
    }

    private static Func<string, double?> Reads(params (string Key, double Value)[] values) =>
        k => values.Where(v => v.Key == k).Select(v => (double?)v.Value).FirstOrDefault();

    [Fact]
    public void Vor_captured_counts_only_while_vor_or_back_course_is_on()
    {
        // L1011_INS.js sets ANN_VOR_ACTIVE whenever a navaid is received and no localizer, ILS or
        // back-course mode is on, with VOR mode off too (seen at the gate with NAV 1 tuned).
        Assert.False(L1011AfcsModes.FlagModeOn("ANN_VOR_ACTIVE", Reads(("SWITCH_AFCS_VOR", 0), ("SWITCH_AFCS_BC", 0))));
        Assert.False(L1011AfcsModes.FlagModeOn("ANN_VOR_ACTIVE", _ => null));   // a mode not known yet is off
        Assert.True(L1011AfcsModes.FlagModeOn("ANN_VOR_ACTIVE", Reads(("SWITCH_AFCS_VOR", 1))));
        Assert.True(L1011AfcsModes.FlagModeOn("ANN_VOR_ACTIVE", Reads(("SWITCH_AFCS_BC", 1))));
        Assert.True(L1011AfcsModes.FlagModeOn("ANN_LOC_ARM", _ => null));        // no mode gate
    }

    [Fact]
    public void A_received_vor_with_vor_mode_off_is_silent_and_speaks_once_the_mode_comes_on()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("SWITCH_AFCS_VOR", 0);
        a.Observe("SWITCH_AFCS_BC", 0);
        a.Observe("ANN_VOR_ACTIVE", 0);
        Assert.Empty(a.Observe("ANN_VOR_ACTIVE", 1));
        Assert.Equal(new[] { "VOR on", "VOR captured" }, a.Observe("SWITCH_AFCS_VOR", 1));
        Assert.Equal(new[] { "VOR off" }, a.Observe("SWITCH_AFCS_VOR", 0));     // the capture going out is silent
        Assert.Equal(new[] { "Back course on", "VOR captured" }, a.Observe("SWITCH_AFCS_BC", 1));
    }

    [Fact]
    public void Msfsba_pressing_vor_over_a_received_vor_still_hears_the_capture()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("SWITCH_AFCS_VOR", 0);
        a.Observe("SWITCH_AFCS_BC", 0);
        a.Observe("ANN_VOR_ACTIVE", 1);
        Assert.Equal(new[] { "VOR captured" }, a.Observe("SWITCH_AFCS_VOR", 1, ownCommand: true));
    }

    [Fact]
    public void A_vor_capture_before_the_mode_baseline_is_a_silent_baseline()
    {
        var a = new L1011AfcsAnnouncer();
        a.Observe("ANN_VOR_ACTIVE", 0);
        Assert.Empty(a.Observe("ANN_VOR_ACTIVE", 1));                // the VOR and back-course switches not seen yet
        Assert.Empty(a.Observe("SWITCH_AFCS_VOR", 1));               // the switch's own first value: a baseline
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
