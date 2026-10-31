const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");

test("generated Designer transfer preserves the exact definition and identifies later edits through recovery", () => {
  const storage = memoryStorage(), h = harness(undefined, undefined, storage);
  const result = { code:"DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF", definition:structuredClone(designerSetup) };
  h.context.generated = result;
  assert.equal(h.run('delveOpenGenerated(generated)'), true);
  assert.deepEqual(plain(h.editor.draft.snapshot()), result.definition);
  const label = h.run('document.getElementById("designer-generation-source")');
  assert.match(label.textContent, /Original generated scenario/);
  h.editor.draft.paintTerrain({x:0,y:0}, "SolidRock"); h.editor.render();
  assert.match(label.textContent, /Modified scenario.*original only/);
  assert.equal(h.editor.documentData().generationSource.code, result.code);
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  assert.match(label.textContent, /Modified scenario/);
  assert.equal(h.editor.draft.terrainAt({x:0,y:0}), "SolidRock");
});
const designerSetup = JSON.parse(fs.readFileSync(path.join(__dirname, "fixtures/designer-setup.json"), "utf8"));

test("All Loot uses current Engine metadata and waits before Copy to Custom when local content changes", async () => {
  const savedPresets = catalog.lootPresets;
  try {
    const all = { id: 'All', name: 'All', isDynamic: true, composition: [{ lootCardId: 'canonical', count: 1 }] };
    catalog.lootPresets = [all];
    const pending = [];
    const h = harness(undefined, undefined, undefined, body => new Promise(resolve => pending.push({ body: plain(body), resolve })));
    const draft = h.editor.draft;
    draft.setLootPreset('All');
    assert.deepEqual(plain(draft.lootComposition()), all.composition);
    assert.throws(() => draft.setLootMultiplier(2), /exactly one/);
    draft.applyContentDefinition('lootCards', { id: 'new-card', name: 'New Card', immediate: { kind: 'healCollector', amount: 3 } });
    h.editor.render();
    const form = () => h.run('document.getElementById("designer-loot-editor")').children[0];
    const copy = () => form().querySelectorAll('button').find(button => button.textContent === 'Copy to Custom');
    assert.equal(copy().disabled, true);
    assert.throws(() => draft.copyLootToCustom(), /Wait for/);
    assert.equal(form().querySelectorAll('input').some(input => input.id === 'designer-loot-multiplier'), false);
    assert.equal(pending.at(-1).body.lootCardDefinitions[0].id, 'new-card');
    const currentRows = [...all.composition, { lootCardId: 'new-card', count: 1 }];
    pending.at(-1).resolve({ unitTypes: catalog.unitTypes, unitIds: [], lootPresets: [{ ...all, composition: currentRows }] });
    await new Promise(setImmediate);
    assert.equal(copy().disabled, false);
    assert.deepEqual(plain(draft.lootComposition()), currentRows);
    // A late canonical response must not replace the effective local result.
    pending[0].resolve({ unitTypes: catalog.unitTypes, unitIds: [], lootPresets: [all] });
    await new Promise(setImmediate);
    assert.deepEqual(plain(draft.lootComposition()), currentRows);
    press(form(), 'Copy to Custom');
    assert.deepEqual(plain(draft.snapshot().loot), { composition: currentRows });
    h.click('undo');
    assert.deepEqual(plain(draft.snapshot().loot), { preset: 'All' });
    draft.applyContentDefinition('lootCards', { id: 'later-card', name: 'Later Card', immediate: { kind: 'healCollector', amount: 1 } });
    h.editor.render();
    assert.equal(copy().disabled, true);
    assert.deepEqual(plain(draft.snapshot().loot), { preset: 'All' });
    const laterRows = [...currentRows, { lootCardId: 'later-card', count: 1 }];
    pending.at(-1).resolve({ unitTypes: catalog.unitTypes, unitIds: [], lootPresets: [{ ...all, composition: laterRows }] });
    await new Promise(setImmediate);
    assert.deepEqual(plain(draft.lootComposition()), laterRows);
  } finally { catalog.lootPresets = savedPresets; }
});

test("Custom Loot editor retains distinct legacy card and Equipment Type quantities", () => {
  const savedCards = catalog.lootCards, savedEquipment = catalog.equipmentTypes;
  try {
    catalog.lootCards = [{kind:"LuckyBoots",equipmentTypeId:"lucky-boots",name:"Lucky Boots",category:"Equipment"}];
    catalog.equipmentTypes = [{id:"lucky-boots",name:"Lucky Boots"},{id:"new-canonical",name:"Cloud Compass"}];
    const h = harness(), draft = h.editor.draft;
    draft.definition.equipmentTypes = [{id:"local",name:"Scenario Compass"}];
    draft.setLootDeck([{kind:"LuckyBoots",count:2},{equipmentTypeId:"lucky-boots",count:3},
      {equipmentTypeId:"new-canonical",count:4},{equipmentTypeId:"local",count:5}]);
    h.editor.render();
    const form = h.run('document.getElementById("designer-loot-editor")').children[0];
    assert.equal(inputField(form,"Lucky Boots").value,"2");
    assert.equal(inputField(form,"Lucky Boots (Equipment Type)").value,"3");
    assert.equal(inputField(form,"Cloud Compass (Equipment Type)").value,"4");
    assert.equal(inputField(form,"Scenario Compass (Equipment Type)").value,"5");
    const before = plain(draft.snapshot().loot);
    h.submit(form);
    assert.deepEqual(plain(draft.snapshot().loot),before);
  } finally {
    catalog.lootCards = savedCards; catalog.equipmentTypes = savedEquipment;
  }
});
const plain = value => JSON.parse(JSON.stringify(value));

test('Inspect content sends a detached draft snapshot without export or Playtest', async () => {
  const requests = [], played = [], inspected = [];
  const h = harness(async (...args) => { requests.push(args); return { valid: true, errors: [] }; },
    async (...args) => { played.push(args); return true; });
  h.context.delveInspectContent = (definition, name) => { inspected.push({ definition, name }); };
  h.editor.ui.title.value = 'Inspection draft';
  const expected = plain(h.editor.draft.snapshot());
  h.editor.ui.content.listeners.click();
  assert.equal(inspected.length, 1);
  assert.deepEqual(plain(inspected[0]), { definition: expected, name: 'Inspection draft' });
  inspected[0].definition.board.width = 99;
  assert.deepEqual(plain(h.editor.draft.snapshot()), expected);
  assert.deepEqual(requests, []); assert.deepEqual(played, []);
});

test('Content destination commits definitions through Designer history, recovery and Playtest snapshots', async () => {
  const storage = memoryStorage(), submitted = [], played = [];
  const h = harness(async (operation, body) => { submitted.push({ operation, body: plain(body) }); return { transport: 'DELVE1:content-fixture' }; },
    async transport => { played.push(transport); return true; }, storage);
  let destination;
  h.context.delveInspectContent = (definition, name, target) => { destination = target; };
  h.click('content');
  const equipment = { id: 'new-medallion', name: 'New Medallion', localAbilities: [
    { key: 'stride', name: 'Steady Stride', timing: 'Passive', mechanic: { kind: 'selfModifiers', modifiers: [{ stat: 'Mov', amount: 1 }, { stat: 'Def', amount: 1 }] } }
  ] };
  await destination.save({ family: 'equipment', definition: equipment, expectedRevision: destination.getRevision() });
  const context = destination.getContext();
  assert.deepEqual(plain(context.equipmentTypes), [equipment]);
  context.equipmentTypes[0].name = 'Detached context';
  const loot = { id: 'medallion-reward', name: 'Medallion Reward', equipmentTypeId: equipment.id };
  await destination.save({ family: 'lootCards', definition: loot, expectedRevision: destination.getRevision() });
  const saved = plain(h.editor.draft.snapshot());
  assert.deepEqual(saved.equipmentTypes, [equipment]); assert.deepEqual(saved.lootCardDefinitions, [loot]);
  assert.ok(h.editor.draft.objectCatalog().some(row => row.reference.equipmentTypeId === equipment.id));
  h.click('undo'); assert.equal(h.editor.draft.definition.lootCardDefinitions, undefined);
  h.click('redo'); assert.deepEqual(plain(h.editor.draft.snapshot()), saved);
  const recovered = harness(undefined, undefined, storage);
  assert.deepEqual(plain(recovered.editor.draft.snapshot()), saved);
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  await h.click('play');
  assert.deepEqual(submitted.at(-1), { operation: 'export', body: saved });
  assert.deepEqual(played, ['DELVE1:content-fixture']);
});

test('Content destination refuses stale saves after another document opens or the draft changes', async () => {
  const h = harness(); let destination;
  h.context.delveInspectContent = (definition, name, target) => { destination = target; };
  h.click('content'); const revision = destination.getRevision();
  h.editor.ui.title.value = 'Renamed destination';
  assert.equal(destination.label, 'Scenario draft · Renamed destination');
  assert.equal(destination.getRevision(), revision);
  const doc = h.editor.documentData(); doc.title = 'Another draft';
  h.editor.loadDocument(JSON.stringify(doc));
  const before = plain(h.editor.draft.snapshot());
  await assert.rejects(destination.save({ family: 'equipment', definition: { id: 'stale', name: 'Stale', localAbilities: [] }, expectedRevision: revision }), /draft changed/);
  assert.deepEqual(plain(h.editor.draft.snapshot()), before);
  assert.equal(destination.getSnapshot().name, 'Another draft');
  assert.equal(destination.label, 'Scenario draft · Another draft');
});

test('Custom Loot editor keeps local Loot Card and Item quantities independent', () => {
  const h = harness(), draft = h.editor.draft;
  draft.definition.itemTypes = [{id:'paper-moon',name:'Paper Moon',localAbilities:[{key:'read',name:'Fold the Sky',timing:'Action',mechanic:{kind:'holyWave'}}]}];
  draft.definition.lootCardDefinitions = [{id:'letter',name:'Unexpected Letter',itemTypeId:'paper-moon'}];
  draft.setLootDeck([{lootCardId:'letter',count:2},{itemTypeId:'paper-moon',count:3}]);
  h.editor.render();
  const form = h.run('document.getElementById("designer-loot-editor")').children[0];
  assert.equal(inputField(form,'Unexpected Letter').value,'2');
  assert.equal(inputField(form,'Paper Moon (Item Type)').value,'3');
  h.submit(form);
  assert.deepEqual(plain(draft.snapshot().loot.composition),[{itemTypeId:'paper-moon',count:3},{lootCardId:'letter',count:2}]);
  assert.ok(plain(draft.objectCatalog()).some(c => c.reference.itemTypeId === 'paper-moon' && c.name === 'Paper Moon'));
});

test('Immediate Loot definitions survive deck editing and recovery without becoming owned objects', () => {
  const h = harness(), draft = h.editor.draft;
  const card = {id:'restorative-letter',name:'A Restorative Letter',immediate:{kind:'healCollector',amount:3}};
  draft.definition.lootCardDefinitions = [card];
  draft.setLootDeck([{lootCardId:card.id,count:2}]);
  h.editor.render();
  const form = h.run('document.getElementById("designer-loot-editor")').children[0];
  assert.equal(inputField(form,card.name).value,'2');
  h.submit(form);
  assert.deepEqual(plain(draft.snapshot().loot.composition),[{lootCardId:card.id,count:2}]);
  assert.ok(!plain(draft.objectCatalog()).some(c => c.reference.lootCardId === card.id));
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  assert.deepEqual(plain(h.editor.draft.snapshot().lootCardDefinitions),[card]);
});

test('Cell Loot marker change commits before Apply and survives export without retained form inputs', async () => {
  const operations = [];
  const h = harness(async (operation, body) => {
    operations.push({ operation, body: plain(body) });
    return { transport: 'DELVE1:marker-fixture' };
  });
  const draft = h.editor.draft;
  draft.setLootDeck([{ kind: 'LuckyBoots', count: 2 }]);
  h.editor.setTool('Select'); h.down(h.cell(2, 2));
  let form = h.editor.ui.properties.children[0];
  const key = form.dataset.formKey;
  const count = inputField(form, 'Unopened Loot markers');
  enter(count, '1'); count.listeners.change();
  // A browser's bubbled change submits this immediate inspector before the
  // Apply button receives its click, replacing the form in the meantime.
  form.listeners.change();
  form = h.editor.ui.properties.children[0];
  assert.equal(inputField(form, 'Unopened Loot markers').value, '1');
  assert.deepEqual(plain(draft.definition.lootMarkers).map(marker => marker.position), [{ x: 2, y: 2 }]);
  const firstId = draft.definition.lootMarkers[0].id;
  press(form, 'Apply Loot markers');
  assert.equal(draft.definition.lootMarkers.length, 1);
  assert.equal(draft.definition.lootMarkers[0].id, firstId);
  assert.equal(h.editor.documentData().buffers[key], undefined);
  await h.click('export');
  assert.equal(operations.length, 1);
  assert.equal(operations[0].operation, 'export');
  assert.deepEqual(operations[0].body.lootMarkers, plain(draft.definition.lootMarkers));

  // The explicit button also commits an input without relying on change bubbling.
  form = h.editor.ui.properties.children[0];
  enter(inputField(form, 'Unopened Loot markers'), '2');
  press(form, 'Apply Loot markers');
  assert.equal(draft.definition.lootMarkers.length, 2);
  assert.equal(h.editor.documentData().buffers[key], undefined);
  await h.click('export');
  assert.equal(operations.length, 2);
  assert.equal(operations[1].body.lootMarkers.length, 2);
});

test('older local Loot IDs retain their name and one deck row beside canonical legacy choices', () => {
  const savedDefinitions = catalog.lootCardDefinitions, savedCards = catalog.lootCards;
  try {
    catalog.lootCardDefinitions = [{id:'pocket-breakfast',name:'Pocket Breakfast',immediate:{kind:'healCollector',amount:2}}];
    catalog.lootCards = [{kind:'PocketBreakfast',name:'Pocket Breakfast',category:'Immediate'}];
    const h = harness(), draft = h.editor.draft;
    const local = {id:'pocket-breakfast',name:'A Folded Letter',itemTypeId:'paper-moon'};
    draft.definition.lootCardDefinitions = [local];
    draft.definition.itemTypes = [{id:'paper-moon',name:'Paper Moon',localAbilities:[{key:'read',name:'Fold the Sky',timing:'Action',mechanic:{kind:'holyWave'}}]}];
    const composition = [{kind:'PocketBreakfast',count:1},{lootCardId:local.id,count:3}];
    draft.setLootDeck(composition);
    h.editor.render();
    assert.equal(draft.objectName({lootCardId:local.id}),local.name);
    assert.equal(draft.objectName({kind:'PocketBreakfast'}),'Pocket Breakfast');
    assert.equal(draft.lootCardCatalog().filter(card => card.reference.lootCardId === local.id).length,1);
    const form = h.run('document.getElementById("designer-loot-editor")').children[0];
    assert.equal(inputField(form,'Pocket Breakfast').value,'1');
    assert.equal(inputField(form,local.name).value,'3');
    h.submit(form);
    assert.deepEqual(plain(draft.snapshot().loot.composition),composition);
    assert.deepEqual(plain(draft.snapshot().lootCardDefinitions),[local]);
  } finally {
    catalog.lootCardDefinitions = savedDefinitions; catalog.lootCards = savedCards;
  }
});

