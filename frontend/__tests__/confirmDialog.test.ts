import { describe, it, expect, vi, afterEach } from 'vitest';
import { confirmDialog } from '@/lib/confirmDialog';

// jsdom has no modal <dialog>, so give the element just enough to drive the real code path.
function fakeModalDialog() {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  proto.showModal = function showModal() {};
  proto.close = function close(this: HTMLDialogElement, value?: string) {
    this.returnValue = value ?? '';
    this.dispatchEvent(new Event('close'));
  };
  return () => {
    delete proto.showModal;
    delete proto.close;
  };
}

describe('confirmDialog', () => {
  afterEach(() => vi.restoreAllMocks());

  it('falls back to window.confirm where modal dialogs are unsupported', async () => {
    const spy = vi.spyOn(window, 'confirm').mockReturnValue(true);
    await expect(confirmDialog('Delete it?')).resolves.toBe(true);
    expect(spy).toHaveBeenCalledWith('Delete it?');
  });

  it('resolves true only for the confirm button and removes itself', async () => {
    const restore = fakeModalDialog();
    try {
      const yes = confirmDialog('Delete it?', 'Delete');
      const buttons = document.querySelectorAll<HTMLButtonElement>('dialog.rp-confirm button');
      expect([...buttons].map((b) => b.textContent)).toEqual(['Cancel', 'Delete']);
      buttons[1].click();
      await expect(yes).resolves.toBe(true);
      expect(document.querySelector('dialog.rp-confirm')).toBeNull();

      const no = confirmDialog('Delete it?');
      document.querySelector<HTMLButtonElement>('dialog.rp-confirm button')!.click();
      await expect(no).resolves.toBe(false);
    } finally {
      restore();
    }
  });
});
