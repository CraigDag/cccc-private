// The term at a glance: Plan ▸ Term lays the semester's weeks side by side, this week marked, the weeks before
// the timetable began blank and the autumn break closed. A week's classes are the week's own count, and its
// issues the week's own, opening the week with them listed. What is different from the week before is listed
// and opens that class on its day; a change is counted, a cancellation is not, and a copied week is the same.
// Fill… on the blank week ahead copies the week before it. The Groups rows say who is short of the target, a
// guest group hidden until asked; the People rows are hours for an administrator and class counts for a coach,
// their own row their hours; a cell opens that thing's week. Ctrl+K and the arrows reach it, and it fits a
// phone, opening on this week. Best on a preview started with AT=10:15.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }

async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport, reducedMotion: 'reduce' })).newPage();
  page.setDefaultTimeout(15000);
  page.on('pageerror', e => log('     page error (' + who + '): ' + e.message));
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
const isoWeek = iso => {
  const d = new Date(iso + 'T12:00:00Z');
  d.setUTCDate(d.getUTCDate() + 3 - ((d.getUTCDay() + 6) % 7));
  const jan4 = new Date(Date.UTC(d.getUTCFullYear(), 0, 4));
  return 1 + Math.round(((d - jan4) / 864e5 - 3 + ((jan4.getUTCDay() + 6) % 7)) / 7);
};
const hm = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
// "16h 30m" as minutes, the way the app writes a length.
const length = s => { const m = /^(?:(\d+)h)? ?(?:(\d+)m)?$/.exec(s); return m && s ? (+(m[1] || 0)) * 60 + (+(m[2] || 0)) : NaN; };
const minutes = s => +s.slice(0, 2) * 60 + +s.slice(3, 5);

// One week's figure on a row of the term, as a person reads it.
const cell = (page, row, monday) => page.locator(`[data-testid=${row}] td[data-monday="${monday}"]`);
const figure = async (page, row, monday) => squash(await cell(page, row, monday).innerText());
// A row of the lens by its name, and its figure for a week.
const lensRow = (page, name) => page.locator('[data-testid=term-row]', { has: page.locator('.term-label', { hasText: new RegExp('^' + name + '$') }) });
const lensCell = (page, name, monday) => lensRow(page, name).locator(`td[data-monday="${monday}"] [data-testid=term-cell]`);
const labels = page => page.$$eval('[data-testid=term-row] .term-label', ls => ls.map(l => l.textContent.trim()));

// To the term from anywhere on the plan; the grid is only ever drawn by the term, so it is the new render.
async function toTerm(page) {
  await page.click('[data-testid=scale-term]');
  await page.waitForSelector('[data-testid=plan-page][data-scale=Term] [data-testid=term-grid]');
}
async function lens(page, which, words) {
  await page.click(`[data-testid=lens-${which.toLowerCase()}]`);
  await page.waitForFunction(([l, w]) => document.querySelector(`[data-testid=plan-page][data-lens=${l}]`)
    && document.querySelector('[data-testid=term-measure]')?.textContent.includes(w), [which, words]);
}
// A week's one day on the plan, as a person gets there from the term: the week's column, then its day.
async function toDay(page, day) {
  await page.click(`[data-testid=term-week][data-monday="${mondayOf(day)}"]`);
  await page.waitForSelector(`[data-testid=plan-page][data-scale=Week]`);
  const n = (new Date(day + 'T12:00:00Z').getUTCDay() + 6) % 7;
  await page.locator('[data-testid=day-chip]').nth(n).click();
  await page.waitForSelector(`[data-testid=plan-page][data-scale=Day][data-day="${day}"] [data-testid=timeline]`);
}
async function closeDialog(page) {
  await page.click('[data-testid=dialog-close]');
  await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
}
const changesListed = page => page.$$eval('[data-testid=week-change]', rows => rows.map(r => ({
  kind: r.dataset.kind, what: r.querySelector('.wc-what b').textContent.trim(),
  when: r.querySelector('.wc-when').textContent.replace(/\s+/g, ' ').trim(),
  words: (r.querySelector('.wc-words')?.textContent || '').trim(),
})));

