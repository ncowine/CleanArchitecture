# Observability from Scratch — A Beginner's Tutorial

> **Who this is for:** someone who has never set up monitoring before. No prior knowledge assumed.
> By the end you'll understand *what* each piece does, *why* it exists, and *how* to set the whole
> thing up yourself. We'll use plenty of analogies and build up slowly.

---

## Table of contents

1. [The 60-second mental model](#1-the-60-second-mental-model)
2. [Why do we even need this? (the problem)](#2-why-do-we-even-need-this-the-problem)
3. [The three kinds of data (signals)](#3-the-three-kinds-of-data-signals)
4. [The cast of characters (the tools)](#4-the-cast-of-characters-the-tools)
5. [How the data flows (and "push vs pull")](#5-how-the-data-flows-and-push-vs-pull)
6. [Setup part 1 — the storage services](#6-setup-part-1--the-storage-services)
7. [Setup part 2 — teaching the app to emit data](#7-setup-part-2--teaching-the-app-to-emit-data)
8. [Setup part 3 — connecting Grafana](#8-setup-part-3--connecting-grafana)
9. [Reading the dashboard](#9-reading-the-dashboard)
10. [Gotchas we hit (and how we fixed them)](#10-gotchas-we-hit-and-how-we-fixed-them)
11. [Glossary](#11-glossary)
12. [Where to go next (production)](#12-where-to-go-next-production)

---

## 1. The 60-second mental model

Imagine your application is a **car engine**. Observability is the **dashboard on the car** — the
speedometer, fuel gauge, and warning lights — plus the mechanic's diagnostic computer.

The engine doesn't know how to draw a speedometer. It just produces raw signals (RPM, temperature,
oil pressure). Something has to **collect** those signals, something has to **store** them, and
something has to **display** them on a nice dial you can read at a glance.

That's exactly our setup, in four moves:

```
   YOUR APP  ──emits──►  STORAGE  ──queried by──►  GRAFANA  ──►  YOU
  (the engine)          (databases)               (the dials)
```

- **Your app** produces raw telemetry (it's already been taught how — see part 7).
- **Storage** = three specialist databases, one per kind of data (Prometheus, Tempo, Loki).
- **Grafana** = the dashboard screen. It draws graphs by *asking questions* to the databases.

The single most important thing to understand: **Grafana stores nothing.** It is just a viewer.
Every graph is a question it sends to a database. If a graph is empty, either nobody put the data
in the database, or Grafana asked the wrong question.

---

## 2. Why do we even need this? (the problem)

When an app is small and running on your laptop, and something breaks, you look at the console
output and you can usually see what happened.

But real systems are different. A single button-click in a browser might travel through a web
server, three services, a database, and a message queue. When a user says *"the page was slow"* or
*"it gave an error,"* where do you even start? You can't `Console.WriteLine` your way out of that.

Observability answers three questions that every developer eventually asks:

1. **"Is the system healthy right now?"** → How many requests, how fast, how many errors.
2. **"Why was *this specific* request slow?"** → A step-by-step timeline of one request.
3. **"What exactly happened at 2:03pm when it broke?"** → The detailed log messages and errors.

Each question needs a different *kind* of data. That's our next topic.

---

## 3. The three kinds of data (signals)

The industry calls these "the three pillars" or "signals." Here's each one with an everyday analogy.

### 📊 Metrics — "the numbers"

**What it is:** numbers measured over time. Requests per second, average response time, memory used,
CPU percentage.

**Analogy:** the **gauges on your car dashboard**. The speedometer doesn't tell you *why* you're
going 60mph or *where* you're driving — just the number, updated continuously. Cheap to collect,
cheap to store, perfect for "is everything normal?" and for triggering alerts.

**Example in our app:** "the API is handling 12 requests/second and 95% of them finish under 5ms."

### 🔗 Traces — "the story of one request"

**What it is:** the complete journey of a *single* request as it moves through your code, with timing
for each hop.

**Analogy:** a **parcel tracking page**. "Arrived at depot 9:01 → out for delivery 9:15 → delivered
9:42." A trace shows: "request arrived → spent 2ms in auth → 40ms querying the database → 1ms
formatting the response." When one request is mysteriously slow, the trace shows you *which step*
ate the time.

**Example in our app:** "this `GET /` request took 213ms total; 210ms of it was the first-time
startup of the database connection."

### 📜 Logs — "the diary entries"

**What it is:** timestamped text messages the app writes as things happen. Exactly what you're used
to from the console — just collected centrally.

**Analogy:** a **ship's logbook** or a diary. "14:03:22 INFO executed SQL query. 14:03:25 ERROR
failed to dispatch message X." When you need the precise detail of what happened (including error
messages and stack traces), logs are where you look.

**Example in our app:** `Dead-lettering outbox message 0d51b2ef… (UnroutableTestMessage)`.

### Why keep them separate?

Because you *ask* about them completely differently. "Average latency over the last hour" (a math
question over numbers) is nothing like "show me the log lines from 2:03pm" (a text search). Each
kind gets its own purpose-built database, and Grafana stitches them together so you can jump from a
metric spike → to an example slow trace → to that trace's logs. That hand-off is the whole payoff.

---

## 4. The cast of characters (the tools)

Now let's meet the actual programs. There are five, and each has exactly one job.

| Tool | Plain-English job | Analogy | Runs on |
|------|-------------------|---------|---------|
| **OpenTelemetry** | The instrumentation *inside* your app that produces the three signals | The car's built-in sensors | (a library, part of the app) |
| **Prometheus** | Database that stores **metrics** (numbers over time) | The gauge recorder | `localhost:9090` |
| **Tempo** | Database that stores **traces** | The parcel-tracking archive | `localhost:3200` / `4317` |
| **Loki** | Database that stores **logs** | The logbook archive | `localhost:3100` |
| **Grafana** | The screen that draws graphs by querying the three databases | The dashboard display | `localhost:3000` |

A few notes so the names stop being scary:

- **OpenTelemetry (often "OTel")** is a *vendor-neutral standard*. It's a set of libraries that know
  how to watch your web framework and produce metrics/traces/logs in a common format. Because it's a
  standard, you could later swap Prometheus for a different metrics database without touching your app.
- **Prometheus, Tempo, Loki, and Grafana** are all made by (or popular with) **Grafana Labs**. People
  call the bundle the **"LGTM stack"** — **L**oki, **G**rafana, **T**empo, **M**imir. (Mimir is a
  bigger-scale metrics database; we use **Prometheus** instead, which does the same job and you
  already had it.)
- **OTLP** ("OpenTelemetry Protocol") is just the *language/format* the app uses to send data. Think
  of it as the shape of the envelope. Tempo and Loki both "speak OTLP," so the app can talk to them
  directly.

---

## 5. How the data flows (and "push vs pull")

Here's the actual wiring for our setup. Follow the arrows:

```
                          ┌──────────────────── Grafana (:3000) ─────────────────────┐
                          │   asks Prometheus   asks Tempo      asks Loki            │
                          └──────▲──────────────────▲──────────────────▲─────────────┘
                                 │ query            │ query            │ query
                          ┌──────┴──────┐   ┌────────┴─────┐   ┌────────┴─────┐
   METRICS               │ Prometheus  │   │    Tempo     │   │     Loki     │
    (numbers)            │   :9090     │   │  :3200/4317  │   │    :3100     │
                         └──────▲──────┘   └────────▲─────┘   └────────▲─────┘
                                │ SCRAPES           │ receives         │ receives
                                │ /metrics          │ (OTLP push)      │ (OTLP push)
                          ┌─────┴───────────────────┴──────────────────┴────────┐
                          │              CleanArch.Api (:5235)                   │
                          │   with OpenTelemetry producing all three signals     │
                          └───────────────────────────────────────────────────────┘
```

Notice that the three arrows into the databases are **not the same direction**. This trips up
everyone, so let's make it obvious.

### Push vs pull — the one concept to internalise

There are two ways data can get from the app into a database:

- **PUSH** — the app actively *sends* the data out. ("Here, take this.") **Tempo and Loki work this
  way.** The app holds their address (`localhost:4317`, `localhost:3100`) and pushes traces and logs
  to them.

- **PULL** — the database *comes and fetches* the data itself, on a schedule. ("I'll come get it.")
  **Prometheus works this way.** Every 15 seconds Prometheus visits a special web page the app
  exposes at `http://localhost:5235/metrics`, reads the current numbers, and records them.

This is why **metrics are configured differently** from traces/logs. For traces and logs, you tell
*the app* where the database is. For metrics, you tell *Prometheus* where the app is. Same goal,
opposite direction. It's not a mistake — it's just how Prometheus was designed (pulling scales
better and lets Prometheus notice when a target goes silent).

> **Quick check of understanding:** if the Tempo panel is empty, you check the *app's* config (is it
> pushing to the right address?). If a metrics panel is empty, you check *Prometheus's* config (is it
> scraping the right target?). Different culprit depending on the signal.

---

## 6. Setup part 1 — the storage services

The three storage services run as Docker containers, one command to start them
all. Rather than repeat the steps here, follow the development quickstart in
[`../observability/README.md`](../observability/README.md) — it is the same
stack this tutorial describes, already configured.

What matters for understanding it:

| Service | Port | Why that port |
|---------|------|---------------|
| Tempo | 4317 (ingest), 3200 (queries) | 4317 is the standard OTLP/gRPC port — the app pushes here |
| Loki | 3100 | one port does both: the app posts to `/otlp/v1/logs`, Grafana reads from the same API |
| Prometheus | 9090 | its UI, and where `/targets` shows whether scraping is working |
| Grafana | 3000 | the only one you actually browse day to day |

Each service gets a config file (`tempo.yaml`, `loki.yaml`, `prometheus.yml`)
that says which ports to listen on and where to store data — nothing more
exotic than that. They are short and commented; read them once and the shape of
the system is clear.

Come back here once `docker compose up -d` is running.

---

## 7. Setup part 2 — teaching the app to emit data

The storage services are running, but they're empty — nothing is sending them anything yet. Now we
make the application produce telemetry. This is the only part that touches code, and it's small.

### Step 7.1 — Add the OpenTelemetry packages

In the API project we reference these NuGet packages (versions are pinned centrally in
`Directory.Packages.props`):

- `OpenTelemetry.Extensions.Hosting` — the core wiring.
- `OpenTelemetry.Instrumentation.AspNetCore` — auto-watches incoming web requests.
- `OpenTelemetry.Instrumentation.Http` — auto-watches outgoing HTTP calls.
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` — sends traces & logs out over OTLP.
- `OpenTelemetry.Exporter.Prometheus.AspNetCore` — exposes the `/metrics` page Prometheus scrapes.

An **"exporter"** is the part that ships data *out* of the app to a destination. An
**"instrumentation"** is the part that *watches* something (like the web framework) and produces the
data in the first place.

### Step 7.2 — The wiring (`src/Api/CleanArch.Api/Observability.cs`)

This one method sets up all three signals. Read the comments — it maps 1:1 to everything above:

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("CleanArch.Api"))   // stamp every signal with the app's name
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()      // watch incoming requests
        .AddHttpClientInstrumentation()      // watch outgoing calls
        .AddOtlpExporter(o => {              // PUSH traces -> Tempo
            o.Endpoint = new Uri("http://localhost:4317");
            o.Protocol = OtlpExportProtocol.Grpc;
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddMeter("System.Runtime")          // .NET runtime metrics: GC, memory, thread pool, CPU
        .AddMeter(OutboxDiagnostics.MeterName)   // our own custom business metric
        .AddPrometheusExporter())            // expose /metrics for Prometheus to PULL
    .WithLogging(logging => logging
        .AddOtlpExporter(o => {              // PUSH logs -> Loki
            o.Endpoint = new Uri("http://localhost:3100/otlp/v1/logs");
            o.Protocol = OtlpExportProtocol.HttpProtobuf;
        }));
```

Then, one line in `Program.cs` actually publishes the metrics page:

```csharp
app.MapPrometheusScrapingEndpoint();   // creates the /metrics page at http://localhost:5235/metrics
```

The push destinations aren't truly hard-coded — they default to `localhost` but can be overridden
from `appsettings.json`:

```json
"Observability": {
  "Tempo": { "OtlpEndpoint": "http://localhost:4317" },
  "Loki":  { "OtlpEndpoint": "http://localhost:3100/otlp/v1/logs" }
}
```

### Step 7.3 — Run the app and confirm

```powershell
dotnet run --project src/Api/CleanArch.Api
```

Now generate a bit of traffic (open Swagger at <http://localhost:5235/swagger> and click a few
endpoints, or just refresh <http://localhost:5235/> a few times), then check the raw metrics page in
your browser: <http://localhost:5235/metrics>. You should see a wall of text with lines like
`http_server_request_duration_seconds_count …`. That's the app exposing its numbers for Prometheus
to collect.

> **Important beginner surprise:** a metric only appears *after it has happened at least once*. Before
> any web request, there's no "request duration" to report. So an empty page early on is normal —
> generate traffic first.

---

## 8. Setup part 3 — connecting Grafana

Grafana needs two things: **data sources** (where the three stores live) and a
**dashboard** (the graphs). Both are already set up for you by *provisioning* —
Grafana reads `observability/grafana/provisioning/` at startup and creates the
three connections, wires the trace↔log links, and imports
`dashboard-cleanarch-api.json`. Nothing to click.

Worth knowing because it explains what you are looking at: a "data source" is
just a saved connection to one of the three stores, and the dashboard JSON
refers to them by the stable ids `prometheus`, `tempo` and `loki` — which is why
those ids must not be renamed.

You now have the full loop: **app → stores → Grafana → your eyes.**

---

## 9. Reading the dashboard

The dashboard is organised the way professionals structure them, using the **RED method** — for each
service you watch **R**ate, **E**rrors, and **D**uration. Sections top to bottom:

- **🚦 Golden signals** — six color-coded tiles (green = good, red = bad) answering "healthy right
  now?": request rate, error rate, p95 & p99 latency, in-flight requests, CPU%.
- **📈 Traffic & Errors** — requests/sec broken down by status code and by route.
- **🏆 Top endpoints** — a table: which endpoints are busiest, their **average** and **p95** response
  time, and error %. (Average = typical cost; p95 = the slow tail the average hides. Watch both.)
- **⏱️ Latency** — p50/p95/p99 over time. A big gap between p50 and p99 means some requests are far
  slower than typical.
- **⚠️ Exceptions & Errors** — count of exceptions, exceptions by type, and an **error-only log feed**
  with stack traces (the actionable one).
- **🧠 .NET Runtime** — CPU, memory, garbage collection, thread pool. The "engine internals."
- **📮 Outbox** — a business metric: domain events being dispatched; dead-lettered should stay 0.
- **📜 Logs** — the full live log stream.

Every panel has an **ⓘ** in its corner — hover it for a one-line explanation of what to look for.

**Two things that are *correctly* quiet:**
- **Error rate 0%** with no 5xx line — good, the app isn't throwing server errors. (404s are *client*
  errors and show in the status-code panel, not here.)
- A panel with "No data" for something that hasn't happened yet (e.g. no domain events dispatched) —
  not broken, just nothing to show.

---

## 10. Gotchas we hit (and how we fixed them)

Real setups hit snags. Here are the exact ones we ran into, so you recognise them.

### "No data" on the metrics panels — the UTF-8 name mismatch

**Symptom:** logs worked, but every metric graph said "No data," even though the app's `/metrics`
page clearly had the numbers.

**Cause:** Prometheus 3.x defaults to a new mode that keeps OpenTelemetry's original metric names,
which contain **dots** (`http.server.request.duration`). But standard dashboards ask for the classic
names with **underscores** (`http_server_request_duration_seconds`). Same metric, two spellings — so
the query matched nothing.

**Fix:** one line in `prometheus.yml` tells Prometheus to use the classic underscore names:

```yaml
global:
  metric_name_escaping_scheme: underscores
```

(Note: trying `metric_name_validation_scheme: legacy` alone fails to load with *"utf8 metric names
requested but validation scheme is not set to UTF8"* — use the escaping-scheme line above instead.)

### A brand-new panel is empty even though everything's wired

Two normal reasons, not bugs:
1. **A metric doesn't exist until it happens once.** No requests yet → no request metrics.
2. **Rates need two data points.** A graph of "requests per second" needs at least two scrapes
   (~30s apart) before it can compute a slope. Give it a moment with steady traffic.

### Tempo wouldn't start — config schema mismatch

**Symptom:** `field ingester not found in type app.Config`. **Cause:** we'd copied config keys from a
different Tempo version. **Fix:** remove the unknown keys; the essentials (server, receiver, storage)
are all Tempo needs, and it has sensible defaults for the rest.

---

## 11. Glossary

| Term | Plain meaning |
|------|---------------|
| **Observability** | Being able to understand what a system is doing from the data it emits. |
| **Telemetry** | The data the app emits about itself (metrics, traces, logs). |
| **Signal** | One of the three kinds of telemetry (metrics / traces / logs). |
| **Metric** | A number measured over time (e.g. requests/sec). |
| **Trace** | The timed journey of one request through the code. |
| **Span** | One step within a trace (e.g. "the database query"). |
| **Log** | A timestamped text message. |
| **OpenTelemetry (OTel)** | The vendor-neutral libraries inside the app that produce telemetry. |
| **OTLP** | The standard format/protocol for shipping telemetry. |
| **Exporter** | The part of the app that sends telemetry *out* to a destination. |
| **Instrumentation** | The part that *watches* something and produces telemetry. |
| **Scrape** | Prometheus fetching the `/metrics` page. |
| **Pull** | The database fetches data itself (Prometheus). |
| **Push** | The app sends data out (to Tempo, Loki). |
| **Data source** | A saved connection from Grafana to a database. |
| **Panel** | One graph/tile on a dashboard. |
| **Percentile (p95)** | "95% of requests were faster than this value." |
| **RED method** | Structuring a dashboard around Rate, Errors, Duration. |
| **LGTM stack** | Loki, Grafana, Tempo, Mimir — the Grafana observability bundle. |
| **Dead-letter** | A message that failed so many times it's set aside instead of retried forever. |

---

## 12. Where to go next (production)

Everything here is a **local development** setup: single-node, storing to local files, no security.
The production stack in [`../observability/prod`](../observability/prod) is the first step up — same
four services, plus retention, passwords and a network boundary. Beyond that the shape stays the
same, with a few upgrades:

- **Add a collector in the middle.** Put **Grafana Alloy** (or the OpenTelemetry Collector) between
  the app and the databases. The app then pushes to *one* address (the collector), and the collector
  fans out to Prometheus/Tempo/Loki. This lets you batch, sample, and re-route without changing app
  code — you'd just point the `Observability` endpoints at the collector.
- **Durable, scalable storage.** Swap local-file storage for object storage (S3/GCS/Azure Blob), and
  consider **Mimir** instead of a single Prometheus for large metric volumes.
- **Retention & alerts.** Configure how long data is kept, and add **alerting rules** so you get
  paged when the error rate crosses a threshold — instead of watching the dashboard by hand.
- **Security.** Turn on authentication, TLS, and multi-tenancy (all disabled here for simplicity).

But the mental model never changes: **the app emits, the databases store, Grafana shows.** Once that
clicks, every observability system you'll ever meet is a variation on this same theme.

---
