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
/// once. The hand-written levers and the stabilizer trim are sent as the panel rows send them
/// (<see cref="L1011Levers"/>), and the altimeters in one string.
/// </summary>
public partial class IniL1011Definition
{
    /// <summary>True when a First Officer write could reach the aircraft: not disposed, attached, and
    /// the calculator path able to land a write.</summary>
    internal bool FoReady => !_disposed && _sim is { } sim && CanLand(sim);

    /// <summary>
    /// Sets a control as a panel pick would: a Set row to <paramref name="value"/>, a Press row pressed
    /// (and released after its hold), an Increase or Decrease row stepped once (they ignore
    /// <paramref name="value"/>), a lever to a position (the flap handle's index 0 to 6, the gear lever's
    /// 0 down, 50 off and 100 up, the speed brake's 0 to 100, the ground spoilers' and parking brake's 0
    /// or 1), or the stabilizer trim to degrees. <see cref="L1011FoOutcome.UnknownKey"/> for any key it
    /// does not take (<see cref="FoAcceptsKey"/>); <see cref="L1011FoOutcome.Unavailable"/> when the
    /// definition is not attached, has gone away, or the write cannot land;
    /// <see cref="L1011FoOutcome.Refused"/> with the reason the panel would have spoken. Completes when
    /// the plan has run.
    /// </summary>
    internal async Task<L1011FoResult> FoSetAsync(string key, double value)
    {
        if (!_rows.TryGetValue(key, out var row) || !IsFoRow(row))
            return L1011FoResult.UnknownKey;
        if (_disposed || _sim is not { } sim)
            return L1011FoResult.Unavailable;
        if (row.Action == L1011RowAction.Custom)
            return FoSetCustom(key, value, sim);
        var (result, run) = ApplyPlan(row, PlanFor(row, value, sim), value, sim);
        await run;
        return result;
    }

    /// <summary>
    /// A lever or the stabilizer trim. The trim is range-checked as the panel's typed entry is
    /// (<see cref="L1011Levers.StabTrimDegrees"/>); a lever takes its target as the panel's combo or
    /// slider offers it, and every lever is sent through <see cref="L1011Levers.LeverRpn"/>, the panels'
    /// own table.
    /// </summary>
    private L1011FoResult FoSetCustom(string key, double value, SimConnectManager sim)
    {
        if (key == L1011Levers.StabTrimKey)
        {
            return L1011Levers.StabTrimDegrees(value) is double degrees
                ? FoSendCustom(key, value, L1011Levers.StabTrimRpn(degrees), sim)
                : L1011FoResult.Refused(L1011Levers.StabTrimRangeError);
        }
        return L1011Levers.LeverRpn(key, value) is string rpn
            ? FoSendCustom(key, value, rpn, sim)
            : L1011FoResult.UnknownKey;
    }

    /// <summary>
    /// Sends one hand-written write, quietly. Its target is recorded as commanded first, so the lever
    /// call-out (<see cref="AnnounceLever"/>) does not speak the lever the First Officer just moved: seen
    /// live, MainForm's UI echo covers only a panel pick, so the aircraft would otherwise say "Parking
    /// brake set" on top of the flow's own line. Nothing is recorded for a write that cannot land.
    /// </summary>
    private L1011FoResult FoSendCustom(string key, double value, string rpn, SimConnectManager sim)
    {
        if (!CanLand(sim))
            return L1011FoResult.Unavailable;
        _commanded.Record(key, value, Clock());
        SendCalc(sim, rpn);
        AfterLeverSent(key, sim);
        return L1011FoResult.Sent;
    }

    /// <summary>
    /// Sets the captain's, first officer's and standby altimeters in one string, as Ctrl+B does, and
    /// says nothing. <paramref name="millibars"/> is read as the typed altimeter fields read a figure
    /// (<see cref="L1011Levers.AltimeterMillibars"/>), so a setting in inches is taken too:
    /// <see cref="L1011FoOutcome.Refused"/> with <see cref="L1011Levers.AltimeterRangeError"/> outside
    /// that range, <see cref="L1011FoOutcome.Unavailable"/> when the definition is not attached, has gone
    /// away, or the write cannot land.
    /// </summary>
    internal Task<L1011FoResult> FoSetAltimetersAsync(double millibars)
    {
        if (_disposed || _sim is not { } sim)
            return Task.FromResult(L1011FoResult.Unavailable);
        if (L1011Levers.AltimeterMillibars(millibars) is not double setting)
            return Task.FromResult(L1011FoResult.Refused(L1011Levers.AltimeterRangeError));
        if (!CanLand(sim))
            return Task.FromResult(L1011FoResult.Unavailable);
        SendCalc(sim, L1011Levers.AllAltimetersRpn(setting));
        return Task.FromResult(L1011FoResult.Sent);
    }

    /// <summary>
    /// Silences the warning-light call-outs for <paramref name="ms"/> from now: a change the First
    /// Officer's own write causes (a light test, a start) is not news. A running annunciator light test
    /// or its tail is never shortened.
    /// </summary>
    internal void FoMuteLampSpeech(int ms) => _lampGate.MuteUntil(Clock() + ms);

    /// <summary>True for exactly the keys <see cref="FoSetAsync"/> does not answer
    /// <see cref="L1011FoOutcome.UnknownKey"/>.</summary>
    internal bool FoAcceptsKey(string key) => _rows.TryGetValue(key, out var row) && IsFoRow(row);

    /// <summary>True when the key's variable is registered for reading (any update frequency but
    /// <see cref="UpdateFrequency.Never"/>) and the reading follows the write: switch rows, levers, lamps
    /// and readouts. Never a button or a typed entry (write-only), a spring switch (back at rest by the
    /// time the run ends) or a knob (read on request, stale until the settle re-read).</summary>
    internal bool FoCanReadBack(string key) =>
        GetVariables().TryGetValue(key, out var def) && def.UpdateFrequency != UpdateFrequency.Never
        && !(_rows.TryGetValue(key, out var row) && row.Control?.Kind is L1011Kinds.Spring or L1011Kinds.Knob);

    /// <summary>A row the First Officer writes: a map row through the panels' core (<see cref="IsFoMapRow"/>),
    /// a lever or the stabilizer trim. Never another hand-written (<see cref="L1011RowAction.Custom"/>)
    /// row, which is a typed entry or a window opener.</summary>
    private static bool IsFoRow(L1011PlacedRow row) =>
        IsFoMapRow(row) || (row.Action == L1011RowAction.Custom
            && (row.Key == L1011Levers.StabTrimKey || L1011Levers.IsLever(row.Key)));

    /// <summary>A map row the First Officer writes through the panels' core.</summary>
    private static bool IsFoMapRow(L1011PlacedRow row) =>
        row.Action is L1011RowAction.Set or L1011RowAction.Press or L1011RowAction.Increase or L1011RowAction.Decrease;
}
