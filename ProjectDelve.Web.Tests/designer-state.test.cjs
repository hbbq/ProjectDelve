const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/designer-state.js'), 'utf8');
const plain = value => JSON.parse(JSON.stringify(value));

test('storage-free Machine drafts preserve Reveal, limits and Side counters while rejecting malformed storage', () => {
  const { draft, draftDocument, readDraftDocument } = setup();
  const definition = draft.snapshot();
  definition.machines = [{ id: 'beacon', position: { x: 2, y: 2 }, type: {
    id: 'beacon', name: 'Beacon', storage: null,
    operation: { costs: [], outputQuantity: 1, reveal: { distance: 15 }, maxUsesTotal: 3, maxUsesPerSide: 1 },
    requiredTerrainTypeId: 'stone-floor'
  }, storedResources: [], useCounts: { blue: 1 } }];
  const saved = JSON.stringify(draftDocument(definition, 'Current Beacon draft'));
  assert.deepEqual(plain(readDraftDocument(saved).definition), definition);
  for (const storage of [{}, { capacities: null }, { capacities: {} }, { capacities: [null] }, { capacities: [{}] }]) {
    const damaged = JSON.parse(saved); damaged.definition.machines[0].type.storage = storage;
    assert.throws(() => readDraftDocument(JSON.stringify(damaged)), /damaged/);
  }
  for (const type of [null, 'bad-type', []]) {
    const damaged = JSON.parse(saved); damaged.definition.machines[0].type = type;
    assert.throws(() => readDraftDocument(JSON.stringify(damaged)), /damaged/);
  }
});

test('new empty drafts honor host limits smaller than the usual blank board', () => {
  const context=vm.createContext({structuredClone});vm.runInContext(source.replace(/^export /gm,''),context);
  const Draft=vm.runInContext('DesignerDraft',context);
  for(const limit of [1,10,15,120]) {
    const draft=new Draft({maxBoardSize:limit});
    assert.equal(draft.definition.board.width,Math.min(15,limit));
    assert.equal(draft.definition.board.height,Math.min(15,limit));
    assert.throws(()=>draft.resize(limit+1,1),/Dimensions/);
  }
});

test('large rectangular terrain editing survives history and draft files without retaining a stale render index', () => {
  const context=vm.createContext({structuredClone});
  vm.runInContext(source.replace(/^export /gm,''),context);
  const {DesignerDraft,DesignerHistory,draftDocument,readDraftDocument}=vm.runInContext('({DesignerDraft,DesignerHistory,draftDocument,readDraftDocument})',context);
  const catalog={maxBoardSize:120,terrains:['Grass','Gravel','Rail','StoneFloor'],terrainDefinitions:[],unitTypes:[]};
  const draft=new DesignerDraft(catalog);
  draft.replace({board:{width:120,height:100,defaultTerrain:'Grass',cells:Array.from({length:12000},(_,i)=>({position:{x:i%120,y:Math.floor(i/120)},terrain:'Gravel'})),edges:[]},units:[],unitTypeIds:[],agency:[]});
  const history=new DesignerHistory(draft);
  // A full-board render must not fall back to linear searches, including missing overrides.
  const cells=draft.definition.board.cells;
  cells.find=()=>{throw Error('Repeated terrain scan');};
  draft.withTerrainLookup(()=>{
    for(let y=0;y<100;y++)for(let x=0;x<120;x++)assert.equal(draft.terrainAt({x,y}),'Gravel');
    assert.equal(draft.terrainAt({x:999,y:999}),'Grass');
  });
  delete cells.find;
  history.transact(()=>{draft.paintTerrain({x:119,y:99},'Rail','West');draft.paintEdge({x:118,y:99},'Right','ClosedDoor');});
  assert.equal(draft.railDirectionAt({x:119,y:99}),'West');
  history.undo();assert.equal(draft.terrainAt({x:119,y:99}),'Gravel');
  history.redo();
  const doc=readDraftDocument(JSON.stringify(draftDocument(draft.snapshot(),'Large fixture')));
  const reopened=new DesignerDraft(catalog);reopened.replace(doc.definition);
  assert.deepEqual(plain(reopened.snapshot()),plain(draft.snapshot()));
  assert.equal(reopened.railDirectionAt({x:119,y:99}),'West');
  assert.throws(()=>readDraftDocument(JSON.stringify(doc),50),/unsupported structure/i);
  assert.throws(()=>draft.withTerrainLookup(()=>{throw Error('cancelled render');}),/cancelled/);
  draft.paintTerrain({x:119,y:99},'Grass');
  assert.equal(draft.terrainAt({x:119,y:99}),'Grass');
  draft.withTerrainLookup(()=>assert.equal(draft.terrainAt({x:119,y:99}),'Grass'));
});

