using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The autobrake buttons: each shows what its lamp shows (the aircraft's lamp code, 1.0.11: the lower
/// segment lights while INI_AUTOBRAKE_LEVEL is its level and its DECEL segment is dark, the DECEL
/// segment from INI_AUTOBRAKE_*_DECEL), and a press meant to arm that leaves the level unarmed says so.
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
        Assert.Equal(state, A300Autobrake.Describe(A300Autobrake.ByButton[rowKey], level, decel));

    [Fact]
    public void A_button_with_its_level_unread_shows_no_state() =>
        Assert.Null(A300Autobrake.Describe(A300Autobrake.ByButton["A300_AUTO_BRK_LO"], null, 0));

    [Fact]
    public void An_unread_decel_light_reads_the_armed_segment() =>
        Assert.Equal("Armed", A300Autobrake.Describe(A300Autobrake.ByButton["A300_AUTO_BRK_LO"], 1, null));

    [Theory]
    [InlineData(1, 0.0, 0.0, true)]    // off, pressed low, still off: refused (gear up, no green pressure)
    [InlineData(3, 1.0, 1.0, true)]    // low armed, pressed max, still low
    [InlineData(1, 0.0, 1.0, false)]   // armed as asked
    [InlineData(1, 1.0, 0.0, false)]   // the active level pressed again: the aircraft disarms it by design
    [InlineData(1, null, 0.0, false)]  // the level before the press was not known: no judgement
    [InlineData(1, 0.0, null, false)]  // the level did not answer after the press: no judgement
    public void A_press_meant_to_arm_that_leaves_its_level_unarmed_did_not_arm(int level, double? before, double? after, bool didNotArm) =>
        Assert.Equal(didNotArm, A300Autobrake.DidNotArm(level, before, after));
}

/// <summary>The autobrake buttons in the definition: the variables they read, their labels, and what a press says.</summary>
public class IniA300AutobrakeTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly Dictionary<string, double> _fresh = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();
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
            ReadFresh = (_, key, _) => Task.FromResult(_fresh.TryGetValue(key, out var v) ? v : (double?)null),
        };
        _def.Attach(_sim);
    }

    private bool Press(string key) => _def.HandleUIVariableSet(key, 1, _def.GetVariables()[key], _sim, _speech);

    [Fact]
    public void The_level_and_decel_lights_stream_on_their_own_subscriptions_and_are_consumed_silently()
    {
        var vars = _def.GetVariables();
        var expected = new Dictionary<string, string>
        {
            [A300Autobrake.LevelKey] = "INI_AUTOBRAKE_LEVEL",
            ["A300_AUTOBRAKE_LOW_DECEL"] = "INI_AUTOBRAKE_LOW_DECEL",
            ["A300_AUTOBRAKE_MED_DECEL"] = "INI_AUTOBRAKE_MED_DECEL",
            ["A300_AUTOBRAKE_HI_DECEL"] = "INI_AUTOBRAKE_HI_DECEL",
        };
        foreach (var (key, name) in expected)
        {
            var def = vars[key];
            Assert.Equal((name, UpdateFrequency.Continuous, true, true),
                (def.Name, def.UpdateFrequency, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(def));   // [A300-9]: never split the FMA's batch
            Assert.True(_def.ProcessSimVarUpdate(key, 1, _speech));
        }
        Assert.Empty(_speech.All);
    }

    [Theory]
    [InlineData("A300_AUTO_BRK_LO", "A300_AUTOBRAKE_LOW_DECEL")]
    [InlineData("A300_AUTO_BRK_MID", "A300_AUTOBRAKE_MED_DECEL")]
    [InlineData("A300_AUTO_BRK_MAX", "A300_AUTOBRAKE_HI_DECEL")]
    public void Each_button_is_relabelled_when_its_lamps_variables_change(string rowKey, string decelKey) =>
        Assert.Equal(new[] { A300Autobrake.LevelKey, decelKey }, _def.GetVariables()[rowKey].StateVariables);

    [Fact]
    public void Each_button_label_shows_its_lamp()
    {
        Assert.False(_def.TryDescribeControlState("A300_AUTO_BRK_LO", out _));   // nothing read yet
        _cache[A300Autobrake.LevelKey] = 2;
        Assert.True(_def.TryDescribeControlState("A300_AUTO_BRK_LO", out var low));
        Assert.True(_def.TryDescribeControlState("A300_AUTO_BRK_MID", out var medium));
        Assert.Equal(("Off", "Armed"), (low, medium));
    }

    [Fact]
    public void A_press_that_arms_says_nothing()
    {
        _cache[A300Autobrake.LevelKey] = 0;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Equal(new[] { "1 (>B:AIRLINER_AUTO_BRK_LO_Set)" }, _sent);
        _fresh[A300Autobrake.LevelKey] = 1;
        _waits.Single().SetResult();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_press_that_does_not_arm_says_so_after_the_settle()
    {
        _cache[A300Autobrake.LevelKey] = 0;
        Assert.True(Press("A300_AUTO_BRK_MAX"));
        Assert.Empty(_speech.All);   // not before the aircraft has had its chance
        _fresh[A300Autobrake.LevelKey] = 0;
        _waits.Single().SetResult();
        Assert.Equal(new[] { A300Autobrake.DidNotArmMessage }, _speech.All);
        Assert.Equal("Autobrake did not arm", A300Autobrake.DidNotArmMessage);
    }

    [Fact]
    public void Pressing_the_active_level_disarms_it_silently_by_the_aircrafts_own_design()
    {
        _cache[A300Autobrake.LevelKey] = 1;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Single(_sent);   // never blocked
        _fresh[A300Autobrake.LevelKey] = 0;
        _waits.Single().SetResult();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Two_quick_presses_arm_then_disarm_and_say_nothing()
    {
        // The second press lands while the first is settling: the cache may still show the level before
        // either, so neither press can be judged, and the label shows where the aircraft ended.
        _cache[A300Autobrake.LevelKey] = 0;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Equal(2, _sent.Count);
        _fresh[A300Autobrake.LevelKey] = 0;
        foreach (var wait in _waits.ToList())
            wait.SetResult();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_press_after_the_last_check_has_finished_is_judged_again()
    {
        _cache[A300Autobrake.LevelKey] = 0;
        _fresh[A300Autobrake.LevelKey] = 1;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        _waits[0].SetResult();
        _cache[A300Autobrake.LevelKey] = 1;
        _fresh[A300Autobrake.LevelKey] = 1;
        Assert.True(Press("A300_AUTO_BRK_MAX"));   // refused: still low
        _waits[1].SetResult();
        Assert.Equal(new[] { A300Autobrake.DidNotArmMessage }, _speech.All);
    }

    [Fact]
    public void A_press_that_cannot_land_is_refused_and_not_checked()
    {
        _canLand = false;
        _cache[A300Autobrake.LevelKey] = 0;
        Assert.True(Press("A300_AUTO_BRK_LO"));
        Assert.Empty(_sent);
        Assert.Empty(_waits);
        Assert.Equal(new[] { "Autobrake low unavailable" }, _speech.All);
    }

    [Fact]
    public void Another_button_is_not_checked()
    {
        Assert.True(Press("A300_TRP_CL"));
        Assert.Empty(_waits);
    }
}
