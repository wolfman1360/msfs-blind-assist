using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Utils.Logging;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// Altitude-crossing automation for the iniBuilds L-1011 TriStar: the MD-11's monitor without
/// Dial-A-Flap, which the TriStar does not have.
///
///   Passing 10,000 ft climbing (10,300): both landing lights off. Descending (9,700): on.
///   Gated on <see cref="AutoLights10kEnabled"/> ([FO-19]); the crossing latch tracks regardless.
///
///   Passing the transition altitude climbing: the captain's, first officer's and standby altimeters
///   to standard in one write, then one sentence saying whether all three read it. With
///   <see cref="AutoAltimetersEnabled"/> off ([FO-23]) nothing is written and the crossing is spoken as
///   a reminder instead (<see cref="TransitionAltimeters.ReminderInstead"/>).
///
///   Passing the transition level descending: announce-only either way. The local pressure is
///   unknowable here, so the First Officer never sets it.
///
///   With no transition altitude loaded, one reminder climbing past 18,300 ft, re-armed below 17,000.
///
///   The seat belt signs follow the shared <see cref="SeatbeltAutomation"/>.
///
/// Every sentence is queued (<c>Announce</c>): a light call, a seat belt call and a transition call can
/// come from one sample, and an interrupting call cut off the one before it.
///
/// UI-thread only, like the rest of the First Officer: Update mutates unsynchronised latches.
/// </summary>
public sealed class L1011FoFlightPhaseMonitor : IFoPhaseMonitor
{
    private const string LogCategory = "L1011 FO";

    private const int LandingLightThresholdFt = 10_000;
    private const int HysteresisFt = 300;

    private readonly L1011FoActionExecutor _executor;
    private readonly ScreenReaderAnnouncer _announcer;
    private readonly SeatbeltAutomation _seatbelt;
    private readonly TransitionCrossingDetector _trans = new();

    private bool? _prevAbove10k;
    private bool _noTransReminderFired;

    public L1011FoFlightPhaseMonitor(L1011FoActionExecutor executor, ScreenReaderAnnouncer announcer)
    {
        _executor = executor;
        _announcer = announcer;
        _seatbelt = new SeatbeltAutomation(on => { _ = _executor.SetSeatbeltSign(on); }, announcer.Announce);
    }

    public bool AutoLights10kEnabled { get; set; } = true;

    public bool AutoAltimetersEnabled { get; set; } = true;

    public FoSeatbeltMode AutoSeatbeltMode
    {
        get => _seatbelt.Mode;
        set => _seatbelt.Mode = value;
    }

    public void SetThresholds(int transAltFt, int transLevelFt) => _trans.SetThresholds(transAltFt, transLevelFt);

    public void Reset()
    {
        _prevAbove10k = null;
        _trans.Reset();
        _seatbelt.Reset();
        _noTransReminderFired = false;
    }

    /// <summary>SimBrief cruise altitude for the TOC/TOD seat-belt automation (null = no plan).</summary>
    public void SetPlannedCruiseAltitude(int? cruiseFt) => _seatbelt.PlannedCruiseFt = cruiseFt;

    public void Update(double altitudeFt, double verticalSpeedFpm, bool onGround)
    {
        if (!_executor.IsAvailable) return;

        bool climbing = verticalSpeedFpm > 150;
        bool descending = verticalSpeedFpm < -150;

        var (action, latch) = Evaluate10kCrossing(altitudeFt, climbing, descending, _prevAbove10k, AutoLights10kEnabled);
        _prevAbove10k = latch;
        switch (action)
        {
            case LandingLightAction.TurnOff:
                _ = _executor.SetLandingLights(false);
                _announcer.Announce("Above ten thousand. Landing lights off.");
                break;
            case LandingLightAction.TurnOn:
                _ = _executor.SetLandingLights(true);
                _announcer.Announce("Below ten thousand. Landing lights on.");
                break;
        }

        _seatbelt.Update(altitudeFt, verticalSpeedFpm, onGround);

        if (_trans.HasThresholds)
            CheckTransitionCrossing(altitudeFt, climbing, descending);
        else
            CheckNoTransitionReminder(altitudeFt, climbing);
    }

    private void CheckTransitionCrossing(double alt, bool climbing, bool descending)
    {
        var crossing = _trans.Update(alt, climbing, descending);
        // The setting off: nothing written, the crossing spoken instead.
        if (TransitionAltimeters.ReminderInstead(crossing, AutoAltimetersEnabled) is { } reminder)
        {
            _announcer.Announce(reminder);
            return;
        }

        switch (crossing)
        {
            case TransitionCrossingDetector.Crossing.ClimbToStd:
                _ = TransitionAltitudeAsync();
                break;
            case TransitionCrossingDetector.Crossing.DescendToQnh:
                _announcer.Announce(TransitionAltimeters.LevelReminder);
                break;
        }
    }

    // Awaits WITHOUT ConfigureAwait(false): Update runs on the UI thread, so the sentence is spoken back
    // there. Fire-and-forget, so every exception is caught here.
    private async Task TransitionAltitudeAsync()
    {
        bool standard;
        try
        {
            standard = await _executor.SetAltimetersStandardAsync();
        }
        catch (Exception ex)
        {
            Log.Warn(LogCategory, $"transition-altitude altimeters failed: {ex.Message}");
            standard = false;
        }
        _announcer.Announce(TransitionAltitudeSentence(standard));
    }

    /// <summary>What the transition altitude crossing did: the altimeters set, or the Captain asked to.</summary>
    internal static string TransitionAltitudeSentence(bool altimetersStandard)
        => altimetersStandard ? "Transition altitude. Altimeters standard." : TransitionAltimeters.AltitudeReminder;

    private void CheckNoTransitionReminder(double alt, bool climbing)
    {
        if (!_noTransReminderFired && climbing && alt > 18_000 + HysteresisFt)
        {
            _noTransReminderFired = true;
            _announcer.Announce(TransitionAltimeters.NoTransitionLoadedReminder(AutoAltimetersEnabled));
        }
        else if (_noTransReminderFired && alt < 17_000)
        {
            _noTransReminderFired = false;
        }
    }

    internal enum LandingLightAction { None, TurnOff, TurnOn }

    /// <summary>
    /// The pure 10,000 ft rule: the action (only while enabled) and the new latch (ALWAYS tracked outside
    /// the hysteresis band, so re-enabling mid-flight never fires a stale crossing). A VS lull on the
    /// crossing tick never fires without a real direction.
    /// </summary>
    internal static (LandingLightAction action, bool? newLatch) Evaluate10kCrossing(
        double alt, bool climbing, bool descending, bool? prevAbove10k, bool autoLightsEnabled)
    {
        bool nowAbove = alt > LandingLightThresholdFt + HysteresisFt;
        bool nowBelow = alt < LandingLightThresholdFt - HysteresisFt;

        var action = LandingLightAction.None;
        if (autoLightsEnabled)
        {
            if (!descending && nowAbove && prevAbove10k == false) action = LandingLightAction.TurnOff;
            else if (!climbing && nowBelow && prevAbove10k == true) action = LandingLightAction.TurnOn;
        }

        bool? newLatch = prevAbove10k;
        if (nowAbove) newLatch = true;
        else if (nowBelow) newLatch = false;
        return (action, newLatch);
    }
}
