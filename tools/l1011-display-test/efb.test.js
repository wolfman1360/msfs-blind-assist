'use strict';
const test = require('node:test');
const assert = require('node:assert');
const { load, reveal, conceal, scrape, lines, idxOf, agentSource } = require('./efb-run');

// coherent-l1011-efb-agent.js against compact live captures of the Paper Flight Bag (2026-10-03).
// The same reader produced these lines on the live tablet; only typed values differ, because a
// capture keeps no field values: each test sets the ones it reads.
const TABS = [
  'tab|Menu', 'tab|Ground', 'tab|Load sheet', 'tab|OFP', 'tab|Takeoff performance', 'tab|Landing performance',
  'tab|Charts', 'tab|Enroute map',
];

function withoutTabs(all) {
  return all.filter((l) => !l.startsWith('tab|'));
}

function events(el, names) {
  const seen = [];
  for (const n of names) el.addEventListener(n, () => seen.push(n));
  return seen;
}

test('the reader installs, says so, and answers in the client contract', () => {
  const { window, A, installed } = load('ground');
  assert.strictEqual(installed, 'MSFSBA_L1011_EFB_INSTALLED');
  for (const f of ['scrape', 'clickElement', 'setValue', 'findRoot', 'isVisible']) assert.strictEqual(typeof A[f], 'function');
  assert.strictEqual(A.findRoot(), window.document.getElementById('iniEFB'));
  const r = scrape(A);
  assert.strictEqual(r.ok, true);
  assert.strictEqual(r.page, 'Ground');
  for (const e of r.elements) {
    for (const f of ['idx', 'text', 'value', 'controlType', 'kind', 'clickable', 'level', 'live', 'disabled', 'options']) {
      assert.ok(f in e, 'missing ' + f);
    }
  }
  assert.deepStrictEqual(r.elements.map((e) => e.idx), r.elements.map((e, i) => i + 1));
});

test('the eight textless tabs, in the order they sit on the tablet, the shown one marked', () => {
  const { A } = load('takeoff');
  const tabs = lines(A).filter((l) => l.startsWith('tab|'));
  assert.deepStrictEqual(tabs, TABS.map((t) => (t === 'tab|Takeoff performance' ? t + ' (current page)' : t)));
});

test('ground services with the chocks popup open', () => {
  const { A } = load('ground');
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'heading|Doors',
    'button|Door 1 left: closed [k=gnd:door_1L_button, ac]',
    'button|Door 1 right: closed [k=gnd:door_1R_button, ac]',
    'button|Door 2 left: closed [k=gnd:door_2L_button, ac]',
    'button|Door 2 right: closed [k=gnd:door_2R_button, ac]',
    'button|Door 3 left: closed [k=gnd:door_3L_button, ac]',
    'button|Door 3 right: closed [k=gnd:door_3R_button, ac]',
    'button|Forward cargo door: closed [k=gnd:cargo_fwd_button, ac]',
    'button|Aft cargo door: closed [k=gnd:cargo_aft_button, ac]',
    'button|Bulk cargo door: closed [k=gnd:bulkhead_door_button, ac]',
    'button|Open all doors',
    'button|Close all doors',
    'heading|Ground equipment',
    'button|Ground power: on [k=gnd:GPU_button, ac]',
    'button|Air conditioning cart: off [k=gnd:AC_CART_button, ac]',
    'button|Jetway: off [k=gnd:JETWAY_button, ac]',
    'button|Ramp truck: off [k=gnd:RAMP_TRUCK_button, ac]',
    'button|Luggage: off [k=gnd:luggage_button, ac]',
    'button|Catering: off [k=gnd:catering_button, ac]',
    'heading|Pushback',
    'button|Pushback aft',
    'button|Pushback left',
    'button|Pushback right',
    'button|Stop pushback',
    'button|Chocks and maintenance',
    'heading|Chocks and maintenance',
    'button|Chocks: installed [k=cover:covers-btn-chocks, ac]',
    'button|Pitot covers: not installed [k=cover:covers-btn-pitot, ac]',
    'button|Engine covers: not installed [k=cover:covers-btn-engine-covers, ac]',
    'button|Engine cowl door 1: closed [k=cover:covers-btn-engine-cowl-door-1, ac]',
    'button|Engine cowl door 2: closed [k=cover:covers-btn-engine-cowl-door-2, ac]',
    'button|Engine cowl door 3: closed [k=cover:covers-btn-engine-cowl-door-3, ac]',
    'button|Aircraft technicians: not called [k=cover:covers-btn-maintenance-technician, ac]',
    'button|Close',
  ]);
});

