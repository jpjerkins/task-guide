# iOS Safari native date picker dismissing itself — research for #46

Research for [issue #46](https://github.com/jpjerkins/task-guide/issues/46). The reported symptom —
a native `<input type="date">` picker opens on iPhone and dismisses itself in under a second — was
observed **inside a published Claude artifact** (a heavily sandboxed, cross-origin `<iframe>`), which
the issue itself flags as a confound. Nobody has reproduced this on a plain page yet. This document
does the reading legwork and hands over the exact minimal test a human needs to run on a real device
to settle it. No claim below is based on hands-on device testing — I have no physical iPhone.

---

## TL;DR

- **No primary source (WebKit Bugzilla, WebKit blog, Apple Developer Forums) documents a defect
  matching "native date picker opens then closes itself within ~1 second" on a plain, unframed page.**
  What *is* well documented are different, adjacent iOS Safari date-input quirks (blank field styling,
  `blur` not firing immediately post-close in iOS 13+, `min`/`max` being ignored, VoiceOver blocking
  the picker from opening at all) — none of which match "opens then auto-dismisses."
- **The iframe/sandbox explanation is plausible and mechanistically motivated, but I could not find a
  primary source that confirms it for this exact symptom either.** The native date picker on iOS is a
  system-presented control layered outside normal iframe DOM, and cross-origin/sandboxed embedding is
  a well-known source of iOS Safari focus and popup weirdness generally — but that is inference from
  adjacent bug reports, not a citation that nails this symptom to a specific sandbox token.
- **Confidence: low-to-moderate that iframe/sandbox is the cause, and low that it's a general WebKit
  regression** — mainly because neither hypothesis has a matching primary-source bug report. The two
  are **not separated by anything short of a device test on a plain page**, which is exactly what
  Section 3 below specifies.
- Fallback landscape: a lenient free-text parser (`chrono-node`, ~40 KB gzip) or a small dedicated
  React date-grid library (`react-day-picker`, ~19 KB gzip) are both realistic no-CDN options; a
  hand-rolled parser/grid is realistic too given the app's narrow needs (single locale, single user).
  `react-datepicker` and MUI X are not — see below.
- Genuinely date-picking surfaces: **2 primary-path fields (Deadline, Event date) + 3 secondary/
  optional-path fields (Defer-absolute, Postpone escape hatch, Recurrence first-due)**. Defer's offset
  form and Recurrence's calendar-anchored form already avoid absolute-date entry for most tasks.

---

## 1. WebKit-defect findings

Search strategy: WebKit Bugzilla (`bugs.webkit.org`), webkit.org/blog, Apple Developer Forums, and
developer writeups with real repro cases (GitHub issues on date-picker libraries, Stack Overflow).
Queries tried included variations of: `webkit bugzilla input type=date picker dismiss ios`,
`ios safari date input picker closes immediately`, `<input type=date> ios safari auto dismiss iframe`.

Nothing found matches the reported symptom directly. What *did* turn up, all distinct issues:

- **[WebKit Bug 119175](https://bugs.webkit.org/show_bug.cgi?id=119175)** — `<input type="date">`
  doesn't show any date-specific controls (a rendering/support issue, not a dismiss-after-open issue).
- **[WebKit Bug 198959](https://bugs.webkit.org/show_bug.cgi?id=198959)** — `input[type=date]` with
  an empty value has shrunken height (styling only).
- **[WebKit Bug 225639](https://bugs.webkit.org/show_bug.cgi?id=225639)** — `[iOS] HTML datepicker's
  min-max attribute not working. Confirmed separately by an [Apple Developer Forums thread](https://developer.apple.com/forums/thread/743096)
  on iOS/iPadOS date pickers ignoring `min`/`max` on open.
- **[Apple Developer Forums — iOS13 blur delay](https://developer.apple.com/forums/thread/123777)** —
  in iOS 13+, the `blur` event does not fire immediately when the date/time popover closes; one extra
  tap is needed. This is a *known, named* regression (iOS 12 fired `blur` immediately; iOS 13 changed
  that), with a documented workaround (force-blur `document.activeElement` on `touchstart`). Relevant
  context — it shows Apple *has* shipped picker-lifecycle regressions before — but it is about a stale
  `blur`, not the picker closing itself.
- **[DEV Community — VoiceOver bug](https://dev.to/mfranzke/voiceover-bug-on-ios-safari-blocks-date-time-related-inputs-especially-in-react-4f61)** —
  documents a case where a surrounding element with a click handler prevents the native date/time
  picker from *opening at all* under VoiceOver. Different failure mode (never opens vs. opens then
  closes), but worth knowing: iOS Safari's date/time picker is sensitive to sibling/ancestor event
  listeners, which is at least thematically close to "something else on the page interferes with the
  picker's lifecycle."
- **[Hacker0x01/react-datepicker#830](https://github.com/Hacker0x01/react-datepicker/issues/830)** —
  "Date picker does not reopen after closing on Safari iOS": select a date, picker closes, tapping the
  field again doesn't reopen it. This is about a *third-party* JS-rendered picker's re-open behavior,
  not the native `<input type="date">` UI, so it's a different control entirely — but it's the closest
  textual match to "closes" in the search results, and it is **not** the reported bug.
- **[Apple Developer Forums #656055 — iOS 14 beta 3](https://developer.apple.com/forums/thread/656055)**
  and **[#664785 — iOS 14](https://developer.apple.com/forums/thread/664785)** — both about the date
  picker failing to open post-update, not about a picker that opens and then vanishes.

**No WebKit Bugzilla entry, blog post, or forum thread found states "the date picker opens and then
closes itself within under a second" as a general (non-iframe) phenomenon.** That absence is itself
informative: this is a heavily searched surface (iOS `<input type="date">` bugs are a well-trodden
complaint topic — see the widely-shared [Safari's date-picker is the cause of 1/3 of our customer
support issues](https://brianlovin.com/hn/34145216) writeup), and a symptom this dramatic would likely
have its own thread if it reproduced on plain pages at any scale. That's circumstantial, not proof.

---

## 2. Iframe-sandbox findings

The setup for issue #46: Claude artifacts render inside a cross-origin, heavily sandboxed `<iframe>`.
The native iOS date picker is presented as a system-level control that layers outside normal in-DOM
rendering — which makes it a reasonable a priori suspect for anything cross-origin-iframe-shaped, since
iOS Safari has many documented cross-origin iframe quirks around focus and popups generally.

What's documented, from MDN's [`<iframe sandbox>` reference](https://developer.mozilla.org/en-US/docs/Web/HTML/Element/iframe#sandbox)
and [WebKit's own sandboxing demo page](https://webkit.org/demos/frames/sandboxing/):

| Token | Effect | Plausibly relevant here? |
|---|---|---|
| `allow-same-origin` | Without it, the framed document is treated as an **opaque origin** — it fails same-origin checks even against its own origin, which affects storage and can affect how the embedding browser treats event/focus plumbing. | Plausible — an opaque-origin frame is the single biggest behavioral difference between "sandboxed" and "normal" iframes. |
| `allow-scripts` | Without it, no JS runs at all (the artifact clearly runs JS, so this is granted). | Not implicated directly, but combined with `allow-same-origin` triggers WebKit's own escape warning (see below). |
| `allow-modals` | Governs `alert`/`confirm`/`prompt`/`<dialog>`. Per [WebKit Bug 171321](https://bugs.webkit.org/show_bug.cgi?id=171321) (the bug that *added* this flag) this class of UI used to be unconditionally allowed for sandboxed frames and was later gated. | The native date/time picker is **not** a `<dialog>` or `alert()` — it's an OS-level form-control UI — so `allow-modals` is not obviously the right lever, but the *precedent* (WebKit gating "outside-the-DOM" UI behind a sandbox flag) is exactly the shape of thing that could apply to date pickers too, undocumented. |
| `allow-popups` / `allow-popups-to-escape-sandbox` | Governs `window.open()`/`target="_blank"`. Per [WebKit Bug 158875](https://bugs.webkit.org/show_bug.cgi?id=158875). | Not obviously relevant — the date picker isn't a new browsing context — but flagged for completeness since it's another "leaves the frame's box" primitive. |
| `allow-forms` | Governs form submission, not rendering of individual controls. | Unlikely to matter — the picker opens at all per the report, it's the staying-open that fails. |

**Also relevant: [WebKit Bug 267688](https://bugs.webkit.org/show_bug.cgi?id=267688)** — "[popover]
Light dismiss doesn't work on iOS/iPadOS" — a confirmed, real WebKit bug about iOS/iPadOS mishandling
the dismiss behavior of *popover-like* UI in some circumstances. It's about the HTML Popover API, not
`<input type="date">`, but it establishes that iOS Safari's popover/overlay dismiss logic is an active
area with known bugs — again thematically adjacent, not a direct hit.

**One general search result is worth flagging as an unsourced but plausible-sounding claim**: a
synthesized web search summary described "complex React or interactive HTML artifacts may render
incompletely on iOS Safari due to iframe sandbox restrictions" and noted **`localStorage`/
`sessionStorage` are blocked in the Claude artifact sandbox**. The storage-blocking claim is consistent
with `allow-same-origin` being withheld (opaque origin ⇒ no persistent storage), which is standard
sandboxed-iframe behavior, not artifact-specific. I could not trace this specific claim to a citable
Anthropic primary source in the time available, so it's reported here as a lead, not a fact — worth an
Anthropic docs check if this becomes load-bearing.

### Confidence level

**Low-to-moderate that iframe/sandbox is the cause; low that this is a general (non-framed) WebKit
regression.** Neither side has a primary source that directly names this symptom. The iframe-sandbox
hypothesis is favored only because:

1. The observation happened specifically inside a sandboxed cross-origin iframe, not on a plain page.
2. iOS Safari's handling of cross-origin iframe focus/UI lifecycle is independently well documented as
   quirky (see the focus-stealing and cross-origin-iframe-focus threads turned up in search, e.g.
   [Apple Developer Forums #28656](https://developer.apple.com/forums/thread/28656) and
   [#763885](https://developer.apple.com/forums/thread/763885)), even though none of those threads
   mention date pickers specifically.
3. `task-guide` itself will **never** run inside an iframe in production — it's a plain served SPA —
   so even a confirmed iframe-sandbox cause would mean issue #46 doesn't threaten production at all.

This is explicitly a case where **the evidence does not let the two hypotheses be fully separated**
from reading alone. That's exactly what Section 3 is for.

---

## 3. The precise minimal test

Goal: reproduce (or rule out) the dismiss-in-under-a-second behavior on a **plain, unframed page**,
served over HTTPS to the iPhone (iOS Safari requires a secure context for full form-control behavior
in some cases, and Tailscale Serve gives you that for free). Two minutes, one decisive yes/no.

**1. Save this file** (e.g. `~/date-test.html`):

```html
<!doctype html>
<html>
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>date test</title>
</head>
<body style="font-family: sans-serif; padding: 2rem;">
  <p>Tap the field below and try to pick a date.</p>
  <input type="date" style="font-size: 1.5rem; padding: 0.5rem;">
</body>
</html>
```

**2. Serve it locally and expose it over Tailscale**, from the Macbook (or any machine on the
Tailnet):

```sh
# from the directory containing date-test.html
python3 -m http.server 8080

# in a second terminal
tailscale serve --bg --https=443 8080
```

`tailscale serve status` will print the HTTPS URL (something like
`https://<macbook-hostname>.<tailnet-name>.ts.net/`). No iframe, no sandbox, no CSP — nothing between
the iPhone and a bare `<input type="date">`.

**3. On the iPhone** (with Tailscale connected to the same tailnet): open that URL in Safari, tap the
date field.

- **Decisive "no bug on plain pages"**: the picker opens and **stays open** — you can scroll the
  wheels/tap a date and it behaves normally. This points the cause squarely at the iframe/sandbox
  environment, and issue #46 can be closed as "confound only" — of no concern to production `task-guide`.
- **Decisive "real WebKit/OS-level bug"**: the picker still opens and dismisses itself within about a
  second, with nothing else on the page. This is a genuine finding worth a fresh, narrowly-scoped
  WebKit Bugzilla report (note exact iOS version and Safari version from Settings → General → About,
  since bug trackers need that), and it changes the fallback-library discussion from "hedge" to
  "required."

That's the whole test — one file, two `tailscale serve` commands, one tap on the phone.

---

## 4. Fallback landscape (real numbers, no-CDN constraint)

`task-guide` ships as static files from one container with **no CDN** — every KB shipped costs real
phone bandwidth on every load. Sizes below are gzip, from Bundlephobia's package-size API
(`bundlephobia.com/api/size?package=<name>`), current as of this research (August 2026).

| Option | Gzip size | Notes |
|---|---|---|
| **`chrono-node`** (lenient free-text date parser) | **~40 KB gzip** (41,184 B gzip / 174,823 B raw, v2.10.1) | Matches the repo's established preference for lenient free-text parsing over rigid widgets (see time-of-day "3p" precedent). No picker UI needed at all — pairs with a plain `<input type="text">`. 40 KB isn't nothing on a phone, but it's a single one-time cache hit, not per-load if the SPA is cached correctly. |
| **Hand-rolled lenient date parser** | **near-zero KB** | The domain only needs a narrow grammar ("aug 21", "8/21", "tomorrow", "next monday", "in 2 weeks", "jan") — not general NLP. A hand-rolled parser covering the app's actual real-world input patterns is very plausibly under 1–2 KB and avoids the dependency entirely. Not benchmarked here (no such module exists yet) — sizing is an estimate, not a citation. |
| **`react-day-picker`** (small dedicated React date-grid component) | **~19 KB gzip** (19,305 B gzip / 67,045 B raw, v10.0.1) | The most realistic "just use a small library" option if a visual date-grid is wanted for Deadline/Event entry. No moment.js, no heavyweight dependency chain in this measurement. |
| **`react-datepicker`** | **~45 KB gzip for the main bundle alone** (44,994 B gzip / 173,474 B raw, v9.1.0) — **plus** its dependency tree: `date-fns` (~185 KB raw), `@floating-ui/react` + `@floating-ui/core` + `@floating-ui/dom` + `@floating-ui/react-dom` + `@floating-ui/utils` (~128 KB + 22 KB + 15 KB + 7 KB + 9 KB raw), `tabbable` (~6 KB raw), `clsx` (~1 KB). | **Deprioritized.** Even before considering that some of those are peer/shared deps that might tree-shake, this is a materially heavier dependency graph than `react-day-picker` for equivalent functionality, and it pulls in a positioning library (`floating-ui`) the app has no other use for. |
| **MUI X date pickers** | Not independently measured here, but MUI X ships as part of the broader MUI ecosystem (emotion/styled-engine, `@mui/base`, `@mui/system`) — routinely reported in the hundreds-of-KB range in practice. | **Ruled out outright.** Pulling in a chunk of MUI's styling engine for one form control is exactly the kind of "mega-dependency for a single-user no-CDN app" this repo's constraints exist to avoid. |
| **moment.js-based pickers (generic)** | moment.js itself is commonly cited in the ~70 KB gzip range (with locale data pushing it higher), and it's in long-term maintenance-only mode upstream. | **Ruled out.** Any picker whose primary dependency is moment.js is worse on both size and freshness than the alternatives above; the ecosystem itself has been migrating off moment.js for years. |
| **Custom-built month-grid control** | Rough scope estimate, not a citation: a one-handed, thumb-sized month grid with prev/next-month taps, no i18n, no locale complexity, single date selection — is a small, well-understood UI (a 6×7 button grid plus two nav buttons and basic date math). Comparable hand-rolled implementations in blog writeups run a few hundred lines of component code. Could plausibly land under `react-day-picker`'s 19 KB once built and minified, at the cost of engineering time instead of dependency weight. | Legitimate "just build it" candidate given the app's narrow needs. |

### Recommendation (flagged as a recommendation, not a decision)

Given the repo's stated preference for lenient free-text entry (the "3p" precedent) and that only
**2 of 5 date-picking surfaces are on the primary path** (see Section 5), the lowest-risk fallback if
the iPhone test in Section 3 confirms a real bug is: **`<input type="text">` with a small, hand-rolled
lenient parser** for the two primary-path fields (Deadline, Event date), reusing whatever pattern
already exists for lenient time parsing. `react-day-picker` (~19 KB gzip) is the fallback-of-the-
fallback if free-text parsing turns out to be a worse UX for dates specifically than it is for times —
worth a second opinion from the repo owner, since this diverges from pure text entry and reintroduces
a picker UI, just a lighter one. `chrono-node` is a reasonable middle ground if the hand-rolled parser
proves harder to get right than expected — 40 KB gzip is a real but bounded, one-time cost.

---

## 5. Date-picking surface count

Counted directly from `CONTEXT.md`'s glossary (Deadline, Defer, Postpone, Recurrence, Event, Override):

| Surface | Needs a date picker? | Why |
|---|---|---|
| **Deadline** | **Yes — primary path.** | Plain absolute date field on every Task that has one. No alternate form. |
| **Event date** | **Yes — primary path** (for the "entered directly" one-off case). | CONTEXT.md: "Instantiated from a prototype, or entered directly." A prototype-instantiated recurring Event needs no date entry; a one-off Event does. |
| **Defer (absolute form)** | **Secondary/optional path.** | Defer is authored as *either* an absolute date *or* an Offset from Deadline; **recurring Tasks must use the offset form**, so only a one-off Task choosing the absolute form ever hits a date picker here. |
| **Postpone escape hatch** | **Secondary/optional path.** | Three fixed intervals (tomorrow / a week / a month) cover the common "not now" gesture with zero date-picker interaction; only the explicit "pick a date…" escape hatch needs one, and CONTEXT.md is explicit that "a picker alone is wrong for a one-handed gesture" — the fixed intervals are the primary UI, the picker is the overflow. |
| **Recurrence first-due** | **Secondary/optional path.** | Completion-anchored rules default to `CreatedAt` as the start point and only need a picker if the user supplies an *explicit* first-due date instead. Calendar-anchored rules and the offset form need no absolute date entry at all. |
| **Override date** | **Flagged, not counted — UI-undecided.** | CONTEXT.md describes an Override as created either by "editing an existing calendar date" (which reads as a calendar/date-grid UI already, not necessarily a raw `<input type="date">`) or generated automatically by an Event/Window overlap (no user date entry at all — it inherits the triggering Event's date). Nothing in CONTEXT.md specifies how a human picks *which* date to stamp when creating an Override from scratch outside those two paths, so this is not asserted as a sixth `<input type="date">` surface — it's an open UI question, separate from this bug investigation. |

**Total: 2 primary-path surfaces (Deadline, Event date) + 3 secondary/optional-path surfaces
(Defer-absolute, Postpone-escape-hatch, Recurrence-first-due).** This matters for blast-radius sizing:
even in the worst case (native picker is genuinely broken and must be replaced everywhere), the
replacement only has to feel great on 2 screens that see it on every use — the other 3 are already
low-frequency by design (Defer's offset form and Recurrence's calendar-anchored form already avoid
absolute-date entry for most tasks that would use those features).

---

## 6. Open questions / what only a device test can settle

- **Does the picker dismiss itself on a plain, unframed page at all?** This is the whole question
  Section 3's test answers. Nothing in this document substitutes for it.
- **If it does reproduce on a plain page: which iOS/Safari version(s)?** No version-specific fix or
  regression window was found for this exact symptom, so a real repro needs its exact OS/Safari
  version recorded (Settings → General → About) to be useful for a WebKit Bugzilla report.
- **If it does reproduce on a plain page: is it Tailscale-HTTPS-specific?** Worth a mental note (not a
  full second test) — if it turns out to reproduce only when served via `tailscale serve`, that would
  point to something about the TLS/proxy setup rather than Safari itself; unlikely, but cheap to keep
  in mind while running the test.
- **If it does NOT reproduce on a plain page: which sandbox token(s) actually matter?** This document
  could not pin the mechanism down beyond "cross-origin iframe UI/focus quirks are a known category on
  iOS Safari." Isolating it further (peeling `sandbox` tokens off one at a time on a test artifact)
  would be a follow-up investigation, and is almost certainly not worth doing — issue #46's practical
  answer at that point is simply "not a production concern," since `task-guide` never runs inside an
  iframe.
- **Unverified lead**: the claim that Claude artifacts block `localStorage`/`sessionStorage` (found in
  a synthesized search result, not traced to a citable Anthropic primary source here) — worth
  confirming directly against Anthropic's own artifact documentation if it ever becomes relevant to
  another investigation.

---

## 7. RESULT — the device test was run, and it settles this (2026-08-24)

**Sections 1–6 above were written before the device test. Section 4's fallback landscape and its
recommendation are now moot** — no fallback is needed. They are kept as the record of what was
considered, not as live options.

The test ran against [`docs/prototypes/date-picker-probe.prototype.html`](../prototypes/date-picker-probe.prototype.html):
seven isolated rows, one variable each, on a plain same-origin page served over Tailscale Serve — no
iframe, no sandbox, no framework, no CDN.

| # | Row | Result |
|---|---|---|
| 1 | Bare input, zero JS | **stayed open** |
| 2 | Passive `input` listener, touches no DOM | **stayed open** |
| 3 | `innerHTML` rebuild on `input` | **dismissed** |
| 4 | Value written back, same node kept | **stayed open** |
| 5 | Node replaced with a fresh one (`replaceChild`) | **dismissed** |
| 6 | Inside a `transform`/`will-change` ancestor | **stayed open** |
| 7 | `focus` handler that blurs sibling fields | **stayed open** |

**The picker dismisses itself if and only if the input element is destroyed or replaced while it is
open.** Rows 3 and 5 are exactly the two rows that break the element's identity, and they are the only
two that failed. Everything else survives.

What each surviving row rules out, which matters because these were the standing suspicions:

- **Row 1 clears Mobile Safari.** No WebKit defect is involved, matching Section 1's finding that no
  primary source documents this symptom on a plain page. That absence was circumstantial; row 1 makes
  it a result.
- **Row 1 makes the iframe/sandbox hypothesis unnecessary — but does NOT refute it.** No row of this
  probe was inside an iframe, so the probe has no power to say what a sandboxed one does. What it
  shows is that a remount is *sufficient* to produce the symptom with no iframe present, which means
  the observed defect is fully explained without invoking the sandbox. Whether the sandbox is *also*
  capable of it, alone or in combination, is untested by anyone: #48 tested a bare input on a plain
  page, and so did row 1. **Neither ticket has ever run a date input inside an artifact.** Section 2's
  low-to-moderate confidence therefore still stands as stated, neither raised nor lowered.
- **Row 4 clears React's controlled inputs.** React's normal reconciliation keeps the DOM node and
  reassigns the value, which is exactly row 4. The idiomatic controlled `<input type="date">` is safe.
- **Row 6 clears the `transform`-ancestor folklore**, and **row 7 clears focus management.**

### Where the observed defect actually came from

The tag entry prototype, not the browser. Its delegated handler
(`docs/prototypes/tag-entry.prototype.html`):

```js
if (act === "deadline")  { t.deadline = e.target.value; …; return render(); }
if (act === "deferdate") { t.defer.date = e.target.value; }   // no render()
```

`render()` does `document.getElementById("device").innerHTML = …` — row 3 exactly. iOS fires `input`
as the picker's wheels move, so the picker's own first event destroys the node it is attached to.

**The Deadline/Defer asymmetry is a prediction from this code, not a recorded observation.** Deadline
re-renders and Defer's "On a date" does not, so the two should behave differently on the same page in
the same second — but only Deadline was ever reported (#38: "the defect you hit on the Deadline
field"), and Defer's absolute form sits behind a mode toggle that is disabled for recurring Tasks and
may simply never have been tapped. It is a clean falsifiable test of this explanation inside the
artifact, and it has not been run. Do not cite it as evidence until it has.

### The constraint this leaves for the build

The native control stays. What the spec carries instead is a rule about the DOM, not about dates:

> **A date input's element must survive its own input events.** Never remount one in response to its
> own change — no changing `key`, no conditional-render branch swap, no `innerHTML` rebuild of an
> ancestor. Reassigning `value` on the same node is fine.

This is **not date-specific**. Every system-presented control on iOS — `<select>`, `type="time"`,
`type="month"`, `type="datetime-local"` — is presented by the OS and anchored to a live DOM element,
so all of them inherit the same lifetime coupling. The spec should state it that way, because a
date-only rule would be rediscovered the first time a `<select>` is rebuilt mid-interaction.
