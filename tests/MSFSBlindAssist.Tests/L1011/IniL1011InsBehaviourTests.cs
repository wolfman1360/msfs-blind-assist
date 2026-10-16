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

    /// <summary>What the chain did, in order: "send:&lt;calculator string&gt;" and "wait:&lt;ms&gt;". Waits return at
    /// once, so a test sees the whole run; the busy estimate (<see cref="IniL1011Definition.KeysBusyForMs"/>) is
    /// computed from the steps, not from how long they took.</summary>
    private readonly List<string> _trace = new();

    public IniL1011InsBehaviourTests()
    {
        _def = new IniL1011Definition
        {
            Clock = () => _now,
            SettingsSource = () => _settings,
            SendCalc = (_, rpn) => _trace.Add("send:" + rpn),
            StepDelay = ms => { _trace.Add("wait:" + ms); return Task.CompletedTask; },
        };
    }

    private IEnumerable<string> Sent => _trace.Where(t => t.StartsWith("send:", StringComparison.Ordinal)).Select(t => t[5..]);

    // What one key's click reads in the calculator string, written out from the map's own press and release
    // (independent of the production packing): a digit key presses its pad and lets the push var go; INSERT sets
    // the push var, presses, and lets it go.
    private static string Digit(int unit, char digit) => $"(>H:PAD_INS_{unit}_KEY_{digit}) 0 (>L:INS_{unit}_KEY_{digit}_PUSH)";
    private static string Insert(int unit) => $"1 (>L:INS_{unit}_KEY_INSERT_PUSH) (>H:INS_{unit}_KEY_INSERT) 0 (>L:INS_{unit}_KEY_INSERT_PUSH)";
    private static string Join(params string[] clicks) => string.Join(' ', clicks);

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

    private const string PresentPositionWords = "INS 2: entering present position N 36 05.1, W 115 08.0";

    /// <summary>The 15 keys of unit 2's present position (lat 36.08540568, lon -115.13363512): N (the 2 key), 3 6 0 5 1,
    /// INSERT, W (the 4 key), 1 1 5 0 8 0, INSERT, in ONE calculator string: 744 characters, under the limit.</summary>
    private static readonly string PresentPositionString = Join(
        Digit(2, '2'), Digit(2, '3'), Digit(2, '6'), Digit(2, '0'), Digit(2, '5'), Digit(2, '1'), Insert(2),
        Digit(2, '4'), Digit(2, '1'), Digit(2, '1'), Digit(2, '5'), Digit(2, '0'), Digit(2, '8'), Digit(2, '0'), Insert(2));

    [Fact]
    public void The_present_position_entry_turns_the_selector_to_position_then_keys_the_position()
    {
        // [L1011-14]: on any other page the N/S/E/W keys start no entry, so the selector moves first, the
        // gauge gets SelectorSettleMs to draw the page, then the 15 keys follow in the same chain, as ONE string.
        _def.CanLand = _ => true;
        SelectorIsAt(2, 5);
        _trace.Clear();
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));

        Assert.Equal(4, _trace.Count);
        Assert.StartsWith("send:", _trace[0]);
        Assert.Contains("INS_2_ROTARY_MODE", _trace[0]);                            // the selector move
        Assert.Equal("wait:" + IniL1011Definition.SelectorSettleMs, _trace[1]);     // the page draws
        Assert.Equal("send:" + PresentPositionString, _trace[2]);                   // the whole position, one string
        Assert.Equal("wait:" + L1011InsKeys.RunGapMs, _trace[3]);
        Assert.True(PresentPositionString.Length <= L1011InsKeys.MaxRunLength);
        Assert.Equal(IniL1011Definition.SelectorSettleMs + L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
        Assert.Equal(450, _def.KeysBusyForMs());
        Assert.Equal(new[] { PresentPositionWords }, _speech.All);
    }

    [Fact]
    public void The_present_position_entry_chains_no_settle_when_the_selector_is_already_at_position()
    {
        _def.CanLand = _ => true;
        SelectorIsAt(2, 3);
        _trace.Clear();
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));

        Assert.Equal(new[] { "send:" + PresentPositionString, "wait:" + L1011InsKeys.RunGapMs }, _trace);
        Assert.Equal(L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
        Assert.Equal(new[] { PresentPositionWords }, _speech.All);
    }

    [Fact]
    public async Task Enter_present_position_decides_the_selector_move_when_its_turn_comes()
    {
        // A page picked from the window just before it records its position only when the pick RUNS, behind the
        // keys still being sent. Deciding at queue time saw the selector still at POSITION, chained no move, and
        // keyed the position onto the picked page.
        var gate = new TaskCompletionSource();
        _def.StepDelay = ms => { _trace.Add("wait:" + ms); return gate.Task; };
        _def.CanLand = _ => true;
        SelectorIsAt(2, 3);
        _trace.Clear();

        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "5", insert: false, _sim, _speech));       // keeps the chain busy
        _def.SetInsSelector("INS_2_ROTARY_MODE", 5, _sim, _speech);                                // queued behind it
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));    // queued behind the pick
        Assert.Equal(new[] { "send:" + Digit(2, '5'), "wait:50" }, _trace);                        // nothing past the first run yet
        gate.SetResult();
        await _def.KeysDone;

        Assert.Equal(new[]
        {
            "send:" + Digit(2, '5'), "wait:50",
            "send:5 (>L:INS_2_ROTARY_MODE) (>H:INS_2_DISPLAY_MODE)",    // the pick, after the keys
            "send:3 (>L:INS_2_ROTARY_MODE) (>H:INS_2_DISPLAY_MODE)",    // POSITION again, decided at the run's turn
            "wait:" + IniL1011Definition.SelectorSettleMs,
            "send:" + PresentPositionString, "wait:50",
        }, _trace);
    }

    [Fact]
    public async Task Enter_present_position_behind_queued_keys_moves_nothing_when_the_selector_is_at_position()
    {
        var gate = new TaskCompletionSource();
        _def.StepDelay = ms => { _trace.Add("wait:" + ms); return gate.Task; };
        _def.CanLand = _ => true;
        SelectorIsAt(2, 3);
        _trace.Clear();

        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "5", insert: false, _sim, _speech));
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));
        gate.SetResult();
        await _def.KeysDone;

        Assert.Equal(new[] { "send:" + Digit(2, '5'), "wait:50", "send:" + PresentPositionString, "wait:50" }, _trace);
    }

    [Fact]
    public void Enter_present_position_queued_behind_keys_counts_a_possible_move_in_the_busy_time()
    {
        // The move is decided later, so what is still unknown is counted: a pick queued ahead of it may change
        // the page. The window holds its read-back of the typed position for this long (an upper bound).
        var gate = new TaskCompletionSource();
        _def.StepDelay = _ => gate.Task;
        _def.CanLand = _ => true;
        SelectorIsAt(2, 3);
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "5", insert: false, _sim, _speech));
        Assert.Equal(L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
        Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));
        Assert.Equal(L1011InsKeys.RunGapMs + IniL1011Definition.SelectorSettleMs + L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
        gate.SetResult();
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
            _trace.Clear();
            Assert.True(_def.EnterPresentPosition(2, 36.08540568, -115.13363512, _sim, _speech));
            Assert.Equal(new[] { PresentPositionWords }, _speech.All);
            Assert.Equal("send:" + PresentPositionString, _trace[2]);
            Assert.Equal(IniL1011Definition.SelectorSettleMs + L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Fact]
    public void A_typed_run_is_one_string_with_each_keys_press_then_release_in_order()
    {
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "N36", insert: false, _sim, _speech));
        Assert.Equal(new[] { "send:" + Join(Digit(1, '2'), Digit(1, '3'), Digit(1, '6')), "wait:50" }, _trace);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Insert_is_the_last_click_of_the_same_string()
    {
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins3, "01", insert: true, _sim, _speech));
        Assert.Equal(new[] { "send:" + Join(Digit(3, '0'), Digit(3, '1'), Insert(3)), "wait:50" }, _trace);
    }

    [Fact]
    public void A_single_pressed_key_is_one_string_too()
    {
        _def.CanLand = _ => true;
        Assert.True(_def.PressKeys(L1011InsDevice.Ins1, new[] { L1011InsDevices.ClearKey(L1011InsDevice.Ins1) }, _sim, _speech));
        Assert.Equal(new[] { "send:(>H:PAD_INS_1_KEY_CLEAR) 0 (>L:INS_1_KEY_CLEAR_PUSH)", "wait:50" }, _trace);
    }

    [Fact]
    public void The_pms_keys_follow_the_same_path()
    {
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.CaptainPms, "12 5", insert: true, _sim, _speech));
        Assert.True(_def.PressKeys(L1011InsDevice.FirstOfficerPms, new[] { "PMS_FO_FWD" }, _sim, _speech));
        Assert.Equal(new[]
        {
            "send:(>H:PAD_PMS_CPT_1) 0 (>L:PMS_CPT_1_PUSH) (>H:PAD_PMS_CPT_2) 0 (>L:PMS_CPT_2_PUSH) "
                + "(>H:PAD_PMS_CPT_SP) 0 (>L:PMS_CPT_SP_PUSH) (>H:PAD_PMS_CPT_5) 0 (>L:PMS_CPT_5_PUSH) "
                + "(>H:PAD_PMS_CPT_INSERT) 0 (>L:PMS_CPT_INSERT_PUSH)",
            "wait:50",
            "send:(>H:PAD_PMS_FO_FWD) 0 (>L:PMS_FO_FWD_PUSH)",
            "wait:50",
        }, _trace);
    }

    [Fact]
    public void A_run_longer_than_the_limit_splits_between_keys_with_a_gap_after_each_string()
    {
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins1, "0123456789012345678901234", insert: false, _sim, _speech));   // 25 keys

        var clicks = "0123456789012345678901234".Select(c => Digit(1, c)).ToArray();
        Assert.Equal(new[]
        {
            "send:" + Join(clicks.Take(20).ToArray()), "wait:50",    // 20 keys of 44 characters: 899
            "send:" + Join(clicks.Skip(20).ToArray()), "wait:50",
        }, _trace);
        Assert.All(Sent, rpn => Assert.True(rpn.Length <= L1011InsKeys.MaxRunLength));
        Assert.Equal(2 * L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
    }

    [Fact]
    public void Two_runs_chained_one_after_the_other_are_separated_by_the_gap()
    {
        _def.CanLand = _ => true;
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "5", insert: false, _sim, _speech));
        Assert.True(_def.TypeKeys(L1011InsDevice.Ins2, "7", insert: true, _sim, _speech));
        Assert.Equal(new[]
        {
            "send:" + Digit(2, '5'), "wait:50",
            "send:" + Join(Digit(2, '7'), Insert(2)), "wait:50",
        }, _trace);
        Assert.Equal(2 * L1011InsKeys.RunGapMs, _def.KeysBusyForMs());
    }

    [Fact]
    public void Nothing_is_sent_when_a_key_in_the_run_is_refused()
    {
        _def.CanLand = _ => true;
        Assert.False(_def.PressKeys(L1011InsDevice.Ins1, new[] { "INS_1_KEY_2", "INS_1_KEY_Q" }, _sim, _speech));
        Assert.Empty(_trace);
        Assert.Equal(0, _def.KeysBusyForMs());
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
        Assert.Equal(L1011InsKeys.RunGapMs, oneKey);   // one string, then the gap before whatever follows
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
    public void Turning_the_mode_selector_from_align_to_nav_in_the_app_does_not_say_navigating()
    {
        // The live case: the pilot's own pick of the MSU is the position the definition knows (commanded), and
        // the status rests at 2 for an update after the turn before it goes to 1.
        _def.CanLand = _ => true;
        string msu = L1011InsAlignmentAnnouncer.MsuKey(1);
        Assert.True(_def.HandleUIVariableSet(msu, 2, _def.GetVariables()[msu], _sim, _speech));   // ALIGN
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 90);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 33.2);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 2);                                       // aligned
        Assert.True(_def.HandleUIVariableSet(msu, 3, _def.GetVariables()[msu], _sim, _speech));   // the pilot turns it to NAV
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 2);
        Deliver(L1011InsAlignmentAnnouncer.StatusKey(1), 1);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void Turning_the_mode_selector_to_nav_after_the_live_countdown_does_not_say_navigating()
    {
        // The values the definition really receives: the cache drops the exact 2 as a change of 0.001 or less
        // from the 2.0000000000000053 before it.
        _def.CanLand = _ => true;
        string msu = L1011InsAlignmentAnnouncer.MsuKey(1);
        string status = L1011InsAlignmentAnnouncer.StatusKey(1);
        Assert.True(_def.HandleUIVariableSet(msu, 2, _def.GetVariables()[msu], _sim, _speech));   // ALIGN
        foreach (double value in new[] { 90, 75.33333333333333, 60.666666666666664, 46, 31.333333333333336, 16.666666666666668, 2.0000000000000053 })
            Deliver(status, value);
        Assert.True(_def.HandleUIVariableSet(msu, 3, _def.GetVariables()[msu], _sim, _speech));   // the pilot turns it to NAV
        Deliver(status, 1);
        Assert.Empty(_speech.All);
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