test('temporary terrain lookup preserves first-match overrides, custom types and implicit defaults', () => {
  const context=vm.createContext({structuredClone});vm.runInContext(source.replace(/^export /gm,''),context);
  const Draft=vm.runInContext('DesignerDraft',context), draft=new Draft({maxBoardSize:120,terrains:['Grass','Rail'],terrainDefinitions:[{id:'soil',name:'Soil',presentation:{recipe:'Dirt'}}],terrainPresentationRecipes:['Dirt']});
  draft.replace({board:{width:120,height:100,defaultTerrain:'Grass',defaultTerrainTypeId:'soil',cells:[{position:{x:1,y:2},terrain:'Rail',direction:'North'},{position:{x:1,y:2},terrain:'Grass'}],edges:[]},units:[]});
  const at=()=>[draft.terrainSelectionAt({x:1,y:2}),draft.railDirectionAt({x:1,y:2}),draft.terrainNameAt({x:119,y:99}),draft.terrainRecipeAt({x:119,y:99})];
  assert.deepEqual(draft.withTerrainLookup(at),at());
  assert.equal(draft.terrainNameAt({x:119,y:99}),'Soil');
});

test('Loot Card batch is one history step, persists, and rejects collisions without partial changes', () => {
  const context = vm.createContext({ structuredClone });
  vm.runInContext(source.replace(/^export /gm, ''), context);
  const { DesignerDraft, DesignerHistory } = vm.runInContext('({DesignerDraft, DesignerHistory})', context);
  const draft = new DesignerDraft({maxBoardSize:50});
  const history = new DesignerHistory(draft);
  const cards = [{id:'first',name:'First',reward:{kind:'equipment',equipmentTypeId:'first'}},{id:'second',name:'Second',reward:{kind:'item',itemTypeId:'second'}}];
  history.transact(()=>draft.applyLootCardBatch(cards));
  assert.deepEqual(plain(draft.snapshot().lootCardDefinitions),cards);
  history.undo();
  assert.equal(draft.snapshot().lootCardDefinitions,undefined);
  history.redo();
  assert.deepEqual(plain(draft.snapshot().lootCardDefinitions),cards);
  const before = JSON.stringify(draft.snapshot());
  assert.throws(()=>draft.applyLootCardBatch([{id:'third'},{id:'first'}]),/collision/);
  assert.equal(JSON.stringify(draft.snapshot()),before);
  const reopened = new DesignerDraft({maxBoardSize:50}); reopened.replace(JSON.parse(before));
  assert.deepEqual(plain(reopened.snapshot().lootCardDefinitions),cards);
});

function setup() {
  const context = vm.createContext({ structuredClone });
  vm.runInContext(source.replace(/^export /gm, ''), context);
  const api = vm.runInContext('({ DesignerDraft, DesignerHistory, draftDocument, readDraftDocument })', context);
  const canonical = { id: 'canonical-bell', name: 'Canonical Bell', localAbilities: [
    { key: 'ring', name: 'Ring', timing: 'Action', mechanic: { kind: 'adjacentUnitPostureChange', selector:'Friendlies', newPosture:'Upright' } }
  ] };
  const draft = new api.DesignerDraft({ maxBoardSize: 50, unitTypes: [], equipmentTypes: [canonical], lootCards: [] });
  draft.replace({
    board: { width: 4, height: 4, defaultTerrain: 'StoneFloor', cells: [], edges: [] },
    unitTypeIds: ['collector'], unitTypes: [{ id: 'collector', mov: 2, rng: 1, atk: 1, def: 0, hp: 2, canHaveEquipment: true }],
    units: [{ unitTypeId: 'collector', sideId: 'blue', anchor: { x: 0, y: 0 }, unitId: 'collector-1' }],
    agency: [{ unitTypeId: 'collector', sideId: 'blue', controller: 'Human' }],
    loot: { composition: [] }, objectives: [{ sideId: 'blue', kind: 'NoObjective' }],
    fogOfWarSides: ['blue'], worldCardDefinitions: [{ id: 'rain', name: 'Rain', lifecycle: 'Immediate', immediate: { kind: 'healDamagedUnits', amount: 1 } }],
    equipmentTypes: [{ id: 'existing', name: 'Existing', localAbilities: [] }]
  });
  return { ...api, draft, canonical, history: new api.DesignerHistory(draft) };
}

