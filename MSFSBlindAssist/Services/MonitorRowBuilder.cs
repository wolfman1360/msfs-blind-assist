using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Services;

/// <summary>
/// Builds the row list every Monitor Manager dialog (Ctrl+M) shows, from an aircraft's
/// variable definitions. Replaces the five hand-rolled copies of this loop that had drifted
/// across the per-aircraft forms.
///
/// A variable is listed when it is Continuous AND IsAnnounced AND not
/// ExcludeFromMonitorManager. Those first two together are what puts a variable on the
/// auto-announce path; the third is the opt-out for variables that ride the continuous
/// stream for plumbing reasons but are never spoken individually (silent caches, detail vars
/// whose speech rides another entry) — a row for those would be a checkbox that does nothing.
/// </summary>
public static class MonitorRowBuilder
{
    public static List<MonitorRow> Build(IReadOnlyDictionary<string, SimVarDefinition> variables)
    {
        var rows = new List<MonitorRow>();
        foreach (var kv in variables)
        {
            if (!IsListed(kv.Value)) continue;
            rows.Add(new MonitorRow(kv.Key, LabelFor(kv.Key, kv.Value)));
        }

        SortRows(rows);
        return rows;
    }

    /// <summary>
    /// Sort by what the pilot actually reads. Ties break on the key: List.Sort is unstable
    /// and a Dictionary has no guaranteed enumeration order, so without a tie-break two
    /// same-labelled rows could swap places between runs.
    /// </summary>
    private static void SortRows(List<MonitorRow> rows)
        => rows.Sort((a, b) =>
        {
            int byLabel = string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase);
            return byLabel != 0 ? byLabel : string.CompareOrdinal(a.Key, b.Key);
        });

    /// <summary>
    /// The standard row build plus rows for call-outs that have no SimVar behind them (the TriStar's
    /// INS window speaks a new leg, a PMS page title and "Entry closed" from its own form, never from a
    /// variable update). Like the A380's fold row they are synthetic: the key goes into the aircraft's
    /// disabled list and whoever speaks the call-out checks that list. Unlike the fold row they are
    /// sorted in with the rest, so a pilot searching or arrowing finds them by name. An extra row whose
    /// key a listed variable already has is not added a second time.
    /// </summary>
    public static List<MonitorRow> BuildWithExtra(IReadOnlyDictionary<string, SimVarDefinition> variables,
                                                  IEnumerable<MonitorRow> extra)
    {
        var rows = Build(variables);
        var known = new HashSet<string>(rows.Select(r => r.Key), StringComparer.Ordinal);
        foreach (var row in extra)
            if (known.Add(row.Key))
                rows.Add(row);
        SortRows(rows);
        return rows;
    }

    /// <summary>
    /// The standard row build with one FAMILY of variables collapsed behind a single synthetic
    /// row: every key starting with <paramref name="foldPrefix"/> is left out of the list, and
    /// if at least one of them would otherwise have been listed, one row carrying
    /// <paramref name="foldKey"/> / <paramref name="foldLabel"/> is appended in their place.
    ///
    /// The A380's 20 E/WD line variables are the only user today. They are real announcements
    /// (so NOT excluded via ExcludeFromMonitorManager) but they are one logical feature, and 20
    /// rows for a single on/off decision is noise.
    ///
    /// Two details the A380 has always depended on, now pinned by tests rather than by luck:
    /// the synthetic row is appended AFTER the sort, so it lands LAST rather than in
    /// alphabetical position; and it appears only when a folded variable WOULD have been
    /// listed — folding away a family that is entirely muted-by-plumbing (not announced, or
    /// ExcludeFromMonitorManager) would leave the pilot a checkbox that silences nothing.
    /// </summary>
    public static List<MonitorRow> BuildWithFold(IReadOnlyDictionary<string, SimVarDefinition> variables,
                                                 string foldPrefix, string foldKey, string foldLabel)
    {
        var kept = new Dictionary<string, SimVarDefinition>(variables.Count);
        bool anyFolded = false;

        foreach (var kv in variables)
        {
            if (kv.Key.StartsWith(foldPrefix, StringComparison.Ordinal))
            {
                if (IsListed(kv.Value)) anyFolded = true;
                continue;
            }
            kept[kv.Key] = kv.Value;
        }

        var rows = Build(kept);
        if (anyFolded) rows.Add(new MonitorRow(foldKey, foldLabel));
        return rows;
    }

    /// <summary>The three inclusion rules, in one place so <see cref="Build"/> and
    /// <see cref="BuildWithFold"/>'s fold test can never drift apart.</summary>
    private static bool IsListed(SimVarDefinition def)
        => def.UpdateFrequency == UpdateFrequency.Continuous
           && def.IsAnnounced
           && !def.ExcludeFromMonitorManager;

    /// <summary>The label for a row: the definition's DisplayName, or the raw key when it has
    /// none — which is also what keeps raw keys findable through the search box.</summary>
    public static string LabelFor(string key, SimVarDefinition def)
        => string.IsNullOrEmpty(def.DisplayName) ? key : def.DisplayName;
}
