using System.Collections.Concurrent;
using System.Diagnostics;
using MSFSBlindAssist.Aircraft;
using MSFSBlindAssist.Aircraft.L1011;
using MSFSBlindAssist.FirstOfficer;
using MSFSBlindAssist.FirstOfficer.L1011;
using MSFSBlindAssist.FirstOfficer.Models;
using MSFSBlindAssist.SimConnect;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;
using Item = MSFSBlindAssist.FirstOfficer.Models.ChecklistItem<MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor, MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;
using Step = MSFSBlindAssist.FirstOfficer.Models.FlowStep<MSFSBlindAssist.FirstOfficer.L1011.L1011FoStateEvaluator>;

namespace MSFSBlindAssist.Tests.FirstOfficer.L1011;

/// <summary>
/// The TriStar First Officer's Shutdown flow (the manual's securing), its action group and its read-back, pinned by
/// structure and by behaviour: both guards first, on the ground and engines stopped, read only, Stop ([FO-25]); the
/// emergency lights and galley off; the APU shut down in the manual's order, its stop held until the APU reads stopped,
/// the wait for it to stop to a standstill only after a stop this run sent, and its master off only once the stop is
/// done or found done; the packs off on the ground only; external power off; the fuel pumps off only with the APU not
/// turning; standby power, the radio masters and the battery last; every cut tick left alone unless it may go ahead; the
/// lines each step delivers ([FO-6], [FO-7]); the groups arrival groups ([FO-22]); and that every key is one the
/// definition takes, a toggle-carrying key only ever written over its own position read ([L1011-4]).
/// </summary>
public class L1011FoShutdownTests
{
    /// <summary>A real-time guard, only so a broken test fails instead of hanging; no test waits on it.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(10);

    private static List<FlowDefinition<L1011FoStateEvaluator>> Flows => L1011FoFlowDefinitions.Build();
    private static List<ChecklistGroup<X, L1011FoStateEvaluator>> Groups => L1011FoChecklistDefinitions.Build();
    private static FlowDefinition<L1011FoStateEvaluator> Shutdown => Flows.Single(f => f.Id == "SHUTDOWN");

    private static Step Of(string id) => Shutdown.Steps.Single(s => s.Id == id);
    private static ChecklistGroup<X, L1011FoStateEvaluator> Group(string id) => Groups.Single(g => g.Id == id);
    private static Item Line(string id) => Groups.SelectMany(g => g.Items).Single(i => i.Id == id);

    private static bool IsSwitch(Step s) =>
        s.ActionType is FlowStepActionType.SetSwitch or FlowStepActionType.SetSwitchMultiple;

    /// <summary>The (key, target) pairs a switch step writes.</summary>
    private static (string Key, int? Target)[] Actions(Step s) => s.ActionType switch
    {
        FlowStepActionType.SetSwitch => new[] { (s.EventName!, s.TargetValue) },
        FlowStepActionType.SetSwitchMultiple => s.MultiActions.Select(a => (a.EventName, a.TargetValue)).ToArray(),
        _ => Array.Empty<(string, int?)>(),
    };

    private static (string, int?)[] All(int target, params string[] keys) => keys.Select(k => (k, (int?)target)).ToArray();

    private const string Ground = "SIM_ON_GROUND";
    private const string ApuRpm = "L1011_RO_APU_RPM";
    private const string ApuMaster = "TOGGLE_APU_POWER";
    private const string ApuGenField = "L1011_RO_APU_GEN_FIELD";
    private const string ApuGenBreaker = "L1011_RO_APU_GEN_BREAKER";
    private const string ExtPowerBreaker = "L1011_RO_EXT_PWR_BREAKER";

    private const string PacksText = "Packs left on: not read on the ground.";
    private const string PumpsFlowText = "Fuel pumps left on: the APU is running.";
    private const string ParkedOnlyText = "Left alone: the aircraft must be on the ground with the engines stopped.";
    private const string ParkedApuOffText =
        "Left alone: the aircraft must be on the ground with the engines stopped and the APU shut down.";
    private const string MasterLeftOnText = "APU master left on: the APU did not stop.";

