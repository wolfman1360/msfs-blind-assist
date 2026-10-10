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
    [InlineData("APU_AVAIL_SEQ1_LIGHT", "APU available light")]
    [InlineData("APU_START_SEQ2_LIGHT", "APU start on light")]
    [InlineData("APU_START_SEQ1_LIGHT", "APU starting light")]
    [InlineData("APU_FUELSHUT_SEQ1_LIGHT", "APU fuel shutoff valve open light")]
    public void The_apu_lights_are_named_from_their_buttons(string node, string name)
    {
        Assert.Equal(name, Lamp(node).Name);
        Assert.Equal("APU", Lamp(node).Panel);
    }

    [Theory]
    [InlineData("FIRE_HANDLE_ENG1_LIGHT", "Engine 1 fire handle light", "INI_ENG1_FIRE_TEST")]
    [InlineData("FIRE_HANDLE_ENG2_LIGHT", "Engine 2 fire handle light", "INI_ENG2_FIRE_TEST")]
    [InlineData("FIRE_HANDLE_APU_LIGHT", "APU fire handle light", "INI_APU_FIRE_TEST")]
    [InlineData("FIRE_1_AG1_SEQ1_LIGHT", "Engine 1 agent 1 squib light", "INI_engine1_agent1_squib")]
    [InlineData("FIRE_1_AG2_SEQ1_LIGHT", "Engine 1 agent 2 squib light", "INI_engine1_agent2_squib")]
    [InlineData("FIRE_2_AG1_SEQ1_LIGHT", "Engine 2 agent 1 squib light", "INI_engine2_agent1_squib")]
    [InlineData("FIRE_2_AG2_SEQ1_LIGHT", "Engine 2 agent 2 squib light", "INI_engine2_agent2_squib")]
    [InlineData("FIRE_APU_AG_SEQ1_LIGHT", "APU agent squib light", "INI_apu_agent_squib")]
    public void The_fire_lights_are_named_from_their_buttons_on_the_dc_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Fire"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Dc), (lamp.Primary.Name, lamp.Power));
    }

    [Theory]
    [InlineData("OUT_TK_PMP_L1_SEQ1_LIGHT", "Left outer tank pump 1 low pressure light", "INI_OUTER_TANK1_LEFT_low_pressure")]
    [InlineData("OUT_TK_PMP_L2_SEQ1_LIGHT", "Left outer tank pump 2 low pressure light", "INI_OUTER_TANK2_LEFT_low_pressure")]
    [InlineData("OUT_TK_PMP_R1_SEQ1_LIGHT", "Right outer tank pump 1 low pressure light", "INI_OUTER_TANK1_RIGHT_low_pressure")]
    [InlineData("OUT_TK_PMP_R2_SEQ1_LIGHT", "Right outer tank pump 2 low pressure light", "INI_OUTER_TANK2_RIGHT_low_pressure")]
    [InlineData("INR_TK_PMP_L1_SEQ1_LIGHT", "Left inner tank pump 1 low pressure light", "INI_INNER_TANK1_LEFT_low_pressure")]
    [InlineData("INR_TK_PMP_L2_SEQ1_LIGHT", "Left inner tank pump 2 low pressure light", "INI_INNER_TANK2_LEFT_low_pressure")]
    [InlineData("INR_TK_PMP_R1_SEQ1_LIGHT", "Right inner tank pump 1 low pressure light", "INI_INNER_TANK1_RIGHT_low_pressure")]
    [InlineData("INR_TK_PMP_R2_SEQ1_LIGHT", "Right inner tank pump 2 low pressure light", "INI_INNER_TANK2_RIGHT_low_pressure")]
    // iniBuilds wires the left center pump's lamp to the right inner pump 1's flag: read as the cockpit lights it.
    [InlineData("CTR_TK_PMP_L_SEQ1_LIGHT", "Left center tank pump low pressure light", "INI_INNER_TANK1_RIGHT_low_pressure")]
    [InlineData("CTR_TK_PMP_R_SEQ1_LIGHT", "Right center tank pump low pressure light", "INI_CENTER_TANK2_low_pressure")]
    [InlineData("TRMTK_1_PMP_SEQ1_LIGHT", "Left trim tank pump low pressure light", "INI_trim_tank_pump1_low_pressure")]
    [InlineData("TRMTK_2_PMP_SEQ1_LIGHT", "Right trim tank pump low pressure light", "INI_trim_tank_pump2_low_pressure")]
    [InlineData("TRMTK_ISO_SEQ1_LIGHT", "Trim tank isolation valve flow bar light", "INI_TRIM_TANK_ISOL_VALVE_BAR")]
    public void The_fuel_lights_are_named_from_their_buttons_on_the_ac_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Fuel"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
    }

    [Theory]
    // The pack flow bars show the pack valve itself: with no bleed air they go out while the buttons stay On
    // (measured 2026-10-10). The ram air light is the valve, which travels after its switch.
    [InlineData("PACK_1_VALVE_IND_003_LIGHT", "Pack 1 flow bar light", "INI_bleed_pack1_percent")]
    [InlineData("PACK_2_VALVE_IND_004_LIGHT", "Pack 2 flow bar light", "INI_bleed_pack2_percent")]
    [InlineData("RAM_AIR_SEQ1_LIGHT", "Ram air valve open light", "INI_bleed_ram_air_open")]
    public void The_air_conditioning_lights_are_named_from_their_buttons_on_the_dc_light_bus(string node, string name, string var)
    {
        Assert.Equal((name, "Air Conditioning"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Dc), (lamp.Primary.Name, lamp.Power));
    }

    [Fact]
    public void The_bleed_panels_ground_bleed_valve_light_is_read()
    {
        // An indicator with no button of its own, on the AC light bus; it followed the APU bleed (2026-10-10).
        Assert.Equal(("Ground bleed valve light", "Bleed"), (Lamp("GND_BLEED_VLVE_SEQ1_LIGHT").Name, Lamp("GND_BLEED_VLVE_SEQ1_LIGHT").Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "GND_BLEED_VLVE_SEQ1_LIGHT");
        Assert.Equal(("INI_GND_BLEED", A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
    }

    [Fact]
    public void A_pack_flow_bar_is_lit_from_half_open()
    {
        var bar = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == "PACK_1_VALVE_IND_003_LIGHT");
        Assert.False(A300LampBoard.IsLit(A300LampBoard.ById[bar.Lamp.Key], k => k == bar.Lamp.Key ? 0.32 : 1));
        Assert.True(A300LampBoard.IsLit(A300LampBoard.ById[bar.Lamp.Key], k => k == bar.Lamp.Key ? 1 : 1));
    }

    [Theory]
    [InlineData("PRESS_SYS_1_SEQ1_LIGHT", "Pressurization system 1 light", "INI_cabin_sys1", "A300_PRESS_SYS_1")]
    [InlineData("PRESS_SYS_2_SEQ1_LIGHT", "Pressurization system 2 light", "INI_cabin_sys2", "A300_PRESS_SYS_2")]
    public void A_pressurization_system_button_is_labelled_by_its_light(string node, string name, string var, string button)
    {
        // The two buttons pick one system or the other (a press of the picked one does nothing), as the TRP
        // buttons pick a mode: so they are buttons, and each one's light is spoken and labels it.
        Assert.Equal((name, "Pressurization"), (Lamp(node).Name, Lamp(node).Panel));
        var lamp = A300PanelLamps.Resolved.Single(l => l.Lamp.Node == node);
        Assert.Equal((var, A300LightPower.Ac), (lamp.Primary.Name, lamp.Power));
        Assert.Equal(lamp.Lamp.Key, A300PanelLamps.ByButton[button]);

        var cache = new Dictionary<string, double> { [lamp.Lamp.Key] = 1, [A300LampBoard.AcPowerKey] = 1 };
        var def = new IniA300Definition { Cached = (_, key) => cache.TryGetValue(key, out var v) ? v : null };
        def.Attach(new SimConnectManager(IntPtr.Zero));
        Assert.Contains(lamp.Lamp.Key, def.GetVariables()[button].StateVariables!);
        Assert.True(def.TryDescribeControlState(button, out var lit));
        cache[lamp.Lamp.Key] = 0;
        Assert.True(def.TryDescribeControlState(button, out var dark));
        Assert.Equal(("On", "Off"), (lit, dark));
    }

    [Fact]
    public void The_fire_handle_lights_are_the_lamps_the_cockpit_draws()
    {
        // MSFSBA read INI_fire_handle_*_light with no light power; the handle lamps draw INI_*_FIRE_TEST on the
        // DC light bus. Both light together in the fire test (sampled 2026-10-09).
        Assert.DoesNotContain(A300FaultLights.All, l => l.Var.StartsWith("INI_fire_handle_", StringComparison.Ordinal));
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
