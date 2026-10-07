using MSFSBlindAssist.Services;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.L1011;

/// <summary>
/// Per-variable background-announcement manager for the iniBuilds L-1011 TriStar (Ctrl+M).
///
/// Rows come from <see cref="MonitorRowBuilder"/> like every other aircraft's: every Continuous +
/// IsAnnounced variable minus those flagged ExcludeFromMonitorManager, so the TriStar lists its
/// warning lights, its announced levers (flap handle, parking brake, ground spoilers), the
/// autopilot's engage switches, mode buttons and armed/captured flags, and the shared base
/// variables; the silently consumed switch positions are excluded because a checkbox there would
/// mute nothing. Unticked keys go to UserSettings.L1011DisabledMonitorVariables, honoured by
/// MainForm (generic gate and the ProcessSimVarUpdate wrap) and by the definition's warning-light
/// flush. All behaviour lives in <see cref="MonitorManagerFormBase"/>.
/// </summary>
public sealed class L1011MonitorManagerForm : MonitorManagerFormBase
{
    public L1011MonitorManagerForm(Dictionary<string, SimVarDefinition> variables)
        : base("L-1011 Monitor Manager", MonitorRowBuilder.Build(variables)) { }

    protected override ICollection<string> DisabledVariables
        => SettingsManager.Current.L1011DisabledMonitorVariables;
}
