# RadioPad RC Design System

- Use `frontend/app/tokens.css` and documented aliases such as `--bg`, `--accent`,
  `--accent-fg`, `--scrim`, `--link`, and the green/blue/red/amber/ai/purple/navy families.
- Never hardcode colors, borders, radii, or shadows in feature CSS or TSX. Extend the token
  system in both themes when a token is missing.
- Both light and deep-navy dark themes are required. Check both themes for every UI change;
  print and exports use the light document theme.
- Render application pages inside `AppShell`; use `Container` and `PageHeader` rather than
  reimplementing shell chrome.
- Use the documented `.rp-*` classes and button variants. Do not introduce MUI, Ant,
  Chakra, Bootstrap, emoji icons, or another primary navigation pattern.
- Map validation severities to red/blocker, amber/warning, and blue/info/style.
- Data-driven views expose `Skeleton`, `EmptyState`, and `ErrorState onRetry` states.
