import type { WebMessage } from '../api/types';
import { LiveSocket, type LiveStatus, POLICY_VIOLATION, type SocketLike } from '../sync/liveSocket';

class FakeSocket implements SocketLike {
  onopen: SocketLike['onopen'] = null;
  onmessage: SocketLike['onmessage'] = null;
  onclose: SocketLike['onclose'] = null;
  onerror: SocketLike['onerror'] = null;
  sent: string[] = [];
  closed = false;

  constructor(readonly url: string) {}

  send(data: string) {
    this.sent.push(data);
  }

  close() {
    this.closed = true;
  }

  open() {
    this.onopen?.({});
  }

  receive(frame: object) {
    this.onmessage?.({ data: JSON.stringify(frame) });
  }

  serverClose(code: number) {
    this.onclose?.({ code });
  }
}

const message = (id: number): WebMessage => ({
  id,
  clientMessageId: null,
  direction: 'out',
  text: 'x',
  createdAt: '2026-10-26T12:00:00Z',
  inReplyToId: null,
});

function harness(cursor = 0) {
  const sockets: FakeSocket[] = [];
  const statuses: LiveStatus[] = [];
  const received: WebMessage[] = [];
  const timers: { callback: () => void; ms: number }[] = [];
  let currentCursor = cursor;

  const live = new LiveSocket({
    url: 'wss://bot.example/api/ws',
    token: 'mbt_secret',
    getCursor: () => currentCursor,
    onMessage: (m) => {
      received.push(m);
      currentCursor = m.id;
    },
    onStatus: (s) => statuses.push(s),
    createSocket: (url) => {
      const socket = new FakeSocket(url);
      sockets.push(socket);
      return socket;
    },
    random: () => 1, // no jitter: delays are the ceiling
    setTimer: (callback, ms) => {
      timers.push({ callback, ms });
      return timers.length;
    },
    clearTimer: () => {},
  });

  return { live, sockets, statuses, received, timers };
}

describe('LiveSocket', () => {
  it('authenticates in the first frame with the current cursor', () => {
    const h = harness(42);
    h.live.start();
    h.sockets[0].open();

    expect(JSON.parse(h.sockets[0].sent[0])).toEqual({ type: 'auth', token: 'mbt_secret', after: 42 });
    expect(h.sockets[0].url).not.toContain('mbt_');
  });

  it('reports connected on ready and delivers messages', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].open();
    h.sockets[0].receive({ type: 'ready', device: 'phone' });
    h.sockets[0].receive({ type: 'message', message: message(1) });

    expect(h.statuses).toEqual(['connecting', 'connected']);
    expect(h.received.map((m) => m.id)).toEqual([1]);
  });

  it('reconnects with growing delays, resuming from the latest cursor', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].open();
    h.sockets[0].receive({ type: 'message', message: message(5) });
    h.sockets[0].serverClose(1006);

    expect(h.timers.map((t) => t.ms)).toEqual([1000]);
    h.timers[0].callback();
    h.sockets[1].serverClose(1006);
    expect(h.timers.map((t) => t.ms)).toEqual([1000, 2000]);

    h.timers[1].callback();
    h.sockets[2].open();
    expect(JSON.parse(h.sockets[2].sent[0]).after).toBe(5);
  });

  it('resets the backoff once a connection is ready again', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].serverClose(1006);
    h.timers[0].callback();
    h.sockets[1].open();
    h.sockets[1].receive({ type: 'ready', device: 'phone' });
    h.sockets[1].serverClose(1006);

    expect(h.timers.map((t) => t.ms)).toEqual([1000, 1000]);
  });

  it('caps the delay at 30 seconds', () => {
    expect(LiveSocket.backoffDelay(20, () => 1)).toBe(30_000);
    expect(LiveSocket.backoffDelay(20, () => 0)).toBe(15_000);
  });

  it('stops for good when the token is rejected', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].open();
    h.sockets[0].serverClose(POLICY_VIOLATION);

    expect(h.statuses.at(-1)).toBe('rejected');
    expect(h.timers).toHaveLength(0);
  });

  it('does not reconnect after stop()', () => {
    const h = harness();
    h.live.start();
    h.live.stop();

    expect(h.sockets[0].closed).toBe(true);
    expect(h.statuses.at(-1)).toBe('stopped');
    expect(h.timers).toHaveLength(0);
  });

  it('ignores frames that are not JSON', () => {
    const h = harness();
    h.live.start();
    h.sockets[0].onmessage?.({ data: 'not json' });

    expect(h.received).toHaveLength(0);
  });
});
