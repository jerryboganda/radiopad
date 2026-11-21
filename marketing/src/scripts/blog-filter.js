// Blog tag filter. The pills link to /blog/tag/<tag>/; with JS they filter the grid in place and the
// URL follows with replaceState (both URLs render the same view, so reload and sharing still work).
import { ScrollTrigger } from "gsap/ScrollTrigger";
import { $, $$ } from "./site.js";

const bar = $("[data-filter]");
if (bar) {
  const pills = $$("a[data-tag]", bar);
  const cards = $$(".bl-grid > [data-tags]");
  const feat = $("#featured"); // index only: the featured post shown above the grid
  const count = $("#blCount");
  bar.addEventListener("click", (e) => {
    const a = e.target.closest("a[data-tag]");
    if (!a || e.button || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    const tag = a.dataset.tag;
    let n = 0;
    cards.forEach((c) => {
      const show = tag ? c.dataset.tags.split("|").includes(tag) : !(feat && "featured" in c.dataset);
      c.hidden = !show;
      if (show) n++;
    });
    if (feat) feat.hidden = Boolean(tag);
    pills.forEach((p) => (p === a ? p.setAttribute("aria-current", "page") : p.removeAttribute("aria-current")));
    count.textContent = tag ? `${n} ${n === 1 ? "post" : "posts"} tagged ${tag}` : `${cards.length} posts`;
    history.replaceState(null, "", a.href);
    ScrollTrigger.refresh(); // page height changed: re-measure the reveal triggers below the grid
  });
}
