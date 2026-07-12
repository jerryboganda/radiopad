'use client';

/**
 * QR scanning for the mobile companion pairing screen.
 *
 * Two paths, chosen by runtime:
 *  - NATIVE (Capacitor Android/iOS): the ML Kit / Google code-scanner plugin
 *    (`@capacitor-mlkit/barcode-scanning`). The Google code scanner runs in a
 *    Play-Services process, so it needs no in-app camera permission and gives a
 *    reliable full-screen scanner. Dynamically imported so the web bundle never
 *    hard-depends on the native module at load.
 *  - WEB (next dev / browser fallback): `getUserMedia` + `BarcodeDetector` when
 *    present, else the pure-JS `jsQR` decoder over canvas frames. Lets the whole
 *    scan→auth→pair flow be exercised in a browser with a webcam.
 *
 * Both return a validated {@link CompanionPairingPayload} (or null) via
 * {@link decodeCompanionPairing}; anything that isn't one of our pairing QRs is
 * ignored so the scanner keeps looking instead of pairing with garbage.
 */

import { isNativeCapacitorPlatform } from './nativeRuntime';
import { decodeCompanionPairing, type CompanionPairingPayload } from './companionPairing';

/** True when a native, camera-backed scanner is available (Capacitor shell). */
export function nativeScanAvailable(): boolean {
  return isNativeCapacitorPlatform();
}

/** True when the browser can scan via camera (webcam + a QR decoder). */
export function webScanAvailable(): boolean {
  return typeof navigator !== 'undefined' && !!navigator.mediaDevices?.getUserMedia;
}

/**
 * Native scan via the ML Kit Google code scanner. Resolves to the decoded
 * payload, or null if the user cancelled or scanned a non-RadioPad code. Throws
 * only on an unexpected plugin/runtime error (the caller shows a friendly message).
 */
export async function scanNative(): Promise<CompanionPairingPayload | null> {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const mod: any = await import('@capacitor-mlkit/barcode-scanning');
  const BarcodeScanner = mod.BarcodeScanner;

  // Some devices download the Google scanner module on first use.
  try {
    const avail = await BarcodeScanner.isGoogleBarcodeScannerModuleAvailable?.();
    if (avail && avail.available === false) {
      await BarcodeScanner.installGoogleBarcodeScannerModule?.();
    }
  } catch {
    /* best effort — fall through to scan() */
  }

  const result = await BarcodeScanner.scan();
  const barcodes: Array<{ rawValue?: string; displayValue?: string }> = result?.barcodes ?? [];
  for (const b of barcodes) {
    const payload = decodeCompanionPairing(b.rawValue ?? b.displayValue ?? '');
    if (payload) return payload;
  }
  return null;
}

interface BarcodeDetectorLike {
  detect(source: CanvasImageSource): Promise<Array<{ rawValue: string }>>;
}

/**
 * Web scan loop over a live `<video>` element. Polls frames until it decodes one
 * of our pairing QRs, the signal aborts, or an error occurs. The caller owns the
 * video element (so it can show a live preview); this owns the camera stream and
 * always stops it on exit.
 */
export async function scanWebcam(
  video: HTMLVideoElement,
  signal: AbortSignal,
): Promise<CompanionPairingPayload | null> {
  const stream = await navigator.mediaDevices.getUserMedia({
    video: { facingMode: { ideal: 'environment' } },
    audio: false,
  });
  video.srcObject = stream;
  video.setAttribute('playsinline', 'true');
  video.muted = true;
  try {
    await video.play();
  } catch {
    /* autoplay can reject; frames still arrive once the stream is live */
  }

  const stop = () => {
    for (const t of stream.getTracks()) t.stop();
    try { video.srcObject = null; } catch { /* ignore */ }
  };

  try {
    // Prefer the native BarcodeDetector; else pure-JS jsQR over canvas frames.
    const Detector = (globalThis as typeof globalThis & {
      BarcodeDetector?: new (opts?: { formats?: string[] }) => BarcodeDetectorLike;
    }).BarcodeDetector;
    const detector = Detector ? new Detector({ formats: ['qr_code'] }) : null;
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    let jsQR: any = null;
    if (!detector) jsQR = (await import('jsqr')).default;

    const canvas = document.createElement('canvas');
    const ctx = canvas.getContext('2d', { willReadFrequently: true });

    while (!signal.aborted) {
      if (video.readyState >= 2 && video.videoWidth > 0) {
        let raw: string | null = null;
        try {
          if (detector) {
            const codes = await detector.detect(video);
            raw = codes[0]?.rawValue ?? null;
          } else if (ctx) {
            canvas.width = video.videoWidth;
            canvas.height = video.videoHeight;
            ctx.drawImage(video, 0, 0, canvas.width, canvas.height);
            const img = ctx.getImageData(0, 0, canvas.width, canvas.height);
            const found = jsQR(img.data, img.width, img.height, { inversionAttempts: 'dontInvert' });
            raw = found?.data ?? null;
          }
        } catch {
          /* transient decode error — keep polling */
        }
        if (raw) {
          const payload = decodeCompanionPairing(raw);
          if (payload) return payload;
        }
      }
      await new Promise((r) => setTimeout(r, 180));
    }
    return null;
  } finally {
    stop();
  }
}
