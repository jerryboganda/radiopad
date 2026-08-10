# RadioPad Architecture Boundaries

- Keep dependency direction `Domain -> Application -> Validation -> Infrastructure -> Api`;
  do not reverse the layers.
- `AiGateway` is the only backend entry point for external model providers.
- Frontend pages use the typed client in `frontend/lib/api.ts`; do not call `fetch` from a
  page.
- Tenant-scoped queries filter by the tenant returned from
  `TenantedController.ResolveContextAsync`.
- The frontend is one codebase with physically staged `desktop`, `web`, `mobile`, and
  `shared` route groups selected by `RADIOPAD_SURFACE`.
- Desktop and mobile consume static frontend exports; do not add server-only assumptions to
  exported UI code.
