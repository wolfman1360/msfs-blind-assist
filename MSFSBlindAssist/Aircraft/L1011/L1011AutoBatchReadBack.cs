namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The INS window's automatic waypoint loading button reads its new state back, because a screen reader
/// does not announce a focused WinForms button's label changing on the next scrape. The window arms it
/// after a press that was sent (<see cref="Expect"/>) and feeds it the state every scrape reports
/// (<see cref="Observe"/>): it returns "Automatic waypoint loading on" or "... off" ONCE, when the report
/// reaches the state that was asked for, and nothing if the report never does within the bound. A report that
/// is still the old state, or lost, is silent and the wait goes on; the fleet's rule for a toggle is that if
/// the status changes it is read that it changes, and if it does not it is not.
///
/// The bound is <see cref="IniL1011Definition.BatchReadoutTimeoutMs"/>, the wait the autoflight read-backs
/// give a fresh read, counted from when the keys already chained will have run, since the press waits behind
/// them. The window speaks the result with <c>Announce</c>, queued behind other speech as the autoflight
/// read-backs are, never interrupting it. Pure.
/// </summary>
public sealed class L1011AutoBatchReadBack
{
    private bool? _expected;
    private long _deadlineMs;

    /// <summary>Whether a press is waiting for the report to reach the state it asked for.</summary>
    public bool Pending => _expected != null;

    public static string Words(bool on) => on ? "Automatic waypoint loading on" : "Automatic waypoint loading off";

    /// <summary>A press that was sent turns loading <paramref name="on"/> or off. A later press replaces an
    /// earlier one still waiting. <paramref name="keysBusyMs"/> is how long the keys already chained still
    /// take (<see cref="IniL1011Definition.KeysBusyForMs"/>).</summary>
    public void Expect(bool on, long nowMs, int keysBusyMs)
    {
        _expected = on;
        _deadlineMs = nowMs + Math.Max(0, keysBusyMs) + IniL1011Definition.BatchReadoutTimeoutMs;
    }

    /// <summary>The state a scrape reports (null when it carries none). Returns the sentence the first time the
    /// report reaches the expected state inside the bound; a report after the bound ends the wait silently.</summary>
    public string? Observe(bool? reported, long nowMs)
    {
        if (_expected is not bool expected)
            return null;
        if (nowMs > _deadlineMs)
        {
            _expected = null;
            return null;
        }
        if (reported != expected)
            return null;
        _expected = null;
        return Words(expected);
    }

    /// <summary>Forgets a wait: the window moved to another unit or was hidden.</summary>
    public void Reset() => _expected = null;
}
