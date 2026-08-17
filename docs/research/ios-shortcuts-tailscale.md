# iOS Shortcuts and API reachability over Tailscale

Research for [issue #4](https://github.com/jpjerkins/task-guide/issues/4). Planning only — nothing built.

**Bottom line:** Yes, this is dependable enough to build on, with two hard constraints:
serve the API over **HTTPS with a real Tailscale-issued certificate on a MagicDNS
`*.ts.net` name** (not `pi5.local`, not a self-signed cert, probably not plain HTTP),
and design the endpoints so a *single* `Get Contents of URL` action does a whole
useful thing.

---

## 1. Can Shortcuts reach a tailnet host?

### The mechanism

Tailscale on iOS is a **Network Extension** — a system-level VPN provider, not a
per-app proxy. Tailscale's own docs describe it as running "in the background to
secure traffic from all applications without requiring them to change anything"
([Tailscale for iOS blog](https://tailscale.com/blog/reimagining-tailscale-for-ios)).
That means the Shortcuts app's `Get Contents of URL` goes through the normal iOS
URL loading system, which sees tailnet routes and MagicDNS exactly like Safari does.
There is nothing Shortcuts-specific to configure.

Tailnet addresses are stable: each device gets a fixed address in the CGNAT range
`100.64.0.0/10`, and "Tailscale IP addresses remain constant regardless of the
device's physical location" — including switching Wi‑Fi to cellular
([100.x addresses](https://tailscale.com/kb/1015/100.x-addresses)). So **cellular
vs. home Wi‑Fi is not a meaningful distinction** for reachability; both are just
"the internet" from WireGuard's point of view. On home Wi‑Fi the connection is
likely a direct LAN peer-to-peer path; on cellular it is direct-over-internet or
DERP-relayed. Either way the address and hostname are the same.

```mermaid
flowchart LR
  A["Shortcut<br/>Get Contents of URL"] --> B["iOS URL loading system"]
  B --> C["Tailscale Network Extension<br/>(system VPN)"]
  C -->|"MagicDNS: pi5.TAILNET.ts.net<br/>-> 100.x.y.z"| D["WireGuard tunnel<br/>(direct or DERP relay)"]
  D --> E["tailscaled on pi5"]
  E -->|"Tailscale Serve<br/>TLS terminated here"| F["task-guide container<br/>http://127.0.0.1:PORT"]
```

### `pi5.local` will not work

`.local` is **mDNS/Bonjour** — link-local only. It resolves when the phone is on
the same LAN and not at all over cellular, and it is not a MagicDNS name. Anything
Shortcuts-facing must use the MagicDNS FQDN (`pi5.<tailnet>.ts.net`) or the raw
`100.x` address. This is worth stating loudly because the issue text and the map
both say `pi5.local`, which is the *deployment* hostname, not the *client-facing*
one.

### Staying connected when idle/backgrounded

Official position: the iOS client "automatically configures a broad VPN On Demand
policy while Tailscale is enabled to ensure that the VPN remains active in the
event of a system restart, auto-update, crash or other event that might disable
the VPN"
([VPN On Demand](https://tailscale.com/docs/features/client/ios-vpn-on-demand)).
VPN On Demand rules can be set per interface type (Wi‑Fi: Always / Only On /
Except On / Never / Do Nothing; Cellular: Always / Never / Do Nothing), and MDM
deployments get an `AlwaysOn.Enabled` policy
([MDM for iOS](https://tailscale.com/docs/integrations/mdm/ios)).

**Uncertainty I could not close:** Tailscale's docs do *not* contain a plain
statement of the form "the tunnel stays up indefinitely when the app is
backgrounded." The VPN On Demand page explicitly says nothing about backgrounding.
The architectural argument is strong (a Network Extension is a separate system
process, kept alive by the VPN subsystem rather than by the app being foreground),
and the On Demand policy exists precisely to re-establish the tunnel if the
extension is killed. But the re-establishment path implies there *is* a window
where the tunnel is down and a request could fail. Practically this shows up as
"first request after the phone has been asleep for hours is slow or fails, second
one works." I found no official Apple or Tailscale document that quantifies this,
and I am not going to pretend otherwise — **it should be tested on the actual
iPhone 16 Pro before the intake story is finalised.**

One documented real cost: Tailscale acknowledges battery complaints, noting that
drain is "most commonly attributed to a device using an exit node for all traffic"
but that roughly 2% of iOS clients on the latest stable release still see
pronounced battery problems
([Mobile battery troubleshooting](https://tailscale.com/docs/reference/troubleshooting/mobile/battery-drains)).
`task-guide` does not need an exit node, so the common cause does not apply.

### Mitigation available: Tailscale ships Shortcuts actions

Since client version 1.36 Tailscale exposes native Shortcuts actions on iOS and
macOS — connect/disconnect, `Get Status`, exit-node toggling, profile switching
([Tailscale Shortcuts actions](https://tailscale.com/blog/ios-macos-shortcuts)).
This is a genuinely useful safety valve: a `task-guide` shortcut can begin by
ensuring Tailscale is connected before issuing the HTTP request, turning a silent
failure into a self-healing one. I have not verified the exact action names or
whether the connect action blocks until the tunnel is actually up — treat the
sequencing as something to work out hands-on.

---

## 2. HTTPS and certificates

### Is HTTPS required?

This is the least clearly documented area and I want to be honest about it.

**What is well established:** Shortcuts **cannot be told to skip TLS verification.**
An Apple Developer Forums thread reports POST requests to a host with a
self-signed/custom certificate failing with "there was a problem running the
shortcut," with the same request succeeding in Postman once "Enable SSL
certificate verification" is turned off
([forum thread 731834](https://developer.apple.com/forums/thread/731834)).
No Apple staff answered. There is no ATS-exception mechanism available to a
Shortcuts user — `NSExceptionAllowsInsecureHTTPLoads` and friends are `Info.plist`
keys belonging to a native app bundle, and the Shortcuts app's plist is Apple's,
not ours. **So: self-signed certificates are out.** Treat that as settled.

**What is genuinely unclear:** whether plain `http://` works. Apple's own guide to
`Get Contents of URL` says nothing about scheme restrictions
([Request your first API](https://support.apple.com/guide/shortcuts/request-your-first-api-apd58d46713f/ios)).
Community evidence conflicts: a large population of Home Assistant users routinely
POST to `http://<lan-ip>:8123/...` from Shortcuts and report it working, while
other forum threads report SSL/ATS-flavoured errors. Some of the failure reports
are confounded — e.g. a well-known case where the *same* action fails inside a
Home-app automation but works when the automation is built in the Shortcuts app
([forum thread 651963](https://developer.apple.com/forums/thread/651963)), which is
a host-resolution issue, not a TLS one. **I could not find any primary Apple
source that states the rule.** Do not design around plain HTTP working.

**Recommendation:** don't bet on it. Use real HTTPS. It costs almost nothing here
because Tailscale gives it away.

### Tailscale certificates

Tailscale provisions genuine Let's Encrypt certificates for MagicDNS names
([Enabling HTTPS](https://tailscale.com/kb/1153/enabling-https)):

- Requires MagicDNS enabled and "Enable HTTPS" toggled in the DNS admin console.
- Certificates are issued for `<machine>.<tailnet>.ts.net`.
- Private keys are generated and held on the machine — "Tailscale never sees them."
- **Trade-off to accept knowingly:** machine names are published to public
  Certificate Transparency ledgers. The tailnet domain obscures ownership, but
  `pi5.<tailnet>.ts.net` becomes public knowledge. Don't put anything sensitive in
  machine names. The *name* leaking is not the *service* leaking — it is still
  unreachable without tailnet membership.
- Certs expire after 90 days. Issued via `tailscale cert`, **renewal is manual.**

Because these are publicly-trusted certificates for a real domain, iOS trusts them
with no profile installation and no user intervention. Shortcuts is happy.

### Tailscale Serve is the better fit

[Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve) proxies a
local service onto the tailnet with HTTPS, and the daemon terminates TLS itself —
"the device's Tailscale daemon terminates the HTTPS connection," with the backend
spoken to over plain HTTP. Certificate lifecycle is handled by the daemon rather
than by a cron job you forget to write, which removes the 90-day manual-renewal
footgun from `tailscale cert`.

Deployment note: Serve's documented proxy target form is `http://127.0.0.1:PORT`,
and the docs state "only `http://127.0.0.1` is supported for proxies." Since
`task-guide` runs as a Docker Compose service, the natural shape is a **Tailscale
sidecar container sharing a network namespace with the app container**, so that
`127.0.0.1:PORT` from the sidecar's perspective is the app. Worth confirming with
the `pi5-devops` agent — I have not validated this against the existing DCM setup.

> ⚠️ **Never enable Funnel.** Serve and Funnel are the same subsystem with one flag
> different; Funnel publishes the service to the public internet. Given the "no
> auth" decision, an accidental Funnel would expose the entire task store to
> anyone. This deserves to be written down as an explicit non-goal in the deploy
> config, not just remembered.

---

## 3. What HTTP shapes Shortcuts handles comfortably

From Apple's guide
([Request your first API](https://support.apple.com/guide/shortcuts/request-your-first-api-apd58d46713f/ios),
[Parsing JSON](https://support.apple.com/guide/shortcuts/parsing-json-apdde2dfe749/ios),
[Intro to JSON](https://support.apple.com/guide/shortcuts/intro-to-using-json-apd0f2e057df/ios),
[Dictionaries](https://support.apple.com/guide/shortcuts/dictionaries-apd43b69f337/ios),
[Get Dictionary Value](https://support.apple.com/guide/shortcuts/get-dictionary-value-action-apdf01294032/ios)):

**Comfortable:**

- All five methods: GET, POST, PUT, PATCH, DELETE (under "Show More").
- Custom headers — arbitrary key/value pairs.
- Request bodies in three flavours: **JSON**, **Form**, or **File**. The JSON body
  editor is a key/value form, so a *flat* JSON object is trivial to build.
- Query parameters `?key=value`.
- Response JSON is auto-coerced to a Shortcuts dictionary; `Get Dictionary Value`
  pulls a key out, and `Get Dictionary from Input` converts raw text to a dictionary.
- Nested dictionaries and lists *are* addressable, but the guide's own framing —
  "raw JSON can be difficult to parse," recommending external formatters — is a
  tell that this is fiddly by hand.
- Displaying results: `Show Alert`, `Show Notification`, `Show Result`, `Speak Text`
  ([Intro to prompts](https://support.apple.com/guide/shortcuts/intro-to-using-prompts-apd8efa49a70/ios)).

**Uncomfortable / to design away:**

- Deeply nested response objects. Every level costs another `Get Dictionary Value`
  action that the user has to build and maintain in a touch UI.
- Arrays needing iteration + formatting. Doable via `Repeat with Each`, tedious.
- Multi-request choreography (fetch id, then post to it). Every extra round trip is
  another failure point when the tunnel is flaky.
- Anything requiring the user to interpret a status code. Shortcuts surfaces HTTP
  failures as a generic "there was a problem running the shortcut."

---

## 4. Realistic capture and query flows

### Quick capture

`Ask for Input` prompts for text at runtime
([Ask for Input](https://support.apple.com/guide/shortcuts/use-the-ask-for-input-action-apd68b5c9161/ios)).
The prompt shows the standard iOS keyboard, which carries the dictation
microphone — so **typed and dictated text are the same input path**; there is no
separate "dictate" action needed and no special handling required. The captured
text feeds straight into a JSON request body field.

Minimum viable capture shortcut is three actions: `Ask for Input` →
`Get Contents of URL` (POST, JSON body `{"text": <input>}`) → `Show Notification`.

### Quick query ("what can I do right now")

`Get Contents of URL` (GET, with query params describing the moment) →
`Show Result` or `Speak Text`. If the response carries a **pre-rendered display
string**, this is two actions and no dictionary spelunking.

### Launch surfaces (all confirmed in Apple's guide)

| Surface | Source | Notes |
|---|---|---|
| **Action button** | [Run shortcuts with the Action button](https://support.apple.com/guide/shortcuts/run-shortcuts-with-the-action-button-apdfea15680b/ios) | iPhone 15 Pro or later — the iPhone 16 Pro qualifies. Single press. Best fit for *capture*. |
| **Home Screen widget** | [Run shortcuts from a widget](https://support.apple.com/guide/shortcuts/run-shortcuts-from-the-home-screen-widget-apd029b36d05/ios) | Runs inline; the widget shows a progress indicator. Falls back to opening the Shortcuts app if an action can't complete in-widget or needs input — so an `Ask for Input` capture shortcut *will* bounce into the app. Best fit for *query*. |
| **Lock Screen / Control Center controls** | [What's new in Shortcuts for iOS 18](https://support.apple.com/en-us/121131), [Use Control Center to run shortcuts](https://support.apple.com/guide/shortcuts/apd06a9201d4/ios) | iOS 18 added Shortcuts controls placeable in Control Center and on the Lock Screen. |
| **Siri** | Shortcuts User Guide | Shortcut name is the invocation phrase. Useful for hands-free capture; note that Siri-run shortcuts that display UI behave differently from tapped ones. |

**Gap:** I could not retrieve the "Run shortcuts with Siri" page content directly
(the fetch returned only the guide's table of contents), so the details of
Siri-specific prompt/output behaviour are unverified. The name-as-phrase behaviour
is well known but I am flagging it as not primary-sourced here.

---

## 5. Automation triggers

Apple's [personal automation](https://support.apple.com/guide/shortcuts/create-a-new-personal-automation-apdfbdbd7123/ios)
categories are Event, Travel, Communication, Setting, and Transaction.

- **Time of Day** — [event triggers](https://support.apple.com/guide/shortcuts/create-a-personal-automation-with-an-event-trigger-apd932ff833f/ios)
  lists Time of Day, Alarm, Sleep, Apple Watch Workout, Sound Recognition.
- **Arrive / Leave a location** — travel triggers.
- **Open/Close an app** — app triggers.

**Uncertainty:** neither the event-trigger nor travel-trigger page I read documents
the "Run Immediately" vs "Run After Confirmation" toggle, or notification-on-run
behaviour. This matters a lot given the map's explicit **notification restraint**
value: an automation that fires with a confirmation banner is itself a
notification. I could not confirm from primary sources which trigger types support
silent immediate execution on current iOS. Verify on device.

**Design opinion, not a finding:** automations are a poor fit for the *core*
reminder loop. The map already says availability windows drive reminders and
Pushover is the delivery channel — server-side, where the restraint policy lives.
Duplicating window firing as iOS time-of-day automations would split the policy
across two systems. Where automations *do* earn their place is as **context hints**:
an "arrive home" automation POSTing a location-context signal, or a "leave work"
automation pre-warming a query. Keep them advisory.

---

## 6. No auth: what identifies the caller, and what's risky

### What identifies the caller

Nothing, by default. With no auth and Serve terminating TLS, the app sees a request
from a `100.x` source address. Options, in ascending order of effort:

1. **Nothing.** Single user, single tailnet. Consistent with the map's decision.
2. **Source IP.** Tailnet IPs are stable per device
   ([100.x addresses](https://tailscale.com/kb/1015/100.x-addresses)), so the
   phone is distinguishable from the laptop — useful for *logging* and for
   "is this request from a mobile context," not for authorization.
3. **Tailscale Serve identity headers.** Serve injects `Tailscale-User-Login`,
   `Tailscale-User-Name`, `Tailscale-User-Profile-Pic`
   ([Tailscale identity](https://tailscale.com/docs/concepts/tailscale-identity)).
   Free, no client-side work, no Shortcuts complexity. Even for a single user this
   is worth logging.

   **Caveat straight from the docs:** these headers are only trustworthy if the
   service listens on localhost, because "any user that can call your service
   directly (rather than with the Serve URL) could trivially provide their own
   values for these HTTP headers." With Docker this means the app port must not be
   published on the tailnet interface — only the sidecar reaches it.

4. **A shared secret header.** Would work (Shortcuts does custom headers fine) but
   contradicts the map's "no auth" decision and puts a secret in a synced shortcut.
   Not recommended.

### Risks worth flagging

- **Every tailnet device is fully privileged.** Any device on the tailnet can read
  and destroy the entire task store. Mitigate with **Tailscale ACLs** restricting
  the `task-guide` port to the owner's own devices, rather than relying on
  "it's my tailnet." Cheap, and it survives a future shared node or a borrowed
  device.
- **Device sharing / tailnet sharing** invites another account's device onto the
  tailnet. With no auth that device gets full access unless ACLs say otherwise.
- **Funnel.** Repeating this because it is the one mistake that converts "no auth"
  from a reasonable single-user choice into a public write endpoint.
- **Machine name in CT logs** (see §2). Accepted, but should be a conscious choice.
- **No replay/idempotency protection.** Shortcuts gives the user no feedback loop
  on a timeout; the human response to "did that work?" is to tap again. Without
  server-side dedupe that silently creates duplicate tasks. See below.

---

## 7. What this should change about the API design

Ordered by how much I'd argue for them.

1. **Serve the API on `https://pi5.<tailnet>.ts.net`, via Tailscale Serve.** Not
   `pi5.local`, not self-signed, not (probably) plain HTTP. This is the single
   load-bearing conclusion.
2. **One-round-trip endpoints.** A capture shortcut should be exactly one
   `Get Contents of URL`. `POST /capture` taking `{"text": "..."}` and doing all
   the interpretation server-side — no client-built property dictionaries, no
   fetch-then-post.
3. **Responses carry a pre-rendered display string.** Something like
   `{"message": "3 tasks fit: ...", "count": 3, "tasks": [...]}`. The Shortcut
   reads one key and shows it; the web UI ignores `message` and renders `tasks`.
   Keeps the top level **flat** — nesting is where Shortcuts gets painful.
4. **Never return 204 / empty bodies to Shortcuts paths.** Always a 200 with a
   human-readable `message`, including for the "nothing matches right now" case.
   Empty responses give the user no confirmation the thing worked.
5. **Idempotency.** Accept a client-supplied request id (Shortcuts can generate a
   UUID) and dedupe on it, so a re-tapped capture after a tunnel hiccup doesn't
   create two tasks. This is the direct API consequence of the tunnel-wake
   uncertainty in §1.
6. **Fast, bounded responses.** The user is standing there with the phone up.
   Keep the query endpoint's work small and cap it server-side.
7. **Log the Serve identity headers and source IP.** Free provenance for the
   "good application logging" requirement, and it distinguishes phone-originated
   from browser-originated writes.
8. **Every Shortcuts endpoint must have a UI equivalent** — the map's rule. In
   practice: `/capture`'s free-text interpretation must be reachable from the web
   UI too, not just its structured cousin.

---

## Open questions for the build session

- Does plain `http://` actually work from `Get Contents of URL` on current iOS?
  (Unresolvable from docs. 10-minute device test. Moot if Serve is used.)
- How long after the phone has been idle does the first tailnet request take, and
  does it ever hard-fail? (Determines whether shortcuts need a Tailscale-connect
  preamble action.)
- Do the Tailscale Shortcuts connect actions block until the tunnel is usable?
- Which automation triggers can run silently without a confirmation banner on
  current iOS?
- Does the Tailscale-sidecar-shares-network-namespace pattern fit the existing DCM
  Compose conventions on `pi5.local`? (Ask `pi5-devops`.)

## Sources

Tailscale:
[What is Tailscale](https://tailscale.com/kb/1151/what-is-tailscale) ·
[MagicDNS](https://tailscale.com/kb/1081/magicdns) ·
[Enabling HTTPS](https://tailscale.com/kb/1153/enabling-https) ·
[Tailscale Serve](https://tailscale.com/docs/features/tailscale-serve) ·
[Serve KB](https://tailscale.com/kb/1242/tailscale-serve) ·
[Tailscale identity](https://tailscale.com/docs/concepts/tailscale-identity) ·
[100.x addresses](https://tailscale.com/kb/1015/100.x-addresses) ·
[Install on iOS](https://tailscale.com/kb/1020/install-ios) ·
[VPN On Demand](https://tailscale.com/docs/features/client/ios-vpn-on-demand) ·
[MDM for iOS](https://tailscale.com/docs/integrations/mdm/ios) ·
[Mobile troubleshooting](https://tailscale.com/docs/reference/troubleshooting/mobile) ·
[Battery drain](https://tailscale.com/docs/reference/troubleshooting/mobile/battery-drains) ·
[Reimagining Tailscale for iOS](https://tailscale.com/blog/reimagining-tailscale-for-ios) ·
[Shortcuts actions](https://tailscale.com/blog/ios-macos-shortcuts)

Apple:
[Shortcuts User Guide](https://support.apple.com/guide/shortcuts/welcome/ios) ·
[Request your first API](https://support.apple.com/guide/shortcuts/request-your-first-api-apd58d46713f/ios) ·
[Parsing JSON](https://support.apple.com/guide/shortcuts/parsing-json-apdde2dfe749/ios) ·
[Intro to JSON](https://support.apple.com/guide/shortcuts/intro-to-using-json-apd0f2e057df/ios) ·
[Dictionaries](https://support.apple.com/guide/shortcuts/dictionaries-apd43b69f337/ios) ·
[Get Dictionary Value](https://support.apple.com/guide/shortcuts/get-dictionary-value-action-apdf01294032/ios) ·
[Ask for Input](https://support.apple.com/guide/shortcuts/use-the-ask-for-input-action-apd68b5c9161/ios) ·
[Intro to prompts](https://support.apple.com/guide/shortcuts/intro-to-using-prompts-apd8efa49a70/ios) ·
[Action button](https://support.apple.com/guide/shortcuts/run-shortcuts-with-the-action-button-apdfea15680b/ios) ·
[Home Screen widget](https://support.apple.com/guide/shortcuts/run-shortcuts-from-the-home-screen-widget-apd029b36d05/ios) ·
[Control Center](https://support.apple.com/guide/shortcuts/apd06a9201d4/ios) ·
[What's new in Shortcuts for iOS 18](https://support.apple.com/en-us/121131) ·
[Personal automation](https://support.apple.com/guide/shortcuts/create-a-new-personal-automation-apdfbdbd7123/ios) ·
[Event triggers](https://support.apple.com/guide/shortcuts/create-a-personal-automation-with-an-event-trigger-apd932ff833f/ios)

Community (explicitly *not* primary — used only where official docs are silent):
[Apple Developer Forums 731834 — self-signed certs fail in Shortcuts](https://developer.apple.com/forums/thread/731834) ·
[Apple Developer Forums 651963 — Home app vs Shortcuts app host resolution](https://developer.apple.com/forums/thread/651963)
