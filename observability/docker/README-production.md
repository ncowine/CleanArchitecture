# Running this stack in production

The files in this folder come in two flavours. `docker-compose.yml` and
`docker-compose.elk.yml` are **development** stacks — they trade safety for
convenience on purpose. `docker-compose.prod.yml` and
`docker-compose.prod.elk.yml` are the production versions.

## The deployment this is built for

```
Windows Server (IIS)                      Ubuntu VM (this folder)
─────────────────────                     ───────────────────────
CleanArch.Api  ──── traces  (OTLP/gRPC) ──▶ otlp.<domain>   ─▶ tempo
               ──── logs    (OTLP/HTTP) ──▶ otlp.<domain>   ─▶ loki
               ──── audit   (HTTPS)     ──▶ audit.<domain>  ─▶ elasticsearch
               ◀─── metrics (scrape)    ─── prometheus dials OUT to IIS

your browser   ──── dashboards          ──▶ grafana.<domain>, kibana.<domain>
                                            (IP-restricted)
```

Two machines, connected **over the public internet**. The app is not in the
compose stack at all.

- **This guide** covers the Ubuntu VM.
- **[README-windows-iis.md](README-windows-iis.md)** covers the app on IIS. You
  need both; roughly half the security-relevant settings live over there.

> **Status:** these files have been checked for syntax and internal consistency,
> but have not been run end-to-end — there was no Docker or Windows Server
> available where they were written. Rehearse on throwaway machines first. The
> verification section tells you what to confirm with your own eyes.

---

## 1. What the split changes, and why it matters most

If you take one thing from this document, take this.

In a single-host deployment, the app talks to Tempo, Loki and Elasticsearch over
a private Docker bridge. That traffic is unreachable from anywhere else, so the
fact that **none of those three services authenticates anything** simply does not
matter. Their security model is "you cannot reach them".

Splitting the two machines across the internet removes that protection entirely,
and the danger is that nothing appears to break. Point the app at the VM's public
IP with the dev configuration and it works perfectly — while:

- **Tempo (4317) and Loki (3100) accept writes from anyone on the internet.**
  Neither has any authentication available. Loki's `auth_enabled` looks like it
  should help but does not: it means multi-tenancy, and it validates no
  credential of any kind. Anyone who finds the port can read your logs, or inject
  fabricated ones.
- **Your Elasticsearch API key crosses the public internet in cleartext** on
  every audit write, because the dev config uses plain `http`. That key can write
  to your audit trail.
- **`/metrics` is readable by the world**, enumerating every route, request rate
  and error count in the service.

So the production design does not publish those ports at all. Everything goes
through Caddy, which provides the three things the stores cannot provide
themselves: **TLS**, **a credential**, and **an IP allowlist**.

### The other dev-stack problems, still true

**`ASPNETCORE_ENVIRONMENT: Development` is a backdoor.** In Development the app
seeds `dev-api-key-reporting` and `dev-api-key-integration` — strings printed in
this repository's public README. Covered in
[README-windows-iis.md](README-windows-iis.md), because that setting now lives on
the Windows box.

**Grafana ran as an anonymous admin** (`GF_AUTH_ANONYMOUS_ORG_ROLE: Admin` with
the login form disabled). Whoever reached port 3000 *was* an administrator.

**The audit trail was writable by anyone.** `xpack.security.enabled: "false"` means
unauthenticated read, write **and delete** on your audit records. An audit log an
attacker can edit is worse than no audit log, because it is trusted.

**Nothing had a retention limit.** Not a security problem, and the one most likely
to actually happen to you: disk fills, stores stop writing, telemetry stops. It
usually arrives four to eight weeks after go-live, at night.

**And the smaller things:** `:latest` tags, containers running as root, no
resource limits, Prometheus's unauthenticated `POST /-/reload` reconfigure
endpoint, Tempo's unused OTLP/HTTP listener, no backups.

---

## 2. How the Ubuntu VM is arranged

```
                    ┌─────────── edge (has internet) ───────────┐
   internet ─443──▶ │ caddy                        prometheus   │──▶ scrapes IIS
                    └───┬────────────────────────────────┬──────┘
                    ┌───┴──── backend (internal: true) ───┴──────┐
                    │ tempo   loki   grafana   elasticsearch     │
                    │                          kibana           │
                    └───────────────────────────────────────────┘
```

**Only Caddy publishes ports** — 80 and 443. Search `docker-compose.prod.yml` for
`ports:` and you will find exactly one occurrence. That is why the production
stack is a standalone file rather than an overlay on the dev one: Compose merges
override files by *appending* list entries, so a `ports:` mapping in a base file
cannot be removed by an override. The most safety-critical property of the stack
should not depend on a merge rule.

This matters more than it sounds, because of a Docker behaviour that catches
almost everyone:

> **Docker's port publishing bypasses UFW.** Docker inserts its own `iptables`
> rules ahead of UFW's, so a published port is reachable from the internet even
> when `ufw status` says it is denied. "I have a firewall" does not cover this.

