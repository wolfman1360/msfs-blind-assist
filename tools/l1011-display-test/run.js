'use strict';
// Loads a captured iniBuilds L-1011 Coherent view into jsdom and runs one of MSFSBA's in-page agents
// in it, the way CoherentDisplayClient does live: the script is evaluated in the page, returns its
// install marker, and scrape() is then called. Fixtures are live captures taken with stamp.js
// (docs/l1011.md, "Capturing a display fixture"): visible elements carry data-vis="1", every
// element data-rect.
const fs = require('fs');
const path = require('path');
const { JSDOM } = require('jsdom');

const RESOURCES = path.join(__dirname, '..', '..', 'MSFSBlindAssist', 'Resources');

function agentSource(agentFile) {
  return fs.readFileSync(path.join(RESOURCES, agentFile), 'utf8');
}

function load(fixtureName, agentFile) {
  const html = fs.readFileSync(path.join(__dirname, 'fixtures', fixtureName + '.html'), 'utf8');
  const dom = new JSDOM('<!DOCTYPE html><html>' + html + '</html>', { runScripts: 'outside-only' });
  const window = dom.window;
  const installed = window.eval(agentSource(agentFile));
  return { window, document: window.document, installed };
}

function scrape(window) {
  return JSON.parse(window.__MSFSBA_DISP.scrape());
}

module.exports = { load, scrape, agentSource };
