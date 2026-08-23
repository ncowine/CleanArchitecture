# Deploy CleanArch.Api + observability on Docker (Ubuntu) — a beginner's guide

This folder runs **the CleanArch.Api app *and* its full observability stack** — Tempo, Loki,
Prometheus, and Grafana — with **one command**, on a plain Ubuntu machine.

**You do NOT need the project's source code on that machine.** The app ships as a prebuilt image on
GitHub Container Registry (GHCR); your Ubuntu box just *pulls* it. All you copy over is the handful of
small config files in this folder. This guide assumes zero prior Docker knowledge.

> This is the Linux/Ubuntu path. The native-Windows dev setup (downloaded `.exe`s) lives one folder up
> and is unaffected.

> ⚠️ **This stack is for development.** It publishes eight ports, runs Grafana as an anonymous admin,
> and starts the app in `Development` mode — which seeds the API keys published in this repo. It is
> safe on a laptop and unsafe on anything with a public IP. For a real deployment use
> **[README-production.md](README-production.md)** and `docker-compose.prod.yml`, which exist for
> exactly this reason.

---

## 1. What's happening (the 2-minute mental model)

**Docker** runs each program in its own isolated box (a **container**) built from a downloadable
template (an **image**). **Docker Compose** starts several containers together from one file
(`docker-compose.yml`) and puts them on a shared private network so they talk to each other by name.

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
        │           CleanArch.Api  (:5235)  — pulled from GHCR          │
        └───────────────────────────────────────────────────────────────┘
