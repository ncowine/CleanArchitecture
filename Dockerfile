# Dockerfile for CleanArch.Api — used by observability/docker/docker-compose.yml.
#
# Two-stage build:
#   1. "build"  — the big .NET SDK image compiles + publishes the app.
#   2. "final"  — the small ASP.NET runtime image runs the published output. The SDK never ships.
#
# Build context is the REPO ROOT (not this file's folder): the API references ~20 sibling projects
# and the root Directory.*.props (central NuGet versions), so restore needs the whole tree.
# .dockerignore trims the heavy, irrelevant bits (bin/obj, .git, node_modules, the ELK stack).

# ---- Stage 1: build ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy everything the build needs (see .dockerignore for what's excluded), then restore + publish
# just the API project. `dotnet publish` pulls in all referenced projects transitively.
COPY . .
RUN dotnet restore src/Api/CleanArch.Api/CleanArch.Api.csproj
RUN dotnet publish src/Api/CleanArch.Api/CleanArch.Api.csproj \
    -c Release -o /app --no-restore

# ---- Stage 2: runtime -------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl is here solely so docker-compose's healthcheck can probe the app. Nothing else uses it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# The SQLite database files live here; docker-compose mounts a named volume over this path so the
# data survives `docker compose down` / container rebuilds.
#
# Ownership matters: the container drops to the non-root "app" user below, and a FRESH named volume
# inherits the ownership of the directory it covers — so chowning here is what lets the app write its
# databases. An EXISTING volume from an earlier root-owned run keeps root ownership and the app will
# fail to open the files; fix that one volume once with:
#   docker run --rm -v cleanarch-observability_api-data:/data alpine chown -R app:app /data
RUN mkdir -p /app/data && chown -R app:app /app

# Bring over ONLY the published app from stage 1 (no source, no SDK).
COPY --from=build --chown=app:app /app .

# Drop root. The .NET runtime images ship a non-root "app" user for exactly this; a bug in the app is
# then not root inside the container, which is the difference between a contained problem and a host
# one when combined with no-new-privileges and a read-only root filesystem (set in the prod compose).
# Kestrel listens on 5235 (>1024), so no privileged port bind is needed.
USER app

# The app listens here (see ASPNETCORE_URLS in docker-compose.yml). EXPOSE is documentation only.
EXPOSE 5235

ENTRYPOINT ["dotnet", "CleanArch.Api.dll"]
