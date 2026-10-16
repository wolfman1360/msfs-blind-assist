using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The INS and PMS keypads behind the INS window (<see cref="Forms.L1011.L1011InsForm"/>, which MainForm
/// owns, as the fleet's CDU windows): every key is the cockpit's own click on a map control, a run of keys is
/// sent as one calculator string (or, past the length limit, consecutive strings cut between keys), through
/// ONE chain so two quick presses can never interleave, and a selector move from the window waits behind
/// every key already chained ([L1011-14]). Refused aloud ("INS 1 keypad
/// unavailable") when the calculator path cannot land a write, and before anything is sent when typed text
/// holds a character the unit has no key for. The window's selectors are the INS panels' own rows.
/// </summary>
public partial class IniL1011Definition
{
    /// <summary>The pause after the display selector moves, before the keys that need its new page.</summary>
    public const int SelectorSettleMs = 400;

    /// <summary>Opens the INS window on a unit, or on the one it last showed (set by MainForm); the Center
    /// Console panels' "Open … window" buttons call it.</summary>
    public Action<L1011InsDevice?>? OpenInsWindow { get; set; }

    /// <summary>Keys run one after another: each new run waits for the one before it.</summary>
    private Task _keyChain = Task.CompletedTask;

    /// <summary>When (on <see cref="Clock"/>) the keys chained so far will have run.</summary>
    private long _keysIdleAtMs;

    /// <summary>Completes when everything chained so far has run; tests await it.</summary>
    internal Task KeysDone => _keyChain;

    /// <summary>How long the keys already chained still take, in ms (0 when the chain is idle): the window
    /// holds its read-back of what was typed until then.</summary>
    public int KeysBusyForMs() => (int)Math.Max(0, _keysIdleAtMs - Clock());

    /// <summary>
    /// Presses typed text on a unit's keypad, then INSERT when <paramref name="insert"/>. A character with
    /// no key sends nothing and names the problem. True when the keys were sent (or there were none).
    /// </summary>
    public bool TypeKeys(L1011InsDevice device, string text, bool insert, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        var typed = L1011InsDevices.Keys(device, text);
        if (typed.Error != null)
        {
            announcer.Announce($"{L1011InsDevices.Name(device)}: {typed.Error}");
            return false;
        }
        var ids = typed.ControlIds.ToList();
        if (insert && ids.Count > 0)
            ids.Add(L1011InsDevices.InsertKey(device));
        return ids.Count == 0 || PressKeys(device, ids, sim, announcer);
    }

    /// <summary>
    /// Presses map controls on a unit's keypad, in order, as ONE calculator string (each key's press, then
    /// its release, back to back: <see cref="L1011InsKeys.RunSteps"/>), or consecutive strings cut between
    /// keys when it would pass <see cref="L1011InsKeys.MaxRunLength"/>. True when they were sent.
    /// </summary>
    public bool PressKeys(L1011InsDevice device, IReadOnlyList<string> controlIds, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        string what = L1011InsDevices.Name(device) + " keypad";
        var clicks = new List<string>();
        foreach (var id in controlIds)
        {
            if (KeyClick(id) is not string click)
            {
                announcer.Announce($"{what}: {id} is not a control of this aircraft");
                return false;
            }
            clicks.Add(click);
        }
        if (!CanLand(sim))
        {
            announcer.Announce($"{what} unavailable");
            return false;
        }
        Chain(L1011InsKeys.RunSteps(clicks), sim);
        return true;
    }

    /// <summary>
    /// Types the aircraft's present position into an INS (latitude, INSERT, longitude, INSERT), turning
    /// the display selector to POSITION first when it is elsewhere: on any other page the N/S/E/W keys
    /// start no entry (L1011_INS.js). Alignment starts by itself once the position is in and the mode
    /// selector is at ALIGN or NAV. The figure is spoken, as for any typed value. A position that is not a
    /// place on the globe (not finite, latitude past 90, longitude past 180) is refused aloud before anything
    /// is chained: <see cref="L1011InsKeys.PresentPositionControls"/> does no range check, and would key
    /// digits the unit takes as another figure or a null control.
    /// </summary>
    public bool EnterPresentPosition(int unit, double latitude, double longitude, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || Math.Abs(latitude) > 90 || Math.Abs(longitude) > 180)
        {
            announcer.Announce($"INS {unit.ToString(CultureInfo.InvariantCulture)}: present position unavailable");
            return false;
        }
        var device = L1011InsDevices.All.Single(d => L1011InsDevices.Unit(d) == unit);
        string what = L1011InsDevices.Name(device) + " keypad";
        if (!CanLand(sim))
        {
            announcer.Announce($"{what} unavailable");
            return false;
        }

        var steps = new List<L1011Step>();
        string selectorKey = $"INS_{unit.ToString(CultureInfo.InvariantCulture)}_ROTARY_MODE";
        const double PositionPage = 3;
        if (CurrentPosition(selectorKey, sim) != PositionPage && _map.Find(selectorKey) is L1011Control selector)
        {
            var plan = L1011WritePlan.ForSet(selector, PositionPage, CurrentPosition(selectorKey, sim));
            if (plan.Refusal == null && !plan.IsEmpty)
            {
                steps.AddRange(plan.Steps);
                steps.Add(new L1011DelayStep(SelectorSettleMs));
                _commanded.Record(selectorKey, PositionPage, Clock());
            }
        }
        var clicks = L1011InsKeys.PresentPositionControls(unit, latitude, longitude)
            .Select(KeyClick).OfType<string>().ToList();   // every id is in the map (IniL1011InsBehaviourTests pins it)
        steps.AddRange(L1011InsKeys.RunSteps(clicks));

        Chain(steps, sim);
        announcer.Announce($"INS {unit.ToString(CultureInfo.InvariantCulture)}: entering present position {L1011InsWaypoints.Position(latitude, longitude)}");
        return true;
    }

    /// <summary>
    /// Automatic waypoint loading (the NAVIGATION screen's AUTO BATCH): with it on, the INS loads the next
    /// nine waypoints of an imported route when it reaches waypoint 9. Turning it on also resumes the
    /// route, exactly as the screen's own switch does (L1011_NAVIGATION.js toggleAutoBatch,
    /// <see cref="L1011InsKeys.AutoBatchRpn"/>). True when the write was chained. Nothing is spoken: the
    /// window's button shows the state the INS reports and the screen reader reads it when it changes.
    /// </summary>
    public bool SetAutoBatch(int unit, bool on, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        string n = unit.ToString(CultureInfo.InvariantCulture);
        if (!CanLand(sim))
        {
            announcer.Announce($"INS {n} automatic waypoint loading unavailable");
            return false;
        }
        Chain(new L1011Step[] { new L1011CalcStep(L1011InsKeys.AutoBatchRpn(unit, on)) }, sim);
        return true;
    }

    /// <summary>The INS window's new-leg ("Leg 0 to 1") or PMS new-page call-out, or null when its Ctrl+M row
    /// is off (<see cref="L1011InsWindowCallouts"/>). The window asks before it speaks.</summary>
    internal string? InsWindowChange(bool pms, string words) =>
        L1011InsWindowCallouts.Change(pms, words, SettingsSource().L1011DisabledMonitorVariablesSet);

    /// <summary>The INS window's entry read-back, or null when it is "Entry closed" and that Ctrl+M row is
    /// off. What was typed is always returned.</summary>
    internal string? InsWindowEntry(string say) =>
        L1011InsWindowCallouts.Entry(say, SettingsSource().L1011DisabledMonitorVariablesSet);

    /// <summary>An INS panel selector's positions in its row's words (empty for a key that is no row).</summary>
    internal IReadOnlyDictionary<double, string> InsSelectorPositions(string key) =>
        _rows.TryGetValue(key, out var row) ? row.Positions : new Dictionary<double, string>();

    /// <summary>The position the aircraft REPORTS for an INS selector ([L1011-11]), or null.</summary>
    internal double? InsSelectorPosition(string key, SimConnectManager sim) => ReportedPosition(key, sim);

    /// <summary>
    /// Moves an INS selector through its panel row's write path, so the refusals match the panels. It runs
    /// behind every key already chained ([L1011-14]): a pick made while typed keys are still being sent
    /// must not land between two key presses. With the chain idle it runs at once. The refusals, the
    /// commanded-position hold and the reported-position rule ([L1011-11]) are the row's own and apply when
    /// the pick's turn comes, so a refusal is spoken then.
    /// </summary>
    internal void SetInsSelector(string key, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (!_rows.ContainsKey(key))
            return;
        ChainAction(() => HandleUIVariableSet(key, value, GetVariables()[key], sim, announcer));
    }

    /// <summary>A unit's mode selector (overhead MSU) as MSFSBA knows it, for the alignment announcer.</summary>
    private double? MsuPosition(int unit) =>
        _sim is SimConnectManager sim ? CurrentPosition(L1011InsAlignmentAnnouncer.MsuKey(unit), sim) : null;

    /// <summary>One key's click as calculator code (its press, then its release: <see cref="L1011InsKeys.ClickRpn"/>
    /// of the control's own <see cref="L1011WritePlan.ForPress"/> plan, [L1011-1]); null when the map has no such
    /// control or it cannot be pressed.</summary>
    private string? KeyClick(string controlId)
    {
        if (_map.Find(controlId) is not L1011Control control)
            return null;
        var plan = L1011WritePlan.ForPress(control);
        return plan.Refusal == null ? L1011InsKeys.ClickRpn(plan) : null;
    }

    /// <summary>Runs steps after every run already chained, on the UI thread, and notes when they will
    /// all have run.</summary>
    private void Chain(IReadOnlyList<L1011Step> steps, SimConnectManager sim)
    {
        long duration = steps.OfType<L1011DelayStep>().Sum(d => (long)d.Milliseconds);
        _keysIdleAtMs = Math.Max(Clock(), _keysIdleAtMs) + duration;
        var previous = _keyChain;
        _keyChain = RunAfterAsync(previous, steps, sim);
    }

    /// <summary>Runs an action on the UI thread after every run already chained, and puts it in the chain, so
    /// whatever is chained later waits for it too. At once (the chain is idle: its task is complete, so the
    /// await continues inline) when nothing is queued. The action's own write is not awaited: a selector's
    /// plan has no wait in it.</summary>
    private void ChainAction(Action action) => _keyChain = RunActionAfterAsync(_keyChain, action);

    private async Task RunActionAfterAsync(Task previous, Action action)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"A keypad run failed: {ex.Message}");
        }
        if (_disposed)
            return;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"A queued selector write failed: {ex.Message}");
        }
    }

    private async Task RunAfterAsync(Task previous, IReadOnlyList<L1011Step> steps, SimConnectManager sim)
    {
        try
        {
            await previous;
        }
        catch (Exception ex)
        {
            Log.Warn("L1011", $"A keypad run failed: {ex.Message}");
        }
        await RunAsync(steps, sim);
    }
}
