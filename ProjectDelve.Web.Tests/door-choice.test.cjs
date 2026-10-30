const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");

test('human Loot cards pause in sequence and Skip dismisses the current reveal', async () => {
  const h = harness(response());
  let completed = false;
  const playing = h.run(`(async () => {
    skipEffects = false;
    await revealLoot({lootCard:{kind:'LuckyBoots',name:'Lucky Boots',category:'Equipment',text:'MOV +1.'},humanCollector:true});
    await revealLoot({lootCard:{kind:'PocketBreakfast',name:'Pocket Breakfast',category:'Immediate',text:'Restore 2 HP.'},humanCollector:true});
  })()`);
  playing.then(()=>completed=true);
  await Promise.resolve();
  assert.equal(completed,false); assert.match(h.context.document.body.textContent,/Lucky Boots/);
  h.context.document.body.querySelectorAll('.loot-continue')[0].onclick();
  await Promise.resolve(); await Promise.resolve();
  assert.match(h.context.document.body.textContent,/Pocket Breakfast/);
  assert.doesNotMatch(h.context.document.body.textContent,/Lucky Boots/);
  h.elements.get('skip').listeners.click(); await playing;
  assert.equal(completed,true); assert.doesNotMatch(h.context.document.body.textContent,/Pocket Breakfast/);
});

test('automated Loot reveals dismiss on their timer and replacement can cancel a human reveal', async () => {
  const h = harness(response()); let callback;
  h.context.setTimeout = fn => { callback=fn; return 1; }; h.context.clearTimeout=()=>{};
  const playing = h.run(`revealLoot({lootCard:{kind:'GoatBell',name:'Bell',category:'Equipment',text:'Stand up a friend.'},humanCollector:false})`);
  assert.equal(typeof callback,'function'); callback(); await playing;
  assert.doesNotMatch(h.context.document.body.textContent,/Bell/);
  const human = h.run(`revealLoot({lootCard:{kind:'Bedsheet',name:'Sheet',category:'Equipment',text:'Phase.'},humanCollector:true})`);
  h.run('playbackGeneration++; cancelPause?.()'); await human;
  assert.doesNotMatch(h.context.document.body.textContent,/Sheet/);
});

for (const view of ["classic", "enhanced"]) test(`Reach Area mission marker survives Unknown Cells and Waypoints off (${view})`, () => {
  const initial = response();
  initial.presentation.viewingSideId = "blue";
  initial.result.state.exploredCells = { blue: [{ x: 0, y: 0 }] };
  initial.presentation.objectives = [{ sideId: "blue", name: "Reach Area", meaning: "Reach the supplied area", area: { x: 4, y: 0, w: 2, h: 1 } }];
  const h = harness(initial); h.context.view = view;
  h.run('boardView.value = view; updateBoardView(); ui.board.classList.add("hide-waypoints"); renderState(snapshot.result.state)');
  const board = h.elements.get("board"), marker = board.querySelectorAll(".mission-area")[0];
  assert.ok(marker); assert.equal(marker.style.left, `${4 / 6 * 100}%`);
  assert.equal(marker.style.width, `${2 / 6 * 100}%`);
  assert.ok(!marker.classList.contains("waypoint-area"));
  assert.ok(h.run('cells.get("4,0").classList.contains("Unknown")'));
  assert.equal(h.run('snapshot.result.state.exploredCells.blue.length'), 1);
  h.context.earlierObjectives = [{ sideId:"blue", area:{ x:2, y:0, w:1, h:1 }, meaning:"Earlier snapshot" }];
  h.run('renderState(snapshot.result.state, true, snapshot.presentation.cards, snapshot.presentation.figures, null, [], [], earlierObjectives)');
  assert.equal(board.querySelectorAll(".mission-area").length, 1);
  assert.equal(board.querySelectorAll(".mission-area")[0].style.left, `${2 / 6 * 100}%`);
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`Troll survives lethal playback using generic HP, posture and card content (${mode})`, async () => {
    const printed = { content: { id: "undying", name: "Undying", category: "Capability",
      description: "If this Upright Unit would be defeated, instead set its current HP to 1 and lay it down.",
      maxUses: null, useLimitText: null }, uses: null };
    const initial = response([], [printed]), final = response([], [printed]);
    for (const snapshot of [initial, final]) {
      snapshot.result.state.types[0].displayName = "Troll";
      snapshot.result.state.types[0].hp = 1;
      snapshot.result.state.units[0].currentHp = 1;
      snapshot.presentation.cards.actor.displayName = "Troll";
    }
    final.result.state.physical.figures[0].posture = "Lying";
    final.result.events = [{ kind: "AttackResolved", damage: 99 }, { kind: "PostureChanged", posture: "Lying" }];
    final.presentation.events = [
      { role: "AttackTarget", text: "Supplied attack result", unitId: "b", targetId: "actor", hits: 99, blocks: 0, damage: 99 },
      { role: "Notice", text: "actor: Lying", unitId: "actor" }
    ];
    final.result.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, stateAfter: final.result.state }));
    final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
    const shown = [], h = harness(initial, async () => ({ ok: true, json: async () => final }));
    assert.match(h.elements.get("unit-card").textContent, /Troll/);
    assert.match(h.elements.get("unit-card").textContent, /Undying/);
    assert.match(h.elements.get("unit-card").textContent, /Capability/);
    assert.doesNotMatch(h.elements.get("unit-card").textContent, /uses|\/game/);
    h.context.shown = shown; h.context.mode = mode;
    h.run(`
      ui.animate.checked = mode !== "disabled";
      pause = async () => {};
      const originalRender = renderState;
      renderState = (...args) => {
        originalRender(...args);
        shown.push({ hp: ui["unit-card"].textContent, lying: figures.get("actor").classList.contains("lying") });
      };
      const originalPresent = present;
      present = async event => {
        await originalPresent(event);
        if (mode === "skip") ui.skip.listeners.click();
      };
    `);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.equal(shown.length, 3);
    assert.ok(shown.every(value => /HP 1 \/ 1/.test(value.hp) && value.lying));
    assert.equal(h.figure("actor").classList.contains("lying"), true);
  });
}

// A small DOM harness exercising the actual renderer, event listeners and request body.
class Element {
  constructor() {
    this.children = []; this.dataset = {}; this.attributes = {}; this.listeners = {};
    this.style = { setProperty: (key, value) => { this.style[key] = value; } }; this.checked = true; this.className = "";
    this.classList = {
      contains: name => this.className.split(" ").includes(name),
      add: name => { if (!this.classList.contains(name)) this.className += ` ${name}`; },
      remove: name => { this.className = this.className.split(" ").filter(value => value !== name).join(" "); },
      toggle: (name, force) => force === undefined
        ? (this.classList.contains(name) ? this.classList.remove(name) : this.classList.add(name))
        : (force ? this.classList.add(name) : this.classList.remove(name))
    };
  }
  append(...children) { for (const child of children) { child.remove(); child.parent = this; this.children.push(child); } }
  replaceChildren(...children) {
    for (const child of this.children) child.parent = null;
    this.children = []; for (const child of children) this.append(child);
  }
  remove() {
    if (this.parent) this.parent.children = this.parent.children.filter(child => child !== this);
    this.parent = null;
  }
  set textContent(value) { this.replaceChildren(); this.content = value; }
  get textContent() { return [this.content ?? "", ...this.children.map(child => child.textContent)].join(" "); }
  getAttribute(name) { return this.attributes[name] ?? null; }
  setAttribute(name, value) { this.attributes[name] = value; }
  removeAttribute(name) { delete this.attributes[name]; }
  getBoundingClientRect() { this.layoutReads = (this.layoutReads ?? 0) + 1; return {}; }
  addEventListener(name, callback) {
    const previous = this.listeners[name];
    this.listeners[name] = (...args) => { previous?.(...args); return callback(...args); };
  }
  focus() { this.focused = true; this.listeners.focus?.(); }
  showModal() { this.open = true; }
  close() { this.open = false; this.listeners.close?.(); }
  querySelector(selector) { return this.querySelectorAll(selector)[0] ?? null; }
  querySelectorAll(selector) {
    return this.children.flatMap(child => [
      ...(selector.startsWith(".") ? child.classList.contains(selector.slice(1)) : child.tagName === selector) ? [child] : [],
      ...child.querySelectorAll(selector)
    ]);
  }
}

function state(hp = 4, actorX = 0) {
  return {
    round: 1, currentUnitId: "actor", activeToken: { typeId: "opaque-type", sideId: "amber" },
    // Distant same-side targets and exhausted uses deliberately contradict apparent rules.
    actionDone: true, moveDone: true, cleavePending: false,
    physical: { board: { width: 6, height: 1, edges: [] }, figures: [
      { id: "actor", position: { x: actorX, y: 0 }, posture: "Upright" },
      { id: "a", position: { x: 4, y: 0 }, posture: "Upright" },
      { id: "b", position: { x: 5, y: 0 }, posture: "Upright" }
    ] },
    units: ["actor", "a", "b"].map(id => ({ id, typeId: "opaque-type", sideId: "same", currentHp: id === "actor" ? hp : 4,
      holyWaveUses: { maxUses: 2, remainingUses: 0 }, healUses: { maxUses: 2, remainingUses: 0 }, cleaveUses: { maxUses: 2, remainingUses: 0 } })),
    types: [{ id: "opaque-type", displayName: "A printed name", mov: 3, rng: 1, atk: 3, def: 2, hp: 4 }],
    effectiveAtk: { actor: 17 }, effectiveDef: { actor: 9 }
  };
}
const entry = (id, name, category = "Action", description = "Wording supplied by content") => ({
  content: { id, name, category, description, maxUses: 2, useLimitText: "2/game" }, uses: { remainingUses: 0, maxUses: 2 }
});
const cards = (entries = []) => Object.fromEntries(["actor", "a", "b"].map(id => [id, {
  displayName: id === "actor" ? "A printed name" : "Inspected name", entries: id === "actor" ? entries : []
}]));
const choice = (key, kind, refs = {}, extra = {}) => ({ key, label: `Supplied label ${key}`, entryId: null,
  relevant: true, interaction: { kind, ...refs }, affectedUnitIds: [], ...extra });
function response(candidates = [], entries = []) {
  return { revision: 8, autoChooseSingleRelevantChoice: false,
    result: { state: state(), nextInput: { kind: "raw engine kind must not be read", candidates: [] }, events: [], resolutionSteps: [] },
    presentation: { cards: cards(entries), decision: { unitId: "actor", prompt: "Supplied prompt", candidates, noneChoice: null }, events: [], resolutionSteps: [] }
  };
}
function harness(initial, fetchImpl, extras = {}) {
  const elements = new Map(), submitted = [];
  const document = {
    body: new Element(),
    querySelector(selector) {
      if (!extras.withCamera) return null;
      return selector === "#camera-viewport" ? this.getElementById("camera-viewport")
        : selector === ".camera-controls" ? this.getElementById("camera-controls") : null;
    },
    querySelectorAll() { return []; },
    addEventListener() {},
    getElementById(id) {
      const find = node => node.id === id ? node : node.children.map(find).find(Boolean);
      for (const root of elements.values()) { const found = find(root); if (found) return found; }
      if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id);
    },
    createElement(tag) { const node = new Element(); node.tagName = tag; return node; }
  };
  document.getElementById("coordinates").checked = false;
  const context = vm.createContext({ document, initial, submitted, setTimeout, clearTimeout,
    fetch: fetchImpl ?? (async () => ({ ok: true, json: async () => initial })), ...extras });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/side-colors.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-visuals.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/tooltips.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/loot-ui.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/playtest-viewport.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/unit-facing.js"), "utf8").replace(/^export /gm, ""), context);
  if (extras.withCamera) for (const file of ["camera-geometry.js", "camera-observation.js", "board-camera.js"])
    vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot", file), "utf8").replace(/^import .*\r?\n/gm, "").replace(/^export /gm, ""), context);
  vm.runInContext(script.replace(/^import .*\r?\n/gm, "").replace(/await refresh\(\);\s*$/, ""), context);
  vm.runInContext("snapshot = initial; renderSnapshot();", context);
  return { elements, submitted, run: code => vm.runInContext(code, context), context,
    figure: id => vm.runInContext(`figures.get(${JSON.stringify(id)})`, context) };
}
const click = node => node.listeners.click({ stopPropagation() {} });
const buttons = node => node.querySelectorAll("button");

function attachThree(h) {
  const rendered = [], frames = [], camera = { setFrame() {}, stable() {}, occurrence() {}, movementStep() {}, finishMovement() {}, settle() {}, cancel() {}, screen:p=>p };
  h.context.testThree = { host: new Element(), camera, setActive() {}, renderFrame: frame=>frames.push(frame),
    showInteraction: view=>rendered.push(view), present: async()=>{}, afterOccurrence() {}, settle() {}, cancel() {}, dispose() {} };
  h.run(`threeViewport = testThree; domViewport.setActive = () => {}; domViewport.host = document.getElementById('board');
    viewportRouter = createViewportRouter({ dom: domViewport, overlay: ui['board-overlays'], loadThree: async () => threeViewport,
      onChange: (selection, active) => { boardView.value = selection; if (active === threeViewport && displayedFrame) threeViewport.renderFrame(displayedFrame); syncThreeInteraction(); } });`);
  return { frames, rendered };
}

test('Three routing receives displayed StateAfter, never future legal decisions, and submits one opaque key', async () => {
  const initial = response([choice('first','Position',{position:{x:1,y:0}}),choice('second','Position',{position:{x:1,y:0}})]);
  const submitted=[];
  const h=harness(initial,async(url,options)=>{submitted.push(JSON.parse(options.body));return {ok:true,json:async()=>initial};});
  const three=attachThree(h);await h.run('viewportRouter.request("three")');
  assert.equal(three.frames.at(-1).state,initial.result.state);
  h.run('snapshot = { ...snapshot, result: { ...snapshot.result, state: { ...snapshot.result.state, round: 99 } } }; busy = true; syncThreeInteraction();');
  assert.equal(three.rendered.at(-1).candidates.length,0);
  assert.equal(three.frames.at(-1).state.round,1);
  h.run('snapshot = initial; busy = false; selectThreeHit({cell:{x:1,y:0}});');
  assert.deepEqual(Array.from(h.run('boardChooser.map(c=>c.key)')),['first','second']);
  h.run('playEvents = async () => {};');
  const command=h.run('chooseCandidate("second")');h.run('selectThreeHit({cell:{x:1,y:0}});');await command;
  assert.equal(submitted.length,1);assert.equal(submitted[0].candidateKey,'second');
  assert.deepEqual(Object.keys(submitted[0]).sort(),['candidateKey','expectedRevision']);
});

test('an app playback switch waits for StateAfter without duplicating history, dice or decisions', async () => {
  const initial=response();const final=structuredClone(initial);
  final.result.state.physical.figures[0].position.x=2;
  final.result.events=[{kind:'MovementCompleted',unitId:'actor'}];
  final.presentation.events=[{role:'Movement',text:'Supplied movement',unitId:'actor',path:[{x:0,y:0},{x:2,y:0}]}];
  final.result.resolutionSteps=[{eventIndex:0,stateAfter:final.result.state}];
  final.presentation.resolutionSteps=[{eventIndex:0,cards:final.presentation.cards}];
  const h=harness(initial);const three=attachThree(h);
  h.context.final=final;
  h.run(`snapshot = final; busy = true; ui.animate.checked = true;
    present = async () => { await viewportRouter.request('three'); if (threeViewport === viewportRouter.active) throw Error('Early handoff'); };
    pause = async () => {}; combatDice = { context: {attackerId:'actor',targets:[]}, names: new Map(), positions: new Map(), finished:true, outcomes:new Map(), attack:null,defences:new Map() };`);
  await h.run('playEvents(final)');
  assert.equal(three.frames.at(-1).state,final.result.state);
  assert.equal(three.frames.at(-1).state.physical.figures[0].position.x,2);
  assert.equal(h.elements.get('events').children.length,1);assert.equal(h.run('combatDice.context.attackerId'),'actor');
  assert.equal(h.run('playbackGeneration'),0);assert.equal(h.submitted.length,0);
});

for(const mode of ['normal','skip','disabled'])test(`observed facing follows matching AttackStarted after-frame and movement with Three inactive (${mode})`,async()=>{
  const initial=response();initial.result.state.physical.board.height=6;
  const final=structuredClone(initial),atAttack=structuredClone(initial.result.state);
  atAttack.physical.figures.find(f=>f.id==='b').position={x:0,y:2};
  final.result.events=[{kind:'AttackStarted',unitId:'actor',attackContext:{attackerId:'actor',actionId:'attack',targets:[{targetId:'b'}]}},{kind:'MovementCompleted',unitId:'a'}];
  final.presentation.events=[{role:'Notice',text:'Observed attack',unitId:'actor'},{role:'Movement',text:'Observed leg',unitId:'a',path:[{x:4,y:0},{x:4,y:1}]}];
  const afterMove=structuredClone(atAttack);afterMove.physical.figures.find(f=>f.id==='a').position={x:4,y:1};
  final.result.resolutionSteps=[{eventIndex:0,stateAfter:atAttack},{eventIndex:1,stateAfter:afterMove}];
  final.presentation.resolutionSteps=[0,1].map(eventIndex=>({eventIndex,cards:final.presentation.cards}));
  // The latest response deliberately disagrees with event-local target position.
  final.result.state.physical.figures.find(f=>f.id==='b').position={x:5,y:0};
  const h=harness(initial);h.context.final=final;h.context.mode=mode;
  h.run('snapshot=final;skipEffects=mode==="skip";ui.animate.checked=mode!=="disabled";present=async()=>{};pause=async()=>{};');
  await h.run('playEvents(final)');assert.equal(h.run('unitFacing.get("actor").yaw'),0);assert.equal(h.run('unitFacing.get("a").yaw-unitFacing.get("a").offset'),0);assert.equal(h.submitted.length,0);
});

for (const view of ["classic", "enhanced"]) for (const mode of ["animate", "disabled", "skip"])
for (const journey of ["known", "enter", "leave", "islands", "door", "phase"])
test(`${view}: observed movement ${journey} starts under fog, keeps gaps hidden and settles (${mode})`, async () => {
  const initial = response(), final = response();
  const known = journey === "islands" ? [0, 1, 4] : [0, 1, 2];
  for (const value of [initial, final]) {
    value.presentation.viewingSideId = "blue";
    value.result.state.fogOfWarSides = ["blue"];
    value.result.state.exploredCells = { blue: known.map(x => ({x,y:0})) };
    value.result.state.physical.figures = value.result.state.physical.figures.filter(f => f.id === "actor");
    value.result.state.units = value.result.state.units.filter(u => u.id === "actor");
  }
  const runs = journey === "enter" ? [[3,2,1]] : journey === "leave" ? [[1,2,3]] : journey === "islands" ? [[1,2],[3,4,5]] : [[0,1,2]];
  const end = runs.at(-1).at(-1);
  final.result.state.physical.figures[0].position.x = end;
  if (!known.includes(end)) { final.result.state.physical.figures = []; final.result.state.units = []; final.presentation.cards = {}; }
  if (journey === "enter") { initial.result.state.physical.figures = []; initial.result.state.units = []; initial.presentation.cards = {}; }
  if (journey === "door") final.result.state.physical.board.edges = [{a:{x:1,y:0},b:{x:2,y:0},kind:"OpenDoor"}];
  if (journey === "phase") final.result.state.physical.board.terrain = [{position:{x:1,y:0},kind:"SolidRock"}];
  final.result.events = [{kind:"MovementObserved",unitId:"actor"}];
  final.presentation.events = [{role:"Movement",text:"Observed movement",unitId:"actor",
    movementSegments:runs.map(run => run.map(x => ({x,y:0}))),
    movingFigure:{typeId:"grunt-type",sideId:"red",name:"Guard",posture:"Upright",cellSpan:1,hp:2,maxHp:2}}];
  final.result.resolutionSteps = [{eventIndex:0,stateAfter:final.result.state}];
  final.presentation.resolutionSteps = [{eventIndex:0,cards:final.presentation.cards}];
  const h = harness(initial, async () => ({ok:true,json:async () => final}));
  const frames = []; h.context.frames = frames;
  h.run(`boardView.value = '${view}'; updateBoardView(); ui.animate.checked = ${mode !== "disabled"};
    pause = async () => { const node = ui.board.querySelectorAll('.movement-figure')[0];
      if (node) frames.push({left:node.style.left, transition:node.style.transition, masks:ui.board.querySelectorAll('.movement-fog').length, interactive:node.dataset.unitId});
      ${mode === "skip" ? "skipEffects = true;" : ""} };`);
  await h.run('mutate("decision", { candidateKey:"opaque" })');
  if (mode !== "disabled") { assert.ok(frames.length); assert.ok(frames.every(f => f.masks === 6-known.length && !f.interactive)); assert.match(frames[0].transition, /linear/); }
  else assert.equal(frames.length, 0);
  assert.equal(h.elements.get("board").querySelectorAll(".movement-figure").length, 0);
  assert.equal(h.elements.get("board").querySelectorAll(".movement-fog").length, 0);
  assert.equal(!!h.figure("actor"), known.includes(end));
  if (h.figure("actor")) assert.equal(h.figure("actor").style.left, `${(end+.5)/6*100}%`);
});

for (const animate of [true, false]) {
  test(`Fog renders Unknown Cells, uses a generic bag and plays appearance/disappearance from supplied snapshots (animate=${animate})`, async () => {
    const initial = response(), revealed = response(), final = response();
    for (const value of [initial, revealed, final]) {
      value.presentation.viewingSideId = "blue";
      value.result.state.fogOfWarSides = ["blue"];
      value.result.state.exploredCells = { blue: [{x:0,y:0}, {x:1,y:0}, {x:2,y:0}] };
      value.result.state.bag = [];
      value.result.state.units = value.result.state.units.filter(u => u.id !== "a");
      value.result.state.physical.figures = value.result.state.physical.figures.filter(f => f.id !== "a");
      delete value.presentation.cards.a;
    }
    for (const value of [initial, final]) {
      value.result.state.units = value.result.state.units.filter(u => u.id !== "b");
      value.result.state.physical.figures = value.result.state.physical.figures.filter(f => f.id !== "b");
      delete value.presentation.cards.b;
    }
    revealed.result.state.physical.figures.find(f => f.id === "b").position.x = 2;
    final.result.events = [{kind:"MovementCompleted",unitId:"b"}, {kind:"MovementCompleted",unitId:"b"}];
    final.presentation.events = [{role:"Notice",text:"A guard appears"}, {role:"Notice",text:"A guard disappears"}];
    final.result.resolutionSteps = [revealed, final].map((v,eventIndex) => ({eventIndex,stateAfter:v.result.state}));
    final.presentation.resolutionSteps = [revealed, final].map((v,eventIndex) => ({eventIndex,cards:v.presentation.cards}));
    const h = harness(initial, async () => ({ok:true,json:async () => final})), seen = [];
    assert.equal(h.run('cells.get("3,0").classList.contains("Unknown")'), true);
    assert.equal(h.run('cells.get("3,0").dataset.visualId'), "Unknown");
    assert.equal(h.run('cells.get("3,0").style["--visual-image"]'), "none");
    assert.equal(h.run('cells.get("3,0").getAttribute("aria-label")'), "Unknown Cell (3,0)");
    assert.match(h.elements.get("bag").textContent, /hidden composition/);
    assert.equal(h.figure("b"), undefined);
    h.context.seen = seen; h.context.animate = animate;
    h.run('ui.animate.checked = animate; pause = async () => {}; const baseRender = renderState; renderState = (...args) => { baseRender(...args); seen.push(figures.has("b")); };');
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.deepEqual(seen, [true,false,false]);
    assert.equal(h.figure("b"), undefined);
    assert.equal(h.run('snapshot.presentation.viewingSideId'), "blue");
  });
}

for (const animate of [true, false]) {
  test(`swap and external movement render supplied positions together (animate=${animate})`, async () => {
    const initial = response(), swapped = state(), displaced = state();
    swapped.physical.figures[0].position.x = 4;
    swapped.physical.figures[1].position.x = 0;
    displaced.physical.figures[0].position.x = 4;
    displaced.physical.figures[1].position.x = 1;
    const final = response();
    final.result.state = displaced;
    final.result.events = [
      { kind: "PlacesSwapped", unitId: "actor", targetId: "a" },
      { kind: "UnitRepositioned", unitId: "a", sourceUnitId: "actor" }
    ];
    final.presentation.events = [
      { role: "Notice", text: "Supplied simultaneous swap", unitId: "actor", targetId: "a" },
      { role: "Movement", text: "Supplied external movement", unitId: "a", path: [{ x: 0, y: 0 }, { x: 1, y: 0 }] }
    ];
    final.result.resolutionSteps = [swapped, displaced].map((stateAfter, eventIndex) => ({ eventIndex, stateAfter }));
    final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
    const h = harness(initial, async () => ({ ok: true, json: async () => final })), positions = [];
    h.context.animate = animate; h.context.positions = positions;
    h.run(`ui.animate.checked = animate; pause = async () => {};
      const renderNormally = renderState;
      renderState = (...args) => {
        renderNormally(...args);
        positions.push(args[0].physical.figures.slice(0, 2).map(figure => figure.position.x));
      };`);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.deepEqual(positions.map(value => Array.from(value)), [[4, 0], [4, 1], [4, 1]]);
    assert.equal(h.figure("a").style.left, "25%");
    assert.match(h.elements.get("events").textContent, /Supplied simultaneous swap/);
    assert.match(h.elements.get("events").textContent, /Supplied external movement/);
  });
}

test("designer play hook submits existing revisioned imported-scenario operation", async () => {
  const initial = response(), submitted = [];
  const h = harness(initial, async (url, options) => {
    submitted.push([url, JSON.parse(options.body)]);
    return { ok: true, json: async () => response() };
  });
  h.run("ui.animate.checked = false");
  assert.equal(await h.run('delvePlayDesign("DELVE1:from-designer")'), true);
  assert.deepEqual(submitted, [["/api/game/scenario/import", { expectedRevision: 8, transport: "DELVE1:from-designer" }]]);
});

test("designer play waits for an in-flight replacement and submits the completed revision", async () => {
  const initial = response(), resolved = response(), fresh = response();
  resolved.revision = 9; fresh.revision = 10;
  let release;
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    if (requests.length === 1) await new Promise(resolve => { release = resolve; });
    return { ok: true, json: async () => requests.length === 1 ? resolved : fresh };
  });
  h.run("ui.animate.checked = false");
  const mutation = h.run('mutate("decision", { candidateKey: "opaque" })');
  const play = h.run('delvePlayDesign("DELVE1:queued-design")');
  assert.equal(requests.length, 1);
  release(); await mutation;
  assert.equal(await play, true);
  assert.deepEqual(requests[1], { url: "/api/game/scenario/import", body: { expectedRevision: 9, transport: "DELVE1:queued-design" } });
});

// Large bases are generic projection data; the renderer never recognizes a Unit Type id.
function largeResponse(candidates = []) {
  const snapshot = response(candidates);
  snapshot.result.state.physical.board.height = 4;
  snapshot.presentation.figures = {
    actor: { anchor: { x: 0, y: 0 }, cellSpan: 2, occupiedCells: [
      { x: 0, y: 0 }, { x: 1, y: 0 }, { x: 0, y: 1 }, { x: 1, y: 1 }] },
    a: { anchor: { x: 4, y: 0 }, cellSpan: 1, occupiedCells: [{ x: 4, y: 0 }] },
    b: { anchor: { x: 5, y: 0 }, cellSpan: 1, occupiedCells: [{ x: 5, y: 0 }] }
  };
  return snapshot;
}

test("generic 2x2 Figure has one identity and all four Cells select and inspect it", async () => {
  const initial = largeResponse([choice("select-opaque", "Unit", { unitId: "actor" })]);
  const submitted = [];
  const h = harness(initial, async (url, options) => { submitted.push(JSON.parse(options.body)); return { ok: true, json: async () => initial }; });
  assert.equal(h.figure("actor").classList.contains("large"), true);
  assert.ok(Math.abs(parseFloat(h.figure("actor").style.left) - 100 / 6) < 1e-10);
  assert.equal(h.figure("actor").style.top, "25%");
  assert.equal(h.figure("actor").style.width, `${190 / 6}%`);
  assert.equal(h.run("figures.size"), 3);
  for (const key of ["0,0", "1,0", "0,1", "1,1"]) {
    const tile = h.run(`cells.get("${key}")`);
    tile.listeners.mouseenter();
    assert.match(h.elements.get("unit-card").textContent, /A printed name/);
    await click(tile);
    assert.equal(submitted.at(-1).candidateKey, "select-opaque");
  }
});

test("Cell targets under a large base retain distinct supplied choices", async () => {
  const initial = largeResponse([
    choice("cell-zero", "Position", { position: { x: 0, y: 0 } }),
    choice("cell-one", "Position", { position: { x: 1, y: 1 } })
  ]);
  const submitted = [];
  const h = harness(initial, async (url, options) => { submitted.push(JSON.parse(options.body)); return { ok: true, json: async () => initial }; });
  assert.equal(h.figure("actor").style.pointerEvents, "none");
  await click(h.run('cells.get("0,0")'));
  assert.equal(submitted.at(-1).candidateKey, "cell-zero");
  await click(h.run('cells.get("1,1")'));
  assert.equal(submitted.at(-1).candidateKey, "cell-one");
});

test("placement previews use supplied Cells without expanding anchor choices", () => {
  const h = harness(largeResponse([choice("move-opaque", "Position", { position: { x: 2, y: 2 } }, {
    placementCells: [{ x: 2, y: 2 }, { x: 3, y: 2 }, { x: 2, y: 3 }, { x: 3, y: 3 }]
  })]));
  h.run('cells.get("2,2").listeners.mouseenter()');
  for (const key of ["2,2", "3,2", "2,3", "3,3"]) {
    assert.equal(h.run(`cells.get("${key}").classList.contains("placement-preview")`), true);
  }
  assert.equal(h.run('cells.get("3,3").classList.contains("board-choice")'), false);
  h.run('cells.get("2,2").listeners.mouseleave()');
  assert.equal(h.run('cells.get("3,3").classList.contains("placement-preview")'), false);
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`2x2 movement and Lying playback retain authoritative square geometry (${mode})`, async () => {
    const initial = largeResponse(), final = largeResponse();
    final.result.state.physical.figures[0].position = { x: 2, y: 1 };
    final.result.state.physical.figures[0].posture = "Lying";
    final.presentation.figures.actor = { anchor: { x: 2, y: 1 }, cellSpan: 2, occupiedCells: [
      { x: 2, y: 1 }, { x: 3, y: 1 }, { x: 2, y: 2 }, { x: 3, y: 2 }] };
    final.presentation.events = [{ role: "Movement", text: "Supplied movement", unitId: "actor",
      path: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }, { x: 2, y: 1 }] }];
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: final.presentation.cards, figures: final.presentation.figures }];
    const h = harness(initial, async () => ({ ok: true, json: async () => final }));
    h.context.mode = mode;
    h.run('ui.animate.checked = mode !== "disabled"; pause = async () => { if (mode === "skip") skipEffects = true; }');
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.equal(h.figure("actor").style.left, "50%");
    assert.equal(h.figure("actor").style.top, "50%");
    assert.equal(h.figure("actor").dataset.cellSpan, 2);
    assert.equal(h.figure("actor").classList.contains("large"), true);
    assert.equal(h.figure("actor").classList.contains("lying"), true);
    assert.equal(h.run('figureCells(displayedState.physical.figures[0]).length'), 4);
    assert.equal(h.run("figures.size"), 3);
  });
}

