using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.A300;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The cockpit lights read through their own generated rule (<see cref="A300LampRule"/>): each named from
/// its button and legend, on its system's panel, spoken as the cockpit lights it and shown in its panel's
/// status box, with its own Ctrl+M row (owner decision 2026-10-09: every light that can be read is spoken).
/// </summary>
public class A300PanelLampTests
{
    private static readonly A300Placement Placement = A300PanelLayout.Place(A300ControlMap.Load());

    private static A300PanelLamp Lamp(string node) => A300PanelLamps.All.Single(l => l.Node == node);

    [Fact]
    public void Every_named_lamp_is_a_lamp_the_cockpit_draws()
    {
        var nodes = A300ControlMap.Load().Lamps.Select(l => l.Node).ToHashSet();
        Assert.All(A300PanelLamps.All, l => Assert.Contains(l.Node, nodes));
    }

    [Fact]
    public void Every_named_lamp_is_distinct_named_and_on_a_real_panel()
    {
        var panels = Placement.Structure.Values.SelectMany(p => p).ToHashSet();
        Assert.Equal(A300PanelLamps.All.Count, A300PanelLamps.All.Select(l => l.Node).Distinct().Count());
        var names = A300PanelLamps.All.Select(l => l.Name).Concat(A300Announcements.Lamps.Select(l => l.Name)).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.All(A300PanelLamps.All, l =>
        {
            Assert.EndsWith(" light", l.Name);
            Assert.Contains(l.Panel, panels);
        });
    }

    [Fact]
    public void No_named_lamp_repeats_a_light_already_read()
    {
        var read = A300FaultLights.All.Select(l => "L:" + l.Var).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.All(A300PanelLamps.Resolved, l => Assert.DoesNotContain(l.Primary.Id, read));
    }

    [Theory]
    [InlineData("EXT_PWR_SEQ1_LIGHT", "External power available light")]
    [InlineData("AC_BUS_1_OFF_LIGHT", "AC bus 1 off light")]
    [InlineData("AC_ESS_BUS_OFF_LIGHT", "AC essential bus off light")]
    [InlineData("AC_EMER_ON_INV_LIGHT", "AC emergency bus on inverter light")]
    [InlineData("DC_NORM_BUS_LIGHT", "DC normal bus off light")]
    [InlineData("DC_ESS_ONBAT_LIGHT", "DC essential bus on battery light")]
    [InlineData("IDG_1_SEQ1_LIGHT", "IDG 1 disconnected light")]
    [InlineData("OVR_SUP_2_SEQ2_LIGHT", "Override supply 2 flow light")]
    public void The_electrical_lights_are_named_from_their_buttons(string node, string name)
    {
        Assert.Equal(name, Lamp(node).Name);
        Assert.Equal("Electrical", Lamp(node).Panel);
    }

