using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Tests.L1011;

/// <summary>
/// What the INS and PMS do and say: typed keys, the present-position entry, automatic waypoint loading,
/// the panel buttons that open the INS window, the window's selectors, and the two variables that speak
/// on their own — against a SimConnectManager that never connected (nothing can be sent) and a fake clock.
/// </summary>
public class IniL1011InsBehaviourTests
{
    private long _now = 10_000;
    private readonly UserSettings _settings = new();
    private readonly IniL1011Definition _def;
    private readonly SpeechCapture _speech = new();
    private readonly SimConnectManager _sim = new(IntPtr.Zero);   // never connected

    public IniL1011InsBehaviourTests()
    {
        _def = new IniL1011Definition { Clock = () => _now, SettingsSource = () => _settings };
    }

    private void Deliver(string key, double value) => _def.ProcessSimVarUpdate(key, value, _speech);

    [Fact]
    public void A_typing_slip_is_named_and_sends_nothing()
    {
        Assert.False(_def.TypeKeys(L1011InsDevice.Ins1, "N36X", insert: true, _sim, _speech));
        Assert.False(_def.TypeKeys(L1011InsDevice.CaptainPms, "-5", insert: true, _sim, _speech));
        Assert.Equal(new[]
        {
            "INS 1: " + L1011InsDevices.InsKeysError,
            "Captain PMS: " + L1011InsDevices.PmsKeysError,
        }, _speech.All);
    }

    [Fact]
    public void Keys_refuse_aloud_when_they_cannot_land()
    {
        Assert.False(_def.TypeKeys(L1011InsDevice.Ins2, "N36051", insert: true, _sim, _speech));
        Assert.False(_def.PressKeys(L1011InsDevice.FirstOfficerPms, new[] { "PMS_FO_FWD" }, _sim, _speech));
        Assert.Equal(new[] { "INS 2 keypad unavailable", "First officer PMS keypad unavailable" }, _speech.All);
    }