    private static readonly string[] FuelPumps =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    private static L1011FoStateEvaluator State(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.With(values));
        return evaluator;
    }

    private static L1011FoStateEvaluator Connected(params (string Key, double Value)[] values)
    {
        var evaluator = new L1011FoStateEvaluator();
        evaluator.SetSimConnect(SeededSimConnectCache.ConnectedWith(values));
        return evaluator;
    }

    /// <summary>Every engine's N3 gauge and fuel-and-ignition switch: stopped (a cold gauge reads 1.5) or running.</summary>
    private static IEnumerable<(string, double)> Engines(bool running)
    {
        for (int n = 1; n <= 3; n++)
        {
            yield return ($"L1011_RO_N3_{n}", running ? 59 : 1.5);
            yield return ($"TOGGLE_CUTOFF_ENG_{n}", running ? 1 : 0);
        }
    }

    /// <summary>Parked with every engine stopped, the APU's speed at <paramref name="rpm"/> (null: unread).</summary>
    private static L1011FoStateEvaluator Parked(double? rpm = null)
    {
        var seeds = Engines(running: false).Append((Ground, 1.0));
        return State((rpm is double r ? seeds.Append((ApuRpm, r)) : seeds).ToArray());
    }

    private static L1011FoStateEvaluator Airborne() => State(Engines(running: true).Append((Ground, 0)).Append((ApuRpm, 0)).ToArray());
    private static L1011FoStateEvaluator RunningOnTheGround() => State(Engines(running: true).Append((Ground, 1)).Append((ApuRpm, 0)).ToArray());

    // ------------------------------------------------------------------ the flow

    [Fact]
    public void Shutdown_NamesItselfAndItsChecklistGroups()
    {
        Assert.Equal("Shutdown", Shutdown.Name);
        Assert.Equal(new[] { "SHUTDOWN", "SHUTDOWN_CL" }, Shutdown.RelatedChecklistGroupIds);
        Assert.False(string.IsNullOrWhiteSpace(Shutdown.Description));
    }

    private static readonly string[] StepIds =
    {
        "SD_ON_GROUND", "SD_ENGINES_STOPPED", "SD_EMER_LIGHTS", "SD_GALLEY", "SD_APU_GEN_TRIP", "SD_APU_BLEED_OFF",
        "SD_APU_STOP", "SD_APU_STOPPED", "SD_APU_MASTER_OFF", "SD_PACKS_OFF", "SD_EXT_PWR_OFF", "SD_FUEL_PUMPS",
        "SD_STBY_POWER_OFF", "SD_RADIO_MASTERS_OFF", "SD_BATTERY_OFF",
    };

    [Fact]
    public void Shutdown_StepIdsAreInOrder_BothGuardsFirst_TheBatteryLast()
    {
        Assert.Equal(StepIds, Shutdown.Steps.Select(s => s.Id));
        Assert.Equal("SD_BATTERY_OFF", Shutdown.Steps[^1].Id);
    }

    /// <summary>[FO-25]: the flow cuts the pumps and the battery, so it opens with both guards, each a read-only Stop of
    /// 5 s that fails unread and links no line: started airborne or with an engine turning, it stops whole.</summary>
    [Fact]
    public void Both_guards_come_first_each_a_read_only_stop_that_links_nothing()
    {
        var (ground, engines) = (Shutdown.Steps[0], Shutdown.Steps[1]);
        Assert.Equal(("SD_ON_GROUND", "On the ground", "SIM_ON_GROUND"), (ground.Id, ground.Label, ground.ConditionFieldName));
        Assert.Equal(("SD_ENGINES_STOPPED", "Engines stopped", "FO_ENGINES_STOPPED"),
            (engines.Id, engines.Label, engines.ConditionFieldName));
        foreach (var guard in new[] { ground, engines })
        {
            Assert.Equal(FlowStepActionType.WaitForCondition, guard.ActionType);
            Assert.Equal(FlowStepFailurePolicy.Stop, guard.FailurePolicy);
            Assert.Equal(5, guard.TimeoutSeconds);
            Assert.Null(guard.SkipCondition);
            Assert.Empty(guard.LinkedChecklistItemIds);
            Assert.False(guard.Condition!(double.NaN), $"{guard.Id}: unread must fail");
            Assert.False(guard.Condition!(0));
            Assert.True(guard.Condition!(1));
        }
    }

    // ------------------------------------------------------------------ the writes

    private static readonly (string Id, string Label, (string Key, int? Target)[] Actions)[] Writes =
    {
        ("SD_EMER_LIGHTS", "Emergency lights: off", new (string, int?)[] { ("GUARD_EMER_LIGHTING", 1), ("TOGGLE_EMER_LIGHTING", 0) }),
        ("SD_GALLEY", "Galley power: off", All(0, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3")),
        ("SD_APU_GEN_TRIP", "APU generator breaker: trip", All(0, X.ApuGenerator)),
        ("SD_APU_BLEED_OFF", "APU bleed: off", All(0, "SWITCH_APU_BLEED")),
        ("SD_APU_STOP", "APU: stop", All(1, X.ApuStop)),
        ("SD_APU_MASTER_OFF", "APU master: off", All(0, ApuMaster)),
        ("SD_PACKS_OFF", "Packs: off", All(0, "SWITCH_PACK_FLOW_ENG_1", "SWITCH_PACK_FLOW_ENG_2", "SWITCH_PACK_FLOW_ENG_3")),
        ("SD_EXT_PWR_OFF", "External power: off", All(0, X.ExternalPower)),
        ("SD_FUEL_PUMPS", "Fuel pumps: all off", All(0, FuelPumps)),
        ("SD_STBY_POWER_OFF", "Standby power: off", new (string, int?)[] { ("GUARD_STBY_POWER", 1), ("TOGGLE_STBY_POWER", 0) }),
        ("SD_RADIO_MASTERS_OFF", "Radio master switches: off", All(0, "SWITCH_MASTER_RADIO_ESS", "SWITCH_MASTER_RADIO_NO2")),
        ("SD_BATTERY_OFF", "Battery: off", All(0, "TOGGLE_BATTERY")),
    };

    [Fact]
    public void TheWrites_HaveTheirLabelKeysAndTargetsWordForWord()
    {
        foreach (var (id, label, actions) in Writes)
        {
            var s = Of(id);
            Assert.Equal(label, s.Label);
            Assert.Equal(actions, Actions(s));
            Assert.Equal(actions.Length == 1 ? FlowStepActionType.SetSwitch : FlowStepActionType.SetSwitchMultiple, s.ActionType);
            Assert.Equal(FlowStepFailurePolicy.Skip, s.FailurePolicy);
            Assert.Null(s.TargetValueProvider);
            Assert.NotNull(s.SkipCondition);
        }
    }

    [Fact]
    public void EverySwitchStep_IsOneOfTheWritesAbove()
        => Assert.Equal(Writes.Select(w => w.Id).OrderBy(x => x, StringComparer.Ordinal),
            Shutdown.Steps.Where(IsSwitch).Select(s => s.Id).OrderBy(x => x, StringComparer.Ordinal));

    /// <summary>Every step's lines, from the table: a write its action line then the read-back line it sets (the packs
    /// their action line only), the guards and the APU wait none.</summary>
    private static readonly Dictionary<string, string[]> Links = new(StringComparer.Ordinal)
    {
        ["SD_ON_GROUND"] = Array.Empty<string>(),
        ["SD_ENGINES_STOPPED"] = Array.Empty<string>(),
        ["SD_EMER_LIGHTS"] = new[] { "SD_EMER_LIGHTS", "SDC_EMER_LIGHTS" },
        ["SD_GALLEY"] = new[] { "SD_GALLEY", "SDC_GALLEY" },
        ["SD_APU_GEN_TRIP"] = new[] { "SD_APU_GEN_TRIP", "SDC_APU_GEN_TRIP" },
        ["SD_APU_BLEED_OFF"] = new[] { "SD_APU_BLEED_OFF", "SDC_APU_BLEED" },
        ["SD_APU_STOP"] = new[] { "SD_APU_STOP", "SDC_APU_STOP" },
        ["SD_APU_STOPPED"] = Array.Empty<string>(),
        ["SD_APU_MASTER_OFF"] = new[] { "SD_APU_MASTER_OFF", "SDC_APU_MASTER" },
        ["SD_PACKS_OFF"] = new[] { "SD_PACKS_OFF" },
        ["SD_EXT_PWR_OFF"] = new[] { "SD_EXT_PWR_OFF", "SDC_EXT_PWR" },
        ["SD_FUEL_PUMPS"] = new[] { "SD_FUEL_PUMPS", "SDC_FUEL_PUMPS" },
        ["SD_STBY_POWER_OFF"] = new[] { "SD_STBY_POWER_OFF", "SDC_STBY_POWER" },
        ["SD_RADIO_MASTERS_OFF"] = new[] { "SD_RADIO_MASTERS_OFF", "SDC_RADIO_MASTERS" },
        ["SD_BATTERY_OFF"] = new[] { "SD_BATTERY_OFF", "SDC_BATTERY" },
    };

    [Fact]
    public void EveryStep_LinksExactlyTheTablesLines_ItsOwnLineFirst()
    {
        foreach (var s in Shutdown.Steps)
        {
            Assert.True(Links.TryGetValue(s.Id, out var expected), $"{s.Id} is not in the table");
            Assert.Equal(expected, s.LinkedChecklistItemIds);
            Assert.Equal(expected!.FirstOrDefault(), s.CompletesChecklistItemId);
        }
        Assert.Equal(StepIds.Length, Links.Count);
    }

    [Fact]
    public void EveryChecklistLine_IsDeliveredByOneStepOnly()
    {
        var linked = Shutdown.Steps.SelectMany(s => s.LinkedChecklistItemIds).ToList();
        Assert.Equal(linked.Count, linked.Distinct().Count());
    }

    // ------------------------------------------------------------------ "Already set", by behaviour

    private static double Wrong(double done) => done == 0 ? 1 : 0;

    /// <summary>The writes whose "Already set" reads only the switch, never its guard: the emergency lights and standby
    /// power are off whatever their guards read.</summary>
    private static readonly string[] GuardedSwitchWrites = { "SD_EMER_LIGHTS", "SD_STBY_POWER_OFF" };

    /// <summary>Every plain write is "Already set" only when every key reads its target, never with one wrong or unread;
    /// the composites and the two guarded switches are pinned below.</summary>
    [Fact]
    public void EveryPlainWrite_IsAlreadySetOnlyWhenEveryPartReadsDone()
    {
        int n = 0;
        foreach (var s in Shutdown.Steps.Where(x => IsSwitch(x) && !Actions(x).Any(a => X.IsPseudoKey(a.Key))
                                                 && !GuardedSwitchWrites.Contains(x.Id)))
        {
            n++;
            var done = Actions(s).Select(a => (a.Key, (double)a.Target!.Value)).ToArray();
            Assert.True(s.SkipCondition!(State(done)), $"{s.Id}: reads done, must be already set");
            Assert.False(s.SkipCondition!(State()), $"{s.Id}: nothing read, must not be already set");

            for (int i = 0; i < done.Length; i++)
            {
                var wrong = done.ToArray();
                wrong[i] = (wrong[i].Key, Wrong(wrong[i].Item2));
                Assert.False(s.SkipCondition!(State(wrong)), $"{s.Id}: {wrong[i].Key} reads wrong, must not be already set");

                var unread = done.Where((_, j) => j != i).ToArray();
                Assert.False(s.SkipCondition!(State(unread)), $"{s.Id}: {done[i].Key} unread, must not be already set");
            }
        }
        Assert.Equal(Writes.Length - 3 - GuardedSwitchWrites.Length, n);
    }

    [Theory]
    [InlineData("SD_EMER_LIGHTS", "TOGGLE_EMER_LIGHTING", "GUARD_EMER_LIGHTING")]
    [InlineData("SD_STBY_POWER_OFF", "TOGGLE_STBY_POWER", "GUARD_STBY_POWER")]
    public void A_guarded_switch_is_already_set_on_the_switch_alone(string id, string switchKey, string guardKey)
    {
        var skip = Of(id).SkipCondition!;
        Assert.True(skip(State((switchKey, 0))), "off, the guard unread");
        Assert.True(skip(State((switchKey, 0), (guardKey, 0))), "off under a closed guard");
        Assert.True(skip(State((switchKey, 0), (guardKey, 1))), "off under an open guard");
        Assert.False(skip(State((switchKey, 1), (guardKey, 1))), "armed");
        Assert.False(skip(State()), "unread");
    }

    /// <summary>The composites are "Already set" on what they deliver: the APU generator's breaker open, the APU's speed
    /// below 5 % (the stop's own "done"), external power's breaker open; none unread.</summary>
    [Fact]
    public void The_composites_are_already_set_on_what_they_deliver()
    {
        var trip = Of("SD_APU_GEN_TRIP").SkipCondition!;
        Assert.True(trip(State((ApuGenBreaker, 0))));
        Assert.False(trip(State((ApuGenBreaker, 1))));
        Assert.False(trip(State()));

        var stop = Of("SD_APU_STOP").SkipCondition!;
        Assert.True(stop(State((ApuRpm, 0))));
        Assert.True(stop(State((ApuRpm, 4.9))));
        Assert.False(stop(State((ApuRpm, 5))));
        Assert.False(stop(State((ApuRpm, 100))));
        Assert.False(stop(State()));

        var ext = Of("SD_EXT_PWR_OFF").SkipCondition!;
        Assert.True(ext(State((ExtPowerBreaker, 0))));
        Assert.False(ext(State((ExtPowerBreaker, 1))));
        Assert.False(ext(State()));
    }

    // ------------------------------------------------------------------ the APU

    /// <summary>The manual's order: the generator breaker tripped, the bleed off, the stop pressed and held until the APU
    /// reads stopped (live, about 60 s from 100 to 0), the wait for it to a standstill, then the master off.</summary>
    [Fact]
    public void The_APU_is_shut_down_in_the_manuals_order()
    {
        var ids = Shutdown.Steps.Select(s => s.Id).ToList();
        Assert.Equal(new[] { "SD_APU_GEN_TRIP", "SD_APU_BLEED_OFF", "SD_APU_STOP", "SD_APU_STOPPED", "SD_APU_MASTER_OFF" },
            ids.Skip(ids.IndexOf("SD_APU_GEN_TRIP")).Take(5));
        Assert.Equal(X.ApuStop, Of("SD_APU_STOP").EventName);
        Assert.Equal(1, Of("SD_APU_STOP").TargetValue);
    }

    /// <summary>
    /// The wait for the APU to a standstill runs only when this run sent the stop: an APU found stopped costs no wait.
    /// It reads the APU's speed below 1 %, so it agrees with the fuel pump cut's own rule (pumps kept while the APU reads
    /// 1 % or more); up to 150 s, then skipped aloud; read only, linking no line.
    /// </summary>
    [Fact]
    public void The_APU_wait_runs_only_when_this_run_sent_the_stop()
    {
        var wait = Of("SD_APU_STOPPED");
        Assert.Equal("APU to stop", wait.Label);
        Assert.Equal(FlowStepActionType.WaitForCondition, wait.ActionType);
        Assert.Equal(ApuRpm, wait.ConditionFieldName);
        Assert.Equal(150, wait.TimeoutSeconds);
        Assert.Equal(FlowStepFailurePolicy.Skip, wait.FailurePolicy);
        Assert.Equal("SD_APU_STOP", wait.RunOnlyIfStepSent);
        Assert.Null(wait.SkipCondition);
        Assert.Null(wait.RequiresStepId);
        Assert.Empty(wait.LinkedChecklistItemIds);
        Assert.True(wait.Condition!(0));
        Assert.True(wait.Condition!(0.9));
        Assert.False(wait.Condition!(1));
        Assert.False(wait.Condition!(4));
        Assert.False(wait.Condition!(double.NaN), "unread must not pass");
        // The same 1 % the pump cut keeps the pumps for.
        Assert.Equal(1, L1011FoStateEvaluator.ApuStartingRpm);
        Assert.Equal(new[] { "SD_APU_STOPPED" }, Shutdown.Steps.Where(s => s.RunOnlyIfStepSent != null).Select(s => s.Id));
    }

    /// <summary>The master goes off only once the stop is done or found done: a stop that did not take leaves the master
    /// on, saying so.</summary>
    [Fact]
    public void The_APU_master_requires_the_stop()
    {
        var master = Of("SD_APU_MASTER_OFF");
        Assert.Equal("SD_APU_STOP", master.RequiresStepId);
        Assert.Equal(MasterLeftOnText, master.RequiresStepSkipText);
        Assert.Equal(new[] { "SD_APU_MASTER_OFF" }, Shutdown.Steps.Where(s => s.RequiresStepId != null).Select(s => s.Id));
    }

    private static readonly string[] ApuTail = { "SD_APU_STOP", "SD_APU_STOPPED", "SD_APU_MASTER_OFF" };

    private static L1011FoStateEvaluator ApuReading(double rpm) => Connected((ApuRpm, rpm), (ApuMaster, 1));

    /// <summary>On a real flow manager: an APU found stopped is "Already set", not skipped, so the wait passes silently
    /// and the master still goes off.</summary>
    [Fact]
    public async Task An_APU_found_stopped_is_already_set_and_the_master_goes_off()
    {
        var run = await L1011FoFlowRun.RunAsync("SHUTDOWN", ApuTail, ApuReading(0));

        Assert.True(run.Finished);
        Assert.Contains("Already set: APU: stop", run.Speech);
        Assert.Empty(run.Skipped);
        Assert.Contains("SD_APU_STOPPED", run.Completed);
        Assert.DoesNotContain("SD_APU_STOPPED", run.Started);
        Assert.Equal(new[] { ApuMaster }, run.Sent);
    }

    /// <summary>On a real flow manager: a stop that is not done is skipped, the wait passes (nothing was sent), and the
    /// master is left on, aloud.</summary>
    [Fact]
    public async Task A_stop_not_done_leaves_the_master_on_aloud()
    {
        var run = await L1011FoFlowRun.RunAsync("SHUTDOWN", ApuTail, ApuReading(100), fail: new[] { X.ApuStop });

        Assert.True(run.Finished);
        Assert.Contains("Skipping: APU: stop", run.Speech);
        Assert.Contains(MasterLeftOnText, run.Speech);
        Assert.Equal(new[] { "SD_APU_STOP", "SD_APU_MASTER_OFF" }, run.Skipped);
        Assert.DoesNotContain(ApuMaster, run.Sent);
    }

    /// <summary>On a real flow manager: a stop this run sent starts the wait for the APU to a standstill (cancelled here,
    /// as a 150 s wait would otherwise run out).</summary>
    [Fact]
    public async Task A_stop_this_run_sent_starts_the_wait()
    {
        var run = await L1011FoFlowRun.RunAsync("SHUTDOWN", ApuTail, ApuReading(100), cancelWhenStarted: "SD_APU_STOPPED");

        Assert.Equal(new[] { X.ApuStop }, run.Sent);
        Assert.Contains("SD_APU_STOPPED", run.Started);
        Assert.DoesNotContain(ApuMaster, run.Sent);
    }

    // ------------------------------------------------------------------ the pump cut and the APU

    /// <summary>
    /// The tank 2 left pump feeds the APU from the moment it turns: the pump cut, the step and its tick, is left alone
    /// while the APU's speed reads 1 % or more, each saying so. Below it, or unread, the cut goes ahead parked; the tick is
    /// still refused airborne, unread or with an engine running.
    /// </summary>
    [Fact]
    public void The_pump_cut_is_left_alone_while_the_APU_runs()
    {
        var step = Of("SD_FUEL_PUMPS");
        var tick = Line("SD_FUEL_PUMPS");
        Assert.Equal(PumpsFlowText, step.LeaveAloneText);
        Assert.Equal(ParkedApuOffText, tick.LeaveAloneText);

        foreach (var leaves in new Func<L1011FoStateEvaluator, bool>[] { step.LeaveAloneWhen!, tick.LeaveAloneWhen! })
        {
            Assert.True(leaves(Parked(100)), "APU running");
            Assert.True(leaves(Parked(3)), "APU spooling up or down");
            Assert.True(leaves(Parked(1)), "APU at 1 %");
            Assert.False(leaves(Parked(0.9)), "APU not turning");
            Assert.False(leaves(Parked(0)), "APU stopped");
            Assert.False(leaves(Parked()), "APU unread: the cut goes ahead");
        }

        Assert.True(tick.LeaveAloneWhen!(Airborne()), "airborne");
        Assert.True(tick.LeaveAloneWhen!(State()), "nothing read");
        Assert.True(tick.LeaveAloneWhen!(RunningOnTheGround()), "engines running");
    }

    [Fact]
    public void The_pump_tick_through_the_checklist_manager_is_refused_aloud_with_the_APU_running()
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected(Engines(running: false).Append((Ground, 1)).Append((ApuRpm, 100)).ToArray()), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == "SD_FUEL_PUMPS");

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(ParkedApuOffText, spoken);
        Assert.False(line.IsChecked);
    }

    // ------------------------------------------------------------------ the other cut ticks ([FO-25])

    /// <summary>The packs, the step and its tick, are left alone off the ground: off in the air they depressurize the
    /// cabin.</summary>
    [Fact]
    public void The_packs_are_left_alone_off_the_ground()
    {
        foreach (var (leaves, text) in new[] { (Of("SD_PACKS_OFF").LeaveAloneWhen!, Of("SD_PACKS_OFF").LeaveAloneText),
                     (Line("SD_PACKS_OFF").LeaveAloneWhen!, Line("SD_PACKS_OFF").LeaveAloneText) })
        {
            Assert.Equal(PacksText, text);
            Assert.True(leaves(State((Ground, 0))), "airborne");
            Assert.True(leaves(State()), "the ground unread");
            Assert.False(leaves(State((Ground, 1))), "on the ground");
        }
    }

    public static IEnumerable<object[]> ParkedOnlyTicks() =>
        new[] { "SD_STBY_POWER_OFF", "SD_BATTERY_OFF" }.Select(id => new object[] { id });

    /// <summary>Standby power and the battery off: refused airborne, unread or with an engine running, going ahead parked
    /// with every engine stopped.</summary>
    [Theory]
    [MemberData(nameof(ParkedOnlyTicks))]
    public void The_standby_power_and_battery_ticks_go_ahead_only_parked(string id)
    {
        var tick = Line(id);
        Assert.Equal(ParkedOnlyText, tick.LeaveAloneText);
        Assert.True(tick.LeaveAloneWhen!(Airborne()), "airborne");
        Assert.True(tick.LeaveAloneWhen!(State()), "unread");
        Assert.True(tick.LeaveAloneWhen!(RunningOnTheGround()), "engines running");
        Assert.False(tick.LeaveAloneWhen!(Parked(100)), "parked, the APU running");
        Assert.False(tick.LeaveAloneWhen!(Parked()), "parked");
    }

    [Theory]
    [MemberData(nameof(ParkedOnlyTicks))]
    public void A_parked_only_tick_through_the_checklist_manager_is_refused_aloud_airborne(string id)
    {
        var groups = L1011FoChecklistDefinitions.Build();
        var mgr = new ChecklistManager<X, L1011FoStateEvaluator>(
            Connected(Engines(running: true).Append((Ground, 0)).ToArray()), new X(), groups);
        var line = groups.SelectMany(g => g.Items).Single(i => i.Id == id);

        Assert.False(mgr.ToggleItem(line.GroupId, line.Id, out var spoken));
        Assert.Equal(ParkedOnlyText, spoken);
        Assert.False(line.IsChecked);
    }

    /// <summary>In the flow only the packs and the pump cut are left alone (both guards stand before every cut); in the
    /// action group the packs, the pumps, standby power and the battery.</summary>
    [Fact]
    public void Only_the_cuts_are_left_alone()
    {
        Assert.Equal(new[] { "SD_PACKS_OFF", "SD_FUEL_PUMPS" }, Shutdown.Steps.Where(s => s.LeaveAloneWhen != null).Select(s => s.Id));
        Assert.Equal(new[] { "SD_PACKS_OFF", "SD_FUEL_PUMPS", "SD_STBY_POWER_OFF", "SD_BATTERY_OFF" },
            Group("SHUTDOWN").Items.Where(i => i.LeaveAloneWhen != null).Select(i => i.Id));
    }

    /// <summary>The sweep sees the pump, pack and battery cuts by their labels, and nothing else.</summary>
    [Fact]
    public void The_sweep_sees_the_pumps_the_packs_and_the_battery()
    {
        var kinds = Shutdown.Steps.Where(IsSwitch).Select(s => (s.Id, Kind: FoCutGuardSweepTests.CutKind(s.Label)))
            .Where(k => k.Kind != null).ToArray();
        Assert.Equal(new[] { ("SD_PACKS_OFF", (string?)"Packs"), ("SD_FUEL_PUMPS", "Pumps"), ("SD_BATTERY_OFF", "Pumps") }, kinds);
    }

    // ------------------------------------------------------------------ every field is served

    [Fact]
    public void Every_field_a_step_or_line_reads_is_one_the_evaluator_serves()
    {
        var vars = new IniL1011Definition().GetVariables();
        var synthetic = L1011FoStateEvaluator.SyntheticKeys.ToHashSet();
        var polled = new L1011FoStateEvaluator().OnRequestPollFields.ToHashSet();
        var fields = Shutdown.Steps.Select(s => s.ConditionFieldName).OfType<string>()
            .Concat(new[] { ApuRpm, ApuGenBreaker, ExtPowerBreaker, Ground })
            .Concat(Groups.Where(g => g.Id is "SHUTDOWN" or "SHUTDOWN_CL").SelectMany(g => g.Items)
                .Select(i => i.StateFieldName).OfType<string>())
            .Distinct().ToList();
        foreach (var field in fields)
        {
            if (synthetic.Contains(field)) continue;
            Assert.True(vars.TryGetValue(field, out var def), $"{field} is not registered");
            Assert.NotEqual(UpdateFrequency.Never, def!.UpdateFrequency);
            if (def.UpdateFrequency == UpdateFrequency.OnRequest)
                Assert.True(polled.Contains(field), $"OnRequest field {field} is not polled");
        }
        Assert.True(fields.Count >= 15, $"only {fields.Count} fields enumerated");
    }

    // ================================================================== the checklists

    [Fact]
    public void TheShutdownGroups_FollowParking_Named()
    {
        Assert.Equal(new[] { "SHUTDOWN", "SHUTDOWN_CL" }, Groups.Select(g => g.Id).SkipWhile(id => id != "SHUTDOWN").Take(2));
        Assert.Equal("PARKING_CL", Groups.Select(g => g.Id).TakeWhile(id => id != "SHUTDOWN").Last());
        Assert.Equal("SHUTDOWN_CL", Groups[^1].Id);
        Assert.Equal("Shutdown", Group("SHUTDOWN").Name);
        Assert.Equal("Securing", Group("SHUTDOWN_CL").Name);
    }

    /// <summary>[FO-22]: the arrival, never marked by an in-flight start.</summary>
    [Fact]
    public void EveryShutdownGroup_IsArrival_AndNeverDeparture()
    {
        foreach (var id in Shutdown.RelatedChecklistGroupIds)
        {
            Assert.True(FoDepartureGroups.Arrival.Contains(id), $"{id} is not an arrival group");
            Assert.False(FoDepartureGroups.Departure.Contains(id), $"{id} is a departure group too");
        }
    }

    [Fact]
    public void The_read_back_has_no_check_action()
        => Assert.All(Group("SHUTDOWN_CL").Items, i => Assert.True(i.CheckAction == null, $"{i.Id} has a CheckAction"));

    private static readonly string[] ActionLineIds =
    {
        "SD_EMER_LIGHTS", "SD_GALLEY", "SD_APU_GEN_TRIP", "SD_APU_BLEED_OFF", "SD_APU_STOP", "SD_APU_MASTER_OFF",
        "SD_PACKS_OFF", "SD_EXT_PWR_OFF", "SD_FUEL_PUMPS", "SD_STBY_POWER_OFF", "SD_RADIO_MASTERS_OFF", "SD_BATTERY_OFF",
    };

    /// <summary>The guards and the APU wait are flow-only.</summary>
    [Fact]
    public void Shutdown_ActionLines_InTheFlowsOrder()
    {
        Assert.Equal(ActionLineIds, Group("SHUTDOWN").Items.Select(i => i.Id));
        foreach (var id in new[] { "SD_ON_GROUND", "SD_ENGINES_STOPPED", "SD_APU_STOPPED" })
            Assert.DoesNotContain(Groups.SelectMany(g => g.Items), i => i.Id == id);
    }

    [Fact]
    public void Shutdown_ActionLabels_AreTheFlowStepLabels()
    {
        foreach (var i in Group("SHUTDOWN").Items)
            Assert.Equal(Of(i.Id).Label, i.Label);
    }

    [Fact]
    public void Shutdown_LineKinds()
    {
        foreach (var i in Group("SHUTDOWN").Items)
        {
            Assert.Equal(ChecklistItemType.AutoDetectable, i.Type);
            Assert.NotNull(i.CheckAction);
            Assert.True(i.AutoCompleteAllowed && i.ManualCompletionAllowed, i.Id);
            Assert.Equal(RevertBehavior.RevertToState, i.RevertBehavior);
            Assert.Empty(i.AdditionalStateFields);
        }
    }

    /// <summary>Each line reads the state its step's "Already set" reads.</summary>
    [Theory]
    [InlineData("SD_EMER_LIGHTS", "FO_EMER_LIGHTS_OFF", 1)]
    [InlineData("SD_GALLEY", "FO_GALLEY_OFF", 1)]
    [InlineData("SD_APU_GEN_TRIP", "FO_APU_GEN_OFF_LINE", 1)]
    [InlineData("SD_APU_BLEED_OFF", "SWITCH_APU_BLEED", 0)]
    [InlineData("SD_APU_STOP", "FO_APU_OFF", 1)]
    [InlineData("SD_APU_MASTER_OFF", "TOGGLE_APU_POWER", 0)]
    [InlineData("SD_PACKS_OFF", "FO_PACKS_OFF", 1)]
    [InlineData("SD_EXT_PWR_OFF", "FO_EXT_POWER_OFF", 1)]
    [InlineData("SD_FUEL_PUMPS", "FO_FUEL_PUMPS_OFF", 1)]
    [InlineData("SD_STBY_POWER_OFF", "TOGGLE_STBY_POWER", 0)]
    [InlineData("SD_RADIO_MASTERS_OFF", "FO_RADIO_MASTERS_OFF", 1)]
    [InlineData("SD_BATTERY_OFF", "TOGGLE_BATTERY", 0)]
    public void Shutdown_ActionLine_ReadsTheStateItsStepDelivers(string id, string field, double done)
    {
        var line = Line(id);
        Assert.Equal(field, line.StateFieldName);
        Assert.True(line.StateCondition!(done), $"{id}: {field} at {done} must read done");
        Assert.False(line.StateCondition!(double.NaN), $"{id}: {field} unread must not read done");
        var others = L1011FoStateEvaluator.SyntheticKeys.Contains(field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
        foreach (double other in others.Where(v => v != done))
            Assert.False(line.StateCondition!(other), $"{id}: {field} at {other} must not read done");
    }

    // ---- a hand tick does what the flow step does ----------------------------------------------------------------

    /// <summary>An aircraft with work for the step: each key the step writes the other way from its target; for the APU
    /// generator trip both relays closed; for the APU stop the APU running; for external power off the switch on and the
    /// breaker closed.</summary>
    private static FakeL1011Aircraft AircraftFor(Step step)
    {
        var ac = new FakeL1011Aircraft();
        foreach (var (key, target) in Actions(step).Where(a => !X.IsPseudoKey(a.Key)))
            ac.Set(key, target == 0 ? 1 : 0);
        if (step.EventName == X.ApuGenerator)
        {
            ac.Set(ApuGenField, 1);
            ac.Set(ApuGenBreaker, 1);
        }
        if (step.EventName == X.ApuStop)
        {
            ac.Set(ApuMaster, 1);
            ac.Set(ApuRpm, 100);
        }
        if (step.EventName == X.ExternalPower)
        {
            ac.Set("LAMP_EXTERNAL_POWER_AVAILABLE", 1);
            ac.Set("TOGGLE_EXT_PWR", 1);
            ac.Set(ExtPowerBreaker, 1);
        }
        return ac;
    }

    public static IEnumerable<object[]> ShutdownLinesWithACheckAction()
        => L1011FoChecklistDefinitions.Build().Single(g => g.Id == "SHUTDOWN").Items.Where(i => i.CheckAction != null)
            .Select(i => new object[] { i.Id });

    /// <summary>Each action line's tick sends what its flow step sends, in the same order, from the same aircraft; the
    /// APU stop's tick presses the stop button once and holds until the APU reads stopped, as the step does.</summary>
    [Theory]
    [MemberData(nameof(ShutdownLinesWithACheckAction))]
    public async Task HandTick_SendsExactlyWhatItsFlowStepSends(string id)
    {
        var step = Of(id);

        var byStep = AircraftFor(step);
        var stepExec = new X();
        stepExec.SetTransport(byStep);
        Assert.True(await stepExec.ExecuteStepAsync(step).WaitAsync(Hang), $"{id}: the flow step was not done");

        var byTick = AircraftFor(step);
        var tickExec = new X();
        tickExec.SetTransport(byTick);
        await Line(id).CheckAction!(tickExec, State()).WaitAsync(Hang);

        static List<(FakeL1011IoKind, string, double?)> Sent(FakeL1011Aircraft ac) =>
            ac.Io.Where(e => e.Kind != FakeL1011IoKind.FreshRead).Select(e => (e.Kind, e.Key, e.Value)).ToList();
        Assert.NotEmpty(Sent(byStep));
        Assert.Equal(Sent(byStep), Sent(byTick));
        if (step.EventName == X.ApuStop)
        {
            Assert.Equal(new[] { ("APU_STOP", 1.0) }, byTick.Writes);
            Assert.True(byTick.Get(ApuRpm) < L1011FoStateEvaluator.ApuStoppedRpm, "the tick held until the APU read stopped");
        }
    }

    // ---- the read-back, word for word ------------------------------------------------------------------------------

    /// <summary>A read-back line: its label, and its field and done value.</summary>
    private sealed record ReadBack(string Id, string Label, string Field, double Done = 1);

    private static readonly ReadBack[] Securing =
    {
        new("SDC_EMER_LIGHTS", "Emergency exit lights: off", "FO_EMER_LIGHTS_OFF"),
        new("SDC_STBY_POWER", "Standby power: off", "TOGGLE_STBY_POWER", 0),
        new("SDC_RADIO_MASTERS", "Master radio switches: off", "FO_RADIO_MASTERS_OFF"),
        new("SDC_GALLEY", "Galley power: off", "FO_GALLEY_OFF"),
        new("SDC_APU_GEN_TRIP", "APU generator breaker: trip", "FO_APU_GEN_OFF_LINE"),
        new("SDC_APU_BLEED", "APU bleed air shutoff valve: unlatch", "SWITCH_APU_BLEED", 0),
        new("SDC_APU_STOP", "APU stop button: press", "FO_APU_OFF"),
        new("SDC_APU_MASTER", "APU master power: off", "TOGGLE_APU_POWER", 0),
        new("SDC_EXT_PWR", "External power switch: off", "FO_EXT_POWER_OFF"),
        new("SDC_FUEL_PUMPS", "Fuel tank pumps: off", "FO_FUEL_PUMPS_OFF"),
        new("SDC_BATTERY", "Battery switch: off", "TOGGLE_BATTERY", 0),
    };

    [Fact]
    public void The_read_back_is_word_for_word()
        => Assert.Equal(Securing.Select(l => (l.Id, l.Label)), Group("SHUTDOWN_CL").Items.Select(i => (i.Id, i.Label)));

    [Fact]
    public void EveryReadBackLine_ReadsItsFieldAtItsValue()
    {
        foreach (var rb in Securing)
        {
            var line = Line(rb.Id);
            Assert.Null(line.CheckAction);
            Assert.Empty(line.AdditionalStateFields);
            Assert.Equal(ChecklistItemType.AutoDetectable, line.Type);
            Assert.Equal(RevertBehavior.RevertToState, line.RevertBehavior);
            Assert.Equal(rb.Field, line.StateFieldName);
            Assert.True(line.StateCondition!(rb.Done), $"{rb.Id}: {rb.Field} at {rb.Done} must read done");
            Assert.False(line.StateCondition!(double.NaN), $"{rb.Id}: unread must not read done");
            var others = L1011FoStateEvaluator.SyntheticKeys.Contains(rb.Field) ? new[] { 0.0 } : new[] { 0.0, 1, 2, 3, 4, 5 };
            foreach (double other in others.Where(v => v != rb.Done))
                Assert.False(line.StateCondition!(other), $"{rb.Id}: {rb.Field} at {other} must not read done");
        }
    }

    /// <summary>A finished run latches the action group and the read-back, and nothing else.</summary>
    [Fact]
    public void Shutdown_LatchesItsTwoGroups()
        => Assert.Equal(Shutdown.RelatedChecklistGroupIds, Shutdown.CompletionGroupIds(id => Groups.Any(g => g.Id == id)));

    /// <summary>The labels, and the APU wait's 1 %, read the same under a comma-decimal culture.</summary>
    [Theory]
    [InlineData("de-DE")]
    [InlineData("sv-SE")]
    public void Labels_and_the_APU_wait_hold_under_a_comma_decimal_culture(string culture)
    {
        var (labels, standstill) = TestCulture.Run(culture, () =>
        {
            var flow = L1011FoFlowDefinitions.Build().Single(f => f.Id == "SHUTDOWN");
            var all = flow.Steps.Select(s => s.Label).Concat(flow.Steps.Select(s => s.LeaveAloneText ?? ""))
                .Concat(L1011FoChecklistDefinitions.Build().SelectMany(g => g.Items).Select(i => i.Label)).ToList();
            return (all, flow.Steps.Single(s => s.Id == "SD_APU_STOPPED").Condition!(0.5));
        });
        Assert.Contains("APU bleed air shutoff valve: unlatch", labels);
        Assert.Contains(PumpsFlowText, labels);
        Assert.True(standstill);
    }

    // ================================================================== what Shutdown writes

    private static readonly Lazy<IniL1011Definition> Definition = new(() =>
    {
        var def = new IniL1011Definition();
        def.GetVariables();
        return def;
    });

    private static readonly Lazy<Dictionary<string, L1011PlacedRow>> Rows = new(() =>
    {
        var rows = new Dictionary<string, L1011PlacedRow>(StringComparer.Ordinal);
        foreach (var panel in L1011PanelLayout.Place(L1011ControlMap.Load(), L1011Levers.Keys).RowsByPanel.Values)
            foreach (var row in panel)
                rows[row.Key] = row;
        return rows;
    });

    private static IEnumerable<(string Step, string Key, int? Target)> EveryWrittenKey()
        => Shutdown.Steps.Where(IsSwitch).SelectMany(s => Actions(s).Select(a => (s.Id, a.Key, a.Target)));

    [Fact]
    public void Every_key_is_a_pseudo_key_or_accepted_by_the_definition()
    {
        var rejected = EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key) && !Definition.Value.FoAcceptsKey(w.Key))
            .Select(w => $"{w.Step}: {w.Key}").ToList();
        Assert.True(rejected.Count == 0, "would silently do nothing: " + string.Join(", ", rejected));
        Assert.Equal(26, EveryWrittenKey().Count());
    }

    /// <summary>The composites are the APU generator trip, the APU stop (its press and its spin-down wait) and external
    /// power off; no lever; no step presses a button row itself.</summary>
    [Fact]
    public void The_composites_and_no_lever()
    {
        Assert.Equal(new[] { X.ApuGenerator, X.ApuStop, X.ExternalPower }, EveryWrittenKey().Select(w => w.Key).Where(X.IsPseudoKey).Distinct());
        Assert.DoesNotContain(EveryWrittenKey(), w => L1011Levers.IsLever(w.Key));
        Assert.DoesNotContain(EveryWrittenKey(), w => !X.IsPseudoKey(w.Key) && Rows.Value[w.Key].Action == L1011RowAction.Press);
    }

    private static IEnumerable<(string Step, string Key, int Target)> PlainSwitchWrites()
        => EveryWrittenKey().Where(w => !X.IsPseudoKey(w.Key)).Select(w => (w.Step, w.Key, w.Target!.Value));

    /// <summary>Every other key is a plain switch row, and its target one of the control's positions.</summary>
    [Fact]
    public void EveryOtherKey_IsAPlainSwitchRow_AtOneOfItsPositions()
    {
        foreach (var (step, key, target) in PlainSwitchWrites())
        {
            Assert.True(Rows.Value.TryGetValue(key, out var row), $"{step}: {key} is not a panel row");
            Assert.Equal(L1011RowAction.Set, row!.Action);
            Assert.Equal(L1011Kinds.Switch, row.Control!.Kind);
            var plan = L1011WritePlan.ForSet(row.Control!, target, current: target == 0 ? 1 : 0);
            Assert.True(plan.Refusal is null, $"{step}: {key} -> {target} is refused ({plan.Refusal})");
        }
    }

    /// <summary>The keys whose click replay carries a TOGGLE K: event, refused from an unknown position ([L1011-4]).</summary>
    private static readonly string[] ToggleCarrying = { "SWITCH_APU_BLEED" };

    /// <summary>
    /// A toggle-carrying key is written only by a single-switch step whose "Already set" reads that key at the target:
    /// the executor reads it first, and from an unknown position the definition refuses the write. Planned from nothing
    /// read, every other key must still be a plan.
    /// </summary>
    [Fact]
    public void No_step_writes_a_toggle_carrying_key_from_an_unread_position_except_through_a_skip()
    {
        var toggles = new List<string>();
        foreach (var (step, key, target) in PlainSwitchWrites())
        {
            var plan = L1011WritePlan.ForSet(Rows.Value[key].Control!, target, current: null);
            Assert.NotEqual(L1011WritePlan.NotAPositionRefusal, plan.Refusal);
            if (plan.Refusal is null) continue;
            Assert.Equal(L1011WritePlan.UnknownPositionRefusal, plan.Refusal);
            toggles.Add(key);

            var s = Of(step);
            Assert.Equal(FlowStepActionType.SetSwitch, s.ActionType);
            int other = target == 0 ? 1 : 0;
            Assert.True(s.SkipCondition!(State((key, target))), $"{step}: {key} at its target must be already set");
            Assert.False(s.SkipCondition!(State((key, other))), $"{step}: {key} the other way must not be already set");
            Assert.False(s.SkipCondition!(State()), $"{step}: {key} unread must not be already set");
        }
        Assert.Equal(ToggleCarrying, toggles);
    }
}

