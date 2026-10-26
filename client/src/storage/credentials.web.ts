import type { Credentials } from './credentialsTypes';

export type { Credentials } from './credentialsTypes';

// Browsers have no secure enclave an app can use, so the token lives in localStorage. That is
// readable by any script on this origin, which is why the bot serves this app with a strict CSP
// (scripts from 'self' only, no inline script) and the page loads nothing from anywhere else.
const KEY = 'mindbot.credentials.v1';

export async function loadCredentials(): Promise<Credentials | null> {
  try {
    const raw = window.localStorage.getItem(KEY);
    if (!raw) {
      return null;
    }
    const value = JSON.parse(raw) as Credentials;
    return typeof value.baseUrl === 'string' && typeof value.token === 'string' ? value : null;
  } catch {
    return null;
  }
}

export async function saveCredentials(credentials: Credentials): Promise<void> {
  window.localStorage.setItem(KEY, JSON.stringify(credentials));
}

export async function clearCredentials(): Promise<void> {
  window.localStorage.removeItem(KEY);
}

/** The bot serves this app, so the API is on the page's own origin (no CORS involved). */
export function defaultServerUrl(): string | null {
  return typeof window !== 'undefined' ? window.location.origin : null;
}