for (const mode of ["animate", "disabled", "skip"]) {
  test(`Spawn Goblin uses supplied Cell choice and new Lying figure from StateAfter (${mode})`, async () => {
    const printed = { content: { id: "spawn-goblin", name: "Spawn Goblin", category: "Action",
      description: "Place one Lying Goblin in an adjacent empty Cell.", maxUses: null, useLimitText: null }, uses: null };
    const supplied = choice("opaque placement", "Position", { position: { x: 1, y: 0 } },
      { entryId: "spawn-goblin", label: "Spawn Goblin (Action) at supplied Cell" });
    const initial = response([supplied], [printed]), final = response();
    initial.presentation.cards.actor.displayName = "Shaman";
    final.result.state.types.push({ id: "goblin-type", displayName: "Goblin", mov: 4, rng: 1, atk: 2, def: 2, hp: 1 });
    final.result.state.units.push({ id: "new-goblin", typeId: "goblin-type", sideId: "red", currentHp: 1 });
    final.result.state.physical.figures.push({ id: "new-goblin", position: { x: 1, y: 0 }, posture: "Lying" });
    final.presentation.cards["new-goblin"] = { displayName: "Goblin", entries: [entry("attack", "Attack")] };
    final.presentation.events = [{ role: "Notice", text: "Supplied Unit creation" }];
    final.result.events = [{ kind: "UnitCreated" }];
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: final.presentation.cards }];
    const requests = [], h = harness(initial, async (url, request) => {
      requests.push(JSON.parse(request.body));
      return { ok: true, json: async () => final };
    });
    assert.match(h.elements.get("unit-card").textContent, /Shaman.*Spawn Goblin/s);
    assert.doesNotMatch(h.elements.get("unit-card").textContent, /Flee|uses|\/game/);
    assert.equal(h.figure("new-goblin"), undefined);
    h.context.mode = mode;
    h.run(`ui.animate.checked = mode !== "disabled"; pause = async () => {};
      const originalPresent = present;
      present = async event => { await originalPresent(event); if (mode === "skip") ui.skip.listeners.click(); };`);
    await click(h.run('cells.get("1,0")'));
    assert.deepEqual(requests, [{ expectedRevision: 8, candidateKey: supplied.key }]);
    assert.equal(h.figure("new-goblin").classList.contains("lying"), true);
    assert.match(h.figure("new-goblin").title, /Goblin.*Lying/);
    assert.equal(h.figure("new-goblin").dataset.unitId, "new-goblin");
    h.figure("new-goblin").listeners.mouseenter();
    assert.match(h.elements.get("unit-card").textContent, /Goblin.*Attack/s);
  });
}

for (const name of ["Holy Wave", "Fire Breath", "An unfamiliar ability"]) for (const affected of [["a"], ["a", "b"]]) {
  test(`Direct ${name} previews authoritative membership (${affected.length}) and submits one opaque key`, () => {
    const direct = choice("opaque complete choice", "Direct", {}, { entryId: "opaque-entry", affectedUnitIds: affected });
    const initial = response([direct, choice("opaque single target", "Unit", { unitId: "a" })],
      [entry("opaque-entry", name, "Action", "Lay down all adjacent upright enemies.\nThen lay down this Unit.")]);
    const h = harness(initial);
    h.run("chooseCandidate = key => submitted.push(key)");
    const button = buttons(h.elements.get("unit-card"))[0];
    assert.equal(button.textContent, direct.label);
    assert.equal(button.disabled, false);
    assert.match(h.elements.get("unit-card").textContent, /0 \/ 2 uses/);
    assert.equal(h.elements.get("choices").children.length, 0);
    for (const event of ["mouseenter", "focus"]) {
      button.listeners[event]();
      for (const id of ["actor", "a", "b"]) assert.equal(h.figure(id).classList.contains("affected-preview"), affected.includes(id));
      button.listeners[event === "focus" ? "blur" : "mouseleave"]();
      assert.equal(h.figure("a").classList.contains("affected-preview"), false);
    }
    click(h.figure("a")); click(button);
    assert.deepEqual(h.submitted, ["opaque single target", "opaque complete choice"]);
    // Affected Units never become selection controls merely by belonging to the set.
    assert.equal(h.figure("b").classList.contains("board-choice"), false);
    h.figure("b").listeners.mouseenter();
    assert.equal(h.elements.get("choices").children[0].textContent, direct.label);
    click(h.elements.get("choices").children[0]);
    assert.equal(h.submitted[2], direct.key);
    h.figure("b").listeners.mouseleave();
    assert.equal(h.elements.get("choices").children.length, 0);
    initial.presentation.decision.candidates = [];
    h.run("renderSnapshot()");
    assert.equal(buttons(h.elements.get("unit-card")).length, 0);
    assert.equal(h.figure("a").classList.contains("board-choice"), false);
  });
}

for (const name of ["Cleave", "Heal", "Claw Attack", "New unit ability"]) {
  test(`Unit interaction ${name} trusts choices despite contradictory state`, () => {
    const supplied = choice("unparsed unit key", "Unit", { unitId: "b" }, { entryId: "arbitrary", affectedUnitIds: ["b"], label: name });
    const initial = response([supplied], [entry("arbitrary", name)]);
    initial.presentation.decision.noneChoice = choice(null, "Direct", {}, { label: "Supplied optional wording" });
    const h = harness(initial);
    h.run("chooseCandidate = key => submitted.push(key)");
    click(h.figure("b")); click(h.elements.get("choices").children[0]);
    assert.deepEqual(h.submitted, ["unparsed unit key", null]);
    assert.match(h.figure("b").attributes["aria-label"], new RegExp(name));
    assert.equal(h.elements.get("prompt").textContent, "Supplied prompt");
    assert.equal(h.figure("a").classList.contains("board-choice"), false);
  });
}

test("Position, Door, Unit selection and optional stay bind explicit references", () => {
  const door = { a: { x: 0, y: 0 }, b: { x: 1, y: 0 }, kind: "ClosedDoor" };
  const initial = response([
    choice("opaque move", "Position", { position: { x: 2, y: 0 } }),
    choice("opaque door", "Door", { door }),
    choice("opaque selection, not a Unit ID", "Unit", { unitId: "b" })
  ]);
  initial.result.state.physical.board.edges = [door];
  initial.presentation.decision.noneChoice = choice(null, "Unit", { unitId: "actor" }, { label: "Stay supplied" });
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  click(h.elements.get("board").children.find(n => n.dataset.cell === "2,0"));
  click(h.run('edges.get("0,0|1,0")'));
  click(h.figure("b")); click(h.figure("actor"));
  assert.deepEqual(h.submitted, ["opaque move", "opaque door", "opaque selection, not a Unit ID", null]);
  assert.equal(h.elements.get("choices").children.length, 0);
});

test("ambiguous board choices use the chooser and missing references retain a fallback", () => {
  const initial = response([
    choice("first", "Unit", { unitId: "b" }), choice("second", "Unit", { unitId: "b" }),
    choice("missing", "Unit", { unitId: "absent" }), choice("unknown direct", "Direct", {}, { entryId: "no-card-entry" })
  ]);
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  assert.equal(h.figure("b").classList.contains("board-choice"), true);
  for (const button of h.elements.get("choices").children) click(button);
  assert.deepEqual(h.submitted, ["missing", "unknown direct"]);
  click(h.figure("b"));
  for (const button of buttons(h.elements.get("choices").children[0]).slice(0, 2)) click(button);
  assert.deepEqual(h.submitted, ["missing", "unknown direct", "first", "second"]);
});

test("all card wording, categories, counters and effective stats are supplied", () => {
  const entries = ["Action", "Free Action", "Bonus Action", "Passive", "Follow-up"].map((category, i) =>
    entry(`id-${i}`, `Unfamiliar ${i}`, category, `Authoritative wording ${i}`));
  entries[3].uses = null; entries[3].content.maxUses = null; entries[3].content.useLimitText = null;
  const initial = response([], entries);
  // Raw engine content should not influence names, descriptions or ability counter lookup.
  initial.result.state.types[0].passives = [{ name: "Wrong name", displayText: "Wrong wording" }];
  initial.result.state.types[0].bonusActions = [{ name: "Wrong name", modifiers: [{ amount: 999, stat: "Atk" }] }];
  const h = harness(initial);
  const content = h.elements.get("unit-card").textContent;
  assert.match(content, /A printed name/);
  assert.match(content, /ATK 3 → 17/); assert.match(content, /DEF 2 → 9/);
  for (let i = 0; i < entries.length; i++) assert.ok(content.includes(`Authoritative wording ${i}`));
  assert.ok(!content.includes("Wrong")); assert.ok(!content.includes("999"));
  assert.equal((content.match(/0 \/ 2 uses/g) ?? []).length, 4);
  assert.equal((content.match(/\[2\/game\]/g) ?? []).length, 4);
});

test("relevance filtering is local and does not infer ability timing or uses", () => {
  const initial = response([choice("legal irrelevant", "Direct", {}, { entryId: "bonus", relevant: false })], [entry("bonus", "Unknown bonus", "Bonus Action")]);
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  assert.equal(buttons(h.elements.get("unit-card"))[0].hidden, true);
  h.run("ui.filter.checked = false; renderSnapshot()");
  assert.equal(buttons(h.elements.get("unit-card"))[0].hidden, false);
  click(buttons(h.elements.get("unit-card"))[0]);
  assert.deepEqual(h.submitted, ["legal irrelevant"]);
  h.run("busy = true; updateControls()");
  assert.equal(buttons(h.elements.get("unit-card"))[0].disabled, true);
  buttons(h.elements.get("unit-card"))[0].listeners.focus();
  assert.equal(h.figure("a").classList.contains("affected-preview"), false);
  assert.equal(h.elements.get("auto").checked, false);
});

test("decision requests submit only revision and opaque key", async () => {
  const initial = response([choice("arbitrary:do not parse", "Direct")]);
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    return { ok: true, json: async () => initial };
  });
  h.run("ui.animate.checked = false");
  await click(h.elements.get("choices").children[0]);
  assert.deepEqual(requests, [{ url: "/api/game/decision", body: { expectedRevision: 8, candidateKey: "arbitrary:do not parse" } }]);
});

test("keyboard board selection keeps supplied labels and HP accessibility", () => {
  const h = harness(response([choice("opaque keyboard", "Unit", { unitId: "b" })]));
  h.run("chooseCandidate = key => submitted.push(key)");
  const figure = h.figure("b");
  assert.match(figure.attributes["aria-label"], /Supplied label opaque keyboard.*HP 4\/4/);
  assert.equal(figure.attributes.role, "button");
  let prevented = 0;
  for (const key of ["Escape", "Enter", " "]) figure.listeners.keydown({ key, preventDefault() { prevented++; } });
  assert.equal(prevented, 2);
  assert.deepEqual(h.submitted, ["opaque keyboard", "opaque keyboard"]);
});

test("several Direct candidates for one entry each retain a complete selection", () => {
  const initial = response([
    choice("one", "Direct", {}, { entryId: "entry" }), choice("two", "Direct", {}, { entryId: "entry" })
  ], [entry("entry", "Unfamiliar alternatives")]);
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  for (const button of buttons(h.elements.get("unit-card"))) click(button);
  assert.deepEqual(h.submitted, ["one", "two"]);
  assert.equal(h.elements.get("choices").children.length, 0);
});

test("uncertain mutations synchronize once without resubmitting a decision", async () => {
  const initial = response([choice("opaque failed request", "Direct")]);
  const synchronized = response([choice("new supplied key", "Direct")]);
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, method: options.method });
    return options.method === "POST"
      ? { ok: false, json: async () => ({ detail: "Stale revision" }) }
      : { ok: true, json: async () => synchronized };
  });
  await click(h.elements.get("choices").children[0]);
  assert.deepEqual(requests, [{ url: "/api/game/decision", method: "POST" }, { url: "/api/game", method: "GET" }]);
  assert.equal(h.elements.get("choices").children[0].textContent, "Supplied label new supplied key");
  assert.equal(h.elements.get("choices").children[0].disabled, false);
  assert.match(h.elements.get("error").textContent, /Synchronized to the server/);
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`progressive StateAfter and card counters remain authoritative (${mode})`, async () => {
    const initial = response();
    const final = response([choice("end", "Direct")]);
    final.result.state = state(1);
    final.presentation.cards = cards([entry("limited", "Printed ability")]);
    final.presentation.cards.actor.entries[0].uses.remainingUses = 0;
    const intermediate = state(3), intermediateCards = cards([entry("limited", "Printed ability")]);
    intermediateCards.actor.entries[0].uses.remainingUses = 1;
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: intermediate }, { eventIndex: 1, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: intermediateCards }, { eventIndex: 1, cards: final.presentation.cards }];
    final.presentation.events = [0, 1].map(() => ({ role: "AttackTarget", text: "Supplied attack result", unitId: "b", targetId: "actor", hits: 100, blocks: 0, damage: 99 }));
    // Raw events differ deliberately; the browser uses projected outcomes and authoritative states.
    final.result.events = [{ kind: "Ignored raw event" }];
    const shown = [], before = [];
    const h = harness(initial, async () => ({ ok: true, json: async () => final }));
    h.context.shown = shown; h.context.before = before; h.context.mode = mode;
    h.run(`
      ui.animate.checked = mode !== "disabled";
      pause = async () => {};
      const originalRender = renderState;
      renderState = (state, preserve, cards) => {
        originalRender(state, preserve, cards);
        shown.push({ hp: figures.get("actor").dataset.hpLabel, card: ui["unit-card"].textContent, choices: ui.choices.children.length });
      };
      const originalPresent = present;
      present = async event => {
        before.push(figures.get("actor").dataset.hpLabel);
        await originalPresent(event);
        if (mode === "skip") ui.skip.listeners.click();
      };
    `);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.deepEqual(shown.map(value => value.hp), ["HP 3/4", "HP 1/4", "HP 1/4"]);
    assert.match(shown[0].card, /1 \/ 2 uses/); assert.match(shown[1].card, /0 \/ 2 uses/);
    assert.deepEqual(shown.slice(0, 2).map(value => value.choices), [0, 0]);
    assert.deepEqual(before, mode === "animate" ? ["HP 4/4", "HP 3/4"] : mode === "skip" ? ["HP 4/4"] : []);
    assert.equal(h.elements.get("choices").children.length, 1);
    assert.equal(h.elements.get("choices").children[0].disabled, false);
    assert.equal(h.elements.get("events").children.length, 2);
    assert.equal(h.elements.get("error").textContent, "");
  });
}

test("aggregate attacks with unfamiliar names use the summary role without target animation", async () => {
  const h = harness(response());
  const pauses = [];
  h.context.pauses = pauses;
  h.run("pause = async ms => pauses.push(ms)");
  h.context.outcome = { role: "AttackSummary", text: "Unknown attack: supplied one-target summary", abilityName: "Never heard of this" };
  await h.run("present(outcome)");
  assert.deepEqual(pauses, [450]);
  assert.equal(h.elements.get("effect").textContent, h.context.outcome.text);
  assert.equal(h.figure("actor").classList.contains("attacking"), false);
});

test("movement retains mounted figures and commits each progressive animation start", async () => {
  const initial = response(), final = response();
  final.presentation.decision = null;
  final.result.state = state(4, 2);
  final.result.resolutionSteps = [0, 1].map(i => ({ eventIndex: i, stateAfter: state(4, i + 1) }));
  final.presentation.resolutionSteps = [0, 1].map(i => ({ eventIndex: i, cards: cards() }));
  final.presentation.events = [0, 1].map(i => ({ role: "Movement", text: "Movement supplied", unitId: "actor", path: [{ x: i, y: 0 }, { x: i + 1, y: 0 }] }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  const mounted = h.figure("actor"), starts = [], positions = [];
  mounted.getBoundingClientRect = () => { starts.push(mounted.style.left); return {}; };
  h.context.positions = positions; h.context.mounted = mounted;
  h.run('pause = async () => positions.push({ same: figures.get("actor") === mounted, left: mounted.style.left })');
  await h.run('mutate("decision")');
  assert.deepEqual(starts, [`${.5 / 6 * 100}%`, `${1.5 / 6 * 100}%`]);
  assert.deepEqual(positions.map(p => p.left), [`${1.5 / 6 * 100}%`, `${2.5 / 6 * 100}%`]);
  assert.ok(positions.every(p => p.same));
});

for (const name of ["Telekinesis", "Unfamiliar posture action"]) for (const surface of ["figure", "cell"]) {
  test(`${name} uses generic Unit targeting on ${surface} and authoritative posture after submission`, async () => {
    const printed = entry("opaque-entry", name, "Action", "Lay down an upright enemy within RNG and LOS.");
    printed.content.maxUses = null; printed.content.useLimitText = null; printed.uses = null;
    const attack = choice("opaque normal attack", "Unit", { unitId: "a" });
    const action = choice("opaque posture choice", "Unit", { unitId: "a" },
      { entryId: "opaque-entry", label: `${name} (Action) → a`, affectedUnitIds: ["a"] });
    const initial = response([attack, action], [printed]), final = response([], [printed]);
    final.result.state.physical.figures[1].posture = "Lying";
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
    final.presentation.events = [{ role: "Notice", text: "a: Lying", unitId: "a" }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: cards([printed]) }];
    const requests = [], h = harness(initial, async (url, request) => {
      requests.push({ url, body: JSON.parse(request.body) });
      return { ok: true, json: async () => final };
    });
    h.run("pause = async () => {}");
    assert.match(h.elements.get("unit-card").textContent, new RegExp(name));
    assert.match(h.elements.get("unit-card").textContent, /Lay down an upright enemy within RNG and LOS\./);
    assert.doesNotMatch(h.elements.get("unit-card").textContent, /\/game|uses/);
    const target = h.figure("a"), left = target.style.left, top = target.style.top;
    // The fixture's same-side distant Unit is selected solely from the supplied interaction.
    click(surface === "figure" ? target : h.run('cells.get("4,0")'));
    const options = buttons(h.elements.get("choices").children[0]);
    assert.deepEqual(options.map(option => option.textContent), [attack.label, action.label, "Cancel"]);
    assert.equal(target.classList.contains("lying"), false);
    await click(options[1]);
    assert.deepEqual(requests, [{ url: "/api/game/decision", body: { expectedRevision: 8, candidateKey: action.key } }]);
    assert.equal(h.figure("a").classList.contains("lying"), true);
    assert.equal(h.figure("a").style.left, left); assert.equal(h.figure("a").style.top, top);
    assert.equal(h.figure("actor").classList.contains("attacking"), false);
  });
}

test("production browser contains no ability identities or concrete rule-counter interpretation", () => {
  assert.doesNotMatch(script, /Red Dragon|Fire Breath|Claw Attack|Troll|Undying|Telekinesis|Wizard|Fireball|HolyWave|Holy Wave|Cleave|\bHeal\b|Rage|Dash|Throwing Knife|Backstab|Aura|Fury|bonusActionUses|remainingUses\s*[<>]|\.modifiers|readableName|candidate\.action|candidate\.kind|event\.abilityName/);
});

test("Lying tokens render generic physical posture with readable identity and supplied choices", () => {
  const initial = response([choice("supplied despite posture", "Unit", { unitId: "actor" })]);
  initial.result.state.physical.figures[0].posture = "Lying";
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  assert.equal(h.figure("actor").classList.contains("lying"), true);
  assert.equal(h.figure("a").classList.contains("lying"), false);
  assert.match(h.figure("actor").attributes["aria-label"], /A printed name.*Lying.*HP 4\/4/);
  assert.match(h.figure("actor").textContent, /Apn1/);
  click(h.figure("actor"));
  assert.deepEqual(h.submitted, ["supplied despite posture"]);
  const mounted = h.figure("actor");
  initial.result.state.physical.figures[0].posture = "Upright";
  h.run("renderState(initial.result.state)");
  assert.equal(h.figure("actor"), mounted);
  assert.equal(mounted.classList.contains("lying"), false);
  const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
  assert.match(css, /\.figure\.lying\s*\{[^}]*aspect-ratio:\s*1\.65[^}]*rotate\(-25deg\)[^}]*border-style:\s*dashed/);
  assert.match(css, /\.figure\.lying \.figure-name[^}]*rotate\(25deg\)/);
});

for (const animate of [true, false]) {
  test(`posture changes follow authoritative StateAfter for unknown causes (animate=${animate})`, async () => {
    const initial = response(), final = response();
    const first = state();
    first.physical.figures[1].posture = "Lying";
    final.result.state.physical.figures[1].posture = "Lying";
    final.result.state.physical.figures[0].posture = "Lying";
    final.result.events = [{ kind: "Unknown future cause" }];
    final.presentation.events = [0, 1].map(i => ({ role: "Notice", text: "Supplied posture change", unitId: i ? "actor" : "a" }));
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: first }, { eventIndex: 1, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: cards() }));
    const shown = [], h = harness(initial, async () => ({ ok: true, json: async () => final }));
    h.context.shown = shown; h.context.animate = animate;
    h.run(`
      ui.animate.checked = animate;
      pause = async () => {};
      const originalRender = renderState;
      renderState = (...args) => {
        originalRender(...args);
        shown.push([figures.get("actor").classList.contains("lying"), figures.get("a").classList.contains("lying")]);
      };
    `);
    await h.run('mutate("decision")');
    assert.deepEqual(shown.map(x => Array.from(x)), [[false, true], [true, true], [true, true]]);
  });
}

for (const name of ["Fireball", "Unfamiliar option"]) for (const surface of ["figure", "cell"]) {
  test(`Unit and Position collision on ${surface} offers supplied labels for ${name}`, () => {
    const unit = choice("opaque attack", "Unit", { unitId: "b" }, { label: "Normal Attack supplied" });
    const position = choice("opaque other", "Position", { position: { x: 5, y: 0 } },
      { label: `${name} supplied`, affectedUnitIds: ["actor", "a"] });
    const h = harness(response([unit, position]));
    h.run("chooseCandidate = key => submitted.push(key)");
    const node = surface === "figure" ? h.figure("b") : h.run('cells.get("5,0")');
    click(node);
    assert.deepEqual(h.submitted, []);
    const chooser = h.elements.get("choices").children[0];
    assert.equal(chooser.className, "board-chooser");
    const items = buttons(chooser);
    assert.deepEqual(items.map(item => item.textContent), [unit.label, position.label, "Cancel"]);
    items[1].listeners.mouseenter();
    assert.equal(h.figure("actor").classList.contains("affected-preview"), true);
    assert.equal(h.figure("a").classList.contains("affected-preview"), true);
    assert.equal(h.figure("b").classList.contains("affected-preview"), false);
    click(items[1]);
    assert.deepEqual(h.submitted, [position.key]);
    click(items[2]);
    assert.equal(h.elements.get("choices").children.length, 0);
    assert.equal(h.figure("a").classList.contains("affected-preview"), false);
    // Reopen through the same keyboard-accessible board target.
    click(node); click(buttons(h.elements.get("choices").children[0])[0]);
    assert.deepEqual(h.submitted, [position.key, unit.key]);
  });
}

test("multiple Position candidates retain authoritative order and keyboard chooser submits opaque key", async () => {
  const options = ["z-key", "a-key"].map(key => choice(key, "Position", { position: { x: 2, y: 0 } }));
  const initial = response(options), requests = [];
  const h = harness(initial, async (url, request) => {
    requests.push({ url, body: JSON.parse(request.body) });
    return { ok: true, json: async () => response() };
  });
  const node = h.run('cells.get("2,0")');
  node.listeners.keydown({ key: "Enter", preventDefault() {} });
  const items = buttons(h.elements.get("choices").children[0]);
  assert.deepEqual(items.slice(0, 2).map(item => item.textContent), options.map(option => option.label));
  await click(items[1]);
  assert.deepEqual(requests, [{ url: "/api/game/decision", body: { expectedRevision: 8, candidateKey: "a-key" } }]);
});

test("one Position candidate on occupied Cell selects directly from either surface", () => {
  const h = harness(response([choice("opaque lone position", "Position", { position: { x: 5, y: 0 } })]));
  h.run("chooseCandidate = key => submitted.push(key)");
  click(h.figure("b")); click(h.run('cells.get("5,0")'));
  assert.deepEqual(h.submitted, ["opaque lone position", "opaque lone position"]);
  assert.equal(h.elements.get("choices").children.length, 0);
});

test("collision chooser only includes currently presented choices and ignores busy input", () => {
  const initial = response([
    choice("visible", "Unit", { unitId: "b" }),
    choice("filtered", "Position", { position: { x: 5, y: 0 } }, { relevant: false })
  ]);
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  click(h.figure("b"));
  assert.deepEqual(h.submitted, ["visible"]);
  h.elements.get("filter").checked = false;
  h.run("renderSnapshot(); busy = true");
  click(h.figure("b"));
  assert.equal(h.elements.get("choices").children.length, 0);
  h.run("busy = false"); click(h.figure("b"));
  assert.equal(buttons(h.elements.get("choices").children[0]).length, 3);
});

test("scenario controls use supplied catalog, switch board size and restart current", async () => {
  const initial = response(), switched = response(), restarted = response();
  const catalog = [
    { id: "setup-a", name: "Supplied first", description: "First description" },
    { id: "setup-b", name: "Supplied second", description: "Second description" }
  ];
  for (const value of [initial, switched, restarted]) value.scenarios = catalog;
  initial.scenarioId = "setup-a";
  for (const value of [switched, restarted]) {
    value.scenarioId = "setup-b";
    value.result.state.round = 0; value.result.state.currentUnitId = null;
    value.result.state.physical.board.width = 9;
    value.result.state.physical.board.height = 2;
    value.presentation.decision = null; value.result.nextInput = null;
  }
  switched.revision = 9; restarted.revision = 10;
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    return { ok: true, json: async () => requests.length === 1 ? switched : restarted };
  });
  const select = h.elements.get("scenario");
  assert.deepEqual(select.children.map(option => [option.value, option.textContent]), catalog.map(s => [s.id, s.name]));
  assert.equal(select.value, "setup-a");
  select.value = "setup-b"; select.listeners.change();
  assert.match(h.elements.get("scenario-description").textContent, /Second description/);
  await h.elements.get("start-scenario").listeners.click();
  assert.deepEqual(requests[0], { url: "/api/game/scenario", body: { expectedRevision: 8, scenarioId: "setup-b" } });
  assert.equal(h.elements.get("board").style.gridTemplateColumns, "repeat(9, minmax(0, 1fr))");
  assert.equal(h.run("cells.size"), 18);
  assert.equal(h.run("latestUnitId"), null);
  assert.equal(h.elements.get("round").disabled, false);
  // Selection can differ from the current scenario; restart uses server identity.
  select.value = "setup-a";
  await h.elements.get("restart-scenario").listeners.click();
  assert.deepEqual(requests[1], { url: "/api/game/restart", body: { expectedRevision: 9 } });
  assert.equal(h.run("snapshot.scenarioId"), "setup-b");
  assert.equal(h.elements.get("scenario").children.length, 2);
  assert.equal(h.run("snapshot.result.state.round"), 0);
});

