// Run against the isolated 5081 QA host. All changes stay in a test-owned draft/game.
module.exports = async function presentationProfilesBrowser(page) {
  const assert = require('node:assert/strict');
  const base = 'http://127.0.0.1:5081', errors = [], results = [];
  const error = value => errors.push(value.message); page.on('pageerror', error);
  const dialog = value => value.accept(); page.on('dialog', dialog);
  const spell = (key, name, presentationProfileId) => ({ key, name, timing: 'Action', mechanic: { kind: 'fireball' },
    ...(presentationProfileId == null ? {} : { presentationProfileId }) });
  const fixture = {
    board: { width: 7, height: 6, defaultTerrain: 'StoneFloor', cells: [], edges: [] },
    unitTypeIds: ['profile-caster', 'profile-target'],
    unitTypes: [
      { id: 'profile-caster', displayName: 'Profile Caster', mov: 2, rng: 4, atk: 1, def: 0, hp: 1, canHaveEquipment: true, canHaveItems: true,
        localAbilities: [spell('first', 'Pointer Spell', 'move'), spell('second', 'Inherited Spell'), spell('third', 'Unknown Spell', 'future.imported'),
          {key:'summon',name:'Summon Target',timing:'Action',mechanic:{kind:'adjacentUnitSummon',unitTypeId:'profile-target',initialPosture:'Upright'}}] },
      { id: 'profile-target', displayName: 'Profile Target', mov: 0, rng: 0, atk: 0, def: 0, hp: 1, actions: 'None' }
    ],
    equipmentTypes: [{ id: 'profile-equipment', name: 'Profile Equipment', localAbilities: [spell('spell', 'Equipment Spell')] },
      { id: 'profile-equipment-second', name: 'Second Profile Equipment', localAbilities: [spell('spell', 'Equipment Spell')] }],
    itemTypes: [{ id: 'profile-item', name: 'Profile Item', localAbilities: [spell('spell', 'Item Spell', 'future.item')] }],
    units: [
      { unitTypeId: 'profile-caster', sideId: 'blue', anchor: { x: 2, y: 2 }, posture: 'Upright', unitId: 'caster',
        inventory: [{ id: 'equipment-one', equipmentTypeId: 'profile-equipment' }, { id: 'equipment-two', equipmentTypeId: 'profile-equipment-second' },
          { id: 'item-one', itemTypeId: 'profile-item' }, { id: 'item-two', itemTypeId: 'profile-item' }] },
      { unitTypeId: 'profile-target', sideId: 'red', anchor: { x: 3, y: 2 }, posture: 'Upright', unitId: 'target' }
    ],
    agency: [{ unitTypeId: 'profile-caster', sideId: 'blue', controller: 'Human' }, { unitTypeId: 'profile-target', sideId: 'red', controller: 'Human' }]
  };
  const read = async () => (await page.request.get(`${base}/api/game`)).json();
  const idle = () => page.waitForFunction(() => document.querySelector('#board').dataset.busy === 'false' && !document.querySelector('#filter').disabled);
  const open = async selector => { if (!await page.locator(selector).evaluate(node => node.open)) await page.locator(`${selector} > summary`).click(); };
  const exportResponse = await page.request.post(`${base}/api/designer/export`, { data: fixture });
  assert.equal(exportResponse.ok(), true, await exportResponse.text());
  let { transport } = await exportResponse.json();
  try {
    await page.goto(`${base}/#designer`);
    await page.locator('[data-designer-tab="document"]').click(); await open('.designer-transport');
    await page.locator('#designer-transport').fill(transport); await page.locator('#designer-import').click();
    await page.waitForFunction(() => JSON.parse(localStorage.getItem('project-delve.designer.v1'))?.definition.unitTypes?.some(t => t.id === 'profile-caster'));
    const profile = page.locator('select[data-field-path="definition.localAbilities[0].presentationProfileId"]');
    for (const [family, id, selected] of [['units', 'profile-caster', 'move'], ['equipment', 'profile-equipment', ''], ['items', 'profile-item', 'future.item']]) {
      await page.locator('#designer-content').click();
      await page.locator(`[data-content-key="${family}:${id}"]`).click(); await page.locator('#content-edit-definition').click();
      await profile.waitFor({ state: 'visible' });
      assert.equal(await profile.inputValue(), selected);
      assert.ok(await profile.locator('option[value="fireball"]').count());
      if (family === 'items') assert.ok((await page.locator('#content-editor').innerText()).includes('Unknown presentation profile'));
      await profile.selectOption('move');
      await page.locator('#content-editor-preview').click();
      await page.waitForFunction(() => document.querySelector('#content-editor .ce-status').textContent.includes('Valid definition'));
      await page.locator('#content-editor-save').click(); await page.locator('#content-editor').waitFor({ state: 'hidden' });
      if (family === 'units') {
        // Both an inherited profile and the unrelated imported unknown ID survive saving.
        const draft = await page.evaluate(() => JSON.parse(localStorage.getItem('project-delve.designer.v1')).definition);
        const abilities = draft.unitTypes.find(t => t.id === 'profile-caster').localAbilities;
        assert.equal(abilities[1].presentationProfileId, undefined); assert.equal(abilities[2].presentationProfileId, 'future.imported');
        await page.locator('#content-edit-definition').click();
        await profile.selectOption('');
        assert.ok((await page.locator('#content-editor').innerText()).includes('Effective profile: fireball'));
        await profile.selectOption('move');
        await page.locator('.ce-ability').first().screenshot({ path: '.playwright-cli/presentation-profile-designer.png' });
        await page.locator('#content-editor-save').click(); await page.locator('#content-editor').waitFor({ state: 'hidden' });
      }
      await page.locator('#mode-designer').click();
      await page.locator('#designer-undo').click(); await page.locator('#designer-redo').click();
      results.push(`${family} selector, validation, save, undo/redo`);
    }
    await page.locator('[data-designer-tab="document"]').click(); await page.locator('#designer-export').click();
    await page.waitForFunction(() => document.querySelector('#designer-transport').value.startsWith('DELVE1:'));
    transport = await page.locator('#designer-transport').inputValue();
    const imported = await page.request.post(`${base}/api/designer/import`, { data: { transport } });
    assert.equal(imported.ok(), true, await imported.text());
    const roundTrip = await imported.json();
    assert.equal(roundTrip.unitTypes.find(t => t.id === 'profile-caster').localAbilities[2].presentationProfileId, 'future.imported');
    await page.locator('#designer-play').click(); await idle();
    await page.evaluate(async () => { const cursors = await import('/presentation-cursors.js'); await cursors.loadCursorAssets(); });
    const settings = async callback => {
      await page.locator('#open-game-menu').click(); await page.locator('#menu-settings').click(); await callback();
      await page.locator('#close-game-tools').click(); await idle();
    };
    await settings(async () => { await open('#tools-gameplay'); await page.locator('#auto').uncheck(); await page.locator('#filter').uncheck(); await open('#tools-display'); await page.locator('#animate').uncheck(); await page.locator('#coordinates').check(); });
    for (const view of ['tabletop', 'three', 'enhanced']) {
      await settings(async () => { await open('#tools-display'); await page.locator('#board-view').selectOption(view); });
      assert.equal(await page.evaluate(value => globalThis.delvePlayDesign(value), transport), true); await idle();
      await page.locator('#start-round').click(); await page.locator('#stay:not([disabled])').waitFor(); await page.locator('#stay').click(); await idle();
      const three = view !== 'enhanced';
      const camera = page.locator('.view-disclosure'); if (!await camera.evaluate(node => node.open)) await camera.locator(':scope > summary').click();
      await page.locator(three ? '#three-camera-mode' : '#camera-mode').selectOption('full');
      if (view === 'tabletop') await page.locator('#three-camera-projection').selectOption('orthographic');
      await camera.locator(':scope > summary').click();
      const state = await read(), choices = state.presentation.decision.candidates;
      const inherited = choices.find(c => c.entryId === 'ability:second' && c.relevant && choices.some(s => s.entryId === 'ability:summon' && s.interaction.position?.x === c.interaction.position.x && s.interaction.position?.y === c.interaction.position.y));
      assert.ok(inherited); assert.equal(inherited.presentationProfileId, 'fireball');
      const point = async cell => {
        if (!three) { const box = await page.locator(`#board .cell[data-cell="${cell.x},${cell.y}"]`).boundingBox(); return { x: box.x + box.width / 2, y: box.y + box.height / 2 }; }
        await page.waitForFunction(() => document.querySelectorAll('.three-coordinates span').length > 3);
        return page.locator('#three-viewport').evaluate((node, cell) => {
          const labels = [...node.querySelectorAll('.three-coordinates span')], at = (x, y) => labels.find(n => n.textContent === `${x},${y}`);
          const [x,y] = labels.map(n => n.textContent.split(',').map(Number)).find(([x,y]) => at(x+1,y) && at(x,y+1));
          const a=at(x,y),b=at(x+1,y),c=at(x,y+1),r=node.getBoundingClientRect(),left=n=>parseFloat(n.style.left),top=n=>parseFloat(n.style.top),dx=cell.x+.5-(x+.05),dy=cell.y+.5-(y+.05);
          return { x:r.left+left(a)+dx*(left(b)-left(a))+dy*(left(c)-left(a)), y:r.top+top(a)+dx*(top(b)-top(a))+dy*(top(c)-top(a)) };
        }, cell);
      };
      const cursor = async (expected, size) => {
        // Action-chip clicks can scroll the page; locate the board after selection.
        const p = await point(inherited.interaction.position);
        await page.mouse.move(p.x,p.y);
        await page.waitForFunction(({p,expected,size}) => {
          const cursor=getComputedStyle(document.elementFromPoint(p.x,p.y)).cursor;
          return ['move','fireball','summon','choose'].includes(expected) ? cursor.includes(`/generated/${size}/${expected}.png`) && cursor.includes(`${size/2} ${size/2},`) : cursor===expected;
        }, {p,expected,size});
      };
      for (const size of [32,40,48]) {
        await settings(async()=>{await open('#tools-display');await page.locator('#cursor-size').selectOption(String(size));});
        await cursor('choose',size);
        for (const [name, expected] of [['Pointer Spell','move'], ['Inherited Spell','fireball'], ['Unknown Spell','crosshair'], ['Equipment Spell','move'], ['Item Spell','choose'], ['Summon Target','summon']]) {
          await page.locator('#play-actions .action-chip').filter({hasText:name}).first().click();
          await cursor(expected,size);
          await page.locator('.action-cancel').click();
        }
        await page.locator('#play-actions .action-chip').filter({hasText:'Equipment Spell'}).nth(1).click();
        await cursor('fireball',size);await page.locator('.action-cancel').click();
        assert.equal(await page.evaluate(()=>localStorage.getItem('delve.cursor.size.v1')),String(size));
      }
      const equipment = state.presentation.cards.caster.entries.filter(e => e.source === 'Equipment');
      assert.equal(equipment.length,2); assert.equal(new Set(equipment.map(e => e.accountingId)).size,2);
      const item = state.presentation.cards.caster.entries.find(e => e.source === 'Item'); assert.equal(item.copyCount,2);
      await page.screenshot({path:`.playwright-cli/presentation-profile-${view}.png`});
      results.push(`${view}: 32/40/48px; inherited, overridden, unknown, Equipment, grouped Items, Summon/Fireball and Choose`);
      for(const [mechanic,asset] of [['fireball','fireball'],['adjacentUnitSummon','summon']]){
        const isolated=structuredClone(fixture);
        isolated.unitTypes[0].actions='None';
        isolated.unitTypes[0].localAbilities=[mechanic==='fireball'?spell('only','Only Fireball'):
          {key:'only',name:'Only Summon',timing:'Action',mechanic:{kind:'adjacentUnitSummon',unitTypeId:'profile-target',initialPosture:'Upright'}}];
        isolated.units[0].inventory=[];
        const exported=await page.request.post(`${base}/api/designer/export`,{data:isolated});assert.equal(exported.ok(),true,await exported.text());
        assert.equal(await page.evaluate(value=>globalThis.delvePlayDesign(value),(await exported.json()).transport),true);await idle();
        await page.locator('#start-round').click();await page.locator('#stay:not([disabled])').waitFor();await page.locator('#stay').click();await idle();
        assert.equal(await page.locator('#play-actions [aria-pressed="true"]').count(),0);
        const boardChoices=(await read()).presentation.decision.candidates.filter(c=>c.interaction.kind!=='Direct');
        assert.ok(boardChoices.length>0);assert.ok(boardChoices.every(c=>c.presentationProfileId===mechanic));
        await cursor(asset,48);
      }
      results.push(`${view}: sole Fireball and sole Summon show their cursor without explicit action selection`);
    }
    await page.reload();await idle();assert.equal(await page.locator('#cursor-size').inputValue(),'48');
    assert.deepEqual(errors, []); return { results };
  } finally { page.off('pageerror', error); page.off('dialog', dialog); }
};