const inputField = (form, label) => form.querySelectorAll("label").find(node => node._text === label)?.children[0];
const press = (root, label) => root.querySelectorAll("button").find(b => b.textContent === label).listeners.click();
const change = (input, value) => { input.value = value; input.listeners.change?.(); };
const objectiveForm = h => h.editor.ui["side-settings"].children.at(-1);
function openObjective(h, side = "blue") {
  h.editor.ui["side-settings"].children.find(f => f.children[0].textContent === side).querySelectorAll("button").find(b => b.textContent.includes("Edit Objective")).listeners.click();
  return objectiveForm(h);
}
const memoryStorage = () => {
  const data = new Map();
  return { getItem: key => data.get(key) ?? null, setItem: (key, value) => data.set(key, value) };
};
function enter(input, value) { input.value = value; input.listeners.input?.(); }
const catalog = {
  maxBoardSize: 50,
  resourceTypes: ["coal", "diamond", "fruit", "banana", "mushroom", "shell"].map(id => ({ id, name: id[0].toUpperCase() + id.slice(1) })),
  cellMarkerTypes: [{ id: "banana-peel", name: "Banana Peel" }],
  machineTypes: [{ id: "diamond-press", name: "Diamond Press",
    storage: { capacities: [{ type: { id: "coal", name: "Coal" }, capacity: 3 }] },
    operation: { costs: [{ type: { id: "coal", name: "Coal" }, quantity: 3 }],
      outputType: { id: "diamond", name: "Diamond" }, outputQuantity: 1 } }],
  unitTypes: [...designerSetup.unitTypeIds, "opaque-large", "unplaced"].map(id => ({
    unitTypeId: id, displayName: `Printed ${id}`, maxHp: 8,
    cellSpan: id === "opaque-large" ? 2 : 1,
    footprintOffsets: id === "opaque-large" ? [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }] : [{ x: 0, y: 0 }]
  })),
  terrains: ["Grass", "Tree", "Water", "StoneFloor", "StoneFloorWithTable", "Lava", "Rail", "Gravel", "Asphalt"],
  railDirections: ["North", "East", "South", "West"],
  edgeKinds: ["Wall", "ClosedDoor", "OpenDoor", "None", "WallWithWindow"],
  postures: ["Upright", "Lying"], controllers: ["Human", "Automated"],
  orderKinds: ["Normal", "DoNothing", "HoldPosition", "MoveTo", "Attack", "Protect", "Retreat", "InteractWith", "FollowRoute"]
};

// Extends the existing playtest's small DOM approach with delegated pointer events and forms.
class Element {
  constructor(tag = "div") {
    this.tagName = tag; this.children = []; this.dataset = {}; this.attributes = {}; this.listeners = {};
    this.style = { setProperty: (key, value) => { this.style[key] = value; } };
    this.className = ""; this._text = ""; this._value = undefined;
    this.classList = {
      contains: name => this.className.split(" ").includes(name),
      add: name => { if (!this.classList.contains(name)) this.className += ` ${name}`; },
      remove: name => { this.className = this.className.split(" ").filter(value => value !== name).join(" "); }
    };
  }
  set textContent(value) { this._text = String(value); this.replaceChildren(); }
  get textContent() { return this._text + this.children.map(child => child.textContent).join(""); }
  set value(value) { this._value = String(value); }
  get value() { return this._value ?? (this.tagName === "select" ? this.children[0]?.value ?? "" : ""); }
  append(...children) { for (const child of children) { child.remove(); child.parent = this; this.children.push(child); } }
  replaceChildren(...children) { for (const child of this.children) child.parent = null; this.children = []; this.append(...children); }
  remove() { if (this.parent) this.parent.children = this.parent.children.filter(child => child !== this); this.parent = null; }
  setAttribute(key, value) { this.attributes[key] = value; }
  addEventListener(name, callback) {
    const previous = this.listeners[name];
    this.listeners[name] = previous ? event => { previous(event); callback(event); } : callback;
  }
  requestSubmit() { this.listeners.submit?.({ preventDefault() {} }); }
  matches(selector) {
    if (selector === "[data-editor-hit]") return !!this.dataset.editorHit;
    if (selector.startsWith(".")) return this.classList.contains(selector.slice(1));
    return this.tagName === selector;
  }
  closest(selector) { return this.matches(selector) ? this : this.parent?.closest(selector); }
  querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
  focus() {} select() {} scrollIntoView() {}
}

function harness(request = async () => ({ valid: true, errors: [] }), play = async () => true, storage, projectMetadata = async () => ({ unitTypes: catalog.unitTypes, unitIds: [] })) {
  const elements = new Map(), documentListeners = {};
  const document = {
    getElementById(id) {
      if (!elements.has(id)) {
        const node = new Element(/type$|terrain$|edge$|default$|controller$/.test(id) ? "select" : "div");
        elements.set(id, node);
      }
      return elements.get(id);
    },
    createElement(tag) { return new Element(tag); },
    addEventListener(name, callback) { const previous = documentListeners[name]; documentListeners[name] = previous ? event => { previous(event); callback(event); } : callback; }
  };
  const tools = document.getElementById("designer-tools");
  for (const tool of ["Select", "Terrain", "Edge", "Unit", "Machine", "BananaPeel", "RemoveMarker"]) {
    const node = new Element("button"); node.dataset.tool = tool; tools.append(node);
  }
  document.getElementById("designer-side").value = "side-1";
  document.getElementById("designer-agency-side").value = "side-1";
  const details = new Element("details"); details.append(document.getElementById("designer-transport"));
  const context = vm.createContext({ document, catalog, request, play, projectMetadata, structuredClone, localStorage: storage, navigator: { clipboard: { writeText: async () => {} } } });
  vm.runInContext(source("designer-state.js").replace(/^export /gm, ""), context);
  vm.runInContext(source("side-colors.js").replace(/^export /gm, ""), context);
  vm.runInContext(source("designer.js").replace(/^import .*\r?\n/gm, "").replace(/^export /gm, "").replace(/startDesigner\(\);\s*$/, ""), context);
  vm.runInContext("globalThis.editor = mountDesigner(catalog, request, play, projectMetadata)", context);
  const editor = context.editor;
  return {
    editor, documentListeners, context,
    run: code => vm.runInContext(code, context),
    cell: (x, y) => editor.ui.board.children.find(node => node.dataset.editorHit === "cell" && +node.dataset.x === x && +node.dataset.y === y),
    edge: (x, y, direction) => editor.ui.board.children.find(node => node.dataset.direction === direction && +node.dataset.x === x && +node.dataset.y === y),
    down: target => editor.ui.board.listeners.pointerdown({ target, button: 0, preventDefault() {} }),
    move: target => editor.ui.board.listeners.pointermove({ target, buttons: 1 }),
    up: () => documentListeners.pointerup(),
    click: id => editor.ui[id].listeners.click(),
    submit: node => node.listeners.submit({ preventDefault() {} })
  };
}

test("Fog of War Side setting survives undo, redo, draft recovery and playtest while Designer stays omniscient", async () => {
  const storage = memoryStorage(), played = [], operations = [];
  const h = harness(async (operation, body) => {
    operations.push({operation,body:plain(body)});
    return operation === "validate" ? {valid:true,errors:[]} : {transport:"DELVE1:fow-fixture"};
  }, async value => { played.push(value); return true; }, storage);
  const draft = h.editor.draft;
  draft.place("grunt-type", "blue", "Human", { x: 0, y: 0 });
  draft.place("grunt-type", "red", "Automated", { x: 14, y: 14 }); h.editor.render();
  let form = h.editor.ui["side-settings"].children[0];
  assert.equal(inputField(form, "Fog of War — permanent exploration").value, "Disabled");
  inputField(form, "Fog of War — permanent exploration").value = "Enabled"; h.submit(form);
  assert.deepEqual(plain(draft.definition.fogOfWarSides), ["blue"]);
  assert.match(form.textContent, /Explored Cells remain known/);
  assert.ok(h.cell(14,14).textContent.includes("grunt-type") || h.cell(14,14).children.length);
  h.click("undo"); assert.equal(draft.definition.fogOfWarSides, undefined);
  h.click("redo"); assert.deepEqual(plain(draft.definition.fogOfWarSides), ["blue"]);
  const exported = plain(draft.snapshot());
  const recovered = harness(undefined, undefined, storage);
  assert.deepEqual(plain(recovered.editor.draft.snapshot()), exported);
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  assert.deepEqual(plain(draft.snapshot()), exported);
  assert.equal(inputField(h.editor.ui["side-settings"].children[0], "Fog of War — permanent exploration").value, "Enabled");
  await h.click("validate"); await h.click("export"); await h.click("play");
  assert.deepEqual(played, ["DELVE1:fow-fixture"]);
  assert.ok(operations.length >= 2);
  assert.ok(operations.every(row => row.body.fogOfWarSides[0] === "blue"));
  form = h.editor.ui["side-settings"].children[0];
  inputField(form, "Fog of War — permanent exploration").value = "Disabled"; h.submit(form);
  assert.equal(draft.definition.fogOfWarSides, undefined);
});

test("Machine tool places catalog content by pointer and keyboard, edits and deletes individual storage", () => {
  const h = harness(), draft = h.editor.draft;
  h.editor.setTool("Machine");
  assert.equal(h.editor.ui["machine-label"].hidden, false);
  h.move(h.cell(2, 1)); assert.match(h.cell(2, 1).className, /placement-preview/);
  h.down(h.cell(2, 1));
  const firstKey = draft.machineKeys[0];
  assert.equal(draft.definition.machines[0].type.id, "diamond-press");
  assert.match(h.cell(2, 1).textContent, /Diamond PressPublicCoal: 0\/3/);
  assert.equal(draft.definition.units.length, 0); assert.equal(draft.definition.agency.length, 0);
  h.editor.ui.board.listeners.keydown({ key: "Enter", target: h.cell(4, 2), preventDefault() {} });
  assert.notEqual(draft.definition.machines[0].id, draft.definition.machines[1].id);
  h.editor.setTool("Select"); h.down(h.cell(2, 1));
  let form = h.editor.ui.properties.children[0];
  inputField(form, "Machine ID").value = "press-a";
  inputField(form, "Position X").value = "3";
  inputField(form, "Initial Coal (capacity 3)").value = "2"; h.submit(form);
  assert.equal(draft.machineKeys[0], firstKey);
  assert.equal(draft.definition.machines[0].id, "press-a");
  assert.match(h.cell(3, 1).textContent, /Coal: 2\/3/);
  assert.match(h.cell(4, 2).textContent, /Coal: 0\/3/);
  assert.match(h.editor.ui.placements.textContent, /press-a/);
  form = h.editor.ui.properties.children[0];
  form.querySelectorAll("button").find(node => node.textContent === "Delete Machine").listeners.click();
  assert.equal(draft.definition.machines.length, 1);
  assert.throws(() => draft.resize(4, 2), /Shrinking/);
});

test("Resources place, reject collisions, change type, move, recover and undo atomically", () => {
  const storage = memoryStorage(), h = harness(undefined, undefined, storage), d = h.editor.draft;
  h.editor.setTool("Resource"); h.editor.ui["resource-type"].value = "diamond";
  h.down(h.cell(2, 2));
  assert.equal(d.definition.resources[0].type.id, "diamond");
  assert.match(h.cell(2, 2).textContent, /💎/);
  const count = h.editor.history.past.length;
  h.down(h.cell(2, 2)); assert.match(h.editor.ui.status.textContent, /refused/);
  assert.equal(h.editor.history.past.length, count);
  h.editor.setTool("Select"); h.down(h.cell(2, 2));
  change(inputField(h.editor.ui.properties.children[0], "Resource Type"), "shell");
  h.submit(h.editor.ui.properties.children[0]);
  assert.equal(d.definition.resources[0].type.id, "shell");
  press(h.editor.ui.properties, "Move Resource on board"); h.down(h.cell(3, 3));
  assert.deepEqual(plain(d.definition.resources[0].position), { x: 3, y: 3 });
  const recovered = harness(undefined, undefined, storage);
  assert.deepEqual(plain(recovered.editor.draft.definition.resources), plain(d.definition.resources));
  h.click("undo"); assert.deepEqual(plain(d.definition.resources[0].position), { x: 2, y: 2 });
  h.click("redo"); assert.deepEqual(plain(d.definition.resources[0].position), { x: 3, y: 3 });
  h.down(h.cell(3, 3)); press(h.editor.ui.properties, "Remove Resource");
  assert.equal(d.definition.resources.length, 0); h.click("undo"); assert.equal(d.definition.resources.length, 1);
});

test("Unit moves use supplied multi-Cell geometry, preserve identity and references and cancel with Escape", () => {
  const h = harness(), d = h.editor.draft;
  const key = d.place("opaque-large", "blue", "Human", { x: 0, y: 0 });
  d.definition.units[0].unitId = "giant";
  d.setObjective("blue", { objective: "DefeatUnits", count: 1, selectors: [{ kind: "Unit", unitId: "giant" }] });
  h.editor.render(); h.down(h.cell(1, 1));
  const before = h.editor.history.past.length;
  press(h.editor.ui.properties, "Move Unit on board"); h.move(h.cell(3, 2));
  assert.match(h.cell(4, 3).className, /placement-preview/);
  h.down(h.cell(3, 2)); assert.equal(h.editor.history.past.length, before + 1);
  assert.equal(d.unitKeys[0], key); assert.equal(d.definition.units[0].unitId, "giant");
  assert.equal(d.definition.objectives[0].selectors[0].unitId, "giant");
  h.click("undo"); assert.deepEqual(plain(d.definition.units[0].anchor), { x: 0, y: 0 });
  h.down(h.cell(0, 0)); press(h.editor.ui.properties, "Move Unit on board");
  h.documentListeners.keydown({ key: "Escape" }); h.down(h.cell(3, 3));
  assert.deepEqual(plain(d.definition.units[0].anchor), { x: 0, y: 0 });
});

