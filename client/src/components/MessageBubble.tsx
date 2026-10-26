import { Platform, Pressable, StyleSheet, Text, View } from 'react-native';

import type { WebMessage } from '../api/types';
import type { OutboxItem } from '../outbox/outbox';
import { theme } from '../theme';

/** Replies to these commands are tabular text that reads best in a fixed-width font. */
const MONOSPACE_COMMANDS = ['/preview', '/status'];

export function usesMonospace(reply: WebMessage, answers: WebMessage | undefined): boolean {
  if (reply.direction !== 'out' || !answers) {
    return false;
  }
  const command = answers.text.trimStart().split(/\s/, 1)[0]?.toLowerCase() ?? '';
  return MONOSPACE_COMMANDS.includes(command);
}

const monospaceFont = Platform.select({ ios: 'Menlo', android: 'monospace', default: 'ui-monospace, Menlo, Consolas, monospace' });

function formatTime(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? '' : date.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}

export function SentBubble({ message, monospace }: { message: WebMessage; monospace: boolean }) {
  const mine = message.direction === 'in';
  const system = message.direction === 'system';

  return (
    <View
      style={[styles.row, mine ? styles.rowMine : styles.rowTheirs]}
      accessibilityLabel={system ? 'Alert' : mine ? 'You' : 'MindBot'}
    >
      <View style={[styles.bubble, mine ? styles.mine : system ? styles.system : styles.theirs]}>
        <Text selectable style={[styles.text, mine && styles.textMine, monospace && { fontFamily: monospaceFont }]}>
          {message.text}
        </Text>
        <Text style={[styles.meta, mine && styles.metaMine]}>{formatTime(message.createdAt)}</Text>
      </View>
    </View>
  );
}

export function PendingBubble({
  item,
  onRetry,
  onDiscard,
}: {
  item: OutboxItem;
  onRetry: () => void;
  onDiscard: () => void;
}) {
  const failed = item.state === 'failed';

  return (
    <View style={[styles.row, styles.rowMine]} accessibilityLabel={failed ? 'Not sent' : 'Sending'}>
      <View style={[styles.bubble, styles.mine, styles.pending, failed && styles.failed]}>
        <Text selectable style={[styles.text, styles.textMine]}>
          {item.text}
        </Text>
        {failed ? (
          <View style={styles.failedRow}>
            <Text style={styles.failedText}>Not sent: {item.error}</Text>
            <Pressable onPress={onRetry} accessibilityRole="button" accessibilityLabel="Retry">
              <Text style={styles.action}>Retry</Text>
            </Pressable>
            <Pressable onPress={onDiscard} accessibilityRole="button" accessibilityLabel="Discard">
              <Text style={styles.action}>Discard</Text>
            </Pressable>
          </View>
        ) : (
          <Text style={[styles.meta, styles.metaMine]}>Sending…</Text>
        )}
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  row: { paddingHorizontal: 12, paddingVertical: 3, flexDirection: 'row' },
  rowMine: { justifyContent: 'flex-end' },
  rowTheirs: { justifyContent: 'flex-start' },
  bubble: { maxWidth: '85%', borderRadius: 14, paddingHorizontal: 12, paddingVertical: 8 },
  mine: { backgroundColor: theme.accent },
  theirs: { backgroundColor: theme.surface },
  system: { backgroundColor: theme.warningSurface, borderWidth: 1, borderColor: theme.warning },
  pending: { opacity: 0.7 },
  failed: { opacity: 1, borderWidth: 1, borderColor: theme.danger },
  text: { fontSize: 15, lineHeight: 21, color: theme.text },
  textMine: { color: theme.onAccent },
  meta: { fontSize: 11, marginTop: 4, color: theme.muted, alignSelf: 'flex-end' },
  metaMine: { color: theme.onAccentMuted },
  failedRow: { flexDirection: 'row', flexWrap: 'wrap', alignItems: 'center', gap: 12, marginTop: 6 },
  failedText: { fontSize: 12, color: theme.onAccent, flexShrink: 1 },
  action: { fontSize: 12, fontWeight: '600', color: theme.onAccent, textDecorationLine: 'underline' },
});
