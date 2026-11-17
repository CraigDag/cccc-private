#!/usr/bin/env bash
# Stops every app server this repository started (dotnet run is three processes; this takes them all).
# The brackets keep the patterns from matching this script's own command line, and the patterns name
# the processes exactly, so a shell whose command merely mentions a path under src/ is left alone.
# A server that has not gone after ten seconds is killed outright: a leftover answers health checks for
# the old build, and the next check would happily test it.
PATTERNS=('dotnet run --project src/Cccc.W[e]b' '[d]otnet exec .*Cccc.Web.csproj' 'Cccc.Web/bin/[D]ebug/net10.0/Cccc.Web')
for p in "${PATTERNS[@]}"; do pkill -f "$p" || true; done
for _ in $(seq 1 20); do
  pgrep -f 'Cccc.Web/bin/[D]ebug|--project src/Cccc.W[e]b' >/dev/null || { echo "Stopped."; exit 0; }
  sleep 0.5
done
for p in "${PATTERNS[@]}"; do pkill -9 -f "$p" || true; done
sleep 1
pgrep -f 'Cccc.Web/bin/[D]ebug|--project src/Cccc.W[e]b' >/dev/null && { echo "Some are still running." >&2; exit 1; }
echo "Stopped, after killing what did not stop by itself."
