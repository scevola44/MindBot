import { useState } from 'react';
import { ActivityIndicator, Pressable, StyleSheet, Text, TextInput, View } from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import { type Api, type ApiConfig, createApi, normalizeBaseUrl, RejectedError, UnauthorizedError } from '../api/client';
import type { Credentials } from '../storage/credentials';
import { theme } from '../theme';

/**
 * Pairs this device with a server. The token is checked against GET /api/session before
 * anything is saved, so a typo is caught here rather than as a chat that never connects.
 */
export function SetupScreen({
  defaultServerUrl,
  initialServerUrl,
  notice,
  onConnected,
  createApiFor = createApi,
}: {
  /** The page's own origin on web; null on native, where the user must name the server. */
  defaultServerUrl: string | null;
  initialServerUrl?: string;
  /** Why the user is here again, e.g. a revoked token. */
  notice?: string;
  onConnected: (credentials: Credentials) => void;
  createApiFor?: (config: ApiConfig) => Api;
}) {
  const [serverUrl, setServerUrl] = useState(initialServerUrl ?? defaultServerUrl ?? '');
  const [token, setToken] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const askForServer = defaultServerUrl === null;

  const connect = async () => {
    const baseUrl = normalizeBaseUrl(serverUrl);
    const trimmedToken = token.trim();
    if (!/^https?:\/\//i.test(baseUrl)) {
      setError('Enter the server address, starting with https://');
      return;
    }
    if (trimmedToken.length === 0) {
      setError('Paste the device token from `device add`.');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const session = await createApiFor({ baseUrl, token: trimmedToken }).session();
      onConnected({ baseUrl, token: trimmedToken, device: session.device });
    } catch (e) {
      setError(
        e instanceof UnauthorizedError
          ? 'That token was not recognised. It may be mistyped or revoked.'
          : e instanceof RejectedError
            ? e.message
            : 'Could not reach the server. Is the VPN on?',
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <SafeAreaView style={styles.screen}>
      <View style={styles.card}>
        <Text style={styles.title}>Connect to MindBot</Text>
        {notice ? (
          <Text style={styles.notice} accessibilityRole="alert">
            {notice}
          </Text>
        ) : null}

        {askForServer && (
          <>
            <Text style={styles.label}>Server</Text>
            <TextInput
              style={styles.input}
              value={serverUrl}
              onChangeText={setServerUrl}
              placeholder="https://mindbot.your-tailnet.ts.net"
              placeholderTextColor={theme.muted}
              autoCapitalize="none"
              autoCorrect={false}
              keyboardType="url"
              accessibilityLabel="Server"
            />
          </>
        )}

        <Text style={styles.label}>Device token</Text>
        <TextInput
          style={styles.input}
          value={token}
          onChangeText={setToken}
          placeholder="mbt_…"
          placeholderTextColor={theme.muted}
          autoCapitalize="none"
          autoCorrect={false}
          secureTextEntry
          onSubmitEditing={connect}
          accessibilityLabel="Device token"
        />
        <Text style={styles.hint}>
          Create one on the server with{'\n'}
          <Text style={styles.code}>docker compose exec mindbot dotnet MindBot.Bot.dll device add &lt;name&gt;</Text>
        </Text>

        {error ? (
          <Text style={styles.error} accessibilityRole="alert">
            {error}
          </Text>
        ) : null}

        <Pressable
          style={[styles.button, busy && styles.buttonBusy]}
          onPress={connect}
          disabled={busy}
          accessibilityRole="button"
          accessibilityLabel="Connect"
        >
          {busy ? <ActivityIndicator color={theme.onAccent} /> : <Text style={styles.buttonText}>Connect</Text>}
        </Pressable>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: theme.background, justifyContent: 'center', padding: 16 },
  card: {
    width: '100%',
    maxWidth: 440,
    alignSelf: 'center',
    backgroundColor: theme.surface,
    borderRadius: 16,
    padding: 20,
    gap: 6,
  },
  title: { fontSize: 20, fontWeight: '600', color: theme.text, marginBottom: 8 },
  notice: { color: theme.warning, backgroundColor: theme.warningSurface, padding: 10, borderRadius: 8, marginBottom: 6 },
  label: { fontSize: 13, fontWeight: '600', color: theme.text, marginTop: 8 },
  input: {
    borderWidth: 1,
    borderColor: theme.border,
    borderRadius: 10,
    paddingHorizontal: 12,
    paddingVertical: 10,
    fontSize: 15,
    color: theme.text,
    backgroundColor: theme.background,
  },
  hint: { fontSize: 12, color: theme.muted, marginTop: 4 },
  code: { fontFamily: 'monospace', fontSize: 11 },
  error: { color: theme.danger, marginTop: 8 },
  button: { backgroundColor: theme.accent, borderRadius: 10, paddingVertical: 12, alignItems: 'center', marginTop: 14 },
  buttonBusy: { opacity: 0.6 },
  buttonText: { color: theme.onAccent, fontWeight: '600', fontSize: 16 },
});
