using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.L1011;

/// <summary>
/// The TriStar's circuit breakers as one searchable list ("HYD IND QTY: in"). States are read all at
/// once through the Coherent debugger (<see cref="L1011CircuitBreakers.BulkReadScript"/>) when the
/// window opens and on Refresh; Space or Enter on a row pulls or pushes it in by replaying the
/// cockpit's own click. The changed row updates in place, so the screen reader reads the new state
/// itself and nothing is announced on top of it; only a failure is spoken.
/// </summary>
public sealed class L1011CircuitBreakerForm : Form
{
    /// <summary>How long after a pull or push the window re-reads the breakers to confirm it.</summary>
    public const int ConfirmReadDelayMs = 1500;

    private readonly IReadOnlyList<L1011Breaker> _breakers;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;

    private bool[]? _states;
    private IReadOnlyList<int> _visible = Array.Empty<int>();
    private bool _reading;

    private TextBox _search = null!;
    private CheckBox _pulledOnly = null!;
    private DisplayListBox _list = null!;
    private TextBox _status = null!;

    public L1011CircuitBreakerForm(IReadOnlyList<L1011Breaker> breakers, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _breakers = breakers;
        _sim = sim;
        _announcer = announcer;
        BuildUi();
        MonitorManagerShared.HideOnClose(this);
    }

    private void BuildUi()
    {
        Text = "TriStar Circuit Breakers";
        Size = new Size(560, 480);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        KeyPreview = true;

        var searchLabel = new Label { Text = "&Search:", Location = new Point(10, 12), Size = new Size(60, 22), TabIndex = 0 };
        _search = new TextBox
        {
            Location = new Point(75, 10), Size = new Size(260, 22), TabIndex = 1,
            AccessibleName = "Search",
            AccessibleDescription = "Type to list only the breakers whose name contains the text",
        };
        _search.TextChanged += (_, _) => Rebuild();

        _pulledOnly = new CheckBox
        {
            Text = "Show &pulled breakers only", Location = new Point(345, 10), Size = new Size(200, 24), TabIndex = 2,
        };
        _pulledOnly.CheckedChanged += (_, _) => Rebuild();

        _list = new DisplayListBox
        {
            Location = new Point(10, 42), Size = new Size(530, 300), TabIndex = 3,
            AccessibleName = "Circuit breakers",
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Space or Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                ToggleSelected();
            }
        };

        _status = new TextBox
        {
            Location = new Point(10, 350), Size = new Size(530, 22), TabIndex = 4, ReadOnly = true,
            AccessibleName = "Breaker summary",
        };

        var toggle = new Button { Text = "Pull or push &in", Location = new Point(10, 385), Size = new Size(150, 28), TabIndex = 5 };
        toggle.Click += (_, _) => ToggleSelected();
        var refresh = new Button { Text = "&Refresh", Location = new Point(170, 385), Size = new Size(100, 28), TabIndex = 6 };
        refresh.Click += async (_, _) => await ReadAsync(announceFailure: true);
        var close = new Button { Text = "&Close", Location = new Point(440, 385), Size = new Size(100, 28), TabIndex = 7 };
        close.Click += (_, _) => Close();
        CancelButton = close;

        Controls.AddRange(new Control[] { searchLabel, _search, _pulledOnly, _list, _status, toggle, refresh, close });
    }

    /// <summary>Shows the window with the list focused and reads every breaker's state.</summary>
    public async void ShowForm()
    {
        Rebuild();
        Show();
        Activate();
        _list.Focus();
        await ReadAsync(announceFailure: true);
    }

    private async Task ReadAsync(bool announceFailure)
    {
        if (_reading || _breakers.Count == 0)
            return;
        _reading = true;
        try
        {
            string result = await CoherentEvalClient.EvalAsync(L1011CircuitBreakers.ViewNeedle,
                L1011CircuitBreakers.BulkReadScript(_breakers));
            var states = L1011CircuitBreakers.ParseStates(result, _breakers.Count);
            if (IsDisposed)
                return;
            if (states == null)
            {
                _status.Text = "Breaker states unavailable: the cockpit's engineer panel did not answer.";
                if (announceFailure)
                    _announcer.Announce("Circuit breakers could not be read");
                return;
            }
            _states = states;
            Rebuild();
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"Breaker read failed: {ex.Message}");
        }
        finally
        {
            _reading = false;
        }
    }

    private void Rebuild()
    {
        if (IsDisposed)
            return;
        _visible = L1011CircuitBreakers.Visible(_breakers, _states, _search.Text, _pulledOnly.Checked);
        var lines = _visible.Select(i => L1011CircuitBreakers.ItemText(_breakers[i], _states?[i])).ToList();
        if (lines.Count == 0)
            lines.Add(_pulledOnly.Checked ? "No pulled breakers" : "No breakers match");
        DisplayList.UpdateInPlace(_list, lines);
        if (_list.SelectedIndex < 0 && _list.Items.Count > 0)
            _list.SelectedIndex = 0;
        _status.Text = _states == null
            ? $"{_breakers.Count} breakers, states not read yet"
            : L1011CircuitBreakers.Summary(_breakers.Count, _states.Count(pulled => pulled));
    }

    private void ToggleSelected()
    {
        int row = _list.SelectedIndex;
        if (row < 0 || row >= _visible.Count)
            return;
        int index = _visible[row];
        var breaker = _breakers[index];
        bool pull = _states?[index] != true;
        var plan = L1011WritePlan.ForBreaker(breaker, pull);
        if (plan.Refusal != null || !_sim.CalcWriteCanLand)
        {
            _announcer.Announce($"{L1011CircuitBreakers.DisplayName(breaker)} unavailable");
            return;
        }
        foreach (var step in plan.Steps)
            if (step is L1011CalcStep calc)
                _sim.ExecuteCalculatorCodeUnique(calc.Rpn);

        // Show the commanded state at once (the screen reader reads the changed row), then confirm.
        if (_states != null)
        {
            _states[index] = pull;
            Rebuild();
        }
        _ = ConfirmAsync();
    }

    private async Task ConfirmAsync()
    {
        await Task.Delay(ConfirmReadDelayMs);
        if (!IsDisposed && Visible)
            await ReadAsync(announceFailure: false);
    }
}