test("scenario change interrupts playback and uses the completed request revision", async () => {
  const initial = response(), resolved = response(), fresh = response();
  resolved.revision = 9; fresh.revision = 10;
  resolved.presentation.events = [{ role: "Notice", text: "Pending playback" }, { role: "Notice", text: "More playback" }];
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    return { ok: true, json: async () => requests.length === 1 ? resolved : fresh };
  });
  h.run(`present = async () => {
    if (ui["start-scenario"].disabled) throw new Error("Scenario must remain available during playback");
    ui.scenario.value = "chosen-setup";
    void ui["start-scenario"].listeners.click();
  };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.deepEqual(requests.map(r => r.url), ["/api/game/decision", "/api/game/scenario"]);
  assert.deepEqual(requests[1].body, { expectedRevision: 9, scenarioId: "chosen-setup" });
  assert.equal(h.run("snapshot.revision"), 10);
  assert.equal(h.run("busy"), false);
});

test("scenario restart waits for an in-flight request without submitting its stale revision", async () => {
  const initial = response(), resolved = response(), fresh = response();
  resolved.revision = 9; fresh.revision = 10;
  let release;
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    if (requests.length === 1) await new Promise(resolve => { release = resolve; });
    return { ok: true, json: async () => requests.length === 1 ? resolved : fresh };
  });
  const pending = h.run('mutate("decision", { candidateKey: "opaque" })');
  const restarting = h.elements.get("restart-scenario").listeners.click();
  assert.equal(requests.length, 1);
  release(); await pending; await restarting;
  assert.deepEqual(requests[1], { url: "/api/game/restart", body: { expectedRevision: 9 } });
  assert.equal(h.run("snapshot.revision"), 10);
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`mandatory RollDice remains pending after playback (${mode})`, async () => {
    const initial = response([choice("commit-attack", "Direct")]);
    const final = response([choice("opaque-roll", "Direct", {}, { label: "Roll Dice" })]);
    final.revision = 9;
    final.presentation.decision.prompt = "actor: roll 3 Attack dice (supplied context)";
    final.presentation.decision.roll = { family: "Attack", count: 3, ownerUnitId: "actor", sourceActionId: "arbitrary" };
    final.result.nextInput = { kind: "RollDice", candidates: [{ key: "opaque-roll" }], allowsNone: false };
    final.result.events = [{ kind: "ActionUsed" }, { kind: "AttackStarted" }];
    final.presentation.events = [
      { role: "Notice", text: "actor used supplied Action" },
      { role: "Notice", text: "actor Attack started with fixed membership" }
    ];
    final.result.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, stateAfter: final.result.state }));
    final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
    const requests = [];
    const h = harness(initial, async (url, options) => {
      requests.push({ url, body: options.body ? JSON.parse(options.body) : null });
      return { ok: true, json: async () => final };
    });
    h.context.mode = mode;
    h.run(`
      ui.animate.checked = mode !== "disabled";
      pause = async () => {};
      const presentNormally = present;
      present = async event => {
        await presentNormally(event);
        if (mode === "skip") ui.skip.listeners.click();
      };
    `);
    await click(buttons(h.elements.get("choices"))[0]);
    assert.equal(requests.length, 1);
    assert.equal(requests[0].body.candidateKey, "commit-attack");
    assert.match(h.elements.get("prompt").textContent, /Roll 3 Attack dice/);
    assert.equal(h.elements.get("choices").children.length, 1, "the roll action has no duplicate descriptive paragraph");
    assert.equal(buttons(h.elements.get("choices")).length, 1);
    assert.equal(buttons(h.elements.get("choices"))[0].textContent, "Roll 3 Attack dice");
    assert.match(buttons(h.elements.get("choices"))[0].attributes["aria-label"], /Roll 3 Attack dice/);
    assert.equal(h.run("snapshot.result.nextInput.kind"), "RollDice");
    assert.equal(h.run("busy"), false);
    // Refresh only reprojects the supplied pending boundary.
    await h.elements.get("refresh").listeners.click();
    assert.equal(requests.length, 2);
    assert.equal(requests[1].body, null);
    await click(buttons(h.elements.get("choices"))[0]);
    assert.equal(requests.length, 3);
    assert.deepEqual(requests[2].body, { expectedRevision: 9, candidateKey: "opaque-roll" });
  });
}

test("dice faces, token Side and arbitrary agency are generic supplied presentation", async () => {
  const initial = response([choice("opaque", "Direct")]), final = response();
  for (const value of [initial, final]) {
    value.result.state.activeToken = { typeId: "opaque-type", sideId: "violet" };
    value.result.state.units[0].sideId = "violet";
    value.result.state.controllers = [{ token: value.result.state.activeToken, controller: "Human" }];
  }
  final.presentation.events = [
    { role: "Notice", text: "Token drawn: A printed name (violet)" },
    { role: "Notice", text: "actor: Attack dice [Hit, Miss, Hit], 2 successes (arbitrary)" },
    { role: "Notice", text: "a: Defence dice [Block, Miss], 1 success (arbitrary)" },
    { role: "Notice", text: "actor: D6 dice [4], 0 successes (arbitrary-check)" }
  ];
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push(JSON.parse(options.body)); return { ok: true, json: async () => final };
  });
  h.run("ui.animate.checked = false");
  assert.match(h.elements.get("active-token").textContent, /violet/);
  await click(buttons(h.elements.get("choices"))[0]);
  assert.equal(requests[0].candidateKey, "opaque");
  assert.match(h.elements.get("events").textContent, /Hit, Miss, Hit/);
  assert.match(h.elements.get("events").textContent, /Block, Miss/);
  assert.match(h.elements.get("events").textContent, /D6 dice \[4\]/);
  assert.match(h.elements.get("events").textContent, /violet/);
});

test("playback failure recovers pending RollDice without submitting it", async () => {
  const final = response([choice("opaque-roll", "Direct", {}, { label: "Roll Dice" })]);
  final.result.nextInput.kind = "RollDice";
  final.presentation.events = [{ role: "Notice", text: "Resolved attack start" }];
  const requests = [];
  const h = harness(response(), async (url, options) => {
    requests.push({ url, body: options.body ? JSON.parse(options.body) : null });
    return { ok: true, json: async () => final };
  });
  h.run('present = async () => { throw new Error("playback failed"); };');
  await h.run('mutate("decision", { candidateKey: "commit" })');
  assert.equal(requests.length, 2);
  assert.deepEqual(requests.map(r => r.url), ["/api/game/decision", "/api/game"]);
  assert.equal(requests[1].body, null);
  assert.equal(buttons(h.elements.get("choices"))[0].textContent, "Roll Dice");
  assert.equal(h.run("snapshot.result.nextInput.kind"), "RollDice");
});


test("side-1 and side-2 use distinct, stable colors on figures and activation tokens", () => {
  const initial = response();
  for (const unit of initial.result.state.units) unit.sideId = unit.id === "actor" ? "side-1" : "side-2";
  initial.result.state.activeToken.sideId = "side-1";
  initial.result.state.bag = [
    { typeId: "opaque-type", sideId: "side-1" }, { typeId: "opaque-type", sideId: "side-2" }
  ];
  const h = harness(initial);
  const first = h.figure("actor").style["--side-hue"], second = h.figure("a").style["--side-hue"];
  const gap = Math.abs(first - second);
  assert.ok(Math.min(gap, 360 - gap) >= 90);
  assert.equal(h.elements.get("bag").children[1].style["--side-hue"], first);
  assert.equal(h.elements.get("bag").children[2].style["--side-hue"], second);
  assert.equal(h.elements.get("active-token").children[1].style["--side-hue"], first);
  h.run('snapshot.result.state.units.reverse(); renderSnapshot();');
  assert.equal(h.figure("actor").style["--side-hue"], first);
  assert.equal(h.figure("a").style["--side-hue"], second);
});

test("arbitrary Side tokens and figures have deterministic generic styling", () => {
  const initial = response();
  initial.result.state.bag = [
    { typeId: "opaque-type", sideId: "violet" }, { typeId: "opaque-type", sideId: "amber" }
  ];
  initial.result.state.units[0].sideId = "violet";
  initial.result.state.units[1].sideId = "amber";
  const h = harness(initial);
  h.run('styleSide = (node, id) => { node.dataset.side = id; }; renderSnapshot();');
  const bag = h.elements.get("bag");
  assert.match(bag.textContent, /2 remaining/);
  assert.match(bag.textContent, /A printed name \u00b7 violet/);
  assert.match(bag.textContent, /A printed name \u00b7 amber/);
  assert.equal(bag.children[1].dataset.side, "violet");
  assert.equal(bag.children[2].dataset.side, "amber");
  assert.equal(h.figure("actor").dataset.side, "violet");
  assert.equal(h.figure("a").dataset.side, "amber");
  const hues = [];
  const styled = { style: { setProperty: (key, value) => hues.push(value) } };
  h.context.styled = styled;
  // Use the original function in a fresh harness, independent of the spy above.
  const other = harness(initial); other.context.styled = styled;
  other.run('styleSide(styled, "arbitrary-side"); styleSide(styled, "arbitrary-side"); styleSide(styled, "another-side");');
  assert.equal(hues[0], hues[1]); assert.notEqual(hues[0], hues[2]);
});

test("activation and attack focus uses explicit Unit and fixed target metadata", () => {
  const h = harness(response());
  h.run('prepareOccurrence({ kind: "ActivationStarted", unitId: "b" }, { text: "Supplied activation" });');
  assert.equal(h.figure("b").classList.contains("event-focus"), true);
  assert.equal(h.run("latestUnitId"), "b");
  h.run(`prepareOccurrence({ kind: "AttackStarted", unitId: "actor", attackContext: {
    attackerId: "actor", actionId: "future-action", targets: [{ targetId: "b" }]
  } }, { text: "Supplied attack" });`);
  assert.equal(h.figure("actor").classList.contains("attack-source"), true);
  assert.equal(h.figure("b").classList.contains("attack-target"), true);
  assert.equal(h.figure("a").classList.contains("attack-target"), false);
  assert.match(h.elements.get("attack-context").textContent, /future-action \u2192 Inspected name \(5,0\)/);
  h.run('renderState(snapshot.result.state);');
  assert.equal(h.figure("b").classList.contains("attack-target"), true);
  h.run('prepareOccurrence({ kind: "ActivationCompleted", unitId: "actor" }, { text: "Complete" });');
  assert.equal(h.figure("b").classList.contains("attack-target"), false);
});

for (const [family, faces, successes, label] of [
  ["Attack", ["Hit", "Miss", "Hit"], 7, "7 Hits"],
  ["Defence", ["Block", "Miss"], 8, "8 Blocks"],
  ["D6", ["4"], 0, "Failed"], ["D6", ["2"], 1, "Success"]
]) {
  test(`${family} graphical dice use supplied faces and interpretation without recounting`, async () => {
    const h = harness(response());
    h.context.occurrence = { kind: "DiceRolled", unitId: "actor", dice: {
      pool: { family, count: faces.length, purpose: "Future check", ownerUnitId: "actor",
        sourceUnitId: "actor", sourceActionId: "unfamiliar-action" }, faces, successes
    } };
    h.run('pause = async () => {}; prepareOccurrence(occurrence, { text: "Authoritative dice" });');
    await h.run('present({ text: "Authoritative dice" }, occurrence)');
    const dice = h.elements.get("dice");
    assert.deepEqual(dice.children[1].children.map(node => node.textContent), faces);
    assert.equal(dice.children[2].textContent, label);
    assert.match(dice.textContent, /unfamiliar-action/);
    assert.equal(dice.classList.contains("rolling"), false);
  });
}

test("unknown actions show supplied category and target, Cell and door context", () => {
  const h = harness(response());
  h.run(`prepareOccurrence({ kind: "ActionUsed", unitId: "actor", targetId: "b",
    cell: { x: 3, y: 0 }, door: { a: { x: 2, y: 0 }, b: { x: 3, y: 0 } }
  }, { text: "actor used Future Magic (Free Action)" });`);
  assert.match(h.elements.get("effect").textContent, /Future Magic \(Free Action\).*Inspected name \(5,0\).*\(3,0\).*door 2,0 \/ 3,0/);
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`multi-target consequences and graphical dice follow authoritative order (${mode})`, async () => {
    const initial = response(), final = response();
    const afterA = state(), afterB = state();
    afterA.units[1].currentHp = 2;
    afterB.units[1].currentHp = 2; afterB.units[2].currentHp = 1;
    final.result.state = afterB;
    const pool = { family: "Defence", count: 1, purpose: "Defence", ownerUnitId: "a", sourceUnitId: "actor", sourceActionId: "future" };
    final.result.events = [
      { kind: "AttackStarted", unitId: "actor", attackContext: { attackerId: "actor", actionId: "future", targets: [{ targetId: "a" }, { targetId: "b" }] } },
      { kind: "DiceRolled", unitId: "a", dice: { pool, faces: ["Block"], successes: 1 } },
      { kind: "AttackTargetResolved", unitId: "actor", targetId: "a" },
      { kind: "AttackTargetResolved", unitId: "actor", targetId: "b" }
    ];
    final.presentation.events = final.result.events.map(event => ({ role: "Notice", text: event.kind, unitId: event.unitId, targetId: event.targetId }));
    final.result.resolutionSteps = [initial.result.state, initial.result.state, afterA, afterB].map((stateAfter, eventIndex) => ({ eventIndex, stateAfter }));
    final.presentation.resolutionSteps = final.result.resolutionSteps.map(step => ({ eventIndex: step.eventIndex, cards: final.presentation.cards }));
    const calls = [], shown = [];
    const h = harness(initial, async (url, options) => {
      calls.push(JSON.parse(options.body)); return { ok: true, json: async () => final };
    });
    h.context.shown = shown; h.context.mode = mode;
    h.run(`ui.animate.checked = mode !== "disabled";
      pause = async () => { if (mode === "skip") skipEffects = true; };
      const renderNormally = renderState;
      renderState = (...args) => { renderNormally(...args); shown.push(args[0].units.slice(1).map(unit => unit.currentHp)); };`);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.deepEqual(shown.map(values => Array.from(values)), [[4,4], [4,4], [2,4], [2,1], [2,1]]);
    assert.equal(calls.length, 1);
    assert.deepEqual(calls[0], { expectedRevision: 8, candidateKey: "opaque" });
    assert.match(h.elements.get("dice").textContent, /Block/);
    assert.equal(h.elements.get("dice").classList.contains("rolling"), false);
  });
}


test("token draw and Round presentation uses each supplied bag snapshot", async () => {
  const initial = response(), final = response();
  const token = { typeId: "opaque-type", sideId: "future-side" };
  const round = state(), drawn = state();
  round.bag = [token]; round.activeToken = null;
  drawn.bag = []; drawn.activeToken = token;
  final.result.state = drawn;
  final.result.events = [{ kind: "RoundStarted" }, { kind: "TokenDrawn", token }];
  final.presentation.events = [{ role: "Notice", text: "Round 1 started" }, { role: "Notice", text: "Token drawn" }];
  final.result.resolutionSteps = [round, drawn].map((stateAfter, eventIndex) => ({ eventIndex, stateAfter }));
  final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
  const shown = [], h = harness(initial, async () => ({ ok: true, json: async () => final }));
  h.context.shown = shown;
  h.run('pause = async () => shown.push({ bag: ui.bag.textContent, token: ui["active-token"].textContent });');
  await h.run('mutate("round")');
  assert.match(shown[0].bag, /1 remaining/);
  assert.match(shown[1].bag, /0 remaining/);
  assert.match(shown[1].token, /future-side/);
});

test("door action, D6 result and outcome share generic supplied door emphasis", async () => {
  const initial = response();
  const door = { a: { x: 0, y: 0 }, b: { x: 1, y: 0 }, kind: "ClosedDoor" };
  initial.result.state.physical.board.edges = [door];
  const h = harness(initial);
  h.context.door = door;
  h.run(`prepareOccurrence({ kind: "ActionUsed", unitId: "actor", door }, { text: "Future door action" });`);
  assert.equal(h.run('edges.get(edgeKey(door)).classList.contains("door-focus")'), true);
  h.run(`prepareOccurrence({ kind: "DiceRolled", unitId: "actor", door, dice: {
    pool: { family: "D6", count: 1, purpose: "Door check", sourceActionId: "unknown-check", sourceUnitId: "actor", ownerUnitId: "actor", door },
    faces: ["6"], successes: 0
  } }, { text: "Supplied D6" });`);
  assert.match(h.elements.get("dice").textContent, /1d6.*6.*Failed/);
  assert.equal(h.run('edges.get(edgeKey(door)).classList.contains("door-focus")'), true);
});

test("skip immediately settles graphical dice without requests or results", async () => {
  const requests = [], h = harness(response(), async (url, options) => {
    requests.push({ url, options }); return { ok: true, json: async () => response() };
  });
  h.run(`renderDice({ pool: { family: "Attack", count: 1, purpose: "Attack", sourceUnitId: "actor", ownerUnitId: "actor", sourceActionId: "future" }, faces: ["Hit"], successes: 1 });
    ui.dice.classList.add("rolling"); busy = true;`);
  await h.elements.get("skip").listeners.click();
  assert.equal(h.elements.get("dice").classList.contains("rolling"), false);
  assert.match(h.elements.get("dice").textContent, /Hit/);
  assert.equal(requests.length, 0);
});

for (const animate of [true, false]) {
  test(`explicit UnitDefeated follows authoritative removal even with positive HP (animate=${animate})`, async () => {
    const initial = response(), final = response();
    final.result.state.physical.figures = final.result.state.physical.figures.filter(figure => figure.id !== "b");
    final.result.events = [{ kind: "UnitDefeated", unitId: "b" }];
    final.presentation.events = [{ role: "Defeat", text: "b was defeated", unitId: "b" }];
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: final.presentation.cards }];
    const h = harness(initial, async () => ({ ok: true, json: async () => final }));
    h.context.animate = animate;
    h.run('ui.animate.checked = animate; pause = async () => {};');
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.equal(h.figure("b"), undefined);
    assert.equal(h.run('displayedState.units.find(unit => unit.id === "b").currentHp'), 4);
    assert.match(h.elements.get("effect").textContent, /Inspected name \(5,0\) was defeated/);
    assert.match(h.elements.get("events").textContent, /b was defeated/);
  });
}


test("compact use circles present supplied remaining and maximum counts accessibly", () => {
  const limited = entry("unknown-limited", "Unfamiliar action");
  limited.uses = { remainingUses: 2, maxUses: 4 };
  const unlimited = entry("unknown-passive", "Unfamiliar passive", "Passive");
  unlimited.uses = null; unlimited.content.useLimitText = null;
  const h = harness(response([], [limited, unlimited]));
  const card = h.elements.get("unit-card");
  const markers = card.querySelectorAll(".use-markers");
  assert.equal(markers.length, 1);
  assert.equal(markers[0].dataset.tooltip, "2 / 4 uses");
  assert.match(markers[0].textContent, /2 \/ 4 uses/);
  const circles = markers[0].querySelectorAll(".use-circle");
  assert.equal(circles.length, 4);
  assert.deepEqual(circles.map(circle => circle.classList.contains("filled")), [true, true, false, false]);
  assert.ok(circles.every(circle => circle.attributes["aria-hidden"] === "true"));
});

test("game shell retains one authoritative target for decisions, field guide and advanced controls", () => {
  const html = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/index.html"), "utf8");
  const tools = html.slice(html.indexOf('<details id="playtest-tools"'), html.indexOf('<main id="playtest-main"'));
  assert.match(tools, /<details id="playtest-tools" class="playtest-tools" hidden>/);
  for (const id of ["scenario", "board-view", "animate", "coordinates", "waypoints", "filter", "auto", "refresh", "commentary-toggle", "decision-commentary"])
    assert.ok(tools.includes(`id="${id}"`), id);
  const header = html.slice(html.indexOf("<header"), html.indexOf("</header>"));
  for (const id of ["mode-designer", "mode-playtest"]) assert.ok(header.includes(`id="${id}"`), `${id} is always accessible`);
  assert.match(html, /class="board-stage camera-viewport"[\s\S]*id="camera-world"[\s\S]*id="board"[\s\S]*class="board-overlays"[\s\S]*id="dice"[\s\S]*id="choices"/);
  assert.match(html, /class="decision-strip"[\s\S]*id="board-hint"[\s\S]*id="end-turn" disabled[\s\S]*id="start-round" disabled[\s\S]*class="board-area"/);
  assert.match(html, /class="card-column"[\s\S]*id="unit-card"[\s\S]*id="inspector-mission"[\s\S]*id="objectives"[\s\S]*id="orders"[\s\S]*id="inspector-journal"[\s\S]*id="events"/);
  for (const id of ["objectives", "orders", "world-effects", "events", "controller-status", "controller-retry", "controller-cancel", "board-hint", "end-turn", "start-round"]) {
    assert.equal(html.split(`id="${id}"`).length - 1, 1, `${id} retains one authoritative rendering target`);
    assert.ok(!tools.includes(`id="${id}"`), `${id} is available without opening Tools`);
  }
  assert.match(html, /class="play-status"[\s\S]*id="status"[\s\S]*id="active-token"[\s\S]*id="bag-count"/);
  assert.match(html, /data-tooltip-source="scenario-description"/);
  const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
  assert.match(css, /100dvh - 126px/);
  assert.match(css, /@media \(max-width: 760px\)/);
  assert.match(css, /pointer-events: none/);
  assert.match(css, /prefers-reduced-motion/);
});


for (const mode of ["animate", "disabled", "skip"]) {
  test(`World playback renders each supplied card and Continuous row in order (${mode})`, async () => {
    const initial = response(), final = response();
    const old = { id: "physical-old", name: "Supplied old", text: "Opaque old modifier text", continuous: true };
    const drawn = { id: "physical-new", name: "Supplied draw", text: "Opaque new modifier text", continuous: true };
    const world = { cardsPerRound: 2, cycling: 1, drawPileCount: 7, discardPileCount: 4, activeContinuous: [old], resolvingCard: null };
    initial.presentation.worldEffects = world;
    const phases = [
      { ...world, drawPileCount: 6, resolvingCard: drawn },
      { ...world, drawPileCount: 6, discardPileCount: 5, activeContinuous: [drawn] }
    ];
    final.presentation.worldEffects = phases[1];
    final.result.events = [{ kind: "WorldCardDrawn" }, { kind: "WorldContinuousChanged" }];
    final.presentation.events = [{ role: "Notice", text: "Supplied World draw" }, { role: "Notice", text: "Supplied atomic replacement" }];
    final.result.resolutionSteps = phases.map((_, eventIndex) => ({ eventIndex, stateAfter: final.result.state }));
    final.presentation.resolutionSteps = phases.map((worldEffects, eventIndex) => ({ eventIndex, cards: final.presentation.cards, worldEffects }));
    const shown = [], paused = [], h = harness(initial, async () => ({ ok: true, json: async () => final }));
    assert.match(h.elements.get("world-effects").textContent, /Supplied old/);
    h.context.shownWorld = shown; h.context.pausedWorld = paused; h.context.mode = mode;
    h.run(`
      ui.animate.checked = mode !== "disabled";
      pause = async () => { pausedWorld.push(ui["world-effects"].textContent); if (mode === "skip") ui.skip.listeners.click(); };
      const originalRenderWorld = renderWorldEffects;
      renderWorldEffects = world => { originalRenderWorld(world); shownWorld.push(ui["world-effects"].textContent); };
    `);
    await h.run('mutate("round")');
    assert.match(shown[0], /Supplied draw/); assert.match(shown[0], /Supplied old/);
    assert.ok(shown.some(value => /Supplied draw/.test(value) && !/Supplied old/.test(value)));
    if (mode !== "disabled") assert.match(paused[0], /Supplied draw/);
    assert.doesNotMatch(h.elements.get("world-effects").textContent, /Supplied old/);
    assert.match(h.elements.get("world-effects").textContent, /Opaque new modifier text/);
    assert.match(h.elements.get("world-effects").textContent, /Discard 5/);
    h.run('renderWorldEffects(null)'); assert.equal(h.elements.get("world-effects").hidden, true);
  });
}

test("World door closure uses the ordinary supplied edge presentation", async () => {
  const h = harness(response());
  const door = { a: { x: 0, y: 0 }, b: { x: 1, y: 0 }, kind: "ClosedDoor" };
  h.context.worldDoor = door;
  h.run('const suppliedEdge = text("div", ""); edges.set(edgeKey(worldDoor), suppliedEdge); pause = async () => {};');
  await h.run('present({ role: "DoorClosed", door: worldDoor }, { kind: "DoorClosed" })');
  assert.equal(h.run('edges.get(edgeKey(worldDoor)).className'), "edge ClosedDoor");
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`edge replacement playback renders supplied board geometry (${mode})`, async () => {
    const initial = response(), final = response();
    const replaced = { a: { x: 0, y: 0 }, b: { x: 1, y: 0 }, kind: "ClosedDoor" };
    const removed = { a: { x: 1, y: 0 }, b: { x: 2, y: 0 }, kind: "OpenDoor" };
    const created = { a: { x: 3, y: 0 }, b: { x: 4, y: 0 }, kind: "Wall" };
    initial.result.state.physical.board.edges = [replaced, removed];
    final.result.state.physical.board.edges = [{ ...replaced, kind: "Wall" }, created];
    final.result.events = [{ kind: "AbilityUsed", unitId: "actor", abilityName: "Reshape Room" }];
    final.presentation.events = [{ role: "Notice", text: "actor used Reshape Room", unitId: "actor" }];
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: final.presentation.cards }];
    const h = harness(initial, async () => ({ ok: true, json: async () => final }));
    h.context.mode = mode; h.context.replaced = replaced; h.context.removed = removed; h.context.created = created;
    h.run(`ui.animate.checked = mode !== "disabled"; pause = async () => {};
      const originalPresent = present;
      present = async (...args) => { await originalPresent(...args); if (mode === "skip") ui.skip.listeners.click(); };`);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.equal(h.run('edges.get(edgeKey(replaced)).className'), "edge Wall");
    assert.equal(h.run('edges.has(edgeKey(removed))'), false);
    assert.equal(h.run('edges.get(edgeKey(created)).className'), "edge Wall");
    assert.match(h.elements.get("events").textContent, /Reshape Room/);
  });
}
// Streaming harness drives actual EventSource reception and native animation timers.
function streamHarness(fetchImpl, extras = {}) {
  const sources = [], timers = new Map(); let nextTimer = 0;
  class FakeEventSource {
    constructor(url) { this.url = url; this.listeners = {}; sources.push(this); }
    addEventListener(name, listener) { this.listeners[name] = listener; }
    close() { this.closed = true; }
    emit(update) { this.listeners.update({ data: JSON.stringify(update) }); }
  }
  const h = harness(response([choice("initial", "Direct")]), fetchImpl, {
    ...extras,
    EventSource: FakeEventSource,
    setTimeout: callback => { const id = ++nextTimer; timers.set(id, callback); return id; },
    clearTimeout: id => timers.delete(id)
  });
  h.run("connectUpdates();");
  return { ...h, sources, timers,
    emit: update => sources.at(-1).emit(update),
    tick: async () => { const timer = timers.entries().next().value; if (timer) { timers.delete(timer[0]); timer[1](); } await settleStream(); }
  };
}
async function settleStream() { for (let i = 0; i < 20; i++) await Promise.resolve(); }
function update(sequence, kind, status = "resolving", view = response([]), sessionId = "session-a", commandId) {
  return { sessionId, sequence, kind, status, revision: view.revision,
    decisionId: status === "waiting-human" ? `decision-${sequence}` : null, snapshot: view, commandId };
}
function movementView(x = 1) {
  const view = response([]);
  view.presentation.decision = null; view.result.nextInput = null;
  view.result.state.physical.figures[0].position.x = x;
  view.result.events = [{ kind: "UnitMoved", unitId: "actor" }];
  view.presentation.events = [{ role: "Movement", text: "Supplied streamed movement", unitId: "actor", path: [{ x: x - 1, y: 0 }, { x, y: 0 }] }];
  view.result.resolutionSteps = [{ eventIndex: 0, stateAfter: view.result.state }];
  view.presentation.resolutionSteps = [{ eventIndex: 0, cards: view.presentation.cards, figures: view.presentation.figures, worldEffects: view.presentation.worldEffects }];
  return view;
}

test("stream receives ahead of playback, drains during external wait, and resumes the same session", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "waiting-human", response([choice("first", "Direct")])));
  h.emit(update(1, "event", "resolving", movementView(1)));
  assert.equal(h.run("playbackRunning"), true);
  assert.equal(h.run("displayedSequence"), 0);
  h.emit(update(2, "event", "resolving", movementView(2)));
  const waiting = response([]); waiting.presentation.decision = null;
  waiting.result.state.physical.figures[0].position.x = 2;
  h.emit(update(3, "boundary", "waiting-external", waiting));
  assert.equal(h.run("receivedSequence"), 3);
  assert.match(h.elements.get("prompt").textContent, /external controller/);
  assert.equal(h.timers.size, 1);
  await h.tick(); await h.tick();
  assert.equal(h.run("displayedSequence"), 3);
  assert.equal(h.run("playbackRunning"), false);
  assert.equal(h.run("displayedState.physical.figures[0].position.x"), 2);
  assert.equal(h.elements.get("events").children.length, 2);
  h.emit(update(4, "event", "resolving", movementView(3)));
  const ready = response([choice("resumed", "Direct")]); ready.result.state.physical.figures[0].position.x = 3;
  h.emit(update(5, "boundary", "waiting-human", ready));
  assert.equal(h.elements.get("choices").children[0]?.disabled ?? true, true);
  await h.tick();
  assert.equal(h.run("displayedSequence"), 5);
  assert.equal(h.elements.get("choices").children[0].disabled, false);
  assert.match(h.elements.get("choices").textContent, /resumed/);
});

test("stream duplicates are ignored and reconnect preserves buffered playback", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "idle"));
  h.emit(update(1, "event", "resolving", movementView()));
  h.emit(update(1, "event", "resolving", movementView()));
  h.sources[0].onerror();
  assert.equal(h.run("streamConnected"), false);
  assert.equal(h.sources.length, 1);
  await h.tick();
  assert.equal(h.run("displayedSequence"), 1);
  assert.equal(h.elements.get("events").children.length, 1);
  h.emit(update(2, "boundary", "waiting-human", response([choice("reconnected", "Direct")])));
  await settleStream();
  assert.equal(h.run("busy"), false);
});

test("gap discards the old drain, requests baseline, and ignores stale source callbacks", async () => {
  const h = streamHarness(); h.emit(update(0, "baseline", "idle"));
  h.emit(update(1, "event", "resolving", movementView()));
  const old = h.sources[0];
  h.emit(update(3, "boundary", "waiting-human"));
  assert.equal(h.sources.length, 2);
  assert.equal(old.closed, true);
  assert.equal(h.run("playbackRunning"), false);
  assert.equal(h.timers.size, 0);
  old.emit(update(2, "boundary", "waiting-human"));
  assert.equal(h.run("receivedSequence"), 1);
  const fresh = response([choice("fresh", "Direct")]); fresh.result.state.physical.figures[0].position.x = 4;
  h.emit(update(3, "baseline", "waiting-human", fresh));
  await settleStream();
  assert.equal(h.run("displayedState.physical.figures[0].position.x"), 4);
  h.emit(update(0, "baseline", "idle"));
  assert.equal(h.run("receivedSequence"), 3);
});

for (const withCamera of [false, true]) test(`session reset cancels native animation and old state cannot repaint (camera=${withCamera})`, async () => {
  const h = streamHarness(undefined, { withCamera });
  const initial = response([]); initial.sessionId = "session-a";
  h.emit(update(0, "baseline", "idle", initial));
  if (withCamera) h.run('boardCamera.zoom(1.2)');
  const moving = movementView(); moving.sessionId = "session-a";
  h.emit(update(1, "event", "resolving", moving));
  const fresh = response([]); fresh.sessionId = "session-b"; fresh.result.state.physical.figures[0].position.x = 4;
  h.emit(update(0, "baseline", "idle", fresh, "session-b"));
  await settleStream();
  assert.equal(h.run("latestUpdate.sessionId"), "session-b");
  assert.equal(h.run("displayedState.physical.figures[0].position.x"), 4);
  assert.equal(h.elements.get("events").children.length, 0);
  assert.equal(h.timers.size, 0);
  if (withCamera) {
    assert.equal(h.run('boardCamera.inspect().suspended'), false);
    assert.equal(h.run('boardCamera.inspect().transitionActive'), false);
  }
});

test("fault settles immediately on the supplied authoritative recovery snapshot", async () => {
  const h = streamHarness(); h.emit(update(0, "baseline", "idle"));
  h.emit(update(1, "event", "resolving", movementView()));
  const fault = update(2, "fault", "faulted", response([])); fault.error = "Controller failed";
  h.emit(fault); await settleStream();
  assert.equal(h.run("displayedState.physical.figures[0].position.x"), 0);
  assert.equal(h.run("displayedSequence"), 2);
  assert.equal(h.timers.size, 0);
  assert.match(h.elements.get("error").textContent, /Controller failed/);
  assert.equal(h.run("busy"), true);
});

test("command acknowledgements do not render; decision IDs and uncertain outcomes never retry", async () => {
  const requests = [];
  const h = streamHarness(async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    return { ok: true, json: async () => ({ accepted: true }) };
  });
  h.emit(update(0, "baseline", "waiting-human", response([choice("opaque", "Direct")])));
  const before = h.run("displayedState");
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.equal(requests[0].url, "/api/game/commands");
  assert.equal(requests[0].body.sessionId, "session-a");
  assert.equal(requests[0].body.decisionId, "decision-0");
  assert.equal(requests[0].body.expectedRevision, 8);
  assert.equal(requests[0].body.candidateKey, "opaque");
  assert.equal(h.run("displayedState"), before);
  assert.equal(h.run("busy"), true);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.equal(requests.length, 1);
  h.emit(update(1, "boundary", "waiting-human", response([choice("next", "Direct")]), "session-a", requests[0].body.commandId));
  await settleStream();
  assert.equal(h.run("busy"), false);

  const failedRequests = [];
  const failed = streamHarness(async (url, options) => {
    failedRequests.push(url); throw new Error("Unknown POST outcome");
  });
  failed.emit(update(0, "baseline", "waiting-human", response([choice("opaque", "Direct")])));
  assert.equal(await failed.run('mutate("decision", { candidateKey: "opaque" })'), false);
  assert.deepEqual(failedRequests, ["/api/game/commands"]);
  assert.equal(failed.sources.length, 2);
  failed.emit(update(1, "baseline", "waiting-human", response([choice("recovered", "Direct")])));
  assert.equal(failed.run("busy"), false);
  assert.match(failed.elements.get("choices").textContent, /recovered/);
});

test("bounded queue resynchronizes instead of stalling game progression", async () => {
  const h = streamHarness(); h.emit(update(0, "baseline", "idle"));
  h.emit(update(1, "event", "resolving", movementView()));
  for (let i = 2; i <= 258; i++) h.emit(update(i, "event", "resolving", movementView()));
  assert.equal(h.sources.length, 2);
  assert.equal(h.run("playbackQueue.length"), 0);
  assert.equal(h.timers.size, 0);
  h.emit(update(258, "baseline", "waiting-external", response([])));
  await settleStream();
  assert.equal(h.run("displayedSequence"), 258);
});


test("reset queues only until POST acknowledgement and designer waits for replacement baseline", async () => {
  const requests = []; let release;
  const h = streamHarness(async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) });
    if (requests.length === 1) await new Promise(resolve => { release = resolve; });
    return { ok: true, json: async () => ({ accepted: true }) };
  });
  h.emit(update(0, "baseline", "waiting-human", response([choice("opaque", "Direct")])));
  const decision = h.run('mutate("decision", { candidateKey: "opaque" })');
  const designed = h.run('globalThis.delvePlayDesign("supplied transport")');
  assert.equal(requests.length, 1);
  h.emit(update(1, "event", "resolving", movementView()));
  release(); await decision; await settleStream();
  assert.equal(requests.length, 2);
  assert.equal(requests[1].body.operation, "scenario/import");
  assert.equal(requests[1].body.expectedRevision, 8);
  assert.equal(requests[1].body.sessionId, "session-a");
  let completed = false; designed.then(() => { completed = true; }); await settleStream();
  assert.equal(completed, false);
  const replaced = response([]); replaced.result.state.physical.figures[0].position.x = 4;
  h.emit(update(0, "baseline", "idle", replaced, "session-b", requests[1].body.commandId));
  assert.equal(await designed, true);
  assert.equal(h.run("displayedState.physical.figures[0].position.x"), 4);
  assert.equal(h.timers.size, 0);
});


test("skip persists across late incremental batches until boundary without sending requests", async () => {
  const requests = [];
  const h = streamHarness(async url => { requests.push(url); return { ok: true, json: async () => ({ accepted: true }) }; });
  h.emit(update(0, "baseline", "idle"));
  h.emit(update(1, "event", "resolving", movementView()));
  h.elements.get("skip").listeners.click(); await settleStream();
  assert.equal(h.run("displayedSequence"), 1);
  h.emit(update(2, "event", "resolving", movementView(2))); await settleStream();
  assert.equal(h.run("displayedSequence"), 2);
  assert.equal(h.timers.size, 0);
  h.emit(update(3, "boundary", "waiting-external", response([]))); await settleStream();
  h.emit(update(4, "event", "resolving", movementView(3)));
  assert.equal(h.timers.size, 1);
  assert.deepEqual(requests, []);
  h.elements.get("skip").listeners.click(); await settleStream();
});


test("same-cursor SSE reconnect clears its connection notice without clearing unrelated errors", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "waiting-human", response([choice("current", "Direct")])));
  const source = h.sources[0]; source.onerror();
  assert.match(h.elements.get("error").textContent, /Connection interrupted/);
  assert.equal(h.run("busy"), true);
  source.listeners.open(); // Server has no newer update to replay.
  assert.equal(h.elements.get("error").textContent, "");
  assert.equal(h.run("busy"), false);
  assert.equal(h.run("receivedSequence"), 0);
  assert.equal(h.sources.length, 1);
  h.elements.get("error").textContent = "Supplied validation error";
  source.listeners.open();
  assert.equal(h.elements.get("error").textContent, "Supplied validation error");
  const fault = update(1, "fault", "faulted", response([])); fault.error = "Supplied controller fault";
  h.emit(fault); source.listeners.open();
  assert.equal(h.elements.get("error").textContent, "Supplied controller fault");
});

test("replacement survives same-session recovery baseline and resolves false for fault recovery", async () => {
  const requests = [];
  const h = streamHarness(async (url, options) => {
    requests.push(JSON.parse(options.body)); return { ok: true, json: async () => ({ accepted: true }) };
  });
  h.emit(update(0, "baseline", "idle"));
  const replacement = h.run('globalThis.delvePlayDesign("supplied transport")');
  await settleStream();
  let complete = false; replacement.then(() => { complete = true; });
  h.emit(update(1, "baseline", "resolving", response([]), "session-a", "older-command"));
  await settleStream();
  assert.equal(complete, false);
  const failedBaseline = update(2, "baseline", "faulted", response([]), "session-a", requests[0].commandId);
  failedBaseline.error = "Reset failed";
  h.emit(failedBaseline);
  assert.equal(await replacement, false);
  assert.equal(h.run("pendingReplacement"), null);
  assert.match(h.elements.get("error").textContent, /Reset failed/);
});

test("uncertain replacement POST resolves safely and recovers without retry", async () => {
  const requests = [];
  const h = streamHarness(async (url, options) => { requests.push(url); throw new Error("Unknown reset outcome"); });
  h.emit(update(0, "baseline", "idle"));
  assert.equal(await h.run('globalThis.delvePlayDesign("supplied transport")'), false);
  assert.equal(h.run("pendingReplacement"), null);
  assert.equal(h.run("postInFlight"), false);
  assert.deepEqual(requests, ["/api/game/commands"]);
  h.emit(update(0, "baseline", "idle", response([]), "session-b"));
  await settleStream();
  assert.equal(h.run("busy"), false);
  assert.equal(h.run("latestUpdate.sessionId"), "session-b");
  assert.deepEqual(requests, ["/api/game/commands"]);
});


const decisionComment = (text, eventIndex = 0) => ({ decisionId: "decision-comment", unitId: "actor",
  controller: "External", sideId: "amber", decisionKind: "ChooseAction", candidateKey: "opaque", text, eventIndex });
const commentaryUpdate = (sequence, text, view = response()) => ({
  ...update(sequence, "commentary", "resolving", view), commentary: decisionComment(text)
});

test("commentary defaults hidden and streams at its playback cue, never network reception", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "waiting-human"));
  assert.equal(h.elements.get("commentary-panel").hidden, true);
  assert.equal(h.elements.get("commentary-toggle").attributes["aria-expanded"], "false");
  h.emit(commentaryUpdate(1, "Already played"));
  h.emit(update(2, "event", "resolving", movementView(1)));
  const laterView = movementView(2);
  laterView.commentaries = [decisionComment("Must not duplicate streaming cue")];
  h.emit(commentaryUpdate(3, "Future intention"));
  h.emit(update(4, "event", "resolving", laterView));
  h.emit(update(5, "boundary", "waiting-human"));
  assert.equal(h.run("receivedSequence"), 5);
  assert.equal(h.run("displayedSequence"), 1);
  h.elements.get("commentary-toggle").listeners.click();
  assert.equal(h.elements.get("commentary-panel").hidden, false);
  assert.equal(h.elements.get("commentary-text").textContent, "Already played");
  assert.match(h.elements.get("commentary-context").textContent, /actor.*External.*amber.*Choose Action/);
  await h.tick();
  assert.equal(h.elements.get("commentary-text").textContent, "Future intention");
  await h.tick(); await settleStream();
  assert.equal(h.run("displayedSequence"), 5);
  assert.equal(h.elements.get("commentary-text").textContent, "Future intention");
  assert.equal(h.run("displayedCommentary.candidateKey"), "opaque");
  h.elements.get("commentary-toggle").listeners.click();
  assert.equal(h.elements.get("commentary-panel").hidden, true);
});

for (const mode of ["animate", "disabled", "skip"]) {
  test(`legacy commentary is ordered before events and at trailing zero-event decisions (${mode})`, async () => {
    const final = movementView();
    final.result.events.push({ kind: "NoOp" });
    final.presentation.events.push({ role: "Notice", text: "Second event" });
    final.commentaries = [decisionComment("First", 0), decisionComment("Second", 1), decisionComment("Trailing", 2)];
    const seen = [], h = harness(response(), async () => ({ ok: true, json: async () => final }));
    h.context.mode = mode; h.context.seenComments = seen;
    h.run(`ui.animate.checked = mode !== "disabled"; pause = async () => {};
      const normalAppend = ui.events.append.bind(ui.events);
      ui.events.append = node => { seenComments.push(displayedCommentary?.text); normalAppend(node); };
      const normalPresent = present;
      present = async (...args) => { await normalPresent(...args); if (mode === "skip") ui.skip.listeners.click(); };`);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.deepEqual(seen, ["First", "Second"]);
    assert.equal(h.elements.get("commentary-text").textContent, "Trailing");
    assert.equal(h.figure("actor").style.left, "25%");
    assert.equal(h.elements.get("commentary-panel").hidden, true);
    const empty = response(); empty.commentaries = [decisionComment("No resulting events")];
    h.context.emptyCommentView = empty;
    await h.run("playEvents(emptyCommentView)");
    assert.equal(h.elements.get("commentary-text").textContent, "No resulting events");
    await h.run("playEvents(initial)"); // Older responses omit commentary entirely.
    assert.equal(h.elements.get("commentary-text").textContent, "No resulting events");
  });
}

test("baseline and fault clear stale or queued commentary without resetting spectator visibility", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "waiting-human"));
  h.elements.get("commentary-toggle").listeners.click();
  h.emit(commentaryUpdate(1, "Old intention"));
  h.emit(update(2, "event", "resolving", movementView()));
  h.emit(commentaryUpdate(3, "Cancelled intention"));
  h.run('ui["decision-commentary"].value = "Old draft"');
  h.emit(update(0, "baseline", "waiting-human", response(), "session-b"));
  await h.tick();
  assert.equal(h.elements.get("commentary-panel").hidden, false);
  assert.equal(h.run("displayedCommentary"), undefined);
  assert.equal(h.elements.get("decision-commentary").value, "");
  assert.doesNotMatch(h.elements.get("commentary-text").textContent, /intention/);
  h.emit({ ...commentaryUpdate(1, "New intention"), sessionId: "session-b" });
  assert.equal(h.elements.get("commentary-text").textContent, "New intention");
  h.emit(update(2, "fault", "faulted", response(), "session-b"));
  assert.equal(h.run("displayedCommentary"), undefined);
  assert.equal(h.elements.get("commentary-panel").hidden, false);
});

test("commentary renders untrusted controller text as plain text", () => {
  const h = harness(response());
  h.context.untrustedComment = decisionComment('<img src=x onerror="alert(1)">');
  h.run("showCommentary(untrustedComment)");
  assert.equal(h.elements.get("commentary-text").textContent, '<img src=x onerror="alert(1)">');
  assert.equal(h.elements.get("commentary-text").children.length, 0);
  assert.equal(h.elements.get("commentary-panel").hidden, true);
  assert.doesNotMatch(h.elements.get("commentary-toggle").textContent, /alert/);
});

for (const transport of ["legacy", "stream"]) {
  test(`optional human commentary preserves selected candidate and submits only on decision (${transport})`, async () => {
    const requests = [];
    const fetchImpl = async (url, options) => { requests.push({ url, body: JSON.parse(options.body) });
      return { ok: true, json: async () => transport === "stream" ? { accepted: true } : response() }; };
    const h = transport === "stream" ? streamHarness(fetchImpl) : harness(response(), fetchImpl);
    if (transport === "stream") h.emit(update(0, "baseline", "waiting-human"));
    h.run('ui.animate.checked = false; ui["decision-commentary"].value = "  My intention  "');
    assert.equal(await h.run('chooseCandidate("opaque-a")'), true);
    assert.equal(requests[0].body.candidateKey, "opaque-a");
    assert.equal(requests[0].body.commentary, "My intention");
    assert.equal(h.elements.get("decision-commentary").value, "");
    if (transport === "stream") h.emit(update(1, "boundary", "waiting-human", response(), "session-a", requests[0].body.commandId));
    h.run('ui["decision-commentary"].value = "  "');
    assert.equal(await h.run('chooseCandidate("opaque-b")'), true);
    assert.equal(requests[1].body.candidateKey, "opaque-b");
    assert.equal(Object.hasOwn(requests[1].body, "commentary"), false);
    if (transport === "stream") h.emit(update(2, "boundary", "waiting-human", response(), "session-a", requests[1].body.commandId));
    h.run('ui["decision-commentary"].value = "Draft for later"');
    await h.run('mutate("preferences", { autoChooseSingleRelevantChoice: false })');
    assert.equal(Object.hasOwn(requests[2].body, "commentary"), false);
    assert.equal(h.elements.get("decision-commentary").value, "Draft for later");
  });
}


for (const mode of ["disabled", "skip"]) {
  test(`stream commentary retains sequence and candidate context when effects are ${mode}`, async () => {
    const h = streamHarness();
    h.emit(update(0, "baseline", "waiting-human"));
    h.run(`ui.animate.checked = ${mode !== "disabled"}`);
    h.emit(commentaryUpdate(1, "Before movement"));
    h.emit(update(2, "event", "resolving", movementView()));
    h.emit(commentaryUpdate(3, "After movement", movementView()));
    h.emit(update(4, "boundary", "waiting-human", movementView()));
    if (mode === "skip") h.elements.get("skip").listeners.click();
    await settleStream();
    assert.equal(h.run("displayedSequence"), 4);
    assert.equal(h.elements.get("commentary-text").textContent, "After movement");
    assert.equal(h.figure("actor").style.left, "25%");
    assert.equal(h.run("displayedCommentary.decisionId"), "decision-comment");
    assert.equal(h.elements.get("commentary-panel").hidden, true);
  });
}

for (const mode of ["animate", "disabled", "skip"]) {
  test(`objective results appear only at authoritative completion playback (${mode})`, async () => {
    const initial = response(), final = response();
    initial.presentation.objectives = final.presentation.objectives = [
      { sideId: "red", name: "Eliminate Enemies" }, { sideId: "blue", name: "Eliminate Enemies" }
    ];
    const before = state(), after = state();
    after.completion = { achievedSideIds: ["red", "blue"] };
    after.currentUnitId = null; after.activeToken = null;
    final.result.state = after; final.presentation.decision = null;
    final.result.nextInput = null;
    final.result.events = [{ kind: "UnitDefeated" }, { kind: "GameCompleted", completion: after.completion }];
    final.presentation.events = [{ role: "Defeat", text: "Defeated" }, { role: "Notice", text: "Game over" }];
    final.result.resolutionSteps = [before, after].map((stateAfter, eventIndex) => ({ eventIndex, stateAfter }));
    final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
    const h = harness(initial, async () => ({ ok: true, json: async () => final })), shown = [];
    h.context.mode = mode; h.context.shown = shown;
    h.run(`ui.animate.checked = mode !== "disabled"; pause = async () => {};
      const renderOriginal = renderState;
      renderState = (...args) => { renderOriginal(...args); shown.push(ui["game-result"].hidden); };
      const presentOriginal = present;
      present = async (...args) => { await presentOriginal(...args); if (mode === "skip") ui.skip.listeners.click(); };`);
    assert.match(h.elements.get("objectives").textContent, /red: Eliminate Enemies.*blue: Eliminate Enemies/);
    assert.equal(h.elements.get("game-result").hidden, true);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.deepEqual(shown, [true, false, false]);
    assert.match(h.elements.get("game-result").textContent, /Simultaneous victory.*red, blue.*each achieved/);
    assert.match(h.elements.get("objectives").textContent, /red:.*Achieved.*blue:.*Achieved/);
    assert.equal(h.elements.get("round").disabled, true);
    assert.match(h.elements.get("prompt").textContent, /Game over/);
    assert.equal(h.elements.get("restart-scenario").disabled, false);
  });
}

test("streamed completion waits for playback and leaves refresh and restart available", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "waiting-human"));
  h.emit(update(1, "event", "resolving", movementView(1)));
  const completed = response([]);
  completed.presentation.decision = null; completed.result.nextInput = null;
  completed.result.state.completion = { achievedSideIds: ["purple"] };
  completed.presentation.objectives = [{ sideId: "purple", name: "Eliminate Enemies" },
    { sideId: "gold", name: "Eliminate Enemies" }];
  completed.result.events = [{ kind: "GameCompleted", completion: completed.result.state.completion }];
  completed.presentation.events = [{ role: "Notice", text: "Game over" }];
  completed.result.resolutionSteps = [{ eventIndex: 0, stateAfter: completed.result.state }];
  completed.presentation.resolutionSteps = [{ eventIndex: 0, cards: completed.presentation.cards }];
  h.emit(update(2, "event", "resolving", completed));
  h.emit(update(3, "boundary", "completed", completed));
  assert.equal(h.run("receivedSequence"), 3);
  assert.equal(h.elements.get("game-result").hidden, true);
  while (h.timers.size || h.run("playbackRunning")) await h.tick();
  assert.equal(h.run("displayedSequence"), 3);
  assert.match(h.elements.get("game-result").textContent, /Victory: purple/);
  assert.match(h.elements.get("objectives").textContent, /gold: Eliminate Enemies\s+- Not achieved/);
  assert.equal(h.elements.get("round").disabled, true);
  assert.equal(h.elements.get("refresh").disabled, false);
  assert.equal(h.elements.get("restart-scenario").disabled, false);
});


test("setup sends controller bindings for actual arbitrary Sides and allows reversed Gambit assignment", async () => {
  for (const gambitSide of ["violet-side", "copper-side"]) {
    const initial = response(), requests = [];
    initial.sessionId = "match-setup"; initial.scenarioId = "custom";
    initial.scenarios = [{ id: "custom", name: "Custom", sideIds: ["violet-side", "copper-side"] }];
    initial.controllers = { "violet-side": "Human", "copper-side": "Default Behavior" };
    const h = harness(initial, async (url, options) => {
      requests.push({ url, body: JSON.parse(options.body) }); return { ok: true, json: async () => initial };
    });
    h.context.gambitSide = gambitSide;
    h.run('controllerSelects.get(gambitSide).value = "Gambit"; controllerSelects.get(gambitSide).listeners.change();');
    await h.elements.get("start-scenario").listeners.click();
    assert.equal(requests[0].body.controllers[gambitSide], "Gambit");
    const otherSide = gambitSide === "violet-side" ? "copper-side" : "violet-side";
    assert.equal(requests[0].body.controllers[otherSide], initial.controllers[otherSide]);
    assert.equal(requests[0].body.scenarioId, "custom");
    assert.match(h.elements.get("side-controllers").textContent, /Default Behavior/);
    h.context.otherSide = otherSide;
    h.run('controllerSelects.get(otherSide).value = "Gambit"; controllerSelects.get(otherSide).listeners.change();');
    assert.equal(h.run("controllerSelects.get(gambitSide).value"), "Human");
    assert.equal(h.run("controllerSelects.get(otherSide).value"), "Gambit");
  }
});

test("per-Type Side controller selectors support Side defaults and mixed Gambit allies", async () => {
  const initial = response(), requests = [];
  initial.sessionId = "selective-session"; initial.scenarioId = "selective";
  initial.controllerAssignments = [
    { typeId: "barbarian", sideId: "blue", controller: "Human" },
    { typeId: "shaman", sideId: "red", controller: "Default Behavior" },
    { typeId: "goblin", sideId: "red", controller: "Default Behavior" }
  ];
  initial.scenarios = [{ id: "selective", name: "Selective", controllerAssignments: initial.controllerAssignments }];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) }); return { ok: true, json: async () => initial };
  });
  h.run('controllerSideDefaults.get("red").value = "Gambit"; controllerSideDefaults.get("red").listeners.change();');
  h.run('controllerSelects.get("goblin\\0red").value = "Default Behavior"; controllerSelects.get("goblin\\0red").listeners.change();');
  await h.elements.get("start-scenario").listeners.click();
  const assignments = requests[0].body.controllerAssignments;
  assert.equal(assignments.find(value => value.typeId === "shaman" && value.sideId === "red").controller, "Gambit");
  assert.equal(assignments.find(value => value.typeId === "goblin" && value.sideId === "red").controller, "Default Behavior");
  assert.equal(assignments.find(value => value.typeId === "barbarian" && value.sideId === "blue").controller, "Human");
  assert.match(h.elements.get("side-controllers").textContent, /All Unit Types/);
});
test("Gambit wait preserves playback and enables Cancel; error enables explicit Retry without choosing", async () => {
  const requests = [], h = streamHarness(async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) }); return { ok: true, json: async () => ({}) };
  });
  const waiting = response([]);
  waiting.controllers = { red: "Gambit", blue: "Human" };
  waiting.controllerStatus = { sideId: "red", status: "waiting-external", available: true };
  h.emit(update(0, "baseline", "waiting-external", waiting));
  assert.equal(h.elements.get("auto").disabled, true);
  assert.equal(h.elements.get("controller-cancel").disabled, false);
  assert.equal(h.elements.get("controller-retry").disabled, true);
  assert.equal(h.elements.get("round").disabled, true);
  await h.elements.get("controller-cancel").listeners.click();
  assert.equal(requests[0].body.operation, "controller/cancel");
  assert.equal(requests[0].body.candidateKey, undefined);
  const failed = response([]);
  failed.controllers = waiting.controllers;
  failed.controllerStatus = { sideId: "red", status: "controller-error", available: false, error: "Resident unavailable" };
  const boundary = update(1, "boundary", "controller-error", failed);
  boundary.commandId = requests[0].body.commandId;
  h.emit(boundary); await settleStream();
  assert.match(h.elements.get("controller-status").textContent, /unavailable/);
  assert.equal(h.elements.get("controller-retry").disabled, false);
  await h.elements.get("controller-retry").listeners.click();
  assert.equal(requests[1].body.operation, "controller/retry");
  assert.equal(requests[1].body.candidateKey, undefined);
});

test("authoritative completion disables controller operations before victory playback", async () => {
  const h = streamHarness(), initial = response([]);
  initial.controllerStatus = { sideId: "red", status: "waiting-external", available: true };
  h.emit(update(0, "baseline", "waiting-external", initial));
  const final = movementView(1);
  final.result.state.completion = { achievedSideIds: ["red"] };
  final.controllerStatus = initial.controllerStatus;
  h.emit(update(1, "event", "resolving", final));
  assert.equal(h.elements.get("controller-cancel").disabled, true);
  assert.equal(h.elements.get("controller-retry").disabled, true);
  assert.equal(h.elements.get("round").disabled, true);
});

test("ordinary browser stream shows all players commentary with one toggle and no owner workflow", async () => {
  const h = streamHarness();
  h.emit(update(0, "baseline", "waiting-human", response([])));
  assert.equal(h.sources[0].url, "/api/game/updates");
  assert.equal(h.elements.get("commentary-panel").hidden, true);
  assert.equal(h.elements.get("commentary-toggle").textContent, "Show decision commentary");
  h.elements.get("commentary-toggle").listeners.click();
  for (const [sequence, sideId, controller, text] of [[1, "amber", "Automated", "Gambit choice"], [2, "violet", "Human", "Human choice"]]) {
    const cue = commentaryUpdate(sequence, text);
    cue.commentary.sideId = sideId; cue.commentary.controller = controller;
    h.emit(cue); await settleStream();
    assert.equal(h.elements.get("commentary-text").textContent, text);
    assert.match(h.elements.get("commentary-context").textContent, new RegExp(sideId));
  }
  assert.equal(h.elements.get("commentary-panel").hidden, false);
  assert.equal(h.sources.length, 1);
});


test("imported scenario exposes its actual Side controller assignments for restart", async () => {
  const initial = response();
  initial.sessionId = "imported-session"; initial.scenarioId = "imported";
  initial.controllers = { "side-with-wizard": "Human", "side-with-barbarian": "Default Behavior" };
  initial.scenarios = [{ id: "built-in", name: "Built in", sideIds: ["blue", "red"] }];
  const requests = [], h = harness(initial, async (url, options) => {
    requests.push(JSON.parse(options.body)); return { ok: true, json: async () => initial };
  });
  h.run('controllerSelects.get("side-with-wizard").value = "Gambit"; controllerSelects.get("side-with-wizard").listeners.change();');
  await h.elements.get("restart-scenario").listeners.click();
  assert.deepEqual(requests[0].controllers, { "side-with-wizard": "Gambit", "side-with-barbarian": "Default Behavior" });
});


test("server operation availability disables Retry for restart-only controller errors", () => {
  const h = streamHarness(), failed = response([]);
  failed.controllerStatus = { sideId: "red", status: "controller-error", error: "Observation limit exceeded. Restart the match.", canRetry: false, canCancel: false };
  h.emit(update(0, "baseline", "controller-error", failed));
  assert.equal(h.elements.get("controller-retry").disabled, true);
  assert.equal(h.elements.get("controller-cancel").disabled, true);
  assert.equal(h.elements.get("restart-scenario").disabled, false);
});

test("standing Orders render projected labels and playback values without an editor", () => {
  const initial = response();
  initial.result.state.types = [
    ...initial.result.state.types,
    { ...initial.result.state.types[0], id: "grunt-type", displayName: "Grunt" },
    { ...initial.result.state.types[0], id: "skeleton-archer-type", displayName: "Skeleton Archer" },
    { ...initial.result.state.types[0], id: "zombie-type", displayName: "Zombie" },
    { ...initial.result.state.types[0], id: "goblin-type", displayName: "Goblin" },
    { ...initial.result.state.types[0], id: "rogue-type", displayName: "Rogue" }
  ];
  initial.presentation.cards["hero-alpha"] = { displayName: "Castle Guard", entries: [] };
  initial.presentation.cards["escort-leader"] = { displayName: "Escort Leader", entries: [] };
  initial.presentation.orders = [
    { token: { typeId: "grunt-type", sideId: "red" }, kind: "DoNothing", destination: null },
    { token: { typeId: "skeleton-archer-type", sideId: "red" }, kind: "HoldPosition", destination: null },
    { token: { typeId: "zombie-type", sideId: "red" }, kind: "MoveTo", destination: { x: 5, y: 9 } },
    { token: { typeId: "grunt-type", sideId: "red" }, kind: "Attack", target: { kind: "Unit", unitId: "hero-alpha" } },
    { token: { typeId: "skeleton-archer-type", sideId: "red" }, kind: "Attack", target: { kind: "Group", group: { typeId: "rogue-type", sideId: "blue" } } },
    { token: { typeId: "goblin-type", sideId: "red" }, kind: "Attack", target: { kind: "Side", sideId: "blue" } },
    { token: { typeId: "zombie-type", sideId: "red" }, kind: "Protect", target: { kind: "Unit", unitId: "escort-leader" } },
    { token: { typeId: "goblin-type", sideId: "red" }, kind: "Retreat", target: null },
    { token: { typeId: "zombie-type", sideId: "red" }, kind: "InteractWith", target: { kind: "Door", door: { a: { x: 5, y: 4 }, b: { x: 6, y: 4 } } } }
  ];
  const h = harness(initial);
  assert.equal(h.elements.get("orders").hidden, false);
  assert.match(h.elements.get("orders").textContent,
    /Standing Orders.*Grunt · red: Do Nothing.*Skeleton Archer · red: Hold Position.*Zombie · red: Move To \(5,9\)/);
  assert.match(h.elements.get("orders").textContent, /Grunt .* red: Attack .* Castle Guard/);
  assert.match(h.elements.get("orders").textContent, /Skeleton Archer .* red: Attack .* Rogue .* blue/);
  assert.match(h.elements.get("orders").textContent, /Goblin .* red: Attack .* Side blue/);
  assert.match(h.elements.get("orders").textContent, /Zombie .* red: Protect .* Escort Leader/);
  assert.match(h.elements.get("orders").textContent, /Goblin .* red: Retreat/);
  assert.match(h.elements.get("orders").textContent, /Zombie .* red: Interact With .* Door \(5,4\).\(6,4\)/);
  assert.equal(h.elements.get("orders").querySelectorAll("button").length, 0);

  h.context.playbackOrders = [{ token: { typeId: "zombie-type", sideId: "red" },
    kind: "MoveTo", destination: { x: 4, y: 8 } }];
  h.run("renderState(snapshot.result.state, true, snapshot.presentation.cards, snapshot.presentation.figures, snapshot.presentation.worldEffects, playbackOrders)");
  assert.match(h.elements.get("orders").textContent, /Zombie · red: Move To \(4,8\)/);
});

test("a Unit card shows the standing Order for its Unit Type and Side", () => {
  const initial = response();
  initial.presentation.cards["target-alpha"] = { displayName: "Target Scout", entries: [] };
  initial.presentation.orders = [{ token: { typeId: "opaque-type", sideId: "same" },
    kind: "Attack", target: { kind: "Unit", unitId: "target-alpha" } }];
  const h = harness(initial);
  assert.match(h.elements.get("unit-card").textContent, /Standing Order.*Attack .* Target Scout/);

  initial.presentation.orders[0] = { token: { typeId: "opaque-type", sideId: "other" }, kind: "Retreat" };
  h.run("renderState(snapshot.result.state)");
  assert.doesNotMatch(h.elements.get("unit-card").textContent, /Standing Order|Retreat/);
});

test("game setup is expanded before play and collapses when play begins", () => {
  const initial = response();
  initial.result.state.round = 0;
  const h = harness(initial);
  assert.equal(h.elements.get("game-setup").open, true);
  initial.result.state.round = 1;
  h.run("renderState(snapshot.result.state)");
  assert.equal(h.elements.get("game-setup").open, false);
});

test("board coordinates start hidden and follow the display checkbox", () => {
  const h = harness(response());
  assert.equal(h.elements.get("board").classList.contains("hide-coordinates"), true);
  h.elements.get("coordinates").checked = true;
  h.elements.get("coordinates").listeners.change();
  assert.equal(h.elements.get("board").classList.contains("hide-coordinates"), false);
});

test("objective panel renders authoritative count, selector and area meanings", () => {
  const initial = response([]);
  initial.presentation.objectives = [
    { sideId: "side-1", name: "Reach Area", meaning: "All fixed initial Blue Units: simultaneously inside (13, 5), 2 x 5, all Cells of all figures." },
    { sideId: "side-2", name: "Defeat Units", meaning: "At least 1 fixed initial Blue Unit must actually be defeated." }
  ];
  const h = harness(initial);
  const panel = h.elements.get("objectives").textContent;
  assert.match(panel, /Reach Area.*All fixed initial Blue Units.*simultaneously.*\(13, 5\), 2 x 5.*all Cells/);
  assert.match(panel, /Defeat Units.*At least 1.*actually be defeated/);
});
test("authored Blue and Red colors apply before figures on fresh start and scenario switch", () => {
  for (const switched of [false, true]) {
    const initial = response([]);
    const h = harness(initial);
    h.context.authored = { id: "fixture-colors", sideHues: { "side-1": 210, "side-2": 0 } };
    h.run(`
      if (${switched}) { styleSide(ui.status, "blue"); styleSide(ui.status, "red"); }
      snapshot.scenarioId = "fixture-colors"; snapshot.scenarios = [authored];
      snapshot.result.state.units[0].sideId = "side-2";
      snapshot.result.state.units[1].sideId = "side-1";
      renderState(snapshot.result.state);
    `);
    assert.equal(h.figure("actor").style["--side-hue"], 0);
    assert.equal(h.figure("b").style["--side-hue"], 210);
  }
});

test("Resources render by authoritative Type and Cell, including under Units, and update without client transport rules", () => {
  const initial = response();
  initial.result.state.resources = [{ type: { id: "coal", name: "Coal" }, position: { x: 0, y: 0 } },
    { type: { id: "other", name: "Other Resource" }, position: { x: 2, y: 0 } }];
  const h = harness(initial);
  const markers = x => h.run(`cells.get("${x},0").querySelectorAll(".resource-marker")`);
  assert.equal(markers(0)[0].attributes["aria-label"], "Coal");
  assert.equal(markers(2)[0].title, "Other Resource");
  h.run('snapshot.result.state.resources[0].position = { x: 3, y: 0 }; renderSnapshot();');
  assert.equal(markers(0).length, 0);
  assert.equal(markers(3)[0].title, "Coal");
  assert.equal(markers(2).length, 1);
  assert.equal(h.figure("actor").style.left, `${.5 / 6 * 100}%`);
});

test("Machine and Resource artwork keeps labels, choices and glyph fallbacks through loads and rerenders", async () => {
  const initial = response([choice("operate-fixture", "Position", { position: { x: 2, y: 0 } })]);
  initial.result.state.physical.board.terrain = [{ position: { x: 2, y: 0 }, kind: "Grass" }];
  initial.result.state.resources = ["coal", "diamond", "fixture-resource"].map((id, x) => ({
    type: { id, name: id }, position: { x, y: 0 }
  }));
  initial.presentation.machines = [{ id: "fixture-press", typeId: "diamond-press", name: "Fixture Press",
    position: { x: 2, y: 0 }, ownerSide: "amber", operation: "Supplied operation",
    resourceStorage: [{ name: "Coal", quantity: 2, capacity: 3 }] }];
  const submitted = [];
  const h = harness(initial, async (url, options) => {
    submitted.push(JSON.parse(options.body)); return { ok: true, json: async () => initial };
  });
  const press = h.run('cells.get("2,0")');
  const art = press.querySelectorAll(".machine-art")[0];
  assert.equal(press.dataset.visualId, "StoneFloor");
  assert.match(press.style["--visual-image"], /stone-floor/);
  assert.equal(press.classList.contains("Grass"), true); // Classic retains its terrain class.
  assert.equal(art.objectArtwork.src, "./art/machines/diamond-press.webp");
  assert.equal(art.dataset.artwork, "loading");
  art.objectArtwork.listeners.load();
  assert.equal(art.dataset.artwork, "ready");
  assert.match(press.textContent, /Fixture Press.*Owner: amber.*Coal: 2\/3/s);
  assert.equal(art.visualGlyph.textContent, "⚙");
  art.objectArtwork.listeners.error();
  assert.equal(art.dataset.artwork, "missing");
  for (const [x, id] of [[0, "coal"], [1, "diamond"]]) {
    const marker = h.run(`cells.get("${x},0").querySelectorAll(".resource-marker")[0]`);
    assert.equal(marker.objectArtwork.src, `./art/resources/${id}.webp`);
    assert.equal(marker.attributes["aria-label"], id);
    marker.objectArtwork.listeners.load(); assert.equal(marker.dataset.artwork, "ready");
    marker.objectArtwork.listeners.error(); assert.equal(marker.dataset.artwork, "missing");
    assert.ok(marker.visualGlyph.textContent);
  }
  const missing = press.querySelectorAll(".resource-marker")[0];
  assert.equal(missing.objectArtwork, null);
  assert.equal(missing.visualGlyph.textContent, "●");
  await click(press);
  assert.equal(submitted.at(-1).candidateKey, "operate-fixture");
  h.run('boardView.value = "enhanced"; updateBoardView(); renderSnapshot();');
  assert.equal(h.run('cells.get("2,0").querySelectorAll(".machine-art")[0].objectArtwork.src'), "./art/machines/diamond-press.webp");
  h.run('snapshot.presentation.machines = []; renderSnapshot();');
  assert.equal(h.run('cells.get("2,0").dataset.visualId'), "Grass");
  assert.equal(h.run('cells.get("2,0").querySelectorAll(".machine-art").length'), 0);
});


test("Unit cards render authoritative shared storage quantities and refresh after a transfer", () => {
  const initial = response([]);
  initial.presentation.cards.actor.resourceStorage = [{ resourceTypeId: "coal", name: "Coal", quantity: 1, capacity: 3 }];
  const h = harness(initial);
  assert.match(h.elements.get("unit-card").textContent, /Coal: 1 \/ 3 \(shared by/);
  initial.presentation.cards.actor.resourceStorage[0].quantity = 2;
  h.run("renderState(snapshot.result.state)");
  assert.match(h.elements.get("unit-card").textContent, /Coal: 2 \/ 3 \(shared by/);
});


test("Machines render individual supplied storage and bind Give and Operate to their Cell", async () => {
  const initial = response([
    choice("opaque-operate", "Position", { position: { x: 2, y: 0 } }),
    choice("opaque-give", "Position", { position: { x: 3, y: 0 } })
  ]);
  initial.presentation.machines = [
    { id: "press-a", position: { x: 2, y: 0 }, name: "Diamond Press", operation: "Spend 3 Coal -> Output 1 Diamond",
      resourceStorage: [{ name: "Coal", quantity: 3, capacity: 3 }] },
    { id: "press-b", position: { x: 3, y: 0 }, name: "Diamond Press", ownerSide: "blue", operation: "Spend 3 Coal -> Output 1 Diamond",
      resourceStorage: [{ name: "Coal", quantity: 1, capacity: 3 }] }
  ];
  initial.result.state.physical.board.edges = [{ a: { x: 1, y: 0 }, b: { x: 2, y: 0 }, kind: "ClosedDoor" }];
  const submitted = [];
  const h = harness(initial, async (url, options) => { submitted.push(JSON.parse(options.body)); return { ok: true, json: async () => initial }; });
  const machine = x => h.run(`cells.get("${x},0")`);
  assert.match(machine(2).textContent, /Diamond Press.*Coal: 3\/3/s);
  assert.match(machine(2).textContent, /Public/);
  assert.match(machine(3).textContent, /Owner: blue/);
  assert.match(machine(3).dataset.tooltip, /Owner: blue/);
  assert.match(machine(3).attributes["aria-label"], /Owner: blue/);
  assert.match(machine(3).textContent, /Coal: 1\/3/);
  assert.match(machine(2).dataset.tooltip, /Spend 3 Coal.*1 Diamond/);
  assert.equal(machine(2).classList.contains("legal"), true);
  assert.equal(h.run("edges.size"), 1);
  // Supplied choices remain selectable even when local stats/Edge appearances suggest otherwise.
  await click(machine(2));
  assert.equal(submitted.at(-1).candidateKey, "opaque-operate");
  await click(machine(3));
  assert.equal(submitted.at(-1).candidateKey, "opaque-give");
});

test("Machine conversion playback shows changed storage and Diamond output in the same supplied snapshot", async () => {
  const initial = response();
  initial.presentation.machines = [{ id: "press", position: { x: 2, y: 0 }, name: "Diamond Press", operation: "Supplied operation",
    resourceStorage: [{ name: "Coal", quantity: 3, capacity: 3 }] }];
  const final = structuredClone(initial);
  final.presentation.machines[0].resourceStorage[0].quantity = 0;
  final.result.state.resources = [{ type: { id: "diamond", name: "Diamond" }, position: { x: 0, y: 0 } }];
  final.result.events = [{ kind: "MachineOperated", machineId: "press" }];
  final.presentation.events = [{ role: "Notice", text: "Supplied conversion" }];
  final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
  final.presentation.resolutionSteps = [{ eventIndex: 0, cards: final.presentation.cards, machines: final.presentation.machines }];
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = false; const originalRender = renderState;
    renderState = (...args) => { originalRender(...args); shown.push({
      storage: cells.get("2,0").textContent,
      output: cells.get("0,0").querySelectorAll(".resource-marker").map(node => node.title),
      symbol: cells.get("0,0").querySelectorAll(".resource-marker")[0]?.visualGlyph.textContent
    }); };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.ok(shown.length > 0);
  assert.ok(shown.every(frame => /Coal: 0\/3/.test(frame.storage) && frame.output[0] === "Diamond" && frame.symbol === "\u25c6"));
});

for (const animate of [true, false]) test(`Lava playback renders each authoritative Coal placement (animate=${animate})`, async () => {
  const initial = response();
  initial.result.state.physical.board.terrain = [{ position: { x: 1, y: 0 }, kind: "Lava" }];
  initial.result.state.resources = [];
  const final = structuredClone(initial);
  const coal = x => ({ type: { id: "coal", name: "Coal" }, position: { x, y: 0 } });
  const first = structuredClone(initial.result.state); first.resources = [coal(0)];
  final.result.state.resources = [coal(0), coal(2)];
  final.result.events = [{ kind: "LavaCoalRolled", dieRoll: 7 }, { kind: "ResourceCreated" },
    { kind: "LavaCoalRolled", dieRoll: 3 }, { kind: "ResourceCreated" }];
  final.presentation.events = ["Lava rolled d8 7", "Coal at 0,0", "Lava rolled d8 3", "Coal at 2,0"].map(text => ({ role: "Notice", text }));
  final.result.resolutionSteps = [initial.result.state, first, first, final.result.state].map((stateAfter, eventIndex) => ({ eventIndex, stateAfter }));
  final.presentation.resolutionSteps = final.result.resolutionSteps.map(({ eventIndex }) => ({ eventIndex, cards: final.presentation.cards }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = ${animate}; pause = async () => {}; const originalRender = renderState;
    renderState = (...args) => { originalRender(...args); shown.push([0, 2].filter(x => cells.get(x + ",0").querySelectorAll(".resource-marker").length).length); };`);
  assert.match(h.run('cells.get("1,0").className'), /Lava/);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.deepEqual(shown.slice(0, 4), [0, 1, 1, 2]);
  assert.equal(h.run('cells.get("0,0").querySelectorAll(".resource-marker")[0].title'), "Coal");
});


