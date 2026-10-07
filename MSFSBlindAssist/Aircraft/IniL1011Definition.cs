using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// iniBuilds L-1011 TriStar (Community package <c>inibuilds-aircraft-l1011</c>, MSFS 2024).
/// Full notes: docs/l1011.md.
///
/// HOW IT WORKS
/// <list type="bullet">
/// <item>Every clickable control comes from the generated map (<c>Resources/l1011_control_map.json</c>,
/// built by <c>tools/l1011-gen</c> from the aircraft's own compiled cockpit behaviour and tooltips);
/// <see cref="L1011PanelLayout"/> places the ones a flight needs into panels in preflight order with
/// spoken names. Regenerate the map after an iniBuilds update; never hand-edit it.</item>
/// <item>A write REPLAYS THE COCKPIT'S OWN CLICK: the L:var writes, H: events and K: events the
/// control's mouse code performs for that position, as one calculator-path string
/// (<see cref="L1011WritePlan"/>), unique-ified so a repeat is never coalesced. The TriStar's gauges
/// re-read their switches only when those H: events fire, so a bare L:var write (or SetLVar's
/// data-definition write) moves the switch and changes nothing.</item>
/// <item>Switch positions, levers and the warning lights ride the continuous batches (no individual
/// data definitions). Positions are consumed silently — the screen reader already speaks the pilot's
/// own picks — and only move the open panel's controls; lights speak through
/// <see cref="L1011LampGate"/>; a few levers speak when something other than MSFSBA moves them.</item>
/// <item>Gauges are on-request readouts listed in each panel's status display.</item>
/// <item>The 981 circuit breakers are never data definitions: the breaker window reads them through
/// the Coherent debugger (<see cref="L1011CircuitBreakers"/>).</item>
/// </list>
/// The AFCS glareshield panel, the INS/PMS and the EFB are added by later sessions (design doc).
/// </summary>
public partial class IniL1011Definition : BaseAircraftDefinition, IDisposable
{
    public const string Code = "INI_L1011";

    public override string AircraftName => "iniBuilds L-1011 TriStar";
    public override string AircraftCode => Code;
    public override string? ChecklistFileName => "iniBuilds_L1011_Checklist.txt";

    private readonly L1011ControlMap _map;
    private readonly L1011Placement _placement;

    /// <summary>Variable key → the panel row it drives (every operable row).</summary>
    private readonly Dictionary<string, L1011PlacedRow> _rows = new(StringComparer.Ordinal);

    /// <summary>Lamp variable key → the light it announces.</summary>
    private readonly Dictionary<string, L1011Lamp> _lamps = new(StringComparer.Ordinal);

    /// <summary>Readout variable key → its gauge.</summary>
    private readonly Dictionary<string, L1011Readout> _readouts = new(StringComparer.Ordinal);

    private SimConnectManager? _sim;
    private bool _disposed;

    /// <summary>The clock every timing rule reads (lamp settle, commanded-position hold); tests replace it.</summary>
    internal Func<long> Clock { get; set; } = () => Environment.TickCount64;

    /// <summary>Where the Ctrl+M mute list is read from; tests replace it.</summary>
    internal Func<Settings.UserSettings> SettingsSource { get; set; } = () => Settings.SettingsManager.Current;

    /// <summary>Whether a write can reach the aircraft (<see cref="SimConnectManager.CalcWriteCanLand"/>); tests replace it.</summary>
    internal Func<SimConnectManager, bool> CanLand { get; set; } = sim => sim.CalcWriteCanLand;

    /// <summary>Asks for a row's value again so the next delivery puts its combo back. Only when
    /// connected: there is nothing to ask otherwise, and the request path needs the SimConnect
    /// library loaded. Tests replace it.</summary>
    internal Action<string, SimConnectManager> ReRead { get; set; } = (key, sim) =>
    {
        if (sim.IsConnected)
            sim.RequestVariable(key, forceUpdate: true);
    };

    /// <summary>The wait before the gear lever is re-read after a write; tests replace it.</summary>
    internal Func<int, Task> SettleDelay { get; set; } = Task.Delay;

    public IniL1011Definition()
    {
        _map = L1011ControlMap.Load();
        _placement = L1011PanelLayout.Place(_map, L1011Levers.Keys);
        foreach (var rows in _placement.RowsByPanel.Values)
            foreach (var row in rows)
                _rows[row.Key] = row;
        foreach (var lamp in L1011Annunciators.All)
            _lamps[L1011Annunciators.KeyFor(lamp)] = lamp;
        foreach (var readout in L1011Readouts.All)
            _readouts[readout.Key] = readout;

        if (_placement.MissingIds.Count > 0)
            Log.Warn("L1011", $"Layout names {_placement.MissingIds.Count} ids the map lacks: {string.Join(", ", _placement.MissingIds.Take(10))}");
        if (_placement.UnreplayableIds.Count > 0)
            Log.Warn("L1011", $"Layout places {_placement.UnreplayableIds.Count} controls the map cannot replay: {string.Join(", ", _placement.UnreplayableIds.Take(10))}");
    }

