// Two people at once, and the phone. The administrator drags a class an hour later; the editor's
// screen shows it without reloading, and the editor's Undo has nothing of theirs to take back. A class
// the administrator cancels today is on the editor's Now at once, and Undo takes it off again.
// Then a phone: Now, the plan as a day, a class in the drawer, a Data list and a report, each
// measured for a page wider than the window.
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
let failed = 0;
const ok = (what) => console.log('OK   ' + what);
const bad = (what, e) => { failed++; console.log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); };
async function check(what, fn) { try { await fn(); ok(what); } catch (e) { bad(what, e); } }
async function signIn(browser, user, pass, opts) {
  const page = await (await browser.newContext(opts)).newPage();
  page.on('pageerror', e => bad('page error (' + user + ')', e));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', user);
  await page.fill('[data-testid=signin-passphrase]', pass);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}
async function openPlan(page) {
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=timeline] [data-testid=block]');
  await page.waitForTimeout(500);
}
async function wide(page) {
  return page.evaluate(() => document.documentElement.scrollWidth - innerWidth);
}
(async () => {
  const browser = await chromium.launch();
  const desk = { viewport: { width: 1400, height: 900 } };
  const a = await signIn(browser, 'admin', 'northlight admin', desk);
  const e = await signIn(browser, 'editor', 'northlight editor', desk);
  await openPlan(a); await openPlan(e);

  await check('the editor sees the administrator\'s drag without reloading', async () => {
    const b = a.locator('[data-testid=timeline] [data-testid=block][data-move="1"]').first();
    const id = await b.getAttribute('data-id'), before = await b.getAttribute('data-start');
    await e.waitForSelector(`[data-testid=block][data-id="${id}"][data-start="${before}"]`);
    const tl = a.locator('[data-testid=timeline]');
    const span = (+await tl.getAttribute('data-end')) - (+await tl.getAttribute('data-start'));
    const track = await a.locator('.tl-row[data-row] .tl-track').first().boundingBox();
    const hour = track.width / span * 60, box = await b.boundingBox();
    await a.mouse.move(box.x + box.width / 2, box.y + box.height / 2); await a.mouse.down();
    for (let i = 1; i <= 10; i++) await a.mouse.move(box.x + box.width / 2 + i * hour / 10, box.y + box.height / 2);
    await a.mouse.up();
    await e.waitForFunction(([id, before]) => { const x = document.querySelector(`[data-testid=block][data-id="${id}"]`); return x && x.dataset.start !== before; }, [id, before], { timeout: 6000 });
    console.log('     start', before, '->', await e.locator(`[data-testid=block][data-id="${id}"]`).getAttribute('data-start'));
    if (!(await e.locator('[data-testid=undo]').isDisabled())) throw new Error('the editor\'s Undo is offered for the administrator\'s change');
    await a.click('[data-testid=undo]');
    await e.waitForSelector(`[data-testid=block][data-id="${id}"][data-start="${before}"]`, { timeout: 6000 });
  });
  console.log('     also here (editor):', (await e.locator('[data-testid=status-bar]').textContent()).replace(/\s+/g, ' ').trim());

  // The drag above was undone, so nothing has changed yet. The editor waits on Now while the
  // administrator cancels one of today's classes, then takes it back.
  let skipped = 0;
  await check("a class cancelled today is on the other person's Now at once, and Undo takes it off", async () => {
    const day = await a.getAttribute('[data-testid=plan-page]', 'data-day');
    const today = await a.evaluate(() => new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Copenhagen' }).format(new Date()));
    if (day !== today) { skipped++; console.log('     SKIP: the plan opened on ' + day + ', not today, so there is no class of today\'s to cancel'); return; }
    await e.goto(B + '/');
    await e.waitForSelector('[data-testid=now-page]');
    const before = await e.locator('[data-testid=change]').count();
    if (before !== 0) throw new Error('Now already lists ' + before + ' changes: ' + (await e.locator('[data-testid=change]').allTextContents()).join(' | '));
    const b = a.locator('[data-testid=timeline] [data-testid=block]').first();
    const title = await b.getAttribute('data-title');
    await b.click();
    await a.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, title);
    await a.click('[data-testid=inspector-cancel]');
    await a.waitForSelector('[data-testid=dialog]');
    await a.click('[data-testid=dialog-ok]');
    await a.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await e.waitForSelector('[data-testid=change]', { timeout: 6000 });
    const row = (await e.locator('[data-testid=change]').first().textContent()).replace(/\s+/g, ' ').trim();
    if (!row.startsWith('Cancelled') || !row.includes(title) || !row.includes('Preview Administrator')) throw new Error('the row reads "' + row + '"');
    console.log('     ', row);
    await a.click('[data-testid=undo]');
    await e.waitForSelector('[data-testid=change]', { state: 'detached', timeout: 6000 });
  });

  const p = await signIn(browser, 'admin', 'northlight admin', { viewport: { width: 390, height: 844 }, isMobile: true, hasTouch: true });
  await check('phone: Now fits the window and the dock is there', async () => {
    await p.waitForSelector('[data-testid=dock]');
    await p.screenshot({ path: OUT + 'ph-now.png', fullPage: true });
    const over = await wide(p); if (over > 0) throw new Error('wider than the window by ' + over + 'px');
  });
  await check('phone: the plan is a day and a class opens from it', async () => {
    await p.click('[data-testid=dock] a[href="plan"]');
    await p.waitForSelector('[data-testid=timeline] [data-testid=block]');
    await p.waitForTimeout(400);
    await p.screenshot({ path: OUT + 'ph-plan.png' });
    const over = await wide(p); if (over > 0) throw new Error('wider than the window by ' + over + 'px');
    await p.locator('[data-testid=timeline] [data-testid=block]').nth(1).tap();
    await p.waitForSelector('[data-testid=inspector]');
    await p.waitForTimeout(300);
    await p.screenshot({ path: OUT + 'ph-inspector.png' });
    await p.click('[data-testid=inspector-edit]');
    await p.waitForSelector('[data-testid=dialog]');
    await p.waitForTimeout(400);
    await p.screenshot({ path: OUT + 'ph-session.png' });
    const d = await p.locator('[data-testid=dialog]').boundingBox();
    if (d.x < 0 || d.x + d.width > 391) throw new Error('the drawer is outside the window: ' + JSON.stringify(d));
    await p.click('[data-testid=dialog-x]');
    await p.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });
  await check('phone: a Data list and its form', async () => {
    await p.goto(B + '/data/students'); await p.waitForSelector('[data-testid=data-add]');
    await p.screenshot({ path: OUT + 'ph-students.png' });
    const over = await wide(p); if (over > 0) throw new Error('wider than the window by ' + over + 'px');
    await p.click('[data-testid=data-add]'); await p.waitForSelector('[data-testid=dialog]'); await p.waitForTimeout(400);
    await p.screenshot({ path: OUT + 'ph-student-form.png' });
    await p.click('[data-testid=dialog-x]');
  });
  await check('phone: a report fits the window', async () => {
    await p.goto(B + '/reports/facilitators'); await p.waitForTimeout(1200);
    await p.screenshot({ path: OUT + 'ph-reports.png', fullPage: true });
    const over = await wide(p); if (over > 0) throw new Error('wider than the window by ' + over + 'px');
  });
  await check('phone: Find from the dock', async () => {
    await p.goto(B + '/');
    await p.waitForSelector('[data-testid=dock]');
    await p.click('[data-testid=dock-find]');
    await p.waitForSelector('[data-testid=find]');
    await p.screenshot({ path: OUT + 'ph-find.png' });
  });
  await browser.close();
  console.log(failed ? failed + ' failed' : 'all passed' + (skipped ? ` (${skipped} skipped: see SKIP above)` : ''));
  process.exit(failed ? 1 : 0);
})();
