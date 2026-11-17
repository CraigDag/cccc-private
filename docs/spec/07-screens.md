# Screens: the second design

The first version of these screens copied the current app's shape: a dark left rail, a week calendar
with cohort lanes, and a pop-up dialog for everything. Craig, 17 Nov 2026: *"It looks very much like
what we already have... systems and usage can stay similar but design and approach should be
different."* So the engine (`Cccc.Domain`, the store, access, the PDFs) is unchanged, and how a person
meets it starts again from the one sentence the whole app exists for:

> **WHO is in what SPACE and WHEN.**

The current app answers it from the cohort's side, as a week. This one answers it from the
**building's** side, as a day, and lets you turn the same picture to look at it from a group's or a
person's side.

## Four ideas

1. **Open on now, not on a week.** The first screen is the building as it is this minute: every space,
   what is in it and until when, what comes next, and the few things that need somebody today.
2. **Time runs left to right; rows are the thing you are asking about.** One timeline component draws a
   day with spaces as rows (the default), or groups, or people. Turning the lens is one click and keeps
   the day. A week is a grid of the same rows against days, for the overview.
3. **Find, don't filter.** One box at the top (Ctrl+K or /) finds any student, facilitator, group,
   space or class and any action. Choosing a person shows their day; choosing a space shows its day.
   There is no filter panel.
4. **Look in a side panel; edit in a drawer.** Choosing a block opens its details beside the timeline
   (who is in it, the roster, what is wrong with it, what you can do). Forms slide in from the right
   over the page instead of popping up in the middle, so the timeline stays in sight.

## The look

Light first, like a printed programme: warm paper, black ink, hairline rules, one vermilion accent and
a deep blue for links. Headings, dates and times in a serif; everything else in the system sans. A
class's colour is a stripe down the block's left edge on a pale wash of itself, never a solid fill, so
text is always ink on paper. **Night** is the dark version, chosen by the device or the person.
Nothing depends on a web font: the laptop may be offline.

## The shell

- **A top bar**: the CCCC mark, the places (Now, Plan, Registers, My week for a linked facilitator,
  Cover for editors, and for administrators People & places, Reports, Admin), the Find box, Undo and
  Redo, and the person's menu (theme, change passphrase, sign out, the version).
- **On a phone** the places move to a bar along the bottom (Now, Plan, Registers, Find, More).
- **A quiet foot line**: who else is here and the last change. The version is in the person's menu.
- **One drawer at a time**, focus trapped, Escape closes, OK before Cancel.
- **Every page is drawn again as each minute turns**, so a screen left open all day (Now on the office
  wall) keeps up with the clock beside it, and with the date after midnight.
- **Find's keys act on what has been typed**, never on the list last drawn: Enter straight after the
  last letter opens the top match for all of it.
- **Nothing behind sign-in is drawn until its connection is up** (no prerendering): the page says
  *Opening the timetable…* for that moment. Drawn on the server first, a page looked ready before it
  was, and Plan pressed in that moment on a cold server left Now on the screen at `/plan`.
  `scripts/browser/keyboard.js` presses Plan with no wait, on purpose.
- **An empty timetable says where to start**: on Now and on the plan, *Start here* lists what the
  plan is made of in the order it needs it (spaces, programmes and groups, facilitators, classes,
  semesters, then the plan itself), each ticked once it has something and linking to its list, with
  *Bring in a file…* beside them. Somebody who is not an administrator is told an administrator fills
  it in. The board is left out until there are spaces.

## Now (`/`)

A big serif clock line (*Tuesday 17 November, 11:42*). For somebody linked to a facilitator, **You
today** comes first: their own classes today, their part of a shared one, marked *now*, *covering* or
*you are away*, with what changed since yesterday under any class of theirs that did, and the
classes they no longer have today struck through and saying why; choosing one opens their week with
that class chosen. Then **the building as a board**, like
departures at a station: a row a space, grouped by category, each with a thin bar of its whole day (its
classes in their colours, a tick at now), what is in it now (class, group, initials, how far through)
or *Free*, until when, and what is next. A board rather than tiles, so ten spaces or forty read without
gaps. Choosing a row opens that space's week. Beside them, **Today
needs**: cover still needed, registers waiting for the reader, clashes today, then **Changed since
yesterday** (since Friday on a Monday): today's classes that were moved, cancelled, given another space
or teacher or cover, added or deleted, each saying what is different (*Was 09:00–11:00*, *Dag Øverlie
covering*), who changed it and when, from `Recent`; then today's events and what is still to come.

## Plan (`/plan`)

- A day strip: the days of the week as chips (an empty weekend is left out), each with its number of
  classes and a dot when something on it needs attention; ‹ › or Alt+← → step a day, skipping an empty
  weekend, or a week when a week is on screen; *Today*; a date box to jump.
- **Lens**: Spaces · Groups · People. **Scale**: Day · Week.
- The **day timeline**: rows grouped (spaces by category, groups by programme, people as facilitators
  then students when asked for), hours across, a line at now (beneath the classes, so it never crosses
  their names, with the time in a pill on the ruler above it). Click a block to look at it; double-click
  empty time to add a class there (the row fills in the space or group); drag sideways to move in time,
  drag to another space row to move rooms, drag an end to stretch, in quarter hours, one undo step.
