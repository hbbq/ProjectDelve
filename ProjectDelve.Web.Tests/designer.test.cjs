const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");
const designerSetup = JSON.parse(fs.readFileSync(path.join(__dirname, "fixtures/designer-setup.json"), "utf8"));
const plain = value => JSON.parse(JSON.stringify(value));
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

test("Lava paints and renders from the terrain catalog and survives export", async () => {
  const submitted = [];
  const h = harness(async (operation, body) => { submitted.push(plain(body)); return { transport: "DELVE1:lava" }; });
  assert.ok(h.editor.ui.terrain.children.some(option => option.value === "Lava"));
  h.editor.setTool("Terrain"); h.editor.ui.terrain.value = "Lava";
  h.down(h.cell(1, 1)); h.up();
  assert.match(h.cell(1, 1).className, /Lava/);
  await h.click("export");
  assert.equal(submitted[0].board.cells.find(cell => cell.position.x === 1 && cell.position.y === 1).terrain, "Lava");
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
