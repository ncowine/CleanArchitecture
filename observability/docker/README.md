# Run the whole thing on Docker (Ubuntu) — a beginner's guide

This folder lets you start **the CleanArch.Api app *and* its full observability stack** — Tempo,
Loki, Prometheus, and Grafana — with **one command**. It's the Linux/Ubuntu equivalent of the
native-Windows setup one folder up (`../start-all.ps1` + a hand-installed Grafana). Nothing here
touches that Windows workflow; the `.exe`s still work as before.

If you've never used Docker: that's fine. This guide assumes zero prior knowledge.

---

## 1. What is actually happening (the 2-minute mental model)

**Docker** runs each program in its own isolated box called a **container**, built from a downloadable
**image** (a prebuilt template). **Docker Compose** is a tool that starts *several* containers together
from one file, `docker-compose.yml`, and puts them on a shared private network so they can talk to
each other by name.

We run **five** containers:

```
        ┌─────────────────────── Grafana (:3000) ───────────────────────┐
        │   the UI you open in a browser — reads from the three stores   │
        └──────▲─────────────────────▲──────────────────────▲───────────┘
               │ metrics             │ traces               │ logs
        ┌──────┴──────┐       ┌───────┴──────┐       ┌───────┴──────┐
        │ Prometheus  │       │    Tempo     │       │     Loki     │
        │   :9090     │       │    :3200     │       │    :3100     │
        └──────▲──────┘       └───────▲──────┘       └───────▲──────┘
   scrapes /metrics│          pushes traces│         pushes logs│
        ┌──────────┴──────────────────┴────────────────────────┴───────┐
        │                    CleanArch.Api  (:5235)                     │
        │           built from the repo's Dockerfile, runs here         │
        └───────────────────────────────────────────────────────────────┘
```

- **The app produces three kinds of telemetry.** *Metrics* = numbers over time (request counts,
  latency). *Traces* = the timeline of one request as it moves through the code. *Logs* = text events.
- **Each kind goes to a store built for it**: Prometheus (metrics), Tempo (traces), Loki (logs).
- **Grafana** is just the dashboard/UI — it stores nothing, it only *reads* from those three.
- Inside the Docker network, containers reach each other by **service name**, e.g. the app pushes
  traces to `http://tempo:4317`. That's why the config files here say `tempo`/`loki`/`prometheus`
  instead of `localhost`.

---

## 2. Install Docker on Ubuntu (one time)

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker "$USER"     # lets you run docker without sudo...
newgrp docker                        # ...applies the group change to your current shell
docker run hello-world               # sanity check — should print a welcome message
```

If `docker run hello-world` works, you're ready. (If it says "permission denied", log out and back
in so the group change takes effect.)

---

## 3. Start everything

```bash
cd observability/docker
docker compose up -d --build
```

What those words mean:
- `up` = create and start the containers.
- `-d` = "detached" — run in the background and give you your terminal back.
- `--build` = build the app image from the `Dockerfile` first. Needed the **first** time and after
  any code change. (Leave it off to start faster when nothing changed.)

The **first** build takes a few minutes (it downloads the .NET SDK image and compiles the app).
Later starts are seconds.

Check they're all healthy:

```bash
docker compose ps
```

You want to see `running`/`healthy` next to each service.

---

## 4. Open it

| What | URL | Notes |
|------|-----|-------|
| **Grafana** (dashboards) | http://localhost:3000 | Opens straight in — no login (dev mode). |
| The CleanArch.Api dashboard | Grafana → Dashboards → **CleanArch.Api — Service Overview** | Already imported for you. |
| **Swagger** (try the API) | http://localhost:5235/swagger | Fire a few requests here to generate telemetry. |
| Prometheus targets | http://localhost:9090/targets | `cleanarch-api` should say **UP**. |

**Generate some data:** open Swagger and call a few endpoints (e.g. create a student, list books).
Metrics and traces only appear once the app has actually served traffic — an idle app shows empty
panels. Rate/latency panels need a little *sustained* traffic before they fill in.

Then in Grafana:
- **Traces**: Explore (compass icon) → pick **Tempo** → Search → your recent requests appear. Click
  one to see its timeline, then "Logs for this trace" to jump to its log lines.
- **Logs**: Explore → **Loki** → query `{service_name="CleanArch.Api"}`.
- **Metrics**: the dashboard panels, or Explore → **Prometheus**.

---

## 5. Everyday commands

```bash
docker compose ps                 # what's running + health
docker compose logs -f api        # follow the app's logs live (Ctrl-C to stop watching)
docker compose logs -f            # follow ALL services' logs
docker compose up -d --build      # rebuild + restart after a code change
docker compose restart api        # restart just the app
docker compose down               # stop & remove containers (KEEPS your data volumes)
docker compose down -v            # stop & ALSO wipe data (fresh databases + dashboards next time)
```

---

## 6. Where your data lives

Containers are disposable, so anything worth keeping is stored in Docker **named volumes** that
outlive them:

| Volume | Holds |
|--------|-------|
| `api-data` | the four SQLite databases (`students.db`, `library.db`, `testplans.db`, `testerguide.db`) |
| `tempo-data` | stored traces |
| `loki-data` | stored logs |
| `prometheus-data` | stored metrics |
| `grafana-data` | Grafana's own state |

`docker compose down` keeps them; `docker compose down -v` deletes them (use it when you want a clean
slate). List them anytime with `docker volume ls`.

---

## 7. How the app is wired (what each setting does)

The app image is built from [`../../Dockerfile`](../../Dockerfile) and configured entirely by
environment variables in [`docker-compose.yml`](docker-compose.yml). The important ones:

| Setting | Why it's there |
|---------|----------------|
| `ASPNETCORE_ENVIRONMENT=Development` | Turns on the dev conveniences: auto-creates the SQLite databases (applies EF Core migrations), seeds sample data, and serves Swagger. Without this the app expects the databases to already exist. |
| `ASPNETCORE_URLS=http://+:5235` | The app listens on port 5235 inside the container; `ports: "5235:5235"` publishes it to your machine. |
| `ConnectionStrings__*=/app/data/…` | Points the four databases at the `api-data` volume so they persist. |
| `Observability__Tempo__OtlpEndpoint=http://tempo:4317` | Where the app pushes **traces** (by service name). |
| `Observability__Loki__OtlpEndpoint=http://loki:3100/otlp/v1/logs` | Where the app pushes **logs**. |
| `Audit__Elasticsearch__Uri=""` (empty) | The separate ELK/Kibana stack isn't part of this compose, so audit-to-Elasticsearch is switched off; audit records still come out as normal logs (visible in Loki). |

