import { GeneratorSession } from "./generator-state.js";
import { worldConfigurationCode, worldConfigurationFromCode, constructionPreview, constructionRange } from "./world-generator-state.js";
import { applyBoardVisual, terrainDisplay } from "./board-visuals.js";

const byId = id => document.getElementById(`world-generator-${id}`);
const dialog = document.getElementById("world-generator");
const enrichmentFields = ["enrichmentEnabled", "enrichmentIntensity", "forestEnrichment", "rockEnrichment", "waterEnrichment", "grasslandEnrichment"];
const civilizationFields = ["civilizationEnabled", "campsiteCount", "settlementCount", "preparationIntensity"];
const routeFields = ["routesEnabled", "throughRoutes", "routeProfile", "connectSites", "explorationLinks", "waterCrossings", "routeIntensity"];
const fields = ["width", "height", "offsetX", "offsetY", "forestCoverage", "waterCoverage", "rockCoverage", "scale", "waterForm", "seed", ...enrichmentFields, ...civilizationFields, ...routeFields];
const session = new GeneratorSession();
let presets = [], catalogPromise, maxBoardSize = 120;
let selectedStage = 0, compareBefore = false;
let selectedOperation = null, selectedSpace = null, traceFrame = null;
const traceButtons = new Map();
const config = () => Object.fromEntries(fields.map(id => [id, byId(id).value]));
const signature = () => JSON.stringify(config());
const status = message => { byId("status").textContent = message; };
function changed() {
  session.changed();
  try { byId("prefix").value = worldConfigurationCode(config(), false, maxBoardSize); worldConfigurationCode(config(), true, maxBoardSize); }
  catch (error) { status(error.message); return; }
  status(session.outdated(signature()) ? "Preview outdated · the retained map and completed code belong to the previous generation." : session.result ? "Terrain preview ready · awaiting scenario authoring." : "Choose a world window, then generate a terrain preview.");
}
for (const field of fields) byId(field).addEventListener("input", changed);
async function loadCatalog() {
  try {
    const response = await fetch("/api/world-generation/catalog", { cache: "no-store" });
    if (!response.ok) throw new Error("World presets could not be loaded. You can still use custom settings.");
    const catalog = await response.json(); presets = catalog.presets;
    maxBoardSize = catalog.maxBoardSize;
    byId("width").max = byId("height").max = maxBoardSize;
    byId("dimension-help").textContent = `Dimensions 1–${maxBoardSize}. Offsets locate this window in the world. Coverage totals must be at most 100%; local terrain proportions vary by window.`;
    changed();
    for (const preset of presets) {
      const option = document.createElement("option"); option.value = preset.id; option.textContent = preset.name; byId("preset").append(option);
    }
  } catch (error) { status(error.message); catalogPromise = null; }
}
for (const node of document.querySelectorAll("[data-open-world-generator]")) node.addEventListener("click", () => {
  dialog.showModal();
  if (!catalogPromise) catalogPromise = loadCatalog();
});
byId("close").addEventListener("click", () => dialog.close());
dialog.addEventListener("close", () => session.changed());
byId("preset").addEventListener("change", () => {
  const preset = presets.find(p => p.id === byId("preset").value);
  if (!preset) return;
  for (const [key, value] of Object.entries(preset.settings)) if (fields.includes(key)) byId(key).value = value;
  const enrichment = preset.enrichment ?? preset.settings.enrichment;
  if (enrichment) for (const [field, key] of [["enrichmentEnabled", "enabled"], ["enrichmentIntensity", "intensity"], ["forestEnrichment", "forest"], ["rockEnrichment", "rock"], ["waterEnrichment", "water"], ["grasslandEnrichment", "grassland"]]) byId(field).value = String(enrichment[key]);
  const civilization = preset.civilization ?? preset.settings.civilization;
  if (civilization) for (const [field, key] of [["civilizationEnabled", "enabled"], ["campsiteCount", "campsites"], ["settlementCount", "settlements"], ["preparationIntensity", "preparationIntensity"]]) byId(field).value = String(civilization[key]);
  const routes = preset.routes ?? preset.settings.routes;
  if (routes) for (const [field, key] of [["routesEnabled", "enabled"], ["throughRoutes", "throughRoutes"], ["routeProfile", "profile"], ["connectSites", "connectSites"], ["explorationLinks", "alternatives"], ["waterCrossings", "waterCrossings"], ["routeIntensity", "intensity"]]) byId(field).value = String(routes[key]);
  changed();
});
for (const field of ["forestCoverage", "waterCoverage", "rockCoverage", "scale", "waterForm", ...enrichmentFields, ...civilizationFields, ...routeFields])
  byId(field).addEventListener("change", () => { byId("preset").value = ""; });
