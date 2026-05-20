export type NativeCapacitorPlatform = 'ios' | 'android';

export function getNativeCapacitorPlatform(): NativeCapacitorPlatform | null {
  if (typeof globalThis === 'undefined') return null;
  const cap = (globalThis as typeof globalThis & {
    Capacitor?: {
      getPlatform?: () => string;
      isNativePlatform?: () => boolean;
    };
  }).Capacitor;

  const platform = typeof cap?.getPlatform === 'function' ? cap.getPlatform() : 'web';
  const isNative = typeof cap?.isNativePlatform === 'function'
    ? cap.isNativePlatform()
    : platform === 'ios' || platform === 'android';

  return isNative && (platform === 'ios' || platform === 'android') ? platform : null;
}

export function isNativeCapacitorPlatform(): boolean {
  return getNativeCapacitorPlatform() !== null;
}