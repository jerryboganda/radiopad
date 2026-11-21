// "On this page" scroll-spy for legal pages and blog posts: marks the link of the section being read.
// Markup: <nav data-toc> with links to heading ids. Smooth anchor scrolling is handled by site.js.
// No motion here, so nothing to gate on reduced motion (the CSS transitions are cut globally).
import { $$ } from "./site.js";

$$("[data-toc]").forEach((toc) => {
  const pairs = $$('a[href^="#"]', toc)
    .map((a) => [a, document.getElementById(decodeURIComponent(a.hash.slice(1)))])
    .filter(([, h]) => h);
  if (!pairs.length) return;
  let cur = null, raf = 0;
  const update = () => {
    raf = 0;
    const line = innerHeight * 0.3; // the "reading line": the last heading above it is the current section
    let i = 0;
    pairs.forEach(([, h], j) => { if (h.getBoundingClientRect().top <= line) i = j; });
    const a = pairs[i][0];
    if (a === cur) return;
    cur?.removeAttribute("aria-current");
    a.setAttribute("aria-current", "true");
    cur = a;
  };
  const queue = () => { if (!raf) raf = requestAnimationFrame(update); };
  addEventListener("scroll", queue, { passive: true });
  addEventListener("resize", queue);
  update();
});
