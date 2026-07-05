# Tester Guide — design doc (new modules on the reusable kit)

> **Status:** proposal for review. No code written yet. This maps the "Tester Guide" use case onto
> the existing modular-monolith kit (`BuildingBlocks.*`) and identifies the one genuinely new
> building block it needs (real-time presence/notifications).

## 1. The use case in one breath

Two databases:

1. **Test Plans DB** — the *system of record*. Holds the test content and its own results/history:
   `Test Plan → Category → Sub-category → Task`, its own **version + sub-version**, and each task's
   **result** (`CheckedOut | Pass | Fail | Skip`) recorded **per platform variation, per version/sub-version**.
   In plain terms: *a task is actioned for a (platform, version, sub-version)*. Has its own **action log**.
2. **Tester Guide DB** — a *new app layered on top* of the tasks, so we never modify the primary DB.
   Holds **guide configs** (a config dedicates a test plan + a specific version, picks a **focus**, assigns
   **users**, is **single- or multi-player**), **config templates**, **focus manager**, a **content manager**
   (mark which tasks/plans are relevant to the tool), its own **action log** that can optionally **sync back**
   to the primary DB's action log, **time tracking**, **reporting**, and a **live view** where admins see who's
   working on which config and everyone sees when a task is actioned by someone else in the same version/sub-version.

## 2. Why this fits the existing architecture almost exactly

This is the same shape as the shipped `Students` (system of record) + `Library` (new app keyed by
`StudentId`) example. The mapping:

