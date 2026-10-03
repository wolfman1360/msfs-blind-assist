namespace MSFSBlindAssist.Aircraft.L1011;

public static partial class L1011PanelLayout
{
    /// <summary>
    /// The center console, in the manual's console-preparation order: disconnect handles, fuel and
    /// ignition switches, thrust levers, speed brake and flaps, radios, audio panels, navigation
    /// radios, ADF, transponder, weather radar. The INS keypads and the PMS are session 3.
    /// </summary>
    private static L1011LayoutPanel[] ConsolePanels() => new[]
    {
        P("Disconnect Handles",
            R("LEVER_PITCH_DISC", "Pitch disconnect handle", "0=Stowed;1=Pulled"),
            R("LEVER_ROLL_DISC", "Roll disconnect handle", "0=Stowed;1=Pulled")),

        P("Fuel and Ignition Switches",
            R("TOGGLE_CUTOFF_ENG_1", "Engine 1 fuel and ignition"),
            R("TOGGLE_CUTOFF_ENG_2", "Engine 2 fuel and ignition"),
            R("TOGGLE_CUTOFF_ENG_3", "Engine 3 fuel and ignition")),

        P("Thrust Levers",
            R("SWITCH_CPT_AT_DISCO", "Captain autothrottle disconnect"),
            R("SWITCH_FO_AT_DISCO", "First officer autothrottle disconnect")),

        P("Speed Brake and Flaps",
            R(L1011Levers.SpeedBrakeKey, "Speed brake lever"),
            R(L1011Levers.GroundSpoilersKey, "Ground spoilers"),
            R("SWITCH_SPOILER_DISABLE", "Auto spoiler disable", "0=Normal;1=Disabled"),
            R(L1011Levers.FlapHandleKey, "Flap handle")),

        P("VHF Radios",
            R(L1011Levers.ComActiveKey(1), "COM 1 active frequency"),
            R(L1011Levers.ComStandbyKey(1), "COM 1 standby frequency"),
            R("TOGGLE_COMM_TFR_1", "VHF 1 transfer"),
            R(L1011Levers.ComActiveKey(2), "COM 2 active frequency"),
            R(L1011Levers.ComStandbyKey(2), "COM 2 standby frequency"),
            R("TOGGLE_COMM_TFR_2", "VHF 2 transfer"),
            R(L1011Levers.ComActiveKey(3), "COM 3 active frequency"),
            R(L1011Levers.ComStandbyKey(3), "COM 3 standby frequency"),
            R("TOGGLE_COMM_TFR_3", "VHF 3 transfer")),

        P("Captain Audio Panel",
            R("SWITCH_RADIO_CPT_VHF_1", "Transmit VHF 1"),
            R("SWITCH_RADIO_CPT_VHF_2", "Transmit VHF 2"),
            R("SWITCH_RADIO_CPT_VHF_3", "Transmit VHF 3"),
            R("SWITCH_RADIO_CPT_HF_1", "Transmit HF 1"),
            R("SWITCH_RADIO_CPT_HF_2", "Transmit HF 2"),
            R("SWITCH_RADIO_CPT_INT", "Transmit interphone"),
            R("SWITCH_RADIO_CPT_PA", "Transmit passenger address"),
            R("ROTARY_RADIO_CPT_VHF_1", "VHF 1 volume"),
            R("ROTARY_RADIO_CPT_VHF_2", "VHF 2 volume"),
            R("ROTARY_RADIO_CPT_VHF_3", "VHF 3 volume"),
            R("ROTARY_RADIO_CPT_HF_1", "HF 1 volume"),
            R("ROTARY_RADIO_CPT_HF_2", "HF 2 volume"),
            R("ROTARY_RADIO_CPT_INT", "Interphone volume"),
            R("ROTARY_RADIO_CPT_PA", "Passenger address volume"),
            R("ROTARY_RADIO_CPT_MARKER", "Marker volume"),
            R("ROTARY_RADIO_CPT_DME_1", "DME 1 volume"),
            R("ROTARY_RADIO_CPT_DME_2", "DME 2 volume"),
            R("TOGGLE_RADIO_CPT_ADF_1", "ADF 1 ident"),
            R("TOGGLE_RADIO_CPT_ADF_2", "ADF 2 ident"),
            R("SWITCH_RADIO_CPT_VOICE", "Voice filter")),

        P("First Officer Audio Panel",
            R("SWITCH_RADIO_FO_VHF_1", "Transmit VHF 1"),
            R("SWITCH_RADIO_FO_VHF_2", "Transmit VHF 2"),
            R("SWITCH_RADIO_FO_VHF_3", "Transmit VHF 3"),
            R("SWITCH_RADIO_FO_HF_1", "Transmit HF 1"),
            R("SWITCH_RADIO_FO_HF_2", "Transmit HF 2"),
            R("SWITCH_RADIO_FO_INT", "Transmit interphone"),
            R("SWITCH_RADIO_FO_PA", "Transmit passenger address"),
            R("ROTARY_RADIO_FO_VHF_1", "VHF 1 volume"),
            R("ROTARY_RADIO_FO_VHF_2", "VHF 2 volume"),
            R("ROTARY_RADIO_FO_VHF_3", "VHF 3 volume"),
            R("ROTARY_RADIO_FO_HF_1", "HF 1 volume"),
            R("ROTARY_RADIO_FO_HF_2", "HF 2 volume"),
            R("ROTARY_RADIO_FO_INT", "Interphone volume"),
            R("ROTARY_RADIO_FO_PA", "Passenger address volume"),
            R("ROTARY_RADIO_FO_MARKER", "Marker volume"),
            R("ROTARY_RADIO_FO_DME_1", "DME 1 volume"),
            R("ROTARY_RADIO_FO_DME_2", "DME 2 volume"),
            R("TOGGLE_RADIO_FO_ADF_1", "ADF 1 ident"),
            R("TOGGLE_RADIO_FO_ADF_2", "ADF 2 ident"),
            R("SWITCH_RADIO_FO_VOICE", "Voice filter")),

        P("Navigation Radios",
            R(L1011Levers.NavFrequencyKey(1), "NAV 1 frequency"),
            R("ROTARY_NAV_1_MODE", "DME 1 mode", "0=Position 1;1=Position 2;2=Position 3;3=Position 4"),
            R(L1011Levers.NavFrequencyKey(2), "NAV 2 frequency"),
            R("ROTARY_NAV_2_MODE", "DME 2 mode", "0=Position 1;1=Position 2;2=Position 3;3=Position 4")),

        P("ADF",
            R("ROTARY_ADF_1_MODE_BIG", "ADF 1 mode", "0=Off;1=Antenna;2=ADF"),
            R("ROTARY_ADF_1_FREQ_100", "ADF 1 hundreds"),
            R("ROTARY_ADF_1_FREQ_10", "ADF 1 tens"),
            R("ROTARY_ADF_1_FREQ_1", "ADF 1 units"),
            R("ROTARY_ADF_1_MODE_SMALL", "ADF 1 volume"),
            R("ROTARY_ADF_2_MODE_BIG", "ADF 2 mode", "0=Off;1=Antenna;2=ADF"),
            R("ROTARY_ADF_2_FREQ_100", "ADF 2 hundreds"),
            R("ROTARY_ADF_2_FREQ_10", "ADF 2 tens"),
            R("ROTARY_ADF_2_FREQ_1", "ADF 2 units"),
            R("ROTARY_ADF_2_MODE_SMALL", "ADF 2 volume")),

        P("Transponder",
            R(L1011Levers.SquawkKey, "Squawk"),
            R("ROTARY_TCAS_MODE", "Transponder mode"),
            R("TOGGLE_TCAS_SYS", "Transponder select"),
            R("SWITCH_TCAS_IDENT", "Ident")),

        // iniBuilds' tooltips swap OFF and TEST; the names follow the gauge (H:SWITCH_WX_OFF selects
        // the radar's OFF mode, L:WX_Mode 2) and the variable ids, which agree with each other.
        P("Weather Radar",
            R("SWITCH_WX_OFF", "Radar off button"),
            R("SWITCH_WX_TEST", "Radar test button"),
            R("SWITCH_WX_WX", "Radar on button"),
            R("SWITCH_WX_MAP", "Radar map"),
            R("ROTARY_WX_GAIN", "Radar gain"),
            R("ROTARY_WX_TILT", "Radar tilt"),
            R("ROTARY_WX_CPT_RANGE", "Captain radar range",
                "1=25 miles;2=50 miles;3=100 miles;4=200 miles;5=300 miles"),
            R("ROTARY_WX_FO_RANGE", "First officer radar range",
                "1=25 miles;2=50 miles;3=100 miles;4=200 miles;5=300 miles"),
            R("ROTARY_WX_CPT_BRT", "Captain radar brightness"),
            R("ROTARY_WX_FO_BRT", "First officer radar brightness")),
    };
}
