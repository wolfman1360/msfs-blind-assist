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

    // An autopilot engage paddle as the map has it: 0 Command, 1 CWS, 2 Off.
    private static L1011Control Paddle(string id) => new()
    {
        Id = id, Kind = L1011Kinds.Switch, StateVar = "L:" + id,
        Positions = new() { ["0"] = "COMMAND", ["1"] = "CWS", ["2"] = "OFF" },
        Transitions = new()
        {
            ["0"] = new() { $"L:{id}=0", $"H:AUTOPILOT_{id}" },
            ["1"] = new() { $"L:{id}=1", $"H:AUTOPILOT_{id}" },
            ["2"] = new() { $"L:{id}=2", $"H:AUTOPILOT_{id}" },
        },
    };

    [Theory]
    [InlineData("SWITCH_AFCS_AP_A")]
    [InlineData("SWITCH_AFCS_AP_B")]
    public void A_paddle_from_off_to_command_stops_at_cws_first(string id)
    {
        // L1011_INS.js turns the pitch and roll loops on only in its CWS routine; its Command routine
        // re-arms the modes and leaves the loops as they are. Straight from Off to Command the
        // autopilot showed Command and flew nothing (2026-10-16).
        var plan = L1011WritePlan.ForSet(Paddle(id), 0, 2);
        Assert.Null(plan.Refusal);
        Assert.Equal(new[]
        {
            $"1 (>L:{id}) (>H:AUTOPILOT_{id})",
            $"wait {L1011WritePlan.PaddleNotchMs}",
            $"0 (>L:{id}) (>H:AUTOPILOT_{id})",
        }, Rpn(plan));
        Assert.Equal(new[] { 1.0 }, plan.Via);
    }

    [Fact]
    public void A_paddle_in_an_unknown_position_also_stops_at_cws_on_the_way_to_command()
    {
        Assert.Equal(3, L1011WritePlan.ForSet(Paddle("SWITCH_AFCS_AP_A"), 0, null).Steps.Count);
    }

    [Theory]
    [InlineData(0, 1)]   // CWS to Command: the loops are already on
    [InlineData(2, 0)]   // Command to Off
    [InlineData(2, 1)]   // CWS to Off
    [InlineData(1, 2)]   // Off to CWS
    public void Every_other_paddle_move_is_one_step(double target, double current)
    {
        var plan = L1011WritePlan.ForSet(Paddle("SWITCH_AFCS_AP_A"), target, current);
        Assert.Single(plan.Steps);
        Assert.Empty(plan.Via);
    }

    [Fact]
    public void A_three_position_switch_that_is_not_a_paddle_jumps_straight_there()
    {
        var selector = Paddle("ROTARY_INS_MODE_1");
        Assert.Single(L1011WritePlan.ForSet(selector, 0, 2).Steps);
    }

    [Fact]
    public void The_paddle_pause_outlasts_the_gauges_own_delay_at_a_low_frame_rate()
    {
        // The gauge runs a paddle's routine 50 ms after the event (setTimeout 0x32), reading the mode
        // from both paddles at that moment; CWS and Command sent back to back ran only the Command
        // routine (seen live, loops left off). At 10 frames a second the CWS routine needs a frame for
        // the event, the 50 ms timer and a frame to run: the pause outlasts that with room to spare.
        const int frameMs = 100;
        Assert.True(L1011WritePlan.PaddleNotchMs >= 2 * (frameMs + 50 + frameMs));
    }

    [Fact]
    public void The_apu_start_press_fires_the_apu_panels_event_after_the_switch()
    {
        // Measured from a stopped APU with the master on: L:SWITCH_APU_START alone (the cockpit click's
        // only effect) cycled the inlet doors and the APU never turned; with H:APU after it, as the
        // tablet's own APU ON step sends, the first press started it.
        var start = new L1011Control
        {
            Id = "SWITCH_APU_START", Kind = L1011Kinds.Button, StateVar = "L:SWITCH_APU_START",
            Press = new() { "L:SWITCH_APU_START, boolean=1" },
            Release = new() { "L:SWITCH_APU_START, boolean=0" },
        };
        Assert.Equal(new[]
        {
            "1 (>L:SWITCH_APU_START, boolean) (>H:APU)",
            $"wait {L1011WritePlan.PolledButtonHoldMs}",
            "0 (>L:SWITCH_APU_START, boolean)",
        }, Rpn(L1011WritePlan.ForPress(start)));
    }

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