/// <summary>
/// Runs part of a real TriStar flow on a real <see cref="FlowManager{TExec,TState}"/> over the real evaluator, with an
/// executor that accepts every write except the keys it is told to fail (the real one needs the sim): what was spoken,
/// started, completed, skipped and sent. A run must finish by itself within 15 s, unless it is cancelled 300 ms after a
/// named step starts (a long wait that would otherwise run out).
/// </summary>
internal static class L1011FoFlowRun
{
    internal sealed record Result(List<string> Speech, List<string> Started, List<string> Completed, List<string> Skipped,
        string[] Sent, bool Finished);

    private sealed class Executor : IFoActionExecutor
    {
        private readonly HashSet<string> _fails;
        public Executor(IEnumerable<string> fails) => _fails = new HashSet<string>(fails, StringComparer.Ordinal);
        public ConcurrentQueue<string> Sent { get; } = new();
        public bool IsAvailable => true;

        public Task<bool> ExecuteStepAsync(IFlowStepDispatch step)
        {
            var keys = step.ActionType == FlowStepActionType.SetSwitchMultiple
                ? step.MultiActions.Select(a => a.EventName).ToArray()
                : new[] { step.EventName ?? "" };
            bool ok = true;
            foreach (var key in keys)
            {
                if (_fails.Contains(key)) { ok = false; continue; }
                Sent.Enqueue(key);
            }
            return Task.FromResult(ok);
        }

