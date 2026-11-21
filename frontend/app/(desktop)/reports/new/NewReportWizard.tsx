'use client';

// One-screen report intake (PRD RPT — "new report" flow): study context, positive
// findings, optional clinical history and one Generate button, all visible at
// once. The AI provider is a remembered default shown beside Generate, not a step.
//
// Phase 6.2 — generation is now a durable async JOB, not an inline blocking call.
// We create + seed the draft, submit a `generate` (online) or `local-generate`
// (on-device) job, confirm with a toast (+ an "Open report" shortcut), then RESET
// the form so the radiologist can immediately post the next case. The topbar jobs
// widget tracks each running job; the report editor shows a GenerationBanner if
// they open one mid-generation.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, COMPLIANCE_LABELS, type CatalogItem, type Provider } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import SearchableSelect from '@/components/ui/SearchableSelect';
import RichTextEditor from '@/components/editor/RichTextEditor';
import { useJobs } from '@/components/jobs/JobsProvider';
import { useToast } from '@/components/ui/ToastProvider';
import { reportHref } from '@/lib/routes';
import { resolveDefaultProvider, setPreferredProviderId } from '@/lib/ai/providerPref';
import { LOCAL_LLAMA_ADAPTER } from '@/lib/models/onDeviceProvider';

function catalogOptions(items: CatalogItem[]) {
  return items
    .filter((c) => c.active !== false)
    .map((c) => ({ value: c.code, label: c.name || c.code, searchText: c.code }));
}

