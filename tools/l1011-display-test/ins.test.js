'use strict';
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, agentSource } = require('./run');

// coherent-l1011-ins-agent.js against INS views captured live at a gate (2026-10-03, INS 1 navigating on the
// POS and DSRTK/STS pages) and on a runway (INS 3, 2026-10-02). The lamps, the selectors and the gauge's page
// globals are not in the DOM, so each test supplies them the way the INS script holds them.
// L1011InsScrapeTests (C#) reads the same rows back.
const AGENT = 'coherent-l1011-ins-agent.js';

function simVars(window, values) {
  window.SimVar = {
    GetSimVarValue: (name) => (Object.prototype.hasOwnProperty.call(values, name) ? values[name] : 0),
  };
}

function slot(ident, lat, lon) {
  return { ident, lat, lon };
}

function liveIns1(window) {
  simVars(window, {
    'L:INS_1_ROTARY_MODE': 3, 'L:INS_1_ROTARY_WPT': 0, 'L:INS_1_ALIGNMENT_STATUS': 1,
    'L:INS_1_READY': 1, 'L:INS_1_HOLD_LIGHT': 1, 'L:INS_1_AUTO_BATCH_ENABLED': 1, 'L:PMS_1_INSERT_LIGHT': 1,
  });
  window.gpsPoints = [slot('', 36.0854, -115.1336), slot('KENNO', 37.2982, -117.3102), slot('RUSME', 37.4942, -117.52)];
  for (let k = 3; k <= 10; k++) window.gpsPoints.push(slot('', 0, 0));
  window.active_batch = 1;
  window.batch_count = 1;
  window.isInput = false;
  window.pms_is_input = false;
  window.pms_line_active = 0;
}

// Byte for byte the rows L1011InsScrapeTests.Live reads back.
const LIVE_ROWS = [
  'unit|1', 'power|on', 'mode|3', 'wheel|0', 'left|36051N', 'right|115080W', 'tk|00', 'dots|123456',
  'status|1', 'entry|', 'lights|READY,HOLD', 'auto_batch|1', 'batch|1|1',
  'wpt_1|KENNO|37.2982|-117.3102', 'wpt_2|RUSME|37.4942|-117.52', 'wpt_3||0|0', 'wpt_4||0|0', 'wpt_5||0|0',
  'wpt_6||0|0', 'wpt_7||0|0', 'wpt_8||0|0', 'wpt_9||0|0',
  'pms_seat|CPT', 'pms_title|TAKEOFF',
  'pms_left_1|GMT', 'pms_right_1|0755Z', 'pms_left_2|EPR', 'pms_right_2|---', 'pms_left_3|PR ALT', 'pms_right_3|1969',
  'pms_left_4|GW', 'pms_right_4|---', 'pms_left_5|FUEL', 'pms_right_5|---', 'pms_left_6|SAT', 'pms_right_6|---',
  'pms_entry|0', 'pms_line|0', 'pms_lights|INSERT',
];

test('the agent installs, says so, and a second install keeps the first', () => {
  const { window, installed } = load('ins1-pos', AGENT);
  assert.strictEqual(installed, 'MSFSBA_DISP_INSTALLED');
  const first = window.__MSFSBA_DISP;
  assert.strictEqual(first.kind, 'l1011-ins');
  assert.strictEqual(window.eval(agentSource(AGENT)), 'MSFSBA_DISP_INSTALLED');
  assert.strictEqual(window.__MSFSBA_DISP, first);
});

test('INS 1 on the position page, navigating, with the SimBrief route loaded', () => {
  const { window } = load('ins1-pos', AGENT);
  liveIns1(window);
  assert.deepStrictEqual(scrape(window).rows, LIVE_ROWS);
});

test('the desired track and status page keeps its blank slots and shows its two dots', () => {
  const { window } = load('ins1-status', AGENT);
  liveIns1(window);
  const rows = scrape(window).rows;
  assert.strictEqual(rows[4], 'left| 0000 ');
  assert.strictEqual(rows[5], 'right|    01 ');
  assert.strictEqual(rows[7], 'dots|25');
});

test('INS 3 takes its unit from the page address and has no live PMS', () => {
  const { window } = load('ins3', AGENT);
  simVars(window, { 'L:INI_CPT_PMS_CLIMB': 1, 'L:PMS_1_INSERT_LIGHT': 1 });
  const rows = scrape(window).rows;
  assert.strictEqual(rows[0], 'unit|3');
  assert.strictEqual(rows[4], 'left|43403N');
  assert.ok(rows.includes('pms_seat|'));
  assert.ok(rows.includes('pms_lights|'));
});

test('the page globals win over the address, and each seat reads its own PMS lights', () => {
  const { window } = load('ins3', AGENT);
  window.insIndex = 2;
  simVars(window, { 'L:INI_FO_PMS_CLIMB': 1, 'L:INI_FO_PMS_CLIMB_2': 1, 'L:INI_CPT_PMS_CRUISE': 1 });
  const rows = scrape(window).rows;
  assert.strictEqual(rows[0], 'unit|2');
  assert.ok(rows.includes('pms_seat|FO'));
  assert.ok(rows.includes('pms_lights|CLIMB,CLIMB_2'));
});

test('an open entry: a latitude, a longitude, a leg', () => {
  const { window } = load('ins1-pos', AGENT);
  liveIns1(window);
  window.isInput = true;
  assert.strictEqual(scrape(window).rows[9], 'entry|left');
  window.isInputRight = true;
  assert.strictEqual(scrape(window).rows[9], 'entry|right');
  window.isTrackChangeInput = true;
  assert.strictEqual(scrape(window).rows[9], 'entry|leg');
});

test('with no SimVar bridge and no page globals it still reads the windows', () => {
  const { window } = load('ins1-pos', AGENT);
  const result = scrape(window);
  assert.strictEqual(result.ok, true);
  const rows = result.rows;
  assert.strictEqual(rows[0], 'unit|1');
  assert.strictEqual(rows[2], 'mode|');
  assert.strictEqual(rows[4], 'left|36051N');
  assert.strictEqual(rows[8], 'status|');
  assert.strictEqual(rows[12], 'batch|0|0');
  assert.strictEqual(rows[13], 'wpt_1||');
});

test('a dark unit and a hidden dot', () => {
  const { window, document } = load('ins1-pos', AGENT);
  liveIns1(window);
  document.getElementById('customElectricity').setAttribute('state', 'off');
  document.getElementById('dot_6').style.display = 'none';
  const rows = scrape(window).rows;
  assert.strictEqual(rows[1], 'power|off');
  assert.strictEqual(rows[7], 'dots|12345');
});

test('a bar in an ident cannot break the row', () => {
  const { window } = load('ins1-pos', AGENT);
  liveIns1(window);
  window.gpsPoints[1].ident = 'A|B';
  assert.strictEqual(scrape(window).rows[13], 'wpt_1|A B|37.2982|-117.3102');
});

test('scraping changes nothing in the page', () => {
  const { window, document } = load('ins1-pos', AGENT);
  liveIns1(window);
  const before = document.documentElement.outerHTML;
  scrape(window);
  scrape(window);
  assert.strictEqual(document.documentElement.outerHTML, before);
});
