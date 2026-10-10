using System.Text.RegularExpressions;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>A cockpit light read through its own rule: the lamp node it is, and what a pilot calls it.</summary>
/// <param name="Node">The lamp's node in the generated map (<see cref="A300ControlMap.Lamps"/>).</param>
/// <param name="Name">Its button plus its legend plus "light" ([VAR-1]).</param>
public sealed record A300PanelLamp(string Node, string Name, string Panel)
{
    /// <summary>Its MSFSBA variable key, which also carries its Ctrl+M row.</summary>
    public string Key => "A300_LT_" + Node;
}

/// <summary>A named lamp with its generated rule: the variable its own subscription reads (the rule's
/// first input), the others' shared keys, and its bus's light power.</summary>
public sealed class A300ResolvedLamp
{
    public A300ResolvedLamp(A300PanelLamp lamp, A300LampRule rule, A300LightPower power)
    {
        Lamp = lamp;
        Rule = rule;
        Power = power;
    }

    public A300PanelLamp Lamp { get; }
    public A300LampRule Rule { get; }
    public A300LightPower Power { get; }

    /// <summary>The variable the lamp's own subscription reads, delivered under its own key.</summary>
    public A300LampInput Primary => Rule.Inputs[0];

    /// <summary>The key an input of this lamp arrives under.</summary>
    public string KeyFor(A300LampInput input) => input.Id == Primary.Id ? Lamp.Key : A300PanelLamps.InputKey(input);
}

/// <summary>
/// The cockpit lights read through their own rule, generated from the aircraft's emissive code
/// (<see cref="A300LampRule"/>, [A300-25]), named here panel by panel as each is swept (owner decision,
/// 2026-10-09: every light that can be read is spoken). A switch's OFF or ON legend that only mirrors its
/// position is left out: the row already says the position. The older fault lights stay in
/// <see cref="A300FaultLights"/>; a lamp named here never repeats one.
/// </summary>
public static class A300PanelLamps
{
    public static readonly IReadOnlyList<A300PanelLamp> All = new A300PanelLamp[]
    {
        // Electrical: the external power and override supply buttons, the IDG buttons, and the bus lights.
        new("EXT_PWR_SEQ1_LIGHT", "External power available light", "Electrical"),
        new("IDG_1_SEQ1_LIGHT", "IDG 1 disconnected light", "Electrical"),
        new("IDG_2_SEQ1_LIGHT", "IDG 2 disconnected light", "Electrical"),
        new("OVR_SUP_1_SEQ2_LIGHT", "Override supply 1 flow light", "Electrical"),
        new("OVR_SUP_2_SEQ2_LIGHT", "Override supply 2 flow light", "Electrical"),
        new("AC_BUS_1_OFF_LIGHT", "AC bus 1 off light", "Electrical"),
        new("AC_BUS_2_OFF_LIGHT", "AC bus 2 off light", "Electrical"),
        new("AC_ESS_BUS_OFF_LIGHT", "AC essential bus off light", "Electrical"),
        new("AC_EMER_ON_INV_LIGHT", "AC emergency bus on inverter light", "Electrical"),
        new("DC_NORM_BUS_LIGHT", "DC normal bus off light", "Electrical"),
        new("DC_ESS_ONBAT_LIGHT", "DC essential bus on battery light", "Electrical"),

        // IRS: each mode panel's four lights. iniBuilds' second and third panel nodes read IRS 3 and IRS 2,
        // in another order, so each is named from the IRS its rule reads.
        new("IRS_1_009_LIGHT", "IRS 1 align light", "IRS"),
        new("IRS_1_010_LIGHT", "IRS 1 on battery light", "IRS"),
        new("IRS_1_011_LIGHT", "IRS 1 battery fault light", "IRS"),
        new("IRS_1_012_LIGHT", "IRS 1 fault light", "IRS"),
        new("IRS_3_012_LIGHT", "IRS 2 align light", "IRS"),
        new("IRS_3_011_LIGHT", "IRS 2 on battery light", "IRS"),
        new("IRS_3_010_LIGHT", "IRS 2 battery fault light", "IRS"),
        new("IRS_3_009_LIGHT", "IRS 2 fault light", "IRS"),
        new("IRS_2_009_LIGHT", "IRS 3 align light", "IRS"),
        new("IRS_2_010_LIGHT", "IRS 3 on battery light", "IRS"),
        new("IRS_2_011_LIGHT", "IRS 3 battery fault light", "IRS"),
        new("IRS_2_012_LIGHT", "IRS 3 fault light", "IRS"),

        // APU: the available light, the start button's two legends and the fuel shutoff valve indicator.
        new("APU_AVAIL_SEQ1_LIGHT", "APU available light", "APU"),
        new("APU_START_SEQ2_LIGHT", "APU start on light", "APU"),
        new("APU_START_SEQ1_LIGHT", "APU starting light", "APU"),
        new("APU_FUELSHUT_SEQ1_LIGHT", "APU fuel shutoff valve open light", "APU"),

        // Fire: each handle's light (its lamp reads INI_*_FIRE_TEST, which the fire test and the aircraft's
        // fire logic set) and each agent button's SQUIB legend. The DISCH legend is the agent row's position.
        new("FIRE_HANDLE_ENG1_LIGHT", "Engine 1 fire handle light", "Fire"),
        new("FIRE_HANDLE_ENG2_LIGHT", "Engine 2 fire handle light", "Fire"),
        new("FIRE_HANDLE_APU_LIGHT", "APU fire handle light", "Fire"),
        new("FIRE_1_AG1_SEQ1_LIGHT", "Engine 1 agent 1 squib light", "Fire"),
        new("FIRE_1_AG2_SEQ1_LIGHT", "Engine 1 agent 2 squib light", "Fire"),
        new("FIRE_2_AG1_SEQ1_LIGHT", "Engine 2 agent 1 squib light", "Fire"),
        new("FIRE_2_AG2_SEQ1_LIGHT", "Engine 2 agent 2 squib light", "Fire"),
        new("FIRE_APU_AG_SEQ1_LIGHT", "APU agent squib light", "Fire"),
    };

