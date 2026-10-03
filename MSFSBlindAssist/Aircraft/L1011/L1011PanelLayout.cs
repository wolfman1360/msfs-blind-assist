using System.Globalization;
using System.Text;

namespace MSFSBlindAssist.Aircraft.L1011;

/// <summary>One control placed on a panel.</summary>
/// <param name="Id">A control id from the map, or a hand-written key from <see cref="L1011Levers"/>.</param>
/// <param name="Name">What the screen reader says for the row. Must be unique within its panel.</param>
/// <param name="Positions">Position words that replace or fill in iniBuilds' tooltip words, written
/// <c>"0=Off;1=Arm;2=On"</c>. Only the listed positions change. Used where the aircraft has no word,
/// a wrong word, or two positions with the same word.</param>
/// <param name="HoldMs">Button hold override in milliseconds; 0 keeps <see cref="L1011WritePlan.HoldFor"/>.</param>
public sealed record L1011Row(string Id, string Name, string? Positions = null, int HoldMs = 0);

public sealed record L1011LayoutPanel(string Name, IReadOnlyList<L1011Row> Rows);

public sealed record L1011LayoutSection(string Name, IReadOnlyList<L1011LayoutPanel> Panels);

/// <summary>What a panel row does when the pilot operates it.</summary>
public enum L1011RowAction { Set, Press, Increase, Decrease, Custom }

/// <summary>A row after placement: the variable key MSFSBA registers, and what it drives.</summary>
public sealed record L1011PlacedRow(string Key, string Name, L1011RowAction Action, L1011Control? Control,
    IReadOnlyDictionary<double, string> Positions, int HoldMs);

public sealed class L1011Placement
{
    /// <summary>Section → panel names, in checklist order.</summary>
    public Dictionary<string, List<string>> Structure { get; } = new();
    /// <summary>Panel name → placed rows, in layout order.</summary>
    public Dictionary<string, List<L1011PlacedRow>> RowsByPanel { get; } = new();
    /// <summary>Layout ids that are neither in the map nor a hand-written key.</summary>
    public List<string> MissingIds { get; } = new();
    /// <summary>Layout ids the map marks as not replayable.</summary>
    public List<string> UnreplayableIds { get; } = new();
}

/// <summary>
/// The TriStar's panels in preflight/checklist order (design doc section 5.6, manual section 7):
/// the flight engineer's station first (cockpit safety inspection, then station preparation),
/// then the overhead, the main instrument panel and the center console. Rows inside a panel run in
/// the order the flows call for them, and a guard always sits directly before the switch it covers.
///
/// Names are what a pilot says, with the discriminator first ("Engine 1 fire handle", "Tank 2 left
/// pump 1"), because iniBuilds' titles are shared by whole families ("TANK PUMP SWITCH" ×8).
/// Position words come from the aircraft's tooltips; a row's <see cref="L1011Row.Positions"/>
/// supplies the few the aircraft lacks. Position words not yet checked live are tracked in the
/// "Verify in the simulator" list of docs/l1011.md.
///
/// The AFCS glareshield panel, the INS/PMS keypads and the EFB are placed by later sessions.
/// </summary>
public static partial class L1011PanelLayout
{
    private static IReadOnlyList<L1011LayoutSection>? _sections;

    public static IReadOnlyList<L1011LayoutSection> Sections => _sections ??= new[]
    {
        new L1011LayoutSection("Engineer Station", EngineerPanels()),
        new L1011LayoutSection("Overhead", OverheadPanels()),
        new L1011LayoutSection("Main Panel", MainPanels()),
        new L1011LayoutSection("Center Console", ConsolePanels()),
    };

    internal static L1011Row R(string id, string name, string? positions = null, int holdMs = 0) =>
        new(id, name, positions, holdMs);

    internal static L1011LayoutPanel P(string name, params L1011Row[] rows) => new(name, rows);

    /// <summary>Parses <c>"0=Off;1=Arm;2=On"</c>.</summary>
    public static IReadOnlyDictionary<double, string> ParsePositions(string? spec)
    {
        var result = new Dictionary<double, string>();
        if (string.IsNullOrWhiteSpace(spec))
            return result;
        foreach (var part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = part.IndexOf('=');
            result[double.Parse(part.Substring(0, eq), NumberStyles.Float, CultureInfo.InvariantCulture)] = part.Substring(eq + 1).Trim();
        }
        return result;
    }