`backend` is declared `internal: true`, which removes its gateway — containers
only on that network cannot reach the internet at all, so a compromised store has
nowhere to send data.

**Prometheus is the exception,** and the reason is worth understanding: it *pulls*
metrics, and its target is on another machine, so it needs outbound internet and
cannot live on the isolated network. That is the practical cost of the pull model
in a split deployment. It still publishes no ports of its own.

**Grafana queries the stores locally.** Only *ingest* crosses the internet, never
queries — which is why the datasource provisioning is reused unchanged from the
dev stack.

---

## 3. First deploy

### Before you start

- Docker Engine and the Compose plugin on the Ubuntu VM.
- **Four DNS records pointing at this VM**: `otlp.`, `audit.`, `grafana.`,
  `kibana.`. Caddy proves domain ownership over port 80 to get certificates, and
  that fails if DNS does not resolve here yet.
- Ports 80 and 443 open to the world on this VM. Nothing else.
- The Windows server's **public IP**, and the IP range you browse from.

### Step 1 — copy the folder

```bash
sudo mkdir -p /opt/cleanarch && sudo chown "$USER" /opt/cleanarch
# from your machine:
rsync -av observability/docker/ user@vm:/opt/cleanarch/observability/docker/
rsync -av observability/grafana/ user@vm:/opt/cleanarch/observability/grafana/
```

The `grafana/` folder one level up is needed too — the compose file mounts the
dashboard JSON from there.

### Step 2 — fill in `.env`

```bash
cd /opt/cleanarch/observability/docker
cp .env.example .env
chmod 600 .env
nano .env
```

Generate secrets with `openssl rand -base64 32`. Two entries need explanation:

**`INGEST_PASSWORD_HASH`** is a bcrypt hash, not a password:

```bash
docker run --rm caddy:2.9-alpine caddy hash-password --plaintext 'the-password-you-chose'
```

Keep the plaintext — the Windows side needs it, base64-encoded, as a Basic auth
header.

**`APP_SERVER_IPS`** is the Windows server's public IP. If that address is
dynamic, ingest will break silently the next time it changes: pushes start
getting 403s and telemetry just stops. Use a static address.

### Step 3 — point Prometheus at the Windows server

Prometheus does **not** substitute environment variables in its config, so this
one is edited by hand:

```bash
nano prometheus.prod.yml
# replace REPLACE_WITH_WINDOWS_PUBLIC_HOSTNAME with the real hostname
```

### Step 4 — start

```bash
docker compose -f docker-compose.prod.yml up -d
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs -f caddy
```

Watch for certificate issuance on all four hostnames. If it fails, the cause is
almost always DNS not yet resolving here, or port 80 blocked.

The IP allowlists do not interfere with this: the Caddyfile explicitly exempts
`/.well-known/acme-challenge/*` from both gates, because Let's Encrypt validates
from its own servers, which are on nobody's allowlist. Without that exemption,
issuance and every later renewal would 403.

### Step 5 — the audit trail (optional)

Elasticsearch needs a kernel setting first:

```bash
echo 'vm.max_map_count=262144' | sudo tee /etc/sysctl.d/99-es.conf
sudo sysctl --system
```

Then start it alone and bootstrap. This step exists because enabling security
leaves `kibana_system` without a usable password and the app without a
credential, and both are API calls against a running cluster:

```bash
docker compose -f docker-compose.prod.yml -f docker-compose.prod.elk.yml up -d elasticsearch
chmod +x scripts/*.sh
./scripts/bootstrap-elk.sh
```

It prints **two secrets that go to different machines** — `KIBANA_SYSTEM_PASSWORD`
into `.env` here, and `Audit__Elasticsearch__ApiKey` onto the Windows server. Do
not put both in the same place.

```bash
docker compose -f docker-compose.prod.yml -f docker-compose.prod.elk.yml up -d
```

The API key it mints is deliberately scoped to `create_doc` on
`cleanarch-audit-*`: the app can **append** audit records but not overwrite or
delete them. If the app is compromised, the attacker can add records, not quietly
rewrite history.

### Step 6 — configure the Windows server

Now do [README-windows-iis.md](README-windows-iis.md). Nothing will arrive in
Grafana until that side is pointed here with the right credentials.

### Step 7 — backups

```bash
sudo mkdir -p /var/backups/cleanarch
sudo crontab -e
# 0 3 * * * /opt/cleanarch/observability/docker/scripts/backup.sh >> /var/log/cleanarch-backup.log 2>&1
```

This backs up the audit trail, Grafana's state and the TLS certificates. It does
**not** back up the application's databases — those are on the Windows server and
need their own job there.

Then restore one into a throwaway VM. A backup you have never restored is a
hypothesis.

---

## 4. Verify it, do not assume it

Run these from a machine that is **neither server**.

**Only 80 and 443 answer on the VM.** The check that matters most:

