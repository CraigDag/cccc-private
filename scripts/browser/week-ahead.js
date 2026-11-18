// Next week on Now, from Thursday on, for whoever plans the timetable. On the sample week's Thursday an
// administrator's Now says next week is ready, with the two classes it drops from this week; somebody away and a
// clash typed in for next week are said, each opening where it is put right, and Undo takes both back; so is a day
// closed. With the weeks after this one cleared, next week is blank and this week is offered to copy onto it, which
// fills it; Undo empties it again and then brings it back. An editor is told the same, a coach nothing; a phone fits
// it. Start the preview with DAY=thu AT=10:15.
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
// The ISO week number, as the app numbers weeks: the week's Thursday decides its year.
const iso = s => {
  const d = new Date(s + 'T12:00:00Z');
  d.setUTCDate(d.getUTCDate() + 3 - (d.getUTCDay() + 6) % 7);
  const first = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
  first.setUTCDate(first.getUTCDate() + 3 - (first.getUTCDay() + 6) % 7);
  return 1 + Math.round((d - first) / (7 * 864e5));
};
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

// Next week's lines on Now, as [kind, what, the line under it], and whether it says it is ready.
const ahead = page => page.$$eval('[data-testid=ahead] .need', ns => ns.map(n => [n.querySelector('.need-kind').textContent.trim(),
  n.querySelector('b').textContent.replace(/\s+/g, ' ').trim(), n.querySelector('small')?.textContent.replace(/\s+/g, ' ').trim() ?? null]));
