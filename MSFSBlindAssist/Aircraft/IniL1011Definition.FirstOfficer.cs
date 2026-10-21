using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The First Officer's write entry: the panels' own write core (<see cref="PlanFor"/>,
/// <see cref="ApplyPlan"/>), so a flow step writes a control exactly as a panel pick does, click replay,
/// toggle refusal from an unknown position ([L1011-4]) and the paddle's CWS notch ([L1011-21])
/// included. Unlike the panels it speaks nothing and returns what happened
/// (<see cref="L1011FoResult"/>), and it completes only once the plan has run, a button's hold
/// included. A press still held when the definition goes away is released by <see cref="Dispose"/>,
/// once.
/// </summary>
public partial class IniL1011Definition
{
    /// <summary>True when a First Officer write could reach the aircraft: not disposed, attached, and
    /// the calculator path able to land a write.</summary>
    internal bool FoReady => !_disposed && _sim is { } sim && CanLand(sim);

    /// <summary>
    /// Sets a control as a panel pick would: a Set row to <paramref name="value"/>, a Press row pressed
    /// (and released after its hold), an Increase or Decrease row stepped once (they ignore
    /// <paramref name="value"/>). <see cref="L1011FoOutcome.UnknownKey"/> for any key it does not take
    /// (<see cref="FoAcceptsKey"/>); <see cref="L1011FoOutcome.Unavailable"/> when the definition is
    /// not attached, has gone away, or the write cannot land; <see cref="L1011FoOutcome.Refused"/> with
    /// the reason the panel would have spoken. Completes when the plan has run.
    /// </summary>
    internal async Task<L1011FoResult> FoSetAsync(string key, double value)
    {
        if (!_rows.TryGetValue(key, out var row) || !IsFoMapRow(row))
            return L1011FoResult.UnknownKey;
        if (_disposed || _sim is not { } sim)
            return L1011FoResult.Unavailable;
        var (result, run) = ApplyPlan(row, PlanFor(row, value, sim), value, sim);
        await run;
        return result;
    }

    /// <summary>True for exactly the keys <see cref="FoSetAsync"/> does not answer
    /// <see cref="L1011FoOutcome.UnknownKey"/>.</summary>
    internal bool FoAcceptsKey(string key) => _rows.TryGetValue(key, out var row) && IsFoMapRow(row);

    /// <summary>True when the key's variable is registered for reading (any update frequency but
    /// <see cref="UpdateFrequency.Never"/>): switch rows, levers, knobs, lamps and readouts, never a
    /// button or a typed entry.</summary>
    internal bool FoCanReadBack(string key) =>
        GetVariables().TryGetValue(key, out var def) && def.UpdateFrequency != UpdateFrequency.Never;

    /// <summary>A map row the First Officer writes through the panels' core: never a hand-written
    /// (<see cref="L1011RowAction.Custom"/>) row, which is a lever, a typed entry or a window opener.</summary>
    private static bool IsFoMapRow(L1011PlacedRow row) =>
        row.Action is L1011RowAction.Set or L1011RowAction.Press or L1011RowAction.Increase or L1011RowAction.Decrease;
}