for (const animate of [true, false]) test(`Rail directions and separate step snapshots render during playback (animate=${animate})`, async () => {
  const initial = response();
  initial.result.state.physical.board.terrain = ["North", "East", "South", "West"].map((direction, x) => ({ position: { x, y: 0 }, kind: "Rail", direction }));
  const final = structuredClone(initial);
  final.result.events = [1, 2, 3].map(x => ({ kind: "UnitRepositioned", unitId: "actor", actionId: "run-rails", path: [{ x: x - 1, y: 0 }, { x, y: 0 }] }));
  final.presentation.events = final.result.events.map(event => ({ role: "Movement", text: "Supplied rail step", unitId: "actor", path: event.path }));
  final.result.resolutionSteps = [1, 2, 3].map((x, eventIndex) => {
    const stateAfter = structuredClone(initial.result.state); stateAfter.physical.figures[0].position.x = x;
    return { eventIndex, stateAfter };
  });
  final.result.state = final.result.resolutionSteps[2].stateAfter;
  final.presentation.resolutionSteps = final.result.resolutionSteps.map(({ eventIndex }) => ({ eventIndex, cards: final.presentation.cards }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  for (const [x, direction] of ["North", "East", "South", "West"].entries()) {
    assert.equal(h.run(`cells.get("${x},0").dataset.railDirection`), direction);
    assert.match(h.run(`cells.get("${x},0").title`), new RegExp(direction));
    assert.equal(h.run(`cells.get("${x},0").railArrow.dataset.direction`), direction);
    assert.equal(h.run(`cells.get("${x},0").railArrow.style["--arrow-rotation"]`), `${x*90}deg`);
  }
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = ${animate}; pause = async () => {}; const originalRender = renderState;
    renderState = (...args) => { originalRender(...args); shown.push(displayedState.physical.figures[0].position.x); };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.deepEqual(shown.slice(0, 3), [1, 2, 3]);
});

for (const view of ["classic", "enhanced"]) test(`${view}: Rail neighbor changes refresh shape while preserving arrow, hits and material fallbacks`, async () => {
  const initial = response([choice("rail-cell", "Position", {position:{x:1,y:1}})]);
  initial.visualSeed = "rail-render-fixture";
  initial.result.state.physical.board.height = 3;
  initial.result.state.physical.board.terrain = [
    {kind:"Rail",position:{x:1,y:1},direction:"East"},
    {kind:"Rail",position:{x:1,y:0},direction:"South"}
  ];
  const h = harness(initial);
  h.elements.get("board-view").value = view; h.elements.get("board-view").listeners.change();
  const cell = h.run('cells.get("1,1")'), image = cell.railArtwork, arrow = cell.railArrow;
  assert.equal(cell.dataset.railShape, "curve"); assert.equal(cell.dataset.railRotation, "0");
  const base = cell.style["--visual-image"], rotation = cell.style["--visual-rotation"];
  image.listeners.error(); assert.equal(cell.dataset.railArtwork, "missing");
  image.listeners.load(); assert.equal(cell.dataset.railArtwork, "ready");
  h.run('snapshot.result.state.physical.board.terrain[1].direction = "North"; renderState(snapshot.result.state, true);');
  assert.equal(cell.dataset.railShape, "straight"); assert.equal(cell.dataset.railRotation, "90");
  assert.equal(cell.railArrow, arrow); assert.equal(arrow.dataset.direction, "East");
  assert.ok(cell.children.includes(image)); assert.ok(cell.children.includes(arrow));
  assert.equal(cell.style["--visual-image"], base); assert.equal(cell.style["--visual-rotation"], rotation);
  h.run('chooseCandidate = key => submitted.push(key);');
  await click(cell); assert.equal(h.submitted.at(-1), "rail-cell");
  h.run('snapshot.result.state.physical.board.terrain[0].direction = "South"; renderState(snapshot.result.state, true);');
  assert.equal(cell.railArrow, arrow); assert.equal(arrow.dataset.direction, "South");
  assert.equal(arrow.style["--arrow-rotation"], "180deg");
  assert.equal(cell.dataset.railShape, "straight"); assert.equal(cell.dataset.railRotation, "0");
  h.run('delete boardVisuals.terrain.Rail.tracks.straight; renderState(snapshot.result.state, true);');
  assert.equal(cell.dataset.railArtwork, "missing"); assert.ok(cell.children.includes(arrow));
  h.run('snapshot.result.state.physical.board.terrain[0].kind = "Gravel"; delete snapshot.result.state.physical.board.terrain[0].direction; renderState(snapshot.result.state, true);');
  assert.equal(cell.railArtwork, null); assert.equal(cell.railArrow, null);
  image.listeners.load(); image.listeners.error(); assert.equal(cell.dataset.railArtwork, undefined);
});

for (const view of ["classic", "enhanced"]) {
  test(`${view}: view switches preserve mounted interactions, selection and authoritative positions`, async () => {
    const door = { a: { x: 1, y: 0 }, b: { x: 2, y: 0 }, kind: "ClosedDoor" };
    const initial = response([
      choice("unit-key", "Unit", { unitId: "a" }),
      choice("cell-key", "Position", { position: { x: 1, y: 0 } }),
      choice("machine-key", "Position", { position: { x: 2, y: 0 } }),
      choice("door-key", "Door", { door })
    ]);
    initial.result.state.physical.board.edges = [door];
    initial.result.state.physical.board.terrain = [{ position: { x: 1, y: 0 }, kind: "Water" }];
    initial.result.state.resources = [{ type: { id: "diamond", name: "Diamond" }, position: { x: 0, y: 0 } }];
    initial.presentation.machines = [{ id: "m", typeId: "test-machine", name: "Fixture", operation: "Supplied", position: { x: 2, y: 0 }, resourceStorage: [] }];
    const submitted = [];
    const h = harness(initial, async (url, options) => { submitted.push(JSON.parse(options.body)); return { ok: true, json: async () => initial }; });
    const actor = h.figure("actor"), cell = h.run('cells.get("1,0")'), machine = h.run('cells.get("2,0")'), edge = h.run('[...edges.values()][0]');
    const before = JSON.stringify(initial);
    h.run('focusedUnitId = "a"; hoveredUnitId = "b"; boardChooser = snapshot.presentation.decision.candidates;');
    for (const next of ["enhanced", "classic", view]) {
      h.elements.get("board-view").value = next;
      h.elements.get("board-view").listeners.change();
      assert.equal(h.elements.get("board").dataset.view, next);
      assert.equal(h.figure("actor"), actor);
      assert.equal(h.run('cells.get("1,0")'), cell);
      assert.equal(h.run('cells.get("2,0")'), machine);
      assert.equal(h.run('[...edges.values()][0]'), edge);
      assert.equal(h.run('focusedUnitId'), "a");
      assert.equal(h.run('hoveredUnitId'), "b");
      assert.equal(h.run('boardChooser.length'), 4);
    }
    assert.equal(JSON.stringify(initial), before);
    assert.equal(submitted.length, 0);
    assert.ok(actor.classList.contains("selected"));
    assert.equal(actor.style.left, `${.5 / 6 * 100}%`);
    assert.equal(edge.style.left, `${2 / 6 * 100}%`);
    assert.equal(machine.dataset.cell, "2,0");
    assert.equal(machine.children.find(n => n.className === "machine-art").dataset.visualId, "test-machine");
    assert.equal(cell.dataset.visualId, "Water");
    assert.equal(h.run('cells.get("0,0").querySelectorAll(".resource-marker")[0].title'), "Diamond");
    for (const [node, key] of [[h.figure("a"), "unit-key"], [cell, "cell-key"], [machine, "machine-key"], [edge, "door-key"]]) {
      await click(node);
      assert.equal(submitted.at(-1).candidateKey, key);
    }
  });

  for (const vertical of [false, true]) for (const mode of ["animate", "disabled", "skip"]) test(`${view}: movement and ${vertical ? "vertical" : "horizontal"} door playback survive switching views (${mode})`, async () => {
    const initial = response();
    initial.result.state.physical.board.height = 2;
    const door = { a: { x: 1, y: 0 }, b: vertical ? { x: 2, y: 0 } : { x: 1, y: 1 }, kind: "ClosedDoor" };
    initial.result.state.physical.board.edges = [door];
    const final = structuredClone(initial);
    final.result.state.physical.figures[0].position.x = 2;
    final.result.state.physical.board.edges[0].kind = "OpenDoor";
    final.result.events = [{ kind: "UnitMoved", unitId: "actor" }, { kind: "DoorOpened" }];
    final.presentation.events = [
      { role: "Movement", text: "Supplied", unitId: "actor", path: [{ x: 0, y: 0 }, { x: 2, y: 0 }] },
      { role: "DoorOpened", text: "Supplied", door: final.result.state.physical.board.edges[0] }
    ];
    final.result.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, stateAfter: final.result.state }));
    final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
    const h = harness(initial, async () => ({ ok: true, json: async () => final }));
    const actor = h.figure("actor");
    const mountedEdge = h.run('[...edges.values()][0]');
    mountedEdge.edgeArtwork.listeners.load();
    h.elements.get("board-view").value = view; h.elements.get("board-view").listeners.change();
    h.context.playbackMode = mode;
    h.run(`ui.animate.checked = playbackMode !== "disabled";
      pause = async () => {};
      const originalPresent = present;
      present = async (...args) => {
        boardView.value = boardView.value === "enhanced" ? "classic" : "enhanced";
        boardView.listeners.change();
        if (playbackMode === "skip") ui.skip.listeners.click();
        await originalPresent(...args);
      };`);
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.equal(h.figure("actor").style.left, `${2.5 / 6 * 100}%`);
    const edge = h.run('[...edges.values()][0]');
    assert.equal(edge.dataset.orientation, vertical ? "Vertical" : "Horizontal");
    assert.equal(edge.dataset.visualId, "OpenDoor");
    assert.ok(edge.classList.contains("OpenDoor"));
    assert.equal(edge.classList.contains("ClosedDoor"), false);
    assert.equal(edge.edgeArtwork.src, "./art/edges/door-open.webp");
    assert.equal(edge.dataset.edgeArtwork, "loading");
    edge.edgeArtwork.listeners.load();
    assert.equal(edge.dataset.edgeArtwork, "ready");
  });
}