function randomSeed() {
  byId("seed").value = [...crypto.getRandomValues(new Uint32Array(2))].map(value => value.toString(16).padStart(8, "0")).join("").toUpperCase(); changed();
}
byId("load").addEventListener("click", () => {
  try {
    const loaded = worldConfigurationFromCode(byId("code-input").value, maxBoardSize);
    const defaults = { civilizationEnabled: "false", campsiteCount: "2", settlementCount: "1", preparationIntensity: "2",
      routesEnabled: "false", throughRoutes: "1", routeProfile: "MinorPath", connectSites: "true", explorationLinks: "1", waterCrossings: "false", routeIntensity: "2" };
    for (const field of fields) byId(field).value = loaded[field] ?? defaults[field] ?? (field === "enrichmentEnabled" ? "false" : field === "enrichmentIntensity" ? "2" : enrichmentFields.includes(field) ? "true" : "");
    byId("preset").value = ""; changed(); status("Recipe loaded · choose Generate Preview.");
  } catch (error) { status(error.message); }
});
async function generate() {
  let code;
  try { code = worldConfigurationCode(config(), true, maxBoardSize); } catch (error) { status(error.message); return; }
  const ticket = session.begin(signature()); status("Generating world terrain…");
  selectedOperation = null; selectedSpace = null; traceFrame = null; compareBefore = false; byId("construction-tree").replaceChildren(); byId("construction").hidden = true;
  try {
    const response = await fetch("/api/world-generation", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ code }), cache: "no-store" });
    const result = await response.json();
    if (ticket.sequence !== session.sequence) return;
    if (!response.ok) throw new Error(result.detail ?? "World generation failed.");
    if (!session.accept(ticket, result)) return;
    byId("export-label").hidden = true; byId("transport").value = "";
    // Once a world is created, moving its window keeps that world. Generate
    // Another explicitly selects a new seed; clearing the field also opts in.
    if (!byId("seed").value.trim()) byId("seed").value = worldConfigurationFromCode(result.code).seed;
    session.signature = signature();
    selectedStage = (result.preview?.frames.length ?? 1) - 1; compareBefore = false;
    selectedOperation = null; selectedSpace = null; traceFrame = null;
    const select = byId("stage"); select.replaceChildren();
    for (const [index, frame] of (result.preview?.frames ?? []).entries()) {
      const option = document.createElement("option"); option.value = String(index); option.textContent = `${index + 1}. ${stageName(frame)}`; select.append(option);
    }
    byId("steps").hidden = !result.preview?.frames.length;
    renderPreview(); status("Terrain preview ready · awaiting scenario authoring. Add Units, Sides and Objectives in Designer.");
  } catch (error) { if (ticket.sequence === session.sequence) status(error.message); }
}
byId("preview").addEventListener("click", () => { void generate(); });
byId("another").addEventListener("click", () => { randomSeed(); void generate(); });
byId("view").addEventListener("change", () => { byId("board").dataset.view = byId("view").value; });
function zoomPreview() {
  const pixels = Number(byId("zoom").value);
  byId("board").style.width = session.result && [12, 24, 48].includes(pixels) ? `${session.result.definition.board.width * pixels}px` : "";
}
byId("zoom").addEventListener("change", zoomPreview);
const stageName = frame => String(frame.stage ?? frame.stepId).replace(/([a-z])([A-Z])/g, "$1 $2");
function showStage(index) {
  const frames = session.result?.preview?.frames;
  if (!frames?.length) return;
  selectedStage = Math.max(0, Math.min(frames.length - 1, index));
  selectedOperation = null; selectedSpace = null; traceFrame = null;
  if (selectedStage === 0) compareBefore = false;
  renderPreview();
}
byId("stage").addEventListener("change", () => showStage(Number(byId("stage").value)));
byId("previous").addEventListener("click", () => showStage(selectedStage - 1));
byId("next").addEventListener("click", () => showStage(selectedStage + 1));
byId("compare").addEventListener("click", () => { compareBefore = !compareBefore; renderPreview(); });
for (const name of ["changed", "boundaries", "components", "opportunities", "sites", "buildings", "rooms", "usable", "site-entrances",
  "route-intents", "route-centerlines", "route-corridors", "route-junctions", "route-crossings", "route-access"])
  byId(name).addEventListener("change", () => { if (session.result) renderPreview(); });
