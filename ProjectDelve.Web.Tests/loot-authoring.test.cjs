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
  assert.equal(authored.lootUnitOverrides.unit.dropSuccesses,4);
  assert.notEqual(authored.groundObjects[0].object.id,authored.units[0].inventory[0].id);
  run('history.undo()'); assert.equal(run('draft.definition.loot.preset'), 'Classic');
  run('history.redo()'); assert.deepEqual(plain(run('draft.snapshot()')),authored);
  const restored = run('readDraftDocument(JSON.stringify(draftDocument(draft.snapshot())))');
  assert.deepEqual(plain(restored.definition),authored);
  run(`globalThis.playtest = draft.snapshot(); playtest.units[0].inventory.length=0; playtest.loot.composition.length=0;`);
  assert.deepEqual(plain(run('draft.snapshot()')),authored);
  assert.throws(()=>run('draft.resize(2,2)'),/Shrinking/);
});

test('multiple markers and cards on one Cell retain unique identities when edited', () => {
  run(`draft.setLootMarkers({x:2,y:1},4); draft.setLootMarkers({x:2,y:1},1); draft.setLootMarkers({x:2,y:1},3);`);
  const markers = plain(run('draft.definition.lootMarkers'));
  assert.equal(markers.length,3); assert.equal(new Set(markers.map(m=>m.id)).size,3);
});

test('live presets copy to independent Custom quantities and undo restores the reference', () => {
  run(`globalThis.presets = [{id:'Classic',composition:[{kind:'LuckyBoots',count:2}]},{id:'Fun',composition:[{kind:'GoatBell',count:3}]}];
    globalThis.live = new DesignerDraft({maxBoardSize:50,unitTypes:[],lootPresets:presets});
    globalThis.undo = new DesignerHistory(live);
    undo.begin(); live.setLootPreset('Fun'); undo.finish();`);
  assert.deepEqual(plain(run('live.snapshot().loot')), {preset:'Fun'});
  run('presets[1].composition[0].count=4'); assert.equal(run('live.lootComposition()[0].count'),4);
  run('undo.begin(); live.copyLootToCustom(); undo.finish(); presets[1].composition[0].count=5');
  assert.deepEqual(plain(run('live.snapshot().loot')), {composition:[{kind:'GoatBell',count:4}]});
  run('undo.undo()'); assert.equal(run('live.lootComposition()[0].count'),5);
  run('undo.redo()'); assert.equal(run('live.lootComposition()[0].count'),4);
  const restored=run('readDraftDocument(JSON.stringify(draftDocument(live.snapshot())))');
  assert.deepEqual(plain(restored.definition.loot),{composition:[{kind:'GoatBell',count:4}]});
});

test('zero drop overrides are explicit and default reset removes only optional settings', () => {
  run(`live.configureLootType('unit',0,null,null)`);
  assert.deepEqual(plain(run('live.snapshot().lootUnitOverrides')), {unit:{dropSuccesses:0}});
  assert.equal(run('live.snapshot().unitTypes'),undefined);
  run(`live.configureLootType('unit',null,null,null)`);
  assert.equal(run('live.snapshot().lootUnitOverrides'),undefined);
});

test('live deck multiplier survives recovery and Copy to Custom copies scaled quantities', () => {
  run(`live.setLootPreset('Fun'); live.setLootMultiplier(3)`);
  assert.equal(run('live.lootComposition()[0].count'), 15);
  const restored = run('readDraftDocument(JSON.stringify(draftDocument(live.snapshot())))');
  assert.deepEqual(plain(restored.definition.loot), {preset:'Fun',presetMultiplier:3});
  run('live.copyLootToCustom()');
  assert.deepEqual(plain(run('live.snapshot().loot')), {composition:[{kind:'GoatBell',count:15}]});
  assert.throws(() => run('live.setLootMultiplier(2)'), /live preset/);
  run(`live.setLootPreset('Classic')`);
  for (const value of [0,-1,1.5,101]) assert.throws(() => run(`live.setLootMultiplier(${value})`), /whole deck multiplier/);
});


