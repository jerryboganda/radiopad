'use client';

import { useEffect, useState } from 'react';
import { api, type MarketplaceSubmission } from '@/lib/api';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import EmptyState from '@/components/ui/EmptyState';
import Banner from '@/components/ui/Banner';

/**
 * PRD §16 + Enterprise GA #13 Marketplace catalogue with submission &
 * approval workflow. Tabs: Browse (approved listings), My Submissions
 * (own submission status), Review (Admin/MedicalDirector approve/reject).
 *
 * Locked tokens only: `.rp-panel`, `.rp-panel-title`, `.rp-grid-3`,
 * `.rp-list`, `.badge.ok/warn/info`, `.primary`, `.primary-ghost`, `.subtle`.
 */
type Listing = {
  id: string;
  name: string;
  description: string;
  kind: string;
  priceCents: number;
  reviewedAt: string;
  installCount?: number;
};

type Tab = 'browse' | 'submissions' | 'review';

function statusBadgeClass(status: string): string {
  switch (status) {
    case 'pending_review':
      return 'badge warn';
    case 'approved':
      return 'badge ok';
    case 'rejected':
      return 'badge danger';
    case 'deprecated':
      return 'badge warn';
    default:
      return 'badge info';
  }
}

function statusLabel(status: string): string {
  switch (status) {
    case 'pending_review':
      return 'Pending Review';
    case 'approved':
      return 'Approved';
    case 'rejected':
      return 'Rejected';
    case 'deprecated':
      return 'Deprecated';
    case 'draft':
      return 'Draft';
    default:
      return status;
  }
}

