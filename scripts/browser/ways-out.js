// Ways out of a clash: a class dragged into a space already in use on the sample week's Thursday shows the
// clash in its side panel and, under it, the ways out: two studios free at the same time, then another time
// in its new space. One press takes one, the clash goes, and Undo brings it back. The class it clashes with
// is a link to its own way out; its teacher is away all day, so it has no other time. Another time says where
// the class was. An editor is offered them too, a coach only reads the clash, and a phone fits. Best on a
// preview started with DAY=wed AT=10:15, the sample week's Wednesday, whatever day it is.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }

async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport, reducedMotion: 'reduce' })).newPage();
  page.setDefaultTimeout(15000);
  page.on('pageerror', e => { failed++; log('     page error (' + who + '): ' + e.message); });
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}

const squash = s => s.replace(/\s+/g, ' ').trim();
const hm = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
const toasts = async page => (await page.locator('[data-testid=toast]').allInnerTexts()).map(squash);

// The plan on the sample week's Thursday, in spaces.
async function toThursday(page) {
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=timeline]');
  await page.locator('[data-testid=day-chip]').nth(3).click();
  await page.waitForSelector('[data-testid=day-chip][aria-current=date] >> text=THU');
}

const row = (page, space) => page.locator('.tl-row[data-row]', { hasText: space });
const block = (page, space, title) => row(page, space).locator(`[data-testid=block][data-title="${title}"]`).first();

// Pressing the chosen class again lets it go, so only one not chosen is pressed; the new render marks it.
async function choose(page, b) {
  const title = await b.getAttribute('data-title'), id = await b.getAttribute('data-id');
  if (!await b.evaluate(x => x.classList.contains('chosen'))) await b.click();
  await page.waitForFunction(([t, id]) => document.querySelector('[data-testid=inspector-title]')?.textContent === t
    && document.querySelector(`[data-testid=block][data-id="${id}"]`)?.classList.contains('chosen'), [title, id]);
}

// The ways out as a person reads them: the button, then what is said beside it.
const ways = page => page.$$eval('[data-testid=way-out]', ls => ls.map(l => ({
  where: l.querySelector('[data-testid=way-take]').textContent.trim(),
  notes: [...l.querySelectorAll('[data-testid=way-note]')].map(n => (n.classList.contains('good') ? '✓ ' : '! ') + n.textContent.trim()),
})));
const said = ws => ws.map(w => w.where + (w.notes.length ? ' (' + w.notes.join('; ') + ')' : ''));

// Drag a class straight up or down onto another space's row at the same time, and let go.
async function dragToRow(page, b, space) {
  const box = await b.boundingBox();
  const target = await row(page, space).locator('.tl-track').boundingBox();
  const x = box.x + box.width / 2, y0 = box.y + box.height / 2, y1 = target.y + target.height / 2;
  await page.mouse.move(x, y0);
  await page.mouse.down();
  for (let i = 1; i <= 8; i++) await page.mouse.move(x, y0 + (y1 - y0) * i / 8);
  await page.mouse.up();
}

// Ways out are drawn by the render that has the class chosen in its clash; nothing else draws them.
const waysShown = (page, title) => page.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t
  && document.querySelectorAll('[data-testid=way-out]').length > 0, title);
