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
  offered.
- **A rule has one implementation, in `Cccc.Domain`**: who is on a class (`Roster`), what happens
  (`Occurrences`), clashes, hours, registers. The screen, the paper and the reports all ask it.
- **A week that has wholly ended is locked except for registers**, and Undo cannot unlock it.
- **Every change goes through `Workspace.Commit`**, which refuses a timetable that follows the current
  app (`Access.Following`, `Follower`): the next save over there would undo it. A path that commits to a
  `LiveProject` directly (History's put-back) checks `Follower.FollowedId` itself.
- **The store refuses to start rather than guess**: a missing or empty folder needs `Store__Create=true`
  on the first start only.
- **Every PDF goes through `PdfGate`** (QuestPDF loses text when two documents generate at once).
- **A name a person reads is ordered with `NameOrder`** (Danish: Æ Ø Å after Z, `Aa` as `Å`), never
  by changing `CurrentCulture`.
- **Bounds advise, never refuse**: clashes, limits, a closure scoped at nobody are warnings.
- **`src/Cccc.Legacy` is a frozen copy of the old loader** (1.66.0); edit it only to keep it compiling.

## The loop

```bash
scripts/check.sh      # zero warnings, then every suite; read the Total: lines
scripts/preview.sh    # made-up school on :5299 (admin / editor / coach, "northlight <name>")
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
