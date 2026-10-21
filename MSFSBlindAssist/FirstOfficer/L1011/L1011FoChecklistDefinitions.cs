using MSFSBlindAssist.FirstOfficer.Models;
using X = MSFSBlindAssist.FirstOfficer.L1011.L1011FoActionExecutor;

namespace MSFSBlindAssist.FirstOfficer.L1011;

using Item = Models.ChecklistItem<L1011FoActionExecutor, L1011FoStateEvaluator>;
using Group = Models.ChecklistGroup<L1011FoActionExecutor, L1011FoStateEvaluator>;
using CheckFn = System.Func<L1011FoActionExecutor, L1011FoStateEvaluator, System.Threading.Tasks.Task>;

/// <summary>
/// The iniBuilds L-1011 TriStar First Officer checklists, the MD-11's two layers. Part 1 is Power Up.
///
/// The ACTION group (<c>POWER_UP</c>) lists the Power Up flow's writes and its unconditional Captain step,
/// in the flow's order and with the flow's labels: ticking a line runs the same write as its flow step,
/// through the executor, and a state line ticks itself from the aircraft (<c>RevertToState</c>, [FOB-1]).
/// The conditional ground power prompt stays flow-only, as the MD-11's conditional Captain prompts do.
///
/// The READ-BACK group (<c>POWER_UP_CL</c>, [FO-3]: action-free) is the aircraft's cockpit safety inspection
/// in its own words and order. Each line reads the field and value its delivering flow step checks or skips
/// on: a single switch its own key at the target, a multi-switch line the matching synthetic. The standby
/// power check's line is judged by no state; the flow's composite step delivers it ([FO-7]). The inspection
/// has no galley line, so none is invented: the galley step completes its action line only.
///
/// Three hand ticks are left alone unless the aircraft reads on the ground with every engine stopped
/// ([FO-25]): the AC hydraulic pumps and the fuel pumps off, and the standby power check, which turns the
/// battery off for a moment. Unread refuses, as the flow's guards do.
/// </summary>
public static class L1011FoChecklistDefinitions
{
    public static List<Group> Build() => new()
    {
        PowerUp(), PowerUpCL(),
    };

    private const string ContIgnition = "SWITCH_CONT_IGNITION";
    private const string Transponder = "ROTARY_TCAS_MODE";
    private const string RadarOff = "SWITCH_WX_OFF";
    private const string AcEssBus = "ROTARY_AC_ESS_BUS";
    private const string Battery = "TOGGLE_BATTERY";

    /// <summary>The AC essential power selector's normal position: bus 3 (generator 1).</summary>
    private const int AcEssNormal = 4;

    /// <summary>The ALIGN position of an INS mode selector.</summary>
    private const int InsAlign = 2;

    private static readonly string[] FuelPumps =
    {
        "SWITCH_FUEL_PUMP_1_1", "SWITCH_FUEL_PUMP_1_2", "SWITCH_FUEL_PUMP_2L_1", "SWITCH_FUEL_PUMP_2L_2",
        "SWITCH_FUEL_PUMP_2R_1", "SWITCH_FUEL_PUMP_2R_2", "SWITCH_FUEL_PUMP_3_1", "SWITCH_FUEL_PUMP_3_2",
    };

    /// <summary>The master and the four dump switches off, then their five guards closed: the flow's order.</summary>
    private static readonly string[] Jettison =
    {
        "SWITCH_FUEL_DUMP_MASTER", "SWITCH_FUEL_DUMP_2L", "SWITCH_FUEL_DUMP_1", "SWITCH_FUEL_DUMP_3", "SWITCH_FUEL_DUMP_2R",
        "GUARD_FUEL_DUMP_MASTER", "GUARD_FUEL_DUMP_2L", "GUARD_FUEL_DUMP_1", "GUARD_FUEL_DUMP_3", "GUARD_FUEL_DUMP_2R",
    };

    private static bool Is(double v, double position) => Math.Abs(v - position) < 0.5;
    private static bool On(double v) => v > 0.5;

    private static CheckFn Set(string key, int target) => (e, _) => e.Set(key, target);

    private static CheckFn SetAll(params (string Key, int Target)[] writes) => async (e, _) =>
    {
        foreach (var (k, t) in writes) await e.Set(k, t);
    };

    private static (string Key, int Target)[] All(int target, params string[] keys)
        => keys.Select(k => (k, target)).ToArray();

    // ================================================================== action group

