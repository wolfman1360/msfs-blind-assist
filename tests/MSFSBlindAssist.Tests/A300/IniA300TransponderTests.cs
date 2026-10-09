using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The pedestal transponder mode while the tablet's IDC option is on: the IDC sets the mode then
/// (IDC::Update rewrites L:INI_tcas_mode_pedestal every frame, 1.0.11), so the panel's combo refuses
/// aloud and is put back instead of showing a mode the aircraft did not take.
/// </summary>
public class IniA300TransponderTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected
    private readonly Dictionary<string, double> _cache = new();
    private readonly List<string> _sent = new();
    private readonly List<string> _reReads = new();

    public IniA300TransponderTests()
    {
        _def = new IniA300Definition
        {
            CanLand = _ => true,
            Cached = (_, key) => _cache.TryGetValue(key, out var v) ? v : null,
            Send = (_, rpn) => _sent.Add(rpn),
            ReRead = (key, _) => _reReads.Add(key),
        };
    }

    private bool Set(string key, double value) => _def.HandleUIVariableSet(key, value, _def.GetVariables()[key], _sim, _speech);

    [Fact]
    public void The_idc_option_streams_on_its_own_subscription_and_is_consumed_silently()
    {
        var def = _def.GetVariables()[A300Idc.OptionKey];
        Assert.Equal(("INI_IS_IDC", UpdateFrequency.Continuous, true, true),
            (def.Name, def.UpdateFrequency, def.ExcludeFromBatch, def.ExcludeFromMonitorManager));
        Assert.False(ContinuousBatchLayout.RidesBatch(def));   // [A300-9]: never split the FMA's batch
        Assert.True(_def.ProcessSimVarUpdate(A300Idc.OptionKey, 1, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void With_the_idc_on_the_transponder_mode_is_refused_aloud_and_put_back()
    {
        _cache[A300Idc.OptionKey] = 1;
        _cache[A300Idc.TransponderModeKey] = 0;
        Assert.True(Set(A300Idc.TransponderModeKey, 2));
        Assert.Empty(_sent);
        Assert.Equal(new[] { "Transponder mode: set from the IDC" }, _speech.All);
        Assert.Equal(new[] { A300Idc.TransponderModeKey }, _reReads);
    }

    [Fact]
    public void With_the_idc_off_the_transponder_mode_is_set_and_nothing_is_said()
    {
        _cache[A300Idc.OptionKey] = 0;
        _cache[A300Idc.TransponderModeKey] = 0;
        Assert.True(Set(A300Idc.TransponderModeKey, 2));
        Assert.Equal(new[] { "2 (>B:AIRLINER_TCAS_MODE_Set)" }, _sent);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void With_the_idc_option_not_read_yet_the_transponder_mode_is_set_as_before()
    {
        _cache[A300Idc.TransponderModeKey] = 0;
        Assert.True(Set(A300Idc.TransponderModeKey, 2));
        Assert.Equal(new[] { "2 (>B:AIRLINER_TCAS_MODE_Set)" }, _sent);
    }

    [Fact]
    public void The_idc_never_refuses_another_row()
    {
        _cache[A300Idc.OptionKey] = 1;
        _cache["A300_BATT_1"] = 0;
        Assert.True(Set("A300_BATT_1", 1));
        Assert.Equal(new[] { "1 (>B:AIRLINER_BATT_1_Set)" }, _sent);
    }
}
