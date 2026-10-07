using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>The autopilot as the definition registers it: the Autopilot panel, its variables and its
/// status display, built from the shipped map.</summary>
public class IniL1011AutopilotDefinitionTests
{
    private readonly IniL1011Definition _def = new();

    [Fact]
    public void The_autopilot_panel_opens_the_main_panel_section()
    {
        Assert.Equal("Autopilot", _def.GetPanelStructure()["Main Panel"][0]);
    }

    [Fact]
    public void The_panel_starts_with_the_main_panel_preparation_in_the_manuals_order()
    {
        // Manual 7.4: HSI sources, then "Autopilot set": IAS V2+70, runway heading set and HDG
        // engaged, flight directors on, autopilots off, both courses, altitude set and armed, TO/GA.
        var rows = _def.GetPanelControls()["Autopilot"];
        Assert.Equal(new[]
        {
            "SWITCH_AFCS_CPT_RNAV", "SWITCH_AFCS_CPT_RNAV_GPS", "SWITCH_AFCS_FO_RNAV", "SWITCH_AFCS_FO_RNAV_GPW",
            L1011Afcs.SpeedKey, "SWITCH_AFCS_SPEED_SEL#INC", "SWITCH_AFCS_SPEED_SEL#DEC",
            L1011Afcs.HeadingKey, "SWITCH_AFCS_HDG_SEL#INC", "SWITCH_AFCS_HDG_SEL#DEC", "SWITCH_AFCS_HDG",
            "SWITCH_AFCS_FD_A", "SWITCH_AFCS_FD_B",
            "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B", L1011Afcs.DisconnectKey,
            L1011Afcs.Course1Key, "SWITCH_AFCS_CRS_1_SEL#INC", "SWITCH_AFCS_CRS_1_SEL#DEC",
            L1011Afcs.Course2Key, "SWITCH_AFCS_CRS_2_SEL#INC", "SWITCH_AFCS_CRS_2_SEL#DEC",
            L1011Afcs.AltitudeKey, "SWITCH_AFCS_ALT_SEL#INC", "SWITCH_AFCS_ALT_SEL#DEC", "SWITCH_AFCS_ALT_MODE",
            "YOKE_CPT_TOGA",
        }, rows.Take(27));
    }

    [Fact]
    public void The_rest_follows_the_glareshield_left_to_right_then_the_alert_resets()
    {
        var rows = _def.GetPanelControls()["Autopilot"];
        Assert.Equal(new[]
        {
            "SWITCH_AFCS_AT", "SWITCH_AFCS_TM",
            "SWITCH_AFCS_VNAV", "SWITCH_AFCS_VS", L1011Afcs.VerticalSpeedKey, "SWITCH_AFCS_ALT", "SWITCH_AFCS_IAS", "SWITCH_AFCS_MACH",
            "SWITCH_AFCS_TURB",
            "SWITCH_AFCS_ILS", "SWITCH_AFCS_LOC", "SWITCH_AFCS_VOR", "SWITCH_AFCS_INS", "SWITCH_AFCS_BC",
            "SWITCH_AFCS_CPT_ALERT", "SWITCH_AFCS_FO_ALERT",
        }, rows.Skip(27));
    }

    [Fact]
    public void Buttons_are_never_registered_and_carry_their_panel_names()
    {
        var vars = _def.GetVariables();
        foreach (var (key, name) in new[]
        {
            (L1011Afcs.DisconnectKey, "Autopilot disconnect"),
            ("YOKE_CPT_TOGA", "Takeoff go-around button"),
            ("SWITCH_AFCS_CPT_ALERT", "Captain autopilot alert reset"),
            ("SWITCH_AFCS_FO_ALERT", "First officer autopilot alert reset"),
        })
        {
            Assert.True(vars[key].RenderAsButton, key);
            Assert.Equal(UpdateFrequency.Never, vars[key].UpdateFrequency);
            Assert.Equal(name, vars[key].DisplayName);
        }
    }

