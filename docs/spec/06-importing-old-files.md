# Importing old files

The school's term lives in a `.cccc` on the laptop. Switching to the rebuild must never mean typing it
in again, so the rebuild reads **every `.cccc` from schema 1 to schema 23**.

## Two steps, kept apart

1. **Read and migrate, exactly as the old app does.** `src/Cccc.Legacy` carries the old app's own file
   model, `ProjectPackage.LoadFromBytes` and `ProjectStore.Validate` — the migration blocks for 2→23 and
   every repair scrub — copied from `cccc-scheduler` at 1.66.0 (`2f2f3a5`) with only what they need, and
   with nothing that writes a file. Re-implementing twenty-two migrations would be the riskiest code in
   the rebuild for no gain; copying them means a file opens here exactly as it opens there. The copy is
   frozen: it is never edited except to keep it compiling.
2. **Translate the migrated schema-23 project into the new model** (`LegacyTranslator`). This is new
   code, and it is where the structural changes in [`01-what-changes.md`](01-what-changes.md) meet old
   data.

A file newer than schema 23 is refused, in the old app's words.

## The translation

| Old | New |
|---|---|
| `Term` / `Terms` | Semesters |
| `Holiday` | Closure |
| `Strand` | Programme |
| `ClassEntry`, `Area`, `TimeRule`, `Branch`, `Discipline`, `Room`, `Group`, `Student`, `Facilitator` | the same, field for field (floor plans and room geometry are dropped: the floor plan is off the screen) |
| `Absence`, `SchoolEvent` | the same |
| `ScheduleEntry` | one **Session per week it runs in**, dated, as below |
| `SessionException` | `Cancelled` / `CoverIds` / a moved date and time with `MovedFrom` |
| `AttendanceRecord` | a **Register** on that week's session |

**Which weeks a session lands in**:

- **Weeks are separate** (`WeeksAreSeparate`): each session already has its week; it becomes one
  dated session.
- **A frozen week**: its sessions already carry their week; each becomes one dated session.
- **The standing pattern** (`WeekMonday` null): it is stamped onto **every semester week that is not
  frozen**, plus any week a pattern session holds a register or one-off change for — exactly the weeks
  the old app draws the pattern on inside the year, so nothing the school sees changes. (The old pattern
  also ran in weeks outside every semester; here those weeks are blank, which the round-trip test pins.)
  Each stamped copy gets a **stable id** derived from the pattern id and the Monday, the old app's own
  `FrozenCopyId`, so an old register keyed to a pattern session lands on the right copy.
- An exception or register keyed to a week where its session does not land is kept on the nearest
  week that has it, as the old `SeparateTheWeeks` does, and the import report counts these.

**An import report** is shown after every import: how many of each thing came across, how many weeks
were stamped from the pattern, and anything that could not be placed — by count, never by name.

## Testing it

- **Fixtures carry real (made-up) data in every list**, because the 2→3 migration once shipped untested
  when every fixture's holiday list was empty.
- The old app's test fixtures for schema migrations are copied with the legacy code.
- **A round-trip test**: build a project in the old model, import it, and check every week's running
  sessions, clashes, hours and registers match what the old resolver gives for the same week.
- **The upgrade trial on the live file is Craig's**, in a scratch folder on his machine, never in this
  repository.
