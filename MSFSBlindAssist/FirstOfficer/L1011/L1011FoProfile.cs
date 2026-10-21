using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.Settings;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// iniBuilds L-1011 TriStar First Officer profile, the MD-11's shape. The executor writes through the
/// live definition's own First Officer entry, so the definition is passed in by the caller
/// (<c>Forms.FirstOfficer.FirstOfficerWindows.Create</c>), with the announcer the executor's own lines
/// are queued on ([FO-26]).
///
/// The evaluator is created ONCE: the window, the flows, the checklists, the phase monitor and the auto
/// manager all read the one instance <see cref="BindDataManager"/> feeds.
///
/// The transport is built in <see cref="SetExecutorSimConnect"/>, which the window calls on the UI
/// thread: its constructor builds the definition's variable table there, never on a pool thread. A null
/// connection leaves the executor without a transport, so it is unavailable and the window says so.
/// </summary>
public sealed class L1011FoProfile : IFoProfile<L1011FoActionExecutor, L1011FoStateEvaluator>
{
    private readonly IniL1011Definition _def;
    private readonly ScreenReaderAnnouncer _announcer;
    private L1011FoStateEvaluator? _state;

    public L1011FoProfile(IniL1011Definition def, ScreenReaderAnnouncer announcer)
    {
        _def = def;
        _announcer = announcer;
    }

    public string Title => "First Officer — iniBuilds L-1011";

    public L1011FoStateEvaluator CreateEvaluator() => _state ??= new();

    public L1011FoActionExecutor CreateExecutor()
    {
        var exec = new L1011FoActionExecutor();
        exec.SetSpeech(_announcer.Announce);
        return exec;
    }

    public void BindDataManager(L1011FoStateEvaluator state, SimConnectManager sc) => state.SetSimConnect(sc);

    public void SetExecutorSimConnect(L1011FoActionExecutor exec, SimConnectManager? sc)
    {
        if (sc == null)
        {
            exec.SetTransport(null);
            return;
        }
        _def.Attach(sc);                                       // idempotent: re-points the definition, keeps its UI context
        exec.SetTransport(new L1011DefinitionFoTransport(_def, sc));
    }

    public List<FlowDefinition<L1011FoStateEvaluator>> BuildFlows() => L1011FoFlowDefinitions.Build();

    public List<ChecklistGroup<L1011FoActionExecutor, L1011FoStateEvaluator>> BuildChecklists()
        => L1011FoChecklistDefinitions.Build();

    public IFoAutoManager CreateAutoManager(L1011FoActionExecutor exec, L1011FoStateEvaluator state,
        ScreenReaderAnnouncer a, UserSettings s)
        => new L1011FoAutoManager { AutoFlapsEnabled = s.FOAutoFlapsEnabled };   // stored, never acted on

    public IFoPhaseMonitor CreatePhaseMonitor(L1011FoActionExecutor exec, L1011FoStateEvaluator state,
        ScreenReaderAnnouncer a)
        => new L1011FoFlightPhaseMonitor(exec, a);
}
