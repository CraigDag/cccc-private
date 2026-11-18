// Who and what is free, asked in Ctrl+K in one line. An administrator's "free <next Monday> 13-15" comes first in
// Find, saying how many spaces and facilitators are free then and listing each with how long; Enter on a space
// opens the plan on that day narrowed to it, with nothing in it then, and Enter on the count opens that day on
// spaces. "who's free" lists only people and opens on people. "bo <next Monday> 11" says what he is in and opens
// his week. An editor is never offered a class titled with the words; the coach (Ada) asks too and is told the
// same. A phone fits it. Every date is in the sample's next week, so the walk runs on any day.
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
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };

// Each hit as a person reads it, with its kind from its badge's class.
const hits = page => page.$$eval('[data-testid=find-hit]', hs => hs.map(h => ({
  kind: [...h.querySelector('.find-kind').classList].find(c => c.startsWith('k-'))?.slice(2) ?? '',
  badge: h.querySelector('.find-kind').textContent.trim(),
  label: h.querySelector('.find-label').textContent.trim(),
  detail: h.querySelector('small').textContent.trim(),
})));
const lines = hs => hs.map(h => h.label + ' — ' + h.detail);

async function openFind(page) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
}
// Ctrl+K, the line typed, and the hits once the first hit is the label wanted with a detail that passes: a line
// read before its last words can make another answer, so the barrier is the whole of what the line says.
async function ask(page, line, label, detail) {
  await openFind(page);
  await page.keyboard.type(line);
  await page.waitForFunction(([label, detail]) => {
    const h = document.querySelector('[data-testid=find-hit]');
    return !!h && h.querySelector('.find-label').textContent.trim() === label && new RegExp(detail).test(h.querySelector('small').textContent.trim());
  }, [label, detail.source]).catch(async () => { throw new Error(`"${line}": the first hit is not "${label}" with ${detail}; Find shows ${JSON.stringify(lines(await hits(page)))}`); });
  return hits(page);
}
async function closeFind(page) {
  await page.keyboard.press('Escape');
  await page.waitForSelector('[data-testid=find]', { state: 'detached' });
}
async function tidy(page) {
  if (await page.locator('[data-testid=find]').count()) await closeFind(page);
}
// The number a count line says of a kind: "5 spaces", "1 facilitator", "no space".
const counted = (detail, one) => {
  const m = detail.match(new RegExp(`(\\d+|no) ${one}s?\\b`));
  if (!m) throw new Error(`"${detail}" says no number of ${one}s`);
  return m[1] === 'no' ? 0 : +m[1];
};
// The plan once it is on a day, with what it is narrowed to and its lens.
async function planOn(page, day) {
  await page.waitForFunction(day => document.querySelector('[data-testid=plan-page]')?.dataset.day === day, day)
    .catch(async () => { throw new Error(`the plan is on ${await page.getAttribute('[data-testid=plan-page]', 'data-day').catch(() => 'no plan')}, not ${day}`); });
  await page.waitForSelector('[data-testid=timeline]');
  return page.evaluate(() => ({
    lens: document.querySelector('[data-testid=plan-page]').dataset.lens,
    scale: document.querySelector('[data-testid=plan-page]').dataset.scale,
    focus: document.querySelector('[data-testid=focus-chip]')?.textContent.replace('×', '').trim() ?? null,
  }));
}
// The classes in the day's row of a narrowed plan, as minutes from midnight.
const pickedBlocks = page => page.$$eval('[data-testid=row].picked [data-testid=block]', bs => bs.map(b => ({ title: b.dataset.title, start: +b.dataset.start, end: +b.dataset.end })));

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  await admin.goto(B + '/plan');
  await admin.waitForSelector('[data-testid=timeline]');
  const today = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  // Next week is the last the sample builds: Bo and Ada teach Monday's 10:45 Acrobatics in the Big Top.
  const nextMonday = addDays(mondayOf(today), 7);
  const asked = `Free ${said(nextMonday)} 13:00–15:00`;
  const counts = /^(\d+ spaces?|no space) and (\d+ facilitators?|no facilitator)$/;
  log('     today', today, '· the Monday typed', nextMonday);
  let spaces = [];

  await step('a question typed into Ctrl+K comes first, saying how many spaces and facilitators are free, then each with how long', async () => {
    await admin.click('[data-testid=nav-now]');
    await admin.waitForSelector('[data-testid=now-page]');
    const found = await ask(admin, `free ${typed(nextMonday)} 13-15`, asked, counts);
    const [count, ...rest] = found;
    log('     ' + lines([count])[0]);
    eq([count.kind, count.badge], ['free', '○'], 'the count line');
    spaces = rest.filter(h => h.kind === 'room' && h.detail.startsWith('free'));
    const people = rest.filter(h => h.kind === 'facilitator' && h.detail.startsWith('free'));
    eq([spaces.length, people.length], [counted(count.detail, 'space'), counted(count.detail, 'facilitator')], 'the spaces and facilitators listed against the count');
    if (spaces.length === 0) throw new Error('no space is free on Monday 13:00–15:00 to go on with');
    eq(rest.slice(0, spaces.length).map(h => h.kind), spaces.map(() => 'room'), 'spaces come first');
    eq(spaces.map(h => h.label), [...spaces.map(h => h.label)].sort((a, b) => a.localeCompare(b, 'da')), 'spaces in name order');
    for (const h of [...spaces, ...people])
      if (!/^free (all day|until \d\d:\d\d|from \d\d:\d\d( until \d\d:\d\d)?)( · outside their hours)?$/.test(h.detail)) throw new Error(`${h.label} says "${h.detail}"`);
    if (rest.some(h => h.label === 'Gwen Stroud')) throw new Error('the visiting facilitator is listed');
    if (!(await admin.textContent('.find-foot')).includes('“free fri 13” to ask who is free')) throw new Error('the foot does not say a question can be asked');
    await admin.screenshot({ path: OUT + 'quick-free-find.png' });
  });

  await step('Enter on a space it lists opens the plan on that day narrowed to it, with nothing in it then', async () => {
    // A space with something else that day, so "nothing then" is a claim about the time and not about the day.
    const pick = Math.max(0, spaces.findIndex(h => h.detail !== 'free all day'));
    const space = spaces[pick];
    for (let i = 0; i <= pick; i++) await admin.keyboard.press('ArrowDown');
    await admin.waitForFunction(at => document.querySelectorAll('[data-testid=find-hit]')[at]?.closest('li')?.getAttribute('aria-selected') === 'true', pick + 1);
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=find]', { state: 'detached' });
    const plan = await planOn(admin, nextMonday);
    eq([plan.focus, plan.scale], [space.label, 'Day'], 'the plan');
    await admin.waitForSelector('[data-testid=row].picked');
    const there = await pickedBlocks(admin);
    const inTheWay = there.filter(b => b.start < 15 * 60 && b.end > 13 * 60);
    if (inTheWay.length) throw new Error(`${space.label} has ${JSON.stringify(inTheWay)} then`);
    if (space.detail !== 'free all day' && there.length === 0) throw new Error(`${space.label} says "${space.detail}" and has nothing that day`);
    log(`     ${space.label} (${space.detail}) on ${nextMonday}: ${there.map(b => `${b.title} ${b.start / 60}-${b.end / 60}`).join(', ') || 'nothing'}`);
    await admin.screenshot({ path: OUT + 'quick-free-space.png' });
  });

  await step('Enter on the count opens that day on spaces, narrowed to nothing, from the week', async () => {
    await admin.click('[data-testid=scale-week]');
    await admin.waitForFunction(() => document.querySelector('[data-testid=plan-page]')?.dataset.scale === 'Week');
    await ask(admin, `free ${typed(nextMonday)} 13-15`, asked, counts);
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=find]', { state: 'detached' });
    await admin.waitForFunction(() => !document.querySelector('[data-testid=focus-chip]'));
    const plan = await planOn(admin, nextMonday);
    eq([plan.lens, plan.scale, plan.focus], ['Spaces', 'Day', null], 'the plan');
  });

  await step('"who\'s free" lists only people, and its count opens that day on people', async () => {
    const found = await ask(admin, `who's free ${typed(nextMonday)} 13-15`, asked, /^(\d+ facilitators?|no facilitator)$/);
    const [count, ...rest] = found;
    eq(count.kind, 'free-people', 'the count line');
    const people = rest.filter(h => h.kind === 'facilitator' && h.detail.startsWith('free'));
    eq(people.length, counted(count.detail, 'facilitator'), 'the people listed against the count');
    if (rest.some(h => h.kind === 'room' && h.detail.startsWith('free'))) throw new Error('a space is listed');
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=find]', { state: 'detached' });
    const plan = await planOn(admin, nextMonday);
    eq([plan.lens, plan.focus], ['People', null], 'the plan');
  });

  await step('somebody named is told what they are in then, and Enter opens their week on that day', async () => {
    const found = await ask(admin, `bo ${typed(nextMonday)} 11`, 'Bo Lindqvist', new RegExp(`^${said(nextMonday)} 11:00 · Acrobatics in Big Top until 12:00$`));
    log('     ' + lines(found)[0]);
    if (found.some(h => h.kind === 'quick-add')) throw new Error('a class is offered to add');
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=find]', { state: 'detached' });
    const plan = await planOn(admin, nextMonday);
    eq(plan.focus, 'Bo Lindqvist', 'narrowed to');
    const there = await pickedBlocks(admin);
    if (!there.some(b => b.title === 'Acrobatics' && b.start <= 11 * 60 && b.end > 11 * 60)) throw new Error(`his Monday has ${JSON.stringify(there)}`);
  });
  await tidy(admin);

  const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
  await step('an editor asks the same and is never offered a class titled with the words', async () => {
    const found = await ask(editor, `free ${typed(nextMonday)} 13-15`, asked, counts);
    if (found.some(h => h.kind === 'quick-add')) throw new Error(`a class is offered: ${JSON.stringify(lines(found.filter(h => h.kind === 'quick-add')))}`);
    const foot = await editor.textContent('.find-foot');
    if (!foot.includes('to add a class') || !foot.includes('to ask who is free')) throw new Error(`the foot says "${foot}"`);
    await closeFind(editor);
  });
  await tidy(editor);

  const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
  await step('the coach asks too, about herself, and is told the same as anybody', async () => {
    const found = await ask(coach, `ada ${typed(nextMonday)} 11`, 'Ada Brightwater', new RegExp(`^${said(nextMonday)} 11:00 · Acrobatics in Big Top until 12:00$`));
    if (found.some(h => h.kind === 'quick-add' || h.kind === 'quick-ask')) throw new Error('something to do is offered');
    if (!(await coach.textContent('.find-foot')).includes('to ask who is free')) throw new Error('the foot does not say a question can be asked');
    await closeFind(coach);
    await ask(coach, `free ${typed(nextMonday)} 13-15`, asked, counts);
    await closeFind(coach);
  });
  await tidy(coach);

  const phone = await signIn(browser, 'admin', { width: 390, height: 844 });
  await step('the phone: the answer and its list fit the screen, and Enter opens that day', async () => {
    const found = await ask(phone, `free ${typed(nextMonday)} 13-15`, asked, counts);
    const wide = await phone.evaluate(() => {
      const over = [...document.querySelectorAll('[data-testid=find-hit]')].filter(h => h.getBoundingClientRect().right > innerWidth + 1).length;
      return { page: document.documentElement.scrollWidth > innerWidth + 1, over };
    });
    eq(wide, { page: false, over: 0 }, 'wider than the phone');
    if (found.length < 2) throw new Error('nothing listed');
    await phone.screenshot({ path: OUT + 'quick-free-phone.png' });
    await phone.keyboard.press('Enter');
    await phone.waitForSelector('[data-testid=find]', { state: 'detached' });
    eq((await planOn(phone, nextMonday)).lens, 'Spaces', 'the plan on a phone');
  });

  await browser.close();
  log(failed ? `${failed} FAILED` : 'all OK');
  process.exit(failed ? 1 : 0);
})();
