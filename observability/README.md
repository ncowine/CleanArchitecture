# Observability & audit stack

Docker hosts the telemetry stores. The API never runs in Docker — in development
you start it from Visual Studio or `dotnet run`, in production it runs on IIS on
a Windows Server. Both environments are therefore the same shape, and there are
exactly two examples to choose from:

| | [`dev/`](dev) | [`prod/`](prod) |
|---|---|---|
| Runs on | Docker Desktop, your machine | a Docker host on the internal network |
| Ports published on | `127.0.0.1` | the LAN address of that host |
| Authentication | none | Grafana + Elasticsearch have passwords; Tempo/Loki rely on the network |
| Retention | unbounded | 30 days, everywhere |
| Elasticsearch + Kibana | optional (`--profile elk`) | always on |
| Secrets | none | `.env`, from [`prod/.env.example`](prod/.env.example) |

Concepts (metrics vs traces vs logs, push vs pull) are in
[`../docs/observability-tutorial.md`](../docs/observability-tutorial.md).
The application side of a production deploy — environment, databases, firewall —
is in [`../docs/deploy-iis.md`](../docs/deploy-iis.md).

## What talks to what

```
  Windows                                        Docker host
  ┌──────────────────────────┐                   ┌──────────────────────────────┐
  │  CleanArch.Api           │ ──── traces ────▶ │  Tempo          :4317 / :3200 │
  │                          │ ──── logs   ────▶ │  Loki           :3100         │
  │  dev : dotnet run        │ ──── audit  ────▶ │  Elasticsearch  :9200         │
  │  prod: IIS               │                   │                              │
  │                          │ ◀─── scrape ───── │  Prometheus     :9090         │
  │  GET /metrics            │                   │                              │
  └──────────────────────────┘                   │  Grafana :3000   Kibana :5601 │
                                                 └──────────────────────────────┘
```

Three of the four signals are **pushed** by the app; metrics are **pulled** —
Prometheus dials the app, not the other way round. That single asymmetry causes
most of the firewall confusion, so it is worth remembering.

## Layout

```
observability/
  dev/     docker-compose.yml  .env.example  tempo.yaml  loki.yaml  prometheus.yml
  prod/    docker-compose.yml  .env.example  tempo.yaml  loki.yaml  prometheus.yml  backup.sh
  grafana/ dashboard-cleanarch-api.json      provisioning/   ← shared by both
```

---

## Development

```bash
cd observability/dev
cp .env.example .env            # PowerShell: copy .env.example .env
docker compose up -d            # Tempo, Loki, Prometheus, Grafana
```

Add the audit trail when you need it — Elasticsearch is ~1 GB of RAM, so it is
off by default:

```bash
docker compose --profile elk up -d
```

Without it the audit trail still works: shipping to Elasticsearch fails, and the
records fall back to the normal logs, which reach Loki. To skip the failed
attempts entirely, set `Audit__Elasticsearch__Uri` to an empty string — the
Elasticsearch sink is then never registered.

Then start the API normally:

```bash
dotnet run --project src/Api/CleanArch.Api
```

**No app configuration is needed.** The defaults in `appsettings.json` already
point at `localhost:4317`, `localhost:3100` and `localhost:9200`, which is where
the dev stack publishes itself.

Open:

| | |
|---|---|
| Grafana — dashboard, traces, logs | <http://localhost:3000> (no login) |
| Kibana — audit trail | <http://localhost:5601> (no login) |
| Prometheus — scrape health at `/targets` | <http://localhost:9090> |

Stop with `docker compose down`, or `docker compose down -v` to also throw away
the collected telemetry.

---

## Production

The Docker host and the IIS server both sit on the internal network. Nothing in
this stack is exposed publicly, and nothing here terminates TLS.

> Doing this on a fresh Ubuntu server, without Docker experience and without a
> copy of this repository? Follow
> [`../tutorials/90-observability-server-ubuntu.md`](../tutorials/90-observability-server-ubuntu.md)
> instead — it reproduces every file below from scratch and explains each line.
> The summary here assumes you already have the repo to copy from.

### 1. Prepare the Docker host

```bash
sudo apt update && sudo apt install -y docker.io docker-compose-v2

# Elasticsearch refuses to start without this. Make it permanent.
echo 'vm.max_map_count=262144' | sudo tee /etc/sysctl.d/99-es.conf
sudo sysctl --system
```

Firewall it to the two things that need it — the IIS server (ingest and nothing
else) and your admin subnet (the UIs):

```bash
sudo ufw allow from 10.20.30.50 to any port 4317,3100,9200 proto tcp   # IIS server
sudo ufw allow from 10.20.30.0/24 to any port 3000,5601,9090 proto tcp # admins
```

