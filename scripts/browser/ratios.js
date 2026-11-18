// Safety ratios, on the sample's next week (always ahead). People & places ▸ Classes gives Juggling at most four
// students to each facilitator, said in the list and kept. Next Wednesday's Juggling, eight students of BA 1 with
// Cleo alone, is then ringed in the clash check's words, with no ways out, since moving it would not help; Bo put on
// it too, the ring goes. Bo recorded away next Wednesday: the absence form says Juggling then needs cover with Cleo
// alone for its eight, the side panel and the Cover page say so too, and giving Ada the cover puts it right. Undo
// takes all of it back, and a phone fits the form. Start the preview with DAY=wed AT=10:15.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
// FAILFAST=1 stops at the first step that fails, for breaking the screens on purpose one way after another.
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); if (process.env.FAILFAST) process.exit(1); } }

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
// "Wed 25 Nov", as the app says a day.
const short = iso => { const d = new Date(iso + 'T12:00:00Z'); return ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][d.getUTCDay()] + ' ' + d.getUTCDate() + ' ' + ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'][d.getUTCMonth()]; };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t)
  .catch(async () => { throw new Error(`no dialog titled "${t}"; it is ${JSON.stringify(await page.textContent('[data-testid=dialog] #dialog-title').catch(() => 'not open'))}`); });
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };
const closed = page => page.waitForSelector('[data-testid=dialog]', { state: 'detached' });

// Something done, and the toast it says, newer than any before it.
async function said(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}
const undo = page => said(page, () => page.click('[data-testid=undo]'), 'Undid: ');

// A data list's row detail, once it reads as wanted.
async function rowSays(page, name, detail, what) {
  await page.waitForFunction(([name, detail]) => {
    const r = [...document.querySelectorAll('[data-testid=data-row]')].find(r => r.querySelector('.name').textContent.trim() === name);
    return r && r.querySelector('small').textContent.replace(/\s+/g, ' ').trim() === detail;
  }, [name, detail], { timeout: 8000 }).catch(async () => {
    const got = await page.$$eval('[data-testid=data-row]', (rs, name) => rs.filter(r => r.querySelector('.name').textContent.trim() === name).map(r => r.querySelector('small').textContent.replace(/\s+/g, ' ').trim()), name);
    throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(detail)}`);
  });
}

// The plan, in spaces, on a day of next week: the day chips of this week, a step on, then the day.
async function nextWeeksDay(page, monday, n) {
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=timeline]');
  await page.click('[data-testid=lens-spaces]');
  await page.click('[data-testid=scale-day]');
  await page.waitForSelector('[data-testid=day-chip]');
  // The plan opens where the tab last looked, so back to this week first.
  const day = await page.getAttribute('[data-testid=timeline]', 'data-day');
  if (mondayOf(day) !== monday) await page.click('[data-testid=today]');
  await page.locator('[data-testid=day-chip]').nth(4).click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 4)}"]`);
  await page.locator('[data-testid=day-next]').first().click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 7)}"]`);
  await page.locator('[data-testid=day-chip]').nth(n).click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 7 + n)}"] .tl-row[data-row]`);
}
const row = (page, space) => page.locator('.tl-row[data-row]', { has: page.locator(`.tl-name:text-is("${space}")`) });
const block = (page, space, title) => row(page, space).locator(`[data-testid=block][data-title="${title}"]`).first();
// Pressing the chosen class again lets it go, so only one not chosen is pressed; the new render marks it.
async function choose(page, b) {
  const title = await b.getAttribute('data-title'), id = await b.getAttribute('data-id');
  if (!await b.evaluate(x => x.classList.contains('chosen'))) await b.click();
  await page.waitForFunction(([t, id]) => document.querySelector('[data-testid=inspector-title]')?.textContent === t
    && document.querySelector(`[data-testid=block][data-id="${id}"]`)?.classList.contains('chosen'), [title, id]);
  return id;
}
const notes = page => page.$$eval('[data-testid=inspector] [data-testid=inspector-clash]', ns => ns.map(n => n.textContent.replace(/\s+/g, ' ').trim()));
// The side panel's warnings, once they read as wanted: a render before may still say the last ones.
async function notesSay(page, want, what) {
  await page.waitForFunction(want => JSON.stringify([...document.querySelectorAll('[data-testid=inspector] [data-testid=inspector-clash]')]
    .map(n => n.textContent.replace(/\s+/g, ' ').trim())) === JSON.stringify(want), want, { timeout: 8000 })
    .catch(async () => { throw new Error(`${what}: ${JSON.stringify(await notes(page))}, not ${JSON.stringify(want)}`); });
}

