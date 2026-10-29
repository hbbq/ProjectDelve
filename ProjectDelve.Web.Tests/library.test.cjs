const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = file => fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot', file), 'utf8');
const plain = value => JSON.parse(JSON.stringify(value));

class Element {
  constructor(tag = 'div') { this.tagName = tag; this.children = []; this.listeners = {}; this.dataset = {}; this.value = ''; this.hidden = false; this.attributes = {}; this.classList = { toggle() {} }; }
  set textContent(value) { this.text = String(value); this.children = []; }
  get textContent() { return (this.text ?? '') + this.children.map(c => c.textContent).join(''); }
  append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); } }
  replaceChildren(...children) { this.text = ''; this.children = []; this.append(...children); }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(name, action) { this.listeners[name] = action; }
  remove() { this.parentElement.children = this.parentElement.children.filter(c => c !== this); }
  all(tag) { return this.children.flatMap(c => [...(c.tagName === tag ? [c] : []), ...c.all(tag)]); }
}
const fixture = {
  scenarios: [{ id: 's-one', name: 'First', revision: 1, coverArt: null }, { id: 's-two', name: 'Second', revision: 3, coverArt: '/art/cover.webp' }],
  campaigns: [{ id: 'c-one', name: 'Campaign', description: 'Public introduction', revision: 2, scenarioIds: ['s-two', 'deleted', 's-one'] }]
};
function harness(hash = '#home') {
  const elements = new Map(), events = {}, calls = [], modes = [], played = [], location = { hash };
  const get = id => { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); };
  get('library-search').parentElement = new Element();
  const editor = { canLeave: () => true, open: async id => { editor.opened = id; } };
  const context = vm.createContext({ location, document: { getElementById: get, querySelector: get, createElement: tag => new Element(tag), body: new Element() },
    history: { replaceState: (_, unused, hash) => { location.hash = hash; } },
    mountCampaignEditor: () => editor, addEventListener: (name, action) => { events[name] = action; },
    delveSetDesignerMode: designer => modes.push(designer), delvePlayDesign: async transport => { played.push(transport); return true; },
    fetch: async (url, options) => { calls.push({ url, ...options }); return { ok: true, status: 200, json: async () => url.endsWith('/transport') ? { transport: 'DELVE1:test-owned' } : structuredClone(fixture) }; }
  });
  vm.runInContext(source('library-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(source('library.js').replace(/^import .*\r?\n/gm, '').replace(/void applyRoute\(\);\s*$/, ''), context);
  return { run: code => vm.runInContext(code, context), get, context, location, events, calls, modes, played, editor };
}

test('library routes retain direct Designer/playtest links and decode scenario identities', () => {
  const h = harness();
  for (const hash of ['', '#unknown', '#campaign/%ZZ']) assert.equal(h.run(`libraryRoute(${JSON.stringify(hash)}).mode`), 'home');
  assert.deepEqual(plain(h.run("libraryRoute('#playtest')")), { mode: 'play' });
  assert.deepEqual(plain(h.run("libraryRoute('#designer')")), { mode: 'designer' });
  assert.deepEqual(plain(h.run("libraryRoute('#scenario/s-one')")), { mode: 'scenario', id: 's-one' });
});

test('home navigation renders only public campaign metadata and keeps game and Designer hidden', async () => {
  const h = harness(); await h.run('applyRoute()');
  assert.equal(h.get('library-main').hidden, false);
  assert.equal(h.get('designer-main').hidden, true); assert.equal(h.get('playtest-main').hidden, true);
  assert.equal(h.get('playtest-tools').hidden, true);
  assert.match(h.get('library-cards').textContent, /Campaign.*Public introduction/);
  assert.deepEqual(h.calls.map(c => c.url), ['/api/library']);
  assert.ok(h.get('library-cards').all('a').some(a => a.href === '#campaign/c-one'));
});

test('campaign selection preserves reference order, offers any available scenario, and marks missing entries', async () => {
  const h = harness('#campaign/c-one'); await h.run('applyRoute()');
  const cards = h.get('library-cards').children;
  assert.deepEqual(cards.map(c => c.dataset.scenarioId), ['s-two', 'deleted', 's-one']);
  assert.equal(cards[0].all('button').length, 1); assert.equal(cards[2].all('button').length, 1);
  assert.equal(cards[1].all('button').length, 0); assert.match(cards[1].textContent, /unavailable/);
  await cards[2].all('button')[0].listeners.click();
  assert.deepEqual(h.played, ['DELVE1:test-owned']); assert.equal(h.location.hash, '#play');
  assert.equal(h.calls.at(-1).url, '/api/library/scenarios/s-one/transport');
  assert.ok(h.calls.every(c => c.method === undefined));
  await h.run('applyRoute()');
  assert.equal(h.get('playtest-main').hidden, false); assert.equal(h.get('library-main').hidden, true);
});

test('Designer ready reapplies the current route without replacing the workspace', async () => {
  const h = harness('#designer'); await h.run('applyRoute()');
  assert.equal(h.get('designer-main').hidden, false); assert.equal(h.calls.length, 0);
  h.events['delve-designer-ready'](); assert.equal(h.modes.at(-1), true);
  h.location.hash = '#home'; await h.run('applyRoute()');
  h.location.hash = '#designer'; await h.run('applyRoute()');
  assert.equal(h.get('designer-main').hidden, false);
  assert.deepEqual(h.calls.map(c => c.url), ['/api/library']);
});

test('a delayed game start does not override subsequent navigation', async () => {
  const h = harness('#scenarios'); await h.run('applyRoute()');
  let finish;
  h.context.delvePlayDesign = () => new Promise(resolve => { finish = resolve; });
  const starting = h.get('library-cards').children[0].all('button')[0].listeners.click();
  await new Promise(resolve => setImmediate(resolve));
  h.location.hash = '#home'; await h.run('applyRoute()');
  finish(true); await starting;
  assert.equal(h.location.hash, '#home');
  assert.equal(h.get('library-main').hidden, false);
});

test('browser navigation cannot discard an unfinished campaign when leave is declined', async () => {
  const h = harness('#campaigns-edit/c-one'); await h.run('applyRoute()');
  assert.equal(h.editor.opened, 'c-one'); h.editor.canLeave = () => false;
  h.location.hash = '#designer'; await h.run('applyRoute()');
  assert.equal(h.location.hash, '#campaigns-edit/c-one');
  assert.equal(h.get('campaign-editor').hidden, false); assert.equal(h.get('designer-main').hidden, true);
});

test('scenario search is name-only and broken cover art falls back to the decoration', async () => {
  const h = harness('#scenarios'); await h.run('applyRoute()');
  const cover = h.get('library-cards').children[1].all('img')[0]; cover.listeners.error();
  assert.equal(h.get('library-cards').children[1].all('img').length, 0);
  h.get('library-search').value = ' SECOND '; h.get('library-search').listeners.input();
  assert.equal(h.get('library-cards').children.length, 1); assert.match(h.get('library-cards').textContent, /Second/);
});

test('hidden bundled content can be restored with revision and conflict token', async () => {
  const h = harness('#scenarios');
  h.context.hidden = { id: 'hidden-one', name: 'Hidden adventure', revision: 8, changeToken: 'hidden-token' };
  await h.run('applyRoute()');
  h.run('library.hiddenScenarios = [hidden]; render()');
  assert.equal(h.get('library-hidden').hidden,false);
  assert.match(h.get('library-hidden').textContent,/Hidden adventure/);
  h.get('library-hidden').all('button')[0].listeners.click();
  await h.get('library-hidden').all('button').find(b=>b.dataset.restoreId==='hidden-one').listeners.click();
  const restore=h.calls.find(c=>c.method==='POST');
  assert.equal(restore.url,'/api/library/scenarios/hidden-one/restore');
  assert.deepEqual(JSON.parse(restore.body),{revision:8,changeToken:'hidden-token'});
});

test('bundled scenario deletion is labelled local hiding and sends the current token', async () => {
  const h=harness('#scenario/s-one'); await h.run('applyRoute()');
  h.run('library.scenarios[0].source="bundled"; library.scenarios[0].changeToken="current-token"; render()');
  assert.match(h.get('library-cards').textContent,/Bundled/);
  h.get('library-cards').all('button').find(b=>b.textContent==='Hide locally').listeners.click();
  await h.get('library-cards').all('button').find(b=>b.textContent==='Confirm deletion').listeners.click();
  assert.equal(h.calls.find(c=>c.method==='DELETE').url,'/api/library/scenarios/s-one?revision=1&changeToken=current-token');
});

test('child routes keep their parent navigation selected and expose public campaign links', async () => {
  for (const [route, active] of [['#campaign/c-one', 'home'], ['#campaigns-edit/c-one', 'home'], ['#scenario/s-one', 'scenarios']]) {
    const h = harness(route); await h.run('applyRoute()');
    for (const name of ['home', 'scenarios', 'designer', 'playtest'])
      assert.equal(h.get('mode-' + name).attributes['aria-pressed'], String(name === active), route + ': ' + name);
    if (route.startsWith('#scenario/')) {
      assert.ok(h.get('library-cards').all('a').some(a => a.href === '#campaign/c-one'));
      assert.deepEqual(h.calls.map(c => c.url), ['/api/library'], 'Details use public metadata without loading scenario definitions');
      assert.ok(h.get('library-cards').all('details').some(d => /Manage scenario/.test(d.textContent)));
    }
  }
});

test('empty search offers recovery without leaving the collection or fetching definitions', async () => {
  const h = harness('#scenarios'); await h.run('applyRoute()');
  h.get('library-search').value = 'missing-name'; h.get('library-search').listeners.input();
  assert.equal(h.get('library-cards').children.length, 0);
  assert.match(h.get('library-status').textContent, /No scenarios match/);
  h.get('library-status').all('button').find(b => b.textContent === 'Clear search').listeners.click();
  assert.equal(h.get('library-search').value, '');
  assert.equal(h.get('library-cards').children.length, 2);
  assert.equal(h.location.hash, '#scenarios');
  assert.deepEqual(h.calls.map(c => c.url), ['/api/library']);
});

for (const succeeds of [true, false]) test(`library start sets return context only after a successful game replacement (${succeeds})`, async () => {
  const h = harness('#campaign/c-one'), contexts = [];
  h.context.delveSetPlayContext = value => contexts.push(plain(value));
  let complete;
  h.context.delvePlayDesign = () => new Promise(resolve => { complete = resolve; });
  await h.run('applyRoute()');
  const play = h.get('library-cards').children[0].all('button')[0];
  const starting = play.listeners.click();
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(play.disabled, true); assert.match(play.textContent, /Preparing/);
  assert.deepEqual(contexts, []);
  complete(succeeds); await starting;
  assert.equal(play.disabled, false); assert.equal(play.textContent, 'Play scenario');
  if (succeeds) {
    assert.deepEqual(contexts, [{ title: 'Second', returnHash: '#campaign/c-one', returnLabel: 'Back to campaign' }]);
    assert.equal(h.location.hash, '#play');
  } else {
    assert.deepEqual(contexts, []); assert.equal(h.location.hash, '#campaign/c-one');
    assert.match(h.get('library-status').textContent, /retry/i);
  }
});

test('main menu features public campaign artwork and keeps direct journey routes available', async () => {
  const h = harness(); await h.run('applyRoute()');
  h.run('library.campaigns[0].coverArt = "/art/test-cover.webp"; render()');
  const hero = h.get('.library-hero');
  assert.match(hero.textContent, /Every table.*An adventure/s);
  assert.match(hero.textContent, /Campaign/);
  assert.ok(hero.all('a').some(a => a.dataset.featuredCampaign === 'c-one' && a.href === '#campaign/c-one'));
  for (const href of ['#scenarios', '#play', '#designer']) assert.ok(hero.all('a').some(a => a.href === href), href);
  assert.equal(hero.all('img')[0].src, '/art/test-cover.webp');
  assert.equal(hero.all('img')[0].loading, 'eager');
  assert.deepEqual(h.calls.map(c => c.url), ['/api/library']);
});

test('empty campaign library retains an actionable home menu without inventing a featured adventure', async () => {
  const h = harness(); await h.run('applyRoute()');
  h.run('library.campaigns = []; render()');
  const hero = h.get('.library-hero');
  assert.equal(hero.hidden, false);
  assert.ok(hero.all('a').some(a => a.href === '#scenarios'));
  assert.ok(hero.all('a').some(a => a.href === '#designer'));
  assert.equal(hero.all('a').filter(a => a.dataset.featuredCampaign).length, 0);
  assert.match(h.get('library-status').textContent, /first campaign/);
});

test('campaign banner shows authored public story while numbered rows retain source ordering', async () => {
  const h = harness('#campaign/c-one'); await h.run('applyRoute()');
  assert.match(h.get('.library-hero').textContent, /Campaign.*Public introduction/s);
  const rows = h.get('library-cards').children;
  assert.deepEqual(rows.map(row => row.dataset.scenarioId), ['s-two', 'deleted', 's-one']);
  assert.ok(rows[0].all('span').some(el => el.className === 'library-expedition-number' && el.textContent === '01'));
  assert.equal(h.context.document.title, 'Campaign · Project Delve');
  h.location.hash = '#scenario/s-two'; await h.run('applyRoute()');
  assert.equal(h.get('.library-hero').hidden, true);
  assert.equal(h.context.document.title, 'Second · Project Delve');
  assert.match(h.get('library-cards').textContent, /BEFORE YOU BEGIN/);
  assert.deepEqual(h.calls.map(c => c.url), ['/api/library', '/api/library']);
});
