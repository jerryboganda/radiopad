// Home hero: cursor spotlight, headline sweep, synthetic CT slice + self-typing report demo.
import { $, $$, reduce, fine } from "./site.js";

const pad = (n) => String(n).padStart(2, "0");
  /* hero spotlight + headline luminance sweep */
  const hero = $('.hero'), spot = $('#spot');
  if (fine && !reduce) {
    let tx = 0, ty = 0, x = 0, y = 0, raf = 0;
    const tick = () => { x += (tx - x) * 0.12; y += (ty - y) * 0.12; spot.style.transform = `translate3d(${x - 340}px,${y - 340}px,0)`; raf = Math.abs(tx - x) + Math.abs(ty - y) > 0.5 ? requestAnimationFrame(tick) : 0; };
    hero.addEventListener('pointermove', e => { const r = hero.getBoundingClientRect(); tx = e.clientX - r.left; ty = e.clientY - r.top; if (!hero.classList.contains('lit')) { x = tx; y = ty; hero.classList.add('lit'); } if (!raf) raf = requestAnimationFrame(tick); });
    hero.addEventListener('pointerleave', () => hero.classList.remove('lit'));
  }
  const h1 = $('[data-scan]');
  if (h1 && !reduce) { let i = 0; $$('.ln', h1).forEach(l => { l.innerHTML = l.textContent.trim().split(/\s+/).map(w => `<span class="w" style="--i:${i++}">${w}</span>`).join(' '); }); requestAnimationFrame(() => h1.classList.add('scan')); }


  /* ===== CT demo ===== */
  const S = 640, cv = $('#ct'), ctx = cv.getContext('2d'), viewer = $('#viewer');
  const mulberry = a => () => { a |= 0; a = a + 0x6D2B79F5 | 0; let t = Math.imul(a ^ a >>> 15, 1 | a); t = t + Math.imul(t ^ t >>> 7, 61 | t) ^ t; return ((t ^ t >>> 14) >>> 0) / 4294967296; };
  const gr = v => `rgb(${v | 0},${v | 0},${v | 0})`;
  const NOD = { x: 0.5 - 0.2, y: 0.5 + 0.085 }; // fraction of image
  function blob(c, pts) {
    const n = pts.length; c.beginPath(); c.moveTo(pts[0][0], pts[0][1]);
    for (let i = 0; i < n; i++) { const p0 = pts[(i - 1 + n) % n], p1 = pts[i], p2 = pts[(i + 1) % n], p3 = pts[(i + 2) % n]; c.bezierCurveTo(p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6, p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6, p2[0], p2[1]); }
    c.closePath();
  }
  function makeSlice(seed, k, nodule) {
    const o = document.createElement('canvas'); o.width = o.height = S; const c = o.getContext('2d'), R = mulberry(seed), u = S, cx = S / 2, cy = S / 2;
    const ell = (x, y, rx, ry, rot, f) => { c.beginPath(); c.ellipse(x, y, rx, ry, rot, 0, Math.PI * 2); c.fillStyle = f; c.fill(); };
    c.fillStyle = '#020203'; c.fillRect(0, 0, S, S);
    c.strokeStyle = gr(46); c.lineWidth = u * 0.008; c.beginPath(); c.moveTo(cx - u * 0.44, cy + u * 0.33); c.quadraticCurveTo(cx, cy + u * 0.39, cx + u * 0.44, cy + u * 0.33); c.stroke();
    c.strokeStyle = gr(26); c.beginPath(); c.moveTo(cx - u * 0.46, cy + u * 0.36); c.quadraticCurveTo(cx, cy + u * 0.42, cx + u * 0.46, cy + u * 0.36); c.stroke();
    ell(cx, cy, u * 0.43, u * 0.305, 0, gr(132));
    ell(cx, cy, u * 0.418, u * 0.292, 0, gr(92));
    ell(cx, cy + u * 0.004, u * 0.392, u * 0.268, 0, gr(146));
    ell(cx - u * 0.085, cy + u * 0.215, u * 0.078, u * 0.05, 0.15, gr(154)); ell(cx + u * 0.085, cy + u * 0.215, u * 0.078, u * 0.05, -0.15, gr(154));
    ell(cx, cy - u * 0.005, u * 0.352, u * 0.232, 0, gr(100));
    c.strokeStyle = gr(236); c.lineCap = 'round';
    [-1.2, -0.86, -0.52, -0.18, 0.16, 0.5, 0.86].forEach(a => [a, Math.PI - a].forEach(b => { c.lineWidth = u * (0.013 + R() * 0.006); const s = 0.07 + R() * 0.04; c.beginPath(); c.ellipse(cx, cy, u * 0.37, u * 0.25, 0, b - s, b + s); c.stroke(); }));
    const P = arr => arr.map(([x, y]) => [cx + x * u, cy + y * u]);
    const rL = P([[-0.07, -0.19], [-0.17, -0.215], [-0.27, -0.185], [-0.325, -0.1], [-0.335, 0.02], [-0.31, 0.13], [-0.24, 0.205], [-0.15, 0.215], [-0.095, 0.17], [-0.075, 0.08], [-0.062, -0.04]]);
    const lL = P([[0.07, -0.19], [0.17, -0.215], [0.27, -0.185], [0.325, -0.1], [0.335, 0.02], [0.31, 0.13], [0.24, 0.205], [0.15, 0.215], [0.1, 0.175], [0.105, 0.1], [0.175, 0.06], [0.205, -0.03], [0.17, -0.12], [0.09, -0.17]]);
    const lg = c.createLinearGradient(0, cy - u * 0.22, 0, cy + u * 0.22); lg.addColorStop(0, gr(22)); lg.addColorStop(1, gr(44));
    const branch = (x, y, a, len, w, d) => {
      const x2 = x + Math.cos(a) * len, y2 = y + Math.sin(a) * len, bend = (R() - 0.5) * 0.7;
      c.strokeStyle = `rgba(226,229,233,${0.3 + 0.45 * Math.min(1, w / (u * 0.011))})`; c.lineWidth = w;
      c.beginPath(); c.moveTo(x, y); c.quadraticCurveTo(x + Math.cos(a + bend) * len * 0.5, y + Math.sin(a + bend) * len * 0.5, x2, y2); c.stroke();
      if (d > 0 && w > 0.55) { const s = 0.32 + R() * 0.38; branch(x2, y2, a - s, len * (0.66 + R() * 0.14), w * 0.68, d - 1); branch(x2, y2, a + s * 0.85, len * (0.6 + R() * 0.14), w * 0.62, d - 1); }
    };
    [[rL, [cx - u * 0.085, cy + u * 0.01], Math.PI, 2.7], [lL, [cx + u * 0.13, cy + u * 0.1], 0, 2.3]].forEach(([pts, h, base, spread]) => {
      c.save(); blob(c, pts); c.fillStyle = lg; c.fill(); c.clip();
      for (let j = 0; j < 9; j++) branch(h[0], h[1], base + spread * (j / 8 - 0.5) + (R() - 0.5) * 0.15, u * (0.08 + R() * 0.05), u * 0.011, 5);
      for (let j = 0; j < 240; j++) { c.fillStyle = `rgba(220,223,228,${0.28 + R() * 0.38})`; c.beginPath(); c.arc(cx + (R() * 0.7 - 0.35) * u, cy + (R() * 0.46 - 0.23) * u, u * (0.0013 + R() * 0.0032), 0, 7); c.fill(); }
      c.restore(); c.strokeStyle = 'rgba(255,255,255,.07)'; c.lineWidth = 1.2; blob(c, pts); c.stroke();
    });
    const hs = 1 + (k - 0.5) * 0.12;
    ell(cx + u * 0.06, cy - u * 0.025, u * 0.146 * hs, u * 0.121 * hs, -0.45, gr(96));
    const hg = c.createRadialGradient(cx + u * 0.04, cy - u * 0.05, u * 0.02, cx + u * 0.06, cy - u * 0.025, u * 0.14); hg.addColorStop(0, gr(198)); hg.addColorStop(1, gr(170));
    ell(cx + u * 0.06, cy - u * 0.025, u * 0.13 * hs, u * 0.105 * hs, -0.45, hg);
    ell(cx + u * 0.065, cy + u * 0.125, u * 0.038, u * 0.038, 0, gr(190));
    ell(cx + u * 0.018, cy + u * 0.11, u * 0.016, u * 0.013, 0, gr(150)); ell(cx + u * 0.018, cy + u * 0.11, u * 0.004, u * 0.003, 0, gr(20));
    ell(cx, cy + u * 0.16, u * 0.056, u * 0.05, 0, gr(212));
    c.strokeStyle = gr(250); c.lineWidth = u * 0.008; c.beginPath(); c.ellipse(cx, cy + u * 0.16, u * 0.056, u * 0.05, 0, 0, Math.PI * 2); c.stroke();
    ell(cx, cy + u * 0.232, u * 0.024, u * 0.02, 0, gr(76));
    c.strokeStyle = gr(238); c.lineWidth = u * 0.016; c.beginPath(); c.ellipse(cx, cy + u * 0.235, u * 0.04, u * 0.034, 0, 0.2, Math.PI - 0.2); c.stroke();
    c.beginPath(); c.moveTo(cx, cy + u * 0.268); c.lineTo(cx, cy + u * 0.29); c.stroke();
    c.lineWidth = u * 0.014; c.beginPath(); c.moveTo(cx - u * 0.035, cy + u * 0.21); c.lineTo(cx - u * 0.088, cy + u * 0.195); c.moveTo(cx + u * 0.035, cy + u * 0.21); c.lineTo(cx + u * 0.088, cy + u * 0.195); c.stroke();
    ell(cx, cy - u * 0.258, u * 0.04, u * 0.016, 0, gr(226));
    ell(cx - u * 0.315, cy + u * 0.175, u * 0.085, u * 0.011, 0.95, gr(228)); ell(cx + u * 0.315, cy + u * 0.175, u * 0.085, u * 0.011, -0.95, gr(228));
    if (nodule) { const nx = NOD.x * S, ny = NOD.y * S, ng = c.createRadialGradient(nx, ny, 0, nx, ny, u * 0.016); ng.addColorStop(0, gr(224)); ng.addColorStop(0.7, gr(198)); ng.addColorStop(1, 'rgba(200,200,200,0)'); c.fillStyle = ng; c.beginPath(); c.arc(nx, ny, u * 0.016, 0, 7); c.fill(); }
    c.filter = 'blur(0.6px)'; c.drawImage(o, 0, 0); c.filter = 'none';
    const id = c.getImageData(0, 0, S, S), d = id.data;
    for (let i = 0; i < d.length; i += 4) { const v = d[i] + (R() - 0.5) * 18; d[i] = d[i + 1] = d[i + 2] = v; }
    c.putImageData(id, 0, 0);
    return { cv: o, data: d };
  }

  let W = 0, dpr = 1, vig = null, grain = [];
  const size = () => { dpr = Math.min(2, devicePixelRatio || 1); W = Math.max(1, Math.round(cv.getBoundingClientRect().width * dpr)); cv.width = cv.height = W; vig = ctx.createRadialGradient(W / 2, W / 2, W * 0.3, W / 2, W / 2, W * 0.72); vig.addColorStop(0, 'rgba(0,0,0,0)'); vig.addColorStop(1, 'rgba(0,0,0,.55)'); };
  for (let k = 0; k < 4; k++) { const g = document.createElement('canvas'); g.width = g.height = 128; const gc = g.getContext('2d'), im = gc.createImageData(128, 128); for (let i = 0; i < im.data.length; i += 4) { const v = Math.random() * 255; im.data[i] = im.data[i + 1] = im.data[i + 2] = v; im.data[i + 3] = 255; } gc.putImageData(im, 0, 0); grain.push(ctx.createPattern(g, 'repeat')); }

  const key = makeSlice(7, 1, true);
  const slices = [key];
  size();

  /* report typing */
  const report = $('#report'), segs = $$('[data-type]', report).map(el => ({ el, full: el.textContent, n: -1 }));
  const lat = $('#lat'), fixBtn = $('#fixBtn'), fBlock = $('#fBlock'), fWarn = $('#fWarn'), flags = $$('.flag', report);
  const signBtn = $('#signBtn'), signNote = $('#signNote'), stStatus = $('#stStatus'), impSec = $('#impSec'), aiTag = $('#aiTag span'), imNum = $('#imNum');
  const T_SCROLL = 1.4, T_SWEEP = 4.6, CPS = 52;
  const AT = [0.25, T_SCROLL + NOD.x * T_SWEEP, 0, 0, T_SCROLL + T_SWEEP * 0.9, 0, 0];
  let sched = [], flagAt = 0, done = false, blockers = 1, signed = false, t0 = performance.now(), running = false, pausedAt = 0, frame = 0;
  const plan = () => { let end = 0; sched = segs.map((s, i) => { const st = Math.max(AT[i] || 0, end); end = st + s.full.length / CPS; return st; }); flagAt = end + 0.35; };
  const setSeg = (s, n) => { if (n === s.n) return; s.n = n; s.el.innerHTML = `<span class="t">${s.full.slice(0, n)}</span><span class="r">${s.full.slice(n)}</span>`; };
  const resetDemo = () => {
    segs[5].full = 'left'; lat.className = 'lat bad'; blockers = 1; signed = false; done = false;
    fBlock.classList.remove('ok'); fWarn.classList.remove('ok'); $('.ftx', fBlock).textContent = 'Laterality mismatch. Findings say right lower lobe, impression says left.'; $('.ftx', fWarn).textContent = 'Impression is AI-drafted and not yet reviewed.';
    $('b', fBlock).textContent = 'Blocker'; $('b', fWarn).textContent = 'Warning';
    impSec.classList.remove('reviewed'); aiTag.textContent = 'AI draft, requires review';
    signBtn.className = 'btn btn-sign'; $('span', signBtn).textContent = 'Sign report'; signBtn.disabled = false;
    signNote.className = 'rp-note'; signNote.textContent = 'Only a radiologist can sign. RadioPad never signs.';
    stStatus.className = 'chip chip-draft'; stStatus.textContent = 'Draft, unsigned';
    plan();
  };
  const finish = () => { segs.forEach(s => setSeg(s, s.full.length)); segs.forEach(s => s.el.classList.remove('typing')); flags.forEach(f => f.classList.add('in')); done = true; signBtn.classList.add('ready'); };

  function draw(t) {
    const sweepP = Math.min(1, Math.max(0, (t - T_SCROLL) / T_SWEEP));
    let img = key.cv;
    if (t < T_SCROLL && slices.length > 1) { const idx = Math.min(slices.length - 1, Math.floor((t / T_SCROLL) * slices.length)); img = slices[idx].cv; imNum.textContent = 120 + Math.round((t / T_SCROLL) * 22); }
    else imNum.textContent = '142';
    ctx.globalAlpha = 1; ctx.drawImage(img, 0, 0, W, W);
    const bx = sweepP * W;
    if (t >= T_SCROLL && sweepP < 1) {
      const dim = Math.min(1, (t - T_SCROLL) / 0.3) * 0.55;
      ctx.fillStyle = `rgba(2,4,8,${dim})`; ctx.fillRect(bx, 0, W - bx, W);
      const g = ctx.createLinearGradient(bx - 70 * dpr, 0, bx, 0); g.addColorStop(0, 'rgba(134,197,255,0)'); g.addColorStop(1, 'rgba(134,197,255,.2)');
      ctx.fillStyle = g; ctx.fillRect(bx - 70 * dpr, 0, 70 * dpr, W);
      ctx.fillStyle = 'rgba(214,236,255,.95)'; ctx.fillRect(bx - dpr, 0, 1.6 * dpr, W);
    }
    const passT = T_SCROLL + NOD.x * T_SWEEP;
    if (t >= passT) {
      const p = Math.min(1, (t - passT) / 0.45), e = 1 - Math.pow(1 - p, 3), nx = NOD.x * W, ny = NOD.y * W, r = W * 0.03 * (1.7 - 0.7 * e);
      ctx.globalAlpha = e; ctx.strokeStyle = 'rgba(134,197,255,.95)'; ctx.lineWidth = 1.4 * dpr; ctx.shadowColor = 'rgba(134,197,255,.8)'; ctx.shadowBlur = 10 * dpr;
      ctx.beginPath(); ctx.arc(nx, ny, r, 0, Math.PI * 2); ctx.stroke(); ctx.shadowBlur = 0;
      ctx.setLineDash([3 * dpr, 3 * dpr]); ctx.beginPath(); ctx.moveTo(nx - W * 0.016, ny); ctx.lineTo(nx + W * 0.016, ny); ctx.stroke(); ctx.setLineDash([]);
      ctx.beginPath(); ctx.moveTo(nx + r * 0.7, ny - r * 0.7); ctx.lineTo(nx + W * 0.08, ny - W * 0.08); ctx.lineTo(nx + W * 0.14, ny - W * 0.08); ctx.stroke();
      ctx.fillStyle = 'rgba(220,236,255,.95)'; ctx.font = `${Math.round(11 * dpr)}px "Geist Mono", ui-monospace, monospace`; ctx.fillText('6 mm', nx + W * 0.085, ny - W * 0.09);
      ctx.globalAlpha = 1;
    }
    ctx.fillStyle = vig; ctx.fillRect(0, 0, W, W);
    if (!reduce) { ctx.globalAlpha = 0.045; ctx.save(); ctx.translate((frame * 37) % 128, (frame * 53) % 128); ctx.fillStyle = grain[frame % 4]; ctx.fillRect(-128, -128, W + 256, W + 256); ctx.restore(); ctx.globalAlpha = 1; }
  }

  function loop(now) {
    if (!running) return;
    frame++;
    const t = (now - t0) / 1000;
    draw(t);
    if (!done) {
      segs.forEach((s, i) => { const n = Math.max(0, Math.min(s.full.length, Math.floor((t - sched[i]) * CPS))); setSeg(s, n); s.el.classList.toggle('typing', t >= sched[i] && n < s.full.length); });
      flags.forEach((f, i) => f.classList.toggle('in', t >= flagAt + i * 0.22));
      if (t >= flagAt + 0.7) { done = true; signBtn.classList.add('ready'); }
    }
    requestAnimationFrame(loop);
  }

  const start = () => { resetDemo(); t0 = performance.now(); if (!reduce) { report.classList.add('armed'); segs.forEach(s => setSeg(s, 0)); } };

  if (reduce) { resetDemo(); finish(); draw(T_SCROLL + T_SWEEP + 1); }
  else {
    start(); draw(0);
    let k = 0; const more = () => { if (k < 5) { slices.splice(slices.length - 1, 0, makeSlice(11 + k, k / 5, false)); k++; setTimeout(more, 16); } };
    setTimeout(more, 60);
    new IntersectionObserver(([en]) => {
      if (en.isIntersecting && !running) { running = true; if (pausedAt) { t0 += performance.now() - pausedAt; pausedAt = 0; } requestAnimationFrame(loop); }
      else if (!en.isIntersecting && running) { running = false; pausedAt = performance.now(); }
    }, { threshold: 0.05 }).observe(viewer);
  }
  addEventListener('resize', () => { size(); if (!running) draw(reduce ? 99 : (performance.now() - t0) / 1000); });

  fixBtn.addEventListener('click', () => {
    segs[5].full = 'right'; segs[5].n = -1; setSeg(segs[5], 5); lat.className = 'lat fixed';
    fBlock.classList.add('ok'); $('b', fBlock).textContent = 'Resolved'; $('.ftx', fBlock).textContent = 'Laterality consistent: right lower lobe in findings and impression.';
    fWarn.classList.add('ok'); $('b', fWarn).textContent = 'Reviewed'; $('.ftx', fWarn).textContent = 'Impression edited and reviewed by the radiologist.';
    impSec.classList.add('reviewed'); aiTag.textContent = 'Reviewed by radiologist';
    blockers = 0; signNote.className = 'rp-note'; signNote.textContent = 'No blockers. Ready for your signature.';
    signBtn.focus({ preventScroll: true });
  });
  signBtn.addEventListener('click', () => {
    if (signed) return;
    if (!done) { signNote.className = 'rp-note warn'; signNote.textContent = 'The draft is still being written.'; return; }
    if (blockers > 0) { signBtn.classList.remove('shake'); void signBtn.offsetWidth; signBtn.classList.add('shake'); signNote.className = 'rp-note warn'; signNote.textContent = 'Resolve 1 blocker before signing.'; return; }
    signed = true; const d = new Date();
    signBtn.className = 'btn btn-sign signed'; $('span', signBtn).textContent = `Signed by you, ${pad(d.getHours())}:${pad(d.getMinutes())}`;
    signNote.className = 'rp-note good'; signNote.textContent = 'FHIR R4 DiagnosticReport, PDF and DOCX ready to export.';
    stStatus.className = 'chip chip-final'; stStatus.textContent = 'Final, signed by radiologist';
  });
  $('#replay').addEventListener('click', () => { if (reduce) { resetDemo(); finish(); return; } flags.forEach(f => f.classList.remove('in')); start(); });

  /* crosshair + HU readout (window math: W1500 L-600) */
  const xh = $('#xh'), hu = $('#hu'), huVal = $('#huVal');
  if (fine) {
    viewer.addEventListener('pointermove', e => {
      const r = viewer.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top;
      const px = Math.min(S - 1, Math.max(0, Math.floor(x / r.width * S))), py = Math.min(S - 1, Math.max(0, Math.floor(y / r.height * S)));
      const v = key.data[(py * S + px) * 4], val = Math.round(-1350 + v / 255 * 1500);
      xh.style.setProperty('--x', x + 'px'); xh.style.setProperty('--y', y + 'px');
      hu.hidden = false; huVal.textContent = val; hu.style.transform = `translate3d(${Math.min(x + 14, r.width - 92)}px,${Math.min(y + 14, r.height - 30)}px,0)`;
      viewer.classList.add('hover');
    });
    viewer.addEventListener('pointerleave', () => { viewer.classList.remove('hover'); hu.hidden = true; });
  }
