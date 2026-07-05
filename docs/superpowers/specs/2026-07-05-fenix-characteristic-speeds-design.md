# Fenix A320 Family: Characteristic Speeds + Top of Descent Hotkeys — Design

Date: 2026-07-05
Aircraft: Fenix A319/A320/A321 (one definition, `FENIX_A320CEO`)
Status: Approved by user (interactive design session); mechanism live-verified against a running Fenix A319 in MSFS 2024 before this doc was written.

## Goal

Give the Fenix the same output-mode readout hotkeys the FlyByWire A32NX has for
characteristic speeds and top of descent, sourced from the Fenix's own FMS.

| Hotkey (output mode) | Announces | Source |
|---|---|---|
| Shift+1 | Green Dot speed ("O") | MCDU PERF TO or APPR page (phase-appropriate) |
| Shift+2 | S speed (slat retraction) | same |
| Shift+3 | F speed (flap retraction) | same |
| Shift+4 | VLS **and VAPP** in one announcement | MCDU PERF APPR page |
| Shift+D | Distance + UTC time to Top of Descent | MCDU PERF CRZ page, "TO (T/D)" field |

**Explicitly NOT shipped:** Shift+5 (stall speed) and Shift+6 (VFE Next). The
Fenix exposes these nowhere (not in its GraphQL dataRef list, not on any MCDU
page). Per user decision these hotkey actions are simply not handled on the
Fenix — the keys stay inert, no "not available" dead keys.

## Data source (live-verified 2026-07-05)

The Fenix publishes a GraphQL API at `http://localhost:8083/graphql` (same
endpoint `FenixMCDUService` already uses for the MCDU window).

- Schema obtained via HotChocolate SDL export (`GET /graphql?sdl` — introspection
  queries are blocked, the SDL download is not).
- `query { dataRef { list { … } } }` returns the complete public catalog:
  **481 dataRefs. None of them are characteristic speeds or TOD.**
  (`aircraft.fms.perf.takeOff.v1/vr/v2` exist; nothing for GD/S/F/VLS/VFE/TOD.
  `aircraft.fms.flightPlanXml` contains only real route legs — no pseudo
  waypoints like (T/D).)
- Therefore the ONLY real source is the MCDU display itself:
  - `query { dataRef { dataRef(name: "aircraft.mcdu2.display") { value } } }`
    returns the current MCDU 2 screen as XML (one-shot HTTP read, no
    subscription needed).
  - `mutation { dataRef { writeInt(name: "system.switches.S_CDU2_KEY_<KEY>", value: 1/0) } }`
    presses MCDU 2 keys (press 1, ~80 ms, release 0 — the pattern
    `FenixMCDUService.SendButtonPress` already uses).

**MCDU 2 (First Officer) is used exclusively** so the pilot's own MCDU 1 (and
MSFSBA's MCDU window, which defaults to MCDU 1) are never disturbed. Side
effect: MCDU 2 is left on a PERF page after a read. Accepted (single-crew
accessibility use; any page key recalls another page).

### Live-verified page contents (Fenix A319, VIDP→VCBI, PreFlight)

- PERF TAKE OFF: `FLP RETR F=117`, `SLT RETR S=153`, `CLEAN O=---`
  (O legitimately uncomputed at that setup stage), V1/VR/V2, `NEXT PHASE>` at R6.
- PERF CLB: no speeds of interest. `PREV/NEXT PHASE` at L6/R6.
- PERF CRZ: `TO (T/D)` header over `UTC DIST` line reading `----/---` on the
  ground (computed in flight).
- PERF DES: nothing of interest.
- PERF APPR: `F=117`, `S=153`, `O=---`, `VAPP 109`, `VLS 104`, QNH/TEMP/wind
  fields, `PREV PHASE` at L6.

Phase page order: TO → CLB → CRZ → DES → APPR (→ GA). `S_CDU2_KEY_PERF` opens
the page for the CURRENT FMGC phase; `LSK6R` = NEXT PHASE, `LSK6L` = PREV PHASE
(TO has no PREV; APPR/GA end has no NEXT beyond GA).

## Architecture

New file `Services/FenixPerfReader.cs` — a small, self-contained reader owned
lazily by `FenixA320Definition`:

- `HttpClient` POSTs to `http://localhost:8083/graphql` (one-shot query /
  writeInt mutation; ~5 s timeout). No WebSocket, no persistent connection —
  cannot conflict with `FenixMCDUService`'s subscription socket (different
  transport; the GraphQL server serves both concurrently — verified live while
  the MCDU window pattern was active).
- Public async API:
  - `Task<PerfSpeeds?> ReadSpeedsAsync(bool approachPage)` → drives MCDU 2 to
    PERF TO or PERF APPR, parses F/S/O (+ VLS/VAPP on APPR).
  - `Task<TodInfo?> ReadTodAsync()` → drives MCDU 2 to PERF CRZ, parses the
    `TO (T/D)` UTC/DIST values.
- Page navigation algorithm (content-driven, not press-count-driven):
  1. Press `PERF`. Read display. Identify which PERF page is up from its title
     line (`TAKE OFF`, `CLB`, `CRZ`, `DES`, `APPR`, `GO AROUND`).
  2. Compute steps to the target page in the fixed order and press
     `LSK6R`/`LSK6L` accordingly, re-reading and re-identifying after each
     press (max 6 presses, then give up).
  3. Parse target values from the final display.
