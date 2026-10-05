using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>Ctrl+B's STD and QNH: the variables they read, and the press, settle, confirm, write and
/// read-back sequence.</summary>
public class IniA300BaroTests
{
    private const string Standard = "1 16212 (>K:2:KOHLSMAN_SET) 2 16212 (>K:2:KOHLSMAN_SET) 3 16212 (>K:2:KOHLSMAN_SET)";

    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);
    private readonly Dictionary<string, double> _cache = new();
    private readonly Dictionary<string, double> _fresh = new();
    private readonly List<string> _sent = new();
    private readonly List<TaskCompletionSource> _waits = new();
    private bool _canLand = true;

    public IniA300BaroTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => _canLand,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
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

    private static string Press(string key) => $"1 (>B:{A300ControlMap.Load().FindByKey(key)!.Event})";

    private void ReadBack(double captain, double firstOfficer, double standby)
    {
        _fresh[A300Readouts.BaroCaptainKey] = captain;
        _fresh[A300Readouts.BaroFirstOfficerKey] = firstOfficer;
        _fresh[A300Readouts.BaroStandbyKey] = standby;
    }

    [Fact]
    public void The_modes_ride_their_own_subscriptions_and_the_saved_settings_are_read_on_request()
    {
        var vars = _def.GetVariables();
        foreach (var side in A300Baro.Sides)
        {
            var mode = vars[side.ModeKey];
            Assert.Equal((side.ModeVar, UpdateFrequency.Continuous, true, true),
                (mode.Name, mode.UpdateFrequency, mode.ExcludeFromBatch, mode.ExcludeFromMonitorManager));
            Assert.False(ContinuousBatchLayout.RidesBatch(mode));
            Assert.Equal((side.SavedVar, UpdateFrequency.OnRequest), (vars[side.SavedKey].Name, vars[side.SavedKey].UpdateFrequency));
        }
    }

    [Fact]
    public void A_mode_change_is_consumed_silently()
    {
        Assert.True(_def.ProcessSimVarUpdate(A300Baro.Captain.ModeKey, 1, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_buttons_carry_the_knobs_own_words_and_both_sides_modes()
    {
        var buttons = _def.BaroButtons(_sim, _speech);
        Assert.Equal(new[] { "&Set STD pressure, both sides", "Set &QNH pressure, both sides" }, buttons.Select(b => b.Label));
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        Assert.Equal("Captain QNH, first officer STD", buttons[0].GetCurrentState());
        Assert.True(buttons.All(b => b.SuppressStateAnnounce!()));
    }

    [Fact]
    public void Standard_pulls_waits_confirms_writes_1013_and_reads_back()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey), Press(A300Baro.FirstOfficer.PullKey) }, _sent);

        _fresh[A300Baro.Captain.ModeKey] = 1;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 1;
        _waits[0].SetResult();
        Assert.Equal(Standard, _sent.Last());
        Assert.Empty(_speech.All);

        ReadBack(1013.25, 1013.25, 1013.25);
        _waits[1].SetResult();
        Assert.Equal(new[] { "Altimeters standard, 1013 hectopascals, 29.92 inches" }, _speech.All);
    }

    [Fact]
    public void Standard_stops_and_says_which_side_did_not_switch()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PullKey) }, _sent);
        _fresh[A300Baro.Captain.ModeKey] = 0;
        _waits[0].SetResult();
        Assert.DoesNotContain(Standard, _sent);
        Assert.Equal(new[] { "Captain altimeter did not switch to STD" }, _speech.All);
    }

    [Fact]
    public void Qnh_pushes_restores_the_saved_settings_and_reads_back()
    {
        _cache[A300Baro.Captain.ModeKey] = 1;
        _cache[A300Baro.FirstOfficer.ModeKey] = 1;
        _fresh[A300Baro.Captain.SavedKey] = 16399;
        _fresh[A300Baro.FirstOfficer.SavedKey] = 16288;
        _ = _def.SetQnhAsync(_sim, _speech);
        Assert.Equal(new[] { Press(A300Baro.Captain.PushKey), Press(A300Baro.FirstOfficer.PushKey) }, _sent);

        _fresh[A300Baro.Captain.ModeKey] = 0;
        _fresh[A300Baro.FirstOfficer.ModeKey] = 0;
        _waits[0].SetResult();
        Assert.Equal("1 16399 (>K:2:KOHLSMAN_SET) 2 16288 (>K:2:KOHLSMAN_SET) 3 16399 (>K:2:KOHLSMAN_SET)", _sent.Last());

        ReadBack(1024.94, 1018, 1024.94);
        _waits[1].SetResult();
        Assert.Equal(new[] { "Altimeters QNH: captain 1025, first officer 1018, standby 1025 hectopascals" }, _speech.All);
    }

    [Fact]
    public void Qnh_when_already_qnh_says_so_and_sends_nothing()
    {
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetQnhAsync(_sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.AlreadyQnh }, _speech.All);
    }

    [Fact]
    public void An_unread_mode_is_refused()
    {
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { A300Baro.UnknownModeRefusal }, _speech.All);
    }

    [Fact]
    public void Altimeters_that_cannot_be_reached_say_so()
    {
        _canLand = false;
        _cache[A300Baro.Captain.ModeKey] = 0;
        _cache[A300Baro.FirstOfficer.ModeKey] = 0;
        _ = _def.SetStandardAsync(_sim, _speech);
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Altimeters unavailable" }, _speech.All);
    }
}
