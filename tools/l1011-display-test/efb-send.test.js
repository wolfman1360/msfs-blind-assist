'use strict';
const test = require('node:test');
const assert = require('node:assert');
const { load, lines, idxOf } = require('./efb-run');

// The Send to INS read-back of coherent-l1011-efb-agent.js. What the aircraft does (package 1.0.8,
// docs/l1011.md "Paper Flight Bag"): the press downloads SimBrief's latest plan, writes its navlog to
// the DataStore (NAVIGRAPH_ARRAY from the captain's tablet, NAVIGRAPH_ARRAY_2 from the first
// officer's), the global NAVIGRAPH_ROUTE_OVERLAY (the origin, then the navlog), then fires
// H:EFB_TO_INS_SENT or _2. One second later the INS writes the waypoints it kept to
// IMPORTED_WAYPOINTS(_n), or empties it while the unit is off. The plan here is synthetic.

const PLAN = [
  fix('BOACH', 'wpt', '1'), fix('KENNO'), fix('RUSME'), fix('TOC', 'ltlg'), fix('OAL', 'vor'), fix('TOD', 'ltlg'),
  fix('SYRAH', 'wpt', '1'), fix('KSFO', 'apt', '1'),
];
const OFP = 'KLAS/LAS KSFO/SFO SYNTHETIC OFP\nROUTE BOACH KENNO RUSME OAL SYRAH\n';
const OLD_OFP = 'KLAS/LAS KPHX/PHX SYNTHETIC OLDER OFP\nROUTE BOACH TRALR DRK\n';

function fix(ident, type, sid) {
  return { index: 0, ident, type: type || 'wpt', icao_region: 'K2', region_code: 'K2', pos_lat: '37.29', pos_long: '-117.31', is_sid_star: sid || '0' };
}

// The DataStore stores what msfs-sdk's DataStore.set writes: the JSON of a JSON string.
function twice(v) {
  return JSON.stringify(JSON.stringify(v));
}

function imported(idents) {
  return JSON.stringify(idents.map((id, i) => 'SB' + String(i + 1).padStart(2, '0') + ' - ' + id.slice(0, 5).padEnd(5, ' ') + '/N3717.9/W11718.6').join('\n'));
}

function key(base, unit) {
  return unit === 1 ? base : base + '_' + unit;
}

// One tablet view with what the sim gives it: the DataStore, L:vars, the H: event stream and a clock.
function tablet(seat, ofp) {
  const sim = { store: {}, lvars: {}, handlers: [], cleared: 0, t: 5000000 };
  const before = (window) => {
    const root = window.document.createElement('ini-efb-l1011');
    root.setAttribute('url', 'coui://html_ui/Pages/VCockpit/Instruments/SCREENS/PFB/ini-efb-l1011.html?Index=' + seat);
    window.document.body.appendChild(root);
    window.GetStoredData = (k) => (k in sim.store ? sim.store[k] : '');
    window.SimVar = { GetSimVarValue: (n) => (n in sim.lvars ? sim.lvars[n] : 0) };
    window.Coherent = {
      on: (name, fn) => {
        const h = { name, fn, live: true };
        sim.handlers.push(h);
        return { clear: () => { h.live = false; sim.cleared++; } };
      },
    };
    window.Date.now = () => sim.t;
  };
  const t = load('ofp', before);
  if (typeof ofp === 'string') t.document.querySelector('.simbrief-viewer__plan pre').textContent = ofp;
  sim.fire = (name) => {
    for (const h of sim.handlers) if (h.live && h.name === 'OnInteractionEvent') h.fn('', [name]);
  };
  sim.wait = (ms) => { sim.t += ms; };
  // What one press does on the aircraft's side, as seen from this tablet.
  sim.send = (navlog, origin, keptByUnit) => {
    const sfx = seat === 1 ? 'NAVIGRAPH_ARRAY' : 'NAVIGRAPH_ARRAY_2';
    sim.store[sfx] = twice(navlog);
    sim.store.NAVIGRAPH_ROUTE_OVERLAY = twice([{ lat: 36.08, lon: -115.15, name: origin, IS_MAP: false }]
      .concat(navlog.map((f) => ({ lat: 37.29, lon: -117.31, name: f.ident, IS_MAP: false }))));
    sim.fire(seat === 1 ? 'EFB_TO_INS_SENT' : 'EFB_TO_INS_SENT_2');
    for (const unit of Object.keys(keptByUnit || {})) sim.store[key('IMPORTED_WAYPOINTS', +unit)] = imported(keptByUnit[unit]);
  };
  return Object.assign(t, { sim });
}

function press(A) {
  assert.strictEqual(A.clickElement(idxOf(A, 'Send to INS')), true);
}

function alerts(A) {
  return lines(A).filter((l) => l.startsWith('alert|'));
}

