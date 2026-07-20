#!/usr/bin/env node
// RadioPad MSI E2E — drives the INSTALLED desktop product's renderer end to end.
//
// Run by desktop-bundle.yml's msi-e2e job AFTER the .msi has been installed and the pinned
// on-device models pre-placed under %LOCALAPPDATA%\com.radiopad.desktop. This is the only check
// in CI that exercises the renderer of the shipped product: login (including the mandatory TOTP
// enrollment, computed from the secret the UI displays), navigation, and the dictation draft
// panel running the REAL on-device MedGemma formatter — through clicks, not HTTP shortcuts.
//
// Design constraints, all deliberate:
//  - Zero npm dependencies. Node 22 provides fetch + WebSocket; CDP is spoken raw. An E2E that
//    needs its own lockfile maintenance is an E2E that rots.
//  - The webview uses the bundled sidecar as its data backend (RADIOPAD_BACKEND=loopback, which
//    the shipped CSP allows), so the flow is fully on-device: no cloud, no secrets, no PHI.
//  - The sidecar is pre-started HERE with a bootstrap secret + throwaway SQLite DB; the shell
//    ADOPTS it (sidecar_manager's single-instance guard). Tauri spawning its own sidecar is
//    covered separately by the bundle job's launch smoke.
//  - The report is created via API (with the token the UI's own login minted) rather than the
//    NewReportWizard, because the wizard hard-requires a successful CLOUD generate call before
//    it yields a report id — and this job deliberately has no cloud AI.
//
// Exit codes: 0 = pass (or tolerated headless-webview early-exit, loudly ::warning-ed),
// non-zero = a real assertion failed. Screenshots land in RADIOPAD_E2E_OUT at every milestone.

