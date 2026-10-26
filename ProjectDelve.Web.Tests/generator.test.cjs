const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const source = name => fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", name), "utf8");
const code = "DNG1-HVM-30x20-R8-C2-D2-N2-L3-OR2-F1-S0123456789ABCDEF";
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
  for (const [key, value] of Object.entries({ width:"30", height:"20", rooms:"8", connectivity:"2", doors:"2", density:"2", difficulty:"3", objective:"reach", required:"2", fog:"1", seed:"", view:"enhanced" })) get(`generator-${key}`).value = value;
  const opened = new Node(), requests = [], plays = [], designs = [], events = {};
  const context = vm.createContext({ structuredClone, Uint32Array, document: { getElementById: get, querySelectorAll: () => [opened], createElement: () => new Node() },
    crypto: { getRandomValues: array => { array[0] = 1; array[1] = 2; return array; } }, navigator: { clipboard: { writeText: async () => {} } },
    addEventListener: (name, callback) => events[name] = callback,
    fetch: (url, options) => new Promise(resolve => requests.push({ url, body: JSON.parse(options.body), resolve })),
    delvePlayGenerated: async value => { plays.push(value); return true; }, delveOpenGenerated: value => { designs.push(value); return true; },
    applyBoardVisual: () => {}, styleSide: () => {} });
  vm.runInContext(source("generator-state.js").replace(/^export /gm, "") + "\n" + source("generator.js").replace(/^import .*;\r?\n/gm, ""), context);
  const click = name => get(`generator-${name}`).click();
  const enter = (name, value) => { get(`generator-${name}`).value = value; get(`generator-${name}`).listeners.input?.(); };
  const result = complete => ({ code:complete, prefix:complete.split("-S")[0], fingerprint:"test", transport:"DELVE1:fixture",
    definition: { board:{ width:14, height:14, defaultTerrain:"SolidRock", cells:[], edges:[] }, units:[] }, diagnostics:{ rooms:[], monsters:0, occupiedRooms:0, destinationDistance:10 } });
  return { nodes, get, click, enter, requests, plays, designs, opened, context, result };
}
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
