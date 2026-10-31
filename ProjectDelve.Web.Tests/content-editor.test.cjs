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
