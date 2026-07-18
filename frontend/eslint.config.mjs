// Flat ESLint config for the RadioPad frontend.
//
// Focus: enforce the RC design lock (no hardcoded colours / no third-party component libraries)
// and React-hooks correctness as ERRORS; keep general hygiene rules advisory (warn) so signal
// stays high on a codebase that has never been linted.
//
// Activate (deps are declared in package.json — install them first):
//   cd frontend && pnpm install && pnpm lint
// CI runs it non-blocking today (.github/workflows/ci.yml "Lint" step, continue-on-error).
// Flip that step to blocking once the codebase is warning-clean.

import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import reactHooks from 'eslint-plugin-react-hooks';

const noHardcodedColour = [
  {
    selector: 'Literal[value=/#[0-9a-fA-F]{3,8}\\b/]',
    message: 'No hardcoded hex colours — use tokens.css variables / Tailwind token scales (RC design lock).',
  },
  {
    selector: 'Literal[value=/\\b(rgb|rgba|hsl|hsla)\\(/]',
    message: 'No hardcoded rgb()/hsl() colours — use tokens.css variables (RC design lock).',
  },
  {
    selector: 'TemplateElement[value.raw=/#[0-9a-fA-F]{3,8}\\b/]',
    message: 'No hardcoded hex colours in template strings — use tokens.css variables (RC design lock).',
  },
];

export default tseslint.config(
  { ignores: ['.next/**', 'out/**', 'out-*/**', 'node_modules/**', 'scripts/**', 'next-env.d.ts'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.{ts,tsx}'],
    plugins: { 'react-hooks': reactHooks },
    rules: {
      'no-undef': 'off', // TypeScript resolves identifiers
      'no-unused-vars': 'off',
      '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      '@typescript-eslint/no-explicit-any': 'warn',
      'react-hooks/rules-of-hooks': 'error',
      'react-hooks/exhaustive-deps': 'warn',
      'no-restricted-syntax': ['error', ...noHardcodedColour],
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@mui/*', 'antd', '@ant-design/*', '@chakra-ui/*', 'bootstrap', 'react-bootstrap'],
              message: 'RC design system only — no third-party component libraries (design lock, CLAUDE.md rule 7).',
            },
          ],
        },
      ],
    },
  },
);
