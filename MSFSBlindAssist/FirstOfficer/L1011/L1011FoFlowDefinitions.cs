using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.L1011;

using Flow = Models.FlowDefinition<L1011FoStateEvaluator>;
using Step = Models.FlowStep<L1011FoStateEvaluator>;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer flows, following the aircraft's manual checklist. Part 1
/// is the Power Up flow. Every switch step but the standby power check carries a SkipCondition on its own
/// state ("Already set"; the check is the point, so it always runs), and the executor re-reads before
/// acting anyway. Captain steps are spoken reminders, never automated.
///
/// Power Up is a cold-and-dark flow that switches the fuel pumps and hydraulic pumps off and, in its
/// standby power check, turns the battery off for a moment ([FO-25]): it opens with BOTH guards, engines
/// stopped and on the ground, each a read-only Stop that links no line and fails unread, so a flow started
/// with an engine turning or in flight stops whole instead of working through its steps one by one. The
/// guards stay first; the standby power check stays linked to the four lines it delivers and never to the
/// electrical meter selector lines, which are the pilot's ([FO-7]).
///
/// Every read-back (PUC_*) line the flow latches has a delivering step ([FO-6], [FO-7]): a write completes
/// its own action line and the read-back line it sets (<see cref="Also"/>), and a read-only
/// <see cref="Check"/> completes a line no write of the flow sets. Power Up writes no lever, no typed value
/// and no control whose click replay carries a toggle K: event, which the definition refuses from an
/// unknown position ([L1011-4]); the tests pin all three.
/// </summary>
public static class L1011FoFlowDefinitions
{
    public static List<Flow> Build() => new()
    {
        BuildPowerUp(),
    };

