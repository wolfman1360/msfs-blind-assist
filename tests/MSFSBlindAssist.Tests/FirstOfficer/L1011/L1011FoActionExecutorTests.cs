using System.Collections.Concurrent;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's executor, single writes, on the fake TriStar: every readable key read
/// fresh before its write (so the definition plans from a delivered value) and read back after it; a
/// toggle never sent on an unread value; a press done when sent; one write at a time; nothing spoken.
/// </summary>
public class L1011FoActionExecutorTests
{
    private const string ContinuousIgnition = "SWITCH_CONT_IGNITION";   // a switch without a toggle
    private const string SeatBelts = "SWITCH_SEATBELT_LIGHT";           // carries a TOGGLE K: event
    private const string TieClose1 = "SWITCH_TIE_FLOW_1";               // a press: holds no position
    private const string LandingLeft = "TOGGLE_LANDING_LEFT";
    private const string LandingRight = "TOGGLE_LANDING_RIGHT";
    private static readonly string[] Galley = { "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3" };
    private static readonly string[] Altimeters = { "L1011_RO_ALTIMETER_1", "L1011_RO_ALTIMETER_2", "L1011_RO_ALTIMETER_3" };

    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private readonly FakeL1011Aircraft _ac = new();
    private readonly L1011FoActionExecutor _exec = new();
    private readonly ConcurrentQueue<string> _spoken = new();
    private readonly ConcurrentQueue<string> _logged = new();

    public L1011FoActionExecutorTests()
    {
        _exec.SetTransport(_ac);
        _exec.SetSpeech(_spoken.Enqueue);
        _exec.LogTap = _logged.Enqueue;
    }

    private sealed class Step : IFlowStepDispatch
    {
        public FlowStepActionType ActionType { get; init; } = FlowStepActionType.SetSwitch;
        public string? EventName { get; init; }
        public int? TargetValue { get; init; }
        public IReadOnlyList<(string EventName, int? TargetValue)> MultiActions { get; init; } = Array.Empty<(string, int?)>();
        public bool UsesMouseFlag => false;
        public bool IsMomentary => false;
    }

    /// <summary>
    /// The fake behind a door the test opens. A write to a key passed to <see cref="Hold"/> waits until
    /// the test releases it (the fake completes every call at once, so without this nothing is ever in
    /// flight); with <see cref="TimeStandsStill"/> no delay ever ends, so the executor's write time-out
    /// cannot fire while a write is held. <see cref="Throw"/> makes a key's write throw. Everything else
    /// is the fake's.
    /// </summary>
    private sealed class HoldingTransport : IL1011FoTransport
    {
        private readonly FakeL1011Aircraft _ac;
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, bool> _throwing = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _started = new();

        public HoldingTransport(FakeL1011Aircraft ac) => _ac = ac;

        public bool TimeStandsStill { get; init; }

        /// <summary>Every write the executor started, in order, held or not.</summary>
        public IReadOnlyList<string> Started => _started.ToList();

        /// <summary>The next write to <paramref name="key"/> waits for the returned door to be opened.</summary>
        public TaskCompletionSource Hold(string key)
        {
            var door = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _held[key] = door;
            return door;
        }

        public void Throw(string key) => _throwing[key] = true;

        public bool Ready => _ac.Ready;
        public long NowMs => _ac.NowMs;

        public async Task<L1011FoResult> SetAsync(string key, double value)
        {
            _started.Enqueue(key);
            if (_throwing.ContainsKey(key))
                throw new InvalidOperationException("the test's transport threw");
            if (_held.TryRemove(key, out var door))
                await door.Task.ConfigureAwait(false);
            return await _ac.SetAsync(key, value).ConfigureAwait(false);
        }

        public Task<L1011FoResult> SetAltimetersAsync(double millibars) => _ac.SetAltimetersAsync(millibars);
        public Task<L1011FoResult> SetCabinBaroAsync(double millibars) => _ac.SetCabinBaroAsync(millibars);
        public Task<L1011FoResult> SetCabinAltitudeSelectorAsync(int step) => _ac.SetCabinAltitudeSelectorAsync(step);
        public bool CanReadBack(string key) => _ac.CanReadBack(key);
        public Task<double?> ReadFreshAsync(string key, int timeoutMs) => _ac.ReadFreshAsync(key, timeoutMs);
        public double? ReadCached(string key) => _ac.ReadCached(key);
        public void MuteLampSpeech(int ms) => _ac.MuteLampSpeech(ms);

        public Task DelayAsync(int ms) => TimeStandsStill ? new TaskCompletionSource().Task : _ac.DelayAsync(ms);
    }

    private List<(FakeL1011IoKind Kind, string Key)> IoKinds() => _ac.Io.Select(e => (e.Kind, e.Key)).ToList();

    // ---- A toggle is never sent on an unread value ------------------------------------------------------

