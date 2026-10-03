namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One warning, caution or status light.</summary>
/// <param name="Var">The L:var the cockpit model's emissive material reads (from the map's "lamps").</param>
/// <param name="Name">The spoken name; the announcement is "{Name} on" or "{Name} off".</param>
/// <param name="Panel">The layout panel whose status display shows it.</param>
public sealed record L1011Lamp(string Var, string Name, string Panel);

/// <summary>
/// The TriStar's lights that MSFS Blind Assist announces. Every variable here is read by an
/// emissive material of the cockpit model (the generator lists them all in the map's "lamps"); this
/// table picks the ones a pilot acts on and names them. Each lamp is a continuously monitored,
/// announced variable that the pilot can mute in Ctrl+M, and the change is spoken through
/// <see cref="L1011LampGate"/> (silent baseline, settle, light-test suppression).
/// </summary>
public static class L1011Annunciators
{
    public const string KeyPrefix = "LAMP_";

    /// <summary>The two annunciator light-test switches (engineer and overhead) that light every lamp.</summary>
    public static IReadOnlyList<string> LightTestKeys { get; } = new[] { "SWITCH_FE_CB_TEST", "SWITCH_LIGHTS_TEST" };

    public static string KeyFor(L1011Lamp lamp) => KeyPrefix + lamp.Var;

    public static string Announcement(L1011Lamp lamp, bool lit) => $"{lamp.Name} {(lit ? "on" : "off")}";

