const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('ProjectDelve.Web/wwwroot/expedition.js', 'utf8').replace(/^export /gm, '');
function harness() {
  class Element {
    constructor(tag) { this.tag = tag; this.children = []; this.listeners = {}; this.value = ''; this.textContent = ''; this.hidden = false; this.disabled = false; this.dataset = {}; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    addEventListener(name, action) { this.listeners[name] = action; }
    setAttribute() {}
    querySelectorAll(tag) { return this.children.flatMap(c => [...(c.tag === tag ? [c] : []), ...c.querySelectorAll(tag)]); }
    reportValidity() { return true; }
  }
  const roots = new Map();
  const get = id => { const find = item => item.id === id ? item : item.children.map(find).find(Boolean); for (const root of roots.values()) { const found = find(root); if (found) return found; } if (!roots.has(id)) roots.set(id, new Element('section')); return roots.get(id); };
  const calls = [], events = {}, location = { hash: '#play' };
  let snapshot = { expedition: null }, success = true;
  const context = vm.createContext({ document: { getElementById: get, createElement: tag => new Element(tag) }, location, crypto: { getRandomValues: a => { a[0] = 123; return a; } }, Uint32Array,
    addEventListener: (name, fn) => events[name] = fn, fetch: async () => ({ ok: true, json: async () => ({ expeditions: [], presets: [] }) }),
    getSnapshot: () => snapshot, command: async (...args) => { calls.push(args); return success; } });
  vm.runInContext(source + '\nui = mountExpeditions({command, getSnapshot, ready: () => true, errorMessage: () => "Storage unavailable"});', context);
  return { context, get, calls, events, location, update(value) { snapshot = value; vm.runInContext('ui.update()', context); }, fail() { success = false; } };
}
const expedition = { id: 'test-run', name: 'Test Journey', seed: '42', region: { x: -1, y: 2 }, completedRegions: 3, exitCount: 1, exitRequired: 2, hasUnsavedChanges: true };
test('Expedition controls follow authoritative metadata, including success authorization', () => {
  const h = harness(); assert.equal(h.get('expedition-bar').hidden, true);
  h.update({ expedition });
  assert.equal(h.get('expedition-bar').hidden, false);
  assert.equal(h.get('expedition-continue').hidden, true);
  h.update({ expedition: { ...expedition, transitionId: 'receipt-1' } });
  assert.equal(h.get('expedition-continue').hidden, false);
});
test('save, leave failure and retry preserve actionable feedback and command contract', async () => {
  const h = harness(); h.update({ expedition });
  await h.get('expedition-save').listeners.click();
  assert.equal(h.calls[0][0], 'expedition/save'); assert.match(h.get('expedition-table-status').textContent, /saved/);
  h.fail(); assert.equal(await h.context.delveLeaveExpedition(), false);
  assert.equal(h.calls[1][0], 'expedition/leave'); assert.match(h.get('expedition-table-status').textContent, /Storage unavailable.*retry/);
});
test('closing warns only for authoritative unsaved Expedition changes', () => {
  const h = harness(); let prevented = 0;
  h.events.beforeunload({ preventDefault: () => prevented++ }); assert.equal(prevented, 0);
  h.update({ expedition }); h.events.beforeunload({ preventDefault: () => prevented++ }); assert.equal(prevented, 1);
});
test('duplicate operations are guarded and failure releases the action for retry', async () => {
  const context = vm.createContext({}); vm.runInContext(source, context);
  let resolve, count = 0;
  context.command = () => { count++; return new Promise(r => resolve = r); };
  vm.runInContext('actions = createExpeditionActions({command})', context);
  const first = vm.runInContext('actions.run("continue", { transitionId: "receipt" })', context);
  assert.equal(await vm.runInContext('actions.run("continue")', context), false); assert.equal(count, 1);
  resolve(false); assert.equal(await first, false); assert.equal(vm.runInContext('actions.pending', context), false);
});
test('new Expedition sends authored inputs and Continue forwards the server transition identity', async () => {
  const h = harness(); h.location.hash = '#expeditions';
  h.get('expedition-name').value = 'Northern Road'; h.get('expedition-seed').value = '42';
  h.get('expedition-preset').value = 'Woodland';
  await h.get('expedition-new').listeners.submit({ preventDefault() {} });
  assert.equal(h.calls[0][0], 'expedition/create');
  assert.deepEqual(JSON.parse(JSON.stringify(h.calls[0][1])), { expeditionName: 'Northern Road', expeditionSeed: '42', expeditionRecipe: 'Woodland' });
  assert.equal(h.location.hash, '#play');
  h.update({ expedition: { ...expedition, transitionId: 'approved-handoff' } });
  await h.get('expedition-continue').listeners.click();
  assert.equal(h.calls[1][0], 'expedition/continue'); assert.equal(h.calls[1][1].transitionId, 'approved-handoff');
});

test('a generated unsigned seed is accepted as hexadecimal and reused after failure', async () => {
  const h = harness(); h.get('expedition-name').value = 'Hex journey'; h.fail();
  const submit = () => h.get('expedition-new').listeners.submit({ preventDefault() {} });
  await submit();
  const seed = h.calls[0][1].expeditionSeed;
  assert.match(seed, /^0x[0-9a-f]{16}$/);
  await submit();
  assert.equal(h.calls[1][1].expeditionSeed, seed);
});