byId("designer").addEventListener("click", () => {
  if (!session.result) return;
  if (!globalThis.delveOpenGenerated) { status("Designer is still loading. Try again."); return; }
  if (globalThis.delveOpenGenerated(session.result)) dialog.close();
});
byId("export").addEventListener("click", () => {
  if (!session.result) return;
  byId("transport").value = session.result.transport; byId("export-label").hidden = false;
  byId("transport").focus(); byId("transport").select(); status("Exact terrain exported as DELVE1 · awaiting scenario authoring.");
});
byId("copy-complete").addEventListener("click", async () => {
  if (!session.result) return;
  try { await navigator.clipboard.writeText(session.result.code); status("Completed world code copied."); }
  catch { byId("complete").focus(); byId("complete").select(); status("Clipboard unavailable. The completed code is selected; use Ctrl+C to copy."); }
});
function renderPreview() {
  const started = performance.now(), result = session.result, board = result.definition.board, root = byId("board");
  const { offsetX, offsetY } = result.window;
  const preview = result.preview, selected = preview?.frames[selectedStage];
  const frame = selectedOperation ? constructionPreview(preview, selectedStage, selectedOperation, compareBefore, board.width)
    : selected && preview.frames[compareBefore ? Math.max(0, selectedStage - 1) : selectedStage];
  const scroll = byId("board-wrap"), scrollLeft = scroll.scrollLeft, scrollTop = scroll.scrollTop;
  const changed = new Set((selectedOperation ? frame : selected)?.changedCells ?? []), boundaries = new Set(preview?.boundaryCells ?? []);
  const chokepoints = new Set(frame?.chokepoints ?? []);
  const entrances = new Map(), opportunities = new Map();
  for (const entrance of frame?.entrances ?? []) for (const index of [entrance.insideIndex, entrance.outsideIndex]) {
    if (!entrances.has(index)) entrances.set(index, []); entrances.get(index).push(entrance);
  }
  for (const opportunity of frame?.opportunities ?? []) {
    if (!opportunities.has(opportunity.anchorIndex)) opportunities.set(opportunity.anchorIndex, []);
    opportunities.get(opportunity.anchorIndex).push(opportunity);
  }
  root.replaceChildren(); root.dataset.generationCode = result.code;
  root.style.gridTemplateColumns = `repeat(${board.width}, 1fr)`; root.style.gridTemplateRows = `repeat(${board.height}, 1fr)`;
  root.style.aspectRatio = `${board.width} / ${board.height}`; root.style.setProperty("--columns", board.width);
  root.style.setProperty("--world-board-ratio", board.width / board.height);
  const overrides = new Map(board.cells.map(c => [`${c.position.x},${c.position.y}`, { kind: c.terrain, typeId: c.terrainTypeId, direction: c.direction }]));
  const terrain = new Map(), counts = new Map(), presentationBoard = { terrainTypes: result.terrainDefinitions };
  // Full absolute-coordinate context keeps appearance stable across overlapping windows.
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const typeId = frame && preview.terrainPalette[frame.terrain.charCodeAt(y * board.width + x) - 65];
    terrain.set(`${x + offsetX},${y + offsetY}`, typeId ? { typeId } : overrides.get(`${x},${y}`) ?? { kind: board.defaultTerrain, typeId: board.defaultTerrainTypeId });
  }
  const fragment = document.createDocumentFragment();
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const worldX = x + offsetX, worldY = y + offsetY, tile = terrain.get(`${worldX},${worldY}`);
    const display = terrainDisplay(presentationBoard, tile), node = document.createElement("div");
    node.className = `cell ${display.kind}${display.fallback ? " custom-terrain" : ""}`; node.title = `${display.name} (${worldX},${worldY})`;
    node.dataset.worldX = worldX; node.dataset.worldY = worldY;
    applyBoardVisual(node, "terrain", display.kind, { seed: result.visualSeed, x: worldX, y: worldY, direction: tile.direction, terrain, assembly: display.assembly });
    if (frame) {
      const index = y * board.width + x, component = frame.walkableComponents?.[index] ?? -1;
      const formation = preview.formations?.[preview.formationLabels?.[index]];
      if (byId("changed").checked && changed.has(index)) node.className += " world-changed";
      if (byId("boundaries").checked && boundaries.has(index)) node.className += " world-boundary";
      if (byId("components").checked && component >= 0) {
        node.className += " world-component"; node.style.setProperty("--component-color", `hsl(${component * 137.508 % 360} 80% 65% / .48)`);
      }
      if (byId("components").checked && chokepoints.has(index)) node.className += " world-chokepoint";
      if (byId("opportunities").checked && (entrances.has(index) || opportunities.has(index))) {
        const marker = document.createElement("span");
        marker.className = `world-analysis-marker${entrances.has(index) ? " world-entrance" : ""}${opportunities.has(index) ? " world-opportunity" : ""}`;
        node.append(marker);
      }
      const details = [node.title, `Stage: ${stageName(frame)}`, `Walkable component: ${component >= 0 ? component : "blocked"}`];
      if (formation) details.push(`Original formation ${formation.id}: ${formation.kind}; core ${formation.coreArea}, envelope ${formation.envelopeArea}, density ${Number(formation.density).toFixed(2)}, depth ${formation.maxDepth}${formation.touchesBoundary ? "; touches window boundary" : ""}`);
      for (const entrance of entrances.get(index) ?? []) details.push(`Entrance ${entrance.id}: feature ${entrance.featureId}, width ${entrance.width}`);
      for (const opportunity of opportunities.get(index) ?? []) details.push(`Opportunity ${opportunity.id}: ${opportunity.kind}; usable area ${opportunity.usableArea}; component ${opportunity.walkableComponent}${opportunity.requiresConnection ? "; requires connection" : ""}${opportunity.otherBankIndex != null ? `; opposite bank (${opportunity.otherBankIndex % board.width + offsetX},${Math.floor(opportunity.otherBankIndex / board.width) + offsetY}), Water span ${opportunity.waterSpan}` : ""}`);
      if (chokepoints.has(index)) details.push("Chokepoint");
      node.title = details.join("\n");
      const inspect = () => { byId("inspection").textContent = node.title; };
      node.addEventListener("pointerenter", inspect); node.addEventListener("click", inspect);
    }
    counts.set(display.name, (counts.get(display.name) ?? 0) + 1); fragment.append(node);
  }
  root.append(fragment);
  renderEdges(root, frame?.edges ?? board.edges ?? [], (selectedOperation ? frame : selected)?.changedEdges ?? [], board.width, board.height);
  renderPlanSpaces(root, selected, board.width, board.height);
  renderRoutes(root, selected, board.width, board.height, result.window);
  renderConstruction(selected);
  zoomPreview();
  scroll.scrollLeft = scrollLeft; scroll.scrollTop = scrollTop;
  if (selected) {
    byId("stage").value = String(selectedStage); byId("previous").disabled = selectedStage === 0;
    byId("next").disabled = selectedStage === preview.frames.length - 1;
    byId("compare").disabled = selectedStage === 0; byId("compare").textContent = compareBefore ? "Before" : "After";
    byId("compare").setAttribute?.("aria-pressed", String(compareBefore));
    const d = frame.diagnostics;
    const operation = selected.construction?.find(operation => operation.id === selectedOperation);
    byId("diagnostics").textContent = `${stageName(frame)} · ${compareBefore ? "Before" : "After"}${operation ? ` ${operation.name} · server analysis describes the accepted stage` : ""} · selected step changes ${changed.size} · features added ${d.featuresAdded} · walkable ${d.walkableArea} · components ${d.componentCount} · largest ${d.largestComponentArea} · route searches ${d.routingSearches} · visited ${d.routingVisitedCells}`;
    byId("inspection").textContent = "Hover or click a Cell to inspect server analysis. Overlays: gold changes, cyan formation boundaries, coloured components, red chokepoints, white entrances, purple opportunities.";
  }
  byId("complete").value = result.code;
  const elapsed = (performance.now() - started).toFixed(1); root.dataset.renderMs = elapsed;
  byId("preview-summary").textContent = `${board.width} × ${board.height} · world (${offsetX}, ${offsetY}) · ${[...counts].map(([name, count]) => `${name} ${count} (${(100 * count / (board.width * board.height)).toFixed(1)}%)`).join(" · ")} · fingerprint ${result.fingerprint.slice(0, 16)}`;
  const metrics = result.metrics, stageMetric = metrics?.stages?.find(stage => stage.stepId === frame?.stepId);
  byId("performance").textContent = `Rendered ${board.width * board.height} terrain Cells in ${elapsed} ms.${metrics ? ` Server frame stage ${stageMetric?.milliseconds?.toFixed(1) ?? "—"} ms / ${stageMetric?.allocatedBytes ?? "—"} allocated bytes; finalization ${metrics.finalizationMilliseconds.toFixed(1)} ms; preview raw data estimate ${metrics.retainedPreviewBytes} bytes.` : ""}`;
  byId("preview-content").hidden = false;
}

