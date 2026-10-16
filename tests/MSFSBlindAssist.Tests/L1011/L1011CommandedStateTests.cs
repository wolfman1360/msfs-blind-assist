using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011CommandedStateTests
{
    [Fact]
    public void A_fresh_command_outranks_the_stale_cache()
    {
        var s = new L1011CommandedState();
        s.Record("TOGGLE_BATTERY", 1, 1000);
        Assert.Equal(1, s.Resolve("TOGGLE_BATTERY", 0, 1000 + L1011CommandedState.HoldMs));
    }

    [Fact]
    public void A_position_passed_through_on_the_way_counts_as_your_own()
    {
        var s = new L1011CommandedState();
        s.Record("SWITCH_AFCS_AP_A", 0, 1000, via: new[] { 1.0 });
        Assert.True(s.IsOwn("SWITCH_AFCS_AP_A", 1, 1100));    // CWS on the way to Command
        Assert.True(s.IsOwn("SWITCH_AFCS_AP_A", 0, 1100));    // where it was put
        Assert.False(s.IsOwn("SWITCH_AFCS_AP_A", 2, 1100));   // Off: the aircraft moved it
        Assert.False(s.IsOwn("SWITCH_AFCS_AP_A", 1, 1001 + L1011CommandedState.HoldMs));
        Assert.Equal(0, s.Resolve("SWITCH_AFCS_AP_A", 2, 1100));   // planning still uses the target
    }

    [Fact]
    public void An_old_command_gives_way_to_the_cache()
    {
        var s = new L1011CommandedState();
        s.Record("TOGGLE_BATTERY", 1, 1000);
        Assert.Equal(0, s.Resolve("TOGGLE_BATTERY", 0, 1001 + L1011CommandedState.HoldMs));
        Assert.Null(s.Resolve("OTHER", null, 1000));
    }
}
