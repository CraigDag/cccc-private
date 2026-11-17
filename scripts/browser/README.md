Browser walk-throughs against the preview (`scripts/preview.sh`, made-up data). Each prints OK or FAIL
per step and saves screenshots and PDFs to `$OUT` (default: the temporary folder's `cccc-browser`).

    node scripts/browser/every-surface.js        # Now, the plan: open, choose, drag, undo, add, draw, lenses, every PDF, Find; data, a register, cover in one press, download, a blank timetable's Start here
    node scripts/browser/keyboard.js             # Space chooses, Enter opens, Escape closes and focus comes back, Alt+arrows, Ctrl+K and /
    node scripts/browser/two-people-and-phone.js # one person's drag reaches another's screen; the phone: Now, the plan, a drawer, Data, a report, Find
    node scripts/browser/a-coach.js              # a coach: Now's registers from earlier days match My week, everybody's registers, who is away, admin pages refuse

Each exits non-zero when a step fails. Set `CCCC_PREVIEW` to point them at another address.

They change the preview's data; start a fresh preview (delete .local-store-preview) between runs.