function renderEdges(root, edges, changes, width, height) {
  const key = edge => `${edge.position.y * width + edge.position.x}:${edge.direction}`;
  const changed = new Map(changes.map(change => [`${change.key.index}:${change.key.direction}`, change]));
  const present = new Set();
  const draw = (edge, removed = false) => {
    const node = document.createElement("div"), id = key(edge), right = edge.direction === "Right";
    node.className = `edge ${edge.kind}${byId("changed").checked && changed.has(id) ? " world-edge-changed" : ""}${removed ? " world-edge-removed" : ""}`;
    node.dataset.edgeKey = id; node.title = `${edge.kind} (${edge.position.x},${edge.position.y}) ${edge.direction}`;
    Object.assign(node.style, { left: `${(edge.position.x + (right ? 1 : .5)) / width * 100}%`, top: `${(edge.position.y + (right ? .5 : 1)) / height * 100}%`, width: right ? "4px" : `${100 / width}%`, height: right ? `${100 / height}%` : "4px" });
    root.append(node);
  };
  for (const edge of edges) if (edge.kind !== "None") { present.add(key(edge)); draw(edge); }
  if (byId("changed").checked) for (const [id, change] of changed) if (!present.has(id))
    draw({ position: { x: change.key.index % width, y: Math.floor(change.key.index / width) }, direction: change.key.direction, kind: "None" }, true);
}

