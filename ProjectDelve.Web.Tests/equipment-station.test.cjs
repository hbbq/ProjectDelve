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

test('Station upgrade selections persist through ownership changes, sales remove conflicting upgrades and Reset clears the proposal', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const requests = [];
  const draft = createStationDraft({ preview: async request => { requests.push(request); return response(request); } });
  draft.sync(view()); await flush();
  draft.upgradeUnit('alice'); await flush();
  draft.upgradeEquipment('boots-physical-1'); await flush();
  draft.assign('boots-physical-1', 'bob'); await flush();
  assert.deepEqual(draft.state().allocation.upgradedUnitIds, ['alice']);
  assert.deepEqual(draft.state().allocation.upgradedEquipmentIds, ['boots-physical-1']);
  assert.deepEqual(draft.state().allocation.assignments[0], { objectId: 'boots-physical-1', unitId: 'bob' });
  draft.sell('boots-physical-1'); await flush();
  assert.deepEqual(draft.state().allocation.upgradedEquipmentIds, []);
  const count = requests.length;
  draft.upgradeEquipment('boots-physical-1'); await flush();
  assert.equal(requests.length, count, 'sold Equipment cannot also be marked for upgrade');
  draft.assign('boots-physical-1', 'alice'); await flush();
  draft.upgradeEquipment('boots-physical-1'); await flush();
  draft.upgradeUnit('alice'); await flush();
  assert.deepEqual(draft.state().allocation.upgradedUnitIds, []);
  assert.deepEqual(draft.state().allocation.upgradedEquipmentIds, ['boots-physical-1']);
  assert.ok(!Object.hasOwn(requests.at(-1).allocation, 'upgradeCost'), 'the server receives only selected physical identities');
  draft.reset(); await flush();
  assert.deepEqual(draft.state().allocation, allocation);
});

test('resumed upgrade draft retains its complete selections and an upgrade-only proposal waits for the Engine response', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const saved = { ...structuredClone(allocation), upgradedUnitIds: ['alice'], upgradedEquipmentIds: ['boots-physical-2'] };
  const draft = createStationDraft({ preview: async request => response(request,
    { canConfirm: true, totalUpgradeCost: 7, resultingCredits: 0 }, saved) });
  draft.sync(view()); await flush();
  assert.deepEqual(draft.state().allocation, saved);
  assert.equal(draft.state().preview.totalUpgradeCost, 7);
  draft.sync(view(2)); await flush();
  assert.deepEqual(draft.state().allocation, saved);
});

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

test('Station sale draft removes only the selected physical copy, supports combined transfer, restore and Reset', async () => {
  const { createStationDraft } = await moduleAt('equipment-station-state.js');
  const requests = [], saved = { assignments: [allocation.assignments[1], allocation.assignments[2]], soldObjectIds: ['boots-physical-1'] };
  const draft = createStationDraft({ preview: async request => { requests.push(request); return response(request, { canConfirm: true }, saved); } });
  draft.sync(view()); await flush(); assert.deepEqual(draft.state().allocation, saved);
  draft.assign('boots-physical-2', 'alice'); await flush();
  assert.deepEqual(requests.at(-1).allocation.soldObjectIds, ['boots-physical-1']);
  draft.assign('boots-physical-1', 'bob'); await flush();
  assert.deepEqual(draft.state().allocation.soldObjectIds, []);
  assert.ok(draft.state().allocation.assignments.some(value => value.objectId === 'boots-physical-1' && value.unitId === 'bob'));
  draft.sell('boots-physical-1'); draft.sell('boots-physical-1'); await flush();
  assert.deepEqual(draft.state().allocation.soldObjectIds, ['boots-physical-1']);
  draft.reset(); await flush(); assert.deepEqual(draft.state().allocation, allocation);
});

