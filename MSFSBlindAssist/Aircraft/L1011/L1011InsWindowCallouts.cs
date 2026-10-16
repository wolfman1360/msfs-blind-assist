namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The INS window's own call-outs that have a Ctrl+M row: "Leg 0 to 1" when an INS moves to its next leg, a
/// PMS's new page title, and "Entry closed", the entry read-back's closing word (what was typed is always read
/// back; only the closing word can be muted). None is a variable, so the rows are synthetic, as the A380's
/// ECAM fold row is (<see cref="Services.MonitorRowBuilder.BuildWithExtra"/>): each key goes into
/// <c>UserSettings.L1011DisabledMonitorVariables</c> when its box is unticked, and the window asks here
/// before it speaks. All are on by default (a key is muted only when it is in the list). Nothing here is
/// registered with SimConnect: no batch slot and no on-request definition. Pure.
/// </summary>
public static class L1011InsWindowCallouts
{
    public const string LegKey = "L1011_INS_WINDOW_LEG";
    public const string PmsPageKey = "L1011_INS_WINDOW_PMS_PAGE";
    public const string EntryClosedKey = "L1011_INS_WINDOW_ENTRY_CLOSED";

    /// <summary>What an emptied entry is announced as; the window gives it to the scratchpad announcer and
    /// <see cref="Entry"/> recognises it.</summary>
    public const string EntryClosedWords = "Entry closed";

    /// <summary>The Ctrl+M rows, key and label.</summary>
    public static IReadOnlyList<(string Key, string Label)> Rows { get; } = new[]
    {
        (LegKey, "INS window leg changes"),
        (PmsPageKey, "INS window PMS page changes"),
        (EntryClosedKey, "INS window entry closed"),
    };

    /// <summary>A new leg (an INS) or a new page title (a PMS), or null when its row is muted.</summary>
    public static string? Change(bool pms, string words, ISet<string> muted) =>
        muted.Contains(pms ? PmsPageKey : LegKey) ? null : words;

    /// <summary>An entry read-back: what was typed is always spoken; "Entry closed" only while its row is on.</summary>
    public static string? Entry(string say, ISet<string> muted) =>
        say == EntryClosedWords && muted.Contains(EntryClosedKey) ? null : say;
}
