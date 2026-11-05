const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");
const code = "WLD1-2x2-X-8-Y12-F35-W20-R15-KBalanced-HAuto-S0123456789ABCDEF";
const flush = () => new Promise(resolve => setImmediate(resolve));
function harness() {
  class Node {
    value = ""; hidden = false; textContent = ""; dataset = {}; children = []; listeners = {}; open = false;
    style = { setProperty() {} };
    addEventListener(name, callback) { this.listeners[name] = callback; }
    append(...nodes) { for (const node of nodes) this.children.push(...(node.fragment ? node.children : [node])); }
    replaceChildren(...nodes) { this.children = nodes; }
    focus() {} select() {} click() { return this.listeners.click?.(); }
    showModal() { this.open = true; } close() { this.open = false; this.listeners.close?.(); }
  }
  const nodes = new Map(), get = id => { if (!nodes.has(id)) nodes.set(id, new Node()); return nodes.get(id); };
  for (const [key, value] of Object.entries({ width:"2", height:"2", offsetX:"-8", offsetY:"12", forestCoverage:"35", waterCoverage:"20", rockCoverage:"15", scale:"Balanced", waterForm:"Auto", seed:"", view:"enhanced" })) get(`world-generator-${key}`).value = value;
  const opened = new Node(), requests = [], designs = [], visuals = [], copied = [];
  const context = vm.createContext({ structuredClone, Uint32Array,
    performance: { now: () => 42 },
    document: { getElementById: get, querySelectorAll: () => [opened], createElement: () => new Node(), createDocumentFragment: () => Object.assign(new Node(), {fragment:true}) },
    crypto: { getRandomValues: array => { array[0] = 1; array[1] = 2; return array; } },
    navigator: { clipboard: { writeText: async value => { copied.push(value); } } },
    fetch: (url, options) => new Promise(resolve => requests.push({ url, body: options.body && JSON.parse(options.body), resolve })),
    delveOpenGenerated: value => { designs.push(value); return true; }
  });
  vm.runInContext(source("board-visuals.js").replace(/^export /gm, ""), context);
  context.applyBoardVisual = (node, category, kind, options) => visuals.push({ node, category, kind, options });
  vm.runInContext(source("generator-state.js").replace(/^export /gm, "") + "\n" + source("world-generator-state.js").replace(/^export /gm, "") + "\n" + source("world-generator.js").replace(/^import .*;\r?\n/gm, ""), context);
  const click = name => get(`world-generator-${name}`).click();
  const enter = (name, value) => { get(`world-generator-${name}`).value = value; get(`world-generator-${name}`).listeners.input?.(); };
  const result = (complete = code) => ({ code:complete, title:"Generated world terrain", fingerprint:"test-owned-fixture", transport:"DELVE1:world-fixture", visualSeed:"stable-world-appearance",
    window:{ offsetX:-8, offsetY:12, width:2, height:2 },
    terrainDefinitions:[{ id:"test-ground", name:"Test Paving", presentation:{recipe:"Gravel"} }, {id:"test-canopy",name:"Test Canopy",presentation:{recipe:"Tree"}}],
    definition:{ board:{width:2,height:2,defaultTerrain:"StoneFloor",defaultTerrainTypeId:"test-ground",cells:[{position:{x:1,y:0},terrain:"Tree",terrainTypeId:"test-canopy"}],edges:[]},units:[] }, diagnostics:{} });
  const accept = async (index, value = result()) => { requests[index].resolve({ok:true,json:async () => value}); await flush(); };
  return { get, click, enter, requests, designs, opened, context, result, visuals, copied, accept };
}

test("world recipes round-trip every scale and water form, signed bounds and seed case", () => {
  const h = harness();
  for (const scale of ["Fine","Balanced","Broad"]) for (const water of ["Auto","Lakes","River","Mixed"]) {
    // Only the seed is case-insensitive; structural tokens retain their defined spelling.
    const canonical = code.replace("KBalanced", `K${scale}`).replace("HAuto", `H${water}`);
    const input = canonical.replace("0123456789ABCDEF", "0123456789abcdef");
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${input}'))`, h.context), canonical);
  }
  const edge = "WLD1-1x1-X2147483647-Y-2147483648-F0-W0-R0-KFine-HRiver";
  assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${edge}'))`, h.context), edge);
  assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${code}'), false)`, h.context), code.split("-S")[0]);
});

test("invalid coverage, dimensions, coordinates and seeds never submit a generation", () => {
  for (const [field, value, error] of [["width","0",/width/],["height","51",/height/],["width","",/width/],["offsetX","2147483647",/entire window/],["offsetY","-2147483649",/offsetY/],["forestCoverage","80",/at most 100/],["waterCoverage","1.5",/whole number/],["seed","bad",/16 hexadecimal/]]) {
    const h = harness(); h.enter(field,value); h.click("preview");
    assert.equal(h.requests.length,0); assert.match(h.get("world-generator-status").textContent,error);
  }
});

test("world presets come from the catalog and preserve dimensions, offsets and seed", async () => {
  const h = harness(); h.opened.click();
  assert.equal(h.requests[0].url,"/api/world-generation/catalog");
  await h.accept(0,{maxBoardSize:50,presets:[{id:"fixture-marsh",name:"Fixture Marsh",settings:{forestCoverage:10,waterCoverage:60,rockCoverage:0,scale:"Broad",waterForm:"Mixed"}}]});
  h.get("world-generator-preset").value = "fixture-marsh"; h.get("world-generator-preset").listeners.change(); h.click("preview");
  assert.equal(h.requests[1].body.code,"WLD1-2x2-X-8-Y12-F10-W60-R0-KBroad-HMixed");
});

