# Trying the rebuild beside the current app

**Nothing here touches the current app.** The current app keeps its container (`cccc-scheduler`), its
port (8080), its nginx site, its data (`/srv/cccc`), its nightly update and its backup. The rebuild
gets a container, a port, a folder and a backup of its own, and is reached only through an SSH tunnel
from Craig's own PC: it is not on nginx, not on the tailnet's 443, and not on the school network.
Step 8, which is optional, lets it follow the current app's timetable by reading a copy of its file; it
never writes to anything of the current app's. Step 9, also optional and Craig's decision, lets other
people reach it on the school network, on a port of its own.

| | Current app | Rebuild |
|---|---|---|
| Container | `cccc-scheduler` | `cccc-next` |
| Port on the laptop | 8080 (nginx in front, 443) | **127.0.0.1:8090 only** (nginx on 8443 only after step 9) |
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

A timetable brought in this way is a copy as it was that day, and drifts from the school's real week
as soon as anything changes in the current app. To keep one the same as the current app instead, use
step 8, which brings the timetable in by itself; this step is then not needed.

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
optional. A mode that is misspelt counts as Off and System says so, rather than guessing.

The evening email of what changed in a coach's next day that they have not seen (New for you, by email)
has the same switches under its own name, and is off unless it is in the file too:

```
NewsMail__Mode=Preview
NewsMail__PreviewTo=YOUR OWN ADDRESS
```

with `NewsMail__At=18:00`, `NewsMail__SummaryTo=` (the count of who was emailed, to an administrator)
and `NewsMail__Link=` optional in the same way. Then recreate the container with the file:

```powershell
ssh -t cccc-ts "sudo docker rm -f cccc-next; sudo docker run -d --name cccc-next --restart unless-stopped -p 127.0.0.1:8090:8080 -v /srv/cccc-next:/data --env-file /srv/cccc-next/mail.env cccc-next; sleep 10; curl -s http://127.0.0.1:8090/healthz"
```

No email names a student, and the log records only how many reminders went out.

## 8. Follow the current app's timetable (optional)

With this, the rebuild keeps one timetable the same as the current app's own file: each save there is
here within a minute, as one change by *the current app*, so Now (with its *Changed since yesterday*),
the plan and every other view show the school's real week with nothing typed twice. **That timetable
is changed in the current app, never here**: the rebuild offers nothing to change on it and says so
wherever somebody would try, registers included, because whatever was saved here would be undone by the
next save over there.

**Nothing about the current app changes.** A small script copies its file every 15 seconds into the
rebuild's own folder, reading it only; its systemd unit makes the whole disk read-only to it except
`/srv/cccc-next`. The current app's file is never written, renamed, locked or given new permissions.

First, the file's name. The current app names it after the timetable:

```powershell
ssh -t cccc-ts "sudo ls /srv/cccc/projects"
```

Write that name down for the copier, in a file only root can read:

```powershell
ssh -t cccc-ts "sudo install -m 600 /dev/null /etc/cccc-next-follow.env; sudo nano /etc/cccc-next-follow.env"
```

and in it, one line, with the name from the list in place of `NAME` (keep the quotes; a name with
spaces is fine):

```
FOLLOW_FILE="/srv/cccc/projects/NAME.cccc"
```

Then install and start the copier. `current.cccc` owned by `1654` in the last line means it copied:

```powershell
ssh -t cccc-ts "sudo install -m 755 /opt/cccc-next-src/docs/deployment/laptop/cccc-next-follow.sh /usr/local/bin/; sudo install -m 644 /opt/cccc-next-src/docs/deployment/laptop/cccc-next-follow.service /etc/systemd/system/; sudo systemctl daemon-reload; sudo systemctl enable --now cccc-next-follow.service; sleep 3; sudo ls -l /srv/cccc-next/follow"
```

And recreate the container with following switched on (add `--env-file /srv/cccc-next/mail.env` before
`cccc-next` at the end if step 7 is done):

```powershell
ssh -t cccc-ts "sudo docker rm -f cccc-next; sudo docker run -d --name cccc-next --restart unless-stopped -p 127.0.0.1:8090:8080 -v /srv/cccc-next:/data -e Follow__File=/data/follow/current.cccc cccc-next; sleep 10; curl -s http://127.0.0.1:8090/healthz"
```

The first look brings the timetable in as a new one called *The current app's timetable* and opens it
for everybody. Administration ▸ System's *Following the current app* card says when it last looked and
what came across; if the file cannot be read it turns amber and says why, and the timetable stays as it
was until it can. The copier leaves a one-line note beside its copy after every look, and the rebuild
reads it first: if the copier has stopped, or cannot find the current app's file, the card says that
too, and everybody sees *Behind the current app* instead of a stale timetable called up to date. If the current app's timetable is ever renamed, its file's name changes with it:
change `/etc/cccc-next-follow.env` and run `ssh -t cccc-ts "sudo systemctl restart cccc-next-follow.service"`.

**When the current app's file format moves on** (a release whose changelog says to update every machine
together), the rebuild cannot read the new file: following stops, the card says *saved by a newer
version*, everybody sees *Behind the current app*, and the timetable stays as it was. Nothing is lost
or half-read. The rebuild then needs a newer copy of the current app's loader, which is a change to
`cccc-private` itself; after it, steps 1 and 2 (pull and build) and recreating the container take it.

**The followed timetable holds the school's real data, students included**, like one brought in at
step 5, and so does the copy in `/srv/cccc-next/follow`. Both stay on the laptop. Erasing a person has
one more step while following: see below.