const clashGone = (page, title) => page.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t
  && !document.querySelector('[data-testid=inspector-clash]') && !document.querySelector('[data-testid=ways-out]'), title);

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  // About the sample week before its Thursday: a preview on a later day says so, rather than failing on whatever today is.
  const day = (await admin.textContent('.now-head h1 span')).trim();
  if (/^(Thursday|Friday|Saturday|Sunday)/.test(day)) { log('FAIL the preview says it is ' + day + ': start it with DAY=wed AT=10:15 scripts/preview.sh'); await browser.close(); process.exit(1); }
  await toThursday(admin);
  let creationId;

  await step('a class dragged into a space in use shows the clash and, under it, the studios free then and another time in its new space', async () => {
    const creation = block(admin, 'Studio B', 'Creation lab');
    creationId = await creation.getAttribute('data-id');
    const start = +await creation.getAttribute('data-start');
    if (start !== 9 * 60) throw new Error("Thursday's Creation lab in Studio B starts at " + hm(start));
    if (await admin.locator('[data-testid=ways-out]').count()) throw new Error('ways out before anything clashes');
    await dragToRow(admin, creation, 'Big Top');
    await admin.waitForFunction(() => [...document.querySelectorAll('[data-testid=toast]')].some(t => t.textContent.includes('Moved Creation lab to Big Top')));
    await waysShown(admin, 'Creation lab');
    const clash = squash(await admin.locator('[data-testid=inspector-clash]').first().textContent());
    log('     ' + clash);
    if (!clash.includes('Space "Big Top"') || !clash.includes('Acrobatics')) throw new Error('the clash reads ' + JSON.stringify(clash));
    const listed = await ways(admin);
    said(listed).forEach(w => log('     ' + w));
    const expect = ['Studio B, same time (✓ Creation lab has been here before)', 'Studio A, same time (✓ Creation lab has been here before)', '12:45–15:45, same space'];
    if (JSON.stringify(listed.map(w => w.where)) !== JSON.stringify(expect.map(w => w.split(' (')[0])))
      throw new Error('the ways out are ' + JSON.stringify(said(listed)));
    if (JSON.stringify(said(listed).slice(0, 2)) !== JSON.stringify(expect.slice(0, 2))) throw new Error('the studios say ' + JSON.stringify(said(listed)));
    const ringed = await admin.$$eval('[data-testid=block].clash', bs => bs.map(b => b.dataset.title).sort());
    if (JSON.stringify(ringed) !== JSON.stringify(['Acrobatics', 'Creation lab'])) throw new Error('ringed: ' + JSON.stringify(ringed));
    const other = squash(await admin.locator('[data-testid=ways-other]').textContent());
    if (other !== 'Or move the other class instead: Acrobatics.') throw new Error('the other class reads ' + JSON.stringify(other));
    await admin.screenshot({ path: OUT + 'ways-out.png' });
  });

  await step('one press takes a way out, the clash goes, and Undo brings both back', async () => {
    await admin.locator('[data-testid=way-take]', { hasText: 'Studio A, same time' }).click();
    await clashGone(admin, 'Creation lab');
    if (!(await toasts(admin)).some(t => t.includes('Moved Creation lab to Studio A'))) throw new Error('the toast says ' + JSON.stringify(await toasts(admin)));
    const there = block(admin, 'Studio A', 'Creation lab');
    if (await there.getAttribute('data-id') !== creationId || +await there.getAttribute('data-start') !== 9 * 60) throw new Error('Creation lab is not in Studio A at 09:00');
    if (await block(admin, 'Big Top', 'Creation lab').count()) throw new Error('Creation lab is still in the Big Top');
    if (await admin.locator('[data-testid=block].clash').count()) throw new Error('something is still ringed as a clash');
    await admin.click('[data-testid=undo]');
    await admin.waitForFunction(id => !!document.querySelector(`.tl-row[data-row] [data-testid=block][data-id="${id}"]`)?.closest('.tl-row')?.textContent.includes('Big Top'), creationId);
    // Still chosen, back in its clash, with its ways out again.
    await choose(admin, block(admin, 'Big Top', 'Creation lab'));
    await waysShown(admin, 'Creation lab');
    if (await admin.locator('[data-testid=block].clash').count() !== 2) throw new Error('after Undo the clash is not ringed again');
    if ((await ways(admin)).length !== 3) throw new Error('after Undo the ways out are ' + JSON.stringify(said(await ways(admin))));
  });

  await step('the other class is a link to its own way out, and its teacher away all day leaves it no other time', async () => {
    await admin.click('[data-testid=ways-other-class]');
    await waysShown(admin, 'Acrobatics');
    const listed = await ways(admin);
    said(listed).forEach(w => log('     ' + w));
    // Ada is away on Thursday: another time would need her, and she is away all of it. Studio B is where
    // Acrobatics has been before, and the Gym has the crash mats it needs.
    if (JSON.stringify(listed.map(w => w.where)) !== JSON.stringify(['Studio B, same time', 'Gym, same time'])) throw new Error('Acrobatics is offered ' + JSON.stringify(said(listed)));
    if (squash(await admin.locator('[data-testid=ways-other]').textContent()) !== 'Or move the other class instead: Creation lab.') throw new Error('no way back to Creation lab');
    await admin.click('[data-testid=ways-other-class]');
    await waysShown(admin, 'Creation lab');
  });

  await step('another time moves it there with where it was, and Undo puts it back', async () => {
    await admin.locator('[data-testid=way-take]', { hasText: '12:45–15:45' }).click();
    await clashGone(admin, 'Creation lab');
    if (!(await toasts(admin)).some(t => t.includes('Moved Creation lab to 12:45'))) throw new Error('the toast says ' + JSON.stringify(await toasts(admin)));
    const moved = squash(await admin.locator('[data-testid=inspector-moved]').textContent());
    if (!/from Thu \d+ \w+, 09:00–12:00/.test(moved)) throw new Error('it says it moved ' + JSON.stringify(moved));
    const b = admin.locator(`[data-testid=block][data-id="${creationId}"]`).first();
    if (+await b.getAttribute('data-start') !== 12 * 60 + 45) throw new Error('it starts at ' + hm(+await b.getAttribute('data-start')));
    await admin.click('[data-testid=undo]');
    await admin.waitForFunction(id => document.querySelector(`[data-testid=block][data-id="${id}"]`)?.getAttribute('data-start') === String(9 * 60), creationId);
  });

  await step('an editor is offered the ways out too; a coach reads the clash and is offered none', async () => {
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await toThursday(editor);
    await choose(editor, block(editor, 'Big Top', 'Creation lab'));
    await waysShown(editor, 'Creation lab');
    if ((await ways(editor)).length !== 3) throw new Error('the editor is offered ' + JSON.stringify(said(await ways(editor))));
    await editor.context().close();
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await toThursday(coach);
    await choose(coach, block(coach, 'Big Top', 'Creation lab'));
    await coach.waitForSelector('[data-testid=inspector-clash]');
    if (await coach.locator('[data-testid=ways-out]').count()) throw new Error('a coach is offered ways out');
    await coach.context().close();
  });

  await step('a phone: the ways out fit the side panel, nothing scrolls sideways, and one press takes one', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await toThursday(phone);
    await choose(phone, block(phone, 'Big Top', 'Creation lab'));
    await waysShown(phone, 'Creation lab');
    await phone.locator('[data-testid=ways-out]').scrollIntoViewIfNeeded();
    const fit = await phone.evaluate(() => {
      const box = document.querySelector('[data-testid=ways-out]').getBoundingClientRect();
      const buttons = [...document.querySelectorAll('[data-testid=way-take]')].map(b => b.getBoundingClientRect());
      return { page: document.documentElement.scrollWidth, width: innerWidth, left: box.left, right: box.right,
        narrow: buttons.every(b => b.left >= box.left - 1 && b.right <= box.right + 1), tall: buttons.every(b => b.height >= 30) };
    });
    log('     ' + JSON.stringify(fit));
    if (fit.page > fit.width) throw new Error('the page scrolls sideways: ' + fit.page + ' > ' + fit.width);
    if (fit.left < 0 || fit.right > fit.width || !fit.narrow) throw new Error('the ways out are cut off');
    if (!fit.tall) throw new Error('a way out is too small to press');
    await phone.screenshot({ path: OUT + 'ways-out-phone.png' });
    await phone.locator('[data-testid=way-take]', { hasText: 'Studio B, same time' }).click();
    await clashGone(phone, 'Creation lab');
    await phone.click('[data-testid=undo]');
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
