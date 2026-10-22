using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The fake TriStar reproduces what the executor tests rely on: the definition's own answers (unknown
/// keys, read-back, a toggle refused from an unknown position, a control already there), the aircraft
/// semantics seen on the live aircraft, failure injection, and a clock that moves only when asked.
/// </summary>
public class FakeL1011AircraftTests
{
    private const string StandbyPower = "TOGGLE_STBY_POWER";
    private const string StandbyGuard = "GUARD_STBY_POWER";
    private const string Battery = "TOGGLE_BATTERY";
    private const string ExternalPower = "TOGGLE_EXT_PWR";
    private const string ExternalPowerAvailable = "LAMP_EXTERNAL_POWER_AVAILABLE";
    private const string ExternalPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";
    private const string Unarmed = "LAMP_STBY_UNARM_LIGHT";
    private const string Flowbar = "LAMP_STBY_PWR_FLOWBAR_LIGHT";
    private const string AcStandbyFail = "LAMP_AC_STBY_FAIL_LIGHT";
    private const string DcStandbyFail = "LAMP_DC_STBY_FAIL_LIGHT";
    private const string AcEssentialFail = "LAMP_AC_ESS_FAIL_LIGHT";
    private const string DcEssentialFail = "LAMP_DC_ESS_FAIL_LIGHT";
    private const string ContinuousIgnition = "SWITCH_CONT_IGNITION";

    private const double Off = 0, Arm = 1, On = 2;   // the standby power switch's positions

    // ---- The definition's answers ------------------------------------------------------------------

