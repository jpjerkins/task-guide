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

## 3. Branch

Off an up-to-date `main`:

```sh
git switch main && git pull && git switch -c <lane>/<short-slug>
```

`<lane>` comes from the ticket's `lane:*` label (`lane:integration`, `lane:firing`,
`lane:adapters`, `lane:schedule`, `lane:capture-tasks`, `lane:web-now`, `lane:web-authoring`,
`lane:validation`).

## 4. Read only what the ticket names

The ticket's own body, the plan's Global constraints, the ADRs it points at
(`docs/adr/README.md` first), and `CONTEXT.md` **only by the line ranges the ticket names**, via
`CONTEXT-INDEX.md`. Never read `CONTEXT.md` whole — it's ~122 KB.

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

Touch only what the ticket's `Owns:` block names, plus new test files it implies. If the work
genuinely needs a file another lane owns, **stop and report it rather than editing it** — that
report is the signal a contract was wrong. This includes any `.csproj` or `task-guide.slnx`
change: the integration lane owns every project file. Ask, don't edit.

## 7. Before opening a PR

```sh
dotnet test
```

green from the repo root. Web lanes additionally:

```sh
cd src/TaskGuide.Web && npm test
```

Then run `/code-review` on the branch and fix what it finds.

## 8. PR into `main`

Rebase onto `main` first. If the diff touches `Application/Ports/`, `Api/Program.cs`, or
`TaskGuide.TestSupport`, say so plainly in the PR body — those additionally need a Claude
integration-lane review before merge.

## 9. Never push or merge without Phil asking

Commit as work is verified, step by step — that's standing permission. It does not extend to
pushing or merging.
