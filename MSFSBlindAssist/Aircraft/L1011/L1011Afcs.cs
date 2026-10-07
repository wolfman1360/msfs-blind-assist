using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>The six values a pilot sets on the TriStar's glareshield autopilot (AFCS) panel.</summary>
public enum L1011AfcsValue { Heading, Speed, Altitude, VerticalSpeed, Course1, Course2 }

/// <summary>
/// Typed autopilot targets, written EXACTLY as the aircraft's own knob handlers write them
/// (L1011_AFCS.js and L1011_INSTRUMENTS.js, package 1.0.8), so a typed value lands where a turn of
/// the knob would have put it:
/// <list type="bullet">
/// <item>heading — <c>K:HEADING_BUG_SET</c>; the heading window shows <c>AUTOPILOT HEADING LOCK DIR</c>;</item>
/// <item>altitude — <c>L:ALTITUDE_SETPOINT_0</c> (the knob clamps 0 to 50,000 in 100 ft steps), then
/// <c>H:AUTOPILOT_CAPTURE_UPDATE</c> so the capture logic re-reads it;</item>
/// <item>speed — <c>L:INI_AT_TARGET</c> (the knob clamps 100 to 350 knots);</item>
/// <item>vertical speed — <c>L:INI_VS_PID_SETPOINT_2</c>, then <c>H:AUTOPILOT_SWITCH_AFCS_VS_SEL</c>,
/// what the vertical-speed wheel does (100 fpm a click);</item>
/// <item>course 1 and 2 — <c>K:VOR1_SET</c> / <c>K:VOR2_SET</c>; the course windows show NAV OBS:1/2.</item>
/// </list>
/// Pure: parsing, limits, RPN and the confirmation spoken back. Numbers are invariant culture.
/// </summary>
public static class L1011Afcs
{
    public const string HeadingKey = "L1011_AFCS_HEADING_SET";
    public const string SpeedKey = "L1011_AFCS_SPEED_SET";
    public const string AltitudeKey = "L1011_AFCS_ALTITUDE_SET";
    public const string VerticalSpeedKey = "L1011_AFCS_VS_SET";
    public const string Course1Key = "L1011_AFCS_COURSE1_SET";
    public const string Course2Key = "L1011_AFCS_COURSE2_SET";

    /// <summary>
    /// Switching vertical speed mode on sets the target to the current vertical speed, rounded to
    /// 100 (L1011_INS.js; seen live at the gate on 2026-10-03), so a value typed while the mode is off
    /// would be overwritten: it is refused with this reason instead.
    /// </summary>
    public const string VerticalSpeedModeOffRefusal = "turn vertical speed mode on first";

    public const int MinSpeed = 100;
    public const int MaxSpeed = 350;
    public const int MaxAltitude = 50000;
    public const int MaxVerticalSpeed = 6000;

    /// <summary>The six typed-value keys.</summary>
    public static IReadOnlySet<string> Keys { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        HeadingKey, SpeedKey, AltitudeKey, VerticalSpeedKey, Course1Key, Course2Key,
    };

    /// <summary>
    /// The "Autopilot disconnect" button. It fires the one event that does the work: the yoke
    /// switch's click fires H:AUTOPILOT_AUTOPILOT_DISCONNECT whichever way the switch moves (its own
    /// L:var and the _0/_1 events drive only the animation), and the aircraft turns the stock
    /// AUTOPILOT_OFF key into the same event (L1011_HANDLING.js). The INS gauge then sets both
    /// engage switches to Off and thrust management off.
    /// </summary>
    public const string DisconnectKey = "L1011_AFCS_DISCONNECT";
    public const string DisconnectRpn = "(>H:AUTOPILOT_AUTOPILOT_DISCONNECT)";

    /// <summary>Every hand-written autopilot row: the typed values and the disconnect button.</summary>
    public static IReadOnlySet<string> RowKeys { get; } =
        new HashSet<string>(Keys.Append(DisconnectKey), StringComparer.Ordinal);

