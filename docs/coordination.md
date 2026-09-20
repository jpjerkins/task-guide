# Project coordination

> FINAL HANDOFF — 2026-09-14 17:13 CDT: this supervisor is stopped at Phil's request.
> Read [agent-status.md](agent-status.md) for current plan, agents, blockers and next actions.
> Future supervision belongs to a Sol scheduled task. Old automation
> `supervise-task-guide-pilot` is PAUSED/retired. All historical instructions below to
> resume workers or restore this supervisor's heartbeat are superseded. No new work now.
>
> SUCCESSOR SWEEP — 2026-09-14 18:06 CDT: scheduled supervisor
> `task-guide-supervisor-sweep` reconstructed the handoff, verified current Codex capacity,
> and resumed #138 with one Luna worker in the preserved worktree. The historical "no new
> work now" statement describes the stopped predecessor and is no longer current.
>
> DIRECT INTEGRATION POLICY — 2026-09-14 21:44 CDT: Phil authorizes completed, validated,
> reviewed task commits to be fast-forwarded/merged directly to `main` and pushed. Do not use
> PRs for this workflow. Deployment, deletion, reset, secrets, spending, and product-scope
> decisions remain separately controlled.

Coordinator task: `01a09e2a-32f4-77c3-b363-bd641670f57c` (Codex app).
Heartbeat: `supervise-task-guide-pilot` (display name Supervise task-guide); ACTIVE, every30minutes.
Started 2026-09-14. Read this record when continuing project supervision; refresh live
state before acting. GitHub issues remain the authority for ticket scope and dependencies.

## Authority and roles

