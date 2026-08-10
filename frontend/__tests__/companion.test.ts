import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { connectCompanion } from '../lib/companion';

vi.mock('../lib/api', () => ({
  companionWsBase: () => 'wss://admin.radiopadstudio.com',
  getActiveAuthToken: () => 'rp_test-token',
}));

class FakeWebSocket {
  static readonly OPEN = 1;
  static readonly CONNECTING = 0;
  static readonly CLOSED = 3;
  static instances: FakeWebSocket[] = [];

  readonly sent: string[] = [];
  readyState = FakeWebSocket.CONNECTING;
  private readonly listeners = new Map<string, Array<(event: unknown) => void>>();

  constructor(readonly url: string) {
    FakeWebSocket.instances.push(this);
  }

  addEventListener(type: string, listener: (event: unknown) => void): void {
    const current = this.listeners.get(type) ?? [];
    current.push(listener);
    this.listeners.set(type, current);
  }

  send(payload: string): void {
    this.sent.push(payload);
  }

  close(): void {
    this.readyState = FakeWebSocket.CLOSED;
    this.emit('close', {});
  }

  open(): void {
    this.readyState = FakeWebSocket.OPEN;
    this.emit('open', {});
  }

  drop(): void {
    this.readyState = FakeWebSocket.CLOSED;
    this.emit('close', {});
  }

  private emit(type: string, event: unknown): void {
    for (const listener of this.listeners.get(type) ?? []) listener(event);
  }
}

describe('companion relay connection', () => {
  beforeEach(() => {
    FakeWebSocket.instances = [];
    vi.stubGlobal('WebSocket', FakeWebSocket);
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
  });

  it('keeps an idle relay alive and reconnects after a transient close', async () => {
    const onClose = vi.fn();
    const connection = connectCompanion({
      sessionId: 'session-1',
      role: 'companion',
      onClose,
    });
    const first = FakeWebSocket.instances[0];

    first.open();
    expect(first.sent[0]).toContain('"type":"hello"');

    await vi.advanceTimersByTimeAsync(20_000);
    expect(first.sent).toContain('{"type":"ping"}');

    first.drop();
    expect(onClose).not.toHaveBeenCalled();

    await vi.advanceTimersByTimeAsync(1_000);
    const replacement = FakeWebSocket.instances[1];
    expect(replacement).toBeDefined();
    replacement.open();
    expect(replacement.sent[0]).toContain('"type":"hello"');

    connection.close();
    expect(onClose).not.toHaveBeenCalled();
  });
});
