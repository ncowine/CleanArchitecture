# Reading Your Telemetry — Finding the Fault

The stack is up. Metrics, logs, traces and the audit trail are all arriving. Now something is
wrong — or somebody says it is — and you have four websites open and no idea which one to
look at.

This guide is the missing half of [50](50-instrumenting-an-application.md) and
[90](90-observability-server-ubuntu.md). Those two get the data flowing. This one is about
**reading it**: what each number means, which panel answers which question, how to get from
"the API is broken" to a file and a line, and how to tell the difference between a real
outage and a telemetry glitch that only looks like one.

**Who this is for.** You can already reach Grafana and Kibana and see data in them. You do
not need to know PromQL, LogQL, TraceQL or KQL — every query here is given in full and
explained.

**What you will be able to do.** Triage an incident in sixty seconds. Say confidently
whether the fault is your code, a dependency, the host, or the caller. Follow one request
from a customer complaint through logs, traces and the audit trail. And know what your
telemetry cannot tell you, which is the part people skip.

---

## Table of contents

| | Chapter | What you do in it |
|---|---|---|
| 1 | [Four stores, four questions](#1-four-stores-four-questions) | Learn which tool answers which question, so you stop guessing |
| 2 | [The sixty-second triage](#2-the-sixty-second-triage) | Run the routine that turns panic into a direction |
| 3 | [The dashboard, section by section](#3-the-dashboard-section-by-section) | Read every panel and know what good and bad look like |
| 4 | [Reading a number without fooling yourself](#4-reading-a-number-without-fooling-yourself) | Understand rates, percentiles and buckets before you trust them |
| 5 | [Metrics — the PromQL you actually need](#5-metrics--the-promql-you-actually-need) | Write your own queries in Explore |
| 6 | [Logs — reading Loki properly](#6-logs--reading-loki-properly) | Search by field, not by eyeball |
| 7 | [Traces — reading a waterfall](#7-traces--reading-a-waterfall) | See where a request's time went and which span failed |
| 8 | [The audit trail — reading Kibana](#8-the-audit-trail--reading-kibana) | Answer "who changed this" and "who looked" |
| 9 | [The golden thread — one request across all four](#9-the-golden-thread--one-request-across-all-four) | Follow a single request end to end |
| 10 | [Eighteen real scenarios](#10-eighteen-real-scenarios) | Work through the failures you will actually meet |
| 11 | [When the telemetry is the problem](#11-when-the-telemetry-is-the-problem) | Recognise a fake outage before you page anyone |
| 12 | [What this tells you — and what it does not](#12-what-this-tells-you--and-what-it-does-not) | Know the blind spots, and close them |
| 13 | [The four alerts worth having](#13-the-four-alerts-worth-having) | Stop watching a dashboard you do not need to watch |
| 14 | [Checklist](#14-checklist) | Run down this list during a real incident |
| 15 | [Troubleshooting](#15-troubleshooting) | Fix the dashboard and the queries themselves |
| 16 | [Cheat sheet](#16-cheat-sheet) | Come back to this page and nothing else |
| 17 | [Glossary](#17-glossary) | Look up a word without leaving |

---

## 1. Four stores, four questions

Four stores, and each answers exactly one kind of question. Most wasted time in an incident
is somebody asking a store a question it structurally cannot answer.

| Store | UI | Answers | Cannot answer |
|---|---|---|---|
| **Prometheus** | Grafana dashboards | *How much? How often? How fast?* — aggregates over all requests | Anything about **one** request. There is no "which user" in a metric |
| **Loki** | Grafana Explore | *What happened, in order?* — the narrative of a request | *How many?* accurately over long ranges, and anything older than retention |
| **Tempo** | Grafana Explore | *Where did the time go?* — the timeline of one request across components | Aggregates. It is a per-request store, and it may be sampled |
| **Elasticsearch** | Kibana | *Who did what to which record, and who looked?* — the legal record | Performance. It is an audit trail, not a profiler |

The rule that keeps you out of trouble:

> **Metrics find the problem. Traces locate it. Logs explain it. The audit trail proves it.**

You go in that order, and you almost never go backwards. Starting in the logs during an
outage is the most common mistake: you are reading a firehose with no idea what you are
looking for, and the answer to "is this line unusual?" is only in the metrics.

### Why not one tool

A metric is a counter — cheap enough to keep every one, forever, but it has no idea who you
are. A log line carries everything but costs a thousand times more per request. A trace has
the shape of the request but is often sampled. Each is a different trade of **detail against
cost**, and that is why the split exists. See
[50 §1](50-instrumenting-an-application.md#1-three-signals-three-questions) for the longer
version.

---

## 2. The sixty-second triage

Open **Grafana → CleanArch.Api — Service Overview**. Do these in order. Do not skip ahead
because you have a hunch; the whole point of the order is that a hunch is usually wrong.

**0–10s — Set the time range.** Default is the last 30 minutes. If the complaint is "it was
broken an hour ago", the dashboard is showing you a healthy service and you will conclude
nothing is wrong. **Always set the range to the reported time, plus twenty minutes either
side.** More incidents are misdiagnosed by the time picker than by any query.

**10–20s — Read section ①.** Twelve tiles. If they are all green, nothing is broken *right
now*, and the question becomes "was it, and when?" — go to section ② and look for the moment
the colours changed.

**20–30s — Which tile is red?** Each one sends you somewhere specific:

| Red tile | It means | Go to |
|---|---|---|
| Success rate / 5xx per sec | Requests are failing | ③ then ④ |
| Latency p95 / p99 | Requests are slow | ② heatmap, then ③, then ④ slow traces |
| In flight | Requests are piling up — saturation | ⑦ thread pool, then ⑤ dependencies |
| 4xx per sec | Callers are being rejected | ② status split, then ⑤ auth / rate limiting |
| Exceptions per sec | Something is throwing | ④ exceptions by type |
| Outbound failures | A dependency or a telemetry store is unreachable | ⑤ dependencies |
| CPU / Memory / Thread-pool queue | The host or the runtime is struggling | ⑦ runtime health |

**30–45s — Narrow it to one endpoint.** Section ③ is a table of every route sorted by error
rate. "The API is broken" becomes "`POST /students` is broken", which is a completely
different and much smaller problem.

**45–60s — Find out why.** Section ④: the errors and warnings log on the left, the failed
traces on the right. Open one failed trace, read the exception on the span, then read the log
lines for that trace id.

At sixty seconds you should be able to finish this sentence: *"`POST /students` started
returning 500 at 14:32, throwing `SqliteException`, and it began two minutes after the
deploy."* That sentence is the handover. Everything after it is ordinary debugging.

---

## 3. The dashboard, section by section

Every panel carries the same explanation in its **ⓘ** tooltip — hover the panel title in
Grafana and you get what is written below, in place. This chapter is that guidance collected
in one readable order.

Three controls sit at the top of the dashboard:

| Control | What it does |
|---|---|
| **Route** | Narrows every metric panel to one or more endpoints. Set it once and the whole dashboard is about that route |
| **Slow threshold** | The cut-off used by the "Slow requests" trace search (100ms … 5s) |
| **Slow query** | The cut-off used by the "Slow database queries" panel (1ms … 1s) |
| **Find request** | Paste a trace id or correlation id here, then open section ⑧ |

### ① Is it healthy right now?

Twelve tiles. The design rule is that **all green means stop looking** — if you cannot walk
away when they are green, the thresholds are wrong and you should change them.

| Tile | Good | Bad, and what it means |
|---|---|---|
| **Success rate** | ≥ 99.9% | The share of requests that were *not* 5xx. 4xx deliberately does not count — a rejected bad request is the API working |
| **Requests/sec** | *(no good value)* | Watch for change. A drop to zero with the process alive means something upstream stopped sending, not that you broke |
| **Latency p95** | < 200ms | 95% of requests were faster than this |
| **Latency p99** | < 500ms | The tail. This is what times out and what people complain about |
| **In flight** | near 0, spiky | Climbing and never returning means requests arrive as fast as ever but leave more slowly — you are queueing |
| **5xx/sec** | 0 | Your bugs. Unhandled exceptions, failed dependencies, timeouts |
| **4xx/sec** | steady | Not an outage, but a *step change* matters: a wave of 401s after a deploy is a broken credential config |
| **Exceptions/sec** | near 0 | Thrown, whether or not the caller saw it. Exceptions **without** 5xx are the interesting case — see scenario 10 |
| **Outbound failures/sec** | 0 | Something this process dials is unreachable. Often a telemetry store, not a real dependency |
| **CPU** | < 70% | Sustained high CPU makes every latency number worse |
| **Memory** | *(shape, not value)* | One value means nothing. Open ⑦ and read the trend |
| **Thread-pool queue** | 0 | Above zero and staying there is thread-pool starvation. Every endpoint slows at once, including `/health` |

### ② What is the traffic doing?

**Requests/sec by status class** — stacked and colour-coded. Read the colours, not the
numbers. A red band appearing is the outage, and *its left edge is the minute it started*.
Set your time range to that edge before doing anything else; it is the single most useful
fact on the dashboard.

**Requests/sec by route** — is the spike one endpoint or all of them? One line rising alone
is a caller behaving badly; every line rising together is real load. Requests that matched no
route carry no `http_route` label and appear as an unnamed series — that is expected, and
"Rate limiting"/"unknown paths" in ⑤ counts them properly.

**Latency percentiles** — p50, p95 and p99 on one axis. The **gap** between the lines is the
diagnosis:

| Shape | Meaning |
|---|---|
| All three rise together | A shared cause: CPU, GC, a slow dependency, the database |
| p99 rises alone | A minority of requests are pathological — big payload, cold cache, a lock |
| p50 rises | Everything is slower. Usually saturation |

**Latency distribution (heatmap)** — the panel percentiles cannot give you. Time runs left to
right, latency bottom to top, darker means more requests.

```
 latency
   1s  │                      ░░░░░░░░░░░░        ← a second population appeared
 250ms │  ░░                  ▓▓▓▓▓▓▓▓▓▓▓▓
  50ms │  ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓  ▓▓▓▓▓▓▓▓▓▓▓▓
  10ms │  ████████████████████████████████████    ← the healthy band
       └──────────────────────────────────────
                              ↑ 14:32
```

A healthy service is **one** dark band, low down. **Two** bands means a bimodal service — say
cache hits at 5ms and cache misses at 300ms — and the p95 you were staring at is the average
of two different behaviours, describing neither. A band drifting up over hours is gradual
degradation; a second band appearing suddenly is a code path that just started being taken.

### ③ Which endpoint is the problem?

One row per route, sorted by error rate. This is the panel that turns *"the API is broken"*
into *"`POST /students` is broken"*.

| What you see | What it means |
|---|---|
| **5xx %** red | Start here, then go to ④ |
| **p95** red, 5xx % green | It works, it is just slow. Open a slow trace for that route |
| **p99 ≫ p95** on one row | That route is *inconsistent*, not uniformly slow |
| High **Req/s**, modest p95 | A busy, healthy endpoint. Leave it alone |

`Avg` is in the table for one reason: to be compared with `p95`. When Avg is small and p95 is
large, the average is lying to you — which is why no tile on this dashboard shows an average
on its own.

### ④ What is failing?

**Warnings & errors (Loki)** — every line at warn or above, newest first. Expand a line to
see its fields; `CorrelationId`, `TraceID`, `Request` and `scope_name` are the ones that
matter, and clicking `TraceID` jumps straight into Tempo.

Learn your **noise floor** first. In this app, `AUDIT(unshipped)` warnings mean Elasticsearch
is unreachable and audit records fell back to the log — a telemetry problem, not an
application one. Real application failures name a feature in `Request`.

**Failed requests (Tempo)** — traces whose HTTP status was 500 or worse. Click a row to open
the waterfall.

**Slow requests (Tempo)** — traces over the `$slow` threshold for the selected routes. This
is the answer to *"p95 is red — why?"*.

**Log volume by level** — a change detector, not an absolute. A step change in `warn` with no
change in traffic means something new started going wrong quietly. A collapse of `info` to
zero while requests continue means logs stopped reaching Loki, which makes every other log
panel lie by omission.

**Exceptions/sec by type** — the type name is usually the diagnosis:

| Exception type | Almost always means |
|---|---|
| `SqliteException` / `DbUpdateException` | The database — constraint, lock, file, migration |
| `HttpRequestException` / `SocketException` | A host you dial is unreachable |
| `TaskCanceledException` | A timeout, or a client that hung up |
| `ValidationException` | Expected, and normal on a public API |
| `InvalidOperationException` | Usually a DI or lifetime mistake, or a disposed context |

### ⑤ Dependencies & the edge — is it us or them?

**Outbound calls/sec by target**, **Outbound errors by target & reason**, and **Outbound p95
by target**. Known targets in this stack:

| Target | Who it is |
|---|---|
| `localhost:4317` | Tempo — traces |
| `localhost:3100` | Loki — logs |
| `localhost:9200` | Elasticsearch — the audit trail |
| your IdP host | The On-Behalf-Of token endpoint |
| anything else | A downstream API registered with `AddOnBehalfOf` |

The outbound p95 panel is the one that settles the *"it's not us, it's you"* argument. When
your p95 rises at exactly the moment a dependency's p95 rises, you have found the cause and
it is not your code. When yours rises and every dependency stays flat, it is your code.

**Error reasons** you will see, and what each means:

| `error_type` | Meaning |
|---|---|
| `connection_error` | Nothing is listening, or a firewall dropped it |
| `4xx` / `5xx` | It answered, and said no. 401 at the token endpoint = wrong client credentials; 403 at Elasticsearch = `audit-writer` lost a privilege |
| `TaskCanceledException` | It answered too slowly |
| `name_resolution_error` | DNS |

**Kestrel connections** — queued connections should be 0. Sustained above zero means the
server cannot accept as fast as clients arrive. Active connections far above your request
rate is normal here: keep-alive clients and SignalR hub connections.

**Rate limiting** — `acquired` is a served request; anything else is a caller getting a 429,
which from their side is an outage. `/health` and `/metrics` are deliberately exempt, so they
never appear.

**Authentication outcomes** — by scheme and result.

| Result | Meaning |
|---|---|
| `none` | No credential presented. Normal for open endpoints |
| `success` | Accepted |
| `failure` | Presented and **rejected**. This is the one to watch |

A step change in failures after a deploy means a key, an audience or an authority setting is
wrong. A steady trickle from one scheme is a stale client. A flood is someone guessing —
cross-check the audit trail's `Security` category.

**Slow database queries** — one row per query that took longer than `$dbslow`, with the SQL and
the duration. Click a row to open the trace it came from. The database is a dependency like any
other, which is why it lives in this section.

**Empty is the healthy state.** When it is not empty, read the SQL column before anything else —
the statement usually names its own problem:

| What you see | What it is |
|---|---|
| The same statement many times in one request | An **N+1**. The fix is in how the query is composed, not in the database |
| One statement occasionally slow, usually fast | A missing index meeting a table that has grown, or lock contention |
| A `SELECT` with no `WHERE` behind a list endpoint | A missing filter, or an accidental materialisation — someone called `ToList()` too early |
| A slow `COUNT(*)` beside a fast page query | Paging cost. The count is often the expensive half, and is sometimes not needed at all |

Only queries that ran **inside a request** appear here. Background work — the outbox poll, every
two seconds — is deliberately not traced, so it cannot drown the panel. That filter lives in
`Observability.cs`; see [§7](#7-traces--reading-a-waterfall).

### ⑥ Features & background work

HTTP routes tell you what callers *asked for*. This section tells you what the code *did*,
derived from the log line `LoggingBehavior` writes for every mediator request:

```
Handled GetStudent.Query in 0ms
```

The two differ wherever one endpoint dispatches several commands, or where work runs off the
request pipeline entirely — outbox handlers, background services.

**Slowest features** is handler time only: it excludes model binding, authentication and the
response write. That is exactly what makes it useful. When this is fast but the route's p95
is slow, the time is being spent *outside* your handler, and no amount of optimising the
handler will help.

**Outbox dispatch** — delivered, failed, dead-lettered. Failures that recover are normal;
surviving a transient wobble is the whole point of an outbox. Failures that climb with no
deliveries mean a handler throws every time, and every retry burns the budget until the
message dead-letters. Flat at zero while writes are happening means the dispatcher is not
running.

**Dead-lettered — total** — any number above zero is unfinished business: a domain event
raised and never acted on. Nothing retries it for you. See scenario 11.

### ⑦ Runtime health (collapsed)

Open it when section ① points inward — CPU, memory or thread-pool queue red.

| Panel | Healthy | Unhealthy |
|---|---|---|
| **CPU by mode** | user ≫ system | High *system* time means I/O and syscalls, not computation — often chatty database access or an exporter retrying hard |
| **Memory: working set vs GC heap** | sawtooth returning to the same floor | GC heap climbing steadily = a managed leak. Working set climbing while GC heap is flat = unmanaged memory (handles, connections, mmap) |
| **GC collections by generation** | gen0 constant, gen2 rare | A rising **gen2** rate is the best early warning of a memory problem, and shows up as latency long before anything runs out |
| **GC pause time** | < 1–2% | 0.05 means 5% of every second was spent frozen. Above a few percent, GC is a material part of your p99 |
| **Allocation rate** | proportional to traffic | Growth with unchanged traffic means a code path started allocating: a new serialization step, a big buffer, an accidental `ToList()` |
| **Thread pool** | queue 0 | A queue that will not drain is starvation. Cause is nearly always blocking on async code on a hot path |
| **Lock contention** | low, flat | Scaling with traffic means a shared lock is serialising request handling — throughput plateaus with CPU to spare |

### ⑧ Investigate one request (collapsed)

Paste an id into the **Find request** box, widen the time range, and open this section.

| Where the id comes from | Looks like |
|---|---|
| `X-Correlation-ID` response header — on every response | `a8faac35-f23e-43b2-a42a-df4e9ae09b03` |
| `traceId` in an RFC 7807 error body: `00-<trace id>-<span id>-01` | paste **only the middle part** |
| `TraceID` field on any log line in ④ | 32 hex characters |
| `correlationId` on an audit record in Kibana | a GUID |

The logs panel matches either id. The trace panel needs a trace id and will error on a
correlation id — that is expected, not a broken panel.

---

## 4. Reading a number without fooling yourself

Four traps. Every one of them has caused a real team to chase the wrong thing.

### `rate()` is a per-second average over a window

`rate(x[5m])` is not "the value now". It is the average per-second increase over the last
five minutes. A one-second burst of 300 errors inside a five-minute window shows as
`1 error/sec` — one hundredth of the peak. **Short spikes are invisible at long windows.**
When you are hunting a spike, shorten the dashboard's time range; `$__rate_interval` shrinks
with it and the spike reappears.

The counterpart: at very short windows you get noise and gaps. Never read a rate over a
window shorter than about four scrape intervals — here, 15s scrapes, so 1m is the floor.

### A percentile is not a request

`p95 = 250ms` does not mean any request took 250ms. It means 95% took less. And percentiles
here come from **histogram buckets**, whose edges are fixed:

```
5ms · 10ms · 25ms · 50ms · 75ms · 100ms · 250ms · 500ms · 750ms · 1s · 2.5s · 5s · 7.5s · 10s
```

`histogram_quantile` interpolates *inside* whichever bucket the percentile lands in. So
"p99 = 2.5s" means **somewhere between 1s and 2.5s**, not a stopwatch reading. Two
consequences worth remembering: above 10s everything is `+Inf` and the estimate is
meaningless, and a p99 that sits at exactly a bucket edge for hours is an artefact, not a
coincidence.

### You cannot average a percentile

`avg(p95_of_each_route)` is not the p95 of the service. Percentiles must be computed from the
raw buckets, which is why every latency query on the dashboard has this shape:

```promql
histogram_quantile(0.95, sum by (le) (rate(..._bucket[$__rate_interval])))
```

`sum by (le)` first, `histogram_quantile` second. Reversing them produces a number that looks
plausible and is wrong.

### A counter resets when the process does

Everything ending in `_total` is cumulative *since this process started*. `rate()` and
`increase()` handle the reset for you. Reading the raw value does not: "dead-lettered = 0" on
the tile means zero **since the last restart**, not ever. For "ever", query the database.

---

## 5. Metrics — the PromQL you actually need

Grafana → **Explore** → Prometheus. Six patterns cover almost everything.

```promql
# 1. How many per second?  (any _count or _total)
sum(rate(http_server_request_duration_seconds_count[$__rate_interval]))

# 2. Broken down by something — put the label in `by`
sum by (http_route) (rate(http_server_request_duration_seconds_count[$__rate_interval]))

# 3. Filtered — labels in braces; =~ is a regex, != is "not"
sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[$__rate_interval]))

# 4. A percentage — always clamp the denominator or you divide by zero on idle
  sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[$__rate_interval]))
/ clamp_min(sum(rate(http_server_request_duration_seconds_count[$__rate_interval])), 0.0001) * 100

# 5. A latency percentile — sum by (le) FIRST
histogram_quantile(0.95, sum by (le, http_route) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))

# 6. The worst few
topk(5, sum by (http_route) (rate(http_server_request_duration_seconds_count[$__rate_interval])))
```

Two idioms that save you:

```promql
# `or vector(0)` — show 0 instead of "No data" when nothing has happened yet
sum(rate(dotnet_exceptions_total[$__rate_interval])) or vector(0)

# `increase()` — how many in total over the window, rather than per second
sum(increase(dotnet_exceptions_total[$__range]))
```

### The metrics this app publishes

Scraped from `http://localhost:5235/metrics`. This is the real list, not a sample.

| Metric | Labels worth grouping by | Answers |
|---|---|---|
| `http_server_request_duration_seconds_{count,sum,bucket}` | `http_route`, `http_response_status_code`, `http_request_method` | Rate, errors, latency — the whole RED method |
| `http_server_active_requests` | — | Saturation: how many in flight |
| `http_client_request_duration_seconds_*` | `server_address`, `server_port`, `error_type` | Every outgoing call: downstreams, IdP, and the telemetry stores |
| `http_client_active_requests`, `http_client_request_time_in_queue_seconds` | `server_address` | Connection-pool pressure on a downstream |
| `kestrel_active_connections`, `kestrel_queued_connections` | — | The socket layer, below HTTP |
| `kestrel_connection_duration_seconds` | — | Long-lived connections (SignalR) |
| `aspnetcore_rate_limiting_requests_total` | `aspnetcore_rate_limiting_result` | Who got throttled |
| `aspnetcore_authentication_authenticate_duration_seconds_*` | `aspnetcore_authentication_scheme`, `aspnetcore_authentication_result` | Auth accepted vs rejected, per scheme |
| `aspnetcore_routing_match_attempts_total` | `aspnetcore_routing_match_status` | Requests to URLs that match no route |
| `dotnet_exceptions_total` | `error_type` | What is being thrown |
| `dotnet_process_cpu_time_seconds_total` | `cpu_mode` | CPU, user vs system |
| `dotnet_process_memory_working_set_bytes` | — | Memory as the OS sees it |
| `dotnet_gc_collections_total` | `gc_heap_generation` | GC pressure |
| `dotnet_gc_last_collection_heap_size_bytes` | `gc_heap_generation` | Managed heap after the last collection |
| `dotnet_gc_pause_time_seconds_total` | — | Time frozen for GC |
| `dotnet_gc_heap_total_allocated_bytes_total` | — | Allocation rate |
| `dotnet_thread_pool_thread_count_total`, `dotnet_thread_pool_queue_length_total` | — | Thread-pool starvation |
| `dotnet_monitor_lock_contentions_total` | — | Lock contention |
| `dns_lookup_duration_seconds` | — | DNS, when a dependency is "slow" for no reason |
| `outbox_delivered_total`, `outbox_failed_total`, `outbox_dead_lettered_total` | — | The one business metric this app defines ([50 §7](50-instrumenting-an-application.md#7-step-4--add-your-own-metric)) |

> **Where the names come from.** OpenTelemetry names these with dots
> (`http.server.request.duration`). Prometheus 3 can keep dots, but the dashboard expects the
> classic underscore form, which is why `prometheus.yml` sets
> `metric_name_escaping_scheme: underscores`. Remove that line and every panel goes blank
> while Explore still works — see [§15](#15-troubleshooting).

### Two things `/metrics` will not tell you

There is **no database metric** — no query count, no query duration, no connection-pool stats —
so you cannot chart or alert on the database. You can still *see* it: every query gets a span,
so the answer lives in Tempo ([§7](#7-traces--reading-a-waterfall)) rather than on a graph.

And there is **no per-feature metric**; feature-level timing comes from logs (section ⑥).
[§12](#12-what-this-tells-you--and-what-it-does-not) has the fix.

---

## 6. Logs — reading Loki properly

Grafana → **Explore** → Loki.

### Labels versus fields — the one thing to understand

A LogQL query has two halves, and they are not interchangeable:

```
{service_name="CleanArch.Api"}  | detected_level = "error"
└──────── stream selector ────┘  └──── field filter ────┘
   labels — ONLY these exist       everything else lives here
```

Because logs arrive over OTLP, Loki keeps only the resource attributes as **labels**:

```
service_name          "CleanArch.Api"
service_instance_id   a GUID, different per process start
```

**That is the complete list.** Everything else — the correlation id, the trace id, the
feature name, the level — is **structured metadata**, filtered *after* the pipe. Putting a
field inside the braces returns "no data" with no error, which reads exactly like "nothing
happened". It is the single most common LogQL mistake.

Every query therefore starts with `{service_name="CleanArch.Api"}`.

### The fields on every line

Verified from a real line in this app:

| Field | Example | Use it for |
|---|---|---|
| `CorrelationId` | `a8faac35-f23e-43b2-a42a-df4e9ae09b03` | All lines of one request — the id the caller got back in `X-Correlation-ID` |
| `trace_id` / `TraceID` | `00c137b6e0894d33697677eabd7a8cb8` | Jumping to Tempo. Grafana renders it as a clickable link |
| `span_id` / `SpanId` | `5bfb2a7f9f7344a4` | Which span within the trace |
| `detected_level` | `info`, `warn`, `error` | Severity filtering |
| `severity_text` | `Information`, `Warning` | The .NET name, if you prefer it |
| `scope_name` | `BuildingBlocks.Messaging.Behaviors.LoggingBehavior` | Which class logged it — the fastest way to silence a noisy component |
| `RequestPath` | `/students/1111…` | The URL, including the actual id |
| `RequestId`, `ConnectionId` | `0HNO9UQQ2HCJF:00000001` | Kestrel's own ids |
| `_OriginalFormat_` | `Handled {Request} in {ElapsedMs}ms` | The message *template* — groups all instances of one message regardless of values |

Plus every placeholder in the message template, as its own field. `LoggingBehavior` gives you
`Request` and `ElapsedMs`; the audit fallback gives you `Action`, `Actor`, `Succeeded`,
`ChangeCount`; EF Core gives you `commandText`, `elapsed`, `parameters`.

That last point is the payoff of structured logging, and why
[50 §9](50-instrumenting-an-application.md#9-step-6--logs-worth-searching) insists on message
templates: `"Handled {Request} in {ElapsedMs}ms"` is searchable, and
`$"Handled {request} in {ms}ms"` is not.

### The queries

```logql
# Everything
{service_name="CleanArch.Api"}

# Errors and warnings
{service_name="CleanArch.Api"} | detected_level =~ "warn|error|critical|fatal"

# One request, by the id the caller was given
{service_name="CleanArch.Api"} | CorrelationId = "a8faac35-f23e-43b2-a42a-df4e9ae09b03"

# One request, by trace id — from a trace, or from an error body
{service_name="CleanArch.Api"} | trace_id = "00c137b6e0894d33697677eabd7a8cb8"

# Either id, whichever you have
{service_name="CleanArch.Api"} | CorrelationId =~ `$find` or trace_id =~ `$find`

# One feature
{service_name="CleanArch.Api"} | Request = "CreateStudent.Command"

# Slow handlers — numeric comparison works on structured metadata
{service_name="CleanArch.Api"} |= `Handled` | ElapsedMs > 500

# Free text in the line body (not the fields)
{service_name="CleanArch.Api"} |= "constraint failed"

# Silence a component
{service_name="CleanArch.Api"} | scope_name != "Microsoft.EntityFrameworkCore.Database.Command"

# One URL, wildcard on the id
{service_name="CleanArch.Api"} | RequestPath =~ "/students/.*/transcript"
```

Turning logs into a graph — this is how section ⑥ is built:

```logql
# Lines per level over time
sum by (detected_level) (count_over_time({service_name="CleanArch.Api"}[$__auto]))

# How often each feature ran
sum by (Request) (count_over_time({service_name="CleanArch.Api"} |= `Handled` | Request != `` [$__auto]))

# Worst handler time per feature — `unwrap` turns a field into a number
topk(10, max by (Request) (max_over_time(
  {service_name="CleanArch.Api"} |= `Handled` | Request != `` | unwrap ElapsedMs [$__auto])))

# Error rate from logs, when you have no metric for something
sum(count_over_time({service_name="CleanArch.Api"} | detected_level = "error" [$__auto]))
```

> **Use backticks, not quotes, for values containing regex or quotes.** `` | Request != `` ``
> is "the field is not empty". In JSON dashboards, backticks also save you an escaping layer.

### Reading a log line like an engineer

```
14:32:07  Handled CreateStudent.Command in 4071ms
          Request=CreateStudent.Command  ElapsedMs=4071
          CorrelationId=a8faac35-…  trace_id=00c137b6…
          scope_name=BuildingBlocks.Messaging.Behaviors.LoggingBehavior
```

Four questions, in order, every time:

1. **Is this line unusual?** Compare against the log-volume panel. A hundred of these per
   minute is the noise floor; one is a signal.
2. **What was running?** `Request` names the feature — which is a class you can open.
3. **How long?** `ElapsedMs` is handler time. 4071ms in a handler is not a slow database, it
   is a *timeout somewhere inside it*, and round numbers near 5s, 30s or 100s are almost
   always a configured timeout rather than real work.
4. **What else happened in this request?** Filter by `CorrelationId` and read the whole
   story.

---

## 7. Traces — reading a waterfall

Grafana → **Explore** → Tempo. Three ways in: **Search** (build a filter), **TraceQL** (type
one), or paste a trace id.

### TraceQL

```traceql
# Everything from this service
{resource.service.name = "CleanArch.Api"}

# Failures
{resource.service.name = "CleanArch.Api" && span.http.response.status_code >= 500}

# Slow requests on one route
{span.http.route = "/students" && duration > 500ms}

# Slow AND failing
{span.http.response.status_code >= 500 && duration > 1s}

# Anything the SDK marked as an error (broader than HTTP status)
{status = error}

# A specific outgoing call that failed
{span.error.type = "connection_error"}

# A token exchange happened — the On-Behalf-Of round-trip
{name = "OnBehalfOf.Exchange"}

# One record, whoever touched it
{span.url.path =~ ".*bd0034a3.*"}
```

Attributes available on spans in this app, verified from Tempo:

| Scope | Attributes |
|---|---|
| `span.` (HTTP) | `http.request.method`, `http.response.status_code`, `http.route`, `url.path`, `url.scheme`, `url.full`, `server.address`, `server.port`, `network.protocol.version`, `user_agent.original`, `error.type` |
| `span.` (database) | `db.statement` — the SQL · `db.system` — `sqlite` · `db.name` |
| `resource.` | `service.name`, `service.instance.id`, `telemetry.sdk.*` |
| events | `exception.type`, `exception.message`, `exception.stacktrace` |
| intrinsic | `name`, `duration`, `status`, `kind`, `rootName`, `rootServiceName` |

Database queries are searchable in their own right, which is often faster than going via the
request that ran them:

```traceql
# Every slow query, whatever asked for it
{span.db.system = "sqlite" && duration > 100ms}

# One table
{span.db.statement =~ ".*Students.*"}
```

### What a waterfall looks like here

```
POST /students/search                             ██████████████████████  76ms   SERVER
  └─ main  SELECT COUNT(*) FROM "Students"                   ▌            0.24ms CLIENT
  └─ main  SELECT "s"."Id", "s"."FirstName" …                  ▌          0.29ms CLIENT

POST /students                                    ██████████████████████  1.2s   SERVER
  └─ OnBehalfOf.Exchange                          ██                      140ms  INTERNAL
  └─ POST billing.internal/invoices                 ████████              600ms  CLIENT
```

Database spans are named after the database, not the query — `main` is SQLite's name for the
file. The SQL is on the span as `db.statement`; click it. Two spans for one paged list is the
normal shape: one `COUNT(*)` for the total, one for the page.

Read it **outside-in**:

1. The **root** span is the whole request as the server saw it.
2. A **CLIENT** child is time spent waiting on somebody else. Its duration is *their*
   problem, not yours.
3. Time inside the root that no child accounts for — the gaps — is time in **your** code.
   That is where a database call, a computation or a lock lives.
4. An `OnBehalfOf.Exchange` span means a token was fetched from the identity provider. Its
   **absence** on a downstream call means the token came from cache. That is the quickest way
   to tell the two apart.

Click a span to see its attributes and, if it failed, its exception event — type, message and
stack trace, right there.

> **A real example from this stack.** A 4,071ms root span with a single child: a CLIENT span,
> `url.full = http://localhost:9200/cleanarch-audit-2026.09.03/_bulk`,
> `error.type = connection_error`. That is the audit shipper failing to reach Elasticsearch —
> four seconds of connect timeout, and *not* a slow request from any user's point of view.
> Your own telemetry exports appear in your traces. Learn to recognise them.

### The two buttons that make this worth it

- **On a span → "Logs for this trace".** Jumps to Loki filtered to that trace id. Configured
  as `tracesToLogsV2` on the Tempo data source.
- **On a log line → the `TraceID` field.** A link back into Tempo. Configured as
  `derivedFields` on the Loki data source.

Both are already set up. If either button is missing, the data source is not provisioned from
`observability/grafana/provisioning/datasources/datasources.yaml` — fix that, not the query.

### What is in a trace, and what is not

You get the server span, a child span per database query, one client span per outgoing HTTP
call, and the On-Behalf-Of exchange. You do **not** get a span per mediator handler, so the
boundary between "validation" and "the handler" is not drawn — though the database spans
inside it usually make that obvious.

**Background work is deliberately not traced.** Database queries that run outside a request —
the outbox dispatcher's poll, every two seconds, forever — are filtered out in
`Observability.cs`. Without that filter each poll became a root span and therefore a trace of
its own: measured on an idle instance, about 38,000 traces a day, every one of them a single
`SELECT` nobody asked about. If you later wrap a background operation in a span of its own,
its queries start appearing again automatically.

---

## 8. The audit trail — reading Kibana

The audit trail answers a different question from the logs, and the difference is not
stylistic: logs are for *diagnosis*, the audit trail is *evidence*. [40](40-auditing.md) is
the full guide; this is the part you need mid-investigation.

Kibana → **Discover**, data view `cleanarch-audit-*`, time field `occurredOnUtc`.

```
actor : "integration-service"
action : "WithdrawStudent" and succeeded : false
category : "Read" and resource : "Student/bd0034a3-832a-4399-b106-54d03a223898"
resource : "Student/bd0034a3-*"
category : "Security"
correlationId : "a8faac35-f23e-43b2-a42a-df4e9ae09b03"
```

That last one is the bridge: **the same correlation id is in your logs, your traces and your
audit trail.** One id, four stores.

| Field | Question it answers |
|---|---|
| `actor` | Who |
| `action` | What operation |
| `resource` | Whose record |
| `category` | `Write` / `Read` / `External` / `Security` / `Custom` |
| `succeeded`, `error` | Outcome |
| `changes[]` | The before → after values, per property |
| `correlationId` | The link to logs and traces |

Two traps, both of which look exactly like "there is no data":

- **Field names are camelCase** (`actor`, not `Actor`), but **category values are
  PascalCase** (`"Read"`, not `"read"`).
- **The time picker defaults to 15 minutes.** Widen it before concluding anything.

**When to use the audit trail instead of the logs:** anything a person will be asked to
answer for. *Who changed this record?* *Who looked at this student's data?* *Did the
integration service actually run that job?* Logs get deleted on a retention schedule and are
full of noise; the audit trail is a deliberate, mapped, searchable record of decisions.

---

## 9. The golden thread — one request across all four

One id ties everything together. Here is the whole path, using a real error response from
this API.

**1. The caller has an error body.** Every RFC 7807 response from this app carries a
`traceId` in W3C traceparent format:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": { "DateOfBirth": ["Date of birth must be before the enrollment date."] },
  "traceId": "00-e77822417fb42c34f750fbe386b1968c-f74a6703e68fc0b5-01"
}
```

Split it: `00` is the version, **`e77822417fb42c34f750fbe386b1968c` is the trace id**,
`f74a6703e68fc0b5` is the span id, `01` means it was sampled. **Take the middle part.**

**2. Or the caller has a correlation id.** Every response also carries
`X-Correlation-ID`. Callers who log their outbound calls will have this even when they did not
keep the body.

**3. Paste it into the dashboard.** Section ⑧, the **Find request** box. Both panels populate.

**4. Or go store by store:**

```logql
# Loki — the narrative
{service_name="CleanArch.Api"} | trace_id = "e77822417fb42c34f750fbe386b1968c"
```
```traceql
# Tempo — the timing
e77822417fb42c34f750fbe386b1968c
```
```
# Kibana — the evidence (use the correlation id, not the trace id)
correlationId : "a8faac35-f23e-43b2-a42a-df4e9ae09b03"
```

**5. And onward, into async work.** The correlation id is written onto outbox messages when
they are enqueued, so the *later* delivery of that domain event carries the id of the request
that caused it. One id spans the synchronous request and the asynchronous consequence — which
is exactly what makes a saga debuggable. See
[60 §13](60-talking-across-modules.md#13-compensation--the-two-leg-saga).

> **The two ids, and why there are two.** The **trace id** is generated by OpenTelemetry and
> spans processes. The **correlation id** is generated by `CorrelationIdMiddleware`, can be
> *supplied by the caller* via the `X-Correlation-ID` header, and is what the audit trail and
> the outbox record. When a caller supplies their own, their support ticket and your audit
> trail share an id — which is worth far more than the two-second saving of not doing it.

---

## 10. Eighteen real scenarios

Each one: **symptom → where you see it → the query → what it means → what to do**. These are
the failures you will actually meet.

### 1. A deploy broke it — 5xx spike

| | |
|---|---|
| **Symptom** | Success rate red, 5xx/sec red, started at a specific minute |
| **See it** | ② status-class chart — the red band's left edge is the start time |
| **Confirm** | ③ table: which route. Usually one, sometimes all |
| **Diagnose** | ④ failed traces → open one → the failing span's `exception.type` and stack trace |

```logql
{service_name="CleanArch.Api"} | detected_level = "error" | Request = "CreateStudent.Command"
```

**What it means.** If the start time is within a minute or two of a deploy, it is the deploy —
do not investigate further until you have compared the diff. **What to do.** Roll back first,
diagnose second. The trace and the logs are still there afterwards.

### 2. It is slow, and nothing is failing

| | |
|---|---|
| **Symptom** | p95 red, success rate green |
| **See it** | ② percentiles: do all three lines rise together? |
| **Confirm** | ⑤ outbound p95 — did a dependency's latency rise at the same moment? |

**What it means.** All percentiles rising together plus one dependency rising = you are
waiting on them. All rising with flat dependencies = you, or the host. **What to do.** If it
is a dependency, add a timeout and a fallback before you optimise anything: an unbounded wait
on somebody else's outage is how one service's problem becomes three services' problem.

### 3. Slow for *some* people — the bimodal service

| | |
|---|---|
| **Symptom** | p99 far above p95, p50 normal. Complaints from a minority |
| **See it** | ② heatmap: **two bands** |

**What it means.** Two populations. A cache with hits and misses, a query that sometimes hits
an index and sometimes scans, a list endpoint where one tenant has 100,000 rows. **What to
do.** Find what distinguishes them — open slow traces (④) and compare `url.path` against a
fast one. The distinguishing feature is usually visible in the URL.

### 4. Everything is slow, including `/health`

| | |
|---|---|
| **Symptom** | Every route's p95 up together, even trivial ones |
| **See it** | ① thread-pool queue above zero and staying there |
| **Confirm** | ⑦ thread pool: queue climbing, thread count climbing slowly behind it |

**What it means.** Thread-pool starvation. `/health` does nothing and cannot be slow for its
own reasons — when it is slow, nothing can get a thread. **What to do.** Find blocking calls
on async code: `.Result`, `.Wait()`, `.GetAwaiter().GetResult()`, any `async void`. The pool
grows one thread per second or so, which is why the recovery is always slower than the onset.

### 5. Memory climbs until recycle

| | |
|---|---|
| **Symptom** | Restarts every few days; latency degrades before each one |
| **See it** | ⑦ memory over **7 days**, not 30 minutes |

**What it means.** A staircase that never returns to its floor is a leak. GC heap climbing =
managed (a static collection, an un-disposed scope, an event handler never unsubscribed).
Working set climbing while GC heap is flat = unmanaged (handles, connections, memory-mapped
files). **What to do.** Correlate the start of the climb with a deploy. Watch gen2 collection
rate — it rises first, and is the earliest signal you get.

### 6. CPU pegged, traffic flat

| | |
|---|---|
| **Symptom** | CPU red, request rate unchanged |
| **See it** | ⑦ CPU by mode, and GC pause time on the same range |

**What it means.** High GC pause with high CPU is allocation churn, not load. High *system*
CPU is I/O or syscalls. High *user* CPU with low GC is a hot loop or a runaway retry.
**What to do.** Check ⑤ outbound calls first: a retry loop against a dead dependency looks
exactly like a hot loop, and is far more common.

### 7. A wave of 401s after a deploy

| | |
|---|---|
| **Symptom** | 4xx tile up, success rate still green |
| **See it** | ② status split: yellow band growing. ⑤ authentication outcomes: `failure` appears |

**What it means.** Credentials stopped being accepted. Which scheme fails tells you where:
`ApiKey` = a key was rotated or the store was reseeded; `Bearer` = wrong authority, wrong
audience, or clock skew; `Basic` = the directory is unreachable. **What to do.** Compare the
`Okta:Authority` / `Okta:Audience` / API-key settings against the previous release. Check the
audit trail's `Security` category for who is being rejected.

### 8. A wave of 404s

| | |
|---|---|
| **Symptom** | 4xx up, mostly 404 |
| **See it** | ⑤ unknown paths — `aspnetcore_routing_match_attempts_total{...match_status="failure"}` |

```promql
sum(rate(aspnetcore_routing_match_attempts_total{aspnetcore_routing_match_status="failure"}[$__rate_interval]))
```

**What it means.** Two different causes with the same symptom. Match **failure** (no route)
means callers are hitting URLs that do not exist: a removed route, a client on an old
version, or a scanner. Match **success with a 404 response** means the route exists and the
*record* does not — normal, and a different problem entirely. Split them:

```promql
sum by (http_route) (rate(http_server_request_duration_seconds_count{http_response_status_code="404"}[$__rate_interval]))
```

A named route means "record not found". A blank one means "no such URL".

### 9. Callers are being throttled

| | |
|---|---|
| **Symptom** | Complaints of intermittent failure; your 5xx is clean |
| **See it** | ⑤ rate limiting: anything other than `acquired` |

**What it means.** They got a 429. From their side that is an outage. **What to do.** If it
appears without a traffic spike, the limit is too low for normal use
(`RateLimiting:PermitLimit` / `WindowSeconds`). If one client caused it, that is the
conversation to have — not a limit change.

### 10. Exceptions are climbing but no request failed

| | |
|---|---|
| **Symptom** | Exceptions/sec red, success rate green |
| **See it** | ④ exceptions by type, then ⑤ outbound errors |

**What it means.** Something in the background is throwing and nobody notices. In this app the
usual suspects, in order: the audit shipper cannot reach Elasticsearch, the OTLP exporters
cannot reach Tempo or Loki, or an outbox handler throws on every attempt.

```logql
{service_name="CleanArch.Api"} |= "AUDIT(unshipped)"
{service_name="CleanArch.Api"} | detected_level="warn" | scope_name =~ ".*Elasticsearch.*"
```

**What to do.** This is real. Records are being written to the log as a fallback instead of to
the audit store, and the fallback is not searchable the same way. Fix the connection, then
backfill if the trail matters.

### 11. An outbox message dead-lettered

| | |
|---|---|
| **Symptom** | Dead-lettered tile above zero. Something downstream "never happened" |
| **See it** | ⑥ outbox dispatch: failures climbing with no matching deliveries |

**What it means.** A handler threw on every attempt until the retry budget ran out. The
message is now sitting in the database doing nothing, and **nothing will retry it for you**.

```logql
{service_name="CleanArch.Api"} | detected_level =~ "warn|error" |= "outbox"
```

**What to do.** Query `OutboxMessages` where `DeadLetteredOnUtc is not null`, read the `Error`
column, fix the handler, then re-queue deliberately. The correlation id on the message leads
back to the request that caused it. See
[60 §10](60-talking-across-modules.md#4-why-writes-need-an-outbox).

### 12. "This record changed and nobody knows why"

Not a dashboard question. Kibana:

```
resource : "Student/bd0034a3-832a-4399-b106-54d03a223898" and category : "Write"
```

Expand a row and read `changes[]` — entity, operation, and each property's old → new. Take the
`correlationId` from that record and go back to the logs for the surrounding context.

**What not to do:** do not build a "revert" button on top of this. [40 §13](40-auditing.md#13-the-revert-question)
explains why, at length, and what to do instead.

### 13. "Who looked at this person's data?"

```
resource : "Student/bd0034a3-*" and category : "Read"
```

A read leaves no trace in the database and no 5xx anywhere. If read auditing is not switched
on for that query, this question has **no answer** — see
[40 §6](40-auditing.md#6-step-2--audit-a-read). That is a decision to make before the
subject-access request arrives, not after.

### 14. A downstream call returns 502

| | |
|---|---|
| **Symptom** | Your API returns 502 with `"downstream": "billing"` in the body |
| **See it** | ⑤ outbound errors — the IdP's token endpoint, or the downstream itself |
| **Confirm** | Tempo: `{name = "OnBehalfOf.Exchange"}` — did the exchange happen, and did it fail? |

**What it means.** By design this app never turns an upstream identity-provider failure into a
500: a rejected subject token is a 401, an unavailable provider is a 502. So a 502 is
*already* the diagnosis — the provider or the downstream is unwell. **What to do.** Check
`obo.failure` and `obo.token_endpoint.status_code` on the span. See
[70](70-authentication.md).

### 15. The dashboard is empty and the app is fine

| | |
|---|---|
| **Symptom** | "No data" everywhere, but `curl` on the API works |

Work outward, in this order:

```bash
curl -s http://localhost:5235/metrics | head          # 1. is the app producing?
# 2. is Prometheus collecting?  http://localhost:9090/targets  → cleanarch-api must be UP
# 3. is Prometheus storing?     Explore → up{job="cleanarch-api"}
# 4. does the panel query work in Explore?            → if yes, it is the dashboard, not the data
```

Each step eliminates one hop. See [§11](#11-when-the-telemetry-is-the-problem).

### 16. Traffic dropped to zero, process alive

| | |
|---|---|
| **Symptom** | Request rate → 0. Success rate reads 100% (nothing failed, because nothing happened) |
| **See it** | ① requests/sec at zero while ⑦ shows the process running normally |

**What it means.** Nothing is reaching you. A load balancer health check started failing, DNS
changed, a firewall rule was applied, a certificate expired at the edge, or the deploy took
the site offline in IIS. **What to do.** Look *outside* this dashboard. `/metrics` still being
scraped proves the process is up and that the problem is between the caller and you.

> **A 100% success rate at zero traffic is the most dangerous green on any dashboard.** Read
> the request-rate tile before you trust the success-rate tile — always.

### 17. Slow only at 09:00

| | |
|---|---|
| **Symptom** | A latency spike at the same time every day |
| **See it** | ② percentiles over **7 days**; the shape repeats |

**What it means.** Cold start (a recycled app pool, a scale-in overnight), a cold cache, or a
scheduled job competing for the database. **What to do.** Check ⑦ allocation and GC at the
spike — a cold start shows a JIT and allocation burst. Check ⑥ feature throughput for a
feature that only runs at that hour.

### 18. Intermittent timeouts against one dependency

| | |
|---|---|
| **Symptom** | Occasional 5xx or 502; most calls fine |
| **See it** | ⑤ outbound p95 spiky for one target; `TaskCanceledException` in ④ |
| **Confirm** | `http_client_request_time_in_queue_seconds` — connection-pool starvation, not a slow server |

```promql
histogram_quantile(0.95, sum by (le, server_address) (rate(http_client_request_time_in_queue_seconds_bucket[$__rate_interval])))
```

**What it means.** Time *in the queue* means your requests waited for a free connection before
they were even sent. That is your pool, not their server. **What to do.** Raise
`MaxConnectionsPerServer`, or find the calls that are not being disposed.

---

## 11. When the telemetry is the problem

A surprising share of "incidents" are the monitoring failing, not the service. Learn the
signatures — the cost of paging someone at 3am for a dead Loki is paid in trust.

| Signature | Almost certainly |
|---|---|
| Dashboard blank, Explore works | The dashboard's data source uid, or metric-name escaping — [§15](#15-troubleshooting) |
| Metrics fine, logs and traces missing | The app cannot reach Loki/Tempo. Push versus pull: metrics are **pulled**, so they survive an outbound network problem that kills the other two |
| Logs and traces fine, metrics missing | The opposite direction — Prometheus cannot reach the app. A Windows Firewall inbound rule, or the wrong port |
| Exceptions climbing, success rate green | Exporters retrying. Check ⑤ for `:3100`, `:4317`, `:9200` |
| `AUDIT(unshipped)` warnings | Elasticsearch is unreachable; audit fell back to logs |
| A 4s span whose only child is a POST to `:9200` | The audit shipper's connect timeout, showing up in your own traces |
| Everything stops at the same instant | A store's disk filled, or retention deleted more than you expected |

The asymmetry is worth internalising because it is diagnostic on its own:

```
  the app                                    the stack
  ┌────────────────┐   traces  ──push──▶   Tempo    :4317
  │  CleanArch.Api │   logs    ──push──▶   Loki     :3100
  │                │   audit   ──push──▶   ES       :9200
  │  GET /metrics  │ ◀──pull────────────   Prometheus :9090
  └────────────────┘
```

Three signals are pushed by the app; metrics are pulled. **Which signals survive tells you
which direction the network broke.**

And the rule that follows from all of it:

> **Never conclude "nothing happened" from an empty panel.** Conclude "I have no data", then
> find out which. They lead to opposite actions.

---

## 12. What this tells you — and what it does not

Honest gaps, so you do not go looking for something that is not there. Each has its fix.

### No database *metrics* — but you do have database spans

There is no metric for query count or query duration, so you cannot chart "queries per second"
or alert on it. What you do have is a **span per query**
(`OpenTelemetry.Instrumentation.EntityFrameworkCore`, wired up in `Observability.cs`), which
answers the question that actually comes up: *which* query was slow, and how many ran. An N+1
is unmistakable — forty identical bars instead of one.

The SQL text rides on the span as `db.statement`. **Parameter values do not** — the package
keeps that switch internal, and it is the right default: parameter values are the data itself,
and a trace store has none of the redaction, access control or retention rules that data needs.

### No span per feature

`LoggingBehavior` writes a log line per mediator request, which is why section ⑥ is built from
logs. A *span* per handler would place it in the waterfall instead.

**Fix.** A pipeline behaviour that starts an `Activity`, the same shape as
`OnBehalfOfDiagnostics` — [50 §8](50-instrumenting-an-application.md#8-step-5--add-your-own-span)
has the pattern.

### No exemplars

You cannot click a point on the latency graph and jump to an example trace from that instant.
Exemplars would link the two. Requires enabling them in the exporter and
`--enable-feature=exemplar-storage` in Prometheus.

### No sampling — and no plan for it

Every trace is currently kept. Fine at development volumes; at production volumes it is
expensive, and the day you add sampling, "I cannot find the trace" becomes a normal
occurrence rather than a bug. Decide deliberately.

### Metrics have no identity, by design

You cannot ask "how many requests did this customer make". That is deliberate — a `user_id`
label would create one time series per user and take Prometheus down. Per-identity questions
belong in logs and the audit trail. See
[50 §7](50-instrumenting-an-application.md#7-step-4--add-your-own-metric) on label
cardinality.

### Retention is finite

Whatever is set in `prometheus.yml`, `loki.yaml`, `tempo.yaml` and Elasticsearch ILM. An
investigation into something from six weeks ago may simply have no data. Know your numbers
before you need them.

---

## 13. The four alerts worth having

A dashboard nobody is looking at is not monitoring. Four rules ship with this repo, in
[`observability/grafana/provisioning/alerting/cleanarch-api.yaml`](../observability/grafana/provisioning/alerting/cleanarch-api.yaml).
Add more only when a real incident proves you needed one.

| Alert | Fires when | `for` | Severity |
|---|---|---|---|
| **High error rate** | over 5% of requests return 5xx | 5m | critical |
| **High latency** | p95 above 1s | 10m | warning |
| **Service down** | Prometheus cannot scrape `/metrics` | 2m | critical |
| **Outbox message dead-lettered** | any message was given up on in the last hour | 1m | warning |

### Why Grafana and not Prometheus rules

Prometheus can evaluate alerting rules perfectly well, but it **cannot notify anyone** — that
needs a separate Alertmanager process with its own config and its own receivers. Grafana is
already running, already has Prometheus as a data source, and already has contact points and
notification policies. One component instead of two, for the same four alerts.

### Turning them on

The Docker stacks pick the file up automatically — both compose files already mount
`../grafana/provisioning` read-only, and the new `alerting/` folder rides along with the
`datasources/` and `dashboards/` folders that are already there.

Running Grafana natively? Copy the file into your Grafana's provisioning folder — the same
place `datasources.yaml` went — and restart:

```
<grafana>/conf/provisioning/alerting/cleanarch-api.yaml
```

Then check **Alerting → Alert rules**. Four rules in a `CleanArch` folder, all Normal on a
healthy service.

### Three design points that matter more than the thresholds

**`for:` is not optional.** Without it, one bad scrape pages someone. Five seconds of failure
is a garbage collection; five minutes is an incident. Every rule waits.

**Alert on symptoms, not causes.** "Over 5% of requests are failing" is worth waking someone
for. "CPU above 80%" is not — it is often entirely fine, and when it is not, the error-rate or
latency rule fires anyway. Cause-based alerts are how teams end up ignoring their pager.

**`noDataState` is a real decision, not boilerplate.** Three of these rules use `OK` when there
is no data, because no data means no traffic, and a quiet service is not a broken one. **Service
down** uses `Alerting` instead, and the reasoning is worth following: `up` is written by
Prometheus itself on every scrape, so its *absence* is not silence — it means Prometheus is not
scraping at all. The app is gone, the host is gone, or the scrape config broke. Getting this
backwards is how an outage produces no alert.

### They evaluate; they do not yet notify

Out of the box the rules show their state in the Alerting UI and reach nobody. Notification
needs a **contact point** (Slack webhook, email, PagerDuty, your own webhook) and a
**notification policy** to route to it. Both are sketched, commented out, at the bottom of the
rules file, with the `severity` label already set on every rule so warnings need not wake
anyone.

They are commented out on purpose: a provisioned `policies:` block **replaces Grafana's entire
notification policy tree**, including the default route. Where your alerts go is a decision for
you, not for a file you inherited.

---

## 14. Checklist

During a real incident, in order:

- [ ] **Set the time range** to when it was reported, ±20 minutes. Do this first, always
- [ ] Section ① — which tiles are red?
- [ ] Section ② — **when** did the colour change? That minute is your anchor
- [ ] Was there a deploy, a config change or a restart at that minute?
- [ ] Section ③ — which route? Now the problem has a name
- [ ] Section ④ — open a failed trace; read the exception type and the failing span
- [ ] Section ⑤ — is a dependency failing or slow at the same moment? Us, or them?
- [ ] Filter logs by `CorrelationId` or `trace_id` and read the whole request
- [ ] If data changed and someone is accountable: Kibana, by `resource` or `correlationId`
- [ ] Before closing: is the fix visible in the same panels that showed the problem?
- [ ] Before closing: would an alert have caught this? If not, add one — one, not five

---

## 15. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| Every panel "No data", Explore works | Data source uid mismatch — the dashboard names `prometheus`, `loki`, `tempo` | Recreate the data sources with those uids, or provision from `datasources.yaml` |
| Metric panels blank, Explore works | `metric_name_escaping_scheme: underscores` missing from `prometheus.yml` | Add it, then `curl -X POST http://localhost:9090/-/reload` |
| LogQL returns nothing and no error | A field was put inside `{ }`. Only `service_name` and `service_instance_id` are labels | Move it after the `\|` |
| Loki 400s on push | `allow_structured_metadata: true` missing | Add it to `limits_config` in `loki.yaml` |
| Traces missing, logs fine | Tempo unreachable (OTLP/gRPC :4317) or its retention expired | ⑤ outbound errors for `:4317` |
| Logs missing, traces fine | Loki unreachable (OTLP/HTTP :3100) | ⑤ outbound errors for `:3100` |
| Prometheus target DOWN | Firewall, wrong port, or the app is not running | `http://localhost:9090/targets` names the error |
| Kibana Discover empty | Time picker (defaults to 15m), or field casing | Widen the range; `actor` not `Actor`, `"Read"` not `"read"` |
| `histogram_quantile` returns `NaN` | No requests in the window — nothing to take a percentile of | Expected on an idle service |
| A percentile pinned to a bucket edge for hours | Bucket granularity, not a real plateau | Read it as "in this bucket" |
| Panel shows "No data" instead of 0 | The series does not exist yet | Append `or vector(0)` |
| Route variable empty | No traffic yet, so no `http_route` label values exist | Generate a request |
| Section ⑧ trace panel errors | A correlation id was pasted where a trace id is needed | Expected — use the logs panel |
| Dashboard changes vanish | The JSON is re-imported from the repo | Edit `observability/grafana/dashboard-cleanarch-api.json` and re-import |
| No alert rules in Grafana | The provisioning file was not picked up | It must sit in `<grafana>/conf/provisioning/alerting/`, and Grafana reads provisioning only at **startup** — restart it |
| "Service down" never fires though the app is off | The scrape job is not called `cleanarch-api` | Match the rule's `job=` to `job_name` in `prometheus.yml` |
| Alerts fire but nobody is told | No contact point or notification policy | Both are commented out in the rules file by default — see [§13](#13-the-four-alerts-worth-having) |
| No database spans in a trace | The query ran outside a request | Background queries are filtered out on purpose — see [§7](#7-traces--reading-a-waterfall) |

---

## 16. Cheat sheet

### URLs

| | |
|---|---|
| Grafana | <http://localhost:3000> |
| Prometheus — scrape health | <http://localhost:9090/targets> |
| The app's raw metrics | <http://localhost:5235/metrics> |
| Kibana — the audit trail | <http://localhost:5601> |
| Loki / Tempo | `:3100` / `:3200` — queried through Grafana, no UI of their own |

### The triage order

```
time range → ① tiles → ② when did it change → ③ which route → ④ why → ⑤ us or them
```

### PromQL

```promql
sum(rate(http_server_request_duration_seconds_count[$__rate_interval]))                      # throughput
sum by (http_route) (rate(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[$__rate_interval]))
histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))
sum(http_server_active_requests)                                                             # saturation
sum by (error_type) (rate(dotnet_exceptions_total[$__rate_interval]))
sum by (server_address) (rate(http_client_request_duration_seconds_count{error_type!=""}[$__rate_interval]))
up{job="cleanarch-api"}                                                                      # is it being scraped
```

### LogQL

```logql
{service_name="CleanArch.Api"}                                              # everything
{service_name="CleanArch.Api"} | detected_level =~ "warn|error"             # problems
{service_name="CleanArch.Api"} | CorrelationId = "<guid>"                   # one request
{service_name="CleanArch.Api"} | trace_id = "<32 hex>"                      # one request, from a trace
{service_name="CleanArch.Api"} | Request = "CreateStudent.Command"          # one feature
{service_name="CleanArch.Api"} |= `Handled` | ElapsedMs > 500               # slow handlers
sum by (Request) (count_over_time({service_name="CleanArch.Api"} |= `Handled` | Request != `` [$__auto]))
```
Labels: **`service_name`, `service_instance_id`. Nothing else.** Everything else goes after the `|`.

### TraceQL

```traceql
{resource.service.name = "CleanArch.Api" && span.http.response.status_code >= 500}
{span.http.route = "/students" && duration > 500ms}
{span.db.system = "sqlite" && duration > 100ms}     # slow queries, whatever ran them
{span.db.statement =~ ".*Students.*"}               # queries against one table
{status = error}
{name = "OnBehalfOf.Exchange"}
<paste a 32-character trace id>
```

### Kibana (audit)

```
resource : "Student/<id>"                     # everything that touched this person
category : "Read" and not actor : "system"    # who looked
action : "*" and succeeded : false            # what failed
correlationId : "<guid>"                      # the bridge to logs and traces
```
Fields camelCase · category values PascalCase · time field `occurredOnUtc` · widen the range.

### The ids

| Id | Where to get it | Where it works |
|---|---|---|
| **trace id** | `traceId` in an error body (middle of `00-…-…-01`), or `TraceID` on a log line | Tempo, Loki |
| **correlation id** | `X-Correlation-ID` response header | Loki, Kibana, outbox messages |

---

## 17. Glossary

| Term | Meaning |
|---|---|
| **Apdex** | A satisfaction score built from latency buckets. Not used here — the heatmap says more |
| **Bimodal** | Two distinct populations of behaviour in one metric. Percentiles describe neither |
| **Bucket** | A latency band in a histogram. Percentiles are estimated between bucket edges |
| **Cardinality** | The number of distinct label combinations. High cardinality is how you take down Prometheus |
| **Correlation id** | This app's per-request id. Caller-supplied or generated; reaches logs, audit and the outbox |
| **Exemplar** | A trace id attached to a metric sample, letting you jump from a graph to an example. Not enabled here |
| **Golden signals** | Rate, Errors, Duration, Saturation — what section ① shows |
| **Histogram** | A metric that counts observations into buckets, so percentiles can be estimated |
| **Label** | A dimension on a metric or a Loki stream. In Loki, only `service_name` and `service_instance_id` |
| **Percentile (p95)** | The value below which that share of requests fell |
| **RED method** | Rate, Errors, Duration — the three questions to ask of any request-serving service |
| **Saturation** | How full the system is. Here: in-flight requests, thread-pool queue, queued connections |
| **Scrape** | Prometheus fetching `/metrics`. The only *pull* signal in this stack |
| **Span** | One timed operation inside a trace. The root span is the whole request |
| **Structured metadata** | Loki's per-line fields. Filtered after the `\|`, not inside `{ }` |
| **Trace id** | The 32-hex id shared by every span of one request, and stamped on its log lines |
| **Traceparent** | The W3C header format `00-<trace id>-<span id>-01`. What `traceId` in an error body is |
| **Waterfall** | The span timeline view of a trace |

---

## Where to go next

| | |
|---|---|
| The instrumentation these signals come from | [50 — Instrumenting an application](50-instrumenting-an-application.md) |
| Building or fixing the stack itself | [90 — Observability server on Ubuntu](90-observability-server-ubuntu.md) |
| The audit trail in depth — what is recorded and why | [40 — Auditing](40-auditing.md) |
| Who the actor is, and why the trail can be trusted | [70 — Authentication](70-authentication.md) |
| Tracing a saga across its async hop | [60 — Talking across modules](60-talking-across-modules.md) |
