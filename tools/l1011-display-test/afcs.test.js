'use strict';
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, agentSource } = require('./run');

// coherent-l1011-afcs-agent.js against the glareshield panel captured live on the runway at CYYZ.
// L1011AfcsWindowsTests (C#) formats these same rows into the lines the pilot reads.
const AGENT = 'coherent-l1011-afcs-agent.js';

test('the agent installs, says so, and a second install keeps the first', () => {
  const { window, installed } = load('afcs', AGENT);
  assert.strictEqual(installed, 'MSFSBA_DISP_INSTALLED');
  const first = window.__MSFSBA_DISP;
  assert.strictEqual(first.kind, 'l1011-afcs');
  assert.strictEqual(window.eval(agentSource(AGENT)), 'MSFSBA_DISP_INSTALLED');
  assert.strictEqual(window.__MSFSBA_DISP, first);
});

test('the captured panel scrapes to its power state and the eight windows as drawn', () => {
  const { window } = load('afcs', AGENT);
  assert.deepStrictEqual(scrape(window), {
    ok: true,
    rows: ['power|on', '1|100', '2|', '3|000', '4|000', '5|000', '6|00000', '7|AOA', '8|T/O'],
  });
});

test('an unpowered panel says so', () => {
  const { window, document } = load('afcs', AGENT);
  document.getElementById('customElectricity').setAttribute('state', 'off');
  assert.strictEqual(scrape(window).rows[0], 'power|off');
});

test('a window the gauge redraws is read fresh, padding and all', () => {
  const { window, document } = load('afcs', AGENT);
  document.getElementById('screen_8').textContent = 'VS';
  document.getElementById('screen_2').textContent = '+X1500';
  const rows = scrape(window).rows;
  assert.strictEqual(rows[2], '2|+X1500');
  assert.strictEqual(rows[8], '8|VS');
});

test('a missing window reads empty instead of failing the scrape', () => {
  const { window, document } = load('afcs', AGENT);
  document.getElementById('screen_5').remove();
  const result = scrape(window);
  assert.strictEqual(result.ok, true);
  assert.strictEqual(result.rows[5], '5|');
});

test('scraping changes nothing in the page', () => {
  const { window, document } = load('afcs', AGENT);
  const before = document.documentElement.outerHTML;
  scrape(window);
  scrape(window);
  assert.strictEqual(document.documentElement.outerHTML, before);
});
