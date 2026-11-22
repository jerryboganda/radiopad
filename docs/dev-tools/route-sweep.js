// Headless click-through of every route against `next dev --webpack` (port 3000) + mock-api.js.
// Usage (from PowerShell: Git Bash rewrites /routes into Windows paths): node docs/dev-tools/route-sweep.js [route...]
// Writes %TEMP%/radiopad-sweep/sweep.json and light/dark screenshots in shots/. Playwright comes from the
// repo's pnpm store; executablePath points at an already-downloaded headless shell (no download needed).
const { chromium } = require('D:/Projects/RadioPad/RADIOPAD/node_modules/.pnpm/playwright@1.62.0/node_modules/playwright');
const fs = require('fs');
const path = require('path');
const OUT = path.join(require('os').tmpdir(), 'radiopad-sweep');
fs.mkdirSync(path.join(OUT, 'shots'), { recursive: true });
const BASE = 'http://localhost:3000';
const ALL = `/ /account/security /admin/billing /admin/feature-flags /admin/fhir-import
/admin/governance /admin/model-eval /admin/pacs /admin/providers/oauth?id=p1 /admin/security
/admin/settings /admin/sso /admin/ubag /admin/usage /admin/users /admin/validation-packs /ai-assistant
/analytics /analytics/quality /audit /audit/verify /body-parts /companion /critical-results /dashboard
/design/motion /device-pairing /findings-library /governance /login /marketplace /modalities
/notifications /offline /pair /peer-review /prompts /protocols /providers /quality /register
/report-templates /report-templates/designer?id=t1 /reporting /reporting/dictate /reports /reports/compose
/reports/new /reports/view?id=r1 /rulebooks /rulebooks/editor?id=rb1 /rulebooks/view?id=rb1 /settings /settings/corrections
/settings/hotkeys /settings/models /settings/snippets /teaching /teaching/view?id=tc1 /templates /terminology
/validation /worklist`.split(/\s+/).filter(Boolean);
const routes = process.argv.slice(2).length ? process.argv.slice(2) : ALL;
const SKIP = /export|download|print|sign out|log ?out|sign & send|pdf|docx|copy/i;

(async () => {
  const browser = await chromium.launch({ executablePath: String.raw`C:/Users/Admin/AppData/Local/ms-playwright/chromium_headless_shell-1248/chrome-headless-shell-win64/chrome-headless-shell.exe` });
  const ctx = await browser.newContext({ viewport: { width: 1366, height: 900 } });
  const results = [];
  for (const route of routes) {
    const page = await ctx.newPage();
    const errs = [];
    page.on('pageerror', (e) => errs.push('PAGEERROR ' + e.message.slice(0, 200)));
    page.on('console', (m) => { if (m.type() === 'error') errs.push('CONSOLE ' + m.text().slice(0, 200)); });
    page.on('dialog', (d) => d.dismiss().catch(() => {}));
    const r = { route, errs, clicks: [], text: '' };
    try {
      await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 120000 });
      await page.waitForTimeout(1500);
      r.final = new URL(page.url()).pathname;
      r.text = (await page.evaluate(() => (document.querySelector('main') || document.body).innerText)).replace(/\s+/g, ' ').slice(0, 400);
      r.overlay = await page.evaluate(() => { const p=document.querySelector('nextjs-portal'); const t=(p&&p.shadowRoot&&p.shadowRoot.textContent)||''; return /Unhandled Runtime Error|Runtime Error|Build Error|Console Error/i.test(t) ? t.replace(/\s+/g,' ').slice(0,300) : ''; });
      const slug = route.replace(/[^a-z0-9]+/gi, '_') || 'root';
      await page.screenshot({ path: path.join(OUT, 'shots', slug + '_light.png') });
      await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'dark'));
      await page.waitForTimeout(300);
      await page.screenshot({ path: path.join(OUT, 'shots', slug + '_dark.png') });
      await page.evaluate(() => document.documentElement.setAttribute('data-theme', 'light'));
      // click sweep
      const sel = 'main button:visible, main [role=tab]:visible, main summary:visible, main input[type=checkbox]:visible, main select:visible';
      const n = Math.min(await page.locator(sel).count(), 60); r.controls = n;
      for (let i = 0; i < n; i++) {
        const el = page.locator(sel).nth(i);
        let label = '';
        try {
          if (!(await el.isVisible())) continue;
          label = ((await el.innerText().catch(() => '')) || (await el.getAttribute('aria-label')) || (await el.evaluate((e) => e.tagName))).trim().slice(0, 40);
          if (SKIP.test(label)) { r.clicks.push('skip:' + label); continue; }
          if (await el.isDisabled().catch(() => false)) continue;
          const before = errs.length;
          const tag = await el.evaluate((e) => e.tagName);
          if (tag === 'SELECT') {
            const opts = await el.locator('option').evaluateAll((os) => os.map((o) => o.value));
            if (opts.length > 1) await el.selectOption(opts[opts.length - 1], { timeout: 2000 });
          } else await el.click({ timeout: 2000 });
          await page.waitForTimeout(400);
          const dlg = page.locator('dialog.rp-confirm[open]');
          if (await dlg.count()) { r.clicks.push('confirm:' + label); await page.keyboard.press('Escape'); }
          if (errs.length > before) r.clicks.push('ERR after ' + label);
          const now = new URL(page.url()).pathname;
          if (now !== r.final) { r.clicks.push('nav:' + label + '->' + now); await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 60000 }); }
          else {
            await page.keyboard.press('Escape'); await page.waitForTimeout(200);
            const open = await page.evaluate(() => { const v = (e) => { const r = e.getBoundingClientRect(); return r.width > 0 && r.height > 0 && getComputedStyle(e).visibility !== 'hidden'; }; const d = [...document.querySelectorAll('dialog[open], [role=dialog], [aria-modal=true], .rp-modal, .rp-drawer')].find(v); return d ? (d.getAttribute('aria-label') || d.className || d.tagName).toString().slice(0, 60) : ''; });
            if (open) { r.clicks.push('stuck-after-esc:' + label + ' [' + open + ']'); await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 60000 }); }
          }
        } catch (e) {
          const ls = e.message.split('\n');
          r.clicks.push('fail:' + label + ' ' + (ls.find((l) => /intercept/.test(l)) || ls[0]).slice(0, 160));
          await page.goto(BASE + route, { waitUntil: 'networkidle', timeout: 60000 }).catch(() => {});
        }
      }
    } catch (e) { r.errs.push('GOTO ' + e.message.split('\n')[0]); }
    results.push(r);
    console.log(JSON.stringify({ route, final: r.final, errs: errs.length, overlay: r.overlay }));
    fs.writeFileSync(path.join(OUT, 'sweep.json'), JSON.stringify(results, null, 1));
    await page.close();
  }
  await browser.close();
})();
