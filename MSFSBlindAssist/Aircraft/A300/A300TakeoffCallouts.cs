namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The A300's take-off roll call-outs ("V1", "Rotate", "V2") on the shared
/// <see cref="TakeoffVSpeedCallouts"/> machine, as the MD-11, the FBW A380 and the iFly 737 have them.
///
/// V1 and VR are the MCDU take-off page's (<c>INI_V1_FMGS</c>, <c>INI_VR_FMGS</c>, measured
/// 2026-10-04). <c>INI_V2_FMGS</c> follows the FCU speed window instead (250 with V2 typed as 150),
/// because the A300 crew sets V2 there for take-off; so it is V2 only when it is plausible: at or
/// above VR and no more than <see cref="MaxV2AboveVr"/> above it. A window left at 250 gives no V2
/// call rather than a wrong one. The speeds are consumed silently (the MCDU shows them); each call is
/// muted by its speed's Ctrl+M row, named for the call it mutes.
/// </summary>
public static class A300TakeoffCallouts
{
    /// <summary>The per-SIM_FRAME <c>AIRSPEED INDICATED</c> feed; consumed, never spoken, hidden from Ctrl+M.</summary>
    public const string IasKey = "A300_TAKEOFF_CALLOUT_IAS";

    public const string V1Key = "A300_TAKEOFF_V1";
    public const string VrKey = "A300_TAKEOFF_VR";
    public const string V2Key = "A300_TAKEOFF_V2";

    /// <summary>The feed, the speeds and their shared Ctrl+M rule.</summary>
    public static readonly TakeoffCalloutKeys Keys = new(IasKey, V1Key, VrKey, V2Key);

    /// <summary>Each speed's key, its variable and its Ctrl+M row's name.</summary>
    public static readonly IReadOnlyList<(string Key, string Var, string Name)> Speeds = new[]
    {
        (V1Key, "INI_V1_FMGS", "V1 call-out"),
        (VrKey, "INI_VR_FMGS", "Rotate call-out"),
        (V2Key, "INI_V2_FMGS", "V2 call-out"),
    };

    /// <summary>The widest V2 the FCU speed is taken for, above VR. A judgement: a real V2 is a few
    /// knots above VR, never this far.</summary>
    public const double MaxV2AboveVr = 30;

    /// <summary>The FCU speed when it is plausibly V2, else 0 (no V2 call).</summary>
    public static double PlausibleV2(double vr, double fcuSpeed) =>
        vr > 0 && fcuSpeed >= vr && fcuSpeed <= vr + MaxV2AboveVr ? fcuSpeed : 0;
}
