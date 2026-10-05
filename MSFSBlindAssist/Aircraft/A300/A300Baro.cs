using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One side's altimeter: its STD flag, the setting the aircraft saves when its knob is pulled,
/// and the knob's push and pull panel rows.</summary>
public sealed record A300BaroSide(string Name, int Index, string ModeKey, string ModeVar, string SavedKey,
    string SavedVar, string PushKey, string PullKey);

/// <summary>What Ctrl+B's STD or QNH button does: press <paramref name="Pressed"/> sides' knobs, then
/// (once each reads the new mode) send <paramref name="Rpn"/>; or say <paramref name="Refusal"/>.</summary>
public sealed record A300BaroPlan(IReadOnlyList<A300BaroSide> Pressed, string? Rpn, string? Refusal,
    IReadOnlyList<string> Warnings)
{
    public static A300BaroPlan Refused(string refusal) =>
        new(Array.Empty<A300BaroSide>(), null, refusal, Array.Empty<string>());
}

/// <summary>
/// Ctrl+B's STD and QNH buttons. Measured live (2026-10-05, gate, external power, package 1.0.11):
/// <list type="bullet">
/// <item>Pulling the altimeter knob is STD and pushing is QNH, as iniBuilds' tooltips say; ignore the
/// variable names, since the push writes one called "…STD_COMMAND".</item>
/// <item><c>L:XMLVAR_Baro1_Mode</c> / <c>Baro2_Mode</c> read 1 in STD and 0 in QNH; the standby has no
/// push or pull.</item>
/// <item>A pull saves the setting × 16 in <c>L:INI_BARO1/2_PRESSURE</c>.</item>
/// <item>STD is a FLAG ONLY: the setting does not change and the altimeter keeps reading against it
/// (500 feet at 1024.94 in STD against a pressure altitude of 197), and a push does not restore the
/// saved value.</item>
/// </list>
/// So STD here also sets all three altimeters to 1013.25, and QNH puts each side's saved setting
/// back ([A300-16]). Pure.
/// </summary>
public static class A300Baro
{
    public static readonly A300BaroSide Captain = new("Captain", 1, "A300_BARO1_MODE", "XMLVAR_Baro1_Mode",
        "A300_BARO1_SAVED", "INI_BARO1_PRESSURE", "A300_CPT_ALTIMETER_KNOB_PUSH", "A300_CPT_ALTIMETER_KNOB_PULL");

    public static readonly A300BaroSide FirstOfficer = new("First officer", 2, "A300_BARO2_MODE", "XMLVAR_Baro2_Mode",
        "A300_BARO2_SAVED", "INI_BARO2_PRESSURE", "A300_FO_ALTIMETER_KNOB_PUSH", "A300_FO_ALTIMETER_KNOB_PULL");

    public static readonly IReadOnlyList<A300BaroSide> Sides = new[] { Captain, FirstOfficer };

    public static readonly IReadOnlySet<string> ModeKeys = Sides.Select(s => s.ModeKey).ToHashSet(StringComparer.Ordinal);

    public const double StandardMillibars = 1013.25;

    /// <summary>How long after a knob press its mode is read back: the aircraft takes the press on its
    /// next update. A judgement; the read-back is what confirms it.</summary>
    public const int KnobSettleMs = 300;

    public const string UnknownModeRefusal = "Altimeters: mode unknown, try again in a moment";
    public const string AlreadyQnh = "Altimeters already QNH";

    public static bool IsStd(double mode) => mode >= 0.5;

    /// <summary>"Captain QNH, first officer STD"; "unknown" for a side not read yet.</summary>
    public static string Describe(double? captainMode, double? firstOfficerMode) =>
        $"Captain {Word(captainMode)}, first officer {Word(firstOfficerMode)}";

    private static string Word(double? mode) => mode is double m ? (IsStd(m) ? "STD" : "QNH") : "unknown";

    /// <summary>STD: pull the sides in QNH (pulling a side already in STD would save 1013 over its QNH),
    /// then set all three altimeters to 1013.25.</summary>
    public static A300BaroPlan Standard(double? captainMode, double? firstOfficerMode)
    {
        if (captainMode is not double c || firstOfficerMode is not double f)
            return A300BaroPlan.Refused(UnknownModeRefusal);
        var pull = new List<A300BaroSide>();
        if (!IsStd(c)) pull.Add(Captain);
        if (!IsStd(f)) pull.Add(FirstOfficer);
        return new A300BaroPlan(pull, A300TypedValues.AllAltimeters(StandardMillibars).Rpn, null, Array.Empty<string>());
    }

    /// <summary>QNH: push the sides in STD and put back the setting each saved when it was pulled; the
    /// standby takes the captain's (else the first officer's). A saved setting outside 955 to 1060
    /// hectopascals is not used: that side is pushed but keeps its setting, and a warning says so. A
    /// side already in QNH is left alone, since what it saved may be older than what it shows.</summary>
    public static A300BaroPlan Qnh(double? captainMode, double? firstOfficerMode, double? captainSaved, double? firstOfficerSaved)
    {
        if (captainMode is not double c || firstOfficerMode is not double f)
            return A300BaroPlan.Refused(UnknownModeRefusal);
        var push = new List<A300BaroSide>();
        var writes = new List<string>();
        var warnings = new List<string>();
        double? standby = null;
        foreach (var (side, mode, saved) in new[] { (Captain, c, captainSaved), (FirstOfficer, f, firstOfficerSaved) })
        {
            if (!IsStd(mode))
                continue;
            push.Add(side);
            if (SavedMillibars(saved) is double mb)
            {
                writes.Add(A300TypedValues.Altimeter(mb, new[] { side.Index }, side.Name).Rpn!);
                standby ??= mb;
            }
            else
                warnings.Add($"{side.Name} saved setting unavailable");
        }
        if (push.Count == 0)
            return A300BaroPlan.Refused(AlreadyQnh);
        if (standby is double s)
            writes.Add(A300TypedValues.Altimeter(s, new[] { 3 }, "Standby").Rpn!);
        return new A300BaroPlan(push, writes.Count == 0 ? null : string.Join(" ", writes), null, warnings);
    }

    /// <summary>A saved setting (millibars × 16, as the aircraft stores it) in millibars, or null when
    /// it is missing or outside 955 to 1060 hectopascals.</summary>
    public static double? SavedMillibars(double? saved16) =>
        saved16 is double v && v / 16 is var mb && mb >= 955 && mb <= 1060 ? mb : null;

    /// <summary>The read-back: one setting when all three agree to the hectopascal ("Altimeters standard,
    /// 1013 hectopascals, 29.92 inches"), else each in whole hectopascals.</summary>
    public static string Confirmation(string lead, double captain, double firstOfficer, double standby)
    {
        long c = (long)Math.Round(captain), f = (long)Math.Round(firstOfficer), s = (long)Math.Round(standby);
        if (c == f && f == s)
            return $"{lead}, {A300Readouts.Altimeter(captain)}";
        var inv = CultureInfo.InvariantCulture;
        return $"{lead}: captain {c.ToString(inv)}, first officer {f.ToString(inv)}, standby {s.ToString(inv)} hectopascals";
    }
}
