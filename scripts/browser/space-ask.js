// Asking for a space. A coach's My week has Ask for a space…: the form says what each space has then, and what a
// yes would run into (Studio A has Juggling next Wednesday at 11, Studio B is free), and sending changes nothing on
// the timetable. An administrator finds it on Now beside Finn's (the sample's own) with its clash counted, puts it
// in Studio B instead, and the class is on the plan there, taught by the coach; one Undo takes it back and it waits
// again. The coach reads where it went. Typed into Ctrl+K, a coach's line is an ask, not an add: "juggling prep
// <next Friday> 15" opens the form filled in from Prep's Juggling; sent and taken back, it waits for nobody. Finn's
// gets a no, and leaves Now. An editor linked to a facilitator adds the class instead: Ctrl+K and My week offer no
// ask. A phone fits. Every date is in the sample's next week, so the walk runs on any day of the sample week.
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
const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
// "Wed 25 Nov", as the app says a date; "27/11", as a person types one.
const said = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
const typed = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${d.getUTCDate()}/${d.getUTCMonth() + 1}`; };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t)
  .catch(async () => { throw new Error(`no "${t}" dialog; the dialog says ${JSON.stringify(await page.locator('[data-testid=dialog] #dialog-title').allInnerTexts())}`); });
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };
const text = async (page, sel) => squash(await page.textContent(sel));
const lines = (page, sel) => page.$$eval(sel, ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim()));
// A line whose parts are drawn apart is read a part at a time with a space between, as a screen reader says it.
const parts = (page, sel) => page.$$eval(sel,
  ls => ls.map(l => [...l.children].map(c => c.textContent.replace(/\s+/g, ' ').trim()).join(' ')));

// Something done, and the toast it says, newer than any before it.
async function toasted(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}

const hits = page => page.$$eval('[data-testid=find-hit]', hs => hs.map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim()));
async function find(page, line, first) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
  await page.keyboard.type(line);
  await page.waitForFunction(first => {
    const h = document.querySelector('[data-testid=find-hit]');
    return h && h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim() === first;
  }, first).catch(async () => { throw new Error(`"${line}": the first hit is not "${first}"; Find shows ${JSON.stringify(await hits(page))}`); });
  return hits(page);
}
async function tidy(page) {
  if (await page.locator('[data-testid=find]').count()) {
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=find]', { state: 'detached' });
  }
  if (await page.locator('[data-testid=dialog]').count()) {
    await page.click('[data-testid=dialog-x]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  }
}

// The ask form as a person reads it.
const askForm = page => page.evaluate(() => {
  const chosen = id => { const s = document.querySelector(`[data-testid=${id}]`); return s ? s.options[s.selectedIndex]?.text.trim() : null; };
  const value = id => document.querySelector(`[data-testid=${id}]`)?.value ?? null;
  return {
    date: value('ask-date'), from: value('ask-start'), until: value('ask-end'), room: chosen('ask-room'), who: chosen('ask-for'),
    groups: [...document.querySelectorAll('[data-testid=ask-group]')].filter(c => c.checked).map(c => c.dataset.name),
    cls: chosen('ask-class'), title: value('ask-title'),
    why: document.querySelector('[data-testid=ask-why]')?.textContent.trim() ?? null,
    clear: document.querySelector('[data-testid=ask-clear]')?.textContent.trim() ?? null,
    clashes: [...document.querySelectorAll('[data-testid=ask-clash]')].map(l => l.textContent.replace(/\s+/g, ' ').trim()),
    send: !document.querySelector('[data-testid=dialog-ok]')?.disabled,
  };
});
// Waiting for an answer on Now, as an administrator reads it.
const waiting = page => parts(page, '[data-testid=waiting-answer] [data-testid=ask]');
async function nowPage(page) {
  await page.goto(B + '/');
  await page.waitForSelector('[data-testid=today-needs]');
}
async function myWeek(page) {
  await page.goto(B + '/my-week');
  await page.waitForSelector('[data-testid=cant-come]');
}
// The plan on a day, and the classes drawn on it as "title start–end space".
async function planOn(page, day) {
  if (!page.url().endsWith('/plan')) { await page.goto(B + '/plan'); await page.waitForSelector('[data-testid=timeline]'); }
  await page.fill('[data-testid=day-date]', day);
  await page.locator('[data-testid=day-date]').dispatchEvent('change');
  await page.waitForFunction(d => document.querySelector('[data-testid=timeline]')?.dataset.day === d, day);
}
const blocksTitled = (page, title) => page.$$eval(`[data-testid=timeline] [data-testid=block][data-title="${title}"]`,
  bs => bs.map(b => b.closest('.tl-row')?.querySelector('.tl-name')?.textContent.trim()));

(async () => {
  const browser = await chromium.launch();
  const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
  await coach.goto(B + '/plan');
  await coach.waitForSelector('[data-testid=timeline]');
  const today = await coach.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today);
  // Next week is the last the sample builds: Juggling for BA 1 in Studio A on its Wednesday at 10:45, Prep's
  // Juggling in Studio B on its Friday at 11; Finn asked yesterday for Studio B on its Thursday at 14:00.
  const wednesday = addDays(monday, 9), thursday = addDays(monday, 10), friday = addDays(monday, 11);
  log('     today', today, '· asking for', wednesday, 'and', friday);
  const adas = `Space Ada Brightwater Handstands, extra · Studio A · ${said(wednesday)}, 11:00–12:00 · 1 clash`;
  const finns = `Space Finn Aalborg-Reyes Duo act rehearsal · Studio B · ${said(thursday)}, 14:00–15:30`;

  await step('a coach asks for a space on My week, the form saying what a yes would run into, and Send waits until it can be sent', async () => {
    await myWeek(coach);
    if (await coach.locator('[data-testid=my-space-ask]').count()) throw new Error('Ada has asked for a space already');
    await coach.click('[data-testid=ask-space]');
    await titled(coach, 'Ask for a space');
    let f = await askForm(coach);
    eq([f.date, f.from, f.until, f.room, f.who, f.why, f.send], [addDays(today, 1), '10:00', '11:00', 'Choose a space…', 'Groups', 'Choose a space.', false], 'the form opens');
    if (!(await text(coach, '[data-testid=ask-public]')).includes('never name a student')) throw new Error('the form does not say its name is public');
    // A day gone, and an end before its start, are said before Send, as the edit would refuse them.
    const why = async (words, what) => coach.waitForFunction(w => document.querySelector('[data-testid=ask-why]')?.textContent.startsWith(w), words)
      .catch(async () => { throw new Error(`${what}: the form says ${JSON.stringify((await askForm(coach)).why)}`); });
    await coach.fill('[data-testid=ask-date]', addDays(today, -1));
    await coach.locator('[data-testid=ask-date]').dispatchEvent('change');
    await why('That time has gone', 'yesterday');
    await coach.fill('[data-testid=ask-date]', wednesday);
    await coach.locator('[data-testid=ask-date]').dispatchEvent('change');
    await coach.fill('[data-testid=ask-start]', '11:00');
    await coach.locator('[data-testid=ask-start]').dispatchEvent('change');
    await coach.fill('[data-testid=ask-end]', '10:30');
    await coach.locator('[data-testid=ask-end]').dispatchEvent('change');
    await why('It has to finish after it starts', 'until before from');
    await coach.fill('[data-testid=ask-end]', '12:00');
    await coach.locator('[data-testid=ask-end]').dispatchEvent('change');
    // Each space says what it has then: Studio A has BA 1's Juggling.
    await coach.waitForFunction(() => [...document.querySelectorAll('[data-testid=ask-room] option')].some(o => o.text.startsWith('Studio A — busy: Juggling 10:45–12:00')))
      .catch(async () => { throw new Error('the spaces read ' + JSON.stringify(await coach.$$eval('[data-testid=ask-room] option', os => os.map(o => o.text)))); });
    await coach.selectOption('[data-testid=ask-room]', { label: 'Studio A — busy: Juggling 10:45–12:00' });
    await coach.waitForFunction(() => document.querySelector('[data-testid=ask-why]')?.textContent.startsWith('Say who it is for'));
    await coach.check('[data-testid=ask-group][data-name=Prep]');
    await coach.waitForFunction(() => document.querySelector('[data-testid=ask-why]')?.textContent.startsWith('Say what it is'));
    await coach.fill('[data-testid=ask-title]', 'Handstands, extra');
    await coach.locator('[data-testid=ask-title]').dispatchEvent('change');
    await coach.waitForSelector('[data-testid=ask-clashes]');
    f = await askForm(coach);
    log('     ' + f.clashes.join(' | '));
    eq([f.room, f.groups, f.cls, f.title, f.why, f.send], ['Studio A — busy: Juggling 10:45–12:00', ['Prep'], 'No class: give it a name', 'Handstands, extra', null, true], 'the form filled in');
    eq(f.clashes.length, 1, 'how many things it runs into');
    if (!(f.clashes[0].includes('Space "Studio A"') && f.clashes[0].includes('"Handstands, extra"') && f.clashes[0].includes('"Juggling"'))) throw new Error('it runs into ' + JSON.stringify(f.clashes));
    // Offered as free then: every space nothing is in, never Studio A or the Aerial Hall.
    eq((await coach.$$eval('[data-testid=ask-free-space] span', s => s.map(x => x.textContent.trim()))).sort(), ['Big Top', 'Canteen', 'Gym', 'Studio B'], 'the spaces free then');
    // Studio B is free then: chosen from the free spaces, nothing to run into.
    await coach.locator('[data-testid=ask-free-space]', { hasText: 'Studio B' }).click();
    await coach.waitForSelector('[data-testid=ask-clear]');
    eq((await askForm(coach)).clear, 'Nothing: the space is free then, and so are you.', 'in Studio B');
    eq((await coach.$$eval('[data-testid=ask-free-space] span', s => s.map(x => x.textContent.trim()))).sort(), ['Big Top', 'Canteen', 'Gym'], 'the spaces free then besides Studio B');
    await coach.screenshot({ path: OUT + 'space-ask-form.png' });
    // Asked for Studio A all the same: an administrator may put it elsewhere.
    await coach.selectOption('[data-testid=ask-room]', { label: 'Studio A — busy: Juggling 10:45–12:00' });
    await coach.waitForSelector('[data-testid=ask-clashes]');
    await toasted(coach, () => coach.click('[data-testid=dialog-ok]'), `Asked for Studio A on ${said(wednesday)}`);
    await coach.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await coach.waitForSelector('[data-testid=my-space-ask]');
    eq(await parts(coach, '[data-testid=my-space-ask]'), [`Space Studio A: Handstands, extra, ${said(wednesday)}, 11:00–12:00 Waiting for an answer Take back`], 'My week');
  });

  await step('nothing on the timetable changes until an answer: the plan has no such class', async () => {
    await planOn(coach, wednesday);
    eq(await blocksTitled(coach, 'Handstands, extra'), [], 'the plan');
  });

  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await step("an administrator finds it on Now beside Finn's, soonest first, its clash counted, and an editor finds neither", async () => {
    await admin.waitForSelector('[data-testid=waiting-answer] [data-kind=space]');
    const asks = (await waiting(admin)).filter(a => a.startsWith('Space'));
    asks.forEach(a => log('     ' + a));
    eq(asks, [adas, finns], 'Now');
    if (!await admin.locator('[data-testid=ask][data-kind=space].warn').count()) throw new Error('the one with a clash is not marked');
    await admin.screenshot({ path: OUT + 'space-ask-now.png' });
    // Only an administrator answers: an editor's Now, while both wait, shows neither.
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await editor.waitForSelector('[data-testid=today-needs]');
    if (await editor.locator('[data-testid=waiting-answer]').count()) throw new Error("an editor's Now shows what is waiting: " + JSON.stringify(await waiting(editor)));
    await editor.context().close();
  });

  await step('the answer says what a yes runs into, and in Studio B instead nothing; yes puts it there, taught by her', async () => {
    await admin.locator('[data-testid=ask][data-kind=space]', { hasText: 'Ada Brightwater' }).click();
    await titled(admin, 'Ada Brightwater asks for a space');
    const line = await text(admin, '[data-testid=answer-space]');
    if (!line.startsWith(`Handstands, extra, ${said(wednesday)}, 11:00–12:00, Studio A · Prep · asked `)) throw new Error('the form says ' + line);
    const clashes = await lines(admin, '[data-testid=answer-clash]');
    if (clashes.length !== 1 || !clashes[0].includes('"Juggling"')) throw new Error('a yes runs into ' + JSON.stringify(clashes));
    const rooms = await admin.$$eval('[data-testid=answer-room] option', os => os.map(o => o.text.trim()));
    log('     ' + rooms.join(' | '));
    if (rooms[0] !== 'Studio A, as asked' || !rooms.includes('Studio B, free then')) throw new Error('the spaces offered are ' + JSON.stringify(rooms));
    await admin.selectOption('[data-testid=answer-room]', { label: 'Studio B, free then' });
    await admin.waitForSelector('[data-testid=answer-clear]');
    eq(await text(admin, '[data-testid=answer-clear]'), 'Nothing: Studio B is free then, and so is Ada Brightwater.', 'in Studio B');
    await admin.screenshot({ path: OUT + 'space-ask-answer.png' });
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), `Put Handstands, extra in Studio B on ${said(wednesday)}, as asked`);
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForFunction(() => ![...document.querySelectorAll('[data-testid=ask]')].some(a => a.textContent.includes('Handstands')));
    await planOn(admin, wednesday);
    eq(await blocksTitled(admin, 'Handstands, extra'), ['Studio B'], 'the plan');
    await admin.locator('[data-testid=block][data-title="Handstands, extra"]').click();
    await admin.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Handstands, extra');
    const panel = await text(admin, '[data-testid=inspector]');
    if (!panel.includes('Ada Brightwater')) throw new Error('the side panel says ' + panel);
  });

  await step('one Undo takes the class back and the ask waits again', async () => {
    await toasted(admin, () => admin.click('[data-testid=undo]'), 'Undid: ');
    await admin.waitForFunction(() => !document.querySelector('[data-testid=timeline] [data-testid=block][data-title="Handstands, extra"]'));
    await nowPage(admin);
    await admin.waitForSelector('[data-testid=waiting-answer] [data-kind=space]');
    eq((await waiting(admin)).filter(a => a.startsWith('Space')), [adas, finns], 'Now after Undo');
    // And yes again, in Studio B.
    await admin.locator('[data-testid=ask][data-kind=space]', { hasText: 'Ada Brightwater' }).click();
    await titled(admin, 'Ada Brightwater asks for a space');
    await admin.selectOption('[data-testid=answer-room]', { label: 'Studio B, free then' });
    await admin.waitForSelector('[data-testid=answer-clear]');
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Put Handstands, extra in Studio B');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('the coach reads where it went, and it is on her week', async () => {
    await myWeek(coach);
    await coach.waitForFunction(() => document.querySelector('[data-testid=my-space-ask] [data-testid=asked-state]')?.textContent.startsWith('Yes'));
    eq(await text(coach, '[data-testid=my-space-ask] [data-testid=asked-state]'), `Yes: on the timetable ${said(wednesday)}, 11:00–12:00 in Studio B`, 'her ask');
    if (await coach.locator('[data-testid=my-space-ask] [data-testid=take-back]').count()) throw new Error('an answered ask can still be taken back');
    await planOn(coach, wednesday);
    eq(await blocksTitled(coach, 'Handstands, extra'), ['Studio B'], 'her plan');
  });

  await step("typed into Ctrl+K, a coach's line is an ask filled in from the class's own week, and taken back it waits for nobody", async () => {
    await tidy(coach);
    const line = `Ask for Studio B · ${said(friday)} 15:00–16:00 — Juggling · Prep · the rest as on ${said(friday)}`;
    const found = await find(coach, `juggling prep ${typed(friday)} 15`, line);
    if (found.some(h => h.startsWith('Add '))) throw new Error('a coach is offered an add: ' + JSON.stringify(found));
    if (!(await coach.textContent('.find-foot')).includes('“studio b thu 14” to ask for a space')) throw new Error('the foot does not say a space can be asked for');
    await coach.keyboard.press('Enter');
    await titled(coach, 'Ask for a space');
    const f = await askForm(coach);
    eq([f.date, f.from, f.until, f.who, f.groups, f.cls, f.title, f.clear, f.send], [friday, '15:00', '16:00', 'Groups', ['Prep'], 'Juggling', '', 'Nothing: the space is free then, and so are you.', true], 'the form from Find');
    if (!f.room.startsWith('Studio B')) throw new Error('the space is ' + f.room);
    await toasted(coach, () => coach.click('[data-testid=dialog-ok]'), `Asked for Studio B on ${said(friday)}`);
    await coach.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await myWeek(coach);
    await coach.waitForFunction(() => document.querySelectorAll('[data-testid=my-space-ask]').length === 2);
    const second = coach.locator('[data-testid=my-space-ask]', { hasText: 'Juggling' });
    eq(await text(coach, '[data-testid=my-space-ask]:has-text("Juggling") [data-testid=asked-state]'), 'Waiting for an answer', 'the typed ask');
    await toasted(coach, () => second.locator('[data-testid=take-back]').click(), 'Took back asking for a space');
    await coach.waitForFunction(() => document.querySelectorAll('[data-testid=my-space-ask]').length === 1);
    await nowPage(admin);
    await admin.waitForSelector('[data-testid=waiting-answer] [data-kind=space]');
    eq((await waiting(admin)).filter(a => a.startsWith('Space')), [finns], 'Now');
  });

  await step("not this time to Finn's changes nothing on the timetable, and it leaves Now", async () => {
    await admin.locator('[data-testid=ask][data-kind=space]', { hasText: 'Finn Aalborg-Reyes' }).click();
    await titled(admin, 'Finn Aalborg-Reyes asks for a space');
    eq(await text(admin, '[data-testid=answer-clear]'), 'Nothing: Studio B is free then, and so is Finn Aalborg-Reyes.', "Finn's");
    await toasted(admin, () => admin.click('[data-testid=answer-no]'), 'Said no to a space asked for');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForFunction(() => !document.querySelector('[data-testid=ask][data-kind=space]'));
    await planOn(admin, thursday);
    eq(await blocksTitled(admin, 'Duo act rehearsal'), [], 'the plan');
  });

  await step("an editor, linked to a facilitator, adds the class instead: neither Ctrl+K nor My week offers an ask", async () => {
    // Linked to Bo, the editor has a week of their own, and still adds classes rather than asking for them.
    await admin.goto(B + '/admin/accounts');
    await admin.locator('[data-testid=account-row]', { hasText: 'Preview Editor' }).click();
    await titled(admin, 'Preview Editor');
    await admin.selectOption('[data-testid=dialog] label.field:has-text("Linked to a facilitator") select', { label: 'Bo Lindqvist' });
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Changed Preview Editor');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await myWeek(editor);
    if (await editor.locator('[data-testid=ask-space]').count()) throw new Error('an editor is offered Ask for a space on My week');
    await editor.goto(B + '/plan');
    await editor.waitForSelector('[data-testid=timeline]');
    const found = await find(editor, `juggling prep ${typed(friday)} 15`, `Add Juggling · ${said(friday)} 15:00–16:00 — Prep · Studio B · Cleo Marchetti · the rest as on ${said(friday)}`);
    if (found.some(h => h.startsWith('Ask for'))) throw new Error('an editor is offered an ask: ' + JSON.stringify(found));
    await editor.context().close();
  });

  await step("a phone: the coach's list and the form fit, and nothing scrolls sideways", async () => {
    const phone = await signIn(browser, 'coach', { width: 390, height: 844 });
    await myWeek(phone);
    await phone.waitForSelector('[data-testid=my-space-ask]');
    const wide = await phone.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (wide > 0) throw new Error('My week scrolls sideways by ' + wide + 'px');
    for (const li of await phone.locator('[data-testid=my-asking] li').all()) {
      const box = await li.boundingBox();
      if (box.x < 0 || box.x + box.width > 390) throw new Error('a line runs off the screen: ' + JSON.stringify(box));
    }
    await phone.click('[data-testid=ask-space]');
    await titled(phone, 'Ask for a space');
    await phone.waitForTimeout(400);
    const panel = await phone.locator('[data-testid=dialog]').boundingBox();
    if (panel.x < 0 || panel.x + panel.width > 391) throw new Error('the form is ' + JSON.stringify(panel));
    const inside = await phone.evaluate(() => {
      const d = document.querySelector('[data-testid=dialog]');
      return [...d.querySelectorAll('input, select, button')].filter(e => e.offsetParent).map(e => e.getBoundingClientRect())
        .filter(r => r.right > d.getBoundingClientRect().right + 1 || r.left < d.getBoundingClientRect().left - 1).length;
    });
    if (inside) throw new Error(inside + ' controls run out of the form');
    await phone.screenshot({ path: OUT + 'space-ask-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  console.log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
