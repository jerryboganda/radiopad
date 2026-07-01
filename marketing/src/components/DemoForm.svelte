<script>
  // Demo-request form island. Full states: idle / submitting / success / error.
  // endpoint: set to your form handler (Formspree, an API route, etc.).
  // When empty, the submit is simulated so the flow is demonstrable without a backend.
  let { endpoint = "" } = $props();

  let fullName = $state("");
  let email = $state("");
  let organization = $state("");
  let message = $state("");
  let errors = $state({});
  let status = $state("idle"); // idle | submitting | success | error
  let serverError = $state("");

  const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  function validate() {
    const e = {};
    if (!fullName.trim()) e.fullName = "Please enter your name.";
    if (!email.trim()) e.email = "Please enter your email.";
    else if (!emailRe.test(email.trim())) e.email = "Enter a valid email address.";
    if (!organization.trim()) e.organization = "Please enter your organization.";
    errors = e;
    return Object.keys(e).length === 0;
  }

  async function onSubmit(ev) {
    ev.preventDefault();
    if (!validate()) {
      const first = Object.keys(errors)[0];
      document.getElementById(`df-${first}`)?.focus();
      return;
    }
    status = "submitting";
    serverError = "";
    try {
      if (endpoint) {
        const r = await fetch(endpoint, {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ fullName, email, organization, message }),
        });
        if (!r.ok) throw new Error("bad status");
      } else {
        await new Promise((res) => setTimeout(res, 900));
      }
      status = "success";
    } catch {
      status = "error";
      serverError = "Something went wrong. Please try again, or email hello@radiopad.example.";
    }
  }
</script>

{#if status === "success"}
  <div class="form-success" role="status">
    <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M5 13l4 4L19 7" /></svg>
    <div>
      <strong>Request received.</strong>
      <p style="margin-top:.3rem">Thanks. We will be in touch within one business day to schedule your demo.</p>
    </div>
  </div>
{:else}
  <h3>Request a demo</h3>
  <form class="form" novalidate onsubmit={onSubmit}>
    <div class="field">
      <label for="df-fullName">Full name</label>
      <input id="df-fullName" name="fullName" type="text" autocomplete="name" bind:value={fullName}
        aria-invalid={errors.fullName ? "true" : "false"} aria-describedby={errors.fullName ? "df-fullName-err" : undefined} />
      {#if errors.fullName}<span class="err" id="df-fullName-err">{errors.fullName}</span>{/if}
    </div>
    <div class="field">
      <label for="df-email">Work email</label>
      <input id="df-email" name="email" type="email" autocomplete="email" placeholder="you@hospital.org" bind:value={email}
        aria-invalid={errors.email ? "true" : "false"} aria-describedby={errors.email ? "df-email-err" : undefined} />
      {#if errors.email}<span class="err" id="df-email-err">{errors.email}</span>{/if}
    </div>
    <div class="field">
      <label for="df-organization">Organization</label>
      <input id="df-organization" name="organization" type="text" autocomplete="organization" bind:value={organization}
        aria-invalid={errors.organization ? "true" : "false"} aria-describedby={errors.organization ? "df-organization-err" : undefined} />
      {#if errors.organization}<span class="err" id="df-organization-err">{errors.organization}</span>{/if}
    </div>
    <div class="field">
      <label for="df-message">What would you like to see? <span class="hint">(optional)</span></label>
      <textarea id="df-message" name="message" bind:value={message}></textarea>
    </div>
    {#if status === "error"}<p class="form-error-banner" role="alert">{serverError}</p>{/if}
    <div class="form-actions">
      <button class="btn btn-primary btn-lg" type="submit" disabled={status === "submitting"} style="width:100%">
        {#if status === "submitting"}<span class="spinner" aria-hidden="true"></span> Sending...{:else}Book a demo{/if}
      </button>
    </div>
    <p class="hint">No commitment. We reply within one business day.</p>
  </form>
{/if}
