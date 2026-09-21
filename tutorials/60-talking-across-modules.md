# Talking Across Modules

**Who this is for:** someone whose module needs something from another module — a piece of
data, an action performed over there, or a multi-step process that must survive a restart —
and who has just discovered that the obvious way to do it isn't available.

**What you'll be able to do by the end:** read another module's data (or trigger a simple
action in it) through a published contract, build a multi-step process that survives a
crash between steps, make each step safe against being run twice, and undo whichever earlier
steps already succeeded when a later one says no.

**What you need first:** a module of your own ([guide 30](30-add-a-module.md)), and one
feature in it that works.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [The problem](#1-the-problem) | Understand why you can't just do it |
| 2 | [Two sanctioned routes](#2-two-sanctioned-routes) | Pick the right one for your case |
| 3 | [Reads and simple actions — published contracts](#3-reads-and-simple-actions--published-contracts) | The easy half |
| 4 | [Why a multi-step process needs the outbox](#4-why-a-multi-step-process-needs-the-outbox) | Durable state instead of memory |
| 5 | [What a saga is](#5-what-a-saga-is) | The vocabulary, before the code |
| 6 | [Step 1 — Define the step messages](#6-step-1--define-the-step-messages) | A record per step, ids only |
| 7 | [Step 2 — Enqueue the first step atomically](#7-step-2--enqueue-the-first-step-atomically) | One line, in the right place |
| 8 | [Step 3 — The published contract the saga calls](#8-step-3--the-published-contract-the-saga-calls) | The other module's entry point |
| 9 | [Step 4 — Route each step in the dispatcher](#9-step-4--route-each-step-in-the-dispatcher) | Business failure vs. genuine failure |
| 10 | [Step 5 — Register the pieces](#10-step-5--register-the-pieces) | Three extension methods |
| 11 | [Idempotency — two strategies, and a third you might need](#11-idempotency--two-strategies-and-a-third-you-might-need) | Pick one, deliberately |
| 12 | [Compensation — the saga unwinding itself](#12-compensation--the-saga-unwinding-itself) | When a later step says no |
| 13 | [Orchestration vs. choreography](#13-orchestration-vs-choreography) | Two shapes for the same problem |
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

Now you need this: *"approving an onboarding request should reserve a laptop, allocate a
licence, and provision system access — and if any of those fails, undo whichever of the
earlier ones already succeeded."* The equipment lives in `equipment.db`. The onboarding
request, and the record of how far the process got, lives in `onboarding.db`.

The instinct is to inject the other module's `DbContext` and do it all in one method. Two
reasons not to:

1. **It deletes the boundary.** The moment `Onboarding` writes to `equipment.db` directly,
   the Equipment module can no longer change its own schema, and nobody will find out until
   it breaks.
2. **It doesn't actually work, past the first step.** A database transaction lives inside
   **one** database. There is no `BEGIN TRAN` spanning both files. Reserving the equipment
   and recording the saga's progress cannot commit as a single atomic unit — so if the
   process dies right after the equipment is reserved but before that fact is recorded,
   nothing knows the reservation exists.

That second point is the hard constraint. Everything in this guide follows from it.

> **What about distributed transactions?** Two-phase commit exists, and this is not the
> place for it: it needs a coordinator, it holds locks across the whole operation, it
> doesn't work across most modern data stores, and its failure mode is worse than the
> problem it solves. The industry moved to eventual consistency for good reasons.

---

## 2. Two sanctioned routes

| You need | Route | Consistency | Chapter |
|---|---|---|---|
| To **read** something the other module owns, or trigger a **simple action** that either fully succeeds or fully fails in one call | A published contract, called synchronously | Immediate | [3](#3-reads-and-simple-actions--published-contracts) |
| A **multi-step process** that must survive the caller (or the whole process) dying partway through | The outbox, driving a durable saga | Eventual, but crash-safe | [4](#4-why-a-multi-step-process-needs-the-outbox) onward |

And one route that is never sanctioned: referencing another module's `Infrastructure` or
`Domain` project, injecting its `DbContext`, or querying its tables.

> **Neither of these is the right fit for data that isn't owned by any module at all.**
> If two (or more) modules both need the same small, read-only, rarely-changing lookup
> data — office locations, currency codes — a published contract would force one module
> to pretend it "owns" data it has no actual business logic around, just to hand it to a
> sibling. This repo's answer is `SharedKernel/`: a plain project (`Site` reference data),
> a peer of `BuildingBlocks`, that any module takes a direct project reference to — no
> contract, no owning module, just a shared dependency both sides agree to, behind a
> long-lived cache (`IReferenceDataService`). See [`Equipment.Infrastructure/Caching/EquipmentDirectory.cs`](../src/Modules/Equipment/Equipment.Infrastructure/Caching/EquipmentDirectory.cs)
> for the consuming side. The discipline that keeps this from turning into a backdoor:
> `SharedKernel` never carries one module's data to another — only data that belongs to
> neither.

**How to tell which you need.** Ask whether the *whole operation* can safely live inside one
method call, in memory, for its entire duration. "Is this equipment reserved?" — yes, one
call, one answer, done. "Reserve equipment, then allocate a licence, then provision access,
undoing whatever succeeded if a later step fails" — that's three separate outcomes over
time, and a crash between any two of them must not lose track of where it got to. That's a
saga.

This repo actually demonstrates **both engines** for that second case side by side —
`ApproveOnboardingInstant` runs the same three steps synchronously in one method, accepting
no crash recovery, purely to show the contrast; `ApproveOnboardingStandard` is the durable
version this guide is about. If you haven't seen the instant version, skim it first — it's
the same steps with none of the machinery, which makes the machinery easier to justify.

---

## 3. Reads and simple actions — published contracts

The module that **owns** the data or the action publishes an interface in its
`*.Contracts` project. That project has zero dependencies, so anyone can reference it
without dragging along a domain model or an ORM.

```csharp
// src/Modules/Equipment/Equipment.Contracts/IEquipmentReservationService.cs
public interface IEquipmentReservationService
{
    Task<EquipmentReservationResult> ReserveAsync(
        Guid onboardingRequestId, string category, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}

public sealed record EquipmentReservationResult(bool Reserved, Guid? EquipmentId, string? Reason);
```

Three things to notice, because all three are deliberate:

**It speaks in primitives, not domain types.** `category` is a `string`, not
`Equipment.Domain.EquipmentCategory` — the consumer (`Onboarding`) never references
`Equipment.Domain` at all, only `Equipment.Contracts`. Crossing the boundary in domain types
would force every consumer to reference the owner's internals just to call one method.

**`Reserved: false` is a normal return value, not an exception.** "No laptops in stock" is
an expected business outcome, and the caller is meant to branch on it — see
[chapter 9](#9-step-4--route-each-step-in-the-dispatcher) for why that distinction matters a
lot more once this call is made from a background dispatcher instead of a request.

**The implementation lives in `Equipment.Infrastructure`** and owns the database access. The
consumer sees an interface and never learns there is a second database involved.

Consuming it is ordinary dependency injection. `Onboarding.Application.csproj` references
`Equipment.Contracts.csproj` and the handler takes the interface:

```csharp
public sealed class Handler : IRequestHandler<Command, Result>
{
    private readonly IOnboardingRequestRepository _requests;
    private readonly IEquipmentReservationService _equipment;

    public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
    {
        var request = await _requests.GetAsync(command.OnboardingRequestId, cancellationToken)
            ?? throw new DomainException($"No onboarding request exists with id '{command.OnboardingRequestId}'.");

        var equipmentResult = await _equipment.ReserveAsync(
            request.Id, request.RequiredEquipmentCategory, cancellationToken);
        // ... branch on equipmentResult.Reserved
    }
}
```

That's the actual shape of `ApproveOnboardingInstant.Handler` — a synchronous contract call,
awaited like any other `Task`, with the result checked like any other value.

### Composing, not joining

When an endpoint needs data from both sides, you fetch from each and combine **in the
application layer**, never with a cross-database join. Neither module in this codebase
currently has an endpoint that reads from both — `GetOnboardingSummary` derives its fields
from Onboarding's own data alone — but the shape is the same one already familiar from
[chapter 3's](#3-reads-and-simple-actions--published-contracts) reservation call: fetch
through each side's own contract, then combine the results as plain values in your handler,
the same way you'd combine any two variables.

> **Design the contract for the consumer, not the owner.** A contract that returns
> everything "in case someone needs it" recreates the coupling you were avoiding — now
> every field is a promise. Publish the narrowest thing that answers the question.

---

## 4. Why a multi-step process needs the outbox

A single synchronous call, like the reservation above, is safe on its own: it either
completes and its own database commits, or it throws and nothing does. The trouble starts
the moment you chain **several** of these across a process that must outlive any one of
them.

Picture doing the whole saga inline, in one request:

```csharp
await _equipment.ReserveAsync(request.Id, category, ct);      // commits to equipment.db
await _licences.AllocateAsync(request.Id, licenceType, ct);    // commits to... itself
// process crashes here
await _access.ProvisionAsync(request.Id, accessLevel, ct);     // never runs
```

If the process dies after the second line, the equipment is reserved and a licence is
allocated — real, committed facts in two different places — and **nothing on disk says the
onboarding request is even mid-flight.** Restart the process and there is no record to
resume, and no record to compensate either. That's exactly what `ApproveOnboardingInstant`
accepts as its tradeoff, deliberately, for the simplicity of having no extra machinery.

### The fix: make "what happens next" part of your own transaction

Instead of holding the sequence in memory, write down **which step comes next** as a row in
your own database, in the same transaction as the step that just completed:

```
   Onboarding approves the request
   ┌──────────────────────────────────────────┐
   │  UPDATE OnboardingRequests SET ...        │
   │  INSERT INTO OnboardingSagaStates (...)   │
   │  INSERT INTO Outbox (ReserveEquipment...) │   ← "do step 1 next" is just a row
   └──────────────────────────────────────────┘
              │ commits together, or not at all
              ▼
   OutboxProcessor (background, every 2s)
              │ reads the undelivered row
              ▼
   OnboardingOutboxDispatcher.DispatchAsync(...)
              │ calls IEquipmentReservationService.ReserveAsync(...) — synchronously, same as chapter 3
              ▼
   records the outcome, enqueues the NEXT step's row, in one transaction
```

All three — the business change, the saga's own progress, and "what to do next" — commit
together or not at all. A crash at any point leaves the last committed row as the honest
truth of where the saga is, and the next poll picks it up from exactly there. This is what
makes `ApproveOnboardingStandard` resumable where the instant version isn't: nothing about
the *steps themselves* changed, only where "what happens next" lives.

| You get | You accept |
|---|---|
| The saga's progress can never be lost once a step commits | Each step happens *slightly later* than the one before, not synchronously in one request |
| A crash between steps is a delay, not data loss | Delivery is **at-least-once** — a step can run twice |
| No coordinator, no distributed locks | Every step must be **idempotent** |

That last row is not optional, and [chapter 11](#11-idempotency--two-strategies-and-a-third-you-might-need)
is entirely about it.

---

## 5. What a saga is

> A **saga** is a business process spanning several steps — often several local
> transactions in different modules — coordinated by durable state rather than one shared
> transaction. Each step commits locally and records what happens next; if a later step
> fails, earlier steps are undone by **compensating actions**, not by rollback, because you
> cannot roll back a transaction that has already committed.

The vocabulary, once, so the rest of the guide reads cleanly:

| Term | Meaning here |
|---|---|
| **Local transaction** | An ordinary transaction inside one module's database. Each saga step commits one |
| **Step message** | The outbox row that says "do this step next". A plain record |
| **Forward leg** | The happy path — reserve, allocate, provision |
| **Reverse / compensating leg** | The undo, when a later step fails |
| **Compensating action** | A *new* operation that semantically reverses a committed one — `Release`, not "un-reserve" |
| **Orchestration** | One component drives every step, forward and reverse, and knows the whole sequence. What `Onboarding`'s Standard saga does — see [chapter 13](#13-orchestration-vs-choreography) |
| **Choreography** | Each module reacts to the other's events independently; no single place knows the whole saga. The heavier alternative — also chapter 13 |
| **At-least-once** | The processor may deliver the same step message more than once |
| **Idempotency** | A repeated delivery has no additional effect |
| **Eventual consistency** | The databases agree *eventually*; briefly, they don't |

**Compensation is not rollback.** If you release equipment to compensate for a licence pool
being empty, the equipment *was* reserved and is now *released* — two real, recorded facts,
not an erased one. That is usually what actually happened, and it's the only thing
available once the reservation has committed.

---

## 6. Step 1 — Define the step messages

A plain record per step, carrying only the id needed to look everything else up:

```csharp
// src/Modules/Onboarding/Onboarding.Application/Outbox/OnboardingSagaMessages.cs
public sealed record ReserveEquipmentForOnboarding(Guid OnboardingRequestId);
public sealed record AllocateLicenceForOnboarding(Guid OnboardingRequestId);
public sealed record ProvisionAccessForOnboarding(Guid OnboardingRequestId);

// Compensation:
public sealed record ReleaseLicenceForOnboarding(Guid OnboardingRequestId);
public sealed record ReleaseEquipmentForOnboarding(Guid OnboardingRequestId);
```

Three rules:

- **Ids and primitives only — here, just the one id.** The message is serialised to JSON and
  read back by a background process, possibly seconds or minutes later. Everything else the
  step needs (which category, which licence type) is re-loaded fresh from the request row —
  not carried in the message — so a redelivery always acts on current data, never on a
  stale copy of it.
- **Name it for the action to take**, in the imperative: `ReserveEquipmentForOnboarding`, not
  `EquipmentReserved`. Unlike a plain integration event announcing something that already
  happened, this *is* the instruction — the saga is telling its future self what to do next.
- **Treat the shape as a published API**, even though only this module ever reads it. Once
  messages of that type are on disk, changing the record's fields breaks the deserialisation
  of anything not yet delivered. Add nullable fields; don't rename or remove.

> The type's **name** is the routing key. `OutboxWriter` stores `typeof(TEvent).Name`, and
> the dispatcher switches on that string. Renaming the record renames the routing key, and
> any undelivered messages of the old name will fail to route.

---

## 7. Step 2 — Enqueue the first step atomically

One call, inside the command that starts the saga:

```csharp
// src/Modules/Onboarding/Onboarding.Application/Requests/ApproveOnboardingStandard.cs
public async Task<Result> Handle(Command command, CancellationToken cancellationToken)
{
    var request = await _requests.GetAsync(command.OnboardingRequestId, cancellationToken)
        ?? throw new DomainException($"No onboarding request exists with id '{command.OnboardingRequestId}'.");

    request.Approve();

    var saga = OnboardingSagaState.Start(request.Id);
    await _sagaStates.AddAsync(saga, cancellationToken);

    // Atomic with the approval and the saga row above (same transaction): the first step is
    // reliably queued the moment this commits, even if the process dies immediately after.
    _outbox.Enqueue(new ReserveEquipmentForOnboarding(request.Id));

    return new Result(saga.Status.ToString());
}
```

**`Enqueue` does not save.** It adds a row to the change tracker and returns. The module's
`TransactionBehavior` commits the approval, the saga state row, *and* the outbox row
together at the end of the request. That shared transaction is the entire mechanism — it is
why the first step can never be silently lost, and why the handler stays this simple.

The same discipline continues inside the dispatcher, one level down: every step handler
enqueues **exactly one** follow-up message — the next forward step on success, or the first
compensating step on failure — from the same branch that decided the outcome, in the same
transaction as recording that outcome. Never both, never neither. [Chapter 12](#12-compensation--the-saga-unwinding-itself)
is the worked example.

---

## 8. Step 3 — The published contract the saga calls

Nothing new here beyond [chapter 3](#3-reads-and-simple-actions--published-contracts) — the
saga's steps call published contracts exactly the way any other consumer would.
`IEquipmentReservationService` is both this saga's step 1/reverse-leg dependency *and* an
ordinary contract anyone else could call. The saga doesn't get a special calling convention;
it's just code that happens to run from a background dispatcher instead of an HTTP request.

**Note the parameter that plays the idempotency-key role.** `onboardingRequestId` is stable
across redeliveries — the same saga step retried is the same id — so it doubles as the
dedupe key ([chapter 11](#11-idempotency--two-strategies-and-a-third-you-might-need)). It
doesn't need to be an abstract generated message id the way a pure integration-event
contract's would: at most one reservation will ever exist per onboarding request, which
makes the request's own id a more meaningful key than a generated one would be.

---

## 9. Step 4 — Route each step in the dispatcher

One dispatcher per module: a switch from the message's type name to the step logic. This is
the real `OnboardingOutboxDispatcher`, one case shown in full:

```csharp
internal sealed class OnboardingOutboxDispatcher : IOutboxDispatcher<OnboardingDbContext>
{
    private readonly OnboardingDbContext _db;
    private readonly IEquipmentReservationService _equipment;
    private readonly ILicenceAllocationService _licences;
    private readonly IAccessProvisioningService _access;
    private readonly IOutbox _outbox;

    private async Task HandleReserveEquipmentAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

        var result = await _equipment.ReserveAsync(onboardingRequestId, request.RequiredEquipmentCategory, cancellationToken);
        if (!result.Reserved)
        {
            request.RecordEquipmentFailed();
            request.MarkFailed(result.Reason!);
            saga.MarkFailed(); // first step — nothing to compensate
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        request.RecordEquipmentReserved(result.EquipmentId!.Value);
        saga.AdvanceTo(SagaStep.AllocateLicence);
        Enqueue(new AllocateLicenceForOnboarding(onboardingRequestId));
        await _db.SaveChangesAsync(cancellationToken);
    }
}
```

(`Enqueue` here is a one-line private wrapper around the injected `IOutbox`, kept so every
step handler reads the same way. It's `_outbox.Enqueue` underneath.)

### Business failure vs. genuine failure — the distinction that matters most here

`result.Reserved == false` ("no laptops in stock") is handled **inline, without throwing** —
the method records the failure and returns normally. This is the single most important
design decision in the whole dispatcher, and it's easy to get backwards:

> If a business outcome like "nothing in stock" were allowed to throw, the generic
> `OutboxProcessor` (which knows nothing about onboarding, licences, or equipment) would
> treat it exactly like a genuine delivery failure — retry it twice more, two seconds apart,
> then dead-letter it. An entirely ordinary, expected business result would end up parked
> next to real bugs, indistinguishable from them, on the dashboard an operator watches for
> actual problems.

Only a **genuinely unexpected** condition — the request or saga row is simply missing, which
is a data-integrity bug, not a business outcome — is allowed to throw:

```csharp
private async Task<(OnboardingRequest Request, OnboardingSagaState Saga)> LoadAsync(
    Guid onboardingRequestId, CancellationToken cancellationToken)
{
    var request = await _db.Requests.FirstOrDefaultAsync(r => r.Id == onboardingRequestId, cancellationToken)
        ?? throw new InvalidOperationException($"Onboarding request '{onboardingRequestId}' does not exist.");
    // ...
}
```

*That* is correctly left to the outbox's normal retry-then-dead-letter path — there is no
sensible business handling for "the row I need doesn't exist," and a human should see it.

The `default` case in the outer `switch` throwing on an unrecognised type is the same
principle: an unrecognised type is a bug (usually a renamed message, or a missing `case`),
so it takes the retry-then-dead-letter path where a human will find it.

---

## 10. Step 5 — Register the pieces

Three extension methods, in the module's `AddOnboardingModule`:

```csharp
services.AddOutboxWriter<OnboardingDbContext>();                              // the writer (IOutbox)
services.AddOutboxProcessing<OnboardingDbContext, OnboardingOutboxDispatcher>(); // background delivery
services.AddOutboxAdmin<OnboardingDbContext>();                                // dead-letter + replay
```

And the table itself, in the module's `DbContext`:

```csharp
public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(OnboardingDbContext).Assembly);
    modelBuilder.ApplyOutboxConfiguration();
}
```

Then a migration for the new table. Because `Onboarding` is the only module in this
codebase that currently needs an outbox, it can inject the shared `IOutbox` directly rather
than defining its own writer interface — see the trap in [chapter 16](#16-the-traps) before
you copy that if you're adding a **second** outbox-using module.

---

## 11. Idempotency — two strategies, and a third you might need

At-least-once delivery means **a step will run twice**. Not might — will, eventually, when a
process dies after doing the work but before the outbox marks the message delivered. If a
step double-charges or double-reserves when that happens, you have a bug with no stack
trace.

This codebase uses two strategies for its steps; a third exists for cases neither fits.

### 1. By existing state, checked first

Look at current state and return the existing outcome if the work is already done. This is
`IEquipmentReservationService.ReserveAsync`'s **first** line:

```csharp
public async Task<EquipmentReservationResult> ReserveAsync(Guid onboardingRequestId, string category, CancellationToken cancellationToken)
{
    // Idempotent: a redelivery/retry for the same request finds its own reservation already made.
    var already = await _db.Equipment
        .FirstOrDefaultAsync(asset => asset.ReservedForOnboardingRequestId == onboardingRequestId, cancellationToken);
    if (already is not null)
    {
        return new EquipmentReservationResult(true, already.Id, null);
    }
    // ... otherwise reserve one
}
```

**Use it when** the operation is naturally "make it so" rather than "add one more".
`ReleaseAsync` is idempotent the same way, from the other direction: if nothing is reserved
for that request, releasing it is a no-op, not an error.

### 2. By a marker field on the thing you create

Notice `ReservedForOnboardingRequestId` in the query above is not a *separate* dedupe table
— it's the same field that already records "who holds this equipment", doing double duty as
the idempotency marker. No extra table, and the field is useful on its own merits (it's how
`GetOnboardingRequest` shows which equipment a request holds) as well as for dedup.

### 3. A marker row keyed on the message id — when neither fits

Sometimes the operation is genuinely additive ("charge £5" is not idempotent by state — two
charges of £5 look exactly like one legitimate £10 charge) and there's no natural field to
attach a marker to. The fix, not currently needed anywhere in this codebase, is a row whose
primary key **is** the message id, checked before acting:

```csharp
var alreadyProcessed = await _db.ProcessedMessages.AnyAsync(m => m.Id == messageId, ct);
if (alreadyProcessed) return;
```

Reach for this only when 1 and 2 genuinely don't fit — it's the one that costs an extra
table.

---

## 12. Compensation — the saga unwinding itself

Everything so far runs the forward leg. The interesting part is what happens when a step
says **no** — the worked example spans the same one dispatcher, calling into `Equipment`
twice: once forward, once to undo.

```
  1. ApproveOnboardingStandard: enqueue ReserveEquipmentForOnboarding    [onboarding.db, one transaction]
                                        │
  2. OnboardingOutboxDispatcher         ▼
     ReserveEquipmentForOnboarding → IEquipmentReservationService.ReserveAsync(...)  [SYNC call into Equipment]
       succeeds ──► record Completed, enqueue AllocateLicenceForOnboarding
              │
              └─ fails (no stock) ──► record Failed, saga.MarkFailed() — nothing to compensate yet
                                        │
  3. OnboardingOutboxDispatcher         ▼
     AllocateLicenceForOnboarding → ILicenceAllocationService.AllocateAsync(...)
       succeeds ──► record Completed, enqueue ProvisionAccessForOnboarding
              │
              └─ fails (pool empty) ──► record Failed, enqueue ReleaseEquipmentForOnboarding   [REVERSE LEG]
                                        │
  4. OnboardingOutboxDispatcher         ▼
     ReleaseEquipmentForOnboarding → IEquipmentReservationService.ReleaseAsync(...)
       record Compensated, saga.MarkCompensated()
```

If it's the **third** step (access) that fails instead, the reverse leg is one message
longer — release the licence, *then* enqueue releasing the equipment, exactly the reverse of
the order they were acquired in:

```csharp
private async Task HandleProvisionAccessAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
{
    var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

    var result = await _access.ProvisionAsync(onboardingRequestId, request.RequiredAccessLevel, cancellationToken);
    if (!result.Provisioned)
    {
        request.RecordAccessFailed();
        request.MarkFailed(result.Reason!);
        saga.BeginCompensating(SagaStep.AllocateLicence);
        Enqueue(new ReleaseLicenceForOnboarding(onboardingRequestId));   // one step at a time...
        await _db.SaveChangesAsync(cancellationToken);
        return;
    }
    // ...
}

private async Task HandleReleaseLicenceAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
{
    var (request, saga) = await LoadAsync(onboardingRequestId, cancellationToken);

    await _licences.ReleaseAsync(onboardingRequestId, cancellationToken);
    request.RecordLicenceCompensated();
    saga.BeginCompensating(SagaStep.ReserveEquipment);
    Enqueue(new ReleaseEquipmentForOnboarding(onboardingRequestId));     // ...then the next
    await _db.SaveChangesAsync(cancellationToken);
}
```

Three things worth naming separately:

1. **The compensating step is enqueued from the exact branch that decided to fail** — in the
   same transaction as recording the failure. The reverse leg gets the same durability
   guarantee as the forward one.
2. **Compensation unwinds one step at a time**, each its own message, in the reverse of the
   order the steps ran forward. There is no "undo everything" message — just the same
   mechanism, run backwards.
3. **`MarkFailed` and `RecordAccessFailed` happen immediately**, before compensation has
   necessarily finished. The *business outcome* (this request has failed) is known the
   moment the failing step reports it; the *cleanup* (releasing what already succeeded) is a
   separate fact that catches up a step at a time. Reading the request mid-compensation is
   completely valid — it shows `Status: Failed` with some steps still `Completed`, briefly,
   until the next poll advances them to `Compensated`.

### Designing your own compensation

The compensating action is a **business decision, not a technical one**. "Access failed, so
release the licence and the equipment" was a policy choice — the alternative, "keep them
reserved and alert an operator", is equally valid code. Ask what the business actually wants
instead of defaulting to whichever reversal is easiest to write.

And compensation must itself be idempotent, for the same reason the forward leg is: the
reverse leg is delivered at-least-once too. `EquipmentAsset.Release()` is a no-op if the
asset is already available — see the comment on it in the real file.

---

## 13. Orchestration vs. choreography

Everything in this guide so far is **orchestration**: one component —
`OnboardingOutboxDispatcher` — owns the entire sequence, forward and reverse. It calls
`Equipment`'s published contract synchronously, the same way any other consumer would, and
`Equipment` has no idea a saga is happening at all. From Equipment's side, its contract is
just being called, then possibly called again to release. There is no `Equipment` outbox,
no `Equipment` dispatcher, and no message ever flows *back into* Equipment.

The alternative is **choreography**: each side has its own dispatcher, reacting to the
other's events independently, with no single component that knows the whole saga. Sketched,
for the *same* scenario, choreographed instead:

```
  Onboarding's dispatcher enqueues "EquipmentReservationRequested"
                          │
  Equipment's OWN dispatcher reacts: reserves, then enqueues "EquipmentReserved" (or "...Rejected")
                          │
  Onboarding's dispatcher reacts to THAT event to decide what happens next
```

Both are legitimate; this codebase uses orchestration because one module (`Onboarding`)
already has to know the whole process to make sense of the domain — asking Equipment to
also carry saga-shaped knowledge it doesn't otherwise need would be coupling for its own
sake. Choreography earns its extra dispatcher and extra event types when **no single module
is a natural owner of the process** and each side genuinely needs to react to the other
independently — most commonly when the two sides are owned by different teams who each want
to evolve their own reaction to events without the other's release schedule. If you're
building a saga and one side is obviously "in charge" of it, default to orchestration; it's
less to build, and there's one place — not two — to read when you want to understand the
whole thing.

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
it. Dead-lettering keeps the queue moving and puts the problem in front of a human. (Which
is exactly why [chapter 9](#9-step-4--route-each-step-in-the-dispatcher)'s business-failure
distinction matters: only genuine bugs should ever reach this path.)

Operators inspect and replay:

```bash
POST /onboarding/outbox/dead-letter/search       # what is parked, and why (paging in the body)
POST /onboarding/outbox/dead-letter/{id}/replay  # clear the flag, try again
```

Replay is the right move once you have fixed the cause — a missing dispatcher case, a
consumer bug, the other module being down. Replaying without fixing the cause just parks it
again three attempts later.

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

So the dispatcher's logs, its audit records, and the original HTTP request that started the
saga all carry the same id — even though each step runs on a background thread, seconds
apart, possibly minutes after the request that kicked things off has long since returned.

**Why this matters:** without it, an async hop is where a trail goes cold. Someone
investigating "what happened to this onboarding request?" gets to the enqueue and stops.
With it, one search returns the whole saga, forward and reverse legs both.

---

## 16. The traps

### The shared `IOutbox` collision

`AddOutboxWriter<TContext>()` registers `IOutbox` as a plain, **non-keyed** interface. If two
modules both call it, DI resolution is last-registration-wins, and the first module's
`Enqueue` silently starts writing rows into the *other* module's outbox table. No error. The
messages are then delivered by the wrong processor, or never.

Right now only `Onboarding` calls `AddOutboxWriter<OnboardingDbContext>()`, so the trap is
dormant. The moment a **second** module needs one, it must **not** also inject bare
`IOutbox` — it needs its own writer interface pointing at its own table:

```csharp
services.AddScoped<IMySecondModuleOutbox, MySecondModuleOutbox>();   // not IOutbox
```

### Enqueuing more than one follow-up per outcome

Covered in [chapter 7](#7-step-2--enqueue-the-first-step-atomically). Each step handler
enqueues exactly one message — the next forward step, or the first compensating step — from
the branch that decided the outcome. Enqueuing from two branches, or forgetting the `return`
after the failure branch, means both the forward and the compensating chain can end up
running.

### Renaming a step message type

The type's simple name is the routing key, stored as a string in already-written rows.
Rename `ReserveEquipmentForOnboarding` and any undelivered messages of the old name hit the
dispatcher's `default` case and dead-letter. If you must rename, drain the outbox first, or
keep a `case` for the old name.

### Assuming a step runs in the original request's transaction

It does not. It runs later, on a background thread, in its own scope, against its own
`DbContext` instance — and it calls `SaveChangesAsync` itself, once per message, inside
`HandleReserveEquipmentAsync` and friends. Anything you wanted atomic with the *approval*
had to be in the approval's own transaction ([chapter 7](#7-step-2--enqueue-the-first-step-atomically));
anything atomic with *one step* has to be in that step's own `SaveChangesAsync` call.

---

## 17. The checklist

For a cross-module **read or simple action**:

- [ ] The owning module publishes an interface in its `*.Contracts` project
- [ ] The contract speaks in primitives, never a domain type from the owning module
- [ ] A business failure is a return value (`Reserved: false`), not an exception
- [ ] Your `Application.csproj` references only that `Contracts` project
- [ ] Composition happens in your application layer, not in SQL

For a **saga**:

- [ ] One message record per step, in `Application/Outbox/`, ids and primitives only
- [ ] `_outbox.Enqueue(...)` for the first step, in the command that starts the saga
- [ ] `DbSet<OutboxMessage>` + `ApplyOutboxConfiguration()` + a migration
- [ ] Your own writer interface if another module already owns the shared `IOutbox`
- [ ] Every step handler calls a published contract synchronously, exactly like chapter 3
- [ ] Every step is idempotent — and you can say which strategy it uses
- [ ] A business failure is handled inline (record it, enqueue compensation); it never throws
- [ ] Only a genuinely unexpected condition throws, into the retry-then-dead-letter path
- [ ] Compensation unwinds one step at a time, in reverse order, each its own message
- [ ] Each step handler calls `SaveChanges` itself
- [ ] A `case` per message type in the dispatcher; `default` still throws
- [ ] `AddOutboxProcessing<,>` and `AddOutboxAdmin<>` registered
- [ ] Tested: full success, failure at each step, and a duplicate delivery of one step

---

## 18. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Message enqueued but never delivered | No `AddOutboxProcessing` for that context | [Chapter 10](#10-step-5--register-the-pieces) |
| Messages land in another module's outbox | Two modules both injected the shared `IOutbox` | Give the second one its own writer interface |
| `Unknown outbox message type` in dead-letter | Missing `case`, or the message was renamed | Add the case; drain before renaming |
| A step ran twice, data is doubled | The step is not idempotent | [Chapter 11](#11-idempotency--two-strategies-and-a-third-you-might-need) |
| An ordinary "no stock" outcome got dead-lettered | The business-failure branch threw instead of returning | [Chapter 9](#9-step-4--route-each-step-in-the-dispatcher) |
| Compensation ran twice | The release step isn't idempotent | Make release a no-op when there's nothing to release |
| Everything dead-letters after ~6 seconds | Other side down; 3 attempts on a 2s poll | Fix the cause, then replay |
| Nothing in the outbox table at all | `Enqueue` ran but the transaction rolled back — or the handler isn't a command | Check the command carries the module's marker |
| Correlation id missing on a step | Message enqueued outside a request scope | Expected for a step enqueued by another step |
| Step's changes never persist | It didn't call `SaveChangesAsync` | Each step handler must call it itself — it runs outside `TransactionBehavior` |
| `outbox_dead_lettered_total` climbing | A real bug, by definition | Inspect `/onboarding/outbox/dead-letter/search` — the `Error` is recorded |

---

## 19. Cheat sheet

### Registration

```csharp
// Onboarding — the only module currently using the shared IOutbox
services.AddOutboxWriter<OnboardingDbContext>();
services.AddOutboxProcessing<OnboardingDbContext, OnboardingOutboxDispatcher>();
services.AddOutboxAdmin<OnboardingDbContext>();

// DbContext
public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
modelBuilder.ApplyOutboxConfiguration();

// A second outbox-using module would need its OWN writer interface instead of IOutbox:
services.AddScoped<IMySecondModuleOutbox, MySecondModuleOutbox>();
```

### The message row

| Column | Meaning |
|---|---|
| `Id` | Unique per row. Not the idempotency key here — the step message's own payload id is |
| `Type` | `typeof(TEvent).Name` — the routing key |
| `Content` | The message, as JSON |
| `OccurredOnUtc` | When enqueued. Delivery is oldest-first |
| `ProcessedOnUtc` | Null until delivered successfully |
| `CorrelationId` | Restored before dispatch, so the flow stays traceable |
| `Attempts` | Incremented per try; 3 is the cap |
| `DeadLetteredOnUtc` | Set at the cap. Parked, not deleted |
| `Error` | The most recent failure message |

### Operations

```bash
POST /onboarding/outbox/dead-letter/search        # inspect parked steps (paging in the body)
POST /onboarding/outbox/dead-letter/{id}/replay   # requeue one, after fixing the cause
```

### Processor constants

`OutboxProcessor<TContext>` — poll **2s**, batch **20**, max attempts **3**, oldest first.

---

## 20. Glossary

| Term | Meaning |
|---|---|
| **At-least-once** | Delivery may repeat. The reason idempotency is mandatory |
| **Choreography** | Each side has its own dispatcher, reacting to the other's events independently — no single owner of the saga. The heavier alternative; see [chapter 13](#13-orchestration-vs-choreography) |
| **Compensating action** | A new operation that semantically reverses a committed one |
| **Contract** | An interface a module publishes for others to call. Lives in `*.Contracts` |
| **Correlation id** | The per-request id carried across every async hop so one flow stays traceable |
| **Dead-letter** | A message parked after the retry cap, for a human to inspect or replay |
| **Dispatcher** | Per-module code mapping a message type name onto the step logic |
| **Eventual consistency** | The databases agree eventually, not instantly |
| **Forward leg** | The happy path of a saga |
| **Idempotency key** | The stable value used to recognise a repeat. Here, usually the saga's own id (e.g. the onboarding request id) |
| **Local transaction** | A transaction inside one database. Each saga step commits one |
| **Orchestration** | One component drives every step, forward and reverse, and knows the whole sequence. What this repo's Standard saga does |
| **Outbox** | A table of pending steps, written in the same transaction as the change that decided them |
| **Poison message** | One that can never succeed. Dead-lettering exists to contain it |
| **Replay** | Clearing the dead-letter flag so a message is retried |
| **Reverse leg** | The compensating half of a saga |
| **Saga** | A multi-step process, coordinated by durable state rather than a single transaction |
| **Shared kernel** | Small, read-only, rarely-changing data owned by *no* module, referenced directly by any that need it — `SharedKernel/` in this repo. Not a contract, not a module |
| **Step message** | An outbox row instructing the saga's own dispatcher to run one specific step next |
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

Worked example — the reserve → allocate → provision saga, both directions:
├── Onboarding.Application/Requests/ApproveOnboardingStandard.cs   starts the saga
├── Onboarding.Application/Outbox/OnboardingSagaMessages.cs        the five step messages
├── Onboarding.Infrastructure/Outbox/OnboardingOutboxDispatcher.cs forward AND reverse legs
├── Onboarding.Domain/OnboardingSagaState.cs                       durable progress + status
└── Onboarding.Domain/OnboardingRequest.cs                         Record*/*Compensated methods

Synchronous contract, called from both the instant and the durable engine:
├── Equipment.Contracts/IEquipmentReservationService.cs   the published interface
├── Equipment.Infrastructure/Contracts/EquipmentReservationService.cs   idempotent implementation
└── Onboarding.Application/Requests/ApproveOnboardingInstant.cs    a consumer that calls it synchronously, no outbox at all
```

---

## Where to go next

- **[Adding a new module](30-add-a-module.md)** — the module that will publish or consume
  these contracts and messages.
- **[Auditing](40-auditing.md)** — commands that enqueue steps are audited like any other;
  the outbox table itself is deliberately excluded from change capture.
- **[Testing](80-testing.md)** — how the same saga is tested at three levels: the domain
  rules, the instant engine with fakes, and the durable engine against a real database.
