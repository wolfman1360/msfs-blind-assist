using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer's switch code, the MD-11's shape: one write at a time
/// behind one gate, through the TriStar definition's own First Officer entry
/// (<see cref="IL1011FoTransport"/>), which plans every write as the panels do (the click replay, the
/// refusal of a toggle from an unknown position, [L1011-4], the paddle's CWS notch, [L1011-21]).
///
/// A single write: a key whose reading follows its write (<see cref="IL1011FoTransport.CanReadBack"/>) is
/// read fresh FIRST, so the definition plans from a delivered value, never from an empty cache; then
/// written; then read back fresh until it reads the target or <see cref="VerifyTimeoutMs"/> passes. A
/// press, a spring switch or a knob holds no position to read: done once sent. A refusal, an unreachable
/// aircraft and an unknown key are not done and logged with the reason; nothing is spoken here (a flow
/// says "Skipping", a checklist tick reverts). Every transport write is bounded by
/// <see cref="WriteTimeoutMs"/> and every read-back loop by <see cref="VerifyTimeoutMs"/>, both on the
/// transport's clock, and nothing blocks a thread on a transport call: a caller on the UI thread waiting
/// on a held press would deadlock the post that runs it.
///
/// Composites are pseudo-keys (<see cref="Pseudo"/>), run under the gate like any write. Nothing is held
/// across a flow's Stop or the window's close in part 1, so <see cref="IFoActionExecutor.ReleaseHeldPresses"/>
/// and <see cref="IFoActionExecutor.OnFlowStoppedAsync"/> keep their defaults: a press still held when the
/// definition goes away is released by the definition's own dispose, once. The engine start (part 2) is
/// the first step a stopped flow must not leave half done, and brings its own.
/// </summary>
public sealed class L1011FoActionExecutor : IFoActionExecutor
{
    private const string LogCategory = "L1011 FO";

    // ---- timing ----

    /// <summary>A fresh read's time-out: the next 1 Hz batch, with margin.</summary>
    internal const int ReadTimeoutMs = 2500;

    /// <summary>How long a written key may take to read its target.</summary>
    internal const int VerifyTimeoutMs = 4000;

    internal const int PollMs = 250;

    /// <summary>The longest any write holds before it completes: a test button that only writes an L:var a
    /// gauge polls (<see cref="L1011WritePlan.PolledButtonHoldMs"/>). The transport does not say which key
    /// holds how long, so every key write is given this.</summary>
    internal const int LongestHoldMs = 2000;

    /// <summary>A posted write whose UI thread never runs it would never complete: past its hold and this,
    /// it is not done.</summary>
    internal const int WriteGraceMs = 10_000;

    internal const int WriteTimeoutMs = LongestHoldMs + WriteGraceMs;

    // ---- the automation's controls ----

    internal const string SeatBelts = "SWITCH_SEATBELT_LIGHT";
    internal const string LandingLightLeft = "TOGGLE_LANDING_LEFT";
    internal const string LandingLightRight = "TOGGLE_LANDING_RIGHT";

    internal const double StandardMillibars = 1013.25;

    /// <summary>An altimeter reads standard within this many hectopascals.</summary>
    internal const double AltimeterTolerance = 0.5;

    internal static readonly string[] AltimeterReadouts =
    {
        "L1011_RO_ALTIMETER_1", "L1011_RO_ALTIMETER_2", "L1011_RO_ALTIMETER_3",
    };

    /// <summary>A switch reads a position within this of it.</summary>
    private const double PositionTolerance = 0.5;

    /// <summary>
    /// The composites, by pseudo-key (empty until the first composite, the standby power check, joins
    /// it): each runs under the gate, writes through <see cref="WriteAsync"/> (never <see cref="Set"/>,
    /// which would wait on the gate it holds) and reads back what it changed.
    /// </summary>
    private static readonly Dictionary<string, Func<L1011FoActionExecutor, IL1011FoTransport, int, Task<bool>>> Pseudo =
        new(StringComparer.Ordinal);

    /// <summary>True for a composite's key. A flow key is known when this or the definition's
    /// <c>FoAcceptsKey</c> is true.</summary>
    public static bool IsPseudoKey(string key) => Pseudo.ContainsKey(key);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IL1011FoTransport? _io;
    private Action<string>? _say;
    private Func<bool> _speaksFlowProgress = () => true;

