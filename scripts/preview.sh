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
# FOLLOW=1 also tries "follow the current app": a made-up school in the current app's own format is written
# to .local-follow-preview/School.cccc and followed, looked at every 5 seconds. Change it "over there" with
#   dotnet run --no-build --project tools/OldFormatSchool -- cancel .local-follow-preview/School.cccc Aerial
# FOLLOW=year follows a whole made-up school year instead, at about a real school's size.
if [ "${FOLLOW:-}" = 1 ] || [ "${FOLLOW:-}" = year ]; then
  FOLLOW_FILE="$PWD/.local-follow-preview/School.cccc"
  mkdir -p "$(dirname "$FOLLOW_FILE")"
  dotnet run --project tools/OldFormatSchool -- "$([ "$FOLLOW" = year ] && echo year || echo make)" "$FOLLOW_FILE"
  set -- "$@" "--Follow:File=$FOLLOW_FILE" --Follow:EverySeconds=5
fi
# AT=10:15 sets the school's clock to that time of day, running on from there: Now and the screens in
# the building then show the middle of a school day whenever the preview is started.
if [ -n "${AT:-}" ]; then set -- "$@" "--Clock:Time=$AT"; fi
ASPNETCORE_ENVIRONMENT=Development \
Store__Path="$STORE" Store__Create=true \
Preview__Seed=true Preview__Accounts=true \
  dotnet run --project src/Cccc.Web --no-launch-profile --urls "http://127.0.0.1:$PORT" "$@"
