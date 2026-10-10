using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The autobrake buttons: each shows what its lamp shows (the aircraft's lamp code, 1.0.11: the lower
/// segment lights while INI_AUTOBRAKE_LEVEL is its level and its DECEL segment is dark, the DECEL
/// segment from INI_AUTOBRAKE_*_DECEL, both on AC light power).
/// </summary>
public class A300AutobrakeTests
{
    [Theory]
    [InlineData("A300_AUTO_BRK_LO", 0.0, 0.0, "Off")]
    [InlineData("A300_AUTO_BRK_LO", 1.0, 0.0, "Armed")]
    [InlineData("A300_AUTO_BRK_LO", 1.0, 1.0, "Decel")]
    [InlineData("A300_AUTO_BRK_LO", 2.0, 0.0, "Off")]
    [InlineData("A300_AUTO_BRK_MID", 2.0, 0.0, "Armed")]
    [InlineData("A300_AUTO_BRK_MAX", 3.0, 1.0, "Decel")]
    [InlineData("A300_AUTO_BRK_MAX", 1.0, 0.0, "Off")]
    public void A_button_reads_its_lamp(string rowKey, double level, double decel, string state) =>
        Assert.Equal(state, A300Autobrake.Describe(A300Autobrake.ByButton[rowKey], level, decel, acPower: 1));

    [Fact]
    public void A_button_with_its_level_unread_shows_no_state() =>
        Assert.Null(A300Autobrake.Describe(A300Autobrake.ByButton["A300_AUTO_BRK_LO"], null, 0, acPower: 1));

    [Fact]
    public void A_button_with_its_light_power_unread_shows_no_state() =>
        Assert.Null(A300Autobrake.Describe(A300Autobrake.ByButton["A300_AUTO_BRK_LO"], 1, 0, acPower: null));

    [Fact]
    public void A_button_on_a_dark_ac_bus_reads_off_as_its_lamp_does() =>
        Assert.Equal("Off", A300Autobrake.Describe(A300Autobrake.ByButton["A300_AUTO_BRK_LO"], 1, 0, acPower: 0));

    [Fact]
    public void An_unread_decel_light_reads_the_armed_segment() =>
        Assert.Equal("Armed", A300Autobrake.Describe(A300Autobrake.ByButton["A300_AUTO_BRK_LO"], 1, null, acPower: 1));
}

/// <summary>
/// The autobrake buttons in the definition: the variables they read, their labels, what a press says
/// (nothing), and the lights spoken as they change, like the Fenix's autobrake lights.
/// </summary>
public class IniA300AutobrakeTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();
    private readonly HashSet<string> _muted = new();
    private bool _canLand = true;

    public IniA300AutobrakeTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => _canLand,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (_, _) => { },
            TypedDelay = _ =>
            {
                var wait = new TaskCompletionSource();
                _waits.Add(wait);
                return wait.Task;
            },
            IsMuted = key => _muted.Contains(key),
        };
        _def.Attach(_sim);
    }

    private bool Press(string key) => _def.HandleUIVariableSet(key, 1, _def.GetVariables()[key], _sim, _speech);

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    private void BatchEnd() => _def.OnContinuousBatchDelivered(1);

    private void Baseline(double level = 0, double ac = 1)
    {
        Deliver(A300LampBoard.AcPowerKey, ac);
        foreach (var button in A300Autobrake.ByButton.Values)
            Deliver(button.DecelKey, 0);
        Deliver(A300Autobrake.LevelKey, level);
        BatchEnd();
    }

    [Fact]
    public void The_level_and_decel_lights_stream_on_their_own_subscriptions_with_ctrl_m_rows()
    {
        var vars = _def.GetVariables();
        var expected = new Dictionary<string, (string Var, string Name)>
        {
            [A300Autobrake.LevelKey] = ("INI_AUTOBRAKE_LEVEL", "Autobrake armed lights"),
            ["A300_AUTOBRAKE_LOW_DECEL"] = ("INI_AUTOBRAKE_LOW_DECEL", "Autobrake low decel light"),
            ["A300_AUTOBRAKE_MED_DECEL"] = ("INI_AUTOBRAKE_MED_DECEL", "Autobrake medium decel light"),
            ["A300_AUTOBRAKE_HI_DECEL"] = ("INI_AUTOBRAKE_HI_DECEL", "Autobrake max decel light"),
        };
        foreach (var (key, (name, display)) in expected)
        {
            var def = vars[key];
            Assert.Equal((name, display, UpdateFrequency.Continuous, true, false),
                (def.Name, def.DisplayName, def.UpdateFrequency, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(def));   // [A300-9]: never split the FMA's batch
            Assert.True(_def.ProcessSimVarUpdate(key, 1, _speech));
        }
        Assert.Empty(_speech.All);   // nothing is spoken at once: lights speak when their batch ends
    }

    [Theory]
    [InlineData("A300_AUTO_BRK_LO", "A300_AUTOBRAKE_LOW_DECEL")]
    [InlineData("A300_AUTO_BRK_MID", "A300_AUTOBRAKE_MED_DECEL")]
    [InlineData("A300_AUTO_BRK_MAX", "A300_AUTOBRAKE_HI_DECEL")]
    public void Each_button_is_relabelled_when_its_lamps_inputs_change(string rowKey, string decelKey) =>
        Assert.Equal(new[] { A300Autobrake.LevelKey, decelKey, A300LampBoard.AcPowerKey }, _def.GetVariables()[rowKey].StateVariables);

    [Fact]
    public void Each_button_label_shows_its_lamp()
    {
        Assert.False(_def.TryDescribeControlState("A300_AUTO_BRK_LO", out _));   // nothing read yet
        _cache[A300Autobrake.LevelKey] = 2;
        _cache[A300LampBoard.AcPowerKey] = 1;
        Assert.True(_def.TryDescribeControlState("A300_AUTO_BRK_LO", out var low));
        Assert.True(_def.TryDescribeControlState("A300_AUTO_BRK_MID", out var medium));
        Assert.Equal(("Off", "Armed"), (low, medium));
    }

    [Fact]
    public void A_press_says_nothing_and_waits_for_nothing()
    {
        _cache[A300Autobrake.LevelKey] = 0;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Equal(new[] { "1 (>B:AIRLINER_AUTO_BRK_LO_Set)" }, _sent);
        Assert.Empty(_waits);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_armed_light_speaks_when_the_aircraft_arms_it()
    {
        Baseline();
        Assert.True(Press("A300_AUTO_BRK_MAX"));
        Deliver(A300Autobrake.LevelKey, 3);
        BatchEnd();
        Assert.Equal(new[] { "Autobrake max armed light on" }, _speech.All);
    }

    [Fact]
    public void A_press_the_aircraft_refuses_changes_nothing_so_nothing_is_said()
    {
        Baseline();
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Deliver(A300Autobrake.LevelKey, 0);   // still off
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Pressing_the_active_level_disarms_it_and_its_light_going_out_is_spoken()
    {
        Baseline(level: 1);
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Single(_sent);   // never blocked: the aircraft disarms it by design
        Deliver(A300Autobrake.LevelKey, 0);
        BatchEnd();
        Assert.Equal(new[] { "Autobrake low armed light off" }, _speech.All);
    }

    [Fact]
    public void A_muted_armed_row_silences_the_armed_lights_even_when_power_lights_them()
    {
        Baseline(level: 2, ac: 0);
        _muted.Add(A300Autobrake.LevelKey);
        Deliver(A300LampBoard.AcPowerKey, 1);
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_autobrake_and_light_power_inputs_check_each_lights_own_mute_rather_than_the_wrap()
    {
        Assert.True(_def.IsMuteWrapExempt(A300Autobrake.LevelKey));
        Assert.True(_def.IsMuteWrapExempt("A300_AUTOBRAKE_LOW_DECEL"));
        Assert.True(_def.IsMuteWrapExempt(A300LampBoard.AcPowerKey));
        Assert.True(_def.IsMuteWrapExempt(A300LampBoard.DcPowerKey));
        Assert.False(_def.IsMuteWrapExempt(A300Announcements.MasterCautionKey));
    }

    [Fact]
    public void A_press_that_cannot_land_is_refused()
    {
        _canLand = false;
        _cache[A300Autobrake.LevelKey] = 0;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Autobrake low unavailable" }, _speech.All);
    }
}