    public void SetTransport(IL1011FoTransport? io) => _io = io;

    /// <summary>The announcer's queued <c>Announce</c>: a composite's verdict of its own ([FO-26]).</summary>
    public void SetSpeech(Action<string>? say) => _say = say;

    public bool IsAvailable => _io is { Ready: true };

    public void AttachFlowProgressSpeech(Func<bool> speaksProgress) => _speaksFlowProgress = speaksProgress;

    /// <summary>The flow's "Speak flow progress" setting, read at each line (on until a flow attaches it).</summary>
    internal bool SpeaksFlowProgress => _speaksFlowProgress();

    /// <summary>Where a composite's own line goes (null until the profile wires the announcer).</summary>
    internal Action<string>? Speech => _say;

    /// <summary>Tests only: every line this executor logs, as it is logged. Never set in the app.</summary>
    internal Action<string>? LogTap { get; set; }

    public async Task WaitForDispatchDrainAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        _gate.Release();
    }

    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
    {
        if (!IsAvailable) return false;
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                return await Set(step.EventName, step.TargetValue ?? 1).ConfigureAwait(false);
            case FlowStepActionType.SetSwitchMultiple:
                if (step.MultiActions.Count == 0) return false;
                // Every part is tried, in order, each under the gate: one that fails does not stop the rest.
                bool ok = true;
                foreach (var (key, target) in step.MultiActions)
                    ok &= await Set(key, target ?? 1).ConfigureAwait(false);
                return ok;
            default:
                return false;
        }
    }

    /// <summary>One control, serialized with every other write. A checklist tick: never cut short.</summary>
    public Task<bool> Set(string key, int target) =>
        GatedAsync($"{key} -> {target.ToString(CultureInfo.InvariantCulture)}", io => DispatchAsync(io, key, target));

    public Task<bool> SetSeatbeltSign(bool on) => Set(SeatBelts, on ? 1 : 0);

    /// <summary>Both landing lights; the second is set even when the first is not done.</summary>
    public async Task<bool> SetLandingLights(bool on)
    {
        bool left = await Set(LandingLightLeft, on ? 1 : 0).ConfigureAwait(false);
        bool right = await Set(LandingLightRight, on ? 1 : 0).ConfigureAwait(false);
        return left && right;
    }

    /// <summary>The captain's, first officer's and standby altimeters to 1013.25 in one write, done when
    /// all three read it, each read fresh.</summary>
    public Task<bool> SetAltimetersStandardAsync() =>
        GatedAsync("altimeters standard", AltimetersStandardAsync);

    // ================= the gate =================

    /// <summary>
    /// Runs <paramref name="work"/> alone. Refuses at once, without waiting for the gate, when the aircraft
    /// cannot be reached; re-reads the transport once through the gate, so a write queued while the window
    /// closed writes nothing. The gate is always released; a throw is not done, logged.
    /// </summary>
    private async Task<bool> GatedAsync(string what, Func<IL1011FoTransport, Task<bool>> work)
    {
        if (_io is not { Ready: true }) return false;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var io = _io;
            if (io is not { Ready: true })
            {
                Note($"{what}: not done, the aircraft can no longer be reached");
                return false;
            }
            bool done = await work(io).ConfigureAwait(false);
            Log.Debug(LogCategory, $"{what}: {(done ? "done" : "not done")}");
            return done;
        }
        catch (Exception ex)
        {
            Note($"{what}: not done, it threw: {ex.Message}", warn: true);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Task<bool> DispatchAsync(IL1011FoTransport io, string key, int target) =>
        Pseudo.TryGetValue(key, out var composite) ? composite(this, io, target) : WriteAsync(io, key, target);

    // ================= a single write =================

    /// <summary>
    /// One control to <paramref name="target"/>, under the gate: read first when its reading follows its
    /// write, written, and read back; a press done once sent. For a composite's own writes too.
    /// </summary>
    private async Task<bool> WriteAsync(IL1011FoTransport io, string key, int target)
    {
        string what = $"{key} -> {target.ToString(CultureInfo.InvariantCulture)}";
        bool readable = io.CanReadBack(key);
        // The definition plans from what it last read: a fresh delivery first, so a toggle is decided
        // on a delivered value, and refused by the definition when there is none.
        if (readable)
            await ReadAsync(io, key).ConfigureAwait(false);
        if (await SendAsync(io, what, io.SetAsync(key, target), LongestHoldMs).ConfigureAwait(false) is not { } result)
            return false;
        switch (result.Outcome)
        {
            case L1011FoOutcome.AlreadyThere:
                return true;
            case L1011FoOutcome.Sent:
                return !readable
                    || await ReadsAsync(io, new[] { key }, v => Math.Abs(v - target) < PositionTolerance, what).ConfigureAwait(false);
            default:
                Note($"{what}: not sent, {Describe(result)}");
                return false;
        }
    }

    private async Task<bool> AltimetersStandardAsync(IL1011FoTransport io)
    {
        const string what = "altimeters standard";
        if (await SendAsync(io, what, io.SetAltimetersAsync(StandardMillibars), holdMs: 0).ConfigureAwait(false) is not { } result)
            return false;
        switch (result.Outcome)
        {
            case L1011FoOutcome.AlreadyThere:
                return true;
            case L1011FoOutcome.Sent:
                return await ReadsAsync(io, AltimeterReadouts,
                    v => Math.Abs(v - StandardMillibars) < AltimeterTolerance, what).ConfigureAwait(false);
            default:
                Note($"{what}: not sent, {Describe(result)}");
                return false;
        }
    }

    /// <summary>
    /// Awaits a transport write for its hold plus <see cref="WriteGraceMs"/> at most, on the transport's
    /// clock. Null (not done, logged) when it does not complete by then: a post the UI thread never runs.
    /// </summary>
    private async Task<L1011FoResult?> SendAsync(IL1011FoTransport io, string what, Task<L1011FoResult> write, int holdMs)
    {
        if (!write.IsCompleted)
        {
            int limitMs = Math.Min(holdMs, LongestHoldMs) + WriteGraceMs;
            var timeout = io.DelayAsync(limitMs);
            if (await Task.WhenAny(write, timeout).ConfigureAwait(false) != write)
            {
                // A late failure is observed here, never left to the finalizer.
                _ = write.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                Note($"{what}: not done, the write did not complete within {limitMs.ToString(CultureInfo.InvariantCulture)} ms", warn: true);
                return null;
            }
        }
        return await write.ConfigureAwait(false);
    }

    // ================= read-back =================

    /// <summary>A fresh read; null when nothing (or NaN) was delivered in <see cref="ReadTimeoutMs"/>.</summary>
    private static async Task<double?> ReadAsync(IL1011FoTransport io, string key)
    {
        var v = await io.ReadFreshAsync(key, ReadTimeoutMs).ConfigureAwait(false);
        return v is double d && !double.IsNaN(d) ? d : null;
    }

    /// <summary>
    /// Reads every key fresh, in order, until each satisfies <paramref name="ok"/>, or not done (logged,
    /// naming the first key that did not) once <see cref="VerifyTimeoutMs"/> has passed on the transport's
    /// clock.
    /// </summary>
    private async Task<bool> ReadsAsync(IL1011FoTransport io, IReadOnlyList<string> keys, Func<double, bool> ok, string what)
    {
        long deadline = io.NowMs + VerifyTimeoutMs;
        while (true)
        {
            string? notThere = null;
            double? reading = null;
            foreach (var key in keys)
            {
                reading = await ReadAsync(io, key).ConfigureAwait(false);
                if (reading is not double v || !ok(v))
                {
                    notThere = key;
                    break;
                }
            }
            if (notThere is null) return true;
            if (io.NowMs >= deadline)
            {
                string read = reading is double r ? r.ToString(CultureInfo.InvariantCulture) : "unread";
                Note($"{what}: not done, {notThere} reads {read} after {VerifyTimeoutMs.ToString(CultureInfo.InvariantCulture)} ms");
                return false;
            }
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    // ================= logging =================

    private static string Describe(L1011FoResult result) =>
        result.Reason is { } why ? $"{result.Outcome}: {why}" : result.Outcome.ToString();

    /// <summary>A not-done line for debug.log ([CORE-15]); never spoken.</summary>
    private void Note(string line, bool warn = false)
    {
        if (warn) Log.Warn(LogCategory, line);
        else Log.Info(LogCategory, line);
        LogTap?.Invoke(line);
    }
}
