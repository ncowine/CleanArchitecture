# Launches the local observability stack — each store in its own console window so you
# see its logs and can Ctrl-C it independently. Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File .\start-all.ps1
#
# Grafana is NOT started here — it runs as your native Windows service on :3000.
# The API you run yourself (dotnet run --project src/Api/CleanArch.Api).

$root = $PSScriptRoot

Write-Host "Starting Tempo (traces)      -> :3200 query / :4317 OTLP"
Start-Process -FilePath "$root\tempo\tempo.exe" `
  -ArgumentList "-config.file=tempo.yaml" -WorkingDirectory "$root\tempo"

Write-Host "Starting Loki (logs)         -> :3100"
Start-Process -FilePath "$root\loki\loki-windows-amd64.exe" `
  -ArgumentList "-config.file=loki-config.yaml" -WorkingDirectory "$root\loki"

$prom = Join-Path $root "prometheus\prometheus.exe"
if (Test-Path $prom) {
    Write-Host "Starting Prometheus (metrics) -> :9090"
    Start-Process -FilePath $prom `
      -ArgumentList "--config.file=prometheus.yml","--storage.tsdb.path=data","--web.enable-lifecycle" `
      -WorkingDirectory "$root\prometheus"
} else {
    Write-Host "SKIPPED Prometheus: drop prometheus.exe into observability\prometheus\ then re-run." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Readiness: Tempo http://localhost:3200/ready  Loki http://localhost:3100/ready  Prometheus http://localhost:9090/-/ready"
Write-Host "Grafana (start/keep your service running): http://localhost:3000"