```

- **Metrics** = numbers over time (request counts, latency) → Prometheus.
- **Traces** = the timeline of one request through the code → Tempo.
- **Logs** = text events → Loki.
- **Grafana** is just the UI — it stores nothing, it only reads from those three.
- Inside the Docker network containers use **service names** (`tempo`, `loki`, `prometheus`), which is
  why the config files here say those instead of `localhost`.

The four stores (Tempo/Loki/Prometheus/Grafana) are **official public images** — Docker downloads them
automatically. Only the **app** image is yours, published from this repo to GHCR (see
[§7 How the app image gets published](#7-how-the-app-image-gets-published-one-time-setup)).

---

## 2. On your Ubuntu box: install Docker (one time)

```bash
sudo apt-get update
sudo apt-get install -y docker.io docker-compose-v2
sudo usermod -aG docker "$USER"     # run docker without sudo...
newgrp docker                        # ...apply that to the current shell
docker run hello-world               # sanity check — should print a welcome message
```

If `hello-world` works, you're ready. (If it says "permission denied", log out and back in.)

---

## 3. Get the deploy files onto the box (no full source needed)

The stack needs the small config files in this folder plus one dashboard JSON — about a dozen tiny
text files, **no application source**. Two ways to get them:

**Option A — sparse checkout (recommended): grab only the `observability/` folder.**

```bash
git clone --no-checkout --depth 1 https://github.com/ncowine/CleanArchitecture.git
cd CleanArchitecture
git sparse-checkout init --cone
git sparse-checkout set observability
git checkout
cd observability/docker
```

This downloads *only* the `observability/` directory (config files + the dashboard), not the app
source. That's the folder the deployment reads.

**Option B — copy the folder yourself.** From any machine that already has the repo, copy the whole
`observability/` directory to the box (e.g. `scp -r observability user@box:~/`). Keep its structure:
`docker-compose.yml` references `../grafana/dashboard-cleanarch-api.json`, so the sibling `grafana/`
folder must come along too.

Either way you end up in `observability/docker/` with these files present.

---

## 4. Let the box pull the private app image

The app image on GHCR is **private by default**, so the box needs permission to pull it. Pick one:

**Option A — make the image public (simplest for a reference project).**
On GitHub → your profile → **Packages** → `cleanarch-api` → **Package settings** → **Change
visibility** → **Public**. Now anyone (including your box) can pull it with no login. Done.

**Option B — log in on the box (keep it private).**
Create a GitHub **Personal Access Token** with the `read:packages` scope
(GitHub → Settings → Developer settings → Personal access tokens), then:

```bash
echo "YOUR_TOKEN" | docker login ghcr.io -u ncowine --password-stdin
```

> If the image doesn't exist in GHCR yet, do [§7](#7-how-the-app-image-gets-published-one-time-setup)
> first — it has to be published once before anything can pull it.

---

## 5. Start everything

```bash
# still in observability/docker/
docker compose pull        # download the app image (+ any store images) ahead of time
docker compose up -d       # create & start all five containers in the background
docker compose ps          # check they're 'running' / 'healthy'
```

- `pull` fetches images from registries. (Skip it and `up` will pull what's missing anyway.)
- `up -d` starts everything detached (in the background).

First start pulls a few hundred MB of images; later starts are seconds.

---

## 6. Open it

| What | URL | Notes |
|------|-----|-------|
| **Grafana** (dashboards) | http://localhost:3000 | Opens straight in — no login (dev mode). |
| The CleanArch.Api dashboard | Grafana → Dashboards → **CleanArch.Api — Service Overview** | Already imported for you. |
| **Swagger** (try the API) | http://localhost:5235/swagger | Fire a few requests here to generate telemetry. |
| Prometheus targets | http://localhost:9090/targets | `cleanarch-api` should say **UP**. |

> Browsing from your laptop to a *remote* Ubuntu server? Replace `localhost` with the server's IP/host
> (and make sure ports 3000/5235 are reachable), or use an SSH tunnel:
> `ssh -L 3000:localhost:3000 -L 5235:localhost:5235 user@your-server`.

**Generate data:** open Swagger and call a few endpoints (create a student, list books). Panels stay
empty until the app has served some traffic; rate/latency panels need a little *sustained* traffic.

Then in Grafana → **Explore** (compass icon):
- **Tempo** → Search → recent requests; click one for its timeline, then "Logs for this trace".
- **Loki** → query `{service_name="CleanArch.Api"}` for logs.
- **Prometheus** → the dashboard panels, or ad-hoc metric queries.

---

## 7. How the app image gets published (one-time setup)

The image the box pulls is built and pushed automatically by GitHub Actions —
[`.github/workflows/publish-api-image.yml`](../../.github/workflows/publish-api-image.yml). You never
run Docker by hand for this.

- **It runs** on every push to `main`, on any `v*` git tag, and on a manual click (Actions tab →
  *Publish API image* → **Run workflow**).
- **It pushes** `ghcr.io/ncowine/cleanarch-api:latest` (plus a `sha-…` tag, and a version tag if you
  tagged a release). No secrets to configure — it uses the built-in `GITHUB_TOKEN`.

So the end-to-end flow for a reference deploy is:

```
push repo to GitHub  →  Actions builds & publishes the image to GHCR  →  Ubuntu box pulls & runs it
```

**Cut a versioned image** (recommended for a stable reference deploy) by tagging a release:

```bash
git tag v1.0.0 && git push origin v1.0.0
```

Then pin the box to it by editing `docker-compose.yml`:
`image: ghcr.io/ncowine/cleanarch-api:v1.0.0` (instead of `:latest`).

---

## 8. Everyday commands

```bash
docker compose ps                 # what's running + health
docker compose logs -f api        # follow the app's logs live (Ctrl-C to stop watching)
docker compose logs -f            # follow ALL services' logs
docker compose pull && docker compose up -d   # get the newest published image and restart
docker compose restart api        # restart just the app
docker compose down               # stop & remove containers (KEEPS your data volumes)
docker compose down -v            # stop & ALSO wipe data (fresh databases + dashboards next time)
```

---

## 9. Where your data lives

Containers are disposable; anything worth keeping is in Docker **named volumes** that outlive them:

| Volume | Holds |
|--------|-------|
| `api-data` | the four SQLite databases (`students.db`, `library.db`, `testplans.db`, `testerguide.db`) |
| `tempo-data` | stored traces |
| `loki-data` | stored logs |
| `prometheus-data` | stored metrics |
| `grafana-data` | Grafana's own state |

`docker compose down` keeps them; `docker compose down -v` deletes them (clean slate). List them with
`docker volume ls`.

---

## 10. How the app is configured (what each setting does)

The app is configured entirely by environment variables in
[`docker-compose.yml`](docker-compose.yml) — no files to edit inside the image:

| Setting | Why it's there |
|---------|----------------|
| `ASPNETCORE_ENVIRONMENT=Development` | Turns on the dev conveniences: auto-creates the SQLite databases (applies EF Core migrations), seeds sample data, and serves Swagger. Without it, the app expects the databases to already exist. |
| `ASPNETCORE_URLS=http://+:5235` | The app listens on 5235 inside the container; `ports: "5235:5235"` publishes it to the host. |
| `ConnectionStrings__*=/app/data/…` | Points the four databases at the `api-data` volume so they persist. |
| `Observability__Tempo__OtlpEndpoint=http://tempo:4317` | Where the app pushes **traces** (by service name). |
| `Observability__Loki__OtlpEndpoint=http://loki:3100/otlp/v1/logs` | Where the app pushes **logs**. |
| `Audit__Elasticsearch__Uri=""` (empty) | The separate ELK/Kibana stack isn't part of this compose, so audit-to-Elasticsearch is off; audit records still appear as normal logs in Loki. |

