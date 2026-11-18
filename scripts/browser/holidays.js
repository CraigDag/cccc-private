// Public holidays: People & places ▸ Semesters ▸ Public holidays… offers Denmark's public holidays still ahead
// on the made-up school's school days, each public holiday ticked and the days many places close beside them
// (Christmas Eve, the Friday after Ascension) not, Easter one closure from Thursday to Monday. OK closes the
// ticked for the whole building in one change, the plan says the building is closed that day, the form says
// each is closed already and ticks none, and one Undo takes them all away. Ctrl+K finds it by "easter". Start
// next year lists next year's own, ticked, and the year it makes has them closed on their own days. An editor
// and a coach have none of it, and the form fits a phone. Any day, against a fresh preview (made-up data).
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
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

const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const short = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${'Sun Mon Tue Wed Thu Fri Sat'.split(' ')[d.getUTCDay()]} ${d.getUTCDate()} ${'Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec'.split(' ')[d.getUTCMonth()]}`; };
const isoWeek = iso => { const d = new Date(iso + 'T12:00:00Z'); const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + 3 - ((d.getUTCDay() + 6) % 7))); const y = new Date(Date.UTC(t.getUTCFullYear(), 0, 4)); return 1 + Math.round(((t - y) / 864e5 - 3 + ((y.getUTCDay() + 6) % 7)) / 7); };
// Easter Sunday, worked out here from the calendar rather than read off the app: the walk's own expectation.
const easter = y => {
  const a = y % 19, b = Math.floor(y / 100), c = y % 100, d = Math.floor(b / 4), e = b % 4, f = Math.floor((b + 8) / 25), g = Math.floor((b - f + 1) / 3);
  const h = (19 * a + b - d - g + 15) % 30, i = Math.floor(c / 4), k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = Math.floor((a + 11 * h + 22 * l) / 451);
  const n = h + l - 7 * m + 114;
  return `${y}-${String(Math.floor(n / 31)).padStart(2, '0')}-${String(n % 31 + 1).padStart(2, '0')}`;
};
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

// The form's list, one entry a closure it offers, as the page draws it.
const offered = page => page.$$eval('[data-testid=holiday]', hs => hs.map(h => ({
  name: h.querySelector('[data-testid=holiday-name]').textContent.trim(),
  line: h.querySelector('[data-testid=holiday-line]').textContent.replace(/\s+/g, ' ').trim(),
  from: h.dataset.from, official: h.dataset.official === 'yes', closed: h.dataset.closed === 'yes',
  ticked: h.querySelector('input[type=checkbox]').checked,
})));
// A data list as "Name — detail".
const rows = page => page.$$eval('[data-testid=data-row]', rs => rs.map(r => r.querySelector('.name').textContent.trim() + ' — ' + r.querySelector('small').textContent.replace(/\s+/g, ' ').trim()));
async function openForm(page) {
  await page.goto(B + '/data/semesters');
  await page.click('[data-testid=close-holidays]');
  await titled(page, 'Close the public holidays');
  await page.waitForSelector('[data-testid=holiday], [data-testid=holidays-none]');
}
const okSays = (page, words) => page.waitForFunction(w => document.querySelector('[data-testid=dialog-ok]')?.textContent.trim() === w, words, { timeout: 5000 })
  .catch(async () => { throw new Error(`OK says "${(await page.textContent('[data-testid=dialog-ok]')).trim()}", not "${words}"`); });
const adds = n => n === 0 ? 'Add them' : n === 1 ? 'Add 1 closure' : `Add ${n} closures`;
const projectRow = (p, name) => p.locator('[data-testid=projects] > li').filter({ has: p.locator('b', { hasText: new RegExp('^' + name.replace(/[()]/g, '\\$&') + '$') }) });
async function openFor(p, name) {
  await p.goto(B + '/admin/projects');
  await projectRow(p, name).locator('[data-testid=project-open]').click();
  await p.waitForFunction(n => [...document.querySelectorAll('[data-testid=projects] > li')].some(li => li.querySelector('b')?.textContent === n && li.textContent.includes('Open for everybody') && !li.querySelector('[data-testid=project-open]')), name);
}
const THIS_YEAR = 'Northlight 2026–27 (sample)';

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1400, height: 900 });
  await admin.goto(B + '/plan');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  // The made-up school's year: autumn from four weeks before this week, 18 weeks; spring three weeks after it, 20.
  const autumn = [addDays(mondayOf(today), -28)]; autumn.push(addDays(autumn[0], 18 * 7 - 1));
  const spring = [addDays(autumn[0], 17 * 7 + 21)]; spring.push(addDays(spring[0], 20 * 7 - 1));
  const inYear = iso => iso >= autumn[0] && iso <= autumn[1] || iso >= spring[0] && iso <= spring[1];
  let list = [], closed = [];

  await step('the public holidays ahead are offered, each public holiday ticked and the days many places close not', async () => {
    await openForm(admin);
    list = await offered(admin);
    log(`      ${list.length} offered: ` + list.map(h => `${h.name} (${h.line})${h.ticked ? ' ✓' : ''}`).join('; '));
    if (list.length === 0) throw new Error('nothing is offered: ' + await admin.textContent('[data-testid=dialog]'));
    for (const h of list) {
      if (h.from < today || !inYear(h.from)) throw new Error(`${h.name} on ${h.from} is not ahead in the semesters`);
      if (h.closed) throw new Error(`${h.name} is said to be closed already, and the sample closes no holiday`);
      if (h.ticked !== h.official) throw new Error(`${h.name} is ${h.ticked ? '' : 'not '}ticked: "${h.line}"`);
      if (!h.official && !h.line.endsWith('not a public holiday, but many places close')) throw new Error(`${h.name} does not say why it is not ticked: "${h.line}"`);
      if (!h.line.startsWith(short(h.from))) throw new Error(`${h.name} on ${h.from} says "${h.line}"`);
    }
    // Easter, when it is ahead in a semester: one closure from Maundy Thursday to Easter Monday, a weekend between.
    const years = [...new Set([today.slice(0, 4), spring[1].slice(0, 4)])].map(Number);
    const e = years.map(easter).find(e => addDays(e, -3) >= today && inYear(addDays(e, -3)) && inYear(addDays(e, 1)));
    if (e) {
      const row = list.find(h => h.name === 'Easter (Påske)');
      if (!row) throw new Error(`Easter ${e} is ahead and not offered`);
      eq(row.line, `${short(addDays(e, -3))} – ${short(addDays(e, 1))} · Maundy Thursday, Good Friday, Easter Monday`, 'Easter says');
      eq(row.from, addDays(e, -3), 'Easter starts');
    } else log('      Easter is not ahead in this year\'s semesters, so it is not looked for');
    await okSays(admin, adds(list.filter(h => h.ticked).length));
    await admin.waitForTimeout(300);
    await admin.screenshot({ path: OUT + 'holidays-form.png' });
  });

  await step('a tick taken off counts one fewer, and OK closes the ticked for the whole building in one change', async () => {
    const ticked = list.filter(h => h.ticked);
    const first = admin.locator('[data-testid=holiday]').nth(list.indexOf(ticked[0])).locator('input');
    await first.uncheck();
    await okSays(admin, adds(ticked.length - 1));
    await first.check();
    await okSays(admin, adds(ticked.length));
    await said(admin, () => admin.click('[data-testid=dialog-ok]'), ticked.length === 1 ? 'Closed ' + ticked[0].name : `Closed ${ticked.length} public holidays`);
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    const wanted = ticked.map(h => `${h.name} — ${h.line.split(' · ')[0]} · whole building`);
    await admin.waitForFunction(w => { const now = [...document.querySelectorAll('[data-testid=data-row]')].map(r => r.querySelector('.name').textContent.trim() + ' — ' + r.querySelector('small').textContent.replace(/\s+/g, ' ').trim()); return w.every(x => now.includes(x)); }, wanted, { timeout: 5000 })
      .catch(async () => { throw new Error(`${JSON.stringify(wanted)} are not all among ${JSON.stringify(await rows(admin))}`); });
    const now = await rows(admin);
    for (const h of list.filter(h => !h.ticked)) if (now.some(r => r.startsWith(h.name + ' — '))) throw new Error(h.name + ' was closed without its tick');
    closed = ticked;
  });

  await step('the plan says the building is closed on the first of them', async () => {
    const h = closed[0];
    await admin.goto(B + '/plan');
    await admin.click('[data-testid=scale-day]');
    await admin.waitForSelector('[data-testid=plan-page][data-scale=Day]');
    await admin.fill('[data-testid=day-date]', h.from);
    await admin.waitForSelector(`[data-testid=timeline][data-day="${h.from}"]`);
    eq((await admin.textContent('[data-testid=day-closed]')).trim(), 'The building is closed: ' + h.name, `${h.from} says`);
    await admin.screenshot({ path: OUT + 'holidays-plan-day.png' });
  });

  await step('the form again says each is closed already, ticks none and adds nothing', async () => {
    await openForm(admin);
    const again = await offered(admin);
    eq(again.map(h => h.name), list.map(h => h.name), 'the list');
    for (const h of again) {
      const was = closed.some(c => c.name === h.name);
      if (h.closed !== was) throw new Error(`${h.name} is ${h.closed ? '' : 'not '}said to be closed already`);
      if (was && !h.line.endsWith('closed already: ' + h.name)) throw new Error(`${h.name} says "${h.line}"`);
      if (h.ticked) throw new Error(h.name + ' is ticked again');
    }
    await okSays(admin, 'Add them');
    if (!(await admin.isDisabled('[data-testid=dialog-ok]'))) throw new Error('OK can be pressed with nothing ticked');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('one Undo takes every one of them away', async () => {
    await said(admin, () => admin.click('[data-testid=undo]'), 'Undid: ' + (closed.length === 1 ? 'Closed ' + closed[0].name : `Closed ${closed.length} public holidays`));
    await admin.waitForFunction(names => ![...document.querySelectorAll('[data-testid=data-row] .name')].some(n => names.includes(n.textContent.trim())), closed.map(h => h.name), { timeout: 5000 })
      .catch(async () => { throw new Error('still listed: ' + JSON.stringify(await rows(admin))); });
  });

  await step('Ctrl+K finds it by "easter", and Enter opens the form', async () => {
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=plan-page]');
    await admin.keyboard.press('Control+k');
    await admin.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
    await admin.keyboard.type('easter');
    await admin.waitForFunction(() => [...document.querySelectorAll('[data-testid=find-hit] .find-label')].some(l => l.textContent.trim() === 'Close the public holidays'), null, { timeout: 5000 })
      .catch(async () => { throw new Error('Find says ' + JSON.stringify(await admin.locator('[data-testid=find-hit], .find-none').allInnerTexts())); });
    const hits = await admin.$$eval('[data-testid=find-hit] .find-label', ls => ls.map(l => l.textContent.trim()));
    eq(hits[0], 'Close the public holidays', 'the first hit');
    await admin.keyboard.press('Enter');
    await titled(admin, 'Close the public holidays');
    await admin.waitForSelector('[data-testid=holiday]');
    eq((await offered(admin)).map(h => h.name), list.map(h => h.name), 'the list opened from Find');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('Start next year lists next year\'s own public holidays, ticked, and the year it makes has them closed', async () => {
    await admin.goto(B + '/admin/projects');
    await projectRow(admin, THIS_YEAR).locator('[data-testid=project-next-year]').click();
    await admin.waitForSelector('[data-testid=next-year-semesters]');
    if (!(await admin.isChecked('[data-testid=next-year-holidays]'))) throw new Error('the holidays are not ticked to start with');
    const text = (await admin.textContent('[data-testid=next-year-holiday-list]')).replace(/\s+/g, ' ').trim();
    log('      ' + text);
    const listed = text.split('. Each on its own day')[0].split('; ').map(s => s.split(', ')[0]);
    // Next year's spring starts in the same week number; its Easter is next year's, worked out here.
    let nextSpring = addDays(spring[0], 364); if (isoWeek(nextSpring) !== isoWeek(spring[0])) nextSpring = addDays(nextSpring, 7);
    const e = easter(+nextSpring.slice(0, 4));
    if (addDays(e, -3) >= nextSpring && addDays(e, 1) <= addDays(nextSpring, 20 * 7 - 1)) {
      const want = `Easter (Påske), ${short(addDays(e, -3))} – ${short(addDays(e, 1))}`;
      if (!text.includes(want)) throw new Error(`no "${want}" in "${text}"`);
    }
    if (listed.some(n => /Eve|Friday after|Constitution/.test(n))) throw new Error('a day that is not a public holiday is closed by Start next year: ' + text);
    await admin.fill('[data-testid=next-year-name]', 'Northlight next year');
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await projectRow(admin, 'Northlight next year').waitFor();
    await openFor(admin, 'Northlight next year');
    await admin.goto(B + '/data/semesters');
    await admin.waitForSelector('[data-testid=data-row]');
    const there = (await rows(admin)).filter(r => r.endsWith('· whole building'));
    eq(there.map(r => r.split(' — ')[0]), listed, 'next year\'s closures');
    await openFor(admin, THIS_YEAR);
  });

  for (const who of ['editor', 'coach']) {
    await step(`${who === 'editor' ? 'an editor' : 'a coach'} has no button and Ctrl+K finds nothing`, async () => {
      const p = await signIn(browser, who, { width: 1400, height: 900 });
      await p.goto(B + '/data/semesters');
      await p.waitForSelector('.empty-state');
      if (await p.locator('[data-testid=close-holidays]').count()) throw new Error('the button is there');
      await p.keyboard.press('Control+k');
      await p.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
      await p.keyboard.type('easter');
      await p.waitForSelector('.find-none', { timeout: 5000 })
        .catch(async () => { throw new Error('Find says ' + JSON.stringify(await p.locator('[data-testid=find-hit]').allInnerTexts())); });
      await p.context().close();
    });
  }

  await step('on a phone the form fits the screen', async () => {
    const p = await signIn(browser, 'admin', { width: 360, height: 780 });
    await openForm(p);
    await p.waitForFunction(() => document.querySelector('[data-testid=dialog]').getAnimations({ subtree: true }).every(a => a.playState === 'finished'));
    const box = await p.locator('[data-testid=dialog]').boundingBox();
    if (box.x < 0 || box.x + box.width > 360.5) throw new Error(`the form runs from ${box.x} to ${box.x + box.width}`);
    const over = await p.locator('[data-testid=holiday]').evaluateAll(es => es.filter(e => e.getBoundingClientRect().right > 360.5 || e.scrollWidth > e.clientWidth + 1).length);
    if (over) throw new Error(`${over} holidays run off the screen`);
    const ok = await p.locator('[data-testid=dialog-ok]').boundingBox();
    if (!ok || ok.y + ok.height > 780.5) throw new Error('OK is below the screen');
    await p.screenshot({ path: OUT + 'holidays-phone.png' });
  });

  log(failed ? `${failed} failed` : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
