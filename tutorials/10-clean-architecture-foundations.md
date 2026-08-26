# Clean Architecture — Foundations

**Who this is for:** someone who has heard "clean architecture" used as a synonym for "lots
of folders" and wants to know what it actually asks of you, and what you get in return.

**What you'll be able to do by the end:** explain the one rule the whole thing rests on,
decide which layer any given piece of code belongs in, recognise a violation before the
compiler does, and know which costs you are signing up for.

**What you need first:** you can read C#. Nothing else.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [The one rule](#1-the-one-rule) | Learn the whole thing in one sentence |
| 2 | [What the rule buys you](#2-what-the-rule-buys-you) | Decide whether you want it |
| 3 | [The layers](#3-the-layers) | Where code goes, and why |
| 4 | [How a request flows](#4-how-a-request-flows) | Follow one call all the way down |
| 5 | [Interfaces are the hinge](#5-interfaces-are-the-hinge) | The trick that makes it possible |
| 6 | [The compiler enforces it](#6-the-compiler-enforces-it) | Why layers are separate projects |
| 7 | [Where does this go?](#7-where-does-this-go) | A decision table for real cases |
| 8 | [Modules — the second dimension](#8-modules--the-second-dimension) | Layers *and* modules |
| 9 | [The machinery around your code](#9-the-machinery-around-your-code) | Pipeline behaviours |
| 10 | [What it costs](#10-what-it-costs) | The honest bill |
| 11 | [Wrong turns](#11-wrong-turns) | Six mistakes and their fixes |
| 12 | [Smell test](#12-smell-test) | Run this on your own code |
| 13 | [Glossary](#13-glossary) | Every term used in this guide |

---

## 1. The one rule

> **Source code dependencies point inward, toward the domain.**

That is it. Everything else — the folder names, the interfaces, the project layout — is
machinery for keeping that one sentence true.

"Inward" means toward your business rules. The rules do not know about the database. They
do not know about HTTP, JSON, EF Core, or ASP.NET. Those things know about *them*.

```
              ┌───────────────────────────────────┐
              │            Presentation           │   HTTP, JSON
              │   ┌───────────────────────────┐   │
              │   │      Infrastructure       │   │   EF Core, the database
              │   │   ┌───────────────────┐   │   │
              │   │   │    Application    │   │   │   use cases
              │   │   │   ┌───────────┐   │   │   │
              │   │   │   │  Domain   │   │   │   │   the rules
              │   │   │   └───────────┘   │   │   │
              │   │   └───────────────────┘   │   │
              │   └───────────────────────────┘   │
              └───────────────────────────────────┘
                    dependencies point ───►  inward only
```

You can verify the rule holds in this repository without reading a line of C#. Open
`src/Modules/Students/Students.Domain/Students.Domain.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

</Project>
```

Empty. No packages, no project references. The most valuable code in the system depends on
nothing at all.

---

## 2. What the rule buys you

Four things, and they are worth stating concretely rather than as slogans.

**Your rules are testable without infrastructure.** A test for "a student's email must
contain an `@`" needs no database, no web server, no mocking framework — it constructs a
`Student` and asserts. That test runs in microseconds, which means you write more of them,
which means the rules are actually verified.

**Replaceable edges.** SQLite here, SQL Server later, with no change to the Domain or
Application layers. That claim is only true *because* those layers cannot name an EF type;
if they could, the swap would be a rewrite.

**The valuable part stays still.** The database, the API shape, the UI framework — those
change often and for reasons that have nothing to do with your business. Keeping them at
the edges means their churn doesn't reach the middle.

**You can find things.** "Where is the rule about waitlists?" has one answer: the domain.
Not "in a service, or a controller, or a stored procedure, or all three."

> **The honest counter-case.** For a genuinely small application — a form, a table, a
> report — this is more structure than the problem deserves. Clean architecture pays off
> when the business rules are the complicated part. If your rules are "save what the user
> typed", use something simpler and don't feel bad about it.

---

## 3. The layers

| Layer | Answers | Contains | Never contains |
|---|---|---|---|
| **Domain** | "What is always true?" | Aggregates, value objects, enums, invariants | Any framework at all |
| **Application** | "What can the system do?" | Commands, queries, handlers, repository *interfaces* | EF Core, ASP.NET |
| **Infrastructure** | "How do we actually do it?" | `DbContext`, repository *implementations*, mappings, external clients | Business rules |
| **Presentation** | "How is it exposed?" | HTTP endpoints, request/response shaping | Business logic |

### Domain — the rules

Objects that cannot exist in an invalid state. The pattern: private constructor, static
factory that validates, private setters, behaviour as methods.

```csharp
public static Student Create(string firstName, string lastName, string email,
                             DateOnly dateOfBirth, DateOnly enrolledOn)
{
    if (string.IsNullOrWhiteSpace(firstName)) throw new DomainException("First name is required.");
    if (!email.Contains('@'))                 throw new DomainException("Email is not a valid address.");
    if (dateOfBirth >= enrolledOn)            throw new DomainException("Date of birth must be before the enrollment date.");

    return new Student(Guid.NewGuid(), firstName.Trim(), lastName.Trim(),
                       email.Trim().ToLowerInvariant(), dateOfBirth, enrolledOn);
}
```

There is no other way to make a `Student`. That is the point — validation you can't bypass
because there is no second door.

**Aggregates** own their children and guard the rules that span them.
`Students.Domain/CourseSection.cs` is the good example here: it owns a roster and runs the
waitlist, so "when a seated student drops, promote the first person waiting" lives inside
the object that knows both facts. Not in a service that has to load two things and hope.

**Value objects** have no identity — they *are* their values. `Address`, `Grade`. Validated
on construction, immutable after.

### Application — the use cases

One file per use case, holding a `Command` (changes state) or `Query` (reads), an optional
`Validator`, and a `Handler`. The handler **orchestrates**; it does not compute rules:

```csharp
public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
{
    var student = Student.Create(...);              // the domain does the work
    await _repository.AddAsync(student, cancellationToken);   // an interface, not EF
    return student.Id;
}
```

Three lines. If a handler grows past orchestration and starts making decisions, the decision
belongs in the domain.

This layer also declares **what persistence it needs**, as interfaces — `IStudentRepository`
— without saying how.

### Infrastructure — the how

EF Core lives here. Repository implementations, `DbContext`, entity configurations, external
API clients. This layer knows about the Application layer; the Application layer has never
heard of it.

### Presentation — the edge

Endpoints bind a request, send it, shape the response:

```csharp
group.MapPost("/students", async (CreateStudent.Command command, ISender sender, CancellationToken ct) =>
{
    var id = await sender.Send(command, ct);
    return Results.Created($"/students/{id}", new { id });
});
```

No business logic. Note it doesn't even catch `DomainException` — the host translates that
into a `400` centrally, so the domain never learns that HTTP exists.

---

## 4. How a request flows

```
HTTP POST /students
  │
  ▼
Presentation   StudentEndpoints.cs          binds JSON → CreateStudent.Command, calls ISender
  │
  ▼
Application    CreateStudent.Handler         the use case: orchestrates
  │
  ├──────────► Domain        Student.Create  enforces the rules — throws if broken
  │
  └──────────► Application   IStudentRepository.AddAsync    "I need this saved"
                   │
                   ▼
               Infrastructure   EfStudentRepository         actually talks to the database
```

Read the last two lines again, because that is where the rule is doing its work. The handler
calls an interface **defined in its own layer**. The class that implements it lives one layer
out and is supplied at runtime. The arrow of *dependency* points inward even though the arrow
of *control* points outward.

That inversion is the whole trick, and it has a name.

---

## 5. Interfaces are the hinge

The Application layer needs to save a student. Saving means a database. Databases are
Infrastructure. So Application must depend on Infrastructure — which points the wrong way.

**Dependency inversion** resolves it: the inner layer declares the interface it needs; the
outer layer implements it.

```
   WITHOUT inversion                  WITH inversion
   ─────────────────                  ──────────────
   Application                        Application
       │ depends on                       │ declares
       ▼                                  ▼
   EfStudentRepository                IStudentRepository        ◄── interface lives INSIDE
       │                                  ▲
       ▼                                  │ implements
   the database                       EfStudentRepository       ◄── implementation lives OUTSIDE
                                          │
                                          ▼
                                      the database
```

The interface belongs to the **consumer**, not the implementer. `IStudentRepository` is in
`Students.Application/Abstractions/` — the layer that *uses* it — even though the only class
implementing it lives in Infrastructure. And it speaks in domain terms: it takes and returns
`Student`, never an EF entity or a DTO.

At runtime, dependency injection supplies the real implementation. The handler never learns
which one it got — which is also why a test can hand it an in-memory fake and nothing
notices.

**Whenever a dependency points the wrong way, the answer is an interface in the inner layer.**
That single move solves nearly every violation you'll hit.

---

## 6. The compiler enforces it

Layers are separate **projects**, not folders. That is deliberate and it is what makes the
architecture real rather than aspirational.

```bash
dotnet add Students.Application reference Students.Domain          # allowed
dotnet add Students.Domain reference Students.Infrastructure       # you can type this...
```

...and now `Students.Domain` compiles against EF Core, and nothing stops a value object from
taking a `DbContext`. Folders would let this happen silently. Project references make it a
deliberate act you have to commit.

The practical version: **if you find yourself wanting to add a reference to Domain or
Application, that urge is the design telling you something.** Don't add it. Add an
interface instead.

> A useful side effect: `dotnet build` is an architecture test. There is no separate linter
> to run, no convention doc to remember. The dependency rule either holds or the build fails.

---

## 7. Where does this go?

The question you will actually ask, twenty times a week.

| The code | Layer | Why |
|---|---|---|
| "An email must contain `@`" | **Domain** | Always true, regardless of how it arrives |
| "Page size must be 1–100" | **Application** (validator) | A request constraint, not a business truth |
| "When a seat frees, promote the first waitlisted student" | **Domain** | A rule spanning the aggregate's own data |
| "Load the student, charge them, save" | **Application** (handler) | Orchestration |
| "`Email` is `nvarchar(256)`" | **Infrastructure** | Storage detail |
| "Return 404 when it's null" | **Presentation** | An HTTP concern |
| "Call the payments provider" | **Infrastructure**, behind an Application interface | External system |
| "Round to 2 decimal places for money" | **Domain** (value object) | Part of what money *is* |
| "Cache this for 5 minutes" | **Infrastructure** (a decorator) | Performance, not behaviour |
| "This student's fines exceed the limit" | **Domain** | A business threshold |
| "Retry the HTTP call three times" | **Infrastructure** | Transport detail |

Two tests that resolve most disagreements:

1. **Would this rule still be true on paper, with no computers?** Then it's Domain.
2. **Would it change if we swapped the database, or the UI?** Then it's an edge.

---

## 8. Modules — the second dimension

Layers are one axis. This codebase has a second: **modules**. `Students`, `Library`,
`TestPlans`, `TesterGuide` — each with its own five layers *and its own database*.

```
                Domain    Application    Infrastructure    Presentation
   Students        ▪           ▪               ▪                ▪        → students.db
   Library         ▪           ▪               ▪                ▪        → library.db
   TestPlans       ▪           ▪               ▪                ▪        → testplans.db
   TesterGuide     ▪           ▪               ▪                ▪        → testerguide.db
```

One process, one deployment — a **modular monolith**. The layers stop code from depending
downward; the modules stop it from depending sideways.

Two rules govern the sideways direction, and both exist because the databases are separate:

- **Reads** go through a published contract — an interface in the owning module's
  `*.Contracts` project, which has zero dependencies so anyone may reference it.
- **Writes** go through the outbox, because a transaction cannot span two databases.

[Guide 60](60-talking-across-modules.md) covers both in full. What matters here is the
principle: **a module's public surface is the interface it publishes, not its tables.**

---

## 9. The machinery around your code

Your handler is three lines because logging, validation, transactions and auditing happen
*around* it rather than inside it. A small mediator dispatches each request through a chain
of **pipeline behaviours**:

```
   ISender.Send(command)
     │
     ├─ LoggingBehavior            every request
     │   ├─ AuditBehavior          only IAuditableRequest — outside validation, so
     │   │                         rejected commands are audited too
     │   │   ├─ ValidationBehavior only requests with a validator
     │   │   │   ├─ TransactionBehavior   only this module's commands
     │   │   │   │   └─ YOUR HANDLER
```

Each layer is opted into by a **marker interface** on the request:

| Marker | Effect |
|---|---|
| `IRequest<T>` | Dispatchable, returns `T` |
| `IAuditableRequest` | Record it to the audit trail |
| `I<Module>Command` | Wrap it in a transaction on *that module's* database |

A query carries none of the last two, so it skips auditing and transactions entirely — no
overhead on reads, no configuration, just the absence of a marker.

**Why this matters architecturally:** cross-cutting concerns are the classic way a clean
design rots. Someone needs an audit log, so they add a line to a handler. Then to forty
handlers. Then someone forgets one. Behaviours make it structural — you cannot forget,
because you never wrote it in the first place.

---

## 10. What it costs

An honest bill, because every list of benefits without one is marketing.

**More files.** A feature that could be one method is a command, a validator, a handler, a
repository method, an endpoint. Five files for one operation. That is a real cost, paid on
every feature, and it is only worth it when the rules are worth protecting.

**Indirection.** "Where does this actually run?" is one hop harder — you find the interface,
then the implementation. Tooling helps; it is still a cost.

**Mapping.** Domain object → response DTO, request → command. Sometimes those shapes are
identical and the mapping feels like pure ceremony. Occasionally it is.

**No cross-module joins or transactions.** The modular part specifically. You compose in the
application layer and accept eventual consistency across modules. Sometimes that is a real
performance or design conversation, not a free lunch.

**A learning curve.** Someone new has to learn where things go before they can add a field.

Worth it when the domain is complex, the system will live for years, or several people work
on it at once. Not worth it for a CRUD form.

---

## 11. Wrong turns

**Anaemic domain.** Classes with public getters and setters and no behaviour, with all the
rules in "services". This is the most common failure, and it is subtle because the folders
still look right. *Fix:* move the rule into the object that owns the data.

**Leaking EF into the Application layer.** A repository interface returning `IQueryable`, or
a handler calling `.Include(...)`. Now Application depends on EF's semantics — including
lazy loading and change tracking — even though the reference is technically absent. *Fix:*
return materialised domain objects or purpose-built DTOs.

**Fat endpoints.** Business logic in the Presentation layer because "it's only an `if`". It
is never only an `if`, and it is now untestable without HTTP. *Fix:* push it into the
handler, then the rule into the domain.

**One DTO for everything.** A `StudentDto` with thirty properties, most null on any given
call. Every endpoint that uses it is coupled to every other. *Fix:* one response record per
endpoint — `GetStudentDetail.Response` is nested inside the feature that returns it.

**Reaching into another module.** Injecting another module's `DbContext` because a contract
would take longer. This works, and it silently deletes the boundary. *Fix:* publish a
contract, or move the boundary.

**Domain objects on the wire.** Serialising an aggregate straight to JSON. Now every internal
rename is a breaking API change, and your invariant-guarded object arrives at the client as a
bag of setters. *Fix:* a response record.

---

## 12. Smell test

Run these against code you are about to commit.

- [ ] Does `Domain` reference anything? It should reference **nothing**.
- [ ] Does `Application` mention `Microsoft.EntityFrameworkCore` or `Microsoft.AspNetCore`? It shouldn't.
- [ ] Do your domain objects have public setters? They shouldn't.
- [ ] Can you construct a domain object in an invalid state? You shouldn't be able to.
- [ ] Is there a business rule in a handler, an endpoint, or a validator that belongs in the domain?
- [ ] Does any repository interface expose `IQueryable` or an EF type?
- [ ] Does one response DTO serve several endpoints with half its fields null?
- [ ] Does any module reference another module's `Infrastructure` or `Domain`?
- [ ] Can you test your newest rule without a database? If not, why not?

---

## 13. Glossary

| Term | Meaning |
|---|---|
| **Aggregate** | A domain object that owns its data and children and guards the rules spanning them |
| **Anaemic domain model** | Data classes with no behaviour, rules pushed into services. The classic failure |
| **Application layer** | Use cases. Orchestrates the domain; declares the interfaces it needs |
| **Behaviour (pipeline)** | Cross-cutting code wrapped around every matching request |
| **Command** | A request that changes state |
| **Contract** | An interface a module publishes for other modules. Zero dependencies |
| **CQRS-lite** | Writes through aggregates; reads projected straight to the response shape |
| **Dependency inversion** | The inner layer declares the interface; the outer layer implements it |
| **Dependency rule** | Source dependencies point inward. The one rule |
| **Domain** | The business rules. The innermost layer; depends on nothing |
| **DTO** | A shape used to move data in or out, separate from the domain model |
| **Handler** | The code that executes one use case |
| **Infrastructure** | The layer where frameworks live — EF Core, HTTP clients, the database |
| **Invariant** | Something always true of an object, enforced by the object itself |
| **Marker interface** | An empty interface used as a switch — audit, transaction |
| **Mediator** | The dispatcher that finds a request's handler and wraps it in behaviours |
| **Modular monolith** | One deployable process, internally partitioned into modules owning their own data |
| **Presentation** | The layer that exposes use cases — here, HTTP endpoints |
| **Query** | A request that only reads |
| **Repository** | An interface describing the persistence a use case needs, in domain terms |
| **Unit of work** | Committing everything one request changed as a single transaction |
| **Value object** | A domain type with no identity, defined by its values, validated on construction |
| **Vertical slice** | One file holding a feature's request, validator and handler |

---

## Where to go next

- **[Adding a feature](20-add-a-feature.md)** — the rule applied, one slice at a time.
- **[Adding a new module](30-add-a-module.md)** — when the work needs a boundary of its own.
