'use client';

import React, { useEffect, useState, useMemo } from 'react';
import { useRouter } from 'next/navigation';
import {
  Plus,
  Search,
  FileText,
  Mic,
  Clock,
  User,
  Filter,
  RefreshCw,
  ChevronRight,
  AlertCircle
} from 'lucide-react';
import { getReports, ReportDto } from '@/lib/api/reportingClient';
import NewReportModal from './components/NewReportModal';

export default function ReportingPage() {
  const router = useRouter();
  const [reports, setReports] = useState<ReportDto[]>([]);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState('');
  const [statusFilter, setStatusFilter] = useState<'All' | 'Pending' | 'Completed'>('All');
  const [isModalOpen, setIsModalOpen] = useState(false);

  const fetchReports = async () => {
    setIsLoading(true);
    setError(null);
    try {
      const data = await getReports(searchQuery, statusFilter);
      setReports(data || []);
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to load reports';
      setError(msg);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchReports();
  }, [statusFilter]);

  const filteredReports = useMemo(() => {
    return reports.filter((r) => {
      const q = searchQuery.toLowerCase().trim();
      const matchesSearch =
        !q ||
        r.radiologyId.toLowerCase().includes(q) ||
        r.patientName.toLowerCase().includes(q);

      const matchesStatus =
        statusFilter === 'All' ||
        r.status.toLowerCase() === statusFilter.toLowerCase();

      return matchesSearch && matchesStatus;
    });
  }, [reports, searchQuery, statusFilter]);

  const handleCardClick = (id: string) => {
    router.push(`/reporting/dictate?id=${id}`);
  };

  return (
    <div className="rp-mobile-reporting min-h-screen bg-[var(--bg-app,#0b0f17)] text-[var(--text,#e2e8f0)] pb-24">
      {/* Top Header */}
      <header className="sticky top-0 z-30 bg-[var(--bg-panel,#131722)]/90 backdrop-blur-md border-b border-[var(--border,#262c40)] px-4 py-3.5 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-bold tracking-tight text-white flex items-center gap-2">
            <FileText className="w-5 h-5 text-blue-500" />
            Reporting
          </h1>
          <p className="text-xs text-slate-400">Radiology audio dictation & positive findings</p>
        </div>
        <button
          type="button"
          onClick={() => setIsModalOpen(true)}
          className="px-3.5 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 active:bg-blue-700 text-white text-xs font-semibold shadow-md shadow-blue-600/30 flex items-center gap-1.5 transition-all"
        >
          <Plus className="w-4 h-4" />
          <span>New Report</span>
        </button>
      </header>

      {/* Controls Container */}
      <div className="p-4 space-y-3">
        {/* Search Bar */}
        <div className="relative">
          <Search className="w-4 h-4 absolute left-3.5 top-3 text-slate-400" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder="Filter by Radiology ID or Patient Name..."
            className="rp-input w-full pl-10 pr-4 py-2.5 bg-[var(--bg-panel,#161b26)] border border-[var(--border,#262c40)] rounded-xl text-sm text-white placeholder-slate-500 focus:outline-none focus:ring-2 focus:ring-blue-500/50"
          />
          {searchQuery && (
            <button
              onClick={() => setSearchQuery('')}
              className="absolute right-3 top-2.5 text-xs text-slate-400 hover:text-white"
            >
              Clear
            </button>
          )}
        </div>

        {/* Filter Pills */}
        <div className="flex items-center gap-2 overflow-x-auto pb-1 no-scrollbar">
          <Filter className="w-3.5 h-3.5 text-slate-400 shrink-0" />
          {(['All', 'Pending', 'Completed'] as const).map((filter) => {
            const isActive = statusFilter === filter;
            return (
              <button
                key={filter}
                type="button"
                onClick={() => setStatusFilter(filter)}
                className={`px-3.5 py-1.5 rounded-full text-xs font-medium transition-all ${
                  isActive
                    ? 'bg-blue-600 text-white shadow-sm shadow-blue-600/40'
                    : 'bg-[var(--bg-panel,#161b26)] text-slate-400 hover:text-slate-200 border border-[var(--border,#262c40)]'
                }`}
              >
                {filter}
              </button>
            );
          })}
        </div>
      </div>

      {/* Main Content Area */}
      <main className="px-4 space-y-3">
        {isLoading ? (
          <div className="py-16 text-center text-slate-400 space-y-3">
            <RefreshCw className="w-7 h-7 animate-spin mx-auto text-blue-500" />
            <p className="text-sm">Loading reporting worklist...</p>
          </div>
        ) : error ? (
          <div className="p-4 rounded-xl bg-red-950/40 border border-red-900 text-center text-red-300 space-y-2">
            <AlertCircle className="w-6 h-6 mx-auto text-red-400" />
            <p className="text-sm">{error}</p>
            <button
              onClick={fetchReports}
              className="px-3 py-1 text-xs bg-red-900/60 rounded-lg hover:bg-red-800 text-white font-medium"
            >
              Retry
            </button>
          </div>
        ) : filteredReports.length === 0 ? (
          <div className="py-16 text-center border border-dashed border-[var(--border,#262c40)] rounded-2xl bg-[var(--bg-panel,#131722)]/50 p-6 space-y-3">
            <FileText className="w-10 h-10 mx-auto text-slate-600" />
            <p className="text-sm text-slate-400 font-medium">No reports found</p>
            <p className="text-xs text-slate-500 max-w-xs mx-auto">
              {searchQuery || statusFilter !== 'All'
                ? 'Try adjusting your search query or filter selection.'
                : 'Create your first radiology report to begin recording dictations.'}
            </p>
            <button
              onClick={() => setIsModalOpen(true)}
              className="mt-2 inline-flex items-center gap-1.5 px-4 py-2 rounded-xl bg-blue-600 hover:bg-blue-500 text-white text-xs font-semibold"
            >
              <Plus className="w-4 h-4" />
              <span>New Report</span>
            </button>
          </div>
        ) : (
          <div className="space-y-3" role="list">
            {filteredReports.map((report) => {
              const dictCount = report.dictations?.length || 0;
              const isPending = report.status?.toLowerCase() === 'pending';
              const isCompleted = report.status?.toLowerCase() === 'completed';

              return (
                <div
                  key={report.id}
                  role="listitem"
                  onClick={() => handleCardClick(report.id)}
                  className="group rp-report-card bg-[var(--bg-panel,#131722)] hover:bg-[var(--bg-subtle,#1a202c)] border border-[var(--border,#262c40)] hover:border-blue-500/50 rounded-2xl p-4 cursor-pointer transition-all duration-200 shadow-sm hover:shadow-md"
                >
                  <div className="flex items-start justify-between gap-3 mb-2">
                    <div>
                      <span className="inline-block px-2.5 py-0.5 rounded-md bg-blue-950/80 border border-blue-800/60 text-blue-300 font-mono font-semibold text-xs mb-1">
                        {report.radiologyId}
                      </span>
                      <h2 className="text-base font-semibold text-white group-hover:text-blue-400 transition-colors flex items-center gap-1.5">
                        <User className="w-4 h-4 text-slate-400 shrink-0" />
                        {report.patientName}
                      </h2>
                    </div>
                    {/* Status Badge */}
                    <span
                      className={`px-2.5 py-1 rounded-full text-[11px] font-semibold tracking-wide uppercase ${
                        isCompleted
                          ? 'bg-emerald-950/80 text-emerald-300 border border-emerald-800/60'
                          : isPending
                          ? 'bg-amber-950/80 text-amber-300 border border-amber-800/60'
                          : 'bg-slate-800 text-slate-300 border border-slate-700'
                      }`}
                    >
                      {report.status}
                    </span>
                  </div>

                  <div className="flex flex-wrap items-center justify-between pt-2 border-t border-[var(--border,#262c40)]/60 text-xs text-slate-400">
                    <div className="flex items-center gap-3">
                      <span>{report.patientAge} yrs • {report.patientGender}</span>
                      <span className="flex items-center gap-1 text-slate-400">
                        <Clock className="w-3.5 h-3.5 text-slate-500" />
                        {new Date(report.createdAt).toLocaleDateString()}
                      </span>
                    </div>

                    <div className="flex items-center gap-2">
                      <span className="flex items-center gap-1 text-blue-400 font-medium">
                        <Mic className="w-3.5 h-3.5" />
                        {dictCount} audio{dictCount === 1 ? '' : 's'}
                      </span>
                      <ChevronRight className="w-4 h-4 text-slate-500 group-hover:text-blue-400 group-hover:translate-x-0.5 transition-all" />
                    </div>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </main>

      {/* New Report Form Modal */}
      <NewReportModal
        isOpen={isModalOpen}
        onClose={() => setIsModalOpen(false)}
        onSuccess={fetchReports}
      />
    </div>
  );
}
