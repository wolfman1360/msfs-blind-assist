'use strict';
// Writes the hand-made A300 tablet fixtures: the freighter's Weight and Balance page, whose two
// halves (payload selection, then loading) slide by 100vw, plus My Flight. The structure and ids
// follow the tablet as it renders live (2026-10-04); the words are its short labels only.
// Run: node fixtures/make.js
const fs = require('fs');
const path = require('path');

function preset(id, weight, name) {
  return '<div class="col-sm-4"><button id="' + id + '" class="btn btn-info btn-lg w-100"><img src="' + id + '.png"><h1>' +
    '<span>' + weight + '</span><br><span>' + name + '</span></h1></button></div>';
}

function field(id, label, value, unit) {
  return '<div class="row mb-2"><div class="col-sm-4 text-end ini-input-label">' + label + '</div><div class="col-sm-8">' +
    '<div class="input-group"><input id="' + id + '" class="form-control ini-small-input" type="number" value="' + value + '">' +
    '<span class="input-group-text bg-dark text-white">' + unit + '</span></div></div></div>';
}

function output(id, label, value) {
  return '<div class="row mb-2"><div class="col-sm-6 text-end ini-input-label">' + label + '</div><div class="col-sm-6">' +
    '<input id="' + id + '" class="form-control ini-small-input" type="number" readonly value="' + value + '"></div></div>';
}

function page1(rect, forwardShown) {
  return '<div id="page1" data-rect="' + rect + '">' +
    '<div id="switcher_page2"' + (forwardShown ? '' : ' data-display="none"') + '><img src="chevron-right.png"></div>' +
    '<div class="row"><h1>PAYLOAD SELECTION</h1>' +
    preset('payload_race', "31'000Kgs / 68'300Lbs", 'Racing Team Charter') +
    preset('payload_aero', "26'500Kgs / 58'400Lbs", 'Aero Parts Transport') +
    preset('payload_horse', "15'700Kgs / 34'600Lbs", 'Horse Stable Transport') +
    preset('payload_human', "21'000Kgs / 46'300Lbs", 'Humanitarian Charter') +
    preset('payload_post', "18'500Kgs / 40'800Lbs", 'Postal Freight') +
    preset('payload_cargo', 'Custom', 'Custom Cargo') +
    preset('payload_cargo_sb', 'From SimBrief', 'Custom Cargo') +
    '<div class="col-sm-12"><button id="payload_clear" class="btn btn-danger btn-lg w-100">Unload Cargo</button></div>' +
    '</div></div>';
}

function page2(rect, unit) {
  return '<div id="page2" data-rect="' + rect + '">' +
    '<div id="switcher_page1"><img src="chevron-left.png"></div>' +
    '<div class="row"><div class="col-sm-12"><h1 class="mb-2">WEIGHT AND BALANCE [' + unit + ']</h1>' +
    '<div class="card"><div class="card-body bg-dark">' +
    '<div class="weight-image"><div id="weight_freight_fwd_box"></div><div id="weight_freight_mid_box"></div><div id="weight_freight_aft_box"></div></div>' +
    '<div class="row pb-5"><div class="col-sm-8 offset-sm-2">' +
    '<div class="row mt-5"><div class="col"><div class="row mb-2"><div class="col-sm-8"></div></div></div>' +
    '<div class="col">' + field('input_payload', 'LOAD', '0', unit).replace('class="col-sm-4 text-end ini-input-label"', 'id="input_payload_title" class="col-sm-4 text-end ini-input-label"') + '</div>' +
    '<div class="col">' + field('weight_fuel', 'FUEL', '60110', unit) + '</div></div>' +
    '<div class="row mt-2">' +
    '<div class="col-sm-4">' + field('weight_freight_fwd', 'FWD', '10333', unit) + '</div>' +
    '<div class="col-sm-4">' + field('weight_freight_mid', 'MID', '10333', unit) + '</div>' +
    '<div class="col-sm-4">' + field('weight_freight_aft', 'AFT', '10333', unit) + '</div></div>' +
    '<div class="row pb-5">' +
    '<div class="col-sm-6"><button id="update_from_simbrief" class="ini-small-button w-100 mt-3 mb-2">Update from SimBrief</button></div>' +
    '<div class="col-sm-6"><button id="weight_freight_apply" class="ini-small-button w-100 mt-3 mb-2">Apply Load to Aircraft</button></div></div>' +
    '<div class="row mt-5">' +
    '<div class="col-sm-4">' + output('output_maczfw', 'MACZFW %', '00.0') + output('output_maccg', 'MACGW %', '24.95') + '</div>' +
    '<div class="col-sm-4">' + output('output_zfw', 'ZFW', '000.0') + output('output_tow', 'GW', '102.40') + '</div>' +
    '<div class="col-sm-4">' + output('output_payload', 'PAYLOAD', '031.0') + output('output_blkfuel', 'BLK FUEL', '060.11') + '</div>' +
    '</div></div></div></div></div></div></div></div>';
}

function tablet(inner, extra) {
  return '<div id="iniEFB"><div>' +
    '<div id="header-bar"><div id="left-menu-bar"><div class="menu-bar-item ml-5 mt-3"><img id="menu-home-button" src="home.png"></div></div></div>' +
    '<div id="renderer"><div class="visiblePage">' + inner + '</div></div></div></div>' + (extra || '');
}

function weights(p1Rect, p2Rect, forwardShown, unit, extra) {
  return tablet('<div id="weights-bg"></div><div id="weights">' + page1(p1Rect, forwardShown) + page2(p2Rect, unit) + '</div>', extra);
}

const ON = '0,0,2048,1536', OFF_LEFT = '0,-2048,0,1536', OFF_RIGHT = '0,2048,4096,1536';
const files = {
  // Payload selection on screen; no load chosen yet, so the forward chevron is hidden.
  'weights-selection': weights(ON, OFF_RIGHT, false, 'kg'),
  // A load was chosen and the pilot came back: the forward chevron shows.
  'weights-selection-chosen': weights(ON, OFF_RIGHT, true, 'kg'),
  // The loading half on screen, the selection slid off to the left.
  'weights-loading': weights(OFF_LEFT, ON, true, 'kg'),
  'weights-loading-lbs': weights(OFF_LEFT, ON, true, 'lbs'),
  // A notie toast (appended to the body) over the selection half.
  'weights-toast': weights(ON, OFF_RIGHT, false, 'kg',
    '<div class="notie-container notie-alert notie-background-success"><div class="notie-textbox"><div class="notie-textbox-inner">Payload Removed</div></div></div>'),
  'myflight': tablet('<div id="flight">' +
    '<div class="row"><h2>DEPARTURE</h2><div id="dep_icao">CYYZ</div><div id="depcit">Toronto/Pearson Intl</div></div>' +
    '<div class="row"><h2>ARRIVAL</h2><div id="arr_icao">CYUL</div></div>' +
    '<div class="row"><button id="btn_simbrief" class="btn btn-primary">IMPORT FROM SIMBRIEF</button></div></div>')
};

for (const name of Object.keys(files))
  fs.writeFileSync(path.join(__dirname, name + '.html'), files[name] + '\n');
console.log('wrote', Object.keys(files).length, 'fixtures');
