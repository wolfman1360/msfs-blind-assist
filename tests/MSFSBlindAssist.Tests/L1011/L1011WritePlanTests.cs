using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.L1011;

public class L1011WritePlanTests
{
    private static L1011Control Battery() => new()
    {
        Id = "TOGGLE_BATTERY", Kind = L1011Kinds.Switch, StateVar = "L:TOGGLE_Battery",
        Positions = new() { ["0"] = "OFF", ["1"] = "ON" },
        Transitions = new()
        {
            ["0"] = new() { "L:TOGGLE_Battery=0", "H:ELECTRICAL", "H:ELECTRICAL_0" },
            ["1"] = new() { "L:TOGGLE_Battery=1", "H:ELECTRICAL", "H:ELECTRICAL_1" },
        },
    };

    private static L1011Control SeatBelts() => new()
    {
        Id = "SWITCH_SEATBELT_LIGHT", Kind = L1011Kinds.Switch, StateVar = "L:SWITCH_SEATBELT_LIGHT",
        Transitions = new()
        {
            ["0"] = new() { "L:SWITCH_SEATBELT_LIGHT=0", "K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE" },
            ["1"] = new() { "L:SWITCH_SEATBELT_LIGHT=1", "K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE" },
        },
    };

    private static string[] Rpn(L1011Plan plan) =>
        plan.Steps.Select(s => s is L1011CalcStep c ? c.Rpn : $"wait {((L1011DelayStep)s).Milliseconds}").ToArray();

    [Fact]
    public void A_switch_writes_its_variable_then_fires_its_events_in_one_command()
    {
        var plan = L1011WritePlan.ForSet(Battery(), 1, 0);
        Assert.Null(plan.Refusal);
        Assert.Equal(new[] { "1 (>L:TOGGLE_Battery) (>H:ELECTRICAL) (>H:ELECTRICAL_1)" }, Rpn(plan));
    }

    [Fact]
    public void A_position_already_held_is_not_replayed()
    {
        var plan = L1011WritePlan.ForSet(Battery(), 1, 1.004);
        Assert.True(plan.IsEmpty);
        Assert.Null(plan.Refusal);
    }

    [Fact]
    public void An_unknown_position_still_sends_an_absolute_transition()
    {
        Assert.Single(L1011WritePlan.ForSet(Battery(), 0, null).Steps);
    }

    [Fact]
    public void A_toggle_event_is_refused_when_the_current_position_is_unknown()
    {
        var plan = L1011WritePlan.ForSet(SeatBelts(), 1, null);
        Assert.True(plan.IsEmpty);
        Assert.Equal(L1011WritePlan.UnknownPositionRefusal, plan.Refusal);
    }

    [Fact]
    public void A_toggle_event_is_sent_when_the_switch_really_moves()
    {
        Assert.Equal(new[] { "1 (>L:SWITCH_SEATBELT_LIGHT) (>K:CABIN_SEATBELTS_ALERT_SWITCH_TOGGLE)" },
            Rpn(L1011WritePlan.ForSet(SeatBelts(), 1, 0)));
    }

    [Fact]
    public void A_value_that_is_not_a_position_is_refused()
    {
        Assert.Equal(L1011WritePlan.NotAPositionRefusal, L1011WritePlan.ForSet(Battery(), 7, 0).Refusal);
    }

    [Fact]
    public void A_spring_switch_returns_to_rest_after_the_hold()
    {
        var discharge = new L1011Control
        {
            Id = "SWITCH_ENG_1_DISCH", Kind = L1011Kinds.Spring, StateVar = "L:SWITCH_ENG_1_DISCH", Rest = 1,
            Transitions = new()
            {
                ["0"] = new() { "L:SWITCH_ENG_1_DISCH=0" },
                ["1"] = new() { "L:SWITCH_ENG_1_DISCH=1" },
                ["2"] = new() { "L:SWITCH_ENG_1_DISCH=2" },
            },
        };
        Assert.Equal(new[] { "2 (>L:SWITCH_ENG_1_DISCH)", $"wait {L1011WritePlan.SpringHoldMs}", "1 (>L:SWITCH_ENG_1_DISCH)" },
            Rpn(L1011WritePlan.ForSet(discharge, 2, 1)));
    }

    [Fact]
    public void A_knob_is_clamped_into_its_range()
    {
        var knob = new L1011Control
        {
            Id = "ROTARY_FLT_STA", Kind = L1011Kinds.Knob, StateVar = "L:ROTARY_FLT_STA",
            Range = new[] { 0.0, 100.0 }, SetTemplate = new() { "L:ROTARY_FLT_STA={v}" },
        };
        Assert.Equal(new[] { "100 (>L:ROTARY_FLT_STA)" }, Rpn(L1011WritePlan.ForSet(knob, 140, 20)));
    }