Phil authorized supervised coordination, agent conversations, sequencing, implementation,
and detailed reviews. Opus provides architectural guidance; Phil decides product behavior,
priorities requiring a tradeoff, and consequential approvals.
**Agent labels decide who implements a ticket, and the other agent reviews it (Phil, 2026-09-15;
supersedes every earlier role statement in this file, `agent-status.md` and #53).**
- **Implementation:** `agent:codex` → a Codex worker (Luna) claims and implements it. `agent:claude`
  → a Claude Code worker (Sonnet) claims and implements it; Codex must not implement, fix, commit
  to, or start a worktree for it. Claude is a full implementation agent, not only an adviser.
- **Review: the agent that did not implement a ticket reviews it.** Codex-implemented work gets its
  independent review from Claude Code; Claude-implemented work gets it from Codex. The reviewer
  reports findings; the implementing agent fixes them. Never self-review to save the other
  provider's capacity — if the reviewer is below reserve, record the ticket as waiting for review.
- **No `agent:` label → ask Claude with Opus to decide the assignment** (implementation lane and
  which provider reviews), then apply that label to the issue before starting it. Do not ask Phil
  and do not guess from the lane plan alone. Check labels live with
  `gh issue view <n> --json labels` before every claim, resume, dispatch, commit or close.
- Labels follow the plan's lane split (`docs/superpowers/plans/2026-09-03-application-layer.md`
  § lanes): Claude owns Integration, Adapters, Web-Now and Web-Authoring; Codex owns Firing,
  Schedule and Capture & Tasks. A ticket that needs a file in the other agent's lane (e.g.
  `App.tsx`) stops and reports it, per the plan's ownership rules. **`schema.d.ts` is the one
  exception (#171):** it is generated, not authored, so the lane that changes the API document
  regenerates it in the same commit (`npm run gen:api` from `src/TaskGuide.Web`, API on 8007) and
  re-runs `./scripts/check-schema-drift.sh` after rebasing onto `origin/main`, before the ff-merge.
  A lane whose diff touches no API surface still reports pre-existing drift rather than absorbing
  it — only the lane that changed the document owns regenerating for it.

Phil's latest authorization (Sep 14): "You don't need my approval for changes completed
according to the GitHub issues. Run." Issue-compliant staging, commits, PRs, merges and
pushes are now authorized; no repeated approval gate for those actions. New product scope,
deployment, deletion, credit redemption, spending and permission changes remain separate.
Do not interrupt Phil's existing sessions. Reuse an idle ticket session only after checking
its handoff and worktree. Preserve files and checkpoints across limits; integrate issue-compliant
work after validation and review. Do not interpret the latest authorization as deployment,
deletion, reset-credit redemption, paid overflow, or permission-change approval.

## Capacity and recovery — supervised pilot policy

- At most one implementation worker **per provider** at a time; a Claude worker and a Codex
  worker may run in parallel on disjoint tickets. Consult Opus for architecture questions;
  give findings and a bounded question, not a demand to reread the whole repository.
- Before dispatch, resume, or review, read Codex account limits with `get_usage_limits`
  and Claude's `/usage` via the Claude Code CLI. Both providers' short and
  weekly windows matter. Record observation time, percent remaining, and reset time.
- Reserve floors: 20% remaining in the short/5-hour window and 5% remaining in the weekly
  window. When approaching either floor, or when a provider warns about exhaustion, request a
  checkpoint at the next safe boundary. At or below the applicable floor, launch no more work
  on that provider. Unknown or stale limits are not spare capacity; refresh them before starting
  work. The current supervisor prompt is authoritative if older historical entries below record
  different thresholds.
- Codex workers and this coordinator share allowance. Include supervision cost; do not
  infer remaining subscription capacity from token totals or process/session age.
- During active work use compact status checks, at most every 30 minutes routinely,
  and fresh limit checks at phase boundaries. Avoid repeatedly waking idle agents.
- A checkpoint records ticket, session ID, worktree, branch, base/head, dirty paths,
  completed/unmet acceptance criteria, validation log paths, review target/findings,
  decisions and next action. Save red/green output when produced, not at the end.
- A reported reset is a time to recheck, not proof of replenishment. Do not resume a
  weekly-limited provider merely because its short window reset. Never use reset
  credits or paid overflow without Phil's explicit permission.
- If Codex cannot run, its heartbeat cannot be assumed to supervise anything. Workers
  must have bounded work and stop conditions; this file supports recovery after reset.
  No independent always-on scheduler has been built or tested.

## Observed capacity

2026-09-14 approximately 07:15 America/Chicago (latest observation):

| Provider | Short window remaining / reset | Weekly remaining / reset | Source |
|---|---|---|---|
| Codex | 21%; Sep 14 12:07:55 CDT | 66%; Sep 19 09:08:10 CDT | Account limit tool, ~07:21 CDT |
| Claude | 83%; Sep 14 07:40 CDT | 72%; Sep 17 02:00 CDT | Live CLI `/usage` |

Codex's short-window remaining fell from 97% to 62% during setup; attribution is unknown
because allowance is account-wide. Keep future supervision compact: avoid full transcripts,
large tool documentation and broad rescans. Workers also check limits at phase boundaries.

Claude is signed in through its Pro subscription; usage credits are off. The sandbox's
initial `auth status` false result was an access artifact: normal host auth status is true.
Do not inspect credential files or keychain values to check usage.

**Historical (Sep 14, superseded 2026-09-15).** The description below predates the "Authority
and roles" rule above: Opus is now consulted for architecture/adviser questions and for deciding
an unlabeled ticket's `agent:` assignment, not as the sole way Claude participates. Do not read
"tools disabled" as a current constraint on Claude's role.

Dedicated Opus adviser: `7e8603a2-a859-4e32-9775-43320dd75d5d`, named
`task-guide-coordination-advisor`, started with tools disabled and no MCP servers.
Its first long terminal input was truncated; the corrected brief was resent using
bracketed paste. Verify actual transcript content before trusting a response.
The adviser exited normally after consulting and checking usage; resume by exact UUID
with tools disabled and no MCP servers. The prior terminal handle is no longer live.

## Work register

> **Superseded 2026-09-15 (Phil).** This table is a Sep 14 snapshot; don't maintain it. The live
> register is GitHub: open `build` issues, their `agent:*` labels and native blockers
> (`gh issue list --label build --state open --json number,title,labels`), with the plan's Ticket
> index as the lane/agent map.

| Ticket | Owner / state | Worktree and next action |
|---|---|---|
| #101 | Claude handoff; blocked by #137 and #138 | `/Users/phil/dev/task-guide-reminder-page`, `web-now/reminder-page`, clean `f75a565`; preserve unchanged. Component, tests and inventory only; unmerged/unpushed. |
| #130 | Server phase delivered; schema regenerated; checkpoint requested for low allowance | `/Users/phil/dev/task-guide-duration-bucket-wire`, `capture-tasks/duration-bucket-wire`, base/head `2a979c5`; uncommitted server/schema/test/inventory changes. Review-proof fixes queued. |
| #137 | Unclaimed; architectural sequencing needed | Shell needs ReminderPage, which is only on #101's unmerged branch. Resolve integration order before dispatch. |
| #138 | Unclaimed; now natively blocked by #130 | Five Duration buckets must survive write and subsequent reads; route/refusal semantics and explicit ownership still need resolution. |
| #132 | Blocked by #130 | Separate unsnapped-create defect; do not fold into #130. |
| #136 | Ready fallback pilot | Small malformed Pattern request fix; deprioritized in favor of completing #101's path. |

The authoring-reads worktree is clean with no unique commits; leave it alone.
Main was clean at `2a979c5`, matching GitHub at assessment. The coordinator's only
main-clone edit is this record; implementation always uses the ticket's worktree.

## Acceptance and handoff

Latest #101 handoff: https://github.com/jpjerkins/task-guide/issues/101#issuecomment-5663594326
It reports 147 Web tests, build/lint green and .NET tests before final frontend fixes.
Original coder red output was lost; seven of eight later mutation checks were caught.
These are reported results, not a new verification by the coordinator.

Do not close #101 for component completion. Deep links and working inline Duration triage
remain required. After dependencies land, preserve Phil's settled behavior: failed writes
re-read server state; no client clock; matching-on writes carry the whole authored tag set.
Review must name the exact worktree, base/head and changed files, and validate findings
against open tickets, ADRs, plan departures and the inventory before changing code.

## Decisions and next transition

Opus consultation completed: accepted #130 first, then #138, then #137/#101 integration.
Reason: longer is a legitimate bucket and subsequent reads must represent it before new
writes expand its use. #136 remains available but is off the direct #101 completion path.
The #130 worker has been resumed with server-only ownership and explicit capacity/checkpoint
instructions. Coordinator will arrange schema regeneration as a separate serialized phase
on the same isolated branch before final review; no stale-schema merge is planned.

Do not treat all Opus advice as decisions: rejected its reference to an existing Duration
write (none exists), its omission of Firing from #137 ownership, and silently converting
unknown Window identities to fallback (different fire identity). #138 route/refusal semantics
remain unsettled pending source inspection; no speculative status restrictions accepted.

#137 options: integrate #101's inert component without closing its issue, or stack a new
branch on its exact head while leaving the existing branch untouched. Neither merge is
authorized. Prepare a concrete reviewed integration proposal before asking Phil to approve.

Next: collect #130 server handoff, validate its evidence, run integration regeneration and
independent Codex review, then present the pilot result and required approval to Phil.
A thread heartbeat checks every 30 minutes and stays quiet unless there is meaningful change
or required user action. This is supervised follow-up, not a custom scheduling service.

Opus accepted the serialized same-branch regeneration on follow-up. Regenerate again after
any server review fix or schema-affecting rebase; inspect expected generated changes and run
frontend typecheck/tests. Never resolve generated-schema conflicts by hand.

Recorded #130 phase ownership: https://github.com/jpjerkins/task-guide/issues/130#issuecomment-5663717696
Verified native #130 -> #138 dependency added. Worker last observed active, no substantive
progress message yet; status cursor `bb358776-06b7-4c99-99da-b05407a93104:2`.

## Pilot checkpoint — Sep 14, ~07:22 CDT

The preceding worker-status observation is superseded. Server handoff received from worker
task `01a09fd6-99cb-70a0-ae42-960c0189fe5c` via the original #130 task. Local evidence:
`/Users/phil/dev/task-guide-duration-bucket-wire/.agent-handoff-130.md`.
Both int.Parse mappings removed; response Duration string? preserves bucket/null.
Original red output is recorded; focused tests passed; full-dotnet report quotes only
the API project's 84 tests, so retain all project summaries in the next validation.
Worker subsequently corrected the longer test name to match the inventory (focused1/1).

Coordinator regenerated schema from built API using isolated scratch data and no secrets/
Pushover credentials, localhost-only18307. Generator7.13.0 from lockfile. Only expected
TaskResponse and CaptureTaskResponse Duration changes (null|number|string -> null|string).
Artifacts: `/var/folders/3f/n3z06n912lq606lbcsnwzhnr0000gn/T/task-guide-130-schema-nw3c9apg/`.
Frontend independently passed:125 tests/19 files, build, lint. Exact logs:
`/private/tmp/task-guide-130-web-tests.log`, `task-guide-130-web-build.log`,
`task-guide-130-web-lint.log` in the same directory.

Independent code-review skill axes completed against the uncommitted server diff at2a979c5:
- Standards: no hard violation. Optional duplicate fixture helper is not a blocker; do not
  expand scope to clean it up. Full-suite handoff excerpt is incomplete, not proof of failure.
- Spec: no implementation defect. Regression proof missing for >60-minute capture returning
 201/longer and for capture-without-Duration plus subsequent GET both returning JSON null.
 Updated OpenAPI nullable-string test asserts only string; strengthen actual nullability
 for both response DTOs. This weakness predates the change but matters to this contract.

Sent ONE bounded verification-fix phase to original #130 task, then immediately superseded
it with CAPACITY CHECKPOINT after live usage reported21% short remaining. If edits already
started, worker must save next safe checkpoint and stop. Do not redispatch blindly: inspect
its latest message/files first. No more model work until actual allowance refresh after reset.
No credit redemption, no paid overflow. ~~Roles unchanged; do not spend Claude on detailed
implementation/reviews to evade the Codex reserve.~~ *(Superseded 2026-09-15: see Authority and roles.)*

Resume checklist: fresh usage; reconcile worker checkpoint; finish missing tests/inventory;
save all solution validation summaries; review only new test diff; regenerate only if server
or base changed. Keep ticket open and code uncommitted until final approval. #101 untouched.

Worker checkpoint received after the above: missing61-minute capture/null round-trip tests
and both DTO nullability assertions are now written; focused4/4 passed. Deliberate restoration
of old capture parsing gave503 vs expected201, then implementation restored. This was mutation
evidence, not initial TDD red. Worker reports20% short remaining at phase start, stopped without
full-suite rerun. On resume inspect this newest test diff and handoff; do not repeat already
completed test edits. Its statement that schema is pending is stale: coordinator completed it.

## Final pilot validation — Sep 14, ~12:14 CDT

Reset verified:98% short and63% weekly remaining at wake. Original worker is inactive;
no duplicate dispatched. Coordinator appended the missing null-roundtrip inventory bullet.
Independent Spec reviewer rechecked final regression tests and both generated response DTOs:
no unresolved findings. Original Standards review had no hard violations; optional helper
deduplication remains intentionally out of scope.

Important verification correction: `dotnet test --no-restore` ran API only in this worktree
because other test projects had not been restored. Do not call that a whole-solution pass.
Explicit `dotnet test task-guide.slnx` restored all projects and passed all595 tests:
Domain242, Application105, Infrastructure34, Storage128, API86; zero failures/skips.
Complete log: `/private/tmp/task-guide-130-final-solution.log`.
Previously verified Web125/125, build and lint remain applicable: subsequent changes were
tests/inventory only. Schema diff contains exactly the two expected Duration response changes.
GitHub main still2a979c52adc0508c33ff45f04978e7bc9518c3db, matching this worktree's base/head.

READY FOR APPROVAL, not merged/closed. Proposed commit scope: the seven tracked dirty files
in #130 worktree (two endpoints, schema, three API test files, inventory). Do not include
the untracked `.agent-handoff-130.md` or main-clone coordination record in the feature commit.
Await explicit approval to commit and merge/push main. No deployment proposed. After approval,
recheck main/ownership, integrate safely, verify remote, update ticket evidence, then evaluate
pilot and resume #138 contract work. #101 remains untouched and blocked until its work lands.

## Pilot integrated — Sep 14, 13:56 CDT

Phil authorized issue-compliant commits/merges/pushes, superseding the prior approval-wait
entries above. Seven feature files committed ase0c77e9f19cd66471a485e6777601b9f557ee7dd;
PR https://github.com/jpjerkins/task-guide/pull/139 merged as28780cb8d631b6e567c306421be404a1224234fa.
Verified GitHub #130 CLOSED and #138 zero open blockers; local main fast-forwarded to merge.
No deployment. Worktrees retained; no deletion authorized. Main's coordination record and
`.pr-130-body.md` are untracked coordination artifacts, not part of the feature commit.

Pilot evaluation: roles and cross-CLI advice worked; independent review caught missing
regression proof; quota monitoring stopped work and reset wakeup recovered it. Improvement:
restore/test the explicit solution to cover all projects, save complete test logs immediately,
and keep coordinator reads small. Allowance dropped quickly during large transcript/tool reads;
avoid repeating full context. Next: settle bounded #138 Duration-write contract with Opus,
name file ownership, then one Codex implementation worker in a new isolated worktree.

## #138 dispatched — Sep 14, ~14:02 CDT

Opus accepted bounded PUT `/api/tasks/{id}/duration`; exact contract and ownership posted:
https://github.com/jpjerkins/task-guide/issues/138#issuecomment-5669125863
Local copy: `/Users/phil/dev/task-guide/.issue-138-contract.md`.
Claimed138; new worktree `/Users/phil/dev/task-guide-task-duration`, branch`codex/task-duration`,
base28780cb8d631b6e567c306421be404a1224234fa. Active Codex collaboration worker`/root/implement138`
owns server command/endpoint/tests/inventory only, leaves uncommitted for coordinator review.
Coordinator owns serialized schema generation/validation/integration. Frontend npmci prepared.
Public test seams already agreed by issue/contract: command,IStore boundary,HTTP,OpenAPI.
Relevant plan may exist only in main checkout: `/Users/phil/dev/task-guide/docs/superpowers/plans/2026-09-03-application-layer.md`.

Latest worker capacity:46% short /55% weekly remaining. It is checking phase boundaries;
checkpoint at25%, no newphase at20%. Adviser/usage checked at13:58: Claude0% short used,
28% weekly used; short reset18:50Central, weeklySep17 02:00. Adviser exited normally.
~~No use of Claude for coding/review to evade Codex reserve.~~ *(Superseded 2026-09-15: see Authority and roles.)* No resetcredit/paid overflow.

## #138 capacity checkpoint — Sep 14, 14:35 CDT

Worker `/root/implement138` completed its turn and stopped at18% short remaining. Fresh
coordinator observation:13% short remaining,50% weekly; short resetSep14 17:10:53 CDT.
Do not resume now. Heartbeat rescheduled after that reset; verify fresh allowance first.

Command-only progress: `SetTaskDuration.cs`, its tests and inventory, all uncommitted.
14 focused command cases passed. HTTP route/request DTO, API/OpenAPI tests, command
preservation/idempotence/Status-transition tests, full solution validation, schema and
independent review are incomplete. Read worktree `.agent-handoff-138.md` and referenced
`.agent-138-*.log` files for exact evidence before resuming the SAME worker.

TDD departure explicitly recorded: first red was missing-class compilation failure, not
the required assertion red. Later deliberate omission of Duration assignment caused all5
bucket assertions to fail; restore gave green. This proves sensitivity but does not make
the original slice compliant red-first. Carry into review; do not rewrite history.

After reset: read limits, restore30minute heartbeat, follow up `/root/implement138` with
remaining bounded HTTP/preservation phase. No duplicate worker. Existing command edits are
preserved; no commits/schema changes yet. #130 remains merged; #101 untouched.
