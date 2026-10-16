using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>A unit the INS window drives.</summary>
public enum L1011InsDevice { Ins1, Ins2, Ins3, CaptainPms, FirstOfficerPms }

/// <summary>The keys a piece of typed text presses, or why it presses none.</summary>
public sealed record L1011TypedKeys(IReadOnlyList<string> ControlIds, string? Error);

/// <summary>
/// The five units behind the INS window and where each lives. INS 1 and the captain's PMS share the
/// Coherent view "L1011_INS_1", INS 2 and the first officer's PMS "L1011_INS_2", INS 3 has "L1011_INS_3"
/// to itself (its PMS copy is inert). Every key is a control in the map, pressed through the cockpit's own
/// click (<see cref="L1011WritePlan.ForPress"/>). Pure.
/// </summary>
public static class L1011InsDevices
{
    public const string InsKeysError = "INS keys are digits and N, S, E, W, L, R";
    public const string PmsKeysError = "PMS keys are digits and space. For a minus sign, type 9 first";

    public static IReadOnlyList<L1011InsDevice> All { get; } =
        new[] { L1011InsDevice.Ins1, L1011InsDevice.Ins2, L1011InsDevice.Ins3, L1011InsDevice.CaptainPms, L1011InsDevice.FirstOfficerPms };

    public static string Name(L1011InsDevice device) => device switch
    {
        L1011InsDevice.CaptainPms => "Captain PMS",
        L1011InsDevice.FirstOfficerPms => "First officer PMS",
        _ => "INS " + Unit(device)!.Value.ToString(CultureInfo.InvariantCulture),
    };

    public static string ViewNeedle(L1011InsDevice device) => device switch
    {
        L1011InsDevice.Ins1 or L1011InsDevice.CaptainPms => "L1011_INS_1",
        L1011InsDevice.Ins2 or L1011InsDevice.FirstOfficerPms => "L1011_INS_2",
        _ => "L1011_INS_3",
    };

    /// <summary>1 to 3 for an INS, null for a PMS.</summary>
    public static int? Unit(L1011InsDevice device) => device switch
    {
        L1011InsDevice.Ins1 => 1,
        L1011InsDevice.Ins2 => 2,
        L1011InsDevice.Ins3 => 3,
        _ => null,
    };

    /// <summary>"CPT" or "FO" for a PMS (the keys' own prefix), null for an INS.</summary>
    public static string? Seat(L1011InsDevice device) => device switch
    {
        L1011InsDevice.CaptainPms => "CPT",
        L1011InsDevice.FirstOfficerPms => "FO",
        _ => null,
    };

    public static bool IsPms(L1011InsDevice device) => Seat(device) != null;

    /// <summary>The panel row that opens the window on this unit.</summary>
    public static string WindowKey(L1011InsDevice device) => "L1011_INS_WINDOW_" + device.ToString().ToUpperInvariant();

    public static L1011InsDevice? FromWindowKey(string key)
    {
        foreach (var device in All)
            if (WindowKey(device) == key)
                return device;
        return null;
    }

    public static string InsertKey(L1011InsDevice device) =>
        Seat(device) is string seat ? L1011InsKeys.PmsKey(seat, "INSERT") : L1011InsKeys.Insert(Unit(device)!.Value);

    public static string ClearKey(L1011InsDevice device) =>
        Seat(device) is string seat ? L1011InsKeys.PmsKey(seat, "CLR") : L1011InsKeys.Clear(Unit(device)!.Value);

    /// <summary>
    /// The keys typed text presses, in order. Dots are left out (a fuel figure "157.3" is keyed 1573, a
    /// latitude "36 05.1" 36051) and so are spaces on an INS; on a PMS a space is its SP key. A character
    /// with no key sends nothing at all, so a typing slip never leaves half an entry in the unit.
    /// </summary>
    public static L1011TypedKeys Keys(L1011InsDevice device, string text)
    {
        var ids = new List<string>();
        string? seat = Seat(device);
        foreach (char c in text ?? string.Empty)
        {
            if (c == '.' || (seat == null && c == ' '))
                continue;
            string? id = seat != null ? L1011InsKeys.PmsControlFor(seat, c) : L1011InsKeys.ControlFor(Unit(device)!.Value, c);
            if (id == null)
                return new L1011TypedKeys(Array.Empty<string>(), seat != null ? PmsKeysError : InsKeysError);
            ids.Add(id);
        }
        return new L1011TypedKeys(ids, null);
    }

    /// <summary>
    /// The window's lines for a unit: "Reading INS 2..." before the first read, "Captain PMS: off" while
    /// its INS has no power (the PMS is drawn inside it), or the unit's own lines. Null while a PMS page is
    /// being redrawn: the window keeps the page it shows.
    /// </summary>
    public static IReadOnlyList<string>? Lines(L1011InsDevice device, L1011InsScrapeResult? scrape)
    {
        if (scrape == null)
        {
            return new[]
            {
                device switch
                {
                    L1011InsDevice.CaptainPms => "Reading the captain PMS...",
                    L1011InsDevice.FirstOfficerPms => "Reading the first officer PMS...",
                    _ => $"Reading {Name(device)}...",
                },
            };
        }
        if (!IsPms(device))
        {
            var lines = L1011InsDisplay.Lines(scrape.Ins).ToList();
            string name = ToWaypointName(scrape);
            int leg = lines.FindIndex(l => l.StartsWith("Leg: ", StringComparison.Ordinal) && l != "Leg: test pattern");
            if (leg >= 0 && name.Length > 0)
                lines[leg] += name;
            return lines;
        }
        if (!scrape.Ins.Powered)
            return new[] { Name(device) + ": off" };
        return scrape.Pms == null ? new[] { Name(device) + ": not shown" } : L1011PmsDisplay.Lines(scrape.Pms);
    }

