# Talking Across Modules

**Who this is for:** someone whose module needs something from another module — a piece of
data, or a change made over there — and who has just discovered that the obvious way to do
it isn't available.

**What you'll be able to do by the end:** read another module's data through a published
contract, cause a write in another module's database reliably, make the receiving end safe
against duplicate delivery, and build a two-step process that undoes itself when the second
step says no.

**What you need first:** a module of your own ([guide 30](30-add-a-module.md)), and one
feature in it that works.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [The problem](#1-the-problem) | Understand why you can't just do it |
| 2 | [Two sanctioned routes](#2-two-sanctioned-routes) | Pick the right one for your case |
| 3 | [Reads — published contracts](#3-reads--published-contracts) | The easy half |
| 4 | [Why writes need an outbox](#4-why-writes-need-an-outbox) | The dual-write problem |
| 5 | [What a saga is](#5-what-a-saga-is) | The vocabulary, before the code |
| 6 | [Step 1 — Define the event](#6-step-1--define-the-event) | A record with the ids |
| 7 | [Step 2 — Enqueue it atomically](#7-step-2--enqueue-it-atomically) | One line, in the right place |
| 8 | [Step 3 — Publish the receiving contract](#8-step-3--publish-the-receiving-contract) | The other module's entry point |
| 9 | [Step 4 — Implement the consumer](#9-step-4--implement-the-consumer) | Where idempotency lives |
| 10 | [Step 5 — Route it in the dispatcher](#10-step-5--route-it-in-the-dispatcher) | Type name to method call |
| 11 | [Step 6 — Register the pieces](#11-step-6--register-the-pieces) | Three extension methods |
| 12 | [Idempotency — three strategies](#12-idempotency--three-strategies) | Pick one, deliberately |
| 13 | [Compensation — the two-leg saga](#13-compensation--the-two-leg-saga) | When step two says no |
| 14 | [When delivery keeps failing](#14-when-delivery-keeps-failing) | Retry, dead-letter, replay |
| 15 | [Correlation across the hop](#15-correlation-across-the-hop) | Keeping one flow traceable |
| 16 | [The traps](#16-the-traps) | Four ways to lose an afternoon |
| 17 | [The checklist](#17-the-checklist) | Run this when doing it for real |
| 18 | [Troubleshooting](#18-troubleshooting) | Symptom, cause, fix |
| 19 | [Cheat sheet](#19-cheat-sheet) | The moving parts, in one place |
| 20 | [Glossary](#20-glossary) | Every term used in this guide |

---

## 1. The problem

Your module owns its database. So does every other module. That is the arrangement, and
[guide 30](30-add-a-module.md#2-why-each-module-owns-its-database) explains why it's worth
having.

Now you need this: *"when a library fine pushes a student over the limit, place a hold on
that student."* The fine lives in `library.db`. The hold lives in `students.db`.

The instinct is to inject the other module's `DbContext` and write both. Two reasons not to:

1. **It deletes the boundary.** The moment `Library` writes to `students.db`, the Students
   module can no longer change its own schema, and nobody will find out until it breaks.
2. **It doesn't actually work.** A database transaction lives inside **one** database. There
   is no `BEGIN TRAN` spanning both files. You would commit one and hope for the other.

That second point is the hard constraint. Everything in this guide follows from it.

> **What about distributed transactions?** Two-phase commit exists, and this is not the
> place for it: it needs a coordinator, it holds locks across the whole operation, it
> doesn't work across most modern data stores, and its failure mode is worse than the
> problem it solves. The industry moved to eventual consistency for good reasons.

---

## 2. Two sanctioned routes

| You need | Route | Consistency | Chapter |
|---|---|---|---|
| To **read** something the other module owns | A published contract, called synchronously | Immediate | [3](#3-reads--published-contracts) |
| To **cause a write** in the other module | An outbox event, delivered in the background | Eventual | [4](#4-why-writes-need-an-outbox) onward |

And one route that is never sanctioned: referencing another module's `Infrastructure` or
`Domain` project, injecting its `DbContext`, or querying its tables.

**How to tell which you need.** Ask whether the caller can proceed if the other side is
momentarily unavailable. "Does this student exist?" — no, you need the answer now, that's a
read. "Charge their account" — yes, a few seconds late is fine, that's an event.

---

## 3. Reads — published contracts

The module that **owns** the data publishes an interface in its `*.Contracts` project. That
project has zero dependencies, so anyone can reference it without dragging along a domain
model or an ORM.

```csharp
// src/Modules/Students/Students.Contracts/IStudentDirectory.cs
public sealed record StudentSummary(Guid Id, string FullName, string Email, string Status);

public interface IStudentDirectory
{
    Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken cancellationToken);
}
```

Two things to notice, because both are deliberate:

**It returns a `StudentSummary`, not a `Student`.** The aggregate never crosses the
boundary. The summary carries only the fields outside modules are allowed to depend on — so
the Students module can restructure `Student` freely, as long as it can still produce these
four values.

**The implementation lives in `Students.Infrastructure`** and owns the database access. The
consumer sees an interface and never learns there is a second database involved.

Consuming it is ordinary dependency injection. Your `Application.csproj` references
`Students.Contracts.csproj` and your handler takes the interface:

```csharp
public sealed class Handler : IRequestHandler<Command, Guid>
{
    private readonly IStudentDirectory _students;
    private readonly ILoanRepository _loans;

    public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
    {
        var student = await _students.GetAsync(command.StudentId, cancellationToken)
            ?? throw new DomainException($"No student exists with id '{command.StudentId}'.");
        // ... proceed, knowing the student is real
    }
}
```

### Composing, not joining

When an endpoint needs data from both sides — a student's loans *and* their name — you fetch
from each and combine **in the application layer**:

```csharp
var loans   = await _loans.GetForStudentAsync(studentId, ct);      // library.db
var student = await _students.GetAsync(studentId, ct);             // students.db, via contract
return new Response(student?.FullName ?? "(unknown)", loans);
```

That is two round trips where a monolithic schema would have done one join. Usually
irrelevant; occasionally a real performance conversation, and the answer then is a
purpose-built read model rather than a shortcut through the boundary.

> **Design the contract for the consumer, not the owner.** A contract that returns
> everything "in case someone needs it" recreates the coupling you were avoiding — now
> every field is a promise. Publish the narrowest thing that answers the question.

---

## 4. Why writes need an outbox

The naive version of "change my data, then tell the other module" is two operations:

```csharp
await _loans.SaveAsync(loan);           // 1. commits to library.db
await _holds.PlaceHoldAsync(studentId); // 2. writes to students.db
```

This is the **dual-write problem**, and it is broken in both directions:

- Crash between 1 and 2 → the fine exists, the hold never happens. The modules disagree
  forever, and nothing knows.
- Step 2 succeeds, step 1's transaction rolls back → a hold for a fine that doesn't exist.

Retrying doesn't fix it, because the crash can happen inside the retry. Doing them in the
other order doesn't fix it either. There is no ordering of two independent writes that is
safe.

### The fix: make the message part of your own transaction

The **transactional outbox** turns two writes into one. Instead of calling the other module,
you write a row into an `Outbox` table **in your own database, in the same transaction** as
your business change:

```
   ONE transaction on library.db
   ┌─────────────────────────────────────┐
   │  UPDATE Loans SET FineAmount = 25   │
   │  INSERT INTO Outbox (StudentHold…)  │   ← the message is just another row
   └─────────────────────────────────────┘
              │ commits together, or not at all
              ▼
   OutboxProcessor (background, every 2s)
              │ reads unprocessed rows
              ▼
   IStudentHoldService.PlaceHoldAsync(...)   → writes students.db
```

Both rows commit together or neither does. The gap is gone.

Delivery then becomes a **separate, retryable step**. It can fail, be retried, and succeed
later — the message is safely on disk the whole time. The trade you are making, stated
plainly:

| You get | You accept |
|---|---|
| The event can never be lost once your change commits | The other module finds out *slightly later* |
| Delivery retries automatically | Delivery is **at-least-once** — duplicates happen |
| No coordinator, no distributed locks | Consumers must be **idempotent** |

That last row is not optional, and [chapter 12](#12-idempotency--three-strategies) is
entirely about it.

---

## 5. What a saga is

> A **saga** is a business process spanning several local transactions in different
> modules, coordinated by **events** rather than by one shared transaction. Each step
> commits locally and publishes an event; the next step reacts. If a later step fails,
> earlier steps are undone by **compensating actions** — not by rollback, because you cannot
> roll back a transaction that has already committed in another database.

The vocabulary, once, so the rest of the guide reads cleanly:

| Term | Meaning here |
|---|---|
| **Local transaction** | An ordinary transaction inside one module's database. Each saga step is one |
| **Integration event** | The message one module publishes for another. A plain record |
| **Forward leg** | The happy path — request the hold, place the hold |
| **Reverse / compensating leg** | The undo, when a later step rejects |
| **Compensating action** | A *new* operation that semantically reverses a committed one |
| **Choreography** | Each module reacts to events; no central coordinator. What this repo does |
| **Orchestration** | One coordinator object drives every step. The alternative |
| **At-least-once** | The processor may deliver the same message more than once |
| **Idempotency** | A repeated delivery has no additional effect |
| **Eventual consistency** | The databases agree *eventually*; briefly, they don't |

**Compensation is not rollback.** If you waive a fine to compensate for a hold that couldn't
be placed, the fine *was* charged and is now *waived* — two real events, both in the
history. That is usually what a business actually wants, and it is the only thing available.

---

## 6. Step 1 — Define the event

A plain record carrying exactly what the consumer needs — ids and values, never objects:

```csharp
// src/Modules/Library/Library.Application/Outbox/StudentHoldRequested.cs
public sealed record StudentHoldRequested(Guid StudentId, string Reason);
```

It lives in the **Application** layer's `Outbox` folder, because the command that publishes
it needs to see it.

Three rules:

- **Ids and primitives only.** The event is serialised to JSON and read back by a different
  module, possibly minutes later. A domain object in there is a coupling and a
  deserialisation hazard.
- **Name it in the past tense**, after what happened: `StudentWithdrawn`, `LibraryFineAssessed`.
  `StudentHoldRequested` is named for a *request* because that is genuinely what it is — the
  Library is asking, and the Students module may say no.
- **Treat the shape as a published API.** Once messages of that type are on disk, changing
  the record's fields breaks the deserialisation of anything not yet delivered. Add
  nullable fields; don't rename or remove.

> The type's **name** is the routing key. `OutboxWriter` stores `typeof(TEvent).Name`, and
> the dispatcher switches on that string. Renaming the class renames the routing key, and
> any undelivered messages of the old name will fail to route.

---

## 7. Step 2 — Enqueue it atomically

One call, inside the handler, alongside your normal work:

```csharp
public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
{
    var loan = await _loans.GetAsync(command.LoanId, cancellationToken)
        ?? throw new DomainException($"No loan exists with id '{command.LoanId}'.");

    var priorTotal = await _loans.GetFineTotalAsync(loan.StudentId, cancellationToken);

    loan.AssessFine(command.Amount);
    var newTotal = priorTotal + command.Amount;

    _outbox.Enqueue(new LibraryFineAssessed(loan.StudentId, command.Amount));

    // "Only when required": enqueue on the TRANSITION from under the limit to over it.
    var holdRequested = priorTotal < HoldThreshold && newTotal >= HoldThreshold;
    if (holdRequested)
    {
        _outbox.Enqueue(new StudentHoldRequested(
            loan.StudentId,
            $"Outstanding library fines of {newTotal:0.00} exceed the {HoldThreshold:0.00} limit."));
    }

    return new Result(newTotal, holdRequested);
}
```

That is `src/Modules/Library/Library.Application/Loans/AssessFine.cs`, and two things in it
are worth pausing on.

**`Enqueue` does not save.** It adds a row to the change tracker and returns. The module's
`TransactionBehavior` commits the loan *and* the outbox row together at the end of the
request. That shared transaction is the entire mechanism — it is why the event cannot be
lost, and it is why the handler stays this simple.

**Publish on the transition, not on the state.** `priorTotal < threshold && newTotal >= threshold`
fires once, on the crossing. Publishing whenever `newTotal >= threshold` would enqueue a hold
request on *every subsequent fine*, and you would be relying on the consumer's idempotency to
clean up a mess you created. Idempotency is a safety net for redelivery, not a licence to
publish carelessly.

The same discipline appears in `WithdrawStudent`:

```csharp
var wasActive = student.Status != StudentStatus.Withdrawn;
student.Withdraw();
if (wasActive)                                   // only on the transition
    _outbox.Enqueue(new StudentWithdrawn(student.Id));
```

---

## 8. Step 3 — Publish the receiving contract

The **receiving** module publishes the entry point, in its `*.Contracts` project:

```csharp
// src/Modules/Students/Students.Contracts/IStudentHoldService.cs
public interface IStudentHoldService
{
    Task PlaceHoldAsync(Guid messageId, Guid studentId, string reason, CancellationToken cancellationToken);
}
```

**Note the first parameter.** `messageId` is the outbox row's id, and it is stable across
redeliveries — the same message retried is the same id. It is therefore the perfect
idempotency key, and passing it is what makes the consumer's job possible at all.

Every write contract in this repository takes it. If you write one that doesn't, you have
given the consumer no way to tell a retry from a genuine second request.

---

## 9. Step 4 — Implement the consumer

In the receiving module's `Infrastructure`, against **its own** `DbContext`. Two differences
from a normal handler:

1. It **does** call `SaveChanges` — the dispatcher runs on a background thread, outside any
   request, so there is no `TransactionBehavior` wrapping it.
2. It **must** be idempotent.

The simplest case is naturally idempotent — `FineWaiver` looks at current state and does
nothing if there is nothing to do:

```csharp
public async Task WaiveStudentFinesAsync(Guid studentId, string reason, CancellationToken ct)
{
    var finedLoans = await _db.Loans
        .Where(loan => loan.StudentId == studentId && loan.FineAmount > 0m)
        .ToListAsync(ct);

    if (finedLoans.Count == 0) return;      // already waived — a redelivery is a no-op

    foreach (var loan in finedLoans) loan.WaiveFine();
    await _db.SaveChangesAsync(ct);
}
```

---

## 10. Step 5 — Route it in the dispatcher

Each module has one dispatcher: a switch from the event's type name to the contract call.

```csharp
internal sealed class LibraryOutboxDispatcher : IOutboxDispatcher<LibraryDbContext>
{
    private readonly IStudentHoldService _holds;
    private readonly IStudentBilling _billing;

    public Task DispatchAsync(Guid messageId, string type, string content, CancellationToken ct)
    {
        switch (type)
        {
            case nameof(StudentHoldRequested):
                var hold = JsonSerializer.Deserialize<StudentHoldRequested>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");
                // The message id is the idempotency key — placing the same hold twice is a no-op.
                return _holds.PlaceHoldAsync(messageId, hold.StudentId, hold.Reason, ct);

            case nameof(LibraryFineAssessed):
                var fine = JsonSerializer.Deserialize<LibraryFineAssessed>(content)
                    ?? throw new InvalidOperationException($"Outbox message {messageId} had empty content.");
                return _billing.ChargeLibraryFineAsync(messageId, fine.StudentId, fine.Amount, ct);

            default:
                throw new InvalidOperationException($"Unknown outbox message type '{type}'.");
        }
    }
}
```

The `default` case throwing is deliberate. An unrecognised type is a bug — usually a renamed
event or a missing case — and throwing sends the message down the retry-then-dead-letter
path where a human will find it. Silently ignoring it would lose the message with no trace.

---

## 11. Step 6 — Register the pieces

Three extension methods, in the module's `Add<Module>Module`:

```csharp
services.AddScoped<ILibraryOutbox, LibraryOutbox>();                        // the writer
services.AddOutboxProcessing<LibraryDbContext, LibraryOutboxDispatcher>();  // background delivery
services.AddOutboxAdmin<LibraryDbContext>();                                // dead-letter + replay
```

And the table itself, in the module's `DbContext`:

```csharp
public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(LibraryDbContext).Assembly);
    modelBuilder.ApplyOutboxConfiguration();
}
```

Then a migration for the new table. The receiving module registers its consumer as an
ordinary service:

```csharp
services.AddScoped<IStudentHoldService, StudentHoldService>();
```

---

## 12. Idempotency — three strategies

At-least-once delivery means **your consumer will run twice**. Not might — will, eventually,
when a process dies after doing the work but before marking the message processed. If it
double-charges when that happens, you have a financial bug with no stack trace.

This repository uses three strategies. Pick deliberately.

### 1. By existing state — the cleanest, when it fits

Look at current state and do nothing if the work is already done. `FineWaiver` (above) and
`LibraryWithdrawalService` both work this way: no outstanding fines, no active loans,
nothing to do.

**Use it when** the operation is naturally "make it so" rather than "add one more".
**Don't** when the operation is additive — "charge £5" is not idempotent by state, because
the state after two charges looks like a legitimate £10.

### 2. By a marker row keyed on the message id

Record something whose primary key **is** the message id, then check for it first:

```csharp
var alreadyAccepted = await _db.Holds.AnyAsync(hold => hold.Id == messageId, ct);
var alreadyRejected = await _db.Outbox.AnyAsync(message => message.Id == messageId, ct);
if (alreadyAccepted || alreadyRejected) return;
```

That is `StudentHoldService`, and it checks **two** places because the message has two
possible outcomes: accepted (a `StudentHold` row with `Id = messageId`) or rejected (an
outbox row with `Id = messageId`). Either one means "this message has been dealt with".

**Why this matters:** a consumer that can reject must make the *rejection* idempotent too.
Guard only the success path and a redelivery of a rejected message publishes the
compensation event a second time — and now the compensating leg runs twice.

### 3. By a marker field on the thing you create

The record you write carries the message id, so it is its own dedupe marker:

```csharp
if (account is not null && account.HasEntryFrom(messageId)) return;

account.Charge(amount, ChargeCategory.LibraryFine, "Library fine", today, messageId);
                                                                 // ^^^^^^^^^ SourceReference
```

`StudentBilling` does this. No extra table, and the ledger gains a useful provenance field
for free — you can trace any charge back to the event that caused it.

---

## 13. Compensation — the two-leg saga

Everything so far delivers one event one way. A saga adds the interesting part: what happens
when the receiver says **no**.

The worked example spans four files. Follow it in order.

```
  1. Library: AssessFine
     fines cross the limit → enqueue StudentHoldRequested        [library.db, one transaction]
                                        │
  2. LibraryOutboxDispatcher            ▼
     StudentHoldRequested → IStudentHoldService.PlaceHoldAsync(messageId, …)
                                        │
  3. Students: StudentHoldService       ▼                        [students.db, one transaction]
     student active?  ──yes──► record the StudentHold. Done.
              │
              └──no (withdrawn / graduated / gone)
                   └─► enqueue StudentHoldRejected               [REVERSE LEG STARTS]
                                        │
  4. StudentsOutboxDispatcher           ▼
     StudentHoldRejected → IFineWaiver.WaiveStudentFinesAsync(…)
                                        │
  5. Library: FineWaiver                ▼                        [library.db, one transaction]
     waives the fines that triggered the request. Compensated.
```

The decision point, from `StudentHoldService`:

```csharp
if (status is null or StudentStatus.Graduated or StudentStatus.Withdrawn)
{
    var rejectionReason = status is null
        ? "Student no longer exists."
        : $"Student is {status} and cannot be placed on hold.";

    _db.Outbox.Add(new OutboxMessage
    {
        Id = messageId,                                  // ← ties the legs together AND dedupes
        Type = nameof(StudentHoldRejected),
        Content = JsonSerializer.Serialize(new StudentHoldRejected(studentId, rejectionReason)),
        OccurredOnUtc = DateTime.UtcNow,
    });
}
else
{
    _db.Holds.Add(StudentHold.Place(messageId, studentId, reason, DateTime.UtcNow));
}

// One transaction: either the hold is recorded, or the rejection event is enqueued.
await _db.SaveChangesAsync(ct);
```

Three things this does at once, and it is worth naming them separately:

1. **The rejection is published through the outbox too** — in the same transaction as the
   decision not to place the hold. The reverse leg gets the same delivery guarantee as the
   forward one.
2. **The outbox row's id is the incoming `messageId`**, which both dedupes the rejection and
   makes the two legs traceable to each other.
3. **Either/or, one transaction.** There is no state where both a hold and a rejection exist.

### Designing your own compensation

The compensating action is a **business decision, not a technical one**. "The hold failed,
so waive the fines" is a policy someone chose — the alternative, "keep the fines and alert
an operator", is equally valid code. Ask the business what should happen; do not default to
the reversal that is easiest to write.

And compensation must itself be idempotent, because the reverse leg is delivered
at-least-once exactly like the forward one.

---

## 14. When delivery keeps failing

The processor's behaviour, with the real numbers from
`src/BuildingBlocks.Outbox/OutboxProcessor.cs`:

| | Value | Meaning |
|---|---|---|
| Poll interval | **2 seconds** | How often each module's outbox is checked |
| Batch size | **20** | Messages pulled per tick, oldest first |
| Max attempts | **3** | Then the message is dead-lettered |

On each attempt it increments `Attempts` and records the `Error`. On success it stamps
`ProcessedOnUtc`. On the third failure it stamps `DeadLetteredOnUtc` — the message is
**parked, not deleted**, and no longer retried.

**Why park rather than retry forever:** one poison message retried forever burns CPU,
fills logs, and — with a batch that fetches oldest-first — can starve every message behind
it. Dead-lettering keeps the queue moving and puts the problem in front of a human.

Operators inspect and replay:

```bash
GET  /library/outbox/dead-letter               # what is parked, and why
POST /library/outbox/dead-letter/{id}/replay   # clear the flag, try again
```

Replay is the right move once you have fixed the cause — a missing dispatcher case, a
consumer bug, the other module being down. Replaying without fixing the cause just parks it
again three attempts later.

There is also a development-only endpoint that injects a deliberately unroutable message, so
you can watch the retry → dead-letter → replay path once without breaking something real.
Do that; it is much better than meeting the mechanism for the first time during an incident.

### Metrics

The processor emits three counters, tagged by database, and they are on the dashboard:

| Counter | Watch for |
|---|---|
| `outbox_delivered_total` | Should track your write volume |
| `outbox_failed_total` | Occasional blips are normal; a sustained rate is not |
| `outbox_dead_lettered_total` | **Should be zero.** Anything else is an unhandled bug |

> A known gap, stated honestly: retries are on the fixed 2-second poll with no exponential
> backoff. If the other side is down, you will burn all three attempts in about six seconds
> and dead-letter a message that would have succeeded a minute later. Backoff is the obvious
> improvement; until then, be ready to replay after an outage.

---

## 15. Correlation across the hop

A request's correlation id is stamped on the outbox row when you enqueue, and **restored**
by the processor before dispatch:

```csharp
if (message.CorrelationId is not null)
{
    correlation.Set(message.CorrelationId);
}
```

So the consumer's logs, its audit records, and the original HTTP request all carry the same
id — even though the consumer runs on a background thread, seconds later, in a different
module, writing to a different database.

**Why this matters:** without it, an async hop is where a trail goes cold. Someone
investigating "what happened to this request?" gets to the enqueue and stops. With it, one
search returns the whole flow, both legs of the saga included.

---

## 16. The traps

### The open-generic `IOutbox` collision

`AddOutboxWriter<TContext>()` registers `IOutbox` — an **open generic with one slot**. If
two modules both call it, DI resolution is last-registration-wins, and the first module's
`Enqueue` silently starts writing rows into the *other* module's outbox table. No error. The
messages are then delivered by the wrong processor, or never.

The convention here: the first module uses the shared `IOutbox`; every module after it
defines **its own** writer interface — `IStudentOutbox`, `ITesterGuideOutbox` — pointing at
its own table.

```csharp
services.AddScoped<ITesterGuideOutbox, TesterGuideOutbox>();   // not IOutbox
```

### Publishing on state instead of on transition

Covered in [chapter 7](#7-step-2--enqueue-it-atomically). Publish on the *change*, or you
will flood the queue and lean on idempotency to hide it.

### Renaming an event type

The type's simple name is the routing key, stored as a string in already-written rows.
Rename the record and undelivered messages of the old name hit the dispatcher's `default`
case and dead-letter. If you must rename, drain the outbox first, or keep a `case` for the
old name.

### Assuming the consumer runs in your transaction

It does not. It runs later, on a background thread, in its own scope, against its own
database — and it calls `SaveChanges` itself. Anything you wanted atomic with your change
had to be in *your* transaction.

---

## 17. The checklist

For a cross-module **read**:

- [ ] The owning module publishes an interface in its `*.Contracts` project
- [ ] The contract returns a purpose-built summary record, never a domain object
- [ ] Your `Application.csproj` references only that `Contracts` project
- [ ] Composition happens in your application layer, not in SQL

For a cross-module **write**:

- [ ] Event record defined in `Application/Outbox/`, ids and primitives only, past tense
- [ ] `_outbox.Enqueue(...)` in the handler, on the **transition**
- [ ] `DbSet<OutboxMessage>` + `ApplyOutboxConfiguration()` + a migration
- [ ] Your own writer interface if another module already owns `IOutbox`
- [ ] The receiving contract takes `messageId` as its first parameter
- [ ] The consumer is idempotent — and you can say which of the three strategies it uses
- [ ] The consumer calls `SaveChanges` itself
- [ ] A `case` in the dispatcher; `default` still throws
- [ ] `AddOutboxProcessing<,>` and `AddOutboxAdmin<>` registered
- [ ] Both sides tested, including a **duplicate delivery** test

If it can be rejected:

- [ ] Rejection publishes a compensation event, in the same transaction as the decision
- [ ] The rejection path is idempotent too
- [ ] The compensating action is one the business actually asked for
- [ ] The reverse leg has a `case` in the *other* module's dispatcher

---

## 18. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Message enqueued but never delivered | No `AddOutboxProcessing` for that context | [Chapter 11](#11-step-6--register-the-pieces) |
| Messages land in another module's outbox | Two modules registered `IOutbox` | Give yours its own writer interface |
| `Unknown outbox message type` in dead-letter | Missing `case`, or the event was renamed | Add the case; drain before renaming |
| Consumer ran twice, data is doubled | Consumer is not idempotent | [Chapter 12](#12-idempotency--three-strategies) |
| Rejection compensates twice | Only the success path is deduped | Guard the rejection path too |
| Everything dead-letters after ~6 seconds | Other side down; 3 attempts on a 2s poll | Fix the cause, then replay |
| Nothing in the outbox table at all | `Enqueue` ran but the transaction rolled back — or the handler isn't a command | Check the command carries the module's marker |
| Correlation id missing on the consumer side | Message enqueued outside a request scope | Expected for background-produced events |
| Consumer changes never persist | It didn't call `SaveChanges` | It runs outside `TransactionBehavior` |
| `outbox_dead_lettered_total` climbing | A real bug, by definition | Inspect `/outbox/dead-letter` — the `Error` is recorded |

---

## 19. Cheat sheet

### Registration

```csharp
// Publishing module
services.AddScoped<IMyModuleOutbox, MyModuleOutbox>();                 // your own writer
services.AddOutboxProcessing<MyDbContext, MyOutboxDispatcher>();       // background delivery
services.AddOutboxAdmin<MyDbContext>();                                // dead-letter + replay

// DbContext
public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
modelBuilder.ApplyOutboxConfiguration();

// Receiving module
services.AddScoped<IMyWriteContract, MyWriteContractImplementation>();
```

### The message row

| Column | Meaning |
|---|---|
| `Id` | The idempotency key. Passed to every consumer |
| `Type` | `typeof(TEvent).Name` — the routing key |
| `Content` | The event, as JSON |
| `OccurredOnUtc` | When enqueued. Delivery is oldest-first |
| `ProcessedOnUtc` | Null until delivered successfully |
| `CorrelationId` | Restored before dispatch, so the flow stays traceable |
| `Attempts` | Incremented per try; 3 is the cap |
| `DeadLetteredOnUtc` | Set at the cap. Parked, not deleted |
| `Error` | The most recent failure message |

### Operations

```bash
GET  /library/outbox/dead-letter                 # inspect parked messages
POST /library/outbox/dead-letter/{id}/replay     # requeue one, after fixing the cause
POST /library/outbox/_dev/poison                 # Development only: exercise the path
```

### Processor constants

`OutboxProcessor<TContext>` — poll **2s**, batch **20**, max attempts **3**, oldest first.

---

## 20. Glossary

| Term | Meaning |
|---|---|
| **At-least-once** | Delivery may repeat. The reason idempotency is mandatory |
| **Choreography** | Modules react to events with no central coordinator. What this repo does |
| **Compensating action** | A new operation that semantically reverses a committed one |
| **Contract** | An interface a module publishes for others to call. Lives in `*.Contracts` |
| **Correlation id** | The per-request id carried across the async hop so one flow stays traceable |
| **Dead-letter** | A message parked after the retry cap, for a human to inspect or replay |
| **Dispatcher** | Per-module code mapping an event type name onto a contract call |
| **Dual-write problem** | Two independent writes with no safe ordering — what the outbox fixes |
| **Eventual consistency** | The databases agree eventually, not instantly |
| **Forward leg** | The happy path of a saga |
| **Idempotency key** | The stable value used to recognise a repeat. Here, the outbox message id |
| **Integration event** | A record published by one module for another. Serialised to JSON |
| **Local transaction** | A transaction inside one database. Each saga step is one |
| **Orchestration** | A central coordinator drives each step. The alternative to choreography |
| **Outbox** | A table of pending events, written in the same transaction as the change |
| **Poison message** | One that can never succeed. Dead-lettering exists to contain it |
| **Replay** | Clearing the dead-letter flag so a message is retried |
| **Reverse leg** | The compensating half of a saga |
| **Saga** | A multi-step process across databases, glued by events |
| **System of record** | The authoritative owner of a piece of data |
| **Transactional outbox** | The full pattern: atomic enqueue, background delivery, retries |

---

## Appendix — The files involved

```
src/BuildingBlocks/Outbox/
└── IOutbox.cs                            Enqueue<TEvent> — stages, never saves

src/BuildingBlocks.Outbox/
├── OutboxMessage.cs                      the row
├── OutboxMessageConfiguration.cs         its EF mapping
├── OutboxModelBuilderExtensions.cs       ApplyOutboxConfiguration()
├── OutboxWriter.cs                       IOutbox implementation, stamps the correlation id
├── OutboxProcessor.cs                    background delivery: 2s / 20 / 3 attempts
├── IOutboxDispatcher.cs                  per-module routing, keyed by DbContext
├── OutboxDeadLetterReader.cs             what is parked
├── OutboxReplayer.cs                     requeue one
├── OutboxDiagnostics.cs                  delivered / failed / dead-lettered counters
└── OutboxServiceCollectionExtensions.cs  AddOutboxProcessing / Writer / Admin

Worked example — the fine → hold → rejection → waiver saga:
├── Library.Application/Loans/AssessFine.cs               enqueue, on the transition
├── Library.Infrastructure/Outbox/LibraryOutboxDispatcher.cs   route forward leg
├── Students.Infrastructure/Contracts/StudentHoldService.cs    accept or reject
├── Students.Infrastructure/Outbox/StudentsOutboxDispatcher.cs route reverse leg
└── Library.Infrastructure/Contracts/FineWaiver.cs             compensate

Synchronous reads:
├── Students.Contracts/IStudentDirectory.cs               the published interface
└── Library.Application/Loans/BorrowBook.cs               a consumer of it
```

---

## Where to go next

- **[Adding a new module](30-add-a-module.md)** — the module that will publish or consume
  these events.
- **[Auditing](40-auditing.md)** — commands that enqueue events are audited like any other;
  the outbox table itself is deliberately excluded from change capture.