    /// <summary>Abbreviations pilots say as letters, from the tooltip words of the rows placed through <see cref="SpokenWord"/>.</summary>
    private static readonly HashSet<string> SpokenAsLetters = new(StringComparer.OrdinalIgnoreCase) { "APU", "DC", "RA", "TA" };

    /// <summary>
    /// Spoken form of one of iniBuilds' upper-case tooltip words: sentence case ("ALTITUDE MODE ON" →
    /// "Altitude mode on"), except that a single letter ("Loop A"), a token with a digit ("1A", "N1")
    /// and an abbreviation said as letters ("TA/RA", "APU generator") keep their capitals. Tokens are
    /// separated by spaces, '/', '-', '(' and ')', which are kept as they were.
    /// </summary>
    public static string SpokenWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;
        string trimmed = word.Trim();
        var spoken = new StringBuilder(trimmed.Length);
        bool first = true;
        int i = 0;
        while (i < trimmed.Length)
        {
            if (IsWordSeparator(trimmed[i]))
            {
                spoken.Append(trimmed[i++]);
                continue;
            }
            int start = i;
            while (i < trimmed.Length && !IsWordSeparator(trimmed[i]))
                i++;
            spoken.Append(SpokenToken(trimmed.Substring(start, i - start), first));
            first = false;
        }
        return spoken.ToString();
    }

    private static bool IsWordSeparator(char c) => c is ' ' or '/' or '-' or '(' or ')';

    private static string SpokenToken(string token, bool first)
    {
        if (token.Any(char.IsAsciiDigit))
            return token;
        if ((token.Length == 1 && char.IsLetter(token[0])) || SpokenAsLetters.Contains(token))
            return token.ToUpperInvariant();
        string lower = token.ToLowerInvariant();
        return first ? char.ToUpperInvariant(lower[0]) + lower.Substring(1) : lower;
    }

    /// <summary>
    /// Places the layout over the map. Encoders become two rows ("… increase", "… decrease");
    /// hand-written keys (<paramref name="customKeys"/>) pass through as <see cref="L1011RowAction.Custom"/>.
    /// </summary>
    public static L1011Placement Place(L1011ControlMap map, IReadOnlySet<string> customKeys)
    {
        var placement = new L1011Placement();
        foreach (var section in Sections)
        {
            var panelNames = new List<string>();
            placement.Structure[section.Name] = panelNames;
            foreach (var panel in section.Panels)
            {
                panelNames.Add(panel.Name);
                var rows = new List<L1011PlacedRow>();
                placement.RowsByPanel[panel.Name] = rows;
                foreach (var row in panel.Rows)
                    PlaceRow(map, customKeys, row, rows, placement);
            }
        }
        return placement;
    }

    private static void PlaceRow(L1011ControlMap map, IReadOnlySet<string> customKeys, L1011Row row,
        List<L1011PlacedRow> rows, L1011Placement placement)
    {
        var overrides = ParsePositions(row.Positions);
        if (customKeys.Contains(row.Id))
        {
            rows.Add(new L1011PlacedRow(row.Id, row.Name, L1011RowAction.Custom, null, overrides, row.HoldMs));
            return;
        }
        var control = map.Find(row.Id);
        if (control == null)
        {
            placement.MissingIds.Add(row.Id);
            return;
        }
        switch (control.Kind)
        {
            case L1011Kinds.Switch:
            case L1011Kinds.Spring:
            {
                var words = new Dictionary<double, string>();
                foreach (var (value, label) in control.OrderedPositions())
                    words[value] = overrides.TryGetValue(value, out var o) ? o : SpokenWord(label);
                rows.Add(new L1011PlacedRow(control.Id, row.Name, L1011RowAction.Set, control, words, row.HoldMs));
                break;
            }
            case L1011Kinds.Knob:
                rows.Add(new L1011PlacedRow(control.Id, row.Name, L1011RowAction.Set, control, overrides, row.HoldMs));
                break;
            case L1011Kinds.Button:
            case L1011Kinds.Latch:
                rows.Add(new L1011PlacedRow(control.Id, row.Name, L1011RowAction.Press, control, overrides, row.HoldMs));
                break;
            case L1011Kinds.Encoder:
                rows.Add(new L1011PlacedRow(control.Id + "#INC", row.Name + " increase", L1011RowAction.Increase, control, overrides, row.HoldMs));
                rows.Add(new L1011PlacedRow(control.Id + "#DEC", row.Name + " decrease", L1011RowAction.Decrease, control, overrides, row.HoldMs));
                break;
            default:
                placement.UnreplayableIds.Add(row.Id);
                break;
        }
    }
}
