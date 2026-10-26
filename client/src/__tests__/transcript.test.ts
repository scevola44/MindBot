import type { WebMessage } from '../api/types';
import { applyResponse, applyStream, emptyTranscript } from '../sync/transcript';

const line = (id: number, text = `line ${id}`): WebMessage => ({
  id,
  clientMessageId: null,
  direction: 'out',
  text,
  createdAt: '2026-10-26T12:00:00Z',
  inReplyToId: null,
});

describe('transcript', () => {
  it('keeps lines sorted by id with one entry per id', () => {
    const t = applyStream(applyStream(emptyTranscript, [line(2), line(1)]), [line(2), line(3)]);

    expect(t.messages.map((m) => m.id)).toEqual([1, 2, 3]);
  });

  it('advances the cursor with the stream', () => {
    expect(applyStream(emptyTranscript, [line(1), line(2)]).cursor).toBe(2);
  });

  it('does not let an HTTP response move the cursor past lines still in flight on the stream', () => {
    // Another device's 8 and 9 have not arrived yet when our send returns 10 and 11.
    let t = applyStream(emptyTranscript, [line(7)]);
    t = applyResponse(t, [line(10), line(11)]);

    expect(t.cursor).toBe(7);
    expect(t.messages.map((m) => m.id)).toEqual([7, 10, 11]);

    t = applyStream(t, [line(8), line(9), line(10), line(11)]);
    expect(t.messages.map((m) => m.id)).toEqual([7, 8, 9, 10, 11]);
    expect(t.cursor).toBe(11);
  });

  it('returns the same object when nothing new arrived, so React can skip a render', () => {
    const t = applyStream(emptyTranscript, [line(1)]);

    expect(applyStream(t, [line(1)])).toBe(t);
    expect(applyResponse(t, [line(1)])).toBe(t);
  });
});
