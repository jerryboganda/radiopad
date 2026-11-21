// /download/: fill in the latest GitHub release (version, date, direct .msi and .apk links).
// The markup already links to the releases page, so loading or any failure leaves a working link.
import { $ } from "./site.js";

const box = $("#dl");
if (box) {
  const set = (id, text) => { const el = document.getElementById(id); if (el) el.textContent = text; };
  const mb = (bytes) => `${Math.round(bytes / 1048576)} MB`;
  // Only follow asset links that point at GitHub over https (the API response is outside our control).
  const safe = (u) => { try { const x = new URL(u); return x.protocol === "https:" && (x.hostname === "github.com" || x.hostname.endsWith(".github.com")) ? x.href : null; } catch { return null; } };

  const fail = () => {
    set("dl-chip", "Release page");
    set("dl-ver", "See the release page");
    set("dl-date", "See the release page");
    set("apk-chip", "Release page");
    const st = $("#dl-status");
    st.textContent = "Could not load the release details from GitHub. The button opens the latest release page, where the .msi installer is listed.";
    st.classList.add("bad");
  };

  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), 10000);
  fetch(box.dataset.api, { headers: { Accept: "application/vnd.github+json" }, signal: ctrl.signal })
    .then((r) => (r.ok ? r.json() : Promise.reject(new Error(String(r.status)))))
    .then((rel) => {
      clearTimeout(timer);
      const assets = Array.isArray(rel.assets) ? rel.assets : [];
      const msi = assets.find((a) => /\.msi$/i.test(a.name || ""));
      const apk = assets.find((a) => /\.apk$/i.test(a.name || ""));
      const msiUrl = msi && safe(msi.browser_download_url);
      if (!msiUrl) return fail();

      const ver = String(rel.tag_name || rel.name || "").trim();
      const date = rel.published_at ? new Date(rel.published_at) : null;
      set("dl-chip", "Latest");
      $("#dl-chip").classList.add("chip-ok");
      set("dl-ver", ver || "Latest");
      set("dl-date", date && !isNaN(date) ? date.toLocaleDateString(undefined, { year: "numeric", month: "long", day: "numeric" }) : "See the release page");
      set("dl-file", msi.size ? `${msi.name} (${mb(msi.size)})` : msi.name);
      $("#dl-btn").href = msiUrl;
      set("dl-btn-l", `Download ${ver ? ver + " " : ""}.msi`);
      set("dl-status", "Direct download from the latest GitHub release. Windows may ask you to confirm before the installer runs.");

      const apkUrl = apk && safe(apk.browser_download_url);
      if (apkUrl) {
        set("apk-chip", "Android build");
        $("#apk-chip").classList.add("chip-ok");
        set("apk-title", "Android companion");
        set("apk-text", `${apk.name}${apk.size ? `, ${mb(apk.size)}` : ""}, from release ${ver || "latest"}. Install it on your phone, then pair it to the desktop app.`);
        $("#apk-btn").href = apkUrl;
        set("apk-btn-l", "Download the Android .apk");
      } else {
        set("apk-chip", "Release page");
      }
    })
    .catch(() => { clearTimeout(timer); fail(); });
}
