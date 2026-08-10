# RadioPad Safety and Trust Boundaries

- The radiologist owns the final report. RadioPad never auto-signs.
- AI-generated text stays visibly marked with `.ai-mark` until reviewed or edited.
- `AiGateway` records `ContainsPhi` in audit and usage data. Current operator policy permits
  enabled providers regardless of compliance class or PHI content; only disabled providers
  and the explicit `Blocked` class are rejected. Do not restore the retired PHI routing gate
  without an explicit operator decision.
- Audit events are append-only. Use `IAuditLog.AppendAsync`; never update or delete
  `AuditEvents`.
- Provider API keys are referenced as `env:NAME`; never place secrets or report bodies in
  logs, JSON responses, or fixtures.
- Bind the backend to `127.0.0.1` by default. Remote exposure requires explicit operator
  configuration and a TLS reverse proxy.
- Never automate provider login, CAPTCHA, 2FA, consent, cookies, or credentials.
