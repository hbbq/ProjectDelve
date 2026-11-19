const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const path = require('node:path');
const moduleAt = file => import(pathToFileURL(path.resolve(__dirname, '../ProjectDelve.Web/wwwroot/' + file)).href);
const allocation = { assignments: [{ objectId: 'boots-physical-1', unitId: 'alice' }, { objectId: 'boots-physical-2', unitId: 'bob' }, { objectId: 'potion-physical-1', unitId: 'alice' }] };
const view = (revision = 1, decisionId = 'station-1', sessionId = 'game-1') => ({ sessionId, revision, decisionId,
  presentation: { decision: { equipmentStation: { eligibleUnitIds: ['alice', 'bob'], originalAllocation: structuredClone(allocation) } } } });
const flush = () => new Promise(resolve => setImmediate(resolve));
const response = (request, preview = { canConfirm: false, diagnostics: [], units: [] }, savedDraft = null) => ({ sessionId: request.sessionId, revision: request.expectedRevision, decisionId: request.decisionId, preview, savedDraft });

test('Station draft restores saved invalid allocation, preserves physical copies and allows simultaneous Equipment swaps', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const requests = [];
  const draft = createStationDraft({ preview: async request => { requests.push(request); return response(request, { canConfirm: requests.length > 2, diagnostics: [], units: [] }); } });
  draft.sync(view()); await flush();
  assert.equal('allocation' in requests[0], false, 'first preview asks the server for saved draft');
  draft.assign('boots-physical-1', 'bob'); await flush();
  assert.equal(requests[1].allocation.assignments.filter(a => a.unitId === 'bob').length, 2, 'intermediate duplicate equipment reaches Engine validation');
  draft.assign('boots-physical-2', 'alice'); await flush();
  assert.deepEqual(draft.state().allocation.assignments, [
    { objectId: 'boots-physical-1', unitId: 'bob' }, { objectId: 'boots-physical-2', unitId: 'alice' }, { objectId: 'potion-physical-1', unitId: 'alice' }
  ]);
  assert.equal(draft.state().preview.canConfirm, true);
  draft.reset(); await flush(); assert.deepEqual(draft.state().allocation, allocation);
});

test('invalid saved drafts remain inspectable and reset recovers the original allocation', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const invalid = { assignments: [{ objectId: 'unknown', unitId: 'missing' }] };
  const draft = createStationDraft({ preview: async request => response(request, { canConfirm: false, diagnostics: [{ code: 'invalid-assignment', message: 'Unavailable object or recipient.' }], units: [] }, invalid) });
  draft.sync(view()); await flush();
  assert.deepEqual(draft.state().allocation, invalid);
  assert.equal(draft.state().preview.diagnostics[0].code, 'invalid-assignment');
  draft.reset(); await flush(); assert.deepEqual(draft.state().allocation, allocation);
});

test('out of order previews cannot overwrite newer edits, revisions, decisions or sessions', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const pending = [];
  const draft = createStationDraft({ preview: request => new Promise((resolve, reject) => pending.push({ request, resolve, reject })) });
  draft.sync(view()); pending[0].resolve(response(pending[0].request)); await flush();
  draft.assign('boots-physical-1', 'bob'); draft.assign('boots-physical-2', 'alice');
  assert.equal(draft.state().preview, null, 'old comparison is removed during a new check');
  pending[2].resolve(response(pending[2].request, { canConfirm: true })); await flush();
  pending[1].reject(new Error('late failure')); await flush();
  assert.equal(draft.state().preview.canConfirm, true); assert.equal(draft.state().error, '');
  draft.sync(view(2));
  draft.sync(view(3, 'station-2', 'game-2'));
  pending[3].resolve(response(pending[3].request, { canConfirm: true })); await flush();
  assert.equal(draft.state().preview, null);
  pending[4].resolve(response(pending[4].request)); await flush();
  assert.equal(draft.state().boundary.sessionId, 'game-2'); assert.deepEqual(draft.state().allocation, allocation);
  draft.sync(null); assert.equal(draft.state().boundary, null);
});

test('failed first preview retries saved draft and unprojected pending state never authorizes editing', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const requests = [];
  const draft = createStationDraft({ preview: async request => { requests.push(request); if (requests.length === 1) throw new Error('Disconnected'); return response(request); } });
  draft.sync({ ...view(), presentation: { decision: null }, result: { state: { machineInteraction: {} } } });
  assert.equal(requests.length, 0);
  draft.sync(view()); await flush(); assert.equal(draft.state().error, 'Disconnected');
  await draft.retry(); assert.equal('allocation' in requests[1], false); assert.equal(draft.state().initialized, true);
});

