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

  // Icon-only controls, by id.
  A.NAMES = {
    'menu-home-button': 'Home', 'control-box-button': 'Control box', 'toggle-maintenance': 'Maintenance in progress',
    refreshMetar: 'Refresh METAR', pb_left: 'Pushback, turn left', pb_right: 'Pushback, turn right',
    pb_stop: 'Pushback, stop', pb_aft: 'Pushback, straight back', switcher_page1: 'Back to payload selection'
  };

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
      if (r.width > 0 && r.height > 0) return true;
      // A zero-size container can still hold visible children: the page container is 2048 x 0,
      // its pages being positioned absolutely inside it.
      return el.children.length > 0 && el.tagName !== 'BUTTON' && el.tagName !== 'INPUT';
    } catch (e) { return false; }
  };

  A.clean = function (s) { return String(s == null ? '' : s).replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); };
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
    if (t === 'DIV' && (el.id === 'control-box-button' || A.hasClass(el, 'is-button') || A.hasClass(el, 'unlock-button'))) return true;
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
    var l = A.rowLabel(input) || A.headingBefore(input) || A.NAMES[input.id] || A.idWords(input.id);
    return l;
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
      if (c.id && A.PAGES[c.id] && A.visible(c)) return A.PAGES[c.id];
    }
    for (var j = 0; j < vp.children.length; j++) {
      var d = vp.children[j];
      if (d.id && !/-bg$/.test(d.id) && A.visible(d)) return A.idWords(d.id);
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
