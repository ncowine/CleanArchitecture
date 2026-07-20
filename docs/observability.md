# Observability (Grafana + LGTM, local Windows dev)

The API is instrumented with **OpenTelemetry** and emits three signals, each to its own store,
all read by Grafana. Grafana stores nothing itself — it only queries.

```
                    ┌───────────────── Grafana (:3000) ─────────────────┐
                    │  Prometheus DS      Tempo DS         Loki DS       │
                    └──────▲────────────────▲─────────────────▲──────────┘
                           │ query          │ query           │ query
                    ┌──────┴──────┐  ┌───────┴──────┐  ┌───────┴──────┐
   METRICS (pull)   │ Prometheus  │  │    Tempo     │  │     Loki     │
     scrape /metrics│   :9090     │  │ :4317 OTLP   │  │ :3100 OTLP   │
                    └──────┬──────┘  └───────▲──────┘  └───────▲──────┘
                           │ GET /metrics     │ push OTLP       │ push OTLP
                    ┌──────┴──────────────────┴─────────────────┴──────┐
                    │            CleanArch.Api (:5235)                  │
                    └───────────────────────────────────────────────────┘
```

| Signal | Store | Transport | Direction |
|--------|-------|-----------|-----------|
| Metrics (counts, latency) | Prometheus `:9090` | `/metrics` scrape | Prometheus **pulls** from the app |
| Traces (per-request timeline) | Tempo `:3200` query / `:4317` ingest | OTLP/gRPC | app **pushes** |
| Logs (text events) | Loki `:3100` | OTLP/HTTP | app **pushes** |

Why the split? You query each differently — numbers-over-time vs. one request's story vs. text at a
timestamp — so each needs a purpose-built store. Grafana correlates them: metric spike → example
trace → that trace's logs.

## Files

One folder per stack (binary + config + data together):

```
observability\
  start-all.ps1                     launches Tempo + Loki + Prometheus, each in its own window
  tempo\      tempo.exe, tempo.yaml, tempo-data\        (traces, local file storage)
  loki\       loki-windows-amd64.exe, loki-config.yaml, loki-data\   (logs)
  prometheus\ prometheus.yml (drop prometheus.exe here), data\        (metrics)
  grafana\    datasources.yaml (+ trace<->log links), dashboard-cleanarch-api.json
```

App wiring lives in `src/Api/CleanArch.Api/Observability.cs`; endpoints are overridable via the
`Observability` section of `appsettings.json`.

## Bring-up order

1. **Install** Tempo + Loki binaries into `observability\` (see table below), Grafana + Prometheus you already run.
   - Tempo: https://github.com/grafana/tempo/releases/latest → `tempo_*_windows_amd64.tar.gz`
   - Loki:  https://github.com/grafana/loki/releases/latest → `loki-windows-amd64.exe.zip`
2. **Start the stores** — drop `prometheus.exe` into `prometheus\`, then from `observability\`:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\start-all.ps1
   ```
   (or start each by hand — see `observability\README.md`).
   Ready checks: `http://localhost:3200/ready`, `http://localhost:3100/ready`, `http://localhost:9090/-/ready`.
3. **Prometheus** already scrapes the app via `prometheus\prometheus.yml` (the `cleanarch-api` job).
   Confirm at `http://localhost:9090/targets`.
4. **Point Grafana at the stores**: copy `grafana\datasources.yaml` into Grafana's
   `conf\provisioning\datasources\` and restart Grafana (or add the 3 sources by hand).
5. **Run the API** (`dotnet run --project src/Api/CleanArch.Api`), hit some endpoints (Swagger at
   `/swagger`), then import `grafana\dashboard-cleanarch-api.json` in Grafana.

## Verify each signal

- **Metrics**: `http://localhost:5235/metrics` returns text; Prometheus `/targets` shows `cleanarch-api` UP.
- **Traces**: in Grafana → Explore → Tempo → Search, recent requests appear.
- **Logs**: in Grafana → Explore → Loki → `{service_name="CleanArch.Api"}`.

## Production upgrade path

For real deployments, insert **Grafana Alloy** (or the OpenTelemetry Collector) between the app and the
stores: the app pushes OTLP to Alloy only, and Alloy fans out to Prometheus/Tempo/Loki. That decouples
the app from backend topology and lets you batch, sample, and re-route without redeploying. Swap the
`Observability:*` endpoints to Alloy's address — no code change.
