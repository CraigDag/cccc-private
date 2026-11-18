// Swapping two classes from the side panel: Swap with… on a class that has not started, then the class to swap it
// with chosen on the plan, marked beside it. Each way of swapping them (their times, their places, or, on at once,
// their spaces) says where it takes them, and what it puts right, brings and leaves as it was, before it is taken.
// A class that has started says so and offers nothing; Esc stops swapping. Thursday's Acrobatics, whose teacher is
// away, swapped into Wednesday's Aerial's place puts the cover right, the plan shows where it went and where it was,
// and one Undo puts both back. An editor may swap, a coach is offered nothing, and a phone's sheet leaves more of the
// plan while the other class is chosen. Best on a preview started with DAY=wed AT=10:15, the sample week's Wednesday.
const { chromium } = require('playwright');
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const OUT = (process.env.OUT || '/tmp') + '/';
let failed = 0;
const log = (...a) => console.log(...a);
// The first line of what went wrong, with the line of Playwright's call log that names what it was waiting for.
const why = e => {
  const lines = String((e && e.message) || e).replace(/\x1b\[[0-9;]*m/g, '').split('\n');
  const wait = lines.slice(1).find(l => /waiting for/.test(l));
  return lines[0] + (wait ? ' (' + wait.trim().replace(/^- /, '') + ')' : '');
};
// FAILFAST=1 stops at the first step that fails, for breaking the screens on purpose one way after another.
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + why(e)); if (process.env.FAILFAST) process.exit(1); } }

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
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };
const DAYS = { Mon: 0, Tue: 1, Wed: 2, Thu: 3, Fri: 4 };

