using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Hotkeys;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// What the autopilot does and says: typed values, the mode announcements, the value boxes' and
/// Ctrl+P's buttons, and the toggle keys — against a SimConnectManager that never connected (nothing
/// is sent, the cache is empty) and a fake clock.
/// </summary>
public class IniL1011AutopilotBehaviourTests
{
    private long _now = 10_000;
    private readonly UserSettings _settings = new();
    private readonly IniL1011Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly List<(string Key, int TimeoutMs)> _freshReads = new();
    private double? _freshValue;

    public IniL1011AutopilotBehaviourTests()
    {
        _def = new IniL1011Definition { Clock = () => _now, SettingsSource = () => _settings };
        _def.ReRead = (_, _) => { };
        _def.ReadBackDelay = _ => Task.CompletedTask;
        _def.ReadFresh = (_, key, timeoutMs) =>
        {
            _freshReads.Add((key, timeoutMs));
            return Task.FromResult(_freshValue);
        };
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    private bool Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    private bool Hotkey(HotkeyAction action)
    {
        using var hotkeys = new HotkeyManager();
        return _def.HandleHotkeyAction(action, _sim, _speech, null!, hotkeys);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(double.NaN)]   // an empty or mistyped panel box
    public void A_typed_speed_outside_the_knob_range_is_an_error(double typed)
    {
        Assert.True(Set(L1011Afcs.SpeedKey, typed));
        Assert.Equal(new[] { "Speed: enter 100 to 350 knots" }, _speech.All);
    }

    [Fact]
    public void A_valid_typed_value_refuses_aloud_when_it_cannot_land()
    {
        Assert.True(Set(L1011Afcs.AltitudeKey, 35000));
        Assert.Equal(new[] { "Altitude unavailable" }, _speech.All);
    }

    [Fact]
    public void A_mode_change_speaks_after_its_baseline()
    {
        Assert.True(Deliver("SWITCH_AFCS_HDG", 0));
        Assert.Empty(_speech.All);
        Assert.True(Deliver("SWITCH_AFCS_HDG", 1));
        Deliver("ANN_ILS_ACTIVE", 0);
        Deliver("ANN_ILS_ACTIVE", 1);
        Deliver("SWITCH_AFCS_AP_A", 2);
        Deliver("SWITCH_AFCS_AP_A", 0);
        Assert.Equal(new[] { "Heading on", "Glideslope captured", "Autopilot A command" }, _speech.All);
    }

    [Fact]
    public void The_disconnect_button_refuses_aloud_when_it_cannot_land()
    {
        Assert.True(Set(L1011Afcs.DisconnectKey, 1));
        Assert.Equal(new[] { "Autopilot disconnect unavailable" }, _speech.All);
    }

    [Fact]
    public void A_vor_capture_is_announced()
    {
        Deliver("ANN_VOR_ACTIVE", 0);
        Deliver("ANN_VOR_ACTIVE", 1);
        Assert.Equal(new[] { "VOR captured" }, _speech.All);
    }

    private void NoDirectorOrAutopilot()
    {
        Deliver("SWITCH_AFCS_FD_A", 0);
        Deliver("SWITCH_AFCS_FD_B", 0);
        Deliver("SWITCH_AFCS_AP_A", 2);
        Deliver("SWITCH_AFCS_AP_B", 2);
    }

    [Fact]
    public void A_mode_button_with_no_flight_director_or_autopilot_is_refused_with_the_reason()
    {
        NoDirectorOrAutopilot();
        Assert.True(Set("SWITCH_AFCS_HDG", 1));
        Assert.Equal(new[] { "Heading: turn on a flight director or the autopilot first" }, _speech.All);
    }

    [Fact]
    public void A_mode_button_with_a_flight_director_on_is_sent()
    {
        NoDirectorOrAutopilot();
        Deliver("SWITCH_AFCS_FD_A", 1);
        _speech.All.Clear();
        Assert.True(Set("SWITCH_AFCS_HDG", 1));
        Assert.Equal(new[] { "Heading unavailable" }, _speech.All);   // passed the check; this test sim cannot send
    }

    [Fact]
    public void Turning_a_mode_off_is_never_refused_for_want_of_a_director()
    {
        NoDirectorOrAutopilot();
        Assert.True(Set("SWITCH_AFCS_HDG", 0));
        Assert.Equal(new[] { "Heading unavailable" }, _speech.All);
    }

    [Fact]
    public void A_typed_vertical_speed_with_the_mode_off_is_refused_with_the_reason()
    {
        Deliver("SWITCH_AFCS_VS", 0);
        Assert.True(Set(L1011Afcs.VerticalSpeedKey, -1500));
        Assert.Equal(new[] { "Vertical speed: turn vertical speed mode on first" }, _speech.All);
    }

    [Fact]
    public void A_typed_vertical_speed_with_the_mode_on_is_sent()
    {
        Deliver("SWITCH_AFCS_VS", 1);
        Assert.True(Set(L1011Afcs.VerticalSpeedKey, -1500));
        Assert.Equal(new[] { "Vertical speed unavailable" }, _speech.All);
    }

    [Fact]
    public void A_silent_autopilot_switch_says_nothing()
    {
        Assert.True(Deliver("SWITCH_AFCS_ALT_MODE", 0));
        Assert.True(Deliver("SWITCH_AFCS_ALT_MODE", 1));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_context_reset_makes_the_next_mode_reading_a_silent_baseline()
    {
        Deliver("SWITCH_AFCS_HDG", 0);
        _def.OnSimContextReset();
        Deliver("SWITCH_AFCS_HDG", 1);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_toggle_in_an_unknown_position_is_refused_and_reported_as_refused()
    {
        Assert.False(_def.ToggleControl("SWITCH_AFCS_HDG", _sim, _speech));
        Assert.Equal(new[] { "Heading: position unknown, try again in a moment" }, _speech.All);
    }

    [Fact]
    public void The_speed_box_carries_its_four_buttons_with_their_names_and_positions()
    {
        Deliver("SWITCH_AFCS_AT", 1);
        var buttons = _def.ValueBoxButtons(L1011AutoflightWindows.Speed, _sim, _speech);
        Assert.Equal(new[] { "&Autothrottle", "&Thrust management", "&IAS hold", "&Mach hold" }, buttons.Select(b => b.Label));
        Assert.Equal("On", buttons[0].GetCurrentState());
        Assert.Equal("", buttons[1].GetCurrentState());   // not known yet
        Assert.All(buttons, b => Assert.True(b.SuppressStateAnnounce!()));   // the press reads back itself
    }

    [Fact]
    public void A_box_button_that_is_sent_reads_its_result_back_fresh()
    {
        _def.CanLand = _ => true;
        Deliver("SWITCH_AFCS_TM", 0);
        _freshValue = 0;   // the aircraft turned thrust management straight back off
        var buttons = _def.ValueBoxButtons(L1011AutoflightWindows.Speed, _sim, _speech);
        buttons[1].OnPressed();
        Assert.Equal(new[] { ("SWITCH_AFCS_TM", IniL1011Definition.BatchReadoutTimeoutMs) }, _freshReads);
        Assert.Equal(new[] { "Thrust management off" }, _speech.All);
    }

    [Fact]
    public void A_box_button_that_is_refused_says_only_why()
    {
        NoDirectorOrAutopilot();
        Deliver("SWITCH_AFCS_HDG", 0);
        _def.CanLand = _ => true;
        _def.ValueBoxButtons(L1011AutoflightWindows.Heading, _sim, _speech)[0].OnPressed();
        Assert.Equal(new[] { "Heading: turn on a flight director or the autopilot first" }, _speech.All);
        Assert.Empty(_freshReads);
    }

    [Fact]
    public void The_autopilot_window_carries_its_buttons_and_the_engage_paddles()
    {
        var buttons = _def.AutopilotButtons(_sim, _speech);
        Assert.Equal(new[]
        {
            "&Flight director A", "Flight di&rector B", "T&urbulence", "&ILS", "&Localizer", "&VOR", "Bac&k course",
            "Autopilot &disconnect", "Captain autot&hrottle disconnect", "Takeoff &go-around button",
        }, buttons.Select(b => b.Label));
        var selectors = _def.AutopilotSelectors(_sim, _speech);
        Assert.Equal(new[] { "Autopilot A", "Autopilot B" }, selectors.Select(s => s.Label));
        Assert.Equal(new[] { "Command", "CWS", "Off" }, selectors[0].Positions.OrderBy(p => p.Key).Select(p => p.Value));
        Deliver("SWITCH_AFCS_AP_B", 2);
        Assert.Equal(2, selectors[1].GetCurrentValue());
    }

    [Fact]
    public void A_window_button_press_says_nothing_when_it_works()
    {
        _def.CanLand = _ => true;
        Deliver("SWITCH_AFCS_FD_A", 0);
        _def.AutopilotButtons(_sim, _speech)[0].OnPressed();
        Assert.Empty(_speech.All);
        Assert.Empty(_freshReads);
    }

    [Fact]
    public void The_status_list_starts_with_the_windows_not_read_yet()
    {
        Deliver("SWITCH_AFCS_AP_A", 2);
        var lines = _def.AutopilotStatusLines(_sim);
        Assert.Equal(L1011AutopilotStatus.NotReadYet, lines[0]);
        Assert.Contains("Autopilot A off", lines);
    }

    [Theory]
    [InlineData(HotkeyAction.ToggleAutopilot1, "SWITCH_AFCS_AP_A", 2, 0, "Autopilot A command")]
    [InlineData(HotkeyAction.ToggleAutopilot2, "SWITCH_AFCS_AP_B", 0, 2, "Autopilot B off")]
    [InlineData(HotkeyAction.ToggleAutothrust, "SWITCH_AFCS_AT", 0, 1, "Autothrottle on")]
    [InlineData(HotkeyAction.ToggleLocalizer, "SWITCH_AFCS_LOC", 1, 0, "Localizer off")]
    [InlineData(HotkeyAction.ToggleApproachMode, "SWITCH_AFCS_ILS", 0, 1, "ILS on")]
    public void A_toggle_key_moves_its_control_and_reads_it_back(HotkeyAction action, string key, double now, double landed, string spoken)
    {
        _def.CanLand = _ => true;
        Deliver("SWITCH_AFCS_FD_A", 1);   // a director is on, so no mode is refused
        Deliver(key, now);
        _speech.All.Clear();
        _freshValue = landed;
        Assert.True(Hotkey(action));
        Assert.Equal(new[] { spoken }, _speech.All);
    }

    [Fact]
    public void A_toggle_key_in_an_unknown_position_is_refused_aloud()
    {
        Assert.True(Hotkey(HotkeyAction.ToggleAutopilot1));
        Assert.Equal(new[] { "Autopilot A: position unknown, try again in a moment" }, _speech.All);
        Assert.Empty(_freshReads);
    }

    [Fact]
    public void A_value_box_refuses_before_it_opens_when_it_cannot_land()
    {
        Assert.True(Hotkey(HotkeyAction.FCUSetHeading));
        Assert.Equal(new[] { "Heading unavailable" }, _speech.All);
    }
}
