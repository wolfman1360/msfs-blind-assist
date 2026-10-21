using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// The TriStar's landing gear confirmed from the legs, never the lever alone (FO-13), the MD-11's
/// shape. The After Takeoff and Before Landing read-only gear checks read the gear lever (0 down,
/// 50 off, 100 up) AND each stock leg's extension in percent, the gear readouts' own simvars:
/// UP = lever above <see cref="L1011FoStateEvaluator.GearLeverUpAbove"/> AND every leg below 5 %;
/// DOWN = lever below <see cref="L1011FoStateEvaluator.GearLeverDownBelow"/> AND every leg above 95 %.
/// NaN whenever the lever or any leg is unread, so an unread field never confirms.
/// </summary>
public static class L1011GearConfirmation
{
    public const string UpField = "FO_GEAR_UP_CONFIRMED";
    public const string DownField = "FO_GEAR_DOWN_CONFIRMED";

    private const string LeftLeg = "L1011_RO_GEAR_LEFT";
    private const string NoseLeg = "L1011_RO_GEAR_NOSE";
    private const string RightLeg = "L1011_RO_GEAR_RIGHT";

    private static readonly string[] Legs = { LeftLeg, NoseLeg, RightLeg };

    /// <summary>The lever, then the left, nose and right legs.</summary>
    public static IReadOnlyList<string> Inputs { get; } = new[] { L1011Levers.GearLeverKey }.Concat(Legs).ToArray();

    private const double UpBelowPercent = 5.0;
    private const double DownAbovePercent = 95.0;

    public static double UpValue(Func<string, double> read)
    {
        double lever = read(L1011Levers.GearLeverKey);
        var legs = Legs.Select(read).ToList();
        if (double.IsNaN(lever) || legs.Any(double.IsNaN)) return double.NaN;
        return lever > L1011FoStateEvaluator.GearLeverUpAbove && legs.All(l => l < UpBelowPercent) ? 1.0 : 0.0;
    }

    public static double DownValue(Func<string, double> read)
    {
        double lever = read(L1011Levers.GearLeverKey);
        var legs = Legs.Select(read).ToList();
        if (double.IsNaN(lever) || legs.Any(double.IsNaN)) return double.NaN;
        return lever < L1011FoStateEvaluator.GearLeverDownBelow && legs.All(l => l > DownAbovePercent) ? 1.0 : 0.0;
    }
}
