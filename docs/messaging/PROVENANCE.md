# Messaging: where this code came from

The RabbitMQ messaging code in this repository is a **copy** of
[ncowine/RabbitMQ-Refactor](https://github.com/ncowine/RabbitMQ-Refactor) at commit
`5a605a1c96b4755327e9f3e63c2c40ac16a85019` (2026-09-27). It replaced this repository's earlier `src/Common.RabbitMQ`,
whose envelope wire format wasn't compatible with the legacy apps.

It's a copy, not a package, because this solution is meant to live next to legacy .NET Framework projects: they
reference the adapter by relative path (`src/Common.RabbitMQ`), so the source repository's layout is kept.

## What was copied

| Here | From | Changes |
|---|---|---|
| `src/Messaging/Messaging.Abstractions`, `.RabbitMQ`, `.Hosting`, `.Prism` | same paths | none |
| `src/Common.RabbitMQ`, `src/Common.RabbitMQ.Configuration` | same paths | none |
| `tests/Common.RabbitMQ.Tests` | same path | two project references point at the relocated fixtures below |
| `tests/Fixtures/Common.RabbitMQ.Baseline`, `Compat.Events`, `Compat.Events.Baseline`, `LegacyModel/*` | same paths | none |
| `tests/Fixtures/Common.Events` | `src/Common.Events` | moved to test fixtures; path to `Common.RabbitMQ` updated |
| `tests/Fixtures/Employees.Contracts` | `src/Contracts/Employees.Contracts` | moved to test fixtures; path to `Messaging.Abstractions` updated |
| `docs/messaging/adr/0001-*.md`, `0002-*.md`, `docs/messaging/legacy-baseline-assumptions.md` | `docs/` | none |
| `docs/messaging/README.md` | `README.md` | links adjusted; a note on what isn't here |

`Common.Events` and `Employees.Contracts` are the source repository's **demo** messages. They are test fixtures
here only, kept with their original namespaces and assembly names so wire names and the golden files stay
byte-for-byte identical. This solution's own messages go in each module's `*.Contracts` project as plain classes
with `[Message("...")]`, referencing only `Messaging.Abstractions`:

- a message a legacy app must receive uses that legacy event's full type name as its wire name;
- a new message gets an explicit wire name that doesn't depend on its namespace.

## What was not copied

- The demo apps: `WpfApp.Net472`, `WpfApp.Net8`, `WpfApp.Modern` and `WebApi`. `CleanArch.Api` is the server
  here.
- The source repository's CI workflow, `CLAUDE.md` and pull request template. Their rules are summarised below.

## Build isolation

`src/Messaging/Directory.Build.props` and `src/Messaging/Directory.Packages.props` hold the settings for every copied
project. The other copied folders (`src/Common.RabbitMQ`, `src/Common.RabbitMQ.Configuration`,
`tests/Common.RabbitMQ.Tests`, `tests/Fixtures`) have small files that import them. None of them import the
repository root's files, because the root sets things the legacy-compatible code must not get:

| Root setting | Messaging setting | Why |
|---|---|---|
| `TargetFramework` net10.0 | each project's own `TargetFrameworks` (net472, netstandard2.0, net8.0) | legacy apps load net472 builds |
| `Nullable`, `ImplicitUsings` enabled | disabled | the code is written without them; implicit usings can change name resolution on net472 |
| `TreatWarningsAsErrors`, Recommended analyzers | off | matches the source repository |
| RabbitMQ.Client 7.2.2 | **7.1.2** | shared with the legacy apps (assumption F4) |
| xunit 2 | xunit.v3 | the copied tests use v3 |

The root `Directory.Packages.props` no longer pins any messaging package.

## Rules that came with the code

- `src/Common.RabbitMQ` (the legacy adapter): **additions only.** A new public member must be added to
  `ApprovedAdditions` in `tests/Common.RabbitMQ.Tests/PublicApi/PublicApiTests.cs`, with its reason. Keep exactly one
  public constructor on `RabbitMQService` and `RabbitMQServiceRouter` (DryIoc).
- Never edit `tests/Fixtures/Common.RabbitMQ.Baseline` or regenerate `Golden/*.json` to make a test pass.
- The legacy model (`tests/Fixtures/LegacyModel`) changes only together with
  `docs/messaging/legacy-baseline-assumptions.md` and the test named after the assumption.
- Design changes get an ADR in `docs/messaging/adr/`, or an update to the relevant one.
- Run the tests against a real broker with `RABBITMQ_TESTS_REQUIRED=1`. Both net472 and net8.0 must pass.
- Changes that affect anything in `docs/messaging/README.md` update it in the same change.

## Changes since the copy

None yet. Record each change to the copied code here (what and why), so a later comparison with the source
repository is easy.
