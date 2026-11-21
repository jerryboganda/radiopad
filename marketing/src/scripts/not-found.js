// 404 page: crosshair and an "HU not found" readout follow the pointer over the empty viewer.
// Direct pointer tracking, no animation, so reduced motion needs no special case; fine pointers only.
import { $, fine } from "./site.js";

const v = $("#nfViewer");
if (v && fine) {
  const xh = $(".xh", v), hu = $(".hu", v);
  v.addEventListener("pointermove", (e) => {
    const r = v.getBoundingClientRect(), x = e.clientX - r.left, y = e.clientY - r.top;
    xh.style.setProperty("--x", x + "px");
    xh.style.setProperty("--y", y + "px");
    hu.hidden = false;
    hu.style.transform = `translate3d(${Math.min(x + 14, r.width - 130)}px,${Math.min(y + 14, r.height - 30)}px,0)`;
    v.classList.add("hover");
  });
  v.addEventListener("pointerleave", () => { v.classList.remove("hover"); hu.hidden = true; });
}
