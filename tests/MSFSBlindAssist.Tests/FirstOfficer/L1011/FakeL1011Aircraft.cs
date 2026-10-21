using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.L1011;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>What reached the fake TriStar.</summary>
internal enum FakeL1011IoKind { Write, Altimeters, FreshRead }

/// <summary>One thing that reached the fake, in order. <see cref="Value"/>: a write's value, the altimeter
/// setting in hectopascals, or what a fresh read delivered (null when nothing was). <see cref="AtMs"/>: the
/// fake's clock when it happened (a fresh read: when it was answered).</summary>
internal readonly record struct FakeL1011Io(FakeL1011IoKind Kind, string Key, double? Value, long AtMs);

/// <summary>
/// A fake TriStar for the First Officer's executor tests: the definition's answers over a dictionary of
/// readings, and the aircraft semantics seen live on 2026-10-21.
/// <list type="bullet">
/// <item>The definition's answers come from the definition's own code: a key it does not take is
/// <see cref="L1011FoOutcome.UnknownKey"/> and <see cref="CanReadBack"/> is its answer
/// (<c>IniL1011Definition.FoAcceptsKey</c>, <c>FoCanReadBack</c>), and a map row is planned with
/// <see cref="L1011WritePlan"/> from the fake's reading, so a switch already at its target is
/// <see cref="L1011FoOutcome.AlreadyThere"/> and a transition carrying a TOGGLE K: event is refused while its
/// switch is unread ([L1011-4]): the fuel and ignition switches (<c>TOGGLE_CUTOFF_ENG_1..3</c>, read through
/// their row keys, the stock valve, never an L:var), the seat belt and no smoking signs, APU bleed and
/// anti-skid.</item>
/// <item>A write sent is logged (<see cref="Io"/>) and the control reads its target, a press excepted (it
/// holds no position). Writes complete at once: a held press's hold is not modelled.</item>
/// <item>Closing the standby power guard (<c>GUARD_STBY_POWER</c> 0) puts the standby power switch to Arm
/// (<c>TOGGLE_STBY_POWER</c> 1), as the guard's own click does.</item>
/// <item>An AC tie close press (<c>SWITCH_TIE_FLOW_n</c>) closes its relay (<c>L1011_RO_AC_TIE_n</c> 1) when
/// external power or a generator is on line (its breaker relay reads closed); otherwise nothing changes. A
/// test seeds the ties either way (they read closed on a cold aircraft).</item>
/// <item>External power on (<c>TOGGLE_EXT_PWR</c> 1) with its available light lit closes its breaker
/// (<c>L1011_RO_EXT_PWR_BREAKER</c> 1); external power off opens it.</item>
/// <item>The standby lights as the aircraft showed them: the unarmed light lit only with the switch Off, the
/// flowbar lit only with it On (neither read while the switch is unread), both standby bus fail lights always
/// out, both essential bus fail lights lit unless the external power breaker reads closed. The battery
/// changes none of them.</item>
/// <item>The altimeters set together set the three altimeter readouts, the setting read as the definition
/// reads it (inches taken too; out of range refused).</item>
/// </list>
/// Failure injection: <see cref="Refuse"/> and <see cref="NeverDeliver"/>. Time moves only through
/// <see cref="DelayAsync"/>, which a fresh read also waits through. Every member is safe from any thread.
/// </summary>
internal sealed class FakeL1011Aircraft : IL1011FoTransport
{
    /// <summary>The reason a write to a <see cref="Refuse"/>d key is refused with.</summary>
    public const string RefusalReason = "refused by the test";

    /// <summary>The key the altimeter writes are logged under.</summary>
    public const string AltimetersIoKey = "ALTIMETERS";

    private const string StandbyPower = "TOGGLE_STBY_POWER";
    private const string StandbyGuard = "GUARD_STBY_POWER";
    private const string ExternalPower = "TOGGLE_EXT_PWR";
    private const string ExternalPowerAvailable = "LAMP_EXTERNAL_POWER_AVAILABLE";
    private const string ExternalPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";
    private const string Unarmed = "LAMP_STBY_UNARM_LIGHT";
    private const string Flowbar = "LAMP_STBY_PWR_FLOWBAR_LIGHT";
    private const string AcStandbyFail = "LAMP_AC_STBY_FAIL_LIGHT";
    private const string DcStandbyFail = "LAMP_DC_STBY_FAIL_LIGHT";
    private const string AcEssentialFail = "LAMP_AC_ESS_FAIL_LIGHT";
    private const string DcEssentialFail = "LAMP_DC_ESS_FAIL_LIGHT";
    private const string TieClosePrefix = "SWITCH_TIE_FLOW_";

    private const double StandbyOff = 0, StandbyArm = 1, StandbyOn = 2;

    /// <summary>A generator is on line when its breaker relay reads closed.</summary>
    private static readonly string[] GeneratorBreakers =
    {
        "L1011_RO_GEN_1_BREAKER", "L1011_RO_GEN_2_BREAKER", "L1011_RO_GEN_3_BREAKER", "L1011_RO_APU_GEN_BREAKER",
    };

