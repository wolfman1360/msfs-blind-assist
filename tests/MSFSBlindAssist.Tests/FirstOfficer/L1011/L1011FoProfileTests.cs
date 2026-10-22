using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer profile: its title, one evaluator for the whole window, the executor's
/// speech through the announcer's queued call, the transport following the connection, and the Power Up
/// flow with its two checklists.
/// </summary>
public class L1011FoProfileTests
{
    private readonly SpeechCapture _speech = new();

    /// <summary>A definition able to land a write, so the executor's readiness follows the connection alone.</summary>
    private L1011FoProfile Profile() => new(new IniL1011Definition { CanLand = _ => true }, _speech);

    [Fact]
    public void Title_names_the_TriStar()
        => Assert.Equal("First Officer — iniBuilds L-1011", Profile().Title);

    [Fact]
    public void The_evaluator_is_created_once_and_shared()
    {
        var p = Profile();
        var state = p.CreateEvaluator();
        Assert.Same(state, p.CreateEvaluator());
        p.CreateExecutor();
        Assert.Same(state, p.CreateEvaluator());   // creating the executor makes no second one
    }

    [Fact]
    public void The_executor_speaks_through_the_announcers_queued_call()
    {
        var exec = Profile().CreateExecutor();

        Assert.NotNull(exec.Speech);
        exec.Speech!("Standby power check: pass");

        Assert.Equal(new[] { "Standby power check: pass" }, _speech.All);
        Assert.Empty(_speech.Interrupts);
    }

    [Fact]
    public void With_SimConnect_the_executor_reaches_the_aircraft_through_the_definition()
    {
        var p = Profile();
        var exec = p.CreateExecutor();
        Assert.False(exec.IsAvailable);   // no transport until the window connects it

        p.SetExecutorSimConnect(exec, SeededSimConnectCache.With());

        Assert.True(exec.IsAvailable);    // attached: the definition's First Officer entry is ready
    }

    [Fact]
    public void Without_SimConnect_the_executor_is_unavailable()
    {
        var p = Profile();
        var exec = p.CreateExecutor();
        p.SetExecutorSimConnect(exec, SeededSimConnectCache.With());
        Assert.True(exec.IsAvailable);

        p.SetExecutorSimConnect(exec, null);   // disconnected, or the window closing

        Assert.False(exec.IsAvailable);
    }

    [Fact]
    public void The_evaluator_reads_the_connection_it_is_bound_to()
    {
        var p = Profile();
        var state = p.CreateEvaluator();
        Assert.True(double.IsNaN(state.GetValue("SIM_ON_GROUND")));

        p.BindDataManager(state, SeededSimConnectCache.With(("SIM_ON_GROUND", 1)));

        Assert.Equal(1, state.GetValue("SIM_ON_GROUND"));
    }

    /// <summary>The profile builds what the definitions build: the flows so far and their checklists.</summary>
    [Fact]
    public void Builds_the_flows_and_their_checklists()
    {
        var p = Profile();
        Assert.Equal(L1011FoFlowDefinitions.Build().Select(f => f.Id), p.BuildFlows().Select(f => f.Id));
        Assert.Equal(L1011FoChecklistDefinitions.Build().Select(g => g.Id), p.BuildChecklists().Select(g => g.Id));
        Assert.Equal(new[]
            {
                "POWER_UP", "PREFLIGHT", "BEFORE_START", "ENGINE_START", "AFTER_START", "BEFORE_TAKEOFF", "AFTER_TAKEOFF",
                "DESCENT", "BEFORE_LANDING", "AFTER_LANDING",
            }, p.BuildFlows().Select(f => f.Id));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_auto_manager_stores_the_flap_setting(bool autoFlaps)
    {
        var p = Profile();
        var exec = p.CreateExecutor();
        var manager = p.CreateAutoManager(exec, p.CreateEvaluator(), _speech, new UserSettings { FOAutoFlapsEnabled = autoFlaps });

        Assert.IsType<L1011FoAutoManager>(manager);
        Assert.Equal(autoFlaps, manager.AutoFlapsEnabled);
    }

    [Fact]
    public void The_phase_monitor_is_the_TriStars()
    {
        var p = Profile();
        var exec = p.CreateExecutor();
        Assert.IsType<L1011FoFlightPhaseMonitor>(p.CreatePhaseMonitor(exec, p.CreateEvaluator(), _speech));
    }
}