function renderConstruction(frame) {
  const operations = frame?.construction ?? [], panel = byId("construction"); panel.hidden = !operations.length;
  if (traceFrame !== frame) {
    traceFrame = frame; traceButtons.clear(); const tree = byId("construction-tree"); tree.replaceChildren();
    const parents = new Set(operations.map(operation => operation.parentId)), nodes = new Map();
    for (const operation of operations) {
      const container = document.createElement(parents.has(operation.id) ? "details" : "div");
      container.dataset.traceId = operation.id;
      container.dataset.traceIndex = String(nodes.size);
      container.className = "world-construction-node"; container.open = operation.parentId === operations[0]?.parentId;
      const heading = parents.has(operation.id) ? document.createElement("summary") : container;
      const button = document.createElement("button"); button.type = "button"; button.textContent = operation.name;
      button.dataset.operationId = operation.id; button.title = operation.id;
      button.dataset.operationIndex = container.dataset.traceIndex;
      button.addEventListener("click", event => { event?.preventDefault?.(); selectedOperation = operation.id; selectedSpace = null; compareBefore = false; renderPreview(); });
      heading.append(button); if (heading !== container) container.append(heading);
      (nodes.get(operation.parentId) ?? tree).append(container); nodes.set(operation.id, container); traceButtons.set(operation.id, button);
    }
    for (const site of frame?.sites ?? []) for (const building of site.buildings ?? []) for (const room of building.rooms ?? []) {
      const layoutId = room.id.slice(0, room.id.lastIndexOf("/room-"));
      const layout = operations.find(operation => operation.objectId === layoutId);
      const button = document.createElement("button"); button.type = "button"; button.className = "world-room-inspection";
      button.dataset.spaceId = room.id; button.textContent = `${room.id} · ${room.usable.area}/${room.interior.area} usable · ${room.portalIds.length} Doors · ${room.twoByTwoPlacements} 2 × 2 placements`;
      button.addEventListener("click", () => { selectedSpace = room.id; selectedOperation = layout?.id ?? building.id; compareBefore = false; renderPreview(); });
      (nodes.get(layout?.id) ?? nodes.get(building.id) ?? tree).append(button);
    }
  }
  for (const [id, button] of traceButtons) { button.dataset.selected = String(id === selectedOperation); button.setAttribute?.("aria-pressed", String(id === selectedOperation)); }
  const operation = operations.find(operation => operation.id === selectedOperation);
  byId("construction-selection").textContent = operation ? `${selectedSpace ?? operation.id} · ${compareBefore ? "Before" : "After"} the complete ${selectedSpace ? "containing room layout" : "selected subtree"} · (${operation.bounds.x}, ${operation.bounds.y}), ${operation.bounds.width} × ${operation.bounds.height}` : "Choose a construction step. Parent steps inspect their complete subtree; Before shows its preceding checkpoint.";
  const stats = frame?.civilizationStats;
  const costs = (frame?.sites ?? []).map(site => `${site.id}: preparation ${site.preparation.cost}, trees ${site.preparation.treesRemoved}, rock ${site.preparation.rockExcavated}, Water ${site.preparation.waterFilled}`);
  byId("civilization-summary").textContent = stats ? `${stats.sites} sites · ${stats.campsites} campsites · ${stats.settlements} settlements · ${stats.buildings} buildings · ${stats.rooms} rooms · ${stats.attempts} attempts · work ${stats.work} · terrain changes ${stats.changedTerrain} · Edge changes ${stats.changedEdges}${costs.length ? "\n" + costs.join("\n") : ""}` : "";
  const failures = frame?.planningFailures ?? [], list = byId("planning-failures"); list.replaceChildren();
  for (const failure of failures.slice(0, 24)) { const item = document.createElement("li"); item.textContent = `${failure.requestId} · ${failure.code}: ${failure.reason} (${failure.attempts} attempts)`; list.append(item); }
  byId("planning-failure-panel").hidden = !failures.length;
}

