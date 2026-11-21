// In-app replacement for window.confirm (audit UIUX-DEST-001): a native modal
// <dialog>, so focus trapping, Esc-to-cancel and the backdrop come from the
// platform. Focus starts on Cancel because every caller guards a destructive or
// irreversible action. Styled by `.rp-confirm` in radiopad.css (tokens only).

/** Resolves true only when the user presses the confirm button. */
export function confirmDialog(message: string, confirmLabel = 'Confirm'): Promise<boolean> {
  const dialog = document.createElement('dialog');
  // jsdom (unit tests) and very old webviews have no modal <dialog>.
  if (typeof dialog.showModal !== 'function') return Promise.resolve(window.confirm(message));

  dialog.className = 'rp-confirm';
  dialog.setAttribute('aria-label', 'Please confirm');

  const text = document.createElement('p');
  text.textContent = message;

  const actions = document.createElement('div');
  actions.className = 'rp-confirm-actions';
  const cancel = document.createElement('button');
  cancel.type = 'button';
  cancel.className = 'ghost';
  cancel.textContent = 'Cancel';
  cancel.onclick = () => dialog.close('cancel');
  const ok = document.createElement('button');
  ok.type = 'button';
  ok.className = 'primary';
  ok.textContent = confirmLabel;
  ok.onclick = () => dialog.close('ok');
  actions.append(cancel, ok);
  dialog.append(text, actions);

  return new Promise((resolve) => {
    dialog.addEventListener('close', () => {
      dialog.remove();
      resolve(dialog.returnValue === 'ok');
    });
    document.body.append(dialog);
    dialog.showModal();
    cancel.focus();
  });
}
