/** PostCSS config — Tailwind 3 + Autoprefixer.
 *  Object-form plugin map so Next 16 / Turbopack discovers it reliably.
 *  Tailwind runs at build time and emits static CSS, fully compatible
 *  with `output: 'export'` (no runtime, ships into Tauri/Capacitor). */
module.exports = {
  plugins: {
    tailwindcss: {},
    autoprefixer: {},
  },
};
