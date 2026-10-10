namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// When the A300's light changes are spoken ([A300-24]). They wait for the end of the next continuous
/// batch and are spoken as one sentence (<see cref="A300LampCallouts"/>), netted per light: a light that
/// went on and back off since the last sentence says nothing.
///
/// After either bus's light power changes they wait <see cref="SettleMs"/> as well. The power flag and
/// the fault flags reach MSFSBA on separate once-a-second subscriptions, so the aircraft's own order is
/// lost: measured on external power (2026-10-09), the cabin regulator 2 fault cleared 500 ms BEFORE the
/// AC light power rose, MSFSBA heard the power first, and said the light on and then, a second later,
/// off. Like the fleet, a power change still speaks every light it lights or darkens; it is said once,
/// when the power has settled. Pure.
/// </summary>
public sealed class A300LampSpeech
{
    /// <summary>Two subscription periods and a batch end: long enough for a fault that changed with
    /// the power to arrive after it.</summary>
    public const int SettleMs = 2500;

    private readonly List<A300LampChange> _pending = new();
    private long _holdUntil = long.MinValue;

    public void Add(A300LampChange change) => _pending.Add(change);

    /// <summary>A bus's light power changed: hold the pending changes until it settles.</summary>
    public void NotePowerChange(long now) => _holdUntil = now + SettleMs;

    /// <summary>The sentence to speak now, or null: nothing pending, still settling, or it all netted out.
    /// Clears what it returns.</summary>
    public string? Flush(long now)
    {
        if (_pending.Count == 0 || now < _holdUntil)
            return null;
        var net = Net(_pending);
        _pending.Clear();
        return net.Count == 0 ? null : A300LampCallouts.Compose(net);
    }

    public void Clear()
    {
        _pending.Clear();
        _holdUntil = long.MinValue;
    }

    /// <summary>Each light's last change, kept only when the light changed an odd number of times (it ends
    /// where it did not start), in the order of those last changes.</summary>
    public static IReadOnlyList<A300LampChange> Net(IReadOnlyList<A300LampChange> changes)
    {
        var count = new Dictionary<string, int>(StringComparer.Ordinal);
        var last = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < changes.Count; i++)
        {
            count[changes[i].Name] = count.GetValueOrDefault(changes[i].Name) + 1;
            last[changes[i].Name] = i;
        }
        return last.Where(l => count[l.Key] % 2 == 1).OrderBy(l => l.Value).Select(l => changes[l.Value]).ToList();
    }
}
