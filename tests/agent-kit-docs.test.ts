import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';

const root = process.cwd();

function readText(relativePath: string): string {
  return fs.readFileSync(path.join(root, relativePath), 'utf8');
}

function expectFile(relativePath: string) {
  expect(fs.existsSync(path.join(root, relativePath)), `${relativePath} should exist`).toBe(true);
}

function manifestFiles(plugin: AgentKitPlugin): string[] {
  return Object.values(plugin.layers).flatMap((layer) => layer.files ?? []);
}

function hookScripts(): string[] {
  return fs.readdirSync(path.join(root, 'hooks'))
    .filter((entry) => entry.endsWith('.mjs'))
    .map((entry) => `hooks/${entry}`);
}

interface AgentKitPlugin {
  name: string;
  version: string;
  layers: Record<string, { files?: string[]; globs?: string[] }>;
}

describe('agent development kit documents', () => {
  it('keeps the five layer documents present', () => {
    [
      '.github/copilot-instructions.md',
      'CLAUDE.md',
      'skills/README.md',
      '.github/hooks/open-design-agent-kit.json',
      'hooks/README.md',
      'subagents/README.md',
      'plugins/open-design-agent-kit/plugin.json',
    ].forEach(expectFile);
  });

  it('keeps Copilot automatic instructions wired to every layer', () => {
    const instructions = readText('.github/copilot-instructions.md');

    [
      'CLAUDE.md',
      'skills/README.md',
      '.github/hooks/open-design-agent-kit.json',
      '.github/agents/*.agent.md',
      'plugins/open-design-agent-kit/plugin.json',
    ].forEach((requiredReference) => {
      expect(instructions).toContain(requiredReference);
    });

    expect(instructions).toContain('Apply them on every task without waiting for the user to ask');
    expect(instructions).toContain('Delegate automatically');
    expect(instructions).toContain('Do not bypass safety prompts for destructive shell commands');
  });

  it('keeps CLAUDE.md aligned with automatic layer utilization', () => {
    const constitution = readText('CLAUDE.md');

    expect(constitution).toContain('## Automatic Utilization');
    expect(constitution).toContain('.github/copilot-instructions.md');
    expect(constitution).toContain('without waiting for the user to name them');
    expect(constitution).toContain('Never commit API keys');
  });

  it('registers hook commands that point at existing scripts', () => {
    const manifest = JSON.parse(readText('.github/hooks/open-design-agent-kit.json')) as {
      hooks: Record<string, Array<{ command: string; type: string; windows: string; timeout: number }>>;
    };

    expect(Object.keys(manifest.hooks).sort()).toEqual([
      'PostToolUse',
      'PreToolUse',
      'SessionStart',
      'Stop',
      'SubagentStop',
    ]);

    for (const hookEntries of Object.values(manifest.hooks)) {
      for (const hookEntry of hookEntries) {
        expect(hookEntry.type).toBe('command');
        expect(hookEntry.command).toMatch(/^node hooks\/[a-z0-9-]+\.mjs$/);
        expect(hookEntry.command).not.toMatch(/https?:|\||&&|;|^[A-Za-z]:|^\//);
        const scriptPath = hookEntry.command.replace(/^node\s+/, '');
        expectFile(scriptPath);

        expect(hookEntry.windows).toMatch(/^powershell -NoProfile -ExecutionPolicy Bypass -File hooks\/[a-z0-9-]+\.ps1$/);
        expect(hookEntry.windows).not.toMatch(/https?:|\||&&|;|^[A-Za-z]:|^\//);
        const windowsScriptPath = hookEntry.windows.replace(/^powershell -NoProfile -ExecutionPolicy Bypass -File\s+/, '');
        expectFile(windowsScriptPath);
        expect(hookEntry.timeout).toBeGreaterThan(0);
        expect(hookEntry.timeout).toBeLessThanOrEqual(15);
      }
    }
  });

  it('keeps hook scripts local and non-mutating', () => {
    for (const scriptPath of hookScripts()) {
      const body = readText(scriptPath);

      expect(body).not.toMatch(/from ['"]node:(?:fs|child_process|http|https|net|tls)['"]/);
      expect(body).not.toMatch(/\bfetch\s*\(/);
      expect(body).not.toMatch(/\b(?:writeFile|appendFile|unlink|rmdir|mkdir|rename)\s*\(/);
      expect(body).not.toMatch(/process\.env/);
    }
  });

  it('keeps Copilot subagents discoverable as subagents', () => {
    const agentFiles = [
      '.github/agents/open-design-explorer.agent.md',
      '.github/agents/open-design-code-reviewer.agent.md',
      '.github/agents/open-design-test-runner.agent.md',
      '.github/agents/open-design-feature-dev.agent.md',
    ];

    for (const agentFile of agentFiles) {
      const body = readText(agentFile);
      expect(body).toContain('description:');
      expect(body).toContain('user-invocable: false');
      expect(body).toMatch(/^---[\s\S]*---/);
    }
  });

  it('keeps the plugin manifest references valid', () => {
    const plugin = JSON.parse(readText('plugins/open-design-agent-kit/plugin.json')) as AgentKitPlugin;

    expect(plugin.name).toBe('open-design-agent-kit');
    expect(plugin.version).toMatch(/^\d+\.\d+\.\d+/);
    expect(plugin.layers.memory.files).toContain('.github/copilot-instructions.md');

    for (const relativePath of manifestFiles(plugin)) {
      expectFile(relativePath);
    }

    expect(plugin.layers.skills.globs).toContain('skills/*/SKILL.md');
    expect(fs.readdirSync(path.join(root, 'skills')).some((entry) =>
      fs.existsSync(path.join(root, 'skills', entry, 'SKILL.md')),
    )).toBe(true);
  });
});