| Tester Guide | Existing analog | Role |
|---|---|---|
| **Test Plans DB** | `Students` | System of record; referenced by key, not modified lightly. |
| **Tester Guide DB** | `Library` | New app; own DB, keyed to the SoR by `TestPlanId`/`VersionId`/`TaskId`. |
| "sync guide action → primary action log" | `StudentHoldRequested` outbox flow | Cross-DB write → **outbox** (can't be one transaction). |
| primary rejects a synced action | `StudentHoldRejected` → `IFineWaiver` | **Saga** reverse leg (compensation). |
| content manager (enable tasks for the tool) | reference-by-key seam | Metadata overlay on SoR rows, keyed by id. |
| **live view / "someone actioned this"** | *nothing* | ⚠️ **New building block** (SignalR presence). |

**Decisions locked for this doc** (from our discussion): Test Plans is built as a **thin stand-in module**
(its own DB + a small seed) so the whole POC runs end-to-end; **real-time is in scope now** as a new
reusable building block; this **design doc comes first**, before code.

## 3. Module & project layout (two new modules)

Mirrors the existing `Modules/<Name>/<Name>.{Domain,Application,Infrastructure,Contracts,Presentation}` shape.

```
src/Modules/
  TestPlans/                 System of record (stand-in for the real primary system)
    TestPlans.Domain/            TestPlan, Category, SubCategory, TestTask, Platform,
                                 TestPlanVersion, TaskResult, ActionLogEntry, status enums
    TestPlans.Application/       vertical slices + abstractions (repos, read services)
    TestPlans.Infrastructure/    EF Core (TestPlansDbContext), repos, read services, seed
    TestPlans.Contracts/         ITestPlanCatalog, ITaskResultReader, ITestPlanActionLog  ← the seam
    TestPlans.Presentation/      minimal-API endpoints (author/seed content, read tree)
  TesterGuide/               The new app (metadata layered on Test Plans)
    TesterGuide.Domain/          GuideConfig, Focus, ConfigTemplate, ConfigAssignment,
                                 ContentSelection, GuideActionLogEntry, TimeEntry, enums
    TesterGuide.Application/      slices + abstractions + outbox messages
    TesterGuide.Infrastructure/   EF Core (TesterGuideDbContext), repos, read services,
                                 outbox dispatcher, realtime publishing
    TesterGuide.Contracts/        IGuideActionReconciler (saga reverse-leg target)
    TesterGuide.Presentation/     minimal-API endpoints + SignalR hub map

src/BuildingBlocks.RealTime/   NEW reusable block: IRealtimeNotifier, presence, post-commit dispatch
```

> **Naming note:** the entity is `TestTask`, **not** `Task` — `System.Threading.Tasks.Task` would shadow it
> everywhere. Databases: `testplans.db`, `testerguide.db`. Connection strings: `TestPlans`, `TesterGuide`.

## 4. Test Plans module (system of record)

### Domain
- `TestPlan` (Id, Name, Code) → `Category` (Id, TestPlanId, Name, Order) → `SubCategory`
  (Id, CategoryId, Name, Order) → `TestTask` (Id, SubCategoryId, Name, Description, Mode: `SinglePlayer|Multiplayer`).
- `Platform` (Id, Name) — the variation dimension (e.g. *PC, Xbox, PS5*).
- `TestPlanVersion` (Id, TestPlanId, Version, SubVersion) — the plan's own versioning.
- `TaskResult` (Id, TestTaskId, PlatformId, TestPlanVersionId, Status, ActorId, ActionedOnUtc) —
  **current status** for a (task, platform, version/sub-version). `Status = CheckedOut | Pass | Fail | Skip`.
- `ActionLogEntry` (Id, TestTaskId, PlatformId, TestPlanVersionId, Status, ActorId, OccurredOnUtc, Source) —
  append-only history; **this is the sync target** the guide writes into.

### Published contracts (the only way Tester Guide may touch it)
```csharp
// Read the content tree + versions/platforms (compose configs, render the guide).
public interface ITestPlanCatalog {
    Task<TestPlanTree?>       GetTreeAsync(Guid testPlanId, CancellationToken ct);
    Task<IReadOnlyList<VersionSummary>>  GetVersionsAsync(Guid testPlanId, CancellationToken ct);
    Task<IReadOnlyList<PlatformSummary>> GetPlatformsAsync(CancellationToken ct);
    Task<bool> VersionExistsAsync(Guid testPlanId, Guid versionId, CancellationToken ct);
}
// Read current status from the source of truth (e.g. "already Pass on another platform").
public interface ITaskResultReader {
    Task<TaskStatusSnapshot?> GetAsync(Guid taskId, Guid platformId, Guid versionId, CancellationToken ct);
}
// WRITE target for the sync — idempotent by messageId (the outbox message id). Analog of IStudentHoldService.
public interface ITestPlanActionLog {
    Task RecordActionAsync(Guid messageId, RecordActionInput input, CancellationToken ct);
}
```
`RecordActionAsync` is where the saga's forward leg lands: it appends an `ActionLogEntry` and updates the
`TaskResult` current status **in one Test Plans-DB transaction**, keyed idempotently by `messageId`. If the
task/version/platform no longer exists or the version is frozen, it **rejects** — enqueuing a
`MainDbActionRejected` event in the **Test Plans outbox** (reverse leg), exactly like `StudentHoldService`.

## 5. Tester Guide module (the app)

### Domain / tables (all in `testerguide.db`)
- `GuideConfig` (Id, Name, **TestPlanId**, **TestPlanVersionId**, FocusId, Mode `SinglePlayer|Multiplayer`,
  **SyncEnabled**, Status, CreatedBy). References the SoR only by id.
- `Focus` (Id, Name, Description) — CRUD via the focus manager.
- `ConfigTemplate` (Id, Name, FocusId?, Mode, SyncEnabled, defaults) — pre-fills a config.
- `ConfigAssignment` (Id, GuideConfigId, UserId, DisplayName, Role) — who's assigned; drives single/multi-player.
- `ContentSelection` (Id, TestPlanId, TestTaskId?, IsEnabled) — **content manager**: metadata overlay keyed by
  SoR ids, enabling which tasks/plans are relevant to the tool. *No SoR write — this lives entirely in DB2.*
- `GuideActionLogEntry` (Id, GuideConfigId, TestTaskId, PlatformId, TestPlanVersionId, Status, UserId,
  OccurredOnUtc, SyncState `NotSynced|Pending|Synced|Rejected`).
- `TimeEntry` (Id, GuideConfigId, UserId, TestTaskId?, StartedUtc, EndedUtc?) — time tracking.
- Outbox table (shared component) for the sync-to-SoR flow.

`Users` are the authenticated principals — reuse the existing `ICurrentActor`; assignments store the id + name.

### Published contract (saga reverse-leg target)
```csharp
public interface IGuideActionReconciler {   // implemented in TesterGuide.Infrastructure
    Task MarkSyncRejectedAsync(Guid messageId, Guid guideActionId, string reason, CancellationToken ct);
}
```

## 6. The three flows that exercise the kit

### (a) Cross-module READ (synchronous) — build/render a config
`POST /guide/configs` validates `TestPlanId` + `TestPlanVersionId` via `ITestPlanCatalog` (the
`BorrowBook`-validates-student analog) before writing the config in DB2. `GET /guide/configs/{id}`
composes the DB2 config + enabled `ContentSelection` with the DB1 task tree + current `TaskResult`
statuses — **in the application layer, never a cross-DB join.**

### (b) Cross-DB WRITE via outbox + saga — action a task with sync on
```
Tester actions a task (Pass/Fail/Skip/CheckedOut) in a config
  └─ RecordAction handler (TesterGuide, ITesterGuideCommand):
       • append GuideActionLogEntry (DB2)                         ┐ one DB2 transaction
       • if config.SyncEnabled → outbox.Enqueue(MainDbActionRequested)  ┘ (atomic)
  └─ OutboxProcessor<TesterGuideDbContext> → TesterGuideOutboxDispatcher
       • MainDbActionRequested → ITestPlanActionLog.RecordActionAsync(messageId, …)   [forward leg]
           – appends ActionLogEntry + updates TaskResult in DB1 (idempotent by messageId)
           – on reject → enqueues MainDbActionRejected in TestPlans outbox              [reverse leg]
  └─ TestPlansOutboxDispatcher
       • MainDbActionRejected → IGuideActionReconciler.MarkSyncRejectedAsync(…)         [compensation]
           – flags the GuideActionLogEntry SyncState = Rejected (+ realtime notify)
```
This is a byte-for-byte reuse of the shipped fine→hold→rejection→waiver saga, now with a real reason to
exist (keeping two independently-owned action logs eventually consistent). Dead-letter + replay come free
from `AddOutboxAdmin<TesterGuideDbContext>()`.

### (c) Real-time (NEW) — live view + "someone actioned this"
When `RecordAction` commits, everyone else viewing the same **(config, version, sub-version)** must see it
immediately, and admins watching a config must see **who is working on it**. Nothing in the current kit
pushes to clients, so we add one small reusable block.

## 7. New building block: `BuildingBlocks.RealTime`

**Abstraction (kit, no SignalR dependency)** — handlers publish without knowing the transport:
```csharp
public interface IRealtimeNotifier {
    Task NotifyGroupAsync(string group, RealtimeEvent evt, CancellationToken ct);
}
public sealed record RealtimeEvent(string Type, object Payload);
```
**Post-commit dispatch** — realtime events must only fire for **committed** work. A tiny outermost pipeline
behavior collects events raised during a request (via a scoped collector) and flushes them **after** the
inner transaction behavior commits. This is a clean, reusable pattern (post-commit side effects) and avoids
notifying about a write that later rolls back. *(MVP shortcut if we want to defer the behavior: publish from
the endpoint after `sender.Send` returns — best-effort, same effect for the POC.)*

**SignalR implementation (host):**
- `PresenceHub` at `/hubs/presence`. Clients join groups: `config:{id}` (admin live view) and
  `config:{id}:v:{versionId}:{subVersion}` (peers on the same version/sub-version).
- `SignalRRealtimeNotifier : IRealtimeNotifier` wraps `IHubContext<PresenceHub>`.
- Presence registry (who's connected to which config) — in-memory for the POC; **one-line switch to a Redis
  backplane** for multi-node, mirroring the HybridCache→Redis story already in the README.

Events emitted: `TaskActioned {taskId, platformId, versionId, status, byUser}`, `PresenceChanged {configId, users[]}`,
`SyncRejected {guideActionId, reason}`.

## 8. Endpoints (representative)

| Method | Route | Auth | Notes |
|---|---|---|---|
| POST | `/testplans` … (+ categories/tasks/versions/platforms) | ✅ | Stand-in authoring/seed for the SoR |
| GET | `/testplans/{id}/tree` | — | Content tree (also used via contract internally) |
| POST | `/guide/focuses` · PUT · DELETE · GET | ✅/— | Focus manager |
| POST | `/guide/templates` · GET | ✅/— | Config templates |
| POST | `/guide/configs` | ✅ | Create (validates plan+version in DB1) |
| POST | `/guide/configs/from-template/{templateId}` | ✅ | Create from template |
| POST | `/guide/configs/search` | — | Paged (paging/filters in body) |
| GET | `/guide/configs/{id}` | — | Composes DB2 config + DB1 tree + current statuses |
| POST | `/guide/configs/{id}/assignments` | ✅ | Assign users |
| POST | `/guide/content/tasks/{taskId}/enable` (`/disable`) | ✅ | Content manager overlay |
| POST | `/guide/configs/{id}/actions` | ✅ | Record action → sync (if on) + realtime notify |
| POST | `/guide/configs/{id}/time/start` · `/stop` | ✅ | Time tracking |
| POST | `/guide/reports/config/{id}` | — | Reporting projection (DB2 + DB1) |
| GET | `/guide/configs/{id}/live` | — | Presence snapshot (push via `/hubs/presence`) |
| GET | `/guide/outbox/dead-letter` · replay | —/✅ | Reuse outbox admin |
| — | `/hubs/presence` (SignalR) | — | Live view + task-actioned push |

## 9. Kit improvements this surfaces

1. **`BuildingBlocks.RealTime`** — the new block above (biggest, committed).
2. **Outbox exponential backoff** — README lists the fixed-2s poll as a known gap; the sync flow makes it
   matter (the primary system can be transiently unavailable). Good candidate to add generically.
3. **Optional: cache the mediator's per-request reflection** (`Sender`) — only if it shows in a profile.
4. Everything else (paging, read-side conventions, transaction behavior, outbox writer/admin, auth,
   observability, health) is reused **as-is** — the point of the exercise.

## 10. Proposed build order (each phase compiles, migrates, and is testable)

- **Phase 1 — Test Plans stand-in.** Domain + `TestPlansDbContext` + migration + a small seed (1 plan,
  a few categories/tasks, 2 platforms, 2 versions). Publish `ITestPlanCatalog`/`ITaskResultReader`/
  `ITestPlanActionLog`. Wire `.AddTestPlansModule(cs)` into `Program.cs`; migrate in dev setup.
- **Phase 2 — Tester Guide skeleton.** `GuideConfig` + focus + template + assignments + content manager
  (pure DB2), plus the cross-module read on config create/render. Endpoints + unit tests for domain invariants.
- **Phase 3 — Action recording + outbox sync + saga.** `RecordAction`, `MainDbActionRequested` →
  `ITestPlanActionLog`, reject → `MainDbActionRejected` → `IGuideActionReconciler`. Dead-letter/replay.
  Handler + integration tests for the round-trip.
- **Phase 4 — Real-time.** `BuildingBlocks.RealTime` + `PresenceHub` + post-commit dispatch; live view and
  "someone actioned this" push.
- **Phase 5 — Time tracking + reporting.** Slices + read projections.
- **Phase 6 — Polish.** README/docs update, optional desktop-client screens, outbox backoff.

## 11. Resolved decisions (from review)
1. **Focus** — a **named label** (CRUD only): name + description attached to a config for organization
   and reporting. It does **not** filter content.
2. **Users** — **auth principals**: assignments store the principal's id + display name (no roster table
   in DB2); the actor comes from the existing `ICurrentActor`.
3. **Test Plans stand-in fidelity** — **minimal**: a small tree, 2 platforms, 2 versions, and the
   action-log write target — just enough to demo the sync/saga/real-time story.
4. **Sync "rejection"** (saga reverse leg) — the primary rejects when the **task or version no longer
   exists**; a redelivery of an accepted action is a no-op (idempotent by message id).
5. **Sub-version** — a flat `(Version, SubVersion)` pair on `TestPlanVersion` (no separate lifecycle).
