#!/usr/bin/env bash
# Stops every app server this repository started (dotnet run is three processes; this takes them all).
# The bracket keeps the pattern from matching this script's own command line.
pkill -f 'src/Cccc.W[e]b' || true
pkill -f 'Cccc.Web/bin/[D]ebug' || true
sleep 1
pgrep -fl 'Cccc.W[e]b' && echo "Some are still running." >&2 || echo "Stopped."