test('a closed popup is not read, and a door or service reads its new state under the same key', () => {
  const { document, A } = load('ground');
  conceal(document.getElementById('covers-popup'));
  const door = document.getElementById('door_1L_button');
  door.className = 'efb-button door-open';
  document.getElementById('door_2L_button').className = 'efb-button door-transit';
  document.getElementById('JETWAY_button').className = 'efb-button door-open';
  document.getElementById('covers-btn-pitot').textContent = 'REMOVE';
  const all = lines(A);
  assert.ok(!all.some((l) => l.startsWith('heading|Chocks')));
  assert.ok(all.includes('button|Door 1 left: open [k=gnd:door_1L_button, ac]'));
  assert.ok(all.includes('button|Door 2 left: moving [k=gnd:door_2L_button, ac]'));
  assert.ok(all.includes('button|Jetway: on [k=gnd:JETWAY_button, ac]'));
});

test('a press is one click and nothing else; an unknown or click-through control is refused', () => {
  const { document, A } = load('ground');
  const door = document.getElementById('door_1L_button');
  const seen = events(door, ['click', 'pointerdown', 'mousedown', 'mouseup']);
  assert.strictEqual(A.clickElement(idxOf(A, 'Door 1 left')), true);
  assert.deepStrictEqual(seen, ['click']);
  assert.strictEqual(A.clickElement(999), false);
  const gpu = document.getElementById('GPU_button');
  gpu.style.pointerEvents = 'none';
  const gpuSeen = events(gpu, ['click']);
  assert.strictEqual(A.clickElement(idxOf(A, 'Ground power')), false);
  assert.deepStrictEqual(gpuSeen, []);
});

test('the load sheet: units, fields in reading order with their unit, the read-only total, the load settings', () => {
  const { document, A } = load('loadsheet');
  const seed = {
    TANK1_INPUT: '35.4', TANK1A_INPUT: '0.0', TANK2L_INPUT: '11.7', TANK2L_OUT_INPUT: '6.0', TANK2R_INPUT: '11.7',
    TANK2R_OUT_INPUT: '6.0', TANK3A_INPUT: '0.0', TANK3_INPUT: '35.4', TOTAL_FUEL_INPUT: '106.3', FWD_CARGO_INPUT: '12.0',
    AFT_CARGO_INPUT: '16.0', BULK_CARGO_INPUT: '3.8', TOTAL_CARGO_INPUT: '31.8', FIRST_PAX_INPUT: '10', BUSINESS_PAX_INPUT: '23',
    ECONOMY_PAX_INPUT: '160', TOTAL_PAX_INPUT: '193', TOTAL_PAX_WEIGHT_INPUT: '33.8', TOTAL_WEIGHT_INPUT: '417.8',
  };
  for (const id in seed) document.getElementById(id).value = seed[id];
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'static|Units: thousands of pounds',
    'button|SimBrief import',
    'button|Load settings',
    'heading|Fuel',
    'text|Tank 1 (thousand lb) = 35.4',
    'text|Tank 1A (thousand lb) = 0.0',
    'text|Tank 2 left (thousand lb) = 11.7',
    'text|Tank 2 left outer (thousand lb) = 6.0',
    'text|Tank 2 right (thousand lb) = 11.7',
    'text|Tank 2 right outer (thousand lb) = 6.0',
    'text|Tank 3A (thousand lb) = 0.0',
    'text|Tank 3 (thousand lb) = 35.4',
    'text|Total fuel (thousand lb) = 106.3',
    'heading|Cargo',
    'text|Forward cargo (thousand lb) = 12.0',
    'text|Aft cargo (thousand lb) = 16.0',
    'text|Bulk cargo (thousand lb) = 3.8',
    'text|Total cargo (thousand lb) = 31.8',
    'heading|Passengers',
    'text|First class passengers = 10',
    'text|Business class passengers = 23',
    'text|Economy passengers = 160',
    'text|Total passengers = 193',
    'text|Total passenger weight (thousand lb) = 33.8',
    'heading|Weight',
    'static|Total weight (thousand lb): 417.8 [k=readout:Total weight]',
    'heading|Load settings',
    'select|Load and unload time = INSTANT {INSTANT / TIMED / REALISTIC}',
    'static|Use the sliders, SimBrief import button or directly change each value individually to instantly change the loading values of the Tristar.',
    'button|Back',
  ]);
});

