// AI-generated text MUST wear `.ai-mark` (purple family) until the
// radiologist acknowledges it. This test renders an AI subtree and
// confirms that the class persists across nested children, then drops
// off after `acknowledge()` is invoked.
import { describe, it, expect } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import * as React from 'react';

function AiSnippet({ initialAcknowledged = false, children }: { initialAcknowledged?: boolean; children: React.ReactNode }) {
  const [ack, setAck] = React.useState(initialAcknowledged);
  return (
    <div data-testid="wrapper">
      <span className={ack ? '' : 'ai-mark'} data-testid="ai">
        {children}
      </span>
      <button type="button" onClick={() => setAck(true)} data-testid="ack">
        Acknowledge
      </button>
    </div>
  );
}

describe('ai-mark', () => {
  it('wraps the rendered subtree until acknowledge is called', () => {
    const { getByTestId } = render(
      <AiSnippet>
        <em>drafted</em> impression
      </AiSnippet>,
    );
    const ai = getByTestId('ai');
    expect(ai.classList.contains('ai-mark')).toBe(true);
    expect(ai.querySelector('em')?.textContent).toBe('drafted');

    fireEvent.click(getByTestId('ack'));
    expect(ai.classList.contains('ai-mark')).toBe(false);
  });

  it('treats already-acknowledged content as plain prose', () => {
    const { getByTestId } = render(
      <AiSnippet initialAcknowledged>signed prose</AiSnippet>,
    );
    expect(getByTestId('ai').classList.contains('ai-mark')).toBe(false);
  });
});
