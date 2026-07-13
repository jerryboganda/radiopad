'use client';

/**
 * Microphone permission helper for the companion.
 *
 * Companion dictation NO LONGER transcribes on the phone — the phone streams raw
 * audio to the desktop over the LAN (see {@link ./companionAudioCapture} +
 * {@link ./companionRtc}) and the desktop's on-device engine transcribes it.
 * (The old on-phone Android SpeechRecognizer is gone; its few-second endpointing
 * was the root cause of the choppy dictation.)
 *
 * All that remains here is ensuring the OS microphone (RECORD_AUDIO) permission
 * is granted before opening `getUserMedia`. On the native Capacitor shell we drive
 * the grant through `@capacitor-community/speech-recognition`, which also owns the
 * `RECORD_AUDIO` manifest-merge entry the generated Android project relies on. In
 * a plain browser, `getUserMedia` prompts on its own.
 */

import { isNativeCapacitorPlatform } from './nativeRuntime';

export async function ensureMicPermission(): Promise<boolean> {
  if (!isNativeCapacitorPlatform()) return true; // browser prompts via getUserMedia
  try {
    // eslint-disable-next-line @typescript-eslint/no-explicit-any
    const mod: any = await import('@capacitor-community/speech-recognition');
    const SR = mod.SpeechRecognition;
    let perm = await SR.checkPermissions().catch(() => null);
    if (perm?.speechRecognition !== 'granted') {
      perm = await SR.requestPermissions().catch(() => null);
    }
    return perm?.speechRecognition === 'granted';
  } catch {
    return true; // plugin unavailable — let getUserMedia try and surface its own error
  }
}
