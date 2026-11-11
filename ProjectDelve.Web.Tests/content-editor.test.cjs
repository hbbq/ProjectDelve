const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const plain = value => JSON.parse(JSON.stringify(value));

class Element {
  constructor(tag, ownerDocument) {
    this.tagName = tag; this.ownerDocument = ownerDocument; this.children = []; this.dataset = {}; this.attributes = {};
    this.listeners = {}; this.className = ''; this.value = ''; this.hidden = false;
    this.classList = { add: value => { this.className += ` ${value}`; }, contains: value => this.className.split(' ').includes(value) };
  }
  append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); } }
  replaceChildren(...children) { this.children = []; this.text = ''; this.append(...children); }
  set textContent(value) { this.text = String(value); this.children = []; }
  get textContent() { return (this.text ?? '') + this.children.map(child => child.textContent).join(''); }
  set innerHTML(_) { throw new Error('Authored values must be text.'); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  addEventListener(name, listener) { this.listeners[name] = listener; }
  all(predicate) { return this.children.flatMap(child => [...(predicate(child) ? [child] : []), ...child.all(predicate)]); }
  focus() { this.ownerDocument.activeElement = this; }
}

const shared = {
  id: 'testVector', name: 'Vector Tuning', family: 'Ability', maxPerSource: 1,
  profiles: [{ source: 'Equipment', timing: 'Passive', usage: 'UnlimitedOnly' }, { source: 'Equipment', timing: 'BonusAction', usage: 'UnlimitedOnly' }],
  parameters: [{ name: 'adjustments', kind: 'List', required: true, minimumItems: 1, maximumItems: 3,
    item: { kind: 'Object', required: true, members: [
      { name: 'stat', kind: 'Enum', required: true, allowedValues: ['Mov', 'Def'] },
      { name: 'amount', kind: 'Integer', required: true, minimum: -7, maximum: 9, defaultValue: 0 },
      { name: 'enabled', kind: 'Boolean', required: false, defaultValue: false }
    ] } },
    { name: 'settings', kind: 'Object', required: false, allowNull: true, members: [{ name: 'resource', kind: 'Reference', required: true, referenceCatalog: 'resources' }] }],
  description: { allowsOverride: true, parameters: ['adjustments'], allowedOverridePlaceholders: ['adjustments'] },
  context: 'BearingUnit', targeting: { selection: 'None', recipients: 'The bearer only.' }
};
const action = { id: 'testAction', name: 'Quiet Chord', family: 'Ability', profiles: [{ source: 'Equipment', timing: 'Action', usage: 'UnlimitedOnly' }], parameters: [{ name: 'amount', kind: 'Integer', required: true, minimum: 1 }], description: {} };
const immediate = { id: 'testImmediate', name: 'Collector Gift', family: 'ImmediateLoot', profiles: [{ source: 'LootCard', timing: 'LootReveal', usage: 'NotApplicable' }], parameters: [{ name: 'amount', kind: 'Integer', required: true, minimum: 1, maximum: 100 }] };
const catalog = { entries: [shared, action, immediate, { ...action, id: 'unitOnly', profiles: [{ source: 'Unit', timing: 'Action', usage: 'UnlimitedOnly' }] }].map(mechanic => ({ family: 'mechanics', id: mechanic.id, mechanic })).concat([
  { family: 'resources', id: 'dew', name: 'Dew' }, { family: 'equipment', id: 'cloak', name: 'Woven Cloak' }, { family: 'items', id: 'tea', name: 'Quiet Tea' }
]) };

// Test-owned catalog: unfamiliar IDs ensure the form is driven by metadata.
const costProfiles = ['Unit', 'Equipment', 'Item'].flatMap(source => ['Action', 'BonusAction', 'FreeAction'].map(timing =>
  ({ source, timing, usage: source === 'Item' ? 'UnlimitedOnly' : 'LimitedOrUnlimited' })));
const costMechanic = { ...action, id: 'costOperation', name: 'Cost Operation', profiles: [
  ...costProfiles, { source: 'Equipment', timing: 'Passive', usage: 'UnlimitedOnly' }
], costProfiles };
const costCatalog = { ...catalog, costEditingAvailable: true, unitAuthoring: { stats: [], fields: [], behaviors: [] },
  entries: [...catalog.entries, { family: 'mechanics', id: costMechanic.id, mechanic: costMechanic },
    { family: 'mechanics', id: 'intrinsic', mechanic: { ...costMechanic, id: 'intrinsic', costProfiles: [], profiles: [{ source: 'Equipment', timing: 'AfterAttack', usage: 'UnlimitedOnly' }] } }],
  costs: [
    { id: 'paperPayment', name: 'Paper Payment', executionAvailable: true, profiles: costProfiles, parameters: [] },
    { id: 'coinPayment', name: 'Coin Payment', executionAvailable: true, profiles: costProfiles, descriptionTemplate: 'NEVER EVALUATE {amount}', parameters: [
      { name: 'resourceTypeId', kind: 'Reference', required: true, referenceCatalog: 'resources' },
      { name: 'amount', kind: 'Integer', required: true, minimum: 2, maximum: 9 },
      { name: 'note', kind: 'String', required: false, allowNull: true }
    ] },
    { id: 'unitPayment', name: 'Unit Payment', executionAvailable: true, profiles: costProfiles.filter(profile => profile.source === 'Unit'), parameters: [] },
    { id: 'futurePayment', name: 'Future Payment', executionAvailable: false, profiles: costProfiles, parameters: [] }
  ]
};
const costHarness = options => harness({ ...options, callbacks: { catalog: costCatalog, ...options?.callbacks } });
const addCost = (h, id, ability = 0) => {
  h.root.all(element => element.dataset.costChoice === String(ability))[0].value = id;
  h.root.all(element => element.dataset.addCost === String(ability))[0].listeners.click();
};
const clickLabel = (h, label) => h.root.all(element => element.attributes['aria-label'] === label)[0].listeners.click();

