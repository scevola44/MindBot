import { RejectedError, RetryableError, UnauthorizedError, createApi, socketUrl } from '../api/client';

const fetchReturning = (status: number, body: unknown) =>
  jest.fn(async () => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }));

describe('api client', () => {
  it('sends the bearer token and the message', async () => {
    const fetchImpl = fetchReturning(200, { message: {}, reply: {}, duplicate: false });
    const api = createApi({ baseUrl: 'https://bot.example/', token: 'mbt_x' }, fetchImpl as unknown as typeof fetch);

    await api.send('id-1', 'hello');

    const [url, init] = fetchImpl.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe('https://bot.example/api/messages');
    expect((init.headers as Record<string, string>).Authorization).toBe('Bearer mbt_x');
    expect(JSON.parse(init.body as string)).toEqual({ clientMessageId: 'id-1', text: 'hello' });
  });

  it('maps 401 to UnauthorizedError', async () => {
    const api = createApi({ baseUrl: 'https://b', token: 't' }, fetchReturning(401, {}) as unknown as typeof fetch);
    await expect(api.session()).rejects.toBeInstanceOf(UnauthorizedError);
  });

  it('maps a 400 to RejectedError with the server reason', async () => {
    const api = createApi(
      { baseUrl: 'https://b', token: 't' },
      fetchReturning(400, { errors: { text: ['A message cannot be empty.'] } }) as unknown as typeof fetch,
    );
    await expect(api.send('i', '')).rejects.toEqual(new RejectedError('A message cannot be empty.', 400));
  });

  it('maps 5xx and network failures to RetryableError', async () => {
    const failing = createApi({ baseUrl: 'https://b', token: 't' }, fetchReturning(503, {}) as unknown as typeof fetch);
    await expect(failing.commands()).rejects.toBeInstanceOf(RetryableError);

    const offline = createApi({ baseUrl: 'https://b', token: 't' }, (async () => {
      throw new TypeError('Network request failed');
    }) as unknown as typeof fetch);
    await expect(offline.commands()).rejects.toBeInstanceOf(RetryableError);
  });

  it('derives the WebSocket URL from the server URL', () => {
    expect(socketUrl('https://bot.example/')).toBe('wss://bot.example/api/ws');
    expect(socketUrl('http://127.0.0.1:8081')).toBe('ws://127.0.0.1:8081/api/ws');
  });
});
