'use client';

import { useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, type Rulebook } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import { rulebookHref, rulebookEditorHref } from '@/lib/routes';
import { statusLabel, statusBadge, relativeTime } from '@/lib/rulebookStatus';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';

function splitCsv(csv?: string): string[] {
  return (csv || '').split(',').map((s) => s.trim()).filter(Boolean);
}

export default function RulebookDetailPage() {
  const router = useRouter();
  const [id, setId] = useState<string | null>(null);
  const [rb, setRb] = useState<(Rulebook & { sourceYaml: string }) | null>(null);
  const [yaml, setYaml] = useState('');
  const [problems, setProblems] = useState<string[] | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<'yaml' | 'visual'>('yaml');
  const [versions, setVersions] = useState<Rulebook[]>([]);
  const [rollbackVersion, setRollbackVersion] = useState('');

  useEffect(() => {
    setId(readQueryParam('id'));
  }, []);

  useEffect(() => {
    if (!id) return;
    api.rulebooks.get(id)
      .then((r) => { setRb(r); setYaml(r.sourceYaml || ''); })
      .catch((e: Error) => setError(e.message));
  }, [id]);

  // RB-008 — load prior approved versions for the rollback dropdown.
  useEffect(() => {
    if (!rb) return;
    api.rulebooks.list()
      .then((rows) => {
        const same = rows.filter((r) =>
          r.rulebookId === rb.rulebookId
          && r.id !== rb.id
          && (typeof r.status === 'string' ? r.status === 'Approved' : r.status === 2));
        setVersions(same);
      })
      .catch(() => {/* silent — rollback UI is optional */});
  }, [rb]);

  // RB-002 — derive a "visual" view from the current YAML so the tabbed
  // editor can render lists for required_sections / style.avoid_terms /
  // style.approved_followups / rules / prompt_blocks.
  const visual = useMemo(() => parseVisual(yaml), [yaml]);

  async function validate() {
    setBusy(true);
    try { setProblems((await api.rulebooks.validateYaml(yaml)).problems); }
    finally { setBusy(false); }
  }

  async function save() {
    setBusy(true);
    setError(null);
    try { await api.rulebooks.save(yaml); setProblems([]); }
    catch (e) { const err = e as { body?: { error?: string }; message: string }; setError(err.body?.error || err.message); }
    finally { setBusy(false); }
  }

  async function approve() {
    if (!rb) return;
    setBusy(true);
    try { setRb({ ...(await api.rulebooks.approve(rb.id)), sourceYaml: yaml }); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function deprecate() {
    if (!rb) return;
    setBusy(true);
    try { setRb({ ...(await api.rulebooks.deprecate(rb.id)), sourceYaml: yaml }); }
    catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }

  async function rollback() {
    if (!rb || !rollbackVersion) return;
    setBusy(true);
    setError(null);
    try {
      const next = await api.rulebooks.rollback(rb.id, rollbackVersion);
      // Materialised as a new approved row; navigate to it.
      router.push(rulebookHref(next.id));
    } catch (e) {
      const err = e as { body?: { error?: string }; message: string };
      setError(err.body?.error || err.message);
    } finally {
      setBusy(false);
    }
  }

  const backLink = (
    <button type="button" className="ghost" onClick={() => router.push('/rulebooks')}>← Rulebooks</button>
  );

  if (error && !rb) {
    return (
      <Container>
        <div className="rp-mb-md">{backLink}</div>
        <ErrorState title="Couldn't load rulebook" message={error} onRetry={() => { setError(null); setId(readQueryParam('id')); }} />
      </Container>
    );
  }
  if (!id && id !== null) {
    return (
      <Container>
        <div className="rp-mb-md">{backLink}</div>
        <ErrorState title="Missing rulebook id" message="This link doesn't point to a rulebook." />
      </Container>
    );
  }
  if (!rb) {
    return (
      <Container>
        <div className="rp-mb-md">{backLink}</div>
        <div className="rp-panel"><Skeleton variant="text" width="40%" /><Skeleton variant="block" height={420} style={{ marginTop: 12 }} /></div>
      </Container>
    );
  }

  const status = statusLabel(rb.status);
  const modalities = splitCsv(rb.appliesToModalities);
  const bodyParts = splitCsv(rb.appliesToBodyParts);
  const updated = relativeTime(rb.updatedAt);

  return (
    <Container>
      <div className="rp-mb-md">{backLink}</div>

      <PageHeader
        title={<>{rb.name} <code style={{ fontSize: 14 }}>{rb.rulebookId}</code></>}
        description={
          <>
            v{rb.version} · {rb.owner || 'no owner'} ·{' '}
            <span className={`badge ${statusBadge(rb.status)}`}>{status}</span>
          </>
        }
        primaryAction={
          <button type="button" className="primary-ghost" onClick={() => router.push(rulebookEditorHref(rb.id))}>
            Open visual editor
          </button>
        }
      />

      <div aria-live="polite">
        {error && <Banner tone="danger" title="Couldn't complete that">{error}</Banner>}
      </div>

      {/* Metadata strip */}
      <div className="rp-panel">
        <div className="rp-grid-2">
          <div className="rp-field">
            <span>Modalities</span>
            <div className="rp-chip-row">
              {modalities.length ? modalities.map((m) => <span key={m} className="rp-chip">{m}</span>)
                : <em style={{ color: 'var(--text-faint)' }}>any</em>}
            </div>
          </div>
          <div className="rp-field">
            <span>Body parts</span>
            <div className="rp-chip-row">
              {bodyParts.length ? bodyParts.map((b) => <span key={b} className="rp-chip">{b}</span>)
                : <em style={{ color: 'var(--text-faint)' }}>any</em>}
            </div>
          </div>
          <div className="rp-field">
            <span>Version</span>
            <div>v{rb.version}</div>
          </div>
          <div className="rp-field">
            <span>Last updated</span>
            <div>{updated || '—'}</div>
          </div>
        </div>
      </div>

      {/* Lifecycle toolbar */}
      <div className="rp-toolbar">
        <button className="ghost" onClick={validate} disabled={busy} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />} Validate
        </button>
        <button className="primary" onClick={save} disabled={busy} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />} Save new version
        </button>
        <button className="primary-ghost" onClick={approve} disabled={busy || status === 'Approved'} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />} Approve
        </button>
        <button className="subtle" onClick={deprecate} disabled={busy || status === 'Deprecated'} aria-busy={busy}>
          {busy && <span className="rp-spinner sm" aria-hidden />} Deprecate
        </button>
        {versions.length > 0 && (
          <span className="rp-row" style={{ gap: 6, marginLeft: 'auto' }}>
            <select
              className="rp-input subtle"
              value={rollbackVersion}
              onChange={(e) => setRollbackVersion(e.target.value)}
              disabled={busy}
              style={{ width: 'auto' }}
            >
              <option value="">Rollback to…</option>
              {versions.map((v) => (
                <option key={v.id} value={v.version}>v{v.version}</option>
              ))}
            </select>
            <button className="subtle" onClick={rollback} disabled={busy || !rollbackVersion} aria-busy={busy}>
              {busy && <span className="rp-spinner sm" aria-hidden />} Rollback
            </button>
          </span>
        )}
      </div>

      <div className="rp-tabs" role="tablist" aria-label="Rulebook editor mode">
        <button
          role="tab"
          aria-selected={tab === 'yaml'}
          className={`rp-tab ${tab === 'yaml' ? 'active' : ''}`}
          onClick={() => setTab('yaml')}
        >YAML source</button>
        <button
          role="tab"
          aria-selected={tab === 'visual'}
          className={`rp-tab ${tab === 'visual' ? 'active' : ''}`}
          onClick={() => setTab('visual')}
        >Visual</button>
      </div>

      {tab === 'yaml' ? (
        <div className="rp-panel rp-anim-fade-in" key="yaml">
          <div className="rp-panel-title">YAML source</div>
          <textarea
            value={yaml}
            onChange={(e) => setYaml(e.target.value)}
            style={{ minHeight: 480, fontFamily: 'var(--mono)', fontSize: 12.5 }}
          />
          {problems !== null && (
            <div style={{ marginTop: 12 }} aria-live="polite">
              {problems.length === 0
                ? <span className="badge ok">OK — schema valid</span>
                : problems.map((p, i) => <div key={i} className="finding warning">{p}</div>)}
            </div>
          )}
        </div>
      ) : (
        <div className="rp-panel rp-anim-fade-in" key="visual">
          <div className="rp-panel-title">Visual</div>
          <p className="rp-page-sub" style={{ marginTop: 0 }}>
            Read-only summary of the parsed rulebook. Edit the YAML tab and re-save, or use the visual editor, to change these fields.
          </p>
          <div className="rp-field">
            <span>Required sections</span>
            <div className="rp-chip-row">{visual.requiredSections.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>none</em>
              : visual.requiredSections.map((s) => <span key={s} className="badge">{s}</span>)}</div>
          </div>
          <div className="rp-field">
            <span>Style — avoid terms</span>
            <div className="rp-chip-row">{visual.avoidTerms.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>none</em>
              : visual.avoidTerms.map((s) => <span key={s} className="badge danger">{s}</span>)}</div>
          </div>
          <div className="rp-field">
            <span>Style — approved follow-ups</span>
            <div>{visual.approvedFollowups.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>none</em>
              : visual.approvedFollowups.map((s) => <div key={s} className="finding info">{s}</div>)}</div>
          </div>
          <div className="rp-field">
            <span>Rules</span>
            <div>{visual.rules.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>none</em>
              : visual.rules.map((r) => (
                <div key={r.id} className={`finding ${r.severity || 'info'}`}>
                  <code>{r.id}</code> · {r.severity || 'info'} — {r.description || ''}
                </div>
              ))}</div>
          </div>
          <div className="rp-field">
            <span>Prompt blocks</span>
            <div className="rp-chip-row">{visual.promptBlockKeys.length === 0
              ? <em style={{ color: 'var(--text-faint)' }}>none</em>
              : visual.promptBlockKeys.map((k) => <span key={k} className="badge">{k}</span>)}</div>
          </div>
        </div>
      )}
    </Container>
  );
}

