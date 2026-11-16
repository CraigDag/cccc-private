# CCCC timetable, rebuilt: the spec

This is the specification for a fresh build of the school's timetable app. It is written from the
current app (`CraigDag/cccc-scheduler`, 1.66.0, schema 23), its `CLAUDE.md`, its five guides and the
project's notes. The current app is a reference and nothing more: **this repository never writes to it,
and nothing here reaches the school's laptop until Craig chooses to switch.**

> The essence of the software is that we need to know **WHO is in what SPACE and WHEN**.

| File | What it settles |
|---|---|
| [`01-what-changes.md`](01-what-changes.md) | The decisions that make this a rebuild rather than a copy, each with its reason |
| [`02-the-model.md`](02-the-model.md) | The things the timetable holds, and the shape of each |
| [`03-the-rules.md`](03-the-rules.md) | The rules won with real use: rosters, cover, hours, time rules, registers, guests, events, lanes |
| [`04-storage-and-editing.md`](04-storage-and-editing.md) | The operation log, live editing, undo per person, history, backups |
| [`05-access-and-privacy.md`](05-access-and-privacy.md) | Sign-in, roles, capabilities, and GDPR |
| [`06-importing-old-files.md`](06-importing-old-files.md) | Reading every `.cccc` up to schema 23 |
| [`07-screens.md`](07-screens.md) | What a person sees, in the order it gets built |
| [`08-build-order.md`](08-build-order.md) | Milestones, and what "done" means for each |

## The rules this spec will not bend

1. **Nothing is lost silently.** A file the app does not understand is refused in words, never read
   with parts dropped. A deletion is something somebody asked for, never the absence of a row.
2. **A rule has one implementation.** Who is on a class, who is in the room, what a week's hours are
   and who may change a register are each answered in exactly one place, and the screen, the paper and
   the reports all ask it.
3. **Fail closed.** Every page needs sign-in by a fallback policy; a capability nobody granted is
   refused; a store that cannot be trusted refuses to start.
4. **Bounds advise, never refuse.** A facilitator over their limit, a clash, a closure scoped at nobody:
   all are warnings. Real timetables break their own rules in exceptional weeks.
5. **Only made-up data in this repository.** No real names, no copy of the live file, no secrets, ever.
   A copy of the live file is personal data and is worked on in a scratch folder only.
