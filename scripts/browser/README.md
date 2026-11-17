Browser walk-throughs against the preview (`scripts/preview.sh`, made-up data). Each prints OK or FAIL
per step and saves screenshots and PDFs to `$OUT` (default: the temporary folder's `cccc-browser`).

    node scripts/browser/every-surface.js        # forms, drag, undo, add, every PDF, data, a register, download
    node scripts/browser/keyboard.js             # Tab to a block, Space chooses, Enter opens, focus comes back
    node scripts/browser/two-people-and-phone.js # one person's drag reaches another's screen; phone forms

They change the preview's data; start a fresh preview (delete .local-store-preview) between runs.
