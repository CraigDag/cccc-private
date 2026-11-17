# What changed, for the person running the school

## 2.0.0-preview.1 — 2026-11-17

The whole app rebuilt. Everything the school uses today is here, with these differences you would notice:

- **It opens on Now**: the building this minute, like a departures board. Every space is a row saying
  what is in it and until when (with a bar showing how far through), or that it is free, what comes
  next, and a thin bar of its whole day. Beside them,
  what needs somebody today: cover, registers waiting for you, clashes, today's events. A coach linked
  to their name sees **their own day** at the top.
- **Plan shows a day with time running across** and the spaces down the side, so a free space and a
  double-booked one are obvious at a glance. One click turns it to groups or people, or to the whole
  week. Drag a class sideways to move it in time, onto another space to move it there, or stretch an
  end; double-click empty time to add a class right there.
- **Find instead of filters.** The box at the top (or Ctrl+K, or /) finds any student, facilitator,
  space, group or thing to do. Choosing a person shows their week, a day to a row; choosing a space
  shows its week.
- **Draw a class straight onto the plan**: press on empty time in a space's row and drag across it,
  and the add form opens with that space and those times. Adding a class says which spaces are busy
  then, and offers the free ones to press.
- **A class opens beside the plan**, not over it: when, where, who it is for and how many, who teaches
  and who is away, what is wrong with it, and Edit, Register, Cover, Cancel, Duplicate and Delete.
  Forms slide in from the right, so the plan stays in sight.
- **Registers start with today's classes as big cards**, made to be tapped on a phone in the room.
- **Two looks**: Paper (light, like a printed programme) and Night (dark), following the device unless
  you choose one in your menu.
- **"Space" and "class" everywhere** a person reads, rather than "room" and "session": a Big Top is
  not a room.

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
- **The morning register is here**: People & places ▸ Absences opens with today's students. Tap who is away,
  planned or unplanned, and pick a reason; their registers open with them excused, and a register
  already taken changes them from absent to excused (never from present).
- **Import people from a spreadsheet**: People & places ▸ Students and Facilitators take rows pasted from Excel,
  show what would be added (and who is left out, and why) before anything changes, and one Undo
  takes the whole import back.
- **Reports download as spreadsheets** (Hours, and Attendance) for whatever weeks are chosen, with
  each time rule in its own column and Æ Ø Å intact in Excel.
- **The line at "now"** on today's column moves by itself through the day.
- **Bring in a file…** reads the current app's `.cccc` (every version) and says what came across.

- **The end-of-day register reminder email** is here, off until it is switched on, with a Preview mode
  that sends every reminder to one address first. Administration ▸ System shows its state. The
  settings have the same names as the current app's.

Not carried over: Google sign-in (the school uses its own accounts), the floor plan (off the screen
since 1.31.0), and the old desktop apps.