```bash
nmap -Pn -p- your.vm.ip
```

If 3000, 3100, 3200, 4317, 9090, 9200 or 5601 respond, something is still
publishing a port. Do not proceed until this is clean.

**Ingest rejects strangers.** From an address that is not the Windows server:

```bash
curl -sS -o /dev/null -w '%{http_code}\n' https://otlp.example.com/otlp/v1/logs   # 403
curl -sS -o /dev/null -w '%{http_code}\n' https://audit.example.com               # 403
```

**Ingest rejects bad credentials.** If you can test from the Windows server's
address, a request with no `Authorization` header should give 401 rather than
succeeding.

**Dashboards reject strangers.** From outside `UI_ALLOWED_IPS`,
`https://grafana.example.com` must be 403 — not a Grafana login page.

**Grafana demands a login** from *inside* the allowed range. The IP allowlist is
a perimeter; Grafana's own login is what protects you from anyone already inside
it.

**Elasticsearch rejects anonymous access.** On the VM:

```bash
docker compose -f docker-compose.prod.yml -f docker-compose.prod.elk.yml \
  exec elasticsearch curl -s -o /dev/null -w '%{http_code}\n' http://localhost:9200/_cluster/health
```

Must be 401.

**Telemetry actually arrives.** Make some requests against the API, then check
Grafana within a minute for traces, logs and a moving metrics line. Partial
arrival is diagnostic: traces and logs but no metrics means the scrape direction
is broken; metrics but no traces or logs means the ingest credential or the
Windows outbound rule is.

```bash
# 401 = wrong ingest credential. 403 = APP_SERVER_IPS does not match where the
# app is really connecting from (NAT?).
docker compose -f docker-compose.prod.yml logs caddy | grep -E '401|403'
```

---

## 5. Honest limitations

**Telemetry ingest is publicly reachable.** It has to be — the app dials in from
the internet. TLS, a credential and an IP allowlist are real controls, but this
is a genuinely exposed surface that would not exist if both machines shared a
private network. **If you can put these two servers on a VPN or private link
later, do it** — it is the single biggest improvement available to this design,
and it would let you drop the public ingest hostnames entirely.

**The IP allowlist is brittle.** A changed public IP on either end breaks things
silently: telemetry stops, or you lose dashboard access. Static addresses, or
a plan for updating them.

**SQLite on one host, with no replication or point-in-time recovery.** The backup
job on the Windows server is the entire recovery story. Moving to PostgreSQL is a
connection-string change plus new migrations.

**Migrations run at app startup.** Correct for one instance; a race with two.

**Secrets are environment variables**, visible in `docker inspect`. Acceptable
where root already owns the box; the upgrade path is Docker secrets or a secret
manager, changing the compose files only.

**One VM means planned downtime.** Every deploy is a short outage; hardware
failure is an outage until you rebuild.

**No alerting.** You now collect metrics, logs and traces, and nothing tells you
when something is wrong. Disk usage on the volumes is the first Grafana alert
rule worth writing, given the retention discussion above. The second is "the
`cleanarch-api` Prometheus target is down", which is also your early warning that
the scrape path has broken.

---

## 6. Keeping it running

**Updating the stores** — image tags are pinned minor versions. Read the release
notes, bump one at a time, check the dashboard still renders. Elasticsearch and
Kibana must move together.

**Applying a Prometheus config change** — restart the container. The production
stack omits `--web.enable-lifecycle` because it is an unauthenticated reconfigure
endpoint.

```bash
docker compose -f docker-compose.prod.yml restart prometheus
```

**Watching disk** — the thing most likely to break this:

```bash
docker system df -v | grep -E 'loki-data|tempo-data|prometheus-data|es-data'
df -h
```

If a volume grows faster than expected, lower the retention values rather than
adding disk.

**Rotating the ingest credential** — change it on the Ubuntu VM first (new hash
in `.env`, restart Caddy), and the app will get 401s until you update the Windows
side. Doing it the other way round gives the same outage in the other order;
there is no zero-downtime path without adding a second credential temporarily.

---

## Files

```
observability/docker/
  docker-compose.prod.yml         Ubuntu VM stack (standalone, not an overlay)
  docker-compose.prod.elk.yml     production ELK add-on, security enabled
  prometheus.prod.yml             scrapes the Windows server (edit by hand)
  loki-config.prod.yaml           logs, with retention
  tempo.prod.yaml                 traces, with retention
  caddy/Caddyfile                 the one internet-facing service
  caddy/kibana.caddyfile          Kibana site, added only by the ELK add-on
  .env.example                    template for the VM's secrets
  scripts/bootstrap-elk.sh        one-time ELK setup (passwords, API key, retention)
  scripts/backup.sh               nightly volume backup (VM only)

  README-production.md            this file — the Ubuntu VM
  README-windows-iis.md           the app on IIS — read both

  docker-compose.yml              DEV stack — see README.md
  docker-compose.elk.yml          DEV ELK add-on — see README-elk.md
```
