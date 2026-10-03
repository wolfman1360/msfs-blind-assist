using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011LampGateTests
{
    [Fact]
    public void The_first_delivery_is_a_silent_baseline()
    {
        var g = new L1011LampGate();
        g.Observe("ENG_FIRE_1", true, 0);
        Assert.Empty(g.Due(10_000));
    }

    [Fact]
    public void A_change_is_spoken_once_after_it_settles()
    {
        var g = new L1011LampGate();
        g.Observe("ENG_FIRE_1", false, 0);
        g.Observe("ENG_FIRE_1", true, 1000);
        Assert.Empty(g.Due(1000 + L1011LampGate.SettleMs - 1));
        Assert.Equal(new[] { ("ENG_FIRE_1", true) }, g.Due(1000 + L1011LampGate.SettleMs));
        Assert.Empty(g.Due(9000));
    }

    [Fact]
    public void A_flash_that_returns_before_settling_is_not_spoken()
    {
        var g = new L1011LampGate();
        g.Observe("LE_FLAPS_LIGHT", false, 0);
        g.Observe("LE_FLAPS_LIGHT", true, 1000);
        g.Observe("LE_FLAPS_LIGHT", false, 1500);
        Assert.Empty(g.Due(5000));
    }

    [Fact]
    public void Light_test_changes_and_the_lamps_going_out_afterwards_are_silent()
    {
        var g = new L1011LampGate();
        g.Observe("ENG_FIRE_1", false, 0);
        g.SetLightTest(true, 1000);
        g.Observe("ENG_FIRE_1", true, 1500);
        Assert.Empty(g.Due(4000));
        g.SetLightTest(false, 5000);
        g.Observe("ENG_FIRE_1", false, 5500);
        Assert.Empty(g.Due(9000));
        g.Observe("ENG_FIRE_1", true, 10_000);
        Assert.Equal(new[] { ("ENG_FIRE_1", true) }, g.Due(12_000));
    }

    [Fact]
    public void Reset_makes_the_next_delivery_a_baseline_again()
    {
        var g = new L1011LampGate();
        g.Observe("ENG_FIRE_1", false, 0);
        g.Reset();
        g.Observe("ENG_FIRE_1", true, 100);
        Assert.Empty(g.Due(5000));
    }

    [Fact]
    public void Seed_fills_only_a_lamp_with_no_baseline_and_says_nothing()
    {
        var g = new L1011LampGate();
        g.Observe("ENG_FIRE_1", false, 0);
        Assert.False(g.Seed("ENG_FIRE_1", true));
        Assert.True(g.Seed("ENG_FIRE_2", true));
        g.Observe("ENG_FIRE_2", true, 100);
        Assert.Empty(g.Due(5000));
        g.Observe("ENG_FIRE_2", false, 6000);
        Assert.Equal(new[] { ("ENG_FIRE_2", false) }, g.Due(6000 + L1011LampGate.SettleMs));
    }
}