test('kilograms, and the timed loading controls once TIMED is chosen', () => {
  const { document, A } = load('loadsheet');
  conceal(document.getElementById('load_sheet_lb'));
  reveal(document.getElementById('load_sheet_kg'));
  conceal(document.getElementById('ls-instant-section'));
  document.getElementById('ls-mode-instant').className = 'ls-mode-btn';
  document.getElementById('ls-mode-timed').className = 'ls-mode-btn active';
  const timed = document.getElementById('ls-timed-section');
  reveal(timed);
  for (const id of ['ls-step2-btn', 'ls-start-btn', 'ls-time-remaining']) reveal(document.getElementById(id));
  reveal(document.getElementById('ls-timed-actions'));
  const all = lines(A);
  assert.strictEqual(all[8], 'static|Units: thousands of kilograms');
  assert.ok(all.includes('text|Tank 1 (thousand kg) = '));
  assert.ok(all.includes('select|Load and unload time = TIMED {INSTANT / TIMED / REALISTIC}'));
  assert.ok(all.includes('button|Select final load state [dis]'));
  assert.ok(all.includes('button|Start load or unload [dis]'));
  assert.ok(all.includes('static|Time remaining: 30:00 [k=status:loadtime, polite]'));
  const start = document.getElementById('ls-start-btn');
  const seen = events(start, ['click']);
  assert.strictEqual(A.clickElement(idxOf(A, 'Start load or unload')), false);
  assert.deepStrictEqual(seen, []);
});

test('a load sheet value is submitted through the tablet keyboard, and the field is let go', () => {
  const { document, A } = load('loadsheet');
  const tank = document.getElementById('TANK1_INPUT');
  const pad = document.getElementById('osk-num');
  const submit = pad.querySelector('.osk-hide-num');
  // The tablet opens its number pad when a keyboard field is clicked, and reads the field on submit.
  tank.addEventListener('click', () => reveal(pad));
  let submitted = null;
  submit.addEventListener('click', () => { submitted = tank.value; conceal(pad); });
  const seen = events(tank, ['click', 'focus', 'input', 'keyup', 'change', 'blur']);
  assert.strictEqual(A.setValue(idxOf(A, 'Tank 1 ('), '36.5'), true);
  assert.strictEqual(submitted, '36.5');
  assert.deepStrictEqual(seen.slice(0, 3), ['click', 'focus', 'input']);
  assert.ok(!seen.includes('keyup') && !seen.includes('change'));
  assert.notStrictEqual(document.activeElement, tank);
});

test('with no keyboard open a field takes input, keyup, change and blur', () => {
  const { document, A } = load('helper');
  const field = document.getElementById('tod-start-altitude');
  const seen = events(field, ['focus', 'input', 'keyup', 'change', 'blur']);
  assert.strictEqual(A.setValue(idxOf(A, 'Start Altitude'), '35000'), true);
  assert.strictEqual(field.value, '35000');
  assert.deepStrictEqual(seen, ['focus', 'input', 'keyup', 'change', 'blur']);
  assert.notStrictEqual(document.activeElement, field);
});

