const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");

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
  append(child) { child.remove(); child.parent = this; this.children.push(child); }
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
  setAttribute(name, value) { this.attributes[name] = value; }
  removeAttribute(name) { delete this.attributes[name]; }
  getBoundingClientRect() { this.layoutReads = (this.layoutReads ?? 0) + 1; return {}; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  focus() { this.listeners.focus?.(); }
  querySelectorAll(selector) {
    return this.children.flatMap(child => [
      ...(selector === "button" ? child.tagName === "button" : child.classList.contains(selector.slice(1))) ? [child] : [],
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
    getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); },
    createElement(tag) { const node = new Element(); node.tagName = tag; return node; }
  };
  document.getElementById("coordinates").checked = false;
  const context = vm.createContext({ document, initial, submitted, setTimeout, clearTimeout,
    fetch: fetchImpl ?? (async () => ({ ok: true, json: async () => initial })), ...extras });
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/side-colors.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-visuals.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext(script.replace(/^import .*\r?\n/gm, "").replace(/await refresh\(\);\s*$/, ""), context);
  vm.runInContext("snapshot = initial; renderSnapshot();", context);
  return { elements, submitted, run: code => vm.runInContext(code, context), context,
    figure: id => vm.runInContext(`figures.get(${JSON.stringify(id)})`, context) };
}
const click = node => node.listeners.click({ stopPropagation() {} });
const buttons = node => node.querySelectorAll("button");

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
    assert.match(h.figure("new-goblin").title, /new-goblin.*Lying/);
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
    assert.equal(h.elements.get("prompt").textContent, "Supplied prompt · actor");
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

test("ambiguous and missing board references remain selectable in the choice panel", () => {
  const initial = response([
    choice("first", "Unit", { unitId: "b" }), choice("second", "Unit", { unitId: "b" }),
    choice("missing", "Unit", { unitId: "absent" }), choice("unknown direct", "Direct", {}, { entryId: "no-card-entry" })
  ]);
  const h = harness(initial);
  h.run("chooseCandidate = key => submitted.push(key)");
  assert.equal(h.figure("b").classList.contains("board-choice"), true);
  for (const button of h.elements.get("choices").children) click(button);
  assert.deepEqual(h.submitted, ["first", "second", "missing", "unknown direct"]);
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
  assert.match(h.figure("actor").attributes["aria-label"], /actor.*Lying.*HP 4\/4/);
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
    assert.equal(h.elements.get("choices").children[0].className, "");
    assert.equal(h.figure("a").classList.contains("affected-preview"), false);
    // Ambiguous choices retain a panel fallback.
    click(h.elements.get("choices").children[0]);
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
  assert.equal(h.elements.get("choices").children[0].className, "");
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
    await ui["start-scenario"].listeners.click();
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
  await h.elements.get("restart-scenario").listeners.click();
  assert.equal(requests.length, 1);
  release(); await pending;
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
    assert.match(h.elements.get("prompt").textContent, /roll 3 Attack dice/);
    assert.equal(buttons(h.elements.get("choices")).length, 1);
    assert.equal(buttons(h.elements.get("choices"))[0].textContent, "Roll Dice");
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
  assert.match(h.elements.get("attack-context").textContent, /future-action \u2192 b/);
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
  assert.match(h.elements.get("effect").textContent, /Future Magic \(Free Action\).*b.*\(3,0\).*door 2,0 \/ 3,0/);
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
    assert.match(h.elements.get("effect").textContent, /b was defeated/);
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
  assert.equal(markers[0].title, "2 / 4 uses");
  assert.match(markers[0].textContent, /2 \/ 4 uses/);
  const circles = markers[0].querySelectorAll(".use-circle");
  assert.equal(circles.length, 4);
  assert.deepEqual(circles.map(circle => circle.classList.contains("filled")), [true, true, false, false]);
  assert.ok(circles.every(circle => circle.attributes["aria-hidden"] === "true"));
});

