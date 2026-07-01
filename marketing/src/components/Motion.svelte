<script>
  import { onMount } from "svelte";

  // Headless motion controller. Renders nothing; wires page motion on mount.
  // mode: "home" | "post" | "index".
  // GSAP is dynamically imported ONLY for home/post, so the blog index never ships it.
  let { mode = "home" } = $props();

  onMount(() => {
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const cleanups = [];

    // --- reveals (all modes, no GSAP) ---
    const revObs = new IntersectionObserver(
      (entries) => {
        entries.forEach((e) => {
          if (e.isIntersecting) {
            e.target.classList.add("in");
            revObs.unobserve(e.target);
          }
        });
      },
      { threshold: 0.15, rootMargin: "0px 0px -8% 0px" }
    );
    document.querySelectorAll(".reveal").forEach((el) => revObs.observe(el));
    cleanups.push(() => revObs.disconnect());

    // --- count-up (home, no GSAP) ---
    if (mode === "home") {
      const cObs = new IntersectionObserver(
        (entries) => {
          entries.forEach((e) => {
            if (!e.isIntersecting) return;
            const el = e.target;
            const target = parseFloat(el.dataset.count);
            const suffix = el.dataset.suffix || "";
            const dec = target % 1 !== 0 ? 1 : 0;
            if (reduce) {
              el.textContent = target.toFixed(dec) + suffix;
            } else {
              const start = performance.now();
              const dur = 1300;
              const tick = (now) => {
                const p = Math.min((now - start) / dur, 1);
                const eased = 1 - Math.pow(1 - p, 3);
                el.textContent = (target * eased).toFixed(dec) + suffix;
                if (p < 1) requestAnimationFrame(tick);
                else el.textContent = target.toFixed(dec) + suffix;
              };
              requestAnimationFrame(tick);
            }
            cObs.unobserve(el);
          });
        },
        { threshold: 0.6 }
      );
      document.querySelectorAll("[data-count]").forEach((c) => cObs.observe(c));
      cleanups.push(() => cObs.disconnect());
    }

    // --- TOC active state (post, no GSAP) ---
    if (mode === "post") {
      const links = Array.from(document.querySelectorAll(".toc a"));
      const map = {};
      links.forEach((a) => (map[a.getAttribute("href").slice(1)] = a));
      const hObs = new IntersectionObserver(
        (entries) => {
          entries.forEach((e) => {
            if (e.isIntersecting) {
              links.forEach((l) => l.classList.remove("active"));
              if (map[e.target.id]) map[e.target.id].classList.add("active");
            }
          });
        },
        { rootMargin: "-20% 0px -70% 0px" }
      );
      document.querySelectorAll(".prose h2[id]").forEach((h) => hObs.observe(h));
      cleanups.push(() => hObs.disconnect());
    }

    // --- GSAP layer (home/post only, dynamic import) ---
    if (!reduce && mode !== "index") {
      let mm;
      (async () => {
        const { gsap } = await import("gsap");
        const { ScrollTrigger } = await import("gsap/ScrollTrigger");
        gsap.registerPlugin(ScrollTrigger);
        mm = gsap.matchMedia();

        mm.add("(prefers-reduced-motion: no-preference)", () => {
          if (mode === "home") {
            // hero line-mask reveal (bolder: larger travel + slight rotate settle)
            gsap.set("#heroH1 .line > span", { yPercent: 120 });
            gsap.to("#heroH1 .line > span", {
              yPercent: 0, duration: 1.1, ease: "power4.out", stagger: 0.11, delay: 0.12,
            });
            gsap.from(".hero-copy .eyebrow, .hero .lead, .hero-cta, .hero-note", {
              y: 22, opacity: 0, duration: 0.85, ease: "power3.out", stagger: 0.09, delay: 0.5,
            });
            gsap.from(".preview-card", {
              y: 40, opacity: 0, scale: 0.96, duration: 1.1, ease: "power3.out", delay: 0.35,
            });

            // hero parallax (stronger)
            gsap.to("#previewCard", {
              yPercent: -12, ease: "none",
              scrollTrigger: { trigger: ".hero", start: "top top", end: "bottom top", scrub: true },
            });

            // magnetic primary CTA
            const mag = document.getElementById("magnetic");
            if (mag) {
              const xTo = gsap.quickTo(mag, "x", { duration: 0.4, ease: "power3" });
              const yTo = gsap.quickTo(mag, "y", { duration: 0.4, ease: "power3" });
              const move = (ev) => {
                const r = mag.getBoundingClientRect();
                xTo((ev.clientX - (r.left + r.width / 2)) * 0.4);
                yTo((ev.clientY - (r.top + r.height / 2)) * 0.55);
              };
              const leave = () => { xTo(0); yTo(0); };
              mag.addEventListener("mousemove", move);
              mag.addEventListener("mouseleave", leave);
              cleanups.push(() => { mag.removeEventListener("mousemove", move); mag.removeEventListener("mouseleave", leave); });
            }

            // sticky-stack: pinning + stacking are handled entirely by CSS position:sticky
            // with fully opaque cards. No GSAP transforms here, so cards never overlap or
            // show through each other. (Kept intentionally simple for robustness.)

            // validation micro-demo (loop red -> amber -> green emphasis)
            const vrows = gsap.utils.toArray("#vdemo .vrow");
            if (vrows.length) {
              gsap.set(vrows, { opacity: 0.35, x: -6 });
              const tl = gsap.timeline({
                scrollTrigger: { trigger: "#vdemo", start: "top 80%" },
                repeat: -1, repeatDelay: 0.6,
              });
              vrows.forEach((row) => {
                tl.to(row, { opacity: 1, x: 0, duration: 0.4, ease: "power2.out" }, "+=0.35")
                  .from(row.querySelector(".vstate"), { scale: 0.2, duration: 0.4, ease: "back.out(3)" }, "<");
              });
            }
          }

          if (mode === "post") {
            gsap.to("#progress", {
              scaleX: 1, ease: "none",
              scrollTrigger: { trigger: "article", start: "top top", end: "bottom bottom", scrub: 0.3 },
            });
            gsap.from(".post-header .inner > *", { y: 20, opacity: 0, duration: 0.7, ease: "power3.out", stagger: 0.08 });
            gsap.from(".cover-hero", { y: 26, opacity: 0, duration: 0.9, ease: "power3.out", delay: 0.15 });
          }
        });
      })();

      cleanups.push(() => mm && mm.revert());
    }

    return () => cleanups.forEach((fn) => fn());
  });
</script>
