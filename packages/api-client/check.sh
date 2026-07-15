#!/usr/bin/env bash
# Fails if the committed API client is out of date with the C# it is generated from.
#
# This exists because generation without verification decays. Somebody adds a field to a DTO, does
# not regenerate, and the frontend keeps compiling against types that no longer describe the API —
# which is precisely the failure hand-written types have, reintroduced by the back door.
#
# Runs in CI. If it fails: `npm run generate:api` in apps/web, and commit the result.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

# Distinguished from staleness on purpose: "the API does not build" and "the client is out of date"
# are different failures with different fixes, and a script that reports them identically sends the
# next person to regenerate a client that cannot be generated.
if ! bash "$root/packages/api-client/generate.sh" "$tmp" > "$tmp/generate.log" 2>&1; then
  echo "Could not generate the API client — the API does not build." >&2
  echo >&2
  tail -20 "$tmp/generate.log" >&2
  exit 2
fi

# openapi.json carries no ordering guarantees worth diffing on its own; schema.d.ts is the artefact
# the frontend actually compiles against, so that is the one that must match.
if ! diff -q "$root/packages/api-client/schema.d.ts" "$tmp/schema.d.ts" > /dev/null; then
  echo "The generated API client is stale." >&2
  echo "The API's contract has changed and packages/api-client was not regenerated." >&2
  echo >&2
  diff -u "$root/packages/api-client/schema.d.ts" "$tmp/schema.d.ts" | head -40 >&2
  echo >&2
  echo "Fix: cd apps/web && npm run generate:api, then commit the result." >&2
  exit 1
fi

echo "API client is up to date."
