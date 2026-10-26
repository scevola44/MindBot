import type { CommandDescriptor, SendResponse, SessionResponse } from './types';

/** The token is unknown or revoked. Never retried; the user must supply a new one. */
export class UnauthorizedError extends Error {
  constructor() {
    super('This device token was rejected. It may have been revoked.');
    this.name = 'UnauthorizedError';
  }
}

/** The server refused the request as invalid (4xx). Retrying the same request cannot succeed. */
export class RejectedError extends Error {
  constructor(
    message: string,
    readonly status: number,
  ) {
    super(message);
    this.name = 'RejectedError';
  }
}

/** The server could not be reached, or failed (5xx). Safe to retry later. */
export class RetryableError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'RetryableError';
  }
}

export interface ApiConfig {
  /** e.g. https://mindbot.tailnet.ts.net, no trailing slash. */
  baseUrl: string;
  token: string;
}

export interface Api {
  session(): Promise<SessionResponse>;
  send(clientMessageId: string, text: string): Promise<SendResponse>;
  commands(): Promise<CommandDescriptor[]>;
}

export function normalizeBaseUrl(url: string): string {
  return url.trim().replace(/\/+$/, '');
}

/** The WebSocket endpoint for a server: same host, ws/wss scheme. */
export function socketUrl(baseUrl: string): string {
  return normalizeBaseUrl(baseUrl).replace(/^http/i, 'ws') + '/api/ws';
}

export function createApi(config: ApiConfig, fetchImpl: typeof fetch = fetch): Api {
  const baseUrl = normalizeBaseUrl(config.baseUrl);

  async function request<T>(path: string, init?: RequestInit): Promise<T> {
    let response: Response;
    try {
      response = await fetchImpl(baseUrl + path, {
        ...init,
        headers: {
          Accept: 'application/json',
          Authorization: `Bearer ${config.token}`,
          ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        },
      });
    } catch {
      // Deliberately not including the error: some runtimes put the URL in it, and the
      // message reaches the UI. Nothing secret is in the URL, but nothing useful either.
      throw new RetryableError('The server could not be reached.');
    }

    if (response.status === 401) {
      throw new UnauthorizedError();
    }

    if (response.status >= 400 && response.status < 500) {
      throw new RejectedError(await problemMessage(response), response.status);
    }

    if (!response.ok) {
      throw new RetryableError(`The server failed (${response.status}).`);
    }

    return (await response.json()) as T;
  }

  return {
    session: () => request<SessionResponse>('/api/session'),
    send: (clientMessageId, text) =>
      request<SendResponse>('/api/messages', {
        method: 'POST',
        body: JSON.stringify({ clientMessageId, text }),
      }),
    commands: () => request<CommandDescriptor[]>('/api/commands'),
  };
}

/** The first validation message from an RFC 7807 problem body, or a generic one. */
async function problemMessage(response: Response): Promise<string> {
  try {
    const body = (await response.json()) as { errors?: Record<string, string[]>; title?: string };
    const first = body.errors && Object.values(body.errors).flat()[0];
    return first ?? body.title ?? `Request refused (${response.status}).`;
  } catch {
    return `Request refused (${response.status}).`;
  }
}