const ready = page => page.$eval('[data-testid=ahead-ready]', e => e.textContent.replace(/\s+/g, ' ').trim()).catch(() => null);
// Now (the page it is on), once next week's lines read as wanted: what was there before may already have the same heading.
async function nowSays(page, lines, readyLine, what) {
  await page.waitForSelector('[data-testid=ahead-head]');
  await page.waitForFunction(([lines, readyLine]) => {
    const got = [...document.querySelectorAll('[data-testid=ahead] .need')].map(n => [n.querySelector('.need-kind').textContent.trim(),
      n.querySelector('b').textContent.replace(/\s+/g, ' ').trim(), n.querySelector('small')?.textContent.replace(/\s+/g, ' ').trim() ?? null]);
    const r = document.querySelector('[data-testid=ahead-ready]')?.textContent.replace(/\s+/g, ' ').trim() ?? null;
    return JSON.stringify(got) === JSON.stringify(lines) && r === readyLine;
  }, [lines, readyLine], { timeout: 8000 }).catch(async () => { throw new Error(`${what}: ${JSON.stringify(await ahead(page))} and ${JSON.stringify(await ready(page))}, not ${JSON.stringify(lines)} and ${JSON.stringify(readyLine)}`); });
}
async function toNow(page) {
  await page.goto(B + '/');
  await page.waitForSelector('[data-testid=now-page]');
}

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  if (today !== addDays(monday, 3)) {
    log(`FAIL the preview says it is ${today}: start it with DAY=thu AT=10:15 scripts/preview.sh`);
    await browser.close(); process.exit(1);
  }
  const d = n => addDays(monday, n);
  log('     today', today, '· next Monday', d(7));
  const HEAD = `Next week · Week ${iso(d(7))}`;
  const CHANGES = ['Changes', `2 changes from week ${iso(d(0))}`, 'What is different, to look over before it starts'];
  const READY = 'Ready: 25 classes, nothing clashes, nobody needs cover.';

  await step('an administrator\'s Now on a Thursday says next week is ready, and what it drops from this week', async () => {
    await toNow(admin);
    await nowSays(admin, [CHANGES], READY, 'next week');
    eq(squash(await admin.textContent('[data-testid=ahead-head]')), HEAD, 'the heading');
    await admin.screenshot({ path: OUT + 'week-ahead-ready.png' });
    await admin.click('[data-testid=ahead-changes]');
    await titled(admin, `What is different in week ${iso(d(7))}`);
    // Against this week, the one before it, as Now counted them.
    const from = squash(await admin.textContent('[data-testid=week-changes-from]'));
    if (!from.startsWith(`Week ${iso(d(7))}, `) || !from.includes(` against week ${iso(d(0))}, `) || from.includes('the last week before it')) throw new Error('the form compares ' + from);
    eq(await admin.$$eval('[data-testid=week-change]', rs => rs.map(r => r.dataset.kind)), ['Gone', 'Gone'], 'what is different');
    await admin.click('[data-testid=dialog-x]');
    await closed(admin);
  });

  let first = null;
  await step('somebody away and a clash typed in for next week are said on Now, each opening where it is put right', async () => {
    // Ada away next Thursday: her acrobatics then needs cover.
    await admin.keyboard.press('Control+k');
    await admin.waitForSelector('[data-testid=find]');
    await admin.keyboard.type('ada away next thu');
    await admin.waitForFunction(w => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim() === w, `Record Ada Brightwater away · ${short(d(10))}`)
      .catch(async () => { throw new Error('Find offers ' + JSON.stringify(await admin.locator('[data-testid=find-hit] .find-label').allInnerTexts())); });
    await admin.keyboard.press('Enter');
    await titled(admin, 'Record an absence');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    await closed(admin);
    // Juggling next Tuesday at ten, as Find fills it in: BA 2, who have dance then.
    await admin.keyboard.press('Control+k');
    await admin.waitForSelector('[data-testid=find]');
    await admin.keyboard.type('juggling next tue 10-11');
    await admin.waitForFunction(w => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim() === w, `Add Juggling · ${short(d(8))} 10:00–11:00`)
      .catch(async () => { throw new Error('Find offers ' + JSON.stringify(await admin.locator('[data-testid=find-hit] .find-label').allInnerTexts())); });
    await admin.keyboard.press('Enter');
    await titled(admin, 'Add a class');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), `Added Juggling on ${short(d(8))}`);
    await closed(admin);
    await toNow(admin);
    await admin.waitForSelector('[data-testid=ahead-clashes]');
    const lines = await ahead(admin);
    log('     next week now says ' + JSON.stringify(lines));
    first = lines.find(l => l[0] === 'Clashes');
    if (!first) throw new Error('no clashes line: ' + JSON.stringify(lines));
    // Cleo teaches Juggling as she usually does, and teaches Aerial then; BA 2 has dance then: four, ringed.
    eq(first[1], '4 clashes', 'how many clash');
    eq(lines.find(l => l[0] === 'Cover'), ['Cover', '1 class needs cover', `Acrobatics ${short(d(10))} 09:00`], 'the cover line');
    eq(lines.map(l => l[0]), ['Clashes', 'Cover', 'Changes'], 'the lines');
    eq(lines[2], ['Changes', `3 changes from week ${iso(d(0))}`, CHANGES[2]], 'the changes, Juggling one of them');
    if (await ready(admin)) throw new Error('it still says it is ready');
    // Spaced apart as Today needs spaces its own, never touching.
    const gaps = await admin.$$eval('[data-testid=ahead] .need', ns => ns.slice(1).map((n, i) => Math.round(n.getBoundingClientRect().top - ns[i].getBoundingClientRect().bottom)));
    if (gaps.length < 2 || gaps.some(g => g < 6)) throw new Error('next week\'s lines touch: ' + JSON.stringify(gaps));
    await admin.screenshot({ path: OUT + 'week-ahead-issues.png' });
    // The clashes open next week in the plan with its issues beside it, the first as Now said it.
    await admin.click('[data-testid=ahead-clashes]');
    await admin.waitForSelector(`[data-testid=plan-page][data-scale=Week][data-day="${d(7)}"] [data-testid=issues]`);
    const issues = await admin.$$eval('[data-testid=issue]', is => is.map(i => i.textContent.replace(/\s+/g, ' ').trim()));
    const clashes = issues.filter(i => i.startsWith('Clash'));
    eq(clashes.length, +first[1].split(' ')[0], 'the clashes beside the plan');
    if (!first[2].startsWith(clashes[0].replace(/^Clash/, '').trim())) throw new Error(`Now said "${first[2]}", the plan "${clashes[0]}"`);
    if (!issues.some(i => i.startsWith('Cover') && i.includes('Acrobatics'))) throw new Error('the plan does not list the cover: ' + JSON.stringify(issues));
    // Cover opens the cover page on next week.
    await toNow(admin);
    await admin.click('[data-testid=ahead-cover]');
    await admin.waitForFunction(w => document.querySelector('[data-testid=week-number]')?.textContent.trim().startsWith(w), `Week ${iso(d(7))}`);
    eq(await admin.$$eval('[data-testid=cover-gap] .cc-title', ts => ts.map(t => t.textContent.trim())), ['Acrobatics'], 'what needs cover');
  });

  await step('a phone: next week\'s lines wrap to fit, the first clash said whole over several lines, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 360, height: 780 });
    await phone.waitForSelector('[data-testid=ahead-clashes]');
    const f = await phone.evaluate(() => {
      // The lines a piece of text is drawn on, by where each starts.
      const lines = e => { const r = document.createRange(); r.selectNodeContents(e); return new Set([...r.getClientRects()].map(x => Math.round(x.top))).size; };
      return {
        page: document.documentElement.scrollWidth, width: innerWidth,
        out: [...document.querySelectorAll('[data-testid=ahead-head], [data-testid=ahead] .need, [data-testid=ahead] .need b, [data-testid=ahead] .need small')]
          .filter(e => { const b = e.getBoundingClientRect(); return b.width === 0 || b.left < -1 || b.right > innerWidth + 1; })
          .map(e => e.dataset.testid || e.className || e.tagName.toLowerCase()),
        found: document.querySelectorAll('[data-testid=ahead] .need').length,
        clash: lines(document.querySelector('[data-testid=ahead-clashes] small')),
      };
    });
    log('     the phone ' + JSON.stringify(f));
    if (f.found < 3) throw new Error('nothing measured: ' + f.found + ' lines');
    if (f.page > f.width) throw new Error('the page scrolls sideways, ' + f.page + ' > ' + f.width);
    if (f.out.length) throw new Error('outside the screen: ' + f.out.join(', '));
    if (f.clash < 2) throw new Error('the first clash is on ' + f.clash + ' line, cut short or off the screen');
    await phone.locator('[data-testid=ahead-head]').scrollIntoViewIfNeeded();
    await phone.screenshot({ path: OUT + 'week-ahead-phone-issues.png' });
    await phone.context().close();
  });

  await step('Undo takes the class and the absence back, and next week is ready again', async () => {
    await toNow(admin);
    await undo(admin);
    await undo(admin);
    await nowSays(admin, [CHANGES], READY, 'after Undo');
  });

  await step('a day closed next week is said with what it closes, the classes that day no longer counted, and Undo takes it back', async () => {
    await admin.goto(B + '/data/semesters');
    await admin.locator('button', { hasText: 'Add a closure…' }).click();
    await titled(admin, 'Add a closure');
    await admin.fill('[data-testid=dialog] input:not([type=date])', 'Staff training');
    for (const n of [0, 1]) {
      await admin.locator('[data-testid=dialog] input[type=date]').nth(n).fill(d(10));
      await admin.locator('[data-testid=dialog] input[type=date]').nth(n).press('Tab');
    }
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Added the closure Staff training');
    await closed(admin);
    await toNow(admin);
    await admin.waitForSelector('[data-testid=ahead-closed]');
    const lines = await ahead(admin), line = await ready(admin);
    log('     a day closed: ' + JSON.stringify(lines) + ' ' + line);
    eq(lines, [['Closed', 'Closed Thursday: Staff training', null], CHANGES], 'the lines');
    // Thursday's four classes no longer counted.
    eq(line, 'Ready: 21 classes, nothing clashes, nobody needs cover.', 'with Thursday closed');
    await undo(admin);
    await nowSays(admin, [CHANGES], READY, 'after Undo');
  });

  await step('with the weeks after this one cleared, next week is blank and this week is offered; copying it fills it, and Undo empties it and brings it back', async () => {
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=timeline]');
    await admin.click('[data-testid=more-menu]');
    await admin.locator('button', { hasText: 'Clear the weeks after this one…' }).click();
    await titled(admin, 'Clear the weeks after this one');
    eq(squash(await admin.textContent('[data-testid=dialog-ok]')), 'Clear 25 classes', 'OK');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Cleared the weeks after this one');
    await closed(admin);
    await toNow(admin);
    await nowSays(admin, [['Blank', 'Nothing is planned yet', `Copy week ${iso(d(0))} onto it…`]], null, 'cleared');
    await admin.screenshot({ path: OUT + 'week-ahead-blank.png' });
    await admin.click('[data-testid=ahead-blank]');
    await admin.waitForFunction(w => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent.startsWith(w), `Copy Week ${iso(d(0))}, `)
      .catch(async () => { throw new Error('the form is ' + JSON.stringify(await admin.textContent('[data-testid=dialog] #dialog-title').catch(() => 'not open'))); });
    const ticked = await admin.$$eval('[data-testid=copy-weeks] label', ls => ls.filter(l => l.querySelector('input').checked).map(l => l.querySelector('.grow').textContent.trim()));
    eq(ticked.map(t => t.split(',')[0]), [`Week ${iso(d(7))}`], 'the week ticked');
    eq(squash(await admin.textContent('[data-testid=dialog-ok]')), 'Copy onto 1 week', 'OK');
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), 'Copied a week');
    await closed(admin);
    await admin.waitForSelector('[data-testid=ahead-ready]');
    const copied = await ready(admin);
    log('     copied: ' + copied + ' ' + JSON.stringify(await ahead(admin)));
    // This week as it is, its two guest workshops too.
    eq(copied, 'Ready: 27 classes, nothing clashes, nobody needs cover.', 'after copying');
    eq(await ahead(admin), [], 'nothing different from this week');
    await undo(admin);
    await nowSays(admin, [['Blank', 'Nothing is planned yet', `Copy week ${iso(d(0))} onto it…`]], null, 'after one Undo');
    await undo(admin);
    await nowSays(admin, [CHANGES], READY, 'after two');
  });

  for (const who of ['editor', 'coach']) {
    await step(`${who === 'editor' ? 'an editor is told the same' : 'a coach is told nothing of it'}`, async () => {
      const page = await signIn(browser, who, { width: 1300, height: 900 });
      if (who === 'editor') {
        await nowSays(page, [CHANGES], READY, 'an editor\'s Now');
      } else {
        await page.waitForSelector('[data-testid=today-needs]');
        if (await page.locator('[data-testid=ahead-head]').count()) throw new Error('a coach is told of next week');
      }
      await page.context().close();
    });
  }

  await step('a phone: next week\'s lines fit, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 360, height: 780 });
    await phone.waitForSelector('[data-testid=ahead-ready]');
    const f = await phone.evaluate(() => ({
      page: document.documentElement.scrollWidth, width: innerWidth,
      out: [...document.querySelectorAll('[data-testid=ahead-head], [data-testid=ahead] .need, [data-testid=ahead] .need b, [data-testid=ahead] .need small, [data-testid=ahead-ready]')]
        .filter(e => { const b = e.getBoundingClientRect(); return b.width === 0 || b.left < -1 || b.right > innerWidth + 1; }).map(e => e.dataset.testid ?? e.className),
      found: document.querySelectorAll('[data-testid=ahead] .need, [data-testid=ahead-ready]').length,
    }));
    log('     the phone ' + JSON.stringify(f));
    if (f.found < 2) throw new Error('nothing measured');
    if (f.page > f.width) throw new Error('the page scrolls sideways, ' + f.page + ' > ' + f.width);
    if (f.out.length) throw new Error('outside the screen: ' + f.out.join(', '));
    await phone.locator('[data-testid=ahead-head]').scrollIntoViewIfNeeded();
    await phone.screenshot({ path: OUT + 'week-ahead-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
