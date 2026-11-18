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
- **A class can be typed into Find in one line** (`QuickAdd`), for editors: *aerial fri 13*, *juggling ba2
  with cleo and bo tomorrow 9.30-11 studio b*, *staff meeting 4/12 15:30 canteen*. It reads names whole
  and longest first (*aerial hall* is the space, *aerial* the class), without their spaces (*ba1*), by a
  first name, or by the start of exactly one (*acro*); a day name counts from the week the plan shows,
  and from today anywhere else; a date the ways people write one (*4/12*, *4.12*, *27 nov*); a time or a
  length (*13-14:30*, *1-2.30pm*, *at 2*, *9 for 90m*), an hour from 1 to 6 being the afternoon. With no
  class named, the words left are the title. Whatever it does not say comes from the class's most like
  session (`Habits.Like`). It comes first in Find, saying what it would add, where the rest came from and
  any word it did not understand (*stu (Studio A or Studio B?)*); Enter opens the add form on it, over
  the plan on its day, and nothing is added until OK there. A coach is never offered it: a coach linked
  to a facilitator is offered the same reading as an ask for its space instead (*Ask for Studio B · Thu
  26 Nov 14:00–15:00*), and Enter opens *Ask for a space* filled in, sending nothing until *Send*.
- **Who is away can be typed into Find the same way** (`QuickAway`): *ada sick*, *bo off fri 9-12*, *dag
  away 1/12 to 4/12*, *alba absent mon-wed*, *cleo out tomorrow afternoon*. A line is about somebody away
  only when it says so (*away*, *off*, *out*, *absent*, *sick*, *ill*, *leave*, *holiday*, *can't come*,
  *not in*) and names one person, a facilitator or a student, or nobody for whoever is signed in. It reads
  a day, a stretch (*mon to wed*, *until fri*, *this week*, *next week*) or from today; a time (*9-12*,
  *morning*, *afternoon*, *from 14*, *until 11*) or whole days; and the cause and notice it says
  (*sick* is an illness, *leave* approved leave, *planned*), the rest of the notice by `Absence.NoticeWhenSaid`.
  It comes first in Find, saying what it would take them off (*Strength and conditioning Mon 09:00:
  needs cover*, *takes them off 4 classes, 3 would need cover*, *misses 7 classes*). **An administrator**
  records it: Enter opens the absence form filled in. **A coach** typing *sick tomorrow* is offered *Ask to
  be away*, for themselves only, and Enter opens *Can't come* on those days; the line says that only the
  days are sent, never why. **Nothing typed becomes the reason**: words not understood are said in Find
  and kept nowhere, as a line about being ill can carry what nobody should store. And whoever reads it, a
  line about somebody away is never a class titled with its words (*Off*, *Sick*).
- **Anybody can ask Find who or what is free** (`QuickFree`), a coach too: *free fri 13-15*, *who's free
  tomorrow morning*, *rooms free now*, *free rigging thu 14*, *is studio b free thu 14*, *bo fri 10*,
  *where is alba*. A line is such a question when it says *free* (*available*, *empty*) or *where*, or
  names somebody or something (a facilitator, a student, a space, a group) with a day, a time or *now*. A
  time with no day is on the day the plan shows, a day with no time is all of it, and neither is now; a
  stretch is two times, *morning*, *afternoon*, *evening*, *from 14* or *until 11*. The answer comes
  first in Find and is `Free`'s. **Naming somebody**, a line each: what they are in then and until when
  (*Guest workshop in Big Top until 11:30*, *covering Acrobatics*, *away all day*), or how long they are
  free and what ends it (*free until 13:00, then Open training in Big Top*); over a stretch, what is in its
  way; over a day, its free stretches. The start of two names answers both. **Naming nobody**, a line
  saying how many are free (*5 spaces and 5 facilitators*) and then each, with how long; *rooms*, *who*
  or *groups* says which, and what a space has (*rigging*) only those with it. The school's own
  facilitators and groups only, a group the day is closed for left out, and somebody outside the hours
  they gave said so and last. A day nobody can know says why (*Prep study day*, a week not filled yet),
  and an event is named only to a reader who may see it, otherwise its space is *booked*. **It only
  looks**: Enter opens the plan on that day, narrowed to what was named, or on spaces, people or groups.
  A class named makes the line a class to add instead, and a line about somebody away is that. Words
  left over after naming something with a time may be a class's title (*staff meeting fri 15:30
  canteen*): to somebody who may add one, adding it comes first and the answer after it
  (`Reading.MayBeAnAdd`).
