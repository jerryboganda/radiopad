// /changelog/: filter change groups by type (Added, Changed, Fixed, Removed...). Hides releases left empty.
import { $, $$ } from "./site.js";
import { ScrollTrigger } from "gsap/ScrollTrigger";

const bar = $("#clFilter");
if (bar) {
  const btns = $$("button[data-type]", bar), groups = $$(".grp[data-type]"), rels = $$(".rel"), count = $("#clCount");
  const items = (g) => g.querySelectorAll(":scope > ul > li").length;

  bar.addEventListener("click", (e) => {
    const b = e.target.closest("button[data-type]");
    if (!b) return;
    const type = b.dataset.type;
    btns.forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
    let n = 0;
    groups.forEach((g) => { const on = type === "all" || g.dataset.type === type; g.hidden = !on; if (on) n += items(g); });
    rels.forEach((r) => { r.hidden = !$$(".grp", r).some((g) => !g.hidden); });
    const label = type === "all" ? "all" : b.firstChild.textContent.trim().toLowerCase();
    count.textContent = type === "all" ? `Showing all ${n} changes` : `Showing ${n} ${label} ${n === 1 ? "change" : "changes"}`;
    ScrollTrigger.refresh(); // page height changed; keep reveal and pin triggers in place
  });
}
