namespace MSFSBlindAssist.Aircraft.L1011;

public static partial class L1011PanelLayout
{
    /// <summary>
    /// The main instrument panel: the glareshield autopilot first (its HSI navigation sources lead,
    /// as in the manual's main-panel preparation), then instrument sources, warning lights, each
    /// pilot's instruments, standby instruments, landing gear and brakes, engine instruments, the
    /// surface position indicator and pilot lighting.
    /// </summary>
    private static L1011LayoutPanel[] MainPanels() => new[]
    {
        AutopilotPanel(),

        P("Captain Instrument Sources",
            R("SWITCH_INSTR_SRC_CPT_NAV", "Captain navigation source"),
            R("SWITCH_INSTR_SRC_CPT_HDG", "Captain heading source"),
            R("SWITCH_INSTR_SRC_CPT_ATT", "Captain attitude source"),
            R("SWITCH_INSTR_SRC_CPT_ALT", "Captain altitude source"),
            R("SWITCH_INSTR_SRC_CPT_IAS", "Captain airspeed source"),
            R("SWITCH_INSTR_SRC_CPT_FLT_DIR", "Captain flight director source")),

        P("First Officer Instrument Sources",
            R("SWITCH_INSTR_SRC_FO_NAV", "First officer navigation source"),
            R("SWITCH_INSTR_SRC_FO_HDG", "First officer heading source"),
            R("SWITCH_INSTR_SRC_FO_ATT", "First officer attitude source"),
            R("SWITCH_INSTR_SRC_FO_ALT", "First officer altitude source"),
            R("SWITCH_INSTR_SRC_FO_IAS", "First officer airspeed source"),
            R("SWITCH_INSTR_SRC_FO_FLT_DIR", "First officer flight director source")),

        P("Warning Lights",
            R("SWITCH_AFCS_CPT_FIRE", "Captain master fire warning"),
            R("SWITCH_AFCS_FO_FIRE", "First officer master fire warning"),
            R("SWITCH_AFCS_CPT_ENG_2_FAIL", "Captain engine 2 fail light"),
            R("SWITCH_AFCS_FO_ENG_2_FAIL", "First officer engine 2 fail light"),
            R("SWITCH_MIP_ANN_RESET", "Annunciator reset"),
            R("SWITCH_CPT_GPWS", "Captain GPWS inhibit"),
            R("SWITCH_FO_GPWS", "First officer GPWS inhibit")),

        P("Captain Instruments",
            R(L1011Levers.CaptainAltimeterKey, "Captain altimeter"),
            R("BUG_CPT_REF_SET", "Captain speed bug"),
            R("ROTARY_CPT_DH", "Captain decision height"),
            R("LX_CPT_DH", "Captain decision height light"),
            R("LX_CPT_OUTER", "Captain outer marker test"),
            R("LX_CPT_MIDDLE", "Captain middle marker test"),
            R("LX_CPT_INNER", "Captain inner marker test"),
            R("SWITCH_TCAS_CPT_ON", "Captain TCAS range increase"),
            R("SWITCH_TCAS_CPT_RNG", "Captain TCAS range decrease"),
            R("ROTARY_TCAS_CPT_BRT", "Captain TCAS brightness")),

        P("First Officer Instruments",
            R(L1011Levers.FirstOfficerAltimeterKey, "First officer altimeter"),
            R("BUG_FO_REF_SET", "First officer speed bug"),
            R("ROTARY_FO_DH", "First officer decision height"),
            R("LX_FO_DH", "First officer decision height light"),
            R("LX_FO_OUTER", "First officer outer marker test"),
            R("LX_FO_MIDDLE", "First officer middle marker test"),
            R("LX_FO_INNER", "First officer inner marker test"),
            R("SWITCH_TCAS_FO_ON", "First officer TCAS range increase"),
            R("SWITCH_TCAS_FO_RNG", "First officer TCAS range decrease"),
            R("ROTARY_TCAS_FO_BRT", "First officer TCAS brightness")),

        P("Standby Instruments",
            R(L1011Levers.StandbyAltimeterKey, "Standby altimeter"),
            R("STBY_HSI_CAGE", "Standby horizon cage")),

        P("Landing Gear and Brakes",
            R(L1011Levers.GearLeverKey, "Landing gear lever"),
            R("TOGGLE_BRAKE_SYS_SEL", "Brake system"),
            R(L1011Levers.ParkingBrakeKey, "Parking brake")),

        P("Engine Instruments"),

        P("Surface Position Indicator",
            R("SWITCH_SPI_AILERON", "Outboard aileron indication")),

        P("Pilot Lighting",
            R("ROTARY_CPT_GLARESHIELD", "Glareshield lights"),
            R("ROTARY_CPT_INSTR", "Captain instrument lights"),
            R("ROTARY_CPT_FLOOD", "Captain flood lights"),
            R("ROTARY_CPT_UTILITY", "Captain utility light"),
            R("SWITCH_CPT_UTILITY_BRT", "Captain utility light switch"),
            R("N_SIDE_003", "Captain side panel light"),
            R("ROTARY_FO_INSTR", "First officer instrument lights"),
            R("ROTARY_FO_FLOOD", "First officer flood lights"),
            R("ROTARY_FO_UTILITY", "First officer utility light"),
            R("SWITCH_FO_UTILITY_BRT", "First officer utility light switch"),
            R("ROTARY_ENG_INSTR_LIGHTS_KGS", "Engine instrument lights"),
            R("ROTARY_CTR_CONSOLE_LIGHT", "Center console lights")),
    };
}
