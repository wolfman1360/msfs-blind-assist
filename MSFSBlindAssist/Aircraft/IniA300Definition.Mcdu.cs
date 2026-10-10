using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// MCDU keys: every press is an L:var pulse, <c>1</c>, <see cref="A300McduKeys.HoldMs"/>, <c>0</c>,
/// then <see cref="A300McduKeys.GapMs"/> before the next (<see cref="A300McduKeys"/>). Presses are
/// serialised through ONE queue so a typed entry lands in order however fast the window asks, and
/// each awaits on the UI thread, so SimConnect is never used from a pool thread.
/// </summary>
public partial class IniA300Definition
{
    private readonly Queue<(A300McduUnit Unit, string Key)> _mcduQueue = new();
    private bool _mcduPumping;
    private (A300McduUnit Unit, string Key)? _mcduHeld;

    /// <summary>Keys queued and not yet released (the window holds its scratchpad read-back on it).</summary>
    public int McduKeysPending => _mcduQueue.Count + (_mcduHeld != null ? 1 : 0);

    /// <summary>
    /// Queues <paramref name="keys"/> on <paramref name="unit"/>. False, having queued NOTHING, when
    /// the calculator path cannot land: a half-sent entry would leave text in the scratchpad the
    /// pilot did not type.
    /// </summary>
    public bool PressMcduKeys(A300McduUnit unit, IReadOnlyList<string> keys)
    {
        var sim = _sim;
        if (_disposed || sim == null || !CanLand(sim))
            return false;
        foreach (var key in keys)
            _mcduQueue.Enqueue((unit, key));
        if (!_mcduPumping)
            _ = PumpMcduAsync(sim);
        return true;
    }

    /// <summary>Switches on the aircraft's MCDU text export (the tablet's External Hardware → MCDU
    /// Export setting). The aircraft publishes the screens only while it is 1, and it does not survive
    /// a flight load, so the window sends it every time it opens.</summary>
    public bool EnableMcduExport()
    {
        var sim = _sim;
        if (_disposed || sim == null || !CanLand(sim))
            return false;
        Send(sim, "1 (>L:INI_MCDU_OPTION)");
        return true;
    }

    /// <summary>The unit's lit annunciators, as the cockpit shows them: each light AND the AC light power
    /// ([A300-23]); an unread light or power is dark.</summary>
    public IReadOnlyList<string> McduAnnunciators(A300McduUnit unit)
    {
        if (_sim is not { } sim || Cached(sim, A300LampBoard.AcPowerKey) is not double power || power < 0.5)
            return Array.Empty<string>();
        return A300McduLights.For(unit).Where(l => Cached(sim, l.Key) is double v && v >= 0.5).Select(l => l.Label).ToArray();
    }

    private async Task PumpMcduAsync(SimConnectManager sim)
    {
        _mcduPumping = true;
        try
        {
            while (!_disposed && _mcduQueue.Count > 0)
            {
                var (unit, key) = _mcduQueue.Dequeue();
                _mcduHeld = (unit, key);
                Send(sim, A300McduKeys.PressRpn(unit, key));
                await Delay(A300McduKeys.HoldMs);
                if (_mcduHeld != null)   // Dispose may already have released it
                {
                    Send(sim, A300McduKeys.ReleaseRpn(unit, key));
                    _mcduHeld = null;
                }
                await Delay(A300McduKeys.GapMs);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("A300", $"An MCDU key press failed: {ex.Message}");
        }
        finally
        {
            _mcduPumping = false;
        }
    }

    /// <summary>Releases a key still held and drops the rest (the aircraft is going away).</summary>
    private void ReleaseMcduKeys()
    {
        _mcduQueue.Clear();
        var sim = _sim;
        if (_mcduHeld is { } held && sim != null)
            Send(sim, A300McduKeys.ReleaseRpn(held.Unit, held.Key));
        _mcduHeld = null;
    }
}
