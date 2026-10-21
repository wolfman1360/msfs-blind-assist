using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using A = MSFSBlindAssist.FirstOfficer.L1011.L1011FoFlightPhaseMonitor.LandingLightAction;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar's altitude-crossing automation, the MD-11's without Dial-A-Flap, flown through the real
/// executor on the fake TriStar: both landing lights off climbing past 10,300 ft and on descending below
/// 9,700 (only while the setting is on, the crossing tracked either way, [FO-19]); all three altimeters
/// to standard at the transition altitude (a reminder instead with the setting off, [FO-23]); the
/// transition level spoken only; the no-plan reminder above 18,300 ft; the seat belts through the shared
/// automation. Every sentence is queued, never interrupting.
/// </summary>
public class L1011FoFlightPhaseMonitorTests
{
    private const string LandingLeft = "TOGGLE_LANDING_LEFT";
    private const string LandingRight = "TOGGLE_LANDING_RIGHT";
    private const string SeatBelts = "SWITCH_SEATBELT_LIGHT";

    private const string LightsOff = "Above ten thousand. Landing lights off.";
    private const string LightsOn = "Below ten thousand. Landing lights on.";
    private const string AltimetersStandard = "Transition altitude. Altimeters standard.";
    private const string SetStandard = "Transition altitude. Set standard altimeters.";
    private const string TransitionLevel = "Transition level. Set local altimeter pressure now.";

    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private readonly FakeL1011Aircraft _ac = new();
    private readonly L1011FoActionExecutor _exec = new();
    private readonly SpeechCapture _speech = new();
    private readonly L1011FoFlightPhaseMonitor _monitor;

    public L1011FoFlightPhaseMonitorTests()
    {
        // Lights and signs on, as for a departure: each read, so a toggle is never refused unread.
        _ac.Set(LandingLeft, 1);
        _ac.Set(LandingRight, 1);
        _ac.Set(SeatBelts, 1);
        _exec.SetTransport(_ac);
        _monitor = new L1011FoFlightPhaseMonitor(_exec, _speech);
    }

    private void Fly(double altitudeFt, double vsFpm) => _monitor.Update(altitudeFt, vsFpm, onGround: false);

    /// <summary>Waits (on the real clock, bounded) until <paramref name="count"/> lines were spoken.</summary>
    private async Task SpokenAsync(int count)
    {
        var deadline = DateTime.UtcNow + Hang;
        while (_speech.All.Count < count && DateTime.UtcNow < deadline)
            await Task.Delay(10);
    }

    private void AssertSpoken(params string[] lines)
    {
        Assert.Equal(lines, _speech.All);
        Assert.Empty(_speech.Interrupts);   // every call queued (Announce), never interrupting
    }

    // ---- 10,000 ft landing lights ([FO-19]) ---------------------------------------------------------------

    [Fact]
    public void Climbing_past_10300_turns_both_landing_lights_off_once()
    {
        Fly(9_000, 1_500);
        Fly(10_400, 1_500);
        Fly(10_600, 1_500);

        Assert.Equal(new[] { (LandingLeft, 0.0), (LandingRight, 0.0) }, _ac.Writes);
        AssertSpoken(LightsOff);
    }

    [Fact]
    public void Descending_below_9700_turns_both_landing_lights_on()
    {
        _ac.Set(LandingLeft, 0);
        _ac.Set(LandingRight, 0);

        Fly(11_000, -1_500);
        Fly(9_600, -1_500);

        Assert.Equal(new[] { (LandingLeft, 1.0), (LandingRight, 1.0) }, _ac.Writes);
        AssertSpoken(LightsOn);
    }

    [Fact]
    public void Inside_the_hysteresis_band_nothing_happens()
    {
        Fly(9_000, 1_500);
        Fly(10_100, 1_500);
        Fly(10_250, 1_500);
        Fly(9_800, -1_500);

        Assert.Empty(_ac.Writes);
        Assert.Empty(_speech.All);
    }

    [Fact]
    public void With_the_setting_off_the_lights_are_left_alone_but_the_crossing_is_tracked()
    {
        _monitor.AutoLights10kEnabled = false;
        Fly(9_000, 1_500);
        Fly(10_400, 1_500);
        Assert.Empty(_ac.Writes);
        Assert.Empty(_speech.All);

        // Turned back on above 10,000 ft: no stale climb crossing, and the descent still fires.
        _monitor.AutoLights10kEnabled = true;
        Fly(10_600, 1_500);
        Assert.Empty(_speech.All);
        _ac.Set(LandingLeft, 0);
        _ac.Set(LandingRight, 0);
        Fly(9_600, -1_500);

        Assert.Equal(new[] { (LandingLeft, 1.0), (LandingRight, 1.0) }, _ac.Writes);
        AssertSpoken(LightsOn);
    }

    [Fact]
    public void Nothing_happens_while_the_aircraft_cannot_be_reached()
    {
        _exec.SetTransport(null);

        Fly(9_000, 1_500);
        Fly(10_400, 1_500);

        Assert.Empty(_ac.Io);
        Assert.Empty(_speech.All);
    }

    // The pure rule, as the MD-11's tests pin it.

    [Fact]
    public void The_10k_rule_turns_the_lights_off_once_climbing()
    {
        var (a1, l1) = L1011FoFlightPhaseMonitor.Evaluate10kCrossing(9_000, true, false, null, true);
        Assert.Equal(A.None, a1);
        Assert.False(l1);
        var (a2, l2) = L1011FoFlightPhaseMonitor.Evaluate10kCrossing(10_400, true, false, l1, true);
        Assert.Equal(A.TurnOff, a2);
        Assert.True(l2);
        var (a3, _) = L1011FoFlightPhaseMonitor.Evaluate10kCrossing(10_600, true, false, l2, true);
        Assert.Equal(A.None, a3);
    }

