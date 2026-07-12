import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';

const appCss = readFileSync(resolve(__dirname, '../app/radiopad.css'), 'utf8');
const globalCss = readFileSync(resolve(__dirname, '../app/globals.css'), 'utf8');
const shellCss = readFileSync(resolve(__dirname, '../app/shell.css'), 'utf8');

function rule(css: string, selector: string): string {
  const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  const match = css.match(new RegExp(`${escaped}\\s*\\{([^}]+)\\}`));
  expect(match, `missing CSS rule for ${selector}`).not.toBeNull();
  return match?.[1] ?? '';
}

function zIndex(css: string, selector: string): number {
  const value = rule(css, selector).match(/z-index:\s*(\d+)/)?.[1];
  expect(value, `${selector} needs an explicit numeric z-index`).toBeTruthy();
  return Number(value);
}

describe('dropdown layering contracts', () => {
  it('keeps the report ribbon above the document stacking context', () => {
    const ribbon = rule(appCss, '.rp-ribbon');
    expect(ribbon).toMatch(/position:\s*relative/);
    expect(zIndex(appCss, '.rp-ribbon')).toBeGreaterThan(0);
  });

  it.each([
    ['Export menu', appCss, '.rp-menu-popover'],
    ['Rewrite menu', appCss, '.rp-rewrite-popover'],
    ['Searchable select', globalCss, '.rp-combobox-panel'],
    ['Profile menu', shellCss, '.rp-profile-popover'],
  ])('%s has an explicit overlay layer', (_name, css, selector) => {
    expect(rule(css, selector)).toMatch(/z-index:\s*(?:\d+|calc\()/);
  });
});
