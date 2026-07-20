'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Eye, Lock, ShieldCheck, Users } from 'lucide-react';
import { api, TEACHING_DIFFICULTY_LABELS, type TeachingCase } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import ErrorState from '@/components/ui/ErrorState';
import Banner from '@/components/ui/Banner';

/**
 * PRD §14.14 — one teaching case. Reached via `/teaching/view?id=…` (query-param
 * routing, matching `reportHref` / `rulebookHref`) so the static export has a
 * single pre-rendered page rather than one per case.
 *
 * Loading it counts as a view for everyone except the author (TF-008).
 */
export default function TeachingCaseViewPage() {
  const router = useRouter();
  const [id, setId] = useState<string | null>(null);
  const [row, setRow] = useState<TeachingCase | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    setId(readQueryParam('id'));
  }, []);

  const load = useCallback(() => {
    if (!id) return;
    setError(null);
    api.teachingCases
      .get(id)
      .then(setRow)
      .catch((e: Error) => setError(e.message));
  }, [id]);

  useEffect(() => { load(); }, [load]);

  async function toggleVisibility() {
    if (!row) return;
    setBusy(true);
    setError(null);
    try {
      const next = row.visibility === 1
        ? await api.teachingCases.unpublish(row.id)
        : await api.teachingCases.publish(row.id);
      setRow(next);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function remove() {
    if (!row) return;
    if (!window.confirm('Delete this teaching case? This cannot be undone.')) return;
    setBusy(true);
    setError(null);
    try {
      await api.teachingCases.delete(row.id);
      router.push('/teaching');
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
    }
  }

  // `null` = the id hasn't been read off the URL yet (first client render);
  // `''` = there genuinely is no `?id=`. Only the latter is an error.
  if (id === '') {
    return (
      <Container>
        <PageHeader title="Teaching case" />
        <ErrorState
          title="No case selected"
          message="Open a case from the teaching library."
        />
      </Container>
    );
  }

  if (error && !row) {
    return (
      <Container>
        <PageHeader title="Teaching case" />
        <ErrorState title="Couldn't load this teaching case" message={error} onRetry={load} />
      </Container>
    );
  }

  if (!row) {
    return (
      <Container>
        <PageHeader title="Teaching case" />
        <div className="rp-panel" aria-busy="true">
          <Skeleton variant="block" height={220} />
        </div>
      </Container>
    );
  }

  const published = row.visibility === 1;
  const tags = (row.tags || '').split(',').map((s) => s.trim()).filter(Boolean);

  return (
    <Container>
      <PageHeader
        title={row.title}
        description={[row.modality, row.bodyPart].filter(Boolean).join(' · ')}
        secondaryActions={
          <Link href="/teaching" className="ghost" style={{ textDecoration: 'none' }}>
            Back to library
          </Link>
        }
        primaryAction={
          row.canEdit ? (
            <button type="button" className="primary" disabled={busy} onClick={toggleVisibility}>
              {published ? 'Withdraw from library' : 'Publish to workspace'}
            </button>
          ) : undefined
        }
      />

      {error && <Banner tone="danger" title="That didn't work">{error}</Banner>}

      <Banner tone="info" title="De-identified case">
        Patient names, identifiers, and explicit dates were removed from this case
        when it was saved. Redacted spans are shown as “[de-identified]”.
      </Banner>

      <div className="rp-panel">
        <div className="rp-chip-row">
          <span className="rp-chip">
            {TEACHING_DIFFICULTY_LABELS[row.difficulty] ?? row.difficultyName}
          </span>
          <span className={`badge ${published ? 'ok' : 'info'}`}>
            {published ? (
              <><Users size={11} strokeWidth={1.9} aria-hidden /> Shared with workspace</>
            ) : (
              <><Lock size={11} strokeWidth={1.9} aria-hidden /> Private to you</>
            )}
          </span>
          <span className="rp-chip">
            <Eye size={11} strokeWidth={1.9} aria-hidden /> {row.viewCount}
          </span>
          {tags.map((t) => <span key={t} className="rp-chip">{t}</span>)}
        </div>

        <Section title="Diagnosis" body={row.diagnosis} />
        <Section title="Teaching points" body={row.teachingPoints} />
        <Section title="Clinical history" body={row.clinicalHistory} />
        <Section title="Findings" body={row.findingsText} />
        <Section title="Impression" body={row.impressionText} />

        {row.sourceReportId && (
          <p className="rp-card-meta" style={{ display: 'flex', alignItems: 'center', gap: 5 }}>
            <ShieldCheck size={12} strokeWidth={1.8} aria-hidden />
            Created from one of your reports. The link is visible only to you and
            workspace administrators.
          </p>
        )}

        {row.canEdit && (
          <div className="rp-card-actions">
            <button type="button" className="subtle" disabled={busy} onClick={remove}>
              Delete case
            </button>
          </div>
        )}
      </div>
    </Container>
  );
}

function Section({ title, body }: { title: string; body: string }) {
  if (!body?.trim()) return null;
  return (
    <div className="section-block">
      <h2 className="rp-card-title">{title}</h2>
      <p style={{ whiteSpace: 'pre-wrap', margin: 0 }}>{body}</p>
    </div>
  );
}
