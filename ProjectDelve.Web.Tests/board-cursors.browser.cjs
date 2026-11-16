// Pass a Playwright page connected to the isolated 5081 QA host. Replaces only its game.
// No client state, candidates, hits or responses are fabricated. Coordinate labels are
// the public display surface used to locate ground Cells in the orthographic views.
module.exports = async function boardCursorsBrowser(page) {
  const assert = require('node:assert/strict');
  const base = 'http://127.0.0.1:5081', errors = [], decisions = [], results = [];
  const onError = error => errors.push(error.message);
  const onRequest = request => {
    if (request.url() === `${base}/api/game/commands` && request.postDataJSON()?.operation === 'decision')
      decisions.push(request.postDataJSON());
  };
  page.on('pageerror', onError); page.on('request', onRequest);
  const read = async () => (await page.request.get(`${base}/api/game`)).json();
  const idle = () => page.waitForFunction(() => document.querySelector('#board').dataset.busy === 'false' && !document.querySelector('#filter').disabled);
  const settings = async callback => {
    await page.locator('#open-game-menu').click(); await page.locator('#menu-settings').click();
    await callback(); await page.locator('#close-game-tools').click(); await idle();
  };
  const open = async selector => {
    if (!await page.locator(selector).evaluate(node => node.open)) await page.locator(`${selector} > summary`).click();
  };
  const filter = checked => settings(async () => { await open('#tools-gameplay'); await page.locator('#filter').setChecked(checked); });
  const fixture = {
    board: { width: 7, height: 6, defaultTerrain: 'StoneFloor', cells: [], edges: [] },
    unitTypeIds: ['wizard-type'],
    units: ['blue', 'red'].map((sideId, index) => ({ unitTypeId: 'wizard-type', sideId,
      anchor: { x: 2 + index, y: 2 }, posture: 'Upright', unitId: `cursor-${sideId}` })),
    agency: ['blue', 'red'].map(sideId => ({ unitTypeId: 'wizard-type', sideId, controller: 'Human' }))
  };
  const largeFixture = {
    board: { width: 9, height: 7, defaultTerrain: 'StoneFloor', cells: [], edges: [] },
    unitTypeIds: ['red-dragon-type'],
    units: ['blue', 'red'].map((sideId, index) => ({ unitTypeId: 'red-dragon-type', sideId,
      anchor: { x: 2 + index * 2, y: 2 }, posture: 'Upright', unitId: `large-cursor-${sideId}` })),
    agency: ['blue', 'red'].map(sideId => ({ unitTypeId: 'red-dragon-type', sideId, controller: 'Human' }))
  };
  let release;
  try {
    await page.setViewportSize({ width: 1440, height: 1000 });
    await page.goto(`${base}/#play`); await idle();
    await page.evaluate(async () => { const cursors = await import('/presentation-cursors.js'); await cursors.loadCursorAssets(); });
    const exported = await page.request.post(`${base}/api/designer/export`, { data: fixture });
    assert.equal(exported.ok(), true, await exported.text());
    const { transport } = await exported.json();
    const largeExport = await page.request.post(`${base}/api/designer/export`, { data: largeFixture });
    assert.equal(largeExport.ok(), true, await largeExport.text());
    const { transport: largeTransport } = await largeExport.json();
    assert.equal(await page.evaluate(value => globalThis.delvePlayDesign(value), transport), true);
    await idle();
    await settings(async () => {
      await open('#tools-gameplay'); await page.locator('#auto').uncheck(); await page.locator('#filter').uncheck();
      await open('#tools-display'); await page.locator('#coordinates').check(); await page.locator('#animate').uncheck(); await page.locator('#cursor-size').selectOption('40');
    });
    await page.waitForFunction(async () => !(await (await fetch('/api/game')).json()).autoChooseSingleRelevantChoice);
    for (const view of ['tabletop', 'three', 'enhanced']) {
      await settings(async () => { await open('#tools-display'); await page.locator('#board-view').selectOption(view); });
      const three = view !== 'enhanced', host = page.locator(three ? '#three-viewport' : '#camera-viewport');
      await host.waitFor({ state: 'visible' });
      const cameraDisclosure = page.locator('.view-disclosure');
      if (!await cameraDisclosure.evaluate(node => node.open)) await cameraDisclosure.locator(':scope > summary').click();
      await page.locator(three ? '#three-camera-mode' : '#camera-mode').selectOption('full');
      if (view === 'tabletop') await page.locator('#three-camera-projection').selectOption('orthographic');
      await cameraDisclosure.locator(':scope > summary').click();
      const fresh = async (scenario = transport) => {
        assert.equal(await page.evaluate(value => globalThis.delvePlayDesign(value), scenario), true);
        await idle(); await page.locator('#start-round').click();
        await page.waitForFunction(() => !document.querySelector('#stay').hidden && !document.querySelector('#stay').disabled);
        await idle();
      };
      // DOM inspection supplies screen geometry only; actual input goes through mouse events.
      const point = async cell => {
        if (!three) {
          const box = await page.locator(`#board .cell[data-cell="${cell.x},${cell.y}"]`).boundingBox();
          assert.ok(box, `${view}: Cell ${JSON.stringify(cell)} exists`);
          return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
        }
        await page.waitForFunction(() => document.querySelectorAll('.three-coordinates span').length >= 3);
        return page.locator('#three-viewport').evaluate((node, cell) => {
          const labels = [...node.querySelectorAll('.three-coordinates span')];
          const at = (x, y) => labels.find(label => label.textContent === `${x},${y}`);
          const origin = labels.map(label => label.textContent.split(',').map(Number))
            .find(([x, y]) => at(x + 1, y) && at(x, y + 1));
          if (!origin) throw Error('Need adjacent public coordinate labels to locate Cell center');
          const [x, y] = origin, a = at(x, y), b = at(x + 1, y), c = at(x, y + 1), rect = node.getBoundingClientRect();
          const left = label => parseFloat(label.style.left), top = label => parseFloat(label.style.top);
          const dx = cell.x + .5 - (x + .05), dy = cell.y + .5 - (y + .05);
          return { x: rect.left + left(a) + dx * (left(b) - left(a)) + dy * (left(c) - left(a)),
            y: rect.top + top(a) + dx * (top(b) - top(a)) + dy * (top(c) - top(a)) };
        }, cell);
      };
      const cursor = async (cell, expected) => {
        const p = await point(cell); await page.mouse.move(p.x, p.y);
        await page.waitForFunction(({ p, expected }) => {
          const node = document.elementFromPoint(p.x, p.y);
          if (!node) return false;
          const cursor = getComputedStyle(node).cursor;
          const assets = ['move', 'attack', 'fireball', 'choose', 'invalid'];
          return assets.includes(expected) ? cursor.includes(`/generated/40/${expected}.png`) && cursor.includes('20 20,') : cursor === expected;
        }, { p, expected });
        return p;
      };
      const click = async (cell, expected) => { const p = await cursor(cell, expected); await page.mouse.click(p.x, p.y); };
      const submitted = async (count, candidate) => {
        await page.waitForFunction(() => document.querySelector('#board').dataset.busy === 'false');
        assert.equal(decisions.length, count + 1, `${view}: exactly one decision`);
        assert.equal(decisions.at(-1).candidateKey, candidate.key, `${view}: authoritative candidate`);
        await idle();
      };
      await fresh();
      let state = await read();
      const move = state.presentation.decision.candidates.find(c => c.boardVisual?.kind === 'move' && c.interaction.kind === 'Position');
      assert.ok(move, `${view}: fixture exposes an authoritative Move`);
      let count = decisions.length;
      // Verify both ordinary dragging and Space-drag preserve camera ownership,
      // show grabbing during capture and restore the real choice cursor afterward.
      for (const space of [false, true]) {
        await host.focus(); const start = await cursor(move.interaction.position, 'move');
        if (space) await page.keyboard.down('Space');
        await page.mouse.down(); const end = { x: start.x - (space ? 3 : 20), y: start.y };
        await page.mouse.move(end.x, end.y, { steps: 3 });
        await page.waitForFunction(p => getComputedStyle(document.elementFromPoint(p.x, p.y)).cursor === 'grabbing', end);
        await page.mouse.up(); if (space) await page.keyboard.up('Space');
        await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
        await cursor(move.interaction.position, 'move');
        assert.equal(decisions.length, count, `${view}: camera drag does not submit`);
      }
      // Keep a real command pending to exercise busy input suppression deterministically.
      let intercepted;
      const waiting = new Promise(resolve => { intercepted = resolve; });
      const gate = new Promise(resolve => { release = resolve; });
      let finishHandler, handlerStarted = false, handlerError, operationError;
      const handlerFinished = new Promise(resolve => { finishHandler = resolve; });
      const route = async requestRoute => {
        handlerStarted = true; intercepted();
        try { await gate; await requestRoute.continue(); }
        catch (error) { handlerError = error; }
        finally { finishHandler(); }
      };
      await page.route(`${base}/api/game/commands`, route);
      try {
        await click(move.interaction.position, 'move'); await waiting;
        await page.waitForFunction(() => document.querySelector('#board').dataset.busy === 'true');
        await click(move.interaction.position, 'default');
        assert.equal(decisions.length, count + 1, `${view}: busy click submits nothing`);
      } catch (error) { operationError = error; }
      finally {
        release(); release = null;
        if (handlerStarted) await handlerFinished;
        try { await page.unroute(`${base}/api/game/commands`, route); }
        catch (error) { handlerError ??= error; }
      }
      if (operationError) {
        if (handlerError) operationError.message += `\nRoute cleanup also failed: ${handlerError.message}`;
        throw operationError;
      }
      if (handlerError) throw handlerError;
      await submitted(count, move);
      const moved = await read();
      assert.deepEqual(moved.result.state.physical.figures.find(f => f.id === state.presentation.decision.unitId).position, move.interaction.position);

      await fresh(); await page.locator('#stay').click(); await idle(); state = await read();
      const attack = state.presentation.decision.candidates.find(c => c.boardVisual?.kind === 'attack' && c.interaction.kind === 'Unit');
      assert.ok(attack, `${view}: fixture exposes an authoritative Unit Attack`);
      const foe = state.result.state.physical.figures.find(f => f.id === attack.interaction.unitId).position;
      const coincident = state.presentation.decision.candidates.filter(c => c.interaction.kind === 'Unit' && c.interaction.unitId === attack.interaction.unitId ||
        c.interaction.kind === 'Position' && c.interaction.position.x === foe.x && c.interaction.position.y === foe.y);
      assert.ok(coincident.length > 1, `${view}: actual ambiguity exists`);
      count = decisions.length; await click(foe, 'choose');
      await page.locator('.board-chooser').waitFor({ state: 'visible' });
      assert.equal(decisions.length, count, `${view}: chooser does not submit`);
      assert.equal(await page.locator('.board-chooser button').count(), coincident.length + 1);
      await page.locator('.board-chooser button').last().click();
      await page.locator('#play-actions .action-chip').filter({ hasText: /^Attack/ }).first().click();
      await click(foe, 'attack'); await submitted(count, attack);
      // Pending Roll Dice has no board-target candidate; hovering/clicking cannot roll it.
      state = await read();
      assert.ok(state.presentation.decision.candidates.every(c => c.interaction.kind === 'Direct'));
      count = decisions.length; await click(foe, 'grab'); assert.equal(decisions.length, count);

      await fresh(); await page.locator('#stay').click(); await idle(); state = await read();
      const fireballs = state.presentation.decision.candidates.filter(c => c.entryId === 'ability:fireball');
      const irrelevant = fireballs.find(c => c.relevant === false && !state.result.state.physical.figures.some(f =>
        f.position.x === c.interaction.position.x && f.position.y === c.interaction.position.y));
      assert.ok(irrelevant, `${view}: legal irrelevant empty Fireball target`);
      await page.locator('#play-actions .action-chip').filter({ hasText: /^Fireball/ }).click();
      count = decisions.length;
      const board = state.result.state.physical.board;
      const invalid = Array.from({ length: board.width * board.height }, (_, index) =>
        ({ x: index % board.width, y: Math.floor(index / board.width) })).find(cell =>
        !fireballs.some(c => c.interaction.position.x === cell.x && c.interaction.position.y === cell.y));
      assert.ok(invalid, `${view}: a board Cell absent from supplied Fireball targets`);
      await click(invalid, 'invalid'); assert.equal(decisions.length, count);
      await filter(true);
      await click(irrelevant.interaction.position, 'invalid'); assert.equal(decisions.length, count);
      await filter(false);
      await click(irrelevant.interaction.position, 'fireball'); await submitted(count, irrelevant);

      // A large Unit is one authoritative target across every published occupied Cell.
      // Multiple legal Actions on that same physical figure must keep chooser semantics.
      await fresh(largeTransport); await page.locator('#stay').click(); await idle(); state = await read();
      const largeDecision = state.presentation.decision;
      const largeTarget = state.result.state.physical.figures.find(f => f.id !== largeDecision.unitId);
      const occupied = state.presentation.figures[largeTarget.id].occupiedCells;
      assert.equal(occupied.length, 4, `${view}: canonical large Unit projection`);
      count = decisions.length;
      for (const cell of occupied) {
        const choices = largeDecision.candidates.filter(candidate => {
          const ref = candidate.interaction;
          if (ref.kind === 'Position') return ref.position.x === cell.x && ref.position.y === cell.y;
          if (ref.kind === 'Unit') return state.presentation.figures[ref.unitId]?.occupiedCells
            .some(position => position.x === cell.x && position.y === cell.y);
          return false;
        });
        assert.ok(choices.length > 1, `${view}: overlapping supplied large-Unit choices at ${JSON.stringify(cell)}`);
        await click(cell, 'choose'); await page.locator('.board-chooser').waitFor({ state: 'visible' });
        assert.equal(await page.locator('.board-chooser button').count(), choices.length + 1);
        assert.equal(decisions.length, count, `${view}: large-Unit chooser submits nothing`);
        if (cell === occupied[0]) await page.screenshot({ path: `.playwright-cli/board-cursors-${view}.png` });
        await page.locator('.board-chooser button').last().click();
        await cursor(cell, 'choose');
        assert.equal(decisions.length, count, `${view}: cancelling restores large-Unit choices`);
      }
      results.push({ view, checks: ['move pointer and physical destination', 'busy default and suppressed click',
        'ambiguity context-menu and chooser', 'explicit Attack crosshair and single submission',
        'no board choices grab', 'invalid target not-allowed', 'Hide Irrelevant and explicit Fireball targeting',
        'camera grab/grabbing and Space-drag restoration',
        'all four large-Unit occupied Cells: overlapping Actions, chooser and cancel restoration'] });
    }
    assert.deepEqual(errors, []);
    return { results, screenshots: ['tabletop', 'three', 'enhanced'].map(view => `.playwright-cli/board-cursors-${view}.png`),
      limits: 'Orthographic ground picking; camera keyboard/Space-drag covered by primary-advance.browser.cjs. Overlapping edges remain covered by focused client suites.' };
  } finally {
    release?.(); page.off('pageerror', onError); page.off('request', onRequest);
  }
};