function renderPlanSpaces(root, frame, width, height) {
  const area = (value, objectId, kind, label) => {
    for (const span of value?.spans ?? []) {
      const node = document.createElement("div"); node.className = `world-plan-space world-plan-${kind}`;
      node.dataset.objectId = objectId; node.dataset.areaId = value.id; node.dataset.spaceKind = kind; node.title = `${label}: ${objectId} · accepted plan space`;
      Object.assign(node.style, { left: `${span.x / width * 100}%`, top: `${span.y / height * 100}%`, width: `${span.length / width * 100}%`, height: `${100 / height}%` }); root.append(node);
    }
  };
  const portal = value => {
    for (const index of [value.insideIndex, value.outsideIndex]) {
      const node = document.createElement("span"); node.className = "world-plan-portal"; node.dataset.objectId = value.id;
      node.title = `Accepted entrance ${value.id} · ${value.kind} · width ${value.width}`;
      Object.assign(node.style, { left: `${(index % width + .5) / width * 100}%`, top: `${(Math.floor(index / width) + .5) / height * 100}%` }); root.append(node);
    }
  };
  for (const site of frame?.sites ?? []) {
    if (byId("sites").checked) area(site.footprint, site.id, "site", site.kind);
    if (byId("usable").checked) area(site.exterior, site.id, "usable", "Exterior");
    if (byId("site-entrances").checked) for (const entrance of site.entrances ?? []) portal(entrance);
    for (const building of site.buildings ?? []) {
      if (byId("buildings").checked) area(building.footprint, building.id, "building", "Building");
      if (byId("site-entrances").checked) for (const entrance of building.portals ?? []) portal(entrance);
      for (const room of building.rooms ?? []) {
        if (byId("rooms").checked) area(room.interior, room.id, "room", room.purpose);
        if (byId("usable").checked) area(room.usable, room.id, "usable", "Room usable space");
      }
    }
  }
  const selected = frame?.construction?.find(operation => operation.id === selectedOperation);
  const room = (frame?.sites ?? []).flatMap(site => site.buildings ?? []).flatMap(building => building.rooms ?? []).find(room => room.id === selectedSpace);
  if (selected || room) {
    const node = document.createElement("div"), b = room?.interior.bounds ?? selected.bounds; node.className = "world-construction-bounds"; node.dataset.objectId = room?.id ?? selected.objectId;
    Object.assign(node.style, { left: `${b.x / width * 100}%`, top: `${b.y / height * 100}%`, width: `${b.width / width * 100}%`, height: `${b.height / height * 100}%` }); root.append(node);
  }
}

