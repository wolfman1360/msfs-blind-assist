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

// The tablet's frame, laid out as it renders live (2026-10-04): #iniEFB holds the maintenance timer
// list, then one div with the powered-off screen, the simulation-rate badge, the control box (the
// gear icon's panel, with its dimming background and its panel-state confirmation) and the header
// bar, then the page renderer and the pause dialog. o: { home, chargeIcon, simRate, controlBox,
// confirm, poweredOff, paused: [title, text, resumeShown], timers: html, maintenanceShown,
// pageClass } — everything else hidden, as at rest.
function tablet(inner, extra, o) {
  o = o || {};
  const hide = shown => (shown ? '' : ' data-display="none"');
  return '<div id="iniEFB">' +
    '<div id="timerContainer"' + hide(!!o.timers) + '>' + (o.timers || '') + '</div>' +
    '<div>' +
    '<div id="powered-off"' + hide(o.poweredOff) + (o.poweredOff ? ' data-rect="0,0,2048,1476"' : '') + '></div>' +
    '<div id="bgimg"></div>' +
    '<div class="sim-rate"' + hide(!!o.simRate) + '><span>SIMULATION RATE: ' + (o.simRate || '2x') + '</span></div>' +
    '<div id="control-box-background" class="control-box-background"' + hide(o.controlBox) + (o.controlBox ? ' data-rect="0,0,2048,1476"' : '') + '></div>' +
    '<div id="control-box"' + hide(o.controlBox) + '><div class="row mt-5"><div class="col-sm-12"><div class="d-grid gap-2 mt-5 mb-5">' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="powerOff" type="button">Power Off EFB</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="lockEfb" type="button">Lock EFB</button><hr>' +
    '<h1 class="text-center text-white">TIME COMPRESSION</h1><div class="row time-compression">' +
    '<div class="col"><button class="btn btn-primary mb-1 btn-custom w-100">DISABLED</button></div>' +
    '<div class="col"><button class="btn btn-primary mb-1 btn-custom w-100">MAX 2x</button></div>' +
    '<div class="col"><button class="btn btn-primary mb-1 btn-custom w-100">MAX 4x</button></div></div>' +
    '<div class="text-center text-white mt-4 time-compression__label">Time Compression: OFF</div><hr>' +
    '<h1 class="text-center text-white">Brightness</h1><input type="range" class="ini-range" min="0" max="100" step="0.1" id="brightness" value="75"><hr>' +
    '<h1 class="text-center text-white">Panel States</h1>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="panelstate_0" type="button">Cold and Dark</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="panelstate_1" type="button">Ready for Takeoff</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-custom" id="panelstate_2" type="button">On APU</button>' +
    '<button class="btn btn-primary mb-5 btn-lg btn-custom" id="panelstate_3" type="button">On GPU</button>' +
    '</div></div></div></div>' +
    '<div id="confirm-box"' + hide(o.confirm) + '><div class="row"><div class="col-sm-12"><div class="d-grid gap-2 mt-5 mb-5">' +
    '<h1 class="mb-3 text-center text-white">Confirm New Panel State</h1>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-success" id="confirmBoxConfirmButton" type="button">Confirm</button>' +
    '<button class="btn btn-primary mb-2 btn-lg btn-danger" id="confirmBoxCancelButton" type="button">Cancel</button></div></div></div></div>' +
    '<div id="header-bar"><div id="left-menu-bar"><div class="menu-bar-item ml-5 mt-3"><img id="menu-home-button" src="home.png"' + hide(o.home !== false) + '></div></div>' +
    '<div id="right-menu-bar">' +
    '<div class="menu-bar-item wider"><button id="toggle-maintenance" class="btn btn-sm btn-danger"' + hide(o.maintenanceShown) + '><img id="maint-spin" class="spin" src="convert.png"></button></div>' +
    '<div class="menu-bar-item wider"><div class="time" title="LOCAL TIME"><span class="time-small">LOCAL</span><br>1357</div></div>' +
    '<div class="menu-bar-item wider"><div class="time" title="ZULU TIME"><span class="time-small">ZULU</span><br>2057</div></div>' +
    '<div class="menu-bar-item"><img id="charge-state" src="/Pages/VCockpit/Instruments/ini-common/Icons/' + (o.chargeIcon || 'plug.png') + '"></div>' +
    '<div class="menu-bar-item" id="control-box-button" data-rect="0,1950,2040,100"><img id="control-box-button-icon" src="gear.png" data-rect="10,1960,2030,90"></div>' +
    '</div></div>' +
    '</div>' +
    '<div id="renderer"><div class="' + (o.pageClass ? o.pageClass + ' ' : '') + 'visiblePage">' + inner + '</div></div>' +
    '<div class="paused-overlay' + (o.paused ? '' : ' hidden') + '"' + hide(!!o.paused) + '><div id="paused" class="paused-overlay__dialog">' +
    '<h2 class="text-danger">' + (o.paused ? o.paused[0] : '') + '</h2><p>' + (o.paused ? o.paused[1] : '') + '</p>' +
    '<button type="button" class="btn btn-secondary' + (o.paused && o.paused[2] ? '' : ' hidden') + '"' + hide(!!(o.paused && o.paused[2])) + '><span>RESUME FLIGHT</span></button></div></div>' +
    '</div>' + (extra || '');
}