    [Fact]
    public void The_10k_rule_turns_the_lights_on_descending()
    {
        var (a, l) = L1011FoFlightPhaseMonitor.Evaluate10kCrossing(9_600, false, true, true, true);
        Assert.Equal(A.TurnOn, a);
        Assert.False(l);
    }

    [Fact]
    public void The_10k_rule_disabled_takes_no_action_but_the_latch_still_tracks()
    {
        var (a, l) = L1011FoFlightPhaseMonitor.Evaluate10kCrossing(10_400, true, false, false, false);
        Assert.Equal(A.None, a);
        Assert.True(l);
    }

    [Fact]
    public void The_10k_rule_holds_the_latch_inside_the_band()
    {
        var (a, l) = L1011FoFlightPhaseMonitor.Evaluate10kCrossing(10_100, true, false, false, true);
        Assert.Equal(A.None, a);
        Assert.False(l);
    }

    // ---- Transition altitude and level ([FO-23]) --------------------------------------------------------

    [Fact]
    public async Task At_the_transition_altitude_all_three_altimeters_go_to_standard()
    {
        _monitor.SetThresholds(5_000, 6_000);

        Fly(4_000, 1_500);
        Fly(5_400, 1_500);
        await SpokenAsync(1);

        Assert.Equal(new[] { 1013.25 }, _ac.AltimeterWrites);
        foreach (int n in new[] { 1, 2, 3 })
            Assert.Equal(1013.25, _ac.Get($"L1011_RO_ALTIMETER_{n}"));
        AssertSpoken(AltimetersStandard);
    }

    [Fact]
    public async Task An_altimeter_not_reading_standard_asks_for_standard()
    {
        _ac.NeverDeliver("L1011_RO_ALTIMETER_2");
        _ac.Set("L1011_RO_ALTIMETER_2", 1020);
        _monitor.SetThresholds(5_000, 6_000);

        Fly(4_000, 1_500);
        Fly(5_400, 1_500);
        await SpokenAsync(1);

        Assert.Equal(new[] { 1013.25 }, _ac.AltimeterWrites);
        AssertSpoken(SetStandard);
    }

    [Fact]
    public void With_the_altimeter_setting_off_the_crossing_is_a_reminder_and_nothing_is_written()
    {
        _monitor.AutoAltimetersEnabled = false;
        _monitor.SetThresholds(5_000, 6_000);

        Fly(4_000, 1_500);
        Fly(5_400, 1_500);

        Assert.Empty(_ac.AltimeterWrites);
        AssertSpoken(SetStandard);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_transition_level_is_spoken_only(bool autoAltimeters)
    {
        _monitor.AutoAltimetersEnabled = autoAltimeters;
        _monitor.SetThresholds(5_000, 6_000);

        Fly(7_000, -1_500);
        Fly(5_600, -1_500);

        Assert.Empty(_ac.AltimeterWrites);
        AssertSpoken(TransitionLevel);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_transition_altitude_sentence_says_what_was_done(bool standard)
        => Assert.Equal(standard ? AltimetersStandard : SetStandard,
            L1011FoFlightPhaseMonitor.TransitionAltitudeSentence(standard));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void With_no_plan_loaded_the_reminder_speaks_once_above_18300_and_again_after_17000(bool autoAltimeters)
    {
        _monitor.AutoAltimetersEnabled = autoAltimeters;
        string reminder = TransitionAltimeters.NoTransitionLoadedReminder(autoAltimeters);

        Fly(18_000, 1_500);
        Fly(18_400, 1_500);
        Fly(19_000, 1_500);
        AssertSpoken(reminder);

        Fly(16_900, -1_500);
        Fly(18_400, 1_500);
        AssertSpoken(reminder, reminder);
        Assert.Empty(_ac.AltimeterWrites);
    }

    // ---- Seat belts through the shared automation ---------------------------------------------------------

    [Fact]
    public void The_seat_belt_signs_follow_the_shared_automation_at_ten_thousand()
    {
        _monitor.AutoSeatbeltMode = FoSeatbeltMode.TenThousand;

        Fly(9_000, 1_500);
        Fly(10_400, 1_500);
        Fly(9_600, -1_500);

        Assert.Equal(new[]
        {
            (LandingLeft, 0.0), (LandingRight, 0.0), (SeatBelts, 0.0),
            (LandingLeft, 1.0), (LandingRight, 1.0), (SeatBelts, 1.0),
        }, _ac.Writes);
        AssertSpoken(
            LightsOff, "Above ten thousand. Seat belt signs off.",
            LightsOn, "Below ten thousand. Seat belt signs on.");
    }

    [Fact]
    public void With_the_seat_belt_automation_disabled_the_signs_are_left_alone()
    {
        _monitor.AutoSeatbeltMode = FoSeatbeltMode.Disabled;

        Fly(9_000, 1_500);
        Fly(10_400, 1_500);

        Assert.DoesNotContain(_ac.Io, e => e.Key == SeatBelts && e.Kind == FakeL1011IoKind.Write);
        AssertSpoken(LightsOff);
    }

    // ---- The auto manager ([FO-15]) ------------------------------------------------------------------------

    [Fact]
    public void The_auto_manager_stores_the_flap_setting_and_acts_on_nothing()
    {
        var m = new L1011FoAutoManager { AutoFlapsEnabled = true };
        Assert.True(m.AutoFlapsEnabled);
        m.Reset();
        m.Update(5_000, -800, 3_000, 180, onGround: false);   // no executor, no actions: a no-op
    }
}
