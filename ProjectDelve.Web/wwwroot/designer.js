import { DesignerDraft, DesignerRequests, DesignerHistory, draftDocument, readDraftDocument, draftStorageKey, sameCell } from "./designer-state.js";
import { styleSide } from "./side-colors.js";
import { prepareCover } from "./cover-upload.js";

const element = (tag, value = "") => {
  const node = document.createElement(tag); node.textContent = value;
  if (tag === "button") node.dataset.action = value.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  return node;
};
const button = (label, action) => {
  const node = element("button", label); node.type = "button"; node.addEventListener("click", action); return node;
};
function options(select, entries) {
  select.replaceChildren(...entries.map(entry => {
    const node = element("option", entry.label ?? entry); node.value = entry.value ?? entry; return node;
  }));
}
function globalField(form, label, value, { entries, type = "text", list } = {}) {
  const wrapper = element("label", label);
  const input = element(entries ? "select" : "input");
  if (entries) options(input, entries); else input.type = type;
  if (entries && value != null && value !== "" && !entries.some(entry => String(entry.value ?? entry) === String(value))) {
    const missing = element("option", `${value} (unavailable)`); missing.value = value; input.append(missing);
  }
  if (type === "number") { input.type = "text"; input.inputMode = "numeric"; input.dataset.number = "true"; }
  input.setAttribute("aria-label", label);
  input.dataset.field = label.toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-|-$/g, "");
  if (list) input.setAttribute("list", list);
  input.value = value ?? ""; wrapper.append(input); form.append(wrapper); return input;
}

