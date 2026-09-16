# Testing

**Who this is for:** someone who wants tests that catch real bugs and survive refactoring —
not a coverage number.

**What you'll be able to do by the end:** test a business rule directly, test a use case
without a database, write a fake that's better than a mock, name a test so its failure
message does the diagnosis, and know which of the very few things actually need an
integration test.

**What you need first:** [guide 20](20-add-a-feature.md), or a feature of your own to test.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [Why this codebase is easy to test](#1-why-this-codebase-is-easy-to-test) | The payoff for the dependency rule |
| 2 | [Two kinds of test, and a third you'll rarely need](#2-two-kinds-of-test-and-a-third-youll-rarely-need) | Pick the right one |
| 3 | [Step 1 — Test a rule](#3-step-1--test-a-rule) | Straight at the domain |
| 4 | [Step 2 — Test a use case](#4-step-2--test-a-use-case) | With a fake |
| 5 | [Step 3 — Write a fake](#5-step-3--write-a-fake) | The three moves |
| 6 | [Fakes vs mocks](#6-fakes-vs-mocks) | Why this codebase chose one |
| 7 | [Naming, and the Build helper](#7-naming-and-the-build-helper) | Make failures self-diagnosing |
| 8 | [Testing the interesting cases](#8-testing-the-interesting-cases) | Transitions, not happy paths |
| 9 | [Testing across modules](#9-testing-across-modules) | Contracts and idempotency |
| 10 | [Integration tests](#10-integration-tests) | The narrow, real use |
| 11 | [What not to test](#11-what-not-to-test) | Where effort is wasted |
| 12 | [The checklist](#12-the-checklist) | Run this when doing it for real |
| 13 | [Troubleshooting](#13-troubleshooting) | Symptom, cause, fix |
| 14 | [Cheat sheet](#14-cheat-sheet) | Patterns and commands |
| 15 | [Glossary](#15-glossary) | Every term used in this guide |

---

## 1. Why this codebase is easy to test

Because of the dependency rule, and that is the whole return on the architecture.

`Equipment.Domain` references nothing. `Equipment.Application` references only the domain and
some building blocks. Neither can touch EF Core or ASP.NET — so **neither needs them to run
in a test**.

```csharp
var asset = EquipmentAsset.Create("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001");
```

No fixture, no container, no connection string, no `[Collection]` attribute to serialise
database access. It constructs an object and asserts. Runs in microseconds.

That speed is not a vanity metric — it is what makes the difference between tests you run on
every save and tests you run in CI and quietly stop reading.

**The corollary:** if something is hard to test, that is usually the design talking. A rule
you cannot test without a database is a rule that has leaked into infrastructure.

---

## 2. Two kinds of test, and a third you'll rarely need

| | Tests | Needs | How many |
|---|---|---|---|
| **Domain test** | A business rule, directly | Nothing | Most of them |
| **Handler test** | A use case, with fakes | Nothing | Most of the rest |
| **Integration test** | Real EF Core + a real database | A temp SQLite file | Very few |

The 88 tests in `tests/CleanArch.UnitTests/` are almost all the first two kinds.
`tests/CleanArch.Api.IntegrationTests/` holds 22 — and that ratio is deliberate, not an
omission.

**Why so few integration tests:** they are slower, they fail for reasons unrelated to your
change, and each one covers a thin path through a lot of code. They earn their place when
they test *wiring* — the thing unit tests structurally cannot see. Use them for that and
nothing else. (This codebase's integration tests don't spin up a full HTTP host either — see
[chapter 10](#10-integration-tests) for why, and what they exercise instead.)

---

## 3. Step 1 — Test a rule

Domain tests are the cheapest tests you will ever write, and they test the most valuable
code. Go straight at the object — this is the real `EquipmentAssetTests`:

```csharp
public class EquipmentAssetTests
{
    private static EquipmentAsset Create() =>
        EquipmentAsset.Create(" ThinkPad X1 ", EquipmentCategory.Laptop, " LAP-001 ");

    [Fact]
    public void Create_trims_input_and_starts_available()
    {
        var asset = Create();

        Assert.Equal("ThinkPad X1", asset.Name);
        Assert.Equal("LAP-001", asset.AssetTag);
        Assert.Equal(EquipmentCategory.Laptop, asset.Category);
        Assert.Equal(EquipmentStatus.Available, asset.Status);
        Assert.Null(asset.ReservedForOnboardingRequestId);
    }
}
```

Note it asserts the **trimming and the starting state**, not just that construction
succeeded. Both are real behaviour someone will depend on; if either silently stops, a test
that only checked `Assert.NotNull(asset)` would still pass.

For the rejection cases, a `[Theory]` keeps the table readable:

```csharp
[Theory]
[InlineData("", "TAG-1")]
[InlineData("  ", "TAG-1")]
[InlineData("Name", "")]
[InlineData("Name", "  ")]
public void Create_with_invalid_input_throws(string name, string assetTag) =>
    Assert.Throws<DomainException>(() => EquipmentAsset.Create(name, EquipmentCategory.Laptop, assetTag));
```

Four rules in four lines, and adding a fifth is one line.

State-transition rules belong here too — `Reserve()` throwing when the asset is already
reserved, `Approve()` throwing when an onboarding request has already been decided
(`OnboardingRequestTests`) — because they are domain invariants, not use cases. If a test
needs a repository or a fake to exercise a rule, the rule has drifted into the wrong layer.

---

## 4. Step 2 — Test a use case

A handler depends on interfaces, so a test hands it in-memory implementations and asserts on
what came out. No database, no mediator, no host — construct the handler and call `Handle`.
This is real code, one of six tests in `ApproveOnboardingInstantHandlerTests`:

```csharp
[Fact]
public async Task Licence_step_failing_compensates_the_reserved_equipment()
{
    var (handler, request, equipment, _, access) = Build(
        licences: FakeLicenceAllocationService.Failing("No Standard licences left."));

    var result = await handler.Handle(new ApproveOnboardingInstant.Command(request.Id), default);

    Assert.False(result.Ready);
    Assert.Equal(OnboardingStatus.Failed, request.Status);
    Assert.Equal(StepStatus.Compensated, request.EquipmentStepStatus);
    Assert.Equal(request.Id, Assert.Single(equipment.ReleaseCalls));
    Assert.Empty(access.ProvisionCalls); // never reached
}
```

Three things this demonstrates that are worth copying:

**It asserts on the compensating call, not just the return value.** `result.Ready` being
false is the handler's own claim about itself. `equipment.ReleaseCalls` is the observable
consequence — proof the equipment was actually released, not just that the handler said it
failed. Assert on consequences.

**`Assert.Single` returns the item**, so you can go on to assert about it (here, that it's
this request's id). Nicer than `Assert.Equal(1, list.Count)` followed by `list[0]`, and the
failure message is better.

**The failure *and* its cleanup are both checked, and so is what never ran.**
`access.ProvisionCalls` being empty proves the saga stopped where it should have — a test
that only checked the equipment release would not notice if access provisioning ran anyway.

You do **not** need the mediator. Behaviours — validation, transactions, audit — are tested
once, where they live. Re-testing them through every handler tests the framework, slowly.

---

## 5. Step 3 — Write a fake

A fake is a real, working, in-memory implementation of an interface. Equipment's live in
`tests/CleanArch.UnitTests/EquipmentFakes.cs`, Onboarding's in `OnboardingFakes.cs`.

```csharp
internal sealed class FakeOnboardingRequestRepository : IOnboardingRequestRepository
{
    private readonly Dictionary<Guid, OnboardingRequest> _requests = new();

    public List<OnboardingRequest> Added { get; } = new();

    public void Seed(OnboardingRequest request) => _requests[request.Id] = request;

    public Task AddAsync(OnboardingRequest request, CancellationToken cancellationToken)
    {
        Added.Add(request);
        _requests[request.Id] = request;
        return Task.CompletedTask;
    }

    public Task<OnboardingRequest?> GetAsync(Guid onboardingRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(_requests.TryGetValue(onboardingRequestId, out var request) ? request : null);
}
```

Three moves, and every good fake has some combination of them:

| Move | Purpose | Here |
|---|---|---|
| **Seed** | Arrange preconditions | `Seed(request)` |
| **Record** | Let the test assert on what happened | `Added` |
| **Configure** | Control a return value per test | `FakeEquipmentReservationService.Succeeding(...)` / `.Failing(reason)` |

That last move doesn't have to be a mutable property — a pair of named factory methods
reads better at the call site (`FakeLicenceAllocationService.Failing("No Standard licences left.")`
tells you what the test is arranging without opening the fake's file).

The simplest useful fake in the whole file is four lines:

```csharp
internal sealed class FakeRealtimeDispatch : IRealtimeDispatch
{
    public List<(string Group, RealtimeEvent Event)> Published { get; } = new();

    public void Publish(string group, RealtimeEvent realtimeEvent) => Published.Add((group, realtimeEvent));
}
```

That one fake is what makes every realtime-publish assertion in the suite possible — see
`CreateEquipmentHandlerTests`.

A fake should be **honest**. `FakeEquipmentReservationService.ReserveCalls` genuinely records
every call, in order, because a fake that silently dropped calls would let a "called it twice
by accident" bug through untested. Where a fake simplifies — it returns one fixed result for
every call rather than modelling real stock — that is a deliberate choice to keep the test's
arrangement direct, and it is what the `Succeeding()` / `Failing()` naming documents.

---

## 6. Fakes vs mocks

This codebase uses hand-written fakes and no mocking framework. The reasoning, since it is a
live argument:

| | Fake | Mock |
|---|---|---|
| Setup | Written once, reused everywhere | Configured in every test |
| Reads like | An object | A DSL |
| Refactoring | Breaks at compile time — you fix it once | Breaks at run time, in every test that configured it |
| Failure message | "expected 1 item, got 0" | "expected invocation not performed" |
| Tests | What the code *did* | What the code *called* |

That last row is the real argument. A mock verifying `_repository.Received().AddAsync(...)`
asserts on an interaction — so renaming or restructuring the call breaks the test even though
the behaviour is unchanged. A fake with an `Added` list asserts on the outcome, and survives
the refactor.

Mocks earn their place for awkward interfaces — twenty members where you care about one — but
that is usually a signal the interface is too big.

> **This is not a rule against mocking libraries.** It is a rule for asserting on outcomes
> rather than interactions, which fakes make the path of least resistance.

---

## 7. Naming, and the `Build` helper

### Name the behaviour, not the method

```csharp
All_steps_succeeding_marks_the_request_ready()
Equipment_step_failing_fails_the_request_with_nothing_to_compensate()
Licence_step_failing_compensates_the_reserved_equipment()
Access_step_failing_compensates_both_licence_and_equipment_in_reverse_order()
```

Read them as sentences. When one fails in CI, the name alone tells you what broke — often
enough to know the cause without opening the file. Compare `TestApproveOnboarding2`.

Underscores are used deliberately (the analyzer warning for them is suppressed in the test
project), because at this length they are far more readable than camel case.

### Factor the arrangement

```csharp
private static (
    ApproveOnboardingInstant.Handler Handler,
    OnboardingRequest Request,
    FakeEquipmentReservationService Equipment,
    FakeLicenceAllocationService Licences,
    FakeAccessProvisioningService Access) Build(
    FakeEquipmentReservationService? equipment = null,
    FakeLicenceAllocationService? licences = null,
    FakeAccessProvisioningService? access = null)
{
    var repository = new FakeOnboardingRequestRepository();
    var request = Pending();
    repository.Seed(request);
    equipment ??= FakeEquipmentReservationService.Succeeding();
    licences ??= FakeLicenceAllocationService.Succeeding();
    access ??= FakeAccessProvisioningService.Succeeding();
    var handler = new ApproveOnboardingInstant.Handler(repository, equipment, licences, access);
    return (handler, request, equipment, licences, access);
}
```

One helper returning a named tuple of everything the tests need, with every fake defaulting
to "succeeds" so each test overrides only the one that matters to it — compare the four-line
call in [chapter 4](#4-step-2--test-a-use-case) to constructing all three fakes by hand.

**Why this matters:** when the handler gains a constructor parameter, you update one helper
rather than six tests. That is the difference between tests that get maintained and tests
that get deleted.

### The test project relaxes the rules

```xml
<TreatWarningsAsErrors>false</TreatWarningsAsErrors>
<NoWarn>$(NoWarn);CA1707;CA2007;CA1861</NoWarn>
```

Underscored names, no `ConfigureAwait`, inline arrays. Production code is held to the strict
bar; test code is optimised for reading.

---

## 8. Testing the interesting cases

Happy paths are the least valuable tests. The bugs live at the edges.

### Transitions

`ApproveOnboardingInstantHandlerTests` is a template. Four tests around one saga:

| Test | What fails | Asserts |
|---|---|---|
| Nothing | — | Ready; every step `Completed`; nothing released |
| Equipment (1st step) | No stock | Failed; equipment `Failed`; licence never attempted |
| Licence (2nd step) | Pool exhausted | Failed; equipment `Compensated`; access never attempted |
| Access (3rd step) | Unsupported level | Failed; **both** equipment and licence `Compensated`, in reverse order |

The last two are the ones people forget. It is easy to test the happy path and the very
first failure, and never check that a failure two steps in actually unwinds what already
succeeded — which is the entire point of building a saga instead of trusting a single
database transaction. [Guide 60](60-talking-across-modules.md#5-compensation--the-saga-pattern)
explains why a transaction can't do this job across two databases.

### Idempotency

Anything a saga step can call more than once (a retried instant approval, a redelivered
outbox message) must handle being called twice. This is `IEquipmentReservationService`'s own
consuming-side implementation, tested against a **real** database — reserving is a stateful
check against what's already in stock, so this is naturally an integration test, not a fake:

```csharp
[Fact]
public async Task Reserving_equipment_twice_for_the_same_onboarding_request_is_idempotent()
{
    using var scope = _provider.CreateScope();
    var sender = scope.ServiceProvider.GetRequiredService<ISender>();
    await sender.Send(new CreateEquipment.Command("ThinkPad X1", EquipmentCategory.Laptop, "LAP-001"), default);

    var reservations = scope.ServiceProvider.GetRequiredService<IEquipmentReservationService>();
    var onboardingRequestId = Guid.NewGuid();

    var first = await reservations.ReserveAsync(onboardingRequestId, "Laptop", default);
    var second = await reservations.ReserveAsync(onboardingRequestId, "Laptop", default);

    Assert.True(first.Reserved);
    Assert.True(second.Reserved);
    Assert.Equal(first.EquipmentId, second.EquipmentId); // the same reservation, not a second one
}
```

If your consumer isn't idempotent, this is the only test that will tell you before production
does — a redelivered message would otherwise reserve a second item, or double-charge, or
double-anything.

### Boundaries and rejections

Exactly at the limit. Zero. Empty collections. The state transition that should throw —
`Approve()` on an already-decided request is documented as a one-way door, so approving twice
is a test (`Approving_an_already_decided_request_throws`).

And the rejection path in a saga: a step that fails must produce compensation, not a
half-finished `Ready` request. That branch is where the saga actually earns its name, and it
is invisible on the happy path.

---

## 9. Testing across modules

You do not need both databases, or either. The seam between modules is a published contract,
so a test fakes it — this is the real `FakeEquipmentReservationService`:

```csharp
internal sealed class FakeEquipmentReservationService : IEquipmentReservationService
{
    private readonly EquipmentReservationResult _result;

    public List<Guid> ReserveCalls { get; } = new();
    public List<Guid> ReleaseCalls { get; } = new();

    private FakeEquipmentReservationService(EquipmentReservationResult result) => _result = result;

    public static FakeEquipmentReservationService Succeeding(Guid? equipmentId = null) =>
        new(new EquipmentReservationResult(true, equipmentId ?? Guid.NewGuid(), null));

    public static FakeEquipmentReservationService Failing(string reason) =>
        new(new EquipmentReservationResult(false, null, reason));

    public Task<EquipmentReservationResult> ReserveAsync(
        Guid onboardingRequestId, string category, CancellationToken cancellationToken)
    {
        ReserveCalls.Add(onboardingRequestId);
        return Task.FromResult(_result);
    }

    public Task ReleaseAsync(Guid onboardingRequestId, CancellationToken cancellationToken)
    {
        ReleaseCalls.Add(onboardingRequestId);
        return Task.CompletedTask;
    }
}
```

Constructed with `Failing("No Laptop in stock.")`, it tests "what happens when nothing is in
stock" — a case that would otherwise need a second database seeded into a specific state.

For the two sides of a cross-module call:

- **The calling side** (Onboarding) — assert the compensating call happens on failure, and
  *only* on failure (`equipment.ReleaseCalls`), via the handler tests in
  [chapter 4](#4-step-2--test-a-use-case).
- **The implementing side** (Equipment) — test the contract implementation directly: does it
  do the work, is it idempotent ([chapter 8](#8-testing-the-interesting-cases)), does it
  reject correctly (the "unknown category" and "nothing in stock" branches)?

The dispatcher in between — `OnboardingOutboxDispatcher`, which drives the persisted saga's
forward and compensating steps — is a `switch` over message type that touches its own
`DbContext` directly, loading the request and saga state fresh on every call. Because of
that, it's naturally an integration-test subject rather than a fake-based unit test: see
`OnboardingSagaTests` in the next chapter, including the case where a step fails and enqueues
its own compensation.

---

## 10. Integration tests

Reserve them for what unit tests structurally cannot see: **wiring**.

`tests/CleanArch.Api.IntegrationTests/` holds two test classes. `OnBehalfOfFlowTests` covers
the On-Behalf-Of token exchange flow, which spans an authentication handler, an HTTP message
handler, a token cache and a downstream call — no unit test can tell you those are correctly
connected in the real pipeline. `EquipmentModuleTests` and `OnboardingSagaTests` cover this
codebase's own wiring the same way: real `AddEquipmentModule`/`AddOnboardingModule`
registrations, a real (temp-file) SQLite database, real `HybridCache` — all through a bare
`ServiceCollection`, **not** a full HTTP host (`WebApplicationFactory`). That's a deliberate
choice, not a shortcut: it exercises every layer that matters (EF Core, the mediator
pipeline, HybridCache, the outbox) while staying fast enough to run on every `dotnet test`,
and it means the small number of things a real HTTP pipeline would add — routing, model
binding, auth middleware — are exactly the things *not* covered here, because nothing else in
this suite needs them proven twice.

Good candidates, all real tests in this repo:

- A second read is served from cache, not the database — proven by mutating the row with raw
  SQL (bypassing the cache invalidator entirely) and asserting the *stale* value comes back
  (`A_second_read_is_served_from_cache_not_the_database`)
- The persisted saga converges to `Ready` after the real dispatcher drains every outbox
  message it enqueues along the way (`Standard_saga_succeeding_converges_to_ready_after_draining_the_outbox`)
- A cross-module compensation genuinely changed the *other* module's data, not just a local
  flag (`Instant_saga_failing_on_access_releases_the_real_equipment_reservation`)
- Reserving the same request twice only reserves once ([chapter 8](#8-testing-the-interesting-cases))

Bad candidates — write these as unit tests instead:

- Business rules
- Handler behaviour
- Anything where the interesting logic is one class deep

> **Every integration test is a small ongoing tax**: it's slower, it can fail for
> environmental reasons, and it will occasionally be flaky. Pay it where the coverage is
> genuinely unavailable elsewhere; refuse it where it duplicates a unit test.

---

## 11. What not to test

Effort spent here buys nothing and costs maintenance:

| Don't test | Why |
|---|---|
| EF Core saves what you told it to | You'd be testing Microsoft's code |
| A validator's `NotEmpty()` works | Same |
| Getters and setters | No behaviour |
| That a handler calls a repository | An interaction, not an outcome. Assert on the result |
| Framework wiring already covered by one integration test | Duplicated cost |
| Private methods | Test them through the public behaviour that uses them |

And the meta-rule: **a test that never fails is not protecting you.** If you cannot describe
the bug a test would catch, don't write it.

Coverage percentage is a poor target. 100% coverage of getters with no test for the
compensation path is worse than 60% with the edges covered.

---

## 12. The checklist

For a new **rule**:

- [ ] A test for the valid case, asserting the *behaviour* — normalisation, defaults, state
- [ ] A `[Theory]` for the rejection cases
- [ ] Fixed dates and ids as constants, never `DateTime.Now`
- [ ] Boundaries: exactly at the limit, zero, empty

For a new **use case**:

- [ ] Handler constructed directly with fakes — no mediator, no host
- [ ] Assertions on outcomes, not on which methods were called
- [ ] The not-found / rejection path
- [ ] Any compensating call is asserted, with what it was called with
- [ ] A `Build(...)` helper if more than two tests share arrangement

For anything **cross-module**:

- [ ] The calling side asserts the compensating call happens on failure, and only then
- [ ] The saga's transitions tested at every step: each one succeeding, and each one failing
- [ ] The consuming side (the contract implementation) tested directly, against a real database
- [ ] **Called twice with the same correlating id** — idempotency
- [ ] The rejection/compensation branch, if it can reject

Generally:

- [ ] Test names read as sentences
- [ ] New fake methods behave honestly — real filtering, real recording
- [ ] `dotnet test` green before you push

---

## 13. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Test needs a database | Logic has leaked into infrastructure | Move the rule into the domain |
| Test passes alone, fails in a run | Shared mutable state between tests | Fresh fakes per test; xUnit makes a new class instance per test |
| Test failed today, passed yesterday | `DateTime.Now` somewhere | Fixed date constants |
| Constructor change broke several tests | No `Build` helper | Factor the arrangement |
| Assertion passes but the bug ships | Asserting on the return value, not the consequence | Assert on what the outside world observes |
| Fake compiles but tests behave oddly | Fake returns defaults for something the handler relies on | Make the fake honest, or configure it per test |
| Handler test needs a `DbContext` | It's depending on infrastructure | It should depend on an interface |
| Every refactor breaks the tests | Testing interactions rather than outcomes | Fakes over mocks |
| Can't test a private method | You shouldn't | Test the public behaviour |

---

## 14. Cheat sheet

### Commands

```bash
dotnet test                                                        # everything
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj
dotnet test --filter "FullyQualifiedName~EquipmentAssetTests"      # one class
dotnet test --filter "Name~compensat"                              # by name fragment
dotnet test -v n                                                   # per-test output
```

### Shapes

```csharp
// Domain test
[Fact]
public void Rule_is_enforced() =>
    Assert.Throws<DomainException>(() => Thing.Create(bad));

// Table of rejections
[Theory]
[InlineData("")]
[InlineData("nope")]
public void Invalid_input_throws(string value) =>
    Assert.Throws<DomainException>(() => Thing.Create(value));

// Handler test
[Fact]
public async Task Handler_does_the_thing()
{
    var (handler, thing, fake) = Build();

    var result = await handler.Handle(new DoThing.Command(thing.Id), default);

    Assert.Equal(thing.Id, Assert.Single(fake.Calls));
}

// Fake: seed, record, configure
internal sealed class FakeThingRepository : IThingRepository
{
    private readonly Dictionary<Guid, Thing> _things = new();
    public List<Thing> Added { get; } = new();
    public void Seed(Thing thing) => _things[thing.Id] = thing;
    public Task AddAsync(Thing t, CancellationToken ct) { Added.Add(t); _things[t.Id] = t; return Task.CompletedTask; }
    public Task<Thing?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(_things.TryGetValue(id, out var t) ? t : null);
}
```

### Assertions worth knowing

| Assertion | Use |
|---|---|
| `Assert.Single(collection)` | Exactly one — **returns it**, so you can assert further |
| `Assert.Empty(collection)` | Nothing happened |
| `Assert.Throws<T>(...)` | A rule rejected it |
| `await Assert.ThrowsAsync<T>(...)` | The async form — `await` it, or it never runs |
| `collection.OfType<TEvent>()` | Filter a mixed event list by type |

---

## 15. Glossary

| Term | Meaning |
|---|---|
| **Arrange / act / assert** | The three phases of a test. Keep them visually separate |
| **Domain test** | A test of a business rule, straight against the domain object |
| **Fake** | A hand-written working in-memory implementation of an interface |
| **`[Fact]`** | xUnit: a test with no parameters |
| **Flaky** | Passes and fails without the code changing. Worse than no test |
| **Handler test** | A test of one use case, with fakes for its dependencies |
| **Idempotency test** | Calling twice with the same key and asserting one effect |
| **Integration test** | A test against real infrastructure (here: EF Core + SQLite + HybridCache), not a full HTTP host. Reserved for wiring |
| **Interaction test** | Asserts which methods were called. Brittle; prefer outcomes |
| **Mock** | A framework-configured stand-in that records and verifies calls |
| **Stub** | A stand-in returning canned values, with no assertions of its own |
| **`[Theory]` / `[InlineData]`** | xUnit: one test run once per row of data |
| **Transition test** | Asserts behaviour on a state *change*, not a state |

---

## Where to go next

- **[Adding a feature](20-add-a-feature.md)** — the two tests every feature should ship with.
- **[Talking across modules](60-talking-across-modules.md)** — why the idempotency test is
  not optional.
