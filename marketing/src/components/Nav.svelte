<script>
  import { onMount } from "svelte";

  // `current` is the label of the active top-level nav item (e.g. "Blog").
  let { nav = [], cta = { label: "Book a demo", href: "/#demo" }, current = "" } = $props();

  let open = $state(false);
  let scrolled = $state(false);

  onMount(() => {
    const onScroll = () => (scrolled = window.scrollY > 8);
    onScroll();
    window.addEventListener("scroll", onScroll, { passive: true });
    return () => window.removeEventListener("scroll", onScroll);
  });
</script>

<header class="nav" class:scrolled>
  <div class="wrap">
    <a class="brand" href="/" aria-label="RadioPad home">
      <span class="mark" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="2.4" stroke-linecap="round"><path d="M4 12h3l2 5 4-12 2 7h5" /></svg>
      </span>
      RadioPad
    </a>
    <nav aria-label="Primary">
      <ul class="nav-links">
        {#each nav as item}
          <li><a href={item.href} aria-current={current === item.label ? "page" : undefined}>{item.label}</a></li>
        {/each}
      </ul>
    </nav>
    <div class="nav-right">
      <a class="btn btn-primary" href={cta.href}>{cta.label}</a>
      <button
        class="nav-toggle"
        aria-label="Toggle menu"
        aria-expanded={open ? "true" : "false"}
        onclick={() => (open = !open)}
      >
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"><path d="M4 7h16M4 12h16M4 17h16" /></svg>
      </button>
    </div>
  </div>
</header>

<div class="mobile-menu" class:open>
  <ul>
    {#each nav as item}
      <li><a href={item.href}>{item.label}</a></li>
    {/each}
    <li><a class="btn btn-primary" href={cta.href}>{cta.label}</a></li>
  </ul>
</div>
