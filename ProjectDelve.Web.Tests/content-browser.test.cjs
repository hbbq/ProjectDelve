const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = name => fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot', name), 'utf8');
const plain = value => JSON.parse(JSON.stringify(value));

class Element {
  constructor(tag, document) {
    this.tagName = tag; this.ownerDocument = document; this.children = []; this.dataset = {}; this.attributes = {};
    this.listeners = {}; this.className = ''; this.hidden = false; this.value = '';
    this.classList = {
      add: name => { this.className = [...new Set([...this.className.split(' '), name])].filter(Boolean).join(' '); },
      remove: name => { this.className = this.className.split(' ').filter(value => value !== name).join(' '); },
      toggle: (name, on) => { const values = new Set(this.className.split(' ').filter(Boolean)); if (on) values.add(name); else values.delete(name); this.className = [...values].join(' '); },
      contains: name => this.className.split(' ').includes(name)
    };
  }
  set textContent(value) { this.text = String(value); this.children = []; }
  get textContent() { return (this.text ?? '') + this.children.map(child => child.textContent).join(''); }
  set innerHTML(_) { throw new Error('The viewer must not inject authored HTML.'); }
  append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); } }
  replaceChildren(...children) { this.text = ''; this.children = []; this.append(...children); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  addEventListener(name, action) { this.listeners[name] = action; }
  all(predicate) { return this.children.flatMap(child => [...(predicate(child) ? [child] : []), ...child.all(predicate)]); }
  querySelector(selector) {
    if (selector === 'h3') return this.all(child => child.tagName === 'h3')[0];
    if (selector === '[aria-current="true"]') return this.all(child => child.attributes['aria-current'] === 'true')[0];
    throw new Error(`Unexpected selector: ${selector}`);
  }
  focus() { this.ownerDocument.activeElement = this; }
  scrollIntoView() { this.scrolled = true; }
}

