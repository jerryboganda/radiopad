import { describe, it, expect } from 'vitest';
import {
  encodeCompanionPairing,
  decodeCompanionPairing,
  type CompanionPairingPayload,
} from '@/lib/companionPairing';

const sample: CompanionPairingPayload = {
  base: 'https://radiopadstudio.com',
  code: '3XAXEF',
  token: 'rp_abc123.def456',
  tenant: 'dev',
  user: 'rad@example.com',
};

describe('companion pairing codec', () => {
  it('round-trips a payload through encode → decode', () => {
    const decoded = decodeCompanionPairing(encodeCompanionPairing(sample));
    expect(decoded).toEqual(sample);
  });

  it('trims surrounding whitespace from a scanned string', () => {
    const decoded = decodeCompanionPairing(`  ${encodeCompanionPairing(sample)}\n`);
    expect(decoded).toEqual(sample);
  });

  it('rejects non-JSON / plain text (e.g. a legacy bare code QR)', () => {
    expect(decodeCompanionPairing('3XAXEF')).toBeNull();
    expect(decodeCompanionPairing('')).toBeNull();
    expect(decodeCompanionPairing('not a qr')).toBeNull();
  });

  it('rejects a foreign QR that happens to be JSON', () => {
    expect(decodeCompanionPairing('{"foo":"bar"}')).toBeNull();
    expect(decodeCompanionPairing(JSON.stringify({ k: 'something-else', v: 1 }))).toBeNull();
  });

  it('rejects a wrong protocol version (forward-compat guard)', () => {
    const wire = JSON.parse(encodeCompanionPairing(sample));
    wire.v = 99;
    expect(decodeCompanionPairing(JSON.stringify(wire))).toBeNull();
  });

  it('rejects a payload missing a required field (e.g. no token)', () => {
    const wire = JSON.parse(encodeCompanionPairing(sample));
    delete wire.t;
    expect(decodeCompanionPairing(JSON.stringify(wire))).toBeNull();
  });
});
