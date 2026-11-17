// The plan by keyboard alone: Space chooses, Enter opens, Escape closes and puts focus back,
// Ctrl+K and / open Find, Alt+arrows step the day, Ctrl+Z undoes.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
let failed = 0;
const ok = (what) => console.log('OK   ' + what);
const bad = (what, e) => { failed++; console.log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); };
async function check(what, fn) { try { await fn(); ok(what); } catch (e) { bad(what, e); } }
(async () => {
  const browser = await chromium.launch();
  const page = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
  page.on('pageerror', e => bad('page error', e));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', 'admin');
  await page.fill('[data-testid=signin-passphrase]', 'northlight admin');
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  // Plan is pressed the moment Now appears, deliberately with no wait. When the page was drawn on the
  // server first it looked ready before its circuit was, and on a cold server this press changed the
  // address to /plan and left Now on the screen (17 November 2026). The plan's blocks did appear for a
  // moment, so the check is that Now is gone once things settle, not that a block was ever drawn.
  // Run it against a freshly started preview for this step to mean anything.
  await page.click('[data-testid=nav-plan]');
  await check('Plan pressed the moment the page appears goes to the plan and stays', async () => {
    await page.waitForSelector('[data-testid=plan-page] [data-testid=timeline] [data-testid=block]', { timeout: 8000 });
    await page.waitForTimeout(1500);
    const s = await page.evaluate(() => ({ at: location.pathname, now: !!document.querySelector('[data-testid=now-page]'), plan: !!document.querySelector('[data-testid=plan-page]') }));
    if (s.now || !s.plan) throw new Error('at ' + s.at + ' the screen shows ' + (s.now ? 'Now' : 'no plan'));
  });
  const block = () => page.locator('[data-testid=timeline] [data-testid=block]').nth(2);

  await check('Space chooses a block and the side panel opens', async () => {
    const title = await block().getAttribute('data-title');
    await block().focus();
    await page.keyboard.press(' ');
    await page.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, title, { timeout: 4000 });
  });
  await check('Enter opens the class in a drawer', async () => {
    await block().focus();
    await page.keyboard.press('Enter');
    await page.waitForSelector('[data-testid=dialog]', { timeout: 4000 });
  });
  await check('Escape closes the drawer and focus goes back to the block', async () => {
    const id = await block().getAttribute('data-id');
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached', timeout: 4000 });
    await page.waitForFunction(id => document.activeElement?.dataset?.id === id, id, { timeout: 2000 });
  });
  await check('Escape again closes the side panel', async () => {
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=inspector]', { state: 'detached', timeout: 4000 });
  });
  await check('Alt+Right steps to the next day with classes', async () => {
    const before = await page.getAttribute('[data-testid=plan-page]', 'data-day');
    await page.click('[data-testid=plan-title]');
    await page.keyboard.press('Alt+ArrowRight');
    await page.waitForFunction(b => document.querySelector('[data-testid=plan-page]').dataset.day !== b, before, { timeout: 4000 });
    await page.keyboard.press('Alt+ArrowLeft');
    await page.waitForFunction(b => document.querySelector('[data-testid=plan-page]').dataset.day === b, before, { timeout: 4000 });
  });
  await check('Ctrl+K opens Find with the box focused', async () => {
    await page.keyboard.press('Control+k');
    await page.waitForSelector('[data-testid=find]', { timeout: 4000 });
    await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input', null, { timeout: 2000 });
  });
  await check('arrows move through what Find offers and Enter goes there', async () => {
    await page.keyboard.type('studio');
    await page.waitForFunction(() => document.querySelectorAll('[data-testid=find-hit]').length >= 2 && document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith('Studio'), null, { timeout: 4000 });
    await page.keyboard.press('ArrowDown');
    const second = await page.locator('[data-testid=find-hit]').nth(1).locator('.find-label').textContent();
    await page.waitForSelector('[data-testid=find-hit].on >> nth=0');
    if ((await page.locator('[data-testid=find-hit].on .find-label').textContent()) !== second) throw new Error('the second hit is not the one marked');
    await page.keyboard.press('Enter');
    await page.waitForSelector('[data-testid=find]', { state: 'detached', timeout: 4000 });
    await page.waitForFunction(n => document.querySelector('[data-testid=focus-chip]')?.textContent.includes(n), second, { timeout: 4000 });
    console.log('     narrowed to', second, '·', await page.locator('[data-testid=plan-title]').textContent());
  });
  await check('Enter straight after the last letter opens what was typed', async () => {
    // No wait between typing and Enter, deliberately: on 17 November "Studio B" then Enter opened Studio A
    // four times in four, because Enter took the top of the list last drawn, which was still for "Studio".
    // The narrowing is cleared first so the chip cannot already say Studio B from the step before.
    await page.click('[data-testid=focus-chip] button');
    await page.waitForSelector('[data-testid=focus-chip]', { state: 'detached', timeout: 4000 });
    await page.keyboard.press('Control+k');
    await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input', null, { timeout: 2000 });
    await page.keyboard.type('Studio B');
    await page.keyboard.press('Enter');
    await page.waitForSelector('[data-testid=find]', { state: 'detached', timeout: 4000 });
    await page.waitForSelector('[data-testid=focus-chip]', { timeout: 4000 });
    const chip = (await page.locator('[data-testid=focus-chip]').textContent()).replace('×', '').trim();
    if (chip !== 'Studio B') throw new Error('typed "Studio B" and Enter, and the plan narrowed to ' + chip);
  });
  await check('/ opens Find, Escape closes it', async () => {
    await page.click('[data-testid=plan-title]');
    await page.keyboard.press('/');
    await page.waitForSelector('[data-testid=find]', { timeout: 4000 });
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=find]', { state: 'detached', timeout: 4000 });
  });
  await check('/ typed in a box is a character, not Find', async () => {
    await page.goto(B + '/data/students');
    await page.waitForSelector('[data-testid=data-search]');
    await page.click('[data-testid=data-search]');
    await page.keyboard.type('a/b');
    await page.waitForTimeout(300);
    if (await page.locator('[data-testid=find]').count()) throw new Error('Find opened while typing');
    if ((await page.inputValue('[data-testid=data-search]')) !== 'a/b') throw new Error('the box lost the slash');
  });
  await browser.close();
  console.log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
