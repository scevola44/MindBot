import * as SecureStore from 'expo-secure-store';

import type { Credentials } from './credentialsTypes';

export type { Credentials } from './credentialsTypes';

// iOS Keychain / Android Keystore. The web build uses credentials.web.ts instead.
const KEY = 'mindbot.credentials.v1';

export async function loadCredentials(): Promise<Credentials | null> {
  const raw = await SecureStore.getItemAsync(KEY);
  return parse(raw);
}

export async function saveCredentials(credentials: Credentials): Promise<void> {
  await SecureStore.setItemAsync(KEY, JSON.stringify(credentials));
}

export async function clearCredentials(): Promise<void> {
  await SecureStore.deleteItemAsync(KEY);
}

/** Native builds talk to a server the user names; there is no page origin to fall back on. */
export function defaultServerUrl(): string | null {
  return null;
}

function parse(raw: string | null): Credentials | null {
  if (!raw) {
    return null;
  }
  try {
    const value = JSON.parse(raw) as Credentials;
    return typeof value.baseUrl === 'string' && typeof value.token === 'string' ? value : null;
  } catch {
    return null;
  }
}
