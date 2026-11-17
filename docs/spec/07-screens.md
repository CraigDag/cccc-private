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
  last letter opens the top match for all of it. **And nothing typed straight after Ctrl+K or / is
  lost**: Find opens after a round trip and its box takes the focus after another, so `shell.js` keeps
  the keys typed in between (a rubbed-out letter too) and puts them in the box when it is focused. The
  round trip decides, not the typing speed: invisible on the school's network, real over Tailscale.
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
departures at a station: a row a space, grouped by category (headed only when there are two
categories or more, here and on the plan, the add form and the printed day), each with a thin bar of its whole day (its
classes in their colours, a tick at now), what is in it now (class, group, initials, how far through)
or *Free*, until when, and what is next. A board rather than tiles, so ten spaces or forty read without
gaps. Choosing a row opens that space's week. Beside them, **Today
needs**: cover still needed, registers waiting for the reader (more than three are one line opening
Registers, so a whole day of them does not push cover and clashes out of sight), clashes today, then **Changed since
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
  is, and a facilitator's semester for their phone when the plan is narrowed to one) and **Show** (open
  sessions, guest groups) sit in one *More* menu. Guest groups start hidden on
  every visit and a count says how many classes that hides; hiding is drawing, never counting.
- **The Scratch Week**, for editors and administrators: *Scratch Week · 3* beside *+ Class* opens it on
  the same day of the week, and *Back to week 47* returns. It is the plan with its dates taken away: the
  title says *Scratch Week · Monday*, the chips carry counts and no dates, there is no *Today*, date box,
  *+ Event* or Print, and the boxes are hatched in the accent under an **A plan** badge and the line
  *Nothing in it counts until it is placed onto a week*. Adding a class there asks for a day, not a
  date; the side panel has no Register, Cover or Cancel; dragging, stretching and Copy a day work as on
  any week. *More ▾* there holds *Place onto a week…* (every week from this one to the end of the year,
  each saying whether it is blank, how many classes it would replace, or that it has registers and is
  kept) and *Empty Scratch Week…*. A real week's side panel has *Copy to Scratch Week*, and its *More ▾*
  *Copy this week to Scratch Week…*, which says how many classes of the plan it replaces. An empty
  Scratch Week says how to fill it.
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

**A facilitator's semester goes onto their phone as a calendar file** (`Print/PhoneCalendar`):
*Add to phone calendar* on My week for their own, and in the plan's *More* menu an administrator's
*Ada Brightwater’s semester* once the plan is narrowed to that person; `Access.MayHaveTheWeekOf`
decides both, and the download refuses too. It is a file, not a subscription, because nothing outside
the school can reach the laptop: the button's note says to download it again after the timetable
changes. Each class is at that person's own part of it, as the week runs, so cover moves with the
teaching; the events every facilitator may see are in it, never an administrators-only one. **It is
made to be imported over the last one**: a class is named by its id and an event date by its event and
its number in the repeat, and nothing that stops being theirs is left out, since a phone keeps whatever
a new file does not mention. A class cancelled, closed, or on a day they are away is published as
cancelled, saying why. A class deleted, cover given to somebody else or an event taken out is found
through what their earlier downloads carried (`PhoneCalendarFiles`, the store's `calendar_files`) and
published as cancelled at the times it had, as *No longer on your timetable*, since its title is not
kept. One still theirs in another semester is left alone: that semester's file moves it, and a
cancellation here would undo that file if this one were opened after it.

**People & places folds what is former under each list** (`Data/FormerList`): *3 former classes*,
closed unless a search is on, each row saying since when and how many classes it has before then. A
class's, space's or facilitator's form says first what Delete (Remove, for a person) will do, counted
by `Former.Of`, the count the delete itself decides by (`Data/FormerWords`); a former one's form says
what it is kept for and offers *Bring back* in place of Delete. A class's form also has *Merge into…*
(`Data/MergeClassForm`): choose the class to keep, and the form lists what changes beyond the name
before OK says *Merge*. A picker keeps a former one only where it is the value already chosen, marked
*(former)*.

**A figure of a facilitator's hours opens the lines that make it** (`Reports/HoursLines`): a week's
figure or the total on Reports ▸ Facilitator hours, *21h 30m this week* under a facilitator's name when
the plan is narrowed to them, and *How it adds up* on My week. The drawer lists every class and time
rule a day at a time (a week heading each when several weeks were asked for), with the total and their
limit at the top; *Classes*, *Cover* and *Time rules* narrow it and say how much of the total that is.

**Reports ▸ Facilitator hours narrows, sorts and spreads** (`Reports/FacilitatorHours`; every number is
`Hours.OverWeeks`, which the spreadsheet reads too). A name box, *Only those over their limit* and
*Guests* narrow the list; any heading sorts by it, a figure biggest first and a name A to Å with guests
after, and pressing it again turns it round (`aria-sort` says which). *A week* is the average over the
weeks the school ran: a week in which no class happened anywhere (closed, or not timetabled yet) is left
out of it, drawn muted, and is never anybody's quietest, while a week the school ran and they had
nothing is. Breakdown adds the limit, the weeks over it, and the quietest and busiest week, each opening
that week's lines.

**A student's hours are Reports ▸ Student hours** (`Reports/StudentHours`): every student under their
group (and *In no group*), the hours that count week by week, a total, and *A week*, the average over
the weeks something of theirs counted in. A week short of the target is amber, with how many of their
classes did not count beside it (*−1*). Every figure opens `Reports/StudentLines`, every class of theirs
a day at a time, the ones that did not count muted with the reason on them; *Counted* and *Not counted
· N* narrow it, and the line beside the total says how the weeks measure up (*6h 30m short of the 15h
target*, *meets the 15h target in all 3 weeks it measures*). The plan narrowed to a student says
*16h this week, meets the 15h target* and opens the same drawer, and the Reports overview counts the
students short this week (a plain card, not amber: a part-time programme can be short of the school's
one target every week). Administrators only (`Access.MayUseAdminTools`): a student's load is a welfare
and curriculum number.


**Start next year…** is on every timetable in Administration ▸ Projects (`Admin/NextYearForm`). The
form is the list, not the button: each semester's week number this year and next (the same, and a line
saying so when a semester moves 53 weeks to keep it), what carries over, what stays with this year, the
closures as a tick left off, and the groups arriving as they are. *Make next year* puts the new year in
the list beside this one, which stays open for everybody until somebody opens the new one ([`03`](03-the-rules.md)),
and a line above the list offers to look at it.

