import { AiActivityPanel } from '@radiopad/frontend';

// RC-06 AI activity rail — session log of AI jobs + last-action provenance
// facts + route/policy card. Right-rail panel width.

const provider = {
  id: 'prov_claude',
  name: 'Anthropic Claude',
  adapter: 'anthropic',
  model: 'claude-sonnet-4-5',
  endpointUrl: 'https://api.anthropic.com',
  compliance: 3, // "PHI-approved"
  enabled: true,
  priority: 1,
  apiKeyConfigured: true,
  quality: 0.92,
  retentionLabel: 'baa-30d',
};

// Fixed afternoon timestamps (epoch ms) — capture pins Date, times render stably.
const T = Date.UTC(2026, 6, 20, 13, 0, 0);

const sessionEntries = [
  {
    id: 1,
    startedAt: T + 2 * 60_000,
    action: 'Generate Draft',
    status: 'completed',
    scope: 'Findings, Impression',
    provider: 'Anthropic Claude',
    model: 'claude-sonnet-4-5',
    promptVersion: 'v12',
    latencyMs: 6400,
  },
  {
    id: 2,
    startedAt: T + 9 * 60_000,
    action: 'Generate Impression',
    status: 'failed',
    provider: 'Anthropic Claude',
    model: 'claude-sonnet-4-5',
    error: 'Provider returned 429 — rate limited. Try again in a minute.',
  },
  {
    id: 3,
    startedAt: T + 14 * 60_000,
    action: 'Rewrite (Concise)',
    status: 'completed',
    scope: 'Impression',
    provider: 'Anthropic Claude',
    model: 'claude-sonnet-4-5',
    promptVersion: 'v12',
    latencyMs: 2100,
  },
];

// Mixed session log — newest entry completed, so the "Last action" facts and
// the route/policy card both render; one failed row shows the error styling.
export const SessionLog = () => (
  <div style={{ maxWidth: 380 }}>
    <AiActivityPanel entries={sessionEntries} provider={provider} onShowProvenance={() => {}} />
  </div>
);

// A job in flight — newest entry running (spinner badge); last-action facts
// hidden while running.
export const JobRunning = () => (
  <div style={{ maxWidth: 380 }}>
    <AiActivityPanel
      entries={[
        {
          id: 1,
          startedAt: T + 20 * 60_000,
          action: 'Dictation cleanup',
          status: 'completed',
          scope: 'Findings',
          provider: 'Anthropic Claude',
          model: 'claude-sonnet-4-5',
          promptVersion: 'v12',
          latencyMs: 1800,
        },
        {
          id: 2,
          startedAt: T + 24 * 60_000,
          action: 'Generate Draft',
          status: 'running',
          scope: 'Findings, Impression',
          provider: 'Anthropic Claude',
          model: 'claude-sonnet-4-5',
        },
      ]}
      provider={provider}
      onShowProvenance={() => {}}
    />
  </div>
);

// Nothing run yet — EmptyState only (provider not yet resolved either).
export const Empty = () => (
  <div style={{ maxWidth: 380 }}>
    <AiActivityPanel entries={[]} provider={null} onShowProvenance={() => {}} />
  </div>
);
