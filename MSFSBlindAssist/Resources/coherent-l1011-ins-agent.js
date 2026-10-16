/*
 * MSFS Blind Assist - iniBuilds L-1011 TriStar INS and PMS reader.
 *
 * Runs inside an "L1011_INS_n" Coherent GT view through the remote debugger (CoherentDisplayClient).
 * READ ONLY: it changes nothing in the page. One view holds one INS unit and, in views 1 and 2, the
 * captain's and the first officer's PMS. scrape() returns "key|value" rows; L1011InsScrape (C#) reads
 * them back and L1011InsDisplay / L1011PmsDisplay turn them into lines.
 *
 * What is read, and why (package 1.0.8, docs/l1011.md):
 *  - The display windows are one-character hidden spans (#left_0..5, #right_0..6, #tk_0..1). Their text
 *    is written at once; the pictures beside them lag up to 200 ms, so the spans are the truth.
 *  - The decimal points are #dot_5 (left window) and #dot_6 (right window), shown by style.display.
 *  - There are no lamp elements: every lamp, the display selector and the waypoint wheel are L:vars.
 *    The overhead mode selector (L:ROTARY_INS_MODE_n) is not read here: the window takes it from its
 *    panel row, as it does the other selectors.
 *  - Entry state, the PMS page state and the nine waypoint slots are the gauge's own page globals
 *    (isInput, pms_is_input, gpsPoints...), read by name and guarded with typeof.
 * Coherent GT is Chromium-49 class, so this is ES5.
 */
(function () {
  if (window.__MSFSBA_DISP && window.__MSFSBA_DISP.kind === 'l1011-ins') {
    return 'MSFSBA_DISP_INSTALLED';
  }

  function text(id) {
    var el = document.getElementById(id);
    return el ? (el.textContent || '') : '';
  }

  // A blank slot is an empty span or a space; it keeps its place so the windows line up.
  function slots(prefix, count) {
    var s = '';
    for (var i = 0; i < count; i++) {
      var t = text(prefix + '_' + i);
      s += t === '' ? ' ' : t.charAt(0);
    }
    return s;
  }

  function lvar(name) {
    try {
      if (typeof SimVar === 'undefined') return null;
      var v = SimVar.GetSimVarValue('L:' + name, 'number');
      return typeof v === 'number' && isFinite(v) ? v : null;
    } catch (e) {
      return null;
    }
  }

  function num(v) {
    return typeof v === 'number' && isFinite(v) ? String(v) : '';
  }

  function on(name) {
    var v = lvar(name);
    return v !== null && v >= 0.5;
  }

  function unitNumber() {
    if (typeof insIndex !== 'undefined' && +insIndex >= 1 && +insIndex <= 3) return +insIndex;
    var root = document.querySelector('l1011-ins');
    var m = root ? /Index=(\d)/.exec(root.getAttribute('url') || '') : null;
    return m ? +m[1] : 0;
  }

  function entryState() {
    if (typeof isTrackChangeInput !== 'undefined' && isTrackChangeInput) return 'leg';
    if (typeof isInput !== 'undefined' && isInput) {
      if (typeof isInputRight !== 'undefined' && isInputRight) return 'right';
      return 'left';
    }
    return '';
  }

  function insLights(n) {
    var lit = [];
    if (on('INS_' + n + '_READY')) lit.push('READY');
    if (on('INS_' + n + '_ALERT_INDEX')) lit.push('ALERT');
    if (on('INS_' + n + '_WARN')) lit.push('WARN');
    if (on('INS_' + n + '_ON_BAT')) lit.push('BAT');
    if (on('INS_' + n + '_HOLD_LIGHT')) lit.push('HOLD');
    if (on('INS_' + n + '_INSERT_LIGHT')) lit.push('INSERT');
    if (on('INS_' + n + '_TK_CHG_LIGHT')) lit.push('TKCHG');
    if (on('INS_' + n + '_TEST')) lit.push('TEST');
    return lit;
  }

  function pmsLights(n, seat) {
    var lit = [];
    var names = ['CLIMB', 'CLIMB_1', 'CLIMB_2', 'CRUISE', 'DESCENT'];
    for (var i = 0; i < names.length; i++) {
      if (on('INI_' + seat + '_PMS_' + names[i])) lit.push(names[i]);
    }
    if (on('PMS_' + n + '_INSERT_LIGHT')) lit.push('INSERT');
    return lit;
  }

  function waypointRow(k) {
    var p = (typeof gpsPoints !== 'undefined' && gpsPoints) ? gpsPoints[k] : null;
    if (!p) return 'wpt_' + k + '||';
    var ident = String(p.ident || '').replace(/\|/g, ' ');
    return 'wpt_' + k + '|' + ident + '|' + num(+p.lat) + '|' + num(+p.lon);
  }

  window.__MSFSBA_DISP = {
    kind: 'l1011-ins',
    scrape: function () {
      try {
        var n = unitNumber();
        var power = document.getElementById('customElectricity');
        var dots = '';
        for (var d = 1; d <= 6; d++) {
          var dot = document.getElementById('dot_' + d);
          if (dot && dot.style.display !== 'none') dots += d;
        }
        var rows = [
          'unit|' + n,
          'power|' + (power && power.getAttribute('state') === 'off' ? 'off' : 'on'),
          'mode|' + num(lvar('INS_' + n + '_ROTARY_MODE')),
          'wheel|' + num(lvar('INS_' + n + '_ROTARY_WPT')),
          'left|' + slots('left', 6),
          'right|' + slots('right', 7),
          'tk|' + slots('tk', 2),
          'dots|' + dots,
          'status|' + num(lvar('INS_' + n + '_ALIGNMENT_STATUS')),
          'entry|' + entryState(),
          'lights|' + insLights(n).join(','),
          'auto_batch|' + num(lvar('INS_' + n + '_AUTO_BATCH_ENABLED')),
          'batch|' + (typeof active_batch !== 'undefined' ? num(+active_batch) : '0') + '|' +
            (typeof batch_count !== 'undefined' ? num(+batch_count) : '0')
        ];
        for (var k = 1; k <= 9; k++) rows.push(waypointRow(k));

        // Views 1 and 2 carry a live PMS (captain, first officer); view 3's copy is inert.
        var seat = n === 1 ? 'CPT' : (n === 2 ? 'FO' : '');
        rows.push('pms_seat|' + seat);
        rows.push('pms_title|' + text('pms_title'));
        for (var i = 1; i <= 6; i++) {
          rows.push('pms_left_' + i + '|' + text('pms_left_' + i));
          rows.push('pms_right_' + i + '|' + text('pms_right_' + i));
        }
        rows.push('pms_entry|' + (typeof pms_is_input !== 'undefined' && pms_is_input ? '1' : '0'));
        rows.push('pms_line|' + (typeof pms_line_active !== 'undefined' ? num(+pms_line_active) : '0'));
        rows.push('pms_lights|' + (seat ? pmsLights(n, seat).join(',') : ''));
        return JSON.stringify({ ok: true, rows: rows });
      } catch (e) {
        return JSON.stringify({ ok: false, error: String(e) });
      }
    }
  };
  return 'MSFSBA_DISP_INSTALLED';
})();
