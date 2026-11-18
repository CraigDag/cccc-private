# The rules

Each of these was decided by somebody looking at the real timetable. They carry over exactly, and
**each has one implementation in `Cccc.Domain`** that every screen, document and report calls. Where the
old app has a class that carries the reasoning, it is named in brackets so the history can be found.

## A week, and what runs in it

- **A week's timetable is the sessions dated Monday to Sunday of that week.** A week nobody has filled
  is blank — a fact the screen states, not an empty page (1.47.0).
- **A week that has wholly ended is locked**: its Sunday is before today. Nothing about its sessions
  changes, except that a register may be filed or corrected by somebody allowed to, and that a merge may
  re-point a class from the class merged to the one kept: a change of `ClassId` and of nothing else, in
  a batch that removes the old class or adds the new one (`Edits.CheckLock`). Undo cannot unlock a week;
  undoing a merge puts the ended weeks back by the same exception. (`Building.FrozenWeeks`,
  `FreezeEndedWeeks`.)
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
  in the same words the class form offers. **The load is the sum of `Hours.StudentLines`**: one line per
  class on their roster, closed and cancelled ones included, each saying why it does not count when it
  does not (the closure by name, a cancellation, or the class's workload), so a figure and the lines
  behind it cannot disagree; a test holds the load to what it was before the lines, for every student
  in every week of the made-up school.
- **A student's week is measured against `Settings.WeeklyScheduledTarget`** (`Hours.StudentTarget`,
  `Hours.ForStudent`) only where the current app's week tally measures one: **inside a semester** (a
  school with no semesters is never outside one), and **never for a visiting programme's student**,
  whose week is not the school's curriculum. **A week in which nothing of theirs counted is not called
  short** (the school closed, every class cancelled, nothing on): it is not a timetable falling short,
  a report that turns every holiday week amber is one nobody reads, and its lines say what happened.
  Short is advice.
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
teaching at that time (the week as it runs), then **whoever has offered to cover it** (below), lightest
week first, then by name. Busy or over a limit is advice, never a refusal.

## Asking (`Asking`, the rebuild's own)

A facilitator asks for themselves, through the account linked to them, and an administrator answers.
**Nothing on the timetable changes until a yes**, and a yes records exactly what the forms would.

- **I can't come** (`TimeOffRequest`) — days from today on, all day or between two times. Before sending,
  the coach is shown what it would take them off (`Asking.WhatAYesLeaves`): each class they are in the
  room for whose part of it the times reach, and what a yes would leave it, resolved against the
  timetable with the absence in it — *needs cover*, or *still taught* by whoever else is in for all of it.
  A yes records an ordinary absence (`AsAbsence`): the same days and times, **planned** when asked before
  its first day and unplanned on the day, with no reason and no cause, since nothing asked for one. A no
  records nothing. A request waits until its **last** day has passed; taken back only by whoever asked,
  while nobody has answered.
- **I can cover that** (`CoverOffer`) — any class needing cover that is not theirs and has not been.
  *Could you cover?* on My week lists the ones in the next two weeks that have not started and that the
  Cover ranking finds them free for (`Asking.CouldCover`); a co-taught class whose gap is somebody else's
  part is still theirs, so not offered (an administrator can still stretch their part from the Cover
  form). A yes makes them its cover, as the Cover form would (so pressing an offerer in the Cover form is
  the same yes); a no records nothing on the timetable. **Whether an offer still waits is worked out from
  the class** (`Asking.StateOf`): covered by anybody, however, it waits for nobody; a class gone or passed,
  passed.
- **Nothing waits for somebody no longer on the timetable** (gone, or kept as former): an answer to them
  is refused, so asking for one would be a dead end. Removing a facilitator removes everything they asked
  and offered; a yes's absence stays as absences do.

## When what a class needs is elsewhere (`Busy`)

WHO is in what SPACE and WHEN, asked on behalf of one class for one day: the stretches its people,
groups and space are elsewhere. Find a time leaves a time out by it and the plan shades a chosen class's
day by it, so the times listed and the picture cannot disagree.

