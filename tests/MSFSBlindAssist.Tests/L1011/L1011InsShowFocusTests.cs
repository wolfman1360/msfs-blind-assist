using MSFSBlindAssist.Forms.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// The INS window opens with the cursor on its display, every time it is shown. WinForms (and Windows) restore
/// the control that had focus when the window was hidden as the window activates; live (2026-10-16) Escape then
/// Shift+M put the screen reader on the display and then on the typing box. The form now sets the display as the
/// active control BEFORE it shows the window, so the activation inside Show lands there, and takes the first
/// activation after a show to put it there again if the activation is delivered later. This is that one-shot: the
/// activation that follows a show takes focus to the display once; a later Alt+Tab back to the window does not.
/// </summary>
public class L1011InsShowFocusTests
{
    private const long Start = 80_000;

    [Fact]
    public void The_first_activation_after_a_show_takes_focus_to_the_display_once()
    {
        var focus = new L1011InsShowFocus();
        focus.Arm(Start);
        Assert.True(focus.TakeOnActivation(Start + 20));
        Assert.False(focus.TakeOnActivation(Start + 40));   // once
    }

    [Fact]
    public void An_activation_with_no_show_before_it_leaves_focus_where_the_pilot_put_it()
    {
        var focus = new L1011InsShowFocus();
        Assert.False(focus.TakeOnActivation(Start));        // Alt+Tab back into the open window
    }

    [Fact]
    public void An_activation_long_after_the_show_is_not_the_shows_activation()
    {
        // Shift+M on a window that is already active raises no activation; the next one, whenever the pilot
        // comes back to the window, must not pull focus off the typing box.
        var focus = new L1011InsShowFocus();
        focus.Arm(Start);
        Assert.False(focus.TakeOnActivation(Start + L1011InsShowFocus.WindowMs + 1));
        Assert.False(focus.TakeOnActivation(Start + L1011InsShowFocus.WindowMs + 2));
    }

    [Fact]
    public void An_activation_exactly_at_the_end_of_the_window_still_counts()
    {
        var focus = new L1011InsShowFocus();
        focus.Arm(Start);
        Assert.True(focus.TakeOnActivation(Start + L1011InsShowFocus.WindowMs));
    }

    [Fact]
    public void A_second_show_arms_it_again()
    {
        var focus = new L1011InsShowFocus();
        focus.Arm(Start);
        Assert.True(focus.TakeOnActivation(Start + 10));
        focus.Arm(Start + 5_000);
        Assert.True(focus.TakeOnActivation(Start + 5_010));
    }

    [Fact]
    public void A_hide_cancels_a_pending_show()
    {
        var focus = new L1011InsShowFocus();
        focus.Arm(Start);
        focus.Cancel();
        Assert.False(focus.TakeOnActivation(Start + 10));
    }
}
