// A facilitator's semester for their phone: a coach downloads their own from My week, an administrator
// downloads one coach's from the plan, and after a class of theirs is deleted the next file still names
// it, as cancelled, so a phone that imports it takes the old entry away.
const { chromium } = require('playwright');
const fs = require('fs');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }

async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport, acceptDownloads: true })).newPage();
  page.on('pageerror', e => log('     page error: ' + e.message));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}

// Presses what downloads the file, and gives back its name and text. The page stays where it was: a
// link the app routed itself would leave the address bar on the file, and a reload would fetch it again.
async function download(page, press) {
  const at = page.url();
  const [d] = await Promise.all([page.waitForEvent('download', { timeout: 15000 }), press()]);
  const text = fs.readFileSync(await d.path(), 'utf8');
  await page.waitForTimeout(500);
  if (page.url() !== at) throw new Error(`the address moved from ${at} to ${page.url()}`);
  return { name: d.suggestedFilename(), text };
}

// Each VEVENT as its properties, folded lines put back together.
function events(text) {
  const lines = [];
  for (const raw of text.split('\r\n')) { if (raw.startsWith(' ')) lines[lines.length - 1] += raw.slice(1); else lines.push(raw); }
  const found = [];
  let e = null;
  for (const l of lines) {
    if (l === 'BEGIN:VEVENT') e = {};
    else if (l === 'END:VEVENT') { found.push(e); e = null; }
    else if (e) { const at = l.indexOf(':'); e[l.slice(0, at)] = l.slice(at + 1); }
  }
  return found;
}

function wellFormed(text) {
  if (!text.startsWith('BEGIN:VCALENDAR\r\nVERSION:2.0\r\n') || !text.endsWith('END:VCALENDAR\r\n')) throw new Error('not a calendar file: ' + JSON.stringify(text.slice(0, 40)));
  if (/[^\r]\n/.test(text)) throw new Error('a line ends without CR');
  const long = text.split('\r\n').find(l => Buffer.byteLength(l, 'utf8') > 75);
  if (long) throw new Error(`a line of ${Buffer.byteLength(long, 'utf8')} octets: ${long}`);
}

