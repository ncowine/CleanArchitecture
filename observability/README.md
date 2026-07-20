# observability/

Local Grafana LGTM stack for CleanArch.Api. One folder per stack — each holds its binary,
config, and data together. Full mental model + verification steps: [`../docs/observability.md`](../docs/observability.md).

```
observability/
  start-all.ps1        launches Tempo + Loki + Prometheus, each in its own window
  tempo/               traces store  — tempo.exe, tempo.yaml, tempo-data/   (:3200 query, :4317 OTLP)
  loki/                logs store    — loki-windows-amd64.exe, loki-config.yaml, loki-data/  (:3100)
  prometheus/          metrics store — prometheus.yml (drop prometheus.exe here)  (:9090)
  grafana/             datasources.yaml + dashboard-cleanarch-api.json (import into your Grafana :3000)
```

## Start everything

1. Put `prometheus.exe` into `prometheus/` (Grafana stays your native `:3000` service).
2. From this folder:
   ```powershell
   powershell -ExecutionPolicy Bypass -File .\start-all.ps1
   ```
3. Run the API: `dotnet run --project ..\src\Api\CleanArch.Api`
4. In Grafana: provision `grafana/datasources.yaml` (or add the 3 sources by hand), then import
   `grafana/dashboard-cleanarch-api.json`.

## Start one stack manually

```powershell
cd tempo;      .\tempo.exe -config.file=tempo.yaml
cd loki;       .\loki-windows-amd64.exe -config.file=loki-config.yaml
cd prometheus; .\prometheus.exe --config.file=prometheus.yml --storage.tsdb.path=data --web.enable-lifecycle
```

## Ports

| Stack | Port(s) | Ready check |
|-------|---------|-------------|
| Tempo | 3200 (query), 4317 (OTLP in) | http://localhost:3200/ready |
| Loki | 3100 | http://localhost:3100/ready |
| Prometheus | 9090 | http://localhost:9090/-/ready |
| Grafana | 3000 | your service |
| CleanArch.Api | 5235 | http://localhost:5235/health |
