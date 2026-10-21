---
paths:
  - "MSFSBlindAssist/Aircraft/IniL1011Definition.Announcements.cs"
---
# L-1011 First Officer rule whose code the TriStar's lever call-out holds

MIRRORS: copied word for word from first-officer-l1011.md, whose globs leave this partial out; the code is `AnnounceLever`'s check that keeps the First Officer's own lever write unspoken. Change both together (ClaudeContextBudgetTests checks).

- [FOL-2] A First Officer lever write records `_commanded` so `AnnounceLever` stays silent for it; a lever moved by anything else is still spoken. A First Officer test mutes lamp speech with `FoMuteLampSpeech` and speaks one verdict, a pass only with flow progress on, a failure always. Full: docs/invariants/first-officer-l1011.md#fol-2
