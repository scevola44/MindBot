import type { CommandDescriptor } from '../api/types';

const MAX_SUGGESTIONS = 8;

/**
 * Commands matching what the user is typing, while they are still typing the command itself:
 * "/" lists everything, "/ta" narrows to /task, and once a space follows the command (they are
 * typing its argument) suggestions stop.
 */
export function suggestCommands(input: string, commands: readonly CommandDescriptor[]): CommandDescriptor[] {
  const text = input.trimStart();
  if (!text.startsWith('/') || /\s/.test(text)) {
    return [];
  }

  const typed = text.toLowerCase();
  return commands.filter((command) => command.name.toLowerCase().startsWith(typed)).slice(0, MAX_SUGGESTIONS);
}

/** What the composer holds after picking a suggestion: the command and a space, ready for its argument. */
export function applySuggestion(command: CommandDescriptor): string {
  return `${command.name} `;
}
