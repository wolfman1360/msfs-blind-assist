using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// What the TriStar definition does and says, against a SimConnectManager that never connected (so
/// nothing can be sent and CalcWriteCanLand is false) and a fake clock.
/// </summary>
public class IniL1011BehaviourTests
{
    private long _now = 10_000;
    private readonly UserSettings _settings = new();
    private readonly IniL1011Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly List<string> _reReads = new();

    public IniL1011BehaviourTests()
    {
        _def = new IniL1011Definition { Clock = () => _now, SettingsSource = () => _settings };
        _def.ReRead = (key, _) => _reReads.Add(key);
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    private void Batch(long advanceMs)
    {
        _now += advanceMs;
        _def.OnContinuousBatchDelivered(1);
    }

    [Fact]
    public void A_base_variable_is_left_to_mainform()
    {
        Assert.False(Set("SIM_ON_GROUND", 1));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_pick_that_cannot_land_is_refused_aloud()
    {
        Assert.True(Set("TOGGLE_BATTERY", 1));
        Assert.Equal(new[] { "Battery unavailable" }, _speech.All);
    }

    [Theory]
    [InlineData(L1011Levers.FlapHandleKey, 3)]
    [InlineData(L1011Levers.GearLeverKey, 100)]
    [InlineData(L1011Levers.SpeedBrakeKey, 50)]
    [InlineData(L1011Levers.GroundSpoilersKey, 1)]
    [InlineData(L1011Levers.ParkingBrakeKey, 1)]
    public void A_refused_lever_pick_is_said_and_snaps_the_combo_back(string key, double value)
    {
        string name = _def.GetVariables()[key].DisplayName;
        Assert.True(Set(key, value));
        Assert.Equal(new[] { $"{name} unavailable" }, _speech.All);
        Assert.Equal(new[] { key }, _reReads);
    }

    [Fact]
    public void A_gear_lever_write_that_is_sent_is_read_again_after_the_settle()
    {
        var settle = new TaskCompletionSource();
        var waits = new List<int>();
        _def.CanLand = _ => true;
        _def.SettleDelay = ms => { waits.Add(ms); return settle.Task; };

        Assert.True(Set(L1011Levers.GearLeverKey, 100));
        Assert.Equal(new[] { L1011Levers.GearSettleMs }, waits);
        Assert.Empty(_reReads);            // not before the settle
        settle.SetResult();
        Assert.Equal(new[] { L1011Levers.GearLeverKey }, _reReads);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_gear_lever_re_read_is_dropped_when_the_definition_goes_away_during_the_settle()
    {
        var settle = new TaskCompletionSource();
        _def.CanLand = _ => true;
        _def.SettleDelay = _ => settle.Task;

        Assert.True(Set(L1011Levers.GearLeverKey, 0));
        _def.Dispose();
        settle.SetResult();
        Assert.Empty(_reReads);
    }

    [Theory]
    [InlineData(L1011Levers.FlapHandleKey, 3)]
    [InlineData(L1011Levers.SpeedBrakeKey, 50)]
    [InlineData(L1011Levers.GroundSpoilersKey, 1)]
    [InlineData(L1011Levers.ParkingBrakeKey, 1)]
    public void Another_lever_write_that_is_sent_is_not_read_again(string key, double value)
    {
        var waits = new List<int>();
        _def.CanLand = _ => true;
        _def.SettleDelay = ms => { waits.Add(ms); return Task.CompletedTask; };

        Assert.True(Set(key, value));
        Assert.Empty(waits);
        Assert.Empty(_reReads);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_toggle_switch_in_an_unknown_position_is_refused_before_anything_is_sent()
    {
        Assert.True(Set("TOGGLE_CUTOFF_ENG_1", 1));
        Assert.Equal(new[] { "Engine 1 fuel and ignition: position unknown, try again in a moment" }, _speech.All);
    }

    [Fact]
    public void A_typed_altimeter_outside_the_range_is_an_error()
    {
        Assert.True(Set(L1011Levers.CaptainAltimeterKey, 50));
        Assert.Equal(new[] { "Captain altimeter: enter 28.20 to 31.30 inches, or 955 to 1060 hectopascals" }, _speech.All);
    }

    [Fact]
    public void A_valid_typed_value_still_refuses_when_it_cannot_land()
    {
        Assert.True(Set(L1011Levers.CaptainAltimeterKey, 29.92));
        Assert.Equal(new[] { "Captain altimeter unavailable" }, _speech.All);
    }

    [Theory]
    [InlineData(1238)]
    [InlineData(77777)]
    [InlineData(12.5)]
    public void A_squawk_with_a_digit_above_seven_or_not_four_digits_is_an_error(double typed)
    {
        Assert.True(Set(L1011Levers.SquawkKey, typed));
        Assert.Equal(new[] { "Squawk: enter four digits, each 0 to 7" }, _speech.All);
    }

    [Fact]
    public void The_breaker_list_button_opens_the_window_and_says_nothing()
    {
        bool opened = false;
        _def.OpenCircuitBreakers = () => opened = true;
        Assert.True(Set(L1011Levers.BreakerListKey, 1));
        Assert.True(opened);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_switch_position_is_consumed_silently()
    {
        Assert.True(_def.ProcessSimVarUpdate("TOGGLE_BATTERY", 0, _speech));
        Assert.True(_def.ProcessSimVarUpdate("TOGGLE_BATTERY", 1, _speech));
        Batch(5_000);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_readout_is_left_to_the_status_display()
    {
        Assert.False(_def.ProcessSimVarUpdate("L1011_RO_FUEL_TOTAL", 12400, _speech));
    }

    [Fact]
    public void A_light_speaks_once_it_has_settled_and_never_on_its_first_reading()
    {
        Deliver("LAMP_ENG_FIRE_1", 0);      // baseline
        Batch(1_000);
        Deliver("LAMP_ENG_FIRE_1", 1);
        Batch(500);
        Assert.Empty(_speech.All);         // not settled yet
        Batch(L1011LampGate.SettleMs);
        Assert.Equal(new[] { "Engine 1 fire light on" }, _speech.All);
        Batch(5_000);
        Assert.Single(_speech.All);        // spoken once
    }

    [Fact]
    public void A_muted_light_is_not_spoken()
    {
        _settings.L1011DisabledMonitorVariables = new List<string> { "LAMP_ENG_FIRE_1" };
        _settings.RebuildDisabledMonitorVariableCaches();
        Deliver("LAMP_ENG_FIRE_1", 0);
        Deliver("LAMP_ENG_FIRE_1", 1);
        Batch(L1011LampGate.SettleMs + 1);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_annunciator_light_test_lights_every_lamp_without_a_word()
    {
        Deliver("SWITCH_LIGHTS_TEST", 0);
        Deliver("LAMP_ENG_FIRE_1", 0);
        Batch(1_000);
        Deliver("SWITCH_LIGHTS_TEST", 1);
        Deliver("LAMP_ENG_FIRE_1", 1);
        Batch(L1011LampGate.SettleMs + 1);
        Deliver("SWITCH_LIGHTS_TEST", 0);
        Deliver("LAMP_ENG_FIRE_1", 0);
        Batch(L1011LampGate.SettleMs + 1);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_lever_moved_outside_msfsba_is_spoken_after_its_baseline()
    {
        Deliver(L1011Levers.ParkingBrakeKey, 0);
        Assert.Empty(_speech.All);
        Deliver(L1011Levers.ParkingBrakeKey, 1);
        Deliver(L1011Levers.FlapHandleKey, 0);
        Deliver(L1011Levers.FlapHandleKey, 3);
        Assert.Equal(new[] { "Parking brake set", "Flaps 14 degrees" }, _speech.All);
    }

    [Fact]
    public void A_context_reset_makes_the_next_reading_a_silent_baseline_again()
    {
        Deliver("LAMP_ENG_FIRE_1", 0);
        Deliver(L1011Levers.ParkingBrakeKey, 0);
        _def.OnSimContextReset();
        Deliver("LAMP_ENG_FIRE_1", 1);
        Deliver(L1011Levers.ParkingBrakeKey, 1);
        Batch(L1011LampGate.SettleMs + 1);
        Assert.Empty(_speech.All);
    }
}
