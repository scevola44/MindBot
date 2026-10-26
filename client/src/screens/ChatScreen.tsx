import { useMemo, useState } from 'react';
import {
  FlatList,
  type NativeSyntheticEvent,
  Platform,
  Pressable,
  StyleSheet,
  Text,
  TextInput,
  type TextInputKeyPressEventData,
  View,
} from 'react-native';
import { SafeAreaView } from 'react-native-safe-area-context';

import type { WebMessage } from '../api/types';
import { defaultChatDeps, type ChatDeps, useChat } from '../chat/useChat';
import { applySuggestion, suggestCommands } from '../commands/suggest';
import { PendingBubble, SentBubble, usesMonospace } from '../components/MessageBubble';
import type { OutboxItem } from '../outbox/outbox';
import type { Credentials } from '../storage/credentials';
import type { LiveStatus } from '../sync/liveSocket';
import { theme } from '../theme';

type Row = { key: string; message: WebMessage } | { key: string; item: OutboxItem };

const STATUS_LABEL: Record<LiveStatus, string> = {
  connecting: 'Connecting…',
  connected: 'Connected',
  reconnecting: 'Reconnecting…',
  rejected: 'Token rejected',
  stopped: 'Offline',
};

export function ChatScreen({
  credentials,
  onRejected,
  onSignOut,
  deps = defaultChatDeps,
}: {
  credentials: Credentials;
  onRejected: () => void;
  onSignOut: () => void;
  deps?: ChatDeps;
}) {
  const chat = useChat(credentials, onRejected, deps);
  const [draft, setDraft] = useState('');

  const byId = useMemo(() => new Map(chat.messages.map((m) => [m.id, m])), [chat.messages]);

  // Newest first: the list is inverted so it opens at the bottom and grows upwards.
  const rows = useMemo<Row[]>(() => {
    const sent: Row[] = chat.messages.map((message) => ({ key: `m${message.id}`, message }));
    const waiting: Row[] = chat.pending.map((item) => ({ key: `p${item.clientMessageId}`, item }));
    return [...sent, ...waiting].reverse();
  }, [chat.messages, chat.pending]);

  const suggestions = suggestCommands(draft, chat.commands);

  const submit = () => {
    const text = draft.trim();
    if (text.length === 0) {
      return;
    }
    chat.send(text);
    setDraft('');
  };

  // Web only: Enter sends, Shift+Enter is a newline. On phones the keyboard has its own send flow.
  const onKeyPress = (event: NativeSyntheticEvent<TextInputKeyPressEventData>) => {
    const native = event.nativeEvent as TextInputKeyPressEventData & { shiftKey?: boolean };
    if (Platform.OS === 'web' && native.key === 'Enter' && !native.shiftKey) {
      event.preventDefault();
      submit();
    }
  };

  return (
    <SafeAreaView style={styles.screen} edges={['top', 'bottom']}>
      <View style={styles.header}>
        <View>
          <Text style={styles.title}>MindBot</Text>
          <View style={styles.statusRow} accessibilityLabel={`Connection: ${STATUS_LABEL[chat.status]}`}>
            <View style={[styles.dot, { backgroundColor: chat.status === 'connected' ? theme.ok : theme.warning }]} />
            <Text style={styles.status}>
              {STATUS_LABEL[chat.status]} · {credentials.device}
            </Text>
          </View>
        </View>
        <Pressable onPress={onSignOut} accessibilityRole="button" accessibilityLabel="Sign out">
          <Text style={styles.link}>Sign out</Text>
        </Pressable>
      </View>

      <FlatList
        style={styles.list}
        inverted
        data={rows}
        keyExtractor={(row) => row.key}
        renderItem={({ item: row }) =>
          'message' in row ? (
            <SentBubble
              message={row.message}
              monospace={usesMonospace(row.message, row.message.inReplyToId ? byId.get(row.message.inReplyToId) : undefined)}
            />
          ) : (
            <PendingBubble
              item={row.item}
              onRetry={() => chat.retry(row.item.clientMessageId)}
              onDiscard={() => chat.discard(row.item.clientMessageId)}
            />
          )
        }
        ListEmptyComponent={
          <Text style={styles.empty}>Send a message to file a note, or type / for commands.</Text>
        }
      />

      {suggestions.length > 0 && (
        <View style={styles.suggestions} accessibilityLabel="Command suggestions">
          {suggestions.map((command) => (
            <Pressable
              key={command.name}
              style={styles.suggestion}
              onPress={() => setDraft(applySuggestion(command))}
              accessibilityRole="button"
              accessibilityLabel={command.name}
            >
              <Text style={styles.suggestionName}>{command.usage}</Text>
              <Text style={styles.suggestionText} numberOfLines={1}>
                {command.description}
              </Text>
            </Pressable>
          ))}
        </View>
      )}

      <View style={styles.composer}>
        <TextInput
          style={styles.input}
          value={draft}
          onChangeText={setDraft}
          onKeyPress={onKeyPress}
          placeholder="Message"
          placeholderTextColor={theme.muted}
          multiline
          accessibilityLabel="Message"
          autoCapitalize="sentences"
        />
        <Pressable
          style={[styles.send, draft.trim().length === 0 && styles.sendDisabled]}
          onPress={submit}
          disabled={draft.trim().length === 0}
          accessibilityRole="button"
          accessibilityLabel="Send"
        >
          <Text style={styles.sendText}>Send</Text>
        </Pressable>
      </View>
    </SafeAreaView>
  );
}

const styles = StyleSheet.create({
  screen: { flex: 1, backgroundColor: theme.background },
  header: {
    flexDirection: 'row',
    justifyContent: 'space-between',
    alignItems: 'center',
    paddingHorizontal: 16,
    paddingVertical: 10,
    borderBottomWidth: StyleSheet.hairlineWidth,
    borderBottomColor: theme.border,
    backgroundColor: theme.surface,
  },
  title: { fontSize: 17, fontWeight: '600', color: theme.text },
  statusRow: { flexDirection: 'row', alignItems: 'center', gap: 6, marginTop: 2 },
  dot: { width: 8, height: 8, borderRadius: 4 },
  status: { fontSize: 12, color: theme.muted },
  link: { fontSize: 14, color: theme.accent },
  list: { flex: 1 },
  empty: { textAlign: 'center', color: theme.muted, padding: 24, transform: [{ scaleY: -1 }] },
  suggestions: {
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: theme.border,
    backgroundColor: theme.surface,
    paddingVertical: 4,
  },
  suggestion: { paddingHorizontal: 16, paddingVertical: 6 },
  suggestionName: { fontSize: 14, fontWeight: '600', color: theme.text },
  suggestionText: { fontSize: 12, color: theme.muted },
  composer: {
    flexDirection: 'row',
    alignItems: 'flex-end',
    gap: 8,
    padding: 8,
    borderTopWidth: StyleSheet.hairlineWidth,
    borderTopColor: theme.border,
    backgroundColor: theme.surface,
  },
  input: {
    flex: 1,
    minHeight: 40,
    maxHeight: 160,
    borderWidth: 1,
    borderColor: theme.border,
    borderRadius: 20,
    paddingHorizontal: 14,
    paddingVertical: 9,
    fontSize: 15,
    color: theme.text,
    backgroundColor: theme.background,
  },
  send: { backgroundColor: theme.accent, borderRadius: 20, paddingHorizontal: 16, paddingVertical: 10 },
  sendDisabled: { opacity: 0.4 },
  sendText: { color: theme.onAccent, fontWeight: '600', fontSize: 15 },
});
