// Every hour explained: press a facilitator's figure on Reports ▸ Facilitator hours, on a facilitator's
// week on the plan, or on My week, and every class and time rule that makes it up is listed, adding up
// to the figure pressed. Against a fresh preview (made-up data).
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
// "21h 30m", "45m", "+1h" → minutes
const minutes = s => { const t = s.replace('+', '').trim(); const h = /(\d+)h/.exec(t), m = /(\d+)m/.exec(t); return (h ? +h[1] * 60 : 0) + (m ? +m[1] : 0); };
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
  // The drawer's own reading of itself: the lines shown add up to the total it says.
  const drawer = async p => {
    await p.waitForSelector('[data-testid=dialog] [data-testid=hours-lines-total]');
    const total = minutes(await p.textContent('[data-testid=hours-lines-total]'));
    const lens = (await p.locator('[data-testid=hours-line] .hl-len').allTextContents()).map(minutes);
    const kinds = await p.locator('[data-testid=hours-line]').evaluateAll(els => els.map(e => e.dataset.kind));
    return { total, sum: lens.reduce((a, b) => a + b, 0), count: lens.length, kinds,
             title: (await p.textContent('[data-testid=dialog] h2')).trim(), span: (await p.textContent('[data-testid=hours-lines-span]')).trim() };
  };
  const close = async p => { await p.click('[data-testid=dialog-close]'); await p.waitForSelector('[data-testid=dialog]', { state: 'detached' }); };

  page = await signIn('admin', { width: 1400, height: 900 });
  let dagRow, cell = {};

  await step('Reports ▸ Facilitator hours: a week\'s figure opens every line of it, and they add up', async () => {
    await page.goto(B + '/reports/facilitators');
    await page.waitForSelector('[data-testid=hours-by-week]');
    dagRow = page.locator('[data-testid=hours-by-week] tbody tr', { hasText: 'Dag Øverlie' });
    // Dag covered a class last week: the week before this one is the second-last week column.
    const heads = await page.locator('[data-testid=hours-by-week] thead th').allTextContents();
    const cells = dagRow.locator('td');
    const lastWeek = heads.length - 3;              // Facilitator, weeks…, Total: the week before this one
    cell = { week: heads[lastWeek].trim(), text: (await cells.nth(lastWeek).textContent()).trim() };
    await cells.nth(lastWeek).locator('[data-testid=hours-cell]').click();
    const d = await drawer(page);
    log('     ', d.title, '·', d.span, '·', d.count, 'lines,', d.kinds.filter(k => k === 'Covering').length, 'covering,', d.kinds.filter(k => k === 'Rule').length, 'from time rules');
    if (d.title !== 'Dag Øverlie’s hours') throw new Error('the drawer is called ' + d.title);
    if (!d.span.startsWith('Week ' + cell.week.replace('Wk ', ''))) throw new Error(`pressed ${cell.week}, the drawer says "${d.span}"`);
    if (d.total !== minutes(cell.text)) throw new Error(`pressed ${cell.text}, the drawer totals ${d.total} minutes`);
    if (d.sum !== d.total) throw new Error(`the lines add up to ${d.sum} minutes, the total says ${d.total}`);
    if (!d.kinds.includes('Covering')) throw new Error('the class Dag covered is not among the lines');
    const cover = (await page.locator('[data-testid=hours-line][data-kind=Covering]').first().textContent()).replace(/\s+/g, ' ');
    if (!/covering Bo/.test(cover)) throw new Error('the cover line reads "' + cover + '"');
    await page.waitForTimeout(400); // the drawer's slide, for the picture only
    await page.screenshot({ path: OUT + 'hours-lines-week.png' });
  });

  await step('Cover alone: only the classes covered, and how much of the total they are', async () => {
    await page.click('[data-testid=hours-lines-show] button:has-text("Cover")');
    await page.waitForSelector('[data-testid=hours-lines-part]');
    const kinds = await page.locator('[data-testid=hours-line]').evaluateAll(els => els.map(e => e.dataset.kind));
    if (!kinds.length || kinds.some(k => k !== 'Covering')) throw new Error('shown: ' + kinds.join(', '));
    const part = (await page.textContent('[data-testid=hours-lines-part]')).replace(/\s+/g, ' ').trim();
    if (!/ of the .* is cover\.$/.test(part)) throw new Error('the line reads "' + part + '"');
    await close(page);
  });

  await step('the Total opens every week chosen, week by week', async () => {
    const total = (await dagRow.locator('[data-testid=hours-total]').textContent()).trim();
    await dagRow.locator('[data-testid=hours-total]').click();
    const d = await drawer(page);
    log('     ', d.span, '·', d.count, 'lines');
    if (d.total !== minutes(total)) throw new Error(`pressed ${total}, the drawer totals ${d.total} minutes`);
    if (d.sum !== d.total) throw new Error(`the lines add up to ${d.sum} minutes, the total says ${d.total}`);
    if (await page.locator('.hl-week').count() < 2) throw new Error('fewer than two weeks are headed');
    await close(page);
  });

  await step('a facilitator\'s week on the plan says their hours, and opens the same lines', async () => {
    await page.goto(B + '/plan');
    await page.waitForSelector('[data-testid=plan-page]');
    await page.keyboard.press('Control+k');
    await page.waitForSelector('[data-testid=find]');
    await page.keyboard.type('Dag');
    await page.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith('Dag'));
    await page.keyboard.press('Enter');
    await page.waitForSelector('[data-testid=focus-chip]');
    const said = (await page.textContent('[data-testid=focus-hours]')).trim();
    await page.click('[data-testid=focus-hours]');
    const d = await drawer(page);
    if (said !== `${await page.textContent('[data-testid=hours-lines-total]')} this week`.trim()) throw new Error(`the week says "${said}", the drawer ${d.total} minutes`);
    if (d.sum !== d.total) throw new Error(`the lines add up to ${d.sum} minutes, the total says ${d.total}`);
    await close(page);
  });

  await step('My week: a coach\'s own hours add up the same way, and a phone holds them', async () => {
    const coach = await signIn('coach', { width: 390, height: 844 });
    page = coach;
    await coach.goto(B + '/my-week');
    await coach.click('[data-testid=my-hours-lines]');
    const d = await drawer(coach);
    if (d.title !== 'Your hours') throw new Error('the drawer is called ' + d.title);
    if (d.sum !== d.total) throw new Error(`the lines add up to ${d.sum} minutes, the total says ${d.total}`);
    const wide = await coach.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (wide > 1) throw new Error(`the page is ${wide}px wider than the phone`);
    await coach.waitForTimeout(400);
    await coach.screenshot({ path: OUT + 'hours-lines-phone.png' });
  });

  if (errors.length) { failed++; log('FAIL browser errors:', errors.slice(0, 3).join(' | ')); }
  log(failed ? `${failed} failed` : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
