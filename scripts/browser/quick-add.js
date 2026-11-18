// A class typed into Ctrl+K in one line. On the plan, "aerial <the week after next's Friday> 13" comes first in
// Find, saying what it would add and that the rest is as on next week's Aerial; Enter opens the add form on
// that day with everything filled in, OK adds it, and Undo takes it away. A day name counts from the week
// the plan shows, and from today anywhere else. A line naming everything takes nothing from before, and a
// clash it would make is warned of in the form, not refused. With no class named, the words are the title,
// and the form says what it still needs. Words not understood are said; a coach is never offered to add it,
// and the same line asks for its space instead (space-ask.js); a phone fits it. Every date is in the sample's
// next two weeks, so the walk runs on any day.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }

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
const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
// "Fri 4 Dec", as the app says a date; "4/12", as a person types one.
const said = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
const typed = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${d.getUTCDate()}/${d.getUTCMonth() + 1}`; };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t);
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };

// Something done, and the toast it says, newer than any before it.
async function toasted(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}

const hits = page => page.$$eval('[data-testid=find-hit]', hs => hs.map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim()));

// Ctrl+K, the line typed, and the hits once the first is the one wanted: "label — detail" whole, or the label
// alone. A line read before its last words can already make the same label (the rest as usual says the
// same times), so where the last words change only the detail, the barrier is the whole of it.
async function find(page, line, first) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
  await page.keyboard.type(line);
  await page.waitForFunction(first => {
    const h = document.querySelector('[data-testid=find-hit]');
    if (!h) return false;
    const label = h.querySelector('.find-label').textContent.trim();
    return (first.includes(' — ') ? label + ' — ' + h.querySelector('small').textContent.trim() : label) === first;
  }, first).catch(async () => { throw new Error(`"${line}": the first hit is not "${first}"; Find shows ${JSON.stringify(await hits(page))}`); });
  return hits(page);
}
async function closeFind(page) {
  await page.keyboard.press('Escape');
  await page.waitForSelector('[data-testid=find]', { state: 'detached' });
}
// What a failed step left open is closed, so the next step starts from the plan as any other would.
async function tidy(page) {
  if (await page.locator('[data-testid=find]').count()) await closeFind(page);
  if (await page.locator('[data-testid=dialog]').count()) {
    await page.click('[data-testid=dialog-x]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  }
}

// The add form as a person reads it.
const form = page => page.evaluate(() => {
  const chosen = id => { const s = document.querySelector(`[data-testid=${id}]`); return s ? s.options[s.selectedIndex]?.text.trim() : null; };
  const value = id => document.querySelector(`[data-testid=${id}]`)?.value ?? null;
  return {
    cls: chosen('session-class'), title: value('session-title'), date: value('session-date'), from: value('session-start'), until: value('session-end'),
    group: chosen('session-group'), room: chosen('session-room'),
    who: [...document.querySelectorAll('[data-testid=session-facilitator] .grow')].map(g => g.childNodes[0].textContent.trim()),
    warnings: document.querySelector('[data-testid=session-warnings] summary')?.textContent.trim() ?? null,
    why: document.querySelector('[data-testid=dialog] .form-error')?.textContent.trim() ?? null,
    ok: !document.querySelector('[data-testid=dialog-ok]')?.disabled,
  };
});

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  // The week after next is blank; next week is the last the sample builds, Aerial on its Friday at 13:00.
  const blankFriday = addDays(monday, 18), nextFriday = addDays(monday, 11), nextMonday = addDays(monday, 7);
  const aerial = `Add Aerial · ${said(blankFriday)} 13:00–14:30`;
  log('     today', today, '· the Friday typed', blankFriday);

  await step('a class and a time typed into Ctrl+K come first, saying what would be added and where the rest came from', async () => {
    const found = await find(admin, `aerial ${typed(blankFriday)} 13`, aerial);
    eq(found[0], `${aerial} — BA 1 · Aerial Hall · Dag Øverlie · the rest as on ${said(nextFriday)}`, 'the line');
    if (!(await admin.textContent('.find-foot')).includes('“aerial fri 13” to add a class')) throw new Error('the foot does not say a class can be typed in');
    await admin.screenshot({ path: OUT + 'quick-add-find.png' });
  });

  await step('Enter opens the add form on its day with everything filled in; OK adds it, chosen on the plan, and Undo takes it away', async () => {
    await admin.keyboard.press('Enter');
    await titled(admin, 'Add a class');
    await admin.waitForSelector(`[data-testid=plan-page][data-day="${blankFriday}"]`);
    const f = await form(admin);
    eq([f.cls, f.date, f.from, f.until, f.group, f.who, f.warnings, f.ok], ['Aerial', blankFriday, '13:00', '14:30', 'BA 1', ['Dag Øverlie'], null, true], 'the form');
    if (!f.room?.startsWith('Aerial Hall')) throw new Error('the space is ' + f.room);
    await admin.screenshot({ path: OUT + 'quick-add-form.png' });
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), `Added Aerial on ${said(blankFriday)}`);
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Aerial'
      && document.querySelector('[data-testid=block].chosen')?.closest('.tl-row')?.querySelector('.tl-name')?.textContent.trim() === 'Aerial Hall');
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await admin.waitForFunction(() => !document.querySelector('[data-testid=timeline] [data-testid=block][data-title="Aerial"]'));
  });

  await step('a day name counts from the week the plan shows, and from today on any other page', async () => {
    await tidy(admin);
    // Aerial has no Monday: the rest is as on its Tuesday at 09:00, the nearest start, two hours with Cleo.
    const onPlan = await find(admin, 'aerial mon 9', `Add Aerial · ${said(addDays(monday, 14))} 09:00–11:00`);
    log('     on the plan: ' + onPlan[0]);
    await closeFind(admin);
    await admin.click('[data-testid=nav-now]');
    await admin.waitForSelector('[data-testid=now-page]');
    const coming = today === monday ? monday : nextMonday;
    const onNow = await find(admin, 'aerial mon 9', `Add Aerial · ${said(coming)} 09:00–11:00`);
    log('     on Now: ' + onNow[0]);
    await closeFind(admin);
  });

  await step('a line naming everything takes nothing from before, and the clashes it would make are warned of, not refused', async () => {
    await tidy(admin);
    await admin.click('[data-testid=nav-plan]');
    await admin.waitForSelector('[data-testid=timeline]');
    const line = `juggling ba2 with cleo and bo ${typed(nextMonday)} 10:45-12 studio b`;
    const label = `Add Juggling · ${said(nextMonday)} 10:45–12:00`;
    const found = await find(admin, line, `${label} — BA 2 · Studio B · Bo Lindqvist, Cleo Marchetti`);
    eq(found[0], `${label} — BA 2 · Studio B · Bo Lindqvist, Cleo Marchetti`, 'the line');
    await admin.keyboard.press('Enter');
    await titled(admin, 'Add a class');
    await admin.waitForSelector('[data-testid=session-warnings]');
    const f = await form(admin);
    eq([f.cls, f.date, f.from, f.until, f.group, f.who, f.ok], ['Juggling', nextMonday, '10:45', '12:00', 'BA 2', ['Cleo Marchetti', 'Bo Lindqvist'], true], 'the form');
    if (!f.room?.startsWith('Studio B')) throw new Error('the space is ' + f.room);
    if (!/^\d+ warnings? — saving is still allowed$/.test(f.warnings)) throw new Error('the warnings say ' + f.warnings);
    // More than three warnings start folded, so their words are read whether or not they are shown.
    const listed = (await admin.locator('[data-testid=session-warnings] li').allTextContents()).map(squash);
    log('     ' + listed.join(' | '));
    if (!listed.some(w => w.includes('Studio B')) || !listed.some(w => w.includes('BA 2'))) throw new Error('the warnings do not name Studio B and BA 2: ' + JSON.stringify(listed));
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('with no class named the words are the title, and the form says what it still needs', async () => {
    await tidy(admin);
    const label = `Add Staff meeting · ${said(blankFriday)} 15:30–16:30`;
    const found = await find(admin, `staff meeting ${typed(blankFriday)} 15:30 canteen`, `${label} — no group yet · Canteen · nobody teaching yet`);
    eq(found[0], `${label} — no group yet · Canteen · nobody teaching yet`, 'the line');
    await admin.keyboard.press('Enter');
    await titled(admin, 'Add a class');
    const f = await form(admin);
    eq([f.cls, f.title, f.from, f.until, f.why, f.ok], ['No class', 'Staff meeting', '15:30', '16:30', 'Choose the group it is for.', false], 'the form');
    if (!f.room?.startsWith('Canteen')) throw new Error('the space is ' + f.room);
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('words it does not understand are said, and a name alone is found rather than added', async () => {
    await tidy(admin);
    const want = `${aerial} — BA 1 · Aerial Hall · Dag Øverlie · the rest as on ${said(nextFriday)} · not understood: blah, stu (Studio A or Studio B?)`;
    eq((await find(admin, `aerial ${typed(blankFriday)} 13 stu blah`, want))[0], want, 'the line');
    await closeFind(admin);
    const name = await find(admin, 'aerial hall', 'Aerial Hall');
    if (name.some(h => h.startsWith('Add '))) throw new Error('a name alone offers to add: ' + JSON.stringify(name));
    await closeFind(admin);
  });

  await step('a coach is never offered to add it: the same line asks for its space instead', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await coach.goto(B + '/plan');
    await coach.waitForSelector('[data-testid=timeline]');
    await coach.keyboard.press('Control+k');
    await coach.waitForSelector('[data-testid=find]');
    await coach.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
    await coach.keyboard.type(`aerial ${typed(blankFriday)} 13`);
    // The ask is drawn only for the line typed, so waiting for it also waits for the answer to that line.
    await coach.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim().startsWith('Ask for '))
      .catch(async () => { throw new Error('the first hit is not an ask; Find shows ' + JSON.stringify(await hits(coach))); });
    if ((await hits(coach)).some(h => h.startsWith('Add '))) throw new Error('a coach is offered to add: ' + JSON.stringify(await hits(coach)));
    const foot = await coach.textContent('.find-foot');
    if (foot.includes('to add a class')) throw new Error('the foot tells a coach to type a class');
    if (!foot.includes('to ask for a space')) throw new Error('the foot does not say a line asks for a space: ' + JSON.stringify(foot));
    await coach.context().close();
  });

  await step('a phone: the line fits the Find box, nothing scrolls sideways, and Enter opens the form', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await phone.goto(B + '/plan');
    await phone.waitForSelector('[data-testid=timeline]');
    await find(phone, `aerial ${typed(blankFriday)} 13`, aerial);
    const fit = await phone.evaluate(() => {
      const h = document.querySelector('[data-testid=find-hit]').getBoundingClientRect(), f = document.querySelector('[data-testid=find]').getBoundingClientRect();
      return { page: document.documentElement.scrollWidth, width: innerWidth, inside: h.left >= f.left - 1 && h.right <= f.right + 1 };
    });
    log('     ' + JSON.stringify(fit));
    if (fit.page > fit.width) throw new Error('the page scrolls sideways: ' + fit.page + ' > ' + fit.width);
    if (!fit.inside) throw new Error('the line spills out of the Find box');
    await phone.screenshot({ path: OUT + 'quick-add-phone.png' });
    await phone.keyboard.press('Enter');
    await titled(phone, 'Add a class');
    eq((await form(phone)).from, '13:00', 'the form on a phone');
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
