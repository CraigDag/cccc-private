# Access and privacy

## Signing in

- **Local accounts only**: a name and passphrase, verified against a PBKDF2-SHA256 verifier
  (600,000 iterations, 16-byte salt). Passphrases are never stored.
- **Every page is behind sign-in by a fallback authorization policy**, so a page added later is
  refused too. Anonymous: the sign-in page, its stylesheet, `/healthz`, and a screen in the building's
  `/screen` and `/screen/collect` (below), each by name.
- **Limited to 10 attempts a minute per client**; behind nginx the client is read from
  `X-Forwarded-For` only when `TrustedProxies` names the proxy.
- The cookie is `HttpOnly`, `SameSite=Lax`, `Secure` when the request is https, and lasts 7 days at
  most. **Every request re-checks the account**, so a removal, suspension or role change takes effect on
  the next click; a live circuit re-checks every minute.
- **First start**: with no accounts, the app prints a one-time setup link to its log, through which the
  first administrator chooses their passphrase. Nothing takes a passphrase on a command line.
- Changing your own passphrase signs you out everywhere else.
- Every sign-in page is sent `Cache-Control: no-store`.

## Roles and capabilities

Three roles: **Viewer**, **Editor**, **Administrator**, plus *nobody* (the fail-closed default).
Capabilities are **named**, never `role >= Editor`:

| Capability | Who |
|---|---|
| `MayEdit` (change the week, add sessions, events) | Editor, Administrator |
| `MayUseAdminTools` (Data, Reports, the dock, whole-project export) | Administrator |
| `MayManageAccounts` | Administrator |
| `MayManageProjects` (new, import, rename, delete, choose which project is open, history) | Administrator |
| `MayLookAtOtherTimetables` (look at, and prepare, a timetable that is not open for everybody, in your own tab) | Administrator |
| `SeesAdministratorsEvents` | Administrator |
| `MayTakeEveryRegister` | Administrator |
| `MaySeeSystem` (backups, the nightly update) | Administrator |
| `MayManageScreens` (pair, change and unpair the screens in the building) | Administrator |

**A timetable that follows the current app is changed by nobody here** (`Access.Following`;
[`04`](04-storage-and-editing.md)). It narrows what anybody may *change* — every collection, registers
included, at the one door and in `MayChange`; a form that would change it offers only Close; History's
put-back and Delete are not offered on it — and nothing they may *see*: an editor still sees what
editors see, and whose registers are outstanding is still counted, because they are outstanding over
there. Accounts and the timetable's name are not the timetable, and stay changeable.

**An account carries**: its role; **tags** (Student, Staff); a **link** to one facilitator (only an
administrator sets either, so nobody tags themself into an audience); suspended; must-change-passphrase;
last signed in (in `signins`, never on the account row). A guest facilitator cannot be linked.
**Three guards**, under one lock: the acting person must be an administrator now; the last
administrator who can sign in cannot be removed, demoted or suspended; nobody removes, demotes or
suspends themself.

## What a person sees

- **Signing in opens the school's timetable** — the project most recently changed — rather than a
  picker. Which project is open is an administrator's choice.
- **An administrator may look at another timetable in their own tab** (next year's, a file just brought
  in) while everybody else stays on the open one. Which timetable a tab shows is one rule,
  `ProjectChoice.ShownId`; everybody but an administrator only ever sees the open one. A print link and a
  spreadsheet link carry the timetable's id, never its name, and their routes ask `ProjectChoice.MaySee`
  again when they are opened, so a link cannot show a timetable to somebody who may not see it, or one
  deleted since.
- **Who may print whose week**: an administrator anybody's; everybody else only their own, through
  their link. The PDF route refuses too, not only the button.
- **Downloads of data about every student are administrators' only.**

## Screens in the building