// Draw only server-authored spans, runs, ports and endpoint pairs. These overlays do not infer routes or connectivity.
function renderRoutes(root, frame, width, height, window) {
  const network = frame?.routeNetwork, routes = network?.routes ?? [], sites = frame?.sites ?? [];
  const inspector = byId("route-inspector"), entries = byId("route-details"); entries.replaceChildren();
  inspector.hidden = !network && !sites.some(site => site.accessPorts?.length);
  const accepted = `Accepted metadata for ${frame ? stageName(frame) : "this stage"}; also shown during Before and construction inspection.`;
  byId("route-authority").textContent = accepted;
  const area = (value, id, kind, label) => {
    for (const span of value?.spans ?? []) {
      const node = document.createElement("div"); node.className = `world-route-area world-route-${kind}`;
      node.dataset.routeObjectId = id; node.dataset.routeKind = kind; node.title = `${label} · ${id} · accepted stage metadata`;
      Object.assign(node.style, { left: `${span.x / width * 100}%`, top: `${span.y / height * 100}%`, width: `${span.length / width * 100}%`, height: `${100 / height}%` }); root.append(node);
    }
  };
  const marker = (index, id, kind, label) => {
    const node = document.createElement("span"); node.className = `world-route-marker world-route-${kind}`;
    node.dataset.routeObjectId = id; node.dataset.routeKind = kind; node.title = label;
    Object.assign(node.style, { left: `${(index % width + .5) / width * 100}%`, top: `${(Math.floor(index / width) + .5) / height * 100}%` }); root.append(node);
  };
  const segment = (x, y, endX, endY, id, kind, label) => {
    const node = document.createElement("div"); node.className = `world-route-line world-route-${kind}`;
    node.dataset.routeObjectId = id; node.dataset.routeKind = kind; node.title = label;
    Object.assign(node.style, { left: `${(x + .5) / width * 100}%`, top: `${(y + .5) / height * 100}%`,
      width: `${Math.hypot(endX - x, endY - y) / width * 100}%`, transform: `rotate(${Math.atan2(endY - y, endX - x) * 180 / Math.PI}deg)` }); root.append(node);
  };
  const detail = (title, text, kind) => {
    const item = document.createElement("details"), summary = document.createElement("summary"), body = document.createElement("p");
    item.className = "world-route-detail"; item.dataset.routeDetail = kind; summary.textContent = title; body.textContent = text;
    item.append(summary, body); entries.append(item);
  };
  for (const route of routes) {
    const p = route.preparation, s = route.stats;
    if (byId("route-corridors").checked) area(route.corridor, route.id, "corridor", "Accepted corridor");
    if (byId("route-centerlines").checked) {
      for (const run of route.geometry?.runs ?? []) {
        const x = run.worldX - window.offsetX, y = run.worldY - window.offsetY;
        const dx = run.direction === "East" ? run.length : run.direction === "West" ? -run.length : 0;
        const dy = run.direction === "South" ? run.length : run.direction === "North" ? -run.length : 0;
        segment(x, y, x + dx, y + dy, route.id, "centerline", `Accepted ${route.profile} centerline · ${route.id}`);
      }
      for (const port of [route.start, route.end]) if (port) marker(port.anchorIndex, port.id, "port", `Accepted route port ${port.id} · ${port.kind} (${port.worldX}, ${port.worldY})${port.siteId ? ` · Site ${port.siteId}` : ""}`);
    }
    if (byId("route-crossings").checked) {
      for (const crossing of route.crossings ?? []) area(crossing.footprint, route.id, "crossing", "Accepted ordinary terrain Water fill");
      for (const mouth of route.tunnelEntrances ?? []) marker(mouth.anchorIndex, route.id, "mouth", `Excavated tunnel mouth · ${route.id} (${mouth.worldX}, ${mouth.worldY})`);
    }
    detail(`${route.id} · ${route.purpose} · ${route.profile}`, `Accepted corridor: ${route.corridor.area} Cells; width ${route.realizedWidth}; verified ${route.verifiedFootprint} × ${route.verifiedFootprint} footprint. ` +
      `Ports: ${route.start.id} (${route.start.worldX}, ${route.start.worldY}) → ${route.end.id} (${route.end.worldX}, ${route.end.worldY}). ` +
      `Surface ${route.surfaceTypeId ?? "server ground"}; realized ${(route.realizedSurfaceTypeIds ?? []).join(", ") || "server terrain"}. Requires opening Doors: ${route.requiresOpeningDoors ? "yes" : "no"}. Length ${s.length}; turns ${s.turns}; reused Cells ${s.reusedCells}; search cost ${s.searchCost}; attempts ${s.attempts}; expanded states ${s.expandedStates}; peak queue ${s.queuePeak}. ` +
      `Preparation cost ${p.cost}; trees ${p.treesRemoved}; rock ${p.rockExcavated}; Water filled ${p.waterFilled}; terrain changes ${p.changedTerrain}; Edge changes ${p.changedEdges}. ` +
      `Water crossing spans: ${(route.crossings ?? []).map(crossing => `${crossing.footprint.area} Cells`).join(", ") || "none"}; tunnel mouths: ${(route.tunnelEntrances ?? []).map(mouth => `(${mouth.worldX}, ${mouth.worldY})`).join(", ") || "none"}. ` +
      `Materialization ${route.materializationId}; source revision ${route.sourceRevision}.`, "route");
  }
  for (const intent of network?.intents ?? []) {
    if (byId("route-intents").checked) {
      marker(intent.startIndex, intent.id, "intent-port", `Intended link start · ${intent.id} · ${intent.status}`);
      if (intent.destinationIndex != null) {
        marker(intent.destinationIndex, intent.id, "intent-port", `Intended link destination · ${intent.id} · ${intent.status}`);
        segment(intent.startIndex % width, Math.floor(intent.startIndex / width), intent.destinationIndex % width, Math.floor(intent.destinationIndex / width),
          intent.id, "intent", `Intended endpoint pair only, not a computed path · ${intent.id} · ${intent.status}`);
      }
    }
    detail(`${intent.status} · ${intent.id}`, `${intent.purpose} · ${intent.profile}${intent.destinationObjectId ? ` · destination ${intent.destinationObjectId}` : ""}${intent.routeId ? ` · accepted route ${intent.routeId}` : ""}. ` +
      `Start (${intent.startIndex % width + window.offsetX}, ${Math.floor(intent.startIndex / width) + window.offsetY})${intent.destinationIndex != null ? `; destination (${intent.destinationIndex % width + window.offsetX}, ${Math.floor(intent.destinationIndex / width) + window.offsetY})` : "; no destination endpoint supplied"}. ` +
      `${intent.reason ?? "No additional explanation supplied."} Intent endpoint pairs show requested links, not computed paths.`, "intent");
  }
  if (byId("route-junctions").checked) {
    for (const shared of network?.sharedSegments ?? []) area(shared.area, shared.id, "shared", `Shared corridor · ${shared.routeIds.join(", ")}`);
    for (const junction of network?.junctions ?? []) area(junction.contact, junction.id, "junction", `Junction · ${junction.routeIds.join(", ")}`);
  }
  for (const junction of network?.junctions ?? []) detail(`Junction · ${junction.id}`,
    `${junction.contact.area} contact Cells; Routes ${junction.routeIds.join(", ")}.`, "junction");
  for (const shared of network?.sharedSegments ?? []) detail(`Shared segment · ${shared.id}`,
    `${shared.area.area} shared corridor Cells; Routes ${shared.routeIds.join(", ")}.`, "shared");
  for (const site of sites) for (const port of site.accessPorts ?? []) {
    if (byId("route-access").checked) {
      area(port.approach, port.id, "approach", `Published Site approach · exact owner ${port.ownerId}`);
      marker(port.anchorIndex, port.id, "access", `Site access ${port.id} · ${port.direction} · verified width ${port.verifiedWidth}`);
      marker(port.outwardIndex, port.id, "access", `Site outward port ${port.id} · ${port.direction}`);
    }
    detail(`Site access · ${port.id}`, `Exact owner ${port.ownerId}; direction ${port.direction}; verified width ${port.verifiedWidth}; published approach ${port.approach.area} Cells. ` +
      `Anchor (${port.anchorIndex % width + window.offsetX}, ${Math.floor(port.anchorIndex / width) + window.offsetY}); outward (${port.outwardIndex % width + window.offsetX}, ${Math.floor(port.outwardIndex / width) + window.offsetY}).`, "access");
  }
  for (const alternative of network?.alternatives ?? []) detail(`Alternative quality · ${alternative.routeId}`,
    `Previous distance ${alternative.previousDistance}; new distance ${alternative.newDistance}; new corridor Cells ${alternative.newCorridorCells}; shared Cells ${alternative.sharedCorridorCells}. ` +
    `Adds cycle: ${alternative.addsCycle ? "yes" : "no"}; bypasses selected bottleneck: ${alternative.bypassesSelectedBottleneck ? "yes" : "no"}; independent Site entrance: ${alternative.independentSiteEntrance ? "yes" : "no"}; connects region: ${alternative.connectsRegion ? "yes" : "no"}.`, "alternative");
  for (const attachment of network?.siteAttachments ?? []) detail(`Site attachment · ${attachment.siteId}`,
    `Port ${attachment.portId}; network component ${attachment.networkComponent}; Routes ${attachment.routeIds.join(", ")}. ` +
    `Ordinary open access: ${attachment.ordinaryOpenAccess ? "yes" : "no"}; Doors-openable access: ${attachment.doorsOpenableAccess ? "yes" : "no"}.`, "attachment");
  const s = network?.statistics;
  byId("route-summary").textContent = s ? `${s.acceptedRoutes} accepted Routes · ${network.topology.components.length} route components · board revision ${network.topology.boardRevision} · ` +
    `${s.searches} searches on accepted Routes · ${s.expandedStates} expanded states on accepted Routes · peak queue ${s.peakQueue} · planning attempts ${s.planningAttempts ?? "—"} · work ${s.planningWork ?? "—"} · ` +
    `preparation cost ${s.preparationCost} · terrain changes ${s.changedTerrain} · Edge changes ${s.changedEdges} · ${s.crossings} Water crossings · ${s.tunnelEntrances} tunnel mouths · ` +
    `${(network.intents ?? []).filter(intent => intent.status === "Unresolved").length} unresolved requests.` : "Published Site access; no route network accepted at this stage.";
}
changed();
