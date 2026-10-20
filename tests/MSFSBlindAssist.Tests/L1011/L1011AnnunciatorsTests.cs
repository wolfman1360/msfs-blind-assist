using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011AnnunciatorsTests
{
    [Fact]
    public void Every_lamp_is_a_lamp_the_cockpit_model_lights()
    {
        var lamps = new HashSet<string>(L1011ControlMap.Load().Lamps, StringComparer.OrdinalIgnoreCase);
        Assert.All(L1011Annunciators.All, l => Assert.Contains(l.Var, lamps));
    }

    [Fact]
    public void Names_and_variables_are_unique()
    {
        Assert.Equal(L1011Annunciators.All.Count, L1011Annunciators.All.Select(l => l.Var).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(L1011Annunciators.All.Count, L1011Annunciators.All.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void One_hundred_nineteen_lights_are_announced()
    {
        Assert.Equal(119, L1011Annunciators.All.Count);
    }

    [Fact]
    public void Every_light_sits_on_a_layout_panel()
    {
        var panels = L1011PanelLayout.Sections.SelectMany(s => s.Panels).Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        Assert.All(L1011Annunciators.All, l => Assert.Contains(l.Panel, panels));
    }

    [Theory]
    // The start VALVE OPEN lights (L1011_OVERHEAD_PANEL.js: lit 3 s after the starter engages, out at about 46 percent N3).
    [InlineData("SWITCH_GND_START_ENG_1_LIGHT", "Engine 1 start valve open light", "Engine Start")]
    [InlineData("SWITCH_GND_START_ENG_2_LIGHT", "Engine 2 start valve open light", "Engine Start")]
    [InlineData("SWITCH_GND_START_ENG_3_LIGHT", "Engine 3 start valve open light", "Engine Start")]
    [InlineData("LX_APU_INLET_DOOR_SEQ1", "APU doors in transit light", "APU")]
    [InlineData("LX_APU_BATTERY_SEQ1", "APU battery condition light", "APU")]
    [InlineData("LX_APU_INLET_FLOW_SEQ1", "APU inlet flow light", "APU")]
    [InlineData("LX_APU_MAX_SEQ1", "APU max mode light", "APU")]
    [InlineData("APU_FUEL_PRI", "APU primary fuel light", "APU")]
    [InlineData("APU_FUEL_SEC", "APU secondary fuel light", "APU")]
    [InlineData("SWITCH_AFCS_CPT_ENG_2_FAIL", "Engine 2 fail light", "Warning Lights")]
    [InlineData("IDG_OIL_PRESSURE_1", "IDG 1 low pressure light", "Electrical")]
    [InlineData("IDG_OIL_PRESSURE_2", "IDG 2 low pressure light", "Electrical")]
    [InlineData("IDG_OIL_PRESSURE_3", "IDG 3 low pressure light", "Electrical")]
    [InlineData("STBY_PWR_FLOWBAR_LIGHT", "Standby power flowbar light", "Electrical")]
    [InlineData("DUMP_LIGHT_1", "Tank 1 dump light", "Fuel Jettison and Engine Fuel")]
    [InlineData("DUMP_LIGHT_2L", "Tank 2 left dump light", "Fuel Jettison and Engine Fuel")]
    [InlineData("DUMP_LIGHT_2R", "Tank 2 right dump light", "Fuel Jettison and Engine Fuel")]
    [InlineData("DUMP_LIGHT_3", "Tank 3 dump light", "Fuel Jettison and Engine Fuel")]
    [InlineData("INI_ABRK_ARMED", "Autobrake armed light", "Antiskid and Autobrake")]
    [InlineData("SWITCH_REFUEL_POWER_ON", "Refuel power on light", "Fuel")]
    public void The_start_apu_electrical_jettison_and_autobrake_lights_are_announced(string var, string name, string panel)
    {
        var lamp = L1011Annunciators.All.Single(l => l.Var == var);
        Assert.Equal((name, panel), (lamp.Name, lamp.Panel));
    }

    [Fact]
    public void Only_the_captains_engine_2_fail_light_is_announced()
    {
        // The first officer's carries the same meaning, as only INS 1's waypoint ALERT is announced.
        Assert.DoesNotContain(L1011Annunciators.All, l => l.Var == "SWITCH_AFCS_FO_ENG_2_FAIL");
    }

    [Fact]
    public void The_captain_decision_height_light_is_announced()
    {
        // The DH light on the captain's attitude indicator (manual: "ADI ... with a DH light").
        var dh = L1011Annunciators.All.Single(l => l.Var == "LX_CPT_DH_LIGHT");
        Assert.Equal("Decision height light", dh.Name);
        Assert.Equal("Captain Instruments", dh.Panel);
    }

    [Fact]
    public void Announcement_wording()
    {
        var fire = L1011Annunciators.All.Single(l => l.Var == "ENG_FIRE_1");
        Assert.Equal("Engine 1 fire light on", L1011Annunciators.Announcement(fire, true));
        Assert.Equal("Engine 1 fire light off", L1011Annunciators.Announcement(fire, false));
    }
}