for (const size of [6, 50]) test(`connected Wall runs meet at a corner on a ${size} by ${size} board`, () => {
  const initial = response();
  Object.assign(initial.result.state.physical.board, { width: size, height: size, edges: [
    { a: { x: 1, y: 0 }, b: { x: 1, y: 1 }, kind: "Wall" },
    { a: { x: 2, y: 0 }, b: { x: 2, y: 1 }, kind: "Wall" },
    { a: { x: 2, y: 1 }, b: { x: 3, y: 1 }, kind: "Wall" },
    { a: { x: 2, y: 2 }, b: { x: 3, y: 2 }, kind: "Wall" }
  ] });
  const h = harness(initial);
  h.elements.get("board-view").value = "enhanced"; h.elements.get("board-view").listeners.change();
  const nodes = Array.from(h.run('[...edges.values()]'));
  const [first, second, third, fourth] = nodes;
  const close = (a, b) => assert.ok(Math.abs(a - b) < 1e-10, `${a} != ${b}`);
  close(parseFloat(first.style.left) + parseFloat(first.style.width), parseFloat(second.style.left));
  close(parseFloat(second.style.left) + parseFloat(second.style.width) / 2, parseFloat(third.style.left));
  close(parseFloat(second.style.top), parseFloat(third.style.top) - parseFloat(third.style.height) / 2);
  close(parseFloat(third.style.top) + parseFloat(third.style.height), parseFloat(fourth.style.top));
  for (const node of nodes) {
    node.edgeArtwork.listeners.load();
    assert.equal(node.dataset.edgeArtwork, "ready");
  }
  h.run('renderState(snapshot.result.state, true);');
  assert.deepEqual(Array.from(h.run('[...edges.values()]')), nodes);
});

for (const [kind, file] of [["Wall", "wall"], ["ClosedDoor", "door-closed"], ["OpenDoor", "door-open"]]) {
  for (const vertical of [false, true]) test(`${kind}: ${vertical ? "vertical" : "horizontal"} artwork stays centered on the authoritative Edge with load/error fallback`, () => {
    const initial = response();
    initial.result.state.physical.board.height = 4;
    initial.result.state.physical.board.edges = [{ a: { x: 1, y: 1 }, b: vertical ? { x: 2, y: 1 } : { x: 1, y: 2 }, kind }];
    const h = harness(initial);
    const edge = h.run('[...edges.values()][0]'), image = edge.edgeArtwork;
    assert.equal(edge.dataset.orientation, vertical ? "Vertical" : "Horizontal");
    assert.equal(edge.style.left, `${(vertical ? 2 : 1.5) / 6 * 100}%`);
    assert.equal(edge.style.top, `${(vertical ? 1.5 : 2) / 4 * 100}%`);
    assert.equal(edge.style.width, vertical ? "var(--edge-thickness)" : `${100 / 6}%`);
    assert.equal(edge.style.height, vertical ? "25%" : "var(--edge-thickness)");
    assert.equal(image.src, `./art/edges/${file}.webp`);
    assert.equal(image.alt, "");
    assert.equal(edge.dataset.edgeArtwork, "loading");
    image.listeners.load(); assert.equal(edge.dataset.edgeArtwork, "ready");
    h.run('renderState(snapshot.result.state, true);');
    assert.equal(edge.edgeArtwork, image); // Retained image and no redundant loads.
    assert.equal(edge.dataset.edgeArtwork, "ready");
    image.listeners.error(); assert.equal(edge.dataset.edgeArtwork, "missing");
    assert.equal(edge.style["--visual-fill"], kind === "Wall" ? "#45494b" : kind === "ClosedDoor" ? "#bd914c" : "transparent");
    h.run(`delete boardVisuals.edge.${kind}.image; renderState(snapshot.result.state, true);`);
    assert.equal(edge.edgeArtwork, null);
    assert.equal(edge.style["--visual-image"], "none");
    assert.equal(edge.children.length, 0);
    h.run('snapshot.result.state.physical.board.width = 50; snapshot.result.state.physical.board.height = 50; renderState(snapshot.result.state, true);');
    assert.equal(h.run('cells.size'), 2500);
    assert.equal(edge.style.left, `${(vertical ? 2 : 1.5) / 50 * 100}%`);
    assert.equal(edge.style.top, `${(vertical ? 1.5 : 2) / 50 * 100}%`);
  });
}

test("visual catalog resolves content IDs, optional images and missing-art fallbacks without touching state", () => {
  const h = harness(response());
  h.run(`boardVisuals.unit["opaque-type"] = { image: "./assets/missing.webp" };
    renderSnapshot();`);
  assert.equal(h.figure("actor").style["--visual-image"], 'url("./assets/missing.webp")');
  assert.match(h.figure("actor").style["--visual-detail"], /radial-gradient/);
  h.run('delete boardVisuals.unit["opaque-type"]; renderSnapshot();');
  assert.equal(h.figure("actor").style["--visual-image"], "none");
  assert.equal(h.figure("actor").dataset.visualId, "opaque-type");
  h.run('snapshot.result.state.physical.board.width = 50; snapshot.result.state.physical.board.height = 50; renderSnapshot();');
  const first = h.run('cells.get("49,49")');
  assert.equal(h.run('cells.size'), 2500);
  h.run('renderState(snapshot.result.state, true);');
  assert.equal(h.run('cells.get("49,49")'), first);
  assert.equal(h.elements.get("board").style.gridTemplateColumns, "repeat(50, minmax(0, 1fr))");
});

for (const view of ["classic", "enhanced"]) test(`${view}: composited token names, HP, Side and posture use supplied presentation`, () => {
  const initial = response();
  initial.result.state.types[0].displayName = "Custom Cartographer";
  initial.presentation.cards.actor.displayName = "Custom Cartographer";
  initial.presentation.cards.actor.individualName = "Mira";
  initial.result.state.types[0].hp = 6;
  initial.result.state.units[0].currentHp = 2;
  initial.result.state.physical.figures[0].posture = "Lying";
  const h = harness(initial);
  h.elements.get("board-view").value = view; h.elements.get("board-view").listeners.change();
  const actor = h.figure("actor"), named = className => actor.children.find(n => n.className === className);
  assert.equal(named("token-nameplate").textContent, "Mira");
  assert.equal(named("figure-name").textContent, "CC1"); // Classic's existing label stays independent.
  assert.equal(named("figure-hp").textContent, "2/6");
  assert.equal(actor.style["--side-hue"], h.figure("a").style["--side-hue"]);
  assert.ok(actor.classList.contains("active"));
  assert.ok(actor.classList.contains("token-selected"));
  assert.ok(actor.classList.contains("lying"));
  assert.equal(actor.dataset.posture, "Lying");
  assert.equal(named("token-posture").textContent, "Lying");
  assert.equal(named("token-side").textContent, "same");
  assert.match(actor.attributes["aria-label"], /Mira.*Side same.*HP 2\/6/);
  assert.equal(named("token-portrait").textContent.trim(), "Mi");
  // CSS truncates the visual name only; full names and Side remain discoverable.
  const fullName = "Mira the Cartographer of the Far Northern Mountains — ExpeditionLeader";
  h.context.fullName = fullName;
  h.run('snapshot.presentation.cards.actor.individualName = fullName; renderSnapshot();');
  const longNameActor = h.figure("actor");
  const longNameplate = longNameActor.children.find(n => n.className === "token-nameplate");
  assert.equal(longNameplate.textContent, fullName);
  assert.equal(longNameplate.title, fullName);
  assert.ok(longNameActor.title.includes(fullName));
  assert.ok(longNameActor.attributes["aria-label"].includes(fullName));
  assert.match(longNameActor.title, /Side same/);
  assert.match(longNameActor.attributes["aria-label"], /Side same/);
  assert.match(h.elements.get("unit-card").textContent, /same/);
  assert.equal(longNameActor.children.find(n => n.className === "token-side").attributes["aria-hidden"], "true");
  assert.ok(longNameActor.classList.contains("active"));
  assert.ok(longNameActor.classList.contains("token-selected"));
  assert.ok(longNameActor.classList.contains("lying"));
  assert.equal(longNameActor.children.find(n => n.className === "figure-name").textContent, "CC1");
  h.run('snapshot.presentation.cards.actor.individualName = "  "; snapshot.result.state.physical.figures[0].posture = "Upright"; snapshot.result.state.types[0].hp = 1; renderSnapshot();');
  const updated = h.figure("actor");
  assert.equal(updated.children.find(n => n.className === "token-nameplate").textContent, "Custom Cartographer");
  assert.equal(updated.children.some(n => n.className === "figure-hp"), false);
  assert.equal(updated.dataset.posture, "Upright");
  assert.equal(updated.classList.contains("lying"), false);
  h.run('focusedUnitId = "a"; highlightContext();');
  assert.equal(h.figure("actor").classList.contains("active"), true);
  assert.equal(h.figure("actor").classList.contains("token-selected"), false);
  assert.equal(h.figure("a").classList.contains("token-selected"), true);
});

