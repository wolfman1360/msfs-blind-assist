'use strict';
// Loads a compact live capture of the L-1011 Paper Flight Bag (fixtures/efb-*.html) into jsdom and runs
// coherent-l1011-efb-agent.js in it, the way CoherentPmdgEfbClient does live. The captures were taken on
// the live tablet (2026-10-03) with the visibility stamped from the live computed style
// (data-vis="1"), then compacted: only the visible page (on the Menu, only the visible pane), no inline
// handlers, decorative images, coordinates or field values, and no personal data (the OFP text is a
// two-line stand-in, the Navigraph code is removed). Not a test: node --test does not pick it up.
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const AGENT = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources', 'coherent-l1011-efb-agent.js');

function agentSource() {
  return fs.readFileSync(AGENT, 'utf8');
}

// Shown = data-vis="1", answered through getComputedStyle and getBoundingClientRect as the live page
// would; pointer-events comes from the nearest inline style (how the tablet makes a control
// click-through); opacity from the element's own inline style.
function load(name) {
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', 'efb-' + name + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html><body>' + html + '</body></html>', { runScripts: 'outside-only' });
  const { window } = dom;
  const vis = (el) => !!el && !!el.getAttribute && el.getAttribute('data-vis') === '1';
  const pointerEvents = (el) => {
    for (let e = el; e && e.nodeType === 1; e = e.parentElement) if (e.style && e.style.pointerEvents) return e.style.pointerEvents;
    return 'auto';
  };
  window.getComputedStyle = (el) => ({
    display: vis(el) ? 'block' : 'none',
    visibility: 'visible',
    pointerEvents: pointerEvents(el),
    opacity: (el.style && el.style.opacity) || '1',
  });
  window.Element.prototype.getBoundingClientRect = function () {
    const on = vis(this);
    return { top: 0, left: 0, right: on ? 100 : 0, bottom: on ? 20 : 0, width: on ? 100 : 0, height: on ? 20 : 0, x: 0, y: 0 };
  };
  const installed = window.eval(agentSource());
  return { window, document: window.document, A: window.__MSFSBA_L1011_EFB, installed };
}

// Marks an element and its subtree shown (as a popup the tablet opens), or hidden.
function reveal(el) {
  el.setAttribute('data-vis', '1');
  if (el.style) el.style.display = '';
  for (const c of el.querySelectorAll('*')) if (!(c.style && c.style.display === 'none')) c.setAttribute('data-vis', '1');
}

function conceal(el) {
  el.removeAttribute('data-vis');
  for (const c of el.querySelectorAll('*')) c.removeAttribute('data-vis');
}

// The last full answer per agent: an "unchanged" answer means it still holds, exactly as the client
// keeps its last push.
const lastFull = new WeakMap();

function scrape(A) {
  const r = JSON.parse(A.scrape());
  if (!r.ok) throw new Error('scrape failed: ' + r.error);
  if (r.unchanged) return lastFull.get(A);
  lastFull.set(A, r);
  return r;
}

// One readable line per element: "button|Door 1 left: closed [k=gnd:door_1L_button, ac]".
function line(e) {
  const kind = e.controlType || e.kind || 'txt';
  let s = kind + '|' + e.text;
  if (e.controlType && e.controlType !== 'pre') s += ' = ' + e.value;
  if (e.options) s += ' {' + e.options.join(' / ') + '}';
  const tags = [];
  if (e.key) tags.push('k=' + e.key);
  if (e.announceChange) tags.push('ac');
  if (e.live) tags.push(e.live);
  if (e.disabled) tags.push('dis');
  return tags.length ? s + ' [' + tags.join(', ') + ']' : s;
}

function lines(A) {
  return scrape(A).elements.map(line);
}

// Index of the first element whose text starts with the given words.
function idxOf(A, start) {
  const el = scrape(A).elements.find((e) => e.text.indexOf(start) === 0);
  if (!el) throw new Error('no element starting "' + start + '"');
  return el.idx;
}

module.exports = { load, reveal, conceal, scrape, line, lines, idxOf, agentSource };
