'use strict';
// The A300 tablet's lock screen and header. The tablet wires its controls with element.onclick, and
// two of them hang the handler on the picture INSIDE the control rather than on the control: the
// lock screen's Unlock (on img.home-button) and the header's gear (on #control-box-button-icon).
// A press is dispatched where a finger would land, so the handler inside the control runs.
const test = require('node:test');
const assert = require('node:assert');
const { load, scrape, find } = require('./run');

for (const hitTest of [true, false]) {
  test('Unlock runs the handler on the picture inside the button' + (hitTest ? '' : ', with no hit testing'), () => {
    const { A, window } = load('lock', { noHitTest: !hitTest });
    const r = scrape(A);
    assert.strictEqual(r.page, 'Locked');
    let unlocked = 0, clicks = 0;
    window.document.querySelector('.unlock-button img.home-button').onclick = () => unlocked++;
    window.document.querySelector('.unlock-button').addEventListener('click', () => clicks++);
    assert.ok(A.clickElement(find(r, 'Unlock').idx));
    assert.strictEqual(unlocked, 1, 'the tablet unlocks');
    assert.strictEqual(clicks, 1, 'one click event');
  });
}
