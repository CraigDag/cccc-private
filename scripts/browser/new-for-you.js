// New for you, on the sample's next week (always ahead, whatever day it is). The coach (Ada) starts with nothing
// new. An administrator records Esme away next Monday to Wednesday with Ada covering all three, takes Ada off
// next Monday's Acrobatics (Bo still teaches it) and moves next Thursday's Acrobatics into Studio A. Ada's Now
// tells her each, soonest first, in her own words, without a reload; the administrator's Cover page says Ada has
// not seen her three yet. Got it on one takes it off her list and the Cover page says Seen for it; changed again
// after that, it is news again, told from as she saw it. A line opens the plan on its day with the class chosen.
// On a phone the list fits. Got them all clears it, and every one is Seen. An editor sees whether it is seen; the
// coach's own Cover page says nothing about who has seen what.
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

const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const said = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
const typed = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${d.getUTCDate()}/${d.getUTCMonth() + 1}`; };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t);
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };
const squash = s => s.replace(/\s+/g, ' ').trim();

// Something done, and the toast it says, newer than any before it.
async function toasted(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}

// New for you as the coach reads it: "what | the class | when" a line, "[off]" when it is no longer in her days.
const news = page => page.$$eval('[data-testid=news-line]', ls => ls.map(l => {
  const b = l.querySelector('[data-testid=news-open]');
  return `${b.querySelector('[data-testid=news-what]').textContent.trim()} | ${b.querySelector('b').textContent.trim()} | ${b.querySelector('small').textContent.trim()}${b.classList.contains('off') ? ' [off]' : ''}`;
}));
// The barrier: the list as wanted, said as it is when it never comes.
async function newsIs(page, want, what) {
  await page.waitForFunction(want => JSON.stringify([...document.querySelectorAll('[data-testid=news-line]')].map(l => l.querySelector('[data-testid=news-what]').textContent.trim())) === JSON.stringify(want), want)
    .catch(async () => { throw new Error(`${what}: ${JSON.stringify(await news(page))}`); });
}

// The Cover page's covered rows as "Title day: seen words", on the week of that Monday, reloaded.
async function coverSeen(page, monday) {
  await page.goto(B + '/cover');
  await page.waitForSelector('[data-testid=week-head]');
  for (let i = 0; i < 3 && await page.inputValue('[data-testid=week-head] .week-date') !== monday; i++) {
    const shown = await page.inputValue('[data-testid=week-head] .week-date');
    await page.click(`[data-testid=week-head] button[aria-label="${shown < monday ? 'Next' : 'Previous'} week"]`);
    await page.waitForFunction(was => document.querySelector('[data-testid=week-head] .week-date')?.value !== was, shown);
  }
  await page.waitForSelector('[data-testid=covered-row]');
  return page.$$eval('[data-testid=covered-row]', rs => rs.map(r => r.querySelector('.name').firstChild.textContent.trim() + ' '
    + r.querySelector('small').textContent.trim() + ': ' + (r.querySelector('[data-testid=cover-seen]')?.textContent.trim() ?? '-')));
}

// The plan, in spaces, on one day of next week: Friday of this week, a step on over the weekend, then the day.
async function nextWeeksDay(page, monday, n) {
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=timeline]');
  await page.click('[data-testid=lens-spaces]');
  await page.click('[data-testid=scale-day]');
  await page.waitForSelector('[data-testid=day-chip]');
  const day = await page.getAttribute('[data-testid=timeline]', 'data-day');
  if (mondayOf(day) !== monday) await page.click('[data-testid=today]');
  await page.locator('[data-testid=day-chip]').nth(4).click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 4)}"]`);
  await page.locator('[data-testid=day-next]').first().click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 7)}"]`);
  await page.locator('[data-testid=day-chip]').nth(n).click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${addDays(monday, 7 + n)}"] .tl-row[data-row]`);
}
const block = (page, space, title) => page.locator('.tl-row[data-row]', { has: page.locator(`.tl-name:text-is("${space}")`) })
  .locator(`[data-testid=block][data-title="${title}"]`).first();

