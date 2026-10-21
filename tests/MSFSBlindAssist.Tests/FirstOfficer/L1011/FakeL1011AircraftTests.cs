using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The fake TriStar reproduces what the executor tests rely on: the definition's own answers (unknown
/// keys, read-back, a toggle refused from an unknown position, a control already there), the aircraft
/// semantics seen live on 2026-10-21, failure injection, and a clock that moves only when asked.
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
    [InlineData(1)]   // the ties read closed on a cold aircraft (live, 2026-10-21)
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

    [Fact]
    public async Task The_stop_press_stops_the_apu()
    {
        var ac = new FakeL1011Aircraft();
        ac.Set(ApuMaster, 1);
        ac.Set(ApuRpm, 100);

        Assert.Equal(L1011FoResult.Sent, await ac.SetAsync(ApuStopPress, 1));

        Assert.Equal(0, ac.Get(ApuRpm));
        Assert.Equal(new[] { (ApuStopPress, 1.0) }, ac.Writes);
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
