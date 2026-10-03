namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One thing the definition does to carry out a plan.</summary>
public abstract record L1011Step;

/// <summary>Send this RPN through the calculator path (unique-ified by the sender).</summary>
public sealed record L1011CalcStep(string Rpn) : L1011Step;

/// <summary>Wait before the next step (a button's hold, a spring switch's hold).</summary>
public sealed record L1011DelayStep(int Milliseconds) : L1011Step;

/// <summary>What to do, or why nothing will be done. An empty plan with no refusal means the
/// control is already where the pilot asked; a refusal is an error the pilot must hear.</summary>
public sealed record L1011Plan(IReadOnlyList<L1011Step> Steps, string? Refusal)
{
    public static readonly L1011Plan AlreadyThere = new(Array.Empty<L1011Step>(), null);
    public static L1011Plan Refused(string why) => new(Array.Empty<L1011Step>(), why);
    public bool IsEmpty => Steps.Count == 0;
}

/// <summary>
/// Turns "set this control to that position" (or press / step it) into the ordered calculator
/// writes the cockpit's own click code performs. Pure: no SimConnect, no clock.
///
/// Rules, in the order they bite:
/// <list type="bullet">
/// <item>A position already held (within <see cref="SameValueTolerance"/>) is not written again:
/// replaying a transition re-fires its H: events, and some gauge handlers act on every event.</item>
/// <item>A transition carrying a TOGGLE K: event (seat-belt signs, anti-skid, fuel cutoff valves,
/// APU bleed) is refused when the current position is unknown, because sending a toggle the
/// wrong way round inverts the switch and the sim state it drives.</item>
/// <item>All effects of one transition go out as ONE RPN string when they fit, so the L:var is
/// written before the H: event that makes the gauge re-read it, in the same frame. A string
/// longer than <see cref="MaxRpnLength"/> (MobiFlight's 1024-byte command less its prefixes) is
/// split at effect boundaries, keeping order.</item>
/// <item>Buttons hold <see cref="EventButtonHoldMs"/> when the press fires an event and
/// <see cref="PolledButtonHoldMs"/> when it only writes an L:var a gauge polls (test buttons,
/// APU start), so the poll cannot miss the press.</item>
/// </list>
/// </summary>
public static class L1011WritePlan
{
    public const double SameValueTolerance = 0.01;
    public const int EventButtonHoldMs = 250;
    public const int PolledButtonHoldMs = 2000;
    public const int SpringHoldMs = 1500;
    public const int MaxRpnLength = 900;

    public const string UnknownPositionRefusal = "position unknown, try again in a moment";
    public const string NotAPositionRefusal = "not a position of this control";
    public const string NotSettableRefusal = "cannot be set from this panel";

    public static L1011Plan ForSet(L1011Control control, double target, double? current)
    {
        switch (control.Kind)
        {
            case L1011Kinds.Switch:
            case L1011Kinds.Spring:
            {
                var transition = control.TransitionFor(target);
                if (transition == null)
                    return L1011Plan.Refused(NotAPositionRefusal);
                if (current is double now && Math.Abs(now - target) < SameValueTolerance)
                    return L1011Plan.AlreadyThere;
                var effects = transition.Select(L1011Effect.Parse).ToList();
                if (current == null && effects.Any(e => e.IsToggleEvent))
                    return L1011Plan.Refused(UnknownPositionRefusal);
                var steps = new List<L1011Step>();
                steps.AddRange(Batches(effects, target));
                if (control.Kind == L1011Kinds.Spring && control.Rest is double rest
                    && Math.Abs(rest - target) >= SameValueTolerance
                    && control.TransitionFor(rest) is { } back)
                {
                    steps.Add(new L1011DelayStep(SpringHoldMs));
                    steps.AddRange(Batches(back.Select(L1011Effect.Parse), rest));
                }
                return new L1011Plan(steps, null);
            }
            case L1011Kinds.Knob:
            {
                double lo = control.Range is { Length: 2 } r ? r[0] : 0;
                double hi = control.Range is { Length: 2 } r2 ? r2[1] : 100;
                double value = Math.Clamp(target, lo, hi);
                if (current is double now && Math.Abs(now - value) < SameValueTolerance)
                    return L1011Plan.AlreadyThere;
                if (control.SetTemplate.Count == 0)
                    return L1011Plan.Refused(NotSettableRefusal);
                return new L1011Plan(Batches(control.SetTemplate.Select(L1011Effect.Parse), value).ToList(), null);
            }
            default:
                return L1011Plan.Refused(NotSettableRefusal);
        }
    }

    public static L1011Plan ForPress(L1011Control control, int? holdOverrideMs = null)
    {
        if (control.Kind != L1011Kinds.Button && control.Kind != L1011Kinds.Latch)
            return L1011Plan.Refused(NotSettableRefusal);
        if (control.Press.Count == 0)
            return L1011Plan.Refused(NotSettableRefusal);
        var steps = new List<L1011Step>(Batches(control.Press.Select(L1011Effect.Parse), null));
        if (control.Kind == L1011Kinds.Button && control.Release.Count > 0)
        {
            steps.Add(new L1011DelayStep(holdOverrideMs is > 0 ? holdOverrideMs.Value : HoldFor(control)));
            steps.AddRange(Batches(control.Release.Select(L1011Effect.Parse), null));
        }
        return new L1011Plan(steps, null);
    }

    public static L1011Plan ForStep(L1011Control control, bool increase)
    {
        var list = increase ? control.Inc : control.Dec;
        if (control.Kind != L1011Kinds.Encoder || list.Count == 0)
            return L1011Plan.Refused(NotSettableRefusal);
        return new L1011Plan(Batches(list.Select(L1011Effect.Parse), null).ToList(), null);
    }

    /// <summary>Pull (true) or push in (false) one circuit breaker: the cockpit's own click for that position.</summary>
    public static L1011Plan ForBreaker(L1011Breaker breaker, bool pull)
    {
        if (!breaker.Transitions.TryGetValue(pull ? "1" : "0", out var effects) || effects.Count == 0)
            return L1011Plan.Refused(NotSettableRefusal);
        return new L1011Plan(Batches(effects.Select(L1011Effect.Parse), null).ToList(), null);
    }

    /// <summary>How long a button is held down before its release effects.</summary>
    public static int HoldFor(L1011Control control) =>
        control.Press.Any(p => p.StartsWith("H:", StringComparison.Ordinal) || p.StartsWith("K:", StringComparison.Ordinal))
            ? EventButtonHoldMs
            : PolledButtonHoldMs;

    internal static IEnumerable<L1011CalcStep> Batches(IEnumerable<L1011Effect> effects, double? target)
    {
        var current = new List<string>();
        int length = 0;
        foreach (var effect in effects)
        {
            string rpn = effect.ToRpn(target);
            if (current.Count > 0 && length + 1 + rpn.Length > MaxRpnLength)
            {
                yield return new L1011CalcStep(string.Join(' ', current));
                current.Clear();
                length = 0;
            }
            current.Add(rpn);
            length += (length == 0 ? 0 : 1) + rpn.Length;
        }
        if (current.Count > 0)
            yield return new L1011CalcStep(string.Join(' ', current));
    }
}
