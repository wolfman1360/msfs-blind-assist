/*
 * MSFS Blind Assist - iniBuilds L-1011 TriStar Paper Flight Bag (the EFB tablet) reader.
 *
 * Runs inside the "iniEFBL1011_1" (captain) or "iniEFBL1011_2" (first officer) Coherent GT view
 * through the remote debugger (SimConnect/CoherentPmdgEfbClient.ForL1011), for the shared
 * Forms/FBWA380/FbwEfbForm. scrape() reads the visible page as the form's elements;
 * clickElement(idx) and setValue(idx, text) press and type for the pilot. Every rule below was
 * checked on the live tablet (package 1.0.8, 2026-10-03):
 *  - The tablet is plain DOM: el.click() is the press. No pointer or mouse prelude: the drag
 *    sliders would start dragging.
 *  - The eight tab buttons, the ground buttons and the flap buttons have no text, so their names
 *    come from the tables below and their state from classes (door-open, door-closed,
 *    door-transit, active).
 *  - A setting's truth is its label's "active" class, never the radio input's "checked" (they
 *    disagree); clicking the label sets it.
 *  - A typed load-sheet value reaches the aircraft only through the tablet's own keyboard: click
 *    the field (the number pad opens), set the value, then press the pad's submit key.
 *  - The tablet rewrites classes several times a second with no net change, so "nothing changed"
 *    is decided by comparing the produced elements, never by watching the DOM.
 *  - The tablet keeps working when its "Electricity" wrapper says off, so that is ignored.
 * READ ONLY apart from the presses and typing the pilot asks for. Coherent GT is Chromium-49
 * class, so this is ES5.
 */
