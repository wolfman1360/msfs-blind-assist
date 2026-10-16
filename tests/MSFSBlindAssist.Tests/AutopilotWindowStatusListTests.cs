using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.PMDG;

namespace MSFSBlindAssist.Tests;

/// <summary>The shared Ctrl+P window's optional status list (the A300's), and that the PMDG window,
/// which passes none, is built exactly as before.</summary>
public class AutopilotWindowStatusListTests
{
    private static List<ToggleButtonDef> Buttons() => new() { new("&AP", () => "On", () => { }) };

    [Fact]
    public void Without_a_status_provider_there_is_no_list_and_the_first_button_comes_first()
    {
        using var w = new PMDGAutopilotWindow("737 Autopilot", Buttons(), Array.Empty<SelectorRowDef>());
        Assert.Empty(w.Controls.OfType<ListBox>());
        Assert.IsType<Button>(w.Controls.Cast<Control>().OrderBy(c => c.TabIndex).First());
    }

    [Fact]
    public void A_status_provider_adds_a_named_list_first_in_the_tab_order_with_the_first_line_selected()
    {
        using var w = new PMDGAutopilotWindow("A300 Autopilot", Buttons(), Array.Empty<SelectorRowDef>(),
            () => new[] { "Speed 250 knots", "Autopilot: CMD 1" }, "Autopilot status");
        var list = Assert.Single(w.Controls.OfType<ListBox>());
        Assert.Equal("Autopilot status", list.AccessibleName);
        Assert.Equal(0, list.TabIndex);
        w.RefreshStates();
        Assert.Equal(new[] { "Speed 250 knots", "Autopilot: CMD 1" }, list.Items.Cast<string>());
        Assert.Equal(0, list.SelectedIndex);
    }

    private static SelectorRowDef[] Selectors() => new[]
    {
        new SelectorRowDef("Autopilot A", new Dictionary<double, string> { [0] = "Command", [1] = "Off" }, () => 1, _ => { }, 'A'),
    };

    /// <summary>The window's controls in tab order, skipping the labels (they are not tab stops).</summary>
    private static List<Control> TabOrder(Form w) =>
        w.Controls.Cast<Control>().Where(c => c is not Label).OrderBy(c => c.TabIndex).ToList();

    [Fact]
    public void By_default_the_buttons_come_before_the_combos()
    {
        using var w = new PMDGAutopilotWindow("737 Autopilot", Buttons(), Selectors());
        var order = TabOrder(w);
        Assert.IsType<Button>(order[0]);
        Assert.IsType<ComboBox>(order[1]);
    }

    [Fact]
    public void With_selectors_first_the_combos_come_straight_after_the_status_list()
    {
        using var w = new PMDGAutopilotWindow("TriStar Autopilot", Buttons(), Selectors(),
            () => new[] { "Speed: IAS 250" }, "Autopilot status", selectorsFirst: true);
        w.RefreshStates();
        var order = TabOrder(w);
        Assert.IsType<ListBox>(order[0]);
        Assert.Equal("Autopilot A", Assert.IsType<ComboBox>(order[1]).AccessibleName);
        Assert.Equal("&AP: On", Assert.IsType<Button>(order[2]).Text);
        // The combo's Alt key is its label's mnemonic, which focuses the NEXT control in tab order.
        var label = w.Controls.OfType<Label>().Single();
        Assert.Equal(order[1].TabIndex - 1, label.TabIndex);
    }

    [Fact]
    public void A_refresh_rewrites_changed_lines_in_place_and_keeps_the_readers_line()
    {
        var lines = new List<string> { "Speed 250 knots", "Heading 270" };
        using var w = new PMDGAutopilotWindow("A300 Autopilot", Buttons(), Array.Empty<SelectorRowDef>(),
            () => lines.ToArray(), "Autopilot status");
        var list = w.Controls.OfType<ListBox>().Single();
        w.RefreshStates();
        list.SelectedIndex = 1;
        lines[0] = "Speed 260 knots";
        w.RefreshStates();
        Assert.Equal("Speed 260 knots", list.Items[0]);
        Assert.Equal(1, list.SelectedIndex);
    }
}
