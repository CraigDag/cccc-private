# Trying the rebuild beside the current app

**Nothing here touches the current app.** The current app keeps its container (`cccc-scheduler`), its
port (8080), its nginx site, its data (`/srv/cccc`), its nightly update and its backup. The rebuild
gets a container, a port, a folder and a backup of its own, and is reached only through an SSH tunnel
from Craig's own PC: it is not on nginx, not on the tailnet's 443, and not on the school network.

| | Current app | Rebuild |
|---|---|---|
| Container | `cccc-scheduler` | `cccc-next` |
| Port on the laptop | 8080 (nginx in front) | **127.0.0.1:8090 only** |
| Data | `/srv/cccc` | `/srv/cccc-next` |
| Image | `ghcr.io/craigdag/cccc-web`, updated nightly | `cccc-next`, built by hand, never updated by a timer |

Each step is one line to paste into PowerShell on Craig's PC. `cccc-ts` is the laptop's SSH name.

## 1. Get the code onto the laptop

The repository is private, so GitHub asks for a name and a token. Make a **fine-grained token** at
github.com ▸ Settings ▸ Developer settings, with read-only *Contents* on `cccc-private` and nothing
else, and paste it when `git` asks for a password.

```powershell
ssh -t cccc-ts "sudo git clone https://github.com/CraigDag/cccc-private.git /opt/cccc-next-src"
```

Later, to take a newer version: `ssh -t cccc-ts "sudo git -C /opt/cccc-next-src pull"`.

## 2. Build the image (a few minutes; nothing restarts)

```powershell
ssh -t cccc-ts "sudo docker build -t cccc-next /opt/cccc-next-src"
```

## 3. Make its folder and start it for the first time

The first start is the only one that says `Store__Create=true`. Every later start leaves it off, so if
the folder ever goes missing the app refuses to start instead of opening an empty timetable.

```powershell
ssh -t cccc-ts "sudo install -d -o 1654 -g 1654 -m 700 /srv/cccc-next; sudo docker run -d --name cccc-next --restart unless-stopped -p 127.0.0.1:8090:8080 -v /srv/cccc-next:/data -e Store__Create=true cccc-next; sleep 10; curl -s http://127.0.0.1:8090/healthz; echo; sudo docker logs cccc-next 2>&1 | grep setup"
```

The last line prints a `/setup?token=…` address: that is how the first administrator is made. Keep the
window open for step 4.

Then recreate the container without the create switch, so it is never used again:

```powershell
ssh -t cccc-ts "sudo docker rm -f cccc-next; sudo docker run -d --name cccc-next --restart unless-stopped -p 127.0.0.1:8090:8080 -v /srv/cccc-next:/data cccc-next; sleep 10; curl -s http://127.0.0.1:8090/healthz"
```

`ok` means it is up. (The setup address still works after this: it is kept in the store until the first
administrator exists.)

## 4. Open it from Craig's PC

Leave this running in its own PowerShell window; it carries the app from the laptop to this PC only:

```powershell
ssh -N -L 8090:127.0.0.1:8090 cccc-ts
```

Then open **http://localhost:8090/setup?token=…** (the token from step 3) in the browser, make the
administrator account, and sign in. From then on, **http://localhost:8090**.

## 5. Bring in the school's timetable

Administration ▸ Projects ▸ *Bring in a file…* takes a `.cccc` from the current app (Download it there
first). It is read in memory, checked week by week, and stored as a new timetable; the current app's
file is not touched. The import report lists counts, never names.

**This is the school's real data, with students in it.** It lives only in `/srv/cccc-next` on the
laptop and in that folder's backup below; it never goes into the repository or a chat.

## 6. Back it up every night

`laptop/cccc-next-backup.sh` copies the whole store with the app running, keeps fourteen nights, and
tells Administration ▸ System when it last ran (the card turns amber after a missed night). It writes
inside `/srv/cccc-next/backups`; sending that folder off-site is one `rclone` line, left commented in
the script for Craig to fill in with the same encrypted remote the current backup uses.

```powershell
ssh -t cccc-ts "sudo install -m 755 /opt/cccc-next-src/docs/deployment/laptop/cccc-next-backup.sh /usr/local/bin/; sudo install -m 644 /opt/cccc-next-src/docs/deployment/laptop/cccc-next-backup.service /opt/cccc-next-src/docs/deployment/laptop/cccc-next-backup.timer /etc/systemd/system/; sudo systemctl daemon-reload; sudo systemctl enable --now cccc-next-backup.timer; systemctl list-timers 'cccc-*'"
```

## 7. The end-of-day register reminder (optional, off unless switched on)

At 18:00 Copenhagen the app can email each facilitator whose class that day is still without a
register. **While the current app is still the school's, leave this Off or on Preview**: if both apps
were set to On, every facilitator would get two emails a night. Preview sends every reminder to one
address instead, with who it was meant for written at the top, so a week of real evenings can be read
first. Administration ▸ System says which mode it is in and when it last ran.

The mail settings carry a password, so they live in a file only root can read, never in this
repository. The names are the same as the current app's:

```powershell
ssh -t cccc-ts "sudo install -m 600 /dev/null /srv/cccc-next/mail.env; sudo nano /srv/cccc-next/mail.env"
```

and in that file, one per line (fill in the parts in capitals):

```
Smtp__Host=smtp.zoho.eu
Smtp__Port=587
Smtp__Security=StartTls
Smtp__User=info@cccc.dk
Smtp__Password=THE APP PASSWORD
Smtp__From=info@cccc.dk
Smtp__FromName=CCCC timetable
Reminders__Mode=Preview
Reminders__PreviewTo=YOUR OWN ADDRESS
```

`Reminders__Link=` (the app's address, written into the email) is left out while the rebuild is only
reachable through the tunnel; the email then says "under Registers". `Reminders__At=18:00` and `Reminders__SummaryTo=` (one list of counts to an administrator) are
optional. A mode that is misspelt counts as Off and System says so, rather than guessing. Then
recreate the container with the file:

```powershell
ssh -t cccc-ts "sudo docker rm -f cccc-next; sudo docker run -d --name cccc-next --restart unless-stopped -p 127.0.0.1:8090:8080 -v /srv/cccc-next:/data --env-file /srv/cccc-next/mail.env cccc-next; sleep 10; curl -s http://127.0.0.1:8090/healthz"
```

No email names a student, and the log records only how many reminders went out.

## Erasing a person

Administration ▸ Erase a person takes the name out of every timetable and every stored copy inside the
app. The nightly copies taken before that still hold it. To finish an erasure request, delete them and
let the next night's backup take a clean copy:

```powershell
ssh -t cccc-ts "sudo rm -f /srv/cccc-next/backups/cccc-*.db; sudo /usr/local/bin/cccc-next-backup.sh"
```

If the off-site line in the backup script is switched on, delete that remote's older copies too.

## Taking it away again

```powershell
ssh -t cccc-ts "sudo systemctl disable --now cccc-next-backup.timer; sudo docker rm -f cccc-next"
```

That stops it and leaves `/srv/cccc-next` in place. Deleting that folder deletes the rebuild's
timetables for good, so it is a separate, deliberate step: `sudo rm -rf /srv/cccc-next`.

## If it does not start

`ssh -t cccc-ts "sudo docker logs cccc-next 2>&1 | tail -20"` — no `2>&1` before a pipe hides
anything here, because the log is the whole output. A refusal is one line beginning *The app will not
start:* and says what to do.
