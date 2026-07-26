'use client';

import { Plus } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api, type ReportLayout, type ReportLayoutListResponse } from '@/lib/api';
import { reportLayoutDesignerHref } from '@/lib/routes';
import { usePermissions } from '@/lib/permissions';
import { REPORT_LAYOUT_PRESETS } from '@/lib/reportLayouts/presets';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Skeleton from '@/components/ui/Skeleton';
import EmptyState from '@/components/ui/EmptyState';
import ErrorState from '@/components/ui/ErrorState';
import { useToast } from '@/components/ui/ToastProvider';
import LayoutCard from './LayoutCard';
import PresetPickerDialog from './PresetPickerDialog';

export default function ReportTemplatesClient() {
  const router = useRouter();
  const { toast } = useToast();
  const { can } = usePermissions();
  const canRecommend = can('tenant_settings.manage');

  const [data, setData] = useState<ReportLayoutListResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [showPresets, setShowPresets] = useState(false);
  const [busy, setBusy] = useState(false);

  const load = useCallback(() => {
    setLoading(true);
    setError(null);
    api.reportLayouts.list()
      .then(setData)
      .catch((e: Error) => setError(e.message))
      .finally(() => setLoading(false));
  }, []);

  useEffect(() => { load(); }, [load]);

  async function handleUsePreset(presetKey: string) {
    const preset = REPORT_LAYOUT_PRESETS.find((p) => p.key === presetKey);
    if (!preset) return;
    setBusy(true);
    try {
      const created = await api.reportLayouts.create({
        name: preset.name,
        description: preset.description,
        layoutJson: JSON.stringify(preset.layout),
      });
      router.push(reportLayoutDesignerHref(created.id));
    } catch (e) {
      toast({ tone: 'danger', title: "Couldn't create the layout", message: (e as Error).message });
    } finally {
      setBusy(false);
    }
  }

  async function handleDuplicate(item: ReportLayout) {
    try {
      const created = await api.reportLayouts.create({
        name: `${item.name} (copy)`,
        description: item.description ?? undefined,
        layoutJson: item.layoutJson,
      });
      toast({ tone: 'success', message: 'Duplicated.' });
      router.push(reportLayoutDesignerHref(created.id));
    } catch (e) {
      toast({ tone: 'danger', title: 'Duplicate failed', message: (e as Error).message });
    }
  }

  async function handleSetDefault(id: string) {
    try {
      await api.reportLayouts.setDefault(id);
      toast({ tone: 'success', message: 'Set as your default.' });
      load();
    } catch (e) {
      toast({ tone: 'danger', title: "Couldn't set default", message: (e as Error).message });
    }
  }

  async function handleDelete(item: ReportLayout) {
    if (!window.confirm(`Delete the layout "${item.name}"? This cannot be undone.`)) return;
    try {
      await api.reportLayouts.remove(item.id);
      toast({ tone: 'success', message: 'Deleted.' });
      load();
    } catch (e) {
      toast({ tone: 'danger', title: 'Delete failed', message: (e as Error).message });
    }
  }

  async function handleRecommend(id: string) {
    try {
      await api.tenant.settings.save({ recommendedReportLayoutId: id });
      toast({ tone: 'success', message: 'Recommended to your team.' });
      load();
    } catch (e) {
      toast({ tone: 'danger', title: "Couldn't recommend", message: (e as Error).message });
    }
  }

  async function handleClearRecommend() {
    try {
      await api.tenant.settings.save({ recommendedReportLayoutId: '' });
      toast({ tone: 'info', message: 'Recommendation cleared.' });
      load();
    } catch (e) {
      toast({ tone: 'danger', title: "Couldn't clear recommendation", message: (e as Error).message });
    }
  }

  return (
    <Container>
      <PageHeader
        title="Report Templates"
        description="Design how your signed reports look when exported to PDF or Word — your own layout, or one your team recommends."
        primaryAction={
          <button type="button" className="primary" onClick={() => setShowPresets(true)}>
            <Plus size={14} strokeWidth={1.8} aria-hidden /> New from preset
          </button>
        }
      />

      {error && !data && <ErrorState title="Couldn't load report templates" message={error} onRetry={load} />}

      {!error && loading && !data && (
        <div aria-busy="true" aria-live="polite" className="rp-rl-gallery">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="rp-panel" style={{ minHeight: 260 }}>
              <Skeleton variant="block" height={160} />
              <Skeleton variant="text" width="60%" style={{ marginTop: 12 }} />
              <Skeleton variant="text" width="40%" />
            </div>
          ))}
        </div>
      )}

      {data && data.items.length === 0 && (
        <EmptyState
          title="No report templates yet"
          description="Start from a preset to design your first output document — you can fully customize it afterward."
          action={<button type="button" className="primary" onClick={() => setShowPresets(true)}>New from preset</button>}
        />
      )}

      {data && data.items.length > 0 && (
        <div className="rp-rl-gallery">
          {data.items.map((item) => (
            <LayoutCard
              key={item.id}
              item={item}
              isRecommended={data.recommendedId === item.id}
              isMyDefault={data.myDefaultId === item.id}
              canRecommend={canRecommend}
              onEdit={() => router.push(reportLayoutDesignerHref(item.id))}
              onDuplicate={() => handleDuplicate(item)}
              onSetDefault={() => handleSetDefault(item.id)}
              onDelete={() => handleDelete(item)}
              onRecommend={() => handleRecommend(item.id)}
              onClearRecommend={handleClearRecommend}
            />
          ))}
        </div>
      )}

      {showPresets && (
        <PresetPickerDialog
          busy={busy}
          onClose={() => setShowPresets(false)}
          onChoose={handleUsePreset}
        />
      )}
    </Container>
  );
}
