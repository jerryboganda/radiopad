# RadioPad Tech Stack

- Frontend: Next.js 16 App Router, React 18, TypeScript, static export, build-time
  Tailwind 3.
- Backend: ASP.NET Core 8 Web API, C# nullable enabled, EF Core.
- Persistence: SQLite for development and tests; PostgreSQL in production.
- Desktop: Tauri 2 with a Windows MSI target only.
- Mobile: Capacitor 6 for Android and iOS.
- CLI: .NET 8 global tool.

Do not introduce another backend framework, ORM, UI component library, or desktop target
without explicit human approval. Reuse the manifests as the version source of truth.
