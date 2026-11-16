const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const root = path.join(__dirname, '../ProjectDelve.Web/wwwroot');
const authored = () => JSON.parse(fs.readFileSync(path.join(root, 'presentation/profiles.json'), 'utf8'));
const plain = value => JSON.parse(JSON.stringify(value));
function harness() {
  const context = vm.createContext({});
  for (const file of ['presentation-cursors.js', 'presentation-profiles.js', 'playtest-viewport.js'])
    vm.runInContext(fs.readFileSync(path.join(root, file), 'utf8').replace(/^import .*\r?\n/gm, '').replace(/^export /gm, ''), context);
  return { context, run: code => vm.runInContext(code, context), api: vm.runInContext('({validatePresentationProfiles, loadPresentationProfiles, presentationProfileDescriptors, resolvePresentationCursor, boardCandidates, boardInteraction})', context) };
}

test('catalog descriptors expose stable IDs and names without renderer configuration', async () => {
  const { api } = harness();
  const descriptors = await api.loadPresentationProfiles(async url => {
    assert.equal(url, './presentation/profiles.json'); return { ok: true, json: async () => authored() };
  });
  assert.ok(descriptors.length >= 57);
  assert.ok(Object.isFrozen(descriptors));
  for (const row of descriptors) assert.deepEqual(Object.keys(row), ['id', 'name']);
  for (const id of ['move', 'stay', 'standard-attack', 'end-turn', 'activate', 'collect-object', 'roll-dice', 'decline-follow-up', 'start-round'])
    assert.ok(descriptors.some(row => row.id === id), id);
  assert.equal(api.resolvePresentationCursor('standard-attack', 'pointer'), 'pointer', 'assets are optional and not yet loaded');
  assert.equal(api.resolvePresentationCursor('move', 'crosshair'), 'crosshair');
  assert.equal(api.resolvePresentationCursor('worldModifiers', 'pointer'), 'pointer');
});

test('validation rejects duplicate IDs and malformed CSS cursors; IDs remain opaque', () => {
  const { api } = harness();
  const profile = { id: 'a', name: 'A', cursor: { css: 'crosshair' } };
  for (const profiles of [[profile, profile], [{ ...profile, id: '' }], [{ ...profile, id: ' a ' }], [{ ...profile, name: '' }],
    ...[null, '../crosshair', '', [], {}, { css: 'url(evil)' }, { css: 'pointer', hotspot: [0, 0] }].map(cursor => [{ ...profile, cursor }])])
    assert.throws(() => api.validatePresentationProfiles({ schemaVersion: 1, profiles }));
  assert.throws(() => api.validatePresentationProfiles({ schemaVersion: 2, profiles: [] }));
  const catalog = api.validatePresentationProfiles({ schemaVersion: 1, profiles: [{ id: '__proto__', name: 'Opaque ID' }] });
  assert.equal(api.resolvePresentationCursor('__proto__', 'pointer', catalog), 'pointer');
  assert.equal(api.resolvePresentationCursor('constructor', 'grab', catalog), 'grab');
});

test('missing, failed and malformed catalogs fail open, and subsequent loads can recover', async () => {
  for (const fetcher of [() => { throw Error('offline'); }, async () => ({ ok: false, status: 404 }),
    async () => ({ ok: true, json: async () => ({ schemaVersion: 1, profiles: [{ id: 'broken' }] }) })]) {
    const { api } = harness();
    assert.deepEqual(plain(await api.loadPresentationProfiles(fetcher)), []);
    assert.equal(api.boardInteraction([{ interaction: { kind: 'Position' }, presentationProfileId: 'fireball' }], { targeting: true }).cursor, 'crosshair');
    assert.ok((await api.loadPresentationProfiles(async () => ({ ok: true, json: async () => authored() }))).length >= 57);
  }
});

test('actual choice profile controls cursor while busy, gesture, chooser and invalid-target priority remain unchanged', async () => {
  const { api } = harness(); await api.loadPresentationProfiles(async () => ({ ok: true, json: async () => authored() }));
  // Legacy CSS-only profile descriptors remain supported alongside asset IDs.
  await api.loadPresentationProfiles(async () => ({ok:true,json:async()=>authored()}));
  const first = { key: 'first-source', mechanicId: 'fireball', presentationProfileId: 'end-turn', interaction: { kind: 'Position' } };
  const second = { ...first, key: 'second-source', presentationProfileId: 'telekinesis' };
  assert.equal(api.boardInteraction([first], { targeting: true }).cursor, 'pointer');
  assert.equal(api.boardInteraction([second]).cursor, 'crosshair');
  assert.equal(api.boardInteraction([first]).choice, first);
  assert.equal(api.boardInteraction([first, second]).cursor, 'context-menu');
  assert.equal(api.boardInteraction([first, { ...first, key: 'third' }]).kind, 'chooser');
  assert.equal(api.boardInteraction([second], { dragging: true }).cursor, 'grabbing');
  assert.equal(api.boardInteraction([second], { dragging: true, busy: true }).cursor, 'default');
  assert.equal(api.boardInteraction([], { targeting: true }).cursor, 'not-allowed');
  assert.equal(api.boardInteraction([], { targeting: true, hit: false }).cursor, 'grab');
  assert.equal(api.boardInteraction([{ ...second, presentationProfileId: 'unknown-explicit' }]).cursor, 'pointer', 'unknown override does not silently fall back to fireball');
  assert.equal(api.boardInteraction([{ ...second, presentationProfileId: 'selfModifiers' }]).cursor, 'pointer');
});

test('mixed Cell, Unit and Edge hits use filtered candidates, including relevance and source-aware action selection', async () => {
  const { api } = harness(); await api.loadPresentationProfiles(async () => ({ ok: true, json: async () => authored() }));
  const cell = { x: 1, y: 1 }, edge = { a: cell, b: { x: 2, y: 1 } };
  const position = { key: 'cell', relevant: true, accountingId: 'equipment:a:spell', presentationProfileId: 'fireball', interaction: { kind: 'Position', position: cell } };
  const unit = { key: 'unit', relevant: false, accountingId: 'equipment:b:spell', presentationProfileId: 'move', interaction: { kind: 'Unit', unitId: 'large' } };
  const door = { key: 'edge', relevant: true, presentationProfileId: 'openDoor', interaction: { kind: 'Edge', edge } };
  const state = { physical: { figures: [{ id: 'large', position: cell }] } };
  const geometry = { large: { occupiedCells: [cell, { x: 1, y: 2 }] } }, decision = { candidates: [position, unit, door] };
  const candidates = visible => api.boardCandidates({ cell }, decision, state, geometry, visible);
  assert.equal(api.boardInteraction(candidates(() => true)).kind, 'chooser');
  assert.equal(api.boardInteraction(candidates(c => c.relevant), {targeting:true}).cursor, 'crosshair');
  assert.equal(api.boardInteraction(candidates(c => c.accountingId === unit.accountingId)).cursor, 'pointer');
  assert.equal(api.boardInteraction(api.boardCandidates({ cell: { x: 1, y: 2 } }, decision, state, geometry)).cursor, 'pointer');
  assert.equal(api.boardInteraction(api.boardCandidates({ cell, door: edge }, decision, state, geometry)).cursor, 'pointer');
});
