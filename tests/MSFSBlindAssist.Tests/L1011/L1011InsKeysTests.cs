using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>Which keypad button each typed character presses, and the present-position key sequence that
/// was typed into INS 1 live at a gate (2026-10-03) and aligned it.</summary>
public class L1011InsKeysTests
{
    [Theory]
    [InlineData('0', "INS_1_KEY_0")]
    [InlineData('7', "INS_1_KEY_7")]
    [InlineData('N', "INS_1_KEY_2")]
    [InlineData('s', "INS_1_KEY_8")]
    [InlineData('E', "INS_1_KEY_6")]
    [InlineData('W', "INS_1_KEY_4")]
    [InlineData('L', "INS_1_KEY_7")]
    [InlineData('R', "INS_1_KEY_9")]
    public void Characters_map_to_the_keypad(char typed, string control)
    {
        Assert.Equal(control, L1011InsKeys.ControlFor(1, typed));
    }

    [Fact]
    public void Other_characters_press_nothing()
    {
        Assert.Null(L1011InsKeys.ControlFor(2, 'X'));
        Assert.Null(L1011InsKeys.ControlFor(2, '.'));
        Assert.Null(L1011InsKeys.ControlFor(2, ' '));
    }

    [Fact]
    public void The_function_keys_by_unit()
    {
        Assert.Equal("INS_3_KEY_INSERT", L1011InsKeys.Insert(3));
        Assert.Equal("INS_3_KEY_CLEAR", L1011InsKeys.Clear(3));
        Assert.Equal("INS_2_KEY_CHG", L1011InsKeys.TrackChange(2));
        Assert.Equal("INS_2_KEY_HOLD", L1011InsKeys.Hold(2));
    }

    [Theory]
    [InlineData(36.08540568, "N36051")]
    [InlineData(43.671003, "N43403")]
    [InlineData(-33.94611, "S33568")]
    [InlineData(36.9999, "N37000")]   // 59.994 minutes rounds to 60.0: carried into the degree
    [InlineData(0.0, "N00000")]
    public void Latitudes_as_the_keypad_takes_them(double lat, string expected)
    {
        Assert.Equal(expected, L1011InsKeys.LatitudeKeys(lat));
    }

    [Theory]
    [InlineData(-115.13363512, "W115080")]
    [InlineData(-79.614799, "W079369")]
    [InlineData(151.17722, "E151106")]
    [InlineData(-179.9999, "W180000")]
    public void Longitudes_as_the_keypad_takes_them(double lon, string expected)
    {
        Assert.Equal(expected, L1011InsKeys.LongitudeKeys(lon));
    }

    [Fact]
    public void The_present_position_sequence_typed_live_at_a_gate()
    {
        var keys = L1011InsKeys.PresentPositionControls(1, 36.08540568, -115.13363512);
        Assert.Equal(new[]
        {
            "INS_1_KEY_2", "INS_1_KEY_3", "INS_1_KEY_6", "INS_1_KEY_0", "INS_1_KEY_5", "INS_1_KEY_1", "INS_1_KEY_INSERT",
            "INS_1_KEY_4", "INS_1_KEY_1", "INS_1_KEY_1", "INS_1_KEY_5", "INS_1_KEY_0", "INS_1_KEY_8", "INS_1_KEY_0", "INS_1_KEY_INSERT",
        }, keys);
    }

    [Fact]
    public void A_comma_decimal_culture_changes_no_key()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("N36051", L1011InsKeys.LatitudeKeys(36.08540568));
            Assert.Equal("W115080", L1011InsKeys.LongitudeKeys(-115.13363512));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // --- One calculator string per run of keys (measured live 2026-10-15: the INS takes several keys in ONE
    // string, each key's press and release back to back with no delay, exactly as key by key) ----------------

    private static readonly L1011ControlMap Map = L1011ControlMap.Load();

    private static string ClickOf(string controlId) => L1011InsKeys.ClickRpn(L1011WritePlan.ForPress(Map.Find(controlId)!));

    [Fact]
    public void A_keys_click_is_its_press_then_its_release_with_no_wait_between()
    {
        Assert.Equal("(>H:PAD_INS_1_KEY_2) 0 (>L:INS_1_KEY_2_PUSH)", ClickOf("INS_1_KEY_2"));
        Assert.Equal("1 (>L:INS_1_KEY_INSERT_PUSH) (>H:INS_1_KEY_INSERT) 0 (>L:INS_1_KEY_INSERT_PUSH)", ClickOf("INS_1_KEY_INSERT"));
        Assert.Equal("(>H:PAD_PMS_CPT_FWD) 0 (>L:PMS_CPT_FWD_PUSH)", ClickOf("PMS_CPT_FWD"));
    }

    [Fact]
    public void A_click_is_the_map_controls_own_press_plan_with_the_hold_dropped()
    {
        // [L1011-1]: the key stays the control's own click (L1011WritePlan.ForPress), only concatenated.
        foreach (var id in new[] { "INS_2_KEY_7", "INS_3_KEY_INSERT", "INS_1_KEY_CLEAR", "PMS_FO_LSK_3" })
        {
            var plan = L1011WritePlan.ForPress(Map.Find(id)!);
            Assert.Equal(string.Join(' ', plan.Steps.OfType<L1011CalcStep>().Select(s => s.Rpn)), L1011InsKeys.ClickRpn(plan));
        }
    }

