using System.Runtime.CompilerServices;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The production transport maps the First Officer's needs onto the definition's entry (Tasks 2 to 4)
/// and SimConnect's reads. Against a SimConnectManager that never connected, with the definition's test
/// hooks for its clock, sends and waits, as IniL1011FirstOfficerEntryTests.
/// </summary>
public class L1011DefinitionFoTransportTests
{
    private const string ContinuousIgnition = "SWITCH_CONT_IGNITION";

    private long _now = 10_000;
    private readonly UserSettings _settings = new();
    private readonly List<string> _sent = new();

    /// <summary>A definition on this test's clock, able to land, sending into <see cref="_sent"/>, never attached.</summary>
    private IniL1011Definition Build() => new()
    {
        Clock = () => _now,
        SettingsSource = () => _settings,
        CanLand = _ => true,
        SendCalc = (_, rpn) => _sent.Add(rpn),
        StepDelay = _ => Task.CompletedTask,
        SettleDelay = _ => Task.CompletedTask,
        ReRead = (_, _) => { },
    };

    /// <summary>Attaches with no synchronization context current, whatever the runner's, so every call runs inline.</summary>
    private static void AttachWithoutContext(IniL1011Definition def, SimConnectManager sim)
    {
        var previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(null);
            def.Attach(sim);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    [Fact]
    public async Task Without_attach_it_is_not_ready_and_every_write_is_unavailable()
    {
        var def = Build();
        var io = new L1011DefinitionFoTransport(def, SeededSimConnectCache.With((ContinuousIgnition, 0)));

        Assert.False(io.Ready);
        Assert.Equal(L1011FoResult.Unavailable, await io.SetAsync(ContinuousIgnition, 1));
        Assert.Equal(L1011FoResult.Unavailable, await io.SetAltimetersAsync(1013.25));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task When_the_write_cannot_land_it_is_not_ready_and_every_write_is_unavailable()
    {
        var def = Build();
        var sim = SeededSimConnectCache.With((ContinuousIgnition, 0));
        AttachWithoutContext(def, sim);
        def.CanLand = _ => false;
        var io = new L1011DefinitionFoTransport(def, sim);

        Assert.False(io.Ready);
        Assert.Equal(L1011FoResult.Unavailable, await io.SetAsync(ContinuousIgnition, 1));
        Assert.Equal(L1011FoResult.Unavailable, await io.SetAltimetersAsync(1013.25));
        Assert.Empty(_sent);
    }

    [Fact]
    public async Task Attached_and_able_to_land_it_writes_through_the_definitions_entry()
    {
        var def = Build();
        var sim = SeededSimConnectCache.With((ContinuousIgnition, 0));
        AttachWithoutContext(def, sim);
        var io = new L1011DefinitionFoTransport(def, sim);

        Assert.True(io.Ready);
        Assert.Equal(L1011FoResult.Sent, await io.SetAsync(ContinuousIgnition, 1));
        Assert.Equal(L1011FoResult.Sent, await io.SetAltimetersAsync(1013.25));

        Assert.Equal(2, _sent.Count);
        Assert.Contains("SWITCH_CONT_IGNITION", _sent[0], StringComparison.Ordinal);
        Assert.Equal(L1011Levers.AllAltimetersRpn(1013.25), _sent[1]);
    }

    [Fact]
    public async Task A_toggle_from_an_unread_position_is_refused_through_the_transport()
    {
        var def = Build();
        var sim = SeededSimConnectCache.With();
        AttachWithoutContext(def, sim);
        var io = new L1011DefinitionFoTransport(def, sim);

        var result = await io.SetAsync("TOGGLE_CUTOFF_ENG_1", 1);

        Assert.Equal(L1011FoResult.Refused(L1011WritePlan.UnknownPositionRefusal), result);
        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData(ContinuousIgnition, true)]      // a switch row
    [InlineData("L1011_PARKING_BRAKE", true)]   // a lever
    [InlineData("LAMP_STBY_UNARM_LIGHT", true)] // a lamp
    [InlineData("L1011_RO_N3_1", true)]         // a readout
    [InlineData("SWITCH_APU_START", false)]     // a press: never registered
    public void It_can_read_back_exactly_what_the_definition_can(string key, bool canReadBack)
    {
        var io = new L1011DefinitionFoTransport(Build(), SeededSimConnectCache.With());

        Assert.Equal(canReadBack, io.CanReadBack(key));
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_cachedVariables")]
    private static extern ref Dictionary<string, SimVarDefinition>? CachedVariables(BaseAircraftDefinition def);

    [Fact]
    public void Constructing_it_builds_the_variable_table_on_the_constructing_thread()
    {
        // CanReadBack is not marshalled and reads the lazily built table: the transport is built on the
        // UI thread, so the table is built there, never by a pool-thread read-back question.
        var def = Build();
        Assert.Null(CachedVariables(def));

        _ = new L1011DefinitionFoTransport(def, SeededSimConnectCache.With());

        Assert.NotNull(CachedVariables(def));
    }

    [Fact]
    public void A_cached_read_is_simconnects_cache()
    {
        // ReadFreshAsync is not called here: SimConnectManager.ReadFreshAsync's body needs the SimConnect
        // assembly, which the test output does not carry (as for the MD-11's transport).
        var io = new L1011DefinitionFoTransport(Build(), SeededSimConnectCache.With((ContinuousIgnition, 1)));

        Assert.Equal(1, io.ReadCached(ContinuousIgnition));
        Assert.Null(io.ReadCached("LAMP_STBY_UNARM_LIGHT"));
    }

    [Fact]
    public void A_lamp_mute_reaches_the_definitions_lamp_gate()
    {
        var def = Build();
        var sim = SeededSimConnectCache.With();
        AttachWithoutContext(def, sim);
        var io = new L1011DefinitionFoTransport(def, sim);
        var speech = new SpeechCapture();
        def.ProcessSimVarUpdate("LAMP_STBY_UNARM_LIGHT", 0, speech);   // baseline
        _now += 1_000;
        def.OnContinuousBatchDelivered(1);

        io.MuteLampSpeech(10_000);
        def.ProcessSimVarUpdate("LAMP_STBY_UNARM_LIGHT", 1, speech);
        _now += L1011LampGate.SettleMs + 1;
        def.OnContinuousBatchDelivered(1);

        Assert.Empty(speech.All);

        _now += 10_000;   // the mute has run out: the next change is news again
        def.ProcessSimVarUpdate("LAMP_STBY_UNARM_LIGHT", 0, speech);
        _now += L1011LampGate.SettleMs + 1;
        def.OnContinuousBatchDelivered(1);
        Assert.Equal(new[] { "Standby power unarmed light off" }, speech.All);
    }

    [Fact]
    public async Task Its_clock_is_the_monotonic_tick_count_and_its_wait_completes()
    {
        var io = new L1011DefinitionFoTransport(Build(), SeededSimConnectCache.With());

        long before = Environment.TickCount64;
        long now = io.NowMs;
        long after = Environment.TickCount64;
        await io.DelayAsync(1).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.InRange(now, before, after);
    }
}
