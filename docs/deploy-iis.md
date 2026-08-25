# Deploying CleanArch.Api on IIS

The application side of a production deploy: the API on a Windows Server, the
telemetry stores on a Docker host on the same internal network. The stack itself
— and the `Observability__*` / `Audit__*` settings that connect the two — is in
[`../observability/README.md`](../observability/README.md).

> Written from the code in this repository and standard IIS behaviour. Treat the
> first deploy as a rehearsal.

## 1. The setting that matters most

```
ASPNETCORE_ENVIRONMENT = Production
```

In Development the app seeds two well-known API keys — `dev-api-key-reporting`
and `dev-api-key-integration` (`ApiKeySeeder.cs`), both printed in this
repository's README — serves Swagger, and returns full exception detail. A site
running in Development is open to anyone who has read the repo.

Set it per-site (**IIS Manager → your site → Configuration Editor →
`system.webServer/aspNetCore` → `environmentVariables`**) or in `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\CleanArch.Api.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <environmentVariable name="ConnectionStrings__Students" value="Data Source=D:\CleanArchData\students.db" />
    <!-- ... the other three databases, and the Observability__/Audit__ settings ... -->
  </environmentVariables>
</aspNetCore>
```

Verify it after deploying rather than assuming:

```powershell
# 404 expected. Anything else means you are running in Development.
(Invoke-WebRequest http://your-api-host/swagger -SkipHttpErrorCheck).StatusCode

# Must NOT be 200.
(Invoke-WebRequest http://your-api-host/instructors `
  -Headers @{'X-Api-Key'='dev-api-key-integration'} -SkipHttpErrorCheck).StatusCode
```

## 2. Configuration and secrets

Configuration comes from environment variables, with `__` for nesting:
`Audit:Elasticsearch:Password` becomes `Audit__Elasticsearch__Password`.

`appsettings.Production.json` deliberately ships empty connection strings so a
missing override fails fast instead of quietly creating a stray database next to
the binary. Every value there must be supplied.

Secrets in `web.config` are readable by anyone who can read the site folder, and
they land in source control if that file is deployed from the repo. For anything
beyond a small internal deployment, put them in the app pool's environment or a
secret store instead. The passwords needed here are the Elasticsearch
`audit-writer` credentials — nothing else in this deployment has one.

## 3. Databases and migrations

Keep the SQLite files **outside** the site folder — anything under the site root
risks being served, wiped by a deploy, or locked during one:

```powershell
New-Item -ItemType Directory D:\CleanArchData -Force
icacls D:\CleanArchData /inheritance:r `
  /grant "IIS AppPool\CleanArchApiPool:(OI)(CI)(M)" /grant "Administrators:(F)"
```

The app pool identity needs **write** access, not just read: SQLite creates
`-wal` and `-shm` files next to each database.

Outside Development, EF Core migrations are opt-in via
`Database__MigrateOnStartup=true`. That is fine with exactly one instance. If
this site ever runs on two servers, they can race on the same schema — move
migrations to a separate deployment step before that happens.

### Back them up — nothing else does

`backup.sh` on the Docker host does not touch these files, and they are the most
irreplaceable data in the system.

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dest  = "E:\Backups\CleanArch\$stamp"
New-Item -ItemType Directory $dest -Force | Out-Null

# Stopping the pool guarantees a consistent copy. SQLite in WAL mode usually
# copies fine live, but "usually" is doing real work in that sentence.
Stop-WebAppPool -Name CleanArchApiPool
Start-Sleep -Seconds 3
Copy-Item D:\CleanArchData\*.db* $dest
Start-WebAppPool -Name CleanArchApiPool
```

Then copy it off this machine.

## 4. Data Protection keys

ASP.NET Core's Data Protection keys default to a location tied to the user
profile, and IIS app pools do not load one by default — so the keys are
regenerated on every recycle, silently invalidating anything protected with the
previous set. Either enable `loadUserProfile` / `setProfileEnvironment` on the
app pool, or point the keys at an explicit folder and back it up with the
databases.

## 5. Leave `Proxy__Enabled` off

`ReverseProxy.cs` exists for deployments behind a reverse proxy. On IIS it
should stay **off**: the ASP.NET Core Module already passes the real client
address through, so the app sees the correct `RemoteIpAddress` unaided. Turning
it on would make the app trust an `X-Forwarded-For` header that nothing on this
path sets — attacker-controlled, and it would let every caller spoof their source
IP, undermining the rate limiter and the audit trail.

The exception: if IIS itself sits behind a hardware load balancer or ARR proxy,
set `Proxy__Enabled=true` **and** `Proxy__KnownProxies__0` to that device's
address. Never leave the allowlist empty.

## 6. Firewall

**Inbound** — Prometheus on the Docker host pulls `/metrics` from this server:

```powershell
New-NetFirewallRule -DisplayName "Prometheus scrape (CleanArch.Api)" `
  -Direction Inbound -Protocol TCP -LocalPort 5235 `
  -RemoteAddress 10.20.30.40 -Action Allow
```

`/metrics` enumerates every route, request rate and error count, so restrict it
by source address. To also require a credential, mint a key
(`dotnet CleanArch.Api.dll --mint-api-key=prometheus-scraper --mint-api-key-roles=service`),
set `Observability__Metrics__RequireAuthentication=true`, and add it to the
scrape config — see the commented block in `observability/prod/prometheus.yml`.

**Outbound** — the app pushes to the Docker host on `4317` (traces), `3100`
(logs) and `9200` (audit). Most Windows Servers allow outbound by default; if
yours does not, allow those three.

## 7. Verify

```powershell
(Invoke-WebRequest http://localhost:5235/health/ready).StatusCode   # 200
(Invoke-WebRequest http://localhost:5235/metrics).Content.Length    # > 0
```

Then confirm the telemetry actually arrived, using the checklist in
[`../observability/README.md`](../observability/README.md) — a healthy app that
cannot reach the stores looks identical from here.
