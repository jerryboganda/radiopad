// Site-wide behaviour: smooth scroll, nav menus, anchors, magnetic buttons, card spotlight,
// tabs, copy buttons, FAQ accordions, scroll reveals, split headings, pinned workflow.
// Every block is guarded so a page only pays for the markup it actually has.
import { gsap } from "gsap";
import { ScrollTrigger } from "gsap/ScrollTrigger";
import { SplitText } from "gsap/SplitText";
import Lenis from "../vendor/lenis.mjs";

gsap.registerPlugin(ScrollTrigger, SplitText);

export const $ = (s, r = document) => r.querySelector(s);
export const $$ = (s, r = document) => [...r.querySelectorAll(s)];
export const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
export const fine = matchMedia("(hover: hover) and (pointer: fine)").matches;

/* smooth scroll */
let lenis = null;
if (!reduce) {
  lenis = new Lenis({ lerp: 0.1 });
  lenis.on("scroll", ScrollTrigger.update);
  gsap.ticker.add((t) => lenis.raf(t * 1000));
  gsap.ticker.lagSmoothing(0);
}

/* nav */
const nav = $("#nav");
const onScroll = () => nav.classList.toggle("scrolled", scrollY > 8);
addEventListener("scroll", onScroll, { passive: true });
onScroll();
const megas = $$(".has-mega");
const closeMenus = (except) =>
  megas.forEach((li) => {
    if (li !== except) { li.classList.remove("open"); li.querySelector("button").setAttribute("aria-expanded", "false"); }
  });
megas.forEach((li) => {
  const b = li.querySelector("button");
  b.addEventListener("click", () => { const o = !li.classList.contains("open"); closeMenus(li); li.classList.toggle("open", o); b.setAttribute("aria-expanded", String(o)); });
  li.addEventListener("focusout", (e) => { if (!li.contains(e.relatedTarget)) { li.classList.remove("open"); b.setAttribute("aria-expanded", "false"); } });
});
document.addEventListener("keydown", (e) => {
  if (e.key !== "Escape") return;
  const o = megas.find((l) => l.classList.contains("open"));
  closeMenus();
  if (o) o.querySelector("button").focus();
});
document.addEventListener("click", (e) => { if (!e.target.closest(".has-mega")) closeMenus(); });
const burger = $("#burger"), mnav = $("#mnav");
burger.addEventListener("click", () => {
  const o = mnav.hidden;
  mnav.hidden = !o;
  burger.setAttribute("aria-expanded", String(o));
  burger.setAttribute("aria-label", o ? "Close menu" : "Open menu");
});

/* same-page anchors scroll smoothly under the sticky nav */
$$('a[href*="#"]').forEach((a) =>
  a.addEventListener("click", (e) => {
    const url = new URL(a.href, location.href);
    if (url.pathname !== location.pathname || !url.hash) return;
    const el = document.getElementById(decodeURIComponent(url.hash.slice(1)));
    if (!el) return;
    e.preventDefault();
    closeMenus();
    mnav.hidden = true;
    burger.setAttribute("aria-expanded", "false");
    history.replaceState(null, "", url.hash);
    if (lenis) lenis.scrollTo(el, { offset: -72 });
    else el.scrollIntoView({ behavior: reduce ? "auto" : "smooth" });
  })
);

/* magnetic buttons */
if (fine && !reduce)
  $$(".mag").forEach((m) => {
    let tx = 0, ty = 0, x = 0, y = 0, raf = 0;
    const tick = () => {
      x += (tx - x) * 0.18; y += (ty - y) * 0.18;
      m.style.transform = `translate3d(${x.toFixed(2)}px,${y.toFixed(2)}px,0)`;
      raf = Math.abs(tx - x) + Math.abs(ty - y) > 0.05 ? requestAnimationFrame(tick) : 0;
    };
    m.addEventListener("pointermove", (e) => {
      const r = m.getBoundingClientRect();
      tx = Math.max(-8, Math.min(8, (e.clientX - r.left - r.width / 2) * 0.22));
      ty = Math.max(-6, Math.min(6, (e.clientY - r.top - r.height / 2) * 0.35));
      if (!raf) raf = requestAnimationFrame(tick);
    });
    m.addEventListener("pointerleave", () => { tx = 0; ty = 0; if (!raf) raf = requestAnimationFrame(tick); });
  });

/* card spotlight */
if (fine)
  $$(".card, .stage-w").forEach((c) =>
    c.addEventListener("pointermove", (e) => {
      const r = c.getBoundingClientRect();
      c.style.setProperty("--mx", `${e.clientX - r.left}px`);
      c.style.setProperty("--my", `${e.clientY - r.top}px`);
    })
  );

