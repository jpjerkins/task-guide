#!/usr/bin/env bash
#
# frontier — show which tickets on a wayfinder map are worth taking next.
#
#   ./scripts/frontier.sh [map-issue-number]     (default: 1)
#
# Two questions, because neither answers the other:
#
#   1. WHAT IS TAKEABLE, AND WHAT DOES IT UNBLOCK?
#      A ticket is on the frontier when it is open, has no *open* blockers, and
#      has no assignee (the assignee is the claim). Ranked by how many other
#      tickets it unblocks — resolving a gate buys two tickets' worth of progress.
#
#      Read a zero sceptically. Edges only exist because a session wired them, so
#      a zero may mean "no dependency" or may mean "nobody noticed yet". When a
#      session finds it cannot answer X until Y is settled, wire the edge then.
#
#   2. WHAT FOG WOULD IT CLEAR?
#      The point of a map is to shrink itself, and the unshrunk part lives in the
#      map body's "Not yet specified" section — invisible to any issue list. A
#      ticket standing between you and being able to *write* a new ticket is worth
#      more than its own resolution. This mines the fog for ticket references.
#
# Requires: gh (authenticated), python3. Repo is inferred from the git remote.
set -euo pipefail

MAP="${1:-1}"
REPO=$(gh repo view --json nameWithOwner --jq .nameWithOwner)
RULE=$(printf '%.0s─' $(seq 1 94))

printf '\n\033[1mFRONTIER\033[0m — open children of #%s in %s\n%s\n' "$MAP" "$REPO" "$RULE"
printf '%-5s %-50s %-11s %-9s %s\n' "#" "TITLE" "TYPE" "UNBLOCKS" "STATE"
printf '%s\n' "$RULE"

gh api --paginate "repos/$REPO/issues/$MAP/sub_issues" \
  --jq '.[] | select(.state=="open") | .number' |
while read -r n; do
  gh api "repos/$REPO/issues/$n" --jq '
    [ .number,
      (.title | .[0:48]),
      ([.labels[].name | select(startswith("wayfinder:")) | sub("wayfinder:";"")] | join(",")),
      (.issue_dependencies_summary.blocking   // 0),
      (.issue_dependencies_summary.blocked_by // 0),
      ([.assignees[].login] | join(","))
    ] | @tsv'
done |
sort -t"$(printf '\t')" -k4,4nr -k1,1n |
while IFS="$(printf '\t')" read -r num title type blocking blockedby who; do
  if   [ -n "$who" ];          then state="claimed by $who"
  elif [ "$blockedby" -gt 0 ]; then state="blocked by $blockedby"
  else                              state="TAKEABLE"
  fi
  printf '%-5s %-50s %-11s %-9s %s\n' "$num" "$title" "$type" "$blocking" "$state"
done

printf '\n\033[1mFOG\033[0m — what "Not yet specified" is waiting on\n%s\n' "$RULE"
gh api "repos/$REPO/issues/$MAP" --jq .body | python3 -c '
import re, sys
body = sys.stdin.read()
if "## Not yet specified" not in body:
    print("  (map has no \"Not yet specified\" section)"); raise SystemExit
fog = body.split("## Not yet specified")[1].split("## Out of scope")[0]
gates = {}
for patch in (p.strip() for p in fog.split("\n-") if p.strip()):
    refs = sorted(set(re.findall(r"#(\d+)", patch)), key=int)
    if not refs: continue
    title = re.sub(r"[*`]", "", re.sub(r"\s+", " ", patch)).split("—")[0].strip()[:52]
    for r in refs: gates.setdefault(r, []).append(title)
    reflist = ", ".join("#" + r for r in refs)
    print(f"  {title:<54} needs {reflist}")
if gates:
    print("\n  Tickets by fog patches they would unlock (closed ones are already done):")
    for t, ps in sorted(gates.items(), key=lambda kv: (-len(kv[1]), int(kv[0]))):
        print(f"    #{t:<4} {len(ps)}")
'
printf '\n\033[2mTakeable + right mode for your situation + unblocks a ticket + clears fog.\033[0m\n'
printf '\033[2mThe last call is yours: grilling and prototype tickets need you present.\033[0m\n\n'
