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
}