test("Objectives author all types, selectors, counts, rectangles and stable board-picked Unit references", () => {
  const storage = memoryStorage(), h = harness(undefined, undefined, storage), d = h.editor.draft;
  d.place("grunt-type", "blue", "Human", { x: 0, y: 0 });
  d.place("grunt-type", "red", "Automated", { x: 4, y: 4 }); h.editor.render();
  let f = openObjective(h);
  change(inputField(f, "Objective type"), "ReachArea");
  f = objectiveForm(h); press(f, "Add selection"); f = objectiveForm(h);
  assert.equal(inputField(f, "Selection 1 · select").value, "SideAtStart");
  change(inputField(f, "Selection 1 · select"), "Unit");
  f = objectiveForm(h); press(f, "Pick Unit on board"); h.down(h.cell(0, 0));
  const id = d.definition.units[0].unitId; assert.ok(id);
  f = objectiveForm(h); press(f, "Draw Objective Area"); h.down(h.cell(2, 1)); h.up(); h.down(h.cell(4, 3)); h.up();
  f = objectiveForm(h); assert.equal(inputField(f, "Area W").value, "3"); h.submit(f);
  assert.deepEqual(plain(d.definition.objectives[0]), { sideId: "blue", objective: "ReachArea", count: 0, selectors: [{ kind: "Unit", unitId: id }], area: { x: 2, y: 1, w: 3, h: 3 } });
  assert.ok(h.editor.ui.board.children.some(n => n.className.includes("objective-area")));
  assert.ok(h.editor.ui.board.children.some(n => n.className.includes("objective-reference")));
  f = objectiveForm(h); change(inputField(f, "Required Units"), "Minimum"); enter(inputField(f, "Minimum count"), "2");
  press(f, "Add selection"); f = objectiveForm(h); change(inputField(f, "Selection 2 · select"), "Group");
  f = objectiveForm(h); change(inputField(f, "Selection 2 · Group"), JSON.stringify(["grunt-type", "red"]));
  h.submit(f); assert.equal(d.definition.objectives[0].count, 2);
  assert.equal(d.definition.objectives[0].selectors[1].kind, "Group");
  f = objectiveForm(h); change(inputField(f, "Objective type"), "DefeatUnits"); h.submit(objectiveForm(h));
  assert.equal(d.definition.objectives[0].area, undefined);
  const exported = plain(d.snapshot());
  const recovered = harness(undefined, undefined, storage);
  assert.deepEqual(plain(recovered.editor.draft.snapshot()), exported);
  f = objectiveForm(h); press(f, "Copy Objective");
  assert.equal(d.definition.objectives.find(o => o.sideId === "red").count, 2);
  f = objectiveForm(h); change(inputField(f, "Objective type"), "NoObjective"); h.submit(objectiveForm(h));
  assert.equal(d.definition.objectives.find(o => o.sideId === "blue").objective, "NoObjective");
  f = objectiveForm(h); change(inputField(f, "Objective type"), "EliminateEnemies"); h.submit(objectiveForm(h));
  assert.equal(d.definition.objectives.find(o => o.sideId === "blue").objective, "EliminateEnemies");
  press(objectiveForm(h), "Use default Objective"); assert.equal(d.definition.objectives.some(o => o.sideId === "blue"), false);
});

test("incomplete Objective inputs recover, selector removal shifts identities and drag rectangle is one authored edit", () => {
  const storage = memoryStorage(), h = harness(undefined, undefined, storage), d = h.editor.draft;
  d.setAgency("grunt-type", "blue", "Human"); h.editor.render();
  let f = openObjective(h); change(inputField(f, "Objective type"), "ReachArea");
  f = objectiveForm(h); press(f, "Add selection"); f = objectiveForm(h); press(f, "Add selection");
  f = objectiveForm(h); enter(inputField(f, "Selection 2 · Side"), "missing-side");
  press(f.querySelectorAll("div").find(n => n.className === "designer-selector"), "Remove selection");
  f = objectiveForm(h); assert.equal(inputField(f, "Selection 1 · Side").value, "missing-side");
  enter(inputField(f, "Area W"), "-"); h.submit(f);
  assert.equal(d.definition.objectives, undefined);
  const recovered = harness(undefined, undefined, storage); let rf = openObjective(recovered);
  assert.equal(inputField(rf, "Area W").value, "-");
  press(f, "Draw Objective Area"); const count = h.editor.history.past.length;
  h.down(h.cell(1, 1)); h.move(h.cell(3, 2)); h.up();
  assert.equal(h.editor.history.past.length, count);
  f = objectiveForm(h); h.submit(f);
  assert.equal(h.editor.history.past.length, count + 1);
  assert.deepEqual(plain(d.definition.objectives[0].area), { x: 1, y: 1, w: 3, h: 2 });
  assert.throws(() => d.resize(2, 2), /Shrinking/);
});

test("group inventory uses effective capacities, preserves unplaced groups and unsupported imported stores", () => {
  const h = harness(), d = h.editor.draft;
  const meta = d.catalog.unitTypes.find(t => t.unitTypeId === "unplaced");
  const previous = meta.resourceStorage; meta.resourceStorage = { capacities: [{ type: catalog.resourceTypes[1], capacity: 3 }] };
  try {
    d.setAgency("unplaced", "blue", "Human");
    d.definition.storedResources = [{ unitTypeId: "unplaced", sideId: "blue", type: { id: "mystery", name: "Mystery" }, quantity: 2 }]; h.editor.render();
    let f = h.editor.ui.agency.children[0]; enter(inputField(f, "Stored Diamond (capacity 3)"), "2"); h.submit(f);
    assert.equal(d.definition.units.length, 0); assert.equal(d.definition.storedResources.length, 2);
    assert.equal(d.definition.storedResources[1].quantity, 2);
    f = h.editor.ui.agency.children[0]; enter(inputField(f, "Stored Diamond (capacity 3)"), "4"); const before = plain(d.snapshot()); h.submit(f);
    assert.deepEqual(plain(d.snapshot()), before); assert.match(h.editor.ui.status.textContent, /refused/);
    h.editor.render(); f = h.editor.ui.agency.children[0]; press(f, "Discard unfinished inputs");
    h.click("undo"); assert.equal(d.definition.storedResources.length, 1);
    h.click("redo"); assert.equal(d.definition.storedResources.length, 2);
  } finally { meta.resourceStorage = previous; }
});

test("custom metadata overrides canonical geometry, ignores stale requests and preserves opaque content", async () => {
  const pending = [], h = harness(undefined, undefined, undefined, body => new Promise(resolve => pending.push({ body, resolve }))), d = h.editor.draft;
  d.place("grunt-type", "blue", "Human", { x: 0, y: 0 });
  d.definition.unitTypes = [{ id: "grunt-type", hp: 12, opaque: { retain: true } }]; h.editor.render();
  assert.equal(d.metadata("grunt-type"), undefined);
  d.definition.unitTypes[0].hp = 13; h.editor.render();
  const metadata = { ...catalog.unitTypes[0], unitTypeId: "grunt-type", maxHp: 13, cellSpan: 2, footprintOffsets: [{ x: 0, y: 0 }, { x: 1, y: 1 }] };
  pending.at(-1).resolve({ unitTypes: [metadata] }); await Promise.resolve(); await Promise.resolve();
  pending.at(-2).resolve({ unitTypes: [{ ...metadata, maxHp: 12 }] }); await Promise.resolve(); await Promise.resolve();
  assert.equal(d.metadata("grunt-type").maxHp, 13); assert.equal(d.unitsAt({ x: 1, y: 1 }).length, 1);
  assert.deepEqual(plain(d.definition.unitTypes[0].opaque), { retain: true });
});

test("picking and deletion freeze generated identities without retargeting imported Objective or Order references", () => {
  const h = harness(), d = h.editor.draft;
  const a = d.place("grunt-type", "blue", "Human", { x: 0, y: 0 });
  const b = d.place("grunt-type", "red", "Human", { x: 4, y: 4 });
  d.setObjective("blue", { objective: "DefeatUnits", count: 1, selectors: [{ kind: "Unit", unitId: "scenario-unit-2" }] });
  assert.throws(() => d.removeUnit(a), /identities are still loading/);
  d.effectiveUnitIds = ["scenario-unit-1", "scenario-unit-2"];
  assert.equal(d.stableUnitId(b), "scenario-unit-2");
  assert.equal(d.definition.units[0].unitId, undefined);
  d.removeUnit(a); assert.equal(d.definition.units[0].unitId, "scenario-unit-2");
  assert.equal(d.definition.objectives[0].selectors[0].unitId, "scenario-unit-2");
});

test("Objective Apply retains unknown data and imported selector and count semantics", () => {
  const h = harness(), d = h.editor.draft;
  d.setAgency("grunt-type", "blue", "Human");
  const original = { sideId: "blue", objective: "ReachArea", count: 0, custom: { retained: true },
    selectors: [{ kind: "Group", unitTypeId: "grunt-type", sideId: "blue", extra: "retained" }, { kind: "SideAtStart", sideId: "blue" }], area: { x: 1, y: 1, w: 2, h: 2 } };
  d.setObjective("blue", original); h.editor.render(); h.submit(openObjective(h));
  assert.deepEqual(plain(d.definition.objectives[0]), original);
});

test("Order destinations, Door edges and Waypoint rectangles use shared buffered board picking", () => {
  const h = harness(), d = h.editor.draft;
  d.setAgency("grunt-type", "blue", "Human"); h.editor.render();
  let form = h.editor.ui.agency.children[0]; change(inputField(form, "Starting Order"), "MoveTo");
  press(form, "Pick Destination on board"); h.down(h.cell(4, 3)); form = h.editor.ui.agency.children[0]; h.submit(form);
  assert.deepEqual(plain(d.definition.orders[0].destination), { x: 4, y: 3 });
  form = h.editor.ui.agency.children[0]; change(inputField(form, "Starting Order"), "InteractWith");
  press(form, "Pick Door edge"); h.down(h.edge(2, 1, "Down")); form = h.editor.ui.agency.children[0]; h.submit(form);
  assert.deepEqual(plain(d.definition.orders[0].target.door), { a: { x: 2, y: 1 }, b: { x: 2, y: 2 } });
  form = h.editor.ui.routes.children.find(n => n.dataset.formKey === "waypoint:new");
  enter(inputField(form, "Waypoint ID"), "drawn"); press(form, "Draw Waypoint Area");
  h.down(h.cell(3, 2)); h.move(h.cell(1, 1)); h.up();
  form = h.editor.ui.routes.children.find(n => n.dataset.formKey === "waypoint:new"); h.submit(form);
  assert.deepEqual(plain(d.definition.waypoints[0]), { id: "drawn", area: { x: 1, y: 1, w: 3, h: 2 } });
});

test("imported Machines survive validation, export and play and remain selectable in invalid overlaps", async () => {
  const definition = structuredClone(designerSetup);
  definition.machines = [{ id: "press", position: { ...definition.units[0].anchor },
    type: structuredClone(catalog.machineTypes[0]), storedResources: [{ type: { id: "coal", name: "Coal" }, quantity: 3 }] }];
  const submitted = [], played = [];
  const h = harness(async (operation, body) => {
    submitted.push({ operation, body: plain(body) });
    return operation === "import" ? structuredClone(definition) : operation === "validate"
      ? { valid: false, errors: [{ message: "Machine overlap", path: "machines[0]" }] } : { transport: "DELVE1:machines" };
  }, async transport => { played.push(transport); return true; });
  await h.click("import");
  assert.deepEqual(plain(h.editor.draft.snapshot()), definition);
  h.down(h.cell(definition.units[0].anchor.x, definition.units[0].anchor.y));
  const chooser = h.editor.ui.properties.querySelectorAll("button");
  chooser.find(node => node.textContent.includes("Diamond Press")).listeners.click();
  assert.match(h.editor.ui.properties.textContent, /Apply Machine/);
  await h.click("validate");
  h.editor.ui.errors.querySelectorAll("button")[0].listeners.click();
  assert.match(h.editor.ui.properties.textContent, /Apply Machine/);
  await h.click("export"); await h.click("play");
  for (const request of submitted.filter(row => row.operation !== "import"))
    assert.deepEqual(request.body.machines, definition.machines);
  assert.deepEqual(played, ["DELVE1:machines"]);
});

test("designer edits optional stable Unit IDs independently of temporary selection keys", () => {
  const h = harness(), draft = h.editor.draft;
  const key = draft.place("goblin-type", "A", "Human", { x: 0, y: 0 }); h.editor.render(); h.down(h.cell(0, 0));
  let form = h.editor.ui.properties.children[0];
  inputField(form, "Unit ID (optional; blank = generated)").value = "escort-leader"; h.submit(form);
  assert.equal(draft.definition.units[0].unitId, "escort-leader");
  assert.equal(draft.unitKeys[0], key);
  assert.match(h.editor.ui.placements.textContent, /escort-leader/);
  form = h.editor.ui.properties.children[0];
  inputField(form, "Unit ID (optional; blank = generated)").value = ""; h.submit(form);
  assert.equal(draft.definition.units[0].unitId, null);
});

test("group editor authors every starting Order and removes irrelevant target fields", () => {
  const h = harness(), draft = h.editor.draft;
  draft.setAgency("goblin-type", "A", "Automated"); h.editor.render();
  const apply = (kind, fill = () => {}) => {
    const form = h.editor.ui.agency.children[0], select = inputField(form, "Starting Order");
    select.value = kind; select.listeners.change(); fill(form); h.submit(form);
    return draft.definition.orders?.[0] ? plain(draft.definition.orders[0]) : undefined;
  };
  const group = { unitTypeId: "goblin-type", sideId: "A" };
  for (const kind of ["DoNothing", "HoldPosition", "Retreat"])
    assert.deepEqual(apply(kind), { ...group, kind });
  assert.deepEqual(apply("MoveTo", form => {
    inputField(form, "Destination X").value = "4"; inputField(form, "Destination Y").value = "7";
  }), { ...group, kind: "MoveTo", destination: { x: 4, y: 7 } });
  for (const kind of ["Attack", "Protect"])
    assert.deepEqual(apply(kind, form => { inputField(form, "Target Unit ID").value = "leader"; }),
      { ...group, kind, target: { kind: "Unit", unitId: "leader" } });
  assert.deepEqual(apply("Attack", form => {
    const scope = inputField(form, "Target scope"); scope.value = "Group"; scope.listeners.change();
    inputField(form, "Target Unit Type").value = "wizard-type"; inputField(form, "Target SideId").value = "B";
  }), { ...group, kind: "Attack", target: { kind: "Group", group: { typeId: "wizard-type", sideId: "B" } } });
  assert.deepEqual(apply("Attack", form => {
    const scope = inputField(form, "Target scope"); scope.value = "Side"; scope.listeners.change();
    inputField(form, "Target SideId").value = "B";
  }), { ...group, kind: "Attack", target: { kind: "Side", sideId: "B" } });
  assert.deepEqual(apply("InteractWith", form => {
    inputField(form, "Door endpoint A X").value = "5"; inputField(form, "Door endpoint A Y").value = "3";
    inputField(form, "Door endpoint B X").value = "4"; inputField(form, "Door endpoint B Y").value = "3";
  }), { ...group, kind: "InteractWith", target: { kind: "Door", door: { a: { x: 4, y: 3 }, b: { x: 5, y: 3 } } } });
  apply("Normal"); assert.equal(draft.definition.orders, undefined);
});

test("editing or removing a group carries or removes its starting Order", () => {
  const h = harness(), draft = h.editor.draft;
  draft.setAgency("goblin-type", "A", "Automated"); draft.setOrder("goblin-type", "A", { kind: "HoldPosition" });
  h.editor.render(); const form = h.editor.ui.agency.children[0]; inputField(form, "SideId").value = "B"; h.submit(form);
  assert.deepEqual(plain(draft.definition.orders), [{ unitTypeId: "goblin-type", sideId: "B", kind: "HoldPosition" }]);
  draft.removeAgency(0); assert.equal(draft.definition.orders, undefined);
});