type VisualRule = { id: string; severity?: string; description?: string };
type VisualView = {
  requiredSections: string[];
  avoidTerms: string[];
  approvedFollowups: string[];
  rules: VisualRule[];
  promptBlockKeys: string[];
};

/**
 * RB-002 — minimal YAML → visual extractor. Avoids a YAML parser dependency:
 * the rulebook spec uses a stable, indentation-driven shape (validated
 * server-side via `RulebookSpec.FromYaml`), so a line-oriented scan is
 * sufficient for displaying the parsed structure.
 */
function parseVisual(src: string): VisualView {
  const requiredSections: string[] = [];
  const avoidTerms: string[] = [];
  const approvedFollowups: string[] = [];
  const rules: VisualRule[] = [];
  const promptBlockKeys: string[] = [];

  const lines = src.split(/\r?\n/);
  let section: 'none' | 'rs' | 'avoid' | 'fu' | 'rules' | 'prompts' = 'none';
  let currentRule: VisualRule | null = null;
  for (const raw of lines) {
    const line = raw.trimEnd();
    if (!line) continue;
    if (/^required_sections\s*:\s*\[/.test(line)) {
      const m = line.match(/\[(.*)\]/);
      if (m) for (const t of m[1].split(',')) requiredSections.push(stripStr(t.trim()));
      section = 'none'; continue;
    }
    if (/^required_sections\s*:\s*$/.test(line)) { section = 'rs'; continue; }
    if (/^\s+avoid_terms\s*:\s*\[/.test(line)) {
      const m = line.match(/\[(.*)\]/);
      if (m) for (const t of m[1].split(',')) avoidTerms.push(stripStr(t.trim()));
      continue;
    }
    if (/^\s+avoid_terms\s*:\s*$/.test(line)) { section = 'avoid'; continue; }
    if (/^\s+approved_followups\s*:\s*$/.test(line)) { section = 'fu'; continue; }
    if (/^rules\s*:\s*$/.test(line)) { section = 'rules'; continue; }
    if (/^prompt_blocks\s*:\s*$/.test(line)) { section = 'prompts'; continue; }

    if (section === 'rs' && /^\s*-\s/.test(line)) requiredSections.push(stripStr(line.replace(/^\s*-\s+/, '')));
    else if (section === 'avoid' && /^\s*-\s/.test(line)) avoidTerms.push(stripStr(line.replace(/^\s*-\s+/, '')));
    else if (section === 'fu' && /^\s*-\s/.test(line)) approvedFollowups.push(stripStr(line.replace(/^\s*-\s+/, '')));
    else if (section === 'rules') {
      const idMatch = line.match(/^\s*-\s*id\s*:\s*(.+)$/);
      const sevMatch = line.match(/^\s*severity\s*:\s*(.+)$/);
      const descMatch = line.match(/^\s*description\s*:\s*(.+)$/);
      if (idMatch) {
        if (currentRule) rules.push(currentRule);
        currentRule = { id: stripStr(idMatch[1]) };
      } else if (currentRule && sevMatch) currentRule.severity = stripStr(sevMatch[1]).toLowerCase();
      else if (currentRule && descMatch) currentRule.description = stripStr(descMatch[1]);
    } else if (section === 'prompts') {
      const m = line.match(/^\s+([a-z_][a-z0-9_]*)\s*:/i);
      if (m) promptBlockKeys.push(m[1]);
    }
  }
  if (currentRule) rules.push(currentRule);
  return { requiredSections, avoidTerms, approvedFollowups, rules, promptBlockKeys };
}

function stripStr(s: string): string {
  let v = s.trim();
  if ((v.startsWith('"') && v.endsWith('"')) || (v.startsWith("'") && v.endsWith("'"))) v = v.slice(1, -1);
  return v;
}
