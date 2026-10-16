using System.Globalization;
using System.Runtime.InteropServices;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Forms.L1011;

/// <summary>
/// The TriStar's three INS units and two PMS units as text (Shift+M in input mode, or a Center Console
/// panel's "Open … window" button), built on the fleet's CDU windows (the A300 and MD-11 MCDU windows, the
/// PMDG CDUs): ONE window, owned by MainForm, hidden on close and reused; the display first, then the input
/// box, the unit combo and the unit's own buttons; a PMS page change is spoken by its title and puts the
/// cursor on line 1; what was typed is read back once it has landed (<see cref="CduScratchpadAnnouncer"/>);
/// the window's keys never take a chord the focused combo needs (<see cref="L1011InsKeyRouting"/>).
///
/// The display is read through the Coherent debugger from the unit's own view ("L1011_INS_1" carries INS 1
/// and the captain's PMS, "L1011_INS_2" INS 2 and the first officer's PMS, "L1011_INS_3" INS 3). The reader
/// holds that one view's socket only while the window is visible ([L1011-13]); nothing else is spoken: the
/// screen reader reads every control (the automatic waypoint loading button's label shows the state the INS
/// reports, and the reader says it when it changes), and the definition speaks errors and typed-value
/// confirmations.
/// </summary>
public sealed class L1011InsForm : Form
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    /// <summary>How often the reader scrapes the view, and the window polls, while it is shown.</summary>
    public const int RefreshMs = 500;

    /// <summary>A unit switch moves the reader to the new view only after this long without another switch,
    /// so arrowing through the unit combo does not open a socket per unit.</summary>
    private const int ViewSwitchMs = 300;

    /// <summary>Beyond the last chained key: the gauge drawing it and the next scrape. A margin, not a
    /// measurement (the A300 window's value).</summary>
    private const int SettleMarginMs = 400;

    private const string AgentFile = "coherent-l1011-ins-agent.js";

    private readonly IniL1011Definition _def;
    private readonly SimConnectManager _sim;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly System.Windows.Forms.Timer _poll = new() { Interval = RefreshMs };
    private readonly System.Windows.Forms.Timer _viewSwitch = new() { Interval = ViewSwitchMs };
    private readonly L1011InsWindowAnnouncer _changes = new();
    private readonly CduScratchpadAnnouncer _entry = new(L1011InsWindowCallouts.EntryClosedWords, stablePolls: 2);

    /// <summary>The window was just shown: the first activation puts the cursor on the display.</summary>
    private readonly L1011InsShowFocus _showFocus = new();

    /// <summary>The entry line spoken on the first poll that saw it (a PMS line the unit erases within about a
    /// second: <see cref="L1011InsDevices.EntryReadsAtOnce"/>); null while no such line shows, so the next one
    /// is spoken again, and never fed to <see cref="_entry"/>, so it is never said twice.</summary>
    private string? _atOnceSpoken;

    /// <summary>A PMS page as it read while an entry was open, and the entry's line, so the entry closing
    /// reads back what was stored (<see cref="L1011InsDevices.InsertReadback"/>); null while none is open.</summary>
    private (IReadOnlyList<string> Lines, int Line)? _openEntry;
    private IntPtr _previousWindow = IntPtr.Zero;

    private DisplayListBox _display = null!;
    private TextBox _input = null!;
    private ComboBox _unit = null!;
    private FlowLayoutPanel _insControls = null!;
    private FlowLayoutPanel _pmsControls = null!;
    private ComboBox _displaySelector = null!;
    private ComboBox _waypointSelector = null!;
    private ComboBox _autoSelector = null!;
    private Button _autoBatch = null!;
    private DisplayListBox _waypoints = null!;
    private readonly Button[] _lineKeys = new Button[6];

    /// <summary>A page key's selector move, said once the aircraft reports it.</summary>
    private L1011InsPages.ReadBack _pageReadBack = new();

    private CoherentDisplayClient? _client;
    private string? _clientNeedle;
    private L1011InsDevice _device = L1011InsDevice.Ins1;
    private L1011InsScrapeResult? _scrape;
    private IReadOnlyList<string>? _shown;
    private bool _updating;
    private bool _shownOnce;

    public L1011InsForm(IniL1011Definition def, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _sim = sim;
        _announcer = announcer;
        BuildLayout();
        MonitorManagerShared.HideOnClose(this, () =>
        {
            if (_previousWindow != IntPtr.Zero) SetForegroundWindow(_previousWindow);
        });
        _poll.Tick += (_, _) => Poll();
        _viewSwitch.Tick += (_, _) =>
        {
            _viewSwitch.Stop();
            if (Visible) StartReader();
        };
        SwitchTo(_device);   // the first unit's selectors and words: a first open without a unit switch fills them too
    }

    private int Unit => L1011InsDevices.Unit(_device) ?? 1;

    private string Seat => L1011InsDevices.Seat(_device) ?? "CPT";

    private string DisplaySelectorKey => $"INS_{Unit.ToString(CultureInfo.InvariantCulture)}_ROTARY_MODE";

    private static bool AlternateLineKeys => MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys;

    // ---------------------------------------------------------------------------------
    // Layout
    // ---------------------------------------------------------------------------------

    private void BuildLayout()
    {
        SuspendLayout();
        Text = "TriStar INS and PMS";
        AccessibleName = "TriStar INS and PMS";
        ClientSize = new Size(640, 760);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        ShowInTaskbar = false;

        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };

        _display = new DisplayListBox
        {
            Size = new Size(600, 220),
            AccessibleName = "INS 1 display",
            AccessibleDescription = "The unit's screen, one line at a time. Updates live.",
            IntegralHeight = false,
        };
        // Named only, as the PMDG CDU window's "Scratchpad" is: a description is read on every visit. The
        // keys it takes are in the hotkey guide.
        _input = new TextBox { Width = 600, AccessibleName = "Keypad input" };
        _unit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "Unit" };
        foreach (var device in L1011InsDevices.All)
            _unit.Items.Add(L1011InsDevices.Name(device));
        _unit.SelectedIndex = 0;

        _insControls = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
        AddButton(_insControls, "&Insert", () => Press(L1011InsDevices.InsertKey(_device)));
        AddButton(_insControls, "&Clear", () => Press(L1011InsDevices.ClearKey(_device)));
        AddButton(_insControls, "&Hold", () => Press(L1011InsKeys.Hold(Unit)));
        AddButton(_insControls, "&Track change", () => Press(L1011InsKeys.TrackChange(Unit)));
        _displaySelector = AddSelector(_insControls, "Display selector");
        _waypointSelector = AddSelector(_insControls, "Waypoint selector");
        _autoSelector = AddSelector(_insControls, "Auto, manual, remote selector");
        AddButton(_insControls, "Enter &present position", EnterPresentPosition);
        _autoBatch = AddButton(_insControls, "Automatic waypoint loading: unknown", ToggleAutoBatch);
        _waypoints = new DisplayListBox
        {
            Size = new Size(600, 180),
            AccessibleName = "INS 1 waypoints",
            IntegralHeight = false,
            TabIndex = _insControls.Controls.Count,
        };
        _insControls.Controls.Add(_waypoints);

        _pmsControls = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Visible = false };
        AddButton(_pmsControls, "&Insert", () => Press(L1011InsDevices.InsertKey(_device)));
        AddButton(_pmsControls, "&Clear", () => Press(L1011InsDevices.ClearKey(_device)));
        AddButton(_pmsControls, "&Next page", () => Press(L1011InsKeys.PmsKey(Seat, "FWD")));
        AddButton(_pmsControls, "Pre&vious page", () => Press(L1011InsKeys.PmsKey(Seat, "BACK")));
        AddButton(_pmsControls, "Performance &management", () => Press(L1011InsKeys.PmsKey(Seat, "PERF_MGT")));
        for (int line = 1; line <= 6; line++)
        {
            int n = line;
            _lineKeys[n - 1] = AddButton(_pmsControls, L1011PmsDisplay.LineKeyLabel(null, n, AlternateLineKeys),
                () => Press(L1011InsKeys.PmsLineKey(Seat, n)));
        }

        layout.Controls.Add(_display);
        layout.Controls.Add(_input);
        layout.Controls.Add(_unit);
        layout.Controls.Add(_insControls);
        layout.Controls.Add(_pmsControls);
        int tab = 0;
        _display.TabIndex = tab++;
        _input.TabIndex = tab++;
        _unit.TabIndex = tab++;
        _insControls.TabIndex = tab++;
        _pmsControls.TabIndex = tab++;
        Controls.Add(layout);

        _unit.SelectionChangeCommitted += (_, _) =>
        {
            if (!_updating && _unit.SelectedIndex >= 0)
                SwitchTo(L1011InsDevices.All[_unit.SelectedIndex]);   // silent: the reader said the new unit
        };
        _display.KeyDown += Display_KeyDown;
        _input.KeyDown += Input_KeyDown;
        KeyDown += Form_KeyDown;
        ResumeLayout(false);
    }

    private static Button AddButton(Control parent, string text, System.Action click)
    {
        var button = new Button { Text = text, Width = 300, Height = 28, TabIndex = parent.Controls.Count };
        button.Click += (_, _) => click();
        parent.Controls.Add(button);
        return button;
    }

    private ComboBox AddSelector(Control parent, string name)
    {
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = name, TabIndex = parent.Controls.Count };
        combo.SelectionChangeCommitted += (_, _) =>
        {
            if (_updating || combo.SelectedIndex < 0 || combo.Tag is not string key)
                return;
            var target = _def.InsSelectorPositions(key).OrderBy(p => p.Key).ElementAt(combo.SelectedIndex).Key;
            _def.SetInsSelector(key, target, _sim, _announcer);
        };
        parent.Controls.Add(combo);
        return combo;
    }

    // ---------------------------------------------------------------------------------
    // Units and the reader
    // ---------------------------------------------------------------------------------

    /// <summary>Shows the window on a unit (a panel button), or on the one it last showed (Shift+M), with the
    /// cursor on its display. A re-show is silent.</summary>
    public void ShowForm(L1011InsDevice? device)
    {
        _previousWindow = GetForegroundWindow();
        if (device is L1011InsDevice d && d != _device)
            SwitchTo(d);
        // The display is chosen BEFORE the window is shown: Show activates it, and WinForms hands focus to the
        // active control as it does, which would otherwise be the control that had focus when the window was
        // hidden (the typing box, after Escape from it): the screen reader read the display and then the box
        // (live, 2026-10-16). The first activation that follows is held to the display too (OnActivated), for
        // an activation Windows delivers after this method has returned.
        ActiveControl = _display;
        _showFocus.Arm(Environment.TickCount64);
        Show();
        BringToFront();
        Activate();
        TopMost = true;
        TopMost = false;
        if (!_shownOnce && _display.Items.Count > 0)
            _display.SelectedIndex = 0;   // a first open names the unit through the reader's read of the row
        _shownOnce = true;
        ActiveControl = _display;
        _display.Focus();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (_showFocus.TakeOnActivation(Environment.TickCount64))
        {
            ActiveControl = _display;
            _display.Focus();
        }
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            _changes.Reset();
            _entry.Reset();
            _atOnceSpoken = null;
            _openEntry = null;
            StartReader();
            _poll.Start();
            Repaint();
        }
        else
        {
            _showFocus.Cancel();
            _poll.Stop();
            _viewSwitch.Stop();
            StopReader();
        }
    }

    /// <summary>Moves the window to another unit: its view, its controls and its words, silently.</summary>
    private void SwitchTo(L1011InsDevice device)
    {
        string before = L1011InsDevices.ViewNeedle(_device);
        _device = device;
        bool pms = L1011InsDevices.IsPms(device);
        string name = L1011InsDevices.Name(device);

        _updating = true;
        try
        {
            _unit.SelectedIndex = L1011InsDevices.All.ToList().IndexOf(device);
            if (!pms)
            {
                string n = Unit.ToString(CultureInfo.InvariantCulture);
                Fill(_displaySelector, $"INS_{n}_ROTARY_MODE");
                Fill(_waypointSelector, $"INS_{n}_ROTARY_WPT");
                Fill(_autoSelector, $"INS_{n}_ROTARY_AUTO");
            }
        }
        finally
        {
            _updating = false;
        }

        _display.AccessibleName = name + " display";
        _waypoints.AccessibleName = name + " waypoints";
        _insControls.Visible = !pms;
        _pmsControls.Visible = pms;
        _changes.Reset();
        _entry.Reset();
        _atOnceSpoken = null;
        _openEntry = null;
        _shown = null;
        _pageReadBack = new();

        if (L1011InsDevices.ViewNeedle(device) != before)
        {
            StopReader();
            _viewSwitch.Stop();
            if (Visible) _viewSwitch.Start();
        }
        else
        {
            _client?.SetActive(true);   // a fresh push for the unit now shown
        }
        Repaint();
    }

    private void Fill(ComboBox combo, string key)
    {
        combo.Tag = key;
        combo.Items.Clear();
        foreach (var p in _def.InsSelectorPositions(key).OrderBy(p => p.Key))
            combo.Items.Add(p.Value);
    }

    /// <summary>
    /// The scrape with the shown INS's overhead mode selector as the aircraft REPORTS it ([L1011-11]), for
    /// the display's "Mode selector" line: the reader runs in the INS view, which does not know the MSU; the
    /// Overhead row does (<c>L:ROTARY_INS_MODE_n</c>, 0 Off, 1 Standby, 2 Align, 3 Nav, 4 Attitude), read the
    /// way the three selector combos are. Null stays null; a PMS is returned as is.
    /// </summary>
    private L1011InsScrapeResult? WithMsu(L1011InsScrapeResult? scrape)
    {
        if (scrape == null || L1011InsDevices.IsPms(_device))
            return scrape;
        int? msu = _def.InsSelectorPosition(L1011InsAlignmentAnnouncer.MsuKey(Unit), _sim) is double m ? (int)Math.Round(m) : null;
        return scrape with { Ins = scrape.Ins with { Msu = msu } };
    }

    /// <summary>Starts the shown unit's reader (the view's one inspector socket).</summary>
    private void StartReader()
    {
        string needle = L1011InsDevices.ViewNeedle(_device);
        if (_client != null && _clientNeedle == needle)
            return;
        StopReader();
        var reader = new CoherentDisplayClient(needle, RefreshMs, AgentFile);
        reader.RowsUpdated += rows => OnRows(reader, rows);
        reader.Error += message => Log.Warn("L1011", $"INS reader: {message}");
        _client = reader;
        _clientNeedle = needle;
        reader.Start();
    }

    /// <summary>Stops the reader and forgets what it read: its socket goes back to the view.</summary>
    private void StopReader()
    {
        _client?.Dispose();
        _client = null;
        _clientNeedle = null;
        _scrape = null;
    }

    /// <summary>Takes a reader's rows only while it is the running one (a post queued before it stopped is dropped).</summary>
    private void OnRows(CoherentDisplayClient source, List<string> rows)
    {
        if (InvokeRequired)
        {
            try { BeginInvoke(new System.Action(() => OnRows(source, rows))); } catch (InvalidOperationException) { }
            return;
        }
        if (IsDisposed || !ReferenceEquals(source, _client) || L1011InsScrape.Parse(rows) is not L1011InsScrapeResult parsed)
            return;
        _scrape = parsed;
        string? words = _changes.Observe(_device, parsed);
        Repaint();
        if (words != null)
        {
            bool pms = L1011InsDevices.IsPms(_device);
            if (_def.InsWindowChange(pms, words) is string callOut)   // a Ctrl+M row can mute the call-out
                _announcer.Announce(callOut);
            if (pms && _display.Items.Count > 1)
                _display.SelectedIndex = 1;   // a new page: the cursor on line 1, as on every CDU window
        }
    }

    // ---------------------------------------------------------------------------------
    // Read-out
    // ---------------------------------------------------------------------------------

    private void Poll()
    {
        if (IsDisposed)
            return;
        Repaint();
        if (!L1011InsDevices.IsPms(_device)
            && _pageReadBack.OnPoll(_def.InsSelectorPosition(DisplaySelectorKey, _sim), Environment.TickCount64) is string page)
            _announcer.Announce(page);
        if (L1011InsDevices.EntryLine(_device, _scrape) is not string entry)
            return;
        if (L1011InsDevices.EntryReadsAtOnce(_device, _scrape))
        {
            // A PMS line the unit erases within about a second (its ERROR word after a refused value; "---"
            // drawn inside the label while the entry is open): read on the first poll that sees it, not after
            // two stable polls ([CORE-7], a validation error always speaks), and not held back by the typing
            // suppression (it is never half-landed). The stable-poll announcer is not fed it, so it is never
            // said twice; the line the gauge draws next (the value as it was, or an emptied entry) is read back
            // the ordinary way. An INS INSERT the unit refuses changes nothing and so says nothing.
            if (entry != _atOnceSpoken)
            {
                _atOnceSpoken = entry;
                _announcer.Announce(entry);
            }
            return;
        }
        _atOnceSpoken = null;
        var lines = L1011InsDevices.Lines(_device, _scrape);
        var before = _openEntry;
        if (entry.Length > 0 && lines != null && _scrape?.Pms is L1011PmsScreen open && L1011InsDevices.IsPms(_device))
            _openEntry = (lines, open.ActiveLine);
        if (_entry.OnPoll(entry, DateTime.UtcNow) is not string say)
            return;
        if (say == L1011InsWindowCallouts.EntryClosedWords)
        {
            _openEntry = null;
            // A PMS INSERT reads back what it stored ("4: GW 418. 2: EPR 1.642"), a typed-value confirmation
            // that always speaks ([CORE-7]); a page change that abandoned the entry has none.
            if (before is { } b && lines != null && L1011InsDevices.InsertReadback(b.Lines, b.Line, lines) is string stored)
            {
                _announcer.Announce(stored);
                return;
            }
        }
        if (_def.InsWindowEntry(say) is string spoken)
            _announcer.Announce(spoken);   // "Entry closed" can be muted in Ctrl+M; what was typed always speaks
    }

    private void Repaint()
    {
        if (IsDisposed)
            return;
        var lines = L1011InsDevices.Lines(_device, WithMsu(_scrape)) ?? _shown ?? L1011InsDevices.Lines(_device, null)!;
        _shown = lines;
        _display.SetLines(lines);
        if (L1011InsDevices.IsPms(_device))
        {
            // The line keys name their lines; kept while the gauge blanks a page to redraw it.
            var pms = _scrape?.Pms;
            if (pms == null || L1011PmsDisplay.Lines(pms) != null)
            {
                for (int i = 0; i < _lineKeys.Length; i++)
                {
                    string label = L1011PmsDisplay.LineKeyLabel(_scrape?.Ins.Powered == false ? null : pms, i + 1, AlternateLineKeys);
                    if (_lineKeys[i].Text != label)
                    {
                        _lineKeys[i].Text = label;
                        _lineKeys[i].AccessibleName = label;
                    }
                }
            }
            return;
        }

        _waypoints.SetLines(_scrape == null
            ? new[] { "Reading the waypoints..." }
            : L1011InsWaypoints.Lines(_scrape.Waypoints, _scrape.ActiveBatch, _scrape.BatchCount));

        string loading = "Automatic waypoint loading: " + (_scrape?.AutoBatch switch { true => "on", false => "off", _ => "unknown" });
        if (_autoBatch.Text != loading)
        {
            _autoBatch.Text = loading;
            _autoBatch.AccessibleName = loading;
        }

        _updating = true;
        try
        {
            foreach (var combo in new[] { _displaySelector, _waypointSelector, _autoSelector })
            {
                // A combo the pilot is in is theirs: the reported position lags a commit by up to a batch.
                if (combo.Tag is not string key || !L1011InsKeyRouting.RepaintsSelector(combo.Focused, combo.DroppedDown))
                    continue;
                var ordered = _def.InsSelectorPositions(key).OrderBy(p => p.Key).ToList();
                int index = _def.InsSelectorPosition(key, _sim) is double v ? ordered.FindIndex(p => Math.Abs(p.Key - v) < 0.01) : -1;
                if (index >= 0 && combo.SelectedIndex != index)
                    combo.SelectedIndex = index;
            }
        }
        finally
        {
            _updating = false;
        }
    }

    // ---------------------------------------------------------------------------------
    // Keys
    // ---------------------------------------------------------------------------------

    private void Form_KeyDown(object? sender, KeyEventArgs e)
    {
        var combo = ActiveControl as ComboBox;
        var focus = combo == null ? L1011InsKeyRouting.ComboFocus.None
            : combo.DroppedDown ? L1011InsKeyRouting.ComboFocus.Dropped
            : L1011InsKeyRouting.ComboFocus.Closed;
        var action = L1011InsKeyRouting.Resolve(e.KeyCode, e.Alt, e.Control, e.Shift, focus,
            MSFSBlindAssist.Settings.SettingsManager.Current.MCDUUseAlternateLSKKeys, L1011InsDevices.IsPms(_device));
        if (action.Kind == L1011InsKeyRouting.ActionKind.Pass)
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        switch (action.Kind)
        {
            case L1011InsKeyRouting.ActionKind.Close:
                Close();
                break;
            case L1011InsKeyRouting.ActionKind.Insert:
                Press(L1011InsDevices.InsertKey(_device));
                break;
            case L1011InsKeyRouting.ActionKind.LineKey:
                Press(L1011InsKeys.PmsLineKey(Seat, action.Line));
                break;
            case L1011InsKeyRouting.ActionKind.NextPage:
                TurnPage(+1);
                break;
            case L1011InsKeyRouting.ActionKind.PreviousPage:
                TurnPage(-1);
                break;
            case L1011InsKeyRouting.ActionKind.FocusInput:
                _input.Focus();
                break;
            case L1011InsKeyRouting.ActionKind.FocusDisplay:
                _display.Focus();
                break;
        }
    }

    /// <summary>Backspace (CLR, or CLEAR on an INS) and Right Arrow (the next page), on the display only: the
    /// input box needs those keys to edit its own text.</summary>
    private void Display_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Alt || e.Control || e.Shift)
            return;
        if (e.KeyCode == Keys.Back)
            Press(L1011InsDevices.ClearKey(_device));
        else if (e.KeyCode == Keys.Right)
            TurnPage(+1);
        else
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    /// <summary>The next or previous page: a PMS's FWD or BACK key; on an INS its display selector one position
    /// on or back (<see cref="L1011InsPages"/>), said once the aircraft reports it.</summary>
    private void TurnPage(int direction)
    {
        if (L1011InsDevices.IsPms(_device))
        {
            Press(L1011InsKeys.PmsKey(Seat, direction > 0 ? "FWD" : "BACK"));
            return;
        }
        string key = DisplaySelectorKey;
        var positions = _def.InsSelectorPositions(key);
        var step = L1011InsPages.Next(positions, _def.InsSelectorPosition(key, _sim), direction);
        if (step.Say != null)
            _announcer.Announce(step.Say);
        if (step.Target is not double target)
            return;
        _pageReadBack.Arm(target, positions[target], Environment.TickCount64);
        _def.SetInsSelector(key, target, _sim, _announcer);
    }

    /// <summary>Enter sends the typed keys (no INSERT: Ctrl+Enter or the Insert button does that). A refused
    /// entry stays in the box, to be edited and sent again.</summary>
    private void Input_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Return || e.Alt || e.Control || e.Shift)
            return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        if (_input.Text.Length == 0)
            return;
        if (!_def.TypeKeys(_device, _input.Text, insert: false, _sim, _announcer))
            return;
        HoldReadBack();
        _input.Clear();
    }

    private void Press(string controlId)
    {
        if (_def.PressKeys(_device, new[] { controlId }, _sim, _announcer))
            HoldReadBack();
    }

    /// <summary>Holds the read-back of what was typed until the chained keys have landed, so an entry is read
    /// once, settled (the A300 window's rule).</summary>
    private void HoldReadBack()
    {
        var until = DateTime.UtcNow.AddMilliseconds(_def.KeysBusyForMs() + SettleMarginMs);
        if (until > _entry.SuppressUntil)
            _entry.SuppressUntil = until;
    }

    /// <summary>The simulator's own position, typed into this INS on its POSITION page.</summary>
    private void EnterPresentPosition()
    {
        int unit = Unit;
        if (_scrape?.Ins.Entry is { Length: > 0 })
        {
            _announcer.Announce($"INS {unit.ToString(CultureInfo.InvariantCulture)}: an entry is open. Press Clear, then try again");
            return;
        }
        if (!_sim.IsConnected)
        {
            _announcer.Announce($"INS {unit.ToString(CultureInfo.InvariantCulture)} keypad unavailable");
            return;
        }
        _sim.RequestAircraftPositionAsync(position =>
        {
            void Enter()
            {
                if (_def.EnterPresentPosition(unit, position.Latitude, position.Longitude, _sim, _announcer))
                    HoldReadBack();
            }
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new System.Action(Enter)); } catch (InvalidOperationException) { }
            }
            else
            {
                Enter();
            }
        });
    }

    /// <summary>Turns automatic waypoint loading the other way. Nothing is said: the button's label shows the
    /// state the INS reports (<see cref="Repaint"/>), and the screen reader reads it when it changes, as it does
    /// the PMDG windows' buttons ([CORE-7]); an app read-back after the press was a double.</summary>
    private void ToggleAutoBatch()
    {
        if (_scrape?.AutoBatch is not bool on)
        {
            _announcer.Announce("Automatic waypoint loading: state unknown");
            return;
        }
        _def.SetAutoBatch(Unit, !on, _sim, _announcer);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _poll.Stop();
            _poll.Dispose();
            _viewSwitch.Stop();
            _viewSwitch.Dispose();
            StopReader();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// The INS window opens with the cursor on its display, every time it is shown. <see cref="Arm"/> is called
/// as the window is shown; the first activation within <see cref="WindowMs"/> after it
/// (<see cref="TakeOnActivation"/>) takes focus to the display, once. An activation with no show before it,
/// or long after one (Shift+M on a window that is already active raises none; the next is the pilot coming
/// back), leaves focus where the pilot put it. Pure.
/// </summary>
public sealed class L1011InsShowFocus
{
    /// <summary>How long after a show an activation still belongs to it.</summary>
    public const int WindowMs = 1_000;

    private bool _armed;
    private long _armedAtMs;

    public void Arm(long nowMs)
    {
        _armed = true;
        _armedAtMs = nowMs;
    }

    public bool TakeOnActivation(long nowMs)
    {
        bool take = _armed && nowMs - _armedAtMs <= WindowMs;
        _armed = false;
        return take;
    }

    /// <summary>The window was hidden: a pending show is over.</summary>
    public void Cancel() => _armed = false;
}

/// <summary>
/// The INS window's form-wide keys, as a pure function of the chord, what has focus, the line-select layout
/// setting and whether a PMS is shown. The A300 MCDU window's rules: a chord the focused unit combo needs is
/// never taken (<see cref="MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey"/>), and a
/// binding matches its own chord and nothing more. The PMS has six LEFT line keys only; the INS has none.
/// </summary>
public static class L1011InsKeyRouting
{
    public enum ComboFocus { None, Closed, Dropped }

    public enum ActionKind { Pass, Close, Insert, LineKey, NextPage, PreviousPage, FocusInput, FocusDisplay }

    public readonly record struct Action(ActionKind Kind, int Line = 0)
    {
        public static readonly Action Pass = new(ActionKind.Pass);
    }

    /// <summary>
    /// Whether the window may rewrite a selector combo to the position the aircraft reports: never one the
    /// pilot is in (focused, or with its list dropped). The report ([L1011-11]) lags a committed choice by up
    /// to a batch, so repainting a combo being arrowed through would say the new position, the old one, then
    /// the new one again; the other fleet windows skip the focused combo the same way.
    /// </summary>
    public static bool RepaintsSelector(bool focused, bool droppedDown) => !focused && !droppedDown;

    public static Action Resolve(Keys keyCode, bool alt, bool control, bool shift, ComboFocus combo,
        bool useAlternateLskKeys, bool pms)
    {
        if (combo != ComboFocus.None
            && MSFSBlindAssist.Forms.MD11.Md11McduForm.KeyRouting.ComboBoxNeedsKey(keyCode, alt, control, shift, combo == ComboFocus.Dropped))
            return Action.Pass;
        bool none = !alt && !control && !shift;
        bool ctrlOnly = control && !alt && !shift;
        bool altOnly = alt && !control && !shift;

        if (keyCode == Keys.Escape && none)
            return new Action(ActionKind.Close);
        if (keyCode == Keys.Return && ctrlOnly)
            return new Action(ActionKind.Insert);
        if (pms)
        {
            if (useAlternateLskKeys && none && keyCode >= Keys.F1 && keyCode <= Keys.F6)
                return new Action(ActionKind.LineKey, keyCode - Keys.F1 + 1);
            if (!useAlternateLskKeys && ctrlOnly && keyCode >= Keys.D1 && keyCode <= Keys.D6)
                return new Action(ActionKind.LineKey, keyCode - Keys.D1 + 1);
        }
        // A PMS's pages, and an INS's: its display selector's positions (L1011InsPages).
        if ((none && keyCode == Keys.PageUp) || (altOnly && keyCode == Keys.Left))
            return new Action(ActionKind.PreviousPage);
        if ((none && keyCode == Keys.PageDown) || (altOnly && keyCode == Keys.Right))
            return new Action(ActionKind.NextPage);
        if (altOnly && keyCode == Keys.S)
            return new Action(ActionKind.FocusInput);
        if (altOnly && keyCode == Keys.Home)
            return new Action(ActionKind.FocusDisplay);
        return Action.Pass;
    }
}
