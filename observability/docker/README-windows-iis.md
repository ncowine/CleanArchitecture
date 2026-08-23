# The application side: CleanArch.Api on IIS

This covers the **Windows Server** half of the production deployment. The Ubuntu
VM running Tempo, Loki, Prometheus, Grafana and ELK is covered in
[README-production.md](README-production.md).

```
Windows Server (IIS)                      Ubuntu VM
─────────────────────                     ─────────
CleanArch.Api  ──── traces  ─────────────▶ otlp.<domain>
               ──── logs    ─────────────▶ otlp.<domain>
               ──── audit   ─────────────▶ audit.<domain>
               ◀─── metrics scrape ─────── prometheus dials in
```

Three of those four flows leave this machine for the public internet. That is
the fact that drives most of what follows.

> **Not verified against a live server.** These settings were derived from the
> code in this repository and standard IIS behaviour, but nothing here has been
> run on an actual Windows Server. Treat the first deploy as a rehearsal.

---

## 1. The single most important setting

```
ASPNETCORE_ENVIRONMENT = Production
```

If this is missing or wrong, the app starts in Development, and in Development
it **seeds two API keys** — `dev-api-key-reporting` and `dev-api-key-integration`
(`ApiKeySeeder.cs`). Those exact strings are published in this repository's
README. An internet-facing site running in Development is wide open to anyone who
has read the repo, with no exploit required.

Development mode also serves Swagger at `/swagger` and returns full exception
detail including stack traces and connection strings.

On IIS, set it per-site rather than machine-wide (**IIS Manager → your site →
Configuration Editor → `system.webServer/aspNetCore` → `environmentVariables`**),
or in `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\CleanArch.Api.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
  </environmentVariables>
</aspNetCore>
```

**Verify it after deploying**, do not assume it:

```powershell
# Must return 404. Anything else means you are running in Development.
(Invoke-WebRequest https://your-api-host/swagger -SkipHttpErrorCheck).StatusCode

# Must NOT return 200.
(Invoke-WebRequest https://your-api-host/instructors `
  -Headers @{'X-Api-Key'='dev-api-key-integration'} -SkipHttpErrorCheck).StatusCode
```

---

## 2. Configuration and secrets

The app reads configuration from environment variables using `__` for nesting,
so `Observability:Tempo:Headers` becomes `Observability__Tempo__Headers`.

### Where to put them

`web.config` `<environmentVariables>` is the usual place, and it works — but that
file sits in the site folder and is readable by anyone who can read the site
directory. If you put secrets there, restrict it:

```powershell
icacls C:\inetpub\CleanArch.Api\web.config /inheritance:r `
  /grant "IIS AppPool\CleanArchApiPool:(R)" /grant "Administrators:(F)"
```

The better option is a **machine-level environment variable** readable only by
the app pool identity, or Windows Credential Manager / a secret store if you have
one. Do not put any of these in `appsettings.json` — that file is in source
control.

### The settings

```
ASPNETCORE_ENVIRONMENT              = Production

ConnectionStrings__Students         = Data Source=D:\CleanArchData\students.db
ConnectionStrings__Library          = Data Source=D:\CleanArchData\library.db
ConnectionStrings__TestPlans        = Data Source=D:\CleanArchData\testplans.db
ConnectionStrings__TesterGuide      = Data Source=D:\CleanArchData\testerguide.db

AllowedHosts                        = your-api-host.example.com

Database__MigrateOnStartup          = true

# Telemetry → the Ubuntu VM, through Caddy, over TLS.
Observability__Tempo__OtlpEndpoint  = https://otlp.example.com
Observability__Loki__OtlpEndpoint   = https://otlp.example.com/otlp/v1/logs
Observability__Tempo__Headers       = Authorization=Basic <base64 of user:password>
Observability__Loki__Headers        = Authorization=Basic <base64 of user:password>

# Audit → Elasticsearch, through Caddy, over TLS.
Audit__Elasticsearch__Uri           = https://audit.example.com
Audit__Elasticsearch__ApiKey        = <from scripts/bootstrap-elk.sh>

# Leave OFF. See section 5.
Proxy__Enabled                      = false
```

Build the Basic credential from the `INGEST_USER` and plaintext password you
chose on the Ubuntu VM:

```powershell
[Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('telemetry-writer:your-password'))
```

Note the two OTLP endpoints differ: traces go to the bare host (gRPC uses no
path) while logs go to `/otlp/v1/logs`. That asymmetry is in the protocol, not a
mistake.

### Why the headers exist at all

Tempo and Loki have **no authentication of their own**, and Loki's `auth_enabled`
setting is multi-tenancy rather than authentication. When everything ran on one
host that did not matter, because nothing could reach them. Now that the writer
is across the internet, this Basic credential — checked by Caddy — is the only
thing stopping a stranger from writing into your traces and logs, or reading them.

---

## 3. Database files and migrations

**Put the databases outside the site folder.** Anything under the site root risks
being served, wiped by a deploy, or locked during one. `D:\CleanArchData` with
permissions granted only to the app pool identity:

```powershell
New-Item -ItemType Directory D:\CleanArchData -Force
icacls D:\CleanArchData /inheritance:r `
  /grant "IIS AppPool\CleanArchApiPool:(OI)(CI)(M)" /grant "Administrators:(F)"
