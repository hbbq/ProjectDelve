const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const context = vm.createContext({ structuredClone });
vm.runInContext(fs.readFileSync(__dirname + '/../ProjectDelve.Web/wwwroot/content-library-state.js', 'utf8').replace(/^export /gm, '') + '\nglobalThis.Draft = LibraryContentDraft;', context);
const Draft = context.Draft;
const plain = value => JSON.parse(JSON.stringify(value));

test('library package workspace uses exact stamps and detached data without a scenario', () => {
  const draft = Draft.create('workshop', 'Workshop', ['standard']);
  const stamp = draft.stamp;
  draft.apply({ family: 'equipment', definition: { id: 'bell', name: 'Bell' }, expectedRevision: stamp });
  assert.throws(() => draft.apply({ family: 'equipment', definition: { id: 'other' }, expectedRevision: stamp }), /workspace changed/);
  draft.bind('gear', 3);
  assert.deepEqual(plain(draft.request().bindings), [{ packageId: 'gear', revision: 3 }]);
  assert.deepEqual(plain(draft.package.dependencies), ['standard', 'gear']);
  const detached = draft.request(); detached.package.definitions.equipmentTypes[0].name = 'Mutated';
  assert.equal(draft.package.definitions.equipmentTypes[0].name, 'Bell');
  draft.load({ revision: 1, libraryId: 'origin', package: draft.package, bindings: draft.bindings });
  assert.equal(draft.dirty, false);
  draft.apply({ family: 'equipment', definition: { id: 'bell', name: 'Edited' }, existingId: 'bell', expectedRevision: draft.stamp });
  assert.equal(draft.dirty, true); draft.reset(); assert.equal(draft.package.definitions.equipmentTypes[0].name, 'Bell');
  assert.equal(Object.hasOwn(draft.request(), 'scenario'), false);
});

test('historical packages and immutable Type IDs cannot be overwritten by a destination', () => {
  const record = { revision: 1, package: { id: 'gear', definitions: { equipmentTypes: [{ id: 'bell' }] } }, bindings: [] };
  const old = new Draft(record, { currentRevision: 2 });
  assert.equal(old.editable, false);
  assert.throws(() => old.apply({ family: 'equipment', existingId: 'bell', definition: { id: 'bell' }, expectedRevision: old.stamp }), /workspace changed/);
  const current = new Draft(record);
  assert.throws(() => current.apply({ family: 'equipment', existingId: 'bell', definition: { id: 'replacement' }, expectedRevision: current.stamp }), /existing Type ID/);
  assert.equal(record.package.definitions.equipmentTypes[0].id, 'bell');
});


test('Library owns Unit definitions through the same revision and copy boundaries', () => {
  const draft=Draft.create('units','Units');
  const unit={id:'keeper',displayName:'Keeper',localAbilities:[{key:'summon',mechanic:{kind:'summonAdjacent',unitTypeId:'friend'}}]};
  draft.apply({family:'units',definition:unit,expectedRevision:draft.stamp});
  unit.localAbilities[0].mechanic.unitTypeId='outside';
  assert.equal(draft.context().unitTypes[0].localAbilities[0].mechanic.unitTypeId,'friend');
  draft.apply({family:'units',definition:{...unit,displayName:'Renamed'},existingId:'keeper',expectedRevision:draft.stamp});
  assert.equal(draft.context().unitTypes[0].displayName,'Renamed');
  assert.throws(()=>draft.apply({family:'units',definition:{id:'replacement'},existingId:'keeper',expectedRevision:draft.stamp}),/existing Type ID/);
});
