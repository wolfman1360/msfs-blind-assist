namespace MSFSBlindAssist.Aircraft.A300;

/// <summary>
/// The tablet's IDC option (Settings, <c>opt_idc1/2</c>: <c>L:INI_IS_IDC</c>), which puts the modern
/// IDC on the pedestal in place of the classic radio panel. While it is 1 the IDC owns the transponder
/// mode: <c>IDC::Update</c> (1.0.11) writes <c>L:INI_tcas_mode_pedestal</c> from its own ATC page every
/// frame, so the pedestal mode knob's write is undone (measured on the KSFO ground probe, 2026-10-04:
/// TA/RA sent, the mode went back to standby; with the option at 0 the mode held). MSFSBA does not
/// drive the IDC, so the transponder mode row refuses aloud instead of showing a mode the aircraft
/// did not take ([A300-19]). Pure.
/// </summary>
public static class A300Idc
{
    public const string OptionKey = "A300_IS_IDC";
    public const string OptionVar = "INI_IS_IDC";

    /// <summary>The pedestal transponder mode row (<c>AIRLINER_TCAS_MODE</c>).</summary>
    public const string TransponderModeKey = "A300_TCAS_MODE";

    public const string Refusal = "set from the IDC";

    /// <summary>Whether this row's write is refused for the IDC option's value; an unread option
    /// refuses nothing, so the row works as it did before the option was read.</summary>
    public static bool Refuses(string rowKey, double? option) =>
        rowKey == TransponderModeKey && option is double v && v >= 0.5;
}
