'use client';

import { useState } from 'react';
import Container from '@/components/shell/Container';
import PageHeader from '@/components/shell/PageHeader';
import Banner, { type BannerTone } from '@/components/ui/Banner';
import AnimatedNumber from '@/components/ui/AnimatedNumber';
import Reveal from '@/components/ui/Reveal';
import { ToastProvider, useToast } from '@/components/ui/ToastProvider';

/**
 * Dev-only motion showcase (/design/motion).
 * Renders every motion token, keyframe utility, and the motion-driven
 * components so designers/auditors can verify the system — and confirm
 * reduced-motion behavior — without grepping CSS.
 */

const ENTRANCES = [
  'rp-anim-fade-in',
  'rp-anim-fade-in-up',
  'rp-anim-fade-in-down',
  'rp-anim-scale-in',
  'rp-anim-pop-in',
  'rp-anim-slide-left',
  'rp-anim-slide-right',
  'rp-anim-spring-in',
];

const EASINGS = ['--ease-out', '--ease-in', '--ease-in-out', '--ease-pop', '--ease-snap', '--ease-spring', '--ease-overshoot'];
const TONES: BannerTone[] = ['info', 'success', 'warn', 'danger', 'ai'];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="rp-panel" style={{ display: 'grid', gap: 'var(--space-4)' }}>
      <h2 style={{ margin: 0, fontSize: 'var(--text-lg)' }}>{title}</h2>
      {children}
    </section>
  );
}

function ToastDemo() {
  const { toast } = useToast();
  return (
    <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-2)' }}>
      {TONES.map((tone) => (
        <button
          key={tone}
          type="button"
          className="ghost"
          onClick={() => toast({ tone, title: `${tone} toast`, message: 'This is a sample notification.' })}
        >
          Toast: {tone}
        </button>
      ))}
    </div>
  );
}

export default function MotionShowcasePage() {
  const [replay, setReplay] = useState(0);
  const [n, setN] = useState(1280);

  return (
    <ToastProvider>
      <Container>
        <PageHeader
          title="Motion system"
          description="Tokens, keyframes, and motion-driven components. Toggle OS “reduce motion” to verify graceful degradation."
          primaryAction={
            <button type="button" className="primary" onClick={() => setReplay((r) => r + 1)}>
              Replay entrances
            </button>
          }
        />

        <div style={{ display: 'grid', gap: 'var(--space-5)' }}>
          <Section title="Entrance animations">
            <div key={replay} className="rp-stagger" style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-3)' }}>
              {ENTRANCES.map((cls) => (
                <div
                  key={cls}
                  className={cls}
                  style={{
                    padding: 'var(--space-3) var(--space-4)',
                    background: 'var(--bg-subtle)',
                    border: '1px solid var(--border)',
                    borderRadius: 'var(--radius)',
                    fontFamily: 'var(--mono)',
                    fontSize: 'var(--text-xs)',
                  }}
                >
                  {cls.replace('rp-anim-', '')}
                </div>
              ))}
            </div>
          </Section>

          <Section title="Easing curves">
            <div style={{ display: 'flex', flexWrap: 'wrap', gap: 'var(--space-2)' }}>
              {EASINGS.map((e) => (
                <code key={e} className="status-badge" data-tone="muted">{e}</code>
              ))}
            </div>
          </Section>

          <Section title="Looping helpers">
            <div style={{ display: 'flex', alignItems: 'center', gap: 'var(--space-5)' }}>
              <span className="rp-motion-spin" style={{ display: 'inline-block', width: 22, height: 22, border: '2px solid var(--border)', borderTopColor: 'var(--accent)', borderRadius: '50%' }} />
              <span className="rp-motion-pulse" style={{ width: 14, height: 14, borderRadius: '50%', background: 'var(--accent)' }} />
              <span className="rp-motion-glow" style={{ width: 14, height: 14, borderRadius: '50%', background: 'var(--accent)' }} />
            </div>
          </Section>

          <Section title="Animated number (count-up)">
            <div style={{ display: 'flex', alignItems: 'baseline', gap: 'var(--space-4)' }}>
              <strong style={{ fontFamily: 'var(--font-display)', fontSize: 'var(--text-2xl)' }}>
                <AnimatedNumber value={n} />
              </strong>
              <button type="button" className="ghost" onClick={() => setN(Math.round(Math.max(0, n + 500)))}>+500</button>
              <button type="button" className="ghost" onClick={() => setN(Math.round(Math.max(0, n - 500)))}>-500</button>
            </div>
          </Section>

          <Section title="Banners">
            <div style={{ display: 'grid', gap: 'var(--space-2)' }}>
              {TONES.map((tone) => (
                <Banner key={tone} tone={tone} title={`${tone} banner`} onDismiss={() => {}}>
                  A standardized banner using the locked semantic palette.
                </Banner>
              ))}
            </div>
          </Section>

          <Section title="Toasts">
            <ToastDemo />
          </Section>

          <Section title="Scroll reveal">
            <Reveal animation="fade-in-up">
              <p style={{ margin: 0 }}>This block reveals on scroll via IntersectionObserver.</p>
            </Reveal>
          </Section>
        </div>
      </Container>
    </ToastProvider>
  );
}
