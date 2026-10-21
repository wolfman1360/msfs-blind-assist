using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;
using MSFSBlindAssist.Tests.FirstOfficer;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// The universal auto-engage (<c>UniversalAutomationService</c>) on the TriStar: closed-loop on the two
/// engage paddles, pressing paddle A through <see cref="IniL1011Definition.FoSetAsync"/> only when both
/// are READ and neither is at Command. The stock AUTOPILOT_ON it used to send is ignored by the
/// paddle-driven autopilot, which left the service announcing "Autopilot engaged" over no press at all.
/// Paddle positions: 0 Command, 1 CWS, 2 Off. The definition is never attached by the test: the engage
/// attaches it, as MainForm's call does.
/// </summary>
public class IniL1011AutopilotEngageTests
{
    private const string PaddleA = "SWITCH_AFCS_AP_A";
    private const string PaddleB = "SWITCH_AFCS_AP_B";

    private const string CwsA = "1 (>L:SWITCH_AFCS_AP_A) (>H:AUTOPILOT_SWITCH_AFCS_AP_A)";
    private const string CommandA = "0 (>L:SWITCH_AFCS_AP_A) (>H:AUTOPILOT_SWITCH_AFCS_AP_A)";

    private readonly List<string> _sent = new();
    private readonly IniL1011Definition _def;

    public IniL1011AutopilotEngageTests()
    {
        _def = new IniL1011Definition
        {
            Clock = () => 10_000,
            SettingsSource = () => new UserSettings(),
            CanLand = _ => true,
            SendCalc = (_, rpn) => _sent.Add(rpn),
            StepDelay = _ => Task.CompletedTask,
            SettleDelay = _ => Task.CompletedTask,
            ReRead = (_, _) => { },
        };
    }

    /// <summary>A manager whose cache holds the given paddle positions; a null one is unread.</summary>
    private static SimConnectManager Sim(double? a, double? b)
    {
        var values = new List<(string, double)>();
        if (a is double x)
            values.Add((PaddleA, x));
        if (b is double y)
            values.Add((PaddleB, y));
        return SeededSimConnectCache.With(values.ToArray());
    }

    // ---- What the engage reads ---------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 2.0)]    // A at Command
    [InlineData(2.0, 0.0)]    // B at Command
    [InlineData(0.0, 0.0)]
    [InlineData(0.0, 1.0)]
    [InlineData(1.0, 0.0)]
    [InlineData(0.4, 2.0)]    // within half a notch of Command
    [InlineData(2.0, -0.3)]
    public void A_paddle_reading_command_is_engaged(double a, double b)
    {
        Assert.True(_def.IsAutopilotEngaged(Sim(a, b)));
    }

    [Fact]
    public void A_paddle_at_command_is_engaged_whatever_the_other_one_reads()
    {
        // The engaged paddle is a fact; the other one being unread does not make it a guess.
        Assert.True(_def.IsAutopilotEngaged(Sim(0, null)));
        Assert.True(_def.IsAutopilotEngaged(Sim(null, 0)));
    }

