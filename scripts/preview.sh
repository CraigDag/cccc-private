#!/usr/bin/env bash
# Runs the app on made-up data for a look: the fictional Northlight school and three preview accounts
# (admin / editor / coach, passphrases "northlight admin" etc.). Development only; never the live file.
set -euo pipefail
cd "$(dirname "$0")/.."
PORT="${PORT:-5299}"
STORE="${STORE:-$PWD/.local-store-preview}"
export LC_ALL=C.UTF-8
ASPNETCORE_ENVIRONMENT=Development \
Store__Path="$STORE" Store__Create=true \
Preview__Seed=true Preview__Accounts=true \
  dotnet run --project src/Cccc.Web --no-launch-profile --urls "http://127.0.0.1:$PORT" "$@"