function harness(fetcher) {
  const calls = [], events = {}, document = { activeElement: null };
  document.createElement = tag => new Element(tag, document);
  const root = document.createElement('main');
  const context = vm.createContext({ document, structuredClone, addEventListener: (name, listener) => { events[name] = listener; }, fetch: async (url, options) => {
    calls.push({ url, options }); return fetcher ? fetcher(url, options) : { ok: true, json: async () => structuredClone(catalog) };
  } });
  vm.runInContext(source('content-browser-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-editor-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-editor.js').replace(/^import .*\r?\n/gm, '').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-browser.js').replace(/^import .*\r?\n/gm, '').replace(/^export /gm, ''), context);
  context.root = root;
  const viewer = vm.runInContext('mountContentBrowser(root)', context);
  const byId = id => root.all(element => element.id === id)[0];
  const byClass = name => root.all(element => element.classList.contains(name));
  const click = element => {
    if (element.tagName === 'summary' && element.parentElement?.tagName === 'details')
      element.parentElement.open = !element.parentElement.open;
    return element.listeners.click?.();
  };
  const change = (id, value, event = 'change') => { const control = byId(id); control.value = value; return control.listeners[event](); };
  return { context, viewer, root, document, calls, events, byId, byClass, click, change, run: code => vm.runInContext(code, context) };
}

const heal = {
  id: 'mend', name: 'Mending', family: 'Ability', parameters: [{ name: 'amount', kind: 'Integer', required: true, minimum: 1 }],
  profiles: [{ source: 'Unit', timing: 'Action', usage: 'LimitedOrUnlimited' }, { source: 'Equipment', timing: 'Action', usage: 'UnlimitedOnly' }],
  context: 'ActingUnit', targeting: { selection: 'Unit', recipients: 'Choose an adjacent friendly Unit.' },
  dependencies: ['posture', 'health'], description: { template: 'Restore {amount} HP.', parameters: ['amount'], allowsOverride: true }, grantable: true
};
const catalog = {
  context: { kind: 'canonical', name: 'Canonical content' },
  categories: [{ id: 'units', name: 'Units' }, { id: 'equipment', name: 'Equipment' }, { id: 'mechanics', name: 'Mechanics' }], diagnostics: [],
  entries: [
    { key: 'units:weaver', family: 'units', id: 'weaver', name: 'Sky Weaver', origin: 'canonical', summary: 'A quiet protector.',
      sections: [{ title: 'Stats', fields: [{ label: 'MOV', value: 3 }, { label: 'Storage', value: { capacities: [{ type: { id: 'dew', name: 'Dew' }, capacity: 2 }] } }] }],
      abilities: [{ key: 'stitch', name: 'Stitch the Air', timing: 'Action', usage: { maxUses: 2 }, mechanicId: 'mend', parameters: { amount: 2 }, description: 'Restore 2 HP.', ownerKey: 'units:weaver' }],
      references: [{ label: 'Produces', family: 'resources', id: 'absent', targetKey: 'resources:absent', exists: false }],
      definition: { id: 'weaver', localAbilities: [{ key: 'stitch', mechanic: { kind: 'mend', amount: 2 } }] } },
    { key: 'equipment:chime', family: 'equipment', id: 'chime', name: 'Copper Chime', origin: 'canonical', summary: 'A restorative sound.', sections: [],
      abilities: [{ key: 'stitch', name: 'Evening Song', timing: 'Action', usage: { maxUses: null }, mechanicId: 'mend', parameters: { amount: 4 }, description: 'Restore 4 HP.', ownerKey: 'equipment:chime' }],
      references: [{ label: 'Mechanic', family: 'mechanics', id: 'mend', targetKey: 'mechanics:mend', exists: true }], definition: { id: 'chime' } },
    { key: 'mechanics:mend', family: 'mechanics', id: 'mend', name: 'Mending', origin: 'canonical', summary: 'Restore health.', sections: [], abilities: [], references: [], definition: heal, mechanic: heal }
  ]
};

test('search, category and origin counts use actual data and reference navigation restores filters', () => {
  const h = harness(); h.context.data = structuredClone(catalog);
  h.context.data.entries.push({ key: 'units:local', id: 'local', family: 'units', name: 'Sky Patient', origin: 'scenario-local' });
  h.run('globalThis.navigation = new ContentBrowserState(); navigation.load(data); navigation.filter({query:"sky",origin:"scenario-local"})');
  assert.deepEqual(plain(h.run('navigation.visible.map(e => e.id)')), ['local']);
  assert.deepEqual(plain(h.run('navigation.categories.map(c => [c.id,c.count])')), [['units',1],['equipment',0],['mechanics',0]]);
  h.run('navigation.filter({query:"chime",origin:"all",category:"equipment"}); navigation.open("equipment:chime"); navigation.open("mechanics:mend",true)');
  assert.equal(h.run('navigation.selected.id'), 'mend');
  assert.equal(h.run('navigation.category'), 'all');
  h.run('navigation.back()');
  assert.equal(h.run('navigation.selected.id'), 'chime');
  assert.equal(h.run('navigation.query'), 'chime');
  assert.equal(h.run('navigation.category'), 'equipment');
  assert.equal(h.run('navigation.open("missing",true)'), false);
  assert.equal(h.run('navigation.selected.id'), 'chime');
});

test('mount is lazy and canonical display keeps owned identities, text and nested values readable', async () => {
  const h = harness(); assert.equal(h.calls.length, 0);
  await h.viewer.show(); await h.viewer.show();
  assert.deepEqual(h.calls.map(call => call.url), ['/api/content/catalog']);
  const detail = h.byId('content-detail');
  assert.match(detail.textContent, /Sky Weaver/);
  assert.match(detail.textContent, /Stitch the AirAction2 \/ gameLocal key · stitchRestore 2 HP/);
  assert.match(detail.textContent, /CapacitiesTypeIddewNameDewCapacity2/);
  assert.equal(h.byClass('cb-ability')[0].dataset.mechanicId, 'mend');
  assert.match(detail.textContent, /Owned by Sky Weaver/);
  assert.ok(h.byClass('cb-missing').some(element => element.textContent.includes('absent')));
  assert.equal(h.byClass('cb-advanced')[0].open, undefined);
  assert.equal(h.byId('content-list').children[0].attributes['aria-current'], 'true');
  assert.equal(h.calls.some(call => /game|gambit|observation/.test(call.url)), false);
});

test('complex fields use the full row and populated groups can collapse and reopen without losing content', async () => {
  const data = structuredClone(catalog);
  data.entries[0].sections.push({ title: 'Empty definition values', fields: [] });
  data.entries[0].sections[0].fields.push({ label: 'Materials', value: ['wood', 'stone'] }, { label: 'No materials', value: [] });
  const h = harness(async () => ({ ok: true, json: async () => data })); await h.viewer.show();
  const groups = h.byClass('cb-content-group');
  assert.equal(groups.length, 1);
  const group = groups[0], summary = group.children[0];
  assert.equal(group.tagName, 'details'); assert.equal(summary.tagName, 'summary');
  assert.equal(summary.textContent, 'Stats'); assert.equal(group.open, true);
  const rows = group.all(element => element.classList.contains('cb-field'));
  assert.deepEqual(rows.filter(row => row.classList.contains('cb-field-complex')).map(row => row.children[0].textContent), ['Storage', 'Materials']);
  assert.ok(rows.find(row => row.children[0].textContent === 'MOV'));
  h.click(summary); assert.equal(group.open, false);
  h.click(summary); assert.equal(group.open, true);
  assert.match(group.textContent, /CapacitiesTypeIddewNameDewCapacity2Materialswoodstone/);
  assert.equal(h.calls.length, 1);
  assert.equal(h.byClass('cb-ability').length, 1); // Primary Ability text remains expanded independently.
});

test('reference links navigate to real mechanics, retain support rows and focus the heading, then return', async () => {
  const h = harness(); await h.viewer.showCanonical();
  h.change('content-search', 'chime', 'input');
  h.click(h.byId('content-list').children[0]);
  const link = h.byClass('cb-reference').find(element => element.dataset.targetId === 'mend'); h.click(link);
  assert.equal(h.document.activeElement.tagName, 'h3');
  assert.equal(h.document.activeElement.textContent, 'Mending');
  assert.equal(h.byClass('cb-profile').length, 2);
  assert.match(h.byId('content-detail').textContent, /UnitActionLimited Or UnlimitedEquipmentActionUnlimited Only/);
  assert.match(h.byId('content-detail').textContent, /Fixed Engine behavior.*Only the listed authored parameters are configurable.*Choose an adjacent friendly Unit/s);
  assert.equal(h.byClass('cb-parameter').length, 1); // Target recipients are not invented configurable parameters.
  assert.equal(h.byClass('cb-wording-policies')[0].open, undefined);
  h.click(h.byClass('cb-text-button').find(element => element.textContent === '← Previous definition'));
  assert.equal(h.byId('content-search').value, 'chime');
  assert.match(h.byId('content-detail').textContent, /Copper Chime.*Restore 4 HP/s);
  h.click(h.byClass('cb-back-list')[0]);
  assert.equal(h.root.classList.contains('cb-show-detail'), false);
  assert.equal(h.document.activeElement.attributes['aria-current'], 'true');
});

test('recursive mechanic schema preserves defaults, enum choices, list bounds and object members', async () => {
  const data = structuredClone(catalog);
  const mechanic = data.entries[2].mechanic;
  mechanic.parameters = [{ name: 'rewards', kind: 'List', required: false, allowNull: true, minimumItems: 1, maximumItems: 6,
    defaultValue: [], item: { name: 'reward', kind: 'Object', required: true, members: [
      { name: 'direction', kind: 'Enum', required: true, allowedValues: ['North','South'], defaultValue: 'North' },
      { name: 'resource', kind: 'Reference', required: true, referenceCatalog: 'resources' },
      { name: 'amount', kind: 'Integer', required: true, minimum: 0, maximum: 5, defaultValue: 0 }
    ] } }];
  const h = harness(async () => ({ ok: true, json: async () => data })); await h.viewer.show(); h.viewer.open('mechanics:mend');
  const rendered = h.byId('content-detail').textContent;
  for (const token of ['rewards', 'Optional', 'Null allowedYes', 'Minimum items1', 'Maximum items6', 'Each item', 'Object fields', 'North', 'South', 'Reference catalogResources', 'Minimum0', 'Maximum5', 'Default0']) assert.ok(rendered.includes(token), token);
  assert.equal(h.byClass('cb-parameter').length, 5);
});

test('Designer inspection snapshots are explicit, isolated, and retained only in memory across context switches', async () => {
  const draft = structuredClone(catalog); draft.entries[0].origin = 'scenario-local'; draft.entries[0].availability = 'declared'; draft.entries[0].name = '<img src=x onerror=alert(1)>';
  const h = harness(async url => ({ ok: true, json: async () => structuredClone(url.endsWith('inspect') ? draft : catalog) }));
  const input = { unitTypes: [{ id: 'new', localAbilities: [{ key: 'owned', name: 'Quiet' }] }] };
  const loading = h.viewer.showScenario(input, 'Lantern draft'); input.unitTypes[0].localAbilities[0].name = 'Changed later';
  await loading;
  assert.equal(h.calls[0].url, '/api/content/inspect'); assert.equal(h.calls[0].options.method, 'POST');
  assert.equal(JSON.parse(h.calls[0].options.body).unitTypes[0].localAbilities[0].name, 'Quiet');
  assert.ok(h.byId('content-detail').textContent.includes('<img src=x onerror=alert(1)>'));
  assert.equal(h.root.all(element => element.tagName === 'img').length, 0);
  assert.equal(h.byId('content-context').value, 'draft');
  assert.match(h.byId('content-detail').textContent, /Declared/);
  assert.match(h.root.textContent, /Effective definitions for this Designer snapshot.*Local content stays scoped to this draft/);
  await h.viewer.showCanonical(); await h.change('content-context', 'draft');
  assert.equal(h.calls.length, 2);
  assert.equal(h.byId('content-context').value, 'draft');
  assert.match(h.root.textContent, /Lantern draft/);
});

test('late responses cannot replace a newer context and show does not race a pending inspection', async () => {
  let resolveCanonical;
  const h = harness(url => url.endsWith('catalog') ? new Promise(resolve => { resolveCanonical = resolve; })
    : Promise.resolve({ ok: true, json: async () => ({ ...catalog, entries: [{ key:'items:local',id:'local',name:'Draft Letter',family:'items',origin:'scenario-local',definition:{id:'local'} }] }) }));
  const canonicalRequest = h.viewer.showCanonical();
  const draftRequest = h.viewer.showScenario({ unitTypes: [] }, 'Explicit snapshot'); const showing = h.viewer.show();
  await draftRequest; await showing;
  resolveCanonical({ ok: true, json: async () => catalog }); await canonicalRequest;
  assert.equal(h.calls.length, 2);
  assert.match(h.byId('content-detail').textContent, /Draft Letter/);
  assert.equal(h.byId('content-context').value, 'draft');
});

test('invalid draft clears previous content and presents field diagnostics without fallback or stale draft reuse', async () => {
  let invalid = false;
  const h = harness(async () => invalid
    ? { ok: false, json: async () => ({ context:{kind:'scenario'},diagnostics:[{ path:'unitTypes[0].localAbilities[0].key', code:'required', message:'An explicit local key is required.' }] }) }
    : { ok: true, json: async () => structuredClone(catalog) });
  await h.viewer.showScenario({ unitTypes: [] }, 'Good draft'); invalid = true;
  await h.viewer.showScenario({ unitTypes: [{}] }, 'Broken draft');
  assert.equal(h.byId('content-list').children.length, 1); // Empty-state message, no definitions.
  assert.ok(!h.byId('content-detail').textContent.includes('Sky Weaver'));
  assert.match(h.root.textContent, /unitTypes\[0\]\.localAbilities\[0\]\.key.*An explicit local key is required/s);
  assert.equal(h.byClass('cb-diagnostics')[0].open, true);
  assert.equal(h.calls.length, 2);
  invalid = false; await h.viewer.showCanonical();
  assert.equal(h.byId('content-context').children.length, 1);
});

test('filtering preserves search focus and does not scroll the detail on every keystroke', async () => {
  const h = harness(); await h.viewer.show();
  h.byId('content-search').focus();
  h.change('content-search', 'missing', 'input');
  assert.equal(h.document.activeElement, h.byId('content-search'));
  assert.equal(h.byId('content-detail').scrolled, undefined);
  assert.match(h.root.textContent, /0 of 3 definitions.*No definitions match/s);
  h.click(h.byClass('cb-text-button').find(element => element.textContent === 'Clear filters'));
  assert.equal(h.byId('content-list').children.length, 3);
});

const settle = () => new Promise(resolve => setImmediate(resolve));
function authoringHarness() {
  let definition = { equipmentTypes: [], itemTypes: [], lootCardDefinitions: [] }, revision = 1;
  const saves = [];
  const destination = { label: 'Test destination', getRevision: () => String(revision), getContext: () => structuredClone(definition),
    getSnapshot: () => ({ definition: structuredClone(definition), name: 'Test draft' }),
    save: async payload => {
      if (payload.expectedRevision !== String(revision)) throw new Error('The draft changed. Reopen the definition.');
      saves.push(plain(payload)); const field = payload.family === 'equipment' ? 'equipmentTypes' : 'lootCardDefinitions';
      definition[field] = definition[field].filter(row => row.id !== payload.existingId).concat([structuredClone(payload.definition)]); revision++;
      return destination.getSnapshot();
    } };
  const asEntry = (row, family) => ({ key: `${family}:${row.id}`, id: row.id, name: row.name, family, origin: 'local', definition: row, sections: [], abilities: [], references: [] });
  const h = harness(async (url, options) => {
    if (url.endsWith('/validate')) {
      const request = JSON.parse(options.body); return { ok: true, json: async () => ({ valid: true, diagnostics: [], definition: request.definition, entry: asEntry(request.definition, request.family) }) };
    }
    const data = structuredClone(catalog);
    data.entries[1].definition = { id: 'chime', name: 'Copper Chime', localAbilities: [{ key: 'stitch', name: 'Evening Song', timing: 'Action', usage: { maxUses: null }, mechanic: { kind: 'mend', amount: 4 } }] };
    if (url.endsWith('/context')) data.entries = data.entries.filter(entry => ['equipment', 'mechanics'].includes(entry.family))
      .concat(definition.equipmentTypes.map(row => asEntry(row, 'equipment')), definition.lootCardDefinitions.map(row => asEntry(row, 'lootCards')));
    return { ok: true, json: async () => data };
  });
  return { ...h, destination, saves, snapshot: () => destination.getSnapshot(), changeDraft: rows => { definition = { ...definition, ...rows }; revision++; } };
}

test('standalone browsing has no implicit save destination; explicit draft context enables create and linked Loot', async () => {
  const h = authoringHarness(); await h.viewer.showCanonical();
  assert.equal(h.byId('content-create-equipment').disabled, true);
  h.viewer.open('equipment:chime'); assert.equal(h.byId('content-copy-definition').disabled, true);
  const snapshot = h.snapshot(); await h.viewer.showScenario(snapshot.definition, snapshot.name, h.destination);
  assert.equal(h.calls.at(-1).url, '/api/content/context');
  assert.equal(h.byId('content-create-equipment').disabled, false);
  assert.ok(h.byId('content-context').children.some(option => option.value === 'scenario'));
  h.click(h.byId('content-create-equipment')); await settle();
  h.change('content-editor-id', 'linen-sash', 'input'); h.change('content-editor-name', 'Linen Sash', 'input');
  h.click(h.byId('content-editor-add-ability')); h.click(h.byId('content-editor-save')); await settle();
  assert.equal(h.saves.length, 1); assert.equal(h.saves[0].expectedRevision, '1');
  assert.equal(h.saves[0].definition.localAbilities[0].mechanic.kind, 'mend');
  assert.match(h.byId('content-detail').textContent, /Linen Sash/); assert.equal(h.byId('content-editor').hidden, true);
  h.click(h.byId('content-create-linked-loot')); await settle();
  const reference = h.root.all(element => element.dataset.fieldPath === 'definition.equipmentTypeId')[0];
  assert.equal(reference.value, 'linen-sash');
  h.change('content-editor-id', 'linen-package', 'input'); h.click(h.byId('content-editor-save')); await settle();
  assert.deepEqual(h.saves[1].definition, { id: 'linen-package', name: 'Linen Sash Loot', equipmentTypeId: 'linen-sash' });
  assert.equal(h.calls.some(call => /game|gambit|observation/.test(call.url)), false);
});

test('copying canonical content stays new; context navigation and beforeunload guard dirty forms', async () => {
  const h = authoringHarness(), snapshot = h.snapshot(); await h.viewer.showScenario(snapshot.definition, snapshot.name, h.destination);
  h.viewer.open('equipment:chime'); h.click(h.byId('content-copy-definition')); await settle();
  assert.equal(h.byId('content-editor-id').value, ''); assert.equal(h.byId('content-editor-id').readOnly, false);
  const key = h.root.all(element => element.dataset.fieldPath === 'definition.localAbilities[0].key')[0];
  assert.equal(key.value, 'stitch'); assert.equal(key.readOnly, false);
  h.change('content-editor-id', 'copy-chime', 'input');
  const event = { prevented: false, preventDefault() { this.prevented = true; } }; h.events.beforeunload(event); assert.equal(event.prevented, true);
  h.change('content-context', 'canonical'); await settle(); h.click(h.byId('content-editor-keep')); await settle();
  assert.equal(h.byId('content-context').value, 'draft'); assert.equal(h.byId('content-editor').hidden, false);
  h.change('content-context', 'scenario'); await settle(); h.click(h.byId('content-editor-discard')); await settle();
  assert.equal(h.byId('content-context').value, 'scenario'); assert.equal(h.calls.at(-1).url, '/api/content/inspect');
  assert.equal(h.saves.length, 0);
});

test('cached draft refreshes after external edits and save carries the opening revision to reject later changes', async () => {
  const h = authoringHarness(), snapshot = h.snapshot(); await h.viewer.showScenario(snapshot.definition, snapshot.name, h.destination);
  h.changeDraft({ equipmentTypes: [{ id: 'local-sash', name: 'Current Sash', localAbilities: [] }] });
  await h.viewer.show(); h.viewer.open('equipment:local-sash');
  assert.match(h.byId('content-detail').textContent, /Current Sash/);
  h.click(h.byId('content-edit-definition')); await settle();
  assert.equal(h.byId('content-editor-id').readOnly, true);
  h.change('content-editor-name', 'Pending rename', 'input');
  h.changeDraft({ equipmentTypes: [{ id: 'local-sash', name: 'Newer Sash', localAbilities: [] }] });
  h.click(h.byId('content-editor-save')); await settle();
  assert.equal(h.saves.length, 0); assert.match(h.byId('content-editor').textContent, /draft changed/);
  assert.equal(h.byId('content-editor').hidden, false);
});

test('creation waits for current catalog metadata and ignores an older context response', async () => {
  let finishFirst, contextRequests = 0;
  const latest = structuredClone(catalog); latest.entries[1].name = 'Current Chime';
  const h = harness(url => {
    assert.equal(url, '/api/content/context'); contextRequests++;
    return contextRequests === 1 ? new Promise(resolve => { finishFirst = resolve; }) : Promise.resolve({ ok: true, json: async () => latest });
  });
  const destination = { label: 'Current draft', getContext: () => ({}), getRevision: () => '1', getSnapshot: () => ({ definition: {}, name: 'Current draft' }) };
  const first = h.viewer.showScenario({}, 'First', destination);
  assert.equal(h.byId('content-create-equipment').disabled, true);
  assert.equal(h.byId('content-create-loot').disabled, true);
  h.click(h.byId('content-create-equipment')); await settle();
  assert.equal(h.byId('content-editor').hidden, true);
  await h.viewer.showScenario({}, 'Current draft', destination);
  assert.equal(h.byId('content-create-equipment').disabled, false);
  h.viewer.open('equipment:chime');
  finishFirst({ ok: true, json: async () => catalog }); await first;
  assert.match(h.byId('content-detail').textContent, /Current Chime/);
  h.click(h.byId('content-create-equipment')); await settle();
  assert.equal(h.byId('content-editor-add-mechanic').children[0].value, 'mend');
  assert.equal(h.byId('content-editor-add-ability').disabled, false);
});