    // ------------------------------------------------------------------ Power Up (the manual's cockpit entry and power-up)
    private static Flow BuildPowerUp() => new()
    {
        Id = "POWER_UP", Name = "Power Up",
        Description = "Fire handle, gear, speed brake, flap and fuel checks; radios, transponder, radar, pumps, jettison, "
            + "passenger oxygen and galley switches; standby power check; essential bus selector; external power; INS align.",
        RelatedChecklistGroupIds = new[] { "POWER_UP", "POWER_UP_CL" },
        Steps = new()
        {
            // A cold-and-dark flow: started with an engine turning, the next steps would switch pumps off
            // under it. Stop instead...
            EnginesStoppedGuard("PU_ENGINES_STOPPED"),
            // ...and never airborne (FO-25). Links no line.
            OnGroundGuard("PU_ON_GROUND"),

            // What the First Officer finds and never moves: read only. Each completes only its read-back line.
            Check("PU_CHK_FIRE_HANDLES", "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", On, CheckSeconds, "PUC_FIRE_HANDLES"),
            Check("PU_CHK_GEAR", "Gear lever: down", "FO_GEAR_LEVER_DOWN", On, CheckSeconds, "PUC_GEAR"),
            Check("PU_CHK_SPEED_BRAKE", "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", On, CheckSeconds, "PUC_SPEED_BRAKE"),
            Check("PU_CHK_FLAPS", "Flaps and flap handle: agree", "FO_FLAPS_AGREE", On, CheckSeconds, "PUC_FLAPS"),
            Check("PU_CHK_FUEL_IGN", "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On, CheckSeconds, "PUC_FUEL_IGN"),

            Also(Done(Skip(SW("PU_CONT_IGN", "Continuous ignition: off", "SWITCH_CONT_IGNITION", 0),
                s => s.IsPosition("SWITCH_CONT_IGNITION", 0)), "PU_CONT_IGN"), "PUC_CONT_IGN"),
            Also(Done(Skip(Multi("PU_RADIO_MASTERS", "Radio master switches: on",
                    ("SWITCH_MASTER_RADIO_ESS", 1), ("SWITCH_MASTER_RADIO_NO2", 1)),
                s => s.IsOn("FO_RADIO_MASTERS_ON")), "PU_RADIO_MASTERS"), "PUC_RADIO_MASTERS"),
            Also(Done(Skip(SW("PU_XPDR", "Transponder: standby", "ROTARY_TCAS_MODE", 1),
                s => s.IsPosition("ROTARY_TCAS_MODE", 1)), "PU_XPDR"), "PUC_XPDR"),
            Also(Done(Skip(SW("PU_RADAR", "Weather radar: off", "SWITCH_WX_OFF", 1),
                s => s.IsPosition("SWITCH_WX_OFF", 1)), "PU_RADAR"), "PUC_RADAR"),
            Also(Done(Skip(Multi("PU_ATMS", "ATM switches: off", ("ROTARY_HYD_B2_MODE", 0), ("ROTARY_HYD_C2_MODE", 0)),
                s => s.IsOn("FO_ATMS_OFF")), "PU_ATMS"), "PUC_ATMS"),
            Also(Done(Skip(Multi("PU_AC_PUMPS", "AC hydraulic pumps: off", ("SWITCH_HYD_AC_PUMP_B3", 0), ("SWITCH_HYD_AC_PUMP_C3", 0)),
                s => s.IsOn("FO_AC_PUMPS_OFF")), "PU_AC_PUMPS"), "PUC_AC_PUMPS"),
            Also(Done(Skip(Multi("PU_FUEL_PUMPS", "Fuel pumps: all off",
                    ("SWITCH_FUEL_PUMP_1_1", 0), ("SWITCH_FUEL_PUMP_1_2", 0),
                    ("SWITCH_FUEL_PUMP_2L_1", 0), ("SWITCH_FUEL_PUMP_2L_2", 0),
                    ("SWITCH_FUEL_PUMP_2R_1", 0), ("SWITCH_FUEL_PUMP_2R_2", 0),
                    ("SWITCH_FUEL_PUMP_3_1", 0), ("SWITCH_FUEL_PUMP_3_2", 0)),
                s => s.IsOn("FO_FUEL_PUMPS_OFF")), "PU_FUEL_PUMPS"), "PUC_FUEL_PUMPS"),
            // The master and the four dump switches off, then their five guards closed: the keys
            // FO_JETTISON_SAFE reads, in its order.
            Also(Done(Skip(Multi("PU_JETTISON", "Jettison switches: off and guarded",
                    ("SWITCH_FUEL_DUMP_MASTER", 0), ("SWITCH_FUEL_DUMP_2L", 0), ("SWITCH_FUEL_DUMP_1", 0),
                    ("SWITCH_FUEL_DUMP_3", 0), ("SWITCH_FUEL_DUMP_2R", 0),
                    ("GUARD_FUEL_DUMP_MASTER", 0), ("GUARD_FUEL_DUMP_2L", 0), ("GUARD_FUEL_DUMP_1", 0),
                    ("GUARD_FUEL_DUMP_3", 0), ("GUARD_FUEL_DUMP_2R", 0)),
                s => s.IsOn("FO_JETTISON_SAFE")), "PU_JETTISON"), "PUC_JETTISON"),
            Also(Done(Skip(Multi("PU_PAX_OXY", "Passenger oxygen: off and guarded",
                    ("SWITCH_PAX_OXY_MANUAL", 0), ("GUARD_PAX_OXY_MANUAL", 0)),
                s => s.IsOn("FO_PAX_OXY_SAFE")), "PU_PAX_OXY"), "PUC_PAX_OXY"),
            // The aircraft's cockpit safety inspection has no galley line, so POWER_UP_CL has no PUC_GALLEY
            // (Ruling R3): this step completes its action line only.
            Done(Skip(Multi("PU_GALLEY", "Galley power: off",
                    ("SWITCH_GALLEY_BUS_1", 0), ("SWITCH_GALLEY_BUS_2", 0), ("SWITCH_GALLEY_BUS_3", 0)),
                s => s.IsOn("FO_GALLEY_OFF")), "PU_GALLEY"),

            // The aircraft's checklist lines 18 to 32 in one composite: battery off, standby power On, the
            // lights read, battery on, standby power armed, the lights read again. No SkipCondition: the check
            // is the point. Linked to the four lines it delivers and nothing else (FO-7, never the meter selectors).
            Also(Done(SW("PU_STBY_CHECK", "Standby power check", X.StandbyPowerCheck, 1), "PU_STBY_CHECK"),
                "PUC_STBY_CHECK", "PUC_BATTERY", "PUC_STBY_ARMED"),
            Also(Done(Skip(SW("PU_AC_ESS", "AC essential bus selector: bus 3, generator 1", "ROTARY_AC_ESS_BUS", 4),
                s => s.IsPosition("ROTARY_AC_ESS_BUS", 4)), "PU_AC_ESS"), "PUC_AC_ESS"),
            Done(Skip(SW("PU_EXT_PWR", "External power: on", X.ExternalPower, 1), s => s.IsOn("FO_EXT_POWER_ON")), "PU_EXT_PWR"),
            // A skipped step is announced "Already set: <label>", so the label is a short noun phrase and
            // the instruction lives in the reminder text.
            Skip(Captain("PU_EXT_PWR_CPT", "Ground power",
                    "Connect ground power on the tablet's Ground page, or the Preflight flow starts the APU."),
                s => s.IsOn("FO_EXT_POWER_ON")),
            Also(Done(Skip(Multi("PU_INS_ALIGN", "INS 1, 2 and 3: align",
                    ("ROTARY_INS_MODE_1", 2), ("ROTARY_INS_MODE_2", 2), ("ROTARY_INS_MODE_3", 2)),
                s => s.IsOn("FO_INS_ALIGN_OR_NAV")), "PU_INS_ALIGN"), "PUC_INS_ALIGN"),
            Captain("PU_INS_POSITION", "Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit."),
        }
    };

    // ------------------------------------------------------------------ step builders (the MD-11's, retyped)

    /// <summary>A read-only check of a switch the flow finds: 5 s, then skipped aloud.</summary>
    private const int CheckSeconds = 5;

    private static bool On(double v) => v > 0.5;

    /// <summary>
    /// A read-only check (FO-6, FO-7): "Already set" when <paramref name="field"/> already satisfies
    /// <paramref name="ok"/>, else a wait of <paramref name="timeoutSec"/> that is skipped aloud. It
    /// never writes, and it completes the read-back lines that read the same state, so a line no
    /// write of the flow sets is latched only when the aircraft shows it; a timed-out check keeps
    /// its lines out of the latch, mirroring the aircraft.
    /// </summary>
    private static Step Check(string id, string label, string field, Func<double, bool> ok, int timeoutSec,
        string readBackItemId, params string[] moreReadBackItemIds)
        => Also(Done(Skip(WaitForField(id, label, field, ok, timeoutSec), s => ok(s.GetValue(field))), readBackItemId),
            moreReadBackItemIds);

    private static Step SW(string id, string label, string eventName, int target) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValue = target,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Provider(string id, string label, string eventName, Func<L1011FoStateEvaluator, int?> provider) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitch,
        EventName = eventName,
        TargetValueProvider = provider,
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step Multi(string id, string label, params (string EventName, int? TargetValue)[] actions) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.SetSwitchMultiple,
        MultiActions = actions.ToList(),
        FailurePolicy = FlowStepFailurePolicy.Skip,
    };

