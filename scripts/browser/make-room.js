// Make room, on the sample's next week (so any day of this one will do): Find a time asked for Finn and BA 1 for
// two hours in Studio B between 09:00 and 12:00 finds nothing, and offers instead the times when one other class
// is in the way, with that class moved to where it clashes with nothing: Thursday, moving Creation lab to the
// afternoon; Friday, moving Contemporary dance. Choosing Thursday opens the new class with the move ticked and no
// clash to warn of; saving makes both and one Undo takes both back. Taken off, the tick leaves the other class
// where it is and the form warns of the clash; at a time it is not in the way the form says it stays. Another
// time… on a class the timetable has makes both moves in one press. An editor is offered it, and it fits a phone.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
// The first line of what went wrong, with the line of Playwright's call log that names what it was waiting for.
const why = e => {
  const lines = String((e && e.message) || e).replace(/\x1b\[[0-9;]*m/g, '').split('\n');
  const wait = lines.slice(1).find(l => /waiting for/.test(l));
  return lines[0] + (wait ? ' (' + wait.trim().replace(/^- /, '') + ')' : '');
};
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + why(e)); } }

async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport, reducedMotion: 'reduce' })).newPage();
  page.setDefaultTimeout(15000);
  page.on('pageerror', e => { failed++; log('     page error (' + who + '): ' + e.message); });
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}

const squash = s => s.replace(/\s+/g, ' ').trim();
const text = async (page, sel) => squash(await page.locator(sel).first().innerText());
const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const shortDate = iso => { const d = new Date(iso + 'T12:00:00Z'); return d.toLocaleDateString('en-GB', { weekday: 'short', timeZone: 'UTC' }) + ' ' + d.getUTCDate() + ' ' + d.toLocaleDateString('en-GB', { month: 'short', timeZone: 'UTC' }); };
const longDate = iso => { const d = new Date(iso + 'T12:00:00Z'); return d.toLocaleDateString('en-GB', { weekday: 'long', timeZone: 'UTC' }) + ' ' + d.getUTCDate() + ' ' + d.toLocaleDateString('en-GB', { month: 'long', timeZone: 'UTC' }); };
const toasts = async page => (await page.locator('[data-testid=toast]').allInnerTexts()).map(squash);
const title = page => text(page, '[data-testid=dialog] #dialog-title');
const selected = (page, sel) => page.locator(sel).evaluate(s => s.options[s.selectedIndex].text.trim());
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t);

// The room that could be made, as a person reads it: the day, the time and space, and the class that would move.
const made = page => page.$$eval('[data-testid=room-made]', rs => rs.map(r => ({
  when: r.querySelector('[data-testid=room-take]').textContent.trim(),
  day: r.querySelector('[data-testid=room-day]').textContent.trim(),
  where: (r.querySelector('.ft-where span:last-child')?.textContent || '').trim(),
  moving: r.querySelector('[data-testid=room-moving]').textContent.replace(/\s+/g, ' ').trim(),
  notes: [...r.querySelectorAll('[data-testid=room-note]')].map(n => (n.classList.contains('good') ? '✓ ' : '! ') + n.textContent.trim()),
  others: [...r.querySelectorAll('[data-testid=room-other-note]')].map(n => (n.classList.contains('good') ? '✓ ' : '! ') + n.textContent.trim()),
})));
const said = rs => rs.map(r => `${r.day} ${r.when} ${r.where} · ${r.moving}`);

