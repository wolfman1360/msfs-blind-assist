using System.Globalization;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// The First Officer's write entry (<see cref="IniL1011Definition.FoSetAsync"/>): the panels' own write
/// core, returning an outcome instead of speaking, and completing only once the plan has run. Against a
/// SimConnectManager that never connected, a fake clock and a fake cache, as IniL1011BehaviourTests.
/// The speech capture only serves the panel twin: the entry takes no announcer.
/// </summary>
public class IniL1011FirstOfficerEntryTests
{
    private long _now = 10_000;
    private readonly UserSettings _settings = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly SpeechCapture _speech = new();
    private readonly Dictionary<string, double> _cache = new(StringComparer.Ordinal);
    private readonly List<string> _sent = new();
    private readonly List<string> _reReads = new();
    private readonly IniL1011Definition _def;

    private const string ContinuousIgnitionKey = "SWITCH_CONT_IGNITION";
    private const string ApuStartKey = "SWITCH_APU_START";
    private const string PaddleAKey = "SWITCH_AFCS_AP_A";

    public IniL1011FirstOfficerEntryTests()
    {
        _def = Build(_sent);
        _def.ReRead = (key, _) => _reReads.Add(key);
        _def.Attach(_sim);
    }

    /// <summary>A definition on this test's clock and cache, able to land, sending into <paramref name="sent"/>.</summary>
    private IniL1011Definition Build(List<string> sent) => new()
    {
        Clock = () => _now,
        SettingsSource = () => _settings,
        CanLand = _ => true,
        SendCalc = (_, rpn) => sent.Add(rpn),
        StepDelay = _ => Task.CompletedTask,
        SettleDelay = _ => Task.CompletedTask,
        CachedValue = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
    };

    /// <summary>What a panel pick of the same value sends, from a second definition on the same cache.</summary>
    private List<string> PanelSends(string key, double value)
    {
        var sent = new List<string>();
        var panel = Build(sent);
        panel.ReRead = (_, _) => { };
        Assert.True(panel.HandleUIVariableSet(key, value, panel.GetVariables()[key], _sim, _speech));
        return sent;
    }

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    private void Batch(long advanceMs)
    {
        _now += advanceMs;
        _def.OnContinuousBatchDelivered(1);
    }