import { spawn, spawnSync } from 'node:child_process';
import { createHmac, randomBytes } from 'node:crypto';
import { mkdirSync, writeFileSync, createWriteStream, existsSync, readdirSync, statSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import path from 'node:path';

const SIDECAR_URL = 'http://127.0.0.1:7457';
const CDP_PORT = 9223;
const OUT = process.env.RADIOPAD_E2E_OUT || path.join(process.cwd(), 'e2e-out');
// Tag builds gate the release, so the headless-webview tolerance below must not apply there —
// a renderer that never came up would otherwise warning-pass and the gate would verify nothing.
const STRICT = process.env.RADIOPAD_E2E_STRICT === '1';

// Which half of the E2E to run.
//   'install' — install the SHIPPED msi and verify the packaged product: sidecar spawns, the
//               model manager reports MedASR + MedGemma present, the API answers. Fast, runs on
//               every bundle. Cannot drive the renderer: the shipped MSI has no CDP port, and
//               it deliberately never will (see the header note on browser arguments).
//   'full'    — additionally drive the renderer end to end. Requires an MSI built with the CDP
//               + fake-media browser args baked into Tauri's own additionalBrowserArgs, which
//               only the e2e build produces. Never a shipped artifact.
const PHASE = process.env.RADIOPAD_E2E_PHASES === 'full' ? 'full' : 'install';

const DICTATION =
  'CT chest with contrast. There is a three point two centimeter nodule in the right upper ' +
  'lobe. No pneumothorax. Impression acute pulmonary embolism in the left lower lobe.';

const log = (m) => console.log(`[e2e ${new Date().toISOString().slice(11, 19)}] ${m}`);
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

/** Thrown inside a waitFor predicate to abort immediately instead of retrying to timeout —
 *  for conditions that can only get worse (a dictation error banner is already showing). */
class FatalError extends Error {}

async function waitFor(what, fn, timeoutMs, intervalMs = 1000) {
  const deadline = Date.now() + timeoutMs;
  let lastErr;
  while (Date.now() < deadline) {
    try {
      const v = await fn();
      if (v) return v;
    } catch (e) {
      if (e instanceof FatalError) throw e;
      lastErr = e;
    }
    await sleep(intervalMs);
  }
  throw new Error(`timed out after ${timeoutMs} ms waiting for: ${what}` + (lastErr ? ` (last error: ${lastErr.message})` : ''));
}

// ── TOTP (RFC 6238: HMAC-SHA1, 30 s period, 6 digits — matches AuthFlowsController) ────────
function base32Decode(s) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  let bits = 0, value = 0;
  const out = [];
  for (const ch of s.replace(/=+$/, '').toUpperCase()) {
    const idx = alphabet.indexOf(ch);
    if (idx === -1) throw new Error(`invalid base32 char '${ch}' in TOTP secret`);
    value = (value << 5) | idx;
    bits += 5;
    if (bits >= 8) {
      out.push((value >>> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  return Buffer.from(out);
}

function totp(base32Secret, atMs = Date.now()) {
  const counter = Math.floor(atMs / 1000 / 30);
  const msg = Buffer.alloc(8);
  msg.writeBigUInt64BE(BigInt(counter));
  const h = createHmac('sha1', base32Decode(base32Secret)).update(msg).digest();
  const off = h[h.length - 1] & 0x0f;
  const code = ((h.readUInt32BE(off) & 0x7fffffff) % 1_000_000).toString().padStart(6, '0');
  return code;
}

// ── Minimal CDP client ─────────────────────────────────────────────────────────────────────
class Cdp {
  constructor(ws) {
    this.ws = ws;
    this.id = 0;
    this.pending = new Map();
    ws.addEventListener('message', (ev) => {
      const msg = JSON.parse(ev.data);
      if (msg.id && this.pending.has(msg.id)) {
        const { resolve, reject } = this.pending.get(msg.id);
        this.pending.delete(msg.id);
        msg.error ? reject(new Error(`CDP ${msg.error.message}`)) : resolve(msg.result);
      }
    });
  }
  static async connect(wsUrl) {
    const ws = new WebSocket(wsUrl);
    await new Promise((resolve, reject) => {
      ws.addEventListener('open', resolve, { once: true });
      ws.addEventListener('error', () => reject(new Error('CDP websocket failed to open')), { once: true });
    });
    return new Cdp(ws);
  }
  send(method, params = {}) {
    const id = ++this.id;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject });
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }
  /** Evaluate an expression in the page; throws on page-side exceptions. */
  async eval(expression, { awaitPromise = false } = {}) {
    const r = await this.send('Runtime.evaluate', {
      expression, awaitPromise, returnByValue: true,
    });
    if (r.exceptionDetails) {
      throw new Error(`page exception: ${r.exceptionDetails.exception?.description ?? r.exceptionDetails.text}`);
    }
    return r.result?.value;
  }
  async screenshot(name) {
    try {
      const { data } = await this.send('Page.captureScreenshot', { format: 'png' });
      writeFileSync(path.join(OUT, `${name}.png`), Buffer.from(data, 'base64'));
      log(`screenshot: ${name}.png`);
    } catch (e) {
      log(`screenshot ${name} failed (non-fatal): ${e.message}`);
    }
  }
}

// React-controlled inputs ignore bare value writes; go through the native setter + input event.
const setValueJs = (selector, value) => `
  (() => {
    const el = document.querySelector(${JSON.stringify(selector)});
    if (!el) return 'missing';
    const proto = el instanceof HTMLTextAreaElement ? HTMLTextAreaElement.prototype : HTMLInputElement.prototype;
    Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, ${JSON.stringify(value)});
    el.dispatchEvent(new Event('input', { bubbles: true }));
    return 'ok';
  })()`;

const clickButtonJs = (label) => `
  (() => {
    const b = [...document.querySelectorAll('button')].find(x => x.textContent.trim() === ${JSON.stringify(label)});
    if (!b) return 'missing';
    b.click();
    return 'ok';
  })()`;

async function setValue(cdp, selector, value) {
  const r = await cdp.eval(setValueJs(selector, value));
  if (r !== 'ok') throw new Error(`could not set value on ${selector}: ${r}`);
}
async function clickButton(cdp, label) {
  const r = await cdp.eval(clickButtonJs(label));
  if (r !== 'ok') throw new Error(`button "${label}" not found`);
}

/**
 * When CDP never comes up, the browser process command line is the definitive evidence: it
 * shows whether --remote-debugging-port actually reached WebView2 (env var consumed) or the
 * flag was lost (delivery problem) or no browser process exists at all (webview never
 * initialized on this runner). Written to the evidence artifact and echoed to the log.
 */
function dumpWebviewDiagnostics() {
  const ps = (cmd) =>
    spawnSync('powershell', ['-NoProfile', '-Command', cmd], { encoding: 'utf8' }).stdout ?? '';
  const procCmd =
    "Get-CimInstance Win32_Process | Where-Object { $_.Name -match 'msedgewebview2|radiopad' } " +
    '| Select-Object ProcessId,Name,CommandLine | Format-List | Out-String -Width 500';
  const report = [
    '--- msedgewebview2 / radiopad processes (command lines) ---',
    ps(procCmd),
    `--- listeners on :${CDP_PORT} ---`,
    ps("netstat -ano | Select-String ':" + CDP_PORT + "'"),
  ].join('\n');
  console.log(report);
  try { writeFileSync(path.join(OUT, 'webview-diagnostics.txt'), report); } catch { /* best effort */ }
}

// ── Locate the installed product ───────────────────────────────────────────────────────────
function findInstalled() {
  const roots = [
    process.env['ProgramFiles'], process.env['ProgramFiles(x86)'],
    process.env.LOCALAPPDATA && path.join(process.env.LOCALAPPDATA, 'Programs'),
  ].filter(Boolean).map((r) => path.join(r, 'RadioPad'));
  for (const root of roots) {
    if (!existsSync(root)) continue;
    const found = { app: null, sidecar: null };
    const walk = (dir) => {
      for (const e of readdirSync(dir)) {
        const p = path.join(dir, e);
        if (statSync(p).isDirectory()) walk(p);
        else if (/^radiopad-desktop\.exe$/i.test(e) || /^RadioPad\.exe$/i.test(e)) found.app = p;
        else if (/^radiopad-api.*\.exe$/i.test(e)) found.sidecar = p;
      }
    };
    walk(root);
    if (found.app && found.sidecar) return found;
  }
  throw new Error(
    'installed RadioPad not found under Program Files — did msiexec /i actually succeed? ' +
    `searched: ${roots.join(', ')}`);
}

async function httpJson(method, url, { body, headers = {} } = {}) {
  const res = await fetch(url, {
    method,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { /* keep raw */ }
  return { status: res.status, json, text };
}

// ── Main ───────────────────────────────────────────────────────────────────────────────────
const children = [];
function killTree(child, name) {
  if (!child || child.exitCode !== null) return;
  log(`stopping ${name} (pid ${child.pid})`);
  // /T kills the whole tree — the sidecar's llama-server child must not outlive it.
  spawn('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' });
}

async function main() {
  mkdirSync(OUT, { recursive: true });
  const { app: appExe, sidecar: sidecarExe } = findInstalled();
  log(`installed app:     ${appExe}`);
  log(`installed sidecar: ${sidecarExe}`);

  // 1. Pre-start the installed sidecar with a bootstrap secret + throwaway DB. The desktop
  //    shell will adopt it (it answers /api/health/ready on the default bind).
  log(`phase: ${PHASE}${PHASE === 'install' ? ' (packaged product only — renderer not driven)' : ' (renderer + microphone)'}`);
  const bootstrapSecret = randomBytes(24).toString('hex');
  const dbPath = path.join(tmpdir(), `radiopad-e2e-${Date.now()}.db`);
  const sidecar = spawn(sidecarExe, [], {
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Development',
      RADIOPAD_LOCAL_STT_ENABLED: '1',
      RADIOPAD_LOCAL_FORMATTER_ENABLED: '1',
      RADIOPAD_DB: `Data Source=${dbPath}`,
      RADIOPAD_BOOTSTRAP_SECRET: bootstrapSecret,
    },
  });
  children.push([sidecar, 'sidecar']);
  const sidecarLog = createWriteStream(path.join(OUT, 'sidecar.log'));
  sidecar.stdout.pipe(sidecarLog);
  sidecar.stderr.pipe(sidecarLog);

  await waitFor('sidecar /api/health/ready', async () => {
    const r = await fetch(`${SIDECAR_URL}/api/health/ready`).catch(() => null);
    return r?.status === 200;
  }, 120_000, 2000);
  log('sidecar ready');

  // 2. The pre-placed models must be visible to the manager — a layout drift must fail HERE
  //    with a clear message, not later as a mysterious 502 in the UI.
  const models = await httpJson('GET', `${SIDECAR_URL}/api/local-models`);
  const list = Array.isArray(models.json) ? models.json : models.json?.models ?? [];
  const byId = (needle) => list.find((m) => String(m.id ?? m.name ?? '').includes(needle));
  const medasr = byId('medasr');
  const medgemma = byId('medgemma');
  if (!medasr) throw new Error('model manager does not offer MedASR');
  if (medasr.downloaded === false) throw new Error('MedASR pre-placement not recognized (downloaded=false) — model dir layout drifted');
  if (!medgemma) throw new Error('model manager does not offer MedGemma');
  if (medgemma.downloaded === false) throw new Error('MedGemma pre-placement not recognized (downloaded=false) — model dir layout drifted');
  log('model manager sees MedASR + MedGemma as downloaded');

  // 3. Provision a real org + admin the UI can log in as.
  const tempPassword = `E2e-${randomBytes(9).toString('base64url')}`;
  const boot = await httpJson('POST', `${SIDECAR_URL}/api/admin/bootstrap-org`, {
    headers: { 'X-RadioPad-Bootstrap': bootstrapSecret },
    body: { slug: 'e2e-ci', name: 'E2E Clinic', adminEmail: 'e2e@radiopad.local', tempPassword },
  });
  if (boot.status !== 200) throw new Error(`bootstrap-org failed: HTTP ${boot.status} ${boot.text}`);
  const { slug, adminEmail } = boot.json;
  log(`bootstrapped org "${slug}" admin ${adminEmail}`);

  if (PHASE === 'install') {
    // Everything above ran against the REAL installed product: its own sidecar binary, its own
    // model store, its own API. That is the half a shipped MSI can prove.
    log('PASS (install phase): shipped MSI installs, packaged sidecar serves, ' +
        'MedASR + MedGemma present, API answers');
    return;
  }

  // 4. Launch the installed app. RADIOPAD_BACKEND points its webview at the sidecar (CSP
  //    allows loopback). The WebView2 flags: devtools protocol for this process only, plus
  //    Chromium's fake media stack so the REAL mic button records the MedASR bundle's own
  //    radiology dictation sample (%noloop = play once, then silence) with no permission UI.
  // Must be the SAME absolute path baked into the e2e build's --use-file-for-fake-audio-capture
  // flag, or the fake microphone plays nothing and the mic phase fails for the wrong reason.
  const micWav = process.env.RADIOPAD_E2E_MIC_WAV;
  if (!micWav || !existsSync(micWav)) {
    throw new Error(
      `RADIOPAD_E2E_MIC_WAV must point at the MedASR sample WAV (got: ${micWav ?? 'unset'}) — ` +
      'the microphone phase is a mandatory part of this E2E');
  }
  // 16 kHz mono 16-bit PCM → 32000 bytes/second; record the whole utterance plus a tail.
  const micMs = Math.ceil(((statSync(micWav).size - 44) / 32000) * 1000);
  // HOW THE FLAGS GET IN — and why neither external channel is used.
  //
  // WebView2 documents two ways to add browser arguments from outside an app: the
  // WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS env var, and a per-exe registry policy. BOTH were
  // tried against this app and BOTH are inert (runs 29739333987 / 29743040588): the captured
  // browser command line carries exactly wry's own defaults and none of ours, with a fully
  // healthy renderer. Tauri calls SetAdditionalBrowserArguments itself and WebView2 does not
  // append external values to it.
  //
  // So the flags are baked into Tauri's OWN additionalBrowserArgs at build time, by the e2e
  // build in desktop-msi-e2e.yml. That MSI is built from the same commit, used once, and
  // thrown away — the shipped installer never carries a debug port. `browserArgs` below is
  // therefore only used to sanity-check what the running browser actually got.
  //
  // A WebView2 browser process is shared per user-data-folder, and a already-running one
  // would be reused WITHOUT these args — so make sure nothing is holding one open and start
  // from a clean profile. Both are cheap and remove a whole class of "why did my flags not
  // apply" ambiguity from every future run.
  spawnSync('taskkill', ['/IM', path.basename(appExe), '/T', '/F'], { stdio: 'ignore' });
  spawnSync('taskkill', ['/IM', 'msedgewebview2.exe', '/T', '/F'], { stdio: 'ignore' });
  const webviewData = path.join(
    process.env.LOCALAPPDATA ?? '', 'com.radiopad.desktop', 'EBWebView');
  try { rmSync(webviewData, { recursive: true, force: true }); } catch { /* first run */ }

  const app = spawn(appExe, [], {
    env: {
      ...process.env,
      RADIOPAD_BACKEND: SIDECAR_URL,
      // WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS is deliberately NOT set — see the note above.
      // Its presence would suppress the registry policy that actually delivers the flags.
    },
  });
  children.push([app, 'app']);
  const appLog = createWriteStream(path.join(OUT, 'app.log'));
  app.stdout.pipe(appLog);
  app.stderr.pipe(appLog);

  // 5. Attach CDP. Mirror the launch smoke's one tolerance: if the shell exits early because
  //    the runner has no usable webview, warn LOUDLY and pass — but if the shell is alive,
  //    a missing CDP endpoint is a hard failure.
  let targets;
  try {
    targets = await waitFor('CDP /json/list', async () => {
      if (app.exitCode !== null) throw new Error(`app exited early (code ${app.exitCode})`);
      const r = await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`).catch(() => null);
      const j = r ? await r.json().catch(() => null) : null;
      return j && j.length ? j : null;
    }, 120_000, 2000);
  } catch (e) {
    dumpWebviewDiagnostics();
    if (app.exitCode !== null) {
      if (STRICT) {
        throw new Error(
          `desktop shell exited (code ${app.exitCode}) before the webview came up — ` +
          'a tag build must verify the renderer, so this is a failure, not a warning');
      }
      console.log('::warning::desktop shell exited before the webview came up — RENDERER NOT VERIFIED in this run');
      return;
    }
    throw e;
  }
  const page = targets.find((t) => t.type === 'page' && /tauri\.localhost/i.test(t.url)) ??
               targets.find((t) => t.type === 'page');
  if (!page) throw new Error(`no page target in CDP list: ${JSON.stringify(targets.map((t) => [t.type, t.url]))}`);
  log(`attaching to ${page.url}`);
  const cdp = await Cdp.connect(page.webSocketDebuggerUrl);
  await cdp.send('Runtime.enable');
  await cdp.send('Page.enable');

  // 6. Login. AuthGate redirects the signed-out app to /login.
  await waitFor('login form', () => cdp.eval(`!!document.querySelector('#li-email')`), 90_000);
  await cdp.screenshot('01-login');

  // Tap fetch to capture the token the UI's own login mints (it lives only in module memory).
  await cdp.eval(`(() => {
    if (window.__e2eTap) return true;
    const orig = window.fetch.bind(window);
    window.__e2eAuth = null;
    window.fetch = async (...a) => {
      const r = await orig(...a);
      try {
        const c = r.clone();
        if ((c.headers.get('content-type') || '').includes('application/json')) {
          const j = await c.json();
          if (j && typeof j.token === 'string' && j.token.length > 20) window.__e2eAuth = j;
        }
      } catch {}
      return r;
    };
    window.__e2eTap = true;
    return true;
  })()`);

  await setValue(cdp, '#li-tenant', slug);
  await setValue(cdp, '#li-email', adminEmail);
  await setValue(cdp, '#li-pw', tempPassword);
  await clickButton(cdp, 'Sign in');

  // 7. Mandatory first-login TOTP enrollment: read the secret the UI shows, compute the code.
  await waitFor('TOTP enrollment stage', () => cdp.eval(`!!document.querySelector('#li-enroll')`), 60_000);
  const secret = (await cdp.eval(
    `document.querySelector('.rp-secret-code')?.textContent ?? ''`)).replace(/\s+/g, '');
  if (!secret) throw new Error('TOTP enrollment secret not shown in .rp-secret-code');
  await cdp.screenshot('02-enroll');
  await setValue(cdp, '#li-enroll', totp(secret));
  await clickButton(cdp, 'Verify & continue');

  // 8. Optional biometric offer, then the shell. ±1-step server tolerance makes a boundary
  //    expiry unlikely; if we still sit on the enroll stage after 10 s, retry one fresh code.
  const landed = () => cdp.eval(
    `location.pathname !== '/login' && !!document.querySelector('.rp-shell')`);
  try {
    await waitFor('post-enroll transition', async () => {
      if (await cdp.eval(clickButtonJs('Skip for now')) === 'ok') log('skipped biometric offer');
      return await landed();
    }, 10_000, 1500);
  } catch {
    log('still on login after first TOTP attempt — retrying with a fresh code');
    await setValue(cdp, '#li-enroll', totp(secret));
    await clickButton(cdp, 'Verify & continue');
    await waitFor('app shell after login', async () => {
      if (await cdp.eval(clickButtonJs('Skip for now')) === 'ok') log('skipped biometric offer');
      return await landed();
    }, 30_000, 1500);
  }
  await cdp.screenshot('03-signed-in');
  log('logged in through the real UI (password + TOTP enrollment)');

  const auth = await cdp.eval('window.__e2eAuth');
  if (!auth?.token) throw new Error('login succeeded but no token was captured from the verify response');

  // 9. Create the report via API (see header for why not the wizard), seeded with the spoken
  //    dictation as findings so the draft panel pre-fills.
  const authHeaders = {
    Authorization: `Bearer ${auth.token}`,
    'X-RadioPad-Tenant': auth.tenant ?? slug,
    'X-RadioPad-User': auth.user ?? adminEmail,
  };
  const created = await httpJson('POST', `${SIDECAR_URL}/api/reports`, {
    headers: authHeaders,
    body: { modality: 'CT', bodyPart: 'Chest', indication: 'Shortness of breath' },
  });
  if (created.status !== 200 || !created.json?.id) throw new Error(`report create failed: HTTP ${created.status} ${created.text}`);
  const reportId = created.json.id;
  const patched = await httpJson('PATCH', `${SIDECAR_URL}/api/reports/${reportId}`, {
    headers: authHeaders,
    body: { findings: DICTATION },
  });
  if (patched.status !== 200) throw new Error(`report patch failed: HTTP ${patched.status} ${patched.text}`);
  log(`report ${reportId} created + seeded with dictation`);

  // 10. Drive the dictation draft panel.
  await cdp.eval(`location.assign('/reports/view?id=${reportId}')`);
  await waitFor('report page + Format draft toggle', () => cdp.eval(
    `[...document.querySelectorAll('button')].some(b => b.textContent.trim() === 'Format draft')`), 60_000);
  await cdp.screenshot('04-report');
  await clickButton(cdp, 'Format draft');

  await waitFor('dictation textarea', () => cdp.eval(`!!document.querySelector('#rp-dictation-raw')`), 30_000);
  const rawLen = await cdp.eval(`document.querySelector('#rp-dictation-raw').value.length`);
  if (!rawLen) await setValue(cdp, '#rp-dictation-raw', DICTATION);

  // The on-device toggle missing here would mean the shipped shell cannot reach its own
  // engines — the exact wiring-gap class this E2E exists to catch (IMPLEMENTATION_NOTES §9).
  const toggle = await cdp.eval(`(() => {
    const el = document.querySelector('[data-testid="dictation-on-device-toggle"]');
    if (!el) return 'missing';
    if (!el.checked) el.click();
    return 'clicked';
  })()`);
  if (toggle === 'missing') {
    throw new Error('on-device toggle missing from the draft panel in the REAL shell — the on-device path is unreachable to users');
  }
  // Controlled checkbox: the click round-trips through React state, so re-read after a beat —
  // existence alone is not proof the on-device path can actually be selected.
  await sleep(500);
  const onDevice = await cdp.eval(
    `document.querySelector('[data-testid="dictation-on-device-toggle"]').checked`);
  if (!onDevice) throw new Error('on-device toggle exists but could not be enabled in the real shell');
  await clickButton(cdp, 'Format (safety-checked)');
  await cdp.screenshot('05-formatting');
  log('formatting with on-device MedGemma (first call loads the 2.5 GB model — this takes minutes on CPU)');

  // 11. Wait for the pipeline verdict. Only a validator-passed draft counts: a fallback or
  //     error here is a real product failure, not a test inconvenience.
  const verdict = await waitFor('draft verdict', () => cdp.eval(`(() => {
    const t = document.body.innerText;
    if (t.includes('Passed the safety validator')) return 'success';
    if (t.includes('AI output rejected')) return 'fallback';
    if (t.includes('On-device formatting failed') || t.includes('formatting was unavailable')) return 'error';
    return null;
  })()`), 12 * 60_000, 5000);
  await cdp.screenshot('06-draft');
  if (verdict !== 'success') {
    throw new Error(`draft did not pass the safety validator through the UI: ${verdict}`);
  }

  // 12. The draft must be a real structured, safety-marked report.
  const checks = await cdp.eval(`(() => {
    const t = document.body.innerText;
    return {
      aiMarked: document.querySelectorAll('.ai-mark').length,
      measurement: t.includes('3.2 cm'),
      review: t.includes('Requires review'),
    };
  })()`);
  if (checks.aiMarked < 2) throw new Error(`expected multiple .ai-mark sections, got ${checks.aiMarked} — raw transcript echoed into one section?`);
  if (!checks.measurement) throw new Error('spoken measurement was not normalized to "3.2 cm" in the draft');
  if (!checks.review) throw new Error('draft is not gated behind "Requires review"');

  await clickButton(cdp, 'Apply to report');
  await sleep(3000);
  const applied = await cdp.eval(`document.body.innerText.includes('3.2 cm')`);
  if (!applied) throw new Error('applied draft content is not visible in the report editor');
  await cdp.screenshot('07-applied');

  // 13. Microphone dictation — the real capture path, not typed text. The fake audio device
  //     plays the MedASR bundle's own radiology sample, so pressing the overlay's HQ mic
  //     exercises getUserMedia → MediaRecorder → 16 kHz WAV encode → /api/stt/transcribe →
  //     on-device MedASR decode → insertion into the focused section editor.
  log(`mic phase: recording ~${Math.ceil(micMs / 1000)}s of the radiology sample through the fake microphone`);

  // The transcript goes to the last-FOCUSED section editor — without one the overlay
  // deliberately discards the recording. Use the empty "technique" section so the landing
  // spot is unambiguous (the typed-dictation phase never wrote there, and its words overlap
  // the sample's except "embolus").
  const focused = await cdp.eval(`(() => {
    const el = document.querySelector('[data-section-editor="technique"] .ProseMirror');
    if (!el) return 'missing';
    el.click();
    el.focus();
    return 'ok';
  })()`);
  if (focused !== 'ok') throw new Error('technique section editor not found to receive the dictation');

  // Engine identity (model=medasr) is only in the network response — the DOM deliberately
  // does not show it — so tap fetch and keep every /api/stt/transcribe response.
  await cdp.eval(`(() => {
    const orig = window.fetch.bind(window);
    window.__e2eStt = [];
    window.fetch = async (...a) => {
      const r = await orig(...a);
      try {
        if (String(a[0]).includes('/api/stt/transcribe')) window.__e2eStt.push(await r.clone().json());
      } catch {}
      return r;
    };
    return true;
  })()`);

  const hqClick = () => cdp.eval(`(() => {
    const b = document.querySelector('[data-testid="dictation-hq"]');
    if (!b) return 'missing';
    if (b.disabled) return 'disabled';
    b.click();
    return 'ok';
  })()`);
  const dictationError = async () => {
    const err = await cdp.eval(
      `document.querySelector('[data-testid="dictation-error"]')?.innerText ?? ''`);
    if (err) throw new FatalError(`dictation error banner: ${err}`);
  };

  const hqStart = await hqClick();
  if (hqStart !== 'ok') throw new Error(`HQ dictation button not clickable: ${hqStart}`);
  // A mic-permission failure shows its own error text and the button never flips to Stop —
  // distinguishable in the artifacts from a decode failure after recording.
  await waitFor('HQ recording to start', async () => {
    await dictationError();
    return cdp.eval(
      `document.querySelector('[data-testid="dictation-hq"]')?.getAttribute('aria-pressed') === 'true'`);
  }, 20_000);
  await cdp.screenshot('08-mic-recording');

  await sleep(micMs + 3000); // let the fake device play the whole utterance, plus a tail
  const hqStop = await hqClick();
  if (hqStop !== 'ok') throw new Error(`HQ stop click failed: ${hqStop}`);

  // Whole-buffer MedASR decode on CPU (the first request may also load the model).
  await waitFor('mic transcript inserted into the technique section', async () => {
    await dictationError();
    return cdp.eval(
      `/embolus/i.test(document.querySelector('[data-section-editor="technique"]')?.innerText ?? '')`);
  }, 10 * 60_000, 5000);
  await cdp.screenshot('09-mic-transcript');

  const mic = await cdp.eval(`(() => ({
    text: document.querySelector('[data-section-editor="technique"]')?.innerText ?? '',
    stt: window.__e2eStt,
  }))()`);
  // Content-word assertions only — MedAsrEngineSmokeTests documents that CTC casing and
  // punctuation are not contractually stable, and the client adds corrections/formatting.
  for (const word of ['chest', 'lobe', 'right', 'embolus', 'pneumothorax']) {
    if (!new RegExp(word, 'i').test(mic.text)) {
      throw new Error(`mic transcript is missing expected content word "${word}" — got: ${mic.text.slice(0, 300)}`);
    }
  }
  if (!mic.stt.length) throw new Error('no /api/stt/transcribe response captured — the mic path never reached the sidecar');
  const wrongEngine = mic.stt.filter((r) => !/medasr/i.test(String(r.model ?? '')));
  if (wrongEngine.length) {
    // The exact audit-era failure mode: dictation silently served by the wrong engine while
    // MedASR sits installed and idle.
    throw new Error('STT served by something other than MedASR: ' +
      JSON.stringify(wrongEngine.map((r) => ({ provider: r.provider, model: r.model }))));
  }
  log(`mic dictation decoded by "${mic.stt[mic.stt.length - 1].model}" and inserted into the report`);

  log('PASS: installed MSI → UI login (password + TOTP) → report → on-device MedGemma draft → applied → mic dictation via MedASR');
}

try {
  await main();
} catch (e) {
  console.error(`::error::MSI E2E failed: ${e.message}`);
  process.exitCode = 1;
} finally {
  // Always reap the app + sidecar (and the sidecar's llama-server child) — the workflow's
  // msiexec /x step needs the product not running. No process.exit(): the taskkill children
  // must be allowed to finish.
  for (const [child, name] of children) killTree(child, name);
}