```

`Database__MigrateOnStartup=true` applies EF Core migrations when the app starts.
That is correct here because there is exactly one instance. Two things follow:

- The app pool identity needs **write** access to that folder, not just read —
  SQLite creates `-wal` and `-shm` files alongside each database.
- If you ever run this site on more than one server, two instances can race on
  the same schema. Move migrations to a separate step before that happens.

### Back them up — nothing else does

The Ubuntu VM's `backup.sh` does **not** touch these files. They are the most
irreplaceable data in the system and they need their own scheduled job here:

```powershell
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$dest  = "E:\Backups\CleanArch\$stamp"
New-Item -ItemType Directory $dest -Force | Out-Null

# Stop the app pool for a guaranteed-consistent copy. SQLite in WAL mode usually
# copies fine live, but "usually" is doing real work in that sentence.
Stop-WebAppPool -Name CleanArchApiPool
Start-Sleep -Seconds 3
Copy-Item D:\CleanArchData\*.db* $dest
Start-WebAppPool -Name CleanArchApiPool
```

Then copy it off this machine. A backup on the same disk as the data survives a
bad deploy but not a dead disk.

---

## 4. Data Protection keys

ASP.NET Core encrypts things with Data Protection keys. On IIS these default to a
location tied to the user profile, and **IIS app pools do not load a user profile
by default** — so the keys are regenerated on every recycle, silently
invalidating anything protected with the previous set.

Either enable `setProfileEnvironment` / `loadUserProfile` on the app pool, or
point the keys somewhere explicit and back that folder up with the databases.
This is a "works fine until it mysteriously doesn't" problem, so it is worth
five minutes now.

---

## 5. Do NOT enable `Proxy__Enabled` on IIS

The repository has forwarded-headers support (`ReverseProxy.cs`), added for the
case where the app sits behind a reverse proxy. **On IIS it should stay off.**

The ASP.NET Core Module already passes the real client address through to the
app, so the app sees the correct `RemoteIpAddress` without any help. Turning on
forwarded-headers handling would make the app trust an `X-Forwarded-For` header
that nothing on this path sets — and that header is attacker-controlled, so you
would be handing every caller the ability to spoof their own source IP. That
directly undermines the rate limiter and the audit trail.

The one exception: if IIS itself sits behind a hardware load balancer or ARR
proxy that terminates the client connection, then set `Proxy__Enabled=true` and
set `Proxy__KnownProxies__0` to that device's address — never leave the
allowlist empty.

---

## 6. Firewall

### Inbound — Prometheus scraping `/metrics`

Prometheus on the Ubuntu VM pulls metrics from this server, so it needs a way in.
Allow exactly one source:

```powershell
New-NetFirewallRule -DisplayName "Prometheus scrape from observability VM" `
  -Direction Inbound -Action Allow -Protocol TCP -LocalPort 443 `
  -RemoteAddress 203.0.113.20    # the Ubuntu VM's public IP
```

`/metrics` is not harmless — it enumerates every route, request rate and error
count in the service. Two more layers are worth adding:

1. **IIS IP restriction on the path.** Install *IP and Domain Restrictions*, then
   deny all and allow only the VM's address for the `/metrics` path specifically.
2. **Require a credential.** Mint a scraper key and make the endpoint demand it:

   ```powershell
   dotnet CleanArch.Api.dll --mint-api-key=prometheus-scraper --mint-api-key-roles=service
   ```

   Set `Observability__Metrics__RequireAuthentication=true`, then add the key to
   `prometheus.prod.yml` under `http_headers`. Change one end at a time and watch
   Prometheus's `/targets` page — if the header is wrong the target just goes
   DOWN, which is quiet rather than loud.

### Outbound

The app needs to reach `otlp.<domain>` and `audit.<domain>` on 443. If outbound
is restricted by default, allow those explicitly, or telemetry will silently stop
arriving — the app is built not to fail when the audit sink is unreachable
(`ElasticsearchAuditSink` never throws), so you will not get an error, you will
just get nothing.

---

## 7. HTTPS on the site itself

The API serves real clients, so it needs a valid certificate bound in IIS for
`AllowedHosts`. Prometheus also scrapes over HTTPS and will reject an untrusted
certificate — which is correct behaviour, so fix the certificate rather than
disabling verification on the Prometheus end.

If the certificate comes from a private/corporate CA, mount that CA bundle into
the Prometheus container and set `tls_config.ca_file` (there is a commented
example in `prometheus.prod.yml`).

---

## 8. Verify

From a machine that is **not** either server:

```powershell
# Swagger must be gone.
(Invoke-WebRequest https://your-api-host/swagger -SkipHttpErrorCheck).StatusCode   # 404

# Dev keys must not work.
(Invoke-WebRequest https://your-api-host/instructors `
  -Headers @{'X-Api-Key'='dev-api-key-integration'} -SkipHttpErrorCheck).StatusCode

# /metrics must not be readable from a random address.
(Invoke-WebRequest https://your-api-host/metrics -SkipHttpErrorCheck).StatusCode   # 403
```

Then confirm the telemetry path end to end: make a few requests against the API,
and within a minute they should appear in Grafana — as traces in Tempo, log lines
in Loki, and a moving line on the dashboard from Prometheus. If traces and logs
arrive but metrics do not, the scrape direction is the problem (section 6); if
metrics arrive but traces and logs do not, it is the ingest credential or the
outbound rule.

Check whether the app is being rejected at the proxy:

```bash
# On the Ubuntu VM — 401 means a bad ingest credential, 403 means APP_SERVER_IPS
# does not match where the app is actually connecting from.
docker compose -f docker-compose.prod.yml logs caddy | grep -E '401|403'
```

That second case is worth knowing about in advance: if the Windows server's
public IP changes, ingest starts returning 403 and telemetry stops, with nothing
on the Windows side reporting an error.
