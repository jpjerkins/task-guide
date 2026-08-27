# The SPA

React + TypeScript + Vite, built into `../TaskGuide.Api/wwwroot` and served as static files from
the same ASP.NET Core container — one container, no separate frontend process (#5, #6).

Chosen over Blazor WASM on bandwidth: Blazor's baseline framework download alone is ~1.5 MB before
app code, and AOT pushes several MB more, against a few hundred KB gzipped for a lean React bundle.
Blazor Server was ruled out earlier for needing a persistent SignalR connection — a poor fit for
"tap a cold Pushover link on a phone that was asleep".

## Types come from the API, never from here

`src/api/schema.d.ts` is generated — do not edit it, and do not hand-write a type for anything
the API returns. `src/api/client.ts` derives its wire types from it and is the one place a
response is normalised before component code sees it.

To regenerate, the script needs a live API on `localhost:8007`:

```sh
# from the repo root, in another shell — /data is not writable on a dev machine
Storage__DataDir=$(mktemp -d) dotnet run --project src/TaskGuide.Api
# then, from this directory
npm run gen:api
```

`tests/TaskGuide.Api.Tests/OpenApiDocumentTests.cs` guards the document's shape. A Minimal API
handler that returns plain `Results.Ok(...)` is typed `IResult`, so the generator infers nothing
and emits a bare `200: OK` with no schema — use `TypedResults` for anything the SPA consumes.

Note .NET 10 describes an int32 as `integer | string`, so a generated numeric field arrives as
`number | string`. Coerce it in `client.ts`, and assert the coercion on the *value*: `${x}m`
renders `30` and `'30'` identically, so a DOM assertion cannot hold it.

## Shell — variant D, "moment-first, merged"

A four-tab bar (**Now / Tasks / Schedule / More**), a Today-style home screen, and quick-add as a
small accent circle owning the nav's right slot on *every* screen.

## The fourteen surfaces

Everything doable via the API must also be doable here:

reminder landing page · task list (+ status filters) · task detail · quick capture · "Right now" on
demand · today/day view · window editor · day-template editor + usage list · pattern editor ·
pattern switcher · override a date · event create + overlap resolution · unprocessed/stale triage ·
read-only dimensions viewer

## Two rules the UI cannot break

**A system-presented control must survive its own input events** (#46). Never remount one in
response to its own change: no changing `key`, no conditional-render branch swap, no `innerHTML`
rebuild of an ancestor. Reassigning `value` on the same node is fine. Deliberately not date-
specific — `<select>`, `type="time"`, `type="month"`, the ordinal-Dimension sliders and the
Recurrence editor all couple the same way.

**There is no client-side clock.** Snooze availability, the landing page's live-day gate and every
other timing predicate are answered by the server; the UI reads them. A page drawn at 11:58p and
tapped at 12:01a was honestly live when drawn, and the rejection renders as the same line the
disabled state would have shown.

## Reaching a date

The **±10-day rail** is the near-term surface, with a **"Pick a date…"** escape beside it opening a
native `<input type="date">` (#41) — the same shape Postpone already uses: fixed intervals plus an
escape. The rail never grows. Authoring an Override from scratch takes a **start–end range**,
writing one Override per date.