- **Narrowed to one** person, space or group (from Find, or a card on Now), the day timeline's rows
  become that one's days: *Cleo Marchetti's week*, Monday to Friday down the side. The chosen day is
  marked; dragging a class onto another day's row moves it to that day. The chip beside the lens shows
  who it is narrowed to, and × goes back to everybody. On this week, a **Now line** under the title
  says where they are this minute and what is next (*Creation lab in Studio B until 16:00, then
  nothing more today*; a space says who the class is for; *Free until 15:00, then Aerial*; *Away
  from …*), from `Where`. Pressing a class there chooses it.
- The **week grid**: the same rows against days, each cell its classes as short chips; click a day
  heading to open that day.
- **Issues** (a count beside the lens): clashes, cover needed and missing registers for what is on
  screen; choosing one opens that class.
- The side panel for a chosen class: title, when, where, who teaches (and who is away), the group and
  roster count, state tags, the clash or cover note, and its verbs: Edit, Register, Cancel/Restore,
  Cover, Duplicate, Delete.
- **Copy** (day, week, clear the weeks after), **Print** (the four documents, narrowed as the screen
  is) and **Show** (open sessions, guest groups) sit in one *More* menu. Guest groups start hidden on
  every visit and a count says how many classes that hides; hiding is drawing, never counting.
- **On a phone** the timeline scrolls inside its box, with the names held at the left, and opens
  scrolled to the class somebody came to look at, else an hour before now, else the first class. The
  side panel becomes a sheet from the bottom.

## Cover (`/cover`)

A card a class that needs cover, and under each, **who could take it**: the first three of
`Cover.Ranked` as buttons (free before busy, lightest week first, guests last), each with the person's
state then and their hours that week. Pressing one gives them the class at once, one change that Undo
takes back, because choosing cover is a decision made fifty times a term and a form for each was the
slow part. *All 6…* opens the full list with a note. The same buttons sit in the plan's side panel for a
class that needs cover, so it is settled where it was noticed. Below: what is covered (*Change…*,
*Take it off*), and **away this week** as a grid of days, a name a row, a whole day filled and part of
a day hatched with its times. Busy people are still offered: busy is advice, never a refusal.

Who is teaching at a time is one rule, `Teaching.At`, beside `Spaces.InUse`: cover ranks by it and the
class form marks *teaching Juggling 10:45–12:00* or *away then* beside a facilitator's name with it, so
the two cannot disagree about who is free.

## Registers, People & places, Reports, Admin, My week

Same jobs as before, restyled: lists are hairline tables, a thing's colour is a spine, buttons are
outlined ink with one filled accent action per screen. Registers lead with **today's** classes as
large tappable cards, because the register is taken standing in a room with a phone; on a phone the
rest of the week is one card a class too, with its state on the right.

**What a person owes in registers is one rule, `Registers.StillToTake`**: missing registers on classes
they were assigned to or covered, from the Monday eight weeks before this week's up to yesterday. Now's
*Registers · N from earlier days* and My week's list both ask it, so they never disagree, and a linked
administrator gets their own there, never the whole school's. An administrator linked to nobody gets
the school's count on Now instead, which opens Registers outstanding on exactly those weeks
(`reports/registers?from=…&to=…`), where every row opens its register. A coach's Registers start on
their own classes with *Only the ones I can take* ticked, and unticking it shows everybody's, because
everybody reads every register. When nobody is in the room because the facilitator is away, the *In
the room* column says *Nobody yet: … away* rather than nothing.

## Words

**Space**, never *room*, wherever a person reads it: the Plan's lens, the forms, People & places, the
printed sheets and the file names. A Big Top is not a room. The code keeps `Room`, which nobody reads.

**Class**, never *session*, for a thing on the timetable: *+ Class*, *Add a class*, *Copy this week's 27
classes*, the printed list's column. The one survivor is the class kind *Open session* (open training
and anything else nobody is required at), which is a name rather than a word for a timetabled thing.
The code keeps `Session`.

## Adding a class

The form says which spaces are in use at the date and times in it (*Aerial Hall — busy: Aerial
09:00–11:00*) and offers the free ones as chips to press: *Free then: Big Top, Gym, Loft*. Hidden guest
classes still make a space busy. On the plan, pressing on empty time and dragging across it draws the
class there and opens the form with that space (or group, or person, or day) and those times; a
double-click does the same with an hour and a half.

## Printed documents

The week as shown, the list, a calendar per student, and **the building's day**: the Plan's Spaces view
on paper, a page a day. Spaces down the side, grouped as on the plan, hours across on the week's own
frame so a week's pages line up, each class where and when it is, overlapping classes stacked, events
in a band along the top. Narrowed to a group or a person, their classes carry words and every other
class in a space is a plain grey bar, with a key saying so: a space busy with somebody else must never
print as free. Classes with no space get the first row of every sheet, because this sheet is how a
building gets scheduled (the current app's rule). A few spaces get tall rows and big words, for a sheet
pinned to a door; many get thinner rows, and when even the thinnest readable row would not fit, the day
goes on over a second sheet rather than leave a space off. What the sheet draws is `SpacesDay`, tested
apart from the drawing.

All four are in the Paper look's colours, under a vermilion rule, with the heading rule without the
project's name, and every generation goes through one gate.
