using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// Each MCDU's four annunciators (the cockpit's <c>CPT_FMS_*_LIGHT</c> and <c>FO_FMS_*_LIGHT</c> lamps, on the AC
/// light bus), shown in the MCDU window's status box and spoken as one comes on, as the MD-11's MCDU window does.
/// </summary>
public class A300McduLightsTests
{
    [Theory]
    [InlineData(A300McduUnit.Captain, "INI_FMS1_message_light", "INI_FMS1_fail_light", "INI_FMS1_display_light", "INI_FMS1_offset_light")]
    [InlineData(A300McduUnit.FirstOfficer, "INI_FMS2_message_light", "INI_FMS2_fail_light", "INI_FMS2_display_light", "INI_FMS2_offset_light")]
    public void Each_mcdu_has_the_md11s_four_annunciators(A300McduUnit unit, string msg, string fail, string dspy, string ofst)
    {
        var lights = A300McduLights.For(unit);
        Assert.Equal(new[] { "MSG", "FAIL", "DSPY", "OFST" }, lights.Select(l => l.Label));
        Assert.Equal(new[] { msg, fail, dspy, ofst }, lights.Select(l => l.Var));
    }

    [Fact]
    public void A_light_is_lit_only_with_its_light_power_on()
    {
        var cache = new Dictionary<string, double>
        {
            [A300McduLights.For(A300McduUnit.FirstOfficer)[0].Key] = 1,
            [A300McduLights.For(A300McduUnit.FirstOfficer)[2].Key] = 1,
            [A300LampBoard.AcPowerKey] = 1,
        };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.Equal(new[] { "MSG", "DSPY" }, def.McduAnnunciators(A300McduUnit.FirstOfficer));
        Assert.Empty(def.McduAnnunciators(A300McduUnit.Captain));
        cache[A300LampBoard.AcPowerKey] = 0;
        Assert.Empty(def.McduAnnunciators(A300McduUnit.FirstOfficer));
    }

    [Fact]
    public void Each_light_streams_on_its_own_subscription_and_is_consumed_silently()
    {
        var def = new IniA300Definition();
        var speech = new SpeechCapture();
        var vars = def.GetVariables();
        foreach (var light in A300McduLights.For(A300McduUnit.Captain).Concat(A300McduLights.For(A300McduUnit.FirstOfficer)))
        {
            Assert.Equal(light.Var, vars[light.Key].Name);
            Assert.True(vars[light.Key].ExcludeFromBatch && vars[light.Key].ExcludeFromMonitorManager, light.Key);
            Assert.True(def.ProcessSimVarUpdate(light.Key, 1, speech), light.Key);
        }
        Assert.Empty(speech.All);
    }

    [Theory]
    [InlineData(new string[0], new[] { "MSG" }, "MSG")]
    [InlineData(new[] { "MSG" }, new[] { "MSG", "DSPY" }, "DSPY")]
    [InlineData(new[] { "MSG", "DSPY" }, new[] { "DSPY" }, null)]
    [InlineData(new[] { "MSG" }, new[] { "MSG" }, null)]
    public void Only_a_light_coming_on_is_spoken(string[] before, string[] now, string? spoken) =>
        Assert.Equal(spoken, A300McduLights.ComingOn(before, now));

    [Theory]
    [InlineData(new string[0], "Captain MCDU: connected")]
    [InlineData(new[] { "MSG", "FAIL" }, "Captain MCDU: MSG, FAIL")]
    public void The_status_box_lists_the_lit_annunciators(string[] lit, string status) =>
        Assert.Equal(status, A300McduLights.Status("Captain MCDU", lit));
}
