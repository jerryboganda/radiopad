// Rulebook library on /product/validation/: filter the build-time list by modality and search text.
import { $, $$ } from "./site.js";

const box = $("#rbLib");
if (box) {
  const q = $("#rbQ", box), btns = $$(".rb-f", box), rows = $$("#rbList li", box);
  const count = $("#rbCount", box), empty = $("#rbEmpty", box);
  let mod = "";
  const apply = () => {
    const terms = q.value.trim().toLowerCase().split(/\s+/).filter(Boolean);
    let n = 0;
    rows.forEach((r) => {
      const show = (!mod || r.dataset.mod === mod) && terms.every((t) => r.dataset.q.includes(t));
      r.hidden = !show;
      if (show) n++;
    });
    count.textContent = `Showing ${n} of ${rows.length} rulebooks`;
    empty.hidden = n > 0;
  };
  btns.forEach((b) =>
    b.addEventListener("click", () => {
      mod = b.dataset.mod;
      btns.forEach((x) => x.setAttribute("aria-pressed", String(x === b)));
      apply();
    })
  );
  q.addEventListener("input", apply);
  q.addEventListener("keydown", (e) => { if (e.key === "Escape" && q.value) { q.value = ""; apply(); } });
}
