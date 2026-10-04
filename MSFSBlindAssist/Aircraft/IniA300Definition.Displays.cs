using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The display readers: the flight mode annunciator, read from the aircraft's own mode numbers
/// (<see cref="A300Fma"/>), spoken as it changes and shown on the PFD panel. Nothing here reads the
/// simulator's memory or a picture; every value is an aircraft variable.
/// </summary>
public partial class IniA300Definition
{
    private readonly A300FmaTracker _fmaTracker = new();

    /// <summary>Whether a Ctrl+M row is unticked; tests replace it. The FMA is spoken from the batch-end
    /// callback, OUTSIDE MainForm's mute wrap, so it asks for itself.</summary>
    internal Func<string, bool> IsMuted { get; set; } =
        key => Settings.SettingsManager.Current.A300DisabledMonitorVariablesSet.Contains(key);

    /// <summary>
    /// Registers every FMA source that is not already registered: batch-covered, consumed silently.
    /// The five that carry a column's Ctrl+M row are listed in the monitor manager under that name.
    /// </summary>
    private void RegisterFmaSources(Dictionary<string, SimVarDefinition> vars, HashSet<string> batchNames)
    {
        foreach (var source in A300FmaSources.All)
        {
            if (vars.ContainsKey(source.Key))
                continue;   // a shared row or light
            var def = new SimVarDefinition
            {
                Name = source.Var,
                DisplayName = source.MuteName ?? source.Var,
                Type = SimVarType.LVar,
                UpdateFrequency = UpdateFrequency.Continuous,
                IsAnnounced = true,
                ExcludeFromMonitorManager = source.MuteName == null,
            };
            batchNames.Add(ContinuousBatchLayout.FullName(def));
            vars[source.Key] = def;
        }
    }

    /// <summary>
    /// The FMA is composed once a sample, when the batch that carries its sources has finished
    /// dispatching, so a mode change that moves several variables at once (a thrust and a pitch mode
    /// together, an armed mode becoming active) is read complete. The A300 waits on this every period
    /// rather than only while something is held: every FMA delivery is held until its batch ends.
    /// </summary>
    public override string? DeferredFlushWatchVariable => A300FmaSources.PitchModeKey;

    public override void OnDeferredFlushBatchDelivered(ScreenReaderAnnouncer announcer)
    {
        base.OnDeferredFlushBatchDelivered(announcer);
        if (_disposed || _sim is not { } sim)
            return;
        var reading = A300Fma.Read(A300FmaSources.Compose(key => Cached(sim, key)));
        var callouts = _fmaTracker.Observe(reading);
        if (_seedGate.Armed)
            return;   // a flight load is still settling: the modes it shows are not changes
        foreach (var callout in callouts)
            if (!IsMuted(A300FmaSources.MuteKeyFor(callout.Column)))
                announcer.Announce(callout.Phrase);
    }
}
