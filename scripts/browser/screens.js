// Screens in the building: a TV with nobody signed in shows a code, an administrator types it in
// Administration ▸ Screens, and the TV shows the building as it is now; a class cancelled in the plan is
// on it at its next look; a tablet by a door shows one space's day; changing and unpairing reach the
// screen; a short screen pages; a screen that loses the timetable keeps what it knew and says so; and the
// screen's cookie opens no other page. Best on a preview started with AT=10:15, mid-morning.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
const SPACES = ['Gym', 'Aerial Hall', 'Big Top', 'Studio A', 'Studio B', 'Canteen'];
let failed = 0;
const log = (...a) => console.log(...a);
// The first line of what went wrong, with the line of Playwright's call log that names what it was waiting for.
const why = e => {
  const lines = String((e && e.message) || e).replace(/\x1b\[[0-9;]*m/g, '').split('\n');
  const wait = lines.slice(1).find(l => /waiting for/.test(l));
  return lines[0] + (wait ? ' (' + wait.trim().replace(/^- /, '') + ')' : '');
};
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + why(e)); } }

async function signIn(browser, who, viewport, extra = {}) {
  const page = await (await browser.newContext({ viewport, ...extra })).newPage();
  page.on('pageerror', e => log('     page error (' + who + '): ' + e.message));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}

// A screen nobody has signed in on: its own browser, as a TV's would be.
async function screen(browser, viewport) {
  const page = await (await browser.newContext({ viewport })).newPage();
  page.on('pageerror', e => log('     page error (screen): ' + e.message));
  page.on('console', m => { if (m.type() === 'error' && !/Failed to load resource|ERR_INTERNET_DISCONNECTED/.test(m.text())) log('     console (screen): ' + m.text()); });
  await page.goto(B + '/screen');
  await page.waitForSelector('[data-testid=screen-code]');
  return page;
}

// Types the screen's code as somebody would read it off the wall, and waits for the screen to show the board.
async function pair(admin, tv, name, shows) {
  const code = (await tv.textContent('[data-testid=screen-code]')).trim();
  if (!/^[A-Z0-9]{3} [A-Z0-9]{3}$/.test(code)) throw new Error('the code reads "' + code + '"');
  if (!admin.url().endsWith('/admin/screens')) { await admin.goto(B + '/admin/screens'); await admin.waitForSelector('[data-testid=pair]'); }
  const before = await admin.locator('[data-testid=screen-item]').count();
  await admin.fill('[data-testid=pair-code]', code.toLowerCase());
  await admin.fill('[data-testid=pair-name]', name);
  if (shows) await admin.selectOption('[data-testid=pair-shows]', { label: shows });
  await admin.click('[data-testid=pair]');
  await admin.waitForFunction(n => document.querySelectorAll('[data-testid=screen-item]').length > n, before);
  await tv.waitForSelector('[data-testid=screen]', { timeout: 20000 });
  await tv.waitForTimeout(500);
  return code;
}

