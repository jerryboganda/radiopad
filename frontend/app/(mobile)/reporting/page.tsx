'use client';

/**
 * Mobile Reporting — standalone report worklist.
 *
 * Radiologist signs in with a real credentialed session (goes through the
 * normal AuthGate → /login, unlike /companion which is intentionally public
 * for QR pairing) and can create + dictate reports without ever pairing to a
 * desktop. Recordings made here push to the desktop app's "Positive Findings
 * & Mobile Dictations" tab for the same report/case id, where a senior
 * consultant's dictation can be transcribed and handed to a junior
 * resident/fellow to finish the written report.
 *
 * Locked mobile classes: `.rp-mobile`, `.rp-page-title`, `.rp-page-sub`,
 * `.primary`, `.ghost`, `.rp-reporting-*`, `.rp-report-card`, `.badge`.
 */

import { useEffect, useState, useMemo } from 'react';
import { useRouter } from 'next/navigation';
import { Plus, Search, FileText, Mic, Clock, User, ChevronRight } from 'lucide-react';
import { getReports } from '@/lib/api/reportingClient';
import type { ReportDto } from '@/lib/api/reportingClient';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import NewReportModal from './components/NewReportModal';

const STATUS_FILTERS = ['All', 'Pending', 'Completed'] as const;
type StatusFilter = (typeof STATUS_FILTERS)[number];

function statusBadgeClass(status: string): string {
  const s = status.toLowerCase();
  if (s === 'completed') return 'badge ok';
  if (s === 'pending') return 'badge warn';
  return 'badge info';
}

export default function ReportingPage() {
  const router = useRouter();
  const [reports, setReports] = useState<ReportDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<StatusFilter>('All');
  const [isModalOpen, setIsModalOpen] = useState(false);

  const fetchReports = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await getReports(searchQuery, statusFilter);
      // Defensive: never let a malformed (non-array) response crash the whole
      // page via the `.filter` below — surface it as a load error instead.
      setReports(Array.isArray(data) ? data : []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load reports';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchReports();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter]);

  const filteredReports = useMemo(() => {
    return reports.filter((r) => {
      const q = searchQuery.toLowerCase().trim();
      const matchesSearch =
        !q ||
        r.radiologyId.toLowerCase().includes(q) ||
        r.patientName.toLowerCase().includes(q);
      const matchesStatus = statusFilter === 'All' || r.status.toLowerCase() === statusFilter.toLowerCase();
      return matchesSearch && matchesStatus;
    });
  }, [reports, searchQuery, statusFilter]);

  const handleCardClick = (id: string) => {
    router.push(`/reporting/dictate?id=${id}`);
  };

  return (
    <div className="rp-mobile">
      <div className="rp-reporting-list-header">
        <div>
          <h1 className="rp-page-title">
            <FileText size={18} aria-hidden style={{ marginRight: 6, verticalAlign: '-3px' }} />
            Reporting
          </h1>
          <p className="rp-page-sub">Radiology audio dictation &amp; positive findings</p>
        </div>
        <button type="button" className="primary" onClick={() => setIsModalOpen(true)} data-testid="new-report-btn">
          <Plus size={16} aria-hidden />
          New Report
        </button>
      </div>

      <div className="rp-reporting-search">
        <Search size={15} aria-hidden />
        <input
          type="text"
          value={searchQuery}
          onChange={(e) => setSearchQuery(e.target.value)}
          placeholder="Filter by Radiology ID or patient name…"
          aria-label="Search reports"
        />
      </div>

      <div className="rp-reporting-filters" role="tablist" aria-label="Filter by status">
        {STATUS_FILTERS.map((filter) => (
          <button
            key={filter}
            type="button"
            role="tab"
            aria-selected={statusFilter === filter}
            className={`rp-reporting-filter-pill${statusFilter === filter ? ' active' : ''}`}
            onClick={() => setStatusFilter(filter)}
          >
            {filter}
          </button>
        ))}
      </div>

      {isLoading ? (
        <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }} aria-busy="true">
          <Skeleton variant="block" height={92} />
          <Skeleton variant="block" height={92} />
          <Skeleton variant="block" height={92} />
        </div>
      ) : error ? (
        <ErrorState title="Couldn't load reports" message={error} onRetry={fetchReports} />
      ) : filteredReports.length === 0 ? (
        <EmptyState
          icon={<FileText size={18} aria-hidden />}
          title="No reports found"
          description={
            searchQuery || statusFilter !== 'All'
              ? 'Try adjusting your search query or filter selection.'
              : 'Create your first radiology report to begin recording dictations.'
          }
          action={
            <button type="button" className="primary" onClick={() => setIsModalOpen(true)}>
              <Plus size={14} aria-hidden />
              New Report
            </button>
          }
        />
      ) : (
        <div role="list" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
          {filteredReports.map((report) => {
            const dictCount = report.dictations?.length || 0;
            return (
              <button
                key={report.id}
                type="button"
                role="listitem"
                className="rp-report-card"
                onClick={() => handleCardClick(report.id)}
                data-testid="report-card"
              >
                <div className="rp-report-card-top">
                  <div>
                    <span className="rp-report-card-id">{report.radiologyId}</span>
                    <h2 className="rp-report-card-name">
                      <User size={15} aria-hidden />
                      {report.patientName}
                    </h2>
                  </div>
                  <span className={statusBadgeClass(report.status)}>{report.status}</span>
                </div>
                <div className="rp-report-card-footer">
                  <div className="rp-report-card-footer-left">
                    <span>{report.patientAge} yrs • {report.patientGender}</span>
                    <span style={{ display: 'flex', alignItems: 'center', gap: 4 }}>
                      <Clock size={13} aria-hidden />
                      {new Date(report.createdAt).toLocaleDateString()}
                    </span>
                  </div>
                  <div style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                    <span className="rp-report-card-audio-count">
                      <Mic size={13} aria-hidden />
                      {dictCount} audio{dictCount === 1 ? '' : 's'}
                    </span>
                    <ChevronRight size={16} aria-hidden style={{ color: 'var(--text-faint)' }} />
                  </div>
                </div>
              </button>
            );
          })}
        </div>
      )}

      <NewReportModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        onSuccess={fetchReports}
      />
    </div>
  );
}
