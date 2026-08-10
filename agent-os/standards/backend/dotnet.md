# RadioPad C# and Backend Conventions

- Use file-scoped namespaces, nullable reference types, records for DTOs, and classes for
  entities.
- Async methods end in `Async` and accept `CancellationToken ct` as the last parameter.
- Keep controllers thin: resolve tenant/user context, validate input, call an application
  service, and return the established HTTP problem-details shape.
- Keep clinical rule evaluation in `RadioPad.Validation`; do not duplicate rule logic in
  controllers or UI.
- Keep EF Core persistence and migrations in Infrastructure. Validate migrations against
  both SQLite development/tests and PostgreSQL production assumptions.
- Tests use xUnit and plain `Assert`; do not add FluentAssertions or Moq without approval.
