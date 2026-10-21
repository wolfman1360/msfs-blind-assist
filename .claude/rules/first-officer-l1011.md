---
paths:
  - "MSFSBlindAssist/FirstOfficer/L1011/**"
  - "MSFSBlindAssist/Aircraft/IniL1011Definition.FirstOfficer.cs"
  - "MSFSBlindAssist/Aircraft/L1011/L1011FoResult.cs"
  - "tests/MSFSBlindAssist.Tests/FirstOfficer/L1011/**"
---
# iniBuilds L-1011 TriStar First Officer rules

Loaded when Claude reads matching code, beside first-officer.md. Background: docs/first-officer.md (the TriStar paragraph) and docs/l1011.md (First Officer). Full text of each rule: docs/invariants/first-officer-l1011.md.

- [FOL-1] The TriStar First Officer writes only through `IniL1011Definition.FoSetAsync`/`FoSetAltimetersAsync`, the panels' own planners, never `HandleUIVariableSet` or a raw calculator string; the entry speaks nothing and returns `L1011FoResult`, and the executor reads every readable write back before calling it done. Full: docs/invariants/first-officer-l1011.md#fol-1
- [FOL-2] A First Officer lever write records `_commanded` so `AnnounceLever` stays silent for it; a lever moved by anything else is still spoken. A First Officer test mutes lamp speech with `FoMuteLampSpeech` and speaks one verdict, a pass only with flow progress on, a failure always. Full: docs/invariants/first-officer-l1011.md#fol-2
- [FOL-3] TriStar engines running and stopped are read on N3 (`L1011_RO_N3_n`, running at 50, stopped below 5 with every fuel and ignition switch off), never the window's stock N2 feed. Full: docs/invariants/first-officer-l1011.md#fol-3