> **Double-underscore = nested config.** .NET reads `Observability__Tempo__OtlpEndpoint` as the JSON
> path `Observability:Tempo:OtlpEndpoint` in `appsettings.json`. That's how the env vars here override
> the app's defaults without editing any file.

---

## 8. Alternative: run the API on the host, stores in Docker

Maybe you're actively editing the app with `dotnet run` and only want the *stores* in Docker. That
works too:

1. Don't start the app container: `docker compose up -d tempo loki prometheus grafana`
2. In [`prometheus.yml`](prometheus.yml), swap the scrape target from `api:5235` to
   `host.docker.internal:5235` (there's a commented line ready), then
   `curl -X POST http://localhost:9090/-/reload`.
3. Run the app on the host — its defaults already point at `localhost:4317` / `localhost:3100`, which
   the compose stack publishes, so **no app config change is needed**:
   ```bash
   dotnet run --project src/Api/CleanArch.Api
   ```

`host.docker.internal` is how a container reaches a program running on your host machine; the
`extra_hosts` line on the Prometheus service makes that name resolve on native Linux (it's automatic
on Docker Desktop).

---

## 9. Ports & health checks

| Service | Port(s) | Ready check |
|---------|---------|-------------|
| Grafana | 3000 | http://localhost:3000 |
| CleanArch.Api | 5235 | http://localhost:5235/health |
| Tempo | 3200 (query), 4317 (OTLP/gRPC), 4318 (OTLP/HTTP) | http://localhost:3200/ready |
| Loki | 3100 | http://localhost:3100/ready |
| Prometheus | 9090 | http://localhost:9090/-/ready |

---

## 10. Troubleshooting

- **`docker compose ps` shows `api` restarting / unhealthy.** Read its logs: `docker compose logs api`.
  First boot needs ~30–40s to run migrations and seeding before `/health` passes — give it a moment.
- **`cleanarch-api` is DOWN in Prometheus `/targets`.** The app isn't reachable at the scrape target.
  If the app runs in compose, the target must be `api:5235` (the default here); if on the host, it must
  be `host.docker.internal:5235`. Then reload Prometheus (step 8).
- **Grafana panels are empty.** The app hasn't served enough traffic yet. Hit some Swagger endpoints
  and wait ~30s; rate/latency panels need a couple of scrapes of *sustained* traffic.
- **"port is already allocated".** Something else on your machine already uses that port (often 3000 or
  5235). Stop the other program, or change the left-hand number in the relevant `ports:` mapping (e.g.
  `"3001:3000"`) and use that new port in your browser.
- **Rebuild from scratch.** `docker compose down -v && docker compose up -d --build`.

---

## 11. A note on security (before you show anyone)

This setup is tuned for easy local dev, **not** for exposing on a network:
- **Grafana** has anonymous admin access (no login). Remove the `GF_AUTH_ANONYMOUS_*` /
  `GF_AUTH_DISABLE_LOGIN_FORM` env vars to require a login.
- **The app** runs in `Development` mode (verbose errors, Swagger, auto-migrate). A real deployment
  would run `Production` and apply migrations as a separate step.

Keep it bound to `localhost` (as it is) until you've hardened those.

---

## 12. File map

```
observability/docker/
  docker-compose.yml      the one file that defines all five containers
  tempo.yaml              Tempo config (storage under /var/tempo in the container)
  loki-config.yaml        Loki config (storage under /loki)
  prometheus.yml          Prometheus config (scrapes api:5235)
  grafana/provisioning/
    datasources/datasources.yaml   auto-adds the 3 data sources (by service name)
    dashboards/dashboards.yaml      auto-imports the dashboard JSON below
  README.md               this file

../grafana/dashboard-cleanarch-api.json   the dashboard (reused, mounted into Grafana)
../../Dockerfile                          builds the CleanArch.Api image
../../.dockerignore                       keeps the build context small
```
