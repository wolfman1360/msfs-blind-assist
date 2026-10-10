namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One MCDU annunciator: the cockpit lamp's variable, under its own key, and its legend.</summary>
public sealed record A300McduLight(string Key, string Var, string Label);

/// <summary>
/// Each MCDU's four annunciators, the cockpit's <c>CPT_FMS_*_LIGHT</c> and <c>FO_FMS_*_LIGHT</c> lamps (each its
/// <c>INI_FMS1/2_*_light</c> variable, on the AC light bus, [A300-23]). The MCDU window shows the lit ones in its
/// status box and speaks one coming on, as the MD-11's MCDU window does with the same four legends.
/// </summary>
public static class A300McduLights
{
    private static readonly (string Suffix, string Label)[] Legends =
    {
        ("message_light", "MSG"), ("fail_light", "FAIL"), ("display_light", "DSPY"), ("offset_light", "OFST"),
    };

    private static readonly IReadOnlyList<A300McduLight> Captain = Build(1, "CPT");
    private static readonly IReadOnlyList<A300McduLight> FirstOfficer = Build(2, "FO");

    public static IReadOnlyList<A300McduLight> For(A300McduUnit unit) =>
        unit == A300McduUnit.FirstOfficer ? FirstOfficer : Captain;

    public static IEnumerable<A300McduLight> All => Captain.Concat(FirstOfficer);

    /// <summary>"Captain MCDU: MSG, FAIL", or "connected" when none is lit.</summary>
    public static string Status(string unitName, IReadOnlyCollection<string> lit) =>
        $"{unitName}: {(lit.Count == 0 ? "connected" : string.Join(", ", lit))}";

    /// <summary>The legends that came on since <paramref name="before"/>, or null: a light going out is not news.</summary>
    public static string? ComingOn(IReadOnlyCollection<string> before, IReadOnlyCollection<string> now)
    {
        var added = now.Where(l => !before.Contains(l)).ToArray();
        return added.Length == 0 ? null : string.Join(", ", added);
    }

    private static IReadOnlyList<A300McduLight> Build(int fms, string side) =>
        Legends.Select(l => new A300McduLight($"A300_MCDU_LT_{side}_{l.Label}", $"INI_FMS{fms}_{l.Suffix}", l.Label)).ToArray();
}
