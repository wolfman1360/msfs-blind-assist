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
/// Composites are pseudo-keys. One in <see cref="Pseudo"/> runs under the gate like any write and is atomic.
/// A flow step hands the flow's Stop to every key, but only a composite in <see cref="StopAware"/> reads it,
/// and no wait ever holds the gate (ruling R23): it runs outside the gate, takes it for each write phase
/// (<see cref="UnderGateAsync"/>: the reads that decide a write and the write, never after the Stop) and
/// releases it before every wait, which is read-only, bounded on the transport's clock and, for a flow step
/// only, held by the flow's Pause without counting it (<see cref="WaitUntilAsync"/>). So a paused flow never
/// queues a hand tick, the automation or the drain behind its wait. Nothing is held
/// across a flow's Stop or the window's close yet, so <see cref="IFoActionExecutor.ReleaseHeldPresses"/>
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

    /// <summary>The speed brake reads its target within this many percent (forward reads below 5).</summary>
    internal const double SpeedBrakeTolerancePercent = 5;

    /// <summary>The stabilizer trim reads its target within this many degrees (the evaluator's zero trim
    /// test uses the same figure).</summary>
    internal const double StabTrimToleranceDegrees = 0.2;

    /// <summary>The write-only keys still verified, each on its readout: the stabilizer trim's typed entry
    /// on ELEVATOR TRIM POSITION.</summary>
    private static readonly IReadOnlyDictionary<string, string> ReadBackKeys =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [L1011Levers.StabTrimKey] = "L1011_RO_ELEVATOR_TRIM",
        };

    // ---- the composites' pseudo-keys ----

    /// <summary>The standby power check (target ignored).</summary>
    public const string StandbyPowerCheck = "FO_STANDBY_POWER_CHECK";

    /// <summary>External power on (1, with its ties) or off (0).</summary>
    public const string ExternalPower = "FO_EXTERNAL_POWER";

    /// <summary>The three INS mode selectors to ALIGN, a unit at ALIGN or NAV left alone (target ignored).</summary>
    public const string InsAlign = "FO_INS_ALIGN";

    /// <summary>The three AC ties closed (target 1).</summary>
    public const string AcTies = "FO_AC_TIES";

    /// <summary>The APU started (target ignored): one press, done once the RPM rises; reads the flow's Stop.</summary>
    public const string ApuStart = "FO_APU_START";

    /// <summary>The APU generator on line (1: field, then breaker) or off line (0: the breaker tripped).</summary>
    public const string ApuGenerator = "FO_APU_GENERATOR";

    /// <summary>The engine and APU fire test (target ignored): one spoken verdict.</summary>
    public const string FireTest = "FO_FIRE_TEST";

    /// <summary>The wheel well fire test (target ignored): one spoken verdict.</summary>
    public const string WheelWellFireTest = "FO_WHEEL_WELL_FIRE_TEST";

    /// <summary>The hydraulic check (target ignored): one spoken verdict.</summary>
    public const string HydraulicCheck = "FO_HYDRAULIC_CHECK";

    /// <summary>
    /// The composites, by pseudo-key: each runs under the gate, writes through <see cref="WriteAsync"/>
    /// (never <see cref="Set"/>, which would wait on the gate it holds) and reads back what it changed.
    /// Atomic: none reads the flow's Stop.
    /// </summary>
    private static readonly Dictionary<string, Func<L1011FoActionExecutor, IL1011FoTransport, int, Task<bool>>> Pseudo =
        new(StringComparer.Ordinal)
        {
            [StandbyPowerCheck] = (exec, io, _) => exec.StandbyPowerCheckAsync(io),
            [ExternalPower] = (exec, io, target) => exec.ExternalPowerAsync(io, target),
            [InsAlign] = (exec, io, _) => exec.InsAlignAsync(io),
            [AcTies] = (exec, io, target) => exec.AcTiesAsync(io, target),
            [ApuGenerator] = (exec, io, target) => exec.ApuGeneratorAsync(io, target),
            // The audible tests (ruling R24): atomic, so no hand tick moves a test switch or pump mid-test.
            [FireTest] = (exec, io, _) => exec.FireTestAsync(io),
            [WheelWellFireTest] = (exec, io, _) => exec.WheelWellFireTestAsync(io),
            [HydraulicCheck] = (exec, io, _) => exec.HydraulicCheckAsync(io),
        };

    /// <summary>
    /// The composites that read the flow's Stop (a wait that may end early in a safe state); looked up first,
    /// before the gate (<see cref="SetCoreAsync"/>). Each runs OUTSIDE the gate and takes it for each write
    /// phase through <see cref="UnderGateAsync"/>, so it is handed no transport: a phase gets the one read
    /// under the gate (ruling R23). A checklist tick passes <see cref="CancellationToken.None"/>.
    /// </summary>
    private static readonly Dictionary<string, Func<L1011FoActionExecutor, int, CancellationToken, Task<bool>>> StopAware =
        new(StringComparer.Ordinal)
        {
            [ApuStart] = (exec, _, stop) => exec.ApuStartAsync(stop),
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

    // ---- the APU ----

    internal const string ApuMaster = "TOGGLE_APU_POWER";
    internal const string ApuStartPress = "SWITCH_APU_START";
    internal const string ApuRpm = "L1011_RO_APU_RPM";
    internal const string ApuGenFieldPress = "SWITCH_APU_BRG";
    internal const string ApuGenBreakerPress = "SWITCH_GND_FLOW";
    internal const string ApuGenBreakerTrip = "SWITCH_GND_FLOW_OPEN";
    internal const string ApuGenFieldRelay = "L1011_RO_APU_GEN_FIELD";
    internal const string ApuGenBreakerRelay = "L1011_RO_APU_GEN_BREAKER";

    /// <summary>How long the APU's RPM may read 0 after its start press: live, it read 0 for about 7 s while
    /// the inlet doors opened, then rose about 3 %/s.</summary>
    internal const int ApuSpinUpWaitMs = 20_000;

    // ---- the audible tests ----

    internal const int FireTestMuteMs = 45_000;
    internal const int HydraulicCheckMuteMs = 60_000;
    internal const double HydraulicNormalPsi = 2000;
    internal const double HydraulicAMinPsi = 900;
    internal const double HydraulicDMinPsi = 1400;
    internal const double HydraulicZeroPsi = 100;
    internal const int HydraulicRiseWaitMs = 20_000;
    internal const int HydraulicBleedWaitMs = 30_000;

    /// <summary>True for a composite's key, Stop-aware or not. A flow key is known when this or the
    /// definition's <c>FoAcceptsKey</c> is true.</summary>
    public static bool IsPseudoKey(string key) => Pseudo.ContainsKey(key) || StopAware.ContainsKey(key);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IL1011FoTransport? _io;
    private Action<string>? _say;
    private Func<bool> _speaksFlowProgress = () => true;
    private Func<CancellationToken, Task> _waitWhilePaused = _ => Task.CompletedTask;

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

    public Task<bool> ExecuteStepAsync(IFlowStepDispatch step) => ExecuteStepAsync(step, CancellationToken.None);

    /// <summary>
    /// The flow's entry point; <paramref name="stop"/> is the running flow's Stop. Every key goes through
    /// <see cref="SetCoreAsync"/> with it, but only a composite in <see cref="StopAware"/> reads it (outside
    /// the gate, ruling R23): a plain switch, a press and every other composite are atomic and ignore it.
    /// </summary>
    public async Task<bool> ExecuteStepAsync(IFlowStepDispatch step, CancellationToken stop)
    {
        if (!IsAvailable) return false;
        switch (step.ActionType)
        {
            case FlowStepActionType.SetSwitch:
                if (step.EventName == null) return false;
                return await SetCoreAsync(step.EventName, step.TargetValue ?? 1, stop).ConfigureAwait(false);
            case FlowStepActionType.SetSwitchMultiple:
                if (step.MultiActions.Count == 0) return false;
                // Every part is tried, in order, each under the gate: one that fails does not stop the rest.
                bool ok = true;
                foreach (var (key, target) in step.MultiActions)
                    ok &= await SetCoreAsync(key, target ?? 1, stop).ConfigureAwait(false);
                return ok;
            default:
                return false;
        }
    }

    /// <summary>The running flow's Pause (FlowManager hands it over once): a flow step's wait
    /// (<see cref="WaitUntilAsync"/>) holds on it and does not count the time paused; a hand tick's never
    /// does. Completes at once until a flow attaches it.</summary>
    public void AttachFlowPause(Func<CancellationToken, Task> waitWhilePaused) => _waitWhilePaused = waitWhilePaused;

    /// <summary>One control, serialized with every other write. A checklist tick: never cut short.</summary>
    public Task<bool> Set(string key, int target) => SetCoreAsync(key, target, CancellationToken.None);

    /// <summary>
    /// One control, with the flow's Stop for a composite that reads it: a <see cref="StopAware"/> composite
    /// looked up first and run outside the gate (<see cref="StopAwareAsync"/>, ruling R23); anything else
    /// under the gate.
    /// </summary>
    private Task<bool> SetCoreAsync(string key, int target, CancellationToken stop)
    {
        string what = $"{key} -> {target.ToString(CultureInfo.InvariantCulture)}";
        if (StopAware.TryGetValue(key, out var stopAware))
            return StopAwareAsync(what, stopAware, target, stop);
        return GatedAsync(what, io => DispatchAsync(io, key, target));
    }

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

    /// <summary>Under the gate: a <see cref="Pseudo"/> composite, else a single write.</summary>
    private Task<bool> DispatchAsync(IL1011FoTransport io, string key, int target)
    {
        if (Pseudo.TryGetValue(key, out var composite)) return composite(this, io, target);
        return WriteAsync(io, key, target);
    }

    // ================= Stop-aware composites, outside the gate (ruling R23) =================

    /// <summary>
    /// A <see cref="StopAware"/> composite, never under the gate: it takes the gate itself for each write
    /// phase (<see cref="UnderGateAsync"/>) and holds none across a wait. Refuses at once, without waiting
    /// for anything, when the aircraft cannot be reached; a throw is not done, logged.
    /// </summary>
    private async Task<bool> StopAwareAsync(string what, Func<L1011FoActionExecutor, int, CancellationToken, Task<bool>> composite,
        int target, CancellationToken stop)
    {
        if (_io is not { Ready: true }) return false;
        try
        {
            bool done = await composite(this, target, stop).ConfigureAwait(false);
            Log.Debug(LogCategory, $"{what}: {(done ? "done" : "not done")}");
            return done;
        }
        catch (Exception ex)
        {
            Note($"{what}: not done, it threw: {ex.Message}", warn: true);
            return false;
        }
    }

    /// <summary>
    /// One write phase of a <see cref="StopAware"/> composite: the reads that decide a write and the write,
    /// under the gate, on the transport read again there (detached or not ready: not done, logged). Never
    /// after the flow's Stop: one arriving while the phase queues for the gate, or found once it has it, is
    /// not done, logged "<paramref name="what"/>: stopped", with nothing read or written. The gate is always
    /// released before the caller's next wait.
    /// </summary>
    private async Task<bool> UnderGateAsync(string what, CancellationToken stop, Func<IL1011FoTransport, Task<bool>> phase)
    {
        try
        {
            await _gate.WaitAsync(stop).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return Stopped(what);
        }
        try
        {
            var io = _io;
            if (io is not { Ready: true })
            {
                Note($"{what}: not done, the aircraft can no longer be reached");
                return false;
            }
            if (stop.IsCancellationRequested)
                return Stopped(what);
            return await phase(io).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    // ================= a single write =================

    /// <summary>
    /// One control to <paramref name="target"/>, under the gate: read first when its reading follows its
    /// write, written, and read back on its own scale (<see cref="LeverReadsTarget"/>); the stabilizer trim,
    /// write-only, read back on its readout (<see cref="ReadBackKeyFor"/>); a press done once sent. For a
    /// composite's own writes too.
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
                string? readout = readable ? key : ReadBackKeyFor(key);
                return readout is null
                    || await ReadsAsync(io, new[] { readout }, v => LeverReadsTarget(key, v, target), what).ConfigureAwait(false);
            default:
                Note($"{what}: not sent, {Describe(result)}");
                return false;
        }
    }

    /// <summary>
    /// True when a reading of the key written (<paramref name="key"/>, or its readout) shows
    /// <paramref name="target"/> on that control's own scale: the flap handle by detent, the gear lever by
    /// band (it travels between down, off and up, <see cref="L1011Levers.GearDescriptionKey"/>), the speed
    /// brake within <see cref="SpeedBrakeTolerancePercent"/> (both in percent; forward reads below 5), the
    /// stabilizer trim within <see cref="StabTrimToleranceDegrees"/>, and anything else, the parking brake
    /// and the ground spoilers included, within half a position.
    /// </summary>
    internal static bool LeverReadsTarget(string key, double read, double target) => key switch
    {
        L1011Levers.FlapHandleKey => Math.Round(read) == Math.Round(target),
        L1011Levers.GearLeverKey => L1011Levers.GearDescriptionKey(read) == L1011Levers.GearDescriptionKey(target),
        L1011Levers.SpeedBrakeKey => Math.Abs(read - target) <= SpeedBrakeTolerancePercent,
        L1011Levers.ParkingBrakeKey or L1011Levers.GroundSpoilersKey => Math.Abs(read - target) < PositionTolerance,
        L1011Levers.StabTrimKey => Math.Abs(read - target) < StabTrimToleranceDegrees,
        _ => Math.Abs(read - target) < PositionTolerance,
    };

    /// <summary>The readout a write-only key is still verified on: the stabilizer trim's typed entry, on the
    /// trim position (degrees, the 0 to 10 scale the entry takes). Null for every other key.</summary>
    private static string? ReadBackKeyFor(string key) =>
        ReadBackKeys.TryGetValue(key, out var readout) ? readout : null;

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
            wrongLight = await WrongLightAsync(io, StandbyCheckName, StandbyOnLights, "standby power on").ConfigureAwait(false);
            // Re-armed: on the 1 Hz batch the check can outlast one window.
            io.MuteLampSpeech(StandbyCheckMuteMs);
            if (!await WriteEachAsync(io, StandbyArmWrites).ConfigureAwait(false))
            {
                await PutStandbyBackAsync(io).ConfigureAwait(false);
                return false;
            }
            io.MuteLampSpeech(StandbyCheckMuteMs);
            // Read even after a wrong light, for the log; only the first is spoken.
            string? armedWrong = await WrongLightAsync(io, StandbyCheckName, StandbyArmedLights, "standby power armed").ConfigureAwait(false);
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
            SpeakVerdict(StandbyCheckName, wrongLight, passed);
        }
    }

    /// <summary>
    /// A test's one verdict, through the announcer ([FO-26], [FOL-2]): the sentence naming the first failure,
    /// always; "<paramref name="name"/>: pass" only while flow progress speech is on; nothing for a test ended by a
    /// write not done (the flow says "Skipping").
    /// </summary>
    private void SpeakVerdict(string name, string? firstFailure, bool passed)
    {
        if (firstFailure is not null)
            _say?.Invoke(firstFailure);
        else if (passed && SpeaksFlowProgress)
            _say?.Invoke($"{name}: pass");
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
        await TryEachAsync(io, StandbyCheckName, StandbyArmWrites).ConfigureAwait(false);
    }

    /// <summary>A test's put-back, best effort: each write tried, whatever the others do; a throw is logged
    /// and the next still tried.</summary>
    private async Task TryEachAsync(IL1011FoTransport io, string name, IReadOnlyList<(string Key, int Target)> writes)
    {
        foreach (var (key, target) in writes)
        {
            try
            {
                await WriteAsync(io, key, target).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Note($"{name}: {key} -> {target.ToString(CultureInfo.InvariantCulture)} threw putting it back: {ex.Message}", warn: true);
            }
        }
    }

    /// <summary>
    /// Reads the lights fresh, in order, until each reads as expected, or, once <see cref="VerifyTimeoutMs"/>
    /// has passed on the transport's clock, the sentence naming the first that did not, as
    /// "<paramref name="testName"/>: <c>light</c> did not come on" (logged). Null when all matched. A light
    /// expected only off external power is skipped unless the breaker reads open.
    /// </summary>
    private async Task<string?> WrongLightAsync(IL1011FoTransport io, string testName, IReadOnlyList<LightCheck> lights, string what)
    {
        long deadline = io.NowMs + VerifyTimeoutMs;
        while (true)
        {
            var wrong = await FirstWrongLightAsync(io, lights).ConfigureAwait(false);
            if (wrong is not { } w) return null;
            if (io.NowMs >= deadline)
            {
                string read = w.Reading is double r ? r.ToString(CultureInfo.InvariantCulture) : "unread";
                Note($"{testName}, {what}: {w.Check.Key} reads {read} after {VerifyTimeoutMs.ToString(CultureInfo.InvariantCulture)} ms, expected {(w.Check.Lit ? "lit" : "out")}");
                string did = w.Reading is null ? "could not be read" : w.Check.WhenWrong;
                return $"{testName}: {SpokenLightName(w.Check.Lamp)} {did}";
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

    // ================= the audible tests (ruling R24) =================

    private const string FireTestName = "Engine and APU fire test";
    private const string WheelWellFireTestName = "Wheel well fire test";
    private const string HydraulicCheckName = "Hydraulic check";

    private const string FireTestSwitchA = "SWITCH_FIRE_DETECT_TEST_A";
    private const string FireTestSwitchB = "SWITCH_FIRE_DETECT_TEST_B";

    /// <summary>The fire loop selectors' Both position. Live, the single-loop positions do not match their
    /// labels, so the test runs on Both only, and leaves the selectors there.</summary>
    private const int FireLoopsBoth = 2;

    private static readonly string[] FireLoopSelectors =
    {
        "ROTARY_FIRE_DETECT_TEST_ENG_1", "ROTARY_FIRE_DETECT_TEST_ENG_2", "ROTARY_FIRE_DETECT_TEST_ENG_3", "ROTARY_FIRE_DETECT_TEST_APU",
    };

    /// <summary>What either loop test lit on Both, live: the three engine fire lights, the engine fire warning and
    /// the APU fire light. The fire detection loop light (<c>LX_MIP_FIRE_LOOP</c>) never lit, so it is not read.</summary>
    private static readonly L1011Lamp[] FireTestLamps =
    {
        LampOf("ENG_FIRE_1"), LampOf("ENG_FIRE_2"), LampOf("ENG_FIRE_3"), LampOf("ENG_FIRE"), LampOf("FIRE_APU"),
    };

    private const string WheelWellLoopSelector = "ROTARY_WHEEL_WELL_TEST";
    private const string WheelWellTestSwitchA = "SWITCH_WHEEL_WELL_TEST_A";
    private const string WheelWellTestSwitchB = "SWITCH_WHEEL_WELL_TEST_B";

    /// <summary>The wheel well loop selector's Both position.</summary>
    private const int WheelWellLoopsBoth = 1;

    /// <summary>What the wheel well test lit, live, with A and B on together (A alone lit nothing): the engineer
    /// panel's light. The main panel's (<c>LX_MIP_WHEEL_WELL_FIRE</c>) stayed out, so it is not read.</summary>
    private static readonly L1011Lamp[] WheelWellTestLamps = { LampOf("FIRE_WHEEL_WELL") };

    /// <summary>One phase of a lamp test: its writes, in order, then its lights read until they match (none for a
    /// phase that only sets the loops). <paramref name="What"/> names it in the log.</summary>
    private sealed record LampTestPhase(string What, (string Key, int Target)[] Writes, LightCheck[] Lights);

    private static LightCheck[] AllLit(IEnumerable<L1011Lamp> lamps) =>
        lamps.Select(l => new LightCheck(l, Lit: true, DidNotComeOn)).ToArray();

    private static LightCheck[] AllOut(IEnumerable<L1011Lamp> lamps) =>
        lamps.Select(l => new LightCheck(l, Lit: false, DidNotGoOut)).ToArray();

    /// <summary>The loops to Both; test A on, the five lit; A off, the five out; then the same with B.</summary>
    private static readonly LampTestPhase[] FireTestPhases =
    {
        new("the loops on both", FireLoopSelectors.Select(k => (k, FireLoopsBoth)).ToArray(), Array.Empty<LightCheck>()),
        new("test A on", new[] { (FireTestSwitchA, 1) }, AllLit(FireTestLamps)),
        new("test A off", new[] { (FireTestSwitchA, 0) }, AllOut(FireTestLamps)),
        new("test B on", new[] { (FireTestSwitchB, 1) }, AllLit(FireTestLamps)),
        new("test B off", new[] { (FireTestSwitchB, 0) }, AllOut(FireTestLamps)),
    };

    private static readonly (string Key, int Target)[] FireTestPutBack = { (FireTestSwitchA, 0), (FireTestSwitchB, 0) };

    /// <summary>The loops to Both; tests A and B on together, the light lit; both off, the light out.</summary>
    private static readonly LampTestPhase[] WheelWellTestPhases =
    {
        new("the loops on both", new[] { (WheelWellLoopSelector, WheelWellLoopsBoth) }, Array.Empty<LightCheck>()),
        new("tests A and B on", new[] { (WheelWellTestSwitchA, 1), (WheelWellTestSwitchB, 1) }, AllLit(WheelWellTestLamps)),
        new("tests A and B off", new[] { (WheelWellTestSwitchA, 0), (WheelWellTestSwitchB, 0) }, AllOut(WheelWellTestLamps)),
    };

    private static readonly (string Key, int Target)[] WheelWellTestPutBack = { (WheelWellTestSwitchA, 0), (WheelWellTestSwitchB, 0) };

    private Task<bool> FireTestAsync(IL1011FoTransport io) =>
        RunLampTestAsync(io, FireTestName, FireTestMuteMs, FireTestPhases, FireTestPutBack, "both loop tests off");

    private Task<bool> WheelWellFireTestAsync(IL1011FoTransport io) =>
        RunLampTestAsync(io, WheelWellFireTestName, FireTestMuteMs, WheelWellTestPhases, WheelWellTestPutBack, "both wheel well loop tests off");

    /// <summary>
    /// A lamp test, the fire test's and the wheel well test's shared core: a <see cref="Pseudo"/> composite, atomic
    /// under the gate (ruling R24: a hand tick moving a test switch mid-test would corrupt it), every wait bounded
    /// and none pause-aware. The lamp speech muted first (<paramref name="muteMs"/>) and re-armed before every phase
    /// (on the 1 Hz batch a test can outlast one window; the end is left to its time-out, never cut short); each
    /// phase's writes in order, then its lights read (<see cref="WrongLightAsync"/>). A wrong light does not stop
    /// the phases, so the test switches still end off; a write not done (or a throw) ends the test at once with
    /// <paramref name="putBack"/>, each write tried. Done when every write was done and every light matched. One
    /// verdict (<see cref="SpeakVerdict"/>): the first wrong light always, the pass only with progress speech on.
    /// </summary>
    private async Task<bool> RunLampTestAsync(IL1011FoTransport io, string name, int muteMs,
        IReadOnlyList<LampTestPhase> phases, IReadOnlyList<(string Key, int Target)> putBack, string turning)
    {
        string? wrongLight = null;
        bool finished = false;
        try
        {
            foreach (var phase in phases)
            {
                io.MuteLampSpeech(muteMs);
                if (!await WriteEachAsync(io, phase.Writes).ConfigureAwait(false))
                    return false;
                if (phase.Lights.Length == 0)
                    continue;
                // Read even after a wrong light, for the log; only the first is spoken.
                string? wrong = await WrongLightAsync(io, name, phase.Lights, phase.What).ConfigureAwait(false);
                wrongLight ??= wrong;
            }
            finished = true;
            return wrongLight is null;
        }
        finally
        {
            if (!finished)
                await PutBackAsync(io, name, muteMs, putBack, turning).ConfigureAwait(false);
            SpeakVerdict(name, wrongLight, passed: finished && wrongLight is null);
        }
    }

    /// <summary>A test ended early (a write not done, or a throw): logged, the lamp mute re-armed (the put-back
    /// changes the lights too), then every switch back, each tried (<see cref="TryEachAsync"/>). Never spoken.</summary>
    private async Task PutBackAsync(IL1011FoTransport io, string name, int muteMs, IReadOnlyList<(string Key, int Target)> writes, string turning)
    {
        Note($"{name}: not done, turning {turning}");
        io.MuteLampSpeech(muteMs);
        await TryEachAsync(io, name, writes).ConfigureAwait(false);
    }

    // ---- the hydraulic check ----

    private const string HydPumpB = "SWITCH_HYD_AC_PUMP_B3";
    private const string HydPumpC = "SWITCH_HYD_AC_PUMP_C3";
    private const string PtuAb = "SWITCH_PTU_AB";
    private const string PtuCd = "SWITCH_PTU_CD";

    /// <summary>A system's pressure (<c>HYDRAULIC ACTUATOR PRESSURE:7..10</c>, psi), read fresh.</summary>
    private static string HydraulicPressure(char system) => $"L1011_RO_HYD_{system}";

    /// <summary>One system's pressure a phase waits for: at least <paramref name="Psi"/> (rising), or below it
    /// (bleeding down).</summary>
    private sealed record PressureCheck(char System, double Psi, bool Rising)
    {
        public string Readout => HydraulicPressure(System);

        public bool Meets(double psi) => Rising ? psi >= Psi : psi < Psi;

        /// <summary>"did not reach 2,000 psi": spoken, so the number is invariant (never "2.000" under de-DE).</summary>
        public string WhenWrong =>
            $"{(Rising ? "did not reach" : "did not fall below")} {Psi.ToString("#,##0", CultureInfo.InvariantCulture)} psi";
    }

    /// <summary>One phase of the hydraulic check: its writes, in order, then its pressures, A to D, waited for
    /// up to <paramref name="WaitMs"/>.</summary>
    private sealed record HydraulicPhase(string What, (string Key, int Target)[] Writes, PressureCheck[] Pressures, int WaitMs);

    /// <summary>Everything off, the power transfer units before the pumps that drive them; also the put-back.</summary>
    private static readonly (string Key, int Target)[] HydraulicOffWrites =
    {
        (PtuAb, 0), (PtuCd, 0), (HydPumpB, 0), (HydPumpC, 0),
    };

    /// <summary>The manual's order: B's AC pump, C's, both power transfer units, then everything off. Live, on
    /// external power: B and C past 1,500 psi in 2 s and about 2,650 shortly after; A and D past 2,600 within
    /// 10 s; every system back to 14.5 psi within 8 s of everything off.</summary>
    private static readonly HydraulicPhase[] HydraulicPhases =
    {
        new("system B AC pump on", new[] { (HydPumpB, 1) },
            new[] { new PressureCheck('B', HydraulicNormalPsi, Rising: true) }, HydraulicRiseWaitMs),
        new("system C AC pump on", new[] { (HydPumpC, 1) },
            new[] { new PressureCheck('C', HydraulicNormalPsi, Rising: true) }, HydraulicRiseWaitMs),
        new("power transfer units on", new[] { (PtuAb, 1), (PtuCd, 1) },
            new[] { new PressureCheck('A', HydraulicAMinPsi, Rising: true), new PressureCheck('D', HydraulicDMinPsi, Rising: true) },
            HydraulicRiseWaitMs),
        new("everything off", HydraulicOffWrites,
            "ABCD".Select(s => new PressureCheck(s, HydraulicZeroPsi, Rising: false)).ToArray(), HydraulicBleedWaitMs),
    };

    /// <summary>
    /// The hydraulic check, a <see cref="Pseudo"/> composite, atomic under the gate (ruling R24: a hand tick moving
    /// a pump mid-check would corrupt it), every wait bounded and none pause-aware. The lamp speech muted first
    /// (<see cref="HydraulicCheckMuteMs"/>: the brake accumulator lights change) and re-armed before every phase;
    /// each phase's writes in order (<see cref="HydraulicPhases"/>), then its pressures read fresh until they meet
    /// their thresholds (<see cref="WrongPressureAsync"/>). A wait that times out is recorded and the check carries
    /// on, since the pumps must still come off; a write not done (or a throw) ends it at once with everything off
    /// (<see cref="HydraulicOffWrites"/>, each tried). Done when every write was done and every pressure met. One
    /// verdict (<see cref="SpeakVerdict"/>): the first failing pressure always, the pass only with progress speech on.
    /// </summary>
    private async Task<bool> HydraulicCheckAsync(IL1011FoTransport io)
    {
        string? firstFailure = null;
        bool finished = false;
        try
        {
            foreach (var phase in HydraulicPhases)
            {
                io.MuteLampSpeech(HydraulicCheckMuteMs);
                if (!await WriteEachAsync(io, phase.Writes).ConfigureAwait(false))
                    return false;
                string? failure = await WrongPressureAsync(io, phase.Pressures, phase.WaitMs, phase.What).ConfigureAwait(false);
                firstFailure ??= failure;
            }
            finished = true;
            return firstFailure is null;
        }
        finally
        {
            if (!finished)
                await PutBackAsync(io, HydraulicCheckName, HydraulicCheckMuteMs, HydraulicOffWrites,
                    "the AC pumps and power transfer units off").ConfigureAwait(false);
            SpeakVerdict(HydraulicCheckName, firstFailure, passed: finished && firstFailure is null);
        }
    }

    /// <summary>
    /// Reads the pressures fresh, in order (A to D), until each meets its threshold, or, once
    /// <paramref name="limitMs"/> has passed on the transport's clock, the sentence naming the first that did not
    /// ("Hydraulic check: system B pressure did not reach 2,000 psi", "… could not be read"; logged). Null when all
    /// met. Read-only; not pause-aware.
    /// </summary>
    private async Task<string?> WrongPressureAsync(IL1011FoTransport io, IReadOnlyList<PressureCheck> pressures, int limitMs, string what)
    {
        long deadline = io.NowMs + limitMs;
        while (true)
        {
            PressureCheck? wrong = null;
            double? reading = null;
            foreach (var check in pressures)
            {
                reading = await ReadAsync(io, check.Readout).ConfigureAwait(false);
                if (reading is not double psi || !check.Meets(psi))
                {
                    wrong = check;
                    break;
                }
            }
            if (wrong is null) return null;
            if (io.NowMs >= deadline)
            {
                string read = reading is double r ? r.ToString(CultureInfo.InvariantCulture) : "unread";
                Note($"{HydraulicCheckName}, {what}: {wrong.Readout} reads {read} after {limitMs.ToString(CultureInfo.InvariantCulture)} ms, expected {(wrong.Rising ? "at least" : "below")} {wrong.Psi.ToString(CultureInfo.InvariantCulture)} psi");
                string did = reading is null ? "could not be read" : wrong.WhenWrong;
                return $"{HydraulicCheckName}: system {wrong.System} pressure {did}";
            }
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
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
        string? firstTieFailure = await CloseTiesAsync(io, what).ConfigureAwait(false);
        if (firstTieFailure is not null)
            _say?.Invoke($"External power on. {firstTieFailure}.");
        return true;
    }

    // ================= the AC ties =================

    /// <summary>
    /// Target 1: <see cref="CloseTiesAsync"/>, done only when all three ties read closed. Unlike external
    /// power it speaks nothing: a tie left open is this step's own failure, the flow's to say. Any other
    /// target reads and writes nothing.
    /// </summary>
    private async Task<bool> AcTiesAsync(IL1011FoTransport io, int target)
    {
        string what = $"{AcTies} -> {target.ToString(CultureInfo.InvariantCulture)}";
        if (target != 1)
        {
            Note($"{what}: not done, the AC ties are 1 (closed)");
            return false;
        }
        return await CloseTiesAsync(io, what).ConfigureAwait(false) is null;
    }

    /// <summary>
    /// Each AC tie read fresh, in order: closed, left alone; open, its close button pressed and the tie read
    /// back closed within <see cref="RelayVerifyTimeoutMs"/>, a relay's 8 s (ruling R22), never the press
    /// button's own state (one not closing does not stop the next); unread, never pressed. Null when all
    /// three read closed, else the first failure in words for a sentence ("AC tie 2 did not close", "AC tie
    /// 2 could not be read"); each failure is logged where it happened.
    /// </summary>
    private async Task<string?> CloseTiesAsync(IL1011FoTransport io, string what)
    {
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
                || !await ReadsAsync(io, new[] { tie }, v => v >= RelayClosedAtOrAbove, $"{what}, {tieName}",
                    RelayVerifyTimeoutMs).ConfigureAwait(false))
                firstTieFailure ??= $"{tieName} did not close";
        }
        return firstTieFailure;
    }

    // ================= the APU =================

    /// <summary>
    /// The APU started, target ignored; a <see cref="StopAware"/> composite, outside the gate (ruling R23).
    /// One write phase under the gate (<see cref="UnderGateAsync"/>, never begun after the flow's Stop): the
    /// RPM read fresh, unread, not done and nothing pressed; at or above
    /// <see cref="L1011FoStateEvaluator.ApuStartingRpm"/>, done with no press (starting or running: never a
    /// second press). Then the master read fresh: not reading on, not done and logged (the flow sets the
    /// master in its own step first). The Stop checked once more, then one press of the start button, the
    /// panel row's press (<c>L:SWITCH_APU_START</c> 1 with <c>H:APU</c>, held 2 s, [L1011-22]). Then, the
    /// gate released, done once the RPM reads at or above <see cref="L1011FoStateEvaluator.ApuStartingRpm"/>
    /// within <see cref="ApuSpinUpWaitMs"/> (<see cref="WaitUntilAsync"/>, read-only: a flow's Pause holds it,
    /// its Stop ends it). The wait to running is the flow's own step, as the MD-11's <c>BS_APU_RUNNING</c>.
    /// Never presses twice; speaks nothing.
    /// </summary>
    private async Task<bool> ApuStartAsync(CancellationToken stop)
    {
        const string what = ApuStart;
        IL1011FoTransport? pressedOn = null;
        bool alreadyTurning = false;
        bool pressed = await UnderGateAsync(what, stop, async io =>
        {
            var rpm = await ReadAsync(io, ApuRpm).ConfigureAwait(false);
            if (rpm is not double r)
            {
                Note($"{what}: not done, {ApuRpm} unread, {ApuStartPress} not pressed");
                return false;
            }
            if (r >= L1011FoStateEvaluator.ApuStartingRpm)
            {
                alreadyTurning = true;
                return true;
            }
            var master = await ReadAsync(io, ApuMaster).ConfigureAwait(false);
            if (master is not double m || Math.Abs(m - 1) >= PositionTolerance)
            {
                Note("APU not started: the master switch does not read on");
                return false;
            }
            if (stop.IsCancellationRequested)   // a Stop while the reads above decided: no press
                return Stopped(what);
            if (!await WriteAsync(io, ApuStartPress, 1).ConfigureAwait(false))
                return false;
            pressedOn = io;
            return true;
        }).ConfigureAwait(false);
        if (!pressed) return false;
        if (alreadyTurning) return true;

        // The gate is released: the spin-up wait is read-only and holds nothing (R23).
        var waitOn = pressedOn!;
        return await WaitUntilAsync(waitOn,
            async () => await ReadAsync(waitOn, ApuRpm).ConfigureAwait(false) is double v && v >= L1011FoStateEvaluator.ApuStartingRpm,
            ApuSpinUpWaitMs, stop, $"{what}, {ApuRpm} rising").ConfigureAwait(false);
    }

    /// <summary>
    /// Target 1, on line: the field relay closed, then the breaker relay, each through
    /// <see cref="PressUntilRelayAsync"/> (a relay already closed is not pressed; an unread relay is never
    /// pressed; the breaker is not tried when the field did not close). Live, the field relay closed about a
    /// second after its press and the button reset itself; the breaker may close with external power still
    /// on (no interlock). Target 0, off line: the breaker tripped and read open; the field is left alone. Any
    /// other target reads and writes nothing. Speaks nothing.
    /// </summary>
    private async Task<bool> ApuGeneratorAsync(IL1011FoTransport io, int target)
    {
        string what = $"{ApuGenerator} -> {target.ToString(CultureInfo.InvariantCulture)}";
        switch (target)
        {
            case 1:
                return await PressUntilRelayAsync(io, ApuGenFieldPress, ApuGenFieldRelay, closed: true, $"{what}, the field").ConfigureAwait(false)
                    && await PressUntilRelayAsync(io, ApuGenBreakerPress, ApuGenBreakerRelay, closed: true, $"{what}, the breaker").ConfigureAwait(false);
            case 0:
                return await PressUntilRelayAsync(io, ApuGenBreakerTrip, ApuGenBreakerRelay, closed: false, $"{what}, the breaker trip").ConfigureAwait(false);
            default:
                Note($"{what}: not done, the APU generator is 0 (off line) or 1 (on line)");
                return false;
        }
    }

    // ================= the INS align =================

    /// <summary>The INS mode selector's positions (<c>ROTARY_INS_MODE_n</c>: 0 Off, 1 Standby, 2 Align,
    /// 3 Nav, 4 Attitude).</summary>
    private const int InsModeAlign = 2;
    private const int InsModeNav = 3;

    /// <summary>
    /// Each unit's mode selector read fresh, in order: at ALIGN or NAV, left alone (never rewritten: a
    /// unit navigating stays navigating); at OFF, STANDBY or ATTITUDE, turned to ALIGN and read back;
    /// unread, never written, and the step not done. Every unit is tried whatever the others do. Done
    /// when every unit ends at ALIGN or NAV. Speaks nothing.
    /// </summary>
    private async Task<bool> InsAlignAsync(IL1011FoTransport io)
    {
        bool done = true;
        for (int n = 1; n <= 3; n++)
        {
            string key = L1011InsAlignmentAnnouncer.MsuKey(n);
            var mode = await ReadAsync(io, key).ConfigureAwait(false);
            if (mode is not double m)
            {
                Note($"{InsAlign}: {key} unread, INS {n.ToString(CultureInfo.InvariantCulture)} not turned");
                done = false;
                continue;
            }
            if (Math.Abs(m - InsModeAlign) < PositionTolerance || Math.Abs(m - InsModeNav) < PositionTolerance)
                continue;
            done &= await WriteAsync(io, key, InsModeAlign).ConfigureAwait(false);
        }
        return done;
    }

    // ================= relay presses =================

    /// <summary>
    /// How long a relay a press drives may take to read as wanted (ruling R21): live, a generator breaker
    /// was still open on an immediate read after its press and closed within about 4 s.
    /// </summary>
    internal const int RelayVerifyTimeoutMs = 8_000;

    /// <summary>
    /// A press verified on the relay it drives, never on the button, which resets itself: the relay read
    /// fresh first; already as wanted (an engine generator's field relay closes by itself once its engine
    /// runs), done with no press; unread, not done and never pressed blind; else
    /// <paramref name="pressKey"/> pressed once and the relay read back until it reads closed (or open,
    /// <paramref name="closed"/> false) or <see cref="RelayVerifyTimeoutMs"/> passes. Under the caller's
    /// gate; speaks nothing.
    /// </summary>
    internal async Task<bool> PressUntilRelayAsync(IL1011FoTransport io, string pressKey, string relayReadout, bool closed, string what)
    {
        var reading = await ReadAsync(io, relayReadout).ConfigureAwait(false);
        if (reading is not double r)
        {
            Note($"{what}: not done, {relayReadout} unread, {pressKey} not pressed");
            return false;
        }
        if (RelayReads(r, closed)) return true;
        return await WriteAsync(io, pressKey, 1).ConfigureAwait(false)
            && await ReadsAsync(io, new[] { relayReadout }, v => RelayReads(v, closed), what, RelayVerifyTimeoutMs).ConfigureAwait(false);
    }

    private static bool RelayReads(double reading, bool closed) =>
        closed ? reading >= RelayClosedAtOrAbove : reading < RelayClosedAtOrAbove;

    // ================= waits =================

    /// <summary>
    /// Read-only, and never under the gate (ruling R23): polls <paramref name="satisfied"/> every
    /// <see cref="PollMs"/> until it is true (true), or until <paramref name="limitMs"/> has passed on the
    /// transport's clock (false, logged with the limit). For a flow step (a <paramref name="stop"/> that can
    /// be cancelled) the flow's Pause holds it, and the time paused is not counted (the MD-11's
    /// <c>ApuStartToFinishAsync</c> shape); a hand tick (<see cref="CancellationToken.None"/>) never waits on
    /// a flow's Pause. The flow's Stop ends it with false (logged "<paramref name="what"/>: stopped") before
    /// the next ask: within a poll plus the time of one <paramref name="satisfied"/> call, which may be a
    /// fresh read of up to <see cref="ReadTimeoutMs"/>; at once while paused or already stopped. Writes
    /// nothing and speaks nothing.
    /// </summary>
    internal async Task<bool> WaitUntilAsync(IL1011FoTransport io, Func<Task<bool>> satisfied, int limitMs, CancellationToken stop, string what)
    {
        long deadline = io.NowMs + limitMs;
        while (true)
        {
            if (stop.CanBeCanceled)   // a flow step: its Pause holds the wait; a hand tick's never does
            {
                long pausedAt = io.NowMs;
                try
                {
                    await _waitWhilePaused(stop).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Stopped(what);
                }
                deadline += io.NowMs - pausedAt;   // a Pause is not counted
            }
            if (stop.IsCancellationRequested)
                return Stopped(what);
            if (await satisfied().ConfigureAwait(false))
                return true;
            if (io.NowMs >= deadline)
            {
                Note($"{what}: not done within {limitMs.ToString(CultureInfo.InvariantCulture)} ms");
                return false;
            }
            await io.DelayAsync(PollMs).ConfigureAwait(false);
        }
    }

    private bool Stopped(string what)
    {
        Note($"{what}: stopped");
        return false;
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
    /// naming the first key that did not) once <paramref name="timeoutMs"/> (by default
    /// <see cref="VerifyTimeoutMs"/>) has passed on the transport's clock.
    /// </summary>
    private async Task<bool> ReadsAsync(IL1011FoTransport io, IReadOnlyList<string> keys, Func<double, bool> ok, string what,
        int timeoutMs = VerifyTimeoutMs)
    {
        long deadline = io.NowMs + timeoutMs;
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
                Note($"{what}: not done, {notThere} reads {read} after {timeoutMs.ToString(CultureInfo.InvariantCulture)} ms");
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
