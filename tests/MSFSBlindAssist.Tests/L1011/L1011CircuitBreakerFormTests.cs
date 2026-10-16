using System.Windows.Forms;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The breaker window opens clean every time, as Ctrl+M does: a search or the pulled-only tick left from
/// the last open would show a short list for a reason the pilot cannot hear.</summary>
public class L1011CircuitBreakerFormTests
{
    [Fact]
    public void Every_open_clears_the_search_and_the_pulled_only_tick()
    {
        var breakers = new[]
        {
            new L1011Breaker { Index = 4, Title = "HYD IND QTY" },
            new L1011Breaker { Index = 12, Title = "" },
        };
        using var form = new L1011CircuitBreakerForm(breakers, new SimConnectManager(IntPtr.Zero), new SpeechCapture());
        var search = form.Controls.OfType<TextBox>().Single(t => t.AccessibleName == "Search");
        var pulledOnly = form.Controls.OfType<CheckBox>().Single();
        var list = form.Controls.OfType<DisplayListBox>().Single();
        search.Text = "HYD";
        pulledOnly.Checked = true;

        form.PrepareToShow();

        Assert.Equal("", search.Text);
        Assert.False(pulledOnly.Checked);
        Assert.Equal(new[] { "HYD IND QTY, breaker 4: unknown", "Breaker 12: unknown" },
            list.Items.Cast<object>().Select(o => o.ToString()));
    }
}
