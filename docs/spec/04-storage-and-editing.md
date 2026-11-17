# Storage and editing

## The operation

A change is a **batch** of one or more **operations**, made by one person at one moment, which is one
undo step. There are two operations and only two:

```
Put(collection, id, value)      // add or replace one thing
Remove(collection, id)          // take one thing away
```

plus `PutSettings(value)` for the project's own settings. `collection` is one of the model's lists
(`rooms`, `sessions`, `registers`, …). A forms-level command such as *Copy week to…* compiles to a
batch of these; the store never sees commands, only operations.

Each stored operation also records **the value it replaced** (`before`), so that:

- **undo** is the inverse batch (put back `before`, or remove what was added);
- **history** can say exactly what changed, field by field, without reading two whole files;
- **conflicts are detectable**: an undo whose thing has changed since (its current value is not the
  `after` this step wrote) skips that thing and names who changed it.

**There is never a hand-written field list.** Operations carry whole values serialised by one JSON
configuration; a field added to the model is in every operation automatically.

## The live project

The server keeps one **live project** per open project: the current state (immutable records), its
sequence number, and the people connected. Applying a batch:

1. takes the project's lock;
2. checks the batch against the rules that refuse (a locked week, a capability the person lacks);
3. applies it to the state, producing a new immutable state;
4. appends it to the log **in one SQLite transaction** — if the write fails, the state is not changed;
5. releases the lock and tells every connected tab, with who made the change and what it touched.

Every tab renders from the same live state, so two people editing at once see each other's changes
within one render, and nothing is ever merged. **The later of two changes to one thing wins and the
earlier person is told** — the tab compares what it showed in a form with what is there now.

**A form open in one tab takes in other people's changes like any other screen**, and if the thing it
is editing changed underneath, it says so and offers to reload the form rather than writing the old
values back. (The old app froze the other tab's changes while a form was open; with whole values and
a `before` check, that freeze is no longer needed.)

## The log on disk

One SQLite database file, `cccc.db`, in WAL mode:

| Table | Holds |
|---|---|
| `projects` | id, name, created, deleted-at (soft delete) |
| `batches` | project id, sequence, who, when (UTC), a short label ("Moved Acrobatics") |
| `ops` | batch, index, collection, thing id, `before` JSON, `after` JSON |
| `snapshots` | project id, sequence, whole state JSON, compressed |
| `accounts`, `signins` | see [`05-access-and-privacy.md`](05-access-and-privacy.md) |
| `meta` | the store's own format number: **2** since the Scratch Week. A store at 1 is brought up to 2 as it opens (its timetables simply have an empty Scratch Week); a store of a newer number is refused, so an older build never opens a store whose timetables hold a plan it would drop |

- **Loading** a project reads the newest snapshot and replays the batches after it.
- **A snapshot is written** every 200 batches and when the last person leaves a project, so a load
  never replays more than a few hundred batches.
- **History** is the batches, newest first, with who, when and what. **Putting an earlier moment back**
  is a new batch that sets the state to what it was then — never a rewrite of the log.
- **Retention**: batches and snapshots older than **30 days** are folded into one snapshot by a prune,
  so the history reaches back thirty days, as the old app's versions did. Soft-deleted projects are
  removed for good after 30 days.
- **The store refuses to start** when its folder is missing, unwritable, inside the temp directory, or
  (in a container) on the image rather than a volume. There is no "accept it anyway" switch.
- **Nothing creates the store's folder except the first start that is told to** (`Store:Create=true`),
  so a vanished volume can never be silently replaced by an empty one.

## Backups

`VACUUM INTO` writes a consistent copy of the whole database while the app runs. The app offers
`/admin/backup` to an administrator and the laptop's nightly script calls the same through a local
command (`cccc backup <path>`), so the existing rclone off-site sync keeps working on one file.

## Export and import

- **Export** a project as `.cccc2` — a zip holding `project.json` (the state) and `format.txt`, whose
  marker is `cccc2 2` since the Scratch Week. `cccc2 1` files still read (with an empty Scratch Week).
- **Import** a `.cccc2`, or an old `.cccc` of any schema up to 24 (see
  [`06-importing-old-files.md`](06-importing-old-files.md)), as a **new** project. Import never
  overwrites a project in place.
- A file of a newer format than this build understands is refused, in the same words as today.

## Following the current app

Optional, and off unless `Follow:File` names a file. The rebuild then keeps **one** timetable the same
as the current app's own `.cccc`, so the two can be used side by side with nothing typed twice:

- Every `Follow:EverySeconds` (30) the file's time and length are looked at. When they change, it is
  read whole, brought in through the same import as *Bring in a file…*, and the difference from the
  timetable here is committed as **one batch by *the current app*** (recorded under a sign-in name no
  account can have), so History and Now's *Changed since yesterday* see the current app's changes like
  anybody's. Nothing is written, and nobody told, when nothing differs. That rests on the import
  bringing one file in as one timetable every time, ids and all, which a test holds.
- That batch skips the week lock, because the current app keeps its own, and goes on nobody's undo.
- The first look creates the timetable (*The current app's timetable*) and opens it for everybody. Its
  name here is this app's own, and following never touches it.
- A file that is missing, unreadable or of a newer format changes nothing: the timetable stays as it
  was and the problem is shown to administrators until a good read clears it. The log is told only the
  kind of failure, never the old loader's words, which are made from the file. Whatever goes wrong, the
  app keeps running.
- **Nothing in that timetable is changed here** ([`05`](05-access-and-privacy.md)): the next save over
  there would undo it.
- On the laptop the file is a copy made by a root script that only reads the current app's folder,
  which is root's alone; the copy is renamed into place whole, as the current app saves (the runbook's
  step 8). After every look the script leaves a note beside the copy (`ok`, `missing` or `failed`, and
  when), and the note is asked before the copy: a copier that has stopped or lost the file leaves a
  copy that reads perfectly well, so without it a stale timetable would be called up to date.

## The clock

`Cccc.Domain` never reads the clock. Every rule that depends on today (locking, missing registers,
cover this week) is given `today` by the caller, and the tests pin it. The web takes today in
Europe/Copenhagen, never the container's zone.