    [Fact]
    public void Keys_that_fit_go_out_as_one_string_followed_by_a_gap()
    {
        var steps = L1011InsKeys.RunSteps(new[] { "a", "b", "c" });
        Assert.Equal(new L1011Step[] { new L1011CalcStep("a b c"), new L1011DelayStep(L1011InsKeys.RunGapMs) }, steps);
    }

    [Fact]
    public void Nothing_to_press_makes_no_steps()
    {
        Assert.Empty(L1011InsKeys.RunSteps(Array.Empty<string>()));
    }

    [Fact]
    public void A_run_over_the_limit_splits_at_a_key_boundary_with_a_gap_after_each_string()
    {
        string a = new('a', 400), b = new('b', 400), c = new('c', 400);
        var steps = L1011InsKeys.RunSteps(new[] { a, b, c });
        Assert.Equal(new L1011Step[]
        {
            new L1011CalcStep(a + " " + b), new L1011DelayStep(L1011InsKeys.RunGapMs),
            new L1011CalcStep(c), new L1011DelayStep(L1011InsKeys.RunGapMs),
        }, steps);
    }

    [Fact]
    public void A_string_may_be_exactly_the_limit_and_one_more_splits()
    {
        string fits = new string('a', 449) + " " + new string('b', 450);   // 900
        Assert.Equal(L1011InsKeys.MaxRunLength, fits.Length);
        Assert.Equal(2, L1011InsKeys.RunSteps(new[] { new string('a', 449), new string('b', 450) }).Count);   // one string, one gap

        var split = L1011InsKeys.RunSteps(new[] { new string('a', 449), new string('b', 451) });   // 901
        Assert.Equal(4, split.Count);
        Assert.Equal(new[] { 449, 451 }, split.OfType<L1011CalcStep>().Select(s => s.Rpn.Length));
    }

    [Fact]
    public void A_key_longer_than_the_limit_goes_alone_and_is_never_cut()
    {
        string huge = new('h', 1_000);
        var calcs = L1011InsKeys.RunSteps(new[] { "a", huge, "b" }).OfType<L1011CalcStep>().Select(s => s.Rpn).ToList();
        Assert.Equal(new[] { "a", huge, "b" }, calcs);
    }

    [Fact]
    public void Twenty_five_digit_keys_make_two_strings_cut_between_keys()
    {
        var clicks = Enumerable.Range(0, 25).Select(i => ClickOf($"INS_1_KEY_{i % 10}")).ToList();
        var calcs = L1011InsKeys.RunSteps(clicks).OfType<L1011CalcStep>().Select(s => s.Rpn).ToList();
        Assert.Equal(2, calcs.Count);
        Assert.All(calcs, rpn => Assert.True(rpn.Length <= L1011InsKeys.MaxRunLength));
        Assert.Equal(string.Join(' ', clicks), string.Join(' ', calcs));                 // nothing lost, nothing reordered
        Assert.Equal(string.Join(' ', clicks.Take(20)), calcs[0]);                         // 20 keys of 44 characters fit in 900
        Assert.Equal(string.Join(' ', clicks.Skip(20)), calcs[1]);
    }

    [Fact]
    public void The_limit_leaves_room_for_the_unique_prefix_in_the_1024_character_command()
    {
        Assert.Equal(900, L1011InsKeys.MaxRunLength);
        Assert.True(L1011InsKeys.MaxRunLength + "9223372036854775807 0 * ".Length < 1024);
        Assert.Equal(50, L1011InsKeys.RunGapMs);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Automatic_waypoint_loading_is_the_navigation_screens_own_switch(int unit)
    {
        // L1011_NAVIGATION.js toggleAutoBatch: on sets the flag and resumes the route, off only clears the flag.
        Assert.Equal($"1 (>L:INS_{unit}_AUTO_BATCH_ENABLED) (>H:INS_{unit}_RESUME_AUTOBATCH)", L1011InsKeys.AutoBatchRpn(unit, on: true));
        Assert.Equal($"0 (>L:INS_{unit}_AUTO_BATCH_ENABLED)", L1011InsKeys.AutoBatchRpn(unit, on: false));
    }

    [Fact]
    public void A_comma_decimal_culture_changes_no_automatic_loading_string()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("1 (>L:INS_2_AUTO_BATCH_ENABLED) (>H:INS_2_RESUME_AUTOBATCH)", L1011InsKeys.AutoBatchRpn(2, on: true));
            Assert.Equal("0 (>L:INS_2_AUTO_BATCH_ENABLED)", L1011InsKeys.AutoBatchRpn(2, on: false));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData('5', "PMS_CPT_5")]
    [InlineData(' ', "PMS_CPT_SP")]
    public void Pms_characters_map_to_its_keypad(char typed, string control)
    {
        Assert.Equal(control, L1011InsKeys.PmsControlFor("CPT", typed));
    }

    [Fact]
    public void Pms_function_keys()
    {
        Assert.Equal("PMS_FO_INSERT", L1011InsKeys.PmsKey("FO", "INSERT"));
        Assert.Equal("PMS_CPT_LSK_4", L1011InsKeys.PmsLineKey("CPT", 4));
        Assert.Null(L1011InsKeys.PmsControlFor("CPT", 'N'));
    }
}