test("completed seed stays with the world when moving the window until Generate Another", async () => {
  const h = harness(); h.click('preview'); await h.accept(0);
  assert.equal(h.get('world-generator-seed').value,'0123456789ABCDEF');
  h.enter('offsetX','0'); h.click('preview');
  assert.match(h.requests[1].body.code,/-X0-Y12-.*-S0123456789ABCDEF$/);
  h.click('another');
  assert.match(h.requests[2].body.code,/-S0000000100000002$/);
});

test("all typed terrain cells render using world appearance context, including default cells", async () => {
  const h = harness(); h.click("preview"); await h.accept(0);
  const cells = h.get("world-generator-board").children;
  assert.equal(cells.length,4); assert.equal(cells[0].className,"cell Gravel"); assert.equal(cells[1].className,"cell Tree");
  assert.equal(cells[1].title,"Test Canopy (-7,12)");
  assert.deepEqual(h.visuals.map(v => [v.options.x,v.options.y]),[[-8,12],[-7,12],[-8,13],[-7,13]]);
  assert.ok(h.visuals.every(v => v.options.seed === "stable-world-appearance"));
  assert.equal(h.visuals[0].options.terrain.size,4); assert.equal(h.visuals[0].options.terrain.get("-7,12").typeId,"test-canopy");
  assert.match(h.get("world-generator-preview-summary").textContent,/Test Paving 3 \(75.0%\)/);
  assert.match(h.get("world-generator-performance").textContent,/Rendered 4 terrain Cells/);
  h.get("world-generator-view").value="classic"; h.get("world-generator-view").listeners.change();
  assert.equal(h.get("world-generator-board").dataset.view,"classic");
});

test("retained preview, completed code, export and Designer handoff stay exact after edits", async () => {
  const h = harness(); h.click("preview"); const result = h.result(); await h.accept(0,result);
  const cells = [...h.get("world-generator-board").children]; h.enter("width","3");
  assert.match(h.get("world-generator-status").textContent,/outdated/); assert.deepEqual(h.get("world-generator-board").children,cells);
  await h.click("copy-complete"); assert.deepEqual(h.copied,[code]);
  h.click("export"); assert.equal(h.get("world-generator-transport").value,result.transport);
  h.click("designer"); assert.equal(h.designs[0].code,code); assert.equal(h.designs[0].title,result.title);
  assert.equal(JSON.stringify(h.designs[0].definition),JSON.stringify(result.definition));
});

test("overlapping windows use the same seed and world cell coordinates despite different recipes", async () => {
  const h = harness(); h.click("preview"); await h.accept(0);
  const original = h.visuals.find(v => v.options.x === -7 && v.options.y === 12).options;
  h.enter("offsetX","-7"); h.click("preview");
  const moved = h.result(code.replace("X-8","X-7")); moved.window.offsetX = -7;
  moved.definition.board.cells = [{position:{x:0,y:0},terrain:"Tree",terrainTypeId:"test-canopy"}];
  await h.accept(1,moved);
  const overlap = h.visuals.slice(4).find(v => v.options.x === -7 && v.options.y === 12).options;
  assert.equal(overlap.seed,original.seed); assert.equal(overlap.x,original.x); assert.equal(overlap.y,original.y);
  assert.notEqual(h.requests[0].body.code,h.requests[1].body.code);
});

test("a 50 by 50 terrain preview materializes every Cell and records its render measurement", async () => {
  const h = harness(); h.enter("width","50"); h.enter("height","50"); h.click("preview");
  const large = h.result(); large.definition.board.width = 50; large.definition.board.height = 50;
  large.window.width = 50; large.window.height = 50; await h.accept(0,large);
  assert.equal(h.get("world-generator-board").children.length,2500);
  assert.equal(h.visuals.length,2500); assert.equal(h.get("world-generator-board").dataset.renderMs,"0.0");
  assert.match(h.get("world-generator-performance").textContent,/Rendered 2500 terrain Cells/);
});

test("catalog arrival does not overwrite edits and closing cancels a pending preview", async () => {
  const h = harness(); h.opened.click(); h.enter("forestCoverage","42");
  await h.accept(0,{maxBoardSize:50,presets:[{id:"late",name:"Late preset",settings:{forestCoverage:10}}]});
  assert.equal(h.get("world-generator-forestCoverage").value,"42");
  h.click("preview"); h.click("close"); await h.accept(1);
  assert.equal(h.get("world-generator-board").children.length,0);
});

test("loaded recipes update controls and stale responses cannot overwrite a newer terrain preview", async () => {
  const h = harness(); h.enter("code-input",code); h.click("load");
  assert.equal(h.get("world-generator-seed").value,"0123456789ABCDEF"); h.click("preview");
  h.click("another"); const newer = code.replace("0123456789ABCDEF","0000000100000002");
  assert.equal(h.requests[1].body.code,newer); await h.accept(1,h.result(newer)); await h.accept(0);
  assert.equal(h.get("world-generator-complete").value,newer);
  h.click("preview"); h.enter("forestCoverage","30"); await h.accept(2);
  assert.equal(h.get("world-generator-complete").value,newer);
});

test("world generator has a separate entry and terrain-only authoring actions", () => {
  const html = source("index.html"), world = html.slice(html.indexOf('<dialog id="world-generator"'));
  assert.match(html,/data-open-world-generator/); assert.match(html,/<script type="module" src="world-generator.js"/);
  assert.match(world,/awaiting scenario authoring/); assert.doesNotMatch(world,/<button[^>]*>Play/);
  for (const id of ["width","height","offsetX","offsetY","seed","view"]) assert.match(world,new RegExp(`<label>[^<]*<(?:input|select) id="world-generator-${id}"`));
});
