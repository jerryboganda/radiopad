// Pairing demo on /product/dictation/: type (or auto-type) the desktop code on the phone,
// pair, then stream a transcript into the focused Findings section. Nothing is sent anywhere.
import { $, reduce } from "./site.js";

const box = $("#pairDemo");
if (box) {
  const CODE = box.dataset.code, WORDS = box.dataset.text.split(" ");
  const input = $("#pdIn", box), pairBtn = $("#pdPair", box), err = $("#pdErr", box);
  const play = $("#pdPlay", box), playLbl = $("span", play), status = $("#pdStatus", box);
  const chip = $("#pdChip", box), text = $("#pdText", box);
  const ALPHA = /[^ABCDEFGHJKLMNPQRSTUVWXYZ2-9]/g; // the real pairing alphabet
  let timers = [], touched = false;
  const T = (fn, ms) => timers.push(setTimeout(fn, reduce ? 0 : ms));
  const stop = () => { timers.forEach(clearTimeout); timers = []; };
  const phase = (p) => { box.dataset.phase = p; };
  const setChip = (cls, label) => { chip.className = `chip ${cls}`; chip.innerHTML = cls === "chip-live" ? `<i></i>${label}` : label; };
  const placeholder = () => { text.innerHTML = '<span class="pd-ph">Dictation appears here</span>'; };
  const clearErr = () => { err.hidden = true; input.removeAttribute("aria-invalid"); };

  const reset = () => {
    stop(); phase("pair"); input.value = ""; clearErr(); placeholder();
    setChip("chip-draft", "Waiting for phone");
  };
  const finish = () => {
    phase("done"); setChip("chip-ok", "Phone paired");
    status.textContent = "Done. The phone only sent voice. Review, validate and sign on the desktop.";
    playLbl.textContent = "Replay";
  };
  const stream = () => {
    phase("live"); setChip("chip-live", "Receiving dictation");
    status.textContent = "Paired. Your voice is streaming into Findings on the desktop.";
    if (reduce) { text.textContent = WORDS.join(" "); finish(); return; }
    text.textContent = "";
    let i = 0;
    const step = () => {
      if (i >= WORDS.length) return T(finish, 700);
      const w = document.createElement("span");
      w.className = "pd-w";
      w.textContent = (i ? " " : "") + WORDS[i];
      text.append(w);
      T(step, /[.,]$/.test(WORDS[i++]) ? 420 : 160);
    };
    T(step, 500);
  };
  const pair = () => {
    clearErr(); phase("link");
    status.textContent = "Code accepted. Connecting through the relay.";
    if (document.activeElement === input) play.focus(); // the phone input is about to be hidden
    T(() => { setChip("chip-ok", "Phone paired"); T(stream, 500); }, 700);
  };
  const tryPair = () => {
    if (box.dataset.phase !== "pair") return;
    if (input.value === CODE) return pair();
    err.hidden = false;
    input.setAttribute("aria-invalid", "true");
    if (!reduce) { input.classList.remove("shake"); void input.offsetWidth; input.classList.add("shake"); }
  };
  const auto = () => {
    reset();
    status.textContent = "Entering the code on the phone.";
    [...CODE].forEach((ch, k) => T(() => { input.value += ch; }, 350 + k * 150));
    T(pair, 350 + CODE.length * 150 + 300);
  };

  input.addEventListener("focus", () => { touched = true; if (box.dataset.phase === "pair") stop(); });
  input.addEventListener("input", () => {
    input.value = input.value.toUpperCase().replace(ALPHA, "").slice(0, CODE.length);
    clearErr();
    if (input.value.length === CODE.length) tryPair();
  });
  input.addEventListener("keydown", (e) => { if (e.key === "Enter") tryPair(); });
  pairBtn.addEventListener("click", tryPair);
  play.addEventListener("click", () => { touched = true; auto(); });

  // play once on its own the first time the demo is mostly on screen, unless the visitor got there first
  new IntersectionObserver((entries, io) => {
    if (!entries[0].isIntersecting) return;
    io.disconnect();
    setTimeout(() => { if (!touched) auto(); }, reduce ? 0 : 900);
  }, { threshold: 0.45 }).observe(box);
}
