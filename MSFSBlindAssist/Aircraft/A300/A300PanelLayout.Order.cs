namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The order of the A300's panels, and the rows that open a few of them. Which panel a control belongs
/// to comes from iniBuilds' component tree and <see cref="ControlPanels"/>
/// (<see cref="A300PanelLayout.PanelFor"/>); only the order is set here.
/// </summary>
public static partial class A300PanelLayout
{
    /// <summary>
    /// Panel order per section, the fleet's Airbuses' order (owner decision, 2026-10-09): the FBW A320's
    /// overhead order where the A300 has the same panel (Electrical, IRS for ADIRS, APU, Oxygen, Fire,
    /// Hydraulics, Fuel, Air Conditioning, Bleed, Pressurization, ...), the A300's own panels beside their
    /// nearest kin, and its Instrument and Pedestal panels by system. A panel this list does not name keeps
    /// its place after the listed ones, so an iniBuilds update is never dropped.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> PanelOrder = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Overhead"] = new[]
        {
            "Electrical", "IRS", "APU", "Oxygen", "Fire", "Hydraulics", "Fuel", "Air Conditioning", "Bleed",
            "Pressurization", "Ventilation", "Anti-Ice", "Window and Probe Heat", "Wipers", "Signs", "Interior Lighting",
            "Exterior Lighting", "Flight Controls", "SAS Control", "Cockpit Door", "Cargo Smoke", "Recorder",
            "Engine Start",
        },
        ["Glareshield"] = new[] { "FCU", "EFIS Captain", "EFIS First Officer" },
        ["Instrument"] = new[]
        {
            "Warnings", "Gear", "Autobrake", "Thrust Rating Panel", "Landing Elevation", "Standby Instruments",
            "Source Switching", "Clock", "GPWS", "Captain Side", "First Officer Side",
        },
        ["Pedestal"] = new[]
        {
            "Engines", "Thrust Levers", "Flaps and Speed Brake", "Parking Brake", "Trim", "ECAM Control Panel",
            "Weather Radar", "Transponder", "VHF Radios", "Navigation Radios", "ADF Radios",
            "Audio Control Panel Captain", "Audio Control Panel First Officer", "MCDU Brightness", "IDC",
            "Pedestal Lighting",
        },
        ["Cockpit"] = new[] { "Yokes", "RAT", "Circuit Breakers", "Cockpit" },
        ["Cargo"] = new[] { "Cargo Door" },
    };

    /// <summary>
    /// Rows that open their panel, in this order; the panel's other rows follow in their own order.
    /// Electrical opens on the batteries (it opened on the indication selectors in cockpit-file order);
    /// Signs and Exterior Lighting follow the A300 checklist (the exterior lights in the order it sets
    /// them); Warnings has the captain's two buttons, then the first officer's; Gear opens on the lever;
    /// the levers' panel on the flaps.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> LeadRows = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Electrical"] = new[] { "A300_BATT_1", "A300_BATT_2", "A300_BATT_3", "A300_EXT_PWR", "A300_APU_GEN", "A300_GEN_1", "A300_GEN_2" },
        ["Signs"] = new[] { "A300_SEATBELT", "A300_NOSMOKING", "A300_EMERGEXIT_SWITCH" },
        ["Exterior Lighting"] = new[]
        {
            "A300_NOSELIGHTSWITCH", "A300_LANDINGLEFTSWITCH", "A300_LANDINGRIGHTSWITCH", "A300_WINGLIGHTSWITCH",
            "A300_STROBESWITCH", "A300_BEACONSWITCH", "A300_RWY_TOFF_L", "A300_RWY_TOFF_R", "A300_NAVLIGHT_SWITCH",
        },
        ["Warnings"] = new[] { "A300_MASTER_WARNING_CPT", "A300_MASTER_CAUTION_CPT", "A300_MASTER_WARNING_FO", "A300_MASTER_CAUTION_FO" },
        ["Gear"] = new[] { "A300_GEAR_LEVER" },
        ["Engines"] = new[] { "A300_ENG1_CUTOFF", "A300_ENG2_CUTOFF" },
        [A300Levers.Panel] = new[] { A300Levers.FlapsKey, A300Levers.SpeedBrakeKey, A300Levers.SpoilersArmKey },
    };

    /// <summary>The listed names that are present, in list order, then the rest in their own order.</summary>
    public static List<string> Ordered(IEnumerable<string> present, IReadOnlyList<string> order)
    {
        var have = present.ToList();
        var result = order.Where(have.Contains).ToList();
        result.AddRange(have.Where(p => !result.Contains(p)));
        return result;
    }

    private static void ApplyOrder(A300Placement placement)
    {
        foreach (var section in placement.Structure.Keys.ToList())
            if (PanelOrder.TryGetValue(section, out var order))
                placement.Structure[section] = Ordered(placement.Structure[section], order);

        foreach (var (panel, keys) in LeadRows)
        {
            if (!placement.RowsByPanel.TryGetValue(panel, out var rows))
                continue;
            var lead = keys.Select(k => rows.Find(r => r.Key == k)).OfType<A300PlacedRow>().ToList();
            rows.RemoveAll(lead.Contains);
            rows.InsertRange(0, lead);
        }
    }
}
