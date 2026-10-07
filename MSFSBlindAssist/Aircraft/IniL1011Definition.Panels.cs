using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panels: the structure and rows come from <see cref="L1011PanelLayout"/> (the only source of panel
/// order), each panel's status display lists its lamps and then its readouts.
/// </summary>
public partial class IniL1011Definition
{
    private Dictionary<string, List<string>>? _panelStructure;
    private Dictionary<string, List<string>>? _panelControls;
    private Dictionary<string, List<string>>? _panelDisplays;

    public override Dictionary<string, List<string>> GetPanelStructure()
    {
        BuildPanelsOnce();
        return _panelStructure!;
    }

    protected override Dictionary<string, List<string>> BuildPanelControls()
    {
        BuildPanelsOnce();
        return _panelControls!;
    }

    public override Dictionary<string, List<string>> GetPanelDisplayVariables()
    {
        BuildPanelsOnce();
        return _panelDisplays!;
    }

    private void BuildPanelsOnce()
    {
        if (_panelStructure != null)
            return;

        var structure = new Dictionary<string, List<string>>();
        foreach (var (section, panels) in _placement.Structure)
            structure[section] = new List<string>(panels);

        // Every panel gets a controls entry, even an empty one: a panel with only a status
        // display (Engine Instruments) is otherwise never built (the HS787 lesson).
        var controls = new Dictionary<string, List<string>>();
        foreach (var (panel, rows) in _placement.RowsByPanel)
            controls[panel] = rows.Select(r => r.Key).ToList();

        var displays = new Dictionary<string, List<string>>();
        foreach (var (key, lamp) in _lamps)
            Add(displays, lamp.Panel, key);
        Add(displays, "Autopilot", PitchModeKey);
        foreach (var flag in L1011AfcsModes.Flags)
            Add(displays, "Autopilot", flag.Key);
        foreach (var readout in L1011Readouts.All)
            Add(displays, readout.Panel, readout.Key);

        _panelStructure = structure;
        _panelControls = controls;
        _panelDisplays = displays;

        static void Add(Dictionary<string, List<string>> map, string panel, string key)
        {
            if (!map.TryGetValue(panel, out var list))
                map[panel] = list = new List<string>();
            list.Add(key);
        }
    }

    /// <summary>A readout's value as the status display shows it ("12,400 pounds"); lamps use
    /// their on/off descriptions.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (_readouts.TryGetValue(varKey, out var readout))
        {
            displayText = L1011Readouts.FormatValue(readout, value);
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }
}
