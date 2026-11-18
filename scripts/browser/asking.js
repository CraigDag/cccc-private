// Asking: a coach says on My week they can't come (seeing first what it would take them off) and nothing on
// the timetable changes; an administrator finds it on Now with what else is waiting, and answers each: not
// this time, yes (recorded as away), and yes to an offer to cover. The coach reads the answers, is offered
// the class the yes left needing cover, offers, takes it back and offers again, and the administrator says
// yes from the class's own side panel, where the offer ranks first. An editor answers nothing; a phone
// fits. Best on a preview started with DAY=wed AT=10:15, the sample week's Wednesday, whatever day it is.
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
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const iso = d => d.toISOString().slice(0, 10);
const addDays = (d, n) => new Date(d.getTime() + n * 86400000);
const short = d => `${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`;

const toasts = async page => (await page.locator('[data-testid=toast]').allInnerTexts()).map(squash);
const lines = (page, sel) => page.$$eval(sel, ls => ls.map(l => l.textContent.replace(/\s+/g, ' ').trim()));
// A line whose parts are drawn apart (one under another, or a button at the end) is read a part at a time
// with a space between, as a screen reader says it, not run together as one textContent is.
const parts = (page, sel) => page.$$eval(sel,
  ls => ls.map(l => [...l.children].map(c => c.textContent.replace(/\s+/g, ' ').trim()).join(' ')));
// Waiting for an answer on Now, as an administrator reads it.
const waiting = page => parts(page, '[data-testid=waiting-answer] [data-testid=ask]');
async function myWeek(page) {
  await page.goto(B + '/my-week');
  await page.waitForSelector('[data-testid=cant-come]');
}
async function nowPage(page) {
  await page.goto(B + '/');
  await page.waitForSelector('[data-testid=today-needs]');
}