(async () => {
  // A container with no locale makes Chromium name a file with an Ø in it "download".
  const browser = await chromium.launch({ env: { ...process.env, LC_ALL: 'C.UTF-8', LANG: 'C.UTF-8' } });

  await step("a coach's own semester from My week: every class at their times, and the day they are away cancelled", async () => {
    const page = await signIn(browser, 'coach', { width: 1400, height: 900 });
    await page.goto(B + '/my-week');
    const button = page.locator('[data-testid=phone-calendar]');
    await button.waitFor();
    const note = await button.getAttribute('title');
    if (!/download it again after the timetable changes/.test(note || '')) throw new Error('the button does not say the file keeps no link: ' + note);
    const { name, text } = await download(page, () => button.click());
    wellFormed(text);
    fs.writeFileSync(OUT + 'phone-coach.ics', text);
    if (name !== 'Ada Brightwater, Autumn semester.ics') throw new Error('named ' + name);
    if (!text.includes('X-WR-CALNAME:Ada Brightwater\\, Autumn semester\r\n')) throw new Error('the calendar is not named for her');
    const all = events(text);
    const classes = all.filter(e => !e.UID.split('@')[0].includes('-'));
    const away = classes.filter(e => e.STATUS === 'CANCELLED' && (e.DESCRIPTION || '').includes('You are away then.'));
    if (classes.length < 10) throw new Error(`only ${classes.length} classes`);
    if (away.length !== 1) throw new Error(`${away.length} classes say she is away; the sample has her away one Thursday`);
    if (all.some(e => e.SUMMARY === 'Budget review')) throw new Error("an administrators' meeting is in a coach's file");
    // Her classes and no others: the file's are the ones My week lists for her (the sample's weeks repeat).
    const listed = new Set((await page.locator('[data-testid=day-row] .day-what b').allTextContents()).map(s => s.trim()));
    const inFile = new Set(classes.map(e => e.SUMMARY.replace(/^Provisional: /, '')));
    if (listed.size === 0 || [...inFile].sort().join('|') !== [...listed].sort().join('|'))
      throw new Error(`the file has ${[...inFile].join(', ')}; My week lists ${[...listed].join(', ')}`);
    log('     ', name, '·', classes.length, 'classes,', all.length - classes.length, 'event dates,', away.length, 'cancelled for being away');
    await page.context().close();
  });

  await step('on a phone, My week offers it and nothing scrolls sideways', async () => {
    const page = await signIn(browser, 'coach', { width: 390, height: 844 });
    await page.goto(B + '/my-week');
    const button = page.locator('[data-testid=phone-calendar]');
    await button.waitFor();
    const box = await button.boundingBox();
    if (!box || box.x < 0 || box.x + box.width > 390) throw new Error('the button is off the screen: ' + JSON.stringify(box));
    // A phone shows no tooltip, so the page says what the button does too.
    const said = await page.locator('[data-testid=phone-calendar-note]').textContent();
    if (!/download it again after the timetable changes/.test(said || '')) throw new Error('My week does not say the file keeps no link: ' + said);
    const wide = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (wide > 0) throw new Error(`the page is ${wide}px wider than the phone`);
    await page.screenshot({ path: OUT + 'phone-my-week.png' });
    await page.context().close();
  });

  await step("on the plan a coach is offered their own semester and nobody else's", async () => {
    const page = await signIn(browser, 'coach', { width: 1400, height: 900 });
    await page.goto(B + '/plan');
    await page.waitForSelector('[data-testid=more-menu]');
    for (const [who, offered] of [['Dag', false], ['Ada', true]]) {
      await page.keyboard.press('Control+k');
      await page.waitForSelector('[data-testid=find]');
      await page.keyboard.type(who);
      await page.waitForFunction(w => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith(w), who);
      await page.keyboard.press('Enter');
      await page.waitForFunction(w => document.querySelector('[data-testid=focus-chip]')?.textContent.startsWith(w), who);
      await page.click('[data-testid=more-menu]');
      // Counted once the menu is drawn, or the count is of the page before it opened.
      await page.locator('.menu a', { hasText: 'As a calendar' }).waitFor();
      const n = await page.locator('[data-testid=print-phone]').count();
      if ((n === 1) !== offered) throw new Error(`${who}'s semester ${n ? 'is' : 'is not'} offered to Ada's account`);
      await page.click('[data-testid=more-menu]');
      await page.locator('.menu a', { hasText: 'As a calendar' }).waitFor({ state: 'detached' });
    }
    await page.context().close();
  });

  const admin = await signIn(browser, 'admin', { width: 1400, height: 900 });
  let deletedUid = null, before = null, link = null;

  await step("an administrator takes one coach's from the plan, once the plan is narrowed to them", async () => {
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=more-menu]');
    await admin.click('[data-testid=more-menu]');
    // Counted once the menu is drawn, or the count is of the page before it opened.
    await admin.locator('.menu a', { hasText: 'As a calendar' }).waitFor();
    if (await admin.locator('[data-testid=print-phone]').count()) throw new Error('offered with nobody chosen');
    await admin.click('[data-testid=more-menu]');
    await admin.keyboard.press('Control+k');
    await admin.waitForSelector('[data-testid=find]');
    await admin.keyboard.type('Dag');
    await admin.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith('Dag'));
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=focus-chip]');
    await admin.click('[data-testid=more-menu]');
    const item = admin.locator('[data-testid=print-phone]');
    await item.waitFor();
    log('     ', (await item.textContent()).trim());
    const { name, text } = await download(admin, () => item.click());
    wellFormed(text);
    if (name !== 'Dag Øverlie, Autumn semester.ics') throw new Error('named ' + name);
    before = events(text);
    log('     ', name, '·', before.length, 'entries');
  });

  await step('a class of theirs deleted next week is in the next file as cancelled, at the time it had', async () => {
    // The new week's blocks are other classes: wait for the first one to be another, not for a title both weeks share.
    const thisWeeks = await admin.locator('[data-testid=block]').first().getAttribute('data-id');
    await admin.click('[data-testid=day-next]');
    await admin.waitForFunction(was => { const b = document.querySelector('[data-testid=block]'); return b && b.getAttribute('data-id') !== was; }, thisWeeks);
    const block = admin.locator('[data-testid=block]').first();
    const id = await block.getAttribute('data-id');
    const title = await block.getAttribute('data-title');
    deletedUid = id.replace(/-/g, '') + '@cccc-scheduler';
    const was = before.find(e => e.UID === deletedUid);
    if (!was) throw new Error(`the first file had no ${title}`);
    await block.click();
    await admin.waitForSelector('[data-testid=inspector-delete]');
    await admin.click('[data-testid=inspector-delete]');
    await admin.waitForFunction(i => !document.querySelector(`[data-testid=block][data-id="${i}"]`), id);
    await admin.click('[data-testid=more-menu]');
    link = await admin.locator('[data-testid=print-phone]').getAttribute('href');
    if (/Dag|Øverlie/.test(link)) throw new Error('the link names him: ' + link);
    const { text } = await download(admin, () => admin.locator('[data-testid=print-phone]').click());
    wellFormed(text);
    fs.writeFileSync(OUT + 'phone-after-delete.ics', text);
    const after = events(text);
    const gone = after.filter(e => e.UID === deletedUid);
    if (gone.length !== 1) throw new Error(`${gone.length} entries for the deleted ${title}`);
    const g = gone[0];
    if (g.STATUS !== 'CANCELLED' || g.SUMMARY !== 'No longer on your timetable') throw new Error(`the deleted ${title} reads ${g.SUMMARY} / ${g.STATUS}`);
    if (g.DTSTART !== was.DTSTART || g.DTEND !== was.DTEND) throw new Error(`at ${g.DTSTART}–${g.DTEND}, was ${was.DTSTART}–${was.DTEND}`);
    if (after.length !== before.length) throw new Error(`${after.length} entries now, ${before.length} before`);
    if (Number(g.SEQUENCE) < Number(was.SEQUENCE)) throw new Error('the sequence went backwards');
    log('     ', title, was.DTSTART, '→ cancelled, as', g.SUMMARY);
  });

  await step('erasing him forgets what his files carried: the next one cancels nothing it never had', async () => {
    if (!link || !deletedUid) throw new Error('nothing downloaded to forget');
    await admin.goto(B + '/admin/erase');
    const select = admin.locator('select', { has: admin.locator('optgroup[label=Facilitators]') });
    const option = select.locator('optgroup[label=Facilitators] option', { hasText: 'Dag Øverlie' });
    await option.waitFor({ state: 'attached' });
    await select.selectOption(await option.getAttribute('value'));
    await admin.fill('input[autocomplete=off]', 'Dag Øverlie');
    await admin.click('button.danger');
    await admin.waitForSelector('p.notice[role=status]');
    // He taught before today, so he is kept as a former facilitator and his file can still be taken.
    const r = await admin.request.get(B + link);
    if (r.status() !== 200) throw new Error('answered ' + r.status());
    const after = events((await r.body()).toString('utf8'));
    if (after.some(e => e.UID === deletedUid)) throw new Error('the class deleted before is still cancelled: his earlier files are remembered');
    if (after.some(e => e.SUMMARY === 'No longer on your timetable')) throw new Error('something gone is still cancelled');
    log('     ', after.length, 'entries, none of them remembered');
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
