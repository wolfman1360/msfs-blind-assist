using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the TriStar says on its own:
/// <list type="bullet">
/// <item>WARNING LIGHTS — <see cref="L1011Annunciators"/> through <see cref="L1011LampGate"/>: a silent
/// baseline, a settle, and silence while an annunciator light test runs. Spoken from the batch hook
/// (<see cref="OnContinuousBatchDelivered"/>), which runs OUTSIDE MainForm's Ctrl+M wrap, so the
/// flush checks <see cref="Settings.UserSettings.L1011DisabledMonitorVariablesSet"/> itself.</item>
/// <item>LEVERS moved by something other than MSFSBA (a hardware lever, the EFB's automation): the
/// flap handle, parking brake and ground-spoiler arm (<see cref="L1011Levers.Announcement"/>), spoken
/// from ProcessSimVarUpdate, where MainForm's wrap applies both the Ctrl+M mute and the UI echo —
/// so the pilot's own combo pick is never spoken back.</item>
/// <item>AUTOPILOT states (<see cref="L1011AfcsModes"/>): engage paddles, mode buttons, the
/// autothrottle's alpha mode and the armed/captured flags, through <see cref="L1011AfcsAnnouncer"/> inside ProcessSimVarUpdate, under the
/// same wrap; MSFSBA's own press, within <see cref="L1011CommandedState.HoldMs"/>, is silent only
/// while the switch sits where it was put (any other movement is the aircraft's and is spoken, except that a value box button or toggle key speaks its own read-back instead).</item>
/// <item>The INS and the PMS: "INS 1 navigating" when a unit starts navigating by itself
/// (<see cref="L1011InsAlignmentAnnouncer"/>) and "PMS climb/cruise/descent" when the aircraft changes
/// the PMS mode (<see cref="L1011PmsPhaseAnnouncer"/>), inside ProcessSimVarUpdate under the same wrap
/// ([L1011-15]). The INS lights (READY NAV, WARN, BATT, INS 1's waypoint ALERT) are warning lights like
/// any other.</item>
/// </list>
/// Switch positions are consumed silently: the panel combo follows them, nothing is spoken.
/// </summary>
public partial class IniL1011Definition
{
    private readonly L1011LampGate _lampGate = new();
    private readonly Dictionary<string, bool> _lightTest = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _leverLast = new(StringComparer.Ordinal);
    private readonly L1011InsAlignmentAnnouncer _insAlignment = new();
    private readonly L1011PmsPhaseAnnouncer _pmsPhase = new();
    private ScreenReaderAnnouncer? _announcer;

    /// <summary>
    /// WHEN, after a context reset, the lamps and levers a flight load left unchanged get their
    /// baselines from the cache (a load re-delivers only changed values): on the batch deliveries'
    /// evidence, never a wall clock — the MD-11's gate, reused as it stands.
    /// </summary>
    private readonly Md11SeedGate _seedGate = new();

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        if (base.ProcessSimVarUpdate(varName, value, announcer))
            return true;
        _announcer = announcer;