(async () => {
  const browser = await chromium.launch();
  const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
  // About the sample week before its Thursday: a preview on a later day says so, rather than failing on whatever today is.
  const day = (await coach.textContent('.now-head h1 span')).trim();
  if (/^(Thursday|Friday|Saturday|Sunday)/.test(day)) { log('FAIL the preview says it is ' + day + ': start it with DAY=wed AT=10:15 scripts/preview.sh'); await browser.close(); process.exit(1); }
  // The sample is built around this week's Monday: Ada is the coach, away this Thursday; Cleo asked yesterday
  // to be away next Tuesday, and Dag offered to cover Ada's Thursday.
  await coach.goto(B + '/plan');
  await coach.waitForSelector('[data-testid=timeline]');
  const today = new Date(await coach.getAttribute('[data-testid=timeline]', 'data-day') + 'T00:00:00Z');
  const monday = addDays(today, -((today.getUTCDay() + 6) % 7));
  const nextMonday = addDays(monday, 7), nextTuesday = addDays(monday, 8), thursday = addDays(monday, 3);
  const beforeThursday = today <= thursday;
  log('     today ' + iso(today) + '; asking to be away on ' + short(nextMonday));

  await step('a coach sees what being away would take them off before saying so, and saying so changes nothing yet', async () => {
    await myWeek(coach);
    if (await coach.locator('[data-testid=my-asking]').count()) throw new Error('Ada has asked something already');
    if (await coach.locator('[data-testid=could-cover]').count()) throw new Error('Ada is offered a class to cover before anybody else is away: ' + JSON.stringify(await lines(coach, '[data-testid=could-cover-class]')));
    await coach.click('[data-testid=cant-come]');
    await coach.waitForSelector('[data-testid=dialog] >> text=Can\'t come');
    await coach.fill('[data-testid=cant-from]', iso(nextMonday));
    await coach.waitForFunction(d => document.querySelector('[data-testid=cant-to]')?.value === d, iso(nextMonday));
    await coach.waitForSelector('[data-testid=cant-takes]');
    const takes = await lines(coach, '[data-testid=cant-class]');
    log('     ' + takes.join(' | '));
    const expect = [`Acrobatics ${short(nextMonday)}, 10:45–12:00 · Bo Lindqvist still teaching it`, `Circus disciplines ${short(nextMonday)}, 13:00–15:00 · needs cover`];
    if (JSON.stringify(takes) !== JSON.stringify(expect)) throw new Error('it says it takes her off ' + JSON.stringify(takes));
    // Between two times that reach none of her classes, it takes her off none.
    await coach.uncheck('[data-testid=cant-all-day]');
    await coach.waitForSelector('[data-testid=cant-start]');
    if (!await coach.locator('[data-testid=dialog-ok]').isDisabled()) throw new Error('Send is offered with no times given');
    await coach.fill('[data-testid=cant-start]', '08:00');
    await coach.fill('[data-testid=cant-end]', '09:30');
    await coach.waitForSelector('[data-testid=cant-none]');
    await coach.check('[data-testid=cant-all-day]');
    await coach.waitForFunction(() => document.querySelectorAll('[data-testid=cant-class]').length === 2);
    await coach.click('[data-testid=dialog-ok]');
    await coach.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await coach.waitForSelector('[data-testid=my-request]');
    const said = await toasts(coach);
    if (!said.some(t => t.startsWith('Asked to be away from ' + short(nextMonday)))) throw new Error('the messages say ' + JSON.stringify(said));
    const mine = await parts(coach, '[data-testid=my-request]');
    if (mine.length !== 1 || mine[0] !== `Can't come ${short(nextMonday)}, all day Waiting for an answer Take back`) throw new Error('My week lists ' + JSON.stringify(mine));
    await coach.screenshot({ path: OUT + 'asking-coach.png' });
  });

  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await step("an administrator finds on Now what is waiting for their answer, soonest first", async () => {
    await admin.waitForSelector('[data-testid=waiting-answer]');
    const asks = await waiting(admin);
    asks.forEach(a => log('     ' + a));
    const expect = [
      `Can't come Ada Brightwater ${short(nextMonday)}, all day · 2 classes`,
      `Can't come Cleo Marchetti ${short(nextTuesday)}, all day · 1 class`,
    ];
    if (beforeThursday) expect.push(`Can cover Dag Øverlie Acrobatics, ${short(thursday)}, 09:00–11:00`);
    if (JSON.stringify(asks) !== JSON.stringify(expect)) throw new Error('Now lists ' + JSON.stringify(asks));
    await admin.screenshot({ path: OUT + 'asking-now.png' });
  });

  await step('an editor answers nothing and is shown nothing waiting, while it waits for an administrator', async () => {
    const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
    await editor.waitForSelector('[data-testid=today-needs]');
    if (await editor.locator('[data-testid=waiting-answer]').count()) throw new Error('an editor is shown what is waiting: ' + JSON.stringify(await waiting(editor)));
    if ((await waiting(admin)).length === 0) throw new Error('nothing is waiting, so this proves nothing');
    await editor.context().close();
  });

  await step('not this time leaves the timetable as it was, and yes records the absence and leaves its class needing cover', async () => {
    await admin.locator('[data-testid=ask]', { hasText: 'Ada Brightwater' }).click();
    await admin.waitForSelector('[data-testid=dialog] >> text=Ada Brightwater can\'t come');
    const days = squash(await admin.locator('[data-testid=answer-days]').textContent());
    if (!days.startsWith(`${short(nextMonday)}, all day · asked today `)) throw new Error('the form says ' + JSON.stringify(days));
    const classes = await lines(admin, '[data-testid=answer-class]');
    if (classes.length !== 2 || !classes[1].endsWith('needs cover')) throw new Error('the form lists ' + JSON.stringify(classes));
    await admin.click('[data-testid=answer-no]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForFunction(() => ![...document.querySelectorAll('[data-testid=ask]')].some(a => a.textContent.includes('Ada Brightwater')));

    await admin.locator('[data-testid=ask]', { hasText: 'Cleo Marchetti' }).click();
    await admin.waitForSelector('[data-testid=dialog] >> text=Cleo Marchetti can\'t come');
    const cleos = await lines(admin, '[data-testid=answer-class]');
    log('     Cleo: ' + cleos.join(' | '));
    if (JSON.stringify(cleos) !== JSON.stringify([`Aerial ${short(nextTuesday)}, 09:00–11:00 · needs cover`])) throw new Error('the form lists ' + JSON.stringify(cleos));
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForFunction(() => ![...document.querySelectorAll('[data-testid=ask]')].some(a => a.textContent.includes('Cleo Marchetti')));
    const said = await toasts(admin);
    if (!said.some(t => t.startsWith('Recorded an absence asked for'))) throw new Error('the messages say ' + JSON.stringify(said));
    // The absence is an ordinary one, in People & places ▸ Absences.
    await admin.goto(B + '/data/absences');
    await admin.waitForSelector(`text=Cleo Marchetti`);
    // And next Tuesday's Aerial now needs cover, on the plan.
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=timeline]');
    await admin.fill('[data-testid=day-date]', iso(nextTuesday));
    await admin.locator('[data-testid=day-date]').dispatchEvent('change');
    await admin.waitForFunction(d => document.querySelector('[data-testid=timeline]')?.dataset.day === d, iso(nextTuesday));
    const aerial = admin.locator('[data-testid=block][data-title=Aerial]').first();
    await aerial.click();
    await admin.waitForSelector('[data-testid=cover-picks]');
  });

  await step('yes to an offer makes them the cover, and nothing waits any more', async () => {
    if (!beforeThursday) { log('     (the sample week\'s Thursday has passed, so Dag\'s offer has too)'); return; }
    await nowPage(admin);
    await admin.locator('[data-testid=ask]', { hasText: 'Dag Øverlie' }).click();
    await admin.waitForSelector('[data-testid=dialog] >> text=Dag Øverlie can cover');
    const then = squash(await admin.locator('[data-testid=answer-then]').textContent());
    log('     ' + then);
    if (!then.startsWith('Dag Øverlie then: Free')) throw new Error('the form says ' + JSON.stringify(then));
    const away = squash(await admin.locator('[data-testid=answer-class]').textContent());
    if (!away.includes('Away: Ada Brightwater')) throw new Error('the form says ' + JSON.stringify(away));
    await admin.click('[data-testid=dialog-ok]');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await admin.waitForSelector('[data-testid=waiting-answer]', { state: 'detached' });
    const said = await toasts(admin);
    if (!said.some(t => t.startsWith('Said yes to an offer to cover Acrobatics'))) throw new Error('the messages say ' + JSON.stringify(said));
  });

  await step('the coach reads the answer, and is offered the class the yes left needing cover', async () => {
    await myWeek(coach);
    await coach.waitForSelector('[data-testid=could-cover-class]');
    const state = squash(await coach.locator('[data-testid=my-request] [data-testid=asked-state]').textContent());
    if (state !== 'Not this time') throw new Error('her request says ' + JSON.stringify(state));
    if (await coach.locator('[data-testid=my-request] [data-testid=take-back]').count()) throw new Error('an answered request can still be taken back');
    const could = await lines(coach, '[data-testid=could-cover-class]');
    log('     ' + could.join(' | '));
    if (could.length !== 1 || !could[0].startsWith(`Aerial ${short(nextTuesday)}, 09:00–11:00 Aerial Hall · BA 1`)) throw new Error('she is offered ' + JSON.stringify(could));
  });

  await step('an offer can be taken back while nobody has answered it, and made again', async () => {
    await coach.click('[data-testid=offer-cover]');
    await coach.waitForSelector('[data-testid=my-offer]');
    if (await coach.locator('[data-testid=could-cover]').count()) throw new Error('the class is still offered beside the offer');
    const offer = await parts(coach, '[data-testid=my-offer]');
    if (offer[0] !== `Cover Aerial, ${short(nextTuesday)}, 09:00–11:00 Offered, waiting for an answer Take back`) throw new Error('the offer reads ' + JSON.stringify(offer));
    await coach.click('[data-testid=my-offer] [data-testid=take-back]');
    await coach.waitForSelector('[data-testid=could-cover-class]');
    if (await coach.locator('[data-testid=my-offer]').count()) throw new Error('the offer is still listed');
    await coach.click('[data-testid=offer-cover]');
    await coach.waitForSelector('[data-testid=my-offer]');
  });

  await step("the administrator says yes from the class's side panel, where whoever offered comes first", async () => {
    await admin.goto(B + '/plan');
    await admin.waitForSelector('[data-testid=timeline]');
    await admin.fill('[data-testid=day-date]', iso(nextTuesday));
    await admin.locator('[data-testid=day-date]').dispatchEvent('change');
    await admin.waitForFunction(d => document.querySelector('[data-testid=timeline]')?.dataset.day === d, iso(nextTuesday));
    await admin.locator('[data-testid=block][data-title=Aerial]').first().click();
    await admin.waitForSelector('[data-testid=cover-pick]');
    const first = squash(await admin.locator('[data-testid=cover-pick]').first().textContent());
    log('     first: ' + first);
    if (!first.startsWith('AB Ada Brightwater Offered · Free')) throw new Error('the first to take it is ' + JSON.stringify(first));
    await admin.locator('[data-testid=cover-pick]').first().click();
    await admin.waitForSelector('[data-testid=cover-picks]', { state: 'detached' });
    // Pressing whoever offered is the yes to the offer, not only the cover, so she reads it as a yes.
    const said = await toasts(admin);
    if (!said.some(t => t.startsWith('Said yes to an offer to cover Aerial'))) throw new Error('the messages say ' + JSON.stringify(said));
    await nowPage(admin);
    if (await admin.locator('[data-testid=waiting-answer]').count()) throw new Error('Now still waits for ' + JSON.stringify(await waiting(admin)));
    await myWeek(coach);
    await coach.waitForFunction(() => document.querySelector('[data-testid=my-offer] [data-testid=asked-state]')?.textContent.trim() === 'Yes: you are covering it');
    // And the week she is covering in says so: next week's Aerial is on her week now.
    await coach.screenshot({ path: OUT + 'asking-answered.png' });
  });

  await step("a phone: the coach's lists and the form fit, and nothing scrolls sideways", async () => {
    const phone = await signIn(browser, 'coach', { width: 390, height: 844 });
    await myWeek(phone);
    await phone.waitForSelector('[data-testid=my-asking]');
    const wide = await phone.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    if (wide > 0) throw new Error('My week scrolls sideways by ' + wide + 'px');
    for (const li of await phone.locator('[data-testid=my-asking] li').all()) {
      const box = await li.boundingBox();
      if (box.x < 0 || box.x + box.width > 390) throw new Error('a line runs off the screen: ' + JSON.stringify(box));
    }
    await phone.click('[data-testid=cant-come]');
    await phone.waitForSelector('[data-testid=dialog] >> text=Can\'t come');
    await phone.waitForTimeout(400);
    const panel = await phone.locator('[data-testid=dialog]').boundingBox();
    if (panel.x < 0 || panel.x + panel.width > 391) throw new Error('the form is ' + JSON.stringify(panel));
    await phone.screenshot({ path: OUT + 'asking-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  console.log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
