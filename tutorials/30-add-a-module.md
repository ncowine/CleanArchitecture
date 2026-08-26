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
| 5 | [Step 1 — Create the projects](#5-step-1--create-the-projects) | `dotnet new`, six times |
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
| 16 | [Connecting to other modules](#16-connecting-to-other-modules) | Contracts, outbox, and the trap |
| 17 | [The checklist](#17-the-checklist) | Run this when doing it for real |
| 18 | [Troubleshooting](#18-troubleshooting) | Symptom, cause, fix |
| 19 | [Command cheat sheet](#19-command-cheat-sheet) | The commands, in one place |
| 20 | [Glossary](#20-glossary) | Every term used in this guide |

---

## 1. What a module is

A **module** is a self-contained slice of the system: its own domain model, its own
database, its own HTTP endpoints, deployed inside the same process as every other module.

That last part is what makes it a *modular monolith* rather than microservices. One
process, one deployment, one debugger — but internally partitioned so that the `Library`
code cannot reach into the `Students` tables, even though both are running a metre apart.

### When to add one

Add a module when the new work has **its own vocabulary and its own reasons to change**.
The test that works in practice: could a different team own this, and would they mostly
argue with you about the *interface* rather than the internals? Then it is a module.

| Situation | What to do |
|---|---|
| A new endpoint on an existing concept | Add a feature to the existing module |
| A new concept that lives *inside* an existing area's rules | Add to the existing module's domain |
| A new area with its own lifecycle, own tables, own users | **Add a module** |
| A new app layered over an existing system of record | **Add a module** that references the other by id |
| Something you'll deploy separately one day | Add a module — it is the natural seam to split on later |

### When not to

Two modules that constantly need each other's data in the same transaction were never two
modules. If your first three features all need a cross-module write, the boundary is in
the wrong place — move it before you have migrations to unpick.

> **The worked example in this guide** is `TesterGuide`, the newest module in this
> repository. It is a good specimen because it does everything: its own database, a
> cross-module read from `TestPlans`, a cross-database write via the outbox, and
> real-time notifications. Open `src/Modules/TesterGuide/` alongside this guide.

---

## 2. Why each module owns its database

Each module has its own database file, its own `DbContext`, its own migration history.
`Students` cannot see `library.db`. That is the point, and it is worth being clear about
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
   Modules/TesterGuide/
   │
   │   ┌─ inner: knows nothing about frameworks ────────────────┐
   ├── TesterGuide.Domain            the rules. Zero references. │
   ├── TesterGuide.Contracts         what other modules may call │
   ├── TesterGuide.Application       use cases + interfaces      │
   │   └────────────────────────────────────────────────────────┘
   │   ┌─ outer: frameworks live here ───────────────────────────┐
   ├── TesterGuide.Infrastructure    EF Core, repositories       │
   └── TesterGuide.Presentation      HTTP endpoints              │
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
<!-- src/Modules/TesterGuide/TesterGuide.Domain/TesterGuide.Domain.csproj -->
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
existing modules. Avoid names that collide with framework types — this repository has an
entity called `TestTask` rather than `Task` for exactly that reason, because
`System.Threading.Tasks.Task` would shadow it in every file that does async work.

**2. The boundary.** Write down, in one sentence, what this module is responsible for. If
the sentence needs an "and", you may have two modules.

**3. What it needs from other modules.** For each: is it a *read* (get me that student's
name) or a *write* (record something over there)? Reads are easy — a published contract.
Writes are the outbox and a saga. Knowing which you need changes what you build.

**4. What other modules will need from it.** This is what goes in `Contracts`. Start
empty; add only when a real consumer appears.

---

## 5. Step 1 — Create the projects

From the repository root:

```bash
cd src/Modules
mkdir TesterGuide && cd TesterGuide

dotnet new classlib -o TesterGuide.Domain
dotnet new classlib -o TesterGuide.Contracts
dotnet new classlib -o TesterGuide.Application
dotnet new classlib -o TesterGuide.Infrastructure
dotnet new classlib -o TesterGuide.Presentation
```

Delete the `Class1.cs` that `dotnet new` puts in each one.

Then add them to the solution. This repository uses the newer XML solution format
(`CleanArchitecture.slnx`), so they go in a folder of their own:

```xml
<!-- CleanArchitecture.slnx -->
<Folder Name="/src/Modules/TesterGuide/">
  <Project Path="src/Modules/TesterGuide/TesterGuide.Application/TesterGuide.Application.csproj" />
  <Project Path="src/Modules/TesterGuide/TesterGuide.Contracts/TesterGuide.Contracts.csproj" />
  <Project Path="src/Modules/TesterGuide/TesterGuide.Domain/TesterGuide.Domain.csproj" />
  <Project Path="src/Modules/TesterGuide/TesterGuide.Infrastructure/TesterGuide.Infrastructure.csproj" />
  <Project Path="src/Modules/TesterGuide/TesterGuide.Presentation/TesterGuide.Presentation.csproj" />
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
dotnet add TesterGuide.Application reference TesterGuide.Domain
dotnet add TesterGuide.Application reference ../../BuildingBlocks/BuildingBlocks.csproj

# Infrastructure depends on Application + this module's Contracts
dotnet add TesterGuide.Infrastructure reference TesterGuide.Application
dotnet add TesterGuide.Infrastructure reference TesterGuide.Contracts
dotnet add TesterGuide.Infrastructure reference ../../BuildingBlocks.Persistence/BuildingBlocks.Persistence.csproj

# Presentation depends on Application only
dotnet add TesterGuide.Presentation reference TesterGuide.Application
```

Then the packages each outer layer needs:

```bash
dotnet add TesterGuide.Application package FluentValidation.DependencyInjectionExtensions
dotnet add TesterGuide.Application package Microsoft.Extensions.DependencyInjection.Abstractions

dotnet add TesterGuide.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add TesterGuide.Infrastructure package Microsoft.EntityFrameworkCore.Design

dotnet add TesterGuide.Presentation package Asp.Versioning.Http
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
behaviour as methods. From `src/Modules/TesterGuide/TesterGuide.Domain/Focus.cs`'s shape:

```csharp
namespace TesterGuide.Domain;

public sealed class Focus
{
    private Focus() { }                       // EF needs a parameterless ctor; nobody else may use it

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }

    public static Focus Create(string name, string? description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A focus needs a name.");

        return new Focus { Id = Guid.NewGuid(), Name = name.Trim(), Description = description };
    }

    public void Rename(string name) { /* validate, then assign */ }
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
// src/Modules/TesterGuide/TesterGuide.Application/ITesterGuideCommand.cs
namespace TesterGuide.Application;

/// <summary>
/// Marks a request as a Tester Guide-module write that must run inside a TesterGuideDbContext
/// transaction. The module's transaction behavior wraps only requests carrying this marker, so
/// queries — and other modules' requests — are left untouched.
/// </summary>
public interface ITesterGuideCommand;
```

Every module has one. It is how the module's transaction behaviour knows which requests
are *its* writes: a `Students` command flowing through the pipeline must not open a
transaction on *your* database.

### 8.2 The abstractions

Interfaces describing the persistence your use cases need, in
`TesterGuide.Application/Abstractions/`. They speak in domain types and know nothing about
EF:

```csharp
public interface IFocusRepository
{
    Task AddAsync(Focus focus, CancellationToken cancellationToken);
    Task<Focus?> GetAsync(Guid focusId, CancellationToken cancellationToken);
}
```

Reads get their own interface — `IGuideReadService` — because reads don't load aggregates;
they project straight to the response shape the endpoint needs.

### 8.3 The first use case

One file per use case: a static class holding the `Command`, an optional `Validator`, and
the `Handler`. This is `TesterGuide.Application/Focuses/CreateFocus.cs`, complete:

```csharp
public static class CreateFocus
{
    public sealed record Command(string Name, string? Description)
        : IRequest<Guid>, ITesterGuideCommand, IAuditableRequest;

    public sealed class Validator : AbstractValidator<Command>
    {
        public Validator()
        {
            RuleFor(command => command.Name).NotEmpty().MaximumLength(100);
            RuleFor(command => command.Description).MaximumLength(500);
        }
    }

    public sealed class Handler : IRequestHandler<Command, Guid>
    {
        private readonly IFocusRepository _focuses;

        public Handler(IFocusRepository focuses) => _focuses = focuses;

        public async Task<Guid> Handle(Command command, CancellationToken cancellationToken)
        {
            var focus = Focus.Create(command.Name, command.Description);
            await _focuses.AddAsync(focus, cancellationToken);
            return focus.Id;
        }
    }
}
```

Read the three marker interfaces on that `Command` — they are the whole cross-cutting story:

| Marker | Effect |
|---|---|
| `IRequest<Guid>` | The mediator can dispatch it, and it returns a `Guid` |
| `ITesterGuideCommand` | Run me inside a transaction on **this module's** database |
| `IAuditableRequest` | Record me to the audit trail ([guide 40](40-auditing.md)) |

Note what the handler does **not** do: it never calls `SaveChanges`. `AddAsync` only
stages the entity; the transaction behaviour commits everything at the end. That is the
unit-of-work pattern, and it is why a handler that touches three aggregates still results
in one atomic write.

### 8.4 The DI extension

```csharp
// src/Modules/TesterGuide/TesterGuide.Application/DependencyInjection.cs
public static class DependencyInjection
{
    public static IServiceCollection AddTesterGuideApplication(this IServiceCollection services)
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
// src/Modules/TesterGuide/TesterGuide.Infrastructure/Persistence/TesterGuideDbContext.cs
public sealed class TesterGuideDbContext : DbContext
{
    public TesterGuideDbContext(DbContextOptions<TesterGuideDbContext> options) : base(options) { }

    public DbSet<Focus> Focuses => Set<Focus>();
    public DbSet<GuideConfig> Configs => Set<GuideConfig>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();   // only if you publish events

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TesterGuideDbContext).Assembly);
        modelBuilder.ApplyOutboxConfiguration();                   // only if you publish events
    }
}
```

`ApplyConfigurationsFromAssembly` means you never edit this file again either — each
entity's mapping lives in its own class.

### 9.2 Entity configurations

One per entity, in `Persistence/EntityConfigurations/`. This is where the domain gets
mapped to tables **without the domain knowing**:

```csharp
internal sealed class FocusConfiguration : IEntityTypeConfiguration<Focus>
{
    public void Configure(EntityTypeBuilder<Focus> builder)
    {
        builder.ToTable("Focuses");
        builder.HasKey(focus => focus.Id);
        builder.Property(focus => focus.Name).IsRequired().HasMaxLength(100);
        builder.Property(focus => focus.Description).HasMaxLength(500);
    }
}
```

Column lengths, owned value objects, enum-to-string conversions and child tables all live
here. The domain class stays a plain C# object.

### 9.3 Repositories

Implement the Application's interfaces. They stage; they do not save:

```csharp
public async Task AddAsync(Focus focus, CancellationToken cancellationToken) =>
    await _db.Focuses.AddAsync(focus, cancellationToken);
```

### 9.4 The design-time factory

EF's command-line tools need to construct your `DbContext` without booting the API. Without
this class, `dotnet ef` either fails or starts the whole host just to read a schema:

```csharp
internal sealed class TesterGuideDbContextFactory : IDesignTimeDbContextFactory<TesterGuideDbContext>
{
    public TesterGuideDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__TesterGuide")
            ?? "Data Source=testerguide-design.db";

        var options = new DbContextOptionsBuilder<TesterGuideDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new TesterGuideDbContext(options);
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
// src/Modules/TesterGuide/TesterGuide.Infrastructure/Behaviors/TransactionBehavior.cs
internal sealed class TransactionBehavior<TRequest, TResponse>
    : TransactionBehaviorBase<TRequest, TResponse, TesterGuideDbContext>
    where TRequest : IRequest<TResponse>, ITesterGuideCommand
{
    public TransactionBehavior(TesterGuideDbContext db) : base(db) { }
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

One public method — the module's only entry point:

```csharp
// src/Modules/TesterGuide/TesterGuide.Infrastructure/DependencyInjection.cs
public static IServiceCollection AddTesterGuideModule(
    this IServiceCollection services, string connectionString)
{
    services.AddTesterGuideApplication();

    // Audit change-tracking: capture before/after values of every write for the audit trail.
    services.AddAuditChangeTracking();
    services.AddDbContext<TesterGuideDbContext>((sp, options) =>
        options.UseSqlite(connectionString).UseAuditChangeTracking(sp));

    services.AddScoped<IFocusRepository, EfFocusRepository>();
    services.AddScoped<IGuideConfigRepository, EfGuideConfigRepository>();
    services.AddScoped<IGuideReadService, GuideReadService>();

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
  "Students": "Data Source=students.db",
  "Library": "Data Source=library.db",
  "TestPlans": "Data Source=testplans.db",
  "TesterGuide": "Data Source=testerguide.db"
}
```

**2. `Program.cs`** — read it, and fail loudly if it is missing:

```csharp
var testerGuideConnectionString = RequireConnectionString("TesterGuide");
```

`RequireConnectionString` throws at startup when the value is absent. That is deliberate:
a missing connection string should stop the process, not quietly create a stray SQLite
file next to the binary in production.

**3. `Program.cs`** — register the module:

```csharp
builder.Services
    .AddApiServices()
    // ...
    .AddStudentsModule(studentsConnectionString)
    .AddLibraryModule(libraryConnectionString)
    .AddTestPlansModule(testPlansConnectionString)
    .AddTesterGuideModule(testerGuideConnectionString);
```

**4. `WebApplicationExtensions.cs`** — migrate it on startup:

```csharp
await scope.ServiceProvider.GetRequiredService<TesterGuideDbContext>().Database.MigrateAsync();
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
  --project src/Modules/TesterGuide/TesterGuide.Infrastructure \
  --startup-project src/Api/CleanArch.Api \
  --context TesterGuideDbContext \
  --output-dir Persistence/Migrations
```

Every argument matters:

| Argument | Why |
|---|---|
| `--project` | Where the migration files are written — your Infrastructure project |
| `--startup-project` | Where the design-time factory is discovered from |
| `--context` | **Required.** The host references four `DbContext`s; without this EF refuses to guess |
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
public static class TesterGuideEndpoints
{
    public static IEndpointRouteBuilder MapTesterGuideEndpoints(
        this IEndpointRouteBuilder app, ApiVersionSet versionSet)
    {
        var focuses = MapGuideGroup(app, versionSet, "Tester Guide — Focus Manager");

        focuses.MapPost("/focuses", async (
            CreateFocus.Command command, ISender sender, CancellationToken cancellationToken) =>
        {
            var id = await sender.Send(command, cancellationToken);
            return Results.Created($"/guide/focuses/{id}", new { id });
        })
        .WithName("CreateFocus")
        .WithSummary("Create a focus (a named label attached to configs).")
        .RequireAuthorization();

        return app;
    }
}
```

Group your routes under one prefix (`/guide` here) and give each group a Swagger tag, so
the API explorer stays navigable as the module grows. Then map it in `Program.cs`:

```csharp
app.MapTesterGuideEndpoints(versionSet);
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
public void Create_rejects_an_empty_name() =>
    Assert.Throws<DomainException>(() => Focus.Create("  ", null));
```

**Handler tests** use hand-written fakes of the Application interfaces — see
`tests/CleanArch.UnitTests/Fakes.cs` for the existing ones (`FakeStudentRepository` and
friends are simple in-memory dictionaries, not mocking-framework setups):

```csharp
[Fact]
public async Task CreateFocus_stores_the_focus()
{
    var repository = new FakeFocusRepository();
    var handler = new CreateFocus.Handler(repository);

    var id = await handler.Handle(new CreateFocus.Command("Regression", null), default);

    Assert.NotNull(await repository.GetAsync(id, default));
}
```

There is also `tests/CleanArch.Api.IntegrationTests/` for tests that need the real host —
use it for the wiring you cannot check in isolation, not as your default.

---

## 16. Connecting to other modules

Two sanctioned ways. Never a third.

### Synchronous reads — a published contract

The module that **owns** the data publishes an interface in its `Contracts` project; you
reference that project and depend on the interface. `TesterGuide` reads test-plan content
this way:

```csharp
// TestPlans.Contracts/ITestPlanCatalog.cs — owned by TestPlans, referenced by TesterGuide
public interface ITestPlanCatalog
{
    Task<TestPlanTree?> GetTreeAsync(Guid testPlanId, CancellationToken ct);
    Task<bool> VersionExistsAsync(Guid testPlanId, Guid versionId, CancellationToken ct);
}
```

`TesterGuide.Application.csproj` references `TestPlans.Contracts.csproj` and nothing else
of theirs. The implementation lives in `TestPlans.Infrastructure` and owns the database
access. Composition happens in your application layer — never a cross-database join.

### Asynchronous writes — the outbox

When your module must cause a write in another module's database, you cannot do it in your
transaction. You write an **outbox message** in the same transaction as your own change,
and a background processor delivers it afterwards:

```csharp
services.AddScoped<ITesterGuideOutbox, TesterGuideOutbox>();
services.AddOutboxProcessing<TesterGuideDbContext, TesterGuideOutboxDispatcher>();
services.AddOutboxAdmin<TesterGuideDbContext>();
```

You also add `DbSet<OutboxMessage> Outbox` and `ApplyOutboxConfiguration()` to your
`DbContext`, and a new migration for the table. Delivery is **at-least-once**, so the
consumer on the other side must be idempotent. The full pattern — sagas, compensation,
dead-lettering, replay — is its own guide.

> ### The trap that will cost you an afternoon
>
> The shared `IOutbox` abstraction is registered as an **open generic**, so only one module
> can own it. If a second module also registers `IOutbox`, DI resolution is
> last-registration-wins and the first module's outbox silently starts writing to the wrong
> database.
>
> That is why every module after the first defines its **own** writer interface —
> `ITesterGuideOutbox`, `IStudentOutbox` — pointing at its own table. Follow that pattern;
> the failure mode if you don't is silent and intermittent.

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
| Another module's outbox messages stop being delivered | Two modules registered the open-generic `IOutbox` | Give your module its own writer interface — see the trap in [chapter 16](#16-connecting-to-other-modules) |
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
| **Saga** | A multi-step process across databases, glued by events, with compensating actions instead of rollback |
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
- The cross-module messaging guide (planned) — for the outbox, sagas and idempotency in
  full, once your module needs to write into someone else's database.