    /// <summary>Each named lamp with its rule from the shipped map; a lamp the map lacks is left out, loudly
    /// (an iniBuilds update renamed it: regenerate and rename here).</summary>
    public static readonly IReadOnlyList<A300ResolvedLamp> Resolved = Resolve(A300ControlMap.Load());

    /// <summary>Every input some lamp reads that is not that lamp's own subscription, once each.</summary>
    public static IReadOnlyList<A300LampInput> SharedInputs =>
        Resolved.SelectMany(l => l.Rule.Inputs.Where(i => i.Id != l.Primary.Id))
                .GroupBy(i => i.Id).Select(g => g.First()).ToList();

    /// <summary>The key a shared input arrives under: <c>A300_IN_</c> plus its id, e.g.
    /// <c>A300_IN_A_EXTERNAL_POWER_ON_1_BOOL</c>.</summary>
    public static string InputKey(A300LampInput input) =>
        "A300_IN_" + Regex.Replace(input.Id.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');

    private static IReadOnlyList<A300ResolvedLamp> Resolve(A300ControlMap map)
    {
        var byNode = map.Lamps.GroupBy(l => l.Node).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var resolved = new List<A300ResolvedLamp>();
        foreach (var lamp in All)
        {
            if (!byNode.TryGetValue(lamp.Node, out var mapLamp))
            {
                Utils.Logging.Log.Warn("A300", $"Lamp {lamp.Node} ({lamp.Name}) is not in the control map; it is not read.");
                continue;
            }
            resolved.Add(new A300ResolvedLamp(lamp, A300LampRule.Parse(mapLamp.State), mapLamp.Power));
        }
        return resolved;
    }
}
