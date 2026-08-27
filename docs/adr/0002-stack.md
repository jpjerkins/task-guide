# ADR-0002 — .NET 10, Minimal APIs, and a React SPA in one container

**Status:** Accepted · **Source:** [#6](https://github.com/jpjerkins/task-guide/issues/6) · **Proven in production** 2026-08-26

## Context

The user leans C# and reads TypeScript comfortably. Onion architecture, SOLID, DDD naming and small
files are standing requirements. The real fork was the UI: a mobile-friendly C# UI means Blazor,
and the alternative splits the stack in two.

## Decision

- **.NET 10** — standing default, proven on this Pi 5 (ARM64) by `tto-web-api`.
- **Minimal APIs, grouped by feature via `MapGroup`.** No controllers — the API layer is a thin
  adapter per onion architecture.
- **React SPA** (TypeScript, Vite), built into `wwwroot` and served as static files from the same
  ASP.NET Core container.
- **Scheduler is an in-process `BackgroundService`** in that same container. No separate process,
  no cron trigger.
- **Logging: Serilog**, rolling daily file sink with a retention count — the same whole-file
  retention shape the Fire record uses.
- **Testing: TDD throughout.** xUnit for the backend; Playwright (Chromium) for front-to-back
  contract tests, which run on pi5, not only on the Macbook. No CI gate — single developer.

### API types are generated, never hand-written

TS types come from the Minimal API's native OpenAPI output via `openapi-typescript`
(`npm run gen:api` → `src/api/schema.d.ts`). See the SPA README for the regeneration loop.

**A handler returning plain `Results.Ok(...)` is typed `IResult`, so the generator infers nothing
and emits a bare `200: OK` with no schema.** Use `TypedResults` for anything the SPA consumes.
`tests/TaskGuide.Api.Tests/OpenApiDocumentTests.cs` guards this.

Type generation is **backstopped by Playwright**, not trusted alone — a matching type does not
prove a working flow.

## Why React over Blazor

Bandwidth on a phone waking from sleep. Blazor WASM's baseline framework download is **~1.5 MB
before app code**, and AOT pushes several MB more, against a few hundred KB gzipped for a lean
React bundle. Blazor Server was ruled out for needing a persistent SignalR connection — a poor fit
for *"tap a cold Pushover link on a phone that was asleep."*

## What this forbids

- **No second process and no second container.** The scheduler shares the write lock with the API
  by being in the same process (ADR-0001). Two processes across a container boundary would need
  invented file locking over a plain-JSON store.
- **Do not hand-write a type for anything the API returns.** If the type is awkward to generate,
  fix the endpoint's OpenAPI metadata, not the SPA.
- **No controllers**, and no business logic in an endpoint handler — endpoints are adapters.
- **`task-guide` never runs in an iframe.** ADR-0006 relies on this.