function equipment(id = 'paperwind', name = 'Paperwind Cape') {
  return { id, name, localAbilities: [
    { key: 'stride', name: 'Tailwind', timing: 'Passive', usage: { maxUses: null },
      mechanic: { kind: 'selfModifiers', modifiers: [{ stat: 'Mov', amount: 1 }, { stat: 'Def', amount: -1 }] } },
    { key: 'wake', name: 'Morning Call', timing: 'Action', mechanic: { kind: 'adjacentUnitPostureChange', selector:'Friendlies', newPosture:'Upright' } }
  ] };
}

test('content saves add Equipment and linked Loot while preserving unrelated draft data', () => {
  const { draft, history } = setup();
  const before = plain(draft.snapshot()), keys = [...draft.unitKeys], revision = draft.revision;
  const cape = equipment(), reward = { id: 'cape-reward', name: 'An Unexpected Cape', equipmentTypeId: cape.id };
  history.transact(() => draft.applyContentDefinition('equipment', cape));
  history.transact(() => draft.applyContentDefinition('lootCards', reward));
  const saved = plain(draft.snapshot());
  assert.deepEqual(saved, { ...before, equipmentTypes: [...before.equipmentTypes, cape], lootCardDefinitions: [reward] });
  assert.deepEqual([...draft.unitKeys], keys);
  assert.equal(draft.revision, revision + 2);
  assert.ok(draft.objectCatalog().some(row => row.reference.equipmentTypeId === cape.id && row.name === cape.name));
  assert.ok(draft.lootCardCatalog().some(row => row.reference.lootCardId === reward.id && row.name === reward.name));
  assert.deepEqual(plain(draft.lootComposition()), []);
  assert.equal(draft.definition.units[0].inventory, undefined);
});

test('copies and payloads stay independent from canonical definitions and other local copies', () => {
  const { draft, canonical } = setup();
  const original = plain(canonical);
  const copy = { ...structuredClone(canonical), id: 'copied-bell', name: 'Copied Bell' };
  draft.applyContentDefinition('equipment', copy);
  draft.applyContentDefinition('equipment', { ...copy, id: 'another-bell', name: 'Another Bell' });
  copy.name = 'Changed outside';
  copy.localAbilities[0].mechanic.kind = 'adjacentUnitsPostureChange';
  const edited = plain(draft.definition.equipmentTypes[1]);
  edited.name = 'Renamed Bell'; edited.localAbilities[0].name = 'Awaken';
  draft.applyContentDefinition('equipment', edited, edited.id);
  edited.localAbilities[0].name = 'Changed after save';
  assert.equal(draft.definition.equipmentTypes[1].name, 'Renamed Bell');
  assert.equal(draft.definition.equipmentTypes[1].localAbilities[0].name, 'Awaken');
  assert.deepEqual(plain(draft.definition.equipmentTypes[2].localAbilities), original.localAbilities);
  assert.deepEqual(plain(canonical), original);
  assert.equal(draft.definition.equipmentTypes[1].localAbilities[0].mechanic.kind, 'adjacentUnitPostureChange');
  assert.equal(draft.definition.equipmentTypes[2].localAbilities[0].mechanic.kind, 'adjacentUnitPostureChange');
});

