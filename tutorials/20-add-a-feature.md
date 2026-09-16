# Adding a Feature

**Who this is for:** someone who has a module to work in and needs to add something to it —
a new endpoint, a new operation, a new thing the system can do.

**What you'll be able to do by the end:** add a write feature and a read feature end to end,
know which layer each piece belongs in, and understand what the pipeline does around your
handler so you can stop writing it yourself.

**What you need first:** the project runs. [Guide 10](10-clean-architecture-foundations.md)
if the layers are new to you — this guide assumes the dependency rule rather than explaining
it.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What a feature looks like here](#1-what-a-feature-looks-like-here) | The vertical slice |
| 2 | [Writes and reads are different](#2-writes-and-reads-are-different) | Pick the right recipe |
| 3 | [Step 1 — Put the rule in the domain](#3-step-1--put-the-rule-in-the-domain) | Start here, always |
| 4 | [Step 2 — Declare what you need](#4-step-2--declare-what-you-need) | The repository interface |
| 5 | [Step 3 — Write the slice](#5-step-3--write-the-slice) | Command, validator, handler |
| 6 | [Step 4 — Implement persistence](#6-step-4--implement-persistence) | The EF half |
| 7 | [Step 5 — Expose it](#7-step-5--expose-it) | The endpoint |
| 8 | [Step 6 — Migrate](#8-step-6--migrate) | If the schema changed |
| 9 | [Step 7 — Test it](#9-step-7--test-it) | Two tests, no database |
| 10 | [Read features — the shortcut](#10-read-features--the-shortcut) | Skip the aggregate |
| 11 | [Paged lists](#11-paged-lists) | The house convention |
| 12 | [What happens around your handler](#12-what-happens-around-your-handler) | The pipeline, explained |
| 13 | [The checklist](#13-the-checklist) | Run this when doing it for real |
| 14 | [Troubleshooting](#14-troubleshooting) | Symptom, cause, fix |
| 15 | [Cheat sheet](#15-cheat-sheet) | The shapes, in one place |
| 16 | [Glossary](#16-glossary) | Every term used in this guide |

---

## 1. What a feature looks like here

One feature is **one file**: a static class holding everything that feature needs.

```csharp
public static class CreateEquipment
{
    public sealed record Command(...) : IRequest<Guid>, IEquipmentCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command> { ... }

    public sealed class Handler : IRequestHandler<Command, Guid> { ... }
}
```

That is a **vertical slice**. Everything about "create a piece of equipment" is in
`CreateEquipment.cs` — the request shape, its validation rules, and the code that runs. Not
spread across a `Models/` folder, a `Services/` folder and a `Validators/` folder where you
have to open three files to understand one operation.

The nesting is doing real work, too: the type is `CreateEquipment.Command`, so the name is
unambiguous at every call site, and there is no need to invent globally-unique names like
`CreateEquipmentCommandRequestDto`.

**Where the file goes:** `<Module>.Application/<Area>/<FeatureName>.cs` — for example
`Equipment.Application/Inventory/CreateEquipment.cs`, or
`Onboarding.Application/Requests/CreateOnboardingRequest.cs`.

> **Name the class after the action**, in the imperative: `CreateEquipment`, `DeleteEquipment`,
> `ApproveOnboardingStandard`. That name is also what appears in the audit trail as the
> action — see [guide 40](40-auditing.md) — so it should read as something a person did.

---

## 2. Writes and reads are different

Two recipes. Using the write recipe for a read is the most common wasted effort in this
codebase.

| | **Write** (command) | **Read** (query) |
|---|---|---|
| Loads | The aggregate, through a repository | Nothing — projects straight to the response |
| Enforces rules | Yes, in the domain | No rules to enforce |
| Transaction | Yes, via the module's marker | No |
| Audited | Yes, via `IAuditableRequest` | Only if it exposes sensitive data, via `IAuditableRead` |
| Returns | An id, or a small result | A per-endpoint response record |
| Depends on | `I<Thing>Repository` | `I<Thing>ReadService` |

**Why reads take the shortcut.** A read has no invariants to protect, so loading a full
aggregate — with its children, its change tracking, its owned types — to then throw most of
it away is pure cost. A read projects with `.Select(...)` and `AsNoTracking()`, so the SQL
fetches exactly the columns the endpoint returns and nothing else.

This split is often called **CQRS-lite**: the same database, different paths in and out.

Chapters 3–9 walk the write recipe. [Chapter 10](#10-read-features--the-shortcut) covers
reads.

---

## 3. Step 1 — Put the rule in the domain

Start with the rule, not the endpoint.

Ask: *is there anything that must always be true after this operation?* If yes, that belongs
in the domain object — as a factory, or as a method on the aggregate.

```csharp
// src/Modules/Equipment/Equipment.Domain/EquipmentAsset.cs

// Idempotent: releasing an already-available asset is a no-op, not an error — a redelivered
// compensation message must be safe to apply twice.
public void Release()
{
    Status = EquipmentStatus.Available;
    ReservedForOnboardingRequestId = null;
}
```

Sometimes it really is that small. Note the comment on it in the real file — the *idempotent*
guarantee is a rule in itself, and it's the reason the Standard saga's compensation
([guide 60](60-talking-across-modules.md)) can safely redeliver this call.

Where a rule spans an aggregate's own state, it goes on the aggregate, not on whoever calls
it. `OnboardingRequest.Approve()` throws if the request has already been decided;
`MarkReady()` throws unless the saga is actually in progress. Both checks live inside the
object being changed, because only it can see its own current state without a caller loading
it first and hoping nothing raced.

**Two tests for whether something is a domain rule:**

1. Would it still be true on paper, with no computers?
2. Would you be alarmed if some other code path bypassed it?

Two "yes"es means the domain. If the answer is no — "the page size must be at most 100" —
it is a request constraint, and it belongs in the validator ([step 3](#5-step-3--write-the-slice)).

If your feature adds no rule at all — it just stores what it was given — skip this step
honestly. Not every operation has an invariant.

---

## 4. Step 2 — Declare what you need

Your handler will need to load or save something. Declare that as an interface **in the
Application layer**, in domain terms:

```csharp
// src/Modules/Equipment/Equipment.Application/Abstractions/IEquipmentRepository.cs
public interface IEquipmentRepository
{
    Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken);
    Task<EquipmentAsset?> GetAsync(Guid equipmentId, CancellationToken cancellationToken);
    void Remove(EquipmentAsset asset);
}
```

Add the method you need to an existing interface, or create a new one for a new aggregate.

Rules for this interface, and each one prevents a specific leak:

- **It returns domain objects.** `EquipmentAsset`, not `EquipmentAssetEntity`, not a DTO.
- **It never exposes `IQueryable`.** That would hand EF's semantics — deferred execution,
  change tracking, lazy loading — to a layer that must not know EF exists.
- **It describes intent, not SQL.** `IEquipmentReadService.SearchAsync(page, pageSize, category, status, ct)`
  beats `Query(Expression<Func<EquipmentAsset, bool>>)`. The second is a database in a trench coat.

---

## 5. Step 3 — Write the slice

Now the file. Three parts — this is the real `CreateEquipment.cs`.

### The request

```csharp
public sealed record Command(string Name, EquipmentCategory Category, string AssetTag)
    : IRequest<Guid>, IEquipmentCommand, IAuditableRequest;
```

A `record`, because a request is a value. Then the markers — the whole cross-cutting story
in one line:

| Marker | Effect | When to include it |
|---|---|---|
| `IRequest<T>` | Dispatchable; returns `T` | Always |
| `I<Module>Command` | Runs in a transaction on **this module's** database | Every write |
| `IAuditableRequest` | Recorded to the audit trail | Writes a person could be asked about |

Return the smallest useful thing — an id for a create, a small `Result` record where the
caller genuinely needs more:

```csharp
public sealed record Result(bool Ready, string? FailureReason);
```

### The validator

Optional. It handles **request-shaped** constraints — the things that make a request
malformed rather than a business operation invalid:

```csharp
public sealed class Validator : AbstractValidator<Command>
{
    public Validator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.Category).IsInEnum();
        RuleFor(command => command.AssetTag).NotEmpty().MaximumLength(50);
    }
}
```

**Validator or domain?** Both, sometimes — and that is not duplication, it is defence in
depth with different jobs. The validator gives the caller a clean `400` listing every
problem at once. The domain guarantees the rule holds no matter which code path ran. If you
can only have one, have the domain one.

Skip the validator entirely when the domain already covers it and there is no message worth
improving. `DeleteEquipment` has no validator — there's nothing to validate beyond "does this
id exist", which is a not-found, not a validation error.

### The handler

```csharp
public sealed class Handler : IRequestHandler<Command, Guid>
{
    private readonly IEquipmentRepository _equipment;
    private readonly IRealtimeDispatch _realtime;

    public Handler(IEquipmentRepository equipment, IRealtimeDispatch realtime)
    {
        _equipment = equipment;
        _realtime = realtime;
    }

    public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
    {
        var asset = EquipmentAsset.Create(command.Name, command.Category, command.AssetTag);
        await _equipment.AddAsync(asset, cancellationToken);

        _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentCreated", new
        {
            id = asset.Id,
            name = asset.Name,
            category = asset.Category.ToString(),
            assetTag = asset.AssetTag,
            status = asset.Status.ToString(),
        }));

        return asset.Id;
    }
}
```

Four things it does **not** do, all of them deliberate:

- **No `SaveChanges`.** `AddAsync` stages; the transaction behaviour commits at the end of
  the request. That is what makes a handler touching three aggregates still one atomic write.
- **No business decisions.** `EquipmentAsset.Create` decides what is valid. The handler moves
  things around.
- **No try/catch.** A `DomainException` becomes a `400` via the host's global handler; a
  `ValidationException` likewise. Catching them here would only make the response worse.
- **No delivery logic for the realtime event.** `Publish` just buffers it. Whether it reaches
  a connected client, and only *after* this transaction actually commits, is the pipeline's
  job — [chapter 12](#12-what-happens-around-your-handler).

You do not register the handler. `AddHandlersFromAssembly` in the module's DI scans for
`IRequestHandler<,>` and picks it up.

---

## 6. Step 4 — Implement persistence

In Infrastructure, where EF is allowed:

```csharp
public async Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken) =>
    await _db.Equipment.AddAsync(asset, cancellationToken);
```

Stage only — no `SaveChanges`, same reason as the handler.

If you added a new entity, it also needs an `IEntityTypeConfiguration` in
`Persistence/EntityConfigurations/` and a `DbSet` on the context. The configuration is where
column lengths, enum-to-string conversions and indexes get declared — keeping all of that out
of the domain class:

```csharp
internal sealed class EquipmentAssetConfiguration : IEntityTypeConfiguration<EquipmentAsset>
{
    public void Configure(EntityTypeBuilder<EquipmentAsset> builder)
    {
        builder.ToTable("EquipmentAssets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Name).IsRequired().HasMaxLength(200);
        builder.Property(asset => asset.Category).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(asset => asset.AssetTag).IsRequired().HasMaxLength(50);
        builder.HasIndex(asset => asset.AssetTag).IsUnique();
    }
}
```

(An entity with an owned value object would add `builder.OwnsOne(thing => thing.SomeValueObject)`
here — neither module in this codebase currently has one to show.)

New interface? Register it in the module's `DependencyInjection.cs`:

```csharp
services.AddScoped<IEquipmentRepository, EfEquipmentRepository>();
```

---

## 7. Step 5 — Expose it

```csharp
equipment.MapPost("/equipment", async (
    CreateEquipment.Command command,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var id = await sender.Send(command, cancellationToken);
    return Results.Created($"/equipment/{id}", new { id });
})
.WithName("CreateEquipment")
.WithSummary("Add a piece of hardware to inventory. Pushes an EquipmentCreated event to connected clients.")
.RequireAuthorization();
```

Bind, send, shape. That is the entire job of an endpoint.

Note that the **command binds directly from the body**. There is no separate request DTO
that gets mapped onto the command — for most endpoints that mapping layer earns nothing. Add
one only when the wire shape genuinely differs from the command, which usually happens when
part of the command comes from the route:

```csharp
equipment.MapPut("/equipment/{equipmentId:guid}", async (
    Guid equipmentId,
    UpdateEquipmentRequest request,
    ISender sender,
    CancellationToken cancellationToken) =>
{
    var updated = await sender.Send(
        new UpdateEquipment.Command(equipmentId, request.Name, request.Category, request.AssetTag),
        cancellationToken);
    return updated ? Results.Ok() : Results.NotFound();
});
```

`equipmentId` comes from the route; `request` supplies the rest. `UpdateEquipmentRequest` is
a tiny record that exists only because the id can't come from the body twice.

Conventions to match:

| | |
|---|---|
| `.WithName(...)` | Stable operation id; used by generated clients |
| `.WithSummary(...)` | What the API explorer shows. Write it for a stranger |
| `.RequireAuthorization()` | On every write |
| Status codes | `201 Created` with a location for creates, `204 No Content` for deletes, `200 OK` otherwise |

---

## 8. Step 6 — Migrate

Only if you changed the EF model — a new entity, a new property, a changed constraint.

```bash
dotnet ef migrations add AddEquipmentAssetSerialNumber \
  --project src/Modules/Equipment/Equipment.Infrastructure \
  --startup-project src/Api/CleanArch.Api \
  --context EquipmentDbContext \
  --output-dir Persistence/Migrations
```

`--context` is required — the host references more than one `DbContext` (Equipment,
Onboarding, and the API-key store) and EF will not guess.

**Read the generated migration before you run it.** EF is good but not clairvoyant: a
rename often generates as a drop-and-add, which is data loss wearing a helpful face. Check
`Up()` says what you meant.

In Development the app applies migrations on startup, so restarting is enough.

---

## 9. Step 7 — Test it

Two tests, neither needing a database.

**The rule**, straight against the domain:

```csharp
[Fact]
public void Create_with_invalid_input_throws() =>
    Assert.Throws<DomainException>(() =>
        EquipmentAsset.Create("", EquipmentCategory.Laptop, "LAP-001"));
```

**The use case**, with a fake instead of a repository:

```csharp
[Fact]
public async Task Creates_the_asset_and_publishes_an_EquipmentCreated_event()
{
    var repository = new FakeEquipmentRepository();
    var realtime = new FakeRealtimeDispatch();
    var handler = new CreateEquipment.Handler(repository, realtime);

    var id = await handler.Handle(
        new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

    var added = Assert.Single(repository.Added);
    Assert.Equal(id, added.Id);
}
```

The fakes live in `tests/CleanArch.UnitTests/EquipmentFakes.cs` and `OnboardingFakes.cs` —
simple in-memory dictionaries, not mocking-framework setups. That is a deliberate choice: a
fake you can read tells you what the test assumes; six lines of mock configuration tell you
what the mocking library's API looks like.

Run them:

```bash
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj
```

There is also `tests/CleanArch.Api.IntegrationTests/` for things you genuinely cannot check
in isolation — the wiring, the auth pipeline, an end-to-end round trip. Use it for those,
not as your default. [Guide 80](80-testing.md) covers the split in full.

---

## 10. Read features — the shortcut

No aggregate, no repository, no transaction. A `Query`, a response record, and a handler
that delegates to a read service. This is the real `GetOnboardingRequest`:

```csharp
public static class GetOnboardingRequest
{
    public sealed record Query(Guid OnboardingRequestId) : IRequest<Response?>;

    public sealed record Response(
        Guid Id,
        string EmployeeName,
        DateOnly StartDate,
        string RequiredEquipmentCategory,
        string RequiredLicenceType,
        string RequiredAccessLevel,
        string Status,
        string? FailureReason,
        string EquipmentStepStatus,
        Guid? EquipmentId,
        string LicenceStepStatus,
        Guid? LicenceId,
        string AccessStepStatus,
        Guid? AccessId);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IOnboardingReadService _reads;
        public Handler(IOnboardingReadService reads) => _reads = reads;

        public Task<Response?> Handle(Query query, CancellationToken cancellationToken) =>
            _reads.GetAsync(query.OnboardingRequestId, cancellationToken);
    }
}
```

Note the `Query` carries **no markers** beyond `IRequest<>` — no command marker, no audit
marker. So it skips the transaction and the audit trail automatically, by simply not opting
in. (`GetOnboardingSummary`'s query *does* carry `IAuditableRead` — it exposes one employee's
onboarding status, which is exactly the sensitive-read case [guide 40](40-auditing.md#6-step-2--audit-a-read)
describes.)

The implementation, in `Infrastructure/Reads/`, projects rather than loads:

```csharp
public Task<GetOnboardingRequest.Response?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken) =>
    _db.Requests
        .AsNoTracking()
        .Where(request => request.Id == onboardingRequestId)
        .Select(request => new GetOnboardingRequest.Response(
            request.Id,
            request.EmployeeName,
            request.StartDate,
            request.RequiredEquipmentCategory,
            request.RequiredLicenceType,
            request.RequiredAccessLevel,
            request.Status.ToString(),
            request.FailureReason,
            request.EquipmentStepStatus.ToString(),
            request.EquipmentId,
            request.LicenceStepStatus.ToString(),
            request.LicenceId,
            request.AccessStepStatus.ToString(),
            request.AccessId))
        .FirstOrDefaultAsync(cancellationToken);
```

`AsNoTracking()` because nothing will be modified; `.Select(...)` so the SQL fetches only
these columns.

### One response record per endpoint

This is the convention worth defending. `GetOnboardingRequest` and `GetOnboardingSummary`
read the very same row and have **completely separate** response records — one raw, one
derived. `GetOnboardingSummary.Response` carries fields — `ReadinessPercentage`,
`MissingRequirements`, `EstimatedCost` — that don't exist as columns anywhere; they're
computed on the way out (that derivation is worth its own read, in the summary endpoint's own
code).

The alternative — one `OnboardingRequestDto` with every field either module could ever want,
most of them null on any given call — couples every endpoint to every other. Add a field for
one consumer and you have changed the contract for all of them; remove one and you don't know
who breaks.

Nested DTOs remain reusable building blocks when a response genuinely has them; a response
that's all scalars, like these two, simply doesn't need any.

---

## 11. Paged lists

House convention: list reads are **`POST /…/search`** with paging and filters in the body,
returning `PagedResult<T>`. A POST for a read looks odd for about a day, and then you notice
the URLs stayed clean instead of accumulating a dozen query-string parameters.

```csharp
public static class SearchEquipment
{
    public sealed record Query(int Page = 1, int PageSize = 20, string? Category = null, string? Status = null)
        : PagedRequest(Page, PageSize), IRequest<PagedResult<EquipmentListItem>>;

    public sealed record EquipmentListItem(
        Guid Id, string Name, string Category, string AssetTag, string Status);

    public sealed class Validator : AbstractValidator<Query>
    {
        public Validator()
        {
            RuleFor(query => query.Page).GreaterThanOrEqualTo(1);

            // Cap the page size — never let a caller ask for an unbounded page.
            RuleFor(query => query.PageSize).InclusiveBetween(1, 100);
        }
    }
}
```

Three things to copy:

- **Derive from `PagedRequest`** for `Page`/`PageSize` defaults.
- **Cap `PageSize`.** An uncapped page size is a denial-of-service endpoint with good
  intentions.
- **Give the list its own item shape** — `EquipmentListItem`, not `GetEquipment.Response`
  reused wholesale. A list view rarely needs every field a detail view does; forcing reuse
  just means the detail response grows nullable fields nobody asked for.

`PagedResult<T>` carries `Items`, `Page`, `PageSize`, `TotalCount` and computes
`TotalPages`, so the client can navigate without guessing.

---

## 12. What happens around your handler

Your handler is short because four things wrap it. Worth knowing, because it explains
behaviour you did not write:

```
   ISender.Send(command)
     │
     ├─ LoggingBehavior          every request
     │   ├─ AuditBehavior        only IAuditableRequest
     │   │   ├─ ValidationBehavior   only if a validator exists
     │   │   │   ├─ TransactionBehavior   only this module's commands
     │   │   │   │   └─ YOUR HANDLER
```

Registration order is outermost-first, and the order here is not arbitrary:

**Audit sits outside validation**, so a command rejected by its validator is *still* audited
— with `succeeded: false`. An audit trail that only records successes cannot answer "what
did they try to do?"

**Transaction sits innermost**, so it wraps the least possible work. It begins a transaction,
runs your handler, calls `SaveChanges`, commits — and rolls back on any exception. It also
checks for an existing transaction first and defers to it, so a nested dispatch doesn't open
a second one.

**Behaviours match by type constraint.** `AuditBehavior<TRequest, TResponse>` is constrained
`where TRequest : IAuditableRequest`, so a request without the marker never enters it — not
"enters and returns early", genuinely never resolved. That is why queries cost nothing.

**Realtime dispatch sits outside the transaction**, not shown above because it's registered
at the host level rather than per-module — see `RealtimeDispatchBehavior` in
`BuildingBlocks.RealTime`. It flushes whatever your handler `Publish`ed only *after* the
transaction commits, which is why a realtime event is never sent for a write that then rolled
back.

The dispatcher itself is in `src/BuildingBlocks/Messaging/Sender.cs` — about forty lines. It
resolves the handler for the request's runtime type, folds the registered behaviours around
it, and invokes the chain. Worth reading once; it demystifies the whole thing.

---

## 13. The checklist

For a **write** feature:

- [ ] The rule is in the domain, enforced in a factory or method that throws `DomainException`
- [ ] The domain object still cannot be constructed in an invalid state
- [ ] Repository interface updated in `Application/Abstractions/`, returning domain types
- [ ] One file: `Command` + optional `Validator` + `Handler`
- [ ] `Command` carries `IRequest<T>`, `I<Module>Command`, and `IAuditableRequest` if it should be audited
- [ ] The handler orchestrates — no rules, no `SaveChanges`, no try/catch
- [ ] Repository method implemented in Infrastructure; stages only
- [ ] New interface registered in the module's `DependencyInjection.cs`
- [ ] Endpoint mapped, with `.WithName`, `.WithSummary`, `.RequireAuthorization()`
- [ ] Migration added **and read** if the model changed
- [ ] Domain test + handler test with a fake
- [ ] `dotnet build` clean — warnings are errors here

For a **read** feature:

- [ ] `Query` carries `IRequest<T>` and **nothing else** (or `IAuditableRead` if the data is sensitive)
- [ ] Its own response record — not a shared DTO
- [ ] Read-service method projecting with `AsNoTracking()` and `.Select(...)`
- [ ] List endpoints: `POST /…/search`, derive from `PagedRequest`, **cap the page size**

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `201 Created` but no row in the database | Handler staged and nothing committed | The command needs the module's marker, and the module needs its `TransactionBehavior` |
| `No service for type IRequestHandler<...>` | Handler not found by the assembly scan | Check it's `public`, non-abstract, and in the module's Application assembly |
| Validator never runs | No validator registered, or wrong request type | `AddValidatorsFromAssembly` in the module; `AbstractValidator<Command>` must name the exact type |
| Rule bypassed by another code path | The rule is in the validator, not the domain | Move it into the aggregate |
| `400` you didn't write | `DomainException` or `ValidationException`, translated by the host | Working as intended |
| Response has nulls everywhere | A shared DTO serving several endpoints | One response record per endpoint |
| Read query is slow | Loading the aggregate rather than projecting | `AsNoTracking()` + `.Select(...)` in a read service |
| `More than one DbContext was found` | `dotnet ef` can't guess | Add `--context <Module>DbContext` |
| Migration wants to drop a column you renamed | EF sees drop + add | Edit the migration to a rename before running it |
| Feature audited but `changes` is empty | The module's `DbContext` has no audit interceptor | [Guide 40, chapter 8](40-auditing.md#8-step-4--capture-before-and-after) |
| Realtime event never arrives at a client | Published before the transaction rolled back, or nobody joined the group | Confirm the write actually committed; a client must call `JoinGroup("equipment")` on the hub first |

---

## 15. Cheat sheet

### The write slice

```csharp
public static class DoSomething
{
    public sealed record Command(Guid Id, string Value)
        : IRequest<Guid>, IMyModuleCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator() => RuleFor(c => c.Value).NotEmpty().MaximumLength(100);
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IThingRepository _things;
        public Handler(IThingRepository things) => _things = things;

        public async Task<Guid> Handle(Command command, CancellationToken ct)
        {
            var thing = await _things.GetAsync(command.Id, ct)
                ?? throw new DomainException($"No thing with id '{command.Id}'.");
            thing.DoSomething(command.Value);          // the domain decides
            return thing.Id;                            // no SaveChanges
        }
    }
}
```

### The read slice

```csharp
public static class GetThing
{
    public sealed record Query(Guid Id) : IRequest<Response?>;      // no other markers

    public sealed record Response(Guid Id, string Name, string Status);

    public sealed class Handler : IRequestHandler<Query, Response?>
    {
        private readonly IThingReadService _reads;
        public Handler(IThingReadService reads) => _reads = reads;

        public Task<Response?> Handle(Query query, CancellationToken ct) =>
            _reads.GetAsync(query.Id, ct);
    }
}
```

### Markers

| Marker | Put it on |
|---|---|
| `IRequest<T>` | Everything |
| `I<Module>Command` | Every write |
| `IAuditableRequest` | Writes worth recording |
| *(none of the above)* | Every read |

### Commands

```bash
dotnet build
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj
dotnet run --project src/Api/CleanArch.Api      # migrations apply in Development

dotnet ef migrations add <Name> \
  --project src/Modules/<Module>/<Module>.Infrastructure \
  --startup-project src/Api/CleanArch.Api \
  --context <Module>DbContext \
  --output-dir Persistence/Migrations
```

---

## 16. Glossary

| Term | Meaning |
|---|---|
| **Aggregate** | A domain object owning its data, guarding the rules spanning its own state |
| **Command** | A request that changes state. Carries the module's command marker |
| **CQRS-lite** | Writes through aggregates and repositories; reads projected straight to the response |
| **Fake** | A hand-written in-memory stand-in for an interface, used in tests |
| **Handler** | The code that executes one use case |
| **Marker interface** | An empty interface used as a switch — audit, transaction |
| **Pipeline behaviour** | Cross-cutting code wrapped around every matching request |
| **Projection** | Building a response shape directly in the query, rather than loading and mapping |
| **Query** | A request that only reads. Carries no command marker (may carry `IAuditableRead`) |
| **Read service** | The read-side counterpart to a repository; returns response shapes, not aggregates |
| **Repository** | An interface describing the persistence a use case needs, in domain terms |
| **Response record** | The shape one endpoint returns. One per endpoint |
| **Unit of work** | Committing everything a request changed as one transaction |
| **Validator** | FluentValidation rules for request-shaped constraints, run before the handler |
| **Vertical slice** | One file holding a feature's request, validator and handler |

---

## Where to go next

- **[Adding a new module](30-add-a-module.md)** — when the feature doesn't belong in any
  existing module.
- **[Talking across modules](60-talking-across-modules.md)** — when it needs data, or a
  write, from another module.
- **[Auditing](40-auditing.md)** — what `IAuditableRequest` actually gets you.
