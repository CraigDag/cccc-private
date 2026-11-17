# What changed, for the person running the school

## 2.0.0-preview.1 — 2026-11-17

The whole app rebuilt. Everything the school uses today is here, with these differences you would notice:

- **Every change is kept the moment it is made.** There is no Save. Two people can work on the same week
  at once and each sees the other's changes straight away.
- **Undo takes back only your own changes**, in order, and says so if somebody else has changed the same
  thing since.
- **Every timetable has a history.** Administration ▸ Projects ▸ History shows who changed what and
  when, and can put the whole timetable back to how it was at any point (that is itself a change, so it
  can be undone too).
- **A week ahead, a week now and a week gone are all simply dated classes.** Copy week to… fills the
  weeks you choose and never overwrites a week that already has a register taken in it.
- **A group put in two places at once is one warning**, naming the students, rather than one warning
  per student. The same group booked twice is warned about even before anybody is enrolled in it.
- **Administration ▸ System says when the nightly backup last ran**, and turns amber after a missed
  night.
- **Erasing a person** (Administration ▸ Erase a person) removes their name from every timetable and
  every stored copy, keeps the counts adding up, and unlinks any account linked to them.
- **The morning register is here**: Data ▸ Absences opens with today's students. Tap who is away,
  planned or unplanned, and pick a reason; their registers open with them excused, and a register
  already taken changes them from absent to excused (never from present).
- **Import people from a spreadsheet**: Data ▸ Students and Facilitators take rows pasted from Excel,
  show what would be added (and who is left out, and why) before anything changes, and one Undo
  takes the whole import back.
- **The line at "now"** on today's column moves by itself through the day.
- **Bring in a file…** reads the current app's `.cccc` (every version) and says what came across.

Not carried over yet: the end-of-day register reminder email (off by default in the current app; it
needs the school's mail settings). Not carried over: Google sign-in (the school uses its own accounts), the floor plan (off the screen
since 1.31.0), and the old desktop apps.
