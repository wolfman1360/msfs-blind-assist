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
        new A300Readout("A300_RO_BARO_FO", "First officer altimeter setting", "First Officer Panel", "KOHLSMAN SETTING MB:2", true, "Millibars",
            Altimeter),
        new A300Readout("A300_RO_BARO_STBY", "Standby altimeter setting", "Center Panel", "KOHLSMAN SETTING MB:3", true, "Millibars",
            Altimeter),
        new A300Readout("A300_RO_FLEX_TEMP", "Flex temperature", "Center Panel", "INI_FLEX_TEMPERATURE", false, "number",
            v => $"{Whole(v)} degrees"),
        new A300Readout("A300_RO_LANDING_ELEV", "Landing elevation", "Center Panel", "INI_landing_elevation", false, "number",
            v => $"{Whole(v)} feet"),
        new A300Readout("A300_RO_VOR1_COURSE", "VOR 1 course", "Navigation Radios", "NAV OBS:1", true, "Degrees",
            v => $"{Heading(v)} degrees"),
        new A300Readout("A300_RO_VOR2_COURSE", "VOR 2 course", "Navigation Radios", "NAV OBS:2", true, "Degrees",
            v => $"{Heading(v)} degrees"),
        new A300Readout("A300_RO_ILS_COURSE", "ILS course", "Navigation Radios", "INI_ils_course", false, "number",
            v => $"{Heading(v)} degrees"),
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