// A day of the week the plan is on, by its chip (Monday first; an empty weekend has none, so Monday to Friday count).
async function toDay(page, iso, monday) {
  const n = Math.round((new Date(iso + 'T12:00:00Z') - new Date(monday + 'T12:00:00Z')) / 864e5);
  await page.locator('[data-testid=day-chip]').nth(n).click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${iso}"]`);
}
// The plan on next week's Monday: this week's Friday, then a step on over the weekend.
async function toNextWeek(page, monday) {
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=timeline]');
  await page.click('[data-testid=scale-day]');
  await page.waitForSelector('[data-testid=day-chip]');
  if (await page.getAttribute('[data-testid=timeline]', 'data-day') !== addDays(monday, 7)) {
    if (mondayOf(await page.getAttribute('[data-testid=timeline]', 'data-day')) !== monday) await page.click('[data-testid=today]');
    await toDay(page, addDays(monday, 4), monday);
    await page.click('[data-testid=day-next]');
    await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 7)}"]`);
  }
}
// What the plan draws on the day it is showing, as "Title HH:mm–HH:mm Space", sorted.
const drawn = page => page.$$eval('[data-testid=timeline] [data-testid=block]', bs => bs.map(b => {
  const hm = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
  return `${b.dataset.title} ${hm(+b.dataset.start)}–${hm(+b.dataset.end)} ${b.closest('[data-testid=row]')?.dataset.space ?? '?'}`;
}).sort());
// The plan drawing exactly this on the day it is showing: the barrier after a change, and the check of it.
async function draws(page, want) {
  const w = [...want].sort();
  await page.waitForFunction(w => {
    const hm = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
    const got = [...document.querySelectorAll('[data-testid=timeline] [data-testid=block]')]
      .map(b => `${b.dataset.title} ${hm(+b.dataset.start)}–${hm(+b.dataset.end)} ${b.closest('[data-testid=row]')?.dataset.space ?? '?'}`).sort();
    return JSON.stringify(got) === JSON.stringify(w);
  }, w, { timeout: 8000 }).catch(async () => { throw new Error('the plan draws ' + JSON.stringify(await drawn(page)) + ', not ' + JSON.stringify(w)); });
}

// Find a time from the plan for a new class: Juggling for Finn and BA 1, two hours in Studio B, from 09:00 until
// the time given, the week the plan is on. With nothing in those hours, the render that says so draws the room.
async function ask(page, until = '12:00') {
  await page.click('[data-testid=find-time]');
  await page.waitForSelector('[data-testid=find-time-start]');
  await page.selectOption('[data-testid=find-time-class]', { label: 'Juggling' });
  await page.selectOption('[data-testid=find-time-add-facilitator]', { label: 'Finn Aalborg-Reyes' });
  await page.waitForSelector('[data-testid=find-time-person]');
  await page.check('[data-testid=find-time-groups] label:has-text("BA 1") input');
  await page.selectOption('[data-testid=find-time-length]', '120');
  await page.selectOption('[data-testid=find-time-space]', { label: 'Studio B' });
  await hours(page, '09:00', until);
}
async function hours(page, from, until) {
  await page.fill('[data-testid=find-time-from]', from);
  await page.press('[data-testid=find-time-from]', 'Tab');
  await page.fill('[data-testid=find-time-until]', until);
  await page.press('[data-testid=find-time-until]', 'Tab');
}
// Only a render with nothing in the hours asked draws this; the hours before them drew times.
const roomOffered = page => page.waitForFunction(() => document.querySelector('[data-testid=openings-none]') && document.querySelectorAll('[data-testid=room-made]').length > 0, null, { timeout: 8000 })
  .catch(async () => { throw new Error('no room offered; the form says ' + await text(page, '[data-testid=openings]').catch(() => '(nothing)')); });
