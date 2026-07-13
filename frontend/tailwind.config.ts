import type { Config } from 'tailwindcss';

/**
 * RadioPad — RC design system (PRD v3.0 §20).
 *
 * Light-first white/blue clinical palette with a first-class deep-navy
 * dark theme. The canonical token source is frontend/app/tokens.css
 * (CSS custom properties, light + dark blocks); every Tailwind color
 * below points at those variables so there is a single source of truth
 * and utilities are automatically theme-aware.
 *
 * darkMode uses the html[data-theme="dark"] selector set by the theme
 * runtime (lib/theme.ts + the pre-paint bootstrap in app/layout.tsx).
 * See docs/02-design/design.md.
 */
export default {
  darkMode: ['selector', '[data-theme="dark"]'],
  content: [
    './app/**/*.{ts,tsx,js,jsx,mdx}',
    './components/**/*.{ts,tsx,js,jsx}',
    './lib/**/*.{ts,tsx}',
  ],
  theme: {
    extend: {
      colors: {
        // RC semantic scale (preferred names for new code)
        canvas: 'var(--color-canvas)',
        surface: 'var(--color-surface)',
        'surface-subtle': 'var(--color-surface-subtle)',
        'surface-muted': 'var(--color-surface-muted)',
        elevated: 'var(--color-elevated)',
        selected: 'var(--color-selected)',
        ink: 'var(--color-ink)',
        'ink-soft': 'var(--color-ink-soft)',
        'ink-mute': 'var(--color-ink-mute)',
        'ink-faint': 'var(--color-ink-faint)',
        rule: 'var(--color-rule)',
        'rule-soft': 'var(--color-rule-soft)',
        'rule-strong': 'var(--color-rule-strong)',
        accent: 'var(--color-accent)',
        'accent-deep': 'var(--color-accent-deep)',
        'accent-soft': 'var(--color-accent-soft)',
        'accent-tint': 'var(--color-accent-tint)',
        'accent-fg': 'var(--color-accent-fg)',
        'focus-ring': 'var(--color-focus-ring)',
        link: 'var(--color-link)',
        success: 'var(--color-success)',
        'success-soft': 'var(--color-success-soft)',
        warning: 'var(--color-warning)',
        'warning-soft': 'var(--color-warning-soft)',
        danger: 'var(--color-danger)',
        'danger-soft': 'var(--color-danger-soft)',
        info: 'var(--color-info)',
        'info-soft': 'var(--color-info-soft)',
        navy: 'var(--color-navy)',
        'navy-soft': 'var(--color-navy-soft)',
        ai: 'var(--color-ai)',
        'ai-soft': 'var(--color-ai-soft)',
        provenance: 'var(--color-purple)',
        'provenance-soft': 'var(--color-purple-soft)',
        // Historical Hallmark names kept as aliases (avoid in new code)
        paper: 'var(--color-canvas)',
        'paper-soft': 'var(--color-surface)',
        'paper-warm': 'var(--color-surface-subtle)',
        saffron: 'var(--color-warning)',
        'saffron-soft': 'var(--color-warning-soft)',
        marine: 'var(--color-info)',
        'marine-soft': 'var(--color-info-soft)',
      },
      fontFamily: {
        display: ['Inter Variable', 'Inter', 'Segoe UI', 'system-ui', 'sans-serif'],
        body: ['Inter Variable', 'Inter', 'Segoe UI', 'BlinkMacSystemFont', 'sans-serif'],
        mono: ['Cascadia Mono', 'SFMono-Regular', 'Consolas', 'ui-monospace', 'monospace'],
        // Serif retired by the RC system; alias kept for legacy call sites.
        serif: ['Inter Variable', 'Inter', 'Segoe UI', 'sans-serif'],
      },
      borderRadius: {
        sm: '8px',
        md: '10px',
        lg: '14px',
        pill: '999px',
      },
      // ---- Motion utilities (mirror of the CSS motion tokens in
      // app/tokens.css + app/motion.css). Lets utility classes compose
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
