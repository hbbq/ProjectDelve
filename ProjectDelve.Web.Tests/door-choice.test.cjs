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
      toggle: () => {}
    };
  }
  append(child) { this.children.push(child); }
  replaceChildren(...children) { this.children = children; }
  setAttribute(name, value) { this.attributes[name] = value; }
  addEventListener(name, callback) { this.listeners[name] = callback; }
  querySelectorAll(selector) {
    return this.children.filter(child => selector === "button"
      ? child.tagName === "button" : child.classList.contains(selector.slice(1)));
  }
}

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
      revision: 2, filterRelevantChoices: true,
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
