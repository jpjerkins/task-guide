# Where does a Shortcut invoked from an Apple Watch execute?

**Research question (GitHub issue #43):** Does a Shortcut invoked from an Apple Watch
(Series 3, watchOS 8.8.2) execute on the watch, or is it relayed to the paired iPhone?

**Researched:** 2026-08-17. Every claim below is dated, because watchOS-8-era behaviour is
poorly documented and the surrounding writing spans several very different eras.

---

## Summary of what the evidence shows

1. **The Shortcut itself runs on the watch.** Since watchOS 7 (2020) the watch has its own
   Shortcuts app and runs shortcuts locally. There is no whole-shortcut relay to the phone.
2. **A per-*action* remote-execution mechanism does exist**, but it is scoped to *third-party
   app intents* whose Intents extension is not installed on the watch. It does not apply to
   Shortcuts' own built-in actions such as `Get Contents of URL`.
3. **`Get Contents of URL` and `Ask for Input` are both permitted on the watch** — `Ask for
   Input` is explicitly endorsed by Apple for Apple Watch; `Get Contents of URL` is not on
   Apple's avoid-list.
4. **The crux (does the HTTP request transit the phone's network stack, and therefore the
   phone's Tailscale tunnel?) is NOT resolved by the available sources.** Apple documents that
   watch network requests are proxied via the paired iPhone, but there is credible evidence
   that this proxying does *not* inherit the phone's network configuration in at least one
   analogous case (Wi-Fi proxy settings). I found no primary source either way for a
   `NEPacketTunnelProvider` VPN such as Tailscale.

---

## 1. Where a watch-invoked Shortcut executes

### watchOS 7 introduced on-watch execution (2020)

Apple's WWDC20 session
[*Create quick interactions with Shortcuts on watchOS* (session 10190, June 2020)](https://developer.apple.com/videos/play/wwdc2020/10190/)
is the primary source. It describes how the watch decides where to run a shortcut's
constituent work:

- **NSUserActivity-based shortcuts** — "can only be run directly on the watch"; they cannot be
  executed remotely on the paired phone, and error out if the app is not installed on the watch.
- **Intent-based shortcuts** — if an Intents extension is installed on the watch and supports
  the intent, it "handles the intent locally on the watch". If there is no Intents extension on
  the watch, the intent "is executed remotely on the paired phone" — Apple's own term is
  **remote execution**.
- Remote execution requires the intent to run entirely in the background; if the phone-side
  handler returns `.continueInApp`, remote execution fails with an error.
- Apple notes local execution is fastest, remote execution is "slower due to the additional hop
  for sending data to the phone".

[AppleInsider, 2020-06-26](https://appleinsider.com/articles/20/06/26/shortcuts-can-run-locally-on-apple-watch-in-watchos-7)
summarised the same session as: "Watch can run shortcuts locally without a connected iPhone."

**Reading:** the *shortcut* runs on the watch. What can be relayed is an individual
**app intent** contributed by a third-party app that has no watch-side extension. `Get Contents
of URL` and `Ask for Input` are built into the Shortcuts app itself, which is present on the
watch, so neither is a candidate for this remote-execution path. Siri on the watch is a
*trigger*; it does not change the execution site.

⚠️ **Caveat:** WWDC20 session 10190 is a *developer-facing* session about adding intents to
your own app. It does not state in so many words "built-in Shortcuts actions never relay". The
inference above is strong but is an inference, not a quoted guarantee.

### Apple's user-facing docs agree, indirectly

- [Use shortcuts on Apple Watch (Apple Watch User Guide)](https://support.apple.com/guide/watch/shortcuts-apd99050d435/watchos)
  — "When you add a new shortcut on your iPhone, the shortcut also appears on Apple Watch (if
  the shortcut is compatible)" and "Not all shortcuts on iPhone are compatible with Apple
  Watch." A per-shortcut compatibility filter only makes sense if the watch is the thing running
  it. A pure relay would make every shortcut compatible.
- [Run shortcuts from Apple Watch (Shortcuts User Guide)](https://support.apple.com/guide/shortcuts/run-shortcuts-from-apple-watch-apd5888b0858/ios)
  — describes running a shortcut in the watch's Shortcuts app or from a complication. It
  describes **no** relay mechanism.

### The one documented relay-ish behaviour: "open the Shortcuts app to finish the job"

The watchOS-8-era version of Apple's page —
[*About actions in complicated shortcuts*, Shortcuts User Guide v5.0 / iOS 15.0](https://support.apple.com/en-gb/guide/shortcuts/apd081d9d61f/5.0/ios/15.0)
(this is the correct-era document: guide 5.0 ships alongside iOS 15 / watchOS 8) — warns that
when running shortcuts from a widget or from Apple Watch, some actions "open the Shortcuts app
to finish the job". The listed problem actions are:

- actions that preview content (e.g. Quick Look)
- actions that **use the camera or microphone**
- actions that ask you to select photos, music or contacts
- actions using the **Ask Each Time** variable
- actions with *Show Compose Sheet* or *Show File Picker* turned on

Neither `Get Contents of URL` nor `Ask for Input` is on that list.

⚠️ **Ambiguity, unresolved:** "open the Shortcuts app" does not say *which device's* Shortcuts
app. The same page also mentions that on an "Unable to Load" memory error you can add a
*Continue Shortcut in App* action, which "redirects processing to the full app". On the watch
this most plausibly means the watch's own Shortcuts app, not the phone's — but the wording is
genuinely device-agnostic and I could not find a source that pins it down.

### Anything change across watchOS 7 → 8 → 9?

- **watchOS 7 (Sept 2020):** Shortcuts app introduced on Apple Watch; local execution model as
  above. ([WWDC20 session 10190](https://developer.apple.com/videos/play/wwdc2020/10190/))
- **watchOS 8 (Sept 2021):** the `Open URLs` action stopped working for `https:` URLs on the
  watch, erroring with `Shortcuts could not open the app for the URL scheme "https"`; the
  workaround was `Show Web Page`.
  ([Apple Community thread 253186723, reported 2021-09-26, resolved 2021-09-30](https://discussions.apple.com/thread/253186723))
  Note this is `Open URLs` (which needs a browser — the watch has none), **not** `Get Contents
  of URL` (a background HTTP request, no browser involved). It is not evidence against
  `Get Contents of URL`.
- **watchOS 9 (Sept 2022):** I found **no** primary source describing a change to the Shortcuts
  execution model. Apple's [watchOS 9 newsroom post (2022-06-06)](https://www.apple.com/newsroom/2022/06/watchos-9-delivers-new-ways-to-stay-connected-active-and-healthy/)
  does not mention Shortcuts execution. Absence of evidence only.

**Conclusion for (1) and (2): the Shortcut runs on the watch, in watchOS 7, 8 and 9 alike.**
Confidence: **high**.

---

## 2. The crux: does the watch's HTTP request use the *phone's* network stack?

This is the question that actually decides the issue, and it is **not** the same as question 1.
Relaying the *network call* is different from relaying the *Shortcut*.

### What Apple documents

Apple's WatchKit "Testing watchOS Networking" guidance (quoted repeatedly in developer
discussions; the original page is no longer served at a stable URL I could fetch) states:

> "If the watch's paired iPhone is connected, NSURLSession connections will use the iPhone as a
> proxy. If the watch cannot connect to a paired iPhone, but it can connect to a known WiFi
> network (a network that the user has previously logged into with their phone), then the
> request is sent using the WiFi network."

Quoted verbatim by developers in
[Apple Developer Forums thread 107964, "WatchOS URLSession does not work without paired iPhone?" (opened Aug 2018; still active Jul 2023)](https://developer.apple.com/forums/thread/107964).

⚠️ **Source-quality flag:** I could not fetch the original Apple page — every instance I found
is a second-hand quotation (developer forums, and
[*Developing for Apple Watch*, O'Reilly](https://www.oreilly.com/library/view/developing-for-apple/9781680501940/f_0055.xhtml),
which returned HTTP 403). The wording is consistent across sources, which is reassuring, but
this is **not a directly verified primary source**.

The mechanism appears to be an IP-level tunnel from watch to phone over the companion link
(described in developer discussions as the `ipsec1` interface), escalating from Bluetooth LE to
Wi-Fi/AWDL when bandwidth demands it.

### Evidence that the proxying does NOT inherit the phone's network config

[Apple Developer Forums thread 652997 (Jul 2020, watchOS 6+)](https://developer.apple.com/forums/thread/652997):

> "I have setup wifi proxy on iPhone and paired to my apple watch, so when i call API from apple
> watch it does not go through proxy setup on iphone instead it throws error unable to connect
> to server with error code -1004 … this issue happens only in apple watch Device running
> watchOS 6.0 or later"

The poster also could not see any watch traffic in Charles. **No Apple engineer replied**
(0 replies, single participant), so this is one unconfirmed user report — but it is directly
on-point and nothing contradicts it.

Related: [thread 730235 (May 2023, watchOS 9.4)](https://developer.apple.com/forums/thread/730235)
— an independent watch app gets `NSURLErrorDomain Code=-1009 "The Internet connection appears
to be offline."` specifically *when routing via the iPhone proxy*, while working fine on the
watch's own Wi-Fi or cellular. Rebooting the iPhone temporarily fixes it. No Apple reply.

Also relevant: [thread 107964](https://developer.apple.com/forums/thread/107964) shows
developers reporting the documented Wi-Fi fallback simply does not work — one comments "the
Apple documentation is simply wrong". An Apple Systems Engineer replied in **Jul 2023** with
troubleshooting steps rather than a correction. So the documented model is known to diverge from
observed behaviour even in Apple's own forums.

### Does the phone's Tailscale tunnel apply?

**I could not establish this either way, and I am not going to guess.**

The two plausible models produce opposite answers:

| Model | Mechanism | Does Tailscale apply? |
|---|---|---|
| **A — phone-side routing table** | Watch traffic arrives over the companion link and the phone routes/NATs it using its own route table, in which Tailscale's `NEPacketTunnelProvider` has claimed `100.64.0.0/10`. | **Yes** — the watch could reach a Tailnet host. |
| **B — interface-scoped egress** | Watch traffic is bound to a specific phone egress interface (Wi-Fi/cellular) and bypasses per-interface and VPN configuration, as the Wi-Fi-proxy report suggests. | **No** — the request leaves the phone outside the tunnel and never reaches the Tailnet. |

The Wi-Fi-proxy evidence (thread 652997) leans toward **B**, but a Wi-Fi proxy setting is an
interface-scoped HTTP proxy, whereas a packet-tunnel VPN installs *routes*. They are not the
same mechanism, so that report does not transfer cleanly.

Supporting the general direction of B: Apple's
[VPN overview for Apple device deployment](https://support.apple.com/guide/deployment/vpn-overview-depae3d361d0/web)
states that Apple Watch pairing isn't supported with Always On VPN, and that some system traffic
takes place outside a VPN. This is about supervised Always On VPN, not Tailscale, so treat it as
weak, adjacent evidence only.

**Confirmed and uncontested:** watchOS ships **no Tailscale client**
([Tailscale iOS install docs](https://tailscale.com/kb/1020/install-ios) list iPhone and iPad
only; no watchOS target). So the watch cannot join the tailnet in its own right under any
scenario. The only possible path is inheriting the phone's tunnel.

I found **no** first-hand report, Apple document, or Tailscale document confirming or denying
that a watch HTTP request reaches a Tailnet-only host via the paired phone. This is the single
biggest gap in this research.

---

## 3. Can the watchOS 8 Shortcuts app run `Get Contents of URL` and `Ask for Input`?

- **`Ask for Input` — yes, explicitly.** The watchOS-8-era
  [*About actions in complicated shortcuts* (guide 5.0 / iOS 15.0)](https://support.apple.com/en-gb/guide/shortcuts/apd081d9d61f/5.0/ios/15.0)
  names `Ask for Input` as one of four actions that work well "in the Shortcuts widget or on
  Apple Watch", alongside Clipboard actions, `Choose from List` / `Choose from Menu`, and
  `Show Alert`.
- **`Get Contents of URL` — not prohibited, but not explicitly blessed either.** It appears on
  neither Apple's works-well list nor its avoid list. I could **not** find an Apple per-action
  platform-availability table covering watchOS for this action; Apple's Shortcuts User Guide
  does not publish one at the granularity the question assumed. The action's own guide page,
  [Request your first API](https://support.apple.com/guide/shortcuts/request-your-first-api-apd58d46713f/ios),
  is iPhone/iPad-scoped and says nothing about watchOS.

⚠️ Note for the wrist-capture use case specifically: Apple's avoid-list **does** include actions
that "use the camera or microphone". A dedicated `Dictate Text` action therefore falls in the
warned category. `Ask for Input` on the watch surfaces watchOS's own input UI (scribble /
dictation), which is the endorsed route — this matters if the spec's capture step was going to
reach for `Dictate Text`.

---

## Verdict

**The evidence supports the "runs on the watch" branch.** A Shortcut invoked by Siri on an
Apple Watch running watchOS 8.x executes in the watch's own Shortcuts app. It is not handed
wholesale to the paired iPhone. Apple's only documented relay — "remote execution" from WWDC20 —
covers third-party app intents lacking a watch-side extension, not Shortcuts' built-in actions.

**Confidence: high** for the execution-site question (1) and for its stability across
watchOS 7 → 9 (2).

**However, this does not by itself settle the issue**, because the decisive question is (3),
and (3) is **inconclusive**:

- watchOS network requests are documented to be proxied through the paired iPhone when it is
  connected.
- Whether that proxying puts the request inside the phone's Tailscale `NEPacketTunnelProvider`
  tunnel is **unknown**. The one on-point data point (the phone's Wi-Fi proxy is *not* honoured
  for watch traffic) points toward "no", but concerns a different mechanism.
- **Confidence that a watch Shortcut can reach a Tailnet-only host: low, leaning negative.**

**Recommendation:** treat wrist capture as **not viable without an on-device test**. Do not
write the spec on the assumption it works.

### The five-minute on-device check that settles it

On the Series 3 (watchOS 8.8.2), paired, in Bluetooth range, with Tailscale connected on the
iPhone and `task-guide` running on pi5:

1. Build a two-action shortcut on the iPhone: `Get Contents of URL` pointing at the
   **Tailscale-only** URL (use the `100.x.y.z` Tailnet IP or MagicDNS name for pi5, not a LAN
   IP and not a public hostname), followed by `Show Alert` displaying the result.
2. Enable it for Apple Watch (Shortcuts app on iPhone → Shortcuts menu → Apple Watch).
3. Run it **from the watch's Shortcuts app**, with the iPhone unlocked and Tailscale showing
   connected.

Outcomes:
- **Alert shows the expected response body** → the watch request transits the phone's Tailscale
  tunnel. Wrist capture is viable; model B is wrong.
- **Alert shows a connection/timeout error** → the request leaves the phone outside the tunnel
  (or never leaves the watch). Wrist capture is ruled out; close #43 as out of scope.

Two useful controls, if the first result is ambiguous:
- Same shortcut against a **public** URL — if that fails too, the failure is about `Get Contents
  of URL` on watchOS 8, not about Tailscale, and the test is invalid.
- Same shortcut against pi5's **LAN** IP with the watch on the same Wi-Fi — distinguishes "watch
  used its own Wi-Fi" from "watch used the phone".

---

## Sources

Primary (Apple):
- [Create quick interactions with Shortcuts on watchOS — WWDC20 session 10190](https://developer.apple.com/videos/play/wwdc2020/10190/) (June 2020)
- [About actions in complicated shortcuts — Shortcuts User Guide 5.0 / iOS 15.0](https://support.apple.com/en-gb/guide/shortcuts/apd081d9d61f/5.0/ios/15.0) (watchOS 8 era)
- [About actions in complicated shortcuts — current](https://support.apple.com/guide/shortcuts/about-actions-in-complicated-shortcuts-apd081d9d61f/ios)
- [Run shortcuts from Apple Watch — Shortcuts User Guide](https://support.apple.com/guide/shortcuts/run-shortcuts-from-apple-watch-apd5888b0858/ios) and its [4.0 / iOS 14.0 version](https://support.apple.com/my-mm/guide/shortcuts/run-shortcuts-from-apple-watch-apd5888b0858/4.0/ios/14.0)
- [Use shortcuts on Apple Watch — Apple Watch User Guide](https://support.apple.com/guide/watch/shortcuts-apd99050d435/watchos)
- [Use Siri to run shortcuts](https://support.apple.com/en-ie/guide/shortcuts/apd07c25bb38/ios)
- [Request your first API in Shortcuts](https://support.apple.com/guide/shortcuts/request-your-first-api-apd58d46713f/ios)
- [VPN overview for Apple device deployment](https://support.apple.com/guide/deployment/vpn-overview-depae3d361d0/web)
- [watchOS 9 newsroom announcement](https://www.apple.com/newsroom/2022/06/watchos-9-delivers-new-ways-to-stay-connected-active-and-healthy/) (2022-06-06)

Apple Developer Forums / Communities (user reports, dated, mostly without Apple replies):
- [Thread 107964 — WatchOS URLSession does not work without paired iPhone?](https://developer.apple.com/forums/thread/107964) (Aug 2018 – Jul 2023; contains the quoted Apple networking doc and a Jul 2023 Apple Systems Engineer reply)
- [Thread 652997 — iPhone Wi-Fi proxy not applied to Apple Watch traffic](https://developer.apple.com/forums/thread/652997) (Jul 2020, unanswered)
- [Thread 730235 — independent watch app cannot request through iPhone proxy](https://developer.apple.com/forums/thread/730235) (May 2023 – Apr 2024, unanswered)
- [Apple Community 253186723 — watchOS 8 broke shortcuts with https URLs](https://discussions.apple.com/thread/253186723) (Sept 2021)

Third-party:
- [AppleInsider — Shortcuts can run locally on Apple Watch in watchOS 7](https://appleinsider.com/articles/20/06/26/shortcuts-can-run-locally-on-apple-watch-in-watchos-7) (2020-06-26)
- [Tailscale — Install Tailscale on iOS](https://tailscale.com/kb/1020/install-ios) (no watchOS target listed)
- [Developing for Apple Watch — Making Network Requests on Apple Watch](https://www.oreilly.com/library/view/developing-for-apple/9781680501940/f_0055.xhtml) (HTTP 403 on fetch; cited via search excerpt only)

#AI-generated