test("portrait assets use the content catalog and retain fallback on image failure", () => {
  const h = harness(response());
  h.run('boardVisuals.unit["opaque-type"] = { image: "./art/units/fixture.webp" }; renderSnapshot();');
  const portrait = h.figure("actor").children.find(n => n.className === "token-portrait");
  const fallback = portrait.children[0], img = portrait.children[1];
  assert.equal(img.src, "./art/units/fixture.webp");
  assert.equal(img.alt, "");
  img.listeners.load(); assert.equal(fallback.hidden, true);
  img.listeners.error(); assert.equal(img.hidden, true); assert.equal(fallback.hidden, false);
  h.run('delete boardVisuals.unit["opaque-type"]; renderSnapshot();');
  assert.equal(h.figure("actor").children.find(n => n.className === "token-portrait").children.length, 1);
  // A missing card uses the scenario's custom Type name, never a generated Unit ID.
  h.run('displayedCards = {}; snapshot.result.state.types[0].displayName = "Custom Type"; renderBoard(snapshot.result.state, false);');
  assert.equal(h.figure("actor").children.find(n => n.className === "token-nameplate").textContent, "Custom Type");
});

for (const name of ["barbarian", "zombie", "worker", "chief-mechanic", "diamond-kart", "geomancer", "wizard", "rogue", "grunt", "skeleton-archer", "goblin", "troll",
  "cleric", "shaman", "ghost", "bomb-imp", "displacer-demon", "labyrinth-shaper", "red-dragon", "locomotive",
  "royal-peasant", "goblin-peasant", "castle-guard", "tower-troll",
  "royal-bishop", "goblin-bishop", "royal-knight", "hobby-knight"]) test(`${name}: shipped portrait composes with token information and existing selection`, () => {
  const initial = response();
  initial.result.state.types[0].id = `${name}-type`;
  initial.result.state.units[0].typeId = `${name}-type`;
  initial.result.state.types[0].hp = 6;
  initial.result.state.units[0].currentHp = 2;
  initial.result.state.physical.figures[0].posture = "Lying";
  initial.presentation.cards.actor.individualName = "Artwork fixture";
  const h = harness(initial);
  h.elements.get("board-view").value = "enhanced"; h.elements.get("board-view").listeners.change();
  const actor = h.figure("actor"), named = c => actor.children.find(n => n.className === c);
  const classicLabel = named("figure-name").textContent;
  const portrait = named("token-portrait");
  assert.equal(portrait.children[1].src, `./art/units/${name}.webp`);
  portrait.children[1].listeners.load();
  assert.equal(portrait.children[0].hidden, true);
  portrait.children[1].listeners.error();
  assert.equal(portrait.children[1].hidden, true);
  assert.equal(portrait.children[0].hidden, false);
  assert.equal(named("token-nameplate").textContent, "Artwork fixture");
  assert.equal(named("figure-hp").textContent, "2/6");
  assert.equal(named("token-side").textContent, "same");
  assert.ok(actor.classList.contains("active"));
  assert.ok(actor.classList.contains("lying"));
  assert.ok(actor.classList.contains("token-selected"));
  h.run('focusedUnitId = "a"; highlightContext();');
  assert.equal(actor.classList.contains("token-selected"), false);
  h.elements.get("board-view").value = "classic"; h.elements.get("board-view").listeners.change();
  assert.equal(named("figure-name").textContent, classicLabel);
  assert.equal(h.elements.get("board").dataset.view, "classic");
});

test("terrain variants use the host seed during rerender, view changes and reload, not runtime board changes", () => {
  const initial = response(); initial.visualSeed = "fixture-board";
  initial.result.state.physical.board.height = 4;
  const h = harness(initial);
  const distribution = 'JSON.stringify([...cells.values()].map(n => [n.dataset.cell, n.dataset.visualVariant, n.style["--visual-fill"], n.style["--visual-image"]]))';
  const first = h.run(distribution);
  h.run('renderSnapshot();'); assert.equal(h.run(distribution), first);
  h.elements.get("board-view").value = "enhanced"; h.elements.get("board-view").listeners.change();
  assert.equal(h.run(distribution), first);
  assert.equal(harness(structuredClone(initial)).run(distribution), first);
  h.run('snapshot.result.state.physical.board.edges.push({a:{x:0,y:0},b:{x:1,y:0},kind:"Wall"}); renderSnapshot();');
  assert.equal(h.run(distribution), first);
  const other = structuredClone(initial); other.visualSeed = "other-board";
  assert.notEqual(harness(other).run(distribution), first);
});

for (const view of ["classic", "enhanced"]) test(`${view}: composite artwork survives rerenders, loading failures, selection and clicking`, async () => {
  const initial = response([choice("cell-key", "Position", {position:{x:1,y:0}})]);
  initial.visualSeed = "composite-fixture";
  initial.result.state.physical.board.terrain = [
    {kind:"Tree", position:{x:1,y:0}}, {kind:"StoneFloorWithTable", position:{x:0,y:0}}
  ];
  initial.result.state.resources = [{type:{id:"coal",name:"Coal"},position:{x:0,y:0}}];
  const h = harness(initial);
  h.elements.get("board-view").value = view; h.elements.get("board-view").listeners.change();
  const cell = h.run('cells.get("1,0")'), image = cell.terrainObjectArtwork;
  const distribution = 'JSON.stringify([...cells.values()].map(n => [n.dataset.visualVariant,n.dataset.visualRotation,n.dataset.objectVariant,n.dataset.objectRotation]))';
  const first = h.run(distribution);
  assert.equal(cell.dataset.objectArtwork, "loading");
  image.listeners.error(); assert.equal(cell.dataset.objectArtwork, "missing");
  image.listeners.load(); assert.equal(cell.dataset.objectArtwork, "ready");
  h.run('renderState(snapshot.result.state, true, snapshot.presentation.cards);');
  assert.equal(h.run('cells.get("1,0")'), cell);
  assert.ok(cell.children.includes(image));
  assert.equal(cell.dataset.objectArtwork, "ready");
  assert.equal(h.run(distribution), first);
  assert.equal(harness(structuredClone(initial)).run(distribution), first);
  assert.equal(h.run('cells.get("0,0").querySelectorAll(".resource-marker")[0].title'), "Coal");
  assert.ok(h.figure("actor"));
  assert.equal(image.alt, ""); assert.equal(image.draggable, false);
  assert.equal(image.attributes["aria-hidden"], "true");
  h.run('chooseCandidate = key => submitted.push(key);');
  await click(cell); assert.equal(h.submitted.at(-1), "cell-key");
  h.run('boardVisuals.terrain.Tree.object.variants = []; delete boardVisuals.terrain.Tree.object.image; renderSnapshot();');
  assert.equal(h.run('cells.get("1,0").dataset.objectArtwork'), "missing");
  const current = h.run('cells.get("1,0")'), currentImage = current.terrainObjectArtwork;
  h.run('snapshot.result.state.physical.board.terrain = []; renderState(snapshot.result.state, true);');
  assert.equal(current.dataset.terrainObject, undefined);
  assert.equal(current.terrainObjectArtwork, null);
  assert.ok(!current.children.includes(currentImage));
  currentImage.listeners.load(); currentImage.listeners.error();
  assert.equal(current.dataset.objectArtwork, undefined);
});

for (const mode of ["animate", "disabled", "skip"]) test(`composite decoration stays stable through supplied movement playback (${mode})`, async () => {
  const initial = response(), final = structuredClone(initial);
  initial.visualSeed = final.visualSeed = "composite-playback";
  const terrain = [{kind:"Tree",position:{x:0,y:0}}, {kind:"StoneFloorWithTable",position:{x:1,y:0}}];
  initial.result.state.physical.board.terrain = structuredClone(terrain);
  final.result.state.physical.board.terrain = structuredClone(terrain);
  final.result.state.physical.figures[0].position.x = 1;
  final.result.events = [{kind:"UnitMoved",unitId:"actor"}];
  final.presentation.events = [{role:"Movement",text:"Supplied move",unitId:"actor",path:[{x:0,y:0},{x:1,y:0}]}];
  final.result.resolutionSteps = [{eventIndex:0,stateAfter:final.result.state}];
  final.presentation.resolutionSteps = [{eventIndex:0,cards:final.presentation.cards}];
  const h = harness(initial, async () => ({ok:true,json:async () => final}));
  h.elements.get("board-view").value = "enhanced"; h.elements.get("board-view").listeners.change();
  const distribution = 'JSON.stringify([...cells.values()].map(n => [n.dataset.visualVariant,n.dataset.visualRotation,n.dataset.objectVariant,n.dataset.objectRotation]))';
  const before = h.run(distribution);
  h.context.mode = mode;
  h.run(`ui.animate.checked = mode !== "disabled"; pause = async () => {};
    const originalPresent = present;
    present = async event => { await originalPresent(event); if (mode === "skip") ui.skip.listeners.click(); };`);
  await h.run('mutate("decision", {candidateKey:"opaque"})');
  assert.equal(h.run(distribution), before);
  assert.ok(h.run('cells.get("0,0").children.includes(cells.get("0,0").terrainObjectArtwork)'));
  assert.equal(h.figure("actor").style.left, `${1.5 / 6 * 100}%`);
});

for (const view of ["classic", "enhanced"]) test(`${view}: Cell Markers render independently, preserve choices and fall back on missing artwork`, async () => {
  const initial = response([choice("peel-destination", "Position", { position: { x: 2, y: 0 } })]);
  initial.result.state.cellMarkers = [{ type: { id: "banana-peel", name: "Banana Peel" }, position: { x: 2, y: 0 } },
    { type: { id: "fixture-missing-marker", name: "Fixture marker" }, position: { x: 3, y: 0 } }];
  initial.result.state.resources = [{ type: { id: "coal", name: "Coal" }, position: { x: 2, y: 0 } }];
  const submitted = [];
  const h = harness(initial, async (url, options) => {
    submitted.push(JSON.parse(options.body)); return { ok: true, json: async () => initial };
  });
  h.run(`boardView.value = "${view}"; updateBoardView(); renderSnapshot();`);
  const cell = h.run('cells.get("2,0")'), marker = cell.querySelectorAll(".cell-marker")[0];
  assert.equal(marker.attributes["aria-label"], "Banana Peel");
  assert.equal(marker.objectArtwork.src, "./art/markers/banana-peel.webp");
  assert.equal(marker.visualGlyph.textContent, "\u{1F34C}");
  marker.objectArtwork.listeners.load(); assert.equal(marker.dataset.artwork, "ready");
  marker.objectArtwork.listeners.error(); assert.equal(marker.dataset.artwork, "missing");
  assert.ok(marker.visualGlyph.textContent);
  assert.equal(cell.querySelectorAll(".resource-marker").length, 1);
  const missing = h.run('cells.get("3,0").querySelectorAll(".cell-marker")[0]');
  assert.equal(missing.objectArtwork, null); assert.equal(missing.visualGlyph.textContent, "?");
  await click(cell); assert.equal(submitted.at(-1).candidateKey, "peel-destination");
  h.run('snapshot.result.state.cellMarkers = []; renderSnapshot();');
  assert.equal(h.run('cells.get("2,0").querySelectorAll(".cell-marker").length'), 0);
});

for (const mode of ["animate", "disabled", "skip"]) test(`loaded board objects survive playback and final snapshot redraw (${mode})`, async () => {
  const initial = response();
  initial.sessionId = "artwork-fixture";
  initial.result.state.cellMarkers = [{ type: { id: "banana-peel", name: "Banana Peel" }, position: { x: 2, y: 0 } }];
  initial.result.state.resources = [{ type: { id: "coal", name: "Coal" }, position: { x: 2, y: 0 } }];
  initial.presentation.machines = [{ id: "press", typeId: "diamond-press", name: "Press", operation: "Supplied",
    position: { x: 3, y: 0 }, resourceStorage: [] }];
  const final = structuredClone(initial);
  final.result.events = [{ kind: "FixtureNotice" }, { kind: "FixtureNotice" }];
  final.presentation.events = final.result.events.map(() => ({ role: "Notice", text: "Supplied notice" }));
  final.result.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, stateAfter: final.result.state }));
  final.presentation.resolutionSteps = [0, 1].map(eventIndex => ({ eventIndex, cards: final.presentation.cards, machines: final.presentation.machines }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  const objects = () => h.run('[cells.get("2,0").querySelectorAll(".cell-marker")[0], cells.get("2,0").querySelectorAll(".resource-marker")[0], cells.get("3,0").querySelectorAll(".machine-art")[0]]');
  const original = objects();
  for (const node of original) node.objectArtwork.listeners.load();
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = ${mode !== "disabled"}; pause = async () => {};
    const originalRender = renderState; renderState = (...args) => { originalRender(...args);
      shown.push([cells.get("2,0").querySelectorAll(".cell-marker")[0], cells.get("2,0").querySelectorAll(".resource-marker")[0], cells.get("3,0").querySelectorAll(".machine-art")[0]]); };
    const originalPresent = present; present = async event => { if (${mode === "skip"}) skipEffects = true; await originalPresent(event); };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.equal(shown.length, 3);
  for (const nodes of shown) for (let i = 0; i < original.length; i++) {
    assert.equal(nodes[i], original[i]);
    assert.equal(nodes[i].dataset.artwork, "ready");
    assert.equal(nodes[i].objectArtwork, original[i].objectArtwork);
  }
  // A changed resource loads its own art; removed objects cannot leave stale art.
  h.run('snapshot.result.state.resources[0].type = { id: "diamond", name: "Diamond" }; renderSnapshot();');
  assert.notEqual(objects()[1], original[1]);
  assert.equal(objects()[1].objectArtwork.src, "./art/resources/diamond.webp");
  assert.equal(h.elements.get("board").querySelectorAll(".resource-marker").includes(original[1]), false);
  h.run('snapshot.result.state.cellMarkers = []; snapshot.result.state.resources = []; snapshot.presentation.machines = []; renderSnapshot();');
  assert.equal(h.run('boardObjects.size'), 0);
  assert.equal(h.run('ui.board.querySelectorAll(".cell-marker").length + ui.board.querySelectorAll(".resource-marker").length + ui.board.querySelectorAll(".machine-art").length'), 0);
  h.run('snapshot = initial; snapshot.sessionId = "replacement-game"; renderSnapshot();');
  assert.notEqual(objects()[0], original[0]);
  assert.equal(objects()[0].dataset.artwork, "loading");
});

for (const mode of ["animate", "disabled", "skip"]) test(`Banana Peel playback preserves Move, consumption and Posture order (${mode})`, async () => {
  const initial = response();
  initial.result.state.cellMarkers = [{ type: { id: "banana-peel", name: "Banana Peel" }, position: { x: 2, y: 0 } }];
  const arrived = structuredClone(initial.result.state); arrived.physical.figures[0].position = { x: 2, y: 0 };
  const consumed = structuredClone(arrived); consumed.cellMarkers = [];
  const lying = structuredClone(consumed); lying.physical.figures[0].posture = "Lying";
  const final = structuredClone(initial); final.result.state = lying;
  final.result.events = [{ kind: "MovementCompleted", unitId: "actor", path: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 0 }] },
    { kind: "CellMarkerConsumed", unitId: "actor", cell: { x: 2, y: 0 } }, { kind: "PostureChanged", unitId: "actor", posture: "Lying" }];
  final.presentation.events = [{ role: "Movement", text: "Moved", unitId: "actor", path: final.result.events[0].path },
    { role: "Notice", text: "Consumed Banana Peel", unitId: "actor" }, { role: "Notice", text: "Lying", unitId: "actor" }];
  final.result.resolutionSteps = [arrived, consumed, lying].map((stateAfter, eventIndex) => ({ stateAfter, eventIndex }));
  final.presentation.resolutionSteps = [0, 1, 2].map(eventIndex => ({ eventIndex, cards: final.presentation.cards }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = ${mode !== 'disabled'}; pause = async () => {};
    const originalRender = renderState; renderState = (...args) => { originalRender(...args);
      shown.push({ marker: cells.get("2,0").querySelectorAll(".cell-marker").length, lying: figures.get("actor").classList.contains("lying") }); };
    const originalPresent = present; present = async event => { if (${mode === 'skip'}) skipEffects = true; await originalPresent(event); };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.deepEqual(JSON.parse(JSON.stringify(shown.slice(0, 3))), [{ marker: 1, lying: false }, { marker: 0, lying: false }, { marker: 0, lying: true }]);
});

for (const view of ["classic", "enhanced"]) test(`Asphalt retains Cell choices and stable decoration (${view})`, async () => {
  const initial = response([choice("asphalt-choice", "Position", {position:{x:1,y:0}})]);
  initial.result.state.physical.board.terrain = [{kind:"Asphalt",position:{x:1,y:0}}];
  initial.visualSeed = "asphalt-fixture";
  const h = harness(initial);
  h.elements.get("board-view").value = view; h.elements.get("board-view").listeners.change();
  const cell = h.run('cells.get("1,0")');
  assert.ok(cell.classList.contains("Asphalt"));
  assert.match(cell.textContent, /Asphalt/);
  assert.match(cell.style["--visual-image"], /terrain\/asphalt\/0[123]\.webp/);
  const image = cell.style["--visual-image"], rotation = cell.style["--visual-rotation"];
  h.run('renderSnapshot()');
  assert.equal(cell.style["--visual-image"], image);
  assert.equal(cell.style["--visual-rotation"], rotation);
  h.run('chooseCandidate = key => submitted.push(key)');
  await click(cell); assert.equal(h.submitted.at(-1), "asphalt-choice");
});

