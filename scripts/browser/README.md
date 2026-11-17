Browser walk-throughs against the preview (`scripts/preview.sh`, made-up data). Each prints OK or FAIL
per step and saves screenshots and PDFs to `$OUT` (default: the temporary folder's `cccc-browser`).

    node scripts/browser/every-surface.js        # Now, the plan: open, choose, drag, undo, add, draw, lenses, every PDF, Find; data, a register, cover in one press, download, a blank timetable's Start here
    node scripts/browser/keyboard.js             # Space chooses, Enter opens, Escape closes and focus comes back, Alt+arrows, Ctrl+K and / (and letters typed before Find has opened)
    node scripts/browser/two-people-and-phone.js # one person's drag reaches another's screen; the phone: Now, the plan, a drawer, Data, a report, Find
    node scripts/browser/a-coach.js              # a coach: Now's registers from earlier days match My week, everybody's registers, who is away, admin pages refuse
    node scripts/browser/scratch-week.js         # an editor: copy a class to the Scratch Week, plan in it (drag, add by day), copy a week in, place it onto a week, undo, empty; the phone; a coach never sees it
    node scripts/browser/hours-lines.js          # a facilitator's figure on Reports, on their week in the plan and on My week opens every class and time rule in it, adding up; cover alone; the phone
    node scripts/browser/student-hours.js        # Reports ▸ Student hours against the 15h target: meets, short (and why), a visitor never measured, totals, the overview's count; a student's week on the plan; a coach sees none; the phone
    node scripts/browser/next-year.js            # Administration ▸ Projects ▸ Start next year…: what it keeps and leaves, every semester in the same week number, the new year beside this one opening on the first week, this year untouched; no semesters, no year; the phone
    node scripts/browser/looking.js              # Look at it: next year in this tab only, the band on every page, the plan on next year's first week, a class added there unseen by the editor, printing and spreadsheets of it, a reload, Back, opening it for everybody, deleting it from another tab; the phone
    node scripts/browser/following.js            # with FOLLOW=1 scripts/preview.sh: the current app's timetable, read only here; its Scratch Week; a class cancelled over there on Now in seconds; starting next year from it; erasing somebody taken off over there

Each exits non-zero when a step fails. Set `CCCC_PREVIEW` to point them at another address.

They change the preview's data; start a fresh preview (delete .local-store-preview) between runs.
