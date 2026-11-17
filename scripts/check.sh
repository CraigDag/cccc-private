#!/usr/bin/env bash
# The per-change loop: build everything with zero warnings, then every suite. Read each "Total:" line,
# never the word "Passed!" alone: an interrupted run prints Passed! with a smaller count.
set -euo pipefail
cd "$(dirname "$0")/.."
export LC_ALL=C.UTF-8
dotnet build Cccc.slnx --nologo -v q
for t in tests/*/; do dotnet test "$t" --nologo -v q --no-build; done
