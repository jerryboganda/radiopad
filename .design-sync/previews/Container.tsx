import { Container, PageHeader } from '@radiopad/frontend';

// Standard page composition — Container capping a worklist page at the shell
// max-width: PageHeader up top, an .rp-panel study list beneath.
export const WorklistPage = () => (
  <div style={{ maxWidth: 860 }}>
    <Container>
      <PageHeader
        title="Worklist"
        description="Studies assigned to you across Meridian Imaging — 6 awaiting a report."
        primaryAction={<button className="primary" type="button">New report</button>}
        secondaryActions={<button className="ghost" type="button">Filters</button>}
      />
      <div className="rp-panel" style={{ padding: 0, overflow: 'hidden' }}>
        {[
          { name: 'Amina Yusuf', study: 'CT Chest w/o contrast', mrn: 'MRN 004821', tone: 'stat', status: 'STAT', time: '08:42' },
          { name: 'Daniel Okafor', study: 'MRI Brain w/ contrast', mrn: 'MRN 007310', tone: 'review', status: 'In progress', time: '09:05' },
          { name: 'Priya Raman', study: 'US Abdomen complete', mrn: 'MRN 001954', tone: 'draft', status: 'Unreported', time: '09:31' },
          { name: 'Tomas Keller', study: 'XR Chest PA/LAT', mrn: 'MRN 009466', tone: 'ready', status: 'Signed', time: '07:58' },
        ].map((row, i) => (
          <div
            key={row.mrn}
            style={{
              display: 'flex',
              alignItems: 'center',
              gap: 12,
              padding: '10px 14px',
              borderTop: i === 0 ? 'none' : '1px solid var(--border-soft)',
            }}
          >
            <div style={{ minWidth: 0, flex: 1 }}>
              <div style={{ font: '600 13px/1.3 var(--sans)', color: 'var(--text-strong)' }}>
                {row.name} <span style={{ font: '500 11.5px/1.3 var(--mono)', color: 'var(--text-faint)' }}>{row.mrn}</span>
              </div>
              <div style={{ font: '400 12px/1.3 var(--sans)', color: 'var(--text-muted)' }}>{row.study}</div>
            </div>
            <span className="status-badge" data-tone={row.tone}>{row.status}</span>
            <span style={{ font: '500 11.5px/1 var(--mono)', color: 'var(--text-faint)' }}>{row.time}</span>
          </div>
        ))}
      </div>
    </Container>
  </div>
);

// Narrow variant (.rp-container.narrow via className) — a reading-width
// settings page: header + a section-block form panel.
export const NarrowSettingsPage = () => (
  <div style={{ maxWidth: 860 }}>
    <Container className="narrow">
      <PageHeader
        title="Report defaults"
        description="Boilerplate inserted into every new report for your account."
        primaryAction={<button className="primary" type="button">Save changes</button>}
      />
      <div className="rp-panel">
        <div className="section-block">
          <label htmlFor="ct-technique">Technique — CT Chest</label>
          <textarea
            id="ct-technique"
            defaultValue="Volumetric CT of the chest was performed without intravenous contrast. Coronal and sagittal reformats were reviewed."
          />
        </div>
        <div className="section-block" style={{ marginBottom: 0 }}>
          <label htmlFor="sign-off">Sign-off line</label>
          <textarea
            id="sign-off"
            defaultValue="Electronically reviewed and signed. Please correlate clinically; findings discussed where critical."
          />
        </div>
      </div>
    </Container>
  </div>
);
