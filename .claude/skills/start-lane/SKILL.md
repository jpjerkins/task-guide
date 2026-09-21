---
name: start-lane
description: Claim and work one ticket from the application-layer build plan (docs/superpowers/plans/2026-09-03-application-layer.md), safely, in a repo where several agents work concurrently on disjoint files.
disable-model-invocation: true
---

This skill does not decide anything the plan already settled. If a ticket looks wrong —
scope, ownership, blockers — **report that instead of redesigning it.**

The plan is `docs/superpowers/plans/2026-09-03-application-layer.md`. Read its "Global
constraints" (12 rules), the "Lanes" table, "Merge safety" and "Review gate" sections before
step 4 below — they are what this skill enforces, not summarized here twice.

Infer the GitHub repo from `git remote -v`; `gh` resolves it automatically inside the clone.

## 1. Pick the ticket

If the user named an issue number, use it — but still check it has zero open blockers (below)
before claiming it. Otherwise list what's takeable: open, labelled `build`, unassigned, zero
open blockers. `scripts/frontier.sh` does **not** apply here — these are plan tickets, not
wayfinder map children.

```sh
gh issue list --label build --state open --limit 100 \
  --json number,title,labels --jq '.[] | [.number, .title] | @tsv' |
while IFS=$'\t' read -r n title; do
  b=$(gh api repos/jpjerkins/task-guide/issues/$n --jq '.issue_dependencies_summary.blocked_by // 0')
  [ "$b" = 0 ] && echo "#$n  $title"
done
```

Present the result and let the user choose. **Refuse to proceed on a ticket with an open
blocker** — name the blocker instead.

## 2. Claim it — before any other work

```sh
gh issue edit <n> --add-assignee @me
```

This is the session's first write. An unassigned open ticket is unclaimed; assignment is the
claim, full stop.

## 3. Branch — always in its own git worktree

**Never work a ticket in the main clone.** Several agents work this repo concurrently; two
sessions switching branches in `/Users/phil/dev/task-guide` collide, and that has already
happened. Every ticket gets its own branch *and* its own worktree, one per ticket, no exceptions
— not even for a one-file change.

```sh
git -C /Users/phil/dev/task-guide fetch origin
git -C /Users/phil/dev/task-guide worktree add \
  /Users/phil/dev/task-guide-<short-slug> -b <lane>/<short-slug> origin/main
cd /Users/phil/dev/task-guide-<short-slug>
```

`<lane>` comes from the ticket's `lane:*` label (`lane:integration`, `lane:firing`,
`lane:adapters`, `lane:schedule`, `lane:capture-tasks`, `lane:web-now`, `lane:web-authoring`,
`lane:validation`). The worktree directory is a sibling of the main clone, named
`task-guide-<short-slug>` — matching the branch's slug so a stray worktree is traceable to its
ticket.

Rules that follow from this:

- **`git switch` in the main clone is off-limits for this skill.** The main clone stays on
  `main`; branch it out, don't switch it.
- Every path in the rest of this skill — `dotnet test`, `npm test`, `/code-review`, the commits
  — runs inside the worktree, and "repo root" means the worktree root.
- Check `git worktree list` first. If a worktree for this ticket's slug already exists, another
  session may hold it: report that rather than reusing or removing it. Never `worktree remove`
  or `git worktree prune` a directory with uncommitted work in it.
- The worktree needs its own `npm install` for web lanes (`node_modules` is not shared).
- The worktree is yours for the ticket's lifetime and you remove it yourself in step 8, once the
  work is merged and pushed. Until then it stays.

## 4. Read only what the ticket names

The ticket's own body, the plan's Global constraints, the ADRs it points at
(`docs/adr/README.md` first), and `CONTEXT.md` **only by the line ranges the ticket names**, via
`CONTEXT-INDEX.md`. Never read `CONTEXT.md` whole — it's ~122 KB.

## 4a. Brief the subagent from what you already read — Claude lanes only

*Codex works its tickets directly; skip this step there.*

Claude's `CLAUDE.md` requires implementation work to go to the `coder` subagent, so on a Claude
lane you do step 4's reading and someone else does steps 5–7. That hand-off is where a lane
wastes its budget, in one specific way: **you finish step 4, then tell the subagent to go read
the same ticket and the same ADRs.** It does — from cold, with no idea which paragraphs mattered
— and can burn a whole session limit re-deriving what you are holding. Seen on #76: the first
subagent hit its limit during exploration, having written nothing.

