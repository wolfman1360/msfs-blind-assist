using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011SpeechTests
{
    [Fact]
    public void Readouts_read_invariantly_in_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("Flap handle 14 degrees, flaps 14 degrees", L1011Speech.Flaps(3, 13.8));
            Assert.Equal("Flap handle up, flaps 0 degrees", L1011Speech.Flaps(0, 0.2));
            Assert.Equal("Fuel 85,400 pounds", L1011Speech.Fuel(85_400, kilograms: false));
            Assert.Equal("Fuel 38,737 kilograms", L1011Speech.Fuel(85_400, kilograms: true));
            Assert.Equal("Altimeter 1013, 29.92", L1011Speech.Altimeter(1013.2));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Gear_reads_the_lever_then_the_legs()
    {
        Assert.Equal("Gear lever down, gear down", L1011Speech.Gear(0, 100, 100, 100));
        Assert.Equal("Gear lever up, gear up", L1011Speech.Gear(100, 0, 0, 0));
        Assert.Equal("Gear lever up, gear in transit: left 40, nose 35, right 42 percent", L1011Speech.Gear(100, 40.2, 35, 41.6));
    }
}
