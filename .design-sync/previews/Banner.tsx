import { Banner } from '@radiopad/frontend';

// Every semantic tone, stacked the way pages actually use them.
export const AllTones = () => (
  <div style={{ display: 'flex', flexDirection: 'column', gap: 10, maxWidth: 560 }}>
    <Banner tone="info">Rulebook chest_ct_v1 bound to this study.</Banner>
    <Banner tone="success">Report exported to PACS.</Banner>
    <Banner tone="warn">2 validation warnings — review before signing.</Banner>
    <Banner tone="danger">Critical result: notify the referring physician.</Banner>
    <Banner tone="ai">Impression drafted by AI — requires review.</Banner>
  </div>
);

// Title + body + dismiss — the full-featured shape.
export const WithTitleAndDismiss = () => (
  <div style={{ maxWidth: 560 }}>
    <Banner
      tone="warn"
      title="Validation found 2 warnings"
      onDismiss={() => {}}
    >
      Laterality mismatch in Findings; measurement unit missing in Impression.
      Resolve or acknowledge before export.
    </Banner>
  </div>
);

// AI provenance banner — the .ai treatment pages show for generated content.
export const AiProvenance = () => (
  <div style={{ maxWidth: 560 }}>
    <Banner tone="ai" title="AI-generated draft" onDismiss={() => {}}>
      The Impression section was drafted from Findings. Review and edit before
      acknowledging — RadioPad never signs reports automatically.
    </Banner>
  </div>
);
