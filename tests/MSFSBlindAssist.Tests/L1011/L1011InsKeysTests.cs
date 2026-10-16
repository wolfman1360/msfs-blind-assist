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
