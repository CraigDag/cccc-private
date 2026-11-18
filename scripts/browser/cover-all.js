// One person for a whole absence, on the Cover page. An administrator records Esme away next Monday to Wednesday
// (typed into Ctrl+K); next week's Cover page shows her three classes together, with the three best placed to take
// all of it: Cleo first, free for all three and having taught Creation lab. One press gives Cleo all three, as one
// change one Undo takes back. With Cleo away that Wednesday too, Dag comes into the three, free for two of them
// (he teaches on Tuesday afternoon), said and dashed, and pressing him gives him those two and leaves Tuesday's
// needing cover. An editor is offered the same; a coach
// who types the page's address sees the classes and nothing to press. A phone fits it. Every date is in the
// sample's next week, so the walk runs on any day of the sample week.
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
// "Mon 23 Nov", as the app says a date; "23/11", as a person types one.
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

// Somebody away typed into Ctrl+K, recorded from the form it opens: the first hit must be the line that records it.
async function recordAway(page, line, label) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
  await page.keyboard.type(line);
  await page.waitForFunction(label => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim() === label, label)
    .catch(async () => { throw new Error(`"${line}": the first hit is not "${label}"; Find shows ${JSON.stringify(await hits(page))}`); });
  const first = (await hits(page))[0];
  await page.keyboard.press('Enter');
  await titled(page, 'Record an absence');
  await page.waitForSelector('[data-testid=away-takes]');
  await toasted(page, () => page.click('[data-testid=dialog-ok]'), 'Recorded an absence');
  await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  return first;
}

// The Cover page on the week of that Monday, stepped to with its own arrows from wherever it opens.
async function coverOn(page, monday) {
  if (!page.url().endsWith('/cover')) {
    await page.goto(B + '/cover');
    await page.waitForSelector('[data-testid=week-head]');
  }
  for (let i = 0; i < 3; i++) {
    const shown = await page.inputValue('[data-testid=week-head] .week-date');
    if (shown === monday) return;
    const next = shown < monday;
    await page.click(`[data-testid=week-head] button[aria-label="${next ? 'Next' : 'Previous'} week"]`);
    await page.waitForFunction(was => document.querySelector('[data-testid=week-head] .week-date')?.value !== was, shown);
  }
  throw new Error('the Cover page did not reach the week of ' + monday);
}

