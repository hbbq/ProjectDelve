// Separately reviewed Edge acceptance, fixed to the isolated QA origin. No persistent profile.
// Run with the installed Node runtime; .codex runtime manifest supplies installed Playwright.
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '..');
const runtime = JSON.parse(fs.readFileSync(path.join(root, '.codex/browser-qa.runtime.json'), 'utf8').replace(/^\uFEFF/, ''));
const { chromium } = require(runtime.playwright);
const origin = 'http://127.0.0.1:5081';
const output = fs.mkdtempSync(path.join(root, '.playwright-cli/fog-acceptance-'));
const fixture = JSON.parse(fs.readFileSync(path.join(root, 'ProjectDelve.Engine.Tests/Fixtures/fog-patrol.json'), 'utf8'));

(async () => {
  const browser = await chromium.launch({ channel:'msedge', headless:false });
  const evidence = { checks:[], errors:[], requests:[] };
  let page;
  try {
    const context = await browser.newContext({ viewport:{width:1440,height:1000}, serviceWorkers:'block' });
    await context.route('**/*', route => new URL(route.request().url()).origin === origin ? route.continue() : route.abort());
    page = await context.newPage();
    page.on('pageerror', e => evidence.errors.push(e.message));
    page.on('console', m => { if (m.type() === 'error') evidence.errors.push(m.text()); });
    page.on('requestfailed', r => { if (!r.failure()?.errorText.includes('ERR_ABORTED')) evidence.requests.push({url:r.url(),error:r.failure()?.errorText}); });
    await page.goto(origin);
    await page.waitForFunction(() => !document.querySelector('#import-scenario').disabled);
    async function details(selector, open) {
      if ((await page.locator(selector).evaluate(el => el.open)) !== open) await page.locator(`${selector} > summary`).click();
      // Native details toggle events are queued; the app opens Game in that handler.
      await page.waitForTimeout(50);
      assert.equal(await page.locator(selector).evaluate(el => el.open), open);
    }

    async function importFixture(definition) {
      const exported = await context.request.post(`${origin}/api/designer/export`, {data:definition});
      assert.equal(exported.status(), 200, await exported.text());
      const {transport} = await exported.json();
      await details('#playtest-tools', true); await details('#game-setup', true);
      await page.locator('#scenario-transport').fill(transport);
      await page.locator('#import-scenario').click();
      await page.waitForFunction(() => document.querySelector('#board [data-unit-id="hero"]') && !document.querySelector('#board [data-unit-id="patrol"]') && !document.querySelector('#start-round').disabled && document.querySelector('#start-round').textContent === 'Start round');
      await details('#playtest-tools', false);
    }
    async function setView(view, animate) {
      await details('#playtest-tools', true); await details('#tools-display', true);
      await page.locator('#board-view').selectOption(view);
      await page.locator('#animate').setChecked(animate);
      await details('#tools-display', false); await details('#playtest-tools', false);
    }
    async function driveRound(useTools = false) {
      await page.locator(useTools ? '#round' : '#start-round').click();
      // Token order is random. Stay and End Turn are the Hero's only submitted choices.
      // Each locator waits through authoritative playback before sending the next decision.
      for (let i = 0; i < 5; i++) {
        await page.waitForFunction(() => !document.querySelector('#start-round').disabled || !document.querySelector('#end-turn').disabled || document.querySelector('#board [data-unit-id="hero"]')?.getAttribute('aria-disabled') === 'false');
        const state = (await (await context.request.get(`${origin}/api/game`)).json()).result.state;
        if (state.roundComplete) break;
        if (state.currentUnitId === 'hero') {
          const hero = page.locator('#board [data-unit-id="hero"]');
          if (await page.locator('#end-turn').isEnabled()) {
            if (useTools) { await page.locator('#end-turn').focus(); await page.locator('#end-turn').press('Enter'); }
            else await page.locator('#end-turn').click();
          }
          else await hero.click();
        }
        await page.waitForTimeout(60);
      }
      await page.waitForFunction(() => !document.querySelector('#start-round').disabled && document.querySelector('#start-round').textContent.includes('Next'));
    }
    async function startProbe() {
      await page.evaluate(() => {
        window.motionFrames = []; window.motionDone = false;
        const probe = window.motionProbe = (window.motionProbe ?? 0) + 1;
        const sample = () => {
          const node = document.querySelector('#board .movement-figure');
          if (node) {
            const b = document.querySelector('#board').getBoundingClientRect(), r = node.getBoundingClientRect();
            const masks = [...document.querySelectorAll('#board .movement-fog')];
            window.motionFrames.push({time:performance.now(), x:((r.x+r.width/2-b.x)/b.width)*14-.5,
              masks:masks.length, opaque:masks.every(m => getComputedStyle(m).backgroundColor === 'rgb(22, 27, 34)' && Number(getComputedStyle(m).zIndex) > Number(getComputedStyle(node).zIndex)),
              stableHidden:[...document.querySelectorAll('#board [data-unit-id="patrol"]')].every(n => getComputedStyle(n).visibility === 'hidden'),
              interactive:node.getAttribute('aria-hidden') !== 'true' || getComputedStyle(node).pointerEvents !== 'none'});
          }
          if (!window.motionDone && window.motionProbe === probe) requestAnimationFrame(sample);
        };
        requestAnimationFrame(sample);
      });
    }
    for (const view of ['classic','enhanced']) {
      const door = structuredClone(fixture);
      door.board.edges = [{position:{x:6,y:2},direction:'Right',kind:'OpenDoor'}];
      const phase = structuredClone(fixture);
      phase.unitTypeIds[1] = 'ghost-type'; phase.units[1].unitTypeId = 'ghost-type'; phase.agency[1].unitTypeId = 'ghost-type'; phase.orders[0].unitTypeId = 'ghost-type';
      phase.board.cells = [{position:{x:7,y:2},terrain:'SolidRock'}];
      phase.board.edges = [{position:{x:6,y:2},direction:'Right',kind:'ClosedDoor'}];
      phase.waypoints[1].area.x = 6;
      for (const [geometry, definition] of [['plain',fixture],['door',door],['phase',phase]]) {
      await importFixture(definition); await setView(view, true);
      for (const direction of ['enter','leave']) {
        await startProbe();
        const driver = driveRound();
        driver.catch(() => {});
        await page.waitForFunction(direction => window.motionFrames.length >= 4 && (direction !== 'leave' || window.motionFrames.at(-1).x > 6.2), direction);
        await page.screenshot({path:path.join(output, `${view}-${geometry}-${direction}.png`),fullPage:true});
        await driver;
        const frames = await page.evaluate(() => { window.motionDone = true; return window.motionFrames; });
        assert.ok(frames.length >= 10, `${view} ${direction}: enough actual animation frames`);
        assert.ok(frames.at(-1).time-frames[0].time >= (geometry === 'phase' ? 180 : 350), 'movement must persist across frames');
        assert.ok(frames.every(f => f.opaque && f.stableHidden && !f.interactive && f.masks > 0));
        assert.ok(Math.max(...frames.map(f=>f.x))-Math.min(...frames.map(f=>f.x)) > (geometry === 'phase' ? .7 : 1), 'movement must change position visibly');
        assert.equal(await page.locator('#board [data-unit-id="patrol"]').count(), direction === 'enter' ? 1 : 0);
        assert.equal(await page.locator('#board .movement-figure, #board .movement-fog').count(),0);
        const narration = await page.locator('#events').innerText();
        assert.ok(!/\b[78],2\b/.test(narration), 'hidden endpoints absent from narration');
        evidence.checks.push({view,geometry,direction,frames:frames.length,duration:frames.at(-1).time-frames[0].time,startX:frames[0].x,endX:frames.at(-1).x});
      }
      }
      for (const mode of ['disabled','skip']) {
        await importFixture(fixture); await setView(view, mode !== 'disabled');
        if (mode === 'skip') { await details('#playtest-tools', true); await details('#game-setup', false); await details('#tools-gameplay', true); }
        await startProbe();
        const driver = driveRound(mode === 'skip');
        driver.catch(() => {});
        if (mode === 'skip') { await page.waitForFunction(() => window.motionFrames.length >= 2); await page.locator('#skip').click(); }
        await driver;
        const frames = await page.evaluate(() => { window.motionDone = true; return window.motionFrames; });
        assert.equal(await page.locator('#board [data-unit-id="patrol"]').count(),1);
        assert.equal(await page.locator('#board .movement-figure, #board .movement-fog').count(),0);
        if (mode === 'disabled') assert.equal(frames.length,0);
        evidence.checks.push({view,mode,frames:frames.length});
        if (mode === 'skip') { await details('#tools-gameplay', false); await details('#playtest-tools', false); }
      }
    }
    assert.deepEqual(evidence.errors, []); assert.deepEqual(evidence.requests, []);
    await context.close();
  } catch (error) {
    evidence.failure = error.message;
    if (page) { await page.screenshot({path:path.join(output,'failure.png'),fullPage:true}); evidence.details = await page.locator('details').evaluateAll(nodes => nodes.map(n => ({id:n.id,open:n.open}))); }
    throw error;
  } finally {
    fs.writeFileSync(path.join(output,'report.json'),JSON.stringify(evidence,null,2));
    await browser.close();
    console.log(output);
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
