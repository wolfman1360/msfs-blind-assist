using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The TriStar definition's variables and panels, built from the shipped map.</summary>
public class IniL1011DefinitionTests
{
    private static readonly IniL1011Definition Def = new();

    [Fact]
    public void Identity()
    {
        Assert.Equal("INI_L1011", Def.AircraftCode);
        Assert.Equal("iniBuilds L-1011 TriStar", Def.AircraftName);
    }

    [Fact]
    public void Stays_inside_the_simconnect_budget()
    {
        // 5 batches of 300 exist and the later sessions (autopilot, INS, EFB) add their own, so
        // session 1 keeps well under; on-request variables each cost one data definition.
        var vars = Def.GetVariables();
        Assert.InRange(vars.Values.Count(ContinuousBatchLayout.RidesBatch), 1, 900);
        Assert.InRange(vars.Values.Count(d => d.UpdateFrequency == UpdateFrequency.OnRequest), 1, 300);
    }

    [Fact]
    public void Switch_positions_are_monitored_silently_and_follow_the_cockpit()
    {
        var battery = Def.GetVariables()["TOGGLE_BATTERY"];
        Assert.Equal("TOGGLE_Battery", battery.Name);
        Assert.Equal(SimVarType.LVar, battery.Type);
        Assert.True(ContinuousBatchLayout.RidesBatch(battery));
        Assert.True(battery.ExcludeFromMonitorManager);
        Assert.True(battery.RefreshesControlWhenDefHandled(1));
        Assert.Equal(new[] { "Off", "On" }, battery.ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
    }

    [Fact]
    public void A_stock_state_variable_keeps_its_unit()
    {
        var cutoff = Def.GetVariables()["TOGGLE_CUTOFF_ENG_1"];
        Assert.Equal("FUELSYSTEM VALVE SWITCH:2", cutoff.Name);
        Assert.Equal(SimVarType.SimVar, cutoff.Type);
        Assert.Equal("Bool", cutoff.Units);
    }

    [Fact]
    public void Buttons_and_typed_entries_are_never_registered()
    {
        var vars = Def.GetVariables();
        Assert.All(vars.Values.Where(d => d.RenderAsButton), d => Assert.Equal(UpdateFrequency.Never, d.UpdateFrequency));
        Assert.Equal(UpdateFrequency.Never, vars[L1011Levers.CaptainAltimeterKey].UpdateFrequency);
        Assert.Contains("_SET", L1011Levers.CaptainAltimeterKey);
    }

    [Fact]
    public void An_empty_or_mistyped_entry_reaches_the_definition_as_not_a_number()
    {
        // MainForm hands an unparseable box to HandleUIVariableSet as 0 unless the field opts in, and
        // 0 is a valid squawk ("Squawk 0000"); NaN is refused by every typed value's own check.
        var typed = L1011Levers.Keys.Where(k => k.EndsWith("_SET", StringComparison.Ordinal)).ToList();
        Assert.Equal(12, typed.Count);   // 3 altimeters, the squawk, 6 COM and 2 NAV frequencies
        var vars = Def.GetVariables();
        Assert.All(typed, k => Assert.True(vars[k].UnparseableTextAsNaN, k));
    }

    [Fact]
    public void No_lvar_name_carries_a_space_or_colon()
    {
        // A name with either is a stock SimVar shape; registering it as an L:var breaks detection.
        var bad = Def.GetVariables().Values
            .Where(d => d.Type == SimVarType.LVar && (d.Name.Contains(' ') || d.Name.Contains(':')))
            .Select(d => d.Name)
            .ToList();
        Assert.Empty(bad);
    }

    [Fact]
    public void Lamps_are_announced_and_muteable()
    {
        var fire = Def.GetVariables()["LAMP_ENG_FIRE_1"];
        Assert.Equal("ENG_FIRE_1", fire.Name);
        Assert.Equal("Engine 1 fire light", fire.DisplayName);
        Assert.True(ContinuousBatchLayout.RidesBatch(fire));
        Assert.False(fire.ExcludeFromMonitorManager);
    }

    [Fact]
    public void Every_panel_has_a_controls_entry_and_every_display_sits_on_a_panel()
    {
        var panels = Def.GetPanelStructure().Values.SelectMany(p => p).ToHashSet();
        Assert.Equal(panels.OrderBy(p => p), Def.GetPanelControls().Keys.OrderBy(p => p));
        Assert.All(Def.GetPanelDisplayVariables().Keys, p => Assert.Contains(p, panels));
        Assert.Contains("L1011_RO_N1_1", Def.GetPanelDisplayVariables()["Engine Instruments"]);
    }

    [Fact]
    public void Readouts_format_through_the_display_override()
    {
        Assert.True(Def.TryGetDisplayOverride("L1011_RO_FUEL_TOTAL", 12400, out var text));
        Assert.Equal("12,400 pounds", text);
        Assert.False(Def.TryGetDisplayOverride("TOGGLE_BATTERY", 1, out _));
    }

    [Fact]
    public void The_monitor_manager_lists_lights_and_announced_levers_only()
    {
        var keys = Services.MonitorRowBuilder.Build(Def.GetVariables()).Select(r => r.Key).ToHashSet();
        Assert.Contains("LAMP_ENG_FIRE_1", keys);
        Assert.Contains(L1011Levers.FlapHandleKey, keys);
        Assert.DoesNotContain("TOGGLE_BATTERY", keys);
        Assert.DoesNotContain(L1011Levers.SpeedBrakeKey, keys);
    }
}