// Every card for somebody away for several, as a person reads it.
const stretches = page => page.$$eval('[data-testid=cover-stretch]', cs => cs.map(c => ({
  when: c.querySelector('.cc-when').textContent.trim(),
  who: c.querySelector('.cs-who').textContent.trim(),
  gaps: [...c.querySelectorAll('[data-testid=stretch-gap]')].map(li => li.textContent.replace(/\s+/g, ' ').trim()),
  picks: [...c.querySelectorAll('[data-testid=stretch-pick]')].map(b =>
    b.querySelector('.cp-name').textContent.trim() + ': ' + b.querySelector('small').textContent.replace(/\s+/g, ' ').trim()),
  dashed: [...c.querySelectorAll('[data-testid=stretch-pick]')].map(b => b.classList.contains('busy')),
})));
const gapCards = page => page.$$eval('[data-testid=cover-gap]', cs => cs.map(c => c.querySelector('.cc-title').textContent.trim() + ' ' + c.querySelector('.cc-when').textContent.trim()));
const coveredRows = page => page.$$eval('[data-testid=covered-row]', rs => rs.map(r => r.textContent.replace(/\s+/g, ' ').trim()));
// The stretch card for Esme with these picks first, once the render that draws them has landed.
const picksAre = (page, first) => page.waitForFunction(first => {
  const c = document.querySelector('[data-testid=cover-stretch] [data-testid=stretch-pick] .cp-name');
  return c?.textContent.trim() === first;
}, first);

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  // Next week is the last the sample builds. Esme teaches Strength and conditioning on Monday and Wednesday
  // mornings and Creation lab on Tuesday afternoon, alone; Cleo shares Thursday's Creation lab with her.
  const nextMonday = addDays(monday, 7), nextTuesday = addDays(monday, 8), nextWednesday = addDays(monday, 9);
  const gaps = [
    `${said(nextMonday)} · 09:00–10:30 · Strength and conditioning`,
    `${said(nextTuesday)} · 13:00–16:00 · Creation lab`,
    `${said(nextWednesday)} · 09:00–10:30 · Strength and conditioning`,
  ];
  const best = [
    'Cleo Marchetti: Free for all 3 · has taught Creation lab · 10h this week',
    'Ada Brightwater: Free for all 3 · 5h 45m this week',
    'Bo Lindqvist: Free for all 3 · 7h 15m this week',
  ];
  log('     today', today, '· Esme away from', nextMonday);

  await step('Esme away next Monday to Wednesday, typed into Ctrl+K and recorded', async () => {
    const line = await recordAway(admin, `esme away ${typed(nextMonday)}-${typed(nextWednesday)}`,
      `Record Esme Halloran away · ${said(nextMonday)} – ${said(nextWednesday)}`);
    log('     ' + line);
  });

  await step('next week\'s Cover page shows her three classes together, with the three best placed to take all of it', async () => {
    await coverOn(admin, nextMonday);
    await admin.waitForSelector('[data-testid=cover-stretch]');
    eq(await stretches(admin), [{ when: 'Away for 3 classes that need cover', who: 'Esme Halloran', gaps, picks: best, dashed: [false, false, false] }], 'the card');
    // Each class still has its own card beneath, for giving them out one at a time.
    eq((await gapCards(admin)).length, 3, 'the cards for each class');
    await admin.screenshot({ path: OUT + 'cover-all.png' });
  });

  await step('one press gives Cleo all three as one change, and one Undo takes all three back', async () => {
    await toasted(admin, () => admin.locator('[data-testid=stretch-pick]', { hasText: 'Cleo Marchetti' }).click(),
      "Cleo Marchetti covering 3 of Esme Halloran's classes");
    await admin.waitForSelector('[data-testid=cover-stretch]', { state: 'detached' });
    eq(await gapCards(admin), [], 'what still needs cover');
    const rows = await coveredRows(admin);
    eq(rows.length, 3, 'the covered rows');
    if (!rows.every(r => r.includes('Cleo Marchetti for Esme Halloran'))) throw new Error('the covered rows say ' + JSON.stringify(rows));
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await admin.waitForSelector('[data-testid=cover-stretch]');
    eq((await stretches(admin))[0].gaps, gaps, 'the card once undone');
    eq(await coveredRows(admin), [], 'the covered rows once undone');
  });

  await step('with Cleo away that Wednesday, Dag comes into the three, free for two of them, and pressing him gives him those two', async () => {
    await recordAway(admin, `cleo off ${typed(nextWednesday)}`, `Record Cleo Marchetti away · ${said(nextWednesday)}`);
    await picksAre(admin, 'Ada Brightwater');
    const card = (await stretches(admin))[0];
    // Free for two of the three, Dag comes before Cleo and Finn: he has taught Strength and conditioning more often.
    eq(card.picks, [best[1], best[2], 'Dag Øverlie: Free for 2 of 3, busy for 1 · has taught Strength and conditioning · 5h 45m this week'], 'the picks');
    eq(card.dashed, [false, false, true], 'which are dashed');
    await admin.screenshot({ path: OUT + 'cover-all-two-of-three.png' });
    await toasted(admin, () => admin.locator('[data-testid=stretch-pick]', { hasText: 'Dag Øverlie' }).click(),
      "Dag Øverlie covering 2 of Esme Halloran's classes");
    await admin.waitForSelector('[data-testid=cover-stretch]', { state: 'detached' });
    const left = await gapCards(admin);
    if (!left.includes(`Creation lab ${said(nextTuesday)} · 13:00–16:00`)) throw new Error('Tuesday\'s is not left needing cover: ' + JSON.stringify(left));
    eq((await coveredRows(admin)).filter(r => r.includes('Dag Øverlie for Esme Halloran')).length, 2, 'the classes Dag covers');
    // Both undone, the cover and her absence, so what follows starts from Esme's alone.
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await admin.waitForSelector('[data-testid=cover-stretch]');
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await picksAre(admin, 'Cleo Marchetti');
    eq((await stretches(admin))[0].picks, best, 'the picks once both are undone');
  });

  await step('an editor is offered the same, and a coach typing the page\'s address sees the classes with nothing to press', async () => {
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await coverOn(editor, nextMonday);
    await editor.waitForSelector('[data-testid=cover-stretch]');
    eq((await stretches(editor))[0].picks, best, 'the editor\'s picks');
    await editor.context().close();
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    if (await coach.locator('[data-testid=nav-cover]').count()) throw new Error('a coach is offered the Cover page');
    await coverOn(coach, nextMonday);
    await coach.waitForFunction(() => document.querySelectorAll('[data-testid=cover-gap]').length === 3);
    eq([await coach.locator('[data-testid=cover-stretch]').count(), await coach.locator('[data-testid=cover-pick], [data-testid=stretch-pick]').count()], [0, 0], 'what a coach may press');
    await coach.context().close();
  });

  await step('the phone: the card fits the screen, each name on its own line, and a press works there too', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await coverOn(phone, nextMonday);
    await phone.waitForSelector('[data-testid=cover-stretch]');
    const fit = await phone.evaluate(() => {
      const card = document.querySelector('[data-testid=cover-stretch]').getBoundingClientRect();
      const picks = [...document.querySelectorAll('[data-testid=stretch-pick]')].map(b => b.getBoundingClientRect());
      return {
        page: document.documentElement.scrollWidth <= innerWidth,
        card: card.left >= 0 && card.right <= innerWidth,
        lines: picks.length === 3 && picks.every((p, i) => i === 0 || p.top >= picks[i - 1].bottom),
        inside: picks.every(p => p.left >= card.left && p.right <= card.right),
      };
    });
    eq(fit, { page: true, card: true, lines: true, inside: true }, 'how it fits');
    await phone.screenshot({ path: OUT + 'cover-all-phone.png' });
    await toasted(phone, () => phone.locator('[data-testid=stretch-pick]', { hasText: 'Ada Brightwater' }).click(),
      "Ada Brightwater covering 3 of Esme Halloran's classes");
    await phone.waitForSelector('[data-testid=cover-stretch]', { state: 'detached' });
    await toasted(phone, () => phone.click('[data-testid=undo]'), 'Undid: ');
    await phone.waitForSelector('[data-testid=cover-stretch]');
    await phone.context().close();
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
