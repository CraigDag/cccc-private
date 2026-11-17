# The rules

Each of these was decided by somebody looking at the real timetable. They carry over exactly, and
**each has one implementation in `Cccc.Domain`** that every screen, document and report calls. Where the
old app has a class that carries the reasoning, it is named in brackets so the history can be found.

## A week, and what runs in it

- **A week's timetable is the sessions dated Monday to Sunday of that week.** A week nobody has filled
  is blank — a fact the screen states, not an empty page (1.47.0).
- **A week that has wholly ended is locked**: its Sunday is before today. Nothing about its sessions
  changes, except that a register may be filed or corrected by somebody allowed to. Undo cannot unlock
  a week. (`Building.FrozenWeeks`, `FreezeEndedWeeks`.)
- **What an occurrence's state is, in this order** (`ScheduleResolver.Resolve`):
  1. a **closure** covering its date and applying to it → *closed* (not drawn by default, never counted);
  2. **cancelled** → *cancelled* (drawn struck through on request, never counted);
  3. **covered** (`CoverIds` not empty) → *runs*, with cover;
  4. otherwise, if an **absence** leaves part of the session with nobody in it → *needs cover*;
  5. otherwise → *runs*.
- **A closure applies to a session** when it is whole-building, or when it names groups and *every*
  group the session is for is among them. A whole-building session is closed only by a whole-building
  closure.
- **A session needs cover only when every assigned facilitator is away for some stretch of it** — with
  per-person times, when an away person's window is not covered by somebody present
  (`FacilitatorParts.LeavesAGap`). No softer "someone away" marker for a co-taught class, deliberately.
- **A reschedule moves the session and leaves the person still away.** A moved class is checked
  against absences at its new time.

## Who is in the room

- **In the room** = the assigned facilitators who are not away for their window, plus the cover.
  Cover substitutes the absent; it never replaces the roster, so covering a co-taught class keeps the
  co-teacher who is still teaching.
- **A person's window** in a session is their own `From`/`Until`, clamped to the session. **A person
  covering takes on the windows of the people they replaced** (their union), widened by their own
  window if they were already assigned (audit finding C2).
- **A session awaiting cover counts for no facilitator.**

## Who is on a class (`SessionRoster`, schema 17)

- **Roster = the students of every group the session is for, plus `AddedStudentIds`, less
  `LeftOffStudentIds`.** A Programme audience is its groups. **A session for Everyone has nobody on it
  and no register.**
- Every reader asks this one function: the register, the missing count, attendance, a student's hours,
  the student clash, the per-student calendars.
- **A filed register keeps the roster it was taken against**, so moving a student between groups
  later cannot rewrite who was absent.

## Where somebody is now (`Where`)

