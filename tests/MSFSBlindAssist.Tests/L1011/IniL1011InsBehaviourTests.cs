using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// What the INS and PMS do and say: typed keys, the present-position entry, automatic waypoint loading,
/// the panel buttons that open the INS window, the window's selectors, and the two variables that speak
/// on their own — against a SimConnectManager that never connected (nothing can be sent) and a fake clock.
/// </summary>
public class IniL1011InsBehaviourTests
{
    private long _now = 10_000;
    private readonly UserSettings _settings = new();
    private readonly IniL1011Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected

    public IniL1011InsBehaviourTests()
    {
        _def = new IniL1011Definition { Clock = () => _now, SettingsSource = () => _settings };
    }

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    [Fact]
    public void A_typing_slip_is_named_and_sends_nothing()
    {
        Assert.False(_def.TypeKeys(L1011InsDevice.Ins1, "N36X", insert: true, _sim, _speech));
        Assert.False(_def.TypeKeys(L1011InsDevice.CaptainPms, "-5", insert: true, _sim, _speech));
        Assert.Equal(new[]
        {
            "INS 1: " + L1011InsDevices.InsKeysError,
            "Captain PMS: " + L1011InsDevices.PmsKeysError,
        }, _speech.All);
    }

    [Fact]
    public void Keys_refuse_aloud_when_they_cannot_land()
    {
        Assert.False(_def.TypeKeys(L1011InsDevice.Ins2, "N36051", insert: true, _sim, _speech));
        Assert.False(_def.PressKeys(L1011InsDevice.FirstOfficerPms, new[] { "PMS_FO_FWD" }, _sim, _speech));
        Assert.Equal(new[] { "INS 2 keypad unavailable", "First officer PMS keypad unavailable" }, _speech.All);
    }

    [Fact]
    public void Nothing_typed_presses_nothing_and_says_nothing()
    {
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "", insert: false, _sim, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_key_the_map_lacks_is_refused_by_name()
    {
        Assert.False(_def.PressKeys(L1011InsDevice.Ins1, new[] { "INS_1_KEY_Q" }, _sim, _speech));
        Assert.Equal(new[] { "INS 1 keypad: INS_1_KEY_Q is not a control of this aircraft" }, _speech.All);
    }

    [Fact]
    public void The_present_position_entry_and_automatic_loading_refuse_aloud_when_they_cannot_land()
    {
        Assert.False(_def.EnterPresentPosition(3, 36.0854, -115.1336, _sim, _speech));
        Assert.False(_def.SetAutoBatch(1, true, _sim, _speech));
        Assert.Equal(new[] { "INS 3 keypad unavailable", "INS 1 automatic waypoint loading unavailable" }, _speech.All);
    }

    [Fact]
    public void A_present_position_that_cannot_be_keyed_is_refused_aloud_and_nothing_is_chained()
    {
        // Task 2's PresentPositionControls does no range check: infinity gives a null key, and a latitude
        // past 90 keys digits the unit would take as another figure. The caller refuses before any key.
        _def.CanLand = _ => true;   // so the position, not the calculator path, is the only reason
        Assert.False(_def.EnterPresentPosition(2, double.NaN, -115.1336, _sim, _speech));
        Assert.False(_def.EnterPresentPosition(2, 36.0854, double.PositiveInfinity, _sim, _speech));
        Assert.False(_def.EnterPresentPosition(2, 91, -115.1336, _sim, _speech));
        Assert.False(_def.EnterPresentPosition(2, 36.0854, -180.5, _sim, _speech));
        Assert.Equal(Enumerable.Repeat("INS 2: present position unavailable", 4), _speech.All);
        Assert.Equal(0, _def.KeysBusyForMs());
    }

    [Fact]
    public void Chained_keys_say_how_long_they_still_take()
    {
        // The window holds its read-back of what was typed until the keys have landed (the A300's McduKeysPending).
        _def.CanLand = _ => true;
        Assert.Equal(0, _def.KeysBusyForMs());
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "2", insert: false, _sim, _speech));
        int oneKey = _def.KeysBusyForMs();
        Assert.Equal(IniL1011Definition.KeyHoldMs + IniL1011Definition.KeyGapMs, oneKey);
        _now += 30;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "3", insert: false, _sim, _speech));
        Assert.Equal(2 * oneKey - 30, _def.KeysBusyForMs());   // queued behind the first
        _now += 10_000;
        Assert.Equal(0, _def.KeysBusyForMs());
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_panel_button_opens_the_window_on_its_unit()
    {
        var opened = new List<L1011InsDevice?>();
        _def.OpenInsWindow = d => opened.Add(d);
        string key = L1011InsDevices.WindowKey(L1011InsDevice.FirstOfficerPms);
        Assert.True(_def.HandleUIVariableSet(key, 1, _def.GetVariables()[key], _sim, _speech));
        Assert.Equal(new L1011InsDevice?[] { L1011InsDevice.FirstOfficerPms }, opened);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_window_selectors_are_the_panel_rows()
    {
        var positions = _def.InsSelectorPositions("INS_2_ROTARY_MODE");
        Assert.Equal(9, positions.Count);
        Assert.Equal("Position", positions[3]);
        Assert.Empty(_def.InsSelectorPositions("NOT_A_ROW"));
        Assert.Null(_def.InsSelectorPosition("INS_2_ROTARY_MODE", _sim));   // nothing reported yet
        // A pick goes through the row's write path, refused aloud as any panel row is when it cannot land.
        _def.SetInsSelector("INS_2_ROTARY_MODE", 3, _sim, _speech);
        Assert.Equal(new[] { "Display selector unavailable" }, _speech.All);
    }

    [Fact]
    public void Alignment_and_the_pms_phase_are_announced_and_muteable()
    {
        var vars = _def.GetVariables();
        for (int unit = 1; unit <= 3; unit++)
        {
            var status = vars[L1011InsAlignmentAnnouncer.StatusKey(unit)];
            Assert.Equal($"INS {unit} alignment", status.DisplayName);
            Assert.True(status.IsAnnounced);
            Assert.False(status.ExcludeFromMonitorManager);
            Assert.True(ContinuousBatchLayout.RidesBatch(status));
        }
        var phase = vars[L1011PmsPhaseAnnouncer.Key];
        Assert.Equal("PMS flight phase", phase.DisplayName);
        Assert.True(phase.IsAnnounced);
        Assert.True(ContinuousBatchLayout.RidesBatch(phase));
    }

    [Fact]
    public void Navigation_starting_by_itself_and_the_pms_phase_are_spoken()
    {
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 90);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 33.2);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 1);
        Deliver(L1011PmsPhaseAnnouncer.Key, 1);
        Deliver(L1011PmsPhaseAnnouncer.Key, 3);
        Assert.Equal(new[] { "INS 1 navigating", "PMS climb" }, _speech.All);
    }

    [Fact]
    public void A_context_reset_makes_the_next_value_a_baseline_again()
    {
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(2), 50);
        Deliver(L1011PmsPhaseAnnouncer.Key, 3);
        _def.OnSimContextReset();
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(2), 1);
        Deliver(L1011PmsPhaseAnnouncer.Key, 4);
        Assert.Empty(_speech.All);
    }
}