    /// <summary>One definition answers for every fake (never attached: it only answers questions).</summary>
    private static readonly Lazy<IniL1011Definition> Definition = new(() =>
    {
        var def = new IniL1011Definition();
        def.GetVariables();
        return def;
    });

    /// <summary>The definition's rows, as it places them: the action and control a write is planned with.</summary>
    private static readonly Lazy<Dictionary<string, L1011PlacedRow>> Rows = new(() =>
    {
        var rows = new Dictionary<string, L1011PlacedRow>(StringComparer.Ordinal);
        foreach (var panel in L1011PanelLayout.Place(L1011ControlMap.Load(), L1011Levers.Keys).RowsByPanel.Values)
            foreach (var row in panel)
                rows[row.Key] = row;
        return rows;
    });

    private readonly object _sync = new();
    private readonly HashSet<string> _refused = new(StringComparer.Ordinal);
    private readonly HashSet<string> _neverDelivered = new(StringComparer.Ordinal);
    private long _clock;

    public FakeL1011Aircraft() => Recompute();

    /// <summary>A write would reach the aircraft; false makes every write <see cref="L1011FoOutcome.Unavailable"/>.</summary>
    public bool Ready { get; set; } = true;

    /// <summary>How long a fresh read takes to be answered; past its time-out it is answered with nothing.</summary>
    public int DeliveryLagMs { get; set; }