test('content identity errors refuse mutation and do not advance the revision', () => {
  const { draft } = setup();
  draft.applyContentDefinition('equipment', equipment());
  const before = plain(draft.snapshot()), revision = draft.revision;
  for (const action of [
    () => draft.applyContentDefinition('unsupported-family', equipment()),
    () => draft.applyContentDefinition('equipment', null),
    () => draft.applyContentDefinition('equipment', []),
    () => draft.applyContentDefinition('equipment', { name: 'Missing ID' }),
    () => draft.applyContentDefinition('equipment', { id: '  ' }),
    () => draft.applyContentDefinition('equipment', equipment()),
    () => draft.applyContentDefinition('equipment', equipment('renamed'), 'paperwind'),
    () => draft.applyContentDefinition('equipment', equipment('missing'), 'missing'),
    () => draft.applyContentDefinition('lootCards', { id: 'paperwind' }, 'paperwind')
  ]) {
    assert.throws(action);
    assert.deepEqual(plain(draft.snapshot()), before);
    assert.equal(draft.revision, revision);
  }
  draft.definition.equipmentTypes.push(equipment());
  const duplicate = plain(draft.snapshot());
  assert.throws(() => draft.applyContentDefinition('equipment', equipment(), 'paperwind'), /already uses/);
  assert.deepEqual(plain(draft.snapshot()), duplicate);
  assert.equal(draft.revision, revision);
});

test('Equipment and Loot edits undo and redo atomically and invalidate earlier revisions', () => {
  const { draft, history } = setup();
  const before = plain(draft.snapshot());
  history.transact(() => draft.applyContentDefinition('equipment', equipment()));
  const equipmentOnly = plain(draft.snapshot()), firstRevision = draft.revision;
  history.transact(() => draft.applyContentDefinition('lootCards', { id: 'cape', name: 'Cape', equipmentTypeId: 'paperwind' }));
  const both = plain(draft.snapshot());
  assert.equal(history.undo(), true); assert.deepEqual(plain(draft.snapshot()), equipmentOnly);
  assert.ok(draft.revision > firstRevision);
  assert.equal(history.undo(), true); assert.deepEqual(plain(draft.snapshot()), before);
  assert.equal(history.redo(), true); assert.deepEqual(plain(draft.snapshot()), equipmentOnly);
  assert.equal(history.redo(), true); assert.deepEqual(plain(draft.snapshot()), both);
  history.transact(() => draft.applyContentDefinition('lootCards', { id: 'cape', name: 'Renamed reward', equipmentTypeId: 'paperwind' }, 'cape'));
  assert.equal(history.undo(), true); assert.deepEqual(plain(draft.snapshot()), both);
  assert.equal(history.redo(), true); assert.equal(draft.definition.lootCardDefinitions[0].name, 'Renamed reward');
});

test('created content survives recovery and import transport with isolated Playtest references', () => {
  const { draft, history, draftDocument, readDraftDocument, DesignerDraft } = setup();
  history.transact(() => draft.applyContentDefinition('equipment', equipment()));
  history.transact(() => draft.applyContentDefinition('lootCards', { id: 'cape', name: 'Cape reward', equipmentTypeId: 'paperwind' }));
  history.transact(() => {
    draft.setLootDeck([{ lootCardId: 'cape', count: 2 }]);
    draft.addObject({ equipmentTypeId: 'paperwind' }, { unitKey: draft.unitKeys[0] });
    draft.addObject({ equipmentTypeId: 'paperwind' }, { position: { x: 1, y: 0 } });
  });
  const expected = plain(draft.snapshot());
  const document = draftDocument(draft.snapshot(), 'Content draft', { unrelated: { values: { title: 'Retained' } } });
  const recovered = readDraftDocument(JSON.stringify(document));
  assert.deepEqual(plain(recovered.definition), expected);
  assert.deepEqual(plain(recovered.buffers), { unrelated: { values: { title: 'Retained' } } });
  const imported = new DesignerDraft(draft.catalog);
  imported.replace(JSON.parse(JSON.stringify(recovered.definition)));
  assert.deepEqual(plain(imported.snapshot()), expected);
  const playtest = imported.snapshot();
  playtest.equipmentTypes[1].localAbilities[0].mechanic.modifiers[0].amount = 99;
  playtest.lootCardDefinitions[0].equipmentTypeId = 'other';
  playtest.units[0].inventory.length = 0;
  assert.deepEqual(plain(imported.snapshot()), expected);
  assert.deepEqual(plain(draft.snapshot()), expected);
});