test("shared Waypoint/Route editor preserves references, ordering, group progress and export/play", async () => {
  const submitted = [], h = harness(async (operation, body) => {
    submitted.push(plain(body)); return operation === "validate" ? { valid: true, errors: [] } : { transport: "DELVE1:route" };
  });
  const draft = h.editor.draft;
  draft.setAgency("goblin-type", "A", "Automated"); h.editor.render();
  let form = h.editor.ui.routes.querySelectorAll("form")[0];
  inputField(form, "Waypoint ID").value = "north";
  inputField(form, "X").value = "2"; inputField(form, "W").value = "3"; h.submit(form);
  form = h.editor.ui.routes.querySelectorAll("form")[1];
  inputField(form, "Waypoint ID").value = "south";
  inputField(form, "Y").value = "4"; inputField(form, "H").value = "2"; h.submit(form);
  form = h.editor.ui.routes.querySelectorAll("form").find(f => inputField(f, "Route ID"));
  inputField(form, "Route ID").value = "track"; inputField(form, "Route mode").value = "PingPong";
  const add = form.querySelectorAll("button").find(b => b.textContent === "Add existing Waypoint");
  add.listeners.click(); add.listeners.click();
  inputField(form, "Waypoint 2").value = "south"; inputField(form, "Waypoint 2").listeners.change(); h.submit(form);
  form = h.editor.ui.agency.children[0];
  const kind = inputField(form, "Starting Order"); kind.value = "FollowRoute"; kind.listeners.change();
  inputField(form, "Shared Route").value = "track";
  inputField(form, "Current waypoint index (0-based)").value = "1"; inputField(form, "Ping-pong direction").value = "-1"; h.submit(form);
  const expected = { unitTypeId: "goblin-type", sideId: "A", kind: "FollowRoute", route: { routeId: "track", currentIndex: 1, direction: -1 } };
  assert.deepEqual(plain(draft.definition.orders[0]), expected);
  assert.deepEqual(plain(draft.definition.routes[0]), {id:"track", mode:"PingPong", waypointIds:["north","south"]});
  assert.equal(h.editor.ui.board.querySelectorAll(".waypoint-area").length, 2);
  assert.throws(() => draft.removeWaypoint("north"), /Routes/);
  assert.throws(() => draft.removeRoute("track"), /groups/);
  assert.throws(() => draft.resize(2, 2), /Remove or reposition/);
  await h.click("validate"); await h.click("export"); await h.click("play");
  for (const body of submitted) assert.deepEqual(body.orders, [expected]);
  draft.saveWaypoint("north", {id:"renamed", area:{x:2,y:0,w:3,h:1}});
  assert.deepEqual(plain(draft.definition.routes[0].waypointIds), ["renamed","south"]);
  draft.saveRoute("track", {id:"new-track", mode:"Once", waypointIds:["south", "renamed", "south"]}, true);
  assert.deepEqual(plain(draft.definition.orders[0].route), {routeId:"new-track",currentIndex:0,direction:1});
  const saved = plain(draft.snapshot()); draft.replace(saved); h.editor.render();
  assert.deepEqual(plain(draft.snapshot()), saved);
  draft.setOrder("goblin-type", "A", {kind:"Normal"}); draft.removeRoute("new-track"); draft.removeWaypoint("renamed");
  assert.equal(draft.definition.waypoints.length, 1);
});
test("imported Orders and IDs survive unrelated editing, validation, export and play", async () => {
  const definition = structuredClone(designerSetup), submitted = [], played = [];
  definition.units[0].unitId = "stable-leader";
  definition.orders = [{ unitTypeId: definition.agency[0].unitTypeId, sideId: definition.agency[0].sideId,
    kind: "Protect", target: { kind: "Unit", unitId: "stable-leader" } }];
  const h = harness(async (operation, body) => {
    submitted.push({ operation, body: plain(body) });
    return operation === "import" ? structuredClone(definition) : operation === "validate"
      ? { valid: true, errors: [] } : { transport: "DELVE1:orders-fixture" };
  }, async transport => { played.push(transport); return true; });
  h.editor.ui.transport.value = "DELVE1:orders-fixture"; await h.click("import");
  assert.equal(inputField(h.editor.ui.agency.children[0], "Starting Order").value, "Protect");
  h.submit(h.editor.ui.agency.children[0]);
  h.editor.draft.paintTerrain({ x: 0, y: 0 }, "Grass"); h.editor.render();
  await h.click("validate"); await h.click("export"); await h.click("play");
  for (const { body } of submitted.filter(row => row.operation !== "import")) {
    assert.deepEqual(body.orders, definition.orders); assert.equal(body.units[0].unitId, "stable-leader");
  }
  assert.deepEqual(played, ["DELVE1:orders-fixture"]);
});

test("designer assigns distinct colors to sequential sides and retains them after edits", () => {
  const h = harness(), draft = h.editor.draft;
  draft.place("opaque-large", "side-1", "Human", { x: 1, y: 1 });
  draft.place("opaque-large", "side-2", "Human", { x: 4, y: 1 });
  h.editor.render();
  const hues = () => h.editor.ui.board.querySelectorAll(".figure").map(node => node.style["--side-hue"]);
  const before = hues(), gap = Math.abs(before[0] - before[1]);
  assert.ok(Math.min(gap, 360 - gap) >= 90);
  draft.place("opaque-large", "side-1", "Human", { x: 7, y: 1 });
  h.editor.render();
  assert.deepEqual(hues(), [...before, before[0]]);
  // More sides than palette entries must receive new colors rather than cycling.
  for (let i = 3; i <= 12; i++) draft.place("opaque-large", `side-${i}`, "Human", { x: 1, y: 4 });
  h.editor.render();
  assert.equal(new Set(hues()).size, 12);
});

test("terrain drag upserts sparse overrides and Use default removes them", () => {
  const h = harness(); h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Tree";
  h.down(h.cell(1, 1)); h.move(h.cell(2, 1)); h.move(h.cell(1, 1)); h.up();
  assert.equal(h.editor.draft.definition.board.cells.length, 2);
  assert.match(h.cell(1, 1).className, /Tree/);
  assert.match(h.editor.ui.status.textContent, /Unchecked/);
  h.editor.ui.terrain.value = "Water"; h.down(h.cell(1, 1)); h.up();
  assert.equal(h.editor.draft.definition.board.cells.length, 2);
  h.editor.ui.terrain.value = ""; h.down(h.cell(1, 1)); h.up();
  assert.equal(h.editor.draft.definition.board.cells.length, 1);
  assert.match(h.cell(1, 1).className, /StoneFloor/);
});

test("imported legacy Lava remains paintable and survives export", async () => {
  const submitted = [];
  const h = harness(async (operation, body) => { submitted.push(plain(body)); return { transport: "DELVE1:lava" }; });
  h.editor.draft.definition.board.cells = [{position:{x:0,y:0},terrain:'Lava'}];
  h.editor.draft.changed(); h.editor.render();
  assert.ok(h.editor.ui.terrain.children.some(option => option.value === "Lava"));
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Lava";
  h.down(h.cell(1, 1)); h.up();
  assert.match(h.cell(1, 1).className, /Lava/);
  await h.click("export");
  assert.equal(submitted[0].board.cells.find(cell => cell.position.x === 1 && cell.position.y === 1).terrain, "Lava");
});

test('scenario-local Terrain is named, paintable, inspectable and retained through undo, recovery and Playtest', async () => {
  const submitted=[], played=[];
  const h=harness(async(operation,body)=>{submitted.push({operation,body:plain(body)});return{transport:'DELVE1:local-terrain'};},async value=>{played.push(value);return true;});
  const definitions=[{id:'velvet-puzzle',name:'An Unfamiliar Plinth',passable:false,blocksLos:false,presentation:{recipe:'StoneFloorWithTable'}},
    {id:'moon-paper',name:'Moon Paper',passable:true,blocksLos:false,presentation:{recipe:'never-seen-before'}}];
  h.editor.draft.definition.terrainTypes=definitions;h.editor.draft.changed();h.editor.render();
  assert.ok(h.editor.ui.terrain.children.some(o=>o.value==='type:velvet-puzzle'&&o.textContent==='An Unfamiliar Plinth'));
  h.editor.setTool('Terrain');h.editor.ui.terrain.value='type:velvet-puzzle';h.down(h.cell(1,1));h.up();
  assert.equal(h.editor.draft.definition.board.cells[0].terrainTypeId,'velvet-puzzle');
  assert.equal(h.editor.draft.definition.board.cells[0].terrain,undefined);
  assert.match(h.cell(1,1).className,/StoneFloorWithTable/);
  assert.match(h.cell(1,1).title,/An Unfamiliar Plinth/);
  h.click('undo');assert.equal(h.editor.draft.definition.board.cells.length,0);
  h.click('redo');assert.equal(h.editor.draft.definition.board.cells[0].terrainTypeId,'velvet-puzzle');
  h.editor.setTool('Select');h.down(h.cell(1,1));
  assert.match(h.editor.ui.properties.textContent,/Blocks movement.*Does not block LOS/);
  h.editor.draft.paintTerrain({x:2,y:1},'moon-paper');h.editor.render();
  assert.match(h.cell(2,1).className,/custom-terrain/);
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  assert.deepEqual(plain(h.editor.draft.definition.terrainTypes),definitions);
  await h.click('export');await h.click('play');
  assert.deepEqual(submitted[0].body.terrainTypes,definitions);
  assert.equal(submitted[0].body.board.cells[0].terrainTypeId,'velvet-puzzle');
  assert.deepEqual(played,['DELVE1:local-terrain']);
});

test('typed default Terrain survives ordinary default changes without stale ID precedence', () => {
  const h=harness(),draft=h.editor.draft;
  draft.definition.terrainTypes=[{id:'paper',name:'Paper',passable:true,blocksLos:false}];
  draft.setDefaultTerrain('paper');assert.equal(draft.terrainAt({x:0,y:0}),'paper');
  draft.setDefaultTerrain('Grass');assert.equal(draft.definition.board.defaultTerrainTypeId,undefined);
  assert.equal(draft.terrainAt({x:0,y:0}),'Grass');
  draft.setDefaultTerrain('paper');assert.equal(draft.definition.board.defaultTerrain,'StoneFloor');
});

test('fixed assemblies retain authoring references while Engine metadata supplies the combined properties', async () => {
  const submitted = [], played = [], projected = [], storage = memoryStorage();
  const component = {id:'fixture-bed',name:'Folded Bed',blocksMovement:true,blocksLos:false,presentation:{recipe:'UnfamiliarBed',layer:'Object'}};
  const assembly = {id:'fixture-grass-bed',name:'Grassy Rest',surfaceId:'grass',components:[{slot:'furniture',componentId:component.id,quarterTurns:1}]};
  const grass = {id:'grass',name:'Grass',passable:true,blocksLos:false,presentation:{recipe:'Grass'}};
  const resolved = {...grass,id:assembly.id,name:assembly.name,passable:false,
    assembly:{surface:grass,components:[{slot:'furniture',definition:component,quarterTurns:1}]}};
  const metadata = async body => { projected.push(plain(body)); return {unitTypes:catalog.unitTypes,unitIds:[],
    terrainDefinitions:[grass,...(body.terrainAssemblies.length ? [resolved] : [])],fixedComponentDefinitions:body.fixedComponentTypes}; };
  const h = harness(async(operation,body) => {submitted.push(plain(body));return {transport:'DELVE1:assembly'};},
    async transport => {played.push(transport);return true;}, storage, metadata);
  h.editor.draft.definition.fixedComponentTypes = [component];
  h.editor.draft.definition.terrainAssemblies = [assembly];
  h.editor.draft.changed(); h.editor.render();
  assert.ok(h.editor.ui.terrain.children.some(option => option.value === 'type:fixture-grass-bed' && option.textContent === assembly.name));
  h.editor.setTool('Terrain'); h.editor.ui.terrain.value = 'type:fixture-grass-bed'; h.down(h.cell(1,1)); h.up();
  h.editor.setTool('Select'); h.down(h.cell(1,1));
  assert.match(h.editor.ui.properties.textContent,/Effective properties awaiting Engine validation/);
  await new Promise(setImmediate);
  assert.match(h.editor.ui.properties.textContent,/Blocks movement.*Does not block LOS/);
  assert.match(h.editor.ui.properties.textContent,/Surface: Grass/);
  assert.match(h.editor.ui.properties.textContent,/Fixed furniture: Folded Bed.*90°/);
  assert.match(h.cell(1,1).className,/Grass/);
  assert.ok(h.cell(1,1).children.some(child => child.className === 'designer-fixed-component' && child.textContent === component.name));
  assert.deepEqual(projected.at(-1).terrainAssemblies,[assembly]);
  assert.deepEqual(projected.at(-1).fixedComponentTypes,[component]);
  h.click('undo'); assert.equal(h.editor.draft.definition.board.cells.length,0);
  h.click('redo'); assert.equal(h.editor.draft.definition.board.cells[0].terrainTypeId,assembly.id);
  const document = plain(h.editor.documentData());
  h.editor.loadDocument(JSON.stringify(document));
  await new Promise(setImmediate);
  assert.deepEqual(plain(h.editor.draft.definition.terrainAssemblies),[assembly]);
  assert.deepEqual(plain(h.editor.draft.definition.fixedComponentTypes),[component]);
  const recovered = harness(undefined,undefined,storage,metadata);
  await new Promise(setImmediate);
  assert.deepEqual(plain(recovered.editor.draft.definition.terrainAssemblies),[assembly]);
  assert.deepEqual(plain(recovered.editor.draft.definition.fixedComponentTypes),[component]);
  await h.click('export'); await h.click('play');
  assert.ok(submitted.every(draft => JSON.stringify(draft.terrainAssemblies) === JSON.stringify([assembly])));
  assert.ok(submitted.every(draft => JSON.stringify(draft.fixedComponentTypes) === JSON.stringify([component])));
  assert.deepEqual(played,['DELVE1:assembly']);
  recovered.editor.draft.paintTerrain({x:1,y:1},null); recovered.editor.render();
  assert.equal(recovered.editor.draft.definition.board.cells.length,0);
  assert.deepEqual(plain(recovered.editor.draft.definition.terrainAssemblies),[assembly]);
});

test('production metadata is Engine-owned and local declarations survive editing, recovery, export and Playtest', async () => {
  const submitted = [], played = [];
  const type = {id:'unfamiliar-cupboard',name:'Unexpected Cupboard',passable:false,blocksLos:false,
    production:{kind:'directionalResourceProduction',resourceTypeId:'banana',attempts:3,placement:'SurroundingCellD8',sourceLos:'RespectTerrain'}};
  const description = 'At Round Start, make 3 d8 attempts to place one Banana. Respect the producing Cell\'s LOS blocking.';
  const h = harness(async(operation,body) => {submitted.push(plain(body));return {transport:'DELVE1:producer'};},
    async transport => {played.push(transport);return true;}, undefined,
    async body => ({unitTypes:catalog.unitTypes,unitIds:[],terrainDefinitions:body.terrainTypes,
      productionDescriptions:{'unfamiliar-cupboard':description}}));
  h.editor.draft.definition.terrainTypes = [type]; h.editor.draft.changed(); h.editor.render();
  await new Promise(setImmediate);
  h.editor.setTool('Terrain'); h.editor.ui.terrain.value = 'type:unfamiliar-cupboard'; h.down(h.cell(1,1)); h.up();
  h.editor.setTool('Select'); h.down(h.cell(1,1));
  assert.ok(h.editor.ui.properties.textContent.includes(description));
  assert.equal(inputField(h.editor.ui.properties.children[0], 'Coal generation'), undefined);
  h.click('undo'); h.click('redo');
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  await new Promise(setImmediate);
  await h.click('export'); await h.click('play');
  assert.ok(submitted.every(draft => JSON.stringify(draft.terrainTypes) === JSON.stringify([type])));
  assert.ok(submitted.every(draft => draft.board.cells[0].terrainTypeId === type.id));
  assert.deepEqual(played,['DELVE1:producer']);
});

