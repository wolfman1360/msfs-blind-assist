using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist;

/// <summary>
/// iniBuilds L-1011 TriStar windows and menu entry: the Ctrl+M monitor manager, the
/// circuit-breaker list, the INS window and the Paper Flight Bag tablets. Kept in its own partial
/// like the MD-11's.
/// </summary>
public partial class MainForm
{
    private Forms.L1011.L1011MonitorManagerForm? l1011MonitorManagerForm;
    private Forms.L1011.L1011CircuitBreakerForm? l1011CircuitBreakerForm;
    private Forms.L1011.L1011InsForm? l1011InsForm;
    private CoherentPmdgEfbClient? l1011EfbCaptain;
    private CoherentPmdgEfbClient? l1011EfbFirstOfficer;
    private Forms.FBWA380.FbwEfbForm? l1011EfbCaptainForm;
    private Forms.FBWA380.FbwEfbForm? l1011EfbFirstOfficerForm;

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

    /// <summary>
    /// The INS and PMS window (input-mode Shift+M, or a Center Console panel's "Open … window" button), on
    /// <paramref name="device"/> or on the unit it last showed. One window, hidden on close and reused, as
    /// the fleet's CDU windows are; guarded on the loaded aircraft like the monitor manager.
    /// </summary>
    public void ShowL1011InsDialog(Aircraft.L1011.L1011InsDevice? device)
    {
        if (currentAircraft is not IniL1011Definition l1011) return;
        hotkeyManager.ExitInputHotkeyMode();
        if (l1011InsForm == null || l1011InsForm.IsDisposed)
            l1011InsForm = new Forms.L1011.L1011InsForm(l1011, simConnectManager, announcer);
        l1011InsForm.ShowForm(device);
    }

    /// <summary>
    /// The Paper Flight Bag (input Shift+T for the captain's tablet, Ctrl+Shift+T for the first
    /// officer's) in the shared FbwEfbForm, read and driven through the Coherent debugger by
    /// coherent-l1011-efb-agent.js, as the fleet's tablets are (the A300's, the MD-11's, the PMDG's).
    /// One client per seat, created on first use and kept until the aircraft changes; it scrapes only
    /// while its window is visible. Guarded on the loaded aircraft like the monitor manager.
    /// </summary>
    public void ShowL1011EfbDialog(bool firstOfficer)
    {
        if (currentAircraft is not IniL1011Definition) return;
        hotkeyManager.ExitInputHotkeyMode();
        var client = firstOfficer ? l1011EfbFirstOfficer : l1011EfbCaptain;
        if (client == null)
        {
            client = CoherentPmdgEfbClient.ForL1011(firstOfficer);
            client.Start();
            if (firstOfficer) l1011EfbFirstOfficer = client; else l1011EfbCaptain = client;
        }
        var form = firstOfficer ? l1011EfbFirstOfficerForm : l1011EfbCaptainForm;
        if (form == null || form.IsDisposed)
        {
            form = new Forms.FBWA380.FbwEfbForm(client, announcer,
                "TriStar Paper Flight Bag" + (firstOfficer ? " (First Officer)" : ""), "Tablet", "Tablet");
            var shown = form;
            var gated = client;
            shown.VisibleChanged += (_, _) => gated.SetActive(!shown.IsDisposed && shown.Visible);
            if (firstOfficer) l1011EfbFirstOfficerForm = form; else l1011EfbCaptainForm = form;
        }
        client.SetActive(true);   // a re-show of an already visible window raises no VisibleChanged
        form.ShowForm();
    }

    /// <summary>Hands the definition what it needs from MainForm. Called on every path that makes
    /// the TriStar the current aircraft (start-up and the aircraft switch).</summary>
    private void AttachL1011(IniL1011Definition l1011)
    {
        l1011.Attach(simConnectManager);
        l1011.OpenCircuitBreakers = ShowL1011CircuitBreakerDialog;
        l1011.OpenInsWindow = ShowL1011InsDialog;
    }

    /// <summary>Disposes the TriStar windows (their rows and breaker list belong to one definition) and the
    /// tablet clients, windows first, so no window outlives the socket it reads through.</summary>
    private void DisposeL1011Windows()
    {
        if (l1011MonitorManagerForm != null && !l1011MonitorManagerForm.IsDisposed) l1011MonitorManagerForm.Dispose();
        l1011MonitorManagerForm = null;
        if (l1011CircuitBreakerForm != null && !l1011CircuitBreakerForm.IsDisposed) l1011CircuitBreakerForm.Dispose();
        l1011CircuitBreakerForm = null;
        // The INS window's reader holds an INS view's one inspector socket.
        if (l1011InsForm != null && !l1011InsForm.IsDisposed) l1011InsForm.Dispose();
        l1011InsForm = null;
        // Each tablet client holds the ONE inspector socket Coherent allows for its seat's view.
        if (l1011EfbCaptainForm != null && !l1011EfbCaptainForm.IsDisposed) l1011EfbCaptainForm.Dispose();
        l1011EfbCaptainForm = null;
        if (l1011EfbFirstOfficerForm != null && !l1011EfbFirstOfficerForm.IsDisposed) l1011EfbFirstOfficerForm.Dispose();
        l1011EfbFirstOfficerForm = null;
        l1011EfbCaptain?.Stop();
        l1011EfbCaptain?.Dispose();
        l1011EfbCaptain = null;
        l1011EfbFirstOfficer?.Stop();
        l1011EfbFirstOfficer?.Dispose();
        l1011EfbFirstOfficer = null;
    }

    private void IniL1011MenuItem_Click(object? sender, EventArgs e)
    {
        SwitchAircraft(new IniL1011Definition());
    }
}
