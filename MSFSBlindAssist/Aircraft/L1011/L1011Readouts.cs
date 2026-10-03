using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One gauge value shown in a panel's status display.</summary>
/// <param name="Key">The MSFSBA variable key.</param>
/// <param name="Name">The spoken name ("Tank 1 fuel").</param>
/// <param name="SimVar">The stock simulator variable it reads.</param>
/// <param name="Units">The SimConnect unit it is requested in.</param>
/// <param name="Format">A .NET numeric format applied with the invariant culture.</param>
/// <param name="Suffix">Words after the number ("pounds"); empty for none.</param>
/// <param name="Panel">The layout panel whose status display lists it.</param>
public sealed record L1011Readout(string Key, string Name, string SimVar, string Units, string Format, string Suffix, string Panel);

/// <summary>
/// The TriStar's gauges as text. Stock simulator variables are used wherever the investigation
/// found them correct on this aircraft (engines, fuel, radios, gear, flaps, trims, battery and bus
/// voltage, cabin pressure); the engineer's own needle L:vars have unverified scaling and are left
/// out. Fuel tank indices follow the aircraft's own load sheet (efb-wasm-systems.md 2.4).
/// Numbers are formatted with the invariant culture: a decimal comma would read "29,92".
/// </summary>
public static class L1011Readouts
{
    private const string SquawkUnits = "BCO16";

    public static IReadOnlyList<L1011Readout> All { get; } = Build();

    private static IReadOnlyList<L1011Readout> Build()
    {
        var list = new List<L1011Readout>
        {
            new("L1011_RO_BATTERY_VOLTS", "Battery voltage", "ELECTRICAL BATTERY VOLTAGE", "Volts", "0.0", "volts", "Electrical"),
            new("L1011_RO_MAIN_BUS_VOLTS", "Main bus voltage", "ELECTRICAL MAIN BUS VOLTAGE", "Volts", "0.0", "volts", "Electrical"),
            new("L1011_RO_APU_RPM", "APU speed", "APU PCT RPM", "Percent", "0", "percent", "APU"),
            new("L1011_RO_FUEL_TOTAL", "Total fuel", "FUEL TOTAL QUANTITY WEIGHT", "Pounds", "#,##0", "pounds", "Fuel"),
            new("L1011_RO_CABIN_ALT", "Cabin altitude", "PRESSURIZATION CABIN ALTITUDE", "Feet", "#,##0", "feet", "Pressurization"),
            new("L1011_RO_CABIN_RATE", "Cabin rate", "PRESSURIZATION CABIN ALTITUDE RATE", "Feet per minute", "#,##0", "feet per minute", "Pressurization"),
            new("L1011_RO_CABIN_DIFF", "Cabin differential pressure", "PRESSURIZATION PRESSURE DIFFERENTIAL", "Psi", "0.0", "psi", "Pressurization"),
            new("L1011_RO_GEAR_LEFT", "Left gear", "GEAR LEFT POSITION", "Percent", "0", "percent down", "Landing Gear and Brakes"),
            new("L1011_RO_GEAR_NOSE", "Nose gear", "GEAR CENTER POSITION", "Percent", "0", "percent down", "Landing Gear and Brakes"),
            new("L1011_RO_GEAR_RIGHT", "Right gear", "GEAR RIGHT POSITION", "Percent", "0", "percent down", "Landing Gear and Brakes"),
            new("L1011_RO_FLAPS_ANGLE", "Flaps", "TRAILING EDGE FLAPS LEFT ANGLE", "Degrees", "0", "degrees", "Speed Brake and Flaps"),
            new("L1011_RO_ELEVATOR_TRIM", "Stabilizer trim", "ELEVATOR TRIM POSITION", "Degrees", "0.0", "degrees", "Speed Brake and Flaps"),
            new("L1011_RO_AILERON_TRIM", "Aileron trim", "AILERON TRIM PCT", "Percent", "0", "percent", "Speed Brake and Flaps"),
            new("L1011_RO_RUDDER_TRIM", "Rudder trim", "RUDDER TRIM PCT", "Percent", "0", "percent", "Speed Brake and Flaps"),
            new("L1011_RO_SQUAWK", "Squawk", "TRANSPONDER CODE:1", SquawkUnits, "squawk", string.Empty, "Transponder"),
            new("L1011_RO_ALTIMETER_1", "Captain altimeter setting", "KOHLSMAN SETTING MB:1", "Millibars", "0", "hectopascals", "Captain Instruments"),
            new("L1011_RO_ALTIMETER_2", "First officer altimeter setting", "KOHLSMAN SETTING MB:2", "Millibars", "0", "hectopascals", "First Officer Instruments"),
            new("L1011_RO_ALTIMETER_3", "Standby altimeter setting", "KOHLSMAN SETTING MB:3", "Millibars", "0", "hectopascals", "Standby Instruments"),
        };

        string[] tanks = { "Tank 2 left inboard", "Tank 2 left outboard", "Tank 1", "Tank 1A", "Tank 3A", "Tank 3",
                           "Tank 2 right outboard", "Tank 2 right inboard" };
        for (int i = 0; i < tanks.Length; i++)
            list.Add(new($"L1011_RO_TANK_{i + 1}", $"{tanks[i]} fuel", $"FUELSYSTEM TANK WEIGHT:{i + 1}", "Pounds", "#,##0", "pounds", "Fuel"));

        for (int n = 1; n <= 4; n++)
            list.Add(new($"L1011_RO_HYD_{n}", $"Hydraulic pressure {n}", $"HYDRAULIC PRESSURE:{n}", "Psi", "#,##0", "psi", "Hydraulics"));

        for (int e = 1; e <= 3; e++)
        {
            list.Add(new($"L1011_RO_EPR_{e}", $"Engine {e} EPR", $"TURB ENG PRESSURE RATIO:{e}", "Ratio", "0.00", string.Empty, "Engine Instruments"));
            list.Add(new($"L1011_RO_N1_{e}", $"Engine {e} N1", $"TURB ENG N1:{e}", "Percent", "0.0", "percent", "Engine Instruments"));
            list.Add(new($"L1011_RO_N2_{e}", $"Engine {e} N2", $"TURB ENG N2:{e}", "Percent", "0.0", "percent", "Engine Instruments"));
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
        return list;
    }

    /// <summary>The value as the status display shows it, e.g. "12,400 pounds" or "1.42".</summary>
    public static string FormatValue(L1011Readout readout, double value)
    {
        if (readout.Units == SquawkUnits)
            return DecodeSquawk(value);
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