const take = (page, iso) => page.locator('[data-testid=room-made]', { has: page.locator(`[data-testid=room-day]:text-is("${longDate(iso)}")`) }).locator('[data-testid=room-take]');

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = addDays(mondayOf(today), 7), thursday = addDays(monday, 3), friday = addDays(monday, 4);
  log('     today', today, '· next week starts', monday);
  await toNextWeek(admin, mondayOf(today));
  await toDay(admin, thursday, monday);
  const thu = await drawn(admin);
  await toDay(admin, friday, monday);
  const fri = await drawn(admin);
  log('     next Thursday', JSON.stringify(thu));
  log('     next Friday', JSON.stringify(fri));
  const want = [
    `${longDate(thursday)} 09:00–11:00 Studio B · Moving Creation lab (09:00–12:00, Studio B) to 12:45–15:45, same space`,
    `${longDate(friday)} 09:00–11:00 Studio B · Moving Contemporary dance (09:00–11:00, Studio A) to 14:30–16:30, same space`,
  ];
  let offered = [];

  await step('nothing free for Finn and BA 1 in Studio B in the mornings, so Find a time offers to make room, one other class moved each time', async () => {
    await ask(admin);
    await roomOffered(admin);
    const none = await text(admin, '[data-testid=openings-none]');
    if (!none.startsWith('Nothing fits ' + shortDate(monday) + ' to ' + shortDate(addDays(monday, 6)))) throw new Error('the form says ' + none);
    if (await text(admin, '[data-testid=make-room] h3') !== 'Or make room') throw new Error('the section is called ' + await text(admin, '[data-testid=make-room] h3'));
    offered = await made(admin);
    for (const r of offered) log('     ' + said([r])[0] + (r.notes.length ? ' | ' + r.notes.join('; ') : '') + (r.others.length ? ' | moved: ' + r.others.join('; ') : ''));
    if (JSON.stringify(said(offered)) !== JSON.stringify(want)) throw new Error('offered ' + JSON.stringify(said(offered)));
    const label = await admin.locator('[data-testid=room-take]').first().getAttribute('aria-label');
    if (label !== `${longDate(thursday)}, 09:00–11:00, Studio B, moving Creation lab`) throw new Error('the first button is read as ' + label);
    await admin.screenshot({ path: OUT + 'make-room-offered.png' });
  });

  await step('choosing Thursday opens the class there with the move ticked and nothing to warn of; saving makes both, said in one message', async () => {
    await take(admin, thursday).click();
    await titled(admin, 'Add a class');
    const form = [await admin.inputValue('[data-testid=session-date]'), await admin.inputValue('[data-testid=session-start]') + '–' + await admin.inputValue('[data-testid=session-end]'),
      await selected(admin, '[data-testid=session-room]'), await selected(admin, '[data-testid=session-class]'), await selected(admin, '[data-testid=session-group]')];
    log('     the form:', form.join(' · '));
    if (JSON.stringify(form) !== JSON.stringify([thursday, '09:00–11:00', 'Studio B', 'Juggling', 'BA 1'])) throw new Error('the form holds ' + JSON.stringify(form));
    const note = await text(admin, '[data-testid=form-make-room]');
    if (note !== 'Also move Creation lab (09:00–12:00, Studio B) to 12:45–15:45, same space, which makes room for this') throw new Error('the form says ' + note);
    if (!await admin.isChecked('[data-testid=form-make-room-tick]')) throw new Error('the move is not ticked');
    if (await admin.locator('[data-testid=session-warnings]').count()) throw new Error('the form warns: ' + await text(admin, '[data-testid=session-warnings]'));
    await admin.screenshot({ path: OUT + 'make-room-form.png' });
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await draws(admin, [...thu.filter(b => !b.startsWith('Creation lab 09:00')), 'Creation lab 12:45–15:45 Studio B', 'Juggling 09:00–11:00 Studio B']);
    const said = await toasts(admin);
    if (!said.some(t => t.startsWith(`Made room for Juggling, ${shortDate(thursday)} 09:00, by moving Creation lab to 12:45`))) throw new Error('the messages say ' + JSON.stringify(said));
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Juggling');
    await admin.screenshot({ path: OUT + 'make-room-made.png' });
  });

  await step('one Undo takes both back', async () => {
    await admin.click('[data-testid=undo]');
    await draws(admin, thu);
  });

  await step('taken off, the tick leaves Creation lab where it is, the form warns of the clash, and saving adds the class alone', async () => {
    await ask(admin);
    await roomOffered(admin);
    await take(admin, thursday).click();
    await titled(admin, 'Add a class');
    await admin.uncheck('[data-testid=form-make-room-tick]');
    await admin.waitForSelector('[data-testid=session-warnings]');
    const warned = await text(admin, '[data-testid=session-warnings]');
    log('     ' + warned);
    if (!/Creation lab/.test(warned)) throw new Error('the form warns: ' + warned);
    const room = await selected(admin, '[data-testid=session-room]');
    if (room === 'Studio B') throw new Error('Studio B is not shown in use');
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await draws(admin, [...thu, 'Juggling 09:00–11:00 Studio B']);
    const said = await toasts(admin);
    if (!said.some(t => t.startsWith('Added Juggling on ' + shortDate(thursday)))) throw new Error('the messages say ' + JSON.stringify(said));
    await admin.click('[data-testid=undo]');
    await draws(admin, thu);
  });

  await step('changed so the move no longer makes room, the form says Creation lab stays, warns of it where it is, and saves the class alone', async () => {
    await ask(admin);
    await roomOffered(admin);
    await take(admin, thursday).click();
    await titled(admin, 'Add a class');
    // Until one o'clock: Creation lab is still in the way, but at a quarter to one it would run into the class.
    await admin.fill('[data-testid=session-end]', '13:00'); await admin.press('[data-testid=session-end]', 'Tab');
    await admin.waitForSelector('[data-testid=form-make-room-gone]');
    const gone = await text(admin, '[data-testid=form-make-room]');
    if (gone !== 'Creation lab is in the way, but moving it to 12:45–15:45, same space no longer makes room for the class as it is now, so it stays where it is.') throw new Error('the form says ' + gone);
    if (await admin.locator('[data-testid=form-make-room-tick]').count()) throw new Error('the tick is still there');
    const warned = await admin.locator('[data-testid=session-warnings]').count() ? await text(admin, '[data-testid=session-warnings]') : '';
    if (!/Creation lab.*\(Thu 09:00–12:00\)/.test(warned)) throw new Error('Creation lab is not warned of where it is: ' + JSON.stringify(warned));
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await draws(admin, [...thu, 'Juggling 09:00–13:00 Studio B']);
    const said = await toasts(admin);
    if (!said.some(t => t.startsWith('Added Juggling on ' + shortDate(thursday)))) throw new Error('the messages say ' + JSON.stringify(said));
    await admin.click('[data-testid=undo]');
    await draws(admin, thu);
  });

  await step('at a time Creation lab is not in the way, the form says it stays where it is', async () => {
    await ask(admin);
    await roomOffered(admin);
    await take(admin, thursday).click();
    await titled(admin, 'Add a class');
    await admin.fill('[data-testid=session-start]', '13:00'); await admin.press('[data-testid=session-start]', 'Tab');
    await admin.fill('[data-testid=session-end]', '15:00'); await admin.press('[data-testid=session-end]', 'Tab');
    await admin.waitForSelector('[data-testid=form-make-room-not-needed]');
    const note = await text(admin, '[data-testid=form-make-room]');
    if (note !== 'Creation lab is not in the way at this time, so it stays where it is.') throw new Error('the form says ' + note);
    if (await admin.locator('[data-testid=form-make-room-tick]').count()) throw new Error('the tick is still there');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('Another time… on a class the timetable has makes room in one press: both move, the plan follows, and one Undo puts both back', async () => {
    // Juggling on Monday at three, from the times Find a time finds there with the whole day to look in.
    await toDay(admin, monday, monday);
    const mon = await drawn(admin);
    await ask(admin, '18:00');
    await admin.waitForSelector(`[data-testid=opening-day][data-date="${monday}"] [data-testid=opening]`);
    if (await admin.locator('[data-testid=make-room]').count()) throw new Error('room is offered beside the times found');
    await admin.locator(`[data-testid=opening-day][data-date="${monday}"] [data-testid=opening]`, { hasText: '15:00–17:00' }).locator('[data-testid=opening-take]').click();
    await titled(admin, 'Add a class');
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await draws(admin, [...mon, 'Juggling 15:00–17:00 Studio B']);
    // Monday has a Juggling of its own already, for BA 2 before lunch.
    const block = admin.locator('[data-testid=timeline] [data-row] [data-testid=block][data-title="Juggling"][data-start="900"]');
    if (!await block.evaluate(b => b.classList.contains('chosen'))) await block.click();
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Juggling');
    await admin.click('[data-testid=inspector-find-time]');
    await titled(admin, 'Find another time for Juggling');
    await hours(admin, '09:00', '12:00');
    await roomOffered(admin);
    const rs = await made(admin);
    log('     ' + said(rs).join(' / '));
    if (JSON.stringify(said(rs)) !== JSON.stringify(want)) throw new Error('offered ' + JSON.stringify(said(rs)));
    if (!/Undo puts both back/.test(await text(admin, '[data-testid=make-room] p.muted'))) throw new Error('it says ' + await text(admin, '[data-testid=make-room] p.muted'));
    await take(admin, thursday).click();
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForSelector(`[data-testid=timeline][data-day="${thursday}"]`);
    await draws(admin, [...thu.filter(b => !b.startsWith('Creation lab 09:00')), 'Creation lab 12:45–15:45 Studio B', 'Juggling 09:00–11:00 Studio B']);
    const said2 = await toasts(admin);
    if (!said2.some(t => t.startsWith(`Made room for Juggling, ${shortDate(thursday)} 09:00, by moving Creation lab to 12:45`))) throw new Error('the messages say ' + JSON.stringify(said2));
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Juggling');
    // A week not begun yet: nobody knew the old time, so neither says it moved.
    if (await admin.locator('[data-testid=inspector-moved]').count()) throw new Error('it says it moved: ' + await text(admin, '[data-testid=inspector-moved]'));
    await admin.click('[data-testid=undo]');
    await draws(admin, thu);
    await toDay(admin, monday, monday);
    await draws(admin, [...mon, 'Juggling 15:00–17:00 Studio B']);
    await admin.click('[data-testid=undo]');
    await draws(admin, mon);
  });

  await step('an editor is offered the same', async () => {
    const ed = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await toNextWeek(ed, mondayOf(today));
    await ask(ed);
    await roomOffered(ed);
    const rs = await made(ed);
    if (JSON.stringify(said(rs)) !== JSON.stringify(want)) throw new Error('offered ' + JSON.stringify(said(rs)));
    await ed.context().close();
  });

  await step('the phone: the room offered fits the width, and its times can be pressed', async () => {
    const phone = await signIn(browser, 'editor', { width: 390, height: 844 });
    await toNextWeek(phone, mondayOf(today));
    await ask(phone);
    await roomOffered(phone);
    const over = await phone.evaluate(() => {
      const body = document.querySelector('.dialog-body'), box = body.getBoundingClientRect();
      const out = [...body.querySelectorAll('.ft-room, .ft-room .ft-open, .ft-moving, [data-testid=room-take]')].filter(e => e.getBoundingClientRect().right > box.right + 1);
      return { scroll: body.scrollWidth - body.clientWidth, out: out.length };
    });
    if (over.scroll > 1 || over.out > 0) throw new Error('wider than the phone: ' + JSON.stringify(over));
    const t = phone.locator('[data-testid=room-take]').first();
    await t.scrollIntoViewIfNeeded();
    const hit = await t.evaluate(b => { const r = b.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return e === b || b.contains(e); });
    if (!hit) throw new Error('the first time cannot be pressed');
    await phone.screenshot({ path: OUT + 'make-room-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