    /// <summary>The readings, by key; a key absent is unread. Seed through <see cref="Set"/>.</summary>
    public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);

    /// <summary>Every write sent, altimeter write and fresh read, in order.</summary>
    public List<FakeL1011Io> Io { get; } = new();

    /// <summary>Every lamp mute asked for, in milliseconds.</summary>
    public List<int> MuteRequests { get; } = new();

    /// <summary>The writes sent, in order (a refused, unknown, unavailable or already-there write is not one).</summary>
    public List<(string Key, double Value)> Writes
    {
        get { lock (_sync) return Io.Where(e => e.Kind == FakeL1011IoKind.Write).Select(e => (e.Key, e.Value!.Value)).ToList(); }
    }

    /// <summary>The keys read fresh, in order.</summary>
    public List<string> FreshReads
    {
        get { lock (_sync) return Io.Where(e => e.Kind == FakeL1011IoKind.FreshRead).Select(e => e.Key).ToList(); }
    }

    /// <summary>The altimeter settings sent, in hectopascals, in order.</summary>
    public List<double> AltimeterWrites
    {
        get { lock (_sync) return Io.Where(e => e.Kind == FakeL1011IoKind.Altimeters).Select(e => e.Value!.Value).ToList(); }
    }

    /// <summary>The reading of <paramref name="key"/>, null when unread.</summary>
    public double? Get(string key)
    {
        lock (_sync) return Value(key);
    }

    /// <summary>Seeds a reading directly (not a write: nothing is logged, nothing behind it runs), even on a
    /// key that is never delivered; the lights then follow it.</summary>
    public void Set(string key, double value)
    {
        lock (_sync)
        {
            Vars[key] = value;
            Recompute();
        }
    }

    /// <summary>Makes a key unread. A light the fake computes reads again at once unless it is never
    /// delivered (<see cref="NeverDeliver"/> first).</summary>
    public void Unset(string key)
    {
        lock (_sync)
        {
            Vars.Remove(key);
            Recompute();
        }
    }

    /// <summary>Every write to <paramref name="key"/> is refused (<see cref="RefusalReason"/>): nothing is
    /// sent, logged or changed.</summary>
    public void Refuse(string key)
    {
        lock (_sync) _refused.Add(key);
    }

    /// <summary>
    /// The aircraft never takes a write to <paramref name="key"/>, and no change of it ever arrives: a write
    /// is sent and logged but drives nothing, and the key keeps its present reading (an unread key stays
    /// unread) whatever the aircraft does. To hold a different reading, <see cref="Set"/> it after this call.
    /// </summary>
    public void NeverDeliver(string key)
    {
        lock (_sync) _neverDelivered.Add(key);
    }

    // ---- IL1011FoTransport ------------------------------------------------------------------------------

    public long NowMs
    {
        get { lock (_sync) return _clock; }
    }

    public Task<L1011FoResult> SetAsync(string key, double value)
    {
        lock (_sync) return Task.FromResult(Write(key, value));
    }

    public Task<L1011FoResult> SetAltimetersAsync(double millibars)
    {
        lock (_sync)
        {
            if (!Ready)
                return Task.FromResult(L1011FoResult.Unavailable);
            if (L1011Levers.AltimeterMillibars(millibars) is not double setting)
                return Task.FromResult(L1011FoResult.Refused(L1011Levers.AltimeterRangeError));
            Io.Add(new(FakeL1011IoKind.Altimeters, AltimetersIoKey, setting, _clock));
            foreach (int n in new[] { 1, 2, 3 })
                Put($"L1011_RO_ALTIMETER_{n}", setting);
            Recompute();
            return Task.FromResult(L1011FoResult.Sent);
        }
    }

    public bool CanReadBack(string key) => Definition.Value.FoCanReadBack(key);

    /// <summary>The reading after <see cref="DeliveryLagMs"/>; for an unread key, or a lag past
    /// <paramref name="timeoutMs"/>, null once the time-out has passed. Either way the wait is on the clock.</summary>
    public Task<double?> ReadFreshAsync(string key, int timeoutMs)
    {
        lock (_sync)
        {
            double? delivered = null;
            if (Value(key) is null || DeliveryLagMs > timeoutMs)
                DelayAsync(timeoutMs);
            else
            {
                DelayAsync(DeliveryLagMs);
                delivered = Value(key);
            }
            Io.Add(new(FakeL1011IoKind.FreshRead, key, delivered, _clock));
            return Task.FromResult(delivered);
        }
    }

    public double? ReadCached(string key) => Get(key);

    public void MuteLampSpeech(int ms)
    {
        lock (_sync) MuteRequests.Add(ms);
    }

    public Task DelayAsync(int ms)
    {
        lock (_sync) _clock += Math.Max(0, ms);
        return Task.CompletedTask;
    }

    // ---- The definition's answers ------------------------------------------------------------------------

    private L1011FoResult Write(string key, double value)
    {
        if (!Definition.Value.FoAcceptsKey(key))
            return L1011FoResult.UnknownKey;
        if (!Ready)
            return L1011FoResult.Unavailable;
        if (_refused.Contains(key))
            return L1011FoResult.Refused(RefusalReason);
        var row = Rows.Value[key];
        var plan = row.Action switch
        {
            // A knob is planned from nothing read (the definition trusts only what it just commanded).
            L1011RowAction.Set => L1011WritePlan.ForSet(row.Control!, value,
                row.Control!.Kind == L1011Kinds.Knob ? null : Value(key)),
            L1011RowAction.Press => L1011WritePlan.ForPress(row.Control!, row.HoldMs),
            _ => null,   // a step, a lever or the stabilizer trim: sent as it is
        };
        if (plan?.Refusal is string why)
            return L1011FoResult.Refused(why);
        if (plan is { IsEmpty: true })
            return L1011FoResult.AlreadyThere;

        Io.Add(new(FakeL1011IoKind.Write, key, value, _clock));
        if (_neverDelivered.Contains(key))
            return L1011FoResult.Sent;
        if (row.Action is L1011RowAction.Set or L1011RowAction.Custom)
            Vars[key] = value;
        Apply(key, value);
        Recompute();
        return L1011FoResult.Sent;
    }

    // ---- The aircraft ------------------------------------------------------------------------------------

    /// <summary>What a write drives beyond its own control.</summary>
    private void Apply(string key, double value)
    {
        if (key == StandbyGuard && value < 0.5)
            Put(StandbyPower, StandbyArm);   // the guard's close click writes L:TOGGLE_STBY_POWER=1
        else if (key == ExternalPower)
        {
            if (value < 0.5)
                Put(ExternalPowerBreaker, 0);
            else if (IsLit(ExternalPowerAvailable))
                Put(ExternalPowerBreaker, 1);
        }
        else if (TieNumber(key) is int tie && SourceOnLine())
            Put($"L1011_RO_AC_TIE_{tie}", 1);
    }

    /// <summary>The lights that follow other readings.</summary>
    private void Recompute()
    {
        if (Value(StandbyPower) is double standby)
        {
            Put(Unarmed, Near(standby, StandbyOff) ? 1 : 0);
            Put(Flowbar, Near(standby, StandbyOn) ? 1 : 0);
        }
        Put(AcStandbyFail, 0);
        Put(DcStandbyFail, 0);
        double essentialFail = IsOn(ExternalPowerBreaker) ? 0 : 1;
        Put(AcEssentialFail, essentialFail);
        Put(DcEssentialFail, essentialFail);
    }

    private bool SourceOnLine() => IsOn(ExternalPowerBreaker) || GeneratorBreakers.Any(IsOn);

    private static int? TieNumber(string key) =>
        key.StartsWith(TieClosePrefix, StringComparison.Ordinal)
        && int.TryParse(key.AsSpan(TieClosePrefix.Length), out int n) && n is >= 1 and <= 3 ? n : null;

    /// <summary>A plain light lit (<see cref="L1011Annunciators.IsLit"/>: at or above 0.5).</summary>
    private bool IsLit(string lamp) => IsOn(lamp);

    /// <summary>A light lit or a relay closed: reads at or above 0.5.</summary>
    private bool IsOn(string key) => Value(key) is double v && v >= 0.5;

    private static bool Near(double value, double position) => Math.Abs(value - position) < 0.5;

    /// <summary>A change the aircraft makes: it never arrives for a key never delivered.</summary>
    private void Put(string key, double value)
    {
        if (!_neverDelivered.Contains(key))
            Vars[key] = value;
    }

    private double? Value(string key) => Vars.TryGetValue(key, out var v) ? v : null;
}
