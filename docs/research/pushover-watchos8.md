# Pushover Apple Watch app on watchOS 8.8.x

Research for [#32](https://github.com/jpjerkins/task-guide/issues/32). Unblocks [#33](https://github.com/jpjerkins/task-guide/issues/33) (ambient/low-noise surfaces for Tasks).

**Question:** does Pushover's Apple Watch app run on an Apple Watch Series 3, which is frozen at watchOS 8.8.x?

**Answer:** yes, but by one point release. Pushover requires watchOS 8.7 or later; the Series 3 ceiling is watchOS 8.8.2. There is no margin, and the floor has moved before.

## Stated requirement

Pushover's App Store listing gives the Apple Watch compatibility line as:

> Requires watchOS 8.7 or later.

Verified identically on the [US](https://apps.apple.com/us/app/pushover-notifications/id506088175) and [UK](https://apps.apple.com/gb/app/pushover-notifications/id506088175) storefronts, against app version 5.0.6 (released 2026-08-08). `pushover.net/clients/ios` is not an independent source — it 302-redirects to this same App Store listing, so the App Store page *is* Pushover's primary statement of requirements.

The floor was set deliberately. The version 4.3 release notes (2025-09-10) read:

> Remove custom watchOS notification handler no longer needed — Increase minimum required iOS version to 15 and watchOS to 8

## Series 3 ceiling

watchOS 8 is the last major version for the Series 3. Apple continues to ship patch updates on that branch: [watchOS 8.8.1](https://support.apple.com/en-us/102074) (build 19U512, June 2023), and later [watchOS 8.8.2](https://support.apple.com/en-us/118389) (build 19U526, [March 2026](https://appleinsider.com/articles/26/03/24/watchos-8-and-watchos-5-get-minor-updates-with-imessage-fix)), both listing Series 3 as supported.

So 8.8.2 ≥ 8.7 and the app is compatible. The gap is two patch releases wide.

## A conflicting signal, resolved

Apple's iTunes lookup API reports Pushover's watch `supportedDevices` as `["Watch4-Watch4"]`, which reads like a Series 4 hardware floor that would exclude a Series 3.

It isn't. Sampling other apps with watch targets returns the identical single value — Things 3 and CARROT Weather both report exactly `["Watch4-Watch4"]` — while apps without a watch target return an empty list. The field behaves as a boolean "has a watchOS binary" marker here, not a per-model list, so it carries no information about the minimum series. Discounted.

*Caveat:* this is inference from sampling, not from Apple documentation. I could not find an Apple source defining the semantics of `supportedDevices` for watch apps. The App Store compatibility line is the better-grounded source, and it is the one Pushover controls.

## Known issues on watchOS 8 specifically

None found. Searches across Pushover's support knowledge base, their forums, and general Apple/MacRumors discussion surfaced no reports of watchOS 8 users being broken by a Pushover update, and no Pushover statement about dropping watchOS 8.

What does exist is a general, version-independent complication problem — relevant to #33 regardless of hardware:

- Complications are **severely rate-limited by watchOS**. Pushover warns that too-frequent updates cause the app to exceed its complication "budget" and simply stop updating. ([support](https://support.pushover.net/i56-apple-watch-complications-not-showing-notifications))
- Users do hit this. The documented workaround is to remove the complication from the Watch app and re-add it. ([support](https://support.pushover.net/i326-complications-not-updating))
- The [Glances API](https://pushover.net/api/glances) states the hard numbers: watchOS allows **50 updates per day**, and Pushover recommends **at least 20 minutes between calls**.

## Bearing on #33

The Glances API is a fit for an ambient Task count. It sends no notification, retains nothing, and exposes a `count` integer field intended for exactly this — alongside `title`, `text`, `subtext` (100 chars each) and `percent`. Apple Watch is currently its only supported widget target.

Two things to design around:

1. **The count is not live, it is periodic.** 50 updates/day with a 20-minute floor means a ~20–30 minute refresh at best. An ambient surface must be honest about being a lagging indicator; anything that reads as real-time will be wrong.
2. **The watchOS 8.7 floor is one Pushover release from stranding this hardware.** Pushover has raised the floor once already, and a bump to watchOS 9 would permanently cut off the Series 3 — no upgrade path exists. Any Glances surface should be treated as a nice-to-have that can vanish with a vendor update, not as load-bearing.

## Confidence

High on the requirement itself (watchOS 8.7, stated by the vendor, cross-checked on two storefronts, current as of app 5.0.6 / 2026-08-08) and on the Series 3 ceiling (Apple, two releases).

Lower on the *absence* of watchOS 8 problems. That rests on not finding reports, which is weak evidence — watchOS 8 users are a small and shrinking population unlikely to generate much public discussion. Absence of complaints is not evidence it works. **The only way to settle this is to install Pushover on the actual Series 3 and send a Glances call**, which is cheap and worth doing before building anything on #33.
