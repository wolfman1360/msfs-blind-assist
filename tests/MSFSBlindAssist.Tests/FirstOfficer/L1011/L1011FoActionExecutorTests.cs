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

    // ================= Part 2 foundations ===========================================================================

    // ---- The flow's Stop and Pause --------------------------------------------------------------------------------

    [Fact]
    public async Task A_flow_step_takes_the_stop_token_and_a_plain_switch_ignores_it()
    {
        _ac.Set(ContinuousIgnition, 0);
        foreach (var key in Galley)
            _ac.Set(key, 1);
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.True(await _exec.ExecuteStepAsync(new Step { EventName = ContinuousIgnition, TargetValue = 1 }, stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(new Step
        {
            ActionType = FlowStepActionType.SetSwitchMultiple,
            MultiActions = Galley.Select(k => (k, (int?)0)).ToArray(),
        }, stop.Token));

        Assert.Equal(new[] { (ContinuousIgnition, 1.0) }.Concat(Galley.Select(k => (k, 0.0))), _ac.Writes);
        Assert.Equal(1, _ac.Get(ContinuousIgnition));   // read back as any write
    }

    [Fact]
    public async Task WaitUntil_is_true_once_satisfied_and_false_at_its_limit()
    {
        int asked = 0;
        Assert.True(await _exec.WaitUntilAsync(_ac, () => Task.FromResult(++asked == 3), 10_000, CancellationToken.None, "a wait"));
        Assert.Equal(3, asked);
        Assert.Equal(2 * L1011FoActionExecutor.PollMs, _ac.NowMs);   // polled, on the transport's clock

        long start = _ac.NowMs;
        Assert.False(await _exec.WaitUntilAsync(_ac, () => Task.FromResult(false), 1_000, CancellationToken.None, "a long wait"));
        Assert.InRange(_ac.NowMs - start, 1_000, 1_000 + L1011FoActionExecutor.PollMs);
        Assert.Contains(_logged, line => line.StartsWith("a long wait", StringComparison.Ordinal) && line.Contains("1000 ms"));
        Assert.Empty(_ac.Io);   // read-only: it writes nothing of its own
    }

    [Fact]
    public async Task WaitUntil_returns_false_on_stop_within_a_poll_and_counts_no_paused_time()
    {
        // Already stopped: false at once, nothing asked.
        using (var stopped = new CancellationTokenSource())
        {
            stopped.Cancel();
            int never = 0;
            Assert.False(await _exec.WaitUntilAsync(_ac, () => Task.FromResult(++never > 0), 60_000, stopped.Token, "a stopped wait"));
            Assert.Equal(0, never);
            Assert.Contains("a stopped wait: stopped", _logged);
        }

        // A Stop arriving while it polls ends it within one poll.
        using (var stop = new CancellationTokenSource())
        {
            int asked = 0;
            long stoppedAt = -1;
            bool done = await _exec.WaitUntilAsync(_ac, () =>
            {
                if (++asked == 3)
                {
                    stop.Cancel();
                    stoppedAt = _ac.NowMs;
                }
                return Task.FromResult(false);
            }, 60_000, stop.Token, "an engine wait");

            Assert.False(done);
            Assert.Equal(3, asked);   // never asked again after the Stop
            Assert.InRange(_ac.NowMs - stoppedAt, 0, L1011FoActionExecutor.PollMs);
            Assert.Contains("an engine wait: stopped", _logged);
        }

        // Paused from the start (a flow step: its token can be cancelled): the minute the pause lasts on the
        // transport's clock is not counted, and nothing is asked while paused.
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => resume.Task.WaitAsync(ct));
        using var flow = new CancellationTokenSource();
        int pausedAsks = 0;
        long pausedFrom = _ac.NowMs;
        var paused = _exec.WaitUntilAsync(_ac, () => Task.FromResult(++pausedAsks < 0), 1_000, flow.Token, "a paused wait");
        Assert.False(paused.IsCompleted);
        Assert.Equal(0, pausedAsks);
        await _ac.DelayAsync(60_000);
        resume.SetResult();

        Assert.False(await paused.WaitAsync(Hang));
        Assert.True(pausedAsks > 1);
        Assert.InRange(_ac.NowMs - pausedFrom, 61_000, 61_000 + L1011FoActionExecutor.PollMs);   // the limit runs from the resume

        // A Stop while paused ends it at once.
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => held.Task.WaitAsync(ct));
        using var stopWhilePaused = new CancellationTokenSource();
        var pausedThenStopped = _exec.WaitUntilAsync(_ac, () => Task.FromResult(true), 60_000, stopWhilePaused.Token, "a paused, stopped wait");
        Assert.False(pausedThenStopped.IsCompleted);
        stopWhilePaused.Cancel();

        Assert.False(await pausedThenStopped.WaitAsync(Hang));
        Assert.Contains("a paused, stopped wait: stopped", _logged);

        // Ruling R23: a hand tick (CancellationToken.None) never waits on a flow's Pause, held or not.
        int tickAsks = 0;
        var tick = _exec.WaitUntilAsync(_ac, () => Task.FromResult(++tickAsks == 2), 10_000, CancellationToken.None, "a hand tick's wait");
        Assert.True(tick.IsCompleted);
        Assert.True(await tick);
        Assert.Equal(2, tickAsks);
    }

    // ---- Levers read back on their own scales ---------------------------------------------------------------------

    private const string Flaps = L1011Levers.FlapHandleKey;
    private const string Gear = L1011Levers.GearLeverKey;
    private const string SpeedBrake = L1011Levers.SpeedBrakeKey;
    private const string StabTrim = L1011Levers.StabTrimKey;
    private const string ElevatorTrim = "L1011_RO_ELEVATOR_TRIM";

    [Theory]
    [InlineData(Flaps, 3.0, 3, true)]
    [InlineData(Flaps, 3.4, 3, true)]
    [InlineData(Flaps, 2.0, 3, false)]
    [InlineData(Flaps, 4.0, 3, false)]
    [InlineData(Gear, 100.0, 100, true)]
    [InlineData(Gear, 80.0, 100, true)]     // past the up band's edge: up
    [InlineData(Gear, 70.0, 100, false)]
    [InlineData(Gear, 50.0, 100, false)]
    [InlineData(Gear, 20.0, 0, true)]
    [InlineData(Gear, 30.0, 0, false)]
    [InlineData(Gear, 60.0, 50, true)]
    [InlineData(SpeedBrake, 5.0, 0, true)]   // forward reads below 5
    [InlineData(SpeedBrake, 5.5, 0, false)]
    [InlineData(SpeedBrake, 96.0, 100, true)]
    [InlineData(SpeedBrake, 44.0, 50, false)]
    [InlineData(L1011Levers.ParkingBrakeKey, 0.4, 0, true)]
    [InlineData(L1011Levers.ParkingBrakeKey, 0.5, 0, false)]
    [InlineData(L1011Levers.GroundSpoilersKey, 1.0, 1, true)]
    [InlineData(L1011Levers.GroundSpoilersKey, 0.5, 1, false)]
    [InlineData(StabTrim, 0.19, 0, true)]
    [InlineData(StabTrim, -0.19, 0, true)]
    [InlineData(StabTrim, 0.2, 0, false)]
    [InlineData(ContinuousIgnition, 1.4, 1, true)]   // any other key: a position within half a step
    [InlineData(ContinuousIgnition, 1.5, 1, false)]
    public void A_reading_meets_its_target_on_the_controls_own_scale(string key, double read, double target, bool meets)
    {
        Assert.Equal(meets, L1011FoActionExecutor.LeverReadsTarget(key, read, target));
    }

    [Theory]
    [InlineData(3.0, true)]
    [InlineData(2.0, false)]   // still at the old detent
    public async Task A_flap_handle_write_reads_back_by_detent(double reads, bool done)
    {
        _ac.Set(Flaps, 2);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(Flaps, 3, () =>
        {
            _ac.NeverDeliver(Flaps);
            _ac.Set(Flaps, reads);
        });

        Assert.Equal(done, await _exec.Set(Flaps, 3));

        Assert.Equal(new[] { (Flaps, 3.0) }, _ac.Writes);
        Assert.Equal(new[] { $"read {Flaps}", $"write {Flaps}=3", $"read {Flaps}" }, io.Events.Take(3));   // read first, written, read back
    }

    [Theory]
    [InlineData(100.0, true)]
    [InlineData(90.0, true)]    // travelling, already in the up band
    [InlineData(50.0, false)]   // held at off
    public async Task A_gear_lever_write_reads_back_by_band(double reads, bool done)
    {
        _ac.Set(Gear, 0);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(Gear, 100, () =>
        {
            _ac.NeverDeliver(Gear);
            _ac.Set(Gear, reads);
        });

        Assert.Equal(done, await _exec.Set(Gear, 100));

        Assert.Equal(new[] { (Gear, 100.0) }, _ac.Writes);
        if (!done)
        {
            Assert.InRange(_ac.NowMs, L1011FoActionExecutor.VerifyTimeoutMs,
                L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + L1011FoActionExecutor.ReadTimeoutMs);
            Assert.Contains(_logged, line => line.Contains(Gear));
        }
    }

    [Theory]
    [InlineData(0, 0.0, true)]
    [InlineData(0, 4.0, true)]
    [InlineData(0, 6.0, false)]
    [InlineData(50, 46.0, true)]
    [InlineData(50, 44.0, false)]
    public async Task A_speed_brake_write_tolerates_five_percent(int target, double reads, bool done)
    {
        _ac.Set(SpeedBrake, target == 0 ? 50 : 0);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(SpeedBrake, target, () =>
        {
            _ac.NeverDeliver(SpeedBrake);
            _ac.Set(SpeedBrake, reads);
        });

        Assert.Equal(done, await _exec.Set(SpeedBrake, target));

        Assert.Equal(new[] { (SpeedBrake, (double)target) }, _ac.Writes);
    }

    [Theory]
    [InlineData(0.1, true)]
    [InlineData(-0.15, true)]
    [InlineData(0.25, false)]
    public async Task A_stab_trim_write_is_verified_on_the_trim_readout(double reads, bool done)
    {
        _ac.Set(ElevatorTrim, 2.38);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(StabTrim, 0, () =>   // the trim moves the readout ELEVATOR TRIM POSITION reads
        {
            _ac.NeverDeliver(ElevatorTrim);
            _ac.Set(ElevatorTrim, reads);
        });

        Assert.Equal(done, await _exec.Set(StabTrim, 0));

        Assert.Equal(new[] { (StabTrim, 0.0) }, _ac.Writes);
        Assert.DoesNotContain(StabTrim, _ac.FreshReads);   // the typed key is write-only: never read first
        Assert.Contains(ElevatorTrim, _ac.FreshReads);
    }

    [Fact]
    public async Task A_stab_trim_whose_readout_never_moves_is_not_done_after_the_verify_time_out()
    {
        _ac.NeverDeliver(ElevatorTrim);
        _ac.Set(ElevatorTrim, 2.38);

        Assert.False(await _exec.Set(StabTrim, 0));

        Assert.Equal(new[] { (StabTrim, 0.0) }, _ac.Writes);   // sent once
        Assert.InRange(_ac.NowMs, L1011FoActionExecutor.VerifyTimeoutMs,
            L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + L1011FoActionExecutor.ReadTimeoutMs);
        Assert.Contains(_logged, line => line.Contains(ElevatorTrim));
        Assert.Empty(_spoken);
    }

    // ---- Relay presses (ruling R21: 8 s on the relay, never the press button's own state) ------------------------

    private const string GenBreakerClose = "SWITCH_GEN_FLOW_1";
    private const string GenBreakerTrip = "SWITCH_GEN_FLOW_OPEN_1";
    private const string GenBreaker = "L1011_RO_GEN_1_BREAKER";
    private const string GenFieldClose = "SWITCH_BRG_GEN_1";
    private const string GenField = "L1011_RO_GEN_1_FIELD";

    [Fact]
    public async Task A_relay_closing_six_seconds_after_its_press_is_done()
    {
        _ac.Set(GenBreaker, 0);
        long pressedAt = -1;
        var io = new ScriptedTransport(_ac);
        io.AfterWrite(GenBreakerClose, 1, () => pressedAt = _ac.NowMs);
        _ac.OnClock = ac =>
        {
            if (pressedAt >= 0 && ac.NowMs >= pressedAt + 6_000)
                ac.Set(GenBreaker, 1);
        };

        Assert.True(await _exec.PressUntilRelayAsync(io, GenBreakerClose, GenBreaker, closed: true, "generator 1 breaker"));

        Assert.Equal(new[] { (GenBreakerClose, 1.0) }, _ac.Writes);   // pressed once
        Assert.InRange(_ac.NowMs - pressedAt, 6_000, 6_000 + L1011FoActionExecutor.PollMs);
        Assert.Equal(8_000, L1011FoActionExecutor.RelayVerifyTimeoutMs);
    }

    [Fact]
    public async Task A_relay_that_never_closes_is_not_done_after_eight_seconds()
    {
        _ac.Set(GenBreaker, 0);
        long pressedAt = -1;
        var io = new ScriptedTransport(_ac);
        io.AfterWrite(GenBreakerClose, 1, () => pressedAt = _ac.NowMs);

        Assert.False(await _exec.PressUntilRelayAsync(io, GenBreakerClose, GenBreaker, closed: true, "generator 1 breaker"));

        Assert.Equal(new[] { (GenBreakerClose, 1.0) }, _ac.Writes);   // never pressed again
        Assert.InRange(_ac.NowMs - pressedAt, L1011FoActionExecutor.RelayVerifyTimeoutMs,
            L1011FoActionExecutor.RelayVerifyTimeoutMs + L1011FoActionExecutor.PollMs + L1011FoActionExecutor.ReadTimeoutMs);
        Assert.Contains(_logged, line => line.Contains(GenBreaker) && line.Contains("8000 ms"));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_relay_trip_reads_its_relay_open()
    {
        _ac.Set(GenBreaker, 1);
        var io = new ScriptedTransport(_ac);
        io.AfterWrite(GenBreakerTrip, 1, () => _ac.Set(GenBreaker, 0));

        Assert.True(await _exec.PressUntilRelayAsync(io, GenBreakerTrip, GenBreaker, closed: false, "generator 1 breaker trip"));

        Assert.Equal(new[] { (GenBreakerTrip, 1.0) }, _ac.Writes);
    }

    [Theory]
    [InlineData(1.0, true)]    // an engine generator's field relay closes by itself once its engine runs
    [InlineData(0.0, false)]
    public async Task A_relay_already_as_wanted_is_done_with_no_press(double reading, bool closed)
    {
        _ac.Set(GenField, reading);

        Assert.True(await _exec.PressUntilRelayAsync(_ac, GenFieldClose, GenField, closed, "generator 1 field"));

        Assert.Empty(_ac.Writes);
        Assert.Equal(new[] { GenField }, _ac.FreshReads);   // read once, fresh
    }

    [Fact]
    public async Task A_relay_that_cannot_be_read_is_never_pressed()
    {
        Assert.False(await _exec.PressUntilRelayAsync(_ac, GenFieldClose, GenField, closed: true, "generator 1 field"));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(GenField));
        Assert.Empty(_spoken);
    }

    // ---- INS align, per unit --------------------------------------------------------------------------------------

    private static string InsMode(int n) => $"ROTARY_INS_MODE_{n}";

    [Fact]
    public void The_ins_align_and_ac_ties_composites_are_pseudo_keys()
    {
        Assert.Equal("FO_INS_ALIGN", L1011FoActionExecutor.InsAlign);
        Assert.Equal("FO_AC_TIES", L1011FoActionExecutor.AcTies);
        Assert.True(L1011FoActionExecutor.IsPseudoKey(L1011FoActionExecutor.InsAlign));
        Assert.True(L1011FoActionExecutor.IsPseudoKey(L1011FoActionExecutor.AcTies));
    }

    [Theory]
    [InlineData(0, 2, 3, new[] { 1 })]          // off: aligned; align and nav left alone
    [InlineData(4, 1, 2, new[] { 1, 2 })]       // attitude and standby: aligned
    [InlineData(2, 3, 3, new int[0])]           // every unit at align or nav: nothing written
    public async Task Ins_align_writes_only_units_at_off_standby_or_attitude(double mode1, double mode2, double mode3, int[] written)
    {
        double[] modes = { mode1, mode2, mode3 };
        for (int n = 1; n <= 3; n++)
            _ac.Set(InsMode(n), modes[n - 1]);

        Assert.True(await _exec.Set(L1011FoActionExecutor.InsAlign, 1));

        Assert.Equal(written.Select(n => (InsMode(n), 2.0)), _ac.Writes);
        for (int n = 1; n <= 3; n++)
            Assert.Equal(written.Contains(n) ? 2 : modes[n - 1], _ac.Get(InsMode(n)));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Ins_align_never_writes_an_unread_unit_and_still_aligns_the_others()
    {
        _ac.Set(InsMode(1), 0);
        _ac.Set(InsMode(3), 1);   // unit 2 unread

        Assert.False(await _exec.Set(L1011FoActionExecutor.InsAlign, 1));

        Assert.Equal(new[] { (InsMode(1), 2.0), (InsMode(3), 2.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains(InsMode(2)));
        Assert.Empty(_spoken);   // not done is the flow's to say
    }

    [Fact]
    public async Task Ins_align_with_a_unit_that_does_not_take_its_write_is_not_done()
    {
        for (int n = 1; n <= 3; n++)
            _ac.Set(InsMode(n), 0);
        _ac.NeverDeliver(InsMode(2));

        Assert.False(await _exec.Set(L1011FoActionExecutor.InsAlign, 1));

        Assert.Equal(new[] { 1, 2, 3 }.Select(n => (InsMode(n), 2.0)), _ac.Writes);   // the next unit still written
    }

    // ---- AC ties ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Ac_ties_presses_only_open_ties_and_is_done_when_all_read_closed()
    {
        _ac.Set(ExtPowerBreaker, 1);   // a source on line: a press closes its tie
        _ac.Set(Tie(1), 0);
        _ac.Set(Tie(2), 1);
        _ac.Set(Tie(3), 0);

        Assert.True(await _exec.Set(L1011FoActionExecutor.AcTies, 1));

        Assert.Equal(new[] { (TieClose(1), 1.0), (TieClose(3), 1.0) }, _ac.Writes);
        foreach (int n in new[] { 1, 2, 3 })
            Assert.Equal(1, _ac.Get(Tie(n)));
        Assert.Empty(_spoken);

        // All three closed now: nothing more pressed, still done.
        Assert.True(await _exec.Set(L1011FoActionExecutor.AcTies, 1));
        Assert.Equal(2, _ac.Writes.Count);
    }

    [Fact]
    public async Task Ac_ties_with_a_tie_that_does_not_close_is_not_done_and_still_presses_the_next()
    {
        _ac.Set(ExtPowerBreaker, 1);
        foreach (int n in new[] { 1, 2, 3 })
            _ac.Set(Tie(n), 0);
        _ac.NeverDeliver(Tie(2));

        Assert.False(await _exec.Set(L1011FoActionExecutor.AcTies, 1));

        Assert.Equal(new[] { (TieClose(1), 1.0), (TieClose(2), 1.0), (TieClose(3), 1.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains(Tie(2)));
        Assert.Empty(_spoken);   // unlike external power, the composite speaks nothing: the flow says it
    }

    [Fact]
    public async Task Ac_ties_never_presses_an_unread_tie()
    {
        _ac.Set(ExtPowerBreaker, 1);
        _ac.Set(Tie(1), 0);
        _ac.Set(Tie(3), 1);   // tie 2 unread

        Assert.False(await _exec.Set(L1011FoActionExecutor.AcTies, 1));

        Assert.Equal(new[] { (TieClose(1), 1.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains(Tie(2)));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Ac_ties_to_any_other_target_writes_nothing(int target)
    {
        _ac.Set(ExtPowerBreaker, 1);
        foreach (int n in new[] { 1, 2, 3 })
            _ac.Set(Tie(n), 0);

        Assert.False(await _exec.Set(L1011FoActionExecutor.AcTies, target));

        Assert.Empty(_ac.Io);
    }

    // Ruling R22: an AC tie is a relay a press drives, read back for the relay's 8 s ([R21]), in the AC ties
    // composite and in external power's tie loop alike.

    /// <summary>Tie 1 open with nothing on line, so its press closes nothing by itself; the script closes it
    /// six seconds after the press. Returns the press time's holder.</summary>
    private Func<long> TieOneClosesSixSecondsAfterItsPress(ScriptedTransport io)
    {
        long pressedAt = -1;
        io.AfterWrite(TieClose(1), 1, () => pressedAt = _ac.NowMs);
        _ac.OnClock = ac =>
        {
            if (pressedAt >= 0 && ac.NowMs >= pressedAt + 6_000)
                ac.Set(Tie(1), 1);
        };
        return () => pressedAt;
    }

    [Fact]
    public async Task Ac_ties_counts_a_tie_closing_six_seconds_after_its_press_as_done()
    {
        _ac.Set(ExtPowerBreaker, 0);
        _ac.Set(Tie(1), 0);
        _ac.Set(Tie(2), 1);
        _ac.Set(Tie(3), 1);
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        var pressedAt = TieOneClosesSixSecondsAfterItsPress(io);

        Assert.True(await _exec.Set(L1011FoActionExecutor.AcTies, 1));

        Assert.Equal(new[] { (TieClose(1), 1.0) }, _ac.Writes);   // pressed once
        Assert.InRange(_ac.NowMs - pressedAt(), 6_000, 6_000 + L1011FoActionExecutor.PollMs);
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task External_power_counts_a_tie_closing_six_seconds_after_its_press_as_closed()
    {
        SeedExternalPower(available: 1);
        _ac.Set(Tie(1), 0);
        _ac.NeverDeliver(Tie(1));   // the press closes nothing: the script does, late
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        var pressedAt = TieOneClosesSixSecondsAfterItsPress(io);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ExternalPower, 1));

        Assert.Equal(new[] { (ExtPower, 1.0), (TieClose(1), 1.0) }, _ac.Writes);
        Assert.InRange(_ac.NowMs - pressedAt(), 6_000, 6_000 + L1011FoActionExecutor.PollMs);
        Assert.Empty(_spoken);   // the tie closed: no failure to speak
    }

    // ================= The APU ==========================================================================================

    private const string ApuMaster = "TOGGLE_APU_POWER";
    private const string ApuStartPress = "SWITCH_APU_START";
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuGenFieldPress = "SWITCH_APU_BRG";
    private const string ApuGenBreakerPress = "SWITCH_GND_FLOW";
    private const string ApuGenBreakerTrip = "SWITCH_GND_FLOW_OPEN";
    private const string ApuGenField = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";
    private const string MasterNotOn = "APU not started: the master switch does not read on";

    private void SeedApu(double rpm, double master = 1)
    {
        _ac.Set(ApuMaster, master);
        _ac.Set(ApuRpm, rpm);
    }

    [Fact]
    public void The_apu_composites_are_pseudo_keys_on_the_aircrafts_own_keys()
    {
        Assert.Equal("FO_APU_START", L1011FoActionExecutor.ApuStart);
        Assert.Equal("FO_APU_GENERATOR", L1011FoActionExecutor.ApuGenerator);
        Assert.True(L1011FoActionExecutor.IsPseudoKey(L1011FoActionExecutor.ApuStart));
        Assert.True(L1011FoActionExecutor.IsPseudoKey(L1011FoActionExecutor.ApuGenerator));

        Assert.Equal(ApuMaster, L1011FoActionExecutor.ApuMaster);
        Assert.Equal(ApuStartPress, L1011FoActionExecutor.ApuStartPress);
        Assert.Equal(ApuRpm, L1011FoActionExecutor.ApuRpm);
        Assert.Equal(ApuGenFieldPress, L1011FoActionExecutor.ApuGenFieldPress);
        Assert.Equal(ApuGenBreakerPress, L1011FoActionExecutor.ApuGenBreakerPress);
        Assert.Equal(ApuGenBreakerTrip, L1011FoActionExecutor.ApuGenBreakerTrip);
        Assert.Equal(ApuGenField, L1011FoActionExecutor.ApuGenFieldRelay);
        Assert.Equal(ApuGenBreaker, L1011FoActionExecutor.ApuGenBreakerRelay);
        Assert.Equal(20_000, L1011FoActionExecutor.ApuSpinUpWaitMs);

        // The presses are the definition's own rows; the master, the RPM and the relays read fresh.
        foreach (var press in new[] { ApuStartPress, ApuGenFieldPress, ApuGenBreakerPress, ApuGenBreakerTrip })
            Assert.False(_ac.CanReadBack(press), press);
        foreach (var read in new[] { ApuMaster, ApuRpm, ApuGenField, ApuGenBreaker })
            Assert.True(_ac.CanReadBack(read), read);
    }

    // ---- The APU start ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Apu_start_presses_once_and_is_done_when_rpm_rises()
    {
        SeedApu(rpm: 0);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuStart, 1));

        Assert.Equal(new[] { (ApuStartPress, 1.0) }, _ac.Writes);
        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, ApuRpm),      // the RPM first
            (FakeL1011IoKind.FreshRead, ApuMaster),   // then the master
            (FakeL1011IoKind.Write, ApuStartPress),   // one press
            (FakeL1011IoKind.FreshRead, ApuRpm),      // then the RPM until it rises
        }, IoKinds());
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(3.0, 1)]
    [InlineData(100.0, 1)]
    [InlineData(3.0, 0)]   // starting with the master since turned off: still never a second press
    public async Task Apu_start_is_already_done_at_or_above_one_percent(double rpm, double master)
    {
        SeedApu(rpm, master);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuStart, 1));

        Assert.Empty(_ac.Writes);
        Assert.Equal(new[] { ApuRpm }, _ac.FreshReads);
    }

    [Theory]
    [InlineData(false)]   // the master off
    [InlineData(true)]    // the master unread
    public async Task Apu_start_refuses_with_the_master_off(bool unread)
    {
        SeedApu(rpm: 0, master: 0);
        if (unread)
            _ac.Unset(ApuMaster);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuStart, 1));

        Assert.Empty(_ac.Writes);
        Assert.Contains(MasterNotOn, _logged);
        Assert.Empty(_spoken);   // not done is the flow's to say
    }

    [Fact]
    public async Task Apu_start_with_its_rpm_unread_is_not_done_and_presses_nothing()
    {
        _ac.Set(ApuMaster, 1);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuStart, 1));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(ApuRpm));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Apu_start_waits_through_the_inlet_doors()
    {
        // Live: the RPM read 0 for about 7 s while the inlet doors opened, then rose about 3 %/s.
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;
        long pressedAt = -1;
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(ApuStartPress, 1, () => pressedAt = _ac.NowMs);
        _ac.OnClock = ac =>
        {
            if (pressedAt >= 0 && ac.NowMs >= pressedAt + 7_000)
                ac.Set(ApuRpm, 5);
        };

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuStart, 1));

        Assert.Equal(new[] { (ApuStartPress, 1.0) }, _ac.Writes);   // pressed once, never again while it waited
        Assert.InRange(_ac.NowMs - pressedAt, 7_000, 7_000 + L1011FoActionExecutor.PollMs);
        Assert.Empty(_spoken);
    }

    private static Step ApuStartStep => new() { EventName = L1011FoActionExecutor.ApuStart, TargetValue = 1 };

    [Theory]
    [InlineData("never rises")]
    [InlineData("stopped while waiting")]
    public async Task Apu_start_gives_up_after_twenty_seconds_or_on_stop(string how)
    {
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;   // the RPM stays 0
        long pressedAt = -1, stoppedAt = -1;
        using var stop = new CancellationTokenSource();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(ApuStartPress, 1, () => pressedAt = _ac.NowMs);
        if (how == "stopped while waiting")
        {
            _ac.OnClock = ac =>
            {
                if (pressedAt >= 0 && stoppedAt < 0 && ac.NowMs >= pressedAt + 3_000)
                {
                    stop.Cancel();
                    stoppedAt = ac.NowMs;
                }
            };
        }

        // A flow step: the composite takes the flow's Stop.
        Assert.False(await _exec.ExecuteStepAsync(ApuStartStep, stop.Token));

        Assert.Equal(new[] { (ApuStartPress, 1.0) }, _ac.Writes);   // one press, never a second
        if (how == "never rises")
        {
            Assert.InRange(_ac.NowMs - pressedAt, L1011FoActionExecutor.ApuSpinUpWaitMs,
                L1011FoActionExecutor.ApuSpinUpWaitMs + L1011FoActionExecutor.PollMs);
            Assert.Contains(_logged, line => line.Contains("20000 ms"));
        }
        else
        {
            Assert.InRange(_ac.NowMs - stoppedAt, 0, L1011FoActionExecutor.PollMs);   // within one poll
            Assert.Contains(_logged, line => line.EndsWith(": stopped", StringComparison.Ordinal));
        }
        Assert.Empty(_spoken);
    }

    // ---- Ruling R23: no wait holds the gate; the Pause is a flow step's; no write after the Stop -------------------

    [Theory]
    [InlineData("before the step")]          // the Stop already pressed when the step runs
    [InlineData("after the master read")]    // the Stop pressed while the start decides
    public async Task A_stop_before_the_press_presses_nothing(string when)
    {
        SeedApu(rpm: 0);
        using var stop = new CancellationTokenSource();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        if (when == "before the step")
            stop.Cancel();
        else
            io.AfterRead(ApuMaster, 1, stop.Cancel);

        Assert.False(await _exec.ExecuteStepAsync(ApuStartStep, stop.Token).WaitAsync(Hang));

        Assert.Empty(_ac.Writes);
        if (when == "before the step")
            Assert.Empty(_ac.Io);   // nothing read either
        Assert.Contains(_logged, line => line.StartsWith(L1011FoActionExecutor.ApuStart, StringComparison.Ordinal)
            && line.EndsWith(": stopped", StringComparison.Ordinal));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_StopAware_key_receives_the_flow_token_and_is_found_before_Pseudo()
    {
        // Found first, outside the gate: while its wait holds on the flow's Pause the gate is free (a Pseudo
        // composite or a plain write would hold it), and the flow's Stop reaches it and ends it.
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => held.Task.WaitAsync(ct));
        using var stop = new CancellationTokenSource();

        var start = _exec.ExecuteStepAsync(ApuStartStep, stop.Token);

        Assert.False(start.IsCompleted);                          // waiting, paused
        Assert.Equal(new[] { (ApuStartPress, 1.0) }, _ac.Writes);  // pressed before the wait
        Assert.True(_exec.WaitForDispatchDrainAsync().IsCompleted);   // the gate is free

        stop.Cancel();
        Assert.False(await start.WaitAsync(Hang));
        Assert.Contains(_logged, line => line.StartsWith(L1011FoActionExecutor.ApuStart, StringComparison.Ordinal)
            && line.EndsWith(": stopped", StringComparison.Ordinal));
        Assert.Single(_ac.Writes);
    }

    [Fact]
    public async Task A_paused_apu_spin_up_wait_does_not_block_a_hand_tick()
    {
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;   // the RPM stays 0 while paused
        _ac.Set(ContinuousIgnition, 0);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => resume.Task.WaitAsync(ct));
        using var stop = new CancellationTokenSource();

        var start = _exec.ExecuteStepAsync(ApuStartStep, stop.Token);
        Assert.False(start.IsCompleted);

        // A hand tick meanwhile: written and read back, not queued behind the paused wait.
        Assert.True(await _exec.Set(ContinuousIgnition, 1).WaitAsync(Hang));
        Assert.Equal(new[] { (ApuStartPress, 1.0), (ContinuousIgnition, 1.0) }, _ac.Writes);
        Assert.False(start.IsCompleted);

        // Resumed, the APU turns: the step is done.
        _ac.Set(ApuRpm, 5);
        resume.SetResult();
        Assert.True(await start.WaitAsync(Hang));
    }

    [Fact]
    public async Task A_hand_tick_of_the_apu_start_ignores_a_flow_pause()
    {
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;
        long pressedAt = -1;
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(ApuStartPress, 1, () => pressedAt = _ac.NowMs);
        _ac.OnClock = ac =>
        {
            if (pressedAt >= 0 && ac.NowMs >= pressedAt + 7_000)
                ac.Set(ApuRpm, 5);
        };
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => never.Task.WaitAsync(ct));   // a flow paused, never resumed

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuStart, 1).WaitAsync(Hang));

        Assert.Equal(new[] { (ApuStartPress, 1.0) }, _ac.Writes);
        Assert.InRange(_ac.NowMs - pressedAt, 7_000, 7_000 + L1011FoActionExecutor.PollMs);   // it waited, unpaused
    }

    // ---- Fix round 1: an APU start already pressed is never pressed again while it spins up ----------------------------
    // R23 releases the gate before the spin-up wait, and the RPM reads 0 for about 7 s after the press while the inlet
    // doors open: a call overlapping that window must not press again.

    [Fact]
    public void Pseudo_and_StopAware_share_no_key()
    {
        // The Stop-aware table is looked up first; sharing no key, the order can never pick the wrong composite.
        var pseudo = L1011FoActionExecutor.PseudoKeys.ToList();
        var stopAware = L1011FoActionExecutor.StopAwareKeys.ToList();

        Assert.NotEmpty(pseudo);
        Assert.NotEmpty(stopAware);
        Assert.Empty(pseudo.Intersect(stopAware, StringComparer.Ordinal));
        Assert.Contains(L1011FoActionExecutor.ApuStart, stopAware);
        Assert.Contains(L1011FoActionExecutor.ApuGenerator, pseudo);
    }

    [Fact]
    public async Task An_overlapping_apu_start_does_not_press_twice()
    {
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;   // the inlet doors: the RPM reads 0 for 7 s after the press
        long pressedAt = -1;
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(ApuStartPress, 1, () => pressedAt = _ac.NowMs);
        _ac.OnClock = ac =>
        {
            if (pressedAt >= 0 && ac.NowMs >= pressedAt + 7_000)
                ac.Set(ApuRpm, 5);
        };
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => resume.Task.WaitAsync(ct));
        using var stop = new CancellationTokenSource();

        var start = _exec.ExecuteStepAsync(ApuStartStep, stop.Token);   // pressed, its wait paused
        Assert.False(start.IsCompleted);

        // A hand tick of "APU: start" meanwhile: the RPM still reads 0, the master on, and it is not pressed again.
        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuStart, 1).WaitAsync(Hang));
        Assert.InRange(_ac.NowMs - pressedAt, 7_000, 7_000 + L1011FoActionExecutor.PollMs);   // it waited for the rise
        Assert.Contains(_logged, line => line.StartsWith(L1011FoActionExecutor.ApuStart, StringComparison.Ordinal)
            && line.Contains("not pressed again"));

        resume.SetResult();
        Assert.True(await start.WaitAsync(Hang));
        Assert.Equal(new[] { (ApuStartPress, 1.0) }, _ac.Writes);   // exactly one press
    }

    [Fact]
    public async Task An_overlapping_apu_start_gives_up_when_the_first_press_does()
    {
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;   // the RPM never rises
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => held.Task.WaitAsync(ct));
        using var stop = new CancellationTokenSource();
        var start = _exec.ExecuteStepAsync(ApuStartStep, stop.Token);   // pressed at 0, its wait paused
        long pressedAt = _ac.NowMs;

        await _ac.DelayAsync(5_000);   // five seconds into the spin-up
        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuStart, 1).WaitAsync(Hang));

        // Its wait ended with the first press's 20 s, not 20 s from the second call.
        Assert.InRange(_ac.NowMs - pressedAt, L1011FoActionExecutor.ApuSpinUpWaitMs,
            L1011FoActionExecutor.ApuSpinUpWaitMs + L1011FoActionExecutor.PollMs);
        Assert.Contains(_logged, line => line.Contains("15000 ms"));
        Assert.Single(_ac.Writes);

        stop.Cancel();
        Assert.False(await start.WaitAsync(Hang));
        Assert.Single(_ac.Writes);
    }

    [Theory]
    [InlineData("after the spin-up window")]   // 20 s with no rise: a retry
    [InlineData("on a new transport")]         // the window reopened: the press recorded on the old one does not count
    public async Task An_apu_start_after_the_spin_up_window_may_press_again(string when)
    {
        SeedApu(rpm: 0);
        _ac.ApuRpmAfterStart = 0;
        using var stop = new CancellationTokenSource();
        Task<bool>? first = null;
        if (when == "after the spin-up window")
        {
            Assert.False(await _exec.Set(L1011FoActionExecutor.ApuStart, 1));   // 20 s, the RPM never rose
        }
        else
        {
            var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _exec.AttachFlowPause(ct => held.Task.WaitAsync(ct));
            first = _exec.ExecuteStepAsync(ApuStartStep, stop.Token);   // pressed at 0, its wait paused
            Assert.False(first.IsCompleted);
            _exec.SetTransport(new ScriptedTransport(_ac));             // a new transport, the same aircraft
        }
        Assert.Single(_ac.Writes);

        _ac.ApuRpmAfterStart = 100;   // this time it turns
        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuStart, 1).WaitAsync(Hang));

        Assert.Equal(new[] { (ApuStartPress, 1.0), (ApuStartPress, 1.0) }, _ac.Writes);
        if (first is not null)
        {
            stop.Cancel();
            await first.WaitAsync(Hang);
        }
    }

    // ---- The APU generator --------------------------------------------------------------------------------------------

    private void SeedApuGenerator(double rpm, double field, double breaker)
    {
        _ac.Set(ApuRpm, rpm);
        _ac.Set(ApuGenField, field);
        _ac.Set(ApuGenBreaker, breaker);
    }

    [Fact]
    public async Task Apu_generator_on_closes_field_then_breaker_and_reads_both()
    {
        SeedApuGenerator(rpm: 100, field: 0, breaker: 0);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuGenerator, 1));

        Assert.Equal(new[] { (ApuGenFieldPress, 1.0), (ApuGenBreakerPress, 1.0) }, _ac.Writes);
        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, ApuGenField),
            (FakeL1011IoKind.Write, ApuGenFieldPress),
            (FakeL1011IoKind.FreshRead, ApuGenField),     // the field read back closed
            (FakeL1011IoKind.FreshRead, ApuGenBreaker),   // only then the breaker
            (FakeL1011IoKind.Write, ApuGenBreakerPress),
            (FakeL1011IoKind.FreshRead, ApuGenBreaker),
        }, IoKinds());
        Assert.Equal(1, _ac.Get(ApuGenField));
        Assert.Equal(1, _ac.Get(ApuGenBreaker));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(1.0, 0.0, new[] { ApuGenBreakerPress })]   // the field closed already
    [InlineData(0.0, 1.0, new[] { ApuGenFieldPress })]     // the breaker closed already
    [InlineData(1.0, 1.0, new string[0])]                  // on line: nothing pressed
    public async Task Apu_generator_on_skips_a_relay_already_closed(double field, double breaker, string[] pressed)
    {
        SeedApuGenerator(rpm: 100, field, breaker);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuGenerator, 1));

        Assert.Equal(pressed.Select(k => (k, 1.0)), _ac.Writes);
        Assert.Equal(1, _ac.Get(ApuGenField));
        Assert.Equal(1, _ac.Get(ApuGenBreaker));
    }

    [Fact]
    public async Task Apu_generator_on_fails_when_the_field_never_closes()
    {
        SeedApuGenerator(rpm: 50, field: 0, breaker: 0);   // not running: the field will not close

        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuGenerator, 1));

        Assert.Equal(new[] { (ApuGenFieldPress, 1.0) }, _ac.Writes);   // no breaker press
        Assert.True(_ac.NowMs >= L1011FoActionExecutor.RelayVerifyTimeoutMs);   // the relay's 8 s
        Assert.Contains(_logged, line => line.Contains(ApuGenField) && line.Contains("8000 ms"));
        Assert.Equal(0, _ac.Get(ApuGenBreaker));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(1, ApuGenField)]     // on: the field unread
    [InlineData(1, ApuGenBreaker)]   // on: the field closed, the breaker unread
    [InlineData(0, ApuGenBreaker)]   // off: the breaker unread
    public async Task Apu_generator_never_presses_on_an_unread_relay(int target, string unread)
    {
        SeedApuGenerator(rpm: 100, field: 1, breaker: target == 1 ? 0 : 1);
        _ac.Unset(unread);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuGenerator, target));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(unread));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Apu_generator_off_trips_the_breaker_and_reads_it_open()
    {
        SeedApuGenerator(rpm: 100, field: 1, breaker: 1);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuGenerator, 0));

        Assert.Equal(new[] { (ApuGenBreakerTrip, 1.0) }, _ac.Writes);
        Assert.Equal(new[] { ApuGenBreaker, ApuGenBreaker }, _ac.FreshReads);   // read first, read back open
        Assert.Equal(0, _ac.Get(ApuGenBreaker));
        Assert.Equal(1, _ac.Get(ApuGenField));   // the field is left alone
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Apu_generator_off_whose_breaker_stays_closed_is_not_done()
    {
        SeedApuGenerator(rpm: 100, field: 1, breaker: 1);
        _ac.NeverDeliver(ApuGenBreaker);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuGenerator, 0));

        Assert.Equal(new[] { (ApuGenBreakerTrip, 1.0) }, _ac.Writes);   // tripped once
        Assert.Contains(_logged, line => line.Contains(ApuGenBreaker) && line.Contains("8000 ms"));
    }

    [Fact]
    public async Task Apu_generator_off_is_done_when_already_open()
    {
        SeedApuGenerator(rpm: 100, field: 1, breaker: 0);

        Assert.True(await _exec.Set(L1011FoActionExecutor.ApuGenerator, 0));

        Assert.Empty(_ac.Writes);
        Assert.Equal(new[] { ApuGenBreaker }, _ac.FreshReads);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task Apu_generator_to_any_other_target_writes_nothing(int target)
    {
        SeedApuGenerator(rpm: 100, field: 0, breaker: 0);

        Assert.False(await _exec.Set(L1011FoActionExecutor.ApuGenerator, target));

        Assert.Empty(_ac.Io);
        Assert.Contains(_logged, line => line.Contains(L1011FoActionExecutor.ApuGenerator));
    }

    // ================= The audible tests (ruling R24: atomic Pseudo composites, one verdict each) ==================

    private static readonly string[] FireLoops =
    {
        "ROTARY_FIRE_DETECT_TEST_ENG_1", "ROTARY_FIRE_DETECT_TEST_ENG_2", "ROTARY_FIRE_DETECT_TEST_ENG_3", "ROTARY_FIRE_DETECT_TEST_APU",
    };
    private const string FireTestA = "SWITCH_FIRE_DETECT_TEST_A";
    private const string FireTestB = "SWITCH_FIRE_DETECT_TEST_B";
    private static readonly string[] FireLights =
    {
        "LAMP_ENG_FIRE_1", "LAMP_ENG_FIRE_2", "LAMP_ENG_FIRE_3", "LAMP_ENG_FIRE", "LAMP_FIRE_APU",
    };
    private const string FireLoopLight = "LAMP_LX_MIP_FIRE_LOOP";   // never lit live: never read
    private const string WheelWellLoops = "ROTARY_WHEEL_WELL_TEST";
    private const string WheelWellA = "SWITCH_WHEEL_WELL_TEST_A";
    private const string WheelWellB = "SWITCH_WHEEL_WELL_TEST_B";
    private const string WheelWellLight = "LAMP_FIRE_WHEEL_WELL";
    private const string MainPanelWheelWellLight = "LAMP_LX_MIP_WHEEL_WELL_FIRE";   // stayed out live: never read
    private const string PumpB = "SWITCH_HYD_AC_PUMP_B3";
    private const string PumpC = "SWITCH_HYD_AC_PUMP_C3";
    private const string PtuAb = "SWITCH_PTU_AB";
    private const string PtuCd = "SWITCH_PTU_CD";
    private static string Hyd(char system) => $"L1011_RO_HYD_{system}";

    private const string FirePass = "Engine and APU fire test: pass";
    private const string WheelWellPass = "Wheel well fire test: pass";
    private const string HydraulicPass = "Hydraulic check: pass";

    /// <summary>The fire test's writes: the four loops to Both, then A on and off, then B on and off.</summary>
    private static readonly (string, double)[] FireTestWrites =
    {
        ("ROTARY_FIRE_DETECT_TEST_ENG_1", 2), ("ROTARY_FIRE_DETECT_TEST_ENG_2", 2),
        ("ROTARY_FIRE_DETECT_TEST_ENG_3", 2), ("ROTARY_FIRE_DETECT_TEST_APU", 2),
        (FireTestA, 1), (FireTestA, 0), (FireTestB, 1), (FireTestB, 0),
    };

    /// <summary>The wheel well test's writes: the loops to Both, A and B on together, then both off.</summary>
    private static readonly (string, double)[] WheelWellTestWrites =
    {
        (WheelWellLoops, 1), (WheelWellA, 1), (WheelWellB, 1), (WheelWellA, 0), (WheelWellB, 0),
    };

    /// <summary>The hydraulic check's writes: the manual's order on, then everything off, the transfer units first.</summary>
    private static readonly (string, double)[] HydraulicCheckWrites =
    {
        (PumpB, 1), (PumpC, 1), (PtuAb, 1), (PtuCd, 1),
        (PtuAb, 0), (PtuCd, 0), (PumpB, 0), (PumpC, 0),
    };

    /// <summary>The loops on Loop A (0, as a cold aircraft may leave them), both loop tests off.</summary>
    private void SeedFireTest()
    {
        foreach (var loop in FireLoops)
            _ac.Set(loop, 0);
        _ac.Set(FireTestA, 0);
        _ac.Set(FireTestB, 0);
    }

    private void SeedWheelWellTest()
    {
        _ac.Set(WheelWellLoops, 0);
        _ac.Set(WheelWellA, 0);
        _ac.Set(WheelWellB, 0);
    }

    private void SeedHydraulicCheck()
    {
        foreach (var key in new[] { PumpB, PumpC, PtuAb, PtuCd })
            _ac.Set(key, 0);
    }

    private void AssertHydraulicsOff()
    {
        foreach (var key in new[] { PumpB, PumpC, PtuAb, PtuCd })
            Assert.Equal(0, _ac.Get(key));
    }

    /// <summary>The keys read fresh in each phase, chosen by <paramref name="which"/>: the events split at each
    /// lamp mute, since every phase begins with its (re-)armed mute.</summary>
    private static List<string[]> ReadsByPhase(ScriptedTransport io, Func<string, bool> which)
    {
        var phases = new List<string[]>();
        List<string>? current = null;
        foreach (var e in io.Events)
        {
            if (e.StartsWith("mute ", StringComparison.Ordinal))
            {
                if (current != null)
                    phases.Add(current.ToArray());
                current = new List<string>();
            }
            else if (current != null && e.StartsWith("read ", StringComparison.Ordinal) && which(e["read ".Length..]))
                current.Add(e["read ".Length..]);
        }
        if (current != null)
            phases.Add(current.ToArray());
        return phases;
    }

    [Fact]
    public void The_audible_tests_are_pseudo_keys_on_registered_lights_switches_and_readouts()
    {
        Assert.Equal("FO_FIRE_TEST", L1011FoActionExecutor.FireTest);
        Assert.Equal("FO_WHEEL_WELL_FIRE_TEST", L1011FoActionExecutor.WheelWellFireTest);
        Assert.Equal("FO_HYDRAULIC_CHECK", L1011FoActionExecutor.HydraulicCheck);
        foreach (var key in new[] { L1011FoActionExecutor.FireTest, L1011FoActionExecutor.WheelWellFireTest, L1011FoActionExecutor.HydraulicCheck })
            Assert.True(L1011FoActionExecutor.IsPseudoKey(key), key);

        Assert.Equal(45_000, L1011FoActionExecutor.FireTestMuteMs);
        Assert.Equal(60_000, L1011FoActionExecutor.HydraulicCheckMuteMs);
        Assert.Equal(2000, L1011FoActionExecutor.HydraulicNormalPsi);
        Assert.Equal(900, L1011FoActionExecutor.HydraulicAMinPsi);
        Assert.Equal(1400, L1011FoActionExecutor.HydraulicDMinPsi);
        Assert.Equal(100, L1011FoActionExecutor.HydraulicZeroPsi);
        Assert.Equal(20_000, L1011FoActionExecutor.HydraulicRiseWaitMs);
        Assert.Equal(30_000, L1011FoActionExecutor.HydraulicBleedWaitMs);

        // Every light read is a registered annunciator lamp the definition reads.
        var lamps = L1011Annunciators.All.Select(L1011Annunciators.KeyFor).ToHashSet(StringComparer.Ordinal);
        foreach (var key in FireLights.Append(WheelWellLight))
        {
            Assert.Contains(key, lamps);
            Assert.True(_ac.CanReadBack(key), key);
        }
        // Every switch is a row the definition reads back, and every pressure a registered readout.
        foreach (var key in FireLoops.Concat(new[] { FireTestA, FireTestB, WheelWellLoops, WheelWellA, WheelWellB, PumpB, PumpC, PtuAb, PtuCd }))
            Assert.True(_ac.CanReadBack(key), key);
        foreach (char system in "ABCD")
            Assert.True(_ac.CanReadBack(Hyd(system)), Hyd(system));
    }

    // ---- The engine and APU fire test ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fire_test_pass_writes_in_order_and_leaves_the_selectors_on_both(bool progress)
    {
        SeedFireTest();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.True(await _exec.Set(L1011FoActionExecutor.FireTest, 1));

        Assert.Equal(FireTestWrites, _ac.Writes);
        Assert.Equal(0, _ac.Get(FireTestA));
        Assert.Equal(0, _ac.Get(FireTestB));
        Assert.All(FireLoops, loop => Assert.Equal(2, _ac.Get(loop)));   // left on Both

        // Muted before anything is read or written, and re-armed before every phase, never cut short.
        Assert.Equal("mute 45000", io.Events[0]);
        Assert.True(_ac.MuteRequests.Count >= 5);
        Assert.All(_ac.MuteRequests, ms => Assert.Equal(L1011FoActionExecutor.FireTestMuteMs, ms));

        // The loops read no light; each test on, then off, reads the five, in order; the loop light never.
        Assert.Equal(new[] { Array.Empty<string>(), FireLights, FireLights, FireLights, FireLights },
            ReadsByPhase(io, k => k.StartsWith("LAMP_", StringComparison.Ordinal)));
        Assert.DoesNotContain(FireLoopLight, _ac.FreshReads);

        Assert.Equal(progress ? new[] { FirePass } : Array.Empty<string>(), _spoken);
    }

    [Theory]
    [InlineData("LAMP_ENG_FIRE_1", "Engine and APU fire test: engine 1 fire light did not come on")]
    [InlineData("LAMP_ENG_FIRE_2", "Engine and APU fire test: engine 2 fire light did not come on")]
    [InlineData("LAMP_ENG_FIRE_3", "Engine and APU fire test: engine 3 fire light did not come on")]
    [InlineData("LAMP_ENG_FIRE", "Engine and APU fire test: engine fire warning light did not come on")]
    [InlineData("LAMP_FIRE_APU", "Engine and APU fire test: APU fire light did not come on")]
    public async Task Each_fire_light_that_fails_is_named_once(string lamp, string sentence)
    {
        SeedFireTest();
        _ac.NeverDeliver(lamp);
        _ac.Set(lamp, 0);   // stays out whatever the tests do

        Assert.False(await _exec.Set(L1011FoActionExecutor.FireTest, 1));

        Assert.Equal(new[] { sentence }, _spoken);   // once, though test B finds it out again
        Assert.Equal(FireTestWrites, _ac.Writes);    // the test still ran to the end
        Assert.Equal(0, _ac.Get(FireTestA));
        Assert.Equal(0, _ac.Get(FireTestB));
    }

    [Fact]
    public async Task Fire_test_light_that_stays_lit_is_named_did_not_go_out()
    {
        SeedFireTest();
        _ac.NeverDeliver("LAMP_ENG_FIRE_2");
        _ac.Set("LAMP_ENG_FIRE_2", 1);   // stays lit whatever the tests do

        Assert.False(await _exec.Set(L1011FoActionExecutor.FireTest, 1));

        Assert.Equal(new[] { "Engine and APU fire test: engine 2 fire light did not go out" }, _spoken);
        Assert.Equal(FireTestWrites, _ac.Writes);
    }

    [Fact]
    public async Task Fire_test_refused_switch_ends_the_test_with_the_switches_back()
    {
        SeedFireTest();
        _ac.Refuse(FireTestA);

        Assert.False(await _exec.Set(L1011FoActionExecutor.FireTest, 1));

        Assert.Equal(FireTestWrites.Take(4), _ac.Writes);   // the loops only: B never written
        Assert.Contains(_logged, line => line.Contains($"{FireTestA} -> 1") && line.Contains(FakeL1011Aircraft.RefusalReason));
        Assert.Contains(_logged, line => line.Contains($"{FireTestA} -> 0"));   // the put-back tried A
        Assert.Contains(FireTestB, _ac.FreshReads);                              // and B (already off)
        Assert.DoesNotContain(_ac.FreshReads, k => k.StartsWith("LAMP_", StringComparison.Ordinal));
        Assert.Equal(0, _ac.Get(FireTestA));
        Assert.Equal(0, _ac.Get(FireTestB));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fire_test_speaks_nothing_for_a_failed_write(bool progress)
    {
        // The flow's "Skipping" is the failed write's only speech: the executor stays silent.
        SeedFireTest();
        _ac.NeverDeliver(FireTestB);   // sent, never reads on: not done after the verify time-out
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.False(await _exec.Set(L1011FoActionExecutor.FireTest, 1));

        Assert.Equal(FireTestWrites.Take(7), _ac.Writes);   // ended at B on
        Assert.Equal(0, _ac.Get(FireTestA));
        Assert.Equal(0, _ac.Get(FireTestB));
        Assert.Empty(_spoken);
    }

    // ---- The wheel well fire test ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Wheel_well_test_pass_writes_selector_then_both_tests_and_reads_the_light(bool progress)
    {
        SeedWheelWellTest();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.True(await _exec.Set(L1011FoActionExecutor.WheelWellFireTest, 1));

        Assert.Equal(WheelWellTestWrites, _ac.Writes);
        Assert.Equal(1, _ac.Get(WheelWellLoops));   // left on Both
        Assert.Equal("mute 45000", io.Events[0]);
        Assert.All(_ac.MuteRequests, ms => Assert.Equal(L1011FoActionExecutor.FireTestMuteMs, ms));
        Assert.Equal(new[] { Array.Empty<string>(), new[] { WheelWellLight }, new[] { WheelWellLight } },
            ReadsByPhase(io, k => k.StartsWith("LAMP_", StringComparison.Ordinal)));
        Assert.DoesNotContain(MainPanelWheelWellLight, _ac.FreshReads);
        Assert.Equal(progress ? new[] { WheelWellPass } : Array.Empty<string>(), _spoken);
    }

    [Fact]
    public async Task The_wheel_well_light_not_coming_on_is_named()
    {
        SeedWheelWellTest();
        _ac.NeverDeliver(WheelWellLight);
        _ac.Set(WheelWellLight, 0);

        Assert.False(await _exec.Set(L1011FoActionExecutor.WheelWellFireTest, 1));

        Assert.Equal(new[] { "Wheel well fire test: wheel well fire light did not come on" }, _spoken);
        Assert.Equal(WheelWellTestWrites, _ac.Writes);
        Assert.Equal(0, _ac.Get(WheelWellA));
        Assert.Equal(0, _ac.Get(WheelWellB));
    }

    [Fact]
    public async Task Wheel_well_light_unread_is_said_could_not_be_read()
    {
        SeedWheelWellTest();
        _ac.NeverDeliver(WheelWellLight);
        _ac.Unset(WheelWellLight);

        Assert.False(await _exec.Set(L1011FoActionExecutor.WheelWellFireTest, 1));

        Assert.Equal(new[] { "Wheel well fire test: wheel well fire light could not be read" }, _spoken);
        Assert.Equal(WheelWellTestWrites, _ac.Writes);
    }

    [Fact]
    public async Task Wheel_well_test_refused_switch_puts_both_tests_back()
    {
        SeedWheelWellTest();
        _ac.Refuse(WheelWellB);

        Assert.False(await _exec.Set(L1011FoActionExecutor.WheelWellFireTest, 1));

        Assert.Equal(new[] { (WheelWellLoops, 1.0), (WheelWellA, 1.0), (WheelWellA, 0.0) }, _ac.Writes);
        Assert.Equal(0, _ac.Get(WheelWellA));
        Assert.DoesNotContain(WheelWellLight, _ac.FreshReads);
        Assert.Empty(_spoken);
    }

    // ---- The hydraulic check ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Hydraulic_check_pass_runs_the_manuals_order_and_ends_with_everything_off(bool progress)
    {
        SeedHydraulicCheck();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        _exec.AttachFlowProgressSpeech(() => progress);

        Assert.True(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));

        Assert.Equal(HydraulicCheckWrites, _ac.Writes);
        AssertHydraulicsOff();

        // Muted first and re-armed before each phase's first write.
        Assert.Equal(new[]
        {
            "mute 60000", $"write {PumpB}=1",
            "mute 60000", $"write {PumpC}=1",
            "mute 60000", $"write {PtuAb}=1", $"write {PtuCd}=1",
            "mute 60000", $"write {PtuAb}=0", $"write {PtuCd}=0", $"write {PumpB}=0", $"write {PumpC}=0",
        }, io.Events.Where(e => !e.StartsWith("read ", StringComparison.Ordinal)));
        Assert.Equal("mute 60000", io.Events[0]);

        // Each phase reads its own pressures fresh, in A, B, C, D order.
        Assert.Equal(new[]
        {
            new[] { Hyd('B') }, new[] { Hyd('C') }, new[] { Hyd('A'), Hyd('D') }, new[] { Hyd('A'), Hyd('B'), Hyd('C'), Hyd('D') },
        }, ReadsByPhase(io, k => k.StartsWith("L1011_RO_HYD_", StringComparison.Ordinal)));

        Assert.Equal(progress ? new[] { HydraulicPass } : Array.Empty<string>(), _spoken);
    }

    [Fact]
    public async Task System_B_not_rising_is_named_with_an_invariant_number()
    {
        SeedHydraulicCheck();
        _ac.NeverDeliver(Hyd('B'));
        _ac.Set(Hyd('B'), 14.5);

        Assert.False(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));

        Assert.Equal(new[] { "Hydraulic check: system B pressure did not reach 2,000 psi" }, _spoken);
        // The B phase waited its 20 s on the transport's clock, then the check carried on.
        long bOn = _ac.Io.Single(e => e is { Kind: FakeL1011IoKind.Write, Key: PumpB, Value: 1 }).AtMs;
        long cOn = _ac.Io.Single(e => e is { Kind: FakeL1011IoKind.Write, Key: PumpC, Value: 1 }).AtMs;
        Assert.InRange(cOn - bOn, L1011FoActionExecutor.HydraulicRiseWaitMs,
            L1011FoActionExecutor.HydraulicRiseWaitMs + L1011FoActionExecutor.PollMs);
        Assert.Equal(HydraulicCheckWrites, _ac.Writes);
        AssertHydraulicsOff();
    }

    [Fact]
    public async Task System_D_short_of_1400_is_named()
    {
        SeedHydraulicCheck();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(PtuCd, 1, () =>
        {
            _ac.NeverDeliver(Hyd('D'));
            _ac.Set(Hyd('D'), 1200);   // rises short of 1,400
        });
        io.AfterWrite(PtuCd, 0, () => _ac.Set(Hyd('D'), 14.5));   // and bleeds down once off

        Assert.False(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));

        Assert.Equal(new[] { "Hydraulic check: system D pressure did not reach 1,400 psi" }, _spoken);
        Assert.Equal(HydraulicCheckWrites, _ac.Writes);
        AssertHydraulicsOff();
    }

    [Fact]
    public async Task Hydraulic_system_that_does_not_bleed_down_is_named()
    {
        SeedHydraulicCheck();
        var io = new ScriptedTransport(_ac);
        _exec.SetTransport(io);
        io.AfterWrite(PumpC, 1, () => _ac.NeverDeliver(Hyd('C')));   // stuck at 2,650 from the C phase on

        Assert.False(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));

        Assert.Equal(new[] { "Hydraulic check: system C pressure did not fall below 100 psi" }, _spoken);
        long lastOff = _ac.Io.Single(e => e is { Kind: FakeL1011IoKind.Write, Key: PumpC, Value: 0 }).AtMs;
        Assert.InRange(_ac.NowMs - lastOff, L1011FoActionExecutor.HydraulicBleedWaitMs,
            L1011FoActionExecutor.HydraulicBleedWaitMs + L1011FoActionExecutor.PollMs);
        Assert.Equal(HydraulicCheckWrites, _ac.Writes);
        AssertHydraulicsOff();
    }

    [Fact]
    public async Task Hydraulic_system_unread_is_said_could_not_be_read()
    {
        SeedHydraulicCheck();
        _ac.NeverDeliver(Hyd('B'));
        _ac.Unset(Hyd('B'));

        Assert.False(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));

        Assert.Equal(new[] { "Hydraulic check: system B pressure could not be read" }, _spoken);
        Assert.Equal(HydraulicCheckWrites, _ac.Writes);
        AssertHydraulicsOff();
    }

    [Fact]
    public async Task Hydraulic_sentences_use_a_dot_and_a_comma_group_under_de_DE()
    {
        SeedHydraulicCheck();
        _ac.NeverDeliver(Hyd('B'));
        _ac.Set(Hyd('B'), 14.5);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");

            Assert.False(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(new[] { "Hydraulic check: system B pressure did not reach 2,000 psi" }, _spoken);   // never "2.000"
        Assert.Contains(_logged, line => line.Contains($"{Hyd('B')} reads 14.5"));                         // never "14,5"
    }

    [Fact]
    public async Task Hydraulic_check_write_not_done_ends_at_once_with_everything_off()
    {
        SeedHydraulicCheck();
        _ac.Refuse(PtuAb);

        Assert.False(await _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1));

        // B and C on, the refused transfer unit ends the check: no D write, no wait; the put-back turns the pumps off.
        Assert.Equal(new[] { (PumpB, 1.0), (PumpC, 1.0), (PumpB, 0.0), (PumpC, 0.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains($"{PtuAb} -> 1") && line.Contains(FakeL1011Aircraft.RefusalReason));
        Assert.DoesNotContain(Hyd('A'), _ac.FreshReads);
        AssertHydraulicsOff();
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task The_hydraulic_check_runs_under_the_one_gate()
    {
        SeedHydraulicCheck();
        _ac.Set(ContinuousIgnition, 0);
        var io = new HoldingTransport(_ac) { TimeStandsStill = true };
        _exec.SetTransport(io);
        var door = io.Hold(PtuCd);

        var check = _exec.Set(L1011FoActionExecutor.HydraulicCheck, 1);
        var tick = _exec.Set(ContinuousIgnition, 1);

        Assert.Equal(new[] { PumpB, PumpC, PtuAb, PtuCd }, io.Started);   // the check is in flight
        Assert.False(tick.IsCompleted);
        Assert.DoesNotContain(_ac.Io, e => e.Key == ContinuousIgnition);

        door.SetResult();
        Assert.Equal(new[] { true, true }, await Task.WhenAll(check, tick).WaitAsync(Hang));

        int lastOfCheck = _ac.Io.FindLastIndex(e => e.Key != ContinuousIgnition);
        int firstOfTick = _ac.Io.FindIndex(e => e.Key == ContinuousIgnition);
        Assert.True(firstOfTick > lastOfCheck);   // the tick only after the whole check, the bleed-down included
    }

    [Fact]
    public async Task A_flow_step_runs_the_hydraulic_check_to_the_end_through_a_pause_and_a_stop()
    {
        // Atomic and not pause-aware (ruling R24): a Stop or Pause mid-check never leaves a pump on.
        SeedHydraulicCheck();
        _ac.NeverDeliver(Hyd('B'));
        _ac.Set(Hyd('B'), 14.5);   // the B phase waits its full 20 s
        var never = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => never.Task.WaitAsync(ct));   // a flow paused, never resumed
        using var stop = new CancellationTokenSource();
        _ac.OnClock = ac =>
        {
            if (ac.NowMs >= 5_000 && !stop.IsCancellationRequested)
                stop.Cancel();
        };

        bool done = await _exec.ExecuteStepAsync(new Step { EventName = L1011FoActionExecutor.HydraulicCheck }, stop.Token).WaitAsync(Hang);

        Assert.False(done);
        Assert.True(stop.IsCancellationRequested);
        Assert.Equal(HydraulicCheckWrites, _ac.Writes);
        AssertHydraulicsOff();
        Assert.Equal(new[] { "Hydraulic check: system B pressure did not reach 2,000 psi" }, _spoken);
    }

    // ================= The cabin pressurization composites ========================================================
    // One write through the transport's own entry and a bounded read-back, under the gate (Pseudo, atomic), as the
    // external power composite is. Nothing is spoken: a pass is the flow's line, a failure the flow's "Skipping".

    private const string CabinBaroReadout = "L1011_RO_CABIN_BARO";
    private const string CabinAltitudeReadout = "L1011_RO_CABIN_ALT_SELECTED";
    private const double MetresPerFoot = 0.3048;

    /// <summary>
    /// The fake with <see cref="IL1011FoTransport.Ready"/> true but both cabin writes
    /// <see cref="L1011FoOutcome.Unavailable"/>, as the definition answers when its UI thread can no longer take
    /// the call. Everything else is the fake's.
    /// </summary>
    private sealed class CabinWritesUnavailableTransport : IL1011FoTransport
    {
        private readonly FakeL1011Aircraft _ac;

        public CabinWritesUnavailableTransport(FakeL1011Aircraft ac) => _ac = ac;

        public bool Ready => _ac.Ready;
        public long NowMs => _ac.NowMs;
        public Task<L1011FoResult> SetAsync(string key, double value) => _ac.SetAsync(key, value);
        public Task<L1011FoResult> SetAltimetersAsync(double millibars) => _ac.SetAltimetersAsync(millibars);
        public Task<L1011FoResult> SetCabinBaroAsync(double millibars) => Task.FromResult(L1011FoResult.Unavailable);
        public Task<L1011FoResult> SetCabinAltitudeSelectorAsync(int step) => Task.FromResult(L1011FoResult.Unavailable);
        public bool CanReadBack(string key) => _ac.CanReadBack(key);
        public Task<double?> ReadFreshAsync(string key, int timeoutMs) => _ac.ReadFreshAsync(key, timeoutMs);
        public double? ReadCached(string key) => _ac.ReadCached(key);
        public void MuteLampSpeech(int ms) => _ac.MuteLampSpeech(ms);
        public Task DelayAsync(int ms) => _ac.DelayAsync(ms);
    }

    private IEnumerable<FakeL1011Io> CabinBaroWrites() => _ac.Io.Where(e => e.Kind == FakeL1011IoKind.CabinBaro);

    private IEnumerable<FakeL1011Io> CabinAltitudeWrites() => _ac.Io.Where(e => e.Kind == FakeL1011IoKind.CabinAltitudeStep);

    [Fact]
    public void Cabin_composites_are_pseudo_keys()
    {
        Assert.Equal("FO_CABIN_BARO", L1011FoActionExecutor.CabinBaro);
        Assert.Equal("FO_CABIN_ALT_SELECT", L1011FoActionExecutor.CabinAltitudeSelect);
        foreach (var key in new[] { L1011FoActionExecutor.CabinBaro, L1011FoActionExecutor.CabinAltitudeSelect })
        {
            Assert.True(L1011FoActionExecutor.IsPseudoKey(key), key);
            Assert.Contains(key, L1011FoActionExecutor.PseudoKeys);           // under the gate, atomic
            Assert.DoesNotContain(key, L1011FoActionExecutor.StopAwareKeys);
        }
    }

    // ---- The cabin barometric setting --------------------------------------------------------------------------------

    [Fact]
    public async Task Cabin_baro_writes_hundredths_as_hectopascals_and_reads_back()
    {
        Assert.True(await _exec.Set(L1011FoActionExecutor.CabinBaro, 100237));

        var write = Assert.Single(CabinBaroWrites());
        Assert.Equal(FakeL1011Aircraft.CabinBaroIoKey, write.Key);
        Assert.Equal(1002.37, write.Value!.Value, 6);
        Assert.Equal(1002.37, _ac.Get(CabinBaroReadout)!.Value, 6);
        Assert.Empty(_ac.Writes);                                      // no panel row written
        Assert.Equal(new[] { CabinBaroReadout }, _ac.FreshReads);      // read back fresh, no read first
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Cabin_baro_not_delivered_is_not_done()
    {
        _ac.NeverDeliver(CabinBaroReadout);
        _ac.Set(CabinBaroReadout, 1013.25);

        Assert.False(await _exec.Set(L1011FoActionExecutor.CabinBaro, 100237));

        Assert.Single(CabinBaroWrites());                              // written once, never repeated
        Assert.InRange(_ac.NowMs, L1011FoActionExecutor.VerifyTimeoutMs,
            L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + L1011FoActionExecutor.ReadTimeoutMs);
        Assert.True(_ac.FreshReads.Count > 2);                         // polled, not read once
        Assert.Contains(_logged, line => line.Contains(CabinBaroReadout) && line.Contains("1013.25"));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task Cabin_baro_never_set_is_not_done_and_logged_unread()
    {
        _ac.NeverDeliver(CabinBaroReadout);   // never read at all

        Assert.False(await _exec.Set(L1011FoActionExecutor.CabinBaro, 100237));

        Assert.Single(CabinBaroWrites());
        Assert.Contains(_logged, line => line.Contains(CabinBaroReadout) && line.Contains("unread"));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(200000)]
    [InlineData(110000)]
    [InlineData(90000)]
    [InlineData(0)]
    public async Task Cabin_baro_out_of_range_is_not_done_and_writes_nothing(int target)
    {
        Assert.False(await _exec.Set(L1011FoActionExecutor.CabinBaro, target));

        Assert.Empty(CabinBaroWrites());
        Assert.Empty(_ac.Writes);
        Assert.Empty(_ac.FreshReads);     // nothing was sent, so nothing to read back and no wait
        Assert.Equal(0, _ac.NowMs);
        Assert.Contains(_logged, line => line.Contains(L1011FoActionExecutor.CabinBaro) && line.Contains(L1011Levers.AltimeterRangeError));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(0.4, true)]
    [InlineData(-0.4, true)]
    [InlineData(0.6, false)]
    [InlineData(-0.6, false)]
    public async Task Cabin_baro_reads_its_target_within_half_a_hectopascal(double offset, bool done)
    {
        _ac.NeverDeliver(CabinBaroReadout);
        _ac.Set(CabinBaroReadout, 1002.37 + offset);

        Assert.Equal(done, await _exec.Set(L1011FoActionExecutor.CabinBaro, 100237));
    }

    [Fact]
    public async Task Cabin_baro_the_aircraft_cannot_take_is_not_done_and_logged()
    {
        _exec.SetTransport(new CabinWritesUnavailableTransport(_ac));

        Assert.False(await _exec.Set(L1011FoActionExecutor.CabinBaro, 100237));

        Assert.Empty(_ac.FreshReads);
        Assert.Contains(_logged, line => line.Contains(L1011FoActionExecutor.CabinBaro) && line.Contains(nameof(L1011FoOutcome.Unavailable)));
        Assert.Empty(_spoken);
    }

    // ---- The cabin altitude selector -----------------------------------------------------------------------------------

    [Theory]
    [InlineData(4500, 49)]
    [InlineData(8000, 100)]
    [InlineData(-250, 0)]
    public async Task Cabin_altitude_select_writes_the_knob_step_and_reads_back_in_feet(int feet, int step)
    {
        Assert.True(await _exec.Set(L1011FoActionExecutor.CabinAltitudeSelect, feet));

        var write = Assert.Single(CabinAltitudeWrites());
        Assert.Equal(FakeL1011Aircraft.CabinAltitudeIoKey, write.Key);
        Assert.Equal(step, write.Value!.Value);
        // The aircraft delivers metres: the step's altitude in feet, times 0.3048.
        Assert.Equal(L1011Pressurization.CabinAltitudeForStep(step) * MetresPerFoot, _ac.Get(CabinAltitudeReadout)!.Value, 6);
        Assert.Empty(_ac.Writes);                                          // no panel row written
        Assert.Equal(new[] { CabinAltitudeReadout }, _ac.FreshReads);      // read back fresh, no read first
        Assert.Empty(_spoken);
        // Logged with the step and the target.
        Assert.Contains(_logged, line => line.Contains($"step {step}") && line.Contains($"-> {feet}"));
    }

    [Fact]
    public async Task Cabin_altitude_select_fails_when_the_readout_stays()
    {
        _ac.NeverDeliver(CabinAltitudeReadout);
        _ac.Set(CabinAltitudeReadout, 7549 * MetresPerFoot);   // where the selector was left

        Assert.False(await _exec.Set(L1011FoActionExecutor.CabinAltitudeSelect, 4500));

        var write = Assert.Single(CabinAltitudeWrites());                  // written once, never repeated
        Assert.Equal(49, write.Value!.Value);
        Assert.InRange(_ac.NowMs, L1011FoActionExecutor.VerifyTimeoutMs,
            L1011FoActionExecutor.VerifyTimeoutMs + L1011FoActionExecutor.PollMs + L1011FoActionExecutor.ReadTimeoutMs);
        Assert.True(_ac.FreshReads.Count > 2);                             // polled, not read once
        Assert.Contains(_logged, line => line.Contains(CabinAltitudeReadout) && line.Contains("step 49") && line.Contains("-> 4500"));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(149, true)]
    [InlineData(-149, true)]
    [InlineData(151, false)]
    [InlineData(-151, false)]
    public async Task Cabin_altitude_select_reads_its_target_within_150_feet(int offsetFeet, bool done)
    {
        _ac.NeverDeliver(CabinAltitudeReadout);
        _ac.Set(CabinAltitudeReadout, (4500 + offsetFeet) * MetresPerFoot);

        Assert.Equal(done, await _exec.Set(L1011FoActionExecutor.CabinAltitudeSelect, 4500));
    }

    [Fact]
    public async Task Cabin_altitude_select_the_aircraft_cannot_take_is_not_done_and_logged()
    {
        _exec.SetTransport(new CabinWritesUnavailableTransport(_ac));

        Assert.False(await _exec.Set(L1011FoActionExecutor.CabinAltitudeSelect, 4500));

        Assert.Empty(_ac.FreshReads);
        Assert.Contains(_logged, line => line.Contains(L1011FoActionExecutor.CabinAltitudeSelect) && line.Contains(nameof(L1011FoOutcome.Unavailable)));
        Assert.Empty(_spoken);
    }

    // ---- Both -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cabin_composites_run_to_the_end_through_a_flow_step_whose_stop_has_been_asked_for()
    {
        // Pseudo, not StopAware: each is one write and a bounded read-back, atomic, so a Stop already asked for
        // neither skips the write nor cuts the read-back short.
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.True(await _exec.ExecuteStepAsync(new Step { EventName = L1011FoActionExecutor.CabinBaro, TargetValue = 100237 }, stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(new Step { EventName = L1011FoActionExecutor.CabinAltitudeSelect, TargetValue = 4500 }, stop.Token));

        Assert.Single(CabinBaroWrites());
        Assert.Single(CabinAltitudeWrites());
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task Cabin_composites_log_their_numbers_invariantly_under_a_comma_decimal_culture(string culture)
    {
        // Nothing here is spoken; the numbers are in debug.log, and a de-DE or sv-SE log would read "1002,37"
        // (and an sv-SE minus sign is not "-").
        _ac.NeverDeliver(CabinBaroReadout);
        _ac.Set(CabinBaroReadout, 1013.25);
        _ac.NeverDeliver(CabinAltitudeReadout);
        _ac.Set(CabinAltitudeReadout, 7549 * MetresPerFoot);
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo(culture);

            Assert.False(await _exec.Set(L1011FoActionExecutor.CabinBaro, 100237));
            Assert.False(await _exec.Set(L1011FoActionExecutor.CabinAltitudeSelect, -250));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }

        Assert.Equal(1002.37, Assert.Single(CabinBaroWrites()).Value!.Value, 6);
        Assert.Equal(0, Assert.Single(CabinAltitudeWrites()).Value!.Value);
        Assert.Contains(_logged, line => line.Contains(CabinBaroReadout) && line.Contains("1002.37 hPa") && line.Contains("1013.25"));
        Assert.Contains(_logged, line => line.Contains(CabinAltitudeReadout) && line.Contains("-> -250") && line.Contains("step 0"));
        Assert.DoesNotContain(_logged, line => line.Contains("1002,37") || line.Contains("1013,25"));
    }
}
