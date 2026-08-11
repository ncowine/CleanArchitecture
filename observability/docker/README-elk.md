# Set up ELK (Elasticsearch + Kibana) on Docker — audit trail viewer

This is the dedicated guide for running the **audit-trail** stack — **Elasticsearch** (stores audit
records) and **Kibana** (the UI you search them in) — as Docker containers on Ubuntu.

It's an **optional add-on** to the main Docker stack. The base stack
([`README.md`](README.md)) already runs the app + Tempo/Loki/Prometheus/Grafana; this layers ELK on
top. It is **off by default on purpose** — Elasticsearch is heavy (budget ~1 GB+ RAM). When it's off,
nothing breaks: the app keeps writing audit records to the normal logs, which you can read in
**Grafana → Explore → Loki**.

> **Prerequisite:** the base stack from [`README.md`](README.md) should already be working (Docker
> installed, these config files on the box). This guide only adds ELK on top.

---

## What "the audit trail" is

The app writes an **audit record** for every audited command: **who** did it (`actor`), **what** they
did (`action`), whether it `succeeded`, and the **before/after** of every changed field (`changes`).
Turning on this add-on ships those records to Elasticsearch so you can search them in Kibana. Behind
the scenes the app's audit shipper is a background worker that safely falls back to logging if
Elasticsearch is unreachable — so this add-on is always safe to turn on or off.

---

## 1. One-time Ubuntu host setting (REQUIRED)

Elasticsearch refuses to start until the Linux kernel allows enough memory-map areas. Run this once on
the box (it's a kernel setting Docker can't apply for you):

```bash
sudo sysctl -w vm.max_map_count=262144                                # applies right now
echo 'vm.max_map_count=262144' | sudo tee /etc/sysctl.d/99-es.conf    # makes it survive reboot
```

Skip this and the `elasticsearch` container will crash-loop with:
`max virtual memory areas vm.max_map_count [65530] is too low`.

---

## 2. Turn it on

From the `observability/docker/` folder, layer the ELK override file on top of the base compose file —
the two `-f` flags combine both:

```bash
docker compose -f docker-compose.yml -f docker-compose.elk.yml up -d
```

That does two things:
- **adds two containers**: `elasticsearch` (:9200) and `kibana` (:5601);
- **re-points the app's audit target** at `http://elasticsearch:9200`, so new audit records go to
  Elasticsearch instead of only the logs.

Give them a minute to boot (Elasticsearch is slow to start, Kibana slower). Check status:

```bash
docker compose -f docker-compose.yml -f docker-compose.elk.yml ps    # wait for elasticsearch + kibana = healthy
```

> **Tip — stop typing both `-f` flags every time.** Set them once per shell and every `docker compose`
> command in that shell includes the add-on:
> ```bash
> export COMPOSE_FILE=docker-compose.yml:docker-compose.elk.yml
> docker compose up -d      # now includes ELK
> docker compose ps
> docker compose down       # also removes ES + Kibana (needs the same file set)
> ```

---

## 3. See your audit records in Kibana

1. **Generate a record**: call an audited endpoint in Swagger (http://localhost:5235/swagger) — e.g.
   create a student or an instructor.
2. **Open Kibana**: http://localhost:5601 (the first load takes ~1 minute while it initializes).
3. **Create a data view** (tells Kibana which indices to read): **Stack Management → Data Views →
   Create data view** →
   - Name: `cleanarch-audit-*`
   - Index pattern: `cleanarch-audit-*`
   - Time field: `occurredOnUtc`
4. **Search** in **Discover**: filter by `actor`, `action`, `succeeded`, and expand `changes` to see
   the per-field before/after values. Field names are camelCase.

Quick sanity check straight from Elasticsearch (no Kibana needed):

```bash
curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"
```

---

## 4. Turn it off

```bash
docker compose -f docker-compose.yml -f docker-compose.elk.yml down    # stops ES + Kibana, KEEPS stored data
```

Then run the base stack normally again (`docker compose up -d`) and audit records flow back to Loki.
To also **delete** the stored audit index, add `-v` (wipes the `es-data` volume):

```bash
docker compose -f docker-compose.yml -f docker-compose.elk.yml down -v
```

---

## 5. Ports

| Service | Port | URL / check |
|---------|------|-------------|
| Elasticsearch | 9200 | http://localhost:9200/_cluster/health |
| Kibana | 5601 | http://localhost:5601 |

---

## 6. Troubleshooting

- **`elasticsearch` crash-loops with `vm.max_map_count [65530] is too low`.** You skipped step 1 — run
  it, then bring the stack up again.
- **Kibana shows "Kibana server is not ready yet".** It's still starting (can take a couple of
  minutes), or Elasticsearch isn't healthy yet. Check `docker compose ... ps` and
  `docker compose ... logs elasticsearch`.
- **No `cleanarch-audit-*` data view to pick / no data.** Make sure you (a) started **with** the
  `-f docker-compose.elk.yml` override, (b) actually called an audited endpoint, and (c) verify records
  exist with the `curl …/_search` command above. Without the override, records go to Loki, not
  Elasticsearch.
- **Everything is slow / the box struggles.** Elasticsearch is memory-hungry. The override caps its
  heap at 512 MB; if the machine is small, close other things or run ELK only when you need it.

---

## 7. Security note

This add-on runs Elasticsearch with **security disabled** for easy local dev — single node, no TLS,
no authentication — exactly like the native [`../../elk/`](../../elk/README.md) setup. **Don't expose
it on a network.** To harden for production (turn security back on, add credentials/TLS, set a
retention policy), see the "Going to production" section of [`../../elk/README.md`](../../elk/README.md);
the app code doesn't change, only the `Audit:Elasticsearch:*` settings.

---

## Files involved

```
observability/docker/
  docker-compose.yml          the base stack (app + Tempo/Loki/Prometheus/Grafana)
  docker-compose.elk.yml      THIS add-on: adds Elasticsearch + Kibana, re-points audit → ES
  README.md                   the base-stack beginner guide
  README-elk.md               this file

../../elk/README.md           the native (non-Docker) ELK setup + production hardening notes
```
