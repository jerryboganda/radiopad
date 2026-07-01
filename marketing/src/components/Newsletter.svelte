<script>
  // Newsletter signup island. States: idle / submitting / success / error.
  // endpoint: set to your email provider handler; when empty the submit is simulated.
  let { endpoint = "" } = $props();

  let email = $state("");
  let error = $state("");
  let status = $state("idle"); // idle | submitting | success | error

  const emailRe = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

  async function onSubmit(ev) {
    ev.preventDefault();
    if (!email.trim() || !emailRe.test(email.trim())) {
      error = "Enter a valid email address.";
      document.getElementById("nl-email")?.focus();
      return;
    }
    error = "";
    status = "submitting";
    try {
      if (endpoint) {
        const r = await fetch(endpoint, {
          method: "POST",
          headers: { "Content-Type": "application/json", Accept: "application/json" },
          body: JSON.stringify({ email }),
        });
        if (!r.ok) throw new Error("bad status");
      } else {
        await new Promise((res) => setTimeout(res, 800));
      }
      status = "success";
    } catch {
      status = "error";
      error = "Something went wrong. Please try again.";
    }
  }
</script>

{#if status === "success"}
  <p class="form-success" role="status" style="padding:.9rem 1.1rem">
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round"><path d="M5 13l4 4L19 7" /></svg>
    You are subscribed. Look out for the next note.
  </p>
{:else}
  <form class="sub-form" novalidate onsubmit={onSubmit}>
    <label for="nl-email">Email address</label>
    <input id="nl-email" type="email" placeholder="you@hospital.org" autocomplete="email" bind:value={email}
      aria-invalid={error ? "true" : "false"} aria-describedby={error ? "nl-err" : undefined} />
    <button class="btn btn-primary" type="submit" disabled={status === "submitting"}>
      {#if status === "submitting"}<span class="spinner" aria-hidden="true"></span>{:else}Subscribe{/if}
    </button>
    {#if error}<span class="err" id="nl-err" role="alert" style="flex-basis:100%">{error}</span>{/if}
  </form>
{/if}
