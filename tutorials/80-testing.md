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
| 9 | [Testing across modules](#9-testing-across-modules) | Events and idempotency |
| 10 | [Integration tests](#10-integration-tests) | The narrow, real use |
| 11 | [What not to test](#11-what-not-to-test) | Where effort is wasted |
| 12 | [The checklist](#12-the-checklist) | Run this when doing it for real |
| 13 | [Troubleshooting](#13-troubleshooting) | Symptom, cause, fix |
| 14 | [Cheat sheet](#14-cheat-sheet) | Patterns and commands |
| 15 | [Glossary](#15-glossary) | Every term used in this guide |

---

## 1. Why this codebase is easy to test

Because of the dependency rule, and that is the whole return on the architecture.

`Students.Domain` references nothing. `Students.Application` references only the domain and
some building blocks. Neither can touch EF Core or ASP.NET — so **neither needs them to run
in a test**.

```csharp
var student = Student.Create("Ada", "Lovelace", "ada@uni.edu", Dob, Enrolled);
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
| **Integration test** | The real host, wired up | The host | Very few |

The 162 tests in `tests/CleanArch.UnitTests/` are almost all the first two kinds.
`tests/CleanArch.Api.IntegrationTests/` holds a handful — and that ratio is deliberate, not
an omission.

**Why so few integration tests:** they are slow, they fail for reasons unrelated to your
change, and each one covers a thin path through a lot of code. They earn their place when
they test *wiring* — the thing unit tests structurally cannot see. Use them for that and
nothing else.

---

## 3. Step 1 — Test a rule

Domain tests are the cheapest tests you will ever write, and they test the most valuable
code. Go straight at the object:

```csharp
public class StudentTests
{
    private static readonly DateOnly Dob = new(1990, 1, 1);
    private static readonly DateOnly Enrolled = new(2024, 9, 1);

    [Fact]
    public void Create_with_valid_input_is_active_and_normalizes_email()
    {
        var student = Student.Create("Ada", "Lovelace", "  ADA@UNI.EDU ", Dob, Enrolled);

        Assert.Equal(StudentStatus.Active, student.Status);
        Assert.Equal("ada@uni.edu", student.Email);      // trimmed and lower-cased
        Assert.Equal("Ada", student.FirstName);
    }
}
```

Note it asserts the **normalisation**, not just that construction succeeded. Trimming and
lower-casing the email is a real behaviour someone will depend on; if it silently stops, a
test that only checked `Assert.NotNull(student)` would still pass.

For the rejection cases, a `[Theory]` keeps the table readable:

```csharp
[Theory]
[InlineData("", "L", "a@b.com")]
[InlineData("F", "", "a@b.com")]
[InlineData("F", "L", "")]
[InlineData("F", "L", "not-an-email")]
public void Create_with_invalid_input_throws(string first, string last, string email) =>
    Assert.Throws<DomainException>(() => Student.Create(first, last, email, Dob, Enrolled));
```

Four rules in four lines, and adding a fifth is one line.

Fixed dates as constants (`Dob`, `Enrolled`) matter more than they look: a test using
`DateTime.Now` passes today and fails on some future Tuesday, and nobody will know why.

---

## 4. Step 2 — Test a use case

A handler depends on interfaces, so a test hands it in-memory implementations and asserts on
what came out. No database, no mediator, no host — construct the handler and call `Handle`:

```csharp
[Fact]
public async Task Fine_crossing_threshold_enqueues_a_hold_for_the_student()
{
    var (handler, outbox, loan) = Build(priorTotal: 0m);

    var result = await handler.Handle(new AssessFine.Command(loan.Id, 25m), default);

    Assert.True(result.HoldRequested);
    var hold = Assert.Single(outbox.Events.OfType<StudentHoldRequested>());
    Assert.Equal(loan.StudentId, hold.StudentId);
    Assert.Single(outbox.Events.OfType<LibraryFineAssessed>());
}
```

Three things this demonstrates that are worth copying:

**It asserts on the event, not just the return value.** `HoldRequested` being true is the
handler's own claim about itself. `outbox.Events.OfType<StudentHoldRequested>()` is the
observable consequence — the thing another module will actually react to. Assert on
consequences.

**`Assert.Single` returns the item**, so you can go on to assert about it. Nicer than
`Assert.Equal(1, list.Count)` followed by `list[0]`, and the failure message is better.

**Both events are checked.** The fine is charged *and* a hold is requested — two separate
integration events with different consumers. A test that only checked the interesting one
would not notice if the other stopped being published.

You do **not** need the mediator. Behaviours — validation, transactions, audit — are tested
once, where they live. Re-testing them through every handler tests the framework, slowly.

---

## 5. Step 3 — Write a fake

A fake is a real, working, in-memory implementation of an interface. They live in
`tests/CleanArch.UnitTests/Fakes.cs`.

```csharp
internal sealed class FakeLoanRepository : ILoanRepository
{
    private readonly Dictionary<Guid, Loan> _loans = new();

    /// <summary>Value returned by GetFineTotalAsync — set per test.</summary>
    public decimal FineTotal { get; set; }

    public List<Loan> Added { get; } = new();

    public void Seed(Loan loan) => _loans[loan.Id] = loan;

    public Task AddAsync(Loan loan, CancellationToken cancellationToken)
    {
        Added.Add(loan);
        _loans[loan.Id] = loan;
        return Task.CompletedTask;
    }

    public Task<Loan?> GetAsync(Guid loanId, CancellationToken cancellationToken) =>
        Task.FromResult(_loans.TryGetValue(loanId, out var loan) ? loan : null);
}
```

Three moves, and every good fake has them:

| Move | Purpose | Here |
|---|---|---|
| **Seed** | Arrange preconditions | `Seed(loan)` |
| **Record** | Let the test assert on what happened | `Added` |
| **Configure** | Control a return value per test | `FineTotal { get; set; }` |

The simplest useful fake in the whole file is four lines:

```csharp
internal sealed class FakeOutbox : IOutbox
{
    public List<object> Events { get; } = new();

    public void Enqueue<TEvent>(TEvent integrationEvent) where TEvent : class => Events.Add(integrationEvent);
}
```

That one fake is what makes every outbox assertion in the suite possible.

A fake should be **honest**. `GetByStudentAsync` in the real `FakeLoanRepository` genuinely
filters, orders and pages, because a fake that ignores paging would let a paging bug through
untested. Where a fake cheats — `FineTotal` returns a fixed value rather than summing — that
is a deliberate choice to make the test's arrangement direct, and it is documented.

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
Fine_below_threshold_does_not_enqueue_a_hold()
Fine_crossing_threshold_enqueues_a_hold_for_the_student()
Fine_when_already_over_threshold_does_not_enqueue_again()
Create_with_dob_not_before_enrollment_throws()
```

Read them as sentences. When one fails in CI, the name alone tells you what broke — often
enough to know the cause without opening the file. Compare `TestAssessFine2`.

Underscores are used deliberately (the analyzer warning for them is suppressed in the test
project), because at this length they are far more readable than camel case.

### Factor the arrangement

```csharp
private static (AssessFine.Handler handler, FakeOutbox outbox, Loan loan) Build(decimal priorTotal)
{
    var loan = Loan.Borrow(Guid.NewGuid(), Guid.NewGuid(), Today, Due);
    var loans = new FakeLoanRepository { FineTotal = priorTotal };
    loans.Seed(loan);
    var outbox = new FakeOutbox();
    return (new AssessFine.Handler(loans, outbox), outbox, loan);
}
```

One helper returning a named tuple of everything the tests need. Each test then starts with
one line, and the parameter (`priorTotal`) is precisely the thing that varies between them.

**Why this matters:** when the handler gains a constructor parameter, you update one helper
rather than fifteen tests. That is the difference between tests that get maintained and tests
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

The `AssessFine` tests are a template. Three tests around one threshold:

| Test | Prior total | Asserts |
|---|---|---|
| Below the limit | 0 | No hold |
| **Crossing** the limit | 0 → 25 | Hold requested |
| **Already over** | 25 → 35 | No *second* hold |

The third is the one people forget, and it is the one that catches the real bug — publishing
on state rather than on the transition, which floods the queue.
[Guide 60](60-talking-across-modules.md#7-step-2--enqueue-it-atomically) explains why.

### Idempotency

Anything consuming an outbox message must handle redelivery. Test it by calling twice with
the same message id:

```csharp
[Fact]
public async Task Placing_the_same_hold_twice_records_it_once()
{
    var messageId = Guid.NewGuid();

    await service.PlaceHoldAsync(messageId, studentId, "reason", default);
    await service.PlaceHoldAsync(messageId, studentId, "reason", default);

    Assert.Single(holds.All);
}
```

If your consumer isn't idempotent, this is the only test that will tell you before production
does.

### Boundaries and rejections

Exactly at the limit. Zero. Empty collections. The state transition that should be a no-op —
`Withdraw()` is documented as idempotent, so withdrawing twice is a test.

And the rejection path in a saga: a hold for a withdrawn student must produce a
`StudentHoldRejected`, not a hold. That branch is where compensation begins, and it is
invisible on the happy path.

---

## 9. Testing across modules

You do not need both databases, or either. The seam between modules is an interface, so a
test fakes it:

```csharp
internal sealed class FakeStudentDirectory : IStudentDirectory
{
    private readonly StudentSummary? _summary;

    public FakeStudentDirectory(StudentSummary? summary) => _summary = summary;

    public Task<StudentSummary?> GetAsync(Guid studentId, CancellationToken cancellationToken) =>
        Task.FromResult(_summary);
}
```

Constructed with `null`, it tests "what happens when the student doesn't exist" — a case that
would otherwise need a second database in a specific state.

For the two sides of a cross-module write:

- **The publishing side** — assert the right event, with the right payload, was enqueued
  (`FakeOutbox`).
- **The consuming side** — test the contract implementation directly: does it do the work,
  is it idempotent, does it reject correctly?

The dispatcher in between is a `switch`. It is worth one test that an unknown type throws, so
the dead-letter path stays intact.

---

## 10. Integration tests

Reserve them for what unit tests structurally cannot see: **wiring**.

`tests/CleanArch.Api.IntegrationTests/` currently holds one — the On-Behalf-Of token exchange
flow, which spans an authentication handler, an HTTP message handler, a token cache and a
downstream call. No unit test can tell you those are correctly connected in the real
pipeline.

Good candidates:

- Authentication actually rejects an unauthenticated call to a protected endpoint
- A request produces the `X-Correlation-ID` response header
- Migrations apply cleanly from empty
- The pipeline order is right — a rejected command is still audited

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

Coverage percentage is a poor target. 100% coverage of getters with no test for the threshold
transition is worse than 60% with the edges covered.

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
- [ ] Any event enqueued is asserted, with its payload
- [ ] A `Build(...)` helper if more than two tests share arrangement

For anything **cross-module**:

- [ ] The publishing side asserts the right event on the transition
- [ ] The transition tested three ways: below, crossing, already over
- [ ] The consuming side tested directly
- [ ] **Called twice with the same message id** — idempotency
- [ ] The rejection branch, if it can reject

Generally:

- [ ] Test names read as sentences
- [ ] New fake methods behave honestly — real filtering, real paging
- [ ] `dotnet test` green before you push

---

## 13. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Test needs a database | Logic has leaked into infrastructure | Move the rule into the domain |
| Test passes alone, fails in a run | Shared mutable state between tests | Fresh fakes per test; xUnit makes a new class instance per test |
| Test failed today, passed yesterday | `DateTime.Now` somewhere | Fixed date constants |
| Constructor change broke fifteen tests | No `Build` helper | Factor the arrangement |
| Assertion passes but the bug ships | Asserting on the return value, not the consequence | Assert on what the outside world observes |
| Fake compiles but tests behave oddly | Fake returns defaults for something the handler relies on | Make the fake honest, or configure it per test |
| Handler test needs a `DbContext` | It's depending on infrastructure | It should depend on an interface |
| Every refactor breaks the tests | Testing interactions rather than outcomes | Fakes over mocks |
| Can't test a private method | You shouldn't | Test the public behaviour |

---

## 14. Cheat sheet

### Commands

```bash
dotnet test                                                    # everything
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj
dotnet test --filter "FullyQualifiedName~StudentTests"         # one class
dotnet test --filter "Name~threshold"                          # by name fragment
dotnet test -v n                                               # per-test output
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
    var (handler, outbox, thing) = Build();

    var result = await handler.Handle(new DoThing.Command(thing.Id), default);

    Assert.Single(outbox.Events.OfType<ThingHappened>());
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
| **Integration test** | A test through the real host. Reserved for wiring |
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
