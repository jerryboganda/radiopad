import type { Config } from 'tailwindcss';

/**
 * RadioPad — Hallmark design system (ported from UBAG).
 *
 * Warm "paper & ink" editorial palette in OKLCH. Light-only: `darkMode`
 * is kept as 'class' but RadioPad ships NO `.dark` palette (every token
 * is a warm-paper light value).
 *
 * NOTE on "Skeleton": UBAG's Hallmark imports ZERO `@skeletonlabs/skeleton`
 * Svelte components — it only pulled in `@skeletonlabs/tw-plugin` for a base
 * theme it didn't really use. That plugin emits `::file-selector-button`
 * selectors followed by combinators which Next 16's strict CSS parser
 * rejects (build break), and we use none of its classes/theme vars. So it
 * is intentionally NOT used here; the Hallmark look comes entirely from the
 * tokens in app/hallmark.css + these Tailwind color/font/radius scales.
 *
 * Canonical token source: frontend/app/hallmark.css (CSS custom properties)
 * + this file. See docs/02-design/design.md.
 */
export default {
  darkMode: 'class',
  content: [
    './app/**/*.{ts,tsx,js,jsx,mdx}',
    './components/**/*.{ts,tsx,js,jsx}',
    './lib/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        paper: 'oklch(96.5% 0.012 75)',
        'paper-soft': 'oklch(99% 0.006 75)',
        'paper-warm': 'oklch(93% 0.02 70)',
        ink: 'oklch(20% 0.022 55)',
        'ink-soft': 'oklch(38% 0.018 55)',
        'ink-mute': 'oklch(50% 0.012 60)',
        rule: 'oklch(86% 0.014 70)',
        'rule-soft': 'oklch(91% 0.01 70)',
        accent: 'oklch(58% 0.18 35)',
        'accent-deep': 'oklch(42% 0.2 32)',
        'accent-soft': 'oklch(82% 0.08 45)',
        saffron: 'oklch(78% 0.16 78)',
        'saffron-soft': 'oklch(91% 0.07 80)',
        marine: 'oklch(34% 0.09 240)',
        'marine-soft': 'oklch(83% 0.045 240)',
        success: 'oklch(50% 0.09 150)',
        'success-soft': 'oklch(90% 0.04 145)',
        danger: 'oklch(52% 0.17 25)',
        'danger-soft': 'oklch(89% 0.055 32)',
        'focus-ring': 'oklch(48% 0.2 32)',
        // RadioPad-specific: AI-generated text marker (must stay distinct
        // from info/marine — a clinical-safety affordance).
        ai: 'oklch(45% 0.14 300)',
        'ai-soft': 'oklch(90% 0.05 300)',
      },
      fontFamily: {
        display: ['ui-rounded', 'Aptos Display', 'Segoe UI', 'system-ui', 'sans-serif'],
        body: ['Aptos', 'Segoe UI', 'BlinkMacSystemFont', 'sans-serif'],
        mono: ['Cascadia Mono', 'SFMono-Regular', 'Consolas', 'ui-monospace', 'monospace'],
        // RadioPad clinical report prose keeps an editorial serif stack.
        serif: ['Source Serif Pro', 'Source Serif 4', 'Iowan Old Style', 'Georgia', 'serif'],
      },
      borderRadius: {
        sm: '4px',
        md: '8px',
        lg: '8px',
        pill: '999px',
      },
      // ---- Motion utilities (mirror of the CSS motion tokens in
      // app/hallmark.css + app/motion.css). Lets utility classes compose
      // motion alongside the named .rp-anim-* classes. ----
      transitionDuration: {
        fast: '120ms',
        base: '180ms',
        slow: '260ms',
      },
      transitionTimingFunction: {
        out: 'cubic-bezier(0.16, 1, 0.3, 1)',
        in: 'cubic-bezier(0.7, 0, 0.84, 0)',
        'in-out': 'cubic-bezier(0.65, 0, 0.35, 1)',
        pop: 'cubic-bezier(0.21, 1.02, 0.73, 1)',
        snap: 'cubic-bezier(0.2, 0, 0.2, 1)',
        spring: 'cubic-bezier(0.175, 0.885, 0.32, 1.275)',
        overshoot: 'cubic-bezier(0.34, 1.56, 0.64, 1)',
      },
      transitionDelay: {
        1: '40ms',
        2: '80ms',
        3: '120ms',
        4: '160ms',
        5: '200ms',
        6: '240ms',
        7: '280ms',
        8: '320ms',
      },
      keyframes: {
        'rp-fade-in': { from: { opacity: '0' }, to: { opacity: '1' } },
        'rp-fade-in-up': {
          from: { opacity: '0', transform: 'translateY(10px)' },
          to: { opacity: '1', transform: 'translateY(0)' },
        },
        'rp-scale-in': {
          from: { opacity: '0', transform: 'scale(0.96)' },
          to: { opacity: '1', transform: 'scale(1)' },
        },
        'rp-pop-in': {
          '0%': { opacity: '0', transform: 'translateY(6px) scale(0.98)' },
          '100%': { opacity: '1', transform: 'translateY(0) scale(1)' },
        },
        'rp-pulse-soft': {
          '0%, 100%': { opacity: '1' },
          '50%': { opacity: '0.55' },
        },
        'rp-rotate': { to: { transform: 'rotate(360deg)' } },
      },
      animation: {
        'fade-in': 'rp-fade-in 260ms cubic-bezier(0.16, 1, 0.3, 1) both',
        'fade-in-up': 'rp-fade-in-up 260ms cubic-bezier(0.16, 1, 0.3, 1) both',
        'scale-in': 'rp-scale-in 260ms cubic-bezier(0.21, 1.02, 0.73, 1) both',
        'pop-in': 'rp-pop-in 260ms cubic-bezier(0.21, 1.02, 0.73, 1) both',
        'spring-in': 'rp-pop-in 420ms cubic-bezier(0.175, 0.885, 0.32, 1.275) both',
        'pulse-soft': 'rp-pulse-soft 1.6s cubic-bezier(0.65, 0, 0.35, 1) infinite',
        'spin-slow': 'rp-rotate 700ms linear infinite',
      },
    },
  },
  plugins: [],
} satisfies Config;
