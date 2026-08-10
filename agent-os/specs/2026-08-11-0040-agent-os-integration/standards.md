# Standards for Agent OS Integration

The integration applies these project standards:

- `global/architecture` - preserves RadioPad layer direction, tenant boundaries, and
  surface-specific static exports.
- `global/change-management` - keeps CI, documentation, and release obligations explicit.
- `global/safety` - prevents agents from weakening report ownership, auditability, or current
  provider policy.
- `global/tech-stack` - blocks accidental framework and platform drift.
- `frontend/rc-design-system` - protects the locked dual-theme UI contract.
- `frontend/surfaces` - prevents routes from shipping in the wrong product surface.
- `backend/dotnet` - keeps backend code and tests consistent.
- `rulebooks/authoring` - protects clinical rulebook IDs, versions, and golden cases.
- `testing/quality-gates` - keeps local checks cheap and CI authoritative.