To stop following, stop the copier, delete its copy, and recreate the container as in step 3, without
`-e Follow__File=…`. The timetable stays, as an ordinary one that can be changed here:

```powershell
ssh -t cccc-ts "sudo systemctl disable --now cccc-next-follow.service; sudo rm -f /srv/cccc-next/follow/current.cccc /srv/cccc-next/follow/copier /etc/cccc-next-follow.env"
```

## 9. Let other people reach it (optional, and Craig's decision)

Until now only Craig reaches the rebuild, through the tunnel in step 4. For the administrator and the
facilitators to try it beside the current app, nginx can serve it on a port of its own, **8443**, with
the certificate the current app already uses (a certificate names the laptop, not a port). Who can
reach it is then the same as who reaches the current app: anybody on the school network, and Craig's
devices over Tailscale.

**This puts a second copy of the school's timetable behind a second sign-in page on the school
network**, with students in it if step 8 is on. Everybody needs an account in the rebuild:
its accounts are its own (Administration ▸ Accounts), not the current app's, and a coach's account is
linked to their name there so Now opens on their own day.

**The current app's nginx site is only read**, for where its certificate is; nothing in it changes. The
rebuild gets a site file of its own (`laptop/cccc-next.nginx`), whose names cannot clash with the
current app's, and a small script puts it in place. nginx checks the whole configuration before it
takes it; if it refuses, the script takes the rebuild's site straight back out, so a mistake here
cannot stop the current app at the next restart. `ON:` in the last line means it is serving:

```powershell
ssh -t cccc-ts "sudo bash /opt/cccc-next-src/docs/deployment/laptop/cccc-next-nginx.sh on"
```

Then the firewall, the same way the current app's 443 is open: the school network, and the tailnet.
The default stays *deny*:

```powershell
ssh -t cccc-ts "sudo ufw allow from 192.168.20.0/24 to any port 8443 proto tcp comment 'rebuild on the school network'; sudo ufw allow in on tailscale0 to any port 8443 proto tcp comment 'rebuild over tailscale'; sudo ufw status numbered"
```

For Craig's own devices over Tailscale, the access policy at `https://login.tailscale.com/admin/acls`
needs the port as well: `"dst": ["tag:cccc-laptop:22,443,8443"]`.

Last, recreate the container so the app counts sign-in attempts per machine behind nginx (it believes
nginx's word about which machine asked only from nginx's own address), with step 8's and step 7's
settings if they are on:

```powershell
ssh -t cccc-ts "sudo docker rm -f cccc-next; sudo docker run -d --name cccc-next --restart unless-stopped -p 127.0.0.1:8090:8080 -v /srv/cccc-next:/data -e SignIn__TrustedProxies=172.17.0.1 -e Follow__File=/data/follow/current.cccc cccc-next; sleep 10; curl -s http://127.0.0.1:8090/healthz"
```

(Leave out `-e Follow__File=…` if step 8 is not on; add `--env-file /srv/cccc-next/mail.env` before the
last `cccc-next` if step 7 is.) The rebuild is then at **https://192.168.20.226:8443** on the school
network, and **https://skema.cccc.internal:8443** on Craig's devices, which already trust the
certificate. The current app stays where it is, on 443. With step 7 on, `Reminders__Link=https://192.168.20.226:8443`
in `mail.env` puts that address in the reminder email.

To stop other people reaching it, and keep it for Craig through the tunnel:

```powershell
ssh -t cccc-ts "sudo bash /opt/cccc-next-src/docs/deployment/laptop/cccc-next-nginx.sh off; sudo ufw delete allow from 192.168.20.0/24 to any port 8443 proto tcp; sudo ufw delete allow in on tailscale0 to any port 8443 proto tcp; sudo ufw status numbered"
```

and take `,8443` back out of the Tailscale policy.

## Erasing a person

Administration ▸ Erase a person takes the name out of every timetable and every stored copy inside the
app. Somebody already taken off the timetable in the last 30 days is still named in its stored history,
so they are listed too, under *Taken off in the last 30 days*.

**While following the current app (step 8), erase them in the current app first.** Within a minute they
are off the followed timetable here too, and the copier's copy of the file no longer holds them; then
erase them here, under *Taken off in the last 30 days*, to take their name out of this app's history.

The nightly copies taken before that still hold it. To finish an erasure request, delete them and
let the next night's backup take a clean copy:

```powershell
ssh -t cccc-ts "sudo rm -f /srv/cccc-next/backups/cccc-*.db; sudo /usr/local/bin/cccc-next-backup.sh"
```

If the off-site line in the backup script is switched on, delete that remote's older copies too.

## Taking it away again

If step 9 is on, run its last line first, so nginx and the firewall no longer offer it. Then:

```powershell
ssh -t cccc-ts "sudo systemctl disable --now cccc-next-backup.timer; sudo systemctl disable --now cccc-next-follow.service 2>/dev/null; sudo rm -f /etc/cccc-next-follow.env; sudo docker rm -f cccc-next"
```

That stops it and leaves `/srv/cccc-next` in place. Deleting that folder deletes the rebuild's
timetables for good, so it is a separate, deliberate step: `sudo rm -rf /srv/cccc-next`.

## If it does not start

`ssh -t cccc-ts "sudo docker logs cccc-next 2>&1 | tail -20"` — no `2>&1` before a pipe hides
anything here, because the log is the whole output. A refusal is one line beginning *The app will not
start:* and says what to do.
