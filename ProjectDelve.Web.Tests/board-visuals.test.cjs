const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");
const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-visuals.js"), "utf8").replace(/^export /gm, "");
const catalog = () => { const context = vm.createContext({}); vm.runInContext(script, context); return code => vm.runInContext(code, context); };

test("artwork catalog resolves all sixteen local files while other content retains fallbacks", () => {
  const run = catalog();
  const names = ["barbarian", "zombie", "worker", "geomancer", "wizard", "rogue", "grunt", "skeleton-archer", "goblin", "troll"];
  const expected = names.map(name => `./art/units/${name}.webp`);
  const portraits = JSON.parse(run(`JSON.stringify(${JSON.stringify(names)}.map(name => boardVisuals.unit[name + "-type"].image))`));
  assert.deepEqual(portraits, expected);
  const terrain = JSON.parse(run('JSON.stringify(boardVisuals.terrain.StoneFloor.variants.map(v => v.image))'));
  assert.deepEqual(terrain, [1, 2, 3].map(i => `./art/terrain/stone-floor/0${i}.webp`));
  const edges = JSON.parse(run('JSON.stringify(["Wall", "ClosedDoor", "OpenDoor"].map(id => boardVisuals.edge[id].image))'));
  assert.deepEqual(edges, ["wall", "door-closed", "door-open"].map(name => `./art/edges/${name}.webp`));
  for (const url of [...portraits, ...terrain, ...edges]) {
    const file = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", url));
    assert.equal(file.toString("ascii", 0, 4), "RIFF", url);
    assert.equal(file.toString("ascii", 8, 12), "WEBP", url);
    assert.equal(file.readUInt32LE(4) + 8, file.length, url);
  }
  run('const node = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };');
  for (const [category, id] of [["unit", "cleric-type"], ["unit", "custom-type"], ["unit", "scenario:wizard"], ["terrain", "Grass"], ["terrain", "Unknown"]]) {
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
  const distribution = seed => `JSON.stringify(Array.from({length: 64}, (_, i) => terrainVariantIndex("${seed}", "StoneFloor", i % 8, Math.floor(i / 8), 3)))`;
  assert.equal(run(distribution("a")), reload(distribution("a")));
  assert.notEqual(run(distribution("a")), run(distribution("b")));
  assert.equal(new Set(JSON.parse(run(distribution("a")))).size, 3);
  run(`const node = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };
    boardVisuals.terrain.Fixture = { variants: [{image: "missing.webp"}, {image: "other.webp"}] };
    applyBoardVisual(node, "terrain", "Fixture", {seed: "a", x: 1, y: 2});`);
  assert.match(run('node.style["--visual-image"]'), /webp/);
  assert.match(run('node.style["--visual-detail"]'), /gradient/);
  run('applyBoardVisual(node, "terrain", "Unknown", {seed: "a", x: 1, y: 2});');
  assert.equal(run('node.style["--visual-image"]'), "none");
  assert.equal(run('node.style["--visual-fill"]'), "#666a68");
});

test("directional artwork overrides variants without rotating the authored Rail direction", () => {
  const run = catalog();
  run(`const node = { dataset: {}, style: { setProperty(key, value) { this[key] = value; } } };
    boardVisuals.terrain.Rail.directions = { North: { image: "north.webp" }, East: { image: "east.webp" } };
    applyBoardVisual(node, "terrain", "Rail", {seed: "a", x: 0, y: 0, direction: "North"});`);
  assert.equal(run('node.style["--visual-image"]'), 'url("north.webp")');
  run('applyBoardVisual(node, "terrain", "Rail", {seed: "a", x: 0, y: 0, direction: "East"});');
  assert.equal(run('node.style["--visual-image"]'), 'url("east.webp")');
});
