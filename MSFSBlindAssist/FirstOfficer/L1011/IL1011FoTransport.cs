using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// Everything the TriStar First Officer's executor needs from the outside world: the definition's write
/// entry (the panels' own click replay, toggle refusal from an unknown position included), reads, the
/// lamp mute and time. Production is <see cref="L1011DefinitionFoTransport"/>; the tests drive the
/// executor through a fake TriStar that answers as the definition does over the semantics seen live.
/// </summary>
public interface IL1011FoTransport
{
    /// <summary>A write would reach the aircraft (attached, not disposed, the calculator path can land it).</summary>
    bool Ready { get; }

    /// <summary>Monotonic milliseconds.</summary>
    long NowMs { get; }

    /// <summary>Sets a control as a panel pick would (a switch row to a position, a press pressed and
    /// released, a lever or the stabilizer trim to a value), saying nothing. Completes once the write has
    /// run, a held press's release included.</summary>
    Task<L1011FoResult> SetAsync(string key, double value);

    /// <summary>Sets the captain's, first officer's and standby altimeters together, saying nothing.</summary>
    Task<L1011FoResult> SetAltimetersAsync(double millibars);

    /// <summary>Sets the cabin barometric setting (read as the altimeters read a setting), saying nothing.</summary>
    Task<L1011FoResult> SetCabinBaroAsync(double millibars);

    /// <summary>Turns the cabin altitude selector to a knob step (0 to 100), saying nothing; the aircraft
    /// computes the cabin altitude the step commands.</summary>
    Task<L1011FoResult> SetCabinAltitudeSelectorAsync(int step);

    /// <summary>True when the key's reading follows its write (a switch row, a lever, a lamp, a readout):
    /// never a press, a spring switch, a knob or a typed entry.</summary>
    bool CanReadBack(string key);

    /// <summary>Completes on the NEXT delivery of <paramref name="key"/>, or null on time-out or when it
    /// cannot be read.</summary>
    Task<double?> ReadFreshAsync(string key, int timeoutMs);

    /// <summary>The last delivered value; null when never delivered.</summary>
    double? ReadCached(string key);

    /// <summary>Keeps the warning-light call-outs quiet for <paramref name="ms"/> from now: a change the
    /// First Officer's own write causes is not news.</summary>
    void MuteLampSpeech(int ms);

    Task DelayAsync(int ms);
}