for (const mode of ["animate", "disabled", "skip"]) test(`route playback uses supplied shared progress and atomic Peel snapshots (${mode})`, async () => {
  const initial = response();
  const order = { token: { typeId: "opaque-type", sideId: "same" }, kind: "FollowRoute", route: {
    routeId: "shared-track", currentIndex: 0, direction: 1 } };
  initial.result.state.waypoints = [{ id: "north", area: { x: 1, y: 0, w: 2, h: 1 } }, { id: "south", area: { x: 0, y: 1, w: 2, h: 1 } }];
  initial.result.state.routes = [{ id: "shared-track", mode: "PingPong", waypointIds: ["north", "south"] }];
  initial.presentation.orders = [order];
  initial.result.state.orders = [order];
  const advanced = structuredClone(initial.result.state);
  advanced.orders[0].route.currentIndex = 1; advanced.orders[0].route.direction = -1;
  const placed = structuredClone(advanced);
  placed.cellMarkers = [{ type: { id: "banana-peel", name: "Banana Peel" }, position: { x: 1, y: 0 } }];
  const final = structuredClone(initial); final.result.state = placed; final.presentation.orders = placed.orders;
  final.result.events = [{ kind: "RouteAdvanced", unitId: "actor" }, { kind: "ResourceSpent", unitId: "actor" },
    { kind: "CellMarkerPlaced", unitId: "actor", cell: { x: 1, y: 0 } }];
  final.presentation.events = final.result.events.map(e => ({ role: "Notice", text: e.kind, unitId: "actor" }));
  const states = [advanced, placed, placed];
  final.result.resolutionSteps = states.map((stateAfter, eventIndex) => ({ stateAfter, eventIndex }));
  final.presentation.resolutionSteps = states.map((state, eventIndex) => ({ eventIndex, cards: final.presentation.cards, orders: state.orders }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  assert.equal(h.elements.get("board").querySelectorAll(".waypoint-area").length, 2);
  const outline = h.elements.get("board").querySelectorAll(".waypoint-area")[0];
  assert.equal(outline.style.left, `${1 / initial.result.state.physical.board.width * 100}%`);
  const toggle = h.elements.get("waypoints"); toggle.checked = false; toggle.listeners.change();
  assert.ok(h.elements.get("board").classList.contains("hide-waypoints"));
  toggle.checked = true; toggle.listeners.change();
  assert.equal(h.elements.get("board").classList.contains("hide-waypoints"), false);
  assert.match(h.elements.get("unit-card").textContent, /Follow Route.*Ping-pong.*waypoint 1\/2/);
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = ${mode !== 'disabled'}; pause = async () => {};
    const originalRender = renderState; renderState = (...args) => { originalRender(...args);
      shown.push({ order: ui.orders.textContent, markers: cells.get("1,0").querySelectorAll(".cell-marker").length }); };
    const originalPresent = present; present = async event => { if (${mode === 'skip'}) skipEffects = true; await originalPresent(event); };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.match(shown[0].order, /waypoint 2\/2.*←/); assert.equal(shown[0].markers, 0);
  assert.equal(shown[1].markers, 1); assert.equal(shown[2].markers, 1);
});

test("racing resources, Fruit Tree and Slot Machine retain Classic/Enhanced interaction and load fallbacks", async () => {
  const initial = response([choice("slot-choice", "Position", {position:{x:5,y:0}})]);
  initial.result.state.physical.board.terrain = [{position:{x:4,y:0},kind:"FruitTree"}];
  initial.result.state.resources = ["fruit","banana","mushroom","shell"].map((id,x) => ({type:{id,name:id},position:{x,y:0}}));
  initial.presentation.machines = [{id:"slot",typeId:"slot-machine",position:{x:5,y:0},name:"Slot Machine",ownerSide:"blue",
    operation:"Supplied random operation",resourceStorage:[{name:"Fruit",quantity:3,capacity:3}]}];
  initial.result.state.movesCompletedThisActivation = 1; initial.result.state.effectiveMovesPerActivation = 2;
  const h = harness(initial); h.run('chooseCandidate = key => submitted.push(key)');
  for (const view of ["classic","enhanced"]) {
    h.run(`boardView.value = "${view}"; updateBoardView(); renderSnapshot();`);
    const slot = h.run('cells.get("5,0")'), tree = h.run('cells.get("4,0")');
    assert.match(slot.textContent, /Slot Machine.*Owner: blue.*Fruit: 3\/3/s);
    assert.equal(slot.querySelectorAll(".machine-art")[0].objectArtwork.src, "./art/machines/slot-machine.webp");
    assert.ok(tree.classList.contains("FruitTree"));
    assert.match(tree.style["--visual-image"], /grass/);
    for (const [x,id] of ["fruit","banana","mushroom","shell"].entries()) {
      const marker = h.run(`cells.get("${x},0").querySelectorAll(".resource-marker")[0]`);
      assert.equal(marker.objectArtwork.src, `./art/resources/${id}.webp`);
      marker.objectArtwork.listeners.load(); assert.equal(marker.dataset.artwork,"ready");
      marker.objectArtwork.listeners.error(); assert.equal(marker.dataset.artwork,"missing");
      assert.ok(marker.visualGlyph.textContent);
    }
    await click(slot); assert.equal(h.submitted.at(-1),"slot-choice");
    assert.match(h.elements.get("active-unit").textContent, /Moves 1\/2/);
  }
});

test("End Turn stays reserved and trusts explicit projection metadata, including busy boundaries", async () => {
  const supplied = choice("opaque-end", "Direct", {}, { label: "Finish activation", isEndTurn: true, relevant: false });
  const h = harness(response([supplied]));
  assert.equal(h.elements.get("end-turn").disabled, false);
  assert.equal(h.elements.get("end-turn").textContent, "Finish activation");
  assert.equal(buttons(h.elements.get("choices")).length, 0);
  h.run('chooseCandidate = key => submitted.push(key)');
  click(h.elements.get("end-turn"));
  assert.deepEqual(h.submitted, ["opaque-end"]);
  h.run('busy = true; updateControls()');
  assert.equal(h.elements.get("end-turn").disabled, true);
  h.run('busy = false; snapshot.presentation.decision = null; renderSnapshot()');
  assert.equal(h.elements.get("end-turn").disabled, true);
  assert.equal(h.elements.get("end-turn").textContent, "End Turn");
  // Printed words are never used to infer an end-activation rule.
  const other = harness(response([choice("other", "Direct", {}, { label: "End Turn" })]));
  assert.equal(other.elements.get("end-turn").disabled, true);
  assert.equal(buttons(other.elements.get("choices")).length, 1);
});

test("short descriptions retain full content through tooltip and native disclosure with fallback", () => {
  const printed = entry("custom", "Custom ability", "Capability", "Full authoritative wording with exceptions.");
  printed.content.shortDescription = "Concise supplied wording.";
  const h = harness(response([], [printed, entry("fallback", "Fallback", "Action", "Fallback full text")]));
  const descriptions = h.elements.get("unit-card").querySelectorAll(".ability-description");
  assert.equal(descriptions[0].children[0].textContent, "Concise supplied wording.");
  assert.equal(descriptions[0].children[0].dataset.tooltip, printed.content.description);
  assert.equal(descriptions[0].children[1].tagName, "details");
  assert.equal(descriptions[0].children[1].children[0].tagName, "summary");
  assert.equal(descriptions[0].children[1].children[1].textContent, printed.content.description);
  assert.equal(descriptions[1].children[0].textContent, "Fallback full text");
  h.figure("b").listeners.mouseenter();
  assert.doesNotMatch(h.elements.get("unit-card").textContent, /Inspecting|Most recently active/);
  h.figure("b").listeners.mouseleave();
  assert.doesNotMatch(h.elements.get("unit-card").textContent, /Active Unit/);
  assert.match(h.elements.get("unit-card").textContent, /Concise supplied wording/);
});

test("collision overlay can move and Escape restores board focus without submitting", () => {
  const h = harness(response([choice("a", "Unit", { unitId: "b" }), choice("b", "Unit", { unitId: "b" })]));
  const node = h.figure("b"); let focused = false; node.focus = () => { focused = true; };
  node.listeners.keydown({ key: "Enter", preventDefault() {} });
  assert.equal(h.elements.get("decision-panel").hidden, false);
  click(h.elements.get("move-overlay"));
  assert.equal(h.elements.get("board-overlays").classList.contains("at-top"), true);
  h.elements.get("decision-panel").listeners.keydown({ key: "Escape", preventDefault() {} });
  assert.equal(h.elements.get("decision-panel").hidden, true);
  assert.equal(focused, true);
  assert.deepEqual(h.submitted, []);
});

test("dice dismissal is temporary and preserves authoritative faces and Event History", () => {
  let dismiss;
  const h = harness(response(), undefined, { setTimeout: (callback, delay) => { assert.equal(delay, 4500); dismiss = callback; return 1; } });
  h.run('ui.events.append(text("li", "Earlier Attack roll")); renderDice({ pool: { family: "Defence", purpose: "Defence", count: 1, ownerUnitId: "b", sourceActionId: "attack" }, faces: ["Block"], successes: 7 })');
  assert.equal(h.elements.get("dice").hidden, false);
  assert.match(h.elements.get("dice").textContent, /7 Blocks/);
  dismiss();
  assert.equal(h.elements.get("dice").hidden, true);
  assert.match(h.elements.get("events").textContent, /Earlier Attack roll/);
  h.run('renderDice({ pool: { family: "Attack", purpose: "Attack", count: 1, ownerUnitId: "actor", sourceActionId: "attack" }, faces: ["Miss"], successes: 0 })');
  assert.equal(h.elements.get("dice").hidden, false);
  assert.match(h.elements.get("dice").textContent, /0 Hits/);
});

for (const view of ["classic", "enhanced"]) for (const [width, height] of [[8, 8], [20, 15], [5, 12]]) {
  test(`${view}: board-first sizing retains ${width}x${height} square Cell geometry`, () => {
    const initial = response(); Object.assign(initial.result.state.physical.board, { width, height });
    const h = harness(initial); const board = h.elements.get("board");
    h.elements.get("board-view").value = view; h.run('updateBoardView()');
    assert.equal(board.dataset.view, view);
    assert.equal(board.style.aspectRatio, `${width} / ${height}`);
    assert.equal(board.style["--board-ratio"], width / height);
    assert.equal(h.run('cells.size'), width * height);
  });
}

test("camera mode changes are presentation-only and preserve mounted choices and hover", () => {
  const h = harness(response([choice("attack", "Unit", { unitId: "b" })]), undefined, { withCamera: true });
  const board = h.elements.get("board"), figure = h.figure("b");
  figure.listeners.mouseenter();
  const inspected = h.elements.get("unit-card").textContent;
  h.run('boardCamera.setMode("fixed"); boardCamera.setMode("full"); boardCamera.settle();');
  assert.equal(h.run('boardCamera.inspect().preferences.mode'), "full");
  assert.equal(h.figure("b"), figure);
  assert.equal(h.elements.get("unit-card").textContent, inspected);
  assert.equal(figure.attributes["aria-disabled"], "false");
  assert.deepEqual(h.submitted, []);
});

test("reduced-motion mode preserves successive authoritative dice rolls in history", async () => {
  const views = ["Attack", "Defence"].map((family, i) => {
    const view = response(); view.revision += i + 1;
    view.result.events = [{ kind: "DiceRolled", dice: {
      pool: { family, purpose: family, count: 1, ownerUnitId: "actor", sourceActionId: "attack" },
      faces: [i ? "Block" : "Hit"], successes: 1
    } }];
    view.presentation.events = [{ role: "Notice", text: `${family} supplied outcome` }];
    return view;
  });
  const h = harness(response(), async () => ({ ok: true, json: async () => views.shift() }), { matchMedia: () => ({ matches: true }) });
  h.run('present = async () => { throw new Error("Reduced-motion playback must not animate"); }');
  await h.run('mutate("decision", { candidateKey: "first" })');
  await h.run('mutate("decision", { candidateKey: "second" })');
  assert.equal(h.run('skipEffects'), true);
  assert.match(h.elements.get("events").textContent, /Attack supplied outcome.*Defence supplied outcome/);
  assert.match(h.elements.get("dice").textContent, /Block.*1 Blocks/);
});

test("compact status updates activation identity and bag count from displayed snapshots", () => {
  const initial = response();
  initial.result.state.bag = [{ typeId: "opaque-type", sideId: "violet" }, { typeId: "opaque-type", sideId: "amber" }];
  initial.presentation.cards.actor.individualName = "Ada";
  const h = harness(initial);
  assert.match(h.elements.get("status").textContent, /Round 1/);
  assert.match(h.elements.get("active-token").textContent, /A printed name.*amber/);
  assert.match(h.elements.get("active-unit").textContent, /Ada/);
  assert.match(h.elements.get("bag-count").textContent, /2 remaining/);
  assert.match(h.elements.get("bag").textContent, /future draw order is unknown/);
  h.run('renderTurn({ ...displayedState, round: 2, bag: [], activeToken: null, currentUnitId: null })');
  assert.match(h.elements.get("status").textContent, /Round 2/);
  assert.match(h.elements.get("bag-count").textContent, /0 remaining/);
  assert.match(h.elements.get("active-token").textContent, /Awaiting draw/);
});

// Test-owned combat snapshots; timers never stand in for authoritative progression.
function combatClock() {
  let now = 0, id = 0; const jobs = new Map();
  return { setTimeout(callback, delay) { const key = ++id; jobs.set(key, { callback, at: now + delay }); return key; },
    clearTimeout(key) { jobs.delete(key); },
    tick(ms) { now += ms; for (const [key, job] of [...jobs]) if (job.at <= now) { jobs.delete(key); job.callback(); } },
    get jobs() { return [...jobs.values()]; } };
}
function combatFixture(targets = ["a"]) {
  const view = response([choice("opaque-roll", "Direct")], [entry("attack", "Attack")]);
  view.sessionId = "combat-session";
  view.result.state.physical.board.height = 8;
  view.result.state.physical.board.width = 8;
  view.presentation.cards.actor.individualName = "Castle Guard";
  view.presentation.cards.a.displayName = view.presentation.cards.b.displayName = "Goblin Peasant";
  view.result.state.attackInProgress = { attackerId: "actor", actionId: "attack", targets: targets.map(targetId => ({ targetId, defenceDice: 2 })),
    attackDice: 3, stage: "AttackRoll", targetIndex: 0, results: [], attackRoll: null };
  view.presentation.decision.roll = { family: "Attack", count: 3, ownerUnitId: "actor", sourceUnitId: "actor", sourceActionId: "attack", purpose: "Attack" };
  return view;
}
function suppliedCombatRoll(family, ownerUnitId = "actor") {
  return { pool: { family, count: family === "Attack" ? 3 : 2, ownerUnitId, sourceUnitId: "actor", sourceActionId: "attack", purpose: family },
    faces: family === "Attack" ? ["Hit", "Miss", "Hit"] : ["Block", "Miss"], successes: family === "Attack" ? 2 : 1 };
}
function withCombatEvents(view, events, states) {
  view.result.events = events;
  view.presentation.events = events.map(e => ({ role: "Notice", text: `${e.kind} raw actor a`, displayText: `${e.kind} Castle Guard`, unitId: e.unitId, targetId: e.targetId }));
  view.result.resolutionSteps = events.map((e, eventIndex) => ({ eventIndex, stateAfter: states?.[eventIndex] ?? view.result.state }));
  view.presentation.resolutionSteps = events.map((e, eventIndex) => ({ eventIndex, cards: view.presentation.cards }));
  return view;
}
for (const mode of ["animate", "disabled", "reduced", "skip"]) test(`manual Attack survives a long Defence wait and clears combined results after resolution (${mode})`, async () => {
  const clock = combatClock(), initial = combatFixture(), pending = combatFixture(), resolved = combatFixture();
  const attack = suppliedCombatRoll("Attack"), defence = suppliedCombatRoll("Defence", "a");
  Object.assign(pending.result.state.attackInProgress, { stage: "DefenceRoll", attackRoll: attack });
  pending.presentation.decision.unitId = "a"; pending.presentation.decision.roll = defence.pool;
  withCombatEvents(pending, [{ kind: "DiceRolled", dice: attack }]);
  const defenceState = structuredClone(pending.result.state);
  const outcomeState = structuredClone(defenceState);
  outcomeState.attackInProgress.targetIndex = 1;
  outcomeState.attackInProgress.results = [{ targetId: "a", blocks: 1, damage: 1 }];
  resolved.result.state.attackInProgress = null; resolved.presentation.decision = null;
  withCombatEvents(resolved, [{ kind: "DiceRolled", dice: defence },
    { kind: "AttackResolved", unitId: "actor", targetId: "a", actionId: "attack", hits: 2, blocks: 1, damage: 1 },
    { kind: "ActivationCompleted", unitId: "actor" }, { kind: "TokenDrawn" }],
    [defenceState, outcomeState, resolved.result.state, resolved.result.state]);
  const queue = [pending, resolved], requests = [];
  const h = harness(initial, async (url, options) => { requests.push(JSON.parse(options.body)); return { ok: true, json: async () => queue.shift() }; },
    { setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout, matchMedia: () => ({ matches: mode === "reduced" }) });
  h.run(`ui.animate.checked = ${mode !== "disabled"}; pause = async () => { ${mode === "skip" ? "skipEffects = true;" : ""} }`);
  await click(buttons(h.elements.get("choices"))[0]);
  clock.tick(600000);
  assert.equal(h.elements.get("dice").hidden, false);
  assert.equal(h.elements.get("dice").querySelectorAll(".dice-group").length, 1);
  assert.match(h.elements.get("dice").textContent, /Hit.*Miss.*Hit.*2 Hits/);
  assert.match(h.elements.get("prompt").textContent, /Goblin Peasant \(4,0\) defends against Castle Guard/);
  assert.equal(clock.jobs.length, 0, "No dismissal timer while Defence is pending");
  h.figure("b").listeners.mouseenter(); h.figure("b").listeners.mouseleave();
  click(h.elements.get("move-overlay"));
  assert.match(h.elements.get("dice").textContent, /2 Hits/);
  await click(buttons(h.elements.get("choices"))[0]);
  assert.equal(h.elements.get("dice").querySelectorAll(".dice-group").length, 2);
  assert.match(h.elements.get("dice").textContent, /2 Hits/);
  const targetRow = h.elements.get("dice").querySelectorAll(".combat-target")[0];
  assert.match(targetRow.textContent, /1 Damage/);
  assert.match(targetRow.textContent, /Block.*Miss.*1 Blocks/);
  assert.deepEqual(requests.map(request => request.candidateKey), ["opaque-roll", "opaque-roll"]);
  assert.match(h.elements.get("events").textContent, /DiceRolled raw actor a.*AttackResolved raw actor a/);
  clock.tick(7999); assert.equal(h.elements.get("dice").hidden, false);
  clock.tick(1); assert.equal(h.elements.get("dice").hidden, true);
  assert.equal(h.elements.get("dice").children.length, 0);
  assert.match(h.elements.get("events").textContent, /AttackResolved raw actor a/);
});

test("automatic combat uses the same combined tray, and a new same-source Attack invalidates old timers", async () => {
  const clock = combatClock(), initial = combatFixture(), view = combatFixture();
  const context = view.result.state.attackInProgress;
  view.result.state.attackInProgress = null; view.presentation.decision = null;
  withCombatEvents(view, [{ kind: "AttackStarted", attackContext: context }, { kind: "DiceRolled", dice: suppliedCombatRoll("Attack") },
    { kind: "DiceRolled", dice: suppliedCombatRoll("Defence", "a") },
    { kind: "AttackResolved", unitId: "actor", targetId: "a", actionId: "attack", hits: 2, blocks: 1, damage: 1 }]);
  // Per-event snapshots are supplied by the engine; keep the continuation until the final outcome.
  view.result.resolutionSteps.slice(0, 3).forEach(step => { step.stateAfter = initial.result.state; });
  const h = harness(initial, async () => ({ ok: true, json: async () => view }), { setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout });
  h.run('ui.animate.checked = false');
  await h.run('mutate("decision", { candidateKey: "automatic" })');
  assert.equal(h.elements.get("dice").querySelectorAll(".dice-group").length, 2);
  const oldDismiss = clock.jobs[0].callback;
  h.context.nextContext = structuredClone(context);
  h.run('prepareOccurrence({ kind: "AttackStarted", attackContext: nextContext }, { text: "Next Attack" })');
  assert.equal(h.elements.get("dice").children.length, 0);
  h.context.nextRoll = suppliedCombatRoll("Attack"); h.context.nextRoll.faces = ["Miss", "Miss", "Miss"]; h.context.nextRoll.successes = 0;
  h.run('prepareOccurrence({ kind: "DiceRolled", dice: nextRoll }, { text: "Second Attack" })');
  oldDismiss(); clock.tick(10000);
  assert.equal(h.elements.get("dice").hidden, false);
  assert.doesNotMatch(h.elements.get("dice").textContent, /Block|2 Hits|1 Damage/);
  assert.match(h.elements.get("dice").textContent, /0 Hits/);
});

test("multi-target Attack retains shared dice across every Defence and supports targets without a roll", () => {
  const clock = combatClock(), initial = combatFixture(["a", "b"]), h = harness(initial, undefined,
    { setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout });
  h.context.attack = suppliedCombatRoll("Attack"); h.context.defence = suppliedCombatRoll("Defence", "a");
  h.run('renderDice(attack); renderDice(defence); prepareOccurrence({ kind: "AttackTargetResolved", unitId: "actor", targetId: "a", hits: 2, blocks: 1, damage: 1 }, { text: "First target" })');
  clock.tick(600000);
  assert.equal(h.elements.get("dice").hidden, false);
  assert.equal(clock.jobs.length, 0);
  h.run('prepareOccurrence({ kind: "AttackTargetResolved", unitId: "actor", targetId: "b", hits: 2, blocks: 0, damage: 2 }, { text: "Zero Defence Dice" })');
  const rows = h.elements.get("dice").querySelectorAll(".combat-target");
  assert.deepEqual(rows.map(row => row.dataset.targetId), ['a', 'b']);
  assert.match(rows[0].textContent, /Goblin Peasant \(4,0\).*1 Damage.*1 Blocks/);
  assert.match(rows[1].textContent, /Goblin Peasant \(5,0\).*2 Damage.*0 Blocks/);
  assert.equal(h.elements.get("dice").querySelectorAll(".dice-group").length, 2, "No fabricated Defence faces for zero dice");
  clock.tick(8000); assert.equal(h.elements.get("dice").children.length, 0);
});

test("baseline hydration pins reported Attack dice; interruption, replacement and playback cancellation clear stale dice", () => {
  const clock = combatClock(), initial = combatFixture();
  initial.result.state.attackInProgress.stage = "DefenceRoll";
  initial.result.state.attackInProgress.attackRoll = suppliedCombatRoll("Attack");
  const h = harness(initial, undefined, { setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout });
  clock.tick(600000); assert.match(h.elements.get("dice").textContent, /2 Hits/);
  h.run('snapshot.result.state.attackInProgress = null; renderSnapshot()');
  assert.equal(h.elements.get("dice").children.length, 0);
  h.context.continuation = structuredClone(initial.result.state);
  h.context.continuation.attackInProgress = { attackerId: "actor", actionId: "attack", targets: [{ targetId: "a" }], stage: "DefenceRoll", attackRoll: suppliedCombatRoll("Attack"), results: [] };
  h.run('snapshot.result.state = continuation; renderSnapshot(); cancelPlayback()');
  assert.equal(h.elements.get("dice").hidden, true);
  h.run('renderSnapshot()');
  assert.match(h.elements.get("dice").textContent, /2 Hits/);
  h.run('snapshot.sessionId = "replacement"; snapshot.result.state.attackInProgress = null; renderSnapshot()');
  assert.equal(h.elements.get("dice").children.length, 0);
});

test("readable display metadata changes no candidate payloads or debug/event IDs", () => {
  const initial = combatFixture(); initial.result.state.attackInProgress = null;
  initial.presentation.decision.roll = null;
  initial.presentation.decision.displayPrompt = "Castle Guard chooses an Action";
  initial.presentation.decision.candidates = [choice("raw-choice-id", "Direct", {}, { label: "raw actor a", displayLabel: "Castle Guard attacks Goblin Peasant" })];
  const h = harness(initial); h.run('chooseCandidate = key => submitted.push(key)');
  assert.equal(buttons(h.elements.get("choices"))[0].textContent, "Castle Guard attacks Goblin Peasant");
  assert.equal(h.elements.get("board-hint").textContent, "Castle Guard chooses an Action");
  click(buttons(h.elements.get("choices"))[0]); assert.deepEqual(h.submitted, ["raw-choice-id"]);
  assert.match(h.elements.get("unit-debug").textContent, /actor/);
  assert.doesNotMatch(h.elements.get("unit-card").textContent, /actor/);
  h.context.displayEvent = { kind: "ActionUsed", unitId: "actor", targetId: "a" };
  h.run('prepareOccurrence(displayEvent, { text: "raw actor a", displayText: "Castle Guard attacks Goblin Peasant" })');
  assert.doesNotMatch(h.elements.get("effect").textContent, /raw actor/);
  assert.equal(h.run('unitName("a")'), "Goblin Peasant (4,0)");
  assert.equal(h.run('unitName("b")'), "Goblin Peasant (5,0)");
  assert.equal(h.run('unitName("missing-generated-id")'), "Unit");
});

for (const view of ["classic", "enhanced"]) test(`Unit Card affiliation follows actual Side through hover and reassignment (${view})`, () => {
  const initial = response();
  initial.scenarioId = "card-fixture";
  initial.scenarios = [{ id: "card-fixture", sideHues: { Red: 0, Blue: 210, Orchard: 140 } }];
  initial.result.state.types = ["wizard-type", "zombie-type"].map(id => ({ ...state().types[0], id }));
  // Hero and Monster portraits deliberately appear on both conventional and unusual Sides.
  initial.result.state.units.forEach((unit, i) => {
    unit.typeId = i === 0 ? "wizard-type" : "zombie-type";
    unit.sideId = ["Red", "Blue", "Orchard"][i];
  });
  const h = harness(initial), card = h.elements.get("unit-card");
  h.context.view = view;
  h.run('document.getElementById("board-view").value = view; renderSnapshot()');
  assert.equal(card.style["--side-hue"], 0);
  assert.equal(card.style["--unit-side-border"], "hsl(var(--side-hue) 60% 70%)");
  for (const [id, hue] of [["a", 210], ["b", 140]]) {
    h.figure(id).listeners.mouseenter();
    assert.equal(card.style["--side-hue"], hue);
    assert.equal(card.style["--side-hue"], h.figure(id).style["--side-hue"]);
    h.figure(id).listeners.mouseleave();
    assert.equal(card.style["--side-hue"], 0);
  }
  h.run('displayedState.units[0].sideId = "Orchard"; renderUnitCard()');
  assert.equal(card.style["--side-hue"], 140);
  h.run('displayedState.units[0].typeId = "zombie-type"; renderUnitCard()');
  assert.equal(card.style["--side-hue"], 140);
  h.run('displayedState.units = []; renderUnitCard()');
  assert.equal(card.style["--unit-side-border"], "var(--ui-border)");
  assert.match(card.textContent, /Inspect a Unit on the board/);
  assert.ok(buttons(card).some(button => button.textContent === "Read the mission"));
  assert.deepEqual(h.submitted, []);
});

test("field guide defaults to Unit with named, separate Mission and Journal panels", () => {
  const html = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/index.html"), "utf8");
  const column = html.slice(html.indexOf('<aside class="card-column"'), html.indexOf('</aside>', html.indexOf('<aside class="card-column"')));
  for (const [tab, title] of [['unit', 'Unit'], ['mission', 'Mission'], ['journal', 'Journal']]) {
    assert.match(column, new RegExp(`data-inspector-tab="${tab}" aria-pressed="${tab === 'unit'}" aria-controls="inspector-${tab}">${title}</button>`));
    const tag = column.match(new RegExp(`<section id="inspector-${tab}"[^>]*>`))[0];
    assert.equal(/ hidden(?:>|\s)/.test(tag), tab !== 'unit', `${tab} initial visibility`);
  }
  assert.match(column, /id="inspector-unit"[^>]*>[\s\S]*?id="unit-card"/);
  assert.match(column, /id="inspector-mission"[\s\S]*?id="objectives"[\s\S]*?id="orders"[\s\S]*?id="world-effects"/);
  assert.match(column, /id="inspector-journal"[\s\S]*?id="events"/);
  assert.ok(!column.includes('class="activation-controls"'));
  assert.match(html, /<dialog id="game-menu" aria-labelledby="game-menu-title">/);
  assert.match(html, /id="open-game-menu" aria-haspopup="dialog" aria-controls="game-menu"/);
});

test("play heading and return link retain context only for the authoritative session", () => {
  const initial = response(); initial.sessionId = 'session-one'; initial.scenarioId = 'fixture';
  initial.scenarios = [{ id: 'fixture', name: 'Supplied scenario name' }];
  const h = harness(initial);
  h.run('delveSetPlayContext({ title: "Campaign chapter", returnHash: "#campaign/test-owned", returnLabel: "Back to campaign" })');
  assert.equal(h.elements.get('session-title').textContent, 'Campaign chapter');
  assert.equal(h.elements.get('session-return').href, '#campaign/test-owned');
  h.run('snapshot.revision++; renderSnapshot()');
  assert.equal(h.elements.get('session-title').textContent, 'Campaign chapter');
  h.run('snapshot.sessionId = "session-two"; renderSnapshot()');
  assert.equal(h.elements.get('session-title').textContent, 'Supplied scenario name');
  assert.equal(h.elements.get('session-return').href, '#scenarios');
  assert.deepEqual(h.submitted, []);
});

test("viewport switching targets camera controls inside their disclosure without hiding the disclosure", () => {
  const h = harness(response(), undefined, { withCamera: true });
  h.run('document.getElementById("view-disclosure").hidden = false; cameraViewport.previousElementSibling = document.getElementById("view-disclosure")');
  assert.equal(h.run('domCameraControls === document.querySelector(".camera-controls")'), true);
  h.run('domViewport.setActive(false)');
  assert.equal(h.elements.get('camera-viewport').hidden, true);
  assert.equal(h.elements.get('camera-controls').hidden, true);
  assert.equal(h.elements.get('view-disclosure').hidden, false);
  h.run('domViewport.setActive(true)');
  assert.equal(h.elements.get('camera-viewport').hidden, false);
  assert.equal(h.elements.get('camera-controls').hidden, false);
  assert.equal(h.elements.get('view-disclosure').hidden, false);
});

for (const succeeds of [true, false]) test(`Designer play context uses the unsaved title only after successful replacement (${succeeds})`, async () => {
  const initial = response(); initial.sessionId = 'session-before';
  const next = response(); next.sessionId = 'session-after';
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: options.body && JSON.parse(options.body) });
    return url.endsWith('/scenario/import') && !succeeds
      ? { ok: false, json: async () => ({ detail: 'Test-owned validation failure' }) }
      : { ok: true, json: async () => succeeds ? next : initial };
  }, { location: { hash: '#designer' } });
  h.run('document.getElementById("designer-title").value = "Unsaved draft name"');
  assert.equal(await h.run('delvePlayDesign("DELVE1:test-owned")'), succeeds);
  assert.equal(requests[0].url, '/api/game/scenario/import');
  assert.equal(requests[0].body.transport, 'DELVE1:test-owned');
  assert.ok(requests.every(request => !request.url.startsWith('/api/library')), 'Play never saves the workspace');
  assert.equal(h.elements.get('designer-title').value, 'Unsaved draft name');
  if (succeeds) {
    assert.equal(h.elements.get('session-title').textContent, 'Unsaved draft name');
    assert.equal(h.elements.get('session-return').href, '#designer');
    assert.equal(h.elements.get('session-return').textContent, 'Back to Designer');
  } else {
    assert.equal(h.elements.get('session-return').href, '#scenarios');
    assert.match(h.elements.get('error').textContent, /validation failure/);
  }
});

test("completion actions restart through the existing revisioned operation and retain the return destination", async () => {
  const initial = response(); initial.sessionId = 'completed-session';
  initial.result.state.completion = { achievedSideIds: ['test-side'] };
  const next = response(); next.sessionId = 'restarted-session'; next.result.state.round = 0;
  const requests = [];
  const h = harness(initial, async (url, options) => {
    requests.push({ url, body: JSON.parse(options.body) }); return { ok: true, json: async () => next };
  });
  h.run('delveSetPlayContext({ title: "Test chapter", returnHash: "#campaign/test-owned", returnLabel: "Back to campaign" }); renderSnapshot()');
  const result = h.elements.get('game-result');
  assert.equal(result.hidden, false); assert.match(result.textContent, /Victory: test-side/);
  const actions = result.children.find(child => child.classList.contains('result-actions'));
  const back = actions.children.find(child => child.tagName === 'a');
  assert.equal(back.href, '#campaign/test-owned');
  await click(buttons(actions).find(button => button.id === 'result-replay'));
  assert.deepEqual(requests, [{ url: '/api/game/restart', body: { expectedRevision: initial.revision } }]);
  assert.equal(h.elements.get('game-result').hidden, true);
  assert.equal(h.elements.get('session-title').textContent, 'Test chapter');
  assert.equal(h.elements.get('session-return').href, '#campaign/test-owned');
  assert.equal(h.run('playContext.sessionId'), 'restarted-session');
});

test("card portraits reuse the Unit Type catalog and preserve fallback through loading and errors", () => {
  for (const typeId of ["wizard-type", "chief-mechanic-type", "royal-peasant-type"]) {
    const initial = response(); initial.result.state.units[0].typeId = typeId;
    initial.result.state.types.push({ ...initial.result.state.types[0], id: typeId });
    const h = harness(initial), card = h.elements.get("unit-card");
    const frame = card.querySelectorAll(".card-portrait")[0];
    const fallback = frame.children[0], button = buttons(frame)[0], image = button.children[0];
    assert.equal(image.src, `./art/units/${typeId.replace(/-type$/, "")}.webp`);
    assert.equal(image.hidden, true); assert.equal(button.disabled, true);
    image.listeners.load();
    assert.equal(image.hidden, false); assert.equal(fallback.hidden, true); assert.equal(button.disabled, false);
    h.run('busy = true; updateControls()');
    assert.equal(button.disabled, false); // Presentation remains usable during playback.
    image.listeners.error();
    assert.equal(image.hidden, true); assert.equal(fallback.hidden, false); assert.equal(button.disabled, true);
    assert.match(frame.attributes["aria-label"], /portrait unavailable/);
    assert.deepEqual(h.submitted, []);
  }
  const h = harness(response()), frame = h.elements.get("unit-card").querySelectorAll(".card-portrait")[0];
  assert.ok(frame.children[0].textContent);
  assert.equal(buttons(frame).length, 0);
});

test("empty Cell hover and state updates keep an unchanged card portrait mounted and loaded", () => {
  const initial = response();
  initial.result.state.types[0].id = "wizard-type";
  for (const unit of initial.result.state.units) unit.typeId = "wizard-type";
  const h = harness(initial), card = h.elements.get("unit-card");
  const header = card.children[0], frame = header.children[0], button = buttons(frame)[0], image = button.children[0];
  image.listeners.load();
  let sourceWrites = 0;
  Object.defineProperty(image, "src", { get: () => "./art/units/wizard.webp", set: () => sourceWrites++ });
  for (const key of ["1,0", "2,0", "3,0", "2,0"]) {
    h.run(`cells.get(${JSON.stringify(key)}).listeners.mouseenter(); cells.get(${JSON.stringify(key)}).listeners.mouseleave()`);
    assert.equal(card.children[0], header);
    assert.equal(header.children[0], frame);
    assert.equal(button.children[0], image);
    assert.equal(image.hidden, false);
    assert.equal(frame.children[0].hidden, true);
  }
  // Another Unit with the same artwork updates the caption without resetting the image.
  h.figure("b").listeners.mouseenter(); click(button);
  assert.equal(h.elements.get("portrait-caption").textContent, "Inspected name (5,0)");
  h.figure("b").listeners.mouseleave();
  h.run('displayedState.units[0].currentHp = 2; renderState(displayedState); renderSnapshot()');
  assert.match(card.textContent, /HP 2 \/ 4/);
  assert.equal(card.children[0], header);
  assert.equal(header.children[0], frame);
  assert.equal(button.children[0], image);
  assert.equal(image.hidden, false);
  assert.equal(button.disabled, false);
  assert.equal(sourceWrites, 0);
});

test("portrait switching shows cached images immediately and ignores obsolete image events", () => {
  const initial = response();
  initial.result.state.types = ["wizard-type", "zombie-type"].map(id => ({ ...state().types[0], id }));
  initial.result.state.units.forEach((unit, index) => { unit.typeId = index ? "zombie-type" : "wizard-type"; });
  const h = harness(initial), card = h.elements.get("unit-card");
  const frame = card.querySelectorAll(".card-portrait")[0], original = buttons(frame)[0].children[0];
  original.listeners.load();
  // Model the browser's synchronously available cached image, before its load event.
  h.run(`const createElement = document.createElement;
    document.createElement = tag => {
      const node = createElement(tag);
      if (tag === "img") { node.complete = true; node.naturalWidth = 256; }
      return node;
    };`);
  for (let index = 0; index < 3; index++) {
    h.figure("b").listeners.mouseenter();
    let button = buttons(frame)[0];
    assert.equal(button.children[0].src, "./art/units/zombie.webp");
    assert.equal(button.children[0].hidden, false);
    assert.equal(frame.children[0].hidden, true);
    original.listeners.error();
    assert.equal(frame.attributes.role, undefined);
    h.figure("b").listeners.mouseleave();
    button = buttons(frame)[0];
    assert.equal(button.children[0].src, "./art/units/wizard.webp");
    assert.equal(button.children[0].hidden, false);
    assert.equal(button.disabled, false);
  }
  const image = buttons(frame)[0].children[0];
  image.listeners.error();
  h.run('renderUnitCard()');
  assert.equal(image.hidden, true);
  assert.equal(frame.children[0].hidden, false);
  assert.equal(buttons(frame)[0].disabled, true);
});

test("portrait enlargement uses a separate modal, survives hover switching and restores focus on dismissal", () => {
  const initial = response([choice("board-choice", "Unit", { unitId: "b" })]);
  initial.result.state.types[0].id = "wizard-type";
  for (const unit of initial.result.state.units) unit.typeId = "wizard-type";
  const h = harness(initial), button = h.elements.get("unit-card").querySelectorAll(".portrait-button")[0];
  button.children[0].listeners.load(); click(button);
  const viewer = h.elements.get("portrait-viewer");
  assert.equal(viewer.open, true);
  assert.equal(h.elements.get("portrait-image").src, "./art/units/wizard.webp");
  assert.equal(h.elements.get("portrait-caption").textContent, "A printed name");
  h.figure("b").listeners.mouseenter(); h.figure("b").listeners.mouseleave();
  assert.equal(viewer.open, true);
  // Native Escape dismissal invokes the same close event as the visible button.
  const restored = h.elements.get("unit-card").querySelectorAll(".portrait-button")[0];
  restored.children[0].listeners.load();
  viewer.close();
  assert.equal(restored.focused, true);
  click(restored);
  click(h.elements.get("close-portrait")); assert.equal(viewer.open, false);
  click(restored); restored.children[0].listeners.error(); viewer.close();
  assert.equal(h.elements.get("unit-card").focused, true);
  assert.deepEqual(h.submitted, []);
  h.elements.get("portrait-image").listeners.error();
  assert.equal(h.elements.get("portrait-image").hidden, true);
  assert.equal(h.elements.get("portrait-error").hidden, false);
});

test("all abilities and usage marks remain visible while only supplied Direct choices activate", () => {
  const attack = entry("attack", "Attack"); attack.content.shortDescription = "";
  const focus = entry("focus", "Focus", "Bonus Action");
  focus.content.shortDescription = "Improve the next Attack.";
  const passive = entry("passive", "Passive", "Capability");
  const free = entry("free", "Free", "Free Action");
  const legal = choice("opaque-focus", "Direct", {}, { entryId: "focus" });
  const h = harness(response([legal], [attack, focus, passive, free]));
  h.run('chooseCandidate = key => submitted.push(key)');
  const rows = h.elements.get("unit-card").querySelectorAll(".card-ability");
  assert.equal(rows.length, 4);
  for (const [index, supplied] of [attack, focus, passive, free].entries()) {
    assert.match(rows[index].textContent, new RegExp(supplied.content.name));
    assert.match(rows[index].textContent, new RegExp(supplied.content.category));
    assert.match(rows[index].textContent, /0 \/ 2 uses/);
    const description = rows[index].querySelectorAll(".ability-description")[0];
    const details = description.children[1], summary = details.children[0];
    assert.equal(details.tagName, "details"); assert.equal(summary.tagName, "summary");
    assert.equal(summary.children[0].className, "sr-only");
    assert.equal(summary.dataset.tooltip, supplied.content.description);
    assert.equal(details.children[1].textContent, supplied.content.description);
    assert.equal(buttons(details).length, 0); // Native keyboard/touch disclosure has no decision listener.
    assert.equal(summary.listeners.click, undefined);
  }
  assert.equal(rows[0].querySelectorAll(".ability-description")[0].children[0].hidden, true);
  assert.equal(buttons(rows[0]).length, 0);
  assert.equal(buttons(rows[1]).length, 1);
  click(buttons(rows[1])[0]); assert.deepEqual(h.submitted, ["opaque-focus"]);
  h.run('busy = true; updateControls()'); assert.equal(buttons(rows[1])[0].disabled, true);
  h.run('busy = false; snapshot.presentation.decision.candidates[0].relevant = false; renderUnitCard()');
  assert.equal(h.elements.get("unit-card").querySelectorAll(".card-ability").length, 4);
  assert.equal(buttons(h.elements.get("unit-card"))[0].hidden, true);
});

