using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The INS and PMS as the definition registers them: five Center Console panels first (the three
/// INS units, then the two PMS; the manual's console preparation sets INS 1 and 2, then the PMS, and INS 3
/// later, so the panels group by unit rather than follow it step by step), each with the button that opens
/// the INS window, and the INS lights on their unit's panel. Only INS 1's waypoint ALERT is announced: the
/// units light it together while they fly the same legs, and INS 1 feeds the captain's HSI and the autopilot
/// (INS 3 when the instrument source switch selects it). A READY NAV flash of one update, which a unit
/// aligning with its MSU already at NAV shows, is below the lamp gate's settle time and never spoken.</summary>
public class IniL1011InsDefinitionTests
{
    private readonly IniL1011Definition _def = new();

    [Fact]
    public void A_ready_nav_flash_of_one_update_at_nav_is_below_the_lamp_gates_settle_time()
    {
        // 2026-10-15, live (INS 2, MSU at NAV): the status rested at 2 for one 1 Hz update and READY NAV was
        // on for about a second (samples at +0 and +981 ms), then off as the status went to 1 (+1036 ms).
        // L1011LampGate speaks a change only once it has held for SettleMs (1,200 ms), so nothing is ever due.
        var gate = new L1011LampGate();
        const long t = 50_000;
        gate.Observe("INS_2_READY", lit: false, t - 5_000);   // baseline
        gate.Observe("INS_2_READY", lit: true, t);
        Assert.Empty(gate.Due(t));
        gate.Observe("INS_2_READY", lit: true, t + 1_000);
        Assert.Empty(gate.Due(t + 1_000));
        gate.Observe("INS_2_READY", lit: false, t + 1_050);
        Assert.Empty(gate.Due(t + 1_050));
        Assert.Empty(gate.Due(t + 10_000));
        Assert.True(1_050 < L1011LampGate.SettleMs);
    }

    [Fact]
    public void The_ins_and_pms_open_the_center_console()
    {
        Assert.Equal(new[] { "INS 1", "INS 2", "INS 3", "Captain PMS", "First Officer PMS", "Disconnect Handles" },
            _def.GetPanelStructure()["Center Console"].Take(6));
    }

    [Fact]
    public void An_ins_panel_opens_its_window_then_lists_its_three_selectors()
    {
        var rows = _def.GetPanelControls()["INS 2"];
        Assert.Equal(new[] { L1011InsDevices.WindowKey(L1011InsDevice.Ins2), "INS_2_ROTARY_MODE", "INS_2_ROTARY_WPT", "INS_2_ROTARY_AUTO" }, rows);
        var vars = _def.GetVariables();
        Assert.True(vars[rows[0]].RenderAsButton);
        Assert.Equal(UpdateFrequency.Never, vars[rows[0]].UpdateFrequency);
        Assert.Equal("Open INS 2 window", vars[rows[0]].DisplayName);
        Assert.Equal("Track and ground speed", vars["INS_2_ROTARY_MODE"].ValueDescriptions[0]);
        Assert.Equal("Position", vars["INS_2_ROTARY_MODE"].ValueDescriptions[3]);
        Assert.Equal("Desired track and status", vars["INS_2_ROTARY_MODE"].ValueDescriptions[7]);
        Assert.Equal("0, present position", vars["INS_2_ROTARY_WPT"].ValueDescriptions[0]);
        Assert.Equal("9", vars["INS_2_ROTARY_WPT"].ValueDescriptions[9]);
        Assert.Equal("Remote", vars["INS_2_ROTARY_AUTO"].ValueDescriptions[2]);
    }

    [Fact]
    public void Each_pms_panel_holds_its_window_button()
    {
        Assert.Equal(new[] { L1011InsDevices.WindowKey(L1011InsDevice.CaptainPms) }, _def.GetPanelControls()["Captain PMS"]);
        Assert.Equal("Open first officer PMS window",
            _def.GetVariables()[L1011InsDevices.WindowKey(L1011InsDevice.FirstOfficerPms)].DisplayName);
    }

    [Fact]
    public void The_ins_lights_sit_on_their_unit_and_only_ins_1_announces_the_waypoint_alert()
    {
        var lamps = L1011Annunciators.All.Where(l => l.Var.StartsWith("INS_", StringComparison.Ordinal)).ToList();
        Assert.Equal(10, lamps.Count);
        Assert.All(lamps, l => Assert.Equal("INS " + l.Var[4], l.Panel));
        var alert = Assert.Single(lamps, l => l.Var.EndsWith("_ALERT_INDEX", StringComparison.Ordinal));
        Assert.Equal("INS_1_ALERT_INDEX", alert.Var);   // INS 1 feeds the captain's HSI and the autopilot
        Assert.Contains(lamps, l => l.Var == "INS_1_READY" && l.Name == "INS 1 ready to navigate light");
        Assert.Contains("LAMP_INS_3_WARN", _def.GetPanelDisplayVariables()["INS 3"]);
    }
}
