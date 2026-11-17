# Build order

Each milestone ends with the build green, the tests green and the changes pushed. Nothing here is
released to the school; Craig decides if and when the rebuild replaces the current app.

| # | Milestone | Done when |
|---|---|---|
| 1 | **Spec** | these files are in the repository |
| 2 | **Domain** | the model and every rule in `03-the-rules.md` exist with tests, including a falsified test per rule |
| 3 | **Store** | the operation log, snapshots, undo per person, history, soft delete, the start-up refusals; tests that kill the process mid-write and find the state whole |
| 4 | **Import** | every schema 1–24 fixture imports; the round-trip test agrees with the old resolver |
| 5 | **Sign-in and accounts** | local accounts, the fallback policy, the limiter, capabilities, the first-start setup link |
| 6 | **The week** | the calendar with lanes, filters, counts, choose, edit, drag, box-select, copy week/day |
| 7 | **Forms and Data** | session, event, cover, register forms; every Data kind |
| 8 | **Reports and My week** | the reports in `07-screens.md` and a facilitator's own week |
| 9 | **Printed documents** | the four PDFs through one gate |
| 10 | **Deployment** | a Dockerfile, the laptop runbook for running it beside the current app on another port, the backup command |
| 11 | **Side by side** | a preview on made-up data that Craig can compare with the current app |

## Ways of working

- **Made-up data only**, from `SampleSchool` (a fictional school with fictional people), in every test,
  screenshot and preview.
- **Local checks rather than CI**: GitHub Actions minutes on the account are nearly spent, so
  `scripts/check.sh` runs the build and every suite, and is run before every push.
- **Falsify every test**: break the behaviour, watch it fail, restore.
- **Zero warnings**, treated as errors.