export default function NewReportWizard() {
  const router = useRouter();
  const jobs = useJobs();
  const { toast } = useToast();

  const [modalities, setModalities] = useState<CatalogItem[]>([]);
  const [bodyParts, setBodyParts] = useState<CatalogItem[]>([]);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Study context + demographics.
  const [modality, setModality] = useState<string | null>(null);
  const [bodyPart, setBodyPart] = useState<string | null>(null);
  const [contrast, setContrast] = useState('None');
  const [age, setAge] = useState('');
  const [ageUnit, setAgeUnit] = useState<'Years' | 'Months'>('Years');
  const [gender, setGender] = useState('');

  // Findings & history — rich text (serialized to clean Markdown by the editor). The
  // editors are uncontrolled, so remount them on reset by bumping `formKey`.
  const [findings, setFindings] = useState('');
  const [history, setHistory] = useState('');
  const [formKey, setFormKey] = useState(0);

  // Provider (remembered default).
  const [providerId, setProviderId] = useState('');

  // Submit lifecycle — `submitting` guards the button while we create the draft
  // and enqueue the job (a sub-second round trip; the generation itself runs in
  // the background and is tracked by the topbar widget, not here).
  const [submitting, setSubmitting] = useState(false);
  const [genError, setGenError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    Promise.all([api.modalities.list(), api.bodyParts.list(), api.providers.list()])
      .then(([m, b, p]) => {
        if (cancelled) return;
        setModalities(m);
        setBodyParts(b);
        const enabled = p.filter((x) => x.enabled);
        setProviders(enabled);
        // The radiologist's saved default engine wins; otherwise the
        // highest-priority enabled provider, so they can generate in one click.
        const preferred = resolveDefaultProvider(enabled);
        if (preferred) setProviderId(preferred.id);
      })
      .catch((e: Error) => {
        if (!cancelled) setLoadError(e.message);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const modalityOptions = useMemo(() => catalogOptions(modalities), [modalities]);
  const bodyPartOptions = useMemo(() => catalogOptions(bodyParts), [bodyParts]);
  const providerOptions = useMemo(
    () => providers.map((p) => ({ value: p.id, label: p.name, searchText: p.adapter })),
    [providers],
  );
  const selectedProvider = providers.find((p) => p.id === providerId) ?? null;

  const studyOk = !!modality && !!bodyPart;
  const canGenerate = studyOk && findings.trim().length > 0 && !submitting;

  /** Clear the form back to a blank first case, keeping the preferred provider
   *  so the next study defaults to the same engine. Remounts the rich editors. */
  const resetForm = useCallback(() => {
    setModality(null);
    setBodyPart(null);
    setContrast('None');
    setAge('');
    setAgeUnit('Years');
    setGender('');
    setFindings('');
    setHistory('');
    setGenError(null);
    setFormKey((k) => k + 1);
  }, []);

  const runGeneration = useCallback(async () => {
    if (!modality || !bodyPart) return;
    setSubmitting(true);
    setGenError(null);
    try {
      // 1) Create the draft with study context + demographics + clinical history
      //    (indication). 2) Seed the dictated positive findings. 3) Enqueue a
      //    durable generation JOB — the radiologist does not wait for it.
      const created = await api.reports.create({
        modality,
        bodyPart,
        contrast,
        age: age === '' ? null : Number(age),
        ageUnit,
        gender,
        indication: history,
      });
      await api.reports.patch(created.id, { findings });

      const info = {
        accession: created.study.accessionNumber,
        modality: created.study.modality,
        bodyPart: created.study.bodyPart,
      };

      if (selectedProvider?.adapter === LOCAL_LLAMA_ADAPTER) {
        // On-device provider: generation never touches the network, so the job
        // runs on the local sidecar (correlated to this hosted report id). The
        // report editor applies the drafted sections when the job finishes.
        await jobs.submit({
          origin: 'local',
          kind: 'local-generate',
          reportId: created.id,
          dto: {
            modality,
            bodyPart,
            contrast,
            age: age === '' ? null : Number(age),
            ageUnit,
            gender,
            indication: history,
            findings,
          },
          report: info,
        });
      } else {
        await jobs.submit({
          origin: 'hosted',
          kind: 'generate',
          reportId: created.id,
          providerId: providerId || undefined,
          report: info,
        });
      }

      // Operator decision: confirm + offer a shortcut, then clear for the next
      // case (batch-posting flow) rather than navigating into the report.
      toast({
        tone: 'success',
        title: 'Generation queued',
        message: (
          <span className="rp-jobs-toast">
            The draft is being generated in the background.
            <button
              type="button"
              className="subtle rp-jobs-toast-btn"
              onClick={() => router.push(reportHref(created.id))}
            >
              Open report
            </button>
          </span>
        ),
      });
      resetForm();
    } catch (e) {
      const err = e as { body?: { error?: string }; message?: string };
      setGenError(err.body?.error || err.message || 'Could not queue generation. Please try again.');
    } finally {
      setSubmitting(false);
    }
  }, [
    modality,
    bodyPart,
    contrast,
    age,
    ageUnit,
    gender,
    history,
    findings,
    providerId,
    selectedProvider,
    jobs,
    toast,
    router,
    resetForm,
  ]);

  return (
    <Container>
      <PageHeader
        title="New report"
        description="Pick the study, dictate or type your positive findings, and generate a first draft to review."
      />

      {loadError && (
        <div className="rp-panel" role="alert" style={{ borderColor: 'var(--red-border)' }}>
          Couldn’t load study catalogs or models: {loadError}
        </div>
      )}

      <div className="rp-panel rp-wizard-panel">
        <div>
          <div className="rp-wizard-grid">
            <div className="section-block">
              <label htmlFor="rp-new-modality">Modality</label>
              <SearchableSelect
                id="rp-new-modality"
                ariaLabel="Modality"
                options={modalityOptions}
                value={modality}
                onChange={setModality}
                placeholder="Select modality…"
              />
            </div>
            <div className="section-block">
              <label htmlFor="rp-new-bodypart">Body part</label>
              <SearchableSelect
                id="rp-new-bodypart"
                ariaLabel="Body part"
                options={bodyPartOptions}
                value={bodyPart}
                onChange={setBodyPart}
                placeholder="Select body part…"
              />
            </div>
            <div className="section-block">
              <label htmlFor="rp-new-contrast">Contrast</label>
              <select
                id="rp-new-contrast"
                className="rp-input"
                value={contrast}
                onChange={(e) => setContrast(e.target.value)}
              >
                <option value="None">Without contrast</option>
                <option value="With">With contrast</option>
                <option value="WithAndWithout">With and without contrast</option>
              </select>
            </div>
            <div className="rp-row rp-gap-sm">
              <div className="section-block" style={{ flex: 1 }}>
                <label htmlFor="rp-new-age">Age</label>
                <div className="rp-row rp-gap-sm">
                  <input
                    id="rp-new-age"
                    className="rp-input"
                    type="number"
                    min={0}
                    max={ageUnit === 'Months' ? 23 : 150}
                    value={age}
                    onChange={(e) => setAge(e.target.value)}
                    placeholder={ageUnit === 'Months' ? 'e.g. 6' : 'e.g. 54'}
                    style={{ flex: 1 }}
                  />
                  <select
                    aria-label="Age unit"
                    className="rp-input"
                    value={ageUnit}
                    onChange={(e) => {
                      // Switching units resets the value rather than converting it —
                      // "54" carried over from Years to Months would silently misstate
                      // a patient's age instead of just prompting a re-entry.
                      setAgeUnit(e.target.value as 'Years' | 'Months');
                      setAge('');
                    }}
                    style={{ flex: '0 0 auto', width: 96 }}
                  >
                    <option value="Years">Years</option>
                    <option value="Months">Months</option>
                  </select>
                </div>
              </div>
              <div className="section-block" style={{ flex: 1 }}>
                <label htmlFor="rp-new-gender">Gender</label>
                <select
                  id="rp-new-gender"
                  className="rp-input"
                  value={gender}
                  onChange={(e) => setGender(e.target.value)}
                >
                  <option value="">— select —</option>
                  <option value="Male">Male</option>
                  <option value="Female">Female</option>
                  <option value="Other">Other</option>
                  <option value="Unknown">Unknown</option>
                </select>
              </div>
            </div>
          </div>
        </div>

        <div>
          <div className="section-block">
            <label>Positive findings</label>
            <p className="rp-wizard-help">
              Dictate or type what you see. Use the mic (bottom-right) for voice, and the toolbar for
              bullets, numbering, and emphasis. The model expands these into a structured report.
            </p>
            <RichTextEditor
              key={`intake-findings-${formKey}`}
              sectionKey="intake-findings"
              ariaLabel="Positive findings"
              className="rp-rte-tall"
              onChange={setFindings}
            />
          </div>
        </div>

        <div>
          <div className="section-block">
            <label>Clinical history / indication (optional)</label>
            <p className="rp-wizard-help">
              Relevant history, presenting complaint, and the clinical question. This becomes the
              report’s indication and steers the impression.
            </p>
            <RichTextEditor
              key={`intake-history-${formKey}`}
              sectionKey="intake-history"
              ariaLabel="Clinical history"
              onChange={setHistory}
            />
          </div>
        </div>

        {genError && (
          <div className="banner warn" role="alert" style={{ marginTop: 12 }}>
            {genError}
          </div>
        )}

        <div className="rp-wizard-footer">
          <div className="section-block" style={{ flex: '1 1 240px', maxWidth: 360, margin: 0 }}>
            <label htmlFor="rp-new-provider">AI model</label>
            <SearchableSelect
              id="rp-new-provider"
              ariaLabel="AI provider"
              options={providerOptions}
              value={providerId}
              onChange={(v) => {
                setProviderId(v ?? '');
                if (v) setPreferredProviderId(v);
              }}
              placeholder="Select a model…"
              emptyLabel="No models enabled — add one under AI models"
            />
            {selectedProvider && (
              <div className="rp-wizard-provider-meta">
                <span className="badge">{COMPLIANCE_LABELS[selectedProvider.compliance] ?? 'Unknown'}</span>
                {selectedProvider.adapter === 'gemini-cli' && (
                  <span className="rp-wizard-help" style={{ margin: 0 }}>
                    Uses your Google sign-in on this machine — no API key. Run <code>gemini</code> once
                    to log in if prompted.
                  </span>
                )}
              </div>
            )}
          </div>
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            {!canGenerate && !submitting && (
              <span className="rp-wizard-help" style={{ margin: 0 }} aria-live="polite">
                {studyOk ? 'Add positive findings to generate.' : 'Choose modality and body part to generate.'}
              </span>
            )}
            <button className="primary" disabled={!canGenerate} onClick={runGeneration}>
              {submitting ? 'Queuing…' : 'Generate report'}
            </button>
          </div>
        </div>
      </div>
    </Container>
  );
}
