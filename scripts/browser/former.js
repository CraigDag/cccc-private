// What deleting keeps: a class, facilitator or space used before today stays, former, for the days it
// was used, folded under its list with Bring back; a week that ran still shows it, the weeks ahead are
// taken off it; two classes merge into one, the ended weeks included, and Undo puts every week back.
// Against a fresh preview (made-up data).
const { chromium } = require('playwright');
const OUT = (process.env.OUT || require('os').tmpdir() + '/cccc-browser') + '/'; require('fs').mkdirSync(OUT, { recursive: true });
const B = process.env.CCCC_PREVIEW || 'http://127.0.0.1:5299';
const log = (...a) => console.log(...a);
const ymd = d => d.toISOString().slice(0, 10);
const plus = (day, n) => { const d = new Date(day + 'T00:00:00Z'); d.setUTCDate(d.getUTCDate() + n); return ymd(d); };
const mondayOf = day => { const d = new Date(day + 'T00:00:00Z'); return plus(day, -((d.getUTCDay() + 6) % 7)); };
(async () => {
  const browser = await chromium.launch();
  const errors = [];
  let failed = 0;
  let page;
  const step = async (name, fn) => { try { await fn(); log('OK  ', name); } catch (e) { failed++; log('FAIL', name, e.message.split('\n')[0]); await page.screenshot({ path: `${OUT}fail-${name.replace(/\W+/g,'-').slice(0, 80)}.png` }); } };
  const signIn = async (who, viewport) => {
    const p = await (await browser.newContext({ viewport })).newPage();
    p.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
    p.on('pageerror', e => errors.push(e.message));
    await p.goto(B + '/signin');
    await p.fill('[data-testid=signin-login]', who);
    await p.fill('[data-testid=signin-passphrase]', 'northlight ' + who);
    await p.click('[data-testid=signin-submit]');
    await p.waitForSelector('[data-testid=now-page]');
    return p;
  };

  // A Data list: the rows in use, and the former ones folded under it.
  const lists = p => p.evaluate(() => {
    const rows = [...document.querySelectorAll('[data-testid=data-row]')];
    const read = r => ({ name: r.querySelector('.name').textContent.trim(), detail: r.querySelector('small').textContent.trim() });
    return { current: rows.filter(r => !r.closest('[data-testid=data-former]')).map(read).map(r => r.name),
             former: rows.filter(r => r.closest('[data-testid=data-former]')).map(read),
             fold: document.querySelector('[data-testid=data-former] summary')?.textContent.trim() ?? null };
  });
  const dataList = async (p, slug) => { await p.goto(B + '/data/' + slug); await p.waitForSelector('[data-testid=data-row]'); return lists(p); };
  // Opens a row's form, from the fold when it is former.
  const openRow = async (p, name, fromFold = false) => {
    if (fromFold) {
      const fold = p.locator('[data-testid=data-former]');
      if (!(await fold.evaluate(d => d.open))) await fold.locator('summary').click();
    }
    const scope = fromFold ? '[data-testid=data-former] ' : '';
    await p.locator(scope + '[data-testid=data-row]', { has: p.locator('.name', { hasText: new RegExp('^' + name + '$') }) }).first().click();
    await p.waitForSelector('[data-testid=dialog]');
    return (await p.textContent('[data-testid=former-note]')).trim();
  };
  // Presses a form's button and waits for the form to go; a change refused says why in a warning instead.
  const press = async (p, testid) => {
    const warned = await p.locator('[data-testid=toast].warn').count();
    await p.click(`[data-testid=dialog] [data-testid=${testid}]`);
    await p.waitForFunction(n => !document.querySelector('[data-testid=dialog]') || document.querySelectorAll('[data-testid=toast].warn').length > n, warned);
    if (await p.locator('[data-testid=dialog]').count()) throw new Error('refused: ' + (await p.locator('[data-testid=toast].warn').last().textContent()).trim());
  };
  const waitFold = (p, text) => p.waitForFunction(t => (document.querySelector('[data-testid=data-former] summary')?.textContent.trim() ?? null) === t, text);

  // The plan on a day: every block's title and the colour it is drawn in (null: no class, so no colour).
  const onDay = async (p, day) => {
    if (!(await p.locator('[data-testid=plan-page]').count())) {
      await p.goto(B + '/plan');
      await p.waitForSelector('[data-testid=plan-page][data-scale=Day]');
    }
    await p.fill('[data-testid=day-date]', day);
    await p.waitForSelector(`[data-testid=plan-page][data-day="${day}"]`);
    await p.waitForSelector('[data-testid=timeline]');
    return p.evaluate(() => ({
      blocks: [...document.querySelectorAll('[data-testid=timeline] [data-testid=block]')].map(b => ({
        title: b.dataset.title, colour: (/--c:\s*([^;]+)/.exec(b.getAttribute('style') || '') || [])[1]?.trim().toUpperCase() ?? null })),
      rows: [...document.querySelectorAll('[data-testid=timeline] [data-testid=row] .tl-label')].map(l => l.title),
    }));
  };
  const titled = (d, title) => d.blocks.filter(b => b.title === title);
  const undo = async p => { await p.click('[data-testid=undo]'); };

  page = await signIn('admin', { width: 1400, height: 900 });
  await page.goto(B + '/plan');
  await page.waitForSelector('[data-testid=plan-page][data-scale=Day]');
  const today = await page.getAttribute('[data-testid=plan-page]', 'data-day');
  const monday = mondayOf(today), lastMonday = plus(monday, -7), nextMonday = plus(monday, 7);
  log('      today', today, '· a week that ran', lastMonday, '· a week ahead', nextMonday);

  // Juggling: Mondays, Wednesdays and Fridays in each of the four built weeks, drawn in #5FB0C8.
  let ran = 0;
  await step('Data ▸ Classes: deleting a class that ran says it will be kept, and keeps it folded under the list', async () => {
    await dataList(page, 'classes');
    const note = await openRow(page, 'Juggling');
    log('     ', note);
    const m = /^Deleting keeps it as a former class for the (\d+) classes it ran before today, and takes it off the (\d+) classes from today on, which keep their name\.$/.exec(note);
    if (!m) throw new Error('the form says: ' + note);
    ran = +m[1];
    if (ran < 6 || +m[2] < 3) throw new Error(`${ran} before today and ${m[2]} ahead: the two built weeks behind and the one ahead hold three each`);
    await press(page, 'data-delete');
    await waitFold(page, '1 former class');
    const l = await lists(page);
    if (l.current.includes('Juggling')) throw new Error('Juggling is still among the classes in use');
    if (l.former.length !== 1 || l.former[0].name !== 'Juggling') throw new Error('the fold holds ' + JSON.stringify(l.former));
    if (!new RegExp(`^Former since \\w{3} \\d{1,2} \\w{3} · ${ran} classes before then$`).test(l.former[0].detail)) throw new Error('the former row says: ' + l.former[0].detail);
    await page.locator('[data-testid=data-former] summary').click();
    await page.waitForSelector('[data-testid=data-former][open] [data-testid=data-row]');
    await page.screenshot({ path: OUT + 'former-classes.png' });
  });

  await step('a week that ran still shows Juggling in its colour; the week ahead keeps the name with no class', async () => {
    const before = await onDay(page, lastMonday);
    const was = titled(before, 'Juggling');
    if (was.length !== 1 || was[0].colour !== '#5FB0C8') throw new Error(`${lastMonday}: ${JSON.stringify(was)}`);
    const ahead = await onDay(page, nextMonday);
    const now = titled(ahead, 'Juggling');
    if (now.length !== 1 || now[0].colour !== null) throw new Error(`${nextMonday}: ${JSON.stringify(now)}`);
  });

  await step('nothing ahead offers a former class: a new class\'s picker leaves it out', async () => {
    await page.click('[data-testid=add-session]');
    await page.waitForSelector('[data-testid=dialog] [data-testid=session-class]');
    const offered = await page.locator('[data-testid=session-class] option').allTextContents();
    if (offered.some(o => o.startsWith('Juggling'))) throw new Error('offered: ' + offered.join(', '));
    if (!offered.includes('Acrobatics')) throw new Error('the picker is not the class picker: ' + offered.join(', '));
    await page.click('[data-testid=dialog-x]');
    await page.waitForSelector('[data-testid=dialog]', { state: 'detached' });
  });

  await step('Bring back offers it again and leaves the classes ahead as they are; Undo twice puts everything back', async () => {
    await dataList(page, 'classes');
    const note = await openRow(page, 'Juggling', true);
    if (!/^A former class, since \w{3} \d{1,2} \w{3}\. Kept for the \d+ classes it ran before then.* Bringing it back offers it again; it does not put it back on the classes it was taken off\.$/.test(note))
      throw new Error('the former form says: ' + note);
    if (await page.locator('[data-testid=dialog] [data-testid=data-delete]').count()) throw new Error('a former class offers Delete');
    await page.waitForTimeout(400); // the form's slide, for the picture only
    await page.screenshot({ path: OUT + 'former-class-form.png' });
    await press(page, 'bring-back');
    await page.waitForSelector('[data-testid=data-former]', { state: 'detached' });
    if (!(await lists(page)).current.includes('Juggling')) throw new Error('Juggling is not back among the classes in use');
    if (titled(await onDay(page, nextMonday), 'Juggling')[0]?.colour !== null) throw new Error('bringing back put the class back on the week ahead');
    await undo(page);   // the bring-back
    await undo(page);   // the delete
    await page.waitForFunction(() => document.querySelector('[data-testid=timeline] [data-testid=block][data-title="Juggling"]')?.getAttribute('style')?.toUpperCase().includes('#5FB0C8'));
    const l = await dataList(page, 'classes');
    if (!l.current.includes('Juggling') || l.fold !== null) throw new Error('after two Undos: ' + JSON.stringify(l));
  });

  // Creation lab (Creation, with Prep time) into Contemporary dance (Body and movement): the past included.
  const lastTuesday = plus(lastMonday, 1), nextTuesday = plus(nextMonday, 1);
  await step('Merge into… says what changes beyond the name, then re-points every week, the ended ones too', async () => {
    await dataList(page, 'classes');
    await openRow(page, 'Creation lab');
    await page.click('[data-testid=dialog] [data-testid=class-merge]');
    await page.waitForSelector('[data-testid=dialog] [data-testid=merge-keep]');
    if (!(await page.isDisabled('[data-testid=dialog-ok]'))) throw new Error('Merge can be pressed before a class to keep is chosen');
    const offered = await page.locator('[data-testid=merge-keep] option').allTextContents();
    if (offered.includes('Creation lab')) throw new Error('a class is offered to merge into itself');
    await page.selectOption('[data-testid=merge-keep]', { label: 'Contemporary dance' });
    await page.waitForSelector('[data-testid=merge-said] li');
    const said = (await page.locator('[data-testid=merge-said] li').allTextContents()).map(s => s.trim());
    log('     ', said.join(' / '));
    if (!/^\d+ classes will count as Contemporary dance, the days already run included\.$/.test(said[0])) throw new Error('first: ' + said[0]);
    for (const line of ['Their hours count towards Body and movement, not Creation.', 'Prep time is no longer credited for them.'])
      if (!said.includes(line)) throw new Error(`"${line}" is not said: ${said.join(' / ')}`);
    if (said.length !== 3) throw new Error('said more than the three differences: ' + said.join(' / '));
    await page.waitForTimeout(400); // the form's slide, for the picture only
    await page.screenshot({ path: OUT + 'merge-class.png' });
    // OK goes through although last week has ended: re-pointing is the one change let in.
    await press(page, 'dialog-ok');
    const l = await lists(page);
    if (l.current.includes('Creation lab') || l.former.some(f => f.name === 'Creation lab')) throw new Error('Creation lab is still listed: ' + JSON.stringify(l));
    const was = titled(await onDay(page, lastTuesday), 'Creation lab');
    if (was.length !== 1 || was[0].colour !== '#F07090') throw new Error(`${lastTuesday} (ended): ${JSON.stringify(was)}`);
    const ahead = await onDay(page, nextTuesday);
    if (titled(ahead, 'Creation lab').length || titled(ahead, 'Contemporary dance').length !== 2) throw new Error(`${nextTuesday}: ${JSON.stringify(ahead.blocks)}`);
  });

  await step('Undo puts the merged class back, in every week', async () => {
    await undo(page);
    await page.waitForFunction(() => !!document.querySelector('[data-testid=timeline] [data-testid=block][data-title="Creation lab"]'));
    const ahead = await onDay(page, nextTuesday);
    if (titled(ahead, 'Creation lab')[0]?.colour !== '#70C8A0') throw new Error(`${nextTuesday}: ${JSON.stringify(ahead.blocks)}`);
    const was = titled(await onDay(page, lastTuesday), 'Creation lab');
    if (was[0]?.colour !== '#70C8A0') throw new Error(`${lastTuesday} (ended): ${JSON.stringify(was)}`);
    if (!(await dataList(page, 'classes')).current.includes('Creation lab')) throw new Error('Creation lab is not back in the list');
  });

  await step('Data ▸ Facilitators: removing one who taught keeps them for the hours report, and Find calls them former', async () => {
    await dataList(page, 'facilitators');
    const note = await openRow(page, 'Esme Halloran');
    const m = /^Removing keeps them as a former facilitator for the (\d+) classes they taught before today, and takes them off the (\d+) classes from today on\.$/.exec(note);
    if (!m) throw new Error('the form says: ' + note);
    if ((await page.textContent('[data-testid=dialog] [data-testid=data-delete]')).trim() !== 'Remove') throw new Error('a person is not Removed');
    await press(page, 'data-delete');
    await waitFold(page, '1 former facilitator');
    await page.goto(B + '/reports/facilitators');
    await page.waitForSelector('[data-testid=hours-by-week] [data-testid=hours-row]');
    const row = page.locator('[data-testid=hours-row][data-name="Esme Halloran"]');
    if (!(await row.count())) throw new Error('the hours report has lost the weeks Esme taught');
    log('      hours report keeps Esme:', (await row.locator('[data-testid=hours-total]').textContent()).trim());
    await page.keyboard.press('Control+k');
    await page.waitForSelector('[data-testid=find]');
    await page.fill('[data-testid=find-input]', 'Esme');
    // Wait for her own hit: with nothing typed yet the box lists what can be done, which is a hit too.
    const esme = page.locator('[data-testid=find-hit]', { has: page.locator('.find-label', { hasText: /^Esme Halloran$/ }) });
    await esme.waitFor();
    const hit = (await esme.locator('small').textContent()).trim();
    if (!hit.startsWith('Former facilitator, since')) throw new Error('Find says: ' + hit);
    await page.keyboard.press('Escape');
    await page.waitForSelector('[data-testid=find]', { state: 'detached' });
    const ahead = await onDay(page, nextMonday);
    if (titled(ahead, 'Strength and conditioning').length !== 1) throw new Error('the class Esme taught ahead is gone, not just her');
  });

  await step('a former facilitator brought back is offered again', async () => {
    await dataList(page, 'facilitators');
    const note = await openRow(page, 'Esme Halloran', true);
    if (!note.startsWith('A former facilitator, since')) throw new Error('the former form says: ' + note);
    await press(page, 'bring-back');
    await page.waitForSelector('[data-testid=data-former]', { state: 'detached' });
    if (!(await lists(page)).current.includes('Esme Halloran')) throw new Error('Esme is not back');
  });

  await step('Data ▸ Spaces: deleting a space that was used keeps it for the weeks that ran, and the plan ahead has no row for it', async () => {
    await dataList(page, 'rooms');
    const note = await openRow(page, 'Gym');
    if (!/^Deleting keeps it as a former space for the \d+ classes held in it before today; the \d+ classes from today on keep running, with no space\.$/.test(note)) throw new Error('the form says: ' + note);
    await press(page, 'data-delete');
    await waitFold(page, '1 former space');
    const was = await onDay(page, lastMonday);
    if (!was.rows.includes('Gym')) throw new Error(`${lastMonday} (ran) has no Gym row: ${was.rows.join(', ')}`);
    const ahead = await onDay(page, nextMonday);
    if (ahead.rows.includes('Gym')) throw new Error(`${nextMonday} still has a Gym row`);
    if (!ahead.rows.includes('No space given')) throw new Error(`${nextMonday}: the classes that were in the Gym have no row: ${ahead.rows.join(', ')}`);
  });

  await step('on a phone the fold fits and the page does not scroll sideways', async () => {
    const phone = await (await browser.newContext({ viewport: { width: 390, height: 844 }, storageState: await page.context().storageState() })).newPage();
    phone.on('pageerror', e => errors.push(e.message));
    await phone.goto(B + '/data/rooms');
    await phone.waitForSelector('[data-testid=data-former] summary');
    await phone.click('[data-testid=data-former] summary');
    await phone.waitForSelector('[data-testid=data-former][open] [data-testid=data-row]');
    const wide = await phone.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
    if (wide > 1) throw new Error(`the page is ${wide}px wider than the phone`);
    const box = await phone.locator('[data-testid=data-former] [data-testid=data-row]').boundingBox();
    if (!box || box.x < 0 || box.x + box.width > 391) throw new Error('the former row is outside the phone: ' + JSON.stringify(box));
    await phone.screenshot({ path: OUT + 'former-phone.png' });
    await phone.context().close();
  });

  log('errors', JSON.stringify(errors));
  await browser.close();
  if (errors.length) failed++;
  console.log(failed ? failed + ' failed' : 'all passed');
  process.exit(failed ? 1 : 0);
})();
