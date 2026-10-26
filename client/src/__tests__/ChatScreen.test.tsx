import { act, fireEvent, render, screen, waitFor } from '@testing-library/react-native';

import type { Api } from '../api/client';
import type { CommandDescriptor, SendResponse, WebMessage } from '../api/types';
import type { ChatDeps } from '../chat/useChat';
import type { KeyValueStore } from '../outbox/outbox';
import { ChatScreen } from '../screens/ChatScreen';
import { POLICY_VIOLATION, type SocketLike } from '../sync/liveSocket';

class FakeSocket implements SocketLike {
  onopen: SocketLike['onopen'] = null;
  onmessage: SocketLike['onmessage'] = null;
  onclose: SocketLike['onclose'] = null;
  onerror: SocketLike['onerror'] = null;
  sent: string[] = [];
  send(data: string) {
    this.sent.push(data);
  }
  close() {}
  async receive(frame: object) {
    await act(() => this.onmessage?.({ data: JSON.stringify(frame) }));
  }
}

const line = (id: number, direction: WebMessage['direction'], text: string, inReplyToId: number | null = null): WebMessage => ({
  id,
  clientMessageId: direction === 'in' ? `c${id}` : null,
  direction,
  text,
  createdAt: '2026-10-26T12:00:00Z',
  inReplyToId,
});

const commands: CommandDescriptor[] = [
  { name: '/task', usage: '/task <item>', description: 'Add a task.' },
  { name: '/todo', usage: '/todo <item>', description: 'Same as /task.' },
  { name: '/new', usage: '/new', description: 'Start a named note.' },
];

async function setup() {
  const sockets: FakeSocket[] = [];
  const memory = new Map<string, string>();
  const store: KeyValueStore = {
    getItem: async (k) => memory.get(k) ?? null,
    setItem: async (k, v) => {
      memory.set(k, v);
    },
  };

  let resolveSend: ((r: SendResponse) => void) | null = null;
  const api: Api = {
    session: async () => ({ device: 'phone' }),
    commands: async () => commands,
    send: (id, text) =>
      new Promise<SendResponse>((resolve) => {
        resolveSend = () =>
          resolve({ message: { ...line(10, 'in', text), clientMessageId: id }, reply: line(11, 'out', 'Added to TODO.md.', 10), duplicate: false });
      }),
  };

  const deps: ChatDeps = {
    createApi: () => api,
    createSocket: () => {
      const socket = new FakeSocket();
      sockets.push(socket);
      return socket;
    },
    store,
    newId: () => 'uuid-1',
  };

  const onRejected = jest.fn();
  await render(
    <ChatScreen
      credentials={{ baseUrl: 'https://bot.example', token: 'mbt_x', device: 'phone' }}
      onRejected={onRejected}
      onSignOut={() => {}}
      deps={deps}
    />,
  );

  return { sockets, onRejected, finishSend: () => act(() => resolveSend?.(undefined as never)) };
}

describe('ChatScreen', () => {
  it('shows streamed lines and the connection state', async () => {
    const { sockets } = await setup();
    await act(() => sockets[0].onopen?.({}));
    await sockets[0].receive({ type: 'ready', device: 'phone' });
    await sockets[0].receive({ type: 'message', message: line(1, 'in', 'hello') });
    await sockets[0].receive({ type: 'message', message: line(2, 'out', '202610261200.md', 1) });

    expect(await screen.findByText('hello')).toBeTruthy();
    expect(screen.getByText('202610261200.md')).toBeTruthy();
    expect(screen.getByLabelText('Connection: Connected')).toBeTruthy();
  });

  it('suggests commands while a slash command is being typed', async () => {
    await setup();

    await fireEvent.changeText(screen.getByLabelText('Message'), '/t');
    expect(await screen.findByLabelText('/task')).toBeTruthy();
    expect(screen.getByLabelText('/todo')).toBeTruthy();
    expect(screen.queryByLabelText('/new')).toBeNull();

    await fireEvent.press(screen.getByLabelText('/task'));
    expect(screen.getByLabelText('Message').props.value).toBe('/task ');
  });

  it('shows a sent message as pending until the server confirms it, then shows the reply', async () => {
    const { finishSend } = await setup();

    await fireEvent.changeText(screen.getByLabelText('Message'), '/task Buy milk');
    await fireEvent.press(screen.getByLabelText('Send'));

    expect(await screen.findByLabelText('Sending')).toBeTruthy();
    expect(screen.getByLabelText('Message').props.value).toBe('');

    await finishSend();
    expect(await screen.findByText('Added to TODO.md.')).toBeTruthy();
    expect(screen.queryByLabelText('Sending')).toBeNull();
    expect(screen.getByText('/task Buy milk')).toBeTruthy();
  });

  it('reports a rejected token to the app', async () => {
    const { sockets, onRejected } = await setup();
    await act(() => sockets[0].onclose?.({ code: POLICY_VIOLATION }));

    await waitFor(() => expect(onRejected).toHaveBeenCalledTimes(1));
  });
});
