// Every hour explained: press a facilitator's figure on Reports ▸ Facilitator hours, on a facilitator's
// week on the plan, or on My week, and every class and time rule that makes it up is listed, adding up
// to the figure pressed. Against a fresh preview (made-up data).
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
// "21h 30m", "45m", "+1h" → minutes
const minutes = s => { const t = s.replace('+', '').trim(); const h = /(\d+)h/.exec(t), m = /(\d+)m/.exec(t); return (h ? +h[1] * 60 : 0) + (m ? +m[1] : 0); };
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
    const lastWeek = heads.findIndex(h => h.trim() === 'Total') - 2;   // Facilitator, weeks…, Total, A week: the week before this one
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

  await step('the report narrows to a name, to those over their limit and without guests', async () => {
    await page.goto(B + '/reports/facilitators');
    await page.waitForSelector('[data-testid=hours-by-week] [data-testid=hours-row]');
    const rows = () => page.locator('[data-testid=hours-row]');
    const names = () => rows().evaluateAll(rs => rs.map(r => r.dataset.name));
    const count = n => page.waitForFunction(n => document.querySelectorAll('[data-testid=hours-row]').length === n, n);
    const all = await names();
    if (all.length < 4) throw new Error('only ' + all.length + ' facilitators are listed: ' + all.join(', '));
    await page.fill('[data-testid=hours-find]', 'øver');
    await count(1);
    if ((await names())[0] !== 'Dag Øverlie') throw new Error('finding "øver" shows ' + (await names()).join(', '));
    await page.fill('[data-testid=hours-find]', '');
    await count(all.length);

    const guests = await rows().filter({ hasText: '(guest)' }).count();
    if (!guests) throw new Error('the sample has no guest facilitator here: ' + all.join(', '));
    await page.uncheck('[data-testid=hours-guests]');
    await count(all.length - guests);
    if (await rows().filter({ hasText: '(guest)' }).count()) throw new Error('a guest is still listed with Guests unticked');
    await page.check('[data-testid=hours-guests]');
    await count(all.length);

    // Who is over is read off the week figures the page draws, then the tick must show exactly them.
    const over = await rows().evaluateAll(rs => rs.filter(r => r.querySelector('td.over')).map(r => r.dataset.name));
    log('     ', over.length, 'of', all.length, 'over their limit in a week:', over.join(', ') || 'nobody');
    if (over.length === all.length) throw new Error('everybody is over their limit, so the tick cannot be seen to narrow: ' + over.join(', '));
    await page.check('[data-testid=hours-over-only]');
    if (over.length === 0) await page.waitForSelector('[data-testid=hours-nobody]');
    else {
      await count(over.length);
      if ((await names()).join('|') !== over.join('|')) throw new Error('over only shows ' + (await names()).join(', ') + '; over: ' + over.join(', '));
    }
    await page.uncheck('[data-testid=hours-over-only]');
    await count(all.length);
  });

  await step('any heading sorts the list, biggest first, and again for the other way round', async () => {
    const totals = () => page.locator('[data-testid=hours-row] [data-testid=hours-total]').allTextContents().then(xs => xs.map(minutes));
    const names = () => page.locator('[data-testid=hours-row]').evaluateAll(rs => rs.map(r => r.dataset.name));
    const before = await names();
    await page.click('[data-testid=hours-sort][data-key=total]');
    await page.waitForSelector('[data-testid=hours-by-week][data-sort=total-down]');
    const down = await totals();
    if (down.some((x, i) => i > 0 && x > down[i - 1])) throw new Error('biggest first reads ' + down.join(', '));
    if (await page.getAttribute('[data-testid=hours-sort][data-key=total] >> xpath=..', 'aria-sort') !== 'descending') throw new Error('the Total heading does not say it is sorted');
    await page.click('[data-testid=hours-sort][data-key=total]');
    await page.waitForSelector('[data-testid=hours-by-week][data-sort=total-up]');
    const up = await totals();
    if (up.some((x, i) => i > 0 && x < up[i - 1])) throw new Error('the other way round reads ' + up.join(', '));
    await page.click('[data-testid=hours-sort][data-key=name]');
    await page.waitForSelector('[data-testid=hours-by-week][data-sort=name-up]');
    if ((await names()).join('|') !== before.join('|')) throw new Error('by name again reads ' + (await names()).join(', '));
  });

  await step('Breakdown: the average week is over the weeks the school ran, and the quietest and busiest open their week', async () => {
    const row = page.locator('[data-testid=hours-row][data-name="Dag Øverlie"]');
    const cells = await row.locator('td[data-ran]').evaluateAll(tds => tds.map(td => ({ ran: td.dataset.ran === '1', text: td.textContent.trim() })));
    const ran = cells.filter(c => c.ran).map(c => minutes(c.text));
    if (ran.length < 2) throw new Error('fewer than two weeks ran: ' + JSON.stringify(cells));
    const expected = Math.round(ran.reduce((a, b) => a + b, 0) / ran.length);
    const avg = minutes(await row.locator('[data-testid=hours-average]').textContent());
    log('      Dag:', ran.join(', '), 'minutes in the weeks that ran; a week', avg);
    if (avg !== expected) throw new Error(`a week says ${avg} minutes; the weeks that ran average ${expected}`);
    // Everybody's figures in the weeks that ran, to hold the breakdown's "each week" to them.
    const ranOf = await page.locator('[data-testid=hours-row]').evaluateAll(rs => rs.map(r => [r.dataset.name, [...r.querySelectorAll('td[data-ran="1"]')].map(td => td.textContent.trim())]));
    await page.click('[data-testid=hours-view-breakdown]');
    await page.waitForSelector('[data-testid=hours-breakdown]');
    // The same figure every week the school ran is said once ("12h each week"), never as the earliest week twice.
    let even = 0;
    for (const [name, texts] of ranOf) {
      const m = texts.map(minutes);
      const same = m.length > 1 && m.every(x => x === m[0]) && m[0] > 0;
      const row = page.locator(`[data-testid=breakdown-row][data-name="${name}"]`);
      const said = await row.locator('[data-testid=breakdown-even]').count();
      if (same !== (said === 1)) throw new Error(`${name}: weeks ${texts.join(', ')}, and the breakdown ${said ? 'says one figure each week' : 'names a quietest and a busiest'}`);
      if (same) { even++; if (minutes(await row.locator('[data-testid=breakdown-even]').textContent()) !== m[0]) throw new Error(`${name}: each week says something other than ${texts[0]}`); }
    }
    log('     ', even, 'with the same hours every week the school ran');
    const b = page.locator('[data-testid=breakdown-row][data-name="Dag Øverlie"]');
    if (minutes(await b.locator('[data-testid=breakdown-average]').textContent()) !== expected) throw new Error('the breakdown\'s average differs from the week by week one');
    const figure = async id => minutes((await b.locator(`[data-testid=${id}]`).textContent()).split('·')[1] || '');
    if (await figure('breakdown-busiest') !== Math.max(...ran)) throw new Error(`the busiest says ${await figure('breakdown-busiest')} minutes; the weeks hold ${ran.join(', ')}`);
    if (await figure('breakdown-quietest') !== Math.min(...ran)) throw new Error(`the quietest says ${await figure('breakdown-quietest')} minutes; the weeks hold ${ran.join(', ')}`);
    const busiest = b.locator('[data-testid=breakdown-busiest]');
    const week = await busiest.getAttribute('data-week');
    await busiest.click();
    const d = await drawer(page);
    if (!d.span.startsWith('Week ' + week)) throw new Error(`pressed week ${week}, the drawer says "${d.span}"`);
    if (d.total !== Math.max(...ran)) throw new Error(`the drawer totals ${d.total} minutes`);
    await page.waitForTimeout(400);
    await page.screenshot({ path: OUT + 'hours-breakdown.png' });
    await close(page);
    await page.click('[data-testid=hours-view-weeks]');
    await page.waitForSelector('[data-testid=hours-by-week]');
  });

  await step('on a phone the report\'s tools wrap and the page does not scroll sideways', async () => {
    const phone = await (await browser.newContext({ viewport: { width: 390, height: 844 }, storageState: await page.context().storageState() })).newPage();
    phone.on('pageerror', e => errors.push(e.message));
    await phone.goto(B + '/reports/facilitators');
    await phone.waitForSelector('[data-testid=hours-by-week] [data-testid=hours-row]');
    const wide = await phone.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (wide > 1) throw new Error(`the page is ${wide}px wider than the phone`);
    const box = await phone.locator('[data-testid=hours-find]').boundingBox();
    if (!box || box.x < 0 || box.x + box.width > 391) throw new Error('the find box is outside the phone: ' + JSON.stringify(box));
    // Squeezed onto one line, everything fits and nothing can be typed into: the box keeps a width a name fits in.
    if (box.width < 200) throw new Error(`the find box is squeezed to ${Math.round(box.width)}px`);
    await phone.screenshot({ path: OUT + 'hours-report-phone.png' });
    await phone.context().close();
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
