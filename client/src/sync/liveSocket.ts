import type { ServerFrame, WebMessage } from '../api/types';

export type LiveStatus = 'connecting' | 'connected' | 'reconnecting' | 'rejected' | 'stopped';

/** The close code the server uses for a missing, unknown or revoked token. */
export const POLICY_VIOLATION = 1008;

const BASE_DELAY_MS = 1_000;
const MAX_DELAY_MS = 30_000;

/** The slice of the WebSocket API this class uses, so tests can supply a fake. */
export interface SocketLike {
  onopen: ((event: unknown) => void) | null;
  onmessage: ((event: { data: unknown }) => void) | null;
  onclose: ((event: { code: number }) => void) | null;
  onerror: ((event: unknown) => void) | null;
  send(data: string): void;
  close(code?: number): void;
}

export interface LiveSocketOptions {
  url: string;
  token: string;
  /** Read on every (re)connect, so the server resumes exactly where this device stopped. */
  getCursor: () => number;
  onMessage: (message: WebMessage) => void;
  onStatus: (status: LiveStatus) => void;
  createSocket?: (url: string) => SocketLike;
  random?: () => number;
  setTimer?: (callback: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

/**
 * The receive side of the chat. Sending never goes through here: it is POST /api/messages,
 * which is idempotent and retried by the outbox.
 */
export class LiveSocket {
  private socket: SocketLike | null = null;
  private timer: unknown = null;
  private attempt = 0;
  private stopped = true;

  private readonly createSocket: (url: string) => SocketLike;
  private readonly random: () => number;
  private readonly setTimer: (callback: () => void, ms: number) => unknown;
  private readonly clearTimer: (handle: unknown) => void;

  constructor(private readonly options: LiveSocketOptions) {
    this.createSocket = options.createSocket ?? ((url) => new WebSocket(url) as unknown as SocketLike);
    this.random = options.random ?? Math.random;
    this.setTimer = options.setTimer ?? ((callback, ms) => setTimeout(callback, ms));
    this.clearTimer = options.clearTimer ?? ((handle) => clearTimeout(handle as ReturnType<typeof setTimeout>));
  }

  start(): void {
    if (!this.stopped) {
      return;
    }
    this.stopped = false;
    this.attempt = 0;
    this.connect('connecting');
  }

  stop(): void {
    this.stopped = true;
    this.cancelTimer();
    this.dropSocket();
    this.options.onStatus('stopped');
  }

  /** Delay before reconnect attempt n (0-based): exponential, capped, with jitter so devices spread out. */
  static backoffDelay(attempt: number, random: () => number): number {
    const ceiling = Math.min(MAX_DELAY_MS, BASE_DELAY_MS * 2 ** attempt);
    return Math.round(ceiling * (0.5 + random() * 0.5));
  }

  private connect(status: 'connecting' | 'reconnecting'): void {
    this.options.onStatus(status);

    let socket: SocketLike;
    try {
      socket = this.createSocket(this.options.url);
    } catch {
      this.scheduleReconnect();
      return;
    }
    this.socket = socket;

    socket.onopen = () => {
      socket.send(JSON.stringify({ type: 'auth', token: this.options.token, after: this.options.getCursor() }));
    };

    socket.onmessage = (event) => {
      const frame = parseFrame(event.data);
      if (frame?.type === 'ready') {
        this.attempt = 0;
        this.options.onStatus('connected');
      } else if (frame?.type === 'message') {
        this.options.onMessage(frame.message);
      }
    };

    socket.onclose = (event) => {
      if (this.socket !== socket) {
        return;
      }
      this.socket = null;

      if (this.stopped) {
        return;
      }

      if (event.code === POLICY_VIOLATION) {
        // The token is gone. Reconnecting would just be refused again.
        this.stopped = true;
        this.options.onStatus('rejected');
        return;
      }

      this.scheduleReconnect();
    };

    // An error is always followed by a close, which does the reconnecting.
    socket.onerror = () => {};
  }

  private scheduleReconnect(): void {
    if (this.stopped) {
      return;
    }
    this.options.onStatus('reconnecting');
    const delay = LiveSocket.backoffDelay(this.attempt, this.random);
    this.attempt += 1;
    this.timer = this.setTimer(() => {
      this.timer = null;
      if (!this.stopped) {
        this.connect('reconnecting');
      }
    }, delay);
  }

  private cancelTimer(): void {
    if (this.timer !== null) {
      this.clearTimer(this.timer);
      this.timer = null;
    }
  }

  private dropSocket(): void {
    const socket = this.socket;
    this.socket = null;
    if (socket) {
      socket.onclose = null;
      socket.onmessage = null;
      socket.close();
    }
  }
}

function parseFrame(data: unknown): ServerFrame | null {
  if (typeof data !== 'string') {
    return null;
  }
  try {
    const frame = JSON.parse(data) as ServerFrame;
    return frame && typeof frame === 'object' && 'type' in frame ? frame : null;
  } catch {
    return null;
  }
}
