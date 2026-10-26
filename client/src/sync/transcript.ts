import type { WebMessage } from '../api/types';

/**
 * The chat as this device knows it.
 *
 * `cursor` is where the live stream resumes, and it is deliberately NOT the highest id seen.
 * A send's HTTP response can hand us ids 10 and 11 while another device's 8 and 9 are still on
 * their way over the socket; resuming "after 11" would then skip 8 and 9 for good. So the cursor
 * only moves with the stream (which the server sends in id order, without gaps), and lines from
 * HTTP responses are merged by id without touching it.
 */
export interface Transcript {
  readonly messages: readonly WebMessage[];
  readonly cursor: number;
}

export const emptyTranscript: Transcript = { messages: [], cursor: 0 };

/** Merge lines that arrived over the ordered stream; advances the cursor. */
export function applyStream(transcript: Transcript, incoming: readonly WebMessage[]): Transcript {
  const merged = merge(transcript.messages, incoming);
  const cursor = incoming.reduce((max, m) => Math.max(max, m.id), transcript.cursor);
  return merged === transcript.messages && cursor === transcript.cursor ? transcript : { messages: merged, cursor };
}

/** Merge lines from an HTTP response; never moves the cursor. */
export function applyResponse(transcript: Transcript, incoming: readonly WebMessage[]): Transcript {
  const merged = merge(transcript.messages, incoming);
  return merged === transcript.messages ? transcript : { messages: merged, cursor: transcript.cursor };
}

/** Sorted by id, one entry per id. Returns the same array when nothing new arrived. */
function merge(existing: readonly WebMessage[], incoming: readonly WebMessage[]): readonly WebMessage[] {
  const known = new Set(existing.map((m) => m.id));
  const fresh = incoming.filter((m) => {
    if (known.has(m.id)) {
      return false;
    }
    known.add(m.id);
    return true;
  });

  if (fresh.length === 0) {
    return existing;
  }

  return [...existing, ...fresh].sort((a, b) => a.id - b.id);
}
