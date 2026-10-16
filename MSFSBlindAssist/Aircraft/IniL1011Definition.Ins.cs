using System.Globalization;
using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The INS and PMS keypads behind the INS window (<see cref="Forms.L1011.L1011InsForm"/>, which MainForm
/// owns, as the fleet's CDU windows): every key is the cockpit's own click on a map control, sent in order
/// through ONE chain so two quick presses can never interleave ([L1011-14]). Refused aloud ("INS 1 keypad
/// unavailable") when the calculator path cannot land a write, and before anything is sent when typed text
/// holds a character the unit has no key for. The window's selectors are the INS panels' own rows.
/// </summary>
public partial class IniL1011Definition
{
    /// <summary>How long a keypad key is held. The INS and PMS act on the press event; the release only
    /// lets the key's animation spring back, so a short hold is enough (L1011_INS.js has no debounce).</summary>
    public const int KeyHoldMs = 60;

    /// <summary>The pause after each key, so the gauge has drawn one key before the next arrives.</summary>
    public const int KeyGapMs = 40;

    /// <summary>The pause after the display selector moves, before the keys that need its new page.</summary>
    public const int SelectorSettleMs = 400;

    /// <summary>Opens the INS window on a unit, or on the one it last showed (set by MainForm); the Center
    /// Console panels' "Open … window" buttons call it.</summary>
    public Action<L1011InsDevice?>? OpenInsWindow { get; set; }

    /// <summary>Keys run one after another: each new run waits for the one before it.</summary>
    private Task _keyChain = Task.CompletedTask;

    /// <summary>When (on <see cref="Clock"/>) the keys chained so far will have run.</summary>
    private long _keysIdleAtMs;

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

    /// <summary>Presses map controls on a unit's keypad, in order. True when they were sent.</summary>
    public bool PressKeys(L1011InsDevice device, IReadOnlyList<string> controlIds, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        _sim = sim;
        string what = L1011InsDevices.Name(device) + " keypad";
        var steps = new List<L1011Step>();
        foreach (var id in controlIds)
        {
            if (KeySteps(id) is not IReadOnlyList<L1011Step> keySteps)
            {
                announcer.Announce($"{what}: {id} is not a control of this aircraft");
                return false;
            }
            steps.AddRange(keySteps);
        }
        if (!CanLand(sim))
        {
            announcer.Announce($"{what} unavailable");
            return false;
        }
        Chain(steps, sim);
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
        foreach (var id in L1011InsKeys.PresentPositionControls(unit, latitude, longitude))
            steps.AddRange(KeySteps(id) ?? Array.Empty<L1011Step>());

        Chain(steps, sim);
        announcer.Announce($"INS {unit.ToString(CultureInfo.InvariantCulture)}: entering present position {L1011InsWaypoints.Position(latitude, longitude)}");
        return true;
    }

    /// <summary>
    /// Automatic waypoint loading (the NAVIGATION screen's AUTO BATCH): with it on, the INS loads the next
    /// nine waypoints of an imported route when it reaches waypoint 9. Turning it on also resumes the
    /// route, exactly as the screen's own switch does (L1011_NAVIGATION.js toggleAutoBatch,
    /// <see cref="L1011InsKeys.AutoBatchRpn"/>). True when the write was chained; the window then reads the
    /// new state back once the reader reports it (<see cref="L1011AutoBatchReadBack"/>).
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

    /// <summary>An INS panel selector's positions in its row's words (empty for a key that is no row).</summary>
    internal IReadOnlyDictionary<double, string> InsSelectorPositions(string key) =>
        _rows.TryGetValue(key, out var row) ? row.Positions : new Dictionary<double, string>();

    /// <summary>The position the aircraft REPORTS for an INS selector ([L1011-11]), or null.</summary>
    internal double? InsSelectorPosition(string key, SimConnectManager sim) => ReportedPosition(key, sim);

    /// <summary>Moves an INS selector through its panel row's write path, so the refusals match the panels.</summary>
    internal void SetInsSelector(string key, double value, SimConnectManager sim, ScreenReaderAnnouncer announcer)
    {
        if (_rows.ContainsKey(key))
            HandleUIVariableSet(key, value, GetVariables()[key], sim, announcer);
    }

    /// <summary>A unit's mode selector (overhead MSU) as MSFSBA knows it, for the alignment announcer.</summary>
    private double? MsuPosition(int unit) =>
        _sim is SimConnectManager sim ? CurrentPosition(L1011InsAlignmentAnnouncer.MsuKey(unit), sim) : null;

    /// <summary>One key's press, short hold, release and gap; null when the map has no such control.</summary>
    private IReadOnlyList<L1011Step>? KeySteps(string controlId)
    {
        if (_map.Find(controlId) is not L1011Control control)
            return null;
        var plan = L1011WritePlan.ForPress(control, KeyHoldMs);
        if (plan.Refusal != null)
            return null;
        return plan.Steps.Append(new L1011DelayStep(KeyGapMs)).ToList();
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