test('application performs structural persistence without duplicating Engine mechanic validation', () => {
  const { draft } = setup();
  const pendingValidation = { id: 'draft-equipment', name: 'Draft', localAbilities: [
    { key: 'draft', name: 'Incomplete mechanic', timing: 'Action', mechanic: { kind: 'heal', selector:'Friendlies', amount: -2 } }
  ] };
  draft.applyContentDefinition('equipment', pendingValidation);
  assert.deepEqual(plain(draft.definition.equipmentTypes.at(-1)), pendingValidation);
});

test('pinned references apply and update as single undoable operations and survive draft recovery', () => {
  const { draft, history, draftDocument, readDraftDocument } = setup();
  const reference = { formatVersion: 1, packageId: 'boots', libraryId: 'library', revision: 1, snapshotDigest: 'one',
    selected: [{ family: 'LootCard', typeId: 'boots-loot' }], snapshot: { equipmentTypes: [equipment('boots')], lootCardDefinitions: [{ id: 'boots-loot', equipmentTypeId: 'boots' }] } };
  const before = plain(draft.snapshot());
  history.transact(() => draft.applyContentReferences([reference]));
  reference.snapshot.equipmentTypes[0].name = 'Mutated source';
  assert.notEqual(draft.definition.contentReferences[0].snapshot.equipmentTypes[0].name, 'Mutated source');
  const pinned = plain(draft.snapshot());
  assert.equal(history.undo(), true); assert.deepEqual(plain(draft.snapshot()), before);
  assert.equal(history.redo(), true); assert.deepEqual(plain(draft.snapshot()), pinned);
  const updated = structuredClone(pinned.contentReferences[0]); updated.revision = 2; updated.snapshotDigest = 'two';
  updated.snapshot.equipmentTypes[0].name = 'Explicitly updated';
  history.transact(() => draft.applyContentReferences([updated]));
  assert.equal(history.undo(), true); assert.deepEqual(plain(draft.snapshot()), pinned);
  assert.equal(history.redo(), true); assert.equal(draft.definition.contentReferences[0].revision, 2);
  const recovered = readDraftDocument(JSON.stringify(draftDocument(draft.snapshot(), 'Pinned scenario')));
  assert.deepEqual(plain(recovered.definition.contentReferences), plain(draft.definition.contentReferences));
  assert.equal(draft.definition.equipmentTypes.some(row => row.id === 'boots'), false, 'pins are never copied into freely editable local arrays');
});


test('authored and pinned Unit definitions are selectable before declaration and do not create placement or agency', () => {
  const {draft,history}=setup(), before=plain(draft.snapshot());
  const unit={id:'keeper',displayName:'Keeper',mov:3,rng:1,atk:2,def:1,hp:3,localAbilities:[]};
  history.transact(()=>draft.applyContentDefinition('units',unit));
  assert.equal(draft.unitTypeCatalog().find(type=>type.unitTypeId==='keeper').displayName,'Keeper');
  assert.deepEqual(plain(draft.definition.units),before.units);
  assert.deepEqual(plain(draft.definition.agency),before.agency);
  assert.deepEqual(plain(draft.definition.unitTypeIds),before.unitTypeIds);
  draft.effectiveMetadata=[{unitTypeId:'pinned',displayName:'Pinned Keeper'},{unitTypeId:'keeper',displayName:'Keeper'}];
  assert.equal(draft.unitTypeCatalog().some(type=>type.unitTypeId==='pinned'),true);
  unit.displayName='Outside'; assert.equal(draft.definition.unitTypes[1].displayName,'Keeper');
  history.undo(); assert.deepEqual(plain(draft.snapshot()),before);
  history.redo(); assert.equal(draft.definition.unitTypes[1].id,'keeper');
});