- **The absence form says what it takes them off before OK**, as *Can't come* does: each class with what
  recording it would leave it, or for a student *They would miss* and the classes (`Away.WhatItLeaves`).
  **For a facilitator it then offers who covers** what it leaves still to come (`CoverAll.ForAbsence`):
  *Who covers the 3 still to come* (or *Who covers Creation lab*), **Not yet** chosen (*They wait on the
  Cover page*), the first three as the Cover page ranks them, each with its initials and the same words
  (*Free for all 3 · has taught Creation lab · 10h this week*; *Free · …* for one class), dashed when free
  for only some, and the rest under **Somebody else**, which opens by itself when the one chosen is under
  it. Choosing somebody says what OK will do (*OK records it with Dag Øverlie covering 2 of the 3;
  Creation lab on Tue 24 Nov still needs cover. One Undo takes both back.*), and OK does exactly that, the
  classes they are free for (`Edits.RecordAway`); *Nobody is free for any of it* when nobody is. Because
  the call that somebody is ill is when cover gets decided: Ctrl+K, *esme sick*, a person, OK. Not yet
  stays the default, so recording an absence never covers anything unasked.
- **A disclosure in a form is the person's to open and close.** `shell.js` closes an open `<details>` on a
  click elsewhere only when it is a menu (`data-menu`: *You* and the phone's *More*), since closing every
  one closed Somebody else under the person's own choice, a class's warnings at the first click into
  another field, and its student list.
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
that class chosen. Under it, **New for you**: what changed in their next two weeks that they have not
seen (`News`), soonest first, each in their own words (*You cover it, for Esme Halloran*, *Not yours
now: Bo Lindqvist teaches it*, *Moved from Thu 26 Nov · was 09:00–11:00*, *Dag Øverlie covering for
you*), with who changed it and when; a week's added or deleted classes are one line (*6 new classes*,
`NewsWords.Lines`), because building or clearing a week ahead is one act, not six pieces of news. **Got
it** beside each, and *Got them all*; a line opens their week on its day with the class chosen. Then **the building as a board**, like
departures at a station: a row a space, grouped by category (headed only when there are two
categories or more, here and on the plan, the add form and the printed day), each with a thin bar of its whole day (its
classes in their colours, a tick at now), what is in it now (class, group, initials, how far through)
or *Free*, until when, and what is next. A board rather than tiles, so ten spaces or forty read without
gaps. Choosing a row opens that space's week. Beside them, **Today
needs**, after **Waiting for your answer** for an administrator (what facilitators have asked: *Can't
come* with the days and how many of their classes it reaches, amber once its first day has come,
*Can cover* with the class, and *Space* with its name, space and time and how many clashes a yes would
make, each opening the answer form): cover still needed, registers waiting for the reader (more than three are one line opening
Registers, so a whole day of them does not push cover and clashes out of sight), clashes today, then, from Thursday
and for whoever plans the timetable, **Next week · Week 48** (`WeekAhead`): blank (*Copy week 47 onto it…*, opening
Copy week with next week ticked), its clashes (how many, the first in the clash check's words, opening next week in
the plan with its issues beside it), the classes needing cover (*Acrobatics Thu 26 Nov 09:00*, opening Cover on next
week), the days it is closed (*Closed Thursday: Ascension Day*) and how many classes are different from the week
before (opening the list of them), or *Ready: 25 classes, nothing clashes, nobody needs cover.*; then **Changed since
yesterday** (since Friday on a Monday): today's classes that were moved, cancelled, given another space
or teacher or cover, added or deleted, each saying what is different (*Was 09:00–11:00*, *Dag Øverlie
covering*), who changed it and when, from `Recent`; then today's events and what is still to come.
**Roll call** in the corner of the clock line, for whoever may call one.

## Roll call (`/roll-call`)

