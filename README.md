# CleanArchitecture — multi-database modular monolith (POC)

A .NET 10 proof-of-concept for an API that communicates with **multiple databases**, built as a
**database-per-domain modular monolith**. The worked example is a small **Employee IT Onboarding
and Equipment Management** system: an `Equipment` module (inventory, cache-aside lookups, a
file-backed catalogue) and an `Onboarding` module (a provisioning saga built two ways — an
instant in-process version and a persisted, resumable one — plus a derived readiness summary).

> Status: POC. The architecture and patterns are production-shaped; some operational pieces are
> deliberately stubbed (see [Production notes](#production-notes)).

📚 **New here?** Start with **[tutorials/](tutorials/README.md)** — ten task-oriented guides covering
the patterns this codebase is built from (foundations, adding a feature, adding a module, auditing,
instrumentation, cross-module sagas, auth, testing, and running/reading the observability stack).
For the build/version plumbing, see **[docs/build-and-packages.md](docs/build-and-packages.md)** —
the `Directory.*.props` files, Central Package Management, and the "multiple NuGet sources"
(NU1507) fix. For hosting the API itself, see **[docs/deploy-iis.md](docs/deploy-iis.md)**; for the
telemetry stack, see **[observability/README.md](observability/README.md)** (dev and prod, both on
Docker) or, from a bare Ubuntu box with no Docker knowledge,
**[tutorials/90-observability-server-ubuntu.md](tutorials/90-observability-server-ubuntu.md)** and
**[tutorials/95-reading-your-telemetry.md](tutorials/95-reading-your-telemetry.md)**.

## What it demonstrates

| Concern | Approach |
|---|---|
| Multiple databases | Each module owns its own DB (SQLite here): `equipment.db`, `onboarding.db`. No cross-DB joins. |
| Cross-module calls | A published contract (`Equipment.Contracts.IEquipmentReservationService`), never by reaching into another module's repository/DbContext. |
| Mediator | Hand-rolled `BuildingBlocks` mediator (no MediatR) with pipeline behaviors: logging, audit, validation, per-module transaction. |
| Saga — instant | `ApproveOnboardingInstant` runs all three provisioning steps synchronously in one handler, compensating in reverse on the first failure. No crash recovery — the tradeoff the persisted version exists to fix. |
| Saga — persisted/resumable | `ApproveOnboardingStandard` enqueues the first step and returns; a background outbox dispatcher drives each step (and compensation) from durable state, so a process restart mid-saga resumes exactly where it left off. |
| Service without persistence | The equipment catalogue is read from a bundled file, not the database — a service doesn't always sit on top of a repository. |
| Caching | **HybridCache** (in-memory now, one-line switch to Redis L2), decorating the hottest read (`GetEquipment`); invalidated on writes. |
| Real-time | Equipment create/update/delete push a SignalR event to connected clients via a generic post-commit dispatch behavior. |
| Derived read model | The onboarding summary (readiness %, at-risk status, blockers, estimated cost) is computed from stored facts on every read — none of it is a stored column. |
| Read models | Per-endpoint response DTOs + a read service; projections fetch exactly what each shape needs. List reads are `POST /…/search` with paging in the body. |
| Correlation | A correlation id flows request → audit → outbox (stamped on messages) so a flow is traceable across the async hop. |
| Auth | Three schemes behind a policy selector, chosen per request: an `X-Api-Key` header (service callers), **HTTP Basic validated against Active Directory** (interactive callers), and an **Okta JWT bearer** token (token callers; enabled by setting `Okta:Authority`/`Okta:Audience`). Write endpoints require authorization; the audit actor comes from the principal. API versioning, rate limiting, CORS, and response compression are wired in the host. |
| Observability | Health checks (`/health`, `/health/live`) + OpenTelemetry tracing/metrics (console exporter) incl. outbox metrics. |
| Audit | Structured audit log via a mediator behavior — Kibana-ready (add an Elasticsearch sink, no code change). |

## Solution layout

```
src/
  BuildingBlocks/            Mediator, behaviors, auditing, correlation, pagination (EF-free)
  BuildingBlocks.Outbox/     Reusable outbox: message, writer, processor, dispatcher, admin, metrics
  Api/CleanArch.Api/         Host: composition root, auth, observability, middleware, endpoints map
  Modules/
    Equipment/               Inventory: CRUD, cache-aside lookup, file-backed catalogue
      Equipment.Domain / .Application / .Infrastructure / .Contracts / .Presentation
    Onboarding/               Provisioning saga (instant + persisted engines) and its readiness summary
      Onboarding.Domain / .Application / .Infrastructure / .Presentation
tests/
  CleanArch.UnitTests/            xUnit: domain invariants + handler behavior (fakes, no database)
  CleanArch.Api.IntegrationTests/ Real EF Core + SQLite + HybridCache against the module DI, no HTTP host
```

Each feature is one file (vertical slice): a `static class` with nested `Command`/`Query`,
`Response`/`Result`, `Validator`, and `Handler`.

## Running it

Prerequisites: .NET 10 SDK.

```bash
dotnet run --project src/Api/CleanArch.Api
```

In Development the app applies EF migrations to both SQLite databases on startup and serves Swagger at
`/swagger`. Health at `/health`. (No Redis required — caching runs in-memory until you wire Redis.)

### Calling protected endpoints

Write endpoints require either an `X-Api-Key` header (dev keys: `dev-api-key-reporting`, `dev-api-key-integration`) or HTTP Basic credentials validated against Active Directory. The dev keys are **seeded into the database on first run** — they are real rows, not hardcoded. In Swagger, click **Authorize** and use the API key. From `curl`:

```bash
curl -X POST http://localhost:5235/equipment -H "X-Api-Key: dev-api-key-reporting" \
  -H "Content-Type: application/json" \
  -d '{"name":"ThinkPad X1","category":0,"assetTag":"LAP-001"}'
```

Reads are open. Every response carries an `X-Correlation-ID` (supply your own to trace a flow).

### Endpoints (summary)

The **live, complete list is in Swagger**, grouped by area.

| Method | Route | Auth | Notes |
|---|---|---|---|
| POST | `/equipment` | ✅ | create; pushes `EquipmentCreated` over SignalR |
| PUT | `/equipment/{id}` | ✅ | update; invalidates the cache entry, pushes `EquipmentUpdated` |
| DELETE | `/equipment/{id}` | ✅ | remove; invalidates the cache entry, pushes `EquipmentDeleted` |
| GET | `/equipment/{id}` | — | cache-aside: served from cache when present, else the database |
| POST | `/equipment/search` | — | paged (paging/filters in body) → `PagedResult` |
| GET | `/equipment/catalogue` | — | purchasable models, read from a file — no database |
| POST | `/onboarding` | ✅ | register a new hire's onboarding request |
| GET | `/onboarding/{id}` | — | raw stored status per step, no derived fields |
| GET | `/onboarding/{id}/summary` | — | readiness %, at-risk status, blockers, estimated cost — all computed |
| POST | `/onboarding/{id}/approve-instant` | ✅ | run the saga synchronously; no crash recovery |
| POST | `/onboarding/{id}/approve` | ✅ | start the persisted saga; returns immediately, resumes after a restart |
| POST | `/onboarding/outbox/dead-letter/search` | — | inspect saga steps that exhausted their delivery attempts (paging in body) |
| POST | `/onboarding/outbox/dead-letter/{id}/replay` | ✅ | requeue a dead-lettered saga step |
| GET | `/health`, `/health/live` | — | readiness / liveness |

## Databases & migrations

### Where connection strings come from

The connection string is **never hardcoded for runtime** and the design-time factories don't bake one in
either. Configuration is layered (highest priority wins), so each environment overrides without code or
committed-secret changes:

| Environment | Source of the connection string | Secret committed? |
|---|---|---|
| Local dev (runtime) | `appsettings.json` / `appsettings.Development.json` — SQLite file paths, no secret | No |
| Local dev (real password) | `dotnet user-secrets set "ConnectionStrings:Equipment" "…"` | No (per-dev, off-repo) |
| Production (IIS) | Env var `ConnectionStrings__Equipment` on the app pool (or `web.config` `<environmentVariables>`) | No (lives on the server) |

The runtime reads these via `Configuration.GetConnectionString("Equipment" | "Onboarding")` in `Program.cs`.
Key names map by replacing `:` with `__` in env vars (`ConnectionStrings__Equipment`).

### Design-time factories (for `dotnet ef`)

Each module has an `IDesignTimeDbContextFactory` (`EquipmentDbContextFactory`, `OnboardingDbContextFactory`,
`ApiKeyDbContextFactory`). EF's CLI uses these to build the context for migration commands **without
booting the API host or reading any secret**. They read the connection string from an environment
variable when present, falling back to a throwaway local SQLite file so a fresh clone can scaffold
migrations with zero setup:

| Factory | Env var override | Local fallback |
|---|---|---|
| Equipment | `ConnectionStrings__Equipment` | `equipment-design.db` |
| Onboarding | `ConnectionStrings__Onboarding` | `onboarding-design.db` |
| ApiKey | `ConnectionStrings__ApiKeys` | `apikeys-design.db` |

> The fallback is a local file path, not a secret, and never runs in production — it only gives
> `dotnet ef` something to connect to on a dev machine. Set the env var to scaffold against another engine.

### Adding a migration

Contexts live in the module projects; the API is the startup project. `--context` disambiguates because
the host references both modules:

```powershell
dotnet ef migrations add <Name> `
  --project src/Modules/Equipment/Equipment.Infrastructure `
  --startup-project src/Api/CleanArch.Api `
  --context EquipmentDbContext
# Onboarding: --project src/Modules/Onboarding/Onboarding.Infrastructure --context OnboardingDbContext
```

Remove a migration created but **not yet applied**: `dotnet ef migrations remove --context EquipmentDbContext`.

### Applying migrations to production — all supported options

EF fully supports prod migrations. Every option below **supplies the connection string at run time**, so
nothing prod-specific is ever hardcoded. Pick per how you deploy:

| Option | How | Best for |
|---|---|---|
| **Direct update** | `dotnet ef database update --context EquipmentDbContext --connection "<prod>"` (plus `--project`/`--startup-project`) | Manual/one-off updates; needs SDK + EF tools + DB access on the runner |
| **Migration bundle** ⭐ | `dotnet ef migrations bundle --context EquipmentDbContext …` → ship `efbundle.exe`, run `./efbundle.exe --connection "<prod>"` | CI/CD deploys to IIS — self-contained, no SDK/tools needed on the server |
| **Startup migrate** | `db.Database.Migrate()` at boot (uses the app's runtime connection string) | Small single-instance apps; risky with multiple workers + needs schema rights |
| **Idempotent SQL script** | `dotnet ef migrations script --idempotent --context EquipmentDbContext --output migrate.sql` | When a DBA must review/run the SQL; generates offline (no DB connection at all) |

**Transactions & safety:** EF wraps **each migration** in its own transaction (SQLite and SQL Server both
have transactional DDL), so a migration that *errors* rolls back. But it's one transaction *per* migration,
not across a batch — apply 1→2→3 and if #2 fails you're left at #1. Transactions don't undo a migration
that *succeeds but is logically wrong* (e.g. drops a needed column). The real safety net for prod is:
**back up first**, prefer the idempotent-script or bundle path so you can review/wrap the whole batch in one
transaction, and treat `Down()` (`dotnet ef database update <PreviousMigration>`) as a dev convenience, not
prod recovery — restore from backup instead.

> In Development the app auto-applies migrations on startup against the local SQLite files. Do **not** rely
> on startup migration for prod — use the bundle or script path above so changes are reviewed and reversible.

## Tests

```bash
dotnet test
```

`CleanArch.UnitTests` covers domain rules and handler orchestration with fakes — no database.
`CleanArch.Api.IntegrationTests` runs the real module DI (EF Core against a temp SQLite file, real
HybridCache) without a full HTTP host; see [tutorials/80-testing.md](tutorials/80-testing.md) for the
reasoning behind that split.

## Going distributed / production notes

- **Redis cache**: add `Microsoft.Extensions.Caching.StackExchangeRedis` + `AddStackExchangeRedisCache(...)`; HybridCache uses it as L2 automatically — no code change.
- **Auth**: API key + Basic/AD + **Okta JWT bearer** are all wired. The JWT scheme is config-gated — set `Okta:Authority` (your Okta issuer, e.g. `https://<domain>/oauth2/default`) and `Okta:Audience` (e.g. `api://default`) to enable token validation; it's off by default for the POC. **API keys are validated against the database** — stored as SHA-256 hashes (never plaintext) in their own database behind a dedicated `ApiKeyDbContext` (its own `__AuthMigrationsHistory`, isolated from every business module), with expiry/revocation columns and a short-TTL validation cache. The two `dev-api-key-*` keys are seeded for local use. For service-to-service auth in a system that already has Okta, prefer the **OAuth2 client-credentials** grant (machines become JWT callers) over long-lived keys.
- **Telemetry to Grafana/Kibana**: already wired — OpenTelemetry pushes traces to Tempo and logs to Loki, Prometheus scrapes `/metrics`, and audit records ship to Elasticsearch. Two ready stacks in [`observability/`](observability/): `dev/` (Docker Desktop, no passwords) and `prod/` (internal network, API on IIS).
- **Databases**: SQLite here for zero-setup; point each module's connection string at its real engine.

### Known gaps (intentional for a POC)

- Outbox: no exponential backoff (fixed 2s poll) and no archival of processed rows.
- No CI pipeline, and the app is not containerized: it deploys to IIS ([docs/deploy-iis.md](docs/deploy-iis.md)). Only the observability stack runs on Docker.
- Cache invalidation is wired only where a write changes cached data today (extend per new write).

## Build note (low-RAM machines)

If a build fails with `OutOfMemoryException` from the Roslyn compiler, the long-lived compiler server has
bloated — run `dotnet build-server shutdown`, then build (optionally with `-m:1`).