test('Single Use Item edits and linked Loot preserve inventory, undo history, recovery and transport isolation', () => {
  const { draft, history, draftDocument, readDraftDocument, DesignerDraft } = setup();
  const before = plain(draft.snapshot());
  const item = { id: 'tea', name: 'Tea', localAbilities: [{ key: 'sip', name: 'Sip', timing: 'BonusAction', usage: { maxUses: null }, mechanic: { kind: 'extraActions', amount: 1 } }] };
  history.transact(() => draft.applyContentDefinition('items', item));
  const itemOnly = plain(draft.snapshot());
  history.transact(() => draft.applyContentDefinition('lootCards', { id: 'tea-loot', name: 'Tea Loot', itemTypeId: 'tea' }));
  const both = plain(draft.snapshot());
  history.undo(); assert.deepEqual(plain(draft.snapshot()), itemOnly);
  history.undo(); assert.deepEqual(plain(draft.snapshot()), before);
  history.redo(); history.redo(); assert.deepEqual(plain(draft.snapshot()), both);
  history.transact(() => {
    draft.addObject({ itemTypeId: 'tea' }, { unitKey: draft.unitKeys[0] });
    draft.addObject({ itemTypeId: 'tea' }, { position: { x: 1, y: 0 } });
    draft.setLootDeck([{ lootCardId: 'tea-loot', count: 2 }]);
  });
  const expected = plain(draft.snapshot());
  const recovered = readDraftDocument(JSON.stringify(draftDocument(draft.snapshot(), 'Item fixture')));
  assert.deepEqual(plain(recovered.definition), expected);
  const imported = new DesignerDraft(draft.catalog); imported.replace(recovered.definition);
  const snapshot = imported.snapshot(); snapshot.itemTypes[0].localAbilities[0].mechanic.amount = 9;
  assert.deepEqual(plain(imported.snapshot()), expected);
  assert.deepEqual(plain(draft.snapshot()), expected);
  assert.equal(item.localAbilities[0].mechanic.amount, 1);
});

test('ordered Ability Costs survive Scenario edits, undo/redo, recovery and Save/Open for all Ability owners', () => {
  for (const [family, field] of [['units', 'unitTypes'], ['equipment', 'equipmentTypes'], ['items', 'itemTypes']]) {
    const { draft, history, draftDocument, readDraftDocument, DesignerDraft } = setup();
    const ability = { key: 'stable-door', name: 'Try Open Door', timing: 'Action', usage: { maxUses: null },
      mechanic: { kind: 'tryOpenDoor', successCount: 2 },
      costs: [{ kind: 'spendResource', resourceTypeId: 'diamond', amount: 1 }, { kind: 'layDownSelf' }] };
    const owner = { id: 'authored-cost-owner', name: 'Authored cost owner', localAbilities: [ability],
      ...(family === 'units' ? { displayName: 'Authored cost owner', mov: 1, rng: 0, atk: 0, def: 0, hp: 1 } : {}) };
    const before = plain(draft.snapshot());
    history.transact(() => draft.applyContentDefinition(family, owner));
    const added = plain(draft.snapshot());
    const edited = structuredClone(owner); edited.localAbilities[0].costs.reverse();
    edited.localAbilities[0].name = 'Renamed attempt';
    history.transact(() => draft.applyContentDefinition(family, edited, owner.id));
    const after = plain(draft.snapshot());
    history.undo(); assert.deepEqual(plain(draft.snapshot()), added);
    history.undo(); assert.deepEqual(plain(draft.snapshot()), before);
    history.redo(); history.redo(); assert.deepEqual(plain(draft.snapshot()), after);
    const recovered = readDraftDocument(JSON.stringify(draftDocument(draft.snapshot(), 'Cost fixture')));
    const opened = new DesignerDraft(draft.catalog); opened.replace(recovered.definition);
    const restored = opened.snapshot()[field].find(row => row.id === owner.id).localAbilities[0];
    assert.equal(restored.key, 'stable-door');
    assert.deepEqual(plain(restored.costs), edited.localAbilities[0].costs);
    restored.costs[1].amount = 99;
    assert.deepEqual(plain(draft.snapshot()), after);
    assert.equal(ability.costs[0].amount, 1);
  }
});

