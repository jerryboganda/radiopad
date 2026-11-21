// /demo/ request form. Validates inline, then either POSTs JSON to PUBLIC_DEMO_ENDPOINT (success only on 2xx)
// or, with no endpoint configured, opens a prefilled mailto: to site.email. It never reports a send that did not happen.
import { $, $$ } from "./site.js";
import { site } from "../lib/site";

const form = $("#demoForm");
if (form) {
  const endpoint = import.meta.env.PUBLIC_DEMO_ENDPOINT;
  const emailRe = /^[^\s@]+@[^\s@.]+(\.[^\s@.]+)+$/;
  const rules = {
    name: (v) => (v ? "" : "Enter your name."),
    email: (v) => (!v ? "Enter your work email." : emailRe.test(v) ? "" : "Enter a valid email address, like name@hospital.org."),
    organization: (v) => (v ? "" : "Enter your organization."),
    role: (v) => (v ? "" : "Choose your role."),
  };
  const el = (n) => form.elements.namedItem(n);
  const val = (n) => el(n).value.trim();
  const alertBox = $("#df-alert"), mailtoBox = $("#df-mailto"), success = $("#df-success");
  const btn = $("#df-submit"), btnL = $("#df-submit-l");

  const check = (n) => {
    const msg = rules[n](val(n)), input = el(n), err = document.getElementById(`df-${n}-err`);
    err.textContent = msg;
    err.hidden = !msg;
    input.setAttribute("aria-invalid", String(!!msg));
    if (msg) input.setAttribute("aria-describedby", err.id);
    else input.removeAttribute("aria-describedby");
    return !msg;
  };
  Object.keys(rules).forEach((n) => {
    const input = el(n);
    const touched = () => input.getAttribute("aria-invalid") === "true";
    input.addEventListener("blur", () => { if (val(n) || touched()) check(n); });
    input.addEventListener(input.tagName === "SELECT" ? "change" : "input", () => { if (touched()) check(n); });
  });

  const showAlert = (text) => {
    alertBox.innerHTML = '<svg class="i" aria-hidden="true"><use href="#i-alert"/></svg><div></div>';
    const div = alertBox.lastElementChild;
    div.append(text, " Try again, or email ");
    const addr = document.createElement("span");
    addr.className = "addr";
    addr.textContent = site.email;
    div.append(addr, ".");
    alertBox.hidden = false;
  };

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    alertBox.hidden = true;
    mailtoBox.hidden = true;
    const bad = Object.keys(rules).filter((n) => !check(n));
    if (bad.length) { el(bad[0]).focus(); return; }

    const data = {
      name: val("name"),
      email: val("email"),
      organization: val("organization"),
      role: val("role"),
      modalities: $$('input[name="modalities"]:checked', form).map((i) => i.value),
      message: val("message"),
    };

    if (!endpoint) {
      const body = [
        `Name: ${data.name}`,
        `Work email: ${data.email}`,
        `Organization: ${data.organization}`,
        `Role: ${data.role}`,
        `Modalities: ${data.modalities.join(", ") || "Not specified"}`,
        "",
        data.message,
      ].join("\r\n");
      // ponytail: long mailto URLs can be cut by some mail clients; the message field is capped at 1500 chars for that reason.
      location.href = `mailto:${site.email}?subject=${encodeURIComponent(`Demo request: ${data.organization}`)}&body=${encodeURIComponent(body)}`;
      mailtoBox.hidden = false;
      return;
    }

    btn.disabled = true;
    btnL.textContent = "Sending";
    try {
      const r = await fetch(endpoint, {
        method: "POST",
        headers: { "Content-Type": "application/json", Accept: "application/json" },
        body: JSON.stringify(data),
      });
      if (!r.ok) throw new Error(`HTTP ${r.status}`);
      $("#df-success-email").textContent = data.email;
      form.hidden = true;
      success.hidden = false;
      success.focus();
    } catch {
      showAlert("Your request was not sent.");
    } finally {
      btn.disabled = false;
      btnL.textContent = "Request a demo";
    }
  });
}