    /// <summary>Which value a typed-entry key sets, or null for any other key.</summary>
    public static L1011AfcsValue? ValueFor(string key) => key switch
    {
        HeadingKey => L1011AfcsValue.Heading,
        SpeedKey => L1011AfcsValue.Speed,
        AltitudeKey => L1011AfcsValue.Altitude,
        VerticalSpeedKey => L1011AfcsValue.VerticalSpeed,
        Course1Key => L1011AfcsValue.Course1,
        Course2Key => L1011AfcsValue.Course2,
        _ => null,
    };

    public static string Name(L1011AfcsValue kind) => kind switch
    {
        L1011AfcsValue.Heading => "Heading",
        L1011AfcsValue.Speed => "Speed",
        L1011AfcsValue.Altitude => "Altitude",
        L1011AfcsValue.VerticalSpeed => "Vertical speed",
        L1011AfcsValue.Course1 => "Course 1",
        _ => "Course 2",
    };

    /// <summary>What may be typed, read with the error.</summary>
    public static string RangeText(L1011AfcsValue kind) => kind switch
    {
        L1011AfcsValue.Speed => "100 to 350 knots",
        L1011AfcsValue.Altitude => "0 to 50000 feet",
        L1011AfcsValue.VerticalSpeed => "-6000 to 6000 feet per minute",
        _ => "0 to 359 degrees",
    };

    /// <summary>
    /// A typed number as the value the knob would produce, or null outside the knob's limits:
    /// headings and courses wrap 360 to 0; altitude and vertical speed round to the nearest 100,
    /// the knob's step; speed rounds to a whole knot.
    /// </summary>
    public static int? Normalise(L1011AfcsValue kind, double typed)
    {
        if (double.IsNaN(typed) || double.IsInfinity(typed))
            return null;
        int whole = (int)Math.Round(typed, MidpointRounding.AwayFromZero);
        switch (kind)
        {
            case L1011AfcsValue.Speed:
                return whole is >= MinSpeed and <= MaxSpeed ? whole : null;
            case L1011AfcsValue.Altitude:
            {
                if (typed < 0 || typed > MaxAltitude) return null;
                return (int)(Math.Round(typed / 100, MidpointRounding.AwayFromZero) * 100);
            }
            case L1011AfcsValue.VerticalSpeed:
            {
                if (Math.Abs(typed) > MaxVerticalSpeed) return null;
                return (int)(Math.Round(typed / 100, MidpointRounding.AwayFromZero) * 100);
            }
            default:
                return whole is >= 0 and <= 360 ? whole % 360 : null;
        }
    }

    /// <summary>Text from a dialog box: invariant, a decimal comma accepted, surrounding spaces ignored.</summary>
    public static int? Parse(L1011AfcsValue kind, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? Normalise(kind, v)
            : null;
    }

    /// <summary>The calculator string that sets the value, as the aircraft's own knob does.</summary>
    public static string Rpn(L1011AfcsValue kind, int value)
    {
        string v = value.ToString(CultureInfo.InvariantCulture);
        return kind switch
        {
            L1011AfcsValue.Heading => $"{v} (>K:HEADING_BUG_SET)",
            L1011AfcsValue.Speed => $"{v} (>L:INI_AT_TARGET)",
            L1011AfcsValue.Altitude => $"{v} (>L:ALTITUDE_SETPOINT_0) (>H:AUTOPILOT_CAPTURE_UPDATE)",
            L1011AfcsValue.VerticalSpeed => $"{v} (>L:INI_VS_PID_SETPOINT_2) (>H:AUTOPILOT_SWITCH_AFCS_VS_SEL)",
            L1011AfcsValue.Course1 => $"{v} (>K:VOR1_SET)",
            _ => $"{v} (>K:VOR2_SET)",
        };
    }

    /// <summary>The figure as the window shows it: "090" for a heading, "+1500" for a climb.</summary>
    public static string Display(L1011AfcsValue kind, int value) => kind switch
    {
        L1011AfcsValue.VerticalSpeed => value.ToString("+0;-0;0", CultureInfo.InvariantCulture),
        L1011AfcsValue.Speed or L1011AfcsValue.Altitude => value.ToString(CultureInfo.InvariantCulture),
        _ => value.ToString("000", CultureInfo.InvariantCulture),
    };

    /// <summary>"Heading 090": a typed value is confirmed with the exact figure set.</summary>
    public static string Confirmation(L1011AfcsValue kind, int value) => $"{Name(kind)} {Display(kind, value)}";
}
