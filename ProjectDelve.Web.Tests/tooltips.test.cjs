const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const vm = require("node:vm");
const path = require("node:path");
const root = path.join(__dirname, "../ProjectDelve.Web/wwwroot");

function harness() {
  const timers = new Map(), events = {}, nodes = [];
  let next = 0, observe;
  const element = () => {
    const attributes = {}, listeners = {};
    const node = { attributes, listeners, dataset: {}, style: {}, isConnected: true,
      getAttribute: key => attributes[key] ?? null,
      setAttribute: (key, value) => { attributes[key] = value; },
      removeAttribute: key => { delete attributes[key]; },
      addEventListener: (key, fn) => { listeners[key] = fn; },
      getBoundingClientRect: () => ({ left: 290, top: 180, bottom: 200, width: 120, height: 60 }) };
    return node;
  };
  const target = element(); target.dataset.tooltipSource = "source";
  const source = { textContent: "Projected full description" };
  const document = { body: { append: node => nodes.push(node) },
    documentElement: { clientWidth: 320, clientHeight: 240 },
    createElement: element, querySelectorAll: () => [target], getElementById: () => source,
    addEventListener: (key, fn) => { events[key] = fn; } };
  const context = vm.createContext({ document,
    setTimeout: (fn, delay) => { timers.set(++next, { fn, delay }); return next; },
    clearTimeout: id => timers.delete(id), addEventListener: (key, fn) => { events[key] = fn; },
    MutationObserver: class { constructor(fn) { observe = fn; } observe() {} } });
  vm.runInContext(fs.readFileSync(path.join(root, "tooltips.js"), "utf8").replace(/^export /gm, ""), context);
  vm.runInContext("initializeTooltips()", context);
  return { context, target, source, events, timers, tooltip: nodes[0], observe: () => observe(),
    tick: () => { const pending = [...timers.values()]; timers.clear(); pending.forEach(timer => timer.fn()); } };
}

test("shared tooltip delays hover, cancels fleeting hover and reads current projected content", () => {
  const h = harness();
  h.target.listeners.pointerenter({ pointerType: "mouse" });
  assert.equal(h.tooltip.hidden, true);
  assert.equal([...h.timers.values()][0].delay, 180);
  h.target.listeners.pointerleave(); h.tick(); assert.equal(h.tooltip.hidden, true);
  h.source.textContent = "New projection";
  h.target.listeners.pointerenter({ pointerType: "mouse" }); h.tick();
  assert.equal(h.tooltip.textContent, "New projection");
  assert.equal(h.tooltip.attributes.role, "tooltip");
  assert.equal(h.target.attributes["aria-describedby"], "shared-tooltip");
  assert.equal(h.tooltip.style.left, "192px"); assert.equal(h.tooltip.style.top, "112px");
});

test("keyboard focus reveals and Escape dismisses without losing existing descriptions", () => {
  const h = harness(); h.target.setAttribute("aria-describedby", "existing");
  h.target.listeners.focus(); h.tick(); assert.equal(h.tooltip.hidden, false);
  assert.equal(h.target.attributes["aria-describedby"], "existing shared-tooltip");
  h.events.keydown({ key: "Escape" });
  assert.equal(h.tooltip.hidden, true); assert.equal(h.target.attributes["aria-describedby"], "existing");
  h.target.listeners.focus(); h.tick(); h.target.listeners.blur(); assert.equal(h.tooltip.hidden, true);
});

for (const event of ["pointerdown", "scroll", "resize", "toggle"]) test(`${event} dismisses tooltip and pending reveal`, () => {
  const h = harness(); h.target.listeners.focus(); h.events[event](); h.tick();
  assert.equal(h.tooltip.hidden, true);
  h.target.listeners.focus(); h.tick(); h.events[event](); assert.equal(h.tooltip.hidden, true);
});

test("touch does not produce a sticky hover and never consumes the explained click", () => {
  const h = harness(); h.target.listeners.pointerenter({ pointerType: "touch" }); h.tick();
  assert.equal(h.tooltip.hidden, true); assert.equal(h.target.listeners.click, undefined);
  let cancelled = false;
  h.events.pointerdown({ preventDefault: () => { cancelled = true; } }); assert.equal(cancelled, false);
  const css = fs.readFileSync(path.join(root, "styles.css"), "utf8");
  assert.match(css, /\.shared-tooltip\s*\{[^}]*pointer-events: none/);
  const html = fs.readFileSync(path.join(root, "index.html"), "utf8");
  assert.match(html, /<details class="scenario-info"><summary[^>]*aria-label="Scenario information"/);
  assert.match(html, /<details class="setting-help"><summary[^>]*>About automatic choices/);
});

test("replaced targets cannot leave a visible or pending tooltip behind", () => {
  const h = harness(); h.target.listeners.focus(); h.target.isConnected = false; h.tick();
  assert.equal(h.tooltip.hidden, true);
  h.target.isConnected = true; h.target.listeners.focus(); h.tick(); h.target.isConnected = false;
  h.observe(); assert.equal(h.tooltip.hidden, true);
});

test("positioning contains popovers at every viewport corner and on narrow screens", () => {
  const h = harness();
  for (const width of [240, 1440]) for (const height of [200, 900]) {
    for (const left of [0, width - 10]) for (const top of [0, height - 10]) {
      const size = { width: Math.min(360, width - 16), height: 130 };
      h.context.geometry = [{ left, top, bottom: top + 10 }, size, { width, height }];
      const result = vm.runInContext("tooltipPosition(...geometry)", h.context);
      assert.ok(result.left >= 8 && result.left + size.width <= width - 8);
      assert.ok(result.top >= 8 && result.top + size.height <= height - 8);
    }
  }
});

test("Tools keeps every existing control once, in its native collapsible section", () => {
  const html = fs.readFileSync(path.join(root, "index.html"), "utf8");
  const sections = new Map(), stack = [];
  for (const token of html.matchAll(/<\/?details\b[^>]*>|<[^>]+\bid="[^"]+"[^>]*>/g)) {
    const tag = token[0];
    if (tag.startsWith("</details")) { stack.pop(); continue; }
    const id = tag.match(/\bid="([^"]+)"/)?.[1];
    if (id) { assert.ok(!sections.has(id), `duplicate ${id}`); sections.set(id, [...stack]); }
    if (tag.startsWith("<details")) stack.push(id ?? "nested");
  }
  for (const [section, controls] of Object.entries({
    "game-setup": ["scenario", "side-controllers", "start-scenario", "restart-scenario", "scenario-transport", "import-scenario"],
    "tools-display": ["board-view", "fit-board", "coordinates", "waypoints", "animate"],
    "tools-gameplay": ["objectives", "orders", "filter", "auto", "round", "skip", "world-effects", "controller-status", "controller-retry", "controller-cancel"],
    "tools-developer": ["events", "unit-debug", "decision-commentary", "commentary-toggle", "commentary-panel", "refresh"]
  })) for (const id of controls) assert.ok(sections.get(id)?.includes(section), `${id} belongs in ${section}`);
  for (const id of ["mode-playtest", "mode-designer"]) assert.deepEqual(sections.get(id), ["playtest-tools"]);
  assert.match(html, /<details id="game-setup"[^>]* open>/);
  for (const title of ["Game", "Display", "Gameplay", "Developer"]) assert.ok(html.includes(`<summary>${title}</summary>`));
  for (const id of ["tools-display", "tools-gameplay", "tools-developer"])
    assert.match(html, new RegExp(`<details id="${id}" class="tools-section">`));
  assert.match(html, /<option value="enhanced" selected>/);
});
