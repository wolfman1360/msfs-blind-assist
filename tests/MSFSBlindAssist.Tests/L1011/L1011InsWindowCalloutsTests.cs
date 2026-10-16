using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Forms.L1011;
using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// The INS window's own call-outs have Ctrl+M rows: a new leg ("Leg 0 to 1"), a PMS's new page title and
/// "Entry closed" (the entry read-back's closing word; what was typed is always read back). None is a
/// variable: they are synthetic rows (the A380's ECAM fold is the precedent) whose keys go into
/// <see cref="UserSettings.L1011DisabledMonitorVariables"/> and are read by the window before it speaks.
/// All are on by default.
/// </summary>
public class L1011InsWindowCalloutsTests
{
    private readonly UserSettings _settings = new();
    private readonly IniL1011Definition _def;

    public L1011InsWindowCalloutsTests()
    {
        _def = new IniL1011Definition { SettingsSource = () => _settings };
    }

    private void Mute(params string[] keys)
    {
        _settings.L1011DisabledMonitorVariables = keys.ToList();
        _settings.RebuildDisabledMonitorVariableCaches();
    }

    [Fact]
    public void The_three_rows_have_clear_names()
    {
        Assert.Equal(new[]
        {
            (L1011InsWindowCallouts.LegKey, "INS window leg changes"),
            (L1011InsWindowCallouts.PmsPageKey, "INS window PMS page changes"),
            (L1011InsWindowCallouts.EntryClosedKey, "INS window entry closed"),
        }, L1011InsWindowCallouts.Rows);
    }

    [Fact]
    public void The_rows_are_in_the_monitor_manager_once_and_sorted_in_with_the_rest()
    {
        var rows = L1011MonitorManagerForm.BuildRows(_def.GetVariables());
        foreach (var (key, label) in L1011InsWindowCallouts.Rows)
            Assert.Single(rows, r => r.Key == key && r.Label == label);

        // The standard rows are all still there, and the list stays in label order.
        var standard = MonitorRowBuilder.Build(_def.GetVariables());
        Assert.Equal(standard.Count + 3, rows.Count);
        Assert.All(standard, s => Assert.Contains(s, rows));
        Assert.Equal(rows.OrderBy(r => r.Label, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Key, StringComparer.Ordinal),
            rows);
    }

    [Fact]
    public void The_rows_cost_no_batch_slot_and_no_on_request_definition()
    {
        // They are not variables at all: nothing is registered with SimConnect for them, and the definition's
        // variable table (which decides the batch layout and the budget) does not know their keys.
        var variables = _def.GetVariables();
        foreach (var (key, _) in L1011InsWindowCallouts.Rows)
        {
            Assert.DoesNotContain(key, variables.Keys);
            Assert.DoesNotContain(variables.Values, v => v.Name == key);
        }
    }

    [Fact]
    public void Every_call_out_speaks_by_default()
    {
        Assert.Empty(_settings.L1011DisabledMonitorVariablesSet);
        Assert.Equal("Leg 0 to 1", _def.InsWindowChange(pms: false, "Leg 0 to 1"));
        Assert.Equal("Takeoff", _def.InsWindowChange(pms: true, "Takeoff"));
        Assert.Equal("Entry closed", _def.InsWindowEntry("Entry closed"));
        Assert.Equal("Entry, left window: 36051N", _def.InsWindowEntry("Entry, left window: 36051N"));
    }

    [Fact]
    public void A_muted_leg_row_silences_the_leg_call_out_only()
    {
        Mute(L1011InsWindowCallouts.LegKey);
        Assert.Null(_def.InsWindowChange(pms: false, "Leg 1 to 2"));
        Assert.Equal("Cruise", _def.InsWindowChange(pms: true, "Cruise"));
        Assert.Equal("Entry closed", _def.InsWindowEntry("Entry closed"));
    }

    [Fact]
    public void A_muted_page_row_silences_the_pms_page_title_only()
    {
        Mute(L1011InsWindowCallouts.PmsPageKey);
        Assert.Null(_def.InsWindowChange(pms: true, "Cruise"));
        Assert.Equal("Leg 1 to 2", _def.InsWindowChange(pms: false, "Leg 1 to 2"));
        Assert.Equal("Entry closed", _def.InsWindowEntry("Entry closed"));
    }

    [Fact]
    public void A_muted_closed_row_silences_entry_closed_but_never_what_was_typed()
    {
        Mute(L1011InsWindowCallouts.EntryClosedKey);
        Assert.Null(_def.InsWindowEntry(L1011InsWindowCallouts.EntryClosedWords));
        Assert.Equal("Entry, left window: 36051N", _def.InsWindowEntry("Entry, left window: 36051N"));
        Assert.Equal("1: GMT, entering: 1200Z", _def.InsWindowEntry("1: GMT, entering: 1200Z"));
        Assert.Equal("Leg 1 to 2", _def.InsWindowChange(pms: false, "Leg 1 to 2"));
    }

    [Fact]
    public void Muting_all_three_leaves_the_typed_read_back_and_other_rows_alone()
    {
        Mute(L1011InsWindowCallouts.LegKey, L1011InsWindowCallouts.PmsPageKey, L1011InsWindowCallouts.EntryClosedKey,
            "LAMP_ENG_FIRE_1");
        Assert.Null(_def.InsWindowChange(pms: false, "Leg 1 to 2"));
        Assert.Null(_def.InsWindowChange(pms: true, "Climb"));
        Assert.Null(_def.InsWindowEntry("Entry closed"));
        Assert.Equal("Entry, left window: 36051N", _def.InsWindowEntry("Entry, left window: 36051N"));
    }

    [Fact]
    public void The_windows_closing_word_is_the_one_the_rows_mute()
    {
        Assert.Equal("Entry closed", L1011InsWindowCallouts.EntryClosedWords);
    }
}