    /// <summary>
    /// What a PMS INSERT stored, read back in place of "Entry closed": the line that was open as it now
    /// reads ("4: GW 418"), then every other line the unit recomputed from it ("2: EPR 1.642", which the
    /// takeoff page fills from the SAT). Null when no line was open, or the page changed (a page change
    /// abandons an entry, and the new page is said already).
    /// </summary>
    public static string? InsertReadback(IReadOnlyList<string> before, int activeLine, IReadOnlyList<string> after)
    {
        if (activeLine < 1 || activeLine > 6 || before.Count < 7 || after.Count < 7 || before[0] != after[0])
            return null;
        var words = new List<string> { after[activeLine] };
        for (int i = 1; i <= 6; i++)
            if (i != activeLine && before[i] != after[i] && !MovesByItself(after[i]))
                words.Add(after[i]);
        return string.Join(". ", words);
    }

    /// <summary>A PMS line the unit keeps live (the clock, the pressure altitude, the climb page's SAT and
    /// TAS), which changes whether or not anything was inserted.</summary>
    private static bool MovesByItself(string line)
    {
        int colon = line.IndexOf(": ", StringComparison.Ordinal);
        string text = colon >= 0 ? line[(colon + 2)..] : line;
        return text.StartsWith("GMT", StringComparison.Ordinal) || text.StartsWith("PR ALT", StringComparison.Ordinal)
            || text.Contains(" TAS ", StringComparison.Ordinal);
    }

    /// <summary>", TANGI": the waypoint an INS flies to, when it is an imported fix (a typed one is USER&lt;k&gt;
    /// and an empty slot has no name); "" otherwise.</summary>
    public static string ToWaypointName(L1011InsScrapeResult scrape)
    {
        string fromTo = new string(scrape.Ins.FromTo.Where(char.IsAsciiDigit).ToArray());
        int slot = fromTo.Length == 2 ? fromTo[1] - '0' : 0;
        if (slot < 1 || slot > scrape.Waypoints.Count || scrape.Waypoints[slot - 1] is not { } wp)
            return string.Empty;
        string ident = wp.Ident.Trim();
        bool empty = wp.Latitude == 0 && wp.Longitude == 0;
        return empty || ident.Length == 0 || L1011InsWaypoints.IsTyped(ident) ? string.Empty : ", " + ident;
    }

    /// <summary>
    /// The line an open entry shows ("Entry, left window: 36N", "4: GW, entering: 47"), "" when none is
    /// open, null when it cannot be judged (nothing read yet, a dark unit, a PMS page being redrawn). The
    /// window reads it back once typed keys have landed, as the fleet's CDU windows read their scratchpad
    /// (<see cref="Forms.CduScratchpadAnnouncer"/>).
    /// </summary>
    public static string? EntryLine(L1011InsDevice device, L1011InsScrapeResult? scrape)
    {
        if (scrape == null || !scrape.Ins.Powered)
            return null;
        if (!IsPms(device))
            return scrape.Ins.Entry.Length > 0 ? L1011InsDisplay.Lines(scrape.Ins)[3] : "";   // after the state, mode selector and display selector lines
        if (scrape.Pms is not L1011PmsScreen pms)
            return null;
        if (L1011PmsDisplay.Lines(pms) is not IReadOnlyList<string> lines)
            return null;
        return pms.EntryOpen && pms.ActiveLine >= 1 && pms.ActiveLine <= 6 ? lines[pms.ActiveLine] : "";
    }

    /// <summary>
    /// True for an entry line the unit will erase within about a second, so the window reads it on the first
    /// poll that sees it instead of waiting for the fleet's stable polls ([CORE-7]: a validation error always
    /// speaks): a PMS value the unit refused shows the gauge's ERROR word in the value cell (page 1), or "---"
    /// drawn inside the label cell while the entry is open (the cruise and descent pages; the climb page's
    /// empty ALT prompt has the same shape and reads at once too). An INS INSERT the unit refuses changes
    /// nothing on the display, so an INS is never read at once: its entry line simply stays.
    /// </summary>
    public static bool EntryReadsAtOnce(L1011InsDevice device, L1011InsScrapeResult? scrape)
    {
        if (!IsPms(device) || scrape == null || !scrape.Ins.Powered || scrape.Pms is not L1011PmsScreen pms)
            return false;
        if (!pms.EntryOpen || pms.ActiveLine < 1 || pms.ActiveLine > 6)
            return false;
        int i = pms.ActiveLine - 1;
        string left = i < pms.Left.Count ? pms.Left[i] ?? string.Empty : string.Empty;
        string right = i < pms.Right.Count ? pms.Right[i] ?? string.Empty : string.Empty;
        return right.Contains("ERROR", StringComparison.OrdinalIgnoreCase) || left.Contains("---", StringComparison.Ordinal);
    }
}
