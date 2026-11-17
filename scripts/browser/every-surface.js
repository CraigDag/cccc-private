const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push(e.message));
  let failed = 0;
  const step = async (name, fn) => { try { await fn(); log('OK  ', name); } catch (e) { failed++; log('FAIL', name, e.message.split('\n')[0]); await page.screenshot({ path: `${OUT}fail-${name.replace(/\W+/g,'-')}.png` }); } };
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', 'admin');
  await page.fill('[data-testid=signin-passphrase]', 'northlight admin');
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  await page.waitForTimeout(800);

  await step('Now shows every space', async () => {
    const n = await page.locator('[data-testid=space-card]').count();
    log('     spaces', n);
    if (n === 0) throw new Error('no spaces on Now');
  });

  await step('open the plan on Monday', async () => {
    await page.click('[data-testid=nav-plan]');
    await page.waitForSelector('[data-testid=timeline]');
    await page.locator('[data-testid=day-chip]').first().click();
    await page.waitForFunction(() => document.querySelector('[data-testid=day-chip][aria-current=date]') && document.querySelectorAll('[data-testid=timeline] [data-testid=block]').length > 0);
  });

  await step('open a class by double-click', async () => {
    await page.locator('[data-testid=timeline] [data-testid=block]').nth(1).dblclick();
    await page.waitForSelector('[data-testid=dialog]');
    log('     drawer:', await page.locator('[data-testid=dialog] h2').first().textContent());
    await page.click('[data-testid=dialog-x]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('choose a block, the side panel opens', async () => {
    const b = page.locator('[data-testid=timeline] [data-testid=block]').nth(2);
    const title = await b.getAttribute('data-title');
    await b.click();
    await page.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t, title);
    log('     inspector:', title);
  });

  await step('drag a block an hour later', async () => {
    const b = page.locator('[data-testid=timeline] [data-testid=block][data-move="1"]').first();
    const id = await b.getAttribute('data-id'); const before = await b.getAttribute('data-start');
    global.dragId = id; global.dragBefore = before;
    const tl = page.locator('[data-testid=timeline]');
    const span = (+await tl.getAttribute('data-end')) - (+await tl.getAttribute('data-start'));
    const track = await page.locator('.tl-row[data-row] .tl-track').first().boundingBox();
    const hour = track.width / span * 60;
    const box = await b.boundingBox();
    await page.mouse.move(box.x + box.width / 2, box.y + box.height / 2);
    await page.mouse.down();
    for (let i = 1; i <= 10; i++) await page.mouse.move(box.x + box.width / 2 + i * hour / 10, box.y + box.height / 2);
    await page.mouse.up();
    await page.waitForFunction(([id, before]) => { const e = document.querySelector(`[data-testid=block][data-id="${id}"]`); return e && e.dataset.start !== before; }, [id, before], { timeout: 5000 });
    log('     start', before, '->', await page.locator(`[data-testid=block][data-id="${id}"]`).getAttribute('data-start'));
  });

  await step('undo the drag', async () => {
    await page.click('[data-testid=undo]');
    await page.waitForFunction(([id, before]) => { const e = document.querySelector(`[data-testid=block][data-id="${id}"]`); return e && e.dataset.start === before; }, [global.dragId, global.dragBefore], { timeout: 5000 });
  });

  await step('Escape closes the side panel', async () => {
    await page.click('[data-testid=plan-title]');
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=inspector]', { state: 'detached', timeout: 4000 });
  });

  await step('add a class', async () => {
    const n = await page.locator('[data-testid=timeline] [data-testid=block]').count();
    await page.click('[data-testid=add-session]');
    await page.waitForSelector('[data-testid=dialog]');
    await page.selectOption('[data-testid=session-class]', { index: 1 });
    await page.selectOption('[data-testid=session-group]', { index: 1 });
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=timeline] [data-testid=block]').length > n, n);
    log('     blocks', n, '->', await page.locator('[data-testid=timeline] [data-testid=block]').count());
  });

  await step('turn the lens: groups, people, the week', async () => {
    await page.click('[data-testid=lens-groups]');
    await page.waitForSelector('[data-testid=plan-page][data-lens=Groups]');
    await page.click('[data-testid=lens-people]');
    await page.waitForSelector('[data-testid=plan-page][data-lens=People]');
    await page.click('[data-testid=scale-week]');
    await page.waitForSelector('[data-testid=week-matrix]');
    log('     week chips', await page.locator('[data-testid=week-matrix] [data-testid=block]').count());
    await page.click('[data-testid=scale-day]');
    await page.click('[data-testid=lens-spaces]');
    await page.waitForSelector('[data-testid=plan-page][data-lens=Spaces][data-scale=Day]');
  });

  for (const kind of ['As a calendar', 'As a list', 'Spaces, a page a day', 'A calendar per student']) {
    await step('print: ' + kind, async () => {
      await page.click('[data-testid=more-menu]');
      const href = await page.locator('.menu a', { hasText: kind }).getAttribute('href');
      const r = await page.request.get(B + '/' + href.replace(/^\//,''));
      const body = await r.body();
      log('     ', r.status(), r.headers()['content-type'], body.length, 'bytes', r.headers()['content-disposition']);
      if (r.status() !== 200 || body.slice(0,4).toString() !== '%PDF') throw new Error('not a pdf');
      require('fs').writeFileSync(OUT + kind.replace(/\W+/g,'-') + '.pdf', body);
      await page.click('[data-testid=more-menu]');
    });
  }

  await step('find a person and see their week', async () => {
    await page.keyboard.press('Control+k');
    await page.waitForSelector('[data-testid=find]');
    await page.keyboard.type('Cleo');
    await page.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith('Cleo'));
    await page.keyboard.press('Enter');
    await page.waitForSelector('[data-testid=focus-chip]');
    log('     ', await page.locator('[data-testid=plan-title]').textContent(), '·', await page.locator('[data-testid=timeline] [data-testid=row]').count(), 'rows');
    await page.click('[data-testid=focus-chip] button');
    await page.waitForSelector('[data-testid=focus-chip]', { state: 'detached' });
  });

  await step('add a room in Data', async () => {
    await page.goto(B + '/data');
    await page.waitForSelector('[data-testid=data-row]');
    const n = await page.locator('[data-testid=data-row]').count();
    await page.click('[data-testid=data-add]');
    await page.fill('[data-testid=room-name]', 'Loft');
    await page.click('[data-testid=dialog-ok]');
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=data-row]').length > n, n);
  });

  await step('every data kind opens its add form', async () => {
    for (const k of ['programmes','groups','students','facilitators','classes','areas','disciplines','time-rules','semesters','absences']) {
      await page.goto(B + '/data/' + k);
      await page.waitForSelector('[data-testid=data-add], [data-testid=data-list]');
      const add = page.locator('[data-testid=data-add]');
      if (await add.count() === 0) { log('     no add on', k); continue; }
      await add.first().click();
      await page.waitForSelector('[data-testid=dialog]', { timeout: 4000 });
      await page.click('[data-testid=dialog-x]');
      await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    }
  });

  await step('take a missing register', async () => {
    await page.goto(B + '/registers');
    await page.waitForSelector('[data-testid=registers-table]');
    const row = page.locator('[data-testid=register-line]', { hasText: 'Missing' }).first();
    await row.locator('[data-testid=register-open]').click();
    await page.waitForSelector('[data-testid=dialog]');
    await page.click('[data-testid=register-all-present]');
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('download the project', async () => {
    await page.goto(B + '/admin/projects');
    const href = await page.locator('[data-testid=project-download]').first().getAttribute('href');
    const r = await page.request.get(B + '/' + href.replace(/^\//,''));
    log('     ', r.status(), r.headers()['content-disposition'], (await r.body()).length);
  });

  log('errors', JSON.stringify(errors));
  await browser.close();
  if (errors.length) failed++;
  console.log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
