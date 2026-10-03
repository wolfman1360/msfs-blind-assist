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
    public void An_old_command_gives_way_to_the_cache()
    {
        var s = new L1011CommandedState();
        s.Record("TOGGLE_BATTERY", 1, 1000);
        Assert.Equal(0, s.Resolve("TOGGLE_BATTERY", 0, 1001 + L1011CommandedState.HoldMs));
        Assert.Null(s.Resolve("OTHER", null, 1000));
    }
}
