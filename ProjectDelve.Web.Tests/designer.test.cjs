const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");
const fullParty = JSON.parse(fs.readFileSync(path.join(__dirname, "fixtures/full-party.json"), "utf8"));
const plain = value => JSON.parse(JSON.stringify(value));
const catalog = {
  maxBoardSize: 50,
  unitTypes: [...fullParty.unitTypeIds, "opaque-large", "unplaced"].map(id => ({
    unitTypeId: id, displayName: `Printed ${id}`, maxHp: 8,
    cellSpan: id === "opaque-large" ? 2 : 1,
    footprintOffsets: id === "opaque-large" ? [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }] : [{ x: 0, y: 0 }]
  })),
  terrains: ["Grass", "Tree", "Water", "StoneFloor", "StoneFloorWithTable"],
  edgeKinds: ["Wall", "ClosedDoor", "OpenDoor", "None", "WallWithWindow"],
  postures: ["Upright", "Lying"], controllers: ["Human", "Automated"]
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
  addEventListener(name, callback) { this.listeners[name] = callback; }
  matches(selector) {
    if (selector === "[data-editor-hit]") return !!this.dataset.editorHit;
    if (selector.startsWith(".")) return this.classList.contains(selector.slice(1));
    return this.tagName === selector;
  }
  closest(selector) { return this.matches(selector) ? this : this.parent?.closest(selector); }
  querySelectorAll(selector) { return this.children.flatMap(child => [...(child.matches(selector) ? [child] : []), ...child.querySelectorAll(selector)]); }
  focus() {} select() {} scrollIntoView() {}
}

function harness(request = async () => ({ valid: true, errors: [] }), play = async () => true) {
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
    addEventListener(name, callback) { documentListeners[name] = callback; }
  };
  const tools = document.getElementById("designer-tools");
  for (const tool of ["Select", "Terrain", "Edge", "Unit"]) {
    const node = new Element("button"); node.dataset.tool = tool; tools.append(node);
  }
  document.getElementById("designer-side").value = "side-1";
  document.getElementById("designer-agency-side").value = "side-1";
  const details = new Element("details"); details.append(document.getElementById("designer-transport"));
  const context = vm.createContext({ document, catalog, request, play, structuredClone, navigator: { clipboard: { writeText: async () => {} } } });
  vm.runInContext(source("designer-state.js").replace(/^export /gm, ""), context);
  vm.runInContext(source("side-colors.js").replace(/^export /gm, ""), context);
  vm.runInContext(source("designer.js").replace(/^import .*\r?\n/gm, "").replace(/^export /gm, "").replace(/startDesigner\(\);\s*$/, ""), context);
  vm.runInContext("globalThis.editor = mountDesigner(catalog, request, play)", context);
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
  assert.equal(h.editor.ui.width.value, "15");
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
  const imported = structuredClone(fullParty);
  imported.unitTypeIds.unshift("unplaced"); imported.unitTypeIds.reverse();
  imported.agency.push({ unitTypeId: "unplaced", sideId: "future", controller: "Automated" });
  imported.board.edges.push({ position: { x: 0, y: 0 }, direction: "Right", kind: "None" });
  imported.board.cells.push({ position: { x: 0, y: 0 }, terrain: "StoneFloor" });
  imported.units[0].initialHp = null; imported.units[1].initialHp = 2;
  draft.replace(imported); h.editor.render();
  assert.deepEqual(plain(draft.snapshot()), imported);
  assert.match(h.editor.ui.order.textContent, /unplaced/);
});

test("successful FullParty import renders concrete cells, edges, placements and order faithfully", async () => {
  const h = harness(async operation => operation === "import" ? structuredClone(fullParty) : { valid: true, errors: [] });
  h.editor.ui.transport.value = "DELVE1:server-decoded-fixture"; await h.click("import");
  assert.deepEqual(plain(h.editor.draft.snapshot()), fullParty);
  assert.equal(h.editor.ui.board.querySelectorAll(".cell").length, 225);
  assert.equal(h.editor.ui.board.querySelectorAll(".figure").length, fullParty.units.length);
  assert.match(h.cell(5, 4).className, /Tree/);
  assert.match(h.edge(7, 3, "Right").className, /ClosedDoor/);
  assert.equal(h.editor.ui.order.children.length, fullParty.unitTypeIds.length);
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
    resolve(operation === "import" ? structuredClone(fullParty) : operation === "validate" ? { valid: true, errors: [] } : { transport: "DELVE1:old" });
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
  h.editor.draft.replace(fullParty); h.editor.render(); const before = plain(h.editor.draft.snapshot());
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
  const definition = structuredClone(fullParty); definition.worldEffects = { cardsPerRound: 2, cycling: 3 };
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