For a fire drill or an evacuation, on a phone at the assembly point. Before it is called, the page lists
who the timetable has where at this minute (`RollCall`), with nothing to tick, and one big button: *Call
the roll at 11:00*, beside who it would list (*3 facilitators and 15 students expected, and 1 class or
event to count by eye*). Called, the moment is fixed and the page is a card a space, each saying what is
in it until when (*Juggling until 12:00*) and how many of its people are found (*1 of 8*), a big button a
name (facilitators first, marked; a guest marked), *All found* for a whole space at once, and *Count by
eye* for what names nobody (*Open training, for the whole building*; *Fire safety talk, an event*). Then
**Between classes**, saying what came last and what comes next, and, behind *Show them*, **Not expected
in a space now** under a heading for each kind (*First class still to come*, *Classes over for today*,
*Not there, says the register*, *Recorded away*, never why), each still tickable.
The count stays in sight at the top as the list scrolls: *10 of 18 found · 8 still to find*, how many
are still to count by eye, *and 1 found who was not expected*, a bar, a box to find a name in, and
**Everybody / Still to find**, which leaves only who is missing. Several people tick at once on their own
phones and each sees the others' ticks as they happen. While it runs, every page carries a band to
everybody who may call the roll (*A roll call is running, called at 11:00 by …: 10 of 18 found*, **Open
the roll call**). **End the roll call** asks once, saying how many expected are not found, then forgets
every tick for everybody. Ctrl+K finds it by *roll*, *fire*, *drill* or *evacuation*, and the phone's
More has it.

## Plan (`/plan`)

- A day strip: the days of the week as chips (an empty weekend is left out), each with its number of
  classes and a dot when something on it needs attention; ‹ › or Alt+← → step a day, skipping an empty
  weekend, or a week when a week is on screen; *Today*; a date box to jump.
- **Lens**: Spaces · Groups · People. **Scale**: Day · Week · Term.
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
- The **term at a glance** (Term, or *Show the whole semester* in Ctrl+K, which also answers *term*):
  every week of the semester a column, the semesters as chips in place of the days, and ‹ › a semester
  at a time. Four rows for every week first: **Classes** (how many happen; *Blank*, or *Closed* and why),
  **Different** (how many classes are new, gone or changed since the week before with anything in it,
  or *Same*), **Issues** and **Events**. Then the lens's rows, each week's figure shaded by its share of
  the term's fullest week, so a row reads across as the shape of its term: hours each space is in use;
  each group's counted hours, orange when short of the target, with how many students are short; each
  person's hours (an administrator's, and a person's own row) or how many classes (anybody else's), over
  their limit in orange. A **Total** column beside the weeks adds each row up for the semester (the weeks
  short or over said under it), and the classes, issues and events too. A week's heading or figure opens that week; Issues opens it with its issues
  listed; a row's figure opens that thing's week narrowed to it. **Different** opens a list, one line a
  class, *New*, *Gone* or *Changed* with what changed (*Was 09:00–10:30 · was in Studio A*), each
  opening that class on its day. The next blank week ahead says **Fill…** (any blank week ahead does on
  a hover), which opens *Copy this week to…* from the week before it with that week already ticked. A
  week that has ended is tinted, this week is marked in the accent, a week closed all week is hatched.
  On a phone it scrolls inside its frame with the names held at the left, opening on the week before
  this one. A class added from the term opens on its day, chosen.
- The side panel for a chosen class: title, when, its verbs straight under the title (Edit, Register,
  Cover, Cancel/Restore, Another time…, Duplicate, Copy to Scratch Week, Delete, so they are on a laptop's
  screen however much is said below), then where, who teaches (and who is away), the group and roster
  count, where it moved from, state tags, the clash or cover note, **Ways out** under a clash, and
  **Elsewhere that day**.
- **Ways out**, for editors, under a chosen class's clash: up to three buttons (*Studio B, same time*;
  *11:00–12:00, same space*; *09:00–10:00 in Studio B*), each with its notes, ✓ for and ! against, as Find
  a time writes them. One press moves the class there, the clash and its ring go, and Undo brings both
  back. Under them, *Or move the other class instead:* and the class it clashes with, pressing to choose
  it and its own ways out. A class that has started says so and offers none; one with no way out the
  same day says *Another time…* looks across its week. A clash in the Issues list opens the class that
  has not started yet, so the ways out are there. A coach reads the clash and is offered none.
