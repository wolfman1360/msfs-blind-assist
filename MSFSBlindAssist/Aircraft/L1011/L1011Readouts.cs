using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One gauge value shown in a panel's status display.</summary>
/// <param name="Key">The MSFSBA variable key.</param>
/// <param name="Name">The spoken name ("Tank 1 fuel").</param>
/// <param name="SimVar">The stock simulator variable it reads, or an aircraft L:var written <c>L:NAME</c>.</param>
/// <param name="Units">The SimConnect unit it is requested in.</param>
/// <param name="Format">A .NET numeric format applied with the invariant culture.</param>
/// <param name="Suffix">Words after the number ("pounds"); empty for none.</param>
/// <param name="Panel">The layout panel whose status display lists it.</param>
/// <param name="Words">Words for a state the gauge shows as a light or flag ("closed"), in place of a number.</param>
/// <param name="Scale">What the delivered value is multiplied by before it is formatted: an L:var arrives in
/// its stored unit (every L:var is registered as "number"), so a length written in feet arrives in metres.</param>
public sealed record L1011Readout(string Key, string Name, string SimVar, string Units, string Format, string Suffix, string Panel,
    IReadOnlyDictionary<double, string>? Words = null, double Scale = 1);

/// <summary>
/// The TriStar's gauges as text. Stock simulator variables are used wherever the investigation
/// found them correct on this aircraft (fuel, radios, gear, flaps, trims, battery voltage, cabin
/// pressure); the engine gauges read the aircraft's own variables, and the electrical buses and
/// hydraulic systems their indexed modular-system variables, as the cockpit gauges do. The
/// engineer's own needle L:vars have unverified scaling and are left out. Fuel tank indices follow
/// the aircraft's own load sheet (efb-wasm-systems.md 2.4).
/// Numbers are formatted with the invariant culture: a decimal comma would read "29,92".
/// </summary>
public static class L1011Readouts
{
    private const string SquawkUnits = "BCO16";

    // Before All: static fields initialise in order, and Build reads this one.
    private static readonly IReadOnlyDictionary<double, string> ClosedOpen = new Dictionary<double, string> { [0] = "open", [1] = "closed" };
    private static readonly IReadOnlyDictionary<double, string> OffOn = new Dictionary<double, string> { [0] = "off", [1] = "on" };
    private static readonly IReadOnlyDictionary<double, string> ValveOpen = new Dictionary<double, string> { [0] = "closed", [1] = "open" };

    public static IReadOnlyList<L1011Readout> All { get; } = Build();

