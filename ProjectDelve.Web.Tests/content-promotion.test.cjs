const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const path = require('node:path');
class Node {
  constructor(tag, document) { this.tagName = tag; this.ownerDocument = document; this.children = []; this.listeners = {}; }
  append(...nodes) { this.children.push(...nodes); }
  replaceChildren(...nodes) { this.children = nodes; }
  addEventListener(name, handler) { this.listeners[name] = handler; }
  all() { return this.children.flatMap(n => [n, ...n.all()]); }
}
function harness() {
  const document = { createElement: tag => new Node(tag, document) }, root = document.createElement('section');
  let stamp = '1', published = 0, reject = false;
  const calls = [], errors = [];
  const entries = [{ key: 'lootCards:gift:one', family: 'lootCards', id: 'gift:one', name: 'Gift', origin: 'library-local', packageId: 'kit' },
    { key: 'equipment:boots', family: 'equipment', id: 'boots', name: 'Boots', origin: 'library-local', packageId: 'kit' }];
  const context = vm.createContext({ document, Date, root, options: {
    source: () => ({ kind: 'library', packageId: 'kit', revision: 3 }), entries: () => entries, stamp: () => stamp,
    call: async (url, body) => {
      calls.push({ url, body: JSON.parse(JSON.stringify(body)) }); if (reject) throw new Error('Stale destination. Re-preview.');
      if (url.endsWith('preview')) return { request: body, sourceDigest: 'source', destinationRevision: 'destination', closure: [...body.selected, {family:'Equipment',typeId:'boots'}], reusedCanonical: [], additional: [{family:'Equipment',typeId:'boots'}], package: {id:body.destinationPackageId}, previewDigest:'sealed' };
      return {packageId:'canonical-kit'};
    }, failed: error => errors.push(error.message), published: () => { published++; }
  } });
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/content-promotion.js'), 'utf8').replace(/^export /gm, ''), context);
  const panel = vm.runInContext('mountContentPromotion(root, options)', context);
  const byId = id => root.all().find(n => n.id === id);
  const click = id => byId(id).listeners.click();
  return { panel, entries, root, byId, click, calls, errors, bump: () => {stamp='2';}, reject: () => {reject=true;}, published: () => published };
}
test('promotion previews exact selections and dependencies and waits for explicit publication confirmation', async () => {
  const h=harness(); h.panel.begin(h.entries[0]);
  assert.equal(h.byId('content-promotion-publish'), undefined);
  await h.click('content-promotion-preview'); assert.equal(h.calls.length,1); assert.equal(h.published(),0);
  assert.deepEqual(h.calls[0].body.selected,[{family:'LootCard',typeId:'gift:one'}]);
  assert.ok(h.root.all().some(n => n.textContent === 'Additional definitions to publish'));
  await h.click('content-promotion-publish');
  assert.equal(h.calls[1].body.confirm,true); assert.equal(h.calls[1].body.preview.previewDigest,'sealed'); assert.equal(h.published(),1);
});
test('promotion blocks changed source stamps and retains rejected preview for explicit retry or cancel', async () => {
  const h=harness(); h.panel.begin(h.entries[0]); await h.click('content-promotion-preview'); h.bump();
  await h.click('content-promotion-publish'); assert.equal(h.calls.length,1); assert.match(h.errors[0],/source changed/); assert.equal(h.published(),0);
  h.click('content-promotion-cancel'); assert.equal(h.root.hidden,true);
  const stale=harness(); stale.panel.begin(stale.entries[0]); await stale.click('content-promotion-preview'); stale.reject();
  await stale.click('content-promotion-publish'); assert.match(stale.errors[0],/Stale destination/); assert.equal(stale.published(),0);
});
test('dependency group selection excludes unrelated packages and supports cancelling before a write', async () => {
  const h=harness(); h.entries.push({...h.entries[1], key:'equipment:other', packageId:'other'}); h.panel.begin(h.entries[0]);
  const checks=h.root.all().filter(n=>n.type==='checkbox'); assert.equal(checks.length,2);
  checks[1].checked=true; checks[1].listeners.change(); await h.click('content-promotion-preview');
  assert.equal(h.calls[0].body.selected.length,2); h.click('content-promotion-cancel'); assert.equal(h.published(),0);
});


test('promotion selects Units explicitly alongside Equipment and Loot', async () => {
  const h=harness(); const unit={key:'units:keeper',family:'units',id:'keeper',name:'Keeper',origin:'library-local',packageId:'kit'};
  h.entries.push(unit); h.panel.begin(unit);
  assert.equal(h.root.all().filter(n=>n.type==='checkbox').length,3);
  await h.click('content-promotion-preview');
  assert.deepEqual(h.calls[0].body.selected,[{family:'Unit',typeId:'keeper'}]);
});
