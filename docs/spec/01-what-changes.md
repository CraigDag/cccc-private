# What changes, and why

The current app is good at its rules and expensive in its structure. Most of its bug classes come from
four structural choices, so those four are what the rebuild changes. Everything else carries over.

## 1. A session is a real occurrence on a real date

**Today:** a session is a day of the week inside a *standing pattern*, seen through every week's dates.
Weeks that have ended are *frozen* by copying the pattern onto them; weeks can be *separated* by a
one-off conversion; and one-off changes (cancel, reschedule, cover) are *exceptions* keyed by Monday and
stored on the pattern session. Registers are keyed by Monday on the session too. Reading "what runs on
Tuesday 13 October" means walking pattern, freeze, separation and exceptions, and `RegisterFiling.Locate`
exists only to find the same class across a pattern and its frozen copy.

**Rebuild:** every session has a `Date`. A week's timetable is the sessions dated inside it, full stop.
Since 1.47.0 the school's rule is already *a week ahead is blank until somebody fills it* (by copying a
week or building it), so the pattern has no remaining job. What this removes:

- the pattern, freezing-by-copy, `WeeksAreSeparate`, `SessionsInWeek`, frozen-copy ids;
- exceptions: **cancelled** is a field on the session, **cover** is a list on the session, and a
  **reschedule** is simply moving the session (with a note saying where it was, for the reader);
- `Locate` and the frozen-copy hash: a register belongs to one session id, forever.

A week that has wholly ended is still **locked** (the freeze rule survives as a rule, not as a copy):
nothing about it changes except registers, which the right people may still file. Undo cannot unlock it.

## 2. Changes are stored as an operation log, not as whole-file snapshots

**Today:** every save writes the whole `.cccc`; live editing (1.62.0) re-reads the file and three-way
merges this tab's changes over it field by field; undo keeps fifty whole-project snapshots per tab
(about 4 GB for 200 edits on a year's file before the cap); a register written by a coach takes a
separate door past the lease.

**Rebuild:** the server holds one live copy of each project. Every change is a small **operation**
("put this room", "remove that session") applied in order under one lock and appended to a log.
Because Blazor Server already runs every tab on the server, there is nothing to merge: every tab sees
the same state and is told when it moves. See [`04-storage-and-editing.md`](04-storage-and-editing.md).
What this gives:

- live editing with no merge code, no lease and no "changed underneath";
- **undo per person**, as the inverse of that person's own operations, refused for a thing somebody
  else has changed since (and saying who);
- a **history of who changed what** for free, at the size of the change rather than the file;
- registers are ordinary operations on their own collection, so a coach filing one cannot collide with
  an administrator moving the class.

## 3. One project, one database file

**Today:** a store folder of `.cccc` zips with `versions/`, `drafts/`, `.cccc.lock` sidecars and
`deleted/`, plus `accounts.json` and `signins.json` beside it.

**Rebuild:** one SQLite file (`cccc.db`) holding projects, their operation logs, snapshots, accounts
and sign-ins, written with SQLite's own atomic transactions (WAL). A backup is one file, taken with
`VACUUM INTO`. `.cccc` remains a format the app can **import** (every schema up to 23) and **export**
(the current, rebuilt shape is a different format, `.cccc2`, so the two cannot be confused).

Drafts disappear: there is no unsaved work, because every change is stored the moment it is made.

## 4. Feature folders, and the rules in a plain library

**Today:** `MainViewModel` is a few thousand lines spread across partial files, the web reads a
desktop-shaped view-model, and the rules live in `ViewModels/` beside UI state.

**Rebuild:**

```
src/Cccc.Domain    the model and every rule; no I/O, no UI, no clock (today is passed in)
src/Cccc.Store     SQLite: the operation log, snapshots, accounts
src/Cccc.Legacy    reading old .cccc files (schemas 1–23) into the new model
src/Cccc.Web       Blazor Server; Features/<Thing>/ holds that thing's pages, forms and services
tests/…            one test project per library
```

No desktop hosts, no platform abstractions, no floor plan (it has been off the screen since 1.31.0).

## What deliberately does NOT change

- **.NET 10, Blazor Server, Docker behind nginx on the school laptop**: Craig already runs and
  maintains exactly this, and the rebuild should be a drop-in at the deployment level.
- **Local accounts** with PBKDF2 verifiers and administrator-managed roles. Google sign-in is dropped
  (the school runs local accounts; it can return if ever wanted).
- **The words on screen**: Programme (not Strand), Semester, Download vs Save, Close not Cancel, OK then
  Cancel, Delete (something built) vs Remove (an entry from a list).
- **Every rule in [`03-the-rules.md`](03-the-rules.md)**, which is the part the school paid for.
- **Danish name order** for every name a person reads (Æ Ø Å after Z, `Aa` as `Å`), without touching
  `CurrentCulture`.
- The design through line: colours as roles (identity, accent, warning, greys), spines, washes, discs,
  dashed for missing, the four dark palettes, mint accent by default.
