// iniBuilds A300 tablet in-page agent — installed at runtime into the tablet's Coherent GT view
// ("VCockpit18 - iniEfbA300") as window.__MSFSBA_A300_EFB. The tablet is a Bootstrap-styled page
// with a real DOM: buttons, inputs, Bootstrap radio groups (input.btn-check + label.btn), range
// inputs, headings and text. States a sighted pilot reads from colour are put into words here:
// a door's open/moving/closed (its classes: door-open, door-transit) and a slide's armed/disarmed
// (its background: red armed, green disarmed, per the page's own legend).
//
// ES5 ONLY. Coherent GT is Chromium 49: var (no const/let), no arrow functions, no template
// literals, no String.includes / Array.from / Object.assign. Element.closest exists there.
//
// Contract, the same as the PMDG and MD-11 agents so the shared FbwEfbForm consumes it:
//   scrape()            -> JSON string {ok, page, elements:[...]}
//   clickElement(idx)   -> press the element carrying that stamped idx
//   setValue(idx, text) -> type into a field, pick a radio option, or move a slider
// Element: {idx,text,value,controlType,kind,clickable,level,disabled,options,min,max,step,key,announceChange}
//
// A press is ONE click: pointerdown, mousedown, pointerup, mouseup, click. The tablet ignores a bare
// el.click() (measured: its unlock button did nothing), and two click events would run a toggle twice.
(function () {
  var A = {};
  A.INSTALLED = 'MSFSBA_A300_EFB_INSTALLED';
  A.ATTR = 'data-a300-efb-idx';

  // The page's root element id → its name.
  A.PAGES = {
    lockscreen: 'Locked', dashboard: 'Home', flight: 'My Flight', equip: 'Ground Equipment',
    weights: 'Weight and Balance', takeOffPerfViewer: 'Take Off', landingPerfViewer: 'Landing',
    maintenance: 'Aircraft Maintenance', throttlecalibration: 'Throttle Calibration', settings: 'Settings'
  };

  // Icon-only controls, and buttons whose own words read badly, by id.
  A.NAMES = {
    'menu-home-button': 'Home', 'control-box-button': 'Control box', 'toggle-maintenance': 'Maintenance in progress',
    refreshMetar: 'Refresh METAR', pb_left: 'Pushback, turn left', pb_right: 'Pushback, turn right',
    pb_stop: 'Pushback, stop', pb_aft: 'Pushback, straight back',
    switcher_page1: 'Back to payload selection', switcher_page2: 'Loading page',
    payload_cargo: 'Custom cargo', payload_cargo_sb: 'Cargo from SimBrief'
  };

  // WEIGHT AND BALANCE. Its two halves slide by 100vw: payload selection (#page1: preset loads)
  // and loading (#page2: the hold weights, the fuel, Update and Apply, and the results). Read from
  // the tablet's own code (2026-10-04): a preset fills the three hold weights with a third of its
  // load each and slides to the loading half; Update from SimBrief fills the holds (a third of the
  // plan's payload each) and the fuel (the plan's ramp fuel); only Apply Load to Aircraft puts
  // them on board. The fuel field is the TOTAL fuel to have on board. Unload Cargo only hides the
  // cargo models (the INI_LOAD_*_SHOW visuals); the weight stays on board.
  A.WEIGHTS_HALVES = { page1: 'payload selection', page2: 'loading' };
  A.WEIGHTS_HELP = {
    page1: 'Choosing a load fills in the hold weights and opens the loading page. Nothing goes on board until you press Apply Load to Aircraft there. ' +
      'Unload Cargo only removes the cargo you can see in the hold; to take its weight off, set the holds to 0 on the loading page and apply.',
    page2: 'Update from SimBrief fills in the cargo and fuel from your latest SimBrief plan. Apply Load to Aircraft puts the cargo and fuel on board.'
  };
  A.FIELD_NAMES = {
    weight_fuel: 'Total fuel', weight_freight_fwd: 'Forward hold', weight_freight_mid: 'Middle hold',
    weight_freight_aft: 'Aft hold', input_pax: 'Passengers'
  };
  // The cargo field's label column says LOAD (CARGO on the passenger version).
  A.FIELD_TITLES = { LOAD: 'Total cargo', CARGO: 'Total cargo' };
  // The results: read-only boxes, printed in thousands of the tablet's weight unit or in % MAC.
  A.READOUTS = {
    output_maczfw: ['Zero fuel weight centre of gravity', 'mac'], output_maccg: ['Gross weight centre of gravity', 'mac'],
    output_zfw: ['Zero fuel weight', 'weight'], output_tow: ['Gross weight', 'weight'],
    output_payload: ['Payload', 'weight'], output_blkfuel: ['Block fuel', 'weight']
  };
  // A preset's two lines: "31'000Kgs / 68'300Lbs", then its name.
  A.PRESET_WEIGHT = /^([\d',.]+)\s*kgs?\s*\/\s*([\d',.]+)\s*lbs?$/i;

  // My Flight's values, which sit under headings ("METAR", "DEP TIME") or under nothing at all.
  A.VALUE_NAMES = {
    dep_icao: 'Departure', depcit: 'Departure city', arr_icao: 'Arrival', arrcit: 'Arrival city',
    depmet: 'Departure METAR', arrmet: 'Arrival METAR', deptime: 'Departure time', arrtime: 'Arrival time',
    route: 'Route'
  };

  A.visible = function (el) {
    if (!el || el.nodeType !== 1) return false;
    try {
      var cs = window.getComputedStyle(el);
      if (cs.display === 'none' || cs.visibility === 'hidden') return false;
      var r = el.getBoundingClientRect();
      // Slid off the side: Weight and Balance moves its other half 100vw away, still laid out.
      var vw = window.innerWidth || 0;
      if (vw > 0 && r.width > 0 && (r.right <= 0 || r.left >= vw)) return false;
      if (r.width > 0 && r.height > 0) return true;
      // A zero-size container can still hold visible children: the page container is 2048 x 0,
      // its pages being positioned absolutely inside it.
      return el.children.length > 0 && el.tagName !== 'BUTTON' && el.tagName !== 'INPUT';
    } catch (e) { return false; }
  };

  A.clean = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); };
  // "31'000" → 31000; 31000 → "31,000" (the tablet's own separator is an apostrophe).
  A.num = function (s) { return parseFloat(String(s).replace(/[',]/g, '')); };
  A.thousands = function (n) { return String(Math.round(n)).replace(/\B(?=(\d{3})+(?!\d))/g, ','); };
  // "000.0" → "0.0", "060.11" → "60.11".
  A.plainNumber = function (s) { return A.clean(s).replace(/^(-?)0+(?=\d)/, '$1'); };
  A.sentence = function (s) { s = A.clean(s).toLowerCase(); return s ? s.charAt(0).toUpperCase() + s.slice(1) : ''; };
  A.txt = function (el) { return el ? A.clean(el.innerText || el.textContent || '') : ''; };
  A.ownText = function (el) {
    var s = '';
    for (var i = 0; i < el.childNodes.length; i++) if (el.childNodes[i].nodeType === 3) s += el.childNodes[i].textContent;
    return A.clean(s);
  };
  A.hasClass = function (el, c) {
    return !!el && typeof el.className === 'string' && (' ' + el.className + ' ').indexOf(' ' + c + ' ') >= 0;
  };

  // "toggleStairsL" / "doors_l1" → "Toggle stairs l" — the last resort for a nameless control.
  A.idWords = function (id) {
    var s = String(id || '').replace(/[_-]+/g, ' ').replace(/([a-z])([A-Z])/g, '$1 $2');
    s = A.clean(s).toLowerCase();
    return s ? s.charAt(0).toUpperCase() + s.slice(1) : '';
  };

  A.isPress = function (el) {
    var t = el.tagName;
    if (t === 'BUTTON') return true;
    if (t === 'INPUT' && (el.type === 'button' || el.type === 'submit')) return true;
    if (t === 'IMG' && el.id === 'menu-home-button') return true;
    if (t === 'DIV' && (el.id === 'control-box-button' || el.id === 'switcher_page1' || el.id === 'switcher_page2' ||
        A.hasClass(el, 'is-button') || A.hasClass(el, 'unlock-button'))) return true;
    return false;
  };

  // The label column of an input's row: "RWY", "LOAD", "FUEL", "Master" (volumes).
  A.rowLabel = function (input) {
    var row = input.closest('.row');
    for (var depth = 0; row && depth < 2; depth++) {
      var c = row.querySelector('.flight-value-title, .ini-input-label, p.text-end');
      if (c && !c.contains(input) && A.visible(c)) return A.clean(A.ownText(c) || A.txt(c));
      row = row.parentElement ? row.parentElement.closest('.row') : null;
    }
    return '';
  };

  // The nearest heading before an element within its card/column: Settings' "SimBrief", "MCDU Export".
  A.headingBefore = function (el) {
    var n = el;
    for (var up = 0; n && up < 6; up++) {
      var p = n.previousElementSibling;
      while (p) {
        if (/^H[1-6]$/.test(p.tagName) && A.visible(p)) {
          var own = A.clean(A.ownText(p) || A.txt(p));
          if (own) return own;
          // An empty heading (Settings' Top Of Descent Pause choice): the card's own title.
          var body = el.closest('.card-body');
          var title = body ? body.querySelector('h1') : null;
          return title ? A.clean(A.ownText(title) || A.txt(title)) : '';
        }
        var h = p.querySelector ? p.querySelectorAll('h1, h2, h3, h4, h5') : [];
        if (h.length && A.visible(h[h.length - 1])) return A.clean(A.ownText(h[h.length - 1]) || A.txt(h[h.length - 1]));
        p = p.previousElementSibling;
      }
      n = n.parentElement;
    }
    return '';
  };

  A.unit = function (input) {
    var g = input.closest('.input-group');
    var u = g ? g.querySelector('.input-group-text') : null;
    return u ? A.txt(u) : '';
  };

  A.fieldLabel = function (input) {
    if (A.FIELD_NAMES[input.id]) return A.FIELD_NAMES[input.id];
    var l = A.rowLabel(input) || A.headingBefore(input) || A.NAMES[input.id] || A.idWords(input.id);
    return A.FIELD_TITLES[l] || l;
  };

  // The tablet's weight unit, from the unit beside its fuel box ("kg" or "lbs").
  A.inPounds = function () {
    var f = document.getElementById('weight_fuel');
    return !!f && /^lb/i.test(A.unit(f));
  };

  // A Weight and Balance result in words: "Gross weight: 102.40 tonnes", "... 24.95 percent MAC".
  A.readout = function (input) {
    var r = A.READOUTS[input.id];
    var unit = r[1] === 'mac' ? 'percent MAC' : (A.inPounds() ? 'thousand pounds' : 'tonnes');
    return r[0] + ': ' + A.plainNumber(input.value) + ' ' + unit;
  };

  // What is on board now, from the simulator: the payload (stations 3 to 8; 1 and 2 are the
  // pilots) and the fuel to the nearest hundred, so an APU's burn does not change the line every
  // poll. While the tablet fuels the aircraft (L:INI_IS_REFUELING) the fuel says so instead of a
  // number that moves every second; the line then changes once, when fuelling ends. '' when the
  // simulator cannot be read.
  A.onBoardLine = function () {
    try {
      if (typeof SimVar === 'undefined' || !SimVar.GetSimVarValue) return '';
      var unit = A.inPounds() ? 'pounds' : 'kilograms';
      var payload = 0;
      for (var st = 3; st <= 8; st++) payload += +SimVar.GetSimVarValue('PAYLOAD STATION WEIGHT:' + st, unit) || 0;
      var fuelling = +SimVar.GetSimVarValue('L:INI_IS_REFUELING', 'number') >= 0.5;
      var fuel = +SimVar.GetSimVarValue('FUEL TOTAL QUANTITY WEIGHT', unit) || 0;
      return 'On board: payload ' + A.thousands(payload) + ' ' + unit + ', ' +
        (fuelling ? 'fuelling in progress' : 'fuel ' + A.thousands(Math.round(fuel / 100) * 100) + ' ' + unit);
    } catch (e) { return ''; }
  };

  // A preset load button: "Racing Team Charter: 31,000 kilograms, 68,300 pounds".
  A.presetLabel = function (btn) {
    var spans = btn.querySelectorAll ? btn.querySelectorAll('h1 span') : [];
    if (spans.length < 2) return '';
    var m = A.PRESET_WEIGHT.exec(A.txt(spans[0]));
    if (!m) return '';
    return A.txt(spans[1]) + ': ' + A.thousands(A.num(m[1])) + ' kilograms, ' + A.thousands(A.num(m[2])) + ' pounds';
  };

  // A door button's state from its classes; a slide button's from its colour.
  A.doorState = function (btn) {
    if (A.hasClass(btn, 'door-transit')) return 'moving';
    if (A.hasClass(btn, 'door-open')) return 'open';
    return 'closed';
  };
  A.slideState = function (btn) {
    var m = /rgba?\((\d+),\s*(\d+),\s*(\d+)/.exec(window.getComputedStyle(btn).backgroundColor || '');
    if (!m) return '';
    var r = +m[1], g = +m[2];
    if (r > g) return 'armed';
    if (g > r) return 'disarmed';
    return '';
  };
  A.slideName = function (id) {
    var m = /^door(\d)([LR])armed$/.exec(id || '');
    return m ? 'Door ' + m[1] + m[2] + ' slide' : A.idWords(id);
  };

  A.pressLabel = function (el) {
    if (A.hasClass(el, 'door')) return A.txt(el) + ': ' + A.doorState(el);
    if (A.hasClass(el, 'door-arm')) {
      var s = A.slideState(el);
      return A.slideName(el.id) + (s ? ': ' + s : '');
    }
    if (el.tagName === 'INPUT') {
      var lab = A.rowLabel(el) || A.idWords(el.id);
      return lab ? lab + ': ' + A.clean(el.value) : A.clean(el.value);
    }
    if (A.NAMES[el.id]) return A.NAMES[el.id];
    if (el.tagName === 'DIV' && A.hasClass(el, 'unlock-button')) return 'Unlock';
    var preset = A.presetLabel(el);
    if (preset) return preset;
    var t = A.txt(el);
    if (t) return t;
    var img = el.querySelector ? el.querySelector('img[title], img[alt]') : null;
    if (img) return img.getAttribute('title') || img.getAttribute('alt');
    return A.idWords(el.id) || 'Button';
  };

  A.pageName = function () {
    var vp = document.querySelector('#renderer .visiblePage');
    if (!vp) return '';
    for (var i = 0; i < vp.children.length; i++) {
      var c = vp.children[i];
      if (c.id && A.PAGES[c.id] && A.visible(c)) return A.PAGES[c.id] + (c.id === 'weights' ? A.weightsHalf() : '');
    }
    for (var j = 0; j < vp.children.length; j++) {
      var d = vp.children[j];
      if (d.id && !/-bg$/.test(d.id) && A.visible(d)) return A.idWords(d.id);
    }
    return '';
  };

  // ", payload selection" or ", loading": the Weight and Balance half on screen.
  A.weightsHalf = function () {
    for (var id in A.WEIGHTS_HALVES) {
      var half = document.getElementById(id);
      if (half && half.closest('#weights') && A.visible(half)) return ', ' + A.WEIGHTS_HALVES[id];
    }
    return '';
  };

  A._idx = 0;
  A.stamp = function (node) { A._idx++; node.setAttribute(A.ATTR, String(A._idx)); return A._idx; };

  A.collect = function () {
    var els = [];
    var old = document.querySelectorAll('[' + A.ATTR + ']');
    for (var o = 0; o < old.length; o++) old[o].removeAttribute(A.ATTR);
    A._idx = 0;
    var doneGroups = {};
    var consumed = [];

    function emit(e) { els.push(e); }

    function walk(el) {
      if (!A.visible(el)) return;
      var tag = el.tagName;

      // A Weight and Balance half opens with what its buttons do and what is on board.
      if (A.WEIGHTS_HELP[el.id] && el.closest('#weights')) {
        emit({ idx: 0, text: A.WEIGHTS_HELP[el.id], value: '', kind: 'static', clickable: false, key: 'help:weights-' + el.id });
        var line = A.onBoardLine();
        if (line) emit({ idx: 0, text: line, value: '', kind: 'static', clickable: false, live: 'polite', key: 'status:onboard' });
      }
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'svg' || tag === 'CANVAS') return;
      // Pictures with letters on them: Take Off's speed diagram (its V1/VR/V2 marks repeat the fields).
      if (el.id === 'diagram' || el.id === 'weight-image') return;

      // Radio groups: one choice control per group, labelled by the heading above it.
      if (tag === 'INPUT' && el.type === 'radio') {
        if (doneGroups[el.name]) return;
        doneGroups[el.name] = true;
        var radios = document.getElementsByName(el.name);
        var opts = [], value = '';
        for (var r = 0; r < radios.length; r++) {
          var lab = document.querySelector('label[for="' + radios[r].id + '"]');
          var word = lab ? A.txt(lab) : A.idWords(radios[r].id);
          opts.push(word);
          if (radios[r].checked) value = word;
        }
        var holder = el.closest('.btn-group') || el.parentElement;
        var idxR = A.stamp(holder);
        holder.setAttribute('data-a300-radio', el.name);
        emit({ idx: idxR, text: A.headingBefore(holder) || A.idWords(el.name.replace(/^opt_/, '')), value: value,
          controlType: 'select', kind: 'static', clickable: false, options: opts, key: 'radio:' + el.name });
        return;
      }
      if (tag === 'LABEL' && el.htmlFor && document.getElementById(el.htmlFor) && document.getElementById(el.htmlFor).type === 'radio') return;

      if (tag === 'INPUT' && el.type === 'range') {
        var idxG = A.stamp(el);
        var rl = A.rowLabel(el) || A.headingBefore(el) || A.idWords(el.id);
        if (el.id === 'lights_cargo') {
          var t = document.getElementById('lighting_title');
          rl = (t ? A.txt(t) + ' ' : '') + 'lighting';
        }
        emit({ idx: idxG, text: rl, value: String(el.value), controlType: 'range', kind: 'static', clickable: false,
          min: +el.min || 0, max: +(el.max || 100), step: +(el.step || 1), key: 'range:' + (el.id || idxG) });
        return;
      }

      if (A.isPress(el)) {
        var idxP = A.stamp(el);
        var label = A.pressLabel(el);
        var item = { idx: idxP, text: label, value: '', kind: 'button', clickable: true, disabled: !!el.disabled };
        if (A.hasClass(el, 'door') || A.hasClass(el, 'door-arm') || el.tagName === 'INPUT') {
          item.key = 'press:' + el.id;
          item.announceChange = true;   // the label carries the control's own new state
        }
        emit(item);
        return;   // a button's inner text and icons are its label
      }

      if (tag === 'INPUT' && A.READOUTS[el.id]) {
        emit({ idx: 0, text: A.readout(el), value: '', kind: 'static', clickable: false, key: 'readout:' + el.id });
        return;
      }

      if (tag === 'INPUT' && (el.type === 'text' || el.type === 'number' || el.type === 'password')) {
        var idxI = A.stamp(el);
        var unit = A.unit(el);
        var name = A.fieldLabel(el) + (unit ? ' (' + unit + ')' : '');
        var secret = el.type === 'password';
        emit({ idx: idxI, text: name, value: secret ? (el.value ? 'set' : '') : A.clean(el.value), controlType: 'text',
          kind: 'static', clickable: false, disabled: !!el.disabled || !!el.readOnly, key: 'field:' + (el.id || idxI) });
        return;
      }

      // My Flight's headings only title the values named below.
      if (/^H[1-6]$/.test(tag) && el.closest('#flight')) return;
      if (el.id && A.VALUE_NAMES[el.id]) {
        emit({ idx: 0, text: A.VALUE_NAMES[el.id] + ': ' + A.txt(el), value: '', kind: 'static', clickable: false, key: 'text:' + el.id });
        return;
      }

      if (/^H[1-6]$/.test(tag)) {
        var ht = A.txt(el);
        // A heading holding a button (Take Off's "Conditions" + SYNC) reads its own words only.
        var btns = el.querySelectorAll('button, input');
        if (btns.length) ht = A.ownText(el);
        // Weight and Balance's capitals and bracketed unit: "WEIGHT AND BALANCE [kg]" → "Weight and balance".
        if (el.closest('#weights')) ht = A.sentence(ht.replace(/\s*\[[^\]]*\]\s*$/, ''));
        if (ht) emit({ idx: 0, text: ht, value: '', kind: 'heading', level: Math.min(6, +tag.charAt(1)), clickable: false });
        for (var b = 0; b < btns.length; b++) walk(btns[b]);
        return;
      }

      // Label columns are read with their field, never on their own.
      if (A.hasClass(el, 'flight-value-title') || A.hasClass(el, 'ini-input-label') || A.hasClass(el, 'input-group-text')) return;
      if (tag === 'P' && A.hasClass(el, 'text-end') && el.closest('.row') && el.closest('.row').querySelector('input[type=range]')) return;

      var own = A.ownText(el);
      if (own && tag !== 'LABEL') {
        emit({ idx: 0, text: own, value: '', kind: 'static', clickable: false, key: el.id ? 'text:' + el.id : undefined });
      }
      for (var k = 0; k < el.children.length; k++) walk(el.children[k]);
    }

    // The tablet's messages ("Payload Removed", a SimBrief error, "Unable to fuel aircraft when
    // engines are running.") pop up outside the page, in notie alert boxes on the body: read
    // first, as an alert the window speaks when it appears. The tablet only ever uses notie.alert.
    var toasts = document.querySelectorAll('.notie-container');
    for (var n = 0; n < toasts.length; n++) {
      if (!A.visible(toasts[n])) continue;
      var said = A.txt(toasts[n].querySelector('.notie-textbox-inner') || toasts[n]);
      if (said) emit({ idx: 0, text: said, value: '', kind: 'alert', clickable: false });
    }

    var header = document.getElementById('header-bar');
    var page = document.querySelector('#renderer .visiblePage');
    if (page) walk(page);
    if (header) {
      var home = document.getElementById('menu-home-button');
      if (home && A.visible(home)) emit({ idx: A.stamp(home), text: 'Home', value: '', kind: 'button', clickable: true });
    }
    return els;
  };

  // A heading that only repeats the name of the field right after it (Settings' "SimBrief" above the
  // SimBrief box) is dropped: the field says it.
  // "SimBrief" names the field "SimBrief", and "CARGO LIGHTING" the slider "CARGO lighting" — but
  // "PUSHBACK" is a section over "Pushback, turn left", not its name.
  A.sameLabel = function (heading, label) {
    var h = heading.toLowerCase(), l = label.toLowerCase();
    return l === h || l.indexOf(h + ' (') === 0 || l.indexOf(h + ':') === 0;
  };

  A.dropEchoHeadings = function (els) {
    var out = [];
    for (var i = 0; i < els.length; i++) {
      var e = els[i], next = els[i + 1];
      if (e.kind === 'heading' && next && next.kind !== 'heading' && next.idx && A.sameLabel(e.text, next.text)) continue;
      out.push(e);
    }
    return out;
  };

  A.scrape = function () {
    try {
      return JSON.stringify({ ok: true, page: A.pageName(), elements: A.dropEchoHeadings(A.collect()) });
    } catch (e) {
      return JSON.stringify({ ok: false, error: String(e && e.message || e), elements: [] });
    }
  };

  A.find = function (idx) { return document.querySelector('[' + A.ATTR + '="' + idx + '"]'); };

  A.press = function (el) {
    var r = el.getBoundingClientRect();
    var x = r.left + r.width / 2, y = r.top + r.height / 2;
    var types = ['pointerdown', 'mousedown', 'pointerup', 'mouseup', 'click'];
    for (var i = 0; i < types.length; i++) {
      var t = types[i], ev;
      if (t.indexOf('pointer') === 0 && typeof PointerEvent !== 'undefined')
        ev = new PointerEvent(t, { bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0 });
      else
        ev = new MouseEvent(t, { bubbles: true, cancelable: true, clientX: x, clientY: y, button: 0, view: window });
      el.dispatchEvent(ev);
    }
  };

  A.clickElement = function (idx) {
    var el = A.find(idx);
    if (!el || !A.visible(el) || el.disabled) return false;
    A.press(el);
    return true;
  };

  A.fire = function (el, type) {
    var ev = document.createEvent('HTMLEvents');
    ev.initEvent(type, true, true);
    el.dispatchEvent(ev);
  };

  A.setValue = function (idx, text) {
    var el = A.find(idx);
    if (!el) return false;
    var group = el.getAttribute('data-a300-radio');
    if (group) {
      var radios = document.getElementsByName(group);
      for (var r = 0; r < radios.length; r++) {
        var lab = document.querySelector('label[for="' + radios[r].id + '"]');
        if (lab && A.txt(lab) === text) { A.press(lab); return true; }
      }
      return false;
    }
    if (el.tagName !== 'INPUT') return false;
    if (el.type === 'range') {
      el.value = String(text);
      A.fire(el, 'input');
      A.fire(el, 'change');
      return true;
    }
    // Never focus(): the tablet's on-screen keyboard opens on a focused field and covers the page.
    el.value = String(text);
    A.fire(el, 'input');
    A.fire(el, 'change');
    A.fire(el, 'keyup');
    A.fire(el, 'blur');
    return true;
  };

  window.__MSFSBA_A300_EFB = A;
  return A.INSTALLED;
})();