test('separate typed Lava entries never acquire legacy Coal toggles and legacy references resolve the authored setting', () => {
  const keys = ['terrainDefinitions','legacyTerrainMappings','legacyTerrainTypeAliases','productionDescriptions'];
  const saved = Object.fromEntries(keys.map(key => [key,catalog[key]]));
  try {
    catalog.terrainDefinitions = [
      {id:'ordinary-lava',name:'Lava',passable:false,blocksLos:false,legacyKind:'Lava',presentation:{recipe:'Lava'}},
      {id:'coal-lava',name:'Coal Lava',passable:false,blocksLos:false,legacyKind:'Lava',presentation:{recipe:'Lava'},
        production:{kind:'directionalResourceProduction',resourceTypeId:'coal',attempts:2}}];
    catalog.legacyTerrainMappings = [{kind:'Lava',terrainTypeId:'coal-lava'}, {kind:'Lava',terrainTypeId:'ordinary-lava',coalGeneration:false}];
    catalog.legacyTerrainTypeAliases = {lava:'Lava'};
    catalog.productionDescriptions = {'coal-lava':'At Round Start, make 2 d8 attempts to place one Coal.'};
    const h = harness(), draft = h.editor.draft;
    assert.ok(!h.editor.ui.terrain.children.some(option => option.value === 'Lava'));
    assert.ok(!h.editor.ui.default.children.some(option => option.value === 'Lava'));
    for (const id of ['ordinary-lava','coal-lava']) {
      assert.ok(h.editor.ui.terrain.children.some(option => option.value === `type:${id}`));
      draft.paintTerrain({x:1,y:1},`type:${id}`,'East',false);h.editor.render();h.down(h.cell(1,1));
      assert.equal(draft.definition.board.cells[0].coalGeneration,undefined);
      assert.equal(inputField(h.editor.ui.properties.children[0],'Coal generation'),undefined);
      assert.ok(h.editor.ui.properties.textContent.includes(id === 'ordinary-lava' ? 'No environmental resource production.' : catalog.productionDescriptions[id]));
    }
    for (const reference of ['Lava','type:lava']) {
      draft.paintTerrain({x:1,y:1},reference,'East',false); h.editor.render(); h.down(h.cell(1,1));
      assert.ok(h.editor.ui.terrain.children.some(option => option.value === 'Lava'));
      assert.equal(draft.terrainDefinitionAt({x:1,y:1}).id,'ordinary-lava');
      assert.equal(inputField(h.editor.ui.properties.children[0],'Coal generation').value,'Off');
      draft.paintTerrain({x:1,y:1},reference); assert.equal(draft.terrainDefinitionAt({x:1,y:1}).id,'coal-lava');
    }
    draft.paintTerrain({x:1,y:1},null); h.editor.render();
    assert.ok(!h.editor.ui.terrain.children.some(option => option.value === 'Lava'));
    draft.setDefaultTerrain('Lava'); h.editor.render();
    assert.ok(h.editor.ui.default.children.some(option => option.value === 'Lava'));
  } finally { for (const key of keys) catalog[key] = saved[key]; }
});

test('a local Type ID matching a legacy Terrain spelling remains a distinct Designer selection', () => {
  const saved = catalog.terrainDefinitions;
  try {
    catalog.terrainDefinitions=[{id:'grass',name:'Grass',legacyKind:'Grass',presentation:{recipe:'Grass'}}];
    const h=harness(),draft=h.editor.draft;
    draft.definition.terrainTypes=[{id:'Grass',name:'Misleading Identity',passable:false,blocksLos:false,presentation:{recipe:'StoneFloorWithTable'}}];
    draft.setDefaultTerrain('Grass');h.editor.render();
    assert.equal(draft.terrainNameAt({x:0,y:0}),'Grass');
    h.editor.setTool('Terrain');h.editor.ui.terrain.value='type:Grass';h.down(h.cell(1,1));h.up();
    assert.equal(draft.definition.board.cells[0].terrainTypeId,'Grass');
    assert.equal(draft.terrainNameAt({x:1,y:1}),'Misleading Identity');
    assert.match(h.cell(1,1).className,/StoneFloorWithTable/);
    h.editor.ui.terrain.value='Grass';h.down(h.cell(2,1));h.up();
    assert.equal(draft.definition.board.cells[1].terrain,'Grass');
    draft.setDefaultTerrain('type:Grass');assert.equal(draft.definition.board.defaultTerrainTypeId,'Grass');
  } finally { catalog.terrainDefinitions=saved; }
});

test("Lava Coal setting survives inspector edits, undo, recovery and export", async () => {
  const submitted = [], h = harness(async (operation, body) => { submitted.push(plain(body)); return { transport: "DELVE1:lava" }; });
  h.editor.draft.paintTerrain({x:1,y:1}, "Lava"); h.editor.render();
  h.down(h.cell(1,1));
  let form = h.editor.ui.properties.children[0];
  assert.equal(inputField(form, "Coal generation").value, "On");
  inputField(form, "Coal generation").value = "Off"; h.submit(form);
  assert.equal(h.editor.draft.definition.board.cells[0].coalGeneration, false);
  h.click("undo"); assert.equal(h.editor.draft.definition.board.cells[0].coalGeneration, undefined);
  h.click("redo"); assert.equal(h.editor.draft.definition.board.cells[0].coalGeneration, false);
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  await h.click("export");
  assert.equal(submitted[0].board.cells[0].coalGeneration, false);
  h.down(h.cell(1,1)); form = h.editor.ui.properties.children[0];
  inputField(form, "Override").value = "Grass"; h.submit(form);
  assert.equal(h.editor.draft.definition.board.cells[0].coalGeneration, undefined);
});

test("a default Lava Cell can disable Coal with an explicit Cell override", () => {
  const h = harness(); h.editor.draft.definition.board.defaultTerrain = "Lava"; h.editor.render();
  h.down(h.cell(1,1)); const form = h.editor.ui.properties.children[0];
  inputField(form, "Coal generation").value = "Off"; h.submit(form);
  assert.deepEqual(plain(h.editor.draft.definition.board.cells), [{position:{x:1,y:1},terrain:"Lava",coalGeneration:false}]);
});

test("all empty internal boundaries support canonical Right/Down upsert and removal", () => {
  const h = harness(); h.editor.setTool("Edge"); h.editor.ui.edge.value = "ClosedDoor";
  h.down(h.edge(1, 1, "Right")); h.move(h.edge(1, 2, "Right")); h.up();
  h.editor.ui.edge.value = "WallWithWindow"; h.down(h.edge(1, 1, "Right")); h.up();
  h.down(h.edge(1, 1, "Down")); h.up();
  assert.equal(h.editor.draft.definition.board.edges.length, 3);
  assert.equal(h.editor.draft.definition.board.edges[0].kind, "WallWithWindow");
  h.editor.ui.edge.value = ""; h.down(h.edge(1, 1, "Right")); h.up();
  assert.equal(h.editor.draft.definition.board.edges.length, 2);
  assert.equal(h.edge(14, 0, "Right"), undefined);
  assert.equal(h.edge(0, 14, "Down"), undefined);
});

test("DefaultTerrain controls unspecified cells while explicit redundant overrides remain", () => {
  const h = harness(); h.editor.draft.paintTerrain({ x: 0, y: 0 }, "Grass");
  h.editor.ui.default.value = "Grass"; h.submit(h.editor.ui["board-form"]);
  assert.match(h.cell(1, 1).className, /Grass/);
  assert.equal(h.editor.draft.definition.board.cells.length, 1);
  h.editor.ui.default.value = "Water"; h.submit(h.editor.ui["board-form"]);
  assert.match(h.cell(1, 1).className, /Water/);
  assert.match(h.cell(0, 0).className, /Grass/);
});

test("resize refuses hidden terrain, edge endpoints and supplied Unit footprint without deletion", () => {
  const h = harness(), draft = h.editor.draft;
  draft.resize(50, 50); assert.equal(draft.definition.board.width, 50);
  assert.throws(() => draft.resize(51, 2)); assert.throws(() => draft.resize(0, 2));
  draft.paintTerrain({ x: 49, y: 49 }, "Grass");
  assert.throws(() => draft.resize(49, 50), /hide/); assert.equal(draft.definition.board.cells.length, 1);
  draft.paintTerrain({ x: 49, y: 49 }, null);
  draft.paintEdge({ x: 48, y: 0 }, "Right", "Wall");
  assert.throws(() => draft.resize(49, 50), /hide/);
  draft.paintEdge({ x: 48, y: 0 }, "Right", null);
  const key = draft.place("opaque-large", "side", "Human", { x: 48, y: 0 });
  assert.throws(() => draft.resize(49, 50), /hide/);
  draft.removeUnit(key); draft.resize(49, 50);
  // An invalid out-of-bounds placement remains editable and does not prevent expansion.
  draft.place("opaque-large", "side", "Human", { x: 49, y: 49 }); draft.resize(50, 50);
});

test("refused board shrink retains the draft and does not claim a Delve validity result", () => {
  const h = harness(); h.editor.draft.paintTerrain({ x: 14, y: 14 }, "Tree"); h.editor.render();
  const before = plain(h.editor.draft.snapshot());
  h.editor.ui.width.value = "14"; h.submit(h.editor.ui["board-form"]);
  assert.deepEqual(plain(h.editor.draft.snapshot()), before);
  assert.match(h.editor.ui.status.textContent, /Edit refused/);
  assert.equal(h.editor.ui.width.value, "14"); // Retain the rejected input so the author can repair it.
});

test("Unit tool places supplied 2x2 geometry; all occupied cells select one temporary identity", () => {
  const h = harness(); h.editor.setTool("Unit"); h.editor.ui.type.value = "opaque-large";
  h.editor.ui.side.value = "arbitrary / Side — blå"; h.editor.ui.controller.value = "Automated";
  h.move(h.cell(1, 1)); assert.equal(h.editor.ui.board.querySelectorAll(".placement-preview").length, 4);
  h.down(h.cell(1, 1));
  const key = h.editor.draft.unitKeys[0];
  const figure = h.editor.ui.board.children.find(node => node.dataset.unitKey === key);
  assert.equal(figure.dataset.cellSpan, 2); assert.match(figure.className, /large/);
  assert.equal(figure.style.left, `${2 / 15 * 100}%`);
  h.editor.setTool("Select"); h.down(h.cell(2, 2));
  assert.match(h.editor.ui.properties.textContent, /Initial HP/);
  assert.equal(h.editor.draft.definition.units[0].sideId, "arbitrary / Side — blå");
  assert.equal(h.editor.draft.definition.units[0].initialHp, null);
});

test("selected Unit property form edits anchor, Side, Posture and HP without reordering placements", () => {
  const h = harness(), draft = h.editor.draft;
  const first = draft.place("opaque-large", "A", "Human", { x: 1, y: 1 });
  const second = draft.place("goblin-type", "A", "Automated", { x: 4, y: 1 }); h.editor.render();
  h.editor.setTool("Select"); h.down(h.cell(1, 1));
  let form = h.editor.ui.properties.children[0], fields = form.children.filter(node => node.tagName === "label").map(node => node.children[0]);
  fields[1].value = "new arbitrary Side"; fields[2].value = "5"; fields[3].value = "6"; fields[4].value = "Lying"; fields[5].value = "3";
  h.submit(form);
  assert.deepEqual(plain(draft.unitKeys), [first, second]);
  assert.deepEqual(plain(draft.definition.units[0].anchor), { x: 5, y: 6 });
  assert.equal(draft.definition.units[0].posture, "Lying"); assert.equal(draft.definition.units[0].initialHp, 3);
  form = h.editor.ui.properties.children[0]; fields = form.children.filter(node => node.tagName === "label").map(node => node.children[0]);
  fields[5].value = ""; h.submit(form); assert.equal(draft.definition.units[0].initialHp, null);
  h.editor.ui.properties.querySelectorAll("button").find(node => node.textContent === "Delete Unit").listeners.click();
  assert.equal(draft.definition.units.length, 1); assert.equal(draft.unitKeys[0], second);
  assert.ok(draft.definition.unitTypeIds.includes("opaque-large"));
  assert.ok(draft.definition.agency.some(row => row.sideId === "new arbitrary Side"));
});

test("invalid overlapping placements and impassable terrain remain editable with a Unit chooser", () => {
  const h = harness(), draft = h.editor.draft;
  draft.place("opaque-large", "A", "Human", { x: 1, y: 1 });
  draft.place("goblin-type", "B", "Automated", { x: 2, y: 2 });
  draft.paintTerrain({ x: 2, y: 2 }, "Tree"); h.editor.render(); h.down(h.cell(2, 2));
  assert.match(h.editor.ui.properties.textContent, /Overlapping/);
  const chooser = h.editor.ui.properties.querySelectorAll("button"); assert.equal(chooser.length, 3);
  chooser[1].listeners.click(); assert.match(h.editor.ui.properties.textContent, /Apply Unit/);
  assert.equal(draft.definition.units.length, 2); assert.equal(draft.occupied(draft.definition.units[0]).length, 4);
});

test("agency is pair-specific, reuses existing pairs and retains zero-placement rows", () => {
  const h = harness(), draft = h.editor.draft;
  draft.setAgency("goblin-type", "same", "Automated");
  const one = draft.place("goblin-type", "same", "Human", { x: 0, y: 0 });
  draft.place("wizard-type", "same", "Human", { x: 1, y: 0 });
  assert.equal(draft.definition.agency[0].controller, "Automated");
  draft.setAgency("goblin-type", "same", "Human");
  draft.setAgency("goblin-type", "other", "Automated");
  draft.editUnit(one, { sideId: "other" }, "Human");
  assert.equal(draft.definition.agency.find(row => row.sideId === "other").controller, "Automated");
  draft.removeUnit(one); assert.equal(draft.definition.agency.length, 3);
  h.editor.render();
  const form = h.editor.ui.agency.children[0], fields = form.children.filter(node => node.tagName === "label").map(node => node.children[0]);
  fields[2].value = "Automated"; h.submit(form);
  assert.equal(draft.definition.agency[0].controller, "Automated");
  assert.equal(draft.definition.agency.find(row => row.unitTypeId === "wizard-type").controller, "Human");
});

test("first-use declarations preserve order, unplaced declarations and imported data exactly", () => {
  const h = harness(), draft = h.editor.draft;
  draft.declare("unplaced");
  const key = draft.place("goblin-type", "A", "Human", { x: 0, y: 0 });
  draft.place("wizard-type", "A", "Human", { x: 1, y: 0 }); draft.removeUnit(key);
  assert.deepEqual(plain(draft.definition.unitTypeIds), ["unplaced", "goblin-type", "wizard-type"]);
  const imported = structuredClone(designerSetup);
  imported.unitTypeIds.unshift("unplaced"); imported.unitTypeIds.reverse();
  imported.agency.push({ unitTypeId: "unplaced", sideId: "future", controller: "Automated" });
  imported.board.edges.push({ position: { x: 0, y: 0 }, direction: "Right", kind: "None" });
  imported.board.cells.push({ position: { x: 0, y: 0 }, terrain: "StoneFloor" });
  imported.units[0].initialHp = null; imported.units[1].initialHp = 2;
  draft.replace(imported); h.editor.render();
  assert.deepEqual(plain(draft.snapshot()), imported);
  assert.match(h.editor.ui.order.textContent, /unplaced/);
});