        public Task WaitForDispatchDrainAsync() => Task.CompletedTask;
    }

    internal static async Task<Result> RunAsync(string flowId, string[] stepIds, L1011FoStateEvaluator state,
        IEnumerable<string>? fail = null, string? cancelWhenStarted = null)
    {
        var source = L1011FoFlowDefinitions.Build().Single(f => f.Id == flowId);
        var flow = new FlowDefinition<L1011FoStateEvaluator>
        {
            Id = source.Id,
            Name = source.Name,
            RelatedChecklistGroupIds = source.RelatedChecklistGroupIds,
            Steps = source.Steps.Where(s => stepIds.Contains(s.Id)).ToList(),
        };
        Assert.Equal(stepIds, flow.Steps.Select(s => s.Id).ToArray());

        var executor = new Executor(fail ?? Array.Empty<string>());
        var speech = new GatedSpeechCapture();
        var checklist = new ChecklistManager<Executor, L1011FoStateEvaluator>(state, executor,
            new List<ChecklistGroup<Executor, L1011FoStateEvaluator>>());
        var flows = new FlowManager<Executor, L1011FoStateEvaluator>(state, executor, checklist, speech);
        var started = new List<string>();
        var completed = new List<string>();
        var skipped = new List<string>();
        flows.StepStarted += (_, step, _) =>
        {
            lock (started) started.Add(step.Id);
            if (step.Id == cancelWhenStarted) _ = Task.Delay(300).ContinueWith(_ => flows.Cancel());
        };
        flows.StepCompleted += (_, step, _) => { lock (completed) completed.Add(step.Id); };
        flows.StepSkipped += (_, step, _) => { lock (skipped) skipped.Add(step.Id); };

        flows.StartFlow(flow);
        var sw = Stopwatch.StartNew();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(15))
            await Task.Delay(20);
        bool finished = !flows.IsRunning;
        flows.Cancel();
        while (flows.IsRunning && sw.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(20);

        return new Result(speech.All.ToList(), started, completed, skipped, executor.Sent.ToArray(), finished);
    }
}
