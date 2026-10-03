using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011AnnunciatorsTests
{
    [Fact]
    public void Every_lamp_is_a_lamp_the_cockpit_model_lights()
    {
        var lamps = new HashSet<string>(L1011ControlMap.Load().Lamps, StringComparer.OrdinalIgnoreCase);
        Assert.All(L1011Annunciators.All, l => Assert.Contains(l.Var, lamps));
    }

    [Fact]
    public void Names_and_variables_are_unique()
    {
        Assert.Equal(L1011Annunciators.All.Count, L1011Annunciators.All.Select(l => l.Var).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(L1011Annunciators.All.Count, L1011Annunciators.All.Select(l => l.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void The_captain_decision_height_light_is_announced()
    {
        // The DH light on the captain's attitude indicator (manual: "ADI ... with a DH light").
        var dh = L1011Annunciators.All.Single(l => l.Var == "LX_CPT_DH_LIGHT");
        Assert.Equal("Decision height light", dh.Name);
        Assert.Equal("Captain Instruments", dh.Panel);
    }

    [Fact]
    public void Announcement_wording()
    {
        var fire = L1011Annunciators.All.Single(l => l.Var == "ENG_FIRE_1");
        Assert.Equal("Engine 1 fire light on", L1011Annunciators.Announcement(fire, true));
        Assert.Equal("Engine 1 fire light off", L1011Annunciators.Announcement(fire, false));
    }
}
