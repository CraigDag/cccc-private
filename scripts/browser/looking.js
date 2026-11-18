// Looking at a timetable without opening it for everybody: an administrator makes next year and looks at
// it in their own tab. The band says so on every page, the plan opens where next year's classes are, a
// class added there stays there (an editor on this year's timetable never sees it), printing and the
// spreadsheets are next year's, a reload keeps the look and Back returns to the day before. Opening it for
// everybody ends the look for good; deleting it from another tab sends this one back with a warning. The
// band fits a phone. Against a fresh preview (made-up data).
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
// The first line of what went wrong, with the line of Playwright's call log that names what it was waiting for.
const why = e => {
  const lines = String((e && e.message) || e).replace(/\x1b\[[0-9;]*m/g, '').split('\n');
  const wait = lines.slice(1).find(l => /waiting for/.test(l));
  return lines[0] + (wait ? ' (' + wait.trim().replace(/^- /, '') + ')' : '');
};
(async () => {
  const browser = await chromium.launch();
  const errors = [];
  let failed = 0;
  let page;
  const step = async (name, fn) => { try { await fn(); log('OK  ', name); } catch (e) { failed++; log('FAIL', name, why(e)); await page.screenshot({ path: `${OUT}fail-${name.replace(/\W+/g,'-')}.png` }); } };
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
  const idOf = async (p, name) => (await projectRow(p, name).locator('[data-testid=project-download]').getAttribute('href')).split('/').pop();
  const ymd = d => d.toISOString().slice(0, 10);
  const monday = d => { const x = new Date(d); x.setUTCDate(x.getUTCDate() - ((x.getUTCDay() + 6) % 7)); return x; };
  const isoWeek = d => { const t = new Date(Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate() + 3 - ((d.getUTCDay() + 6) % 7))); const y = new Date(Date.UTC(t.getUTCFullYear(), 0, 4)); return 1 + Math.round(((t - y) / 864e5 - 3 + ((y.getUTCDay() + 6) % 7)) / 7); };
  const onDay = (p, day) => p.waitForSelector(`[data-testid=plan-page][data-day="${day}"]`);
  const blocksToday = async (p, day) => {
    await p.goto(B + '/plan');
    await p.waitForSelector('[data-testid=plan-page][data-scale=Day]');
    await p.fill('[data-testid=day-date]', day);
    await onDay(p, day);
    await p.waitForSelector('[data-testid=timeline]');
    return p.locator('[data-testid=timeline] [data-testid=block]').count();
  };
  const THIS_YEAR = 'Northlight 2026–27 (sample)';
  const NEXT = 'Northlight next year';
  const today = ymd(new Date(new Date().toISOString().slice(0, 10)));
  // Next year's first week: this year's first timetabled week (two weeks ago) in the same week number next year.
  const was = monday(new Date(today)); was.setUTCDate(was.getUTCDate() - 14);
  const nextFirst = new Date(was); nextFirst.setUTCDate(nextFirst.getUTCDate() + 364);
  if (isoWeek(nextFirst) !== isoWeek(was)) nextFirst.setUTCDate(nextFirst.getUTCDate() + 7);
  const landing = ymd(nextFirst);
  let editor, other, thisId, nextId, editorToday;

  page = await signIn('admin', { width: 1400, height: 900 });
  editor = await signIn('editor', { width: 1400, height: 900 });

  await step('making next year offers to look at it, and looking opens the plan on next year\'s first week with the band', async () => {
    editorToday = await blocksToday(editor, today);
    await page.goto(B + '/admin/projects');
    await projectRow(page, THIS_YEAR).locator('[data-testid=project-next-year]').click();
    await page.waitForSelector('[data-testid=next-year-name]');
    await page.fill('[data-testid=next-year-name]', NEXT);
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await page.waitForSelector('[data-testid=next-year-made]');
    thisId = await idOf(page, THIS_YEAR);
    nextId = await idOf(page, NEXT);
    if (await page.locator('[data-testid=looking]').count()) throw new Error('the band is up before anybody looked');
    await page.click('[data-testid=next-year-look]');
    await page.waitForSelector('[data-testid=looking]');
    await onDay(page, landing);
    if (!page.url().endsWith('/plan')) throw new Error('looking went to ' + page.url());
    const name = await page.textContent('[data-testid=looking-name]');
    const open = await page.textContent('[data-testid=looking-open]');
    if (name !== NEXT || open !== THIS_YEAR) throw new Error(`the band says ${name}, everybody else on ${open}`);
    await page.waitForSelector('[data-testid=timeline] [data-testid=block]');
    const n = await page.locator('[data-testid=timeline] [data-testid=block]').count();
    log(`      the plan opened on ${landing}, next year's first Monday, with ${n} classes`);
    await page.waitForTimeout(300);
    await page.screenshot({ path: OUT + 'looking-plan.png' });
    // Straight away, a reload comes back to the same timetable and the same day.
    await page.reload();
    await page.waitForSelector('[data-testid=looking]');
    await onDay(page, landing);
  });

  await step('the band is on every page, and the Projects list marks the one this tab is looking at', async () => {
    for (const path of ['', 'registers', 'data', 'reports', 'admin/projects']) {
      await page.goto(B + '/' + path);
      await page.waitForSelector('[data-testid=looking]');
    }
    if (await projectRow(page, NEXT).locator('[data-testid=project-looking]').count() !== 1) throw new Error('next year is not marked as the one looked at');
    if (await projectRow(page, NEXT).locator('[data-testid=project-look], [data-testid=project-delete]').count()) throw new Error('the row looked at offers to look at it again, or to delete it');
    if (await projectRow(page, THIS_YEAR).locator('[data-testid=project-look]').count() !== 1) throw new Error('the open one offers no way to look back at it');
  });

  await step('a class added while looking stays in next year: the editor, on this year, never sees it', async () => {
    const before = await blocksToday(page, today);
    if (before !== 0) throw new Error(`next year's timetable has ${before} classes today`);
    await page.click('[data-testid=add-session]');
    await page.waitForSelector('[data-testid=dialog]');
    await page.selectOption('[data-testid=session-class]', { index: 1 });
    await page.selectOption('[data-testid=session-group]', { index: 1 });
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await page.waitForFunction(() => document.querySelectorAll('[data-testid=timeline] [data-testid=block]').length === 1);
    await editor.reload();
    const seen = await blocksToday(editor, today);
    if (seen !== editorToday) throw new Error(`the editor had ${editorToday} classes today and now has ${seen}`);
    if (await editor.locator('[data-testid=looking]').count()) throw new Error('the editor has the band');
  });

  await step('printing and the spreadsheets are next year\'s', async () => {
    await page.goto(B + '/plan');
    await page.waitForSelector('[data-testid=plan-page]');
    await page.click('[data-testid=more-menu]');
    const print = await page.locator('a[href^="/print/"]').first().getAttribute('href');
    const r = await page.request.get(B + print);
    if (r.status() !== 200 || !(await r.body()).slice(0, 4).equals(Buffer.from('%PDF'))) throw new Error(`the print link answered ${r.status()}`);
    await page.goto(B + '/reports');
    await page.waitForSelector('[data-testid=csv-hours]');
    const href = await page.getAttribute('[data-testid=csv-hours]', 'href');
    if (!href.includes('project=' + nextId)) throw new Error('the spreadsheet link is ' + href);
    // Over next year's first week, next year's sheet has the classes carried over; this year's has nothing then.
    const week = `from=${landing}&to=${landing}`;
    const total = async id => {
      const csv = await (await page.request.get(`${B}/reports/csv/hours?project=${id}&${week}`)).text();
      const lines = csv.split('\r\n');
      const head = lines.findIndex(l => l.startsWith('Facilitator,Teaching'));
      const col = lines[head].split(',').indexOf('Total');
      return lines.slice(head + 1).filter(l => l && !l.startsWith('Cover counts')).reduce((a, l) => a + (+l.split(',')[col] || 0), 0);
    };
    const [next, now] = [await total(nextId), await total(thisId)];
    log(`      hours in the week of ${landing}: next year's ${next}, this year's ${now}`);
    if (!(next > 0) || now !== 0) throw new Error(`next year's sheet has ${next} hours that week, this year's ${now}`);
  });

  await step('a reload keeps the look and the day; Back returns to the open one on the day the tab was on', async () => {
    // Look away from a day that is not today, so Back has to have kept it through the reload.
    const lastMonday = monday(new Date(today)); lastMonday.setUTCDate(lastMonday.getUTCDate() - 7);
    const from = ymd(lastMonday);
    const editorThen = await blocksToday(editor, from);
    await page.goto(B + '/plan');
    await page.waitForSelector('[data-testid=looking]');
    await page.click('[data-testid=looking-back]');
    await page.waitForSelector('[data-testid=looking]', { state: 'detached' });
    await page.fill('[data-testid=day-date]', from);
    await onDay(page, from);
    await page.goto(B + '/admin/projects');
    await projectRow(page, NEXT).locator('[data-testid=project-look]').click();
    await page.waitForSelector('[data-testid=looking]');
    await page.waitForSelector('[data-testid=plan-page]');
    await page.fill('[data-testid=day-date]', landing);
    await onDay(page, landing);
    await page.reload();
    await page.waitForSelector('[data-testid=looking]');
    await onDay(page, landing);
    await page.click('[data-testid=looking-back]');
    await page.waitForSelector('[data-testid=looking]', { state: 'detached' });
    await onDay(page, from);
    await page.waitForSelector('[data-testid=timeline]');
    const n = await page.locator('[data-testid=timeline] [data-testid=block]').count();
    if (n !== editorThen) throw new Error(`back on this year, ${from} has ${n} classes; the editor sees ${editorThen}`);
    await page.reload();
    await page.waitForSelector('[data-testid=plan-page]');
    if (await page.locator('[data-testid=looking]').count()) throw new Error('the band came back after a reload');
    await onDay(page, from);
  });

  await step('opening it for everybody ends the look, and opening this year again does not bring it back', async () => {
    await page.goto(B + '/admin/projects');
    await projectRow(page, NEXT).locator('[data-testid=project-look]').click();
    await page.waitForSelector('[data-testid=looking]');
    await page.goto(B + '/admin/projects');
    await projectRow(page, NEXT).locator('[data-testid=project-open]').click();
    await page.waitForSelector('[data-testid=looking]', { state: 'detached' });
    await projectRow(page, THIS_YEAR).locator('[data-testid=project-open]').click();
    await projectRow(page, THIS_YEAR).locator('[data-testid=project-open]').waitFor({ state: 'detached' });
    await page.waitForTimeout(500);
    if (await page.locator('[data-testid=looking]').count()) throw new Error('the tab went back to looking at next year');
  });

  await step('deleting it in another tab sends this tab back to the open one, and says why', async () => {
    await projectRow(page, NEXT).locator('[data-testid=project-look]').click();
    await page.waitForSelector('[data-testid=looking]');
    other = await signIn('admin', { width: 1400, height: 900 });
    await other.goto(B + '/admin/projects');
    await projectRow(other, NEXT).locator('[data-testid=project-delete]').click();
    await page.waitForSelector('[data-testid=looking]', { state: 'detached' });
    await page.waitForSelector('[data-testid=toast] >> text=has been deleted');
    await page.reload();
    await page.waitForSelector('[data-testid=plan-page]');
    if (await page.locator('[data-testid=looking]').count()) throw new Error('the deleted one came back after a reload');
    await other.locator('.section', { has: other.locator('h2', { hasText: 'Deleted' }) }).locator('button', { hasText: 'Bring back' }).first().click();
    await projectRow(other, NEXT).waitFor();
  });

  await step('a copy brought in offers Look at it first, and the band tells two timetables of one name apart', async () => {
    await page.goto(B + '/admin/projects');
    await page.waitForSelector('[data-testid=projects]');
    const file = await (await page.request.get(`${B}/download/project/${thisId}`)).body();
    const before = await page.locator('[data-testid=projects] > li').count();
    await page.setInputFiles('[data-testid=import-file]', { name: 'copy.cccc2', mimeType: 'application/octet-stream', buffer: file });
    await page.waitForSelector('[data-testid=import-report]');
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=projects] > li').length === n + 1, before);
    const ids = await page.locator('[data-testid=project-download]').evaluateAll(as => as.map(a => a.getAttribute('href').split('/').pop()));
    const copyId = ids.find(i => i !== thisId && i !== nextId);
    const copyRow = page.locator('[data-testid=projects] > li', { has: page.locator(`[data-testid=project-download][href$="${copyId}"]`) });
    // Named as this year is: the case the band has to tell apart.
    await copyRow.locator('button', { hasText: 'Rename' }).click();
    await page.fill('[data-testid=dialog] input', THIS_YEAR);
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await page.waitForFunction(n => [...document.querySelectorAll('[data-testid=projects] > li b')].filter(b => b.textContent === n).length === 2, THIS_YEAR);
    await page.click('[data-testid=import-look]');
    await page.waitForSelector('[data-testid=looking]');
    const band = (await page.textContent('[data-testid=looking]')).replace(/\s+/g, ' ');
    log('     ', band);
    if (!band.includes('another timetable called ' + THIS_YEAR) || await page.locator('[data-testid=looking-open]').count()) throw new Error('the band says ' + band);
    if ((await page.textContent('[data-testid=looking-back]')).trim() !== 'Back to the open timetable') throw new Error('Back says ' + await page.textContent('[data-testid=looking-back]'));
    await page.screenshot({ path: OUT + 'looking-same-name.png' });
    await page.click('[data-testid=looking-back]');
    await page.waitForSelector('[data-testid=looking]', { state: 'detached' });
    await page.goto(B + '/admin/projects');
    await copyRow.locator('[data-testid=project-delete]').click();
    await copyRow.waitFor({ state: 'detached' });
  });

  await step('on a phone the band fits and leaves the page below it', async () => {
    page = await signIn('admin', { width: 390, height: 844 });
    await page.goto(B + '/admin/projects');
    await projectRow(page, NEXT).locator('[data-testid=project-look]').click();
    await page.waitForSelector('[data-testid=looking]');
    await page.waitForSelector('[data-testid=plan-page]');
    const band = await page.locator('[data-testid=looking]').boundingBox();
    const back = await page.locator('[data-testid=looking-back]').boundingBox();
    log(`      band ${Math.round(band.width)}×${Math.round(band.height)}, Back ${Math.round(back.x)}–${Math.round(back.x + back.width)}`);
    if (band.x < 0 || band.x + band.width > 390.5 || back.x < 0 || back.x + back.width > 390.5) throw new Error('the band runs off the screen');
    if (band.height > 220) throw new Error(`the band is ${band.height}px tall`);
    const over = await page.evaluate(() => document.documentElement.scrollWidth);
    if (over > 391) throw new Error(`the page is ${over}px wide`);
    await page.screenshot({ path: OUT + 'looking-phone.png' });
  });

  if (errors.length) { failed++; log('FAIL browser errors:', errors.slice(0, 3).join(' | ')); }
  log(failed ? `${failed} failed` : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
