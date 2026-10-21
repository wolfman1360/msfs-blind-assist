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

    // ---- the composites' pseudo-keys ----

    /// <summary>The standby power check (target ignored).</summary>
    public const string StandbyPowerCheck = "FO_STANDBY_POWER_CHECK";

    /// <summary>External power on (1, with its ties) or off (0).</summary>
    public const string ExternalPower = "FO_EXTERNAL_POWER";

    /// <summary>
    /// The composites, by pseudo-key: each runs under the gate, writes through <see cref="WriteAsync"/>
    /// (never <see cref="Set"/>, which would wait on the gate it holds) and reads back what it changed.
    /// </summary>
    private static readonly Dictionary<string, Func<L1011FoActionExecutor, IL1011FoTransport, int, Task<bool>>> Pseudo =
        new(StringComparer.Ordinal)
        {
            [StandbyPowerCheck] = (exec, io, _) => exec.StandbyPowerCheckAsync(io),
            [ExternalPower] = (exec, io, target) => exec.ExternalPowerAsync(io, target),
        };

    // ---- the standby power check ----

    internal const string Battery = "TOGGLE_BATTERY";
    internal const string StandbyGuard = "GUARD_STBY_POWER";
    internal const string StandbyPowerSwitch = "TOGGLE_STBY_POWER";
    internal const string ExternalPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";

    /// <summary>A breaker or tie relay reads closed at or above this (1 closed, 0 open).</summary>
    private const double RelayClosedAtOrAbove = 0.5;

    /// <summary>The check's lamp mute, asked for at its start and again before steps 3 and 4: on the 1 Hz
    /// batch the check can outlast one window. A re-arm only moves the end later (the lamp gate keeps the
    /// later end); the end is left to this time-out, never cut short.</summary>
    internal const int StandbyCheckMuteMs = 20_000;

    private const string StandbyCheckName = "Standby power check";
    private const string DidNotComeOn = "did not come on";
    private const string DidNotGoOut = "did not go out";
    private const string CameOn = "came on";

    /// <summary>Lines 18 and 19: battery off, the guard open, standby power On.</summary>
    private static readonly (string Key, int Target)[] StandbyOnWrites =
    {
        (Battery, 0), (StandbyGuard, 1), (StandbyPowerSwitch, 2),
    };

    /// <summary>Lines 28 and 30: battery on, standby power Arm, the guard closed. Also the put-back after a
    /// write not done, so the aircraft is never left with the battery off.</summary>
    private static readonly (string Key, int Target)[] StandbyArmWrites =
    {
        (Battery, 1), (StandbyPowerSwitch, 1), (StandbyGuard, 0),
    };

    /// <summary>One light the check expects, and what it is said to have done when it reads otherwise.
    /// <paramref name="OnlyOffExternalPower"/>: expected only while the external power breaker reads open.</summary>
    private sealed record LightCheck(L1011Lamp Lamp, bool Lit, string WhenWrong, bool OnlyOffExternalPower = false)
    {
        public string Key => L1011Annunciators.KeyFor(Lamp);
    }

    private static readonly L1011Lamp UnarmedLamp = LampOf("STBY_UNARM_LIGHT");
    private static readonly L1011Lamp FlowbarLamp = LampOf("STBY_PWR_FLOWBAR_LIGHT");
    private static readonly L1011Lamp AcStandbyFailLamp = LampOf("AC_STBY_FAIL_LIGHT");
    private static readonly L1011Lamp DcStandbyFailLamp = LampOf("DC_STBY_FAIL_LIGHT");
    private static readonly L1011Lamp AcEssentialFailLamp = LampOf("AC_ESS_FAIL_LIGHT");
    private static readonly L1011Lamp DcEssentialFailLamp = LampOf("DC_ESS_FAIL_LIGHT");

    /// <summary>Step 2, with standby power On, as this aircraft shows it (live, 2026-10-21): the flowbar lit,
    /// both standby bus fail lights out, and both essential bus fail lights lit unless external power is on
    /// line. The unarmed light means "switch Off" here, so it is not expected lit.</summary>
    private static readonly LightCheck[] StandbyOnLights =
    {
        new(FlowbarLamp, Lit: true, DidNotComeOn),
        new(AcStandbyFailLamp, Lit: false, CameOn),
        new(DcStandbyFailLamp, Lit: false, CameOn),
        new(AcEssentialFailLamp, Lit: true, DidNotComeOn, OnlyOffExternalPower: true),
        new(DcEssentialFailLamp, Lit: true, DidNotComeOn, OnlyOffExternalPower: true),
    };

    /// <summary>Step 4, armed: the unarmed light, the flowbar and both standby bus fail lights out.</summary>
    private static readonly LightCheck[] StandbyArmedLights =
    {
        new(UnarmedLamp, Lit: false, CameOn),
        new(FlowbarLamp, Lit: false, DidNotGoOut),
        new(AcStandbyFailLamp, Lit: false, CameOn),
        new(DcStandbyFailLamp, Lit: false, CameOn),
    };

    private static L1011Lamp LampOf(string lampVar) => L1011Annunciators.All.Single(l => l.Var == lampVar);

    // ---- external power ----

    internal const string ExternalPowerSwitch = "TOGGLE_EXT_PWR";
    private static readonly L1011Lamp ExternalPowerAvailableLamp = LampOf("EXTERNAL_POWER_AVAILABLE");

    private static string TieClosePress(int n) => $"SWITCH_TIE_FLOW_{n.ToString(CultureInfo.InvariantCulture)}";
    private static string TieReadout(int n) => $"L1011_RO_AC_TIE_{n.ToString(CultureInfo.InvariantCulture)}";

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

    // ================= the standby power check =================

    /// <summary>
    /// The aircraft's checklist lines 18 to 32 with the lamp speech muted (<see cref="StandbyCheckMuteMs"/>,
    /// re-armed before steps 3 and 4, its end left to its time-out): battery off, the guard open, standby
    /// power On; the lights read (<see cref="StandbyOnLights"/>); battery on, standby power Arm, the guard
    /// closed; the lights read again (<see cref="StandbyArmedLights"/>). Done when every write was done and every light matched.
    /// A wrong light does not stop the check, so the aircraft still ends with the battery on and standby
    /// power armed. A write not done (or a throw) ends it at once with those three put back, best effort.
    /// Speech, through the announcer ([FO-26]): one sentence naming the first wrong light, always; "pass"
    /// only while flow progress speech is on; a write not done is the flow's to say.
    /// </summary>
    private async Task<bool> StandbyPowerCheckAsync(IL1011FoTransport io)
    {
        io.MuteLampSpeech(StandbyCheckMuteMs);
        string? wrongLight = null;
        bool passed = false;
        try
        {
            if (!await WriteEachAsync(io, StandbyOnWrites).ConfigureAwait(false))
            {
                await PutStandbyBackAsync(io).ConfigureAwait(false);
                return false;
            }
            wrongLight = await WrongLightAsync(io, StandbyOnLights, "standby power on").ConfigureAwait(false);
            // Re-armed: on the 1 Hz batch the check can outlast one window.
            io.MuteLampSpeech(StandbyCheckMuteMs);
            if (!await WriteEachAsync(io, StandbyArmWrites).ConfigureAwait(false))
            {
                await PutStandbyBackAsync(io).ConfigureAwait(false);
                return false;
            }
            io.MuteLampSpeech(StandbyCheckMuteMs);
            // Read even after a wrong light, for the log; only the first is spoken.
            string? armedWrong = await WrongLightAsync(io, StandbyArmedLights, "standby power armed").ConfigureAwait(false);
            wrongLight ??= armedWrong;
            passed = wrongLight is null;
            return passed;
        }
        catch
        {
            await PutStandbyBackAsync(io).ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (wrongLight is not null)
                _say?.Invoke(wrongLight);
            else if (passed && SpeaksFlowProgress)
                _say?.Invoke($"{StandbyCheckName}: pass");
        }
    }

    /// <summary>Each write in order; stops at the first not done (it was logged).</summary>
    private async Task<bool> WriteEachAsync(IL1011FoTransport io, IReadOnlyList<(string Key, int Target)> writes)
    {
        foreach (var (key, target) in writes)
            if (!await WriteAsync(io, key, target).ConfigureAwait(false))
                return false;
        return true;
    }

    /// <summary>Battery on, standby power Arm, the guard closed: each tried, whatever the others do.</summary>
    private async Task PutStandbyBackAsync(IL1011FoTransport io)
    {
        Note($"{StandbyCheckName}: not done, putting the battery back on and standby power back to Arm");
        foreach (var (key, target) in StandbyArmWrites)
        {
            try
            {
                await WriteAsync(io, key, target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Note($"{StandbyCheckName}: {key} -> {target.ToString(CultureInfo.InvariantCulture)} threw putting it back: {ex.Message}", warn: true);
            }
        }
    }

    /// <summary>
    /// Reads the lights fresh, in order, until each reads as expected, or, once <see cref="VerifyTimeoutMs"/>
    /// has passed on the transport's clock, the sentence naming the first that did not (logged). Null when
    /// all matched. A light expected only off external power is skipped unless the breaker reads open.
    /// </summary>
    private async Task<string?> WrongLightAsync(IL1011FoTransport io, IReadOnlyList<LightCheck> lights, string what)
    {
        long deadline = io.NowMs + VerifyTimeoutMs;
        while (true)
        {
            var wrong = await FirstWrongLightAsync(io, lights).ConfigureAwait(false);
            if (wrong is not { } w) return null;
            if (io.NowMs >= deadline)
            {
                string read = w.Reading is double r ? r.ToString(CultureInfo.InvariantCulture) : "unread";
                Note($"{StandbyCheckName}, {what}: {w.Check.Key} reads {read} after {VerifyTimeoutMs.ToString(CultureInfo.InvariantCulture)} ms, expected {(w.Check.Lit ? "lit" : "out")}");
                string did = w.Reading is null ? "could not be read" : w.Check.WhenWrong;
                return $"{StandbyCheckName}: {SpokenLightName(w.Check.Lamp)} {did}";
            }
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    private async Task<(LightCheck Check, double? Reading)?> FirstWrongLightAsync(IL1011FoTransport io, IReadOnlyList<LightCheck> lights)
    {
        bool? offExternalPower = null;
        foreach (var check in lights)
        {
            if (check.OnlyOffExternalPower)
            {
                // Read once a round, and only when needed: unread is not "open".
                offExternalPower ??= await ReadAsync(io, ExternalPowerBreaker).ConfigureAwait(false) is double b
                    && b < RelayClosedAtOrAbove;
                if (offExternalPower == false) continue;
            }
            var reading = await ReadAsync(io, check.Key).ConfigureAwait(false);
            if (reading is not double v || L1011Annunciators.IsLit(check.Lamp, v) != check.Lit)
                return (check, reading);
        }
        return null;
    }

    /// <summary>
    /// A lamp's spoken name inside a sentence: <see cref="L1011Lamp.Name"/> with its first letter lowered
    /// unless its first word is an abbreviation ("AC"), and "light" added when the name does not end in it:
    /// "standby power unarmed light", "AC standby bus fail light".
    /// </summary>
    internal static string SpokenLightName(L1011Lamp lamp)
    {
        string name = lamp.Name;
        int space = name.IndexOf(' ');
        string firstWord = space < 0 ? name : name[..space];
        if (!firstWord.All(char.IsUpper))
            name = char.ToLowerInvariant(name[0]) + name[1..];
        return name.EndsWith(" light", StringComparison.OrdinalIgnoreCase) ? name : name + " light";
    }

    // ================= external power =================

    /// <summary>
    /// Target 1: refused, nothing written, unless the external power available light reads lit; the switch
    /// on, verified on its breaker reading closed (not done otherwise); then each AC tie that reads open
    /// pressed closed and verified (one not closing does not stop the next; an unread tie is never pressed).
    /// Once the breaker reads closed the step is done, external power being on: a tie that did not close or
    /// could not be read is spoken in one sentence through the announcer, always ([FO-26]), naming the first
    /// ("External power on. AC tie 2 did not close."); the others are logged. Target 0: the switch off,
    /// verified on the breaker reading open; the ties are left alone. Any other target writes nothing.
    /// </summary>
    private async Task<bool> ExternalPowerAsync(IL1011FoTransport io, int target)
    {
        string what = $"{ExternalPower} -> {target.ToString(CultureInfo.InvariantCulture)}";
        if (target == 0)
            return await WriteAsync(io, ExternalPowerSwitch, 0).ConfigureAwait(false)
                && await ReadsAsync(io, new[] { ExternalPowerBreaker }, v => v < RelayClosedAtOrAbove, what).ConfigureAwait(false);
        if (target != 1)
        {
            Note($"{what}: not done, external power is 0 (off) or 1 (on)");
            return false;
        }

        string availableKey = L1011Annunciators.KeyFor(ExternalPowerAvailableLamp);
        var available = await ReadAsync(io, availableKey).ConfigureAwait(false);
        if (available is not double a || !L1011Annunciators.IsLit(ExternalPowerAvailableLamp, a))
        {
            Note($"{what}: not done, {availableKey} reads {(available is double d ? d.ToString(CultureInfo.InvariantCulture) : "unread")}: no ground power to connect");
            return false;
        }
        if (!await WriteAsync(io, ExternalPowerSwitch, 1).ConfigureAwait(false)
            || !await ReadsAsync(io, new[] { ExternalPowerBreaker }, v => v >= RelayClosedAtOrAbove, what).ConfigureAwait(false))
            return false;

        // From here external power is on, so the step is done whatever the ties do; a tie left open is a
        // failure of its own, spoken once ([FO-26]), naming the first (each is logged where it failed).
        string? firstTieFailure = null;
        for (int n = 1; n <= 3; n++)
        {
            string tie = TieReadout(n);
            string tieName = $"AC tie {n.ToString(CultureInfo.InvariantCulture)}";
            var reading = await ReadAsync(io, tie).ConfigureAwait(false);
            if (reading is not double t)
            {
                Note($"{what}: {tie} unread, its tie not pressed");
                firstTieFailure ??= $"{tieName} could not be read";
                continue;
            }
            if (t >= RelayClosedAtOrAbove) continue;   // closed already
            if (!await WriteAsync(io, TieClosePress(n), 1).ConfigureAwait(false)
                || !await ReadsAsync(io, new[] { tie }, v => v >= RelayClosedAtOrAbove, $"{what}, {tieName}").ConfigureAwait(false))
                firstTieFailure ??= $"{tieName} did not close";
        }
        if (firstTieFailure is not null)
            _say?.Invoke($"External power on. {firstTieFailure}.");
        return true;
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
