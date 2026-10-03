using System.Text;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>
/// The TriStar's 981 circuit breakers (numbered 1 to 999; 644 named by iniBuilds). They are never registered as
/// SimConnect data definitions: 981 more would break the 1,000-per-connection budget. The
/// breaker window reads them all at once with one Coherent debugger evaluation in the hidden
/// flight-engineer logic view (no MSFSBA client holds that view), and pulls or pushes one by
/// replaying the cockpit's own click through the calculator path. 1 = pulled, 0 = pushed in.
/// </summary>
public static class L1011CircuitBreakers
{
    /// <summary>Title needle of the hidden flight-engineer logic gauge (panel.cfg VCockpit04).</summary>
    public const string ViewNeedle = "L1011_ENGINEER_PANEL";

    public static string DisplayName(L1011Breaker breaker) =>
        string.IsNullOrWhiteSpace(breaker.Title) ? $"Breaker {breaker.Index}" : breaker.Title.Trim();

    public static string ItemText(L1011Breaker breaker, bool? pulled) =>
        $"{DisplayName(breaker)}: {(pulled == null ? "unknown" : pulled.Value ? "pulled" : "in")}";

    /// <summary>ES5 (Coherent GT is Chromium-49 class): one '1' or '0' per breaker, in list order.</summary>
    public static string BulkReadScript(IReadOnlyList<L1011Breaker> breakers)
    {
        var sb = new StringBuilder("(function(){var n=[");
        for (int i = 0; i < breakers.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('"').Append(StateName(breakers[i])).Append('"');
        }
        sb.Append("];var s='';for(var i=0;i<n.length;i++){s+=SimVar.GetSimVarValue('L:'+n[i],'number')>0.5?'1':'0';}return s;})()");
        return sb.ToString();
    }

    /// <summary>The evaluation result as pulled flags, or null when it is not exactly one flag per breaker.</summary>
    public static bool[]? ParseStates(string? result, int count)
    {
        if (result == null || result.Length != count || result.Any(c => c != '0' && c != '1'))
            return null;
        return result.Select(c => c == '1').ToArray();
    }

    /// <summary>
    /// The indices of the breakers the window lists: whose name contains <paramref name="search"/>
    /// (ignoring case; ordinal, so a Turkish locale cannot break it) and, when
    /// <paramref name="pulledOnly"/>, that read as pulled. With no states read, "pulled only" lists none.
    /// </summary>
    public static IReadOnlyList<int> Visible(IReadOnlyList<L1011Breaker> breakers, bool[]? states,
        string? search, bool pulledOnly)
    {
        var result = new List<int>();
        string needle = (search ?? string.Empty).Trim();
        for (int i = 0; i < breakers.Count; i++)
        {
            if (needle.Length > 0 && DisplayName(breakers[i]).IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            if (pulledOnly && (states == null || !states[i]))
                continue;
            result.Add(i);
        }
        return result;
    }

    /// <summary>Summary line: "981 breakers, all in" or "981 breakers, 2 pulled".</summary>
    public static string Summary(int total, int pulled) =>
        pulled == 0 ? $"{total} breakers, all in" : $"{total} breakers, {pulled} pulled";

    private static string StateName(L1011Breaker breaker) =>
        string.IsNullOrEmpty(breaker.StateVar) ? $"V_C_Breaker_{breaker.Index:D3}" : breaker.StateVar.Substring(2);
}