### 2. Copy the stack over

```bash
rsync -av observability/prod observability/grafana user@10.20.30.40:/opt/cleanarch/observability/
```

`prod/` mounts `../grafana/`, so both folders have to travel together.

### 3. Fill in `.env`

```bash
cd /opt/cleanarch/observability/prod
cp .env.example .env && chmod 600 .env
nano .env                       # every CHANGE-ME value; openssl rand -base64 24
```

`BIND_ADDR` is this host, `API_HOST_IP` is the IIS server. Both are required and
compose will refuse to start without the passwords.

### 4. Start

```bash
docker compose up -d
docker compose ps               # everything Up/healthy; es-setup shows Exited (0)
```

`es-setup` is a one-shot container that creates the `audit-writer` account and
sets Kibana's service password, then exits. Exit code 0 is success — it runs
again harmlessly on every `up`.

### 5. Point the app at the stack

On the IIS server, set these on the site (see
[`../docs/deploy-iis.md`](../docs/deploy-iis.md) for where they go and how to
keep the password out of source control). Substitute your `BIND_ADDR`:

```
ASPNETCORE_ENVIRONMENT             = Production
Observability__Tempo__OtlpEndpoint = http://10.20.30.40:4317
Observability__Loki__OtlpEndpoint  = http://10.20.30.40:3100/otlp/v1/logs
Audit__Elasticsearch__Uri          = http://10.20.30.40:9200
Audit__Elasticsearch__Username     = audit-writer          ← AUDIT_USER
Audit__Elasticsearch__Password     = ...                   ← AUDIT_PASSWORD
```

Metrics need no setting: Prometheus already knows where to scrape. What it does
need is an inbound Windows Firewall rule allowing this Docker host to reach the
site.

Recycle the app pool, then generate a little traffic.

### 6. Verify, do not assume

| Check | Where |
|---|---|
| Metrics arriving | Prometheus `/targets` → `cleanarch-api` is **UP** |
| Traces arriving | Grafana → Explore → Tempo → Search → last 15 minutes |
| Logs arriving | Grafana → Explore → Loki → `{service_name="CleanArch.Api"}` |
| Audit arriving | Kibana → Discover, data view `cleanarch-audit-*` (log in as `elastic`) |
| Dashboard | Grafana → "CleanArch.Api — Service Overview" |

If traces and logs are missing but metrics are fine, the app cannot reach the
host — check the Docker-host firewall and that `Observability__*` really took
effect. If metrics are missing but traces are fine, it is the other direction:
the Windows Firewall inbound rule, or the wrong port in `prometheus.yml`.

### 7. Keep it running

```bash
# nightly backups of the audit trail and Grafana (0 3 * * * in cron)
/opt/cleanarch/observability/prod/backup.sh

# updates: bump the pinned versions in .env, then
docker compose pull && docker compose up -d

# disk usage per volume
docker system df -v
```

Retention is 30 days in all four stores, set in `.env`
(`PROMETHEUS_RETENTION_*`), `tempo.yaml` and `loki.yaml`. It exists so the disk
cannot fill; check actual growth after the first week and adjust.

---

## Ports

| Port | Service | Dev | Prod — who connects |
|---|---|---|---|
| 4317 | Tempo, OTLP/gRPC ingest | localhost | the IIS server |
| 3200 | Tempo query API | localhost | Grafana (internal) |
| 3100 | Loki, ingest + queries | localhost | the IIS server, Grafana |
| 9090 | Prometheus UI | localhost | admins |
| 3000 | Grafana | localhost | admins |
| 9200 | Elasticsearch | localhost | the IIS server, Kibana |
| 5601 | Kibana | localhost | admins |
| 5235 | the API itself | localhost | Prometheus scrapes it |

## Troubleshooting

| Symptom | Cause |
|---|---|
| `elasticsearch` exits immediately | `vm.max_map_count` too low, or `ES_HEAP` larger than the host has |
| `es-setup` exits non-zero | wrong `ELASTIC_PASSWORD` for an existing `es-data` volume — it only bootstraps on a fresh one; use `elasticsearch-reset-password` |
| Kibana never becomes healthy | `KIBANA_SYSTEM_PASSWORD` changed without re-running `es-setup`, or `KIBANA_ENCRYPTION_KEY` shorter than 32 characters |
| Dashboard panels empty, Explore works | metric-name mismatch — `metric_name_escaping_scheme: underscores` must stay in `prometheus.yml` |
| Loki 400s on push | `allow_structured_metadata: true` is required for OTLP logs |
| `variable is not set` on `up` | a value is missing from `.env`; compose names it |
