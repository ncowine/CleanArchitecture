# Setting Up the Observability & Audit Stack on Ubuntu Server

**A complete, from-nothing manual.**

This document assumes you have never used Docker, Grafana, Prometheus, Loki,
Tempo, Elasticsearch or Kibana. It assumes you have not seen the source
repository and never will. Everything you need to type is written out in full,
in the order you type it, with the directory you are standing in stated at every
step.

What is *not* covered: installing the application itself. This guide assumes the
web API (`CleanArch.Api`) is **already deployed and running** on a separate
Windows server, and that someone can set a handful of configuration values on
it. Chapter 13 lists exactly which values, and you can hand that chapter to
whoever owns that machine.

---

## Table of contents

| # | Chapter | What you do there |
|---|---|---|
| 1 | [What you are building](#1-what-you-are-building) | The picture, before any typing |
| 2 | [The six programs, in plain English](#2-the-six-programs-in-plain-english) | Why each one exists |
| 3 | [Push vs pull — the one idea to hold on to](#3-push-vs-pull--the-one-idea-to-hold-on-to) | Prevents most firewall confusion |
| 4 | [What Docker is and why we use it](#4-what-docker-is-and-why-we-use-it) | Vocabulary: image, container, volume, compose |
| 5 | [Before you start — the checklist](#5-before-you-start--the-checklist) | Information and access to collect |
| 6 | [Step 1 — Prepare the Ubuntu server](#6-step-1--prepare-the-ubuntu-server) | Log in, update, set a kernel value |
| 7 | [Step 2 — Install Docker](#7-step-2--install-docker) | Command by command |
| 8 | [Step 3 — Create the folders](#8-step-3--create-the-folders) | The exact directory layout |
| 9 | [Step 4 — Create the configuration files](#9-step-4--create-the-configuration-files) | Seven files, typed out in full |
| 10 | [Step 5 — The docker-compose.yml, line by line](#10-step-5--the-docker-composeyml-line-by-line) | The big one, fully explained |
| 11 | [Step 6 — The firewall](#11-step-6--the-firewall) | Who is allowed to reach what |
| 12 | [Step 7 — Start the stack](#12-step-7--start-the-stack) | First run, and what "healthy" looks like |
| 13 | [Step 8 — Point the application at this server](#13-step-8--point-the-application-at-this-server) | The other machine's settings |
| 14 | [Step 9 — Verify all four signals](#14-step-9--verify-all-four-signals) | Prove it works; do not assume |
| 15 | [Step 10 — Set up Kibana for the audit trail](#15-step-10--set-up-kibana-for-the-audit-trail) | Clicking through Kibana once |
| 16 | [Step 11 — Keep it running](#16-step-11--keep-it-running) | Backups, updates, disk |
| 17 | [Troubleshooting](#17-troubleshooting) | Symptom, cause, fix |
| 18 | [Command cheat sheet](#18-command-cheat-sheet) | The ten commands you will actually use |
| 19 | [Glossary](#19-glossary) | Every term used in this document |

---

## 1. What you are building

One Ubuntu server that collects and displays everything the application does.

```
   WINDOWS SERVER (already set up)              UBUNTU SERVER (you build this)
   +---------------------------+                +----------------------------------+
   |                           |                |                                  |
   |   CleanArch.Api           | - traces ----> |  Tempo         ports 4317, 3200  |
   |   (the web application)   | - logs ------> |  Loki          port  3100        |
   |                           | - audit -----> |  Elasticsearch port  9200        |
   |                           |                |                                  |
   |   GET /metrics            | <- scrape ---- |  Prometheus    port  9090        |
   |                           |                |                                  |
   +---------------------------+                |  Grafana  :3000   Kibana  :5601  |
                                                |  (the two websites you look at)  |
                                                +----------------------------------+
```

When you are finished you will have two web pages you can open in a browser:

- **Grafana** at `http://<your-ubuntu-server>:3000` — charts of how fast and how
  busy the application is, the story of any individual request, and a live log
  viewer.
- **Kibana** at `http://<your-ubuntu-server>:5601` — a searchable record of who
  changed what and when (the audit trail).

Everything runs on that one Ubuntu server. Nothing here is exposed to the public
internet, and nothing here handles HTTPS certificates.

### A word about what this is *not*

This stack does not make the application work. It watches the application work.
If you switch this whole server off, the application keeps serving traffic — it
just stops being observable. That is a comforting property: you can experiment
here without endangering production.

---

## 2. The six programs, in plain English

There are six programs. Four of them store data, two of them are websites that
show you the data.

### The four stores

**Prometheus — the numbers.**
Every 15 seconds it asks the application "how many requests have you served? how
long did they take? how much memory are you using?" and writes the answers down
with a timestamp. It is very good at answering questions like *"was the site
slower at 3pm than at 2pm?"* It is very bad at answering *"what happened to Mrs
Smith's order?"* — it has no idea who Mrs Smith is. Numbers only, no text.

**Tempo — the story of one request.**
When someone calls the API, the application records a *trace*: a nested timeline
of everything that happened while serving that one call. "Total 240ms — of which
190ms was the database query, 30ms was serialising the response." Tempo stores
those timelines so you can pull up any single slow request and see exactly which
part of it was slow.

**Loki — the diary.**
The text lines the application writes ("Student 42 enrolled in course 7",
"Failed to connect to X"). Loki stores them and lets you search them. Think of
it as a `grep` across every log line from every server, with a date filter.

**Elasticsearch — the audit trail.**
A separate, more formal record: who did what, to which record, when. This is the
one you keep for compliance reasons and would show to an auditor. It is stored
apart from ordinary logs on purpose — ordinary logs are noisy and disposable, an
audit trail is neither.

### The two websites

**Grafana** reads Prometheus, Tempo and Loki and draws them. It stores no
telemetry of its own; it is a window onto the other three. This is where you
will spend most of your time.

**Kibana** does the same job for Elasticsearch. It is where you search the audit
trail.

### Why not just one tool for everything?

Because the three kinds of data have wildly different shapes. Numbers compress to
almost nothing and you keep them for a year. Text logs are bulky and you keep
them for a month. Traces are bulkier still and you keep only the recent ones.
Putting all three in one database gives you something that is expensive at all
three jobs. Splitting them is the standard industry answer, and Grafana stitches
the three back together in one interface so the split barely shows.

> **You will see "ELK" used for the audit half.** It stands for
> **E**lasticsearch, **L**ogstash, **K**ibana. We do not use Logstash — the
> application writes to Elasticsearch directly — so what you are actually
> installing is "EK". Everybody still says ELK.

---

## 3. Push vs pull — the one idea to hold on to

This is the single concept that, if skipped, will cost you an afternoon.

**Three of the four signals are PUSHED.** The application opens a connection to
your Ubuntu server and sends traces, logs and audit records *out*. The Ubuntu
server is passive: it listens and receives.

**Metrics are PULLED.** Prometheus does the opposite: it opens a connection *to*
the Windows server every 15 seconds and asks for the numbers. The application is
passive: it publishes a page at `/metrics` and waits to be asked.

Why the inconsistency? Because pulling gives Prometheus a free health check — if
it cannot reach the application, that itself is the alert. It is a deliberate
design choice by Prometheus, and everyone lives with it.

**The practical consequence is about firewalls, and it is the whole reason this
chapter exists:**

| Direction | Who dials | Who answers | Firewall rule needed |
|---|---|---|---|
| Traces, logs, audit | Windows server | Ubuntu server | **Inbound on Ubuntu**: ports 4317, 3100, 9200 |
| Metrics | Ubuntu server | Windows server | **Inbound on Windows**: port 5235 |

So there are firewall rules on *both* machines, pointing in *opposite*
directions. If later you find you have traces and logs but no metrics, you
already know where to look: the Windows firewall, not yours.

---

## 4. What Docker is and why we use it

### The problem Docker solves

Installing Elasticsearch the traditional way means: install the right version of
Java, create a service user, unpack the archive to the right place, edit three
config files, register a systemd service, set file permissions. Then do something
similar five more times for the other five programs. Then discover that Grafana
wanted a different version of some shared library than Prometheus did.

### What Docker does instead

An **image** is a frozen, pre-built copy of a program plus everything it needs to
run — its own miniature filesystem, its own libraries, its own Java if it needs
one. Somebody at Grafana Labs built the Grafana image, tested it, and published
it. You download it and it works, identically, on any Linux machine.

A **container** is one running copy of an image. The image is the recipe; the
container is the meal. You can throw a container away and start another from the
same image in two seconds, and it will be byte-for-byte what the first one was on
its first day.

That last property is the catch, and it leads to the next word.

### Volumes — where your data actually lives

Containers are disposable. Anything written *inside* a container is destroyed
when the container is destroyed. That is fine for a program and catastrophic for
a database.

A **volume** is a folder that lives on the Ubuntu server's real disk, managed by
Docker, and plugged into the container when it starts. Data written there
survives the container being deleted, upgraded or restarted. Every store in this
stack has one:

```
   container "elasticsearch"                      Ubuntu's real disk
   +----------------------------+                +------------------------+
   |  /usr/share/elasticsearch  |                | /var/lib/docker/       |
   |      /data  ---------------+--- volume ---->|   volumes/             |
   |                            |   "es-data"    |     cleanarch-prod_    |
   |  (replaced on upgrade)     |                |       es-data/         |
   +----------------------------+                | (survives everything)  |
                                                 +------------------------+
```

There are two kinds of volume and you will use both:

- **Named volume** (`es-data:/usr/share/elasticsearch/data`) — Docker picks the
  location on disk and manages it. Used for data. You never touch these files by
  hand.
- **Bind mount** (`./loki.yaml:/etc/loki/loki.yaml`) — you name a specific file
  or folder on the server, and it appears inside the container. Used for config
  files that you write and edit. This is how the files you are about to create
  reach the programs.

### Networks — how the containers talk to each other

Docker puts all the containers in this stack on one private network of their own.
On that network, **each container is reachable by its service name**. Grafana
asks for `http://prometheus:9090` and it just works, with nobody needing to know
any IP addresses. That is why the config files you are about to write say
`http://elasticsearch:9200` rather than a number.

That private network is invisible from outside. For anything on the *outside* to
reach a container — you with your browser, or the Windows server — the port has
to be explicitly **published**. That is what the `ports:` lines do, and every one
of them is a deliberate decision to open something.

### Compose — describing the whole stack in one file

Running six containers by hand, each with its right ports, volumes and settings,
would be six long unmemorable commands. **Docker Compose** lets you write all of
it down in one file called `docker-compose.yml`, and then:

```bash
docker compose up -d      # start everything
docker compose down       # stop everything
```

The file is the source of truth. If you ever need to rebuild this server, you
copy that file and its neighbours across and run one command. That is the real
payoff, and it is why this guide has you write files rather than click through
installers.

### The five words, summarised

| Word | Means |
|---|---|
| **Image** | A downloadable, frozen, ready-to-run copy of a program |
| **Container** | One running instance of an image |
| **Volume** | A folder on the real disk, plugged into a container, that outlives it |
| **Network** | The private LAN the containers share; they find each other by name |
| **Compose** | The `docker-compose.yml` file describing all of the above at once |

---

## 5. Before you start — the checklist

### The server you need

| | Minimum | Comfortable |
|---|---|---|
| Ubuntu Server | 22.04 LTS | 24.04 LTS |
| RAM | 8 GB | 16 GB |
| Disk | 100 GB | 250 GB |
| CPU | 2 cores | 4 cores |

RAM is the binding constraint. Elasticsearch alone reserves 1 GB and will happily
take 2, and the other five want roughly 3 GB between them. On a 4 GB machine
Elasticsearch gets killed by the kernel at some unpredictable moment, which is a
miserable thing to debug. Do not start with 4 GB.

Disk is about retention. This stack is configured to keep 30 days of everything,
which for a small internal API is typically 10–30 GB. Chapter 16 shows how to
measure your real usage after the first week.

### The four facts to write down now

You will type these into a file in Chapter 9. Get them before you begin — a large
share of failed setups are one of these being wrong.

| # | Fact | Example | How to find it |
|---|---|---|---|
| 1 | **This Ubuntu server's IP address** | `10.20.30.40` | Run `hostname -I` on it |
| 2 | **The Windows/API server's IP address** | `10.20.30.50` | Ask whoever runs it. Must be an **IP**, not a name |
| 3 | **The port the API listens on** | `5235` | Ask. It is in the IIS site binding |
| 4 | **Your own admin subnet** | `10.20.30.0/24` | The range of addresses you browse from |

Throughout this document, wherever you see `10.20.30.40` substitute fact 1, and
wherever you see `10.20.30.50` substitute fact 2.

### The access you need

- SSH access to the Ubuntu server, as a user who can run `sudo`.
- Someone with administrator access to the Windows server, for Chapter 13. You
  do not need that access yourself; you need ten minutes of their time.
- Outbound internet access from the Ubuntu server, to download the images. If the
  server is fully air-gapped this guide does not apply unmodified — the images
  would have to be transferred by hand.

### One habit worth adopting immediately

Every command in this guide states which directory to be in. When you come back
to this server in three months, the first thing to type is:

```bash
cd /opt/cleanarch/observability
pwd
```

Almost every command in this document only works from there or from its `prod`
subfolder. If a command says "no such file", check `pwd` before anything else.

---
## 6. Step 1 — Prepare the Ubuntu server

### 6.1 Log in

From your own machine:

```bash
ssh your-username@10.20.30.40
```

Everything from here until Chapter 13 happens on that server.

### 6.2 Confirm where you are

```bash
hostname -I          # this server's IP address(es) — confirm fact 1
lsb_release -a       # confirm it really is Ubuntu 22.04 or 24.04
free -h              # confirm the RAM
df -h /              # confirm the free disk space
```

`free -h` should show at least 8 GB total. `df -h /` should show at least 50 GB
available. If either is short, stop and get a bigger server — it is far cheaper
to fix now than after Elasticsearch has been silently killed twice.

### 6.3 Update the package lists and installed packages

```bash
sudo apt update
sudo apt upgrade -y
```

- `apt` is Ubuntu's package manager.
- `apt update` refreshes the *catalogue* of available software. It installs
  nothing.
- `apt upgrade -y` installs the newer versions of what is already there. `-y`
  answers "yes" to the confirmation prompt in advance.

If this ends by telling you a reboot is required, reboot now and reconnect:

```bash
sudo reboot
```

### 6.4 Set the kernel value Elasticsearch demands

Elasticsearch refuses to start unless the kernel allows a process to have a lot
of memory-mapped regions. Ubuntu's default is roughly 65,000; Elasticsearch
requires 262,144.

```bash
echo 'vm.max_map_count=262144' | sudo tee /etc/sysctl.d/99-elasticsearch.conf
sudo sysctl --system
```

Line by line:

- `echo 'vm.max_map_count=262144'` prints that text.
- `|` pipes the text into the next command instead of onto your screen.
- `sudo tee /etc/sysctl.d/99-elasticsearch.conf` writes it into that file with
  administrator rights. (`tee` is used rather than `>` because `>` is handled by
  your shell, which is not running as root, whereas `tee` is a program that can
  be.)
- `/etc/sysctl.d/` is where Ubuntu looks for permanent kernel settings, so this
  survives reboots.
- `sudo sysctl --system` re-reads every file in there and applies it now, so you
  do not have to reboot.

Verify it took effect:

```bash
sysctl vm.max_map_count
```

Expected output: `vm.max_map_count = 262144`. If it says 65530, the setting did
not apply and Elasticsearch will not start.

### 6.5 Set the timezone (optional but strongly recommended)

Every timestamp you look at for the next year comes from this machine.

```bash
timedatectl                                # what it is now
sudo timedatectl set-timezone Asia/Kolkata # or Europe/London, America/New_York...
```

`timedatectl list-timezones | grep -i <your city>` finds the exact spelling.

---

## 7. Step 2 — Install Docker

Ubuntu ships a version of Docker in its own repositories, but it is usually a
year or more behind and the packaging differs. Use Docker's official repository
instead — this is the procedure from Docker's own documentation.

### 7.1 Remove any old or conflicting packages

```bash
for pkg in docker.io docker-doc docker-compose podman-docker containerd runc; do
  sudo apt remove -y $pkg
done
```

This loops through the names that commonly conflict and removes each. On a fresh
server every one of them will say "not installed", which is fine and expected.

### 7.2 Install the tools needed to add a software repository

```bash
sudo apt update
sudo apt install -y ca-certificates curl gnupg
```

- `ca-certificates` — the list of certificate authorities, so HTTPS downloads can
  be verified.
- `curl` — a command-line tool for downloading over HTTP(S).
- `gnupg` — verifies cryptographic signatures on packages.

### 7.3 Add Docker's signing key

```bash
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg \
  -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
```

- The first line creates the folder Ubuntu keeps third-party signing keys in,
  with permission mode `0755` (owner may write; everyone may read).
- The second downloads Docker's public key. `-f` fail on HTTP errors, `-s`
  silent, `-S` but still show errors, `-L` follow redirects, `-o` write to this
  file.
- The third makes the key readable by all users, which `apt` needs.

This key is what lets `apt` prove that the Docker packages it downloads really
came from Docker and were not tampered with in transit.

### 7.4 Add Docker's repository to your software sources

```bash
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] \
  https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" \
  | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null
```

This looks alarming; it is one line of text being written to one file. Unpacked:

- `deb` — this is a Debian/Ubuntu package repository.
- `arch=$(dpkg --print-architecture)` — fills in your CPU architecture,
  `amd64` on a normal server.
- `signed-by=...` — only trust packages signed with the key you just added.
- `https://download.docker.com/linux/ubuntu` — where to download from.
- `$(. /etc/os-release && echo "$VERSION_CODENAME")` — fills in your Ubuntu
  release name, e.g. `jammy` for 22.04 or `noble` for 24.04.
- `stable` — the release channel, as opposed to `test` or `nightly`.
- `> /dev/null` at the end just hides the echoed copy from your screen.

### 7.5 Install Docker itself

```bash
sudo apt update
sudo apt install -y docker-ce docker-ce-cli containerd.io \
  docker-buildx-plugin docker-compose-plugin
```

What each package is:

| Package | What it does |
|---|---|
| `docker-ce` | The Docker engine — the background service that runs containers |
| `docker-ce-cli` | The `docker` command you type |
| `containerd.io` | The lower-level runtime the engine uses to actually start containers |
| `docker-buildx-plugin` | For building your own images. Unused here; comes as standard |
| `docker-compose-plugin` | Adds the `docker compose` subcommand. **This one matters** |

> **`docker compose` vs `docker-compose`.** The old standalone tool was spelled
> with a hyphen; the current plugin is spelled with a space. Every command in
> this guide uses the space. If you find an older tutorial online using the
> hyphen, the commands are otherwise nearly identical.

### 7.6 Confirm it works

```bash
sudo docker run --rm hello-world
```

This downloads a tiny test image and runs it. Expected: a paragraph beginning
"Hello from Docker!". `--rm` means "delete the container as soon as it exits", so
this leaves nothing behind.

If you get a permission or connection error, check the service is running:

```bash
sudo systemctl status docker
sudo systemctl enable --now docker    # start it and start it on every boot
```

### 7.7 Let your user run Docker without sudo

```bash
sudo usermod -aG docker $USER
```

This adds you to the `docker` group. **It does not take effect in your current
session** — log out and back in:

```bash
exit
```

then `ssh` back in and check:

```bash
docker ps
```

If that prints a table header without complaining about permissions, you are
done. From here on, no command in this guide needs `sudo` for Docker.

> **A caveat worth stating once:** membership of the `docker` group is
> effectively root access to this machine, because a container can be told to
> mount the host's filesystem. That is an accepted trade-off on a dedicated
> internal server like this one; do not hand out that group membership casually.

---

## 8. Step 3 — Create the folders

### 8.1 Why `/opt`

On Linux, `/opt` is the conventional home for self-contained software that did
not come from the package manager. Putting the stack there means anyone who
inherits this server will find it, and backup tools that skip `/home` will not
skip it.

### 8.2 Create the layout

```bash
sudo mkdir -p /opt/cleanarch/observability/prod
sudo mkdir -p /opt/cleanarch/observability/grafana/provisioning/datasources
sudo mkdir -p /opt/cleanarch/observability/grafana/provisioning/dashboards
sudo mkdir -p /opt/cleanarch/observability/grafana/dashboards
sudo chown -R $USER:$USER /opt/cleanarch
```

- `mkdir -p` creates a folder and any missing parents, and does not complain if
  it already exists.
- `chown -R $USER:$USER` makes you the owner of all of it, recursively, so you do
  not need `sudo` to edit files from now on.

### 8.3 What you have just created

```
/opt/cleanarch/observability/
├── prod/                       <- you will stand here to run every command
│   ├── docker-compose.yml        the description of all six containers
│   ├── .env                      your addresses and passwords
│   ├── tempo.yaml                traces store settings
│   ├── loki.yaml                 logs store settings
│   ├── prometheus.yml            what to scrape, and how often
│   └── backup.sh                 nightly backup script (Chapter 16)
└── grafana/
    ├── provisioning/
    │   ├── datasources/
    │   │   └── datasources.yaml  tells Grafana where the three stores are
    │   └── dashboards/
    │       └── dashboards.yaml   tells Grafana to auto-import the folder below
    └── dashboards/
        └── cleanarch-api.json    the dashboard itself
```

`grafana/` is a sibling of `prod/`, not inside it, because in the original
project the same Grafana configuration is shared by a development stack and this
production one. Keep the two folders together — the compose file reaches out of
`prod/` and into `../grafana/`, so moving one and not the other breaks it.

### 8.4 Go to the working directory

```bash
cd /opt/cleanarch/observability/prod
pwd
```

Expected: `/opt/cleanarch/observability/prod`. **Stay here for all of Chapters 9
to 12.**

---

## 9. Step 4 — Create the configuration files

### How to create a file from this document

Every file below is created the same way, with a *heredoc*:

```bash
cat > filename <<'EOF'
...file contents...
EOF
```

Read that as: "run `cat`, send its output into `filename`, and feed it every line
until a line containing only `EOF`". The quotes around `'EOF'` are important —
they tell the shell to treat the contents as literal text and not try to
interpret `$` signs or backticks inside it.

To use it: copy the whole block including the `cat` line and the final `EOF`,
paste it into your SSH session, press Enter. To edit a file afterwards, use
`nano filename` (save with `Ctrl+O`, `Enter`, exit with `Ctrl+X`).

After each file, `cat filename` prints it back so you can confirm it arrived
intact.

---

### 9.1 `.env` — your addresses and passwords

This is the only file you must customise, and it is the file everything else
reads its values from. Docker Compose automatically reads a file named exactly
`.env` in the folder you run it from, and substitutes those values wherever
`${SOMETHING}` appears in `docker-compose.yml`.

First, generate five passwords. Run this and keep the output somewhere safe:

```bash
for n in GRAFANA ELASTIC KIBANA_SYSTEM AUDIT; do
  echo "$n: $(openssl rand -base64 24)"
done
echo "KIBANA_ENCRYPTION_KEY: $(openssl rand -hex 32)"
```

`openssl rand -base64 24` produces 24 random bytes rendered as text — a password
no one will guess and no one will ever type twice. The last one is a 64-character
hex string, because Kibana requires at least 32 characters for its encryption
key.

Now create the file. **Every `CHANGE-ME` value below must be replaced**, and the
two IP addresses must be your fact 1 and fact 2:

```bash
cat > .env <<'EOF'
# =============================================================================
#  Production stack settings
#  This file is read automatically by "docker compose" from this directory.
#  It contains passwords: keep it at chmod 600 and never copy it anywhere public.
# =============================================================================

# ── Image versions ───────────────────────────────────────────────────────────
# Pinned deliberately. If these said "latest", a routine "docker compose pull"
# could swap in a new major version overnight and break the stack while you sleep.
TEMPO_VERSION=2.7.1
LOKI_VERSION=3.4.2
PROMETHEUS_VERSION=v3.1.0
GRAFANA_VERSION=11.5.1
# Must match the Elasticsearch client version the application was built against (9.x).
ELASTIC_VERSION=9.4.4

# ── Network ──────────────────────────────────────────────────────────────────
# FACT 1 — the LAN address of THIS Ubuntu server. Every port is published on this
# address only. Do not use 0.0.0.0 unless this machine has just one interface:
# Tempo and Loki accept anything that can reach them, so the network is their
# only protection.
BIND_ADDR=10.20.30.40

# FACT 2 — the Windows/API server, as an IP ADDRESS. Prometheus dials out to it.
# It must be an IP: the Docker feature used here does not resolve hostnames.
API_HOST_IP=10.20.30.50

# ── Grafana (the dashboards website) ─────────────────────────────────────────
GRAFANA_ADMIN_USER=admin
GRAFANA_ADMIN_PASSWORD=CHANGE-ME

# ── Elasticsearch and Kibana (the audit trail) ───────────────────────────────
# The "elastic" superuser. This is what you log into Kibana with.
# IMPORTANT: it is only applied on the FIRST start with an empty data volume.
# Changing it later requires the elasticsearch-reset-password tool (Chapter 17).
ELASTIC_PASSWORD=CHANGE-ME

# Kibana's own internal service account. You never type this; the setup
# container applies it for you.
KIBANA_SYSTEM_PASSWORD=CHANGE-ME

# 32+ characters, and it must never change. If it does, Kibana silently loses
# every saved search, dashboard and alert it had.
KIBANA_ENCRYPTION_KEY=CHANGE-ME-0123456789abcdef0123456789abcdef

# SHARED WITH THE WINDOWS SERVER — the write-only account the application ships
# audit records as. The same two values go into the app's configuration in
# Chapter 13. Write them down.
AUDIT_USER=audit-writer
AUDIT_PASSWORD=CHANGE-ME

# How much memory Elasticsearch reserves for itself up front. Rule of thumb:
# half of what you are willing to give it, and never above 31g.
ES_HEAP=1g

# ── Retention ────────────────────────────────────────────────────────────────
# Prometheus deletes old data when it hits whichever limit comes first.
# Tempo and Loki have their own 30-day settings in their own files.
# Unbounded retention is the usual way a self-hosted observability stack
# eventually takes down its own host.
PROMETHEUS_RETENTION_TIME=30d
PROMETHEUS_RETENTION_SIZE=8GB
EOF
```

Now edit it and put your real values in:

```bash
nano .env
```

Replace all five `CHANGE-ME` values and both IP addresses. Save with `Ctrl+O`,
`Enter`, then exit with `Ctrl+X`.

Then lock the file down, because it now contains passwords:

```bash
chmod 600 .env
ls -l .env
```

Expected: `-rw------- 1 you you ... .env` — readable and writable by you, nobody
else.

Finally, check for anything you missed:

```bash
grep CHANGE-ME .env
```

**This must print nothing.** If it prints a line, that value is still a
placeholder.

> **An honest limitation.** These passwords are visible to anyone who can run
> `docker inspect` on this machine — that is, to anyone with root or `docker`
> group access. On a single dedicated internal server where root already owns
> everything, that is an accepted trade-off. If you later need better, the
> upgrade path is Docker secrets.

---

### 9.2 `tempo.yaml` — the traces store

```bash
cat > tempo.yaml <<'EOF'
# Tempo — stores traces (the timeline of individual requests).

server:
  # The port Tempo answers queries on. Grafana reads from here.
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc:
          # Where the application pushes traces to. 0.0.0.0 means "listen on
          # every network interface INSIDE the container" — which is not the
          # same as exposing it to the world. What the outside world can reach
          # is decided solely by the ports: section of docker-compose.yml.
          endpoint: "0.0.0.0:4317"
        # There is normally a second receiver on port 4318 (OTLP over HTTP).
        # It is deliberately absent: nothing here uses it, and an unused open
        # listener is only extra surface for an attacker. Add it back if you
        # ever need it.

storage:
  trace:
    # "local" = plain files on disk, inside the tempo-data volume. The
    # alternative is cloud object storage (S3, GCS), which this deployment
    # does not use.
    backend: local
    local:
      path: /var/tempo/blocks
    wal:
      # Write-ahead log: incoming traces land here first, then get compacted
      # into blocks. This is what makes a crash lose seconds, not hours.
      path: /var/tempo/wal

compactor:
  compaction:
    # 720 hours = 30 days. Without this line, trace blocks accumulate until the
    # disk is full, at which point Tempo — and then usually the whole host —
    # stops. This is not optional.
    block_retention: 720h

usage_report:
  # Do not phone home to Grafana Labs with usage statistics.
  reporting_enabled: false
EOF
cat tempo.yaml
```

---

### 9.3 `loki.yaml` — the logs store

```bash
cat > loki.yaml <<'EOF'
# Loki — stores log lines.

# Counter-intuitive but important: in Loki, "auth_enabled" does NOT mean
# authentication. It means multi-tenancy. Turning it on would require every
# request to carry an X-Scope-OrgID header, would validate no credential
# whatsoever, and would break both the application's push and Grafana's
# queries. Leave it false. Loki's protection here is the network boundary.
auth_enabled: false

server:
  http_listen_port: 3100      # both ingest and queries use this one port
  grpc_listen_port: 9096      # internal only, never published
  log_level: warn             # Loki at "info" is extremely chatty

common:
  instance_addr: 127.0.0.1
  path_prefix: /loki
  storage:
    filesystem:
      chunks_directory: /loki/chunks
      rules_directory: /loki/rules
  replication_factor: 1       # single instance, so exactly one copy of each chunk
  ring:
    kvstore:
      store: inmemory         # no external coordination service needed

schema_config:
  configs:
    - from: 2020-10-24        # a date in the past: "this layout applies from here on"
      store: tsdb
      object_store: filesystem
      schema: v13             # the current Loki index format
      index:
        prefix: index_
        period: 24h           # one index file per day

limits_config:
  # REQUIRED for OpenTelemetry logs. Structured metadata is what carries the
  # trace_id on each log line, which is what makes "jump from this log line to
  # its trace" work. Without it, Loki rejects the application's pushes with a
  # 400 error.
  allow_structured_metadata: true
  volume_enabled: true

  # 30 days. On its own this line does nothing at all — the compactor section
  # below is what actually enforces it.
  retention_period: 720h

  # Rate limits. Their purpose is that an application bug logging in a tight
  # loop gets rejected with a 429, instead of filling the disk in an hour.
  # Raise them if you ever see legitimate traffic being rejected.
  ingestion_rate_mb: 8
  ingestion_burst_size_mb: 16
  max_line_size: 256KB
  max_label_names_per_series: 30

compactor:
  working_directory: /loki/compactor
  compaction_interval: 10m
  retention_enabled: true          # WITHOUT THIS, retention_period is ignored
  retention_delete_delay: 2h       # grace window — undo a mistaken change
  retention_delete_worker_count: 150
  delete_request_store: filesystem

analytics:
  reporting_enabled: false
EOF
cat loki.yaml
```

> The `retention_enabled: true` line is the one people miss. Setting
> `retention_period` alone looks like it works, produces no error, and quietly
> keeps everything forever.

---

### 9.4 `prometheus.yml` — what to scrape

This is the file that reaches out to the Windows server. **Change the port on the
`targets:` line if your API does not listen on 5235** (fact 3).

```bash
cat > prometheus.yml <<'EOF'
# Prometheus — the list of things to collect numbers from, and how often.
#
# NOTE: Prometheus does NOT read the .env file and does NOT expand ${...}.
# Everything in this file is edited by hand. After changing it:
#     docker compose restart prometheus

global:
  scrape_interval: 15s        # ask every target for its numbers this often
  evaluation_interval: 15s    # how often alert rules are evaluated (none defined here)

  # Prometheus 3.x defaults to UTF-8 metric names with dots
  # (http.server.request.duration). The dashboard, and every query example you
  # will find online, expect the classic underscore form
  # (http_server_request_duration_seconds). This line keeps the old form.
  # Remove it and your dashboard panels go blank while Explore still works —
  # a genuinely confusing failure.
  metric_name_escaping_scheme: underscores

scrape_configs:

  # Prometheus scraping itself. Useful: if this target is down, Prometheus has
  # a problem; if only the other one is down, the network or the app does.
  - job_name: prometheus
    static_configs:
      - targets: ['localhost:9090']

  # The application.
  - job_name: cleanarch-api
    metrics_path: /metrics    # the URL path the app publishes its numbers at

    # Plain http is acceptable on a trusted internal LAN. If your site is
    # HTTPS-only, change this to https and make sure the certificate is valid
    # for the name below — or replace "api-host" with the real hostname.
    scheme: http

    static_configs:
      # "api-host" is NOT a DNS name. It is an alias that docker-compose.yml
      # creates, pointing at API_HOST_IP from your .env file. Change the PORT
      # here if your IIS site uses a different one.
      - targets: ['api-host:5235']

    # OPTIONAL HARDENING.
    # /metrics enumerates every route, request rate and error count of your API.
    # The first line of defence is the Windows Firewall rule that only lets this
    # server reach it. To also require a credential:
    #   1. On the Windows server, mint a key:
    #        dotnet CleanArch.Api.dll --mint-api-key=prometheus-scraper --mint-api-key-roles=service
    #   2. Set the app setting Observability__Metrics__RequireAuthentication=true
    #   3. Uncomment the four lines below and paste the key
    # Change ONE end at a time: a mismatched header makes the target go DOWN
    # with no explanation anywhere.
    #
    # http_headers:
    #   X-Api-Key:
    #     values:
    #       - 'PASTE_THE_MINTED_API_KEY'
EOF
cat prometheus.yml
```

---

### 9.5 `datasources.yaml` — telling Grafana where to look

This file lives in the **grafana** folder, not `prod`. Note the different path in
the `cat` command — you do not need to change directory.

```bash
cat > ../grafana/provisioning/datasources/datasources.yaml <<'EOF'
# Grafana "provisioning": data sources created automatically at startup, so
# nobody has to add them by hand through the UI.
#
# The URLs use Docker SERVICE NAMES, not IP addresses. Grafana reaches the three
# stores across the private Docker network, where each container answers to its
# service name from docker-compose.yml.

apiVersion: 1

datasources:

  # ── METRICS ────────────────────────────────────────────────────────────────
  - name: Prometheus
    uid: prometheus         # a stable id the dashboard JSON refers to — do not rename
    type: prometheus
    access: proxy           # Grafana's server queries; your browser never connects directly
    url: http://prometheus:9090
    isDefault: true

  # ── TRACES ─────────────────────────────────────────────────────────────────
  - name: Tempo
    uid: tempo
    type: tempo
    access: proxy
    url: http://tempo:3200
    jsonData:
      # Adds a "Logs for this trace" button when viewing a trace: it jumps
      # into Loki filtered to the same trace id and time window.
      tracesToLogsV2:
        datasourceUid: loki
        filterByTraceID: true
        spanStartTimeShift: '-5m'
        spanEndTimeShift: '5m'
      serviceMap:
        datasourceUid: prometheus

  # ── LOGS ───────────────────────────────────────────────────────────────────
  - name: Loki
    uid: loki
    type: loki
    access: proxy
    url: http://loki:3100
    jsonData:
      # The link in the other direction: a trace_id appearing on a log line
      # becomes a clickable link into Tempo.
      derivedFields:
        - name: TraceID
          matcherType: label
          matcherRegex: trace_id
          url: '$${__value.raw}'
          datasourceUid: tempo
EOF
cat ../grafana/provisioning/datasources/datasources.yaml
```

Those two cross-links — trace to logs, log to trace — are the single most useful
thing in the whole stack. When someone reports "the site was slow at 2:10", you
find the slow trace, click through to its log lines, and read what the
application was complaining about at that exact moment.

> The `'$${__value.raw}'` is not a typo. Grafana substitutes environment
> variables into its provisioning files wherever it sees a `$`, so a literal
> dollar sign has to be written `$$`. Here the `${__value.raw}` that survives is
> a Grafana template that means "the trace id found on this log line".

---

### 9.6 `dashboards.yaml` — telling Grafana to import the dashboard

```bash
cat > ../grafana/provisioning/dashboards/dashboards.yaml <<'EOF'
# Automatically import every dashboard JSON file found in the folder below.
# docker-compose.yml mounts ../grafana/dashboards there, so any .json file you
# drop in that folder appears in Grafana within 30 seconds.

apiVersion: 1

providers:
  - name: CleanArch
    orgId: 1
    folder: ''                  # put them in Grafana's top-level "General" folder
    type: file
    disableDeletion: false
    allowUiUpdates: true        # you may tweak panels in the UI...
                                # ...but the changes are NOT written back to the
                                # .json file, and are lost if the file changes.
                                # Treat the file as the source of truth.
    updateIntervalSeconds: 30   # re-check the folder for changes this often
    options:
      path: /var/lib/grafana/dashboards
      foldersFromFilesStructure: false
EOF
cat ../grafana/provisioning/dashboards/dashboards.yaml
```

---

### 9.7 `cleanarch-api.json` — the dashboard itself

A dashboard is just a JSON description of panels and the queries behind them.
This is a compact starter with five panels — request rate, error rate, latency
percentiles, traffic by route, and a live log tail. Chapter 14 shows how to add
more, and you can also import ready-made dashboards from the community.

```bash
cat > ../grafana/dashboards/cleanarch-api.json <<'EOF'
{
  "uid": "cleanarch-overview",
  "title": "CleanArch.Api - Service Overview",
  "tags": ["cleanarch"],
  "timezone": "browser",
  "schemaVersion": 39,
  "version": 1,
  "refresh": "30s",
  "time": { "from": "now-1h", "to": "now" },
  "panels": [
    {
      "id": 1,
      "type": "timeseries",
      "title": "Requests per second",
      "datasource": { "type": "prometheus", "uid": "prometheus" },
      "gridPos": { "h": 8, "w": 12, "x": 0, "y": 0 },
      "fieldConfig": { "defaults": { "unit": "reqps" }, "overrides": [] },
      "targets": [
        {
          "refId": "A",
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "expr": "sum(rate(http_server_request_duration_seconds_count[$__rate_interval]))",
          "legendFormat": "all requests"
        }
      ]
    },
    {
      "id": 2,
      "type": "timeseries",
      "title": "Server errors per second (5xx)",
      "datasource": { "type": "prometheus", "uid": "prometheus" },
      "gridPos": { "h": 8, "w": 12, "x": 12, "y": 0 },
      "fieldConfig": { "defaults": { "unit": "reqps" }, "overrides": [] },
      "targets": [
        {
          "refId": "A",
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "expr": "sum(rate(http_server_request_duration_seconds_count{http_response_status_code=~\"5..\"}[$__rate_interval]))",
          "legendFormat": "5xx"
        }
      ]
    },
    {
      "id": 3,
      "type": "timeseries",
      "title": "Latency p50 / p95 / p99",
      "datasource": { "type": "prometheus", "uid": "prometheus" },
      "gridPos": { "h": 8, "w": 12, "x": 0, "y": 8 },
      "fieldConfig": { "defaults": { "unit": "s" }, "overrides": [] },
      "targets": [
        {
          "refId": "A",
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "expr": "histogram_quantile(0.50, sum by (le) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))",
          "legendFormat": "p50"
        },
        {
          "refId": "B",
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "expr": "histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))",
          "legendFormat": "p95"
        },
        {
          "refId": "C",
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "expr": "histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket[$__rate_interval])))",
          "legendFormat": "p99"
        }
      ]
    },
    {
      "id": 4,
      "type": "timeseries",
      "title": "Requests per second by route",
      "datasource": { "type": "prometheus", "uid": "prometheus" },
      "gridPos": { "h": 8, "w": 12, "x": 12, "y": 8 },
      "fieldConfig": { "defaults": { "unit": "reqps" }, "overrides": [] },
      "targets": [
        {
          "refId": "A",
          "datasource": { "type": "prometheus", "uid": "prometheus" },
          "expr": "sum by (http_route) (rate(http_server_request_duration_seconds_count[$__rate_interval]))",
          "legendFormat": "{{http_route}}"
        }
      ]
    },
    {
      "id": 5,
      "type": "logs",
      "title": "Application logs",
      "datasource": { "type": "loki", "uid": "loki" },
      "gridPos": { "h": 10, "w": 24, "x": 0, "y": 16 },
      "options": { "showTime": true, "wrapLogMessage": true, "sortOrder": "Descending" },
      "targets": [
        {
          "refId": "A",
          "datasource": { "type": "loki", "uid": "loki" },
          "expr": "{service_name=\"CleanArch.Api\"}"
        }
      ]
    }
  ]
}
EOF
```

Check it is valid JSON before moving on — a single misplaced comma means the
dashboard silently never appears:

```bash
python3 -c "import json; json.load(open('../grafana/dashboards/cleanarch-api.json')); print('JSON OK')"
```

Reading the panel definitions, in case you want to write your own later:

| Key | Meaning |
|---|---|
| `uid` | A permanent id for the dashboard. Its URL contains this, so keep it stable |
| `panels` | The list of boxes on the screen |
| `type` | `timeseries` for a line chart, `stat` for a single big number, `logs` for a log tail |
| `gridPos` | Position and size. The screen is 24 units wide; `w: 12` is half-width, `h: 8` is a normal chart height |
| `datasource.uid` | Which store to query — matches the `uid` values in `datasources.yaml` |
| `targets[].expr` | The query itself. PromQL for Prometheus, LogQL for Loki |
| `legendFormat` | The label under the chart. `{{http_route}}` inserts the value of that label |

And the queries themselves:

- `rate(x_count[$__rate_interval])` — "how fast is this counter increasing",
  i.e. requests per second. `$__rate_interval` is a Grafana variable that picks a
  sensible window for the zoom level you are at.
- `sum by (http_route) (...)` — total it up, but keep one line per route.
- `histogram_quantile(0.95, ...)` — "95% of requests were faster than this".
  p95 is the standard way to talk about latency; an average hides the slow tail
  that users actually notice.
- `{service_name="CleanArch.Api"}` — LogQL. Every Loki query starts with a label
  selector in braces, and this one means "log lines from this application".

---
## 10. Step 5 — The `docker-compose.yml`, line by line

This is the file that describes all six containers. Create it first, then read
the walkthrough below — every block is explained.

Make sure you are in the right place:

```bash
cd /opt/cleanarch/observability/prod
pwd
```

### 10.1 Create the file

```bash
cat > docker-compose.yml <<'EOF'
# =============================================================================
#  Production observability stack — one Docker host on the internal network
# =============================================================================
#   docker compose up -d          start, or apply changes to this file
#   docker compose ps             what is running
#   docker compose logs -f loki   follow one service's output
#   docker compose down           stop   (NEVER add -v here: it deletes the data)
# =============================================================================

name: cleanarch-prod

# ── Reusable blocks ──────────────────────────────────────────────────────────
# "&name" defines a block; "*name" pastes it in. Written once, used six times.

x-logging: &logging
  logging:
    driver: json-file
    options:
      max-size: "10m"     # without these two lines, container logs grow
      max-file: "3"       # until they fill the disk. This caps them at 30 MB.

x-hardening: &hardening
  security_opt:
    - no-new-privileges:true
  restart: unless-stopped

services:

  # ── TRACES ─────────────────────────────────────────────────────────────────
  tempo:
    image: grafana/tempo:${TEMPO_VERSION}
    <<: [*logging, *hardening]
    command: ["-config.file=/etc/tempo.yaml"]
    volumes:
      - ./tempo.yaml:/etc/tempo.yaml:ro
      - tempo-data:/var/tempo
    ports:
      - "${BIND_ADDR}:4317:4317"   # OTLP/gRPC ingest — the Windows server pushes here
      - "${BIND_ADDR}:3200:3200"   # query API — Grafana reads here
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://localhost:3200/ready | grep -q ready || exit 1"]
      interval: 30s
      timeout: 5s
      retries: 5
    mem_limit: 1g

  # ── LOGS ───────────────────────────────────────────────────────────────────
  loki:
    image: grafana/loki:${LOKI_VERSION}
    <<: [*logging, *hardening]
    command: ["-config.file=/etc/loki/loki.yaml"]
    volumes:
      - ./loki.yaml:/etc/loki/loki.yaml:ro
      - loki-data:/loki
    ports:
      - "${BIND_ADDR}:3100:3100"   # both ingest and queries
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://localhost:3100/ready | grep -q ready || exit 1"]
      interval: 30s
      timeout: 5s
      retries: 5
    mem_limit: 1g

  # ── METRICS ────────────────────────────────────────────────────────────────
  prometheus:
    image: prom/prometheus:${PROMETHEUS_VERSION}
    <<: [*logging, *hardening]
    command:
      - "--config.file=/etc/prometheus/prometheus.yml"
      - "--storage.tsdb.path=/prometheus"
      - "--storage.tsdb.retention.time=${PROMETHEUS_RETENTION_TIME}"
      - "--storage.tsdb.retention.size=${PROMETHEUS_RETENTION_SIZE}"
      # --web.enable-lifecycle is deliberately NOT here: it is an unauthenticated
      # endpoint that lets anyone reload or shut down Prometheus. Apply config
      # changes with "docker compose restart prometheus" instead.
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prometheus-data:/prometheus
    ports:
      - "${BIND_ADDR}:9090:9090"
    # prometheus.yml scrapes the name "api-host". This is what that name means.
    extra_hosts:
      - "api-host:${API_HOST_IP:?set API_HOST_IP in .env — the Windows server IP}"
    healthcheck:
      test: ["CMD-SHELL", "wget -qO- http://localhost:9090/-/ready | grep -q Ready || exit 1"]
      interval: 30s
      timeout: 5s
      retries: 5
    mem_limit: 1g

  # ── DASHBOARDS ─────────────────────────────────────────────────────────────
  grafana:
    image: grafana/grafana:${GRAFANA_VERSION}
    <<: [*logging, *hardening]
    environment:
      GF_SECURITY_ADMIN_USER: "${GRAFANA_ADMIN_USER}"
      GF_SECURITY_ADMIN_PASSWORD: "${GRAFANA_ADMIN_PASSWORD:?set GRAFANA_ADMIN_PASSWORD in .env}"
      GF_AUTH_ANONYMOUS_ENABLED: "false"
      GF_USERS_ALLOW_SIGN_UP: "false"
      GF_SERVER_ROOT_URL: "http://${BIND_ADDR}:3000"
      GF_ANALYTICS_REPORTING_ENABLED: "false"
      GF_ANALYTICS_CHECK_FOR_UPDATES: "false"
    volumes:
      - ../grafana/provisioning:/etc/grafana/provisioning:ro
      - ../grafana/dashboards:/var/lib/grafana/dashboards:ro
      - grafana-data:/var/lib/grafana
    ports:
      - "${BIND_ADDR}:3000:3000"
    depends_on:
      - prometheus
      - tempo
      - loki
    mem_limit: 512m

  # ── AUDIT TRAIL: storage ───────────────────────────────────────────────────
  elasticsearch:
    image: docker.elastic.co/elasticsearch/elasticsearch:${ELASTIC_VERSION}
    <<: [*logging, *hardening]
    environment:
      discovery.type: "single-node"
      xpack.security.enabled: "true"
      # Sets the built-in "elastic" superuser password on the FIRST start with an
      # empty volume only. Changing it later needs elasticsearch-reset-password.
      ELASTIC_PASSWORD: "${ELASTIC_PASSWORD:?set ELASTIC_PASSWORD in .env}"
      # TLS off: this link is internal to the LAN. Turn it on if the network is
      # shared with parties you do not control.
      xpack.security.http.ssl.enabled: "false"
      ES_JAVA_OPTS: "-Xms${ES_HEAP} -Xmx${ES_HEAP}"
    ulimits:
      memlock: { soft: -1, hard: -1 }
    volumes:
      - es-data:/usr/share/elasticsearch/data
    ports:
      - "${BIND_ADDR}:9200:9200"   # the Windows server ships audit records here
    healthcheck:
      test: ["CMD-SHELL", "curl -sf -u elastic:$${ELASTIC_PASSWORD} http://localhost:9200/_cluster/health || exit 1"]
      interval: 15s
      timeout: 10s
      retries: 20
      start_period: 90s            # Elasticsearch is slow to boot; do not panic
    mem_limit: 2g

  # ── AUDIT TRAIL: one-time account setup ────────────────────────────────────
  # Runs, creates two accounts and a role, then exits. Safe to run every time.
  es-setup:
    image: curlimages/curl:8.11.1
    <<: *logging
    depends_on:
      elasticsearch:
        condition: service_healthy
    environment:
      ELASTIC_PASSWORD: "${ELASTIC_PASSWORD}"
      KIBANA_SYSTEM_PASSWORD: "${KIBANA_SYSTEM_PASSWORD:?set KIBANA_SYSTEM_PASSWORD in .env}"
      AUDIT_USER: "${AUDIT_USER}"
      AUDIT_PASSWORD: "${AUDIT_PASSWORD:?set AUDIT_PASSWORD in .env}"
    entrypoint: ["/bin/sh", "-c"]
    command:
      - |
        set -e
        ES=http://elasticsearch:9200
        A="elastic:$${ELASTIC_PASSWORD}"

        echo "- setting the kibana_system password"
        curl -sSf -u "$$A" -X POST "$$ES/_security/user/kibana_system/_password" \
          -H "Content-Type: application/json" \
          -d "{\"password\":\"$${KIBANA_SYSTEM_PASSWORD}\"}" >/dev/null

        echo "- creating role cleanarch-audit-writer (write-only, audit indices only)"
        curl -sSf -u "$$A" -X PUT "$$ES/_security/role/cleanarch-audit-writer" \
          -H "Content-Type: application/json" \
          -d "{\"indices\":[{\"names\":[\"cleanarch-audit-*\"],\"privileges\":[\"auto_configure\",\"create_index\",\"write\"]}]}" >/dev/null

        echo "- creating user $${AUDIT_USER} — what the application logs in as"
        curl -sSf -u "$$A" -X PUT "$$ES/_security/user/$${AUDIT_USER}" \
          -H "Content-Type: application/json" \
          -d "{\"password\":\"$${AUDIT_PASSWORD}\",\"roles\":[\"cleanarch-audit-writer\"]}" >/dev/null

        echo "elasticsearch accounts ready"
    restart: "no"

  # ── AUDIT TRAIL: the website ───────────────────────────────────────────────
  kibana:
    image: docker.elastic.co/kibana/kibana:${ELASTIC_VERSION}
    <<: [*logging, *hardening]
    environment:
      ELASTICSEARCH_HOSTS: "http://elasticsearch:9200"
      ELASTICSEARCH_USERNAME: "kibana_system"
      ELASTICSEARCH_PASSWORD: "${KIBANA_SYSTEM_PASSWORD}"
      # Must be stable across restarts. If Kibana generates its own, it changes
      # on every boot and saved objects, alerts and reports are silently lost.
      XPACK_SECURITY_ENCRYPTIONKEY: "${KIBANA_ENCRYPTION_KEY:?set KIBANA_ENCRYPTION_KEY in .env (32+ chars)}"
      XPACK_ENCRYPTEDSAVEDOBJECTS_ENCRYPTIONKEY: "${KIBANA_ENCRYPTION_KEY}"
      XPACK_REPORTING_ENCRYPTIONKEY: "${KIBANA_ENCRYPTION_KEY}"
      SERVER_PUBLICBASEURL: "http://${BIND_ADDR}:5601"
    ports:
      - "${BIND_ADDR}:5601:5601"
    depends_on:
      es-setup:
        condition: service_completed_successfully
    healthcheck:
      test: ["CMD-SHELL", "curl -sf http://localhost:5601/api/status || exit 1"]
      interval: 15s
      timeout: 10s
      retries: 20
      start_period: 120s
    mem_limit: 1g

# ── Named volumes: the data that outlives the containers ─────────────────────
volumes:
  tempo-data:
  loki-data:
  prometheus-data:
  grafana-data:
  es-data:
EOF
```

Now check that Docker can read and understand it:

```bash
docker compose config --quiet && echo "compose file is valid"
```

`docker compose config` reads the file, substitutes every `${...}` from `.env`,
and reports any error. `--quiet` suppresses the (very long) expanded output.
If you want to *see* the substituted result — a genuinely useful way to confirm
your IP addresses landed where you expected — run it without `--quiet`:

```bash
docker compose config | head -40
```

---

### 10.2 The walkthrough

#### The top-level structure

A compose file has three top-level sections that matter here:

```yaml
name: cleanarch-prod    # 1. the project name
services:               # 2. the containers
volumes:                # 3. the data folders
```

**`name: cleanarch-prod`** is the project name. Docker prefixes everything it
creates with it: containers become `cleanarch-prod-grafana-1`, volumes become
`cleanarch-prod_es-data`. It is why you can run a second, unrelated stack on the
same machine without a collision — and you will need to know it when restoring
a backup in Chapter 16.

> **A note on YAML.** Indentation is the syntax. Two spaces means "belongs to the
> thing above". Tabs are a hard error. If you get a parse error you cannot see,
> it is nearly always a tab character or a misaligned space.

#### The reusable blocks (`x-logging`, `x-hardening`)

```yaml
x-logging: &logging
  logging:
    driver: json-file
    options:
      max-size: "10m"
      max-file: "3"
```

Anything starting with `x-` is ignored by Docker itself — it exists purely so you
can define something once and reuse it. `&logging` gives the block a name;
`<<: *logging` inside a service pastes the block in there.

What this particular block does matters: by default Docker keeps **every line a
container has ever printed**, forever, in a file on disk. A chatty container will
eventually fill the disk with its own logs, and this is a genuinely common way to
lose a server. These two options cap each container at 3 files of 10 MB.

```yaml
x-hardening: &hardening
  security_opt:
    - no-new-privileges:true
  restart: unless-stopped
```

- `no-new-privileges:true` — a process inside the container can never gain more
  privileges than it started with, even via a setuid binary. Cheap, and closes a
  whole class of container escape.
- `restart: unless-stopped` — if the container crashes, Docker restarts it; if
  the server reboots, Docker starts it again. The exception in the name is the
  useful part: if *you* deliberately stopped it, it stays stopped.

`<<: [*logging, *hardening]` merges both blocks into a service.

#### Every key you will see on a service

| Key | What it does | Concrete example from this file |
|---|---|---|
| `image:` | Which image to download and run | `grafana/tempo:2.7.1` |
| `command:` | Overrides the command the image runs by default | `-config.file=/etc/tempo.yaml` |
| `entrypoint:` | Overrides the program that command is passed to | `/bin/sh -c` for `es-setup` |
| `environment:` | Settings passed in as environment variables | `GF_SECURITY_ADMIN_USER` |
| `volumes:` | Folders/files plugged into the container | `./loki.yaml:/etc/loki/loki.yaml:ro` |
| `ports:` | Which container ports are reachable from outside | `10.20.30.40:3000:3000` |
| `depends_on:` | Start order, optionally waiting for health | Kibana waits for `es-setup` |
| `healthcheck:` | A command Docker runs to decide if it is really working | `wget .../ready` |
| `mem_limit:` | Hard memory ceiling for this container | `1g` |
| `ulimits:` | Kernel resource limits | `memlock` for Elasticsearch |
| `extra_hosts:` | Adds a name-to-IP entry inside the container | `api-host:10.20.30.50` |
| `restart:` | What to do when it exits | `unless-stopped` |

#### `image:` and why the versions are pinned

```yaml
image: grafana/tempo:${TEMPO_VERSION}
```

The part before the colon is the image name; after it, the **tag** — here a
version number, filled in from `.env` (`2.7.1`).

Every version is pinned on purpose. The alternative tag, `latest`, means "whatever
is newest at the moment you happen to pull", which turns a routine
`docker compose pull` into an unplanned major-version upgrade. Upgrading should
be a decision you make, on a day you chose, by editing `.env`.

#### `command:` — what the program is told to do

```yaml
command: ["-config.file=/etc/tempo.yaml"]
```

Each image has a built-in default command. This replaces it. Here it tells Tempo
which config file to read — and `/etc/tempo.yaml` is the path *inside the
container*, which the next section puts there.

Prometheus takes several arguments, so its `command:` is a list:

```yaml
command:
  - "--config.file=/etc/prometheus/prometheus.yml"
  - "--storage.tsdb.path=/prometheus"
  - "--storage.tsdb.retention.time=${PROMETHEUS_RETENTION_TIME}"
  - "--storage.tsdb.retention.size=${PROMETHEUS_RETENTION_SIZE}"
```

The two retention arguments are Prometheus's disk safety valve: it deletes old
data when it reaches 30 days **or** 8 GB, whichever comes first.

#### `volumes:` — the most important lines to understand

```yaml
volumes:
  - ./tempo.yaml:/etc/tempo.yaml:ro     # a bind mount
  - tempo-data:/var/tempo               # a named volume
```

The format is `source:destination:options`.

**Line 1 is a bind mount.** The source starts with `./`, so it is a real path on
this server, relative to this folder — `/opt/cleanarch/observability/prod/tempo.yaml`.
It appears inside the container at `/etc/tempo.yaml`. `:ro` means read-only: the
container can read it but never modify it.

This is precisely how the file you wrote in Chapter 9 reaches the program. It
also means editing that file and restarting the container is all it takes to
change the configuration — there is nothing to rebuild.

**Line 2 is a named volume.** The source is a bare name, so Docker manages the
storage itself and keeps it under `/var/lib/docker/volumes/`. This is where the
actual traces live. Delete and recreate the container as often as you like; the
data stays.

The distinction in one sentence: **bind mounts are for configuration you write;
named volumes are for data the program writes.**

Grafana has three, which is a useful illustration:

```yaml
- ../grafana/provisioning:/etc/grafana/provisioning:ro   # config, read-only
- ../grafana/dashboards:/var/lib/grafana/dashboards:ro   # config, read-only
- grafana-data:/var/lib/grafana                          # its database
```

The first two go *up* one folder — that is the sibling `grafana/` directory. The
third is where Grafana keeps its own users, sessions and preferences; without it,
every user you create is lost on the next restart.

#### `ports:` — the only lines that open anything up

```yaml
ports:
  - "${BIND_ADDR}:4317:4317"
```

Three parts, `IP:host-port:container-port`:

- **`${BIND_ADDR}`** — which of this server's addresses to listen on. Filled in
  from `.env`. This is what stops the stack from being reachable on every
  interface the machine has.
- **`4317`** (middle) — the port on the Ubuntu server.
- **`4317`** (right) — the port inside the container. They are usually the same
  number, and there is no requirement that they be. `"${BIND_ADDR}:9999:3000"`
  would publish Grafana on port 9999.

**Nothing is reachable from outside unless it appears here.** Loki's gRPC port
9096 is configured in `loki.yaml` but never published, so nothing off this
machine can touch it. Meanwhile Grafana reaches Tempo on port 3200 over the
private Docker network whether it is published or not; publishing it merely lets
*you* poke at it from a shell.

#### `environment:` — settings passed in as variables

```yaml
environment:
  GF_SECURITY_ADMIN_USER: "${GRAFANA_ADMIN_USER}"
  GF_SECURITY_ADMIN_PASSWORD: "${GRAFANA_ADMIN_PASSWORD:?set GRAFANA_ADMIN_PASSWORD in .env}"
  GF_AUTH_ANONYMOUS_ENABLED: "false"
```

Most server programs read configuration from environment variables, and
container images lean on this heavily. Grafana's rule is `GF_` + section +
setting, so `GF_SECURITY_ADMIN_PASSWORD` sets `admin_password` in the `security`
section of its config file.

Note `GF_AUTH_ANONYMOUS_ENABLED: "false"` and `GF_USERS_ALLOW_SIGN_UP: "false"`.
Anonymous access is a convenience some development setups enable; on a production
box it means anyone who reaches port 3000 sees all your telemetry, and sign-up
would let them create themselves an account. Both stay off.

**The `:?` syntax is worth learning:**

```yaml
${GRAFANA_ADMIN_PASSWORD:?set GRAFANA_ADMIN_PASSWORD in .env}
```

means "substitute this variable, and if it is missing or empty, refuse to start
and print this message". Every password in this file uses it. The effect is that
a forgotten password produces a clear error at startup, rather than a Grafana
with a blank admin password quietly serving your metrics to the network.

#### `healthcheck:` — the difference between "running" and "working"

```yaml
healthcheck:
  test: ["CMD-SHELL", "wget -qO- http://localhost:3200/ready | grep -q ready || exit 1"]
  interval: 30s
  timeout: 5s
  retries: 5
  start_period: 90s
```

A container can be *running* while the program inside it is still starting up,
or wedged, or crashed in a way that did not exit the process. A health check is a
command Docker runs *inside* the container on a schedule to find out.

- `test` — the command. `CMD-SHELL` means "run this as a shell command line".
  The one above fetches the program's own readiness page and looks for the word
  `ready`; `|| exit 1` reports failure if that fails.
- `interval: 30s` — how often to run it.
- `timeout: 5s` — how long to wait before calling that attempt a failure.
- `retries: 5` — how many consecutive failures before the container is marked
  `unhealthy`.
- `start_period: 90s` — a grace window at startup during which failures do not
  count. Elasticsearch needs 90 seconds and Kibana 120; without this they would
  be marked unhealthy while still legitimately booting.

`docker compose ps` shows the result, and it is the first thing you look at when
something is wrong.

#### `depends_on:` — start order, and the useful version of it

```yaml
depends_on:
  - prometheus
  - tempo
  - loki
```

The simple form (Grafana's) only says "start these first". It does **not** wait
for them to be ready — it waits for them to be *started*, which is a much weaker
promise. That is fine here, because Grafana retries its data sources.

The powerful form appears twice:

```yaml
depends_on:
  elasticsearch:
    condition: service_healthy            # wait for the HEALTH CHECK to pass
```
```yaml
depends_on:
  es-setup:
    condition: service_completed_successfully   # wait for it to EXIT with code 0
```

Together these enforce a strict chain: Elasticsearch must be genuinely healthy
before `es-setup` runs; `es-setup` must succeed before Kibana starts. Without it,
Kibana would start, fail to log in with a password that has not been set yet, and
crash-loop.

#### `es-setup:` — the one-shot container

This is the only unusual service, and it is worth understanding because the
pattern is common.

It is not a server. It is a tiny container holding just `curl`, whose job is to
make four API calls to Elasticsearch and then exit:

1. Set the password for `kibana_system`, Kibana's internal account.
2. Create a role called `cleanarch-audit-writer` that can **only** write, and
   only to indices named `cleanarch-audit-*`.
3. Create the user `audit-writer` with that role.

That role definition is the security point of the whole exercise. The application
ships audit records using an account that cannot read them back, cannot delete
them, and cannot touch any other index. If the application server is ever
compromised, the attacker gets an account that can only append to the audit log.

`restart: "no"` overrides the usual restart policy — this container is *supposed*
to exit. In `docker compose ps` it will show as `Exited (0)`, and **that is
success**, not a failure. It runs again harmlessly on every `up`, because each of
its calls simply overwrites the previous state.

Two details in its script:

```yaml
entrypoint: ["/bin/sh", "-c"]
command:
  - |
    set -e
    ...
```

The `|` starts a multi-line block of text in YAML, which becomes the shell script
passed to `sh -c`. `set -e` means "stop at the first command that fails" — so a
failed account creation makes the container exit non-zero, which stops Kibana
from starting, which makes the problem visible instead of mysterious.

And the doubled dollar signs:

```
A="elastic:$${ELASTIC_PASSWORD}"
```

`$$` is how you write a literal `$` in a compose file. A single `$` would be
substituted by Docker Compose from `.env` before the container ever starts; `$$`
becomes a plain `$` and is left for the shell *inside* the container to expand at
run time. Getting this wrong is a classic compose bug — one that happens to leak
your password into the process list.

#### The Elasticsearch-specific lines

```yaml
ulimits:
  memlock: { soft: -1, hard: -1 }
```

Allows Elasticsearch to lock its memory so the kernel never swaps its heap to
disk. `-1` means unlimited. A swapped-out search engine is pathologically slow,
so this is standard for Elasticsearch everywhere, not a quirk of this setup.

```yaml
ES_JAVA_OPTS: "-Xms${ES_HEAP} -Xmx${ES_HEAP}"
```

Java's minimum and maximum heap. They are set to the same value deliberately —
it stops the JVM spending time growing the heap, and makes memory use
predictable. Note this is the *Java heap*, not the container's total: Elasticsearch
also needs memory outside the heap, which is why `mem_limit` is `2g` while
`ES_HEAP` is `1g`.

```yaml
mem_limit: 2g
```

A hard ceiling. If the container exceeds it, the kernel kills it. Every service
here has one, and the reason is containment: without limits, one misbehaving
container can consume all the RAM and take the other five down with it. With
limits, it dies alone and restarts.

#### `extra_hosts:` — how Prometheus finds the Windows server

```yaml
extra_hosts:
  - "api-host:${API_HOST_IP:?set API_HOST_IP in .env — the Windows server IP}"
```

This adds one line to the container's `/etc/hosts` file, so that inside the
Prometheus container the name `api-host` resolves to your Windows server's IP.

That is why `prometheus.yml` can say `targets: ['api-host:5235']` instead of
containing an IP address. The address lives in exactly one place — `.env` — and
if the Windows server ever moves, you change one line.

It must be an **IP address**: this mechanism writes a hosts entry, and a hosts
entry cannot point at another name.

#### The `volumes:` section at the bottom

```yaml
volumes:
  tempo-data:
  loki-data:
  prometheus-data:
  grafana-data:
  es-data:
```

Every named volume used above must also be declared here. The empty value means
"default settings, Docker manages it". These five names are all your durable
data; treat this list as the answer to "what would I lose if this server burned
down".

---

## 11. Step 6 — The firewall

Nothing in this stack should be reachable from the public internet, and most of
it should not even be reachable from most of your own network.

`ufw` ("uncomplicated firewall") is Ubuntu's front end to the kernel firewall.

### 11.1 First, do not lock yourself out

```bash
sudo ufw allow OpenSSH
```

Run this **before** enabling the firewall. If you enable `ufw` with a default
deny policy and no SSH rule, your session survives but your next login does not,
and you will be visiting the server room.

### 11.2 Set the defaults

```bash
sudo ufw default deny incoming
sudo ufw default allow outgoing
```

"Refuse everything arriving unless a rule allows it; allow anything this server
initiates." This is the standard posture for a server.

### 11.3 Let the Windows server ship telemetry in

Substitute your fact 2 address:

```bash
sudo ufw allow from 10.20.30.50 to any port 4317 proto tcp comment 'traces from API'
sudo ufw allow from 10.20.30.50 to any port 3100 proto tcp comment 'logs from API'
sudo ufw allow from 10.20.30.50 to any port 9200 proto tcp comment 'audit from API'
```

Read one of these as: allow traffic **from** that one address, **to** any address
on this machine, **on** port 4317, TCP only. No other machine on the network can
reach those ports.

This matters because Tempo and Loki have no authentication of their own, and none
is available in the single-binary builds used here. The network boundary *is*
their security. Anything that can reach port 3100 can write anything it likes
into your logs.

### 11.4 Let administrators reach the websites

Substitute your fact 4 subnet:

```bash
sudo ufw allow from 10.20.30.0/24 to any port 3000 proto tcp comment 'Grafana UI'
sudo ufw allow from 10.20.30.0/24 to any port 5601 proto tcp comment 'Kibana UI'
sudo ufw allow from 10.20.30.0/24 to any port 9090 proto tcp comment 'Prometheus UI'
```

`/24` means "the 256 addresses from 10.20.30.0 to 10.20.30.255". If you only ever
browse from one machine, use its address on its own and drop the `/24` — smaller
is better.

### 11.5 Turn it on and check

```bash
sudo ufw enable          # answer y
sudo ufw status verbose
```

The output should list exactly the rules you added and nothing else. Ports 3200
(Tempo queries) and 9096 (Loki gRPC) should **not** appear — they are used only
inside Docker's private network.

> **A wrinkle you should know about.** Docker manipulates the kernel firewall
> directly and can, in some configurations, publish a port in a way that bypasses
> `ufw` rules. This stack avoids the problem by publishing every port on
> `${BIND_ADDR}` rather than on all interfaces — so if that server has a public
> interface, the ports are not bound to it in the first place. If your server is
> internet-facing, verify from an outside machine with
> `nc -zv <public-ip> 3000` and confirm it is refused.

---

## 12. Step 7 — Start the stack

### 12.1 Download the images first

```bash
cd /opt/cleanarch/observability/prod
docker compose pull
```

This downloads all six images — roughly 3 GB, most of it Elasticsearch and
Kibana — and does nothing else. Doing it
as a separate step means the actual start is fast, and a network problem shows up
here rather than tangled up with a startup problem.

### 12.2 Start everything

```bash
docker compose up -d
```

- `up` — create and start every service in the file.
- `-d` — "detached": run in the background and give you your prompt back.
  Without it, all six containers' output streams to your terminal and `Ctrl+C`
  stops the lot.

Expect it to take two to three minutes to settle. Elasticsearch is the slow one.

### 12.3 Watch it come up

```bash
docker compose ps
```

Run it a few times over the first three minutes. The end state you are looking
for:

```
NAME                          STATUS
cleanarch-prod-elasticsearch-1  Up 2 minutes (healthy)
cleanarch-prod-es-setup-1       Exited (0)
cleanarch-prod-grafana-1        Up 2 minutes
cleanarch-prod-kibana-1         Up 1 minute (healthy)
cleanarch-prod-loki-1           Up 2 minutes (healthy)
cleanarch-prod-prometheus-1     Up 2 minutes (healthy)
cleanarch-prod-tempo-1          Up 2 minutes (healthy)
```

Reading that:

- **`Up ... (healthy)`** — running, and its health check passes. This is the goal.
- **`Up ... (starting)`** — running, still inside its `start_period`. Wait.
- **`Exited (0)` for `es-setup`** — **correct**. That container is meant to run
  once and finish. Zero means it succeeded.
- **`Exited (1)` or `Restarting`** for anything else — a problem. Go to 12.4.

### 12.4 When something is not right, read its log

```bash
docker compose logs elasticsearch      # everything it has printed
docker compose logs --tail 50 kibana   # just the last 50 lines
docker compose logs -f loki            # follow live; Ctrl+C to stop watching
docker compose logs                    # all services interleaved
```

The log is almost always explicit about what is wrong. Chapter 17 lists the
common messages and what they mean.

### 12.5 Prove the stack itself works, before involving the application

These commands run on the server and check each service directly. Substitute your
`BIND_ADDR`:

```bash
curl -s http://10.20.30.40:3200/ready              # Tempo   -> "ready"
curl -s http://10.20.30.40:3100/ready              # Loki    -> "ready"
curl -s http://10.20.30.40:9090/-/ready            # Prometheus -> "Prometheus Server is Ready."
curl -s -o /dev/null -w '%{http_code}\n' http://10.20.30.40:3000   # Grafana -> 302 (redirect to login)
curl -s -u elastic:YOUR_ELASTIC_PASSWORD http://10.20.30.40:9200/_cluster/health
```

The last one returns JSON. In it, `"status":"yellow"` is what you want on a
single-node cluster — yellow means "the data is fine, but there is no replica",
which is correct and expected when there is only one node. `green` is
unreachable here and `red` means real trouble.

### 12.6 Open the two websites

From your own machine (not the server):

- **Grafana** — `http://10.20.30.40:3000`
  Log in with `admin` and the `GRAFANA_ADMIN_PASSWORD` from `.env`.
  It may ask you to change the password; you may skip that.
  Go to **Connections → Data sources**. Prometheus, Tempo and Loki should already
  be listed — that is the `datasources.yaml` file working. Click each and press
  **Save & test**; each should report success.
  Go to **Dashboards**. "CleanArch.Api - Service Overview" should be there. It
  will be empty of data, which is correct: the application has not been told
  where to send anything yet.

- **Kibana** — `http://10.20.30.40:5601`
  Log in as `elastic` with `ELASTIC_PASSWORD`. Kibana takes a minute to become
  responsive on first load and may show "Kibana server is not ready yet" — wait
  and reload before assuming failure.

If both pages load, the Ubuntu half of the job is done.

---
## 13. Step 8 — Point the application at this server

Nothing arrives until the application is told where to send it. This chapter
happens on the **Windows server**, not the Ubuntu one. If you do not administer
that machine, hand this chapter to whoever does — it is self-contained.

### 13.1 The six settings

The application reads its configuration from environment variables. A double
underscore (`__`) represents a level of nesting, so
`Audit__Elasticsearch__Password` sets the `Password` inside `Elasticsearch`
inside `Audit`.

Substitute the Ubuntu server's address for `10.20.30.40`:

```
ASPNETCORE_ENVIRONMENT             = Production
Observability__Tempo__OtlpEndpoint = http://10.20.30.40:4317
Observability__Loki__OtlpEndpoint  = http://10.20.30.40:3100/otlp/v1/logs
Audit__Elasticsearch__Uri          = http://10.20.30.40:9200
Audit__Elasticsearch__Username     = audit-writer
Audit__Elasticsearch__Password     = <the AUDIT_PASSWORD from your .env>
```

| Setting | Why |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | Must be `Production`. In `Development` the application seeds well-known API keys, serves its API explorer and returns full exception detail to callers |
| `Observability__Tempo__OtlpEndpoint` | Where traces are pushed. Port 4317, no path |
| `Observability__Loki__OtlpEndpoint` | Where logs are pushed. Port 3100 **with** the path `/otlp/v1/logs` — this one is easy to get wrong |
| `Audit__Elasticsearch__Uri` | Where audit records are shipped. Port 9200 |
| `Audit__Elasticsearch__Username` | The `AUDIT_USER` you set in `.env` |
| `Audit__Elasticsearch__Password` | The `AUDIT_PASSWORD` you set in `.env` |

There is deliberately **no metrics setting**. Metrics are pulled — Prometheus
already knows where to find the application. See Chapter 3 if that is surprising.

### 13.2 Where to put them on IIS

**IIS Manager → select the site → Configuration Editor → section
`system.webServer/aspNetCore` → `environmentVariables`**, or directly in the
site's `web.config`:

```xml
<aspNetCore processPath="dotnet" arguments=".\CleanArch.Api.dll" hostingModel="inprocess">
  <environmentVariables>
    <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />
    <environmentVariable name="Observability__Tempo__OtlpEndpoint" value="http://10.20.30.40:4317" />
    <environmentVariable name="Observability__Loki__OtlpEndpoint" value="http://10.20.30.40:3100/otlp/v1/logs" />
    <environmentVariable name="Audit__Elasticsearch__Uri" value="http://10.20.30.40:9200" />
    <environmentVariable name="Audit__Elasticsearch__Username" value="audit-writer" />
    <environmentVariable name="Audit__Elasticsearch__Password" value="..." />
  </environmentVariables>
</aspNetCore>
```

> Anyone who can read the site folder can read that password out of `web.config`,
> and if `web.config` is deployed from source control the password goes with it.
> For anything beyond a small internal deployment, set these in the application
> pool's environment or a secret store instead. The `audit-writer` account can
> only append to the audit indices, which limits the damage, but it is still a
> credential.

Then recycle the application pool so the new values are read:

```powershell
Restart-WebAppPool -Name CleanArchApiPool
```

### 13.3 The Windows firewall rule

Prometheus dials *in* to this server every 15 seconds. Allow it — and allow only
it:

```powershell
New-NetFirewallRule -DisplayName "Prometheus scrape (CleanArch.Api)" `
  -Direction Inbound -Protocol TCP -LocalPort 5235 `
  -RemoteAddress 10.20.30.40 -Action Allow
```

`-RemoteAddress` is the Ubuntu server. Restricting it matters: `/metrics`
enumerates every route in your API along with request rates and error counts,
which is a useful reconnaissance page for anyone who should not have it.

Outbound traffic to 4317, 3100 and 9200 is usually already allowed by default on
Windows Server. If yours blocks outbound, add rules for those three.

### 13.4 Confirm the application side

On the Windows server:

```powershell
(Invoke-WebRequest http://localhost:5235/health/ready).StatusCode   # expect 200
(Invoke-WebRequest http://localhost:5235/metrics).Content.Length    # expect a large number
```

Then generate some traffic — click around the application, or call a few
endpoints — so that there is something for the Ubuntu server to have received.

---

## 14. Step 9 — Verify all four signals

Check all four, individually. A healthy-looking Grafana proves nothing on its
own: three of these can be broken while the fourth works perfectly.

Wait two minutes after generating traffic before concluding anything is missing.

### 14.1 Metrics

Open `http://10.20.30.40:9090/targets` (Prometheus).

You should see two entries. `cleanarch-api` must show **UP** in green. If it
shows DOWN, the error text next to it is specific and useful:

| Error text | Means |
|---|---|
| `connection refused` | The application is not listening on that port, or the port in `prometheus.yml` is wrong |
| `context deadline exceeded` | Something is silently dropping the packets — almost always the Windows firewall |
| `no such host` | `API_HOST_IP` is not set, or `extra_hosts` did not apply |
| `401` or `403` | The application requires an API key for `/metrics` but none is configured |

### 14.2 Traces

Grafana → **Explore** (compass icon) → pick **Tempo** from the dropdown at the
top → **Search** tab → set the time range to "Last 15 minutes" → **Run query**.

You should get a list of traces. Click one. You get a flame-graph-like view of
where the time went inside that single request.

### 14.3 Logs

Grafana → **Explore** → pick **Loki** → paste this into the query box:

```
{service_name="CleanArch.Api"}
```

→ **Run query**. You should get log lines.

Useful variations once that works:

```
{service_name="CleanArch.Api"} |= "error"                      # containing "error"
{service_name="CleanArch.Api"} | json | level = "Error"        # structured field match
{service_name="CleanArch.Api"} |= "timeout" | line_format "{{.message}}"
```

Now try the cross-link: click a log line, expand it, and if it has a `TraceID`
field there is a link that jumps straight to that trace in Tempo. That link
working end-to-end means all three of your data sources and both cross-links are
correctly configured.

### 14.4 Audit

Kibana → **Discover**. If you have not created a data view yet, Chapter 15 does
that. Once created, select `cleanarch-audit-*` and you should see audit records.

### 14.5 The dashboard

Grafana → **Dashboards** → **CleanArch.Api - Service Overview**. Panels should be
drawing lines.

If Explore works but a dashboard panel is empty, the cause is nearly always a
metric-name mismatch — check that `metric_name_escaping_scheme: underscores` is
still in `prometheus.yml`, then `docker compose restart prometheus`.

### 14.6 Reading the results

| Metrics | Traces/logs | Diagnosis |
|---|---|---|
| UP | present | Everything works |
| UP | missing | The application cannot reach the Ubuntu server. Check the Ubuntu firewall, and that the `Observability__*` settings really took effect (a missed app-pool recycle is the usual cause) |
| DOWN | present | The reverse. Check the Windows inbound rule and the port in `prometheus.yml` |
| DOWN | missing | Nothing is connecting at all — check basic network reachability between the machines first (`ping`, then `nc -zv`) |

That table is the payoff for reading Chapter 3.

---

## 15. Step 10 — Set up Kibana for the audit trail

Elasticsearch stores the audit records in one index per day, named like
`cleanarch-audit-2026.08.26`. Kibana needs to be told to treat that whole family
as one searchable thing.

### 15.1 Confirm data has actually arrived

On the Ubuntu server:

```bash
curl -s -u elastic:YOUR_ELASTIC_PASSWORD \
  'http://10.20.30.40:9200/_cat/indices/cleanarch-audit-*?v'
```

You should get one line per day, with a document count. If it returns nothing,
the application has not shipped anything yet — do something in the application
that would be audited (create or change a record), wait a minute, and retry.
Nothing below will work until this returns rows.

### 15.2 Create the data view

1. Open Kibana at `http://10.20.30.40:5601` and log in as `elastic`.
2. Menu (top left) → **Stack Management**.
3. Under *Kibana*, choose **Data Views**.
4. **Create data view**.
5. Fill in:
   - **Name**: `Audit trail`
   - **Index pattern**: `cleanarch-audit-*` — the `*` is what makes one view
     cover every day. Kibana will show you which indices it matched; if it says
     no matches, go back to 15.1.
   - **Timestamp field**: pick `@timestamp` from the dropdown. This is what lets
     the time picker work.
6. **Save data view to Kibana**.

### 15.3 Search it

Menu → **Analytics → Discover**, then choose "Audit trail" in the dropdown at the
top left.

The time picker (top right) defaults to the last 15 minutes, which is the single
most common reason a new user concludes there is no data. Widen it to "Last 7
days" first.

Search syntax (KQL) is readable:

```
action : "StudentCreated"
user : "alice" and action : "Delete*"
not action : "Read*"
```

Click any row to see the whole record. Use **Add field as column** on the fields
you care about — usually the actor, the action, and the entity id — and the view
becomes an actual audit report rather than a wall of JSON.

### 15.4 Set a retention policy (recommended)

Audit indices accumulate forever unless told otherwise. Unlike the other three
stores, this one has no retention configured — deliberately, because "delete the
audit trail automatically" is a decision that should be yours and is often
governed by policy.

Decide on a period, then: Kibana → **Stack Management → Index Lifecycle
Policies → Create policy**, add a **Delete** phase at your chosen age (365 days
is a common starting point), then attach it to an index template matching
`cleanarch-audit-*`.

If your retention obligation is long, the cheaper option is to keep the nightly
backups from Chapter 16 and delete old indices — a compressed archive of old
audit records costs a fraction of keeping them live and searchable.

---

## 16. Step 11 — Keep it running

### 16.1 Back up what you cannot regenerate

Two of the five volumes hold data that cannot be recreated: `es-data` (the audit
trail) and `grafana-data` (users and any dashboard edits made in the UI). The
other three hold a rolling 30-day window of telemetry — worth having, rarely
worth restoring.

Create the backup script:

```bash
cd /opt/cleanarch/observability/prod
cat > backup.sh <<'EOF'
#!/usr/bin/env bash
# Back up the Docker volumes holding data that cannot be regenerated.
#
#   ./backup.sh [destination-dir]      (default: /var/backups/cleanarch)
#
# NOT backed up here: the application's own databases. They live on the Windows
# server and need their own job there. Backing up this VM is not backing up the
# application.
set -euo pipefail

DEST="${1:-/var/backups/cleanarch}"
STAMP="$(date +%Y%m%d-%H%M%S)"
PROJECT="cleanarch-prod"           # matches "name:" in docker-compose.yml
KEEP_DAYS="${KEEP_DAYS:-14}"

mkdir -p "$DEST"

for vol in es-data grafana-data; do
  full="${PROJECT}_${vol}"
  out="${DEST}/${vol}-${STAMP}.tar.gz"

  if ! docker volume inspect "$full" >/dev/null 2>&1; then
    echo "skip   $full (not present)"
    continue
  fi

  # Mount the volume into a throwaway container and tar it from inside. This
  # works regardless of where Docker keeps its volumes on this host.
  docker run --rm -v "${full}:/source:ro" -v "${DEST}:/backup" alpine:3.21 \
    tar czf "/backup/$(basename "$out")" -C /source .

  echo "saved  $full -> $out ($(du -h "$out" | cut -f1))"
done

find "$DEST" -name '*.tar.gz' -mtime "+${KEEP_DAYS}" -print -delete
echo "Done. ${KEEP_DAYS}-day retention applied to ${DEST}."
echo "OFFSITE: copy these elsewhere — right now they are on the same disk as the data."
EOF
chmod +x backup.sh
```

`chmod +x` makes the file executable. Run it once by hand to be sure it works:

```bash
sudo ./backup.sh
ls -lh /var/backups/cleanarch
```

Then schedule it nightly:

```bash
sudo crontab -e
```

Add this line at the bottom, save and exit:

```
0 3 * * * /opt/cleanarch/observability/prod/backup.sh >> /var/log/cleanarch-backup.log 2>&1
```

Reading the cron syntax: five fields — minute, hour, day-of-month, month,
day-of-week. `0 3 * * *` is "minute 0 of hour 3, every day". `>>` appends the
output to a log file; `2>&1` sends error output to the same place.

> **A backup you have never restored is a hypothesis.** Try the restore below
> into a throwaway machine at least once, before you need it.
>
> ```bash
> docker compose down
> docker volume rm cleanarch-prod_es-data
> docker volume create cleanarch-prod_es-data
> docker run --rm -v cleanarch-prod_es-data:/target -v /var/backups/cleanarch:/backup \
>   alpine:3.21 tar xzf /backup/es-data-YYYYMMDD-HHMMSS.tar.gz -C /target
> docker compose up -d
> ```

And copy the archives off this machine — rsync, rclone, S3, a network share, it
does not matter which. A backup sitting on the same disk as the original survives
exactly none of the failures you are actually worried about.

### 16.2 Watch the disk

```bash
df -h /                  # overall free space
docker system df         # how much Docker is using in total
docker system df -v      # broken down per volume — this is the useful one
```

Check `docker system df -v` after the first week and compare it against your
disk. Retention is set to 30 days everywhere, so week-one usage times four is a
fair estimate of the steady state. If that number is uncomfortable, reduce
retention now rather than at 95% full:

| Store | Where retention is set | Change it with |
|---|---|---|
| Prometheus | `PROMETHEUS_RETENTION_TIME` / `_SIZE` in `.env` | `docker compose up -d` |
| Tempo | `block_retention` in `tempo.yaml` | `docker compose restart tempo` |
| Loki | `retention_period` in `loki.yaml` | `docker compose restart loki` |
| Elasticsearch | Not set — see 15.4 | Kibana index lifecycle policy |

### 16.3 Updating

```bash
cd /opt/cleanarch/observability/prod
nano .env                     # bump the version numbers you want to change
docker compose pull           # download the new images
docker compose up -d          # recreate only the containers that changed
```

Compose is smart enough to leave untouched services alone. Change one version at
a time, and read the release notes for Elasticsearch major versions in particular
— its data format changes between majors, and its version must stay compatible
with the client library the application uses (currently 9.x).

Update the Ubuntu server itself on your normal schedule:

```bash
sudo apt update && sudo apt upgrade -y
```

If Docker itself is upgraded, containers restart. `restart: unless-stopped`
brings them back on their own.

### 16.4 Restarting, stopping, and the one dangerous flag

```bash
docker compose restart prometheus   # one service
docker compose restart              # all of them
docker compose down                 # stop and remove the containers — DATA IS KEPT
docker compose up -d                # bring them back
```

`docker compose down` is safe. It removes containers, not volumes.

**`docker compose down -v` deletes every volume**, which means the audit trail,
the dashboards, and all telemetry. There is no confirmation prompt and no undo.
It is a useful command on a development machine and has no business being typed
on this one.

### 16.5 A five-minute monthly check

1. `docker compose ps` — everything `Up` and `(healthy)`.
2. `df -h /` — disk under 80%.
3. Grafana → the dashboard — panels drawing.
4. Prometheus `/targets` — `cleanarch-api` UP.
5. `ls -lh /var/backups/cleanarch` — last night's file exists and is not 0 bytes.

---

## 17. Troubleshooting

Start here, always:

```bash
cd /opt/cleanarch/observability/prod
docker compose ps
docker compose logs --tail 50 <the-service-that-looks-wrong>
```

### Startup problems

| Symptom | Cause | Fix |
|---|---|---|
| `variable is not set` on `up` | A value is missing from `.env` — the message names it | Add it, then `docker compose up -d` |
| `elasticsearch` exits immediately, log mentions `max virtual memory areas` | `vm.max_map_count` too low | Redo section 6.4, check with `sysctl vm.max_map_count` |
| `elasticsearch` is killed after a minute | `ES_HEAP` bigger than the machine can give | Lower `ES_HEAP` in `.env`, or add RAM |
| `es-setup` exits non-zero | `ELASTIC_PASSWORD` does not match an existing `es-data` volume. It only bootstraps on a *fresh* volume | See "changing the elastic password" below |
| `kibana` never becomes healthy | `KIBANA_SYSTEM_PASSWORD` changed without re-running `es-setup`, or `KIBANA_ENCRYPTION_KEY` is under 32 characters | Fix `.env`, then `docker compose up -d` |
| Kibana page says "Kibana server is not ready yet" | It is still starting | Wait two minutes and reload before investigating |
| `port is already allocated` | Something else on the server uses that port | `sudo ss -tlnp \| grep <port>` to find it |
| `permission denied` from the docker command | Your user is not in the `docker` group, or you have not logged out and in since 7.7 | `groups` should list `docker` |

### No data arriving

| Symptom | Cause |
|---|---|
| Prometheus target DOWN, `connection refused` | Wrong port in `prometheus.yml`, or the app is not running |
| Prometheus target DOWN, `context deadline exceeded` | Windows firewall is dropping the scrape |
| Prometheus target DOWN, `no such host` | `API_HOST_IP` missing from `.env`, or `extra_hosts` not applied — `docker compose up -d` |
| Metrics fine, no traces or logs | The app cannot reach this server: Ubuntu firewall, or the `Observability__*` settings did not take effect. Recycle the app pool |
| Loki returns 400 on push | `allow_structured_metadata: true` missing from `loki.yaml` |
| No audit records | Wrong `AUDIT_USER`/`AUDIT_PASSWORD` on the Windows server; or `es-setup` never ran successfully |
| Dashboard panels empty, but Explore works | Metric-name mismatch — `metric_name_escaping_scheme: underscores` must be in `prometheus.yml`; then restart Prometheus |
| Kibana Discover shows nothing | The time range. Widen it to "Last 7 days" before anything else |

### Testing connectivity between the two machines

From the Windows server, checking it can reach Ubuntu:

```powershell
Test-NetConnection 10.20.30.40 -Port 4317
Test-NetConnection 10.20.30.40 -Port 3100
Test-NetConnection 10.20.30.40 -Port 9200
```

From the Ubuntu server, checking it can reach Windows:

```bash
nc -zv 10.20.30.50 5235      # sudo apt install -y netcat-openbsd if nc is missing
```

`TcpTestSucceeded : True` and `succeeded!` respectively. Anything else is a
firewall or a routing problem, and no amount of application configuration will
work around it.

### Changing the elastic password after the first start

`ELASTIC_PASSWORD` in `.env` only has an effect on the very first start with an
empty `es-data` volume. Afterwards, use the tool inside the container:

```bash
docker compose exec elasticsearch \
  bin/elasticsearch-reset-password -u elastic -i
```

`-i` prompts you for the new password. Then put the same value into `.env` — the
health check and `es-setup` both use it — and run `docker compose up -d`.

### Getting a shell inside a container

Occasionally useful for looking around:

```bash
docker compose exec grafana sh          # most images have sh
docker compose exec elasticsearch bash  # this one has bash
```

Type `exit` to leave. Remember that anything you change in there is lost when the
container is recreated — which is exactly why the config files are bind-mounted
from outside.

### The last resort

If a service is thoroughly confused and you are willing to lose *its* data:

```bash
docker compose stop loki
docker compose rm -f loki
docker volume rm cleanarch-prod_loki-data
docker compose up -d loki
```

This is reasonable for Loki, Tempo or Prometheus — you lose telemetry history and
nothing else. **Never do it for `es-data`** without a verified backup: that is the
audit trail.

---

## 18. Command cheat sheet

Everything assumes `cd /opt/cleanarch/observability/prod` first.

```bash
# ── Daily ────────────────────────────────────────────────────────────────────
docker compose ps                     # what is running and is it healthy
docker compose logs --tail 50 loki    # last 50 lines from one service
docker compose logs -f grafana        # follow live (Ctrl+C to stop watching)

# ── Changing something ───────────────────────────────────────────────────────
nano loki.yaml                        # edit a config file
docker compose restart loki           # apply it
nano .env                             # edit a version, address or password
docker compose up -d                  # apply it (recreates what changed)
docker compose config --quiet         # validate before applying

# ── Lifecycle ────────────────────────────────────────────────────────────────
docker compose up -d                  # start everything
docker compose down                   # stop everything (data is kept)
docker compose pull                   # download newer images
docker compose restart                # restart everything

# ── Inspecting ───────────────────────────────────────────────────────────────
docker compose exec grafana sh        # shell inside a container
docker system df -v                   # disk used, per volume
docker volume ls                      # list volumes
df -h /                               # server disk space
free -h                               # server memory

# ── Backup ───────────────────────────────────────────────────────────────────
./backup.sh                           # run it now
ls -lh /var/backups/cleanarch         # what has been kept
```

### The URLs

| What | URL |
|---|---|
| Grafana — dashboards, traces, logs | `http://10.20.30.40:3000` |
| Kibana — audit trail | `http://10.20.30.40:5601` |
| Prometheus — scrape health | `http://10.20.30.40:9090/targets` |

---

## 19. Glossary

| Term | Meaning |
|---|---|
| **Bind mount** | A file or folder from the real server, made visible inside a container. Used for config |
| **Compose** | Docker's tool for describing several containers in one `docker-compose.yml` file |
| **Container** | One running instance of an image |
| **Data view** | Kibana's name for "this family of indices, treated as one searchable set" |
| **ELK** | Elasticsearch + Logstash + Kibana. Here it is really just Elasticsearch + Kibana |
| **Health check** | A command Docker runs inside a container to decide whether it is genuinely working |
| **Heredoc** | The `cat > file <<'EOF' ... EOF` shell trick used throughout this guide to create files |
| **Image** | A frozen, ready-to-run copy of a program plus its dependencies |
| **Index** | Elasticsearch's word for a table. This stack creates one per day |
| **KQL** | Kibana Query Language — the search box syntax in Discover |
| **Label** | A key/value tag attached to a metric or log line, e.g. `http_route="/students"` |
| **LogQL** | Loki's query language. Always starts with a `{label="value"}` selector |
| **Named volume** | Docker-managed storage on the real disk that outlives its container. Used for data |
| **OTLP** | OpenTelemetry Protocol — the standard format the application uses to push traces and logs |
| **p95** | "95% of requests were faster than this." The standard way to describe latency, because averages hide the slow tail |
| **Provisioning** | Grafana's term for configuring it from files at startup rather than by clicking |
| **PromQL** | Prometheus's query language |
| **Publish (a port)** | Making a container's port reachable from outside Docker, via a `ports:` entry |
| **Pull / push** | Who initiates the connection. Metrics are pulled; traces, logs and audit are pushed |
| **Retention** | How long data is kept before automatic deletion |
| **Scrape** | One round of Prometheus fetching `/metrics` from a target |
| **Signal** | One of the kinds of telemetry: metrics, traces, logs (and here, audit) |
| **Span** | One step inside a trace — a database call, an HTTP call |
| **Trace** | The full nested timeline of one request |
| **`ufw`** | Ubuntu's firewall front end |
| **YAML** | The indentation-sensitive file format used by compose and the config files |

---

## Appendix A — Everything you created, in one list

```
/opt/cleanarch/observability/
├── prod/
│   ├── .env                                   Chapter 9.1  (chmod 600)
│   ├── tempo.yaml                             Chapter 9.2
│   ├── loki.yaml                              Chapter 9.3
│   ├── prometheus.yml                         Chapter 9.4
│   ├── docker-compose.yml                     Chapter 10.1
│   └── backup.sh                              Chapter 16.1 (chmod +x)
└── grafana/
    ├── provisioning/
    │   ├── datasources/datasources.yaml       Chapter 9.5
    │   └── dashboards/dashboards.yaml         Chapter 9.6
    └── dashboards/cleanarch-api.json          Chapter 9.7
```

Plus, outside that tree:

- `/etc/sysctl.d/99-elasticsearch.conf` — the kernel setting (Chapter 6.4)
- `/etc/apt/keyrings/docker.asc` and `/etc/apt/sources.list.d/docker.list` — the
  Docker repository (Chapter 7)
- `ufw` rules (Chapter 11)
- A root crontab entry (Chapter 16.1)

**Back up the whole `/opt/cleanarch` tree somewhere.** It is small, it is text,
and with it plus this document you can rebuild this server from a blank Ubuntu
install in half an hour.

---

## Appendix B — A sensible order to do this in

If you are doing this for the first time and want to know how long to book:

| | Task | Roughly |
|---|---|---|
| 1 | Read Chapters 1–4 | 20 min |
| 2 | Collect the four facts, get server access | varies — often the longest part |
| 3 | Chapters 6–7: prepare Ubuntu, install Docker | 20 min |
| 4 | Chapters 8–10: create every file | 40 min |
| 5 | Chapters 11–12: firewall, first start | 20 min |
| 6 | Chapter 13: the Windows side | 15 min, plus finding the right person |
| 7 | Chapters 14–15: verify, set up Kibana | 30 min |
| 8 | Chapter 16: backups and the cron job | 20 min |

Do not skip step 8 because the stack already appears to work. The audit trail
becomes irreplaceable the moment it contains something you would be asked about.
