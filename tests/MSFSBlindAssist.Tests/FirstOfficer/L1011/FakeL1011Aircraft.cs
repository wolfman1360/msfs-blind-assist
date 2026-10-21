using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer.L1011;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>What reached the fake TriStar.</summary>
internal enum FakeL1011IoKind { Write, Altimeters, FreshRead, CabinBaro, CabinAltitudeStep }

/// <summary>One thing that reached the fake, in order. <see cref="Value"/>: a write's value, the altimeter
/// or cabin baro setting in hectopascals, the cabin altitude selector's step, or what a fresh read
/// delivered (null when nothing was). <see cref="AtMs"/>: the fake's clock when it happened (a fresh read:
/// when it was answered).</summary>
internal readonly record struct FakeL1011Io(FakeL1011IoKind Kind, string Key, double? Value, long AtMs);

/// <summary>
/// A fake TriStar for the First Officer's executor tests: the definition's answers over a dictionary of
/// readings, and the aircraft semantics seen on the live aircraft.
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
/// <item>The APU: a start press (<c>SWITCH_APU_START</c>) with the master (<c>TOGGLE_APU_POWER</c>) reading 1
/// sets its RPM (<c>L1011_RO_APU_RPM</c>) to <see cref="ApuRpmAfterStart"/>, with the master off or unread
/// nothing; the stop press (<c>APU_STOP</c>) sets the RPM to 0. The generator field press
/// (<c>SWITCH_APU_BRG</c>) closes its relay (<c>L1011_RO_APU_GEN_FIELD</c>) only with the APU running (95 %);
/// the breaker press (<c>SWITCH_GND_FLOW</c>) closes the breaker relay (<c>L1011_RO_APU_GEN_BREAKER</c>) only
/// over a closed field, whatever external power does (no interlock); the trip (<c>SWITCH_GND_FLOW_OPEN</c>)
/// opens it. The buttons hold no position.</item>
/// <item>The standby lights as the aircraft showed them: the unarmed light lit only with the switch Off, the
/// flowbar lit only with it On (neither read while the switch is unread), both standby bus fail lights always
/// out, both essential bus fail lights lit unless the external power breaker reads closed. The battery
/// changes none of them.</item>
/// <item>The fire tests as the aircraft showed them: with all four fire loop selectors
/// (<c>ROTARY_FIRE_DETECT_TEST_ENG_1..3</c>, <c>_APU</c>) at Both (2), both loop test switches
/// (<c>SWITCH_FIRE_DETECT_TEST_A</c> and <c>_B</c>) on together light the three engine fire lights, the engine fire
/// warning and the APU fire light; otherwise they read out. Live, on APU and external power with the engines off,
/// A alone lit nothing and B alone lit all five; the fake asks for both, so a test that turns on one switch fails. The wheel well fire light (<c>LAMP_FIRE_WHEEL_WELL</c>) lights
/// only with its selector (<c>ROTARY_WHEEL_WELL_TEST</c>) at Both (1) and both its test switches on (A alone lit
/// nothing). Each light stays unread until one of its own selectors or test switches reads.</item>
/// <item>Hydraulics as on external power, but at once: system B (<c>L1011_RO_HYD_B</c>) reads
/// <see cref="PumpPsi"/> while its AC pump (<c>SWITCH_HYD_AC_PUMP_B3</c>) is on, system C likewise from
/// <c>_C3</c>; systems A and D read <see cref="PtuPsi"/> while both power transfer units (<c>SWITCH_PTU_AB</c>,
/// <c>_CD</c>) are on and systems B and C read at least <see cref="PressurizedPsi"/>; anything else reads
/// <see cref="DepressurizedPsi"/>. A test wanting a slow rise or a stuck pressure uses <see cref="NeverDeliver"/>
/// then <see cref="Set"/>.</item>
/// <item>The altimeters set together set the three altimeter readouts, the setting read as the definition
/// reads it (inches taken too; out of range refused). The cabin baro sets its own readout
/// (<c>L1011_RO_CABIN_BARO</c>), read the same way.</item>
/// <item>The cabin altitude selector turned to a step (0 to 100, else refused as the definition refuses it)
/// sets the selected cabin altitude readout to the step's altitude (the knob table,
/// <see cref="L1011Pressurization.CabinAltitudeForStep"/>) in METRES, as SimConnect delivers
/// <c>L:TARGET_CABIN_ALTITUDE</c>.</item>
/// </list>
/// Failure injection: <see cref="Refuse"/> and <see cref="NeverDeliver"/>. Time moves only through
/// <see cref="DelayAsync"/>, which a fresh read also waits through; a test scripts what the aircraft does
/// as time passes with <see cref="OnClock"/>. Every member is safe from any thread.
/// </summary>
internal sealed class FakeL1011Aircraft : IL1011FoTransport
{
    /// <summary>The reason a write to a <see cref="Refuse"/>d key is refused with.</summary>
    public const string RefusalReason = "refused by the test";

