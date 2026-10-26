// Mirrors the server contract in src/MindBot.Bot/Web/WebApi.cs. Keep the two in step.

export type Direction = 'in' | 'out' | 'system';

export interface WebMessage {
  /** Assigned by the server, only ever grows. */
  id: number;
  /** Set on lines the user sent; the device-generated dedupe key. */
  clientMessageId: string | null;
  direction: Direction;
  text: string;
  createdAt: string;
  /** For an immediate reply, the line it answers. */
  inReplyToId: number | null;
}

export interface SendResponse {
  message: WebMessage;
  reply: WebMessage;
  /** True when the server had already accepted this clientMessageId. */
  duplicate: boolean;
}

export interface CommandDescriptor {
  name: string;
  usage: string;
  description: string;
}

export interface SessionResponse {
  device: string;
}

/** Frames the server sends on /api/ws. */
export type ServerFrame =
  | { type: 'ready'; device: string }
  | { type: 'message'; message: WebMessage };
