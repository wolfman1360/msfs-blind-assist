using System.Windows.Forms;
using MSFSBlindAssist.Forms.L1011;
using static MSFSBlindAssist.Forms.L1011.L1011InsKeyRouting;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The INS window's form-wide keys, as the fleet's CDU windows bind them (the A300 MCDU window's
/// routing): line keys, paging, Insert, focus and Escape, never a chord the focused unit combo needs.</summary>
public class L1011InsKeyRoutingTests
{
    private static L1011InsKeyRouting.Action On(Keys key, bool alt = false, bool ctrl = false, bool shift = false,
        ComboFocus combo = ComboFocus.None, bool alternate = false, bool pms = true) =>
        Resolve(key, alt, ctrl, shift, combo, alternate, pms);

    [Theory]
    [InlineData(Keys.D1, 1)]
    [InlineData(Keys.D6, 6)]
    public void Ctrl_digits_press_the_pms_line_keys(Keys key, int line)
    {
        Assert.Equal(new L1011InsKeyRouting.Action(ActionKind.LineKey, line), On(key, ctrl: true));
    }

    [Fact]
    public void The_alternate_layout_uses_f1_to_f6()
    {
        Assert.Equal(new L1011InsKeyRouting.Action(ActionKind.LineKey, 1), On(Keys.F1, alternate: true));
        Assert.Equal(new L1011InsKeyRouting.Action(ActionKind.LineKey, 6), On(Keys.F6, alternate: true));
        Assert.Equal(ActionKind.Pass, On(Keys.D1, ctrl: true, alternate: true).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.F7, alternate: true).Kind);   // the PMS has no right line keys
    }

    [Fact]
    public void An_ins_has_no_line_keys_and_no_pages()
    {
        Assert.Equal(ActionKind.Pass, On(Keys.D1, ctrl: true, pms: false).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.PageDown, pms: false).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.Right, alt: true, pms: false).Kind);
    }

    [Fact]
    public void Paging_on_a_pms()
    {
        Assert.Equal(ActionKind.PreviousPage, On(Keys.PageUp).Kind);
        Assert.Equal(ActionKind.NextPage, On(Keys.PageDown).Kind);
        Assert.Equal(ActionKind.NextPage, On(Keys.Right, alt: true).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.PageDown, ctrl: true).Kind);   // a binding matches its own chord only
    }

    [Fact]
    public void Ctrl_enter_presses_insert_on_any_unit()
    {
        Assert.Equal(ActionKind.Insert, On(Keys.Return, ctrl: true).Kind);
        Assert.Equal(ActionKind.Insert, On(Keys.Return, ctrl: true, pms: false).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.Return).Kind);   // plain Enter is the focused control's
    }

    [Fact]
    public void Focus_keys_and_escape()
    {
        Assert.Equal(ActionKind.FocusInput, On(Keys.S, alt: true).Kind);
        Assert.Equal(ActionKind.FocusDisplay, On(Keys.Home, alt: true).Kind);
        Assert.Equal(ActionKind.Close, On(Keys.Escape).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.Escape, shift: true).Kind);
    }

    [Fact]
    public void A_chord_the_focused_combo_needs_is_never_taken()
    {
        Assert.Equal(ActionKind.Pass, On(Keys.PageDown, combo: ComboFocus.Closed).Kind);
        Assert.Equal(ActionKind.Pass, On(Keys.Escape, combo: ComboFocus.Dropped).Kind);
        Assert.Equal(ActionKind.Close, On(Keys.Escape, combo: ComboFocus.Closed).Kind);
        Assert.Equal(ActionKind.LineKey, On(Keys.D2, ctrl: true, combo: ComboFocus.Closed).Kind);
    }

    [Fact]
    public void A_selector_the_pilot_is_in_is_never_repainted()
    {
        // The reported position lags a commit by up to one batch (about a second): repainting a combo the
        // pilot is arrowing through would say the new position, the old one, then the new one again.
        Assert.True(RepaintsSelector(focused: false, droppedDown: false));
        Assert.False(RepaintsSelector(focused: true, droppedDown: false));
        Assert.False(RepaintsSelector(focused: false, droppedDown: true));
        Assert.False(RepaintsSelector(focused: true, droppedDown: true));
    }
}