    public static IReadOnlyList<L1011Lamp> All { get; } = new[]
    {
        new L1011Lamp("ENG_FIRE_1", "Engine 1 fire light", "Engine Fire Handles"),
        new L1011Lamp("ENG_FIRE_2", "Engine 2 fire light", "Engine Fire Handles"),
        new L1011Lamp("ENG_FIRE_3", "Engine 3 fire light", "Engine Fire Handles"),
        new L1011Lamp("ENG_FIRE", "Engine fire warning", "Warning Lights"),
        new L1011Lamp("FIRE_APU", "APU fire light", "APU Fire"),
        new L1011Lamp("FIRE_WHEEL_WELL", "Wheel well fire light", "Fire Detection"),
        new L1011Lamp("LX_MIP_WHEEL_WELL_FIRE", "Main panel wheel well fire", "Warning Lights"),
        new L1011Lamp("LX_MIP_FIRE_LOOP", "Fire detection loop light", "Warning Lights"),
        new L1011Lamp("LX_MIP_SMOKE", "Smoke light", "Warning Lights"),
        new L1011Lamp("LX_MIP_NAC_OVHT_ENG_1", "Engine 1 nacelle overheat", "Fire Detection"),
        new L1011Lamp("LX_MIP_NAC_OVHT_ENG_2", "Engine 2 nacelle overheat", "Fire Detection"),
        new L1011Lamp("LX_MIP_NAC_OVHT_ENG_3", "Engine 3 nacelle overheat", "Fire Detection"),
        new L1011Lamp("LX_MIP_TURB_OVHT_ENG_1", "Engine 1 turbine overheat", "Fire Detection"),
        new L1011Lamp("LX_MIP_TURB_OVHT_ENG_2", "Engine 2 turbine overheat", "Fire Detection"),
        new L1011Lamp("LX_MIP_TURB_OVHT_ENG_3", "Engine 3 turbine overheat", "Fire Detection"),
        new L1011Lamp("FWD_CARGO_EXT_MAIN_FIRED", "Forward cargo main bottle discharged", "Cargo Fire and Smoke Detection"),
        new L1011Lamp("FWD_CARGO_EXT_ALTN_FIRED", "Forward cargo alternate bottle discharged", "Cargo Fire and Smoke Detection"),

        new L1011Lamp("AC_ESS_FAIL_LIGHT", "AC essential bus fail", "Electrical"),
        new L1011Lamp("AC_STBY_FAIL_LIGHT", "AC standby bus fail", "Electrical"),
        new L1011Lamp("DC_ESS_FAIL_LIGHT", "DC essential bus fail", "Electrical"),
        new L1011Lamp("DC_STBY_FAIL_LIGHT", "DC standby bus fail", "Electrical"),
        new L1011Lamp("STBY_PWR_ON_LIGHT", "Standby power on light", "Electrical"),
        new L1011Lamp("STBY_UNARM_LIGHT", "Standby power unarmed light", "Electrical"),
        new L1011Lamp("EXTERNAL_POWER_AVAILABLE", "External power available", "Electrical"),
        new L1011Lamp("LX_APU_LOW_OIL_SEQ1", "APU low oil", "APU"),

        new L1011Lamp("LX_MIP_ELEC_SYS", "Electrical system caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_ESS_POWER", "Essential power caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_DOOR_OPEN", "Door open caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_ENG_VIB", "Engine vibration caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_FLT_CONT", "Flight controls caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_FUEL_SYSTEM", "Fuel system caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_HYDRAULIC_SYS", "Hydraulic system caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_ICING", "Icing caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_LOW_BRK_PRESS", "Low brake pressure caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_OIL_PRESS_ENG_1", "Engine 1 low oil pressure", "Warning Lights"),
        new L1011Lamp("LX_MIP_OIL_PRESS_ENG_2", "Engine 2 low oil pressure", "Warning Lights"),
        new L1011Lamp("LX_MIP_OIL_PRESS_ENG_3", "Engine 3 low oil pressure", "Warning Lights"),
        new L1011Lamp("LX_MIP_RUDDER_LMTR", "Rudder limiter caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_FAIL_ARMED_ENG_2", "Engine 2 fail armed", "Warning Lights"),
        new L1011Lamp("LX_MIP_ANTI_SKID", "Anti-skid caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_ECS", "Air conditioning caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_ELEVATOR", "Elevator caution", "Warning Lights"),
        new L1011Lamp("LX_MIP_RAT_DEPLOYED", "Ram air turbine deployed", "Warning Lights"),
        new L1011Lamp("LX_MIP_WINDSHEAR_CMPTR", "Windshear computer caution", "Warning Lights"),
        new L1011Lamp("LX_CPT_DH_LIGHT", "Decision height light", "Captain Instruments"),

        new L1011Lamp("LX_GEAR_DOOR", "Gear door light", "Landing Gear and Brakes"),
        new L1011Lamp("BRK_NORM_LOW_PRESS", "Normal brakes low pressure", "Landing Gear and Brakes"),
        new L1011Lamp("BRK_ALT_LOW_PRESS", "Alternate brakes low pressure", "Landing Gear and Brakes"),
        new L1011Lamp("INI_ABRK_ARM_FAILED", "Autobrake arm failed", "Antiskid and Autobrake"),

        new L1011Lamp("LE_FLAPS_LIGHT_WASM", "Leading edge flaps light", "Speed Brake and Flaps"),
        new L1011Lamp("LE_SLATS_LIGHT_WASM", "Leading edge slats light", "Speed Brake and Flaps"),
        new L1011Lamp("LX_MIP_FLAP_INOP", "Flaps inoperative caution", "Warning Lights"),
        new L1011Lamp("LX_REVERSE_ENG_1", "Engine 1 reverser deployed", "Thrust Levers"),
        new L1011Lamp("LX_REVERSE_ENG_2", "Engine 2 reverser deployed", "Thrust Levers"),
        new L1011Lamp("LX_REVERSE_ENG_3", "Engine 3 reverser deployed", "Thrust Levers"),
        new L1011Lamp("LX_TRANSIT_ENG_1", "Engine 1 reverser in transit", "Thrust Levers"),
        new L1011Lamp("LX_TRANSIT_ENG_2", "Engine 2 reverser in transit", "Thrust Levers"),
        new L1011Lamp("LX_TRANSIT_ENG_3", "Engine 3 reverser in transit", "Thrust Levers"),
        new L1011Lamp("LX_CAB_PRESS_FAULT_SEQ1", "Cabin pressure fault", "Pressurization"),

        new L1011Lamp("SWITCH_ACS_1_FAIL", "ACS 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_ACS_2_FAIL", "ACS 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_YAW_SAS_1_FAIL", "Yaw SAS 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_YAW_SAS_2_FAIL", "Yaw SAS 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_STALL_WARN_1_FAIL", "Stall warning 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_STALL_WARN_2_FAIL", "Stall warning 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_PITCH_TRIM_1_FAIL", "Pitch trim 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_PITCH_TRIM_2_FAIL", "Pitch trim 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_DLC_AUTO_1_FAIL", "DLC 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_DLC_AUTO_2_FAIL", "DLC 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_ATS_1_FAIL", "Autothrottle system 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_ATS_2_FAIL", "Autothrottle system 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_MACH_TRIM_1_FAIL", "Mach trim 1 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_MACH_TRIM_2_FAIL", "Mach trim 2 fail", "Flight Control Electronics"),
        new L1011Lamp("SWITCH_PFCS_PITCH_1_FAIL", "Pitch monitor 1 fail", "Primary Flight Controls"),
        new L1011Lamp("SWITCH_PFCS_PITCH_2_FAIL", "Pitch monitor 2 fail", "Primary Flight Controls"),
        new L1011Lamp("SWITCH_PFCS_ROLL_1_FAIL", "Roll monitor 1 fail", "Primary Flight Controls"),
        new L1011Lamp("SWITCH_PFCS_ROLL_2_FAIL", "Roll monitor 2 fail", "Primary Flight Controls"),
        new L1011Lamp("SWITCH_MACH_FEEL_1_FAIL", "Mach feel 1 fail", "Mach Feel and Rudder Limiter"),
        new L1011Lamp("SWITCH_MACH_FEEL_2_FAIL", "Mach feel 2 fail", "Mach Feel and Rudder Limiter"),
        new L1011Lamp("SWITCH_RUDDER_LIMITER_FAIL", "Rudder limiter fail", "Mach Feel and Rudder Limiter"),
        new L1011Lamp("LX_ANTI_ICE_LEFT_FAIL", "Left wing anti-ice fail", "Anti-Ice"),
        new L1011Lamp("LX_ANTI_ICE_RIGHT_FAIL", "Right wing anti-ice fail", "Anti-Ice"),
        new L1011Lamp("SWITCH_ANTI_ICE_LEFT_AUTO_OVHT", "Left wing anti-ice overheat", "Anti-Ice"),
        new L1011Lamp("SWITCH_ANTI_ICE_RIGHT_AUTO_OVHT", "Right wing anti-ice overheat", "Anti-Ice"),
        new L1011Lamp("SWITCH_ISLN_VALVE_OVHT_2A_OVERHEAT", "Hot air duct 2A overheat", "Bleed Air"),
        new L1011Lamp("SWITCH_ISLN_VALVE_OVHT_2B_OVERHEAT", "Hot air duct 2B overheat", "Bleed Air"),
        new L1011Lamp("AVIONIC_FWD_LO_FLOW", "Forward avionics low cooling flow", "Air Conditioning"),
        new L1011Lamp("AVIONIC_MID_LO_FLOW", "Mid avionics low cooling flow", "Air Conditioning"),
        new L1011Lamp("LX_PAX_OXY_FLOW", "Passenger oxygen flow", "Passenger Oxygen"),
    };
}