    private static IReadOnlyList<L1011Readout> Build()
    {
        var list = new List<L1011Readout>
        {
            new("L1011_RO_BATTERY_VOLTS", "Battery voltage", "ELECTRICAL BATTERY VOLTAGE", "Volts", "0.0", "volts", "Electrical"),
            // The modular electrical system's own buses (systems.cfg bus.9, 12, 13, 23, 21); the legacy
            // ELECTRICAL MAIN BUS VOLTAGE is never fed and read 0.0 with every bus powered.
            new("L1011_RO_AC_BUS_1_VOLTS", "AC bus 1 voltage", "ELECTRICAL BUS VOLTAGE:9", "Volts", "0", "volts", "Electrical"),
            new("L1011_RO_AC_BUS_2_VOLTS", "AC bus 2 voltage", "ELECTRICAL BUS VOLTAGE:12", "Volts", "0", "volts", "Electrical"),
            new("L1011_RO_AC_BUS_3_VOLTS", "AC bus 3 voltage", "ELECTRICAL BUS VOLTAGE:13", "Volts", "0", "volts", "Electrical"),
            new("L1011_RO_AC_ESS_VOLTS", "AC essential bus voltage", "ELECTRICAL BUS VOLTAGE:23", "Volts", "0", "volts", "Electrical"),
            new("L1011_RO_DC_ESS_VOLTS", "DC essential bus voltage", "ELECTRICAL BUS VOLTAGE:21", "Volts", "0.0", "volts", "Electrical"),
            // The generator field, generator breaker and AC tie breaker CLOSE and TRIP buttons are latches
            // the aircraft resets; the state is the relay L1011_ENGINEER_PANEL.js switches (systems.cfg relays).
            Relay("L1011_RO_GEN_1_FIELD", "Generator 1 field", 1),
            Relay("L1011_RO_GEN_1_BREAKER", "Generator 1 breaker", 5),
            Relay("L1011_RO_GEN_2_FIELD", "Generator 2 field", 2),
            Relay("L1011_RO_GEN_2_BREAKER", "Generator 2 breaker", 8),
            Relay("L1011_RO_GEN_3_FIELD", "Generator 3 field", 3),
            Relay("L1011_RO_GEN_3_BREAKER", "Generator 3 breaker", 10),
            Relay("L1011_RO_APU_GEN_FIELD", "APU generator field", 4),
            Relay("L1011_RO_APU_GEN_BREAKER", "APU generator breaker", 12),
            Relay("L1011_RO_EXT_PWR_BREAKER", "External power breaker", 13),
            Relay("L1011_RO_AC_TIE_1", "AC tie breaker 1", 7),
            Relay("L1011_RO_AC_TIE_2", "AC tie breaker 2", 9),
            Relay("L1011_RO_AC_TIE_3", "AC tie breaker 3", 11),
            new("L1011_RO_APU_RPM", "APU speed", "APU PCT RPM", "Percent", "0", "percent", "APU"),
            new("L1011_RO_FUEL_TOTAL", "Total fuel", "FUEL TOTAL QUANTITY WEIGHT", "Pounds", "#,##0", "pounds", "Fuel"),
            new("L1011_RO_CABIN_ALT", "Cabin altitude", "PRESSURIZATION CABIN ALTITUDE", "Feet", "#,##0", "feet", "Pressurization"),
            new("L1011_RO_CABIN_RATE", "Cabin rate", "PRESSURIZATION CABIN ALTITUDE RATE", "Feet per minute", "#,##0", "feet per minute", "Pressurization"),
            new("L1011_RO_CABIN_DIFF", "Cabin differential pressure", "PRESSURIZATION PRESSURE DIFFERENTIAL", "Psi", "0.0", "psi", "Pressurization"),
            // The ALT SEL knob's cabin altitude (L1011_ENGINEER_PANEL.js maps its step, L:FLT_CAB, to the
            // altitude L1011_PRESSURIZATION.js flies) and the BARO SET knob (Kohlsman setting 4).
            new("L1011_RO_CABIN_ALT_SELECTED", "Selected cabin altitude", "L:TARGET_CABIN_ALTITUDE", "number", "#,##0", "feet", "Pressurization",
                Scale: 1 / 0.3048),   // written in feet, stored and delivered in metres (live: 2,220 for 7,284 feet)
            new("L1011_RO_CABIN_BARO", "Barometric pressure setting", "KOHLSMAN SETTING MB:4", "Millibars", "0", "hectopascals", "Pressurization"),
            // The "Cabin barometric setting" knob steps Kohlsman setting 5; the outflow valves are 0 to 100 open
            // (L1011_PRESSURIZATION.js writes them unless a manual switch is on).
            new("L1011_RO_CABIN_BARO_SETTING", "Cabin barometric setting", "KOHLSMAN SETTING MB:5", "Millibars", "0", "hectopascals", "Pressurization"),
            new("L1011_RO_OUTFLOW_FWD", "Forward outflow valve", "L:OUTFLOW_VALVE_FWD", "number", "0", "percent open", "Pressurization"),
            new("L1011_RO_OUTFLOW_AFT", "Aft outflow valve", "L:OUTFLOW_VALVE_AFT", "number", "0", "percent open", "Pressurization"),
            new("L1011_RO_GEAR_LEFT", "Left gear", "GEAR LEFT POSITION", "Percent", "0", "percent down", "Landing Gear and Brakes"),
            new("L1011_RO_GEAR_NOSE", "Nose gear", "GEAR CENTER POSITION", "Percent", "0", "percent down", "Landing Gear and Brakes"),
            new("L1011_RO_GEAR_RIGHT", "Right gear", "GEAR RIGHT POSITION", "Percent", "0", "percent down", "Landing Gear and Brakes"),
            new("L1011_RO_FLAPS_ANGLE", "Flaps", "TRAILING EDGE FLAPS LEFT ANGLE", "Degrees", "0", "degrees", "Speed Brake and Flaps"),
            // The tablet's takeoff "STAB TRIM 2.38" is this 0 to 10 degree scale, so to the hundredth.
            new("L1011_RO_ELEVATOR_TRIM", "Stabilizer trim", "ELEVATOR TRIM POSITION", "Degrees", "0.00", "degrees", "Speed Brake and Flaps"),
            new("L1011_RO_STRUCTURAL_ICE", "Structural ice", "STRUCTURAL ICE PCT", "Percent", "0", "percent", "Anti-Ice"),
            new("L1011_RO_WING_ANTI_ICE_LEFT", "Left wing anti-ice valve", "L:SWITCH_ANTI_ICE_LEFT_MAN_OPEN", "number", "0", string.Empty, "Anti-Ice", ValveOpen),
            new("L1011_RO_WING_ANTI_ICE_RIGHT", "Right wing anti-ice valve", "L:SWITCH_ANTI_ICE_RIGHT_MAN_OPEN", "number", "0", string.Empty, "Anti-Ice", ValveOpen),
            new("L1011_RO_AILERON_TRIM", "Aileron trim", "AILERON TRIM PCT", "Percent", "0", "percent", "Speed Brake and Flaps"),
            new("L1011_RO_RUDDER_TRIM", "Rudder trim", "RUDDER TRIM PCT", "Percent", "0", "percent", "Speed Brake and Flaps"),
            new("L1011_RO_SQUAWK", "Squawk", "TRANSPONDER CODE:1", SquawkUnits, "squawk", string.Empty, "Transponder"),
            new("L1011_RO_ALTIMETER_1", "Captain altimeter setting", "KOHLSMAN SETTING MB:1", "Millibars", "0", "hectopascals", "Captain Instruments"),
            new("L1011_RO_ALTIMETER_2", "First officer altimeter setting", "KOHLSMAN SETTING MB:2", "Millibars", "0", "hectopascals", "First Officer Instruments"),
            new("L1011_RO_ALTIMETER_3", "Standby altimeter setting", "KOHLSMAN SETTING MB:3", "Millibars", "0", "hectopascals", "Standby Instruments"),
            // The decision height and reference speed bug knobs' values (L1011_INSTRUMENTS.js: 0 to 499 feet, knots).
            new("L1011_RO_DH_CPT", "Captain decision height", "L:ROTARY_RA_CPT_DH_SET", "number", "0", "feet", "Captain Instruments"),
            new("L1011_RO_REF_BUG_CPT", "Captain reference speed bug", "L:BUG_CPT_REF_SPEED", "number", "0", "knots", "Captain Instruments"),
            new("L1011_RO_DH_FO", "First officer decision height", "L:ROTARY_RA_FO_DH_SET", "number", "0", "feet", "First Officer Instruments"),
            new("L1011_RO_REF_BUG_FO", "First officer reference speed bug", "L:BUG_FO_REF_SPEED", "number", "0", "knots", "First Officer Instruments"),
            new("L1011_RO_AFCS_HEADING", "Heading window", "AUTOPILOT HEADING LOCK DIR", "Degrees", "000", string.Empty, "Autopilot"),
            new("L1011_RO_AFCS_SPEED", "Speed target", "L:INI_AT_TARGET", "number", "0", "knots", "Autopilot"),
            new("L1011_RO_AFCS_ALTITUDE", "Altitude window", "L:ALTITUDE_SETPOINT_0", "number", "0", "feet", "Autopilot"),
            new("L1011_RO_AFCS_VS", "Vertical speed target", "L:INI_VS_PID_SETPOINT_2", "number", "+0;-0;0", "feet per minute", "Autopilot"),
            new("L1011_RO_AFCS_COURSE_1", "Course 1 window", "NAV OBS:1", "Degrees", "000", string.Empty, "Autopilot"),
            new("L1011_RO_AFCS_COURSE_2", "Course 2 window", "NAV OBS:2", "Degrees", "000", string.Empty, "Autopilot"),
        };

        string[] tanks = { "Tank 2 left inboard", "Tank 2 left outboard", "Tank 1", "Tank 1A", "Tank 3A", "Tank 3",
                           "Tank 2 right outboard", "Tank 2 right inboard" };
        for (int i = 0; i < tanks.Length; i++)
            list.Add(new($"L1011_RO_TANK_{i + 1}", $"{tanks[i]} fuel", $"FUELSYSTEM TANK WEIGHT:{i + 1}", "Pounds", "#,##0", "pounds", "Fuel"));

        // Systems A to D: the pressure checkers on each system's junction (systems.cfg actuators 7 to
        // 10, SYSTEM_1_CHECKER to SYSTEM_4_CHECKER), which the cockpit's pressure gauges read.
        string[] systems = { "A", "B", "C", "D" };
        for (int n = 0; n < systems.Length; n++)
            list.Add(new($"L1011_RO_HYD_{systems[n]}", $"System {systems[n]} hydraulic pressure", $"HYDRAULIC ACTUATOR PRESSURE:{n + 7}", "Psi", "#,##0", "psi", "Hydraulics"));

        // The engine gauges' own variables (L1011_COCKPIT.behavior.xml): EPR, N1 and N3 (the RB211's
        // high-pressure spool, the manual's start cues), and N2, the flight engineer's engine status
        // panel gauges (manual 2.8). The stock pressure ratio read 1.34 at idle.
        for (int e = 1; e <= 3; e++)
        {
            list.Add(new($"L1011_RO_EPR_{e}", $"Engine {e} EPR", $"L:ENG{e}_EPR_CALC", "number", "0.000", string.Empty, "Engine Instruments"));
            list.Add(new($"L1011_RO_N1_{e}", $"Engine {e} N1", $"L:INI_ENG{e}_N1", "number", "0.0", "percent", "Engine Instruments"));
            list.Add(new($"L1011_RO_N2_{e}", $"Engine {e} N2", $"L:INI_ENG{e}_N2", "number", "0.0", "percent", "Engine Instruments"));
            list.Add(new($"L1011_RO_N3_{e}", $"Engine {e} N3", $"L:INI_ENG{e}_N3", "number", "0.0", "percent", "Engine Instruments"));
            list.Add(new($"L1011_RO_EGT_{e}", $"Engine {e} turbine gas temperature", $"ENG EXHAUST GAS TEMPERATURE:{e}", "Celsius", "0", "degrees", "Engine Instruments"));
            list.Add(new($"L1011_RO_FF_{e}", $"Engine {e} fuel flow", $"ENG FUEL FLOW PPH:{e}", "Pounds per hour", "#,##0", "pounds per hour", "Engine Instruments"));
            list.Add(new($"L1011_RO_OIL_PRESS_{e}", $"Engine {e} oil pressure", $"ENG OIL PRESSURE:{e}", "Psi", "0", "psi", "Engine Oil and Vibration"));
            list.Add(new($"L1011_RO_OIL_TEMP_{e}", $"Engine {e} oil temperature", $"ENG OIL TEMPERATURE:{e}", "Celsius", "0", "degrees", "Engine Oil and Vibration"));
            list.Add(new($"L1011_RO_THROTTLE_{e}", $"Engine {e} thrust lever", $"GENERAL ENG THROTTLE LEVER POSITION:{e}", "Percent", "0", "percent", "Thrust Levers"));
        }

        for (int r = 1; r <= 3; r++)
        {
            list.Add(new($"L1011_RO_COM_ACTIVE_{r}", $"COM {r} active", $"COM ACTIVE FREQUENCY:{r}", "MHz", "0.000", string.Empty, "VHF Radios"));
            list.Add(new($"L1011_RO_COM_STANDBY_{r}", $"COM {r} standby", $"COM STANDBY FREQUENCY:{r}", "MHz", "0.000", string.Empty, "VHF Radios"));
        }
        for (int r = 1; r <= 2; r++)
        {
            list.Add(new($"L1011_RO_NAV_{r}", $"NAV {r}", $"NAV ACTIVE FREQUENCY:{r}", "MHz", "0.00", string.Empty, "Navigation Radios"));
            list.Add(new($"L1011_RO_ADF_{r}", $"ADF {r}", $"ADF ACTIVE FREQUENCY:{r}", "kHz", "0", "kilohertz", "ADF"));
        }

        // Each INS's distance and time to its TO waypoint, written whatever the display selector shows
        // (capped at 999.9; the time reads 0 below 100 knots ground speed).
        for (int n = 1; n <= 3; n++)
        {
            list.Add(new($"L1011_RO_INS_{n}_DISTANCE", $"INS {n} distance to waypoint", $"L:CPT_HSI_INS_{n}_DISTANCE_NM", "number", "0.0", "miles", $"INS {n}"));
            list.Add(new($"L1011_RO_INS_{n}_TIME", $"INS {n} time to waypoint", $"L:CPT_HSI_INS_{n}_TIME_MIN", "number", "0.0", "minutes", $"INS {n}"));
        }

        // Air conditioning: the temperature each zone knob sets (L1011_ENGINEER_PANEL.js, degrees C), each
        // zone's temperature in the zone display's order (Area_1 to Area_5), each pack's temperature, and
        // the pack valves' flowbars. A zone line says "zone" and a flowbar "valve flow": the knob "Flight
        // station temperature" sets the zone and the switch "Pack 1 flow" is the valve's position.
        (string Id, string Name)[] zoneKnobs =
        {
            ("FLT_STA", "Flight station"), ("FWD_CAB", "Forward cabin"), ("MID_CAB", "Mid cabin"), ("AFT_CAB", "Aft cabin"),
        };
        foreach (var (id, name) in zoneKnobs)
            list.Add(new($"L1011_RO_SET_TEMP_{id}", $"{name} set temperature", $"L:TEMP_SET_{id}", "number", "0", "degrees", "Air Conditioning"));
        string[] zones = { "Flight station", "Forward cabin", "Mid cabin", "Aft cabin", "Galley" };
        for (int z = 0; z < zones.Length; z++)
            list.Add(new($"L1011_RO_ZONE_TEMP_{z + 1}", $"{zones[z]} zone temperature", $"PNEUMATICS AREA TEMPERATURE:{z + 1}", "Celsius", "0", "degrees", "Air Conditioning"));
        for (int p = 1; p <= 3; p++)
            list.Add(new($"L1011_RO_PACK_TEMP_{p}", $"Pack {p} temperature", $"PNEUMATICS PACK TEMPERATURE:{p}", "Celsius", "0", "degrees", "Air Conditioning"));
        for (int p = 1; p <= 3; p++)
            list.Add(Flowbar($"L1011_RO_PACK_FLOW_{p}", $"Pack {p} valve flow", $"SWITCH_PACK_FLOW_ENG_{p}_FLOWBAR", "Air Conditioning"));

        // Bleed air: the flowbar in each valve switch (1 = open with pressure).
        for (int e = 1; e <= 3; e++)
            list.Add(Flowbar($"L1011_RO_HI_PRESS_FLOW_{e}", $"Engine {e} high pressure valve flow", $"SWITCH_HI_PRESS_ENG_{e}_FLOWBAR", "Bleed Air"));
        for (int e = 1; e <= 3; e++)
            list.Add(Flowbar($"L1011_RO_ISLN_FLOW_{e}", $"Engine {e} isolation valve flow", $"SWITCH_ISLN_VALVE_ENG_{e}_FLOWBAR", "Bleed Air"));
        list.Add(Flowbar("L1011_RO_XBLEED_1_FLOW", "Crossbleed valve 1 flow", "SWITCH_XBLEED_1_FLOWBAR", "Bleed Air"));
        list.Add(Flowbar("L1011_RO_XBLEED_3_FLOW", "Crossbleed valve 3 flow", "SWITCH_XBLEED_3_FLOWBAR", "Bleed Air"));
        list.Add(Flowbar("L1011_RO_FUSELAGE_ISLN_FLOW", "Fuselage isolation valve flow", "SWITCH_APU_ISLN_VALVE_FLOWBAR", "Bleed Air"));
        return list;
    }

