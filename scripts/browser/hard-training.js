// How hard a group's day is on the body, on the sample's this week (any day of it): Plan ▸ Groups says each group's
// hard training beside its name, BA 1's Monday past both of the school's limits (09:00–12:00 is 2h 45m without a
// rest, and 4h 45m in the day), marked along the foot of its row; the side panel on Acrobatics says both, with no
// ring and no ways out, because nothing is double-booked; the week says each day's; Reports counts the three.
// Settings: a limit that is not a length is refused before saving, three hours in a row clears the mornings, no
// limits at all keeps the figures and warns of nothing, and Undo puts the limits back. Classes: Acrobatics made
// moderate takes BA 1 inside the limits. The add form warns of a hard class straight after BA 1's afternoon next
// week and saves it all the same. A coach reads the same; and it fits a phone.
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
const text = async (page, sel) => squash(await page.locator(sel).first().innerText());
const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t);
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };

const BA1_IN_A_ROW = 'BA 1: 2h 45m of hard training in a row, Mon 09:00–12:00 (at most 2h without a 30m rest) — "Strength and conditioning", "Acrobatics"';
const BA1_IN_A_DAY = 'BA 1: 4h 45m of hard training on Mon (at most 4h in a day)';

// The plan, Groups, one day of this week: the chip for it, Monday first (an empty weekend has none).
async function groupsDay(page, iso) {
  if (!page.url().endsWith('/plan')) { await page.goto(B + '/plan'); }
  await page.waitForSelector('[data-testid=timeline]');
  await page.click('[data-testid=lens-groups]');
  await page.click('[data-testid=scale-day]');
  await page.waitForSelector('[data-testid=day-chip]');
  const day = await page.getAttribute('[data-testid=timeline]', 'data-day');
  if (mondayOf(day) !== mondayOf(iso)) { await page.click('[data-testid=today]'); }
  const n = Math.round((new Date(iso + 'T12:00:00Z') - new Date(mondayOf(iso) + 'T12:00:00Z')) / 864e5);
  await page.locator('[data-testid=day-chip]').nth(n).click();
  await page.waitForSelector(`[data-testid=timeline][data-day="${iso}"] .tl-row:has(.tl-name:text-is("BA 1"))`);
}

