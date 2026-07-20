import { Banner, Reveal } from '@radiopad/frontend';

// A dashboard panel entering with the default fade-in-up. Rendered at the top
// of the page, the IntersectionObserver fires immediately, so the capture
// shows the settled (fully revealed) state.
export const FadeInUpPanel = () => (
  <Reveal animation="fade-in-up">
    <div
      style={{
        maxWidth: 520,
        border: '1px solid var(--border)',
        borderRadius: 10,
        background: 'var(--bg-panel)',
        padding: 16,
      }}
    >
      <div style={{ fontSize: 13, fontWeight: 600, color: 'var(--text-strong)' }}>Today&rsquo;s reporting</div>
      <p style={{ margin: '8px 0 0', fontSize: 13, color: 'var(--text-muted)' }}>
        14 studies signed, 3 awaiting validation. Median turnaround 22 minutes across CT and plain film.
      </p>
    </div>
  </Reveal>
);

// A delay cascade — stat tiles staggering in 0 / 120 / 240 ms apart, the way
// the dashboard reveals its KPI row.
export const StaggeredStats = () => {
  const stats = [
    { label: 'Reports signed', value: '128', tone: 'var(--green)' },
    { label: 'Awaiting validation', value: '9', tone: 'var(--amber)' },
    { label: 'Critical results', value: '2', tone: 'var(--red)' },
  ];
  return (
    <div style={{ display: 'flex', gap: 12, maxWidth: 560 }}>
      {stats.map((s, i) => (
        <Reveal key={s.label} animation="fade-in-up" delay={i * 120} style={{ flex: 1 }}>
          <div
            style={{
              border: '1px solid var(--border)',
              borderRadius: 10,
              background: 'var(--bg-panel)',
              padding: '12px 14px',
            }}
          >
            <div style={{ fontSize: 22, fontWeight: 700, color: s.tone }}>{s.value}</div>
            <div style={{ fontSize: 12, color: 'var(--text-muted)', marginTop: 2 }}>{s.label}</div>
          </div>
        </Reveal>
      ))}
    </div>
  );
};

// scale-in wrapping an AI provenance banner — how generated-content notices
// pop into the editor once a draft lands.
export const ScaleInNotice = () => (
  <Reveal animation="scale-in">
    <div style={{ maxWidth: 520 }}>
      <Banner tone="ai" title="Impression drafted by AI">
        Drafted from Findings — review and edit before acknowledging.
      </Banner>
    </div>
  </Reveal>
);