test('takeoff performance: conditions, the flap choice, the buttons and the results', () => {
  const { document, A } = load('takeoff');
  document.getElementById('to_weight_input').value = '407.3';
  document.getElementById('press_alt_input').value = '449';
  document.getElementById('ambient_temp_input').value = '18';
  const shown = { v1_display: '138', vr_display: '144', v2_display: '156', f10_display: '166', f4_display: '176',
    fup_display: '216', f0_display: '226', takeoff_n1_display: '.643', climb_n1_display: '.535', go_around_n1_display: '1.634',
    max_cont_n1_display: '.578', max_n1_display: '.656', oat_display: '18°C', selected_flaps_display: '14°', stabilizer_trim_display: '2.49' };
  for (const id in shown) document.getElementById(id).textContent = shown[id];
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'heading|Conditions',
    'text|Takeoff weight (thousand lb) = 407.3',
    'text|Pressure altitude (feet) = 449',
    'text|Ambient temperature (degrees C) = 18',
    'select|Takeoff flaps = 10 {10 / 14 / 18 / 22}',
    'button|Sync from aircraft',
    'button|Calculate',
    'button|Set bugs and EPR',
    'button|Takeoff card: hide [k=card:takeoff, ac]',
    'heading|Results',
    'static|V1: 138 knots [k=readout:V1]',
    'static|VR: 144 knots [k=readout:VR]',
    'static|V2: 156 knots [k=readout:V2]',
    'static|Select flaps 10 at: 166 knots [k=readout:Select flaps 10 at]',
    'static|Select flaps 4 at: 176 knots [k=readout:Select flaps 4 at]',
    'static|Select flaps up at: 216 knots [k=readout:Select flaps up at]',
    'static|Minimum clean speed: 226 knots [k=readout:Minimum clean speed]',
    'static|Takeoff EPR: 1.643 [k=readout:Takeoff EPR]',
    'static|Climb EPR: 1.535 [k=readout:Climb EPR]',
    'static|Go-around EPR: 1.634 [k=readout:Go-around EPR]',
    'static|Maximum continuous EPR: 1.578 [k=readout:Maximum continuous EPR]',
    'static|Maximum EPR: 1.656 [k=readout:Maximum EPR]',
    'static|Outside air temperature: 18°C [k=readout:Outside air temperature]',
    'static|Selected flaps: 14° [k=readout:Selected flaps]',
    'static|Stabilizer trim: 2.49 [k=readout:Stabilizer trim]',
  ]);
});

test('choosing flaps clicks that one button only; a dimmed flap is not offered', () => {
  const { document, A } = load('takeoff');
  const seen = {};
  for (const f of ['flaps_10', 'flaps_14', 'flaps_18', 'flaps_22']) seen[f] = events(document.getElementById(f), ['click']);
  assert.strictEqual(A.setValue(idxOf(A, 'Takeoff flaps'), '18'), true);
  assert.deepStrictEqual(Object.keys(seen).filter((f) => seen[f].length > 0), ['flaps_18']);
  assert.strictEqual(A.setValue(idxOf(A, 'Takeoff flaps'), '25'), false);
  for (const f of ['flaps_10', 'flaps_14']) document.getElementById(f).setAttribute('style', 'opacity: 0.35; pointer-events: none;');
  assert.ok(lines(A).includes('select|Takeoff flaps = (none) {(none) / 18 / 22}'));
});

test('landing performance: the two pick-one pairs and the results', () => {
  const { document, A } = load('landing');
  document.getElementById('landing-weight-input').value = '345';
  document.getElementById('landing-elevation-input').value = '1500';
  document.getElementById('vapp-display').textContent = '139';
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'heading|Conditions',
    'text|Landing weight (thousand lb) = 345',
    'text|Landing field elevation (feet) = 1500',
    'select|Runway condition = Dry {Dry / Wet}',
    'select|Anti-skid = On {On / Off}',
    'button|Sync from aircraft',
    'button|Calculate',
    'button|Set bugs',
    'button|Landing card: hide [k=card:landing, ac]',
    'heading|Results',
    'static|Approach speed VAPP: 139 knots [k=readout:Approach speed VAPP]',
    'static|Reference speed VREF: not calculated [k=readout:Reference speed VREF]',
    'static|VREF plus 20: not calculated [k=readout:VREF plus 20]',
    'static|VREF plus 30: not calculated [k=readout:VREF plus 30]',
    'static|VREF plus 40: not calculated [k=readout:VREF plus 40]',
    'static|VREF plus 60: not calculated [k=readout:VREF plus 60]',
    'static|Landing distance: not calculated [k=readout:Landing distance]',
  ]);
  const wet = events(document.getElementById('runway-wet-btn'), ['click']);
  assert.strictEqual(A.setValue(idxOf(A, 'Runway condition'), 'Wet'), true);
  assert.deepStrictEqual(wet, ['click']);
});