    private static Group PowerUp()
    {
        const string G = "POWER_UP";
        return new Group
        {
            Id = G, Name = "Power Up",
            Items = new()
            {
                Auto("PU_CONT_IGN", G, "Continuous ignition: off", ContIgnition, v => Is(v, 0), Set(ContIgnition, 0)),
                Auto("PU_RADIO_MASTERS", G, "Radio master switches: on", "FO_RADIO_MASTERS_ON", On,
                    SetAll(("SWITCH_MASTER_RADIO_ESS", 1), ("SWITCH_MASTER_RADIO_NO2", 1))),
                Auto("PU_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1), Set(Transponder, 1)),
                Auto("PU_RADAR", G, "Weather radar: off", RadarOff, v => Is(v, 1), Set(RadarOff, 1)),
                Auto("PU_ATMS", G, "ATM switches: off", "FO_ATMS_OFF", On,
                    SetAll(("ROTARY_HYD_B2_MODE", 0), ("ROTARY_HYD_C2_MODE", 0))),
                // A cold-and-dark cut ([FO-25]): never airborne, never with an engine turning, never unread.
                LeaveAlone(Auto("PU_AC_PUMPS", G, "AC hydraulic pumps: off", "FO_AC_PUMPS_OFF", On,
                    SetAll(("SWITCH_HYD_AC_PUMP_B3", 0), ("SWITCH_HYD_AC_PUMP_C3", 0))),
                    NotParked, ParkedOnlyText),
                LeaveAlone(Auto("PU_FUEL_PUMPS", G, "Fuel pumps: all off", "FO_FUEL_PUMPS_OFF", On, SetAll(All(0, FuelPumps))),
                    NotParked, ParkedOnlyText),
                Auto("PU_JETTISON", G, "Jettison switches: off and guarded", "FO_JETTISON_SAFE", On, SetAll(All(0, Jettison))),
                Auto("PU_PAX_OXY", G, "Passenger oxygen: off and guarded", "FO_PAX_OXY_SAFE", On,
                    SetAll(("SWITCH_PAX_OXY_MANUAL", 0), ("GUARD_PAX_OXY_MANUAL", 0))),
                Auto("PU_GALLEY", G, "Galley power: off", "FO_GALLEY_OFF", On,
                    SetAll(All(0, "SWITCH_GALLEY_BUS_1", "SWITCH_GALLEY_BUS_2", "SWITCH_GALLEY_BUS_3"))),
                // The composite turns the battery off for a moment, which the fleet's cut sweep cannot see
                // by its label: the same guard as the pumps ([FO-25]). Judged by no state; the check is the point.
                LeaveAlone(ActionManual("PU_STBY_CHECK", G, "Standby power check", Set(X.StandbyPowerCheck, 1)),
                    NotParked, ParkedOnlyText),
                Auto("PU_AC_ESS", G, "AC essential bus selector: bus 3, generator 1", AcEssBus, v => Is(v, AcEssNormal),
                    Set(AcEssBus, AcEssNormal)),
                Auto("PU_EXT_PWR", G, "External power: on", "FO_EXT_POWER_ON", On, Set(X.ExternalPower, 1)),
                Auto("PU_INS_ALIGN", G, "INS 1, 2 and 3: align", "FO_INS_ALIGN_OR_NAV", On,
                    SetAll(All(InsAlign, "ROTARY_INS_MODE_1", "ROTARY_INS_MODE_2", "ROTARY_INS_MODE_3"))),
                Reminder("PU_INS_POSITION", G,
                    "Enter present position on INS 1, 2 and 3: input Shift+M, then Alt+P on each unit."),
            }
        };
    }

    // ================================================================== read-back group (the aircraft's words)

    private static Group PowerUpCL()
    {
        const string G = "POWER_UP_CL";
        return new Group
        {
            Id = G, Name = "Cockpit Safety Inspection",
            Items = new()
            {
                Read("PUC_FIRE_HANDLES", G, "Engine and APU fire handles: in", "FO_FIRE_HANDLES_IN", On),
                Read("PUC_CONT_IGN", G, "Continuous ignition: unlatched", ContIgnition, v => Is(v, 0)),
                Read("PUC_RADIO_MASTERS", G, "Essential and no. 2 radio master switches: latched", "FO_RADIO_MASTERS_ON", On),
                Read("PUC_GEAR", G, "Gear lever: down", "FO_GEAR_LEVER_DOWN", On),
                Read("PUC_SPEED_BRAKE", G, "Speed brake handle: forward", "FO_SPEED_BRAKE_FORWARD", On),
                Read("PUC_FLAPS", G, "Flaps and flap handle: agree", "FO_FLAPS_AGREE", On),
                Read("PUC_FUEL_IGN", G, "Fuel and ignition switches: off", "FO_FUEL_IGNITION_OFF", On),
                Read("PUC_XPDR", G, "Transponder: standby", Transponder, v => Is(v, 1)),
                Read("PUC_RADAR", G, "Radar: off", RadarOff, v => Is(v, 1)),
                Read("PUC_ATMS", G, "ATM switches: off", "FO_ATMS_OFF", On),
                Read("PUC_AC_PUMPS", G, "AC hydraulic pump switches: unlatched", "FO_AC_PUMPS_OFF", On),
                Read("PUC_FUEL_PUMPS", G, "Fuel tank pump switches: unlatched", "FO_FUEL_PUMPS_OFF", On),
                Read("PUC_JETTISON", G, "Master jettison and dump switches: unlatched", "FO_JETTISON_SAFE", On),
                Read("PUC_PAX_OXY", G, "Passenger oxygen manual switch: guarded and unlatched", "FO_PAX_OXY_SAFE", On),
                // Judged by no state: the flow's standby power check delivers it, with the two lines after it.
                Manual("PUC_STBY_CHECK", G, "Standby power check: complete"),
                Read("PUC_BATTERY", G, "Battery: on", Battery, v => Is(v, 1)),
                Read("PUC_STBY_ARMED", G, "Standby power: armed", "FO_STBY_ARMED", On),
                Read("PUC_AC_ESS", G, "AC essential power selector: normal, bus 3 (generator 1)", AcEssBus, v => Is(v, AcEssNormal)),
                Read("PUC_INS_ALIGN", G, "All INS: align", "FO_INS_ALIGN_OR_NAV", On),
            }
        };
    }

    // ================================================================== builders (the MD-11's)

    private static Item Auto(string id, string groupId, string label, string field, Func<double, bool> condition,
        CheckFn? action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.AutoDetectable,
        AutoCompleteAllowed = true,
        ManualCompletionAllowed = true,
        StateFieldName = field,
        StateCondition = condition,
        RevertBehavior = RevertBehavior.RevertToState,
        AdditionalStateFields = Array.Empty<string>(),
        AdditionalStateCondition = condition,
        CheckAction = action,
    };

    /// <summary>A read-back (*_CL) item: auto-detected, action-free.</summary>
    private static Item Read(string id, string groupId, string label, string field, Func<double, bool> condition)
        => Auto(id, groupId, label, field, condition, action: null);

    private static Item ActionManual(string id, string groupId, string label, CheckFn action) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
        CheckAction = action,
    };