export default function MarketplacePage() {
  const [tab, setTab] = useState<Tab>('browse');
  const [items, setItems] = useState<Listing[]>([]);
  const [submissions, setSubmissions] = useState<MarketplaceSubmission[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [rejectNotes, setRejectNotes] = useState<Record<string, string>>({});

  // ─── Submit form state ───
  const [submitCategory, setSubmitCategory] = useState('rulebook');
  const [submitSourceId, setSubmitSourceId] = useState('');
  const [submitVersion, setSubmitVersion] = useState('1.0.0');
  const [submitDesc, setSubmitDesc] = useState('');
  const [showSubmitForm, setShowSubmitForm] = useState(false);

  useEffect(() => {
    if (tab === 'browse') {
      api.marketplace.list().then(setItems).catch((e) => setError((e as Error).message));
    } else {
      api.marketplace.listSubmissions().then(setSubmissions).catch((e) => setError((e as Error).message));
    }
  }, [tab]);

  async function buy(id: string) {
    try {
      setBusy(id);
      const r = await api.marketplace.checkout(
        id,
        typeof window === 'undefined' ? '' : window.location.origin + '/marketplace',
      );
      if (r.url) window.location.assign(r.url);
      else if (r.granted) setError('Free asset granted. Refresh to use it.');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function installListing(id: string) {
    try {
      setBusy(id);
      const r = await api.marketplace.install(id);
      if (r.installed) {
        setError(`Installed successfully (install count: ${r.installCount}). Content added as Draft.`);
        // refresh listings to update install counts
        api.marketplace.list().then(setItems).catch(() => {});
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function handleSubmit() {
    try {
      setBusy('submit');
      await api.marketplace.submitForReview({
        category: submitCategory,
        sourceId: submitSourceId,
        version: submitVersion,
        description: submitDesc || undefined,
      });
      setShowSubmitForm(false);
      setSubmitSourceId('');
      setSubmitDesc('');
      setTab('submissions');
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function approveSubmission(id: string) {
    try {
      setBusy(id);
      await api.marketplace.approveSubmission(id);
      api.marketplace.listSubmissions().then(setSubmissions).catch(() => {});
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  async function rejectSubmission(id: string) {
    try {
      setBusy(id);
      await api.marketplace.rejectSubmission(id, rejectNotes[id]);
      api.marketplace.listSubmissions().then(setSubmissions).catch(() => {});
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(null);
    }
  }

  const pendingSubmissions = submissions.filter((s) => s.status === 'pending_review');
  const ownSubmissions = submissions;

  return (
    <Container>
      <PageHeader
        title="Marketplace"
        description="Browse and install rulebooks, templates, and prompt packs shared by other clinics. You can also publish your own."
        primaryAction={
          <button
            type="button"
            className="primary-ghost"
            onClick={() => setShowSubmitForm(!showSubmitForm)}
          >
            Publish something
          </button>
        }
      />

      <div className="rp-panel">
        {/* Tab bar */}
        <div className="tab-list" role="tablist" aria-label="Marketplace sections" style={{ marginBottom: '1rem' }}>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'browse'}
            className="tab-button"
            onClick={() => setTab('browse')}
          >
            Browse
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'submissions'}
            className="tab-button"
            onClick={() => setTab('submissions')}
          >
            My Submissions
          </button>
          <button
            type="button"
            role="tab"
            aria-selected={tab === 'review'}
            className="tab-button"
            onClick={() => setTab('review')}
          >
            Review ({pendingSubmissions.length})
          </button>
        </div>

        {error ? (
          <Banner tone="warn" onDismiss={() => setError(null)}>
            {error}
          </Banner>
        ) : null}

        {/* Submit form */}
        {showSubmitForm ? (
          <div className="rp-panel" data-testid="submit-form">
            <div className="rp-panel-title">Submit Content for Review</div>
            <div className="rp-list">
              <label>
                Category
                <select value={submitCategory} onChange={(e) => setSubmitCategory(e.target.value)}>
                  <option value="rulebook">Rulebook</option>
                  <option value="template">Template</option>
                  <option value="prompt_pack">Prompt Pack</option>
                </select>
              </label>
              <label>
                Source ID
                <input
                  type="text"
                  value={submitSourceId}
                  onChange={(e) => setSubmitSourceId(e.target.value)}
                  placeholder="e.g. chest-xray-v1"
                />
              </label>
              <label>
                Version
                <input
                  type="text"
                  value={submitVersion}
                  onChange={(e) => setSubmitVersion(e.target.value)}
                  placeholder="1.0.0"
                />
              </label>
              <label>
                Description
                <textarea
                  value={submitDesc}
                  onChange={(e) => setSubmitDesc(e.target.value)}
                  placeholder="Optional description"
                />
              </label>
              <button
                type="button"
                className="primary"
                disabled={busy === 'submit' || !submitSourceId}
                aria-busy={busy === 'submit'}
                onClick={handleSubmit}
              >
                {busy === 'submit' && <span className="rp-spinner sm" aria-hidden />}
                Submit for Review
              </button>
            </div>
          </div>
        ) : null}

        {/* Browse tab */}
        {tab === 'browse' ? (
          items.length === 0 && !error ? (
            <EmptyState
              title="No approved listings yet"
              description="When clinics publish rulebooks, templates, or prompt packs, they'll appear here for you to install."
            />
          ) : (
            <div className="rp-grid-3 rp-stagger" aria-live="polite">
              {items.map((item) => (
                <div key={item.id} className="rp-panel">
                  <div className="rp-panel-title">
                    {item.name} <span className="badge ok">{item.kind}</span>
                    {item.installCount != null && item.installCount > 0 ? (
                      <span className="badge">{item.installCount} installs</span>
                    ) : null}
                  </div>
                  <p className="rp-page-sub">{item.description}</p>
                  <div className="rp-row">
                    <strong>
                      {item.priceCents === 0
                        ? 'Free'
                        : `$${(item.priceCents / 100).toFixed(2)}`}
                    </strong>
                    <button
                      type="button"
                      className="primary"
                      disabled={busy === item.id}
                      aria-busy={busy === item.id}
                      onClick={() => installListing(item.id)}
                    >
                      {busy === item.id && <span className="rp-spinner sm" aria-hidden />}
                      Install
                    </button>
                    {item.priceCents > 0 ? (
                      <button
                        type="button"
                        className="subtle"
                        disabled={busy === item.id}
                        onClick={() => buy(item.id)}
                      >
                        Buy
                      </button>
                    ) : null}
                  </div>
                </div>
              ))}
            </div>
          )
        ) : null}

        {/* My Submissions tab */}
        {tab === 'submissions' ? (
          ownSubmissions.length === 0 ? (
            <EmptyState
              title="No submissions yet"
              description="Publish a rulebook, template, or prompt pack and track its review status here."
            />
          ) : (
            <div className="rp-grid-3 rp-stagger" aria-live="polite">
              {ownSubmissions.map((sub) => (
                <div key={sub.id} className="rp-panel">
                  <div className="rp-panel-title">
                    {sub.name}{' '}
                    <span className={statusBadgeClass(sub.status)}>
                      {statusLabel(sub.status)}
                    </span>
                  </div>
                  <p className="rp-page-sub">{sub.description}</p>
                  <div className="rp-row">
                    <span className="badge info">{sub.kind}</span>
                    <span className="badge">v{sub.version}</span>
                    {sub.installCount > 0 ? (
                      <span className="badge">{sub.installCount} installs</span>
                    ) : null}
                  </div>
                  {sub.reviewNotes ? (
                    <p className="rp-page-sub">
                      <strong>Review notes:</strong> {sub.reviewNotes}
                    </p>
                  ) : null}
                </div>
              ))}
            </div>
          )
        ) : null}

        {/* Review tab (Admin/MedicalDirector) */}
        {tab === 'review' ? (
          pendingSubmissions.length === 0 ? (
            <EmptyState
              title="Nothing to review"
              description="Submissions awaiting approval will show up here."
            />
          ) : (
            <div className="rp-grid-3 rp-stagger" aria-live="polite">
              {pendingSubmissions.map((sub) => (
                <div key={sub.id} className="rp-panel">
                  <div className="rp-panel-title">
                    {sub.name}{' '}
                    <span className={statusBadgeClass(sub.status)}>
                      {statusLabel(sub.status)}
                    </span>
                  </div>
                  <p className="rp-page-sub">{sub.description}</p>
                  <div className="rp-row">
                    <span className="badge info">{sub.kind}</span>
                    <span className="badge">v{sub.version}</span>
                  </div>
                  <div className="rp-list">
                    <textarea
                      placeholder="Review notes (optional)"
                      value={rejectNotes[sub.id] ?? ''}
                      onChange={(e) =>
                        setRejectNotes((prev) => ({ ...prev, [sub.id]: e.target.value }))
                      }
                    />
                    <div className="rp-row">
                      <button
                        type="button"
                        className="primary"
                        disabled={busy === sub.id}
                        aria-busy={busy === sub.id}
                        onClick={() => approveSubmission(sub.id)}
                      >
                        {busy === sub.id && <span className="rp-spinner sm" aria-hidden />}
                        Approve
                      </button>
                      <button
                        type="button"
                        className="subtle"
                        disabled={busy === sub.id}
                        onClick={() => rejectSubmission(sub.id)}
                      >
                        Reject
                      </button>
                    </div>
                  </div>
                </div>
              ))}
            </div>
          )
        ) : null}
      </div>
    </Container>
  );
}
