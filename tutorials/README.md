# Tutorials

Guides to the patterns this codebase is built from — written to be **followed**, not just
read. Each one answers *"how do I do X, and why is it done this way?"* for one topic, and
uses this repository only as the worked example. The aim is that you could apply any of them
to a different codebase.

This folder is self-contained and is the current documentation — the older `docs/` folder has
been reduced to two operational guides with no home here:
[build and packages](../docs/build-and-packages.md) and [deploying to IIS](../docs/deploy-iis.md).

---

## Start here

New to the codebase? Read **10**, then **20**, and stop. That's enough to add features.
Come back for the rest when you hit the problem it solves.

| | Guide | Read it when |
|---|---|---|
| 10 | [Clean architecture foundations](10-clean-architecture-foundations.md) | You want to know why the code is arranged this way, and where new code goes |
| 20 | [Adding a feature](20-add-a-feature.md) | You're adding an endpoint or an operation to a module that already exists |
| 30 | [Adding a new module](30-add-a-module.md) | The work needs its own boundary, its own database, its own vocabulary |
| 40 | [Auditing — who changed what](40-auditing.md) | You need a defensible record of every write, separate from your logs |
| 45 | [Caching — cache-aside with HybridCache](45-caching.md) | A hot read is hitting the database every time and doesn't need to |
| 50 | [Instrumenting an application](50-instrumenting-an-application.md) | The app is a black box anywhere but your machine |
| 60 | [Talking across modules](60-talking-across-modules.md) | You need data — or a write — from another module, and found out you can't just do it |
| 65 | [Real-time notifications](65-real-time-notifications.md) | Connected clients need to see a change the moment it happens, without polling |
| 70 | [Authentication and the audit actor](70-authentication.md) | You need to know who is calling, and they don't all authenticate the same way |
| 80 | [Testing](80-testing.md) | You want tests that catch bugs and survive refactoring |
| 90 | [Observability server on Ubuntu](90-observability-server-ubuntu.md) | You're building the box that collects telemetry, from a blank Ubuntu install |
| 95 | [Reading your telemetry](95-reading-your-telemetry.md) | The stack is up, something is wrong, and you need to find out what |

### By task

| I need to… | Guide |
|---|---|
| Understand the layers | [10](10-clean-architecture-foundations.md) |
| Add an endpoint | [20](20-add-a-feature.md) |
| Add a paged list endpoint | [20 §11](20-add-a-feature.md#11-paged-lists) |
| Create a whole new area of the system | [30](30-add-a-module.md) |
| Record who changed what | [40](40-auditing.md) |
| Cache a hot read, and invalidate it correctly | [45](45-caching.md) |
| Add a metric for my own module | [50 §7](50-instrumenting-an-application.md#7-step-4--add-your-own-metric) |
| Read another module's data, or trigger a simple action in it | [60 §3](60-talking-across-modules.md#3-reads-and-simple-actions--published-contracts) |
| Share small, read-only data across modules without making it a module | [30 §1](30-add-a-module.md#when-not-to) / [60 §2](60-talking-across-modules.md#2-two-sanctioned-routes) |
| Build a multi-step process that survives a restart | [60 §4](60-talking-across-modules.md#4-why-a-multi-step-process-needs-the-outbox) onward |
| Undo a step when a later one fails | [60 §12](60-talking-across-modules.md#12-compensation--the-saga-unwinding-itself) |
| Push a live update to connected clients | [65](65-real-time-notifications.md) |
| Issue an API key | [70 §4](70-authentication.md#4-step-1--api-keys) |
| Protect an endpoint | [70 §8](70-authentication.md#8-step-4--protect-an-endpoint) |
| Test a rule, or a handler | [80](80-testing.md) |
| Stand up Grafana, Loki, Tempo, Prometheus, Elasticsearch, Kibana | [90](90-observability-server-ubuntu.md) |
| Triage an incident from the dashboard | [95 §2](95-reading-your-telemetry.md#2-the-sixty-second-triage) |
| Work out whether it's us or a dependency | [95 §10](95-reading-your-telemetry.md#10-eighteen-real-scenarios) |
| Follow one request through logs, traces and the audit trail | [95 §9](95-reading-your-telemetry.md#9-the-golden-thread--one-request-across-all-four) |
| Write a PromQL / LogQL / TraceQL query | [95 §§5–7](95-reading-your-telemetry.md#16-cheat-sheet) |
| Get alerted instead of watching a dashboard | [95 §13](95-reading-your-telemetry.md#13-the-four-alerts-worth-having) |

### Threads that run through several guides

Some things are deliberately covered from more than one angle, because they matter in more
than one place:

- **The audit actor** is produced in [70](70-authentication.md) and consumed in
  [40](40-auditing.md). Neither guide is complete without the other — an audit trail is only
  as trustworthy as the authentication behind it.
- **Idempotency** appears in [60](60-talking-across-modules.md) as a design requirement and
  in [80](80-testing.md) as a test you must write.
- **Correlation ids** are set up in [50](50-instrumenting-an-application.md), are what
  makes a [60](60-talking-across-modules.md) saga traceable across its async hop, and are
  the thread you actually pull on in [95](95-reading-your-telemetry.md).
- **Telemetry** is emitted in [50](50-instrumenting-an-application.md), stored by
  [90](90-observability-server-ubuntu.md) and *read* in
  [95](95-reading-your-telemetry.md). The last of the three is the one that pays for the
  other two.
- **One follow-up message per outcome** — never publish twice, never publish nothing — is
  stated in [60](60-talking-across-modules.md) and the saga's transitions (each step
  succeeding, each step failing) are tested in [80](80-testing.md).
- **The caching decorator shape** shows up twice — over a database read in
  [45](45-caching.md) and over API key validation in
  [70](70-authentication.md#4-step-1--api-keys). Same pattern, two different reasons to
  reach for it.

---

## How these guides are structured

Every guide follows the same shape, so you can predict where to look:

1. **Who it's for and what you'll be able to do** — stated up front, so you can leave early.
2. **A table of contents** with a column saying what you actually *do* in each chapter.
3. **Why before how.** The reasoning comes first; a step you don't understand is a step
   you'll undo later.
4. **Numbered chapters**, in the order you'd do the work.
5. **A worked example from this repo** — real file paths you can open alongside.
6. **A checklist** you can run down when doing it for real.
7. **Troubleshooting** — symptom, cause, fix.
8. **A cheat sheet and a glossary**, so you can return to a guide without re-reading it.

Code blocks are complete enough to use. Where a guide shows a file, it is the real file from
this repository, not a simplified version — simplified samples are how tutorials quietly
stop compiling.

## Conventions used throughout

| Convention | Meaning |
|---|---|
| `src/Modules/Equipment/…` | A real path in this repository — open it |
| **Why this matters** | The reasoning behind a step. These mark the places where the *wrong* choice still compiles and still returns `201` |
| > A note block | A gotcha, a caveat, or an honest limitation |
| ✅ / — | In endpoint tables: authorization required / open |

The worked examples come from two modules — `Equipment` and `Onboarding`. When a guide needs
a *simple, build-it-from-scratch* example it uses `Equipment`; when it needs a *multi-step,
crosses-into-another-module* one it uses `Onboarding`, which also happens to be the only
module with an outbox.

## Not covered here

Honest gaps, so you don't go looking:

- **Deploying the API itself.** [`../docs/deploy-iis.md`](../docs/deploy-iis.md) covers IIS.
- **Build and package plumbing.** [`../docs/build-and-packages.md`](../docs/build-and-packages.md)
  covers `Directory.*.props`, Central Package Management and NU1507.
