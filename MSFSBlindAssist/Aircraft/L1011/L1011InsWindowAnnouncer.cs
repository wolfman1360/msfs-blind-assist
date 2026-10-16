namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// What the INS window says on its own while it is open. For a PMS, the new page title when the page
/// changes, so a FWD or BACK press (or the aircraft's own PERF MGT press on a phase change) is heard
/// without hunting for the first line. For an INS, "Leg 1 to 2, KENNO" when the FROM/TO window moves on:
/// the unit sequencing to its next waypoint, or a leg the pilot has just inserted, with the TO waypoint's
/// name when it is an imported fix. A leg being typed, the
/// TEST pattern "88", a dark unit and a page being redrawn are not changes. The first read of a unit is
/// a baseline, and so is every switch to another unit. Pure.
/// </summary>
public sealed class L1011InsWindowAnnouncer
{
    private L1011InsDevice? _device;
    private string? _last;

    public string? Observe(L1011InsDevice device, L1011InsScrapeResult scrape)
    {
        string? now = Shown(device, scrape);
        if (_device != device)
        {
            _device = device;
            _last = now;
            return null;
        }
        if (now == null || now == _last)
            return null;
        bool baseline = _last == null;
        _last = now;
        // The TO waypoint's name is added to the words, never to what is compared, so a batch reload that
        // renames the slot is not a leg change.
        return baseline ? null : now + (L1011InsDevices.IsPms(device) ? string.Empty : L1011InsDevices.ToWaypointName(scrape));
    }

    public void Reset()
    {
        _device = null;
        _last = null;
    }

    /// <summary>The words this unit would say now, or null when it shows nothing to compare.</summary>
    private static string? Shown(L1011InsDevice device, L1011InsScrapeResult scrape)
    {
        if (!scrape.Ins.Powered)
            return null;
        if (L1011InsDevices.IsPms(device))
        {
            string title = scrape.Pms == null ? string.Empty : L1011PmsDisplay.Clean(scrape.Pms.Title);
            return title.Length == 0 ? null : title;
        }
        string fromTo = new string(scrape.Ins.FromTo.Where(char.IsAsciiDigit).ToArray());
        if (scrape.Ins.Entry == "leg" || fromTo.Length != 2 || fromTo == "88")
            return null;
        return $"Leg {fromTo[0]} to {fromTo[1]}";
    }
}
