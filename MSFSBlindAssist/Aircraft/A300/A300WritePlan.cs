using System.Globalization;

namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>One thing the definition does to carry out a plan.</summary>
public abstract record A300Step;

/// <summary>Send this RPN through the calculator path (unique-ified by the sender).</summary>
public sealed record A300CalcStep(string Rpn) : A300Step;

/// <summary>Wait before the next step (a hold button's hold, a spring switch's hold).</summary>
public sealed record A300DelayStep(int Milliseconds) : A300Step;

/// <summary>What to do, or why nothing will be done. An empty plan with no refusal means the
/// control is already where the pilot asked; a refusal is an error the pilot must hear.</summary>
public sealed record A300Plan(IReadOnlyList<A300Step> Steps, string? Refusal)
{
    public static readonly A300Plan AlreadyThere = new(Array.Empty<A300Step>(), null);
    public static A300Plan Refused(string why) => new(Array.Empty<A300Step>(), why);
    public bool IsEmpty => Steps.Count == 0;
}

/// <summary>
/// Turns "set this control to that position" (or press / step it) into the aircraft's own B: Set
/// event, the way the cockpit's own click code calls it. Pure: no SimConnect, no clock.
///
/// Rules, in the order they bite:
/// <list type="bullet">
/// <item>A position already held is not written again.</item>
/// <item>A TOGGLE or COMMAND flips whatever value is passed (iniBuilds' own "_ON" bindings flip too,
/// measured live 2026-10-03), so it is sent only when the current position is KNOWN and differs;
/// an unknown position is refused, because a flip sent the wrong way round inverts the switch.</item>
/// <item>Selectors and knobs are absolute: Set n is position n, whatever the aircraft held.</item>
/// <item>A hold button is pressed with Set 2 and released with Set 0 after <see cref="HoldMs"/>; a
/// spring switch is held at the picked side for <see cref="SpringHoldMs"/>, then returned to rest.</item>
/// </list>
/// </summary>
public static class A300WritePlan
{
    public const double SameValueTolerance = 0.01;

    /// <summary>A hold button's press. The A300 samples them once per frame or slower; 250 ms
    /// reached the ECAM page keys every time in the live test (2026-10-03).</summary>
    public const int HoldMs = 250;

    /// <summary>How long a spring switch is held off centre (one trim nudge).</summary>
    public const int SpringHoldMs = 1000;

    public const string UnknownPositionRefusal = "position unknown, try again in a moment";
    public const string NotAPositionRefusal = "not a position of this control";
    public const string NotSettableRefusal = "cannot be set from this panel";

    /// <summary><paramref name="target"/> and <paramref name="current"/> are STATE values (what the
    /// state variable reads; for a knob, its scaled value).</summary>
    public static A300Plan ForSet(A300Control control, double target, double? current)
    {
        switch (control.Kind)
        {
            case A300Kinds.Toggle:
            case A300Kinds.Command:
            {
                if (control.PositionForState(target) is not double position)
                    return A300Plan.Refused(NotAPositionRefusal);
                if (current is not double now)
                    return A300Plan.Refused(UnknownPositionRefusal);
                if (Math.Abs(now - target) < SameValueTolerance)
                    return A300Plan.AlreadyThere;
                return Steps(Set(control, position));
            }
            case A300Kinds.Selector:
            {
                if (control.PositionForState(target) is not double position)
                    return A300Plan.Refused(NotAPositionRefusal);
                if (current is double now && Math.Abs(now - target) < SameValueTolerance)
                    return A300Plan.AlreadyThere;
                return Steps(Set(control, position));
            }
            case A300Kinds.Spring:
            {
                if (control.PositionForState(target) is not double position)
                    return A300Plan.Refused(NotAPositionRefusal);
                double rest = control.Rest ?? 1;
                if (Math.Abs(position - rest) < SameValueTolerance)
                    return Steps(Set(control, rest));
                return Steps(Set(control, position), new A300DelayStep(SpringHoldMs), Set(control, rest));
            }
            case A300Kinds.Knob:
            {
                double scale = control.Scale is > 0 ? control.Scale.Value : 1.0;
                double setValue = Math.Clamp(target / scale, 0, 100);
                if (current is double now && Math.Abs(now - setValue * scale) < SameValueTolerance * scale)
                    return A300Plan.AlreadyThere;
                return Steps(Set(control, setValue));
            }
            default:
                return A300Plan.Refused(NotSettableRefusal);
        }
    }

    public static A300Plan ForPress(A300Control control) => control.Kind switch
    {
        A300Kinds.Button => Steps(Set(control, control.Press ?? 1)),
        A300Kinds.Hold => Steps(Set(control, control.Press ?? 2), new A300DelayStep(HoldMs), Set(control, 0)),
        _ => A300Plan.Refused(NotSettableRefusal),
    };

    public static A300Plan ForStep(A300Control control, bool increase) => control.Kind == A300Kinds.Encoder
        ? Steps(Set(control, increase ? 1 : -1))
        : A300Plan.Refused(NotSettableRefusal);

    /// <summary><c>value (&gt;B:EVENT)</c>, the number in invariant culture.</summary>
    public static A300CalcStep Set(A300Control control, double value) =>
        new($"{Format(value)} (>B:{control.Event})");

    /// <summary>Invariant, up to four decimals, never exponent notation: the MSFS RPN parser
    /// rejects both comma decimals and "1E-05".</summary>
    public static string Format(double value) =>
        value.ToString("0.####", CultureInfo.InvariantCulture);

    private static A300Plan Steps(params A300Step[] steps) => new(steps, null);
}
