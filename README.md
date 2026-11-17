# CCCC Scheduler, rebuilt

A fresh build of the timetable app for Copenhagen Contemporary Circus College: **who is in what space,
and when.** It does everything the current app (`CraigDag/cccc-scheduler`) does for the school, on a
simpler structure, and reads the current app's `.cccc` files so nothing has to be typed in again.

**The current app is untouched by this repository** and remains what the school runs. Whether and when
this one replaces it is Craig's decision.

## What is different, in one breath

- A class is a real occurrence on a real date. No standing pattern, no freezing by copy, no exceptions
  stored on the side: cancelled, cover and a move are facts on the session itself.
- Every change is kept as it is made, in one SQLite file. Two people edit at once with nothing to merge,
  **Undo takes back only your own changes**, and every timetable has a history to put things back from.
- One database file holds everything, so the nightly backup is one consistent copy taken while the app
  runs, and Administration ▸ System says when it last ran.

[`docs/spec/`](docs/spec/README.md) is the full reasoning; [`CHANGELOG.md`](CHANGELOG.md) is what a
person at the school would notice.

## Look at it on made-up data

```bash
scripts/preview.sh          # http://127.0.0.1:5299
```

It opens a fictional school (Northlight) with three made-up accounts: `admin`, `editor` and `coach`,
whose passphrases are `northlight admin`, `northlight editor` and `northlight coach`. Those accounts
exist only in the preview; a real installation starts with no accounts and makes its first
administrator through a one-time setup address printed in the log.

## Try it on the school laptop, beside the current app

[`docs/deployment/trying-it-beside-the-current-app.md`](docs/deployment/trying-it-beside-the-current-app.md):
its own container, port, folder and backup, reached only through an SSH tunnel.

## Build and test

```bash
scripts/check.sh            # build with zero warnings, then all four suites
```

.NET 10. `src/Cccc.Domain` holds the model and every rule (no I/O, no clock: today is passed in),
`src/Cccc.Store` the SQLite store and accounts, `src/Cccc.Legacy` the reading of old `.cccc` files,
`src/Cccc.Web` the Blazor Server app.
