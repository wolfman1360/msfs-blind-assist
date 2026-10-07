using System.Globalization;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>Typed autopilot targets: the knob's own limits and steps, the gauge's own writes, and the
/// figure spoken back.</summary>
public class L1011AfcsTests
{
    [Theory]
    [InlineData(L1011AfcsValue.Heading, 90, 90)]
    [InlineData(L1011AfcsValue.Heading, 360, 0)]
    [InlineData(L1011AfcsValue.Heading, 0, 0)]
    [InlineData(L1011AfcsValue.Heading, 359.6, 0)]
    [InlineData(L1011AfcsValue.Course1, 247, 247)]
    [InlineData(L1011AfcsValue.Speed, 100, 100)]
    [InlineData(L1011AfcsValue.Speed, 350, 350)]
    [InlineData(L1011AfcsValue.Speed, 249.5, 250)]
    [InlineData(L1011AfcsValue.Altitude, 35000, 35000)]
    [InlineData(L1011AfcsValue.Altitude, 12345, 12300)]
    [InlineData(L1011AfcsValue.Altitude, 50000, 50000)]
    [InlineData(L1011AfcsValue.Altitude, 0, 0)]
    [InlineData(L1011AfcsValue.VerticalSpeed, -1550, -1600)]
    [InlineData(L1011AfcsValue.VerticalSpeed, 1449, 1400)]
    [InlineData(L1011AfcsValue.VerticalSpeed, 6000, 6000)]
    public void A_typed_number_becomes_what_the_knob_would_set(L1011AfcsValue kind, double typed, int expected)
    {
        Assert.Equal(expected, L1011Afcs.Normalise(kind, typed));
    }

    [Theory]
    [InlineData(L1011AfcsValue.Heading, 361)]
    [InlineData(L1011AfcsValue.Heading, -1)]
    [InlineData(L1011AfcsValue.Speed, 99)]
    [InlineData(L1011AfcsValue.Speed, 351)]
    [InlineData(L1011AfcsValue.Altitude, 50001)]
    [InlineData(L1011AfcsValue.Altitude, -100)]
    [InlineData(L1011AfcsValue.VerticalSpeed, 6001)]
    [InlineData(L1011AfcsValue.VerticalSpeed, double.NaN)]
    public void A_number_outside_the_knob_limits_is_refused(L1011AfcsValue kind, double typed)
    {
        Assert.Null(L1011Afcs.Normalise(kind, typed));
    }

    [Fact]
    public void Each_value_is_written_as_the_aircraft_knob_writes_it()
    {
        Assert.Equal("90 (>K:HEADING_BUG_SET)", L1011Afcs.Rpn(L1011AfcsValue.Heading, 90));
        Assert.Equal("250 (>L:INI_AT_TARGET)", L1011Afcs.Rpn(L1011AfcsValue.Speed, 250));
        Assert.Equal("35000 (>L:ALTITUDE_SETPOINT_0) (>H:AUTOPILOT_CAPTURE_UPDATE)", L1011Afcs.Rpn(L1011AfcsValue.Altitude, 35000));
        Assert.Equal("-1500 (>L:INI_VS_PID_SETPOINT_2) (>H:AUTOPILOT_SWITCH_AFCS_VS_SEL)", L1011Afcs.Rpn(L1011AfcsValue.VerticalSpeed, -1500));
        Assert.Equal("247 (>K:VOR1_SET)", L1011Afcs.Rpn(L1011AfcsValue.Course1, 247));
        Assert.Equal("67 (>K:VOR2_SET)", L1011Afcs.Rpn(L1011AfcsValue.Course2, 67));
    }

    [Fact]
    public void The_confirmation_reads_the_figure_as_the_window_shows_it()
    {
        Assert.Equal("Heading 090", L1011Afcs.Confirmation(L1011AfcsValue.Heading, 90));
        Assert.Equal("Course 2 005", L1011Afcs.Confirmation(L1011AfcsValue.Course2, 5));
        Assert.Equal("Vertical speed +1500", L1011Afcs.Confirmation(L1011AfcsValue.VerticalSpeed, 1500));
        Assert.Equal("Vertical speed -800", L1011Afcs.Confirmation(L1011AfcsValue.VerticalSpeed, -800));
        Assert.Equal("Vertical speed 0", L1011Afcs.Confirmation(L1011AfcsValue.VerticalSpeed, 0));
        Assert.Equal("Altitude 35000", L1011Afcs.Confirmation(L1011AfcsValue.Altitude, 35000));
        Assert.Equal("Speed 250", L1011Afcs.Confirmation(L1011AfcsValue.Speed, 250));
    }

    [Theory]
    [InlineData("1500", 1500)]
    [InlineData(" -800 ", -800)]
    [InlineData("1450,0", 1500)]
    [InlineData("1449.9", 1400)]
    public void Dialog_text_parses_invariantly(string text, int expected)
    {
        Assert.Equal(expected, L1011Afcs.Parse(L1011AfcsValue.VerticalSpeed, text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("7000")]
    public void Unusable_dialog_text_is_null(string? text)
    {
        Assert.Null(L1011Afcs.Parse(L1011AfcsValue.VerticalSpeed, text));
    }

    [Fact]
    public void A_comma_decimal_culture_changes_nothing_written_or_spoken()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            Assert.Equal(-1500, L1011Afcs.Parse(L1011AfcsValue.VerticalSpeed, "-1500"));
            Assert.Equal(250, L1011Afcs.Parse(L1011AfcsValue.Speed, "249,6"));
            Assert.Equal("-1500 (>L:INI_VS_PID_SETPOINT_2) (>H:AUTOPILOT_SWITCH_AFCS_VS_SEL)", L1011Afcs.Rpn(L1011AfcsValue.VerticalSpeed, -1500));
            Assert.Equal("Vertical speed -1500", L1011Afcs.Confirmation(L1011AfcsValue.VerticalSpeed, -1500));
            Assert.Equal("Altitude 35000", L1011Afcs.Confirmation(L1011AfcsValue.Altitude, 35000));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void The_disconnect_button_fires_the_event_the_yoke_button_fires()
    {
        // The yoke switch's click fires H:AUTOPILOT_AUTOPILOT_DISCONNECT whichever way it moves,
        // and the aircraft turns the stock AUTOPILOT_OFF key into the same event (L1011_HANDLING.js).
        Assert.Equal("(>H:AUTOPILOT_AUTOPILOT_DISCONNECT)", L1011Afcs.DisconnectRpn);
        Assert.Contains(L1011Afcs.DisconnectKey, L1011Afcs.RowKeys);
        Assert.Subset(L1011Afcs.RowKeys.ToHashSet(), L1011Afcs.Keys.ToHashSet());
        Assert.Subset(L1011Levers.Keys.ToHashSet(), L1011Afcs.RowKeys.ToHashSet());
        Assert.Null(L1011Afcs.ValueFor(L1011Afcs.DisconnectKey));
    }

    [Fact]
    public void Every_typed_key_maps_back_to_its_value()
    {
        Assert.Equal(6, L1011Afcs.Keys.Count);
        foreach (var key in L1011Afcs.Keys)
            Assert.NotNull(L1011Afcs.ValueFor(key));
        Assert.Equal(L1011AfcsValue.Course2, L1011Afcs.ValueFor(L1011Afcs.Course2Key));
        Assert.Null(L1011Afcs.ValueFor("SWITCH_AFCS_HDG"));
        Assert.Subset(L1011Levers.Keys.ToHashSet(), L1011Afcs.Keys.ToHashSet());
    }
}