test("successful test-fixture import renders concrete cells, edges, placements and order faithfully", async () => {
  const h = harness(async operation => operation === "import" ? structuredClone(designerSetup) : { valid: true, errors: [] });
  h.editor.ui.transport.value = "DELVE1:server-decoded-fixture"; await h.click("import");
  assert.deepEqual(plain(h.editor.draft.snapshot()), designerSetup);
  assert.equal(h.editor.ui.board.querySelectorAll(".cell").length, 30);
  assert.equal(h.editor.ui.board.querySelectorAll(".figure").length, designerSetup.units.length);
  assert.match(h.cell(4, 3).className, /Tree/);
  assert.match(h.edge(3, 1, "Right").className, /ClosedDoor/);
  assert.equal(h.editor.ui.order.children.length, designerSetup.unitTypeIds.length);
  assert.match(h.editor.ui.status.textContent, /Valid/);
});

test("failed import and invalid validation preserve draft; structured errors select the offending Unit", async () => {
  const h = harness(async operation => {
    if (operation === "import") throw new Error("Malformed DELVE1");
    return { valid: false, errors: [{ message: "Authoritative placement error", path: "units[0]" }] };
  });
  h.editor.draft.place("goblin-type", "side", "Human", { x: 0, y: 0 }); h.editor.render();
  const before = plain(h.editor.draft.snapshot()); await h.click("import"); assert.deepEqual(plain(h.editor.draft.snapshot()), before);
  await h.click("validate"); assert.match(h.editor.ui.errors.textContent, /Authoritative placement error/);
  h.editor.ui.errors.querySelectorAll("button")[0].listeners.click(); assert.match(h.editor.ui.properties.textContent, /Apply Unit/);
});

for (const operation of ["validate", "export", "import", "play"]) {
  test(`stale ${operation} response cannot mark, replace, export or play a newer draft`, async () => {
    let resolve, played = 0;
    const h = harness(() => new Promise(done => { resolve = done; }), async () => { played++; return true; });
    const pending = h.click(operation);
    h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Tree"; h.down(h.cell(1, 1)); h.up();
    resolve(operation === "import" ? structuredClone(designerSetup) : operation === "validate" ? { valid: true, errors: [] } : { transport: "DELVE1:old" });
    await pending;
    assert.match(h.editor.ui.status.textContent, /Unchecked/); assert.equal(h.editor.ui.transport.value, ""); assert.equal(played, 0);
    assert.equal(h.editor.draft.definition.units.length, 0);
  });
}

test("latest validation request wins even when the draft has not changed", async () => {
  const pending = [], h = harness(() => new Promise(done => pending.push(done)));
  const first = h.click("validate"), second = h.click("validate");
  pending[1]({ valid: false, errors: [{ message: "latest" }] }); await second;
  pending[0]({ valid: true, errors: [] }); await first;
  assert.match(h.editor.ui.status.textContent, /Invalid/); assert.match(h.editor.ui.errors.textContent, /latest/);
});

test("Play design exports through server, passes DELVE1 to existing client hook, and retains the draft", async () => {
  const operations = [], plays = [];
  const h = harness(async (operation, body) => { operations.push([operation, plain(body)]); return { transport: "DELVE1:authoritative" }; },
    async transport => { plays.push(transport); return true; });
  h.editor.draft.replace(designerSetup); h.editor.render(); const before = plain(h.editor.draft.snapshot());
  await h.click("play"); assert.deepEqual(operations, [["export", before]]); assert.deepEqual(plays, ["DELVE1:authoritative"]);
  assert.deepEqual(plain(h.editor.draft.snapshot()), before);
  h.editor.setMode(true); assert.deepEqual(plain(h.editor.draft.snapshot()), before);
  assert.equal(h.editor.ui.main.inert, false);
});

test("invalid export prevents Play design", async () => {
  let played = false;
  const h = harness(async () => { const error = new Error("invalid"); error.errors = [{ message: "Invalid HP", path: "units[0]" }]; throw error; },
    async () => { played = true; });
  await h.click("play"); assert.equal(played, false); assert.match(h.editor.ui.errors.textContent, /Invalid HP/);
});


test("World Effects controls enable, configure and disable concrete scenario settings", () => {
  const h = harness(), ui = h.editor.ui;
  assert.equal(ui["world-enabled"].checked, false);
  assert.equal(ui["world-draws"].disabled, true);
  ui["world-enabled"].checked = true;
  ui["world-enabled"].listeners.change();
  assert.equal(ui["world-draws"].disabled, false);
  ui["world-draws"].value = "3"; ui["world-cycling"].value = "2";
  ui["world-form"].listeners.submit({ preventDefault() {} });
  assert.deepEqual(plain(h.editor.draft.snapshot().worldEffects), { cardsPerRound: 3, cycling: 2 });
  assert.match(ui.status.textContent, /Unchecked/);
  ui["world-enabled"].checked = false;
  ui["world-form"].listeners.submit({ preventDefault() {} });
  assert.equal(h.editor.draft.snapshot().worldEffects, undefined);
  assert.equal(ui["world-cycling"].disabled, true);
});

test("World Effects settings survive import, edits, validation, export and play", async () => {
  const definition = structuredClone(designerSetup); definition.worldEffects = { cardsPerRound: 2, cycling: 3 };
  const submitted = [], played = [];
  const h = harness(async (operation, body) => {
    submitted.push({ operation, body: plain(body) });
    return operation === "import" ? structuredClone(definition) : operation === "validate"
      ? { valid: true, errors: [] } : { transport: "DELVE1:world-fixture" };
  }, async transport => { played.push(transport); return true; });
  h.editor.ui.transport.value = "DELVE1:world-fixture"; await h.click("import");
  assert.equal(h.editor.ui["world-enabled"].checked, true);
  assert.equal(h.editor.ui["world-draws"].value, "2");
  assert.equal(h.editor.ui["world-cycling"].value, "3");
  h.editor.draft.paintTerrain({ x: 0, y: 0 }, "Grass"); h.editor.render();
  await h.click("validate"); await h.click("export"); await h.click("play");
  for (const request of submitted.filter(row => row.operation !== "import"))
    assert.deepEqual(request.body.worldEffects, definition.worldEffects);
  assert.deepEqual(played, ["DELVE1:world-fixture"]);
});

test("local World Cards survive World settings edits, recovery, export and play", async () => {
  const definition = structuredClone(designerSetup);
  definition.worldCardDefinitions = [
    { id: "paper-wind", name: "Paper Wind", lifecycle: "Continuous", modifiers: [{ target: "Mov", amount: 2 }] },
    { id: "silver-rain", name: "Silver Rain", lifecycle: "Immediate", immediate: { kind: "healDamagedUnits", amount: 2 } }
  ];
  definition.worldEffects = { cardsPerRound: 2, cycling: 1,
    composition: [{ typeId: "paper-wind", count: 2 }, { typeId: "silver-rain", count: 1 }] };
  const submitted = [], played = [];
  const h = harness(async (operation, body) => {
    submitted.push({ operation, body: plain(body) });
    return operation === "import" ? structuredClone(definition) : operation === "validate"
      ? { valid: true, errors: [] } : { transport: "DELVE1:local-world" };
  }, async transport => { played.push(transport); return true; });
  h.editor.ui.transport.value = "DELVE1:local-world"; await h.click("import");
  h.editor.ui["world-draws"].value = "3";
  h.editor.ui["world-cycling"].value = "2";
  h.editor.ui["world-form"].listeners.submit({ preventDefault() {} });
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  await h.click("validate"); await h.click("export"); await h.click("play");
  for (const request of submitted.filter(row => row.operation !== "import")) {
    assert.deepEqual(request.body.worldCardDefinitions, definition.worldCardDefinitions);
    assert.deepEqual(request.body.worldEffects, { ...definition.worldEffects, cardsPerRound: 3, cycling: 2 });
  }
  assert.deepEqual(played, ["DELVE1:local-world"]);
});


test("owned Abilities remain nested through import, editing, recovery, export and play", async () => {
  const definition = structuredClone(designerSetup);
  definition.unitTypes = [{ id: "woven-keeper", displayName: "Woven Keeper", mov: 2, rng: 1, atk: 0, def: 1, hp: 3,
    localAbilities: [
      { key: "pulse", name: "Silver Stitch", timing: "Action", usage: { maxUses: 2 }, mechanic: { kind: "heal", amount: 2 } },
      { key: "shade", name: "Woven Shade", timing: "Passive", mechanic: { kind: "adjacentModifiers", modifiers: [{ stat: "Def", amount: 2 }] } }
    ] }];
  definition.equipmentTypes = [{ id: "hollow-lantern", name: "Hollow Lantern",
    localAbilities: [{ key: "pulse", name: "Lantern Hush", timing: "Action", mechanic: { kind: "holyWave" } }] }];
  definition.itemTypes = [{ id: "folded-remedy", name: "Folded Remedy",
    localAbilities: [{ key: "pulse", name: "Paper Stitch", timing: "Action", mechanic: { kind: "heal", amount: 3 } }] },
    { id: "spare-moment", name: "Spare Moment",
      localAbilities: [{ key: "pulse", name: "Borrow a Moment", timing: "BonusAction", mechanic: { kind: "extraActions", amount: 1 } }] }];
  definition.unitTypeIds.push("woven-keeper");
  definition.units.push({ unitTypeId: "woven-keeper", sideId: "amber", unitId: "keeper", anchor: { x: 1, y: 1 },
    inventory: [{ id: "lantern", equipmentTypeId: "hollow-lantern" },
      { id: "remedy-one", itemTypeId: "folded-remedy" }, { id: "remedy-two", itemTypeId: "folded-remedy" },
      { id: "moment-one", itemTypeId: "spare-moment" }, { id: "moment-two", itemTypeId: "spare-moment" }] });
  definition.agency.push({ unitTypeId: "woven-keeper", sideId: "amber", controller: "Human" });
  const storage = memoryStorage(), submitted = [], played = [];
  const h = harness(async (operation, body) => {
    submitted.push({ operation, body: plain(body) });
    return operation === "import" ? structuredClone(definition) : operation === "validate"
      ? { valid: true, errors: [] } : { transport: "DELVE1:owned-abilities" };
  }, async transport => { played.push(transport); return true; }, storage);
  h.editor.ui.transport.value = "DELVE1:owned-abilities"; await h.click("import");
  h.editor.draft.paintTerrain({ x: 0, y: 0 }, "Grass"); h.editor.render();
  h.click("undo"); h.click("redo");
  const recovered = harness(undefined, undefined, storage);
  assert.deepEqual(plain(recovered.editor.draft.snapshot()), plain(h.editor.draft.snapshot()));
  h.editor.loadDocument(JSON.stringify(h.editor.documentData()));
  await h.click("validate"); await h.click("export"); await h.click("play");
  for (const request of submitted.filter(row => row.operation !== "import")) {
    for (const field of ["unitTypes", "equipmentTypes", "itemTypes"])
      assert.deepEqual(request.body[field], definition[field]);
    assert.deepEqual(request.body.units.find(unit => unit.unitId === "keeper").inventory, definition.units.at(-1).inventory);
    assert.equal(request.body.abilityDefinitions, undefined);
  }
  assert.deepEqual(played, ["DELVE1:owned-abilities"]);
});

test("imported Resources render, survive export and prevent shrinking off their Cells", async () => {
  const definition = structuredClone(designerSetup);
  definition.resources = [{ type: { id: "coal", name: "Coal" }, position: { x: 5, y: 4 } }];
  const submitted = [];
  const h = harness(async (operation, body) => {
    submitted.push({ operation, body: plain(body) });
    return operation === "import" ? structuredClone(definition) : { transport: "DELVE1:resources" };
  });
  h.editor.ui.transport.value = "DELVE1:resources"; await h.click("import");
  const marker = h.editor.ui.board.querySelectorAll(".resource-marker")[0];
  assert.equal(marker.title, "Coal");
  assert.throws(() => h.editor.draft.resize(5, 4), /Shrinking/);
  await h.click("export");
  assert.deepEqual(submitted.find(row => row.operation === "export").body.resources, definition.resources);
});


test("Machine ownership selector defaults to public, lists scenario Sides, edits and clears ownership", () => {
  const h = harness(), draft = h.editor.draft;
  draft.setAgency("goblin-type", "blue", "Human");
  draft.setAgency("goblin-type", "red", "Automated");
  draft.setAgency("wizard-type", "blue", "Human");
  draft.place("goblin-type", "unit-only", "Human", { x: 0, y: 0 });
  h.editor.render(); h.editor.setTool("Machine"); h.down(h.cell(2, 1));
  assert.equal(draft.definition.machines[0].ownerSide, null);
  let form = h.editor.ui.properties.children[0], owner = inputField(form, "Owner Side");
  assert.equal(owner.tagName, "select"); assert.equal(owner.value, "");
  assert.deepEqual(owner.children.map(option => option.textContent), ["None / Public", "unit-only", "blue", "red"]);
  owner.value = "blue"; h.submit(form);
  assert.equal(draft.snapshot().machines[0].ownerSide, "blue");
  assert.match(h.cell(2, 1).textContent, /Owner: blue/);
  assert.match(h.editor.ui.placements.textContent, /Owner: blue/);
  form = h.editor.ui.properties.children[0];
  assert.equal(inputField(form, "Owner Side").value, "blue");
  inputField(form, "Owner Side").value = "red"; h.submit(form);
  assert.equal(draft.snapshot().machines[0].ownerSide, "red");
  form = h.editor.ui.properties.children[0]; inputField(form, "Owner Side").value = ""; h.submit(form);
  assert.equal(draft.snapshot().machines[0].ownerSide, null);
  assert.match(h.cell(2, 1).textContent, /Public/);
});

for (const ownerSide of [undefined, null, "blue"]) {
  test(`Machine owner ${ownerSide} survives designer load, save, export and reload`, async () => {
    const definition = structuredClone(designerSetup);
    definition.agency.push({ unitTypeId: "goblin-type", sideId: "blue", controller: "Human" });
    definition.machines = [{ id: "press", position: { x: 5, y: 4 }, type: structuredClone(catalog.machineTypes[0]), storedResources: [] }];
    if (ownerSide !== undefined) definition.machines[0].ownerSide = ownerSide;
    let saved;
    const h = harness(async (operation, body) => {
      if (operation === "import") return structuredClone(saved ?? definition);
      if (operation === "export") { saved = plain(body); return { transport: "DELVE1:ownership" }; }
      return { valid: true, errors: [] };
    });
    await h.click("import"); h.down(h.cell(5, 4));
    let form = h.editor.ui.properties.children[0];
    assert.equal(inputField(form, "Owner Side").value, ownerSide ?? "");
    assert.deepEqual(plain(h.editor.draft.snapshot()), definition);
    inputField(form, "Machine ID").value = "renamed-press"; h.submit(form);
    await h.click("validate"); await h.click("export");
    assert.equal(saved.machines[0].ownerSide, ownerSide ?? null);
    h.editor.draft.removeMachine(h.editor.draft.machineKeys[0]);
    await h.click("import"); h.down(h.cell(5, 4));
    assert.deepEqual(plain(h.editor.draft.snapshot()), saved);
    form = h.editor.ui.properties.children[0];
    assert.equal(inputField(form, "Owner Side").value, ownerSide ?? "");
    assert.equal(inputField(form, "Machine ID").value, "renamed-press");
  });
}

