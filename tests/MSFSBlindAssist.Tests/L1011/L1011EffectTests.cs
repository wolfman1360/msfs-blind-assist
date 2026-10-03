using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011EffectTests
{
    [Theory]
    [InlineData("L:TOGGLE_Battery=1", "1 (>L:TOGGLE_Battery)")]
    [InlineData("L:SWITCH_APU_START, boolean=0", "0 (>L:SWITCH_APU_START, boolean)")]
    [InlineData("H:ELECTRICAL_1", "(>H:ELECTRICAL_1)")]
    [InlineData("K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE", "(>K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE)")]
    [InlineData("K:FUELSYSTEM_VALVE_TOGGLE=2", "2 (>K:FUELSYSTEM_VALVE_TOGGLE)")]
    [InlineData("K:2:HYDRAULIC_ACTUATOR_ACTIVE_SET=1,'Slats'_n", "1 'Slats'_n (>K:2:HYDRAULIC_ACTUATOR_ACTIVE_SET)")]
    [InlineData("A:FLAPS HANDLE INDEX, number=2", "2 (>A:FLAPS HANDLE INDEX, number)")]
    public void Renders_the_cockpit_rpn(string text, string rpn)
    {
        Assert.Equal(rpn, L1011Effect.Parse(text).ToRpn());
    }

    [Fact]
    public void Target_placeholder_is_formatted_invariantly_even_in_a_comma_culture()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("37.5 (>L:ROTARY_FLT_STA)", L1011Effect.Parse("L:ROTARY_FLT_STA={v}").ToRpn(37.5));
            Assert.Equal("100 (>L:ROTARY_FLT_STA)", L1011Effect.Parse("L:ROTARY_FLT_STA={v}").ToRpn(100));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Toggle_events_are_recognised()
    {
        Assert.True(L1011Effect.Parse("K:ANTISKID_BRAKES_TOGGLE").IsToggleEvent);
        Assert.False(L1011Effect.Parse("K:TURBINE_IGNITION_SWITCH_SET1=1").IsToggleEvent);
        Assert.False(L1011Effect.Parse("H:FUEL").IsToggleEvent);
    }

    [Fact]
    public void Garbage_is_rejected()
    {
        Assert.Throws<FormatException>(() => L1011Effect.Parse("X:NOPE"));
        Assert.Throws<FormatException>(() => L1011Effect.Parse("L:NO_VALUE"));
    }
}
