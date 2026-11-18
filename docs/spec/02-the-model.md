# The model

A **project** is one school year's timetable. Everything below lives inside one project. Every thing
with an identity has a `Guid Id`, and every reference is by id. Colours are `#RRGGBB` strings.

## The year

| Thing | Fields | Notes |
|---|---|---|
| **Semester** | `Name`, `StartMonday`, `WeekCount` | Any number; need not cover the year; overlaps warn. Called `Term` in the old code. |
| **Closure** | `Name`, `From`, `To`, `Scope` (whole building or named groups), `GroupIds` | A single day, a range, or a group-scoped closure. A closure scoped at nobody is stored and says it closes nothing. Old name `Holiday`. |

## Places and people

| Thing | Fields |
|---|---|
| **Room** | `Name`, `Category?`, `Colour`, `Has` (equipment ids, the rebuild's own) |
| **Equipment** | `Name` — listed once by the school (the rebuild's own): rigging points, crash mats, a sprung floor |
| **Programme** | `Name`, `Colour`, `IsGuest` (old name `Strand`) |
| **Group** | `Name`, `Colour`, `ProgrammeId?` |
| **Branch** | `Name`, `Colour` — a family of disciplines |
| **Discipline** | `Name`, `Colour`, `BranchId?` |
| **Student** | `FirstName`, `Surname`, `Email`, `GroupId?`, `DisciplineId?` (one group, one discipline) |
| **Facilitator** | `Name`, `Colour`, `Email`, `IsGuest`, `Availability` (day + window list), `MaxWeeklyHours?`, `MaxGap?` |

Initials for a facilitator are the first letters of the first two words of their name.

## What is taught

| Thing | Fields |
|---|---|
| **Area** | `Name`, `WeeklyTarget?` — a curriculum strand a class counts towards |
| **Class** | `Name`, `Colour`, `AreaId?`, `Workload` (Counted, DropIn = "open session", NotCounted), `Effort` (NotSet, Light, Moderate, Hard), `Needs` (equipment ids) and `StudentsEach?` (how many students one facilitator may have in it), all three the rebuild's own |
| **TimeRule** | `Name`, `Kind` (Preparation, Break), `ClassIds`, `Percent` (prep), `Minutes` (break), `Days` (break; empty = every day), `Condition` (TeachesBeforeAndAfter, TeachesThatDay) |

## The timetable

**Session** — one class meeting, on one date:

| Field | Meaning |
|---|---|
| `Date`, `Start`, `End` | When. Lengths snap to 15 minutes in the forms; the store keeps what it is given. |
| `Title` | Shown on the block. Follows its class's name when the class is renamed, unless the session was given a title of its own. |
| `ClassId?` | The class this is a meeting of. |
| `Audience` | `Kind` = Group, Groups, Programme or Everyone; `GroupIds`; `ProgrammeId?`. A Programme audience stores its groups too (as the old app does). |
| `RoomId?` | Where. A session with no room is legal. |
| `Facilitators` | List of `{FacilitatorId, From?, Until?}`; a person with no times is in the whole session. Normalised: unknown ids and whole-session parts are dropped. |
| `AddedStudentIds`, `LeftOffStudentIds` | Exceptions to the roster (see the rules). |
| `Notes`, `Provisional` | |
| `Cancelled`, `CancelNote` | This occurrence does not run. |
| `CoverIds`, `CoverNote` | Who is covering this occurrence. |
| `MovedFrom?` | Set when an occurrence was rescheduled, so the reader can be told "moved from Tue 10:00". |

**Register** — at most one per session, its own collection, keyed by `SessionId`:
`PresentIds`, `ExcusedIds` (never overlapping), `RosterIds` (the list it was taken against),
`FiledBy`, `FiledAt`. Absent is the default mark: anybody on the roster not present or excused.

**Absence** — `Person` (Facilitator or Student), `PersonId`, `From`, `To`, optional `StartTime`/`EndTime`
(both or neither), `Reason?` (free text, administrators only), `Notice?` (Planned, Unplanned),
`Cause?` (Illness, Personal, ApprovedLeave, Other).

**Asking to be away** (`Requests`, the rebuild's own) — `FacilitatorId`, `From`, `To`, optional
`StartTime`/`EndTime` (both or neither), `AskedOn` (the school's date), `AskedAt`, and once answered
`Answer` (Yes, No), `AnsweredAt` and, for a yes, the `AbsenceId` it recorded. A list of its own, never an
Absence, so nothing that counts absences can read one by accident. **There is no field for why.**

**An offer to cover** (`Offers`, the rebuild's own) — `SessionId`, `FacilitatorId`, `OfferedAt`, and once
answered `Answer` and `AnsweredAt`. Whether one is still waiting is worked out from the class, never stored.

**Asking for a space** (`SpaceAsks`, the rebuild's own) — `FacilitatorId`, `Date`, `Start`, `End`,
`RoomId`, `Audience` (the whole building, or groups), `ClassId?`, `Title` (its own name, or blank for the
class's), `AskedAt`, and once answered `Answer`, `AnsweredAt` and, for a yes, the `SessionId` it added. A
class that is not one yet, in a list of its own, so nothing that counts classes can read one by accident.

**Equipment out of use** (`OutOfUse`, the rebuild's own) — `RoomId`, `EquipmentId`, `From`, `Until?` (its
last day out of use; none until somebody says it is back). A piece one space has, out of use there on those
days: the space lacks it then ([`03`](03-the-rules.md#equipment-out-of-use-spacesouton-the-rebuilds-own)).
**There is no field for why.**

**The Scratch Week** (`Scratch`, schema 24 in the current app) — a plan: sessions exactly like the
ones above, in a collection of their own, all dated in one week no semester reaches (the Monday
2200-01-06, `Week.ScratchMonday`, the current app's own). One per timetable, shared by everybody who
edits. Nothing that counts reads it; see the rules.

**Event** — beside the classes, never one of them: `Title`, `Category` (Meeting, Performance, Audition,
Deadline, Trip, Visitor, HostedGroup, Other), `Date`, `EndDate?`, `AllDay`, `Start`, `End`, `Repeat?`
(Daily, Weekly, MonthlyOnDate, MonthlyOnWeekday; `Every`, `Days`, `Until?`/`Count?` — a repeat always
ends), `Skipped` dates, `SkipClosures`, `Audience` (flags: Everyone = 0, Students, Facilitators, Staff,
Administrators), `RoomId?`, `Place`, `FacilitatorIds`, `StudentIds`, `Note`.

## The project's own settings

`Name`, `SchoolName`, `WeeklyScheduledTarget?` (each student's week is measured against it,
[`03`](03-the-rules.md)). Nothing about which week a tab is looking at is stored
in the project: the week box belongs to the tab.

## What is not in the project

Accounts, sign-ins, the operation log and the history are the store's, not the project's — so an
exported project never carries anybody's credentials.
