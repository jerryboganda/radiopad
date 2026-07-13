'use client';

/**
 * Direct, LAN-only WebRTC data channel between the phone companion and the
 * desktop host, used to stream dictation AUDIO peer-to-peer so it never touches
 * the cloud. Only tiny signaling messages (SDP + ICE) ride the cloud companion
 * relay (see {@link ./companion} `sendSignal`); the audio itself flows over the
 * data channel directly between the two devices.
 *
 * `iceServers: []` — no STUN/TURN — means only host ICE candidates (local IPs)
 * are gathered, so a connection can ONLY succeed when both devices are on the
 * same local network. That is the intended "same-Wi-Fi only, audio never leaves
 * the LAN" guarantee; if they aren't, the connect watchdog fires `onFailed`.
 *
 * The DESKTOP is the offerer (it creates the data channel on `peer_joined`); the
 * PHONE answers. Audio flows phone → desktop. Segments are chunked with an 8-byte
 * header so the desktop can reassemble each self-contained webm blob in order.
 */

import type { CompanionSignal } from './companion';
import {
  encodeSegmentFrames,
  createSegmentReassembler,
  MAX_CHUNKS,
  type SegmentReassembler,
} from './companionAudioFrames';

const DEFAULT_CONNECT_TIMEOUT_MS = 12_000;

export interface RtcPeerOptions {
  role: 'host' | 'companion';
  /** Relay a signaling message to the peer (→ `conn.sendSignal`). */
  sendSignal: (signal: CompanionSignal) => void;
  /** Data channel is open and ready. */
  onOpen?: () => void;
  /** Host only: a fully reassembled audio segment arrived, in capture order. */
  onSegment?: (blob: Blob, seq: number) => void;
  onState?: (state: RTCPeerConnectionState) => void;
  /** The connection failed or never reached `connected` within the watchdog. */
  onFailed?: () => void;
  connectTimeoutMs?: number;
}

export interface RtcPeer {
  /** Feed an inbound relay signal (rtc_offer / rtc_answer / rtc_ice / rtc_bye). */
  handleSignal: (signal: CompanionSignal) => Promise<void>;
  /** Host: create the data channel + offer. */
  startAsHost: () => Promise<void>;
  /** Companion: send one captured audio segment. The seq is assigned INTERNALLY
   *  and is monotonic for the life of THIS peer/connection — matching the desktop
   *  receiver, which is likewise recreated (nextSeq→0) whenever a new peer forms.
   *  So it stays correct across mic toggles and resets cleanly on reconnect. */
  sendSegment: (blob: Blob) => Promise<void>;
  close: () => void;
  connectionState: () => RTCPeerConnectionState;
}

export function createRtcPeer(opts: RtcPeerOptions): RtcPeer {
  const pc = new RTCPeerConnection({ iceServers: [] });
  let channel: RTCDataChannel | null = null;
  let remoteSet = false;
  let closed = false;
  let sendSeq = 0; // monotonic per connection (see sendSegment)
  const pendingCandidates: RTCIceCandidateInit[] = [];
  const reassembler: SegmentReassembler = createSegmentReassembler(({ seq, bytes }) => {
    opts.onSegment?.(new Blob([bytes as BlobPart], { type: 'audio/webm' }), seq);
  });

  const timeout = opts.connectTimeoutMs ?? DEFAULT_CONNECT_TIMEOUT_MS;
  const watchdog = setTimeout(() => {
    if (!closed && pc.connectionState !== 'connected') opts.onFailed?.();
  }, timeout);

  function bindChannel(dc: RTCDataChannel) {
    channel = dc;
    dc.binaryType = 'arraybuffer';
    dc.onopen = () => opts.onOpen?.();
    dc.onmessage = (ev) => {
      if (ev.data instanceof ArrayBuffer) reassembler.onFrame(ev.data);
    };
  }

  pc.onicecandidate = (ev) => {
    opts.sendSignal({ type: 'rtc_ice', candidate: ev.candidate ? ev.candidate.toJSON() : null });
  };
  pc.onconnectionstatechange = () => {
    opts.onState?.(pc.connectionState);
    if (pc.connectionState === 'failed') opts.onFailed?.();
    if (pc.connectionState === 'connected') clearTimeout(watchdog);
  };
  pc.ondatachannel = (ev) => bindChannel(ev.channel);

  async function flushCandidates() {
    while (pendingCandidates.length) {
      const c = pendingCandidates.shift();
      try { await pc.addIceCandidate(c); } catch { /* stale/invalid candidate */ }
    }
  }

  return {
    async startAsHost() {
      const dc = pc.createDataChannel('dictation', { ordered: true });
      bindChannel(dc);
      const offer = await pc.createOffer();
      await pc.setLocalDescription(offer);
      opts.sendSignal({ type: 'rtc_offer', sdp: offer.sdp ?? '' });
    },

    async handleSignal(signal) {
      if (closed) return;
      try {
        if (signal.type === 'rtc_offer') {
          await pc.setRemoteDescription({ type: 'offer', sdp: signal.sdp });
          remoteSet = true;
          await flushCandidates();
          const answer = await pc.createAnswer();
          await pc.setLocalDescription(answer);
          opts.sendSignal({ type: 'rtc_answer', sdp: answer.sdp ?? '' });
        } else if (signal.type === 'rtc_answer') {
          await pc.setRemoteDescription({ type: 'answer', sdp: signal.sdp });
          remoteSet = true;
          await flushCandidates();
        } else if (signal.type === 'rtc_ice') {
          if (signal.candidate == null) return; // end-of-candidates marker
          if (remoteSet) {
            try { await pc.addIceCandidate(signal.candidate); } catch { /* ignore */ }
          } else {
            pendingCandidates.push(signal.candidate);
          }
        } else if (signal.type === 'rtc_bye') {
          this.close();
        }
      } catch {
        opts.onFailed?.();
      }
    },

    async sendSegment(blob) {
      const dc = channel;
      if (!dc || dc.readyState !== 'open') return; // not connected — no seq consumed
      // Reserve + advance the seq synchronously (before the async read) so two
      // segments can never collide on the same number.
      const seq = sendSeq;
      sendSeq += 1;
      const bytes = new Uint8Array(await blob.arrayBuffer());
      if (closed || dc.readyState !== 'open') return; // dropped after the read
      const frames = encodeSegmentFrames(bytes, seq);
      if (frames.length > MAX_CHUNKS) return; // absurdly large — guard the u16 fields
      for (const frame of frames) {
        try { dc.send(frame); } catch { return; }
      }
    },

    close() {
      if (closed) return;
      closed = true;
      clearTimeout(watchdog);
      reassembler.clear();
      try { channel?.close(); } catch { /* noop */ }
      try { pc.close(); } catch { /* noop */ }
    },

    connectionState() {
      return pc.connectionState;
    },
  };
}
