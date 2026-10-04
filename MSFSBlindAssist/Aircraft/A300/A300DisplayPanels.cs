namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The Displays section: one status box per A300 screen, each read from the aircraft's own variables
/// (no screen capture, no AI, no memory reading). It follows the FlyByWire A320's PFD, ND and ISIS
/// boxes, in A300 words. A display panel has no controls; its lines are the keys listed here, and
/// the definition's display text reads each one.
/// </summary>
public static class A300DisplayPanels
{
    public const string Section = "Displays";

    /// <summary>The section comes right after this one.</summary>
    public const string AfterSection = "Main Panel";

    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Lines = new Dictionary<string, IReadOnlyList<string>>
    {
        ["PFD"] = new[]
        {
            A300FmaSources.ThrustModeKey, A300FmaSources.PitchModeKey, A300FmaSources.RollModeKey, A300FmaSources.ArmedKey,
            A300Readouts.SpeedKey, A300Readouts.HeadingKey, A300Readouts.AltitudeKey, A300Readouts.VerticalSpeedKey,
            "PLANE_PITCH_DEGREES", "PLANE_BANK_DEGREES", A300Readouts.PfdHeadingKey, A300Readouts.PfdAirspeedKey,
            "INDICATED_ALTITUDE", A300Readouts.PfdVerticalSpeedKey, A300Readouts.PfdRadioAltitudeKey,
            A300Readouts.VlsKey, A300Readouts.VmaxKey, A300Readouts.GreenDotKey, A300Readouts.SSpeedKey, A300Readouts.FSpeedKey,
            A300Readouts.VsSpeedKey, A300Readouts.MinimumsKey,
        },
    };

    /// <summary>The panels, in the order the section lists them.</summary>
    public static readonly IReadOnlyList<string> Panels = new[] { "PFD" };

    public static bool IsDisplayPanel(string panel) => Lines.ContainsKey(panel);
}
