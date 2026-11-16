// Pass a Playwright page connected to the isolated 5081 QA host. Replaces only its game.
// Uses trusted keyboard input so capture/bubble order and native button activation are exercised.
module.exports = async function primaryAdvanceBrowser(page) {
  const assert = require('node:assert/strict');
  const errors = [], decisions = [];
  page.on('pageerror', error => errors.push(error.message));
  page.on('request', request => {
    if (request.url().endsWith('/api/game/commands') && request.postDataJSON()?.operation === 'decision') decisions.push(request.postDataJSON());
  });
  await page.goto('http://127.0.0.1:5081/#play');
  await page.waitForFunction(() => !document.querySelector('#filter').disabled);
  const fixture = { board: { width: 20, height: 12, defaultTerrain: 'StoneFloor', cells: [], edges: [] },
    unitTypeIds: ['barbarian-type'], units: [
      { unitTypeId: 'barbarian-type', sideId: 'blue', anchor: { x: 1, y: 1 }, posture: 'Upright', unitId: 'hero' },
      { unitTypeId: 'barbarian-type', sideId: 'red', anchor: { x: 2, y: 1 }, posture: 'Upright', unitId: 'foe' }],
    agency: ['blue', 'red'].map(sideId => ({ unitTypeId: 'barbarian-type', sideId, controller: 'Human' })) };
  const exported = await page.request.post('http://127.0.0.1:5081/api/designer/export', { data: fixture });
  assert.equal(exported.ok(), true, await exported.text());
  const { transport } = await exported.json();
  const settings = async () => {
    // Space may leave a native menu button focused/open after the preceding
    // dialog-focus check. Reuse that menu rather than clicking behind it.
    if (!await page.locator('#game-menu').evaluate(node => node.open)) await page.locator('#open-game-menu').click();
    await page.locator('#menu-settings').click();
  };
  const open = async selector => { if (!await page.locator(selector).evaluate(node => node.open)) await page.locator(`${selector} > summary`).click(); };
  const ready = () => page.waitForFunction(() => !document.querySelector('#stay').hidden && !document.querySelector('#stay').disabled);
  const fresh = async () => {
    assert.equal(await page.evaluate(value => globalThis.delvePlayDesign(value), transport), true);
    await page.locator('#start-round').click(); await ready();
  };
  await page.evaluate(value => globalThis.delvePlayDesign(value), transport);
  await settings(); await open('#tools-gameplay'); await page.locator('#auto').uncheck();
  await page.locator('#close-game-tools').click();
  await page.waitForFunction(async () => !(await (await fetch('/api/game')).json()).autoChooseSingleRelevantChoice);
  for (const view of ['tabletop', 'three', 'enhanced']) {
    await settings(); await open('#tools-display'); await page.locator('#board-view').selectOption(view); await page.locator('#close-game-tools').click();
    await fresh();
    const three = view !== 'enhanced', host = page.locator(three ? '#three-viewport' : '#camera-viewport');
    await host.waitFor({ state: 'visible' }); await host.focus();
    assert.equal(await host.evaluate(node => node === document.activeElement), true);
    let count = decisions.length;
    // Camera keys retain their handlers and never submit game decisions.
    if (three) {
      await page.keyboard.press('ArrowRight');
      assert.equal(await page.locator('.three-focus').evaluate(node => node === document.activeElement), true);
      const beforeZoom = await page.locator('#three-camera-status').textContent();
      await page.keyboard.press('+');
      await page.waitForFunction(old => document.querySelector('#three-camera-status').textContent !== old, beforeZoom);
      await page.keyboard.press('Shift+ArrowRight');
      await page.waitForFunction(() => document.querySelector('#three-camera-status').textContent.includes('Manual'));
    }
    await page.keyboard.press('+'); await page.keyboard.press('-');
    assert.equal(decisions.length, count);
    if (!three) {
      // At a whole-board span the DOM camera clamps panning. Give this gesture
      // room to move before asserting a transform change.
      await open('.view-disclosure'); await page.locator('#camera-mode').selectOption('fixed');
      await page.locator('#camera-span').selectOption('10'); await page.locator('.view-disclosure > summary').click();
    }
    await host.focus();
    // Space-drag pans and must not advance. The same Space modifier remains armed by the renderer.
    const rect = await host.boundingBox();
    await page.keyboard.down('Space'); await page.mouse.move(rect.x + rect.width / 2, rect.y + rect.height / 2);
    const cameraStyle = () => page.locator(three ? '.three-focus' : '#camera-world').getAttribute('style');
    const beforeDrag = await cameraStyle();
    // Below the normal 6px mouse threshold: movement must use the armed Space modifier.
    await page.mouse.down(); await page.mouse.move(rect.x + rect.width / 2 - 3, rect.y + rect.height / 2, { steps: 3 }); await page.mouse.up(); await page.keyboard.up('Space');
    await page.waitForFunction(({ three, old }) => document.querySelector(three ? '.three-focus' : '#camera-world').getAttribute('style') !== old, { three, old: beforeDrag });
    assert.equal(decisions.length, count);
    await host.focus(); await page.keyboard.down('s'); await page.keyboard.down('s'); await page.keyboard.up('s');
    await page.waitForFunction(() => !document.querySelector('#end-turn').hidden && !document.querySelector('#end-turn').disabled);
    assert.equal(decisions.length, count + 1); assert.equal(decisions.at(-1).candidateKey, 'stay');
    // Arrow navigation focuses a native button in 3D; Space must still prefer End Turn.
    await host.focus(); if (three) await page.keyboard.press('ArrowRight');
    await page.keyboard.down('Space'); await page.keyboard.down('Space'); await page.keyboard.up('Space');
    await ready(); assert.equal(decisions.length, count + 2); assert.equal(decisions.at(-1).candidateKey, 'end-turn');
    // Space also falls back to Stay on a fresh focused-board Move decision.
    await host.focus(); await page.keyboard.press('Space');
    await page.waitForFunction(() => !document.querySelector('#end-turn').hidden && !document.querySelector('#end-turn').disabled);
    assert.equal(decisions.length, count + 3); assert.equal(decisions.at(-1).candidateKey, 'stay');
    // Selected-action targeting owns board input. S/Space must not end this interaction.
    count = decisions.length;
    await page.locator('#play-actions .action-chip').filter({ hasText: /^Attack/ }).first().click();
    await page.locator('.action-target-options').waitFor({ state: 'visible' });
    await host.focus(); await page.keyboard.press('s'); await page.keyboard.press('Space');
    assert.equal(decisions.length, count);
    const current = await (await page.request.get('http://127.0.0.1:5081/api/game')).json();
    const actor = current.result.state.physical.figures.find(figure => figure.id === current.presentation.decision.unitId);
    const target = current.presentation.decision.candidates.find(choice => choice.boardVisual?.kind === 'attack');
    assert.ok(target);
    if (three) {
      await page.keyboard.press(actor.position.x === 1 ? 'ArrowRight' : 'ArrowLeft');
      await page.keyboard.press('Enter');
      await page.waitForFunction(() => !document.querySelector('#decision-panel').hidden && !!document.querySelector('#decision-panel .primary-roll'));
      assert.equal(decisions.length, count + 1); assert.equal(decisions.at(-1).candidateKey, target.key);
    } else await page.locator('.action-cancel').click();
    // Dialog and editor focus must keep both shortcuts inactive.
    count = decisions.length; await page.locator('#open-game-menu').click(); await page.keyboard.press('s'); await page.keyboard.press('Space');
    if (await page.locator('#game-menu').evaluate(node => node.open)) await page.locator('#close-game-menu').click();
    await page.evaluate(() => { const input = document.createElement('textarea'); input.id = 'qa-shortcut-editor'; document.querySelector('#playtest-main').append(input); input.focus(); });
    await page.keyboard.press('s'); await page.keyboard.press('Space'); assert.equal(decisions.length, count);
    await page.locator('#qa-shortcut-editor').evaluate(node => node.remove());
  }
  assert.deepEqual(errors, []);
  return { views: ['tabletop', 'three', 'enhanced'], checks: 'focused S, Space Stay/End Turn, repeat, single submission, camera navigation/zoom/pan, Space-drag, selected-action targeting, Enter, dialogs and editor focus' };
};
