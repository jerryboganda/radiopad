// Rule-impact demo on solution pages ([data-rdemo]). A rule can be switched off for the tenant
// ([data-toggle]) or its flag fixed by the radiologist ([data-fix]). An open blocker holds signing.
import { $, $$, reduce } from "./site.js";

$$("[data-rdemo]").forEach((box) => {
  const flags = $$("[data-flag]", box), toggles = $$("[data-toggle]", box);
  const sign = $("[data-sign]", box), note = $("[data-note]", box), tag = $(".ai-tag span", box);
  const marks = Object.fromEntries($$("mark[data-m]", box).map((m) => [m.dataset.m, m]));
  const orig = flags.map((f) => [f, $("b", f).textContent, $("use", f).getAttribute("href"), marks[f.dataset.flag].textContent]);
  const off = new Set(), fixed = new Set();
  let signed = false, blockers = 0;
  const n = (k, w) => `${k} ${w}${k === 1 ? "" : "s"}`;

  const render = () => {
    let warnings = 0, missed = 0;
    blockers = 0;
    flags.forEach((f) => {
      const id = f.dataset.flag, done = fixed.has(id), live = !off.has(id) && !done;
      f.hidden = off.has(id);
      f.classList.toggle("ok", done);
      marks[id].className = done ? "ok" : live ? "bad" : "";
      if (live) f.dataset.sev === "blocker" ? blockers++ : warnings++;
      if (off.has(id) && !done) missed++;
    });
    toggles.forEach((t) => t.setAttribute("aria-pressed", String(!off.has(t.dataset.toggle))));
    sign.classList.toggle("ready", !blockers && !signed);
    sign.classList.toggle("signed", signed);
    box.classList.toggle("reviewed", signed);
    $("span", sign).textContent = signed ? "Signed" : "Sign report";
    tag.textContent = signed ? "Reviewed by radiologist" : "AI draft, requires review";
    const gap = missed ? ` ${n(missed, "issue")} unflagged with rules off.` : "";
    note.className = "rp-note" + (signed ? " good" : blockers ? " warn" : "");
    note.textContent = signed
      ? "Signed by the radiologist. RadioPad never signs." + gap
      : blockers
        ? `${n(blockers, "blocker")} open. Signing is held until resolved.`
        : (warnings ? `${n(warnings, "warning")}. The radiologist decides.` : "No open flags. Ready to sign.") + gap;
  };

  flags.forEach((f) =>
    $("[data-fix]", f).addEventListener("click", (e) => {
      const id = f.dataset.flag;
      fixed.add(id);
      marks[id].textContent = e.currentTarget.dataset.fix;
      $("b", f).textContent = "Resolved";
      $("use", f).setAttribute("href", "#i-okc");
      signed = false;
      render();
    })
  );
  toggles.forEach((t) =>
    t.addEventListener("click", () => {
      const id = t.dataset.toggle;
      off.has(id) ? off.delete(id) : off.add(id);
      signed = false;
      render();
    })
  );
  sign.addEventListener("click", () => {
    if (signed) return;
    if (blockers) {
      if (!reduce) { sign.classList.remove("shake"); void sign.offsetWidth; sign.classList.add("shake"); }
      return;
    }
    signed = true;
    render();
  });
  $("[data-reset]", box).addEventListener("click", () => {
    off.clear(); fixed.clear(); signed = false;
    orig.forEach(([f, b, href, text]) => { $("b", f).textContent = b; $("use", f).setAttribute("href", href); marks[f.dataset.flag].textContent = text; });
    render();
  });
  render();
});