function weights(p1Rect, p2Rect, forwardShown, unit, extra) {
  return tablet('<div id="weights-bg"></div><div id="weights">' + page1(p1Rect, forwardShown) + page2(p2Rect, unit) + '</div>', extra);
}

const ON = '0,0,2048,1536', OFF_LEFT = '0,-2048,0,1536', OFF_RIGHT = '0,2048,4096,1536';
const files = {
  // The lock screen: the unlock handler is the tablet's onclick on the picture INSIDE the button
  // (LockComponent sets it on document.querySelector('.home-button')), never on the button itself.
  'lock': tablet('<div id="lockscreen"><div class="lock-time mt-5 text-center">1355</div>' +
    '<div class="unlock-button" data-rect="688,152,1895,869"><div class="menu-row-item text-center" data-rect="688,152,1895,869">' +
    '<img class="home-button" src="unlock-button.png" data-rect="688,933,1114,869"></div></div></div>', '', { home: false }),
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
  // Settings, Third Party Settings: the SimBrief box is labelled only by its heading.
  'settings': tablet('<div id="settings-bg"></div><div id="settings"><div id="settings-container" class="row">' +
    '<h1 class="mb-2">SETTINGS</h1><div class="card"><div class="card-body bg-dark"><h1 class="mt-2">Third Party Settings</h1>' +
    '<div class="row"><div class="col-sm-4"><h2 class="mt-2 text-info">SimBrief</h2><div class="row"><div class="col">' +
    '<div class="d-grid gap-2 mb-1 p-3"><input id="simbrief" class="form-control form-control-lg w-100" type="text" name="simbrief" value=""></div></div></div></div>' +
    '<div class="col-sm-4"><h2 class="mt-2 text-info">Hoppie</h2><div class="row"><div class="col">' +
    '<div class="d-grid gap-2 mb-1 p-3"><input id="hoppie" class="form-control form-control-lg w-100" type="password" name="hoppie" value=""></div></div></div></div>' +
    '</div></div></div></div></div>'),
  'myflight': tablet('<div id="flight">' +
    '<div class="row"><h2>DEPARTURE</h2><div id="dep_icao">CYYZ</div><div id="depcit">Toronto/Pearson Intl</div></div>' +
    '<div class="row"><h2>ARRIVAL</h2><div id="arr_icao">CYUL</div></div>' +
    '<div class="row"><button id="btn_simbrief" class="btn btn-primary">IMPORT FROM SIMBRIEF</button></div></div>')
};

for (const name of Object.keys(files))
  fs.writeFileSync(path.join(__dirname, name + '.html'), files[name] + '\n');
console.log('wrote', Object.keys(files).length, 'fixtures');