test('the OFP: reload, send to INS, the plan with its line breaks, and closing it', () => {
  const { document, A } = load('ofp');
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'button|Reload OFP',
    'button|Send to INS',
    'pre|SYNTHETIC OFP LINE 1\nSYNTHETIC OFP LINE 2 [k=ofp:plan:0]',
    'button|Close OFP',
  ]);
  document.querySelector('.send-to-ins-button').setAttribute('data-active', 'false');
  document.querySelector('.simbrief-viewer').setAttribute('data-active', 'false');
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'button|Reload OFP',
    'button|Send to INS [dis]',
    'static|No flight plan is loaded. Enter your SimBrief ID under Menu, Third party, then press Reload OFP.',
  ]);
});

test('charts are pictures; the METAR lookup is offered', () => {
  const { A } = load('charts');
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'static|Charts are pictures and cannot be read here.',
    'text|METAR airport (ICAO) = ',
    'button|Search METAR',
    'static|METAR: none [k=status:metar, polite]',
  ]);
});

test('flight engineer flows: steps only for an expanded flow, each with its status', () => {
  const { document, A } = load('flows');
  const all = lines(A);
  assert.ok(all.includes('tab|Autocomplete (selected)'));
  const body = withoutTabs(all);
  assert.deepStrictEqual(body, [
    'heading|FLIGHT ENGINEER AUTOCOMPLETE',
    'heading|Preflight preparation',
    'button|Preflight preparation (expanded) [k=fe-toggle:Preflight preparation, ac]',
    'button|Autocomplete Preflight preparation [k=fe-run:Preflight preparation]',
    'button|Master Switch to ON: not set [k=fe-step:Preflight preparation:0, ac]',
    'button|GPU to ON: not set [k=fe-step:Preflight preparation:1, ac]',
    'button|APU to ON: not set [k=fe-step:Preflight preparation:2, ac]',
    'button|HYDRAULIC PANEL to SET: set [k=fe-step:Preflight preparation:3, ac]',
    'button|ELECTRICAL PANEL to SET: not set [k=fe-step:Preflight preparation:4, ac]',
    'button|Fuel PANEL to SET: not set [k=fe-step:Preflight preparation:5, ac]',
    'heading|Engine start',
    'button|Engine start (collapsed) [k=fe-toggle:Engine start, ac]',
    'button|Autocomplete Engine start [k=fe-run:Engine start]',
    'heading|After start',
    'button|After start (collapsed) [k=fe-toggle:After start, ac]',
    'button|Autocomplete After start [k=fe-run:After start]',
  ]);
  const box = document.querySelector('.event-checkbox[data-event-index="0"]');
  box.className = 'event-checkbox is-in-progress';
  assert.ok(lines(A).includes('button|Master Switch to ON: in progress [k=fe-step:Preflight preparation:0, ac]'));
  const complete = events(document.querySelector('.event-complete-button[data-event-index="0"]'), ['click']);
  assert.strictEqual(A.clickElement(idxOf(A, 'Master Switch to ON')), true);
  assert.deepStrictEqual(complete, ['click']);
  document.querySelector('.event-list').className = 'event-list';
  assert.ok(lines(A).includes('button|Preflight preparation (collapsed) [k=fe-toggle:Preflight preparation, ac]'));
  assert.ok(!lines(A).some((l) => l.includes('Master Switch')));
});