- **A chosen class shades its day** where what it needs is elsewhere (`Busy`): a hatch down every row
  at those times, orange where somebody is away, so where it could go is the clear part. On rows that
  are spaces, a space's own classes already show when it is in use, so only its people and groups are
  shaded; on groups' and people's rows its space is too. Narrowed to one, each day's row is shaded for
  it, so a clear stretch on another day is plain. **Elsewhere that day** in the side panel says the same
  in words, a line for each class or event (*13:00–15:00 Circus disciplines: Ada Brightwater, BA 1, Big
  Top in use*), a class pressing to choose it. Dragging the chosen class over a shaded stretch says above
  it what it would run into (*Ada Brightwater busy · BA 1 busy*), somebody in only part of it only for
  their part, and onto a space's row where another class is, *Big Top in use*; it never stops the drag.
  An event the reader may not read is only *booked*; students are counted, never named.
- **Hard training**, on the Groups lens (the day, the week, and one group's days): each group's figure
  beside its name (*4h 45m hard*), in the warning colour when the day goes past the school's limits, and
  on a hover the longer look (*Hard training 09:00–12:00 (2h 45m), 13:00–15:00 (2h); 4h 45m in all. More
  than 2h in a row without a 30m rest.*). Along the foot of the row each hard class is marked solid and a
  moderate one thin, and a stretch past the limit is underlined in the warning colour, its short breaks
  and all. The week grid says each day's under its chips. The side panel says a warning beside the
  classes it is about, with no ring and no ways out. On a phone the figure sits under the group's name.
  People & places ▸ Classes says how hard each class is; ▸ Settings holds the limits and the rest (*2*,
  *1:30*, *90m*; empty is no limit), and Reports counts the week's warnings beside the hours target.
- **Equipment**, on the Spaces lens: choosing a class that needs any marks each space without it, a faint
  wash along its day and the lack under its name (*without Rigging points*), dragging it onto one says so
  over the class (*Studio A without Crash mats and Rigging points*), and the side panel says what the class
  **Needs**. A class in such a space is ringed, its panel says what the space lacks, and its ways out are
  the same time in a space that has it. Ctrl+K finds a space by what it has (*rigging*). The add form offers the free spaces without it last,
  dashed and saying what each lacks, and the space list says it beside each; Find a time says which
  spaces it left out (*Aerial needs Crash mats and Rigging points, so only Aerial Hall and Big Top are
  looked in*). People & places ▸ Equipment lists each piece with where it is and what needs it; Spaces
  and Classes tick theirs.
- **Equipment out of use**, for administrators: a piece's form in People & places ▸ Equipment lists each
  space that has it, *In use* or *Out of use since Wed 18 Nov, back Mon 23 Nov*, with *Out of use…* or
  *Change…*; *broken*, *repair* or *out of use* in Ctrl+K opens the same form for any space. It takes the
  space, the piece, the first day and the day it is back in use (empty until somebody knows), refuses a
  return before it goes, and says before OK which classes need it there while it is out (the first four,
  and how many more). A spell already made offers *Back in use*, or *Delete* when none of it has come. The
  equipment list says it beside the space on the days it is out (*Big Top (out of use)*), and an
  administrator's Now has a line for each piece out of use today, with how many of the rest of this week's
  classes need it there, opening the form. On the plan it is the equipment ring above, saying *out of use*
  and when it is back, and the add form's free spaces and the space list say it beside the space that day.
- **Find a time** beside *+ Class*, for editors (below).
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
Whoever has offered to cover it says *Offered*, and ranks first among the free; pressing them is the
administrator's yes to the offer.

**Whether the person covering has seen it**: for editors and administrators, each covered class says
*Seen* or, in amber, *Not seen yet* (*Seen by Cleo Marchetti; not seen yet by Dag Øverlie* for two),
read as that person's own New for you reads it (`News.Seen`), for whoever signs in with an account
linked to them; nothing when nothing changed it lately. It closes the loop a phone call used to: the
person arranging cover knows who already knows. A coach does not see whether a colleague has.

**Somebody away for several classes still to come gets one card above the rest** (`CoverStretch`,
`CoverAll`): *Away for 3 classes that need cover*, their name, the classes a line each, and **one person for
all of it**, the first three of `CoverAll.Ranked` as the same buttons: *Free for all 3 · has taught
Creation lab · 10h this week*, or dashed and *Free for 2 of 3, busy for 1* (or *away for 1*), *Offered*
first when they offered for one, *would go over their limit* when it would. One press gives them every
class in it they are free for, one change Undo takes back, and the rest keep their own cards. Because an
absence is usually several classes and one substitute for all of them is how a school covers it: one
person who knows the classes, not three people found one class at a time. Editors and administrators see
it; a class that has started is left to its own card, for recording who stood in.

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

**A coach asks on My week, and an administrator answers on Now** (`Ask/`; the rules in
[`03`](03-the-rules.md#asking-asking-the-rebuilds-own)). *Can't come…* takes the days, all day or between
two times, and lists what it would take them off before *Send*: each class with what a yes would leave it,
*needs cover* or *Bo Lindqvist still teaching it*. There is no box for why, and the form says so.
*Ask for a space…* takes a day and times, a space (each marked when it is busy, booked or without what the
class needs, and the free ones offered as chips to press), who it is for, and a class or a name, and says
before *Send* what a yes would run into, in the clash check's own words (*Space "Studio A": "Handstands,
extra" overlaps "Juggling"*) and any booking of the space, or *Nothing: the space is free then, and so are
you*; the form says that the name will be on the
timetable and the screens for everybody, so never a student's name or anything about health. An editor,
who adds classes, is not offered it. *Asked* lists what they asked and offered with where each stands
(*Waiting for an answer*, *Yes: recorded as away*, *Yes: on the timetable Wed 25 Nov, 11:00–12:00 in
Studio B* when it was put somewhere else or has moved since, *Not this time*), each with *Take back* while
nobody has answered; *Could you cover?* lists the classes in the next two weeks needing cover while they
are free, each with *I can cover it*. The administrator's answer form says first what a yes does: for
being away, the classes it takes them off and what each is left as; for an offer, the class, who is away,
and the offerer then (*Free*, or teaching), with their hours that week and anybody else who offered; for a
space, what a yes runs into, with *Put it in* offering the other spaces free then. *Yes, record it*, *Yes,
they cover it* or *Yes, put it on* beside *Not this time* or *No thanks*. Nothing else on the timetable
moves until a yes, and a yes is one change that Undo takes back.

**People & places folds what is former under each list** (`Data/FormerList`): *3 former classes*,
closed unless a search is on, each row saying since when and how many classes it has before then. A
class's, space's or facilitator's form says first what Delete (Remove, for a person) will do, counted
by `Former.Of`, the count the delete itself decides by (`Data/FormerWords`); a former one's form says
what it is kept for and offers *Bring back* in place of Delete. A class's form also has *Merge into…*
(`Data/MergeClassForm`): choose the class to keep, and the form lists what changes beyond the name
before OK says *Merge*. A picker keeps a former one only where it is the value already chosen, marked
*(former)*.

**Public holidays…** beside *Add a closure…* in People & places ▸ Semesters (`Data/HolidaysForm`, and
*easter*, *holidays* or *helligdage* in Ctrl+K for an administrator, never before anything is typed, being
once a year) lists what `PublicHolidays.For` offers from today: each closure's name, its days (*Thu 25 Mar
– Mon 29 Mar*), the holidays in it when there are several, and why one is not ticked (*closed already:
Easter break*, *not a public holiday, but many places close*). OK says how many it adds (*Add 8
closures*) and is one change named for them (*Closed 8 public holidays*), so one Undo takes them all.

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
closures as a tick left off, next year's own public holidays as a tick left on (listed with their days next
year), and the groups arriving as they are. *Make next year* puts the new year in
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

## Screens in the building (`/screen`)

The building on a wall, for people walking past rather than people planning: a TV in a corridor shows
**every space now**, and a tablet by a studio door shows **that space's day**. Nobody signs in on either;
how a screen is paired, and what it may show, are in [`05`](05-access-and-privacy.md#screens-in-the-building).
It is always in the Night look (a lit panel on a wall glares in Paper), and everything is sized from the
screen itself, so one page suits a 1080p TV, a 4K one, a tablet, and a TV hung on its side.

- **The whole building**: a line a space, grouped by kind as on Now, saying what is on now (with who it
  is for, the first names of who is teaching, and a bar for how far through), until when, and what comes
  next (*Nothing more today* once a space's day is done). Beside it: what is **not on today**, what
  **changed since** the weekday before, **coming up** (the next eight things to start anywhere, with
  their space, for a student who knows their class but not where it is), today's events, classes with no
  space, and the spaces **free now** (where a student finds somewhere to train). A few spaces grow to fill
  the board; more than fit are paged every twelve seconds, a heading never last on a page. On its side,
  each space takes two lines and the list sits under the board.
- **One space's day**: its name, **Now** big enough to read from along the corridor (or *Free* and until
  when), its whole day with what has passed dimmed, and the next three days it is used within a week,
  fading at the foot rather than stopping mid-line. A space deleted since says so, and how to choose
  another.
- **It keeps itself current**: it fetches the page again every half minute and swaps the board in place,
  pages, and reloads itself every six hours to pick up a new stylesheet. The clock runs on the school's
  time, never the TV's. **If the timetable cannot be reached it keeps the last board** and says *Cannot
  reach the timetable since 10:42* rather than going blank in front of everybody. Unpaired since, it
  shows a code again.

**Administration ▸ Screens** (`Admin/ScreensTab`) pairs one: the address to open on the screen, the code
it shows, a name, and what it shows (the whole building, or one space's day). The list gives each
screen's name and what it shows, when it was paired and last looked (*is it switched on?* after a day),
**Change…** (a name, or what it shows; the screen follows at its next look) and **Unpair**. A preview
sits at any time of day with `AT=10:15 scripts/preview.sh`, so a screen can be looked at mid-morning.

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
09:00–11:00*), and which an event has (*Big Top — booked: Work-in-progress showing 16:00–17:30*, or
only *booked* for an event the reader may not see), and offers the free ones as chips to press: *Free
then: Gym, Loft*. Hidden guest classes still make a space busy. *Find a time…* takes the class as typed
to Find a time and back. On the plan, pressing on empty time and dragging across it draws the
class there and opens the form with that space (or group, or person, or day) and those times; a
double-click does the same with an hour and a half.

## Find a time

A form like any other, from three doors: *Find a time* on the plan (starting from whoever or whatever
the plan is narrowed to; a student's group, for a student), Ctrl+K, and *Find a time…* in the add form.

- **Asked fresh**, it takes who teaches (chips, and a pick list that starts afresh after each pick), who
  it is for (Everyone, or groups to tick), the class (only so the notes can name it), how long (quarter
  hours to 4h, then 5h and 6h), the space (none needed; any; any of a kind, naming them; one), which
  weeks (this one, two, four or eight, from the week on screen) and the hours (08:00 to 18:00 to start).
  Nothing is listed until somebody, a group or a space is chosen: a list of every free quarter hour
  answers nothing.
- **From the add form**, the class is as typed there (*Find a time for Rigging check*): who teaches,
  who it is for and how long are shown, not asked. Its Close button reads *Back to the class*, and it,
  the × and Escape all go back to the add form with everything as it was.
- **The list**: one sentence of what was asked (*8 times for 1h 30m when Ada Brightwater and BA 1 are
  all free and a Hall is*), then day by day: a heading, a bar of the hours looked in with where the class
  can be marked on it, and each time as a button (*16:00–17:30*), its space and the others free then, how
  late it can start, and its notes, ✓ for, ! against. A day with no time, and the days not looked in, sit
  in their place in the order, saying why. When there were more than 30, *And 4 more times after these*.
  Nothing at all says what to try: a shorter time, any space, more weeks, longer hours.
- **Or make room**: under *Nothing fits*, up to three times when one other class is in the way, each a
  button with its day and space, Find a time's notes as it would be, and the move that makes room
  (*Moving Creation lab (09:00–12:00, Studio B) to 12:45–15:45, same space*) with that class's own notes.
  For a class being added, pressing one opens the add form there with *Also move Creation lab … which
  makes room for this* ticked: its busy spaces, busy people and warnings read the timetable as it would be
  with the move, saving makes both, and taking the tick off saves the class alone, warning of the clash it
  makes. At a time the other class is no longer in the way, the form says it stays where it is. From
  **Another time…**, one press makes both moves. Either way, one Undo puts both back.
- **Pressing a time** opens the add form filled in with it: the date, the times, the space (each
  person's part moved with the class), to look over and save.
- **Another time…** on a class's side panel looks only in that class's week, from now, and pressing a
  time **moves it there** at once (Undo takes it back), each person's part with it, and the plan goes
  to it with it chosen. *Mark it on the plan as moved from Thu 19 Nov, 09:00–11:00* is ticked for this
  week, where somebody may have known the old time, and not for a week ahead; ticked, the block says
  MOVED and the side panel where it was.

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