    /// <summary>An electrical relay as the engineer's panel shows it: closed or open.</summary>
    private static L1011Readout Relay(string key, string name, int relay) =>
        new(key, name, $"ELECTRICAL RELAY CONNECTION ON:{relay}", "Bool", "0", string.Empty, "Electrical", ClosedOpen);

    /// <summary>A valve switch's flowbar as the engineer's panel shows it: on while the valve is open with pressure.</summary>
    private static L1011Readout Flowbar(string key, string name, string lvar, string panel) =>
        new(key, name, "L:" + lvar, "number", "0", string.Empty, panel, OffOn);

    /// <summary>The value as the status display shows it, e.g. "12,400 pounds", "1.42" or "closed".</summary>
    public static string FormatValue(L1011Readout readout, double value)
    {
        if (readout.Units == SquawkUnits)
            return DecodeSquawk(value);
        if (readout.Words != null && readout.Words.TryGetValue(Math.Round(value), out var words))
            return words;
        value *= readout.Scale;
        string number = WithoutNegativeZero(value.ToString(readout.Format, CultureInfo.InvariantCulture));
        return readout.Suffix.Length == 0 ? number : $"{number} {readout.Suffix}";
    }

    /// <summary>
    /// An already-formatted number without the sign .NET leaves on a negative that rounds to zero
    /// ("-0", "-0.0"), which a screen reader reads "minus zero"; any other string is returned unchanged.
    /// </summary>
    public static string WithoutNegativeZero(string formatted) =>
        formatted.Length > 1 && formatted[0] == '-' && formatted.Any(char.IsAsciiDigit)
            && formatted.All(c => !char.IsAsciiDigit(c) || c == '0')
            ? formatted.Substring(1)
            : formatted;

    /// <summary>TRANSPONDER CODE:1 arrives as a BCD16 word (0x2000 for 2000); each nibble is a digit.</summary>
    public static string DecodeSquawk(double raw)
    {
        int bcd = (int)Math.Round(raw);
        return $"{(bcd >> 12) & 0xF}{(bcd >> 8) & 0xF}{(bcd >> 4) & 0xF}{bcd & 0xF}";
    }
}
