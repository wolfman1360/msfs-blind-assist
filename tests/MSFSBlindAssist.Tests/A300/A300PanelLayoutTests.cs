using MSFSBlindAssist.Aircraft.A300;

namespace MSFSBlindAssist.Tests.A300;

/// <summary>
/// The shipped map and its placement. Fleet-wide rules (no two rows of a panel share a name, labels
/// fit, no batch name collisions) are pinned by the fleet tests the A300 is listed in.
/// </summary>
public class A300PanelLayoutTests
{
    private static readonly A300ControlMap Map = A300ControlMap.Load();
    private static readonly A300Placement Placement = A300PanelLayout.Place(Map);

    private static IEnumerable<A300PlacedRow> AllRows => Placement.RowsByPanel.Values.SelectMany(r => r);

    private static A300PlacedRow Row(string key) => AllRows.Single(r => r.Key == key);

    [Fact]
    public void The_embedded_map_loads_with_every_control()
    {
        Assert.Equal(614, Map.Controls.Count);
        Assert.False(string.IsNullOrEmpty(Map.PackageVersion));
    }

    [Fact]
    public void Every_drivable_control_names_its_set_event_and_a_unique_key()
    {
        foreach (var c in Map.Controls.Where(A300PanelLayout.IsPlaced))
            Assert.EndsWith("_Set", c.Event);
        Assert.Equal(Map.Controls.Count, Map.Controls.Select(c => c.Key).Distinct().Count());
    }

    [Fact]
    public void The_sections_follow_the_cockpit()
    {
        Assert.Equal(new[] { "Overhead", "Glareshield", "Main Panel", "Pedestal", "Cockpit", "Cargo" },
            Placement.Structure.Keys);
        Assert.Equal(new[] { "Cabin Pressure", "IRS", "APU", "Anti-Ice" }, Placement.Structure["Overhead"].Take(4));
        Assert.Equal(new[] { "FCU", "Captain EFIS", "First Officer EFIS" }, Placement.Structure["Glareshield"]);
    }

    [Fact]
    public void Every_panel_in_the_structure_has_rows()
    {
        foreach (var panel in Placement.Structure.Values.SelectMany(p => p))
            Assert.NotEmpty(Placement.RowsByPanel[panel]);
    }

    [Theory]
    [InlineData("A300_BATT_1", "Battery 1")]
    [InlineData("A300_GEN_2", "Engine 2 generator")]
    [InlineData("A300_NAVATTLEFT", "IRS 1 mode")]
    [InlineData("A300_FO_ALTIMETER_KNOB#INC", "Altimeter knob increase")]
    [InlineData("A300_VS_KNOB#DEC", "Vertical speed knob decrease")]
    [InlineData("A300_CPT_VHF1_VOL_BUTTON", "VHF 1 receiver")]
    [InlineData("A300_FO_INT_VOL", "Interphone volume")]
    [InlineData("A300_CPT_HF2_PUSH", "HF 2 transmit")]
    [InlineData("A300_STORMLIGHT", "Storm light")]
    [InlineData("A300_EXT_PWR", "External power")]
    public void Rows_carry_the_names_a_pilot_says(string key, string name) => Assert.Equal(name, Row(key).Name);

    [Fact]
    public void The_first_officers_altimeter_is_not_named_after_the_captains()
    {
        // iniBuilds titles both altimeter knobs "CPT BAROMETER".
        Assert.Contains(Row("A300_FO_ALTIMETER_KNOB#INC"), Placement.RowsByPanel["First Officer Panel"]);
        Assert.DoesNotContain(AllRows, r => r.Name.Contains("CPT", StringComparison.Ordinal));
    }

    [Fact]
    public void A_cargo_smoke_guard_comes_just_before_its_switch_and_reads_closed_or_open()
    {
        var fire = Placement.RowsByPanel["Fire"].Select(r => r.Key).ToList();
        Assert.Equal(fire.IndexOf("A300_CARGO_FWD_SMOKE_TOGGLE_GUARD") + 1, fire.IndexOf("A300_CARGO_FWD_SMOKE_TOGGLE"));
        Assert.Equal(fire.IndexOf("A300_CARGO_AFT_SMOKE_TOGGLE_GUARD") + 1, fire.IndexOf("A300_CARGO_AFT_SMOKE_TOGGLE"));
        Assert.Equal(new[] { "Closed", "Open" }, Row("A300_CARGO_FWD_SMOKE_TOGGLE_GUARD").Positions.Values);
    }

    [Fact]
    public void Position_words_are_spoken_forms()
    {
        Assert.Equal(new[] { "Off", "Navigate", "Attitude" }, Row("A300_NAVATTLEFT").Positions.Values);
        Assert.Equal(new[] { "System 2", "System 1", "Off" }, Row("A300_NAVLIGHT_SWITCH").Positions.Values);
        Assert.Equal(new[] { "Off", "On" }, Row("A300_BATT_1").Positions.Values);
        Assert.Equal(new[] { "Released", "Set" }, Row("A300_PARKINGBRAKE").Positions.Values);
    }

    [Fact]
    public void The_levers_end_the_throttle_quadrant()
    {
        var keys = Placement.RowsByPanel[A300Levers.Panel].Select(r => r.Key).TakeLast(3);
        Assert.Equal(new[] { A300Levers.FlapsKey, A300Levers.SpoilersArmKey, A300Levers.SpeedBrakeKey }, keys);
        Assert.Contains(A300Levers.Panel, Placement.Structure["Pedestal"]);
    }

    [Theory]
    [InlineData("AIRLINER_CPT_VHF1_IDC")]
    [InlineData("AIRLINER_TABLET_BRT_UP")]
    [InlineData("AIRLINER_SEAT_CPT_FWD")]
    [InlineData("AIRLINER_AT_DISCO2")]
    [InlineData("AIRLINER_MAIN_CARGO_DOOR_SWITCH")]
    public void Copies_and_cosmetic_parts_are_not_placed(string id)
    {
        if (Map.Find(id) is { } control)
            Assert.False(A300PanelLayout.IsPlaced(control));
        Assert.DoesNotContain(AllRows, r => r.Control?.Id == id);
    }

    [Fact]
    public void Guard_covers_are_never_rows()
    {
        Assert.DoesNotContain(AllRows, r => r.Control?.Kind == A300Kinds.Cover);
    }

    [Theory]
    [InlineData("STORM LIGHT", "Storm light")]
    [InlineData("TA/RA", "TA/RA")]
    [InlineData("ENG1 LOOP A", "ENG1 loop A")]
    [InlineData("APU MASTER", "APU master")]
    [InlineData("WINDOW HEATER WSLO&LAT L", "Window heater wslo&lat L")]
    [InlineData("", "")]
    public void Spoken_words_keep_letters_codes_and_numbers(string word, string spoken) =>
        Assert.Equal(spoken, A300PanelLayout.SpokenWord(word));

    [Theory]
    [InlineData("AIRLINER_CPT_VOR2_VOL_BUTTON", "VOR 2 receiver")]
    [InlineData("AIRLINER_FO_MKR_VOL", "Marker volume")]
    [InlineData("AIRLINER_CPT_PA_PUSH", "PA transmit")]
    [InlineData("AIRLINER_CPT_VHF1_MHZ", null)]
    public void The_audio_panels_follow_one_rule(string id, string? name) =>
        Assert.Equal(name, A300PanelLayout.AudioName(id));
}
