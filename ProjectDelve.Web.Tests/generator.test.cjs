const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");
const code = "DNG1-HVM-30x20-R8-E2-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF";
const flush = () => new Promise(resolve => setImmediate(resolve));
function harness() {
  class Node {
    value = ""; hidden = false; textContent = ""; dataset = {}; children = []; listeners = {}; open = false;
    style = { setProperty() {} };
    addEventListener(name, callback) { this.listeners[name] = callback; }
    setAttribute(name, value) { this[name] = value; }
    append(...nodes) { this.children.push(...nodes); }
    replaceChildren(...nodes) { this.children = nodes; }
    focus() {} select() {} click() { return this.listeners.click?.(); }
    showModal() { this.open = true; } close() { this.open = false; this.listeners.close?.(); }
  }
  const nodes = new Map(), get = id => { if (!nodes.has(id)) nodes.set(id, new Node()); return nodes.get(id); };
  for (const [key, value] of Object.entries({ profile:"HVM", width:"30", height:"20", rooms:"8", excavation:"2", connectivity:"2", doors:"2", openness:"2", terrain:"2", treasure:"2", density:"2", difficulty:"3", objective:"reach", required:"2", fog:"1", loot:"Classic", seed:"", view:"enhanced" })) get(`generator-${key}`).value = value;
  const opened = new Node(), requests = [], plays = [], designs = [], events = {}, visuals = [];
  const context = vm.createContext({ structuredClone, Uint32Array, document: { getElementById: get, querySelectorAll: () => [opened], createElement: () => new Node() },
    crypto: { getRandomValues: array => { array[0] = 1; array[1] = 2; return array; } }, navigator: { clipboard: { writeText: async () => {} } },
    addEventListener: (name, callback) => events[name] = callback,
    fetch: (url, options) => new Promise(resolve => requests.push({ url, body: JSON.parse(options.body), resolve })),
    delvePlayGenerated: async value => { plays.push(value); return true; }, delveOpenGenerated: value => { designs.push(value); return true; },
    styleSide: () => {} });
  vm.runInContext(source("board-visuals.js").replace(/^export /gm, ""), context);
  context.applyBoardVisual = (node, category, kind, options) => visuals.push({ node, category, kind, options });
  vm.runInContext(source("generator-state.js").replace(/^export /gm, "") + "\n" + source("generator.js").replace(/^import .*;\r?\n/gm, ""), context);
  const click = name => get(`generator-${name}`).click();
  const enter = (name, value) => { get(`generator-${name}`).value = value; get(`generator-${name}`).listeners.input?.(); };
  const result = complete => ({ code:complete, prefix:complete.split("-S")[0], fingerprint:"test", transport:"DELVE1:fixture",
    definition: { board:{ width:14, height:14, defaultTerrain:"SolidRock", cells:[], edges:[] }, units:[] }, diagnostics:{ rooms:[], monsters:0, occupiedRooms:0, destinationDistance:10 } });
  return { nodes, get, click, enter, requests, plays, designs, opened, context, result, visuals };
}