test('named groups retain independent controllers, Normal overrides and route progress through roundtrip and edits', () => {
  const context = vm.createContext({ structuredClone }); vm.runInContext(source.replace(/^export /gm, ''), context);
  const Draft = vm.runInContext('DesignerDraft', context), d = new Draft({ maxBoardSize: 20, unitTypes: [] });
  d.setAgency('scout', 'A', 'Human'); d.setAgency('scout', 'A', 'Automated', 'east'); d.setAgency('scout', 'A', 'Gambit', 'west');
  d.saveWaypoint(null, { id: 'a', area: { x: 0, y: 0, w: 1, h: 1 } }); d.saveWaypoint(null, { id: 'b', area: { x: 2, y: 0, w: 1, h: 1 } });
  d.saveRoute(null, { id: 'road', mode: 'PingPong', waypointIds: ['a', 'b'] });
  const route = { kind: 'PatrolRoute', route: { routeId: 'road', currentIndex: 1, direction: -1 } };
  d.setOrder('scout', 'A', route, 'east'); d.setOrder('scout', 'A', { kind: 'Normal' }, 'west');
  route.route.currentIndex = 0;
  assert.equal(d.definition.orders[0].route.currentIndex, 1);
  const key = d.place('scout', 'A', 'Human', { x: 1, y: 1 });
  d.editUnit(key, { groupId: 'east', dormant: true, wakeDistance: 3 }, 'Automated');
  const reopened = new Draft({ maxBoardSize: 20, unitTypes: [] }); reopened.replace(d.snapshot());
  assert.deepEqual(plain(reopened.snapshot()), plain(d.snapshot()));
  assert.equal(reopened.definition.units[0].dormant, true); assert.equal(reopened.definition.units[0].wakeDistance, 3);
  assert.equal(reopened.definition.agency.length, 3);
  reopened.editAgency(1, { unitTypeId: 'scout', sideId: 'A', groupId: 'north', controller: 'Human' }, reopened.definition.orders[0]);
  assert.deepEqual(plain(reopened.definition.orders.map(o => [o.groupId, o.kind])), [['west', 'Normal'], ['north', 'PatrolRoute']]);
  reopened.removeAgency(2);
  assert.equal(reopened.definition.orders.length, 1); assert.equal(reopened.definition.orders[0].route.currentIndex, 1);
  assert.throws(() => reopened.setOrder('scout', 'A', { kind: 'PatrolRoute', route: { routeId: 'missing' } }, 'north'), /existing Route/);
  reopened.saveRoute('road', { id: 'renamed', mode: 'PingPong', waypointIds: ['a', 'b'] });
  assert.equal(reopened.definition.orders[0].route.routeId, 'renamed');
  assert.equal(reopened.definition.orders[0].route.currentIndex, 1);
});

test('same-Type groups retain separate Patrol and Follow Route progress when a shared Route changes', () => {
  const context = vm.createContext({ structuredClone }); vm.runInContext(source.replace(/^export /gm, ''), context);
  const Draft = vm.runInContext('DesignerDraft', context), d = new Draft({ maxBoardSize: 20, unitTypes: [] });
  for (const [id, x] of [['a',0],['b',1],['c',2]]) d.saveWaypoint(null, { id, area: { x, y:0, w:1, h:1 } });
  d.saveRoute(null, { id:'track', mode:'PingPong', waypointIds:['a','b'] });
  d.setOrder('scout','A',{kind:'FollowRoute',route:{routeId:'track',currentIndex:0,direction:1}},'east');
  d.setOrder('scout','A',{kind:'PatrolRoute',route:{routeId:'track',currentIndex:1,direction:-1}},'west');
  assert.throws(()=>d.saveRoute('track',{id:'track',mode:'PingPong',waypointIds:['a','c']}), /Reset affected group progress/);
  assert.equal(d.definition.orders[1].route.currentIndex,1);
  d.saveRoute('track',{id:'track',mode:'PingPong',waypointIds:['a','c']},true);
  assert.deepEqual(plain(d.definition.orders.map(o=>o.route)), [{routeId:'track',currentIndex:0,direction:1},{routeId:'track',currentIndex:0,direction:1}]);
  d.definition.orders[1].route.currentIndex=1;
  assert.equal(d.definition.orders[0].route.currentIndex,0);
});
