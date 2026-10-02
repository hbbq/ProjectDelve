const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

// Exercise the actual renderer and event bindings without a browser dependency.
class Element {
  constructor() {
    this.children = [];
    this.dataset = {};
    this.attributes = {};
    this.listeners = {};
    this.style = { setProperty() {} };
    this.checked = true;
    this.className = "";
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
    this.children = [];
    for (const child of children) this.append(child);
  }
  remove() {
    if (this.parent) this.parent.children = this.parent.children.filter(child => child !== this);
    this.parent = null;
  }
  set textContent(value) { this.replaceChildren(); this.content = value; }
  get textContent() { return this.content; }
  setAttribute(name, value) { this.attributes[name] = value; }
  removeAttribute(name) { delete this.attributes[name]; }
  getBoundingClientRect() { this.layoutReads = (this.layoutReads ?? 0) + 1; return {}; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  querySelectorAll(selector) {
    return this.children.filter(child => selector === "button"
      ? child.tagName === "button" : child.classList.contains(selector.slice(1)));
  }
}

for (const mode of ["animate", "disabled", "skip"]) {
  test(`playback renders supplied progressive HP and final state (${mode})`, async () => {
    const elements = new Map();
    const document = {
      getElementById(id) {
        if (!elements.has(id)) elements.set(id, new Element());
        return elements.get(id);
      },
      createElement(tag) { const element = new Element(); element.tagName = tag; return element; }
    };
    const state = hp => ({
      round: 1, activeTypeId: "monster-type", currentUnitId: "monster",
      physical: { board: { width: 2, height: 1, edges: [] }, figures: [
        { id: "hero", position: { x: 0, y: 0 }, posture: "Upright" },
        { id: "monster", position: { x: 1, y: 0 }, posture: "Upright" }
      ] },
      units: [{ id: "hero", typeId: "hero-type", sideId: "blue", currentHp: hp },
        { id: "monster", typeId: "monster-type", sideId: "red", currentHp: 1 }],
      types: [{ id: "hero-type", hp: 5 }, { id: "monster-type", hp: 1 }]
    });
    const initial = { revision: 1, result: { state: state(5), nextInput: null } };
    const response = {
      revision: 2, result: {
        state: state(2), nextInput: { kind: "Activation", unitId: "hero", candidates: [
          { key: "end-turn", kind: "EndTurn" }
        ] },
        // Deliberately unrelated Damage values: presentation must use supplied HP.
        events: [1, 2].map(() => ({ kind: "AttackResolved", unitId: "monster", targetId: "hero", damage: 99 })),
        resolutionSteps: [{ eventIndex: 0, stateAfter: state(4) }, { eventIndex: 1, stateAfter: state(2) }]
      }
    };
    const shown = [], beforeAnimation = [];
    const context = vm.createContext({ document, initial, response, shown, beforeAnimation, mode,
      fetch: async () => ({ ok: true, json: async () => response }) });
    const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");
    vm.runInContext(script.replace(/await refresh\(\);\s*$/, ""), context);
    vm.runInContext(`
      snapshot = initial; renderSnapshot();
      ui.animate.checked = mode !== "disabled";
      pause = async () => {};
      const originalRenderState = renderState;
      renderState = (state, preserveNodes) => {
        originalRenderState(state, preserveNodes);
        shown.push({ hp: figures.get("hero").dataset.hpLabel, choices: ui.choices.children.length });
      };
      const originalPresent = present;
      present = async event => {
        beforeAnimation.push(figures.get("hero").dataset.hpLabel);
        await originalPresent(event);
        if (mode === "skip") ui.skip.listeners.click();
      };
    `, context);
    await vm.runInContext('mutate("decision", { candidateKey: "end-turn" })', context);
    assert.deepEqual(shown.map(value => value.hp), ["HP 4/5", "HP 2/5", "HP 2/5"]);
    assert.deepEqual(shown.slice(0, 2).map(value => value.choices), [0, 0]);
    assert.deepEqual(beforeAnimation, mode === "animate" ? ["HP 5/5", "HP 4/5"]
      : mode === "skip" ? ["HP 5/5"] : []);
    assert.equal(elements.get("choices").children.length, 1);
    assert.equal(elements.get("choices").children[0].disabled, false);
    assert.match(elements.get("units").children[0].textContent, /HP 2\/5/);
    assert.equal(initial.result.state.units[0].currentHp, 5);
    assert.equal(response.result.state.units[0].currentHp, 2);
  });
}

test("successive movement snapshots retain mounted figures and commit each animation start", async () => {
  const elements = new Map();
  const document = {
    getElementById(id) {
      if (!elements.has(id)) elements.set(id, new Element());
      return elements.get(id);
    },
    createElement(tag) { const element = new Element(); element.tagName = tag; return element; }
  };
  const state = x => ({ round: 1,
    physical: { board: { width: 3, height: 1, edges: [] },
      figures: [{ id: "hero", position: { x, y: 0 }, posture: "Upright" }] },
    units: [{ id: "hero", typeId: "hero-type", currentHp: 5 }],
    types: [{ id: "hero-type", hp: 5 }] });
  const initial = { revision: 1, result: { state: state(0), nextInput: null } };
  const response = { revision: 2, result: { state: state(2), nextInput: null,
    events: [0, 1].map(x => ({ kind: "MovementCompleted", unitId: "hero",
      path: [{ x, y: 0 }, { x: x + 1, y: 0 }] })),
    resolutionSteps: [0, 1].map(eventIndex => ({ eventIndex, stateAfter: state(eventIndex + 1) })) } };
  const movements = [], starts = [];
  const context = vm.createContext({ document, initial, movements, starts,
    fetch: async () => ({ ok: true, json: async () => response }) });
  const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");
  vm.runInContext(script.replace(/await refresh\(\);\s*$/, ""), context);
  vm.runInContext(`
    snapshot = initial; renderSnapshot();
    const mounted = figures.get("hero");
    mounted.getBoundingClientRect = () => { starts.push(mounted.style.left); return {}; };
    pause = async () => {
      movements.push({ sameNode: figures.get("hero") === mounted,
        attached: ui.board.children.includes(mounted), left: mounted.style.left });
    };
  `, context);
  await vm.runInContext('mutate("decision")', context);
  assert.deepEqual(starts, [`${.5 / 3 * 100}%`, "50%"]);
  assert.deepEqual(movements.map(step => step.sameNode && step.attached), [true, true]);
  assert.deepEqual(movements.map(step => step.left), ["50%", `${2.5 / 3 * 100}%`]);
  assert.equal(elements.get("error").textContent, "");
});

for (const moveDone of [false, true]) {
  test(`clicking the door submits its Free Action key ${moveDone ? "after" : "before"} Move`, () => {
    const elements = new Map();
    const document = {
      getElementById(id) {
        if (!elements.has(id)) elements.set(id, new Element());
        return elements.get(id);
      },
      createElement(tag) { const element = new Element(); element.tagName = tag; return element; }
    };
    const submitted = [];
    const context = vm.createContext({ document, submitted });
    const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");
    vm.runInContext(script.replace(/await refresh\(\);\s*$/, ""), context);
    const door = { a: { x: 1, y: 0 }, b: { x: 0, y: 0 }, kind: "ClosedDoor" };
    const key = "open-door:0,0:1,0";
    const response = {
      revision: 2, autoChooseSingleRelevantChoice: true,
      result: {
        state: {
          round: 1, roundComplete: false, activeTypeId: "barbarian-type", currentUnitId: "barbarian",
          moveDone, actionDone: false, bonusActionUsed: false,
          physical: { board: { width: 2, height: 1, edges: [door] }, figures: [] },
          units: [], types: []
        },
        nextInput: {
          kind: "Activation", unitId: "barbarian", allowsNone: false,
          // Canonical candidate endpoints differ from the board edge's endpoint order.
          candidates: [{ key, kind: "FreeAction", freeAction: "OpenDoor", action: null,
            door: { ...door, a: door.b, b: door.a } }]
        }
      }
    };
    vm.runInContext(`snapshot = ${JSON.stringify(response)};
      chooseCandidate = key => submitted.push(key);
      renderSnapshot();`, context);
    const renderedDoor = elements.get("board").children.find(node => node.classList.contains("edge"));
    assert.ok(renderedDoor.classList.contains("board-choice"));
    assert.equal(renderedDoor.attributes.role, "button");
    assert.equal(renderedDoor.attributes["aria-disabled"], "false");
    assert.match(renderedDoor.attributes["aria-label"], /Open door.*\(Free Action\)/);
    let stopped = false;
    renderedDoor.listeners.click({ stopPropagation() { stopped = true; } });
    assert.ok(stopped);
    assert.deepEqual(submitted, [key]);
    let prevented = false;
    renderedDoor.listeners.keydown({ key: "Enter", preventDefault() { prevented = true; } });
    assert.ok(prevented);
    assert.deepEqual(submitted, [key, key]);
    elements.get("choices").children[0].listeners.click();
    assert.deepEqual(submitted, [key, key, key]);
  });
}


test("relevance filtering is local, preserves legal submissions, and is independent of automatic progression", async () => {
  const elements = new Map();
  const document = {
    getElementById(id) {
      if (!elements.has(id)) elements.set(id, new Element());
      return elements.get(id);
    },
    createElement(tag) { const element = new Element(); element.tagName = tag; return element; }
  };
  const door = { a: { x: 0, y: 0 }, b: { x: 1, y: 0 }, kind: "ClosedDoor" };
  const irrelevant = { key: "open-door:0,0:1,0", kind: "FreeAction", freeAction: "OpenDoor", door, relevant: false };
  const response = {
    revision: 2, autoChooseSingleRelevantChoice: false,
    result: {
      state: { round: 1, physical: { board: { width: 2, height: 1, edges: [door] }, figures: [] }, units: [], types: [] },
      nextInput: { kind: "Activation", unitId: "hero", allowsNone: false,
        candidates: [irrelevant, { key: "end-turn", kind: "EndTurn", relevant: true }] },
      events: [], resolutionSteps: []
    }
  };
  const requests = [];
  const context = vm.createContext({ document, response,
    fetch: async (url, options) => {
      requests.push({ url, body: JSON.parse(options.body) });
      return { ok: true, json: async () => response };
    }
  });
  const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");
  vm.runInContext(script.replace(/await refresh\(\);\s*$/, ""), context);
  vm.runInContext("snapshot = response; renderSnapshot();", context);
  const renderedDoor = () => elements.get("board").children.find(node => node.classList.contains("edge"));
  assert.equal(elements.get("choices").children.length, 1);
  assert.equal(renderedDoor().classList.contains("board-choice"), false);
  assert.equal(elements.get("auto").checked, false);
  elements.get("filter").checked = false;
  elements.get("filter").listeners.change();
  assert.equal(requests.length, 0);
  assert.equal(response.revision, 2);
  assert.equal(response.result.nextInput.candidates.length, 2);
  assert.equal(elements.get("choices").children.length, 2);
  assert.equal(renderedDoor().classList.contains("board-choice"), true);
  assert.equal(elements.get("auto").checked, false);
  await elements.get("choices").children[0].listeners.click();
  assert.deepEqual(requests[0], { url: "/api/game/decision", body: { expectedRevision: 2, candidateKey: irrelevant.key } });
  assert.equal(elements.get("filter").checked, false);
  assert.equal(elements.get("choices").children.length, 2);
  elements.get("filter").checked = true;
  elements.get("filter").listeners.change();
  assert.equal(requests.length, 1);
  assert.equal(elements.get("choices").children.length, 1);
  assert.equal(renderedDoor().classList.contains("board-choice"), false);
  // Even a hidden supplied key remains available for explicit submission.
  await vm.runInContext(`chooseCandidate("${irrelevant.key}")`, context);
  assert.equal(requests[1].body.candidateKey, irrelevant.key);
  elements.get("auto").checked = true;
  await elements.get("auto").listeners.change();
  assert.deepEqual(requests[2], { url: "/api/game/preferences",
    body: { expectedRevision: 2, autoChooseSingleRelevantChoice: true } });
  assert.equal(elements.get("filter").checked, true);
});

test("Rage displays supplied uses and effective ATK and submits irrelevant supplied choices", async () => {
  const elements = new Map();
  const document = {
    getElementById(id) {
      if (!elements.has(id)) elements.set(id, new Element());
      return elements.get(id);
    },
    createElement(tag) { const element = new Element(); element.tagName = tag; return element; }
  };
  const ability = { name: "Rage", maxUses: 2, modifier: { stat: "Atk", amount: 2 } };
  const response = {
    revision: 7, autoChooseSingleRelevantChoice: false,
    result: {
      state: { round: 1, currentUnitId: "barbarian", moveDone: false,
        physical: { board: { width: 1, height: 1, edges: [] }, figures: [] },
        types: [{ id: "barbarian-type", hp: 5, mov: 3, rng: 1, atk: 4, def: 3, bonusAction: ability }],
        units: [{ id: "barbarian", typeId: "barbarian-type", sideId: "blue", currentHp: 5,
          bonusActionUses: { remainingUses: 1, maxUses: 2 } }],
        modifiersThisTurn: [ability.modifier],
        // Deliberately different from base + modifier: render the supplied engine value.
        effectiveAtk: { barbarian: 17 } },
      nextInput: { kind: "Activation", unitId: "barbarian", allowsNone: false,
        candidates: [{ key: "opaque-bonus-key", kind: "BonusAction", bonusAction: ability, relevant: false },
          { key: "stay", kind: "Stay", relevant: true }] },
      events: [], resolutionSteps: []
    }
  };
  const requests = [];
  const context = vm.createContext({ document, response,
    fetch: async (url, options) => {
      requests.push({ url, body: JSON.parse(options.body) });
      return { ok: true, json: async () => response };
    }
  });
  const script = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/app.js"), "utf8");
  vm.runInContext(script.replace(/await refresh\(\);\s*$/, ""), context);
  vm.runInContext("snapshot = response; renderSnapshot();", context);
  assert.match(elements.get("units").children[0].textContent, /ATK 17 \(base 4\)/);
  assert.match(elements.get("units").children[0].textContent, /Rage 1\/2/);
  assert.equal(elements.get("choices").children.length, 1);
  elements.get("filter").checked = false;
  elements.get("filter").listeners.change();
  assert.equal(requests.length, 0);
  assert.equal(elements.get("choices").children.length, 2);
  assert.equal(elements.get("choices").children[0].textContent, "Rage (Bonus Action)");
  await elements.get("choices").children[0].listeners.click();
  assert.deepEqual(requests[0], { url: "/api/game/decision", body: { expectedRevision: 7, candidateKey: "opaque-bonus-key" } });
  assert.match(vm.runInContext('describe({ kind: "AbilityUsed", unitId: "barbarian", abilityName: "Rage" })', context), /used Rage/);
});