test("preview resolves unfamiliar typed assemblies and defaults through shared presentation metadata", async () => {
  const h = harness(); h.click("preview");
  const result = h.result(code);
  const assembly = { surface: { presentation: { recipe: 'Grass' } },
    components: [{ slot: 'ornament', definition: { id: 'unknown-component', name: 'Brazier', presentation: { recipe: 'Campfire' } } }] };
  result.terrainDefinitions = [
    { id: 'unfamiliar-tile', name: 'Watchfire Clearing', presentation: { recipe: 'generic' }, assembly },
    { id: 'unfamiliar-ground', name: 'Old Paving', presentation: { recipe: 'Gravel' } }
  ];
  result.definition.board = { width: 2, height: 1, defaultTerrain: 'StoneFloor', defaultTerrainTypeId: 'unfamiliar-ground',
    cells: [{ position: { x: 0, y: 0 }, terrain: 'StoneFloor', terrainTypeId: 'unfamiliar-tile' }], edges: [] };
  h.requests[0].resolve({ ok: true, json: async () => result }); await flush();
  const cells = h.get('generator-board').children;
  assert.equal(cells[0].className, 'cell Grass');
  assert.equal(cells[0].title, 'Watchfire Clearing (0,0)');
  assert.deepEqual(h.visuals[0].options.assembly, assembly);
  assert.equal(h.visuals[0].options.seed, code);
  assert.equal(cells[1].className, 'cell Gravel');
  assert.equal(cells[1].title, 'Old Paving (1,0)');
});
test("a loaded complete code starts unseen without fetching or rendering an authoring preview", async () => {
  const h = harness(); h.opened.click();
  h.enter("code-input", code); h.click("code-play"); await flush();
  assert.deepEqual(h.plays, [code]); assert.equal(h.requests.length, 0);
  assert.equal(h.get("generator-preview-content").hidden, true);
  assert.equal(h.get("generator-board").children.length, 0);
  assert.equal(h.get("dungeon-generator").open, false);
});
test("preview stays stable when configuration changes and Designer receives that exact result", async () => {
  const h = harness(); h.click("preview");
  assert.equal(h.requests.length, 1);
  h.requests[0].resolve({ ok:true, json:async () => h.result(code) }); await flush();
  assert.equal(h.get("generator-complete").value, code);
  const board = [...h.get("generator-board").children];
  h.enter("difficulty", "5");
  assert.match(h.get("generator-status").textContent, /outdated/);
  assert.deepEqual(h.get("generator-board").children, board);
  h.click("designer"); assert.equal(h.designs[0].code, code);
  h.click("play-preview"); await flush(); assert.deepEqual(h.plays, [code]);
});
test("out of order responses and configuration edits cannot replace a newer preview or Designer draft", async () => {
  const h = harness(); h.click("preview"); h.click("another");
  assert.equal(h.requests.length, 2); assert.match(h.requests[1].body.code, /-S0000000100000002$/);
  const newer = code.replace("0123456789ABCDEF", "0000000100000002");
  h.requests[1].resolve({ ok:true, json:async () => h.result(newer) }); await flush();
  h.requests[0].resolve({ ok:true, json:async () => h.result(code) }); await flush();
  assert.equal(h.get("generator-complete").value, newer);
  assert.equal(h.designs.length, 0);
  h.click("preview"); h.enter("rooms", "7");
  h.requests[2].resolve({ ok:true, json:async () => h.result(code) }); await flush();
  assert.equal(h.get("generator-complete").value, newer);
});
test("invalid input retains the preview and does not submit a play or generation request", async () => {
  const h = harness(); h.enter("seed", "bad"); h.click("preview"); h.click("play");
  assert.equal(h.requests.length, 0); assert.equal(h.plays.length, 0);
  assert.match(h.get("generator-status").textContent, /exactly 16/);
  h.enter("code-input", code.replace("DNG1", "DNG2")); h.click("code-play");
  assert.equal(h.plays.length, 0); assert.match(h.get("generator-status").textContent, /DNG1/);
});
test("configuration prefixes omit seeds, OR4 normalizes and invalid capacities fail", () => {
  const h = harness();
  assert.equal(vm.runInContext(`configurationCode(configurationFromCode('${code}'), false)`, h.context), code.split("-S")[0]);
  assert.match(vm.runInContext(`configurationCode(configurationFromCode('${code.replace("OR2", "OR4")}'))`, h.context), /-ORA-/);
  assert.throws(() => vm.runInContext(`configurationFromCode('${code.replace("30x20-R8", "14x14-R24")}')`, h.context), /rooms do not fit/);
});

for (const excavation of ["1", "2", "3"]) test(`Excavation ${excavation} round-trips and invalidates a retained preview`, async () => {
  const h = harness(); h.click("preview");
  h.requests[0].resolve({ok:true,json:async () => h.result(code)}); await flush();
  const changedCode = code.replace("-E2-", `-E${excavation}-`);
  h.enter("code-input", changedCode); h.click("load");
  assert.equal(h.get("generator-excavation").value, excavation);
  assert.match(h.get("generator-prefix").value, new RegExp(`-E${excavation}-`));
  assert.equal(h.get("generator-complete").value, code);
  h.enter("excavation", excavation === "1" ? "3" : "1");
  assert.match(h.get("generator-status").textContent, /outdated/);
  h.click("play-preview"); await flush(); assert.deepEqual(h.plays,[code]);
});