> **Double-underscore = nested config.** .NET reads `Observability__Tempo__OtlpEndpoint` as the JSON
> path `Observability:Tempo:OtlpEndpoint`. That's how these env vars override the app's built-in
> defaults without touching any file.

---

## 11. Other layouts

**Build the app from source instead of pulling** (only if you *do* have the full repo checked out):

```bash
docker compose -f docker-compose.yml -f docker-compose.build.yml up -d --build
```

The override file adds a `build:` step that compiles the image locally from the repo's `Dockerfile`.

**Run only the stores here, the app on your host** (e.g. while developing with `dotnet run`):

1. Start just the stores: `docker compose up -d tempo loki prometheus grafana`
2. In [`prometheus.yml`](prometheus.yml) swap the scrape target from `api:5235` to
   `host.docker.internal:5235` (a commented line is ready), then
   `curl -X POST http://localhost:9090/-/reload`.
3. Run the app on the host — its defaults already point at `localhost:4317` / `localhost:3100`, which
   this stack publishes, so no app config change is needed:
   `dotnet run --project src/Api/CleanArch.Api`.

---

## 12. Optional add-on: audit trail in Kibana (Elasticsearch)

The app writes an **audit record** for every audited command (who did what, and the before/after of
each changed field). By default those records go to the logs — so on the base stack you can already
read them in **Grafana → Explore → Loki**. If you'd rather search them in **Kibana**, there's an
optional Elasticsearch + Kibana add-on (off by default — Elasticsearch is heavy). Enable it with:

```bash
docker compose -f docker-compose.yml -f docker-compose.elk.yml up -d
```

📖 **Full setup guide (host setting, Kibana data view, turn on/off, troubleshooting):**
**[`README-elk.md`](README-elk.md)** — a self-contained walkthrough for ELK on Docker.

---

## 13. Ports & health checks

| Service | Port(s) | Ready check |
|---------|---------|-------------|
| Grafana | 3000 | http://localhost:3000 |
| CleanArch.Api | 5235 | http://localhost:5235/health |
| Tempo | 3200 (query), 4317 (OTLP/gRPC), 4318 (OTLP/HTTP) | http://localhost:3200/ready |
| Loki | 3100 | http://localhost:3100/ready |
| Prometheus | 9090 | http://localhost:9090/-/ready |
| Elasticsearch *(ELK add-on)* | 9200 | http://localhost:9200/_cluster/health |
| Kibana *(ELK add-on)* | 5601 | http://localhost:5601/api/status |

---

## 14. Troubleshooting