class Element {
  constructor(tag, document) { this.tagName = tag; this.document = document; this.children = []; this.dataset = {}; this.listeners = {}; this.attributes = {}; this.isConnected = true; this.open = false; }
  append(...nodes) { for (const node of nodes) { this.children.push(node); node.parentElement = this; } }
  replaceChildren(...nodes) { this.children = []; this.append(...nodes); }
  addEventListener(name, fn) { this.listeners[name] = fn; }
  setAttribute(name, value) { this.attributes[name] = value; }
  focus() { this.document.activeElement = this; }
  showModal() { this.open = true; }
  close() { this.open = false; }
  querySelectorAll(selector) { return this.children.flatMap(node => [...(selector === 'select' && node.tagName === 'select' ? [node] : []), ...node.querySelectorAll(selector)]); }
  closest(selector) { return this.tagName === selector ? this : this.parentElement?.closest(selector); }
}
async function uiHarness(options = {}) {
  const document = { createElement(tag) { return new Element(tag, this); } }; document.body = new Element('body', document);
  const prior = new Element('button', document); prior.focus(); global.document = document;
  const cards = { alice: { displayName: 'Alice', ownedObjects: [{ id: 'boots-physical-1', content: { name: 'Boots', category: 'Equipment', text: 'Authored rules.' } }, { id: 'potion-physical-1', content: { name: 'Potion', category: 'Item' } }] }, bob: { displayName: 'Bob', ownedObjects: [{ id: 'boots-physical-2', content: { name: 'Boots', category: 'Equipment' } }] } };
  let current = { ...view(), expedition: { id: 'expedition-1' } }; current.presentation.cards = { ...cards, ...options.cards };
  const calls = [], previews = [], navigation = [];
  let visible = options.visible ?? true;
  if (options.reserved) current.presentation.decision.reservedConsumptionObjectId = options.reserved;
  const { mountEquipmentStation } = await moduleAt('equipment-station.js');
  const preview = { canConfirm: true, diagnostics: [], units: [{ unitId: 'alice', allocation: [{ id: 'boots-physical-1', lootCardId: 'test-loot-provenance' }], mov: 8, rng: 2, atk: 3, def: 4, hp: 9, sightDistance: 5, baseline: { mov: 5 }, difference: { mov: 3 }, movementCapabilities: ['land'], abilityPreviews: [{ ability: { sourceKind: 'Equipment', sourceTypeId: 'boots', sourceInstanceId: 'boots-physical-1', definition: { key: 'run', name: 'Run', timing: 'Passive' } }, content: { name: 'Running boots', category: 'Passive', description: 'Canonical Engine wording.' }, remainingUses: 2, applicable: false }] }] };
  const ui = mountEquipmentStation({ getView: () => current, request: async (_, body) => { previews.push(body); return response(body, options.preview ?? preview); }, command: async (operation, body) => { calls.push({ operation, body }); return true; }, ready: () => true, visible: () => visible, navigate: mode => navigation.push(mode) });
  const all = () => { const walk = node => [node, ...node.children.flatMap(walk)]; return walk(document.body); };
  const byId = id => all().find(node => node.id === `equipment-station-${id}`);
  const text = () => all().map(node => node.textContent ?? '').join(' ');
  ui.update(); await flush();
  return { ui, prior, document, byId, text, all, calls, previews, navigation, setVisible(value) { visible = value; ui.update(); }, setView(value) { current = value; ui.update(); } };
}
test('editor uses modal focus, server stat deltas and Ability descriptions; save and confirm submit complete typed allocations', async () => {
  const h = await uiHarness();
  assert.equal(h.byId('dialog').open, true); assert.equal(h.document.activeElement.id, 'equipment-station-title');
  assert.match(h.text(), /Canonical Engine wording/); assert.match(h.text(), /Does not apply in this comparison/); assert.match(h.text(), /2 uses remaining/); assert.match(h.text(), /\+3/);
  assert.match(h.text(), /Loot Card: test-loot-provenance/, 'provenance comes from the Engine preview OwnedObject, independently of content metadata');
  await h.byId('save').listeners.click();
  assert.deepEqual(h.calls[0], { operation: 'expedition/save', body: { equipmentAllocation: allocation } });
  await h.byId('confirm').listeners.click();
  assert.deepEqual(h.calls[1], { operation: 'machine/confirm', body: { equipmentAllocation: allocation } });
  let stopped = false; h.byId('dialog').listeners.keydown({ stopPropagation() { stopped = true; } }); assert.equal(stopped, true);
  h.setView(null); assert.equal(h.byId('dialog').open, false); assert.equal(h.document.activeElement, h.prior);
});
test('Cancel and Escape use ordinary Machine cancellation; recipient chooser sends only physical ownership assignments', async () => {
  const h = await uiHarness();
  const select = h.all().find(node => node.dataset.stationObject === 'boots-physical-1'); select.value = 'bob'; select.listeners.change(); await flush();
  assert.deepEqual(h.previews[1].allocation.assignments[0], { objectId: 'boots-physical-1', unitId: 'bob' });
  await h.byId('cancel').listeners.click(); assert.deepEqual(h.calls[0], { operation: 'machine/cancel', body: {} });
  let prevented = false; h.byId('dialog').listeners.cancel({ preventDefault() { prevented = true; } }); await flush();
  assert.equal(prevented, true); assert.equal(h.calls[1].operation, 'machine/cancel');
});
test('invalid proposals retain Engine diagnostics and can be saved; confirm waits for Engine approval', async () => {
  const h = await uiHarness({ preview: { canConfirm: false, diagnostics: [{ code: 'illegal-occupancy', message: 'The proposed Equipment does not permit current placement.', unitId: 'alice' }], units: [] } });
  assert.equal(h.byId('confirm').disabled, true);
  assert.match(h.text(), /does not permit current placement/);
  await h.byId('confirm').listeners.click(); assert.equal(h.calls.length, 0);
  assert.equal(h.byId('save').disabled, false);
  await h.byId('save').listeners.click(); assert.equal(h.calls[0].operation, 'expedition/save');
  await h.byId('reset').listeners.click(); await flush(); assert.deepEqual(h.ui.allocation(), allocation);
});
test('Ability and object disclosures survive a new preview; duplicate copies have distinct readable labels', async () => {
  const h = await uiHarness();
  const details = h.all().find(node => node.tagName === 'details' && node.children[0].textContent === 'Ability definitions and sources');
  details.open = true; details.listeners.toggle();
  const select = h.all().find(node => node.dataset.stationObject === 'potion-physical-1'); select.value = 'bob'; select.listeners.change(); await flush();
  const restored = h.all().find(node => node.tagName === 'details' && node.children[0].textContent === 'Ability definitions and sources');
  assert.equal(restored.open, true); assert.match(h.text(), /Boots \(copy 1\)/); assert.match(h.text(), /Boots \(copy 2\)/);
});
test('pending Station stays off Home and Expedition navigation, reopening only for the same gameplay interaction', async () => {
  const h = await uiHarness({ visible: false });
  assert.equal(h.byId('dialog').open, false, 'reload on Home must not trap navigation');
  h.setVisible(true); assert.equal(h.byId('dialog').open, true);
  const focus = h.byId('focus'); focus.value = 'bob'; focus.listeners.change();
  const details = h.all().find(node => node.tagName === 'details' && node.children[0].textContent === 'Ability definitions and sources');
  details.open = true; details.listeners.toggle();
  h.setVisible(false); assert.equal(h.byId('dialog').open, false);
  h.setVisible(true); assert.equal(h.byId('focus').value, 'bob', 'same pending interaction retains focused Unit');
  assert.equal(details.open, true);
  const next = view(2, 'station-2', 'game-2'); next.presentation.cards = { alice: { displayName: 'Alice' }, bob: { displayName: 'Bob' } };
  h.setView(next); await flush();
  assert.equal(h.byId('focus').value, 'alice', 'new interaction resets focus');
  const reopened = h.all().find(node => node.tagName === 'details' && node.children[0].textContent === 'Ability definitions and sources');
  assert.equal(reopened.open, false, 'new interaction resets expanded disclosures');
});
test('successful Save exposes existing Expedition navigation and keeps the complete pending draft', async () => {
  const h = await uiHarness();
  assert.equal(h.byId('return').hidden, true);
  await h.byId('save').listeners.click();
  assert.equal(h.byId('return').hidden, false);
  h.byId('return').listeners.click(); assert.deepEqual(h.navigation, ['expeditions']);
  h.setVisible(false); assert.equal(h.byId('dialog').open, false); assert.deepEqual(h.ui.allocation(), allocation);
  h.setVisible(true); assert.equal(h.byId('dialog').open, true);
  const chooser = h.all().find(node => node.dataset.stationObject === 'boots-physical-1'); chooser.value = 'bob'; chooser.listeners.change(); await flush();
  assert.equal(h.byId('return').hidden, true, 'unsaved edits require another Save before safe return');
});
test('reserved Single Use Item warning remains visible without opening comparison explanation', async () => {
  const h = await uiHarness({ reserved: 'potion-physical-1' });
  assert.equal(h.byId('reserved').hidden, false);
  assert.match(h.byId('reserved').textContent, /Potion is reserved for consumption/);
  const explanation = h.all().find(node => node.tagName === 'details' && node.children[0].textContent === 'How comparison works');
  assert.equal(explanation.open, false);
});
test('closing the modal restores the page scroll position retained before the root scroll lock', async () => {
  const previous = { scrollX: global.scrollX, scrollY: global.scrollY, scrollTo: global.scrollTo };
  const restored = [];
  try {
    global.scrollX = 3; global.scrollY = 217; global.scrollTo = (...position) => restored.push(position);
    const h = await uiHarness(); h.setVisible(false);
    assert.deepEqual(restored, [[3, 217]]);
    assert.equal(h.byId('dialog').open, false);
  } finally { Object.assign(global, previous); }
});
test('lost Ability names, descriptions and individual source uses come directly from opening Engine previews', async () => {
  const lost = (id, name, timing, remainingUses, applicable) => ({ ability: { sourceKind: 'Item', sourceTypeId: 'potion', sourceInstanceId: id,
    definition: { key: 'opaque key/%with spaces', name, timing } }, content: { name, category: timing, description: `Opening definition for ${name}.` }, remainingUses, applicable });
  const h = await uiHarness({ preview: { canConfirm: true, diagnostics: [], units: [{ unitId: 'alice', abilityPreviews: [],
    lostAbilityPreviews: [lost('potion-physical-1', 'Second Wind', 'Action', 1, false), lost('boots-physical-1', 'Passive Charm', 'Passive', 0, false)] }] } });
  assert.match(h.text(), /Lost: Second Wind, Passive Charm/);
  assert.match(h.text(), /Opening definition for Second Wind/);
  assert.match(h.text(), /Opening definition for Passive Charm/);
  assert.match(h.text(), /1 use remaining/); assert.doesNotMatch(h.text(), /1 uses remaining|use\(s\)|opaque key/);
  const panels = h.all().filter(node => node.className === 'station-ability');
  const textOf = node => [node.textContent ?? '', ...node.children.map(textOf)].join(' ');
  assert.match(textOf(panels[0]), /Action Ability · Potion/);
  assert.doesNotMatch(textOf(panels[0]), /Does not apply/, 'active Ability is defined by timing, not passive comparison applicability');
  assert.match(textOf(panels[1]), /Exhausted · 0 uses remaining · Does not apply in this comparison/);
});
test('timed Ability applicability is not presented as a passive restriction and stat strip labels maximum HP', async () => {
  const h = await uiHarness({ preview: { canConfirm: true, diagnostics: [], units: [{ unitId: 'alice', hp: 7,
    abilityPreviews: [{ ability: { sourceKind: 'Unit', sourceInstanceId: 'alice', definition: { name: 'Quick Action', timing: 'BonusAction' } },
      content: { name: 'Quick Action', category: 'Bonus Action', description: 'Use at its stated timing.' }, remainingUses: 1, applicable: false }] }] } });
  assert.match(h.text(), /Bonus Action Ability · Native/); assert.doesNotMatch(h.text(), /Does not apply in this comparison/);
  const strip = h.all().find(node => node.className === 'station-stat-strip');
  assert.equal(strip.children.find(node => node.attributes['aria-label']?.startsWith('Max HP:'))?.attributes['aria-label'], 'Max HP: 7');
});
test('duplicate Unit and physical-copy numbering excludes nonparticipating visible cards; control refresh keeps mounted preview', async () => {
  const h = await uiHarness({ cards: { outsider: { displayName: 'Alice', ownedObjects: [{ id: 'unrelated-boots', content: { name: 'Boots', category: 'Equipment' } }] } } });
  assert.equal(h.byId('focus').children[0].textContent, 'Alice');
  assert.match(h.text(), /Boots \(copy 2\)/); assert.doesNotMatch(h.text(), /Boots \(copy 3\)/);
  const card = h.byId('roster').children[0]; h.ui.update(); h.ui.update();
  assert.equal(h.byId('roster').children[0], card, 'stable presentation version avoids remounting the full roster on unrelated control refreshes');
});
