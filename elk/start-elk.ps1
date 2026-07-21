# Launches Elasticsearch, waits for it to be ready, then Kibana — for viewing the audit trail.
#
# DEV ONLY: Elasticsearch starts with security DISABLED so the audit sink can connect over plain
# http://localhost:9200 with no credentials. Do NOT run this way in production (see README.md).
#
#   powershell -ExecutionPolicy Bypass -File .\start-elk.ps1

$root = $PSScriptRoot

function Find-Executable([string] $dir, [string] $name) {
    if (-not (Test-Path $dir)) { return $null }
    Get-ChildItem -Path $dir -Recurse -Filter $name -ErrorAction SilentlyContinue | Select-Object -First 1
}

# --- Elasticsearch ---------------------------------------------------------------------------------
$es = Find-Executable "$root\elasticsearch" "elasticsearch.bat"
if (-not $es) {
    Write-Host "Elasticsearch not found. Paste the extracted distribution into elk\elasticsearch\ first." -ForegroundColor Yellow
    return
}

# Cap the JVM heap so Elasticsearch stays light on a dev laptop.
$env:ES_JAVA_OPTS = "-Xms512m -Xmx512m"

Write-Host "Starting Elasticsearch (security disabled, single-node) -> http://localhost:9200" -ForegroundColor Cyan
Start-Process -FilePath $es.FullName -WorkingDirectory $es.Directory.Parent.FullName -ArgumentList @(
    "-E", "xpack.security.enabled=false",
    "-E", "xpack.security.http.ssl.enabled=false",
    "-E", "xpack.security.enrollment.enabled=false",
    "-E", "discovery.type=single-node",
    "-E", "network.host=127.0.0.1"
)

Write-Host "Waiting for Elasticsearch to accept connections (first start can take ~1 min)..."
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 3
    try {
        $code = (Invoke-WebRequest -Uri "http://localhost:9200" -UseBasicParsing -TimeoutSec 3).StatusCode
        if ($code -eq 200) { $ready = $true; break }
    } catch { }
}

if ($ready) {
    Write-Host "Elasticsearch is up." -ForegroundColor Green
} else {
    Write-Host "Elasticsearch didn't report ready in time — check its window. Starting Kibana anyway (it will retry)." -ForegroundColor Yellow
}

# --- Kibana ----------------------------------------------------------------------------------------
$kibana = Find-Executable "$root\kibana" "kibana.bat"
if (-not $kibana) {
    Write-Host "Kibana not found. Paste the extracted distribution into elk\kibana\ then re-run (Elasticsearch is already up)." -ForegroundColor Yellow
    return
}

Write-Host "Starting Kibana -> http://localhost:5601 (takes ~1 min to become available)" -ForegroundColor Cyan
Start-Process -FilePath $kibana.FullName -WorkingDirectory $kibana.Directory.Parent.FullName -ArgumentList @(
    "--elasticsearch.hosts=http://localhost:9200"
)

Write-Host ""
Write-Host "Elasticsearch: http://localhost:9200   Kibana: http://localhost:5601"
Write-Host "Once the app has shipped audit records, create a Kibana data view for 'cleanarch-audit-*' (see README.md)."