const KEPT = ['KENNO', 'RUSME', 'OAL'];

test('Send to INS reads back what the INS took: the airports, the waypoints, the batches and the first one', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  assert.deepStrictEqual(alerts(A), [], 'nothing before the aircraft has sent');
  sim.wait(1200);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(1000);
  assert.deepStrictEqual(alerts(A), [], 'nothing until the INS has had time to load');
  sim.wait(1600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. [k=alert:inssend, assertive]',
  ]);
});

test('the first officer\'s tablet reads its own keys and names INS 2', () => {
  const { A, sim } = tablet(2, OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 2: KEPT });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 2: 3 waypoints in 1 batch, first KENNO. [k=alert:inssend, assertive]',
  ]);
});

test('with the SID and STAR import on the INS keeps those fixes too', () => {
  const { A, sim } = tablet(1, OFP);
  sim.lvars['L:INI_SIDSTAR_IMPORT'] = 1;
  press(A);
  sim.send(PLAN, 'KLAS', { 1: ['BOACH', 'KENNO', 'RUSME', 'OAL', 'SYRAH'] });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 5 waypoints in 1 batch, first BOACH. [k=alert:inssend, assertive]',
  ]);
});

test('nine waypoints a batch: twenty en-route fixes are three batches', () => {
  const long = [];
  for (let i = 1; i <= 20; i++) long.push(fix('FIX' + String(i).padStart(2, '0')));
  long.push(fix('KSFO', 'apt', '1'));
  const { A, sim } = tablet(1, 'KLAS KSFO\n' + long.map((f) => f.ident).join(' '));
  press(A);
  sim.send(long, 'KLAS', { 1: long.slice(0, 20).map((f) => f.ident) });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 20 waypoints in 3 batches, first FIX01. [k=alert:inssend, assertive]',
  ]);
});

test('units the INS passes the route to in Remote are named with it', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT, 2: KEPT, 3: KEPT });
  sim.wait(1000);
  sim.fire('EFB_TO_INS_SENT_2');
  sim.fire('EFB_TO_INS_SENT_3');
  sim.wait(1600);
  assert.deepStrictEqual(alerts(A), [], 'INS 2 and 3 load a second after their own event');
  sim.wait(400);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1, 2 and 3: 3 waypoints in 1 batch, first KENNO. [k=alert:inssend, assertive]',
  ]);
});

test('an H: event named like an object property is not taken for a send', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.fire('constructor');
  sim.fire('toString');
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.fire('hasOwnProperty');
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. [k=alert:inssend, assertive]',
  ]);
});

test('a unit that was off kept nothing, and the read-back says so', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: [] });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. INS 1 is off and kept nothing: turn it on, then send again. [k=alert:inssend, assertive]',
  ]);
});

test('a route with every fix on the SID or STAR leaves the INS nothing, and says which setting keeps them', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.send([fix('BOACH', 'wpt', '1'), fix('SYRAH', 'wpt', '1'), fix('KSFO', 'apt', '1')], 'KLAS', { 1: [] });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1, but the INS kept no waypoints: every fix is on the SID or STAR. Turn on IMPORT SID/STAR WPTS (Menu, Aircraft settings), then send again. [k=alert:inssend, assertive]',
  ]);
});

test('an OFP on this tablet older than the plan sent is named in the read-back', () => {
  const { A, sim } = tablet(1, OLD_OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. This tablet\'s OFP is an older plan: press Reload OFP to read the one sent. [k=alert:inssend, assertive]',
  ]);
});

// Live: the INS stores its list a second after the event, so the page warning spoke first and the
// read-back then said the same thing again.
test('while a send is being read back, the OFP page does not speak its own warning first', () => {
  const { A, sim } = tablet(1, OLD_OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(1100);
  assert.deepStrictEqual(alerts(A), []);
  assert.ok(lines(A).includes('static|This OFP differs from the route in INS 1 (to KSFO). Reload OFP and Send to INS both use SimBrief\'s latest plan. [k=status:ofpdiffers]'));
  sim.wait(1000);
  assert.deepStrictEqual(alerts(A).length, 1);
  assert.ok(alerts(A)[0].indexOf('older plan') > 0);
});

// Live: after Reload OFP the kept line still told the pilot to reload.
test('once the OFP is reloaded, the kept read-back line no longer calls it older', () => {
  const { A, sim, document } = tablet(1, OLD_OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(2600);
  assert.ok(alerts(A)[0].indexOf('older plan') > 0);
  document.querySelector('.simbrief-viewer__plan pre').textContent = OFP;
  sim.wait(20000);
  const all = lines(A);
  assert.strictEqual(all[all.indexOf('button|Send to INS') + 1],
    'static|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. [k=status:inssend]');
  assert.ok(!all.some((l) => l.indexOf('differs') >= 0));
});

test('the origin is left out when the overlay is not this plan\'s', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.store.NAVIGRAPH_ROUTE_OVERLAY = twice([{ name: 'KLAS' }, { name: 'TRALR' }, { name: 'KPHX' }]);
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|Route to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. [k=alert:inssend, assertive]',
  ]);
});