test("card identity preserves effective stats, changing posture, nonstandard footprint and Tools debugging", () => {
  const initial = response(); initial.presentation.cards.actor.footprintLabel = "Footprint 2 × 2";
  initial.result.state.physical.figures[0].posture = "Lying";
  const h = harness(initial), card = h.elements.get("unit-card");
  assert.match(card.textContent, /Side same.*HP 4 \/ 4.*Lying.*Footprint 2 × 2/s);
  assert.match(card.textContent, /MOV 3.*RNG 1.*ATK 3 → 17.*DEF 2 → 9/s);
  assert.doesNotMatch(card.textContent, /Active Unit|actor|opaque-type/);
  assert.match(h.elements.get("unit-debug").textContent, /actor.*opaque-type/);
  const html = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/index.html"), "utf8");
  assert.match(html, /class="tools-content"[\s\S]*Unit debug information[\s\S]*id="unit-debug"/);
  assert.match(html, /<dialog id="portrait-viewer" aria-labelledby="portrait-caption">/);
  assert.match(html, /id="close-portrait"[^>]*autofocus/);
  const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
  assert.match(css, /\.card-identity \{[^}]*108px minmax\(0, 1fr\)/);
  assert.match(css, /\.portrait-button img \{[^}]*object-fit: contain/);
  assert.match(css, /@media \(max-width: 760px\) \{\s*\.card-identity \{[^}]*96px/);
  assert.match(css, /@media \(pointer: coarse\)[\s\S]*summary \{ width: 44px; height: 44px/);
  assert.match(css, /summary:focus-visible/);
});

for (const operation of ["start-scenario", "restart-scenario"]) {
  for (const success of [true, false]) test(`${operation} closes Tools only after success (${success}) and preserves Classic`, async () => {
    const initial = response();
    let release;
    const h = harness(initial, async (url) => {
      if (url === "/api/game") return { ok: true, json: async () => initial };
      await new Promise(resolve => { release = resolve; });
      return { ok: success, status: 400, json: async () => success ? response() : { detail: "Test replacement rejected" } };
    });
    h.run('ui.animate.checked = false');
    const tools = h.elements.get("playtest-tools"); tools.open = true;
    const view = h.elements.get("board-view"); view.value = "classic"; view.listeners.change();
    const pending = h.elements.get(operation).listeners.click();
    assert.equal(tools.open, true);
    release(); await pending;
    assert.equal(tools.open, !success);
    assert.equal(view.value, "classic"); assert.equal(h.elements.get("board").dataset.view, "classic");
    if (success) assert.equal(h.elements.get("tools-toggle").focused, true);
    else assert.match(h.elements.get("error").textContent, /Test replacement rejected/);
  });
}

test("Enhanced view defaults once, Tools reopening expands Game without resetting other sections", () => {
  const h = harness(response());
  assert.equal(h.elements.get("board-view").value, "enhanced");
  assert.equal(h.elements.get("board").dataset.view, "enhanced");
  const tools = h.elements.get("playtest-tools"), game = h.elements.get("game-setup");
  tools.open = true; game.open = false; tools.listeners.toggle(); assert.equal(game.open, true);
  game.open = false; tools.open = false; tools.listeners.toggle(); assert.equal(game.open, false);
});

for (const operation of ["start-scenario", "restart-scenario"]) test(`streamed ${operation} keeps Tools open until confirmed replacement`, async () => {
  const requests = [];
  const h = streamHarness(async (url, options) => {
    requests.push(JSON.parse(options.body)); return { ok: true, json: async () => ({}) };
  });
  h.emit(update(0, "baseline", "waiting-human"));
  const tools = h.elements.get("playtest-tools"); tools.open = true;
  const pending = h.elements.get(operation).listeners.click();
  await settleStream();
  assert.equal(tools.open, true); // POST acknowledgement alone is insufficient.
  h.emit(update(0, "baseline", "waiting-human", response(), "fresh-session", requests[0].commandId));
  await pending;
  assert.equal(tools.open, false);
});

for (const operation of ["start-scenario", "restart-scenario"]) test(`streamed ${operation} failure keeps Tools and error feedback`, async () => {
  const h = streamHarness(async () => ({ ok: false, json: async () => ({ detail: "Replacement rejected" }) }));
  h.emit(update(0, "baseline", "waiting-human"));
  const tools = h.elements.get("playtest-tools"); tools.open = true;
  assert.equal(await h.elements.get(operation).listeners.click(), false);
  assert.equal(tools.open, true);
  assert.match(h.elements.get("error").textContent, /Replacement rejected/);
});

for (const view of ["classic", "enhanced"]) for (const terrain of ["StoneFloor", "Asphalt", "Grass", "Water", "Lava", "Rail"]) {
  test(`${view} ${terrain}: board feedback follows projected decision metadata and keyboard focus`, () => {
    const candidates = [
      choice("opaque-select", "Unit", { unitId: "b" }, { boardVisual: { kind: "activate", preview: "Activate Hobby Knight" } }),
      choice("opaque-move", "Position", { position: { x: 2, y: 0 } }, { boardVisual: { kind: "move", preview: "Move Wizard to (2,0)" } }),
      choice("opaque-stay", "Unit", { unitId: "actor" }, { boardVisual: { kind: "stay", preview: "Stay \u2014 Wizard", icon: "\u2161" } }),
      choice("opaque-attack", "Unit", { unitId: "a" }, { boardVisual: { kind: "attack", preview: "Attack Goblin", icon: "\u2316" } })
    ];
    const initial = response(candidates);
    initial.result.state.physical.board.terrain = Array.from({ length: 6 }, (_, x) => ({ position: { x, y: 0 }, kind: terrain, direction: "East" }));
    const h = harness(initial); h.context.view = view;
    h.run('boardView.value = view; updateBoardView()');
    const nodes = [h.figure("b"), h.run('cells.get("2,0")'), h.figure("actor"), h.figure("a")];
    for (let i = 0; i < nodes.length; i++) {
      const node = nodes[i], visual = candidates[i].boardVisual;
      assert.equal(node.classList.contains(`choice-${visual.kind}`), true);
      assert.equal(node.dataset.tooltip.startsWith(visual.preview), true);
      assert.equal(node.attributes["aria-label"].startsWith(visual.preview), true);
      assert.equal(node.tabIndex, 0);
      assert.equal(node.title, "");
      node.focus();
      assert.equal(h.submitted.length, 0, "focus previews without submitting");
      assert.equal(node.querySelectorAll(".choice-marker").length, 0, "input surfaces do not own visual markers");
      const key = i === 0 ? "5,0" : i === 1 ? "2,0" : i === 2 ? "0,0" : "4,0";
      const feedback = h.run(`choiceFeedback.get("${key}")`);
      assert.equal(feedback.parent, h.elements.get("board"));
      assert.equal(feedback.classList.contains("feedback-hover"), true);
      assert.equal(feedback.querySelectorAll(i === 2 ? ".choice-icon" : ".choice-marker").length, 1);
    }
    const stayCell = h.run('cells.get("0,0")');
    assert.equal(stayCell.classList.contains("choice-stay"), true);
    assert.equal(stayCell.classList.contains("choice-move"), false);
    assert.equal(stayCell.classList.contains("legal"), false);
  });
}

test("Fireball and Move on the same occupied Cell describe every alternative and never choose on hover", async () => {
  const initial = response([
    choice("first", "Position", { position: { x: 4, y: 0 } }, { boardVisual: { kind: "special", preview: "Fireball at (4,0)", icon: "\u{1f525}" }, affectedUnitIds: ["actor", "b"] }),
    choice("second", "Unit", { unitId: "a" }, { boardVisual: { kind: "move", preview: "Move Wizard to (4,0)" } })
  ]);
  const h = harness(initial), node = h.figure("a");
  h.run("chooseCandidate = key => submitted.push(key)");
  assert.match(node.dataset.tooltip, /Choose an option: Fireball at \(4,0\) \/ Move Wizard to \(4,0\)/);
  assert.equal(node.classList.contains("choice-special"), true);
  assert.equal(node.classList.contains("choice-move"), true);
  assert.match(h.run('choiceFeedback.get("4,0")').textContent, /\u{1f525}/u);
  node.focus(); assert.equal(h.submitted.length, 0);
  assert.equal(h.figure("b").classList.contains("affected-preview"), false, "no arbitrary alternative preview");
  await node.listeners.keydown({ key: "Enter", preventDefault() {} });
  const options = buttons(h.elements.get("choices"));
  assert.equal(options[0].dataset.tooltip, "Fireball at (4,0)");
  assert.equal(options[1].dataset.tooltip, "Move Wizard to (4,0)");
  options[0].focus();
  assert.equal(h.figure("b").classList.contains("affected-preview"), true);
  assert.equal(h.run('ui.board.querySelectorAll(".placement-preview").length'), 0, "no invented explosion geometry");
  await click(options[1]); assert.equal(h.submitted.at(-1), "second");
});

test("hidden irrelevant feedback is absent and automatic-choice setting is retained", () => {
  const initial = response([choice("irrelevant", "Unit", { unitId: "a" }, { relevant: false, boardVisual: { kind: "attack", preview: "Attack Goblin" } })]);
  initial.autoChooseSingleRelevantChoice = true;
  const h = harness(initial);
  assert.equal(h.figure("a").classList.contains("choice-attack"), false);
  assert.equal(h.elements.get("auto").checked, true);
  h.run('ui.filter.checked = false; renderSnapshot()');
  assert.equal(h.figure("a").classList.contains("choice-attack"), true);
});

test("ranged relationship persists across pending Defence, defeated targets and clears with dice; rapid attacks replace it", () => {
  const clock = combatClock(), h = harness(combatFixture(), undefined, { setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout });
  const links = () => h.elements.get("board").querySelectorAll(".combat-link");
  assert.equal(links().length, 1);
  assert.equal(links()[0].style.width, "50%");
  h.run('snapshot.result.state.attackInProgress.stage = "DefenceRoll"; renderSnapshot()');
  clock.tick(600000);
  assert.equal(links().length, 1);
  assert.equal(h.figure("actor").classList.contains("attack-source"), true);
  assert.equal(h.figure("a").classList.contains("attack-target"), true);
  h.run('prepareOccurrence({ kind: "AttackResolved", unitId: "actor", targetId: "a", actionId: "attack" }, { text: "Complete" }); snapshot.result.state.attackInProgress = null; snapshot.result.state.physical.figures = snapshot.result.state.physical.figures.filter(f => f.id !== "a"); renderSnapshot()');
  assert.equal(links().length, 1, "snapshot positions survive figure removal");
  clock.tick(8000); assert.equal(links().length, 0);
  assert.equal(h.figure("actor").classList.contains("attack-source"), false);
  h.run('prepareOccurrence({ kind: "AttackStarted", attackContext: { attackerId: "actor", actionId: "attack", targets: [{targetId: "b"}] } }, { text: "Next" }); finishCombatDice()');
  const oldDismiss = clock.jobs[0].callback;
  h.run('prepareOccurrence({ kind: "AttackStarted", attackContext: { attackerId: "b", actionId: "attack", targets: [{targetId: "actor"}] } }, { text: "Immediate next" })');
  oldDismiss();
  assert.equal(links().length, 1);
  assert.equal(links()[0].style.left, "68.75%");
  assert.equal(h.figure("b").classList.contains("attack-source"), true);
  assert.equal(h.figure("actor").classList.contains("attack-target"), true);
});

test("feedback graphics never intercept clicks, with contrasting shapes in both views", () => {
  const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-enhanced.css"), "utf8");
  assert.match(css, /#board \.choice-marker \{[^}]*pointer-events: none/s);
  assert.match(css, /#board \.combat-link \{[^}]*pointer-events: none/s);
  assert.match(css, /\.marker-activate[^}]*linear-gradient/s);
  assert.match(css, /\.marker-move[^}]*border: 2px solid[^}]*box-shadow/s);
  assert.match(css, /#board \.feedback-hover/);
});

for (const view of ["classic", "enhanced"]) test(`${view}: Cell and portrait share one set of feedback and alternatives above the token`, async () => {
  const initial = response([
    choice("select", "Unit", { unitId: "a" }, { boardVisual: { kind: "activate", preview: "Activate Wizard" } }),
    choice("fireball", "Position", { position: { x: 4, y: 0 } }, { boardVisual: { kind: "special", preview: "Fireball at (4,0)", icon: "flame" } }),
    choice("heal", "Unit", { unitId: "a" }, { boardVisual: { kind: "special", preview: "Heal Wizard", icon: "heal" } }),
    choice("telekinesis", "Unit", { unitId: "a" }, { boardVisual: { kind: "special", preview: "Telekinesis Wizard", icon: "hand" } })
  ]);
  const h = harness(initial); h.context.view = view;
  h.run('boardView.value = view; updateBoardView(); chooseCandidate = key => submitted.push(key)');
  const cell = h.run('cells.get("4,0")'), figure = h.figure("a"), feedback = h.run('choiceFeedback.get("4,0")');
  assert.equal(h.elements.get("board").querySelectorAll(".choice-feedback").length, 1);
  assert.equal(feedback.parent, h.elements.get("board"), "outside isolated terrain and token stacking contexts");
  assert.equal(cell.querySelectorAll(".choice-marker").length, 0);
  assert.equal(figure.querySelectorAll(".choice-marker").length, 0);
  assert.equal(feedback.querySelectorAll(".marker-activate").length, 1);
  assert.deepEqual(feedback.querySelectorAll(".choice-icon").map(icon => icon.textContent), ["flame", "heal", "hand"]);
  assert.equal(feedback.attributes["aria-hidden"], "true");
  assert.equal(feedback.listeners.click, undefined);
  assert.equal(cell.dataset.tooltip, figure.dataset.tooltip);
  for (const surface of [cell, figure]) {
    surface.listeners.mouseenter(); assert.equal(feedback.classList.contains("feedback-hover"), true);
    surface.listeners.mouseleave(); assert.equal(feedback.classList.contains("feedback-hover"), false);
    surface.focus(); assert.equal(feedback.classList.contains("feedback-hover"), true);
    surface.listeners.blur(); assert.equal(feedback.classList.contains("feedback-hover"), false);
    assert.equal(surface.tabIndex, 0);
    assert.equal(h.submitted.length, 0);
    await click(surface);
    const options = buttons(h.elements.get("choices"));
    assert.equal(options.length, 5, "four alternatives and Cancel from either input surface");
    assert.equal(options[1].dataset.tooltip, "Fireball at (4,0)");
    h.run('closeBoardChooser()');
  }
  h.run('renderState(snapshot.result.state)');
  assert.equal(h.elements.get("board").querySelectorAll(".choice-feedback").length, 0, "playback removes stale feedback");
});

test("Cell feedback layers sit above whole tokens, with non-overlapping click-through icons below decision overlays", () => {
  const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-enhanced.css"), "utf8");
  const base = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
  assert.match(css, /\.choice-feedback \{[^}]*position: absolute;[^}]*z-index: 4;[^}]*pointer-events: none/s);
  assert.match(css, /\.choice-feedback \* \{ pointer-events: none/);
  assert.match(css, /\.choice-icons \{[^}]*display: flex; flex-wrap: wrap;[^}]*gap: 2px/s);
  assert.match(base, /\.figure \{[^}]*z-index: 3/s);
  assert.match(base, /\.board-overlays \{[^}]*z-index: 9/s);
  assert.match(base, /\.shared-tooltip \{[^}]*pointer-events: none/s);
  assert.match(css, /\.cell-choice-feedback:focus-visible \{ outline: none/);
});

test("hidden Unit panel returns Direct actions to pending choices and tab changes retain opaque submissions", () => {
  const listeners = new Map();
  const ability = entry('test-ability', 'Supplied ability');
  const candidate = choice('opaque-card-action', 'Direct', {}, { entryId: ability.content.id });
  const h = harness(response([candidate], [ability]), undefined, {
    addEventListener: (name, listener) => listeners.set(name, listener)
  });
  h.run('chooseCandidate = key => submitted.push(key)');
  assert.equal(buttons(h.elements.get('choices')).length, 0, 'visible card owns its Direct action');
  h.elements.get('inspector-unit').hidden = true;
  listeners.get('delve-inspector-change')({ detail: { tab: 'mission' } });
  const pending = buttons(h.elements.get('choices'));
  assert.equal(pending.length, 1);
  assert.equal(h.elements.get('decision-panel').hidden, false);
  assert.equal(pending[0].disabled, false);
  click(pending[0]);
  assert.deepEqual(h.submitted, ['opaque-card-action']);
  // A new authoritative decision while Mission remains selected must also be reachable.
  h.run('snapshot.presentation.decision.candidates[0].key = "next-opaque-action"; renderSnapshot()');
  assert.equal(buttons(h.elements.get('choices')).length, 1);
  click(buttons(h.elements.get('choices'))[0]);
  assert.deepEqual(h.submitted, ['opaque-card-action', 'next-opaque-action']);
  h.elements.get('inspector-unit').hidden = false;
  listeners.get('delve-inspector-change')({ detail: { tab: 'unit' } });
  assert.equal(buttons(h.elements.get('choices')).length, 0);
  const action = buttons(h.elements.get('unit-card')).find(button => button.dataset.legal === 'true');
  assert.ok(action);
  click(action);
  assert.deepEqual(h.submitted, ['opaque-card-action', 'next-opaque-action', 'next-opaque-action']);
  h.run('busy = true; updateControls()');
  assert.equal(action.disabled, true, 'tab presentation cannot bypass authoritative busy gating');
});

test("final-board inspection dismisses only the current result and reopening never changes game state", () => {
  const initial = response(); initial.sessionId = 'completed-one';
  initial.result.state.completion = { achievedSideIds: ['same'] };
  const h = harness(initial);
  const result = h.elements.get('game-result'), board = h.elements.get('board');
  const originalState = JSON.stringify(initial.result.state);
  const firstFigure = h.figure('actor');
  click(buttons(result).find(button => button.id === 'result-inspect'));
  assert.equal(result.hidden, true);
  assert.equal(h.elements.get('show-result').hidden, false);
  assert.equal(h.elements.get('show-result').focused, true);
  assert.equal(h.figure('actor'), firstFigure, 'dismissing results leaves the table mounted');
  h.run('renderSnapshot()');
  assert.equal(result.hidden, true, 'ordinary refresh must not reopen a dismissed result');
  assert.equal(h.figure('actor').dataset.unitId, 'actor');
  assert.equal(h.elements.get('board'), board);
  click(h.elements.get('show-result'));
  assert.equal(result.hidden, false);
  assert.equal(JSON.stringify(initial.result.state), originalState);
  assert.deepEqual(h.submitted, []);
  click(buttons(result).find(button => button.id === 'result-inspect'));
  h.run('snapshot.sessionId = "completed-two"; renderSnapshot()');
  assert.equal(result.hidden, false, 'a different completed session has its own result');
  h.run('snapshot.result.state.completion = null; renderSnapshot()');
  assert.equal(result.hidden, true);
  assert.equal(h.elements.get('show-result').hidden, true);
});

test("command dock shows only supplied End Turn and available round transitions", () => {
  const initial = response(); initial.result.state.round = 0; initial.presentation.decision = null;
  const h = harness(initial), start = h.elements.get('start-round'), end = h.elements.get('end-turn');
  assert.equal(start.hidden, false); assert.equal(start.disabled, false); assert.equal(end.hidden, true);
  h.run('snapshot.result.state.round = 1; snapshot.result.state.roundComplete = false; renderSnapshot()');
  assert.equal(start.hidden, true); assert.equal(end.hidden, true);
  h.context.endCandidate = choice('opaque-finish', 'Direct', {}, { isEndTurn: true, label: 'Finish supplied activation' });
  h.run('snapshot.presentation.decision = { unitId: "actor", prompt: "Supplied", candidates: [endCandidate] }; renderSnapshot(); chooseCandidate = key => submitted.push(key)');
  assert.equal(end.hidden, false); assert.equal(end.disabled, false);
  assert.equal(end.textContent, 'Finish supplied activation');
  click(end); assert.deepEqual(h.submitted, ['opaque-finish']);
  h.run('busy = true; updateControls()'); assert.equal(end.disabled, true);
  h.run('busy = false; snapshot.presentation.decision = null; snapshot.result.state.roundComplete = true; renderSnapshot()');
  assert.equal(start.hidden, false); assert.equal(end.hidden, true); assert.equal(start.disabled, false);
  h.run('snapshot.result.state.completion = { achievedSideIds: ["same"] }; renderSnapshot()');
  assert.equal(start.hidden, true); assert.equal(start.disabled, true); assert.equal(end.hidden, true);
});

for (const saved of ['classic', 'enhanced', 'three', 'tabletop']) test(`view preference ${saved} is retained and library entry does not initialize Three`, () => {
  const listeners = new Map(), writes = [], requests = [];
  const h = harness(response(), undefined, { withCamera: true, location: { hash: '#home' },
    localStorage: { getItem: key => key === 'delve.board.view.v1' ? saved : null, setItem: (key, value) => writes.push([key, value]) },
    addEventListener: (name, listener) => listeners.set(name, listener)
  });
  assert.equal(h.elements.get('board-view').value, saved);
  assert.equal(h.run('threeViewport'), undefined);
  assert.equal(h.run('viewportRouter.active === domViewport'), true);
  h.context.rendererRequests = requests;
  h.run('viewportRouter = { request: value => rendererRequests.push(value) }; location.hash = "#play"');
  listeners.get('hashchange')();
  assert.deepEqual(requests, [saved]);
  h.run('location.hash = "#scenarios"'); listeners.get('hashchange')();
  assert.deepEqual(requests, [saved], 'browsing does not request a renderer');
  h.elements.get('board-view').value = 'enhanced';
  h.elements.get('board-view').listeners.change();
  assert.deepEqual(writes, [['delve.board.view.v1', 'enhanced']]);
  assert.deepEqual(requests, [saved, 'enhanced']);
});

test('unavailable preference storage does not prevent loading or changing views', () => {
  const h = harness(response(), undefined, {
    localStorage: { getItem() { throw Error('Storage unavailable'); }, setItem() { throw Error('Storage unavailable'); } }
  });
  h.elements.get('board-view').value = 'classic';
  assert.doesNotThrow(() => h.elements.get('board-view').listeners.change());
  assert.equal(h.elements.get('board').dataset.view, 'classic');
});

test('canvas board alternatives stay in the command dock, submit exact candidates and honor busy state', async () => {
  const candidate = choice('opaque-board-cell', 'Position', { position: { x: 1, y: 0 } }, { boardVisual: { kind: 'move' } });
  const h = harness(response([candidate]));
  attachThree(h); await h.run('viewportRouter.request("three")');
  h.run('renderSnapshot(); chooseCandidate = key => submitted.push(key)');
  const host = h.elements.get('board-alternatives');
  let disclosure = host.querySelectorAll('.three-board-choices')[0];
  assert.ok(disclosure); assert.equal(disclosure.open, false);
  assert.equal(h.elements.get('decision-panel').hidden, true, 'a board-only choice does not create an empty overlay');
  assert.equal(buttons(h.elements.get('choices')).length, 0);
  let alternative = buttons(host)[0];
  assert.equal(alternative.dataset.boardChoiceKey, 'opaque-board-cell');
  assert.equal(alternative.dataset.boardChoiceCell, '1-0');
  assert.equal(alternative.disabled, false);
  disclosure.open = true; click(alternative);
  assert.deepEqual(h.submitted, ['opaque-board-cell']);
  assert.equal(disclosure.open, false);
  h.run('busy = true; updateControls()'); assert.equal(alternative.disabled, true);
  h.run('renderChoicePanel()'); alternative = buttons(host)[0];
  assert.equal(alternative.disabled, true, 'rerendering alternatives must not bypass busy gating');
  disclosure = host.querySelectorAll('.three-board-choices')[0]; disclosure.open = true;
  let prevented = false;
  host.listeners.keydown({ key: 'Escape', preventDefault() { prevented = true; } });
  assert.equal(prevented, true); assert.equal(disclosure.open, false);
  assert.equal(disclosure.querySelector('summary').focused, true);
  h.run('snapshot.presentation.decision = null; renderSnapshot()');
  assert.equal(host.children.length, 0, 'obsolete choices are cleared');
});

test('journal uses only supplied event-time player copy and retains filtered technical details', async () => {
  const initial = response(), final = response();
  final.presentation.events = [
    { role: 'Notice', displayText: 'The named defender blocks.', text: 'Filtered raw target-id: blocks' },
    { role: 'Notice', text: 'Fallback observation without display copy' },
    { role: 'Notice', displayText: 'Same supplied line', text: 'Same supplied line' }
  ];
  final.result.events = final.presentation.events.map(() => ({ kind: 'Notice' }));
  const h = harness(initial, async () => ({ ok: true, json: async () => final }));
  h.run('ui.animate.checked = false');
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  const rows = h.elements.get('events').children;
  assert.equal(rows.length, 3);
  assert.equal(rows[0].content, 'The named defender blocks.');
  const details = rows[0].querySelectorAll('details');
  assert.equal(details.length, 1);
  assert.equal(details[0].open, undefined, 'technical detail is collapsed by default');
  assert.match(details[0].textContent, /Event details.*Filtered raw target-id: blocks/);
  assert.equal(rows[1].textContent, 'Fallback observation without display copy');
  assert.equal(rows[2].textContent, 'Same supplied line');
  assert.equal(rows[1].querySelectorAll('details').length, 0);
  assert.equal(rows[2].querySelectorAll('details').length, 0);
});

test('combat tray groups every supplied defender once and never invents missing defence faces', () => {
  const initial = combatFixture(['a', 'b', 'c']);
  initial.result.state.units.push({ ...initial.result.state.units[1], id: 'c' });
  initial.result.state.physical.figures.push({ id: 'c', position: { x: 6, y: 0 }, posture: 'Upright' });
  initial.presentation.cards.c = { ...initial.presentation.cards.a, individualName: 'Third defender' };
  const context = initial.result.state.attackInProgress;
  context.stage = 'DefenceRoll'; context.targetIndex = 1; context.attackRoll = suppliedCombatRoll('Attack');
  context.targets[2].defenceDice = 0;
  context.results = [{ targetId: 'a', blocks: 1, damage: 1 }];
  const clock = combatClock(), h = harness(initial, undefined, { setTimeout: clock.setTimeout, clearTimeout: clock.clearTimeout });
  const tray = h.elements.get('dice'), rows = () => tray.querySelectorAll('.combat-target');
  assert.equal(tray.querySelectorAll('.combat-attack').length, 1, 'one shared Attack roll');
  assert.deepEqual(rows().map(row => row.dataset.targetId), ['a', 'b', 'c']);
  assert.equal(rows()[0].querySelector('.combat-target-name').textContent, 'Goblin Peasant (4,0)');
  assert.equal(rows()[1].querySelector('.combat-target-name').textContent, 'Goblin Peasant (5,0)');
  assert.equal(rows()[2].querySelector('.combat-target-name').textContent, 'Third defender');
  assert.deepEqual(rows().map(row => row.dataset.pending), ['false', 'true', 'false']);
  assert.match(rows()[0].textContent, /1 Damage.*1 Blocks/);
  assert.equal(rows()[0].querySelectorAll('.dice-faces').length, 0, 'hydrated outcome is not a fabricated defence roll');
  assert.match(rows()[1].textContent, /Awaiting defence.*2 Defence dice.*Your roll/);
  assert.equal(rows()[2].querySelectorAll('.dice-faces').length, 0);
  assert.match(rows()[2].textContent, /0 Defence dice/);
  assert.equal(tray.querySelectorAll('.combat-dismiss').length, 0, 'pending defence cannot be dismissed');
  h.context.roll = suppliedCombatRoll('Defence', 'b');
  h.run('renderDice(roll); prepareOccurrence({ kind: "AttackTargetResolved", unitId: "actor", targetId: "b", hits: 2, blocks: 1, damage: 1 }, { text: "Supplied second result" })');
  assert.equal(rows().length, 3);
  assert.match(rows()[1].textContent, /1 Damage.*Block.*Miss.*1 Blocks/);
  assert.equal(rows()[1].querySelectorAll('.dice-faces').length, 1);
  h.run('prepareOccurrence({ kind: "AttackTargetResolved", unitId: "actor", targetId: "c", hits: 2, blocks: 0, damage: 2 }, { text: "Supplied zero-dice result" })');
  assert.equal(rows().length, 3);
  assert.match(rows()[2].textContent, /2 Damage.*0 Blocks/);
  assert.equal(rows()[2].querySelectorAll('.dice-faces').length, 0);
  assert.equal(tray.querySelectorAll('.combat-dismiss').length, 1);
  assert.equal(tray.querySelectorAll('.combat-dismiss')[0].attributes['aria-label'], 'Dismiss combat results');
  click(tray.querySelectorAll('.combat-dismiss')[0]);
  assert.equal(tray.hidden, true); assert.equal(tray.children.length, 0);
  assert.deepEqual(h.submitted, [], 'inspecting and dismissing results never submits gameplay');
});

test('combat tray follows restricted observation targets after replacing an unrestricted session', () => {
  const initial = combatFixture(['a', 'b']);
  initial.presentation.cards.b.individualName = 'Hidden sentinel';
  const h = harness(initial);
  assert.match(h.elements.get('dice').textContent, /Hidden sentinel/);
  const restricted = combatFixture(['a']); restricted.sessionId = 'restricted-session';
  restricted.presentation.viewingSideId = 'same';
  restricted.result.state.units = restricted.result.state.units.filter(unit => unit.id !== 'b');
  restricted.result.state.physical.figures = restricted.result.state.physical.figures.filter(figure => figure.id !== 'b');
  delete restricted.presentation.cards.b;
  h.context.restricted = restricted;
  h.run('snapshot = restricted; renderSnapshot()');
  const rows = h.elements.get('dice').querySelectorAll('.combat-target');
  assert.deepEqual(rows.map(row => row.dataset.targetId), ['a']);
  assert.doesNotMatch(h.elements.get('dice').textContent, /Hidden sentinel|2 targets/);
  assert.match(h.elements.get('dice').textContent, /1 target/);
  assert.equal(h.run('combatDice.names.has("b")'), false);
  assert.deepEqual(h.submitted, []);
});

test("inventory rows distinguish sources and consolidate identical use choices", () => {
  const native = entry("attack", "Attack");
  const boots = { ...entry("equipment:walking:modifiers", "Walking Charm", "Equipment"), source: "Equipment", sourceName: "Walking Charm", copyCount: 1 };
  const wind = { ...entry("object:SecondWind", "Second Wind", "Bonus Action"), source: "Item", copyCount: 2 };
  const initial = response([
    choice("wind-one", "Direct", {}, { entryId: "object:SecondWind" }),
    choice("wind-two", "Direct", {}, { entryId: "object:SecondWind" })
  ], [native, boots, wind]);
  initial.presentation.cards.actor.inventory = [{name:"Walking Charm"},{name:"Second Wind"},{name:"Second Wind"}];
  initial.result.state.effectiveMov = { actor: 2 };
  const h = harness(initial), card = h.elements.get("unit-card");
  const rows = card.querySelectorAll(".card-ability");
  assert.deepEqual(rows.map(row => row.dataset.source), ["Native", "Equipment", "Item"]);
  assert.match(rows[1].textContent, /Equipment · Walking Charm/);
  assert.doesNotMatch(rows[1].textContent, /Identical effects apply once/);
  assert.match(rows[2].textContent, /Single Use Item/);
  assert.equal(buttons(rows[2]).length, 1);
  assert.match(card.querySelectorAll(".inventory-summary")[0].textContent, /3 carried objects/);
  const changes = card.querySelectorAll(".modified-stat");
  assert.ok(changes.some(stat => stat.dataset.change === "negative"));
  assert.ok(changes.some(stat => stat.dataset.change === "positive"));
  h.run('chooseCandidate = key => submitted.push(key)');
  click(buttons(rows[2])[0]); assert.deepEqual(h.submitted, ["wind-one"]);
  assert.equal(h.run('cardChoiceKeys.has("wind-two")'), true);
});
