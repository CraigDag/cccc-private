// The Scratch Week, as an editor: copy a class in, plan in it, copy a week in, place it onto a week,
// undo, empty it; and a coach never sees it. Against a fresh preview (made-up data).
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
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push(e.message));
  let failed = 0;
  const step = async (name, fn) => { try { await fn(); log('OK  ', name); } catch (e) { failed++; log('FAIL', name, why(e)); await page.screenshot({ path: `${OUT}fail-${name.replace(/\W+/g,'-')}.png` }); } };
  const signIn = async (p, who) => {
    await p.goto(B + '/signin');
    await p.fill('[data-testid=signin-login]', who);
    await p.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
    await p.click('[data-testid=signin-submit]');
    await p.waitForSelector('[data-testid=now-page]');
  };
  const inScratch = (p, yes) => p.waitForFunction(y => document.querySelector('[data-testid=plan-page]')?.dataset.scratch === y, yes ? '1' : '0');
  const blocks = p => p.locator('[data-testid=timeline] [data-testid=block]');
  const more = async (p, testid) => {
    await p.click('[data-testid=more-menu]');
    await p.click(`[data-testid=${testid}]`);
    await p.waitForSelector('[data-testid=dialog]');
  };
  const ok = async p => {
    await p.click('[data-testid=dialog-ok]');
    await p.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  };

  await signIn(page, 'editor');
  let real = {};

  await step('the plan offers the Scratch Week to an editor, empty to begin with', async () => {
    await page.click('[data-testid=nav-plan]');
    await page.waitForSelector('[data-testid=timeline]');
    await page.locator('[data-testid=day-chip]').first().click();
    // The FIRST chip must be the picked one: today's chip is picked before the click lands, and its blocks would do.
    await page.waitForFunction(() => document.querySelector('[data-testid=day-chip]')?.getAttribute('aria-current') === 'date'
      && document.querySelectorAll('[data-testid=timeline] [data-testid=block]').length > 0);
    const label = (await page.textContent('[data-testid=scratch-open]')).trim();
    if (label !== 'Scratch Week') throw new Error('the button says ' + label);
  });

  await step('Copy to Scratch Week leaves the class where it is and counts it on the button', async () => {
    const b = blocks(page).first();
    real = { id: await b.getAttribute('data-id'), title: await b.getAttribute('data-title'), start: await b.getAttribute('data-start') };
    await page.click(`[data-testid=block][data-id="${real.id}"]`);
    await page.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, real.title);
    await page.click('[data-testid=inspector-to-scratch]');
    await page.waitForFunction(() => /Scratch Week · 1$/.test(document.querySelector('[data-testid=scratch-open]')?.textContent.trim() ?? ''));
    const toast = await page.locator('[data-testid=toast]').last().textContent();
    if (!toast.includes('Copied ' + real.title + ' to the Scratch Week')) throw new Error('the toast says ' + toast);
    if (await page.locator(`[data-testid=block][data-id="${real.id}"]`).count() !== 1) throw new Error('the class left its week');
  });

  let copy = {};
  await step('the Scratch Week opens on the same day, with the copy, no dates and nothing to print', async () => {
    await page.click('[data-testid=scratch-open]');
    await inScratch(page, true);
    const title = await page.textContent('[data-testid=plan-title]');
    if (!/^Scratch Week · (Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday)$/.test(title.trim())) throw new Error('the title is ' + title);
    if (await page.locator('[data-testid=day-date]').count() || await page.locator('[data-testid=today]').count()) throw new Error('a date box or Today is offered');
    if (await page.locator('.day-chip .dc-num').count()) throw new Error('the day chips carry dates');
    await page.waitForFunction(t => [...document.querySelectorAll('[data-testid=timeline] [data-testid=block]')].some(b => b.dataset.title === t), real.title);
    const b = blocks(page).first();
    copy = { id: await b.getAttribute('data-id'), start: await b.getAttribute('data-start') };
    if (copy.id === real.id) throw new Error('the copy has the class\'s own id');
    await page.click('[data-testid=more-menu]');
    // Wait for the menu's own render before counting what is in it, or the count reads the screen before it opened.
    await page.locator('.menu .menu-label', { hasText: 'The Scratch Week' }).waitFor();
    const printable = await page.locator('.menu a', { hasText: 'As a calendar' }).count();
    await page.click('[data-testid=more-menu]');
    await page.waitForSelector('.menu', { state: 'detached' });
    if (printable) throw new Error('the Scratch Week can be printed');
    await page.screenshot({ path: OUT + 'scratch-week.png' });
  });

  await step('a drag in the Scratch Week moves the copy and not the class', async () => {
    const b = page.locator(`[data-testid=block][data-id="${copy.id}"]`);
    const tl = page.locator('[data-testid=timeline]');
    const span = (+await tl.getAttribute('data-end')) - (+await tl.getAttribute('data-start'));
    const track = await page.locator('.tl-row[data-row] .tl-track').first().boundingBox();
    const hour = track.width / span * 60;
    const box = await b.boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    for (let i = 1; i <= 10; i++) await page.mouse.move(box.x + box.width / 2 + i * hour / 10, box.y + box.height / 2);
    await page.mouse.up();
    await page.waitForFunction(([id, before]) => { const e = document.querySelector(`[data-testid=block][data-id="${id}"]`); return e && e.dataset.start !== before; }, [copy.id, copy.start], { timeout: 5000 });
    const toast = await page.locator('[data-testid=toast]').last().textContent();
    if (!toast.includes('in the Scratch Week')) throw new Error('the toast says ' + toast);
  });

  await step('adding a class in the Scratch Week asks for a day, not a date, and it clashes only with the plan', async () => {
    const before = await blocks(page).count();
    await page.click('[data-testid=add-session]');
    await page.waitForSelector('[data-testid=dialog]');
    const h = await page.locator('[data-testid=dialog] h2').first().textContent();
    if (!h.includes('to the Scratch Week')) throw new Error('the form says ' + h);
    if (await page.locator('[data-testid=session-date]').count()) throw new Error('the form asks for a date');
    await page.locator('[data-testid=session-day]').waitFor();
    await page.fill('[data-testid=session-title]', 'Trial piece');
    await page.selectOption('[data-testid=session-for]', 'Everyone');
    await ok(page);
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=timeline] [data-testid=block]').length === n + 1, before);
  });

  await step('back to the real week: the class is where it was', async () => {
    await page.click('[data-testid=scratch-close]');
    await inScratch(page, false);
    await page.waitForSelector(`[data-testid=block][data-id="${real.id}"]`);
    const start = await page.getAttribute(`[data-testid=block][data-id="${real.id}"]`, 'data-start');
    if (start !== real.start) throw new Error(`the class moved from ${real.start} to ${start}`);
    await page.waitForFunction(() => /Scratch Week · 2$/.test(document.querySelector('[data-testid=scratch-open]')?.textContent.trim() ?? ''));
  });

    await step('Copy this week to Scratch Week says what it replaces, then opens the plan with the week in it', async () => {
    await page.click('[data-testid=scale-week]');
    await page.waitForSelector('[data-testid=week-matrix]');
    await more(page, 'scratch-copy-week');
    const said = await page.textContent('[data-testid=scratch-replaces]');
    if (!said.includes('2 classes are replaced')) throw new Error('the form says ' + said);
    await ok(page);
    await inScratch(page, true);
    await page.waitForFunction(() => /^Scratch Week$/.test(document.querySelector('[data-testid=plan-title]')?.textContent.trim() ?? ''));
    await page.screenshot({ path: OUT + 'scratch-week-whole.png' });
  });

  await step('Place onto a week replaces the week chosen, says so first, and goes there', async () => {
    await more(page, 'scratch-place');
    const weeks = page.locator('[data-testid=place-weeks] label');
    if (await weeks.count() < 2) throw new Error('fewer than two weeks offered');
    // The week after the one we came from.
    const pick = weeks.nth(1).locator('input[type=radio]');
    await pick.check();
    await page.waitForSelector('[data-testid=dialog-ok]:not([disabled])');
    const okLabel = await page.textContent('[data-testid=dialog-ok]');
    await page.waitForTimeout(400); // the drawer's slide, for the picture only
    await page.screenshot({ path: OUT + 'scratch-place.png' });
    await ok(page);
    await inScratch(page, false);
    const title = await page.textContent('[data-testid=plan-title]');
    if (okLabel.trim() !== 'Place onto ' + title.trim().replace('Week ', 'week ')) throw new Error(`placed by "${okLabel}" and landed on "${title}"`);
    const toast = await page.locator('[data-testid=toast]').last().textContent();
    if (!toast.includes('Placed the Scratch Week onto week')) throw new Error('the toast says ' + toast);
  });

  await step('Undo takes the placing back in one step', async () => {
    await page.click('[data-testid=undo]');
    await page.waitForFunction(() => [...document.querySelectorAll('[data-testid=toast]')].some(t => t.textContent.includes('Undid: Placed the Scratch Week')));
  });

  await step('Empty Scratch Week takes the plan out and says it is empty', async () => {
    await page.click('[data-testid=scratch-open]');
    await inScratch(page, true);
    await more(page, 'scratch-empty');
    await ok(page);
    await page.waitForSelector('[data-testid=scratch-empty-note]');
  });

  await step('the phone: the Scratch Week\'s header and bar fit the screen', async () => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.waitForTimeout(300);
    const wide = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (wide > 1) throw new Error(`the page is ${wide}px wider than the phone`);
    await page.screenshot({ path: OUT + 'scratch-phone.png' });
    await page.setViewportSize({ width: 1400, height: 900 });
  });

  await step('a coach is never offered the Scratch Week', async () => {
    const c = await (await browser.newContext({ viewport: { width: 1400, height: 900 } })).newPage();
    await signIn(c, 'coach');
    await c.click('[data-testid=nav-plan]');
    await c.waitForSelector('[data-testid=plan-page]');
    if (await c.locator('[data-testid=scratch-open]').count()) throw new Error('the coach is offered it');
    if (await c.locator('[data-testid=inspector-to-scratch]').count()) throw new Error('the coach can copy into it');
  });

  if (errors.length) { failed++; log('FAIL browser errors:', errors.slice(0, 3).join(' | ')); }
  log(failed ? `${failed} failed` : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
