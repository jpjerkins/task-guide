# ADR-0003 — Docker Swarm via DCM, host-mode port 8007, tailnet-only

**Status:** Accepted · **Source:** [#5](https://github.com/jpjerkins/task-guide/issues/5),
[#4](https://github.com/jpjerkins/task-guide/issues/4) · **Proven in production** 2026-08-26

## Context

Deployment is to `pi5` (Raspberry Pi 5, 8 GB, ARM64) through the Docker Compose Manager (DCM),
whose registry has migrated to Swarm — `artifact_format` now defaults to `swarm`. The service must
reach the tailnet and nothing else.

## Decision

**One build-based Docker Swarm service.** Register once via DCM's `POST /deploy` with a `build`
block pointing at a source tree on the Pi; DCM builds ARM64 natively, pushes to
`localhost:5000/task-guide`, and deploys the immutable digest.

- **`data_dir: true`** — creates `/mnt/data/task-guide` and binds it to `/data`.
- **One host-mode published port, `8007`**, fitting the existing `800x` block.
- **`update_config.order: stop-first`, never `start-first`.** Two replicas must never overlap on a
  JSON-file bind mount — this is ADR-0001's single-writer rule expressed in the orchestrator.
- **Dockerfile:** `sdk:10.0` build stage → `aspnet:10.0` runtime, copied from `tto-web-api`.
  Deployed .NET images on this box measure 262–265 MB.
- **TLS via Tailscale Serve**, with real Let's Encrypt certs. The client-facing name is the MagicDNS
  name `pi5.<tailnet>.ts.net` — **not** `pi5.local`.
- **Secrets are read in-process at runtime**, from a read-only bind mount of `/run/vault-t2-fs`.

## What this forbids

- **Funnel is an explicit never.** So is joining the `nginx` or `cloudflared` networks. Tailnet-only
  is the default on this box, not a configuration: the router forwards no ports, so a host port is
  invisible to the internet unless someone adds a public hostname in the Cloudflare dashboard.
- **Do not read secrets through Compose `env_file:`.** It resolves client-side at deploy time as
  root, and vault-t2 serves each envfile to its declared UID alone — denying even root. The process
  must read the file itself, as its own UID (50013).
- **Do not set Docker memory limits.** `/proc/cmdline` carries `cgroup_disable=memory`, so limits
  are unenforceable host-wide and `docker stats` reports `0B` for every container
  ([#39](https://github.com/jpjerkins/task-guide/issues/39)). The answer is the self-imposed GC hard
  limit in the `Dockerfile`.
- **Do not use `~/dev/dcm/skills/dcm.skill.md`.** It is orphaned and broken as written — every
  `curl` omits the required `Authorization: Bearer` header, and it documents no build-based deploy
  path. Use the `mcp__dcm__*` tools. Trust `lib/registry.py` over the documented `ServiceSpec`.

## Operational notes

- `PATCH /registry/{service}` alone does **not** re-render the stack file; follow it with
  `POST /upgrade/{service}`. A second `POST /deploy` returns *already exists*.
- pi5 runs `sudo-rs`: `sudo -u '#50013'` fails. Use
  `sudo setpriv --reuid=50013 --regid=50013 --clear-groups`.
- Mount `purpose` is `secret`, singular.
- Full sequence and outcomes: `docs/runbooks/first-deploy.md`.
