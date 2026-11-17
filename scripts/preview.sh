#!/usr/bin/env bash
# Runs the app on made-up data for a look: the fictional Northlight school and three preview accounts
# (admin / editor / coach, passphrases "northlight admin" etc.). Development only; never the live file.
set -euo pipefail
cd "$(dirname "$0")/.."
PORT="${PORT:-5299}"
STORE="${STORE:-$PWD/.local-store-preview}"
export LC_ALL=C.UTF-8
# A port that answers BEFORE this server starts is a leftover server, and a check against it would
# happily test the old build. Refuse rather than guess.
if curl -s -o /dev/null "http://127.0.0.1:$PORT/healthz"; then
  echo "Something is already answering on port $PORT. Stop it first (scripts/stop.sh)." >&2
  exit 1
fi
ASPNETCORE_ENVIRONMENT=Development \
Store__Path="$STORE" Store__Create=true \
Preview__Seed=true Preview__Accounts=true \
  dotnet run --project src/Cccc.Web --no-launch-profile --urls "http://127.0.0.1:$PORT" "$@"