- Display parsing: XDocument over the display XML; strip Fenix format codes
  (`{...}` markup and single-letter color prefixes) the way
  `FenixMCDUService.StripFormatCodes` does; regex the value fields
  (`F=(\d+|-+)`, `S=(\d+|-+)`, `O=(\d+|-+)`, `VLS` column digits, `VAPP`
  digits, T/D `(\d{4}|-+)/(\d+|-+)`).
- Reentrancy: an `Interlocked` latch — a second hotkey while a walk is in
  progress announces the busy-path result of the first (or is dropped) rather
  than interleaving two MCDU walks (GsxGateSelector precedent).

`FenixA320Definition.HandleHotkeyAction` gains cases for `ReadSpeedGD`,
`ReadSpeedS`, `ReadSpeedF`, `ReadSpeedVLS`, `ReadDistanceToTOD` (NOT
`ReadSpeedVS`/`ReadSpeedVFE`), each fire-and-forget async → announce via
`AnnounceImmediate` on completion. Phase selection for speeds: read
`aircraft.fms.flightPhase` in the same GraphQL round trip — `PreFlight`,
`TakeOff`, `Climb` → TO page; anything else (`Cruise`, `Descent`, `Approach`,
`GoAround`, `Done`) → APPR page.

## Announcements

- Speeds: `"Green Dot 205 knots"` / `"S speed 184 knots"` / `"F speed 139 knots"`
  (FBW wording). Dashes on the page → `"Green Dot not computed yet"`.
- VLS: `"VLS 104 knots, VAPP 109 knots"`; dashes → `"VLS not computed yet"`.
- TOD: `"156 miles to top of descent, at 0543 Zulu"`; `----/---` →
  `"Top of descent not computed yet"`. (The Fenix gives UTC-at-TOD, not
  time-to-go, so wording deliberately differs from FBW's `: HH:MM:SS`
  time-to-go suffix — announce what is real.)
- Fenix API unreachable (connection refused/timeout): `"Fenix connection not
  available"`. Page walk failed to find the target page: `"Could not read the
  MCDU"`.
- All error/announce paths use `AnnounceImmediate` (user-initiated hotkey
  readouts per the screen-reader rules).

## Isolation guarantees

- Hotkeys `HOTKEY_SPEED_*`/`HOTKEY_DISTANCE_TO_TOD` are already registered
  app-wide in `HotkeyManager` and dispatched per-aircraft — zero changes there.
- Changes are confined to: `FenixA320Definition.cs` (new switch cases + lazy
  reader field + disposal), new `Services/FenixPerfReader.cs`, and
  `HotkeyGuides/Fenix_A320_Hotkeys.txt`. No FBW/PMDG/HS787 file is touched.
- The reader is only instantiated on first use of one of the new hotkeys and is
  disposed on aircraft switch (aircraft-swap disposal rule).

## Testing

No automated test project exists (repo convention: live verification). Test
plan, in the user's current flight (Fenix A319 VIDP→VCBI):

1. On the ground pre-departure: Shift+3/Shift+2 announce F/S from PERF TO
   (117/153 expected today); Shift+1 announces "Green Dot not computed yet"
   if O is still dashes; Shift+4 announces VLS/VAPP (104/109 expected);
   Shift+D announces "Top of descent not computed yet".
2. In cruise: Shift+D announces real distance + UTC; Shift+1/2/3 read the
   APPR-page values; MCDU 1 (pilot's unit + MSFSBA MCDU window) unaffected
   throughout.
3. Fenix not loaded / sim closed: keys announce "Fenix connection not
   available" (verify once after the flight).
4. Regression spot-check: FBW A32NX Shift+1/Shift+D unchanged (different
   definition file, but verify once).

## Post-incident revision (2026-07-05, same day — forward-only navigation)

The first build navigated PERF phase pages in BOTH directions (LSK6R = NEXT
PHASE, LSK6L = PREV PHASE), based on ground captures where line 6 left reads
"PREV PHASE". **In flight, the ACTIVE phase's PERF page renders "ACTIVATE
APPR PHASE" on that same key** — the ground captures could not show this.
During the live test (Climb phase), a speeds hotkey targeting the TAKE OFF
page pressed LSK6L twice (press + didn't-advance retry), which activated and
confirmed the approach phase mid-climb on the user's aircraft. Recovery:
re-enter the cruise FL on the PROG page.

Revised rules, implemented in `FenixPerfReader` and probe-locked:

- **LSK6L is never pressed. Ever.** Navigation is strictly forward via LSK6R.
- LSK6R is only pressed after `HasNextPhaseKey` confirms the display actually
  renders the right-aligned "PHASE>" label (pure parser, probe-tested).
- A walk that fails to ADVANCE the page index aborts — no retries.
- Speeds: PERF opens on TAKE OFF or GO AROUND → that page's own F/S/O;
  anything else → forward to APPR (landing-weight predictions). The
  flight-phase GraphQL pre-read is deleted — the page PERF opens on is the
  phase truth.
- TOD: PERF opens past CRZ (DES/APPR/GA) → announce "Past top of descent"
  with zero key presses (`TodInfo.PastTod`).

## Non-goals

- Shift+5 / Shift+6 on the Fenix (no data source — keys stay unhandled).
- Live PFD-tape values (Fenix does not publish them; the MCDU PERF values are
  what a sighted pilot reads anyway).
- Restoring MCDU 2 to its pre-read page.
- Any change to FBW/PMDG/787 behavior.
