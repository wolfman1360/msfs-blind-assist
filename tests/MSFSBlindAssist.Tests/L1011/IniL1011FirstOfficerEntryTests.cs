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
        foreach (var key in new[] { ContinuousIgnitionKey, ApuStartKey, PaddleAKey, "ROTARY_CABIN_BARO#DEC", "ROTARY_FE_LIGHTS_PANEL" })
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
    [InlineData("ROTARY_FE_LIGHTS_PANEL", true)]       // a knob, read on request
    [InlineData(L1011Levers.ParkingBrakeKey, true)]    // a lever
    [InlineData("LAMP_ENG_FIRE_1", true)]              // a lamp
    [InlineData("L1011_RO_FUEL_TOTAL", true)]          // a readout
    [InlineData(ApuStartKey, false)]                   // a button: write-only
    [InlineData(L1011Levers.SquawkKey, false)]         // a typed entry: write-only
    [InlineData("NOT_A_TRISTAR_KEY", false)]
    public void It_can_read_back_a_key_registered_for_reading(string key, bool canReadBack)
    {
        Assert.Equal(canReadBack, _def.FoCanReadBack(key));
    }
}
