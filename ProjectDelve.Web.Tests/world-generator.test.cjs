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
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${recipe}'))`,h.context),recipe);
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
  assert.equal(h.requests[1].body.code,newer.replace('-S','-E0-I2-B1111-C0-N2-V1-P2-S')); await h.accept(1,h.result(newer)); await h.accept(0);
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
  assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${enriched}'))`,h.context),enriched);
  h.enter('code-input',enriched);h.click('load');h.click('preview');assert.equal(h.requests[0].body.code,enriched.replace('-S','-C0-N2-V1-P2-S'));
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
  for (const suffix of ['-C1-N2-V1-P2','-C0-N0-V0-P1','-E1-I3-B1111-C1-N8-V4-P3']) {
    const complete = code.replace('-S',`${suffix}-S`);
    assert.equal(vm.runInContext(`worldConfigurationCode(worldConfigurationFromCode('${complete}'))`,h.context),complete);
    h.enter('code-input',complete); h.click('load');
    assert.equal(h.get('world-generator-civilizationEnabled').value, suffix.includes('-C1-') ? 'true' : 'false');
  }
  for (const suffix of ['-C2-N2-V1-P2','-C1-N9-V1-P2','-C1-N2-V5-P2','-C1-N2-V1-P0'])
    assert.throws(()=>vm.runInContext(`worldConfigurationFromCode('${code.replace('-S',`${suffix}-S`)}')`,h.context),/ordered WLD1/);
  h.enter('campsiteCount','9'); h.click('preview'); assert.equal(h.requests.length,0); assert.match(h.get('world-generator-status').textContent,/campsiteCount/);
});

test("catalog civilization presets update only authored settings", async () => {
  const h=harness(); h.opened.click();
  await h.accept(0,{maxBoardSize:120,presets:[{id:'settled',name:'Settled',settings:{},civilization:{enabled:true,campsites:3,settlements:2,preparationIntensity:3}}]});
  h.get('world-generator-preset').value='settled'; h.get('world-generator-preset').listeners.change(); h.click('preview');
  assert.equal(h.requests[1].body.code,code.split('-S')[0]+'-C1-N3-V2-P3');
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
