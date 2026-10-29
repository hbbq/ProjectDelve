const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/player-shell.js'), 'utf8');
const html = fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/index.html'), 'utf8');

function harness(options = {}) {
  const nodes = new Map(), listeners = new Map(), events = [];
  let focused;
  const closeEvents = [], rootStyle = {};
  const node = id => {
    if (!nodes.has(id)) nodes.set(id, {
      id, open: false, hidden: false, dataset: {}, attributes: {}, listeners: {},
      addEventListener(type, fn) { this.listeners[type] = fn; },
      getAttribute(name) { return this.attributes[name] ?? null; },
      setAttribute(name, value) { this.attributes[name] = value; },
      focus() { focused = id; },
      showModal() { this.open = true; this.modalOpened = true; },
      close() { this.open = false; if (options.queuedClose) closeEvents.push(() => this.listeners.close?.()); else this.listeners.close?.(); },
      getBoundingClientRect() { return { height: options.dockHeight ?? 94 }; },
      querySelector(selector) { return selector === ".decision-strip" ? node("command-dock") : null; },
      querySelectorAll(selector) { return selector === 'a' ? links : []; }
    });
    return nodes.get(id);
  };
  const links = ['session-return', 'menu-home-link', 'menu-designer-link'].map(node);
  links.forEach((link, index) => link.href = ['#campaign/test-owned', '#home', '#designer'][index]);
  const tabs = ['unit', 'mission', 'journal'].map(tab => {
    const button = node(`tab-${tab}`); button.dataset.inspectorTab = tab;
    button.attributes['aria-controls'] = `inspector-${tab}`;
    button.attributes['aria-pressed'] = String(tab === 'unit');
    node(`inspector-${tab}`).hidden = tab !== 'unit';
    return button;
  });
  const draft = node('designer-title'); draft.value = 'Unpublished draft';
  const transport = node('designer-transport'); transport.value = 'Unfinished transport input';
  const board = node('board'); board.observation = { knownUnit: 'supplied-unit', unknownCell: 'Unknown' };
  const document = { getElementById: node, querySelectorAll: selector => selector === '[data-inspector-tab]' ? tabs : [],
    querySelector: selector => selector === '.decision-strip' ? node('command-dock') : null,
    documentElement: { style: { setProperty: (key, value) => rootStyle[key] = value } } };
  const context = vm.createContext({ document, ResizeObserver: options.ResizeObserver,
    CustomEvent: class { constructor(type, init) { this.type = type; this.detail = init?.detail; } },
    addEventListener: (type, fn) => listeners.set(type, fn),
    dispatchEvent: event => { events.push(event); listeners.get(event.type)?.(event); },
    fetch: () => { throw new Error('Presentation navigation must not make game or library requests.'); }
  });
  vm.runInContext(source, context);
  return { node, nodes, tabs, links, events, draft, transport, board,
    click: id => node(id).listeners.click(),
    event: type => listeners.get(type)?.(),
    focused: () => focused, rootStyle, flushClose: () => { for (const dispatch of closeEvents.splice(0)) dispatch(); } };
}

test('game menu uses a native modal and closing returns focus to its trigger', () => {
  const h = harness();
  h.node('playtest-tools').open = true;
  h.click('open-game-menu');
  assert.equal(h.node('game-menu').modalOpened, true);
  assert.equal(h.node('game-menu').open, true);
  assert.equal(h.node('playtest-tools').open, false);
  h.click('resume-game');
  assert.equal(h.node('game-menu').open, false);
  assert.equal(h.focused(), 'open-game-menu');
  h.click('open-game-menu');
  // Native Escape dismissal produces the same close event.
  h.node('game-menu').close();
  assert.equal(h.focused(), 'open-game-menu');
  h.node('playtest-main').hidden = true;
  h.node('designer-title').focus(); h.node('game-menu').close();
  assert.equal(h.focused(), 'designer-title', 'leaving the game must not focus its hidden trigger');
  assert.match(html, /<dialog id="game-menu" aria-labelledby="game-menu-title">/);
});

test('menu settings and close affordance transfer focus without replacing live controls', () => {
  const h = harness(), tools = h.node('playtest-tools');
  h.node('scenario-transport').value = 'Unfinished game import';
  const input = h.node('scenario-transport');
  h.click('open-game-menu'); h.click('menu-settings');
  assert.equal(h.node('game-menu').open, false);
  assert.equal(tools.open, true); assert.equal(h.focused(), 'close-game-tools');
  h.click('close-game-tools');
  assert.equal(tools.open, false); assert.equal(h.focused(), 'tools-toggle');
  assert.equal(h.node('scenario-transport'), input);
  assert.equal(input.value, 'Unfinished game import');
});

test('field guide tabs expose exactly one panel and notify choices without replacing observations', () => {
  const h = harness(), unit = h.node('inspector-unit'), mission = h.node('inspector-mission'), journal = h.node('inspector-journal');
  const board = h.board, observation = board.observation;
  for (const target of ['mission', 'journal', 'unit']) {
    h.click(`tab-${target}`);
    for (const name of ['unit', 'mission', 'journal']) {
      assert.equal(h.node(`inspector-${name}`).hidden, name !== target);
      assert.equal(h.node(`tab-${name}`).attributes['aria-pressed'], String(name === target));
    }
    assert.equal(h.events.at(-1).type, 'delve-inspector-change');
    assert.equal(h.events.at(-1).detail.tab, target);
  }
  assert.equal(h.node('inspector-unit'), unit); assert.equal(h.node('inspector-mission'), mission); assert.equal(h.node('inspector-journal'), journal);
  assert.equal(h.node('board'), board); assert.equal(board.observation, observation);
});

test('menu navigation and route changes retain unsaved Designer nodes and form buffers', () => {
  const h = harness(), draft = h.draft, transport = h.transport, board = h.board;
  for (const link of h.links) {
    h.click('open-game-menu'); link.listeners.click();
    assert.equal(h.node('game-menu').open, false);
    h.node('playtest-tools').open = true;
    h.event('hashchange');
    assert.equal(h.node('playtest-tools').open, false);
    assert.equal(h.node('designer-title'), draft); assert.equal(draft.value, 'Unpublished draft');
    assert.equal(h.node('designer-transport'), transport); assert.equal(transport.value, 'Unfinished transport input');
    assert.equal(h.node('board'), board);
  }
  assert.deepEqual(h.links.map(link => link.href), ['#campaign/test-owned', '#home', '#designer']);
  assert.deepEqual(h.events, [], 'navigation must not fabricate game events');
});

test('queued native dialog close keeps focus in the settings opened from the menu', () => {
  const h = harness({ queuedClose: true });
  h.click('open-game-menu'); h.click('menu-settings');
  assert.equal(h.node('playtest-tools').open, true);
  assert.equal(h.focused(), 'close-game-tools');
  h.flushClose();
  assert.equal(h.focused(), 'close-game-tools', 'native close fires after the click handler has opened Settings');
});

test('mobile command dock reserves measured height, including wrapped instructions', () => {
  let observe, update;
  const h = harness({ ResizeObserver: class {
    constructor(callback) { update = callback; }
    observe(target) { observe = target; }
  }, dockHeight: 133.2 });
  assert.equal(observe, h.node('command-dock'));
  update([{ borderBoxSize: [{ blockSize: 101.5 }] }]);
  assert.equal(h.rootStyle['--command-dock-height'], '102px');
  update([{}]);
  assert.equal(h.rootStyle['--command-dock-height'], '134px', 'bounding box fallback still reserves the whole dock');
});
