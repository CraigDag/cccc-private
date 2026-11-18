// Elsewhere: a class chosen on the plan's day shades the times its people, groups and space are elsewhere,
// the same on every row, and lists them beside it, never its own time. Dragging it over a shaded stretch says
// what it would run into, over a clear one says nothing, and letting go where it was changes nothing; let go
// over one, it moves all the same and the words go, Undo putting it back. A line
// chooses the class in the way. A space's other class is shaded on rows that are not spaces, and said while
// dragging on rows that are. A person's week as rows of days shades every day, Thursday all of it while Ada
// is away. A coach reads it all and drags nothing; it fits a phone. Best on a preview started with AT=10:15.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + (e && e.message ? e.message.split('\n')[0] : e)); } }

async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport, reducedMotion: 'reduce' })).newPage();
  page.setDefaultTimeout(15000);
  page.on('pageerror', e => log('     page error (' + who + '): ' + e.message));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}

const squash = s => s.replace(/\s+/g, ' ').trim();
const hm = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
const minutes = s => s === '24:00' ? 1440 : +s.slice(0, 2) * 60 + +s.slice(3, 5);

// The plan on this week's Monday, the day of the sample's two-person Acrobatics in the Big Top.
async function toMonday(page) {
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=timeline]');
  const monday = await page.locator('[data-testid=day-chip]').first();
  await monday.click();
  await page.waitForSelector('[data-testid=day-chip][aria-current=date] >> text=MON');
  return page.getAttribute('[data-testid=timeline]', 'data-day');
}

// Choosing a class: the side panel's Elsewhere is drawn only by the render that has it chosen.
async function choose(page, block) {
  const title = await block.getAttribute('data-title');
  await block.click();
  await page.waitForFunction(t => document.querySelector('[data-testid=inspector-title]')?.textContent === t
    && document.querySelector('[data-testid=inspector-elsewhere]'), title);
  return title;
}

// What the side panel lists, as a person reads it.
const lines = page => page.$$eval('[data-testid=elsewhere-line]', ls => ls.map(l => ({
  when: l.querySelector('.ie-when').textContent.trim(), what: l.querySelector('.ie-what').textContent.trim(),
  who: l.querySelector('.ie-who').textContent.trim(), away: l.classList.contains('away'),
})));
// The shading on each row, as minutes.
const bands = page => page.$$eval('.tl-row[data-row]', rows => rows.map(r => ({
  row: r.dataset.row, name: r.querySelector('.tl-name')?.textContent.trim(),
  bands: [...r.querySelectorAll('[data-testid=busy-band]')].map(b => ({ from: +b.dataset.from, until: +b.dataset.until, away: b.classList.contains('away'), title: b.title })),
})));
const frame = async page => {
  const tl = page.locator('[data-testid=timeline]');
  return { start: +await tl.getAttribute('data-start'), end: +await tl.getAttribute('data-end') };
};
const covered = (bs, from, until) => bs.some(b => b.from <= from && until <= b.until);

// Drag the chosen class by a number of minutes (and onto another row), read what it says, and bring it back
// where it was before letting go, so nothing changes.
async function dragAndLook(page, block, steps, toRow, byEnd) {
  const f = await frame(page);
  const track = await page.locator('.tl-row[data-row] .tl-track').first().boundingBox();
  const perMinute = track.width / (f.end - f.start);
  const box = await block.boundingBox();
  const x0 = byEnd ? box.x + box.width - 3 : box.x + box.width / 2, y0 = box.y + box.height / 2;
  await page.mouse.move(x0, y0);
  await page.mouse.down();
  const said = [];
  for (const dm of steps) {
    let y = y0;
    if (toRow) { const r = await page.locator(`.tl-row[data-row="${toRow}"] .tl-track`).boundingBox(); y = r.y + r.height / 2; }
    for (let i = 1; i <= 6; i++) await page.mouse.move(x0 + dm * perMinute * i / 6, y0 + (y - y0) * i / 6);
    const tip = page.locator('[data-testid=drag-busy]');
    said.push({ dm, tip: await tip.count() ? squash(await tip.textContent()) : '', warn: await block.evaluate(b => b.classList.contains('over-busy')), when: await block.getAttribute('data-when') });
  }
  for (let i = 5; i >= 0; i--) await page.mouse.move(x0 + (said.at(-1).dm * perMinute) * i / 6, y0);
  await page.mouse.up();
  return said;
}

