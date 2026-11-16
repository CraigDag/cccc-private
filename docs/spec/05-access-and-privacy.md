# Access and privacy

## Signing in

- **Local accounts only**: a name and passphrase, verified against a PBKDF2-SHA256 verifier
  (600,000 iterations, 16-byte salt). Passphrases are never stored.
- **Every page is behind sign-in by a fallback authorization policy**, so a page added later is
  refused too. Anonymous: the sign-in page, its stylesheet, and `/healthz`, each by name.
- **Limited to 10 attempts a minute per client**; behind nginx the client is read from
  `X-Forwarded-For` only when `TrustedProxies` names the proxy.
- The cookie is `HttpOnly`, `SameSite=Lax`, `Secure` when the request is https, and lasts 7 days at
  most. **Every request re-checks the account**, so a removal, suspension or role change takes effect on
  the next click; a live circuit re-checks every minute.
- **First start**: with no accounts, the app prints a one-time setup link to its log, through which the
  first administrator chooses their passphrase. Nothing takes a passphrase on a command line.
- Changing your own passphrase signs you out everywhere else.
- Every sign-in page is sent `Cache-Control: no-store`.

## Roles and capabilities

Three roles: **Viewer**, **Editor**, **Administrator**, plus *nobody* (the fail-closed default).
Capabilities are **named**, never `role >= Editor`:

| Capability | Who |
|---|---|
| `MayEdit` (change the week, add sessions, events) | Editor, Administrator |
| `MayUseAdminTools` (Data, Reports, the dock, whole-project export) | Administrator |
| `MayManageAccounts` | Administrator |
| `MayManageProjects` (new, import, rename, delete, choose which project is open, history) | Administrator |
| `SeesAdministratorsEvents` | Administrator |
| `MayTakeEveryRegister` | Administrator |
| `MaySeeSystem` (backups, the nightly update) | Administrator |

**An account carries**: its role; **tags** (Student, Staff); a **link** to one facilitator (only an
administrator sets either, so nobody tags themself into an audience); suspended; must-change-passphrase;
last signed in (in `signins`, never on the account row). A guest facilitator cannot be linked.
**Three guards**, under one lock: the acting person must be an administrator now; the last
administrator who can sign in cannot be removed, demoted or suspended; nobody removes, demotes or
suspends themself.

## What a person sees

- **Signing in opens the school's timetable** — the project most recently changed — rather than a
  picker. Which project is open is an administrator's choice.
- **Who may print whose week**: an administrator anybody's; everybody else only their own, through
  their link. The PDF route refuses too, not only the button.
- **Downloads of data about every student are administrators' only.**

## GDPR

The live file has held real students since 2026-10-29, so all of this applies to real people.

- **No project contents and no project name are ever logged**, and **no name reaches a URL**: pages
  address things by id.
- **This repository holds made-up data only.** A copy of the live file is personal data: it is worked on
  in a scratch folder, never committed, never screenshotted, and deleted when the trial is done.
- **Erasing a person** is an administrator's action that removes them from the live project **and
  rewrites every stored operation and snapshot that names them** (their name and email replaced, their
  id kept so counts still add up). This is the one sanctioned rewrite of the log, and it is logged as
  "a person was erased" with no name. The off-site backup keeps old copies for 90 days; the erasure
  page says so and names the step on Drive, as the current runbook does.
- **Absence reasons** are free text an administrator may fill; the form warns not to write health
  details there.
- Security headers on every response: a content security policy (self only), `X-Content-Type-Options`,
  `Referrer-Policy: no-referrer`, `frame-ancestors 'none'`.
