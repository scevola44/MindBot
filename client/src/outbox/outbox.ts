import { RejectedError, UnauthorizedError } from '../api/client';
import type { SendResponse } from '../api/types';
import { LiveSocket } from '../sync/liveSocket';

export interface OutboxItem {
  /** Generated once, reused on every retry: the server's duplicate guard keys on it. */
  clientMessageId: string;
  text: string;
  createdAt: string;
  state: 'pending' | 'failed';
  /** Why the server refused it, for a failed item. */
  error?: string;
}

/** Async key-value storage: AsyncStorage on every platform (localStorage on web). */
export interface KeyValueStore {
  getItem(key: string): Promise<string | null>;
  setItem(key: string, value: string): Promise<void>;
}

export interface OutboxOptions {
  store: KeyValueStore;
  /** Storage key; defaults to OUTBOX_KEY. Scope it per server so messages never go to the wrong one. */
  key?: string;
  send: (clientMessageId: string, text: string) => Promise<SendResponse>;
  newId: () => string;
  onChange: (items: readonly OutboxItem[]) => void;
  onSent: (response: SendResponse) => void;
  onUnauthorized: () => void;
  now?: () => Date;
  random?: () => number;
  setTimer?: (callback: () => void, ms: number) => unknown;
  clearTimer?: (handle: unknown) => void;
}

export const OUTBOX_KEY = 'mindbot.outbox.v1';

/**
 * Messages the user has written but the server has not yet confirmed.
 *
 * Each item is written to storage before the first send, so a message survives the app being
 * closed mid-send. Items go out one at a time, oldest first: the /new flow depends on a note's
 * name arriving before its content. A send that fails because the server is unreachable is
 * retried with the same clientMessageId, which the server deduplicates, so a retry can never
 * file a second note. A send the server refuses as invalid is marked failed and skipped; it
 * would fail the same way every time.
 */
export class Outbox {
  private items: OutboxItem[] = [];
  private running = false;
  private paused = false;
  private timer: unknown = null;
  private attempt = 0;
  private persistChain: Promise<void> = Promise.resolve();

  private readonly key: string;
  private readonly now: () => Date;
  private readonly random: () => number;
  private readonly setTimer: (callback: () => void, ms: number) => unknown;
  private readonly clearTimer: (handle: unknown) => void;

  constructor(private readonly options: OutboxOptions) {
    this.key = options.key ?? OUTBOX_KEY;
    this.now = options.now ?? (() => new Date());
    this.random = options.random ?? Math.random;
    this.setTimer = options.setTimer ?? ((callback, ms) => setTimeout(callback, ms));
    this.clearTimer = options.clearTimer ?? ((handle) => clearTimeout(handle as ReturnType<typeof setTimeout>));
  }

  get snapshot(): readonly OutboxItem[] {
    return this.items;
  }

  /** Restores items left over from a previous run and starts sending them. */
  async load(): Promise<void> {
    const raw = await this.options.store.getItem(this.key);
    if (raw) {
      try {
        const stored = JSON.parse(raw) as OutboxItem[];
        if (Array.isArray(stored)) {
          this.items = stored;
        }
      } catch {
        // Unreadable storage is treated as empty rather than crashing the app.
        this.items = [];
      }
    }
    this.emit();
    void this.flush();
  }

  async enqueue(text: string): Promise<void> {
    this.items = [
      ...this.items,
      { clientMessageId: this.options.newId(), text, createdAt: this.now().toISOString(), state: 'pending' },
    ];
    this.emit();
    await this.persist();
    void this.flush();
  }

  /** Puts a failed item back in the queue (the user may have edited nothing; the server decides again). */
  async retry(clientMessageId: string): Promise<void> {
    this.items = this.items.map((item) =>
      item.clientMessageId === clientMessageId ? { ...item, state: 'pending', error: undefined } : item,
    );
    this.emit();
    await this.persist();
    void this.flush();
  }

  async discard(clientMessageId: string): Promise<void> {
    this.items = this.items.filter((item) => item.clientMessageId !== clientMessageId);
    this.emit();
    await this.persist();
  }

  /** Stops sending (after a rejected token) without losing anything. */
  pause(): void {
    this.paused = true;
    this.cancelTimer();
  }

  /** Resumes after the user re-authenticates, or when the connection comes back. */
  resume(): void {
    this.paused = false;
    this.cancelTimer();
    this.attempt = 0;
    void this.flush();
  }

  /**
   * Skips the current backoff wait, e.g. when the live connection comes back. Unlike resume(),
   * it never lifts a pause: a rejected token stays paused until the user re-authenticates.
   */
  kick(): void {
    if (this.paused) {
      return;
    }
    this.cancelTimer();
    this.attempt = 0;
    void this.flush();
  }

  dispose(): void {
    this.paused = true;
    this.cancelTimer();
  }

  /** Sends pending items in order until the queue is empty, a send fails, or the outbox is paused. */
  async flush(): Promise<void> {
    // A scheduled retry owns the next attempt: a message typed meanwhile joins the queue
    // instead of cutting the backoff short. kick() and resume() cancel the timer first.
    if (this.running || this.paused || this.timer !== null) {
      return;
    }
    this.running = true;

    try {
      while (!this.paused) {
        const next = this.items.find((item) => item.state === 'pending');
        if (!next) {
          return;
        }

        try {
          const response = await this.options.send(next.clientMessageId, next.text);
          this.attempt = 0;
          this.items = this.items.filter((item) => item.clientMessageId !== next.clientMessageId);
          // The confirmed lines go into the transcript before the pending bubble disappears,
          // so the message never visibly vanishes.
          this.options.onSent(response);
          this.emit();
          await this.persist();
        } catch (error) {
          if (error instanceof UnauthorizedError) {
            this.pause();
            this.options.onUnauthorized();
            return;
          }

          if (error instanceof RejectedError) {
            this.items = this.items.map((item) =>
              item.clientMessageId === next.clientMessageId ? { ...item, state: 'failed', error: error.message } : item,
            );
            this.emit();
            await this.persist();
            continue;
          }

          // Unreachable or a server error: keep the order, wait, try the same item again.
          this.scheduleRetry();
          return;
        }
      }
    } finally {
      this.running = false;
    }
  }

  private scheduleRetry(): void {
    if (this.paused || this.timer !== null) {
      return;
    }
    const delay = LiveSocket.backoffDelay(this.attempt, this.random);
    this.attempt += 1;
    this.timer = this.setTimer(() => {
      this.timer = null;
      void this.flush();
    }, delay);
  }

  private cancelTimer(): void {
    if (this.timer !== null) {
      this.clearTimer(this.timer);
      this.timer = null;
    }
  }

  private emit(): void {
    this.options.onChange(this.items);
  }

  /** Writes are chained so an older snapshot can never land after a newer one. */
  private persist(): Promise<void> {
    const snapshot = JSON.stringify(this.items);
    this.persistChain = this.persistChain
      .then(() => this.options.store.setItem(this.key, snapshot))
      .catch(() => {
        // Storage full or unavailable: the item still sends from memory this session.
      });
    return this.persistChain;
  }
}
