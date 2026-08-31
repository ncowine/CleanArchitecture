# Auditing — Who Changed What, and Who Looked

**Who this is for:** someone who needs a defensible record of what was done through their
application — for compliance, for support, or for the day somebody asks "who did this?" and
"the database" is not an acceptable answer.

**What you'll be able to do by the end:** make any command audited, capture before-and-after
values of every field it changes, audit sensitive **reads** as well as writes, record things
the pipeline cannot see at all (a vendor API call, a denied permission), choose where those
records are stored, make the "who" trustworthy, and read the trail back. And you'll know the
one thing you must never build on top of it.

**What you need first:** nothing beyond a running application. Auditing is opt-in per
request and costs one interface.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What an audit trail is](#1-what-an-audit-trail-is) | Understand the job it does |
| 2 | [Audit is not logging](#2-audit-is-not-logging) | Why it gets its own store |
| 3 | [What one record contains](#3-what-one-record-contains) | Read a real record |
| 4 | [The moving parts](#4-the-moving-parts) | How capture actually works |
| 5 | [Step 1 — Audit a command](#5-step-1--audit-a-command) | One interface |
| 6 | [Step 2 — Audit a read](#6-step-2--audit-a-read) | Who *looked* at it |
| 7 | [Step 3 — Audit what the pipeline can't see](#7-step-3--audit-what-the-pipeline-cant-see) | Vendor calls, caches, security events |
| 8 | [Step 4 — Capture before and after](#8-step-4--capture-before-and-after) | Two lines per module |
| 9 | [Step 5 — Choose where records go](#9-step-5--choose-where-records-go) | Sinks, and the fallback trap |
| 10 | [Step 6 — Make the actor trustworthy](#10-step-6--make-the-actor-trustworthy) | The weakest link |
| 11 | [Step 7 — Verify it](#11-step-7--verify-it) | Prove it, don't assume |
| 12 | [Reading the trail](#12-reading-the-trail) | Kibana, and the casing trap |
| 13 | [The revert question](#13-the-revert-question) | Read this before building "undo" |
| 14 | [Retention and tamper-resistance](#14-retention-and-tamper-resistance) | Making it hold up |
| 15 | [Writing your own sink](#15-writing-your-own-sink) | The extension point |
| 16 | [The checklist](#16-the-checklist) | Run this when doing it for real |
| 17 | [Troubleshooting](#17-troubleshooting) | Symptom, cause, fix |
| 18 | [Cheat sheet](#18-cheat-sheet) | Settings and commands, in one place |
| 19 | [Glossary](#19-glossary) | Every term used in this guide |

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

That last one has a sharp edge on it, which [chapter 13](#13-the-revert-question) is
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
| Coverage | Everything, noisy | Only what you opted in — meaningful writes, sensitive reads |
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
  "category": "Write",
  "source": null,
  "resource": null,
  "details": null,
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
| `category` | `Write` / `Read` / `External` / `Security` / `Custom` — what *kind* of activity this was |
| `source` | Where the data lived when it wasn't our own database: `Api:CreditBureau`, `Cache:students`. Null means our DB |
| `resource` | **Whose** data — a searchable identifier like `Student/7f3…` |
| `details` | Free-form facts the code attached: row counts, upstream request ids |
| `changes[]` | Per entity: type, id, operation, and each property's old → new |

Not every record has every field. A read has no `changes[]` — nothing changed — and a
database write has no `source`. The same shape covers all of them, which is the point: one
index, one data view, one query language for every kind of auditable activity.

A read record for the same trail, produced by the query in
[chapter 6](#6-step-2--audit-a-read):

```json
{
  "correlationId": "9a1f0c72-3f5e-4a91-8d02-6c9b4c1e5f77",
  "actor": "clerk@uni",
  "action": "GetStudentLoans",
  "occurredOnUtc": "2026-08-30T09:02:11Z",
  "succeeded": true,
  "elapsedMs": 14,
  "error": null,
  "category": "Read",
  "source": null,
  "resource": "Student/bd0034a3-832a-4399-b106-54d03a223898",
  "details": { "loansReturned": "3", "identitySource": "Module:Students" },
  "changes": []
}
```

Read as a sentence: *clerk@uni looked at that student's loans at 09:02 and three came back.*
Nothing changed, and that is exactly the event worth recording.

The shape of `changes` depends on the operation: an **add** has only `newValue`, a
**delete** only `oldValue`, an **edit** has both. Property values are stored as strings so
one index can hold changes from every entity type in the system without mapping conflicts,
and they are truncated at 512 characters.

**Sensitive values are never stored.** Any property whose name contains `password`,
`secret`, `token`, `apikey`, `api_key`, `salt`, `hash` or `credential` is recorded as
`***REDACTED***`. The match is on the *name*, case-insensitively, so a field called
`PasswordHash` is caught twice over.

That policy lives in one place (`AuditRedaction`) and applies to **every** route into the
trail — the interceptor's captured columns and the `details` a caller writes by hand are
redacted, truncated and capped by the same rules. A single list, because a second copy of it
is a second chance to forget a marker.

---

## 4. The moving parts

Capture is automatic, but it is not magic. It is worth knowing which piece does what before
you debug a missing record.

```
   Request marked IAuditableRequest (write) or IAuditableRead (read)
        │
        ▼
   ┌──────────────────────────────────────────────────────────┐
   │ AuditBehavior          starts a stopwatch, notes the actor│
   │   ├─ ValidationBehavior                                   │
   │   ├─ TransactionBehavior                                  │
   │   │     └─ your Handler                                   │
   │   │           │ SaveChanges      Annotate() ──► IAuditScope│
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
                               ▲
                               │  anything not in the pipeline at all:
   IAuditRecorder ─────────────┘  vendor calls, cache hits, denied permissions
```

| Piece | Job |
|---|---|
| `IAuditableRequest` | A marker interface. Its presence is the opt-in. Alone, it means a write |
| `IAuditableRead` | The same opt-in for a query. Records it as `category: Read` |
| `AuditBehavior` | Wraps the request: who, what, when, outcome, duration |
| `AuditingSaveChangesInterceptor` | Hooks EF's `SaveChanges` and captures before/after values |
| `IAuditScope` | A per-request notepad — the interceptor writes changes to it, handlers write annotations, the behaviour reads both back |
| `IAuditRecorder` | Records activity that never passes through the pipeline at all ([chapter 7](#7-step-3--audit-what-the-pipeline-cant-see)) |
| `IAuditSink` | Where every finished record goes, from either route. Swappable |

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
`"Command"` for every command in the system. The behaviour asks `RequestName.Feature(…)` for
the enclosing type's name instead, which gives you `CreateFocus`.

That rule lives in one place because logs need it too: `LoggingBehavior` calls
`RequestName.Display(…)` for the fuller `CreateFocus.Command`, since a log line is read on its
own and has no surrounding record to say whether a read or a write ran.

Practical consequence: **your feature class name is what appears in the audit trail**, so
name it as an action someone investigating would search for. `WithdrawStudent` is a good
audit action. `StudentUpdateHandlerV2` is not.

### What to mark, and what not to

Mark **writes that change data a person could be asked about**. Anything carrying no marker
is skipped entirely and costs nothing.

| Mark it | Leave it |
|---|---|
| Creates, edits, deletes of business records | Internal bookkeeping (outbox plumbing) |
| State transitions (withdraw, cancel, approve) | Health checks, metrics scrapes |
| Money, permissions, personal data | Cache warming |
| Anything a regulator or a customer might query | Anything with no human decision behind it |

Queries are a separate decision, not an automatic no: a query that exposes one person's
sensitive data can be audited too, with `IAuditableRead` — that is
[chapter 6](#6-step-2--audit-a-read).

> A tempting mistake is to mark everything "to be safe". Auditing everything produces a
> trail nobody reads, at a storage cost nobody budgeted for. The value of an audit trail is
> inversely proportional to how much noise you have to filter out of it.

---

## 6. Step 2 — Audit a read

Everything so far records changes. But "who **looked** at this?" is a question regulated
data asks just as loudly — a nurse opening a patient record they had no reason to open
changes nothing, and is exactly the incident an audit trail exists to catch.

A query opts in with a different marker:

```csharp
public sealed record Query(Guid StudentId, int Page = 1, int PageSize = 20)
    : PagedRequest(Page, PageSize), IRequest<Response>, IAuditableRead
{                                                    // ^^^^^^^^^^^^^^^ this
    // Names whose data was read, so the record answers "whose?" and not just "which query?"
    public string AuditResource => $"Student/{StudentId}";
}
```

`IAuditableRead` extends `IAuditableRequest`, so the same behaviour picks it up and the same
sink stores it. The difference is one field: the record is stamped
`category: "Read"` instead of `category: "Write"`, which is what keeps "who changed this"
and "who looked at this" separable in a store that holds both.

### Name the resource, not just the action

`AuditResource` is available on **every** auditable request, read or write, and it is worth
setting on anything scoped to one subject:

```csharp
public string AuditResource => $"Student/{StudentId}";
```

Without it, a record says `GetStudentLoans` ran. With it, the record says whose loans were
read — the difference between a trail you can search by *person* and one you can only search
by *feature*. Investigations start from a person far more often than from a query name.

> **A create cannot name its resource.** The behaviour reads `AuditResource` off the request
> *before* the handler runs, so an id generated inside the handler does not exist yet and the
> field stays null. That is not a gap: for a create, the `changes[]` array already carries the
> new entity's id. `AuditResource` earns its keep on requests that name an existing
> subject — reads, updates, state transitions.

### Read auditing is opt-in for a reason

Reads outnumber writes by orders of magnitude. Auditing all of them costs real storage and
buries the records that matter under dashboard refreshes and health checks.

| Audit the read | Leave it |
|---|---|
| One named person's records — medical, financial, HR, PII | List and search screens over non-sensitive data |
| Exports and bulk downloads (the shape data-theft takes) | Reference data — lookups, catalogues, code tables |
| Anything a subject could file an access request about | Anything the UI polls on a timer |

> **Why this matters:** the value of read auditing collapses if you turn it on everywhere.
> A trail where 99.9% of the records are a dashboard refreshing is one nobody reads, and
> "nobody reads it" is indistinguishable from not having one.

---

## 7. Step 3 — Audit what the pipeline can't see

The behaviour audits requests, and the interceptor captures database writes. Plenty of
things worth auditing are neither: a credit-bureau lookup, a file drop consumed off a share,
a permission check that said no. None of them pass through a command handler, and none of
them touch your `DbContext`.

Inject `IAuditRecorder` and record them explicitly. The ambient fields — actor, correlation
id, timestamp — are filled in for you, and the record goes to the **same sink**, so a vendor
API call and a database write land side by side in one view.

```csharp
await _audit.RecordAsync(new AuditFact("PermissionDenied")
{
    Category = AuditCategory.Security,
    Resource = $"Student/{studentId}",
}.With("requiredScope", "students.read"), cancellationToken);
```

### Timing something, and recording how it went

For anything that can fail or take time, `TrackAsync` wraps the call and writes exactly one
record either way — success with the elapsed time, or failure with the message. The
exception is rethrown untouched, because auditing must never change behaviour:

```csharp
var score = await _audit.TrackAsync(
    new AuditFact("CreditScoreLookup")
    {
        Category = AuditCategory.External,
        Source = "Api:CreditBureau",
        Resource = $"Student/{studentId}",
    },
    token => _bureau.GetScoreAsync(studentId, token),
    cancellationToken);
```

`Source` is the field that says the data did not come from your own database. Records with
a `source` are the ones to look at when a regulator asks what you sent to third parties.

### Annotating the record already in flight

Sometimes the fact belongs to the request being handled, not to a separate event — how many
rows came back, which cache tier served it, the vendor's request id. `Annotate` attaches it
to **that request's own record** rather than creating a second one:

```csharp
_audit.Annotate("loansReturned", summaries.Count.ToString(CultureInfo.InvariantCulture));
_audit.Annotate("identitySource", "Module:Students");
```

Those arrive under `details` on the same entry the behaviour writes. Annotations survive
failure, too: if the handler throws afterwards, the record is `succeeded: false` and still
carries the annotations — they say how far the request got before it broke.

### The five categories

| Category | Use it for |
|---|---|
| `Write` | A command that changed state. Carries `changes[]`. The behaviour's default |
| `Read` | A data-access request — someone looked. What `IAuditableRead` produces |
| `External` | A system you do not own: third-party API, file drop, queue |
| `Security` | Sign-in, token exchange, permission denied |
| `Custom` | Anything else worth recording. The default for a hand-written `AuditFact` |

The point of the taxonomy is not tidiness — it is that these have different retention
periods, different alerting rules, and different audiences. One `category` field keeps them
in one index while staying filterable apart.

> ### The lifetime trap
>
> `IAuditRecorder` is **scoped**, because it reads the current request's actor and
> correlation id. A singleton that resolves one in its constructor captures the *first*
> request's identity and stamps it on every record thereafter — every vendor call
> attributed to whoever happened to warm the app up.
>
> A singleton that needs to audit should take `IServiceScopeFactory` and resolve a recorder
> per call, or be registered scoped itself. This fails silently: the records look fine,
> the `actor` is just quietly wrong.

### Never record the payload

`Details` is for facts about the call, not the contents of it. Record the vendor's request
id, the row count, the scope that was required — not the response body, not the request
body, not anything you would not want kept for seven years in a store with different access
control from your database.

The same guardrail that protects `changes[]` does apply here — a detail whose **key** looks
like a secret (`vendorApiKey`, `accessToken`) is stored as `***REDACTED***`, values over 512
characters are cut, and a record keeps at most 32 details before dropping the rest and
recording `detailsDropped`. One policy, in `AuditRedaction`, for every route into the trail.

> **What it cannot do is read your mind.** The match is on the key name, so a response body
> passed as `details["result"]` is stored in full, up to the length limit. The guardrail
> catches the accident; it does not license the habit.

---

## 8. Step 4 — Capture before and after

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
is the failure mode to watch for, and [chapter 11](#11-step-7--verify-it) is how you catch it.

---

## 9. Step 5 — Choose where records go

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
> category, correlation id, action, actor, succeeded, elapsed, source, resource, details,
> error. It does **not** write the `changes` array. So the fallback path gives you
> accountability but not forensics. If before/after values matter to you, the logging sink
> is not sufficient on its own, and you should treat a silent fall-back to it as an incident
> rather than a shrug.

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

### Map the index before you ship to a real cluster

Elasticsearch will happily index audit records with no mapping at all — it infers one. That
is fine until `details` arrives, because every distinct annotation key becomes its **own
mapped field**: `details.loansReturned`, `details.bureauReference`, one per key your code
ever writes. The default ceiling is 1000 fields per index, and an index that hits it starts
**rejecting** documents — which this sink logs and drops. The trail thins out, and nothing
in the application looks broken.

`src/BuildingBlocks.Auditing.Elasticsearch/audit-index-template.json` fixes that by mapping
`details` as `flattened`: arbitrary keys, one field, still queryable as `details.key : value`.
Apply it once, before the first record arrives:

```bash
curl -X PUT "http://localhost:9200/_index_template/cleanarch-audit" \
  -H "Content-Type: application/json" \
  --data-binary @src/BuildingBlocks.Auditing.Elasticsearch/audit-index-template.json
```

It applies to indices created *after* it, so on an existing cluster you either wait for
tomorrow's daily index or reindex today's.

> **Why this is an ops step and not something the app does at startup.** The shipping account
> is meant to be write-only to `cleanarch-audit-*`
> ([chapter 14](#14-retention-and-tamper-resistance)). Creating index templates needs cluster
> privileges, and an append-only pipe should not hold them. The application writes records;
> it does not administer the cluster it writes to.

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

## 10. Step 6 — Make the actor trustworthy

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

## 11. Step 7 — Verify it

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
that module's `DbContext` — go back to [chapter 8](#8-step-4--capture-before-and-after).
This is the single most common problem, and nothing anywhere reports it as an error.

**4. Check a failure is recorded too.** Send the same request again to trigger a duplicate;
you should get a new record with `succeeded: false`, an `error`, and empty `changes`.

**5. Check redaction**, if you have any sensitive field: confirm it reads `***REDACTED***`
rather than the value.

**6. Check an audited read.** Call a query marked `IAuditableRead` and confirm a record
arrives with `category: "Read"`, a `resource` naming whose data it was, and empty `changes`:

```bash
curl -s "http://localhost:9200/cleanarch-audit-*/_search?q=category:Read&pretty"
```

If writes appear but reads never do, the query is missing the marker — reads are opt-in
([chapter 6](#6-step-2--audit-a-read)), and nothing warns you that you forgot.

---

## 12. Reading the trail

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

Now that one index holds more than writes, `category` and `resource` are the two fields that
turn it into an investigation tool:

```
category : "Read" and resource : "Student/bd0034a3-832a-4399-b106-54d03a223898"
resource : "Student/bd0034a3-*"
category : "External" and succeeded : false
category : "Security"
category : "Read" and not actor : "system"
```

The second one is the query an access request turns into: **everything that touched this
person**, read and write together, in one timeline. That is what the shared index buys you.

Expand a row to read `changes` — the entity, the operation, and the before → after values.
Add the actor, action, category and resource as columns and Discover becomes an actual audit
report rather than a wall of JSON.

> **The casing trap.** Stored field names are **camelCase** — `actor`, not `Actor`;
> `occurredOnUtc`, not `OccurredOnUtc`. The `category` **values**, however, are PascalCase
> strings: `"Read"`, not `"read"`. That is how the Elasticsearch client serialises
> them. A query with the wrong casing returns zero results and no error, which reads
> exactly like "there is no data".

The other thing that reads like "no data" is the time picker, which defaults to the last 15
minutes. Widen it before concluding anything.

---

## 13. The revert question

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

## 14. Retention and tamper-resistance

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

## 15. Writing your own sink

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

## 16. The checklist

Per request:

- [ ] The command carries `IAuditableRequest`; a sensitive query carries `IAuditableRead`
- [ ] The feature class is named as an action worth searching for
- [ ] `AuditResource` is set on anything scoped to one person or record
- [ ] Queries that are *not* sensitive carry no marker — read auditing is deliberate, not default

Per hand-written record (`IAuditRecorder`):

- [ ] The `Category` is set — `External`, `Security`, or `Custom`; the default is `Custom`
- [ ] `Source` names the third-party system, when the data was not ours
- [ ] `Details` carries facts about the call, never the payload
- [ ] No singleton resolves the scoped recorder at construction

Per module:

- [ ] `services.AddAuditChangeTracking()`
- [ ] `.UseAuditChangeTracking(sp)` on the `DbContext`, using the `(sp, options)` overload

Per application:

- [ ] A sink chosen deliberately — not left on the logging fallback by accident
- [ ] `Audit:Elasticsearch:Uri` set, and credentials supplied from the environment
- [ ] The shipping account is **write-only** to the audit indices
- [ ] Authentication required on auditable commands, so `actor` is real
- [ ] `audit-index-template.json` applied to the cluster, so `details` is mapped `flattened`
- [ ] A retention policy chosen and applied
- [ ] Verified end to end, including that `changes` is populated

---

## 17. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| No audit records at all | The command has no `IAuditableRequest` | [Chapter 5](#5-step-1--audit-a-command) |
| Writes are audited, reads never are | The query has no `IAuditableRead` — reads are opt-in | [Chapter 6](#6-step-2--audit-a-read) |
| `resource` is null everywhere | `AuditResource` not overridden on the request | [Chapter 6](#6-step-2--audit-a-read) |
| Every custom record has the same wrong `actor` | A singleton captured the scoped recorder at construction | [Chapter 7](#7-step-3--audit-what-the-pipeline-cant-see) — the lifetime trap |
| `details` is always null | Nothing called `Annotate`, or it ran in a different DI scope than the request | [Chapter 7](#7-step-3--audit-what-the-pipeline-cant-see) |
| `category` is `Custom` on records that aren't | `AuditFact` defaults to `Custom`; set it explicitly | [Chapter 7](#7-step-3--audit-what-the-pipeline-cant-see) |
| Records appear, `changes` always empty | Interceptor not attached to that module's `DbContext` | [Chapter 8](#8-step-4--capture-before-and-after) |
| Records only in the logs, never in Elasticsearch | `Audit:Elasticsearch:Uri` is empty — the sink is a deliberate no-op | Set the URI |
| `Cannot consume scoped service` at startup | Used the one-argument `AddDbContext` overload | Use `(sp, options) => …` |
| `action` is `"Command"` for everything | The command is not nested inside a feature class | Follow the vertical-slice shape |
| Kibana Discover shows nothing | Time range, or field casing | Widen the range; fields are camelCase |
| Data view won't accept the time field | Wrong field name | It is `occurredOnUtc` |
| `actor` is `"system"` for real user actions | The request was not authenticated | Require authorization on the endpoint |
| `actor` is a name you do not recognise | Someone set `X-Actor` on an unauthenticated call | [Chapter 10](#10-step-6--make-the-actor-trustworthy) — this is the hole, close it |
| A sensitive value appears in the trail | Its property name matches no redaction marker | Rename the property, or extend the marker list |
| Audit records stop during heavy load | The shipment queue filled and fell back to logs | Raise `QueueCapacity`, or investigate why Elasticsearch is not keeping up |
| Records stop arriving; ES logs `Limit of total fields [1000] has been exceeded` | The index template was never applied, so each annotation key became its own field | [Chapter 9](#9-step-5--choose-where-records-go) — apply `audit-index-template.json` |
| A record carries `detailsDropped` | More than 32 details were annotated; the rest were discarded | Annotate summaries, not one key per row |
| A detail reads `***REDACTED***` unexpectedly | Its key contains `token`, `hash`, `secret`… — the match is on the name | Rename the key if the value is genuinely not sensitive |

---

## 18. Cheat sheet

### The code you write

```csharp
// Audit one command — the entire opt-in
public sealed record Command(...) : IRequest<Guid>, I<Module>Command, IAuditableRequest;

// Audit one query — same behaviour, recorded as category: Read
public sealed record Query(Guid StudentId) : IRequest<Response>, IAuditableRead
{
    public string AuditResource => $"Student/{StudentId}";   // whose data — set this
}

// Record something the pipeline never sees (inject IAuditRecorder)
await _audit.RecordAsync(new AuditFact("PermissionDenied")
{
    Category = AuditCategory.Security,
    Resource = $"Student/{studentId}",
}.With("requiredScope", "students.read"), cancellationToken);

// Time it, and record success or failure automatically
var score = await _audit.TrackAsync(
    new AuditFact("CreditScoreLookup") { Category = AuditCategory.External, Source = "Api:CreditBureau" },
    token => _bureau.GetScoreAsync(studentId, token),
    cancellationToken);

// Attach a fact to the record of the request already in flight
_audit.Annotate("loansReturned", count.ToString(CultureInfo.InvariantCulture));

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
# Map the index family (once, before the first record — needs cluster privileges)
curl -X PUT "http://localhost:9200/_index_template/cleanarch-audit" \
  -H "Content-Type: application/json" \
  --data-binary @src/BuildingBlocks.Auditing.Elasticsearch/audit-index-template.json

# Is anything arriving at all?
curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"

# Are before/after values being captured? (0 means the interceptor is not attached)
curl -s "http://localhost:9200/cleanarch-audit-*/_search?pretty" | grep -c '"properties"'

# What indices exist, and how many records in each?
curl "http://localhost:9200/_cat/indices/cleanarch-audit-*?v"

# One actor's activity
curl "http://localhost:9200/cleanarch-audit-*/_search?q=actor:alice&pretty"

# Who read this person's data?
curl "http://localhost:9200/cleanarch-audit-*/_search?q=category:Read&pretty"

# Everything that touched one subject — reads and writes together
curl "http://localhost:9200/cleanarch-audit-*/_search?q=resource:%22Student/<id>%22&pretty"
```

### Reading it in Kibana

| | |
|---|---|
| Data view pattern | `cleanarch-audit-*` |
| Time field | `occurredOnUtc` |
| Field casing | **camelCase** — `actor`, `action`, `succeeded`, `category`, `resource`, `changes` |
| Category values | PascalCase — `Write`, `Read`, `External`, `Security`, `Custom` |
| First thing to check when empty | The time range, then the casing |

---

## 19. Glossary

| Term | Meaning |
|---|---|
| **Action** | Which request ran. Taken from the feature class name, e.g. `WithdrawStudent` |
| **Annotation** | A fact a handler attaches to the record of the request already in flight (`details`) |
| **Actor** | Who performed it — an authenticated user or a service |
| **Append-only** | A store you may add to but not edit or delete from. The goal for a strict audit trail |
| **Audit trail** | The record of who changed — or read — what data, when |
| **Category** | What kind of activity a record describes: `Write`, `Read`, `External`, `Security`, `Custom` |
| **Change-set** | The list of entity changes captured for one command (`changes[]`) |
| **Compensating command** | A new, first-class operation that reverses an earlier one — the safe way to "undo" |
| **Correlation id** | The per-request id that ties an audit record to the logs and traces of the same request |
| **Data view** | Kibana's saved index pattern: which indices, and which field is the timestamp |
| **Forensics** | Establishing exactly what changed, as opposed to merely who acted |
| **ILM (index lifecycle management)** | Elasticsearch's rules for rolling over and deleting old indices — how retention is enforced |
| **Index** | Elasticsearch's unit of storage. Here, one per day |
| **Interceptor** | Code hooked into EF's `SaveChanges` to observe writes as they commit |
| **Marker interface** | An empty interface used as a switch. `IAuditableRequest` and `IAuditableRead` are the audit opt-ins |
| **Pipeline behaviour** | Cross-cutting code wrapped around a handler. `AuditBehavior` is one |
| **Read audit** | A record that someone *looked at* data. Changes nothing, and is often the incident |
| **Recorder** | `IAuditRecorder` — records activity that never passes through the request pipeline |
| **Redaction** | Replacing a sensitive value with `***REDACTED***` at capture time. Applies to `changes[]`, **not** to details you write yourself |
| **Resource** | Whose data a record concerns, as a searchable id — `Student/7f3…` |
| **Separation of duties** | Ensuring whoever can write audit records cannot also delete them |
| **Sink** | The destination audit records are sent to (`IAuditSink`) — shared by every route |
| **Source** | The system data came from when it wasn't our own database — `Api:CreditBureau` |
| **System of record** | The authoritative store for a piece of data. The audit trail is *not* one |
| **Tamper-evident** | Designed so that alteration can be detected, even if not prevented |

---

## Appendix — The files involved

Nothing in this list is something you write; it is where to look when something is wrong.

```
src/BuildingBlocks/
├── Messaging/IAuditableRequest.cs                  the markers — IAuditableRequest, IAuditableRead
├── Messaging/Behaviors/AuditBehavior.cs            wraps the request; builds the record
├── Messaging/RequestName.cs                        how a request is named, for logs and audit
└── Auditing/
    ├── AuditEntry.cs                               the record's shape
    ├── AuditCategory.cs                            Write / Read / External / Security / Custom
    ├── AuditFact.cs                                what a caller supplies to the recorder
    ├── IAuditRecorder.cs                           record / track / annotate — the general capture point
    ├── AuditRecorder.cs                            stamps the ambient actor, correlation id, timestamp
    ├── DependencyInjection.cs                      AddAuditing — sink, actor, scope, recorder
    ├── AuditRedaction.cs                           one redaction/truncation/size policy, every route
    ├── EntityChange.cs                             per-entity + per-property changes
    ├── IAuditScope.cs                              the per-request notepad: changes + annotations
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
├── ElasticsearchAuditShipper.cs                    background bulk indexer with retry
└── audit-index-template.json                       apply once - maps details as flattened

src/Api/CleanArch.Api/
└── HttpContextActor.cs                             principal, else X-Actor header, else "system"
```

---

## Where to go next

- **[Adding a new module](30-add-a-module.md)** — the two lines that put a new module's
  writes into the trail are part of its registration.
- **[Observability server on Ubuntu](90-observability-server-ubuntu.md)** — how to stand up
  the Elasticsearch and Kibana this guide ships to, including the write-only account.
