namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// Remembers the position MSFSBA just commanded for each control, so the next decision about that
/// control uses it instead of the SimConnect cache. The cache is fed by the 1 Hz continuous batch,
/// so for up to a second after a write it still holds the old position; two picks in that window
/// would both be planned from the stale value, and for a control whose transition carries a TOGGLE
/// event the second pick would toggle the wrong way. Same idea as the A380's
/// CommandedOrCachedValue. Pure: the caller passes the clock.
/// </summary>
public sealed class L1011CommandedState
{
    /// <summary>Long enough for the write to land and the next batch to deliver it.</summary>
    public const long HoldMs = 2500;

    private readonly Dictionary<string, (double Value, IReadOnlyList<double> Via, long AtMs)> _commanded =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Records a command; <paramref name="via"/> are the positions the control passes through
    /// on its way (<see cref="L1011Plan.Via"/>).</summary>
    public void Record(string key, double value, long nowMs, IReadOnlyList<double>? via = null) =>
        _commanded[key] = (value, via ?? Array.Empty<double>(), nowMs);

    /// <summary>The commanded value while it is fresh, otherwise <paramref name="cached"/>.</summary>
    public double? Resolve(string key, double? cached, long nowMs) =>
        _commanded.TryGetValue(key, out var c) && nowMs - c.AtMs <= HoldMs ? c.Value : cached;

    /// <summary>Whether a delivered position is MSFSBA's own doing: where it was just put, or a position
    /// it passed through on the way, while the command is fresh.</summary>
    public bool IsOwn(string key, double value, long nowMs) =>
        _commanded.TryGetValue(key, out var c) && nowMs - c.AtMs <= HoldMs
        && (Math.Abs(c.Value - value) < 0.5 || c.Via.Any(v => Math.Abs(v - value) < 0.5));

    public void Clear() => _commanded.Clear();
}
