'use client';

// Guided multi-step report intake (PRD RPT — "new report" flow). Replaces the old
// zero-input "+ New report → empty editor" jump: the radiologist supplies study
// context + patient demographics, dictates/types the positive findings and the
// clinical history in rich editors, picks an AI provider, then hits Generate.
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

type Step = 1 | 2 | 3 | 4;

const STEPS: { n: Step; label: string }[] = [
  { n: 1, label: 'Study & patient' },
  { n: 2, label: 'Positive findings' },
  { n: 3, label: 'Clinical history' },
  { n: 4, label: 'Provider & generate' },
];

function catalogOptions(items: CatalogItem[]) {
  return items
    .filter((c) => c.active !== false)
    .map((c) => ({ value: c.code, label: c.name || c.code, searchText: c.code }));
}

export default function NewReportWizard() {
  const router = useRouter();
  const jobs = useJobs();
  const { toast } = useToast();
  const [step, setStep] = useState<Step>(1);

  const [modalities, setModalities] = useState<CatalogItem[]>([]);
  const [bodyParts, setBodyParts] = useState<CatalogItem[]>([]);
  const [providers, setProviders] = useState<Provider[]>([]);
  const [loadError, setLoadError] = useState<string | null>(null);

  // Step 1 — study context + demographics.
  const [modality, setModality] = useState<string | null>(null);
  const [bodyPart, setBodyPart] = useState<string | null>(null);
  const [contrast, setContrast] = useState('None');
  const [age, setAge] = useState('');
  const [gender, setGender] = useState('');

  // Steps 2 & 3 — rich text (serialized to clean Markdown by the editor). The
  // editors are uncontrolled, so remount them on reset by bumping `formKey`.
  const [findings, setFindings] = useState('');
  const [history, setHistory] = useState('');
  const [formKey, setFormKey] = useState(0);

  // Step 4 — provider.
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

  const step1Ok = !!modality && !!bodyPart;
  const canGenerate = step1Ok && findings.trim().length > 0 && !submitting;

  /** Clear the form back to a blank first case, keeping the preferred provider
   *  so the next study defaults to the same engine. Remounts the rich editors. */
  const resetForm = useCallback(() => {
    setStep(1);
    setModality(null);
    setBodyPart(null);
    setContrast('None');
    setAge('');
    setGender('');
    setFindings('');
    setHistory('');
    setGenError(null);
    setFormKey((k) => k + 1);
  }, []);

  const runGeneration = useCallback(async () => {
    if (!modality || !bodyPart) {
      setStep(1);
      return;
    }
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
        description="Give the study context and your findings, pick a model, and generate a first draft to review."
      />

      {loadError && (
        <div className="rp-panel" role="alert" style={{ borderColor: 'var(--red-border)' }}>
          Couldn’t load study catalogs or models: {loadError}
        </div>
      )}

      {/* Stepper */}
      <ol className="rp-wizard-steps" aria-label="Progress">
        {STEPS.map((s) => (
          <li
            key={s.n}
            className={`rp-wizard-step${step === s.n ? ' current' : ''}${step > s.n ? ' done' : ''}`}
            aria-current={step === s.n ? 'step' : undefined}
          >
            <button
              type="button"
              className="rp-wizard-step-btn"
              onClick={() => setStep(s.n)}
              disabled={s.n > 1 && !step1Ok}
            >
              <span className="rp-wizard-step-num">{step > s.n ? '✓' : s.n}</span>
              <span className="rp-wizard-step-label">{s.label}</span>
            </button>
          </li>
        ))}
      </ol>

      <div className="rp-panel rp-wizard-panel">
        {/* Step 1 — study & patient */}
        <div hidden={step !== 1}>
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
                <input
                  id="rp-new-age"
                  className="rp-input"
                  type="number"
                  min={0}
                  max={150}
                  value={age}
                  onChange={(e) => setAge(e.target.value)}
                  placeholder="e.g. 54"
                />
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

        {/* Step 2 — positive findings */}
        <div hidden={step !== 2}>
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

        {/* Step 3 — clinical history */}
        <div hidden={step !== 3}>
          <div className="section-block">
            <label>Clinical history / indication</label>
            <p className="rp-wizard-help">
              Relevant history, presenting complaint, and the clinical question. This becomes the
              report’s indication and steers the impression.
            </p>
            <RichTextEditor
              key={`intake-history-${formKey}`}
              sectionKey="intake-history"
              ariaLabel="Clinical history"
              className="rp-rte-tall"
              onChange={setHistory}
            />
          </div>
        </div>

        {/* Step 4 — provider & generate */}
        <div hidden={step !== 4}>
          <div className="section-block">
            <label htmlFor="rp-new-provider">AI provider</label>
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
                    Uses your Google sign-in (Gemini Pro / Code Assist) on this machine — no API key.
                    Run <code>gemini</code> once to log in if prompted.
                  </span>
                )}
              </div>
            )}
          </div>

          <div className="rp-wizard-summary">
            <h3>Ready to generate</h3>
            <ul>
              <li>
                <strong>Study:</strong>{' '}
                {modality ? modalityOptions.find((o) => o.value === modality)?.label : '—'} ·{' '}
                {bodyPart ? bodyPartOptions.find((o) => o.value === bodyPart)?.label : '—'}
              </li>
              <li>
                <strong>Patient:</strong> {age || '—'}
                {gender ? `, ${gender}` : ''}
              </li>
              <li>
                <strong>Findings:</strong>{' '}
                {findings.trim() ? `${findings.trim().slice(0, 80)}${findings.length > 80 ? '…' : ''}` : 'none yet'}
              </li>
            </ul>
            {!findings.trim() && (
              <p className="rp-wizard-help" style={{ color: 'var(--red)' }}>
                Add at least some positive findings (Step 2) before generating.
              </p>
            )}
          </div>
        </div>

        {genError && (
          <div className="banner warn" role="alert" style={{ marginTop: 12 }}>
            {genError}
          </div>
        )}

        {/* Footer nav */}
        <div className="rp-wizard-footer">
          <button
            className="ghost"
            disabled={step === 1}
            onClick={() => setStep((s) => (s > 1 ? ((s - 1) as Step) : s))}
          >
            ← Back
          </button>
          {step < 4 ? (
            <button
              className="primary"
              disabled={step === 1 && !step1Ok}
              onClick={() => setStep((s) => (s < 4 ? ((s + 1) as Step) : s))}
            >
              Next →
            </button>
          ) : (
            <button className="primary" disabled={!canGenerate} onClick={runGeneration}>
              {submitting ? 'Queuing…' : 'Generate report'}
            </button>
          )}
        </div>
      </div>
    </Container>
  );
}