    [Theory]
    [InlineData(2.0, 2.0)]    // both Off
    [InlineData(1.0, 1.0)]    // CWS flies nothing of its own: not engaged
    [InlineData(1.0, 2.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.6, 2.0)]    // nearer CWS than Command
    public void Both_paddles_read_and_neither_at_command_is_not_engaged(double a, double b)
    {
        Assert.False(_def.IsAutopilotEngaged(Sim(a, b)));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 2.0)]   // A unread
    [InlineData(2.0, null)]   // B unread
    [InlineData(1.0, null)]
    [InlineData(null, 1.0)]
    [InlineData(double.NaN, 2.0)]   // a NaN is no reading
    [InlineData(2.0, double.NaN)]
    public void A_paddle_not_read_leaves_the_engage_unknown_unless_the_other_is_at_command(double? a, double? b)
    {
        Assert.Null(_def.IsAutopilotEngaged(Sim(a, b)));
    }

    [Fact]
    public void A_paddle_position_the_aircraft_delivered_counts_as_read()
    {
        // The autopilot's own reading: the cache, else the last value delivered (as Ctrl+P's labels).
        var sim = Sim(null, 2.0);
        _def.ProcessSimVarUpdate(PaddleA, 0, new SpeechCapture());

        Assert.True(_def.IsAutopilotEngaged(sim));
    }

    [Fact]
    public void The_engaged_state_is_what_the_aircraft_reports_never_what_was_just_pressed()
    {
        // Closed loop: "Autopilot engaged" must follow the paddle reading Command, not our own press.
        var sim = Sim(2.0, 2.0);

        _def.EngageAutopilot(sim);

        Assert.NotEmpty(_sent);
        Assert.False(_def.IsAutopilotEngaged(sim));
    }

    // ---- What the engage presses -------------------------------------------------------------

    [Theory]
    [InlineData(0.0, 2.0)]    // engaged: a press would only disturb it
    [InlineData(2.0, 0.0)]
    [InlineData(0.0, null)]
    [InlineData(null, 0.0)]
    [InlineData(null, null)]  // unread: never a blind press
    [InlineData(null, 2.0)]
    [InlineData(2.0, null)]
    [InlineData(1.0, null)]
    [InlineData(double.NaN, 2.0)]
    public void It_sends_nothing_when_the_autopilot_is_engaged_or_a_paddle_is_unread(double? a, double? b)
    {
        _def.EngageAutopilot(Sim(a, b));

        Assert.Empty(_sent);
    }

    [Fact]
    public void It_presses_paddle_A_through_cws_to_command_when_both_read_off()
    {
        var trace = new List<string>();
        _def.SendCalc = (_, rpn) => trace.Add("send:" + rpn);
        _def.StepDelay = ms => { trace.Add("wait:" + ms); return Task.CompletedTask; };

        _def.EngageAutopilot(Sim(2.0, 2.0));

        // A paddle sent straight to Command from Off shows Command and flies nothing ([L1011-21]).
        Assert.Equal(new[]
        {
            "send:" + CwsA,
            $"wait:{L1011WritePlan.PaddleNotchMs}",
            "send:" + CommandA,
        }, trace);
    }

    [Theory]
    [InlineData(1.0, 2.0)]    // A at CWS is not engaged: it goes on to Command
    [InlineData(2.0, 1.0)]    // B at CWS: still A that is pressed
    [InlineData(1.0, 1.0)]
    public void It_presses_paddle_A_when_neither_paddle_is_at_command(double a, double b)
    {
        _def.EngageAutopilot(Sim(a, b));

        Assert.NotEmpty(_sent);
        Assert.Equal(CommandA, _sent[^1]);
    }

    [Theory]
    [InlineData(2.0, 2.0)]
    [InlineData(1.0, 2.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(1.0, 1.0)]
    public void It_never_touches_paddle_B(double a, double b)
    {
        _def.EngageAutopilot(Sim(a, b));

        Assert.NotEmpty(_sent);
        Assert.All(_sent, rpn => Assert.DoesNotContain("AP_B", rpn));
    }

    [Fact]
    public void It_sends_nothing_when_the_calculator_path_cannot_land()
    {
        _def.CanLand = _ => false;

        _def.EngageAutopilot(Sim(2.0, 2.0));

        Assert.Empty(_sent);
    }

    [Fact]
    public void It_attaches_the_definition_to_the_manager_it_was_given()
    {
        Assert.False(_def.FoReady);

        _def.EngageAutopilot(Sim(2.0, 2.0));

        Assert.True(_def.FoReady);
    }

    [Fact]
    public void The_paddle_moves_it_makes_are_its_own_and_not_announced()
    {
        // The service's "Autopilot engaged" is the one confirmation: the CWS notch and the Command
        // the engage puts the paddle in are not spoken again by the aircraft's autopilot call-out.
        var speech = new SpeechCapture();
        _def.ProcessSimVarUpdate(PaddleA, 2, speech);   // the call-out's baseline: Off

        _def.EngageAutopilot(Sim(2.0, 2.0));
        _def.ProcessSimVarUpdate(PaddleA, 1, speech);   // a batch catching the CWS notch
        _def.ProcessSimVarUpdate(PaddleA, 0, speech);

        Assert.Empty(speech.All);
    }

    // ---- How the service drives it -------------------------------------------------------------

    [Fact]
    public void The_service_waits_for_a_reading_and_spaces_its_presses_3_seconds_apart()
    {
        Assert.True(_def.AutopilotEngageWaitsForState);
        Assert.Equal(3000, _def.AutopilotEngageRetryIntervalMs);
    }

    [Fact]
    public void There_is_no_engage_floor_because_the_manual_gives_none()
    {
        Assert.Equal(0, _def.MinimumAutopilotEngageAltitudeAgl);
    }
}
