# CleanArchitecture — multi-database modular monolith (POC)

A .NET 10 proof-of-concept for an API that communicates with **multiple databases**, built as a
**database-per-domain modular monolith**. It mirrors a common real-world shape: a new application that
owns its own database while referencing a legacy *system of record* by key, composing data in the
application layer rather than with cross-database joins.

> Status: POC. The architecture and patterns are production-shaped; some operational pieces are
> deliberately stubbed (see [Production notes](#production-notes)).

## What it demonstrates

| Concern | Approach |
|---|---|
| Multiple databases | Each module owns its own DB (SQLite here): `students.db`, `library.db`. No cross-DB joins. |
| Cross-module reads | Composed in the application layer via **published contracts** (`*.Contracts`), never by reaching into another module's repository/DbContext. |
| Mediator | Hand-rolled `BuildingBlocks` mediator (no MediatR) with pipeline behaviors: logging, audit, validation, per-module transaction. |
| Cross-DB writes | **Outbox pattern** (shared, reusable component) — atomic enqueue, background dispatcher, idempotent consumers, capped retries, dead-letter, replay. |
| Distributed consistency | **Choreography saga** — a rejected hold publishes a compensation event back to the originating module (eventual consistency, no distributed transaction). |
| Caching | **HybridCache** (in-memory now, one-line switch to Redis L2), decorating the hottest read; invalidated on writes. |
| Read models | Per-endpoint response DTOs + a read service; projections fetch exactly what each shape needs. List reads are `POST /…/search` with paging in the body. |
| Correlation | A correlation id flows request → audit → outbox (stamped on messages) so a flow is traceable across the async hop. |
| Auth | JWT bearer (symmetric key for the POC; swappable to a real IdP). Write endpoints require authorization; the audit actor comes from the token. |
| Observability | Health checks (`/health`, `/health/live`) + OpenTelemetry tracing/metrics (console exporter) incl. outbox metrics. |
| Audit | Structured audit log via a mediator behavior — Kibana-ready (add an Elasticsearch sink, no code change). |

## Solution layout

```
src/
  BuildingBlocks/            Mediator, behaviors, auditing, correlation, pagination (EF-free)
  BuildingBlocks.Outbox/     Reusable outbox: message, writer, processor, dispatcher, admin, metrics
  Api/CleanArch.Api/         Host: composition root, auth, observability, middleware, endpoints map
  Modules/
    Students/                System of record
      Students.Domain/           Entities, value objects, invariants
      Students.Application/      Vertical-slice features (Command/Query + Handler), abstractions
      Students.Infrastructure/   EF Core, repositories, read service, caching, outbox dispatcher
      Students.Contracts/        Published API for other modules (IStudentDirectory, IStudentHoldService)
      Students.Presentation/     Minimal-API endpoints
    Library/                 New app: loans, keyed by StudentId from the main DB
      Library.Domain / .Application / .Infrastructure / .Contracts / .Presentation
tests/
  CleanArch.UnitTests/       xUnit: domain invariants + handler behavior
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

Write endpoints require a JWT. In Development, mint one:

```bash
# get a token
curl -s -X POST http://localhost:5080/dev/token -H "Content-Type: application/json" \
  -d '{"actor":"registrar@uni","roles":["registrar"]}'

# use it
curl -X POST http://localhost:5080/students -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Ada","lastName":"Lovelace","email":"ada@uni.edu","dateOfBirth":"1990-12-10","enrolledOn":"2024-09-01"}'
```

Reads are open. Every response carries an `X-Correlation-ID` (supply your own to trace a flow).

### Endpoints (summary)

| Method | Route | Auth | Notes |
|---|---|---|---|
| POST | `/students` | ✅ | create |
| GET | `/students/{id}` | — | summary projection |
| GET | `/students/{id}/detail` | — | rich projection (address, contacts, enrollments + computed count) |
| POST | `/students/search` | — | paged (paging/filters in body) → `PagedResult` |
| GET | `/students/{id}/holds` | — | where cross-module write-backs land |
| POST | `/students/{id}/withdraw` | ✅ | triggers saga rejection for later holds |
| POST | `/library/loans` | ✅ | borrow (validates student in main DB) |
| GET | `/library/students/{id}/loans` | — | composes Library + Students data |
| POST | `/library/loans/{id}/fines` | ✅ | crossing a limit enqueues a hold via the outbox |
| GET | `/library/outbox/dead-letter` | — | inspect dead-lettered messages |
| POST | `/library/outbox/dead-letter/{id}/replay` | ✅ | requeue a dead-lettered message |
| GET | `/health`, `/health/live` | — | readiness / liveness |
| POST | `/dev/token`, `/library/outbox/_dev/poison` | — | Development only |

## Tests

```bash
dotnet test
```

## Going distributed / production notes

- **Redis cache**: add `Microsoft.Extensions.Caching.StackExchangeRedis` + `AddStackExchangeRedisCache(...)`; HybridCache uses it as L2 automatically — no code change.
- **Auth**: replace the symmetric-key JWT setup with a real identity provider (Authority/metadata). The actor wiring is unchanged.
- **Telemetry to Kibana/Grafana**: swap the console exporter for OTLP; audit (structured logs) flows to Elasticsearch by adding a logging sink.
- **Databases**: SQLite here for zero-setup; point each module's connection string at its real engine.

### Known gaps (intentional for a POC)

- Outbox: no exponential backoff (fixed 2s poll) and no archival of processed rows.
- No CI pipeline / Dockerfile.
- Cache invalidation is wired only where a write changes cached data today (extend per new write).

## Build note (low-RAM machines)

If a build fails with `OutOfMemoryException` from the Roslyn compiler, the long-lived compiler server has
bloated — run `dotnet build-server shutdown`, then build (optionally with `-m:1`).
