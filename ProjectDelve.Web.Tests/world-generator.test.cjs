const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");
const code = "WLD1-2x2-X-8-Y12-F35-W20-R15-KBalanced-HAuto-S0123456789ABCDEF";
const withEmptyLoot = value => value.includes('-S') ? value.replace('-S', '-OEmpty-S') : value + '-OEmpty';
const flush = () => new Promise(resolve => setImmediate(resolve));

test('storage-free Beacon Quick Play passes the real draft parser before launching temporary transport', async () => {
  const h = harness(); h.get('world-generator').open = true;
  const parser = vm.runInNewContext(source('designer-state.js').replace(/^export /gm, '') +
    '\n({draftDocument, readDraftDocument})', { structuredClone });
  h.context.delveOpenGenerated = result => {
    const doc = parser.readDraftDocument(JSON.stringify(parser.draftDocument(result.definition, result.title)));
    h.designs.push(doc.definition); return true;
  };
  const generated = h.result();
  generated.definition.unitTypeIds = []; generated.definition.agency = [];
  generated.definition.machines = [{ id: 'beacon', position: { x: 1, y: 1 },
    type: { id: 'beacon', name: 'Beacon', storage: null,
      operation: { costs: [], reveal: { distance: 15 }, maxUsesPerSide: 1 } },
    storedResources: [], useCounts: {} }];
  h.click('preview'); await h.accept(0, generated);
  const playing = h.click('quick-play'); await h.accept(1, { transport: 'DELVE1:prepared-beacon-party' }); await playing;
  assert.deepEqual(h.plays, ['DELVE1:prepared-beacon-party'], h.get('world-generator-status').textContent);
  assert.equal(h.designs.length, 1);
  assert.equal(JSON.stringify(h.designs[0]), JSON.stringify(generated.definition));
  assert.equal(h.get('world-generator').open, false);
});
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
  for (const [key, value] of Object.entries({ width:"2", height:"2", offsetX:"-8", offsetY:"12", forestCoverage:"35", waterCoverage:"20", rockCoverage:"15", scale:"Balanced", waterForm:"Auto", seed:"", lootPreset:"Empty", view:"enhanced" })) get(`world-generator-${key}`).value = value;
  const opened = new Node(), requests = [], designs = [], visuals = [], copied = [], plays = [], navigations = [];
  const context = vm.createContext({ structuredClone, Uint32Array,
    performance: { now: () => 42 },
    document: { getElementById: get, querySelectorAll: () => [opened], createElement: () => new Node(), createDocumentFragment: () => Object.assign(new Node(), {fragment:true}) },
    crypto: { getRandomValues: array => { array[0] = 1; array[1] = 2; return array; } },
    navigator: { clipboard: { writeText: async value => { copied.push(value); } } },
    fetch: (url, options) => new Promise(resolve => requests.push({ url, body: options.body && JSON.parse(options.body), resolve })),
    delveOpenGenerated: value => { designs.push(value); return true; },
    delvePlayDesign: async transport => { plays.push(transport); return true; },
    delveNavigate: mode => navigations.push(mode)
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
  return { get, click, enter, requests, designs, opened, context, result, visuals, copied, plays, navigations, accept };
}

test("Quick Play passes only the temporary transport to Playtest and retains the original world in Designer", async () => {
  const h = harness(), result = h.result();
  result.definition.units = [{ unitId: 'original-enemy', groupId: 'original-camp' }];
  result.definition.orders = [{ groupId: 'original-camp', kind: 'GuardArea' }];
  const original = JSON.stringify(result);
  h.get('world-generator').open = true;
  h.click('preview'); await h.accept(0, result);
  h.enter('forestCoverage', '20'); // Like Export, Quick Play uses the retained result.
  const pending = h.click('quick-play'); h.click('quick-play');
  assert.equal(h.requests.length, 2); assert.equal(h.requests[1].url, '/api/world-generation/quick-play');
  assert.deepEqual(h.requests[1].body, result.definition);
  assert.equal(h.get('world-generator-quick-play').disabled, true);
  await h.accept(1, { transport: 'DELVE1:temporary-heroes' }); await pending;
  assert.deepEqual(h.plays, ['DELVE1:temporary-heroes']); assert.equal(JSON.stringify(h.designs[0]), original);
  assert.deepEqual(h.navigations, ['play']);
  assert.equal(JSON.stringify(result), original); assert.equal(h.get('world-generator').open, false);
  h.click('export'); assert.equal(h.get('world-generator-transport').value, result.transport);
  assert.equal(h.get('world-generator-quick-play').disabled, false);
});

test("Quick Play placement failure leaves the original preview and Designer untouched", async () => {
  const h = harness(); h.get('world-generator').open = true;
  h.click('preview'); await h.accept(0); const original = h.get('world-generator-board').children[0];
  const pending = h.click('quick-play');
  h.requests[1].resolve({ ok: false, json: async () => ({ detail: 'No free legal cluster for five Heroes.' }) });
  await pending;
  assert.match(h.get('world-generator-status').textContent, /No free legal cluster/);
  assert.equal(h.designs.length, 0); assert.equal(h.plays.length, 0);
  assert.equal(h.get('world-generator-board').children[0], original);
  assert.equal(h.get('world-generator').open, true); assert.equal(h.get('world-generator-quick-play').disabled, false);
});

test("closed or replaced previews cannot launch a late Quick Play preparation", async () => {
  for (const close of [true, false]) {
    const h = harness(); h.get('world-generator').open = true;
    h.click('preview'); await h.accept(0); const pending = h.click('quick-play');
    if (close) h.click('close');
    else { h.click('preview'); await h.accept(2, h.result(code.replace('X-8', 'X-7'))); }
    await h.accept(1, { transport: 'DELVE1:late-party' }); await pending;
    assert.equal(h.plays.length, 0); assert.equal(h.designs.length, 0);
  }
});

test("Quick Play uses existing handoff cancellation and reports game replacement failure", async () => {
  for (const canceled of [true, false]) {
    const h = harness(); h.get('world-generator').open = true;
    h.click('preview'); await h.accept(0);
    if (canceled) h.context.delveOpenGenerated = () => false;
    else h.context.delvePlayDesign = async () => false;
    const pending = h.click('quick-play'); await h.accept(1, { transport: 'DELVE1:temporary' }); await pending;
    assert.equal(h.plays.length, 0); assert.equal(h.get('world-generator').open, true);
    assert.equal(h.navigations.length, 0);
    assert.match(h.get('world-generator-status').textContent, canceled ? /canceled/ : /Could not start Quick Play/);
  }
});

test("world recipes round-trip every scale and water form, signed bounds and seed case", () => {
  const h = harness();
  for (const scale of ["Fine","Balanced","Broad"]) for (const water of ["Auto","Lakes","River","Mixed"]) {
    // Only the seed is case-insensitive; structural tokens retain their defined spelling.
    const canonical = code.replace("KBalanced", `K${scale}`).replace("HAuto", `H${water}`);
    const input = canonical.replace("0123456789ABCDEF", "0123456789abcdef");
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${input}'))`, h.context), withEmptyLoot(canonical));
  }
  const edge = "WLD1-1x1-X2147483647-Y-2147483648-F0-W0-R0-KFine-HRiver";
  assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${edge}'))`, h.context), withEmptyLoot(edge));
  assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${code}'), false)`, h.context), withEmptyLoot(code.split("-S")[0]));
});

test("world Loot presets round-trip and submit one finite preset selection without a size control", async () => {
  const h = harness();
  for (const preset of ['Empty', 'Classic', 'Fun', 'Experimental', 'FarOut', 'All']) {
    const recipe = code.replace('-S', `-MHigh-D4-O${preset}-S`);
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${recipe}'))`, h.context), recipe);
    h.enter('code-input', recipe); h.click('load');
    assert.equal(h.get('world-generator-lootPreset').value, preset);
  }
  h.click('preview'); assert.match(h.requests[0].body.code, /-MHigh-D4-OAll-S/);
  await h.accept(0);
  h.enter('lootPreset', 'Fun'); assert.match(h.get('world-generator-status').textContent, /outdated/);
  h.enter('lootPreset', 'Unknown'); h.click('preview'); assert.equal(h.requests.length, 1);
  assert.match(h.get('world-generator-status').textContent, /Choose a Loot deck/);
  assert.throws(() => vm.runInContext(`worldConfigurationFromCode('${code.replace('-S', '-OUnknown-S')}')`, h.context), /ordered WLD1/);
  const html = source('index.html');
  assert.match(html, /id="world-generator-lootPreset"/);
  assert.doesNotMatch(html, /id="world-generator-(?:lootMultiplier|lootCopies|lootDeckSize)"/);
});

test("invalid coverage, dimensions, coordinates and seeds never submit a generation", () => {
  for (const [field, value, error] of [["width","0",/width/],["height","121",/height/],["width","",/width/],["offsetX","2147483647",/entire window/],["offsetY","-2147483649",/offsetY/],["forestCoverage","80",/at most 100/],["waterCoverage","1.5",/whole number/],["seed","bad",/16 hexadecimal/]]) {
    const h = harness(); h.enter(field,value); h.click("preview");
    assert.equal(h.requests.length,0); assert.match(h.get("world-generator-status").textContent,error);
  }
});

test("world presets come from the catalog and preserve dimensions, offsets and seed", async () => {
  const h = harness(); h.opened.click();
  assert.equal(h.requests[0].url,"/api/world-generation/catalog");
  await h.accept(0,{maxBoardSize:50,presets:[{id:"fixture-marsh",name:"Fixture Marsh",settings:{forestCoverage:10,waterCoverage:60,rockCoverage:0,scale:"Broad",waterForm:"Mixed"}}]});
  h.get("world-generator-preset").value = "fixture-marsh"; h.get("world-generator-preset").listeners.change(); h.click("preview");
  assert.equal(h.requests[1].body.code,"WLD1-2x2-X-8-Y12-F10-W60-R0-KBroad-HMixed-OEmpty");
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

test("a 120 by 100 terrain preview materializes every Cell and records its render measurement", async () => {
  const h = harness(); h.enter("width","120"); h.enter("height","100"); h.click("preview");
  const large = h.result(); large.definition.board.width = 120; large.definition.board.height = 100;
  large.window.width = 120; large.window.height = 100; await h.accept(0,large);
  assert.equal(h.get("world-generator-board").children.length,12000);
  assert.equal(h.visuals.length,12000); assert.equal(h.get("world-generator-board").dataset.renderMs,"0.0");
  assert.match(h.get("world-generator-performance").textContent,/Rendered 12000 terrain Cells/);
  const mounted=h.get('world-generator-board').children[0];
  h.get('world-generator-zoom').value='48';h.get('world-generator-zoom').listeners.change();
  assert.equal(h.get('world-generator-board').style.width,'5760px');
  assert.equal(h.get('world-generator-board').children[0],mounted);
  h.get('world-generator-zoom').value='fit';h.get('world-generator-zoom').listeners.change();
  assert.equal(h.get('world-generator-board').style.width,'');
});

test("large rectangular recipes retain signed offsets and respect a lower host authoring limit", async () => {
  const h = harness();
  for (const dimensions of ['120x100','100x120','120x120','1x120']) {
    const recipe=code.replace('2x2',dimensions);
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${recipe}'))`,h.context),withEmptyLoot(recipe));
  }
  assert.throws(()=>vm.runInContext(`worldConfigurationFromCode('${code.replace('2x2','120x100')}',50)`,h.context),/width/);
  h.opened.click(); await h.accept(0,{maxBoardSize:32,presets:[]});
  assert.equal(h.get('world-generator-width').max,32);
  h.enter('width','33'); h.click('preview'); assert.equal(h.requests.length,1);
  assert.match(h.get('world-generator-status').textContent,/32/);
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
  assert.equal(h.requests[1].body.code,newer.replace('-S','-E0-I2-B1111-C0-NLow-VMedium-P2-Q0-TLow-JMinorPath-L1-ALow-U0-Z2-MOff-D3-OEmpty-S')); await h.accept(1,h.result(newer)); await h.accept(0);
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

function stagedResult(h, count = 3) {
  const result = h.result();
  result.preview = { terrainPalette:["test-ground","test-canopy"], formationLabels:[0,0,-1,-1], boundaryCells:[0,1],
    formations:[{id:"forest-1",kind:"Forest",coreArea:2,envelopeArea:3,density:0.67,maxDepth:1,touchesBoundary:true}],
    frames:Array.from({length:count}, (_, index) => ({stepId:`step-${index}`,stage:["NaturalTerrain","FormationAnalysis","WalkabilityAnalysis"][index % 3],terrain:index === 0 ? "AAAA" : "ABAA",changedCells:index ? [1] : [],walkableComponents:[0,-1,0,0],chokepoints:[2],entrances:[{id:"entry",featureId:"clearing",insideIndex:0,outsideIndex:2,width:1}],opportunities:[{id:"clearing",kind:"Clearing",anchorIndex:0,usableArea:3,walkableComponent:0,requiresConnection:true}],diagnostics:{changedCells:index ? 1 : 0,featuresAdded:0,walkableArea:3,componentCount:1,largestComponentArea:3,routingSearches:0,routingVisitedCells:0}})) };
  return result;
}

test("stage navigation and before comparison retain scroll, zoom and the final handoff", async () => {
  const h = harness(), result = stagedResult(h); h.click("preview"); await h.accept(0,result);
  assert.equal(h.get("world-generator-stage").value,"2");
  assert.equal(h.get("world-generator-stage").children.length,3);
  h.get("world-generator-board-wrap").scrollTop=70; h.get("world-generator-board-wrap").scrollLeft=35;
  h.click("previous"); assert.equal(h.get("world-generator-stage").value,"1");
  h.click("compare"); assert.equal(h.get("world-generator-board").children[1].className,"cell Gravel");
  assert.match(h.get("world-generator-diagnostics").textContent,/Before/);
  assert.match(h.get("world-generator-diagnostics").textContent,/Natural Terrain/);
  h.click("compare"); assert.equal(h.get("world-generator-board").children[1].className,"cell Tree");
  h.click("next"); assert.equal(h.get("world-generator-next").disabled,true);
  assert.equal(h.get("world-generator-board-wrap").scrollTop,70); assert.equal(h.get("world-generator-board-wrap").scrollLeft,35);
  h.get("world-generator-zoom").value="24"; h.get("world-generator-zoom").listeners.change();
  const mounted=h.get("world-generator-board").children[0];
  h.get("world-generator-zoom").value="48"; h.get("world-generator-zoom").listeners.change();
  assert.equal(h.get("world-generator-board").children[0],mounted);
  h.click("previous"); h.click("previous");
  h.click("export"); h.click("designer");
  assert.equal(h.get("world-generator-transport").value,result.transport);
  assert.equal(JSON.stringify(h.designs[0]),JSON.stringify(result)); assert.equal(h.get("world-generator-complete").value,code);
});

test("server supplied overlays and cell inspection display analysis without recomputing it", async () => {
  const h = harness(); h.click("preview"); await h.accept(0,stagedResult(h));
  for(const name of ["changed","boundaries","components","opportunities"]) { h.get(`world-generator-${name}`).checked=true; h.get(`world-generator-${name}`).listeners.change(); }
  const cells=h.get("world-generator-board").children;
  assert.match(cells[0].className,/world-boundary.*world-component/);
  assert.match(cells[0].children[0].className,/world-entrance.*world-opportunity/);
  assert.match(cells[1].className,/world-changed/); assert.match(cells[2].className,/world-chokepoint/);
  cells[0].click();
  assert.match(h.get("world-generator-inspection").textContent,/Original formation forest-1: Forest/);
  assert.match(h.get("world-generator-inspection").textContent,/Opportunity clearing: Clearing/);
  assert.match(h.get("world-generator-inspection").textContent,/Entrance entry: feature clearing/);
  assert.match(h.get("world-generator-diagnostics").textContent,/walkable 3 · components 1/);
});

test("performance labels distinguish finalization and follow the displayed comparison frame", async () => {
  const h=harness(), result=stagedResult(h);
  result.metrics={stages:[{stepId:'step-0',milliseconds:1.25,allocatedBytes:100},{stepId:'step-1',milliseconds:2.5,allocatedBytes:200},{stepId:'step-2',milliseconds:3.75,allocatedBytes:300}],finalizationMilliseconds:4.5,retainedPreviewBytes:800};
  h.click('preview');await h.accept(0,result);h.click('previous');
  assert.match(h.get('world-generator-performance').textContent,/Server frame stage 2.5 ms/);
  h.click('compare');assert.match(h.get('world-generator-performance').textContent,/Server frame stage 1.3 ms/);
  assert.match(h.get('world-generator-performance').textContent,/finalization 4.5 ms; preview raw data estimate 800 bytes/);
});

test("enrichment codes round-trip settings, preserve legacy codes and load old codes disabled", () => {
  const h=harness(), enriched=code.replace('-S','-E1-I3-B1010-S');
  assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${enriched}'))`,h.context),withEmptyLoot(enriched));
  h.enter('code-input',enriched);h.click('load');h.click('preview');assert.equal(h.requests[0].body.code,enriched.replace('-S','-C0-NLow-VMedium-P2-Q0-TLow-JMinorPath-L1-ALow-U0-Z2-MOff-D3-OEmpty-S'));
  assert.equal(h.get('world-generator-enrichmentIntensity').value,'3'); assert.equal(h.get('world-generator-rockEnrichment').value,'false');
  h.enter('code-input',code);h.click('load');assert.equal(h.get('world-generator-enrichmentEnabled').value,'false');
  for(const suffix of ['-E2-I1-B1111','-E1-I4-B1111','-E1-I2-B111','-E1-I2-B1112']) assert.throws(()=>vm.runInContext(`worldConfigurationFromCode('${code.split('-S')[0]+suffix}')`,h.context),/ordered WLD1/);
});

test("a maximum map with the bounded 24-frame preview renders stage changes and rejects stale results", async () => {
  const h=harness(); h.enter('width','120');h.enter('height','120');h.click('preview');
  const result=stagedResult(h,24);result.definition.board.width=result.window.width=120;result.definition.board.height=result.window.height=120;
  result.preview.formationLabels=Array(14400).fill(-1);
  for(const frame of result.preview.frames) {frame.terrain='A'.repeat(14400);frame.walkableComponents=Array(14400).fill(0);}
  await h.accept(0,result);assert.equal(h.get('world-generator-board').children.length,14400);
  h.click('previous');assert.equal(h.get('world-generator-stage').value,'22');
  h.click('preview');h.enter('forestCoverage','30');await h.accept(1,stagedResult(h));
  assert.equal(h.get('world-generator-stage').value,'22');assert.equal(h.get('world-generator-board').children.length,14400);
});

test("civilization suffixes round-trip independently and validate controls before generation", () => {
  const h = harness();
  for (const suffix of ['-C1-NLow-VMedium-P2','-C0-NNone-VNone-P1','-E1-I3-B1111-C1-NVeryHigh-VVeryHigh-P3']) {
    const complete = code.replace('-S',`${suffix}-S`);
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${complete}'))`,h.context),withEmptyLoot(complete));
    h.enter('code-input',complete); h.click('load');
    assert.equal(h.get('world-generator-civilizationEnabled').value, suffix.includes('-C1-') ? 'true' : 'false');
  }
  for (const suffix of ['-C2-NLow-VMedium-P2','-C1-N9-VMedium-P2','-C1-NLow-V5-P2','-C1-NLow-VMedium-P0'])
    assert.throws(()=>vm.runInContext(`worldConfigurationFromCode('${code.replace('-S',`${suffix}-S`)}')`,h.context),/ordered WLD1/);
  h.enter('campsiteDensity','9'); h.click('preview'); assert.equal(h.requests.length,0); assert.match(h.get('world-generator-status').textContent,/campsiteDensity/);
});

test("catalog civilization presets update only authored settings", async () => {
  const h=harness(); h.opened.click();
  await h.accept(0,{maxBoardSize:120,presets:[{id:'settled',name:'Settled',settings:{},civilization:{enabled:true,campsites:"High",settlements:"High",preparationIntensity:3}}]});
  h.get('world-generator-preset').value='settled'; h.get('world-generator-preset').listeners.change(); h.click('preview');
  assert.equal(h.requests[1].body.code,code.split('-S')[0]+'-C1-NHigh-VHigh-P3-OEmpty');
  assert.equal(h.get('world-generator-offsetX').value,'-8');
});

function constructionResult(h) {
  const result=stagedResult(h,3), bounds={x:0,y:0,width:2,height:2}, root='test/site', building=root+'/building', layout=building+'/rooms';
  const op=(id,parentId,name,terrainChanges=[],edgeChanges=[])=>({id,parentId,name,objectId:id,bounds,terrainChanges,edgeChanges});
  const edge=(index,direction,before,after)=>({key:{index,direction},before,after});
  const operations=[op(root,'test','Settlement'),op(root+'/prepare',root,'Prepare',[{index:0,before:'test-ground',after:'test-canopy'}]),
    op(building,root,'Building'),op(layout,building,'Room layout'),
    op(layout+'/walls',layout,'Partitions',[{index:1,before:'test-ground',after:'test-canopy'}],[edge(0,'Right','Wall','None')]),
    op(layout+'/furniture',layout,'Furniture',[{index:0,before:'test-canopy',after:'test-ground'}],[edge(2,'Right','None','ClosedDoor')]),op(root+'/commit',root,'Ready')];
  const area=(id,spans)=>({id,bounds,spans,area:spans.reduce((total,span)=>total+span.length,0)});
  const footprint=area(root+'/footprint',[{y:0,x:0,length:2},{y:1,x:0,length:2}]);
  const room={id:layout+'/room-0',purpose:'Room',interior:area(layout+'/interior',[{y:1,x:0,length:2}]),usable:area(layout+'/usable',[{y:1,x:0,length:1}]),portalIds:[building+'/door'],twoByTwoPlacements:0};
  const portal={id:building+'/door',ownerId:building,insideIndex:2,outsideIndex:3,edge:{index:2,direction:'Right'},kind:'ClosedDoor',width:1};
  const site={id:root,kind:'Settlement',footprint,exterior:area(root+'/exterior',[{y:0,x:0,length:2}]),entrances:[],buildings:[{id:building,footprint:room.interior,rooms:[room],portals:[portal]}],preparation:{cost:5,treesRemoved:1,rockExcavated:0,waterFilled:0}};
  const wall={position:{x:0,y:0},direction:'Right',kind:'Wall'}, door={position:{x:0,y:1},direction:'Right',kind:'ClosedDoor'};
  result.preview.frames[0]={...result.preview.frames[0],terrain:'AAAA',edges:[wall],construction:[],sites:[]};
  for(const i of [1,2]) result.preview.frames[i]={...result.preview.frames[i],stage:i===1?'Civilization':'WalkabilityAnalysis',terrain:'ABAA',edges:[door],changedEdges:[edge(0,'Right','Wall','None'),edge(2,'Right','None','ClosedDoor')],construction:operations,sites:[site],planningFailures:[{requestId:'test/failure',code:'NoFootprint',reason:'No valid ground',attempts:4}],civilizationStats:{sites:1,campsites:0,settlements:1,buildings:1,rooms:1,attempts:6,work:40,changedTerrain:1,changedEdges:2}};
  result.definition.board.edges=[door];
  return result;
}
function descendants(node) { return [node,...node.children.flatMap(descendants)]; }

test("Mountain Dungeon inspection uses actual rooms and server spans without synthetic Buildings", async () => {
  const h = harness(), result = constructionResult(h), site = result.preview.frames[2].sites[0];
  const room = site.buildings[0].rooms[0], area = id => ({ ...site.exterior, id });
  site.kind = 'MountainDungeon'; site.buildings = [];
  site.mountainDungeon = { profile: 'ChamberComplex', rooms: [room], roomCapacity: 3, rockMassArea: 90, rockMassAnchor: 0,
    corridors: area('corridor'), protectedRock: area('rock'), requiredAccess: area('access'), lava: area('lava'), treasureAreas: [area('treasure')] };
  site.populationIntent = { layout: 'Dungeon', minimumUnits: 0, maximumUnits: 4, alertTopology: 'Independent', areas: [],
    encounterRegions: [{ id: 'dungeon/encounter', area: area('encounter'), alertTopology: 'Connected' }] };
  Object.assign(result.preview.frames[2].civilizationStats, { dungeonsRequested: 3, dungeonsAccepted: 1, dungeonsOmitted: 2 });
  for (const id of ['usable', 'rooms', 'sites', 'buildings']) h.get(`world-generator-${id}`).checked = true;
  h.click('preview'); await h.accept(0, result);
  assert.match(h.get('world-generator-civilization-summary').textContent, /Mountain Dungeon ChamberComplex; 1 rooms \/ 3 capacity/);
  assert.match(h.get('world-generator-result-summary').textContent, /0 buildings · 1 rooms/);
  assert.match(h.get('world-generator-civilization-summary').textContent, /3 requested \/ 1 accepted \/ 2 omitted/);
  const nodes = descendants(h.get('world-generator-board'));
  for (const id of ['corridor', 'rock', 'access', 'lava', 'treasure', 'encounter', room.interior.id])
    assert.ok(nodes.some(node => node.dataset.areaId === id), id);
  assert.equal(nodes.filter(node => node.className === 'world-plan-space world-plan-building').length, 0);
  const cell = nodes.find(node => node.dataset.worldX === -8 && node.dataset.worldY === 12);
  cell.listeners.click(); assert.match(h.get('world-generator-inspection').textContent, /Protected rock.*\nRequired dungeon access.*\nDungeon Lava/);
  assert.match(h.get('world-generator-inspection').textContent, /Encounter region dungeon\/encounter: Connected/);
  const button = descendants(h.get('world-generator-construction-tree')).find(node => node.dataset.spaceId === room.id);
  assert.ok(button); button.click();
  assert.ok(descendants(h.get('world-generator-board')).some(node => node.className === 'world-construction-bounds' && node.dataset.objectId === room.id));
});

test("Settlement preview exposes server-authored family, lots, public spaces and fallback reasons", async () => {
  const h = harness(), result = constructionResult(h), site = result.preview.frames[2].sites[0];
  site.settlementLayout = { family: 'InnCompound', access: site.exterior,
    lots: [{ id: 'service-lot', bounds: { x: 0, y: 0, width: 1, height: 1 }, purpose: 'Storage', buildingId: 'service-building' }],
    publicSpaces: [{ id: 'service-yard', role: 'Courtyard', area: site.exterior }],
    fallbackReasons: ['Reduced to two buildings: constrained geography.'] };
  h.get('world-generator-usable').checked = true;
  h.get('world-generator-buildings').checked = true;
  h.click('preview'); await h.accept(0, result);
  assert.match(h.get('world-generator-civilization-summary').textContent, /InnCompound.*Courtyard.*Reduced to two buildings/);
  const nodes = descendants(h.get('world-generator-board'));
  assert.ok(nodes.some(node => node.dataset.objectId === 'service-lot' && node.title.includes('Storage')));
  assert.ok(nodes.some(node => node.dataset.objectId === 'service-yard' && node.title.includes('Courtyard')));
  assert.ok(nodes.some(node => node.title?.includes('Public access')));
});

test("Population controls round-trip all densities and difficulties without type selection controls", () => {
  const h = harness();
  for (const density of ['Off', 'Low', 'Medium', 'High']) for (const difficulty of [1, 3, 5]) {
    const recipe = code.replace('-S', `-M${density}-D${difficulty}-S`);
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${recipe}'))`, h.context), withEmptyLoot(recipe));
    h.enter('code-input', recipe); h.click('load');
    assert.equal(h.get('world-generator-populationDensity').value, density);
    assert.equal(h.get('world-generator-populationDifficulty').value, String(difficulty));
  }
  h.enter('code-input', code); h.click('load');
  assert.equal(h.get('world-generator-populationDensity').value, 'Off');
  for (const suffix of ['-MRare-D3', '-MHigh-D0', '-MHigh-D6', '-MHigh'])
    assert.throws(() => vm.runInContext(`worldConfigurationFromCode('${code.replace('-S', suffix + '-S')}')`, h.context), /ordered WLD1/);
  const html = source('index.html');
  assert.match(html, /id="world-generator-populationDensity"/); assert.match(html, /id="world-generator-populationDifficulty"/);
  assert.doesNotMatch(html, /id="world-generator-(?:theme|encounterType|rarity|encounterWeight)"/);
});

test("Population preset settings and edits invalidate the retained preview", async () => {
  const h = harness(); h.opened.click();
  await h.accept(0, { maxBoardSize: 120, presets: [{ id: 'inhabited', name: 'Inhabited', settings: {}, population: { density: 'High', difficulty: 4 } }] });
  h.get('world-generator-preset').value = 'inhabited'; h.get('world-generator-preset').listeners.change();
  h.click('preview'); assert.match(h.requests[1].body.code, /-MHigh-D4-OEmpty$/); await h.accept(1);
  const retained = h.get('world-generator-board').children[0];
  h.enter('populationDensity', 'Low');
  assert.match(h.get('world-generator-status').textContent, /outdated/); assert.equal(h.get('world-generator-board').children[0], retained);
  h.enter('populationDifficulty', '6'); h.click('preview'); assert.equal(h.requests.length, 2);
  assert.match(h.get('world-generator-status').textContent, /populationDifficulty/);
});

test("Site Loot is visible without Population, remains stage-local and deduplicates shared marker IDs", async () => {
  const h = harness(), result = constructionResult(h), frame = result.preview.frames[2];
  frame.siteLootMarkers = [{ id: 'dungeon-treasure', position: { x: 1, y: 1 } }];
  result.definition.lootMarkers = [...frame.siteLootMarkers, { id: 'final-only', position: { x: 0, y: 0 } }];
  const markerNodes = () => descendants(h.get('world-generator-board')).filter(node => node.dataset.lootMarkerId);
  h.click('preview'); await h.accept(0, result);
  assert.deepEqual(markerNodes().map(node => node.dataset.lootMarkerId), ['dungeon-treasure']);
  assert.equal(h.get('world-generator-population').hidden, false);
  assert.match(h.get('world-generator-population-summary').textContent, /1 Site Loot markers; no Population/);
  markerNodes()[0].click(); assert.match(h.get('world-generator-inspection').textContent, /dungeon-treasure.*unopened Loot/);
  h.click('compare'); assert.equal(markerNodes().length, 0);
  assert.equal(h.get('world-generator-population').hidden, true);
  frame.population = { encounters: [], lootMarkers: [...frame.siteLootMarkers, { id: 'encounter-loot', position: { x: 0, y: 1 } }],
    statistics: { acceptedEncounters: 0, target: 0, units: 0, groups: 0, attempts: 0, work: 0, lootMarkers: 2 }, failures: [] };
  h.click('preview'); await h.accept(1, result);
  assert.deepEqual(markerNodes().map(node => node.dataset.lootMarkerId), ['dungeon-treasure', 'encounter-loot']);
  assert.match(h.get('world-generator-population-summary').textContent, /1 Site Loot markers \(2 total unique Loot markers\)/);
});

test("Population preview renders only its displayed stage and preserves the ordinary final scenario", async () => {
  const h = harness(), result = constructionResult(h);
  const unit = { unitTypeId: 'test-guard', unitId: 'unit-7', sideId: 'monsters', groupId: 'slot-7-guards', anchor: { x: 1, y: 1 }, posture: 'Lying', dormant: true };
  const waypoint = { id: 'slot-7-waypoint', area: { x: 0, y: 0, w: 1, h: 1 } };
  const route = { id: 'slot-7-route', mode: 'PingPong', waypointIds: [waypoint.id, 'slot-7-other'] };
  const order = { unitTypeId: unit.unitTypeId, sideId: unit.sideId, groupId: unit.groupId, kind: 'FollowRoute', route: { routeId: route.id, currentIndex: 0, direction: 1 } };
  const encounter = { id: 'slot-7', kind: 'RoadPatrol', sourceId: 'road-2', groupIds: [unit.groupId], units: [unit], orders: [order], routes: [route], waypoints: [waypoint, { id: 'slot-7-other', area: { x: 1, y: 1, w: 1, h: 1 } }] };
  encounter.roles = [{ unitId: unit.unitId, role: 'sentry', areaId: 'test-wing' }];
  encounter.requestedUnits = 6; encounter.intendedAlertTopology = 'Separated'; encounter.actualAlertComponents = 2;
  result.unitDefinitions = [{ id: unit.unitTypeId, displayName: 'Test Guard', cellSpan: 1 }];
  result.definition.units = [unit]; result.definition.orders = [order]; result.definition.routes = [route]; result.definition.waypoints = encounter.waypoints;
  result.preview.frames[2].stage = 'Population';
  result.preview.frames[2].population = { encounters: [encounter], statistics: { target: 2, acceptedEncounters: 1, units: 1, groups: 1, attempts: 2, work: 30, shortfall: 1 }, failures: [{ kind: 'ZombieHouse', reason: 'No accessible building.' }] };
  Object.assign(result.preview.frames[2].population.statistics, { dormantUnits: 1, awakeUnits: 0, initialTokens: 0, requestedUnits: 6, unitShortfall: 5, lootMarkers: 1, quietRegions: 3 });
  result.preview.frames[2].population.alertComponents = [{ index: 42, unitIds: [unit.unitId] }];
  result.preview.frames[2].population.lootMarkers = [{ id: 'cache-7', position: { x: 0, y: 1 } }];
  h.get('world-generator-population-units').checked = true; h.get('world-generator-population-routes').checked = true;
  h.click('preview'); await h.accept(0, result);
  const populationNodes = () => h.get('world-generator-board').children.filter(node => node.className.includes('world-population-'));
  assert.equal(populationNodes().length, 4); assert.equal(h.get('world-generator-population').hidden, false);
  const figure = populationNodes().find(node => node.dataset.unitId);
  assert.match(figure.className, /lying dormant/); assert.match(figure.title, /Test Guard.*unit-7/);
  assert.match(figure.title, /Role: sentry · test-wing/); assert.match(figure.title, /Alert component: 42/);
  assert.equal(h.get('world-generator-board').children.filter(node => node.dataset.lootMarkerId === 'cache-7').length, 1);
  figure.click(); assert.match(h.get('world-generator-inspection').textContent, /Group slot-7-guards/);
  assert.match(h.get('world-generator-population-summary').textContent, /1 \/ 2 encounters.*shortfall 1/);
  assert.match(h.get('world-generator-population-summary').textContent, /1 Dormant.*0 awake.*0 initial tokens.*1 Loot markers.*3 quiet regions/);
  assert.match(h.get('world-generator-population-summary').textContent, /Unit shortfall 5/);
  assert.ok(descendants(h.get('world-generator-population-details')).some(node => /intended Alert: Separated · actual Alert components: 2/.test(node.textContent)));
  assert.match(descendants(h.get('world-generator-population-details')).find(node => node.dataset.groupId).textContent, /FollowRoute.*slot-7-route/);
  assert.match(h.get('world-generator-population-failures').children[0].textContent, /No accessible building/);
  h.click('compare'); assert.equal(populationNodes().length, 0); assert.equal(h.get('world-generator-population').hidden, true);
  h.click('compare'); assert.equal(populationNodes().length, 4);
  operation(h, 'test/site').click(); assert.equal(populationNodes().length, 0);
  h.click('previous'); assert.equal(populationNodes().length, 0);
  h.click('export'); h.click('designer'); assert.equal(h.get('world-generator-transport').value, result.transport);
  assert.equal(JSON.stringify(h.designs[0].definition), JSON.stringify(result.definition));
});

function operation(h,id) { return descendants(h.get('world-generator-construction-tree')).find(node=>node.dataset.operationId===id); }

test("nested construction replay restores terrain, deletes Edges and uses the first trace checkpoint on Final", async () => {
  const h=harness(), result=constructionResult(h); h.click('preview'); await h.accept(0,result);
  const frame=vm.runInContext(`constructionPreview(${JSON.stringify(result.preview)},2,'test/site/building/rooms',false,2)`,h.context);
  assert.equal(frame.terrain,'ABAA'); assert.deepEqual(JSON.parse(JSON.stringify(frame.edges)),[{position:{x:0,y:1},direction:'Right',kind:'ClosedDoor'}]);
  const before=vm.runInContext(`constructionPreview(${JSON.stringify(result.preview)},2,'test/site/building/rooms',true,2)`,h.context);
  assert.equal(before.terrain,'BAAA'); assert.equal(before.edges[0].kind,'Wall');
  assert.equal(JSON.stringify(vm.runInContext(`constructionRange(${JSON.stringify(result.preview.frames[2].construction)},'test/site/building')`,h.context)),JSON.stringify({start:2,end:6}));
  operation(h,'test/site').click(); h.click('compare');
  assert.equal(h.get('world-generator-board').children[0].className,'cell Gravel');
  assert.ok(h.get('world-generator-board').children.some(node=>node.className==='edge Wall'));
  h.click('compare');
  assert.equal(h.get('world-generator-board').children[1].className,'cell Tree');
  assert.ok(h.get('world-generator-board').children.some(node=>node.className==='edge ClosedDoor'));
  h.click('export');h.click('designer');assert.equal(h.get('world-generator-transport').value,result.transport); assert.equal(JSON.stringify(h.designs[0]),JSON.stringify(result));
});

test("construction tree expands parents and shows server Room identities, quality and accepted spans", async () => {
  const h=harness();h.click('preview');await h.accept(0,constructionResult(h));
  assert.equal(h.get('world-generator-construction').hidden,false);
  assert.ok(descendants(h.get('world-generator-construction-tree')).some(node=>node.open===true));
  const room=descendants(h.get('world-generator-construction-tree')).find(node=>node.dataset.spaceId);
  assert.match(room.textContent,/test\/site\/building\/rooms\/room-0.*1\/2 usable.*1 Doors/);room.click();
  assert.match(h.get('world-generator-construction-selection').textContent,/room-0.*containing room layout/);
  assert.ok(h.get('world-generator-board').children.some(node=>node.className==='world-construction-bounds'&&node.dataset.objectId.endsWith('/room-0')));
  for(const name of ['sites','buildings','rooms','usable','site-entrances']) {h.get('world-generator-'+name).checked=true;h.get('world-generator-'+name).listeners.change();}
  const overlays=h.get('world-generator-board').children;
  assert.ok(overlays.some(node=>node.dataset.spaceKind==='site'&&node.dataset.objectId==='test/site'));
  assert.ok(overlays.some(node=>node.dataset.spaceKind==='room'&&node.dataset.areaId.endsWith('/interior')));
  assert.ok(overlays.some(node=>node.dataset.spaceKind==='usable'&&node.style.width==='50%'));
  assert.ok(overlays.some(node=>node.className==='world-plan-portal'&&node.title.includes('ClosedDoor')));
  assert.match(h.get('world-generator-civilization-summary').textContent,/1 settlements.*1 buildings.*1 rooms/);
  assert.match(h.get('world-generator-civilization-summary').textContent,/preparation 5, trees 1/);
  assert.match(h.get('world-generator-planning-failures').children[0].textContent,/NoFootprint: No valid ground/);
});

test("Edge-only frames, removal highlights and selection reset preserve navigation and zoom", async () => {
  const h=harness(), result=constructionResult(h);result.preview.frames[1].terrain=result.preview.frames[0].terrain;
  h.click('preview');await h.accept(0,result);
  h.get('world-generator-changed').checked=true;h.get('world-generator-changed').listeners.change();
  assert.ok(h.get('world-generator-board').children.some(node=>node.className.includes('world-edge-removed')));
  operation(h,'test/site/building/rooms/walls').click();
  h.get('world-generator-board-wrap').scrollLeft=45;h.get('world-generator-board-wrap').scrollTop=70;
  h.get('world-generator-zoom').value='48';h.get('world-generator-zoom').listeners.change();
  h.click('compare');assert.equal(h.get('world-generator-board-wrap').scrollTop,70);assert.equal(h.get('world-generator-board').style.width,'96px');
  h.click('previous');assert.match(h.get('world-generator-construction-selection').textContent,/Choose a construction step/);
  operation(h,'test/site').click();h.click('preview');assert.equal(h.get('world-generator-construction').hidden,true);assert.equal(h.get('world-generator-construction-tree').children.length,0);
  await h.accept(1,result);assert.match(h.get('world-generator-construction-selection').textContent,/Choose a construction step/);
  assert.equal(h.get('world-generator-stage').value,'2');
});

test("construction checkpoints isolate overlapping edits across several writer stages", () => {
  const h = harness();
  const operations = [
    {id:"primary",terrainChanges:[{index:0,before:"grass",after:"dirt"}],edgeChanges:[{key:{index:0,direction:"Right"},before:"None",after:"Wall"}]},
    {id:"civilization",terrainChanges:[{index:0,before:"dirt",after:"stone-floor"}],edgeChanges:[{key:{index:0,direction:"Right"},before:"Wall",after:"ClosedDoor"}]},
    {id:"connections",terrainChanges:[{index:0,before:"stone-floor",after:"gravel"}],edgeChanges:[{key:{index:0,direction:"Right"},before:"ClosedDoor",after:"None"}]}
  ];
  const edge=kind=>[{position:{x:0,y:0},direction:"Right",kind}];
  const batches=[{stepId:"primary",checkpointStepId:"base",firstOperation:0,endOperation:1},{stepId:"civilization",checkpointStepId:"primary",firstOperation:1,endOperation:2},{stepId:"connections",checkpointStepId:"civilization",firstOperation:2,endOperation:3}];
  h.context.checkpoints={terrainPalette:["grass","dirt","stone-floor","gravel"],frames:[
    {stepId:"base",terrain:"AAAA",edges:[],construction:[]},
    {stepId:"primary",terrain:"BAAA",edges:edge("Wall"),construction:operations.slice(0,1),constructionBatches:batches.slice(0,1)},
    {stepId:"civilization",terrain:"CAAA",edges:edge("ClosedDoor"),construction:operations.slice(0,2),constructionBatches:batches.slice(0,2)},
    {stepId:"connections",terrain:"DAAA",edges:[],construction:operations,constructionBatches:batches},
    {stepId:"final",terrain:"DAAA",edges:[],construction:operations,constructionBatches:batches}
  ]};
  for(const [id,expected] of [["primary","BAAA"],["civilization","CAAA"],["connections","DAAA"]])
    assert.equal(vm.runInContext(`constructionPreview(checkpoints,4,'${id}',false,2).terrain`,h.context),expected);
  assert.equal(vm.runInContext("constructionPreview(checkpoints,4,'connections',true,2).edges[0].kind",h.context),"ClosedDoor");
  assert.equal(vm.runInContext("constructionPreview(checkpoints,4,'connections',false,2).edges.length",h.context),0);
  assert.equal(h.context.checkpoints.frames[4].terrain,"DAAA");
});

test("route recipes round-trip every profile and bounded setting, with old codes explicitly disabled on load", () => {
  const h=harness();
  for(const profile of ['ForestTrail','MinorPath','Road','RockPassage']) for(const enabled of [0,1]) {
    const recipe=code.replace('-S',`-E1-I2-B1111-C1-NLow-VMedium-P2-Q${enabled}-TVeryHigh-J${profile}-L0-AHigh-U1-Z3-S`);
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${recipe}'))`,h.context),withEmptyLoot(recipe));
    h.enter('code-input',recipe);h.click('load');
    assert.equal(h.get('world-generator-routeProfile').value,profile);assert.equal(h.get('world-generator-throughRoutes').value,'VeryHigh');
    assert.equal(h.get('world-generator-explorationLinks').value,'High');assert.equal(h.get('world-generator-waterCrossings').value,'true');
  }
  h.enter('code-input',code);h.click('load');
  assert.equal(h.get('world-generator-routesEnabled').value,'false');assert.equal(h.get('world-generator-waterCrossings').value,'false');
  assert.match(h.get('world-generator-prefix').value,/-Q0-TLow-JMinorPath-L1-ALow-U0-Z2-MOff-D3-OEmpty$/);
  for(const suffix of ['-Q2-TLow-JRoad-L1-ALow-U0-Z2','-Q1-T5-JRoad-L1-ALow-U0-Z2','-Q1-TLow-JRail-L1-ALow-U0-Z2',
    '-Q1-TLow-JRoad-L1-A4-U0-Z2','-Q1-TLow-JRoad-L1-ALow-U2-Z2','-Q1-TLow-JRoad-L1-ALow-U0-Z0'])
    assert.throws(()=>vm.runInContext(`worldConfigurationFromCode('${code.replace('-S',suffix+'-S')}')`,h.context),/ordered WLD1/);
});

test("catalog route settings preserve world inputs and HTML declares enabled conservative defaults", async () => {
  const h=harness();h.opened.click();
  await h.accept(0,{maxBoardSize:120,presets:[{id:'connected',name:'Connected',settings:{},routes:{enabled:true,throughRoutes:"Medium",profile:'Road',connectSites:true,alternatives:"High",waterCrossings:false,intensity:1}}]});
  h.get('world-generator-preset').value='connected';h.get('world-generator-preset').listeners.change();h.click('preview');
  assert.equal(h.requests[1].body.code,code.split('-S')[0]+'-Q1-TMedium-JRoad-L1-AHigh-U0-Z1-OEmpty');
  assert.equal(h.get('world-generator-offsetX').value,'-8');assert.equal(h.get('world-generator-width').value,'2');
  const html=source('index.html').slice(source('index.html').indexOf('<dialog id="world-generator"'));
  assert.match(html,/id="world-generator-routesEnabled"><option value="true">Enabled/);
  assert.match(html,/id="world-generator-throughRoutes">.*?<option value="Low" selected>/);
  assert.match(html,/<option value="MinorPath" selected>/);
  assert.match(html,/id="world-generator-waterCrossings"><option value="false">Preserve Water/);
  assert.match(html,/<option value="true">Allow limited crossings \(Stepping Stones\)<\/option>/);
  assert.match(html,/permits Stepping Stones within construction limits; it does not guarantee crossings or connected islands/);
  for(const id of ['route-intents','route-centerlines','route-corridors','route-junctions','route-crossings','route-access'])
    assert.match(html,new RegExp(`id="world-generator-${id}"`));
  assert.match(html,/<summary>Routes: requests, costs and connection quality<\/summary>/);
});

test("water preset crossing permission can be overridden and survives recipe reload and preview submission", async () => {
  for (const id of ['LakeCountry', 'RiverValley']) {
    const h = harness(); h.opened.click();
    await h.accept(0, {maxBoardSize:120, presets:[{id, name:id, settings:{},
      enrichment:{enabled:true, intensity:2, forest:true, rock:true, water:true, grassland:true},
      civilization:{enabled:true, campsites:'Low', settlements:'Medium', preparationIntensity:2},
      population:{density:'Medium', difficulty:3}, routes:{
      enabled:true, throughRoutes:'Low', profile:'MinorPath', connectSites:true,
      alternatives:'Low', waterCrossings:true, intensity:2
    }}]});
    h.get('world-generator-preset').value = id; h.get('world-generator-preset').listeners.change();
    assert.equal(h.get('world-generator-waterCrossings').value, 'true');
    assert.match(h.get('world-generator-prefix').value, /-Q1-TLow-JMinorPath-L1-ALow-U1-Z2/);
    h.enter('waterCrossings', 'false'); h.get('world-generator-waterCrossings').listeners.change();
    assert.equal(h.get('world-generator-preset').value, '');
    const overridden = h.get('world-generator-prefix').value;
    assert.match(overridden, /-Q1-TLow-JMinorPath-L1-ALow-U0-Z2/);
    h.enter('code-input', overridden + '-S0123456789ABCDEF'); h.click('load');
    assert.equal(h.get('world-generator-waterCrossings').value, 'false');
    assert.equal(h.get('world-generator-offsetX').value, '-8');
    h.click('preview');
    assert.equal(h.requests[1].body.code, overridden + '-S0123456789ABCDEF');
    await h.accept(1, h.result(h.requests[1].body.code));
    assert.equal(h.get('world-generator-waterCrossings').value, 'false');
  }
});

test("invalid route controls reject submission without changing the retained preview", async () => {
  for(const [field,value,error] of [['throughRoutes','5',/throughRoutes/],['routeProfile','Unknown',/route profile/],
    ['explorationLinks','4',/explorationLinks/],['waterCrossings','yes',/waterCrossings/],['routeIntensity','0',/routeIntensity/]]) {
    const h=harness();h.enter('code-input',code);h.click('load');h.click('preview');await h.accept(0);
    const original=h.get('world-generator-board').children[0];h.enter(field,value);h.click('preview');
    assert.equal(h.requests.length,1);assert.match(h.get('world-generator-status').textContent,error);
    assert.equal(h.get('world-generator-board').children[0],original);
  }
});

function routedResult(h) {
  const result=constructionResult(h), bounds={x:0,y:0,width:2,height:2};
  const area=(id,spans)=>({id,bounds,spans,area:spans.reduce((sum,span)=>sum+span.length,0)});
  const contact=area('routes/shared/contact',[{y:0,x:1,length:1}]);
  const route={id:'routes/test',purpose:'ThroughRoute',profile:'MinorPath',corridor:area('routes/test/corridor',[{y:0,x:0,length:2},{y:1,x:1,length:1}]),
    geometry:{worldX:-8,worldY:12,runs:[{worldX:-8,worldY:12,direction:'East',length:1},{worldX:-7,worldY:12,direction:'South',length:1}]},
    start:{id:'north',anchorIndex:0,worldX:-8,worldY:12,kind:'Boundary',side:'North'},end:{id:'south',anchorIndex:3,worldX:-7,worldY:13,kind:'Boundary',side:'South'},
    verifiedFootprint:1,realizedWidth:1,preparation:{cost:9,treesRemoved:1,rockExcavated:1,waterFilled:1,changedTerrain:3,changedEdges:1},
    stats:{attempts:1,expandedStates:8,queuePeak:4,searchCost:27,length:3,turns:1,reusedCells:1},
    crossings:[{footprint:contact}],tunnelEntrances:[{anchorIndex:3,worldX:-7,worldY:13,edge:{index:1,direction:'Down'}}],materializationId:'routes/test/materialization-0',sourceRevision:0};
  const network={routes:[route],intents:[{id:'request/accepted',purpose:'ThroughRoute',profile:'MinorPath',startIndex:0,destinationIndex:3,status:'Accepted',routeId:route.id},
    {id:'request/unresolved',purpose:'SiteConnection',profile:'MinorPath',startIndex:2,destinationIndex:1,destinationObjectId:'test/site',status:'Unresolved',reason:'No legal portal'},
    {id:'request/remote',purpose:'SiteConnection',profile:'ForestTrail',startIndex:2,status:'DeliberatelyRemote',reason:'Authored remote Site'}],
    junctions:[{id:'routes/junction',contact,routeIds:[route.id,'routes/other']}],sharedSegments:[{id:'routes/shared',area:contact,routeIds:[route.id,'routes/other']}],
    siteAttachments:[{siteId:'test/site',portId:'test/site/access',routeIds:[route.id],networkComponent:0,ordinaryOpenAccess:false,doorsOpenableAccess:true}],
    alternatives:[{routeId:'routes/alternative',previousDistance:10,newDistance:6,newCorridorCells:4,sharedCorridorCells:2,addsCycle:true,bypassesSelectedBottleneck:true,independentSiteEntrance:false,connectsRegion:false}],
    topology:{boardRevision:4,components:[{label:0,area:3,routeIds:[route.id]}]},
    statistics:{searches:2,expandedStates:15,peakQueue:5,acceptedRoutes:1,changedTerrain:3,changedEdges:1,preparationCost:9,crossings:1,tunnelEntrances:1,planningWork:55,planningAttempts:2}};
  for(const i of [1,2]) {
    result.preview.frames[i].routeNetwork=structuredClone(network);
    result.preview.frames[i].sites[0].accessPorts=[{id:'test/site/access',ownerId:'test/site',anchorIndex:2,outwardIndex:3,direction:'East',verifiedWidth:1,
      approach:area('test/site/access/approach',[{y:1,x:0,length:1}]),permission:{exactOwnerId:'test/site',traverseCells:[2],writableCells:[],writableEdges:[]}}];
  }
  result.preview.frames[1].stage='PrimaryRoutes';result.preview.frames[2].stage='ExplorationLinks';
  return result;
}

test("route overlays draw supplied spans, world runs, exact ports and intended pairs without computing paths", async () => {
  const h=harness(), result=routedResult(h);h.click('preview');await h.accept(0,result);
  for(const name of ['route-centerlines','route-intents','route-corridors','route-junctions','route-crossings','route-access']) {
    h.get('world-generator-'+name).checked=true;h.get('world-generator-'+name).listeners.change();
  }
  const overlays=h.get('world-generator-board').children.filter(node=>node.dataset.routeKind);
  for(const kind of ['centerline','port','intent','intent-port','corridor','shared','junction','crossing','mouth','approach','access'])
    assert.ok(overlays.some(node=>node.dataset.routeKind===kind),kind);
  const lines=overlays.filter(node=>node.dataset.routeKind==='centerline');
  assert.equal(lines.length,2);assert.equal(lines[0].style.left,'25%');assert.equal(lines[0].style.width,'50%');assert.equal(lines[1].style.transform,'rotate(90deg)');
  assert.equal(overlays.filter(node=>node.dataset.routeKind==='corridor').length,2);
  assert.ok(overlays.some(node=>node.dataset.routeKind==='intent'&&node.title.includes('not a computed path')));
  assert.ok(overlays.some(node=>node.dataset.routeKind==='approach'&&node.title.includes('exact owner test/site')));
  assert.ok(overlays.every(node=>Object.keys(node.listeners).length===0));
  assert.match(h.get('world-generator-route-summary').textContent,/1 accepted Routes.*2 searches.*15 expanded states.*preparation cost 9.*1 unresolved/);
  const details=descendants(h.get('world-generator-route-details')).map(node=>node.textContent).join('\n');
  assert.match(details,/search cost 27.*Preparation cost 9/);assert.match(details,/Unresolved.*request\/unresolved/);assert.match(details,/No legal portal/);
  assert.match(details,/Adds cycle: yes; bypasses selected bottleneck: yes/);assert.match(details,/Ordinary open access: no; Doors-openable access: yes/);
  assert.match(details,/DeliberatelyRemote/);assert.match(details,/published approach 1 Cells/);
});

test("route metadata stays labeled as accepted while inspecting earlier construction and final handoff remains immutable", async () => {
  const h=harness(), result=routedResult(h), original=JSON.stringify(result);h.click('preview');await h.accept(0,result);
  h.get('world-generator-route-centerlines').checked=true;h.get('world-generator-route-centerlines').listeners.change();
  operation(h,'test/site/building/rooms/walls').click();h.click('compare');
  assert.match(h.get('world-generator-route-authority').textContent,/Accepted metadata for Exploration Links; also shown during Before/);
  assert.ok(h.get('world-generator-board').children.some(node=>node.dataset.routeKind==='centerline'));
  h.click('previous');assert.match(h.get('world-generator-route-authority').textContent,/Accepted metadata for Primary Routes/);
  h.click('previous');assert.equal(h.get('world-generator-route-inspector').hidden,true);
  assert.equal(h.get('world-generator-board').children.filter(node=>node.dataset.routeKind).length,0);
  h.click('export');h.click('designer');assert.equal(h.get('world-generator-transport').value,result.transport);
  assert.equal(JSON.stringify(h.designs[0]),original);assert.equal(JSON.stringify(result),original);
});

test("maximum-window route overlays scale with supplied spans and runs without Cell expansion", async () => {
  const h=harness(), result=routedResult(h);result.definition.board.width=result.window.width=120;result.definition.board.height=result.window.height=120;
  result.preview.formationLabels=Array(14400).fill(-1);
  for(const frame of result.preview.frames) { frame.terrain='A'.repeat(14400);frame.walkableComponents=Array(14400).fill(0); }
  const route=result.preview.frames[2].routeNetwork.routes[0];
  route.corridor.spans=[{x:0,y:0,length:120},...Array.from({length:119},(_,i)=>({x:119,y:i+1,length:1}))];route.corridor.area=239;
  route.geometry.runs=[{worldX:-8,worldY:12,direction:'East',length:119},{worldX:111,worldY:12,direction:'South',length:119}];
  route.end={id:'south',anchorIndex:14399,worldX:111,worldY:131,kind:'Boundary',side:'South'};
  h.click('preview');await h.accept(0,result);
  for(const kind of ['route-centerlines','route-corridors']) {h.get('world-generator-'+kind).checked=true;h.get('world-generator-'+kind).listeners.change();}
  const overlays=h.get('world-generator-board').children.filter(node=>node.dataset.routeKind);
  assert.equal(overlays.filter(node=>node.dataset.routeKind==='corridor').length,120);
  assert.equal(overlays.filter(node=>node.dataset.routeKind==='centerline').length,2);assert.equal(overlays.length,124);
  assert.ok(overlays.every(node=>Object.keys(node.listeners).length===0));
  assert.equal(h.get('world-generator-board').children.filter(node=>node.className.startsWith('cell ')).length,14400);
});


test("density controls round-trip every level independently of Population and preparation", () => {
  const h = harness();
  for (const level of ['None', 'Low', 'Medium', 'High', 'VeryHigh']) {
    const recipe = code.replace('-S', `-C1-N${level}-V${level}-P1-Q1-T${level}-JRoad-L1-A${level}-U0-Z3-MOff-D4-S`);
    h.enter('code-input', recipe); h.click('load');
    assert.equal(h.get('world-generator-campsiteDensity').value, level);
    assert.equal(h.get('world-generator-settlementDensity').value, level);
    assert.equal(h.get('world-generator-populationDensity').value, 'Off');
    assert.equal(h.get('world-generator-preparationIntensity').value, '1');
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${recipe}'))`, h.context), withEmptyLoot(recipe));
  }
});

test("settings summaries track edited and loaded values without generating or remounting a preview", async () => {
  const h = harness(); h.click('preview'); await h.accept(0);
  const cell = h.get('world-generator-board').children[0];
  h.enter('code-input', code.replace('-S', '-C1-NLow-VVeryHigh-P2-Q1-TMedium-JMinorPath-L1-ALow-U0-Z2-MLow-D3-S'));
  h.click('load');
  assert.equal(h.get('world-generator-summary-civilization').textContent, 'Very High settlements · Low campsites');
  assert.equal(h.get('world-generator-summary-world').textContent, '2×2 · Auto · Balanced');
  assert.match(h.get('world-generator-summary-routes').textContent, /Medium through · Low links · Medium intensity/);
  assert.match(h.get('world-generator-summary-population').textContent, /Low · Difficulty 3/);
  for (const key of ['world','civilization','routes','population','advanced']) {
    const panel = h.get(`world-generator-settings-${key}`);
    panel.open = true; panel.open = false;
    assert.deepEqual(panel.listeners, {}); // Native disclosure is independent of input/session listeners.
  }
  assert.equal(h.requests.length, 1);
  assert.equal(h.get('world-generator-board').children[0], cell);
  assert.equal(h.get('world-generator-settlementDensity').value, 'VeryHigh');
});

test("diagnostics start collapsed and warnings persist while inspecting earlier stages", async () => {
  const h = harness(), result = constructionResult(h);
  const final = result.preview.frames.at(-1);
  final.planningFailures = [{requestId:'omitted',code:'NoFootprint',reason:'No suitable footprint',attempts:3}];
  final.routeNetwork = { routes:[], intents:[{status:'Unresolved'}], siteAttachments:[], alternatives:[], junctions:[], sharedSegments:[] };
  final.campsiteDensity = { requested: 3, attempted: 2, accepted: 1, omitted: 2 };
  h.get('world-generator-diagnostic-panel').open = true;
  h.click('preview'); await h.accept(0, result);
  assert.equal(h.get('world-generator-diagnostic-panel').open, false);
  assert.equal(h.get('world-generator-warning-panel').hidden, false);
  assert.match(h.get('world-generator-warnings').textContent, /construction requests failed or omitted/);
  assert.match(h.get('world-generator-warnings').textContent, /unresolved route connections/);
  assert.match(h.get('world-generator-density-summary').textContent, /3 requested · 2 attempted · 1 accepted · 2 omitted/);
  h.click('previous');
  assert.equal(h.get('world-generator-warning-panel').hidden, false);
  h.click('investigate');
  assert.equal(h.get('world-generator-diagnostic-panel').open, true);
  assert.equal(h.get('world-generator-stage').value, String(result.preview.frames.length - 1));
});

test("native disclosures have keyboard semantics and keep the board before independently scrolling diagnostics", () => {
  const html = source('index.html').split('<dialog id="world-generator"')[1].split('</dialog>')[0];
  for (const key of ['world','civilization','routes','population','advanced']) {
    assert.match(html, new RegExp(`<details id="world-generator-settings-${key}" class="world-generator-setting"><summary>`));
  }
  assert.match(html, /<details id="world-generator-diagnostic-panel" class="world-generator-diagnostic-panel"><summary>/);
  assert.ok(html.indexOf('id="world-generator-board-wrap"') < html.indexOf('id="world-generator-diagnostic-panel"'));
  assert.match(source('styles.css'), /world-generator-diagnostic-scroll[^}]*max-height: 44dvh; overflow: auto/s);
  assert.match(source('styles.css'), /@media \(max-width: 850px\)[\s\S]*world-generator-result \{ grid-row: 1/);
  assert.match(source('styles.css'), /summary:focus-visible/);
});

test("unattempted route allocations still surface omission warnings and their reason", async () => {
  const h = harness(), result = stagedResult(h);
  result.preview.frames.at(-1).explorationLinkDensity = {
    requested: 3, attempted: 0, accepted: 0, omitted: 3, reason: 'No useful exploration candidates remain.'
  };
  h.click('preview'); await h.accept(0, result);
  assert.equal(h.get('world-generator-warning-panel').hidden, false);
  assert.match(h.get('world-generator-warnings').textContent, /3 route requests omitted/);
  assert.match(h.get('world-generator-result-summary').textContent, /3 warnings/);
  assert.match(h.get('world-generator-density-summary').textContent, /No useful exploration candidates remain/);
});

test("route planning failures are not mislabeled as failed site construction", async () => {
  const h = harness(), result = stagedResult(h);
  result.preview.frames.at(-1).planningFailures = [{generator:'Route',requestId:'route',code:'NoFootprint',reason:'No corridor',attempts:3}];
  h.click('preview'); await h.accept(0, result);
  assert.match(h.get('world-generator-warnings').textContent, /1 route planning failures/);
  assert.doesNotMatch(h.get('world-generator-warnings').textContent, /construction requests/);
});
