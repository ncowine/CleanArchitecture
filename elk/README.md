# elk/ — Elasticsearch + Kibana for the audit trail

The API's audit sink ships every audited command to **Elasticsearch** (index `cleanarch-audit-*`), and
**Kibana** is the UI you search it in. This folder holds those two services, one per subfolder — same
"each stack its own folder" layout as `observability/`.

> **On Linux/Ubuntu / prefer Docker?** These native binaries aren't needed there — the containerized
> deployment ships Elasticsearch + Kibana as an opt-in add-on. See
> [`../observability/docker/README.md`](../observability/docker/README.md) §12 (run it with
> `-f docker-compose.yml -f docker-compose.elk.yml`).

```
elk/
  start-elk.ps1              launches Elasticsearch, waits for it, then Kibana
  elasticsearch/             ← paste the extracted Elasticsearch distribution here
  kibana/                    ← paste the extracted Kibana distribution here
```

| Service | Port | Role |
|---------|------|------|
| Elasticsearch | 9200 | stores audit records (`cleanarch-audit-YYYY.MM.dd`) |
| Kibana | 5601 | search/visualise the audit records |
| CleanArch.Api | 5235 | ships audit records here (config `Audit:Elasticsearch:Uri`) |

## 1. Download (match the client major — 9.x)

The API uses the `Elastic.Clients.Elasticsearch` **9.x** client, so grab **9.x** server + Kibana:

- Elasticsearch (Windows `.zip`): <https://www.elastic.co/downloads/elasticsearch>
- Kibana (Windows `.zip`): <https://www.elastic.co/downloads/kibana>

Extract them **into this folder** so you end up with:

```
elk/elasticsearch/…/bin/elasticsearch.bat
elk/kibana/…/bin/kibana.bat
```

(It's fine if the versioned folder is nested, e.g. `elk/elasticsearch/elasticsearch-9.x.y/bin/…` — the
start script searches for the `.bat` files.)

## 2. Launch

```powershell
cd X:\Repos\CleanArchitecture\elk
powershell -ExecutionPolicy Bypass -File .\start-elk.ps1
```

> **DEV ONLY.** The script starts Elasticsearch with **security disabled** (`xpack.security.enabled=false`,
> single-node, bound to 127.0.0.1) and caps the JVM heap at 512 MB so it's light on a laptop. This is why
> the audit sink can reach it over plain `http://localhost:9200` with no credentials. **Do not** run it this
> way in production — see "Going to production" below.

Readiness checks: Elasticsearch <http://localhost:9200> (returns JSON), Kibana <http://localhost:5601>
(takes ~1 min the first time).

## 3. Complete the flow

1. Start the app: `dotnet run --project ..\src\Api\CleanArch.Api`
   (`Audit:Elasticsearch:Uri` is already `http://localhost:9200` in `appsettings.json`.)
2. Trigger an audited command, e.g. create an instructor (dev API key seeded):
   ```powershell
   curl -X POST http://localhost:5235/instructors -H "Content-Type: application/json" `
     -H "X-Api-Key: dev-api-key-integration" `
     -d '{\"firstName\":\"Ada\",\"lastName\":\"Lovelace\",\"email\":\"ada@uni.edu\",\"departmentName\":\"Computer Science\",\"rank\":1}'
   ```
3. Confirm the record landed in Elasticsearch:
   ```powershell
   curl "http://localhost:9200/cleanarch-audit-*/_search?pretty"
   ```
4. In **Kibana** → **Stack Management → Data Views → Create data view**, name `cleanarch-audit-*`,
   index pattern `cleanarch-audit-*`, time field `occurredOnUtc`. Then open **Discover** to search by
   `actor`, `action`, and drill into `changes` (entityType, entityId, operation, and per-property
   before/after values). Fields are **camelCase** (`actor`, `action`, `succeeded`, `changes`, …).

## Going to production

Turn Elasticsearch security back **on** (TLS + auth), then give the app credentials instead of anonymous
access — set `Audit:Elasticsearch:ApiKey` (preferred) or `Username`/`Password`, from env/Key Vault, never
in source. Add an ILM policy on `cleanarch-audit-*` for retention. The app code doesn't change.
