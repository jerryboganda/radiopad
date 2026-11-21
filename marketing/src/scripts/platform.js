// Platform explorer: auto-advancing modules, each with a live demo.
import { $, $$, reduce, fine } from "./site.js";

  (() => {
    const plat = $('#plat'); if (!plat) return;
    const tabs = $$('.pn', plat), panels = tabs.map(t => document.getElementById(t.getAttribute('aria-controls')));
    const list = $('.pn-list', plat), idx = $('#swIdx'), state = $('#swState'), stage = $('#platStage');
    const row = matchMedia('(max-width: 960px)');
    const pad = n => String(n).padStart(2, '0');
    let cur = 0, timers = [], visible = false;
    const T = (fn, ms) => timers.push(setTimeout(fn, ms));
    const EVERY = (fn, ms) => timers.push(setInterval(fn, ms));
    const stop = () => { timers.forEach(id => { clearTimeout(id); clearInterval(id); }); timers = []; };
    const manual = () => { stop(); plat.classList.remove('auto'); state.textContent = 'Interactive'; };
    const seg = (box, i) => { box.style.setProperty('--i', i); $$('.sg', box).forEach((b, k) => b.setAttribute('aria-pressed', String(k === i))); };
    const restart = el => { el.classList.remove('in', 'flash'); void el.offsetWidth; };

    const TPL = [
      ['Chest CT, standard', 'CT', ['Indication', 'Technique', 'Comparison', 'Lungs and airways', 'Pleura', 'Mediastinum and hila', 'Heart and great vessels', 'Bones and soft tissues', 'Impression']],
      ['Chest CT, lung screening', 'CT', ['Indication', 'Technique', 'Comparison', 'Nodule table', 'Emphysema', 'Coronary calcification', 'Other findings', 'Impression', 'Recommendation']],
      ['MR brain, stroke protocol', 'MR', ['Indication', 'Technique', 'Comparison', 'Diffusion', 'Hemorrhage', 'Vessels', 'Parenchyma', 'Ventricles and extra-axial spaces', 'Impression']]
    ];
    const tplSeg = $('#tplSeg'), tplSecs = $('#tplSecs');
    const setTpl = k => {
      seg(tplSeg, k); const [n, m, s] = TPL[k];
      $('#tplName').textContent = n; $('#tplMod').textContent = m;
      tplSecs.innerHTML = s.map((x, j) => `<li style="--j:${j}"><span>${x}</span><i></i></li>`).join('');
      if (!reduce) { restart(tplSecs); tplSecs.classList.add('in'); }
    };
    $$('.sg', tplSeg).forEach((b, k) => b.addEventListener('click', () => { manual(); setTpl(k); }));

    const libQ = $('#libQ'), libIns = $('#libIns'), libRes = $$('#libRes .lr');
    const insert = b => { libRes.forEach(x => x.classList.toggle('pick', x === b)); libIns.textContent = b.textContent; if (!reduce) { restart(libIns); libIns.classList.add('flash'); } };
    libRes.forEach(b => b.addEventListener('click', () => { manual(); libRes.forEach(x => x.classList.remove('hide')); libQ.textContent = 'pleural eff'; insert(b); }));

    const steps = $$('#critSteps li'), critOk = $('#critOk');

    const peerSeg = $('#peerSeg'), dfs = $$('#pp-peer .df');
    const setPeer = i => { seg(peerSeg, i); peerSeg.dataset.tone = 'gar'[i]; };
    $$('.sg', peerSeg).forEach((b, k) => b.addEventListener('click', () => { manual(); dfs.forEach(d => d.classList.add('hl')); setPeer(k); }));

    const drift = $('#pp-drift .drift');

    const prSeg = $('#prSeg'), prVal = $('#prVal'), impV = $$('#pp-prompt .imp-v');
    const PRV = ['"Short prose paragraph, most significant first"', '"Numbered, most significant first, max 5"'];
    const setPr = i => { seg(prSeg, i); prVal.textContent = PRV[i]; impV.forEach((v, k) => v.classList.toggle('on', k === i)); };
    $$('.sg', prSeg).forEach((b, k) => b.addEventListener('click', () => { manual(); setPr(k); }));

    const mkBtns = $$('#mkList .mk-b');
    const label = (b, t) => { b.firstElementChild.textContent = t; };
    const install = b => {
      if (b.classList.contains('done') || b.classList.contains('ing')) return;
      b.classList.add('ing'); label(b, 'Installing');
      setTimeout(() => { b.classList.remove('ing'); b.classList.add('done'); b.setAttribute('aria-pressed', 'true'); label(b, 'Installed'); }, reduce ? 0 : 1150);
    };
    mkBtns.forEach(b => b.addEventListener('click', () => { manual(); install(b); }));

    const cliCmd = $('#termCmd'), CMD = cliCmd.textContent, outLines = $$('#cliOut .ln.o');

    const DEMOS = {
      tpl() { let k = 0; setTpl(0); EVERY(() => { k = (k + 1) % TPL.length; setTpl(k); }, 2300); },
      lib() {
        libQ.textContent = ''; libIns.textContent = ''; libRes.forEach(b => b.classList.remove('pick'));
        libRes.forEach(b => b.classList.add('hide'));
        const q = 'pleural eff'; [...q].forEach((c, i) => T(() => { libQ.textContent += c; }, 300 + i * 85));
        const t1 = 450 + q.length * 85;
        libRes.forEach((b, i) => T(() => b.classList.remove('hide'), t1 + i * 70));
        T(() => libRes[1].classList.add('pick'), t1 + 900);
        T(() => insert(libRes[1]), t1 + 1500);
      },
      crit() { steps.forEach(s => s.classList.remove('done')); critOk.classList.add('off'); steps.forEach((s, i) => T(() => s.classList.add('done'), 500 + i * 1100)); T(() => critOk.classList.remove('off'), 3800); },
      peer() { dfs.forEach(d => d.classList.remove('hl')); setPeer(0); T(() => dfs.forEach(d => d.classList.add('hl')), 900); T(() => setPeer(1), 1800); },
      drift() { drift.classList.add('reset'); void drift.offsetWidth; T(() => drift.classList.remove('reset'), 80); },
      prompt() { let i = 0; setPr(0); EVERY(() => { i = 1 - i; setPr(i); }, 2400); },
      market() {
        mkBtns.slice(1).forEach(b => { b.classList.remove('done', 'ing'); b.setAttribute('aria-pressed', 'false'); label(b, 'Install'); });
        T(() => install(mkBtns[1]), 700); T(() => install(mkBtns[2]), 2300); T(() => install(mkBtns[3]), 3900);
      },
      cli() {
        cliCmd.textContent = ''; outLines.forEach(l => l.classList.add('hide'));
        [...CMD].forEach((c, i) => T(() => { cliCmd.textContent += c; }, 250 + i * 36));
        const t1 = 500 + CMD.length * 36; outLines.forEach((l, i) => T(() => l.classList.remove('hide'), t1 + i * 280));
      }
    };

    const show = (i, focus) => {
      stop(); cur = i;
      tabs.forEach((t, k) => { const on = k === i; t.setAttribute('aria-selected', String(on)); t.tabIndex = on ? 0 : -1; panels[k].classList.toggle('on', on); });
      idx.textContent = `MODULE ${pad(i + 1)} / ${pad(tabs.length)}`;
      if (row.matches) list.scrollTo({ left: tabs[i].offsetLeft - 8, behavior: reduce ? 'auto' : 'smooth' });
      if (focus) tabs[i].focus({ preventScroll: true });
      if (!reduce && visible) DEMOS[panels[i].dataset.demo]();
    };

    const orient = () => list.setAttribute('aria-orientation', row.matches ? 'horizontal' : 'vertical');
    orient(); row.addEventListener('change', orient);

    tabs.forEach((t, i) => {
      t.addEventListener('click', () => { manual(); show(i); });
      t.addEventListener('keydown', e => {
        const n = { ArrowDown: i + 1, ArrowRight: i + 1, ArrowUp: i - 1, ArrowLeft: i - 1, Home: 0, End: tabs.length - 1 }[e.key];
        if (n === undefined) return;
        e.preventDefault(); manual(); show((n + tabs.length) % tabs.length, true);
      });
      $('.pn-bar i', t).addEventListener('animationend', () => { if (plat.classList.contains('auto') && i === cur) show((cur + 1) % tabs.length); });
    });

    if (fine) stage.addEventListener('pointermove', e => { const r = stage.getBoundingClientRect(); stage.style.setProperty('--mx', `${e.clientX - r.left}px`); stage.style.setProperty('--my', `${e.clientY - r.top}px`); });

    if (!reduce && 'IntersectionObserver' in window) {
      plat.style.setProperty('--dur', '7200ms');
      plat.classList.add('auto');
      plat.addEventListener('pointerenter', e => { if (e.pointerType === 'mouse') plat.classList.add('hold'); });
      plat.addEventListener('pointerleave', () => plat.classList.remove('hold'));
      plat.addEventListener('focusin', () => plat.classList.add('hold'));
      plat.addEventListener('focusout', e => { if (!plat.contains(e.relatedTarget)) plat.classList.remove('hold'); });
      new IntersectionObserver(([en]) => {
        visible = en.isIntersecting; plat.classList.toggle('off', !visible);
        if (!visible) stop(); else DEMOS[panels[cur].dataset.demo]();
      }, { threshold: 0.3 }).observe(plat);
    }
  })();

