using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The console and instrument controls the generated map does not cover well, written by hand from
/// the aircraft's own code: the flap handle and parking brake are stock Asobo templates, the gear
/// lever is a drag lever the HANDLING gauge interprets, the speed brake is a continuous lever, and
/// altimeters, NAV and COM frequencies and the squawk are typed values. Pure: keys, words, the RPN
/// each write sends and the words that confirm a typed value; the definition registers the
/// variables and sends the RPN through the calculator path.
/// Keys carry no colon: an L:var name with a colon is a stock-SimVar shape (CLAUDE.md).
/// </summary>
public static class L1011Levers
{
    /// <summary>A button that opens the circuit-breaker window.</summary>
    public const string BreakerListKey = "L1011_BREAKER_LIST";
    public const string FlapHandleKey = "L1011_FLAP_HANDLE";
    public const string GearLeverKey = "L1011_GEAR_LEVER";
    public const string SpeedBrakeKey = "L1011_SPEED_BRAKE";
    public const string GroundSpoilersKey = "L1011_GROUND_SPOILERS";
    public const string ParkingBrakeKey = "L1011_PARKING_BRAKE";
    public const string CaptainAltimeterKey = "L1011_ALTIMETER_CPT_SET";
    public const string FirstOfficerAltimeterKey = "L1011_ALTIMETER_FO_SET";
    public const string StandbyAltimeterKey = "L1011_ALTIMETER_STBY_SET";
    public const string SquawkKey = "L1011_SQUAWK_SET";

    public static string ComActiveKey(int radio) => $"L1011_COM{radio}_ACTIVE_SET";
    public static string ComStandbyKey(int radio) => $"L1011_COM{radio}_STANDBY_SET";
    public static string NavFrequencyKey(int radio) => $"L1011_NAV{radio}_SET";

