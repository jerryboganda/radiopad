import { describe, it, expect } from 'vitest';
import { encodeWavPcm16 } from '@/lib/dictation/wavEncode';

function readStr(view: DataView, off: number, len: number): string {
  let s = '';
  for (let i = 0; i < len; i++) s += String.fromCharCode(view.getUint8(off + i));
  return s;
}

describe('encodeWavPcm16', () => {
  it('writes a valid 16 kHz mono PCM16 WAV header', () => {
    const wav = encodeWavPcm16(new Float32Array([0, 1, -1, 0.5]), 16000);
    const v = new DataView(wav);
    expect(readStr(v, 0, 4)).toBe('RIFF');
    expect(readStr(v, 8, 4)).toBe('WAVE');
    expect(readStr(v, 12, 4)).toBe('fmt ');
    expect(v.getUint16(20, true)).toBe(1); // PCM
    expect(v.getUint16(22, true)).toBe(1); // mono
    expect(v.getUint32(24, true)).toBe(16000); // sample rate
    expect(v.getUint16(34, true)).toBe(16); // bits per sample
    expect(readStr(v, 36, 4)).toBe('data');
    expect(v.getUint32(40, true)).toBe(8); // 4 frames * 2 bytes
    expect(wav.byteLength).toBe(44 + 8);
  });

  it('clamps and scales float samples to int16', () => {
    const wav = encodeWavPcm16(new Float32Array([0, 1, -1, 0.5]), 16000);
    const v = new DataView(wav);
    expect(v.getInt16(44, true)).toBe(0);
    expect(v.getInt16(46, true)).toBe(32767); // +1 -> max
    expect(v.getInt16(48, true)).toBe(-32768); // -1 -> min
    expect(v.getInt16(50, true)).toBe(16383); // 0.5 * 0x7fff
  });

  it('clamps out-of-range samples', () => {
    const wav = encodeWavPcm16(new Float32Array([2, -2]), 16000);
    const v = new DataView(wav);
    expect(v.getInt16(44, true)).toBe(32767);
    expect(v.getInt16(46, true)).toBe(-32768);
  });
});