    [Fact]
    public async Task A_set_that_lands_is_sent_once_and_reports_sent()
    {
        _cache[ContinuousIgnitionKey] = 0;

        var result = await _def.FoSetAsync(ContinuousIgnitionKey, 1);

        Assert.Equal(L1011FoResult.Sent, result);
        var panel = PanelSends(ContinuousIgnitionKey, 1);
        Assert.Single(panel);
        Assert.Equal(panel, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task A_set_that_lands_is_read_again_after_the_settle_as_a_panel_pick_is()
    {
        _cache[ContinuousIgnitionKey] = 0;

        await _def.FoSetAsync(ContinuousIgnitionKey, 1);

        Assert.Equal(new[] { ContinuousIgnitionKey }, _reReads);
    }

    [Fact]
    public async Task A_set_already_in_place_sends_nothing()
    {
        _cache[ContinuousIgnitionKey] = 1;

        var result = await _def.FoSetAsync(ContinuousIgnitionKey, 1);

        Assert.Equal(L1011FoResult.AlreadyThere, result);
        Assert.Empty(_sent);
        Assert.Empty(_reReads);
    }

    [Fact]
    public async Task A_toggle_from_an_unknown_position_is_refused_quietly()
    {
        // The row's position is its state variable, A:FUELSYSTEM VALVE SWITCH:2 (the row key's cache),
        // never L:TOGGLE_CUTOFF_ENG_1: seen live, that L:var read 1 on a cold aircraft while the valve
        // switch read 0. Nothing in the cache here, so the toggle's direction is not known.
        var result = await _def.FoSetAsync("TOGGLE_CUTOFF_ENG_1", 1);

        Assert.Equal(L1011FoResult.Refused(L1011WritePlan.UnknownPositionRefusal), result);
        Assert.Empty(_sent);
        Assert.Empty(_reReads);   // no snap-back: the First Officer has no combo to put back
        Assert.Empty(_speech.All);

        PanelSends("TOGGLE_CUTOFF_ENG_1", 1);   // the panel speaks the same reason
        Assert.Equal(new[] { $"Engine 1 fuel and ignition: {result.Reason}" }, _speech.All);
    }

    [Fact]
    public async Task It_is_unavailable_when_the_calculator_path_cannot_land()
    {
        _def.CanLand = _ => false;
        _cache[ContinuousIgnitionKey] = 0;

        var result = await _def.FoSetAsync(ContinuousIgnitionKey, 1);

        Assert.Equal(L1011FoResult.Unavailable, result);
        Assert.Empty(_sent);
        Assert.Empty(_reReads);
    }

    [Fact]
    public async Task It_is_unavailable_once_the_definition_has_gone_away()
    {
        _cache[ContinuousIgnitionKey] = 0;
        _def.Dispose();

        var result = await _def.FoSetAsync(ContinuousIgnitionKey, 1);

        Assert.Equal(L1011FoResult.Unavailable, result);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task It_is_unavailable_before_it_is_attached()
    {
        var sent = new List<string>();
        var def = Build(sent);
        _cache[ContinuousIgnitionKey] = 0;

        var result = await def.FoSetAsync(ContinuousIgnitionKey, 1);

        Assert.Equal(L1011FoResult.Unavailable, result);
        Assert.Empty(sent);
    }

    [Theory]
    [InlineData("SIM_ON_GROUND")]                     // a base variable
    [InlineData(L1011Levers.SquawkKey)]               // a typed entry
    [InlineData(L1011Levers.BreakerListKey)]          // a window opener
    [InlineData(L1011Levers.CaptainAltimeterKey)]     // the altimeters have their own entry
    [InlineData("NOT_A_TRISTAR_KEY")]
    public async Task An_unknown_or_typed_key_is_unknown(string key)
    {
        bool opened = false;
        _def.OpenCircuitBreakers = () => opened = true;

        var result = await _def.FoSetAsync(key, 1);

        Assert.Equal(L1011FoResult.UnknownKey, result);
        Assert.False(_def.FoAcceptsKey(key));
        Assert.Empty(_sent);
        Assert.False(opened);
    }

    [Fact]
    public async Task A_held_press_completes_only_after_its_release()
    {
        var panel = PanelSends(ApuStartKey, 1);   // the press, then the release
        Assert.Equal(2, panel.Count);
        var hold = new TaskCompletionSource();
        _def.StepDelay = _ => hold.Task;

        var task = _def.FoSetAsync(ApuStartKey, 1);

        Assert.False(task.IsCompleted);
        Assert.Equal(new[] { panel[0] }, _sent);
        hold.SetResult();
        Assert.True(task.IsCompleted);
        Assert.Equal(panel, _sent);
        Assert.Equal(L1011FoResult.Sent, await task);
        Assert.Empty(_reReads);   // a press has no position to read again
    }

    [Fact]
    public async Task A_press_still_held_at_dispose_is_released_once()
    {
        var panel = PanelSends(ApuStartKey, 1);
        var hold = new TaskCompletionSource();
        _def.StepDelay = _ => hold.Task;

        var task = _def.FoSetAsync(ApuStartKey, 1);
        _def.Dispose();

        Assert.Equal(panel, _sent);   // the release, sent by Dispose
        hold.SetResult();
        Assert.True(task.IsCompleted);
        Assert.Equal(panel, _sent);   // and never again when the hold ends
        Assert.Equal(L1011FoResult.Sent, await task);
    }

    [Fact]
    public async Task The_paddle_to_command_from_off_passes_through_cws()
    {
        _cache[PaddleAKey] = 2;   // Off
        var trace = new List<string>();
        _def.SendCalc = (_, rpn) => trace.Add("send:" + rpn);
        _def.StepDelay = ms => { trace.Add("wait:" + ms); return Task.CompletedTask; };

        var result = await _def.FoSetAsync(PaddleAKey, 0);

        Assert.Equal(L1011FoResult.Sent, result);
        var panel = PanelSends(PaddleAKey, 0);
        Assert.Equal(new[]
        {
            "send:" + panel[0],
            $"wait:{L1011WritePlan.PaddleNotchMs}",
            "send:" + panel[1],
        }, trace);
        Assert.Equal(new[]
        {
            "1 (>L:SWITCH_AFCS_AP_A) (>H:AUTOPILOT_SWITCH_AFCS_AP_A)",
            "0 (>L:SWITCH_AFCS_AP_A) (>H:AUTOPILOT_SWITCH_AFCS_AP_A)",
        }, panel);
    }

    [Fact]
    public async Task The_paddle_positions_the_first_officer_commanded_are_its_own_and_not_announced()
    {
        _def.ProcessSimVarUpdate(PaddleAKey, 2, _speech);   // the announcer's baseline: Off
        _cache[PaddleAKey] = 2;

        await _def.FoSetAsync(PaddleAKey, 0);
        _def.ProcessSimVarUpdate(PaddleAKey, 1, _speech);   // a batch catching the CWS notch
        _def.ProcessSimVarUpdate(PaddleAKey, 0, _speech);

        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task An_afcs_mode_refused_by_the_aircraft_rules_is_refused()
    {
        _cache["SWITCH_AFCS_FD_A"] = 0;
        _cache["SWITCH_AFCS_FD_B"] = 0;
        _cache["SWITCH_AFCS_AP_A"] = 2;
        _cache["SWITCH_AFCS_AP_B"] = 2;
        _cache["SWITCH_AFCS_ALT"] = 0;

        var result = await _def.FoSetAsync("SWITCH_AFCS_ALT", 1);

        Assert.Equal(L1011FoResult.Refused(L1011AfcsModes.NeedsDirectorRefusal), result);
        Assert.Empty(_sent);
        Assert.Empty(_reReads);
    }

    [Fact]
    public async Task A_step_row_ignores_the_value_and_sends_the_panels_step()
    {
        const string key = "ROTARY_CABIN_BARO#INC";

        var result = await _def.FoSetAsync(key, 0);

        Assert.Equal(L1011FoResult.Sent, result);
        var panel = PanelSends(key, 1);
        Assert.NotEmpty(panel);
        Assert.Equal(panel, _sent);
        Assert.Empty(_reReads);
    }

    [Fact]
    public async Task It_accepts_exactly_the_keys_it_does_not_answer_unknown()
    {
        _def.CanLand = _ => false;   // nothing is sent while the sweep runs
        var keys = _def.GetVariables().Keys.Append("NOT_A_TRISTAR_KEY").ToList();

        foreach (var key in keys)
        {
            var result = await _def.FoSetAsync(key, 0);
            Assert.True(_def.FoAcceptsKey(key) == (result.Outcome != L1011FoOutcome.UnknownKey), key);
        }
        Assert.Empty(_sent);
        foreach (var key in new[]
        {
            ContinuousIgnitionKey, ApuStartKey, PaddleAKey, "ROTARY_CABIN_BARO#DEC", "ROTARY_FE_LIGHTS_PANEL",
            L1011Levers.FlapHandleKey, L1011Levers.GearLeverKey, L1011Levers.SpeedBrakeKey,
            L1011Levers.GroundSpoilersKey, L1011Levers.ParkingBrakeKey, L1011Levers.StabTrimKey,
        })
            Assert.True(_def.FoAcceptsKey(key), key);
    }

    [Fact]
    public void It_is_ready_only_while_attached_undisposed_and_able_to_land()
    {
        var def = Build(new List<string>());
        Assert.False(def.FoReady);   // never attached
        def.Attach(_sim);
        Assert.True(def.FoReady);
        def.CanLand = _ => false;
        Assert.False(def.FoReady);
        def.CanLand = _ => true;
        def.Dispose();
        Assert.False(def.FoReady);
    }

    [Theory]
    [InlineData(ContinuousIgnitionKey, true)]          // a switch row, on the batch
    [InlineData(L1011Levers.FlapHandleKey, true)]      // the levers are batch-read
    [InlineData(L1011Levers.GearLeverKey, true)]
    [InlineData(L1011Levers.SpeedBrakeKey, true)]
    [InlineData(L1011Levers.GroundSpoilersKey, true)]
    [InlineData(L1011Levers.ParkingBrakeKey, true)]
    [InlineData("LAMP_ENG_FIRE_1", true)]              // a lamp
    [InlineData("L1011_RO_FUEL_TOTAL", true)]          // a readout
    [InlineData("ROTARY_FE_LIGHTS_PANEL", false)]      // a knob: read on request, stale until the settle re-read
    [InlineData("SWITCH_ENG_1_DISCH", false)]          // a spring switch: back at rest by the time the run ends
    [InlineData(ApuStartKey, false)]                   // a button: write-only
    [InlineData(L1011Levers.SquawkKey, false)]         // a typed entry: write-only
    [InlineData(L1011Levers.StabTrimKey, false)]       // a typed entry: write-only
    [InlineData("NOT_A_TRISTAR_KEY", false)]
    public void It_can_read_back_only_a_key_whose_reading_follows_its_write(string key, bool canReadBack)
    {
        Assert.Equal(canReadBack, _def.FoCanReadBack(key));
    }

    // ---- Levers, stabilizer trim, altimeters and the lamp mute ----------------------------------

    [Theory]
    [InlineData(L1011Levers.FlapHandleKey, 0)]
    [InlineData(L1011Levers.FlapHandleKey, 3)]
    [InlineData(L1011Levers.GearLeverKey, 0)]
    [InlineData(L1011Levers.GearLeverKey, 50)]
    [InlineData(L1011Levers.GearLeverKey, 100)]
    [InlineData(L1011Levers.SpeedBrakeKey, 0)]
    [InlineData(L1011Levers.SpeedBrakeKey, 50)]
    [InlineData(L1011Levers.GroundSpoilersKey, 0)]
    [InlineData(L1011Levers.GroundSpoilersKey, 1)]
    [InlineData(L1011Levers.ParkingBrakeKey, 0)]
    [InlineData(L1011Levers.ParkingBrakeKey, 1)]
    [InlineData(L1011Levers.StabTrimKey, 0)]
    [InlineData(L1011Levers.StabTrimKey, 2.38)]
    [InlineData(L1011Levers.StabTrimKey, 10)]
    public async Task A_lever_write_sends_the_panels_string(string key, double value)
    {
        var result = await _def.FoSetAsync(key, value);

        Assert.Equal(L1011FoResult.Sent, result);
        Assert.Empty(_speech.All);   // before the panel twin, which confirms a typed trim aloud
        var panel = PanelSends(key, value);
        Assert.Single(panel);
        Assert.Equal(panel, _sent);
    }

    [Fact]
    public async Task A_lever_write_that_cannot_land_is_unavailable_and_commands_nothing()
    {
        _def.CanLand = _ => false;
        Deliver(L1011Levers.ParkingBrakeKey, 0);   // the call-out's baseline

        var result = await _def.FoSetAsync(L1011Levers.ParkingBrakeKey, 1);

        Assert.Equal(L1011FoResult.Unavailable, result);
        Assert.Empty(_sent);
        Deliver(L1011Levers.ParkingBrakeKey, 1);   // nothing landed, so this is the aircraft's own move
        Assert.Equal(new[] { "Parking brake set" }, _speech.All);
    }

    [Fact]
    public async Task A_lever_write_is_unavailable_once_the_definition_has_gone_away()
    {
        _def.Dispose();

        var result = await _def.FoSetAsync(L1011Levers.FlapHandleKey, 3);

        Assert.Equal(L1011FoResult.Unavailable, result);
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task A_gear_lever_write_is_read_again_after_the_settle_as_a_panel_pick_is()
    {
        // The lever's own gauge refuses "up" on the ground: the write lands, nothing moves, and no
        // delivery comes to put an open panel's combo back.
        await _def.FoSetAsync(L1011Levers.GearLeverKey, 100);

        Assert.Equal(new[] { L1011Levers.GearLeverKey }, _reReads);

        _reReads.Clear();
        await _def.FoSetAsync(L1011Levers.ParkingBrakeKey, 1);
        Assert.Empty(_reReads);   // the other levers are not read again
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(10.5)]
    [InlineData(double.NaN)]
    public async Task A_stab_trim_outside_its_travel_is_refused_with_the_panels_reason(double degrees)
    {
        var result = await _def.FoSetAsync(L1011Levers.StabTrimKey, degrees);

        Assert.Equal(L1011FoResult.Refused(L1011Levers.StabTrimRangeError), result);
        Assert.Empty(_sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task A_stab_trim_that_cannot_land_is_unavailable()
    {
        _def.CanLand = _ => false;

        Assert.Equal(L1011FoResult.Unavailable, await _def.FoSetAsync(L1011Levers.StabTrimKey, 2.38));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task A_first_officer_parking_brake_write_is_not_announced_back()
    {
        // Seen live: MainForm's UI echo covers only a panel pick, so the aircraft's own lever call-out
        // would say "Parking brake set" on top of the flow's line for the First Officer's write.
        Deliver(L1011Levers.ParkingBrakeKey, 0);   // the call-out's baseline

        var result = await _def.FoSetAsync(L1011Levers.ParkingBrakeKey, 1);
        _now += 1_000;
        Deliver(L1011Levers.ParkingBrakeKey, 1);   // the batch that catches the write

        Assert.Equal(L1011FoResult.Sent, result);
        Assert.Empty(_speech.All);

        _now += L1011CommandedState.HoldMs + 1;    // past the hold: the next move is not ours
        Deliver(L1011Levers.ParkingBrakeKey, 0);   // a hardware lever
        Assert.Equal(new[] { "Parking brake released" }, _speech.All);
    }

    [Fact]
    public async Task A_first_officer_flap_and_spoiler_write_is_not_announced_back()
    {
        Deliver(L1011Levers.FlapHandleKey, 0);
        Deliver(L1011Levers.GroundSpoilersKey, 0);

        await _def.FoSetAsync(L1011Levers.FlapHandleKey, 3);
        await _def.FoSetAsync(L1011Levers.GroundSpoilersKey, 1);
        Deliver(L1011Levers.FlapHandleKey, 3);
        Deliver(L1011Levers.GroundSpoilersKey, 1);

        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task A_lever_moved_elsewhere_inside_the_hold_is_still_spoken()
    {
        // Only the position the First Officer put the lever in is its own.
        Deliver(L1011Levers.FlapHandleKey, 0);

        await _def.FoSetAsync(L1011Levers.FlapHandleKey, 3);
        Deliver(L1011Levers.FlapHandleKey, 5);   // a hardware handle moved on past it

        Assert.Equal(new[] { "Flaps 22 degrees" }, _speech.All);
    }

    [Fact]
    public void A_panel_lever_pick_still_relies_on_the_ui_echo()
    {
        Deliver(L1011Levers.ParkingBrakeKey, 0);

        Assert.True(_def.HandleUIVariableSet(L1011Levers.ParkingBrakeKey, 1,
            _def.GetVariables()[L1011Levers.ParkingBrakeKey], _sim, _speech));
        Deliver(L1011Levers.ParkingBrakeKey, 1);   // no UI echo wrap here: the call-out is the panel's to mute

        Assert.Equal(new[] { "Parking brake set" }, _speech.All);
    }

    [Fact]
    public async Task Altimeters_are_written_in_one_string_and_say_nothing()
    {
        var result = await _def.FoSetAltimetersAsync(1013.25);

        Assert.Equal(L1011FoResult.Sent, result);
        Assert.Equal(new[] { L1011Levers.AllAltimetersRpn(1013.25) }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task Altimeters_take_inches_as_the_typed_fields_do()
    {
        var result = await _def.FoSetAltimetersAsync(29.92);

        Assert.Equal(L1011FoResult.Sent, result);
        Assert.Equal(new[] { L1011Levers.AllAltimetersRpn(L1011Levers.AltimeterMillibars(29.92)!.Value) }, _sent);
    }

    [Theory]
    [InlineData(50)]
    [InlineData(900)]
    [InlineData(1100)]
    [InlineData(double.NaN)]
    public async Task An_altimeter_setting_outside_the_range_is_refused(double setting)
    {
        var result = await _def.FoSetAltimetersAsync(setting);

        Assert.Equal(L1011FoResult.Refused(L1011Levers.AltimeterRangeError), result);
        Assert.Empty(_sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task Altimeters_that_cannot_land_are_unavailable()
    {
        _def.CanLand = _ => false;

        Assert.Equal(L1011FoResult.Unavailable, await _def.FoSetAltimetersAsync(1013.25));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Altimeters_are_unavailable_before_attach_and_after_dispose()
    {
        var sent = new List<string>();
        var unattached = Build(sent);
        Assert.Equal(L1011FoResult.Unavailable, await unattached.FoSetAltimetersAsync(1013.25));

        _def.Dispose();
        Assert.Equal(L1011FoResult.Unavailable, await _def.FoSetAltimetersAsync(1013.25));
        Assert.Empty(sent);
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_lamp_mute_silences_a_test_light()
    {
        Deliver("LAMP_STBY_UNARM_LIGHT", 0);       // baseline
        Batch(1_000);

        _def.FoMuteLampSpeech(10_000);
        Deliver("LAMP_STBY_UNARM_LIGHT", 1);
        Batch(L1011LampGate.SettleMs + 1);

        Assert.Empty(_speech.All);

        _now += 10_000;                             // the mute has run out: the next change is news again
        Deliver("LAMP_STBY_UNARM_LIGHT", 0);
        Batch(L1011LampGate.SettleMs + 1);
        Assert.Equal(new[] { "Standby power unarmed light off" }, _speech.All);
    }

    [Fact]
    public async Task Stab_trim_and_altimeter_strings_are_invariant_under_de_DE()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            await _def.FoSetAsync(L1011Levers.StabTrimKey, 2.38);
            await _def.FoSetAltimetersAsync(1013.25);
            await _def.FoSetAsync(L1011Levers.SpeedBrakeKey, 50);

            Assert.Equal(new[]
            {
                "3899 (>K:ELEVATOR_TRIM_SET)",
                "1 16212 (>K:2:KOHLSMAN_SET) 2 16212 (>K:2:KOHLSMAN_SET) 3 16212 (>K:2:KOHLSMAN_SET)",
                "50 (>L:LEVER_SPOILERS)",
            }, _sent);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
