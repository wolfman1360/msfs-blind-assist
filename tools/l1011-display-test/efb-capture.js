// Compact capture of the L-1011 Paper Flight Bag for the efb-*.html fixtures (see docs/l1011.md, "Capturing a
// display fixture"). Evaluated in an iniEFBL1011_n view, it stamps data-vis="1" from the LIVE computed style,
// clones the tablet, keeps the nav bar, the pause overlay, the keyboard shell and the visible page (on the Menu,
// only the sidebar and the visible pane), and returns that clone's HTML with iniBuilds' inline handlers,
// decorative images, coordinates and every field value removed, the Navigraph code removed and the OFP replaced
// by a two-line stand-in. The live page is left as it was. Not a test: node --test does not pick it up.
(function () {
  var root = document.getElementById('iniEFB');
  var all = root.getElementsByTagName('*');
  for (var i = 0; i < all.length; i++) {
    try {
      var cs = window.getComputedStyle(all[i]), r = all[i].getBoundingClientRect();
      if (cs.display !== 'none' && cs.visibility !== 'hidden' && r.width > 0 && r.height > 0) all[i].setAttribute('data-vis', '1');
    } catch (e) {}
  }
  var clone = root.cloneNode(true);
  for (var j = 0; j < all.length; j++) all[j].removeAttribute('data-vis');

  function drop(sel) {
    var n = clone.querySelectorAll(sel);
    for (var k = n.length - 1; k >= 0; k--) n[k].parentNode.removeChild(n[k]);
  }
  var pages = clone.querySelectorAll('.page');
  for (var p = pages.length - 1; p >= 0; p--) if (pages[p].classList.contains('page--hide')) pages[p].parentNode.removeChild(pages[p]);
  var panes = clone.querySelectorAll('.page-options__content > *');
  for (var q = panes.length - 1; q >= 0; q--) if (!panes[q].getAttribute('data-vis')) panes[q].parentNode.removeChild(panes[q]);
  drop('script, style, svg, canvas, img.perforated, .virtual-keyboard .keyboard-input, .navigation-bar-background');
  drop('.navigraph-login__usercode span');
  var imgs = clone.querySelectorAll('img');
  for (var m = imgs.length - 1; m >= 0; m--) if (!imgs[m].id && !imgs[m].classList.contains('toggle-icon')) imgs[m].parentNode.removeChild(imgs[m]);
  var plan = clone.querySelector('.simbrief-viewer__plan');
  if (plan) plan.innerHTML = '<div data-vis="1"><pre data-vis="1">SYNTHETIC OFP LINE 1\nSYNTHETIC OFP LINE 2</pre></div>';

  var KEEP = /^(id|class|for|type|role|placeholder|disabled|readonly|min|max|step|inputmode|aria-label|title|alt|src|name|tabindex|style|data-[a-z0-9-]+)$/;
  var STYLE = /^(display|visibility|opacity|pointer-events)$/;
  var everything = [clone];
  var els = clone.getElementsByTagName('*');
  for (var e = 0; e < els.length; e++) everything.push(els[e]);
  everything.forEach(function (el) {
    for (var a = el.attributes.length - 1; a >= 0; a--) {
      var name = el.attributes[a].name;
      if (!KEEP.test(name) || name.indexOf('data-react') === 0 || name === 'data-rect') { el.removeAttribute(name); continue; }
      if (name === 'src') el.setAttribute('src', el.getAttribute('src').split('/').pop());
      if (name === 'style') {
        var kept = (el.getAttribute('style') || '').split(';').map(function (s) { return s.trim(); }).filter(function (s) {
          return s && STYLE.test(s.split(':')[0].trim());
        });
        if (kept.length) el.setAttribute('style', kept.join('; ') + ';'); else el.removeAttribute('style');
      }
    }
    if (el.tagName === 'INPUT') el.removeAttribute('value');
  });
  (function clean(n) {
    for (var c = n.firstChild; c; ) {
      var next = c.nextSibling;
      if (c.nodeType === 8) n.removeChild(c);
      else if (c.nodeType === 3) {
        var t = c.data.replace(/\s+/g, ' ');
        if (t === ' ' && (!c.previousSibling || !c.nextSibling)) n.removeChild(c); else c.data = t;
      } else if (c.nodeType === 1 && c.tagName !== 'PRE') clean(c);
      c = next;
    }
  })(clone);
  return clone.outerHTML.replace(/></g, '>\n<');
})()
