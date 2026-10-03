using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>Pins the layout against the SHIPPED map: every row resolves, nothing is placed twice,
/// and every row a pilot can land on has a name and position words a screen reader can tell apart.</summary>
public class L1011PanelLayoutTests
{
    private static readonly L1011Placement Placement = L1011PanelLayout.Place(L1011ControlMap.Load(), L1011Levers.Keys);

    [Fact]
    public void Every_layout_id_is_in_the_map_or_hand_written()
    {
        Assert.Empty(Placement.MissingIds);
    }

    [Fact]
    public void No_row_points_at_a_control_the_map_cannot_replay()
    {
        Assert.Empty(Placement.UnreplayableIds);
    }

    [Fact]
    public void No_control_is_placed_twice()
    {
        var keys = Placement.RowsByPanel.Values.SelectMany(r => r).Select(r => r.Key).ToList();
        var duplicates = keys.GroupBy(k => k, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(duplicates);
    }

    [Fact]
    public void Names_are_unique_within_each_panel()
    {
        foreach (var (panel, rows) in Placement.RowsByPanel)
        {
            var duplicates = rows.GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(duplicates.Count == 0, $"{panel}: {string.Join(", ", duplicates)}");
        }
    }

    [Fact]
    public void Every_switch_row_has_distinct_non_empty_position_words()
    {
        foreach (var row in Placement.RowsByPanel.Values.SelectMany(r => r).Where(r => r.Action == L1011RowAction.Set && r.Control?.Kind is L1011Kinds.Switch or L1011Kinds.Spring))
        {
            Assert.True(row.Positions.Count >= 2, $"{row.Key} has {row.Positions.Count} positions");
            Assert.All(row.Positions.Values, w => Assert.False(string.IsNullOrWhiteSpace(w), $"{row.Key} has an empty position word"));
            Assert.True(row.Positions.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() == row.Positions.Count,
                $"{row.Key} repeats a position word: {string.Join(", ", row.Positions.Values)}");
        }
    }

    [Fact]
    public void Sections_run_in_checklist_order()
    {
        Assert.Equal(new[] { "Engineer Station", "Overhead", "Main Panel", "Center Console" }, Placement.Structure.Keys);
        Assert.Equal("Circuit Breakers", Placement.Structure["Engineer Station"][0]);
        Assert.Equal("Engineer Lights and Annunciators", Placement.Structure["Engineer Station"][1]);
        Assert.Equal("Electrical", Placement.Structure["Engineer Station"][3]);
        Assert.Equal("INS Mode Selectors", Placement.Structure["Overhead"][0]);
    }

    [Fact]
    public void Guards_sit_directly_before_their_switch()
    {
        foreach (var rows in Placement.RowsByPanel.Values)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].Key.StartsWith("GUARD_", StringComparison.Ordinal))
                    continue;
                string covered = rows[i].Key.Substring("GUARD_".Length);
                Assert.True(i + 1 < rows.Count && rows[i + 1].Key.EndsWith(covered, StringComparison.Ordinal),
                    $"{rows[i].Key} is not followed by the switch it covers");
            }
        }
    }

    [Fact]
    public void Encoders_become_increase_and_decrease_rows()
    {
        var dh = Placement.RowsByPanel["Captain Instruments"].Where(r => r.Key.StartsWith("ROTARY_CPT_DH#", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "Captain decision height increase", "Captain decision height decrease" }, dh.Select(r => r.Name));
        Assert.Equal(new[] { L1011RowAction.Increase, L1011RowAction.Decrease }, dh.Select(r => r.Action));
    }

    [Fact]
    public void Override_words_replace_only_the_listed_positions()
    {
        var compass = Placement.RowsByPanel["Compass"].Single(r => r.Key == "ROTARY_COMPASS_1_SET");
        Assert.Equal("Centre", compass.Positions[2]);
        Assert.Equal("Fast left rotation", compass.Positions[0]);
    }

    [Theory]
    [InlineData("OFF", "Off")]
    [InlineData("ALTITUDE MODE ON", "Altitude mode on")]
    [InlineData("TA/RA", "TA/RA")]
    [InlineData("APU GENERATOR", "APU generator")]
    [InlineData("STANDBY DC BUS", "Standby DC bus")]
    [InlineData("LOOP A", "Loop A")]
    [InlineData("SYSTEM B", "System B")]
    [InlineData("IGNITION SYSTEM A", "Ignition system A")]
    [InlineData("A", "A")]
    [InlineData("TANK 1A", "Tank 1A")]
    [InlineData("N1 VIBRATION", "N1 vibration")]
    [InlineData("BUS 3 (GENERATOR 1)", "Bus 3 (generator 1)")]
    public void Position_words_keep_acronyms_and_single_letters_in_capitals(string tooltip, string spoken)
    {
        Assert.Equal(spoken, L1011PanelLayout.SpokenWord(tooltip));
    }

    [Fact]
    public void Every_single_letter_in_a_placed_position_word_is_a_capital()
    {
        foreach (var row in Placement.RowsByPanel.Values.SelectMany(r => r).Where(r => r.Control?.Kind is L1011Kinds.Switch or L1011Kinds.Spring))
        {
            foreach (var word in row.Positions.Values)
            {
                var lower = word.Split(new[] { ' ', '/', '-', '(', ')' }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(t => t.Length == 1 && char.IsLetter(t[0]) && !char.IsUpper(t[0]))
                    .ToList();
                Assert.True(lower.Count == 0, $"{row.Key}: \"{word}\"");
            }
        }
    }

    [Fact]
    public void ParsePositions_reads_the_override_syntax()
    {
        var p = L1011PanelLayout.ParsePositions("0=Off; 1=Arm ;2=On");
        Assert.Equal(new[] { "Off", "Arm", "On" }, new[] { p[0], p[1], p[2] });
        Assert.Empty(L1011PanelLayout.ParsePositions(null));
    }

    [Fact]
    public void Weather_radar_buttons_are_named_by_what_they_do()
    {
        // iniBuilds' tooltips swap these two. The gauge does not: H:SWITCH_WX_OFF selects the radar's
        // OFF mode (L:WX_Mode 2) and H:SWITCH_WX_TEST its TEST mode (L1011_INSTRUMENTS.js,
        // setWeatherRadarMode), and the variable ids say the same.
        var radar = Placement.RowsByPanel["Weather Radar"];
        Assert.Equal("Radar off button", radar.Single(r => r.Key == "SWITCH_WX_OFF").Name);
        Assert.Equal("Radar test button", radar.Single(r => r.Key == "SWITCH_WX_TEST").Name);
    }

    [Fact]
    public void The_cockpit_door_closes_the_engineer_station()
    {
        Assert.Equal("Cockpit Door", Placement.Structure["Engineer Station"][^1]);
        var door = Placement.RowsByPanel["Cockpit Door"].Single();
        Assert.Equal("COCKPIT_DOOR", door.Key);
        Assert.Equal(new[] { "Closed", "Open" }, door.Positions.OrderBy(p => p.Key).Select(p => p.Value));
    }

    [Fact]
    public void Every_readout_and_lamp_names_a_panel_of_the_layout()
    {
        var panels = Placement.RowsByPanel.Keys.ToHashSet();
        Assert.All(L1011Readouts.All, r => Assert.Contains(r.Panel, panels));
        Assert.All(L1011Annunciators.All, l => Assert.Contains(l.Panel, panels));
    }

    [Fact]
    public void No_lamp_shares_a_variable_with_a_placed_switch()
    {
        var stateVars = Placement.RowsByPanel.Values.SelectMany(r => r)
            .Where(r => r.Control?.StateVar != null)
            .Select(r => r.Control!.StateVar!.Substring(2))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(L1011Annunciators.All, l => stateVars.Contains(l.Var));
    }
}
