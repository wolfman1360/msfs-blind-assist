using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.FirstOfficer.L1011;

/// <summary>
/// The production transport: the TriStar definition's First Officer entry (the panels' own write core,
/// marshalled onto the UI thread captured at <see cref="IniL1011Definition.Attach"/>) plus SimConnect
/// reads. Writes and the lamp mute may be called from any thread. Build it on the UI thread: the
/// read-back question (<see cref="CanReadBack"/>) is not marshalled and reads the definition's lazily
/// built variable table, so the constructor builds that table here, never on a pool thread.
/// </summary>
public sealed class L1011DefinitionFoTransport : IL1011FoTransport
{
    private readonly IniL1011Definition _def;
    private readonly SimConnectManager _sim;

    public L1011DefinitionFoTransport(IniL1011Definition def, SimConnectManager sim)
    {
        _def = def;
        _sim = sim;
        _def.GetVariables();
    }

    public bool Ready => _def.FoReady;
    public long NowMs => Environment.TickCount64;

    public Task<L1011FoResult> SetAsync(string key, double value) => _def.FoSetAsync(key, value);
    public Task<L1011FoResult> SetAltimetersAsync(double millibars) => _def.FoSetAltimetersAsync(millibars);
    public bool CanReadBack(string key) => _def.FoCanReadBack(key);

    public Task<double?> ReadFreshAsync(string key, int timeoutMs) => _sim.ReadFreshAsync(key, timeoutMs);
    public double? ReadCached(string key) => _sim.GetCachedVariableValue(key);

    public void MuteLampSpeech(int ms) => _def.FoMuteLampSpeech(ms);

    public Task DelayAsync(int ms) => Task.Delay(ms);
}
