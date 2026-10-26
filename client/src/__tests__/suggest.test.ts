import type { CommandDescriptor } from '../api/types';
import { applySuggestion, suggestCommands } from '../commands/suggest';

const commands: CommandDescriptor[] = ['/new', '/cancel', '/task', '/todo', '/status'].map((name) => ({
  name,
  usage: name,
  description: '',
}));

describe('suggestCommands', () => {
  it('lists every command for a bare slash', () => {
    expect(suggestCommands('/', commands)).toHaveLength(5);
  });

  it('narrows by prefix, case-insensitively', () => {
    expect(suggestCommands('/T', commands).map((c) => c.name)).toEqual(['/task', '/todo']);
  });

  it('stops once the user is typing the argument', () => {
    expect(suggestCommands('/task buy', commands)).toEqual([]);
  });

  it('suggests nothing for plain text', () => {
    expect(suggestCommands('hello /task', commands)).toEqual([]);
  });

  it('fills in the command and a space', () => {
    expect(applySuggestion(commands[2])).toBe('/task ');
  });
});
