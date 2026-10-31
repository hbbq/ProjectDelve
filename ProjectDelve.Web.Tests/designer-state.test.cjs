const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/designer-state.js'), 'utf8');
const plain = value => JSON.parse(JSON.stringify(value));

function setup() {
  const context = vm.createContext({ structuredClone });
  vm.runInContext(source.replace(/^export /gm, ''), context);
  const api = vm.runInContext('({ DesignerDraft, DesignerHistory, draftDocument, readDraftDocument })', context);
  const canonical = { id: 'canonical-bell', name: 'Canonical Bell', localAbilities: [
    { key: 'ring', name: 'Ring', timing: 'Action', mechanic: { kind: 'standAdjacentFriendly' } }
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
    { key: 'wake', name: 'Morning Call', timing: 'Action', mechanic: { kind: 'standAdjacentFriendly' } }
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
  copy.localAbilities[0].mechanic.kind = 'holyWave';
  const edited = plain(draft.definition.equipmentTypes[1]);
  edited.name = 'Renamed Bell'; edited.localAbilities[0].name = 'Awaken';
  draft.applyContentDefinition('equipment', edited, edited.id);
  edited.localAbilities[0].name = 'Changed after save';
  assert.equal(draft.definition.equipmentTypes[1].name, 'Renamed Bell');
  assert.equal(draft.definition.equipmentTypes[1].localAbilities[0].name, 'Awaken');
  assert.deepEqual(plain(draft.definition.equipmentTypes[2].localAbilities), original.localAbilities);
  assert.deepEqual(plain(canonical), original);
  assert.equal(draft.definition.equipmentTypes[1].localAbilities[0].mechanic.kind, 'standAdjacentFriendly');
  assert.equal(draft.definition.equipmentTypes[2].localAbilities[0].mechanic.kind, 'standAdjacentFriendly');
});

test('content identity errors refuse mutation and do not advance the revision', () => {
  const { draft } = setup();
  draft.applyContentDefinition('equipment', equipment());
  const before = plain(draft.snapshot()), revision = draft.revision;
  for (const action of [
    () => draft.applyContentDefinition('units', equipment()),
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
    { key: 'draft', name: 'Incomplete mechanic', timing: 'Action', mechanic: { kind: 'heal', amount: -2 } }
  ] };
  draft.applyContentDefinition('equipment', pendingValidation);
  assert.deepEqual(plain(draft.definition.equipmentTypes.at(-1)), pendingValidation);
});