function harness(options = {}) {
  const document = { createElement: tag => new Element(tag, document) }, root = document.createElement('section');
  const validated = [], saved = [], environment = vm.createContext({ structuredClone });
  for (const file of ['content-browser-state.js', 'content-editor-state.js', 'content-editor.js'])
    vm.runInContext(fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot', file), 'utf8').replace(/^import .*\r?\n/gm, '').replace(/^export /gm, ''), environment);
  environment.root = root;
  environment.options = { catalog, validate: async request => { validated.push(plain(request)); return options.validation ? options.validation(request) : { valid: true, diagnostics: [], definition: request.definition, entry: { name: request.definition.name, id: request.definition.id, summary: 'Engine-derived wording.', abilities: [{ key: 'stable', name: 'Engine ability', timing: 'Action', usage: { maxUses: null }, description: 'Authoritative generated rule.' }] } }; },
    save: async request => { saved.push(plain(request)); if (options.saving) return options.saving(request); }, ...options.callbacks };
  const editor = vm.runInContext('mountContentEditor(root, options)', environment);
  const byId = id => root.all(element => element.id === id)[0];
  const field = fieldPath => root.all(element => element.dataset.fieldPath === fieldPath).at(-1);
  const change = (fieldPath, value, event = 'input') => { const element = field(fieldPath); assert.ok(element, fieldPath); element.value = value; return element.listeners[event](); };
  const clickText = text => { const element = root.all(element => element.tagName === 'button' && element.textContent === text)[0]; assert.ok(element, text); return element.listeners.click(); };
  return { root, document, editor, validated, saved, byId, field, change, clickText, run: text => vm.runInContext(text, environment), environment };
}

test('metadata selects supported families and profiles without ID-specific forms, retaining actual defaults and bounds', () => {
  const h = harness(); h.environment.catalog = catalog;
  h.run('globalThis.state = new ContentEditorState(catalog); state.open({family:"equipment"}); state.addAbility("testVector")');
  assert.deepEqual(plain(h.run('state.supportedMechanics().map(m => m.id)')), ['testVector', 'testAction']);
  assert.deepEqual(plain(h.run('state.definition.localAbilities[0].mechanic')), { kind: 'testVector', adjustments: [{ stat: 'Mov', amount: 0, enabled: false }] });
  assert.equal(h.run('state.canAdd(state.mechanic("testVector"))'), false);
  assert.equal(h.run('state.addAbility("unitOnly")'), false);
  h.run('state.changeTiming(0,"BonusAction")');
  assert.equal(h.run('state.definition.localAbilities[0].timing'), 'BonusAction');
  assert.equal(h.run('state.changeTiming(0,"AfterAttack")'), false);
  assert.deepEqual(plain(h.run('state.supportedMechanics("lootCards").map(m => m.id)')), ['testImmediate']);
  assert.equal(h.run('parameterValue({kind:"Integer",minimum:-100,maximum:100})'), 0);
  assert.equal(h.run('parameterValue({kind:"Integer",minimum:-100,maximum:-3})'), -3);
  assert.equal(h.run('parameterValue({kind:"Integer",minimum:1})'), 1);
});

test('Swap relationship and Action or Bonus Action are editable through the ordinary metadata form', async () => {
  const swap = { id: 'swap', name: 'Adjacent Unit Position Swap', family: 'Ability', maxPerSource: null,
    profiles: ['Action', 'BonusAction'].map(timing => ({ source: 'Equipment', timing, usage: 'LimitedOrUnlimited' })),
    parameters: [{ name: 'selector', kind: 'Enum', required: false, defaultValue: 'Enemies', allowedValues: ['Friendlies', 'Enemies'] }],
    description: { parameters: ['selector'] } };
  const h = harness({ callbacks: { catalog: { ...catalog, entries: [...catalog.entries, { family: 'mechanics', id: 'swap', mechanic: swap }] } } });
  await h.editor.open({ family: 'equipment' });
  h.change('definition.id', 'swap-kit'); h.change('definition.name', 'Swap kit');
  h.byId('content-editor-add-mechanic').value = 'swap'; h.clickText('Add Ability');
  assert.equal(h.field('definition.localAbilities[0].mechanic.selector').value, 'Enemies');
  assert.deepEqual(h.field('definition.localAbilities[0].mechanic.selector').children.map(e => e.value), ['Friendlies', 'Enemies']);
  h.change('definition.localAbilities[0].mechanic.selector', 'Friendlies', 'change');
  h.change('definition.localAbilities[0].timing', 'BonusAction', 'change');
  await h.editor.validate();
  assert.deepEqual(h.validated[0].definition.localAbilities[0].mechanic, { kind: 'swap', selector: 'Friendlies' });
  assert.equal(h.validated[0].definition.localAbilities[0].timing, 'BonusAction');
  h.change('definition.localAbilities[0].timing', 'Action', 'change');
  h.change('definition.localAbilities[0].mechanic.selector', 'Enemies', 'change');
  await h.editor.validate();
  assert.deepEqual(h.validated[1].definition.localAbilities[0].mechanic, { kind: 'swap', selector: 'Enemies' });
  assert.equal(h.validated[1].definition.localAbilities[0].timing, 'Action');
});

test('new and copied definitions retain local keys through rename; adding after removal does not recycle a key', () => {
  const h = harness(); h.environment.catalog = catalog;
  h.run('globalThis.state = new ContentEditorState(catalog); state.open({family:"equipment"}); state.addAbility("testAction"); state.set(["localAbilities",0,"name"],"Renamed"); state.addAbility("testAction"); state.removeAbility(0); state.addAbility("testAction")');
  assert.deepEqual(plain(h.run('state.definition.localAbilities.map(a => a.key)')), ['ability-2', 'ability-3']);
  const definition = plain(h.run('state.definition')); definition.id = 'original'; h.environment.definition = definition;
  h.run('state.open({family:"equipment",definition,existingId:"original",copy:true})');
  assert.equal(h.run('state.definition.id'), ''); assert.equal(h.run('state.existingId'), undefined);
  assert.deepEqual(plain(h.run('state.definition.localAbilities.map(a => a.key)')), ['ability-2', 'ability-3']);
  assert.equal(definition.id, 'original');
});

test('Equipment forms edit nested lists, defaults, enums, booleans, nullable objects and searchable typed references', async () => {
  const h = harness(); await h.editor.open({ family: 'equipment' });
  h.change('definition.id', 'new-cloak'); h.change('definition.name', 'New Cloak');
  h.byId('content-editor-add-mechanic').value = 'testVector'; h.clickText('Add Ability');
  assert.equal(h.field('definition.localAbilities[0].key').readOnly, false);
  h.change('definition.localAbilities[0].key', 'steady-vector');
  assert.equal(h.field('definition.localAbilities[0].mechanic.adjustments[0].amount').value, 0);
  assert.equal(h.field('definition.localAbilities[0].mechanic.adjustments[0].amount').min, -7);
  h.change('definition.localAbilities[0].mechanic.adjustments[0].stat', 'Def', 'change');
  h.change('definition.localAbilities[0].mechanic.adjustments[0].amount', '3');
  h.change('definition.localAbilities[0].mechanic.adjustments[0].enabled', 'true', 'change');
  h.clickText('Add entry');
  h.change('definition.localAbilities[0].mechanic.settings', 'null', 'change');
  await h.editor.validate();
  assert.equal(h.validated[0].definition.localAbilities[0].mechanic.settings, null);
  h.change('definition.localAbilities[0].mechanic.settings', 'value', 'change');
  h.change('definition.localAbilities[0].mechanic.settings.resource', 'dew', 'change');
  await h.editor.validate();
  const ability = h.validated[1].definition.localAbilities[0];
  assert.equal(ability.key, 'steady-vector');
  assert.deepEqual(ability.mechanic.adjustments, [{ stat: 'Def', amount: 3, enabled: true }, { stat: 'Mov', amount: 0, enabled: false }]);
  assert.deepEqual(ability.mechanic.settings, { resource: 'dew' });
  assert.equal(ability.timing, 'Passive'); assert.equal(ability.usage.maxUses, null);
  assert.match(h.root.textContent, /Authoritative generated rule/);
  assert.ok(!h.root.textContent.includes('unitOnly'));
});

test('Loot reward switches remain exclusive and object references include local context without copying definitions', async () => {
  const h = harness(); await h.editor.open({ family: 'lootCards', context: { equipmentTypes: [{ id: 'local-sash', name: 'Local Sash' }] } });
  h.change('definition.id', 'gift'); h.change('definition.name', 'Wrapped Gift');
  h.change('definition.equipmentTypeId', 'local-sash', 'change'); await h.editor.validate();
  assert.equal(h.validated[0].definition.equipmentTypeId, 'local-sash');
  assert.deepEqual(h.validated[0].context.equipmentTypes, [{ id: 'local-sash', name: 'Local Sash' }]);
  const reward = h.byId('content-editor-reward-kind'); reward.value = 'immediate'; reward.listeners.change();
  h.change('definition.immediate.amount', '8'); await h.editor.validate();
  assert.deepEqual(h.validated[1].definition, { id: 'gift', name: 'Wrapped Gift', immediate: { kind: 'testImmediate', amount: 8 } });
  const next = h.byId('content-editor-reward-kind'); next.value = 'items'; next.listeners.change();
  h.change('definition.itemTypeId', 'tea', 'change'); await h.editor.validate();
  assert.deepEqual(h.validated[2].definition, { id: 'gift', name: 'Wrapped Gift', itemTypeId: 'tea' });
});

test('Engine field diagnostics block persistence and appear beside the affected control, including root diagnostics', async () => {
  const h = harness({ validation: async () => ({ valid: false, diagnostics: [{ path: 'definition.id', code: 'duplicate', message: 'This ID already exists.' }, { path: 'definition', code: 'invalid', message: 'Review the reward.' }] }) });
  await h.editor.open({ family: 'lootCards' }); h.change('definition.id', 'duplicate');
  assert.equal(await h.editor.save(), false); assert.equal(h.saved.length, 0);
  assert.equal(h.field('definition.id').attributes['aria-invalid'], 'true');
  assert.match(h.root.textContent, /This ID already exists/); assert.match(h.root.textContent, /Review the reward/);
  assert.equal(h.editor.isDirty(), true);
});

test('existing IDs stay fixed and save receives only the Engine-validated normalized definition', async () => {
  const h = harness({ validation: async request => ({ valid: true, diagnostics: [], definition: { ...request.definition, name: 'Normalized' }, entry: { name: 'Normalized', id: request.definition.id } }) });
  const original = { id: 'keep-id', name: 'Original', localAbilities: [] };
  await h.editor.open({ family: 'equipment', definition: original, existingId: 'keep-id', context: { equipmentTypes: [original] } });
  assert.equal(h.byId('content-editor-id').readOnly, true);
  h.change('definition.name', '<img src=x>'); assert.equal(await h.editor.save(), true);
  assert.equal(h.saved[0].definition.name, 'Normalized'); assert.equal(h.saved[0].existingId, 'keep-id');
  assert.equal(original.name, 'Original'); assert.equal(h.editor.isDirty(), false); assert.equal(h.root.hidden, true);
  assert.equal(h.root.all(element => element.tagName === 'img').length, 0);
});

test('cancel uses an inline discard decision and a failed save keeps the editable draft', async () => {
  const h = harness({ saving: async () => { throw new Error('The destination changed. Reopen the current draft.'); } });
  await h.editor.open({ family: 'equipment' }); h.change('definition.name', 'Unsaved');
  const stay = h.editor.requestClose();
  assert.equal(h.byId('content-editor-save').disabled, true); assert.equal(await h.editor.save(), false);
  h.byId('content-editor-keep').listeners.click(); assert.equal(await stay, false);
  assert.equal(h.byId('content-editor-save').disabled, false);
  assert.equal(h.editor.isOpen(), true); assert.equal(h.editor.isDirty(), true);
  assert.equal(await h.editor.save(), false); assert.match(h.root.textContent, /destination changed/);
  assert.equal(h.editor.isDirty(), true);
  const leave = h.editor.requestClose(); h.byId('content-editor-discard').listeners.click(); assert.equal(await leave, true);
  assert.equal(h.editor.isOpen(), false);
});

test('an obsolete validation result cannot preview or save values changed during the request', async () => {
  let finish;
  const h = harness({ validation: request => new Promise(resolve => { finish = () => resolve({ valid: true, diagnostics: [], definition: request.definition, entry: { name: 'Stale preview' } }); }) });
  await h.editor.open({ family: 'equipment' }); h.change('definition.name', 'Before');
  const saving = h.editor.save(); h.change('definition.name', 'After'); finish();
  assert.equal(await saving, false); assert.equal(h.saved.length, 0);
  assert.ok(!h.root.textContent.includes('Stale preview')); assert.match(h.root.textContent, /form changed/);
});

test('preview references resolve readable names and expand inspection without discarding the form', async () => {
  const h = harness({ validation: async request => ({ valid: true, diagnostics: [], definition: request.definition,
    entry: { id: 'parcel', name: 'Woven Parcel', references: [{ label: 'Equipment reward', family: 'equipment', id: 'cloak', targetKey: 'equipment:cloak', exists: true }] } }) });
  await h.editor.open({ family: 'lootCards' }); h.change('definition.id', 'parcel'); h.change('definition.equipmentTypeId', 'cloak', 'change');
  await h.editor.validate(); h.clickText('Inspect Woven Cloak · cloak');
  const expanded = h.root.all(element => element.classList.contains('ce-reference-content'))[0];
  assert.equal(expanded.hidden, false); assert.match(expanded.textContent, /Woven Cloakcloak/);
  assert.equal(h.editor.isDirty(), true); assert.equal(h.editor.isOpen(), true); assert.equal(h.saved.length, 0);
});

test('original keys stay fixed while new keys accept explicit authoring and show duplicate validation errors', async () => {
  const h = harness({ validation: async () => ({ valid: false, diagnostics: [{ path: 'definition.localAbilities[1].key', code: 'duplicateKey', message: 'Local keys must be unique.' }] }) });
  const original = { key: 'retained', name: 'Old Name', timing: 'Action', mechanic: { kind: 'testAction', amount: 2 } };
  await h.editor.open({ family: 'equipment', existingId: 'existing', definition: { id: 'existing', name: 'Existing', localAbilities: [original] } });
  assert.equal(h.field('definition.localAbilities[0].key').readOnly, true);
  h.change('definition.localAbilities[0].name', 'Renamed');
  h.byId('content-editor-add-mechanic').value = 'testAction'; h.clickText('Add Ability');
  assert.equal(h.field('definition.localAbilities[1].key').readOnly, false);
  h.change('definition.localAbilities[1].key', 'retained');
  assert.equal(await h.editor.save(), false);
  assert.equal(h.field('definition.localAbilities[1].key').attributes['aria-invalid'], 'true');
  assert.equal(h.validated[0].definition.localAbilities[0].key, 'retained');
  assert.equal(h.saved.length, 0);
});

test('unavailable existing mechanic data is preserved until an explicit mechanic change', async () => {
  const h = harness(); const definition = { id: 'unfamiliar', name: 'Unfamiliar', localAbilities: [{ key: 'old', name: 'Old', timing: 'AfterAttack', mechanic: { kind: 'unavailable', retainedParameter: 37 } }] };
  await h.editor.open({ family: 'equipment', definition, existingId: definition.id });
  await h.editor.validate(); assert.deepEqual(h.validated[0].definition, definition);
  assert.match(h.root.textContent, /mechanic is unavailable/);
});

test('Engine profiles expose free timing and physical Equipment uses without mechanic name rules', async () => {
  const mechanic = { ...action, id: 'unfamiliar-active', name: 'Unfamiliar Active',
    profiles: ['Action', 'BonusAction', 'FreeAction'].map(timing => ({ source: 'Equipment', timing, usage: 'LimitedOrUnlimited' })),
    contextRequirements: 'Requires an acting Unit.', unsupportedContextReason: 'This trigger requires a completed Attack.' };
  const h = harness({ callbacks: { catalog: { entries: [{ family: 'mechanics', id: mechanic.id, mechanic }] } } });
  await h.editor.open({ family: 'equipment' });
  h.byId('content-editor-add-mechanic').value = mechanic.id; h.clickText('Add Ability');
  h.change('definition.localAbilities[0].timing', 'FreeAction', 'change');
  h.change('definition.localAbilities[0].usage', 'limited', 'change');
  h.change('definition.localAbilities[0].usage.maxUses', '4');
  await h.editor.validate();
  assert.equal(h.validated[0].definition.localAbilities[0].timing, 'FreeAction');
  assert.equal(h.validated[0].definition.localAbilities[0].usage.maxUses, 4);
  assert.match(h.root.textContent, /Requires an acting Unit/);

  const second = harness({ callbacks: { catalog: { entries: [{ family: 'mechanics', id: mechanic.id, mechanic }] } } });
  await second.editor.open({ family: 'equipment', definition: { id: 'old', name: 'Old', localAbilities: [
    { key: 'old', name: 'Old Ability', timing: 'AfterAttack', mechanic: { kind: mechanic.id, amount: 1 } }
  ] } });
  assert.match(second.root.textContent, /This trigger requires a completed Attack/);
});

test('typed Unit reference picker merges scenario-local Units and scoped canonical names into the validation context', async () => {
  const mechanic = { id: 'adjacentUnitSummon', name: 'Summon', family: 'Ability',
    profiles: [{ source: 'Equipment', timing: 'Action', usage: 'LimitedOrUnlimited' }],
    parameters: [{ name: 'unitTypeId', kind: 'Reference', required: true, referenceCatalog: 'units' },
      { name: 'initialPosture', kind: 'Enum', required: true, allowedValues: ['Upright', 'Lying'] }], description: {} };
  const unitTypes = [{ id: 'grunt', displayName: 'Scoped Guard' }, { id: 'paper-unit', displayName: 'Paper Friend' }];
  const effective = { entries: [{ family: 'mechanics', id: mechanic.id, mechanic },
    { family: 'units', id: 'grunt', name: 'Canonical Guard' }, { family: 'units', id: 'worker', name: 'Worker' }] };
  const h = harness({ callbacks: { catalog: effective } });
  await h.editor.open({ family: 'equipment', context: { unitTypes } });
  h.byId('content-editor-add-mechanic').value = mechanic.id; h.clickText('Add Ability');
  const picker = h.field('definition.localAbilities[0].mechanic.unitTypeId');
  assert.equal(picker.children.filter(option => option.value === 'grunt').length, 1);
  assert.match(picker.textContent, /Scoped Guard/);
  assert.ok(!picker.textContent.includes('Canonical Guard'));
  assert.match(picker.textContent, /Paper Friend/);
  h.change('definition.localAbilities[0].mechanic.unitTypeId', 'paper-unit', 'change');
  h.change('definition.localAbilities[0].mechanic.initialPosture', 'Lying', 'change');
  await h.editor.validate();
  assert.deepEqual(h.validated[0].context.unitTypes, unitTypes);
  assert.deepEqual(h.validated[0].definition.localAbilities[0].mechanic,
    { kind: 'adjacentUnitSummon', unitTypeId: 'paper-unit', initialPosture: 'Lying' });
});


test('Unit form uses authoritative fields and Unit profiles, preserving inline storage and stable reordered keys', async () => {
  const unitMetadata = {
    defaults: { id: '', displayName: '', mov: 3, footprint: 'OneByOne', actions: 'NormalAttack', behaviors: 'None', localAbilities: [] },
    stats: [{ name: 'mov', kind: 'Integer', minimum: 0, maximum: 17 }], footprints: ['OneByOne', 'TwoByTwo'], movementPatterns: ['Adjacent'],
    behaviors: [{value:'None',name:'None'}, {value:'Flee',name:'Flee'}],
    fields: [{name:'resourceStorage', kind:'Object', allowNull:true, members:[{name:'capacities',kind:'List',required:true,minimumItems:1,item:{kind:'Object',required:true,members:[
      {name:'type',kind:'Object',required:true,members:[{name:'id',kind:'String',required:true},{name:'name',kind:'String',required:true}]},
      {name:'capacity',kind:'Integer',required:true,minimum:0}
    ]}}]}]
  };
  const h = harness({callbacks:{catalog:{...catalog,unitAuthoring:unitMetadata}}});
  await h.editor.open({family:'units'});
  assert.equal(h.field('definition.mov').max,17);
  assert.equal(h.byId('content-editor-add-mechanic').children.some(o=>o.value==='testAction'),false);
  h.change('definition.id','river-keeper'); h.change('definition.displayName','River Keeper');
  h.change('definition.resourceStorage','value','change');
  h.change('definition.resourceStorage.capacities[0].type.id','private-dew');
  h.change('definition.resourceStorage.capacities[0].type.name','Private Dew');
  h.change('definition.resourceStorage.capacities[0].capacity','9');
  h.clickText('Add Ability'); h.clickText('Add Ability');
  h.change('definition.localAbilities[0].name','First');
  const down=h.root.all(e=>e.attributes['aria-label']==='Move down Ability 1')[0]; down.listeners.click();
  h.change('definition.actions','false','change'); h.change('definition.behaviors.Flee','true','change');
  await h.editor.save();
  const unit=h.saved[0].definition;
  assert.equal(unit.displayName,'River Keeper'); assert.equal(Object.hasOwn(unit,'name'),false);
  assert.equal(unit.actions,'None'); assert.equal(unit.behaviors,'Flee');
  assert.deepEqual(unit.resourceStorage,{capacities:[{type:{id:'private-dew',name:'Private Dew'},capacity:9}]});
  assert.deepEqual(unit.localAbilities.map(a=>a.key),['ability-2','ability-1']);
  assert.equal(unit.localAbilities[1].name,'First');
});

test('Unit copies retain authored fields and independent stable Ability identity', () => {
  const h=harness(); h.environment.catalog=catalog;
  h.run('globalThis.state=new ContentEditorState(catalog); state.open({family:"units",definition:{id:"source",displayName:"Source",actions:"None",localAbilities:[{key:"own",name:"Own",timing:"Action",mechanic:{kind:"unitOnly"}}]},copy:true,newId:"copy"}); state.set(["displayName"],"Copy"); state.addAbility("unitOnly")');
  assert.equal(h.run('state.definition.id'),'copy');
  assert.deepEqual(plain(h.run('state.definition.localAbilities.map(a=>a.key)')),['own','ability-1']);
  assert.equal(h.run('state.profiles(state.mechanic("unitOnly"))[0].source'),'Unit');
});


test('Unit sections group metadata fields and offer the unsaved candidate for self summoning', async () => {
  const summon={id:'adjacentUnitSummon',name:'Summon',family:'Ability',profiles:[{source:'Unit',timing:'Action',usage:'UnlimitedOnly'}],parameters:[{name:'unitTypeId',kind:'Reference',required:true,referenceCatalog:'units'}],description:{}};
  const field=(name,kind='Boolean')=>({name,kind,required:true});
  const effective={entries:[{family:'mechanics',id:summon.id,mechanic:summon}],unitAuthoring:{
    defaults:{id:'',displayName:'',actions:'NormalAttack',behaviors:'None',localAbilities:[]},stats:[{name:'hp',kind:'Integer',minimum:1}],
    fields:[field('unique'),field('footprint','Enum'),field('canHaveItems'),field('canHaveEquipment'),field('lootDropSuccesses','Integer'),
      {name:'resourceStorage',kind:'Object',allowNull:true,members:[]},{name:'attackDefinitionOrder',kind:'List',required:true,item:{kind:'String',required:true}}],behaviors:[]
  }};
  const h=harness({callbacks:{catalog:effective}});await h.editor.open({family:'units'});
  const headings=h.root.all(e=>e.tagName==='h4').map(e=>e.textContent);
  assert.deepEqual(headings.slice(0,8),['Identity','Stats','Movement & footprint','Capabilities','Abilities','Loot','Resources','Automation']);
  const sectionOf=path=>{let node=h.field(path);while(node&&!node.className.split(' ').includes('ce-section'))node=node.parentElement;return node.children[0].textContent;};
  for(const [path,section] of [['unique','Stats'],['footprint','Movement & footprint'],['canHaveItems','Capabilities'],['lootDropSuccesses','Loot'],['resourceStorage','Resources']])
    assert.equal(sectionOf('definition.'+path),section);
  h.change('definition.id','new-self');h.change('definition.displayName','New Self');h.clickText('Add Ability');
  let picker=h.field('definition.localAbilities[0].mechanic.unitTypeId');
  assert.match(picker.children.find(o=>o.value==='new-self').textContent,/New Self .* new-self/);
  h.change('definition.id','renamed-self');
  h.change('definition.localAbilities[0].mechanic.kind','adjacentUnitSummon','change');
  picker=h.field('definition.localAbilities[0].mechanic.unitTypeId');
  assert.ok(picker.children.some(o=>o.value==='renamed-self'));
  assert.equal(picker.children.some(o=>o.value==='new-self'),false);
  h.change('definition.localAbilities[0].mechanic.unitTypeId','renamed-self','change');await h.editor.validate();
  assert.equal(h.validated[0].definition.localAbilities[0].mechanic.unitTypeId,'renamed-self');
});


test('successful preview uses the validated candidate catalog for reference inspection', async () => {
  const candidate={key:'units:new-self',family:'units',id:'new-self',name:'New Self',summary:'Validated candidate reference.'};
  const h=harness({validation:async request=>({valid:true,diagnostics:[],definition:request.definition,
    entry:{...candidate,references:[{label:'Summons',family:'units',id:'new-self',targetKey:candidate.key,exists:true}]},catalog:{entries:[candidate]}})});
  await h.editor.open({family:'units',definition:{id:'new-self',displayName:'New Self',actions:'None',localAbilities:[]}});
  await h.editor.validate();
  const inspect=h.root.all(e=>e.dataset.referenceId==='new-self')[0];
  assert.ok(inspect);assert.match(inspect.textContent,/Inspect New Self/);
  assert.match(h.root.textContent,/Validated candidate reference/);
});


test('Single Use Item forms use Item profiles and retain two independent Ability keys through rename, reorder, edit and copy', async () => {
  const h = harness();
  const itemMechanic = { ...shared, id: 'itemVector', profiles: [
    { source: 'Equipment', timing: 'Passive', usage: 'UnlimitedOnly' },
    { source: 'Item', timing: 'BonusAction', usage: 'UnlimitedOnly' }
  ] };
  const itemAction = { ...action, id: 'itemAction', profiles: [{ source: 'Item', timing: 'Action', usage: 'UnlimitedOnly' }] };
  h.environment.options.catalog = { entries: [...catalog.entries, ...[itemMechanic, itemAction].map(mechanic => ({ family: 'mechanics', id: mechanic.id, mechanic }))] };
  await h.editor.open({ family: 'items' });
  h.change('definition.id', 'tea'); h.change('definition.name', 'Tea');
  assert.deepEqual(h.byId('content-editor-add-mechanic').children.map(option => option.value), ['itemVector', 'itemAction']);
  h.clickText('Add Ability');
  assert.deepEqual(h.field('definition.localAbilities[0].timing').children.map(option => option.value), ['BonusAction']);
  assert.equal(h.field('definition.localAbilities[0].usage'), undefined);
  assert.match(h.root.textContent, /consumes the selected physical Item/);
  h.change('definition.localAbilities[0].key', 'sip');
  h.change('definition.localAbilities[0].name', 'A Sip');
  h.byId('content-editor-add-mechanic').value = 'itemAction'; h.clickText('Add Ability');
  h.change('definition.localAbilities[1].key', 'share');
  h.change('definition.localAbilities[1].name', 'Share a Cup');
  h.change('definition.localAbilities[1].mechanic.amount', '3');
  h.clickText('Move down');
  assert.equal(h.field('definition.localAbilities[0].key').value, 'share');
  assert.equal(h.field('definition.localAbilities[1].key').value, 'sip');
  await h.editor.save();
  assert.equal(h.saved[0].family, 'items');
  const definition = h.saved[0].definition;
  assert.deepEqual(definition.localAbilities.map(ability => [ability.key, ability.name, ability.timing, ability.usage.maxUses]), [
    ['share', 'Share a Cup', 'Action', null], ['sip', 'A Sip', 'BonusAction', null]
  ]);
  await h.editor.open({ family: 'items', definition, existingId: 'tea' });
  assert.equal(h.field('definition.localAbilities[0].key').readOnly, true);
  assert.equal(h.field('definition.localAbilities[1].key').readOnly, true);
  h.change('definition.localAbilities[0].key', 'replaced');
  h.change('definition.localAbilities[0].name', 'Offer a Cup');
  h.clickText('Move down');
  await h.editor.save();
  assert.equal(h.saved[1].existingId, 'tea');
  const edited = h.saved[1].definition;
  assert.deepEqual(edited.localAbilities.map(ability => [ability.key, ability.name]), [['sip', 'A Sip'], ['share', 'Offer a Cup']]);
  await h.editor.open({ family: 'items', definition: edited, existingId: 'tea', copy: true, newId: 'fresh-tea' });
  assert.equal(h.field('definition.localAbilities[0].key').readOnly, false);
  h.change('definition.localAbilities[0].mechanic.adjustments[0].amount', '5');
  h.change('definition.localAbilities[1].name', 'Copied Cup');
  await h.editor.save();
  assert.equal(h.saved[2].existingId, undefined);
  assert.equal(h.saved[2].definition.id, 'fresh-tea');
  assert.deepEqual(h.saved[2].definition.localAbilities.map(ability => ability.key), ['sip', 'share']);
  assert.equal(h.saved[2].definition.localAbilities[0].mechanic.adjustments[0].amount, 5);
  assert.equal(edited.localAbilities[0].mechanic.adjustments[0].amount, 0);
  assert.equal(edited.localAbilities[1].name, 'Offer a Cup');
  assert.equal(definition.localAbilities[0].name, 'Share a Cup');
});

test('ordered Costs add, type changes, typed references, nullable fields and reordering reach Engine preview and save', async () => {
  const h = costHarness(); await h.editor.open({ family: 'equipment' });
  h.byId('content-editor-add-mechanic').value = 'costOperation'; h.clickText('Add Ability');
  addCost(h, 'paperPayment'); addCost(h, 'paperPayment');
  // Duplicate validity belongs to the Engine, rather than an ID-specific client rule.
  h.change('definition.localAbilities[0].costs[1].kind', 'coinPayment', 'change');
  assert.equal(h.field('definition.localAbilities[0].costs[1].amount').value, 2);
  assert.equal(h.field('definition.localAbilities[0].costs[1].amount').max, 9);
  assert.match(h.field('definition.localAbilities[0].costs[1].resourceTypeId').textContent, /Dew/);
  h.change('definition.localAbilities[0].costs[1].resourceTypeId', 'dew', 'change');
  h.change('definition.localAbilities[0].costs[1].amount', '4');
  h.change('definition.localAbilities[0].costs[1].note', 'null', 'change');
  clickLabel(h, 'Move up Cost 2 of Ability 1');
  assert.equal(h.field('definition.localAbilities[0].costs[0].kind').value, 'coinPayment');
  await h.editor.validate();
  assert.deepEqual(h.validated[0].definition.localAbilities[0].costs, [
    { kind: 'coinPayment', resourceTypeId: 'dew', amount: 4, note: null }, { kind: 'paperPayment' }
  ]);
  assert.match(h.root.textContent, /Authoritative generated rule/);
  assert.ok(!h.root.textContent.includes('NEVER EVALUATE'));
  clickLabel(h, 'Remove Cost 2 from Ability 1');
  assert.ok(!h.root.textContent.includes('Authoritative generated rule'));
  assert.equal(h.editor.isDirty(), true);
  await h.editor.save();
  assert.deepEqual(h.saved[0].definition.localAbilities[0].costs, [{ kind: 'coinPayment', resourceTypeId: 'dew', amount: 4, note: null }]);
});

test('Costs stay attached to independent Ability keys through rename, Ability reorder and copy for every source', async () => {
  for (const family of ['units', 'equipment', 'items']) {
    const original = { id: 'original', name: 'Original', displayName: 'Original', localAbilities: [
      { key: 'first', name: 'First', timing: 'Action', usage: { maxUses: null }, mechanic: { kind: 'costOperation', amount: 1 }, costs: [{ kind: 'coinPayment', resourceTypeId: 'dew', amount: 2 }] },
      { key: 'second', name: 'Second', timing: 'BonusAction', usage: { maxUses: null }, mechanic: { kind: 'costOperation', amount: 1 }, costs: [{ kind: 'paperPayment' }] }
    ] };
    const h = costHarness(); await h.editor.open({ family, definition: original, existingId: 'original', copy: true, newId: 'copy' });
    h.change('definition.localAbilities[0].name', 'Renamed'); clickLabel(h, 'Move down Ability 1');
    h.change('definition.localAbilities[1].costs[0].amount', '7');
    await h.editor.save();
    assert.deepEqual(h.saved[0].definition.localAbilities.map(ability => [ability.key, ability.name, ability.costs]), [
      ['second', 'Second', [{ kind: 'paperPayment' }]], ['first', 'Renamed', [{ kind: 'coinPayment', resourceTypeId: 'dew', amount: 7 }]]
    ]);
    assert.equal(h.saved[0].family, family); assert.equal(h.saved[0].existingId, undefined);
    assert.equal(original.localAbilities[0].costs[0].amount, 2);
  }
});

test('Cost choices intersect mechanic and Cost profiles by source, timing, usage and execution availability', async () => {
  const h = costHarness(); await h.editor.open({ family: 'equipment' });
  h.byId('content-editor-add-mechanic').value = 'costOperation'; h.clickText('Add Ability');
  let choices = h.root.all(element => element.dataset.costChoice === '0')[0];
  assert.deepEqual(choices.children.map(choice => choice.value), ['paperPayment', 'coinPayment']);
  h.change('definition.localAbilities[0].timing', 'Passive', 'change');
  assert.equal(h.root.all(element => element.dataset.addCost === '0')[0].disabled, true);
  h.change('definition.localAbilities[0].timing', 'Action', 'change'); addCost(h, 'paperPayment');
  assert.equal(h.field('definition.localAbilities[0].timing').children.find(choice => choice.value === 'Passive').disabled, true);
  assert.equal(h.field('definition.localAbilities[0].mechanic.kind').children.find(choice => choice.value === 'intrinsic').disabled, true);
  h.change('definition.localAbilities[0].timing', 'Passive', 'change');
  await h.editor.validate(); assert.equal(h.validated[0].definition.localAbilities[0].timing, 'Action');
  assert.deepEqual(h.validated[0].definition.localAbilities[0].costs, [{ kind: 'paperPayment' }]);
  clickLabel(h, 'Remove Cost 1 from Ability 1');
  h.change('definition.localAbilities[0].mechanic.kind', 'intrinsic', 'change');
  assert.equal(h.root.all(element => element.dataset.addCost === '0')[0].disabled, true);

  h.environment.catalog = costCatalog;
  h.run('globalThis.state = new ContentEditorState(catalog); state.open({family:"items",definition:{id:"x",localAbilities:[{timing:"Action",usage:{maxUses:2},mechanic:{kind:"costOperation"}}]}})');
  assert.deepEqual(plain(h.run('state.availableCosts(state.definition.localAbilities[0])')), []);
  h.run('state.set(["localAbilities",0,"usage"],{maxUses:null})');
  assert.deepEqual(plain(h.run('state.availableCosts(state.definition.localAbilities[0]).map(cost=>cost.id)')), ['paperPayment', 'coinPayment']);
  h.run('state.open({family:"units"}); state.addAbility("costOperation")');
  assert.deepEqual(plain(h.run('state.availableCosts(state.definition.localAbilities[0]).map(cost=>cost.id)')), ['paperPayment', 'coinPayment', 'unitPayment']);
});

test('imported unsupported Costs are preserved with repair controls and unchanged payloads', async () => {
  const h = costHarness();
  const definition = { id: 'import', name: 'Import', localAbilities: [{ key: 'old', name: 'Old', timing: 'Passive',
    mechanic: { kind: 'costOperation', amount: 1 }, costs: [{ kind: 'coinPayment', resourceTypeId: 'dew', amount: 3 }, { kind: 'unfamiliarCost', opaque: 71 }] }] };
  await h.editor.open({ family: 'equipment', definition, existingId: 'import' });
  assert.match(h.root.textContent, /Cost is preserved but is unavailable/);
  assert.equal(h.editor.isDirty(), false);
  assert.equal(h.root.all(element => element.dataset.addCost === '0')[0].disabled, true);
  await h.editor.validate(); assert.deepEqual(h.validated[0].definition, definition);
  clickLabel(h, 'Remove Cost 2 from Ability 1');
  h.change('definition.localAbilities[0].timing', 'Action', 'change');
  await h.editor.validate();
  assert.deepEqual(h.validated[1].definition.localAbilities[0].costs, [definition.localAbilities[0].costs[0]]);
  assert.equal(h.validated[1].definition.localAbilities[0].timing, 'Action');
});

test('Cost collection, individual, kind and parameter diagnostics map to their exact field paths and block save', async () => {
  const base = 'definition.localAbilities[0].costs';
  const h = costHarness({ validation: async () => ({ valid: false, diagnostics: [
    { path: base, message: 'Review Costs.' }, { path: base + '[0]', message: 'Duplicate payment.' },
    { path: base + '[0].kind', message: 'Unsupported kind.' }, { path: base + '[1].amount', message: 'Amount must be positive.' }
  ] }) });
  await h.editor.open({ family: 'equipment' }); h.byId('content-editor-add-mechanic').value = 'costOperation'; h.clickText('Add Ability');
  addCost(h, 'paperPayment'); addCost(h, 'coinPayment');
  assert.equal(await h.editor.save(), false);
  for (const path of [base, base + '[0]', base + '[0].kind', base + '[1].amount']) {
    const field = h.field(path); assert.equal(field.attributes['aria-invalid'], 'true'); field.focus(); assert.equal(h.document.activeElement, field);
  }
  assert.match(h.field(base + '[0]').textContent, /Duplicate payment/);
  assert.equal(h.saved.length, 0); assert.equal(h.editor.isDirty(), true);
});

test('usage compatibility follows Cost metadata and supported mechanic changes preserve ordered Costs', async () => {
  const limitedProfiles = costProfiles.filter(profile => profile.source === 'Equipment').map(profile => ({ ...profile, usage: 'LimitedOnly' }));
  const effective = { ...costCatalog, costs: [...costCatalog.costs, { id: 'limitedPayment', name: 'Limited Payment', executionAvailable: true, profiles: limitedProfiles, parameters: [] }],
    entries: [...costCatalog.entries, { family: 'mechanics', id: 'otherOperation', mechanic: { ...costMechanic, id: 'otherOperation' } }] };
  const h = costHarness({ callbacks: { catalog: effective } }); await h.editor.open({ family: 'equipment' });
  h.byId('content-editor-add-mechanic').value = 'costOperation'; h.clickText('Add Ability');
  assert.equal(h.root.all(element => element.dataset.costChoice === '0')[0].children.some(option => option.value === 'limitedPayment'), false);
  h.change('definition.localAbilities[0].usage', 'limited', 'change'); addCost(h, 'limitedPayment'); addCost(h, 'paperPayment');
  assert.equal(h.field('definition.localAbilities[0].usage').children.find(option => option.value === 'unlimited').disabled, true);
  h.change('definition.localAbilities[0].usage', 'unlimited', 'change');
  h.change('definition.localAbilities[0].mechanic.kind', 'otherOperation', 'change');
  h.change('definition.localAbilities[0].timing', 'FreeAction', 'change');
  await h.editor.validate();
  assert.deepEqual(h.validated[0].definition.localAbilities[0].costs, [{ kind: 'limitedPayment' }, { kind: 'paperPayment' }]);
  assert.equal(h.validated[0].definition.localAbilities[0].usage.maxUses, 1);
  assert.equal(h.validated[0].definition.localAbilities[0].mechanic.kind, 'otherOperation');
  const closing = h.editor.requestClose(); h.byId('content-editor-keep').listeners.click(); assert.equal(await closing, false);
  assert.equal(h.editor.isDirty(), true); assert.equal(h.editor.isOpen(), true);
});
