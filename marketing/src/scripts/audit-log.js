// Illustrative append-only audit log that streams rows while visible.
import { $, $$, reduce, fine } from "./site.js";

const pad = (n) => String(n).padStart(2, "0");
  /* audit log stream */
  const log = $('#log');
  const EV = [
    ['ai.draft.generate', 'ev-ai', 'provider=azure-openai study=CT-chest', true],
    ['validation.run', 'ev-val', 'rulebook=chest_ct_v1 blockers=1', false],
    ['report.edit', 'ev-x', 'section=impression actor=radiologist', true],
    ['validation.run', 'ev-val', 'rulebook=chest_ct_v1 blockers=0', false],
    ['report.sign', 'ev-sign', 'actor=radiologist ai.signed=false', false],
    ['export.fhir', 'ev-x', 'DiagnosticReport status=final', false],
    ['ai.draft.generate', 'ev-ai', 'provider=ollama local=true study=MR-brain', true],
    ['critical.callback', 'ev-val', 'documented=true actor=radiologist', false],
  ];
  let evI = 0;
  const addRow = anim => {
    const [e, cls, meta, phi] = EV[evI++ % EV.length], d = new Date();
    const li = document.createElement('li');
    li.innerHTML = `<span class="t">${pad(d.getHours())}:${pad(d.getMinutes())}:${pad(d.getSeconds())}</span><span><span class="${cls}">${e}</span> ${meta}</span><span class="phi${phi ? ' y' : ''}">phi=${phi}</span>`;
    if (anim) li.classList.add('new');
    log.prepend(li); while (log.children.length > 8) log.lastElementChild.remove();
  };
  for (let i = 0; i < 6; i++) addRow(false);
  if (!reduce) { let iv = 0; new IntersectionObserver(([en]) => { if (en.isIntersecting && !iv) iv = setInterval(() => addRow(true), 2300); else if (!en.isIntersecting && iv) { clearInterval(iv); iv = 0; } }).observe(log); }

