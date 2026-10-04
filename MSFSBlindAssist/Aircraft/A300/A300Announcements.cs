namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One warning light the A300 speaks.</summary>
public sealed record A300Lamp(string Key, string Var, string Name, string Panel);

/// <summary>
/// What the A300 says on its own in part 1: the two master lights when they come on, and the
/// levers when something other than the pilot's own pick moves them (a hardware lever, the cockpit
/// itself). Each phrase is composed here; <see cref="A300AnnouncementTracker"/> decides when.
///
/// The master lights speak only as they COME ON. Pressing the light writes 0 to the very variable
/// read here (the cockpit's own click code), so speaking the light going out would announce the
/// pilot's own button press back to them.
/// </summary>
public static class A300Announcements
{
    public const string MasterWarningKey = "A300_LAMP_MASTER_WARNING";
    public const string MasterCautionKey = "A300_LAMP_MASTER_CAUTION";

    /// <summary>The map row keys of the two levers the generated map drives.</summary>
    public const string GearLeverKey = "A300_GEAR_LEVER";
    public const string ParkingBrakeKey = "A300_PARKINGBRAKE";

    public static readonly IReadOnlyList<A300Lamp> Lamps = new[]
    {
        new A300Lamp(MasterWarningKey, "INI_MASTER_WARNING_ACTIVE", "Master warning", "Captain Panel"),
        new A300Lamp(MasterCautionKey, "INI_MASTER_CAUTION_ACTIVE", "Master caution", "Captain Panel"),
    };

    /// <summary>Every key that speaks: the lamps and the four levers.</summary>
    public static readonly IReadOnlySet<string> AnnouncedKeys = new HashSet<string>(StringComparer.Ordinal)
    {
        MasterWarningKey, MasterCautionKey,
        A300Levers.FlapsKey, A300Levers.SpoilersArmKey, GearLeverKey, ParkingBrakeKey,
    };

    /// <summary>The phrase a key's value speaks, or null when that value says nothing (a light out,
    /// a flap handle between detents).</summary>
    public static string? Phrase(string key, double value) => key switch
    {
        MasterWarningKey => value >= 0.5 ? "Master warning" : null,
        MasterCautionKey => value >= 0.5 ? "Master caution" : null,
        A300Levers.FlapsKey => A300Levers.FlapPositions.TryGetValue(Math.Round(value), out var detent)
            && Math.Abs(value - Math.Round(value)) < 0.01
                ? $"Flaps {detent.ToLowerInvariant()}"
                : null,
        A300Levers.SpoilersArmKey => value >= 0.5 ? "Ground spoilers armed" : "Ground spoilers disarmed",
        GearLeverKey => value >= 0.5 ? "Gear lever down" : "Gear lever up",
        ParkingBrakeKey => value >= 0.5 ? "Parking brake set" : "Parking brake released",
        _ => null,
    };
}

/// <summary>
/// Baseline-first: the first value of each key after a reset is recorded silently, and a later value
/// speaks when its phrase differs from the last one recorded. A value with no phrase (a light going
/// out) is recorded too, so the light coming on again speaks again. Pure.
/// </summary>
public sealed class A300AnnouncementTracker
{
    private readonly Dictionary<string, string?> _last = new(StringComparer.Ordinal);

    /// <summary>The phrase to speak for this delivery, or null.</summary>
    public string? Observe(string key, double value)
    {
        string? phrase = A300Announcements.Phrase(key, value);
        bool known = _last.TryGetValue(key, out var last);
        _last[key] = phrase;
        if (!known || phrase == null || phrase == last)
            return null;
        return phrase;
    }

    /// <summary>Records a baseline only where none exists yet; true when one was added.</summary>
    public bool Seed(string key, double value) => _last.TryAdd(key, A300Announcements.Phrase(key, value));

    public bool HasBaseline(string key) => _last.ContainsKey(key);

    public void Reset() => _last.Clear();
}
