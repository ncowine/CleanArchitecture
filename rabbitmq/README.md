# RabbitMQ broker: one user per application

A broker in Docker where every application connects as its **own** user, allowed only what it needs. A password
shipped with a desktop app can always be read by someone with that machine, so the aim is not to hide it but to make
a stolen one nearly useless and easy to revoke on its own.

## What each application user may do

`users.sh` gives each user, on the default vhost:

| Right | Allowed on | So the user can't |
|---|---|---|
| configure, write, read | the shared exchange `CleanArch`, and queues named after its own `ClientName` | create, bind, read or delete another application's queues; create other exchanges |
| publish (topic permission) | routing keys matching its regex, e.g. `^Equipment\.` for the API | send events in another application's name, e.g. a fake `LegacyApp1.Events.OrderSaved` |
| management UI / HTTP API | nothing (no tags) | log in to the UI or change the broker |

Revoking one application is deleting or re-passwording its user; nothing else is touched.

**Known gap:** every application declares the shared exchange when it connects, which RabbitMQ only allows with the
*configure* right on it, and that right also lets it delete the exchange. Closing that needs the applications to
check the exchange instead of declaring it, which is a legacy code change, so it stays open.

RabbitMQ has no API keys: over AMQP it takes a username and password. A per-application user with a long random
password is that API key: one secret per application, revoked on its own, with its own rights.

## Run it

```
cd rabbitmq
cp .env.example .env        # then fill in ADMIN_PASSWORD and one password per application
docker compose up -d
docker compose logs users   # "cleanarch-api: ready"
```

The management UI is http://localhost:15672 (ADMIN_USER / ADMIN_PASSWORD). There is no `guest` user.

## Add an application

1. In `users.sh`, add a line: user, password variable, the application's `ClientName`, and the routing keys it may
   publish. Legacy applications publish under the event's full type name, so that's usually
   `'^TheApp\.Events\.'`. Use `'^$'` for one that only receives.
2. Add its password to `.env`.
3. `docker compose run --rm users`, which also re-applies every other line.

Set `ClientName` explicitly in the application's configuration. Left empty, legacy applications fall back to the
executable's file name (e.g. `WpfApp1.exe`), and `users.sh` must then use exactly that.

## Point the applications at it

- **API:** keep passwords out of `appsettings*.json`. Set `Messaging:Buses:Main:UserName` and `:Password` with user
  secrets in development, and environment variables (`Messaging__Buses__Main__Password`) on the server.
- **Legacy apps:** the same keys in their `appsettings.json`, with the password from an environment variable
  (`Messaging__Buses__<Bus>__Password`) set at deployment, not committed.
- **Modern WPF apps** get events from the API over SignalR and need no broker user at all.

## Before other machines connect

The compose file listens on 127.0.0.1 only. Before setting `BIND_ADDR=0.0.0.0`:

- **Add TLS** (port 5671): plain AMQP sends the password readably over the network. Changing the applications only
  means changing their connection settings.
- Allow the ports from the company network or VPN only. Never expose 5672 or 15672 to the internet.
