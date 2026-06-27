import { describe, expect, it } from 'vitest';
import { createClaudeStreamHandler } from '../daemon/claude-stream.js';

describe('structured agent stream fixtures', () => {
  it('emits TodoWrite tool_use from Claude Code stream JSON', () => {
    const events: unknown[] = [];
    const handler = createClaudeStreamHandler((event: unknown) => events.push(event));
    handler.feed(`${JSON.stringify({
      type: 'assistant',
      message: {
        id: 'msg-1',
        content: [
          {
            type: 'tool_use',
            id: 'toolu-1',
            name: 'TodoWrite',
            input: {
              todos: [{ content: 'Run QA', status: 'pending' }],
            },
          },
        ],
      },
    })}\n`);
    handler.flush();

    expect(events).toContainEqual({
      type: 'tool_use',
      id: 'toolu-1',
      name: 'TodoWrite',
      input: {
        todos: [{ content: 'Run QA', status: 'pending' }],
      },
    });
  });
});
