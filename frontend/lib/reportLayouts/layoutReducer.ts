/**
 * Report Templates (REPORT-TEMPLATES) — designer state: the layout draft plus undo/redo
 * history (capped at 50 steps) and the currently-selected block. Explicit Save
 * only — there is no autosave, so `dirty` gates the "unsaved changes" guard and
 * the Save button's enabled state.
 */
import type { LayoutBlock, LayoutFooter, PageSetup, ReportLayoutJson } from './schema';

const HISTORY_CAP = 50;

export interface DesignerState {
  past: ReportLayoutJson[];
  present: ReportLayoutJson;
  future: ReportLayoutJson[];
  selectedBlockId: string | null;
  dirty: boolean;
}

export type DesignerAction =
  | { type: 'set-page'; page: Partial<PageSetup> }
  | { type: 'set-footer'; footer: Partial<LayoutFooter> }
  | { type: 'add-block'; block: LayoutBlock; index?: number }
  | { type: 'update-block'; id: string; patch: Partial<LayoutBlock> }
  | { type: 'remove-block'; id: string }
  | { type: 'move-block'; id: string; toIndex: number }
  | { type: 'select'; id: string | null }
  | { type: 'replace-all'; layout: ReportLayoutJson }
  | { type: 'undo' }
  | { type: 'redo' }
  | { type: 'mark-saved' };

export function createDesignerState(layout: ReportLayoutJson): DesignerState {
  return { past: [], present: layout, future: [], selectedBlockId: null, dirty: false };
}

function commit(state: DesignerState, present: ReportLayoutJson): DesignerState {
  const past = [...state.past, state.present].slice(-HISTORY_CAP);
  return { ...state, past, present, future: [], dirty: true };
}

export function designerReducer(state: DesignerState, action: DesignerAction): DesignerState {
  switch (action.type) {
    case 'set-page':
      return commit(state, { ...state.present, page: { ...state.present.page, ...action.page } });

    case 'set-footer':
      return commit(state, { ...state.present, footer: { ...state.present.footer, ...action.footer } });

    case 'add-block': {
      const blocks = [...state.present.blocks];
      const index = action.index ?? blocks.length;
      blocks.splice(index, 0, action.block);
      return {
        ...commit(state, { ...state.present, blocks }),
        selectedBlockId: action.block.id,
      };
    }

    case 'update-block': {
      const blocks = state.present.blocks.map((b) =>
        b.id === action.id ? ({ ...b, ...action.patch } as LayoutBlock) : b);
      return commit(state, { ...state.present, blocks });
    }

    case 'remove-block': {
      const blocks = state.present.blocks.filter((b) => b.id !== action.id);
      return {
        ...commit(state, { ...state.present, blocks }),
        selectedBlockId: state.selectedBlockId === action.id ? null : state.selectedBlockId,
      };
    }

    case 'move-block': {
      const blocks = [...state.present.blocks];
      const fromIndex = blocks.findIndex((b) => b.id === action.id);
      if (fromIndex === -1) return state;
      const [moved] = blocks.splice(fromIndex, 1);
      const clampedTo = Math.max(0, Math.min(action.toIndex, blocks.length));
      blocks.splice(clampedTo, 0, moved);
      return commit(state, { ...state.present, blocks });
    }

    case 'select':
      return { ...state, selectedBlockId: action.id };

    case 'replace-all':
      return commit(state, action.layout);

    case 'undo': {
      if (state.past.length === 0) return state;
      const previous = state.past[state.past.length - 1];
      return {
        ...state,
        past: state.past.slice(0, -1),
        present: previous,
        future: [state.present, ...state.future].slice(0, HISTORY_CAP),
        dirty: true,
      };
    }

    case 'redo': {
      if (state.future.length === 0) return state;
      const [next, ...rest] = state.future;
      return {
        ...state,
        past: [...state.past, state.present].slice(-HISTORY_CAP),
        present: next,
        future: rest,
        dirty: true,
      };
    }

    case 'mark-saved':
      return { ...state, dirty: false };
  }
}