    [Theory]
    // The second and third mode panels' lamp nodes carry IRS 3 and IRS 2 in iniBuilds' numbering, in another
    // order: each light is named from the IRS its rule reads.
    [InlineData("IRS_1_009_LIGHT", "IRS 1 align light")]
    [InlineData("IRS_1_010_LIGHT", "IRS 1 on battery light")]
    [InlineData("IRS_1_011_LIGHT", "IRS 1 battery fault light")]
    [InlineData("IRS_1_012_LIGHT", "IRS 1 fault light")]
    [InlineData("IRS_2_009_LIGHT", "IRS 3 align light")]
    [InlineData("IRS_2_012_LIGHT", "IRS 3 fault light")]
    [InlineData("IRS_3_009_LIGHT", "IRS 2 fault light")]
    [InlineData("IRS_3_010_LIGHT", "IRS 2 battery fault light")]
    [InlineData("IRS_3_011_LIGHT", "IRS 2 on battery light")]
    [InlineData("IRS_3_012_LIGHT", "IRS 2 align light")]
    public void The_irs_lights_are_named_from_the_irs_they_read(string node, string name)
    {
        Assert.Equal(name, Lamp(node).Name);
        Assert.Equal("IRS", Lamp(node).Panel);
        int irs = name[4] - '0';
        Assert.Contains($"INI_IRS{irs}_", A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node).Primary.Name);
    }

    [Theory]
    [InlineData("INI_BAT1_light", "Battery 1 charge light")]
    [InlineData("INI_BAT3_light", "Battery 3 charge light")]
    public void The_battery_lights_are_charge_lights(string var, string name) =>
        // Owner decision 2026-10-09: they follow the battery's charge current (measured on flight 2).
        Assert.Equal(name, A300FaultLights.All.Single(l => l.Var == var).Name);

    [Fact]
    public void External_power_available_reads_the_cockpits_own_rule()
    {
        var board = new A300LampBoard();
        var avail = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "EXT_PWR_SEQ1_LIGHT");
        string externalPowerOn = A300PanelLamps.InputKey(avail.Rule.Inputs.Single(i => i.IsStock));
        // Its lamp has no light power term: the ground power unit lights it.
        Assert.Empty(board.Update(avail.Lamp.Key, 1));
        Assert.Empty(board.Update(externalPowerOn, 1));                      // on: AVAIL dark (baseline)
        Assert.Equal(new[] { "External power available light on" }, Said(board.Update(externalPowerOn, 0)));
        Assert.Equal(new[] { "External power available light off" }, Said(board.Update(avail.Lamp.Key, 0)));
    }

    [Fact]
    public void A_bus_off_light_needs_its_light_power_like_every_other_light()
    {
        var board = new A300LampBoard();
        string key = Lamp("AC_BUS_1_OFF_LIGHT").Key;
        board.Update(A300LampBoard.DcPowerKey, 0);
        board.Update(key, 0);
        Assert.Empty(board.Update(key, 1));                                   // dark bus: nothing to see
        board.Update(A300LampBoard.DcPowerKey, 1);
        Assert.True(A300LampBoard.IsLit(A300LampBoard.ById[key], k => k == key || k == A300LampBoard.DcPowerKey ? 1 : null));
        Assert.Equal(new[] { "AC bus 1 off light off" }, Said(board.Update(key, 0)));
    }

    [Fact]
    public void Each_lamp_streams_on_its_own_subscription_with_a_ctrl_m_row_and_a_status_box_line()
    {
        var def = new IniA300Definition();
        var vars = def.GetVariables();
        foreach (var lamp in A300PanelLamps.Resolved)
        {
            var v = vars[lamp.Lamp.Key];
            Assert.Equal(lamp.Primary.Name, v.Name);
            Assert.Equal(lamp.Lamp.Name, v.DisplayName);
            Assert.Equal(UpdateFrequency.Continuous, v.UpdateFrequency);
            Assert.True(v.IsAnnounced);
            Assert.True(v.ExcludeFromBatch);                                  // [A300-9]: never the batch
            Assert.False(v.ExcludeFromMonitorManager);
            Assert.Contains(lamp.Lamp.Key, def.GetPanelDisplayVariables()[lamp.Lamp.Panel]);
            foreach (var input in lamp.Rule.Inputs.Where(i => i.Id != lamp.Primary.Id))
            {
                var shared = vars[A300PanelLamps.InputKey(input)];
                Assert.Equal((input.Name, true, true), (shared.Name, shared.ExcludeFromBatch, shared.ExcludeFromMonitorManager));
                Assert.Equal(input.IsStock ? SimVarType.SimVar : SimVarType.LVar, shared.Type);
            }
        }
        Assert.Equal("Bool", vars[A300PanelLamps.InputKey(new A300LampInput("EXTERNAL POWER ON:1", true, "Bool"))].Units);
    }

    [Fact]
    public void A_lamps_status_box_reads_what_the_cockpit_shows()
    {
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        string key = Lamp("AC_BUS_1_OFF_LIGHT").Key;
        cache[key] = 1;
        cache[A300LampBoard.DcPowerKey] = 0;
        Assert.True(def.TryDescribeControlState(key, out var dark));
        cache[A300LampBoard.DcPowerKey] = 1;
        Assert.True(def.TryDescribeControlState(key, out var lit));
        Assert.Equal(("Off", "On"), (dark, lit));
    }

    [Fact]
    public void A_lamps_status_list_line_reads_what_the_cockpit_shows()
    {
        // MainForm's status list composes a line through TryGetDisplayOverride (UpdateDisplayText), not
        // TryDescribeControlState: measured 2026-10-09, external power available read "On" with external
        // power on, the raw value of its first variable.
        var cache = new Dictionary<string, double>();
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        var avail = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "EXT_PWR_SEQ1_LIGHT");
        cache[avail.Lamp.Key] = 1;
        cache[A300PanelLamps.InputKey(avail.Rule.Inputs.Single(i => i.IsStock))] = 1;   // external power on
        Assert.True(def.TryGetDisplayOverride(avail.Lamp.Key, 1, out var text));
        Assert.Equal("Off", text);

        string gen = A300FaultLights.All.Single(l => l.Var == "INI_elec_gen1_fault").Key;
        cache[gen] = 1;
        cache[A300LampBoard.DcPowerKey] = 0;
        Assert.True(def.TryGetDisplayOverride(gen, 1, out var dark));
        Assert.Equal("Off", dark);                                            // a fault on a dark bus is not lit
    }

    [Fact]
    public void A_lamp_change_is_spoken_by_the_definition_and_honours_its_ctrl_m_row()
    {
        var speech = new SpeechCapture();
        long now = 10_000;
        var muted = new HashSet<string>();
        var def = new IniA300Definition { Clock = () => now, IsMuted = k => muted.Contains(k) };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        string key = Lamp("DC_ESS_ONBAT_LIGHT").Key;
        void BatchEnd()
        {
            now += A300LampSpeech.GatherMs;
            def.OnContinuousBatchDelivered(1);
        }
        def.ProcessSimVarUpdate(A300LampBoard.DcPowerKey, 1, speech);
        def.ProcessSimVarUpdate(key, 0, speech);
        BatchEnd();
        def.ProcessSimVarUpdate(key, 1, speech);
        BatchEnd();
        Assert.Equal(new[] { "DC essential bus on battery light on" }, speech.All);
        muted.Add(key);
        def.ProcessSimVarUpdate(key, 0, speech);
        BatchEnd();
        Assert.Single(speech.All);
    }

    private static IEnumerable<string> Said(IReadOnlyList<A300BoardChange> changes) =>
        changes.Select(c => $"{c.Lamp.Name} {(c.On ? "on" : "off")}");
}
