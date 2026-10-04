namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The order of the A300's panels, and the rows that open a few of them (owner decision,
/// 2026-10-04). Which panel a control belongs to still comes from iniBuilds' component tree
/// (<see cref="A300PanelLayout.PanelFor"/>); only the order is set here.
/// </summary>
public static partial class A300PanelLayout
{
    /// <summary>
    /// Panel order per section: the order every other aircraft in the app uses, power first
    /// (Electrical, IRS, APU, Fire, Hydraulics, Fuel), then air, cabin items, and the panels a
    /// pilot rarely touches last. A panel this list does not name keeps its place after the
    /// listed ones, so an iniBuilds update is never dropped.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> PanelOrder = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Overhead"] = new[]
        {
            "Electrical", "IRS", "APU", "Fire", "Hydraulics", "Fuel", "Air Bleed and Air Conditioning",
            "Cabin Pressure", "Ventilation", "Anti-Ice", "Window and Probe Heat", "Wipers", "Lights", "Oxygen",
            "Flight Controls", "Autoflight Levers", "Engine Start", "Recorders", "Cockpit Door",
        },
        ["Glareshield"] = new[] { "FCU", "Captain EFIS", "First Officer EFIS" },
        ["Main Panel"] = new[] { "Captain Panel", "Center Panel", "First Officer Panel" },
        ["Pedestal"] = new[]
        {
            "Throttle Quadrant", "Trim", "ECAM Control", "Weather Radar", "Transponder and TCAS", "VHF Radios",
            "Navigation Radios", "ADF Radios", "Captain Audio", "First Officer Audio", "MCDU Brightness",
            "IDC", "Pedestal Lighting",
        },
        ["Cockpit"] = new[] { "Yokes", "Gear Gravity Extension", "RAT", "Circuit Breakers", "Cockpit" },
        ["Cargo"] = new[] { "Cargo Door" },
    };

    /// <summary>
    /// Rows that open their panel, in this order; the panel's other rows follow in their own order.
    /// Only the panels whose opening rows were wrong in cockpit-file order: Electrical opened on the
    /// AC and DC indication selectors and the IDG switches, Lights on the storm light, the Center
    /// Panel on the standby altimeter with the gear lever in the middle, and the levers ended the
    /// Throttle Quadrant. Lights follows the A300 checklist (signs, then the exterior lights in the
    /// order it sets them).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> LeadRows = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Electrical"] = new[] { "A300_BATT_1", "A300_BATT_2", "A300_BATT_3", "A300_EXT_PWR", "A300_APU_GEN", "A300_GEN_1", "A300_GEN_2" },
        ["Lights"] = new[]
        {
            "A300_SEATBELT", "A300_NOSMOKING", "A300_EMERGEXIT_SWITCH", "A300_NOSELIGHTSWITCH", "A300_LANDINGLEFTSWITCH",
            "A300_LANDINGRIGHTSWITCH", "A300_WINGLIGHTSWITCH", "A300_STROBESWITCH", "A300_BEACONSWITCH", "A300_RWY_TOFF_L",
            "A300_RWY_TOFF_R", "A300_NAVLIGHT_SWITCH",
        },
        ["Captain Panel"] = new[] { "A300_MASTER_WARNING_CPT", "A300_MASTER_CAUTION_CPT" },
        ["First Officer Panel"] = new[] { "A300_MASTER_WARNING_FO", "A300_MASTER_CAUTION_FO" },
        ["Center Panel"] = new[] { "A300_GEAR_LEVER" },
        [A300Levers.Panel] = new[]
        {
            "A300_ENG1_CUTOFF", "A300_ENG2_CUTOFF", A300Levers.FlapsKey, A300Levers.SpeedBrakeKey,
            A300Levers.SpoilersArmKey, "A300_PARKINGBRAKE",
        },
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
