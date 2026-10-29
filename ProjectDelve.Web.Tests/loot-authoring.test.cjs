const {test} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({structuredClone});
vm.runInContext(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/designer-state.js'),'utf8').replace(/^export /gm,''), context);
const run = code => vm.runInContext(code, context);
const plain = value => JSON.parse(JSON.stringify(value));

test('Loot authoring retains individual identity through history, recovery and playtest snapshot isolation', () => {
  run(`globalThis.draft = new DesignerDraft({maxBoardSize:50, unitTypes:[{unitTypeId:'unit',cellSpan:1,footprintOffsets:[{x:0,y:0}],definition:{id:'unit',mov:2,rng:1,atk:1,def:0,hp:1}}]});
    draft.place('unit','blue','Human',{x:1,y:1});
    globalThis.history = new DesignerHistory(draft); history.begin();
    draft.configureLootType('unit',4,true,true);
    draft.setLootDeck([{kind:'LuckyBoots',count:2},{kind:'PocketBreakfast',count:1}]);
    draft.setLootMarkers({x:2,y:1},2);
    draft.addObject('SecondWind',{unitKey:draft.unitKeys[0]});
    draft.addObject('OfficialKey',{position:{x:2,y:1}});
    history.finish();`);
  const authored = plain(run('draft.snapshot()'));
  assert.equal(authored.units[0].inventory[0].kind, 'SecondWind');
  assert.equal(authored.unitTypes[0].lootDropSuccesses,4);
  assert.notEqual(authored.groundObjects[0].object.id,authored.units[0].inventory[0].id);
  run('history.undo()'); assert.equal(run('draft.definition.loot'), undefined);
  run('history.redo()'); assert.deepEqual(plain(run('draft.snapshot()')),authored);
  const restored = run('readDraftDocument(JSON.stringify(draftDocument(draft.snapshot())))');
  assert.deepEqual(plain(restored.definition),authored);
  run(`globalThis.playtest = draft.snapshot(); playtest.units[0].inventory.length=0; playtest.loot.cards.length=0;`);
  assert.deepEqual(plain(run('draft.snapshot()')),authored);
  assert.throws(()=>run('draft.resize(2,2)'),/Shrinking/);
});

test('multiple markers and cards on one Cell retain unique identities when edited', () => {
  run(`draft.setLootMarkers({x:2,y:1},4); draft.setLootMarkers({x:2,y:1},1); draft.setLootMarkers({x:2,y:1},3);`);
  const markers = plain(run('draft.definition.lootMarkers'));
  assert.equal(markers.length,3); assert.equal(new Set(markers.map(m=>m.id)).size,3);
});
