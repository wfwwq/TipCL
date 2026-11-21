/* AI3-visual: injected at the end of a copied v3 mockup (see shoot-mockups.ps1).
 * Reads the URL hash and clicks the mockup's own state buttons, then records the
 * bounding box of the window element in <pre id="__tour_rect"> so the shooter can crop.
 * Hash keys (all optional, joined with &):
 *   m=normal|edit|lib      launch_v3 mode (#segMode)
 *   w=1000|780             launch_v3 width (#segW)
 *   accent=green|red       layout_v3 theme colour (#segAccent)
 *   A=normal|edit|now      layout_v3 section A view
 *   lib=1                  layout_v3 section A: open the add drawer
 *   B=new|now              layout_v3 section B view
 *   adv=1                  layout_v3 section B: expand the "advanced" card
 *   scroll=<group title>   layout_v3 section B: scroll #scB so that group title is on top
 *   bottom=1               layout_v3 section B: scroll #scB to the bottom
 *   sel=<css>              element to crop (default: #win, else #winA)
 */
(function () {
  var kv = {};
  decodeURIComponent(location.hash.replace(/^#/, '')).split('&').forEach(function (t) {
    if (!t) return;
    var i = t.indexOf('=');
    if (i < 0) kv[t] = '1'; else kv[t.slice(0, i)] = t.slice(i + 1);
  });
  function q(s) { return document.querySelector(s); }
  function click(s) { var e = q(s); if (e) e.click(); return !!e; }
  // end states only: no CSS transitions / animations (the shot must not catch a half-finished slide)
  var css = document.createElement('style');
  css.textContent = '*,*::before,*::after{transition:none!important;animation:none!important}';
  document.head.appendChild(css);
  var done = [];
  if (kv.accent && click('#segAccent button[data-a="' + kv.accent + '"]')) done.push('accent');
  if (kv.m && click('#segMode button[data-m="' + kv.m + '"]')) done.push('m');
  if (kv.w && click('#segW button[data-w="' + kv.w + '"]')) done.push('w');
  if (kv.A && click('#secA .vseg button[data-v="' + kv.A + '"]')) done.push('A');
  if (kv.B && click('#secB .vseg button[data-v="' + kv.B + '"]')) done.push('B');
  setTimeout(function () {
    if (kv.w) click('#segW button[data-w="' + kv.w + '"]');
    if (kv.lib && q('#winA') && click('#winA #bAdd')) done.push('lib');
    if (kv.adv && click('#advH')) done.push('adv');
  }, 350);
  setTimeout(function () {
    var sc = q('#scB');
    if (sc && kv.scroll) {
      var gl = Array.prototype.slice.call(sc.querySelectorAll('.gl')).filter(function (g) {
        return g.textContent.trim().indexOf(kv.scroll) === 0;
      })[0];
      if (gl) { sc.scrollTop = gl.offsetTop - 6; done.push('scroll'); }
    }
    if (sc && kv.bottom) { sc.scrollTop = sc.scrollHeight; done.push('bottom'); }
  }, 900);
  setTimeout(function () {
    var el = kv.sel ? q(kv.sel) : (q('#win') || q('#winA'));
    var out = { ok: !!el, done: done, hash: location.hash };
    if (el) {
      var r = el.getBoundingClientRect();
      out.x = Math.round(r.left + window.scrollX); out.y = Math.round(r.top + window.scrollY);
      out.w = Math.round(r.width); out.h = Math.round(r.height);
    }
    out.dw = document.documentElement.scrollWidth; out.dh = document.documentElement.scrollHeight;
    var pre = document.createElement('pre');
    pre.id = '__tour_rect';
    pre.style.cssText = 'position:absolute;left:-99999px;top:0;';
    pre.textContent = JSON.stringify(out);
    document.body.appendChild(pre);
  }, 1500);
})();
