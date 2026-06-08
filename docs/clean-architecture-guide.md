# Building an API with Clean Architecture — A Beginner's Guide

This is a hands-on, step-by-step guide to how this repository is structured and how to add a feature to it. Every concept points to a **real file in this project** so you can read working code alongside the explanation.

> **Never built or run a .NET project before?** Start with **[Getting Started — from zero](./getting-started.md)** first: it installs the tools, gets this project running, explains the everyday vocabulary, and shows how to scaffold *your own* application. Then come back here for how the code is organized.

If you're new to clean architecture, read it top to bottom once. If you just want to add a feature, jump to [Part 3: Add a feature, step by step](#part-3-add-a-feature-step-by-step) and use the [checklist](#the-checklist).

---

## Table of contents

1. [The one rule that makes it "clean"](#part-1-the-one-rule)
2. [The layers, mapped to this project](#part-2-the-layers)
3. [Add a feature, step by step](#part-3-add-a-feature-step-by-step)
4. [The cross-cutting machinery (mediator, validation, transactions)](#part-4-the-machinery)
5. [Talking across modules — contracts, and the **saga** pattern (what it is, terminology, how to build one)](#part-5-across-modules)
6. [Testing](#part-6-testing)
7. [The checklist](#the-checklist)

---

## Part 1: The one rule

Clean architecture is mostly **one rule**: *source code dependencies point inward, toward the domain.* The business rules (the "domain") don't know about the database, the web framework, or JSON. The outer layers depend on the inner ones, never the reverse.

You can literally see this rule enforced by the project references. The domain project has **none**:

- `src/Modules/Students/Students.Domain/Students.Domain.csproj` — zero `<ProjectReference>`, zero NuGet packages. It's pure C#.
- `src/Modules/Students/Students.Application/Students.Application.csproj` — references **only** `Students.Domain` (plus building blocks). It does **not** reference EF Core or ASP.NET.
- `src/Modules/Students/Students.Infrastructure/...csproj` and `Students.Presentation/...csproj` — these reference `Students.Application`. This is where EF Core and ASP.NET live.

So if you ever find yourself wanting to `using Microsoft.EntityFrameworkCore;` inside the Domain or Application layer — stop. That dependency points the wrong way. The fix is always an **interface** in the inner layer, implemented in the outer layer (you'll see this everywhere below).

Why bother? Because the things that change fastest (the database, the API shape, the UI) are kept at the edges, and the thing that's most valuable and most stable (the business rules) sits in the middle, testable without a database or a web server.

---

## Part 2: The layers

This solution is a **modular monolith**: one deployable API, split into self-contained modules (`Students`, `Library`), each with its own database. Within a module, the layers are separate projects:

| Layer | Project (Students example) | Depends on | What lives here |
|-------|----------------------------|------------|-----------------|
| **Domain** | `Students.Domain` | *(nothing)* | Aggregates, value objects, enums, business rules |
| **Contracts** | `Students.Contracts` | *(nothing)* | Interfaces/DTOs other modules may use |
| **Application** | `Students.Application` | Domain | Use cases (commands & queries), repository *interfaces* |
| **Infrastructure** | `Students.Infrastructure` | Application | EF Core, repository *implementations*, DB access |
| **Presentation** | `Students.Presentation` | Application | HTTP endpoints |
| **Host** | `src/Api/CleanArch.Api` | every Infrastructure + Presentation | Wires it all together, runs the web server |

Shared building blocks (the mediator, outbox, pagination) live under `src/BuildingBlocks*`.

### How a request flows

```
HTTP request
  → Presentation (StudentEndpoints.cs)            maps the route to a command/query
    → Application (CreateStudent.Handler)          the use case; orchestrates the domain
      → Domain (Student.Create)                    enforces the business rules
      → Application interface (IStudentRepository)  "I need to save this"
        → Infrastructure (EfStudentRepository)      actually talks to the database
```

Notice the handler depends on `IStudentRepository` (an **interface in the Application layer**), and the EF implementation is injected at runtime. The Application layer never sees EF Core.

---

## Part 3: Add a feature, step by step

Let's walk the layers by following one complete feature that already exists: **"enroll a new student"** — `CreateStudent`. Open these files side by side.

### Step 1 — Model the rule in the Domain

A feature usually starts with an **aggregate**: an object that owns its data and guards its own invariants. Look at `src/Modules/Students/Students.Domain/Student.cs`:

- The constructor is `private`. You can't `new` a `Student` from outside.
- You create one through a **static factory** `Student.Create(...)`, which validates everything (name required, email contains `@`, date of birth before enrollment) and throws `DomainException` if a rule is broken.
- Properties have `private set` — outside code can read state but only change it through methods like `Withdraw()` or `EnrollIn(...)`.

This is the heart of the pattern: **the object cannot exist in an invalid state.** The validation isn't in a service or a controller — it's in the thing itself.

Supporting domain building blocks to study:
- **Value object** (no identity, validated, immutable): `Students.Domain/Address.cs`, or `Grade.cs` (a letter grade + its points).
- **Enum**: `Students.Domain/StudentStatus.cs`.
- **Domain exception**: `Students.Domain/DomainException.cs` — thrown when a business rule is violated; later turned into an HTTP 400 (see Step 7).
- **A richer aggregate** with a child collection and a real algorithm: `Students.Domain/CourseSection.cs` — it owns a roster and runs the waitlist (when full, new students are waitlisted; dropping a seated student promotes the next in line).

### Step 2 — Define the use case in the Application layer

A use case is a **command** (changes state) or a **query** (reads state). The convention here: one static class per use case, holding a `Command`/`Query`, an optional `Validator`, and a `Handler`. See `src/Modules/Students/Students.Application/Students/CreateStudent.cs`:

```csharp
public static class CreateStudent
{
    public sealed record Command(string FirstName, string LastName, string Email,
        DateOnly DateOfBirth, DateOnly EnrolledOn)
        : IRequest<Guid>, IStudentsCommand, IAuditableRequest;   // <- marker interfaces

    public sealed class Validator : AbstractValidator<Command> { /* FluentValidation rules */ }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IStudentRepository _repository;          // <- an interface, not EF
        public Handler(IStudentRepository repository) => _repository = repository;

        public async Task<Guid> Handle(Command command, CancellationToken ct)
        {
            var student = Student.Create(/* ... */);              // <- domain does the work
            await _repository.AddAsync(student, ct);              // <- just "stage" it
            return student.Id;
        }
    }
}
```

Three things a beginner should notice:

1. The handler depends on **`IStudentRepository`**, defined in `Students.Application/Abstractions/IStudentRepository.cs`. The Application layer says *what* it needs ("add a student"); the Infrastructure layer decides *how*.
2. The handler does **not** call `SaveChanges`. `AddAsync` only stages the entity. A pipeline behavior commits the transaction around the whole handler (see [Part 4](#part-4-the-machinery)). This is the *unit of work*.
3. The command carries **marker interfaces** — `IStudentsCommand` (run me in a transaction) and `IAuditableRequest` (record me to the audit log). They're empty interfaces (`Students.Application/IStudentsCommand.cs`) that let cross-cutting behaviors target only the requests that need them.

### Step 3 — Define the repository interface (still in Application)

`Students.Application/Abstractions/IStudentRepository.cs` declares what persistence the use cases need — `AddAsync`, `GetAsync`, `AddHoldAsync`, etc. **No EF types appear here.** It returns domain objects (`Student`).

### Step 4 — Implement persistence in the Infrastructure layer

Now we cross into the outer layer, where EF Core is allowed. `Students.Infrastructure/Repositories/EfStudentRepository.cs` implements the interface using the `DbContext`:

```csharp
public async Task AddAsync(Student student, CancellationToken ct) =>
    await _db.Students.AddAsync(student, ct);   // stages only — the unit of work commits
```

EF needs to know how to map the aggregate to tables **without** the domain knowing about EF. That mapping lives in `Students.Infrastructure/Persistence/EntityConfigurations/StudentConfiguration.cs` (table name, column lengths, the owned `Address` value object flattened into columns, the owned `EmergencyContacts` child table, enums stored as strings). The `DbContext` itself is `Students.Infrastructure/Persistence/StudentsDbContext.cs`, which just applies all configurations from the assembly.

### Step 5 — Reads are separate (CQRS-lite)

Writes go through the repository + aggregate. **Reads** take a shortcut: they project straight to a response shape, so the SQL fetches exactly what the endpoint needs.

- The query: `Students.Application/Students/GetStudentDetail.cs` — a `Query` and a `Handler` that delegates to a read service.
- The read interface: `Students.Application/Abstractions/IStudentReadService.cs`.
- The implementation: `Students.Infrastructure/Reads/StudentReadService.cs` — uses `AsNoTracking()` and `.Select(...)` to build a per-endpoint DTO (e.g. `GetStudentDetail.Response`). It never loads the full aggregate.

Rule of thumb in this codebase: **one response record per endpoint**, never a shared fat DTO with half the fields null. List reads are paged with `PagedResult<T>` (`src/BuildingBlocks/Pagination/`). See `SearchStudents.cs` for the canonical paged search.

### Step 6 — Wire the dependencies

Interfaces are useless until something maps them to implementations. Each module has a `DependencyInjection.cs`:

- `Students.Infrastructure/DependencyInjection.cs` → `AddStudentsModule(...)`: registers the `DbContext`, `IStudentRepository → EfStudentRepository`, read services, etc.
- `Students.Application/DependencyInjection.cs` → `AddStudentsApplication()`: scans the assembly to register all handlers and validators (`AddHandlersFromAssembly`, `AddValidatorsFromAssembly`).

The host calls these in `src/Api/CleanArch.Api/Program.cs`:

```csharp
builder.Services
    .AddApiServices()
    .AddMediator()
    .AddStudentsModule(studentsConnectionString)
    .AddLibraryModule(libraryConnectionString);
```

### Step 7 — Expose it over HTTP (Presentation)

Endpoints map a route to a command/query and send it through the mediator. `Students.Presentation/StudentEndpoints.cs`:

```csharp
group.MapPost("/students", async (CreateStudent.Command command, ISender sender, CancellationToken ct) =>
{
    var id = await sender.Send(command, ct);
    return Results.Created($"/students/{id}", new { id });
})
.WithName("CreateStudent").RequireAuthorization();
```

The endpoint is thin: it has no business logic. It binds the request, calls `ISender.Send`, and shapes the HTTP response. Endpoints are registered in `Program.cs` (`app.MapStudentEndpoints(versionSet)`).

What about the `DomainException` thrown back in Step 1? It's turned into a clean `400 Problem Details` response by a global handler: `src/Api/CleanArch.Api/GlobalExceptionHandler.cs`. So your domain throws a plain exception and the host translates it — the domain stays ignorant of HTTP.

### Step 8 — Add the database migration

Because you changed the EF model (a new entity/column), generate a migration. From the repo root:

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/Students/Students.Infrastructure \
  --startup-project src/Api/CleanArch.Api \
  --context StudentsDbContext \
  --output-dir Persistence/Migrations
```

(There are two `DbContext`s, so pass `--context`.) Existing migrations live in `Students.Infrastructure/Persistence/Migrations/`. In Development, the host applies them on startup (`WebApplicationExtensions.cs`).

---

## Part 4: The machinery

How does sending a `Command` end up running validation and a transaction without the handler doing anything? Through a **mediator with a pipeline**. This project ships a small hand-rolled one in `src/BuildingBlocks/Messaging/` (no MediatR).

- `ISender.Send(request)` finds the one handler for that request type and runs it — but first it wraps the call in a chain of **pipeline behaviors** (`IPipelineBehavior<TRequest, TResponse>`).
- The behaviors, in `src/BuildingBlocks/Messaging/Behaviors/`:
  - **`LoggingBehavior`** — logs every request.
  - **`AuditBehavior`** — records requests marked `IAuditableRequest` to an audit sink.
  - **`ValidationBehavior`** — runs the FluentValidation `Validator` (if any) and throws before the handler if invalid.
- Each module adds its own **`TransactionBehavior`** (`Students.Infrastructure/Behaviors/`): for requests marked `IStudentsCommand`, it opens a DB transaction, runs the handler, then `SaveChanges` + commits. That's why your handler can just call `AddAsync` and return — the behavior commits everything (the new entity *and* any outbox messages) atomically.

So the marker interfaces from Step 2 are the "switches" that opt a request into auditing and transactions. A query carries neither marker, so it skips both — no transaction overhead on reads.

This is clean architecture paying off: cross-cutting concerns (logging, validation, transactions, audit) are added **around** your use case, not tangled inside it.

---

## Part 5: Across modules

Modules must not reach into each other's database or domain. Two sanctioned ways to communicate:

### Synchronous: published contracts

A module publishes an interface in its `*.Contracts` project (which has zero dependencies, so anyone can reference it). Example: `Students.Contracts/IStudentDirectory.cs` lets the Library look up a student by id:

```csharp
public interface IStudentDirectory
{
    Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken ct);
}
```

The Library's `BorrowBook` handler (`Library.Application/Loans/BorrowBook.cs`) depends on `IStudentDirectory` to check the student exists — without touching the Students database. The implementation lives in `Students.Infrastructure` and owns the DB access.

### Asynchronous: sagas and the transactional outbox

This is the big one, so we'll take it slowly: first *why* you need it, then *what the words mean*, then *how to build one*.

#### The problem: there is no transaction across two databases

Each module owns its own database. The Students module can write a `StudentHold`; the Library module can waive a `Loan`'s fine. But you **cannot** wrap "place a hold in the Students DB" and "waive a fine in the Library DB" in a single transaction — a database transaction lives inside *one* database. There is no `BEGIN TRAN` that spans both.

So when a business process needs to touch two modules — *"a student's fines piled up, so place a hold; but if the student already left, undo the fines"* — you can't do it atomically. You need a different pattern. That pattern is a **saga**.

#### What is a saga?

> A **saga** is a business process that spans multiple local transactions in different services/modules, coordinated by **events** rather than by one big shared transaction. Each step does its own local transaction and publishes an event; the next step reacts to that event. If a later step fails, earlier steps are undone by **compensating actions** — not by a rollback (you can't roll back a committed transaction in another database), but by a *new* action that semantically reverses the first.

In other words: instead of one atomic distributed transaction (impossible), you get a **chain of small, local, atomic transactions glued together by events**, plus a plan for undoing them if something downstream says "no."

#### Terminology you'll meet

| Term | What it means here |
|------|--------------------|
| **Local transaction** | An ordinary DB transaction inside one module/DB. Each saga step is one of these. |
| **Integration event** | The message a module publishes for another to consume (e.g. `StudentHoldRequested`). A plain record. |
| **Saga** | The whole multi-step workflow (request a hold → place or reject it → compensate). |
| **Forward leg** | The happy-path step (request and place the hold). |
| **Reverse / compensating leg** | The undo step that runs when a later step is rejected (waive the fines). |
| **Compensating action** | A *new* operation that semantically undoes a committed one (you can't `ROLLBACK` another DB — you `WaiveFine()` instead). |
| **Choreography vs orchestration** | *Choreography* (used here): each module reacts to events, no central boss. *Orchestration*: one coordinator object drives every step. |
| **Transactional outbox** | The technique that makes "commit my change **and** publish my event" atomic, by writing the event into a table **in the same transaction** as the change. |
| **At-least-once delivery** | The processor may deliver a message **more than once** (e.g. it crashed after doing the work but before marking the message done). |
| **Idempotency** | Designing the consumer so a *repeated* delivery is a no-op. The consumer's job, because of at-least-once. |
| **Eventual consistency** | The two databases are consistent *eventually*, not instantly. Between the local transactions there's a brief window where they disagree — that's the price of not having a distributed transaction. |
| **Dead-letter** | A message that failed past the retry cap, parked for an operator to inspect or replay. |

#### Why the *transactional outbox* specifically?

The naive approach is "save to my DB, then publish a message to a broker." That's two separate operations — and it has a classic bug called the **dual-write problem**: if the process crashes *between* the two, you either saved the change but lost the event, or published the event for a change that got rolled back. Now your modules are permanently out of sync.

The **transactional outbox** fixes this by making the event part of the *same* local transaction as the business change:

1. The command writes its business change **and** an "outbox" row into the **same database**, in the **same transaction** (`IOutbox.Enqueue(...)`). They commit together or not at all — no dual-write gap.
2. A separate background `OutboxProcessor` polls the outbox table and tries to deliver each message.
3. Delivery can fail and be **retried** independently. If it succeeds twice (crash before marking done), that's fine — the consumer is **idempotent**.

So: *publishing is atomic with the change; delivery is a separate, retried, at-least-once step.* That trade is the whole point.

#### The moving parts in this project (`src/BuildingBlocks.Outbox/` + `src/BuildingBlocks/Outbox/`)

| Piece | File | Role |
|-------|------|------|
| `IOutbox.Enqueue<T>` | `BuildingBlocks/Outbox/IOutbox.cs` | A command calls this to stage an event (no SaveChanges — the unit of work commits it). |
| `OutboxMessage` | the row written to each module's `Outbox` table | `Id`, `Type` (the event's type name), JSON `Content`, retry count, correlation id. |
| `OutboxProcessor<TContext>` | `BuildingBlocks.Outbox/OutboxProcessor.cs` | Background service: polls a module's outbox, calls its dispatcher, retries, dead-letters. |
| `IOutboxDispatcher<TContext>` | each module's `*/Outbox/*OutboxDispatcher.cs` | Routes a message (by its `Type`) to the right consumer contract. |
| Published contract | the consumer module's `*.Contracts` interface | What the dispatcher calls — the other module's idempotent entry point. |

#### Trace two real sagas end to end

**Saga A — fine → hold, with compensation** (the textbook two-leg saga). Files to open in order:

1. **Forward leg, publish:** `Library.Application/Loans/AssessFine.cs` — when cumulative fines cross the limit, it `_outbox.Enqueue(new StudentHoldRequested(...))` *in the same transaction* as the fine.
2. **Forward leg, route:** `Library.Infrastructure/Outbox/LibraryOutboxDispatcher.cs` — `case nameof(StudentHoldRequested)` → calls `IStudentHoldService.PlaceHoldAsync(messageId, ...)`.
3. **Forward leg, consume:** `Students.Infrastructure/Contracts/StudentHoldService.cs` — *if the student is still active*, records the hold. **But if the student was withdrawn/graduated, it rejects** — and publishes a `StudentHoldRejected` event into the **Students** outbox. That's the start of the reverse leg.
4. **Reverse (compensating) leg:** `Students.Infrastructure/Outbox/StudentsOutboxDispatcher.cs` routes `StudentHoldRejected` → `IFineWaiver.WaiveStudentFinesAsync(...)` (`Library.Infrastructure/Contracts/FineWaiver.cs`), which **waives the fines** that triggered the request. The library is made whole because the hold couldn't be applied — that's *compensation*, not rollback.

**Saga B — student withdrawal cascades into the Library** (a simpler, one-direction saga we'll rebuild below):

1. `Students.Application/Students/WithdrawStudent.cs` enqueues `StudentWithdrawn` on the transition to withdrawn.
2. `Students.Infrastructure/Outbox/StudentsOutboxDispatcher.cs` routes it → `ILibraryWithdrawalService`.
3. `Library.Infrastructure/Contracts/LibraryWithdrawalService.cs` returns all the student's active loans and cancels their reservations — idempotently.

#### Step-by-step: build your own saga

Goal: *"when X happens in module A, do Y in module B, reliably."* We'll mirror the withdrawal saga (A = Students, B = Library).

**Step 1 — Define the integration event.** A plain record carrying just the ids/values B needs. Because it's published by a *command*, it lives in the Application layer's `Outbox` folder so the command can see it. See `Students.Application/Outbox/StudentWithdrawn.cs`:

```csharp
public sealed record StudentWithdrawn(Guid StudentId);
```

**Step 2 — Enqueue it in the command, atomically, on the meaningful transition.** Don't publish on every call — publish on the *state change*. See `WithdrawStudent.cs`:

```csharp
var wasActive = student.Status != StudentStatus.Withdrawn;
student.Withdraw();
if (wasActive)                                  // only on the transition
    _outbox.Enqueue(new StudentWithdrawn(student.Id));
```

The command is an `IStudentsCommand`, so the `TransactionBehavior` commits the student change **and** the outbox row together. Atomic publish — no dual-write gap.

> **A real gotcha you'll hit.** The shared `IOutbox` is registered as a single **open-generic** (`AddOutboxWriter<LibraryDbContext>()`), and the Library module already owns it. If a second module also registered `IOutbox`, DI resolution would be **ambiguous (last-registration-wins)** and would silently break the first module's saga. That's why the Students module has its **own** writer, `IStudentOutbox` / `StudentOutbox` (`Students.Infrastructure/Outbox/StudentOutbox.cs`), which writes to *its* outbox table directly. Lesson: an open-generic single-registration abstraction can only serve one consumer — give the second one its own.

**Step 3 — Define B's published contract.** B exposes an interface in its `*.Contracts` project (zero dependencies, so A can reference it). See `Library.Contracts/ILibraryWithdrawalService.cs`:

```csharp
public interface ILibraryWithdrawalService
{
    Task OnStudentWithdrawnAsync(Guid studentId, CancellationToken cancellationToken);
}
```

**Step 4 — Implement the consumer in B's Infrastructure.** It works on **B's own `DbContext`**, and — because the dispatcher runs outside a request — it calls `SaveChanges` itself. Crucially, it's **idempotent**. See `Library.Infrastructure/Contracts/LibraryWithdrawalService.cs`: it loads the student's *active* loans/reservations and processes them; a redelivery finds nothing active left to do, so it's a safe no-op.

**Step 5 — Route the event in A's dispatcher.** Switch on the event's type name and call the contract, **passing the message id** so the consumer can dedupe. See `StudentsOutboxDispatcher.cs`:

```csharp
case nameof(StudentWithdrawn):
    var e = JsonSerializer.Deserialize<StudentWithdrawn>(content)!;
    return _withdrawal.OnStudentWithdrawnAsync(e.StudentId, cancellationToken);
```

**Step 6 — Register the consumer in B's DI.** `Library.Infrastructure/DependencyInjection.cs`:

```csharp
services.AddScoped<ILibraryWithdrawalService, LibraryWithdrawalService>();
```

That's it. A publishes atomically; the background processor delivers; B reacts idempotently.

#### Idempotency: the discipline you cannot skip

Because delivery is **at-least-once**, every consumer must make a *repeated* delivery a no-op, or you'll double-charge, double-hold, double-return. This project uses three concrete strategies — study them:

1. **Marker by existing state.** `LibraryWithdrawalService` and `FineWaiver` just look at current state ("are there active loans / outstanding fines?"). If the work is already done, there's nothing to do. The cleanest kind when it fits.
2. **Marker row keyed by the message id.** `StudentHoldService` records the `StudentHold` (or the rejection outbox row) with `Id = messageId`. Re-running checks "does a row with this id already exist?" and returns early.
3. **Marker field keyed by the message id.** `StudentBilling.cs` (the fine → account-charge consumer) stamps the new ledger entry's `SourceReference = messageId`, and skips if `account.HasEntryFrom(messageId)`. The charge *is* its own dedupe marker — no separate table.

The unifying idea: the **message id is stable across redeliveries** (it's the outbox row's id), so it's the perfect idempotency key.

#### When delivery keeps failing: retries, dead-letter, replay

The `OutboxProcessor` retries a failing message a few times; past the cap it **dead-letters** it (parks it instead of blocking the queue). Operators can inspect and replay:

- `GET /library/outbox/dead-letter` — list parked messages (`GetDeadLetter.cs`).
- `POST /library/outbox/dead-letter/{id}/replay` — requeue one (`ReplayDeadLetter.cs`).
- There's even a dev-only endpoint that injects a deliberately unroutable message so you can watch the retry → dead-letter → replay path (`WebApplicationExtensions.cs`).

---

## Part 6: Testing

Because the domain and application layers have no infrastructure dependencies, you test them **without a database or web server** — fast, in-memory.

Look in `tests/CleanArch.UnitTests/`:

- **Domain tests** — exercise the rules directly: `StudentTests.cs`, `CourseSectionTests.cs` (waitlist promotion), `GradeTests.cs`. No mocks; just call the aggregate and assert.
- **Handler tests** — run a use case with **hand-written fakes** instead of real repositories: `CreateStudent`-style handler tests, `EnrollInSectionHandlerTests.cs`. The fakes live in `tests/CleanArch.UnitTests/Fakes.cs` and implement the Application interfaces (e.g. `FakeStudentRepository : IStudentRepository`) with simple in-memory dictionaries.

That's the reward for the dependency rule: a handler depends on `IStudentRepository`, so a test hands it a `FakeStudentRepository` and verifies behaviour with zero I/O.

Run them all:

```bash
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj
```

---

## The checklist

To add a new write feature (say, `DoSomething` in the Students module):

1. **Domain** — add/extend an aggregate in `Students.Domain`; put the rules in a static factory or method that throws `DomainException`. *(example: `Student.cs`)*
2. **Application — interface** — add any persistence you need to `Students.Application/Abstractions/IStudentRepository.cs` (or a new repo interface). *(returns domain types, no EF)*
3. **Application — use case** — add `Students.Application/.../DoSomething.cs` with `Command : IRequest<T>, IStudentsCommand, IAuditableRequest`, an optional `Validator`, and a `Handler` that orchestrates the domain via the interface. *(example: `CreateStudent.cs`)*
4. **Infrastructure — implement** — implement the repo method in `EfStudentRepository.cs`; if you added an entity, add an `IEntityTypeConfiguration` and a `DbSet`. *(examples: `EfStudentRepository.cs`, `StudentConfiguration.cs`)*
5. **Infrastructure — register** — map interface → implementation in `Students.Infrastructure/DependencyInjection.cs`. *(handlers/validators auto-register via assembly scan)*
6. **Presentation** — add an endpoint in `Students.Presentation/StudentEndpoints.cs` that sends the command via `ISender`. *(example: the `POST /students` endpoint)*
7. **Migration** — `dotnet ef migrations add ...` if the schema changed (Step 8 above).
8. **Tests** — a domain test for the rule + a handler test using fakes from `Fakes.cs`.

For a **read** feature, skip the repository/aggregate: add a `Query` + `Handler` that delegates to a read service (`IStudentReadService`) projecting to a per-endpoint response record. *(example: `GetStudentDetail.cs`)*

For **cross-module** work: a synchronous lookup → publish an interface in `*.Contracts`; an eventual side effect → enqueue an outbox event and handle it in the dispatcher. *(examples in [Part 5](#part-5-across-modules))*

---

### Golden rules to keep it clean

- **Dependencies point inward.** If the Domain or Application layer needs `using Microsoft.EntityFrameworkCore` or `using Microsoft.AspNetCore`, you've taken a wrong turn — introduce an interface instead.
- **Aggregates guard themselves.** No setters from outside; validate in factories/methods; throw `DomainException`.
- **Handlers orchestrate, they don't compute rules.** The rule belongs in the domain.
- **Endpoints are thin.** Bind → `Send` → shape the response. No business logic.
- **One response record per read endpoint.** Project exactly what you need.
- **Cross-module = contracts or outbox.** Never another module's `DbContext`.
