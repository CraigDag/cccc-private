// Start next year: Admin ▸ Projects ▸ Start next year… on the made-up school lists what carries over and
// what stays with this year, every semester starting in the same week number; making it puts a new
// timetable beside this one, opening on this year's first timetabled week with every later week blank,
// and this year's is left as it was. A timetable with no semesters says it has no year to roll forward.
// Against a fresh preview (made-up data).
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
(async () => {
  const browser = await chromium.launch();
  const errors = [];
  let failed = 0;
  let page;
  const step = async (name, fn) => { try { await fn(); log('OK  ', name); } catch (e) { failed++; log('FAIL', name, e.message.split('\n')[0]); await page.screenshot({ path: `${OUT}fail-${name.replace(/\W+/g,'-')}.png` }); } };
  const signIn = async (who, viewport) => {
    const p = await (await browser.newContext({ viewport })).newPage();
    p.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    p.on('pageerror', e => errors.push(e.message));
    await p.goto(B + '/signin');
    await p.fill('[data-testid=signin-login]', who);
    await p.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
    await p.click('[data-testid=signin-submit]');
    await p.waitForSelector('[data-testid=now-page]');
    return p;
  };
  const projectRow = (p, name) => p.locator('[data-testid=projects] > li').filter({ has: p.locator('b', { hasText: new RegExp('^' + name.replace(/[()]/g, '\\$&') + '$') }) });
  const openFor = async (p, name) => {
    await p.goto(B + '/admin/projects');
    const row = projectRow(p, name);
    await row.locator('[data-testid=project-open]').click();
    await p.waitForFunction(n => [...document.querySelectorAll('[data-testid=projects] > li')].some(li => li.querySelector('b')?.textContent === n && li.textContent.includes('Open for everybody') && !li.querySelector('[data-testid=project-open]')), name);
  };
  // The classes of a week, counted off the plan's week view by space: one block a class.
  const classesInWeekOf = async (p, date) => {
    await p.goto(B + '/plan');
    await p.waitForSelector('[data-testid=plan-page][data-lens=Spaces]');
    await p.click('[data-testid=scale-week]');
    await p.waitForSelector('[data-testid=plan-page][data-scale=Week]');
    await p.fill('[data-testid=day-date]', date);
    await p.waitForSelector(`[data-testid=plan-page][data-day="${date}"] [data-testid=week-matrix]`);
    return p.locator('[data-testid=week-matrix] [data-testid=block]').count();
  };
  const THIS_YEAR = 'Northlight 2026–27 (sample)';
  let carried, firstWeek, landsOn, landsDay;

  page = await signIn('admin', { width: 1400, height: 900 });

  await step('the form says what carries over and what stays, every semester in the same week number', async () => {
    await page.goto(B + '/admin/projects');
    await projectRow(page, THIS_YEAR).locator('[data-testid=project-next-year]').click();
    await page.waitForSelector('[data-testid=next-year-semesters]');
    const sems = await page.locator('[data-testid=next-year-semester]').evaluateAll(es => es.map(e => [e.dataset.from, e.dataset.to, +e.dataset.days]));
    log('      semesters (week from → to, days):', sems.map(s => s.join(' → ')).join(', '));
    if (sems.length !== 2 || sems.some(([a, b, d]) => a !== b || (d !== 364 && d !== 371))) throw new Error('the semesters move to ' + JSON.stringify(sems));
    // A semester that moves 53 weeks says why, and only then: which year that is depends on today's date.
    const said = await page.locator('[data-testid=next-year-week-53]').count();
    if (said !== sems.filter(s => s[2] === 371).length) throw new Error(`${said} semesters say they move 53 weeks, ${sems.filter(s => s[2] === 371).length} do`);
    const name = await page.inputValue('[data-testid=next-year-name]');
    if (name !== 'Northlight 2027–28 (sample)') throw new Error('the name offered is ' + name);
    const first = (await page.textContent('[data-testid=next-year-first-week]')).trim();
    log('     ', first);
    const m = /^The classes of week (\d+) .*?: (\d+) classes, onto week (\d+) of next year \((\d+)–\d+ (\w+) (\d{4})\)/.exec(first);
    if (!m) throw new Error('the first week says ' + first);
    [firstWeek, carried, landsOn, landsDay] = [+m[1], +m[2], +m[3], `${m[4]} ${m[5]} ${m[6]}`];
    if (firstWeek !== landsOn || carried < 10) throw new Error(`week ${firstWeek}'s ${carried} classes land on week ${landsOn}`);
    const registers = +(await page.textContent('[data-testid=next-year-registers]')).match(/\d+/)[0];
    if (registers < 10) throw new Error(`${registers} registers are said to stay with this year`);
    if (await page.isChecked('[data-testid=next-year-closures]')) throw new Error('the closures are kept unless somebody ticks them');
    await page.waitForTimeout(300);
    await page.screenshot({ path: OUT + 'next-year-form.png' });
  });

  await step('making it puts a new timetable beside this one, and this one stays open for everybody', async () => {
    await page.fill('[data-testid=next-year-name]', 'Northlight next year');
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await projectRow(page, 'Northlight next year').waitFor();
    if (!(await projectRow(page, THIS_YEAR).textContent()).includes('Open for everybody')) throw new Error('this year is no longer the one open');
    if (await projectRow(page, 'Northlight next year').locator('[data-testid=project-open]').count() !== 1) throw new Error('next year was opened for everybody by itself');
  });

  await step('next year opens on this year\'s first timetabled week, and the week after it is blank', async () => {
    await openFor(page, 'Northlight next year');
    // The made-up school's first timetabled week is the week of Monday two weeks ago; next year's is the same week number.
    const monday = d => { const x = new Date(d); x.setUTCDate(x.getUTCDate() - ((x.getUTCDay() + 6) % 7)); return x; };
    const today = new Date(new Date().toISOString().slice(0, 10));
    const was = monday(today); was.setUTCDate(was.getUTCDate() - 14);
    const isoWeek = d => { const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + 3 - ((d.getUTCDay() + 6) % 7))); const y = new Date(Date.UTC(t.getUTCFullYear(), 0, 4)); return 1 + Math.round(((t - y) / 864e5 - 3 + ((y.getUTCDay() + 6) % 7)) / 7); };
    if (isoWeek(was) !== firstWeek) throw new Error(`the form named week ${firstWeek}, two weeks ago was week ${isoWeek(was)}`);
    // Next year's same week: 52 or 53 weeks on, whichever keeps the number.
    let next = new Date(was); next.setUTCDate(next.getUTCDate() + 364);
    if (isoWeek(next) !== firstWeek) next.setUTCDate(next.getUTCDate() + 7);
    const day = next.toISOString().slice(0, 10);
    const named = `${next.getUTCDate()} ${next.toLocaleString('en-GB', { month: 'long', timeZone: 'UTC' })} ${next.getUTCFullYear()}`;
    if (named !== landsDay) throw new Error(`the form said the classes land on the week of ${landsDay}, next year's week ${firstWeek} starts ${named}`);
    const there = await classesInWeekOf(page, day);
    log(`      week of ${day}: ${there} classes`);
    if (there !== carried) throw new Error(`the form said ${carried} classes carry over, week of ${day} has ${there}`);
    const after = new Date(next); after.setUTCDate(after.getUTCDate() + 7);
    const later = after.toISOString().slice(0, 10);
    await page.fill('[data-testid=day-date]', later);
    await page.waitForSelector(`[data-testid=plan-page][data-day="${later}"] [data-testid=week-matrix]`);
    const blank = await page.locator('[data-testid=week-matrix] [data-testid=block]').count();
    if (blank !== 0) throw new Error(`the week after has ${blank} classes`);
    await page.fill('[data-testid=day-date]', day);
    await page.waitForSelector(`[data-testid=plan-page][data-day="${day}"] [data-testid=week-matrix] [data-testid=block]`);
    await page.waitForTimeout(300);
    await page.screenshot({ path: OUT + 'next-year-first-week.png' });
  });

  await step('this year is as it was: its first week still has its classes and registers', async () => {
    await openFor(page, THIS_YEAR);
    const monday = new Date(new Date().toISOString().slice(0, 10));
    monday.setUTCDate(monday.getUTCDate() - ((monday.getUTCDay() + 6) % 7) - 14);
    const n = await classesInWeekOf(page, monday.toISOString().slice(0, 10));
    if (n !== carried) throw new Error(`this year's first week has ${n} classes, it had ${carried}`);
    await page.goto(B + '/admin/projects');
    await projectRow(page, THIS_YEAR).locator('[data-testid=project-next-year]').click();
    const registers = +(await page.textContent('[data-testid=next-year-registers]')).match(/\d+/)[0];
    if (registers < 10) throw new Error(`this year has ${registers} registers now`);
    await page.click('[data-testid=dialog-cancel]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a timetable with no semesters has no year to roll forward, and says so', async () => {
    await page.click('[data-testid=project-new-blank]');
    const row = page.locator('[data-testid=projects] > li', { hasText: 'New timetable' });
    await row.first().waitFor();
    await row.first().locator('[data-testid=project-next-year]').click();
    await page.waitForSelector('[data-testid=next-year-no-semesters]');
    if (await page.locator('[data-testid=dialog-ok]').count()) throw new Error('it offers to make a year anyway');
    await page.click('[data-testid=dialog-close]');
  });

  await step('on a phone the form fits the screen', async () => {
    page = await signIn('admin', { width: 390, height: 844 });
    await page.goto(B + '/admin/projects');
    await projectRow(page, THIS_YEAR).locator('[data-testid=project-next-year]').click();
    await page.waitForSelector('[data-testid=next-year-semesters]');
    await page.waitForFunction(() => document.querySelector('[data-testid=dialog]').getAnimations({ subtree: true }).every(a => a.playState === 'finished'));
    const box = await page.locator('[data-testid=dialog]').boundingBox();
    if (box.x < 0 || box.x + box.width > 390.5) throw new Error(`the form runs from ${box.x} to ${box.x + box.width}`);
    const over = await page.locator('[data-testid=dialog] li').evaluateAll(es => es.filter(e => e.getBoundingClientRect().right > 390.5).length);
    if (over) throw new Error(`${over} lines run off the screen`);
    await page.screenshot({ path: OUT + 'next-year-phone.png' });
  });

  if (errors.length) { failed++; log('FAIL browser errors:', errors.slice(0, 3).join(' | ')); }
  log(failed ? `${failed} failed` : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