    private static Step WaitForField(string id, string label, string field, Func<double, bool> condition, int timeoutSec,
        FlowStepFailurePolicy onTimeout = FlowStepFailurePolicy.Skip) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.WaitForCondition,
        ConditionFieldName = field,
        Condition = condition,
        TimeoutSeconds = timeoutSec,
        FailurePolicy = onTimeout,
    };

    /// <summary>
    /// Stops a flow that switches pumps off or turns the battery off unless the aircraft reads on the
    /// ground (the stock SIM_ON_GROUND; unread fails the guard).
    /// </summary>
    private static Step OnGroundGuard(string id)
        => WaitForField(id, "On the ground", "SIM_ON_GROUND", v => v > 0.5, CheckSeconds,
            onTimeout: FlowStepFailurePolicy.Stop);

    /// <summary>
    /// Stops a cold-and-dark flow unless every engine reads stopped (<c>FO_ENGINES_STOPPED</c>: every N3 low
    /// and every fuel-and-ignition switch off). An unread state is NaN, which fails the guard rather than
    /// letting a destructive step through.
    /// </summary>
    private static Step EnginesStoppedGuard(string id)
        => WaitForField(id, "Engines stopped", "FO_ENGINES_STOPPED", v => v > 0.5, CheckSeconds,
            onTimeout: FlowStepFailurePolicy.Stop);

    private static Step Captain(string id, string label, string? reminderText = null) => new()
    {
        Id = id, Label = label,
        ActionType = FlowStepActionType.CaptainReminder,
        ReminderText = reminderText ?? label,
    };

    private static Step Skip(Step step, Func<L1011FoStateEvaluator, bool> cond)
    {
        step.SkipCondition = cond;
        return step;
    }

    private static Step Stop(Step step)
    {
        step.FailurePolicy = FlowStepFailurePolicy.Stop;
        return step;
    }

    private static Step Done(Step step, string checklistItemId)
    {
        step.CompletesChecklistItemId = checklistItemId;
        return step;
    }

    /// <summary>The read-back line this step also delivers: ticked with it, and kept unticked when it is skipped (FO-6).</summary>
    private static Step Also(Step step, params string[] readBackItemIds)
    {
        step.AlsoCompletesChecklistItemIds = readBackItemIds;
        return step;
    }
}
