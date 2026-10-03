using System.Globalization;

namespace MSFSBlindAssist.Aircraft.L1011;

public enum L1011EffectKind { LVar, AVar, HEvent, KEvent }

/// <summary>
/// One entry of a control-map effect list, parsed from the generator's text form and rendered
/// back as an RPN fragment for the MobiFlight calculator path. Text forms (Task 3 contract):
/// <c>L:NAME=VALUE</c>, <c>L:NAME, UNIT=VALUE</c>, <c>A:NAME, UNIT=VALUE</c>, <c>H:NAME</c>,
/// <c>K:NAME</c>, <c>K:NAME=P</c>, <c>K:2:NAME=P1,P2</c>. A VALUE may be <c>{v}</c>, the target.
/// </summary>
public sealed record L1011Effect(L1011EffectKind Kind, string Name, string? Unit, string Value)
{
    /// <summary>The target-value placeholder knob templates use.</summary>
    public const string TargetPlaceholder = "{v}";

    /// <summary>A number as RPN accepts it: invariant culture, no exponent ("1", "0.5", "100").</summary>
    public static string FormatNumber(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>True for a K: event that flips stock state rather than setting it, so replaying it
    /// when the control is already in position would undo the pilot's intent.</summary>
    public bool IsToggleEvent =>
        Kind == L1011EffectKind.KEvent && Name.Contains("TOGGLE", StringComparison.OrdinalIgnoreCase);

    public static L1011Effect Parse(string text)
    {
        if (text.Length < 3 || text[1] != ':')
            throw new FormatException($"Not an L-1011 effect: '{text}'");
        string body = text.Substring(2);
        switch (char.ToUpperInvariant(text[0]))
        {
            case 'H':
                return new L1011Effect(L1011EffectKind.HEvent, body, null, string.Empty);
            case 'K':
            {
                int eq = body.IndexOf('=');
                return eq < 0
                    ? new L1011Effect(L1011EffectKind.KEvent, body, null, string.Empty)
                    : new L1011Effect(L1011EffectKind.KEvent, body.Substring(0, eq), null, body.Substring(eq + 1));
            }
            case 'L':
            case 'A':
            {
                int eq = body.LastIndexOf('=');
                if (eq < 0)
                    throw new FormatException($"Variable effect without a value: '{text}'");
                string head = body.Substring(0, eq);
                string value = body.Substring(eq + 1);
                string? unit = null;
                int comma = head.IndexOf(',');
                if (comma >= 0)
                {
                    unit = head.Substring(comma + 1).Trim();
                    head = head.Substring(0, comma).Trim();
                }
                var kind = char.ToUpperInvariant(text[0]) == 'L' ? L1011EffectKind.LVar : L1011EffectKind.AVar;
                return new L1011Effect(kind, head, unit, value);
            }
            default:
                throw new FormatException($"Unknown effect kind in '{text}'");
        }
    }

    /// <summary>The RPN fragment for this effect, with <c>{v}</c> replaced by <paramref name="target"/>.</summary>
    public string ToRpn(double? target = null)
    {
        string value = Value == TargetPlaceholder
            ? FormatNumber(target ?? throw new InvalidOperationException("A {v} effect needs a target value."))
            : Value;
        return Kind switch
        {
            L1011EffectKind.HEvent => $"(>H:{Name})",
            L1011EffectKind.KEvent => Value.Length == 0
                ? $"(>K:{Name})"
                : $"{string.Join(' ', value.Split(','))} (>K:{Name})",
            L1011EffectKind.LVar => Unit == null ? $"{value} (>L:{Name})" : $"{value} (>L:{Name}, {Unit})",
            _ => $"{value} (>A:{Name}, {Unit ?? "number"})",
        };
    }
}
