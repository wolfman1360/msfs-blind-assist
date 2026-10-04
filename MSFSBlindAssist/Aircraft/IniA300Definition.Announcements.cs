using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.Aircraft.MD11;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// What the A300 says on its own: the master warning and master caution as they come on, and the
/// flap handle, ground spoiler arm, gear lever and parking brake when something other than the
/// pilot's own pick moves them (<see cref="A300Announcements"/>). All of it is spoken from
/// ProcessSimVarUpdate, inside MainForm's wrap, which applies both the Ctrl+M mute
/// (<see cref="Settings.UserSettings.A300DisabledMonitorVariablesSet"/>) and the UI echo, so the
/// pilot's own lever pick is never spoken back. Every other switch position is consumed silently:
/// the panel combo follows it.
/// </summary>
public partial class IniA300Definition
{
    private readonly A300AnnouncementTracker _tracker = new();

    /// <summary>The FCU altitude window's call-out (<see cref="A300FcuWindows"/>).</summary>
    private readonly A300WindowTracker _altitudeWindow = new();

    /// <summary>
    /// WHEN, after a context reset, the lights and levers a flight load left unchanged get their
    /// baselines from the cache (a load re-delivers only changed values): on the batch deliveries'
    /// evidence, never a wall clock. The MD-11's gate, reused as it stands, as the L-1011 does.
    /// </summary>
    private readonly Md11SeedGate _seedGate = new();

    public override bool ProcessSimVarUpdate(string varName, double value, ScreenReaderAnnouncer announcer)
    {
        // First: the take-off call-outs peek SIM_ON_GROUND, which the base may consume.
        if (TryHandleTakeoffCallouts(varName, value, announcer))
            return true;
        if (base.ProcessSimVarUpdate(varName, value, announcer))
            return true;

        // An FCU button's lamp: shown on the button's label (TryDescribeControlState), never spoken.
        if (A300FcuState.LightKeys.Contains(varName))
            return true;

        // An FMA source (some are switch rows too): read when its batch has finished dispatching
        // (OnDeferredFlushBatchDelivered), never spoken here.
        if (A300FmaSources.Keys.Contains(varName))
            return true;

        // The FCU altitude window: a change MSFSBA did not make ("Altitude 12,000 feet"). Spoken from
        // here, inside MainForm's mute wrap; MSFSBA's own typed value or knob step is its echo.
        if (varName == A300Readouts.AltitudeKey)
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: true);
            if (_altitudeWindow.Observe(A300FcuWindows.Altitude(value), Clock()) is string window)
                announcer.Announce(window);
            return true;
        }

        if (A300Announcements.AnnouncedKeys.Contains(varName))
        {
            if (_seedGate.Armed)
                _seedGate.NoteValue(varName, value, ownedByAircraft: _lamps.ContainsKey(varName));
            if (_tracker.Observe(varName, value) is string phrase)
                announcer.Announce(phrase);
            return true;
        }

        // A position: the open control follows it (RefreshControlWhenDefHandled). Readouts and the
        // probe are not rows, so they go on to MainForm's display path.
        return _rows.ContainsKey(varName);
    }

    public override void OnContinuousBatchDelivered(int batchNum)
    {
        base.OnContinuousBatchDelivered(batchNum);
        var sim = _sim;
        if (!_seedGate.Armed || sim == null)
            return;
        var trigger = _seedGate.OnBatchDelivered(batchNum, sim.ActiveContinuousBatches, Clock());
        if (trigger != Md11SeedTrigger.None)
            SeedFromCache(sim, trigger);
    }

    /// <summary>
    /// A disconnect or a flight load: every baseline goes, so the next delivery of each light and
    /// lever is silent, and the seed gate arms to fill in the ones a load leaves unchanged.
    /// </summary>
    public override void OnSimContextReset()
    {
        base.OnSimContextReset();
        _tracker.Reset();
        _fmaTracker.Reset();
        _altitudeWindow.Reset();
        _takeoffCallouts.Reset();
        _commanded.Clear();
        _seedGate.Arm(KnownSeedValues());
    }

    private IEnumerable<KeyValuePair<string, double>> KnownSeedValues()
    {
        var sim = _sim;
        if (sim == null)
            yield break;
        foreach (var key in A300Announcements.AnnouncedKeys.Append(A300Readouts.AltitudeKey))
            if (Cached(sim, key) is double value)
                yield return new KeyValuePair<string, double>(key, value);
    }

    private void SeedFromCache(SimConnectManager sim, Md11SeedTrigger trigger)
    {
        int seeded = 0;
        foreach (var key in A300Announcements.AnnouncedKeys)
            if (Cached(sim, key) is double value && _tracker.Seed(key, value))
                seeded++;
        if (Cached(sim, A300Readouts.AltitudeKey) is double altitude && _altitudeWindow.Seed(A300FcuWindows.Altitude(altitude)))
            seeded++;
        Log.Debug("A300", $"Context reset: {seeded} baselines seeded from the cache ({trigger}, {_seedGate.Deliveries} batch deliveries).");
    }
}