// Each group row's figure as a person reads it: "BA 1 4h 45m hard (over)", or the name alone when it has none.
const sums = page => page.$$eval('[data-testid=timeline] .tl-row[data-row]', rows => rows.map(r => {
  const s = r.querySelector('[data-testid=hard-sum]');
  return r.querySelector('.tl-name').textContent.trim() + (s ? ' ' + s.textContent.trim() + (s.dataset.over === '1' ? ' (over)' : '') : '');
}));
// The plan drawing exactly these figures: the barrier after a change, and the check of it.
async function sumsAre(page, want) {
  await page.waitForFunction(w => {
    const got = [...document.querySelectorAll('[data-testid=timeline] .tl-row[data-row]')].map(r => {
      const s = r.querySelector('[data-testid=hard-sum]');
      return r.querySelector('.tl-name').textContent.trim() + (s ? ' ' + s.textContent.trim() + (s.dataset.over === '1' ? ' (over)' : '') : '');
    });
    return JSON.stringify(got) === JSON.stringify(w);
  }, want, { timeout: 8000 }).catch(async () => { throw new Error('the plan says ' + JSON.stringify(await sums(page)) + ', not ' + JSON.stringify(want)); });
}
// The marks along one group's row: hard and moderate classes, and the stretches past the limit, as minutes.
const marks = (page, group) => page.locator(`[data-testid=timeline] .tl-row:has(.tl-name:text-is("${group}"))`).evaluate(r => ({
  hard: r.querySelectorAll('[data-testid=effort-mark][data-effort=Hard]').length,
  moderate: r.querySelectorAll('[data-testid=effort-mark][data-effort=Moderate]').length,
  over: [...r.querySelectorAll('[data-testid=effort-over]')].map(x => `${x.dataset.from}–${x.dataset.until}`),
}));
// The week's figures by group and day, as "BA 1 Mon 4h 45m hard (over)".
const weekSums = page => page.$$eval('[data-testid=week-matrix] tbody tr[data-row]', rows => {
  const days = [...document.querySelectorAll('[data-testid=week-matrix] thead [data-testid=matrix-day] span')].map(s => s.textContent.trim());
  return rows.flatMap(r => [...r.querySelectorAll('td')].map((td, i) => {
    const s = td.querySelector('[data-testid=week-hard]');
    return s ? `${r.querySelector('th').textContent.trim()} ${days[i]} ${s.textContent.trim()}${s.dataset.over === '1' ? ' (over)' : ''}` : null;
  }).filter(Boolean));
});
const card = async page => {
  await page.goto(B + '/reports');
  await page.waitForSelector('[data-testid=students-short], .cards');
  return (await page.locator('[data-testid=hard-past-limits]').count()) ? (await page.locator('[data-testid=hard-past-limits]').textContent()).trim() : null;
};
// People & places ▸ Settings, the three hard-training boxes set and saved (each read when it is left, as typed).
async function limits(page, inARow, inADay, rest) {
  await page.goto(B + '/data/settings');
  await page.waitForSelector('[data-testid=settings-hard-row]');
  for (const [id, v] of [['settings-hard-row', inARow], ['settings-hard-day', inADay], ['settings-hard-rest', rest]]) {
    if (v === undefined) continue;
    await page.fill(`[data-testid=${id}]`, v);
    await page.press(`[data-testid=${id}]`, 'Tab');
  }
  await page.waitForSelector('[data-testid=settings-save]:not([disabled])');
  await said(page, () => page.click('[data-testid=settings-save]'), 'Changed the settings');
}
// Something done, and the toast it says, newer than any before it: two Undos of the same change say the same words.
async function said(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}
const undo = page => said(page, () => page.click('[data-testid=undo]'), 'Undid: ');

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today), wednesday = addDays(monday, 2), nextMonday = addDays(monday, 7);
  log('     today', today, '· this week starts', monday);

  await step('Plan ▸ Groups on Monday: each group\'s hard training beside its name, BA 1\'s past both limits and marked along its row', async () => {
    await groupsDay(admin, monday);
    eq(await sums(admin), ['BA 1 4h 45m hard (over)', 'BA 2 3h 30m hard', 'Prep 1h 30m hard'], 'the figures');
    const title = await admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=hard-sum]').getAttribute('title');
    eq(title, 'Hard training 09:00–12:00 (2h 45m), 13:00–15:00 (2h); 4h 45m in all. More than 2h in a row without a 30m rest. More than 4h in a day.', 'BA 1\'s longer look');
    // Strength, Acrobatics and Circus disciplines hard; 09:00–12:00 the stretch past the limit, its gap and all.
    eq(await marks(admin, 'BA 1'), { hard: 3, moderate: 0, over: ['540–720'] }, 'BA 1\'s marks');
    eq(await marks(admin, 'BA 2'), { hard: 2, moderate: 1, over: [] }, 'BA 2\'s marks');
    // The marks are drawn, not only in the page: the over mark spans 09:00 to 12:00 under the blocks.
    const box = await admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=effort-over]').boundingBox();
    const strength = await admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=block][data-title="Strength and conditioning"]').boundingBox();
    const acro = await admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=block][data-title=Acrobatics]').boundingBox();
    if (!box || box.height < 2 || Math.abs(box.x - strength.x) > 2 || Math.abs(box.x + box.width - (acro.x + acro.width)) > 4)
      throw new Error(`the over mark is at ${JSON.stringify(box)}, the morning from ${strength.x} to ${acro.x + acro.width}`);
    log('     ' + title);
    await admin.screenshot({ path: OUT + 'hard-day.png' });
  });

  await step('the side panel on Acrobatics says both beside the class, with no ring on it and no ways out', async () => {
    const acro = admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=block][data-title=Acrobatics]');
    await acro.click();
    await admin.waitForSelector('[data-testid=inspector] [data-testid=inspector-hard]');
    eq((await admin.locator('[data-testid=inspector-hard]').allInnerTexts()).map(squash), [BA1_IN_A_ROW, BA1_IN_A_DAY], 'the panel');
    if (await acro.evaluate(b => b.classList.contains('clash'))) throw new Error('Acrobatics is ringed');
    if (await admin.locator('[data-testid=inspector] [data-testid=ways-out]').count()) throw new Error('the panel offers ways out');
    // Prep's Acrobatics, inside the limits, says nothing of it.
    await admin.locator('.tl-row:has(.tl-name:text-is("Prep")) [data-testid=block][data-title=Acrobatics]').click();
    await admin.waitForFunction(() => /13:00–14:30/.test(document.querySelector('[data-testid=inspector] .insp-when')?.textContent ?? ''));
    if (await admin.locator('[data-testid=inspector-hard]').count()) throw new Error('Prep\'s Acrobatics is warned of: ' + await text(admin, '[data-testid=inspector-hard]'));
    await acro.click();
    await admin.waitForSelector('[data-testid=inspector-hard]');
    await admin.screenshot({ path: OUT + 'hard-panel.png' });
    await admin.click('[data-testid=inspector-close]');
  });

  await step('the week: each group\'s day says its hard training, BA 1\'s Monday and BA 2\'s Wednesday past the limits', async () => {
    await admin.click('[data-testid=scale-week]');
    await admin.waitForSelector('[data-testid=week-matrix] [data-testid=week-hard]');
    const all = await weekSums(admin);
    log('     ' + all.join(' · '));
    eq(all.filter(s => s.endsWith('(over)')), ['BA 1 Mon 4h 45m hard (over)', 'BA 2 Wed 2h 45m hard (over)'], 'past the limits');
    for (const want of ['BA 2 Mon 3h 30m hard', 'Prep Mon 1h 30m hard', 'BA 1 Wed 1h 30m hard'])
      if (!all.includes(want)) throw new Error('the week does not say ' + want);
    if (all.some(s => s.startsWith('BA 1 Thu'))) throw new Error('BA 1\'s Thursday, lunch alone, has a figure');
    await admin.screenshot({ path: OUT + 'hard-week.png' });
  });

  await step('Reports counts this week\'s three, beside the hours target', async () => {
    eq(await card(admin), '3', 'Hard training past the limits');
  });

  await step('Settings: a limit that is not a length is refused before saving; 3h in a row clears the mornings, the day stays past 4h', async () => {
    await admin.goto(B + '/data/settings');
    await admin.waitForSelector('[data-testid=settings-hard-row]');
    eq([await admin.inputValue('[data-testid=settings-hard-row]'), await admin.inputValue('[data-testid=settings-hard-day]'), await admin.inputValue('[data-testid=settings-hard-rest]')], ['2:00', '4:00', '0:30'], 'the boxes');
    await admin.fill('[data-testid=settings-hard-row]', 'lots');
    await admin.press('[data-testid=settings-hard-row]', 'Tab');
    await admin.waitForSelector('[data-testid=settings-problem]');
    eq(await text(admin, '[data-testid=settings-problem]'), 'Hard training in a row is not a length of time (2, 1:30 or 90m).', 'the problem');
    if (!(await admin.locator('[data-testid=settings-save]').isDisabled())) throw new Error('Save is offered');
    await admin.screenshot({ path: OUT + 'hard-settings.png' });
    await limits(admin, '3', undefined, '30m');
    await groupsDay(admin, monday);
    await sumsAre(admin, ['BA 1 4h 45m hard (over)', 'BA 2 3h 30m hard', 'Prep 1h 30m hard']);
    eq((await marks(admin, 'BA 1')).over, [], 'BA 1\'s stretches past the limit');
    const title = await admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=hard-sum]').getAttribute('title');
    eq(title, 'Hard training 09:00–12:00 (2h 45m), 13:00–15:00 (2h); 4h 45m in all. More than 4h in a day.', 'BA 1\'s longer look');
    eq(await card(admin), '1', 'Hard training past the limits');
  });

  await step('Settings: no limits at all, the figures stay and nothing warns, the card goes; two Undos put the limits back', async () => {
    await limits(admin, '', '');
    await groupsDay(admin, monday);
    await sumsAre(admin, ['BA 1 4h 45m hard', 'BA 2 3h 30m hard', 'Prep 1h 30m hard']);
    await admin.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=block][data-title=Acrobatics]').click();
    await admin.waitForSelector('[data-testid=inspector]');
    if (await admin.locator('[data-testid=inspector-hard]').count()) throw new Error('the panel still warns');
    await admin.click('[data-testid=inspector-close]');
    eq(await card(admin), null, 'the card');
    await undo(admin);
    await undo(admin);
    await admin.goto(B + '/data/settings');
    await admin.waitForSelector('[data-testid=settings-hard-row]');
    eq([await admin.inputValue('[data-testid=settings-hard-row]'), await admin.inputValue('[data-testid=settings-hard-day]')], ['2:00', '4:00'], 'the limits after Undo');
    eq(await card(admin), '3', 'Hard training past the limits');
  });

  await step('Classes: Acrobatics made moderate takes BA 1 inside the limits and leaves Prep none; Undo puts it back', async () => {
    await admin.goto(B + '/data/classes');
    const row = admin.locator('[data-testid=data-row]:has(.name:text-is("Acrobatics"))');
    eq(squash(await row.locator('small').innerText()), 'Counted · Technique · Hard · needs Crash mats', 'its row');
    await row.click();
    await titled(admin, 'Edit Acrobatics');
    eq(await admin.locator('[data-testid=class-effort]').evaluate(s => s.options[s.selectedIndex].text), 'Hard', 'the form');
    await admin.selectOption('[data-testid=class-effort]', { label: 'Moderate' });
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForFunction(() => [...document.querySelectorAll('[data-testid=data-row]')].find(r => r.querySelector('.name').textContent.trim() === 'Acrobatics')?.querySelector('small').textContent.trim() === 'Counted · Technique · Moderate · needs Crash mats');
    await groupsDay(admin, monday);
    await sumsAre(admin, ['BA 1 3h 30m hard', 'BA 2 3h 30m hard', 'Prep']);
    eq(await marks(admin, 'Prep'), { hard: 0, moderate: 2, over: [] }, 'Prep\'s marks');
    await undo(admin);
    await sumsAre(admin, ['BA 1 4h 45m hard (over)', 'BA 2 3h 30m hard', 'Prep 1h 30m hard']);
  });

  await step('adding Aerial straight after BA 1\'s afternoon next Monday warns of both limits, saves all the same, and Undo takes it back', async () => {
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=timeline]');
    await admin.click('[data-testid=add-session]');
    await titled(admin, 'Add a class');
    await admin.selectOption('[data-testid=session-class]', { label: 'Aerial' });
    await admin.fill('[data-testid=session-date]', nextMonday); await admin.press('[data-testid=session-date]', 'Tab');
    await admin.fill('[data-testid=session-start]', '15:00'); await admin.press('[data-testid=session-start]', 'Tab');
    await admin.fill('[data-testid=session-end]', '16:00'); await admin.press('[data-testid=session-end]', 'Tab');
    await admin.selectOption('[data-testid=session-for]', { label: 'A group' }).catch(() => {});
    await admin.selectOption('[data-testid=session-group]', { label: 'BA 1' });
    await admin.waitForFunction(() => /5h 45m/.test(document.querySelector('[data-testid=session-warnings]')?.textContent ?? ''));
    const warned = (await admin.locator('[data-testid=session-warnings] li').allInnerTexts()).map(squash);
    eq(warned, [
      'BA 1: 3h of hard training in a row, Mon 13:00–16:00 (at most 2h without a 30m rest) — "Circus disciplines", "Aerial"',
      'BA 1: 5h 45m of hard training on Mon (at most 4h in a day)',
    ], 'the form warns');
    if (await admin.locator('[data-testid=dialog-ok]').isDisabled()) throw new Error('the form refuses');
    await admin.screenshot({ path: OUT + 'hard-add.png' });
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await groupsDay(admin, addDays(monday, 4));      // this Friday, then a step on over the weekend
    await admin.click('[data-testid=day-next]');
    await admin.waitForSelector(`[data-testid=timeline][data-day="${nextMonday}"]`);
    await sumsAre(admin, ['BA 1 5h 45m hard (over)', 'BA 2 3h 30m hard', 'Prep 1h 30m hard']);
    eq((await marks(admin, 'BA 1')).over, ['540–720', '780–960'], 'BA 1\'s stretches past the limit');
    await undo(admin);
    await sumsAre(admin, ['BA 1 4h 45m hard (over)', 'BA 2 3h 30m hard', 'Prep 1h 30m hard']);
  });

  await step('a coach reads the same on the plan and the side panel, and is offered no settings', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await groupsDay(coach, monday);
    eq(await sums(coach), ['BA 1 4h 45m hard (over)', 'BA 2 3h 30m hard', 'Prep 1h 30m hard'], 'the figures');
    await coach.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=block][data-title=Acrobatics]').click();
    await coach.waitForSelector('[data-testid=inspector-hard]');
    eq((await coach.locator('[data-testid=inspector-hard]').allInnerTexts()).map(squash), [BA1_IN_A_ROW, BA1_IN_A_DAY], 'the panel');
    await coach.goto(B + '/data/settings');
    await coach.waitForSelector('text=Only an administrator can change');
    if (await coach.locator('[data-testid=settings-hard-row]').count()) throw new Error('the coach is offered the limits');
    await coach.context().close();
  });

  await step('a phone: the marks along each group\'s row and the week\'s figures, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await groupsDay(phone, monday);
    const over = await phone.locator('.tl-row:has(.tl-name:text-is("BA 1")) [data-testid=effort-over]').boundingBox();
    if (!over || over.width < 20 || over.height < 2) throw new Error('the over mark is ' + JSON.stringify(over));
    // Each group's figure under its name, inside the label, where "free" beside a space's name is left off.
    for (const g of ['BA 1', 'BA 2', 'Prep']) {
      const row = phone.locator(`.tl-row:has(.tl-name:text-is("${g}"))`);
      const [sum, name, label] = [await row.locator('[data-testid=hard-sum]').boundingBox(), await row.locator('.tl-name').boundingBox(), await row.locator('.tl-label').boundingBox()];
      if (!sum || sum.width < 20) throw new Error(`${g}'s figure is not drawn: ${JSON.stringify(sum)}`);
      if (sum.y < name.y + name.height - 1 || sum.x + sum.width > label.x + label.width + 1 || sum.y + sum.height > label.y + label.height + 1)
        throw new Error(`${g}'s figure ${JSON.stringify(sum)} is not under its name ${JSON.stringify(name)} inside ${JSON.stringify(label)}`);
    }
    await groupsDay(phone, monday);
    await phone.click('[data-testid=lens-spaces]');
    await phone.waitForSelector('[data-testid=timeline] .tl-row.free');
    if (await phone.locator('[data-testid=timeline] .tl-row.free .tl-label small').first().isVisible()) throw new Error('"free" is beside a space\'s name on a phone');
    await phone.click('[data-testid=lens-groups]');
    await phone.waitForSelector('[data-testid=timeline] [data-testid=hard-sum]');
    const fit = await phone.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (fit > 1) throw new Error(`the page scrolls ${fit}px sideways`);
    await phone.screenshot({ path: OUT + 'hard-phone-day.png' });
    await phone.click('[data-testid=scale-week]');
    await phone.waitForSelector('[data-testid=week-matrix] [data-testid=week-hard]');
    eq((await weekSums(phone)).filter(s => s.endsWith('(over)')), ['BA 1 Mon 4h 45m hard (over)', 'BA 2 Wed 2h 45m hard (over)'], 'past the limits');
    await phone.screenshot({ path: OUT + 'hard-phone-week.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
