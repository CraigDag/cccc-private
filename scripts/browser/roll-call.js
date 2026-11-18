// A roll call, for a fire drill or an evacuation, on the sample's Wednesday at 11:00 (DAY=wed AT=11:00): Juggling
// in Studio A and Aerial in the Aerial Hall, Bo's day still to come and Esme's over. Now and the phone's More offer
// it, and Ctrl+K finds it by "fire". Before it is called it lists who the timetable has where; it follows the
// timetable as it changes (Alba recorded away, a class Bo had this morning, an event in Studio B to count by eye).
// Called, every page shows a band to whoever may tick, and two phones tick at once: each sees the other's ticks, a
// whole space is ticked in one press, Still to find leaves who is missing, a name typed finds them, somebody not
// expected is ticked apart. Ended, it is forgotten for everybody. An account that is neither staff nor linked to a
// facilitator (a student) has no roll call. A phone fits, and the count stays in sight while the list scrolls.
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
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const DAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
const said = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${DAYS[d.getUTCDay()]} ${d.getUTCDate()} ${MONTHS[d.getUTCMonth()]}`; };
const typed = iso => { const d = new Date(iso + 'T12:00:00Z'); return `${d.getUTCDate()}/${d.getUTCMonth() + 1}`; };
const eq = (got, want, what) => { if (JSON.stringify(got) !== JSON.stringify(want)) throw new Error(`${what}: ${JSON.stringify(got)}, not ${JSON.stringify(want)}`); };
const titled = (page, t) => page.waitForFunction(t => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === t, t)
  .catch(async () => { throw new Error(`no "${t}" dialog; the dialog says ${JSON.stringify(await page.locator('[data-testid=dialog] #dialog-title').allInnerTexts())}`); });

async function toasted(page, act, words) {
  const last = await page.$$eval('[data-testid=toast]', ts => Math.max(0, ...ts.map(t => +t.dataset.toast)));
  await act();
  await page.waitForFunction(([last, words]) => [...document.querySelectorAll('[data-testid=toast]')]
    .some(t => +t.dataset.toast > last && t.querySelector('span').textContent.startsWith(words)), [last, words])
    .catch(async () => { throw new Error(`no new "${words}" toast; they say ${JSON.stringify(await page.locator('[data-testid=toast] span').allInnerTexts())}`); });
}

const hits = page => page.$$eval('[data-testid=find-hit]', hs => hs.map(h => h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim()));
// Types a line into Ctrl+K and waits until the first answer is the one whose words start so.
async function find(page, line, starts) {
  await page.keyboard.press('Control+k');
  await page.waitForSelector('[data-testid=find]');
  await page.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
  await page.keyboard.type(line);
  await page.waitForFunction(starts => {
    const h = document.querySelector('[data-testid=find-hit]');
    return h && (h.querySelector('.find-label').textContent.trim() + ' — ' + h.querySelector('small').textContent.trim()).startsWith(starts);
  }, starts).catch(async () => { throw new Error(`"${line}": the first hit does not start "${starts}"; Find shows ${JSON.stringify(await hits(page))}`); });
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

// The roll call as a person reads it: whether it runs, the count, each space and who is in it, who is between
// classes, and who is listed apart under each heading.
const roll = page => page.evaluate(() => {
  const words = el => el ? el.textContent.replace(/\s+/g, ' ').trim() : null;
  const person = b => ({
    name: words(b.querySelector('[data-testid=roll-name]')), found: b.dataset.found === 'yes', pressable: !b.disabled,
    tag: words(b.querySelector('.roll-tag')) ?? '', detail: words(b.querySelector('[data-testid=roll-detail]')) ?? '',
  });
  const root = document.querySelector('[data-testid=roll-call]');
  if (!root) return null;
  return {
    running: root.dataset.running === 'yes',
    found: words(document.querySelector('[data-testid=roll-found]')),
    left: words(document.querySelector('[data-testid=roll-left]')),
    eyesLeft: words(document.querySelector('[data-testid=roll-eyes-left]')),
    anyway: words(document.querySelector('[data-testid=roll-anyway]')),
    summary: words(document.querySelector('[data-testid=roll-summary]')),
    called: words(document.querySelector('[data-testid=roll-called]')),
    spaces: [...document.querySelectorAll('[data-testid=roll-space]')].map(s => ({
      name: words(s.querySelector('[data-testid=roll-space-name]')),
      count: words(s.querySelector('[data-testid=roll-space-count]')),
      in: words(s.querySelector('[data-testid=roll-space-in]')) ?? '',
      people: [...s.querySelectorAll('[data-testid=roll-person]')].map(person),
      eyes: [...s.querySelectorAll('[data-testid=roll-eye]')].map(e => ({ what: words(e.querySelector('[data-testid=roll-eye-what]')), found: e.dataset.found === 'yes', pressable: !e.disabled })),
      all: !!s.querySelector('[data-testid=roll-space-all]'),
    })),
    between: [...document.querySelectorAll('[data-testid=roll-between] [data-testid=roll-person]')].map(person),
    others: words(document.querySelector('[data-testid=roll-others] .now-section')),
    kinds: [...document.querySelectorAll('[data-testid=roll-kind]')].map(h => ({ heading: words(h), people: [...h.nextElementSibling.querySelectorAll('[data-testid=roll-person]')].map(person) })),
  };
});
const names = ps => ps.map(p => p.name);
// Waits for the count to read so, saying what it read instead. Short: every page is drawn again as a minute
// turns, which must not stand in for being told of a tick.
const LIVE = { timeout: 5000 };
const counted = (page, want) => page.waitForFunction(w => document.querySelector('[data-testid=roll-found]')?.textContent.trim() === w, want, LIVE)
  .catch(async () => { throw new Error(`the count is not "${want}" but ${JSON.stringify((await roll(page))?.found ?? null)}`); });
const personButton = (page, name) => page.locator('[data-testid=roll-person]', { has: page.locator(`[data-testid=roll-name]:text-is("${name}")`) });
async function tick(page, name, found) {
  await personButton(page, name).click();
  await page.waitForFunction(([n, f]) => [...document.querySelectorAll('[data-testid=roll-person]')]
    .some(b => b.querySelector('[data-testid=roll-name]').textContent.trim() === n && b.dataset.found === f), [name, found ? 'yes' : 'no']);
}
async function openRoll(page) {
  await page.goto(B + '/roll-call');
  await page.waitForSelector('[data-testid=roll-call]');
}
async function showOthers(page) {
  if (await page.locator('[data-testid=roll-kind]').count()) return;
  await page.click('[data-testid=roll-others-toggle]');
  await page.waitForSelector('[data-testid=roll-kind]');
}

const AERIAL = ['Dag Øverlie', 'Ines Isaksen', 'Jory Jepsen', 'Kaia Kestrel', 'Lior Lund', 'Maren Moberg', 'Nils Nygaard', 'Orla Ostrander', 'Piet Pell'];
const STUDIO_A = ['Cleo Marchetti', 'Alba Ashcombe', 'Benno Birkeland', 'Carys Corvino', 'Dmitri Dunmore', 'Elin Eskildsen', 'Fenna Falk', 'Gaspard Grummet', 'Hedda Hjort'];

(async () => {
  const browser = await chromium.launch();
  const admin = await signIn(browser, 'admin', { width: 1300, height: 900 });
  const coach = await signIn(browser, 'coach', { width: 390, height: 844 });
  const editor = await signIn(browser, 'editor', { width: 1300, height: 900 });
  // Today, as the preview's clock has it: the plan opens on it.
  await admin.goto(B + '/plan');
  const todayIso = await admin.getAttribute('[data-testid=plan-page]', 'data-day');
  log('     today', todayIso);
  await admin.goto(B + '/');
  await admin.waitForSelector('[data-testid=now-page]');

  await step('Now offers the roll call to whoever may call it, the phone\'s More does too, and Ctrl+K finds it by what it is for', async () => {
    for (const p of [admin, editor]) if (!(await p.locator('[data-testid=now-roll-call]').count())) throw new Error('no Roll call on Now');
    const first = (await find(admin, 'fire', 'Roll call — '))[0];
    eq(first, 'Roll call — Fire drill or evacuation · everybody in the building now, by space', 'the first hit for "fire"');
    await tidy(admin);
    await find(admin, 'evacuation', 'Roll call — ');
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=roll-call]');
    await admin.goto(B + '/');
    await admin.waitForSelector('[data-testid=now-page]');
    await coach.click('.dock-more > summary');
    await coach.waitForSelector('[data-testid=dock-roll-call]', { state: 'visible' });
    await coach.click('.dock-more > summary');
  });

  await step('before it is called, it lists who the timetable has where at this minute, nothing to tick yet, and who is not expected apart', async () => {
    await admin.click('[data-testid=now-roll-call]');
    await admin.waitForSelector('[data-testid=roll-call][data-running=no]');
    const r = await roll(admin);
    eq(r.spaces.map(s => [s.name, s.in, s.count]), [['Aerial Hall', 'Aerial until 12:00', '0 of 9'], ['Studio A', 'Juggling until 12:00', '0 of 9']], 'the spaces');
    eq(names(r.spaces[0].people), AERIAL, 'who is in the Aerial Hall, facilitator first');
    eq(names(r.spaces[1].people), STUDIO_A, 'who is in Studio A');
    eq([r.spaces[0].people[0].tag, r.spaces[0].people[1].tag], ['Facilitator', ''], 'who is a facilitator');
    if (r.spaces.some(s => s.all || s.people.some(p => p.pressable))) throw new Error('something can be ticked before the roll is called');
    eq(r.summary, '2 facilitators and 16 students expected.', 'the summary');
    eq(r.others, 'Not expected in a space now · 2', 'the heading over who is not expected');
    if (await admin.locator('[data-testid=roll-kind]').count()) throw new Error('who is not expected is shown before it is asked for');
    await showOthers(admin);
    eq((await roll(admin)).kinds.map(k => [k.heading, k.people.map(p => p.name + ' — ' + p.detail)]),
      [['First class still to come', ['Bo Lindqvist — Open training in Big Top at 13:00']],
       ['Classes over for today', ['Esme Halloran — Strength and conditioning in Gym, until 10:30']]], 'who is not expected');
    const start = squash(await admin.textContent('[data-testid=roll-start]'));
    if (!/^Call the roll at \d\d:\d\d$/.test(start)) throw new Error('the button says ' + JSON.stringify(start));
    await admin.screenshot({ path: OUT + 'roll-call-before.png' });
  });

  await step('the list follows the timetable: Alba recorded away, a class Bo had this morning, and an event in Studio B to count by eye', async () => {
    if (!/^\d{4}-\d\d-\d\d$/.test(todayIso ?? '')) throw new Error('the plan did not say what day it is: ' + JSON.stringify(todayIso));
    await find(admin, 'alba sick today', `Record Alba Ashcombe away · ${said(todayIso)}`);
    await admin.keyboard.press('Enter');
    await admin.waitForSelector('[data-testid=away-takes]');
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Recorded an absence');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await find(admin, `open training everyone ${typed(todayIso)} 10-10:45 big top bo`, `Add Open training · ${said(todayIso)} 10:00–10:45`);
    await admin.keyboard.press('Enter');
    await titled(admin, 'Add a class');
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), `Added Open training on ${said(todayIso)}`);
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await find(admin, 'add an event', 'Add an event');
    await admin.keyboard.press('Enter');
    await titled(admin, 'New event');
    await admin.fill('[data-testid=event-title]', 'Fire safety talk');
    await admin.press('[data-testid=event-title]', 'Tab');
    await admin.check('[data-testid=dialog] label.check:has-text("All day") input');
    await admin.selectOption('[data-testid=dialog] label.field:has-text("Space") select', { label: 'Studio B' });
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Added the event Fire safety talk');
    await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });

    await openRoll(admin);
    const r = await roll(admin);
    eq(r.spaces.map(s => [s.name, s.in, s.count]),
      [['Aerial Hall', 'Aerial until 12:00', '0 of 9'], ['Studio A', 'Juggling until 12:00', '0 of 8'], ['Studio B', 'Fire safety talk, all day', null]], 'the spaces');
    eq(names(r.spaces[1].people), STUDIO_A.filter(n => n !== 'Alba Ashcombe'), 'who is in Studio A, Alba away');
    eq(r.spaces[2].eyes.map(e => e.what), ['Fire safety talk, an event'], 'what to count by eye in Studio B');
    eq(r.between.map(p => p.name + ' — ' + p.detail), ['Bo Lindqvist — Open training in Big Top until 10:45, then Open training in Big Top at 13:00'], 'who is between classes');
    eq(r.summary, '3 facilitators and 15 students expected, and 1 class or event to count by eye.', 'the summary');
    await showOthers(admin);
    eq((await roll(admin)).kinds.map(k => [k.heading, k.people.map(p => p.name + ' — ' + p.detail)]),
      [['Classes over for today', ['Esme Halloran — Strength and conditioning in Gym, until 10:30']], ['Recorded away', ['Alba Ashcombe — ']]], 'who is not expected');
  });

  let calledAt = null;
  await step('called, it counts from none found, and every page shows the band to whoever may tick, as it starts', async () => {
    await editor.goto(B + '/plan');
    await editor.waitForSelector('[data-testid=timeline]');
    if (await coach.locator('[data-testid=roll-band]').count() || await editor.locator('[data-testid=roll-band]').count()) throw new Error('a band before anything is called');
    await admin.click('[data-testid=roll-start]');
    await admin.waitForSelector('[data-testid=roll-call][data-running=yes]');
    const r = await roll(admin);
    eq([r.found, r.left, r.eyesLeft, r.anyway], ['0 of 18 found', '18 still to find', '1 class or event still to count by eye', null], 'the count');
    calledAt = (r.called.match(/^Called at (\d\d:\d\d) by Preview Administrator\.$/) ?? [])[1];
    if (!calledAt) throw new Error('it says ' + JSON.stringify(r.called));
    if (!r.spaces.every(s => s.people.every(p => p.pressable) && s.eyes.every(e => e.pressable))) throw new Error('somebody cannot be ticked');
    if (await admin.locator('[data-testid=roll-band]').count()) throw new Error('the band is on the roll call itself');
    const band = `A roll call is running, called at ${calledAt} by Preview Administrator: 0 of 18 found. Open the roll call`;
    for (const [who, p] of [['coach', coach], ['editor', editor]]) {
      await p.waitForSelector('[data-testid=roll-band]', LIVE).catch(() => { throw new Error(`no band on the ${who}'s page`); });
      eq(squash(await p.textContent('[data-testid=roll-band]')), band, `the band on the ${who}'s page`);
    }
    await admin.screenshot({ path: OUT + 'roll-call-called.png' });
  });

  await step("two phones tick at once and each sees the other's ticks, a whole space in one press", async () => {
    await coach.click('[data-testid=roll-band-open]');
    await coach.waitForSelector('[data-testid=roll-call][data-running=yes]');
    await tick(coach, 'Benno Birkeland', true);
    await counted(admin, '1 of 18 found');
    eq((await roll(admin)).spaces[1].people.find(p => p.name === 'Benno Birkeland').found, true, "Benno ticked on the administrator's page");
    // A found name is filled in, so a glance down a space shows who is still to find: read with the pointer
    // away from it, since on a phone nothing hovers and the fill alone has to say it.
    await coach.mouse.move(1, 1);
    await coach.waitForFunction(() => !document.querySelector('[data-testid=roll-person]:hover'));
    const fill = await coach.evaluate(() => {
      const bg = n => getComputedStyle([...document.querySelectorAll('[data-testid=roll-person]')].find(b => b.querySelector('[data-testid=roll-name]').textContent.trim() === n)).backgroundColor;
      const probe = document.createElement('span');
      probe.style.background = 'var(--selection)';
      document.body.append(probe);
      const selection = getComputedStyle(probe).backgroundColor;
      probe.remove();
      return { found: bg('Benno Birkeland') === selection, notFound: bg('Carys Corvino') !== selection };
    });
    eq(fill, { found: true, notFound: true }, 'a found name, filled in');
    await admin.click('[data-testid=roll-space]:has-text("Aerial Hall") [data-testid=roll-space-all]');
    await counted(admin, '10 of 18 found');
    await counted(coach, '10 of 18 found');
    const r = await roll(coach);
    eq([r.spaces[0].count, r.spaces[0].people.every(p => p.found), r.spaces[0].all, r.spaces[1].count], ['9 of 9', true, false, '1 of 8'], "the coach's spaces");
    await editor.waitForFunction(() => document.querySelector('[data-testid=roll-band-count]')?.textContent.trim() === '10 of 18 found', null, LIVE)
      .catch(async () => { throw new Error(`the editor's band says ${JSON.stringify(await editor.textContent('[data-testid=roll-band-count]'))}`); });
  });

  await step('Still to find leaves who is missing, a name typed finds them, and the count stays in sight as the list scrolls', async () => {
    await admin.click('[data-testid=roll-show-left]');
    await admin.waitForFunction(() => ![...document.querySelectorAll('[data-testid=roll-space-name]')].some(h => h.textContent.trim() === 'Aerial Hall'));
    const r = await roll(admin);
    eq(r.spaces.map(s => s.name), ['Studio A', 'Studio B'], 'the spaces still to find');
    eq(names(r.spaces[0].people), STUDIO_A.filter(n => n !== 'Alba Ashcombe' && n !== 'Benno Birkeland'), 'who is still to find in Studio A');
    eq(names(r.between), ['Bo Lindqvist'], 'who is still to find between classes');
    await admin.fill('[data-testid=roll-find]', 'hed');
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=roll-person]').length === 1);
    eq((await roll(admin)).spaces.map(s => [s.name, names(s.people), s.eyes.length]), [['Studio A', ['Hedda Hjort'], 0]], 'what "hed" finds');
    // Found already, a name typed still finds them, ticked; Everybody shows them all again.
    await admin.fill('[data-testid=roll-find]', 'benno');
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=roll-person]').length === 0);
    await admin.click('[data-testid=roll-show-all]');
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=roll-person]').length === 1);
    eq((await roll(admin)).spaces[0].people.map(p => [p.name, p.found]), [['Benno Birkeland', true]], 'Benno, found');
    await admin.fill('[data-testid=roll-find]', '');
    await admin.waitForFunction(() => document.querySelectorAll('[data-testid=roll-space]').length === 3);

    // On the phone, scrolled to the foot, the count is still at the top of the page.
    await coach.evaluate(() => { const p = document.querySelector('.shell-page'); p.scrollTop = p.scrollHeight; });
    const sticky = await coach.evaluate(() => {
      const page = document.querySelector('.shell-page').getBoundingClientRect(), bar = document.querySelector('.roll-sticky').getBoundingClientRect();
      const at = document.elementFromPoint(bar.left + 20, bar.top + bar.height / 2);
      return { scrolled: document.querySelector('.shell-page').scrollTop > 0, top: Math.round(bar.top - page.top), seen: !!at?.closest('.roll-sticky') };
    });
    eq(sticky, { scrolled: true, top: 0, seen: true }, 'the count, scrolled to the foot');
    await coach.screenshot({ path: OUT + 'roll-call-phone-scrolled.png' });
    await coach.evaluate(() => { document.querySelector('.shell-page').scrollTop = 0; });
  });

  await step('somebody not expected who turns up is ticked apart, a tick taken back is taken back for everybody, and the event is counted by eye', async () => {
    await showOthers(coach);
    await tick(coach, 'Esme Halloran', true);
    await admin.waitForFunction(() => document.querySelector('[data-testid=roll-anyway]')?.textContent.trim() === 'and 1 found who was not expected', null, LIVE);
    eq((await roll(admin)).found, '10 of 18 found', 'the count, Esme found');
    await tick(coach, 'Benno Birkeland', false);
    await counted(admin, '9 of 18 found');
    // The band counts only who was expected, as the page does: Esme, found apart, is not one of the nine.
    await editor.waitForFunction(() => document.querySelector('[data-testid=roll-band-count]')?.textContent.trim() === '9 of 18 found', null, LIVE)
      .catch(async () => { throw new Error(`the editor's band says ${JSON.stringify(await editor.textContent('[data-testid=roll-band-count]'))}`); });
    await admin.click('[data-testid=roll-eye]');
    await coach.waitForFunction(() => !document.querySelector('[data-testid=roll-eyes-left]'), null, LIVE);
    eq((await roll(coach)).spaces[2].eyes.map(e => e.found), [true], 'the event, counted');
  });

  await step('ending it asks once, then forgets every tick for everybody, and the band goes', async () => {
    await admin.click('[data-testid=roll-end]');
    await titled(admin, 'End the roll call');
    eq(squash(await admin.textContent('[data-testid=roll-end-left]')), '9 people the timetable expected have not been found.', 'what ending says');
    eq(squash(await admin.textContent('[data-testid=dialog-ok]')), 'End it', 'the button');
    await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'The roll call has ended. Its ticks are forgotten.');
    await admin.waitForSelector('[data-testid=roll-call][data-running=no]');
    await coach.waitForSelector('[data-testid=roll-call][data-running=no]', LIVE);
    await editor.waitForSelector('[data-testid=roll-band]', { state: 'detached', ...LIVE });
    if ((await roll(coach)).spaces.some(s => s.people.some(p => p.found))) throw new Error('a tick is still shown after it ended');
    // Called again, it starts from nobody found.
    await admin.click('[data-testid=roll-start]');
    await counted(admin, '0 of 18 found');
    await counted(coach, '0 of 18 found');
  });

  await step('an account that is a student, linked to nobody, has no roll call: not on Now, not in Ctrl+K, no band, and told why', async () => {
    const account = async (staff, linkedTo) => {
      await admin.goto(B + '/admin/accounts');
      await admin.locator('[data-testid=account-row]', { hasText: 'Ada Brightwater' }).click();
      await titled(admin, 'Ada Brightwater');
      await admin.setChecked('[data-testid=dialog] label:has-text("Staff") input[type=checkbox]', staff);
      await admin.setChecked('[data-testid=dialog] label:has-text("Student") input[type=checkbox]', !staff);
      await admin.selectOption('[data-testid=dialog] label.field:has-text("Linked to a facilitator") select', { label: linkedTo });
      await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'Changed Ada Brightwater');
      await admin.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    };
    await account(false, 'Not linked');
    try {
      const student = await signIn(browser, 'coach', { width: 390, height: 844 });
      if (await student.locator('[data-testid=now-roll-call]').count()) throw new Error('a student is offered the roll call on Now');
      if (await student.locator('[data-testid=roll-band]').count()) throw new Error('a student sees the band while a roll call runs');
      await student.keyboard.press('Control+k');
      await student.waitForSelector('[data-testid=find]');
      await student.waitForFunction(() => document.activeElement?.getAttribute('data-testid') === 'find-input');
      await student.keyboard.type('fire');
      // An empty line lists every action, so wait for what only "fire" draws: nothing found, or the roll call.
      await student.waitForFunction(() => document.querySelector('.find-none')
        || [...document.querySelectorAll('[data-testid=find-hit] .find-label')].some(l => l.textContent.trim() === 'Roll call'))
        .catch(async () => { throw new Error('"fire", for a student: Find shows ' + JSON.stringify(await hits(student))); });
      const found = await hits(student);
      if (found.some(h => h.startsWith('Roll call'))) throw new Error('Ctrl+K offers a student the roll call: ' + JSON.stringify(found));
      await tidy(student);
      await student.click('.dock-more > summary');
      if (await student.locator('[data-testid=dock-roll-call]').count()) throw new Error("the phone's More offers a student the roll call");
      await student.goto(B + '/roll-call');
      await student.waitForSelector('[data-testid=roll-not-yours], [data-testid=roll-call]');
      if (!(await student.locator('[data-testid=roll-not-yours]').count())) throw new Error('the roll call page does not say it is for staff');
      if (await student.locator('[data-testid=roll-person], [data-testid=roll-start]').count()) throw new Error('a student is shown the list');
      await student.context().close();
    } finally {
      // Put back as it was, whatever happened, so the phone below is the coach's; then end the roll call.
      await tidy(admin);
      await account(true, 'Ada Brightwater');
      await openRoll(admin);
      await admin.click('[data-testid=roll-end]');
      await titled(admin, 'End the roll call');
      await toasted(admin, () => admin.click('[data-testid=dialog-ok]'), 'The roll call has ended.');
    }
  });

  await step('a phone fits: nothing scrolls sideways, every name is big enough to press', async () => {
    const phone = await signIn(browser, 'coach', { width: 360, height: 740 });
    await openRoll(phone);
    await phone.click('[data-testid=roll-start]');
    await phone.waitForSelector('[data-testid=roll-call][data-running=yes]');
    const fit = await phone.evaluate(() => ({
      wide: document.scrollingElement.scrollWidth - document.scrollingElement.clientWidth,
      page: document.querySelector('.shell-page').scrollWidth - document.querySelector('.shell-page').clientWidth,
      small: [...document.querySelectorAll('[data-testid=roll-person], [data-testid=roll-eye], [data-testid=roll-end], [data-testid=roll-space-all], [data-testid=roll-show-all], [data-testid=roll-show-left], [data-testid=roll-find]')]
        .filter(b => b.getBoundingClientRect().height < 44).map(b => b.dataset.testid + ' ' + (b.textContent || b.placeholder).trim()),
      pressables: document.querySelectorAll('[data-testid=roll-person]').length,
      outside: [...document.querySelectorAll('[data-testid=roll-name]')].filter(n => n.getBoundingClientRect().right > n.closest('[data-testid=roll-person]').getBoundingClientRect().right + 0.5).map(n => n.textContent),
    }));
    if (fit.pressables < 18) throw new Error(`only ${fit.pressables} names on the phone`);
    delete fit.pressables;
    eq(fit, { wide: 0, page: 0, small: [], outside: [] }, 'the phone');
    await phone.screenshot({ path: OUT + 'roll-call-phone.png' });
    await phone.click('[data-testid=roll-end]');
    await titled(phone, 'End the roll call');
    await toasted(phone, () => phone.click('[data-testid=dialog-ok]'), 'The roll call has ended.');
    await phone.context().close();
  });

  await browser.close();
  log(failed ? `${failed} failed` : 'all passed');
  process.exit(failed ? 1 : 0);
})();