    /// <summary>The key the altimeter writes are logged under.</summary>
    public const string AltimetersIoKey = "ALTIMETERS";

    /// <summary>The keys the cabin baro and cabin altitude selector writes are logged under.</summary>
    public const string CabinBaroIoKey = "CABIN_BARO";
    public const string CabinAltitudeIoKey = "CABIN_ALTITUDE_SELECTOR";

    private const string CabinBaroReadout = "L1011_RO_CABIN_BARO";
    private const string CabinAltitudeReadout = "L1011_RO_CABIN_ALT_SELECTED";
    private const double MetresPerFoot = 0.3048;

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
    private const string ApuMaster = "TOGGLE_APU_POWER";
    private const string ApuStartPress = "SWITCH_APU_START";
    private const string ApuStopPress = "APU_STOP";
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuGenFieldPress = "SWITCH_APU_BRG";
    private const string ApuGenBreakerPress = "SWITCH_GND_FLOW";
    private const string ApuGenBreakerTrip = "SWITCH_GND_FLOW_OPEN";
    private const string ApuGenField = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";

    private const double StandbyOff = 0, StandbyArm = 1, StandbyOn = 2;

    private static readonly string[] FireLoopSelectors =
    {
        "ROTARY_FIRE_DETECT_TEST_ENG_1", "ROTARY_FIRE_DETECT_TEST_ENG_2", "ROTARY_FIRE_DETECT_TEST_ENG_3", "ROTARY_FIRE_DETECT_TEST_APU",
    };
    private static readonly string[] FireTestSwitches = { "SWITCH_FIRE_DETECT_TEST_A", "SWITCH_FIRE_DETECT_TEST_B" };
    private static readonly string[] FireLamps =
    {
        "LAMP_ENG_FIRE_1", "LAMP_ENG_FIRE_2", "LAMP_ENG_FIRE_3", "LAMP_ENG_FIRE", "LAMP_FIRE_APU",
    };
    private const double FireLoopsBoth = 2;

    private const string WheelWellSelector = "ROTARY_WHEEL_WELL_TEST";
    private static readonly string[] WheelWellTestSwitches = { "SWITCH_WHEEL_WELL_TEST_A", "SWITCH_WHEEL_WELL_TEST_B" };
    private const string WheelWellFire = "LAMP_FIRE_WHEEL_WELL";
    private const double WheelWellBoth = 1;

    private const string HydPumpB = "SWITCH_HYD_AC_PUMP_B3";
    private const string HydPumpC = "SWITCH_HYD_AC_PUMP_C3";
    private const string PtuAb = "SWITCH_PTU_AB";
    private const string PtuCd = "SWITCH_PTU_CD";
    private const string HydA = "L1011_RO_HYD_A", HydB = "L1011_RO_HYD_B", HydC = "L1011_RO_HYD_C", HydD = "L1011_RO_HYD_D";

    /// <summary>System B or C with its AC pump on (live: about 2,650 psi a few seconds after the switch).</summary>
    public const double PumpPsi = 2650;

    /// <summary>Systems A and D with both power transfer units on over B and C pressurized (live: past 2,600 in 10 s).</summary>
    public const double PtuPsi = 2600;

    /// <summary>A system depressurized (live: every system back to 14.5 psi within 8 s of everything off).</summary>
    public const double DepressurizedPsi = 14.5;

    /// <summary>System B or C drives its power transfer unit at or above this.</summary>
    public const double PressurizedPsi = 2000;

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

    /// <summary>The APU's RPM right after a start press with the master on: running (100) by default; a test
    /// scripting the spool-up sets 0 and drives the RPM through <see cref="OnClock"/>.</summary>
    public double ApuRpmAfterStart { get; set; } = 100;