    [Fact]
    public void Typed_values_are_text_boxes_that_are_never_read_and_refuse_an_empty_box()
    {
        foreach (var key in L1011Afcs.Keys)
        {
            var def = _def.GetVariables()[key];
            Assert.Equal(UpdateFrequency.Never, def.UpdateFrequency);
            Assert.Equal("MSFSBA_" + key, def.Name);
            Assert.True(def.UnparseableTextAsNaN, key);
            Assert.EndsWith("_SET", key);
        }
    }

    [Fact]
    public void Mode_buttons_and_engage_switches_keep_a_ctrl_m_row_and_silent_switches_do_not()
    {
        var vars = _def.GetVariables();
        Assert.False(vars["SWITCH_AFCS_HDG"].ExcludeFromMonitorManager);
        Assert.False(vars["SWITCH_AFCS_AP_A"].ExcludeFromMonitorManager);
        Assert.True(vars["SWITCH_AFCS_ALT_MODE"].ExcludeFromMonitorManager);
        Assert.True(vars["SWITCH_AFCS_CPT_RNAV"].ExcludeFromMonitorManager);
        Assert.True(ContinuousBatchLayout.RidesBatch(vars["SWITCH_AFCS_HDG"]));
        Assert.Equal(new[] { "Command", "CWS", "Off" }, vars["SWITCH_AFCS_AP_A"].ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.Equal(new[] { "Standby", "Normal" }, vars["SWITCH_AFCS_ALT_MODE"].ValueDescriptions.OrderBy(p => p.Key).Select(p => p.Value));
    }

    [Fact]
    public void Armed_and_captured_flags_are_monitored_and_muteable()
    {
        var flag = _def.GetVariables()["ANN_ILS_ACTIVE"];
        Assert.Equal("ANN_ILS_ACTIVE", flag.Name);
        Assert.Equal("Glideslope captured", flag.DisplayName);
        Assert.Equal(SimVarType.LVar, flag.Type);
        Assert.True(ContinuousBatchLayout.RidesBatch(flag));
        Assert.False(flag.ExcludeFromMonitorManager);
    }

    [Fact]
    public void An_armed_flare_reads_armed()
    {
        var vars = _def.GetVariables();
        Assert.Equal("armed", vars["FLARE_ACTIVE"].ValueDescriptions![99]);   // L1011_INS.js: 99 while armed above 120 ft
        Assert.Equal("on", vars["FLARE_ACTIVE"].ValueDescriptions![1]);
        Assert.False(vars["ANN_LOC_ARM"].ValueDescriptions!.ContainsKey(99));
    }

    [Fact]
    public void The_status_display_lists_the_pitch_mode_the_flags_and_the_targets()
    {
        var shown = _def.GetPanelDisplayVariables()["Autopilot"];
        Assert.Contains(IniL1011Definition.PitchModeKey, shown);
        Assert.Contains("ANN_LOC_ARM", shown);
        Assert.Contains("L1011_RO_AFCS_HEADING", shown);
        var pitch = _def.GetVariables()[IniL1011Definition.PitchModeKey];
        Assert.Equal("VS_MASTER_SOURCE", pitch.Name);
        Assert.Equal(UpdateFrequency.OnRequest, pitch.UpdateFrequency);
        Assert.Equal("Vertical speed", pitch.ValueDescriptions[2]);
    }

    [Fact]
    public void A_readout_of_an_aircraft_variable_is_an_lvar()
    {
        var speed = _def.GetVariables()["L1011_RO_AFCS_SPEED"];
        Assert.Equal("INI_AT_TARGET", speed.Name);
        Assert.Equal(SimVarType.LVar, speed.Type);
        var heading = _def.GetVariables()["L1011_RO_AFCS_HEADING"];
        Assert.Equal("AUTOPILOT HEADING LOCK DIR", heading.Name);
        Assert.Equal(SimVarType.SimVar, heading.Type);
    }
}
