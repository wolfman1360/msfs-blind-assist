'use strict';
// The A300 tablet's Settings page. Its SimBrief box takes the numeric SimBrief Pilot ID: the
// tablet fetches xml.fetcher.php?userid=, so a user name never loads a plan. The box's only
// label is its "SimBrief" heading, which does not say so.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape } = require('./run');

const fields = r => r.elements.filter(e => e.controlType === 'text').map(e => e.text);

test('the SimBrief box says it wants the Pilot ID number', () => {
  const r = scrape(load('settings', {}).A);
  assert.strictEqual(r.page, 'Settings');
  assert.deepStrictEqual(fields(r), ['SimBrief Pilot ID, numbers only', 'Hoppie']);
});