test('sale UI shows authoritative balances and zero values; combined sale and transfer survives Save and Confirm', async () => {
  const h = await uiHarness({ cards: { alice: { displayName: 'Alice', ownedObjects: [
    { id: 'boots-physical-1', sellValue: 0, content: { name: 'Boots', category: 'Equipment' } },
    { id: 'potion-physical-1', sellValue: null, content: { name: 'Potion', category: 'Item' } }
  ] } }, preview: { canConfirm: true, diagnostics: [], units: [], sales: [{ objectId: 'boots-physical-1', fromUnitId: 'alice', sellValue: 0 }], currentCredits: 7, totalProceeds: 0, resultingCredits: 7 } });
  assert.match(h.byId('credits').textContent, /Current Credits: 7.*Sale proceeds: 0.*Resulting Credits: 7/);
  assert.equal(h.all().some(node => node.dataset.stationSell === 'potion-physical-1'), false);
  const sell = h.all().find(node => node.dataset.stationSell === 'boots-physical-1');
  assert.equal(sell.textContent, 'Sell for 0 Credits'); sell.focus(); sell.listeners.click(); await flush();
  assert.equal(h.byId('sales').hidden, false);
  assert.equal(h.document.activeElement.dataset.stationRestore, 'boots-physical-1');
  const recipient = h.all().find(node => node.dataset.stationObject === 'boots-physical-2'); recipient.value = 'alice'; recipient.listeners.change(); await flush();
  await h.byId('save').listeners.click(); await h.byId('confirm').listeners.click();
  for (const call of h.calls) {
    assert.deepEqual(call.body.equipmentAllocation.soldObjectIds, ['boots-physical-1']);
    assert.equal(call.body.equipmentAllocation.assignments.some(row => row.objectId === 'boots-physical-1'), false);
    assert.ok(call.body.equipmentAllocation.assignments.some(row => row.objectId === 'boots-physical-2' && row.unitId === 'alice'));
  }
  const restore = h.all().find(node => node.dataset.stationRestore === 'boots-physical-1'); restore.value = 'bob'; restore.listeners.change(); await flush();
  assert.equal(h.byId('sales').hidden, true);
  assert.deepEqual(h.ui.allocation().soldObjectIds, []);
  await h.byId('reset').listeners.click(); await flush(); assert.deepEqual(h.ui.allocation(), allocation);
});

