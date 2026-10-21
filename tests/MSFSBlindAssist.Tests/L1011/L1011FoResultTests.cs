using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011FoResultTests
{
    [Theory]
    [InlineData(L1011FoOutcome.Sent, true)]
    [InlineData(L1011FoOutcome.AlreadyThere, true)]
    [InlineData(L1011FoOutcome.Refused, false)]
    [InlineData(L1011FoOutcome.Unavailable, false)]
    [InlineData(L1011FoOutcome.UnknownKey, false)]
    public void Done_is_true_only_for_sent_and_already_there(L1011FoOutcome outcome, bool done)
    {
        Assert.Equal(done, new L1011FoResult(outcome).Done);
    }

    [Fact]
    public void The_named_results_carry_their_outcome_and_no_reason()
    {
        Assert.Equal(L1011FoOutcome.Sent, L1011FoResult.Sent.Outcome);
        Assert.Equal(L1011FoOutcome.AlreadyThere, L1011FoResult.AlreadyThere.Outcome);
        Assert.Equal(L1011FoOutcome.Unavailable, L1011FoResult.Unavailable.Outcome);
        Assert.Equal(L1011FoOutcome.UnknownKey, L1011FoResult.UnknownKey.Outcome);
        Assert.Null(L1011FoResult.Sent.Reason);
        Assert.Null(L1011FoResult.AlreadyThere.Reason);
        Assert.Null(L1011FoResult.Unavailable.Reason);
        Assert.Null(L1011FoResult.UnknownKey.Reason);
    }

    [Fact]
    public void A_refusal_carries_its_reason_and_is_not_done()
    {
        var r = L1011FoResult.Refused("position unknown");
        Assert.Equal(L1011FoOutcome.Refused, r.Outcome);
        Assert.Equal("position unknown", r.Reason);
        Assert.False(r.Done);
    }

    [Fact]
    public void Results_compare_by_value()
    {
        Assert.Equal(L1011FoResult.Refused("x"), L1011FoResult.Refused("x"));
        Assert.NotEqual(L1011FoResult.Refused("x"), L1011FoResult.Refused("y"));
        Assert.NotEqual(L1011FoResult.Sent, L1011FoResult.AlreadyThere);
    }
}
