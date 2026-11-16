# Screens

The calendar is the shape a timetable is read in, so a project opens on the week. Dark first
(Setsquare Dark on a first visit, mint accent), with the four dark palettes offered. Everything works
at 360px; below 640px the week becomes a day list.

## The shell

- **A rail** on the left: the project's name, then SCHEDULE (Week, My week, Registers, Cover), MANAGE
  (Data, Reports, Administration — administrators only), and at the foot the signed-in person, Change
  passphrase and Sign out.
- **A status bar**: who else is here ("Also editing: …"), the last change and who made it, and the
  version at the right-hand end. The project name is never in the browser tab's title.
- **One dialog at a time.** Forms put OK before Cancel (Close where nothing is pending), Enter commits,
  Escape closes, focus is trapped inside.

## The week

- A header: the week number tile, ◂ date box ▸ and *This week* (the tab's own week, kept in
  `sessionStorage`, never saved in the project).
- A context bar: *Add session…*, *New event*, *Cover…*, *Copy ▾* (Copy day…, Copy week to…, Clear the
  weeks after this one…), and when something is chosen, its verbs behind its name.
- A filter bar: *+ Filter* (search, programme, group, room, facilitator, student; also show open
  sessions, guests, events), each active filter a chip with its ×, and beside it the counts of
  **clashes**, **cover needed** and **registers missing** (a button, opening exactly what it counts).
- The calendar: days across, hours down, fitted to the week; blocks as in the rules; a dashed edge and
  a COVER tag on a block needing cover; a solid warning ring on a clash; events as a thin line at the
  top of their day or a strip for all-day ones. Click to choose, double-click to edit, drag to move,
  drag an edge to stretch (quarter hours, one undo step), drag a box to choose several, Delete removes
  what is chosen. A locked week draws as read-only and says so.
- **The week is the same for everybody** on the screen, filtered for the reader as the rules say.

## Forms

- **Session**: class (sets title and colour), audience (group, groups, programme, everyone), date and
  times, room, facilitators each with optional from/until, students added or left off, notes,
  provisional. Warnings (clashes, over hours) shown folded below, never blocking.
- **Cover…**: the week's sessions needing cover, each with the ranked list of who could take it.
- **Register**: the roster with Present / Excused / Absent, pre-filled from known absences, filed with
  who and when.
- **Event**: title, category, dates, times or all day, repeat (always ending), audience, place, people.

## Data (administrators)

One page per kind, each a list with a form: Rooms, Programmes, Groups, Students, Facilitators, Classes,
Areas, Disciplines and branches, Time rules, Semesters & closures, Absences. Every list is in Danish
name order; a thing's own colour is a spine beside its name, never text colour.

## Reports (administrators)

Overview, Utilisation (by programme, group, area; whole-building last), Attendance (per class, per
student), **Facilitator hours** (week by week, and the breakdown of teaching and each time rule), and
the registers outstanding.

## My week

A facilitator's own week through their link: their sessions (their part of a session drawn as their
part), their hours against their limit, their registers to take. Printable as their own week.

## Administration

Accounts (add, role, tags, link, reset, suspend, remove), Projects (new, import, export, rename,
delete, history and putting an earlier moment back), System (last backup, last update), Erase a person.

## Printed documents

The calendar view of the week as shown (filters stated on the sheet), the list, the room grid one page
per day, and a calendar per student. One heading rule: `CCCC Scheduler · Week 37, 7–13 September 2026 ·
<shape>`, never the project's name. Every PDF generation goes through one gate (QuestPDF loses text when
two generate at once).