    [Fact]
    public void Nothing_typed_presses_nothing_and_says_nothing()
    {
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "", insert: false, _sim, _speech));
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_key_the_map_lacks_is_refused_by_name()
    {
        Assert.False(_def.PressKeys(L1011InsDevice.Ins1, new[] { "INS_1_KEY_Q" }, _sim, _speech));
        Assert.Equal(new[] { "INS 1 keypad: INS_1_KEY_Q is not a control of this aircraft" }, _speech.All);
    }

    [Fact]
    public void The_present_position_entry_and_automatic_loading_refuse_aloud_when_they_cannot_land()
    {
        Assert.False(_def.EnterPresentPosition(3, 36.0854, -115.1336, _sim, _speech));
        Assert.False(_def.SetAutoBatch(1, true, _sim, _speech));
        Assert.Equal(new[] { "INS 3 keypad unavailable", "INS 1 automatic waypoint loading unavailable" }, _speech.All);
    }

    [Fact]
    public void A_present_position_that_cannot_be_keyed_is_refused_aloud_and_nothing_is_chained()
    {
        // Task 2's PresentPositionControls does no range check: infinity gives a null key, and a latitude
        // past 90 keys digits the unit would take as another figure. The caller refuses before any key.
        _def.CanLand = _ => true;   // so the position, not the calculator path, is the only reason
        Assert.False(_def.EnterPresentPosition(2, double.NaN, -115.1336, _sim, _speech));
        Assert.False(_def.EnterPresentPosition(2, 36.0854, double.PositiveInfinity, _sim, _speech));
        Assert.False(_def.EnterPresentPosition(2, 91, -115.1336, _sim, _speech));
        Assert.False(_def.EnterPresentPosition(2, 36.0854, -180.5, _sim, _speech));
        Assert.Equal(Enumerable.Repeat("INS 2: present position unavailable", 4), _speech.All);
        Assert.Equal(0, _def.KeysBusyForMs());
    }

    /// <summary>Puts the unit's display selector somewhere the definition knows: a pick through its panel row,
    /// which the definition trusts for L1011CommandedState.HoldMs over the 1 Hz cache (the never-connected
    /// manager has no cache), the way a pilot's own pick would be.</summary>
    private void SelectorIsAt(int unit, double position)
    {
        string key = $"INS_{unit}_ROTARY_MODE";
        Assert.True(_def.HandleUIVariableSet(key, position, _def.GetVariables()[key], _sim, _speech));
        Assert.Empty(_speech.All);   // a pick that can land is silent
    }

    private static readonly int FifteenKeysMs = 15 * (IniL1011Definition.KeyHoldMs + IniL1011Definition.KeyGapMs);

    private const string PresentPositionWords = "INS 2: entering present position N 36 05.1, W 115 08.0";

    [Fact]
    public void The_present_position_entry_turns_the_selector_to_position_then_keys_the_position()
    {
        // [L1011-14]: on any other page the N/S/E/W keys start no entry, so the selector moves first, the
        // gauge gets SelectorSettleMs to draw the page, then the 15 keys follow in the same chain.
        _def.CanLand = _ => true;
        SelectorIsAt(2, 5);
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));
        Assert.Equal(IniL1011Definition.SelectorSettleMs + FifteenKeysMs, _def.KeysBusyForMs());
        Assert.Equal(new[] { PresentPositionWords }, _speech.All);
    }

    [Fact]
    public void The_present_position_entry_chains_no_settle_when_the_selector_is_already_at_position()
    {
        _def.CanLand = _ => true;
        SelectorIsAt(2, 3);
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));
        Assert.Equal(FifteenKeysMs, _def.KeysBusyForMs());
        Assert.Equal(new[] { PresentPositionWords }, _speech.All);
    }

    [Fact]
    public void The_present_position_sentence_is_the_same_under_a_comma_decimal_culture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("sv-SE");
            _def.CanLand = _ => true;
            SelectorIsAt(2, 5);
            Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));
            Assert.Equal(new[] { PresentPositionWords }, _speech.All);
            Assert.Equal(IniL1011Definition.SelectorSettleMs + FifteenKeysMs, _def.KeysBusyForMs());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void Every_control_a_present_position_can_press_is_a_button_in_the_map()
    {
        // Digits 0-9 and INSERT, on all three units: the entry's whole key set, swept over many places so a
        // key the sequence can reach but this list lacks fails here, not in the sim.
        var wanted = new HashSet<string>();
        foreach (int unit in new[] { 1, 2, 3 })
        {
            for (char digit = '0'; digit <= '9'; digit++)
                wanted.Add($"INS_{unit}_KEY_{digit}");
            wanted.Add($"INS_{unit}_KEY_INSERT");
        }
        var emitted = new HashSet<string>();
        var random = new Random(1011);
        foreach (int unit in new[] { 1, 2, 3 })
            for (int i = 0; i < 2_000; i++)
                foreach (var id in L1011InsKeys.PresentPositionControls(unit, random.NextDouble() * 180 - 90, random.NextDouble() * 360 - 180))
                    emitted.Add(id);
        Assert.Equal(wanted.OrderBy(id => id), emitted.OrderBy(id => id));

        var map = L1011ControlMap.Load();
        foreach (var id in wanted)
        {
            var control = map.Find(id);
            Assert.True(control != null, $"{id} is not in the control map");
            Assert.Equal(L1011Kinds.Button, control!.Kind);
        }
    }

    [Fact]
    public void Chained_keys_say_how_long_they_still_take()
    {
        // The window holds its read-back of what was typed until the keys have landed (the A300's McduKeysPending).
        _def.CanLand = _ => true;
        Assert.Equal(0, _def.KeysBusyForMs());
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "2", insert: false, _sim, _speech));
        int oneKey = _def.KeysBusyForMs();
        Assert.Equal(IniL1011Definition.KeyHoldMs + IniL1011Definition.KeyGapMs, oneKey);
        _now += 30;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "3", insert: false, _sim, _speech));
        Assert.Equal(2 * oneKey - 30, _def.KeysBusyForMs());   // queued behind the first
        _now += 10_000;
        Assert.Equal(0, _def.KeysBusyForMs());
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void A_panel_button_opens_the_window_on_its_unit()
    {
        var opened = new List<L1011InsDevice?>();
        _def.OpenInsWindow = d => opened.Add(d);
        string key = L1011InsDevices.WindowKey(L1011InsDevice.FirstOfficerPms);
        Assert.True(_def.HandleUIVariableSet(key, 1, _def.GetVariables()[key], _sim, _speech));
        Assert.Equal(new L1011InsDevice?[] { L1011InsDevice.FirstOfficerPms }, opened);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void The_window_selectors_are_the_panel_rows()
    {
        var positions = _def.InsSelectorPositions("INS_2_ROTARY_MODE");
        Assert.Equal(9, positions.Count);
        Assert.Equal("Position", positions[3]);
        Assert.Empty(_def.InsSelectorPositions("NOT_A_ROW"));
        Assert.Null(_def.InsSelectorPosition("INS_2_ROTARY_MODE", _sim));   // nothing reported yet
        // A pick goes through the row's write path, refused aloud as any panel row is when it cannot land.
        _def.SetInsSelector("INS_2_ROTARY_MODE", 3, _sim, _speech);
        Assert.Equal(new[] { "Display selector unavailable" }, _speech.All);
    }

    [Fact]
    public async Task A_selector_pick_waits_for_the_keys_queued_before_it()
    {
        // A pick made from the window while typed keys are still being sent must not land between two key
        // presses: it runs behind the chain, through the row's own write path. The gate holds the chain at
        // its first wait, so "before" and "after" are exact, not timed.
        var sent = new List<string>();
        var gate = new TaskCompletionSource();
        _def.SendCalc = (_, rpn) => sent.Add(rpn);
        _def.StepDelay = _ => gate.Task;
        _def.CanLand = _ => true;

        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "2", insert: false, _sim, _speech));
        Assert.True(_def.KeysBusyForMs() > 0);
        int keysSentSoFar = sent.Count;
        Assert.True(keysSentSoFar > 0);

        _def.SetInsSelector("INS_2_ROTARY_MODE", 5, _sim, _speech);
        Assert.Equal(keysSentSoFar, sent.Count);   // nothing of the pick yet
        Assert.DoesNotContain(sent, rpn => rpn.Contains("INS_2_ROTARY_MODE", StringComparison.Ordinal));

        gate.SetResult();
        await _def.KeysDone;

        Assert.All(sent.Take(sent.Count - 1), rpn => Assert.Contains("INS_2_KEY_2", rpn));   // every key first
        Assert.Contains("INS_2_ROTARY_MODE", sent[^1]);                                       // then the pick
        Assert.Empty(_speech.All);
    }

    [Fact]
    public async Task A_selector_pick_with_no_keys_queued_is_written_at_once()
    {
        var sent = new List<string>();
        _def.SendCalc = (_, rpn) => sent.Add(rpn);
        _def.CanLand = _ => true;
        Assert.Equal(0, _def.KeysBusyForMs());

        _def.SetInsSelector("INS_2_ROTARY_MODE", 5, _sim, _speech);

        Assert.Single(sent);   // no await needed: the chain was idle
        Assert.Contains("INS_2_ROTARY_MODE", sent[0]);
        await _def.KeysDone;
        Assert.Single(sent);
    }

    [Fact]
    public async Task A_queued_selector_pick_keeps_the_rows_own_refusal_and_speaks_it_when_it_runs()
    {
        var gate = new TaskCompletionSource();
        _def.SendCalc = (_, _) => { };
        _def.StepDelay = _ => gate.Task;
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "3", insert: false, _sim, _speech));

        _def.CanLand = _ => false;   // the write cannot land when the pick's turn comes
        _def.SetInsSelector("INS_1_ROTARY_MODE", 3, _sim, _speech);
        Assert.Empty(_speech.All);   // not before the keys

        gate.SetResult();
        await _def.KeysDone;
        Assert.Equal(new[] { "Display selector unavailable" }, _speech.All);
    }

    [Fact]
    public async Task Keys_chained_after_a_queued_pick_run_behind_it()
    {
        var sent = new List<string>();
        var gate = new TaskCompletionSource();
        _def.SendCalc = (_, rpn) => sent.Add(rpn);
        _def.StepDelay = _ => gate.Task;
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "2", insert: false, _sim, _speech));
        _def.SetInsSelector("INS_2_ROTARY_MODE", 5, _sim, _speech);
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "4", insert: false, _sim, _speech));

        gate.SetResult();
        await _def.KeysDone;

        int pick = sent.FindIndex(rpn => rpn.Contains("INS_2_ROTARY_MODE", StringComparison.Ordinal));
        Assert.True(pick > 0);
        Assert.DoesNotContain(sent.Take(pick), rpn => rpn.Contains("KEY_4", StringComparison.Ordinal));
        Assert.Contains(sent.Skip(pick + 1), rpn => rpn.Contains("KEY_4", StringComparison.Ordinal));
    }

    [Fact]
    public void Alignment_and_the_pms_phase_are_announced_and_muteable()
    {
        var vars = _def.GetVariables();
        for (int unit = 1; unit <= 3; unit++)
        {
            var status = vars[L1011InsAlignmentAnnouncer.StatusKey(unit)];
            Assert.Equal($"INS {unit} alignment", status.DisplayName);
            Assert.True(status.IsAnnounced);
            Assert.False(status.ExcludeFromMonitorManager);
            Assert.True(ContinuousBatchLayout.RidesBatch(status));
        }
        var phase = vars[L1011PmsPhaseAnnouncer.Key];
        Assert.Equal("PMS flight phase", phase.DisplayName);
        Assert.True(phase.IsAnnounced);
        Assert.True(ContinuousBatchLayout.RidesBatch(phase));
    }

    [Fact]
    public void Navigation_starting_by_itself_and_the_pms_phase_are_spoken()
    {
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 90);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 33.2);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 1);
        Deliver(L1011PmsPhaseAnnouncer.Key, 1);
        Deliver(L1011PmsPhaseAnnouncer.Key, 3);
        Assert.Equal(new[] { "INS 1 navigating", "PMS climb" }, _speech.All);
    }

    [Fact]
    public void A_context_reset_makes_the_next_value_a_baseline_again()
    {
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(2), 50);
        Deliver(L1011PmsPhaseAnnouncer.Key, 3);
        _def.OnSimContextReset();
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(2), 1);
        Deliver(L1011PmsPhaseAnnouncer.Key, 4);
        Assert.Empty(_speech.All);
    }
}
