'use client';

import { useEffect, useMemo, useReducer, useState } from 'react';
import { useRouter } from 'next/navigation';
import { api } from '@/lib/api';
import { readQueryParam } from '@/lib/browserParams';
import { reportLayoutDesignerHref } from '@/lib/routes';
import { saveDownload } from '@/lib/saveDownload';
import { useToast } from '@/components/ui/ToastProvider';
import Container from '@/components/shell/Container';
import ErrorState from '@/components/ui/ErrorState';
import Skeleton from '@/components/ui/Skeleton';
import { createEmptyLayout, validateLayout } from '@/lib/reportLayouts/schema';
import { createDesignerState, designerReducer } from '@/lib/reportLayouts/layoutReducer';
import DesignerToolbar from './DesignerToolbar';
import BlockPalettePanel from './BlockPalettePanel';
import LayoutCanvas from './LayoutCanvas';
import InspectorPanel from './InspectorPanel';

/**
 * Report Templates (RPT-030) — the three-zone visual designer. Loaded via
 * `?id=` (static-export query-param routing — see `lib/routes.ts`); with no id
 * it starts from `createEmptyLayout()` and the first Save creates the row.
 *
 * State ownership: this component owns the single `useReducer` (undo/redo
 * history) and the load/save/download side effects; the three panels below are
 * presentational and dispatch actions upward.
 */
export default function DesignerClient() {
  const router = useRouter();
  const { toast } = useToast();

  const [layoutId, setLayoutId] = useState<string | null>(null);
  const [name, setName] = useState('Untitled design');
  const [description, setDescription] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadWarning, setLoadWarning] = useState<string | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [downloading, setDownloading] = useState<'pdf' | 'docx' | null>(null);

  const [state, dispatch] = useReducer(designerReducer, createDesignerState(createEmptyLayout()));

  useEffect(() => {
    const id = readQueryParam('id');
    if (!id) {
      setLoading(false);
      return;
    }
    setLayoutId(id);
    api.reportLayouts.get(id)
      .then((row) => {
        setName(row.name);
        setDescription(row.description ?? '');
        const parsed = validateLayout(JSON.parse(row.layoutJson || '{}'));
        if (parsed.ok) {
          dispatch({ type: 'replace-all', layout: parsed.value });
          dispatch({ type: 'mark-saved' });
        } else {
          setLoadWarning(
            `This saved design has ${parsed.errors.length} issue(s) and could not be loaded as-is; `
            + 'starting from a blank canvas. Saving here will overwrite it with a valid design.',
          );
        }
      })
      .catch((e: Error) => setLoadError(e.message))
      .finally(() => setLoading(false));
  }, []);

  // Ctrl/Cmd+Z undo, Ctrl/Cmd+Shift+Z or Ctrl+Y redo.
  useEffect(() => {
    function onKeyDown(e: KeyboardEvent) {
      if (!(e.ctrlKey || e.metaKey)) return;
      const key = e.key.toLowerCase();
      if (key === 'z' && !e.shiftKey) {
        e.preventDefault();
        dispatch({ type: 'undo' });
      } else if ((key === 'z' && e.shiftKey) || key === 'y') {
        e.preventDefault();
        dispatch({ type: 'redo' });
      }
    }
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, []);

  useEffect(() => {
    function onBeforeUnload(e: BeforeUnloadEvent) {
      if (!state.dirty) return;
      e.preventDefault();
      e.returnValue = '';
    }
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => window.removeEventListener('beforeunload', onBeforeUnload);
  }, [state.dirty]);

  const selectedBlock = useMemo(
    () => state.present.blocks.find((b) => b.id === state.selectedBlockId) ?? null,
    [state.present.blocks, state.selectedBlockId],
  );

  async function handleSave() {
    if (!name.trim()) {
      toast({ tone: 'danger', message: 'Name is required.' });
      return;
    }
    setSaving(true);
    try {
      const layoutJson = JSON.stringify(state.present);
      if (layoutId) {
        await api.reportLayouts.update(layoutId, { name: name.trim(), description: description.trim() || undefined, layoutJson });
      } else {
        const created = await api.reportLayouts.create({ name: name.trim(), description: description.trim() || undefined, layoutJson });
        setLayoutId(created.id);
        router.replace(reportLayoutDesignerHref(created.id));
      }
      dispatch({ type: 'mark-saved' });
      toast({ tone: 'success', message: 'Saved.' });
    } catch (e) {
      toast({ tone: 'danger', title: 'Save failed', message: (e as Error).message });
    } finally {
      setSaving(false);
    }
  }

  async function handleDownloadSample(format: 'pdf' | 'docx') {
    setDownloading(format);
    try {
      const layoutJson = JSON.stringify(state.present);
      const blob = format === 'pdf'
        ? await api.reportLayouts.previewPdf(layoutJson)
        : await api.reportLayouts.previewDocx(layoutJson);
      await saveDownload(blob, `report-layout-preview.${format}`);
    } catch (e) {
      toast({ tone: 'danger', title: 'Preview failed', message: (e as Error).message });
    } finally {
      setDownloading(null);
    }
  }

  if (loading) {
    return (
      <Container fluid>
        <Skeleton variant="text" width="30%" style={{ marginTop: 16 }} />
        <Skeleton variant="block" height={520} style={{ marginTop: 12 }} />
      </Container>
    );
  }

  if (loadError) {
    return (
      <Container>
        <ErrorState title="Couldn't load this report template" message={loadError} onRetry={() => window.location.reload()} />
      </Container>
    );
  }

  return (
    <Container fluid>
      <DesignerToolbar
        name={name}
        onNameChange={setName}
        description={description}
        onDescriptionChange={setDescription}
        onSave={handleSave}
        saving={saving}
        dirty={state.dirty}
        canUndo={state.past.length > 0}
        canRedo={state.future.length > 0}
        onUndo={() => dispatch({ type: 'undo' })}
        onRedo={() => dispatch({ type: 'redo' })}
        downloading={downloading}
        onDownloadPdf={() => handleDownloadSample('pdf')}
        onDownloadDocx={() => handleDownloadSample('docx')}
        warning={loadWarning}
      />

      <div className="rp-rl-designer-grid">
        <BlockPalettePanel
          blocks={state.present.blocks}
          selectedBlockId={state.selectedBlockId}
          onSelect={(id) => dispatch({ type: 'select', id })}
          onAdd={(block) => dispatch({ type: 'add-block', block })}
          onRemove={(id) => dispatch({ type: 'remove-block', id })}
          onMove={(id, toIndex) => dispatch({ type: 'move-block', id, toIndex })}
        />

        <LayoutCanvas
          layout={state.present}
          selectedBlockId={state.selectedBlockId}
          onSelectBlock={(id) => dispatch({ type: 'select', id })}
        />

        <InspectorPanel
          page={state.present.page}
          footer={state.present.footer}
          block={selectedBlock}
          onPageChange={(page) => dispatch({ type: 'set-page', page })}
          onFooterChange={(footer) => dispatch({ type: 'set-footer', footer })}
          onBlockChange={(id, patch) => dispatch({ type: 'update-block', id, patch })}
        />
      </div>
    </Container>
  );
}
