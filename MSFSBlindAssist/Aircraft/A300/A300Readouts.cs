using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One value shown in a panel's status display.</summary>
public sealed record A300Readout(string Key, string Name, string Panel, string Var, bool IsStock, string Units,
    Func<double, string> Format);

/// <summary>
/// The readouts part 1 places beside the knobs that set them: the four FCU windows, the three
/// altimeter settings, the flex temperature, the landing elevation and the course knobs. The radio
/// frequencies come with the radio work in part 3. Every number is formatted with the invariant
/// culture, so a comma-decimal Windows locale never makes "29.92" read "29,92".
/// </summary>
public static class A300Readouts
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public const string SpeedKey = "A300_RO_FCU_SPEED";
    public const string HeadingKey = "A300_RO_FCU_HEADING";
    public const string AltitudeKey = "A300_RO_FCU_ALTITUDE";
    public const string VerticalSpeedKey = "A300_RO_FCU_VS";
    public const string BaroCaptainKey = "A300_RO_BARO_CPT";
    public const string BaroFirstOfficerKey = "A300_RO_BARO_FO";
    public const string BaroStandbyKey = "A300_RO_BARO_STBY";
    public const string FuelTotalKey = "A300_RO_FUEL_TOTAL";
    public const string FlexTemperatureKey = "A300_RO_FLEX_TEMP";

    // The PFD status box (A300DisplayPanels).
    public const string PfdHeadingKey = "A300_RO_PFD_HEADING";
    public const string PfdAirspeedKey = "A300_RO_PFD_IAS";
    public const string PfdVerticalSpeedKey = "A300_RO_PFD_VS";
    public const string PfdRadioAltitudeKey = "A300_RO_PFD_RA";
    public const string VlsKey = "A300_RO_VLS";
    public const string VmaxKey = "A300_RO_VMAX";
    public const string GreenDotKey = "A300_RO_GREEN_DOT";
    public const string SSpeedKey = "A300_RO_S_SPEED";
    public const string FSpeedKey = "A300_RO_F_SPEED";
    public const string VsSpeedKey = "A300_RO_VS_SPEED";
    public const string MinimumsKey = "A300_RO_MINIMUMS";

    // The ND status box (A300DisplayPanels). VOR 1 and 2 are NAV 1 and 2, and the ILS is NAV 3
    // (measured 2026-10-03: the radio panel's ILS frequency shows on NAV 3).
    public const string WaypointDistanceKey = "A300_RO_ND_WPT_DIST";
    public const string TrueAirspeedKey = "A300_RO_ND_TAS";
    public const string WindDirectionKey = "A300_RO_ND_WIND_DIR";
    public const string WindSpeedKey = "A300_RO_ND_WIND_SPEED";
    public const string Vor1FrequencyKey = "A300_RO_ND_VOR1_FREQ";
    public const string Dme1Key = "A300_RO_ND_DME1";
    public const string Vor2FrequencyKey = "A300_RO_ND_VOR2_FREQ";
    public const string Dme2Key = "A300_RO_ND_DME2";
    public const string IlsFrequencyKey = "A300_RO_ND_ILS_FREQ";
    public const string IlsCourseKey = "A300_RO_ILS_COURSE";
    public const string LocalizerKey = "A300_RO_ND_LOC";
    public const string GlideslopeKey = "A300_RO_ND_GS";
    public const string Adf1FrequencyKey = "A300_RO_ND_ADF1_FREQ";
    public const string Adf2FrequencyKey = "A300_RO_ND_ADF2_FREQ";

    // The standby instruments box.
    public const string StandbyAltitudeKey = "A300_RO_STBY_ALTITUDE";
    public const string StandbyCompassKey = "A300_RO_STBY_COMPASS";

    public static readonly IReadOnlyList<A300Readout> All = new[]
    {
        new A300Readout("A300_RO_FCU_SPEED", "Speed window", "FCU", "INI_Airspeed_Dial", false, "number",
            v => $"{Whole(v)} knots"),
        new A300Readout("A300_RO_FCU_HEADING", "Heading window", "FCU", "INI_HEADING_DIAL", false, "number",
            v => $"{Heading(v)} degrees"),
        new A300Readout("A300_RO_FCU_ALTITUDE", "Altitude window", "FCU", "INI_Altitude_Dial", false, "number",
            v => $"{Math.Round(v).ToString("#,0", Inv)} feet"),
        new A300Readout("A300_RO_FCU_VS", "Vertical speed window", "FCU", "INI_vvi_dial", false, "number",
            v => $"{Whole(v)} feet per minute"),
        new A300Readout("A300_RO_BARO_CPT", "Captain altimeter setting", "Captain Panel", "KOHLSMAN SETTING MB:1", true, "Millibars",
            Altimeter),
        new A300Readout(BaroFirstOfficerKey, "First officer altimeter setting", "First Officer Panel", "KOHLSMAN SETTING MB:2", true, "Millibars",
            Altimeter),
        new A300Readout(BaroStandbyKey, "Standby altimeter setting", "Center Panel", "KOHLSMAN SETTING MB:3", true, "Millibars",
            Altimeter),
        new A300Readout("A300_RO_FLEX_TEMP", "Flex temperature", "Center Panel", "INI_FLEX_TEMPERATURE", false, "number",
            v => $"{Whole(v)} degrees"),
        new A300Readout("A300_RO_LANDING_ELEV", "Landing elevation", "Center Panel", "INI_landing_elevation", false, "number",
            v => $"{Whole(v)} feet"),
        new A300Readout("A300_RO_VOR1_COURSE", "VOR 1 course", "Navigation Radios", "NAV OBS:1", true, "Degrees",
            v => $"{Heading(v)} degrees"),
        new A300Readout("A300_RO_VOR2_COURSE", "VOR 2 course", "Navigation Radios", "NAV OBS:2", true, "Degrees",
            v => $"{Heading(v)} degrees"),
        new A300Readout(IlsCourseKey, "ILS course", "Navigation Radios", "INI_ils_course", false, "number",
            v => $"{Heading(v)} degrees"),
        new A300Readout(FuelTotalKey, "Total fuel", "Fuel", "FUEL TOTAL QUANTITY WEIGHT", true, "pounds",
            v => $"{Math.Round(v).ToString("#,0", Inv)} pounds"),

        // The PFD: what the speed tape, attitude and altitude show. VMAX is the top of the tape
        // (VMO, or the gear or flap limit: 270 with the gear down), green dot, S and F are read only
        // at the flap settings the tape shows them (the definition's display text), and VS is the
        // aircraft's own name for the speed at the bottom of the tape.
        new A300Readout(PfdHeadingKey, "Heading", "PFD", "PLANE HEADING DEGREES MAGNETIC", true, "degrees", Heading),
        new A300Readout(PfdAirspeedKey, "Indicated airspeed", "PFD", "AIRSPEED INDICATED", true, "knots",
            v => $"{Whole(v)} knots"),
        new A300Readout(PfdVerticalSpeedKey, "Vertical speed", "PFD", "VERTICAL SPEED", true, "feet per minute", A300DisplayText.VerticalSpeed),
        new A300Readout(PfdRadioAltitudeKey, "Radio altitude", "PFD", "RADIO HEIGHT", true, "feet", A300DisplayText.Feet),
        new A300Readout(VlsKey, "VLS", "PFD", "INI_VLS_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(VmaxKey, "VMAX", "PFD", "INI_max_speed", false, "number", A300DisplayText.Speed),
        new A300Readout(GreenDotKey, "Green dot", "PFD", "FMGEC_GD_SPD", false, "number", A300DisplayText.Speed),
        new A300Readout(SSpeedKey, "S speed", "PFD", "FCPC_S_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(FSpeedKey, "F speed", "PFD", "FCPC_F_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(VsSpeedKey, "VS", "PFD", "INI_VS_SPEED", false, "number", A300DisplayText.Speed),
        new A300Readout(MinimumsKey, "Minimums", "PFD", "INI_MINIMUMS_PILOT", false, "number", A300DisplayText.Minimums),

        // The ND. The waypoint's name and bearing are not aircraft variables (the ND computes them
        // from its flight plan); the MCDU's F-PLN page reads them.
        new A300Readout(WaypointDistanceKey, "Distance to next waypoint", "ND", "FMGS_DIST", false, "number", A300DisplayText.WaypointDistance),
        new A300Readout(TrueAirspeedKey, "True airspeed", "ND", "AIRSPEED TRUE", true, "knots", A300DisplayText.Knots),
        new A300Readout(WindDirectionKey, "Wind direction", "ND", "AMBIENT WIND DIRECTION", true, "degrees", A300DisplayText.WindDirection),
        new A300Readout(WindSpeedKey, "Wind speed", "ND", "AMBIENT WIND VELOCITY", true, "knots", A300DisplayText.Knots),
        new A300Readout(Vor1FrequencyKey, "VOR 1", "ND", "NAV ACTIVE FREQUENCY:1", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(Dme1Key, "DME 1", "ND", "NAV DME:1", true, "nautical miles", A300DisplayText.Dme),
        new A300Readout(Vor2FrequencyKey, "VOR 2", "ND", "NAV ACTIVE FREQUENCY:2", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(Dme2Key, "DME 2", "ND", "NAV DME:2", true, "nautical miles", A300DisplayText.Dme),
        new A300Readout(IlsFrequencyKey, "ILS", "ND", "NAV ACTIVE FREQUENCY:3", true, "MHz", A300DisplayText.Megahertz),
        new A300Readout(LocalizerKey, "Localizer", "ND", "NAV HAS LOCALIZER:3", true, "Bool", A300DisplayText.Received),
        new A300Readout(GlideslopeKey, "Glideslope", "ND", "NAV HAS GLIDE SLOPE:3", true, "Bool", A300DisplayText.Received),
        new A300Readout(Adf1FrequencyKey, "ADF 1", "ND", "ADF ACTIVE FREQUENCY:1", true, "KHz", A300DisplayText.Kilohertz),
        new A300Readout(Adf2FrequencyKey, "ADF 2", "ND", "ADF ACTIVE FREQUENCY:2", true, "KHz", A300DisplayText.Kilohertz),

        // The standby instruments: the standby altimeter has its own baro setting (altimeter 3).
        new A300Readout(StandbyAltitudeKey, "Standby altitude", "Standby Instruments", "INDICATED ALTITUDE:3", true, "feet", A300DisplayText.Feet),
        new A300Readout(StandbyCompassKey, "Standby compass", "Standby Instruments", "WISKEY COMPASS INDICATION DEGREES", true, "degrees", Heading),
    };

    /// <summary>The three speeds the tape shows only at some flap settings.</summary>
    public static readonly IReadOnlyDictionary<string, A300PfdSpeed> FlapSpeeds = new Dictionary<string, A300PfdSpeed>(StringComparer.Ordinal)
    {
        [GreenDotKey] = A300PfdSpeed.GreenDot,
        [SSpeedKey] = A300PfdSpeed.S,
        [FSpeedKey] = A300PfdSpeed.F,
    };

    /// <summary>"1013 hectopascals, 29.92 inches".</summary>
    public static string Altimeter(double millibars) =>
        $"{Math.Round(millibars).ToString("0", Inv)} hectopascals, {(millibars * 0.0295299830714).ToString("0.00", Inv)} inches";

    /// <summary>Headings are shown as the window shows them: floored, three digits, 360 as 000.</summary>
    public static string Heading(double degrees) =>
        (((int)Math.Floor(degrees) % 360 + 360) % 360).ToString("000", Inv);

    private static string Whole(double value)
    {
        string text = Math.Round(value).ToString("0", Inv);
        return text == "-0" ? "0" : text;
    }
}
