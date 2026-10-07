// Capture helper for the fixtures (see docs/l1011.md, "Capturing a display fixture"): evaluated in a
// Coherent view, it stamps data-vis/data-rect on every element, returns the body HTML, and removes
// the stamps again. Not a test: node --test does not pick it up.
(function(){
  var all = document.body.getElementsByTagName('*');
  function stamp(el){ try {
    var cs = window.getComputedStyle(el);
    var r = el.getBoundingClientRect();
    if (cs.display !== 'none' && cs.visibility !== 'hidden' && r.width > 0 && r.height > 0) el.setAttribute('data-vis','1');
    el.setAttribute('data-rect', Math.round(r.top)+','+Math.round(r.left)+','+Math.round(r.right)+','+Math.round(r.bottom));
  } catch(e){} }
  for (var i = 0; i < all.length; i++) stamp(all[i]);
  var html = document.body.outerHTML;
  for (var j = 0; j < all.length; j++) { all[j].removeAttribute('data-vis'); all[j].removeAttribute('data-rect'); }
  return html;
})()
