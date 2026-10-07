/*
 * MSFS Blind Assist - iniBuilds L-1011 TriStar glareshield autopilot (AFCS) window reader.
 *
 * Runs inside the "L1011_AFCS" Coherent GT view through the remote debugger (CoherentDisplayClient).
 * READ ONLY: it changes nothing in the page. scrape() returns the panel's power state and the text
 * of its eight windows, #screen_1 .. #screen_8, exactly as drawn; L1011AfcsWindows (C#) turns them
 * into readable lines. Coherent GT is Chromium-49 class, so this is ES5.
 */
(function () {
  if (window.__MSFSBA_DISP && window.__MSFSBA_DISP.kind === 'l1011-afcs') {
    return 'MSFSBA_DISP_INSTALLED';
  }
  window.__MSFSBA_DISP = {
    kind: 'l1011-afcs',
    scrape: function () {
      try {
        var power = document.getElementById('customElectricity');
        var rows = ['power|' + (power && power.getAttribute('state') === 'off' ? 'off' : 'on')];
        for (var i = 1; i <= 8; i++) {
          var el = document.getElementById('screen_' + i);
          rows.push(i + '|' + (el ? (el.textContent || '') : ''));
        }
        return JSON.stringify({ ok: true, rows: rows });
      } catch (e) {
        return JSON.stringify({ ok: false, error: String(e) });
      }
    }
  };
  return 'MSFSBA_DISP_INSTALLED';
})();
