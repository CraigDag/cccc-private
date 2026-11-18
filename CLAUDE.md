# CCCC Scheduler, rebuilt — notes for whoever works here next

A fresh build of the school's timetable app. **The current app (`CraigDag/cccc-scheduler`) is a
reference only: never push to it, branch it, or open anything on it, and never touch the school
laptop, its images or `ghcr.io/craigdag/cccc-web` from here.** Craig decides if and when this replaces
it. `README.md` says what this is; `docs/spec/` says why each part is shaped the way it is.

## Rules you would break without knowing

- **Made-up data only.** No real names, no copy of the live `.cccc`, no secrets, in code, fixtures,
  screenshots or commits. A copy of the live file is personal data: scratch folder only, deleted after.
- **No project contents and no project name are ever logged, and no name reaches a URL.** Print links
  are short-lived tokens for that reason (`PrintRequests`).
- **Every page is behind sign-in by a fallback policy**, and capabilities are named on `Access`, never
  compared as role levels. The server checks every change with `Access.MayChange`, whatever the screen
  offered. **The one page outside it is `/screen`**, a screen in the building: its cookie is a token
  (`ScreenDirectory`, only the hash stored) that opens nothing else, and it shows only what `ScreenBoard`
  decides a corridor may read: never a student, a note, who is away, or an event the students may not see.
- **A rule has one implementation, in `Cccc.Domain`**: who is on a class (`Roster`), what happens
  (`Occurrences`), clashes, hours, registers. The screen, the paper and the reports all ask it.
- **A week that has wholly ended is locked except for registers**, and Undo cannot unlock it. The one
  other change let in is a merge re-pointing a class (`Edits.CheckLock`), checked op by op with its batch.
- **Deleting a class, space or facilitator used before today keeps it as former** (`Former`): the past
  keeps it whole, nothing ahead offers it, and every copy goes through `Edits.CopyOf`, which leaves it out.
  A new picker or list asks `.Current()`, and keeps a former one only where it is already the value.
- **Which timetable a tab shows is `ProjectChoice.ShownId`**: an administrator's tab may look at one that
  is not open for everybody (`TabTimetable`). So a route that serves a tab (print, spreadsheet) takes the
  timetable's id from its link and asks `ProjectChoice.MaySee`, never `ProjectChoice.Open()`, which is
  only for what belongs to the school as a whole (the reminder email, following).
- **Every change goes through `Workspace.Commit`**, which refuses a timetable that follows the current
  app (`Access.Following`, `Follower`): the next save over there would undo it. A path that commits to a
  `LiveProject` directly (History's put-back) checks `Follower.FollowedId` itself.
- **The store refuses to start rather than guess**: a missing or empty folder needs `Store__Create=true`
  on the first start only.
- **Every PDF goes through `PdfGate`** (QuestPDF loses text when two documents generate at once).
- **A name a person reads is ordered with `NameOrder`** (Danish: Æ Ø Å after Z, `Aa` as `Å`), never
  by changing `CurrentCulture`.
- **Bounds advise, never refuse**: clashes, limits, a closure scoped at nobody are warnings.
- **`src/Cccc.Legacy` is a frozen copy of the old loader** (1.68.0, schema 24); edit it only to keep it compiling.
  When the current app's format moves on, refresh the copied files whole from its `master`, never by hand.
- **A phone calendar file is imported over the last one, so nothing is removed by leaving it out**:
  what is no longer theirs is published cancelled, and what has gone from the timetable is found through
  `PhoneCalendarFiles` (ids and times only, never a title). Anything kept outside the timetable goes
  with its project in `ProjectStore.Purge`, and with its person in the erasure (`EraseTab`).
- **The Scratch Week is a collection of its own** (`Timetable.Scratch`, `ScratchWeek`), never part of
  `Sessions`, so nothing that counts can read it by accident; `Edits.CheckLocks` keeps the two apart.
- **A roll call is held in memory only** (`RollCalls`): nothing of who was found is stored, logged or put in
  a URL, and ending it, a restart or three hours forget it. It is staff's (`Access.MayCallTheRoll`), lists
  the timetable open for everybody, and each person on it is `Where`'s answer (`RollCall`), never a second one.
- **What a facilitator asks is a list of its own too** (`Timetable.Requests`, `Offers`, `SpaceAsks`), never
  an absence, a cover or a class: only an administrator's yes records one, through the edits the forms use
  (`Edits.AnswerRequest`, `AnswerOffer`, `AnswerSpaceAsk`). There is no box for why, and none should be
  added: a coach asked why writes the illness.

## The loop

```bash
scripts/check.sh      # zero warnings, then every suite; read the Total: lines
scripts/preview.sh    # made-up school on :5299 (admin / editor / coach, "northlight <name>")
AT=10:15 scripts/preview.sh   # the same at 10:15 school time, for Now and the screens mid-morning
DAY=wed AT=10:15 scripts/preview.sh   # and on this week's Wednesday, for a walk written for the sample week's Wednesday
FOLLOW=1 scripts/preview.sh   # the same, also following a made-up school in the current app's own format
FOLLOW=year scripts/preview.sh   # following a whole made-up school year, at about a real school's size
scripts/stop.sh       # stop every server this repo started; a leftover answers health checks for the old build
```

**Break every new test on purpose and watch it fail** before believing it. A browser read straight
after a click can see the previous render: wait for something only the new render draws.

GitHub Actions minutes are nearly spent, so there is no CI here: run `scripts/check.sh` before pushing.

## Where things are

`src/Cccc.Web/Features/<Thing>/` holds that thing's pages, forms and services. `docs/deployment/` has
the runbook for trying it on the laptop beside the current app, and the backup script and timer.
