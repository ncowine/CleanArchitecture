# Getting Started — from zero

This is the **on-ramp**. It assumes **no prior experience**: it explains the words, installs the tools, gets this project running on your machine, and then shows how to scaffold *your own* application with the same architecture.

When you're comfortable here, read the deeper companion: **[Clean Architecture — A Beginner's Guide](./clean-architecture-guide.md)**, which explains *how the code is organized and how to add features*.

> New to all of this? Read Parts A–D in order. Already a developer? Skim Part B's glossary, then jump to [Part F: Build your own app](#part-f-build-your-own-app-from-scratch).

---

## Contents

- [Part A: The ideas in plain language](#part-a-the-ideas-in-plain-language)
- [Part B: A plain-English glossary](#part-b-a-plain-english-glossary)
- [Part C: Install the tools (one time)](#part-c-install-the-tools-one-time)
- [Part D: Run this project (about 5 minutes)](#part-d-run-this-project-about-5-minutes)
- [Part E: Everyday commands (cheat sheet)](#part-e-everyday-commands-cheat-sheet)
- [Part F: Build your own app from scratch](#part-f-build-your-own-app-from-scratch)
- [Part G: Troubleshooting & FAQ](#part-g-troubleshooting--faq)

---

## Part A: The ideas in plain language

**What is an API?** Imagine a restaurant. You (a *client* — a phone app, a website, another program) don't walk into the kitchen; you talk to a *waiter*. You make a **request** ("I'd like the pasta"), and you get a **response** (the pasta, or "sorry, we're out"). An **API** (Application Programming Interface) is that waiter: a well-defined menu of things a program can ask a server to do, over the internet. This project *is* such a waiter — for a college's students and library.

**What is a "server" and a "client"?** The **server** is the always-on program that holds the data and does the work (this project, running on a computer). A **client** is anything that talks to it. The same server can serve a website, a mobile app, and the desktop app that's in this repo.

**What is "Clean Architecture"?** Think of a well-organized house. The **rules of the household** (who's allowed to do what) don't depend on the brand of your fridge or the colour of the walls — you can swap the fridge without rewriting the house rules. Clean architecture keeps the **business rules** (e.g. "a student's email must contain an @", "you can't borrow a book that's already out") in the centre, *independent* of the database, the web framework, or the screen. The fast-changing, replaceable things (database, web server, UI) sit at the **edges**. The single guiding rule: **the centre never depends on the edges.** That makes the valuable part — the rules — easy to test and hard to break.

**What is a "module" and "database-per-module"?** This app is split into two self-contained parts — **Students** and **Library** — each with its **own** database. They don't reach into each other's data; they talk through small, published "menus" (contracts) and messages. This mirrors a very common real-world situation: a new system that owns its own data but must cooperate with an older one.

You don't need to fully grasp these yet — running the project (Part D) makes them concrete.

---

## Part B: A plain-English glossary

You'll meet these words throughout. Skim now; refer back later.

| Word | Plain meaning |
|------|---------------|
| **API** | A program's "menu" — the set of requests other programs can make to it. |
| **Endpoint** | One item on that menu — a single URL + action, e.g. `POST /students` ("create a student"). |
| **HTTP** | The language clients and servers speak over the web. Verbs: `GET` (read), `POST` (create/do), etc. |
| **REST / JSON** | A common *style* of API (REST) that sends data as **JSON** — human-readable text like `{ "name": "Ada" }`. |
| **Request / Response** | What you send, and what you get back. |
| **Status code** | A number in the response: `200/201` = OK, `400` = you sent something invalid, `401` = not authorized, `404` = not found. |
| **SDK** | "Software Development Kit" — the toolbox that lets you build and run programs in a language. Here: the **.NET SDK**. |
| **IDE / editor** | The program you write code in (Visual Studio, VS Code, Rider). |
| **NuGet package** | A reusable library someone else published, that your project pulls in. |
| **Build / compile** | Turning source code into a runnable program. |
| **Run** | Starting the built program (here, the API server). |
| **Solution / project** | A *project* is one buildable unit (a folder of code). A *solution* groups several projects. This repo has ~18 projects in one solution. |
| **Namespace / class / record / interface** | Ways C# organizes code. A **class**/**record** is a thing with data and behaviour; an **interface** is a *promise* of behaviour ("anything that can save a student") without saying *how*. |
| **Dependency Injection (DI)** | Instead of a class creating the things it needs, they're *handed to it*. This is how the "centre" can use an interface and have the real database implementation supplied at runtime. |
| **Repository (two meanings!)** | (1) A *git repository* = this whole project folder under version control. (2) The *repository pattern* = an interface like `IStudentRepository` that hides the database. Context tells them apart. |
| **DbContext / EF Core** | Entity Framework Core is the library that maps C# objects to database tables. A `DbContext` is your handle to one database. |
| **Migration** | A versioned script that updates the database shape to match your code (e.g. "add a Courses table"). |
| **DTO** | "Data Transfer Object" — a simple shape used to send data in/out, separate from the internal model. |
| **async / await** | C# keywords for "do this without blocking while we wait" (e.g. for the database). You'll see them everywhere; you can mostly read past them. |
| **Unit test** | A small automated check that one piece of code behaves correctly. This repo has ~150 of them. |

A few project-specific terms (mediator, command/query, outbox, **saga**, idempotency) are explained in the [architecture guide](./clean-architecture-guide.md) — don't worry about them yet.

---

## Part C: Install the tools (one time)

You need three things. Install them once.

1. **The .NET 10 SDK** — the toolbox to build and run the project.
   Download: <https://dotnet.microsoft.com/download/dotnet/10.0> (pick the **SDK**, not just the Runtime, for your OS).
2. **An editor** (choose one):
   - **Visual Studio 2022/2026** (Windows/Mac) — the most beginner-friendly; press the green ▶ to run.
   - **VS Code** (any OS) + the **C# Dev Kit** extension — lightweight and free.
   - **JetBrains Rider** (any OS) — powerful, paid.
3. **Git** — to download the project and track changes. <https://git-scm.com/downloads>

**Verify the install.** Open a terminal (Windows: "Terminal" or "PowerShell"; macOS/Linux: "Terminal") and run:

```bash
dotnet --version    # should print 10.x.x
git --version       # should print a version
```

If both print a version, you're ready.

---

## Part D: Run this project (about 5 minutes)

**1. Download the project** (clone it) and enter the folder:

```bash
git clone https://github.com/ncowine/CleanArchitecture.git
cd CleanArchitecture
```

**2. (One time, for HTTPS)** Trust the local development certificate so your browser doesn't warn:

```bash
dotnet dev-certs https --trust
```

**3. Start the server:**

```bash
dotnet run --project src/Api/CleanArch.Api
```

The first run restores packages and **creates the two SQLite databases automatically** (`students.db`, `library.db`) by applying the migrations. Watch the console for a line like:

```
Now listening on: http://localhost:5235
```

**4. Open the interactive API explorer (Swagger).** Take the URL from that line and add `/swagger`. Typically:
- from the terminal: **http://localhost:5235/swagger**
- from Visual Studio's ▶: **https://localhost:7214/swagger**

You'll see every endpoint, **grouped by area** (Students, Courses, Sections, Transcripts, Billing, Library — Catalog/Loans/Reservations, …). Click any one to read what it does and try it.

**5. Make your first call — create a student.** Most *read* endpoints are open; *write* endpoints need a key.

   a. Click the green **Authorize** button (top right).
   b. In the **ApiKey** box, paste a built-in development key: `dev-api-key-reporting` — then **Authorize**, **Close**.
   c. Expand **`POST /students`** → **Try it out** → edit the example body, e.g.:
      ```json
      {
        "firstName": "Ada",
        "lastName": "Lovelace",
        "email": "ada@uni.edu",
        "dateOfBirth": "1990-12-10",
        "enrolledOn": "2024-09-01"
      }
      ```
   d. **Execute**. You should get **`201 Created`** and an `id` back. (Try an invalid email like `"ada"` and you'll get a **`400`** with the reason — that's the *domain rule* rejecting it.)

**6. Read it back.** Copy the `id`, expand **`GET /students/{studentId}`**, paste the id, **Execute** → you'll see the student. That row now lives in `students.db` in the project folder.

That's the full loop: a request came in, a business rule ran, data was saved, and you read it back. 🎉

> **Want to stop the server?** Press `Ctrl + C` in the terminal.

---

## Part E: Everyday commands (cheat sheet)

Run these from the repo root.

```bash
# Build everything (compile, check for errors)
dotnet build

# Run the API server
dotnet run --project src/Api/CleanArch.Api

# Run all the automated tests
dotnet test tests/CleanArch.UnitTests/CleanArch.UnitTests.csproj

# Add a database migration after changing the model (note --context: there are two databases)
dotnet ef migrations add <AName> \
  --project src/Modules/Students/Students.Infrastructure \
  --startup-project src/Api/CleanArch.Api \
  --context StudentsDbContext \
  --output-dir Persistence/Migrations
```

(`dotnet ef` is a one-time install if you don't have it: `dotnet tool install --global dotnet-ef`.)

---

## Part F: Build your own app from scratch

Here's how to create a **new** application laid out like this one. We'll scaffold a tiny "Orders" service. The goal is the *shape*; you then fill it in using the patterns in the [architecture guide](./clean-architecture-guide.md).

### F.1 — Create the solution and the layer projects

```bash
mkdir MyApp && cd MyApp
dotnet new sln -n MyApp

# The inner layers (plain class libraries — no framework dependencies)
dotnet new classlib -o src/Orders.Domain
dotnet new classlib -o src/Orders.Contracts
dotnet new classlib -o src/Orders.Application

# The outer layers
dotnet new classlib -o src/Orders.Infrastructure   # EF Core lives here
dotnet new classlib -o src/Orders.Presentation     # HTTP endpoints
dotnet new web      -o src/Api                      # the host that runs the server

# A test project
dotnet new xunit -o tests/Orders.UnitTests

# Add them all to the solution
dotnet sln add (Get-ChildItem -Recurse *.csproj)    # PowerShell
# or on macOS/Linux:  dotnet sln add $(find . -name "*.csproj")
```

### F.2 — Wire the references **pointing inward** (this is the whole game)

```bash
# Application depends only on Domain
dotnet add src/Orders.Application reference src/Orders.Domain

# Infrastructure and Presentation depend on Application
dotnet add src/Orders.Infrastructure reference src/Orders.Application
dotnet add src/Orders.Presentation   reference src/Orders.Application

# The host wires everything together
dotnet add src/Api reference src/Orders.Infrastructure src/Orders.Presentation

# Tests can see the inner layers
dotnet add tests/Orders.UnitTests reference src/Orders.Application src/Orders.Domain
```

> The single rule made concrete: **`Orders.Domain` has no references at all**, and nothing inner ever references EF Core or ASP.NET. If you ever need to add such a reference to Domain/Application, that's the signal to introduce an *interface* instead.

### F.3 — Add the libraries the outer layers need

```bash
# Infrastructure: a database provider (SQLite is zero-setup to start)
dotnet add src/Orders.Infrastructure package Microsoft.EntityFrameworkCore.Sqlite
dotnet add src/Orders.Infrastructure package Microsoft.EntityFrameworkCore.Design

# Application: validation (optional but recommended)
dotnet add src/Orders.Application package FluentValidation
```

### F.4 — The cross-cutting "building blocks"

This repo includes hand-rolled, reusable infrastructure under `src/BuildingBlocks*` — a small **mediator** (so endpoints send `Command`/`Query` objects to handlers), **pipeline behaviors** (logging, validation, transactions), the **outbox** (for cross-module messaging/sagas), and **pagination**. For your own app you have two honest options:

- **Copy them** from this repo (`src/BuildingBlocks/`, `src/BuildingBlocks.Outbox/`) — they're self-contained and dependency-light. This is the fastest way to get the same patterns.
- **Use libraries** for the generic parts — e.g. a mediator library for request/handler dispatch — and only hand-roll what's specific to you. *(Note: this repo deliberately avoids one popular mediator library for licensing reasons; check the license before adopting one.)*

You don't need all of it on day one. A first version can be just: Domain + Application (with handlers called directly) + Infrastructure (EF) + a host that maps endpoints to handlers.

### F.5 — Add your first feature

Now follow the **[step-by-step feature recipe](./clean-architecture-guide.md#part-3-add-a-feature-step-by-step)**: model an aggregate in `Orders.Domain`, write a `Command`/`Handler` in `Orders.Application` against an `IOrderRepository` interface, implement that interface with EF in `Orders.Infrastructure`, expose an endpoint in `Orders.Presentation`, register everything in the host, add a migration, and write a test with an in-memory fake. The `Students` module in *this* repo is your worked reference for every one of those steps.

---

## Part G: Troubleshooting & FAQ

| Symptom | Cause & fix |
|---------|-------------|
| **Build fails with "file is locked" / "being used by another process"** | The API is still **running** (you can't overwrite a running program). Stop it (`Ctrl + C` in its terminal, or stop debugging in your IDE), then build. |
| **`dotnet ef migrations add` says "More than one DbContext was found"** | There are two databases. Add `--context StudentsDbContext` (or `LibraryDbContext`) to the command. |
| **`dotnet ef` : command not found** | Install the tool once: `dotnet tool install --global dotnet-ef`. |
| **Browser warns the site isn't secure (HTTPS)** | Trust the dev certificate once: `dotnet dev-certs https --trust`. Or just use the `http://localhost:5235` URL. |
| **"Address already in use" / port busy** | Another copy is running, or another app uses the port. Stop the other process, or change the port in `src/Api/CleanArch.Api/Properties/launchSettings.json`. |
| **A write endpoint returns `401 Unauthorized`** | You didn't authorize. Click **Authorize** in Swagger and enter the dev key `dev-api-key-reporting` (the `X-Api-Key` scheme). Reads don't need it. |
| **Build fails on a warning** | This repo treats warnings as errors (a quality gate). Read the warning — it's pointing at a real issue — and fix it rather than suppressing it. |
| **Build fails with `OutOfMemoryException` (low-RAM machine)** | The background compiler bloated: run `dotnet build-server shutdown`, then build again (optionally with `-m:1` to limit parallelism). |
| **I changed code but the database didn't change** | EF doesn't change the DB automatically from code edits — you must **add a migration** (Part E) and restart (migrations apply on startup in Development). |

---

## Where to go next

You can now run the app and make changes. To understand *how the code is organized* and *how to build features the clean-architecture way* — including the mediator pipeline and the full **saga** tutorial — read **[Clean Architecture — A Beginner's Guide](./clean-architecture-guide.md)**.
