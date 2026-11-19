const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const context = vm.createContext({ structuredClone });
for (const file of ['credits-ui.js', 'designer-state.js'])
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot', file), 'utf8').replace(/^export /gm, ''), context);
const run = code => vm.runInContext(code, context);
const plain = value => JSON.parse(JSON.stringify(value));

test('Credits display follows projected observer and active Side without computing pickup', () => {
  run(`globalThis.state = {sideCredits:{blue:7,red:3}, activeToken:{sideId:'red'}, creditsPiles:[{position:{x:2,y:1},amount:4}]}`);
  assert.equal(run(`creditsBalanceLabel(state, 'blue')`), 'Side blue · Credits 7');
  assert.equal(run(`creditsBalanceLabel(state, null)`), 'Side red · Credits 3');
  assert.equal(run(`creditsBalanceLabel({...state,pending:{token:{sideId:'blue'}}}, null)`), 'Side blue · Credits 7');
  assert.deepEqual(plain(run('creditsPiles(state)')), [{ position: { x: 2, y: 1 }, amount: 4 }]);
  assert.equal(run('creditsPiles(state) === state.creditsPiles'), true);
  run(`state.creditsPiles = []; state.sideCredits = {red:7}`);
  assert.deepEqual(plain(run('creditsPiles(state)')), []);
  assert.equal(run(`creditsBalanceLabel(state, 'red')`), 'Side red · Credits 7');
  assert.equal(run(`creditsBalanceLabel({}, 'blue')`), 'Side blue · Credits 0');
  assert.equal(run(`creditsBalanceLabel({}, '__proto__')`), 'Side __proto__ · Credits 0');
  assert.equal(run(`creditsBalanceLabel({}, 'constructor')`), 'Side constructor · Credits 0');
  assert.equal(run(`creditsBalanceLabel({}, null, [{sideId:'blue'}, {sideId:'red'}])`), 'Side blue · Credits 0 | Side red · Credits 0');
});

test('arbitrary Side IDs remain own numeric balances when edited and recovered', () => {
  run(`globalThis.special = new DesignerDraft({maxBoardSize:50,unitTypes:[]});
    special.setSideCredits('__proto__',6); special.setSideCredits('constructor',3);`);
  assert.equal(run(`Object.hasOwn(special.definition.sideCredits,'__proto__')`), true);
  assert.equal(run(`creditsBalanceLabel(special.definition,'__proto__')`), 'Side __proto__ · Credits 6');
  assert.equal(run(`creditsBalanceLabel(special.definition,'constructor')`), 'Side constructor · Credits 3');
  assert.deepEqual(plain(run('readDraftDocument(JSON.stringify(draftDocument(special.snapshot()))).definition.sideCredits')), JSON.parse('{"__proto__":6,"constructor":3}'));
});

test('Credits authoring preserves piles and balances through undo, recovery and independent playtest snapshots', () => {
  run(`globalThis.draft = new DesignerDraft({maxBoardSize:50,unitTypes:[]});
    globalThis.history = new DesignerHistory(draft); history.begin();
    draft.setCreditsPile({x:4,y:3},6); draft.setSideCredits('blue',9); history.finish();`);
  const authored = plain(run('draft.snapshot()'));
  assert.deepEqual(authored.creditsPiles, [{ position: { x: 4, y: 3 }, amount: 6 }]);
  assert.deepEqual(authored.sideCredits, { blue: 9 });
  assert.throws(() => run('draft.resize(4,4)'), /Shrinking/);
  run('history.undo()'); assert.equal(run('draft.definition.creditsPiles'), undefined);
  run('history.redo()'); assert.deepEqual(plain(run('draft.snapshot()')), authored);
  assert.deepEqual(plain(run('readDraftDocument(JSON.stringify(draftDocument(draft.snapshot()))).definition')), authored);
  run(`globalThis.playtest = draft.snapshot(); playtest.creditsPiles[0].amount = 100; playtest.sideCredits.blue = 100;`);
  assert.deepEqual(plain(run('draft.snapshot()')), authored);
  run('draft.setCreditsPile({x:4,y:3},2)'); assert.equal(run('draft.definition.creditsPiles.length'), 1);
  run(`draft.definition.creditsPiles.push({position:{x:4,y:3},amount:3}); draft.setCreditsPile({x:4,y:3},4)`);
  assert.equal(run('draft.definition.creditsPiles.length'), 1);
  assert.equal(run('draft.definition.creditsPiles[0].amount'), 4);
  run(`draft.definition.creditsPiles.push({position:{x:4,y:3},amount:3}); draft.setCreditsPile({x:4,y:3},0)`);
  assert.equal(run('draft.definition.creditsPiles'), undefined);
  run(`delete draft.definition.sideCredits; draft.setSideCredits('blue',0); draft.setCreditsPile({x:0,y:0},0)`);
  assert.equal(run('draft.definition.sideCredits'), undefined);
  assert.equal(run('draft.definition.creditsPiles'), undefined);
});

test('Credits draft recovery rejects damaged display shapes and retains numeric drafts for Engine validation', () => {
  assert.throws(() => run(`readDraftDocument(JSON.stringify(draftDocument({...draft.snapshot(),creditsPiles:{}})))`), /damaged/);
  assert.throws(() => run(`readDraftDocument(JSON.stringify(draftDocument({...draft.snapshot(),sideCredits:[]})))`), /damaged/);
  assert.throws(() => run(`readDraftDocument(JSON.stringify(draftDocument({...draft.snapshot(),creditsPiles:[{position:null,amount:3}]})))`), /damaged/);
  assert.equal(run(`readDraftDocument(JSON.stringify(draftDocument({...draft.snapshot(),sideCredits:{blue:-1}}))).definition.sideCredits.blue`), -1);
});
