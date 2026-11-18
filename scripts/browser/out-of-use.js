// Equipment out of use, on a preview started with DAY=wed AT=10:15 (the sample week's Wednesday, any week): the
// Crash mats form says where each is in use, and puts the Aerial Hall's out of use for next Monday to Wednesday,
// saying first which classes need them there then, and refusing to come back before it goes. Next Tuesday's Aerial
// is ringed for it, said in the clash check's words with the day it is back, and its way out is the Big Top at the
// same time; one is taken and Undo puts it back, and Friday, after it is back, is not ringed. The add form offers the
// Aerial Hall last that Tuesday and warns of it. Ctrl+K "broken" puts the Big Top's out of use from today, and the
// list of equipment and an administrator's Now say so with the classes it leaves without it this week; Back in use
// from Now, and Undo. Next week's is changed and deleted from the form, and Undo. A coach asking for a space that
// Tuesday is told, and so is the administrator answering. An editor and a coach are told of the clash and offered
// none of this, and a phone fits it.
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
// "Tue 24 Nov", as the app says a day.
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

// Waits for an element's words to read as wanted (the new render's, not the one before), and says what they read.
async function reads(page, sel, want, what) {
  await page.waitForFunction(([sel, want]) => document.querySelector(sel)?.textContent.replace(/\s+/g, ' ').trim() === want, [sel, want], { timeout: 8000 })
    .catch(async () => { throw new Error(`${what}: ${JSON.stringify(await page.$eval(sel, e => e.textContent.replace(/\s+/g, ' ').trim()).catch(() => 'not there'))}, not ${JSON.stringify(want)}`); });
}

// The equipment form's Out of use list, as "Space — what it says — its button", once it reads as wanted.
const outRows = page => page.$$eval('[data-testid=equipment-out-row]', rs => rs.map(r => r.dataset.space + ' — '
  + r.querySelector('small').textContent.replace(/\s+/g, ' ').trim() + ' — ' + r.querySelector('[data-testid=equipment-out-open]').textContent.trim()));
async function outRowsAre(page, want, what) {
  await page.waitForFunction(want => JSON.stringify([...document.querySelectorAll('[data-testid=equipment-out-row]')].map(r => r.dataset.space + ' — '
    + r.querySelector('small').textContent.replace(/\s+/g, ' ').trim() + ' — ' + r.querySelector('[data-testid=equipment-out-open]').textContent.trim())) === JSON.stringify(want), want, { timeout: 8000 })
    .catch(async () => { throw new Error(what + ': ' + JSON.stringify(await outRows(page))); });
}
// The equipment list's line for a piece, once it reads as wanted.
const listLine = (page, name) => page.$$eval('[data-testid=data-row]', (rs, name) => rs.find(r => r.querySelector('.name').textContent.trim() === name)?.querySelector('small').textContent.replace(/\s+/g, ' ').trim() ?? null, name);
async function listSays(page, name, want, what) {
  await page.waitForFunction(([name, want]) => [...document.querySelectorAll('[data-testid=data-row]')]
    .find(r => r.querySelector('.name').textContent.trim() === name)?.querySelector('small').textContent.replace(/\s+/g, ' ').trim() === want, [name, want], { timeout: 8000 })
    .catch(async () => { throw new Error(`${what}: ${JSON.stringify(await listLine(page, name))}, not ${JSON.stringify(want)}`); });
}
async function crashMats(page) {
  await page.goto(B + '/data/equipment');
  await page.waitForSelector('[data-testid=data-row]');
  await page.locator('[data-testid=data-row]:has(.name:text-is("Crash mats"))').click();
  await titled(page, 'Edit Crash mats');
  await page.waitForSelector('[data-testid=equipment-out-row]');
}
const pressOut = (page, space) => page.click(`[data-testid=equipment-out-row][data-space="${space}"] [data-testid=equipment-out-open]`);
const chosenText = (page, sel) => page.$eval(sel, s => s.selectedOptions[0]?.textContent.trim() ?? null);
async function date(page, sel, iso) { await page.fill(sel, iso); await page.press(sel, 'Tab'); }

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
const row = (page, space) => page.locator('.tl-row[data-row]', { has: page.locator(`.tl-name:text-is("${space}")`) });
const block = (page, space, title) => row(page, space).locator(`[data-testid=block][data-title="${title}"]`).first();
async function choose(page, b) {
  const title = await b.getAttribute('data-title'), id = await b.getAttribute('data-id');
  if (!await b.evaluate(x => x.classList.contains('chosen'))) await b.click();
  await page.waitForFunction(([t, id]) => document.querySelector('[data-testid=inspector-title]')?.textContent === t
    && document.querySelector(`[data-testid=block][data-id="${id}"]`)?.classList.contains('chosen'), [title, id]);
}
const lacks = page => page.$$eval('[data-testid=timeline] .tl-row[data-row]', rs => rs
  .filter(r => r.querySelector('[data-testid=row-lacks]'))
  .map(r => r.querySelector('.tl-name').textContent.trim() + ': ' + r.querySelector('[data-testid=row-lacks]').textContent.trim()));