test('sale-only draft needs Engine approval, can be saved when invalid, and Cancel submits no sale', async () => {
  const h = await uiHarness({ cards: { alice: { displayName: 'Alice', ownedObjects: [
    { id: 'boots-physical-1', sellValue: 9, content: { name: 'Water Boots', category: 'Equipment' } }
  ] } }, preview: { canConfirm: false, diagnostics: [{ code: 'illegal-occupancy', message: 'The sale would leave a Unit unable to occupy Water.' }], units: [], sales: [], currentCredits: 1, totalProceeds: 9, resultingCredits: 10 } });
  h.all().find(node => node.dataset.stationSell === 'boots-physical-1').listeners.click(); await flush();
  assert.equal(h.byId('confirm').disabled, true);
  assert.match(h.text(), /sale would leave a Unit unable to occupy Water/);
  await h.byId('confirm').listeners.click(); assert.equal(h.calls.length, 0);
  await h.byId('save').listeners.click();
  assert.deepEqual(h.calls[0].body.equipmentAllocation.soldObjectIds, ['boots-physical-1']);
  await h.byId('cancel').listeners.click(); assert.deepEqual(h.calls[1], { operation: 'machine/cancel', body: {} });
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
  if (options.upgradeUnits) current.presentation.decision.equipmentStation.eligibleUpgradeUnitIds = options.upgradeUnits;
  if (options.recruit) current.presentation.decision.equipmentStation.canRecruit = true;
  const calls = [], previews = [], navigation = [];
  let visible = options.visible ?? true;
  if (options.reserved) current.presentation.decision.reservedConsumptionObjectId = options.reserved;
  const { mountEquipmentStation } = await moduleAt('equipment-station.js');
  const preview = { canConfirm: true, diagnostics: [], units: [{ unitId: 'alice', allocation: [{ id: 'boots-physical-1', lootCardId: 'test-loot-provenance' }], mov: 8, rng: 2, atk: 3, def: 4, hp: 9, sightDistance: 5, baseline: { mov: 5 }, difference: { mov: 3 }, movementCapabilities: ['land'], abilityPreviews: [{ ability: { sourceKind: 'Equipment', sourceTypeId: 'boots', sourceInstanceId: 'boots-physical-1', definition: { key: 'run', name: 'Run', timing: 'Passive' } }, content: { name: 'Running boots', category: 'Passive', description: 'Canonical Engine wording.' }, remainingUses: 2, applicable: false }] }] };
  const ui = mountEquipmentStation({ getView: () => current, request: async (url, body) => {
    previews.push(body);
    return url.endsWith('/recruitment/preview') ? response(body, options.recruit(body))
      : { ...response(body, options.preview ?? preview), savedRecruitTypeId: options.savedRecruitTypeId };
  }, command: async (operation, body) => { calls.push({ operation, body }); return true; }, ready: () => true, visible: () => visible, navigate: mode => navigation.push(mode) });
  const all = () => { const walk = node => [node, ...node.children.flatMap(walk)]; return walk(document.body); };
  const byId = id => all().find(node => node.id === `equipment-station-${id}`);
  const text = () => all().map(node => node.textContent ?? '').join(' ');
  ui.update(); await flush();
  return { ui, prior, document, byId, text, all, calls, previews, navigation, setVisible(value) { visible = value; ui.update(); }, setView(value) { current = value; ui.update(); } };
}
test('Station names the owning Unit Level and distinguishes both physical Levels of same-Type Equipment', async () => {
  const gear = (id, level) => ({ id, level, content: { name: 'Boots', category: 'Equipment' } });
  const h = await uiHarness({ cards: {
    alice: { displayName: 'Alice', level: 2, ownedObjects: [gear('boots-physical-1', 2)] },
    bob: { displayName: 'Bob', level: 1, ownedObjects: [gear('boots-physical-2', 1)] }
  } });
  assert.match(h.text(), /Alice · Level 2/);
  assert.match(h.text(), /Boots · Level 2 \(copy 1\)/);
  assert.match(h.text(), /Boots · Level 1 \(copy 2\)/);
  assert.match(h.text(), /Canonical Engine wording/);
});

function recruitQuote({ recruitTypeId }, affordable = true) {
  const offer = { unitTypeId: 'test-hero', name: 'Test Hero', cost: 75, canRecruit: affordable,
    type: { mov: 4, rng: 1, atk: 2, def: 2, hp: 5, localAbilities: [{ name: 'Operate' }] },
    spawnCell: { x: 2, y: 3 } };
  return { offers: [offer], partySize: 5, partyCapacity: 6, currentCredits: affordable ? 75 : 60,
    selectedTypeId: recruitTypeId, resultingCredits: affordable ? 0 : -15, canConfirm: !!recruitTypeId && affordable,
    diagnostics: !affordable && recruitTypeId ? [{ message: 'Insufficient Credits.' }] : [] };
}

test('Recruit previews placement, switches independently of Manage and confirms only the selected Type', async () => {
  const h = await uiHarness({ recruit: recruitQuote });
  await h.byId('mode').listeners.click(); await flush();
  assert.equal(h.byId('recruit-confirm').disabled, true);
  assert.equal(h.byId('confirm').hidden, true);
  const choice = h.all().find(n => n.id === 'recruitment-type');
  choice.value = 'test-hero'; choice.listeners.change(); await flush();
  assert.match(h.text(), /Party 5\/6/); assert.match(h.text(), /Spawn Cell \(2, 3\)/);
  assert.equal(h.byId('recruit-confirm').disabled, false);
  await h.byId('recruit-confirm').listeners.click();
  assert.deepEqual(h.calls, [{ operation: 'machine/recruit', body: { recruitTypeId: 'test-hero' } }]);
});

test('resumed recruitment selection requotes its Type and reports an unaffordable proposal', async () => {
  const h = await uiHarness({ recruit: request => recruitQuote(request, false), savedRecruitTypeId: 'test-hero' });
  assert.match(h.text(), /Insufficient Credits/);
  assert.equal(h.byId('recruit-confirm').disabled, true);
  assert.equal(h.byId('recruit-confirm').hidden, false);
  await h.byId('cancel').listeners.click();
  assert.deepEqual(h.calls, [{ operation: 'machine/cancel', body: {} }]);
});
test('Station shows affected activation budgets and preserves the unchanged counts in the full comparison', async () => {
  const h = await uiHarness({ preview: { canConfirm: true, diagnostics: [], units: [{ unitId: 'alice', moves: 2, actions: 1, bonusActionUses: 3,
    baseline: { moves: 1, actions: 1, bonusActionUses: 2 }, difference: { moves: 1, actions: 0, bonusActionUses: 1 } }] } });
  const strip = h.all().find(node => node.className === 'station-stat-strip');
  const labels = strip.children.map(node => node.attributes['aria-label']);
  assert.ok(labels.includes('Moves per activation: 2, change +1'));
  assert.ok(labels.includes('Uses per Bonus Action: 3, change +1'));
  assert.ok(!labels.some(text => text.startsWith('Actions per activation')));
  assert.match(h.text(), /Actions per activation/);
});

test('Station renders paid and free authoritative upgrade offers with final HP, costs and individual Equipment selections', async () => {
  const preview = { canConfirm: true, diagnostics: [], currentCredits: 10, totalProceeds: 4, totalUpgradeCost: 12, resultingCredits: 2,
    unitUpgrades: [{ targetId: 'alice', currentLevel: 1, nextLevel: 2, cost: 0 }],
    equipmentUpgrades: [{ targetId: 'boots-physical-1', currentLevel: 1, nextLevel: 2, cost: 12 }],
    units: [{ unitId: 'alice', currentLevel: 1, resultingLevel: 2, currentHp: 2, resultingHp: 7,
      allocation: [{ id: 'boots-physical-1', level: 2 }], hp: 7, abilityPreviews: [{ ability: { sourceKind: 'Equipment', sourceInstanceId: 'boots-physical-1', definition: { name: 'Next Step' } },
        content: { name: 'Next Step', category: 'Bonus Action', description: 'Destination configuration.' }, remainingUses: 3 }] }] };
  const h = await uiHarness({ preview });
  assert.match(h.text(), /Upgrade costs: 12 · Resulting Credits: 2/);
  assert.match(h.text(), /Current HP 2 → Proposed HP 7/);
  assert.match(h.text(), /Level 1 → Level 2/);
  assert.match(h.text(), /3 uses remaining after Confirm/);
  assert.match(h.text(), /Destination configuration/);
  const unit = h.all().find(node => node.dataset.stationUpgradeUnit === 'alice');
  assert.match(unit.textContent, /0 Credits/);
  unit.listeners.click(); await flush();
  const equipment = h.all().find(node => node.dataset.stationUpgradeEquipment === 'boots-physical-1');
  equipment.listeners.click(); await flush();
  assert.deepEqual(h.ui.allocation().upgradedUnitIds, ['alice']);
  assert.deepEqual(h.ui.allocation().upgradedEquipmentIds, ['boots-physical-1']);
  assert.equal(h.all().find(node => node.dataset.stationUpgradeEquipment === 'boots-physical-1').attributes['aria-pressed'], 'true');
  assert.equal(h.all().some(node => node.dataset.stationUpgradeEquipment === 'boots-physical-2'), false);
  await h.byId('confirm').listeners.click();
  assert.deepEqual(h.calls[0].body.equipmentAllocation.upgradedEquipmentIds, ['boots-physical-1']);
});

test('Units without inventory can upgrade through the focused roster and are excluded from recipient choices', async () => {
  const h = await uiHarness({ upgradeUnits: ['alice', 'bob', 'runner'],
    cards: { runner: { displayName: 'Runner', level: 1 } },
    preview: { canConfirm: true, units: [{ unitId: 'runner', currentLevel: 1, resultingLevel: 2 }],
      unitUpgrades: [{ targetId: 'runner', currentLevel: 1, nextLevel: 2, cost: 4 }] } });
  assert.deepEqual(h.byId('focus').children.map(node => node.value), ['alice', 'bob', 'runner']);
  const selector = h.all().find(node => node.dataset.stationObject === 'boots-physical-1');
  assert.deepEqual(selector.children.map(node => node.value), ['alice', 'bob']);
  const focus = h.byId('focus'); focus.value = 'runner'; focus.listeners.change();
  assert.equal(h.all().find(node => node.dataset.stationUnit === 'runner').dataset.focused, 'true');
  h.all().find(node => node.dataset.stationUpgradeUnit === 'runner').listeners.click(); await flush();
  assert.deepEqual(h.ui.allocation().upgradedUnitIds, ['runner']);
});

test('resumed Station without an upgrade roster can show an authoritative offer for a Unit without inventory', async () => {
  const h = await uiHarness({ cards: { runner: { displayName: 'Runner', level: 1 } },
    preview: { canConfirm: true, units: [{ unitId: 'runner' }],
      unitUpgrades: [{ targetId: 'runner', currentLevel: 1, nextLevel: 2, cost: 0 }] } });
  assert.ok(h.byId('focus').children.some(node => node.value === 'runner'));
  h.all().find(node => node.dataset.stationUpgradeUnit === 'runner').listeners.click(); await flush();
  assert.deepEqual(h.ui.allocation().upgradedUnitIds, ['runner']);
});

test('Station compares complete opening and proposed Ability configurations and omits ineligible empty Unit panels', async () => {
  const entry = (timing, remainingUses) => ({ ability: { sourceKind: 'Unit', definition: { key: 'swap', name: 'Friendly Swap', timing, usage: { maxUses: remainingUses } } },
    content: { name: 'Friendly Swap', category: timing, description: `Authored ${timing} rules.` }, remainingUses });
  const h = await uiHarness({ upgradeUnits: ['alice', 'bob', 'empty'],
    cards: { empty: { displayName: 'Empty' } }, preview: { canConfirm: true,
      units: [{ unitId: 'alice', openingAbilityPreviews: [entry('Action', 1)], abilityPreviews: [entry('Bonus Action', 3)] }] } });
  assert.deepEqual(h.byId('focus').children.map(node => node.value), ['alice', 'bob']);
  assert.equal(h.all().some(node => node.dataset.stationUnit === 'empty'), false);
  assert.match(h.text(), /Opening Abilities and Uses/);
  assert.match(h.text(), /Proposed Abilities and Uses/);
  assert.match(h.text(), /Authored Action rules/);
  assert.match(h.text(), /Authored Bonus Action rules/);
  assert.match(h.text(), /1 use remaining/);
  assert.match(h.text(), /3 uses remaining/);
  assert.match(h.text(), /Maximum 1 per game/);
  assert.match(h.text(), /Maximum 3 per game/);
});
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
  assert.match(h.text(), /Lost: Second Wind, Passive Charm \(copy 1\)/);
  assert.match(h.text(), /Opening definition for Second Wind/);
  assert.match(h.text(), /Opening definition for Passive Charm/);
  assert.match(h.text(), /1 use remaining/); assert.doesNotMatch(h.text(), /1 uses remaining|use\(s\)|opaque key/);
  const panels = h.all().filter(node => node.className === 'station-ability');
  const textOf = node => [node.textContent ?? '', ...node.children.map(textOf)].join(' ');
  assert.match(textOf(panels[0]), /Action Ability · Potion/);
  assert.doesNotMatch(textOf(panels[0]), /Does not apply/, 'active Ability is defined by timing, not passive comparison applicability');
  assert.match(textOf(panels[1]), /Exhausted · 0 uses remaining · Does not apply in this comparison/);
});

test('compact Ability changes distinguish duplicate physical sources using inventory copy numbers', async () => {
  const grant = id => ({ sourceKind: 'Equipment', sourceInstanceId: id, definition: { key: 'second-wind', name: 'Second Wind', timing: 'Action' } });
  const h = await uiHarness({ preview: { canConfirm: true, diagnostics: [], units: [{ unitId: 'alice',
    gainedAbilities: [grant('boots-physical-2')],
    abilityPreviews: [{ ability: grant('boots-physical-2'), content: { name: 'Second Wind' }, remainingUses: 1 }],
    lostAbilityPreviews: [{ ability: grant('boots-physical-1'), content: { name: 'Second Wind' }, remainingUses: 0 }]
  }] } });
  const summaries = h.all().filter(node => node.tagName === 'p' && node.className === 'station-delta').map(node => node.textContent);
  assert.deepEqual(summaries, ['Gained: Second Wind (copy 2)', 'Lost: Second Wind (copy 1)']);
  assert.doesNotMatch(h.text(), /Second Wind \(copy [12]\) \(copy/);
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