// A renderer for drafts, deliberately separate from GameState, choices and event playback.
export function mountDesigner(catalog, request, playDesign, projectMetadata = body => request("metadata", body)) {
  const ids = ["main", "board", "board-form", "world-form", "world-enabled", "world-draws", "world-cycling", "width", "height", "default", "tools", "terrain", "edge", "type", "side", "controller",
    "terrain-label", "edge-label", "unit-brush", "machine-label", "machine-type", "help", "hover", "status", "errors", "properties", "order", "declare-form", "declare-type",
    "rail-direction", "side-settings", "agency", "routes", "agency-form", "agency-type", "agency-side", "agency-controller", "placements", "sides", "unit-ids", "transport", "validate", "export", "play", "import", "copy",
    "title", "save-status", "pending", "undo", "redo", "save", "open", "file", "new", "catalog", "catalog-open", "zoom", "resource-label", "resource-type",
    "library-save", "library-copy", "library-open", "library-refresh", "library-select", "library-status", "library-cover", "cover-file", "cover-import", "cover-preview", "library-restore", "library-restore-confirm", "library-restore-apply", "library-restore-cancel"];
  const ui = Object.fromEntries(ids.map(id => [id, document.getElementById(`designer-${id}`)]));
  const draft = new DesignerDraft(catalog);
  const history = new DesignerHistory(draft);
  let buffers = {}, activeForm = null, recovering = true, persistenceBlocked = false;
  let savedRecovery = null;
  let lastExport = "";
  let generationSource = null;
  let librarySource = null, libraryBusy = false, librarySequence = 0, libraryOpening = 0;
  // Opening a different document is undoable. Its identity must travel with its
  // content so Undo cannot accidentally turn Save into an overwrite of another scenario.
  const captureHistory = history.capture.bind(history), restoreHistory = history.restore.bind(history);
  history.capture = () => ({ ...captureHistory(), libraryContext: structuredClone({
    source: librarySource, title: ui.title.value, coverArt: ui["library-cover"]?.value ?? "", generationSource }) });
  history.restore = state => {
    const { libraryContext, ...content } = state;
    restoreHistory(content);
    if (libraryContext && libraryContext.source?.id !== librarySource?.id) {
      librarySource = structuredClone(libraryContext.source); ui.title.value = libraryContext.title;
      if (ui["library-cover"]) ui["library-cover"].value = libraryContext.coverArt;
      generationSource = structuredClone(libraryContext.generationSource); librarySequence++; describeLibrary();
    }
  };
  let recoveryMessage = "New draft · saved in this browser";
  try {
    const saved = savedRecovery = globalThis.localStorage?.getItem(draftStorageKey);
    if (saved) {
      const doc = readDraftDocument(saved, catalog.maxBoardSize);
      draft.replace(doc.definition); restoreKeys(doc); buffers = doc.buffers ?? {}; ui.title.value = doc.title; ui.transport.value = doc.transportInput ?? "";
      generationSource = doc.generationSource ?? null;
      librarySource = doc.librarySource ?? null;
      if (ui["library-cover"]) ui["library-cover"].value = doc.coverArt ?? "";
      recoveryMessage = "Recovered draft · saved in this browser";
    }
  } catch (error) { persistenceBlocked = true; recoveryMessage = `Recovery unavailable: ${error.message} ${savedRecovery ? "Original recovery data retained. Download it before New/Open." : "Save draft to keep a file."}`; }
  if (!ui.title.value) ui.title.value = "Untitled scenario";
  function persist() {
    if (recovering) return;
    updateDocumentState();
    if (persistenceBlocked) return;
    try {
      if (!globalThis.localStorage) throw new Error("Browser storage unavailable");
      globalThis.localStorage.setItem(draftStorageKey, JSON.stringify(documentData()));
      ui["save-status"].textContent = "Saved in this browser · " + new Date().toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
    } catch { ui["save-status"].textContent = "Browser recovery unavailable · Save draft to keep a file"; }
  }
  function documentData() {
    return { ...draftDocument(draft.snapshot(), ui.title.value, buffers), transportInput: ui.transport.value === lastExport ? "" : ui.transport.value,
      generationSource, librarySource, coverArt: ui["library-cover"]?.value ?? "",
      editorKeys: { unitKeys: [...draft.unitKeys], machineKeys: [...draft.machineKeys], nextKey: draft.nextKey } };
  }
  function restoreKeys(doc) {
    if (!doc.editorKeys) return;
    draft.unitKeys = [...doc.editorKeys.unitKeys]; draft.machineKeys = [...doc.editorKeys.machineKeys]; draft.nextKey = doc.editorKeys.nextKey;
  }
  function formLabel(key) {
    if (key.startsWith("selection:")) {
      try { const s = JSON.parse(key.slice(10)); return s.position ? `${s.kind} (${s.position.x}, ${s.position.y})` : `${s.kind} properties`; } catch {}
    }
    if (key === "board-form") return "Board setup";
    if (key === "world-form") return "World Effects";
    if (key === "agency-form") return "New group";
    if (key.startsWith("agency:")) { try { return JSON.parse(key.slice(7)).join(" · "); } catch {} }
    return key.replace(":", " · ");
  }
  function updateDocumentState() {
    const generationLabel = document.getElementById("designer-generation-source");
    if (generationLabel) {
      generationLabel.hidden = !generationSource;
      generationLabel.textContent = generationSource ? `${JSON.stringify(draft.definition) === generationSource.definitionJson ? "Original generated scenario" : "Modified scenario · code reproduces the original only"} · ${generationSource.code}` : "";
    }
    ui.undo.disabled = !history.past.length; ui.redo.disabled = !history.future.length;
    const count = Object.keys(buffers).length;
    ui.pending.textContent = count ? `${count} unfinished form${count > 1 ? "s" : ""} retained. Apply or discard before Validate, Export or Test. Save draft includes these inputs.`
      : "Inspector properties commit on change. Objectives, Board, World Effects, Groups and Routes use Apply / Save.";
    ui.pending.dataset.pending = String(!!count);
    document.getElementById("designer-discard-inputs").hidden = !count;
    if (count) for (const key of Object.keys(buffers)) ui.pending.append(button(`Review ${formLabel(key)}`, () => reviewForm(key)));
  }
  function reviewForm(key) {
    if (key.startsWith("selection:")) { try { selection = JSON.parse(key.slice(10)); } catch {} showPanel("inspect"); }
    else showPanel(key.startsWith("route:") || key.startsWith("waypoint:") ? "routes" : key.startsWith("side:") || key.startsWith("objective:") ? "sides" : key.startsWith("agency:") || key.startsWith("loot-type:") || key === "agency-form" ? "groups" : "board");
    if (key.startsWith("objective:")) objectiveSide = key.slice(10);
    render();
    for (const form of [...ui.main.querySelectorAll("form"), ...ui.main.querySelectorAll("details")]) if (form.dataset.formKey === key) {
      if (form.dataset.lootType) form.open = true;
      form.scrollIntoView?.({ block: "nearest" }); form.querySelector?.("input,select")?.focus();
    }
  }
  function editorForm(key, immediate = false) {
    const form = element("form"); form.className = "designer-property-form"; form.dataset.formKey = key;
    const listen = form.addEventListener.bind(form);
    form.addEventListener = (name, action) => listen(name, name === "submit" ? event => {
      event.preventDefault();
      if (!numbersComplete(form)) { ui.status.textContent = "Enter a whole number in each numeric field. Unfinished inputs are retained."; return; }
      activeForm = key;
      try { action(event); } finally { activeForm = null; }
    } : action);
    if (immediate) listen("change", () => {
      if (!numbersComplete(form)) { ui.status.textContent = "Invalid numeric input · finish the whole number or discard the retained inputs."; return; }
      form.requestSubmit?.();
    });
    if (buffers[key]) form.append(button("Discard unfinished inputs", () => { delete buffers[key]; persist(); render(); }));
    return form;
  }
  function field(form, label, value, config) {
    let root = form;
    while (!root.dataset.formKey && (root.parentElement ?? root.parent)) root = root.parentElement ?? root.parent;
    const key = root.dataset.formKey;
    const input = globalField(form, label, buffers[key]?.values?.[label] ?? value, config);
    if (config?.type === "number" && label.startsWith("Initial HP")) input.dataset.optional = "true";
    const remember = () => {
      if (!key) return;
      const buffer = buffers[key] ??= { values: {} }; buffer.values ??= {};
      buffer.values[label] = input.value; requests.sequence++; persist();
    };
    input.addEventListener("input", remember); input.addEventListener("change", remember);
    return input;
  }
  let tool = "Select", selection = null, painting = false, lastPaint = null, pendingPlay = false;
  let objectiveSide = null, inspectedObjective = null, highlightedGroup = null, picker = null, metadataSignature = null, metadataSequence = 0;
  const resourceSymbol = type => ({ coal: "⬢", diamond: "💎", fruit: "🍒", banana: "🍌", mushroom: "🍄", shell: "🐚" }[type.id] ?? type.name.slice(0, 2));
  const resourceTypes = catalog.resourceTypes ?? [];
  options(ui["resource-type"], resourceTypes.map(t => ({ value: t.id, label: `${resourceSymbol(t)} ${t.name}` })));
  const resourceTool = button("◆ Resources", () => setTool("Resource")); resourceTool.dataset.tool = "Resource"; ui.tools.append(resourceTool);
  const cancelPick = button("Cancel board pick (Esc)", () => { picker = null; setTool("Select"); renderBoard(); });
  cancelPick.hidden = true; ui.help.after?.(cancelPick);
  function beginPick(kind, label, apply, unitTypeId = null) {
    picker = { kind, label, apply, unitTypeId }; setTool("Pick");
    ui.board.dataset.pick = kind;
    ui.help.textContent = `${label} · ${kind === "area" ? "Drag a rectangle, or click two opposite corners." : kind === "edge" ? "Choose an internal edge." : "Choose a Cell."} Esc cancels.`;
    cancelPick.hidden = false; ui.board.scrollIntoView?.({ block: "nearest" });
  }
  function completePick(value) {
    const apply = picker.apply; picker = null; setTool("Select"); apply(value); renderBoard();
  }
  function pick(target) {
    if (!picker) return false;
    if (picker.kind === "edge") { if (target.direction) completePick(target); return true; }
    if (target.direction) return true;
    if (picker.kind !== "area") { completePick(target.position); return true; }
    if (!picker.start) { picker.start = target.position; picker.end = target.position; }
    else completePick(rectangle(picker.start, target.position));
    return true;
  }
  const rectangle = (a, b) => ({ x: Math.min(a.x, b.x), y: Math.min(a.y, b.y), w: Math.abs(a.x - b.x) + 1, h: Math.abs(a.y - b.y) + 1 });
  function bufferedValues(form, values) {
    const key = form.dataset.formKey;
    Object.assign((buffers[key] ??= { values: {} }).values ??= {}, values); requests.sequence++; persist(); render();
  }
  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && picker) { picker = null; setTool("Select"); renderBoard(); }
  });
  function refreshMetadata() {
    const custom = draft.definition.unitTypes ?? [];
    const body = { unitTypes: custom, terrainTypes: draft.definition.terrainTypes ?? [], units: draft.definition.units, machineIds: (draft.definition.machines ?? []).map(m => m.id) };
    const signature = JSON.stringify([custom, body.terrainTypes, body.units.map(u => [u.unitTypeId, u.sideId, u.unitId]), body.machineIds]);
    if (signature === metadataSignature) return;
    metadataSignature = signature; draft.effectiveUnitIds = null;
    draft.effectiveTerrainMetadata = null;
    if (draft.metadataContentSignature !== JSON.stringify(custom)) draft.effectiveMetadata = null;
    draft.metadataContentSignature = JSON.stringify(custom);
    const sequence = ++metadataSequence;
    Promise.resolve(projectMetadata(body)).then(result => {
      if (sequence !== metadataSequence) return;
      if (!Array.isArray(result?.unitTypes)) throw new Error("Custom metadata unavailable");
      draft.effectiveMetadata = result.unitTypes; draft.effectiveTerrainMetadata = result.terrainDefinitions;
      draft.effectiveUnitIds = result.unitIds; render();
    }).catch(() => { if (sequence === metadataSequence && custom.length) ui.status.textContent = "Custom content metadata unavailable. Definitions are retained; custom Units show anchors only. Validate for engine diagnostics."; });
  }
  const typeOptions = catalog.unitTypes.map(type => ({ value: type.unitTypeId, label: type.displayName }));
  function syncImportedTypes() {
    typeOptions.splice(0, typeOptions.length, ...catalog.unitTypes.map(type => ({ value: type.unitTypeId, label: draft.metadata(type.unitTypeId)?.displayName ?? type.unitTypeId })));
    for (const id of new Set([...draft.definition.unitTypeIds, ...draft.definition.units.map(u => u.unitTypeId)]))
      if (!typeOptions.some(t => t.value === id)) typeOptions.push({ value: id, label: draft.metadata(id)?.displayName ?? `${id} (imported · anchor preview)` });
    for (const select of [ui.type, ui["declare-type"], ui["agency-type"]]) { const value = select.value; options(select, typeOptions); if (typeOptions.some(t => t.value === value)) select.value = value; }
  }
  const terrainLabel = kind => kind === "SolidRock" ? { value: kind, label: "Solid Rock" } : kind;
  const terrainOptions = [];
  function syncTerrainTypes() {
    const entries = catalog.terrains.map(kind => ({ value: kind, label: draft.terrainMetadata(kind)?.name ?? (kind === 'SolidRock' ? 'Solid Rock' : kind) }));
    for (const type of draft.terrainCatalog())
      if (!type.legacyKind || draft.definition.board.defaultTerrainTypeId === type.id || draft.definition.board.cells.some(cell => cell.terrainTypeId === type.id))
        entries.push({ value: draft.terrainTypeSelection(type.id), label: type.name });
    // Invalid references remain visible and recoverable until authoritative validation.
    for (const id of [draft.definition.board.defaultTerrainTypeId, ...draft.definition.board.cells.map(cell => cell.terrainTypeId)].filter(Boolean))
      if (!entries.some(entry => entry.value === draft.terrainTypeSelection(id))) entries.push({ value: draft.terrainTypeSelection(id), label: `${id} (unresolved)` });
    terrainOptions.splice(0, terrainOptions.length, { value: '', label: 'Use default (remove override)' }, ...entries);
    const selected = ui.terrain.value;
    options(ui.terrain, terrainOptions);
    if (terrainOptions.some(entry => entry.value === selected)) ui.terrain.value = selected;
    const defaultSelection = ui.default.value;
    options(ui.default, entries.filter(entry => (draft.terrainMetadata(entry.value)?.legacyKind ?? entry.value) !== 'Rail'));
    ui.default.value = entries.some(entry => entry.value === defaultSelection) ? defaultSelection : draft.defaultTerrainSelection();
  }
  const edgeOptions = [{ value: "", label: "Remove" }, ...catalog.edgeKinds.filter(kind => kind !== "None")];
  options(ui["rail-direction"], catalog.railDirections ?? ["North", "East", "South", "West"]); ui["rail-direction"].value = "East";
  options(ui.zoom, [{value:"fit",label:"Fit board"},{value:"24",label:"Comfortable"},{value:"36",label:"Large"},{value:"48",label:"Extra large"}]); ui.zoom.value = "fit";
  ui.zoom.addEventListener("change", renderBoard);
  options(ui.default, catalog.terrains.filter(kind => kind !== "Rail").map(terrainLabel)); ui.default.value = draft.definition.board.defaultTerrain;
  options(ui.terrain, terrainOptions); options(ui.edge, edgeOptions); ui.edge.value = "Wall";
  options(ui["machine-type"], catalog.machineTypes.map(type => ({ value: type.id, label: type.name })));
  for (const select of [ui.type, ui["declare-type"], ui["agency-type"]]) options(select, typeOptions);
  for (const select of [ui.controller, ui["agency-controller"]]) options(select, catalog.controllers);

  function setMode(designer, notify = true) {
    document.getElementById("playtest-main").hidden = designer;
    ui.main.hidden = !designer;
    document.getElementById("mode-designer").setAttribute("aria-pressed", String(designer));
    document.getElementById("mode-playtest").setAttribute("aria-pressed", String(!designer));
    document.body?.classList.toggle("designing", designer);
    document.getElementById("playtest-tools").open = false;
    try { globalThis.localStorage?.setItem("project-delve.mode", designer ? "designer" : "playtest"); } catch {}
    if (designer) render();
    if (notify) globalThis.delveNavigate?.(designer ? "designer" : "play");
  }
  globalThis.delveSetDesignerMode = designer => setMode(designer, false);
  // The application shell owns navigation and its unsaved campaign guard.
  document.getElementById("mode-designer").addEventListener("click", () => { if (!globalThis.delveNavigate) setMode(true); });
  document.getElementById("mode-playtest").addEventListener("click", () => { if (!globalThis.delveNavigate) setMode(false); });

  function report(result, operation) {
    ui.status.dataset.validation = result.valid ? "valid" : "invalid";
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
    ui.status.dataset.validation = "unchecked";
    ui.status.textContent = "Unchecked · validate before export or play";
    ui.errors.replaceChildren();
    if (ui.transport.value === lastExport) ui.transport.value = "";
    lastExport = "";
  }
  function commit(action, discardKeys = []) {
    try {
      history.transact(action);
      if (activeForm) delete buffers[activeForm];
      for (const key of discardKeys) delete buffers[key];
      unchecked(); persist(); render(); return true;
    }
    catch (error) {
      report({ valid: false, errors: [{ message: error.message }] });
      ui.status.textContent = "Edit refused · current draft retained";
      persist(); return false;
    }
  }
  function syncBrushAgency() {
    const row = draft.definition.agency.find(row => row.unitTypeId === ui.type.value && row.sideId === ui.side.value);
    ui.controller.disabled = !!row;
    if (row) ui.controller.value = row.controller;
    ui.controller.title = row ? "Existing pair: change its controller in the Agency table." : "Controller for a new Type + Side pair.";
  }
  ui.type.addEventListener("change", syncBrushAgency); ui.side.addEventListener("input", syncBrushAgency);
  prepareStatic("board-form", ["width", "height", "default"]);
  prepareStatic("world-form", ["world-enabled", "world-draws", "world-cycling"]);
  prepareStatic("agency-form", ["agency-type", "agency-side", "agency-controller"]);
  ui["board-form"].addEventListener("submit", event => {
    event.preventDefault();
    if (commit(() => {
      draft.resize(Number(ui.width.value), Number(ui.height.value));
      draft.setDefaultTerrain(ui.default.value);
    })) syncBoardFields();
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
    const lootForm = editorForm('loot-deck'); lootForm.id = 'designer-loot-deck';
    lootForm.append(element('h2', 'Loot Deck'));
    const custom = !draft.definition.loot || draft.definition.loot.cards != null || draft.definition.loot.composition != null;
    const selected = custom ? 'Custom' : draft.definition.loot?.preset ?? catalog.defaultLootPreset ?? 'Classic';
    const selector = field(lootForm, 'Deck', selected, { entries: [...(catalog.lootPresets ?? []).map(p => ({value:p.id,label:p.name})), {value:'Custom',label:'Custom'}] });
    selector.id = 'designer-loot-preset';
    selector.addEventListener('change', () => { delete buffers['loot-deck']; commit(() => selector.value === 'Custom' ? draft.copyLootToCustom() : draft.setLootPreset(selector.value)); });
    const preset = catalog.lootPresets?.find(p => p.id === selected);
    lootForm.append(element('p', custom ? 'Custom composition. Card rules stay live; quantities belong to this scenario. An empty deck disables drops.' : 'Live preset. New games use its current composition; saving does not copy its cards.'));
    if (preset) lootForm.append(element('p', preset.description));
    const rows = draft.lootComposition();
    lootForm.append(element('p', `${rows.reduce((sum,r) => sum + r.count,0)} cards. Each unopened marker reserves one card. No reshuffle.`));
    if (custom) {
      const counts = draft.lootCardCatalog().map(c => ({ reference:c.reference, input:field(lootForm, c.reference.equipmentTypeId != null ? `${c.name} (Equipment Type)` : c.reference.itemTypeId != null ? `${c.name} (Item Type)` : c.name, rows.find(r => draft.referenceKey(r) === draft.referenceKey(c.reference))?.count ?? 0, {type:'number'}) }));
      lootForm.append(element('button', 'Apply Custom Deck'));
      lootForm.addEventListener('submit', event => { event.preventDefault(); commit(() => draft.setLootDeck(counts.map(c => ({...c.reference,count:Number(c.input.value)})))); });
    } else {
      const multiplier = field(lootForm, 'Deck multiplier', draft.definition.loot?.presetMultiplier ?? 1, {type:'number'});
      multiplier.id = 'designer-loot-multiplier'; multiplier.min = '1'; multiplier.max = '100'; multiplier.step = '1';
      multiplier.addEventListener('change', () => commit(() => draft.setLootMultiplier(Number(multiplier.value))));
      const list = element('ul'); list.className = 'loot-preset-contents';
      for (const row of rows) list.append(element('li', `${draft.objectName(row)} \u00d7${row.count}`));
      lootForm.append(list, button('Copy to Custom', () => commit(() => draft.copyLootToCustom())));
      lootForm.addEventListener('submit', event => event.preventDefault());
    }
    document.getElementById('designer-loot-editor').replaceChildren(lootForm);
    const world = draft.definition.worldEffects;
    ui["world-enabled"].checked = !!world;
    ui["world-draws"].value = world?.cardsPerRound ?? 1;
    ui["world-cycling"].value = world?.cycling ?? 1;
    ui["world-draws"].disabled = ui["world-cycling"].disabled = !world;
    restoreStatic("world-form");
    ui["world-draws"].disabled = ui["world-cycling"].disabled = !ui["world-enabled"].checked;
  }
  function syncBoardFields() {
    syncTerrainTypes();
    ui.width.value = draft.definition.board.width; ui.height.value = draft.definition.board.height;
    ui.default.value = draft.defaultTerrainSelection();
    restoreStatic("board-form");
  }
  function setTool(value) {
    if (value !== "Pick") picker = null;
    cancelPick.hidden = value !== "Pick";
    tool = value; ui.board.dataset.tool = tool;
    for (const node of ui.board.querySelectorAll(".designer-edge-hit")) node.tabIndex = tool === "Edge" || tool === "Select" || picker?.kind === "edge" ? 0 : -1;
    for (const node of ui.tools.querySelectorAll("button")) node.setAttribute("aria-pressed", String(node.dataset.tool === tool));
    ui["terrain-label"].hidden = tool !== "Terrain"; ui["edge-label"].hidden = tool !== "Edge";
    ui["unit-brush"].hidden = tool !== "Unit";
    ui["machine-label"].hidden = tool !== "Machine";
    ui["resource-label"].hidden = tool !== "Resource";
    if (["Unit", "Machine", "Resource"].includes(tool)) showPanel("inspect");
    ui.help.textContent = {
      Select: "Select a cell, edge, Unit or Machine to edit. Overlapping placements appear in a chooser and placement list.",
      Terrain: "Click or drag cells to paint. Use default removes the explicit override.",
      Edge: "Click or drag internal boundaries. The highlighted segment is the paint target; Remove erases it.",
      Unit: "Click the top-left anchor to place. Supplied footprint geometry is a preview; Validate checks legality.",
      BananaPeel: "Click a Cell to place a Banana Peel. Validate checks placement legality.",
      RemoveMarker: "Click a Cell to remove its Cell Marker.",
      Resource: "Place one Resource per Cell. Select a Resource to move, change or remove it. Validate checks terrain and Machine conflicts.",
      Machine: "Click a cell to place a Machine. Select it to edit its ID, position, Owner Side and starting Resources. Validate checks legality."
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
      draft.paintTerrain(target.position, ui.terrain.value || null, ui["rail-direction"].value);
      target.node.className = `cell ${draft.terrainRecipeAt(target.position)}`;
      target.node.dataset.railDirection = draft.railDirectionAt(target.position) ?? "";
    } else if (tool === "Edge" && target.direction) {
      draft.paintEdge(target.position, target.direction, ui.edge.value || null);
      target.node.className = `edge designer-edge-hit ${ui.edge.value || "None"}`;
    } else return;
    lastPaint = key; unchecked();
  }
  ui.board.addEventListener("pointerdown", event => {
    if (event.button !== 0 || pendingPlay) return;
    const target = hit(event); if (!target) return;
    // Board pointer handling prevents native focus changes; explicitly finish any simple field first.
    document.activeElement?.blur?.();
    event.preventDefault();
    if (pick(target)) return;
    if (tool === "Terrain" || tool === "Edge") { history.begin(); painting = true; lastPaint = null; paint(target); }
    else if (tool === "Unit" && !target.direction) commit(() => {
      selection = { kind: "unit", key: draft.place(ui.type.value, ui.side.value, ui.controller.value, target.position) };
    });
    else if ((tool === "BananaPeel" || tool === "RemoveMarker") && !target.direction) commit(() => {
      draft.setCellMarker(target.position, tool === "RemoveMarker" ? null :
        catalog.cellMarkerTypes.find(type => type.id === "banana-peel"));
    });
    else if (tool === "Machine" && !target.direction) placeMachine(target.position);
    else if (tool === "Resource" && !target.direction) commit(() => { draft.setResource(target.position, resourceTypes.find(t => t.id === ui["resource-type"].value)); selection = { kind: "cell", position: target.position }; });
    else if (tool === "Select") selectTarget(target);
  });
  ui.board.addEventListener("pointermove", event => {
    const target = hit(event);
    for (const node of ui.board.querySelectorAll(".placement-preview")) node.classList.remove("placement-preview");
    if (!target) return;
    ui.hover.textContent = target.direction ? `Edge (${target.position.x}, ${target.position.y}) ${target.direction}`
      : `Cell (${target.position.x}, ${target.position.y}) · ${draft.terrainNameAt(target.position)}`;
    if ((tool === "Unit" || picker?.unitTypeId) && !target.direction) {
      const preview = draft.occupied({ unitTypeId: picker?.unitTypeId ?? ui.type.value, anchor: target.position });
      for (const cell of preview) cellNodes.get(`${cell.x},${cell.y}`)?.classList.add("placement-preview");
    }
    if (tool === "Machine" && !target.direction) target.node.classList.add("placement-preview");
    if (picker && !target.direction) {
      picker.end = target.position;
      const area = picker.kind === "area" && picker.start ? rectangle(picker.start, target.position) : rectangle(target.position, target.position);
      for (let y = area.y; y < area.y + area.h; y++) for (let x = area.x; x < area.x + area.w; x++) cellNodes.get(`${x},${y}`)?.classList.add("placement-preview");
    }
    if (painting && event.buttons === 1) paint(target);
  });
  document.addEventListener("pointerup", () => {
    if (picker?.kind === "area" && picker.start && picker.end && !sameCell(picker.start, picker.end)) completePick(rectangle(picker.start, picker.end));
    if (painting) { painting = false; lastPaint = null; history.finish(); persist(); render(); }
  });
  document.addEventListener("pointercancel", () => { painting = false; lastPaint = null; history.finish(); persist(); render(); });
  ui.board.addEventListener("keydown", event => {
    if (!["Enter", " "].includes(event.key) || pendingPlay) return;
    const target = hit(event); if (!target) return;
    event.preventDefault();
    if (pick(target)) return;
    if (tool === "Select") selectTarget(target);
    else if (tool === "Unit" && !target.direction) commit(() => {
      selection = { kind: "unit", key: draft.place(ui.type.value, ui.side.value, ui.controller.value, target.position) };
    });
    else if ((tool === "BananaPeel" || tool === "RemoveMarker") && !target.direction) commit(() => {
      draft.setCellMarker(target.position, tool === "RemoveMarker" ? null :
        catalog.cellMarkerTypes.find(type => type.id === "banana-peel"));
    });
    else if (tool === "Machine" && !target.direction) placeMachine(target.position);
    else if (tool === "Resource" && !target.direction) commit(() => draft.setResource(target.position, resourceTypes.find(t => t.id === ui["resource-type"].value)));
    else { history.begin(); lastPaint = null; paint(target); history.finish(); persist(); render(); }
  });
  function placeMachine(position) {
    commit(() => { selection = { kind: "machine", key: draft.placeMachine(ui["machine-type"].value, position) }; });
  }
  function selectTarget(target) {
    showPanel("inspect");
    if (target.direction) selection = { kind: "edge", position: target.position, direction: target.direction };
    else {
      const units = draft.unitsAt(target.position);
      const machines = (draft.definition.machines ?? []).flatMap((machine, index) =>
        sameCell(machine.position, target.position) ? [draft.machineKeys[index]] : []);
      const resource = (draft.definition.resources ?? []).some(r => sameCell(r.position, target.position));
      const count = units.length + machines.length + (resource ? 1 : 0);
      if (resource && count === 1) { selection = { kind: "cell", position: target.position }; render(); return; }
      selection = count === 1 ? (units.length ? { kind: "unit", key: units[0] } : { kind: "machine", key: machines[0] })
        : count > 1 ? { kind: "overlap", keys: units, machineKeys: machines, resource, position: target.position }
        : { kind: "cell", position: target.position };
    }
    render();
  }
  function selectPath(path) {
    const clean = path.replace(/^\$\.?/, "");
    const match = clean.match(/^(units|machines|resources|cellMarkers|board.cells|board.edges|agency|orders|routes|waypoints|objectives|neutralSides)\[(\d+)\]/);
    if (!match) { showPanel(clean.startsWith("board") || clean.startsWith("world") || clean.startsWith("unitType") ? "board" : "groups"); return; }
    const index = Number(match[2]);
    if (match[1] === "objectives") { objectiveSide = draft.definition.objectives?.[index]?.sideId; showPanel("sides"); render(); reviewForm(`objective:${objectiveSide}`); return; }
    showPanel(["agency", "orders"].includes(match[1]) ? "groups" : ["objectives", "neutralSides"].includes(match[1]) ? "sides" : ["routes", "waypoints"].includes(match[1]) ? "routes" : "inspect");
    if (["routes", "waypoints"].includes(match[1])) {
      const id = draft.definition[match[1]]?.[index]?.id;
      reviewForm(`${match[1] === "routes" ? "route" : "waypoint"}:${id}`); return;
    }
    if (match[1] === "units" && draft.unitKeys[index]) selection = { kind: "unit", key: draft.unitKeys[index] };
    if (match[1] === "machines" && draft.machineKeys[index]) selection = { kind: "machine", key: draft.machineKeys[index] };
    if (match[1] === "cellMarkers" && draft.definition.cellMarkers?.[index])
      selection = { kind: "cell", position: draft.definition.cellMarkers[index].position };
    if (match[1] === "resources" && draft.definition.resources?.[index]) selection = { kind: "cell", position: draft.definition.resources[index].position };
    if (match[1] === "board.cells" && draft.definition.board.cells[index])
      selection = { kind: "cell", position: draft.definition.board.cells[index].position };
    if (match[1] === "board.edges" && draft.definition.board.edges[index])
      selection = { kind: "edge", ...draft.definition.board.edges[index] };
    if (match[1] === "agency") ui.agency.children[index]?.scrollIntoView?.({ block: "nearest" });
    if (match[1] === "orders") {
      const order = draft.definition.orders?.[index];
      const groupIndex = draft.definition.agency.findIndex(row => row.unitTypeId === order?.unitTypeId && row.sideId === order?.sideId);
      ui.agency.children[groupIndex]?.scrollIntoView?.({ block: "nearest" });
    }
    setTool("Select"); renderBoard(); renderProperties();
    if (!["agency", "orders", "objectives", "neutralSides", "fogOfWarSides"].includes(match[1])) ui.properties.scrollIntoView?.({ block: "nearest" });
  }

  const cellNodes = new Map();
  function renderBoard() {
    const board = draft.definition.board; ui.board.replaceChildren(); cellNodes.clear();
    ui.board.style.gridTemplateColumns = `repeat(${board.width}, minmax(0, 1fr))`;
    ui.board.style.gridTemplateRows = `repeat(${board.height}, minmax(0, 1fr))`;
    ui.board.style.setProperty("--columns", board.width);
    ui.board.style.setProperty("--board-ratio", board.width / board.height);
    ui.board.style.aspectRatio = `${board.width} / ${board.height}`;
    ui.board.style.minWidth = ui.zoom.value === "fit" ? "" : `${board.width * Number(ui.zoom.value)}px`;
    for (const waypoint of draft.definition.waypoints ?? []) {
      const area = waypoint.area, node = element("span", waypoint.id); node.className = "waypoint-area";
      Object.assign(node.style, { left: `${area.x / board.width * 100}%`, top: `${area.y / board.height * 100}%`,
        width: `${area.w / board.width * 100}%`, height: `${area.h / board.height * 100}%` });
      ui.board.append(node);
    }
    const objectives = (draft.definition.objectives ?? []).filter(o => o.sideId !== objectiveSide);
    if (inspectedObjective && objectiveSide) objectives.push(inspectedObjective);
    for (const objective of objectives) {
      const a = objective.area;
      if (objective.objective !== "ReachArea" || !a || ![a.x, a.y, a.w, a.h].every(Number.isFinite) || a.w < 1 || a.h < 1) continue;
      const x = Math.max(0, a.x), y = Math.max(0, a.y), w = Math.max(0, Math.min(board.width, a.x + a.w) - x), h = Math.max(0, Math.min(board.height, a.y + a.h) - y);
      if (!w || !h) continue;
      const node = element("span", `${objective.sideId} · Objective`); node.className = `waypoint-area objective-area${objective.sideId === objectiveSide ? " active" : ""}`;
      if (x !== a.x || y !== a.y || w !== a.w || h !== a.h) { node.className += " invalid-area"; node.textContent += " · outside board"; }
      styleSide(node, objective.sideId);
      Object.assign(node.style, { left: `${x / board.width * 100}%`, top: `${y / board.height * 100}%`, width: `${w / board.width * 100}%`, height: `${h / board.height * 100}%` });
      ui.board.append(node);
    }
    for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
      const cell = { x, y }, node = element("div");
      node.className = `cell ${draft.terrainRecipeAt(cell)}`;
      node.dataset.railDirection = draft.railDirectionAt(cell) ?? "";
      node.dataset.editorHit = "cell"; node.dataset.x = x; node.dataset.y = y;
      node.tabIndex = 0; node.setAttribute("role", "button");
      node.setAttribute("aria-label", `Cell ${x},${y}: ${draft.terrainNameAt(cell)} ${draft.railDirectionAt(cell) ?? ""}`);
      node.title = `${x},${y} · ${draft.terrainNameAt(cell)}`;
      node.append(element("span", `${x},${y}`));
      if (selection?.kind === "cell" && sameCell(selection.position, cell)) node.classList.add("editor-selected");
      for (const [index, machine] of (draft.definition.machines ?? []).entries()) {
        if (!sameCell(machine.position, cell)) continue;
        node.classList.add("machine-cell");
        node.append(element("span", machine.type.name));
        node.append(element("small", machine.ownerSide == null ? "Public" : `Owner: ${machine.ownerSide}`));
        for (const capacity of machine.type.storage.capacities) {
          const quantity = machine.storedResources?.find(row => row.type.id === capacity.type.id)?.quantity ?? 0;
          node.append(element("small", `${capacity.type.name}: ${quantity}/${capacity.capacity}`));
        }
        node.title += ` · ${machine.type.name} (${machine.id}) - ${machine.ownerSide == null ? "Public" : `Owner: ${machine.ownerSide}`}`;
        node.setAttribute("aria-label", node.title);
        if (selection?.kind === "machine" && selection.key === draft.machineKeys[index]) node.classList.add("editor-selected");
      }
      const unopened = (draft.definition.lootMarkers ?? []).filter(m => sameCell(m.position, cell)).length;
      const known = (draft.definition.groundObjects ?? []).filter(g => sameCell(g.position, cell)).length;
      if (unopened || known) { const loot = element('span', `▣ ${unopened} · ◆ ${known}`); loot.className = 'loot-ground'; node.append(loot); }
      const cellMarker = (draft.definition.cellMarkers ?? []).find(marker => sameCell(marker.position, cell));
      if (cellMarker) {
        const marker = element("span", "\u{1F34C}"); marker.className = "cell-marker";
        marker.title = cellMarker.type.name; marker.setAttribute("aria-label", cellMarker.type.name);
        node.append(marker); node.title += ` · ${cellMarker.type.name}`;
        node.setAttribute("aria-label", node.title);
      }
      const resource = (draft.definition.resources ?? []).find(resource => sameCell(resource.position, cell));
      if (resource) {
        const marker = element("span", resourceSymbol(resource.type)); marker.className = "resource-marker";
        marker.title = resource.type.name; marker.setAttribute("aria-label", resource.type.name);
        node.append(marker); node.title += ` · ${resource.type.name}`; node.setAttribute("aria-label", node.title);
      }
      ui.board.append(node); cellNodes.set(`${x},${y}`, node);
    }
    const edges = new Map(board.edges.map(edge => [`${edge.position.x},${edge.position.y},${edge.direction}`, edge]));
    for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) for (const direction of ["Right", "Down"]) {
      if (direction === "Right" && x === board.width - 1 || direction === "Down" && y === board.height - 1) continue;
      const edge = edges.get(`${x},${y},${direction}`), node = element("div");
      node.className = `edge designer-edge-hit ${edge?.kind ?? "None"}`;
      node.dataset.editorHit = "edge"; node.dataset.x = x; node.dataset.y = y; node.dataset.direction = direction;
      node.tabIndex = tool === "Edge" || tool === "Select" || picker?.kind === "edge" ? 0 : -1;
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
      if (highlightedGroup?.unitTypeId === unit.unitTypeId && highlightedGroup?.sideId === unit.sideId ||
        objectiveSide && inspectedObjective?.selectors?.some(s => s.kind === "Unit" ? (unit.unitId ?? draft.effectiveUnitIds?.[index]) === s.unitId : s.sideId === unit.sideId && (s.kind === "SideAtStart" || s.unitTypeId === unit.unitTypeId))) node.classList.add("objective-reference");
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
    if (!selection) { ui.properties.textContent = "Select a Cell, edge, Unit or Machine on the board. Use a placement tool to add something new."; return; }
    if (selection.kind === "overlap") {
      ui.properties.append(element("p", "Overlapping placements: choose an object."));
      for (const key of selection.keys) ui.properties.append(button(unitLabel(draft.unitIndex(key)), () => {
        selection = { kind: "unit", key }; render();
      }));
      for (const key of selection.machineKeys ?? []) ui.properties.append(button(machineLabel(draft.machineKeys.indexOf(key)), () => {
        selection = { kind: "machine", key }; render();
      }));
      if (selection.resource) ui.properties.append(button("Inspect Resource", () => { selection = { kind: "cell", position: selection.position }; render(); }));
      ui.properties.append(button("Edit terrain below", () => { selection = { kind: "cell", position: selection.position }; render(); }));
      return;
    }
    const form = editorForm(`selection:${JSON.stringify(selection)}`, true);
    if (selection.kind === "unit") {
      const key = selection.key, unit = draft.definition.units[draft.unitIndex(key)];
      if (!unit) { selection = null; renderProperties(); return; }
      if (!draft.metadata(unit.unitTypeId)) form.append(element("p", "Imported content: geometry is unavailable in the editor catalog; only the anchor is previewed. Engine validation still checks the full scenario. Save draft retains custom definitions."));
      const type = field(form, "Unit Type", unit.unitTypeId, { entries: typeOptions });
      const side = field(form, "SideId", unit.sideId, { list: "designer-sides" });
      const x = field(form, "Anchor X", unit.anchor.x, { type: "number" });
      const y = field(form, "Anchor Y", unit.anchor.y, { type: "number" });
      const posture = field(form, "Posture", unit.posture, { entries: catalog.postures });
      const hp = field(form, `Initial HP (blank = full; max ${draft.metadata(unit.unitTypeId)?.maxHp ?? "?"})`, unit.initialHp, { type: "number" });
      const agency = draft.definition.agency.find(row => row.unitTypeId === unit.unitTypeId && row.sideId === unit.sideId);
      const controller = field(form, "Controller for new Type + Side", agency?.controller ?? ui.controller.value, { entries: catalog.controllers });
      const unitId = field(form, "Unit ID (optional; blank = generated)", unit.unitId);
      objectEditor(form, unit.inventory ?? [], { unitKey: key });
      const sync = () => {
        const existing = draft.definition.agency.find(row => row.unitTypeId === type.value && row.sideId === side.value);
        controller.disabled = !!existing; if (existing) controller.value = existing.controller;
      };
      type.addEventListener("change", sync); side.addEventListener("input", sync); sync();
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.editUnit(key,
        { unitTypeId: type.value, sideId: side.value, anchor: { x: Number(x.value), y: Number(y.value) },
          posture: posture.value, initialHp: hp.value === "" ? null : Number(hp.value),
          unitId: unitId.value.trim() ? unitId.value : null }, controller.value)); });
      form.append(element("button", "Apply Unit"));
      form.append(button("Move Unit on board", () => beginPick("cell", `Choose top-left anchor for ${draft.metadata(unit.unitTypeId)?.displayName ?? unit.unitTypeId}`, anchor => commit(() => {
        draft.editUnit(key, { anchor }, agency?.controller ?? "Human");
      }), unit.unitTypeId)));
      form.append(button("Delete Unit", () => commit(() => { draft.removeUnit(key); selection = null; }, [form.dataset.formKey])));
      form.append(button("Edit terrain at anchor", () => { selection = { kind: "cell", position: unit.anchor }; render(); }));
      form.append(button("Inspect group and inventory", () => {
        highlightedGroup = { unitTypeId: unit.unitTypeId, sideId: unit.sideId }; objectiveSide = null; showPanel("groups"); render();
        const index = draft.definition.agency.findIndex(g => g.unitTypeId === unit.unitTypeId && g.sideId === unit.sideId);
        ui.agency.children[index]?.scrollIntoView?.({ block: "nearest" });
      }));
    } else if (selection.kind === "machine") {
      const key = selection.key, machine = draft.definition.machines?.[draft.machineKeys.indexOf(key)];
      if (!machine) { selection = null; renderProperties(); return; }
      form.append(element("p", machine.type.name));
      const id = field(form, "Machine ID", machine.id);
      const sides = [...new Set([...draft.definition.units, ...draft.definition.agency].map(row => row.sideId))];
      const ownerEntries = [{ value: "", label: "None / Public" }, ...sides.map(value => ({ value, label: value }))];
      // Preserve dangling references for authoritative validation, just like Orders and Unit Side references.
      if (machine.ownerSide != null && !sides.includes(machine.ownerSide))
        ownerEntries.push({ value: machine.ownerSide, label: `${machine.ownerSide} (invalid Side)` });
      const owner = field(form, "Owner Side", machine.ownerSide, { entries: ownerEntries });
      const x = field(form, "Position X", machine.position.x, { type: "number" });
      const y = field(form, "Position Y", machine.position.y, { type: "number" });
      const quantities = machine.type.storage.capacities.map(capacity => {
        const quantity = machine.storedResources?.find(row => row.type.id === capacity.type.id)?.quantity ?? 0;
        const input = field(form, `Initial ${capacity.type.name} (capacity ${capacity.capacity})`, quantity, { type: "number" });
        input.step = "1"; return { type: capacity.type, input };
      });
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.editMachine(key, {
        id: id.value, ownerSide: owner.value === "" ? null : owner.value, position: { x: Number(x.value), y: Number(y.value) },
        storedResources: [...(machine.storedResources ?? []).filter(row => !quantities.some(q => q.type.id === row.type.id)),
          ...quantities.map(q => ({ type: q.type, quantity: Number(q.input.value) }))]
      })); });
      form.append(element("button", "Apply Machine"));
      form.append(button("Move Machine on board", () => beginPick("cell", "Move Machine", position => commit(() => draft.editMachine(key, { position })))));
      form.append(button("Delete Machine", () => commit(() => { draft.removeMachine(key); selection = null; }, [form.dataset.formKey])));
      form.append(button("Edit terrain below", () => { selection = { kind: "cell", position: machine.position }; render(); }));
    } else if (selection.kind === "cell") {
      const position = { ...selection.position };
      form.append(element("p", `Cell (${position.x}, ${position.y})`));
      const lootCount = field(form, 'Unopened Loot markers', (draft.definition.lootMarkers ?? []).filter(m => sameCell(m.position, position)).length, { type: 'number' });
      form.append(button('Apply Loot markers', () => commit(() => draft.setLootMarkers(position, Number(lootCount.value)))));
      objectEditor(form, (draft.definition.groundObjects ?? []).filter(g => sameCell(g.position, position)).map(g => g.object), { position });
      const resource = draft.definition.resources?.find(row => sameCell(row.position, position));
      let resourceType = null;
      if (resource) {
        form.append(element("strong", `${resourceSymbol(resource.type)} ${resource.type.name}`));
        resourceType = field(form, "Resource Type", resource.type.id, { entries: resourceTypes.map(t => ({ value: t.id, label: t.name })) });
        form.append(button("Move Resource on board", () => beginPick("cell", `Move ${resource.type.name}`, to => commit(() => { draft.moveResource(position, to); selection = { kind: "cell", position: to }; }))));
        form.append(button("Remove Resource", () => commit(() => draft.setResource(position, null), [form.dataset.formKey])));
      }
      const marker = draft.definition.cellMarkers?.find(marker => sameCell(marker.position, position));
      if (marker) {
        form.append(element("p", `Cell Marker: ${marker.type.name}`));
        form.append(button("Remove Cell Marker", () => commit(() => draft.setCellMarker(position, null))));
      }
      const explicit = draft.definition.board.cells.find(cell => sameCell(cell.position, position));
      const terrain = field(form, "Override", explicit?.terrainTypeId != null ? draft.terrainTypeSelection(explicit.terrainTypeId) : explicit?.terrain ?? "", { entries: terrainOptions });
      const terrainDefinition = draft.terrainMetadata(draft.terrainSelectionAt(position));
      if (terrainDefinition) form.append(element('p', `${terrainDefinition.name} · Type ${terrainDefinition.id} · ${terrainDefinition.passable ? 'Passable' : 'Blocks movement'} · ${terrainDefinition.blocksLos ? 'Blocks LOS' : 'Does not block LOS'}`));
      const direction = field(form, "Rail outgoing direction", explicit?.direction ?? "East", { entries: catalog.railDirections ?? ["North", "East", "South", "West"] });
      const coal = (terrainDefinition?.legacyKind ?? draft.terrainAt(position)) === "Lava"
        ? field(form, "Coal generation", explicit?.coalGeneration === false ? "Off" : "On", { entries: ["On", "Off"] }) : null;
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => {
        const defaultReference = draft.defaultTerrainSelection();
        const defaultKind = draft.terrainMetadata(defaultReference)?.legacyKind ?? defaultReference;
        const kind = terrain.value || (coal?.value === "Off" && defaultKind === "Lava" ? defaultReference : null);
        draft.paintTerrain(position, kind, direction.value, coal ? coal.value !== "Off" : null);
        if (resource && resourceType.value !== resource.type.id) { resource.type = structuredClone(resourceTypes.find(t => t.id === resourceType.value)); draft.changed(); }
      }); });
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
    return `${index + 1}. ${draft.metadata(unit.unitTypeId)?.displayName ?? unit.unitTypeId} · ${unit.sideId} @ ${unit.anchor.x},${unit.anchor.y}${unit.unitId ? ` · ID ${unit.unitId}` : ""}`;
  }
  function orderFields(form, initial) {
    const kind = field(form, "Starting Order", initial?.kind ?? "Normal", {
      entries: catalog.orderKinds.map(value => ({ value, label: value.replace(/([a-z])([A-Z])/g, "$1 $2") }))
    });
    const details = element("div"); details.className = "designer-property-form"; form.append(details);
    let readDetails = () => ({});
    function update() {
      details.replaceChildren();
      const saved = kind.value === initial?.kind ? initial : null;
      const cellFields = (label, cell) => {
        const x = field(details, `${label} X`, cell?.x ?? 0, { type: "number" });
        const y = field(details, `${label} Y`, cell?.y ?? 0, { type: "number" });
        x.step = y.step = "1";
        details.append(button(`Pick ${label} on board`, () => beginPick("cell", label, cell => bufferedValues(form, { [`${label} X`]: String(cell.x), [`${label} Y`]: String(cell.y) }))));
        return () => ({ x: Number(x.value), y: Number(y.value) });
      };
      readDetails = () => ({});
      if (kind.value === "FollowRoute") {
        const route = saved?.route;
        const routeId = field(details, "Shared Route", route?.routeId, {
          entries: (draft.definition.routes ?? []).map(r => ({ value: r.id, label: `${r.id} (${r.mode})` })) });
        const current = field(details, "Current waypoint index (0-based)", route?.currentIndex ?? 0, { type: "number" });
        const direction = field(details, "Ping-pong direction", String(route?.direction ?? 1), { entries: ["1", "-1"] });
        routeId.addEventListener("change", () => { current.value = "0"; direction.value = "1"; });
        readDetails = () => ({ route: { routeId: routeId.value, currentIndex: Number(current.value), direction: Number(direction.value) } });      } else if (kind.value === "MoveTo") {
        const destination = cellFields("Destination", saved?.destination);
        readDetails = () => ({ destination: destination() });
      } else if (["Attack", "Protect"].includes(kind.value)) {
        const scope = field(details, "Target scope", saved?.target?.kind ?? "Unit", {
          entries: kind.value === "Protect" ? ["Unit"] : ["Unit", "Group", "Side"]
        });
        const targetFields = element("div"); targetFields.className = "designer-property-form"; details.append(targetFields);
        let readTarget;
        function updateTarget() {
          targetFields.replaceChildren();
          const target = scope.value === saved?.target?.kind ? saved.target : null;
          if (scope.value === "Unit") {
            const id = field(targetFields, "Target Unit ID", target?.unitId, { list: "designer-unit-ids" });
            const suggestions = element("p", "Assign a stable Unit ID in the selected Unit's properties first.");
            targetFields.append(suggestions);
            targetFields.append(button("Pick Order Unit on board", () => beginPick("cell", "Pick Order target", position => {
              const keys = draft.unitsAt(position); if (keys.length !== 1) { ui.status.textContent = "Choose a Cell with one Unit."; return; }
              let unitId; if (commit(() => { unitId = draft.stableUnitId(keys[0]); })) bufferedValues(form, { "Target Unit ID": unitId });
            })));
            readTarget = () => ({ kind: "Unit", unitId: id.value });
          } else if (scope.value === "Group") {
            const type = field(targetFields, "Target Unit Type", target?.group?.typeId ?? typeOptions[0].value, { entries: typeOptions });
            const side = field(targetFields, "Target SideId", target?.group?.sideId, { list: "designer-sides" });
            readTarget = () => ({ kind: "Group", group: { typeId: type.value, sideId: side.value } });
          } else {
            const side = field(targetFields, "Target SideId", target?.sideId, { list: "designer-sides" });
            readTarget = () => ({ kind: "Side", sideId: side.value });
          }
        }
        scope.addEventListener("change", updateTarget); updateTarget();
        readDetails = () => ({ target: readTarget() });
      } else if (kind.value === "InteractWith") {
        const a = cellFields("Door endpoint A", saved?.target?.door?.a);
        const b = cellFields("Door endpoint B", saved?.target?.door?.b);
        details.append(button("Pick Door edge", () => beginPick("edge", "Pick Door edge", target => {
          const end = { x: target.position.x + (target.direction === "Right" ? 1 : 0), y: target.position.y + (target.direction === "Down" ? 1 : 0) };
          bufferedValues(form, { "Door endpoint A X": String(target.position.x), "Door endpoint A Y": String(target.position.y), "Door endpoint B X": String(end.x), "Door endpoint B Y": String(end.y) });
        })));
        readDetails = () => {
          const endpoints = [a(), b()].sort((left, right) => left.y - right.y || left.x - right.x);
          return { target: { kind: "Door", door: { a: endpoints[0], b: endpoints[1] } } };
        };
      }
    }
    kind.addEventListener("change", update); update();
    return () => ({ kind: kind.value, ...readDetails() });
  }
  function machineLabel(index) {
    const machine = draft.definition.machines[index];
    return `${machine.type.name} (${machine.id}) - ${machine.ownerSide == null ? "Public" : `Owner: ${machine.ownerSide}`} · (${machine.position.x}, ${machine.position.y})`;
  }
  function objectiveEditor(side, current, sides) {
    const form = editorForm(`objective:${side}`);
    form.dataset.objectiveSide = side;
    const heading = element("strong", `Objective · ${side}`); styleSide(heading, side); form.append(heading);
    const kind = field(form, "Objective type", current?.objective ?? "", { entries: [
      { value: "", label: "Default · Eliminate Enemies" }, { value: "EliminateEnemies", label: "Eliminate Enemies" },
      { value: "NoObjective", label: "No Objective" }, { value: "ReachArea", label: "Reach Area" }, { value: "DefeatUnits", label: "Defeat Units" }] });
    kind.addEventListener("change", render);
    const selected = ["ReachArea", "DefeatUnits"].includes(kind.value);
    let readSelectors = () => [], readArea = () => null, readCount = () => current?.count ?? 0;
    if (selected) {
      form.append(element("small", kind.value === "ReachArea" ? "Selected living Units must fit completely inside the area at the same evaluation boundary." : "Selected Units must have been defeated. Removing a figure alone does not count."));
      const mode = field(form, "Required Units", current?.count ? "Minimum" : "All", { entries: ["All", "Minimum"] });
      const count = field(form, "Minimum count", current?.count || 1, { type: "number" });
      count.disabled = mode.value === "All";
      mode.addEventListener("change", () => { count.disabled = mode.value === "All"; });
      readCount = () => mode.value === "All" ? 0 : Number(count.value);
      form.append(element("small", "Selectors combine without double counting. An empty selection never achieves the Objective."));
      const selectorRows = structuredClone(buffers[form.dataset.formKey]?.selectors ?? current?.selectors ?? []);
      const reads = [];
      const saveRows = rows => { (buffers[form.dataset.formKey] ??= { values: {} }).selectors = rows; requests.sequence++; persist(); render(); };
      selectorRows.forEach((row, index) => {
        const section = element("div"); section.className = "designer-selector"; form.append(section);
        const prefix = `Selection ${index + 1}`;
        const scope = field(section, `${prefix} · select`, row.kind, { entries: [
          { value: "Unit", label: "One specific Unit" }, { value: "SideAtStart", label: "Side's initial Units" }, { value: "Group", label: "Unit Type + Side · includes later Units" }] });
        scope.addEventListener("change", render);
        let read;
        if (scope.value === "Unit") {
          const entries = [{ value: "", label: "Choose a Unit with a stable ID, or pick on board" }, ...draft.definition.units.flatMap((u, i) => {
            const id = u.unitId ?? draft.effectiveUnitIds?.[i]; return id ? [{ value: id, label: `${draft.metadata(u.unitTypeId)?.displayName ?? u.unitTypeId} · ${u.sideId} · ${id}` }] : [];
          })];
          const id = field(section, `${prefix} · Unit`, row.unitId ?? "", { entries });
          section.append(button("Pick Unit on board", () => beginPick("cell", "Pick the specific Unit", position => {
            const keys = draft.unitsAt(position);
            if (keys.length !== 1) { ui.status.textContent = "Choose a Cell containing exactly one Unit. Overlaps can be fixed in Inspector."; return; }
            let unitId; if (commit(() => { unitId = draft.stableUnitId(keys[0]); })) bufferedValues(form, { [`${prefix} · Unit`]: unitId });
          })));
          read = () => ({ kind: "Unit", unitId: id.value });
        } else if (scope.value === "SideAtStart") {
          const id = field(section, `${prefix} · Side`, row.sideId ?? sides[0], { entries: sides });
          section.append(element("small", "Only Units present at setup are included; summoned Units are excluded."));
          read = () => ({ kind: "SideAtStart", sideId: id.value });
        } else {
          const entries = draft.definition.agency.map(g => ({ value: JSON.stringify([g.unitTypeId, g.sideId]), label: `${draft.metadata(g.unitTypeId)?.displayName ?? g.unitTypeId} · ${g.sideId}` }));
          const identity = row.unitTypeId && row.sideId ? JSON.stringify([row.unitTypeId, row.sideId]) : "missing";
          const group = field(section, `${prefix} · Group`, identity, { entries: [{ value: "missing", label: "Choose a group" }, ...entries] });
          read = () => { try { const [unitTypeId, sideId] = JSON.parse(group.value); return { kind: "Group", unitTypeId, sideId }; } catch { return { ...row, kind: "Group" }; } };
          section.append(button("Pick group on board", () => beginPick("cell", "Pick a Unit in the group", position => {
            const keys = draft.unitsAt(position), unit = keys.length === 1 ? draft.definition.units[draft.unitIndex(keys[0])] : null;
            if (!unit) { ui.status.textContent = "Choose a Cell with one Unit."; return; }
            bufferedValues(form, { [`${prefix} · Group`]: JSON.stringify([unit.unitTypeId, unit.sideId]) });
          })));
        }
        const readKnown = read;
        reads.push(() => {
          const result = { ...row, ...readKnown() };
          if (result.kind !== "Unit") delete result.unitId;
          if (result.kind === "Unit") { delete result.sideId; delete result.unitTypeId; }
          if (result.kind === "SideAtStart") delete result.unitTypeId;
          return result;
        });
        section.append(button("Remove selection", () => {
          const rows = reads.map(fn => fn()).concat(selectorRows.slice(reads.length)); rows.splice(index, 1);
          for (const label of Object.keys(buffers[form.dataset.formKey]?.values ?? {})) if (label.startsWith("Selection ")) delete buffers[form.dataset.formKey].values[label];
          saveRows(rows);
        }));
      });
      readSelectors = () => reads.map(fn => fn());
      form.append(button("Add selection", () => saveRows([...readSelectors(), { kind: "SideAtStart", sideId: side }])));
      if (kind.value === "ReachArea") {
        const coordinates = element("div"); coordinates.className = "designer-coordinate-grid"; form.append(coordinates);
        const geometry = ["x", "y", "w", "h"].map(key => field(coordinates, `Area ${key.toUpperCase()}`, current?.area?.[key] ?? (key === "w" || key === "h" ? 1 : 0), { type: "number" }));
        readArea = () => Object.fromEntries(["x", "y", "w", "h"].map((key, i) => [key, Number(geometry[i].value)]));
        form.append(button("Draw Objective Area", () => beginPick("area", `Draw Objective Area for ${side}`, area => {
          bufferedValues(form, Object.fromEntries(Object.entries(area).map(([key, value]) => [`Area ${key.toUpperCase()}`, String(value)])));
        })));
      }
    }
    const read = () => {
      if (!kind.value) return null;
      if (kind.value === current?.objective && !selected) return structuredClone(current);
      const result = { ...(current ?? {}), sideId: side, objective: kind.value, count: selected ? readCount() : 0, selectors: selected ? readSelectors() : [] };
      if (kind.value === "ReachArea") result.area = readArea(); else delete result.area;
      return result;
    };
    inspectedObjective = read();
    form.append(element("button", "Apply Objective"));
    form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.setObjective(side, read())); });
    if (current) form.append(button("Use default Objective", () => commit(() => draft.setObjective(side, null), [form.dataset.formKey])));
    const destinations = sides.filter(s => s !== side);
    if (destinations.length) {
      const destination = field(form, "Copy Objective to Side", destinations[0], { entries: destinations });
      form.append(button("Copy Objective", () => {
        if (draft.definition.objectives?.some(o => o.sideId === destination.value)) { ui.status.textContent = "That Side already has an explicit Objective. Use its default Objective first before copying."; return; }
        if (!numbersComplete(form)) { ui.status.textContent = "Finish numeric fields before copying."; return; }
        commit(() => draft.setObjective(destination.value, read()));
      }));
    }
    return form;
  }
  function renderData() {
    ui.order.replaceChildren(...draft.definition.unitTypeIds.map(id => element("li", `${draft.metadata(id)?.displayName ?? id} (${id})`)));
    const sides = [...new Set([...draft.definition.units, ...draft.definition.agency].map(row => row.sideId))];
    ui["side-settings"].replaceChildren(...sides.map(side => {
      const form = editorForm(`side:${side}`, true);
      form.dataset.sideId = side;
      form.append(element("strong", side));
      const neutral = field(form, "Neutral (no hostility in either direction)", (draft.definition.neutralSides ?? []).includes(side) ? "Yes" : "No", { entries: ["No", "Yes"] });
      const fog = field(form, "Fog of War — permanent exploration", (draft.definition.fogOfWarSides ?? []).includes(side) ? "Enabled" : "Disabled", { entries: ["Disabled", "Enabled"] });
      fog.dataset.fogSide = side;
      form.append(element("small", "Digital only*. This Side explores using its own Units. Explored Cells remain known for the entire game."));
      const current = draft.definition.objectives?.find(row => row.sideId === side);
      form.append(button(`${current?.objective?.replace(/([a-z])([A-Z])/g, "$1 $2") ?? "Default · Eliminate Enemies"} · Edit Objective`, () => { objectiveSide = side; highlightedGroup = null; render(); }));
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => {
        draft.setNeutral(side, neutral.value === "Yes");
        draft.setFogOfWar(side, fog.value === "Enabled");
      }); });
      form.append(element("button", "Apply Side")); return form;
    }));
    if (objectiveSide && sides.includes(objectiveSide)) ui["side-settings"].append(objectiveEditor(objectiveSide, draft.definition.objectives?.find(o => o.sideId === objectiveSide), sides));
    if (!sides.length) ui["side-settings"].append(element("p", "Place a Unit or add an unplaced group in Groups to create a Side."));
    ui.sides.replaceChildren(...sides.map(side => { const option = element("option"); option.value = side; return option; }));
    ui["unit-ids"].replaceChildren(...draft.definition.units.filter(unit => unit.unitId).map(unit => {
      const option = element("option", `${draft.metadata(unit.unitTypeId)?.displayName ?? unit.unitTypeId} · ${unit.sideId}`);
      option.value = unit.unitId; return option;
    }));
    ui.agency.replaceChildren(...draft.definition.agency.map((row, index) => {
      const form = editorForm(`agency:${JSON.stringify([row.unitTypeId, row.sideId])}`); form.className += " designer-agency-row";
      form.dataset.groupType = row.unitTypeId; form.dataset.groupSide = row.sideId;
      const type = field(form, "Type", row.unitTypeId, { entries: typeOptions });
      const side = field(form, "SideId", row.sideId, { list: "designer-sides" });
      const controller = field(form, "Controller", row.controller, { entries: catalog.controllers });
      const readOrder = orderFields(form, draft.definition.orders?.find(order =>
        order.unitTypeId === row.unitTypeId && order.sideId === row.sideId));
      form.append(button("Highlight group", () => { highlightedGroup = { unitTypeId: row.unitTypeId, sideId: row.sideId }; objectiveSide = null; renderBoard(); }));
      const capacities = draft.metadata(row.unitTypeId)?.resourceStorage?.capacities ?? [];
      const lootType = draft.definition.unitTypes?.find(t => t.id === row.unitTypeId) ?? draft.metadata(row.unitTypeId)?.definition;
      const lootPanel = element('details'); lootPanel.dataset.lootType = row.unitTypeId; lootPanel.dataset.formKey = `loot-type:${row.unitTypeId}`; lootPanel.append(element('summary', 'Loot & ownership · Unit Type')); form.append(lootPanel);
      const overrides = draft.definition.lootUnitOverrides?.[row.unitTypeId] ?? {};
      const dropChance = field(lootPanel, 'Drop probability', overrides.dropSuccesses ?? 'default', { entries: [{value:'default',label:`Unit Type default (${lootType?.lootDropSuccesses ?? 0}/6)`}, ...Array.from({length:7},(_,i) => ({value:String(i),label:`Override: ${i}/6`}))] });
      const capability = (label, key) => field(lootPanel,label,overrides[key] == null ? 'default' : overrides[key] ? 'Yes' : 'No', {entries:[{value:'default',label:`Unit Type default (${lootType?.[key] ? 'Yes' : 'No'})`},'No','Yes']});
      const items = capability('Can Have Items','canHaveItems'), equipment = capability('Can Have Equipment','canHaveEquipment');
      const value = input => input.value === 'default' ? null : input.value === 'Yes';
      lootPanel.append(button('Apply Loot settings', () => commit(() => draft.configureLootType(row.unitTypeId,
        dropChance.value === 'default' ? null : Number(dropChance.value), value(items), value(equipment)), [lootPanel.dataset.formKey])));
      if (buffers[lootPanel.dataset.formKey]) lootPanel.append(button('Discard unfinished Loot settings', () => {
        delete buffers[lootPanel.dataset.formKey]; persist(); render();
      }));
      const inventoryPanel = element("details"), inventoryFields = element("div"); inventoryFields.className = "designer-coordinate-grid";
      if (capacities.length) {
        const quantity = (draft.definition.storedResources ?? []).filter(r => r.unitTypeId === row.unitTypeId && r.sideId === row.sideId).reduce((sum, r) => sum + r.quantity, 0);
        inventoryPanel.append(element("summary", `Edit shared inventory · ${quantity} stored`)); form.append(inventoryPanel);
        inventoryPanel.append(inventoryFields);
        inventoryPanel.open = quantity > 0 || Object.keys(buffers[form.dataset.formKey]?.values ?? {}).some(k => k.startsWith("Stored "));
      }
      const inventory = capacities.map(c => ({ type: c.type, input: field(inventoryFields, `Stored ${c.type.name} (capacity ${c.capacity})`,
        draft.definition.storedResources?.find(r => r.unitTypeId === row.unitTypeId && r.sideId === row.sideId && r.type.id === c.type.id)?.quantity ?? 0, { type: "number" }) }));
      form.append(element("small", capacities.length ? "Shared group inventory · stored on the Unit Type + Side card, including when no Units are placed." : "This Unit Type has no Resource Storage."));
      for (const resource of draft.definition.storedResources ?? []) if (resource.unitTypeId === row.unitTypeId && resource.sideId === row.sideId && !capacities.some(c => c.type.id === resource.type.id)) {
        form.append(element("small", `Retained unsupported inventory: ${resource.quantity} ${resource.type.name}`));
        form.append(button(`Remove unsupported ${resource.type.name}`, () => commit(() => { draft.definition.storedResources = draft.definition.storedResources.filter(r => r !== resource); draft.changed(); })));
      }
      const count = draft.definition.units.filter(unit => unit.unitTypeId === row.unitTypeId && unit.sideId === row.sideId).length;
      form.append(element("small", `${count} initial Units`));
      form.append(element("button", "Apply pair"));
      form.append(button("Remove pair", () => commit(() => draft.removeAgency(index), [form.dataset.formKey])));
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => {
        const identityChanged = type.value !== row.unitTypeId || side.value !== row.sideId;
        if (identityChanged && inventory.some(q => Number(q.input.value) !== (draft.definition.storedResources?.find(r => r.unitTypeId === row.unitTypeId && r.sideId === row.sideId && r.type.id === q.type.id)?.quantity ?? 0))) throw new Error("Apply inventory before changing the group's identity. Existing stored Resources retain their original group.");
        if (inventory.length && !identityChanged) draft.setInventory(row.unitTypeId, row.sideId, inventory.map(q => ({ type: q.type, quantity: Number(q.input.value) })));
        draft.editAgency(index, { unitTypeId: type.value, sideId: side.value, controller: controller.value }, readOrder());
      }); });
      return form;
    }));
    ui.placements.replaceChildren(...draft.unitKeys.map((key, index) => button(unitLabel(index), () => {
      selection = { kind: "unit", key }; setTool("Select"); render();
    })));
    ui.placements.append(...draft.machineKeys.map((key, index) => button(machineLabel(index), () => {
      selection = { kind: "machine", key }; setTool("Select"); render();
    })));
    ui.placements.append(...(draft.definition.resources ?? []).map(r => button(`${resourceSymbol(r.type)} ${r.type.name} · ${r.position.x},${r.position.y}`, () => {
      selection = { kind: "cell", position: r.position }; setTool("Select"); showPanel("inspect"); render();
    })));
    syncBrushAgency();
  }
  function renderRoutes() {
    ui.routes.replaceChildren();
    ui.routes.append(element("h3", "Waypoints"));
    for (const waypoint of [...(draft.definition.waypoints ?? []), null]) {
      const form = editorForm(`waypoint:${waypoint?.id ?? "new"}`);
      form.dataset.waypointId = waypoint?.id ?? "new";
      const id = field(form, "Waypoint ID", waypoint?.id ?? "");
      const geometry = ["x", "y", "w", "h"].map(key => field(form, key.toUpperCase(), waypoint?.area[key] ?? (key === "w" || key === "h" ? 1 : 0), { type: "number" }));
      form.append(button("Draw Waypoint Area", () => beginPick("area", "Draw Waypoint Area", area => {
        bufferedValues(form, Object.fromEntries(Object.entries(area).map(([key, value]) => [key.toUpperCase(), String(value)])));
      })));
      form.append(element("button", waypoint ? "Save Waypoint" : "Create Waypoint"));
      if (waypoint) form.append(button("Delete Waypoint", () => commit(() => draft.removeWaypoint(waypoint.id), [form.dataset.formKey])));
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.saveWaypoint(waypoint?.id, {
        id: id.value, area: Object.fromEntries(["x", "y", "w", "h"].map((key, i) => [key, Number(geometry[i].value)])) })); });
      ui.routes.append(form);
    }
    ui.routes.append(element("h3", "Routes"));
    for (const route of [...(draft.definition.routes ?? []), null]) {
      const form = editorForm(`route:${route?.id ?? "new"}`);
      const id = field(form, "Route ID", route?.id ?? "");
      const mode = field(form, "Route mode", route?.mode ?? "Loop", { entries: ["Once", "Loop", { value: "PingPong", label: "Ping-pong" }] });
      const sequence = [...(buffers[form.dataset.formKey]?.sequence ?? route?.waypointIds ?? [])], rows = element("div"); form.append(rows);
      function rememberSequence() { (buffers[form.dataset.formKey] ??= { values: {} }).sequence = [...sequence]; persist(); }
      function renderSequence() {
        rows.replaceChildren();
        sequence.forEach((value, i) => {
          const row = element("div");
          const select = globalField(row, `Waypoint ${i + 1}`, value, { entries: (draft.definition.waypoints ?? []).map(w => w.id) });
          select.addEventListener("change", () => { sequence[i] = select.value; rememberSequence(); });
          row.append(button("Up", () => { if (i > 0) [sequence[i-1], sequence[i]] = [sequence[i], sequence[i-1]]; rememberSequence(); renderSequence(); }));
          row.append(button("Down", () => { if (i+1 < sequence.length) [sequence[i+1], sequence[i]] = [sequence[i], sequence[i+1]]; rememberSequence(); renderSequence(); }));
          row.append(button("Remove", () => { sequence.splice(i, 1); rememberSequence(); renderSequence(); })); rows.append(row);
        });
      }
      form.append(button("Add existing Waypoint", () => { if (draft.definition.waypoints?.length) { sequence.push(draft.definition.waypoints[0].id); rememberSequence(); renderSequence(); } }));
      renderSequence();
      form.append(element("button", route ? "Save Route" : "Create Route"));
      if (route) form.append(button("Delete Route", () => commit(() => draft.removeRoute(route.id), [form.dataset.formKey])));
      const reset = field(form, "Reset affected group progress", "No", { entries: ["No", "Yes"] });
      form.append(element("small", "Progress is retained while the current slot targets the same Waypoint. Changed targets or traversal mode require a reset to waypoint 0 for affected groups."));
      form.addEventListener("submit", event => { event.preventDefault(); commit(() => draft.saveRoute(route?.id, { id: id.value, mode: mode.value, waypointIds: sequence }, reset.value === "Yes")); });
      ui.routes.append(form);
    }
  }
  function objectEditor(form, objects, destination) {
    const panel = element('details'); panel.open = objects.length > 0;
    panel.append(element('summary', `${destination.unitKey != null ? 'Individual inventory' : 'Known ground objects'} · ${objects.length}`));
    const catalogObjects = draft.objectCatalog();
    for (const object of objects) {
      const row = element('div'); row.className = 'designer-loot-row';
      row.append(element('span', draft.objectName(object)),
        button('Remove', () => commit(() => draft.removeObject(object.id)))); panel.append(row);
    }
    const kind = field(panel, 'Item or Equipment', catalogObjects[0]?.key, { entries: catalogObjects.map(c => ({ value: c.key, label: `${c.name} (${c.category})` })) });
    panel.append(button('Add object', () => commit(() => draft.addObject(catalogObjects.find(c => c.key === kind.value).reference, destination))));
    form.append(panel);
  }
  function render() { refreshMetadata(); syncImportedTypes(); syncTerrainTypes(); renderRoutes(); syncWorldFields(); renderProperties(); renderData(); renderBoard(); updateDocumentState(); }
  ui["declare-form"].addEventListener("submit", event => { event.preventDefault(); commit(() => { draft.declare(ui["declare-type"].value); draft.changed(); }); });
  ui["agency-form"].addEventListener("submit", event => { event.preventDefault(); commit(() =>
    draft.setAgency(ui["agency-type"].value, ui["agency-side"].value, ui["agency-controller"].value)); });
  ui.validate.addEventListener("click", () => ready() ? requests.run("validate", draft.snapshot()) : null);
  ui.export.addEventListener("click", async () => {
    if (!ready()) return;
    const result = await requests.run("export", draft.snapshot());
    if (result?.transport) { lastExport = ui.transport.value = result.transport; showPanel("document"); ui.transport.closest("details").open = true; }
  });
  ui.import.addEventListener("click", async () => {
    const result = await requests.run("import", { transport: ui.transport.value.trim() });
    if (result?.board) {
      try { if (!canReplace()) return; loadDocument(JSON.stringify(draftDocument(result, "Imported scenario"))); report({ valid: true, errors: [] }); }
      catch (error) { report({ valid: false, errors: [{ message: error.message }] }, "import"); }
    }
  });
  ui.play.addEventListener("click", async () => {
    if (pendingPlay || !ready()) return;
    const revision = draft.revision;
    const result = await requests.run("export", draft.snapshot());
    if (!result?.transport || revision !== draft.revision) return;
    pendingPlay = true; ui.play.disabled = true;
    // Block editing only during game replacement, so the exported version is the one being played.
    ui.main.inert = true;
    try {
      if (await playDesign(result.transport)) { persist(); setMode(false); }
      else report({ valid: false, errors: [{ message: "Could not start the design. Check Playtest status, refresh and try again." }] });
    } catch (error) { report({ valid: false, errors: [{ message: error.message }] }); }
    finally { pendingPlay = false; ui.play.disabled = false; ui.main.inert = false; }
  });
  ui.copy.addEventListener("click", async () => {
    try { await navigator.clipboard.writeText(ui.transport.value); }
    catch { ui.transport.focus(); ui.transport.select(); }
  });
  function numbersComplete(form) {
    return [...form.querySelectorAll("input")].every(input => !input.dataset.number || input.disabled ||
      input.dataset.optional && input.value === "" || /^-?\d+$/.test(input.value) && Number.isSafeInteger(Number(input.value)));
  }
  function prepareStatic(id, fields) {
    const form = ui[id]; form.dataset.formKey = id;
    for (const name of fields) {
      const input = ui[name];
      if (input.type === "number") { input.type = "text"; input.inputMode = "numeric"; input.dataset.number = "true"; }
      const remember = () => {
        (buffers[id] ??= { values: {} }).values[name] = input.type === "checkbox" ? input.checked : input.value;
        requests.sequence++; persist();
      };
      input.addEventListener("input", remember);
      // Use input for text, change for selects and checkbox without replacing existing change behavior.
      input.addEventListener("change", remember);
    }
    const listen = form.addEventListener.bind(form);
    form.addEventListener = (name, action) => listen(name, name === "submit" ? event => {
      event.preventDefault();
      if (!numbersComplete(form)) { ui.status.textContent = "Complete numeric inputs before applying. Inputs are retained."; return; }
      activeForm = id; try { action(event); } finally { activeForm = null; }
    } : action);
    restoreStatic(id);
  }
  function restoreStatic(id) {
    for (const [name, value] of Object.entries(buffers[id]?.values ?? {})) {
      if (!ui[name]) continue;
      if (ui[name].type === "checkbox") ui[name].checked = value; else ui[name].value = value;
    }
  }
  function ready() {
    if (!Object.keys(buffers).length) return true;
    ui.status.textContent = "Unfinished forms · apply or discard the retained inputs first. Save draft keeps all inputs.";
    ui.pending.scrollIntoView?.({ block: "nearest" }); return false;
  }
  function canReplace() {
    if (!Object.keys(buffers).length && ui.title.value === "Untitled scenario" &&
        JSON.stringify(draft.definition) === JSON.stringify(new DesignerDraft(catalog).definition)) return true;
    return !globalThis.confirm || globalThis.confirm("Replace this draft? Save draft first to keep a separate file. The current scenario can be restored with Undo; unfinished form inputs will be replaced.");
  }
  function showPanel(name) {
    if (!picker && name !== "sides") objectiveSide = null;
    if (!picker && name !== "groups") highlightedGroup = null;
    for (const panel of ui.main.querySelectorAll("[data-designer-panel]")) panel.hidden = panel.dataset.designerPanel !== name;
    for (const tab of ui.main.querySelectorAll("[data-designer-tab]")) tab.setAttribute("aria-pressed", String(tab.dataset.designerTab === name));
  }
  function loadDocument(text) {
    const doc = readDraftDocument(text, catalog.maxBoardSize);
    // Renderability checks are distinct from engine validation. Roll back if content cannot be displayed.
    const previous = history.capture(), previousRevision = draft.revision, previousBuffers = buffers, previousTitle = ui.title.value, previousSelection = selection;
    try {
      draft.replace(doc.definition); restoreKeys(doc); buffers = doc.buffers ?? {}; ui.title.value = doc.title || "Untitled scenario";
      selection = null; syncBoardFields(); render();
    } catch (error) {
      Object.assign(draft, previous); draft.revision = previousRevision; buffers = previousBuffers; ui.title.value = previousTitle; selection = previousSelection;
      syncBoardFields(); render(); throw new Error(`Could not open draft: ${error.message}`);
    }
    history.past.push(previous); if (history.past.length > 100) history.past.shift(); history.future = []; persistenceBlocked = false;
    generationSource = doc.generationSource ?? null;
    librarySource = doc.librarySource ?? null;
    if (ui["library-cover"]) ui["library-cover"].value = doc.coverArt ?? "";
    librarySequence++;
    describeLibrary();
    unchecked(); ui.transport.value = doc.transportInput ?? ""; persist(); updateDocumentState();
  }
  function describeLibrary(message) {
    if (ui["library-status"]) ui["library-status"].textContent = message ?? (librarySource
      ? `${librarySource.source === "bundled" ? "Bundled scenario · saving creates a local override" : librarySource.source === "override" ? "Local override of bundled scenario" : "Local scenario"} · revision ${librarySource.revision}. Save to library updates this scenario in every campaign.`
      : "Local workspace · not saved to the shared library. Playtest uses this workspace directly.");
    if (ui["library-restore"]) ui["library-restore"].hidden = librarySource?.source !== "override";
    renderCoverPreview();
  }
  function renderCoverPreview() {
    const preview = ui["cover-preview"]; if (!preview) return;
    preview.replaceChildren();
    const value = ui["library-cover"]?.value.trim();
    if (!value || !(/^(\/[^/\\]|https:\/\/)/i.test(value)) || /[\\\u0000-\u001f]/.test(value)) { preview.hidden = true; return; }
    preview.hidden = false;
    const image = element("img"); image.alt = "Scenario cover illustration"; image.referrerPolicy = "no-referrer";
    image.addEventListener("error", () => { if (preview.children[0] === image) preview.replaceChildren(element("small", "Artwork unavailable. The scenario remains playable.")); });
    image.src = value; preview.append(image);
  }
  async function libraryRequest(path, method = "GET", body) {
    const response = await fetch(`/api/library${path}`, { method, cache: "no-store",
      ...(body === undefined ? {} : { headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) }) });
    const result = response.status === 204 ? null : await response.json();
    if (!response.ok) throw new Error(result?.detail ?? result?.errors?.[0]?.message ?? "Library request failed. Your workspace is unchanged.");
    return result;
  }
  async function refreshLibrary() {
    try {
      const result = await libraryRequest("");
      const selected = ui["library-select"].value;
      options(ui["library-select"], result.scenarios.map(s => ({ value: s.id, label: s.name })));
      if (result.scenarios.some(s => s.id === selected)) ui["library-select"].value = selected;
      ui["library-open"].disabled = !result.scenarios.length;
    } catch (error) { describeLibrary(error.message); }
  }
  async function openLibraryScenario(id) {
    if (libraryBusy || pendingPlay) return false;
    const before = JSON.stringify(documentData()), sequence = ++librarySequence;
    libraryOpening++;
    ui.main.setAttribute("aria-busy", "true");
    try {
      const saved = await libraryRequest(`/scenarios/${encodeURIComponent(id)}`);
      // Include title and unfinished forms in the guard, not just committed board changes.
      const now = documentData(), then = JSON.parse(before); delete now.savedAt; delete then.savedAt;
      if (sequence !== librarySequence || JSON.stringify(now) !== JSON.stringify(then)) {
        describeLibrary("Workspace changed while opening. Try again."); return false;
      }
      if (!canReplace()) return false;
      loadDocument(JSON.stringify({ ...draftDocument(saved.definition, saved.name), coverArt: saved.coverArt ?? "",
        librarySource: libraryIdentity(saved) }));
      setMode(true); return true;
    } catch (error) { describeLibrary(error.message); return false; }
    finally { ui.main.setAttribute("aria-busy", String(--libraryOpening > 0)); }
  }
  async function saveLibrary(asNew = false) {
    if (libraryBusy || pendingPlay || !ready()) return false;
    const source = asNew ? null : librarySource, sequence = librarySequence;
    const body = { name: ui.title.value, coverArt: ui["library-cover"]?.value || null, definition: draft.snapshot(),
      ...(source ? { revision: source.revision, changeToken: source.changeToken } : {}) };
    libraryBusy = true;
    ui["library-save"].disabled = ui["library-copy"].disabled = true;
    describeLibrary("Saving to the shared library…");
    try {
      const saved = await libraryRequest(`/scenarios${source ? `/${encodeURIComponent(source.id)}` : ""}`, source ? "PUT" : "POST", body);
      if (sequence === librarySequence) {
        librarySource = libraryIdentity(saved); persist();
        describeLibrary(`Saved “${saved.name}” to the shared library. Further workspace edits remain local until saved.`);
      }
      globalThis.dispatchEvent?.(new Event("delve-library-changed"));
      await refreshLibrary(); return true;
    } catch (error) { describeLibrary(`${error.message} Use Save as new to keep a separate scenario, or reopen the saved version.`); return false; }
    finally { libraryBusy = false; ui["library-save"].disabled = ui["library-copy"].disabled = false; }
  }
  function libraryIdentity(saved) {
    return { id: saved.id, revision: saved.revision, ...(saved.source ? { source: saved.source } : {}), ...(saved.changeToken ? { changeToken: saved.changeToken } : {}) };
  }
  async function importCover(file) {
    if (libraryBusy || pendingPlay || !file) return false;
    const sequence = librarySequence, previousCover = ui["library-cover"].value;
    libraryBusy = true; ui["cover-import"].disabled = true;
    describeLibrary("Importing cover image…");
    try {
      const png = await prepareCover(file);
      const response = await fetch("/api/library/covers", { method: "POST", headers: { "Content-Type": "image/png" }, body: png });
      const result = await response.json();
      if (!response.ok) throw new Error(result.detail ?? "Cover import failed.");
      if (sequence !== librarySequence || ui["library-cover"].value !== previousCover) { describeLibrary("Workspace or cover changed during import. Existing edits retained; import again when ready."); return false; }
      ui["library-cover"].value = result.coverArt; persist();
      describeLibrary("Cover imported locally. Save to library to apply it to this scenario in every campaign."); return true;
    } catch (error) { describeLibrary(`${error.message} Existing cover retained.`); return false; }
    finally { libraryBusy = false; ui["cover-import"].disabled = false; ui["cover-file"].value = ""; }
  }
  async function restoreBundled() {
    if (libraryBusy || pendingPlay || librarySource?.source !== "override") return false;
    libraryBusy = true; ui.main.inert = true;
    try {
      const saved = await libraryRequest(`/scenarios/${encodeURIComponent(librarySource.id)}/restore`, "POST", { revision: librarySource.revision, changeToken: librarySource.changeToken });
      loadDocument(JSON.stringify({ ...draftDocument(saved.definition, saved.name), coverArt: saved.coverArt ?? "", librarySource: libraryIdentity(saved) }));
      ui["library-restore-confirm"].hidden = true;
      globalThis.dispatchEvent?.(new Event("delve-library-changed")); return true;
    } catch (error) { describeLibrary(`${error.message} Workspace retained.`); return false; }
    finally { libraryBusy = false; ui.main.inert = false; }
  }
  function download(name, text) {
    const link = element("a"); const url = URL.createObjectURL(new Blob([text], { type: "application/json" }));
    link.href = url; link.download = name; link.click(); URL.revokeObjectURL(url);
  }
  function installDocumentControls() {
    const recoveryDownload = document.getElementById("designer-recovery-download");
    recoveryDownload.hidden = !(persistenceBlocked && savedRecovery);
    recoveryDownload.addEventListener("click", () => download("designer-recovery.json", savedRecovery));
    ui.title.addEventListener("input", persist);
    ui.transport.addEventListener("input", () => { lastExport = ""; persist(); });
    ui.save.addEventListener("click", () => {
      download(`${ui.title.value.replace(/[^a-z0-9_-]/gi, "-") || "scenario"}.delve-draft.json`, JSON.stringify(documentData(), null, 2));
      ui["save-status"].textContent = "Draft file saved · includes unfinished inputs";
    });
    ui.open.addEventListener("click", () => ui.file.click());
    ui.file.addEventListener("change", async () => {
      const file = ui.file.files?.[0]; if (!file) return;
      try {
        const text = await file.text(); readDraftDocument(text, catalog.maxBoardSize);
        if (canReplace()) loadDocument(text);
      } catch (error) { ui.status.textContent = error.message; }
      finally { ui.file.value = ""; }
    });
    ui.new.addEventListener("click", () => {
      if (!canReplace()) return;
      const blank = new DesignerDraft(catalog);
      loadDocument(JSON.stringify(draftDocument(blank.snapshot())));
      ui["save-status"].textContent = "New draft · previous scenario available with Undo";
    });
    function travel(direction) {
      if (!ready()) return;
      if (history[direction]()) { selection = null; syncBoardFields(); unchecked(); persist(); render(); }
    }
    ui.undo.addEventListener("click", () => travel("undo")); ui.redo.addEventListener("click", () => travel("redo"));
    document.addEventListener("keydown", event => {
      if (ui.main.hidden || event.target?.closest?.("input,textarea,select") || !(event.ctrlKey || event.metaKey)) return;
      if (event.key.toLowerCase() === "z") { event.preventDefault(); travel(event.shiftKey ? "redo" : "undo"); }
      if (event.key.toLowerCase() === "y") { event.preventDefault(); travel("redo"); }
    });
    globalThis.addEventListener?.("storage", event => {
      if (event.key !== draftStorageKey) return;
      persistenceBlocked = true;
      ui["save-status"].textContent = "Recovery changed in another tab · Save draft to keep this version, or reload to recover the other version";
    });
    for (const tab of ui.main.querySelectorAll("[data-designer-tab]")) tab.addEventListener("click", () => showPanel(tab.dataset.designerTab));
    document.getElementById("designer-discard-inputs").addEventListener("click", () => {
      if (!globalThis.confirm || globalThis.confirm("Discard all unfinished form inputs? Committed scenario edits will stay.")) { buffers = {}; syncBoardFields(); render(); persist(); }
    });
    options(ui.catalog, (catalog.scenarios ?? []).map(s => ({ value: s.id, label: s.name })));
    ui["catalog-open"].addEventListener("click", async () => {
      const revision = draft.revision;
      try {
        const response = await fetch(`/api/designer/scenarios/${encodeURIComponent(ui.catalog.value)}`);
        if (!response.ok) throw new Error("Could not open catalog scenario.");
        const definition = await response.json();
        if (draft.revision !== revision) { ui.status.textContent = "Draft changed while opening the catalog copy. Try again."; return; }
        if (canReplace()) loadDocument(JSON.stringify(draftDocument(definition, catalog.scenarios.find(s => s.id === ui.catalog.value)?.name)));
      } catch (error) { ui.status.textContent = error.message; }
    });
  }
  installDocumentControls();
  ui["library-save"]?.addEventListener("click", () => saveLibrary());
  ui["library-copy"]?.addEventListener("click", () => saveLibrary(true));
  ui["library-open"]?.addEventListener("click", () => openLibraryScenario(ui["library-select"].value));
  ui["library-refresh"]?.addEventListener("click", refreshLibrary);
  ui["library-cover"]?.addEventListener("input", () => { persist(); renderCoverPreview(); });
  ui["cover-import"]?.addEventListener("click", () => ui["cover-file"].click());
  ui["cover-file"]?.addEventListener("change", () => importCover(ui["cover-file"].files?.[0]));
  ui["library-restore"]?.addEventListener("click", () => { ui["library-restore-confirm"].hidden = false; });
  ui["library-restore-cancel"]?.addEventListener("click", () => { ui["library-restore-confirm"].hidden = true; });
  ui["library-restore-apply"]?.addEventListener("click", restoreBundled);
  globalThis.delveOpenLibraryScenario = openLibraryScenario;
  globalThis.addEventListener?.("delve-library-changed", refreshLibrary);
  describeLibrary();
  if (typeof fetch === "function") refreshLibrary();
  globalThis.delveOpenGenerated = result => {
    if (!canReplace()) return false;
    const doc = draftDocument(result.definition, result.code.startsWith("DNG2-OS-") ? "Overgrown Stronghold" : "Generated dungeon");
    doc.generationSource = { code: result.code, definitionJson: JSON.stringify(result.definition) };
    loadDocument(JSON.stringify(doc)); setMode(true); return true;
  };
  setTool("Select");
  try { syncBoardFields(); render(); }
  catch (error) {
    // A damaged recovery payload must never prevent the author from opening a healthy document.
    draft.replace(new DesignerDraft(catalog).snapshot()); buffers = {}; selection = null; librarySource = null;
    if (ui["library-cover"]) ui["library-cover"].value = "";
    ui.title.value = "Untitled scenario"; ui.transport.value = ""; persistenceBlocked = true;
    recoveryMessage = `Recovery unavailable: ${error.message}. Original recovery data retained. Download it before New/Open.`;
    document.getElementById("designer-recovery-download").hidden = !savedRecovery;
    syncBoardFields(); render();
  }
  recovering = false; ui["save-status"].textContent = recoveryMessage;
  try { if (!globalThis.delveNavigate && globalThis.localStorage?.getItem("project-delve.mode") === "designer") setMode(true); } catch {}
  globalThis.dispatchEvent?.(new Event("delve-designer-ready"));
  return { draft, history, documentData, loadDocument, render, setTool, setMode, requests, selectTarget, ui, saveLibrary, openLibraryScenario, refreshLibrary, importCover, restoreBundled };
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