test('local Equipment references survive editing, undo, draft recovery and isolated playtest snapshots', () => {
  run(`globalThis.equipmentDraft = new DesignerDraft({maxBoardSize:50,unitTypes:[],equipmentTypes:[{id:'canonical',name:'Canonical Boots'}],lootCards:[]});
    equipmentDraft.replace({board:{width:3,height:3,defaultTerrain:'StoneFloor',cells:[],edges:[]},unitTypeIds:['local-unit'],
      unitTypes:[{id:'local-unit',mov:2,rng:1,atk:1,def:0,hp:2}],
      units:[{unitTypeId:'local-unit',sideId:'blue',anchor:{x:0,y:0}}],agency:[{unitTypeId:'local-unit',sideId:'blue',controller:'Human'}],
      equipmentTypes:[{id:'unfamiliar',name:'Cloud Compass',localAbilities:[
        {key:'wind',name:'Tailwind',timing:'Passive',mechanic:{kind:'selfModifiers',modifiers:[{stat:'Mov',amount:2},{stat:'Def',amount:1}]}},
        {key:'phase',name:'Phase',timing:'Passive',mechanic:{kind:'phase'}},
        {key:'door',name:'Open Door',timing:'FreeAction',mechanic:{kind:'openDoor'}},
        {key:'wake',name:'Awaken',timing:'Action',mechanic:{kind:'standAdjacentFriendly'}}]}],
      loot:{composition:[]}});
    globalThis.equipmentHistory = new DesignerHistory(equipmentDraft);
    equipmentHistory.begin();
    equipmentDraft.setLootDeck([{equipmentTypeId:'unfamiliar',count:3}]);
    equipmentDraft.addObject({equipmentTypeId:'unfamiliar'},{unitKey:equipmentDraft.unitKeys[0]});
    equipmentDraft.addObject({equipmentTypeId:'unfamiliar'},{position:{x:1,y:0}});
    equipmentHistory.finish();`);
  const saved = plain(run('equipmentDraft.snapshot()'));
  assert.equal(saved.units[0].inventory[0].equipmentTypeId,'unfamiliar');
  assert.equal(saved.groundObjects[0].object.equipmentTypeId,'unfamiliar');
  assert.deepEqual(saved.loot.composition,[{equipmentTypeId:'unfamiliar',count:3}]);
  assert.equal(run('equipmentDraft.objectName(equipmentDraft.definition.units[0].inventory[0])'),'Cloud Compass');
  assert.equal(run('equipmentDraft.objectCatalog().length'),2);
  assert.deepEqual(plain(run('equipmentDraft.lootCardCatalog().map(c => c.reference.equipmentTypeId)')),['canonical','unfamiliar']);
  run('equipmentHistory.undo(); equipmentHistory.redo()');
  assert.deepEqual(plain(run('equipmentDraft.snapshot()')),saved);
  assert.deepEqual(plain(run('readDraftDocument(JSON.stringify(draftDocument(equipmentDraft.snapshot()))).definition')),saved);
  run('globalThis.equipmentPlaytest = equipmentDraft.snapshot(); equipmentPlaytest.equipmentTypes[0].localAbilities[0].mechanic.modifiers[0].amount = 99; equipmentPlaytest.units[0].inventory.length = 0');
  assert.deepEqual(plain(run('equipmentDraft.snapshot()')),saved);
});


test('local Abilities, Items and Loot references survive Designer editing and playtest snapshots', () => {
  run(`globalThis.itemDraft = new DesignerDraft({maxBoardSize:50,unitTypes:[],itemTypes:[{id:'second-wind',name:'Second Wind'}],lootCards:[]});
    itemDraft.replace({board:{width:3,height:3,defaultTerrain:'StoneFloor',cells:[],edges:[]},unitTypeIds:['reader'],
      units:[{unitTypeId:'reader',sideId:'blue',anchor:{x:0,y:0}}],agency:[{unitTypeId:'reader',sideId:'blue',controller:'Human'}],
      itemTypes:[{id:'folded-sky',name:'Folded Sky',localAbilities:[{key:'read',name:'A Quiet Ending',timing:'Action',mechanic:{kind:'holyWave'}}]}],
      lootCardDefinitions:[{id:'sky-reward',name:'An Unexpected Letter',itemTypeId:'folded-sky'}],loot:{composition:[]}});
    globalThis.itemHistory = new DesignerHistory(itemDraft); itemHistory.begin();
    itemDraft.addObject({itemTypeId:'folded-sky'},{unitKey:itemDraft.unitKeys[0]});
    itemDraft.addObject({itemTypeId:'folded-sky'},{unitKey:itemDraft.unitKeys[0]});
    itemDraft.addObject({itemTypeId:'folded-sky'},{position:{x:1,y:0}});
    itemDraft.setLootDeck([{lootCardId:'sky-reward',count:2},{itemTypeId:'second-wind',count:1}]);
    itemHistory.finish();`);
  const saved = plain(run('itemDraft.snapshot()'));
  assert.equal(saved.units[0].inventory.length,2);
  assert.notEqual(saved.units[0].inventory[0].id,saved.units[0].inventory[1].id);
  assert.equal(run('itemDraft.objectName(itemDraft.definition.units[0].inventory[0])'),'Folded Sky');
  assert.equal(run(`itemDraft.objectName({lootCardId:'sky-reward'})`),'An Unexpected Letter');
  assert.deepEqual(saved.loot.composition,[{lootCardId:'sky-reward',count:2},{itemTypeId:'second-wind',count:1}]);
  assert.ok(plain(run('itemDraft.lootCardCatalog()')).some(c=>c.reference.lootCardId==='sky-reward'));
  run('itemHistory.undo(); itemHistory.redo()');
  assert.deepEqual(plain(run('itemDraft.snapshot()')),saved);
  assert.deepEqual(plain(run('readDraftDocument(JSON.stringify(draftDocument(itemDraft.snapshot()))).definition')),saved);
  run('globalThis.itemPlaytest=itemDraft.snapshot(); itemPlaytest.itemTypes[0].name="Modified copy"; itemPlaytest.itemTypes[0].localAbilities[0].name="Other name"; itemPlaytest.units[0].inventory.pop()');
  assert.deepEqual(plain(run('itemDraft.snapshot()')),saved);
});
