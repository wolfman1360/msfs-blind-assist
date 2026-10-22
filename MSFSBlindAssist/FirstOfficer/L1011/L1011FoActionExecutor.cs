using System.Collections.Concurrent;
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
/// and a Stop-aware composite never holds the gate through a wait: it runs outside the gate, takes it for
/// each write phase (<see cref="UnderGateAsync"/>: the reads that decide a write and the write, never after
/// the Stop) and releases it before every wait, which is read-only, bounded on the transport's clock and,
/// for a flow step only, held by the flow's Pause without counting it (<see cref="WaitUntilAsync"/>). So a
/// paused flow never queues a hand tick, the automation or the drain behind its wait.
///
/// The exception is the <see cref="Pseudo"/> composites, which keep the gate for their whole run: every wait inside
/// them is bounded on the transport's clock, and none waits on the flow's Pause. The long ones include the audible
/// tests (the fire, wheel well and hydraulic tests) and the standby power check, which must keep it, because a hand
/// tick moving a test switch or a pump in the middle of a test would corrupt it.
///
/// The engine start is the one step a stopped flow must not leave half done: a ground start a flow turned on is
/// recorded with the run's Stop (<see cref="_flowGroundStarts"/>), and a run stopped before that engine's fuel and
/// ignition are on has it released, only while they read off and never by a fuel write, once: by
/// <see cref="OnFlowStoppedAsync"/> or the window's close (<see cref="ReleaseHeldPresses"/>), whichever takes it out
/// first (the MD-11's shape); a watch that timed out, or a click not done, releases it itself. A press still held
/// when the definition goes away is released by the definition's own dispose, once.
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

    /// <summary>The APU stopped (target 1): one press of the stop button, then done once the APU reads stopped; reads
    /// the flow's Stop. The After Start and Shutdown flows' stop, and their ticks.</summary>
    public const string ApuStop = "FO_APU_STOP";

    /// <summary>The engine and APU fire test (target ignored): one spoken verdict.</summary>
    public const string FireTest = "FO_FIRE_TEST";

    /// <summary>The wheel well fire test (target ignored): one spoken verdict.</summary>
    public const string WheelWellFireTest = "FO_WHEEL_WELL_FIRE_TEST";

    /// <summary>The hydraulic check (target ignored): one spoken verdict.</summary>
    public const string HydraulicCheck = "FO_HYDRAULIC_CHECK";

    /// <summary>The cabin barometric setting; the target is hectopascals × 100 (1002.37 is 100237), an integer
    /// as every flow target is.</summary>
    public const string CabinBaro = "FO_CABIN_BARO";

    /// <summary>The cabin altitude selector turned to the knob step that commands a cabin altitude; the
    /// target is that altitude in feet.</summary>
    public const string CabinAltitudeSelect = "FO_CABIN_ALT_SELECT";

    /// <summary>Engine <paramref name="n"/>'s ground start (target 1): the switch on, done once N3 rises; reads the
    /// flow's Stop.</summary>
    public static string GroundStart(int n) => $"FO_GROUND_START_{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Engine <paramref name="n"/>'s light-off watch (target ignored): N3 up to the fuel-on point; reads the
    /// flow's Stop.</summary>
    public static string EngineLightOff(int n) => $"FO_ENGINE_LIGHT_OFF_{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Engine <paramref name="n"/>'s idle wait (target ignored, read-only); reads the flow's Stop.</summary>
    public static string EngineIdle(int n) => $"FO_ENGINE_IDLE_{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The three engine generators on line (target 1): every field closed, then every breaker.</summary>
    public const string EngineGenerators = "FO_ENGINE_GENERATORS";

    /// <summary>
    /// The composites, by pseudo-key: each runs under the gate, writes through <see cref="WriteAsync"/>
    /// (never <see cref="Set"/>, which would wait on the gate it holds), or for a typed or knob entry through
    /// the transport's own (<see cref="SendAndReadBackAsync"/>), and reads back what it changed.
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
            // The audible tests: atomic, so no hand tick moves a test switch or pump mid-test.
            [FireTest] = (exec, io, _) => exec.FireTestAsync(io),
            [WheelWellFireTest] = (exec, io, _) => exec.WheelWellFireTestAsync(io),
            [HydraulicCheck] = (exec, io, _) => exec.HydraulicCheckAsync(io),
            [CabinBaro] = (exec, io, target) => exec.CabinBaroAsync(io, target),
            [CabinAltitudeSelect] = (exec, io, target) => exec.CabinAltitudeSelectAsync(io, target),
            [EngineGenerators] = (exec, io, target) => exec.EngineGeneratorsAsync(io, target),
        };

    /// <summary>
    /// The composites that read the flow's Stop (a wait that may end early in a safe state); looked up first,
    /// before the gate (<see cref="SetCoreAsync"/>). Each runs OUTSIDE the gate and takes it for each write
    /// phase through <see cref="UnderGateAsync"/>, so it is handed no transport: a phase gets the one read
    /// under the gate. A checklist tick passes <see cref="CancellationToken.None"/>.
    /// </summary>
    private static readonly Dictionary<string, Func<L1011FoActionExecutor, int, CancellationToken, Task<bool>>> StopAware =
        new(StringComparer.Ordinal)
        {
            [ApuStart] = (exec, _, stop) => exec.ApuStartAsync(stop),
            [ApuStop] = (exec, target, stop) => exec.ApuStopAsync(target, stop),
            [GroundStart(1)] = (exec, target, stop) => exec.GroundStartAsync(1, target, stop),
            [GroundStart(2)] = (exec, target, stop) => exec.GroundStartAsync(2, target, stop),
            [GroundStart(3)] = (exec, target, stop) => exec.GroundStartAsync(3, target, stop),
            [EngineLightOff(1)] = (exec, _, stop) => exec.EngineLightOffAsync(1, stop),
            [EngineLightOff(2)] = (exec, _, stop) => exec.EngineLightOffAsync(2, stop),
            [EngineLightOff(3)] = (exec, _, stop) => exec.EngineLightOffAsync(3, stop),
            [EngineIdle(1)] = (exec, _, stop) => exec.EngineIdleAsync(1, stop),
            [EngineIdle(2)] = (exec, _, stop) => exec.EngineIdleAsync(2, stop),
            [EngineIdle(3)] = (exec, _, stop) => exec.EngineIdleAsync(3, stop),
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

    /// <summary>Step 2, with standby power On, as this aircraft shows it (observed live): the flowbar lit,
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

    /// <summary>The APU stop button's row: a press, holding no position (<c>L:APU_STOP</c> with <c>K:APU_OFF_SWITCH</c>).</summary>
    internal const string ApuStopPress = "APU_STOP";

    /// <summary>How long the APU may take to read stopped after its stop press: live, it ran down from 100 to 0 in
    /// about 60 s.</summary>
    internal const int ApuSpinDownWaitMs = 90_000;

    // ---- the audible tests ----

    internal const int FireTestMuteMs = 45_000;
    internal const int HydraulicCheckMuteMs = 60_000;
    internal const double HydraulicNormalPsi = 2000;
    internal const double HydraulicAMinPsi = 900;
    internal const double HydraulicDMinPsi = 1400;
    internal const double HydraulicZeroPsi = 100;
    internal const int HydraulicRiseWaitMs = 20_000;
    internal const int HydraulicBleedWaitMs = 30_000;

    // ---- the engine start ----

    /// <summary>An engine's ground start switch row (<c>L:SWITCH_GND_START_ENG_n</c>, 0 off, 1 on). Its own click to
    /// 0 releases the starter: seen live, with an engine turning on APU air and its fuel off, the starter and the
    /// switch read 0 at once and N3 fell away.</summary>
    internal static string GroundStartKey(int n) => $"SWITCH_GND_START_ENG_{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>An engine's fuel and ignition switch row: it reads the stock fuel valve, and its write is a toggle,
    /// so it is read before it is written ([L1011-4]).</summary>
    internal static string FuelIgnitionKey(int n) => $"TOGGLE_CUTOFF_ENG_{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>An engine's N3 gauge, in percent: a cold gauge reads about 1.4 to 2.6, never 0.</summary>
    internal static string N3Key(int n) => $"L1011_RO_N3_{n.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>By engine (index 0 unused), for <see cref="SetCoreAsync"/>'s look-up.</summary>
    private static readonly string[] FuelIgnitionKeys = { "", FuelIgnitionKey(1), FuelIgnitionKey(2), FuelIgnitionKey(3) };

    /// <summary>The rise in N3 that shows a ground start turning its engine, and how long it may take. Live, on APU
    /// air, N3 rose at once, about 1 %/s.</summary>
    internal const double RotationRiseN3 = 2;
    internal const int RotationWaitMs = 30_000;

    /// <summary>The N3 at which the fuel and ignition go on, and how long the light-off watch waits for it. Live, N3
    /// reached 20 % about 20 s after the ground start.</summary>
    internal const double FuelOnN3 = 20;
    internal const int LightOffWaitMs = 60_000;

    /// <summary>Idle: N3 at or above <see cref="IdleN3"/> for <see cref="IdleHoldMs"/> without a break, within
    /// <see cref="IdleWaitMs"/>. Live, a stable idle read 59 to 61 on the gauge.</summary>
    internal const double IdleN3 = 57;
    internal const int IdleHoldMs = 3_000;
    internal const int IdleWaitMs = 90_000;

    private static string GenFieldPress(int n) => $"SWITCH_BRG_GEN_{n.ToString(CultureInfo.InvariantCulture)}";
    private static string GenBreakerPress(int n) => $"SWITCH_GEN_FLOW_{n.ToString(CultureInfo.InvariantCulture)}";
    private static string GenFieldRelay(int n) => $"L1011_RO_GEN_{n.ToString(CultureInfo.InvariantCulture)}_FIELD";
    private static string GenBreakerRelay(int n) => $"L1011_RO_GEN_{n.ToString(CultureInfo.InvariantCulture)}_BREAKER";

    /// <summary>True for a composite's key, Stop-aware or not. A flow key is known when this or the
    /// definition's <c>FoAcceptsKey</c> is true.</summary>
    public static bool IsPseudoKey(string key) => Pseudo.ContainsKey(key) || StopAware.ContainsKey(key);

    /// <summary>Tests only: the two tables' keys, which never overlap (<see cref="StopAware"/> is looked up first).</summary>
    internal static IEnumerable<string> PseudoKeys => Pseudo.Keys;

    internal static IEnumerable<string> StopAwareKeys => StopAware.Keys;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private IL1011FoTransport? _io;
    private Action<string>? _say;
    private Func<bool> _speaksFlowProgress = () => true;
    private Func<CancellationToken, Task> _waitWhilePaused = _ => Task.CompletedTask;

    /// <summary>The last APU start press still spinning up: the transport it went out on and that transport's
    /// clock when it was sent. Read and written only under the gate (<see cref="ApuStartAsync"/>'s phase).</summary>
    private (IL1011FoTransport Io, long AtMs)? _apuStartPressed;

    /// <summary>The last APU stop press still spinning down: the transport it went out on and that transport's clock
    /// when it was sent. Read and written only under the gate (<see cref="ApuStopAsync"/>'s phase, and a start press,
    /// which ends it).</summary>
    private (IL1011FoTransport Io, long AtMs)? _apuStopPressed;

    /// <summary>
    /// The ground starts a flow had this executor turn on, by engine, with that run's Stop (the MD-11's
    /// <c>_flowStarterPulls</c>): the starts a stopped run could leave turning a dry engine. Recorded before the
    /// switch's click goes out (a click that lands late is still one to release), and only for a flow step: a
    /// checklist tick's start, the pilot's own and a switch found already on are never here. Forgotten once that
    /// engine's fuel and ignition write is done (<see cref="SetCoreAsync"/>), and taken out by whoever releases it,
    /// so its release goes out once: after a Stop, <see cref="OnFlowStoppedAsync"/> or the window's close
    /// (<see cref="ReleaseHeldPresses"/>); after a time-out or a click not done, the give-up under the gate
    /// (<see cref="GiveUpGroundStartAsync"/>).
    /// </summary>
    private readonly ConcurrentDictionary<int, CancellationToken> _flowGroundStarts = new();

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
    /// the gate): a plain switch, a press and every other composite are atomic and ignore it.
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
    /// looked up first and run outside the gate (<see cref="StopAwareAsync"/>); anything else
    /// under the gate. An engine's fuel and ignition written on forgets its recorded ground start
    /// (<see cref="_flowGroundStarts"/>): the start runs on, and the aircraft releases the switch itself.
    /// </summary>
    private Task<bool> SetCoreAsync(string key, int target, CancellationToken stop)
    {
        string what = $"{key} -> {target.ToString(CultureInfo.InvariantCulture)}";
        if (StopAware.TryGetValue(key, out var stopAware))
            return StopAwareAsync(what, stopAware, target, stop);
        return GatedAsync(what, async io =>
        {
            bool done = await DispatchAsync(io, key, target).ConfigureAwait(false);
            if (done && target == 1 && Array.IndexOf(FuelIgnitionKeys, key) is int engine and > 0)
                _flowGroundStarts.TryRemove(engine, out _);
            return done;
        });
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

    // ================= Stop-aware composites, outside the gate =================

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
    /// composite's own writes too. <paramref name="readJustNow"/>: the caller read the key fresh under this
    /// same gate a moment ago, so it is not read again.
    /// </summary>
    private async Task<bool> WriteAsync(IL1011FoTransport io, string key, int target, bool readJustNow = false)
    {
        string what = $"{key} -> {target.ToString(CultureInfo.InvariantCulture)}";
        bool readable = io.CanReadBack(key);
        // The definition plans from what it last read: a fresh delivery first, so a toggle is decided
        // on a delivered value, and refused by the definition when there is none.
        if (readable && !readJustNow)
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

    // ================= the audible tests =================

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

    /// <summary>What the loop tests lit on Both, live: the three engine fire lights, the engine fire warning and
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

    /// <summary>Both loop test switches off: written before the test's first phase, and again as its put-back. A
    /// switch left on (by an aborted run, or a saved state) would keep the lights lit after the other one went
    /// off, and the test would name an engine fire light that did not go out. A switch already off is written
    /// as no change.</summary>
    private static readonly (string Key, int Target)[] FireTestSwitchesOff = { (FireTestSwitchA, 0), (FireTestSwitchB, 0) };

    /// <summary>Both switches off, then the loops to Both; tests A and B on together, the five lit; both off, the
    /// five out. The two switches go on together because, live on APU and external power with the engines off, test
    /// A alone lit nothing (held 8 s), while B alone, and A with B, lit all five: a test of A on its own would name
    /// five lights that cannot come on.</summary>
    private static readonly LampTestPhase[] FireTestPhases =
    {
        new("the test switches off, the loops on both",
            FireTestSwitchesOff.Concat(FireLoopSelectors.Select(k => (k, FireLoopsBoth))).ToArray(), Array.Empty<LightCheck>()),
        new("tests A and B on", new[] { (FireTestSwitchA, 1), (FireTestSwitchB, 1) }, AllLit(FireTestLamps)),
        new("tests A and B off", new[] { (FireTestSwitchA, 0), (FireTestSwitchB, 0) }, AllOut(FireTestLamps)),
    };

    private static readonly (string Key, int Target)[] FireTestPutBack = FireTestSwitchesOff;

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
    /// under the gate (a hand tick moving a test switch mid-test would corrupt it), every wait bounded
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
    /// The hydraulic check, a <see cref="Pseudo"/> composite, atomic under the gate (a hand tick moving
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
    /// back closed within <see cref="RelayVerifyTimeoutMs"/>, a relay's 8 s, never the press
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
    /// The APU started, target ignored; a <see cref="StopAware"/> composite, outside the gate.
    /// One write phase under the gate (<see cref="UnderGateAsync"/>, never begun after the flow's Stop): the
    /// RPM read fresh, unread, not done and nothing pressed; at or above
    /// <see cref="L1011FoStateEvaluator.ApuStartingRpm"/>, done with no press (starting or running: never a
    /// second press). Below it within <see cref="ApuSpinUpWaitMs"/> of a press this executor sent on the same
    /// transport (<see cref="_apuStartPressed"/>), not pressed again (logged): the RPM reads 0 for about 7 s
    /// while the inlet doors open, and a call overlapping that window (a hand tick while the flow's step waits,
    /// a tick, untick and re-tick) waits for that press instead, ending when its 20 s end. Past them, or on
    /// another transport (the window reopened), a new press is a retry. Then the master read fresh: not
    /// reading on, not done and logged (the flow sets the master in its own step first). The Stop checked once
    /// more, then one press of the start button, the panel row's press (<c>L:SWITCH_APU_START</c> 1 with
    /// <c>H:APU</c>, held 2 s, [L1011-22]), recorded on the transport's clock. Then, the gate released, done
    /// once the RPM reads at or above <see cref="L1011FoStateEvaluator.ApuStartingRpm"/> within
    /// <see cref="ApuSpinUpWaitMs"/> of the press (<see cref="WaitUntilAsync"/>, read-only: a flow's Pause
    /// holds it, its Stop ends it). The wait to running is the flow's own step, as the MD-11's
    /// <c>BS_APU_RUNNING</c>. Never presses twice within a spin-up; speaks nothing.
    /// </summary>
    private async Task<bool> ApuStartAsync(CancellationToken stop)
    {
        const string what = ApuStart;
        IL1011FoTransport? pressedOn = null;
        int waitMs = ApuSpinUpWaitMs;
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
                _apuStartPressed = null;   // turning: that press did its work
                alreadyTurning = true;
                return true;
            }
            if (_apuStartPressed is { } earlier && ReferenceEquals(earlier.Io, io)
                && io.NowMs - earlier.AtMs < ApuSpinUpWaitMs)
            {
                long agoMs = io.NowMs - earlier.AtMs;
                Note($"{what}: {ApuStartPress} pressed {agoMs.ToString(CultureInfo.InvariantCulture)} ms ago and the APU is still spinning up: not pressed again");
                pressedOn = io;
                waitMs = (int)(ApuSpinUpWaitMs - agoMs);   // ends with that press's wait
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
            _apuStartPressed = (io, io.NowMs);
            _apuStopPressed = null;   // started again: an earlier stop press is over
            pressedOn = io;
            return true;
        }).ConfigureAwait(false);
        if (!pressed) return false;
        if (alreadyTurning) return true;

        // The gate is released: the spin-up wait is read-only and holds nothing.
        var waitOn = pressedOn!;
        return await WaitUntilAsync(waitOn,
            async () => await ReadAsync(waitOn, ApuRpm).ConfigureAwait(false) is double v && v >= L1011FoStateEvaluator.ApuStartingRpm,
            waitMs, stop, $"{what}, {ApuRpm} rising").ConfigureAwait(false);
    }

    /// <summary>
    /// The APU stopped, target 1 (any other reads and writes nothing, logged); a <see cref="StopAware"/> composite,
    /// outside the gate, the APU start's shape. One write phase under the gate (<see cref="UnderGateAsync"/>, never
    /// begun after the flow's Stop): the RPM read fresh, unread, not done and nothing pressed; below
    /// <see cref="L1011FoStateEvaluator.ApuStoppedRpm"/>, done with no press. At or above it within
    /// <see cref="ApuSpinDownWaitMs"/> of a stop press this executor sent on the same transport
    /// (<see cref="_apuStopPressed"/>), not pressed again (logged): a call overlapping that spin-down (a hand tick
    /// while the flow's step waits) waits for that press instead, ending when its 90 s end. Past them, on another
    /// transport, or after a start press since, a new press is a retry. The Stop checked once more, then one press of
    /// the stop button, the panel row's press (<see cref="ApuStopPress"/>), recorded on the transport's clock. Then,
    /// the gate released, done once the RPM reads below <see cref="L1011FoStateEvaluator.ApuStoppedRpm"/> within
    /// <see cref="ApuSpinDownWaitMs"/> of the press (<see cref="WaitUntilAsync"/>, read-only: a flow's Pause holds it,
    /// its Stop ends it). Holding until the APU reads stopped is what keeps a hand tick's line, judged on the APU
    /// stopped, from reverting in the minute it takes ([FO-11]). Never presses twice within a spin-down; speaks
    /// nothing.
    /// </summary>
    private async Task<bool> ApuStopAsync(int target, CancellationToken stop)
    {
        const string what = ApuStop;
        if (target != 1)
        {
            Note($"{what}: not done, the APU stop is 1, not {target.ToString(CultureInfo.InvariantCulture)}");
            return false;
        }
        IL1011FoTransport? pressedOn = null;
        int waitMs = ApuSpinDownWaitMs;
        bool alreadyStopped = false;
        bool pressed = await UnderGateAsync(what, stop, async io =>
        {
            var rpm = await ReadAsync(io, ApuRpm).ConfigureAwait(false);
            if (rpm is not double r)
            {
                Note($"{what}: not done, {ApuRpm} unread, {ApuStopPress} not pressed");
                return false;
            }
            if (r < L1011FoStateEvaluator.ApuStoppedRpm)
            {
                _apuStopPressed = null;   // stopped: that press did its work
                alreadyStopped = true;
                return true;
            }
            if (_apuStopPressed is { } earlier && ReferenceEquals(earlier.Io, io)
                && io.NowMs - earlier.AtMs < ApuSpinDownWaitMs)
            {
                long agoMs = io.NowMs - earlier.AtMs;
                Note($"{what}: {ApuStopPress} pressed {agoMs.ToString(CultureInfo.InvariantCulture)} ms ago and the APU is still spinning down: not pressed again");
                pressedOn = io;
                waitMs = (int)(ApuSpinDownWaitMs - agoMs);   // ends with that press's wait
                return true;
            }
            if (stop.IsCancellationRequested)   // a Stop while the read above decided: no press
                return Stopped(what);
            if (!await WriteAsync(io, ApuStopPress, 1).ConfigureAwait(false))
                return false;
            _apuStopPressed = (io, io.NowMs);
            _apuStartPressed = null;   // stopping: an earlier start press is over
            pressedOn = io;
            return true;
        }).ConfigureAwait(false);
        if (!pressed) return false;
        if (alreadyStopped) return true;

        // The gate is released: the spin-down wait is read-only and holds nothing.
        var waitOn = pressedOn!;
        return await WaitUntilAsync(waitOn,
            async () => await ReadAsync(waitOn, ApuRpm).ConfigureAwait(false) is double v && v < L1011FoStateEvaluator.ApuStoppedRpm,
            waitMs, stop, $"{what}, {ApuRpm} falling").ConfigureAwait(false);
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

    // ================= the engine start =================

    /// <summary>
    /// Engine <paramref name="n"/>'s ground start, target 1 (any other reads and writes nothing, logged: a target
    /// asking for off never turns a starter on); a <see cref="StopAware"/> composite, outside the gate. One write
    /// phase under the gate (<see cref="UnderGateAsync"/>, never begun after the flow's Stop): N3
    /// read fresh, unread, not done and nothing written; at or above
    /// <see cref="L1011FoStateEvaluator.EngineRunningN3"/>, done with nothing written (a starter is never engaged on
    /// a running engine). Then the switch read fresh: unread, not done and never clicked; already on, not clicked
    /// again (a second call while the first still watches, the pilot's own start) and not recorded, but the
    /// rotation is still watched; otherwise, the Stop checked once more, then, for a flow step, the start recorded
    /// with the run's Stop (<see cref="_flowGroundStarts"/>) BEFORE the click goes out, since a click that lands
    /// late or reads back late is still one to release, then the row's click (<c>L:SWITCH_GND_START_ENG_n</c> 1 with
    /// <c>H:STARTER</c> and <c>H:STARTER_1</c>, the event the generated map sends for all three engines) read back
    /// on. A click not done (refused, not completed in time, not reading on, or a throw) is not done, and the start
    /// is then released over fuel off (<see cref="GiveUpGroundStartAsync"/>, the switch read first, so a switch
    /// still off costs a read). Then, the gate released, done once N3 reads <see cref="RotationRiseN3"/> above what
    /// it first read, within <see cref="RotationWaitMs"/> (<see cref="WaitUntilAsync"/>, read-only: a flow's Pause
    /// holds it, its Stop ends it). On the time-out or the Stop the start is given up the same way and the step is
    /// not done. The switch is left on once turning: the aircraft releases it itself past about 55 % N3. Speaks
    /// nothing.
    /// </summary>
    private async Task<bool> GroundStartAsync(int n, int target, CancellationToken stop)
    {
        string what = GroundStart(n);
        if (target != 1)
        {
            Note($"{what}: not done, the ground start is 1 (on), not {target.ToString(CultureInfo.InvariantCulture)}");
            return false;
        }
        string n3Key = N3Key(n), switchKey = GroundStartKey(n);
        double from = 0;
        bool running = false, ownStart = false, clickNotDone = false;
        IL1011FoTransport? turnedOn = null;
        bool watched = await UnderGateAsync(what, stop, async io =>
        {
            if (await ReadAsync(io, n3Key).ConfigureAwait(false) is not double n3)
            {
                Note($"{what}: not done, {n3Key} unread, {switchKey} not written");
                return false;
            }
            if (n3 >= L1011FoStateEvaluator.EngineRunningN3)
            {
                Note($"{what}: {n3Key} reads {n3.ToString(CultureInfo.InvariantCulture)}, the engine runs: {switchKey} not written");
                running = true;
                return true;
            }
            from = n3;
            if (await ReadAsync(io, switchKey).ConfigureAwait(false) is not double s)
            {
                Note($"{what}: not done, {switchKey} unread, not clicked");
                return false;
            }
            if (s >= PositionTolerance)
            {
                Note($"{what}: {switchKey} already reads on: not clicked again, the rotation still watched");
                ownStart = OwnsStart(n, stop);   // this run's own, from an earlier call
            }
            else
            {
                if (stop.IsCancellationRequested)   // a Stop while the reads above decided: no write
                    return Stopped(what);
                if (stop.CanBeCanceled)
                {
                    _flowGroundStarts[n] = stop;
                    ownStart = true;
                }
                bool clicked;
                try
                {
                    clicked = await WriteAsync(io, switchKey, 1, readJustNow: true).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Note($"{what}: {switchKey} -> 1 threw: {ex.Message}", warn: true);
                    clicked = false;
                }
                if (!clicked)
                {
                    clickNotDone = true;
                    return false;
                }
            }
            turnedOn = io;
            return true;
        }).ConfigureAwait(false);
        if (clickNotDone)
        {
            await GiveUpGroundStartAsync(n, stop, ownStart, what).ConfigureAwait(false);
            return false;
        }
        if (!watched) return false;
        if (running) return true;

        // The gate is released: the rotation watch is read-only and holds nothing.
        var waitOn = turnedOn!;
        double rise = from + RotationRiseN3;
        if (await WaitUntilAsync(waitOn,
                async () => await ReadAsync(waitOn, n3Key).ConfigureAwait(false) is double v && v >= rise,
                RotationWaitMs, stop, $"{what}, {n3Key} rising from {from.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false))
            return true;
        await GiveUpGroundStartAsync(n, stop, ownStart, what).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Engine <paramref name="n"/>'s light-off watch, target ignored; a <see cref="StopAware"/> composite with no
    /// write phase of its own (the MD-11's light-up watch): done once N3 reads at least <see cref="FuelOnN3"/>,
    /// the point the fuel and ignition go on, within <see cref="LightOffWaitMs"/> (<see cref="WaitUntilAsync"/>,
    /// read-only; already there, done at once: a resumed flow). On the time-out or the flow's Stop the start is
    /// given up (<see cref="GiveUpGroundStartAsync"/>): released over fuel off, once, by whoever takes it out of
    /// <see cref="_flowGroundStarts"/>; after a Stop that is the run's Stop clean-up or the window's close, never
    /// this watch. Speaks nothing.
    /// </summary>
    private async Task<bool> EngineLightOffAsync(int n, CancellationToken stop)
    {
        string what = EngineLightOff(n);
        var io = _io;
        if (io is not { Ready: true }) return false;
        // Decided before the wait: once taken out by someone else, it is no longer this watch's to release.
        bool ownStart = OwnsStart(n, stop);
        string n3Key = N3Key(n);
        if (await WaitUntilAsync(io,
                async () => await ReadAsync(io, n3Key).ConfigureAwait(false) is double v && v >= FuelOnN3,
                LightOffWaitMs, stop, $"{what}, {n3Key} up to {FuelOnN3.ToString(CultureInfo.InvariantCulture)}").ConfigureAwait(false))
            return true;
        await GiveUpGroundStartAsync(n, stop, ownStart, what).ConfigureAwait(false);
        return false;
    }

    /// <summary>
    /// Engine <paramref name="n"/>'s idle wait, target ignored; a <see cref="StopAware"/> composite, read-only:
    /// done once N3 has read at least <see cref="IdleN3"/> for <see cref="IdleHoldMs"/> without a break, within
    /// <see cref="IdleWaitMs"/> (<see cref="WaitUntilAsync"/>). A reading below, or unread, starts the hold again,
    /// and so does a Pause, since the engine was not watched. Not done on the time-out or the flow's Stop; nothing
    /// is written either way (the fuel is on by now: the start runs on). Speaks nothing.
    /// </summary>
    private async Task<bool> EngineIdleAsync(int n, CancellationToken stop)
    {
        string what = EngineIdle(n);
        var io = _io;
        if (io is not { Ready: true }) return false;
        string n3Key = N3Key(n);
        long? aboveSince = null;
        return await WaitUntilAsync(io, async () =>
            {
                if (await ReadAsync(io, n3Key).ConfigureAwait(false) is double v && v >= IdleN3)
                {
                    aboveSince ??= io.NowMs;
                    return io.NowMs - aboveSince.Value >= IdleHoldMs;
                }
                aboveSince = null;
                return false;
            }, IdleWaitMs, stop,
            $"{what}, {n3Key} held at {IdleN3.ToString(CultureInfo.InvariantCulture)} for {IdleHoldMs.ToString(CultureInfo.InvariantCulture)} ms",
            resumed: () => aboveSince = null).ConfigureAwait(false);
    }

    /// <summary>True when <paramref name="stop"/> is a flow's and engine <paramref name="n"/>'s recorded start is
    /// that run's.</summary>
    private bool OwnsStart(int n, CancellationToken stop) =>
        stop.CanBeCanceled && _flowGroundStarts.TryGetValue(n, out var recordedFor) && recordedFor == stop;

    /// <summary>
    /// A ground start given up: its watch timed out or was stopped, or its click was not done. The start is released
    /// over fuel off (<see cref="ReleaseGroundStartAsync"/>), once, by whoever takes it out of
    /// <see cref="_flowGroundStarts"/>:
    /// <list type="bullet">
    /// <item>A start this run recorded (<paramref name="ownStart"/>), on the flow's Stop: left where it is. The run's
    /// Stop clean-up (<see cref="OnFlowStoppedAsync"/>, which FlowManager runs once the step has returned) or the
    /// window's close (<see cref="ReleaseHeldPresses"/>, on the UI thread while the aircraft still takes writes) takes
    /// it. The watch resumes from the cancel on another thread, so taking it here could beat the window's close to it
    /// and then find the First Officer detached, or the aircraft gone, before its click.</item>
    /// <item>A start this run recorded, with no Stop (a time-out, a click not done): taken out under the gate, so
    /// whoever holds the record also holds the means to release it. Gone already (the window's close released it, or
    /// its fuel and ignition went on): nothing is sent.</item>
    /// <item>A start not recorded (a checklist tick's, a switch found already on): released under the gate.</item>
    /// </list>
    /// The release runs under the gate whatever the Stop: it is the clean-up, never a new step.
    /// </summary>
    private async Task GiveUpGroundStartAsync(int n, CancellationToken stop, bool ownStart, string what)
    {
        if (ownStart && stop.IsCancellationRequested)
        {
            Note($"{what}: stopped; its ground start left to the stop clean-up or the window's close");
            return;
        }
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (ownStart && !_flowGroundStarts.TryRemove(new KeyValuePair<int, CancellationToken>(n, stop)))
            {
                Note($"{what}: its ground start was already released, or its fuel and ignition went on: nothing sent");
                return;
            }
            await ReleaseOnAsync(_io, n, $"{what} gave up").ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Under the gate: engine <paramref name="n"/>'s start released on <paramref name="io"/>, the transport read
    /// under the gate. Detached (the window closed, and its close released what it could, <see cref="ReleaseHeldPresses"/>)
    /// or unable to reach the aircraft, left as it is and logged; a throw is logged. Nothing is spoken.
    /// </summary>
    private async Task ReleaseOnAsync(IL1011FoTransport? io, int n, string why)
    {
        string engine = $"engine {n.ToString(CultureInfo.InvariantCulture)}";
        if (io is null)
        {
            Note($"{engine}: {why} with its ground start on; the First Officer is detached, so it is left as it is");
            return;
        }
        if (!io.Ready)
        {
            Note($"{engine}: {why} with its ground start on; the aircraft cannot be reached, so it is left as it is", warn: true);
            return;
        }
        try
        {
            await ReleaseGroundStartAsync(io, n).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Note($"{engine}: {why}: the ground start release threw: {ex.Message}", warn: true);
        }
    }

    /// <summary>
    /// Engine <paramref name="n"/>'s ground start released, never a fuel write: its fuel and ignition read fresh
    /// first (the row: the stock fuel valve). Unread, the switch is left as it is; on, the start runs on, and the
    /// aircraft releases the switch itself past about 55 % N3. Off, the switch read fresh: unread, never clicked
    /// (its click carries the same <c>H:STARTER</c> events either way, so it is sent only from a known position);
    /// already off, nothing sent; on, the switch row's own click to 0 (<c>L:SWITCH_GND_START_ENG_n</c> 0 with its
    /// <c>H:STARTER</c> events) read back off. Each case logged. Under the caller's gate.
    /// </summary>
    private async Task ReleaseGroundStartAsync(IL1011FoTransport io, int n)
    {
        string engine = $"engine {n.ToString(CultureInfo.InvariantCulture)}";
        string fuelKey = FuelIgnitionKey(n), switchKey = GroundStartKey(n);
        if (await ReadAsync(io, fuelKey).ConfigureAwait(false) is not double fuel)
        {
            Note($"{engine}: {fuelKey} unread, its ground start left as it is", warn: true);
            return;
        }
        if (fuel >= PositionTolerance)
        {
            Note($"{engine}: fuel on, the start runs on");
            return;
        }
        if (await ReadAsync(io, switchKey).ConfigureAwait(false) is not double reading)
        {
            Note($"{engine}: fuel off, {switchKey} unread: not clicked, its ground start left as it is", warn: true);
            return;
        }
        if (reading < PositionTolerance)
        {
            Note($"{engine}: fuel off, its ground start already reads off: nothing sent");
            return;
        }
        bool off = await WriteAsync(io, switchKey, 0, readJustNow: true).ConfigureAwait(false);
        Note(off
            ? $"{engine}: fuel off, its ground start released and read back off"
            : $"{engine}: fuel off, its ground start does not read off after its release: the starter may still be engaged",
            warn: !off);
    }

    /// <summary>
    /// A flow was stopped part-way (Stop Flow, another flow started, the window closed, a step whose failure stops
    /// the flow); the run's Stop is cancelled by then. A ground start that run had this executor turn on is never
    /// left turning a dry engine: each recorded with a cancelled Stop is taken out first (another run's start
    /// stays its own), then, under the gate, released over fuel off (<see cref="ReleaseGroundStartAsync"/>). Never an
    /// engine whose fuel and ignition are on (forgotten when they went on), never a start a checklist tick or the
    /// pilot made, never on an unread value. Detached (the window closed), there is no aircraft to write to and the
    /// window's close already released it (<see cref="ReleaseHeldPresses"/>); unable to reach the aircraft, it is
    /// left and logged. Nothing is spoken: the flow says how it ended.
    /// </summary>
    public async Task OnFlowStoppedAsync()
    {
        var stopped = _flowGroundStarts.Where(p => p.Value.IsCancellationRequested).OrderBy(p => p.Key).ToList();
        if (stopped.Count == 0) return;   // nothing started for a stopped run: no wait on the gate
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var io = _io;   // read behind the gate: a window closing meanwhile detaches it
            foreach (var start in stopped)
            {
                if (!_flowGroundStarts.TryRemove(start)) continue;   // taken: whoever took it releases it
                await ReleaseOnAsync(io, start.Key, "flow stopped").ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The First Officer window is closing (an aircraft switch, app exit), its flow just cancelled; the stop
    /// clean-up then runs detached, with no aircraft to write to. So a ground start a stopped run left on is
    /// released now, while the sim still takes writes, on the UI thread: each recorded with a cancelled Stop taken
    /// out first, so it is released once, and released only over the CACHE reading its fuel and ignition off and its
    /// switch on (<see cref="ShouldReleaseGroundStart"/>; the window reads both each second), by the switch row's
    /// click to 0, not awaited and not read back; what the click came to (sent, refused, unavailable) is logged when
    /// it completes. Anything else is logged and left. Nothing is spoken.
    /// </summary>
    public void ReleaseHeldPresses()
    {
        var io = _io;
        foreach (var start in _flowGroundStarts.Where(p => p.Value.IsCancellationRequested).OrderBy(p => p.Key).ToList())
        {
            if (!_flowGroundStarts.TryRemove(start)) continue;
            int n = start.Key;
            string engine = $"engine {n.ToString(CultureInfo.InvariantCulture)}";
            if (io is not { Ready: true })
            {
                Note($"{engine}: window closing with its ground start on; the aircraft cannot be reached, so it is left as it is", warn: true);
                continue;
            }
            string key = GroundStartKey(n);
            if (!ShouldReleaseGroundStart(io.ReadCached(FuelIgnitionKey(n)), io.ReadCached(key)))
            {
                Note($"{engine}: window closing; its ground start left as it is (fuel on, the switch already off, or unread)");
                continue;
            }
            try
            {
                // Not awaited: the window is closing, and MainForm may disconnect right after.
                Note($"{engine}: window closing with its ground start on and the fuel off; releasing it, not read back");
                _ = io.SetAsync(key, 0).ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                        Note($"{engine}: window closing; its ground start release: {Describe(t.Result)}",
                            warn: t.Result.Outcome is not (L1011FoOutcome.Sent or L1011FoOutcome.AlreadyThere));
                    else
                        Note($"{engine}: window closing; its ground start release did not complete: {t.Exception?.GetBaseException().Message ?? "cancelled"}", warn: true);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                Note($"{engine}: window closing; the ground start release threw: {ex.Message}", warn: true);
            }
        }
    }

    /// <summary>The window's close releases a ground start only over a fuel and ignition read off and a switch read
    /// on (the MD-11's <c>ShouldPushStartIn</c>).</summary>
    internal static bool ShouldReleaseGroundStart(double? fuel, double? groundStart) =>
        fuel is double f && !double.IsNaN(f) && f < PositionTolerance
        && groundStart is double s && !double.IsNaN(s) && s >= PositionTolerance;

    /// <summary>
    /// Target 1, the three engine generators on line: each field relay closed, engines 1 to 3, then each breaker
    /// relay, each through <see cref="PressUntilRelayAsync"/>: a relay already closed is not pressed (live, an
    /// engine's field relay closed by itself at idle), an unread relay is never pressed, and each press is verified
    /// on its relay, never the button (live, a breaker closed 1 to 4 s after its press). One that does not close
    /// does not stop the next; a breaker whose field did not close is not pressed. Done when all six read closed.
    /// A <see cref="Pseudo"/> composite: atomic under the gate, and bounded. Any other target reads and writes
    /// nothing. Speaks nothing.
    /// </summary>
    private async Task<bool> EngineGeneratorsAsync(IL1011FoTransport io, int target)
    {
        string what = $"{EngineGenerators} -> {target.ToString(CultureInfo.InvariantCulture)}";
        if (target != 1)
        {
            Note($"{what}: not done, the engine generators are 1 (on line)");
            return false;
        }
        var fieldClosed = new bool[4];
        for (int n = 1; n <= 3; n++)
            fieldClosed[n] = await PressUntilRelayAsync(io, GenFieldPress(n), GenFieldRelay(n), closed: true,
                $"{what}, generator {n.ToString(CultureInfo.InvariantCulture)} field").ConfigureAwait(false);
        bool allClosed = true;
        for (int n = 1; n <= 3; n++)
        {
            string generator = $"generator {n.ToString(CultureInfo.InvariantCulture)}";
            if (!fieldClosed[n])
            {
                Note($"{what}: {generator} breaker not pressed, its field is not closed");
                allClosed = false;
                continue;
            }
            allClosed &= await PressUntilRelayAsync(io, GenBreakerPress(n), GenBreakerRelay(n), closed: true,
                $"{what}, {generator} breaker").ConfigureAwait(false);
        }
        return allClosed;
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

    // ================= the cabin pressurization =================

    /// <summary>The cabin barometric setting's readout (<c>KOHLSMAN SETTING MB:4</c>, hectopascals).</summary>
    internal const string CabinBaroReadout = "L1011_RO_CABIN_BARO";

    /// <summary>The selected cabin altitude's readout (<c>L:TARGET_CABIN_ALTITUDE</c>): delivered in METRES,
    /// written in feet.</summary>
    internal const string CabinAltitudeSelectedReadout = "L1011_RO_CABIN_ALT_SELECTED";

    /// <summary>
    /// The cabin barometric setting, target hectopascals × 100 as an int (the flow's provider gives it): one
    /// typed set through the transport's own entry (<see cref="IL1011FoTransport.SetCabinBaroAsync"/>, which
    /// refuses a setting outside its range, so nothing is written), then the readout read fresh until it reads
    /// the setting within <see cref="L1011FoStateEvaluator.CabinBaroToleranceHpa"/> or
    /// <see cref="VerifyTimeoutMs"/> passes. A <see cref="Pseudo"/> composite: no read first (the entry holds
    /// no position to plan from), atomic. Speaks nothing.
    /// </summary>
    private Task<bool> CabinBaroAsync(IL1011FoTransport io, int target)
    {
        double hectopascals = target / 100.0;
        string what = $"{CabinBaro} -> {target.ToString(CultureInfo.InvariantCulture)} ({hectopascals.ToString("0.00", CultureInfo.InvariantCulture)} hPa)";
        return SendAndReadBackAsync(io, what, io.SetCabinBaroAsync(hectopascals), CabinBaroReadout,
            v => Math.Abs(v - hectopascals) < L1011FoStateEvaluator.CabinBaroToleranceHpa);
    }

    /// <summary>
    /// The cabin altitude selector turned to a cabin altitude, target feet: the knob step that commands it
    /// (<see cref="L1011Pressurization.KnobStep"/>, clamped to the knob's travel) turned through the transport's
    /// own entry (<see cref="IL1011FoTransport.SetCabinAltitudeSelectorAsync"/>: it seeds the knob's step and
    /// clicks it, the aircraft computes the altitude), then the readout read fresh until it reads the target
    /// within <see cref="L1011FoStateEvaluator.CabinAltitudeToleranceFt"/> (it arrives in metres,
    /// <see cref="L1011FoStateEvaluator.MetresPerFoot"/>) or <see cref="VerifyTimeoutMs"/> passes. A
    /// <see cref="Pseudo"/> composite, atomic. Logged with the step and the target; speaks nothing.
    /// </summary>
    private Task<bool> CabinAltitudeSelectAsync(IL1011FoTransport io, int target)
    {
        int step = L1011Pressurization.KnobStep(target);
        string what = $"{CabinAltitudeSelect} -> {target.ToString(CultureInfo.InvariantCulture)} ft (knob step {step.ToString(CultureInfo.InvariantCulture)})";
        Note($"{what}: turning the cabin altitude selector");
        return SendAndReadBackAsync(io, what, io.SetCabinAltitudeSelectorAsync(step), CabinAltitudeSelectedReadout,
            metres => Math.Abs(metres / L1011FoStateEvaluator.MetresPerFoot - target) <= L1011FoStateEvaluator.CabinAltitudeToleranceFt);
    }

    /// <summary>
    /// One write that holds no position the executor could read first, then its readout read back: the write
    /// awaited for <see cref="WriteGraceMs"/> at most (<see cref="SendAsync"/>), a refusal or an unreachable
    /// aircraft not done and logged with the reason, a sent write done once <paramref name="readout"/> reads
    /// as <paramref name="ok"/> says (<see cref="ReadsAsync"/>).
    /// </summary>
    private async Task<bool> SendAndReadBackAsync(IL1011FoTransport io, string what, Task<L1011FoResult> write, string readout, Func<double, bool> ok)
    {
        if (await SendAsync(io, what, write, holdMs: 0).ConfigureAwait(false) is not { } result)
            return false;
        switch (result.Outcome)
        {
            case L1011FoOutcome.AlreadyThere:
                return true;
            case L1011FoOutcome.Sent:
                return await ReadsAsync(io, new[] { readout }, ok, what).ConfigureAwait(false);
            default:
                Note($"{what}: not sent, {Describe(result)}");
                return false;
        }
    }

    // ================= relay presses =================

    /// <summary>
    /// How long a relay a press drives may take to read as wanted: the aircraft's breaker relay can take about
    /// 4 s to close (a generator breaker was still open on an immediate read after its press).
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
    /// Read-only, and never under the gate: polls <paramref name="satisfied"/> every
    /// <see cref="PollMs"/> until it is true (true), or until <paramref name="limitMs"/> has passed on the
    /// transport's clock (false, logged with the limit). For a flow step (a <paramref name="stop"/> that can
    /// be cancelled) the flow's Pause holds it, and the time paused is not counted (the MD-11's
    /// <c>ApuStartToFinishAsync</c> shape); a hand tick (<see cref="CancellationToken.None"/>) never waits on
    /// a flow's Pause. The flow's Stop ends it with false (logged "<paramref name="what"/>: stopped") before
    /// the next ask: within a poll plus the time of one <paramref name="satisfied"/> call, which may be a
    /// fresh read of up to <see cref="ReadTimeoutMs"/>; at once while paused or already stopped. Writes
    /// nothing and speaks nothing. <paramref name="resumed"/>, when given, is called once a Pause that held
    /// the wait ends, before the next ask: a wait that counts time watched (the idle hold) starts it again,
    /// since nothing was watched while paused.
    /// </summary>
    internal async Task<bool> WaitUntilAsync(IL1011FoTransport io, Func<Task<bool>> satisfied, int limitMs, CancellationToken stop, string what,
        Action? resumed = null)
    {
        long deadline = io.NowMs + limitMs;
        while (true)
        {
            if (stop.CanBeCanceled)   // a flow step: its Pause holds the wait; a hand tick's never does
            {
                long pausedAt = io.NowMs;
                bool held = false;
                try
                {
                    var pause = _waitWhilePaused(stop);
                    held = !pause.IsCompleted;
                    await pause.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Stopped(what);
                }
                deadline += io.NowMs - pausedAt;   // a Pause is not counted
                if (held)
                    resumed?.Invoke();
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

    /// <summary>A line for debug.log ([CORE-15]), a not-done one or a composite's account of what it is about to
    /// do (<see cref="CabinAltitudeSelectAsync"/>); never spoken.</summary>
    private void Note(string line, bool warn = false)
    {
        if (warn) Log.Warn(LogCategory, line);
        else Log.Info(LogCategory, line);
        LogTap?.Invoke(line);
    }
}
