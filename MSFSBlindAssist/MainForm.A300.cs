using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>
/// iniBuilds A300 windows and menu entry: the Ctrl+M monitor manager. Kept in its own partial like
/// the MD-11's.
/// </summary>
public partial class MainForm
{
    private Forms.A300.A300MonitorManagerForm? a300MonitorManagerForm;

    /// <summary>
    /// The A300's monitor manager (Ctrl+M). Guarded on the loaded aircraft because the definition
    /// calls it: a queued call that runs after a switch must not build an "A300" manager over
    /// another aircraft's variables.
    /// </summary>
    public void ShowA300MonitorManagerDialog()
    {
        if (currentAircraft is not IniA300Definition a300) return;
        hotkeyManager.ExitOutputHotkeyMode();
        if (a300MonitorManagerForm == null || a300MonitorManagerForm.IsDisposed)
            a300MonitorManagerForm = new Forms.A300.A300MonitorManagerForm(a300.GetVariables());
        a300MonitorManagerForm.ShowForm();
    }

    /// <summary>Disposes the A300 windows: their rows belong to one definition.</summary>
    private void DisposeA300Windows()
    {
        if (a300MonitorManagerForm != null && !a300MonitorManagerForm.IsDisposed) a300MonitorManagerForm.Dispose();
        a300MonitorManagerForm = null;
    }

    private void IniA300MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniA300Definition());
    }
}
