using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>What each autoflight window carries, against the shipped map and layout, and the status
/// list's lines.</summary>
public class L1011AutoflightWindowsTests
{
    private static readonly Dictionary<string, L1011PlacedRow> Rows = L1011PanelLayout
        .Place(L1011ControlMap.Load(), L1011Levers.Keys).RowsByPanel.Values
        .SelectMany(rows => rows).ToDictionary(r => r.Key, StringComparer.Ordinal);

    private static IEnumerable<(string Window, IReadOnlyList<L1011WindowButton> Buttons)> Windows() => new[]
    {
        ("Speed", L1011AutoflightWindows.Speed),
        ("Heading", L1011AutoflightWindows.Heading),
        ("Altitude", L1011AutoflightWindows.Altitude),
        ("Vertical speed", L1011AutoflightWindows.VerticalSpeed),
        ("Autopilot", L1011AutoflightWindows.AutopilotButtons),
    };

    [Fact]
    public void Every_window_control_is_a_placed_panel_row()
    {
        foreach (var (_, buttons) in Windows())
            Assert.All(buttons, b => Assert.True(Rows.ContainsKey(b.RowKey), b.RowKey));
        Assert.All(L1011AutoflightWindows.AutopilotSelectors, s => Assert.True(Rows.ContainsKey(s.RowKey), s.RowKey));
    }

    [Fact]
    public void Each_control_sits_in_one_window_only()
    {
        var keys = Windows().SelectMany(w => w.Buttons.Select(b => b.RowKey))
            .Concat(L1011AutoflightWindows.AutopilotSelectors.Select(s => s.RowKey)).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void A_switch_button_is_a_two_position_row_and_a_selector_is_an_engage_paddle()
    {
        foreach (var (_, buttons) in Windows())
            foreach (var b in buttons.Where(b => b.State == L1011ButtonState.Switch))
                Assert.Equal(2, Rows[b.RowKey].Positions.Count);
        Assert.All(L1011AutoflightWindows.AutopilotSelectors, s =>
        {
            Assert.True(L1011AutoflightWindows.IsEngagePaddle(s.RowKey));
            Assert.Equal(new[] { "Command", "CWS", "Off" }, Rows[s.RowKey].Positions.OrderBy(p => p.Key).Select(p => p.Value));
        });
    }

    [Fact]
    public void Every_alt_key_letter_is_in_its_label_and_unique_in_its_window()
    {
        foreach (var (window, buttons) in Windows())
        {
            var letters = buttons.Select(b => char.ToUpperInvariant(b.Mnemonic)).ToList();
            if (window == "Autopilot")
                letters.AddRange(L1011AutoflightWindows.AutopilotSelectors.Select(s => char.ToUpperInvariant(s.Mnemonic)));
            Assert.Equal(letters.Count, letters.Distinct().Count());
            Assert.All(buttons, b => Assert.Contains(char.ToUpperInvariant(b.Mnemonic), Rows[b.RowKey].Name.ToUpperInvariant()));
        }
        Assert.All(L1011AutoflightWindows.AutopilotSelectors, s => Assert.Contains(s.Mnemonic, s.Label));
    }

    [Theory]
    [InlineData("SWITCH_AFCS_AP_A", 2, 0)]   // Off -> Command
    [InlineData("SWITCH_AFCS_AP_A", 0, 2)]   // Command -> Off
    [InlineData("SWITCH_AFCS_AP_B", 1, 2)]   // CWS -> Off
    [InlineData("SWITCH_AFCS_LOC", 0, 1)]
    [InlineData("SWITCH_AFCS_AT", 1, 0)]
    public void A_toggle_key_sends_the_control_to_its_other_position(string key, double now, double target)
    {
        Assert.Equal(target, L1011AutoflightWindows.ToggleTarget(key, now));
    }

    [Theory]
    [InlineData(null, "Autopilot Speed", false)]
    [InlineData("Autopilot Speed", "Autopilot Speed", false)]
    [InlineData("Autopilot Speed", "Autopilot Heading", true)]
    [InlineData("Altimeter Setting", "Autopilot Heading", true)]
    public void A_different_value_box_replaces_the_open_one(string? open, string requested, bool replaces)
    {
        Assert.Equal(replaces, L1011AutoflightWindows.ReplacesOpenBox(open, requested));
    }

    [Fact]
    public void The_status_list_reads_the_windows_then_the_modes_and_engagement()
    {
        var rows = new[] { "power|on", "1|250", "2|+X1500", "3|090", "4|247", "5|067", "6|X5000", "7|IAS", "8|VS" };
        var values = new Dictionary<string, double>
        {
            ["SWITCH_AFCS_HDG"] = 1, ["SWITCH_AFCS_VS"] = 1, ["ANN_LOC_ARM"] = 1, ["ANN_ALT_ARM"] = 1,
            ["SWITCH_AFCS_AT"] = 1, ["SWITCH_AFCS_TM"] = 0, ["SWITCH_AFCS_AP_A"] = 0, ["SWITCH_AFCS_AP_B"] = 2,
        };
        Assert.Equal(new[]
        {
            "Speed: IAS 250",
            "Pitch: VS +1500",
            "Heading: 090",
            "Course 1: 247",
            "Course 2: 067",
            "Altitude: 5000",
            "Engaged modes: Vertical speed, Heading",
            "Armed and captured: Localizer armed, Altitude armed",
            "Autothrottle on",
            "Thrust management off",
            "Autopilot A command",
            "Autopilot B off",
        }, L1011AutopilotStatus.Lines(rows, k => values.TryGetValue(k, out var v) ? v : null));
    }

    [Fact]
    public void Before_the_reader_answers_and_with_nothing_known_the_list_still_says_so()
    {
        Assert.Equal(new[]
        {
            L1011AutopilotStatus.NotReadYet,
            "Engaged modes: none",
            "Armed and captured: none",
            "Autothrottle unknown",
            "Thrust management unknown",
            "Autopilot A unknown",
            "Autopilot B unknown",
        }, L1011AutopilotStatus.Lines(null, _ => null));
    }

    [Fact]
    public void An_armed_flare_is_not_read_as_flaring()
    {
        // FLARE_ACTIVE reads 99 while armed above 120 ft and 1 once the flare runs (L1011_INS.js).
        var lines = L1011AutopilotStatus.Lines(null, k => k == "FLARE_ACTIVE" ? 99 : null);
        Assert.Contains("Armed and captured: none", lines);
    }
}
