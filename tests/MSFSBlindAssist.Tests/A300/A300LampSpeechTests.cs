using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// When the A300's light changes are spoken: at a batch end, netted per light, and only once a bus's
/// light power has settled (measured 2026-10-09: on external power the faults cleared 500 ms before
/// the AC light power rose, and MSFSBA, reading each on its own once-a-second subscription, heard the
/// power first, so it said "Cabin regulator 2 fault light on" and then "off").
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
        _speech.Add(On("Pack 1 fault light"));
        _speech.Add(On("Pack 2 fault light"));
        Assert.Equal("2 lights on: Pack 1 fault, Pack 2 fault", _speech.Flush(10_000));
        Assert.Null(_speech.Flush(10_000));
    }

    [Fact]
    public void A_pair_that_nets_to_nothing_says_nothing()
    {
        _speech.Add(On("Cabin regulator 2 fault light"));
        _speech.Add(Off("Cabin regulator 2 fault light"));
        Assert.Null(_speech.Flush(10_000));
    }

    [Fact]
    public void After_a_power_change_the_changes_wait_until_the_power_has_settled()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Cabin regulator 2 fault light"));
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs - 1));
        _speech.Add(Off("Cabin regulator 2 fault light"));   // the clear, a subscription period later
        _speech.Add(On("Standby generator fault light"));
        Assert.Equal("Standby generator fault light on", _speech.Flush(10_000 + A300LampSpeech.SettleMs));
    }

    [Fact]
    public void Clearing_drops_the_pending_changes_and_the_hold()
    {
        _speech.NotePowerChange(10_000);
        _speech.Add(On("Pack 1 fault light"));
        _speech.Clear();
        Assert.Null(_speech.Flush(10_000 + A300LampSpeech.SettleMs));
        _speech.Add(On("Pack 1 fault light"));
        Assert.Equal("Pack 1 fault light on", _speech.Flush(10_001));
    }

    [Fact]
    public void The_settle_window_covers_two_subscription_periods() =>
        Assert.InRange(A300LampSpeech.SettleMs, 2000, 3000);
}
