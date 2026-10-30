const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-visuals.js"), "utf8").replace(/^export /gm, "");
const catalog = () => { const context = vm.createContext({}); vm.runInContext(script, context); return code => vm.runInContext(code, context); };

test('assemblies reuse component artwork independently of their surface and identity', () => {
  const run = catalog();
  run(`const table={id:'a-table',name:'A Table',presentation:{recipe:'Table',layer:'Object'}};
    const assembly=surface=>({surface:{presentation:{recipe:surface}},components:[{slot:'furniture',definition:table}]});
    const sample=surface=>terrainLayers(surface,{seed:'fixture',x:2,y:4,assembly:assembly(surface)});
    const stone=sample('StoneFloor'),grass=sample('Grass');`);
  assert.match(run('stone.base.image'),/stone-floor/);
  assert.match(run('grass.base.image'),/grass/);
  assert.equal(run('stone.components[0].object.image'),run('grass.components[0].object.image'));
  assert.equal(run('stone.components[0].objectRotation'),run('grass.components[0].objectRotation'));
  assert.equal(run('stone.components[0].object.image'),run("terrainLayers('StoneFloorWithTable',{seed:'fixture',x:2,y:4}).object.image"));
  run(`const board={terrainTypes:[{id:'unheard-of',name:'Grass & Furniture',presentation:{recipe:'generic'},assembly:assembly('Grass')}]};`);
  assert.equal(run("terrainDisplay(board,{typeId:'unheard-of'}).kind"),'Grass');
  assert.equal(run("terrainDisplay(board,{typeId:'unheard-of'}).name"),'Grass & Furniture');
  assert.equal(run("Object.hasOwn(boardVisuals.terrain,'unheard-of')"),false);
});

test('canonical Table and Tree assemblies preserve every existing sampled layer', () => {
  const run = catalog();
  for(const [recipe,surface,component] of [['StoneFloorWithTable','StoneFloor','Table'],['Tree','Grass','Tree']])
    for(let i=0;i<12;i++){
      const context={seed:'legacy-fixture',x:i%4,y:Math.floor(i/4)};
      const assembly={surface:{presentation:{recipe:surface}},components:[{slot:'furniture',definition:{presentation:{recipe:component,layer:'Object'}}}]};
      assert.equal(run(`JSON.stringify(terrainLayers(${JSON.stringify(recipe)},${JSON.stringify(context)}))`),
        run(`JSON.stringify(terrainLayers(${JSON.stringify(recipe)},${JSON.stringify({...context,assembly})}))`));
    }
});

test('unfamiliar component recipes have explicit fallbacks and orientation is independent of floor rotation', () => {
  const run = catalog();
  run(`const assembly={surface:{presentation:{recipe:'Grass'}},components:[
    {slot:'furniture',definition:{name:'Odd Plinth',presentation:{recipe:'undiscovered-model',layer:'Object'}},quarterTurns:3},
    {slot:'seal',definition:{name:'First Seal',presentation:{recipe:'unknown-stamp',layer:'Decal'}},quarterTurns:0},
    {slot:'badge',definition:{name:'Other Seal',presentation:{recipe:'unknown-stamp',layer:'Decal'}},quarterTurns:1}]};
    const before=terrainLayers('Grass',{seed:'fixture',x:1,y:2});
    const after=terrainLayers('Grass',{seed:'fixture',x:1,y:2,assembly});`);
  assert.equal(run('before.rotation'),run('after.rotation'));
  assert.equal(run('after.components[0].fallback'),true);
  assert.equal(run('after.components[0].object.glyph'),'?');
  assert.equal(run('after.components[0].objectRotation'),270);
  assert.equal(run('after.components[1].objectRotation'),0);
  assert.equal(run('after.components[2].objectRotation'),90);
  assert.notEqual(run('after.components[1].x'),run('after.components[2].x'));
  assert.equal(run('after.components[1].decal'),true);
  for (const recipe of ['constructor', '__proto__']) {
    run(`assembly.components[0].definition.presentation.recipe=${JSON.stringify(recipe)};`);
    assert.equal(run("terrainLayers('Grass',{assembly}).components[0].fallback"),true);
    assert.equal(run("terrainLayers('Grass',{assembly}).components[0].object.glyph"),'?');
  }
});