// A class on a day of next week opened in its form, changed by what `change` does, and saved.
async function changeClass(page, monday, n, space, title, change) {
  await nextWeeksDay(page, monday, n);
  await block(page, space, title).dblclick();
  await titled(page, 'Edit ' + title);
  await change();
  await toasted(page, () => page.click('[data-testid=dialog-ok]'), 'Changed ' + title);
  await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
}
async function spaceTo(page, name) {
  const value = await page.$eval('[data-testid=session-room]', (s, name) => [...s.options].find(o => o.textContent.startsWith(name))?.value, name);
  if (!value) throw new Error('the form offers no ' + name);
  await page.selectOption('[data-testid=session-room]', value);
}

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  const nextMonday = addDays(monday, 7), nextTuesday = addDays(monday, 8), nextWednesday = addDays(monday, 9), nextThursday = addDays(monday, 10);
  log('     today', today, '· next week from', nextMonday);
  const covering = 'You cover it, for Esme Halloran';
  const all = [covering, 'Not yours now: Bo Lindqvist teaches it', covering, covering, 'Was in Big Top'];

  await step('the coach starts with nothing new', async () => {
    await coach.waitForSelector('[data-testid=you-today]');
    eq(await coach.locator('[data-testid=news], [data-testid=news-line]').count(), 0, 'New for you before anything changed');
  });

  await step('an administrator gives Ada three classes to cover, takes her off one and moves another, and her Now says each in her words, soonest first, without a reload', async () => {
    await admin.keyboard.press('Control+k');
    await admin.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
    await admin.keyboard.type(`esme away ${typed(nextMonday)}-${typed(nextWednesday)}`);
    await admin.waitForFunction(l => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim() === l, `Record Esme Halloran away · ${said(nextMonday)} – ${said(nextWednesday)}`);
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    await admin.waitForSelector('[data-testid=away-cover]');
    await admin.locator('[data-testid=dialog] .away-cover label', { hasText: 'Ada Brightwater' }).locator('input').check();
    await admin.waitForFunction(() => document.querySelector('[data-testid=away-cover-says]')?.textContent.startsWith('OK records it with Ada Brightwater covering all 3.'));
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), "Recorded an absence, with Ada Brightwater covering 3 of Esme Halloran's classes");
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await changeClass(admin, monday, 0, 'Big Top', 'Acrobatics', () => admin.click('[data-testid=dialog] button[aria-label="Remove Ada Brightwater"]'));
    await changeClass(admin, monday, 3, 'Big Top', 'Acrobatics', () => spaceTo(admin, 'Studio A'));

    await newsIs(coach, all, 'New for you');
    const lines = await news(coach);
    eq(lines.map(l => l.split(' | ').slice(1).join(' | ')), [
      `Strength and conditioning | ${said(nextMonday)}, 09:00–10:30 · Gym`,
      `Acrobatics | ${said(nextMonday)}, 10:45–12:00 · Big Top [off]`,
      `Creation lab | ${said(nextTuesday)}, 13:00–16:00 · Studio B`,
      `Strength and conditioning | ${said(nextWednesday)}, 09:00–10:30 · Gym`,
      `Acrobatics | ${said(nextThursday)}, 09:00–11:00 · Studio A`,
    ], 'the classes');
    const by = await coach.$$eval('[data-testid=news-line] .change-by', bs => bs.map(b => b.textContent.trim()));
    if (!by.every(b => / today \d\d:\d\d$/.test(b))) throw new Error('who changed it and when: ' + JSON.stringify(by));
    eq(await coach.locator('[data-testid=news-got-all]').count(), 1, 'Got them all');
    await coach.screenshot({ path: OUT + 'new-for-you.png' });
  });

  await step('the administrator\'s Cover page says Ada has not seen her three yet', async () => {
    eq(await coverSeen(admin, nextMonday), [
      `Strength and conditioning ${said(nextMonday)}, 09:00–10:30: Not seen yet`,
      `Creation lab ${said(nextTuesday)}, 13:00–16:00: Not seen yet`,
      `Strength and conditioning ${said(nextWednesday)}, 09:00–10:30: Not seen yet`,
    ], 'the covered rows');
  });

  await step('Got it takes one off her list, and the Cover page says Seen for that one alone', async () => {
    await coach.locator('[data-testid=news-line]').first().locator('[data-testid=news-got]').click();
    await newsIs(coach, all.slice(1), 'New for you after Got it on Monday\'s');
    eq((await coverSeen(admin, nextMonday)).map(r => r.split(': ')[1]), ['Seen', 'Not seen yet', 'Not seen yet'], 'what the Cover page says');
  });

  await step('changed again, it is news again, told from as she saw it, and the Cover page says she has not seen that', async () => {
    await changeClass(admin, monday, 0, 'Gym', 'Strength and conditioning', () => spaceTo(admin, 'Studio A'));
    await newsIs(coach, ['Was in Gym', ...all.slice(1)], 'New for you once Monday\'s moved space');
    eq((await coverSeen(admin, nextMonday)).map(r => r.split(': ')[1]), ['Not seen yet', 'Not seen yet', 'Not seen yet'], 'what the Cover page says');
  });

  await step('a line opens the plan on its day with the class chosen', async () => {
    await coach.locator('[data-testid=news-line]').last().locator('[data-testid=news-open]').click();
    await coach.waitForSelector(`[data-testid=plan-page][data-day="${nextThursday}"]`);
    await coach.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Acrobatics')
      .catch(async () => { throw new Error('chosen: ' + await coach.locator('[data-testid=inspector-title]').allTextContents()); });
    await coach.goto(B + '/');
    await newsIs(coach, ['Was in Gym', ...all.slice(1)], 'New for you, back on Now');
  });

  await step('the phone: every line fits, with its Got it on the screen', async () => {
    const phone = await signIn(browser, 'coach', { width: 390, height: 844 });
    await newsIs(phone, ['Was in Gym', ...all.slice(1)], 'New for you on the phone');
    const fit = await phone.evaluate(() => {
      const ls = [...document.querySelectorAll('[data-testid=news-line]')];
      const inside = r => r.width > 0 && r.left >= 0 && r.right <= innerWidth;
      return { page: document.documentElement.scrollWidth <= innerWidth, lines: ls.length,
        inside: ls.every(l => inside(l.getBoundingClientRect()) && inside(l.querySelector('[data-testid=news-got]').getBoundingClientRect())) };
    });
    eq(fit, { page: true, lines: 5, inside: true }, 'how it fits');
    await phone.locator('[data-testid=news]').scrollIntoViewIfNeeded();
    await phone.screenshot({ path: OUT + 'new-for-you-phone.png' });
    await phone.context().close();
  });

  await step('Got them all clears it, and every one Ada covers is Seen', async () => {
    await coach.click('[data-testid=news-got-all]');
    await coach.waitForSelector('[data-testid=news]', { state: 'detached' });
    eq((await coverSeen(admin, nextMonday)).map(r => r.split(': ')[1]), ['Seen', 'Seen', 'Seen'], 'what the Cover page says');
    await coach.reload();
    await coach.waitForSelector('[data-testid=you-today]');
    eq(await coach.locator('[data-testid=news-line]').count(), 0, 'New for you after a reload');
  });

  await step('an editor sees whether it is seen; the coach\'s own Cover page says nothing about who has seen what', async () => {
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    eq((await coverSeen(editor, nextMonday)).map(r => r.split(': ')[1]), ['Seen', 'Seen', 'Seen'], 'what the editor reads');
    await editor.context().close();
    eq((await coverSeen(coach, nextMonday)).map(r => r.split(': ')[1]), ['-', '-', '-'], 'what the coach reads');
  });

  await step('Administration says whether tomorrow\'s changes go by email, and how to start them', async () => {
    await admin.goto(B + '/admin/system');
    const card = (await admin.locator('[data-testid=newsmail-card]').textContent({ timeout: 8000 })).replace(/\s+/g, ' ').trim();
    // The preview sends no mail, so they are off, and the card names its own switch, not the reminders'.
    eq(card, "Tomorrow's changes by email Off Nobody is emailed. NewsMail__Mode=Preview or On starts them; the runbook has the steps.", 'the card');
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