// What the board says is on now in one space; throws unless the board has exactly one row for it.
async function nowIn(tv, space) {
  const cells = await tv.$$eval('[data-testid=screen-row]', (rs, sp) => rs.filter(r => r.querySelector('.sb-space').textContent.trim() === sp).map(r => r.querySelector('.sb-now').textContent.replace(/\s+/g, ' ').trim()), space);
  if (cells.length !== 1) throw new Error('the board has ' + cells.length + ' rows for "' + space + '"');
  return cells[0];
}
const item = (admin, name) => admin.locator('[data-testid=screen-item]', { hasText: name });
const rows = tv => tv.$$eval('[data-testid=screen-row]', rs => rs.filter(r => r.offsetParent !== null).map(r => r.querySelector('.sb-space').textContent.trim()));
// As a person reads it: innerText keeps the line breaks the layout draws, which textContent runs together.
const text = async (page, sel) => (await page.locator(sel).innerText()).replace(/\s+/g, ' ').trim();

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  const tv = await screen(browser, { width: 1920, height: 1080 });

  await step('a TV with nobody signed in shows a code, and the code typed in Administration ▸ Screens pairs it', async () => {
    await tv.screenshot({ path: OUT + 'screen-code.png' });
    const code = await pair(admin, tv, 'Corridor');
    log('     paired with', code);
    await admin.screenshot({ path: OUT + 'screen-admin.png' });
    const said = await item(admin, 'Corridor').textContent();
    if (!/The whole building/.test(said)) throw new Error('the list says "' + said.replace(/\s+/g, ' ') + '"');
  });

  await step('the board shows every space, and the clock runs on the school\'s time', async () => {
    const shown = await rows(tv);
    if (shown.join('|') !== [...shown].filter(s => SPACES.includes(s)).join('|') || SPACES.some(s => !shown.includes(s)))
      throw new Error('the board shows ' + shown.join(', '));
    const clock = await text(tv, '[data-screen-clock]');
    if (!/^\d\d:\d\d$/.test(clock)) throw new Error('the clock reads "' + clock + '"');
    if (process.env.AT && !(clock >= process.env.AT && clock < '23:59')) throw new Error(`the clock reads ${clock}, before ${process.env.AT}`);
    await tv.screenshot({ path: OUT + 'screen-corridor.png' });
    log('     ', clock, '·', shown.length, 'spaces ·', (await tv.locator('[data-testid=screen-coming] .sb-item').count()), 'coming up');
  });

  await step('nothing on it a corridor should not show: no student, no staff-only event', async () => {
    const all = await tv.locator('[data-testid=screen]').textContent();
    for (const never of ['Ashcombe', 'Birkeland', 'Corvino', 'Staff meeting', 'Budget review', 'Brightwater', 'Øverlie'])
      if (all.includes(never)) throw new Error('the screen says "' + never + '"');
  });

  await step('a class cancelled in the plan is under "Not on today" at the screen\'s next look, and its space is free', async () => {
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=plan-page]');
    const day = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
    const today = await admin.evaluate(() => new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Copenhagen' }).format(new Date()));
    const blocks = admin.locator('[data-testid=timeline] [data-testid=block]');
    if (day !== today || await blocks.count() === 0) { log('     SKIP: no class today to cancel'); return; }
    // A class in Studio A, so the space it leaves free can be read on the board.
    let b = null;
    for (let i = 0; i < await blocks.count(); i++) {
      const row = await blocks.nth(i).evaluate(el => el.closest('[data-testid=row]')?.querySelector('.tl-label')?.getAttribute('title') || '');
      if (row === 'Studio A') { b = blocks.nth(i); break; }
    }
    b = b || blocks.first();
    const title = await b.getAttribute('data-title');
    const space = await b.evaluate(el => el.closest('[data-testid=row]')?.querySelector('.tl-label')?.getAttribute('title') || '');
    const wasOn = (await nowIn(tv, space)).includes(title);
    await b.click();
    await admin.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, title);
    await admin.click('[data-testid=inspector-cancel]');
    await admin.waitForSelector('[data-testid=dialog]');
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await tv.evaluate(() => window.ccccScreen.refresh());
    await tv.waitForFunction(t => (document.querySelector('[data-testid=screen-off]')?.textContent || '').includes(t), title, { timeout: 8000 });
    const off = await text(tv, '[data-testid=screen-off]');
    if (!/cancelled/.test(off)) throw new Error('"Not on today" reads "' + off + '"');
    log('     ', off);
    if (!wasOn) log('     (' + title + ' was not on in ' + space + ' at this time, so whether its space shows free is not tried)');
    else if ((await nowIn(tv, space)).includes(title)) throw new Error(space + ' on the board still reads "' + await nowIn(tv, space) + '"');
    await tv.screenshot({ path: OUT + 'screen-cancelled.png' });
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-cancel]')?.textContent === 'Restore');
    await admin.click('[data-testid=undo]');
    // The click returns before the undo lands: wait for the button only the undone render draws.
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-cancel]')?.textContent === 'Cancel');
    await tv.evaluate(() => window.ccccScreen.refresh());
    await tv.waitForFunction(t => !(document.querySelector('[data-testid=screen-off]')?.textContent || '').includes(t), title, { timeout: 8000 });
  });

  const tablet = await screen(browser, { width: 800, height: 1280 });
  await step('a tablet by Studio A\'s door shows that space: what is on now, big, and its day', async () => {
    await pair(admin, tablet, 'Studio A door', "Studio A's day, for a screen by its door");
    const name = await text(tablet, '[data-testid=screen-space]');
    if (name !== 'Studio A') throw new Error('the door sign is for "' + name + '"');
    const now = await text(tablet, '[data-testid=screen-now]');
    if (!/^now \S/i.test(now)) throw new Error('"Now" reads "' + now + '"');
    if (await tablet.locator('[data-testid=screen-row]').count() !== 0) throw new Error('the door sign shows the whole building');
    await tablet.screenshot({ path: OUT + 'screen-door.png' });
    log('     ', now, '·', await tablet.locator('[data-testid=screen-ahead]').count(), 'days ahead');
  });

  await step('changing what a screen shows reaches it at its next look', async () => {
    await admin.goto(B + '/admin/screens');
    await item(admin, 'Corridor').locator('[data-testid=screen-change]').click();
    await admin.selectOption('[data-testid=screen-edit-shows]', { label: "Big Top's day" });
    await admin.click('[data-testid=screen-save]');
    await admin.waitForSelector('[data-testid=screen-edit-shows]', { state: 'detached' });
    await tv.evaluate(() => window.ccccScreen.refresh());
    await tv.waitForFunction(() => document.querySelector('[data-testid=screen-space]')?.textContent === 'Big Top', null, { timeout: 8000 });
    await item(admin, 'Corridor').locator('[data-testid=screen-change]').click();
    await admin.selectOption('[data-testid=screen-edit-shows]', { label: 'The whole building' });
    await admin.click('[data-testid=screen-save]');
    await admin.waitForSelector('[data-testid=screen-edit-shows]', { state: 'detached' });
    await tv.evaluate(() => window.ccccScreen.refresh());
    await tv.waitForFunction(() => document.querySelectorAll('[data-testid=screen-row]').length > 0, null, { timeout: 8000 });
  });

  await step('a screen that cannot reach the timetable keeps the board and says since when', async () => {
    await tv.context().setOffline(true);
    await tv.evaluate(() => window.ccccScreen.refresh());
    const status = await text(tv, '[data-screen-status]');
    if (!/^Cannot reach the timetable since \d\d:\d\d/.test(status)) throw new Error('the status reads "' + status + '"');
    if (await tv.locator('[data-testid=screen-row]').count() === 0) throw new Error('the board went blank');
    await tv.screenshot({ path: OUT + 'screen-offline.png' });
    await tv.context().setOffline(false);
    await tv.evaluate(() => window.ccccScreen.refresh());
    const back = await text(tv, '[data-screen-status]');
    if (!/^Updated \d\d:\d\d$/.test(back)) throw new Error('back online, the status reads "' + back + '"');
  });

  await step('the screen\'s cookie opens no other page', async () => {
    const other = await tv.context().newPage();
    for (const path of ['/', '/plan', '/admin/screens']) {
      await other.goto(B + path);
      if (!new URL(other.url()).pathname.startsWith('/signin')) throw new Error(path + ' opened at ' + other.url());
    }
    await other.close();
  });

  const short = await screen(browser, { width: 1280, height: 430 });
  await step('a short screen pages through the spaces, each on exactly one page', async () => {
    await pair(admin, short, 'Stairs');
    const pages = await short.evaluate(() => window.ccccScreen.pages());
    if (pages < 2) throw new Error('every space fits on one page of a 430px screen');
    const label = await text(short, '[data-screen-page]');
    if (label !== `Page 1 of ${pages}`) throw new Error('the footer says "' + label + '"');
    const seen = [];
    for (let i = 0; i < pages; i++) {
      seen.push(...await rows(short));
      if (i === 0) await short.screenshot({ path: OUT + 'screen-paged.png' });
      await short.evaluate(() => window.ccccScreen.nextPage());
    }
    if (seen.length !== SPACES.length || SPACES.some(s => !seen.includes(s))) throw new Error('across the pages: ' + seen.join(', '));
    log('     ', pages, 'pages:', seen.join(', '));
  });

  await step('Administration ▸ Screens says when each screen last looked', async () => {
    await admin.goto(B + '/admin/screens');
    await admin.waitForSelector('[data-testid=screen-item]');
    const seen = await admin.locator('[data-testid=screen-seen]').allTextContents();
    if (seen.length !== 3 || seen.some(s => !/last looked today \d\d:\d\d/.test(s))) throw new Error('the list says ' + JSON.stringify(seen));
  });

  await step('unpairing turns the TV back into a code', async () => {
    await item(admin, 'Corridor').locator('[data-testid=screen-unpair]').click();
    await admin.click('[data-testid=screen-unpair-yes]');
    await admin.waitForFunction(() => ![...document.querySelectorAll('[data-testid=screen-item]')].some(li => li.textContent.includes('Corridor')));
    // The screen finds out at its next look and starts again from a code.
    await tv.evaluate(() => window.ccccScreen.refresh()).catch(() => { });
    await tv.waitForSelector('[data-testid=screen-code]', { timeout: 15000 });
    if (await tv.locator('[data-testid=screen]').count() !== 0) throw new Error('the board is still there');
  });

  const phone = await signIn(browser, 'admin', { width: 390, height: 844 }, { isMobile: true, hasTouch: true });
  await step('phone: Administration ▸ Screens fits the window', async () => {
    await phone.goto(B + '/admin/screens');
    await phone.waitForSelector('[data-testid=pair]');
    await phone.waitForTimeout(300);
    await phone.screenshot({ path: OUT + 'screen-admin-phone.png', fullPage: true });
    const over = await phone.evaluate(() => document.documentElement.scrollWidth - innerWidth);
    if (over > 0) throw new Error('wider than the window by ' + over + 'px');
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