const ways = page => page.$$eval('[data-testid=way-out]', ls => ls.map(l => l.querySelector('[data-testid=way-take]').textContent.trim()
  + [...l.querySelectorAll('[data-testid=way-note]')].map(n => (n.classList.contains('good') ? ' ✓ ' : ' ! ') + n.textContent.trim()).join('')));
const ringed = page => page.$$eval('[data-testid=block].clash', bs => bs.map(b => b.dataset.title));
const spaceOf = (page, id) => page.$eval(`[data-testid=block][data-id="${id}"]`, b => b.closest('.tl-row').querySelector('.tl-name').textContent.trim()).catch(() => null);

// Now's line for each piece out of use today: what it is, where, and what that leaves without it.
const outLines = page => page.$$eval('[data-testid=need-out]', ns => ns.map(n => [n.querySelector('.need-kind').textContent.trim(), n.querySelector('b').textContent.trim(),
  n.querySelector('small').textContent.replace(/\s+/g, ' ').trim(), n.classList.contains('warn')]));

// Ctrl+K, a query put into it in one go (so the first render after it is the query's own), and what it offers
// once a hit reads as wanted, or, wanting none, once the list is no longer the one before anything was typed.
const labels = () => [...document.querySelectorAll('[data-testid=find-hit] .find-label')].map(l => l.textContent.trim());
async function find(page, q, waitFor) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
  const before = await page.evaluate(labels);
  if (before.length === 0) throw new Error('Ctrl+K offers nothing before anything is typed');
  await page.fill('[data-testid=find-input]', q);
  await page.waitForFunction(([before, w]) => {
    const now = [...document.querySelectorAll('[data-testid=find-hit] .find-label')].map(l => l.textContent.trim());
    return w === null ? JSON.stringify(now) !== JSON.stringify(before) : now.includes(w);
  }, [before, waitFor]).catch(async () => { throw new Error(`"${q}" in Ctrl+K: ${JSON.stringify(await page.evaluate(labels))}`); });
  return page.$$eval('[data-testid=find-hit]', hs => hs.map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + (h.querySelector('small')?.textContent.trim() ?? '')));
}

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  const minute = await admin.getAttribute('[data-testid=now-page]', 'data-minute');
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  // Written for the sample week's Wednesday morning, before its 10:45 Aerial: any other day or time says so.
  if (today !== addDays(monday, 2) || minute < '10:00' || minute >= '10:45') {
    log(`FAIL the preview says it is ${today} ${minute}: start it with DAY=wed AT=10:15 scripts/preview.sh`);
    await browser.close(); process.exit(1);
  }
  const d = n => addDays(monday, n), s = n => short(d(n));
  log('     today', today, minute, '· next Monday', d(7));
  const MATS_IN_USE = 'In Aerial Hall, Big Top, Gym and Studio B · needed by Acrobatics and Aerial';
  const IN_USE = ['Aerial Hall — In use — Out of use…', 'Big Top — In use — Out of use…', 'Gym — In use — Out of use…', 'Studio B — In use — Out of use…'];
  const NEXT_WEEK = `Aerial Hall — Out of use from ${s(7)}, back ${s(10)} — Change…`;
  const TUESDAY = `Space "Aerial Hall" lacks what "Aerial" needs: Crash mats out of use, back ${s(10)} (Tue 09:00–11:00)`;

  await step('the Crash mats form says where they are in use, and putting the Aerial Hall\'s out of use says first which classes need them there', async () => {
    await crashMats(admin);
    eq(await outRows(admin), IN_USE, 'the Out of use list');
    await admin.screenshot({ path: OUT + 'out-of-use-equipment.png' });
    await pressOut(admin, 'Aerial Hall');
    await titled(admin, 'Put equipment out of use');
    eq([await chosenText(admin, '[data-testid=out-room]'), await chosenText(admin, '[data-testid=out-piece]')], ['Aerial Hall', 'Crash mats'], 'what is chosen');
    eq([await admin.inputValue('[data-testid=out-from]'), await admin.inputValue('[data-testid=out-back]')], [today, ''], 'the days');
    // From today, until somebody says it is back: today's Aerial is still ahead at 10:15, and next week's too.
    await reads(admin, '[data-testid=out-affects]', `5 classes need it there while it is out: Aerial ${s(2)} 10:45, Aerial ${s(4)} 13:00, Aerial ${s(8)} 09:00, Aerial ${s(9)} 10:45 and 1 more.`, 'from today');
    eq(await admin.getAttribute('[data-testid=out-affects]', 'class'), 'notice', 'how it is said');
    await date(admin, '[data-testid=out-from]', d(7));
    await reads(admin, '[data-testid=out-affects]', `3 classes need it there while it is out: Aerial ${s(8)} 09:00, Aerial ${s(9)} 10:45, Aerial ${s(11)} 13:00.`, 'from next Monday');
    await date(admin, '[data-testid=out-back]', d(10));
    await reads(admin, '[data-testid=out-affects]', `2 classes need it there while it is out: Aerial ${s(8)} 09:00, Aerial ${s(9)} 10:45.`, 'back next Thursday');
    await admin.screenshot({ path: OUT + 'out-of-use-form.png' });
  });

  await step('it cannot come back before it goes, and OK puts it out of use for those days, the form then saying so', async () => {
    await date(admin, '[data-testid=out-back]', d(7));
    await reads(admin, '[data-testid=out-why]', 'It comes back in use after the day it goes out of use, not before.', 'back the day it goes');
    if (await admin.locator('[data-testid=out-affects]').count()) throw new Error('it says what a refused spell would affect');
    if (!await admin.locator('[data-testid=dialog-ok]').isDisabled()) throw new Error('OK may be pressed');
    await date(admin, '[data-testid=out-back]', d(10));
    await admin.waitForSelector('[data-testid=out-why]', { state: 'detached' });
    eq(squash(await admin.textContent('[data-testid=dialog-ok]')), 'Put it out of use', 'OK');
    if (await admin.locator('[data-testid=out-back-in-use]').count()) throw new Error('a spell not yet made offers Back in use');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Put the Crash mats in Aerial Hall out of use');
    await closed(admin);
    // Today they are in use, so the list says nothing of it yet.
    await listSays(admin, 'Crash mats', MATS_IN_USE, 'the list today');
    await crashMats(admin);
    await outRowsAre(admin, [NEXT_WEEK, ...IN_USE.slice(1)], 'the Out of use list');
    await admin.click('[data-testid=dialog-cancel]');
    await closed(admin);
  });

  let aerialId;
  await step('next Tuesday\'s Aerial is ringed for it in the clash check\'s words, the Aerial Hall saying so, and the way out is the Big Top', async () => {
    await nextWeeksDay(admin, monday, 1);
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=block].clash').length > 0);
    eq(await ringed(admin), ['Aerial'], 'what is ringed');
    const aerial = block(admin, 'Aerial Hall', 'Aerial');
    aerialId = await aerial.getAttribute('data-id');
    await choose(admin, aerial);
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=way-out]').length > 0);
    eq((await admin.locator('[data-testid=inspector-clash]').allInnerTexts()).map(squash), [TUESDAY], 'the side panel');
    // In the plan's own order: the Gym's section, the halls', the studios', then the Canteen.
    eq(await lacks(admin), ['Gym: without Rigging points', `Aerial Hall: Crash mats out of use, back ${s(10)}`, 'Studio A: without Crash mats and Rigging points',
      'Studio B: without Rigging points', 'Canteen: without Crash mats and Rigging points'], 'what each space lacks for it');
    // Cut short beside the name, so said in full when it is pointed at; the name's own title stays its name.
    eq(await admin.$eval('.tl-row[data-space="Aerial Hall"] [data-testid=row-lacks]', l => l.title), `Crash mats out of use, back ${s(10)}`, 'the Aerial Hall\'s line, pointed at');
    eq(await admin.$eval('.tl-row[data-space="Aerial Hall"] .tl-label', l => l.title), 'Aerial Hall', 'the Aerial Hall\'s name, pointed at');
    eq(await ways(admin), ['Big Top, same time ! Aerial has not been here before'], 'the ways out');
    await admin.screenshot({ path: OUT + 'out-of-use-plan.png' });
  });

  await step('one press takes the Big Top and the ring goes; Undo puts it back, ringed; and Friday, when they are back, is not ringed', async () => {
    await said(admin, () => admin.locator('[data-testid=way-take]', { hasText: 'Big Top, same time' }).click(), 'Moved Aerial to Big Top');
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=block].clash').length === 0 && !document.querySelector('[data-testid=inspector-clash]'));
    eq(await spaceOf(admin, aerialId), 'Big Top', 'where it is');
    await undo(admin);
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=block].clash').length === 1);
    eq(await spaceOf(admin, aerialId), 'Aerial Hall', 'where Undo puts it');
    await admin.locator('[data-testid=day-chip]').nth(4).click();
    await admin.waitForSelector(`[data-testid=timeline][data-day="${d(11)}"] [data-testid=block][data-title="Aerial"]`);
    eq(await ringed(admin), [], 'ringed on Friday');
    eq(await admin.locator('[data-testid=timeline]').getAttribute('data-day'), d(11), 'the day');
  });

  await step('the add form offers the Aerial Hall last that Tuesday, saying why, and warns of it once chosen', async () => {
    await admin.locator('[data-testid=day-chip]').nth(1).click();
    await admin.waitForSelector(`[data-testid=timeline][data-day="${d(8)}"] .tl-row[data-row]`);
    await admin.click('[data-testid=add-session]');
    await titled(admin, 'Add a class');
    await admin.selectOption('[data-testid=session-class]', { label: 'Aerial' });
    await admin.fill('[data-testid=session-date]', d(8)); await admin.press('[data-testid=session-date]', 'Tab');
    await admin.fill('[data-testid=session-start]', '15:00'); await admin.press('[data-testid=session-start]', 'Tab');
    await admin.fill('[data-testid=session-end]', '16:30'); await admin.press('[data-testid=session-end]', 'Tab');
    await admin.selectOption('[data-testid=session-for]', { label: 'A group' }).catch(() => {});
    // BA 1 is in Creation lab until four that Tuesday; BA 2 is free then, with nothing else hard that day.
    await admin.selectOption('[data-testid=session-group]', { label: 'BA 2' });
    await admin.waitForFunction(() => [...document.querySelectorAll('[data-testid=free-space]')].some(c => c.querySelector('span').textContent.trim() === 'Aerial Hall' && c.dataset.lacks === '1'));
    const chips = await admin.$$eval('[data-testid=free-space]', cs => cs.map(c => c.querySelector('span').textContent.trim() + (c.querySelector('small') ? ' (' + c.querySelector('small').textContent.trim() + ')' : '')));
    eq(chips[0], 'Big Top', 'the first space offered');
    eq(chips.find(c => c.startsWith('Aerial Hall')), `Aerial Hall (Crash mats out of use, back ${s(10)})`, 'the Aerial Hall');
    if (chips.indexOf(chips.find(c => c.startsWith('Aerial Hall'))) < chips.findIndex(c => c.includes('('))) throw new Error('the Aerial Hall is not with the wrong spaces: ' + JSON.stringify(chips));
    const option = await admin.$$eval('[data-testid=session-room] option', os => os.map(o => o.textContent.trim()).find(t => t.startsWith('Aerial Hall')));
    eq(option, `Aerial Hall — Crash mats out of use, back ${s(10)}`, 'the Aerial Hall\'s option');
    await admin.locator('[data-testid=free-space]', { hasText: 'Aerial Hall' }).click();
    await admin.waitForFunction(() => /lacks what/.test(document.querySelector('[data-testid=session-warnings]')?.textContent ?? ''));
    eq((await admin.locator('[data-testid=session-warnings] li').allInnerTexts()).map(squash),
      [`Space "Aerial Hall" lacks what "Aerial" needs: Crash mats out of use, back ${s(10)} (Tue 15:00–16:30)`], 'the form warns');
    if (await admin.locator('[data-testid=dialog-ok]').isDisabled()) throw new Error('the form refuses');
    await admin.click('[data-testid=dialog-cancel]');
    await closed(admin);
  });

  await step('Ctrl+K "broken" offers it first, and puts the Big Top\'s crash mats out of use from today, saying what that leaves without them', async () => {
    const hits = await find(admin, 'broken', 'Put equipment out of use');
    eq(hits[0], 'Put equipment out of use — Broken or being repaired · the plan rings each class that needs it there', 'the first hit');
    await admin.keyboard.press('Enter');
    await titled(admin, 'Put equipment out of use');
    eq([await chosenText(admin, '[data-testid=out-room]'), await chosenText(admin, '[data-testid=out-piece]')], ['Aerial Hall', 'Crash mats'], 'what it starts on');
    // Only the spaces with something to be out of use, as a person reads them; the Canteen has nothing.
    eq(await admin.$$eval('[data-testid=out-room] option', os => os.map(o => o.textContent.trim())), ['Aerial Hall', 'Big Top', 'Gym', 'Studio A', 'Studio B'], 'the spaces');
    await admin.selectOption('[data-testid=out-piece]', { label: 'Rigging points' });
    await admin.waitForFunction(() => document.querySelector('[data-testid=out-piece]')?.selectedOptions[0]?.textContent.trim() === 'Rigging points');
    // Another space starts again from its own first piece.
    await admin.selectOption('[data-testid=out-room]', { label: 'Big Top' });
    await admin.waitForFunction(() => [...document.querySelectorAll('[data-testid=out-piece] option')].map(o => o.textContent.trim()).join() === 'Crash mats,Rigging points,Sprung floor');
    eq(await chosenText(admin, '[data-testid=out-piece]'), 'Crash mats', 'the piece');
    // Thursday's Acrobatics needs cover and runs all the same, so it needs them too.
    await reads(admin, '[data-testid=out-affects]', `3 classes need it there while it is out: Acrobatics ${s(3)} 09:00, Acrobatics ${s(7)} 10:45, Acrobatics ${s(10)} 09:00.`, 'the Big Top\'s crash mats');
    await admin.selectOption('[data-testid=out-piece]', { label: 'Rigging points' });
    await reads(admin, '[data-testid=out-affects]', 'No class needs it there while it is out.', 'the Big Top\'s rigging');
    eq(await admin.getAttribute('[data-testid=out-affects]', 'class'), 'hint', 'how nothing is said');
    await admin.selectOption('[data-testid=out-piece]', { label: 'Crash mats' });
    await reads(admin, '[data-testid=out-affects]', `3 classes need it there while it is out: Acrobatics ${s(3)} 09:00, Acrobatics ${s(7)} 10:45, Acrobatics ${s(10)} 09:00.`, 'the crash mats again');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Put the Crash mats in Big Top out of use');
    await closed(admin);
    await admin.goto(B + '/data/equipment');
    await listSays(admin, 'Crash mats', 'In Aerial Hall, Big Top (out of use), Gym and Studio B · needed by Acrobatics and Aerial', 'the list');
    await crashMats(admin);
    await outRowsAre(admin, [NEXT_WEEK, `Big Top — Out of use since ${s(2)}, until somebody says it is back — Change…`, ...IN_USE.slice(2)], 'the Out of use list');
    await admin.click('[data-testid=dialog-cancel]');
    await closed(admin);
  });

  await step('an administrator\'s Now says it with this week\'s class that needs them there; Back in use takes the line away, Undo brings it back, and back tomorrow leaves this week none', async () => {
    await admin.goto(B + '/');
    await admin.waitForSelector('[data-testid=need-out]');
    eq(await outLines(admin), [['Out of use', 'Crash mats in Big Top', 'until somebody says it is back · 1 class needs it there for the rest of this week', true]], 'Now');
    await admin.screenshot({ path: OUT + 'out-of-use-now.png' });
    await admin.click('[data-testid=need-out]');
    await titled(admin, 'Out of use: Crash mats in Big Top');
    if (await admin.locator('[data-testid=out-room]').count()) throw new Error('the space may be changed on a spell made');
    eq([await admin.inputValue('[data-testid=out-from]'), await admin.inputValue('[data-testid=out-back]')], [today, ''], 'the days');
    eq(squash(await admin.textContent('[data-testid=out-back-in-use]')), 'Back in use', 'the button');
    await said(admin, () => admin.click('[data-testid=out-back-in-use]'), 'Back in use: the Crash mats in Big Top');
    await closed(admin);
    await admin.waitForSelector('[data-testid=need-out]', { state: 'detached' });
    await undo(admin);
    await admin.waitForSelector('[data-testid=need-out]');
    eq((await outLines(admin)).map(l => l[1]), ['Crash mats in Big Top'], 'Now after Undo');
    // Back tomorrow: out today only, when no class needs them there; Thursday's Acrobatics has them again.
    await admin.click('[data-testid=need-out]');
    await titled(admin, 'Out of use: Crash mats in Big Top');
    await date(admin, '[data-testid=out-back]', d(3));
    await reads(admin, '[data-testid=out-affects]', 'No class needs it there while it is out.', 'back tomorrow');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Changed the days out of use for the Crash mats in Big Top');
    await closed(admin);
    await admin.waitForFunction(() => /no class needs/.test(document.querySelector('[data-testid=need-out] small')?.textContent ?? ''));
    eq(await outLines(admin), [['Out of use', 'Crash mats in Big Top', `back ${s(3)} · no class needs it there for the rest of this week`, false]], 'Now, back tomorrow');
  });

  await step('next week\'s is changed from the form (Delete, since none of it has come), deleted, and Undo puts it back', async () => {
    await crashMats(admin);
    await pressOut(admin, 'Aerial Hall');
    await titled(admin, 'Out of use: Crash mats in Aerial Hall');
    eq([await admin.inputValue('[data-testid=out-from]'), await admin.inputValue('[data-testid=out-back]')], [d(7), d(10)], 'the days');
    eq([squash(await admin.textContent('[data-testid=out-back-in-use]')), await admin.getAttribute('[data-testid=out-back-in-use]', 'class')], ['Delete', 'btn danger'], 'the button');
    eq(squash(await admin.textContent('[data-testid=dialog-ok]')), 'OK', 'OK');
    await date(admin, '[data-testid=out-back]', d(9));
    await reads(admin, '[data-testid=out-affects]', `1 class needs it there while it is out: Aerial ${s(8)} 09:00.`, 'back next Wednesday');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Changed the days out of use for the Crash mats in Aerial Hall');
    await closed(admin);
    await crashMats(admin);
    await outRowsAre(admin, [`Aerial Hall — Out of use from ${s(7)}, back ${s(9)} — Change…`, `Big Top — Out of use since ${s(2)}, back ${s(3)} — Change…`, ...IN_USE.slice(2)], 'changed');
    await pressOut(admin, 'Aerial Hall');
    await titled(admin, 'Out of use: Crash mats in Aerial Hall');
    await said(admin, () => admin.click('[data-testid=out-back-in-use]'), `No longer putting the Crash mats in Aerial Hall out of use from ${s(7)}`);
    await closed(admin);
    await crashMats(admin);
    await outRowsAre(admin, [IN_USE[0], `Big Top — Out of use since ${s(2)}, back ${s(3)} — Change…`, ...IN_USE.slice(2)], 'deleted');
    await admin.click('[data-testid=dialog-cancel]');
    await closed(admin);
    await undo(admin);
    await crashMats(admin);
    await outRowsAre(admin, [`Aerial Hall — Out of use from ${s(7)}, back ${s(9)} — Change…`, `Big Top — Out of use since ${s(2)}, back ${s(3)} — Change…`, ...IN_USE.slice(2)], 'after Undo');
    await admin.click('[data-testid=dialog-cancel]');
    await closed(admin);
  });

  await step('a coach asking for a space that Tuesday is told the Aerial Hall\'s are out of use, and so is the administrator answering', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await coach.goto(B + '/my-week');
    await coach.waitForSelector('[data-testid=cant-come]');
    await coach.click('[data-testid=ask-space]');
    await titled(coach, 'Ask for a space');
    for (const [id, v] of [['ask-date', d(8)], ['ask-start', '15:00'], ['ask-end', '16:30']]) {
      await coach.fill(`[data-testid=${id}]`, v);
      await coach.locator(`[data-testid=${id}]`).dispatchEvent('change');
    }
    await coach.selectOption('[data-testid=ask-class]', { label: 'Aerial' });
    await coach.check('[data-testid=ask-group][data-name="BA 2"]');
    const hall = `Aerial Hall — Crash mats out of use, back ${s(9)}`;
    await coach.waitForFunction(h => [...document.querySelectorAll('[data-testid=ask-room] option')].some(o => o.textContent.trim() === h), hall)
      .catch(async () => { throw new Error('the spaces read ' + JSON.stringify(await coach.$$eval('[data-testid=ask-room] option', os => os.map(o => o.textContent.trim())))); });
    const studio = await coach.$$eval('[data-testid=ask-room] option', os => os.find(o => o.textContent.trim().startsWith('Studio A'))?.value);
    await coach.selectOption('[data-testid=ask-room]', studio);
    await coach.waitForFunction(() => [...document.querySelectorAll('[data-testid=ask-free-space]')].some(c => c.querySelector('span').textContent.trim() === 'Aerial Hall' && c.dataset.lacks === '1'));
    const chips = await coach.$$eval('[data-testid=ask-free-space]', cs => cs.map(c => c.querySelector('span').textContent.trim() + (c.querySelector('small') ? ' (' + c.querySelector('small').textContent.trim() + ')' : '')));
    eq(chips[0], 'Big Top', 'the first space free then');
    eq(chips.find(c => c.startsWith('Aerial Hall')), `Aerial Hall (Crash mats out of use, back ${s(9)})`, 'the Aerial Hall');
    await said(coach, () => coach.click('[data-testid=dialog-ok]'), 'Asked for Studio A');
    await closed(coach);
    await coach.context().close();
    await admin.goto(B + '/');
    await admin.locator('[data-testid=waiting-answer] [data-testid=ask][data-kind=space]', { hasText: 'Aerial' }).click();
    await titled(admin, 'Ada Brightwater asks for a space');
    const rooms = await admin.$$eval('[data-testid=answer-room] option', os => os.map(o => o.textContent.trim()));
    eq(rooms.slice(0, 2), ['Studio A, as asked', 'Big Top, free then'], 'the first spaces offered');
    eq(rooms.find(r => r.startsWith('Aerial Hall')), `Aerial Hall, free then — Crash mats out of use, back ${s(9)}`, 'the Aerial Hall');
    await admin.click('[data-testid=dialog-cancel]');
    await closed(admin);
  });

  for (const who of ['editor', 'coach']) {
    await step(`${who === 'editor' ? 'an editor' : 'a coach'} is told of the clash it makes and offered none of this: no line on Now, nothing in Ctrl+K`, async () => {
      const page = await signIn(browser, who, { width: 1300, height: 900 });
      if (await page.locator('[data-testid=need-out]').count()) throw new Error('Now says it');
      const hits = await find(page, 'broken', null);
      if (hits.some(h => h.startsWith('Put equipment out of use'))) throw new Error('Ctrl+K offers it: ' + JSON.stringify(hits));
      await page.keyboard.press('Escape');
      await page.waitForSelector('[data-testid=find]', { state: 'detached' });
      await nextWeeksDay(page, monday, 1);
      await choose(page, block(page, 'Aerial Hall', 'Aerial'));
      await page.waitForSelector('[data-testid=inspector-clash]');
      eq((await page.locator('[data-testid=inspector-clash]').allInnerTexts()).map(squash), [TUESDAY.replace(s(10), s(9))], 'the side panel');
      // An editor may move it as the clash check suggests; a coach reads it.
      const offered = await page.locator('[data-testid=way-take]').count();
      if (who === 'coach' ? offered > 0 : offered === 0) throw new Error(offered + ' ways out offered');
      await page.context().close();
    });
  }

  await step('a phone: Now\'s line, the equipment form\'s list and the form itself fit, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 360, height: 780 });
    await phone.waitForSelector('[data-testid=need-out]');
    const fits = (page, sel) => page.evaluate(sel => ({
      page: document.documentElement.scrollWidth, width: innerWidth,
      out: [...document.querySelectorAll(sel)].filter(e => { const b = e.getBoundingClientRect(); return b.width === 0 || b.left < -1 || b.right > innerWidth + 1; }).map(e => e.dataset.testid ?? e.className),
      found: document.querySelectorAll(sel).length,
    }), sel);
    const check = (f, what) => {
      log('     ' + what + ' ' + JSON.stringify(f));
      if (f.found === 0) throw new Error(what + ': nothing measured');
      if (f.page > f.width) throw new Error(what + ': the page scrolls sideways, ' + f.page + ' > ' + f.width);
      if (f.out.length) throw new Error(what + ': outside the screen: ' + f.out.join(', '));
    };
    check(await fits(phone, '[data-testid=need-out], [data-testid=need-out] b, [data-testid=need-out] small'), 'Now');
    await phone.screenshot({ path: OUT + 'out-of-use-phone-now.png' });
    await phone.goto(B + '/data/equipment');
    await phone.waitForSelector('[data-testid=data-row]');
    await phone.locator('[data-testid=data-row]:has(.name:text-is("Mirrors"))').click();
    await titled(phone, 'Edit Mirrors');
    await outRowsAre(phone, ['Studio A — In use — Out of use…', 'Studio B — In use — Out of use…'], 'the Mirrors');
    check(await fits(phone, '[data-testid=equipment-out-row] .out-name, [data-testid=equipment-out-row] small, [data-testid=equipment-out-open]'), 'the list');
    await phone.screenshot({ path: OUT + 'out-of-use-phone-list.png' });
    await pressOut(phone, 'Studio B');
    await titled(phone, 'Put equipment out of use');
    eq([await chosenText(phone, '[data-testid=out-room]'), await chosenText(phone, '[data-testid=out-piece]')], ['Studio B', 'Mirrors'], 'what it starts on');
    check(await fits(phone, '[data-testid=out-room], [data-testid=out-piece], [data-testid=out-from], [data-testid=out-back], [data-testid=out-affects], [data-testid=dialog-ok]'), 'the form');
    await phone.screenshot({ path: OUT + 'out-of-use-phone-form.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
