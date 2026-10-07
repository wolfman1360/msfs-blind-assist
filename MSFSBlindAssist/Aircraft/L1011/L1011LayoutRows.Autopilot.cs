namespace MSFSBlindAssist.Aircraft.L1011;

public static partial class L1011PanelLayout
{
    /// <summary>
    /// The glareshield autopilot (AFCS) panel, first in the Main Panel section. Its first rows follow
    /// the manual's main-panel preparation (section 7.4) item by item: the HSI navigation sources,
    /// then "Autopilot set" — speed (V2+70), runway heading set and HDG engaged, flight directors on,
    /// autopilots off, both courses, altitude set and armed, TO/GA engaged. The rest of the panel's
    /// controls follow in cockpit order, left to right: autothrottle, pitch, the turbulence button,
    /// navigation, and the AFCS warning panels' ALERT reset buttons. A typed value sits right before
    /// its knob. The typed values and the disconnect button are hand-written keys
    /// (<see cref="L1011Afcs"/>); everything else is the map's. Position words are written out where
    /// the tooltip's upper-case acronym would not survive title case ("CWS", "INS").
    /// </summary>
    private static L1011LayoutPanel AutopilotPanel() => P("Autopilot",
        R("SWITCH_AFCS_CPT_RNAV", "Captain HSI navigation source", "0=Radio;1=INS"),
        R("SWITCH_AFCS_CPT_RNAV_GPS", "Captain HSI GPS"),
        R("SWITCH_AFCS_FO_RNAV", "First officer HSI navigation source", "0=Radio;1=INS"),
        R("SWITCH_AFCS_FO_RNAV_GPW", "First officer HSI GPS"),
        R(L1011Afcs.SpeedKey, "Selected speed"),
        R("SWITCH_AFCS_SPEED_SEL", "Speed knob"),
        R(L1011Afcs.HeadingKey, "Selected heading"),
        R("SWITCH_AFCS_HDG_SEL", "Heading knob"),
        R("SWITCH_AFCS_HDG", "Heading"),
        R("SWITCH_AFCS_FD_A", "Flight director A"),
        R("SWITCH_AFCS_FD_B", "Flight director B"),
        R("SWITCH_AFCS_AP_A", "Autopilot A", "0=Command;1=CWS;2=Off"),
        R("SWITCH_AFCS_AP_B", "Autopilot B", "0=Command;1=CWS;2=Off"),
        R(L1011Afcs.DisconnectKey, "Autopilot disconnect"),
        R(L1011Afcs.Course1Key, "Selected course 1"),
        R("SWITCH_AFCS_CRS_1_SEL", "Course 1 knob"),
        R(L1011Afcs.Course2Key, "Selected course 2"),
        R("SWITCH_AFCS_CRS_2_SEL", "Course 2 knob"),
        R(L1011Afcs.AltitudeKey, "Selected altitude"),
        R("SWITCH_AFCS_ALT_SEL", "Altitude knob"),
        R("SWITCH_AFCS_ALT_MODE", "Altitude reference"),
        R("YOKE_CPT_TOGA", "Takeoff go-around button"),
        R("SWITCH_AFCS_AT", "Autothrottle"),
        R("SWITCH_AFCS_TM", "Thrust management"),
        R("SWITCH_AFCS_VNAV", "VNAV"),
        R("SWITCH_AFCS_VS", "Vertical speed"),
        R(L1011Afcs.VerticalSpeedKey, "Selected vertical speed"),
        R("SWITCH_AFCS_ALT", "Altitude hold"),
        R("SWITCH_AFCS_IAS", "IAS hold"),
        R("SWITCH_AFCS_MACH", "Mach hold"),
        R("SWITCH_AFCS_TURB", "Turbulence"),
        R("SWITCH_AFCS_ILS", "ILS"),
        R("SWITCH_AFCS_LOC", "Localizer"),
        R("SWITCH_AFCS_VOR", "VOR"),
        R("SWITCH_AFCS_INS", "INS"),
        R("SWITCH_AFCS_BC", "Back course"),
        R("SWITCH_AFCS_CPT_ALERT", "Captain autopilot alert reset"),
        R("SWITCH_AFCS_FO_ALERT", "First officer autopilot alert reset"));
}