    public static IReadOnlySet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        BreakerListKey, FlapHandleKey, GearLeverKey, SpeedBrakeKey, GroundSpoilersKey, ParkingBrakeKey,
        CaptainAltimeterKey, FirstOfficerAltimeterKey, StandbyAltimeterKey, SquawkKey,
        ComActiveKey(1), ComActiveKey(2), ComActiveKey(3),
        ComStandbyKey(1), ComStandbyKey(2), ComStandbyKey(3),
        NavFrequencyKey(1), NavFrequencyKey(2),
    };

    /// <summary>The altimeter a key sets: 1 captain, 2 first officer, 3 standby; null for any other key.</summary>
    public static int? AltimeterIndex(string key) => key switch
    {
        CaptainAltimeterKey => 1,
        FirstOfficerAltimeterKey => 2,
        StandbyAltimeterKey => 3,
        _ => null,
    };

    /// <summary>The COM radio and slot a key sets, or null.</summary>
    public static (int Radio, bool Active)? ComEntry(string key)
    {
        for (int radio = 1; radio <= 3; radio++)
        {
            if (key == ComActiveKey(radio)) return (radio, true);
            if (key == ComStandbyKey(radio)) return (radio, false);
        }
        return null;
    }

    /// <summary>The NAV radio a key sets, or null.</summary>
    public static int? NavEntry(string key) =>
        key == NavFrequencyKey(1) ? 1 : key == NavFrequencyKey(2) ? 2 : null;

    // ---- Flap handle -----------------------------------------------------------------------

    /// <summary>Flap detents from flight_model.cfg [FLAPS.0] flaps-position.0..6, in degrees.</summary>
    public static readonly int[] FlapDegrees = { 0, 4, 10, 14, 18, 22, 33 };

    public static IReadOnlyDictionary<double, string> FlapPositions { get; } =
        FlapDegrees.Select((deg, index) => (index, deg))
                   .ToDictionary(p => (double)p.index, p => p.deg == 0 ? "Up" : $"{p.deg} degrees");

    /// <summary>The aircraft's own EFB moves the handle by writing FLAPS HANDLE INDEX.</summary>
    public static string FlapRpn(int index) =>
        $"{Math.Clamp(index, 0, FlapDegrees.Length - 1)} (>A:FLAPS HANDLE INDEX, number)";

    // ---- Gear lever ------------------------------------------------------------------------

    /// <summary>L:LEVER_LANDING_GEAR as the HANDLING gauge reads it: 0 down, 50 off, 100 up.</summary>
    public static IReadOnlyDictionary<double, string> GearPositions { get; } =
        new Dictionary<double, string> { [0] = "Down", [50] = "Off", [100] = "Up" };

    /// <summary>The <see cref="GearPositions"/> key a lever value describes; the lever travels between them.</summary>
    public static double GearDescriptionKey(double value) => value < 25 ? 0 : value > 75 ? 100 : 50;

    /// <summary>Down and up go through the stock gear events, which the HANDLING gauge intercepts
    /// and turns into its own lever logic (and refuses "up" on the ground); off writes the lever.</summary>
    public static string GearRpn(double target) =>
        target < 25 ? "(>K:GEAR_DOWN)" : target > 75 ? "(>K:GEAR_UP)" : "50 (>L:LEVER_LANDING_GEAR)";

    /// <summary>How long after a gear-lever write the lever is read again, so the combo follows a
    /// refusal by the aircraft's own gauge ("up" on the ground). One 1 Hz batch period plus margin:
    /// a judgement, not a measurement.</summary>
    public const int GearSettleMs = 1500;

    // ---- Speed brake, ground spoilers, parking brake --------------------------------------

    /// <summary>The speed-brake lever input the HANDLING gauge copies into SPOILERS HANDLE POSITION.</summary>
    public static string SpeedBrakeRpn(double percent) =>
        $"{L1011Effect.FormatNumber(Math.Clamp(Math.Round(percent), 0, 100))} (>L:LEVER_SPOILERS)";

    public static IReadOnlyDictionary<double, string> ArmedPositions { get; } =
        new Dictionary<double, string> { [0] = "Disarmed", [1] = "Armed" };

    public static string GroundSpoilersRpn(double target) => $"{(target >= 0.5 ? 1 : 0)} (>K:SPOILERS_ARM_SET)";

    public static IReadOnlyDictionary<double, string> ParkingBrakePositions { get; } =
        new Dictionary<double, string> { [0] = "Released", [1] = "Set" };

    /// <summary>What the parking-brake input event's own Set code sends.</summary>
    public static string ParkingBrakeRpn(double target) => $"{(target >= 0.5 ? 1 : 0)} (>K:PARKING_BRAKE_SET)";

    /// <summary>
    /// What is spoken when a lever moves without MSFSBA moving it (a hardware lever, the aircraft's
    /// own automation): "Flaps 14 degrees", "Flaps up", "Parking brake set", "Ground spoilers armed".
    /// Null for a lever that is never announced (the gear lever travels, the speed brake streams).
    /// </summary>
    public static string? Announcement(string key, double value) => key switch
    {
        FlapHandleKey => FlapPositions.TryGetValue(Math.Round(value), out var w) ? $"Flaps {w.ToLowerInvariant()}" : null,
        ParkingBrakeKey => $"Parking brake {(value >= 0.5 ? "set" : "released")}",
        GroundSpoilersKey => $"Ground spoilers {(value >= 0.5 ? "armed" : "disarmed")}",
        _ => null,
    };

    // ---- Altimeters ------------------------------------------------------------------------

    /// <summary>Lowest and highest settings the aircraft's altimeter input accepts, inches of mercury.</summary>
    public const double MinAltimeterInHg = 28.2;
    public const double MaxAltimeterInHg = 31.3;
    public const double MillibarsPerInHg = 33.8638866667;

    /// <summary>A typed altimeter value as millibars: 28.20 to 31.30 is inches, 955 to 1060 is
    /// hectopascals; anything else is null (an error the pilot hears).</summary>
    public static double? AltimeterMillibars(double typed)
    {
        if (typed >= MinAltimeterInHg && typed <= MaxAltimeterInHg)
            return typed * MillibarsPerInHg;
        if (typed >= MinAltimeterInHg * MillibarsPerInHg - 0.5 && typed <= MaxAltimeterInHg * MillibarsPerInHg + 0.5)
            return typed;
        return null;
    }

    /// <summary>Exactly what the INSTRUMENT_ALTIMETER_n input event sends: index, then millibars × 16.</summary>
    public static string AltimeterRpn(int index, double millibars) =>
        $"{index} {L1011Effect.FormatNumber(Math.Round(millibars * 16))} (>K:2:KOHLSMAN_SET)";

    /// <summary>"Captain altimeter 1013, 29.92": hectopascals first, then inches, as the PMDG and MD-11 say it.</summary>
    public static string AltimeterConfirmation(string name, double millibars) =>
        $"{name} {Math.Round(millibars).ToString("0", CultureInfo.InvariantCulture)}, " +
        $"{(millibars / MillibarsPerInHg).ToString("0.00", CultureInfo.InvariantCulture)}";

    public const string AltimeterRangeError = "enter 28.20 to 31.30 inches, or 955 to 1060 hectopascals";

    // ---- NAV and COM radios ----------------------------------------------------------------

    /// <summary>A typed NAV frequency in megahertz as hertz, or null outside 108.00 to 117.95.</summary>
    public static uint? NavFrequencyHz(double megahertz) =>
        megahertz >= 108.0 && megahertz <= 117.95 ? (uint)Math.Round(megahertz * 1_000_000) : null;

    public static string NavFrequencyRpn(int radio, uint hertz) => $"{hertz} (>K:NAV{radio}_RADIO_SET_HZ)";

    public const string NavRangeError = "enter 108.00 to 117.95";

    /// <summary>A typed COM frequency in megahertz as hertz, or null outside 118.000 to 136.990.</summary>
    public static uint? ComFrequencyHz(double megahertz) =>
        megahertz >= 118.0 && megahertz <= 136.99 ? (uint)Math.Round(megahertz * 1_000_000) : null;

    /// <summary>
    /// The standby frequency is set with the stock event (COM 1's is the un-numbered
    /// COM_STBY_RADIO_SET_HZ); an active frequency is set into standby and then swapped, in the same
    /// string so the swap cannot overtake the set. The TriStar's radio panel follows the stock radios
    /// (design doc 5.4).
    /// </summary>
    public static string ComFrequencyRpn(int radio, uint hertz, bool active)
    {
        string standby = radio == 1 ? "COM_STBY_RADIO_SET_HZ" : $"COM{radio}_STBY_RADIO_SET_HZ";
        return active ? $"{hertz} (>K:{standby}) (>K:COM{radio}_RADIO_SWAP)" : $"{hertz} (>K:{standby})";
    }

    public const string ComRangeError = "enter 118.000 to 136.990";

    /// <summary>"COM 2 active 121.900" / "NAV 1 110.30": the radio, then the value as the pilot reads it.</summary>
    public static string FrequencyConfirmation(string name, double megahertz, int decimals) =>
        $"{name} {megahertz.ToString(decimals == 3 ? "0.000" : "0.00", CultureInfo.InvariantCulture)}";

    // ---- Squawk ----------------------------------------------------------------------------

    /// <summary>A typed squawk as the BCD16 word XPNDR_SET takes, or null when it is not four octal digits.</summary>
    public static uint? SquawkBcd(double typed)
    {
        if (double.IsNaN(typed) || typed < 0 || typed > 7777 || Math.Abs(typed - Math.Round(typed)) > 1e-9)
            return null;
        string digits = ((int)Math.Round(typed)).ToString("D4", CultureInfo.InvariantCulture);
        uint bcd = 0;
        foreach (char c in digits)
        {
            if (c < '0' || c > '7')
                return null;
            bcd = (bcd << 4) | (uint)(c - '0');
        }
        return bcd;
    }

    public static string SquawkRpn(uint bcd) => $"{bcd} (>K:XPNDR_SET)";

    public static string SquawkConfirmation(uint bcd) => $"Squawk {L1011Readouts.DecodeSquawk(bcd)}";

    public const string SquawkError = "enter four digits, each 0 to 7";
}