- **`docker compose pull` says "denied" / "manifest unknown" for the api image.** Either the image
  hasn't been published yet (do [§7](#7-how-the-app-image-gets-published-one-time-setup)) or the box
  can't access a private package (do [§4](#4-let-the-box-pull-the-private-app-image)).
- **`api` keeps restarting / unhealthy.** Read `docker compose logs api`. First boot needs ~30–40s to
  run migrations + seeding before `/health` passes — give it a moment.
- **`cleanarch-api` is DOWN in Prometheus `/targets`.** With the app in compose the scrape target must
  be `api:5235` (the default here); if you moved the app to the host it must be
  `host.docker.internal:5235`. Then reload Prometheus (§11).
- **Grafana panels are empty.** The app hasn't served enough traffic. Hit some Swagger endpoints and
  wait ~30s; rate/latency panels need a couple of scrapes of *sustained* traffic.
- **"port is already allocated".** Something else uses that port (often 3000 or 5235). Change the
  left-hand number in the relevant `ports:` mapping (e.g. `"3001:3000"`) and use the new port.
- **`elasticsearch` (ELK add-on) crash-loops with `vm.max_map_count [65530] is too low`.** Run the
  one-time host setting in [§12](#12-optional-add-on-audit-trail-in-kibana-elasticsearch), then
  `docker compose -f docker-compose.yml -f docker-compose.elk.yml up -d` again.
- **Audit records aren't in Kibana.** Confirm you started with the `-f docker-compose.elk.yml` override
  (otherwise records go to Loki, not Elasticsearch), that you created the `cleanarch-audit-*` data
  view, and that you actually called an audited endpoint. Cross-check with
  `curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"`.
- **Rebuild from scratch.** `docker compose down -v && docker compose pull && docker compose up -d`.

---

## 15. A note on security (before you expose it)

This setup is tuned for easy local/reference use, **not** for a public network. The four things that
matter most:

- **The app runs in `Development` mode**, which seeds the API keys `dev-api-key-reporting` and
  `dev-api-key-integration`. Those strings are printed in this repository. Anyone who reaches the app
  has full write access with a credential they can read on GitHub.
- **Grafana has anonymous *admin* access** — not viewer, admin.
- **Everything publishes a port**, and this is the part that surprises people: Docker inserts its own
  `iptables` rules ahead of UFW's, so a published port is reachable from the internet **even when
  `ufw status` says it is denied**. "I have a firewall" does not cover this.
- **Nothing has a retention limit.** Loki, Tempo, Prometheus and Elasticsearch grow until the disk is
  full. This is the one that is most likely to actually happen to you, usually a month or two in.

The ELK add-on additionally runs Elasticsearch with security disabled, so the audit trail can be read,
forged or deleted by anyone who can reach port 9200.

Keep this stack on a laptop, or bound to `localhost` behind an SSH tunnel.

**Do not harden this file — use the one that is already hardened.**
[README-production.md](README-production.md) explains each difference and walks through a first
deploy; `docker-compose.prod.yml` and `docker-compose.prod.elk.yml` are the stacks it describes.

---

## 16. File map

```
observability/docker/
  docker-compose.yml          defines all five containers (api pulls from GHCR)
  docker-compose.build.yml    OPTIONAL override to build the app from source instead of pulling
  docker-compose.elk.yml      OPTIONAL add-on: Elasticsearch + Kibana for the audit trail (§12)
  README-elk.md               dedicated setup guide for the ELK add-on
  tempo.yaml                  Tempo config (storage under /var/tempo in the container)
  loki-config.yaml            Loki config (storage under /loki)
  prometheus.yml              Prometheus config (scrapes api:5235)
  grafana/provisioning/
    datasources/datasources.yaml   auto-adds the 3 data sources (by service name)
    dashboards/dashboards.yaml      auto-imports the dashboard JSON below
  README.md                   this file

../grafana/dashboard-cleanarch-api.json     the dashboard (mounted into Grafana)
../../Dockerfile                            builds the app image (used by CI and the build override)
../../.github/workflows/publish-api-image.yml   publishes the image to GHCR
```
