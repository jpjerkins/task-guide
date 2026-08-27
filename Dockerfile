# Built natively on pi5 (linux/arm64) via DCM's build-based deploy (#5). Build context is the
# repo root; DCM registers this service with `build.dockerfile: Dockerfile` at that root.

# ---------- stage 1: SPA ----------
# vite.config.ts's build.outDir is "../TaskGuide.Api/wwwroot", resolved relative to this stage's
# working directory — so WORKDIR is set to the same src/TaskGuide.Web nesting as the repo, and
# the build writes straight into /src/TaskGuide.Api/wwwroot without any outDir override. Vite
# creates that directory itself; nothing else needs to be copied into the sibling path.
FROM node:22-alpine AS web-build
WORKDIR /src/TaskGuide.Web
COPY src/TaskGuide.Web/package.json src/TaskGuide.Web/package-lock.json ./
RUN npm ci
COPY src/TaskGuide.Web/ ./
RUN npm run build

# ---------- stage 2: .NET publish ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
WORKDIR /src
COPY Directory.Build.props ./
COPY src/TaskGuide.Domain/TaskGuide.Domain.csproj src/TaskGuide.Domain/
COPY src/TaskGuide.Application/TaskGuide.Application.csproj src/TaskGuide.Application/
COPY src/TaskGuide.Infrastructure/TaskGuide.Infrastructure.csproj src/TaskGuide.Infrastructure/
COPY src/TaskGuide.Api/TaskGuide.Api.csproj src/TaskGuide.Api/
RUN dotnet restore src/TaskGuide.Api/TaskGuide.Api.csproj
COPY src/TaskGuide.Domain/ src/TaskGuide.Domain/
COPY src/TaskGuide.Application/ src/TaskGuide.Application/
COPY src/TaskGuide.Infrastructure/ src/TaskGuide.Infrastructure/
COPY src/TaskGuide.Api/ src/TaskGuide.Api/
RUN dotnet publish src/TaskGuide.Api/TaskGuide.Api.csproj -c Release -o /app/publish --no-restore

# ---------- stage 3: runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Per-container memory accounting is disabled on pi5 (cgroup_disable=memory, #39) — Docker
# cannot enforce or even observe a memory limit for this container. The settled substitute is a
# self-imposed GC hard limit inside the runtime itself:
#   - DOTNET_GCHeapHardLimit: 0x10000000 = 256 MiB. Comparable .NET 10 services on this Pi
#     (tto-web-api, exercise) run ~262-265 MB as *whole images* and were observed at
#     roughly 100-200 MB RSS at idle (docs/research/dcm-dotnet-deployment.md §5). 256 MiB gives
#     this single-user, low-traffic service headroom above that idle band without letting an
#     unbounded leak eat into a host that's already 70% into swap.
#   - DOTNET_gcServer=0: force workstation GC. Server GC allocates a heap per core, which is
#     tuned for throughput on multi-core hosts, not for capping worst-case RSS on a shared Pi.
ENV DOTNET_GCHeapHardLimit=0x10000000 \
    DOTNET_gcServer=0 \
    ASPNETCORE_ENVIRONMENT=Production

# Non-root. NOTE: the host bind-mount /mnt/data/task-guide -> /data is created by DCM
# (data_dir: true) and its ownership has been inconsistent across existing services
# (docs/research/dcm-dotnet-deployment.md §6) - confirm this UID can write it post-deploy, or
# chown the host directory to match.
RUN groupadd --gid 10001 appuser \
    && useradd --uid 10001 --gid appuser --no-create-home --shell /usr/sbin/nologin appuser

COPY --from=dotnet-build /app/publish .
COPY --from=web-build /src/TaskGuide.Api/wwwroot ./wwwroot

RUN chown -R appuser:appuser /app
USER appuser

# Program.cs hardcodes http://0.0.0.0:8007 (host-mode, tailnet-only, TLS terminated by Tailscale
# Serve) - the container's own listening port IS 8007, not 8080. See the note in the report about
# docs/research/dcm-dotnet-deployment.md's example registration, which assumed a proxied 8080.
EXPOSE 8007

# Pushover token/user key and any other secrets are supplied as environment at deploy time
# (DCM Tier 2 secrets) - never baked into this image.
ENTRYPOINT ["dotnet", "TaskGuide.Api.dll"]
