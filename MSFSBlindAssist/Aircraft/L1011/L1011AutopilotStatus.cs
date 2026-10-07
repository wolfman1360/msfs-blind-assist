namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The Ctrl+P window's status list: the glareshield's six windows exactly as the cockpit draws them
/// (<see cref="L1011AfcsWindows"/>, from the reader that runs while the window is shown), then the
/// engaged modes, the armed and captured modes, the autothrottle, thrust management and both
/// autopilots, in the words the announcements use (<see cref="L1011AfcsModes"/>). Pure.
/// </summary>
public static class L1011AutopilotStatus
{
    public const string NotReadYet = "Autopilot windows not read yet";

    /// <summary>The engagement lines after the modes, in this order.</summary>
    public static readonly IReadOnlyList<string> EngagementKeys = new[]
    {
        "SWITCH_AFCS_AT", "SWITCH_AFCS_TM", "SWITCH_AFCS_AP_A", "SWITCH_AFCS_AP_B",
    };

    /// <param name="windowRows">The reader's rows, or null while it has delivered none.</param>
    /// <param name="read">A control's or flag's position, or null when it is not known.</param>
    public static IReadOnlyList<string> Lines(IReadOnlyList<string>? windowRows, Func<string, double?> read)
    {
        var lines = new List<string>();
        if (windowRows == null)
            lines.Add(NotReadYet);
        else
            lines.AddRange(L1011AfcsWindows.Format(windowRows));

        lines.Add("Engaged modes: " + L1011AfcsModes.Engaged(read));
        var flags = L1011AfcsModes.Flags
            .Select(f => read(f.Key) is double v ? L1011AfcsModes.Words(f.Key, v) : null)
            .Where(w => w != null)
            .ToList();
        lines.Add("Armed and captured: " + (flags.Count == 0 ? "none" : string.Join(", ", flags)));

        foreach (var key in EngagementKeys)
        {
            var state = L1011AfcsModes.All.First(s => s.Key == key);
            lines.Add(read(key) is double v && L1011AfcsModes.Words(key, v) is string words
                ? words
                : $"{state.Name} unknown");
        }
        return lines;
    }
}
