/** Save an exported report in desktop and browser runtimes. */

type TauriInvoke = (command: string, args?: unknown) => Promise<unknown>;

function tauriInvoke(): TauriInvoke | undefined {
  if (typeof window === 'undefined') return undefined;
  const tauri = (window as typeof window & {
    __TAURI__?: { core?: { invoke?: TauriInvoke }; invoke?: TauriInvoke };
  }).__TAURI__;
  return tauri?.core?.invoke ?? tauri?.invoke;
}

function blobBytes(blob: Blob): Promise<number[]> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onerror = () => reject(reader.error ?? new Error('Could not read export data.'));
    reader.onload = () => {
      if (!(reader.result instanceof ArrayBuffer)) {
        reject(new Error('Could not read export data.'));
        return;
      }
      resolve(Array.from(new Uint8Array(reader.result)));
    };
    reader.readAsArrayBuffer(blob);
  });
}

export async function saveDownload(blob: Blob, fileName: string): Promise<'saved' | 'cancelled'> {
  const invoke = tauriInvoke();
  if (invoke) {
    const bytes = await blobBytes(blob);
    const saved = await invoke('save_export_file', { fileName, bytes });
    return saved === false ? 'cancelled' : 'saved';
  }

  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  anchor.hidden = true;
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();
  // Some engines consume the URL after click() returns. Immediate revocation
  // silently cancels an otherwise valid download.
  window.setTimeout(() => URL.revokeObjectURL(url), 1_000);
  return 'saved';
}
