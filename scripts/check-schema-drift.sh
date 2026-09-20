#!/usr/bin/env bash
#
# check-schema-drift — verify the checked-in TypeScript API types
# (src/TaskGuide.Web/src/api/schema.d.ts) match the API's live OpenAPI document.
#
#   ./scripts/check-schema-drift.sh
#
# Boots the API on its fixed port, regenerates the schema to a temp file with
# the same openapi-typescript version already pinned in package.json, and
# diffs it against what's checked in. Exits 0 if they agree, non-zero (with
# the diff) if they don't.
#
# A script and not a CI workflow: this repo has no CI workflows at all. If
# one is ever added, this becomes a single `run: ./scripts/check-schema-drift.sh`
# step in it.
set -euo pipefail

cd "$(git rev-parse --show-toplevel)"

PORT=8007
if [ -n "$(lsof -ti:$PORT || true)" ]; then
  echo "error: port $PORT is in use — stop the running API first" >&2
  exit 1
fi

if [ ! -d src/TaskGuide.Web/node_modules ]; then
  echo "error: src/TaskGuide.Web/node_modules is missing — run 'npm install' there first" >&2
  exit 1
fi

DATA_DIR=$(mktemp -d)
GENERATED=$(mktemp)
API_PID=""

cleanup() {
  [ -n "$API_PID" ] && kill "$API_PID" 2>/dev/null || true
  rm -rf "$DATA_DIR" "$GENERATED"
}
trap cleanup EXIT

Storage__DataDir="$DATA_DIR" dotnet run --project src/TaskGuide.Api >/dev/null 2>&1 &
API_PID=$!

for _ in $(seq 1 60); do
  if curl -fsS "http://localhost:$PORT/openapi/v1.json" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
if ! curl -fsS "http://localhost:$PORT/openapi/v1.json" >/dev/null 2>&1; then
  echo "error: API never came up on port $PORT within 60s" >&2
  exit 1
fi

(cd src/TaskGuide.Web && npx --no-install openapi-typescript "http://localhost:$PORT/openapi/v1.json" -o "$GENERATED")

if diff -u src/TaskGuide.Web/src/api/schema.d.ts "$GENERATED"; then
  echo "schema.d.ts matches the live OpenAPI document"
else
  echo "error: schema.d.ts is stale — regenerate with 'npm run gen:api' in src/TaskGuide.Web" >&2
  exit 1
fi