test('aircraft settings: one dropdown per visible setting, its value from the active label', () => {
  const { document, A } = load('aircraft');
  // The input's checked state disagrees with the label on the live tablet; the label wins.
  document.getElementById('opt_wgt1').checked = true;
  const body = withoutTabs(lines(A));
  assert.deepStrictEqual(body, [
    'heading|AIRCRAFT SETTINGS',
    'select|Weight Units = IMPERIAL - LBs {METRIC - KGs / IMPERIAL - LBs}',
    'select|Rudder Controls Tiller = YES {YES / NO}',
    'select|FX Performance Mode = OFF {ON / OFF}',
    'select|Map Display screen = OFF {ON / OFF}',
    'select|Realistic Parking Brake = OFF {ON / OFF}',
    'select|Auto Set GA EPR Bugs = ON {ON / OFF}',
    'select|Keyboard input lock = OFF {ON / OFF}',
    'select|Time compression = DISABLED {DISABLED / MAX 2X / MAX 4X}',
    'select|NAVIGATION OPTIONS = INS + GPS {RADNAV / INS / GPS / INS + GPS}',
    'select|INS DRIFT = OFF {ON / OFF}',
    'select|IMPORT SID/STAR WPTS = OFF {ON / OFF}',
    'select|ALT SYNC = OFF {OFF / CPT & FO / CPT, FO & STBY}',
    'select|INS Alignment Time = INSTANT {INSTANT / FAST / REALISTIC}',
    'static|PFB v1.0.9',
  ]);
  const metric = events(document.querySelector('label[for=opt_wgt1]'), ['click']);
  const imperial = events(document.querySelector('label[for=opt_wgt2]'), ['click']);
  assert.strictEqual(A.setValue(idxOf(A, 'Weight Units'), 'METRIC - KGs'), true);
  assert.deepStrictEqual(metric, ['click']);
  assert.deepStrictEqual(imperial, []);
});

test('panel states: the four states, the progress line, and an error spoken as an alert', () => {
  const { document, A } = load('panelstates');
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'heading|PANEL STATES',
    'button|COLD & DARK [k=ps:COLD & DARK]',
    'button|ON GPU [k=ps:ON GPU]',
    'button|ON APU [k=ps:ON APU]',
    'button|READY FOR TAKEOFF [k=ps:READY FOR TAKEOFF]',
    'static|Waiting for command... [k=status:panelstates-step-display, polite]',
  ]);
  const err = document.querySelector('.panelstates-error-display');
  err.textContent = 'Aircraft must be stationary (ground speed below 2 knots)';
  reveal(err);
  assert.ok(lines(A).includes('alert|Aircraft must be stationary (ground speed below 2 knots) [k=alert:panelstates, assertive]'));
});

test('the top of descent calculator: fields named by their titles, the result line', () => {
  const { A } = load('helper');
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'heading|TOP OF DESCEND CALCULATOR',
    'text|Start Altitude (ft) = ',
    'text|End of Descend Altitude (ft) = ',
    'text|Glide Path (degrees) = ',
    'button|CALCULATE',
    'static|Result: -- NM [k=status:helper-result, polite]',
    'button|BACK',
  ]);
});

test('third party: the SimBrief Id, the streamer switch and the Navigraph sign-in code', () => {
  const { window, document, A } = load('thirdparty');
  const code = document.createElement('span');
  code.textContent = 'ABCD1234';
  code.setAttribute('data-vis', '1');
  document.querySelector('.navigraph-login__usercode').insertBefore(code, document.querySelector('.navigraph-login__usercode p'));
  assert.deepStrictEqual(withoutTabs(lines(A)), [
    'heading|Simbrief',
    'text|Simbrief Id = ',
    'checkbox|Streamer Mode = false',
    'heading|Connect Navigraph',
    'static|Navigraph code: ABCD1234 [k=status:navigraphcode]',
    'static|Navigate to https://navigraph.com/code and enter the above code to connect.',
    'button|OPEN IN BROWSER',
    'button|REFRESH CODE',
  ]);
  const sw = document.querySelector('.form-switch input');
  const clicks = events(document.querySelector('.form-switch label'), ['click']);
  assert.strictEqual(A.setValue(idxOf(A, 'Streamer Mode'), 'false'), true);
  assert.deepStrictEqual(clicks, []);
  assert.strictEqual(A.setValue(idxOf(A, 'Streamer Mode'), 'true'), true);
  assert.deepStrictEqual(clicks, ['click']);
  assert.ok(window.document.contains(sw));
});

