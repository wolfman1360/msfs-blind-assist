using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// What the TriStar's output-mode readout keys say (F flaps, G gear, B altimeter, U fuel). Pure;
/// every number is formatted with the invariant culture, so a comma-decimal Windows locale never
/// makes "29.92" read "29,92".
/// </summary>
public static class L1011Speech
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const double KilogramsPerPound = 0.45359237;

    /// <summary>"Flap handle 14 degrees, flaps 14 degrees" or "Flap handle up, flaps 0 degrees".</summary>
    public static string Flaps(double handleIndex, double angleDegrees)
    {
        string handle = L1011Levers.FlapPositions.TryGetValue(Math.Round(handleIndex), out var w)
            ? w.ToLowerInvariant()
            : "between detents";
        return $"Flap handle {handle}, flaps {L1011Readouts.WithoutNegativeZero(Math.Round(angleDegrees).ToString("0", Inv))} degrees";
    }

    /// <summary>
    /// "Gear lever down, gear down" when all three legs read down, "gear up" when all read up,
    /// otherwise each leg's percentage ("gear in transit: left 40, nose 35, right 42 percent").
    /// </summary>
    public static string Gear(double lever, double left, double nose, double right)
    {
        string leverWord = L1011Levers.GearPositions[L1011Levers.GearDescriptionKey(lever)].ToLowerInvariant();
        string legs = left >= 99 && nose >= 99 && right >= 99 ? "gear down"
            : left <= 1 && nose <= 1 && right <= 1 ? "gear up"
            : $"gear in transit: left {Pct(left)}, nose {Pct(nose)}, right {Pct(right)} percent";
        return $"Gear lever {leverWord}, {legs}";
    }

    /// <summary>"Fuel 85,400 pounds" or, converted, "Fuel 38,737 kilograms".</summary>
    public static string Fuel(double pounds, bool kilograms) =>
        kilograms
            ? $"Fuel {(pounds * KilogramsPerPound).ToString("#,##0", Inv)} kilograms"
            : $"Fuel {pounds.ToString("#,##0", Inv)} pounds";

    /// <summary>"Altimeter 1013, 29.92": hectopascals, then inches.</summary>
    public static string Altimeter(double millibars) => L1011Levers.AltimeterConfirmation("Altimeter", millibars);

    private static string Pct(double value) => L1011Readouts.WithoutNegativeZero(Math.Round(value).ToString("0", Inv));
}
