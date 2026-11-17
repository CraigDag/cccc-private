// A student's week, counted: Reports ▸ Student hours against the school's weekly target, a student's
// figure on their week in the plan, and the lines behind each, the ones that count adding up and the
// others saying why. The made-up school's target is 15h: BA students meet it, Prep students fall short,
// and the visiting company is not measured. An administrator's; a coach is shown no student's hours.
// Against a fresh preview (made-up data).
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
const minutes = s => { const t = s.replace(/[()+]/g, '').trim(); const h = /(\d+)h/.exec(t), m = /(\d+)m/.exec(t); return (h ? +h[1] * 60 : 0) + (m ? +m[1] : 0); };
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
  // The drawer's reading of itself: the counted lines add up to the total it says.
  const drawer = async p => {
    await p.waitForSelector('[data-testid=dialog] [data-testid=student-lines-total]');
    const total = minutes(await p.textContent('[data-testid=student-lines-total]'));
    const counted = (await p.locator('[data-testid=student-line][data-counted="1"] .hl-len').allTextContents()).map(minutes);
    const off = await p.locator('[data-testid=student-line][data-counted="0"]').count();
    const whys = await p.locator('[data-testid=student-line-why]').allTextContents();
    const target = await p.locator('[data-testid=student-lines-target]').count() ? (await p.textContent('[data-testid=student-lines-target]')).trim() : null;
    return { total, sum: counted.reduce((a, b) => a + b, 0), counted: counted.length, off, whys, target,
             title: (await p.textContent('[data-testid=dialog] h2')).trim(), span: (await p.textContent('[data-testid=student-lines-span]')).trim() };
  };
  const adds = d => { if (d.sum !== d.total) throw new Error(`the counted lines add up to ${d.sum} minutes, the total says ${d.total}`);
                      if (d.whys.length !== d.off) throw new Error(`${d.off} lines do not count and ${d.whys.length} say why`); };
  const close = async p => { await p.click('[data-testid=dialog-close]'); await p.waitForSelector('[data-testid=dialog]', { state: 'detached' }); };
  const row = (p, name) => p.locator(`[data-testid=student-hours-row][data-name="${name}"]`);
  let thisWeek, weekLabel;

  page = await signIn('admin', { width: 1400, height: 900 });

  await step('Reports ▸ Student hours: a week that meets the target opens the classes behind it, and they add up', async () => {
    await page.goto(B + '/reports/students');
    await page.waitForSelector('[data-testid=student-hours]');
    const heads = (await page.locator('[data-testid=student-hours] thead th').allTextContents()).map(s => s.trim());
    thisWeek = heads.length - 3;          // Student, weeks…, Total, A week
    weekLabel = heads[thisWeek].replace('Wk ', '');
    const cell = row(page, 'Alba Ashcombe').locator('td').nth(thisWeek);
    if (await cell.getAttribute('data-short') !== '0') throw new Error('a 16h week is marked short of 15h');
    const text = (await cell.locator('[data-testid=student-hours-cell]').textContent()).trim();
    await cell.locator('[data-testid=student-hours-cell]').click();
    const d = await drawer(page);
    log('     ', d.title, '·', d.span, '·', d.counted, 'counted,', d.off, 'not ·', d.target);
    if (d.title !== 'Alba Ashcombe’s hours') throw new Error('the drawer is called ' + d.title);
    if (!d.span.startsWith('Week ' + weekLabel)) throw new Error(`pressed week ${weekLabel}, the drawer says "${d.span}"`);
    if (d.total !== minutes(text)) throw new Error(`pressed ${text}, the drawer totals ${d.total} minutes`);
    adds(d);
    if (d.target !== 'meets the 15h target') throw new Error('the target line says ' + d.target);
    await close(page);
  });

  await step('a Prep student short of the target: marked, and the class the study day closed says why', async () => {
    const cell = row(page, 'Quinn Quist').locator('td').nth(thisWeek);
    if (await cell.getAttribute('data-short') !== '1') throw new Error('an 8h 30m week is not marked short of 15h');
    const off = (await cell.locator('[data-testid=student-hours-off]').textContent()).trim();
    if (off !== '−1') throw new Error('the cell says ' + off + ' did not count');
    await cell.locator('[data-testid=student-hours-cell]').click();
    const d = await drawer(page);
    log('     ', d.title, '·', d.counted, 'counted,', d.off, 'not', '(' + d.whys.join(', ') + ') ·', d.target);
    adds(d);
    if (d.target !== `${Math.floor((900 - d.total) / 60)}h ${(900 - d.total) % 60}m short of the 15h target`.replace(' 0m', '')) throw new Error('the target line says ' + d.target);
    if (d.whys.join() !== 'Closed: Prep study day') throw new Error('the lines that did not count say ' + d.whys.join(', '));
    await page.click('[data-testid=student-lines-show] button:has-text("Not counted")');
    await page.waitForFunction(() => document.querySelectorAll('[data-testid=student-line]').length === 1);
    if (await page.getAttribute('[data-testid=student-line]', 'data-counted') !== '0') throw new Error('Not counted shows a line that counts');
    await page.waitForTimeout(300);
    await page.screenshot({ path: OUT + 'student-lines-short.png' });
    await close(page);
  });

  await step('the visiting company is never measured against the target', async () => {
    const cells = row(page, 'Wren Wolde').locator('td[data-short]');
    if (await cells.count() < 2) throw new Error('no week cells on the row');
    if ((await cells.evaluateAll(es => es.filter(e => e.dataset.short === '1').length)) > 0) throw new Error('a visitor\'s week is marked short');
    await row(page, 'Wren Wolde').locator('td').nth(thisWeek).locator('[data-testid=student-hours-cell]').click();
    const d = await drawer(page);
    adds(d);
    if (d.target !== 'no target for a visiting programme') throw new Error('the target line says ' + d.target);
    await close(page);
  });

  await step('the Total opens every week chosen, and the target counts only the weeks it measures', async () => {
    await row(page, 'Alba Ashcombe').locator('[data-testid=student-hours-total]').click();
    let d = await drawer(page);
    log('     ', d.span, '·', d.counted, 'counted ·', d.target);
    adds(d);
    if (await page.locator('.hl-week').count() < 2) throw new Error('fewer than two weeks are headed');
    const had = await row(page, 'Alba Ashcombe').locator('td[data-short] [data-testid=student-hours-cell]').count();
    if (d.target !== `meets the 15h target in all ${had} weeks it measures`) throw new Error(`${had} weeks have hours and the target line says ${d.target}`);
    await close(page);
    await row(page, 'Quinn Quist').locator('[data-testid=student-hours-total]').click();
    d = await drawer(page);
    log('     ', d.counted, 'counted,', d.off, 'not ·', d.target);
    adds(d);
    const marked = await row(page, 'Quinn Quist').locator('td[data-short="1"]').count();
    if (d.target !== `short of the 15h target in ${marked} of the ${marked} weeks it measures`) throw new Error(`${marked} weeks are marked and the target line says ${d.target}`);
    await close(page);
    await page.screenshot({ path: OUT + 'student-hours.png', fullPage: true });
  });

  await step('the overview counts the students short this week, the ones the report marks', async () => {
    const marked = await page.locator('[data-testid=student-hours-row]').evaluateAll((rows, i) => rows.filter(r => r.querySelectorAll('td')[i].dataset.short === '1').length, thisWeek);
    await page.goto(B + '/reports');
    const said = +(await page.textContent('[data-testid=students-short]')).trim();
    if (said !== marked || marked !== 6) throw new Error(`the overview says ${said} short, the report marks ${marked} this week, and the made-up school has 6 Prep students`);
    await page.click('a[href="reports/students"]');
    await page.waitForSelector('[data-testid=student-hours]');
  });

  const focus = async (p, name) => {
    await p.goto(B + '/plan');
    await p.waitForSelector('[data-testid=plan-page]');
    await p.keyboard.press('Control+k');
    await p.waitForSelector('[data-testid=find]');
    await p.keyboard.type(name);
    await p.waitForFunction(n => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith(n), name);
    await p.keyboard.press('Enter');
    await p.waitForFunction(n => document.querySelector('[data-testid=focus-chip]')?.textContent.includes(n), name);
  };

  await step('a student\'s week on the plan says their hours against the target, and opens the same lines', async () => {
    for (const [name, says] of [['Alba', /^16h this week, meets the 15h target$/], ['Quinn', /^8h 30m this week, short of the 15h target$/], ['Wren', /^4h this week$/]]) {
      await focus(page, name);
      const said = (await page.textContent('[data-testid=focus-hours]')).trim();
      if (!says.test(said)) throw new Error(`${name}'s week says "${said}"`);
      await page.click('[data-testid=focus-hours]');
      const d = await drawer(page);
      if (minutes(said.split(' this week')[0]) !== d.total) throw new Error(`the week says "${said}", the drawer ${d.total} minutes`);
      adds(d);
      await close(page);
    }
  });

  await step('a coach looking at a student\'s week is shown no hours', async () => {
    page = await signIn('coach', { width: 1400, height: 900 });
    await focus(page, 'Alba');
    if (await page.locator('[data-testid=focus-hours]').count()) throw new Error('the coach is shown their hours');
  });

  await step('on a phone the report scrolls inside itself and the lines fit the screen', async () => {
    page = await signIn('admin', { width: 390, height: 844 });
    await page.goto(B + '/reports/students');
    await page.waitForSelector('[data-testid=student-hours]');
    const wide = await page.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
    if (wide[0] > wide[1]) throw new Error(`the page is ${wide[0]}px wide on a ${wide[1]}px screen`);
    await row(page, 'Quinn Quist').locator('[data-testid=student-hours-cell]').last().scrollIntoViewIfNeeded();
    await row(page, 'Quinn Quist').locator('[data-testid=student-hours-cell]').last().click();
    await drawer(page);
    // The drawer slides in from the right: measure it where it comes to rest, not on its way.
    await page.waitForFunction(() => document.querySelector('[data-testid=dialog]').getAnimations({ subtree: true }).every(a => a.playState === 'finished'));
    const box = await page.locator('[data-testid=dialog]').boundingBox();
    if (box.x < 0 || box.x + box.width > 390.5) throw new Error(`the drawer runs from ${box.x} to ${box.x + box.width}`);
    const rows = await page.locator('[data-testid=student-line]').evaluateAll(es => es.map(e => [e.getBoundingClientRect().right, e.scrollWidth - e.clientWidth]));
    if (rows.some(([r, over]) => r > 390.5 || over > 1)) throw new Error('a line runs off the screen: ' + JSON.stringify(rows));
    await page.waitForTimeout(300);
    await page.screenshot({ path: OUT + 'student-lines-phone.png' });
  });

  if (errors.length) { failed++; log('FAIL browser errors:', errors.slice(0, 3).join(' | ')); }
  log(failed ? `${failed} failed` : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
