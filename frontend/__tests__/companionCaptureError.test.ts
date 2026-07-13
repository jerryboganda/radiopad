import { describe, it, expect } from 'vitest';
import { describeCaptureError } from '@/lib/companionAudioCapture';

describe('describeCaptureError', () => {
  it('maps NotAllowedError to an actionable permission message', () => {
    const msg = describeCaptureError(new DOMException('denied', 'NotAllowedError'));
    expect(msg).toMatch(/Settings/);
    expect(msg).toMatch(/Microphone/i);
  });

  it('treats SecurityError like a blocked-permission error', () => {
    expect(describeCaptureError(new DOMException('x', 'SecurityError'))).toMatch(/blocked/i);
  });

  it('reports a missing microphone for NotFoundError', () => {
    expect(describeCaptureError(new DOMException('x', 'NotFoundError'))).toMatch(/No microphone/i);
  });

  it('reports a busy microphone for NotReadableError', () => {
    expect(describeCaptureError(new DOMException('x', 'NotReadableError'))).toMatch(/busy/i);
  });

  it('surfaces the error name and message for unknown errors', () => {
    const msg = describeCaptureError(new DOMException('boom', 'WeirdError'));
    expect(msg).toContain('WeirdError');
    expect(msg).toContain('boom');
  });

  it('never throws on a non-error value', () => {
    expect(() => describeCaptureError(undefined)).not.toThrow();
    expect(describeCaptureError('nope')).toMatch(/Could not start the microphone/);
  });
});
