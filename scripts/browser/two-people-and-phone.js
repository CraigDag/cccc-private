const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = 'http://127.0.0.1:5299';
async function signIn(browser, user, pass, w, h) {
  const page = await (await browser.newContext({ viewport: { width: w, height: h } })).newPage();
  page.on('pageerror', e => console.log('pageerror', user, e.message));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', user);
  await page.fill('[data-testid=signin-passphrase]', pass);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=shell]');
  return page;
}
(async () => {
  const browser = await chromium.launch();
  const a = await signIn(browser, 'admin', 'northlight admin', 1400, 900);
  const e = await signIn(browser, 'editor', 'northlight editor', 1400, 900);
  await a.waitForSelector('[data-testid=week-grid] [data-testid=block]');
  await e.waitForSelector('[data-testid=week-grid] [data-testid=block]');
  await a.waitForTimeout(1000);
  const b = a.locator('[data-testid=week-grid] [data-testid=block]').nth(5);
  const id = await b.getAttribute('data-id');
  const before = (await e.locator(`[data-testid=block][data-id="${id}"]`).textContent()).replace(/\s+/g,' ');
  const box = await b.boundingBox();
  await a.mouse.move(box.x + box.width/2, box.y + 10); await a.mouse.down();
  for (let i = 1; i <= 10; i++) await a.mouse.move(box.x + box.width/2, box.y + 10 + i*6);
  await a.mouse.up();
  try {
    await e.waitForFunction(([id, before]) => { const x = document.querySelector(`[data-testid=block][data-id="${id}"]`); return x && x.textContent.replace(/\s+/g,' ') !== before; }, [id, before], { timeout: 5000 });
    console.log('OK   editor saw the admin\'s drag without reloading');
  } catch { console.log('FAIL editor did not see the drag'); }
  // editor's Undo should be disabled (nothing of theirs)
  console.log('     editor undo disabled:', await e.locator('[data-testid=undo]').isDisabled());
  console.log('     also here (editor):', (await e.locator('[data-testid=status-bar]').textContent()).replace(/\s+/g,' '));
  // phone forms
  const p = await signIn(browser, 'admin', 'northlight admin', 390, 844);
  await p.waitForSelector('[data-testid=day-list]');
  await p.locator('[data-testid=day-row]').first().click();
  await p.waitForTimeout(800);
  await p.screenshot({ path: '' + OUT + 'ph-session.png' });
  const dlg = await p.locator('[data-testid=dialog]').boundingBox();
  console.log('     phone dialog box', JSON.stringify(dlg));
  await p.goto(B + '/data/students'); await p.waitForSelector('[data-testid=data-add]');
  await p.screenshot({ path: '' + OUT + 'ph-students.png' });
  await p.click('[data-testid=data-add]'); await p.waitForSelector('[data-testid=dialog]'); await p.waitForTimeout(500);
  await p.screenshot({ path: '' + OUT + 'ph-student-form.png' });
  await p.goto(B + '/reports/facilitators'); await p.waitForTimeout(1200);
  await p.screenshot({ path: '' + OUT + 'ph-reports.png', fullPage: true });
  const sw = await p.evaluate(() => [document.documentElement.scrollWidth, innerWidth]);
  console.log('     reports page width vs window', sw);
  await browser.close();
})();
