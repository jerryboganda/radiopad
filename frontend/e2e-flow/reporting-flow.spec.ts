import { test, expect, type Page, type APIRequestContext, type Locator } from '@playwright/test';

/**
 * End-to-end reporting flow against the REAL RadioPad API (started by
 * .github/workflows/e2e-reporting-flow.yml with RADIOPAD_DEV_SEED=1 on a throwaway
 * SQLite database — never production).
 *
 * Covers what the mock-data sweeps could not prove: that the real backend accepts
 * what the editor sends, that sign-off signs the text on screen (edits typed a
 * second before are in the signed report), that "Next case" opens the STAT case
 * the worklist suggests, every export format, RIS copy, and sign-out.
 *
 * Every step attaches a screenshot, so the run's artifact doubles as a visual record.
 */

test.use({ permissions: ['clipboard-read', 'clipboard-write'], viewport: { width: 1366, height: 900 } });

const RUN = Date.now().toString(36).toUpperCase();
const ACC = { routineA: `E2E-${RUN}-A`, stat: `E2E-${RUN}-S`, routineB: `E2E-${RUN}-B` };

type Created = { id: string };

async function createCase(api: APIRequestContext, accession: string, indication: string, modality = 'CT', bodyPart = 'Chest'): Promise<string> {
  const res = await api.post('/api/reports', {
    data: { modality, bodyPart, contrast: 'With', indication, age: 61, ageUnit: 'Years', gender: 'Female', accessionNumber: accession, comparison: 'None.' },
  });
  expect(res.ok(), `create ${accession}: ${res.status()} ${await res.text()}`).toBeTruthy();
  const { id } = (await res.json()) as Created;
  // A complete report, so validation can pass and sign-off is reachable.
  const patch = await api.patch(`/api/reports/${id}`, {
    data: {
      technique: 'Axial CT images of the chest were obtained after intravenous contrast.',
      comparison: 'None.',
      findings: 'Lungs are clear. No pleural effusion or pneumothorax. Heart size is normal.',
      // The bundled Chest CT rulebook blocks sign-off without a Lung-RADS category.
      impression: 'No acute cardiopulmonary abnormality. Lung-RADS category 1, negative.',
      recommendations: 'No follow-up imaging is required.',
    },
  });
  expect(patch.ok(), `patch ${accession}: ${patch.status()} ${await patch.text()}`).toBeTruthy();
  return id;
}

async function shot(page: Page, name: string) {
  await test.info().attach(name, { body: await page.screenshot({ fullPage: false }), contentType: 'image/png' });
}

async function openFromWorklist(page: Page, accession: string) {
  await page.goto('/worklist');
  await page.locator(`tr[aria-label="Open report ${accession}"]`).click();
  await page.waitForURL(/\/reports\/view/);
  await page.locator('main [contenteditable=true]').first().waitFor({ timeout: 90_000 });
}

/** Type at the end of the Findings section — and act IMMEDIATELY after, to prove save-before-sign. */
async function typeIntoFindings(page: Page, text: string) {
  const findings = page.locator('main [data-section="findings"] [contenteditable=true]').first();
  const target = (await findings.count()) ? findings : page.locator('main [contenteditable=true]').last();
  await target.click();
  await page.keyboard.press('End');
  await page.keyboard.type(` ${text}`);
}

/** Tabs and toggle chips are role=tab/radio elements, not plain buttons — match any clickable by text. */
function clickable(scope: Locator, label: string) {
  const exact = new RegExp(`^\\s*${label.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')}\\s*$`);
  return scope.locator('button, [role=tab], [role=radio], [role=option]').filter({ hasText: exact }).first();
}

function mainButton(page: Page, name: RegExp) {
  return page.locator('main button:visible').filter({ hasText: name }).first();
}

async function confirmInAppDialog(page: Page) {
  const dlg = page.locator('dialog.rp-confirm[open]');
  if (await dlg.count()) await dlg.getByRole('button', { name: /confirm|ok|yes|acknowledge/i }).last().click();
}

async function exportText(api: APIRequestContext, id: string): Promise<string> {
  const r = await api.get(`/api/reports/${id}/export/text?preview=true`);
  return r.ok() ? await r.text() : `HTTP ${r.status()}`;
}

