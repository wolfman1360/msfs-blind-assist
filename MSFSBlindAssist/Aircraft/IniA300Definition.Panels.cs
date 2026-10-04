using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Aircraft;

/// <summary>
/// Panels: the structure and rows come from <see cref="A300PanelLayout"/> (the only source of panel
/// order); each panel's status display lists its lights and then its readouts.
/// </summary>
public partial class IniA300Definition
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
        {
            structure[section] = new List<string>(panels);
            if (section == A300DisplayPanels.AfterSection)
                structure[A300DisplayPanels.Section] = new List<string>(A300DisplayPanels.Panels);
        }

        // Every panel gets a controls entry, even an empty one: a panel with only a status
        // display is otherwise never built (the HS787 lesson).
        var controls = new Dictionary<string, List<string>>();
        foreach (var (panel, rows) in _placement.RowsByPanel)
            controls[panel] = rows.Select(r => r.Key).ToList();

        var displays = new Dictionary<string, List<string>>();
        foreach (var lamp in A300Announcements.Lamps)
            Add(displays, lamp.Panel, lamp.Key);
        foreach (var readout in A300Readouts.All)
            if (!A300DisplayPanels.IsDisplayPanel(readout.Panel))
                Add(displays, readout.Panel, readout.Key);
        foreach (var panel in A300DisplayPanels.Panels)
        {
            controls[panel] = new List<string>();
            displays[panel] = new List<string>(A300DisplayPanels.Lines[panel]);
        }

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

    /// <summary>An FCU button's label state, from its lamp in the cache ("Heading select: On").</summary>
    public override bool TryDescribeControlState(string varKey, out string stateText)
    {
        if (A300FcuState.ByButton.TryGetValue(varKey, out var light)
            && _sim is { } sim && Cached(sim, light.Key) is double value)
        {
            stateText = A300FcuState.Describe(light, value);
            return true;
        }
        return base.TryDescribeControlState(varKey, out stateText);
    }

    /// <summary>A readout's value as the status display shows it ("1013 hectopascals, 29.92 inches").
    /// The speed window reads as Mach while SPD/MACH is in Mach.</summary>
    public override bool TryGetDisplayOverride(string varKey, double value, out string displayText)
    {
        if (TryGetDisplayText(varKey, value) is string text)
        {
            displayText = text;
            return true;
        }
        if (varKey == A300Readouts.SpeedKey)
        {
            displayText = A300FcuState.SpeedWindow(value, IsMach());
            return true;
        }
        if (_readouts.TryGetValue(varKey, out var readout))
        {
            displayText = readout.Format(value);
            return true;
        }
        return base.TryGetDisplayOverride(varKey, value, out displayText);
    }

    /// <summary>Whether the FCU speed window is in Mach, from the cache (false when unknown).</summary>
    private bool IsMach() => _sim is { } sim && Cached(sim, A300FcuState.SpeedMachLightKey) is double v && v >= 0.5;

    /// <summary>The Displays section's lines that need more than their own value: the FMA columns,
    /// the attitude in words, and the speeds the tape shows only at some flap settings.</summary>
    private string? TryGetDisplayText(string key, double value)
    {
        switch (key)
        {
            case A300FmaSources.ThrustModeKey:
            case A300FmaSources.PitchModeKey:
            case A300FmaSources.RollModeKey:
            case A300FmaSources.ArmedKey:
                return FmaLine(key);
            case "PLANE_PITCH_DEGREES":
                return A300DisplayText.Pitch(value);
            case "PLANE_BANK_DEGREES":
                return A300DisplayText.Bank(value);
            case "INDICATED_ALTITUDE":
                return A300DisplayText.Feet(value);
            case "GROUND_VELOCITY":
                return A300DisplayText.Knots(value);
        }
        if (A300Readouts.FlapSpeeds.TryGetValue(key, out var speed))
            return A300DisplayText.FlapSpeed(speed, value, _sim is { } sim ? Cached(sim, A300Levers.FlapsKey) : null);
        return null;
    }

    /// <summary>One FMA line, read fresh from the cache. A blank column reads "blank"; the whole FMA
    /// reads "not shown" while the PFD draws no guidance columns. The armed line carries the
    /// autopilot column beneath it.</summary>
    private string FmaLine(string key)
    {
        var fma = A300Fma.Read(A300FmaSources.Compose(k => _sim is { } sim ? Cached(sim, k) : null));
        string Column(string? words) => !fma.IsShown ? "not shown" : words ?? "blank";
        return key switch
        {
            A300FmaSources.ThrustModeKey => Column(fma.Thrust),
            A300FmaSources.PitchModeKey => Column(fma.Common ?? fma.Pitch),
            A300FmaSources.RollModeKey => Column(fma.Common ?? fma.Roll),
            _ => $"{(!fma.IsShown ? "not shown" : fma.Armed.Count == 0 ? "none" : string.Join(", ", fma.Armed))}\nAutopilot: {fma.Autopilot ?? "off"}",
        };
    }
}