    [Fact]
    public void Nothing_reads_until_it_is_set()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Null(ac.ReadCached(ContinuousIgnition));
        Assert.Null(ac.Get(ContinuousIgnition));
        ac.Set(ContinuousIgnition, 1);
        Assert.Equal(1, ac.ReadCached(ContinuousIgnition));
    }

    [Fact]
    public async Task A_switch_write_is_sent_logged_and_read_at_its_target()
    {
        var ac = new FakeL1011Aircraft();

        var result = await ac.SetAsync(ContinuousIgnition, 1);

        Assert.Equal(L1011FoResult.Sent, result);
        Assert.Equal(new[] { (ContinuousIgnition, 1.0) }, ac.Writes);
        Assert.Equal(1, ac.ReadCached(ContinuousIgnition));
    }

    [Fact]
    public async Task A_switch_already_at_its_target_is_already_there_and_nothing_is_written()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ContinuousIgnition, 1);

        Assert.Equal(L1011FoResult.AlreadyThere, await ac.SetAsync(ContinuousIgnition, 1));
        Assert.Empty(ac.Writes);
    }

    [Fact]
    public async Task Not_ready_is_unavailable_and_nothing_is_written()
    {
        var ac = new FakeL1011Aircraft { Ready = false };
        ac.Set(ContinuousIgnition, 0);

        Assert.Equal(L1011FoResult.Unavailable, await ac.SetAsync(ContinuousIgnition, 1));
        Assert.Equal(L1011FoResult.Unavailable, await ac.SetAltimetersAsync(1013.25));
        Assert.Empty(ac.Writes);
        Assert.Empty(ac.AltimeterWrites);
        Assert.Equal(0, ac.Get(ContinuousIgnition));
    }

    [Theory]
    [InlineData("NOT_A_TRISTAR_KEY")]
    [InlineData(Unarmed)]                   // a lamp: read, never written
    [InlineData(ExternalPowerBreaker)]      // a readout: read, never written
    public async Task A_key_the_definition_does_not_take_is_unknown(string key)
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.UnknownKey, await ac.SetAsync(key, 1));
        Assert.Empty(ac.Writes);
    }

    [Theory]
    [InlineData(ContinuousIgnition, true)]          // a switch row
    [InlineData("L1011_PARKING_BRAKE", true)]       // a lever
    [InlineData(Unarmed, true)]                     // a lamp
    [InlineData("L1011_RO_N3_1", true)]             // a readout
    [InlineData("SWITCH_APU_START", false)]         // a press: never registered
    [InlineData("SWITCH_TIE_FLOW_1", false)]        // a latch press: never registered
    [InlineData("NOT_A_TRISTAR_KEY", false)]
    public void It_can_read_back_what_the_definition_can(string key, bool canReadBack)
    {
        Assert.Equal(canReadBack, new FakeL1011Aircraft().CanReadBack(key));
    }

    // ---- A toggle is refused from an unknown position ([L1011-4]) ------------------------------------

    [Theory]
    [InlineData("TOGGLE_CUTOFF_ENG_1")]
    [InlineData("TOGGLE_CUTOFF_ENG_2")]
    [InlineData("TOGGLE_CUTOFF_ENG_3")]
    [InlineData("SWITCH_SEATBELT_LIGHT")]
    [InlineData("SWITCH_SMOKING_LIGHT")]
    [InlineData("SWITCH_APU_BLEED")]
    [InlineData("SWITCH_ANTISKID_ON")]
    public async Task A_toggle_carrying_switch_is_refused_while_unread_and_sent_once_read(string key)
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Refused(L1011WritePlan.UnknownPositionRefusal), await ac.SetAsync(key, 1));
        Assert.Empty(ac.Writes);
        Assert.Null(ac.Get(key));

        ac.Set(key, 0);
        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(key, 1));
        Assert.Equal(new[] { (key, 1.0) }, ac.Writes);
        Assert.Equal(1, ac.Get(key));
    }

    [Fact]
    public async Task A_switch_without_a_toggle_is_sent_while_unread()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ContinuousIgnition, 1));
    }

    [Fact]
    public async Task The_fuel_and_ignition_switch_reads_through_its_row_key_only()
    {
        // Seen live: L:TOGGLE_CUTOFF_ENG_1 read 1 on a cold aircraft while the stock valve read 0. The
        // fake has no such L:var: the row key (the stock valve) is the switch.
        var ac = new FakeL1011Aircraft();
        ac.Set("TOGGLE_CUTOFF_ENG_1", 0);

        await ac.SetAsync("TOGGLE_CUTOFF_ENG_1", 1);

        Assert.Equal(1, ac.ReadCached("TOGGLE_CUTOFF_ENG_1"));
        Assert.DoesNotContain(ac.Vars.Keys, k => k.StartsWith("L:", StringComparison.Ordinal));
    }

    // ---- Electrical semantics ------------------------------------------------------------------------

    [Fact]
    public async Task Closing_the_standby_power_guard_puts_the_switch_to_arm()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(StandbyGuard, 1);
        ac.Set(StandbyPower, On);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(StandbyGuard, 0));

        Assert.Equal(0, ac.Get(StandbyGuard));
        Assert.Equal(Arm, ac.Get(StandbyPower));
        Assert.Equal(new[] { (StandbyGuard, 0.0) }, ac.Writes);   // the switch moved by itself, not by a write
    }

    [Fact]
    public async Task Opening_the_standby_power_guard_leaves_the_switch_alone()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(StandbyGuard, 0);
        ac.Set(StandbyPower, Arm);

        await ac.SetAsync(StandbyGuard, 1);

        Assert.Equal(Arm, ac.Get(StandbyPower));
    }

    [Theory]
    [InlineData(Off, 1, 0)]   // Off: the unarmed light means "switch Off"
    [InlineData(On, 0, 1)]    // On: unarmed out, flowbar lit
    [InlineData(Arm, 0, 0)]   // Arm: flowbar out
    public async Task The_standby_power_switch_drives_its_unarmed_and_flowbar_lights(double position, double unarmed, double flowbar)
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(StandbyGuard, 1);
        ac.Set(StandbyPower, position == Off ? Arm : Off);   // start elsewhere, so the write is sent

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(StandbyPower, position));

        Assert.Equal(unarmed, ac.Get(Unarmed));
        Assert.Equal(flowbar, ac.Get(Flowbar));
    }

    [Fact]
    public void The_standby_lights_follow_the_live_check_sequence()
    {
        // Cold, battery off, standby Off; standby On; battery On (unchanged); standby Arm.
        var ac = new FakeL1011Aircraft();
        ac.Set(Battery, 0);
        ac.Set(StandbyPower, Off);
        Assert.Equal((1.0, 0.0), (ac.Get(Unarmed)!.Value, ac.Get(Flowbar)!.Value));

        ac.Set(StandbyPower, On);
        Assert.Equal((0.0, 1.0), (ac.Get(Unarmed)!.Value, ac.Get(Flowbar)!.Value));

        ac.Set(Battery, 1);
        Assert.Equal((0.0, 1.0), (ac.Get(Unarmed)!.Value, ac.Get(Flowbar)!.Value));

        ac.Set(StandbyPower, Arm);
        Assert.Equal((0.0, 0.0), (ac.Get(Unarmed)!.Value, ac.Get(Flowbar)!.Value));
    }

    [Fact]
    public void The_unarmed_and_flowbar_lights_are_unread_while_the_switch_is()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Null(ac.Get(Unarmed));
        Assert.Null(ac.Get(Flowbar));
    }

    [Theory]
    [InlineData(Off, 0)]
    [InlineData(Off, 1)]
    [InlineData(Arm, 0)]
    [InlineData(Arm, 1)]
    [InlineData(On, 0)]
    [InlineData(On, 1)]
    public void The_standby_bus_fail_lights_are_always_out(double position, double battery)
    {
        var ac = new FakeL1011Aircraft();
        Assert.Equal(0, ac.Get(AcStandbyFail));
        Assert.Equal(0, ac.Get(DcStandbyFail));

        ac.Set(Battery, battery);
        ac.Set(StandbyPower, position);

        Assert.Equal(0, ac.Get(AcStandbyFail));
        Assert.Equal(0, ac.Get(DcStandbyFail));
    }

    [Fact]
    public void The_essential_bus_fail_lights_are_lit_unless_the_external_power_breaker_reads_closed()
    {
        var ac = new FakeL1011Aircraft();
        Assert.Equal(1, ac.Get(AcEssentialFail));   // breaker unread
        Assert.Equal(1, ac.Get(DcEssentialFail));

        ac.Set(ExternalPowerBreaker, 0);
        Assert.Equal(1, ac.Get(AcEssentialFail));
        Assert.Equal(1, ac.Get(DcEssentialFail));

        ac.Set(ExternalPowerBreaker, 1);
        Assert.Equal(0, ac.Get(AcEssentialFail));
        Assert.Equal(0, ac.Get(DcEssentialFail));
    }

    [Fact]
    public async Task The_battery_changes_no_standby_light()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(StandbyPower, On);
        ac.Set(Battery, 0);
        var before = new[] { Unarmed, Flowbar, AcStandbyFail, DcStandbyFail, AcEssentialFail, DcEssentialFail }
            .Select(ac.Get).ToList();

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(Battery, 1));

        Assert.Equal(before, new[] { Unarmed, Flowbar, AcStandbyFail, DcStandbyFail, AcEssentialFail, DcEssentialFail }
            .Select(ac.Get).ToList());
    }

    [Fact]
    public async Task External_power_on_with_its_light_lit_closes_its_breaker_and_puts_the_essential_lights_out()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ExternalPowerAvailable, 1);
        ac.Set(ExternalPowerBreaker, 0);
        ac.Set(ExternalPower, 0);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ExternalPower, 1));

        Assert.Equal(1, ac.Get(ExternalPowerBreaker));
        Assert.Equal(0, ac.Get(AcEssentialFail));
        Assert.Equal(0, ac.Get(DcEssentialFail));
    }

    [Theory]
    [InlineData(false)]   // the light out
    [InlineData(true)]    // the light unread
    public async Task External_power_on_without_its_light_lit_leaves_the_breaker_open(bool unread)
    {
        var ac = new FakeL1011Aircraft();
        if (!unread)
            ac.Set(ExternalPowerAvailable, 0);
        ac.Set(ExternalPowerBreaker, 0);
        ac.Set(ExternalPower, 0);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ExternalPower, 1));

        Assert.Equal(1, ac.Get(ExternalPower));
        Assert.Equal(0, ac.Get(ExternalPowerBreaker));
    }

    [Fact]
    public async Task External_power_off_opens_its_breaker()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ExternalPowerAvailable, 1);
        ac.Set(ExternalPower, 1);
        ac.Set(ExternalPowerBreaker, 1);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ExternalPower, 0));

        Assert.Equal(0, ac.Get(ExternalPowerBreaker));
        Assert.Equal(1, ac.Get(AcEssentialFail));
    }

    [Theory]
    [InlineData(1, ExternalPowerBreaker)]
    [InlineData(2, "L1011_RO_GEN_1_BREAKER")]
    [InlineData(3, "L1011_RO_GEN_2_BREAKER")]
    [InlineData(1, "L1011_RO_GEN_3_BREAKER")]
    [InlineData(2, "L1011_RO_APU_GEN_BREAKER")]
    public async Task A_tie_close_press_closes_its_relay_with_a_source_on_line(int tie, string source)
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(source, 1);
        ac.Set($"L1011_RO_AC_TIE_{tie}", 0);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync($"SWITCH_TIE_FLOW_{tie}", 1));

        Assert.Equal(1, ac.Get($"L1011_RO_AC_TIE_{tie}"));
        Assert.Equal(new[] { ($"SWITCH_TIE_FLOW_{tie}", 1.0) }, ac.Writes);
        Assert.Null(ac.Get($"SWITCH_TIE_FLOW_{tie}"));   // a press holds no position
        foreach (int other in new[] { 1, 2, 3 }.Where(n => n != tie))
            Assert.Null(ac.Get($"L1011_RO_AC_TIE_{other}"));
    }

    [Fact]
    public async Task A_tie_close_press_with_nothing_on_line_changes_nothing()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ExternalPowerBreaker, 0);
        ac.Set("L1011_RO_GEN_1_BREAKER", 0);
        ac.Set("L1011_RO_AC_TIE_1", 0);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync("SWITCH_TIE_FLOW_1", 1));

        Assert.Equal(0, ac.Get("L1011_RO_AC_TIE_1"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]   // the ties read closed on a cold aircraft (seen on the live aircraft)
    public void A_tie_relay_reads_however_a_test_seeds_it(double seeded)
    {
        var ac = new FakeL1011Aircraft();
        foreach (int n in new[] { 1, 2, 3 })
            ac.Set($"L1011_RO_AC_TIE_{n}", seeded);

        foreach (int n in new[] { 1, 2, 3 })
            Assert.Equal(seeded, ac.ReadCached($"L1011_RO_AC_TIE_{n}"));
    }

    // ---- The APU -------------------------------------------------------------------------------------------

    private const string ApuMaster = "TOGGLE_APU_POWER";
    private const string ApuStartPress = "SWITCH_APU_START";
    private const string ApuStopPress = "APU_STOP";
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuGenFieldPress = "SWITCH_APU_BRG";
    private const string ApuGenBreakerPress = "SWITCH_GND_FLOW";
    private const string ApuGenBreakerTrip = "SWITCH_GND_FLOW_OPEN";
    private const string ApuGenField = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";

    [Fact]
    public async Task A_start_press_with_the_master_on_spins_the_apu()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ApuMaster, 1);
        ac.Set(ApuRpm, 0);
        Assert.Equal(100, ac.ApuRpmAfterStart);   // running, by default

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ApuStartPress, 1));

        Assert.Equal(100, ac.Get(ApuRpm));
        Assert.Equal(new[] { (ApuStartPress, 1.0) }, ac.Writes);
        Assert.Null(ac.Get(ApuStartPress));   // a press holds no position

        // A test scripting the spool-up starts it from 0 and drives the RPM itself.
        var scripted = new FakeL1011Aircraft { ApuRpmAfterStart = 0 };
        scripted.Set(ApuMaster, 1);
        scripted.Set(ApuRpm, 0);

        await scripted.SetAsync(ApuStartPress, 1);

        Assert.Equal(0, scripted.Get(ApuRpm));
    }

    [Theory]
    [InlineData(false)]   // the master off
    [InlineData(true)]    // the master unread
    public async Task A_start_press_with_the_master_off_does_nothing(bool unread)
    {
        var ac = new FakeL1011Aircraft();
        if (!unread)
            ac.Set(ApuMaster, 0);
        ac.Set(ApuRpm, 0);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ApuStartPress, 1));

        Assert.Equal(0, ac.Get(ApuRpm));
    }

    /// <summary>Live, the APU ran down in about half a minute to a minute after the stop press: the fake spins it down
    /// <see cref="FakeL1011Aircraft.ApuSpinDownPerSecond"/> (2 %) a second of its clock, 50 s from 100, never below
    /// 0.</summary>
    [Fact]
    public async Task The_stop_press_spins_the_apu_down_by_the_clock()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ApuMaster, 1);
        ac.Set(ApuRpm, 100);
        Assert.Equal(2, ac.ApuSpinDownPerSecond);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ApuStopPress, 1));

        Assert.Equal(100, ac.Get(ApuRpm));   // nothing at once
        Assert.Equal(new[] { (ApuStopPress, 1.0) }, ac.Writes);
        await ac.DelayAsync(25_000);
        Assert.Equal(50, ac.Get(ApuRpm));
        await ac.DelayAsync(30_000);
        Assert.Equal(0, ac.Get(ApuRpm));
        await ac.DelayAsync(10_000);
        Assert.Equal(0, ac.Get(ApuRpm));
    }

    [Fact]
    public async Task A_start_press_ends_a_spin_down_and_no_press_leaves_the_apu_alone()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ApuMaster, 1);
        ac.Set(ApuRpm, 100);
        await ac.DelayAsync(10_000);
        Assert.Equal(100, ac.Get(ApuRpm));   // no stop press: it runs on

        await ac.SetAsync(ApuStopPress, 1);
        await ac.DelayAsync(10_000);
        Assert.Equal(80, ac.Get(ApuRpm));
        await ac.SetAsync(ApuStartPress, 1);   // started again: running
        await ac.DelayAsync(10_000);
        Assert.Equal(100, ac.Get(ApuRpm));
    }

    [Fact]
    public async Task Field_then_breaker_close_only_in_order_and_only_running()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ApuRpm, 50);   // starting, not running
        ac.Set(ApuGenField, 0);
        ac.Set(ApuGenBreaker, 0);

        await ac.SetAsync(ApuGenFieldPress, 1);
        Assert.Equal(0, ac.Get(ApuGenField));            // the field closes only with the APU running

        ac.Set(ApuRpm, 100);
        await ac.SetAsync(ApuGenBreakerPress, 1);
        Assert.Equal(0, ac.Get(ApuGenBreaker));          // the breaker closes only over a closed field

        await ac.SetAsync(ApuGenFieldPress, 1);
        Assert.Equal(1, ac.Get(ApuGenField));
        Assert.Equal(0, ac.Get(ApuGenBreaker));          // the field press closes the field alone

        await ac.SetAsync(ApuGenBreakerPress, 1);
        Assert.Equal(1, ac.Get(ApuGenBreaker));
        Assert.Null(ac.Get(ApuGenFieldPress));           // the buttons hold no position
        Assert.Null(ac.Get(ApuGenBreakerPress));
    }

    [Fact]
    public async Task The_trip_opens_the_breaker()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ApuRpm, 100);
        ac.Set(ApuGenField, 1);
        ac.Set(ApuGenBreaker, 1);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ApuGenBreakerTrip, 1));

        Assert.Equal(0, ac.Get(ApuGenBreaker));
        Assert.Equal(1, ac.Get(ApuGenField));            // the field stays closed
    }

    // ---- The fire tests (as the live aircraft behaves) ---------------------------------------------------

    private static readonly string[] FireLoopSelectors =
    {
        "ROTARY_FIRE_DETECT_TEST_ENG_1", "ROTARY_FIRE_DETECT_TEST_ENG_2", "ROTARY_FIRE_DETECT_TEST_ENG_3", "ROTARY_FIRE_DETECT_TEST_APU",
    };
    private const string FireTestA = "SWITCH_FIRE_DETECT_TEST_A";
    private const string FireTestB = "SWITCH_FIRE_DETECT_TEST_B";
    private static readonly string[] FireLamps =
    {
        "LAMP_ENG_FIRE_1", "LAMP_ENG_FIRE_2", "LAMP_ENG_FIRE_3", "LAMP_ENG_FIRE", "LAMP_FIRE_APU",
    };
    private const string WheelWellSelector = "ROTARY_WHEEL_WELL_TEST";
    private const string WheelWellTestA = "SWITCH_WHEEL_WELL_TEST_A";
    private const string WheelWellTestB = "SWITCH_WHEEL_WELL_TEST_B";
    private const string WheelWellFire = "LAMP_FIRE_WHEEL_WELL";

    private static FakeL1011Aircraft FireLoopsAt(double position)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var selector in FireLoopSelectors)
            ac.Set(selector, position);
        ac.Set(FireTestA, 0);
        ac.Set(FireTestB, 0);
        return ac;
    }

    [Fact]
    public async Task Both_fire_tests_on_every_loop_on_both_light_the_five_fire_lights_and_off_puts_them_out()
    {
        var ac = FireLoopsAt(2);
        Assert.All(FireLamps, lamp => Assert.Equal(0, ac.Get(lamp)));

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(FireTestA, 1));
        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(FireTestB, 1));
        Assert.All(FireLamps, lamp => Assert.Equal(1, ac.Get(lamp)));

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(FireTestA, 0));
        Assert.All(FireLamps, lamp => Assert.Equal(0, ac.Get(lamp)));
    }

    /// <summary>The fake's own rule, not the aircraft's: it lights the five only with A AND B on. Live, on APU and
    /// external power with the engines off, A alone lit nothing but B alone lit all five; the fake holds back on B
    /// alone so that an executor turning on one switch fails its test.</summary>
    [Theory]
    [InlineData(FireTestA)]
    [InlineData(FireTestB)]
    public async Task The_fakes_and_rule_lights_nothing_for_one_fire_test_alone(string test)
    {
        var ac = FireLoopsAt(2);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(test, 1));

        Assert.All(FireLamps, lamp => Assert.Equal(0, ac.Get(lamp)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]   // the single-loop positions do not match their labels live: the test runs on Both only
    public async Task Both_fire_tests_with_a_loop_selector_off_both_light_nothing(double position)
    {
        var ac = FireLoopsAt(2);
        ac.Set(FireLoopSelectors[3], position);

        await ac.SetAsync(FireTestA, 1);
        await ac.SetAsync(FireTestB, 1);

        Assert.All(FireLamps, lamp => Assert.Equal(0, ac.Get(lamp)));
    }

    [Fact]
    public void The_fire_lights_are_unread_until_a_loop_selector_or_test_switch_reads()
    {
        var ac = new FakeL1011Aircraft();
        Assert.All(FireLamps, lamp => Assert.Null(ac.Get(lamp)));
        Assert.Null(ac.Get(WheelWellFire));

        ac.Set(FireTestB, 0);
        Assert.All(FireLamps, lamp => Assert.Equal(0, ac.Get(lamp)));
        Assert.Null(ac.Get(WheelWellFire));   // the wheel well light follows its own switches

        ac.Set(WheelWellSelector, 0);
        Assert.Equal(0, ac.Get(WheelWellFire));
    }

    [Theory]
    [InlineData(1, 1, 1, 1)]   // Both, A and B on: lit
    [InlineData(1, 1, 0, 0)]   // A alone lit nothing live
    [InlineData(1, 0, 1, 0)]
    [InlineData(0, 1, 1, 0)]   // Loop A
    [InlineData(2, 1, 1, 0)]   // Loop B
    public async Task The_wheel_well_light_lights_only_with_both_tests_on_the_both_selector(double selector, double a, double b, double lit)
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(WheelWellSelector, selector);
        ac.Set(WheelWellTestA, 0);
        ac.Set(WheelWellTestB, 0);

        if (a == 1) Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(WheelWellTestA, 1));
        if (b == 1) Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(WheelWellTestB, 1));

        Assert.Equal(lit, ac.Get(WheelWellFire));
        Assert.All(FireLamps, lamp => Assert.Null(ac.Get(lamp)));   // the engine fire lights are not the wheel well's
    }

    [Fact]
    public async Task A_fire_light_never_delivered_holds_its_reading()
    {
        var ac = FireLoopsAt(2);
        ac.NeverDeliver(FireLamps[1]);   // stuck out

        await ac.SetAsync(FireTestA, 1);
        await ac.SetAsync(FireTestB, 1);

        Assert.Equal(0, ac.Get(FireLamps[1]));
        Assert.Equal(1, ac.Get(FireLamps[0]));
    }

    // ---- Hydraulics (as the live aircraft behaves on external power) ---------------------------------------

    private const string PumpB = "SWITCH_HYD_AC_PUMP_B3";
    private const string PumpC = "SWITCH_HYD_AC_PUMP_C3";
    private const string PtuAb = "SWITCH_PTU_AB";
    private const string PtuCd = "SWITCH_PTU_CD";
    private static string Hyd(char system) => $"L1011_RO_HYD_{system}";

    private static FakeL1011Aircraft HydraulicsOff()
    {
        var ac = new FakeL1011Aircraft();
        foreach (var key in new[] { PumpB, PumpC, PtuAb, PtuCd })
            ac.Set(key, 0);
        return ac;
    }

    [Fact]
    public void Every_system_reads_depressurized_with_nothing_on()
    {
        var ac = new FakeL1011Aircraft();

        foreach (char system in "ABCD")
            Assert.Equal(14.5, ac.Get(Hyd(system)));
    }

    [Theory]
    [InlineData(PumpB, 'B', 'C')]
    [InlineData(PumpC, 'C', 'B')]
    public async Task An_ac_pump_pressurizes_its_own_system_alone(string pump, char own, char other)
    {
        var ac = HydraulicsOff();

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(pump, 1));
        Assert.Equal(2650, ac.Get(Hyd(own)));
        Assert.Equal(14.5, ac.Get(Hyd(other)));
        Assert.Equal(14.5, ac.Get(Hyd('A')));
        Assert.Equal(14.5, ac.Get(Hyd('D')));

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(pump, 0));
        Assert.Equal(14.5, ac.Get(Hyd(own)));
    }

    [Theory]
    [InlineData(1, 1, 1, 1, 2600)]   // both pumps and both PTUs: A and D pressurized
    [InlineData(1, 1, 1, 0, 14.5)]   // one PTU off
    [InlineData(1, 1, 0, 1, 14.5)]
    [InlineData(1, 0, 1, 1, 14.5)]   // system C not pressurized
    [InlineData(0, 1, 1, 1, 14.5)]   // system B not pressurized
    public async Task The_ptus_pressurize_a_and_d_only_with_both_on_over_b_and_c_pressurized(
        double b, double c, double ab, double cd, double ad)
    {
        var ac = HydraulicsOff();

        foreach (var (key, on) in new[] { (PumpB, b), (PumpC, c), (PtuAb, ab), (PtuCd, cd) })
            if (on == 1)
                Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(key, 1));

        Assert.Equal(ad, ac.Get(Hyd('A')));
        Assert.Equal(ad, ac.Get(Hyd('D')));
    }

    [Fact]
    public async Task Everything_off_bleeds_every_system_down_at_once()
    {
        var ac = HydraulicsOff();
        foreach (var key in new[] { PumpB, PumpC, PtuAb, PtuCd })
            await ac.SetAsync(key, 1);
        Assert.Equal(2600, ac.Get(Hyd('A')));

        foreach (var key in new[] { PtuAb, PtuCd, PumpB, PumpC })
            await ac.SetAsync(key, 0);

        foreach (char system in "ABCD")
            Assert.Equal(14.5, ac.Get(Hyd(system)));
    }

    [Fact]
    public async Task A_stuck_system_b_keeps_a_from_pressurizing()
    {
        var ac = HydraulicsOff();
        ac.NeverDeliver(Hyd('B'));   // stuck at 14.5

        foreach (var key in new[] { PumpB, PumpC, PtuAb, PtuCd })
            await ac.SetAsync(key, 1);

        Assert.Equal(14.5, ac.Get(Hyd('B')));
        Assert.Equal(2650, ac.Get(Hyd('C')));
        Assert.Equal(14.5, ac.Get(Hyd('A')));
        Assert.Equal(14.5, ac.Get(Hyd('D')));
    }

    // ---- The engines (as the live aircraft behaves on APU air) ----------------------------------------------

    private static string GroundStart(int n) => $"SWITCH_GND_START_ENG_{n}";
    private static string Fuel(int n) => $"TOGGLE_CUTOFF_ENG_{n}";
    private static string N3(int n) => $"L1011_RO_N3_{n}";
    private static string GenField(int n) => $"L1011_RO_GEN_{n}_FIELD";
    private static string GenBreaker(int n) => $"L1011_RO_GEN_{n}_BREAKER";

    /// <summary>Every engine cold (a gauge reads 1.6, never 0), its fuel and ignition and its ground start off.</summary>
    private static FakeL1011Aircraft ColdEngines()
    {
        var ac = new FakeL1011Aircraft();
        foreach (int n in new[] { 1, 2, 3 })
        {
            ac.Set(N3(n), 1.6);
            ac.Set(Fuel(n), 0);
            ac.Set(GroundStart(n), 0);
        }
        return ac;
    }

    [Fact]
    public async Task A_ground_start_turns_n3_one_percent_a_second_up_to_dry_motoring_with_the_fuel_off()
    {
        var ac = ColdEngines();
        Assert.Equal(1.0, ac.GroundStartN3PerSecond);   // live: about 1 %/s on APU air

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(GroundStart(2), 1));
        Assert.Equal(1.6, ac.Get(N3(2)));                // nothing until the clock moves

        await ac.DelayAsync(2000);
        Assert.Equal(3.6, ac.Get(N3(2))!.Value, 9);
        Assert.Equal(1.6, ac.Get(N3(1)));                // the other engines stay cold
        Assert.Equal(1.6, ac.Get(N3(3)));

        await ac.DelayAsync(60_000);
        Assert.Equal(FakeL1011Aircraft.DryMotoringN3, ac.Get(N3(2)));
        Assert.Equal(1, ac.Get(GroundStart(2)));         // dry: the switch stays on
        Assert.Equal(new[] { (GroundStart(2), 1.0) }, ac.Writes);
    }

    [Fact]
    public async Task With_the_fuel_on_it_runs_on_to_idle_and_the_aircraft_releases_the_switch_past_fifty_five()
    {
        var ac = ColdEngines();
        await ac.SetAsync(GroundStart(2), 1);
        await ac.DelayAsync(20_000);                      // 21.6 %
        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(Fuel(2), 1));

        await ac.DelayAsync(33_000);                      // 54.6 %: the switch still on
        Assert.Equal(1, ac.Get(GroundStart(2)));
        await ac.DelayAsync(1_000);                       // past 55 %: released by the aircraft
        Assert.Equal(0, ac.Get(GroundStart(2)));

        await ac.DelayAsync(30_000);
        Assert.Equal(FakeL1011Aircraft.IdleN3Reading, ac.Get(N3(2)));   // runs on to idle, unstarted
        Assert.Equal(59.2, FakeL1011Aircraft.IdleN3Reading);
        Assert.Equal(new[] { (GroundStart(2), 1.0), (Fuel(2), 1.0) }, ac.Writes);   // the release was not a write
    }

    [Fact]
    public async Task Writing_the_ground_start_off_freezes_n3_where_it_is()
    {
        var ac = ColdEngines();
        await ac.SetAsync(GroundStart(2), 1);
        await ac.DelayAsync(5_000);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(GroundStart(2), 0));
        double frozen = ac.Get(N3(2))!.Value;
        await ac.DelayAsync(30_000);

        Assert.Equal(frozen, ac.Get(N3(2)));
        Assert.Equal(0, ac.Get(GroundStart(2)));
    }

    [Fact]
    public async Task A_ground_start_already_on_turns_the_engine_too()
    {
        var ac = ColdEngines();
        ac.Set(GroundStart(3), 1);   // seeded, not written

        await ac.DelayAsync(2000);

        Assert.Equal(3.6, ac.Get(N3(3))!.Value, 9);
        Assert.Empty(ac.Writes);
    }

    [Fact]
    public async Task No_rise_per_second_never_turns_the_engine()
    {
        var ac = ColdEngines();
        ac.GroundStartN3PerSecond = 0;

        await ac.SetAsync(GroundStart(2), 1);
        await ac.DelayAsync(60_000);

        Assert.Equal(1.6, ac.Get(N3(2)));
    }

    [Fact]
    public async Task The_ground_start_release_turns_every_ground_start_off_and_freezes_them()
    {
        var ac = ColdEngines();
        foreach (int n in new[] { 1, 2, 3 })
            await ac.SetAsync(GroundStart(n), 1);
        await ac.DelayAsync(3_000);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync("SWITCH_GND_START", 1));
        var frozen = new[] { 1, 2, 3 }.Select(n => ac.Get(N3(n))).ToList();
        await ac.DelayAsync(10_000);

        foreach (int n in new[] { 1, 2, 3 })
            Assert.Equal(0, ac.Get(GroundStart(n)));
        Assert.Equal(frozen, new[] { 1, 2, 3 }.Select(n => ac.Get(N3(n))).ToList());
        Assert.Null(ac.Get("SWITCH_GND_START"));   // a press holds no position
    }

    [Fact]
    public async Task A_generator_field_closes_only_on_a_running_engine_and_its_breaker_only_over_a_closed_field()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(N3(1), 49);   // below running
        ac.Set(GenField(1), 0);
        ac.Set(GenBreaker(1), 0);

        await ac.SetAsync("SWITCH_BRG_GEN_1", 1);
        Assert.Equal(0, ac.Get(GenField(1)));
        await ac.SetAsync("SWITCH_GEN_FLOW_1", 1);
        Assert.Equal(0, ac.Get(GenBreaker(1)));       // the breaker closes only over a closed field

        ac.Set(N3(1), 59.2);
        await ac.SetAsync("SWITCH_BRG_GEN_1", 1);
        Assert.Equal(1, ac.Get(GenField(1)));
        Assert.Equal(0, ac.Get(GenBreaker(1)));       // the field press closes the field alone
        await ac.SetAsync("SWITCH_GEN_FLOW_1", 1);
        Assert.Equal(1, ac.Get(GenBreaker(1)));

        Assert.Null(ac.Get(GenField(2)));             // another engine's relays are not touched
        Assert.Null(ac.Get("SWITCH_BRG_GEN_1"));      // the buttons hold no position
        Assert.Null(ac.Get("SWITCH_GEN_FLOW_1"));
    }

    // ---- Altimeters and the lamp mute ------------------------------------------------------------------

    [Fact]
    public async Task The_altimeters_are_set_together()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Sent, await ac.SetAltimetersAsync(1013.25));

        Assert.Equal(new[] { 1013.25 }, ac.AltimeterWrites);
        foreach (int n in new[] { 1, 2, 3 })
            Assert.Equal(1013.25, ac.Get($"L1011_RO_ALTIMETER_{n}"));
    }

    [Fact]
    public async Task An_altimeter_setting_in_inches_is_read_as_the_definition_reads_it()
    {
        var ac = new FakeL1011Aircraft();

        await ac.SetAltimetersAsync(29.92);

        Assert.Equal(L1011Levers.AltimeterMillibars(29.92), ac.Get("L1011_RO_ALTIMETER_1"));
    }

    [Fact]
    public async Task An_altimeter_setting_out_of_range_is_refused_as_the_definition_refuses_it()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Refused(L1011Levers.AltimeterRangeError), await ac.SetAltimetersAsync(900));
        Assert.Empty(ac.AltimeterWrites);
        Assert.Null(ac.Get("L1011_RO_ALTIMETER_1"));
    }

    // ---- The cabin baro and the cabin altitude selector -------------------------------------------------

    private const string CabinBaro = "L1011_RO_CABIN_BARO";
    private const string CabinAltitudeSelected = "L1011_RO_CABIN_ALT_SELECTED";

    [Fact]
    public async Task A_cabin_baro_write_sets_the_readout()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Sent, await ac.SetCabinBaroAsync(1002.0));

        Assert.Equal(1002.0, ac.Get(CabinBaro));
        var io = Assert.Single(ac.Io);
        Assert.Equal((FakeL1011IoKind.CabinBaro, FakeL1011Aircraft.CabinBaroIoKey, (double?)1002.0), (io.Kind, io.Key, io.Value));
        Assert.Empty(ac.Writes);
        Assert.Empty(ac.AltimeterWrites);
        Assert.Null(ac.Get("L1011_RO_ALTIMETER_1"));   // the cabin's setting is its own
    }

    [Fact]
    public async Task A_cabin_baro_write_reads_the_setting_as_the_definition_does()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Sent, await ac.SetCabinBaroAsync(29.92));
        Assert.Equal(L1011Levers.AltimeterMillibars(29.92), ac.Get(CabinBaro));

        Assert.Equal(L1011FoResult.Refused(L1011Levers.AltimeterRangeError), await ac.SetCabinBaroAsync(2000));
        Assert.Single(ac.Io);

        ac.Ready = false;
        Assert.Equal(L1011FoResult.Unavailable, await ac.SetCabinBaroAsync(1002.0));
        Assert.Single(ac.Io);
    }

    [Fact]
    public async Task A_cabin_altitude_step_sets_the_readout_in_metres()
    {
        // SimConnect delivers L:TARGET_CABIN_ALTITUDE in metres (L1011Readouts scales it to feet).
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Sent, await ac.SetCabinAltitudeSelectorAsync(49));

        Assert.InRange(ac.Get(CabinAltitudeSelected)!.Value, 4500 * 0.3048 - 0.01, 4500 * 0.3048 + 0.01);
        var io = Assert.Single(ac.Io);
        Assert.Equal((FakeL1011IoKind.CabinAltitudeStep, FakeL1011Aircraft.CabinAltitudeIoKey, (double?)49), (io.Kind, io.Key, io.Value));
        Assert.Empty(ac.Writes);
    }

    [Fact]
    public async Task A_cabin_altitude_step_outside_its_travel_is_refused_as_the_definition_refuses_it()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Equal(L1011FoResult.Refused(IniL1011Definition.CabinAltitudeSelectorRangeError), await ac.SetCabinAltitudeSelectorAsync(101));
        Assert.Equal(L1011FoResult.Refused(IniL1011Definition.CabinAltitudeSelectorRangeError), await ac.SetCabinAltitudeSelectorAsync(-1));
        ac.Ready = false;
        Assert.Equal(L1011FoResult.Unavailable, await ac.SetCabinAltitudeSelectorAsync(49));

        Assert.Empty(ac.Io);
        Assert.Null(ac.Get(CabinAltitudeSelected));
    }

    [Fact]
    public async Task The_tablets_take_off_exports_read_fresh_like_any_key()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(IniL1011Definition.PerfSelectedFlapsKey, 10);
        ac.Set(IniL1011Definition.PerfStabTrimKey, 2.38);

        Assert.Equal(10, await ac.ReadFreshAsync(IniL1011Definition.PerfSelectedFlapsKey, 2500));
        Assert.Equal(2.38, ac.ReadCached(IniL1011Definition.PerfStabTrimKey));
    }

    [Fact]
    public async Task OnClock_runs_after_each_delay_with_the_new_time()
    {
        var ac = new FakeL1011Aircraft { DeliveryLagMs = 300 };
        var seen = new List<long>();
        ac.OnClock = fake =>
        {
            seen.Add(fake.NowMs);
            fake.Set("L1011_RO_N3_2", fake.NowMs / 100.0);   // a script: N3 rises with time
        };

        await ac.DelayAsync(1500);
        await ac.DelayAsync(500);
        Assert.Equal(new long[] { 1500, 2000 }, seen);
        Assert.Equal(20, ac.Get("L1011_RO_N3_2"));

        // A fresh read waits through the clock too, and delivers what the script made of the new time.
        Assert.Equal(23, await ac.ReadFreshAsync("L1011_RO_N3_2", 2500));
        Assert.Equal(new long[] { 1500, 2000, 2300 }, seen);
    }

    [Fact]
    public void A_lamp_mute_is_recorded()
    {
        var ac = new FakeL1011Aircraft();

        ac.MuteLampSpeech(20_000);

        Assert.Equal(new[] { 20_000 }, ac.MuteRequests);
    }

    // ---- Reads and time ---------------------------------------------------------------------------------

    [Fact]
    public async Task Time_moves_only_when_asked()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ContinuousIgnition, 0);
        Assert.Equal(0, ac.NowMs);

        await ac.SetAsync(ContinuousIgnition, 1);
        ac.ReadCached(ContinuousIgnition);
        await ac.ReadFreshAsync(ContinuousIgnition, 2500);   // delivered with no lag
        Assert.Equal(0, ac.NowMs);

        await ac.DelayAsync(1500);
        Assert.Equal(1500, ac.NowMs);
    }

    [Fact]
    public async Task A_fresh_read_delivers_the_value_after_the_delivery_lag()
    {
        var ac = new FakeL1011Aircraft { DeliveryLagMs = 1000 };
        ac.Set(ContinuousIgnition, 1);

        Assert.Equal(1, await ac.ReadFreshAsync(ContinuousIgnition, 2500));
        Assert.Equal(1000, ac.NowMs);
        Assert.Equal(new[] { ContinuousIgnition }, ac.FreshReads);
    }

    [Fact]
    public async Task A_fresh_read_of_an_unset_key_is_null_at_its_timeout()
    {
        var ac = new FakeL1011Aircraft();

        Assert.Null(await ac.ReadFreshAsync(ContinuousIgnition, 2500));
        Assert.Equal(2500, ac.NowMs);
        Assert.Equal(new[] { ContinuousIgnition }, ac.FreshReads);
    }

    [Fact]
    public async Task A_fresh_read_lagging_past_its_timeout_is_null_at_its_timeout()
    {
        var ac = new FakeL1011Aircraft { DeliveryLagMs = 3000 };
        ac.Set(ContinuousIgnition, 1);

        Assert.Null(await ac.ReadFreshAsync(ContinuousIgnition, 2500));
        Assert.Equal(2500, ac.NowMs);
    }

    [Fact]
    public async Task Reads_and_writes_are_logged_in_order()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ContinuousIgnition, 0);

        await ac.ReadFreshAsync(ContinuousIgnition, 2500);
        await ac.SetAsync(ContinuousIgnition, 1);
        await ac.ReadFreshAsync(ContinuousIgnition, 2500);
        await ac.SetAltimetersAsync(1013.25);

        Assert.Equal(new[]
        {
            FakeL1011IoKind.FreshRead, FakeL1011IoKind.Write, FakeL1011IoKind.FreshRead, FakeL1011IoKind.Altimeters,
        }, ac.Io.Select(e => e.Kind));
        Assert.Equal(new double?[] { 0, 1, 1, 1013.25 }, ac.Io.Select(e => e.Value));
    }

    // ---- Failure injection --------------------------------------------------------------------------------

    [Fact]
    public async Task A_refused_key_is_refused_and_nothing_changes()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(Battery, 1);
        ac.Refuse(Battery);

        Assert.Equal(L1011FoResult.Refused(FakeL1011Aircraft.RefusalReason), await ac.SetAsync(Battery, 0));
        Assert.Empty(ac.Writes);
        Assert.Equal(1, ac.Get(Battery));
    }

    [Fact]
    public async Task A_write_never_delivered_is_sent_but_the_reading_stays()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ContinuousIgnition, 0);
        ac.NeverDeliver(ContinuousIgnition);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ContinuousIgnition, 1));

        Assert.Equal(new[] { (ContinuousIgnition, 1.0) }, ac.Writes);
        Assert.Equal(0, await ac.ReadFreshAsync(ContinuousIgnition, 2500));
        Assert.Equal(0, ac.ReadCached(ContinuousIgnition));
    }

    [Fact]
    public async Task A_write_never_delivered_drives_nothing_behind_it()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ExternalPowerAvailable, 1);
        ac.Set(ExternalPowerBreaker, 0);
        ac.Set(ExternalPower, 0);
        ac.NeverDeliver(ExternalPower);

        await ac.SetAsync(ExternalPower, 1);

        Assert.Equal(0, ac.Get(ExternalPower));
        Assert.Equal(0, ac.Get(ExternalPowerBreaker));
    }

    [Fact]
    public async Task A_light_never_delivered_holds_its_reading()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(StandbyGuard, 1);
        ac.Set(StandbyPower, Off);
        ac.NeverDeliver(Unarmed);   // stuck lit
        ac.NeverDeliver(Flowbar);   // stuck out
        ac.NeverDeliver(AcStandbyFail);
        ac.Set(AcStandbyFail, 1);   // set after NeverDeliver: held lit

        await ac.SetAsync(StandbyPower, On);

        Assert.Equal(1, ac.Get(Unarmed));
        Assert.Equal(0, ac.Get(Flowbar));
        Assert.Equal(1, ac.Get(AcStandbyFail));
        Assert.Equal(0, ac.Get(DcStandbyFail));   // not held: the aircraft's own reading
    }

    [Fact]
    public async Task A_light_never_delivered_and_unset_stays_unread()
    {
        var ac = new FakeL1011Aircraft();
        ac.NeverDeliver(AcEssentialFail);
        ac.Unset(AcEssentialFail);

        ac.Set(ExternalPowerBreaker, 0);

        Assert.Null(await ac.ReadFreshAsync(AcEssentialFail, 2500));
        Assert.Equal(1, ac.Get(DcEssentialFail));
    }
}
