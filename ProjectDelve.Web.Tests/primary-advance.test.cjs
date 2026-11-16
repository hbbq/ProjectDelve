const { test } = require('node:test');
const assert = require('node:assert/strict');
const { pathToFileURL } = require('node:url');
const path = require('node:path');
const moduleAt = () => import(pathToFileURL(path.resolve(__dirname, '../ProjectDelve.Web/wwwroot/primary-advance.js')).href);
const stay = { key: 'stay', boardVisual: { kind: 'stay' } };
const endTurn = { key: 'end', isEndTurn: true, label: 'End Turn' };
async function harness(candidates = [stay]) {
  const { advanceChoices, bindAdvanceControls } = await moduleAt();
  const node = () => ({ hidden: false, disabled: false, listeners: {}, attributes: {},
    addEventListener(k, fn) { this.listeners[k] = fn; }, setAttribute(k, v) { this.attributes[k] = v; } });
  const document = node(), stayButton = node(), endTurnButton = node();
  let decision = { candidates }, hide = true, busy = false, selecting = false, active = true, overlay = false;
  const calls = [];
  document.querySelector = () => overlay;
  const render = bindAdvanceControls({ document, stayButton, endTurnButton,
    choices: () => advanceChoices(decision, c => !(hide && c.relevant === false)), busy: () => busy,
    selecting: () => selecting, active: () => active, submit: key => { calls.push(key); busy = true; }, tooltip: () => {} });
  const event = (key, extra = {}) => ({ key, target: { closest: () => false }, preventDefault() { this.defaultPrevented = true; }, ...extra });
  const key = (key, extra) => { const e = event(key, extra); document.listeners.keydown(e); return e; };
  const release = (key, extra) => { const e = event(key, extra); document.listeners.keyup(e); return e; };
  return { stayButton, endTurnButton, document, calls, render, key, release, set: values => {
    if ('decision' in values) decision = values.decision;
    if ('hide' in values) hide = values.hide;
    if ('busy' in values) busy = values.busy;
    if ('selecting' in values) selecting = values.selecting;
    if ('active' in values) active = values.active;
    if ('overlay' in values) overlay = values.overlay;
    render();
  } };
}
test('move/action phases, both and neither render without placeholders; existing choice data is retained', async () => {
  const h = await harness(); h.render();
  assert.equal(h.stayButton.hidden, false); assert.equal(h.endTurnButton.hidden, true);
  h.set({ decision: { candidates: [endTurn] } });
  assert.equal(h.stayButton.hidden, true); assert.equal(h.endTurnButton.hidden, false);
  h.set({ decision: { candidates: [stay, endTurn] } });
  assert.equal(h.stayButton.hidden, false); assert.equal(h.endTurnButton.hidden, false);
  h.set({ decision: null }); assert.equal(h.stayButton.hidden, true); assert.equal(h.endTurnButton.hidden, true);
});

test('focused board Space advances once on release; pointer gestures and changed decisions cancel it', async () => {
  const target = { closest: () => false, matches: () => true };
  const h = await harness([stay, endTurn]);
  assert.equal(h.key(' ', { target }).defaultPrevented, true);
  assert.deepEqual(h.calls, []);
  h.key(' ', { target, repeat: true });
  assert.equal(h.release(' ', { target }).defaultPrevented, true);
  assert.deepEqual(h.calls, ['end']);
  h.set({ busy: false }); h.release(' ', { target }); assert.equal(h.calls.length, 1);
  h.key(' ', { target }); h.document.listeners.pointerdown({ pointerId: 1 });
  h.document.listeners.pointerup({ pointerId: 1 }); h.release(' ', { target });
  assert.equal(h.calls.length, 1);
  h.key(' ', { target }); h.set({ decision: { candidates: [stay] } }); h.release(' ', { target });
  assert.equal(h.calls.length, 1);
});
test('Hide Irrelevant applies equally to buttons, mouse and shortcuts', async () => {
  const h = await harness([{ ...stay, relevant: false }, { ...endTurn, relevant: false }]); h.render();
  assert.equal(h.stayButton.hidden, true); assert.equal(h.endTurnButton.hidden, true);
  h.stayButton.listeners.click(); h.endTurnButton.listeners.click();
  assert.equal(h.key(' ').defaultPrevented, undefined); h.key('s'); assert.deepEqual(h.calls, []);
  h.set({ hide: false }); assert.equal(h.stayButton.hidden, false); h.key('s'); assert.deepEqual(h.calls, ['stay']);
});
test('Space advances Stay or End Turn; both prioritize End Turn and S stays', async () => {
  for (const [choices, key, expected] of [[[stay], ' ', 'stay'], [[endTurn], ' ', 'end'], [[stay, endTurn], ' ', 'end'], [[stay, endTurn], 's', 'stay'], [[stay], 'S', 'stay']]) {
    const h = await harness(choices); assert.equal(h.key(key).defaultPrevented, true); assert.deepEqual(h.calls, [expected]);
  }
  const h = await harness([endTurn]); assert.equal(h.key('s').defaultPrevented, undefined);
});
test('noneChoice Stay supports mandatory movement follow-up without inventing a choice', async () => {
  const h = await harness([]); h.set({ decision: { candidates: [], noneChoice: { ...stay, key: null } } });
  h.key(' '); assert.deepEqual(h.calls, [null]);
});
test('repeat, native controls, editors, overlays, selection and unrelated keys are left alone', async () => {
  for (const extra of [{ repeat: true }, { isComposing: true }, { ctrlKey: true }, { altKey: true }, { metaKey: true }, { shiftKey: true }, { defaultPrevented: true },
    { target: { isContentEditable: true } }, { target: { closest: () => true } }]) {
    const h = await harness(); h.key(' ', extra); assert.deepEqual(h.calls, []);
  }
  for (const state of [{ overlay: true }, { selecting: true }, { active: false }, { busy: true }]) {
    const h = await harness(); h.set(state); assert.equal(h.key(' ').defaultPrevented, undefined); assert.deepEqual(h.calls, []);
  }
  const h = await harness(); h.key('Escape'); h.key('x'); assert.deepEqual(h.calls, []);
});
test('one submission across rapid keyboard/mouse input until busy clears', async () => {
  const h = await harness([stay, endTurn]); h.render(); h.key('s'); h.key(' '); h.stayButton.listeners.click(); h.endTurnButton.listeners.click();
  assert.deepEqual(h.calls, ['stay']); h.set({ busy: false }); h.endTurnButton.listeners.click(); assert.deepEqual(h.calls, ['stay', 'end']);
});
