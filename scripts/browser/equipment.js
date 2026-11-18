// Equipment, on the sample's next week (always ahead, whatever day it is): People & places ▸ Equipment says
// where each piece is and what needs it; a piece is added, ticked on a space, deleted and put back with Undo.
// Choosing next Friday's Aerial on the plan marks each space without what it needs; dragged into Studio A it is
// ringed, the side panel says what Studio A lacks, and the ways out are the same time in the two spaces with
// rigging; one is taken and Undo puts it back. Ctrl+K finds a space by what it has. Find a time for Aerial
// looks only where it can go and says so; the add form offers the wrong spaces last and warns of one chosen.
// A coach reads the warning and is offered no way out, and a phone shows what each space lacks without
// scrolling sideways.
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
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t);
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };

// Something done, and the toast it says, newer than any before it.
async function said(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}
const undo = page => said(page, () => page.click('[data-testid=undo]'), 'Undid: ');

// A data list as "Name — detail", and the barrier that waits for one row to read as wanted.
const rows = page => page.$$eval('[data-testid=data-row]', rs => rs.map(r => r.querySelector('.name').textContent.trim() + ' — ' + r.querySelector('small').textContent.replace(/\s+/g, ' ').trim()));
// The page allows no eval, so the test is data: { is } for the whole detail, { ends } for how it ends.
async function rowSays(page, name, test, what) {
  await page.waitForFunction(([name, test]) => {
    const r = [...document.querySelectorAll('[data-testid=data-row]')].find(r => r.querySelector('.name').textContent.trim() === name);
    const d = r ? r.querySelector('small').textContent.replace(/\s+/g, ' ').trim() : null;
    return d !== null && (test.is !== undefined ? d === test.is : d.endsWith(test.ends));
  }, [name, test], { timeout: 8000 }).catch(async () => { throw new Error(what + ': ' + JSON.stringify((await rows(page)).find(r => r.startsWith(name + ' — ')) ?? 'no row')); });
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

const row = (page, space) => page.locator('.tl-row[data-row]', { has: page.locator(`.tl-name:text-is("${space}")`) });
const block = (page, space, title) => row(page, space).locator(`[data-testid=block][data-title="${title}"]`).first();

// Pressing the chosen class again lets it go, so only one not chosen is pressed; the new render marks it.
async function choose(page, b) {
  const title = await b.getAttribute('data-title'), id = await b.getAttribute('data-id');
  if (!await b.evaluate(x => x.classList.contains('chosen'))) await b.click();
  await page.waitForFunction(([t, id]) => document.querySelector('[data-testid=inspector-title]')?.textContent === t
    && document.querySelector(`[data-testid=block][data-id="${id}"]`)?.classList.contains('chosen'), [title, id]);
}

// What each space row says it lacks for the chosen class, as "Studio A: without …".
const lacks = page => page.$$eval('[data-testid=timeline] .tl-row[data-row]', rs => rs
  .filter(r => r.querySelector('[data-testid=row-lacks]'))
  .map(r => r.querySelector('.tl-name').textContent.trim() + ': ' + r.querySelector('[data-testid=row-lacks]').textContent.trim()));

const ways = page => page.$$eval('[data-testid=way-out]', ls => ls.map(l => l.querySelector('[data-testid=way-take]').textContent.trim()
  + [...l.querySelectorAll('[data-testid=way-note]')].map(n => (n.classList.contains('good') ? ' ✓ ' : ' ! ') + n.textContent.trim()).join('')));

// Drag a class straight up or down onto another space's row at the same time, say what the drag says over
// it, and let go.
async function dragToRow(page, b, space) {
  const box = await b.boundingBox();
  const target = await row(page, space).locator('.tl-track').boundingBox();
  const x = box.x + box.width / 2, y0 = box.y + box.height / 2, y1 = target.y + target.height / 2;
  await page.mouse.move(x, y0);
  await page.mouse.down();
  for (let i = 1; i <= 8; i++) await page.mouse.move(x, y0 + (y1 - y0) * i / 8);
  const tip = await page.waitForSelector('[data-testid=drag-busy]', { timeout: 3000 }).then(t => t.textContent()).catch(() => null);
  await page.mouse.up();
  return tip;
}

const STUDIO_A = 'Space "Studio A" lacks what "Aerial" needs: Crash mats, Rigging points (Fri 13:00–14:30)';
const LACKING = ['Gym: without Rigging points', 'Studio A: without Crash mats and Rigging points', 'Studio B: without Rigging points', 'Canteen: without Crash mats and Rigging points'];

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today), nextFriday = addDays(monday, 11);
  log('     today', today, '· next Friday', nextFriday);

  await step('People & places ▸ Equipment: where each piece is and what needs it; spaces and classes say theirs', async () => {
    await admin.goto(B + '/data/equipment');
    await admin.waitForSelector('[data-testid=data-row]');
    eq(await rows(admin), [
      'Crash mats — In Aerial Hall, Big Top, Gym and Studio B · needed by Acrobatics and Aerial',
      'Mirrors — In Studio A and Studio B · needed by no class',
      'Rigging points — In Aerial Hall and Big Top · needed by Aerial',
      'Sprung floor — In Big Top, Studio A and Studio B · needed by Contemporary dance',
    ], 'the list');
    await admin.screenshot({ path: OUT + 'equipment-list.png' });
    await admin.goto(B + '/data/rooms');
    await admin.waitForSelector('[data-testid=data-row]');
    const spaces = await rows(admin);
    for (const [name, has] of [['Aerial Hall', ' · Crash mats, Rigging points'], ['Studio A', ' · Mirrors, Sprung floor'], ['Gym', ' · Crash mats']])
      if (!spaces.find(r => r.startsWith(name + ' — '))?.endsWith(has)) throw new Error(name + ' reads ' + JSON.stringify(spaces.find(r => r.startsWith(name + ' — '))));
    if (/ · [A-Z][a-z]+ [a-z]+$/.test(spaces.find(r => r.startsWith('Canteen — ')))) throw new Error('the Canteen lists equipment: ' + spaces.find(r => r.startsWith('Canteen — ')));
    await admin.goto(B + '/data/classes');
    await admin.waitForSelector('[data-testid=data-row]');
    const classes = await rows(admin);
    eq(classes.find(r => r.startsWith('Aerial — ')), 'Aerial — Counted · Technique · Hard · needs Crash mats, Rigging points', 'Aerial\'s row');
    eq(classes.find(r => r.startsWith('Juggling — ')), 'Juggling — Counted · Technique · Moderate', 'Juggling\'s row');
    await admin.locator('[data-testid=data-row]:has(.name:text-is("Aerial"))').click();
    await titled(admin, 'Edit Aerial');
    const ticked = await admin.$$eval('[data-testid=class-needs] label', ls => ls.map(l => (l.querySelector('input').checked ? '☑ ' : '☐ ') + l.textContent.trim()));
    eq(ticked, ['☑ Crash mats', '☐ Mirrors', '☑ Rigging points', '☐ Sprung floor'], 'Aerial\'s form');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a piece is added (not twice under one name), ticked on the Gym, deleted off it with the piece, and Undo puts both back', async () => {
    await admin.goto(B + '/data/equipment');
    await admin.waitForSelector('[data-testid=data-row]');
    await admin.click('[data-testid=data-add]');
    await titled(admin, 'Add a piece of equipment');
    await admin.fill('[data-testid=equipment-name]', ' crash MATS ');
    await admin.waitForSelector('[data-testid=equipment-taken]');
    eq(squash(await admin.textContent('[data-testid=equipment-taken]')), 'The school already lists crash MATS.', 'the notice');
    if (!await admin.locator('[data-testid=dialog-ok]').isDisabled()) throw new Error('a second Crash mats may be saved');
    await admin.fill('[data-testid=equipment-name]', 'Trampoline');
    await admin.waitForSelector('[data-testid=equipment-taken]', { state: 'detached' });
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Added the equipment Trampoline');
    await rowSays(admin, 'Trampoline', { is: 'In no space · needed by no class' }, 'the new row');

    await admin.goto(B + '/data/rooms');
    await admin.locator('[data-testid=data-row]:has(.name:text-is("Gym"))').click();
    await titled(admin, 'Edit Gym');
    await admin.locator('[data-testid=room-has] label', { hasText: 'Trampoline' }).locator('input').check();
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Changed the space Gym');
    await rowSays(admin, 'Gym', { ends: ' · Crash mats, Trampoline' }, 'the Gym');

    await admin.goto(B + '/data/equipment');
    await rowSays(admin, 'Trampoline', { is: 'In Gym · needed by no class' }, 'the Trampoline row');
    await admin.locator('[data-testid=data-row]:has(.name:text-is("Trampoline"))').click();
    await titled(admin, 'Edit Trampoline');
    eq(squash(await admin.textContent('[data-testid=equipment-uses]')), 'In Gym · needed by no class. Deleting it takes it off them too.', 'the form');
    await said(admin, () => admin.click('[data-testid=data-delete]'), 'Deleted the equipment Trampoline');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForFunction(() => ![...document.querySelectorAll('[data-testid=data-row] .name')].some(n => n.textContent.trim() === 'Trampoline'));
    await admin.goto(B + '/data/rooms');
    await rowSays(admin, 'Gym', { ends: ' · Crash mats' }, 'the Gym once it is deleted');
    await undo(admin);
    await rowSays(admin, 'Gym', { ends: ' · Crash mats, Trampoline' }, 'the Gym after Undo');
    await undo(admin);
    await undo(admin);
    await admin.goto(B + '/data/equipment');
    await admin.waitForSelector('[data-testid=data-row]');
    eq((await rows(admin)).map(r => r.split(' — ')[0]), ['Crash mats', 'Mirrors', 'Rigging points', 'Sprung floor'], 'the list after three Undos');
  });

  let aerialId;
  await step('choosing next Friday\'s Aerial marks each space without what it needs, and only those', async () => {
    await nextWeeksDay(admin, monday, 4);
    const aerial = block(admin, 'Aerial Hall', 'Aerial');
    aerialId = await aerial.getAttribute('data-id');
    eq(await lacks(admin), [], 'before anything is chosen');
    // Juggling needs nothing: no space is marked, and its panel says nothing of it.
    await choose(admin, block(admin, 'Studio B', 'Juggling'));
    eq(await lacks(admin), [], 'with Juggling chosen');
    if (await admin.locator('[data-testid=inspector-needs]').count()) throw new Error('Juggling\'s panel says it needs something');
    await choose(admin, aerial);
    await admin.waitForSelector('[data-testid=row-lacks]');
    eq(await lacks(admin), LACKING, 'the spaces marked');
    const title = await row(admin, 'Studio A').locator('.tl-label').getAttribute('title');
    // The label's title stays its name, for a name cut short: what it lacks is the line under it.
    eq(title, 'Studio A', 'Studio A\'s label');
    if (await admin.locator('[data-testid=inspector] [data-testid=inspector-clash]').count()) throw new Error('Aerial in the Aerial Hall is warned of');
    eq(squash(await admin.textContent('[data-testid=inspector-needs]')), 'Crash mats and Rigging points', 'the side panel');
    await admin.screenshot({ path: OUT + 'equipment-chosen.png' });
  });

  await step('dragged into Studio A it is ringed, the panel says what Studio A lacks, and the ways out are the two spaces with rigging', async () => {
    const tip = await dragToRow(admin, block(admin, 'Aerial Hall', 'Aerial'), 'Studio A');
    eq(tip, 'Studio A without Crash mats and Rigging points', 'said over it while dragging');
    await admin.waitForFunction(() => [...document.querySelectorAll('[data-testid=toast]')].some(t => t.textContent.includes('Moved Aerial to Studio A')));
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Aerial' && document.querySelectorAll('[data-testid=way-out]').length > 0);
    eq((await admin.locator('[data-testid=inspector-clash]').allInnerTexts()).map(squash), [STUDIO_A], 'the panel');
    eq(await admin.$$eval('[data-testid=block].clash', bs => bs.map(b => b.dataset.title)), ['Aerial'], 'what is ringed');
    eq(await ways(admin), ['Aerial Hall, same time ✓ Aerial has been here before', 'Big Top, same time ! Aerial has not been here before'], 'the ways out');
    await admin.screenshot({ path: OUT + 'equipment-ways-out.png' });
  });

  await step('a coach reads what Studio A lacks and is offered no way out', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await nextWeeksDay(coach, monday, 4);
    await choose(coach, block(coach, 'Studio A', 'Aerial'));
    await coach.waitForSelector('[data-testid=inspector-clash]');
    eq((await coach.locator('[data-testid=inspector-clash]').allInnerTexts()).map(squash), [STUDIO_A], 'the panel');
    eq(squash(await coach.textContent('[data-testid=inspector-needs]')), 'Crash mats and Rigging points', 'what it needs');
    if (await coach.locator('[data-testid=ways-out]').count()) throw new Error('a coach is offered ways out');
    await coach.context().close();
  });

  await step('one press takes the Big Top, the ring goes, and two Undos put it back in Studio A and then the Aerial Hall', async () => {
    await said(admin, () => admin.locator('[data-testid=way-take]', { hasText: 'Big Top, same time' }).click(), 'Moved Aerial to Big Top');
    await admin.waitForFunction(id => document.querySelector(`[data-testid=block][data-id="${id}"]`)?.closest('.tl-row')?.querySelector('.tl-name')?.textContent.trim() === 'Big Top', aerialId);
    await admin.waitForFunction(() => !document.querySelector('[data-testid=inspector-clash]'));
    if (await admin.locator('[data-testid=block].clash').count()) throw new Error('something is still ringed');
    await undo(admin);
    await admin.waitForFunction(id => document.querySelector(`[data-testid=block][data-id="${id}"]`)?.closest('.tl-row')?.querySelector('.tl-name')?.textContent.trim() === 'Studio A', aerialId);
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=block].clash').length === 1);
    await undo(admin);
    await admin.waitForFunction(id => document.querySelector(`[data-testid=block][data-id="${id}"]`)?.closest('.tl-row')?.querySelector('.tl-name')?.textContent.trim() === 'Aerial Hall', aerialId);
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=block].clash').length === 0);
  });

  await step('Ctrl+K finds the spaces with rigging points by what they have', async () => {
    await admin.keyboard.press('Control+k');
    await admin.waitForSelector('[data-testid=find]');
    await admin.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
    await admin.keyboard.type('rigging');
    // Each letter is a round trip, and "ri" already lists the Big Top by what it has, so the list is waited for whole:
    // anything short of the word lists Ada Brightwater, or more.
    const want = ['Aerial Hall — Hall · has Rigging points', 'Big Top — Hall · has Rigging points'];
    const read = () => admin.$$eval('[data-testid=find-hit]', hs => hs.map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim()));
    await admin.waitForFunction(want => JSON.stringify([...document.querySelectorAll('[data-testid=find-hit]')]
      .map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim())) === JSON.stringify(want), want, { timeout: 8000 })
      .catch(async () => { throw new Error(`what Find offers for "${await admin.inputValue('[data-testid=find-input]')}": ${JSON.stringify(await read())}, not ${JSON.stringify(want)}`); });
    await admin.keyboard.press('Escape');
    await admin.waitForSelector('[data-testid=find]', { state: 'detached' });
  });

  await step('Find another time for Aerial in any space looks only where it can go, says so, and one space without it finds nothing', async () => {
    await choose(admin, block(admin, 'Aerial Hall', 'Aerial'));
    await admin.click('[data-testid=inspector-find-time]');
    await titled(admin, 'Find another time for Aerial');
    await admin.waitForSelector('[data-testid=openings]');
    if (await admin.locator('[data-testid=find-time-needs]').count()) throw new Error('the halls alone, and it says something was left out');
    await admin.selectOption('[data-testid=find-time-space]', 'any');
    await admin.waitForSelector('[data-testid=find-time-needs]');
    eq(squash(await admin.textContent('[data-testid=find-time-needs]')), 'Aerial needs Crash mats and Rigging points, so only Aerial Hall and Big Top are looked in.', 'the line');
    const where = await admin.$$eval('[data-testid=opening] .ft-where', ws => ws.map(w => [w.querySelector('b')?.textContent.trim(), w.querySelector('small:not(.ft-later)')?.textContent.trim() ?? '']));
    if (where.length === 0) throw new Error('no time found at all');
    for (const [space, more] of where)
      if (!['Aerial Hall', 'Big Top'].includes(space) || !/^(|or (Aerial Hall|Big Top))$/.test(more)) throw new Error('a time in ' + space + ' ' + more);
    const option = await admin.$$eval('[data-testid=find-time-space] option', os => os.map(o => o.textContent.trim()).find(t => t.startsWith('Studio A')));
    eq(option, 'Studio A — without Crash mats and Rigging points', 'Studio A\'s option');
    await admin.screenshot({ path: OUT + 'equipment-find-time.png' });
    await admin.selectOption('[data-testid=find-time-space]', { label: option });
    await admin.waitForSelector('[data-testid=openings-none]');
    eq(squash(await admin.textContent('[data-testid=find-time-needs]')), 'Aerial needs Crash mats and Rigging points, and Studio A lacks Crash mats and Rigging points.', 'the line for Studio A');
    if (await admin.locator('[data-testid=make-room]').count()) throw new Error('room is made in Studio A');
    await admin.click('[data-testid=dialog-close]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('the add form offers the spaces without it last and says so, and warns of Studio A once chosen', async () => {
    await admin.click('[data-testid=add-session]');
    await titled(admin, 'Add a class');
    await admin.selectOption('[data-testid=session-class]', { label: 'Aerial' });
    await admin.fill('[data-testid=session-date]', nextFriday); await admin.press('[data-testid=session-date]', 'Tab');
    await admin.fill('[data-testid=session-start]', '15:00'); await admin.press('[data-testid=session-start]', 'Tab');
    await admin.fill('[data-testid=session-end]', '16:30'); await admin.press('[data-testid=session-end]', 'Tab');
    await admin.selectOption('[data-testid=session-for]', { label: 'A group' }).catch(() => {});
    await admin.selectOption('[data-testid=session-group]', { label: 'BA 1' });
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=free-space][data-lacks="1"]').length > 0);
    const chips = await admin.$$eval('[data-testid=free-space]', cs => cs.map(c => c.querySelector('span').textContent.trim() + (c.querySelector('small') ? ' (' + c.querySelector('small').textContent.trim() + ')' : '')));
    eq(chips, ['Aerial Hall', 'Big Top', 'Gym (without Rigging points)', 'Studio A (without Crash mats and Rigging points)', 'Studio B (without Rigging points)', 'Canteen (without Crash mats and Rigging points)'], 'free then');
    const option = await admin.$$eval('[data-testid=session-room] option', os => os.map(o => o.textContent.trim()).find(t => t.startsWith('Studio B')));
    eq(option, 'Studio B — without Rigging points', 'Studio B\'s option');
    await admin.locator('[data-testid=free-space]', { hasText: 'Studio A' }).click();
    await admin.waitForFunction(() => /lacks what/.test(document.querySelector('[data-testid=session-warnings]')?.textContent ?? ''));
    eq((await admin.locator('[data-testid=session-warnings] li').allInnerTexts()).map(squash),
      ['Space "Studio A" lacks what "Aerial" needs: Crash mats, Rigging points (Fri 15:00–16:30)'], 'the form warns');
    if (await admin.locator('[data-testid=dialog-ok]').isDisabled()) throw new Error('the form refuses');
    await admin.screenshot({ path: OUT + 'equipment-add.png' });
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a phone: what each space lacks for Aerial shows under its name, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await nextWeeksDay(phone, monday, 4);
    await choose(phone, block(phone, 'Aerial Hall', 'Aerial'));
    await phone.waitForSelector('[data-testid=row-lacks]');
    eq(await lacks(phone), LACKING, 'the spaces marked');
    const fit = await phone.evaluate(() => ({
      page: document.documentElement.scrollWidth, width: innerWidth,
      shown: [...document.querySelectorAll('[data-testid=row-lacks]')].every(s => {
        const b = s.getBoundingClientRect(), l = s.closest('.tl-label').getBoundingClientRect();
        return getComputedStyle(s).display !== 'none' && b.height > 0 && b.left >= l.left - 1 && b.right <= l.right + 1;
      }),
    }));
    log('     ' + JSON.stringify(fit));
    if (fit.page > fit.width) throw new Error('the page scrolls sideways: ' + fit.page + ' > ' + fit.width);
    if (!fit.shown) throw new Error('a space\'s lack is hidden or spills out of its label');
    await phone.screenshot({ path: OUT + 'equipment-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
