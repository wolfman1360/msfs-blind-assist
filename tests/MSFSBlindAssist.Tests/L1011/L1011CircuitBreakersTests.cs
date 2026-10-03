using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011CircuitBreakersTests
{
    private static readonly IReadOnlyList<L1011Breaker> Breakers = L1011ControlMap.Load().Breakers;

    [Fact]
    public void Names_fall_back_to_the_number()
    {
        Assert.Equal("HYD IND QTY: in", L1011CircuitBreakers.ItemText(new L1011Breaker { Index = 4, Title = "HYD IND QTY" }, false));
        Assert.Equal("Breaker 12: pulled", L1011CircuitBreakers.ItemText(new L1011Breaker { Index = 12, Title = "" }, true));
        Assert.Equal("Breaker 12: unknown", L1011CircuitBreakers.ItemText(new L1011Breaker { Index = 12 }, null));
    }

    [Fact]
    public void The_bulk_read_script_names_every_breaker_in_order()
    {
        string js = L1011CircuitBreakers.BulkReadScript(Breakers);
        Assert.StartsWith("(function(){var n=[\"V_C_Breaker_001\"", js);
        Assert.Contains("\"V_C_Breaker_999\"]", js); // numbering runs 1 to 999 with 18 gaps
        Assert.DoesNotContain("=>", js);
    }

    [Fact]
    public void States_parse_only_when_complete()
    {
        Assert.Equal(new[] { false, true, false }, L1011CircuitBreakers.ParseStates("010", 3));
        Assert.Null(L1011CircuitBreakers.ParseStates("01", 3));
        Assert.Null(L1011CircuitBreakers.ParseStates("0x1", 3));
        Assert.Null(L1011CircuitBreakers.ParseStates("", 3));
    }

    [Fact]
    public void The_list_filters_by_name_and_by_pulled_state()
    {
        var breakers = new[]
        {
            new L1011Breaker { Index = 1, Title = "HYD IND QTY" },
            new L1011Breaker { Index = 2, Title = "RADIO ALTM 2" },
            new L1011Breaker { Index = 3, Title = "" },
        };
        var states = new[] { false, true, false };
        Assert.Equal(new[] { 0, 1, 2 }, L1011CircuitBreakers.Visible(breakers, states, "", pulledOnly: false));
        Assert.Equal(new[] { 1 }, L1011CircuitBreakers.Visible(breakers, states, "altm", pulledOnly: false));
        Assert.Equal(new[] { 2 }, L1011CircuitBreakers.Visible(breakers, states, "breaker 3", pulledOnly: false));
        Assert.Equal(new[] { 1 }, L1011CircuitBreakers.Visible(breakers, states, null, pulledOnly: true));
        Assert.Empty(L1011CircuitBreakers.Visible(breakers, null, null, pulledOnly: true));
    }

    [Fact]
    public void Summary_wording()
    {
        Assert.Equal("981 breakers, all in", L1011CircuitBreakers.Summary(981, 0));
        Assert.Equal("981 breakers, 2 pulled", L1011CircuitBreakers.Summary(981, 2));
    }
}
