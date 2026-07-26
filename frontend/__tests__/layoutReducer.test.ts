// Report Templates (REPORT-TEMPLATES) — the designer's undo/redo history reducer.
import { describe, it, expect } from 'vitest';
import { createDesignerState, designerReducer } from '@/lib/reportLayouts/layoutReducer';
import { createEmptyLayout, newBlockOfType } from '@/lib/reportLayouts/schema';

describe('designerReducer', () => {
  it('starts clean: not dirty, empty history', () => {
    const state = createDesignerState(createEmptyLayout());
    expect(state.dirty).toBe(false);
    expect(state.past).toEqual([]);
    expect(state.future).toEqual([]);
  });

  it('add-block appends the block, selects it, marks dirty, and records history', () => {
    const initial = createDesignerState(createEmptyLayout());
    const block = newBlockOfType('divider');
    const next = designerReducer(initial, { type: 'add-block', block });

    expect(next.present.blocks.at(-1)).toEqual(block);
    expect(next.selectedBlockId).toBe(block.id);
    expect(next.dirty).toBe(true);
    expect(next.past).toHaveLength(1);
    expect(next.past[0]).toBe(initial.present);
  });

  it('update-block patches only the matching block', () => {
    const initial = createDesignerState(createEmptyLayout());
    const target = initial.present.blocks[0];
    const next = designerReducer(initial, {
      type: 'update-block',
      id: target.id,
      patch: { showAccentRule: true } as never,
    });
    const updated = next.present.blocks.find((b) => b.id === target.id);
    expect(updated).toMatchObject({ showAccentRule: true });
    // Every other block is untouched.
    const others = next.present.blocks.filter((b) => b.id !== target.id);
    const originalOthers = initial.present.blocks.filter((b) => b.id !== target.id);
    expect(others).toEqual(originalOthers);
  });

  it('remove-block drops the block and clears selection if it was selected', () => {
    const initial = createDesignerState(createEmptyLayout());
    const block = newBlockOfType('divider');
    const withBlock = designerReducer(initial, { type: 'add-block', block });
    expect(withBlock.selectedBlockId).toBe(block.id);

    const removed = designerReducer(withBlock, { type: 'remove-block', id: block.id });
    expect(removed.present.blocks.find((b) => b.id === block.id)).toBeUndefined();
    expect(removed.selectedBlockId).toBeNull();
  });

  it('move-block reorders without adding or losing blocks', () => {
    const initial = createDesignerState(createEmptyLayout());
    const ids = initial.present.blocks.map((b) => b.id);
    const moved = designerReducer(initial, { type: 'move-block', id: ids[0], toIndex: ids.length - 1 });
    expect(moved.present.blocks.map((b) => b.id).sort()).toEqual([...ids].sort());
    expect(moved.present.blocks.at(-1)?.id).toBe(ids[0]);
  });

  it('select does not push history or mark dirty', () => {
    const initial = createDesignerState(createEmptyLayout());
    const next = designerReducer(initial, { type: 'select', id: initial.present.blocks[0].id });
    expect(next.dirty).toBe(false);
    expect(next.past).toEqual([]);
    expect(next.selectedBlockId).toBe(initial.present.blocks[0].id);
  });

  it('undo/redo round-trip restores exact prior and next states', () => {
    const initial = createDesignerState(createEmptyLayout());
    const block = newBlockOfType('text');
    const afterAdd = designerReducer(initial, { type: 'add-block', block });

    const afterUndo = designerReducer(afterAdd, { type: 'undo' });
    expect(afterUndo.present).toBe(initial.present);
    expect(afterUndo.future).toHaveLength(1);

    const afterRedo = designerReducer(afterUndo, { type: 'redo' });
    expect(afterRedo.present).toBe(afterAdd.present);
    expect(afterRedo.future).toEqual([]);
  });

  it('undo on empty history is a no-op', () => {
    const initial = createDesignerState(createEmptyLayout());
    const next = designerReducer(initial, { type: 'undo' });
    expect(next).toBe(initial);
  });

  it('redo on empty future is a no-op', () => {
    const initial = createDesignerState(createEmptyLayout());
    const next = designerReducer(initial, { type: 'redo' });
    expect(next).toBe(initial);
  });

  it('a new edit after undo discards the redo branch', () => {
    const initial = createDesignerState(createEmptyLayout());
    const first = designerReducer(initial, { type: 'add-block', block: newBlockOfType('divider') });
    const undone = designerReducer(first, { type: 'undo' });
    expect(undone.future).toHaveLength(1);

    const second = designerReducer(undone, { type: 'add-block', block: newBlockOfType('text') });
    expect(second.future).toEqual([]);
  });

  it('history is capped at 50 entries', () => {
    let state = createDesignerState(createEmptyLayout());
    for (let i = 0; i < 60; i++) {
      state = designerReducer(state, { type: 'add-block', block: newBlockOfType('divider') });
    }
    expect(state.past.length).toBeLessThanOrEqual(50);
  });

  it('mark-saved clears dirty without touching history', () => {
    const initial = createDesignerState(createEmptyLayout());
    const dirty = designerReducer(initial, { type: 'add-block', block: newBlockOfType('divider') });
    expect(dirty.dirty).toBe(true);
    const saved = designerReducer(dirty, { type: 'mark-saved' });
    expect(saved.dirty).toBe(false);
    expect(saved.past).toEqual(dirty.past);
    expect(saved.present).toBe(dirty.present);
  });

  it('replace-all swaps the whole document and records history', () => {
    const initial = createDesignerState(createEmptyLayout());
    const fresh = createEmptyLayout();
    const next = designerReducer(initial, { type: 'replace-all', layout: fresh });
    expect(next.present).toBe(fresh);
    expect(next.past).toHaveLength(1);
  });
});
