---
paths:
  - "MSFSBlindAssist/FirstOfficer/L1011/**"
  - "MSFSBlindAssist/Aircraft/IniL1011Definition.FirstOfficer.cs"
  - "MSFSBlindAssist/Aircraft/L1011/L1011FoResult.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/L1011/**"
---
# iniBuilds L-1011 TriStar First Officer rules

Loaded when Claude reads matching code, beside first-officer.md. Background: docs/first-officer.md (the TriStar paragraphs) and docs/l1011.md (First Officer). Full text of each rule: docs/invariants/first-officer-l1011.md.

- [FOL-1] The TriStar First Officer writes only through `IniL1011Definition.FoSetAsync`/`FoSetAltimetersAsync`, the panels' own write paths, never `HandleUIVariableSet` or a raw calculator string; the entry speaks nothing and returns `L1011FoResult`, and the executor reads every readable write back before calling it done. Full: docs/invariants/first-officer-l1011.md#fol-1
- [FOL-2] A First Officer lever write records `_commanded` so `AnnounceLever` stays silent for it; a lever moved by anything else is still spoken. A First Officer test mutes lamp speech with `FoMuteLampSpeech` and speaks one verdict, a pass only with flow progress on, a failure always. Full: docs/invariants/first-officer-l1011.md#fol-2
- [FOL-3] TriStar engines running and stopped are read on N3 (`L1011_RO_N3_n`, running at 50, stopped below 5 with every fuel and ignition switch off), never the window's stock N2 feed. Full: docs/invariants/first-officer-l1011.md#fol-3
- [FOL-4] A stopped TriStar flow's ground start is released once, by `OnFlowStoppedAsync` or `ReleaseHeldPresses`, whichever takes it from `_flowGroundStarts` first, never the stopped watch; a time-out or failed click releases under the gate. The release is the row's own click to 0, only while `TOGGLE_CUTOFF_ENG_n` reads off: never a fuel write. Full: docs/invariants/first-officer-l1011.md#fol-4
- [FOL-5] Only `FoSetCabinAltitudeSelectorAsync` writes the cabin altitude selector: `L:FLT_CAB` seeded one step off the target, then ONE click toward it of the knob's own `H:ROTARY_FLT_CAB_DEC` or `_INC`, never several H: events in one string (they move one step). The step is `L1011Pressurization.KnobStep`'s, read back in metres within 150 ft. Full: docs/invariants/first-officer-l1011.md#fol-5
- [FOL-6] A TriStar fuel pump cut (Power Up, Shutdown; step and tick) is left alone, aloud, while `FO_APU_STARTING_OR_RUNNING` reads on (APU at 1 %; unread lets it go): the tank 2 left pump feeds a spooling APU. Parking keeps that pump and the engine 2 tank valve on, its fuel panel after the `FO_POWER_SOURCE_ON` and `FO_ENGINES_STOPPED` waits. Full: docs/invariants/first-officer-l1011.md#fol-6
- [FOL-7] The TriStar First Officer runs only tests it can read: `FireTestAsync` (Both loops) and `WheelWellFireTestAsync`, each A and B together, `HydraulicCheckAsync`, `StandbyPowerCheckAsync`; never the turbine or area overheat test. Each re-arms the lamp mute every phase and puts its switches back; a failure is spoken, never stops the flow. Full: docs/invariants/first-officer-l1011.md#fol-7
- [FOL-8] The TriStar APU stop (`ApuStopAsync`) is Stop-aware: one press, then a read-only wait outside the gate until the APU reads below `ApuStoppedRpm`, up to `ApuSpinDownWaitMs` (it takes up to a minute, past a tick's grace, [FO-11]), never done once sent. The APU master off, step or tick, follows the stop, and Shutdown's battery the master. Full: docs/invariants/first-officer-l1011.md#fol-8
- [FOL-9] A TriStar flow or tick run in the wrong phase is guarded, unread failing: After Landing opens with `OnGroundGuard`, its anti-skid, air data heat, trim, speed brake and flaps ticks are left alone off the ground; After Takeoff's gear step and tick are left alone unless read airborne (`NotAirborne`), not trusting the aircraft to refuse. Full: docs/invariants/first-officer-l1011.md#fol-9
