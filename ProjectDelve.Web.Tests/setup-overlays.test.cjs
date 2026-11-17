const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = name => fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot', name), 'utf8').replace(/^export /gm, '');
const api = vm.runInNewContext(source('designer-state.js') + '\n' + source('setup-overlays.js') +
  '\n({DesignerDraft,DesignerHistory,DesignerRequests,draftDocument,readDraftDocument,setupOverlayCells,setupStatusText,showFinalSetup})', { structuredClone });
const plain = value => JSON.parse(JSON.stringify(value));
const setup = () => ({ version: 1, deployment: { capacity: 1, cells: [{x:0,y:0}], maneuverCore: [{x:1,y:0}], placementOrder: [{x:0,y:0}], departure: {x:2,y:0} },
  requiredLocations: [
    { id: 'fixture-house', producerId: 'fixture-site', sourceId: 'fixture-room', accessClass: 'Ordinary', groups: [{id:'door', coverage:'Any', positions:[{x:2,y:1},{x:8,y:8}]}] },
    { id: 'fixture-island', producerId: 'fixture-site', sourceId: 'fixture-water', accessClass:'DeliberateSpecial', specialReason:'Authored water access', groups:[{id:'landing',coverage:'All',positions:[{x:3,y:2}]}] }
  ] });
function draft() {
  const d = new api.DesignerDraft({ maxBoardSize: 120, unitTypes: [] });
  d.definition.setup = setup(); return d;
}
test('setup survives resize, history, recovery and isolated snapshots without persisting certification', () => {
  const d = draft(), history = new api.DesignerHistory(d), original = plain(d.definition.setup);
  d.playReady = {status:'Valid', valid:true,diagnostics:[]};
  history.transact(() => d.resize(3, 3));
  assert.equal(d.playReady, null);
  assert.deepEqual(plain(d.definition.setup), original, 'out-of-bounds requirements remain authored');
  history.undo(); assert.deepEqual(plain(d.definition.setup), original);
  history.redo(); assert.deepEqual(plain(d.definition.setup), original);
  const doc = api.readDraftDocument(JSON.stringify(api.draftDocument(d.snapshot())));
  assert.deepEqual(plain(doc.definition.setup), original);
  assert.equal(Object.hasOwn(doc, 'playReady'), false);
  assert.equal(Object.hasOwn(doc.definition, 'playReady'), false);
  doc.definition.setup.requiredLocations.pop();
  assert.equal(d.definition.setup.requiredLocations.length, 2);
});
test('only current authoritative checks certify a revision, independently of Engine validity', async () => {
  const d = draft(), pending = [], reports = [];
  const requests = new api.DesignerRequests(d, () => new Promise(resolve => pending.push(resolve)), r => reports.push(r));
  const valid = {valid:true,errors:[],playReady:{status:'Invalid',valid:false,diagnostics:[{path:'setup',message:'No departure'}]}};
  const first = requests.run('validate', {}); d.changed(); pending.shift()(valid); await first;
  assert.equal(d.playReady, null); assert.equal(reports.length, 0);
  const older = requests.run('validate', {}), newer = requests.run('validate', {});
  pending[1](valid); await newer;
  assert.equal(d.playReady.valid, false); assert.equal(reports[0].valid, true);
  pending[0]({valid:true,errors:[],playReady:{status:'Valid',valid:true}}); await older;
  assert.equal(d.playReady.valid, false);
  const imported = requests.run('import', {}); pending.at(-1)({playReady:{status:'Valid',valid:true}}); await imported;
  assert.equal(d.playReady.valid, false, 'import cannot certify declarations');
  d.changed(); assert.equal(d.playReady, null);
});
test('projection distinguishes ordinary and deliberate-special groups and clips rendering without mutating declarations', () => {
  const declared = setup(), before = JSON.stringify(declared);
  const cells = plain(api.setupOverlayCells(declared, 4, 3));
  assert.deepEqual(cells.map(c => c.kind), ['deployment','core','departure','ordinary','special']);
  assert.equal(cells[3].locationId, 'fixture-house'); assert.equal(cells[3].groupId, 'door');
  assert.equal(cells[4].locationId, 'fixture-island');
  assert.equal(JSON.stringify(declared), before);
  assert.match(api.setupStatusText(declared, null), /unchecked/);
  assert.match(api.setupStatusText(null, {status:'Legacy',valid:false}), /no PlayReady1 contract/);
});
test('final setup cannot be projected onto earlier, Before or construction frames', () => {
  const preview = {frames:[{stage:'Terrain'},{stage:'Deployment'},{stage:'Population'}]};
  assert.equal(api.showFinalSetup(preview, 0, false, null), false);
  assert.equal(api.showFinalSetup(preview, 1, false, null), false);
  assert.equal(api.showFinalSetup(preview, 2, true, null), false);
  assert.equal(api.showFinalSetup(preview, 2, false, 'building'), false);
  assert.equal(api.showFinalSetup(preview, 2, false, null), true);
  assert.equal(api.showFinalSetup(null, 0, false, null), true);
});
