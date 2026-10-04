using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The A300's fault and warning lights: each on its panel's status box with a Ctrl+M row, speaking
/// both ways, and the lights one batch delivery changes spoken as one sentence when the batch ends.
/// </summary>
public class A300FaultLightTests
{
    private readonly IniA300Definition _def;
    private readonly SpeechCapture _speech = new();

    public A300FaultLightTests()
    {
        _def = new IniA300Definition();
        _def.Attach(new SimConnectManager(IntPtr.Zero));   // never connected
    }

    private static A300Lamp Lamp(string var) => A300FaultLights.All.Single(l => l.Var == var);

    private void Deliver(A300Lamp lamp, double value) => _def.ProcessSimVarUpdate(lamp.Key, value, _speech);

    private void BatchEnd() => _def.OnContinuousBatchDelivered(1);

    [Fact]
    public void Every_light_is_distinct_named_and_on_a_real_panel()
    {
        var panels = _def.GetPanelStructure().Values.SelectMany(p => p).ToHashSet();
        var displays = _def.GetPanelDisplayVariables();
        Assert.Equal(58, A300FaultLights.All.Count);
        Assert.Equal(A300FaultLights.All.Count, A300FaultLights.All.Select(l => l.Key).Distinct().Count());
        Assert.Equal(A300FaultLights.All.Count, A300FaultLights.All.Select(l => l.Var).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(A300FaultLights.All.Count, A300FaultLights.All.Select(l => l.Name).Distinct().Count());
        foreach (var lamp in A300FaultLights.All)
        {
            Assert.StartsWith("INI_", lamp.Var);
            Assert.EndsWith(" light", lamp.Name);
            Assert.True(lamp.SpeaksOff);
            Assert.Contains(lamp.Panel, panels);
            Assert.Contains(lamp.Key, displays[lamp.Panel]);
        }
    }

    [Fact]
    public void Every_light_streams_on_its_own_subscription_with_a_ctrl_m_row()
    {
        // Not on the batch: 58 more names there pushed the FMA's pitch trim sources into a second
        // batch, and the FMA must read one complete sample (IniA300FmaBehaviourTests).
        var vars = _def.GetVariables();
        foreach (var lamp in A300FaultLights.All)
        {
            var def = vars[lamp.Key];
            Assert.False(ContinuousBatchLayout.RidesBatch(def), lamp.Var);
            Assert.Equal(UpdateFrequency.Continuous, def.UpdateFrequency);
            Assert.True(def.IsAnnounced);
            Assert.True(def.ExcludeFromBatch);
            Assert.False(def.HighFrequency);   // once a second is plenty for a light
            Assert.False(def.ExcludeFromMonitorManager);
            Assert.Equal(lamp.Name, def.DisplayName);
        }
    }

    [Fact]
    public void The_master_lights_stay_on_the_batch()
    {
        var vars = _def.GetVariables();
        Assert.True(ContinuousBatchLayout.RidesBatch(vars[A300Announcements.MasterWarningKey]));
        Assert.True(ContinuousBatchLayout.RidesBatch(vars[A300Announcements.MasterCautionKey]));
    }

    [Fact]
    public void A_light_speaks_both_ways_at_the_end_of_its_batch()
    {
        var gen = Lamp("INI_elec_gen1_fault");
        Assert.True(_def.ProcessSimVarUpdate(gen.Key, 1, _speech));   // baseline, consumed
        BatchEnd();
        Assert.Empty(_speech.All);
        Deliver(gen, 0);
        Assert.Empty(_speech.All);   // held until the batch ends
        BatchEnd();
        Deliver(gen, 1);
        BatchEnd();
        Assert.Equal(new[] { "Engine 1 generator fault light off", "Engine 1 generator fault light on" }, _speech.All);
        Assert.Empty(_speech.Interrupts);   // queued, never interrupting
    }

    [Fact]
    public void Lights_changing_together_are_one_sentence()
    {
        var lamps = new[] { Lamp("INI_SPEEDBRAKE7_FAULT"), Lamp("INI_SPEEDBRAKE6_FAULT"), Lamp("INI_SPEEDBRAKE5_FAULT"), Lamp("INI_APU_FAULT") };
        foreach (var lamp in lamps)
            Deliver(lamp, lamp.Var == "INI_APU_FAULT" ? 0 : 1);
        BatchEnd();
        foreach (var lamp in lamps)
            Deliver(lamp, lamp.Var == "INI_APU_FAULT" ? 1 : 0);
        BatchEnd();
        Assert.Equal(new[] { "APU fault light on. 3 lights off: Spoiler 7 fault, Spoiler 6 fault, Spoiler 5 fault" }, _speech.All);
    }

    [Fact]
    public void A_muted_light_is_not_collected()
    {
        var gen = Lamp("INI_elec_gen1_fault");
        Deliver(gen, 1);
        _speech.Suppressed = true;   // MainForm's wrap around a muted row's delivery
        Deliver(gen, 0);
        _speech.Suppressed = false;
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_master_lights_still_speak_at_once_on_the_rising_edge_only()
    {
        _def.ProcessSimVarUpdate(A300Announcements.MasterCautionKey, 0, _speech);
        _def.ProcessSimVarUpdate(A300Announcements.MasterCautionKey, 1, _speech);
        Assert.Equal(new[] { "Master caution" }, _speech.All);
        _def.ProcessSimVarUpdate(A300Announcements.MasterCautionKey, 0, _speech);
        BatchEnd();
        Assert.Single(_speech.All);
    }

    [Fact]
    public void A_context_reset_drops_what_was_waiting()
    {
        var gen = Lamp("INI_elec_gen1_fault");
        Deliver(gen, 1);
        Deliver(gen, 0);
        _def.OnSimContextReset();
        BatchEnd();
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_light_reads_on_or_off_in_its_status_box()
    {
        var vars = _def.GetVariables();
        var gen = Lamp("INI_elec_gen1_fault");
        Assert.Equal("On", vars[gen.Key].ValueDescriptions[1]);
        Assert.Equal("Off", vars[gen.Key].ValueDescriptions[0]);
    }

    [Fact]
    public void One_change_reads_as_its_own_phrase() =>
        Assert.Equal("Engine 1 generator fault light off",
            A300LampCallouts.Compose(new[] { new A300LampChange("Engine 1 generator fault light", false) }));

    [Fact]
    public void Two_changes_the_same_way_are_counted()
    {
        var text = A300LampCallouts.Compose(new[]
        {
            new A300LampChange("Pack 1 fault light", true),
            new A300LampChange("Pack 2 fault light", true),
        });
        Assert.Equal("2 lights on: Pack 1 fault, Pack 2 fault", text);
    }

    [Fact]
    public void Lights_coming_on_are_read_first()
    {
        var text = A300LampCallouts.Compose(new[]
        {
            new A300LampChange("Pack 1 fault light", false),
            new A300LampChange("Engine 1 fire handle light", true),
        });
        Assert.Equal("Engine 1 fire handle light on. Pack 1 fault light off", text);
    }
}