test.describe.serial('reporting flow on the real API', () => {
  const ids: Record<keyof typeof ACC, string> = { routineA: '', stat: '', routineB: '' };
  const pageErrors: string[] = [];

  test.beforeAll(async ({ request }) => {
    // Order matters: routine A waits longest, the STAT case arrives later.
    ids.routineA = await createCase(request, ACC.routineA, 'Routine follow-up of known nodule.');
    ids.routineB = await createCase(request, ACC.routineB, 'Routine staging.');
    ids.stat = await createCase(request, ACC.stat, 'Acute chest pain. STAT.');
  });

  test.beforeEach(async ({ page }) => {
    page.on('pageerror', (e) => pageErrors.push(`${page.url()} :: ${e.message}`));
  });

  test.afterAll(() => {
    expect(pageErrors, `uncaught page errors:\n${pageErrors.join('\n')}`).toEqual([]);
  });

  test('worklist suggests the STAT case first', async ({ page }) => {
    await page.goto('/worklist');
    await expect(page.locator(`tr[aria-label="Open report ${ACC.stat}"]`)).toBeVisible({ timeout: 90_000 });
    await shot(page, 'worklist');
    await expect(page.getByText('Suggested next read').locator('..')).toContainText(ACC.stat);
  });

  test('validate → acknowledge → sign as primary signs the on-screen text', async ({ page, request }) => {
    await openFromWorklist(page, ACC.routineA);
    await shot(page, 'editor-opened');

    const marker = `MARK-A-${RUN}`;
    await typeIntoFindings(page, marker);
    // No waiting for autosave: sign-off must save first by itself.
    await mainButton(page, /^Validate$/).click();
    const inspector = page.locator('main .rp-inspector');
    await expect(inspector).toContainText('Validation summary', { timeout: 30_000 });
    await shot(page, 'validated');
    const summary = (await inspector.innerText()).replace(/\s+/g, ' ');
    expect(summary, `validation found blockers on a complete report: ${summary.slice(0, 600)}`).toMatch(/\b0 Blockers\b/i);

    await mainButton(page, /^Acknowledge$/).click();
    await confirmInAppDialog(page);
    await expect(page.locator('.rp-patientbar')).toContainText(/acknowledged/i, { timeout: 30_000 });

    await mainButton(page, /^Review & sign$/).click();
    await mainButton(page, /^Sign as Primary$/).click();
    await expect(page.locator('main')).toContainText(/Add Co-Signer/, { timeout: 30_000 });
    await shot(page, 'signed-primary');

    const stored = await exportText(request, ids.routineA);
    expect(stored, 'the signed report is missing the edit typed just before sign-off').toContain(marker);
  });

  test('every export format downloads, and RIS copy carries the latest text', async ({ page }) => {
    await page.goto(`/reports/view?id=${ids.routineA}`);
    await page.locator('main [contenteditable=true]').first().waitFor({ timeout: 90_000 });
    const inspector = page.locator('main .rp-inspector');
    await clickable(inspector, 'Export').click();
    await shot(page, 'export-tab');

    for (const fmt of ['PDF', 'Plain text (.txt)', 'Word (.docx)', 'JSON', 'FHIR']) {
      await clickable(inspector, fmt).click();
      const [dl] = await Promise.all([
        page.waitForEvent('download', { timeout: 60_000 }),
        clickable(inspector, 'Export report').click(),
      ]);
      const path = await dl.path();
      const size = path ? (await import('node:fs')).statSync(path).size : 0;
      test.info().annotations.push({ type: 'download', description: `${fmt} → ${dl.suggestedFilename()} (${size} bytes)` });
      expect(size, `${fmt} export is empty`).toBeGreaterThan(20);
    }

    const marker = `MARK-RIS-${RUN}`;
    await typeIntoFindings(page, marker);
    await page.getByRole('button', { name: 'Copy report to clipboard for RIS' }).click();
    await expect(page.getByText(/Copied \(auto-clears in 30s\)/)).toBeVisible({ timeout: 30_000 });
    const clip = await page.evaluate(() => navigator.clipboard.readText());
    expect(clip, 'RIS copy is missing the latest edit').toContain(marker);
    await shot(page, 'ris-copied');
  });

  test('Sign & Send refreshes the status, then Next case opens the STAT case', async ({ page, request }) => {
    await openFromWorklist(page, ACC.routineB);
    const marker = `MARK-B-${RUN}`;
    await typeIntoFindings(page, marker);
    await mainButton(page, /^Send$/).click();
    await mainButton(page, /^Sign & Send$/).click();
    await mainButton(page, /^Confirm sign-off$/).click();
    await expect(page.getByText('Signed, acknowledged & exported.')).toBeVisible({ timeout: 60_000 });
    await expect(page.locator('.rp-patientbar')).not.toContainText(/\bdraft\b/i, { timeout: 30_000 });
    await shot(page, 'signed-and-sent');
    expect(await exportText(request, ids.routineB), 'Sign & Send signed stale text').toContain(marker);

    await mainButton(page, /^Next case$/).click();
    await page.waitForURL((u) => u.search.includes(ids.stat), { timeout: 60_000 });
    await expect(page.locator('.rp-patientbar')).toContainText(ACC.stat, { timeout: 60_000 });
    await expect(page.getByText('Signed, acknowledged & exported.')).toHaveCount(0);
    await shot(page, 'next-case-stat');
  });

  test('narrow laptop window keeps the report and Export on screen', async ({ page }) => {
    await page.setViewportSize({ width: 1024, height: 768 });
    await page.goto(`/reports/view?id=${ids.stat}`);
    await page.locator('main [contenteditable=true]').first().waitFor({ timeout: 90_000 });
    const exp = page.locator('.rp-patientbar').getByRole('button', { name: /export/i }).first();
    const box = await exp.boundingBox();
    expect(box && box.x + box.width <= 1024, 'Export button is off-screen at 1024 px').toBeTruthy();
    await shot(page, 'narrow-1024');
  });

  test('sign out lands on the sign-in page', async ({ page }) => {
    await page.goto('/dashboard');
    await page.locator('.rp-profile-trigger').first().click();
    await page.getByRole('menuitem', { name: /sign out/i }).click();
    await page.waitForURL(/\/login/, { timeout: 30_000 });
    await shot(page, 'signed-out');
  });
});