        if (L1011AfcsModes.IsAnnounced(varName))
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: true);
            // MSFSBA's own press is silent while the switch sits where it was put (the PMDG windows'
            // value-matched echo); any other movement is the aircraft's and is spoken. A value box
            // button or toggle key reads its own result back, so the call-out stays quiet for that key until it has.
            // MainForm's UI echo wrap covers a panel combo pick, as on every aircraft.
            // A position passed through on the way (an engage paddle's CWS notch) is MSFSBA's own too.
            bool ownCommand = _commanded.IsOwn(varName, value, Clock()) || _readBackPending.ContainsKey(varName);
            foreach (string words in _afcs.Observe(varName, value, ownCommand))
                announcer.Announce(words);
            return true;   // a mode button or engage paddle is also a position: the open control follows it
        }

        if (L1011InsAlignmentAnnouncer.UnitOf(varName) is int unit)
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: true);
            if (_insAlignment.Observe(unit, value, MsuPosition(unit)) is string navigating)
                announcer.Announce(navigating);
            return true;
        }

        if (varName == L1011PmsPhaseAnnouncer.Key)
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: true);
            if (_pmsPhase.Observe(value) is string phase)
                announcer.Announce(phase);
            return true;
        }

        if (_lamps.ContainsKey(varName))
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: true);
            _lampGate.Observe(varName, value >= 0.5, Clock());
            return true;
        }

        if (!_rows.ContainsKey(varName))
            return false;   // readouts and the probe: MainForm's display path

        if (L1011Annunciators.LightTestKeys.Contains(varName))
        {
            _lightTest[varName] = value >= 0.5;
            _lampGate.SetLightTest(_lightTest.Values.Any(on => on), Clock());
        }

        if (L1011Levers.Announcement(varName, value) != null)
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: false);
            AnnounceLever(varName, value, announcer);
        }
        return true;   // a position: the open control follows it (RefreshControlWhenDefHandled)
    }

    /// <summary>Baseline-first: the first value after a reset is recorded, a later change is spoken.</summary>
    private void AnnounceLever(string key, double value, ScreenReaderAnnouncer announcer)
    {
        string? words = L1011Levers.Announcement(key, value);
        bool known = _leverLast.TryGetValue(key, out double last);
        _leverLast[key] = value;
        if (!known || words == null || words == L1011Levers.Announcement(key, last))
            return;
        announcer.Announce(words);
    }

    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        long now = Clock();

        var sim = _sim;
        if (_seedGate.Armed && sim != null)
        {
            var trigger = _seedGate.OnBatchDelivered(batchNum, sim.ActiveContinuousBatches, now);
            if (trigger != Md11SeedTrigger.None)
                SeedFromCache(sim, trigger);
        }

        var announcer = _announcer;
        if (announcer == null)
            return;
        var muted = SettingsSource().L1011DisabledMonitorVariablesSet;
        foreach (var (key, lit) in _lampGate.Due(now))
        {
            if (muted.Contains(key) || !_lamps.TryGetValue(key, out var lamp))
                continue;
            announcer.Announce(L1011Annunciators.Announcement(lamp, lit));
        }
    }

    /// <summary>
    /// A disconnect or a flight load: every baseline goes, so the next delivery of each lamp and lever
    /// is silent, and the seed gate arms to fill in the ones a load leaves unchanged.
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _lampGate.Reset();
        _lightTest.Clear();
        _leverLast.Clear();
        _afcs.Reset();
        _insAlignment.Reset();
        _pmsPhase.Reset();
        _readBackPending.Clear();
        _commanded.Clear();
        _seedGate.Arm(KnownSeedValues());
    }

    /// <summary>The keys the seed pass fills: every lamp, every announced lever, both light-test switches,
    /// the autopilot's announced states, each INS's alignment status and the PMS flight phase.</summary>
    private IEnumerable<string> SeedKeys() =>
        _lamps.Keys
            .Concat(new[] { L1011Levers.FlapHandleKey, L1011Levers.ParkingBrakeKey, L1011Levers.GroundSpoilersKey })
            .Concat(L1011Annunciators.LightTestKeys)
            .Concat(L1011AfcsModes.All.Select(s => s.Key))
            .Concat(new[] { 1, 2, 3 }.Select(L1011InsAlignmentAnnouncer.StatusKey))
            .Append(L1011PmsPhaseAnnouncer.Key);

    private IEnumerable<KeyValuePair<string, double>> KnownSeedValues()
    {
        var sim = _sim;
        if (sim == null)
            yield break;
        foreach (var key in SeedKeys())
            if (sim.GetCachedVariableValue(key) is double value)
                yield return new KeyValuePair<string, double>(key, value);
    }

    private void SeedFromCache(SimConnectManager sim, Md11SeedTrigger trigger)
    {
        int seeded = 0;
        foreach (var key in SeedKeys())
        {
            if (sim.GetCachedVariableValue(key) is not double value)
                continue;
            if (_lamps.ContainsKey(key))
            {
                if (_lampGate.Seed(key, value >= 0.5)) seeded++;
            }
            else if (L1011AfcsModes.IsAnnounced(key))
            {
                if (_afcs.Seed(key, value)) seeded++;
            }
            else if (L1011Annunciators.LightTestKeys.Contains(key))
            {
                if (_lightTest.TryAdd(key, value >= 0.5)) seeded++;
            }
            else if (L1011InsAlignmentAnnouncer.UnitOf(key) is int unit)
            {
                if (_insAlignment.Seed(unit, value, MsuPosition(unit))) seeded++;
            }
            else if (key == L1011PmsPhaseAnnouncer.Key)
            {
                if (_pmsPhase.Seed(value)) seeded++;
            }
            else if (_leverLast.TryAdd(key, value))
            {
                seeded++;
            }
        }
        _lampGate.SetLightTest(_lightTest.Values.Any(on => on), Clock());
        Log.Debug("L1011", $"Context reset: {seeded} baselines seeded from the cache ({trigger}, {_seedGate.Deliveries} batch deliveries).");
    }
}