(function () {
  var MARK = 'MSFSBA_L1011_EFB_INSTALLED';

  var TABS = [
    ['mainmenu-btn', 'Menu', 'page-options'],
    ['testbasic-btn', 'Ground', 'page-test-basic'],
    ['loadsheet-btn', 'Load sheet', 'page-load-sheet'],
    ['ofp-btn', 'OFP', 'page-ofp'],
    ['takeoff-btn', 'Takeoff performance', 'page-takeoff'],
    ['landing-btn', 'Landing performance', 'page-landing'],
    ['charts-btn', 'Charts', 'page-charts'],
    ['enroute-btn', 'Enroute map', 'page-map']
  ];

  var DOORS = [
    ['door_1L_button', 'Door 1 left'], ['door_1R_button', 'Door 1 right'],
    ['door_2L_button', 'Door 2 left'], ['door_2R_button', 'Door 2 right'],
    ['door_3L_button', 'Door 3 left'], ['door_3R_button', 'Door 3 right'],
    ['cargo_fwd_button', 'Forward cargo door'], ['cargo_aft_button', 'Aft cargo door'],
    ['bulkhead_door_button', 'Bulk cargo door']
  ];
  var EQUIPMENT = [
    ['GPU_button', 'Ground power'], ['AC_CART_button', 'Air conditioning cart'], ['JETWAY_button', 'Jetway'],
    ['RAMP_TRUCK_button', 'Ramp truck'], ['luggage_button', 'Luggage'], ['catering_button', 'Catering']
  ];
  var PUSHBACK = [
    ['pushback_aft_button', 'Pushback aft'], ['pushback_left_button', 'Pushback left'],
    ['pushback_right_button', 'Pushback right'], ['pushback_stop_button', 'Stop pushback']
  ];
  // The covers popup's buttons say what a press does; the state is the other side of it.
  var COVER_STATES = { INSTALL: 'not installed', REMOVE: 'installed', OPEN: 'closed', CLOSE: 'open', CALL: 'not called' };

  // [id, name, carries the weight unit]
  var LOAD_GROUPS = [
    ['Fuel', [['TANK1_INPUT', 'Tank 1', true], ['TANK1A_INPUT', 'Tank 1A', true], ['TANK2L_INPUT', 'Tank 2 left', true],
      ['TANK2L_OUT_INPUT', 'Tank 2 left outer', true], ['TANK2R_INPUT', 'Tank 2 right', true],
      ['TANK2R_OUT_INPUT', 'Tank 2 right outer', true], ['TANK3A_INPUT', 'Tank 3A', true], ['TANK3_INPUT', 'Tank 3', true],
      ['TOTAL_FUEL_INPUT', 'Total fuel', true]]],
    ['Cargo', [['FWD_CARGO_INPUT', 'Forward cargo', true], ['AFT_CARGO_INPUT', 'Aft cargo', true],
      ['BULK_CARGO_INPUT', 'Bulk cargo', true], ['TOTAL_CARGO_INPUT', 'Total cargo', true]]],
    ['Passengers', [['FIRST_PAX_INPUT', 'First class passengers', false], ['BUSINESS_PAX_INPUT', 'Business class passengers', false],
      ['ECONOMY_PAX_INPUT', 'Economy passengers', false], ['TOTAL_PAX_INPUT', 'Total passengers', false],
      ['TOTAL_PAX_WEIGHT_INPUT', 'Total passenger weight', true]]],
    ['Weight', [['TOTAL_WEIGHT_INPUT', 'Total weight', true]]]
  ];
  var LOAD_TIMED = [
    ['ls-step1-btn', 'Set initial load state'], ['ls-step2-btn', 'Select final load state'], ['ls-start-load-btn', 'Load'],
    ['ls-start-unload-btn', 'Unload'], ['ls-timed-stop-btn', 'Stop'], ['ls-start-btn', 'Start load or unload']
  ];
  var LOAD_NOTES = ['ls-timed-warning', 'ls-step1-info', 'ls-step2-info', 'ls-realistic-duration-breakdown'];

  var FLAPS = [['flaps_10', '10'], ['flaps_14', '14'], ['flaps_18', '18'], ['flaps_22', '22']];
  // [id, name, unit] -- unit "knots", "epr" (the card drops the leading 1: ".643" is EPR 1.643) or "".
  var TAKEOFF_RESULTS = [
    ['v1_display', 'V1', 'knots'], ['vr_display', 'VR', 'knots'], ['v2_display', 'V2', 'knots'],
    ['f10_display', 'Select flaps 10 at', 'knots'], ['f4_display', 'Select flaps 4 at', 'knots'],
    ['fup_display', 'Select flaps up at', 'knots'], ['f0_display', 'Minimum clean speed', 'knots'],
    ['takeoff_n1_display', 'Takeoff EPR', 'epr'], ['climb_n1_display', 'Climb EPR', 'epr'],
    ['go_around_n1_display', 'Go-around EPR', 'epr'], ['max_cont_n1_display', 'Maximum continuous EPR', 'epr'],
    ['max_n1_display', 'Maximum EPR', 'epr'], ['oat_display', 'Outside air temperature', ''],
    ['selected_flaps_display', 'Selected flaps', ''], ['stabilizer_trim_display', 'Stabilizer trim', '']
  ];
  var LANDING_RESULTS = [
    ['vapp-display', 'Approach speed VAPP', 'knots'], ['vref-display', 'Reference speed VREF', 'knots'],
    ['vref20-display', 'VREF plus 20', 'knots'], ['vref30-display', 'VREF plus 30', 'knots'],
    ['vref40-display', 'VREF plus 40', 'knots'], ['vref60-display', 'VREF plus 60', 'knots'],
    ['adjusted-distance-display', 'Landing distance', '']
  ];

  var SIDEBAR = {
    'btn-autocomplete': 'Autocomplete', 'btn-panelstates': 'Panel states', 'btn-helper': 'Helper',
    'btn-Aircraft': 'Aircraft settings', 'btn-throttle-calib': 'Throttle calibration',
    'btn-thirdparty': 'Third party', 'btn-audio': 'Audio'
  };
  var FE_STATUS = { 'is-matched': 'set', 'is-unmatched': 'not set', 'is-in-progress': 'in progress', 'is-unknown': 'status unknown' };
  // Status lines the menu panes rewrite in place now and then: spoken politely by the form while it
  // is open.
  var LIVE_STATUS = ['panelstates-step-display', 'helper-result', 'helper-armed-label', 'throttle-calib-status'];
  // A countdown changes every second, so it is patched in place (its key) and read when the pilot
  // arrows onto it, never spoken by itself: the A300 tablet never speaks a moving number either.
  var SILENT_STATUS = ['helper-live-dist', 'helper-timer-display'];

  // ---- small helpers ------------------------------------------------------------------------

  function cls(el, c) { return !!(el && el.classList && el.classList.contains(c)); }

  function text(el) { return el ? (el.textContent || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '') : ''; }

  function attached(el) { return !!el && document.documentElement.contains(el); }

  // Shown on screen: displayed, not hidden, and with a size (anything under a display:none
  // ancestor has none). Checked on the control itself, so an empty zero-size wrapper does not
  // hide the values positioned inside it.
  function shown(el) {
    if (!el || el.nodeType !== 1 || !el.getBoundingClientRect) return false;
    var r = el.getBoundingClientRect();
    if (!(r.width > 0 && r.height > 0)) return false;
    var cs = window.getComputedStyle(el);
    return cs.display !== 'none' && cs.visibility !== 'hidden';
  }

  // A control the tablet will not act on: disabled, made click-through, or dimmed to 0.35.
  // el.click() ignores pointer-events, so the reader refuses these itself.
  function inert(el) {
    if (!el || el.disabled) return true;
    if (window.getComputedStyle(el).pointerEvents === 'none') return true;
    var op = parseFloat(el.style ? el.style.opacity : '');
    return !isNaN(op) && op <= 0.5;
  }

  function byId(scope, id) { return scope.querySelector('#' + id); }

  function item(kind, label, o) {
    o = o || {};
    return {
      kind: kind, text: label, value: o.value === undefined || o.value === null ? '' : String(o.value),
      controlType: o.type || '', clickable: !!o.clickable, level: o.level || 0, live: o.live || '',
      disabled: !!o.disabled, options: o.options || null, key: o.key || '', announceChange: !!o.ac,
      min: o.min, max: o.max, step: o.step, act: o.act || null
    };
  }

  function heading(out, label, level) { if (label) out.push(item('heading', label, { level: level || 2 })); }

  function stat(out, label, o) { if (label) out.push(item('static', label, o)); }

  function button(out, label, node, o) {
    o = o || {};
    o.clickable = true;
    o.disabled = o.disabled || inert(node);
    o.act = { press: node };
    out.push(item(o.kind || 'button', label, o));
  }

  function field(out, label, node, o) {
    o = o || {};
    o.type = 'text';
    o.value = node.value;
    o.disabled = inert(node);   // the same rule as the buttons, and as setValue's refusal
    o.act = { field: node };
    out.push(item('', label, o));
  }

  // A pick-one set (members are buttons, labels or divs): one dropdown. A dimmed member is left out,
  // even the selected one; with nothing selectable chosen the value is "(none)", which setValue ignores.
  function pick(out, label, members, isSelected) {
    var opts = [], acts = [], value = '';
    for (var i = 0; i < members.length; i++) {
      var m = members[i];
      if (!m.node || !shown(m.node) || inert(m.node)) continue;
      opts.push(m.text);
      acts.push(m);
      if (isSelected(m.node)) value = m.text;
    }
    if (opts.length === 0) return;
    if (!value) { opts.unshift('(none)'); value = '(none)'; }
    out.push(item('', label, { type: 'select', value: value, options: opts, act: { pick: acts } }));
  }

  function readout(raw, unit) {
    var v = (raw || '').replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, '');
    if (!v || v === '-' || v === '--') return 'not calculated';
    if (unit === 'epr') return /^\.\d+$/.test(v) ? '1' + v : v;
    if (unit === 'knots' && /^\d+$/.test(v)) return v + ' knots';
    return v;
  }

  // ---- the page frame --------------------------------------------------------------------------

  function visiblePage(root) {
    var pages = root.querySelectorAll('.page');
    for (var i = 0; i < pages.length; i++) {
      if (cls(pages[i], 'page--hide')) continue;
      for (var t = 0; t < TABS.length; t++) if (cls(pages[i], TABS[t][2])) return { node: pages[i], tab: t };
    }
    return null;
  }

  function pauseOverlay(root, out) {
    var o = root.querySelector('.tod-pause-overlay');
    if (!o || window.getComputedStyle(o).display === 'none') return;
    var parts = [];
    for (var c = o.firstElementChild; c; c = c.nextElementSibling) if (c.tagName !== 'BUTTON' && text(c)) parts.push(text(c));
    out.push(item('alert', parts.join('. '), { live: 'assertive', key: 'alert:todpause' }));
    var b = o.querySelector('button');
    if (b) button(out, 'Unpause sim', b);
  }

  function simRate(root, out) {
    var spans = root.querySelectorAll('span');
    for (var i = 0; i < spans.length; i++) {
      var t = text(spans[i]);
      if (t.indexOf('SIMULATION RATE') === 0 && shown(spans[i])) stat(out, t, { live: 'polite', key: 'status:simrate' });
    }
  }

  function tabs(root, page, out) {
    for (var t = 0; t < TABS.length; t++) {
      var b = root.querySelector('.navigation-bar #' + TABS[t][0]);
      if (!b) continue;
      button(out, TABS[t][1] + (page && page.tab === t ? ' (current page)' : ''), b, { kind: 'tab' });
    }
  }

  // ---- Ground ------------------------------------------------------------------------------------

  function doorState(b, open, closed) {
    if (cls(b, 'door-transit')) return 'moving';
    if (cls(b, 'door-open')) return open;
    if (cls(b, 'door-closed')) return closed;
    return 'unknown';
  }

  function ground(page, out) {
    heading(out, 'Doors');
    var i, b;
    for (i = 0; i < DOORS.length; i++) {
      b = byId(page, DOORS[i][0]);
      if (b && shown(b)) button(out, DOORS[i][1] + ': ' + doorState(b, 'open', 'closed'), b, { key: 'gnd:' + DOORS[i][0], ac: true });
    }
    b = byId(page, 'open_all_button');
    if (b && shown(b)) button(out, 'Open all doors', b);
    b = byId(page, 'close_all_button');
    if (b && shown(b)) button(out, 'Close all doors', b);
    heading(out, 'Ground equipment');
    for (i = 0; i < EQUIPMENT.length; i++) {
      b = byId(page, EQUIPMENT[i][0]);
      if (b && shown(b)) button(out, EQUIPMENT[i][1] + ': ' + doorState(b, 'on', 'off'), b, { key: 'gnd:' + EQUIPMENT[i][0], ac: true });
    }
    heading(out, 'Pushback');
    for (i = 0; i < PUSHBACK.length; i++) {
      b = byId(page, PUSHBACK[i][0]);
      if (b && shown(b)) button(out, PUSHBACK[i][1], b);
    }
    b = byId(page, 'covers-open-button');
    if (b && shown(b)) button(out, 'Chocks and maintenance', b);

    var popup = byId(page, 'covers-popup');
    if (popup && shown(popup)) {
      heading(out, 'Chocks and maintenance');
      var rows = popup.querySelectorAll('.covers-item');
      for (i = 0; i < rows.length; i++) {
        var title = text(rows[i].querySelector('.covers-item-title'));
        var act = rows[i].querySelector('.covers-item-action-btn');
        if (!title || !act || !shown(act)) continue;
        var caption = text(act).toUpperCase();
        var state = COVER_STATES[caption] || caption.toLowerCase();
        button(out, title + ': ' + state, act, { key: 'cover:' + act.id, ac: true });
      }
      b = byId(popup, 'covers-close-button');
      if (b && shown(b)) button(out, 'Close', b);
    }
  }

  // ---- Load sheet --------------------------------------------------------------------------------

  function weightUnit(scope, kgId, lbId) {
    if (shown(byId(scope, kgId))) return 'thousand kg';
    if (shown(byId(scope, lbId))) return 'thousand lb';
    return 'thousands';
  }

  function loadSheet(page, out) {
    var unit = weightUnit(page, 'load_sheet_kg', 'load_sheet_lb');
    stat(out, 'Units: ' + (unit === 'thousand kg' ? 'thousands of kilograms' : unit === 'thousand lb' ? 'thousands of pounds' : 'unknown'));
    var b = page.querySelector('.simbrief-import-button');
    if (b && shown(b)) button(out, 'SimBrief import', b);
    b = page.querySelector('.load-settings-button');
    if (b && shown(b)) button(out, 'Load settings', b);
    for (var g = 0; g < LOAD_GROUPS.length; g++) {
      heading(out, LOAD_GROUPS[g][0]);
      var fields = LOAD_GROUPS[g][1];
      for (var f = 0; f < fields.length; f++) {
        var input = byId(page, fields[f][0]);
        if (!input || !shown(input)) continue;
        var label = fields[f][1] + (fields[f][2] ? ' (' + unit + ')' : '');
        if (input.disabled) stat(out, label + ': ' + (input.value || 'blank'), { key: 'readout:' + fields[f][1] });
        else field(out, label, input);
      }
    }
    var banner = byId(page, 'ls-status-banner');
    if (banner && shown(banner) && text(banner)) stat(out, text(banner), { live: 'polite', key: 'status:loading' });
    var pod = page.querySelector('.ferry-pod-warning');
    if (pod && shown(pod)) stat(out, text(pod));
    var safety = byId(page, 'ls-safety-msg');
    if (safety && shown(safety) && text(safety)) out.push(item('alert', text(safety), { live: 'assertive', key: 'alert:loadsafety' }));

    var popup = byId(page, 'load-settings-popup');
    if (!popup || !shown(popup)) return;
    heading(out, 'Load settings');
    var modes = popup.querySelectorAll('.ls-mode-btn'), members = [];
    for (var m = 0; m < modes.length; m++) members.push({ text: text(modes[m]), node: modes[m] });
    pick(out, 'Load and unload time', members, function (n) { return cls(n, 'active'); });
    var instant = byId(popup, 'ls-instant-section');
    if (instant && shown(instant)) stat(out, text(instant));
    for (var n = 0; n < LOAD_NOTES.length; n++) {
      var note = byId(popup, LOAD_NOTES[n]);
      if (note && shown(note) && text(note)) stat(out, text(note));
    }
    for (var t = 0; t < LOAD_TIMED.length; t++) {
      b = byId(popup, LOAD_TIMED[t][0]);
      if (b && shown(b)) button(out, LOAD_TIMED[t][1], b);
    }
    var remaining = byId(popup, 'ls-time-remaining');
    // A countdown: kept in place by its key, never spoken by itself (see SILENT_STATUS).
    if (remaining && shown(remaining)) stat(out, 'Time remaining: ' + text(remaining), { key: 'status:loadtime' });
    b = byId(popup, 'load-settings-close-btn');
    if (b && shown(b)) button(out, 'Back', b);
  }

  // ---- Takeoff and landing performance ------------------------------------------------------------

  function cardToggle(out, node, name, key) {
    if (node && shown(node)) button(out, name + ': ' + text(node).toLowerCase(), node, { key: key, ac: true });
  }

  function results(page, out, table) {
    heading(out, 'Results');
    for (var i = 0; i < table.length; i++) {
      var d = byId(page, table[i][0]);
      if (!d) continue;
      stat(out, table[i][1] + ': ' + readout(text(d), table[i][2]), { key: 'readout:' + table[i][1] });
    }
  }

  function takeoff(page, out) {
    var unit = weightUnit(page, 'kg_unit', 'lb_unit');
    heading(out, 'Conditions');
    var f = byId(page, 'to_weight_input');
    if (f && shown(f)) field(out, 'Takeoff weight (' + unit + ')', f);
    f = byId(page, 'press_alt_input');
    if (f && shown(f)) field(out, 'Pressure altitude (feet)', f);
    f = byId(page, 'ambient_temp_input');
    if (f && shown(f)) field(out, 'Ambient temperature (degrees C)', f);
    var members = [];
    for (var i = 0; i < FLAPS.length; i++) members.push({ text: FLAPS[i][1], node: byId(page, FLAPS[i][0]) });
    pick(out, 'Takeoff flaps', members, function (n) { return cls(n, 'door-open'); });
    var b = byId(page, 'sync');
    if (b && shown(b)) button(out, 'Sync from aircraft', b);
    b = byId(page, 'calculate');
    if (b && shown(b)) button(out, 'Calculate', b);
    b = byId(page, 'send-airspeed');
    if (b && shown(b)) button(out, 'Set bugs and EPR', b);
    cardToggle(out, byId(page, 'speed-card-housing-toggle'), 'Takeoff card', 'card:takeoff');
    results(page, out, TAKEOFF_RESULTS);
    var err = byId(page, 'calc_error_alert');
    if (err && shown(err) && text(err)) out.push(item('alert', text(err), { live: 'assertive', key: 'alert:takeoff' }));
  }

  function landing(page, out) {
    var unit = weightUnit(page, 'kg_unit_lnd', 'lb_unit_lnd');
    heading(out, 'Conditions');
    var f = byId(page, 'landing-weight-input');
    if (f && shown(f)) field(out, 'Landing weight (' + unit + ')', f);
    f = byId(page, 'landing-elevation-input');
    if (f && shown(f)) field(out, 'Landing field elevation (feet)', f);
    var selected = function (n) { return cls(n, 'active') || cls(n, 'door-open'); };
    pick(out, 'Runway condition', [{ text: 'Dry', node: byId(page, 'runway-dry-btn') }, { text: 'Wet', node: byId(page, 'runway-wet-btn') }], selected);
    pick(out, 'Anti-skid', [{ text: 'On', node: byId(page, 'antiskid-on-btn') }, { text: 'Off', node: byId(page, 'antiskid-off-btn') }], selected);
    var b = byId(page, 'sync-landing-btn');
    if (b && shown(b)) button(out, 'Sync from aircraft', b);
    b = byId(page, 'calculate-landing-btn');
    if (b && shown(b)) button(out, 'Calculate', b);
    b = byId(page, 'set-landing-btn');
    if (b && shown(b)) button(out, 'Set bugs', b);
    cardToggle(out, byId(page, 'speed-card-housing-toggle-landing-btn'), 'Landing card', 'card:landing');
    results(page, out, LANDING_RESULTS);
    var err = byId(page, 'error-message-display');
    if (err && shown(err) && text(err)) out.push(item('alert', text(err), { live: 'assertive', key: 'alert:landing' }));
  }

  // ---- OFP ---------------------------------------------------------------------------------------

  function ofp(page, out) {
    var b = page.querySelector('.reload-button');
    if (b && shown(b)) button(out, 'Reload OFP', b);
    b = page.querySelector('.send-to-ins-button');
    if (b && shown(b)) button(out, 'Send to INS', b, { disabled: b.getAttribute('data-active') === 'false' });
    var viewer = page.querySelector('.simbrief-viewer');
    var plan = viewer && viewer.getAttribute('data-active') === 'true' && shown(viewer) ? viewer.querySelector('.simbrief-viewer__plan') : null;
    var pres = plan ? plan.querySelectorAll('pre') : [];
    if (pres.length > 0) {
      for (var i = 0; i < pres.length; i++) out.push(item('static', pres[i].textContent, { type: 'pre', key: 'ofp:plan:' + i }));
    } else if (plan && text(plan)) {
      stat(out, text(plan), { key: 'ofp:plan' });
    } else {
      stat(out, 'No flight plan is loaded. Enter your SimBrief ID under Menu, Third party, then press Reload OFP.');
    }
    var close = viewer && plan ? viewer.querySelector('.close-btn') : null;
    if (close && shown(close)) button(out, 'Close OFP', close);
  }

  // ---- Charts and map ------------------------------------------------------------------------------

  function charts(page, out) {
    stat(out, 'Charts are pictures and cannot be read here.');
    var lookup = page.querySelector('input.wx-lookup');
    if (lookup && shown(lookup)) field(out, 'METAR airport (ICAO)', lookup);
    var search = page.querySelector('button.wx-lookup');
    if (search && shown(search)) button(out, 'Search METAR', search);
    var result = page.querySelector('.wx-flight-value');
    if (result && shown(result)) stat(out, 'METAR: ' + (text(result) === '----' ? 'none' : text(result)), { live: 'polite', key: 'status:metar' });
  }

  function map(page, out) {
    stat(out, 'The map is a picture and cannot be read here.');
  }

  // ---- Menu --------------------------------------------------------------------------------------

  function sidebarName(b) {
    for (var c in SIDEBAR) if (SIDEBAR.hasOwnProperty(c) && cls(b, c)) return SIDEBAR[c];
    return text(b);
  }

  function menu(page, out) {
    var pane = null, paneName = '';
    var side = page.querySelectorAll('.page-options__sidebar button.btn');
    for (var i = 0; i < side.length; i++) {
      if (!shown(side[i])) continue;
      var on = cls(side[i], 'active');
      if (on) paneName = sidebarName(side[i]);
      button(out, sidebarName(side[i]) + (on ? ' (selected)' : ''), side[i], { kind: 'tab' });
    }
    var content = page.querySelector('.page-options__content');
    if (content) {
      for (var c = content.firstElementChild; c; c = c.nextElementSibling) if (shown(c)) { pane = c; break; }
    }
    if (pane) {
      if (cls(pane, 'options-Autocomplete')) flows(pane, out);
      else walk(pane, out, { heading: '', fieldLabel: '' });
    }
    return paneName;
  }

  function flows(pane, out) {
    var title = pane.querySelector('.autocomplete-title');
    if (title && shown(title)) heading(out, text(title));
    var items = pane.querySelectorAll('.autocomplete-item');
    for (var i = 0; i < items.length; i++) {
      var name = text(items[i].querySelector('.autocomplete-item-title'));
      var list = items[i].querySelector('.event-list');
      var open = cls(list, 'expanded');
      heading(out, name, 3);
      var toggle = items[i].querySelector('.toggle-button');
      if (toggle) button(out, name + (open ? ' (expanded)' : ' (collapsed)'), toggle, { key: 'fe-toggle:' + name, ac: true });
      var run = items[i].querySelector('.autocomplete-button');
      if (run) button(out, 'Autocomplete ' + name, run, { key: 'fe-run:' + name });
      if (!open || !list) continue;   // a collapsed flow's steps are drawn in a three-pixel strip
      var steps = list.querySelectorAll('.event-item');
      for (var s = 0; s < steps.length; s++) {
        var box = steps[s].querySelector('.event-checkbox');
        var status = 'status unknown';
        for (var k in FE_STATUS) if (FE_STATUS.hasOwnProperty(k) && cls(box, k)) status = FE_STATUS[k];
        var stepName = text(steps[s].querySelector('.event-name')).replace(/\s*\.{2,}\s*/g, ' to ');
        var complete = steps[s].querySelector('.event-complete-button');
        if (complete) button(out, stepName + ': ' + status, complete, { key: 'fe-step:' + name + ':' + s, ac: true });
      }
    }
  }

  // The settings panes: walked in reading order. Radio groups become one dropdown named by the
  // heading above them, a switch a check box, a real range a slider; everything else is a heading,
  // a button, a text field or a line of text.
  function hiddenBranch(el) {
    if (cls(el, 'hidden') || cls(el, 'd-none') || cls(el, 'hide') || cls(el, 'page--hide')) return true;
    return !!(el.style && el.style.display === 'none');
  }

  function liveStatus(el) {
    for (var i = 0; i < LIVE_STATUS.length; i++) if (cls(el, LIVE_STATUS[i])) return LIVE_STATUS[i];
    return '';
  }

  function silentStatus(el) {
    for (var i = 0; i < SILENT_STATUS.length; i++) if (cls(el, SILENT_STATUS[i])) return SILENT_STATUS[i];
    return '';
  }

  function isLeafText(el) {
    for (var c = el.firstElementChild; c; c = c.nextElementSibling) if (c.tagName !== 'BR') return false;
    return true;
  }

  // A heading that only names the one control after it ("Weight Units" over its two buttons) is that
  // control's label, not a heading of its own: read as a heading it would say every name twice.
  function controlsIn(el) {
    if (el.getAttribute('role') === 'group' && el.querySelector('input.btn-check')) return 1;
    return el.querySelectorAll('[role=group] input.btn-check:first-child, input[type=range], .form-switch').length;
  }

  function labelsNextControl(h) {
    var n = h.nextElementSibling;
    while (n && hiddenBranch(n)) n = n.nextElementSibling;
    return !!n && controlsIn(n) === 1;
  }

  function radioGroup(group, out, ctx) {
    var labels = group.querySelectorAll('label'), members = [];
    for (var i = 0; i < labels.length; i++) members.push({ text: text(labels[i]), node: labels[i] });
    pick(out, ctx.heading || group.getAttribute('aria-label') || 'Option', members, function (n) { return cls(n, 'active'); });
  }

  function walk(el, out, ctx) {
    for (var c = el.firstElementChild; c; c = c.nextElementSibling) {
      if (hiddenBranch(c)) continue;
      var tag = c.tagName;
      if (/^H[1-6]$/.test(tag)) {
        if (shown(c) && text(c)) {
          ctx.heading = text(c);
          if (!labelsNextControl(c)) heading(out, text(c), Math.min(4, +tag.charAt(1)));
        }
        continue;
      }
      if (c.getAttribute('role') === 'group' && c.querySelector('input.btn-check')) { radioGroup(c, out, ctx); continue; }
      if (cls(c, 'form-switch')) {
        var box = c.querySelector('input[type=checkbox]'), lab = c.querySelector('label');
        if (box && shown(box)) {
          out.push(item('', text(lab) || ctx.heading || 'Switch', { type: 'checkbox', value: box.checked ? 'true' : 'false', act: { sw: box, label: lab } }));
        }
        continue;
      }
      if (cls(c, 'status-row')) {
        var name = text(c.querySelector('.status-label')), sb = c.querySelector('button');
        if (sb && shown(sb)) {
          if (sb.disabled || sb.className.indexOf('--readonly') >= 0) stat(out, name + ': ' + text(sb), { key: 'readout:' + name });
          else button(out, name + ': ' + text(sb).toLowerCase(), sb, { key: 'toggle:' + name, ac: true });
        }
        continue;
      }
      if (cls(c, 'helper-input-block')) {
        ctx.fieldLabel = text(c.querySelector('.helper-input-title'));
        walk(c, out, ctx);
        ctx.fieldLabel = '';
        continue;
      }
      if (cls(c, 'helper-input-title') || cls(c, 'audio-volume-value-editor')) continue;   // a label, a duplicate of the slider
      if (cls(c, 'navigraph-login__usercode')) {   // the sign-in code, then how to use it
        var code = text(c.querySelector('span'));
        if (shown(c) && code) stat(out, 'Navigraph code: ' + code, { key: 'status:navigraphcode' });
        var how = c.querySelector('p');
        if (how && shown(how)) stat(out, text(how));
        continue;
      }
      if (tag === 'INPUT') {
        if (!shown(c) || c.type === 'radio' || c.type === 'checkbox' || cls(c, 'audio-volume-value-input')) continue;
        if (c.type === 'range') {
          out.push(item('', ctx.heading || 'Slider', { type: 'range', value: c.value, act: { range: c },
            min: +c.min, max: +c.max, step: +c.step || 1 }));
          continue;
        }
        var fieldName = c.getAttribute('aria-label') || ctx.fieldLabel || c.getAttribute('placeholder') || ctx.heading || 'Text';
        if (c.disabled) stat(out, fieldName + ': ' + (c.value || 'blank'));
        else field(out, fieldName, c);
        continue;
      }
      if (tag === 'BUTTON' || c.getAttribute('role') === 'button') {
        if (!shown(c)) continue;
        var labelEl = c.querySelector('.panel-state-label');
        var label = labelEl ? text(labelEl) : (text(c) || c.getAttribute('aria-label') || c.getAttribute('title') || '');
        if (!label) continue;
        var key = c.getAttribute('data-status') ? 'ps:' + c.getAttribute('data-status') : c.getAttribute('data-helper') ? 'helper:' + c.getAttribute('data-helper') : '';
        button(out, label, c, { key: key, disabled: cls(c, 'helper-pause-btn-disabled') });
        continue;
      }
      var upper = tag.toUpperCase();   // an svg element's tagName is lowercase in an HTML document
      if (upper === 'IMG' || upper === 'SVG' || upper === 'CANVAS' || upper === 'LABEL') continue;
      var live = liveStatus(c), silent = silentStatus(c);
      if (live || silent) {
        if (shown(c) && text(c)) stat(out, text(c), live ? { live: 'polite', key: 'status:' + live } : { key: 'status:' + silent });
        continue;
      }
      if (cls(c, 'panelstates-error-display')) {
        if (shown(c) && text(c)) out.push(item('alert', text(c), { live: 'assertive', key: 'alert:panelstates' }));
        continue;
      }
      if (isLeafText(c)) {
        if (shown(c) && text(c)) stat(out, text(c));
        continue;
      }
      // A row of plain text cells ("FULL 100%") reads as one line.
      var cells = [], plain = true;
      for (var d = c.firstElementChild; d; d = d.nextElementSibling) {
        if (!isLeafText(d) || d.tagName === 'BUTTON' || d.tagName === 'INPUT' || d.tagName === 'IMG') { plain = false; break; }
        if (shown(d) && text(d)) cells.push(text(d));
      }
      if (plain && cells.length >= 2) { stat(out, cells.join(' ')); continue; }
      walk(c, out, ctx);
    }
  }

  // ---- scrape, press, type ---------------------------------------------------------------------

  var acts = [null];
  var lastFull = null;

  function collect() {
    var root = document.getElementById('iniEFB');
    var out = [];
    if (!root) return { page: '', items: out };
    pauseOverlay(root, out);
    simRate(root, out);
    var page = visiblePage(root);
    tabs(root, page, out);
    var name = page ? TABS[page.tab][1] : '';
    if (page) {
      var cl = TABS[page.tab][2];
      if (cl === 'page-test-basic') ground(page.node, out);
      else if (cl === 'page-load-sheet') loadSheet(page.node, out);
      else if (cl === 'page-takeoff') takeoff(page.node, out);
      else if (cl === 'page-landing') landing(page.node, out);
      else if (cl === 'page-ofp') ofp(page.node, out);
      else if (cl === 'page-charts') charts(page.node, out);
      else if (cl === 'page-map') map(page.node, out);
      else if (cl === 'page-options') { var pane = menu(page.node, out); if (pane) name += ', ' + pane; }
    }
    return { page: name, items: out };
  }

  function scrape() {
    try {
      var r = collect();
      var nextActs = [null], elements = [];
      for (var i = 0; i < r.items.length; i++) {
        var it = r.items[i];
        nextActs.push(it.act);
        var e = {
          idx: i + 1, text: it.text, value: it.value, controlType: it.controlType, kind: it.kind,
          clickable: it.clickable, level: it.level, live: it.live, disabled: it.disabled, options: it.options
        };
        if (it.key) e.key = it.key;
        if (it.announceChange) e.announceChange = true;
        if (it.controlType === 'range') { e.min = it.min; e.max = it.max; e.step = it.step; }
        elements.push(e);
      }
      acts = nextActs;   // fresh nodes every scrape, so a press always reaches the live control
      var full = JSON.stringify({ ok: true, page: r.page, elements: elements });
      if (full === lastFull) return JSON.stringify({ ok: true, unchanged: true });
      lastFull = full;
      return full;
    } catch (e) {
      return JSON.stringify({ ok: false, error: String(e) });
    }
  }

  function fire(el, type) {
    var ev;
    if (type === 'keyup') ev = new KeyboardEvent('keyup', { bubbles: true });
    else { ev = document.createEvent('Event'); ev.initEvent(type, true, true); }
    el.dispatchEvent(ev);
  }

  function setNative(el, value) {
    var proto = Object.getPrototypeOf(el);
    var desc = Object.getOwnPropertyDescriptor(proto, 'value') || Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value');
    if (desc && desc.set) desc.set.call(el, value); else el.value = value;
  }

  // Load sheet, takeoff and landing fields carry data-keyboard: the value counts only when the
  // tablet's own keyboard submits it. Other fields (the TOD helper, the SimBrief Id) take events.
  function typeInto(el, value) {
    if (!attached(el) || el.disabled || inert(el)) return false;
    var board = el.getAttribute('data-keyboard');
    if (board) el.click();
    el.focus();
    setNative(el, value);
    fire(el, 'input');
    if (board) {
      var pad = document.getElementById(board === 'numeric' ? 'osk-num' : 'osk');
      var submit = pad ? pad.querySelector(board === 'numeric' ? '.osk-hide-num' : '.osk-hide') : null;
      if (submit && window.getComputedStyle(pad).display !== 'none') {
        submit.click();
        if (document.activeElement === el) el.blur();
        return true;
      }
    }
    fire(el, 'keyup');
    fire(el, 'change');
    el.blur();
    return true;
  }

  function clickElement(idx) {
    var a = acts[idx];
    if (!a || !a.press || !attached(a.press) || inert(a.press)) return false;
    a.press.click();
    return true;
  }

  function setValue(idx, value) {
    var a = acts[idx];
    var v = value === undefined || value === null ? '' : String(value);
    if (!a) return false;
    if (a.field) return typeInto(a.field, v);
    if (a.pick) {
      for (var i = 0; i < a.pick.length; i++) {
        var m = a.pick[i];
        if (m.text !== v) continue;
        if (!attached(m.node) || inert(m.node)) return false;
        m.node.click();
        return true;
      }
      return false;
    }
    if (a.sw) {
      var want = v === 'true';
      if (a.sw.checked === want) return true;
      (a.label && attached(a.label) ? a.label : a.sw).click();
      return true;
    }
    if (a.range) {
      setNative(a.range, v);
      fire(a.range, 'input');
      fire(a.range, 'change');
      return true;
    }
    return false;
  }

  window.__MSFSBA_L1011_EFB = {
    scrape: scrape,
    clickElement: clickElement,
    setValue: setValue,
    findRoot: function () { return document.getElementById('iniEFB'); },
    isVisible: shown
  };
  return MARK;
})();
