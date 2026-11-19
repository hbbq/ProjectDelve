const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const source = fs.readFileSync('ProjectDelve.Web/wwwroot/expedition.js', 'utf8').replace(/^export /gm, '');
function harness({ fetchResponse, copyFails = false } = {}) {
  class Element {
    constructor(tag) { this.tag = tag; this.children = []; this.listeners = {}; this.value = ''; this.textContent = ''; this.hidden = false; this.disabled = false; this.dataset = {}; }
    append(...children) { this.children.push(...children); }
    replaceChildren(...children) { this.children = children; }
    addEventListener(name, action) { this.listeners[name] = action; }
    setAttribute() {}
    focus() {}
    select() { this.selected = true; }
    querySelectorAll(tag) { return this.children.flatMap(c => [...(c.tag === tag ? [c] : []), ...c.querySelectorAll(tag)]); }
    reportValidity() { return true; }
  }
  const roots = new Map();
  const get = id => { const find = item => item.id === id ? item : item.children.map(find).find(Boolean); for (const root of roots.values()) { const found = find(root); if (found) return found; } if (!roots.has(id)) roots.set(id, new Element('section')); return roots.get(id); };
  const calls = [], reads = [], clipboard = [], events = {}, location = { hash: '#play' };
  let snapshot = { expedition: null }, success = true;
  const context = vm.createContext({ document: { getElementById: get, createElement: tag => new Element(tag) }, location, crypto: { getRandomValues: a => { a[0] = 123; return a; } }, Uint32Array,
    addEventListener: (name, fn) => events[name] = fn,
    fetch: async (...args) => { reads.push(args); return fetchResponse ? fetchResponse(...args) : { ok: true, json: async () => ({ expeditions: [], presets: [] }) }; },
    navigator: { clipboard: { writeText: async value => { if (copyFails) throw new Error('Clipboard denied'); clipboard.push(value); } } },
    getSnapshot: () => snapshot, command: async (...args) => { calls.push(args); return success; } });
  vm.runInContext(source + '\nui = mountExpeditions({command, getSnapshot, ready: () => true, errorMessage: () => "Storage unavailable"});', context);
  return { context, get, calls, reads, clipboard, events, location, update(value) { snapshot = value; vm.runInContext('ui.update()', context); }, fail() { success = false; } };
}
const expedition = { id: 'test-run', name: 'Test Journey', seed: '42', region: { x: -1, y: 2 }, completedRegions: 3, exitCount: 1, exitRequired: 2, hasUnsavedChanges: true };
test('Copy DELVE1 uses a session-bound read and distinguishes the starting Scenario', async () => {
  const h = harness({ fetchResponse: () => ({ ok: true, text: async () => 'DELVE1:fixture' }) });
  h.update({ sessionId: 'initial session', expedition });
  await h.get('expedition-copy-delve1').listeners.click();
  assert.equal(h.reads[0][0], '/api/expeditions/current/scenario?sessionId=initial%20session');
  assert.equal(h.reads[0][1].cache, 'no-store');
  assert.deepEqual(h.clipboard, ['DELVE1:fixture']);
  assert.deepEqual(h.calls, []);
  assert.match(h.get('expedition-table-status').textContent, /Starting Scenario.*Live progress/);
});
test('Clipboard denial offers a manual selection and clears it on region handoff', async () => {
  const h = harness({ copyFails: true, fetchResponse: () => ({ ok: true, text: async () => 'DELVE1:fixture' }) });
  h.update({ sessionId: 'old', expedition });
  await h.get('expedition-copy-delve1').listeners.click();
  const field = h.get('expedition-scenario-transport');
  assert.equal(field.hidden, false);
  assert.equal(field.readOnly, true);
  assert.equal(field.value, 'DELVE1:fixture');
  assert.equal(field.selected, true);
  h.update({ sessionId: 'new', expedition });
  assert.equal(field.hidden, true);
  assert.equal(field.value, '');
});
test('A late export after a region handoff stays off the clipboard', async () => {
  let finish;
  const h = harness({ fetchResponse: () => new Promise(resolve => { finish = resolve; }) });
  h.update({ sessionId: 'old', expedition });
  const request = h.get('expedition-copy-delve1').listeners.click();
  h.update({ sessionId: 'new', expedition });
  finish({ ok: true, text: async () => 'DELVE1:old' });
  await request;
  assert.deepEqual(h.clipboard, []);
  assert.equal(h.get('expedition-scenario-transport').hidden, true);
  assert.equal(h.get('expedition-copy-delve1').disabled, false);
});
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
