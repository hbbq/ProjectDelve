import { DesignerDraft, DesignerRequests, sameCell } from "./designer-state.js";
import { styleSide } from "./side-colors.js";

const element = (tag, value = "") => {
  const node = document.createElement(tag); node.textContent = value; return node;
};
const button = (label, action) => {
  const node = element("button", label); node.type = "button"; node.addEventListener("click", action); return node;
};
function options(select, entries) {
  select.replaceChildren(...entries.map(entry => {
    const node = element("option", entry.label ?? entry); node.value = entry.value ?? entry; return node;
  }));
}
function field(form, label, value, { entries, type = "text", list } = {}) {
  const wrapper = element("label", label);
  const input = element(entries ? "select" : "input");
  if (entries) options(input, entries); else input.type = type;
  if (list) input.setAttribute("list", list);
  input.value = value ?? ""; wrapper.append(input); form.append(wrapper); return input;
}

// A renderer for drafts, deliberately separate from GameState, choices and event playback.
export function mountDesigner(catalog, request, playDesign) {
  const ids = ["main", "board", "board-form", "world-form", "world-enabled", "world-draws", "world-cycling", "width", "height", "default", "tools", "terrain", "edge", "type", "side", "controller",
    "terrain-label", "edge-label", "unit-brush", "help", "hover", "status", "errors", "properties", "order", "declare-form", "declare-type",
    "agency", "agency-form", "agency-type", "agency-side", "agency-controller", "placements", "sides", "transport", "validate", "export", "play", "import", "copy"];
  const ui = Object.fromEntries(ids.map(id => [id, document.getElementById(`designer-${id}`)]));
  const draft = new DesignerDraft(catalog);
  let tool = "Select", selection = null, painting = false, lastPaint = null, pendingPlay = false;
  const typeOptions = catalog.unitTypes.map(type => ({ value: type.unitTypeId, label: type.displayName }));
  const terrainOptions = [{ value: "", label: "Use default (remove override)" }, ...catalog.terrains];
  const edgeOptions = [{ value: "", label: "Remove" }, ...catalog.edgeKinds.filter(kind => kind !== "None")];
  options(ui.default, catalog.terrains); ui.default.value = draft.definition.board.defaultTerrain;
  options(ui.terrain, terrainOptions); options(ui.edge, edgeOptions); ui.edge.value = "Wall";
  for (const select of [ui.type, ui["declare-type"], ui["agency-type"]]) options(select, typeOptions);
  for (const select of [ui.controller, ui["agency-controller"]]) options(select, catalog.controllers);

  function setMode(designer) {
    document.getElementById("playtest-main").hidden = designer;
    ui.main.hidden = !designer;
    document.getElementById("mode-designer").setAttribute("aria-pressed", String(designer));
    document.getElementById("mode-playtest").setAttribute("aria-pressed", String(!designer));
    if (designer) render();
  }
  document.getElementById("mode-designer").addEventListener("click", () => setMode(true));
  document.getElementById("mode-playtest").addEventListener("click", () => setMode(false));

  function report(result, operation) {
    ui.status.textContent = result.valid ? "Valid · checked by the engine" : operation === "import"
      ? "Import failed · current draft retained" : "Invalid · draft remains editable";
    ui.errors.replaceChildren(...result.errors.map(error => {
      const row = element("p", error.message);
      if (error.path) row.append(button(`Select ${error.path}`, () => selectPath(error.path)));
      return row;
    }));
  }
  const requests = new DesignerRequests(draft, request, report);
  function unchecked() {
    ui.status.textContent = "Unchecked · validate before export or play";
    ui.errors.replaceChildren();
    ui.transport.value = "";
  }
  function commit(action) {
    try { action(); unchecked(); render(); }
    catch (error) {
      report({ valid: false, errors: [{ message: error.message }] });
      ui.status.textContent = "Edit refused · current draft retained";
    }
  }
  function syncBrushAgency() {
    const row = draft.definition.agency.find(row => row.unitTypeId === ui.type.value && row.sideId === ui.side.value);
    ui.controller.disabled = !!row;
    if (row) ui.controller.value = row.controller;
    ui.controller.title = row ? "Existing pair: change its controller in the Agency table." : "Controller for a new Type + Side pair.";
  }
  ui.type.addEventListener("change", syncBrushAgency); ui.side.addEventListener("input", syncBrushAgency);
  ui["board-form"].addEventListener("submit", event => {
    event.preventDefault();
    commit(() => {
      draft.resize(Number(ui.width.value), Number(ui.height.value));
      draft.definition.board.defaultTerrain = ui.default.value;
    });
    // Restore fields after a refused shrink.
    syncBoardFields();
  });
  ui["world-enabled"].addEventListener("change", () => {
    ui["world-draws"].disabled = ui["world-cycling"].disabled = !ui["world-enabled"].checked;
  });
  ui["world-form"].addEventListener("submit", event => {
    event.preventDefault();
    commit(() => draft.setWorldEffects(ui["world-enabled"].checked,
      Number(ui["world-draws"].value), Number(ui["world-cycling"].value)));
  });
  function syncWorldFields() {
    const world = draft.definition.worldEffects;
    ui["world-enabled"].checked = !!world;
    ui["world-draws"].value = world?.cardsPerRound ?? 1;
    ui["world-cycling"].value = world?.cycling ?? 1;
    ui["world-draws"].disabled = ui["world-cycling"].disabled = !world;
  }
  function syncBoardFields() {
    ui.width.value = draft.definition.board.width; ui.height.value = draft.definition.board.height;
    ui.default.value = draft.definition.board.defaultTerrain;
  }
  function setTool(value) {
    tool = value; ui.board.dataset.tool = tool;
    for (const node of ui.board.querySelectorAll(".designer-edge-hit")) node.tabIndex = tool === "Edge" || tool === "Select" ? 0 : -1;
    for (const node of ui.tools.querySelectorAll("button")) node.setAttribute("aria-pressed", String(node.dataset.tool === tool));
    ui["terrain-label"].hidden = tool !== "Terrain"; ui["edge-label"].hidden = tool !== "Edge";
    ui["unit-brush"].hidden = tool !== "Unit";
    ui.help.textContent = {
      Select: "Select a cell, edge or Unit to edit. Overlapping Units appear in a chooser and placement list.",
      Terrain: "Click or drag cells to paint. Use default removes the explicit override.",
      Edge: "Click or drag internal boundaries. The highlighted segment is the paint target; Remove erases it.",
      Unit: "Click the top-left anchor to place. Supplied footprint geometry is a preview; Validate checks legality."
    }[tool];
  }
  for (const node of ui.tools.querySelectorAll("button")) node.addEventListener("click", () => setTool(node.dataset.tool));

  function hit(event) {
    const node = event.target.closest("[data-editor-hit]");
    if (!node) return null;
    return { node, position: { x: Number(node.dataset.x), y: Number(node.dataset.y) }, direction: node.dataset.direction };
  }
  function paint(target) {
    const key = `${target.position.x},${target.position.y},${target.direction ?? ""}`;
    if (lastPaint === key) return;
    if (tool === "Terrain" && !target.direction) {
      draft.paintTerrain(target.position, ui.terrain.value || null);
      target.node.className = `cell ${draft.terrainAt(target.position)}`;
    } else if (tool === "Edge" && target.direction) {
      draft.paintEdge(target.position, target.direction, ui.edge.value || null);
      target.node.className = `edge designer-edge-hit ${ui.edge.value || "None"}`;
    } else return;
    lastPaint = key; unchecked();
  }
  ui.board.addEventListener("pointerdown", event => {
    if (event.button !== 0 || pendingPlay) return;
    const target = hit(event); if (!target) return;
    event.preventDefault();
    if (tool === "Terrain" || tool === "Edge") { painting = true; lastPaint = null; paint(target); }
    else if (tool === "Unit" && !target.direction) commit(() => {
      selection = { kind: "unit", key: draft.place(ui.type.value, ui.side.value, ui.controller.value, target.position) };
    });
    else if (tool === "Select") selectTarget(target);
  });
  ui.board.addEventListener("pointermove", event => {
    const target = hit(event);
    for (const node of ui.board.querySelectorAll(".placement-preview")) node.classList.remove("placement-preview");
    if (!target) return;
    ui.hover.textContent = target.direction ? `Edge (${target.position.x}, ${target.position.y}) ${target.direction}`
      : `Cell (${target.position.x}, ${target.position.y}) · ${draft.terrainAt(target.position)}`;
    if (tool === "Unit" && !target.direction) {
      const preview = draft.occupied({ unitTypeId: ui.type.value, anchor: target.position });
      for (const cell of preview) cellNodes.get(`${cell.x},${cell.y}`)?.classList.add("placement-preview");
    }
    if (painting && event.buttons === 1) paint(target);
  });
  document.addEventListener("pointerup", () => {
    if (painting) { painting = false; lastPaint = null; render(); }
  });
  document.addEventListener("pointercancel", () => { painting = false; lastPaint = null; render(); });
  ui.board.addEventListener("keydown", event => {
    if (!["Enter", " "].includes(event.key) || pendingPlay) return;
    const target = hit(event); if (!target) return;
    event.preventDefault();
    if (tool === "Select") selectTarget(target);
    else if (tool === "Unit" && !target.direction) commit(() => {
      selection = { kind: "unit", key: draft.place(ui.type.value, ui.side.value, ui.controller.value, target.position) };
    });
    else { lastPaint = null; paint(target); render(); }
  });
  function selectTarget(target) {
    if (target.direction) selection = { kind: "edge", position: target.position, direction: target.direction };
    else {
      const units = draft.unitsAt(target.position);
      selection = units.length === 1 ? { kind: "unit", key: units[0] }
        : units.length > 1 ? { kind: "overlap", keys: units, position: target.position }
        : { kind: "cell", position: target.position };
    }
    render();
  }
  function selectPath(path) {
    const match = path.replace(/^\$\.?/, "").match(/^(units|board.cells|board.edges|agency)\[(\d+)\]/);
    if (!match) return;
    const index = Number(match[2]);
    if (match[1] === "units" && draft.unitKeys[index]) selection = { kind: "unit", key: draft.unitKeys[index] };
    if (match[1] === "board.cells" && draft.definition.board.cells[index])
      selection = { kind: "cell", position: draft.definition.board.cells[index].position };
    if (match[1] === "board.edges" && draft.definition.board.edges[index])
      selection = { kind: "edge", ...draft.definition.board.edges[index] };
    if (match[1] === "agency") ui.agency.children[index]?.scrollIntoView?.({ block: "nearest" });
    setTool("Select"); renderBoard(); renderProperties();
  }

  const cellNodes = new Map();
  function renderBoard() {
    const board = draft.definition.board; ui.board.replaceChildren(); cellNodes.clear();
    ui.board.style.gridTemplateColumns = `repeat(${board.width}, minmax(0, 1fr))`;
    ui.board.style.gridTemplateRows = `repeat(${board.height}, minmax(0, 1fr))`;
    ui.board.style.setProperty("--columns", board.width);
    ui.board.style.setProperty("--board-ratio", board.width / board.height);
    ui.board.style.aspectRatio = `${board.width} / ${board.height}`;
    for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
      const cell = { x, y }, node = element("div");
      node.className = `cell ${draft.terrainAt(cell)}`;
      node.dataset.editorHit = "cell"; node.dataset.x = x; node.dataset.y = y;
      node.tabIndex = 0; node.setAttribute("role", "button");
      node.setAttribute("aria-label", `Cell ${x},${y}: ${draft.terrainAt(cell)}`);
      node.title = `${x},${y} · ${draft.terrainAt(cell)}`;
      node.append(element("span", `${x},${y}`));
      if (selection?.kind === "cell" && sameCell(selection.position, cell)) node.classList.add("editor-selected");
      ui.board.append(node); cellNodes.set(`${x},${y}`, node);
    }
    const edges = new Map(board.edges.map(edge => [`${edge.position.x},${edge.position.y},${edge.direction}`, edge]));
    for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) for (const direction of ["Right", "Down"]) {
      if (direction === "Right" && x === board.width - 1 || direction === "Down" && y === board.height - 1) continue;
      const edge = edges.get(`${x},${y},${direction}`), node = element("div");
      node.className = `edge designer-edge-hit ${edge?.kind ?? "None"}`;
      node.dataset.editorHit = "edge"; node.dataset.x = x; node.dataset.y = y; node.dataset.direction = direction;
      node.tabIndex = tool === "Edge" || tool === "Select" ? 0 : -1;
      node.setAttribute("role", "button"); node.setAttribute("aria-label", `Edge ${x},${y} ${direction}: ${edge?.kind ?? "empty"}`);
      node.title = `(${x},${y}) ${direction} · ${edge?.kind ?? "empty"}`;
      node.style.left = `${(x + (direction === "Right" ? 1 : .5)) / board.width * 100}%`;
      node.style.top = `${(y + (direction === "Down" ? 1 : .5)) / board.height * 100}%`;
      node.style.width = direction === "Right" ? "var(--edge-thickness)" : `${100 / board.width}%`;
      node.style.height = direction === "Down" ? "var(--edge-thickness)" : `${100 / board.height}%`;
      if (selection?.kind === "edge" && selection.direction === direction && sameCell(selection.position, { x, y }))
        node.classList.add("editor-selected");
      ui.board.append(node);
    }
    for (const [index, unit] of draft.definition.units.entries()) {
      const metadata = draft.metadata(unit.unitTypeId), span = metadata?.cellSpan ?? 1;
      const name = metadata?.displayName ?? unit.unitTypeId, node = element("div");
      node.className = `figure${span > 1 ? " large" : ""}${unit.posture === "Lying" ? " lying" : ""}`;
      node.dataset.unitKey = draft.unitKeys[index]; node.dataset.cellSpan = span;
      if (selection?.kind === "unit" && selection.key === draft.unitKeys[index]) node.classList.add("selected");
      const label = element("span", name.split(/\s+/).map(word => word[0]).join("").slice(0, 3) + (index + 1));
      label.className = "figure-name"; node.append(label);
      node.style.left = `${(unit.anchor.x + span / 2) / board.width * 100}%`;
      node.style.top = `${(unit.anchor.y + span / 2) / board.height * 100}%`;
      node.style.width = `${(span > 1 ? span * 95 : 70) / board.width}%`;
      styleSide(node, unit.sideId);
      node.title = `${name} · ${unit.sideId} · ${unit.posture} · HP ${unit.initialHp ?? metadata?.maxHp}`;
      ui.board.append(node);
    }
  }
  function renderProperties() {
    ui.properties.replaceChildren();
    if (!selection) { ui.properties.textContent = "Nothing selected."; return; }
    if (selection.kind === "overlap") {
      ui.properties.append(element("p", "Overlapping placements: choose a Unit."));
      for (const key of selection.keys) ui.properties.append(button(unitLabel(draft.unitIndex(key)), () => {
        selection = { kind: "unit", key }; render();
      }));
      ui.properties.append(button("Edit terrain below", () => { selection = { kind: "cell", position: selection.position }; render(); }));
      return;
    }
    const form = element("form"); form.className = "designer-property-form";
    if (selection.kind === "unit") {
      const key = selection.key, unit = draft.definition.units[draft.unitIndex(key)];
      if (!unit) { selection = null; renderProperties(); return; }
      const type = field(form, "Unit Type", unit.unitTypeId, { entries: typeOptions });
      const side = field(form, "SideId", unit.sideId, { list: "designer-sides" });
      const x = field(form, "Anchor X", unit.anchor.x, { type: "number" });
      const y = field(form, "Anchor Y", unit.anchor.y, { type: "number" });
      const posture = field(form, "Posture", unit.posture, { entries: catalog.postures });
      const hp = field(form, `Initial HP (blank = full; max ${draft.metadata(unit.unitTypeId)?.maxHp ?? "?"})`, unit.initialHp, { type: "number" });
      const agency = draft.definition.agency.find(row => row.unitTypeId === unit.unitTypeId && row.sideId === unit.sideId);
      const controller = field(form, "Controller for new Type + Side", agency?.controller ?? ui.controller.value, { entries: catalog.controllers });
      const sync = () => {
        const existing = draft.definition.agency.find(row => row.unitTypeId === type.value && row.sideId === side.value);
        controller.disabled = !!existing; if (existing) controller.value = existing.controller;
      };
      type.addEventListener("change", sync); side.addEventListener("input", sync); sync();
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.editUnit(key,
        { unitTypeId: type.value, sideId: side.value, anchor: { x: Number(x.value), y: Number(y.value) },
          posture: posture.value, initialHp: hp.value === "" ? null : Number(hp.value) }, controller.value)); });
      form.append(element("button", "Apply Unit"));
      form.append(button("Delete Unit", () => commit(() => { draft.removeUnit(key); selection = null; })));
      form.append(button("Edit terrain at anchor", () => { selection = { kind: "cell", position: unit.anchor }; render(); }));
    } else if (selection.kind === "cell") {
      const position = { ...selection.position };
      form.append(element("p", `Cell (${position.x}, ${position.y})`));
      const explicit = draft.definition.board.cells.find(cell => sameCell(cell.position, position));
      const terrain = field(form, "Override", explicit?.terrain ?? "", { entries: terrainOptions });
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.paintTerrain(position, terrain.value || null)); });
      form.append(element("button", "Apply terrain"));
    } else if (selection.kind === "edge") {
      const position = { ...selection.position }, direction = selection.direction;
      const existing = draft.definition.board.edges.find(edge => sameCell(edge.position, position) && edge.direction === direction);
      form.append(element("p", `Edge (${position.x}, ${position.y}) ${direction}`));
      // Explicit None survives import; the property panel can distinguish it from absence.
      const kind = field(form, "Kind", existing?.kind ?? "", { entries: [...edgeOptions, "None"] });
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.paintEdge(position, direction, kind.value || null)); });
      form.append(element("button", "Apply edge"));
    }
    ui.properties.append(form);
  }
  function unitLabel(index) {
    const unit = draft.definition.units[index];
    return `${index + 1}. ${draft.metadata(unit.unitTypeId)?.displayName ?? unit.unitTypeId} · ${unit.sideId} @ ${unit.anchor.x},${unit.anchor.y}`;
  }
  function renderData() {
    ui.order.replaceChildren(...draft.definition.unitTypeIds.map(id => element("li", `${draft.metadata(id)?.displayName ?? id} (${id})`)));
    const sides = [...new Set([...draft.definition.units, ...draft.definition.agency].map(row => row.sideId))];
    ui.sides.replaceChildren(...sides.map(side => { const option = element("option"); option.value = side; return option; }));
    ui.agency.replaceChildren(...draft.definition.agency.map((row, index) => {
      const form = element("form"); form.className = "designer-agency-row designer-property-form";
      const type = field(form, "Type", row.unitTypeId, { entries: typeOptions });
      const side = field(form, "SideId", row.sideId, { list: "designer-sides" });
      const controller = field(form, "Controller", row.controller, { entries: catalog.controllers });
      const count = draft.definition.units.filter(unit => unit.unitTypeId === row.unitTypeId && unit.sideId === row.sideId).length;
      form.append(element("small", `${count} initial Units`));
      form.append(element("button", "Apply pair"));
      form.append(button("Remove pair", () => commit(() => draft.removeAgency(index))));
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => {
        draft.declare(type.value);
        draft.definition.agency[index] = { unitTypeId: type.value, sideId: side.value, controller: controller.value };
        draft.changed();
      }); });
      return form;
    }));
    ui.placements.replaceChildren(...draft.unitKeys.map((key, index) => button(unitLabel(index), () => {
      selection = { kind: "unit", key }; setTool("Select"); render();
    })));
    syncBrushAgency();
  }
  function render() { syncWorldFields(); renderBoard(); renderProperties(); renderData(); }
  ui["declare-form"].addEventListener("submit", event => { event.preventDefault(); commit(() => { draft.declare(ui["declare-type"].value); draft.changed(); }); });
  ui["agency-form"].addEventListener("submit", event => { event.preventDefault(); commit(() =>
    draft.setAgency(ui["agency-type"].value, ui["agency-side"].value, ui["agency-controller"].value)); });
  ui.validate.addEventListener("click", () => requests.run("validate", draft.snapshot()));
  ui.export.addEventListener("click", async () => {
    const result = await requests.run("export", draft.snapshot());
    if (result?.transport) { ui.transport.value = result.transport; ui.transport.closest("details").open = true; }
  });
  ui.import.addEventListener("click", async () => {
    const result = await requests.run("import", { transport: ui.transport.value.trim() });
    if (result?.board) {
      try { draft.replace(result); selection = null; syncBoardFields(); render(); report({ valid: true, errors: [] }); }
      catch (error) { report({ valid: false, errors: [{ message: error.message }] }, "import"); }
    }
  });
  ui.play.addEventListener("click", async () => {
    if (pendingPlay) return;
    const revision = draft.revision;
    const result = await requests.run("export", draft.snapshot());
    if (!result?.transport || revision !== draft.revision) return;
    pendingPlay = true; ui.play.disabled = true;
    // Block editing only during game replacement, so the exported version is the one being played.
    ui.main.inert = true;
    try {
      if (await playDesign(result.transport)) { ui.transport.value = result.transport; setMode(false); }
      else report({ valid: false, errors: [{ message: "Could not start the design. Check Playtest status, refresh and try again." }] });
    } catch (error) { report({ valid: false, errors: [{ message: error.message }] }); }
    finally { pendingPlay = false; ui.play.disabled = false; ui.main.inert = false; }
  });
  ui.copy.addEventListener("click", async () => {
    try { await navigator.clipboard.writeText(ui.transport.value); }
    catch { ui.transport.focus(); ui.transport.select(); }
  });
  setTool("Select"); syncBoardFields(); render();
  return { draft, render, setTool, setMode, requests, selectTarget, ui };
}

async function startDesigner() {
  try {
    const response = await fetch("/api/designer/catalog", { cache: "no-store" });
    if (!response.ok) throw new Error("Could not load designer catalog. Reload to retry.");
    mountDesigner(await response.json(), async (operation, body) => {
      const response = await fetch(`/api/designer/${operation}`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
      const result = await response.json();
      if (!response.ok) { const error = new Error(result.detail ?? "Designer request failed."); error.errors = result.errors; throw error; }
      return result;
    }, transport => globalThis.delvePlayDesign(transport));
  } catch (error) {
    document.getElementById("designer-status").textContent = error.message;
    document.getElementById("mode-designer").addEventListener("click", () => {
      document.getElementById("playtest-main").hidden = true;
      document.getElementById("designer-main").hidden = false;
    });
  }
}
startDesigner();