// The plan, on a day of the sample week, in spaces.
async function toDay(page, day) {
  if (!await page.locator('[data-testid=timeline]').count()) {
    await page.goto(B + '/plan');
    await page.waitForSelector('[data-testid=timeline]');
  }
  await page.locator('[data-testid=day-chip]').nth(DAYS[day]).click();
  await page.waitForFunction(i => document.querySelectorAll('[data-testid=day-chip]')[i]?.getAttribute('aria-current') === 'date', DAYS[day]);
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

// Swap with… pressed on the chosen class: the panel asks for the other class.
async function startSwap(page) {
  await page.click('[data-testid=inspector-swap]');
  await page.waitForSelector('[data-testid=swap-choose]');
}

// The other class pressed while swapping: the panel names it, and only the render that has it draws its ways.
async function swapWith(page, b) {
  const title = await b.getAttribute('data-title'), id = await b.getAttribute('data-id');
  await b.click();
  await page.waitForFunction(([t, id]) => document.querySelector('[data-testid=swap-with] b')?.textContent === t
    && document.querySelector(`[data-testid=block][data-id="${id}"]`)?.classList.contains('paired'), [title, id]);
}

// A class marked beside the one being swapped is outlined in the accent, as drawn rather than as a class name.
async function inAccent(locator, what) {
  const drawn = await locator.evaluate(x => {
    const probe = document.createElement('span');
    probe.style.color = 'var(--accent)';
    document.body.append(probe);
    const accent = getComputedStyle(probe).color;
    probe.remove();
    return { shadow: getComputedStyle(x).boxShadow, accent };
  });
  if (!drawn.shadow.includes(drawn.accent) || /inset/.test(drawn.shadow))
    throw new Error(what + ' is marked by its class alone: drawn ' + drawn.shadow + ', not outlined in the accent ' + drawn.accent);
}

// Each way as a person reads it: the button, where it takes them, then what is said under it.
const options = page => page.$$eval('[data-testid=swap-option]', ls => ls.map(l => ({
  kind: l.dataset.kind,
  take: l.querySelector('[data-testid=swap-take]').textContent.trim(),
  says: l.querySelector('[data-testid=swap-says]').textContent.trim(),
  clear: l.querySelector('[data-testid=swap-clear]')?.textContent.trim() ?? null,
  putsRight: [...l.querySelectorAll('[data-testid=swap-puts-right]')].map(n => n.textContent.trim()),
  brings: [...l.querySelectorAll('[data-testid=swap-brings]')].map(n => n.textContent.trim()),
  still: [...l.querySelectorAll('[data-testid=swap-still]')].map(n => n.textContent.trim()),
})));

// Something done, and the toast it says, newer than any before it.
async function said(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  // About the sample week's Wednesday morning: a preview on another day says so, rather than failing on whatever today is.
  const day = (await admin.textContent('.now-head h1 span')).trim();
  if (!/^Wednesday/.test(day)) { log('FAIL the preview says it is ' + day + ': start it with DAY=wed AT=10:15 scripts/preview.sh'); await browser.close(); process.exit(1); }
  await toDay(admin, 'Wed');

  await step('a class that has started has no Swap with…; Juggling has, and the panel asks for the class to swap it with', async () => {
    await choose(admin, block(admin, 'Gym', 'Strength and conditioning'));
    if (await admin.locator('[data-testid=inspector-swap]').count()) throw new Error('Strength and conditioning, started at 09:00, offers Swap with…');
    await choose(admin, block(admin, 'Studio A', 'Juggling'));
    await startSwap(admin);
    eq(squash(await admin.textContent('[data-testid=swap-choose]')), 'Choose the class on the plan to swap Juggling with: any class this week that has not started.', 'the panel asks');
    if (await admin.locator('[data-testid=inspector-swap]').count()) throw new Error('Swap with… is still offered while swapping');
    if (await admin.locator('[data-testid=inspector-edit]').count()) throw new Error('the buttons are still there while swapping');
    // Juggling pressed again is the class being swapped, not one to swap it with: the panel goes on asking. The week's
    // view, asked for after it, is drawn after the press is answered, so it is the barrier.
    await block(admin, 'Studio A', 'Juggling').click();
    await admin.click('[data-testid=scale-week]');
    await admin.waitForSelector('.wm-chip.chosen');
    if (await admin.locator('[data-testid=swap-with]').count() || !await admin.locator('[data-testid=swap-choose]').count()) throw new Error('pressing Juggling again chose it to swap with itself');
    await admin.click('[data-testid=scale-day]');
    await admin.waitForSelector('[data-testid=timeline]');
  });

  await step('Aerial, on at the same time, is marked beside Juggling: their spaces, saying what that brings and what is there still', async () => {
    await swapWith(admin, block(admin, 'Aerial Hall', 'Aerial'));
    const w = squash(await admin.textContent('[data-testid=swap-with]'));
    if (!/^With Aerial, Wed \d+ \w{3}, 10:45–12:00, Aerial Hall$/.test(w)) throw new Error('the panel says ' + JSON.stringify(w));
    eq(await admin.textContent('[data-testid=inspector-title]'), 'Juggling', 'the class being swapped');
    if (!await block(admin, 'Studio A', 'Juggling').evaluate(x => x.classList.contains('chosen'))) throw new Error('Juggling is no longer chosen');
    eq(await options(admin), [{
      kind: 'Spaces', take: 'Swap their spaces', says: 'Juggling to Aerial Hall, Aerial to Studio A', clear: null, putsRight: [],
      brings: ['Space "Studio A" lacks what "Aerial" needs: Crash mats, Rigging points (Wed 10:45–12:00)'],
      // Two hours and three quarters of hard training for BA 2 is there already, and swapping spaces leaves it so.
      still: ['Still: BA 2: 2h 45m of hard training in a row, Wed 09:00–12:00 (at most 2h without a 30m rest) — "Strength and conditioning", "Aerial"'],
    }], 'the ways');
    // Every block has a faint inset edge of its own, so the mark is the accent's outline, not just any shadow.
    await inAccent(block(admin, 'Aerial Hall', 'Aerial'), 'Aerial on the plan');
    // The week's view marks the two the same way.
    await admin.click('[data-testid=scale-week]');
    await admin.waitForFunction(() => document.querySelector('.wm-chip.paired')?.textContent.includes('Aerial') && document.querySelector('.wm-chip.chosen')?.textContent.includes('Juggling'))
      .catch(async () => { throw new Error('the week marks ' + JSON.stringify(await admin.$$eval('.wm-chip.paired, .wm-chip.chosen', cs => cs.map(c => c.className + ': ' + c.textContent.trim())))); });
    await inAccent(admin.locator('.wm-chip.paired'), 'Aerial on the week');
    await admin.click('[data-testid=scale-day]');
    await admin.waitForSelector('[data-testid=timeline]');
  });

  await step('a class that has started, chosen instead, says so and is offered nothing', async () => {
    const strength = block(admin, 'Gym', 'Strength and conditioning');
    await strength.click();
    await admin.waitForFunction(() => document.querySelector('[data-testid=swap-with] b')?.textContent === 'Strength and conditioning'
      && document.querySelector('[data-testid=swap-note]'));
    eq(await admin.$$eval('[data-testid=swap-note]', ns => ns.map(n => n.textContent.trim())), ['Strength and conditioning has started, so it stays where it is.'], 'the note');
    eq(await admin.locator('[data-testid=swap-option]').count(), 0, 'ways offered');
  });

  await step('Esc stops swapping, and Juggling stays chosen with Swap with… back', async () => {
    await admin.keyboard.press('Escape');
    await admin.waitForSelector('[data-testid=swap]', { state: 'detached' });
    eq(await admin.textContent('[data-testid=inspector-title]'), 'Juggling', 'the chosen class');
    await admin.waitForSelector('[data-testid=inspector-swap]');
    eq(await admin.locator('[data-testid=block].paired').count(), 0, 'classes still marked to swap with');
  });

  await step("Thursday's Acrobatics, needing cover, and Creation lab, on at once: their spaces, nothing new against it and the cover still said; Stop swapping stops", async () => {
    await toDay(admin, 'Thu');
    await choose(admin, block(admin, 'Big Top', 'Acrobatics'));
    await startSwap(admin);
    await swapWith(admin, block(admin, 'Studio B', 'Creation lab'));
    eq(await options(admin), [{
      kind: 'Spaces', take: 'Swap their spaces', says: 'Acrobatics to Studio B, Creation lab to Big Top', clear: 'Nothing new against it', putsRight: [], brings: [],
      still: ['Still: "Acrobatics" needs cover — Ada Brightwater away (Thu 09:00–11:00)'],
    }], 'the ways');
    await admin.click('[data-testid=swap-stop]');
    await admin.waitForSelector('[data-testid=swap]', { state: 'detached' });
    eq(await admin.textContent('[data-testid=inspector-title]'), 'Acrobatics', 'the chosen class');
    eq(await admin.locator('[data-testid=block].paired').count(), 0, 'classes still marked to swap with');
  });

  await step("Thursday's Acrobatics and Wednesday's Aerial: their times or their places, both putting the cover right, each saying what it brings", async () => {
    await startSwap(admin);
    // Another day of the same week keeps the class being swapped.
    await toDay(admin, 'Wed');
    eq(await admin.textContent('[data-testid=inspector-title]'), 'Acrobatics', 'the class being swapped, on Wednesday');
    await swapWith(admin, block(admin, 'Aerial Hall', 'Aerial'));
    const ways = await options(admin);
    eq(ways.map(w => w.kind + ' · ' + w.take), ['Times · Swap their times', 'Places · Swap their places'], 'the ways');
    if (!/^Acrobatics to Wed \d+ \w{3} 10:45, Aerial to Thu \d+ \w{3} 09:00$/.test(ways[0].says)) throw new Error('their times: ' + ways[0].says);
    if (!/^Acrobatics to Wed \d+ \w{3} 10:45 in Aerial Hall, Aerial to Thu \d+ \w{3} 09:00 in Big Top$/.test(ways[1].says)) throw new Error('their places: ' + ways[1].says);
    for (const w of ways) {
      if (!w.putsRight.includes('Puts right: "Acrobatics" needs cover — Ada Brightwater away (Thu 09:00–11:00)')) throw new Error(w.take + ' puts right ' + JSON.stringify(w.putsRight));
      // Two hours of Acrobatics from 10:45 runs into BA 2's lunch: said, not refused.
      if (!w.brings.some(b => b.startsWith('BA 2 double-booked: "Lunch" (Canteen) is on for all of them, and "Acrobatics"'))) throw new Error(w.take + ' brings ' + JSON.stringify(w.brings));
      if (w.clear !== null) throw new Error(w.take + ' says ' + w.clear);
    }
    // In its own hall at nine on Thursday, Aerial would meet the guest workshop, hidden or not; in the Big Top it would not.
    if (!ways[0].brings.some(b => b.startsWith('Space "Aerial Hall": "Aerial" overlaps "Guest workshop"'))) throw new Error('their times bring ' + JSON.stringify(ways[0].brings));
    if (ways[1].brings.some(b => b.includes('Guest workshop'))) throw new Error('their places bring ' + JSON.stringify(ways[1].brings));
    await admin.screenshot({ path: OUT + 'swap-ways.png' });
  });

  await step('their places taken from Thursday: the plan goes to Wednesday, where Acrobatics now is, chosen and saying where it was; Aerial is in the Big Top on Thursday', async () => {
    // Back on Acrobatics' own day, the panel still offers the two ways with Aerial.
    await toDay(admin, 'Thu');
    await admin.waitForSelector('[data-testid=swap-option][data-kind=Places]');
    await said(admin, () => admin.click('[data-testid=swap-option][data-kind=Places] [data-testid=swap-take]'), 'Swapped the places of Acrobatics and Aerial');
    await admin.waitForFunction(i => document.querySelectorAll('[data-testid=day-chip]')[i]?.getAttribute('aria-current') === 'date', DAYS.Wed)
      .catch(() => { throw new Error('the plan stayed on Thursday, where Acrobatics no longer is'); });
    await admin.waitForSelector('[data-testid=swap]', { state: 'detached' });
    const acro = block(admin, 'Aerial Hall', 'Acrobatics');
    await acro.waitFor();
    if (!await acro.evaluate(x => x.classList.contains('chosen'))) throw new Error('Acrobatics is not chosen where it went');
    eq(await admin.textContent('[data-testid=inspector-title]'), 'Acrobatics', 'the side panel');
    const moved = squash(await admin.textContent('[data-testid=inspector-moved]'));
    if (!/^from Thu \d+ \w{3}, 09:00–11:00$/.test(moved)) throw new Error('it says it moved ' + JSON.stringify(moved));
    // Ada is not away on Wednesday: nothing about cover on it now.
    if (await admin.locator('[data-testid=inspector] .insp-note.warn', { hasText: 'needs cover' }).count()) throw new Error('Acrobatics still needs cover');
    await toDay(admin, 'Thu');
    await block(admin, 'Big Top', 'Aerial').waitFor();
  });

  await step('one Undo puts both back', async () => {
    await said(admin, () => admin.keyboard.press('Control+z'), 'Undid: Swapped the places of Acrobatics and Aerial');
    await block(admin, 'Big Top', 'Acrobatics').waitFor();
    eq(await row(admin, 'Big Top').locator('[data-testid=block][data-title="Aerial"]').count(), 0, 'Aerial still in the Big Top on Thursday');
    await toDay(admin, 'Wed');
    await block(admin, 'Aerial Hall', 'Aerial').waitFor();
    eq(await row(admin, 'Aerial Hall').locator('[data-testid=block][data-title="Acrobatics"]').count(), 0, 'Acrobatics still in the Aerial Hall on Wednesday');
  });

  await step('an editor may swap; a coach reads the class and is offered nothing', async () => {
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await toDay(editor, 'Wed');
    await choose(editor, block(editor, 'Studio A', 'Juggling'));
    await editor.waitForSelector('[data-testid=inspector-swap]');
    await editor.context().close();
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await toDay(coach, 'Wed');
    await choose(coach, block(coach, 'Studio A', 'Juggling'));
    if (await coach.locator('[data-testid=inspector-swap]').count()) throw new Error('a coach is offered Swap with…');
    await coach.context().close();
  });

  await step('a phone: while the other class is chosen the sheet leaves more of the plan, and nothing scrolls sideways', async () => {
    const phone = await signIn(browser, 'admin', { width: 360, height: 740 });
    await toDay(phone, 'Wed');
    await choose(phone, block(phone, 'Studio A', 'Juggling'));
    const before = (await phone.locator('[data-testid=inspector]').boundingBox()).height;
    await startSwap(phone);
    await phone.waitForSelector('[data-testid=inspector].swap-choosing');
    const f = await phone.evaluate(() => {
      const sheet = document.querySelector('[data-testid=inspector]').getBoundingClientRect();
      return {
        sheet: sheet.height, tall: innerHeight, wide: document.documentElement.scrollWidth, width: innerWidth,
        text: (() => { const p = document.querySelector('[data-testid=swap-choose]'); return p.scrollWidth - p.clientWidth; })(),
        // In place of the buttons, so what to do and how to stop are in the sheet without scrolling it.
        stopBelow: Math.round(document.querySelector('[data-testid=swap-stop]').getBoundingClientRect().bottom - sheet.bottom),
      };
    });
    log('     ' + JSON.stringify({ before, ...f }));
    if (f.sheet > f.tall * 0.4 + 1) throw new Error(`the sheet is ${f.sheet}px of ${f.tall}`);
    if (f.stopBelow > 0) throw new Error(`Stop swapping is ${f.stopBelow}px below the sheet`);
    if (f.wide > f.width) throw new Error(`the page is ${f.wide}px wide in ${f.width}`);
    if (f.text > 0) throw new Error('the sentence asking for the other class runs out of its box');
    await phone.screenshot({ path: OUT + 'swap-phone.png' });
    await phone.context().close();
  });

  await admin.screenshot({ path: OUT + 'swap.png' });
  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})().catch(e => { log('FAIL ' + why(e)); process.exit(1); });