    [Fact]
    public void An_event_button_holds_briefly_and_a_polled_button_holds_long()
    {
        var insert = new L1011Control
        {
            Id = "INS_1_KEY_INSERT", Kind = L1011Kinds.Button,
            Press = new() { "L:INS_1_KEY_INSERT_PUSH=1", "H:INS_1_KEY_INSERT" },
            Release = new() { "L:INS_1_KEY_INSERT_PUSH=0" },
        };
        var apuStart = new L1011Control
        {
            Id = "SWITCH_APU_START", Kind = L1011Kinds.Button,
            Press = new() { "L:SWITCH_APU_START, boolean=1" }, Release = new() { "L:SWITCH_APU_START, boolean=0" },
        };
        Assert.Equal(new[] { "1 (>L:INS_1_KEY_INSERT_PUSH) (>H:INS_1_KEY_INSERT)", $"wait {L1011WritePlan.EventButtonHoldMs}", "0 (>L:INS_1_KEY_INSERT_PUSH)" },
            Rpn(L1011WritePlan.ForPress(insert)));
        Assert.Equal($"wait {L1011WritePlan.PolledButtonHoldMs}", Rpn(L1011WritePlan.ForPress(apuStart))[1]);
        Assert.Equal("wait 3000", Rpn(L1011WritePlan.ForPress(apuStart, 3000))[1]);
    }

    [Fact]
    public void A_latch_button_has_no_release()
    {
        var close = new L1011Control
        {
            Id = "SWITCH_BRG_GEN_1", Kind = L1011Kinds.Latch,
            Press = new() { "L:SWITCH_BRG_GEN_1=1", "H:ELECTRICAL", "H:ELECTRICAL_1" },
        };
        Assert.Equal(new[] { "1 (>L:SWITCH_BRG_GEN_1) (>H:ELECTRICAL) (>H:ELECTRICAL_1)" }, Rpn(L1011WritePlan.ForPress(close)));
    }

    [Fact]
    public void An_encoder_steps_both_ways()
    {
        var dh = new L1011Control { Id = "ROTARY_CPT_DH", Kind = L1011Kinds.Encoder, Inc = new() { "H:ROTARY_CPT_DH_INC" }, Dec = new() { "H:ROTARY_CPT_DH_DEC" } };
        Assert.Equal(new[] { "(>H:ROTARY_CPT_DH_INC)" }, Rpn(L1011WritePlan.ForStep(dh, true)));
        Assert.Equal(new[] { "(>H:ROTARY_CPT_DH_DEC)" }, Rpn(L1011WritePlan.ForStep(dh, false)));
    }

    [Fact]
    public void A_long_transition_is_split_at_effect_boundaries_in_order()
    {
        var effects = Enumerable.Range(0, 60).Select(i => L1011Effect.Parse($"H:SOME_LONG_EVENT_NAME_NUMBER_{i:D3}")).ToList();
        var batches = L1011WritePlan.Batches(effects, null).ToList();
        Assert.True(batches.Count > 1);
        Assert.All(batches, b => Assert.True(b.Rpn.Length <= L1011WritePlan.MaxRpnLength));
        Assert.Equal(string.Join(' ', effects.Select(e => e.ToRpn())), string.Join(' ', batches.Select(b => b.Rpn)));
    }

    [Fact]
    public void Pulling_a_breaker_replays_its_click()
    {
        var b = new L1011Breaker
        {
            Index = 4,
            Transitions = new()
            {
                ["0"] = new() { "L:V_C_Breaker_004=0", "H:V_C_Breaker_004", "H:V_C_Breaker_004_0" },
                ["1"] = new() { "L:V_C_Breaker_004=1", "H:V_C_Breaker_004", "H:V_C_Breaker_004_1" },
            },
        };
        var plan = L1011WritePlan.ForBreaker(b, pull: true);
        Assert.Equal("1 (>L:V_C_Breaker_004) (>H:V_C_Breaker_004) (>H:V_C_Breaker_004_1)", ((L1011CalcStep)plan.Steps.Single()).Rpn);
        Assert.Equal(L1011WritePlan.NotSettableRefusal, L1011WritePlan.ForBreaker(new L1011Breaker { Index = 5 }, pull: true).Refusal);
    }
}