test("Loot preset participates in preview, code loading and unseen play", async () => {
  const h = harness(); h.enter("loot", "FarOut"); h.click("preview");
  assert.match(h.requests[0].body.code, /-QFarOut$/);
  h.enter("code-input", code.replace('-S','-QFun-S')); h.click("code-play"); await flush();
  assert.match(h.plays[0], /-QFun-S/); assert.equal(h.get("generator-loot").value, "Fun");
});

const strongholdCode = "DNG2-OS-30x24-O2-T2-N2-L3-B2-OR2-F1-QClassic-S0123456789ABCDEF";
test("Overgrown Stronghold code preserves parameters and rejects invalid sizes", () => {
  const h = harness();
  for (const complete of [strongholdCode, strongholdCode.replace("QClassic", "QFarOut").replace("B2", "B0").replace("OR2", "ORA").replace("T2", "T0").replace("O2", "O3")]) {
    assert.equal(vm.runInContext(`configurationCode(configurationFromCode('${complete}'))`, h.context), complete);
  }
  assert.throws(() => vm.runInContext(`configurationFromCode('${strongholdCode.replace("30x24", "17x24")}')`, h.context), /18 to 50/);
  assert.throws(() => vm.runInContext(`configurationFromCode('${strongholdCode.replace("O2", "O0")}')`, h.context), /DNG2-OS/);
});

test("switching profile supplies useful dimensions and displays only relevant controls", () => {
  const h = harness(); h.enter("profile", "OS");
  assert.equal(h.get("generator-height").value, "24");
  assert.equal(h.get("generator-rooms-label").hidden, true);
  assert.equal(h.get("generator-treasure-label").hidden, false);
  assert.equal(h.get("generator-heading").textContent, "Overgrown Stronghold");
  h.click("preview");
  assert.equal(h.requests[0].body.code, strongholdCode.split("-S")[0]);
  h.enter("profile", "HVM");
  assert.equal(h.get("generator-height").value, "20");
  assert.equal(h.get("generator-rooms-label").hidden, false);
  assert.equal(h.get("generator-treasure-label").hidden, true);
  assert.equal(h.get("generator-prefix").value, code.split("-S")[0]);
});

test("Stronghold code starts unseen with no omniscient preview request", async () => {
  const h = harness(); h.enter("code-input", strongholdCode); h.click("code-play"); await flush();
  assert.deepEqual(h.plays, [strongholdCode]);
  assert.equal(h.requests.length, 0);
  assert.equal(h.get("generator-profile").value, "OS");
  assert.equal(h.get("generator-board").children.length, 0);
});

test("Stronghold preview distinguishes unopened caches, displays the live deck multiplier and uses supplied footprints", async () => {
  const h = harness(); h.enter("profile", "OS"); h.click("preview");
  const result = h.result(strongholdCode);
  result.definition.loot = { preset: "Fun", presetMultiplier: 3 };
  result.definition.lootMarkers = [{ id: "a", position: {x:2,y:3} }, { id: "b", position: {x:2,y:3} }];
  result.definition.units = [{ unitTypeId: "shaman-type", sideId: "side-2", anchor: {x:5,y:5} }];
  result.unitMetadata = [{ unitTypeId: "shaman-type", footprintOffsets: [{x:0,y:0}, {x:1,y:0}, {x:0,y:1}, {x:1,y:1}] }];
  h.requests[0].resolve({ ok:true, json:async () => result }); await flush();
  assert.match(h.get("generator-preview-summary").textContent, /1 caches \/ 2 unopened Loot.*Fun Loot ×3/);
  const marker = h.get("generator-board").children.find(node => node.className === "generator-loot");
  assert.equal(marker.textContent, "◆2");
  assert.match(marker.title, /unopened Loot/);
  const figure = h.get("generator-board").children.find(node => node.className === "figure large");
  assert.equal(figure.style.width, `${185 / 14}%`);
  h.enter("treasure", "3"); h.click("export");
  assert.equal(h.get("generator-transport").value, result.transport);
});
