using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// When the A300's light changes are spoken: after a short gather period, netted per light, and only once a
/// bus's light power has settled. The power flags and the fault flags reach MSFSBA on separate once-a-second
/// subscriptions, in either order (measured 2026-10-09: on external power on the faults cleared before the
/// AC light power rose, MSFSBA heard the power first and said "Cabin regulator 2 fault light on", then
/// "off"; on external power off a fault arrived before the power flag).
/// </summary>
public class A300LampSpeechTests
{
    private readonly A300LampSpeech _speech = new();

    private static A300LampChange On(string name) => new(name, true);
    private static A300LampChange Off(string name) => new(name, false);

    [Fact]
    public void A_light_that_went_on_and_back_off_nets_to_nothing() =>
        Assert.Empty(A300LampSpeech.Net(new[] { On("Pack 1 fault light"), Off("Pack 1 fault light") }));

    [Fact]
    public void A_light_that_changed_an_odd_number_of_times_keeps_its_last_change() =>
        Assert.Equal(new[] { Off("Pack 1 fault light") },
            A300LampSpeech.Net(new[] { Off("Pack 1 fault light"), On("Pack 1 fault light"), Off("Pack 1 fault light") }));

    [Fact]
    public void Netted_lights_keep_the_order_of_their_last_change() =>
        Assert.Equal(new[] { On("Pack 2 fault light"), On("Pack 1 fault light") },
            A300LampSpeech.Net(new[] { On("Pack 1 fault light"), On("Pack 2 fault light"), Off("Pack 1 fault light"), On("Pack 1 fault light") }));

    [Fact]
    public void Pending_changes_are_spoken_as_one_sentence_and_then_cleared()
    {
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Add(On("Pack 2 fault light"), 10_000);
        Assert.Equal("2 lights on: Pack 1 fault, Pack 2 fault", _speech.Flush(10_000 + A300LampSpeech.GatherMs));
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void A_first_change_waits_one_gather_period_before_it_is_spoken()
    {
        _speech.Add(On("Pack 1 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs - 1));
        Assert.Equal("Pack 1 fault light on", _speech.Flush(10_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void A_pair_that_nets_to_nothing_says_nothing()
    {
        _speech.Add(On("Cabin regulator 2 fault light"), 10_000);
        _speech.Add(Off("Cabin regulator 2 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void After_a_power_change_the_changes_wait_until_the_power_has_settled()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Cabin regulator 2 fault light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs - 1));
        _speech.Add(Off("Cabin regulator 2 fault light"), 11_000);   // the clear, a subscription period later
        _speech.Add(On("Standby generator fault light"), 11_000);
        Assert.Equal("Standby generator fault light on", _speech.Flush(10_000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void A_fault_arriving_just_before_its_power_flag_is_netted_with_it()
    {
        // Measured 2026-10-09, external power off: "2 lights on: AC bus 2 off, Rudder travel limiter system 2
        // fault" a second before the AC light power flag arrived, then the same fault among the lights off.
        _speech.Add(On("Rudder travel limiter system 2 fault light"), 10_000);
        _speech.Add(On("AC bus 2 off light"), 10_000);
        Assert.Null(_speech.Flush(10_000 + 900));                     // a batch ends inside the gather period
        _speech.NotePowerChange(10_000 + 1000);
        _speech.Add(Off("Rudder travel limiter system 2 fault light"), 10_000 + 1000);
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.GatherMs));
        Assert.Equal("AC bus 2 off light on", _speech.Flush(10_000 + 1000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void Clearing_drops_the_pending_changes_and_the_hold()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Pack 1 fault light"), 10_000);
        _speech.Clear();
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs));
        _speech.Add(On("Pack 1 fault light"), 20_000);
        Assert.Equal("Pack 1 fault light on", _speech.Flush(20_000 + A300LampSpeech.GatherMs));
    }

    [Fact]
    public void The_settle_window_covers_two_subscription_periods_and_the_gather_period_one() =>
        Assert.Equal((true, true), (A300LampSpeech.SettleMs is >= 2000 and <= 3000, A300LampSpeech.GatherMs is >= 1000 and < 2000));
}
