using System.Collections.Concurrent;
using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's engine start on the fake TriStar's engines: the ground start written once and
/// watched for rotation, the light-off watch, the idle wait and the generators. A start that times out or is
/// stopped is released only while that engine's fuel and ignition read off, never by a fuel write, and once: by
/// whoever takes the run's recorded start out (the watch, the flow's Stop clean-up or the window's close).
/// Nothing is spoken: a start a flow's run took and could not release becomes that run's notice, which FlowManager
/// speaks after the run's own end sentence. Every engine starts cold (N3 1.6, a cold gauge never reads 0), its fuel
/// and ignition and its ground start off.
/// </summary>
public class L1011FoEngineStartExecutorTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static readonly int[] Engines = { 1, 2, 3 };
    private const double Cold = 1.6;
    private const double Idle = 59.2;
    private const int Poll = L1011FoActionExecutor.PollMs;

    private readonly FakeL1011Aircraft _ac = new();
    private readonly L1011FoActionExecutor _exec = new();
    private readonly ConcurrentQueue<string> _spoken = new();
    private readonly ConcurrentQueue<string> _logged = new();

    public L1011FoEngineStartExecutorTests()
    {
        _exec.SetTransport(_ac);
        _exec.SetSpeech(_spoken.Enqueue);
        _exec.LogTap = _logged.Enqueue;
        foreach (int n in Engines)
        {
            _ac.Set(N3(n), Cold);
            _ac.Set(Fuel(n), 0);
            _ac.Set(Switch(n), 0);
        }
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

    private static Step Flow(string key, int target = 1) => new() { EventName = key, TargetValue = target };

    /// <summary>
    /// Runs <paramref name="act"/> with no synchronization context, so a wait it cancels resumes inline, inside the
    /// cancel, before the next line runs. On the UI thread that wait resumes on the thread pool instead and races the
    /// next line: this is the order in which the watch gets there first.
    /// </summary>
    private static void WithTheWatchResumingFirst(Action act)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            act();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    /// <summary>
    /// The fake behind a live transport's timing. <see cref="YieldOnRead"/>: a fresh read completes later, never
    /// inline (SimConnect delivers on the next batch). <see cref="HoldRead"/>: the next fresh read of a key waits for
    /// the returned door, so a write reading it first holds the gate until then (a held write would not: the
    /// executor's write time-out ends at once on the fake's clock). <see cref="LandLate"/>: the next click of a key
    /// to 1 is sent, but reads on only once <c>landsOnReadOf</c> is next read fresh. Everything else is the fake's.
    /// </summary>
    private sealed class LiveTimingTransport : IL1011FoTransport
    {
        private readonly FakeL1011Aircraft _ac;
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _held = new(StringComparer.Ordinal);
        private (string Key, string LandsOnReadOf)? _late;
        private string? _landing;

        public LiveTimingTransport(FakeL1011Aircraft ac) => _ac = ac;

        public bool YieldOnRead { get; init; }

        public TaskCompletionSource HoldRead(string key)
        {
            var door = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _held[key] = door;
            return door;
        }

        public void LandLate(string key, string landsOnReadOf) => _late = (key, landsOnReadOf);

        public bool Ready => _ac.Ready;
        public long NowMs => _ac.NowMs;

        public async Task<double?> ReadFreshAsync(string key, int timeoutMs)
        {
            if (YieldOnRead)
                await Task.Yield();
            if (_held.TryRemove(key, out var door))
                await door.Task.ConfigureAwait(false);
            if (_late is { } late && _landing == late.Key && key == late.LandsOnReadOf)
            {
                _ac.Set(late.Key, 1);   // the late click lands
                _late = null;
                _landing = null;
            }
            return await _ac.ReadFreshAsync(key, timeoutMs).ConfigureAwait(false);
        }

        public async Task<L1011FoResult> SetAsync(string key, double value)
        {
            var result = await _ac.SetAsync(key, value).ConfigureAwait(false);
            if (_late is { } late && late.Key == key && value == 1 && _landing is null)
            {
                _ac.Set(key, 0);   // sent, but not delivered yet
                _landing = key;
            }
            return result;
        }

        public Task<L1011FoResult> SetAltimetersAsync(double millibars) => _ac.SetAltimetersAsync(millibars);
        public Task<L1011FoResult> SetCabinBaroAsync(double millibars) => _ac.SetCabinBaroAsync(millibars);
        public Task<L1011FoResult> SetCabinAltitudeSelectorAsync(int step) => _ac.SetCabinAltitudeSelectorAsync(step);
        public bool CanReadBack(string key) => _ac.CanReadBack(key);
        public double? ReadCached(string key) => _ac.ReadCached(key);
        public void MuteLampSpeech(int ms) => _ac.MuteLampSpeech(ms);
        public Task DelayAsync(int ms) => _ac.DelayAsync(ms);
    }

    private static string Switch(int n) => $"SWITCH_GND_START_ENG_{n}";
    private static string Fuel(int n) => $"TOGGLE_CUTOFF_ENG_{n}";
    private static string N3(int n) => $"L1011_RO_N3_{n}";
    private static string FieldPress(int n) => $"SWITCH_BRG_GEN_{n}";
    private static string BreakerPress(int n) => $"SWITCH_GEN_FLOW_{n}";
    private static string Field(int n) => $"L1011_RO_GEN_{n}_FIELD";
    private static string Breaker(int n) => $"L1011_RO_GEN_{n}_BREAKER";

    private static string GroundStart(int n) => L1011FoActionExecutor.GroundStart(n);
    private static string LightOff(int n) => L1011FoActionExecutor.EngineLightOff(n);
    private static string IdleWait(int n) => L1011FoActionExecutor.EngineIdle(n);
    private const string Generators = L1011FoActionExecutor.EngineGenerators;

    private List<(FakeL1011IoKind Kind, string Key)> IoKinds() => _ac.Io.Select(e => (e.Kind, e.Key)).ToList();

    private long WrittenAt(string key, double value) =>
        _ac.Io.First(e => e.Kind == FakeL1011IoKind.Write && e.Key == key && e.Value == value).AtMs;

    private int FreshReadsOf(string key) => _ac.FreshReads.Count(k => k == key);

    /// <summary>The flow's Stop pressed, once, the first time engine <paramref name="n"/>'s N3 reads at least
    /// <paramref name="n3"/>; returns where on the clock it was pressed (-1 until then).</summary>
    private Func<long> StopAt(CancellationTokenSource stop, int n, double n3, Action? alsoThen = null)
    {
        long stoppedAt = -1;
        _ac.OnClock = ac =>
        {
            if (stoppedAt < 0 && ac.Get(N3(n)) >= n3)
            {
                stoppedAt = ac.NowMs;
                stop.Cancel();
                alsoThen?.Invoke();
            }
        };
        return () => stoppedAt;
    }

    // ---- The keys ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Every_engine_start_key_is_a_pseudo_key_and_takes_the_stop_token()
    {
        Assert.Equal("FO_GROUND_START_2", GroundStart(2));
        Assert.Equal("FO_ENGINE_LIGHT_OFF_2", LightOff(2));
        Assert.Equal("FO_ENGINE_IDLE_2", IdleWait(2));
        Assert.Equal("FO_ENGINE_GENERATORS", Generators);
        Assert.Equal(Switch(2), L1011FoActionExecutor.GroundStartKey(2));
        Assert.Equal(Fuel(2), L1011FoActionExecutor.FuelIgnitionKey(2));
        Assert.Equal(N3(2), L1011FoActionExecutor.N3Key(2));
        Assert.Equal(2, L1011FoActionExecutor.RotationRiseN3);
        Assert.Equal(30_000, L1011FoActionExecutor.RotationWaitMs);
        Assert.Equal(20, L1011FoActionExecutor.FuelOnN3);
        Assert.Equal(60_000, L1011FoActionExecutor.LightOffWaitMs);
        Assert.Equal(57, L1011FoActionExecutor.IdleN3);
        Assert.Equal(3_000, L1011FoActionExecutor.IdleHoldMs);
        Assert.Equal(90_000, L1011FoActionExecutor.IdleWaitMs);

        var stopAware = Engines.SelectMany(n => new[] { GroundStart(n), LightOff(n), IdleWait(n) }).ToList();
        var all = stopAware.Append(Generators).ToList();
        Assert.Equal(10, all.Distinct(StringComparer.Ordinal).Count());
        Assert.All(all, key => Assert.True(L1011FoActionExecutor.IsPseudoKey(key), key));
        Assert.All(stopAware, key => Assert.Contains(key, L1011FoActionExecutor.StopAwareKeys));
        Assert.Contains(Generators, L1011FoActionExecutor.PseudoKeys);          // atomic, under the gate
        Assert.DoesNotContain(Generators, L1011FoActionExecutor.StopAwareKeys);

        // The rows the start writes and reads are the definition's own: the switch and the fuel read back, the
        // generator buttons are presses.
        foreach (int n in Engines)
        {
            Assert.True(_ac.CanReadBack(Switch(n)), Switch(n));
            Assert.True(_ac.CanReadBack(Fuel(n)), Fuel(n));
            Assert.True(_ac.CanReadBack(N3(n)), N3(n));
            Assert.False(_ac.CanReadBack(FieldPress(n)), FieldPress(n));
            Assert.False(_ac.CanReadBack(BreakerPress(n)), BreakerPress(n));
            Assert.True(_ac.CanReadBack(Field(n)), Field(n));
            Assert.True(_ac.CanReadBack(Breaker(n)), Breaker(n));
        }

        // Each Stop-aware key takes the flow's Stop: one already asked for writes nothing.
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        foreach (var key in stopAware)
            Assert.False(await _exec.ExecuteStepAsync(Flow(key), stop.Token).WaitAsync(Hang), key);
        Assert.Empty(_ac.Writes);
        Assert.Empty(_spoken);
    }

    // ---- The ground start ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_ground_start_writes_once_and_is_done_when_N3_rises_two_percent()
    {
        Assert.True(await _exec.Set(GroundStart(2), 1));

        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);
        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, N3(2)),       // the N3 the rise is measured from
            (FakeL1011IoKind.FreshRead, Switch(2)),   // the switch, read once
            (FakeL1011IoKind.Write, Switch(2)),       // the row's click
            (FakeL1011IoKind.FreshRead, Switch(2)),   // read back on
        }, IoKinds().Take(4));
        Assert.InRange(_ac.NowMs - WrittenAt(Switch(2), 1), 2_000, 2_000 + Poll);
        Assert.True(_ac.Get(N3(2)) >= Cold + 2);
        Assert.Equal(1, _ac.Get(Switch(2)));   // left on: the aircraft releases it itself
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_ground_start_already_on_is_not_clicked_again_but_the_rotation_is_still_watched()
    {
        _ac.Set(Switch(2), 1);
        using var stop = new CancellationTokenSource();

        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));

        Assert.Empty(_ac.Writes);
        Assert.InRange(_ac.NowMs, 2_000, 2_000 + Poll);   // it waited for the rise
        Assert.True(FreshReadsOf(N3(2)) > 2);

        // Not this run's start (the pilot's own, or an earlier one's): the run's Stop leaves it.
        stop.Cancel();
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();
        Assert.Empty(_ac.Writes);
        Assert.Equal(0, FreshReadsOf(Fuel(2)));
    }

    [Fact]
    public async Task A_ground_start_on_a_running_engine_writes_nothing_and_is_done()
    {
        _ac.Set(N3(2), Idle);

        Assert.True(await _exec.Set(GroundStart(2), 1));

        Assert.Empty(_ac.Writes);
        Assert.Equal(new[] { N3(2) }, _ac.FreshReads);   // never a starter on a running engine
        Assert.Equal(0, _ac.NowMs);
    }

    [Theory]
    [InlineData(0)]   // never a starter turned on by a target that asks for off
    [InlineData(2)]
    public async Task A_ground_start_to_any_other_target_reads_and_writes_nothing(int target)
    {
        using var stop = new CancellationTokenSource();

        Assert.False(await _exec.ExecuteStepAsync(Flow(GroundStart(2), target), stop.Token));

        Assert.Empty(_ac.Io);
        Assert.Contains(_logged, line => line.StartsWith(GroundStart(2), StringComparison.Ordinal) && line.Contains("1 (on)"));
    }

    [Fact]
    public async Task A_ground_start_with_its_N3_unread_writes_nothing_and_is_not_done()
    {
        _ac.Unset(N3(2));

        Assert.False(await _exec.Set(GroundStart(2), 1));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.Contains(N3(2)) && line.Contains("unread"));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(false)]   // a hand tick: nothing recorded
    [InlineData(true)]    // a flow step: its recorded start taken out by the watch
    public async Task No_rotation_in_thirty_seconds_releases_the_switch_over_fuel_off(bool flowStep)
    {
        _ac.GroundStartN3PerSecond = 0;   // the switch on, nothing turning
        using var stop = new CancellationTokenSource();

        bool done = flowStep
            ? await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token)
            : await _exec.Set(GroundStart(2), 1);

        Assert.False(done);
        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0) }, _ac.Writes);
        Assert.InRange(WrittenAt(Switch(2), 0) - WrittenAt(Switch(2), 1), L1011FoActionExecutor.RotationWaitMs,
            L1011FoActionExecutor.RotationWaitMs + Poll);
        var io = IoKinds();
        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, Fuel(2)),     // the fuel read first: off
            (FakeL1011IoKind.FreshRead, Switch(2)),   // the switch, before its click
            (FakeL1011IoKind.Write, Switch(2)),       // the row's own click to off releases the starter
            (FakeL1011IoKind.FreshRead, Switch(2)),   // read back off
        }, io.Skip(io.Count - 4));
        Assert.Equal(0, _ac.Get(Switch(2)));
        Assert.Contains(_logged, line => line.Contains("30000 ms"));
        Assert.DoesNotContain(_ac.Writes, w => w.Key == Fuel(2));   // never a fuel write

        // Released once: the run's Stop finds nothing left.
        stop.Cancel();
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();
        Assert.Equal(1, FreshReadsOf(Fuel(2)));
        Assert.Equal(2, _ac.Writes.Count);
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_ground_start_switch_that_cannot_be_read_is_never_clicked()
    {
        _ac.Unset(Switch(2));
        using var stop = new CancellationTokenSource();

        Assert.False(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));

        Assert.Empty(_ac.Writes);
        Assert.Contains(_logged, line => line.StartsWith(GroundStart(2), StringComparison.Ordinal)
            && line.Contains(Switch(2)) && line.Contains("unread"));
        stop.Cancel();
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(0, FreshReadsOf(Fuel(2)));   // nothing clicked: nothing recorded, nothing to release
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(false)]   // a hand tick
    [InlineData(true)]    // a flow step
    public async Task A_ground_start_click_that_never_reads_on_is_not_done_and_its_release_reads_the_switch(bool flowStep)
    {
        _ac.NeverDeliver(Switch(2));   // sent, but it never reads on
        using var stop = new CancellationTokenSource();

        bool done = flowStep
            ? await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token)
            : await _exec.Set(GroundStart(2), 1);

        Assert.False(done);
        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);   // it still reads off: nothing more sent
        var io = IoKinds();
        Assert.Equal(new[]
        {
            (FakeL1011IoKind.FreshRead, Fuel(2)),     // the release ran: the fuel off
            (FakeL1011IoKind.FreshRead, Switch(2)),   // and the switch read before any click
        }, io.Skip(io.Count - 2));
        Assert.Contains(_logged, line => line.Contains("engine 2") && line.Contains("already reads off"));
        Assert.DoesNotContain(_logged, line => line.Contains("read back off"));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();   // released once: nothing recorded is left
        _exec.ReleaseHeldPresses();
        Assert.Equal(1, FreshReadsOf(Fuel(2)));
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_ground_start_click_that_lands_after_its_read_back_is_released(bool flowStep)
    {
        // The click goes out, reads on only after the 4 s read-back gave up, and has landed by the time the
        // release reads the fuel: the starter it engaged is released.
        var io = new LiveTimingTransport(_ac);
        io.LandLate(Switch(2), landsOnReadOf: Fuel(2));
        _exec.SetTransport(io);
        using var stop = new CancellationTokenSource();

        bool done = flowStep
            ? await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token).WaitAsync(Hang)
            : await _exec.Set(GroundStart(2), 1).WaitAsync(Hang);

        Assert.False(done);
        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0) }, _ac.Writes);
        Assert.Equal(0, _ac.Get(Switch(2)));
        Assert.Contains(_logged, line => line.Contains("engine 2") && line.Contains("released and read back off"));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();
        Assert.Equal(1, FreshReadsOf(Fuel(2)));
        Assert.Equal(2, _ac.Writes.Count);
    }

    [Fact]
    public async Task A_release_never_clicks_a_ground_start_switch_that_cannot_be_read()
    {
        _ac.GroundStartN3PerSecond = 0;   // no rotation: the watch gives up at 30 s
        _ac.OnClock = ac => { if (ac.NowMs >= 10_000) ac.Unset(Switch(2)); };

        Assert.False(await _exec.Set(GroundStart(2), 1));

        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);   // no blind click to off
        Assert.Contains(_logged, line => line.Contains("engine 2") && line.Contains(Switch(2)) && line.Contains("unread"));
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_ground_start_logs_its_N3_invariantly_under_a_comma_decimal_culture(string culture)
    {
        // Nothing here is spoken; the N3 the rise is measured from is in debug.log.
        _ac.GroundStartN3PerSecond = 0;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.False(await _exec.Set(GroundStart(2), 1));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Contains(_logged, line => line.Contains(N3(2)) && line.Contains("1.6"));
        Assert.DoesNotContain(_logged, line => line.Contains("1,6") || line.Contains("3,6"));
    }

    // ---- The light-off watch -------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_light_off_watch_is_done_at_twenty_percent()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));

        Assert.True(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token));

        Assert.True(_ac.Get(N3(2)) >= L1011FoActionExecutor.FuelOnN3);
        Assert.InRange(_ac.NowMs - WrittenAt(Switch(2), 1), 18_400, 18_400 + Poll);   // 1.6 % at 1 %/s
        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);

        // Already there (a resumed flow): done at once.
        long now = _ac.NowMs;
        Assert.True(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token));
        Assert.Equal(now, _ac.NowMs);
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task The_light_off_watch_is_false_on_stop_and_the_stop_clean_up_releases_the_switch()
    {
        // The watch ends on the Stop and leaves its run's start to the clean-up FlowManager runs after the step
        // (or to the window's close): it never takes the start itself.
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        var stoppedAt = StopAt(stop, 2, 10);

        Assert.False(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token).WaitAsync(Hang));

        Assert.InRange(_ac.NowMs - stoppedAt(), 0, Poll);   // within a poll of the Stop
        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.StartsWith(LightOff(2), StringComparison.Ordinal)
            && line.Contains("left to the stop clean-up"));

        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0) }, _ac.Writes);
        Assert.Equal(0, _ac.Get(Switch(2)));
        Assert.True(_ac.Get(N3(2)) < 11);                   // no longer turning

        // The release went out once: the clean-up again, and the window's close, find nothing.
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();
        Assert.Equal(1, FreshReadsOf(Fuel(2)));
        Assert.Equal(2, _ac.Writes.Count);
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task The_light_off_watch_releases_once_when_the_window_closes_mid_watch()
    {
        // The window closes during the watch: the flow is cancelled, then its held presses released.
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        StopAt(stop, 2, 10, _exec.ReleaseHeldPresses);

        Assert.False(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token).WaitAsync(Hang));
        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0) }, _ac.Writes);
        Assert.Equal(0, FreshReadsOf(Fuel(2)));   // the close released it over the cache; nothing released it again
    }

    [Fact]
    public async Task The_light_off_watch_releases_the_switch_after_sixty_seconds_without_twenty_percent()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        _ac.NeverDeliver(N3(2));   // stuck short of 20 %
        long watchFrom = _ac.NowMs;

        Assert.False(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token));

        Assert.InRange(WrittenAt(Switch(2), 0) - watchFrom, L1011FoActionExecutor.LightOffWaitMs,
            L1011FoActionExecutor.LightOffWaitMs + Poll);
        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0) }, _ac.Writes);
        Assert.Contains(_logged, line => line.Contains("60000 ms"));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();
        Assert.Equal(1, FreshReadsOf(Fuel(2)));
    }

    [Theory]
    [InlineData(true)]    // the run's own start
    [InlineData(false)]   // a switch already on
    public async Task A_stop_with_fuel_on_never_releases_or_cuts(bool runsOwnStart)
    {
        using var stop = new CancellationTokenSource();
        if (runsOwnStart)
            Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        else
            _ac.Set(Switch(2), 1);
        _ac.Set(Fuel(2), 1);   // the pilot's own fuel and ignition
        int writesBefore = _ac.Writes.Count;
        StopAt(stop, 2, 10);

        Assert.False(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token).WaitAsync(Hang));
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();

        Assert.Equal(writesBefore, _ac.Writes.Count);   // no write at all
        Assert.Equal(1, _ac.Get(Switch(2)));
        Assert.Equal(1, _ac.Get(Fuel(2)));
        Assert.Contains(_logged, line => line.Contains("fuel on, the start runs on"));
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task A_stop_with_fuel_unread_leaves_the_switch_logs_and_tells_the_Captain()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        _ac.Unset(Fuel(2));
        StopAt(stop, 2, 10);

        Assert.False(await _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token).WaitAsync(Hang));
        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);
        Assert.Equal(1, _ac.Get(Switch(2)));
        Assert.Contains(_logged, line => line.Contains(Fuel(2)) && line.Contains("unread"));
        Assert.Empty(_spoken);   // never spoken here: the flow's end speaks it, after its own sentence
        Assert.Equal(StillOn2, _exec.TakeStoppedRunNotice());
    }

    /// <summary>The stopped run's notice for engine 2's start the executor took and could not release.</summary>
    private const string StillOn2 = "Engine 2 ground start switch still on: release it.";

    /// <summary>
    /// The Stop clean-up took the run's start but could not release it: its fuel and ignition unread, its switch
    /// unread, or its click to off refused or never reading off. The starter may still turn a dry engine, which the
    /// Captain cannot see, so the run's notice says so, once; the executor speaks nothing itself (FlowManager queues
    /// the notice after the run's end sentence).
    /// </summary>
    [Theory]
    [InlineData("fuel unread")]
    [InlineData("switch unread")]
    [InlineData("click refused")]
    [InlineData("click never reads off")]
    public async Task A_stop_clean_up_that_cannot_release_the_ground_start_leaves_one_notice(string why)
    {
        _exec.AttachFlowProgressSpeech(() => false);
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        switch (why)
        {
            case "fuel unread": _ac.Unset(Fuel(2)); break;
            case "switch unread": _ac.Unset(Switch(2)); break;
            case "click refused": _ac.Refuse(Switch(2)); break;
            case "click never reads off": _ac.NeverDeliver(Switch(2)); break;
        }
        stop.Cancel();

        await _exec.OnFlowStoppedAsync().WaitAsync(Hang);
        await _exec.OnFlowStoppedAsync().WaitAsync(Hang);   // taken once: nothing left to take
        _exec.ReleaseHeldPresses();

        Assert.Empty(_spoken);
        Assert.Equal(StillOn2, _exec.TakeStoppedRunNotice());
        Assert.Null(_exec.TakeStoppedRunNotice());          // taken once, then cleared
        Assert.DoesNotContain(_ac.Writes, w => w.Key == Fuel(2));   // never a fuel write
        if (why != "switch unread")
            Assert.Equal(1, _ac.Get(Switch(2)));
    }

    /// <summary>A start the clean-up released, one already off and one whose fuel and ignition are on (the start runs
    /// on) leave no notice.</summary>
    [Theory]
    [InlineData("released")]
    [InlineData("already off")]
    [InlineData("fuel on")]
    public async Task A_stop_clean_up_leaves_no_notice_when_the_ground_start_is_released_or_needs_no_release(string how)
    {
        _exec.AttachFlowProgressSpeech(() => true);
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        if (how == "already off")
            _ac.Set(Switch(2), 0);
        if (how == "fuel on")
            _ac.Set(Fuel(2), 1);
        stop.Cancel();

        await _exec.OnFlowStoppedAsync().WaitAsync(Hang);

        Assert.Equal(how == "fuel on" ? 1 : 0, _ac.Get(Switch(2)));
        Assert.Equal(how == "released", _ac.Writes.Contains((Switch(2), 0.0)));
        Assert.Null(_exec.TakeStoppedRunNotice());
        Assert.Empty(_spoken);
    }

    /// <summary>The window's close takes a stopped run's start and releases it over the cache: a cache that leaves it
    /// unread, or a click refused, leaves the notice; fuel on, a switch already off and a click sent leave none.</summary>
    [Theory]
    [InlineData("fuel unread", true)]
    [InlineData("switch unread", true)]
    [InlineData("click refused", true)]
    [InlineData("fuel on", false)]
    [InlineData("already off", false)]
    [InlineData("released", false)]
    public async Task The_windows_close_leaves_a_notice_only_for_a_start_it_could_not_release(string how, bool notice)
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        switch (how)
        {
            case "fuel unread": _ac.Unset(Fuel(2)); break;
            case "switch unread": _ac.Unset(Switch(2)); break;
            case "click refused": _ac.Refuse(Switch(2)); break;
            case "fuel on": _ac.Set(Fuel(2), 1); break;
            case "already off": _ac.Set(Switch(2), 0); break;
        }
        stop.Cancel();

        _exec.ReleaseHeldPresses();

        Assert.Equal(notice ? StillOn2 : null, _exec.TakeStoppedRunNotice());
        Assert.Equal(how == "released", _ac.Writes.Contains((Switch(2), 0.0)));
        Assert.Empty(_spoken);
    }

    /// <summary>A flow step's watch that times out releases its own start; one it cannot release (fuel unread) leaves the
    /// notice for that run's end. A hand tick's has no run to end, so it leaves none.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_timed_out_watch_that_cannot_release_leaves_a_notice_only_for_a_flow_step(bool flowStep)
    {
        _ac.GroundStartN3PerSecond = 0;   // the switch on, nothing turning: the watch gives up at 30 s
        _ac.OnClock = ac => { if (ac.NowMs >= 10_000) ac.Unset(Fuel(2)); };
        using var stop = new CancellationTokenSource();

        bool done = flowStep
            ? await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token).WaitAsync(Hang)
            : await _exec.Set(GroundStart(2), 1).WaitAsync(Hang);

        Assert.False(done);
        Assert.Equal(new[] { (Switch(2), 1.0) }, _ac.Writes);   // fuel unread: no click to off
        Assert.Equal(flowStep ? StillOn2 : null, _exec.TakeStoppedRunNotice());
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public async Task The_still_on_sentence_names_each_engine_once_under_a_comma_decimal_culture(string culture)
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(3)), stop.Token));
        _ac.Unset(Fuel(2));
        _ac.Unset(Fuel(3));
        stop.Cancel();
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            await _exec.OnFlowStoppedAsync().WaitAsync(Hang);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }

        Assert.Empty(_spoken);
        Assert.Equal(StillOn2 + " Engine 3 ground start switch still on: release it.", _exec.TakeStoppedRunNotice());
    }

    // ---- The idle wait ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_idle_wait_needs_three_seconds_above_fifty_seven()
    {
        // 56.5 for 5 s, then 59.2: done 3 s after it first reads above.
        _ac.Set(N3(2), 56.5);
        _ac.OnClock = ac => ac.Set(N3(2), ac.NowMs < 5_000 ? 56.5 : Idle);
        using var stop = new CancellationTokenSource();

        Assert.True(await _exec.ExecuteStepAsync(Flow(IdleWait(2)), stop.Token));

        Assert.InRange(_ac.NowMs, 8_000, 8_000 + Poll);
        Assert.Empty(_ac.Writes);
    }

    [Fact]
    public async Task The_idle_wait_restarts_its_hold_on_every_sample_below_and_gives_up_after_ninety_seconds()
    {
        // 58 and 56 a second each: never 3 s above.
        _ac.Set(N3(2), 58);
        _ac.OnClock = ac => ac.Set(N3(2), ac.NowMs / 1_000 % 2 == 0 ? 58 : 56);
        using var stop = new CancellationTokenSource();

        Assert.False(await _exec.ExecuteStepAsync(Flow(IdleWait(2)), stop.Token));

        Assert.InRange(_ac.NowMs, L1011FoActionExecutor.IdleWaitMs, L1011FoActionExecutor.IdleWaitMs + Poll);
        Assert.Contains(_logged, line => line.StartsWith(IdleWait(2), StringComparison.Ordinal) && line.Contains("90000 ms"));
        Assert.Empty(_ac.Writes);
        Assert.Empty(_spoken);
    }

    [Fact]
    public async Task The_idle_wait_restarts_its_hold_after_a_pause()
    {
        // The engine is not watched while the flow is paused, so the hold starts again once it resumes.
        _ac.Set(N3(2), Idle);
        bool paused = false;
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => paused ? resume.Task.WaitAsync(ct) : Task.CompletedTask);
        _ac.OnClock = ac => paused |= ac.NowMs >= 1_000;
        using var stop = new CancellationTokenSource();

        var wait = _exec.ExecuteStepAsync(Flow(IdleWait(2)), stop.Token);
        Assert.False(wait.IsCompleted);   // paused a second into its hold

        await _ac.DelayAsync(60_000);
        long resumedAt = _ac.NowMs;
        paused = false;
        _ac.OnClock = null;
        resume.SetResult();

        Assert.True(await wait.WaitAsync(Hang));
        Assert.InRange(_ac.NowMs - resumedAt, L1011FoActionExecutor.IdleHoldMs, L1011FoActionExecutor.IdleHoldMs + Poll);
    }

    [Fact]
    public async Task The_idle_wait_is_false_on_stop_and_writes_nothing()
    {
        _ac.Set(N3(2), 40);
        using var stop = new CancellationTokenSource();
        _ac.OnClock = ac => { if (ac.NowMs >= 5_000) stop.Cancel(); };

        Assert.False(await _exec.ExecuteStepAsync(Flow(IdleWait(2)), stop.Token).WaitAsync(Hang));

        Assert.InRange(_ac.NowMs, 5_000, 5_000 + Poll);
        Assert.Empty(_ac.Writes);
        Assert.Equal(0, FreshReadsOf(Fuel(2)));
    }

    // ---- The flow's Stop clean-up and the window's close -------------------------------------------------------------------

    [Fact]
    public async Task OnFlowStopped_releases_only_this_runs_recorded_start_and_once()
    {
        // A checklist tick's start is never recorded; of two runs, only the stopped one's start is released.
        Assert.True(await _exec.Set(GroundStart(1), 1));
        using var stoppedRun = new CancellationTokenSource();
        using var runningRun = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stoppedRun.Token));
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(3)), runningRun.Token));

        stoppedRun.Cancel();
        await _exec.OnFlowStoppedAsync();

        Assert.Equal(new[] { (Switch(1), 1.0), (Switch(2), 1.0), (Switch(3), 1.0), (Switch(2), 0.0) }, _ac.Writes);
        Assert.Equal(1, _ac.Get(Switch(1)));
        Assert.Equal(0, _ac.Get(Switch(2)));
        Assert.Equal(1, _ac.Get(Switch(3)));

        // Once: the clean-up again, and the window's close, find nothing of the stopped run's.
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();
        Assert.Equal(1, FreshReadsOf(Fuel(2)));
        Assert.Equal(0, FreshReadsOf(Fuel(1)));
        Assert.Equal(0, FreshReadsOf(Fuel(3)));
        Assert.Equal(4, _ac.Writes.Count);
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(true, "detached")]                 // the window closed: its close released what it could
    [InlineData(false, "cannot be reached")]
    public async Task OnFlowStopped_detached_or_unreachable_writes_nothing_and_logs(bool detached, string logged)
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(3)), stop.Token));
        stop.Cancel();

        if (detached)
            _exec.SetTransport(null);
        else
            _ac.Ready = false;
        await _exec.OnFlowStoppedAsync();
        _exec.SetTransport(_ac);
        _ac.Ready = true;
        await _exec.OnFlowStoppedAsync();   // nor is it released later

        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(3), 1.0) }, _ac.Writes);
        Assert.Equal(0, FreshReadsOf(Fuel(2)));
        Assert.Contains(_logged, line => line.Contains("engine 2") && line.Contains(logged));
        Assert.Contains(_logged, line => line.Contains("engine 3") && line.Contains(logged));
        Assert.Empty(_spoken);
        Assert.Null(_exec.TakeStoppedRunNotice());
    }

    [Theory]
    [InlineData(0.0, 1.0, true)]
    [InlineData(1.0, 1.0, false)]            // fuel on: the start runs on
    [InlineData(0.0, 0.0, false)]            // already off
    [InlineData(double.NaN, 1.0, false)]     // fuel unread
    [InlineData(0.0, double.NaN, false)]     // the switch unread
    public void ReleaseHeldPresses_releases_over_the_cache_only_with_fuel_off_and_the_switch_on(double fuel, double groundStart, bool release)
        => Assert.Equal(release, L1011FoActionExecutor.ShouldReleaseGroundStart(
            double.IsNaN(fuel) ? null : fuel, double.IsNaN(groundStart) ? null : groundStart));

    [Fact]
    public async Task The_windows_close_releases_a_stopped_runs_start_once_with_no_read_back()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        stop.Cancel();   // the window's close cancels the flow first
        int ioBefore = _ac.Io.Count;

        _exec.ReleaseHeldPresses();

        var after = _ac.Io.Skip(ioBefore).ToList();
        var release = Assert.Single(after);   // over the cache: nothing read fresh, nothing read back
        Assert.Equal((FakeL1011IoKind.Write, Switch(2), (double?)0), (release.Kind, release.Key, release.Value));
        Assert.Equal(0, _ac.Get(Switch(2)));

        await _exec.OnFlowStoppedAsync();   // the run's own clean-up, later: nothing left
        _exec.ReleaseHeldPresses();
        Assert.Single(_ac.Io.Skip(ioBefore));
    }

    [Fact]
    public async Task The_windows_close_logs_what_its_release_came_to()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(3)), stop.Token));
        _ac.Refuse(Switch(3));
        stop.Cancel();

        _exec.ReleaseHeldPresses();

        Assert.Contains(_logged, line => line.Contains("engine 2") && line.Contains("release: Sent"));
        Assert.Contains(_logged, line => line.Contains("engine 3") && line.Contains("release: Refused")
            && line.Contains(FakeL1011Aircraft.RefusalReason));
    }

    /// <summary>The window's close: the flow cancelled, the held presses released, the First Officer detached, then
    /// the aircraft gone (an aircraft switch, an app exit), with a paused watch resuming inside the cancel.</summary>
    private void CloseWindowWithTheWatchResumingFirst(CancellationTokenSource stop) =>
        WithTheWatchResumingFirst(() =>
        {
            stop.Cancel();
            _exec.ReleaseHeldPresses();
            _exec.SetTransport(null);
            _ac.Ready = false;
        });

    [Fact]
    public async Task A_window_closing_on_a_paused_light_off_watch_releases_once_over_the_cache()
    {
        // The cancel ends the Pause at once, and the watch may get there before the window's close. It must leave
        // the start to the close, which releases it over the cache while the aircraft still takes writes: a watch
        // that took it would find the aircraft gone before its click.
        var io = new LiveTimingTransport(_ac) { YieldOnRead = true };
        _exec.SetTransport(io);
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token).WaitAsync(Hang));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => held.Task.WaitAsync(ct));
        var watch = _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token);
        Assert.False(watch.IsCompleted);   // paused
        int ioBefore = _ac.Io.Count;

        CloseWindowWithTheWatchResumingFirst(stop);

        Assert.False(await watch.WaitAsync(Hang));
        await _exec.OnFlowStoppedAsync();   // the run's clean-up, after the step: detached, nothing left
        var release = Assert.Single(_ac.Io.Skip(ioBefore));   // one click, over the cache: nothing read fresh
        Assert.Equal((FakeL1011IoKind.Write, Switch(2), (double?)0), (release.Kind, release.Key, release.Value));
        Assert.Equal(0, _ac.Get(Switch(2)));   // the starter does not stay on
    }

    [Fact]
    public async Task A_window_closing_while_the_gate_is_busy_releases_once_over_the_cache()
    {
        // A hand tick holds the gate when the window closes. A watch that took the start and then waited for the gate
        // would leave the close nothing to release, and then find the First Officer detached.
        var io = new LiveTimingTransport(_ac);
        _exec.SetTransport(io);
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _exec.AttachFlowPause(ct => held.Task.WaitAsync(ct));
        var watch = _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token);
        Assert.False(watch.IsCompleted);
        const string ContinuousIgnition = "SWITCH_CONT_IGNITION";
        _ac.Set(ContinuousIgnition, 0);
        var door = io.HoldRead(ContinuousIgnition);
        var tick = _exec.Set(ContinuousIgnition, 1);   // holds the gate, reading first, until the door opens
        Assert.False(tick.IsCompleted);

        WithTheWatchResumingFirst(() =>
        {
            stop.Cancel();
            _exec.ReleaseHeldPresses();
            _exec.SetTransport(null);
        });
        door.SetResult();

        Assert.True(await tick.WaitAsync(Hang));
        Assert.False(await watch.WaitAsync(Hang));
        await _exec.OnFlowStoppedAsync();
        // The close's click goes out at once, without the gate, while the tick is still reading.
        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0), (ContinuousIgnition, 1.0) }, _ac.Writes);
        Assert.Equal(0, _ac.Get(Switch(2)));
        Assert.Equal(0, FreshReadsOf(Fuel(2)));   // released over the cache, once
    }

    [Fact]
    public async Task A_timed_out_watch_takes_its_start_only_once_it_holds_the_gate()
    {
        // No Stop: the light-off watch times out while a hand tick holds the gate, and the window closes before the
        // gate frees. The close takes the start and releases it; the watch, once it has the gate, finds it gone.
        var io = new LiveTimingTransport(_ac);
        _exec.SetTransport(io);
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        _ac.NeverDeliver(N3(2));   // stuck short of 20 %: the watch times out
        const string ContinuousIgnition = "SWITCH_CONT_IGNITION";
        _ac.Set(ContinuousIgnition, 0);
        var door = io.HoldRead(ContinuousIgnition);
        var tick = _exec.Set(ContinuousIgnition, 1);   // holds the gate, reading first, until the door opens
        Assert.False(tick.IsCompleted);

        var watch = _exec.ExecuteStepAsync(Flow(LightOff(2)), stop.Token);   // times out at once on the fake's clock
        Assert.False(watch.IsCompleted);                                       // waiting for the gate
        stop.Cancel();
        _exec.ReleaseHeldPresses();
        door.SetResult();

        Assert.True(await tick.WaitAsync(Hang));
        Assert.False(await watch.WaitAsync(Hang));
        Assert.Equal(new[] { (Switch(2), 1.0), (Switch(2), 0.0), (ContinuousIgnition, 1.0) }, _ac.Writes);
        Assert.Equal(0, FreshReadsOf(Fuel(2)));   // the close's release, over the cache; none from the watch
        Assert.Contains(_logged, line => line.StartsWith(LightOff(2), StringComparison.Ordinal) && line.Contains("already released"));
    }

    [Theory]
    [InlineData("fuel on")]
    [InlineData("unreachable")]
    [InlineData("a tick's start")]
    [InlineData("a running run's start")]
    public async Task The_windows_close_leaves_a_start_it_must_not_release(string why)
    {
        using var stop = new CancellationTokenSource();
        if (why == "a tick's start")
            Assert.True(await _exec.Set(GroundStart(2), 1));
        else
            Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        if (why == "fuel on")
            _ac.Set(Fuel(2), 1);
        if (why == "unreachable")
            _ac.Ready = false;
        if (why != "a running run's start")
            stop.Cancel();
        int ioBefore = _ac.Io.Count;

        _exec.ReleaseHeldPresses();

        Assert.Empty(_ac.Io.Skip(ioBefore));
        Assert.Equal(1, _ac.Get(Switch(2)));
    }

    [Fact]
    public async Task Fuel_on_forgets_the_recorded_start()
    {
        using var stop = new CancellationTokenSource();
        Assert.True(await _exec.ExecuteStepAsync(Flow(GroundStart(2)), stop.Token));
        Assert.True(await _exec.ExecuteStepAsync(Flow(Fuel(2)), stop.Token));   // the toggle, read first
        int fuelReads = FreshReadsOf(Fuel(2));

        stop.Cancel();
        await _exec.OnFlowStoppedAsync();
        _exec.ReleaseHeldPresses();

        Assert.Equal(new[] { (Switch(2), 1.0), (Fuel(2), 1.0) }, _ac.Writes);
        Assert.Equal(fuelReads, FreshReadsOf(Fuel(2)));   // nothing left to release: not even read
        Assert.Equal(1, _ac.Get(Fuel(2)));
    }

    // ---- The engine generators ------------------------------------------------------------------------------------------

    private void SeedGenerators(double n3 = Idle)
    {
        foreach (int n in Engines)
        {
            _ac.Set(N3(n), n3);
            _ac.Set(Field(n), 0);
            _ac.Set(Breaker(n), 0);
        }
    }

    [Theory]
    [InlineData("", "", new[] { "SWITCH_BRG_GEN_1", "SWITCH_BRG_GEN_2", "SWITCH_BRG_GEN_3", "SWITCH_GEN_FLOW_1", "SWITCH_GEN_FLOW_2", "SWITCH_GEN_FLOW_3" })]
    // Live: an engine's field relay closed by itself at idle, before any press.
    [InlineData("2", "3", new[] { "SWITCH_BRG_GEN_1", "SWITCH_BRG_GEN_3", "SWITCH_GEN_FLOW_1", "SWITCH_GEN_FLOW_2" })]
    [InlineData("123", "123", new string[0])]
    public async Task Engine_generators_close_three_fields_then_three_breakers_and_skip_closed_relays(
        string fieldsClosed, string breakersClosed, string[] pressed)
    {
        SeedGenerators();
        foreach (char c in fieldsClosed)
            _ac.Set(Field(c - '0'), 1);
        foreach (char c in breakersClosed)
            _ac.Set(Breaker(c - '0'), 1);

        Assert.True(await _exec.Set(Generators, 1));

        Assert.Equal(pressed.Select(k => (k, 1.0)), _ac.Writes);
        foreach (int n in Engines)
        {
            Assert.Equal(1, _ac.Get(Field(n)));
            Assert.Equal(1, _ac.Get(Breaker(n)));
        }
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData("engine 3 not running")]
    [InlineData("breaker 1 unread")]
    public async Task Engine_generators_fail_on_an_engine_not_running_and_never_press_an_unread_relay(string how)
    {
        SeedGenerators();
        string[] pressed;
        if (how == "engine 3 not running")
        {
            _ac.Set(N3(3), Cold);
            // Its field never closes, so its breaker is never pressed; the others still are.
            pressed = new[] { FieldPress(1), FieldPress(2), FieldPress(3), BreakerPress(1), BreakerPress(2) };
        }
        else
        {
            _ac.Unset(Breaker(1));
            pressed = new[] { FieldPress(1), FieldPress(2), FieldPress(3), BreakerPress(2), BreakerPress(3) };
        }

        Assert.False(await _exec.Set(Generators, 1));

        Assert.Equal(pressed.Select(k => (k, 1.0)), _ac.Writes);
        if (how == "engine 3 not running")
        {
            Assert.Equal(0, _ac.Get(Field(3)));
            Assert.Contains(_logged, line => line.Contains(Field(3)) && line.Contains("8000 ms"));
        }
        else
        {
            Assert.Null(_ac.Get(Breaker(1)));
            Assert.Contains(_logged, line => line.Contains(Breaker(1)) && line.Contains("unread"));
        }
        Assert.Empty(_spoken);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Engine_generators_to_any_other_target_writes_nothing(int target)
    {
        SeedGenerators();

        Assert.False(await _exec.Set(Generators, target));

        Assert.Empty(_ac.Io);
        Assert.Contains(_logged, line => line.Contains(Generators));
    }

    [Fact]
    public async Task Engine_generators_run_to_the_end_through_a_flow_step_whose_stop_has_been_asked_for()
    {
        // Pseudo, not Stop-aware: atomic under the gate and bounded.
        SeedGenerators();
        using var stop = new CancellationTokenSource();
        stop.Cancel();

        Assert.True(await _exec.ExecuteStepAsync(Flow(Generators), stop.Token));

        Assert.Equal(6, _ac.Writes.Count);
    }
}
