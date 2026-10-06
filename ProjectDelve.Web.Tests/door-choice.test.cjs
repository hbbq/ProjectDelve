const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");

for (const mode of ["animate", "disabled", "skip"]) {
  test(`Troll survives lethal playback using generic HP, posture and card content (${mode})`, async () => {
    const printed = { content: { id: "undying", name: "Undying", category: "Capability",
      description: "When upright and reduced to 0 HP, remain in your Cell at 1 HP and lay down instead of dying.",
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
    this.style = { setProperty() {} }; this.checked = true; this.className = "";
    this.classList = {
      contains: name => this.className.split(" ").includes(name),
      add: name => { if (!this.classList.contains(name)) this.className += ` ${name}`; },
      remove: name => { this.className = this.className.split(" ").filter(value => value !== name).join(" "); },
      toggle: () => {}
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
function harness(initial, fetchImpl) {
  const elements = new Map(), submitted = [];
  const document = {
    getElementById(id) { if (!elements.has(id)) elements.set(id, new Element()); return elements.get(id); },
    createElement(tag) { const node = new Element(); node.tagName = tag; return node; }
  };
  const context = vm.createContext({ document, initial, submitted, setTimeout, clearTimeout,
    fetch: fetchImpl ?? (async () => ({ ok: true, json: async () => initial })) });
  vm.runInContext(script.replace(/await refresh\(\);\s*$/, ""), context);
  vm.runInContext("snapshot = initial; renderSnapshot();", context);
  return { elements, submitted, run: code => vm.runInContext(code, context), context,
    figure: id => vm.runInContext(`figures.get(${JSON.stringify(id)})`, context) };
}
const click = node => node.listeners.click({ stopPropagation() {} });
const buttons = node => node.querySelectorAll("button");

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
  test(`explicit UnitDied follows authoritative removal even with positive HP (animate=${animate})`, async () => {
    const initial = response(), final = response();
    final.result.state.physical.figures = final.result.state.physical.figures.filter(figure => figure.id !== "b");
    final.result.events = [{ kind: "UnitDied", unitId: "b" }];
    final.presentation.events = [{ role: "Death", text: "b died", unitId: "b" }];
    final.result.resolutionSteps = [{ eventIndex: 0, stateAfter: final.result.state }];
    final.presentation.resolutionSteps = [{ eventIndex: 0, cards: final.presentation.cards }];
    const h = harness(initial, async () => ({ ok: true, json: async () => final }));
    h.context.animate = animate;
    h.run('ui.animate.checked = animate; pause = async () => {};');
    await h.run('mutate("decision", { candidateKey: "opaque" })');
    assert.equal(h.figure("b"), undefined);
    assert.equal(h.run('displayedState.units.find(unit => unit.id === "b").currentHp'), 4);
    assert.match(h.elements.get("effect").textContent, /b died/);
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
  assert.ok(!html.includes("Start a round to play."));
});
