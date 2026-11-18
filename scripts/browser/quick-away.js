// Somebody away, typed into Ctrl+K in one line. An administrator's "bo sick <next Monday>" comes first in Find,
// saying what it would take him off; Enter opens the absence form filled in, with those classes listed, OK
// records it, the class he taught alone needs cover, and Undo takes it away. Times, a student and a stretch of
// days are read too; words not understood are said in Find and never reach the reason box. An editor is
// offered nothing for it, not even a class titled "Off". A coach (Ada) typing "sick <next Monday>" is offered
// to ask, for herself only, and the can't-come form opens on that day. A phone fits it. Every date is in the
// sample's next week, so the walk runs on any day.
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

// Ctrl+K, the line typed, and the hits once the first is the whole "label — detail" wanted: a line read before
// its last words can already make the same label, so the barrier is the whole of it.
async function find(page, line, first) {
  await openFind(page);
  await page.keyboard.type(line);
  await page.waitForFunction(first => {
    const h = document.querySelector('[data-testid=find-hit]');
    return !!h && h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim() === first;
  }, first).catch(async () => { throw new Error(`"${line}": the first hit is not "${first}"; Find shows ${JSON.stringify(await hits(page))}`); });
  return hits(page);
}
async function openFind(page) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
}
// A line that offers nothing: Find says so once the whole line is read, and nothing it lists is about being away.
async function findsNothing(page, line) {
  await openFind(page);
  await page.keyboard.type(line);
  await page.waitForSelector('[data-testid=find] .find-none');
  return hits(page);
}
async function closeFind(page) {
  await page.keyboard.press('Escape');
  await page.waitForSelector('[data-testid=find]', { state: 'detached' });
}
// What a failed step left open is closed, so the next step starts as any other would.
async function tidy(page) {
  if (await page.locator('[data-testid=find]').count()) await closeFind(page);
  if (await page.locator('[data-testid=dialog]').count()) {
    await page.click('[data-testid=dialog-x]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  }
}

// The absence form as a person reads it.
const absence = page => page.evaluate(() => {
  const chosen = id => { const s = document.querySelector(`[data-testid=${id}]`); return s ? s.options[s.selectedIndex]?.text.trim() : null; };
  const value = id => document.querySelector(`[data-testid=${id}]`)?.value ?? null;
  return {
    who: chosen('absence-who'), from: value('absence-from'), to: value('absence-to'), start: value('absence-start'), end: value('absence-end'),
    notice: chosen('absence-notice'), cause: chosen('absence-cause'), reason: value('absence-reason'),
    heading: document.querySelector('[data-testid=away-heading]')?.textContent.trim() ?? null,
    takes: [...document.querySelectorAll('[data-testid=away-class]')].map(li => li.textContent.replace(/\s+/g, ' ').trim()),
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
  // Next week is the last the sample builds: Bo shares Monday's 10:45 Acrobatics with Ada and teaches the 13:00 one alone.
  const nextMonday = addDays(monday, 7), nextWednesday = addDays(monday, 9);
  const bo = `Record Bo Lindqvist away · ${said(nextMonday)}`;
  log('     today', today, '· the Monday typed', nextMonday);

  await step('somebody away typed into Ctrl+K comes first, saying what it would take them off', async () => {
    await admin.click('[data-testid=nav-now]');
    await admin.waitForSelector('[data-testid=now-page]');
    const found = await find(admin, `bo sick ${typed(nextMonday)}`, `${bo} — illness, unplanned · takes them off 2 classes, 1 would need cover`);
    log('     ' + found[0]);
    if (!(await admin.textContent('.find-foot')).includes('who is away, like')) throw new Error('the foot does not say who is away can be typed in');
    await admin.screenshot({ path: OUT + 'quick-away-find.png' });
  });

  await step('Enter opens the absence form filled in and listing those classes; OK records it, the class he taught alone needs cover, and Undo takes it away', async () => {
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    await admin.waitForSelector('[data-testid=away-takes]');
    const f = await absence(admin);
    eq([f.who, f.from, f.to, f.start, f.end, f.notice, f.cause, f.reason, f.heading, f.ok],
      ['Bo Lindqvist', nextMonday, nextMonday, '', '', 'Unplanned', 'Illness', '', 'It takes them off', true], 'the form');
    eq(f.takes, [`Acrobatics ${said(nextMonday)}, 10:45–12:00 · Ada Brightwater still teaching it`, `Acrobatics ${said(nextMonday)}, 13:00–14:30 · needs cover`], 'what it takes him off');
    await admin.screenshot({ path: OUT + 'quick-away-form.png' });
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    // On the plan that Monday, the Acrobatics he teaches alone in Studio B offers cover.
    await admin.click('[data-testid=nav-plan]');
    await admin.waitForSelector('[data-testid=timeline]');
    await admin.fill('[data-testid=day-date]', nextMonday);
    await admin.locator('[data-testid=day-date]').dispatchEvent('change');
    await admin.waitForFunction(d => document.querySelector('[data-testid=timeline]')?.dataset.day === d, nextMonday);
    await admin.locator('.tl-row', { has: admin.locator('.tl-name', { hasText: 'Studio B' }) }).locator('[data-testid=block][data-title=Acrobatics]').click();
    await admin.waitForSelector('[data-testid=cover-picks]');
    // Now it is recorded, the line says he is off nothing more: he is away already.
    eq((await find(admin, `bo sick ${typed(nextMonday)}`, `${bo} — illness, unplanned · none of their classes`))[0], `${bo} — illness, unplanned · none of their classes`, 'the line once recorded');
    await closeFind(admin);
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    eq((await find(admin, `bo sick ${typed(nextMonday)}`, `${bo} — illness, unplanned · takes them off 2 classes, 1 would need cover`))[0],
      `${bo} — illness, unplanned · takes them off 2 classes, 1 would need cover`, 'the line once undone');
    await closeFind(admin);
  });

  await step('times, a student and a stretch of days are read too', async () => {
    await tidy(admin);
    const morning = `Record Bo Lindqvist away · ${said(nextMonday)} 09:00–12:00 — planned · Acrobatics Mon 10:45: Ada Brightwater still teaching it`;
    await find(admin, `bo away ${typed(nextMonday)} 9-12`, morning);
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    await admin.waitForSelector('[data-testid=away-takes]');
    const f = await absence(admin);
    eq([f.from, f.start, f.end, f.notice, f.cause, f.takes.length], [nextMonday, '09:00', '12:00', 'Planned', 'Not said', 1], 'the form for a morning');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });

    const alba = `Record Alba Ashcombe away · ${said(nextMonday)} – ${said(nextWednesday)} — planned · misses 7 classes`;
    await find(admin, `alba absent ${typed(nextMonday)}-${typed(nextWednesday)}`, alba);
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    await admin.waitForSelector('[data-testid=away-takes]');
    const s = await absence(admin);
    eq([s.who, s.from, s.to, s.heading, s.takes.length], ['Alba Ashcombe', nextMonday, nextWednesday, 'They would miss', 7], 'the form for a student');
    if (s.takes.some(t => t.startsWith('Lunch'))) throw new Error('a student misses the whole school\'s lunch: ' + JSON.stringify(s.takes));
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('words not understood are said in Find and never reach the reason box', async () => {
    await tidy(admin);
    const want = `${bo} — illness, unplanned · takes them off 2 classes, 1 would need cover · not understood: flu`;
    await find(admin, `bo sick ${typed(nextMonday)} flu`, want);
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    eq((await absence(admin)).reason, '', 'the reason');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('an editor is offered nothing for it, not even a class titled with its words', async () => {
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    const listed = await findsNothing(editor, `bo off ${typed(nextMonday)} 9-12`);
    if (listed.length) throw new Error('an editor is offered ' + JSON.stringify(listed));
    if ((await editor.textContent('.find-foot')).includes('who is away')) throw new Error('the foot tells an editor to type who is away');
    await editor.context().close();
  });

  await step('a coach typing that she is sick is offered to ask, for herself only, and the can\'t-come form opens on the day', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    const ask = `Ask to be away · ${said(nextMonday)} — takes you off 2 classes, 1 would need cover · only the days are sent, never why`;
    eq((await find(coach, `sick ${typed(nextMonday)}`, ask))[0], ask, 'the line');
    if (!(await coach.textContent('.find-foot')).includes('say you can’t come')) throw new Error('the foot does not tell a coach she can say she can\'t come');
    await coach.screenshot({ path: OUT + 'quick-away-coach.png' });
    await coach.keyboard.press('Enter');
    await titled(coach, 'Can\'t come');
    await coach.waitForSelector('[data-testid=cant-takes]');
    const form = await coach.evaluate(() => ({
      from: document.querySelector('[data-testid=cant-from]').value, to: document.querySelector('[data-testid=cant-to]').value,
      allDay: document.querySelector('[data-testid=cant-all-day]').checked,
      takes: [...document.querySelectorAll('[data-testid=cant-class]')].length,
    }));
    eq(form, { from: nextMonday, to: nextMonday, allDay: true, takes: 2 }, 'the can\'t-come form');
    await coach.click('[data-testid=dialog-x]');
    await coach.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    // Somebody else is nothing she may ask for.
    const other = await findsNothing(coach, `bo sick ${typed(nextMonday)}`);
    if (other.some(h => h.startsWith('Ask ') || h.startsWith('Record '))) throw new Error('a coach is offered somebody else: ' + JSON.stringify(other));
    await coach.context().close();
  });

  await step('a phone: the line fits the Find box, nothing scrolls sideways, and Enter opens the form', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await find(phone, `bo sick ${typed(nextMonday)}`, `${bo} — illness, unplanned · takes them off 2 classes, 1 would need cover`);
    const fit = await phone.evaluate(() => {
      const h = document.querySelector('[data-testid=find-hit]').getBoundingClientRect(), f = document.querySelector('[data-testid=find]').getBoundingClientRect();
      return { page: document.documentElement.scrollWidth, width: innerWidth, inside: h.left >= f.left - 1 && h.right <= f.right + 1 };
    });
    log('     ' + JSON.stringify(fit));
    if (fit.page > fit.width) throw new Error('the page scrolls sideways: ' + fit.page + ' > ' + fit.width);
    if (!fit.inside) throw new Error('the line spills out of the Find box');
    await phone.screenshot({ path: OUT + 'quick-away-phone.png' });
    await phone.keyboard.press('Enter');
    await titled(phone, 'Record an absence');
    eq((await absence(phone)).who, 'Bo Lindqvist', 'the form on a phone');
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
