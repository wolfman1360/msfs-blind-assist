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
}