    /// <summary>A line no state judges and no tick acts on: ticked by hand, or by the flow step that delivers it.</summary>
    private static Item Manual(string id, string groupId, string label) => new()
    {
        Id = id, GroupId = groupId, Label = label,
        Type = ChecklistItemType.Actionable,
        ManualCompletionAllowed = true,
    };

    private static Item Reminder(string id, string groupId, string text) => new()
    {
        Id = id, GroupId = groupId, Label = text,
        Type = ChecklistItemType.CaptainReminder,
        ManualCompletionAllowed = true,
        ReminderText = text,
    };

    // A line whose hand-tick the First Officer refuses in the aircraft's current state
    // (ChecklistItem.LeaveAloneWhen), speaking `text` instead: the guards the flow carries.
    private static Item LeaveAlone(Item item, Func<L1011FoStateEvaluator, bool> when, string text)
    {
        item.LeaveAloneWhen = when;
        item.LeaveAloneText = text;
        return item;
    }

    internal const string ParkedOnlyText = "Left alone: the aircraft must be on the ground with the engines stopped.";

    // On the ground (the stock SIM_ON_GROUND, the flow's PU_ON_GROUND) with every engine read stopped
    // (FO_ENGINES_STOPPED, the flow's PU_ENGINES_STOPPED); airborne, an engine turning, or either unread,
    // the pumps and the battery stay as they are ([FO-25]).
    private static bool NotParked(L1011FoStateEvaluator s) =>
        !s.IsPosition("SIM_ON_GROUND", 1) || !s.IsOn("FO_ENGINES_STOPPED");
}