// The Cover page on the week of that Monday, stepped to with its own arrows from wherever it opens.
async function coverOn(page, monday) {
  await page.goto(B + '/cover');
  await page.waitForSelector('[data-testid=week-head]');
  for (let i = 0; i < 3; i++) {
    const shown = await page.inputValue('[data-testid=week-head] .week-date');
    if (shown === monday) return;
    await page.click(`[data-testid=week-head] button[aria-label="${shown < monday ? 'Next' : 'Previous'} week"]`);
    await page.waitForFunction(was => document.querySelector('[data-testid=week-head] .week-date')?.value !== was, shown);
  }
  throw new Error('the Cover page did not reach the week of ' + monday);
}

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  const d = n => addDays(monday, n);
  log('     today', today, '· next Wednesday', d(9));
  const RING = '"Juggling" has 8 students and 1 facilitator: one to every 4 wants 2 (Wed 10:45–12:00)';
  const LEFT = '1 facilitator for 8 students, one to every 4';
  let juggling = null;

  await step('People & places ▸ Classes gives Juggling at most four students to each facilitator, said in the list and kept', async () => {
    await admin.goto(B + '/data/classes');
    await admin.locator('[data-testid=data-row]', { has: admin.locator('.name:text-is("Juggling")') }).click();
    await titled(admin, 'Edit Juggling');
    eq(await admin.inputValue('[data-testid=class-each]'), '', 'the box before');
    await admin.fill('[data-testid=class-each]', '4');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Changed the class Juggling');
    await closed(admin);
    await rowSays(admin, 'Juggling', 'Counted · Technique · Moderate · one facilitator to every 4 students', 'the list');
    await admin.locator('[data-testid=data-row]', { has: admin.locator('.name:text-is("Juggling")') }).click();
    await titled(admin, 'Edit Juggling');
    eq(await admin.inputValue('[data-testid=class-each]'), '4', 'the box opened again');
    await admin.click('[data-testid=dialog-x]');
    await closed(admin);
  });

  await step('next Wednesday\'s Juggling, eight students with Cleo alone, is ringed in the clash check\'s words, with no ways out', async () => {
    await nextWeeksDay(admin, monday, 2);
    const b = block(admin, 'Studio A', 'Juggling');
    juggling = await choose(admin, b);
    await notesSay(admin, [RING], 'the side panel');
    if (!await b.evaluate(x => x.classList.contains('clash'))) throw new Error('the block is not ringed');
    // Moving it would take its facilitators with it, so none is offered.
    if (await admin.locator('[data-testid=ways-out]').count()) throw new Error('ways out are offered: ' + squash(await admin.textContent('[data-testid=ways-out]')));
    await admin.screenshot({ path: OUT + 'ratios-ring.png' });
  });

  await step('with Bo Lindqvist on it too, the ring goes', async () => {
    await admin.click('[data-testid=inspector-edit]');
    await titled(admin, 'Edit Juggling');
    await admin.selectOption('[data-testid=session-add-facilitator]', { label: 'Bo Lindqvist' });
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=session-facilitator]').length === 2);
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), `Changed Juggling on ${short(d(9))}`);
    await closed(admin);
    await notesSay(admin, [], 'the side panel with Bo on it');
    if (await block(admin, 'Studio A', 'Juggling').evaluate(x => x.classList.contains('clash'))) throw new Error('the block is still ringed');
  });

  await step('Bo away next Wednesday: the absence form, the side panel and Cover each say Juggling needs cover with Cleo alone for its eight, and Ada covering puts it right', async () => {
    // From Now, so "next wed" counts from today rather than from the day the plan shows.
    await admin.goto(B + '/');
    await admin.waitForSelector('[data-testid=now-page]');
    await admin.keyboard.press('Control+k');
    await admin.waitForSelector('[data-testid=find]');
    await admin.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
    await admin.keyboard.type('bo away next wed');
    await admin.waitForFunction(w => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim() === w, `Record Bo Lindqvist away · ${short(d(9))}`)
      .catch(async () => { throw new Error('Find offers ' + JSON.stringify(await admin.locator('[data-testid=find-hit] .find-label').allInnerTexts())); });
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    await admin.waitForSelector('[data-testid=away-class]');
    const takes = await admin.$$eval('[data-testid=away-class]', ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim()));
    log('     it takes him off ' + JSON.stringify(takes));
    const line = takes.find(l => l.startsWith('Juggling '));
    if (!line || !line.endsWith(' · needs cover, leaving ' + LEFT)) throw new Error('the form says ' + JSON.stringify(takes));
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    await closed(admin);
    // The plan: no longer a ring, but a class needing cover, saying why.
    await nextWeeksDay(admin, monday, 2);
    await choose(admin, block(admin, 'Studio A', 'Juggling'));
    await admin.waitForFunction(w => document.querySelector('[data-testid=inspector-cover-why]')?.textContent.trim() === w, `Bo Lindqvist away, leaving ${LEFT}, so it needs cover.`)
      .catch(async () => { throw new Error('the side panel says ' + JSON.stringify(await admin.textContent('[data-testid=inspector-cover-why]').catch(() => 'nothing of cover'))); });
    await notesSay(admin, [], 'the side panel\'s rings');
    if (!await block(admin, 'Studio A', 'Juggling').evaluate(x => x.classList.contains('cover'))) throw new Error('the block is not marked as needing cover');
    // Cover: Juggling among those needing it next week, with why.
    await coverOn(admin, d(7));
    const card = admin.locator('[data-testid=cover-gap]', { has: admin.locator('.cc-title:text-is("Juggling")') });
    await card.waitFor();
    eq(squash(await card.locator('[data-testid=cover-left]').textContent()), ', leaving ' + LEFT, 'the Cover page');
    // Cleo is teaching it already, so covering it herself would add nobody: she is not offered for it, nor for all
    // of Bo's day.
    const names = await card.locator('[data-testid=cover-pick] .cp-name').allInnerTexts();
    if (names.includes('Cleo Marchetti')) throw new Error('Cleo, teaching it already, is offered for it: ' + JSON.stringify(names));
    eq(squash(await card.locator('[data-testid=cover-more]').textContent()), 'All 5…', 'how many could take it');
    const forAll = await admin.locator('[data-testid=cover-stretch] [data-testid=stretch-pick]').allInnerTexts();
    if (forAll.length === 0) throw new Error('nobody is offered for all of Bo\'s day');
    if (forAll.some(p => p.includes('Cleo Marchetti') && p.includes('for all'))) throw new Error('Cleo is offered for all of it: ' + JSON.stringify(forAll));
    await admin.screenshot({ path: OUT + 'ratios-cover.png' });
    // Ada, free then, covers it, and it no longer needs cover.
    const pick = card.locator('[data-testid=cover-pick]', { has: admin.locator('.cp-name:text-is("Ada Brightwater")') });
    await said(admin, () => pick.click(), 'Ada Brightwater covering Juggling');
    await card.waitFor({ state: 'detached' });
  });

  await step('Undo takes back the cover, the absence, Bo and the number', async () => {
    for (let i = 0; i < 4; i++) await undo(admin);
    await admin.goto(B + '/data/classes');
    await rowSays(admin, 'Juggling', 'Counted · Technique · Moderate', 'the list after Undo');
    await nextWeeksDay(admin, monday, 2);
    await choose(admin, block(admin, 'Studio A', 'Juggling'));
    await notesSay(admin, [], 'the side panel after Undo');
    if (await admin.locator('[data-testid=inspector-cover-why]').count()) throw new Error('it still needs cover');
  });

  await step('a phone: the class form fits, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 360, height: 780 });
    await phone.goto(B + '/data/classes');
    await phone.locator('[data-testid=data-row]', { has: phone.locator('.name:text-is("Juggling")') }).click();
    await titled(phone, 'Edit Juggling');
    const f = await phone.evaluate(() => {
      const box = document.querySelector('[data-testid=class-each]').getBoundingClientRect();
      return { page: document.documentElement.scrollWidth, width: innerWidth, left: box.left, right: box.right };
    });
    log('     the phone ' + JSON.stringify(f));
    if (f.page > f.width) throw new Error('the page scrolls sideways, ' + f.page + ' > ' + f.width);
    if (f.left < 0 || f.right > f.width) throw new Error('the box is outside the screen');
    await phone.context().close();
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
