namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// iniBuilds L-1011 TriStar First Officer automation layer. Deliberately empty, as the MD-11's: flaps
/// are the Captain's, and gear plus autopilot engagement are the universal
/// <see cref="MSFSBlindAssist.Automation.UniversalAutomationService"/>'s job. AutoFlapsEnabled is stored
/// from settings and never acted on ([FO-15]).
/// </summary>
public sealed class L1011FoAutoManager : IFoAutoManager
{
    public bool AutoFlapsEnabled { get; set; }   // stored, never acted on

    public void Reset() { }

    public void Update(double altitudeMsl, double verticalSpeedFpm, double altitudeAgl, double airspeedKts, bool onGround) { }
}
