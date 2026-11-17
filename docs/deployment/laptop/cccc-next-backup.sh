#!/bin/bash
# Nightly backup of the REBUILD's store (/srv/cccc-next), at 02:45, after the current app's own backup.
# The current app's data and backup are not touched.
#
# `cccc backup` copies the whole store with SQLite's VACUUM INTO while the app runs: one consistent file
# with every timetable, its history and the accounts. It also records the time, which Administration ▸
# System shows (amber after a missed night) — the app is the one place somebody is already looking.
set -euo pipefail

DIR=/srv/cccc-next/backups
STAMP="$(date +%Y-%m-%d)"
install -d -o 1654 -g 1654 -m 700 "$DIR"
rm -f "$DIR/cccc-$STAMP.db"     # a second run the same night replaces that night's copy
docker exec cccc-next dotnet Cccc.Web.dll backup "/data/backups/cccc-$STAMP.db"

# Keep fourteen nights.
ls -1t "$DIR"/cccc-*.db | tail -n +15 | xargs -r rm -f

# Off-site: fill in the same encrypted remote the current backup uses, then remove the #.
# rclone sync "$DIR" gcrypt:cccc-next --config /root/.config/rclone/rclone.conf --log-level INFO

echo "backed up cccc-$STAMP.db"
