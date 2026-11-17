// A coach's day: what Now says they owe, My week agreeing with it, everybody's registers one untick
// away, a class whose facilitator is away saying so, and the pages that are not theirs saying that.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }
(async () => {
  const browser = await chromium.launch();
  const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', 'coach');
  await page.fill('[data-testid=signin-passphrase]', 'northlight coach');
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');

  await step('Now says how many registers from earlier days are theirs, and My week lists the same', async () => {
    const text = await page.locator('[data-testid=need-earlier]').textContent({ timeout: 8000 });
    const n = Number((text.match(/(\d+) from earlier days/) || [])[1]);
    if (!(n > 0)) throw new Error('no count in "' + text.trim() + '"');
    if (await page.locator('[data-testid=all-clear]').count()) throw new Error('Now also says nothing is needed');
    await page.click('[data-testid=need-earlier]');
    await page.waitForSelector('[data-testid=still-to-take] li');
    const card = Number(await page.locator('[data-testid=still-to-take-count]').textContent());
    const rows = await page.locator('[data-testid=still-to-take] li').count();
    if (card !== n || rows !== n) throw new Error(`Now says ${n}; My week's card says ${card} and lists ${rows}`);
    log('     ', n, 'from earlier days on both');
  });

  await step("the registers start with their own and widen to everybody's", async () => {
    await page.goto(B + '/registers');
    await page.waitForSelector('[data-testid=register-line]');
    if (!(await page.isChecked('[data-testid=registers-mine]'))) throw new Error('a coach starts on everybody\'s');
    const mine = await page.locator('[data-testid=register-line]').count();
    await page.uncheck('[data-testid=registers-mine]');
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=register-line]').length > n, mine);
    log('     ', mine, 'of theirs,', await page.locator('[data-testid=register-line]').count(), 'in all');
    await page.check('[data-testid=registers-mine]');
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=register-line]').length === n, mine);
  });

  await step('a class whose facilitator is away says so where the names go', async () => {
    const away = page.locator('[data-testid=register-line] .rt-room', { hasText: ' away' });
    if (await away.count() === 0) throw new Error('no row says who is away; the column reads: ' + (await page.locator('[data-testid=register-line] .rt-room').allTextContents()).map(s => s.trim()).join(' | '));
    log('     ', (await away.first().textContent()).trim());
  });

  await step('People & places and Reports say they are for administrators', async () => {
    await page.goto(B + '/data');
    await page.waitForSelector('text=Only an administrator can change');
    await page.goto(B + '/reports');
    await page.waitForSelector('text=Reports are for administrators');
  });

  log('errors', JSON.stringify(errors));
  await browser.close();
  if (errors.length) failed++;
  console.log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
