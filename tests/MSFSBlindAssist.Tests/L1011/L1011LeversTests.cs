using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011LeversTests
{
    [Fact]
    public void Flap_detents_match_the_flight_model()
    {
        Assert.Equal(new[] { "Up", "4 degrees", "10 degrees", "14 degrees", "18 degrees", "22 degrees", "33 degrees" },
            L1011Levers.FlapPositions.OrderBy(p => p.Key).Select(p => p.Value));
        Assert.Equal("3 (>A:FLAPS HANDLE INDEX, number)", L1011Levers.FlapRpn(3));
        Assert.Equal("6 (>A:FLAPS HANDLE INDEX, number)", L1011Levers.FlapRpn(9));
    }

    [Fact]
    public void Gear_down_and_up_use_the_intercepted_stock_events()
    {
        Assert.Equal("(>K:GEAR_DOWN)", L1011Levers.GearRpn(0));
        Assert.Equal("(>K:GEAR_UP)", L1011Levers.GearRpn(100));
        Assert.Equal("50 (>L:LEVER_LANDING_GEAR)", L1011Levers.GearRpn(50));
    }

    [Theory]
    [InlineData(29.92, 1013.2)]
    [InlineData(1013, 1013)]
    [InlineData(28.2, 955.0)]
    public void Typed_altimeter_values_become_millibars(double typed, double expected)
    {
        Assert.Equal(expected, L1011Levers.AltimeterMillibars(typed)!.Value, 1);
    }

    [Theory]
    [InlineData(27.0)]
    [InlineData(500)]
    [InlineData(2992)]
    public void Out_of_range_altimeter_values_are_refused(double typed)
    {
        Assert.Null(L1011Levers.AltimeterMillibars(typed));
    }

    [Fact]
    public void The_altimeter_write_matches_the_aircraft_and_ignores_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("1 16211 (>K:2:KOHLSMAN_SET)", L1011Levers.AltimeterRpn(1, 1013.2));
            Assert.Equal("37 (>L:LEVER_SPOILERS)", L1011Levers.SpeedBrakeRpn(36.6));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Nav_frequencies_are_validated()
    {
        Assert.Equal(110_300_000u, L1011Levers.NavFrequencyHz(110.3));
        Assert.Null(L1011Levers.NavFrequencyHz(121.5));
        Assert.Equal("110300000 (>K:NAV1_RADIO_SET_HZ)", L1011Levers.NavFrequencyRpn(1, 110_300_000));
    }

    [Fact]
    public void Brakes_and_spoilers_send_absolute_events()
    {
        Assert.Equal("1 (>K:PARKING_BRAKE_SET)", L1011Levers.ParkingBrakeRpn(1));
        Assert.Equal("0 (>K:SPOILERS_ARM_SET)", L1011Levers.GroundSpoilersRpn(0));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(12, 0)]
    [InlineData(50, 50)]
    [InlineData(80, 100)]
    [InlineData(100, 100)]
    public void The_gear_lever_travel_reads_as_its_nearest_position(double travel, double key)
    {
        Assert.Equal(key, L1011Levers.GearDescriptionKey(travel));
    }

    [Fact]
    public void Keys_carry_no_colon_and_each_typed_key_maps_back()
    {
        Assert.DoesNotContain(L1011Levers.Keys, k => k.Contains(':'));
        Assert.Equal(3, L1011Levers.AltimeterIndex(L1011Levers.StandbyAltimeterKey));
        Assert.Equal((2, true), L1011Levers.ComEntry(L1011Levers.ComActiveKey(2)));
        Assert.Equal((3, false), L1011Levers.ComEntry(L1011Levers.ComStandbyKey(3)));
        Assert.Equal(1, L1011Levers.NavEntry(L1011Levers.NavFrequencyKey(1)));
        Assert.Null(L1011Levers.ComEntry(L1011Levers.FlapHandleKey));
        Assert.Null(L1011Levers.AltimeterIndex(L1011Levers.SquawkKey));
    }

    [Fact]
    public void Com_frequencies_set_standby_and_swap_for_active()
    {
        Assert.Equal(121_900_000u, L1011Levers.ComFrequencyHz(121.9));
        Assert.Null(L1011Levers.ComFrequencyHz(117.0));
        Assert.Equal("121900000 (>K:COM_STBY_RADIO_SET_HZ)", L1011Levers.ComFrequencyRpn(1, 121_900_000, active: false));
        Assert.Equal("121900000 (>K:COM2_STBY_RADIO_SET_HZ) (>K:COM2_RADIO_SWAP)", L1011Levers.ComFrequencyRpn(2, 121_900_000, active: true));
    }

    [Theory]
    [InlineData(1200, 0x1200u)]
    [InlineData(7, 0x0007u)]
    [InlineData(7700, 0x7700u)]
    public void Squawks_become_bcd(double typed, uint bcd)
    {
        Assert.Equal(bcd, L1011Levers.SquawkBcd(typed));
        Assert.Equal($"{bcd} (>K:XPNDR_SET)", L1011Levers.SquawkRpn(bcd));
    }

    [Theory]
    [InlineData(1280)]
    [InlineData(-1)]
    [InlineData(10000)]
    [InlineData(12.5)]
    public void Squawks_that_are_not_four_octal_digits_are_refused(double typed)
    {
        Assert.Null(L1011Levers.SquawkBcd(typed));
    }

    [Fact]
    public void Lever_announcements()
    {
        Assert.Equal("Flaps up", L1011Levers.Announcement(L1011Levers.FlapHandleKey, 0));
        Assert.Equal("Flaps 22 degrees", L1011Levers.Announcement(L1011Levers.FlapHandleKey, 5));
        Assert.Equal("Parking brake released", L1011Levers.Announcement(L1011Levers.ParkingBrakeKey, 0));
        Assert.Equal("Ground spoilers armed", L1011Levers.Announcement(L1011Levers.GroundSpoilersKey, 1));
        Assert.Null(L1011Levers.Announcement(L1011Levers.GearLeverKey, 100));
    }

    [Fact]
    public void Confirmations_read_invariantly_in_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal("Captain altimeter 1013, 29.92", L1011Levers.AltimeterConfirmation("Captain altimeter", 1013.2));
            Assert.Equal("COM 2 active 121.900", L1011Levers.FrequencyConfirmation("COM 2 active", 121.9, 3));
            Assert.Equal("NAV 1 110.30", L1011Levers.FrequencyConfirmation("NAV 1", 110.3, 2));
            Assert.Equal("Squawk 1200", L1011Levers.SquawkConfirmation(0x1200));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- Input mode Ctrl+B: one entry sets all three altimeters ---------------------------

    [Theory]
    [InlineData(29.92, "16211")]
    [InlineData(1013, "16208")]
    public void Ctrl_b_writes_the_captain_first_officer_and_standby_altimeters_in_that_order_in_one_string(
        double typed, string word)
    {
        double mb = L1011Levers.AltimeterMillibars(typed)!.Value;
        Assert.Equal(
            $"{L1011Levers.AltimeterRpn(1, mb)} {L1011Levers.AltimeterRpn(2, mb)} {L1011Levers.AltimeterRpn(3, mb)}",
            L1011Levers.AllAltimetersRpn(mb));
        Assert.Equal(
            $"1 {word} (>K:2:KOHLSMAN_SET) 2 {word} (>K:2:KOHLSMAN_SET) 3 {word} (>K:2:KOHLSMAN_SET)",
            L1011Levers.AllAltimetersRpn(mb));
    }

    [Theory]
    [InlineData("29.92", 1013.2)]
    [InlineData("29,92", 1013.2)]
    [InlineData(" 1013 ", 1013.0)]
    [InlineData("28.20", 955.0)]
    public void A_ctrl_b_entry_is_read_like_the_typed_altimeter_fields(string text, double millibars)
    {
        Assert.Equal(millibars, L1011Levers.AltimeterEntryMillibars(text)!.Value, 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("50")]
    [InlineData("2992")]
    [InlineData("31.31")]
    public void A_ctrl_b_entry_outside_the_range_or_not_a_number_is_refused(string text)
    {
        Assert.Null(L1011Levers.AltimeterEntryMillibars(text));
    }

    [Fact]
    public void Ctrl_b_names_the_altimeters_in_its_error_refusal_and_confirmation()
    {
        Assert.Equal("Altimeters: enter 28.20 to 31.30 inches, or 955 to 1060 hectopascals", L1011Levers.AltimetersEntryError);
        Assert.Equal("Altimeters unavailable", L1011Levers.Unavailable(L1011Levers.AltimetersName));
        Assert.Equal("Altimeters 1013, 29.92",
            L1011Levers.AltimeterConfirmation(L1011Levers.AltimetersName, L1011Levers.AltimeterEntryMillibars("29.92")!.Value));
    }

    // ---- Input mode Ctrl+N: both NAV radios, frequency and course ------------------------

    [Fact]
    public void Ctrl_n_tunes_both_radios_and_sets_both_courses_in_one_string()
    {
        Assert.Equal(
            "110300000 (>K:NAV1_RADIO_SET_HZ) 45 (>K:VOR1_SET) 113900000 (>K:NAV2_RADIO_SET_HZ) 270 (>K:VOR2_SET)",
            L1011Levers.NavRadiosRpn(110.3, 45, 113.9, 270));
        Assert.Equal("45 (>K:VOR1_SET)", L1011Levers.NavCourseRpn(1, 45));
        Assert.Equal("0 (>K:VOR2_SET)", L1011Levers.NavCourseRpn(2, 0));
    }

    [Theory]
    [InlineData(121.5, 45, 113.9, 270)]
    [InlineData(110.3, 45, 107.95, 270)]
    [InlineData(110.3, 360, 113.9, 270)]
    [InlineData(110.3, 45, 113.9, -1)]
    public void Ctrl_n_refuses_a_frequency_or_course_out_of_range(double f1, int c1, double f2, int c2)
    {
        Assert.Null(L1011Levers.NavRadiosRpn(f1, c1, f2, c2));
    }

    [Fact]
    public void Ctrl_n_confirms_both_radios_in_one_sentence_and_names_itself_when_refused()
    {
        Assert.Equal("NAV 1 110.30, course 45; NAV 2 113.90, course 270",
            L1011Levers.NavRadiosConfirmation(110.3, 45, 113.9, 270));
        Assert.Equal("NAV radios unavailable", L1011Levers.Unavailable(L1011Levers.NavRadiosName));
    }

    [Theory]
    [InlineData(110.3, 110.3)]
    [InlineData(117.95, 117.95)]
    [InlineData(0.0, 108.0)]
    [InlineData(121.5, 108.0)]
    [InlineData(double.NaN, 108.0)]
    [InlineData(null, 108.0)]
    public void Ctrl_n_pre_fills_the_active_frequency_or_108(double? live, double expected)
    {
        Assert.Equal(expected, L1011Levers.NavPrefillMegahertz(live), 2);
    }

    [Theory]
    [InlineData(45.2, 45)]
    [InlineData(359.6, 0)]
    [InlineData(-10.0, 350)]
    [InlineData(double.NaN, 0)]
    [InlineData(null, 0)]
    public void Ctrl_n_pre_fills_the_course_in_whole_degrees_or_0(double? live, int expected)
    {
        Assert.Equal(expected, L1011Levers.NavPrefillCourse(live));
    }

    [Fact]
    public void Ctrl_b_and_ctrl_n_texts_ignore_a_comma_culture()
    {
        double mb = L1011Levers.AltimeterEntryMillibars("29.92")!.Value;
        string allAltimeters = L1011Levers.AllAltimetersRpn(mb);
        string confirmation = L1011Levers.AltimeterConfirmation(L1011Levers.AltimetersName, mb);
        string error = L1011Levers.AltimetersEntryError;
        string? nav = L1011Levers.NavRadiosRpn(110.3, 45, 113.9, 270);
        string navConfirmation = L1011Levers.NavRadiosConfirmation(110.3, 45, 113.9, 270);
        double prefill = L1011Levers.NavPrefillMegahertz(113.9);

        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(mb, L1011Levers.AltimeterEntryMillibars("29.92")!.Value, 6);
            Assert.Equal(allAltimeters, L1011Levers.AllAltimetersRpn(mb));
            Assert.Equal(confirmation, L1011Levers.AltimeterConfirmation(L1011Levers.AltimetersName, mb));
            Assert.Equal(error, L1011Levers.AltimetersEntryError);
            Assert.Equal(nav, L1011Levers.NavRadiosRpn(110.3, 45, 113.9, 270));
            Assert.Equal(navConfirmation, L1011Levers.NavRadiosConfirmation(110.3, 45, 113.9, 270));
            Assert.Equal(prefill, L1011Levers.NavPrefillMegahertz(113.9));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
