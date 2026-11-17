const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
(async () => {
  const browser = await chromium.launch();
  const ctx = await browser.newContext({ viewport: { width: 1400, height: 900 }, acceptDownloads: true });
  const page = await ctx.newPage();
  const errors = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('pageerror', e => errors.push(e.message));
  const step = async (name, fn) => { try { await fn(); log('OK  ', name); } catch (e) { log('FAIL', name, e.message.split('\n')[0]); await page.screenshot({ path: `${OUT}fail-${name.replace(/\W+/g,'-')}.png` }); } };
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', 'admin');
  await page.fill('[data-testid=signin-passphrase]', 'northlight admin');
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=shell][data-ready], [data-testid=shell]');
  await page.waitForSelector('[data-testid=week-grid] [data-testid=block]');
  await page.waitForTimeout(800);

  await step('open a class by double-click', async () => {
    const b = page.locator('[data-testid=week-grid] [data-testid=block]').nth(3);
    await b.dblclick();
    await page.waitForSelector('[data-testid=dialog]');
    const t = await page.locator('[data-testid=dialog] h2').first().textContent();
    log('     dialog:', t);
    await page.click('[data-testid=dialog-x]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('choose a block, chosen bar appears', async () => {
    await page.locator('[data-testid=week-grid] [data-testid=block]').nth(5).click();
    await page.waitForSelector('[data-testid=chosen]');
    log('     chosen:', (await page.locator('[data-testid=chosen]').textContent()).replace(/\s+/g,' ').slice(0,120));
  });

  await step('drag a block down an hour', async () => {
    const b = page.locator('[data-testid=week-grid] [data-testid=block]').nth(5);
    const id = await b.getAttribute('data-id');
    const before = (await b.textContent()).replace(/\s+/g,' '); global.dragId = id; global.dragBefore = before;
    const box = await b.boundingBox();
    await page.mouse.move(box.x + box.width/2, box.y + 10);
    await page.mouse.down();
    for (let i = 1; i <= 10; i++) await page.mouse.move(box.x + box.width/2, box.y + 10 + i*6);
    await page.mouse.up();
    await page.waitForFunction(([id, before]) => { const e = document.querySelector(`[data-testid=block][data-id="${id}"]`); return e && e.textContent.replace(/\s+/g,' ') !== before; }, [id, before], { timeout: 5000 });
    log('     before:', before.slice(0,60)); log('     after: ', (await page.locator(`[data-testid=block][data-id="${id}"]`).textContent()).replace(/\s+/g,' ').slice(0,60));
  });

  await step('undo the drag', async () => {
    await page.click('[data-testid=undo]');
    await page.waitForFunction(([id, before]) => { const e = document.querySelector(`[data-testid=block][data-id="${id}"]`); return e && e.textContent.replace(/\s+/g,' ') === before; }, [global.dragId, global.dragBefore], { timeout: 5000 });
  });

  await step('Escape clears the choice and brings the bar back', async () => {
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=add-session]', { timeout: 4000 });
  });

  await step('add a class', async () => {
    const n = await page.locator('[data-testid=week-grid] [data-testid=block]').count();
    await page.click('[data-testid=add-session]');
    await page.waitForSelector('[data-testid=dialog]');
    await page.selectOption('[data-testid=session-class]', { index: 1 });
    await page.selectOption('[data-testid=session-group]', { index: 1 });
    await page.click('[data-testid=dialog-ok]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await page.waitForFunction(n => document.querySelectorAll('[data-testid=week-grid] [data-testid=block]').length > n, n);
    log('     blocks', n, '->', await page.locator('[data-testid=week-grid] [data-testid=block]').count());
  });

  for (const kind of ['The week as shown', 'The week as a list', 'Room grid, a page a day', 'A calendar per student']) {
    await step('print: ' + kind, async () => {
      await page.keyboard.press('Escape');
      await page.click('[data-testid=print-menu]');
      const href = await page.locator('.menu a', { hasText: kind }).getAttribute('href');
      const r = await page.request.get(B + '/' + href.replace(/^\//,''));
      const body = await r.body();
      log('     ', r.status(), r.headers()['content-type'], body.length, 'bytes', r.headers()['content-disposition']);
      if (r.status() !== 200 || body.slice(0,4).toString() !== '%PDF') throw new Error('not a pdf');
      require('fs').writeFileSync(OUT + kind.replace(/\W+/g,'-') + '.pdf', body);
      await page.keyboard.press('Escape');
      await page.click('[data-testid=week-page] h1, body', { position: { x: 5, y: 5 } }).catch(()=>{});
    });
  }

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
    await page.goto(B + '/registers?week=2026-11-16');
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
})();