    [Fact]
    public async Task A_toggle_on_an_unread_switch_is_never_sent()
    {
        Assert.False(await _exec.SetSeatbeltSign(true));

        Assert.Equal(new[] { SeatBelts }, _ac.FreshReads);   // one read attempted, nothing delivered
        Assert.Empty(_ac.Writes);                              // the definition refused: no toggle sent
        Assert.Contains(_logged, line => line.Contains(SeatBelts) && line.Contains(L1011WritePlan.UnknownPositionRefusal));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_toggle_on_a_switch_read_first_is_sent_once_and_read_back()
    {
        _ac.Set(SeatBelts, 0);

        Assert.True(await _exec.SetSeatbeltSign(true));

        Assert.Equal(new[] { (SeatBelts, 1.0) }, _ac.Writes);
        Assert.Equal(1, _ac.Get(SeatBelts));
    }

    // ---- Read first, write, read back --------------------------------------------------------------------

    [Fact]
    public async Task A_switch_is_read_first_written_and_read_back()
    {
        _ac.Set(ContinuousIgnition, 1);

        Assert.True(await _exec.Set(ContinuousIgnition, 0));

        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, ContinuousIgnition),
            (FakeL1011IoKind.Write, ContinuousIgnition),
            (FakeL1011IoKind.FreshRead, ContinuousIgnition),
        }, IoKinds());
        Assert.Equal(new double?[] { 1, 0, 0 }, _ac.Io.Select(e => e.Value));
    }

    [Fact]
    public async Task An_unread_switch_without_a_toggle_is_the_definitions_call_then_read_back()
    {
        Assert.True(await _exec.Set(ContinuousIgnition, 1));

        Assert.Equal(new[] { (ContinuousIgnition, 1.0) }, _ac.Writes);
        Assert.Equal(new[] { ContinuousIgnition, ContinuousIgnition }, _ac.FreshReads);
    }

    [Fact]
    public async Task A_write_the_aircraft_never_takes_is_not_done_after_the_verify_time_out()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.NeverDeliver(ContinuousIgnition);

        Assert.False(await _exec.Set(ContinuousIgnition, 1));

        Assert.Equal(new[] { (ContinuousIgnition, 1.0) }, _ac.Writes);   // sent once, never repeated
        Assert.InRange(_ac.NowMs, L1011FoActionExecutor.VerifyTimeoutMs,
            L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + L1011FoActionExecutor.ReadTimeoutMs);
        Assert.True(_ac.FreshReads.Count > 2);                            // polled, not read once
        Assert.Contains(_logged, line => line.Contains(ContinuousIgnition));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_press_is_done_when_sent_with_nothing_read()
    {
        // Nothing on line: the tie stays open, and the press is still done (it holds no position to read).
        _ac.Set("L1011_RO_EXT_PWR_BREAKER", 0);
        _ac.Set("L1011_RO_AC_TIE_1", 0);

        Assert.True(await _exec.Set(TieClose1, 1));

        Assert.Equal(new[] { (TieClose1, 1.0) }, _ac.Writes);
        Assert.Empty(_ac.FreshReads);
    }

    [Fact]
    public async Task A_switch_already_there_writes_nothing_and_is_done()
    {
        _ac.Set(ContinuousIgnition, 1);

        Assert.True(await _exec.Set(ContinuousIgnition, 1));

        Assert.Empty(_ac.Writes);
        Assert.Equal(new[] { ContinuousIgnition }, _ac.FreshReads);   // the read first, no verify
    }

    [Theory]
    [InlineData("NOT_A_TRISTAR_KEY")]
    [InlineData("LAMP_STBY_UNARM_LIGHT")]   // read, never written
    public async Task A_key_the_definition_does_not_take_is_not_done_and_logged(string key)
    {
        Assert.False(await _exec.Set(key, 1));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(key));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_refused_write_is_not_done_and_logged_with_its_reason()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.Refuse(ContinuousIgnition);

        Assert.False(await _exec.Set(ContinuousIgnition, 1));

        Assert.Empty(_ac.Writes);
        Assert.Equal(new[] { ContinuousIgnition }, _ac.FreshReads);   // no verify after a refusal
        Assert.Contains(_logged, line => line.Contains(ContinuousIgnition) && line.Contains(FakeL1011Aircraft.RefusalReason));
        Assert.Empty(_spoken);
    }

    // ---- Flow steps ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_switch_step_with_no_target_sets_one()
    {
        _ac.Set(ContinuousIgnition, 0);

        Assert.True(await _exec.ExecuteStepAsync(new Step { EventName = ContinuousIgnition }));

        Assert.Equal(new[] { (ContinuousIgnition, 1.0) }, _ac.Writes);
    }

    [Fact]
    public async Task A_multi_step_sets_every_part_in_order()
    {
        foreach (var key in Galley)
            _ac.Set(key, 1);

        Assert.True(await _exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = Galley.Select(k => (k, (int?)0)).ToArray(),
        }));

        Assert.Equal(Galley.Select(k => (k, 0.0)), _ac.Writes);
    }

    [Fact]
    public async Task A_multi_step_with_a_part_failing_is_not_done_but_still_tries_every_part()
    {
        foreach (var key in Galley)
            _ac.Set(key, 1);
        _ac.Refuse(Galley[1]);

        Assert.False(await _exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = Galley.Select(k => (k, (int?)0)).ToArray(),
        }));

        Assert.Equal(new[] { (Galley[0], 0.0), (Galley[2], 0.0) }, _ac.Writes);
    }

    [Fact]
    public async Task A_step_it_does_not_dispatch_is_not_done_and_writes_nothing()
    {
        _ac.Set(ContinuousIgnition, 0);

        Assert.False(await _exec.ExecuteStepAsync(new Step { ActionType = FlowStepActionType.WaitSeconds, EventName = ContinuousIgnition }));
        Assert.False(await _exec.ExecuteStepAsync(new Step { EventName = null }));
        Assert.False(await _exec.ExecuteStepAsync(new Step { ActionType = FlowStepActionType.SetSwitchMultiple }));

        Assert.Empty(_ac.Io);
    }

    [Fact]
    public void No_key_is_a_pseudo_key_yet()
    {
        Assert.False(L1011FoActionExecutor.IsPseudoKey(ContinuousIgnition));
        Assert.False(L1011FoActionExecutor.IsPseudoKey("NOT_A_TRISTAR_KEY"));
    }

    // ---- One write at a time ---------------------------------------------------------------------------------

    [Fact]
    public async Task Two_concurrent_writes_are_serialized_the_second_starting_after_the_first_is_read_back()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.Set(Galley[0], 1);
        var io = new HoldingTransport(_ac) { TimeStandsStill = true };
        _exec.SetTransport(io);
        var door = io.Hold(ContinuousIgnition);

        var first = _exec.Set(ContinuousIgnition, 1);
        var second = _exec.Set(Galley[0], 0);

        Assert.Equal(new[] { ContinuousIgnition }, io.Started);              // the first is in flight
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        Assert.DoesNotContain(_ac.Io, e => e.Key == Galley[0]);             // the second has not even read

        door.SetResult();
        Assert.Equal(new[] { true, true }, await Task.WhenAll(first, second).WaitAsync(Hang));

        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, ContinuousIgnition),
            (FakeL1011IoKind.Write, ContinuousIgnition),
            (FakeL1011IoKind.FreshRead, ContinuousIgnition),   // the first's read-back
            (FakeL1011IoKind.FreshRead, Galley[0]),            // only then the second
            (FakeL1011IoKind.Write, Galley[0]),
            (FakeL1011IoKind.FreshRead, Galley[0]),
        }, IoKinds());
    }

    [Fact]
    public async Task The_drain_waits_for_a_running_write_and_its_read_back()
    {
        _ac.Set(ContinuousIgnition, 0);
        var io = new HoldingTransport(_ac) { TimeStandsStill = true };
        _exec.SetTransport(io);
        var door = io.Hold(ContinuousIgnition);
        var write = _exec.Set(ContinuousIgnition, 1);

        var drain = _exec.WaitForDispatchDrainAsync();
        Assert.False(drain.IsCompleted);

        door.SetResult();
        await drain.WaitAsync(Hang);

        Assert.Equal(new[] { ContinuousIgnition, ContinuousIgnition }, _ac.FreshReads);   // read first, read back
        Assert.True(await write.WaitAsync(Hang));
    }

    [Fact]
    public async Task The_drain_completes_at_once_when_idle()
    {
        var drain = _exec.WaitForDispatchDrainAsync();

        Assert.True(drain.IsCompleted);
        await drain;
    }

    [Fact]
    public async Task A_write_that_throws_is_not_done_logged_and_frees_the_next()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.Set(Galley[0], 1);
        var io = new HoldingTransport(_ac);
        _exec.SetTransport(io);
        io.Throw(ContinuousIgnition);

        Assert.False(await _exec.Set(ContinuousIgnition, 1));
        Assert.Contains(_logged, line => line.Contains(ContinuousIgnition));

        Assert.True(await _exec.Set(Galley[0], 0).WaitAsync(Hang));
    }

    [Fact]
    public async Task A_write_that_never_completes_is_not_done_after_the_write_time_out_and_frees_the_next()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.Set(Galley[0], 1);
        var io = new HoldingTransport(_ac);   // time moves: the time-out can fire
        _exec.SetTransport(io);
        io.Hold(ContinuousIgnition);         // never opened

        Assert.False(await _exec.Set(ContinuousIgnition, 1).WaitAsync(Hang));

        Assert.Equal(L1011FoActionExecutor.WriteTimeoutMs, _ac.NowMs);   // measured on the transport's clock
        Assert.Equal(12_000, L1011FoActionExecutor.WriteTimeoutMs);      // the longest hold, 2 s, plus 10 s
        Assert.Contains(_logged, line => line.Contains(ContinuousIgnition));
        Assert.True(await _exec.Set(Galley[0], 0).WaitAsync(Hang));
    }

    // ---- Unavailable -------------------------------------------------------------------------------------------

    [Fact]
    public async Task With_no_transport_it_is_unavailable_and_does_nothing()
    {
        var exec = new L1011FoActionExecutor();

        Assert.False(exec.IsAvailable);
        Assert.False(await exec.Set(ContinuousIgnition, 1));
        Assert.False(await exec.ExecuteStepAsync(new Step { EventName = ContinuousIgnition, TargetValue = 1 }));
        Assert.False(await exec.SetSeatbeltSign(true));
        Assert.False(await exec.SetLandingLights(true));
        Assert.False(await exec.SetAltimetersStandardAsync());
    }

    [Fact]
    public async Task A_transport_that_cannot_reach_the_aircraft_reads_and_writes_nothing()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.Ready = false;

        Assert.False(_exec.IsAvailable);
        Assert.False(await _exec.Set(ContinuousIgnition, 1));
        Assert.False(await _exec.SetAltimetersStandardAsync());

        Assert.Empty(_ac.Io);
    }

    [Fact]
    public async Task An_unavailable_transport_refuses_at_once_without_waiting_for_the_gate()
    {
        _ac.Set(ContinuousIgnition, 0);
        var io = new HoldingTransport(_ac) { TimeStandsStill = true };
        _exec.SetTransport(io);
        var door = io.Hold(ContinuousIgnition);
        var running = _exec.Set(ContinuousIgnition, 1);   // holds the gate

        _ac.Ready = false;
        var refused = _exec.Set(Galley[0], 0);
        Assert.True(refused.IsCompleted);                 // never queued behind the running write
        Assert.False(await refused);

        _ac.Ready = true;
        door.SetResult();
        Assert.True(await running.WaitAsync(Hang));
        Assert.DoesNotContain(_ac.Io, e => e.Key == Galley[0]);
    }

    [Fact]
    public async Task A_write_queued_when_the_transport_is_detached_writes_nothing()
    {
        _ac.Set(ContinuousIgnition, 0);
        _ac.Set(Galley[0], 1);
        var io = new HoldingTransport(_ac) { TimeStandsStill = true };
        _exec.SetTransport(io);
        var door = io.Hold(ContinuousIgnition);
        var running = _exec.Set(ContinuousIgnition, 1);
        var queued = _exec.Set(Galley[0], 0);

        _exec.SetTransport(null);   // the window closed
        door.SetResult();

        Assert.True(await running.WaitAsync(Hang));      // finished on the transport it started with
        Assert.False(await queued.WaitAsync(Hang));
        Assert.DoesNotContain(_ac.Io, e => e.Key == Galley[0]);
    }

    // ---- The automation's writes -----------------------------------------------------------------------------

    [Fact]
    public async Task The_seat_belt_signs_go_on_and_off()
    {
        _ac.Set(SeatBelts, 0);

        Assert.True(await _exec.SetSeatbeltSign(true));
        Assert.True(await _exec.SetSeatbeltSign(false));

        Assert.Equal(new[] { (SeatBelts, 1.0), (SeatBelts, 0.0) }, _ac.Writes);
    }

    [Fact]
    public async Task Both_landing_lights_are_set()
    {
        _ac.Set(LandingLeft, 0);
        _ac.Set(LandingRight, 0);

        Assert.True(await _exec.SetLandingLights(true));
        Assert.Equal(new[] { (LandingLeft, 1.0), (LandingRight, 1.0) }, _ac.Writes);

        Assert.True(await _exec.SetLandingLights(false));
        Assert.Equal(0, _ac.Get(LandingLeft));
        Assert.Equal(0, _ac.Get(LandingRight));
    }

    [Fact]
    public async Task A_landing_light_failing_is_not_done_but_the_other_is_still_set()
    {
        _ac.Set(LandingLeft, 0);
        _ac.Set(LandingRight, 0);
        _ac.Refuse(LandingLeft);

        Assert.False(await _exec.SetLandingLights(true));

        Assert.Equal(new[] { (LandingRight, 1.0) }, _ac.Writes);
    }

    [Fact]
    public async Task The_altimeters_go_to_standard_and_are_read_back_each_fresh()
    {
        Assert.True(await _exec.SetAltimetersStandardAsync());

        Assert.Equal(new[] { 1013.25 }, _ac.AltimeterWrites);
        Assert.Equal(Altimeters, _ac.FreshReads);
    }

    [Fact]
    public async Task An_altimeter_that_never_reads_standard_is_not_done_after_the_verify_time_out()
    {
        _ac.NeverDeliver(Altimeters[1]);
        _ac.Set(Altimeters[1], 1020);

        Assert.False(await _exec.SetAltimetersStandardAsync());

        Assert.Equal(new[] { 1013.25 }, _ac.AltimeterWrites);   // written once
        Assert.InRange(_ac.NowMs, L1011FoActionExecutor.VerifyTimeoutMs,
            L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + 3 * L1011FoActionExecutor.ReadTimeoutMs);
        Assert.Contains(_logged, line => line.Contains(Altimeters[1]));
        Assert.Empty(_spoken);
    }

    // ---- Speech --------------------------------------------------------------------------------------------------

    [Fact]
    public void Flow_progress_speech_is_on_until_a_flow_attaches_its_setting()
    {
        Assert.True(_exec.SpeaksFlowProgress);

        bool speaks = false;
        _exec.AttachFlowProgressSpeech(() => speaks);
        Assert.False(_exec.SpeaksFlowProgress);

        speaks = true;
        Assert.True(_exec.SpeaksFlowProgress);
    }

    // ================= Composites =================================================================================

    private const string Battery = "TOGGLE_BATTERY";
    private const string StandbyGuard = "GUARD_STBY_POWER";
    private const string StandbyPower = "TOGGLE_STBY_POWER";
    private const string Unarmed = "LAMP_STBY_UNARM_LIGHT";
    private const string Flowbar = "LAMP_STBY_PWR_FLOWBAR_LIGHT";
    private const string AcStandbyFail = "LAMP_AC_STBY_FAIL_LIGHT";
    private const string DcStandbyFail = "LAMP_DC_STBY_FAIL_LIGHT";
    private const string AcEssentialFail = "LAMP_AC_ESS_FAIL_LIGHT";
    private const string DcEssentialFail = "LAMP_DC_ESS_FAIL_LIGHT";
    private const string ExtPower = "TOGGLE_EXT_PWR";
    private const string ExtPowerAvailable = "LAMP_EXTERNAL_POWER_AVAILABLE";
    private const string ExtPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";
    private static string TieClose(int n) => $"SWITCH_TIE_FLOW_{n}";
    private static string Tie(int n) => $"L1011_RO_AC_TIE_{n}";

    private const string Pass = "Standby power check: pass";

    /// <summary>The check's writes, in the checklist's order (lines 18 to 32).</summary>
    private static readonly (string, double)[] CheckWrites =
    {
        (Battery, 0), (StandbyGuard, 1), (StandbyPower, 2),   // step 1
        (Battery, 1), (StandbyPower, 1), (StandbyGuard, 0),   // step 3
    };

    /// <summary>Battery on, standby power Off under its closed guard, external power off line.</summary>
    private void SeedStandbyCheck()
    {
        _ac.Set(Battery, 1);
        _ac.Set(StandbyPower, 0);
        _ac.Set(StandbyGuard, 0);
        _ac.Set(ExtPowerBreaker, 0);
    }

    private void AssertEndsArmed()
    {
        Assert.Equal(1, _ac.Get(Battery));
        Assert.Equal(1, _ac.Get(StandbyPower));
        Assert.Equal(0, _ac.Get(StandbyGuard));
    }

    private static bool IsLampOrBreaker(string key) => key.StartsWith("LAMP_", StringComparison.Ordinal) || key == ExtPowerBreaker;

    /// <summary>
    /// The fake behind a script: <see cref="AfterWrite"/> runs an action once, right after a given write has
    /// reached the fake (so a light can go wrong between the check's steps), <see cref="AfterRead"/> once after
    /// the nth fresh read of a key, and <see cref="Events"/> lists the lamp mutes, fresh reads and writes in the
    /// order they arrived. Everything else is the fake's.
    /// </summary>
    private sealed class ScriptedTransport : IL1011FoTransport
    {
        private readonly FakeL1011Aircraft _ac;
        private readonly List<(string Key, double Value, Action Act)> _after = new();
        private readonly List<(string Key, int Nth, Action Act)> _afterRead = new();
        private readonly Dictionary<string, int> _reads = new(StringComparer.Ordinal);
        private readonly ConcurrentQueue<string> _events = new();

        public ScriptedTransport(FakeL1011Aircraft ac) => _ac = ac;

        public IReadOnlyList<string> Events => _events.ToList();

        public void AfterWrite(string key, double value, Action act) => _after.Add((key, value, act));

        public void AfterRead(string key, int nth, Action act) => _afterRead.Add((key, nth, act));

        public async Task<double?> ReadFreshAsync(string key, int timeoutMs)
        {
            _events.Enqueue($"read {key}");
            var value = await _ac.ReadFreshAsync(key, timeoutMs).ConfigureAwait(false);
            int count = _reads[key] = _reads.GetValueOrDefault(key) + 1;
            int i = _afterRead.FindIndex(a => a.Key == key && a.Nth == count);
            if (i >= 0)
            {
                var act = _afterRead[i].Act;
                _afterRead.RemoveAt(i);
                act();
            }
            return value;
        }

        public bool Ready => _ac.Ready;
        public long NowMs => _ac.NowMs;

        public async Task<L1011FoResult> SetAsync(string key, double value)
        {
            _events.Enqueue($"write {key}={value.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            var result = await _ac.SetAsync(key, value).ConfigureAwait(false);
            int i = _after.FindIndex(a => a.Key == key && a.Value == value);
            if (i >= 0)
            {
                var act = _after[i].Act;
                _after.RemoveAt(i);
                act();
            }
            return result;
        }

        public Task<L1011FoResult> SetAltimetersAsync(double millibars) => _ac.SetAltimetersAsync(millibars);
        public Task<L1011FoResult> SetCabinBaroAsync(double millibars) => _ac.SetCabinBaroAsync(millibars);
        public Task<L1011FoResult> SetCabinAltitudeSelectorAsync(int step) => _ac.SetCabinAltitudeSelectorAsync(step);
        public bool CanReadBack(string key) => _ac.CanReadBack(key);
        public double? ReadCached(string key) => _ac.ReadCached(key);

        public void MuteLampSpeech(int ms)
        {
            _events.Enqueue($"mute {ms.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            _ac.MuteLampSpeech(ms);
        }

        public Task DelayAsync(int ms) => _ac.DelayAsync(ms);
    }

    [Fact]
    public void The_composites_are_pseudo_keys()
    {
        Assert.Equal("FO_STANDBY_POWER_CHECK", L1011FoActionExecutor.StandbyPowerCheck);
        Assert.Equal("FO_EXTERNAL_POWER", L1011FoActionExecutor.ExternalPower);
        Assert.True(L1011FoActionExecutor.IsPseudoKey(L1011FoActionExecutor.StandbyPowerCheck));
        Assert.True(L1011FoActionExecutor.IsPseudoKey(L1011FoActionExecutor.ExternalPower));
    }

    [Fact]
    public void Every_light_the_composites_read_is_a_registered_annunciator_lamp()
    {
        var lamps = L1011Annunciators.All.Select(L1011Annunciators.KeyFor).ToHashSet(StringComparer.Ordinal);
        foreach (var key in new[] { Unarmed, Flowbar, AcStandbyFail, DcStandbyFail, AcEssentialFail, DcEssentialFail, ExtPowerAvailable })
        {
            Assert.Contains(key, lamps);
            Assert.True(_ac.CanReadBack(key), key);   // a registered variable the definition reads
        }
    }

    // ---- The standby power check -----------------------------------------------------------------------------

    [Fact]
    public async Task The_standby_power_check_writes_in_the_checklists_order_reads_between_and_ends_armed()
    {
        SeedStandbyCheck();

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(CheckWrites, _ac.Writes);
        AssertEndsArmed();

        // Step 2 read between the standby On write and the battery On write; step 4 after the guard closes.
        var io = _ac.Io;
        int on = io.FindIndex(e => e is { Kind: FakeL1011IoKind.Write, Key: StandbyPower, Value: 2 });
        int batteryOn = io.FindIndex(e => e is { Kind: FakeL1011IoKind.Write, Key: Battery, Value: 1 });
        int guardClosed = io.FindIndex(e => e is { Kind: FakeL1011IoKind.Write, Key: StandbyGuard, Value: 0 });
        Assert.Equal(new[] { Flowbar, AcStandbyFail, DcStandbyFail, ExtPowerBreaker, AcEssentialFail, DcEssentialFail },
            io.Skip(on).Take(batteryOn - on).Where(e => e.Kind == FakeL1011IoKind.FreshRead && IsLampOrBreaker(e.Key)).Select(e => e.Key));
        Assert.Equal(new[] { Unarmed, Flowbar, AcStandbyFail, DcStandbyFail },
            io.Skip(guardClosed).Where(e => e.Kind == FakeL1011IoKind.FreshRead && IsLampOrBreaker(e.Key)).Select(e => e.Key));

        Assert.Equal(new[] { Pass }, _spoken);   // progress speech on until a flow attaches its setting
    }

    [Fact]
    public async Task The_standby_power_check_from_cold_and_dark_finds_the_battery_already_off()
    {
        SeedStandbyCheck();
        _ac.Set(Battery, 0);

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(CheckWrites.Skip(1), _ac.Writes);
        AssertEndsArmed();
    }

    [Fact]
    public async Task A_passing_check_is_silent_with_flow_progress_speech_off()
    {
        SeedStandbyCheck();
        _exec.AttachFlowProgressSpeech(() => false);

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Empty(_spoken);
        AssertEndsArmed();
    }

    [Fact]
    public async Task The_check_mutes_the_lamp_speech_before_its_first_read_or_write()
    {
        SeedStandbyCheck();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal("mute 20000", io.Events[0]);
        Assert.StartsWith("read ", io.Events[1]);   // the battery's read-first comes after it
    }

    [Fact]
    public async Task The_check_re_arms_the_mute_before_step_3_and_step_4_and_leaves_it_to_end_by_itself()
    {
        // On the 1 Hz batch the check can outlast one 20 s window, and a lamp change that begins after the
        // window is spoken. Each re-arm only ever moves the end later (L1011LampGate.MuteUntil keeps the max).
        SeedStandbyCheck();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        var events = io.Events.ToList();
        Assert.Equal(new[]
        {
            "mute 20000",
            "write TOGGLE_BATTERY=0", "write GUARD_STBY_POWER=1", "write TOGGLE_STBY_POWER=2",   // step 1
            "mute 20000",                                                                         // re-armed for step 3
            "write TOGGLE_BATTERY=1", "write TOGGLE_STBY_POWER=1", "write GUARD_STBY_POWER=0",   // step 3
            "mute 20000",                                                                         // re-armed for step 4
        }, events.Where(e => !e.StartsWith("read ", StringComparison.Ordinal)));
        int[] mutes = events.Select((e, i) => (e, i)).Where(x => x.e.StartsWith("mute ", StringComparison.Ordinal)).Select(x => x.i).ToArray();
        // Step 3's re-arm after step 2's light round; step 4's before its first light read.
        Assert.True(mutes[1] > events.LastIndexOf($"read {DcEssentialFail}"));
        Assert.True(mutes[2] < events.IndexOf($"read {Unarmed}"));
        Assert.Equal(new[] { 20_000, 20_000, 20_000 }, _ac.MuteRequests);   // never cut short
    }

    [Fact]
    public async Task A_flow_step_runs_the_check_by_its_pseudo_key()
    {
        SeedStandbyCheck();

        Assert.True(await _exec.ExecuteStepAsync(new Step { EventName = L1011FoActionExecutor.StandbyPowerCheck }));

        Assert.Equal(CheckWrites, _ac.Writes);
    }

    [Fact]
    public async Task The_essential_bus_fail_lights_are_not_expected_with_external_power_on_line()
    {
        SeedStandbyCheck();
        _ac.Set(ExtPowerBreaker, 1);   // the essential bus fail lights go out with external power on line

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.DoesNotContain(AcEssentialFail, _ac.FreshReads);
        Assert.DoesNotContain(DcEssentialFail, _ac.FreshReads);
    }

    [Fact]
    public async Task The_essential_bus_fail_lights_are_not_expected_while_the_breaker_is_unread()
    {
        SeedStandbyCheck();
        _ac.NeverDeliver(ExtPowerBreaker);
        _ac.Unset(ExtPowerBreaker);
        _ac.NeverDeliver(AcEssentialFail);
        _ac.Set(AcEssentialFail, 0);   // would fail the check were it expected lit

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { Pass }, _spoken);
    }

    public static TheoryData<string, double, string, bool> StepTwoWrongLights()
    {
        var data = new TheoryData<string, double, string, bool>();
        foreach (bool progress in new[] { true, false })
        {
            data.Add(Flowbar, 0, "Standby power check: standby power flowbar light did not come on", progress);
            data.Add(AcStandbyFail, 1, "Standby power check: AC standby bus fail light came on", progress);
            data.Add(DcStandbyFail, 1, "Standby power check: DC standby bus fail light came on", progress);
            data.Add(AcEssentialFail, 0, "Standby power check: AC essential bus fail light did not come on", progress);
            data.Add(DcEssentialFail, 0, "Standby power check: DC essential bus fail light did not come on", progress);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(StepTwoWrongLights))]
    public async Task A_wrong_light_with_standby_power_on_speaks_one_sentence_and_the_check_still_ends_armed(
        string lamp, double reading, string sentence, bool progress)
    {
        SeedStandbyCheck();
        _ac.NeverDeliver(lamp);
        _ac.Set(lamp, reading);   // the light stays wrong whatever the switches do
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { sentence }, _spoken);   // one sentence, even when step 4 finds it wrong again
        Assert.Equal(CheckWrites, _ac.Writes);       // carried on through step 3
        AssertEndsArmed();
    }

    public static TheoryData<string, double, string, bool> StepFourWrongLights()
    {
        var data = new TheoryData<string, double, string, bool>();
        foreach (bool progress in new[] { true, false })
        {
            data.Add(Unarmed, 1, "Standby power check: standby power unarmed light came on", progress);
            data.Add(Flowbar, 1, "Standby power check: standby power flowbar light did not go out", progress);
            data.Add(AcStandbyFail, 1, "Standby power check: AC standby bus fail light came on", progress);
            data.Add(DcStandbyFail, 1, "Standby power check: DC standby bus fail light came on", progress);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(StepFourWrongLights))]
    public async Task A_wrong_light_once_armed_speaks_one_sentence(string lamp, double reading, string sentence, bool progress)
    {
        SeedStandbyCheck();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(StandbyGuard, 0, () =>
        {
            _ac.NeverDeliver(lamp);
            _ac.Set(lamp, reading);
        });
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { sentence }, _spoken);
        Assert.Equal(CheckWrites, _ac.Writes);
        AssertEndsArmed();
    }

    [Fact]
    public async Task An_unread_light_speaks_one_sentence_naming_it_and_the_check_still_ends_armed()
    {
        SeedStandbyCheck();
        _ac.NeverDeliver(Flowbar);
        _ac.Unset(Flowbar);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { "Standby power check: standby power flowbar light could not be read" }, _spoken);
        Assert.Equal(CheckWrites, _ac.Writes);
        AssertEndsArmed();
    }

    [Fact]
    public async Task A_wrong_light_waits_for_the_lights_to_settle_within_the_verify_time_out()
    {
        SeedStandbyCheck();
        _ac.NeverDeliver(Flowbar);
        _ac.Set(Flowbar, 0);
        long on = -1, batteryOn = -1;
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(StandbyPower, 2, () => on = _ac.NowMs);
        io.AfterWrite(Battery, 1, () => batteryOn = _ac.NowMs);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        // Step 2 polled the flowbar until the verify time-out, on the transport's clock, then carried on.
        Assert.InRange(batteryOn - on, L1011FoActionExecutor.VerifyTimeoutMs,
            L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + 4 * L1011FoActionExecutor.ReadTimeoutMs);
        Assert.True(_ac.FreshReads.Count(k => k == Flowbar) > 2);   // polled, not read once
    }

    [Fact]
    public async Task A_light_that_settles_a_little_late_still_passes()
    {
        SeedStandbyCheck();
        _ac.NeverDeliver(Flowbar);
        _ac.Set(Flowbar, 0);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterRead(Flowbar, 2, () => _ac.Set(Flowbar, 1));   // lit from its third read on
        io.AfterWrite(StandbyGuard, 0, () => _ac.Set(Flowbar, 0));   // and out once armed

        Assert.True(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { Pass }, _spoken);
        Assert.Equal(CheckWrites, _ac.Writes);
    }

    [Fact]
    public async Task A_refused_battery_write_ends_the_check_at_once_restores_and_is_not_done()
    {
        SeedStandbyCheck();
        _ac.Refuse(Battery);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { (StandbyPower, 1.0) }, _ac.Writes);   // the guard never opened; standby armed
        Assert.DoesNotContain(_ac.FreshReads, IsLampOrBreaker);     // no light read
        AssertEndsArmed();
        Assert.Contains(_logged, line => line.Contains(Battery) && line.Contains(FakeL1011Aircraft.RefusalReason));
        Assert.Empty(_spoken);   // a write not done is the flow's to say ("Skipping")
    }

    [Fact]
    public async Task A_guard_that_will_not_open_puts_the_battery_back_on()
    {
        SeedStandbyCheck();
        _ac.Refuse(StandbyGuard);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { (Battery, 0.0), (Battery, 1.0), (StandbyPower, 1.0) }, _ac.Writes);
        AssertEndsArmed();
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_write_failing_after_a_wrong_light_still_speaks_the_light_and_ends_armed()
    {
        SeedStandbyCheck();
        _ac.NeverDeliver(Flowbar);
        _ac.Set(Flowbar, 0);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(StandbyPower, 2, () => _ac.Refuse(StandbyPower));   // standby will not go back to Arm

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1));

        Assert.Equal(new[] { "Standby power check: standby power flowbar light did not come on" }, _spoken);
        // Step 3 stops at the refused standby write; the put-back closes the guard, which arms it.
        Assert.Equal(new[] { (Battery, 0.0), (StandbyGuard, 1.0), (StandbyPower, 2.0), (Battery, 1.0), (StandbyGuard, 0.0) }, _ac.Writes);
        Assert.DoesNotContain(_ac.FreshReads, k => k == Unarmed);   // step 4 never ran
        AssertEndsArmed();
    }

    [Fact]
    public async Task A_write_that_throws_mid_check_still_puts_the_battery_back_on()
    {
        SeedStandbyCheck();
        var io = new HoldingTransport(_ac);
        _exec.SetTransport(io);
        io.Throw(StandbyGuard);

        Assert.False(await _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1).WaitAsync(Hang));

        Assert.Equal(new[] { (Battery, 0.0), (Battery, 1.0), (StandbyPower, 1.0) }, _ac.Writes);
        AssertEndsArmed();
        Assert.Contains(_logged, line => line.Contains("threw"));
        Assert.True(await _exec.Set(ContinuousIgnition, 1).WaitAsync(Hang));   // the gate was freed
    }

    [Fact]
    public async Task The_whole_check_runs_under_the_one_gate()
    {
        SeedStandbyCheck();
        _ac.Set(ContinuousIgnition, 0);
        var io = new HoldingTransport(_ac) { TimeStandsStill = true };
        _exec.SetTransport(io);
        var door = io.Hold(StandbyGuard);

        var check = _exec.Set(L1011FoActionExecutor.StandbyPowerCheck, 1);
        var other = _exec.Set(ContinuousIgnition, 1);

        Assert.Equal(new[] { Battery, StandbyGuard }, io.Started);   // the check is in flight
        Assert.False(other.IsCompleted);
        Assert.DoesNotContain(_ac.Io, e => e.Key == ContinuousIgnition);

        door.SetResult();
        Assert.Equal(new[] { true, true }, await Task.WhenAll(check, other).WaitAsync(Hang));

        int lastOfCheck = _ac.Io.FindLastIndex(e => e.Key != ContinuousIgnition);
        int firstOfOther = _ac.Io.FindIndex(e => e.Key == ContinuousIgnition);
        Assert.True(firstOfOther > lastOfCheck);   // the other write only after the whole check, step 4 included
    }

    // ---- External power ---------------------------------------------------------------------------------------

    private void SeedExternalPower(double available)
    {
        _ac.Set(ExtPowerAvailable, available);
        _ac.Set(ExtPower, 0);
        _ac.Set(ExtPowerBreaker, 0);
        foreach (int n in new[] { 1, 2, 3 })
            _ac.Set(Tie(n), 1);
    }

    [Fact]
    public async Task External_power_is_refused_with_its_available_light_dark()
    {
        SeedExternalPower(available: 0);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(ExtPowerAvailable));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task External_power_is_refused_with_its_available_light_unread()
    {
        SeedExternalPower(available: 0);
        _ac.Unset(ExtPowerAvailable);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(ExtPowerAvailable));
    }

    [Fact]
    public async Task External_power_on_closes_its_breaker_then_presses_only_the_open_ties()
    {
        SeedExternalPower(available: 1);
        _ac.Set(Tie(1), 0);
        _ac.Set(Tie(3), 0);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0), (TieClose(1), 1.0), (TieClose(3), 1.0) }, _ac.Writes);
        Assert.Equal(1, _ac.Get(ExtPowerBreaker));
        foreach (int n in new[] { 1, 2, 3 })
            Assert.Equal(1, _ac.Get(Tie(n)));
        // The breaker verified before any tie was read.
        int breakerRead = _ac.Io.FindLastIndex(e => e.Kind == FakeL1011IoKind.FreshRead && e.Key == ExtPowerBreaker);
        int firstTieRead = _ac.Io.FindIndex(e => e.Kind == FakeL1011IoKind.FreshRead && e.Key.StartsWith("L1011_RO_AC_TIE_", StringComparison.Ordinal));
        Assert.True(breakerRead < firstTieRead);
        Assert.Empty(_spoken);   // a pass is the flow's line alone
    }

    [Fact]
    public async Task External_power_on_with_every_tie_closed_presses_no_tie()
    {
        SeedExternalPower(available: 1);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0) }, _ac.Writes);
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task External_power_whose_breaker_never_closes_is_not_done_and_presses_no_tie()
    {
        SeedExternalPower(available: 1);
        _ac.Set(Tie(1), 0);
        _ac.NeverDeliver(ExtPowerBreaker);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains(ExtPowerBreaker));
        Assert.Empty(_spoken);   // not on: the flow's "Skipping" says it
    }

    // A tie left open once the breaker reads closed: external power IS on (the step's line is true), so the
    // step is done, and the open tie is a failure of its own, spoken once whatever the progress setting.

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_tie_that_does_not_close_is_spoken_once_the_next_tie_is_still_pressed_and_external_power_is_on(bool progress)
    {
        SeedExternalPower(available: 1);
        foreach (int n in new[] { 1, 2, 3 })
            _ac.Set(Tie(n), 0);
        _ac.NeverDeliver(Tie(2));
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0), (TieClose(1), 1.0), (TieClose(2), 1.0), (TieClose(3), 1.0) }, _ac.Writes);
        Assert.Equal(new[] { "External power on. AC tie 2 did not close." }, _spoken);
        Assert.Contains(_logged, line => line.Contains(Tie(2)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unread_tie_is_never_pressed_spoken_once_and_external_power_is_on(bool progress)
    {
        SeedExternalPower(available: 1);
        _ac.Unset(Tie(2));
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0) }, _ac.Writes);
        Assert.Equal(new[] { "External power on. AC tie 2 could not be read." }, _spoken);
        Assert.Contains(_logged, line => line.Contains(Tie(2)));
    }

    [Fact]
    public async Task Only_the_first_tie_that_failed_is_spoken_and_the_others_are_logged()
    {
        SeedExternalPower(available: 1);
        _ac.Unset(Tie(1));             // unread: never pressed
        _ac.Set(Tie(3), 0);
        _ac.NeverDeliver(Tie(3));      // pressed, never closes

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0), (TieClose(3), 1.0) }, _ac.Writes);
        Assert.Equal(new[] { "External power on. AC tie 1 could not be read." }, _spoken);
        Assert.Contains(_logged, line => line.Contains(Tie(1)));
        Assert.Contains(_logged, line => line.Contains(Tie(3)));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_tie_sentence_is_invariant_under_a_comma_decimal_culture(string culture)
    {
        SeedExternalPower(available: 1);
        _ac.Set(Tie(2), 0);
        _ac.NeverDeliver(Tie(2));
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);

            Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(new[] { "External power on. AC tie 2 did not close." }, _spoken);
    }

    [Fact]
    public async Task External_power_off_opens_its_breaker_and_leaves_the_ties()
    {
        SeedExternalPower(available: 1);
        _ac.Set(ExtPower, 1);
        _ac.Set(ExtPowerBreaker, 1);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 0));

        Assert.Equal(new[] { (ExtPower, 0.0) }, _ac.Writes);
        Assert.Equal(0, _ac.Get(ExtPowerBreaker));
        Assert.DoesNotContain(_ac.Io, e => e.Key.StartsWith("L1011_RO_AC_TIE_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task External_power_off_whose_breaker_stays_closed_is_not_done()
    {
        SeedExternalPower(available: 1);
        _ac.Set(ExtPower, 1);
        _ac.Set(ExtPowerBreaker, 1);
        _ac.NeverDeliver(ExtPowerBreaker);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ExternalPower, 0));

        Assert.Equal(new[] { (ExtPower, 0.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains(ExtPowerBreaker));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task External_power_to_any_other_target_writes_nothing(int target)
    {
        SeedExternalPower(available: 1);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ExternalPower, target));

        Assert.Empty(_ac.Io);
    }
}