So the brief carries **findings, not a reading list**. Name the decision and its reason inline
(*"`SendReceiptAsync` returns `Task<bool>` — #69: adapters return an outcome, 'logged, never
retried' is the caller's policy"*), and cite the source as provenance the subagent can check, not
as homework it must do first. Point it at a document only where you genuinely need it to read
more than you did.

Two properties keep the brief cheap to recover:

- **Write it so it can be re-sent verbatim.** A subagent dies mid-ticket — budget, a crash, a bad
  turn — and a self-contained brief costs one re-dispatch to retry. A brief assembled across
  several conversational messages costs re-deriving the whole spec. Write the whole thing before
  you send any of it.
- **Tell it to commit each section as it lands.** Committing as work is verified is standing
  permission, addressed to *you*, and the subagent never sees this skill — so restate it in the
  brief, as crash-resilience
  rather than as permission. An interruption should then cost one section, not the run.

Scope each task so it can finish without asking questions, and dispatch genuinely independent
tasks in parallel — but never two that touch one file, which is the same hazard the plan's
disjoint-ownership rule exists to prevent, one level down. Review the diff yourself afterwards
and confirm the verification it claims actually ran; never take the report at face value. That
review runs under step 7a's gate, same as any other.

## 5. Work it TDD, red first

Write the test, run it, confirm it fails for the right reason — an assertion failure, not a
compile error or a fixture error. Only then implement. Quote the verbatim red and green output
in your final report.

Pure-rule tickets (Domain/Application logic, not adapters or endpoints) additionally need the
mutation drill: mutate the implementation, confirm red, revert, and name the mutation in the
report.

Test names come verbatim from `tests/TEST-INVENTORY.md`, snake_cased. Any test beyond the
inventory gets a new line appended to the inventory in the same commit.

## 6. Stay in the file lane

Touch only what the ticket's `Owns:` block names, **as extended by the plan's Merge safety rules**,
plus new test files it implies. Before reporting an ownership blocker, reconcile the needed file
with those rules: a ticket changing a signature owns every test call site of that signature, even
when the existing test file is not named in its `Owns:` block. If the work genuinely needs a file
another lane owns, **stop and report it rather than editing it** — that report is the signal a
contract was wrong. This includes any `.csproj` or `task-guide.slnx` change: the integration lane
owns every project file. Ask, don't edit.

`Api/Program.cs` is the standing case, and the answer is never an ownership amendment: a new
constructor dependency on a port makes API startup fail with *Unable to resolve service for type
`IPort`*. Report the registration you need — the concrete type and its ctor args — and the
integration lane wires it, usually the same day. Your lane's own tests still have to be green; API
tests red on that one DI error alone are not a blocker.

## 7. Before merging

```sh
dotnet test
```

green from the repo root. Web lanes additionally:

```sh
cd src/TaskGuide.Web && npm test
```

**If the diff touches `src/TaskGuide.Web/src/index.css` — or any of
`docs/prototypes/{ui-screens,schedule-editing,tag-entry}.prototype.html`, which are the guard's
other inputs** — run the prototype-union guard as well:

```sh
cd src/TaskGuide.Web && npm run check:css
```

It checks `index.css` against the union of `ui-screens` and `schedule-editing`, selectors *and*
declarations, plus `tag-entry`'s selectors where those two don't already define them. The two
remaining prototypes are **not** inputs and you cannot port class names from them on this guard's
authority: `override-authoring` links the real `index.css`, and `date-picker-probe` carries its own
throwaway design system, so every class in it would come back as `unexpected selectors`.

Nothing else runs the guard: there is no CI, and `npm test`, `npm run build` and `dotnet test` are
all green while `index.css` drifts (#178). It is not a formality — #148 tripped it, and #149, #176
and #177 all sharpened it. The trigger names the prototypes because a design lane that edits one
without touching `index.css` otherwise merges a red guard, and the next `index.css` lane inherits
a failure it did not cause.

Two failure shapes, and neither is fixed by editing around the guard:

- **Drift in a rule your change didn't touch** — a report, not an edit, on the same terms as the
  drift check below. Name it and leave it to the lane that moved it.
- **`unexpected selectors` on a class you added deliberately** — `index.css` is frozen for the Web
  lanes, so a design needing a class no prototype defines is *a report to the integration lane,
  not an edit* (`override-authoring.prototype.html`'s own header; #142 is one). That lane either
  ports the rule from a prototype or adds it to `retainedSelectors` in
  `scripts/check-css-union.mjs`.

**If the diff touches API surface** — any `Api/Endpoints/` file, or a response/request type one
serializes — you own regenerating `src/TaskGuide.Web/src/api/schema.d.ts`, **whatever lane you are
on** (#171). It is generated, not authored: merge safety protects intent, and this file has none.
Regenerate it in the same commit that changes the shape — **never hand-edit it**; a hand-patched
`schema.d.ts` is what produced #156. Then verify:

```sh
# one shell, from the worktree root — /data isn't writable on a dev machine
Storage__DataDir=$(mktemp -d) dotnet run --project src/TaskGuide.Api

# another shell
cd src/TaskGuide.Web && npm install && npm run gen:api
cd ../.. && ./scripts/check-schema-drift.sh   # after stopping the API above
```

`npm install` is needed even on lanes that otherwise touch no Node. **Check whose API is on 8007
before generating:** the port is hard-coded in `gen:api`, several worktrees share it, and a stray
listener from another branch will silently generate `schema.d.ts` from someone else's OpenAPI
document. `lsof -ti:8007` — if it isn't the `dotnet run` you just started, stop and sort that out
rather than killing it. The drift check needs the port free, so stop yours before running it.

The check boots the API, regenerates the types from the live `/openapi/v1.json`, and diffs them
against the checked-in copy. Non-zero means the checked-in copy is stale: the SPA is typed off a
contract the server stopped honouring, with no compile error and no test failure to say so (#135;
#98, #99, #133, #156 and #169 are the same drift caught late five times).

**If your diff touches no API surface, a non-zero exit is still a report, not an edit** — that drift
is someone else's, and absorbing it puts a generated TypeScript file in a diff that has no business
carrying one. Name it; the lane that changed the document regenerates for it.

**Write down what Phil settled in-session, before review runs.** A decision that lives only in the
conversation — an `AskUserQuestion` answer, an accepted consequence, a "yes, do it that way" — is
invisible to every reviewer, and comes back as a finding. Put it in a ticket comment, or in the body
of the commit it justifies, while it is still fresh. The merge commit is too late: review has
already run by then.

**Confirm the review target — and confirm it again afterwards.** `/code-review` runs as a forked
agent that inherits **the session's working directory, not the worktree you have been working in**.
Your `Bash` cwd is not the session's cwd: it resets between calls, so a `cd` into the worktree does
not follow the review out. When the session sits in the main clone, the review reads `main` and
reviews already-merged code — the branch is never opened.

It never announces this. It returns a full, confident, well-verified review of the wrong commits.
That has now happened twice: six findings against `main` on #115, and seven findings against the
merged #111 web shell during #72 — the second time *after* the target check below passed, because
the check and the review were looking at different repositories.

So the check below is necessary and not sufficient. Do all three:

```sh
git diff origin/main --stat
```

From inside the worktree, non-empty, and the paths are the ones the ticket owns. Otherwise the
target is wrong; fix that before launching anything.

**Name the branch in the invocation** — never rely on an inherited cwd:

```
/code-review high <lane>/<short-slug>
```

**Then read the review's own statement of what it reviewed**, in the first lines of its report, and
check it against your branch — the commit range, the file count, and the file *types*. A C#-only
ticket that comes back with findings in `.tsx` files reviewed something else. If the target is
wrong, the findings are about someone else's merged code: discard them wholesale and re-run with the
branch named. Do not triage them, and do not report them as your ticket's findings.

Then run `/code-review` on the branch and fix what it finds — under the gate in 7a.

## 7a. The review gate — binds every reviewer

Applies to `/code-review`, to any subagent you dispatch to review, and to you reviewing the
subagent's diff.

A reviewer that consults only the code cannot tell a deliberate design choice from a defect, because
the mechanism is real in both cases: the closure genuinely does capture a stale view, the discarded
`bool` genuinely is discarded. **A real mechanism is not evidence of a defect.** Only a document
settles that, and the call site does not point at one — a 12-line comment at
`PushoverClient.SendGlanceAsync` naming its owning lane and ticket did not stop that stub being
reported as a finding.

So every finding carries one line of provenance before it is reported:

> _Not settled: no ADR covers `Infrastructure/Pushover/`; `gh issue list --search "Pushover"
> --state open` returns nothing._

Reach that line in this order. The order **is** the gate — reading code first is what manufactures
the false finding.

1. `gh issue list --state open --search "<file or area>"`. An open ticket that owns the file has
   already claimed the behaviour, and a finding inside it is that ticket's work, not a new one.
2. `docs/adr/README.md` — its "Read it before touching" column resolves an area in one read.
3. The plan's **Departures from the resolutions** and **Global constraints**; knowing departures are
   declared there, and `tests/TEST-INVENTORY.md` records accepted coverage gaps the same way.
4. Then the code.

One search covers a batch of findings in the same area, which is cheap beside a single false
finding's round trip. A finding that survives all four is reported with its provenance line. A
finding that does not survive is dropped — and when the justification was hard to find, say where it
finally turned up, so the next reviewer reaches it sooner.

**Receiving a finding:** before applying it, re-read the source it cites and apply the source's
wording, not the reviewer's paraphrase — in #100 a paraphrase was sound in substance and still
wrong in the spec's terms ("counted as if Active" where § Scarcity says _undefined_).

**Restate this gate in the brief of any subagent you dispatch to review.** The subagent never sees
this skill, and step 4a applies here too: hand it the decisions you already hold as findings, not as
a reading list.

## 8. Merge into `main` and close out

**Do not open a PR, and do not stop here to wait for Phil.** Once step 7's verification is green
and step 7a's review is settled, finishing the ticket is your job, start to finish. The commands
below are one sequence; run them in order.

Rebase first — from inside the worktree, because the local `main` ref belongs to the main clone
and may be stale:

```sh
git fetch origin && git rebase origin/main
```

**Then, if your diff touches API surface, re-run `./scripts/check-schema-drift.sh` — even when the
rebase applied cleanly.** Two lanes editing distant parts of the OpenAPI document both apply without
a textual conflict and still leave the combined output wrong; the check is seconds and settles it.
Non-zero means regenerate and amend the tip commit — on a multi-commit branch that puts the regen on
the tip rather than on the shape-changing commit, which is fine: what matters is that the tree that
becomes `main` is self-consistent. A `schema.d.ts` *conflict* during the rebase is resolved the same
way: regenerate, never merge the hunks. This is what keeps `main` matching its own code by
construction — after the rebase the worktree is the tree that is about to become `main`, and
`--ff-only` makes `main` bit-identical to it.

Then fast-forward `main` in the main clone and push it. The main clone stays on `main` (step 3),
so it is already the right checkout, and `--ff-only` is what makes this safe with several lanes
finishing at once:

```sh
git -C /Users/phil/dev/task-guide merge --ff-only <lane>/<short-slug>
git -C /Users/phil/dev/task-guide push origin main
```

If the merge is **rejected as not a fast-forward**, another lane pushed between your rebase and
your merge. Rebase again, **re-run the drift check if your diff touches API surface** — the lane that
beat you may have changed the document — and retry. Never `--no-ff`, never `merge -s ours`, never
force-push.

Close the ticket, with the verification in the comment so the close is auditable:

```sh
gh issue close <n> --comment "<what you ran, and its green output>"
```

Then remove your worktree and delete the branch — the work is in `main`, so the branch is spent:

```sh
git -C /Users/phil/dev/task-guide worktree remove /Users/phil/dev/task-guide-<short-slug>
git -C /Users/phil/dev/task-guide branch -d <lane>/<short-slug>
```

`worktree remove` refuses a dirty worktree and `branch -d` refuses an unmerged branch. Both
refusals mean something is not actually finished: read what they say, don't reach for `-f` or
`-D`.

Finally, leave the main clone current:

```sh
git -C /Users/phil/dev/task-guide pull --ff-only
```

## 9. What still needs Phil

Committing, merging, pushing `main`, closing the ticket and cleaning up are all standing
permission — that is step 8, and finishing it is not something to ask about.

Three things are still his:

- **A file another lane owns** (step 6), including any `.csproj` or `task-guide.slnx`. Report it;
  don't edit it. `src/TaskGuide.Web/src/api/schema.d.ts` is no longer on this list — regenerate it
  yourself when your diff changes the API shape (step 7).
- **A ticket that looks wrong** — scope, ownership, a blocker the plan didn't anticipate. Report
  it; don't redesign it.
- **Anything outside the ticket's scope**, however small and however obviously right.

If the diff touches `Application/Ports/`, `Api/Program.cs`, or `TaskGuide.TestSupport`, say so
plainly in your final report — those are the changes most likely to break another lane, and Phil
wants to know they landed even though you merged them yourself.
