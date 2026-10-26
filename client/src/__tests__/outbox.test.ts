import { RejectedError, RetryableError, UnauthorizedError } from '../api/client';
import type { SendResponse } from '../api/types';
import { OUTBOX_KEY, Outbox, type OutboxItem } from '../outbox/outbox';

class MemoryStore {
  data = new Map<string, string>();
  async getItem(key: string) {
    return this.data.get(key) ?? null;
  }
  async setItem(key: string, value: string) {
    this.data.set(key, value);
  }
  stored(): OutboxItem[] {
    return JSON.parse(this.data.get(OUTBOX_KEY) ?? '[]');
  }
}

const response = (id: string, text: string): SendResponse => ({
  message: { id: 1, clientMessageId: id, direction: 'in', text, createdAt: '', inReplyToId: null },
  reply: { id: 2, clientMessageId: null, direction: 'out', text: 'ok', createdAt: '', inReplyToId: 1 },
  duplicate: false,
});

/** Lets queued promise callbacks run. */
const settle = () => new Promise((resolve) => setTimeout(resolve, 0));

function harness(send: (id: string, text: string) => Promise<SendResponse>, store = new MemoryStore()) {
  let n = 0;
  const timers: { callback: () => void; ms: number }[] = [];
  const sent: SendResponse[] = [];
  let unauthorized = 0;
  let items: readonly OutboxItem[] = [];

  const outbox = new Outbox({
    store,
    send,
    newId: () => `id-${++n}`,
    onChange: (next) => (items = next),
    onSent: (r) => sent.push(r),
    onUnauthorized: () => unauthorized++,
    random: () => 1,
    setTimer: (callback, ms) => {
      timers.push({ callback, ms });
      return timers.length;
    },
    clearTimer: () => {},
  });

  return {
    outbox,
    store,
    timers,
    sent,
    get items() {
      return items;
    },
    get unauthorized() {
      return unauthorized;
    },
  };
}

describe('Outbox', () => {
  it('stores a message before sending it, and forgets it once confirmed', async () => {
    const store = new MemoryStore();
    let storedAtSendTime: OutboxItem[] = [];
    const h = harness(async (id, text) => {
      storedAtSendTime = store.stored();
      return response(id, text);
    }, store);

    await h.outbox.enqueue('hello');
    await settle();

    expect(storedAtSendTime.map((i) => i.text)).toEqual(['hello']);
    expect(h.sent).toHaveLength(1);
    expect(h.items).toEqual([]);
    expect(store.stored()).toEqual([]);
  });

  it('retries an unreachable send with the same clientMessageId', async () => {
    const attempts: string[] = [];
    let fail = true;
    const h = harness(async (id, text) => {
      attempts.push(id);
      if (fail) {
        throw new RetryableError('offline');
      }
      return response(id, text);
    });

    await h.outbox.enqueue('hello');
    await settle();
    expect(h.timers.map((t) => t.ms)).toEqual([1000]);
    expect(h.items[0].state).toBe('pending');

    fail = false;
    h.timers[0].callback();
    await settle();

    expect(attempts).toEqual(['id-1', 'id-1']);
    expect(h.items).toEqual([]);
  });

  it('sends in order and never overtakes a message that is waiting to retry', async () => {
    const attempts: string[] = [];
    let offline = true;
    const h = harness(async (id, text) => {
      attempts.push(text);
      if (offline) {
        throw new RetryableError('offline');
      }
      return response(id, text);
    });

    await h.outbox.enqueue('/new');
    await settle();
    await h.outbox.enqueue('Groceries');
    await settle();
    expect(attempts).toEqual(['/new']);

    offline = false;
    h.timers[0].callback();
    await settle();

    expect(attempts).toEqual(['/new', '/new', 'Groceries']);
  });

  it('marks a refused message failed with the reason, and moves on', async () => {
    const h = harness(async (id, text) => {
      if (text === 'bad') {
        throw new RejectedError('A message cannot be empty.', 400);
      }
      return response(id, text);
    });

    await h.outbox.enqueue('bad');
    await h.outbox.enqueue('good');
    await settle();

    expect(h.items).toEqual([expect.objectContaining({ text: 'bad', state: 'failed', error: 'A message cannot be empty.' })]);
    expect(h.sent.map((r) => r.message.text)).toEqual(['good']);
    expect(h.timers).toHaveLength(0);
  });

  it('pauses on a rejected token and keeps the message', async () => {
    const h = harness(async () => {
      throw new UnauthorizedError();
    });

    await h.outbox.enqueue('hello');
    await settle();

    expect(h.unauthorized).toBe(1);
    expect(h.items[0].state).toBe('pending');
    expect(h.store.stored()).toHaveLength(1);
    expect(h.timers).toHaveLength(0);
  });

  it('kick() does not lift a pause caused by a rejected token', async () => {
    let calls = 0;
    const h = harness(async () => {
      calls++;
      throw new UnauthorizedError();
    });

    await h.outbox.enqueue('hello');
    await settle();
    h.outbox.kick();
    await settle();

    expect(calls).toBe(1);
  });

  it('picks up messages left over from a previous run', async () => {
    const store = new MemoryStore();
    store.data.set(OUTBOX_KEY, JSON.stringify([{ clientMessageId: 'old', text: 'from before', createdAt: '', state: 'pending' }]));
    const attempts: string[] = [];
    const h = harness(async (id, text) => {
      attempts.push(id);
      return response(id, text);
    }, store);

    await h.outbox.load();
    await settle();

    expect(attempts).toEqual(['old']);
    expect(h.items).toEqual([]);
  });

  it('treats unreadable storage as empty instead of crashing', async () => {
    const store = new MemoryStore();
    store.data.set(OUTBOX_KEY, '{not json');
    const h = harness(async (id, text) => response(id, text), store);

    await h.outbox.load();

    expect(h.items).toEqual([]);
  });
});
