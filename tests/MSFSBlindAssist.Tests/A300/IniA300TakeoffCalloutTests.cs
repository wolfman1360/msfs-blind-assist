using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300's take-off roll call-outs on the shared machine: V1 and VR from the MCDU take-off page,
/// V2 from the FCU speed window only when it is plausibly V2.
/// </summary>
public class IniA300TakeoffCalloutTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly HashSet<string> _muted = new();

    public IniA300TakeoffCalloutTests()
    {
        _def = new IniA300Definition { IsMuted = key => _muted.Contains(key) };
        _def.Attach(new SimConnectManager(IntPtr.Zero));   // never connected
    }

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    private void Speeds(double v1, double vr, double fcuSpeed)
    {
        Deliver(A300TakeoffCallouts.V1Key, v1);
        Deliver(A300TakeoffCallouts.VrKey, vr);
        Deliver(A300TakeoffCallouts.V2Key, fcuSpeed);
    }

    private void Roll(params double[] speeds)
    {
        foreach (var ias in speeds)
            Deliver(A300TakeoffCallouts.IasKey, ias);
    }

    [Theory]
    [InlineData(145, 150, 150)]
    [InlineData(145, 175, 175)]
    [InlineData(145, 176, 0)]   // more than 30 knots above VR: the FCU speed is not V2
    [InlineData(145, 140, 0)]   // below VR
    [InlineData(0, 150, 0)]     // no VR
    public void V2_is_the_fcu_speed_only_when_it_is_plausible(double vr, double fcu, double expected) =>
        Assert.Equal(expected, A300TakeoffCallouts.PlausibleV2(vr, fcu));

    [Fact]
    public void The_speeds_ride_the_batch_with_ctrl_m_rows_and_the_airspeed_is_per_frame()
    {
        var vars = _def.GetVariables();
        Assert.Equal("INI_V1_FMGS", vars[A300TakeoffCallouts.V1Key].Name);
        Assert.Equal("INI_VR_FMGS", vars[A300TakeoffCallouts.VrKey].Name);
        Assert.Equal("INI_V2_FMGS", vars[A300TakeoffCallouts.V2Key].Name);
        foreach (var (key, _, name) in A300TakeoffCallouts.Speeds)
        {
            Assert.True(ContinuousBatchLayout.RidesBatch(vars[key]), key);
            Assert.False(vars[key].ExcludeFromMonitorManager);
            Assert.Equal(name, vars[key].DisplayName);
        }
        var ias = vars[A300TakeoffCallouts.IasKey];
        Assert.Equal("AIRSPEED INDICATED", ias.Name);
        Assert.True(ias.ExcludeFromBatch);
        Assert.True(ias.HighFrequency);
        Assert.True(ias.ExcludeFromMonitorManager);
        Assert.Equal(A300TakeoffCallouts.IasKey, _def.TakeoffCalloutFeedKey);
    }

    [Fact]
    public void A_take_off_calls_v1_rotate_and_v2()
    {
        Speeds(140, 145, 150);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30, 141, 146);
        Deliver("SIM_ON_GROUND", 0);
        Roll(151);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, _speech.Interrupts);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, _speech.All);
    }

    [Fact]
    public void Entering_the_speeds_is_consumed_and_says_nothing()
    {
        foreach (var (key, _, _) in A300TakeoffCallouts.Speeds)
            Assert.True(_def.ProcessSimVarUpdate(key, 150, _speech));
        Assert.True(_def.ProcessSimVarUpdate(A300TakeoffCallouts.IasKey, 20, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void An_fcu_speed_that_is_not_v2_gives_no_v2_call()
    {
        Speeds(140, 145, 250);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30, 141, 146);
        Deliver("SIM_ON_GROUND", 0);
        Roll(160, 251);
        Assert.Equal(new[] { "V1", "Rotate" }, _speech.Interrupts);
    }

    [Fact]
    public void V2_set_on_the_fcu_after_the_speeds_is_still_called()
    {
        Speeds(140, 145, 250);
        Deliver(A300TakeoffCallouts.V2Key, 152);   // the crew sets V2 on the FCU last
        Deliver("SIM_ON_GROUND", 1);
        Roll(30, 141, 146);
        Deliver("SIM_ON_GROUND", 0);
        Roll(153);
        Assert.Equal(new[] { "V1", "Rotate", "V2" }, _speech.Interrupts);
    }

    [Fact]
    public void V1_equal_to_vr_is_one_utterance()
    {
        Speeds(145, 145, 150);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30, 146);
        Assert.Equal(new[] { "V1, Rotate" }, _speech.Interrupts);
    }

    [Fact]
    public void Each_call_is_muted_by_its_own_row()
    {
        _muted.Add(A300TakeoffCallouts.V1Key);
        Speeds(140, 145, 150);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30, 141, 146);
        Assert.Equal(new[] { "Rotate" }, _speech.Interrupts);
    }

    [Fact]
    public void Nothing_is_called_while_the_announcer_is_suppressed()
    {
        Speeds(140, 145, 150);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30);
        _speech.Suppressed = true;
        Roll(141);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_context_reset_drops_the_arm()
    {
        Speeds(140, 145, 150);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30);
        _def.OnSimContextReset();
        Roll(100, 141, 146);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_reconnect_drops_the_arm_but_keeps_the_speeds()
    {
        Speeds(140, 145, 150);
        Deliver("SIM_ON_GROUND", 1);
        Roll(30);
        _def.ResetAnnouncementBaselines();
        Roll(100, 141);
        Assert.Empty(_speech.All);
        Roll(20, 141);   // a fresh arm on the ground: the speeds were kept
        Assert.Equal(new[] { "V1" }, _speech.Interrupts);
    }

    [Fact]
    public void The_feed_pauses_airborne_once_the_roll_is_over()
    {
        Speeds(140, 145, 150);
        Assert.True(_def.TakeoffCalloutFeedNeeded);   // on the ground
        Deliver("SIM_ON_GROUND", 0);
        Roll(250);
        Assert.False(_def.TakeoffCalloutFeedNeeded);
    }
}
