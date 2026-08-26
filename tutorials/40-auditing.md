# Auditing — Who Changed What

**Who this is for:** someone who needs a defensible record of every change made through
their application — for compliance, for support, or for the day somebody asks "who did
this?" and "the database" is not an acceptable answer.

**What you'll be able to do by the end:** make any command audited, capture before-and-after
values of every field it changes, choose where those records are stored, make the "who"
trustworthy, and read the trail back. And you'll know the one thing you must never build
on top of it.

**What you need first:** nothing beyond a running application. Auditing is opt-in per
command and costs one interface.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What an audit trail is](#1-what-an-audit-trail-is) | Understand the job it does |
| 2 | [Audit is not logging](#2-audit-is-not-logging) | Why it gets its own store |
| 3 | [What one record contains](#3-what-one-record-contains) | Read a real record |
| 4 | [The four moving parts](#4-the-four-moving-parts) | How capture actually works |
| 5 | [Step 1 — Audit a command](#5-step-1--audit-a-command) | One interface |
| 6 | [Step 2 — Capture before and after](#6-step-2--capture-before-and-after) | Two lines per module |
| 7 | [Step 3 — Choose where records go](#7-step-3--choose-where-records-go) | Sinks, and the fallback trap |
| 8 | [Step 4 — Make the actor trustworthy](#8-step-4--make-the-actor-trustworthy) | The weakest link |
| 9 | [Step 5 — Verify it](#9-step-5--verify-it) | Prove it, don't assume |
| 10 | [Reading the trail](#10-reading-the-trail) | Kibana, and the casing trap |
| 11 | [The revert question](#11-the-revert-question) | Read this before building "undo" |
| 12 | [Retention and tamper-resistance](#12-retention-and-tamper-resistance) | Making it hold up |
| 13 | [Writing your own sink](#13-writing-your-own-sink) | The extension point |
| 14 | [The checklist](#14-the-checklist) | Run this when doing it for real |
| 15 | [Troubleshooting](#15-troubleshooting) | Symptom, cause, fix |
| 16 | [Cheat sheet](#16-cheat-sheet) | Settings and commands, in one place |
| 17 | [Glossary](#17-glossary) | Every term used in this guide |

---

## 1. What an audit trail is

An audit trail is a **security camera for your data**. Every time someone changes something
that matters, it records a short clip: **who** did it, **what** they did, **when**, whether
it worked, and **what the values were before and after**.

A database holds the *current* state. It cannot tell you how that state came to be. If a
fee was waived that shouldn't have been, the database shows you a missing fee and nothing
else — not who removed it, not what it was, not when. The audit trail is the only thing
that answers those questions.

Four things it gives you:

| | The question it answers |
|---|---|
| **Accountability** | *Who* did this — a named person or service, not "the system" |
| **Forensics** | *What exactly* changed — this field went from X to Y |
| **History** | *When*, and did it succeed or fail |
| **Evidence for correction** | Because the before-value was captured, a human can see what to put back |

That last one has a sharp edge on it, which [chapter 11](#11-the-revert-question) is
entirely about.

---

## 2. Audit is not logging

They feel like the same thing — both are "records of stuff that happened" — and treating
them as the same thing is the most common mistake in this area.

| | **Logs** | **Audit trail** |
|---|---|---|
| Question | What was the application doing? | Who changed what data? |
| Audience | Developers | Compliance, security, support |
| Content | Free-form technical messages | Structured who / what / before → after |
| Coverage | Everything, noisy | Only meaningful **writes** |
| Retention | Days | Months or years |
| Attitude | Disposable | Kept, sometimes tamper-evident |

**Why this matters:** those differences are all *operational*. Different retention, different
access control, different consumers, different volume. Put audit records in your log stream
and you inherit the log stream's 30-day retention and its "everyone in engineering can read
it" access model — for the data most likely to be subject to neither.

That is why audit gets its own destination, and why the destination is swappable.

---

## 3. What one record contains

A real record, exactly as stored:

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
        { "name": "Email",     "newValue": "grace.hopper@navy.mil" }
      ]
    }
  ]
}
```

Read it as a sentence: *integration-service ran CreateInstructor at 19:14, it succeeded in
106ms, and it added an Instructor with these values.*

| Field | Meaning |
|---|---|
| `actor` | **Who** — the authenticated user or service |
| `action` | **What** — the command name |
| `occurredOnUtc` | **When** — UTC |
| `succeeded` / `error` | Outcome, and why not |
| `elapsedMs` | How long it took |
| `correlationId` | Ties this record to the logs and traces of the same request |
| `changes[]` | Per entity: type, id, operation, and each property's old → new |

The shape of `changes` depends on the operation: an **add** has only `newValue`, a
**delete** only `oldValue`, an **edit** has both. Property values are stored as strings so
one index can hold changes from every entity type in the system without mapping conflicts,
and they are truncated at 512 characters.

**Sensitive values are never stored.** Any property whose name contains `password`,
`secret`, `token`, `apikey`, `api_key`, `salt`, `hash` or `credential` is recorded as
`***REDACTED***`. The match is on the *name*, case-insensitively, so a field called
`PasswordHash` is caught twice over.

---

## 4. The four moving parts

Capture is automatic, but it is not magic. Four pieces, and it is worth knowing which does
what before you debug a missing record.

```
   Command marked IAuditableRequest
        │
        ▼
   ┌──────────────────────────────────────────────────────────┐
   │ AuditBehavior          starts a stopwatch, notes the actor│
   │   ├─ ValidationBehavior                                   │
   │   ├─ TransactionBehavior                                  │
   │   │     └─ your Handler                                   │
   │   │           │ SaveChanges                               │
   │   │           ▼                                           │
   │   │     AuditingSaveChangesInterceptor                    │
   │   │        captures before/after ──► IAuditScope          │
   │   │           │ commit                                    │
   │   ◄───────────┘                                           │
   │ AuditBehavior reads the scope, builds an AuditEntry,      │
   │ hands it to IAuditSink                                    │
   └───────────────────────────┬──────────────────────────────┘
                               ▼
                          IAuditSink   ── logs, or Elasticsearch, or yours
```

| Piece | Job |
|---|---|
| `IAuditableRequest` | A marker interface. Its presence is the opt-in |
| `AuditBehavior` | Wraps the command: who, what, when, outcome, duration |
| `AuditingSaveChangesInterceptor` | Hooks EF's `SaveChanges` and captures before/after values |
| `IAuditScope` | A per-request notepad the interceptor writes to and the behaviour reads back |
| `IAuditSink` | Where the finished record goes. Swappable |

Two design decisions in there are worth understanding, because they explain behaviour that
looks wrong at first:

**The behaviour sits *outside* validation.** So a command rejected by its validator is
still audited, with `succeeded: false` and the error. That is deliberate — an audit trail
that only records successes cannot answer "what did they *try* to do?", which is exactly
the question asked after a security incident.

**The interceptor sits *inside* the transaction.** So it only reports changes that actually
committed. A command that throws produces an audit record with `succeeded: false` and an
empty `changes` array — the attempt is recorded, the rolled-back data is not, because it
never existed.

---

## 5. Step 1 — Audit a command

Add one interface to the command:

```csharp
public static class CreateFocus
{
    public sealed record Command(string Name, string? Description)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;
                                                //  ^^^^^^^^^^^^^^^^^ this
```

That is the entire opt-in. No handler changes, no calls to make, nothing to remember at
the call site.

### What gets used as the "action" name

The action is taken from the **enclosing type's** name, not the command's. Vertical-slice
commands are nested types, so `typeof(TRequest).Name` would be the useless string
`"Command"` for every command in the system. The behaviour reads
`requestType.DeclaringType?.Name` instead, which gives you `CreateFocus`.

Practical consequence: **your feature class name is what appears in the audit trail**, so
name it as an action someone investigating would search for. `WithdrawStudent` is a good
audit action. `StudentUpdateHandlerV2` is not.

### What to mark, and what not to

Mark **writes that change data a person could be asked about**. Do not mark queries — they
carry no marker, so they are skipped entirely and cost nothing.

| Mark it | Leave it |
|---|---|
| Creates, edits, deletes of business records | Any query |
| State transitions (withdraw, cancel, approve) | Health checks, metrics scrapes |
| Money, permissions, personal data | Internal bookkeeping (outbox plumbing) |
| Anything a regulator or a customer might query | Cache warming |

> A tempting mistake is to mark everything "to be safe". Auditing everything produces a
> trail nobody reads, at a storage cost nobody budgeted for. The value of an audit trail is
> inversely proportional to how much noise you have to filter out of it.

---

## 6. Step 2 — Capture before and after

The marker alone gives you who/what/when/outcome. To also capture **field-level changes**,
the module's `DbContext` needs the audit interceptor attached. Two lines, in the module's
registration:

```csharp
public static IServiceCollection AddTesterGuideModule(
    this IServiceCollection services, string connectionString)
{
    services.AddTesterGuideApplication();

    services.AddAuditChangeTracking();                                    // 1
    services.AddDbContext<TesterGuideDbContext>((sp, options) =>
        options.UseSqlite(connectionString).UseAuditChangeTracking(sp));  // 2

    // ...
}
```

1. Registers the interceptor. It is idempotent (`TryAddScoped`), so every module calls it
   without coordinating.
2. Attaches it to *this* module's context.

**The `(sp, options)` overload is not optional.** The interceptor is scoped — it holds the
per-request audit scope — so it has to be resolved from the service provider. The
single-argument overload of `AddDbContext` compiles perfectly and then fails at runtime
with a lifetime error, or worse, silently captures nothing.

### What the interceptor skips

The outbox table. Outbox rows are infrastructure plumbing, not business data, and a saga
that writes six messages would otherwise bury the one change a human cares about.

**Why this matters:** if you add a new module and forget these two lines, auditing does not
break. Records still appear — with an empty `changes` array. It looks like it works. That
is the failure mode to watch for, and [chapter 9](#9-step-5--verify-it) is how you catch it.

---

## 7. Step 3 — Choose where records go

`IAuditSink` is a one-method interface, and it is the seam that makes the destination a
configuration decision rather than a code change:

```csharp
public interface IAuditSink
{
    Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken);
}
```

### The default: structured logs

Out of the box, records go to `LoggingAuditSink`, which writes each one as a structured log
event. Fields arrive as queryable properties rather than interpolated text, so any
structured log store can search them.

> **An honest limitation you need to know about.** The logging sink records the summary —
> correlation id, action, actor, succeeded, elapsed, error. It does **not** write the
> `changes` array. So the fallback path gives you accountability but not forensics. If
> before/after values matter to you, the logging sink is not sufficient on its own, and you
> should treat a silent fall-back to it as an incident rather than a shrug.

### Shipping to Elasticsearch

One line in `Program.cs`:

```csharp
builder.Services
    // ...
    .AddElasticsearchAudit(builder.Configuration)
```

configured under `Audit:Elasticsearch`:

```json
"Audit": {
  "Elasticsearch": {
    "Uri": "http://localhost:9200",
    "IndexFormat": "cleanarch-audit-{0:yyyy.MM.dd}",
    "ApiKey": "",
    "Username": "",
    "Password": ""
  }
}
```

| Setting | Notes |
|---|---|
| `Uri` | **Empty disables the sink entirely** and leaves the logging sink in place. A missing config is safe, not broken |
| `IndexFormat` | Formatted with the record's UTC timestamp — one index per day, which is what lets a retention policy delete by age |
| `ApiKey` | Preferred over username/password. Overrides them if both are set |
| `Username` / `Password` | Basic auth. Supply from environment or a secret store, never `appsettings.json` |

Registration is deliberately last-wins: the Elasticsearch sink replaces the logging sink
that was registered earlier with `TryAdd`.

### Why shipping is non-blocking

The sink does not make a network call on your request thread. It hands the record to an
in-memory queue; a background hosted service batches records into bulk requests and
retries.

The consequence is the important part: **if Elasticsearch is slow or down, your API stays
fast and your command still succeeds.** Audit shipping can never become an availability
dependency of the thing being audited. The queue has a capacity (10,000 by default) and
falls back to logging when full, so a prolonged outage degrades rather than exhausts
memory.

**Why this matters:** the alternative design — write the audit record synchronously, fail
the command if it can't be written — is genuinely defensible for some compliance regimes.
Know which one you are building. This is the fast one, and it trades a small window of
possible loss for never taking the API down.

---

## 8. Step 4 — Make the actor trustworthy

An audit trail is only as good as its `actor` field. Everything else can be perfect and
the record is still worthless if "who" can be forged.

The actor comes from `ICurrentActor`. The implementation here resolves it in three steps:

```csharp
public string Current
{
    get
    {
        var user = _accessor.HttpContext?.User;
        if (user?.Identity is { IsAuthenticated: true })
            return user.Identity.Name ?? "authenticated";

        var header = _accessor.HttpContext?.Request.Headers["X-Actor"].ToString();
        return string.IsNullOrWhiteSpace(header) ? "system" : header;
    }
}
```

1. **The authenticated principal** — trustworthy.
2. **The `X-Actor` header** — a development convenience, and **anyone can set it**.
3. **`"system"`** — background work.

> ### The thing to fix before you rely on this
>
> Step 2 is spoofable by definition. A caller who is not authenticated can claim to be
> anyone by sending a header. For a POC that is a reasonable shortcut; for a real audit
> trail it is a hole straight through the middle of the feature.
>
> The fix is not in the auditing code — it is to **require authentication on every
> auditable command**, so step 1 always wins and step 2 is never reached. Auditing
> inherits its trustworthiness from authentication; it cannot manufacture it.

For background work — an outbox dispatcher, a scheduled job — there is no user, and
`"system"` is the honest answer. If you need to distinguish *which* background process,
give each one its own actor rather than letting them all report as `system`.

---

## 9. Step 5 — Verify it

Do not assume. The failure modes here are all silent.

**1. Run a command that writes something.**

```bash
curl -X POST http://localhost:5235/instructors \
  -H "X-Api-Key: dev-api-key-integration" \
  -H "Content-Type: application/json" \
  -d '{"firstName":"Grace","lastName":"Hopper","email":"grace.hopper@navy.mil","departmentName":"Computer Science","rank":2}'
```

**2. Confirm a record exists.**

```bash
curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"
```

**3. Now check the part that fails silently** — that `changes` is populated:

```bash
curl -s "http://localhost:9200/cleanarch-audit-*/_search?pretty" | grep -c '"properties"'
```

If you get records but every `changes` array is empty, the interceptor is not attached to
that module's `DbContext` — go back to [chapter 6](#6-step-2--capture-before-and-after).
This is the single most common problem, and nothing anywhere reports it as an error.

**4. Check a failure is recorded too.** Send the same request again to trigger a duplicate;
you should get a new record with `succeeded: false`, an `error`, and empty `changes`.

**5. Check redaction**, if you have any sensitive field: confirm it reads `***REDACTED***`
rather than the value.

---

## 10. Reading the trail

The store holds one index per day (`cleanarch-audit-2026.08.26`). Kibana needs to be told
to treat that family as one searchable thing — a **data view**.

1. Kibana → **Stack Management → Data Views → Create data view**
2. Index pattern: `cleanarch-audit-*`
3. Time field: **`occurredOnUtc`**
4. Save, then open **Discover**

Useful searches:

```
actor : "integration-service"
action : "WithdrawStudent"
succeeded : false
action : "Delete*" and not actor : "system"
```

Expand a row to read `changes` — the entity, the operation, and the before → after values.
Add the actor, action and entity id as columns and Discover becomes an actual audit report
rather than a wall of JSON.

> **The casing trap.** Stored field names are **camelCase** — `actor`, not `Actor`;
> `occurredOnUtc`, not `OccurredOnUtc`. That is how the Elasticsearch client serialises
> them. A query with the wrong casing returns zero results and no error, which reads
> exactly like "there is no data".

The other thing that reads like "no data" is the time picker, which defaults to the last 15
minutes. Widen it before concluding anything.

---

## 11. The revert question

Sooner or later someone asks: *we captured the before-values — can we add a button that
reverts an action straight from the audit trail?*

**No. Do not drive writes from your audit store.**

The reasons are not stylistic:

- **It is not your system of record.** It is a search and observability store. It has
  retention limits, it can be reindexed, it can be administered by people who are not your
  DBAs.
- **It is not in your transaction.** A revert read from it has no consistency relationship
  with the data it is reverting.
- **It is lossy by design.** Values are truncated at 512 characters and sensitive fields
  are redacted. A "revert" would write `***REDACTED***` into a real column.
- **Naive reverse-replay corrupts data.** The moment there are dependent changes,
  concurrency, or cascades, playing entries backwards produces a state the system was never
  in and no rule ever validated.

### What to do instead

Treat the captured before/after as **evidence for a human**, and perform the actual
correction as a **compensating command** — a new, first-class domain operation that sets
the values back, enforces the same invariants as any other write, and is **itself audited**.

An `UnwaiveCharge` that reverses a `WaiveCharge` leaves you with two honest records of two
real decisions. A silent revert-from-log leaves you with a mutation nobody authorised and
no record of who triggered it.

> **Kibana is where you investigate. A compensating command is how you fix.** The audit
> trail informs the revert; it never performs it.

---

## 12. Retention and tamper-resistance

Three things separate an audit trail that will hold up from one that merely exists.

**1. Retention you chose.** Audit indices accumulate forever unless told otherwise. Unlike
telemetry, "delete after 30 days" is not a sensible default here — the retention period is
a policy decision, often a legal one. Set it explicitly with an index lifecycle policy on
`cleanarch-audit-*` and a delete phase at your chosen age. Daily indices exist precisely so
this is cheap.

**2. Write-only credentials.** The application should ship audit records using an account
that can **create and write** to `cleanarch-audit-*` and nothing else — it cannot read them
back, cannot delete them, cannot touch another index. If the application server is
compromised, the attacker gets an append-only pipe. See the
[Ubuntu server guide](90-observability-server-ubuntu.md) for the role definition that does
this.

**3. Separation of duties.** The people who can *write* audit records should not be the
people who can *delete* them. If one credential can do both, the trail is evidence of
nothing to anyone determined enough to matter.

For strict compliance regimes, go further: append-only indices, a separate cluster with
separate administrators, or shipping a copy to write-once storage. Those are real costs;
spend them where the requirement is real.

---

## 13. Writing your own sink

The destination is one interface, so a new one is a small class:

```csharp
internal sealed class SqlAuditSink : IAuditSink
{
    public async Task RecordAsync(AuditEntry entry, CancellationToken cancellationToken)
    {
        // insert into your audit table
    }
}
```

Register it after the defaults so it wins:

```csharp
services.AddScoped<IAuditSink, SqlAuditSink>();
```

When you might want to: a regulator requires audit in the same relational database as the
data (so it is inside your backup and your transaction story); or you want to write to two
places at once, which is a decorator that wraps two sinks and awaits both.

Two rules if you write one:

- **Do not make it slow.** It runs inside the request. Anything involving a network should
  queue and return, the way the Elasticsearch sink does.
- **Do not let it throw.** An audit sink that throws turns a successful business operation
  into a failed one. Catch, log loudly, and let the command succeed — or, if your regime
  genuinely requires "no audit, no write", make that a deliberate, documented decision
  rather than an accident of an unhandled exception.

---

## 14. The checklist

Per command:

- [ ] The command carries `IAuditableRequest`
- [ ] The feature class is named as an action worth searching for
- [ ] Queries carry no marker

Per module:

- [ ] `services.AddAuditChangeTracking()`
- [ ] `.UseAuditChangeTracking(sp)` on the `DbContext`, using the `(sp, options)` overload

Per application:

- [ ] A sink chosen deliberately — not left on the logging fallback by accident
- [ ] `Audit:Elasticsearch:Uri` set, and credentials supplied from the environment
- [ ] The shipping account is **write-only** to the audit indices
- [ ] Authentication required on auditable commands, so `actor` is real
- [ ] A retention policy chosen and applied
- [ ] Verified end to end, including that `changes` is populated

---

## 15. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No audit records at all | The command has no `IAuditableRequest` | [Chapter 5](#5-step-1--audit-a-command) |
| Records appear, `changes` always empty | Interceptor not attached to that module's `DbContext` | [Chapter 6](#6-step-2--capture-before-and-after) |
| Records only in the logs, never in Elasticsearch | `Audit:Elasticsearch:Uri` is empty — the sink is a deliberate no-op | Set the URI |
| `Cannot consume scoped service` at startup | Used the one-argument `AddDbContext` overload | Use `(sp, options) => …` |
| `action` is `"Command"` for everything | The command is not nested inside a feature class | Follow the vertical-slice shape |
| Kibana Discover shows nothing | Time range, or field casing | Widen the range; fields are camelCase |
| Data view won't accept the time field | Wrong field name | It is `occurredOnUtc` |
| `actor` is `"system"` for real user actions | The request was not authenticated | Require authorization on the endpoint |
| `actor` is a name you do not recognise | Someone set `X-Actor` on an unauthenticated call | [Chapter 8](#8-step-4--make-the-actor-trustworthy) — this is the hole, close it |
| A sensitive value appears in the trail | Its property name matches no redaction marker | Rename the property, or extend the marker list |
| Audit records stop during heavy load | The shipment queue filled and fell back to logs | Raise `QueueCapacity`, or investigate why Elasticsearch is not keeping up |

---

## 16. Cheat sheet

### The code you write

```csharp
// Audit one command — the entire opt-in
public sealed record Command(...) : IRequest<Guid>, I<Module>Command, IAuditableRequest;

// Capture before/after for a module (in Add<Module>Module)
services.AddAuditChangeTracking();
services.AddDbContext<<Module>DbContext>((sp, options) =>
    options.UseSqlite(connectionString).UseAuditChangeTracking(sp));   // (sp, options) is required

// Ship records to Elasticsearch (in Program.cs)
builder.Services.AddElasticsearchAudit(builder.Configuration);

// Replace the destination entirely
services.AddScoped<IAuditSink, YourSink>();                            // last registration wins
```

### The settings

| Key | Effect |
|---|---|
| `Audit__Elasticsearch__Uri` | The endpoint. **Empty = sink disabled**, falls back to logs |
| `Audit__Elasticsearch__IndexFormat` | Default `cleanarch-audit-{0:yyyy.MM.dd}` — one index per day |
| `Audit__Elasticsearch__ApiKey` | Preferred credential; overrides username/password |
| `Audit__Elasticsearch__Username` / `__Password` | Basic auth. From the environment, never appsettings |
| `Audit__Elasticsearch__QueueCapacity` | Buffered records before falling back to logs (default 10,000) |

Double underscores are the environment-variable form of the `:` separator.

### Verifying

```bash
# Is anything arriving at all?
curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"

# Are before/after values being captured? (0 means the interceptor is not attached)
curl -s "http://localhost:9200/cleanarch-audit-*/_search?pretty" | grep -c '"properties"'

# What indices exist, and how many records in each?
curl "http://localhost:9200/_cat/indices/cleanarch-audit-*?v"

# One actor's activity
curl "http://localhost:9200/cleanarch-audit-*/_search?q=actor:alice&pretty"
```

### Reading it in Kibana

| | |
|---|---|
| Data view pattern | `cleanarch-audit-*` |
| Time field | `occurredOnUtc` |
| Field casing | **camelCase** — `actor`, `action`, `succeeded`, `changes` |
| First thing to check when empty | The time range, then the casing |

---

## 17. Glossary

| Term | Meaning |
|---|---|
| **Action** | Which command ran. Taken from the feature class name, e.g. `WithdrawStudent` |
| **Actor** | Who performed it — an authenticated user or a service |
| **Append-only** | A store you may add to but not edit or delete from. The goal for a strict audit trail |
| **Audit trail** | The record of who changed what data, when |
| **Change-set** | The list of entity changes captured for one command (`changes[]`) |
| **Compensating command** | A new, first-class operation that reverses an earlier one — the safe way to "undo" |
| **Correlation id** | The per-request id that ties an audit record to the logs and traces of the same request |
| **Data view** | Kibana's saved index pattern: which indices, and which field is the timestamp |
| **Forensics** | Establishing exactly what changed, as opposed to merely who acted |
| **ILM (index lifecycle management)** | Elasticsearch's rules for rolling over and deleting old indices — how retention is enforced |
| **Index** | Elasticsearch's unit of storage. Here, one per day |
| **Interceptor** | Code hooked into EF's `SaveChanges` to observe writes as they commit |
| **Marker interface** | An empty interface used as a switch. `IAuditableRequest` is the audit opt-in |
| **Pipeline behaviour** | Cross-cutting code wrapped around a handler. `AuditBehavior` is one |
| **Redaction** | Replacing a sensitive value with `***REDACTED***` at capture time |
| **Separation of duties** | Ensuring whoever can write audit records cannot also delete them |
| **Sink** | The destination audit records are sent to (`IAuditSink`) |
| **System of record** | The authoritative store for a piece of data. The audit trail is *not* one |
| **Tamper-evident** | Designed so that alteration can be detected, even if not prevented |

---

## Appendix — The files involved

Nothing in this list is something you write; it is where to look when something is wrong.

```
src/BuildingBlocks/
├── Messaging/IAuditableRequest.cs                  the marker — the opt-in
├── Messaging/Behaviors/AuditBehavior.cs            wraps the command; builds the record
└── Auditing/
    ├── AuditEntry.cs                               the record's shape
    ├── EntityChange.cs                             per-entity + per-property changes
    ├── IAuditScope.cs                              the per-request notepad
    ├── IAuditSink.cs                               where records go — the seam
    ├── ICurrentActor.cs                            "who" — the weakest link
    └── LoggingAuditSink.cs                         the default. Summary only, no changes[]

src/BuildingBlocks.Persistence/
├── AuditingSaveChangesInterceptor.cs               captures before/after; redacts; skips outbox
└── AuditChangeTrackingRegistration.cs              AddAuditChangeTracking / UseAuditChangeTracking

src/BuildingBlocks.Auditing.Elasticsearch/
├── DependencyInjection.cs                          AddElasticsearchAudit — no-op if Uri empty
├── ElasticsearchAuditOptions.cs                    the settings above
├── ElasticsearchAuditSink.cs                       hands records to the queue, returns
├── AuditShipmentQueue.cs                           the in-memory buffer
└── ElasticsearchAuditShipper.cs                    background bulk indexer with retry

src/Api/CleanArch.Api/
└── HttpContextActor.cs                             principal, else X-Actor header, else "system"
```

---

## Where to go next

- **[Adding a new module](30-add-a-module.md)** — the two lines that put a new module's
  writes into the trail are part of its registration.
- **[Observability server on Ubuntu](90-observability-server-ubuntu.md)** — how to stand up
  the Elasticsearch and Kibana this guide ships to, including the write-only account.
