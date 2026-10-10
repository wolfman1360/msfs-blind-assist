using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// What the A300 definition sends and says, against a SimConnectManager that never connected, a
/// fake cache, a fake clock and a recording sender.
/// </summary>
public class IniA300BehaviourTests
{
    private long _now = 10_000;
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<string> _reReads = new();
    private readonly List<TaskCompletionSource> _waits = new();

    public IniA300BehaviourTests()
    {
        _def = new IniA300Definition
        {
            Clock = () => _now,
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (key, _) => _reReads.Add(key),
            Delay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
        };
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    [Fact]
    public void A_base_variable_is_left_to_mainform()
    {
        Assert.False(Set("SIM_ON_GROUND", 1));
        Assert.Empty(_sent);
    }

    [Fact]
    public void A_switch_is_flipped_once_and_nothing_is_said()
    {
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_BATT_1_Set)" }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_second_pick_before_the_cache_catches_up_plans_from_the_first()
    {
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.True(Set("A300_BATT_1", 1));   // the cache still says 0: without the commanded state this flipped it back
        Assert.Single(_sent);
    }

    [Fact]
    public void A_row_sharing_its_variable_with_another_reads_it_live_on_its_own_subscription()
    {
        // The crew oxygen supply button toggles the courier supply's variable (iniBuilds' wiring): read
        // once at panel open, its row went stale after the other row's write, and its next pick was
        // planned from the old position. [VAR-7]: the copy stays off the batch.
        var vars = _def.GetVariables();
        var courier = vars["A300_COURIER_O2_SUPPLY"];
        var crew = vars["A300_PAX_OXY_SUPPLY"];
        Assert.Equal(courier.Name, crew.Name);
        Assert.True(ContinuousBatchLayout.RidesBatch(courier));
        Assert.Equal((UpdateFrequency.Continuous, true, true),
            (crew.UpdateFrequency, crew.IsAnnounced, crew.ExcludeFromBatch));
    }

    [Fact]
    public void Every_position_row_follows_the_cockpit_live()
    {
        // Every combo; a knob's slider is read when its panel opens.
        var rows = _def.GetVariables().Values.Where(v => v.ValueDescriptions?.Count > 0 && !v.RenderAsSlider);
        Assert.NotEmpty(rows);
        Assert.Empty(rows.Where(v => v.UpdateFrequency != UpdateFrequency.Continuous).Select(v => $"{v.DisplayName} ({v.Name})"));
    }

    private void ReadFreshReturns(string key, double value)
    {
        _def.TypedDelay = _ => Task.CompletedTask;
        _def.ReadFresh = (_, k, _) => Task.FromResult<double?>(k == key ? value : null);
    }

    [Fact]
    public void A_switch_the_aircraft_did_not_move_snaps_back_and_says_so()
    {
        // The crossbleed button acts only in manual mode: in auto the aircraft ignores it, and the combo kept
        // the pilot's pick while the valve stayed open (2026-10-10).
        _cache["A300_AIR_XFEED"] = 1;
        ReadFreshReturns("A300_AIR_XFEED", 1);
        Assert.True(Set("A300_AIR_XFEED", 0));
        Assert.Equal(new[] { "0 (>B:AIRLINER_AIR_XFEED_Set)" }, _sent);
        Assert.Equal(new[] { "Crossbleed stayed Open" }, _speech.All);
        Assert.Equal(new[] { "A300_AIR_XFEED" }, _reReads);
    }

    [Fact]
    public void A_switch_that_moved_says_nothing()
    {
        _cache["A300_BATT_1"] = 0;
        ReadFreshReturns("A300_BATT_1", 1);
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Empty(_speech.All);
        Assert.Empty(_reReads);
    }

    [Fact]
    public void A_switch_that_did_not_move_is_planned_from_where_it_is()
    {
        _cache["A300_AIR_XFEED"] = 1;
        ReadFreshReturns("A300_AIR_XFEED", 1);
        Set("A300_AIR_XFEED", 0);
        Assert.True(Set("A300_AIR_XFEED", 0));   // without the read-back this planned from the stale pick: nothing sent
        Assert.Equal(2, _sent.Count);
    }

    [Fact]
    public void Only_the_latest_pick_of_a_switch_is_checked()
    {
        var release = new TaskCompletionSource();
        _def.TypedDelay = _ => release.Task;
        _def.ReadFresh = (_, _, _) => Task.FromResult<double?>(1);
        _cache["A300_AIR_XFEED"] = 1;
        Set("A300_AIR_XFEED", 0);
        Set("A300_AIR_XFEED", 1);   // the pilot changed their mind before the first check
        release.SetResult();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_lever_that_moved_then_dropped_is_not_said_to_have_stayed()
    {
        // Pitch trim 2 picked on with no hydraulics: it engaged, then dropped before the read-back, which said
        // "Pitch trim 2 stayed Off" after the drop's own "Pitch trim 2 off" (2026-10-10).
        var release = new TaskCompletionSource();
        _def.TypedDelay = _ => release.Task;
        _def.ReadFresh = (_, _, _) => Task.FromResult<double?>(0);
        _cache["A300_PITCH_TRIM_2"] = 0;
        Deliver("A300_PITCH_TRIM_2", 0);
        Set("A300_PITCH_TRIM_2", 1);
        Deliver("A300_PITCH_TRIM_2", 1);
        _speech.All.Clear();   // the engage: the real announcer drops it inside the echo wrap
        Deliver("A300_PITCH_TRIM_2", 0);
        release.SetResult();
        Assert.Equal(new[] { "Pitch trim 2 off" }, _speech.All);
        Assert.Empty(_reReads);
    }

    [Fact]
    public void The_crossbleed_reads_open_or_closed_and_its_mode_auto_or_manual()
    {
        var vars = _def.GetVariables();
        Assert.Equal(new[] { "Closed", "Open" }, vars["A300_AIR_XFEED"].ValueDescriptions!.Values);
        Assert.Equal(new[] { "Auto", "Manual" }, vars["A300_AIR_XFEED_AUTO"].ValueDescriptions!.Values);
    }

    [Fact]
    public void A_switch_in_an_unknown_position_is_refused_aloud_and_snapped_back()
    {
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Battery 1: position unknown, try again in a moment" }, _speech.All);
        Assert.Equal(new[] { "A300_BATT_1" }, _reReads);
    }

    [Fact]
    public void A_write_that_cannot_land_is_refused_aloud()
    {
        _def.CanLand = _ => false;
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Battery 1 unavailable" }, _speech.All);
    }

    [Fact]
    public void A_hold_button_is_released_after_its_hold()
    {
        Assert.True(Set("A300_ECAM_ENG", 1));
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)" }, _sent);
        _waits.Single().SetResult();
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)", "0 (>B:AIRLINER_ECAM_ENG_Set)" }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Disposing_mid_hold_releases_the_button_at_once()
    {
        _def.Attach(_sim);
        Assert.True(Set("A300_ECAM_ENG", 1));
        _def.Dispose();
        Assert.Equal(new[] { "2 (>B:AIRLINER_ECAM_ENG_Set)", "0 (>B:AIRLINER_ECAM_ENG_Set)" }, _sent);
        _waits.Single().SetResult();
        Assert.Equal(2, _sent.Count);   // the release is not sent twice
    }

    [Fact]
    public void An_encoder_row_steps_its_knob()
    {
        Assert.True(Set("A300_SPEED_KNOB#INC", 1));
        Assert.True(Set("A300_SPEED_KNOB#DEC", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_SPEED_KNOB_Set)", "-1 (>B:AIRLINER_SPEED_KNOB_Set)" }, _sent);
    }

    [Theory]
    [InlineData(A300Levers.FlapsKey, 2, "8192 (>K:FLAPS_SET)")]
    [InlineData(A300Levers.SpoilersArmKey, 1, "1 (>K:SPOILERS_ARM_SET)")]
    [InlineData(A300Levers.SpeedBrakeKey, 0.5, "8192 (>K:SPOILERS_SET)")]
    public void A_lever_is_written_with_the_stock_event(string key, double value, string rpn)
    {
        Assert.True(Set(key, value));
        Assert.Equal(new[] { rpn }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_lever_write_that_cannot_land_is_refused_aloud()
    {
        _def.CanLand = _ => false;
        Assert.True(Set(A300Levers.FlapsKey, 1));
        Assert.Equal(new[] { "Flaps lever unavailable" }, _speech.All);
        Assert.Equal(new[] { A300Levers.FlapsKey }, _reReads);
    }

    [Fact]
    public void The_master_warning_speaks_as_it_comes_on_and_not_as_it_goes_out()
    {
        Deliver(A300Announcements.MasterWarningKey, 0);   // baseline
        Deliver(A300Announcements.MasterWarningKey, 1);
        Deliver(A300Announcements.MasterWarningKey, 0);   // the pilot pressed it
        Deliver(A300Announcements.MasterWarningKey, 1);
        Assert.Equal(new[] { "Master warning", "Master warning" }, _speech.All);
    }

    [Fact]
    public void A_lever_moved_elsewhere_is_spoken_after_its_baseline()
    {
        Deliver(A300Levers.FlapsKey, 0);
        Deliver(A300Levers.FlapsKey, 2);
        Deliver(A300Announcements.ParkingBrakeKey, 1);
        Deliver(A300Announcements.ParkingBrakeKey, 0);
        Assert.Equal(new[] { "Flaps 15/15", "Parking brake released" }, _speech.All);
    }

    [Theory]
    [InlineData("A300_PITCH_TRIM_1", "Pitch trim 1 off")]
    [InlineData("A300_PITCH_TRIM_2", "Pitch trim 2 off")]
    [InlineData("A300_YAW_DAMPER_1", "Yaw damper 1 off")]
    [InlineData("A300_YAW_DAMPER_2", "Yaw damper 2 off")]
    public void A_sas_lever_that_drops_by_itself_is_spoken(string key, string phrase)
    {
        // A pitch trim lever picked on with no hydraulics engaged, then dropped by itself 2.1 s later (2026-10-10).
        // MainForm's echo wrap silences the engage, the pilot's own pick; the drop is the aircraft's.
        Deliver(key, 0);
        Deliver(key, 1);
        _speech.All.Clear();   // the real announcer drops this inside the echo wrap; SpeechCapture records all
        Deliver(key, 0);
        Assert.Equal(new[] { phrase }, _speech.All);
        Assert.False(_def.GetVariables()[key].ExcludeFromMonitorManager);   // Ctrl+M can mute it
    }

    [Fact]
    public void A_switch_position_is_consumed_silently()
    {
        Assert.True(_def.ProcessSimVarUpdate("A300_BATT_1", 1, _speech));
        Assert.Empty(_speech.All);
        Assert.False(_def.ProcessSimVarUpdate("A300_RO_FCU_SPEED", 250, _speech));   // a readout: MainForm's display path
    }

    [Fact]
    public void A_context_reset_makes_the_next_value_a_silent_baseline()
    {
        Deliver(A300Announcements.GearLeverKey, 1);
        _def.OnSimContextReset();
        Deliver(A300Announcements.GearLeverKey, 0);
        Deliver(A300Announcements.GearLeverKey, 1);
        Assert.Equal(new[] { "Gear lever down" }, _speech.All);
    }
}