- **Each facilitator**, for their part of the class: in another class's room, teaching it or covering
  it for their part of it (`Teaching.InRooms`, which `Teaching.At` asks too); recorded away, for the
  times given or else the whole day; named on an event the reader may see.
- **Its groups**: any other class on for one of them, lunch included. An open session is not, because
  nobody is required at it. A whole-building class needs every group.
- **Its students**: any other class some of them are on, and an event the reader may see that names
  some of them. Counted, never named.
- **Its space**: another class in it, and any event in it whoever may read the event; one the reader may
  not see is only *booked*.
- The class itself never stands in its own way, and a cancelled or closed class keeps nobody busy.
- Being busy is information, never a refusal: bounds advise, never refuse.

## Finding a time (`Openings`)

WHO is in what SPACE and WHEN, asked the other way round. Given a class as it would be (who teaches it,
each in their part; who it is for; how long) and the spaces it may go in, the times it fits, soonest
first, from now. A time is offered only when **nothing it needs is busy then** (`Busy`, each facilitator
for their part; its own space aside, since it may go in another) and one of its spaces has no class and
**no event** in it (`Spaces.Booked`): any event with a space, whoever may read it, so a space is never
offered over a booking the reader cannot see.

Everything else the clash check would say about the time (a limit, a long gap, outside somebody's
hours, a whole-building class over it) is a **note beside it, never a reason to leave it out**: bounds
advise, never refuse. So is an event its people are expected at (a staff meeting, for facilitators; a
showing, for students), and how it sits in each person's and group's day: *comes straight from*, *goes
straight on to*, *has nothing else that day*. A group's day counts only counted classes, so lunch is
nothing to come straight from.

- A class the timetable has is looked for **as itself**: never in its own way, its own time not offered
  as another, and each person's part moved with it.
- One entry per stretch where it can start at any quarter hour, saying the latest start, in the spaces
  free at the first, its own first.
- **Not looked in, and said**: a day the building is closed, or closed for a group it is for; a week
  with nothing in it yet (a week ahead is blank until it is filled, so nothing there can be checked). A
  weekend is looked in only when the timetable has a class that day; the Scratch Week never.
- **A day looked in with no time says why**: somebody away all of it; never all free at once; free
  together for so long at most; or no space free when they are.
- At most 30 times, and how many more; at most half a year of days.
- **Moving a class to a time found** (`Edits.MoveTo`) carries each part and takes the space chosen. It
  can say it has moved (`MovedFrom`, kept from the first move and dropped when it goes back there),
  which stays inside its own week: a move to another week is a copy.

## Ways out of a clash (`WaysOut`, the rebuild's own)

A clash says what is wrong; a way out is the change that ends it. For a class in a clash that rings its
block (a space, a person, a group or students in two places at once, or somebody outside their hours),
not started yet and on a real week, **at most three ways out**, each the class moved so that it is in
**no clash at all**: every one is put to the clash check itself (`Clashes.Find`), so taking one never
trades this clash for another.

- **First, the same time in another space**, when its space is all that clashes, so nobody's day moves.
  At most two, so another time is among them whenever there is one: a different space is not always the
  better answer.
- **Then another time the same day in its own space**, nearest the time it has. From each stretch Find a
  time finds, the time in it nearest that one which is in no clash, so each is a different gap in the
  day: outside somebody's hours, the stretch's first time inside them, not its end.
- **Only when there is neither**, another time in a space like it, since moving both is the bigger
  change.
- **A space is like it** when its class has been in it before (the most used first) or it is of the same
  kind; a space with no kind is like no other. Never a former space, and never one an event books.
- **The day looked in** runs from 08:00 to 18:00, or as early or as late as the class already is.
- **Beside each**: in another space, whether its class has been there before; at another time, Find a
  time's notes, less what is against it where it is already (a limit it is over wherever it goes, an
  event all that day, somebody with nothing else that day), which is no reason for one time over another.
