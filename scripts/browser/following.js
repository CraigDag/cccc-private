// Following the current app, on a preview started with FOLLOW=1: the open timetable is the current app's,
// nothing in it is changed here (pages say so, forms only close), and a class cancelled over there is on
// Now within seconds, said to be by the current app. "Over there" is tools/OldFormatSchool writing the
// made-up school's file the way the current app saves it.
const { chromium } = require('playwright');
const { execFileSync } = require('child_process');
const path = require('path');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const ROOT = path.resolve(__dirname, '../..');
const FILE = process.env.FOLLOW_FILE || path.join(ROOT, '.local-follow-preview/School.cccc');
const overThere = (...a) => execFileSync('dotnet', ['run', '--no-build', '--project', path.join(ROOT, 'tools/OldFormatSchool'), '--', ...a], { stdio: 'pipe' }).toString();
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }
async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport })).newPage();
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}
(async () => {
  const browser = await chromium.launch();
  const page = await signIn(browser, 'admin', { width: 1400, height: 900 });
  const errors = [];
  page.on('pageerror', e => errors.push(e.message));

  await step('the school opens on the timetable it follows, and Now and the status bar say so', async () => {
    const badge = (await page.locator('[data-testid=now-page] [data-testid=following]').textContent({ timeout: 8000 })).trim();
    if (badge !== 'Follows the current app') throw new Error('the badge reads "' + badge + '"');
    const bar = (await page.locator('[data-testid=status-following]').textContent()).trim();
    if (!/Follows the current app/.test(bar)) throw new Error('the status bar reads "' + bar + '"');
    // The made-up school's own classes, not the preview's sample.
    const spaces = await page.locator('[data-testid=now-page]').textContent();
    if (!/Big Top/.test(spaces)) throw new Error('Now does not show the followed school\'s spaces');
  });

  await step('the plan offers nothing to change', async () => {
    await page.goto(B + '/plan');
    await page.waitForSelector('[data-testid=plan-page] [data-testid=following]');
    for (const id of ['add-session', 'new-event']) {
      if (await page.locator(`[data-testid=${id}]`).count()) throw new Error(id + ' is offered');
    }
  });

  await step('a class cancelled over there is on Now within seconds, by the current app', async () => {
    await page.goto(B + '/');
    await page.waitForSelector('[data-testid=now-page]');
    const before = await page.locator('[data-testid=change]').count();
    log('     ' + overThere('cancel', FILE, 'Aerial').trim());
    // No reload: the open page is drawn again when the change comes across.
    const row = page.locator('[data-testid=change]', { hasText: 'Aerial' });
    await row.first().waitFor({ timeout: 20000 });
    const text = (await row.first().textContent()).replace(/\s+/g, ' ').trim();
    if (!/Cancelled/.test(text) || !/the current app/.test(text)) throw new Error('the row reads "' + text + '"');
    if (await page.locator('[data-testid=change]').count() !== before + 1) throw new Error('more than the one class changed');
    log('      ' + text);
  });

  await step('People & places says where to change things, and its forms only close', async () => {
    await page.goto(B + '/data/rooms');
    await page.waitForSelector('[data-testid=data-list]');
    if (!(await page.locator('[data-testid=following]').count())) throw new Error('no notice on the page');
    if (await page.locator('[data-testid=data-add]').count()) throw new Error('Add is offered');
    await page.locator('[data-testid=data-row]').first().click();
    await page.waitForSelector('[data-testid=dialog] [data-testid=following]');
    if (await page.locator('[data-testid=dialog-ok]').count()) throw new Error('the form offers OK');
    if (await page.locator('[data-testid=dialog] .btn.danger').count()) throw new Error('the form offers Delete');
    await page.click('[data-testid=dialog-close]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a register is read here and taken in the current app', async () => {
    await page.goto(B + '/registers');
    await page.waitForSelector('[data-testid=registers-page] [data-testid=following], main [data-testid=following]');
    const card = page.locator('[data-testid=register-card], [data-testid=register-line]').first();
    await card.click();
    await page.waitForSelector('[data-testid=dialog] [data-testid=following]');
    if (await page.locator('[data-testid=dialog-ok]').count()) throw new Error('the register offers OK');
    await page.keyboard.press('Escape');
  });

  await step('Administration says it is following, and the followed timetable has no Delete', async () => {
    await page.goto(B + '/admin/system');
    const card = (await page.locator('[data-testid=follow-card]').textContent({ timeout: 8000 })).replace(/\s+/g, ' ');
    if (!/Looked today/.test(card) || !/Last brought in/.test(card)) throw new Error('the card reads "' + card.trim() + '"');
    await page.goto(B + '/admin/projects');
    await page.waitForSelector('[data-testid=projects] li');
    const row = page.locator('[data-testid=projects] li', { has: page.locator('[data-testid=project-following]') });
    if (await row.count() !== 1) throw new Error((await row.count()) + ' timetables say they follow the current app');
    // Open the made-up sample for everybody instead, so the followed one is not the open one either,
    // which on its own would hide Delete; then put it back.
    const other = page.locator('[data-testid=projects] li', { hasNot: page.locator('[data-testid=project-following]') }).first();
    await other.locator('[data-testid=project-open]').click();
    await row.locator('[data-testid=project-open]').waitFor();
    if (await row.locator('button', { hasText: 'Delete' }).count()) throw new Error('it can be deleted');
    if (!(await other.locator('button', { hasText: 'Delete' }).count() === 0)) throw new Error('the open one can be deleted');
    await page.screenshot({ path: path.join(process.env.OUT || '/tmp', 'following-projects.png') });
    await row.locator('[data-testid=project-open]').click();
    await row.locator('[data-testid=project-open]').waitFor({ state: 'detached' });
  });

  await step('somebody erased over there is offered for erasing from this app\'s history, and then gone from it', async () => {
    log('     ' + overThere('remove', FILE, 'Cleo').trim());
    await page.goto(B + '/admin/erase');
    await page.waitForSelector('[data-testid=following]');
    const option = page.locator('[data-testid=erase-former] option', { hasText: 'Cleo Vantor' });
    // Within the follow interval: reload until the change has come across.
    for (let i = 0; i < 10 && !(await option.count()); i++) { await page.waitForTimeout(2000); await page.reload(); await page.waitForSelector('[data-testid=following]'); }
    if (!(await option.count())) throw new Error('Cleo Vantor is not offered under "Taken off in the last 30 days"');
    if (await page.locator('select optgroup[label=Students]').count()) throw new Error('people still on the followed timetable are offered');
    await page.locator('select', { has: page.locator('[data-testid=erase-former]') }).selectOption(await option.getAttribute('value'));
    await page.fill('input[autocomplete=off]', 'Cleo Vantor');
    await page.click('button.danger');
    await page.waitForSelector('p.notice[role=status]');
    if (await page.locator('[data-testid=erase-former] option', { hasText: 'Cleo Vantor' }).count()) throw new Error('still offered after erasing');
  });

  await step('a coach on a phone sees the same: Now says it follows the current app', async () => {
    const phone = await signIn(browser, 'coach', { width: 390, height: 844 });
    const badge = (await phone.locator('[data-testid=now-page] [data-testid=following]').textContent({ timeout: 8000 })).trim();
    if (badge !== 'Follows the current app') throw new Error('the badge reads "' + badge + '"');
    await phone.screenshot({ path: path.join(process.env.OUT || '/tmp', 'following-phone.png') });
  });

  log('errors', JSON.stringify(errors));
  log(failed ? failed + ' failed' : 'all passed');
  await browser.close();
  process.exit(failed ? 1 : 0);
})();