test("removing or renaming last Side reference preserves dangling ownership for validation and repair", async () => {
  const h = harness(async () => ({ valid: false, errors: [{ message: "Machine OwnerSide must reference a valid Side.", path: "machines[0].ownerSide" }] }));
  const draft = h.editor.draft;
  draft.setAgency("goblin-type", "old-side", "Human");
  h.editor.render(); h.editor.setTool("Machine"); h.down(h.cell(2, 1));
  let form = h.editor.ui.properties.children[0]; inputField(form, "Owner Side").value = "old-side"; h.submit(form);
  draft.editAgency(0, { unitTypeId: "goblin-type", sideId: "new-side", controller: "Human" }, { kind: "Normal" });
  h.editor.render(); form = h.editor.ui.properties.children[0];
  assert.equal(inputField(form, "Owner Side").value, "old-side");
  assert.match(inputField(form, "Owner Side").textContent, /old-side \(invalid Side\)/);
  assert.equal(draft.definition.machines[0].ownerSide, "old-side");
  await h.click("validate");
  h.editor.ui.errors.querySelectorAll("button")[0].listeners.click();
  assert.match(h.editor.ui.properties.textContent, /Owner Side/);
  form = h.editor.ui.properties.children[0]; inputField(form, "Owner Side").value = "new-side"; h.submit(form);
  draft.removeAgency(0); h.editor.render();
  assert.equal(draft.definition.machines[0].ownerSide, "new-side");
  assert.match(inputField(h.editor.ui.properties.children[0], "Owner Side").textContent, /new-side \(invalid Side\)/);
  form = h.editor.ui.properties.children[0]; inputField(form, "Owner Side").value = ""; h.submit(form);
  assert.equal(draft.definition.machines[0].ownerSide, null);
});


test("Rail brush authors every outgoing direction, renders arrows, edits and clears direction", () => {
  const h = harness(), draft = h.editor.draft;
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Rail";
  for (const [x, direction] of catalog.railDirections.entries()) {
    h.editor.ui["rail-direction"].value = direction;
    h.down(h.cell(x, 0)); h.up();
    assert.equal(draft.railDirectionAt({ x, y: 0 }), direction);
    assert.equal(h.cell(x, 0).dataset.railDirection, direction);
  }
  h.editor.setTool("Select"); h.down(h.cell(0, 0));
  const form = h.editor.ui.properties.children[0];
  inputField(form, "Rail outgoing direction").value = "West"; h.submit(form);
  assert.equal(draft.railDirectionAt({ x: 0, y: 0 }), "West");
  draft.paintTerrain({ x: 0, y: 0 }, "Grass"); h.editor.render();
  assert.equal(draft.definition.board.cells[0].direction, undefined);
  assert.equal(h.cell(0, 0).dataset.railDirection, "");
  assert.ok(!h.editor.ui.default.children.some(option => option.value === "Rail"));
});

test("Gravel is a standalone brush and default, clears Rail direction and roundtrips", () => {
  const h = harness(), draft = h.editor.draft;
  assert.ok(h.editor.ui.default.children.some(option => option.value === "Gravel"));
  draft.paintTerrain({x:0,y:0}, "Rail", "North");
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Gravel";
  h.down(h.cell(0, 0)); h.up();
  assert.equal(draft.terrainAt({x:0,y:0}), "Gravel");
  assert.equal(draft.railDirectionAt({x:0,y:0}), undefined);
  draft.definition.board.defaultTerrain = "Gravel";
  const exported = draft.snapshot(); draft.replace(JSON.parse(JSON.stringify(exported))); h.editor.render();
  assert.deepEqual(plain(draft.snapshot()), plain(exported));
  assert.equal(draft.terrainAt({x:1,y:1}), "Gravel");
  assert.ok(h.cell(0,0).classList.contains("Gravel"));
});

test("Neutrality and No Objective edit independently and survive designer roundtrip", () => {
  const h = harness(), draft = h.editor.draft;
  draft.place("grunt-type", "Railway", "Automated", { x: 0, y: 0 });
  draft.paintTerrain({ x: 0, y: 0 }, "Rail", "South"); h.editor.render();
  let form = h.editor.ui["side-settings"].children[0];
  inputField(form, "Neutral (no hostility in either direction)").value = "Yes";
  h.submit(form);
  form.querySelectorAll("button").find(b => b.textContent.includes("Edit Objective")).listeners.click();
  form = h.editor.ui["side-settings"].children.at(-1);
  inputField(form, "Objective type").value = "NoObjective"; h.submit(form);
  assert.deepEqual(plain(draft.definition.neutralSides), ["Railway"]);
  assert.equal(draft.definition.objectives[0].objective, "NoObjective");
  const exported = draft.snapshot(); draft.replace(JSON.parse(JSON.stringify(exported))); h.editor.render();
  assert.deepEqual(plain(draft.snapshot()), plain(exported));
  form = h.editor.ui["side-settings"].children[0];
  inputField(form, "Neutral (no hostility in either direction)").value = "No"; h.submit(form);
  assert.deepEqual(plain(draft.definition.neutralSides), []);
  assert.equal(draft.definition.objectives[0].objective, "NoObjective");
  form = h.editor.ui["side-settings"].children.at(-1); inputField(form, "Objective type").value = ""; h.submit(form);
  assert.deepEqual(plain(draft.definition.objectives), []);
  assert.equal(draft.railDirectionAt({ x: 0, y: 0 }), "South");
});

test("Cell Marker brushes place and remove by pointer/keyboard, roundtrip and guard shrinking", () => {
  const h = harness(), draft = h.editor.draft;
  h.editor.setTool("BananaPeel"); h.down(h.cell(4, 2));
  assert.equal(draft.definition.cellMarkers[0].type.id, "banana-peel");
  assert.equal(h.cell(4, 2).querySelectorAll(".cell-marker")[0].title, "Banana Peel");
  assert.equal(h.cell(4, 2).attributes["aria-label"].includes("Banana Peel"), true);
  h.editor.ui.board.listeners.keydown({ key: "Enter", target: h.cell(3, 1), preventDefault() {} });
  assert.equal(draft.definition.cellMarkers.length, 2);
  const saved = plain(draft.snapshot()); draft.replace(saved);
  assert.deepEqual(plain(draft.definition), saved);
  assert.throws(() => draft.resize(3, 2), /Cell Markers/);
  h.editor.setTool("RemoveMarker"); h.down(h.cell(4, 2));
  assert.equal(draft.definition.cellMarkers.length, 1);
  h.editor.ui.board.listeners.keydown({ key: "Enter", target: h.cell(3, 1), preventDefault() {} });
  assert.equal(draft.definition.cellMarkers.length, 0);
  h.editor.setTool("BananaPeel"); h.down(h.cell(2, 1));
  h.editor.setTool("Select"); h.down(h.cell(2, 1));
  const form = h.editor.ui.properties.children[0];
  assert.match(form.textContent, /Cell Marker: Banana Peel/);
  form.querySelectorAll("button").find(node => node.textContent === "Remove Cell Marker").listeners.click();
  assert.equal(draft.definition.cellMarkers.length, 0);
});

test("rejected compound agency edits preserve the complete definition and revision", () => {
  const h = harness(), d = h.editor.draft;
  d.setAgency("goblin-type", "A", "Automated");
  d.setOrder("goblin-type", "A", { kind: "HoldPosition" });
  const before = plain(d.snapshot()), revision = d.revision;
  assert.throws(() => d.editAgency(0, { unitTypeId: "unplaced", sideId: "B", controller: "Human" },
    { kind: "FollowRoute", route: { routeId: "missing", currentIndex: 0, direction: 1 } }), /existing Route/);
  assert.deepEqual(plain(d.snapshot()), before); assert.equal(d.revision, revision);
  assert.throws(() => d.saveRoute(null, { id: "bad", mode: "Loop", waypointIds: ["missing"] }));
  assert.throws(() => d.saveWaypoint(null, { id: "", area: { x: 0, y: 0, w: 1, h: 1 } }));
  assert.deepEqual(plain(d.snapshot()), before); assert.equal(d.revision, revision);
  assert.throws(() => h.editor.history.transact(() => { d.setNeutral("A", true); throw new Error("refused"); }));
  assert.deepEqual(plain(d.snapshot()), before); assert.equal(d.revision, revision);
  assert.equal(h.editor.history.past.length, 0);
});

test("Route saves preserve meaningful progress and require deliberate resets only for affected groups", () => {
  const d = harness().editor.draft;
  for (const id of ["a", "b", "c"]) d.saveWaypoint(null, { id, area: {x:0,y:0,w:1,h:1} });
  d.saveRoute(null, { id: "track", mode: "PingPong", waypointIds: ["a", "b"] });
  d.setOrder("unplaced", "C", { kind: "HoldPosition" });
  d.saveRoute(null, { id: "other", mode: "Once", waypointIds: ["a"] });
  assert.deepEqual(plain(d.definition.orders[0]), {unitTypeId:"unplaced",sideId:"C",kind:"HoldPosition"});
  d.setOrder("unplaced", "C", { kind: "Normal" });
  d.setOrder("goblin-type", "A", { kind: "FollowRoute", route: { routeId: "track", currentIndex: 1, direction: -1 } });
  d.setOrder("wizard-type", "B", { kind: "FollowRoute", route: { routeId: "track", currentIndex: 0, direction: 1 } });
  const progress = plain(d.definition.orders);
  d.saveRoute("track", plain(d.definition.routes[0]));
  assert.deepEqual(plain(d.definition.orders), progress);
  d.saveRoute("track", { id: "renamed", mode: "PingPong", waypointIds: ["a", "b", "c"] });
  assert.deepEqual(plain(d.definition.orders[0].route), { routeId: "renamed", currentIndex: 1, direction: -1 });
  const before = plain(d.snapshot()), revision = d.revision;
  const changed = { id: "renamed", mode: "PingPong", waypointIds: ["a", "c", "b"] };
  assert.throws(() => d.saveRoute("renamed", changed), /Reset affected group progress/);
  assert.deepEqual(plain(d.snapshot()), before); assert.equal(d.revision, revision);
  d.saveRoute("renamed", changed, true);
  assert.deepEqual(plain(d.definition.orders[0].route), { routeId: "renamed", currentIndex: 0, direction: 1 });
  assert.deepEqual(plain(d.definition.orders[1].route), { routeId: "renamed", currentIndex: 0, direction: 1 });
});

test("strokes and compound edits undo atomically; selection has no history and a new edit clears redo", () => {
  const h = harness(), d = h.editor.draft;
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Grass";
  h.down(h.cell(0,0)); h.move(h.cell(1,0)); h.move(h.cell(2,0)); h.up();
  assert.equal(h.editor.history.past.length, 1);
  h.editor.setTool("Select"); h.down(h.cell(0,0)); h.down(h.cell(1,1));
  assert.equal(h.editor.history.past.length, 1);
  const revision = d.revision;
  h.click("undo"); assert.equal(d.definition.board.cells.length, 0); assert.ok(d.revision > revision);
  h.click("redo"); assert.equal(d.definition.board.cells.length, 3);
  h.click("undo"); h.editor.setTool("Unit"); h.editor.ui.type.value = "goblin-type"; h.down(h.cell(0,0));
  assert.equal(h.editor.history.future.length, 0);
  assert.equal(d.definition.units.length, 1); assert.equal(d.definition.agency.length, 1);
  h.click("undo"); assert.equal(d.definition.units.length, 0); assert.equal(d.definition.agency.length, 0);
  assert.deepEqual(plain(d.definition.unitTypeIds), []);
});

test("simple properties commit on change; incomplete numeric input survives selection, unrelated edits and recovery", async () => {
  const storage = memoryStorage(), sent = [];
  const h = harness(async (operation, data) => { sent.push(operation); return { valid:true, errors:[] }; }, undefined, storage);
  h.editor.setTool("Unit"); h.editor.ui.type.value = "goblin-type"; h.down(h.cell(1,1));
  h.editor.setTool("Select"); h.down(h.cell(1,1));
  let form = h.editor.ui.properties.children[0];
  enter(inputField(form, "SideId"), "blue"); form.listeners.change();
  assert.equal(h.editor.draft.definition.units[0].sideId, "blue");
  form = h.editor.ui.properties.children[0]; enter(inputField(form, "Anchor X"), "-"); form.listeners.change();
  h.down(h.cell(4,4));
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Water"; h.down(h.cell(2,2)); h.up();
  h.editor.setTool("Select"); h.down(h.cell(1,1));
  assert.equal(inputField(h.editor.ui.properties.children[0], "Anchor X").value, "-");
  await h.click("validate"); assert.deepEqual(sent, []); assert.match(h.editor.ui.status.textContent, /Unfinished/);
  const recovered = harness(undefined, undefined, storage); recovered.down(recovered.cell(1,1));
  assert.equal(inputField(recovered.editor.ui.properties.children[0], "Anchor X").value, "-");
  assert.equal(recovered.editor.draft.terrainAt({x:2,y:2}), "Water");
  assert.match(recovered.editor.ui["save-status"].textContent, /Recovered/);
});

test("draft files preserve incomplete scenarios, unfinished Route sequences and editor identities", () => {
  const h = harness(), d = h.editor.draft;
  const removed = d.place("goblin-type", "A", "Human", {x:0,y:0}); d.removeUnit(removed);
  d.place("goblin-type", "A", "Human", {x:2,y:2});
  d.place("goblin-type", "B", "Human", {x:2,y:2}); // Invalid overlap is deliberate authoring data.
  d.saveWaypoint(null, {id:"first",area:{x:0,y:0,w:1,h:1}}); h.editor.render();
  const form = h.editor.ui.routes.querySelectorAll("form").find(f => inputField(f,"Route ID"));
  enter(inputField(form,"Route ID"), "unfinished-route");
  form.querySelectorAll("button").find(b => b.textContent === "Add existing Waypoint").listeners.click();
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Tree"; h.down(h.cell(3,3)); h.up();
  const doc = plain(h.editor.documentData());
  const other = harness(); other.editor.loadDocument(JSON.stringify(doc));
  assert.deepEqual(plain(other.editor.draft.snapshot()), plain(d.snapshot()));
  assert.deepEqual(plain(other.editor.draft.unitKeys), plain(d.unitKeys));
  const restored = other.editor.ui.routes.querySelectorAll("form").find(f => inputField(f,"Route ID"));
  assert.equal(inputField(restored,"Route ID").value,"unfinished-route");
  assert.equal(inputField(restored,"Waypoint 1").value,"first");
});