    /// <summary>The SimConnect manager the definition writes through; MainForm calls this when the
    /// TriStar is selected and at start-up, and every write path refreshes it.</summary>
    public void Attach(SimConnectManager sim) => _sim = sim;

    /// <summary>Opens the circuit-breaker window (set by MainForm).</summary>
    public Action? OpenCircuitBreakers { get; set; }

    /// <summary>The map's circuit breakers, for the breaker window.</summary>
    public IReadOnlyList<L1011Breaker> Breakers => _map.Breakers;

    // The glareshield AFCS panel is session 2; until then nothing here takes typed autopilot values.
    public override FCUControlType GetAltitudeControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetHeadingControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetSpeedControlType() => FCUControlType.IncrementDecrement;
    public override FCUControlType GetVerticalSpeedControlType() => FCUControlType.IncrementDecrement;

    public override Dictionary<string, string> GetButtonStateMapping() => new();

    // =================================================================================
    // Variables
    // =================================================================================

    /// <summary>
    /// One variable per panel row, lamp and readout. The UpdateFrequency choice here IS the
    /// SimConnect budget strategy: positions and lamps ride the continuous batches (5 × 300 slots,
    /// no individual definitions); readouts, knobs and duplicate positions are on-request
    /// definitions; buttons and typed entries are write-only (never registered). A position variable
    /// that two rows share rides the batch once — two keys with one name in a batch shift every
    /// later slot — and the second row reads it on request.
    /// </summary>
    protected override Dictionary<string, SimVarDefinition> BuildVariables()
    {
        var vars = GetBaseVariables();
        var batchNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var def in vars.Values)
            if (ContinuousBatchLayout.RidesBatch(def))
                batchNames.Add(ContinuousBatchLayout.FullName(def));

        foreach (var row in _rows.Values)
        {
            if (vars.ContainsKey(row.Key))
                continue;   // never shadow a base variable
            var def = row.Action == L1011RowAction.Custom ? BuildCustomVariable(row) : BuildRowVariable(row);
            if (ContinuousBatchLayout.RidesBatch(def) && !batchNames.Add(ContinuousBatchLayout.FullName(def)))
            {
                def.UpdateFrequency = UpdateFrequency.OnRequest;
                def.IsAnnounced = false;
            }
            vars[row.Key] = def;
        }

        foreach (var (key, lamp) in _lamps)
        {
            var def = new SimVarDefinition
            {
                Name = lamp.Var,
                DisplayName = lamp.Name,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,                  // batch-covered; spoken through the lamp gate
                ValueDescriptions = new Dictionary<double, string> { [0] = "off", [1] = "on" },
                RenderAsReadOnlyStatus = true,
            };
            if (!batchNames.Add(ContinuousBatchLayout.FullName(def)))
            {
                Log.Warn("L1011", $"Lamp {lamp.Var} is already monitored under another key - not registered twice.");
                continue;
            }
            vars[key] = def;
        }

        foreach (var readout in L1011Readouts.All)
        {
            vars[readout.Key] = new SimVarDefinition
            {
                Name = readout.SimVar,
                DisplayName = readout.Name,
                Type = SimVarType.SimVar,
                Units = readout.Units,
                UpdateFrequency = UpdateFrequency.OnRequest,
                RenderAsReadOnlyStatus = true,
            };
        }

        // End-to-end MobiFlight probe target: every TriStar write is a calculator-path string, and
        // with no WASM module installed each one is dropped with nothing to say so. Registering this
        // var opts the TriStar into MainForm's probe and its spoken warning (CalcPathProbeOptInTests).
        vars["MSFSBA_BRIDGE_PROBE"] = new SimVarDefinition
        {
            Name = "MSFSBA_BRIDGE_PROBE",
            DisplayName = "Bridge Probe",
            Type = SimVarType.LVar,
            UpdateFrequency = UpdateFrequency.OnRequest,
        };

