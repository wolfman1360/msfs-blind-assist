using System.Windows.Forms;
using MSFSBlindAssist.Forms;
using MSFSBlindAssist.Forms.PMDG;

namespace MSFSBlindAssist.Tests;

/// <summary>
/// The shared Ctrl+P window's Alt-key letters press a button only with Alt held. A WinForms dialog also
/// fires a button's mnemonic on the plain letter while focus is on another button (the Win32 dialog
/// convention), so a stray G pressed the TriStar's TOGA button and a D the autopilot disconnect (seen live).
/// </summary>
public class AutopilotWindowMnemonicTests
{
    private const int WM_CHAR = 0x0102;
    private const int WM_SYSCHAR = 0x0106;   // what Windows sends for a letter typed with Alt held

    private sealed class Harness : IDisposable
    {
        public readonly PMDGAutopilotWindow Window;
        public int Presses;

        public Harness()
        {
            var buttons = new List<ToggleButtonDef>
            {
                new("&Flight director A", () => "Off", () => { }),
                new("Takeoff &go-around button", () => "", () => Presses++),
            };
            Window = new PMDGAutopilotWindow("Autopilot", buttons, Array.Empty<SelectorRowDef>())
            {
                // Shown only so its buttons can take a mnemonic: transparent, off-screen, out of the taskbar.
                ShowInTaskbar = false,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(-32000, -32000),
                Opacity = 0,
            };
            Window.Show();
            Window.RefreshStates();   // the buttons take their labels, and so their letters, here
            Window.Controls.OfType<Button>().First().Focus();
        }

        /// <summary>A character typed with focus on the first button, delivered the way the message loop does.</summary>
        public void Type(char c, int message)
        {
            var focused = Window.Controls.OfType<Button>().First();
            var msg = Message.Create(focused.Handle, message, (IntPtr)c, IntPtr.Zero);
            focused.PreProcessMessage(ref msg);
        }

        public void Dispose()
        {
            Window.Close();
            Window.Dispose();
        }
    }

    [Fact]
    public void A_plain_letter_on_a_focused_button_presses_nothing()
    {
        using var h = new Harness();
        h.Window.ModifierKeysSource = () => Keys.None;
        h.Type('g', WM_CHAR);
        Assert.Equal(0, h.Presses);
    }

    [Fact]
    public void The_letter_with_alt_presses_its_button()
    {
        using var h = new Harness();
        h.Window.ModifierKeysSource = () => Keys.Alt;
        h.Type('g', WM_SYSCHAR);
        Assert.Equal(1, h.Presses);
    }
}