test('Terrain presentation resolves declared names and recipes without registering unfamiliar IDs', () => {
  const run = catalog();
  run(`const terrainBoard = {terrainTypes:[
    {id:'odd-plinth',name:'Velvet Puzzle',presentation:{recipe:'StoneFloorWithTable'}},
    {id:'odd-paper',name:'Moon Paper',presentation:{recipe:'unpublished-material'}}]};`);
  assert.equal(run("terrainDisplay(terrainBoard,{typeId:'odd-plinth',kind:'StoneFloor'}).kind"),'StoneFloorWithTable');
  assert.equal(run("terrainDisplay(terrainBoard,{typeId:'odd-plinth'}).name"),'Velvet Puzzle');
  assert.equal(run("terrainDisplay(terrainBoard,{typeId:'odd-paper'}).kind"),'default');
  assert.equal(run("terrainDisplay(terrainBoard,{typeId:'odd-paper'}).fallback"),true);
  assert.equal(run("terrainDisplay(terrainBoard,{typeId:'missing',kind:'Lava'}).kind"),'default');
  assert.equal(run("terrainDisplay({}, {kind:'Lava'}).kind"),'Lava');
  assert.equal(run("Object.hasOwn(boardVisuals.terrain,'odd-plinth')"),false);
});

test("Solid Rock uses full-Cell bedrock variants and retains a distinct material fallback", () => {
  const run = catalog();
  assert.notEqual(run('terrainLayers("SolidRock").base.fill'), run('terrainLayers("Tree").base.fill'));
  assert.ok(run('terrainLayers("SolidRock").base.detail').includes("gradient"));
  assert.match(run('terrainLayers("SolidRock").base.image'), /^\.\/art\/terrain\/solid-rock\/0[123]\.webp$/);
  assert.equal(run('terrainLayers("SolidRock").object'), null);
});