/* pill tabs: [data-tabs] > .tabs[role=tablist] > .tab + .tab-ind, panels via aria-controls */
$$("[data-tabs]").forEach((box) => {
  const tabs = $$("[role=tab]", box), ind = $(".tab-ind", box);
  const place = (t) => { ind.style.width = t.offsetWidth + "px"; ind.style.transform = `translate3d(${t.offsetLeft}px,${t.offsetTop - 4}px,0)`; };
  const select = (t, focus) => {
    tabs.forEach((x) => {
      const on = x === t;
      x.setAttribute("aria-selected", String(on));
      x.tabIndex = on ? 0 : -1;
      const p = document.getElementById(x.getAttribute("aria-controls"));
      p.hidden = !on;
      if (on && !reduce) { p.classList.remove("enter"); void p.offsetWidth; p.classList.add("enter"); }
    });
    place(t);
    if (focus) t.focus();
  };
  tabs.forEach((t, i) => {
    t.addEventListener("click", () => select(t));
    t.addEventListener("keydown", (e) => {
      const d = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
      if (d) { e.preventDefault(); select(tabs[(i + d + tabs.length) % tabs.length], true); }
    });
  });
  const cur = () => tabs.find((t) => t.getAttribute("aria-selected") === "true");
  place(cur());
  addEventListener("resize", () => place(cur()));
  if (document.fonts) document.fonts.ready.then(() => place(cur()));
});

/* copy buttons: <button class="copy" data-copy="text"> next to the code it copies */
$$(".copy[data-copy]").forEach((btn) =>
  btn.addEventListener("click", () => {
    const lbl = $("span", btn), code = btn.parentElement.querySelector("code");
    const ok = () => { btn.classList.add("done"); lbl.textContent = "Copied"; setTimeout(() => { btn.classList.remove("done"); lbl.textContent = "Copy"; }, 1600); };
    const fallback = () => {
      if (!code) return;
      const r = document.createRange(); r.selectNodeContents(code);
      const s = getSelection(); s.removeAllRanges(); s.addRange(r);
      lbl.textContent = "Press Ctrl+C";
    };
    try { navigator.clipboard.writeText(btn.dataset.copy).then(ok, fallback); } catch { fallback(); }
  })
);

/* faq accordions */
$$(".qa button").forEach((b) =>
  b.addEventListener("click", () => {
    const qa = b.closest(".qa"), o = !qa.classList.contains("open");
    qa.classList.toggle("open", o);
    b.setAttribute("aria-expanded", String(o));
  })
);

/* scroll reveals, split headings, pinned workflow (desktop) */
if (!reduce) {
  const rev = $$("[data-reveal]");
  gsap.set(rev, { opacity: 0, y: 26 });
  ScrollTrigger.batch(rev, { start: "top 90%", once: true, onEnter: (b) => gsap.to(b, { opacity: 1, y: 0, duration: 0.9, ease: "expo.out", stagger: 0.06, overwrite: true }) });
  $$("[data-split]").forEach((el) =>
    SplitText.create(el, {
      type: "lines", mask: "lines", autoSplit: true,
      onSplit: (s) => gsap.from(s.lines, { yPercent: 110, duration: 1.05, ease: "expo.out", stagger: 0.08, scrollTrigger: { trigger: el, start: "top 88%", once: true } }),
    })
  );
  const stage = $("#flowStage");
  if (stage)
    gsap.matchMedia().add("(min-width: 1000px)", () => {
      stage.classList.add("pinned");
      const texts = $$(".fs-text", stage), vis = $$(".fs-vis", stage), rail = $("#railFill");
      let cur = -1;
      const set = (i) => {
        if (i === cur) return;
        cur = i;
        texts.forEach((t, j) => t.classList.toggle("active", j === i));
        vis.forEach((v, j) => v.classList.toggle("active", j === i));
      };
      set(0);
      ScrollTrigger.create({
        trigger: stage, start: "top top+=110", end: () => "+=" + Math.round(innerHeight * 2.2),
        pin: true, scrub: true, invalidateOnRefresh: true,
        onUpdate: (s) => { set(Math.min(texts.length - 1, Math.floor(s.progress * (texts.length - 0.001)))); rail.style.transform = `scaleY(${s.progress})`; },
      });
      return () => { stage.classList.remove("pinned"); texts.concat(vis).forEach((x) => x.classList.remove("active")); };
    });
  if (document.fonts) document.fonts.ready.then(() => ScrollTrigger.refresh());
}