test('no event within 15 seconds of the press: nothing was sent, and a late one is still read back', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.wait(14000);
  assert.deepStrictEqual(alerts(A), []);
  sim.wait(1000);
  assert.deepStrictEqual(alerts(A), [
    'alert|Send to INS: nothing sent after 15 seconds. The tablet first downloads SimBrief\'s latest plan: check the SimBrief ID (Menu, Third party) and the internet connection. [k=alert:inssend, assertive]',
  ]);
  sim.wait(5000);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(2600);
  assert.deepStrictEqual(alerts(A), [
    'alert|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. [k=alert:inssend, assertive]',
  ]);
});

test('only the pilot\'s own press is read back, never another tablet\'s send or a mirror', () => {
  const { A, sim } = tablet(2, OFP);
  sim.fire('EFB_TO_INS_SENT_2');
  sim.wait(3000);
  assert.deepStrictEqual(alerts(A), [], 'an event with no press from this window');
  press(A);
  sim.fire('EFB_TO_INS_SENT');
  sim.wait(3000);
  assert.deepStrictEqual(alerts(A), [], 'the captain\'s send is not this tablet\'s');
});

test('the read-back is spoken once, then stays on the OFP page as a plain line under Send to INS', () => {
  const { A, sim } = tablet(1, OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(2600);
  assert.strictEqual(alerts(A).length, 1);
  sim.wait(20000);
  const all = lines(A);
  assert.deepStrictEqual(all.filter((l) => l.startsWith('alert|')), []);
  const at = all.indexOf('button|Send to INS');
  assert.strictEqual(all[at + 1], 'static|KLAS to KSFO sent to INS 1: 3 waypoints in 1 batch, first KENNO. [k=status:inssend]');
});

test('the OFP page warns when its plan is not the route an INS holds', () => {
  const { A, sim } = tablet(2, OLD_OFP);
  sim.store.NAVIGRAPH_ARRAY = twice(PLAN);
  sim.store.IMPORTED_WAYPOINTS = imported(KEPT);
  assert.deepStrictEqual(alerts(A), [
    'alert|This OFP differs from the route in INS 1 (to KSFO). Reload OFP and Send to INS both use SimBrief\'s latest plan. [k=alert:ofpdiffers, assertive]',
  ]);
});

test('no warning when the OFP shows the route an INS holds, or when no INS holds a route it can match', () => {
  const t1 = tablet(2, OFP);
  t1.sim.store.NAVIGRAPH_ARRAY = twice(PLAN);
  t1.sim.store.IMPORTED_WAYPOINTS = imported(KEPT);
  assert.deepStrictEqual(alerts(t1.A), [], 'the same plan');
  const t2 = tablet(2, OLD_OFP);
  t2.sim.store.NAVIGRAPH_ARRAY = twice(PLAN);
  t2.sim.store.IMPORTED_WAYPOINTS = JSON.stringify('');
  assert.deepStrictEqual(alerts(t2.A), [], 'INS 1 off: it holds nothing');
  const t3 = tablet(2, OLD_OFP);
  t3.sim.store.NAVIGRAPH_ARRAY = twice(PLAN);
  t3.sim.store.IMPORTED_WAYPOINTS = imported(['TRALR', 'DRK']);
  assert.deepStrictEqual(alerts(t3.A), [], 'INS 1 holds a list the stored navlog did not make');
});

test('after the read-back has named the older OFP, the page line is not spoken again', () => {
  const { A, sim } = tablet(1, OLD_OFP);
  press(A);
  sim.send(PLAN, 'KLAS', { 1: KEPT });
  sim.wait(2600);
  assert.strictEqual(alerts(A).length, 1);
  assert.ok(alerts(A)[0].indexOf('older plan') > 0);
  sim.wait(20000);
  assert.deepStrictEqual(alerts(A), []);
  assert.ok(lines(A).includes('static|This OFP differs from the route in INS 1 (to KSFO). Reload OFP and Send to INS both use SimBrief\'s latest plan. [k=status:ofpdiffers]'));
});

test('a reinstalled reader keeps one event listener', () => {
  const { window, sim } = tablet(1, OFP);
  window.eval(require('./efb-run').agentSource());
  assert.strictEqual(sim.handlers.filter((h) => h.live).length, 1);
  assert.strictEqual(sim.cleared, 1);
});

test('without the sim\'s DataStore or event stream the OFP page reads as before', () => {
  const { A } = load('ofp');
  assert.deepStrictEqual(alerts(A), []);
  press(A);
  assert.deepStrictEqual(alerts(A), []);
});
