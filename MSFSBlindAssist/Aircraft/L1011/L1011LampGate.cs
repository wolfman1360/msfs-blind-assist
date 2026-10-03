namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// Decides which warning-light changes are spoken. Pure; the definition feeds it every lamp
/// delivery (<see cref="Observe"/>) and asks after each continuous batch which changes are due
/// (<see cref="Due"/>), so no timer is needed.
/// <list type="bullet">
/// <item>BASELINE FIRST: a lamp's first delivery after a reset is recorded, never spoken, so loading
/// a flight or reconnecting does not read out the whole panel.</item>
/// <item>SETTLE: a change is spoken only once the lamp has held its new state for
/// <see cref="SettleMs"/>; a flashing lamp (the leading-edge flap light flashes in transit) is
/// spoken once, in the state it settles in.</item>
/// <item>LIGHT TEST: while the annunciator light test is on, and for <see cref="TestTailMs"/> after it
/// ends, lamp changes are recorded silently: the test lights every lamp at once, which is not news.</item>
/// </list>
/// </summary>
public sealed class L1011LampGate
{
    public const long SettleMs = 1200;
    public const long TestTailMs = 3000;

    private readonly Dictionary<string, bool> _known = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, (bool Lit, long SinceMs)> _pending = new(StringComparer.OrdinalIgnoreCase);
    private long _testUntilMs = long.MinValue;

    public void SetLightTest(bool on, long nowMs) =>
        _testUntilMs = on ? long.MaxValue : (_testUntilMs == long.MaxValue ? nowMs + TestTailMs : _testUntilMs);

    public void Observe(string key, bool lit, long nowMs)
    {
        if (!_known.TryGetValue(key, out bool known))
        {
            _known[key] = lit;
            return;
        }
        if (known == lit)
        {
            _pending.Remove(key);
            return;
        }
        if (!_pending.TryGetValue(key, out var p) || p.Lit != lit)
            _pending[key] = (lit, nowMs);
    }

    /// <summary>Changes that have settled, in key order; each is spoken once.</summary>
    public IReadOnlyList<(string Key, bool Lit)> Due(long nowMs)
    {
        var due = new List<(string Key, bool Lit)>();
        foreach (var (key, p) in _pending.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList())
        {
            if (nowMs - p.SinceMs < SettleMs)
                continue;
            _pending.Remove(key);
            _known[key] = p.Lit;
            if (p.SinceMs > _testUntilMs)
                due.Add((key, p.Lit));
        }
        return due;
    }

    /// <summary>
    /// Records <paramref name="lit"/> as the lamp's baseline when it has none yet, silently: the
    /// context-reset seed pass, for a lamp a flight load left unchanged and so never re-delivered.
    /// True when it did.
    /// </summary>
    public bool Seed(string key, bool lit)
    {
        if (_known.ContainsKey(key))
            return false;
        _known[key] = lit;
        return true;
    }

    /// <summary>Forget every lamp: the next delivery of each is a silent baseline again.</summary>
    public void Reset()
    {
        _known.Clear();
        _pending.Clear();
        _testUntilMs = long.MinValue;
    }
}