- **One question, one implementation**: for a student, facilitator, group or space at a moment, the
  class they are in and the next one that day, and whether the person is recorded away. Each kind says
  what is theirs the way the rest of the app does: a student by the roster (so an Everyone class is
  nobody's), a facilitator by being in the room for their own part (cover included, away not), a
  group by the class being for it (a whole-building class is), a space by the class being in it.
- Only classes that happen count, and a class ending as the moment starts is over. A student who is
  away but on a class's roster is *away from* it, not simply absent from the answer.

## What changed lately (`Recent`)

- **Read from the timetable's own history**, never a second record: a class is a day's if it was on
  that day when the window opened or is on it now, so a class moved away from the day is news there
  too. The window opens at the start of the weekday before today (on a Monday, Friday's).
- **Only what somebody in the building would notice**: the day, the times, the space, who teaches (and
  for which part), cover, who it is for, its name, cancelled. Registers, notes and roster exceptions
  are not the timetable changing. A change put back inside the window, moved and moved back or
  undone, is no change.

## Registers

- **Three marks**: Present, Excused (away and the school knew), Absent (the default, by saying
  nothing). The two stored lists never overlap. *Late* was refused on purpose.
- **The mark is stored, never derived from absences.** Known absences pre-fill the excused list when a
  register is opened; what is filed is what somebody wrote down.
- **No reason is stored on a register**: a reason is health data about an identifiable person, and it
  lives on the student's `Absence`, entered once.
- **A register is missing when three things are true: the session ran, somebody is on its roster, and
  its date is before today.** A register with nobody ticked is not missing: it says nobody came.
  (`RegisterGaps`.)
- **Who may change a register** (`RegisterFiling.MayChange`): everybody may read every register; a
  facilitator may change one for a session they were in the room for (assigned or covering), through
  the account linked to them; administrators change all. Editors are narrowed like anybody else.
- **A week's "N registers missing" counts only what the reader may file.**
- **The morning register** (1.64.0): a student's absence says whether it was planned and why, from a
  short list; the free-text reason is for administrators.

## Hours

- **Facilitator hours go to whoever is actually in the room**: cover counts for the substitute; a
  co-taught session counts in full for both; a session awaiting cover counts for nobody; a person in
  part of a session counts their part.
- **Time rules** (`TimeRules`, schema 18) credit time beyond teaching, set in one place:
  - **Preparation**: each of the rule's classes earns `Percent` of its length (each person's own
    length) for each person in the room, per occurrence, **never divided between co-teachers**.
    Rounded down to the minute.
  - **Break**: a break session (its class is on the rule, on one of the rule's days) credits `Minutes`
    to each person who meets the condition that day — *teaches before and after it* or *teaches that
    day* — counting only Counted classes as teaching, and never somebody teaching during the break.
    When several rules match, the largest wins. **Credited once per person, per day, per rule** (audit
    finding C1).
  - Reported **beside** teaching under the rule's own name, and counted towards the limit.
  - It is a facilitator's number with no student equivalent and reaches no area target.
- **Every hour of a facilitator's can be explained, one line each** (`Hours.Lines`, the current app's
  1.65.0): each class they were in the room for (their own part of it; a class covered under whoever
  covered it, naming who they stood in for) and each time rule's credit, a credit straight after the
  class it was earned on. Built from the same two lists as every total, so the lines add up to the
  figure on Reports, My week and the plan; a test holds that for every facilitator in every week of
  the made-up school. Craig, 2026-11-16, could not find an hour and a half of a coach's week on the
  timetable: it was a class covered and a rule's credit, neither of which a week's timetable shows
  under that person. Who may see them is `Access.MayHaveTheWeekOf`: an administrator anybody's,
  everybody else their own.
- **Student load counts `Counted` classes only** — plus a session whose class no longer exists — and
  **a session awaiting cover still counts** for a student (welfare and curriculum, not contract; Craig,
  2026-08-22). Do not "fix" it to match the facilitator rule. A line the total does not count says so,
  in the same words the class form offers.
- **Whole-building hours are their own last row** in utilisation, never credited to every cohort; every
  cohort is seeded at zero.
- **Both facilitator limits warn and never block.** Weekly total (teaching + rules) over
  `MaxWeeklyHours`; a gap between two of a day's windows longer than `MaxGap`.

## Clashes (`ScheduleConflicts`)

All warnings, computed over a week's running occurrences:

- **Room double-booked**: two sessions in one room overlap.
- **Facilitator double-booked**: a person's windows in two sessions overlap.
- **Facilitator unavailable**: a person with availability set is in a session outside it.
- **Student double-booked**: one student on two overlapping rosters.
- **Group double-booked**: a wider session (its groups a proper superset of the other's; Everyone is
  every group) overlaps a narrower one — the same people in two rooms. Raised even for open training
  and lunch, deliberately, and never derived from the lanes.
- **Needs cover**, **over hours**, **long gap**, as above.
- Availability feeds only the clash check.

## Cover (`CoverSubstitutes.Ranked`)

Offer **everybody not away**, ranked rather than filtered: non-guests before guests, free before
teaching at that time (the week as it runs), lightest week first, then by name. Busy or over a limit
is advice, never a refusal.

## Guests (schemas 20 and 21)

- **A guest facilitator is nobody's account**: no link offered, no address match, and an existing link
  answers nobody — so they can file no register.
- **A guest class** is one whose every group belongs to a guest programme; a shared or whole-building
  class never is. **Guest classes are off the screen until asked for** (Hide guests starts true on
  every visit, remembered nowhere); a programme or group filter shows them, a person filter shows that
  person's own, My week always shows a coach's own.
- **Hiding is drawing, never counting.** A hidden guest class still clashes, needs cover and counts hours.

## Events (schemas 16 and 19)

- An event never counts: no hours, no clash, no register, no cover. Repeats always end.
- **Who sees an event is one rule** (`EventViewer.Sees`): administrators see all; editors all but
  Administrators-only; everybody else sees Everyone, plus Students / Facilitators / Staff by their
  account's tags and link. Only administrators may choose Administrators.
- Every download and print asks the same rule; **an Administrators-only event never prints**.

## The Scratch Week (`ScratchWeek`, the current app's 1.67.0)

One week with no dates, to try a timetable out in without touching a real week. Craig asked for it in
the current app with four safeguards, and all four hold here:

- **Copy in, never move.** *Copy to Scratch Week* on a class puts a copy there, on the same day of the
  week, with an id of its own, and leaves the class where it is. *Copy this week to Scratch Week*
  replaces the plan with a copy of the whole week.
- **Nothing in it counts.** Its classes are a collection of their own (`Timetable.Scratch`), never
  `Sessions`, so hours, time rules, a student's load, registers, cover, clashes with the real
  timetable, Now, Find, the printed sheets and the reports cannot see it without somebody writing a
  line to make them. Drawn, it has its own clashes (a plan double-booking a space is worth seeing) and
  nothing dated: no closures, absences, events or registers.
- **Editors and administrators only** (`Access.MaySeeScratch`). A coach is never offered it, and the
  server refuses a change to it from anybody else.
- **One step to bring it back.** *Place onto a week…* puts the plan onto one real week, replacing that
  week's classes, each on the same day and time; it is one change, so one Undo takes it back. It
  refuses a week that has ended or has a register taken, exactly as Copy week does. The plan stays, so
  it can be placed again.

Every edit of a class (add, change, drag, Copy a day, delete) works on it through `ScratchWeek.Edit`,
which makes the edit on the plan and writes it back to `Scratch`. **`Edits.CheckLocks` keeps the two
apart on every change**: nothing real is dated into the Scratch Week, and nothing of the plan leaves it
except by being placed. Deleting a space, person, group, programme or class, and renaming a class,
reach the plan as they reach the weeks that are not locked.

## Drawing the week (`WeekLanes`, `the-week-calendar.md`)

- A block names its class's title, time, group, room — in that order of importance — and is coloured
  by its **class** (a class-less session takes a neutral, never its group's colour).
- **A day splits into lanes only when two or more groups have sessions of their own that day.** A block
  nothing runs beside owns the whole column. Several blocks of one group that only run beside each other
  share the column. A week filtered to one person has no lanes. Nothing is drawn for a lane itself.
- **A shared session that something runs inside keeps its whole span and is drawn beneath it**, its
  words starting clear of what covers them.
- **A block widens into a neighbouring lane that is empty for its whole time** (1.64.2).
- **The grid is fitted to the week's own hours** (earliest to latest plus an hour each side), asked of
  the week's rows and never the filtered ones; an hour is a fixed height.
- **Open sessions** (DropIn classes) can be hidden; the tick starts on every visit and is remembered
  nowhere. Lunch is NotCounted and is not an open session.

## Names

- **Every name a person reads is ordered in Danish** through one comparer (`NameOrder`).
- **No project contents and no project name are ever logged, and no name reaches a URL.**

## Editing rules that are decisions

- **Copy week replaces the destination week** and never merges; registers, cancellations and cover do
  not travel with a copy. Copying a week again never pulls a class back out of another week it has
  since been moved to. **Copy day appends.** Both say how many sessions they will overwrite first.
- **Clear the weeks after this one** empties every week after today's (clamped inside the year), on a
  press, never automatically.
- **Renaming a class renames the sessions still carrying its old title**, in weeks that are not locked.
- **Deleting a room leaves its sessions without a room**, stated, never a side effect.
- **Undo is per person**, and keeps at most the last 100 of their own steps.
