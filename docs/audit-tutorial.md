# Audit Trail from Scratch — A Beginner's Tutorial

> **Who this is for:** someone who has never built an audit trail before. No prior knowledge assumed.
> By the end you'll understand *what* an audit trail is, *why* it exists, *how* every piece of ours
> works, and how to run the whole thing (Elasticsearch + Kibana) yourself. Plenty of analogies.
>
> Companion reads: [`observability-tutorial.md`](observability-tutorial.md) (the metrics/traces/logs
> story) and [`../observability/README.md`](../observability/README.md) (the runnable stack,
> Elasticsearch and Kibana included).

---

## Table of contents

1. [The 60-second mental model](#1-the-60-second-mental-model)
2. [Why do we even need this? (the problem)](#2-why-do-we-even-need-this-the-problem)
3. [What one audit record contains](#3-what-one-audit-record-contains)
4. [Audit vs logs — aren't they the same thing?](#4-audit-vs-logs--arent-they-the-same-thing)
5. [The cast of characters (the pieces)](#5-the-cast-of-characters-the-pieces)
6. [How an audit record is born (the flow)](#6-how-an-audit-record-is-born-the-flow)
7. [Setup part 1 — run Elasticsearch + Kibana](#7-setup-part-1--run-elasticsearch--kibana)
8. [Setup part 2 — how the app is wired](#8-setup-part-2--how-the-app-is-wired)
9. [Setup part 3 — see it in Kibana](#9-setup-part-3--see-it-in-kibana)
10. [The revert question (read this before building "undo")](#10-the-revert-question-read-this-before-building-undo)
11. [Gotchas we hit (and how we fixed them)](#11-gotchas-we-hit-and-how-we-fixed-them)
12. [Glossary](#12-glossary)
13. [Where to go next (production)](#13-where-to-go-next-production)

---

## 1. The 60-second mental model

An **audit trail** is a **security camera for your data**. Every time someone *changes* something
important — creates a record, edits a field, deletes a row — the camera records a little clip:
**who** did it, **what** they did, **when**, and **what changed** (the values before and after).

Later, when someone asks *"who withdrew student #42, and what did they change?"*, you don't guess —
you rewind the tape.

Our audit trail has three moves:

```
   A COMMAND RUNS      →      WE RECORD IT       →      YOU SEARCH IT
   (someone writes           (who / what / when         (in Kibana)
    data via the API)         / before → after)
```

- The app **records** a structured audit entry for every write command.
- The entries are **stored** in Elasticsearch (a search database).
- You **search** them in Kibana (the viewer).

The single most important idea, which we'll keep coming back to: **an audit trail is for *reading*,
not for *driving writes*.** It tells you what happened. It is not the thing you "replay" to undo
changes (more on that in section 10 — it's the part most people get wrong).

---

## 2. Why do we even need this? (the problem)

Imagine a fee gets waived on a student's account that shouldn't have been. Someone asks: *who did
that, and when?* Without an audit trail, you're stuck — the database only shows the **current** state
(the fee is gone), not **how it got there** or **who's responsible**.

An audit trail answers the questions a database alone can't:

- **Accountability** — "*who* did this?" (a named person or service, not "the system").
- **Forensics** — "*what exactly* changed?" (this field went from X to Y).
- **History** — "*when* did it happen, and did it succeed or fail?"
- **Foundation for correction** — because we captured the *before* value, a human can see exactly
  what to set it back to.

This is different from ordinary application logging, which we'll clarify next.

---

## 3. What one audit record contains

Here's a real record from our system (a created instructor), exactly as stored in Elasticsearch:

```json
{
  "correlationId": "5deecb4e-bd9b-4e8a-b99b-9842e4166157",
  "actor": "integration-service",
  "action": "CreateInstructor",
  "occurredOnUtc": "2026-07-21T19:14:05Z",
  "succeeded": true,
  "elapsedMs": 106,
  "error": null,
  "changes": [
    {
      "entityType": "Instructor",
      "entityId": "bd0034a3-832a-4399-b106-54d03a223898",
      "operation": "Added",
      "properties": [
        { "name": "FirstName", "newValue": "Grace" },
        { "name": "LastName",  "newValue": "Hopper" },
        { "name": "Email",     "newValue": "grace.hopper@navy.mil" },
        { "name": "Rank",      "newValue": "AssociateProfessor" }
      ]
    }
  ]
}
```

Read it like a sentence: *"**integration-service** ran **CreateInstructor** at 19:14, it **succeeded**
in 106ms, and it **Added** an Instructor (id bd00…) with these field values."*

The fields:

| Field | Meaning |
|-------|---------|
| `actor` | **Who** — the authenticated user (or service). |
| `action` | **What** — the command name (`CreateInstructor`, `WithdrawStudent`, …). |
| `occurredOnUtc` | **When** — UTC timestamp. |
| `succeeded` / `error` | **Outcome** — did it work? If not, the error. |
| `elapsedMs` | How long it took. |
| `correlationId` | The request id — ties this to the logs/traces of the same request. |
| `changes[]` | **What data changed** — per entity: type, id, operation (Added/Modified/Deleted), and each property's **old → new** value. |

For an **edit**, a property entry looks like `{ "name": "Status", "oldValue": "Active", "newValue":
"Withdrawn" }` — you see both sides. For an **add** there's only `newValue`; for a **delete**, only
`oldValue`. Sensitive fields (anything named like *password*, *secret*, *token*…) are stored as
`***REDACTED***` so the audit trail never leaks secrets.

---

## 4. Audit vs logs — aren't they the same thing?

They feel similar (both are "records of things that happened"), but they answer different questions and
have different rules:

| | **Logs** (→ Loki, see observability tutorial) | **Audit trail** (→ Elasticsearch/Kibana) |
|---|---|---|
| Purpose | Debugging — "what was the app doing?" | Accountability — "who changed what data?" |
| Audience | Developers | Compliance, security, support, developers |
| Content | Free-form technical messages | Structured who/what/before→after |
| Coverage | Everything (noisy) | Only meaningful **writes** |
| Retention | Short (days) | Long (often months/years) |
| Tone | Disposable | Kept, sometimes tamper-evident |

That's *why* audit gets its **own** store (Elasticsearch) instead of being buried in the log stream —
different lifetime, different access control, different consumers. (Our audit records *also* fall back
to the log pipeline if Elasticsearch is unavailable, so nothing is ever lost — but the primary home is
Elasticsearch.)

---

## 5. The cast of characters (the pieces)

There are two groups: the **capture** side (inside the app) and the **storage/view** side (external).

### Capture side (already built into the app)

| Piece | Plain-English job | Where |
|-------|-------------------|-------|
| `IAuditableRequest` | A marker that says "this command should be audited." Reads don't carry it, so they're ignored. | `BuildingBlocks/Messaging` |
| `AuditBehavior` | Wraps every auditable command and records the who/what/when/outcome. | `BuildingBlocks/Messaging/Behaviors` |
| `ICurrentActor` | Answers "who is doing this?" — the authenticated user, else the dev `X-Actor` header, else `system`. | `BuildingBlocks/Auditing`, `Api/HttpContextActor` |
| `AuditingSaveChangesInterceptor` | Watches the database save and captures **before/after** values of every changed entity. | `BuildingBlocks.Persistence` |
| `IAuditScope` | A per-request notepad the interceptor writes changes onto, and the behavior reads back. | `BuildingBlocks/Auditing` |
| `AuditEntry` | The finished record (who/what/when/outcome + `changes[]`). | `BuildingBlocks/Auditing` |
| `IAuditSink` | "Where do audit records go?" — swappable destination. | `BuildingBlocks/Auditing` |

### Storage / view side

| Piece | Plain-English job | Runs at |
|-------|-------------------|---------|
| `ElasticsearchAuditSink` + shipper | Sends audit records to Elasticsearch (in the background, batched). | in the app |
| **Elasticsearch** | Search database that **stores** the audit records. | `localhost:9200` |
| **Kibana** | The UI you **search and read** the audit trail in. | `localhost:5601` |

> Like Grafana in the observability story, **Kibana stores nothing** — it's just a viewer that queries
> Elasticsearch. Elasticsearch is the actual home of the data.

---

## 6. How an audit record is born (the flow)

Follow one `CreateInstructor` request from click to Kibana:

```
  HTTP POST /instructors
        │
        ▼
  ┌─────────────────────────────────────────────────────────────┐
  │ MEDIATOR PIPELINE (wraps the command, outer → inner)         │
  │                                                              │
  │  AuditBehavior ──────────────────────────────────┐          │
  │    (starts a stopwatch, notes the actor)          │          │
  │      Validation → Transaction → Handler           │          │
  │                        │                          │          │
  │                        ▼ SaveChanges              │          │
  │        AuditingSaveChangesInterceptor             │          │
  │          captures before/after ► IAuditScope      │          │
  │                        │ commit                   │          │
  │      ◄─────────────────┘                          │          │
  │    AuditBehavior reads IAuditScope, builds        │          │
  │    an AuditEntry, hands it to IAuditSink ◄─────────┘          │
  └───────────────────────────┬──────────────────────────────────┘
                              │ (non-blocking)
                              ▼
                    AuditShipmentQueue  (in-memory buffer)
                              │
                              ▼
             ElasticsearchAuditShipper  (background worker)
                              │ bulk index (retry + log-fallback)
                              ▼
                    Elasticsearch  cleanarch-audit-2026.07.21
                              ▲
                              │ query
                            Kibana
```

Two things worth calling out:

- **The interceptor sits inside the transaction**, so it only records changes that actually
  **committed**. If the command fails and rolls back, the audit entry is still written (so you know it
  was *attempted*), but with `succeeded: false` and no `changes` (nothing stuck).
- **Shipping to Elasticsearch is non-blocking.** The command hands its record to an in-memory queue and
  returns immediately; a background worker does the network call. So even if Elasticsearch is slow or
  down, **your API stays fast and the command still succeeds** — the record just waits in the queue (or,
  if ES is truly unavailable, falls back to the logs).

### Push, not pull

In the observability tutorial, Prometheus *pulled* metrics. Audit is the opposite: the app **pushes**
records **to** Elasticsearch. So the app config holds Elasticsearch's address
(`Audit:Elasticsearch:Uri`), not the other way around.

---

## 7. Setup part 1 — run Elasticsearch + Kibana

Elasticsearch and Kibana run as containers alongside the rest of the observability stack. They sit
behind a profile because Elasticsearch wants about a gigabyte of RAM, so you start them only when you
want the audit trail:

```bash
cd observability/dev
docker compose --profile elk up -d
```

**Confirm**: Elasticsearch <http://localhost:9200> returns JSON; Kibana <http://localhost:5601> comes
up after a minute or two (first boot initialises a lot of plugins).

The dev stack runs Elasticsearch with **security disabled** — single-node, plain `http`, no
credentials — which is what lets the app connect to `http://localhost:9200` with nothing configured.
Full setup: [`../observability/README.md`](../observability/README.md).

> This is a **development** setup. Never disable security in production — see section 13.

---

## 8. Setup part 2 — how the app is wired

The app is already instrumented; you don't have to write anything. But here's what's doing the work, so
it's not a black box.

### Marking what to audit

A command opts in by implementing the `IAuditableRequest` marker:

```csharp
public sealed record Command(string FirstName, string LastName, string Email, /*…*/)
    : IRequest<Guid>, IStudentsCommand, IAuditableRequest;   // ← audited
```

No marker → not audited. That's why reads (queries) never clutter the audit trail.

### Capturing before/after (the interceptor)

An EF Core `SaveChanges` interceptor walks the change tracker on every write and records, for each
changed entity, its type, id, operation, and each property's old→new value into the per-request
`IAuditScope`. It's attached to each module's database like this:

```csharp
services.AddAuditChangeTracking();
services.AddDbContext<StudentsDbContext>((sp, options) =>
    options.UseSqlite(connectionString).UseAuditChangeTracking(sp));
```

### Choosing where audit goes (the sink)

One line in `Program.cs` points audit at Elasticsearch:

```csharp
.AddElasticsearchAudit(builder.Configuration)   // ships to ES; no-op if not configured
```

…configured in `appsettings.json`:

```json
"Audit": {
  "Elasticsearch": {
    "Uri": "http://localhost:9200",
    "IndexFormat": "cleanarch-audit-{0:yyyy.MM.dd}"
  }
}
```

**Config-gated and safe:** if `Uri` is blank, the Elasticsearch sink isn't registered and audit falls
back to structured **logs** — so a machine without Elasticsearch still works, it just won't ship to
Kibana. For production you'd add `ApiKey` (or `Username`/`Password`) here, from a secret store.

### Run it and generate a record

```powershell
dotnet run --project src\Api\CleanArch.Api
# create an instructor (the seeded dev API key authenticates the call):
curl -X POST http://localhost:5235/instructors -H "Content-Type: application/json" `
  -H "X-Api-Key: dev-api-key-integration" `
  -d '{\"firstName\":\"Grace\",\"lastName\":\"Hopper\",\"email\":\"grace.hopper@navy.mil\",\"departmentName\":\"Computer Science\",\"rank\":2}'
```

Confirm it landed:

```powershell
curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"
```

---

## 9. Setup part 3 — see it in Kibana

Kibana needs to know which indices to show — that's a **data view** (a saved index pattern).

1. Kibana → **Stack Management → Data Views → Create data view**.
2. Name it `CleanArch Audit`, index pattern `cleanarch-audit-*`, time field **`occurredOnUtc`**.
3. Open **Discover** and pick the `CleanArch Audit` view.

Now you can:

- **Search** by `actor: "integration-service"` or `action: "WithdrawStudent"`.
- **Filter** to failures with `succeeded: false`.
- **Expand a row** to read `changes` — the exact entity, operation, and before→after values.
- **Pick a time range** (top-right) since `occurredOnUtc` is the time field.

> **Field names are `camelCase`** (`actor`, `action`, `succeeded`, `changes`, `occurredOnUtc`) — that's
> how the Elasticsearch client serialises them. If a Kibana search returns nothing, check the casing.

---

## 10. The revert question (read this before building "undo")

A natural next thought is: *"I've captured the before/after — can I revert an action straight from the
audit trail?"* This is the one place people go wrong, so here's the honest guidance.

**Do not drive reverts from the Kibana/Elasticsearch audit logs.** Elasticsearch is a **search and
observability store**, not your system of record. It has retention limits, can be reindexed or
administered, and isn't part of your database transaction. Reading from it to perform authoritative
**writes** (undoing changes) is fragile and unsafe.

The right way to get "undo":

- The captured **before/after** is your *evidence* of what to reverse — great for a human to inspect.
- Perform the actual revert as a **compensating command**: a new, first-class domain operation that
  sets the values back — and which is **itself audited**. (e.g. an `UnwaiveCharge` that reverses a
  `WaiveCharge`.)
- **Never** blindly "replay in reverse" from log entries. The moment there are dependent changes,
  concurrency, or cascades, a naive replay silently corrupts data.

So: **Kibana is where you investigate; a compensating command is how you fix.** The audit trail informs
the revert; it does not *perform* it.

**One more caveat — trust follows identity.** An audit trail is only as trustworthy as its `actor`.
Today, an unauthenticated call falls back to the spoofable `X-Actor` header (a dev convenience). For
real accountability in production, **require authentication** on auditable commands so the actor is a
verified identity, not a header anyone can set.

---

## 11. Gotchas we hit (and how we fixed them)

These are the exact snags from setting this up on Windows, so you recognise them.

### Elasticsearch won't start: `cluster.initial_master_nodes` vs `single-node`

On first run, Elasticsearch 9 **auto-configures security** and writes settings into
`config/elasticsearch.yml` — including `cluster.initial_master_nodes`. That conflicts with the
`discovery.type=single-node` we pass for a simple dev node:
`setting [cluster.initial_master_nodes] is not allowed when [discovery.type] is set to [single-node]`.
**Fix:** simplify `elasticsearch.yml` to security-off and remove `cluster.initial_master_nodes` (let
`single-node` handle bootstrap).

### Elasticsearch won't start: leftover keystore passwords

After disabling security, ES still complained about
`xpack.security.transport.ssl.keystore.secure_password` — those live in the **keystore**
(`config/elasticsearch.keystore`), separate from the yml. **Fix:** delete the keystore; ES regenerates
a clean one on next start.

### Kibana takes "forever" on first boot

Kibana initialises ~190 plugins the first time and logs warnings about generating random encryption
keys. That's **normal** — it's not stuck, it just needs a minute or two. The warnings are non-fatal.

### "No results" in Kibana — the casing trap

The stored fields are **camelCase** (`actor`, not `Actor`; time field `occurredOnUtc`, not
`OccurredOnUtc`). Searching or setting the data view with the wrong casing shows nothing.

### A failed command still produced an audit record

Creating a duplicate (same email) returned HTTP 500 — **and** wrote an audit record with
`succeeded: false`, the error, and empty `changes`. That's by design: **failed attempts are audited
too** (the behavior sits outside validation), so you can see what was *tried*, not just what worked.

---

## 12. Glossary

| Term | Plain meaning |
|------|---------------|
| **Audit trail** | A record of who changed what data, when. |
| **Audit record / entry** | One such record (our `AuditEntry`). |
| **Actor** | Who performed the action (a user or service). |
| **Action** | Which command ran (e.g. `WithdrawStudent`). |
| **Change-set** | The list of entity changes in a command (`changes[]`). |
| **Before/after** | A property's old value and new value. |
| **Interceptor** | Code that hooks into `SaveChanges` to observe DB writes. |
| **Sink** | The destination audit records are sent to (`IAuditSink`). |
| **Compensating command** | A new action that reverses an earlier one (the safe way to "undo"). |
| **Elasticsearch** | The search database that stores the audit records. |
| **Kibana** | The UI for searching Elasticsearch. |
| **Index** | Elasticsearch's unit of storage (ours: `cleanarch-audit-YYYY.MM.dd`). |
| **Data view** | A Kibana saved index pattern (which indices + the time field). |
| **Redaction** | Replacing a sensitive value with `***REDACTED***`. |
| **Push vs pull** | Here the app *pushes* to Elasticsearch (vs Prometheus *pulling* metrics). |

---

## 13. Where to go next (production)

The dev setup keeps things simple; production tightens each part. The app code doesn't change — only
config and the surrounding infrastructure.

- **Turn security back on.** The production stack in [`../observability/prod`](../observability/prod)
  already does this: `xpack.security` on, and a write-only `audit-writer` account the app authenticates
  as via `Audit__Elasticsearch__Username`/`Password` from the environment — **never** committed. Add
  TLS on top if the network between the app and Elasticsearch is not one you trust.
- **Real actor identity.** Require authentication on auditable commands so `actor` is a verified user,
  not the `X-Actor` dev header.
- **Retention (ILM).** Add an Index Lifecycle Management policy on `cleanarch-audit-*` to roll over and
  delete (or archive) old indices per your compliance window.
- **Tamper-resistance.** For strict compliance, restrict who can write/delete the audit indices, and
  consider a write-once/append-only strategy.
- **Revert capability.** If you want "undo", build it as **compensating commands** (section 10), driven
  by a human reading the trail — not by replaying logs.
- **A collector, optionally.** As with observability, you can route audit through a collector (e.g. the
  OpenTelemetry Collector / Logstash) for buffering and fan-out, instead of the app talking to
  Elasticsearch directly.

The mental model never changes: **the app records who-did-what-with-before-and-after, Elasticsearch
stores it, Kibana shows it — and reverting is a new audited action, not a replay.**

---

*Companion files: the runnable stack lives in [`../observability/`](../observability/); the app wiring is in
`src/BuildingBlocks/Auditing/`, `src/BuildingBlocks.Persistence/AuditingSaveChangesInterceptor.cs`, and
`src/BuildingBlocks.Auditing.Elasticsearch/`.*
