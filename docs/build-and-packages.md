# Build props & package management (and the "multiple NuGet sources" error)

A practical guide to the `Directory.*.props` files and `nuget.config` — what each one does, how they
interact, and how to fix the central-package-management vs. multiple-feeds error (NU1507). The examples are
this repo's real files, so you can compare them against another setup (e.g. an office repo with a private
feed).

## TL;DR mental model

| File | Answers the question | Scope rule |
|---|---|---|
| `Directory.Build.props` | **How** do projects build? (TFM, nullable, analyzers, warnings) | MSBuild imports the **nearest one up the tree** — does *not* auto-merge multiple |
| `Directory.Packages.props` | **Which version** of each package? (Central Package Management) | Same nearest-wins import; versions move out of the `.csproj` |
| `nuget.config` | **Where** do packages come from? (feeds + source mapping) | NuGet **merges** all of them up the tree *and* the machine-global config — use `<clear />` |

The "multiple NuGet sources" error is the collision of the middle and right columns: **Central Package
Management + more than one feed** forces you to map packages to feeds. [Jump to the fix](#the-multiple-nuget-sources-error-nu1507).

---

## 1. `Directory.Build.props` — shared build settings

MSBuild, before it reads any `.csproj`, walks **up** the folder tree and imports the **first**
`Directory.Build.props` it finds. So common settings live in one place instead of being copy-pasted into
every project. This repo's root file:

```xml
<!-- ./Directory.Build.props -->
<Project>
  <PropertyGroup Label="Language">
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <PropertyGroup Label="CodeQuality">
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <AnalysisLevel>latest</AnalysisLevel>
    <AnalysisMode>Recommended</AnalysisMode>
  </PropertyGroup>

  <PropertyGroup Label="Reproducibility">
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

Every project under the repo root inherits all of this — none of these settings appear in the individual
`.csproj` files.

### Nearest-wins, does NOT merge

The single biggest gotcha: MSBuild stops at the **first** `Directory.Build.props` it finds going up and
ignores any others higher up. It does **not** combine them. This repo relies on that on purpose —
`clients/Directory.Build.props` shadows the root one so desktop client apps escape the API's strict
warnings-as-errors:

```xml
<!-- ./clients/Directory.Build.props -->
<Project>
  <!-- This is the nearest Directory.Build.props for everything under clients/,
       so the root one is NOT imported — client apps get relaxed analyzer rules. -->
  <PropertyGroup>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
  </PropertyGroup>
</Project>
```

If you instead want the inner file to **extend** the outer one rather than replace it, import the parent
explicitly:

```xml
<Project>
  <Import Project="$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))" />
  <!-- inner-specific settings here -->
</Project>
```

> There is also `Directory.Build.targets`, imported at the **end** of the build instead of the start. Use
> `.props` for defaults you might override per-project, `.targets` for things that must win.

---

## 2. `Directory.Packages.props` — Central Package Management (CPM)

CPM moves every package **version** into one file so all projects agree. Opt in with
`ManagePackageVersionsCentrally`, then declare versions with `<PackageVersion>`:

```xml
<!-- ./Directory.Packages.props (excerpt) -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>

  <ItemGroup Label="EntityFrameworkCore">
    <PackageVersion Include="Microsoft.EntityFrameworkCore" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageVersion Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.8" />
  </ItemGroup>
  <!-- ...grouped by area: Api, Authentication, Validation, Caching, Testing, Observability... -->
</Project>
```

Then in each `.csproj` you reference packages **with no `Version` attribute** — the version comes centrally:

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />   <!-- version omitted on purpose -->
```

This is why the project files in this repo list packages with no versions. Two notes on the settings above:

- **`CentralPackageTransitivePinningEnabled`** also pins the versions of *transitive* dependencies (the
  packages your packages pull in), so a dependency can't silently float to a higher version.
- A project can still override one package locally with
  `<PackageReference Include="X" VersionOverride="1.2.3" />` when it genuinely needs a different version.

Like `Directory.Build.props`, CPM uses nearest-wins: `clients/Directory.Packages.props` is a **separate**
central list for the desktop clients (Prism, test SDK), independent of the root one.

---

## 3. `nuget.config` — where packages come from

This file lists the **feeds** (package sources). Unlike the props files, NuGet **merges** every
`nuget.config` it finds up the directory tree **and** the machine-global one
(`%APPDATA%\NuGet\NuGet.Config` on Windows, `~/.nuget/NuGet/NuGet.Config` elsewhere).

> This repo intentionally has **no** `nuget.config`, so it uses only the default nuget.org feed and never
> hits the multiple-sources error. The section below is for setups (like an office repo) that add a private
> feed.

---

## The "multiple NuGet sources" error (NU1507)

### Why it happens

Turning on CPM activates a stricter rule: if **more than one feed** is configured, NuGet refuses to guess
which feed a package should come from. You get:

```
NU1507: There are 2 package sources defined in your configuration. When using central package
management, please map your package sources with package source mapping.
```

This is a **security feature**, not just a nag. With a private feed in the mix, a package name on nuget.org
could be a malicious lookalike of an internal package (a *dependency-confusion* attack). Source mapping
forces you to declare which feed each package is allowed to come from.

### The hidden cause: inherited sources

You often see this even when your repo's `nuget.config` lists only one source — because NuGet **merged in**
a feed from the machine-global config or a parent folder. Always start the repo file with `<clear />` to
reset the inherited list:

```xml
<packageSources>
  <clear />   <!-- ignore machine-global + parent sources; only what we list below counts -->
  ...
</packageSources>
```

### The fix: package source mapping

Put a `nuget.config` next to the `.sln` declaring sources **and** mapping package-name patterns to them:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="office-internal" value="https://pkgs.dev.azure.com/yourorg/_packaging/Feed/nuget/v3/index.json" />
  </packageSources>

  <packageSourceMapping>
    <!-- default: everything resolves from nuget.org -->
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <!-- exception: company packages MUST come from the internal feed -->
    <packageSource key="office-internal">
      <package pattern="YourCompany.*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

Now `YourCompany.*` resolves only from the internal feed, everything else from nuget.org, and NU1507 is
satisfied because you've answered its question. Patterns support a trailing `*` wildcard; the **longest
matching prefix wins**, so `YourCompany.*` beats `*` for your packages.

### Authenticated feeds

Private feeds (Azure Artifacts, Artifactory, GitHub Packages) need credentials, and an auth failure can
masquerade as a resolution error:

- **Locally:** install the [Azure Artifacts Credential Provider](https://github.com/microsoft/artifacts-credprovider),
  or run `dotnet nuget add source <url> --name office-internal --username <user> --password <PAT>`.
- **CI/build server:** use the pipeline's auth step (e.g. Azure DevOps `NuGetAuthenticate@1`) to inject a
  token — don't commit PATs to `nuget.config`.

### Escape hatches (use sparingly)

- **Single feed only:** `<clear />` + just nuget.org. Fine if you don't actually consume private packages.
- **Suppress the warning:** `<NoWarn>$(NoWarn);NU1507</NoWarn>` in `Directory.Build.props`. This silences
  the prompt but gives up dependency-confusion protection — not recommended when a private feed is present.

---

## Troubleshooting cheat sheet

| Symptom | Likely cause | Fix |
|---|---|---|
| `NU1507: multiple package sources … please map` | CPM on + >1 feed, no mapping | Add `packageSourceMapping` (above) |
| "I only configured one source but still get NU1507" | A feed merged in from machine-global / parent config | Add `<clear />` as the first line of `<packageSources>` |
| `NU1008: Packages should not have version on PackageReference` | CPM on, but a `.csproj` still has `Version="…"` | Remove the `Version` attribute; add `<PackageVersion>` centrally |
| `NU1010 / NU1009: version not specified / no PackageVersion` | Package referenced but missing from `Directory.Packages.props` | Add a `<PackageVersion Include="…" Version="…" />` |
| A `Directory.Build.props` setting "isn't applying" | A nearer `Directory.Build.props` shadows it (no auto-merge) | `Import` the parent explicitly, or move the setting down |
| One project needs a different version | CPM pins everyone to one version | `VersionOverride="…"` on that one `PackageReference` |
| 401 / auth errors resolving packages | Private feed needs credentials | Credential provider locally; `NuGetAuthenticate` in CI |

## See also

- This repo's live examples: [`Directory.Build.props`](../Directory.Build.props),
  [`Directory.Packages.props`](../Directory.Packages.props), and the `clients/` variants.
- MS docs: [Central Package Management](https://learn.microsoft.com/nuget/consume-packages/central-package-management),
  [Package source mapping](https://learn.microsoft.com/nuget/consume-packages/package-source-mapping),
  [Customize your build](https://learn.microsoft.com/visualstudio/msbuild/customize-by-directory).
