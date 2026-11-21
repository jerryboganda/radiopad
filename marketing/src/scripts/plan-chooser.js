// "Which plan fits" chooser on /pricing/: three radio questions, the most demanding answer picks the tier.
import { $, $$ } from "./site.js";

const TIERS = [
  { name: "Radiologist", note: "Free trial on your own Windows workstation.", cta: "Download for Windows", href: "/download/" },
  { name: "Team", note: "Per seat. Talk to us for seat pricing.", cta: "Talk to us", href: "/demo/" },
  { name: "Enterprise", note: "Custom, scoped with your clinical, IT and governance teams.", cta: "Book a demo", href: "/demo/" },
];

const form = $("#planChooser");
if (form) {
  const cards = $$("#pricing .tier");
  const name = $("#planName"), note = $("#planNote"), why = $("#planWhy"), cta = $("#planCta"), prog = $("#planProgress");
  const total = $$("fieldset", form).length;

  form.addEventListener("submit", (e) => e.preventDefault());
  form.addEventListener("change", () => {
    const picked = $$("input:checked", form);
    prog.textContent = `${picked.length} of ${total} answered`;
    if (picked.length < total) return;
    const tier = Math.max(...picked.map((i) => Number(i.value)));
    const t = TIERS[tier];
    name.textContent = t.name;
    note.textContent = t.note;
    why.replaceChildren(
      ...picked.filter((i) => Number(i.value) === tier).map((i) => {
        const li = document.createElement("li");
        li.innerHTML = '<svg class="i" aria-hidden="true"><use href="#i-check"/></svg>';
        li.append(i.dataset.why);
        return li;
      })
    );
    cta.textContent = t.cta;
    cta.href = t.href;
    cta.hidden = false;
    prog.classList.add("chip-ok");
    cards.forEach((c, i) => c.classList.toggle("pick", i === tier));
  });
}