test("racing assets use shared Grass, three stable rotating Fruit Tree objects and separate item icons", () => {
  const run = catalog();
  assert.equal(run('boardVisuals.terrain.FruitTree.baseTerrain'), "Grass");
  assert.equal(run('boardVisuals.terrain.FruitTree.object.rotation'), true);
  const objects = JSON.parse(run('JSON.stringify(boardVisuals.terrain.FruitTree.object.variants.map(v => v.image))'));
  assert.deepEqual(objects, [1,2,3].map(i => `./art/terrain/objects/fruit-tree/0${i}.webp`));
  const items = ["fruit", "banana", "mushroom", "shell"];
  for (const id of items) {
    assert.equal(run(`boardVisuals.resource.${id}.image`), `./art/resources/${id}.webp`);
    assert.ok(run(`boardVisuals.resource.${id}.glyph`));
  }
  assert.notEqual(run('boardVisuals.resource.banana.image'), run('boardVisuals.cellMarker["banana-peel"].image'));
  assert.equal(run('boardVisuals.machine["slot-machine"].baseTerrain'), "StoneFloor");
  for (const file of [...objects, ...items.map(id => `./art/resources/${id}.webp`), "./art/machines/slot-machine.webp"])
    assert.ok(fs.existsSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", file)));
  assert.equal(run('JSON.stringify(terrainLayers("FruitTree", {seed:"fixture",x:2,y:3}))'),
    run('JSON.stringify(terrainLayers("FruitTree", {seed:"fixture",x:2,y:3}))'));
});

test("artwork catalog resolves registered local files while other content retains fallbacks", () => {
  const run = catalog();
  const names = ["barbarian", "zombie", "worker", "chief-mechanic", "diamond-kart", "geomancer", "wizard", "rogue", "grunt", "skeleton-archer", "goblin", "troll",
    "cleric", "shaman", "ghost", "bomb-imp", "displacer-demon", "labyrinth-shaper", "red-dragon", "locomotive",
    "royal-peasant", "goblin-peasant", "castle-guard", "tower-troll",
    "royal-bishop", "goblin-bishop", "royal-knight", "hobby-knight",
    "royal-queen", "witch-queen", "royal-king", "goblin-king"];
  const expected = names.map(name => `./art/units/${name}.webp`);
  const portraits = JSON.parse(run(`JSON.stringify(${JSON.stringify(names)}.map(name => boardVisuals.unit[name + "-type"].image))`));
  assert.deepEqual(portraits, expected);
  const terrain = [];
  for (const [id, folder] of [["StoneFloor", "stone-floor"], ["SolidRock", "solid-rock"], ["Gravel", "gravel"], ["Asphalt", "asphalt"], ["Grass", "grass"], ["Water", "water"], ["Lava", "lava"]]) {
    const variants = JSON.parse(run(`JSON.stringify(boardVisuals.terrain.${id}.variants.map(v => v.image))`));
    assert.deepEqual(variants, [1, 2, 3].map(i => `./art/terrain/${folder}/0${i}.webp`));
    terrain.push(...variants);
  }
  const edges = JSON.parse(run('JSON.stringify(["Wall", "ClosedDoor", "OpenDoor"].map(id => boardVisuals.edge[id].image))'));
  assert.deepEqual(edges, ["wall", "door-closed", "door-open"].map(name => `./art/edges/${name}.webp`));
  const objects = JSON.parse(run('JSON.stringify([boardVisuals.machine["diamond-press"].image, boardVisuals.resource.coal.image, boardVisuals.resource.diamond.image])'));
  assert.deepEqual(objects, ["./art/machines/diamond-press.webp", "./art/resources/coal.webp", "./art/resources/diamond.webp"]);
  for (const url of [...portraits, ...terrain, ...edges, ...objects]) {
    const file = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", url));
    assert.equal(file.toString("ascii", 0, 4), "RIFF", url);
    assert.equal(file.toString("ascii", 8, 12), "WEBP", url);
    assert.equal(file.readUInt32LE(4) + 8, file.length, url);
  }
  run('const node = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };');
  for (const [category, id] of [["unit", "fixture-missing-portrait"], ["unit", "custom-type"], ["unit", "scenario:wizard"], ["terrain", "FixtureMissingTerrain"], ["terrain", "Unknown"]]) {
    run(`applyBoardVisual(node, ${JSON.stringify(category)}, ${JSON.stringify(id)});`);
    assert.equal(run('node.style["--visual-image"]'), "none");
    assert.match(run('node.style["--visual-detail"]'), /gradient/);
  }
});

test("terrain hash has fixed cross-runtime vectors and does not use random state", () => {
  const run = catalog();
  assert.equal(run('terrainVariantIndex("scenario-a", "StoneFloor", 0, 0, 3)'), 0);
  assert.equal(run('terrainVariantIndex("scenario-a", "StoneFloor", 2, 4, 97)'), 84);
  assert.equal(run('terrainVariantIndex("scenario-b", "StoneFloor", 2, 4, 97)'), 85);
  assert.equal(run('terrainVariantIndex("r\\u00e4v\\ud83d\\udc3a", "Grass", 7, 3, 97)'), 87);
  assert.equal(run('terrainVariantIndex("scenario-a", "StoneFloor", 0, 0, 0)'), 0);
  assert.doesNotMatch(script, /Math\.random/);
});

test("terrain variants survive reload, differ across seeds, and preserve category fallbacks", () => {
  const run = catalog(), reload = catalog();
  for (const id of ["StoneFloor", "SolidRock", "Asphalt", "Gravel", "Grass", "Water", "Lava"]) {
    const distribution = seed => `JSON.stringify(Array.from({length: 64}, (_, i) => terrainVariantIndex("${seed}", "${id}", i % 8, Math.floor(i / 8), 3)))`;
    assert.equal(run(distribution("a")), reload(distribution("a")));
    assert.notEqual(run(distribution("a")), run(distribution("b")));
    assert.equal(new Set(JSON.parse(run(distribution("a")))).size, 3);
    run('var tile = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };');
    run(`applyBoardVisual(tile, "terrain", "${id}", {seed: "a", x: 2, y: 3});`);
    const index = run(`terrainVariantIndex("a", "${id}", 2, 3, 3)`);
    assert.equal(run('tile.dataset.visualVariant'), String(index));
    assert.equal(run('tile.style["--visual-image"]'), run(`'url(' + JSON.stringify(boardVisuals.terrain.${id}.variants[${index}].image) + ')'`));
    assert.match(run('tile.style["--visual-detail"]'), /gradient/);
  }
  run(`const node = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };
    boardVisuals.terrain.Fixture = { variants: [{image: "missing.webp"}, {image: "other.webp"}] };
    applyBoardVisual(node, "terrain", "Fixture", {seed: "a", x: 1, y: 2});`);
  assert.match(run('node.style["--visual-image"]'), /webp/);
  assert.match(run('node.style["--visual-detail"]'), /gradient/);
  run('applyBoardVisual(node, "terrain", "UnregisteredTerrainFixture", {seed: "a", x: 1, y: 2});');
  assert.equal(run('node.style["--visual-image"]'), "none");
  assert.equal(run('node.style["--visual-fill"]'), "#666a68");
});

test("explicit directional overrides remain supported for test-owned terrain", () => {
  const run = catalog();
  run(`const node = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };
    boardVisuals.terrain.FixtureDirectional = { directions: { North: { image: "north.webp" }, East: { image: "east.webp" } } };
    applyBoardVisual(node, "terrain", "FixtureDirectional", {seed: "a", x: 0, y: 0, direction: "North"});`);
  assert.equal(run('node.style["--visual-image"]'), 'url("north.webp")');
  run('applyBoardVisual(node, "terrain", "FixtureDirectional", {seed: "a", x: 0, y: 0, direction: "East"});');
  assert.equal(run('node.style["--visual-image"]'), 'url("east.webp")');
  assert.equal(run('node.dataset.visualRotation'), "0");
});

test("composite mappings reuse base slots and choose object slots independently", () => {
  const run = catalog();
  for (const [id, base, folder] of [["Tree", "Grass", "tree"], ["FruitTree", "Grass", "fruit-tree"], ["StoneFloorWithTable", "StoneFloor", "table"]]) {
    const pairs = new Set();
    for (let y = 0; y < 8; y++) for (let x = 0; x < 8; x++) {
      const layers = JSON.parse(run(`JSON.stringify(terrainLayers("${id}", {seed:"fixture", x:${x}, y:${y}}))`));
      assert.equal(layers.index, run(`terrainVariantIndex("fixture", "${base}", ${x}, ${y}, 3)`));
      assert.equal(layers.base.image, run(`boardVisuals.terrain.${base}.variants[${layers.index}].image`));
      assert.equal(layers.object.image, `./art/terrain/objects/${folder}/0${layers.objectIndex + 1}.webp`);
      assert.ok(fs.existsSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", layers.object.image)));
      pairs.add(`${layers.index},${layers.objectIndex}`);
    }
    assert.equal(pairs.size, 9);
  }
});

test("quarter turns are opt-in, deterministic and independent of variants and the other layer", () => {
  const run = catalog(), reload = catalog();
  for (const id of ["StoneFloor", "SolidRock", "Asphalt", "Gravel", "Rail", "Grass", "Water", "Lava", "Tree", "FruitTree", "StoneFloorWithTable"]) {
    const distribution = seed => `JSON.stringify(Array.from({length:256}, (_,i) => terrainLayers("${id}", {seed:"${seed}", x:i%16, y:Math.floor(i/16)})))`;
    const first = run(distribution("fixture"));
    assert.equal(first, reload(distribution("fixture")));
    assert.notEqual(first, run(distribution("other")));
    const samples = JSON.parse(first);
    assert.deepEqual([...new Set(samples.map(s => s.rotation))].sort((a,b) => a-b), [0,90,180,270]);
    if (samples[0].object) {
      assert.equal(new Set(samples.map(s => `${s.rotation},${s.objectRotation}`)).size, 16);
      assert.equal(new Set(samples.map(s => `${s.objectIndex},${s.objectRotation}`)).size, 12);
    }
    assert.equal(new Set(samples.map(s => `${s.index},${s.rotation}`)).size, 12);
  }
  const before = run('JSON.stringify(terrainLayers("Tree", {seed:"fixture", x:3, y:5}))');
  run('boardVisuals.terrain.Tree.object.variants.push({image:"test-owned.webp"});');
  const after = run('JSON.stringify(terrainLayers("Tree", {seed:"fixture", x:3, y:5}))');
  assert.equal(JSON.parse(before).rotation, JSON.parse(after).rotation);
  assert.equal(JSON.parse(before).objectRotation, JSON.parse(after).objectRotation);
  assert.equal(JSON.parse(before).base.image, JSON.parse(after).base.image);
  for (const category of ["unit", "edge", "machine", "resource"])
    assert.equal(run(`Object.values(boardVisuals.${category}).some(v => v.rotation)`), false);
  assert.equal(run('terrainLayers("Rail").base.image'), run('terrainLayers("Gravel").base.image'));
  assert.equal(run('terrainLayers("MissingFixture").rotation'), 0);
  run('boardVisuals.terrain.Tree.object.rotation = false; boardVisuals.terrain.Grass.rotation = false;');
  assert.equal(run('terrainLayers("Tree").rotation'), 0);
  assert.equal(run('terrainLayers("Tree").objectRotation'), 0);
});

test("Rail derives incoming ports from pointing neighbors and has only a visual opposite fallback", () => {
  const run = catalog();
  run('var tiles = new Map();');
  for (const [direction, ports] of [["North", [0,2]], ["East", [1,3]], ["South", [0,2]], ["West", [1,3]]])
    assert.deepEqual(JSON.parse(run(`JSON.stringify(railConnections(tiles, 2, 2, "${direction}"))`)), ports);
  run(`tiles.set("2,1", {kind:"Rail", direction:"South"});
    tiles.set("3,2", {kind:"Rail", direction:"East"});
    tiles.set("2,3", {kind:"Gravel", direction:"North"});
    tiles.set("1,1", {kind:"Rail", direction:"East"});`);
  assert.equal(run('JSON.stringify(railConnections(tiles,2,2,"East"))'), '[0,1]');
  run('tiles.set("1,2", {kind:"Rail", direction:"East"});');
  assert.equal(run('JSON.stringify(railConnections(tiles,2,2,"East"))'), '[0,1,3]');
  run('tiles.set("2,3", {kind:"Rail", direction:"North"});');
  assert.equal(run('JSON.stringify(railConnections(tiles,2,2,"East"))'), '[0,1,2,3]');
  run('tiles.get("2,1").direction = "North";');
  assert.equal(run('JSON.stringify(railConnections(tiles,2,2,"East"))'), '[1,2,3]');
  run('tiles = new Map([["3,2", {kind:"Rail",direction:"West"}]]);');
  assert.equal(run('JSON.stringify(railConnections(tiles,2,2,"East"))'), '[1,3]');
});

test("all four undirected rail shapes align every distinct mask by quarter turns", () => {
  const run = catalog();
  const shapes = {straight:[0,2], curve:[0,1], tee:[0,1,2], cross:[0,1,2,3]};
  const covered = new Set();
  for (const [shape, ports] of Object.entries(shapes)) for (let turn=0; turn<4; turn++) {
    const mask = ports.map(p => (p+turn)%4).sort(); covered.add(mask.join());
    const actual = JSON.parse(run(`JSON.stringify(railGeometry(${JSON.stringify(mask)}))`));
    assert.equal(actual.shape, shape);
    assert.deepEqual(ports.map(p => (p+actual.rotation/90)%4).sort(), mask);
    assert.ok(fs.existsSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", run(`boardVisuals.terrain.Rail.tracks.${shape}`))));
  }
  assert.equal(covered.size, 11);
});
