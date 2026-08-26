# Instrumenting an Application

**Who this is for:** someone whose application runs fine on their machine and is a black box
anywhere else.

**What you'll be able to do by the end:** wire an application to emit metrics, traces and
logs; add a metric that measures something your business cares about; make one request
traceable across every signal and across an async hop; and know which knob to turn when a
graph is empty.

**What you need first:** somewhere to send the telemetry. In development that's
`observability/dev` — one `docker compose up -d`. In production it's the server built in
[guide 90](90-observability-server-ubuntu.md). This guide is about the **application** half.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [Three signals, three questions](#1-three-signals-three-questions) | Know which one you need |
| 2 | [Push and pull](#2-push-and-pull) | The asymmetry that trips everyone |
| 3 | [What you get for free](#3-what-you-get-for-free) | Before you write anything |
| 4 | [Step 1 — The wiring](#4-step-1--the-wiring) | One method, explained |
| 5 | [Step 2 — Point it somewhere](#5-step-2--point-it-somewhere) | Configuration, and the secret |
| 6 | [Step 3 — Correlation](#6-step-3--correlation) | Tie the signals together |
| 7 | [Step 4 — Add your own metric](#7-step-4--add-your-own-metric) | The valuable part |
| 8 | [Step 5 — Add your own span](#8-step-5--add-your-own-span) | When a trace is too coarse |
| 9 | [Step 6 — Logs worth searching](#9-step-6--logs-worth-searching) | Structured, not interpolated |
| 10 | [Health checks](#10-health-checks) | Liveness vs readiness |
| 11 | [Securing /metrics](#11-securing-metrics) | It is not harmless |
| 12 | [Verify it](#12-verify-it) | Per signal, not in general |
| 13 | [The checklist](#13-the-checklist) | Run this when doing it for real |
| 14 | [Troubleshooting](#14-troubleshooting) | Symptom, cause, fix |
| 15 | [Cheat sheet](#15-cheat-sheet) | Settings and snippets |
| 16 | [Glossary](#16-glossary) | Every term used in this guide |

---

## 1. Three signals, three questions

Instrumentation exists to answer three different questions, and each needs a different shape
of data.

| Signal | Answers | Shape | Kept for |
|---|---|---|---|
| **Metrics** | "Is it healthy *right now*?" | Numbers over time | A long time — they're tiny |
| **Traces** | "Why was *this one* request slow?" | A nested timeline per request | Weeks |
| **Logs** | "What exactly happened at 2:03?" | Timestamped text | Days to weeks |

**Metrics** are cheap and aggregate: 12 requests/second, p95 of 40ms, 300 MB in use. They
tell you *that* something changed, never *what* or *for whom* — a counter has no idea who
Mrs Smith is.

**Traces** are the opposite: one request, in detail. "240ms total — 190ms in the database
query, 30ms serialising." When something is mysteriously slow, the trace shows which step
ate the time.

**Logs** are the detail of record. Error messages, stack traces, the line where the code
said what it was doing.

You need all three, and the payoff is the hand-off between them: a metric spike → an example
slow trace → that trace's log lines. [Chapter 6](#6-step-3--correlation) is what makes that
hand-off work.

---

## 2. Push and pull

The one asymmetry to internalise, because it explains most configuration confusion.

**Traces and logs are pushed.** The application holds the address of the store and sends
data out.

**Metrics are pulled.** The application publishes a page at `/metrics` and waits.
Prometheus dials *in*, every 15 seconds, and reads it.

```
   YOUR APP                                     TELEMETRY STORES
   ┌──────────────────────┐                     ┌──────────────────────┐
   │                      │ ─── traces ───────► │  Tempo   :4317       │
   │  OpenTelemetry       │ ─── logs ─────────► │  Loki    :3100       │
   │                      │                     │                      │
   │  GET /metrics        │ ◄── scrape ──────── │  Prometheus :9090    │
   └──────────────────────┘                     └──────────────────────┘
```

The consequences are practical:

| | Configured where | Firewall direction |
|---|---|---|
| Traces, logs | **In the app** — it needs the store's address | App → store |
| Metrics | **In Prometheus** — it needs the app's address | Store → app |

So there is no "metrics endpoint" setting in your application configuration, and that is not
an omission. If metrics are missing but traces work, the problem is on Prometheus's side, or
in the firewall between them — never in your app's settings.

---

## 3. What you get for free

Before writing any custom instrumentation, the automatic instrumentation already gives you:

| From | What |
|---|---|
| `AddAspNetCoreInstrumentation()` | Every incoming request: route, status code, duration — as both a metric and a trace span |
| `AddHttpClientInstrumentation()` | Every outgoing HTTP call, as a child span |
| `AddMeter("System.Runtime")` | GC collections, heap size, thread pool depth, CPU, exceptions |
| `WithLogging(...)` | Every `ILogger` call, shipped out with its structured properties |

That covers "how busy, how fast, how broken" for the whole HTTP surface without a single
line of your own. Custom instrumentation is for what those cannot see: **your business**.

> **A beginner surprise worth knowing up front:** a metric does not exist until it has
> happened at least once. Before the first request there is no request-duration metric, so
> `/metrics` looks alarmingly empty. Generate some traffic before concluding anything is
> broken.

---

## 4. Step 1 — The wiring

One method sets up all three signals. This is
`src/Api/CleanArch.Api/Observability.cs`, trimmed to its structure:

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(ServiceName))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri(tempoEndpoint);
            otlp.Protocol = OtlpExportProtocol.Grpc;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("System.Runtime")
        .AddMeter(OutboxDiagnostics.MeterName)
        .AddPrometheusExporter())
    .WithLogging(logging => logging
        .AddOtlpExporter(otlp =>
        {
            otlp.Endpoint = new Uri(lokiEndpoint);
            otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
        }));
```

Line by line, because each one is a decision:

**`ConfigureResource(... AddService(ServiceName))`** stamps every signal with the
application's name. This is what makes `{service_name="CleanArch.Api"}` work as a Loki
query and what separates your data from another service's in a shared store. Get it wrong
and everything still works, invisibly mixed together.

**`AddOtlpExporter` with `Grpc` for traces.** Tempo's OTLP/gRPC receiver is on 4317. gRPC
uses no URL path, so the endpoint is used verbatim.

**`AddOtlpExporter` with `HttpProtobuf` for logs.** Loki's OTLP endpoint is HTTP, and the
endpoint **already includes the signal path** — `http://host:3100/otlp/v1/logs`. That
trailing path is the single most common configuration mistake here.

**`AddPrometheusExporter()`** does *not* send anything. It creates the in-memory registry
that `/metrics` renders. The endpoint itself is mapped separately in `Program.cs`:

```csharp
var metricsEndpoint = app.MapPrometheusScrapingEndpoint().DisableRateLimiting();
```

`DisableRateLimiting()` matters: a scraper hitting the same endpoint every 15 seconds from
one address looks exactly like abuse to a per-IP rate limiter, and a throttled scrape shows
up as mysterious gaps in your graphs.

**`AddMeter(...)`** subscribes to a named meter. `"System.Runtime"` is .NET's built-in one;
`OutboxDiagnostics.MeterName` is this application's own. **A meter that is not subscribed
here emits nothing** — this line is how a custom metric gets turned on, and forgetting it is
[chapter 7](#7-step-4--add-your-own-metric)'s classic failure.

One more, easy to miss:

```csharp
services.Configure<OpenTelemetryLoggerOptions>(options =>
{
    options.IncludeFormattedMessage = true;
    options.IncludeScopes = true;
});
```

Without `IncludeFormattedMessage`, Loki receives the raw message *template*
(`"Student {StudentId} enrolled"`) rather than the rendered text. Searching for a student id
in the message then finds nothing. `IncludeScopes` carries the logging scope — including the
correlation id — as attributes.

---

## 5. Step 2 — Point it somewhere

Two settings, and they are the only ones the app needs:

```json
"Observability": {
  "Tempo": { "OtlpEndpoint": "http://localhost:4317" },
  "Loki":  { "OtlpEndpoint": "http://localhost:3100/otlp/v1/logs" }
}
```

In production, override them per environment:

```
Observability__Tempo__OtlpEndpoint = http://10.20.30.40:4317
Observability__Loki__OtlpEndpoint  = http://10.20.30.40:3100/otlp/v1/logs
```

Defaults point at `localhost`, so a developer with the dev stack running needs no
configuration at all.

### The setting for when the stores are on another host

Tempo and Loki have **no authentication of their own**. On a developer machine that's fine —
they're on loopback. Across a network it means anything that can reach port 3100 can write
whatever it likes into your logs.

If a proxy in front of them checks a credential, the app can send one with every export:

```
Observability__Tempo__Headers = "Authorization=Basic <base64 of user:password>"
```

The format is `key=value,key2=value2`. **This is a secret** — supply it from an environment
variable or a secret store, never from `appsettings.json`.

If there is no proxy, the network boundary is the security: keep the stores off the public
internet and firewall them to the application host. That is the approach
[guide 90](90-observability-server-ubuntu.md) takes.

---

## 6. Step 3 — Correlation

Signals are only worth three times as much as one signal if you can move between them. The
mechanism is a **correlation id**: one value that appears on the request, its logs, its audit
record, and any message it produces.

```csharp
public const string HeaderName = "X-Correlation-ID";

public async Task InvokeAsync(HttpContext context, ICorrelationContext correlation, ILogger<...> logger)
{
    var correlationId = context.Request.Headers[HeaderName].ToString();
    if (string.IsNullOrWhiteSpace(correlationId))
        correlationId = Guid.NewGuid().ToString();

    correlation.Set(correlationId);
    context.Response.Headers[HeaderName] = correlationId;

    using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await _next(context);
    }
}
```

Four things in ten lines:

1. **Reuses an inbound id** if the caller supplied one — so a chain of services shares one id
   rather than each inventing its own.
2. **Generates one** otherwise.
3. **Echoes it on the response**, so a client (or a support ticket) can quote the exact id.
4. **Opens a logging scope**, so every log line in the request carries it without anyone
   remembering to add it.

The middleware runs early in the pipeline, and the id then flows on its own:

```
  HTTP request ──► correlation id set
        │
        ├──► every log line in the request       (via the logging scope)
        ├──► the audit record                    (AuditBehavior reads ICorrelationContext)
        └──► any outbox message enqueued         (OutboxWriter stamps it on the row)
                     │
                     └──► restored by the OutboxProcessor before dispatch
                              └──► the consumer's logs, minutes later, in another module
```

**Why this matters:** an async hop is where a trail normally goes cold. Someone investigating
follows the request to the point where it enqueued a message and stops. Because the processor
restores the id before dispatch, one search still returns the whole flow — both legs of a
saga included.

Traces get the same treatment automatically: OpenTelemetry puts `trace_id` on log records, and
Grafana's data sources are configured to link in both directions — a trace has a "logs for
this trace" button, and a `trace_id` in a log line is a clickable link back to the trace.

---

## 7. Step 4 — Add your own metric

Automatic instrumentation measures the *plumbing*. Only you can measure the *business*.

The pattern, from `src/BuildingBlocks.Outbox/OutboxDiagnostics.cs`:

```csharp
public static class OutboxDiagnostics
{
    public const string MeterName = "CleanArch.Outbox";

    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Delivered    = Meter.CreateCounter<long>("outbox.delivered");
    public static readonly Counter<long> Failed       = Meter.CreateCounter<long>("outbox.failed");
    public static readonly Counter<long> DeadLettered = Meter.CreateCounter<long>("outbox.dead_lettered");
}
```

Record where the thing happens:

```csharp
OutboxDiagnostics.Delivered.Add(1, new KeyValuePair<string, object?>("db", context));
```

**Then subscribe the meter**, or nothing is exported:

```csharp
.WithMetrics(metrics => metrics
    .AddMeter(OutboxDiagnostics.MeterName))     // ← without this line, the counter is invisible
```

That is the single most common custom-metric bug: the counter increments happily, the code is
correct, and the graph is empty because nobody subscribed the meter.

### Choosing an instrument

| Instrument | Use for | Example |
|---|---|---|
| `Counter<T>` | Things that only go up | Messages delivered, orders placed |
| `Histogram<T>` | Distributions — you want percentiles | Time to fulfil an order |
| `UpDownCounter<T>` | Things that go both ways | Items in a queue |
| `ObservableGauge<T>` | A value you sample on demand | Current connection-pool size |

### Tags, and the trap in them

Tags (`("db", context)` above) split one metric into series you can group by. They are the
difference between "delivery failures" and "delivery failures *for the Library module*".

**The trap: never tag with something unbounded.** A tag whose value is a user id, an order
id, or a raw URL creates one time series per distinct value. That is called a *cardinality
explosion*, and it is the standard way to take down a Prometheus. Tag with things that have
tens of values, not millions.

| Good tag | Bad tag |
|---|---|
| module name, status code, message type | user id, order id, correlation id, full URL |

### What is worth measuring

Ask: *"if this number moved, would someone do something about it?"* If not, don't collect it.
Good business metrics are the ones an operator would want alerting on — dead-lettered
messages, failed payments, orders stuck in a state.

`outbox_dead_lettered_total` is the model here: it should always be zero, so any non-zero
value is actionable by definition.

---

## 8. Step 5 — Add your own span

Automatic tracing shows one span per request and one per outgoing HTTP call. When the
interesting work happens *inside* your handler, add a span of your own:

```csharp
private static readonly ActivitySource Activity = new("CleanArch.Enrollment");

using var activity = Activity.StartActivity("promote-waitlist");
activity?.SetTag("section.id", sectionId);
activity?.SetTag("waitlist.length", waiting.Count);
```

Then subscribe the source, exactly as with meters:

```csharp
.WithTracing(tracing => tracing.AddSource("CleanArch.Enrollment"))
```

Note the `?.` — `StartActivity` returns `null` when nothing is listening, which is the normal
state when tracing is off. Code that dereferences it unconditionally crashes in exactly the
configuration you were least likely to test.

**When to bother:** when a request has a slow step that isn't a database call or an HTTP call,
and "the handler took 800ms" doesn't tell you which part. Don't span every method — a trace
with two hundred spans is as unreadable as one with none.

---

## 9. Step 6 — Logs worth searching

Log with **structured properties**, not string interpolation:

```csharp
// Yes — StudentId arrives as a searchable field
_logger.LogInformation("Student {StudentId} enrolled in {SectionId}", studentId, sectionId);

// No — the values are baked into one opaque string
_logger.LogInformation($"Student {studentId} enrolled in {sectionId}");
```

The first produces a log record with `StudentId` and `SectionId` as fields you can filter on.
The second produces a sentence you can only substring-match. In a log store the difference is
between a query and a grep.

For hot paths, the source-generated form avoids allocating when the level is disabled — the
pattern used throughout this codebase:

```csharp
internal static partial class OutboxLog
{
    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Dead-lettering outbox message {MessageId} ({MessageType}) for {Context} after {Attempts} failed attempts.")]
    public static partial void DeadLettered(
        ILogger logger, Exception exception, Guid messageId, string messageType, string context, int attempts);
}
```

Two rules that matter more than the formatting:

- **Never log a secret.** Passwords, tokens, API keys, and anything a regulator would call
  personal data. Logs go to more places, and are read by more people, than you expect.
- **Log levels mean something.** `Error` is "a human should look at this". If your `Error`
  volume is such that nobody looks, you have no error logging at all — just noise with a
  severity field.

---

## 10. Health checks

Two endpoints, and the difference between them matters to whatever restarts your process:

```csharp
app.MapHealthChecks("/health").DisableRateLimiting();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
   .DisableRateLimiting();
```

| Endpoint | Checks | Answers |
|---|---|---|
| `/health` | Dependencies — the databases | "Can I serve traffic?" (**readiness**) |
| `/health/live` | Nothing — `Predicate = _ => false` | "Is the process alive?" (**liveness**) |

**Why liveness must check nothing:** if your liveness probe checks the database and the
database goes down, the orchestrator concludes your application is broken and restarts it.
Repeatedly. You now have a database outage *and* a crash-looping application. Liveness answers
one question — is the process running — and readiness handles the rest.

Registered like this:

```csharp
services.AddHealthChecks()
    .AddDbContextCheck<StudentsDbContext>("students-db")
    .AddDbContextCheck<LibraryDbContext>("library-db");
```

Both are exempt from rate limiting, for the same reason as `/metrics`: probes come
relentlessly from one address.

---

## 11. Securing `/metrics`

`/metrics` is not a harmless diagnostic page. It enumerates **every route in your
application**, with request rates and error counts per route. For anyone probing your system
it is an excellent map.

Default: anonymous, which is right when the scraper and the app share a host. When they don't,
there are two controls and you want both:

**1. Network** — allow only the scraper's address inbound. On Windows:

```powershell
New-NetFirewallRule -DisplayName "Prometheus scrape" -Direction Inbound -Protocol TCP `
  -LocalPort 5235 -RemoteAddress 10.20.30.40 -Action Allow
```

**2. A credential** — turn on the built-in switch:

```csharp
if (app.Configuration.GetValue<bool>("Observability:Metrics:RequireAuthentication"))
{
    metricsEndpoint.RequireAuthorization();
}
```

Set `Observability__Metrics__RequireAuthentication=true`, mint a key for the scraper, and
configure Prometheus to send it as `X-Api-Key`.

> **Change one end at a time.** Enable the requirement before the scraper sends the header and
> the target goes DOWN; add the header first and it is simply ignored. Do the scraper first,
> then the app — a mismatched credential produces a target that fails silently, which looks
> exactly like a network problem.

---

## 12. Verify it

Check each signal separately. Three of the four can be broken while the fourth looks perfect.

**Metrics** — the raw page first, then the scraper:

```bash
curl http://localhost:5235/metrics | head -20     # expect http_server_request_duration_seconds_*
```

Then Prometheus at `/targets` — your job must be **UP**.

**Traces** — Grafana → Explore → Tempo → Search → last 15 minutes. Click one; you should get
a timeline.

**Logs** — Grafana → Explore → Loki:

```
{service_name="CleanArch.Api"}
```

If that returns nothing but traces work, check `AddService(ServiceName)` — the label comes
from there.

**The links** — open a log line, find its `TraceID` field, click through to Tempo. That link
working proves the whole chain: both exporters, the resource attributes, and both data-source
configurations.

Generate traffic first, and wait two minutes before concluding anything is missing.

---

## 13. The checklist

Wiring:

- [ ] `ConfigureResource(... AddService(name))` — the name you'll query by
- [ ] Tracing: ASP.NET Core + HttpClient instrumentation, OTLP/gRPC exporter
- [ ] Metrics: instrumentation, `System.Runtime`, your own meters, Prometheus exporter
- [ ] Logging: OTLP/HTTP exporter, endpoint **including** `/otlp/v1/logs`
- [ ] `IncludeFormattedMessage = true` and `IncludeScopes = true`
- [ ] `MapPrometheusScrapingEndpoint()` with `DisableRateLimiting()`

Correlation:

- [ ] Correlation middleware registered early, before anything that logs
- [ ] Id echoed on the response
- [ ] Async work restores the id before doing its work

Custom telemetry:

- [ ] Every custom meter passed to `AddMeter(...)`; every `ActivitySource` to `AddSource(...)`
- [ ] No unbounded tag values
- [ ] Logs use structured properties, never interpolation
- [ ] No secrets or personal data in any log

Operations:

- [ ] `/health` checks dependencies; `/health/live` checks nothing
- [ ] `/metrics` restricted by network, and by credential if the scraper is remote
- [ ] All four signals verified individually, including the trace ↔ log link

---

## 14. Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|
| `/metrics` is empty | No traffic yet — metrics don't exist until they happen | Generate requests |
| Prometheus target DOWN, connection refused | Wrong port, or the app isn't running | Check the scrape config |
| Prometheus target DOWN, deadline exceeded | Firewall dropping the scrape | Inbound rule on the app host |
| Metrics fine, no traces or logs | The app can't reach the stores | Check `Observability__*` really applied — a missed restart is the usual cause |
| Logs rejected with 400 | Endpoint missing `/otlp/v1/logs`, or the store rejects OTLP | Check the full URL |
| Log messages are templates, not text | `IncludeFormattedMessage` not set | [Chapter 4](#4-step-1--the-wiring) |
| Loki query returns nothing | `service_name` doesn't match | It comes from `AddService(...)` |
| Custom metric never appears | Meter not subscribed | `AddMeter(YourDiagnostics.MeterName)` |
| Custom span never appears | Source not subscribed | `AddSource("Your.Source")` |
| `NullReferenceException` on an activity | `StartActivity` returns null with no listener | Use `activity?.SetTag(...)` |
| Prometheus memory climbing | Cardinality explosion from an unbounded tag | Remove the high-cardinality tag |
| Dashboard panels empty, Explore works | Metric-name mismatch (dots vs underscores) | `metric_name_escaping_scheme: underscores` in `prometheus.yml` |
| Gaps in graphs at regular intervals | Scrapes being rate-limited | `DisableRateLimiting()` on the metrics endpoint |

---

## 15. Cheat sheet

### Settings

| Key | Purpose |
|---|---|
| `Observability__Tempo__OtlpEndpoint` | Traces target. gRPC, no path — `http://host:4317` |
| `Observability__Loki__OtlpEndpoint` | Logs target. HTTP, **with** path — `http://host:3100/otlp/v1/logs` |
| `Observability__Tempo__Headers` | Optional export headers, `key=value,key2=value2`. **Secret** |
| `Observability__Loki__Headers` | Same, for logs |
| `Observability__Metrics__RequireAuthentication` | Require a credential on `/metrics` |

### A custom metric

```csharp
public static class MyDiagnostics
{
    public const string MeterName = "MyApp.Orders";
    private static readonly Meter Meter = new(MeterName);
    public static readonly Counter<long> Placed = Meter.CreateCounter<long>("orders.placed");
}

MyDiagnostics.Placed.Add(1, new KeyValuePair<string, object?>("channel", channel));

.WithMetrics(m => m.AddMeter(MyDiagnostics.MeterName))     // required
```

### A custom span

```csharp
private static readonly ActivitySource Activity = new("MyApp.Orders");

using var activity = Activity.StartActivity("reserve-stock");
activity?.SetTag("order.lines", lines.Count);

.WithTracing(t => t.AddSource("MyApp.Orders"))             // required
```

### Endpoints

| Path | Purpose |
|---|---|
| `/metrics` | Prometheus scrapes this |
| `/health` | Readiness — checks the databases |
| `/health/live` | Liveness — process only |

### Queries to know

```promql
sum(rate(http_server_request_duration_seconds_count[$__rate_interval]))
histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))
```
```logql
{service_name="CleanArch.Api"} |= "error"
```

---

## 16. Glossary

| Term | Meaning |
|---|---|
| **Auto-instrumentation** | Libraries that watch a framework and emit telemetry with no code from you |
| **Cardinality** | How many distinct series a metric has. High cardinality kills metric stores |
| **Correlation id** | One value shared by every signal from one request, across async hops |
| **Counter** | An instrument that only increases |
| **Exporter** | The component that ships telemetry out of the process |
| **Histogram** | An instrument recording a distribution, so percentiles can be computed |
| **Instrument** | One measuring device — a counter, histogram, gauge |
| **Liveness** | "Is the process alive?" Must not check dependencies |
| **Meter** | A named group of instruments. Must be subscribed to be exported |
| **Metric** | A number measured over time |
| **OpenTelemetry (OTel)** | The vendor-neutral standard for producing telemetry |
| **OTLP** | OpenTelemetry Protocol — the wire format for exporting |
| **p95** | "95% of requests were faster than this" |
| **Pull** | The store fetches from the app. How metrics work |
| **Push** | The app sends to the store. How traces and logs work |
| **Readiness** | "Can I serve traffic?" Checks dependencies |
| **Resource attributes** | Properties stamped on every signal — chiefly the service name |
| **Scrape** | One round of Prometheus fetching `/metrics` |
| **Span** | One step within a trace |
| **Structured logging** | Logging with named properties rather than interpolated strings |
| **Tag / label** | A key-value pair splitting a metric into series |
| **Trace** | The full nested timeline of one request |

---

## Where to go next

- **[Observability server on Ubuntu](90-observability-server-ubuntu.md)** — the other half:
  the stores this application ships to.
- **[Auditing](40-auditing.md)** — a fourth signal with different rules, and why it doesn't
  live in your logs.
- **[Talking across modules](60-talking-across-modules.md)** — where the correlation id earns
  its keep.