        int batched = vars.Values.Count(ContinuousBatchLayout.RidesBatch);
        int onRequest = vars.Values.Count(d => d.UpdateFrequency == UpdateFrequency.OnRequest);
        Log.Info("L1011", $"Built {vars.Count} variables: {batched} batch-covered, {onRequest} on request, {_rows.Count} panel rows.");
        return vars;
    }

    /// <summary>A map control's row.</summary>
    private static SimVarDefinition BuildRowVariable(L1011PlacedRow row)
    {
        var control = row.Control!;
        switch (row.Action)
        {
            case L1011RowAction.Set when control.Kind == L1011Kinds.Knob:
            {
                double lo = control.Range is { Length: 2 } r ? r[0] : 0;
                double hi = control.Range is { Length: 2 } r2 ? r2[1] : 100;
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar!),
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.OnRequest,   // read when its panel opens
                    RenderAsSlider = true,
                    SliderMin = lo,
                    SliderMax = hi,
                    RefreshControlWhenDefHandled = _ => true,
                };
            }

            case L1011RowAction.Set:
            {
                bool stockVar = control.StateVar!.StartsWith("A:", StringComparison.Ordinal);
                return new SimVarDefinition
                {
                    Name = Bare(control.StateVar),
                    DisplayName = row.Name,
                    Type = stockVar ? SimVarType.SimVar : SimVarType.LVar,
                    Units = stockVar ? control.StateUnit ?? "Bool" : "number",
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true,                    // batch-covered; consumed silently in ProcessSimVarUpdate
                    ExcludeFromMonitorManager = true,      // a checkbox here would mute nothing
                    ValueDescriptions = new Dictionary<double, string>(row.Positions),
                    // The panel combo follows a change made elsewhere (the cockpit, the EFB's
                    // autocomplete): the write fires only on a user commit, so a refresh sends nothing.
                    RefreshControlWhenDefHandled = _ => true,
                };
            }

            default:   // Press, Increase, Decrease: write-only buttons, never registered
                return new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key.Replace('#', '_'),
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    RenderAsButton = true,
                };
        }
    }

    /// <summary>A hand-written row (<see cref="L1011Levers"/>).</summary>
    private static SimVarDefinition BuildCustomVariable(L1011PlacedRow row)
    {
        switch (row.Key)
        {
            case L1011Levers.FlapHandleKey:
                return Lever(row.Name, "FLAPS HANDLE INDEX", SimVarType.SimVar, "number", L1011Levers.FlapPositions, announced: true);
            case L1011Levers.ParkingBrakeKey:
                return Lever(row.Name, "BRAKE PARKING POSITION", SimVarType.SimVar, "Bool", L1011Levers.ParkingBrakePositions, announced: true);
            case L1011Levers.GroundSpoilersKey:
                return Lever(row.Name, "SPOILERS ARMED", SimVarType.SimVar, "Bool", L1011Levers.ArmedPositions, announced: true);
            case L1011Levers.GearLeverKey:
            {
                var gear = Lever(row.Name, "LEVER_LANDING_GEAR", SimVarType.LVar, "number", L1011Levers.GearPositions, announced: false);
                gear.ValueToDescriptionKey = L1011Levers.GearDescriptionKey;
                return gear;
            }
            case L1011Levers.SpeedBrakeKey:
                return new SimVarDefinition
                {
                    Name = "SPOILERS HANDLE POSITION",
                    DisplayName = row.Name,
                    Type = SimVarType.SimVar,
                    Units = "Percent",
                    UpdateFrequency = UpdateFrequency.Continuous,
                    IsAnnounced = true,
                    ExcludeFromMonitorManager = true,
                    RenderAsSlider = true,
                    SliderMin = 0,
                    SliderMax = 100,
                    RefreshControlWhenDefHandled = _ => true,
                };
            case L1011Levers.BreakerListKey:
                return new SimVarDefinition
                {
                    Name = "MSFSBA_L1011_BREAKER_LIST",
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    RenderAsButton = true,
                };
            default:   // typed values: a text box and a Set button (the key ends in _SET)
                return new SimVarDefinition
                {
                    Name = "MSFSBA_" + row.Key,
                    DisplayName = row.Name,
                    Type = SimVarType.LVar,
                    UpdateFrequency = UpdateFrequency.Never,
                    // An empty or mistyped box arrives as NaN, which every typed value refuses with
                    // its error; the historical 0 would be a valid squawk ("Squawk 0000").
                    UnparseableTextAsNaN = true,
                };
        }
    }

    /// <summary>A lever read from the batch and shown as a combo. <paramref name="announced"/> levers
    /// speak when something other than MSFSBA moves them, so they keep a Ctrl+M row.</summary>
    private static SimVarDefinition Lever(string name, string var, SimVarType type, string units,
        IReadOnlyDictionary<double, string> positions, bool announced) => new()
    {
        Name = var,
        DisplayName = name,
        Type = type,
        Units = units,
        UpdateFrequency = UpdateFrequency.Continuous,
        IsAnnounced = true,
        ExcludeFromMonitorManager = !announced,
        ValueDescriptions = new Dictionary<double, string>(positions),
        RefreshControlWhenDefHandled = _ => true,
    };

    /// <summary>"L:NAME" → "NAME", "A:NAME:2" → "NAME:2" (SimVarDefinition names carry no prefix).</summary>
    private static string Bare(string stateVar) => stateVar.Length > 2 && stateVar[1] == ':' ? stateVar.Substring(2) : stateVar;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _seedGate.Disarm();
        ReleaseOwedSteps();   // a held button is let go before the definition goes away
        _sim = null;
        DisposeTrackedWindows();
        GC.SuppressFinalize(this);
    }
}