test("gameplay sections are outside the board column and scenario description is a tooltip", () => {
  const html = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/index.html"), "utf8");
  const boardColumn = html.slice(html.indexOf('<section aria-label="Board">'), html.indexOf('<aside'));
  const sidebar = html.slice(html.indexOf('<aside'));
  assert.match(boardColumn, /id="board"/);
  for (const id of ["status", "active-token", "active-unit", "bag", "attack-context", "effect", "dice", "choices", "unit-card"]) {
    assert.ok(!boardColumn.includes(`id="${id}"`), `${id} must not shift the board`);
    assert.ok(sidebar.includes(`id="${id}"`));
  }
  assert.match(html, /aria-label="Scenario information" aria-describedby="scenario-description"/);
  assert.match(html, /id="scenario-description" role="tooltip"/);
  assert.match(boardColumn, /<details id="game-setup"[\s\S]*id="scenario"[\s\S]*id="side-controllers"[\s\S]*id="import-scenario"[\s\S]*<\/details>/);
  assert.match(sidebar, /<details class="sidebar-reference">[\s\S]*id="objectives"[\s\S]*id="orders"[\s\S]*<\/details>/);
  assert.match(html, /id="coordinates" type="checkbox"> Show coordinates/);
  assert.doesNotMatch(html, /id="coordinates"[^>]*checked/);
  assert.ok(!html.includes("Start a round to play."));
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
function streamHarness(fetchImpl) {
  const sources = [], timers = new Map(); let nextTimer = 0;
  class FakeEventSource {
    constructor(url) { this.url = url; this.listeners = {}; sources.push(this); }
    addEventListener(name, listener) { this.listeners[name] = listener; }
    close() { this.closed = true; }
    emit(update) { this.listeners.update({ data: JSON.stringify(update) }); }
  }
  const h = harness(response([choice("initial", "Direct")]), fetchImpl, {
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

test("session reset cancels native animation and old state cannot repaint", async () => {
  const h = streamHarness(); h.emit(update(0, "baseline", "idle"));
  h.emit(update(1, "event", "resolving", movementView()));
  const fresh = response([]); fresh.result.state.physical.figures[0].position.x = 4;
  h.emit(update(0, "baseline", "idle", fresh, "session-b"));
  await settleStream();
  assert.equal(h.run("latestUpdate.sessionId"), "session-b");
  assert.equal(h.run("displayedState.physical.figures[0].position.x"), 4);
  assert.equal(h.elements.get("events").children.length, 0);
  assert.equal(h.timers.size, 0);
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
  assert.match(h.elements.get("orders").textContent, /Grunt .* red: Attack .* Unit hero-alpha/);
  assert.match(h.elements.get("orders").textContent, /Skeleton Archer .* red: Attack .* Rogue .* blue/);
  assert.match(h.elements.get("orders").textContent, /Goblin .* red: Attack .* Side blue/);
  assert.match(h.elements.get("orders").textContent, /Zombie .* red: Protect .* Unit escort-leader/);
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
  initial.presentation.orders = [{ token: { typeId: "opaque-type", sideId: "same" },
    kind: "Attack", target: { kind: "Unit", unitId: "target-alpha" } }];
  const h = harness(initial);
  assert.match(h.elements.get("unit-card").textContent, /Standing Order.*Attack .* Unit target-alpha/);

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
  assert.match(machine(3).title, /Owner: blue/);
  assert.match(machine(3).attributes["aria-label"], /Owner: blue/);
  assert.match(machine(3).textContent, /Coal: 1\/3/);
  assert.match(machine(2).title, /Spend 3 Coal.*1 Diamond/);
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
      symbol: cells.get("0,0").querySelectorAll(".resource-marker")[0]?.textContent
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
  }
  const shown = []; h.context.shown = shown;
  h.run(`ui.animate.checked = ${animate}; pause = async () => {}; const originalRender = renderState;
    renderState = (...args) => { originalRender(...args); shown.push(displayedState.physical.figures[0].position.x); };`);
  await h.run('mutate("decision", { candidateKey: "opaque" })');
  assert.deepEqual(shown.slice(0, 3), [1, 2, 3]);
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

  for (const mode of ["animate", "disabled", "skip"]) test(`${view}: movement and door playback survive switching views (${mode})`, async () => {
    const initial = response();
    const door = { a: { x: 1, y: 0 }, b: { x: 2, y: 0 }, kind: "ClosedDoor" };
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
    assert.equal(edge.dataset.visualId, "OpenDoor");
    assert.ok(edge.classList.contains("OpenDoor"));
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
