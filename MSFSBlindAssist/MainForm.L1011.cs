using MSFSBlindAssist.Aircraft;

namespace MSFSBlindAssist;

/// <summary>
/// iniBuilds L-1011 TriStar windows and menu entry: the Ctrl+M monitor manager and the
/// circuit-breaker list. Kept in its own partial like the MD-11's.
/// </summary>
public partial class MainForm
{
    private Forms.L1011.L1011MonitorManagerForm? l1011MonitorManagerForm;
    private Forms.L1011.L1011CircuitBreakerForm? l1011CircuitBreakerForm;

    /// <summary>
    /// The TriStar's monitor manager (Ctrl+M). Guarded on the loaded aircraft because the definition
    /// calls it: a queued call that runs after a switch must not build a "TriStar" manager over
    /// another aircraft's variables.
    /// </summary>
    public void ShowL1011MonitorManagerDialog()
    {
        if (currentAircraft is not IniL1011Definition l1011) return;
        hotkeyManager.ExitOutputHotkeyMode();
        if (l1011MonitorManagerForm == null || l1011MonitorManagerForm.IsDisposed)
            l1011MonitorManagerForm = new Forms.L1011.L1011MonitorManagerForm(l1011.GetVariables());
        l1011MonitorManagerForm.ShowForm();
    }

    /// <summary>The circuit-breaker list, opened from the Engineer Station's Circuit Breakers panel.</summary>
    public void ShowL1011CircuitBreakerDialog()
    {
        if (currentAircraft is not IniL1011Definition l1011) return;
        hotkeyManager.ExitInputHotkeyMode();
        if (l1011CircuitBreakerForm == null || l1011CircuitBreakerForm.IsDisposed)
            l1011CircuitBreakerForm = new Forms.L1011.L1011CircuitBreakerForm(l1011.Breakers, simConnectManager, announcer);
        l1011CircuitBreakerForm.ShowForm();
    }

    /// <summary>Hands the definition what it needs from MainForm. Called on every path that makes
    /// the TriStar the current aircraft (start-up and the aircraft switch).</summary>
    private void AttachL1011(IniL1011Definition l1011)
    {
        l1011.Attach(simConnectManager);
        l1011.OpenCircuitBreakers = ShowL1011CircuitBreakerDialog;
    }

    /// <summary>Disposes the TriStar windows: their rows and breaker list belong to one definition.</summary>
    private void DisposeL1011Windows()
    {
        if (l1011MonitorManagerForm != null && !l1011MonitorManagerForm.IsDisposed) l1011MonitorManagerForm.Dispose();
        l1011MonitorManagerForm = null;
        if (l1011CircuitBreakerForm != null && !l1011CircuitBreakerForm.IsDisposed) l1011CircuitBreakerForm.Dispose();
        l1011CircuitBreakerForm = null;
    }

    private void IniL1011MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniL1011Definition());
    }
}
