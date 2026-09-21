# Adding a New Module

**Who this is for:** someone who needs to add a new area of functionality to a modular
monolith — one that owns its own data and can be developed without stepping on anyone
else's code.

**What you'll be able to do by the end:** create a module from nothing, wire it into the
host, give it its own database and migrations, expose it over HTTP, and connect it to the
modules that already exist — while keeping the dependency rule intact.

**What you need first:** the project runs on your machine, and you have added a feature to
an existing module at least once. If not, do that first — a module is a container for
features, and it is hard to build the container before you know what goes in it.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What a module is](#1-what-a-module-is) | Decide whether you need one at all |
| 2 | [Why each module owns its database](#2-why-each-module-owns-its-database) | Understand the trade you are making |
| 3 | [The five projects](#3-the-five-projects) | The shape you are about to create |
| 4 | [Decisions to make before you type](#4-decisions-to-make-before-you-type) | Name, boundary, relationships |
| 5 | [Step 1 — Create the projects](#5-step-1--create-the-projects) | `dotnet new`, five times |
| 6 | [Step 2 — Point the references inward](#6-step-2--point-the-references-inward) | The step that *is* the architecture |
| 7 | [Step 3 — Model the domain](#7-step-3--model-the-domain) | The rules, with no framework in sight |
| 8 | [Step 4 — The Application layer](#8-step-4--the-application-layer) | Markers, abstractions, your first slice |
| 9 | [Step 5 — The Infrastructure layer](#9-step-5--the-infrastructure-layer) | DbContext, mappings, repositories |
| 10 | [Step 6 — The module's unit of work](#10-step-6--the-modules-unit-of-work) | One small class, easy to forget |
| 11 | [Step 7 — Register the module](#11-step-7--register-the-module) | The one public entry point |
| 12 | [Step 8 — Connection string and host wiring](#12-step-8--connection-string-and-host-wiring) | Four edits outside your module |
| 13 | [Step 9 — The first migration](#13-step-9--the-first-migration) | Create the database |
| 14 | [Step 10 — Expose it over HTTP](#14-step-10--expose-it-over-http) | Endpoints |
| 15 | [Step 11 — Tests](#15-step-11--tests) | Prove the rules without a database |
| 16 | [Connecting to other modules](#16-connecting-to-other-modules) | Contracts, the outbox, and the trap |
| 17 | [The checklist](#17-the-checklist) | Run this when doing it for real |
| 18 | [Troubleshooting](#18-troubleshooting) | Symptom, cause, fix |
| 19 | [Command cheat sheet](#19-command-cheat-sheet) | The commands, in one place |
| 20 | [Glossary](#20-glossary) | Every term used in this guide |

---

## 1. What a module is

A **module** is a self-contained slice of the system: its own domain model, its own
database, its own HTTP endpoints, deployed inside the same process as every other module.

That last part is what makes it a *modular monolith* rather than microservices. One
process, one deployment, one debugger — but internally partitioned so that `Onboarding`
code cannot reach into `Equipment`'s tables, even though both are running a metre apart.

### When to add one

Add a module when the new work has **its own vocabulary and its own reasons to change**.
The test that works in practice: could a different team own this, and would they mostly
argue with you about the *interface* rather than the internals? Then it is a module.

| Situation | What to do |
|---|---|
| A new endpoint on an existing concept | Add a feature to the existing module |
| A new concept that lives *inside* an existing area's rules | Add to the existing module's domain |
| A new area with its own lifecycle, own tables, own users | **Add a module** |
| A new process layered over an existing system of record | **Add a module** that references the other by id |
| Something you'll deploy separately one day | Add a module — it is the natural seam to split on later |

### When not to

Two modules that constantly need each other's data in the same transaction were never two
modules. If your first three features all need a cross-module write, the boundary is in
the wrong place — move it before you have migrations to unpick.

Also don't add one for data that's genuinely **shared, read-only, and small** — a lookup
list used by more than one module (office locations, currency codes, that kind of thing)
that nobody writes to through this app and that changes rarely enough to live behind a
long-lived cache. That isn't a bounded context — it has no vocabulary of its own and no
reason to change on its own schedule — so giving it a full five-project module (its own
`Domain`, its own write side) is ceremony with nothing behind it. This repo has one:
`SharedKernel/` (`Site` reference data, consumed directly by `Equipment`'s read side). See
[Talking Across Modules, chapter 2](60-talking-across-modules.md#2-two-sanctioned-routes)
for how it differs from a published contract.

> **The worked example in this guide** is `Equipment`. It's a good specimen because it does
> almost everything a module can: its own database, plain CRUD, real-time notifications, a
> cache-aside read, a published contract that another module (`Onboarding`) calls into, and
> a service that never touches the database at all (the file-backed catalogue). The one
> thing it doesn't need is an outbox — nothing calls *out* of Equipment asynchronously. Open
> `src/Modules/Equipment/` alongside this guide; where the outbox specifically comes up in
> [chapter 16](#16-connecting-to-other-modules), we'll look at `Onboarding` instead, since
> that's the module that actually has one.

---

## 2. Why each module owns its database

Each module has its own database file, its own `DbContext`, its own migration history.
`Equipment` cannot see `onboarding.db`. That is the point, and it is worth being clear about
what you gain and what it costs, because the cost is real.

**What you gain.** You can change a module's schema without a company-wide meeting.
Nobody has written a report that joins your table to theirs, because they *can't*. The
module's public surface is the interface it publishes, not its tables — so the tables stay
yours to refactor. And the day one module needs to become its own service, the seam
already exists.

**What it costs.** Two things, and they are not small:

1. **No cross-module joins.** You cannot write one SQL query spanning two modules. You
   fetch from each and compose in the application layer. Almost always fine; occasionally
   a real performance conversation.
2. **No cross-module transactions.** A database transaction lives inside one database.
   "Write here *and* there, atomically" is not available to you. You get eventual
   consistency via the outbox instead — see [chapter 16](#16-connecting-to-other-modules).

**Why this matters:** most people meet cost #2 for the first time halfway through building
a feature, and try to solve it by reaching into the other module's `DbContext`. That works,
and it silently deletes the boundary you built the module for. Knowing the constraint up
front means you design around it instead of through it.

---

## 3. The five projects

A module is five projects. Each layer is a separate project *specifically so that the
compiler enforces the dependency rule* — you cannot accidentally use EF Core in the domain
if the domain project doesn't reference it.

```
   Modules/Equipment/
   │
   │   ┌─ inner: knows nothing about frameworks ────────────────┐
   ├── Equipment.Domain              the rules. Zero references. │
   ├── Equipment.Contracts           what other modules may call │
   ├── Equipment.Application         use cases + interfaces      │
   │   └────────────────────────────────────────────────────────┘
   │   ┌─ outer: frameworks live here ───────────────────────────┐
   ├── Equipment.Infrastructure      EF Core, repositories       │
   └── Equipment.Presentation        HTTP endpoints              │
       └────────────────────────────────────────────────────────┘
```

| Project | Depends on | What lives here | What must never appear |
|---|---|---|---|
| **Domain** | *nothing* | Entities, value objects, enums, invariants | Any framework at all |
| **Contracts** | *nothing* | Interfaces + DTOs other modules may use | Anything internal |
| **Application** | Domain | Commands, queries, handlers, repository *interfaces* | `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore` |
| **Infrastructure** | Application, Contracts | `DbContext`, repository *implementations*, mappings | HTTP concerns |
| **Presentation** | Application | Minimal-API endpoints | Business logic |

Two of those projects have genuinely empty project files, which is the clearest possible
statement of the rule:

```xml
<!-- src/Modules/Equipment/Equipment.Domain/Equipment.Domain.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

</Project>
```

No packages. No project references. If you ever find yourself adding one here, stop — the
answer is an interface in the Application layer, implemented in Infrastructure.

> **Why Contracts is separate from Application.** Another module needs to call into yours,
> but must not see your use cases, your repositories or your domain objects. `Contracts`
> has zero dependencies precisely so anyone can reference it without dragging your
> internals along. It is the only project other modules are allowed to reference.

---

## 4. Decisions to make before you type

Four things, and getting them wrong is expensive later.

**1. The name.** It becomes the namespace, five project names, the database file, the
connection-string key and the route prefix. Singular or plural, pick one and match the
existing modules.

> Avoid names that collide with **the module's own namespace**, not just framework types.
> This module's aggregate is called `EquipmentAsset`, not `Equipment` — because a class
> named identically to its root namespace makes every bare reference to it ambiguous
> throughout the module. Inside `namespace Equipment.Application`, an unqualified
> `Equipment` would try to resolve against the enclosing namespace `Equipment` before it
> ever considers the type `Equipment.Domain.Equipment`, and the compiler tells you a
> namespace is being used like a type. This was a real decision made while building this
> module, not a hypothetical.

**2. The boundary.** Write down, in one sentence, what this module is responsible for. If
the sentence needs an "and", you may have two modules.

**3. What it needs from other modules.** For each: is it a *read* (or a simple synchronous
action), or does it need to survive a crash mid-flight? The first is a published contract.
The second is the outbox and a saga. Knowing which you need changes what you build.

**4. What other modules will need from it.** This is what goes in `Contracts`. Start
empty; add only when a real consumer appears.

---

## 5. Step 1 — Create the projects

From the repository root:

```bash
cd src/Modules
mkdir Equipment && cd Equipment

dotnet new classlib -o Equipment.Domain
dotnet new classlib -o Equipment.Contracts
dotnet new classlib -o Equipment.Application
dotnet new classlib -o Equipment.Infrastructure
dotnet new classlib -o Equipment.Presentation
```

Delete the `Class1.cs` that `dotnet new` puts in each one.

Then add them to the solution. This repository uses the newer XML solution format
(`CleanArchitecture.slnx`), so they go in a folder of their own:

```xml
<!-- CleanArchitecture.slnx -->
<Folder Name="/src/Modules/Equipment/">
  <Project Path="src/Modules/Equipment/Equipment.Application/Equipment.Application.csproj" />
  <Project Path="src/Modules/Equipment/Equipment.Contracts/Equipment.Contracts.csproj" />
  <Project Path="src/Modules/Equipment/Equipment.Domain/Equipment.Domain.csproj" />
  <Project Path="src/Modules/Equipment/Equipment.Infrastructure/Equipment.Infrastructure.csproj" />
  <Project Path="src/Modules/Equipment/Equipment.Presentation/Equipment.Presentation.csproj" />
</Folder>
```

**You do not set a target framework, nullable, or analyzer settings.** Those come from
`Directory.Build.props` at the repository root, which every project under it inherits.
That also means your new projects are subject to `TreatWarningsAsErrors` from their first
build — which is the intent, though it will surprise you the first time an unused `using`
fails the build.

---

## 6. Step 2 — Point the references inward

This step *is* the architecture. Everything else is detail.

```bash
# Domain and Contracts: nothing. Leave them alone.

# Application depends on Domain (and the shared building blocks)
dotnet add Equipment.Application reference Equipment.Domain
dotnet add Equipment.Application reference ../../BuildingBlocks/BuildingBlocks.csproj

# Infrastructure depends on Application + this module's Contracts
dotnet add Equipment.Infrastructure reference Equipment.Application
dotnet add Equipment.Infrastructure reference Equipment.Contracts
dotnet add Equipment.Infrastructure reference ../../BuildingBlocks.Persistence/BuildingBlocks.Persistence.csproj

# Presentation depends on Application only
dotnet add Equipment.Presentation reference Equipment.Application
```

Then the packages each outer layer needs:

```bash
dotnet add Equipment.Application    package FluentValidation.DependencyInjectionExtensions
dotnet add Equipment.Application    package Microsoft.Extensions.DependencyInjection.Abstractions

dotnet add Equipment.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add Equipment.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add Equipment.Infrastructure package Microsoft.Extensions.Caching.Hybrid   # only if you're adding a cache-aside read

dotnet add Equipment.Presentation   package Asp.Versioning.Http
```

> **No version numbers.** This repository uses Central Package Management: every version
> lives in `Directory.Packages.props` and the `.csproj` files carry bare
> `<PackageReference Include="…" />`. If your package isn't in that file yet, add a
> `<PackageVersion>` entry there — otherwise the build fails with NU1010.

Presentation also needs the ASP.NET Core framework, which is a `FrameworkReference`
rather than a package:

```xml
<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

And `Microsoft.EntityFrameworkCore.Design` should be marked as a design-time-only
dependency so it doesn't flow to anything referencing Infrastructure:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Design">
  <PrivateAssets>all</PrivateAssets>
  <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
</PackageReference>
```

**Sanity check.** Build now, before writing any code:

```bash
dotnet build
```

An empty module that builds is a module whose references are right. It is much easier to
diagnose a reference problem now than tangled up with your first compile error.

---

## 7. Step 3 — Model the domain

Start here, always. Not with the database, not with the endpoint — with the rules.

The domain layer holds objects that **cannot exist in an invalid state**. The pattern in
this repository: private constructor, static factory that validates, private setters,
behaviour as methods. From `src/Modules/Equipment/Equipment.Domain/EquipmentAsset.cs`'s
shape (trimmed — the real file also has `Update`, `Release`, and two more fields):

```csharp
namespace Equipment.Domain;

public sealed class EquipmentAsset
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public EquipmentStatus Status { get; private set; }

    private EquipmentAsset() { }                // EF needs a parameterless ctor; nobody else may use it

    private EquipmentAsset(Guid id, string name, DateTime createdOnUtc)
    {
        Id = id;
        Name = name;
        Status = EquipmentStatus.Available;
    }

    public static EquipmentAsset Create(string name, EquipmentCategory category, string assetTag)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Name is required.");

        return new EquipmentAsset(Guid.NewGuid(), name.Trim(), DateTime.UtcNow);
    }

    public void Reserve(Guid onboardingRequestId)
    {
        if (Status != EquipmentStatus.Available)
            throw new DomainException($"Equipment '{Id}' is not available (status: {Status}).");

        Status = EquipmentStatus.Reserved;
    }
}
```

Each module gets **its own `DomainException`** in its own namespace. That looks like
duplication and isn't: it keeps the Domain project at zero references, and a module's
exceptions are part of its own vocabulary. The host translates any of them into a
`400 Problem Details` response centrally.

**Why this matters:** validation that lives in a handler or an endpoint gets bypassed the
first time someone adds a second way to create the object. Validation inside the factory
cannot be bypassed, because there is no other way in.

---

## 8. Step 4 — The Application layer

Three things go here: the command marker, the abstractions, and the use cases.

### 8.1 The command marker

One empty interface, and it does more work than its size suggests:

```csharp
// src/Modules/Equipment/Equipment.Application/IEquipmentCommand.cs
namespace Equipment.Application;

/// <summary>
/// Marks a request as an Equipment-module write that must run inside an EquipmentDbContext
/// transaction. The module's transaction behavior wraps only requests carrying this marker, so
/// queries — and other modules' requests — are left untouched.
/// </summary>
public interface IEquipmentCommand;
```

Every module has one. It is how the module's transaction behaviour knows which requests
are *its* writes: an `Onboarding` command flowing through the pipeline must not open a
transaction on *your* database.

### 8.2 The abstractions

Interfaces describing the persistence your use cases need, in
`Equipment.Application/Abstractions/`. They speak in domain types and know nothing about
EF:

```csharp
public interface IEquipmentRepository
{
    Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken);
    Task<EquipmentAsset?> GetAsync(Guid equipmentId, CancellationToken cancellationToken);
    void Remove(EquipmentAsset asset);
}
```

Reads get their own interface — this module actually has two. `IEquipmentReadService` is
the plain paged search; `IEquipmentDirectory` is the single-item lookup that gets a caching
decorator in front of it in Infrastructure ([chapter 9](#9-step-5--the-infrastructure-layer)).
Neither loads the full aggregate to then throw most of it away; they project straight to
the response shape the endpoint needs.

### 8.3 The first use case

One file per use case: a static class holding the `Command`, an optional `Validator`, and
the `Handler`. This is `Equipment.Application/Inventory/CreateEquipment.cs`, complete —
[guide 20](20-add-a-feature.md) walks through writing a slice like this line by line, so
here it's just the finished shape:

```csharp
public static class CreateEquipment
{
    public sealed record Command(string Name, EquipmentCategory Category, string AssetTag)
        : IRequest<Guid>, IEquipmentCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
            RuleFor(command => command.Category).IsInEnum();
            RuleFor(command => command.AssetTag).NotEmpty().MaximumLength(50);
        }
    }

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

            _realtime.Publish(RealtimeGroups.Equipment(), new RealtimeEvent("EquipmentCreated", new { id = asset.Id }));

            return asset.Id;
        }
    }
}
```

Read the three marker interfaces on that `Command` — they are the whole cross-cutting story:

| Marker | Effect |
|---|---|
| `IRequest<Guid>` | The mediator can dispatch it, and it returns a `Guid` |
| `IEquipmentCommand` | Run me inside a transaction on **this module's** database |
| `IAuditableRequest` | Record me to the audit trail ([guide 40](40-auditing.md)) |

Note what the handler does **not** do: it never calls `SaveChanges`. `AddAsync` only
stages the entity; the transaction behaviour commits everything at the end. That is the
unit-of-work pattern, and it is why a handler that touches three aggregates still results
in one atomic write.

### 8.4 The DI extension

```csharp
// src/Modules/Equipment/Equipment.Application/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddEquipmentApplication(this IServiceCollection services)
    {
        services.AddHandlersFromAssembly(typeof(DependencyInjection).Assembly);
        services.AddValidatorsFromAssembly(typeof(DependencyInjection).Assembly);
        return services;
    }
}
```

Two assembly scans, and every handler and validator you ever add registers itself. You
will not come back to this file.

---

## 9. Step 5 — The Infrastructure layer

Now frameworks are allowed.

### 9.1 The DbContext

```csharp
// src/Modules/Equipment/Equipment.Infrastructure/Persistence/EquipmentDbContext.cs
public sealed class EquipmentDbContext : DbContext
{
    public EquipmentDbContext(DbContextOptions<EquipmentDbContext> options) : base(options) { }

    public DbSet<EquipmentAsset> Equipment => Set<EquipmentAsset>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(EquipmentDbContext).Assembly);
    }
}
```

(A module that publishes outbox messages adds a `DbSet<OutboxMessage> Outbox` and calls
`modelBuilder.ApplyOutboxConfiguration()` here too — `Onboarding`'s context does exactly
that. This one doesn't need it; see [chapter 16](#16-connecting-to-other-modules) for why.)

`ApplyConfigurationsFromAssembly` means you never edit this file again either — each
entity's mapping lives in its own class.

### 9.2 Entity configurations

One per entity, in `Persistence/EntityConfigurations/`. This is where the domain gets
mapped to tables **without the domain knowing**:

```csharp
internal sealed class EquipmentAssetConfiguration : IEntityTypeConfiguration<EquipmentAsset>
{
    public void Configure(EntityTypeBuilder<EquipmentAsset> builder)
    {
        builder.ToTable("EquipmentAssets");
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.Name).IsRequired().HasMaxLength(200);
        builder.Property(asset => asset.AssetTag).IsRequired().HasMaxLength(50);
        builder.HasIndex(asset => asset.AssetTag).IsUnique();
    }
}
```

Column lengths, owned value objects, enum-to-string conversions and child tables all live
here. The domain class stays a plain C# object.

### 9.3 Repositories

Implement the Application's interfaces. They stage; they do not save:

```csharp
public async Task AddAsync(EquipmentAsset asset, CancellationToken cancellationToken) =>
    await _db.Equipment.AddAsync(asset, cancellationToken);
```

### 9.4 The design-time factory

EF's command-line tools need to construct your `DbContext` without booting the API. Without
this class, `dotnet ef` either fails or starts the whole host just to read a schema:

```csharp
internal sealed class EquipmentDbContextFactory : IDesignTimeDbContextFactory<EquipmentDbContext>
{
    public EquipmentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Equipment")
            ?? "Data Source=equipment-design.db";

        var options = new DbContextOptionsBuilder<EquipmentDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new EquipmentDbContext(options);
    }
}
```

The fallback is a throwaway local file, so a fresh clone can scaffold migrations with zero
setup. It never runs in production — it exists only to give the tooling something to
connect to.

---

## 10. Step 6 — The module's unit of work

Small, mandatory, and the thing people forget:

```csharp
// src/Modules/Equipment/Equipment.Infrastructure/Behaviors/TransactionBehavior.cs
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, EquipmentDbContext>
    where TRequest : IRequest<TResponse>, IEquipmentCommand
{
    public TransactionBehavior(EquipmentDbContext db) : base(db) { }
}
```

The whole implementation is inherited. All this class does is **bind two type parameters**:
this module's `DbContext` and this module's command marker. The generic constraint is what
makes the pipeline skip requests that aren't yours.

**Why this matters:** without it, your handlers stage entities that are never saved. Nothing
throws. The endpoint returns `201 Created` and the row is not there. It is a genuinely
confusing first bug, and it is always this file missing.

---

## 11. Step 7 — Register the module

One public method — the module's only entry point. This is the real, complete
`AddEquipmentModule` (it's a fuller module than the bare minimum — the caching lines are
only here because this module happens to have a cache-aside read; skip them if yours
doesn't):

```csharp
// src/Modules/Equipment/Equipment.Infrastructure/DependencyInjection.cs
public static IServiceCollection AddEquipmentModule(
    this IServiceCollection services, string connectionString)
{
    services.AddEquipmentApplication();

    // Audit change-tracking: capture before/after values of every write for the audit trail.
    services.AddAuditChangeTracking();
    services.AddDbContext<EquipmentDbContext>((sp, options) =>
        options.UseSqlite(connectionString).UseAuditChangeTracking(sp));

    services.AddScoped<IEquipmentRepository, EfEquipmentRepository>();

    // Cache-aside single lookup: EquipmentDirectory is the cache-miss fallthrough to the database.
    services.AddScoped<EquipmentDirectory>();
    services.AddScoped<IEquipmentDirectory>(provider => new CachingEquipmentDirectory(
        provider.GetRequiredService<EquipmentDirectory>(),
        provider.GetRequiredService<HybridCache>()));
    services.AddScoped<IEquipmentCacheInvalidator, EquipmentCacheInvalidator>();

    services.AddScoped<IEquipmentReadService, EquipmentReadService>();
    services.AddScoped<IEquipmentCatalogueReader, EquipmentCatalogueFileReader>();

    // Published contract: the Onboarding module calls this directly to reserve/release equipment.
    services.AddScoped<IEquipmentReservationService, EquipmentReservationService>();

    services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

    return services;
}
```

Note the `(sp, options)` overload of `AddDbContext` — the audit interceptor is scoped, so
it must be resolved from the provider rather than captured. The single-argument overload
compiles fine and then fails at runtime with a lifetime error.

Everything a module needs is registered here. The host calls one method and knows nothing
about your repositories.

---

## 12. Step 8 — Connection string and host wiring

Four small edits outside your module. This is the only time you touch shared files.

**1. `src/Api/CleanArch.Api/appsettings.json`** — add the connection string:

```json
"ConnectionStrings": {
  "ApiKeys": "Data Source=apikeys.db",
  "Equipment": "Data Source=equipment.db",
  "Onboarding": "Data Source=onboarding.db"
}
```

**2. `Program.cs`** — read it, and fail loudly if it is missing:

```csharp
var equipmentConnectionString = RequireConnectionString("Equipment");
```

`RequireConnectionString` throws at startup when the value is absent. That is deliberate:
a missing connection string should stop the process, not quietly create a stray SQLite
file next to the binary in production.

**3. `Program.cs`** — register the module:

```csharp
builder.Services
    .AddApiServices()
    // ...
    .AddEquipmentModule(equipmentConnectionString)
    .AddOnboardingModule(onboardingConnectionString);
```

**4. `WebApplicationExtensions.cs`** — migrate it on startup:

```csharp
await scope.ServiceProvider.GetRequiredService<EquipmentDbContext>().Database.MigrateAsync();
```

That runs automatically in Development, and outside Development only when
`Database:MigrateOnStartup` is set — because auto-migrating a production database from
inside the app is fine with one instance and a race condition with two.

`appsettings.Production.json` deliberately ships **empty** connection strings, so a missing
override fails fast rather than silently working against the wrong database. Add your key
there too, empty.

---

## 13. Step 9 — The first migration

From the repository root:

```bash
dotnet ef migrations add InitialCreate \
  --project src/Modules/Equipment/Equipment.Infrastructure \
  --startup-project src/Api/CleanArch.Api \
  --context EquipmentDbContext \
  --output-dir Persistence/Migrations
```

Every argument matters:

| Argument | Why |
|---|---|
| `--project` | Where the migration files are written — your Infrastructure project |
| `--startup-project` | Where the design-time factory is discovered from |
| `--context` | **Required.** The host references more than one `DbContext` (Equipment, Onboarding, and the API-key store); without this EF refuses to guess |
| `--output-dir` | Keeps migrations beside the `DbContext` instead of a top-level `Migrations/` folder |

Then run the app. In Development it applies migrations on startup and your database file
appears. Confirm it exists before moving on — an empty module with a broken migration is
easier to fix than a full one.

> `dotnet ef` is a one-time install if you don't have it:
> `dotnet tool install --global dotnet-ef`

---

## 14. Step 10 — Expose it over HTTP

Endpoints map a route to a command and send it through the mediator. They contain no
business logic — bind, send, shape the response:

```csharp
public static class EquipmentEndpoints
{
    public static IEndpointRouteBuilder MapEquipmentEndpoints(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var equipment = app.MapGroup("")
            .WithTags("Equipment")
            .WithApiVersionSet(versionSet)
            .HasApiVersion(new ApiVersion(1, 0));

        equipment.MapPost("/equipment", async (
            CreateEquipment.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/equipment/{id}", new { id });
        })
        .WithName("CreateEquipment")
        .WithSummary("Add a piece of hardware to inventory. Pushes an EquipmentCreated event to connected clients.")
        .RequireAuthorization();

        return app;
    }
}
```

Group your routes under one tag, so the API explorer stays navigable as the module grows.
Then map it in `Program.cs`:

```csharp
app.MapEquipmentEndpoints(versionSet);
```

`RequireAuthorization()` goes on writes. Reads in this repository are open; that is a
choice for a POC, not a recommendation.

Conventions worth matching, because they are consistent across every module here:

- **One response record per endpoint.** Not a shared fat DTO with half the fields null.
- **List endpoints are `POST /…/search`** with paging and filters in the body, returning
  `PagedResult<T>` — it keeps URLs clean and avoids a dozen query-string parameters.
- **`DomainException` becomes a `400`** automatically, via the host's global handler. Your
  endpoint does not catch it.

---

## 15. Step 11 — Tests

The payoff for the dependency rule is that most of your module is testable with no
database and no web server.

**Domain tests** call the aggregate directly:

```csharp
[Fact]
public void Create_with_invalid_input_throws() =>
    Assert.Throws<DomainException>(() => EquipmentAsset.Create("", EquipmentCategory.Laptop, "LAP-001"));
```

**Handler tests** use hand-written fakes of the Application interfaces — see
`tests/CleanArch.UnitTests/EquipmentFakes.cs` and `OnboardingFakes.cs` for the existing ones
(simple in-memory dictionaries, not mocking-framework setups):

```csharp
[Fact]
public async Task Creates_the_asset_and_publishes_an_EquipmentCreated_event()
{
    var repository = new FakeEquipmentRepository();
    var realtime = new FakeRealtimeDispatch();
    var handler = new CreateEquipment.Handler(repository, realtime);

    var id = await handler.Handle(
        new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

    Assert.Single(repository.Added);
}
```

There is also `tests/CleanArch.Api.IntegrationTests/` for tests that need real EF Core and a
real database — use it for the wiring you cannot check in isolation, not as your default.
[Guide 80](80-testing.md) covers the split, and what this repository's integration tests
actually look like, in full.

---

## 16. Connecting to other modules

Two sanctioned ways. Never a third.

### Synchronous calls — a published contract

The module that **owns** the data or the action publishes an interface in its `Contracts`
project; the caller references that project and depends on the interface. This covers both
plain reads and simple actions that either fully succeed or fully fail within one call —
`Onboarding` reserves equipment this way:

```csharp
// Equipment.Contracts/IEquipmentReservationService.cs — owned by Equipment, referenced by Onboarding
public interface IEquipmentReservationService
{
    Task<EquipmentReservationResult> ReserveAsync(
        Guid onboardingRequestId, string category, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken);
}
```

`Onboarding.Application.csproj` references `Equipment.Contracts.csproj` and nothing else of
theirs. The implementation lives in `Equipment.Infrastructure` and owns the database
access. Composition happens in the caller's application layer — never a cross-database
join.

### When a process needs to survive a crash — the outbox

A synchronous contract call is fine as long as the caller is still running to react to the
result. A multi-step process — reserve equipment, allocate a licence, provision access,
compensating whichever of those already succeeded if a later step fails — is not something
you want to keep only in memory, because a process restart between steps 2 and 3 would
strand it. `Onboarding` solves this by driving **itself** through its own outbox: each step,
on success, enqueues the message for the next step in the same transaction as its own
change, and a background processor delivers those messages one at a time — including after
a restart, because the queue lives in the database, not in memory:

```csharp
services.AddOutboxWriter<OnboardingDbContext>();
services.AddOutboxAdmin<OnboardingDbContext>();
services.AddOutboxProcessing<OnboardingDbContext, OnboardingOutboxDispatcher>();
```

You also add `DbSet<OutboxMessage> Outbox` and `ApplyOutboxConfiguration()` to your
`DbContext`, and a migration for the table. This is a **self-directed** saga: `Onboarding`'s
own dispatcher still calls into `Equipment`'s published contract synchronously from
Onboarding's side, exactly as in the previous section — the outbox's job is only to
guarantee the *next* step still happens even if the process dies right after this one
committed. [Guide 60](60-talking-across-modules.md) covers the full pattern, including
compensation and why a business failure (no stock left) and an unexpected exception (a bug)
are handled completely differently.

> ### The trap that will cost you an afternoon
>
> The shared `IOutbox` abstraction is a plain, non-keyed interface, so only **one** module
> in the whole process can safely call `AddOutboxWriter<TContext>()` and then inject bare
> `IOutbox` — the last module to register it wins, and DI resolution silently hands every
> consumer the wrong module's writer, pointed at the wrong database. Right now only
> `Onboarding` does this (`Equipment` has no outbox at all), so the trap is dormant. The
> moment a **second** module needs to enqueue its own messages, it must not also inject
> bare `IOutbox` — it needs its own module-specific writer interface (following the same
> shape: an interface plus a small implementation over its own `DbContext`) pointing at its
> own table. The failure mode if you don't is silent and intermittent.

### What is never allowed

Referencing another module's `Infrastructure` or `Domain` project. Injecting another
module's `DbContext`. Querying another module's tables. If you need any of those, you need
a contract — or the boundary is wrong.

---

## 17. The checklist

Five projects created and added to the solution:

- [ ] `Domain` — no references at all
- [ ] `Contracts` — no references at all
- [ ] `Application` — references Domain + BuildingBlocks
- [ ] `Infrastructure` — references Application + Contracts + EF packages
- [ ] `Presentation` — references Application + `Microsoft.AspNetCore.App`

Inside the module:

- [ ] Domain aggregate with a private constructor, a validating factory, private setters
- [ ] A module `DomainException`
- [ ] `I<Module>Command` marker interface
- [ ] Repository/read interfaces in `Application/Abstractions/`
- [ ] At least one use-case slice: `Command` + `Validator` + `Handler`
- [ ] `<Module>DbContext` with `ApplyConfigurationsFromAssembly`
- [ ] An `IEntityTypeConfiguration` per entity
- [ ] Repository implementations that stage but never `SaveChanges`
- [ ] `IDesignTimeDbContextFactory`
- [ ] **`TransactionBehavior`** binding your context and marker
- [ ] `Add<Module>Application()` with both assembly scans
- [ ] `Add<Module>Module(connectionString)` registering everything

Outside the module (four edits):

- [ ] Connection string in `appsettings.json` — and an empty one in `appsettings.Production.json`
- [ ] `RequireConnectionString("<Module>")` in `Program.cs`
- [ ] `.Add<Module>Module(...)` in the service chain
- [ ] `MigrateAsync()` in `WebApplicationExtensions.cs`

Then:

- [ ] `dotnet ef migrations add InitialCreate` with all four arguments
- [ ] Endpoints mapped, writes carrying `RequireAuthorization()`
- [ ] `app.Map<Module>Endpoints(versionSet)` in `Program.cs`
- [ ] Domain test + handler test
- [ ] `dotnet build` clean — remember warnings are errors here

---

## 18. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Endpoint returns `201` but no row exists | No `TransactionBehavior` registered for the module | [Chapter 10](#10-step-6--the-modules-unit-of-work) — and check the command carries your marker |
| `More than one DbContext was found` | `dotnet ef` cannot guess | Add `--context <Module>DbContext` |
| `Unable to create a DbContext` from `dotnet ef` | No design-time factory, or `--startup-project` missing | [Chapter 9.4](#94-the-design-time-factory) |
| `NU1010: no PackageVersion` | Package not in `Directory.Packages.props` | Add a `<PackageVersion>` entry centrally |
| `Cannot consume scoped service … from singleton` | Used the one-argument `AddDbContext` overload | Use `(sp, options) => …` so the audit interceptor resolves |
| Another module's outbox messages stop being delivered | Two modules both injected the shared `IOutbox` | Give the second module its own writer interface — see the trap in [chapter 16](#16-connecting-to-other-modules) |
| Build fails on an unused `using` | `TreatWarningsAsErrors` is inherited from the root props | Fix it — that is the gate working |
| `ConnectionStrings:<Module> is not configured` | The key is missing from configuration | [Chapter 12](#12-step-8--connection-string-and-host-wiring) |
| Domain project won't compile after adding EF | You added a reference that points outward | Introduce an interface in Application instead |
| Migration created in the wrong folder | `--output-dir` omitted | Delete it, re-run with `--output-dir Persistence/Migrations` |

---

## 19. Command cheat sheet

Everything assumes you are at the repository root unless stated.

```bash
# ── Creating the module ──────────────────────────────────────────────────────
cd src/Modules && mkdir <Module> && cd <Module>
dotnet new classlib -o <Module>.Domain          # repeat for Contracts, Application,
                                                # Infrastructure, Presentation

# ── References (run from src/Modules/<Module>) ───────────────────────────────
dotnet add <Module>.Application    reference <Module>.Domain
dotnet add <Module>.Application    reference ../../BuildingBlocks/BuildingBlocks.csproj
dotnet add <Module>.Infrastructure reference <Module>.Application
dotnet add <Module>.Infrastructure reference <Module>.Contracts
dotnet add <Module>.Infrastructure reference ../../BuildingBlocks.Persistence/BuildingBlocks.Persistence.csproj
dotnet add <Module>.Presentation   reference <Module>.Application

# ── Packages (no versions — Central Package Management) ──────────────────────
dotnet add <Module>.Application    package FluentValidation.DependencyInjectionExtensions
dotnet add <Module>.Application    package Microsoft.Extensions.DependencyInjection.Abstractions
dotnet add <Module>.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add <Module>.Infrastructure package Microsoft.EntityFrameworkCore.Design
dotnet add <Module>.Presentation   package Asp.Versioning.Http

# ── Migrations (from the repo root; all four arguments matter) ───────────────
dotnet ef migrations add InitialCreate   --project src/Modules/<Module>/<Module>.Infrastructure   --startup-project src/Api/CleanArch.Api   --context <Module>DbContext   --output-dir Persistence/Migrations

dotnet ef migrations remove --context <Module>DbContext   # if not yet applied
dotnet tool install --global dotnet-ef                    # one-time, if missing

# ── Building and running ─────────────────────────────────────────────────────
dotnet build                                    # warnings are errors here
dotnet run --project src/Api/CleanArch.Api      # applies migrations in Development
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj
dotnet build-server shutdown                    # if the compiler runs out of memory
```

---

## 20. Glossary

| Term | Meaning |
|---|---|
| **Aggregate** | A domain object that owns its data and guards its own rules. You change it only through its methods |
| **Bounded area / boundary** | The slice of the business one module is responsible for |
| **Command** | A request that changes state. Carries the module's command marker |
| **Contract** | An interface a module publishes for other modules to call. Lives in `*.Contracts`, has no dependencies |
| **CQRS-lite** | Writes go through aggregates and repositories; reads project straight to the response shape |
| **Dependency rule** | Source dependencies point inward. The domain depends on nothing |
| **DI (dependency injection)** | Handing a class the things it needs instead of letting it construct them |
| **DbContext** | EF Core's handle to one database. One per module |
| **Design-time factory** | A class that lets `dotnet ef` build your `DbContext` without booting the app |
| **Entity configuration** | The class that maps a domain object to tables, so the domain needn't know about EF |
| **Handler** | The code that executes one use case |
| **Idempotent** | Doing it twice has the same effect as doing it once. Required of outbox consumers |
| **Marker interface** | An empty interface used as a switch — `IAuditableRequest`, `I<Module>Command` |
| **Mediator** | The dispatcher that finds the handler for a request and wraps it in pipeline behaviours |
| **Migration** | A versioned script that brings a database schema in line with the code |
| **Modular monolith** | One deployable process, internally partitioned into modules that own their own data |
| **Outbox** | A table you write events into *in the same transaction* as your data, delivered later by a background processor |
| **Pipeline behaviour** | Cross-cutting code wrapped around every handler — logging, validation, transactions, audit |
| **Query** | A request that only reads. Carries no command marker, so it skips transactions and audit |
| **Repository** | An interface in the Application layer describing the persistence a use case needs |
| **Saga** | A multi-step process, glued by durable state rather than a single transaction, with compensating actions instead of rollback |
| **System of record** | The authoritative owner of a piece of data. Other modules reference it by id |
| **Unit of work** | Committing everything a request changed as one transaction — here, the module's `TransactionBehavior` |
| **Value object** | A domain type with no identity, defined by its values and validated on construction |
| **Vertical slice** | One file holding a feature's `Command`/`Query`, `Validator` and `Handler` |

---

## Appendix — Everything you created

```
src/Modules/<Module>/
├── <Module>.Domain/                     no references at all
│   ├── <Aggregate>.cs                       private ctor, validating factory, private setters
│   └── DomainException.cs                   this module's own
├── <Module>.Contracts/                  no references at all
│   └── I<Something>.cs                      only what other modules may call
├── <Module>.Application/
│   ├── I<Module>Command.cs                  the transaction marker
│   ├── DependencyInjection.cs               two assembly scans
│   ├── Abstractions/                        repository + read-service interfaces
│   └── <Feature>/<UseCase>.cs               Command + Validator + Handler
├── <Module>.Infrastructure/
│   ├── DependencyInjection.cs               Add<Module>Module(connectionString)
│   ├── Behaviors/TransactionBehavior.cs     binds context + marker
│   ├── Persistence/<Module>DbContext.cs
│   ├── Persistence/<Module>DbContextFactory.cs
│   ├── Persistence/EntityConfigurations/    one per entity
│   ├── Persistence/Migrations/              generated
│   ├── Repositories/                        stage, never SaveChanges
│   └── Reads/                               projections
└── <Module>.Presentation/
    └── <Module>Endpoints.cs                 bind, send, shape
```

Plus four edits outside the module:

- `CleanArchitecture.slnx` — the five projects, in their own folder
- `src/Api/CleanArch.Api/appsettings.json` — the connection string (and an empty one in `appsettings.Production.json`)
- `src/Api/CleanArch.Api/Program.cs` — `RequireConnectionString`, `.Add<Module>Module(...)`, `app.Map<Module>Endpoints(versionSet)`
- `src/Api/CleanArch.Api/WebApplicationExtensions.cs` — the `MigrateAsync()` call

---

## Where to go next

- **[Auditing — who changed what](40-auditing.md)** — your module's writes are already
  being captured if you called `AddAuditChangeTracking()`; that guide explains what you
  get and how to make it trustworthy.
- **[Talking across modules](60-talking-across-modules.md)** — the outbox, sagas and
  idempotency in full, once your module needs a multi-step process that must survive a
  restart, or a write into someone else's database.
