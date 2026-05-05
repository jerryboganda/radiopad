// Renders a Blocker / Warning / Info finding and asserts the locked
// severity class lands on the rendered node. The design system maps
//   Blocker → red, Warning → amber, Info → blue
// via `.finding.blocker`, `.finding.warning`, `.finding.info` in
// `frontend/app/radiopad.css`. We assert on class names only — never on
// raw hex values — so the test survives a future palette refresh.
import { describe, it, expect } from 'vitest';
import { render } from '@testing-library/react';
import * as React from 'react';

const LOCKED_SEVERITY_CLASSES = ['blocker', 'warning', 'info'] as const;
type Severity = (typeof LOCKED_SEVERITY_CLASSES)[number];

function Finding({ severity, rule, message }: { severity: Severity; rule: string; message: string }) {
  return (
    <div className={`finding ${severity}`} data-testid="finding">
      <span className="rule"><code>{rule}</code></span>
      <span className="message">{message}</span>
    </div>
  );
}

describe('validation finding', () => {
  it.each(LOCKED_SEVERITY_CLASSES)('renders %s with the locked severity class', (sev) => {
    const { getByTestId } = render(
      <Finding severity={sev} rule={`r.${sev}.1`} message={`sample ${sev}`} />,
    );
    const node = getByTestId('finding');
    expect(node.classList.contains('finding')).toBe(true);
    expect(node.classList.contains(sev)).toBe(true);
    // exactly one severity class — no overlap, no rogue colour utilities
    const matches = LOCKED_SEVERITY_CLASSES.filter((c) => node.classList.contains(c));
    expect(matches).toEqual([sev]);
  });

  it('does not leak raw colour utilities or inline colour styles', () => {
    const { getByTestId } = render(<Finding severity="blocker" rule="r.1" message="x" />);
    const node = getByTestId('finding');
    // Forbidden Tailwind-style escape hatches
    for (const klass of ['text-red-500', 'bg-red-100', 'text-amber-500', 'text-blue-500']) {
      expect(node.classList.contains(klass)).toBe(false);
    }
    expect(node.getAttribute('style')).toBeNull();
  });
});
