using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>Pins the SHIPPED map (the embedded resource), not a fixture: a regeneration that
/// breaks the C# contract fails here before it reaches a pilot.</summary>
public class L1011ControlMapTests
{
    private static readonly L1011ControlMap Map = L1011ControlMap.Load();

    private static readonly HashSet<string> Kinds = new()
    {
        L1011Kinds.Switch, L1011Kinds.Button, L1011Kinds.Latch, L1011Kinds.Spring,
        L1011Kinds.Knob, L1011Kinds.Encoder, L1011Kinds.None,
    };

    [Fact]
    public void The_embedded_map_loads()
    {
        Assert.True(Map.Controls.Count > 700, $"only {Map.Controls.Count} controls");
        Assert.True(Map.Breakers.Count > 900, $"only {Map.Breakers.Count} breakers");
        Assert.False(string.IsNullOrEmpty(Map.PackageVersion));
    }

    [Fact]
    public void Ids_are_unique_and_kinds_are_known()
    {
        Assert.Equal(Map.Controls.Count, Map.Controls.Select(c => c.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(Map.Controls, c => Assert.Contains(c.Kind, Kinds));
    }

    [Fact]
    public void Every_effect_in_the_map_parses()
    {
        foreach (var c in Map.Controls)
        {
            foreach (var text in c.Transitions.Values.SelectMany(t => t)
                         .Concat(c.Press).Concat(c.Release).Concat(c.Inc).Concat(c.Dec).Concat(c.SetTemplate))
                L1011Effect.Parse(text);
        }
        foreach (var b in Map.Breakers)
            foreach (var text in b.Transitions.Values.SelectMany(t => t))
                L1011Effect.Parse(text);
    }

    [Fact]
    public void Switches_have_a_transition_for_every_position()
    {
        foreach (var c in Map.Controls.Where(c => c.Kind is L1011Kinds.Switch or L1011Kinds.Spring))
        {
            Assert.True(c.Positions.Count >= 2, $"{c.Id} has {c.Positions.Count} positions");
            foreach (var (value, _) in c.OrderedPositions())
                Assert.True(c.TransitionFor(value) is { Count: > 0 }, $"{c.Id} has no transition for {value}");
        }
    }

    [Fact]
    public void The_battery_replays_the_cockpit_click()
    {
        var battery = Map.Find("toggle_battery");
        Assert.NotNull(battery);
        Assert.Equal(L1011Kinds.Switch, battery!.Kind);
        Assert.Equal(new[] { "L:TOGGLE_Battery=1", "H:ELECTRICAL", "H:ELECTRICAL_1" }, battery.TransitionFor(1));
    }

    [Fact]
    public void Parse_builds_the_index()
    {
        var map = L1011ControlMap.Parse("{\"package_version\":\"t\",\"controls\":[{\"id\":\"A\",\"kind\":\"switch\"}]}");
        Assert.Equal("t", map.PackageVersion);
        Assert.NotNull(map.Find("a"));
        Assert.Null(map.Find("b"));
    }
}