(async () => {
  const browser = await chromium.launch();
  const ed = await signIn(browser, 'editor', { width: 1300, height: 900 });
  const monday = await toMonday(ed);
  const acro = ed.locator('.tl-row[data-row]', { hasText: 'Big Top' }).locator('[data-testid=block][data-title=Acrobatics]').first();

  await step('choosing a class shades when its people and groups are elsewhere, the same on every row, and lists it beside it', async () => {
    await choose(ed, acro);
    const listed = await lines(ed);
    const rows = await bands(ed);
    const f = await frame(ed);
    listed.forEach(l => log('     ' + l.when + ' ' + l.what + ': ' + l.who));
    if (!listed.some(l => l.what === 'Circus disciplines' && l.who.includes('Ada Brightwater') && l.who.includes('BA 1')))
      throw new Error('Ada and BA 1 in Circus disciplines is not listed');
    if (!listed.some(l => l.what === 'Acrobatics' && l.who === 'Bo Lindqvist')) throw new Error("Bo's other Acrobatics is not listed");
    const first = JSON.stringify(rows[0].bands.map(b => [b.from, b.until]));
    if (!rows[0].bands.length) throw new Error('nothing is shaded');
    for (const r of rows) if (JSON.stringify(r.bands.map(b => [b.from, b.until])) !== first) throw new Error(r.name + ' is shaded differently: ' + JSON.stringify(r.bands));
    log('     shaded on all ' + rows.length + ' rows: ' + rows[0].bands.map(b => hm(b.from) + '–' + hm(b.until)).join(', '));
    // Every line is shaded, unless it is only the class's own space, which the space's row already shows.
    for (const l of listed) {
      if (/ (in use|booked)$/.test(l.who) && !l.who.includes(',')) continue;
      const [a, z] = l.when.split('–').map(minutes);
      if (!covered(rows[0].bands, Math.max(a, f.start), Math.min(z, f.end))) throw new Error(l.when + ' ' + l.what + ' is not shaded');
    }
    const s = +await acro.getAttribute('data-start'), e = +await acro.getAttribute('data-end');
    if (rows[0].bands.some(b => b.from < e && s < b.until)) throw new Error('its own time is shaded');
    await ed.screenshot({ path: OUT + 'elsewhere-chosen.png' });
  });

  await step('dragging it over a shaded stretch says what it runs into, over a clear one nothing, and letting it go where it was changes nothing', async () => {
    const s0 = +await acro.getAttribute('data-start');
    const id = await acro.getAttribute('data-id');
    // From 10:45: to 14:45, into Circus disciplines; then to 15:15, after it.
    const said = await dragAndLook(ed, acro, [14 * 60 + 45 - s0, 15 * 60 + 15 - s0]);
    said.forEach(x => log('     ' + x.when + ': ' + (x.tip || '(nothing said)')));
    const [into, after] = said;
    for (const w of ['Ada Brightwater busy', 'BA 1 busy', 'Big Top in use'])
      if (!into.tip.includes(w)) throw new Error('over 14:45 it says ' + JSON.stringify(into.tip));
    if (!into.warn) throw new Error('the class is not marked while over a busy stretch');
    if (after.tip || after.warn) throw new Error('over 15:15 it says ' + JSON.stringify(after.tip));
    if (await ed.locator('[data-testid=drag-busy]').count()) throw new Error('the words stay after letting go');
    await ed.waitForTimeout(400);
    const now = +await ed.locator(`[data-testid=block][data-id="${id}"]`).first().getAttribute('data-start');
    if (now !== s0) throw new Error('it moved from ' + hm(s0) + ' to ' + hm(now));
    if (!await ed.locator('[data-testid=undo]').isDisabled()) throw new Error('there is something to undo');
    // Another class dragged while this one is chosen is none of this one's business.
    const juggling = ed.locator('.tl-row[data-row]', { hasText: 'Studio A' }).locator('[data-testid=block][data-title=Juggling]').first();
    const other = (await dragAndLook(ed, juggling, [13 * 60 + 15 - +await juggling.getAttribute('data-start')]))[0];
    log('     Juggling, not chosen, at ' + other.when + ': ' + (other.tip || '(nothing said)'));
    if (other.tip) throw new Error('dragging Juggling says ' + JSON.stringify(other.tip));
    if (await ed.locator('[data-testid=inspector-title]').textContent() !== 'Acrobatics') throw new Error('dragging Juggling chose it');
    // Bounds advise, never refuse: let go over the busy stretch and it goes there, the words going with the
    // drag; Undo puts it back.
    const f = await frame(ed);
    const track = await ed.locator('.tl-row[data-row] .tl-track').first().boundingBox();
    const box = await acro.boundingBox();
    const x0 = box.x + box.width / 2, y0 = box.y + box.height / 2, dx = (14 * 60 + 45 - s0) * track.width / (f.end - f.start);
    await ed.mouse.move(x0, y0); await ed.mouse.down();
    for (let i = 1; i <= 6; i++) await ed.mouse.move(x0 + dx * i / 6, y0);
    const before = await ed.locator('[data-testid=drag-busy]').count();
    await ed.mouse.up();
    await ed.waitForFunction(id => document.querySelector(`[data-testid=block][data-id="${id}"]`)?.dataset.start === String(14 * 60 + 45), id);
    const left = await ed.locator('[data-testid=drag-busy]').count();
    const marked = await ed.locator(`[data-testid=block][data-id="${id}"].over-busy`).count();
    log('     let go at 14:45: it moved there; words before letting go ' + before + ', after ' + left);
    if (!before) throw new Error('nothing was said over 14:45 before letting go');
    if (left || marked) throw new Error('after letting go over a busy stretch the words stay (' + left + ') or it is still marked (' + marked + ')');
    await ed.click('[data-testid=undo]');
    await ed.waitForFunction(([id, s]) => document.querySelector(`[data-testid=block][data-id="${id}"]`)?.dataset.start === String(s), [id, s0]);
  });

  await step("a line chooses the class in the way, and the day is shaded for that one instead", async () => {
    await ed.locator('[data-testid=elsewhere-line] button.ie-what', { hasText: 'Circus disciplines' }).click();
    await ed.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Circus disciplines');
    await ed.waitForFunction(() => [...document.querySelectorAll('[data-testid=elsewhere-line] .ie-what')].some(w => w.textContent.trim() === 'Acrobatics'));
    const listed = await lines(ed);
    const acroLine = listed.find(l => l.what === 'Acrobatics' && l.when === '10:45–12:00');
    if (!acroLine || !acroLine.who.includes('Ada Brightwater')) throw new Error('Circus disciplines lists ' + JSON.stringify(listed));
    log('     ' + acroLine.when + ' ' + acroLine.what + ': ' + acroLine.who);
    await choose(ed, acro);
  });

  await step("a space's other class is shaded on rows that are groups, and said while dragging on rows that are spaces", async () => {
    // A rigging check in the Big Top at 15:30, for Prep, with nobody teaching: none of Acrobatics' people or groups.
    const f = await frame(ed);
    const row = ed.locator('.tl-row[data-row]', { hasText: 'Big Top' });
    const track = await row.locator('.tl-track').boundingBox();
    const x = m => track.x + (m - f.start) / (f.end - f.start) * track.width, y = track.y + track.height - 6;
    await ed.mouse.move(x(15 * 60 + 35), y); await ed.mouse.down();
    for (let i = 1; i <= 8; i++) await ed.mouse.move(x(15 * 60 + 35) + (x(16 * 60 + 25) - x(15 * 60 + 35)) * i / 8, y);
    await ed.mouse.up();
    await ed.waitForSelector('[data-testid=dialog]');
    await ed.fill('[data-testid=session-title]', 'Rigging check');
    await ed.selectOption('[data-testid=session-group]', { label: 'Prep' });
    await ed.click('[data-testid=dialog-ok]');
    await ed.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ed.waitForSelector('.tl-row[data-row]:has-text("Big Top") [data-testid=block][data-title="Rigging check"]');
    await choose(ed, acro);
    await ed.waitForFunction(() => [...document.querySelectorAll('[data-testid=elsewhere-line] .ie-what')].some(w => w.textContent.trim() === 'Rigging check'));
    const rig = (await lines(ed)).find(l => l.what === 'Rigging check');
    log('     ' + rig.when + ' ' + rig.what + ': ' + rig.who);
    if (rig.when !== '15:30–16:30' || rig.who !== 'Big Top in use') throw new Error('it lists ' + JSON.stringify(rig));
    const spaces = (await bands(ed))[0].bands;
    if (spaces.some(b => b.from < 990 && 930 < b.until)) throw new Error('the Big Top row already shows it, yet it is shaded on the spaces');
    const said = await dragAndLook(ed, acro, [15 * 60 + 30 - +await acro.getAttribute('data-start')]);
    log('     ' + said[0].when + ': ' + said[0].tip);
    if (said[0].tip !== 'Big Top in use') throw new Error('over the rigging check it says ' + JSON.stringify(said[0].tip));
    await ed.click('[data-testid=lens-groups]');
    await ed.waitForSelector('[data-testid=timeline] .tl-corner:text("Group")');
    await ed.waitForFunction(() => document.querySelectorAll('[data-testid=busy-band]').length > 0);
    const groups = (await bands(ed))[0].bands;
    if (!covered(groups, 930, 990)) throw new Error('on the groups the rigging check is not shaded: ' + JSON.stringify(groups));
    log('     groups shaded: ' + groups.map(b => hm(b.from) + '–' + hm(b.until)).join(', '));
    await ed.screenshot({ path: OUT + 'elsewhere-groups.png' });
    await ed.click('[data-testid=lens-spaces]');
    await ed.waitForSelector('[data-testid=timeline] .tl-corner:text("Space")');
  });

  await step("Ada's week as rows of days is shaded on every day, Thursday all of it, and dragging onto Thursday says she is away", async () => {
    await ed.keyboard.press('Escape');
    await ed.keyboard.press('Control+k');
    await ed.waitForSelector('[data-testid=find]');
    await ed.keyboard.type('Ada Brightwater');
    await ed.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent === 'Ada Brightwater');
    await ed.keyboard.press('Enter');
    await ed.waitForSelector('[data-testid=focus-chip]');
    await ed.waitForSelector(`.tl-row[data-row="day-${monday}"]`);
    const mine = ed.locator(`.tl-row[data-row="day-${monday}"] [data-testid=block][data-title=Acrobatics]`).first();
    await choose(ed, mine);
    const rows = await bands(ed);
    const f = await frame(ed);
    rows.forEach(r => log('     ' + r.name + ': ' + r.bands.map(b => hm(b.from) + '–' + hm(b.until) + (b.away ? ' away' : '')).join(', ')));
    const thursday = rows.find(r => r.name.startsWith('Thursday'));
    if (!thursday || thursday.bands.length !== 1 || !thursday.bands[0].away || thursday.bands[0].from !== f.start || thursday.bands[0].until !== f.end)
      throw new Error('Thursday is shaded ' + JSON.stringify(thursday));
    if (rows.filter(r => r.bands.length).length < 4) throw new Error('only ' + rows.filter(r => r.bands.length).length + ' days are shaded');
    const said = await dragAndLook(ed, mine, [0], thursday.row);
    log('     onto Thursday: ' + said[0].tip);
    if (!said[0].tip.includes('Ada Brightwater away')) throw new Error('onto Thursday it says ' + JSON.stringify(said[0].tip));
    await ed.screenshot({ path: OUT + 'elsewhere-week.png' });
    await ed.click('[data-testid=inspector-close]');
    await ed.waitForSelector('[data-testid=busy-band]', { state: 'detached' });
  });

  await step("somebody in only part of it is kept only from their part: carried by a move, left where it is by a stretch", async () => {
    await ed.click('[data-testid=lens-spaces]');
    await ed.waitForSelector('[data-testid=timeline] .tl-corner:text("Space")');
    await choose(ed, acro);
    // Ada in only the first half hour of Acrobatics, 10:45–11:15; Bo in all of it.
    await ed.click('[data-testid=inspector-edit]');
    await ed.waitForSelector('[data-testid=dialog]');
    await ed.locator('[data-testid=session-facilitator]', { hasText: 'Ada Brightwater' }).locator('input[aria-label="In the class until"]').fill('11:15');
    await ed.keyboard.press('Tab');
    await ed.click('[data-testid=dialog-ok]');
    await ed.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ed.waitForFunction(() => (document.querySelector('.blk.chosen')?.dataset.parts || '').includes(',675]'));
    const s0 = +await acro.getAttribute('data-start');
    // Moved to 12:15, her half hour is 12:15–12:45, before Circus disciplines at 13:00; Bo's whole is not.
    const moved = (await dragAndLook(ed, acro, [12 * 60 + 15 - s0]))[0];
    log('     moved to ' + moved.when + ': ' + moved.tip);
    if (moved.tip.includes('Ada') || !moved.tip.includes('Bo Lindqvist busy') || !moved.tip.includes('BA 1 busy')) throw new Error('moved, it says ' + JSON.stringify(moved.tip));
    // Stretched to 14:00, her half hour stays 10:45–11:15.
    const e0 = +await acro.getAttribute('data-end');
    const stretched = (await dragAndLook(ed, acro, [14 * 60 - e0], null, true))[0];
    log('     stretched to ' + stretched.when + ': ' + stretched.tip);
    if (!stretched.when.endsWith('14:00') || stretched.tip.includes('Ada') || !stretched.tip.includes('Bo Lindqvist busy')) throw new Error('stretched, it says ' + JSON.stringify(stretched));
    if (+await acro.getAttribute('data-start') !== s0 || +await acro.getAttribute('data-end') !== e0) throw new Error('it changed');
  });

  await step('a coach reads where the people of a class are and drags nothing', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await toMonday(coach);
    const theirs = coach.locator('.tl-row[data-row]', { hasText: 'Big Top' }).locator('[data-testid=block][data-title=Acrobatics]').first();
    await choose(coach, theirs);
    if (await theirs.getAttribute('data-move') !== '0') throw new Error('a coach may drag it');
    const listed = await lines(coach);
    if (!listed.length || !(await bands(coach))[0].bands.length) throw new Error('a coach is shown nothing');
    log('     ' + listed.length + ' lines for the coach');
    await coach.context().close();
  });

  await step('a phone: the list in the panel and the shading beneath it, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await toMonday(phone);
    const theirs = phone.locator('.tl-row[data-row]', { hasText: 'Big Top' }).locator('[data-testid=block][data-title=Acrobatics]').first();
    await choose(phone, theirs);
    if (!(await bands(phone))[0].bands.length) throw new Error('nothing is shaded');
    const fit = await phone.evaluate(() => {
      const list = document.querySelector('[data-testid=inspector-elsewhere]').getBoundingClientRect();
      return { page: document.documentElement.scrollWidth, width: innerWidth, left: list.left, right: list.right };
    });
    log('     ' + JSON.stringify(fit));
    if (fit.page > fit.width) throw new Error('the page scrolls sideways: ' + fit.page + ' > ' + fit.width);
    if (fit.left < 0 || fit.right > fit.width) throw new Error('the list is cut off');
    await phone.locator('[data-testid=inspector-elsewhere]').scrollIntoViewIfNeeded();
    await phone.screenshot({ path: OUT + 'elsewhere-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { console.error(e); process.exit(1); });