test("corrupt, incompatible and unrenderable recovery data is retained without overwriting it", () => {
  for (const saved of ["{broken", '{"format":"ProjectDelveDraft","version":99}', JSON.stringify({format:"ProjectDelveDraft",version:1,definition:{board:{width:15,height:15}}})]) {
    const storage = memoryStorage(); storage.setItem("project-delve.designer.v1", saved);
    const h = harness(undefined, undefined, storage);
    assert.match(h.editor.ui["save-status"].textContent, /Recovery unavailable/);
    h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Grass"; h.down(h.cell(0,0)); h.up();
    assert.equal(storage.getItem("project-delve.designer.v1"), saved);
    const before = plain(h.editor.draft.snapshot()), revision = h.editor.draft.revision;
    assert.throws(() => h.editor.loadDocument(saved));
    assert.deepEqual(plain(h.editor.draft.snapshot()), before); assert.equal(h.editor.draft.revision, revision);
  }
});

test("new unfinished input invalidates an in-flight validation response", async () => {
  let resolve; const h = harness(() => new Promise(done => {resolve = done;}));
  const pending = h.click("validate");
  enter(h.editor.ui.width,"unfinished");
  resolve({valid:true,errors:[]}); await pending;
  assert.doesNotMatch(h.editor.ui.status.textContent,/^Valid/);
});

test("pasted import text survives unrelated edits and recovery; zoom never enters history", () => {
  const storage = memoryStorage(), h = harness(undefined, undefined, storage);
  enter(h.editor.ui.transport,"DELVE1:unfinished-paste");
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Grass"; h.down(h.cell(0,0)); h.up();
  assert.equal(h.editor.ui.transport.value,"DELVE1:unfinished-paste");
  const historyLength = h.editor.history.past.length;
  h.editor.ui.zoom.value = "48"; h.editor.ui.zoom.listeners.change();
  assert.equal(h.editor.history.past.length,historyLength);
  assert.equal(h.editor.ui.board.style.minWidth,"720px");
  const recovered = harness(undefined, undefined, storage);
  assert.equal(recovered.editor.ui.transport.value,"DELVE1:unfinished-paste");
});

test("nested damaged recovery cannot prevent opening a healthy draft; missing select values remain visible", () => {
  const good = harness().editor.documentData(), damaged = plain(good), storage = memoryStorage();
  damaged.definition.machines = [{id:"broken",position:{x:0,y:0},type:catalog.machineTypes[0],storedResources:[null]}];
  const raw = JSON.stringify(damaged); storage.setItem("project-delve.designer.v1",raw);
  const h = harness(undefined,undefined,storage);
  assert.match(h.editor.ui["save-status"].textContent,/Recovery unavailable/);
  assert.equal(storage.getItem("project-delve.designer.v1"),raw);
  h.editor.loadDocument(JSON.stringify(good));
  assert.equal(h.editor.draft.definition.machines,undefined);
  h.editor.draft.setAgency("goblin-type","A","Human");
  h.editor.draft.definition.orders = [{unitTypeId:"goblin-type",sideId:"A",kind:"FollowRoute",route:{routeId:"missing",currentIndex:0,direction:1}}];
  h.editor.render();
  const missing = inputField(h.editor.ui.agency.children[0],"Shared Route");
  assert.equal(missing.value,"missing"); assert.match(missing.textContent,/unavailable/);
});

test("group buffers survive other edits and rejected Apply preserves revision and authored Order", () => {
  const h = harness(), d = h.editor.draft;
  d.setAgency("goblin-type","A","Automated"); d.setOrder("goblin-type","A",{kind:"HoldPosition"}); h.editor.render();
  let form = h.editor.ui.agency.children[0];
  enter(inputField(form,"SideId"),"B");
  const kind = inputField(form,"Starting Order"); kind.value="FollowRoute"; kind.listeners.change();
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value="Grass"; h.down(h.cell(0,0)); h.up();
  form = h.editor.ui.agency.children[0];
  assert.equal(inputField(form,"SideId").value,"B"); assert.equal(inputField(form,"Starting Order").value,"FollowRoute");
  const before = plain(d.snapshot()), revision = d.revision;
  h.submit(form);
  assert.deepEqual(plain(d.snapshot()),before); assert.equal(d.revision,revision);
  assert.equal(inputField(h.editor.ui.agency.children[0],"SideId").value,"B");
  assert.match(h.editor.ui.pending.textContent,/unfinished/);
});

test("Asphalt is a standalone brush and default, clears Rail direction and roundtrips", () => {
  const h = harness(), draft = h.editor.draft;
  assert.ok(h.editor.ui.default.children.some(option => option.value === "Asphalt"));
  draft.paintTerrain({x:0,y:0}, "Rail", "North");
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Asphalt";
  h.down(h.cell(0, 0)); h.up();
  assert.equal(draft.terrainAt({x:0,y:0}), "Asphalt");
  assert.equal(draft.railDirectionAt({x:0,y:0}), undefined);
  draft.definition.board.defaultTerrain = "Asphalt";
  const exported = draft.snapshot(); draft.replace(JSON.parse(JSON.stringify(exported))); h.editor.render();
  assert.deepEqual(plain(draft.snapshot()), plain(exported));
  assert.equal(draft.terrainAt({x:1,y:1}), "Asphalt");
  assert.ok(h.cell(0,0).classList.contains("Asphalt"));
});

function libraryFixture(h, overrides = {}) {
  const saved = { id: "scenario-fixture", name: "Library fixture", coverArt: "/art/cover.webp", revision: 4, definition: structuredClone(designerSetup) };
  const calls = [];
  h.context.fetch = async (url, options = {}) => {
    const body = options.body ? JSON.parse(options.body) : undefined;
    calls.push({ url, method: options.method, body });
    if (overrides.request) return overrides.request(url, options, body, saved);
    let value;
    if (url === "/api/library") value = { scenarios: [saved], campaigns: [] };
    else if (options.method === "GET") value = saved;
    else value = { ...saved, ...body, id: options.method === "POST" ? "new-scenario" : saved.id, revision: saved.revision + 1 };
    return { ok: true, status: 200, json: async () => structuredClone(value) };
  };
  return { saved, calls };
}

test("shared library open keeps stable identity, rename updates it, Save as new creates another", async () => {
  const storage = memoryStorage(), h = harness(undefined, undefined, storage), { calls } = libraryFixture(h);
  assert.equal(await h.editor.openLibraryScenario("scenario-fixture"), true);
  assert.equal(h.editor.ui.title.value, "Library fixture");
  assert.equal(h.editor.ui["library-cover"].value, "/art/cover.webp");
  h.editor.ui.title.value = "Renamed fixture";
  h.editor.draft.paintTerrain({x:0,y:0}, "Grass");
  assert.equal(await h.editor.saveLibrary(), true);
  const update = calls.find(c => c.method === "PUT");
  assert.equal(update.url, "/api/library/scenarios/scenario-fixture");
  assert.equal(update.body.revision, 4); assert.equal(update.body.name, "Renamed fixture");
  assert.deepEqual(update.body.definition, plain(h.editor.draft.snapshot()));
  assert.equal(h.editor.documentData().librarySource.id, "scenario-fixture");
  assert.equal(h.editor.documentData().librarySource.revision, 5);
  assert.equal(await h.editor.saveLibrary(true), true);
  assert.equal(calls.find(c => c.method === "POST").url, "/api/library/scenarios");
  assert.equal(h.editor.documentData().librarySource.id, "new-scenario");
  const recovered = harness(undefined, undefined, storage);
  assert.deepEqual(plain(recovered.editor.documentData().librarySource), plain(h.editor.documentData().librarySource));
  assert.equal(recovered.editor.ui["library-cover"].value, "/art/cover.webp");
});

test("unsaved linked workspace playtest returns with title, cover, pasted import and definition intact, without a library write", async () => {
  const h = harness(async () => ({ transport: "DELVE1:workspace" })), { calls } = libraryFixture(h);
  await h.editor.openLibraryScenario("scenario-fixture");
  h.editor.draft.paintTerrain({x:0,y:0}, "Grass");
  enter(h.editor.ui.title, "Unsaved name"); enter(h.editor.ui["library-cover"], "/art/unsaved.webp");
  enter(h.editor.ui.transport, "DELVE1:unfinished-input");
  const before = plain(h.editor.documentData()); delete before.savedAt;
  await h.click("play"); h.editor.setMode(true);
  const after = plain(h.editor.documentData()); delete after.savedAt;
  assert.deepEqual(after, before);
  assert.ok(calls.every(call => call.method === "GET"));
});

test("library conflict and missing scenario retain the complete workspace and identity", async () => {
  const h = harness(); libraryFixture(h); await h.editor.openLibraryScenario("scenario-fixture");
  const before = plain(h.editor.documentData()); delete before.savedAt;
  h.context.fetch = async () => ({ ok: false, status: 409, json: async () => ({ detail: "Saved version changed." }) });
  assert.equal(await h.editor.saveLibrary(), false);
  assert.match(h.editor.ui["library-status"].textContent, /Saved version changed/);
  h.context.fetch = async () => ({ ok: false, status: 404, json: async () => ({ detail: "Scenario no longer exists." }) });
  assert.equal(await h.editor.openLibraryScenario("missing"), false);
  const after = plain(h.editor.documentData()); delete after.savedAt;
  assert.deepEqual(after, before);
  assert.equal(h.editor.ui["library-save"].disabled, false);
});

test("library opening ignores a response after title or unfinished input changes", async () => {
  const h = harness(); let resolve;
  const { saved } = libraryFixture(h, { request: () => new Promise(done => { resolve = done; }) });
  const opening = h.editor.openLibraryScenario(saved.id);
  enter(h.editor.ui.title, "Work while loading");
  resolve({ ok: true, status: 200, json: async () => saved });
  assert.equal(await opening, false);
  assert.equal(h.editor.ui.title.value, "Work while loading");
  assert.equal(h.editor.documentData().librarySource, null);
});

test("incomplete forms block library save and importing a separate draft clears its library link", async () => {
  const h = harness(), { calls } = libraryFixture(h);
  await h.editor.openLibraryScenario("scenario-fixture");
  enter(h.editor.ui.width, "-");
  assert.equal(await h.editor.saveLibrary(), false);
  assert.ok(calls.every(call => call.method === "GET"));
  const separate = harness().editor.documentData();
  h.editor.loadDocument(JSON.stringify(separate));
  assert.equal(h.editor.documentData().librarySource, null);
  assert.equal(h.editor.ui["library-cover"].value, "");
});

test("Undo across opened library scenarios restores the matching identity before saving", async () => {
  const h = harness(), { calls, saved } = libraryFixture(h);
  await h.editor.openLibraryScenario("scenario-fixture");
  saved.id = "other-scenario"; saved.name = "Other scenario"; saved.revision = 7;
  await h.editor.openLibraryScenario("other-scenario");
  assert.equal(h.editor.documentData().librarySource.id, "other-scenario");
  await h.click("undo");
  assert.equal(h.editor.documentData().librarySource.id, "scenario-fixture");
  assert.equal(h.editor.documentData().librarySource.revision, 4);
  assert.equal(h.editor.ui.title.value, "Library fixture");
  await h.editor.saveLibrary();
  assert.equal(calls.find(c => c.method === "PUT").url, "/api/library/scenarios/scenario-fixture");
});

test("bundled edits preserve conflict tokens in saves, recovery and Playtest", async () => {
  const storage = memoryStorage(), h = harness(async () => ({ transport: "DELVE1:workspace" }), async () => true, storage);
  const { saved, calls } = libraryFixture(h);
  saved.source = "bundled"; saved.changeToken = "bundle-token";
  await h.editor.openLibraryScenario(saved.id);
  assert.equal(h.editor.documentData().librarySource.changeToken, "bundle-token");
  assert.match(h.editor.ui["library-status"].textContent, /saving creates a local override/);
  h.editor.ui.title.value = "Local experiment";
  await h.editor.saveLibrary();
  assert.equal(calls.find(c => c.method === "PUT").body.changeToken, "bundle-token");
  const beforePlay = calls.length;
  await h.click("play");
  assert.equal(calls.length, beforePlay);
  assert.equal(h.editor.documentData().librarySource.changeToken, "bundle-token");
  const recovered = harness(undefined, undefined, storage);
  assert.equal(recovered.editor.documentData().librarySource.source, "bundled");
  assert.equal(recovered.editor.documentData().librarySource.changeToken, "bundle-token");
});

test("cover import changes only workspace artwork and retains a cover edited during upload", async () => {
  const h = harness(), { saved } = libraryFixture(h);
  await h.editor.openLibraryScenario(saved.id);
  const calls = [], png = { size: 12, type: "image/png" };
  h.context.prepareCover = async file => { assert.equal(file.type, "image/jpeg"); return png; };
  h.context.fetch = async (url, options) => { calls.push({url, options}); return { ok: true, json: async () => ({coverArt: "/api/library/covers/abc.png"}) }; };
  assert.equal(await h.editor.importCover({type:"image/jpeg"}), true);
  assert.equal(h.editor.ui["library-cover"].value, "/api/library/covers/abc.png");
  assert.equal(calls.length, 1); assert.equal(calls[0].url, "/api/library/covers");
  assert.equal(calls[0].options.headers["Content-Type"], "image/png");
  assert.equal(h.editor.documentData().librarySource.revision, 4);
  let resolve;
  h.context.prepareCover = () => new Promise(done => { resolve = done; });
  const pending = h.editor.importCover({type:"image/jpeg"});
  h.editor.ui["library-cover"].value = "/art/new-choice.webp";
  resolve(png); await pending;
  assert.equal(h.editor.ui["library-cover"].value, "/art/new-choice.webp");
  assert.match(h.editor.ui["library-status"].textContent, /Existing edits retained/);
});

test("restore bundled scenario uses token and preserves workspace on conflict", async () => {
  const h = harness(), { saved } = libraryFixture(h);
  saved.source = "override"; saved.changeToken = "override-token";
  await h.editor.openLibraryScenario(saved.id);
  h.editor.ui.title.value = "Unsaved title";
  h.context.fetch = async (url, options) => {
    assert.equal(url, "/api/library/scenarios/scenario-fixture/restore");
    assert.deepEqual(JSON.parse(options.body), {revision:4,changeToken:"override-token"});
    return {ok:false,json:async () => ({detail:"Changed elsewhere."})};
  };
  assert.equal(await h.editor.restoreBundled(), false);
  assert.equal(h.editor.ui.title.value, "Unsaved title");
  assert.equal(h.editor.documentData().librarySource.source, "override");
  assert.equal(h.editor.ui.main.inert, false);
});

test('Loot overrides preserve other unfinished group edits and restore inheritance without copying content', () => {
  const h = harness(); h.editor.draft.replace(designerSetup); h.editor.render();
  let form = h.editor.ui.agency.children[0];
  const id = form.dataset.groupType;
  enter(inputField(form, 'SideId'), 'unfinished-side');
  let panel = form.querySelectorAll('details').find(d => d.dataset.lootType === id);
  change(inputField(panel, 'Drop probability'), '0');
  press(panel, 'Apply Loot settings');
  assert.equal(h.editor.draft.snapshot().lootUnitOverrides[id].dropSuccesses, 0);
  form = h.editor.ui.agency.children[0];
  assert.equal(inputField(form, 'SideId').value, 'unfinished-side');
  panel = form.querySelectorAll('details').find(d => d.dataset.lootType === id);
  change(inputField(panel, 'Drop probability'), 'default');
  press(panel, 'Apply Loot settings');
  assert.equal(h.editor.draft.snapshot().lootUnitOverrides, undefined);
  assert.equal(inputField(h.editor.ui.agency.children[0], 'SideId').value, 'unfinished-side');
});
