'use client';

import type { ReportLayoutJson } from '@/lib/reportLayouts/schema';
import { useDesignerZoom } from '@/lib/reportLayouts/prefs';
import LayoutPaper from '@/components/reportLayouts/LayoutPaper';

export interface LayoutCanvasProps {
  layout: ReportLayoutJson;
  selectedBlockId: string | null;
  onSelectBlock: (id: string) => void;
}

/**
 * The fixed-width paper canvas MUST live inside an `overflow: auto` wrapper —
 * the CI layout-regression audit (`frontend/e2e/layout-audit.spec.ts`) fails
 * any page whose content drags the document sideways, and a true-size Letter/A4
 * page (612–792px) is exactly the shape that would trip it without a scroll
 * container of its own. See docs/02-design/design.md §3.2 / §4.10.
 */
export default function LayoutCanvas({ layout, selectedBlockId, onSelectBlock }: LayoutCanvasProps) {
  const [zoom] = useDesignerZoom();

  return (
    <div className="rp-rl-canvas-scroll">
      <div className="rp-rl-canvas-inner">
        <LayoutPaper
          layout={layout}
          scale={zoom / 100}
          selectedBlockId={selectedBlockId}
          onSelectBlock={onSelectBlock}
          interactive
          clip={false}
        />
      </div>
    </div>
  );
}
