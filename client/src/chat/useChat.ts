import AsyncStorage from '@react-native-async-storage/async-storage';
import * as Crypto from 'expo-crypto';
import { useCallback, useEffect, useMemo, useReducer, useRef, useState } from 'react';

import { type Api, createApi, socketUrl } from '../api/client';
import type { CommandDescriptor, WebMessage } from '../api/types';
import { type KeyValueStore, OUTBOX_KEY, Outbox, type OutboxItem } from '../outbox/outbox';
import type { Credentials } from '../storage/credentials';
import { LiveSocket, type LiveStatus, type SocketLike } from '../sync/liveSocket';
import { applyResponse, applyStream, emptyTranscript, type Transcript } from '../sync/transcript';

/** Everything the chat talks to, swappable in tests. */
export interface ChatDeps {
  createApi: (credentials: Credentials) => Api;
  createSocket?: (url: string) => SocketLike;
  store: KeyValueStore;
  newId: () => string;
}

export const defaultChatDeps: ChatDeps = {
  createApi: (credentials) => createApi(credentials),
  store: AsyncStorage,
  newId: () => Crypto.randomUUID(),
};

type TranscriptAction = { kind: 'stream'; messages: WebMessage[] } | { kind: 'response'; messages: WebMessage[] };

function transcriptReducer(state: Transcript, action: TranscriptAction): Transcript {
  return action.kind === 'stream' ? applyStream(state, action.messages) : applyResponse(state, action.messages);
}

export interface ChatState {
  messages: readonly WebMessage[];
  pending: readonly OutboxItem[];
  status: LiveStatus;
  commands: readonly CommandDescriptor[];
  send: (text: string) => void;
  retry: (clientMessageId: string) => void;
  discard: (clientMessageId: string) => void;
}

/**
 * Wires one signed-in device to its server: the live socket fills the transcript, the outbox
 * sends, and a rejected token from either side calls onRejected exactly once.
 */
export function useChat(credentials: Credentials, onRejected: () => void, deps: ChatDeps = defaultChatDeps): ChatState {
  const [transcript, dispatch] = useReducer(transcriptReducer, emptyTranscript);
  const [pending, setPending] = useState<readonly OutboxItem[]>([]);
  const [status, setStatus] = useState<LiveStatus>('connecting');
  const [commands, setCommands] = useState<readonly CommandDescriptor[]>([]);

  // The socket reads the cursor on every reconnect. It mirrors transcript.cursor, but is
  // advanced in the stream handler itself so a reconnect never sees a value one render stale.
  const cursorRef = useRef(0);

  const rejectedRef = useRef(onRejected);
  useEffect(() => {
    rejectedRef.current = onRejected;
  }, [onRejected]);

  const api = useMemo(() => deps.createApi(credentials), [deps, credentials]);
  const outboxRef = useRef<Outbox | null>(null);

  useEffect(() => {
    let rejected = false;
    const reject = () => {
      if (!rejected) {
        rejected = true;
        rejectedRef.current();
      }
    };

    const outbox = new Outbox({
      store: deps.store,
      key: `${OUTBOX_KEY}:${credentials.baseUrl}`,
      send: (id, text) => api.send(id, text),
      newId: deps.newId,
      onChange: setPending,
      onSent: (response) => dispatch({ kind: 'response', messages: [response.message, response.reply] }),
      onUnauthorized: reject,
    });
    outboxRef.current = outbox;

    const live = new LiveSocket({
      url: socketUrl(credentials.baseUrl),
      token: credentials.token,
      getCursor: () => cursorRef.current,
      onMessage: (message) => {
        cursorRef.current = Math.max(cursorRef.current, message.id);
        dispatch({ kind: 'stream', messages: [message] });
      },
      onStatus: (next) => {
        setStatus(next);
        if (next === 'connected') {
          // The server is reachable again: don't make waiting messages sit out their backoff.
          outbox.kick();
        } else if (next === 'rejected') {
          outbox.pause();
          reject();
        }
      },
      createSocket: deps.createSocket,
    });

    void outbox.load();
    live.start();
    api.commands().then(setCommands, () => {
      // Autocomplete is a convenience; the chat works without it.
    });

    return () => {
      live.stop();
      outbox.dispose();
      outboxRef.current = null;
    };
  }, [api, credentials, deps]);

  const send = useCallback((text: string) => {
    void outboxRef.current?.enqueue(text);
  }, []);
  const retry = useCallback((id: string) => {
    void outboxRef.current?.retry(id);
  }, []);
  const discard = useCallback((id: string) => {
    void outboxRef.current?.discard(id);
  }, []);

  return { messages: transcript.messages, pending, status, commands, send, retry, discard };
}
