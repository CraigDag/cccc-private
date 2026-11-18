// Find a time: an editor asks for a facilitator and a group in a hall, and the list is the times they are all
// free, soonest first, with what the rule promised on it; choosing one fills in a new class there, with no
// clash in the form's own check; a class being added goes to Find a time and back without losing what was
// typed; Another time… on a class moves it inside its week, each person's part with it, marked as moved when
// asked, and Undo puts it back; Ctrl+K offers it, starting from whoever the plan is looking at; the class
// form counts an event as booking its space; a coach is offered none of it; and it fits a phone.
// Best on a preview started with AT=10:15, so the day's times are mid-morning wherever it is run.
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
async function step(what, fn) { try { await fn(); log('OK   ' + what); } catch (e) { failed++; log('FAIL ' + what + ': ' + why(e)); } }

async function signIn(browser, who, viewport) {
  const page = await (await browser.newContext({ viewport })).newPage();
  page.setDefaultTimeout(15000);
  page.on('pageerror', e => log('     page error (' + who + '): ' + e.message));
  await page.goto(B + '/signin');
  await page.fill('[data-testid=signin-login]', who);
  await page.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
  await page.click('[data-testid=signin-submit]');
  await page.waitForSelector('[data-testid=now-page]');
  return page;
}

const text = async (page, sel) => (await page.locator(sel).first().innerText()).replace(/\s+/g, ' ').trim();
const addDays = (iso, n) => { const d = new Date(iso + 'T12:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return d.toISOString().slice(0, 10); };
const mondayOf = iso => { const d = new Date(iso + 'T12:00:00Z'); return addDays(iso, -((d.getUTCDay() + 6) % 7)); };
const shortDate = iso => { const d = new Date(iso + 'T12:00:00Z'); return d.toLocaleDateString('en-GB', { weekday: 'short', timeZone: 'UTC' }) + ' ' + d.getUTCDate() + ' ' + d.toLocaleDateString('en-GB', { month: 'short', timeZone: 'UTC' }); };
const hm = m => String(Math.floor(m / 60)).padStart(2, '0') + ':' + String(m % 60).padStart(2, '0');
const minutes = s => +s.slice(0, 2) * 60 + +s.slice(3, 5);

// What the form says above its list, whichever it is drawing: nothing asked yet, the times, or none.
const SUM = '[data-testid=openings-sum], [data-testid=openings-none], [data-testid=find-time-start]';
const sum = page => text(page, SUM);
// A change made in the form, and the barrier after it: something only the NEW render draws.
async function changes(page, act) {
  const before = await sum(page);
  await act();
  await page.waitForFunction(([sel, b]) => { const s = document.querySelector(sel); return s && s.textContent.replace(/\s+/g, ' ').trim() !== b; }, [SUM, before]);
}
// The times listed, as a person reads them.
const openings = page => page.$$eval('[data-testid=opening]', os => os.map(o => ({
  date: o.closest('[data-testid=opening-day]').dataset.date,
  when: o.querySelector('[data-testid=opening-take]').textContent.trim(),
  where: (o.querySelector('.ft-where b')?.textContent || '').trim(),
  later: (o.querySelector('[data-testid=opening-later]')?.textContent || '').trim(),
  notes: [...o.querySelectorAll('[data-testid=opening-note]')].map(n => (n.classList.contains('good') ? '✓ ' : '! ') + n.textContent.trim()),
})));
const gaps = page => page.$$eval('[data-testid=opening-gap]', gs => gs.map(g => g.textContent.replace(/\s+/g, ' ').trim()));
const selected = (page, sel) => page.locator(sel).evaluate(s => s.options[s.selectedIndex].text);
const title = page => text(page, '[data-testid=dialog] #dialog-title');

(async () => {
  const browser = await chromium.launch();
  const ed = await signIn(browser, 'editor', { width: 1300, height: 900 });
  await ed.goto(B + '/plan');
  await ed.waitForSelector('[data-testid=timeline]');
  const today = await ed.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today), nextMonday = addDays(monday, 7);
  log('     today', today, '· the sample\'s next week starts', nextMonday);
  let chosen;

  await step('Find a time on the plan: Ada and BA 1 for 1h 30m in a hall over two weeks, soonest first, as the rule promised', async () => {
    await ed.click('[data-testid=find-time]');
    await ed.waitForSelector('[data-testid=find-time-start]');
    if (await title(ed) !== 'Find a time') throw new Error('the form is called "' + await title(ed) + '"');
    // A class alone asks nothing yet: the list waits for somebody, a group or a space.
    await ed.selectOption('[data-testid=find-time-class]', { label: 'Creation lab' });
    await changes(ed, () => ed.selectOption('[data-testid=find-time-add-facilitator]', { label: 'Ada Brightwater' }));
    // The pick list starts afresh once somebody is picked from it, rather than showing the next name.
    if (!/^Add another/.test(await selected(ed, '[data-testid=find-time-add-facilitator]'))) throw new Error('the pick list shows ' + await selected(ed, '[data-testid=find-time-add-facilitator]'));
    await changes(ed, () => ed.check('[data-testid=find-time-groups] label:has-text("BA 1") input'));
    await changes(ed, () => ed.selectOption('[data-testid=find-time-space]', 'kind:Hall'));
    await ed.selectOption('[data-testid=find-time-weeks]', '2');
    await ed.waitForSelector(`[data-testid=openings][data-to="${addDays(monday, 13)}"]`);
    const said = await sum(ed);
    log('     ' + said);
    if (!/^\d+ times? for 1h 30m when Ada Brightwater and BA 1 are all free and a Hall is/.test(said)) throw new Error('the list says "' + said + '"');
    const list = await openings(ed);
    if (list.length === 0) throw new Error('nothing is listed');
    for (let i = 0; i < list.length; i++) {
      const o = list[i], p = list[i - 1];
      if (o.date < today) throw new Error('a time before today: ' + JSON.stringify(o));
      if (p && (o.date < p.date || (o.date === p.date && o.when <= p.when))) throw new Error('out of order: ' + JSON.stringify(p) + ' then ' + JSON.stringify(o));
      if (!/^\d\d:\d\d–\d\d:\d\d$/.test(o.when) || minutes(o.when.slice(6)) - minutes(o.when) !== 90) throw new Error('not an hour and a half: ' + o.when);
      if (!/^(Big Top|Aerial Hall)$/.test(o.where)) throw new Error('not a hall: ' + JSON.stringify(o));
    }
    // Next Monday, Ada and BA 1 are free from 15:00, and the staff meeting is beside it (her team meets then).
    chosen = list.find(o => o.date === nextMonday && o.when === '15:00–16:30');
    if (!chosen || !chosen.notes.includes('! Staff meeting then, 15:30–16:30')) throw new Error('next Monday reads ' + JSON.stringify(list.filter(o => o.date === nextMonday)));
    // Ada is away this Thursday, all day: said, while it is still ahead.
    const thursday = addDays(monday, 3);
    if (thursday >= today) {
      const g = await gaps(ed);
      if (!g.some(x => x.startsWith(shortDate(thursday) + ' No time: Ada Brightwater is away'))) throw new Error('Thursday is not said: ' + JSON.stringify(g));
    }
    for (const o of list.slice(0, 4)) log('     ', o.date, o.when, o.where, o.later, '|', o.notes.join(' · '));
    await ed.screenshot({ path: OUT + 'findtime-list.png' });
  });

  await step('choosing a time fills in a new class there, the form finds no clash, and saving puts it on the plan', async () => {
    const take = ed.locator(`[data-testid=opening-day][data-date="${nextMonday}"] [data-testid=opening]`, { hasText: '15:00–16:30' }).locator('[data-testid=opening-take]');
    await take.click();
    await ed.waitForFunction(() => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === 'Add a class');
    const date = await ed.inputValue('[data-testid=session-date]');
    const times = [await ed.inputValue('[data-testid=session-start]'), await ed.inputValue('[data-testid=session-end]')];
    const space = await selected(ed, '[data-testid=session-room]');
    const cls = await selected(ed, '[data-testid=session-class]');
    const group = await selected(ed, '[data-testid=session-group]');
    const people = await ed.locator('[data-testid=session-facilitator]').allInnerTexts();
    log('     the form:', date, times.join('–'), space, '·', cls, '·', group, '·', people.map(p => p.split('\n')[0].trim()).join(', '));
    if (date !== nextMonday || times.join('–') !== '15:00–16:30' || !space.startsWith(chosen.where) || cls !== 'Creation lab' || group !== 'BA 1' || people.length !== 1 || !people[0].includes('Ada Brightwater'))
      throw new Error('the form does not hold the time chosen');
    const warned = await ed.locator('[data-testid=session-warnings]').count() ? await text(ed, '[data-testid=session-warnings]') : '';
    if (/double-booked|overlaps|booked twice/.test(warned)) throw new Error('the form warns: ' + warned);
    await ed.click('[data-testid=dialog-ok]');
    await ed.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    await ed.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Creation lab');
    const toasts = (await ed.locator('[data-testid=toast]').allInnerTexts()).map(t => t.replace(/\s+/g, ' ').trim());
    if (!toasts.some(t => t.startsWith('Added Creation lab on ' + shortDate(nextMonday)))) throw new Error('the messages say ' + JSON.stringify(toasts));
    const when = await text(ed, '.insp-when');
    if (when !== shortDate(nextMonday) + ' · 15:00–16:30') throw new Error('the panel says ' + when);
    if (await ed.getAttribute('[data-testid=timeline]', 'data-day') !== nextMonday) throw new Error('the plan did not follow it');
  });

  await step('a class being added goes to Find a time and back, by its button and by Escape, keeping what was typed', async () => {
    await ed.click('[data-testid=add-session]');
    await ed.waitForSelector('[data-testid=session-title]');
    await ed.fill('[data-testid=session-title]', 'Rigging check');
    await ed.press('[data-testid=session-title]', 'Tab');
    await ed.selectOption('[data-testid=session-group]', { label: 'BA 2' });
    await ed.selectOption('[data-testid=session-add-facilitator]', { label: 'Dag Øverlie' });
    await ed.waitForSelector('[data-testid=session-facilitator]');
    const back = async how => {
      await ed.click('[data-testid=session-find-time]');
      await ed.waitForFunction(() => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === 'Find a time for Rigging check');
      const facts = await text(ed, '[data-testid=find-time-facts]');
      if (!/Dag Øverlie/.test(facts) || !/BA 2/.test(facts) || !/1h 30m/.test(facts)) throw new Error('the form says ' + facts);
      await ed.waitForSelector('[data-testid=openings]');
      if (how === 'button') {
        if ((await text(ed, '[data-testid=dialog-close]')) !== 'Back to the class') throw new Error('the button says ' + await text(ed, '[data-testid=dialog-close]'));
        await ed.click('[data-testid=dialog-close]');
      } else await ed.keyboard.press('Escape');
      await ed.waitForFunction(() => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === 'Add a class');
      const kept = [await ed.inputValue('[data-testid=session-title]'), await selected(ed, '[data-testid=session-group]'), (await ed.locator('[data-testid=session-facilitator]').allInnerTexts()).join()];
      if (kept[0] !== 'Rigging check' || kept[1] !== 'BA 2' || !kept[2].includes('Dag Øverlie')) throw new Error('back by ' + how + ', the form holds ' + JSON.stringify(kept));
    };
    await back('button');
    await back('Escape');
    await ed.click('[data-testid=session-find-time]');
    await ed.waitForSelector('[data-testid=opening-take]');
    const first = (await openings(ed))[0];
    await ed.locator('[data-testid=opening-take]').first().click();
    await ed.waitForFunction(() => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === 'Add a class');
    const got = [await ed.inputValue('[data-testid=session-title]'), await ed.inputValue('[data-testid=session-date]'), await ed.inputValue('[data-testid=session-start]') + '–' + await ed.inputValue('[data-testid=session-end]')];
    log('     first time for it:', first.date, first.when, first.where, '→ the form:', got.join(' '));
    if (got[0] !== 'Rigging check' || got[1] !== first.date || got[2] !== first.when) throw new Error('the form holds ' + JSON.stringify(got));
    await ed.click('[data-testid=dialog-cancel]');
    await ed.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('Another time… moves a class inside its week, each part with it, marked as moved, and Undo puts it back', async () => {
    const block = ed.locator('[data-testid=timeline] [data-testid=block][data-title="Circus disciplines"]');
    await block.first().click();
    await ed.waitForFunction(() => document.querySelector('[data-testid=inspector-title]')?.textContent === 'Circus disciplines');
    const was = await text(ed, '.insp-when');
    await ed.click('[data-testid=inspector-find-time]');
    await ed.waitForFunction(() => document.querySelector('[data-testid=dialog] #dialog-title')?.textContent === 'Find another time for Circus disciplines');
    const facts = await text(ed, '[data-testid=find-time-facts]');
    if (!facts.includes('Dag Øverlie (from 14:00)')) throw new Error('the form says ' + facts);
    if (await ed.isChecked('[data-testid=find-time-say-moved]')) throw new Error('a week ahead is marked as moved before anybody asked');
    await ed.check('[data-testid=find-time-say-moved]');
    await ed.waitForSelector('[data-testid=opening-take]');
    const first = (await openings(ed))[0];
    if (mondayOf(first.date) !== nextMonday) throw new Error('a time outside its week: ' + JSON.stringify(first));
    await ed.screenshot({ path: OUT + 'findtime-another.png' });
    await ed.locator('[data-testid=opening-take]').first().click();
    await ed.waitForSelector('[data-testid=dialog]', { state: 'detached' });
    const start = minutes(first.when), end = minutes(first.when.slice(6));
    await ed.waitForFunction(w => document.querySelector('.insp-when')?.textContent.replace(/\s+/g, ' ').trim() === w, shortDate(first.date) + ' · ' + first.when);
    const toasts = (await ed.locator('[data-testid=toast]').allInnerTexts()).map(t => t.replace(/\s+/g, ' ').trim());
    if (!toasts.some(t => t.includes('Moved Circus disciplines to ' + shortDate(first.date) + ', ' + hm(start)))) throw new Error('the messages say ' + JSON.stringify(toasts));
    const dag = await ed.locator('.insp-person', { hasText: 'Dag Øverlie' }).innerText();
    if (!dag.includes(hm(start + 60) + '–' + hm(end))) throw new Error('Dag\'s part did not move with it: ' + dag);
    const moved = await text(ed, '[data-testid=inspector-moved]');
    if (moved !== 'from ' + was.replace(' · ', ', ')) throw new Error('it says it moved ' + moved);
    const tag = await ed.locator('[data-testid=timeline] [data-testid=block][data-title="Circus disciplines"] .blk-tag').allInnerTexts();
    if (!tag.includes('MOVED')) throw new Error('the block carries ' + JSON.stringify(tag));
    log('     moved to', first.date, first.when, '· Dag', dag.replace(/\s+/g, ' '), '·', moved);
    await ed.click('[data-testid=undo]');
    await ed.waitForFunction(w => document.querySelector('.insp-when')?.textContent.replace(/\s+/g, ' ').trim() === w, was);
    if (await ed.locator('[data-testid=inspector-moved]').count()) throw new Error('Undo left it saying it moved');
  });

  await step('Ctrl+K offers Find a time, starting from whoever the plan is looking at', async () => {
    await ed.keyboard.press('Escape');
    await ed.keyboard.press('Control+k');
    await ed.waitForSelector('[data-testid=find]');
    await ed.keyboard.type('Cleo');
    await ed.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent.startsWith('Cleo'));
    await ed.keyboard.press('Enter');
    await ed.waitForSelector('[data-testid=focus-chip]');
    await ed.keyboard.press('Control+k');
    await ed.waitForSelector('[data-testid=find]');
    await ed.keyboard.type('find a time');
    await ed.waitForFunction(() => document.querySelector('[data-testid=find-hit] .find-label')?.textContent === 'Find a time');
    await ed.keyboard.press('Enter');
    await ed.waitForSelector('[data-testid=find-time-person]');
    const people = await ed.locator('[data-testid=find-time-person]').allInnerTexts();
    if (people.length !== 1 || !people[0].includes('Cleo Marchetti')) throw new Error('it starts with ' + JSON.stringify(people));
    await ed.waitForSelector('[data-testid=openings-sum]');
    log('     ' + await sum(ed));
    await ed.click('[data-testid=dialog-close]');
    await ed.click('[data-testid=focus-chip] button');
    await ed.waitForSelector('[data-testid=focus-chip]', { state: 'detached' });
  });

  await step('the class form counts an event as booking its space, as Find a time does', async () => {
    const friday = addDays(monday, 4);
    await ed.click('[data-testid=add-session]');
    await ed.waitForSelector('[data-testid=session-date]');
    await ed.fill('[data-testid=session-date]', friday); await ed.press('[data-testid=session-date]', 'Tab');
    await ed.fill('[data-testid=session-start]', '16:00'); await ed.press('[data-testid=session-start]', 'Tab');
    await ed.fill('[data-testid=session-end]', '17:00'); await ed.press('[data-testid=session-end]', 'Tab');
    await ed.waitForFunction(() => [...document.querySelectorAll('[data-testid=session-room] option')].some(o => o.text.startsWith('Big Top — booked')));
    const big = await ed.locator('[data-testid=session-room] option', { hasText: 'Big Top' }).textContent();
    const free = await ed.locator('[data-testid=free-space]').allInnerTexts();
    log('     ' + big.trim(), '· free then:', free.join(', '));
    if (big.trim() !== 'Big Top — booked: Work-in-progress showing 16:00–17:30') throw new Error('Big Top reads ' + big);
    if (free.includes('Big Top') || !free.includes('Aerial Hall')) throw new Error('free then: ' + free.join(', '));
    await ed.click('[data-testid=dialog-cancel]');
    await ed.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('a coach is offered none of it: not on the plan, not in Ctrl+K, not on a class', async () => {
    const coach = await signIn(browser, 'coach', { width: 1300, height: 900 });
    await coach.goto(B + '/plan');
    await coach.waitForSelector('[data-testid=timeline] [data-testid=block]');
    if (await coach.locator('[data-testid=find-time]').count()) throw new Error('the plan offers it');
    await coach.keyboard.press('Control+k');
    await coach.waitForSelector('[data-testid=find]');
    await coach.keyboard.type('find a time');
    await coach.waitForFunction(() => document.querySelector('[data-testid=find-hit], .find-none'));
    const hits = await coach.locator('[data-testid=find-hit] .find-label').allInnerTexts();
    if (hits.includes('Find a time')) throw new Error('Ctrl+K offers it');
    await coach.keyboard.press('Escape');
    await coach.locator('[data-testid=timeline] [data-testid=block]').first().click();
    await coach.waitForSelector('[data-testid=inspector]');
    if (await coach.locator('[data-testid=inspector-find-time]').count()) throw new Error('a class offers it');
    await coach.context().close();
  });

  await step('the phone: Find a time fits the width, and its times can be pressed', async () => {
    const phone = await signIn(browser, 'editor', { width: 390, height: 844 });
    await phone.goto(B + '/plan');
    await phone.waitForSelector('[data-testid=find-time]');
    await phone.click('[data-testid=find-time]');
    await phone.waitForSelector('[data-testid=find-time-start]');
    await changes(phone, () => phone.selectOption('[data-testid=find-time-add-facilitator]', { label: 'Esme Halloran' }));
    await changes(phone, () => phone.check('[data-testid=find-time-groups] label:has-text("BA 2") input'));
    await phone.waitForSelector('[data-testid=opening-take]');
    const over = await phone.evaluate(() => {
      const body = document.querySelector('.dialog-body'), box = body.getBoundingClientRect();
      const out = [...body.querySelectorAll('.ft-open, .ft-bar, .ft-gap, [data-testid=opening-take]')].filter(e => e.getBoundingClientRect().right > box.right + 1);
      return { scroll: body.scrollWidth - body.clientWidth, out: out.length };
    });
    if (over.scroll > 1 || over.out > 0) throw new Error('wider than the phone: ' + JSON.stringify(over));
    const take = phone.locator('[data-testid=opening-take]').first();
    const hit = await take.evaluate(b => { const r = b.getBoundingClientRect(); const e = document.elementFromPoint(r.x + r.width / 2, r.y + r.height / 2); return e === b || b.contains(e); });
    if (!hit) throw new Error('the first time cannot be pressed');
    await phone.screenshot({ path: OUT + 'findtime-phone.png' });
    await phone.context().close();
  });

  await browser.close();
  log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
