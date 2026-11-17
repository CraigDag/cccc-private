#!/bin/bash
# Keeps a copy of the CURRENT app's timetable file inside the rebuild's own folder, for the rebuild to
# follow (docs/deployment/trying-it-beside-the-current-app.md, step 8).
#
# It only READS the current app's file: it never writes, renames, locks or changes the permissions of
# anything of the current app's, and its unit makes the whole disk read-only to it except
# /srv/cccc-next. It runs as root only because the current app's folder (/srv/cccc/projects, mode 700)
# is root's alone; the copy belongs to the rebuild's own user (1654) and nobody else can read it.
#
# The current app saves by writing the whole file aside and renaming it over the old one, so a copy
# begun at any moment reads one whole save. The copy is written aside and renamed in turn, so the
# rebuild never reads half a copy either. `cp -p` keeps the save's own time, which System shows.
set -uo pipefail

SRC="${FOLLOW_FILE:?FOLLOW_FILE is not set: see step 8 of the runbook}"
DIR=/srv/cccc-next/follow
DST="$DIR/current.cccc"
install -d -o 1654 -g 1654 -m 700 "$DIR"

# Each problem is said once, not every fifteen seconds, and never with the file's name in it: the
# current app names a file after its timetable.
said=""
say() { [ "$1" = "$said" ] || { echo "$1"; said="$1"; }; }

# And after every look, one line beside the copy for the rebuild to read: how the look went (ok,
# missing or failed) and when. Without it a copier that had stopped, or could no longer find the file,
# would leave the last copy in place and the rebuild calling a stale timetable up to date.
note() {
  printf '%s %s\n' "$1" "$(date +%s)" > "$DIR/copier.part" && chown 1654:1654 "$DIR/copier.part" \
    && chmod 600 "$DIR/copier.part" && mv -f "$DIR/copier.part" "$DIR/copier"
}

while :; do
  if [ ! -f "$SRC" ]; then
    say "The current app's file is not there. Was its timetable renamed? /etc/cccc-next-follow.env names it."
    note missing
  elif ! cmp -s "$SRC" "$DST"; then
    if cp -p "$SRC" "$DST.part" && chown 1654:1654 "$DST.part" && chmod 600 "$DST.part" && mv -f "$DST.part" "$DST"; then
      say "Copying the current app's file."
      note ok
    else
      rm -f "$DST.part"
      say "Could not copy the current app's file."
      note failed
    fi
  else
    note ok
  fi
  sleep 15
done