- **Taking one** (`Edits.TakeWayOut`) works them out again from the timetable as it is now and refuses one
  no longer offered, so a change somebody else made meanwhile is never walked into. Once its week has
  begun, another time says it has moved, as Find a time does. One Undo step.
- **None** for a class that only needs cover (that is Cover's), is cancelled, has started, or is in the
  Scratch Week. Moving the other class is that class's own way out, offered when it is chosen; another
  day is Find a time's.

## Making room (`MakeRoom`, the rebuild's own)

When Find a time finds no time at all, the times when **only one other class** is in a class's way, each
with that class moved to one of its own ways out, so that afterwards neither is in any clash. **At most
three**, the soonest first, as Find a time lists them.

- **In the way** is Find a time's own question: the class's people, groups or students elsewhere, or the
  space asked for in use. A time counts when one class and nothing else is the cause, whatever it keeps
  (its people, the space, or both). Never round somebody away or an event, which are not the timetable's
  to move, and never two classes: one move is a change to look over, a chain of them is a reshuffle
  nobody asked for.
- **Where the other class goes** is the first of its ways out (`WaysOut`) with the class placed there,
  put to the clash check like every way out; and the class has to be in no clash once it has gone.
- **Each other class once**: at the first time it alone is in the way, or, when it has no way out then, at
  the last. Those two are enough: a way out that moves it later only needs the class to start as early as
  it can, one that moves it earlier only needs it to start as late as it can, and anything else about
  where it goes is the same at every time between. Twelve other classes at most are looked at, each a
  search of its day, or two.
- **Never** before now, at the time a class already has, on a day closed for its groups or a weekend
  nothing uses (as Find a time), or round a class that has started.
- **Beside each**: Find a time's notes for the class as it would be once the other has gone, and the way
  out's own notes for the other class.
- **Making it** (`Edits.MakeRoom`) moves both in one step, which one Undo takes back. It works the way out
  again from the timetable as it is now and refuses one no longer offered, so a change somebody else
  made meanwhile is never walked into. The other class says it has moved once its week has begun, as
  Find a time does.

## Hard training (`HardTraining`, the rebuild's own)

How hard a group's day is on the body. A circus school's injuries come from loading, not from clashes, and
the timetable is where the loading is decided; the current app has no idea which classes are hard.

- **A class says how hard it is** (`Class.Effort`: not said, light, moderate, hard), once, in Classes. Only
  **hard** counts towards the figure; moderate is drawn beside it on the plan and counts for nothing.
- **A group's hard training** is the classes that are hard, on for that group (a whole-building class is
  on for every group), **counted** (an open session or a break is nobody's required training) and that
  **happen** (cancelled and closed are not; waiting for cover still is, because the students are still
  timetabled to be there, as with a student's hours).
- **A stretch** is hard classes with less than the school's **rest** between them (`RestBetweenHard`, half
  an hour when it has not said): back to back and overlapping run together whatever the rest. Its figure
  is the **time in those classes**, not the span: the short breaks are not training, but they are not a
  rest either. A group double-booked counts the overlap once.
- **The limits are the school's** (`MostHardInARow` for a stretch, `MostHardInADay` for the day), each
  optional. Past one is a warning beside the classes it is about (`GroupHardInARow`, `GroupHardInADay`),
  **never a ring and never a refusal**: there is nothing double-booked to move out of the way, so no ways
  out, nothing for Make room to count as in the way (`MakeRoom.InTheWay`), and none in the term's issues
  or Now's clashes. Exactly the limit is not past it. **No limits, no warnings**, and the figures are
  still there to look at.
- **The add form and Find a time say it** like any other warning, because they ask the clash check what a
  class would add: the figure growing past a limit is new, so it is said.

## Equipment (`Spaces.Lacking`, the rebuild's own)

WHO is in what SPACE: the space has to be fit for the class, and the current app knows only a space's kind.

- **The school lists its equipment once** (`Timetable.Equipment`); a space ticks what it has (`Room.Has`)
  and a class what it cannot be taught without (`Class.Needs`). Nothing needs anything until the school
  says so.
- **A class in a space without everything it needs is in the wrong space**: one warning on that class
  naming all it lacks there, in the order a person reads names (`SpaceLacksEquipment`). It is **a ring**,
  unlike hard training, because it is a placement with a fix: it has ways out, and it counts in Issues,
  Now and the term. A class with no space is in none rather than the wrong one, and a piece taken off the
  list is needed by nobody.
- **Its ways out** are the same time in a space that has it (every such space is offered for a class
  that needs anything, after the ones it has used and those of its kind), then another time in one; a
  time in the wrong space is no way out.
- **Find a time and Make room look only in spaces that have it** (`Openings.SpacesFor`) and say which of
  the spaces asked they left out (`OpeningsFound.Unsuited`), so a missing space is no mystery. Bounds
  advise: the add form still saves a class in the wrong space, warned.
- **Deleting a piece takes it off every space and class in the same step** (`Edits.RemoveEquipment`),
  so one Undo puts all of it back. Nothing is kept as former: the past has no need of it.
- Next year carries the list, and the spaces' and classes' ticks, as they are. **The store's format 5**
  brought it: a format-4 build would replay a piece as a collection it has never heard of, and save a
  space or a class back without its ticks.

## What a class is usually like (`Habits.Like`, the rebuild's own)

A class typed in a line (*aerial fri 13*) says little; the rest is what that class usually is.

- **The most like session of the class**: for the groups or with the facilitators named, if any were;
  then on the same weekday; then nearest the start named; then the latest before that day, and only
  when there is none before, the soonest after. A cancelled one did not run, so it is never the one, and
  nothing former comes with it (`Former.Strip`, as for every copy).
- **It fills only what the line did not say**: who it is for, the space, who teaches (keeping who is in
  only part of it only at the very same times), the start, the length. What the line says always wins.
- **It is a suggestion, never a rule anything counts by**: the add form shows the result, saying which
  day the rest came from, and nothing is saved until OK.

## What an absence takes somebody off (`Away`, the rebuild's own)

Said before it is recorded, by the absence form, by a coach asking and by a line typed into Find, so all
three say the same (`Asking.TakesOff` and `WhatAYesLeaves` ask it too).

- **A facilitator**: each class on its days that they are in the room for, teaching or covering, whose part
  of it the absence's times reach; and what recording it would leave it, resolved against the timetable
  with the absence in it: *needs cover*, *still taught* by whoever else is in for all of it, or, for a
  class they cover, its cover to change.
- **A student**: each class they are on (`Roster`) whose time it reaches. A whole-school class has nobody
  on it, so lunch is never missed. Being away changes nothing about how a class runs.
- **An absence being changed is read without itself**, so it still lists the classes it takes them off.
  Cancelled and closed classes take nobody off anything.
- **Its notice, when nobody says** (`Absence.NoticeWhenSaid`): planned when it is said before its first
  day, unplanned on the day, and an illness always unplanned, since nobody plans to be ill even when they
  phone the night before. A coach's yes, which has no cause, is planned when asked before its first day.

## A week against the one before (`WeekChanges`)

- A week is compared with **the last week before it with anything in it**, at most half a year back: a
  blank week between is a timetable not built yet, not every class gone.
- **Compared as it is written**: the day, the times, the space, who teaches (and for which part), who it
  is for, its name. Cancelled, closed and covered are the week happening, not its timetable changing;
  registers, notes and roster exceptions are not compared. So a week copied from the one before is the
  same.
- **The same class is found again by what it is** (its class, else its title): first every pair that is
  the same, then the same day and groups, then the same day, then the same groups, the nearest time
  first. What is left over is new or gone.

## The term at a glance (`TermGlance`)

- The weeks of the semester the plan is on, side by side; outside every semester the stretch around the
  day, at most 30 weeks.
- **Each week's figures are that week's own rules asked of it**, never a second count: the classes that
  happen (`Occurrences`), the clashes ringed (`Clashes`), the classes still needing cover (`Cover`), the
  registers missing that the reader may file (`Registers`), the events the reader may see
  (`EventSchedule`), each person's hours (`Hours.ByFacilitator`), each group's counted hours (a class
  named for the group; a whole-building class is never a group's) and how many of its students are short
  of the target (`Hours.GroupTarget`, `Hours.StudentLoad`).
- A week is blank, closed (the whole building on all five weekdays) or built. A group closed all week
  says so, rather than reading nought hours short of its target.
- Hours are a person's own and an administrator's to see; anybody else reads how many classes. A row is
  shaded only against what the reader may see, so a shade says nothing of anybody else's hours.

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
- **An event with a space books it**: Find a time and the class form never offer that space as free
  then, whoever may read the event (one the reader may not see says only *booked*).
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

## Next year (`NextYear`, the current app's *Start next year from this one*)

Next year is this year's shape with an empty history. Two differences from the current app, each for a
reason:

- **A new timetable beside this one**, never this one changed: this year's registers, absences and
  history stay where they happened, and nobody has to remember to keep a copy first. So the form is
  allowed on a timetable that follows the current app: it changes nothing there.
- **Every semester starts in the same week number next year** (`NextYear.SameWeekNextYear`), moving by
  its own 52 or 53 weeks, and whatever is dated inside a semester moves with it. Moving everything 52
  weeks, as the current app does, starts the year a week early after a year with an ISO week 53: week 34
  of 2026 plus 52 weeks is week 33 of 2027.

What carries over and what stays is the current app's. The spaces, the people, the groups (unchanged:
nothing can know who has graduated), the catalogue, the time rules, the settings and the Scratch Week
carry over; the timetable carries over as **its first timetabled week only**
(`NextYear.FirstTimetabledWeek`: the first at or after the year's start that has a class), copied as
Copy week copies, so nothing that happened that week travels, because a week ahead is blank until
somebody fills it; registers and every other week stay with this year. **Events and absences are dated
to the calendar and never move**: one with a date in the new year is kept (an audition already booked,
a leave across the summer), the rest stay with this year, and **asking to be away** follows its days the
same way; an offer to cover was for one of this year's classes, so none carries over. **Closures are a
tick, off by default**: kept,
each moves with its semester, so week-numbered ones land right and Christmas and Easter do not.
`NextYear.For` is the plan the form shows and `Make` carries it out, so the two cannot describe
different years, and a test fails when a collection is added to the timetable without a decision here.

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
- **Deleting a class, a space or a facilitator used before today keeps it, as former** (`FormerSince`,
  `Rules/Former`). The days before today keep it whole: its name, colour, area and workload, where a
  class was held, who taught it, so hours, credit and reports over those weeks add up as they did. From
  today on (the Scratch Week included) it is taken off, stated: a class's sessions keep the name they
  showed with no class, a space's run with no space, a person comes off them; a space stays on an event
  already over, a person keeps the absences that began before today. Nothing ahead offers it: pickers,
  Find (last, marked former), Now, the plan's rows, counts, cover. Every copy (a week, a day, the Scratch
  Week, next year, Duplicate) leaves it out through `Edits.CopyOf`, and next year does not carry it.
  *Bring back* offers it again and restores nothing that deleting took off; Undo does. One never used
  before today is removed outright (a class comes off every time rule too). Students are not kept this way: a
  filed register keeps their id, and nothing more of a person who has left is held than that.
- **Merging two classes** (`Edits.MergeClass`) re-points every session of one to the other, the ended
  weeks included, removes it and takes it off the time rules. In weeks not ended, a session titled with
  the merged name takes the kept one's; ended weeks keep the titles they ran under. The form says first
  what else changes (`Edits.MergeDifferences`): the area, whether it counts for a student (only when
  that really changes), each time rule that stops or starts crediting it. A class cannot be merged into
  itself or into a former one.
- **Undo is per person**, and keeps at most the last 100 of their own steps.
