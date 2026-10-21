using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// The First Officer's write entry: the panels' own write core (<see cref="PlanFor"/>,
/// <see cref="ApplyPlan"/>), so a flow step writes a control exactly as a panel pick does, click replay,
/// toggle refusal from an unknown position ([L1011-4]) and the paddle's CWS notch ([L1011-21])
/// included. Unlike the panels it speaks nothing and returns what happened
/// (<see cref="L1011FoResult"/>), and it completes only once the plan has run, a button's hold
/// included. A press still held when the definition goes away is released by <see cref="Dispose"/>,
/// once. The hand-written levers and the stabilizer trim are sent as the panel rows send them
/// (<see cref="L1011Levers"/>), and the altimeters in one string. The First Officer's executor calls in
/// from pool threads, so every write (and the lamp mute) hops onto the UI thread captured at
/// <see cref="Attach"/> first (<see cref="OnUiThreadAsync{T}"/>). The universal autopilot engage
/// (<see cref="EngageAutopilot"/>) presses paddle A through the same entry.
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
    /// the plan has run. Marshals onto the UI thread captured at <see cref="Attach"/>, because the state
    /// it touches (the commanded positions, the write generations, the owed releases) and SimConnect
    /// are UI-thread only; <see cref="L1011FoOutcome.Unavailable"/> when that thread can no longer take
    /// the call.
    /// </summary>
    internal Task<L1011FoResult> FoSetAsync(string key, double value) =>
        OnUiThreadAsync(() => FoSetOnUiThreadAsync(key, value), L1011FoResult.Unavailable);

    /// <summary>The body of <see cref="FoSetAsync"/>, run on the UI thread.</summary>
    private async Task<L1011FoResult> FoSetOnUiThreadAsync(string key, double value)
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
    /// away, or the write cannot land. Marshals onto the UI thread captured at <see cref="Attach"/>,
    /// because SimConnect is UI-thread only; <see cref="L1011FoOutcome.Unavailable"/> when that thread
    /// can no longer take the call.
    /// </summary>
    internal Task<L1011FoResult> FoSetAltimetersAsync(double millibars) =>
        OnUiThreadAsync(() => Task.FromResult(FoSetAltimetersOnUiThread(millibars)), L1011FoResult.Unavailable);

    /// <summary>The body of <see cref="FoSetAltimetersAsync"/>, run on the UI thread.</summary>
    private L1011FoResult FoSetAltimetersOnUiThread(double millibars)
    {
        if (_disposed || _sim is not { } sim)
            return L1011FoResult.Unavailable;
        if (L1011Levers.AltimeterMillibars(millibars) is not double setting)
            return L1011FoResult.Refused(L1011Levers.AltimeterRangeError);
        if (!CanLand(sim))
            return L1011FoResult.Unavailable;
        SendCalc(sim, L1011Levers.AllAltimetersRpn(setting));
        return L1011FoResult.Sent;
    }

    /// <summary>
    /// Silences the warning-light call-outs for <paramref name="ms"/> from now: a change the First
    /// Officer's own write causes (a light test, a start) is not news. A running annunciator light test
    /// or its tail is never shortened. Marshals onto the UI thread captured at <see cref="Attach"/>,
    /// because the lamp gate is UI-thread state; it is posted in order with the writes around it, and
    /// dropped when that thread can no longer take it. The window runs from this call, not from when
    /// the UI thread gets to it.
    /// </summary>
    internal void FoMuteLampSpeech(int ms)
    {
        long until = Clock() + ms;
        OnUiThread(() => _lampGate.MuteUntil(until));
    }

    /// <summary>True when the caller is already on the UI thread's context, or none was captured (tests,
    /// headless): the work then runs inline, where posting to the context the caller is blocking could
    /// deadlock.</summary>
    private bool OnUiContext => _uiContext is not { } context || SynchronizationContext.Current == context;

    /// <summary>
    /// Runs <paramref name="work"/> on the UI thread captured at <see cref="Attach"/>, whatever thread
    /// calls. Inline when the caller is already on that context or none was captured. Otherwise it is
    /// posted, and the returned task completes with the work's result, a failure inside the work
    /// surfacing on the task (inline or posted, never thrown from this call). The caller's own
    /// continuations run asynchronously, never on the UI thread inside the work. A UI thread that can
    /// no longer take a post (its window is gone: the app is closing) never runs the work elsewhere:
    /// the task completes with <paramref name="whenUnreachable"/>.
    /// </summary>
    private Task<T> OnUiThreadAsync<T>(Func<Task<T>> work, T whenUnreachable)
    {
        if (OnUiContext)
        {
            try
            {
                return work();
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _uiContext!.Post(async _ =>
            {
                try
                {
                    done.TrySetResult(await work());
                }
                catch (Exception ex)
                {
                    done.TrySetException(ex);
                }
            }, null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Log.Debug("L1011", $"UI thread unavailable; a First Officer call was dropped: {ex.Message}");
            done.TrySetResult(whenUnreachable);
        }
        return done.Task;
    }

    /// <summary>As <see cref="OnUiThreadAsync{T}"/>, for work nobody waits on: inline on the UI thread's
    /// context (or with none captured), else posted, and dropped when that thread can no longer take it.</summary>
    private void OnUiThread(Action action)
    {
        if (OnUiContext)
        {
            action();
            return;
        }
        try
        {
            _uiContext!.Post(_ => action(), null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            Log.Debug("L1011", $"UI thread unavailable; a First Officer call was dropped: {ex.Message}");
        }
    }

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

    // ---- The universal auto-engage -----------------------------------------------------------------
    //
    // UniversalAutomationService engages the autopilot through the definition (EngageAutopilot) and, as
    // IsAutopilotEngaged is non-null, works closed loop on it ([FO-18]): it announces only a CONFIRMED
    // engagement, retries a rejected press and reports a Captain action if it never takes. The default
    // press, the stock AUTOPILOT_ON, is ignored by the paddle-driven autopilot.

    /// <summary>The two engage paddles (map positions: 0 Command, 1 CWS, 2 Off). The auto-engage presses A
    /// and never touches B.</summary>
    private const string EngagePaddleAKey = "SWITCH_AFCS_AP_A";
    private const string EngagePaddleBKey = "SWITCH_AFCS_AP_B";

    /// <summary>
    /// True when either engage paddle reads Command, false when both are read and neither is, null while
    /// either is unread and the other is not at Command (an engaged paddle is a fact whatever the other
    /// reads). Read as the aircraft REPORTS it (<see cref="ReportedPosition"/>), never as MSFSBA just
    /// commanded it, so the service's "Autopilot engaged" follows the aircraft and not our own press.
    /// CWS is not engaged: it flies nothing of its own.
    /// </summary>
    public override bool? IsAutopilotEngaged(SimConnectManager simConnect)
    {
        double? a = ReadEngagePaddle(EngagePaddleAKey, simConnect);
        double? b = ReadEngagePaddle(EngagePaddleBKey, simConnect);
        if (AtCommand(a) || AtCommand(b))
            return true;
        return a is null || b is null ? null : false;
    }

    /// <summary>A paddle's reported position; a NaN is no reading.</summary>
    private double? ReadEngagePaddle(string key, SimConnectManager sim) =>
        ReportedPosition(key, sim) is double v && !double.IsNaN(v) ? v : null;

    private static bool AtCommand(double? paddle) => paddle is double v && Math.Abs(v) < 0.5;

    /// <summary>The paddle's reading comes with the 1 Hz batch, after the CWS notch and the settle: presses
    /// 3 s apart, so a retry never lands on a press that is still settling.</summary>
    public override int AutopilotEngageRetryIntervalMs => ToggleAutopilotEngageRetryIntervalMs;

    /// <summary>The engage presses nothing on an unread paddle, so the service must wait for a reading
    /// rather than take the open-loop path, which announced "Autopilot engaged" over no press at all.</summary>
    public override bool AutopilotEngageWaitsForState => true;

    /// <summary>
    /// Universal auto-engage: paddle A to Command, only when both paddles read and neither is at Command.
    /// The press is the First Officer's own entry (<see cref="FoSetAsync"/>), so it is the panel row's
    /// click replay, with the CWS notch from Off ([L1011-21]), its paddle moves recorded as ours (the
    /// service's confirmation is the one line) and nothing spoken. Never the stock AUTOPILOT_ON. Called on
    /// the UI thread, so the write runs inline; a write that cannot land sends nothing and the service
    /// retries, then reports a Captain action. No engage floor: the manual gives none.
    /// </summary>
    public override void EngageAutopilot(SimConnectManager simConnect)
    {
        if (IsAutopilotEngaged(simConnect) != false)
            return;
        Attach(simConnect);
        _ = FoSetAsync(EngagePaddleAKey, 0);
    }
}
