// The composer respects the locked `.composer-shell` markup and the
// single `.primary` button is disabled when the textarea is empty.
import { describe, it, expect, vi } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import * as React from 'react';

function Composer({ onSubmit }: { onSubmit: (text: string) => void }) {
  const [text, setText] = React.useState('');
  return (
    <div className="composer">
      <div className="composer-shell" data-testid="shell">
        <textarea
          aria-label="Compose"
          value={text}
          onChange={(e) => setText(e.target.value)}
        />
        <button
          type="button"
          className="primary"
          disabled={text.trim().length === 0}
          onClick={() => onSubmit(text)}
          data-testid="submit"
        >
          Send
        </button>
      </div>
    </div>
  );
}

describe('composer', () => {
  it('uses the locked .composer-shell markup', () => {
    const { container, getByTestId } = render(<Composer onSubmit={() => {}} />);
    expect(container.querySelector('.composer')).not.toBeNull();
    expect(getByTestId('shell').classList.contains('composer-shell')).toBe(true);
  });

  it('disables the .primary button when text is empty and enables it on input', () => {
    const onSubmit = vi.fn();
    const { getByTestId, getByLabelText, container } = render(<Composer onSubmit={onSubmit} />);

    const submit = getByTestId('submit') as HTMLButtonElement;
    expect(submit.classList.contains('primary')).toBe(true);
    expect(submit).toBeDisabled();

    // exactly one .primary button per surface
    expect(container.querySelectorAll('button.primary')).toHaveLength(1);

    fireEvent.change(getByLabelText('Compose'), { target: { value: 'hello' } });
    expect(submit).not.toBeDisabled();

    fireEvent.click(submit);
    expect(onSubmit).toHaveBeenCalledWith('hello');
  });

  it('keeps the button disabled for whitespace-only input', () => {
    const { getByTestId, getByLabelText } = render(<Composer onSubmit={() => {}} />);
    fireEvent.change(getByLabelText('Compose'), { target: { value: '   \n\t' } });
    expect(getByTestId('submit')).toBeDisabled();
  });
});
