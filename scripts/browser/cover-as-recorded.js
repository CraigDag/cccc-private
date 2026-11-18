// Cover it as you record it. An administrator types Esme away next Monday to Wednesday into Ctrl+K; the absence
// form it opens lists her three classes and offers who covers them, ranked as the Cover page ranks them, with "Not
// yet" chosen: the three best placed, and the rest under Somebody else (Dag and Finn dashed, free for two). Choosing
// Cleo says what OK will do; OK records the absence with Cleo on all three as one change, and one Undo takes back
// both. Dag, chosen from Somebody else, gets the two he is free for and Tuesday's is left needing cover. Not yet
// records the absence alone, for the Cover page. Narrowing the days changes what is offered and what OK says, and
// opens Somebody else when the one chosen moves under it. A class that has started is not offered (run it with
// DAY=wed AT=10:15: Esme's Wednesday 09:00 has started). A student's absence offers no cover. The phone fits it.
// The menus still close on a click elsewhere (a form's own disclosure no longer does), and that click does nothing
// else; on a phone, More opens over a class open beside the plan. With everybody else away then too, nobody is
// offered. Every other date is in the sample's next week.
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

const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
// "Mon 23 Nov", as the app says a date; "23/11", as a person types one.
const said = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
const typed = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${d.getUTCDate()}/${d.getUTCMonth() + 1}`; };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t);
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };

// Something done, and the toast it says, newer than any before it; the toast's words are handed back.
async function toasted(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  const t = await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .find(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words))?.querySelector('span').textContent, [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
  return t.jsonValue();
}

const hits = page => page.$$eval('[data-testid=find-hit]', hs => hs.map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim()));

// Somebody away typed into Ctrl+K: the first hit must be the line that records it, and Enter opens the form on it.
async function awayForm(page, line, label) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
  await page.keyboard.type(line);
  await page.waitForFunction(label => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.trim() === label, label)
    .catch(async () => { throw new Error(`"${line}": the first hit is not "${label}"; Find shows ${JSON.stringify(await hits(page))}`); });
  await page.keyboard.press('Enter');
  await titled(page, 'Record an absence');
  await page.waitForSelector('[data-testid=away-heading]');
}

// The Cover page on the week of that Monday, stepped to with its own arrows from wherever it opens.
async function coverOn(page, monday) {
  if (!page.url().endsWith('/cover')) {
    await page.goto(B + '/cover');
    await page.waitForSelector('[data-testid=week-head]');
  }
  for (let i = 0; i < 3; i++) {
    const shown = await page.inputValue('[data-testid=week-head] .week-date');
    if (shown === monday) return;
    const next = shown < monday;
    await page.click(`[data-testid=week-head] button[aria-label="${next ? 'Next' : 'Previous'} week"]`);
    await page.waitForFunction(was => document.querySelector('[data-testid=week-head] .week-date')?.value !== was, shown);
  }
  throw new Error('the Cover page did not reach the week of ' + monday);
}

// The form's choice of who covers, as a person reads it: "(•) Cleo Marchetti: Free for all 3 · …", "+" for one
// under Somebody else, "[dashed]" for somebody free for only some of it.
const choices = page => page.$$eval('[data-testid=dialog] .away-cover label', ls => ls.map(l => {
  const who = (l.querySelector('b') ?? l.querySelector('.grow').firstChild).textContent.trim();
  return `${l.closest('details') ? '+ ' : ''}${l.querySelector('input').checked ? '(•)' : '( )'} ${who}: ${l.querySelector('.ac-words').textContent.replace(/\s+/g, ' ').trim()}${l.classList.contains('part') ? ' [dashed]' : ''}`;
}));
const heading = page => page.$eval('[data-testid=away-cover-heading]', h => h.textContent.trim());
const says = page => page.locator('[data-testid=away-cover-says]').textContent();
// A choice made, once the render that says what OK will do has landed with these words.
async function choose(page, name, sentence) {
  await page.locator('[data-testid=dialog] .away-cover label', { hasText: name }).locator('input').check();
  await page.waitForFunction(s => document.querySelector('[data-testid=away-cover-says]')?.textContent === s, sentence)
    .catch(async () => { throw new Error(`choosing ${name} says ${JSON.stringify(await page.locator('[data-testid=away-cover-says]').allTextContents())}`); });
}
const gapCards = page => page.$$eval('[data-testid=cover-gap]', cs => cs.map(c => c.querySelector('.cc-title').textContent.trim() + ' ' + c.querySelector('.cc-when').textContent.trim()));
const coveredRows = page => page.$$eval('[data-testid=covered-row]', rs => rs.map(r => r.textContent.replace(/\s+/g, ' ').trim()));
const covered = (page, n) => page.waitForFunction(n => document.querySelectorAll('[data-testid=covered-row]').length === n, n);

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  // Next week is the last the sample builds. Esme teaches Strength and conditioning on Monday and Wednesday
  // mornings and Creation lab on Tuesday afternoon, alone; Cleo shares Thursday's Creation lab with her.
  const nextMonday = addDays(monday, 7), nextTuesday = addDays(monday, 8), nextWednesday = addDays(monday, 9);
  const esme = `esme away ${typed(nextMonday)}-${typed(nextWednesday)}`, esmeHit = `Record Esme Halloran away · ${said(nextMonday)} – ${said(nextWednesday)}`;
  const offered = [
    '(•) Not yet: They wait on the Cover page.',
    '( ) Cleo Marchetti: Free for all 3 · has taught Creation lab · 10h this week',
    '( ) Ada Brightwater: Free for all 3 · 5h 45m this week',
    '( ) Bo Lindqvist: Free for all 3 · 7h 15m this week',
    '+ ( ) Dag Øverlie: Free for 2 of 3, busy for 1 · has taught Strength and conditioning · 5h 45m this week [dashed]',
    '+ ( ) Finn Aalborg-Reyes: Free for 2 of 3, busy for 1 · has taught Creation lab · 10h 30m this week [dashed]',
    '+ ( ) Gwen Stroud (guest): Free for all 3 · 0m this week',
  ];
  log('     today', today, '· Esme away from', nextMonday);
  await coverOn(admin, nextMonday);

  await step('the absence form typed into Ctrl+K offers who covers her three classes, ranked as the Cover page does, Not yet chosen', async () => {
    await awayForm(admin, esme, esmeHit);
    await admin.waitForSelector('[data-testid=away-cover]');
    eq(await heading(admin), 'Who covers the 3 still to come', 'the heading');
    eq(await choices(admin), offered, 'the choice');
    eq([await admin.locator('[data-testid=away-cover-more]').evaluate(d => d.open), await admin.locator('[data-testid=away-cover-says]').count()],
      [false, 0], 'Somebody else open, and a sentence said');
    await admin.screenshot({ path: OUT + 'cover-as-recorded.png' });
  });

  await step('choosing Cleo says what OK will do, and OK records the absence with Cleo on all three as one change one Undo takes back', async () => {
    await choose(admin, 'Cleo Marchetti', 'OK records it with Cleo Marchetti covering all 3. One Undo takes both back.');
    eq((await choices(admin)).filter(c => c.includes('(•)')), ['(•) Cleo Marchetti: Free for all 3 · has taught Creation lab · 10h this week'], 'what is chosen');
    const words = await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    eq(words, "Recorded an absence, with Cleo Marchetti covering 3 of Esme Halloran's classes", 'the toast');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await covered(admin, 3);
    eq([await gapCards(admin), await admin.locator('[data-testid=cover-stretch]').count()], [[], 0], 'what still needs cover');
    const rows = await coveredRows(admin);
    if (!rows.every(r => r.includes('Cleo Marchetti for Esme Halloran'))) throw new Error('the covered rows say ' + JSON.stringify(rows));
    // One Undo: no cover left and nobody away, so nothing needing cover either.
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: Recorded an absence, with Cleo');
    await covered(admin, 0);
    eq(await gapCards(admin), [], 'what needs cover once undone');
  });

  await step('Dag, chosen from Somebody else, gets the two he is free for and Tuesday\'s is left needing cover', async () => {
    await awayForm(admin, esme, esmeHit);
    await admin.click('[data-testid=away-cover-more] summary');
    await choose(admin, 'Dag Øverlie', `OK records it with Dag Øverlie covering 2 of the 3; Creation lab on ${said(nextTuesday)} still needs cover. One Undo takes both back.`);
    eq(await admin.locator('[data-testid=away-cover-more]').evaluate(d => d.open), true, 'Somebody else open once Dag is chosen in it');
    const words = await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    eq(words, "Recorded an absence, with Dag Øverlie covering 2 of Esme Halloran's classes", 'the toast');
    await covered(admin, 2);
    eq(await gapCards(admin), [`Creation lab ${said(nextTuesday)} · 13:00–16:00`], 'what still needs cover');
    eq((await coveredRows(admin)).filter(r => r.includes('Dag Øverlie for Esme Halloran')).length, 2, 'the classes Dag covers');
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await covered(admin, 0);
  });

  await step('Not yet records the absence alone, and the Cover page offers one person for all of it', async () => {
    await awayForm(admin, esme, esmeHit);
    await admin.waitForSelector('[data-testid=away-cover]');
    const words = await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    eq(words, 'Recorded an absence', 'the toast');
    await admin.waitForSelector('[data-testid=cover-stretch]');
    eq((await gapCards(admin)).length, 3, 'the classes needing cover');
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await admin.waitForSelector('[data-testid=cover-stretch]', { state: 'detached' });
  });

  await step('narrowing the days changes who is offered and what OK says, and opens Somebody else when the one chosen moves under it', async () => {
    await awayForm(admin, esme, esmeHit);
    await choose(admin, 'Cleo Marchetti', 'OK records it with Cleo Marchetti covering all 3. One Undo takes both back.');
    await admin.fill('[data-testid=absence-to]', nextMonday);
    await admin.waitForFunction(() => document.querySelector('[data-testid=away-cover-heading]')?.textContent.trim() === 'Who covers Strength and conditioning');
    // Monday's class alone: Dag has taught it, so he comes first, and Cleo, still chosen, is under Somebody else.
    eq(await choices(admin), [
      '( ) Not yet: It waits on the Cover page.',
      '( ) Dag Øverlie: Free · has taught Strength and conditioning · 5h 45m this week',
      '( ) Ada Brightwater: Free · 5h 45m this week',
      '( ) Bo Lindqvist: Free · 7h 15m this week',
      '+ (•) Cleo Marchetti: Free · 10h this week',
      '+ ( ) Gwen Stroud (guest): Free · 0m this week',
    ], 'the choice for Monday alone');
    eq(await admin.locator('[data-testid=away-cover-more]').evaluate(d => d.open), true, 'Somebody else open');
    eq(await says(admin), 'OK records it with Cleo Marchetti covering Strength and conditioning. One Undo takes both back.', 'what OK says');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a class that has started is not offered: Esme sick today at 10:15 leaves Wednesday\'s 09:00 needing cover and offers nobody for it', async () => {
    if (new Date(today + 'T12:00:00Z').getUTCDay() !== 3) throw new Error(`today is ${said(today)}: run the preview with DAY=wed AT=10:15`);
    await awayForm(admin, 'esme sick', `Record Esme Halloran away · ${said(today)}`);
    eq(await admin.$$eval('[data-testid=away-class]', ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim())),
      [`Strength and conditioning ${said(today)}, 09:00–10:30 · needs cover`], 'what it takes her off');
    eq(await admin.locator('[data-testid=away-cover], [data-testid=away-cover-heading]').count(), 0, 'cover offered for a class that has started');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a student\'s absence offers no cover', async () => {
    await awayForm(admin, esme, esmeHit);
    await admin.waitForSelector('[data-testid=away-cover]');
    const student = await admin.$eval('[data-testid=absence-who] optgroup[label=Students] option', o => o.value);
    await admin.selectOption('[data-testid=absence-who]', student);
    await admin.waitForFunction(() => document.querySelector('[data-testid=away-heading]')?.textContent.trim() === 'They would miss');
    eq(await admin.locator('[data-testid=away-cover], [data-testid=away-cover-heading]').count(), 0, 'cover offered for a student');
    await admin.click('[data-testid=dialog-cancel]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('the phone: the form fits the screen with every choice inside it, and choosing works there too', async () => {
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    await coverOn(phone, nextMonday);
    await awayForm(phone, esme, esmeHit);
    await phone.waitForSelector('[data-testid=away-cover]');
    await phone.click('[data-testid=away-cover-more] summary');
    await phone.waitForFunction(() => document.querySelector('[data-testid=away-cover-more]')?.open);
    const fit = await phone.evaluate(() => {
      const d = document.querySelector('[data-testid=dialog]').getBoundingClientRect();
      const labels = [...document.querySelectorAll('[data-testid=dialog] .away-cover label')];
      const ls = labels.map(l => l.getBoundingClientRect());
      // Each choice's words sit under its name and wrap inside the form, rather than running off it on the name's line;
      // any that do not are named, with where they were.
      const words = labels.flatMap(l => {
        const w = l.querySelector('.ac-words'), g = w?.parentElement;
        if (!w) return [`${l.textContent.trim().slice(0, 20)}: no words`];
        const off = [...w.getClientRects()].some(r => r.width === 0 || r.left < d.left || r.right > d.right);
        const drop = w.getBoundingClientRect().top - g.getBoundingClientRect().top, line = parseFloat(getComputedStyle(g).lineHeight);
        return off || drop < line * 0.8 ? [`${(l.querySelector('b') ?? g).textContent.slice(0, 20)}:${off ? ' outside the form,' : ''} ${Math.round(drop)}px down a ${Math.round(line)}px line`] : [];
      });
      return { page: document.documentElement.scrollWidth <= innerWidth, dialog: d.left >= 0 && d.right <= innerWidth, choices: ls.length, inside: ls.every(l => l.width > 0 && l.left >= d.left && l.right <= d.right), words };
    });
    eq(fit, { page: true, dialog: true, choices: 7, inside: true, words: [] }, 'how it fits');
    await choose(phone, 'Bo Lindqvist', 'OK records it with Bo Lindqvist covering all 3. One Undo takes both back.');
    eq(await phone.locator('[data-testid=away-cover-more]').evaluate(d => d.open), true, 'Somebody else, opened, once somebody above it is chosen');
    await phone.locator('[data-testid=away-cover-says]').scrollIntoViewIfNeeded();
    await phone.screenshot({ path: OUT + 'cover-as-recorded-phone.png' });
    await toasted(phone, () => phone.click('[data-testid=dialog-ok]'), "Recorded an absence, with Bo Lindqvist covering 3 of Esme Halloran's classes");
    await covered(phone, 3);
    await toasted(phone, () => phone.click('[data-testid=undo]'), 'Undid: ');
    await covered(phone, 0);
    await phone.context().close();
  });

  await step('the menus still close on a click anywhere else, and a choice in one closes it', async () => {
    await admin.click('[data-testid=me-menu] summary');
    await admin.waitForFunction(() => document.querySelector('[data-testid=me-menu]').open);
    await admin.mouse.click(8, 400);
    await admin.waitForFunction(() => !document.querySelector('[data-testid=me-menu]').open)
      .catch(() => { throw new Error('the You menu stays open after a click elsewhere'); });
    const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
    // The tap that puts More away does nothing else: pressed on a space in use, it leaves Now as it is, where the
    // same tap with nothing open opens that space's class beside the plan.
    await phone.click('.dock-more summary');
    await phone.waitForFunction(() => document.querySelector('.dock-more').open);
    const row = await phone.locator('[data-testid=space-row].busy').first().boundingBox();
    await phone.mouse.click(row.x + row.width / 2, row.y + row.height / 2);
    await phone.waitForFunction(() => !document.querySelector('.dock-more').open)
      .catch(() => { throw new Error('the phone\'s More stays open after a click elsewhere'); });
    // Long enough for a tap that went through to have been answered: nothing is to happen, so nothing can be waited for.
    await phone.waitForTimeout(800);
    eq(await phone.evaluate(() => [location.pathname, !!document.querySelector('[data-testid=inspector]')]), ['/', false], 'Now, after the tap that put More away (where, a class open)');
    await phone.mouse.click(row.x + row.width / 2, row.y + row.height / 2);
    await phone.waitForSelector('[data-testid=inspector]');
    // With a class open beside the plan, More opens over it, and every choice in it can be pressed.
    await phone.click('.dock-more summary');
    await phone.waitForFunction(() => document.querySelector('.dock-more').open);
    const under = await phone.evaluate(() => [...document.querySelectorAll('.dock-sheet a')]
      .filter(a => { const r = a.getBoundingClientRect(); return !a.contains(document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2)); })
      .map(a => a.textContent.trim()));
    eq(under, [], 'the choices in More that the open class covers');
    await phone.click('.dock-sheet a[href=cover]');
    await phone.waitForSelector('[data-testid=week-head]');
    eq(await phone.evaluate(() => document.querySelector('.dock-more').open), false, 'More once Cover is chosen in it');
    await phone.context().close();
  });

  await step('with everybody else away then too, the form says nobody is free for any of it and offers only Not yet', async () => {
    for (const [line, name] of [['ada', 'Ada Brightwater'], ['bo', 'Bo Lindqvist'], ['cleo', 'Cleo Marchetti'], ['dag', 'Dag Øverlie'], ['finn', 'Finn Aalborg-Reyes'], ['gwen', 'Gwen Stroud']]) {
      await awayForm(admin, `${line} away ${typed(nextMonday)}-${typed(nextWednesday)}`, `Record ${name} away · ${said(nextMonday)} – ${said(nextWednesday)}`);
      await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
      await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    }
    await awayForm(admin, esme, esmeHit);
    await admin.waitForSelector('[data-testid=away-cover-nobody]');
    eq([await heading(admin), await choices(admin), await admin.locator('[data-testid=away-cover-more]').count()],
      ['Who covers the 3 still to come', ['(•) Not yet: They wait on the Cover page.'], 0], 'the choice');
    eq(await admin.locator('[data-testid=away-cover-nobody]').textContent(), 'Nobody is free for any of it.', 'what it says');
    await admin.click('[data-testid=dialog-cancel]');
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
