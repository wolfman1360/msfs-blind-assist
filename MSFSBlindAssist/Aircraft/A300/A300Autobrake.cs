namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One autobrake button: the level it arms and its DECEL light's variable.</summary>
public sealed record A300AutobrakeButton(int Level, string DecelKey, string DecelVar);

/// <summary>
/// The LO, MED and MAX autobrake buttons, read from the aircraft's own code (package 1.0.11):
/// <list type="bullet">
/// <item>Each button has two lamp segments. The lower one lights while <c>INI_AUTOBRAKE_LEVEL</c> is
/// its level (1 LO, 2 MED, 3 MAX; 0 off) and its DECEL segment is dark; the DECEL segment lights from
/// <c>INI_AUTOBRAKE_LOW/MED/HI_DECEL</c> (the cockpit's emissive code), which the aircraft sets only
/// at that level, engaged, past 1.36 of deceleration (<c>update_ab_datarefs</c>). So a label reads
/// "Armed", "Decel" or "Off", as the lamp does.</item>
/// <item>A press writes <c>INI_ABRK_LOW/MED/HI_COMMAND</c>, which the aircraft consumes: the active
/// level pressed again disarms it (level 0) by design; any other level arms only with the gear handle
/// down, green hydraulics at 2,200 psi or more, no autobrake fault, and, with the parking brake off,
/// <c>INI_brake_system</c> at 0 (<c>INI_autobrake_low_handler</c> and its two siblings). Flight 1
/// measured LO refused clean with the gear up and armed with it down.</item>
/// <item><c>INI_autobrake_armed</c> is NOT the state: only the three button handlers write it, while
/// the aircraft's own disarms (the gear handle leaving down, its disconnects in <c>INI_autobrake</c>)
/// set the level to 0 and leave it at 1 ([A300-20]).</item>
/// </list>
/// Pure.
/// </summary>
public static class A300Autobrake
{
    public const string LevelKey = "A300_AUTOBRAKE_LEVEL";
    public const string LevelVar = "INI_AUTOBRAKE_LEVEL";

    /// <summary>Said when a press meant to arm leaves its level unarmed (an error, so it is spoken).</summary>
    public const string DidNotArmMessage = "Autobrake did not arm";

    /// <summary>Button row key → the level it arms and its DECEL light.</summary>
    public static readonly IReadOnlyDictionary<string, A300AutobrakeButton> ByButton =
        new Dictionary<string, A300AutobrakeButton>(StringComparer.Ordinal)
        {
            ["A300_AUTO_BRK_LO"] = new(1, "A300_AUTOBRAKE_LOW_DECEL", "INI_AUTOBRAKE_LOW_DECEL"),
            ["A300_AUTO_BRK_MID"] = new(2, "A300_AUTOBRAKE_MED_DECEL", "INI_AUTOBRAKE_MED_DECEL"),
            ["A300_AUTO_BRK_MAX"] = new(3, "A300_AUTOBRAKE_HI_DECEL", "INI_AUTOBRAKE_HI_DECEL"),
        };

    /// <summary>Every state variable the labels read: the level, then the three DECEL lights.</summary>
    public static readonly IReadOnlySet<string> StateKeys =
        ByButton.Values.Select(b => b.DecelKey).Prepend(LevelKey).ToHashSet(StringComparer.Ordinal);

    /// <summary>A button's lamp in words: "Decel", "Armed" or "Off"; null while the level is unread.</summary>
    public static string? Describe(A300AutobrakeButton button, double? level, double? decel)
    {
        if (level is not double l)
            return null;
        if ((int)Math.Round(l) != button.Level)
            return "Off";
        return decel is double d && d >= 0.5 ? "Decel" : "Armed";
    }

    /// <summary>
    /// Whether a press of <paramref name="level"/>'s button, made from <paramref name="before"/>, failed
    /// to arm it: the press was meant to arm (another level or none was active), and the level read
    /// after the settle is still not <paramref name="level"/>. Pressing the active level disarms it by
    /// design and is never judged; an unread level before or after is no evidence either way.
    /// </summary>
    public static bool DidNotArm(int level, double? before, double? after) =>
        before is double b && (int)Math.Round(b) != level
        && after is double a && (int)Math.Round(a) != level;
}
