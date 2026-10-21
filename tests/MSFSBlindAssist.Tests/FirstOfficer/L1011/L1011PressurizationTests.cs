using System.Globalization;
using MSFSBlindAssist.FirstOfficer.L1011;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The pressurization maths behind the TriStar First Officer's cruise cabin altitude: the standard
/// atmosphere, the cabin altitude that holds the maximum differential at a cruise altitude (clamped to
/// the selector's range), and the cabin altitude selector's knob table. The knob table and its -250 to
/// 8,000 ft clamp are the aircraft's own; the live sim confirmed step 49 gives a 4,500 ft cabin target and
/// step 88.5 gives 7,549.
/// </summary>
public class L1011PressurizationTests
{
    [Fact]
    public void Isa_pressure_at_sea_level_and_the_tropopause()
    {
        Assert.Equal(1013.25, L1011Pressurization.IsaPressureHpa(0), 6);
        Assert.Equal(226.32, L1011Pressurization.IsaPressureHpa(36_089), 1);
        // Above the tropopause the pressure keeps falling, continuously from the troposphere's value.
        Assert.True(L1011Pressurization.IsaPressureHpa(40_000) < L1011Pressurization.IsaPressureHpa(36_089));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4_500)]
    [InlineData(8_000)]
    public void Isa_altitude_inverts_pressure(double altitudeFt)
    {
        double pressure = L1011Pressurization.IsaPressureHpa(altitudeFt);
        Assert.InRange(L1011Pressurization.IsaAltitudeFt(pressure), altitudeFt - 1, altitudeFt + 1);
    }

    [Theory]
    [InlineData(42_000, 8_000)]   // clamped: the unclamped value is about 8,060
    [InlineData(10_000, -250)]    // clamped: the differential limit is not reached
    [InlineData(0, -250)]
    public void Cabin_altitude_at_max_differential_is_clamped(double cruiseFt, int expectedFt)
    {
        Assert.Equal(expectedFt, L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(cruiseFt));
    }

    [Fact]
    public void Cabin_altitude_at_max_differential_at_35000()
    {
        Assert.InRange(L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(35_000), 5_780 - 60, 5_780 + 60);
    }

    [Fact]
    public void Cabin_altitude_at_max_differential_before_the_clamp_is_about_8060_at_42000()
    {
        double unclamped = L1011Pressurization.IsaAltitudeFt(
            L1011Pressurization.IsaPressureHpa(42_000)
            + L1011Pressurization.MaxDifferentialPsi * L1011Pressurization.HectopascalsPerPsi);
        Assert.InRange(unclamped, 8_040, 8_080);
    }

    [Theory]
    [InlineData(4_500, 49)]       // live: step 49 gave 4,500
    [InlineData(0, 3)]
    [InlineData(-250, 0)]
    [InlineData(8_000, 100)]
    [InlineData(7_000, 75)]       // 74.5 rounds away from zero
    [InlineData(5_500, 59)]       // midway between the 5,000 and 6,000 points
    [InlineData(6_500, 69)]       // 64 + 5.25 on the 6,000 to 7,000 segment, 69.25
    [InlineData(9_000, 100)]      // clamped to the table's end
    [InlineData(-500, 0)]
    public void Knob_step_inverts_the_aircrafts_table(int cabinAltitudeFt, int expectedStep)
    {
        Assert.Equal(expectedStep, L1011Pressurization.KnobStep(cabinAltitudeFt));
    }

    [Theory]
    [InlineData(49, 4_500, 0)]
    [InlineData(88.5, 7_549, 1)]  // the cold default seen live
    [InlineData(0, -250, 0)]
    [InlineData(100, 8_000, 0)]
    [InlineData(-10, -250, 0)]    // clamped to the table's end
    [InlineData(150, 8_000, 0)]
    public void Cabin_altitude_for_step(double step, double expectedFt, double tolerance)
    {
        Assert.Equal(expectedFt, L1011Pressurization.CabinAltitudeForStep(step), tolerance);
    }

    [Fact]
    public void Knob_table_is_the_aircrafts_own()
    {
        Assert.Equal(
            new[] { (0.0, -250), (3.0, 0), (13.0, 1000), (23.0, 2000), (33.0, 3000), (44.0, 4000), (54.0, 5000), (64.0, 6000), (74.5, 7000), (100.0, 8000) },
            L1011Pressurization.KnobTable);
    }

    [Fact]
    public void Constants_are_the_aircrafts_and_the_standard_atmospheres()
    {
        Assert.Equal(8.42, L1011Pressurization.MaxDifferentialPsi);
        Assert.Equal(68.9476, L1011Pressurization.HectopascalsPerPsi);
        Assert.Equal(-250, L1011Pressurization.MinCabinAltitudeFt);
        Assert.Equal(8_000, L1011Pressurization.MaxCabinAltitudeFt);
        Assert.Equal(1013.25, L1011Pressurization.SeaLevelHpa);
        Assert.Equal(36_089.24, L1011Pressurization.TropopauseFt);
    }

    [Fact]
    public void Comma_culture_changes_nothing()
    {
        CultureInfo saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal(8_000, L1011Pressurization.CabinAltitudeAtMaxDifferentialFt(42_000));
            Assert.Equal(49, L1011Pressurization.KnobStep(4_500));
            Assert.Equal(4_500, L1011Pressurization.CabinAltitudeForStep(49), 6);
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }
}