A TV in a corridor, or a tablet by a studio door, shows the timetable as it is now (`/screen`,
[`07`](07-screens.md#screens-in-the-building-screen)) **with nobody signed in on it**: a passphrase typed
on a TV would be typed in front of everybody, and a signed-in browser left on a wall is an open door.

- **Pairing.** A screen that is not paired shows a six-character code from letters and digits nobody
  misreads across a corridor (no 0/O, 1/I/L, 2/Z, 5/S, 8/B, 6/G). An administrator types it in
  Administration ▸ Screens, names the screen and says what it shows. The code only says *which* waiting
  screen is meant: the page that showed it was also given a **claim**, a secret it keeps to itself, and
  only that browser can collect the screen's token, once (`ScreenPairing`). A code lasts ten minutes; a
  paired screen has two more to collect; at most 64 wait at once, and one address gets ten new codes a
  minute, so a page reloaded in a loop cannot fill the list. Waiting screens live in memory: a restart
  just shows a new code.
- **The token** is 32 random bytes in a cookie sent only to `/screen` (its path), `HttpOnly`,
  `SameSite=Strict`, `Secure` when the request is https, kept 400 days and renewed by every look, so a
  screen that is on never needs pairing twice. The store keeps only its SHA-256 (`ScreenDirectory`).
  **It is not a sign-in**: the fallback policy refuses a screen every other page exactly as it refuses
  anybody not signed in, and the screen's page reads nothing but the timetable open for everybody.
- **Unpairing** deletes the screen's row, so its token stops working at once; its next look (within half
  a minute) shows a new code and takes the cookie back. The list says when each screen last looked, and
  warns about one that has not looked for a day.
- **What a screen shows**, decided in one place (`ScreenBoard`) so the page and its tests read the same
  answer, is what a printed timetable on the wall would carry, since everybody walking past reads it,
  visitors too: each class, who it is for, the **first names** of who is in the room, and where; what is
  off today, as *cancelled* and nothing more; what changed since the weekday before, as a student needs
  it (on or off, moved, a new time or space); what starts next anywhere; today's events that every
  student may see; and which spaces are free. **Never** a student's name, a note, why a class is off,
  who is away or covering (an uncovered class simply names nobody), or an event the students may not
  see: in a space that one is only *Booked*, and without a space it is not there at all. A visiting
  company's class is on it, because a space with a company in it is not free.

## GDPR

The live file has held real students since 2026-10-29, so all of this applies to real people.

- **No project contents and no project name are ever logged**, and **no name reaches a URL**: pages
  address things by id.
- **This repository holds made-up data only.** A copy of the live file is personal data: it is worked on
  in a scratch folder, never committed, never screenshotted, and deleted when the trial is done.
- **Erasing a person** is an administrator's action that removes them from the live project **and
  rewrites every stored operation and snapshot that names them** (their name and email replaced, their
  id kept so counts still add up). This is the one sanctioned rewrite of the log, and it is logged as
  "a person was erased" with no name. The off-site backup keeps old copies for 90 days; the erasure
  page says so and names the step on Drive, as the current runbook does. **Every timetable is rewritten
  through its one live copy, under its lock** (`LiveProjects.RewriteEverywhere`), which then holds the
  rewritten history: forgetting a copy and rewriting the store behind it would leave a tab that still
  holds it (an administrator looking at another timetable) to write the name back with its next change.
- **Erasure also reaches somebody taken off the timetable in the last 30 days**, because the stored
  changes still name them until they are folded away (`FormerPeople`). While following the current app,
  a person is erased there first; their removal comes across; then they are erased here from the
  history. The follower's copy of the current app's file is personal data like the store, and stays on
  the laptop.
- **What a facilitator's phone calendar downloads carried** (`calendar_files`) is kept so the next
  file can cancel what has gone: ids, dates and times, never a title, a place or a note, so nothing a
  person wrote is copied out of the timetable, where erasure would have to find it. Erasing a
  facilitator forgets theirs in every timetable.
- **Absence reasons** are free text an administrator may fill; the form warns not to write health
  details there.
- Security headers on every response: a content security policy (self only), `X-Content-Type-Options`,
  `Referrer-Policy: no-referrer`, `frame-ancestors 'none'`.