    /// <summary>The readings, by key; a key absent is unread. Seed through <see cref="Set"/>.</summary>
    public Dictionary<string, double> Vars { get; } = new(StringComparer.Ordinal);

    /// <summary>Every write sent, altimeter, cabin baro and cabin altitude selector write, and fresh read, in order.</summary>
    public List<FakeL1011Io> Io { get; } = new();

    /// <summary>
    /// A test-only script of what the aircraft does as time passes (an engine's N3 rising): run inside
    /// every <see cref="DelayAsync"/>, a fresh read's wait included, once the clock has moved, with the
    /// fake, whose <see cref="NowMs"/> is the new time. It runs under the fake's lock: seed through
    /// <see cref="Set"/>, never wait on another thread.
    /// </summary>
    public Action<FakeL1011Aircraft>? OnClock { get; set; }

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

    public Task<L1011FoResult> SetCabinBaroAsync(double millibars)
    {
        lock (_sync)
        {
            if (!Ready)
                return Task.FromResult(L1011FoResult.Unavailable);
            if (L1011Levers.AltimeterMillibars(millibars) is not double setting)
                return Task.FromResult(L1011FoResult.Refused(L1011Levers.AltimeterRangeError));
            Io.Add(new(FakeL1011IoKind.CabinBaro, CabinBaroIoKey, setting, _clock));
            Put(CabinBaroReadout, setting);
            Recompute();
            return Task.FromResult(L1011FoResult.Sent);
        }
    }

    public Task<L1011FoResult> SetCabinAltitudeSelectorAsync(int step)
    {
        lock (_sync)
        {
            if (!Ready)
                return Task.FromResult(L1011FoResult.Unavailable);
            if (step < 0 || step > IniL1011Definition.CabinAltitudeSelectorMaxStep)
                return Task.FromResult(L1011FoResult.Refused(IniL1011Definition.CabinAltitudeSelectorRangeError));
            Io.Add(new(FakeL1011IoKind.CabinAltitudeStep, CabinAltitudeIoKey, step, _clock));
            Put(CabinAltitudeReadout, L1011Pressurization.CabinAltitudeForStep(step) * MetresPerFoot);
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
        lock (_sync)
        {
            _clock += Math.Max(0, ms);
            OnClock?.Invoke(this);
        }
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
        else if (key == ApuStartPress)
        {
            if (Value(ApuMaster) is double master && Near(master, 1))
                Put(ApuRpm, ApuRpmAfterStart);
        }
        else if (key == ApuStopPress)
            Put(ApuRpm, 0);
        else if (key == ApuGenFieldPress)
        {
            if (Value(ApuRpm) is double rpm && rpm >= L1011FoStateEvaluator.ApuRunningRpm)
                Put(ApuGenField, 1);
        }
        else if (key == ApuGenBreakerPress)
        {
            if (IsOn(ApuGenField))
                Put(ApuGenBreaker, 1);
        }
        else if (key == ApuGenBreakerTrip)
            Put(ApuGenBreaker, 0);
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

        // The fire tests: a light reads once any of its own selectors or test switches does.
        if (FireLoopSelectors.Concat(FireTestSwitches).Any(k => Value(k) is not null))
        {
            bool fireLit = FireLoopSelectors.All(k => Value(k) is double v && Near(v, FireLoopsBoth))
                && FireTestSwitches.All(IsOn);
            foreach (var lamp in FireLamps)
                Put(lamp, fireLit ? 1 : 0);
        }
        if (WheelWellTestSwitches.Append(WheelWellSelector).Any(k => Value(k) is not null))
        {
            bool wheelWellLit = Value(WheelWellSelector) is double s && Near(s, WheelWellBoth)
                && WheelWellTestSwitches.All(IsOn);
            Put(WheelWellFire, wheelWellLit ? 1 : 0);
        }

        // Hydraulics: B and C from their own pumps first, then A and D from what B and C read.
        Put(HydB, IsOn(HydPumpB) ? PumpPsi : DepressurizedPsi);
        Put(HydC, IsOn(HydPumpC) ? PumpPsi : DepressurizedPsi);
        bool ptusDriven = IsOn(PtuAb) && IsOn(PtuCd)
            && Value(HydB) >= PressurizedPsi && Value(HydC) >= PressurizedPsi;
        Put(HydA, ptusDriven ? PtuPsi : DepressurizedPsi);
        Put(HydD, ptusDriven ? PtuPsi : DepressurizedPsi);
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
