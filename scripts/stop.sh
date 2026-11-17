#!/usr/bin/env bash
# Stops every app server this repository started (dotnet run is three processes; this takes them all).
# The brackets keep the patterns from matching this script's own command line, and the patterns name
# the processes exactly, so a shell whose command merely mentions a path under src/ is left alone.
pkill -f 'dotnet run --project src/Cccc.W[e]b' || true
pkill -f '[d]otnet exec .*Cccc.Web.csproj' || true
pkill -f 'Cccc.Web/bin/[D]ebug/net10.0/Cccc.Web' || true
sleep 1
pgrep -f 'Cccc.Web/bin/[D]ebug|--project src/Cccc.W[e]b' >/dev/null && echo "Some are still running." >&2 || echo "Stopped."