**Look at it** is on every timetable in Administration ▸ Projects but the one the tab shows. It opens
that timetable **in this tab only** (`Shell/TabTimetable`; an administrator's, `Access.MayLookAtOtherTimetables`)
and goes to the plan, moved to where its classes are when the week on screen has none
(`TabTimetable.Landing`), so next year's opens on next year's first week; what the plan and the week were
narrowed to is let go, being another timetable's. Everybody else, and the administrator's other tabs,
stay where they are. **A band across every page** (`Shell/LookingBand`) names the timetable looked at and
the one everybody else is on, with *Back to …*, which returns to the day the tab was on. The tab's
sessionStorage keeps the look over a reload, with the day it came from; the plan's own day is put back as
the tab starts, before anything moves it. Two timetables of one name (a copy of the open one, brought in)
are told apart: *another timetable called …*, *Back to the open timetable*. Which timetable a tab shows is `ProjectChoice.ShownId`,
asked whenever the tab's workspace is built, and the tab lets go of the look when the timetable is
deleted (with a warning, the deletion going through `ProjectChoice.Delete` so every tab hears of it),
when it becomes the open one (so opening another later never sends the tab back to it), or when the
account may no longer look away. Printing and the Reports spreadsheets are of the timetable the tab
shows. The list marks the one this tab is looking at, offers no Delete on it, and says who else is in a
timetable that is not open (*In it now*). *Bring in a file…* offers *Look at it first* beside *Open it
for everybody*.

## Following the current app

When the open timetable follows the current app ([`04`](04-storage-and-editing.md)), every screen still
reads it and nothing offers to change it: Now's and the plan's heading lines carry a *Follows the
current app* badge (amber, *Behind the current app*, when the file could not be read); People & places,
Registers and Cover say in a line that it is changed in the current app, within a minute (My week only
when it is behind); a form that would change it shows that line and only Close; Add buttons and the
morning list's buttons are not drawn. The foot line says so, System has a card for it, Projects marks
the timetable and offers no Delete on it (but does offer *Start next year…*, which makes a timetable of
its own and changes nothing here), and Erase a person explains the order. A change brought
across reads *the current app* wherever a name goes.

`FOLLOW=1 scripts/preview.sh` tries it on made-up data: `tools/OldFormatSchool` writes a school in the
current app's own format and changes it the way the current app would, and
`scripts/browser/following.js` walks through it. A Scratch Week planned in the current app comes across
too, for editors to look at and not change.

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
