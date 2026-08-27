# Front-to-back contract tests

Playwright (Chromium), driving the real SPA against a live API — **not** type generation alone
(#6). `openapi-typescript` keeps the TS types honest about shapes; these keep the app honest about
behaviour. Verified to have solid ARM64/Debian-12 support, so this runs on pi5 as well as the
Macbook.

No CI gate and no hosted pipeline: single-developer project, run on demand on whichever machine is
in use.
