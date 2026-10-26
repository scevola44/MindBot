import { StatusBar } from 'expo-status-bar';
import { useCallback, useEffect, useState } from 'react';
import { ActivityIndicator, StyleSheet, View } from 'react-native';
import { SafeAreaProvider } from 'react-native-safe-area-context';

import { ChatScreen } from './src/screens/ChatScreen';
import { SetupScreen } from './src/screens/SetupScreen';
import {
  clearCredentials,
  type Credentials,
  defaultServerUrl,
  loadCredentials,
  saveCredentials,
} from './src/storage/credentials';
import { theme } from './src/theme';

type Screen =
  | { kind: 'loading' }
  | { kind: 'setup'; notice?: string; serverUrl?: string }
  | { kind: 'chat'; credentials: Credentials };

export default function App() {
  const [screen, setScreen] = useState<Screen>({ kind: 'loading' });

  useEffect(() => {
    loadCredentials().then(
      (credentials) => setScreen(credentials ? { kind: 'chat', credentials } : { kind: 'setup' }),
      () => setScreen({ kind: 'setup' }),
    );
  }, []);

  const connected = useCallback((credentials: Credentials) => {
    void saveCredentials(credentials);
    setScreen({ kind: 'chat', credentials });
  }, []);

  // The stored token is kept (not silently deleted): the user replaces it on the setup screen.
  const rejected = useCallback((credentials: Credentials) => {
    setScreen({
      kind: 'setup',
      serverUrl: credentials.baseUrl,
      notice: 'This device’s token was rejected; it may have been revoked. Create a new one with `device add` and paste it below. Unsent messages are kept and go out once you reconnect.',
    });
  }, []);

  const signOut = useCallback(() => {
    void clearCredentials();
    setScreen({ kind: 'setup' });
  }, []);

  return (
    <SafeAreaProvider>
      <StatusBar style="dark" />
      {screen.kind === 'loading' ? (
        <View style={styles.loading}>
          <ActivityIndicator color={theme.accent} />
        </View>
      ) : screen.kind === 'setup' ? (
        <SetupScreen
          defaultServerUrl={defaultServerUrl()}
          initialServerUrl={screen.serverUrl}
          notice={screen.notice}
          onConnected={connected}
        />
      ) : (
        <ChatScreen
          credentials={screen.credentials}
          onRejected={() => rejected(screen.credentials)}
          onSignOut={signOut}
        />
      )}
    </SafeAreaProvider>
  );
}

const styles = StyleSheet.create({
  loading: { flex: 1, alignItems: 'center', justifyContent: 'center', backgroundColor: theme.background },
});