test('audio: real sliders with their range, the voice settings, no stray percent signs', () => {
  const { document, A } = load('audio');
  const r = scrape(A);
  const engines = r.elements.find((e) => e.text === 'ENGINES');
  assert.strictEqual(engines.controlType, 'range');
  assert.deepStrictEqual([engines.min, engines.max, engines.step], [0, 100, 1]);
  assert.ok(!r.elements.some((e) => e.text === '%'));
  assert.strictEqual(A.setValue(engines.idx, '70'), true);
  assert.strictEqual(document.querySelector('.ini-range-audio').value, '70');
  assert.ok(lines(A).includes('select|FLIGHT ENGINEER VOICE = ON {ON / OFF}'));
});

test('the top-of-descent pause comes first as an alert, and its unpause button still works', () => {
  const { document, A } = load('ofp');
  const overlay = document.querySelector('.tod-pause-overlay');
  reveal(overlay);
  overlay.style.pointerEvents = 'none';
  const all = lines(A);
  assert.deepStrictEqual(all.slice(0, 2), [
    'alert|AUTO PAUSE TRIGGERED BY TOD. CLICK UNPAUSE SIM button to continue [k=alert:todpause, assertive]',
    'button|Unpause sim',
  ]);
  const seen = events(overlay.querySelector('button'), ['click']);
  assert.strictEqual(A.clickElement(2), true);
  assert.deepStrictEqual(seen, ['click']);
});

test('"unchanged" only while nothing the pilot reads has changed, values included', () => {
  const { window, document, A } = load('loadsheet');
  assert.ok(!JSON.parse(A.scrape()).unchanged);
  assert.strictEqual(JSON.parse(A.scrape()).unchanged, true);
  document.getElementById('TANK1_INPUT').value = '1.0';            // a property, no DOM mutation
  assert.ok(!JSON.parse(A.scrape()).unchanged);
  document.querySelector('.header-bar').className += ' busy';        // a hidden part of the tablet
  assert.strictEqual(JSON.parse(A.scrape()).unchanged, true);
  window.eval(agentSource());                                       // re-injected: the first answer is full
  assert.ok(!JSON.parse(window.__MSFSBA_L1011_EFB.scrape()).unchanged);
});

test('the tablet keyboard and hidden pages are never read', () => {
  const { document, A } = load('loadsheet');
  const before = lines(A).join('\n');
  reveal(document.getElementById('osk-num'));
  assert.strictEqual(lines(A).join('\n'), before);
});

test('a tablet whose power wrapper says off is still read', () => {
  // Live: the #Electricity wrapper around the tablet read state="off" with the aircraft battery off,
  // while the tablet stayed displayed and a tab press still changed the page.
  const { document, A } = load('ground');
  const root = document.getElementById('iniEFB');
  const wrapper = document.createElement('div');
  wrapper.id = 'Electricity';
  wrapper.setAttribute('state', 'off');
  wrapper.setAttribute('data-vis', '1');
  root.parentNode.insertBefore(wrapper, root);
  wrapper.appendChild(root);
  assert.strictEqual(scrape(A).page, 'Ground');
  assert.ok(lines(A).includes('button|Door 1 left: closed [k=gnd:door_1L_button, ac]'));
});

test('every control has a name, and keys are unique on every page', () => {
  for (const name of ['ground', 'loadsheet', 'takeoff', 'landing', 'ofp', 'charts', 'flows', 'aircraft', 'panelstates', 'helper', 'thirdparty', 'audio']) {
    const { A } = load(name);
    const els = scrape(A).elements;
    for (const e of els) if (e.clickable || e.controlType) assert.ok(e.text.trim().length > 0, name + ': a control with no name');
    const keys = els.filter((e) => e.key).map((e) => e.key);
    assert.strictEqual(new Set(keys).size, keys.length, name + ': a key repeats');
  }
});