(async () => {
  const browser = await chromium.launch();
  const ad = await signIn(browser, 'admin', { width: 1366, height: 900 });
  await ad.goto(B + '/plan');
  await ad.waitForSelector('[data-testid=timeline]');
  const today = await ad.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  const wk = n => addDays(monday, 7 * n);   // the sample's weeks, counted from this one
  log('     today', today, '· week', isoWeek(monday));
  let weekClasses = -1, adminEvents = -1, changed = null;

  await step('the week as the plan draws it: its classes, day by day', async () => {
    const counts = await ad.$$eval('[data-testid=day-chip] .dc-count', cs => cs.map(c => c.textContent.trim()));
    if (counts.length < 5) throw new Error('the strip has ' + counts.length + ' days');
    weekClasses = counts.reduce((n, c) => n + (c === '–' ? 0 : +c), 0);
    log('     this week by its days:', counts.join(' '), '=', weekClasses);
    if (!(weekClasses > 10)) throw new Error('the week has ' + weekClasses + ' classes');
  });

  await step('Term lays out the autumn semester: 18 weeks, this one marked, the weeks before the timetable began blank, the autumn break closed, the next blank week offered to fill', async () => {
    await toTerm(ad);
    if (await text(ad, '[data-testid=plan-title]') !== 'Autumn semester') throw new Error('the title is ' + await text(ad, '[data-testid=plan-title]'));
    if (!(await text(ad, '[data-testid=term-range]')).includes('18 weeks side by side')) throw new Error('it says ' + await text(ad, '[data-testid=term-range]'));
    const weeks = await ad.$$eval('[data-testid=term-week]', bs => bs.map(b => b.dataset.monday));
    if (weeks.length !== 18 || weeks[0] !== wk(-4)) throw new Error('the weeks are ' + weeks[0] + ' … (' + weeks.length + ')');
    const now = await ad.$$eval('thead th.now [data-testid=term-week]', bs => bs.map(b => b.dataset.monday));
    if (now.length !== 1 || now[0] !== monday) throw new Error('marked as this week: ' + JSON.stringify(now));
    for (const n of [-4, -3]) {
      if (await figure(ad, 'term-classes', wk(n)) !== 'Blank') throw new Error('week ' + n + ' reads ' + await figure(ad, 'term-classes', wk(n)));
      if (await cell(ad, 'term-classes', wk(n)).locator('[data-testid=term-fill]').count()) throw new Error('a week that has ended is offered to fill');
    }
    const shut = await figure(ad, 'term-classes', wk(3));
    if (!shut.startsWith('Closed') || !shut.includes('Autumn break')) throw new Error('the autumn break reads ' + shut);
    const mine = await figure(ad, 'term-classes', monday);
    if (mine !== String(weekClasses)) throw new Error('this week reads ' + mine + ' classes; its days add up to ' + weekClasses);
    // The total beside the weeks is the weeks added up.
    const each = (await ad.locator('[data-testid=term-classes] td[data-monday] .term-n').allInnerTexts()).map(x => parseInt(x, 10));
    const total = squash(await ad.locator('[data-testid=term-classes] [data-testid=term-total]').innerText());
    if (each.length !== 4 || String(each.reduce((a, b) => a + b, 0)) !== total) throw new Error('the weeks read ' + each.join(' + ') + ', the total ' + total);
    const next = ad.locator('[data-testid=term-fill].next');
    if (await next.count() !== 1) throw new Error((await next.count()) + ' weeks are the next to fill');
    if (await next.evaluate(b => b.closest('td').dataset.monday) !== wk(2)) throw new Error('the next to fill is not the blank week after next');
    const diff = [];
    for (const n of [-2, -1, 0, 1]) diff.push(await figure(ad, 'term-changes', wk(n)));
    log('     different:', diff.join(' | '));
    if (JSON.stringify(diff) !== JSON.stringify(['', 'Same', '2', '2'])) throw new Error('what is different reads ' + JSON.stringify(diff));
    adminEvents = +(await figure(ad, 'term-events', monday));
    await ad.screenshot({ path: OUT + 'term-laptop.png' });
  });

  await step('Groups: each group\'s counted hours a week, Prep short of the target in orange with six students short, the guest group hidden until asked', async () => {
    await lens(ad, 'Groups', 'group');
    const rows = await labels(ad);
    if (JSON.stringify(rows) !== JSON.stringify(['BA 1', 'BA 2', 'Prep'])) throw new Error('the rows are ' + JSON.stringify(rows));
    for (const n of [-2, -1, 0, 1]) {
      const prep = lensCell(ad, 'Prep', wk(n)), ba = lensCell(ad, 'BA 1', wk(n));
      if (await prep.getAttribute('data-warn') !== '1' || !squash(await prep.innerText()).includes('6 short')) throw new Error('Prep in week ' + n + ' reads ' + squash(await prep.innerText()));
      if (await ba.getAttribute('data-warn') !== '0') throw new Error('BA 1 in week ' + n + ' is marked short: ' + squash(await ba.innerText()));
    }
    log('     Prep this week:', squash(await lensCell(ad, 'Prep', monday).innerText()), '· BA 1:', squash(await lensCell(ad, 'BA 1', monday).innerText()));
    if (await text(ad, '[data-testid=guests-hidden]') !== '1 guest group hidden') throw new Error('the bar says ' + await text(ad, '[data-testid=guests-hidden]'));
    await ad.click('[data-testid=guests-hidden]');
    await lensRow(ad, 'Nordlys Troupe').waitFor();
    await ad.click('[data-testid=more-menu]');
    await ad.click('[data-testid=toggle-guests]');
    await lensRow(ad, 'Nordlys Troupe').waitFor({ state: 'detached' });
    await ad.waitForSelector('[data-testid=guests-hidden]');
  });

  await step('a row\'s figure opens that group\'s week, narrowed to it', async () => {
    await lensCell(ad, 'Prep', monday).click();
    await ad.waitForSelector(`[data-testid=plan-page][data-scale=Day][data-day="${today}"] [data-testid=focus-chip]`);
    if (!(await text(ad, '[data-testid=focus-chip]')).startsWith('Prep')) throw new Error('narrowed to ' + await text(ad, '[data-testid=focus-chip]'));
    if (await text(ad, '[data-testid=plan-title]') !== 'Prep’s week') throw new Error('the title is ' + await text(ad, '[data-testid=plan-title]'));
    await ad.click('[data-testid=focus-chip] button');
    await ad.waitForSelector('[data-testid=focus-chip]', { state: 'detached' });
  });

  await step('what is different: the week before the same, this week\'s two new guest workshops; one listed opens that class on its day, its guest group shown', async () => {
    await toTerm(ad);
    await cell(ad, 'term-changes', wk(-1)).locator('[data-testid=term-change-count]').click();
    await ad.waitForSelector('[data-testid=week-changes-none]');
    const from = await text(ad, '[data-testid=week-changes-from]');
    if (!from.includes('against week ' + isoWeek(wk(-2)))) throw new Error('it compares ' + from);
    await closeDialog(ad);
    await cell(ad, 'term-changes', monday).locator('[data-testid=term-change-count]').click();
    await ad.waitForSelector('[data-testid=week-change]');
    const listed = await changesListed(ad);
    log('     ' + listed.map(c => c.kind + ' ' + c.what + ' ' + c.when).join(' | '));
    if (listed.length !== 2 || listed.some(c => c.kind !== 'Added' || c.what !== 'Guest workshop')) throw new Error('listed ' + JSON.stringify(listed));
    await ad.screenshot({ path: OUT + 'term-changes.png' });
    await ad.locator('[data-testid=week-change]').first().click();
    await ad.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ad.waitForSelector(`[data-testid=plan-page][data-scale=Day][data-day="${addDays(monday, 3)}"]`);
    await ad.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Guest workshop');
    if (!await ad.locator('[data-testid=timeline] [data-testid=block][data-title="Guest workshop"]').count()) throw new Error('the class is not drawn on its day');
  });

  await step('issues: this week\'s figure is the week\'s own count, and opens the week with them listed', async () => {
    await toTerm(ad);
    const btn = cell(ad, 'term-issues', monday).locator('[data-testid=term-issue-count]');
    const n = await btn.count() ? +squash(await btn.innerText()) : 0;
    if (n === 0) throw new Error('the sample\'s week has no issues to open');
    await btn.click();
    await ad.waitForSelector(`[data-testid=plan-page][data-scale=Week][data-day="${today}"] [data-testid=issues]`);
    const said = await text(ad, '[data-testid=issues-button]'), listed = await ad.locator('[data-testid=issue]').count();
    log('     the term says', n, '· the week says', said, '· listed', listed);
    if (said !== (n === 1 ? '1 issue' : n + ' issues') || listed !== n) throw new Error('the term says ' + n + ', the week ' + said + ' with ' + listed + ' listed');
  });

  await step('People: an administrator reads each person\'s hours, and Ada\'s this week is the figure her own week gives', async () => {
    await toTerm(ad);
    await lens(ad, 'People', 'hours');
    const ada = squash(await lensCell(ad, 'Ada Brightwater', monday).innerText());
    if (!/^(\d+h( \d+m)?|\d+m)$/.test(ada)) throw new Error('Ada this week reads ' + ada);
    // Her semester beside her weeks is her weeks added up.
    const weeks = (await lensRow(ad, 'Ada Brightwater').locator('td[data-monday] [data-testid=term-cell]').allInnerTexts()).map(squash);
    const total = squash(await lensRow(ad, 'Ada Brightwater').locator('[data-testid=term-total]').innerText());
    log('     Ada\'s weeks:', weeks.join(' + '), '= total', total);
    if (weeks.length !== 4 || weeks.reduce((n, w) => n + length(w), 0) !== length(total)) throw new Error('her weeks read ' + weeks.join(' + ') + ', her total ' + total);
    await lensCell(ad, 'Ada Brightwater', monday).click();
    await ad.waitForSelector('[data-testid=focus-hours]');
    const own = await text(ad, '[data-testid=focus-hours]');
    log('     the term says', ada, '· her week says', own);
    if (own !== ada + ' this week') throw new Error('the term says ' + ada + ', her week ' + own);
    await ad.click('[data-testid=focus-chip] button');
    await ad.waitForSelector('[data-testid=focus-chip]', { state: 'detached' });
  });

  await step('a week changed is counted and listed with what changed', async () => {
    await toTerm(ad);
    await toDay(ad, wk(1));
    const block = ad.locator('[data-testid=timeline] [data-testid=block]').first();
    const title = await block.getAttribute('data-title');
    await block.click();
    await ad.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, title);
    await ad.click('[data-testid=inspector-edit]');
    await ad.waitForSelector('[data-testid=session-start]');
    const start = await ad.inputValue('[data-testid=session-start]'), end = await ad.inputValue('[data-testid=session-end]');
    await ad.fill('[data-testid=session-end]', hm(minutes(end) + 15));
    await ad.fill('[data-testid=session-start]', hm(minutes(start) + 15));
    await ad.click('[data-testid=dialog-ok]');
    await ad.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ad.waitForFunction(w => document.querySelector('.insp-when')?.textContent.includes(w), hm(minutes(start) + 15) + '–' + hm(minutes(end) + 15));
    changed = { title, was: start + '–' + end };
    await toTerm(ad);
    const diff = await figure(ad, 'term-changes', wk(1));
    if (diff !== '3') throw new Error('next week reads ' + diff + ' different, not 3');
    await cell(ad, 'term-changes', wk(1)).locator('[data-testid=term-change-count]').click();
    await ad.waitForSelector('[data-testid=week-change]');
    const listed = await changesListed(ad);
    log('     ' + listed.map(c => c.kind + ' ' + c.what + (c.words ? ' (' + c.words + ')' : '')).join(' | '));
    const mine = listed.filter(c => c.kind === 'Changed');
    if (listed.length !== 3 || listed.filter(c => c.kind === 'Gone' && c.what === 'Guest workshop').length !== 2) throw new Error('listed ' + JSON.stringify(listed));
    if (mine.length !== 1 || mine[0].what !== title || mine[0].words !== 'Was ' + changed.was) throw new Error('the change reads ' + JSON.stringify(mine));
    await closeDialog(ad);
  });

  await step('Fill… on the blank week ahead copies the week before it, already ticked, and the copy is the same', async () => {
    const before = await figure(ad, 'term-classes', wk(1));
    await ad.click('[data-testid=term-fill].next');
    await ad.waitForSelector('[data-testid=copy-weeks]');
    const title = await text(ad, '[data-testid=dialog] #dialog-title');
    if (!title.startsWith('Copy Week ' + isoWeek(wk(1)) + ',')) throw new Error('the form is ' + title);
    const ticked = await ad.$$eval('[data-testid=copy-weeks] input:checked', is => is.map(i => i.closest('label').textContent.replace(/\s+/g, ' ').trim()));
    if (ticked.length !== 1 || !ticked[0].startsWith('Week ' + isoWeek(wk(2)) + ',')) throw new Error('ticked: ' + JSON.stringify(ticked));
    if (await text(ad, '[data-testid=dialog-ok]') !== 'Copy onto 1 week') throw new Error('the button says ' + await text(ad, '[data-testid=dialog-ok]'));
    await ad.click('[data-testid=dialog-ok]');
    await ad.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ad.waitForFunction(m => !document.querySelector(`[data-testid=term-classes] td[data-monday="${m}"] [data-testid=term-fill]`), wk(2));
    const filled = await figure(ad, 'term-classes', wk(2)), same = await figure(ad, 'term-changes', wk(2));
    log('     week after next:', filled, 'classes,', same);
    if (filled !== before || same !== 'Same') throw new Error('the copy reads ' + filled + ' classes and ' + same + '; the week copied has ' + before);
    // The next blank week to build is after the break, filled from the week just built.
    const next = ad.locator('[data-testid=term-fill].next');
    if (await next.evaluate(b => b.closest('td').dataset.monday) !== wk(4)) throw new Error('the next to fill is not the week after the break');
    if (!(await next.getAttribute('title')).includes('Copy week ' + isoWeek(wk(2)) + ' onto it')) throw new Error('it would copy ' + await next.getAttribute('title'));
  });

  await step('a class cancelled is the week happening, not its timetable changing: one class fewer, and nothing more different', async () => {
    const classes = +(await figure(ad, 'term-classes', wk(1))), diff = await figure(ad, 'term-changes', wk(1));
    await toDay(ad, wk(1));
    await ad.locator(`[data-testid=timeline] [data-testid=block][data-title="${changed.title}"]`).first().click();
    await ad.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, changed.title);
    await ad.click('[data-testid=inspector-cancel]');
    await ad.waitForSelector('[data-testid=dialog-ok]');
    await ad.click('[data-testid=dialog-ok]');
    await ad.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ad.waitForFunction(() => document.querySelector('[data-testid=inspector-cancel]')?.textContent.trim() === 'Restore');
    await toTerm(ad);
    const after = +(await figure(ad, 'term-classes', wk(1))), diffAfter = await figure(ad, 'term-changes', wk(1));
    log('     next week:', classes, '→', after, 'classes;', diff, '→', diffAfter, 'different');
    if (after !== classes - 1 || diffAfter !== diff) throw new Error('next week reads ' + after + ' classes and ' + diffAfter + ' different');
  });

  await step('Ctrl+K opens the whole semester from anywhere; the arrows step to spring, and This semester comes back', async () => {
    await ad.goto(B + '/');
    await ad.waitForSelector('[data-testid=now-page]');
    await ad.keyboard.press('Control+k');
    await ad.waitForSelector('[data-testid=find]');
    await ad.keyboard.type('term');
    await ad.waitForFunction(() => [...document.querySelectorAll('[data-testid=find-hit] .find-label')].some(l => l.textContent === 'Show the whole semester'));
    await ad.locator('[data-testid=find-hit]', { hasText: 'Show the whole semester' }).click();
    await ad.waitForSelector('[data-testid=plan-page][data-scale=Term] [data-testid=term-grid]');
    if (await text(ad, '[data-testid=plan-title]') !== 'Autumn semester') throw new Error('it opened ' + await text(ad, '[data-testid=plan-title]'));
    await ad.click('[data-testid=day-next]');
    await ad.waitForFunction(() => document.querySelector('[data-testid=plan-title]')?.textContent === 'Spring semester');
    const weeks = await ad.locator('[data-testid=term-week]').count();
    if (weeks !== 20) throw new Error('spring lays out ' + weeks + ' weeks');
    await ad.click('[data-testid=today]');
    await ad.waitForFunction(() => document.querySelector('[data-testid=plan-title]')?.textContent === 'Autumn semester');
    if (await ad.locator('[data-testid=today]').count()) throw new Error('This semester is still offered on this semester');
  });

  await step('a class added from the term opens on its day, chosen, so it is plain where it went', async () => {
    await ad.click('[data-testid=add-session]');
    await ad.waitForSelector('[data-testid=session-title]');
    await ad.fill('[data-testid=session-title]', 'Rigging check');
    await ad.press('[data-testid=session-title]', 'Tab');
    await ad.selectOption('[data-testid=session-group]', { label: 'BA 2' });
    const date = await ad.inputValue('[data-testid=session-date]');
    await ad.click('[data-testid=dialog-ok]');
    await ad.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ad.waitForSelector(`[data-testid=plan-page][data-scale=Day][data-day="${date}"]`);
    await ad.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Rigging check');
  });

  await step('a coach: class counts for everybody else, their own hours on their own row, nothing to fill, and no administrators\' event', async () => {
    const coach = await signIn(browser, 'coach', { width: 1366, height: 900 });
    await coach.goto(B + '/plan');
    await coach.waitForSelector('[data-testid=timeline]');
    await toTerm(coach);
    await lens(coach, 'People', 'classes');
    const said = await text(coach, '[data-testid=term-measure]');
    if (said !== 'How many classes each person is in each week; your own row, your hours.') throw new Error('it says ' + said);
    const own = squash(await lensCell(coach, 'Ada Brightwater', monday).innerText());
    const other = squash(await lensCell(coach, 'Bo Lindqvist', monday).innerText());
    log('     Ada (their own):', own, '· Bo:', other);
    if (!/^(\d+h( \d+m)?|\d+m)$/.test(own) || !/^\d+$/.test(other)) throw new Error('Ada reads ' + own + ', Bo ' + other);
    if (await coach.locator('[data-testid=term-fill]').count()) throw new Error('a coach is offered to fill a week');
    if (!await coach.locator('[data-testid=term-blank]').count()) throw new Error('no blank week says so');
    const events = +(await figure(coach, 'term-events', monday));
    log('     events this week: the coach', events, '· the administrator', adminEvents);
    if (events !== adminEvents - 1) throw new Error('the coach reads ' + events + ' events, the administrator ' + adminEvents);
    await coach.context().close();
  });

  await step('a phone: the term scrolls inside its frame and opens on this week', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await phone.goto(B + '/plan');
    await phone.waitForSelector('[data-testid=timeline]');
    await toTerm(phone);
    await phone.waitForFunction(() => document.querySelector('.term-wrap').scrollLeft > 0);
    const fit = await phone.evaluate(() => {
      const wrap = document.querySelector('.term-wrap').getBoundingClientRect();
      const corner = document.querySelector('.term-corner').getBoundingClientRect();
      const now = document.querySelector('thead th.now').getBoundingClientRect();
      return { page: document.documentElement.scrollWidth, width: innerWidth, nowLeft: now.left, nowRight: now.right, from: corner.right, to: wrap.right };
    });
    log('     ' + JSON.stringify(fit));
    if (fit.page > fit.width) throw new Error('the page scrolls sideways: ' + fit.page + ' > ' + fit.width);
    if (fit.nowLeft < fit.from - 1 || fit.nowRight > fit.to + 1) throw new Error('this week is out of sight');
    await phone.screenshot({ path: OUT + 'term-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
