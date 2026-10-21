namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// The pressurization maths behind the TriStar First Officer's cruise cabin altitude, all pure:
///
///   The standard atmosphere (<see cref="IsaPressureHpa"/>, <see cref="IsaAltitudeFt"/>).
///
///   The cabin altitude that holds the aircraft's maximum differential at a cruise altitude
///   (<see cref="CabinAltitudeAtMaxDifferentialFt"/>), clamped to the range the selector can command.
///
///   The cabin altitude selector's knob table (<see cref="KnobTable"/>): the aircraft's own mapping from
///   knob step (0 to 100) to the cabin altitude the knob commands, linear between its points
///   (<see cref="CabinAltitudeForStep"/>, <see cref="KnobStep"/>).
///
/// Numbers only, no parsing or formatting: nothing here depends on the PC's culture.
/// </summary>
public static class L1011Pressurization
{
    /// <summary>The TriStar's maximum cabin-to-outside pressure differential, in psi.</summary>
    public const double MaxDifferentialPsi = 8.42;

    public const double HectopascalsPerPsi = 68.9476;

    /// <summary>The selector's lowest cabin altitude: below sea level.</summary>
    public const int MinCabinAltitudeFt = -250;

    /// <summary>The selector's highest cabin altitude.</summary>
    public const int MaxCabinAltitudeFt = 8000;

    public const double SeaLevelHpa = 1013.25;

    public const double TropopauseFt = 36089.24;

    private const double TroposphereLapse = 6.87535e-6;
    private const double TroposphereExponent = 5.2559;
    private const double TropopausePressureHpa = 226.32;
    private const double StratosphereScaleFt = 20805.8;

    /// <summary>
    /// The aircraft's cabin altitude selector, knob step to cabin altitude, linear between points. The
    /// first and last points are the selector's limits ([-250 .. 8,000] ft).
    /// </summary>
    public static readonly (double Step, int FeetAt)[] KnobTable =
    {
        (0, -250),
        (3, 0),
        (13, 1000),
        (23, 2000),
        (33, 3000),
        (44, 4000),
        (54, 5000),
        (64, 6000),
        (74.5, 7000),
        (100, 8000),
    };

    /// <summary>The standard atmosphere's static pressure at a pressure altitude, in hectopascals.</summary>
    public static double IsaPressureHpa(double altitudeFt)
    {
        if (altitudeFt < TropopauseFt)
            return SeaLevelHpa * Math.Pow(1 - TroposphereLapse * altitudeFt, TroposphereExponent);

        return TropopausePressureHpa * Math.Exp(-(altitudeFt - TropopauseFt) / StratosphereScaleFt);
    }

    /// <summary>
    /// The pressure altitude of a static pressure in the troposphere, the inverse of
    /// <see cref="IsaPressureHpa"/> below the tropopause. Every cabin lies there.
    /// </summary>
    public static double IsaAltitudeFt(double pressureHpa)
        => (1 - Math.Pow(pressureHpa / SeaLevelHpa, 1 / TroposphereExponent)) / TroposphereLapse;

    /// <summary>
    /// The cabin altitude at which the outside pressure at <paramref name="cruiseFt"/> plus the maximum
    /// differential equals the cabin pressure: the lowest cabin altitude that stays inside the aircraft's
    /// differential limit. Rounded to the nearest foot and clamped to what the selector can command
    /// (-250 to 8,000 ft).
    /// </summary>
    public static int CabinAltitudeAtMaxDifferentialFt(double cruiseFt)
    {
        double cabinHpa = IsaPressureHpa(cruiseFt) + MaxDifferentialPsi * HectopascalsPerPsi;
        double cabinFt = IsaAltitudeFt(cabinHpa);
        double clamped = Math.Clamp(cabinFt, MinCabinAltitudeFt, MaxCabinAltitudeFt);
        return (int)Math.Round(clamped, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The cabin altitude, in feet, a knob at <paramref name="step"/> commands: linear between the
    /// table's points, clamped to the table's ends.
    /// </summary>
    public static double CabinAltitudeForStep(double step)
    {
        var first = KnobTable[0];
        var last = KnobTable[^1];
        if (step <= first.Step) return first.FeetAt;
        if (step >= last.Step) return last.FeetAt;

        for (int i = 1; i < KnobTable.Length; i++)
        {
            var (lowStep, lowFeet) = KnobTable[i - 1];
            var (highStep, highFeet) = KnobTable[i];
            if (step <= highStep)
                return lowFeet + (step - lowStep) / (highStep - lowStep) * (highFeet - lowFeet);
        }

        return last.FeetAt;
    }

    /// <summary>
    /// The knob step (0 to 100) that commands <paramref name="cabinAltitudeFt"/>: the inverse of
    /// <see cref="CabinAltitudeForStep"/>, linear between the table's points, clamped to the table's ends
    /// and rounded to a whole step, a half step away from zero.
    /// </summary>
    public static int KnobStep(double cabinAltitudeFt)
    {
        var first = KnobTable[0];
        var last = KnobTable[^1];
        if (cabinAltitudeFt <= first.FeetAt) return (int)first.Step;
        if (cabinAltitudeFt >= last.FeetAt) return (int)last.Step;

        for (int i = 1; i < KnobTable.Length; i++)
        {
            var (lowStep, lowFeet) = KnobTable[i - 1];
            var (highStep, highFeet) = KnobTable[i];
            if (cabinAltitudeFt <= highFeet)
            {
                double step = lowStep + (cabinAltitudeFt - lowFeet) / (highFeet - lowFeet) * (highStep - lowStep);
                return (int)Math.Round(step, MidpointRounding.AwayFromZero);
            }
        }

        return (int)last.Step;
    }
}
