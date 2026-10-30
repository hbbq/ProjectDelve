import { configurationCode, configurationFromCode, GeneratorSession } from "./generator-state.js";
import { applyBoardVisual, terrainDisplay } from "./board-visuals.js";
import { styleSide } from "./side-colors.js";

const byId = id => document.getElementById(`generator-${id}`);
const dialog = document.getElementById("dungeon-generator");
const fields = ["profile", "width", "height", "rooms", "excavation", "connectivity", "doors", "openness", "terrain", "treasure", "density", "difficulty", "objective", "required", "fog", "loot", "seed"];
const state = new GeneratorSession();
let playing = false, currentGameCode = null;
const config = () => Object.fromEntries(fields.map(id => [id, byId(id).value]));
const signature = () => JSON.stringify(config());
function status(message) { byId("status").textContent = message; }
function changed() {
  state.changed();
  const stronghold = byId("profile").value === "OS";
  byId("heading").textContent = stronghold ? "Overgrown Stronghold" : "Heroes vs Monsters";
  byId("profile-description").textContent = stronghold ? "Explore a reclaimed fortress: outdoor approaches, ruined buildings, defenders and optional treasure." : "Connected dungeon rooms, corridors and encounters.";
  for (const id of ["rooms", "excavation", "connectivity", "doors"]) byId(`${id}-label`).hidden = stronghold;
  for (const id of ["openness", "terrain", "treasure"]) byId(`${id}-label`).hidden = !stronghold;
  for (const id of ["width", "height"]) byId(id).min = stronghold ? "18" : "14";
  byId("loot-help").hidden = !stronghold;
  byId("configuration-help").textContent = stronghold ? "Dimensions 18–50. Try 18 × 18 for a compact encounter, 30 × 24 for an expedition, or 42 × 32 for exploration. Openness shapes approaches; landscape changes cover and atmosphere. Difficulty selects Threat; density selects population." : "Dimensions 14–50; rooms need 6 × 6 regions. Extra routes are limited by distinct room pairs. Difficulty selects Threat; density selects population.";
  byId("required-label").hidden = byId("objective").value !== "reach";
  try { byId("prefix").value = configurationCode(config(), false); }
  catch (error) { byId("prefix").value = ""; status(error.message); return; }
  status(state.result && state.outdated(signature()) ? "Preview outdated · configuration changed. The map and complete code below still belong to the previous generation." : state.result ? "Preview ready · complete map revealed." : "Ready. Preview reveals the map; Play Unseen keeps it hidden.");
}
for (const id of fields.filter(id => id !== "profile")) byId(id).addEventListener("input", changed);
byId("profile").addEventListener("input", () => {
  byId("width").value = "30"; byId("height").value = byId("profile").value === "OS" ? "24" : "20";
  changed();
});
for (const node of document.querySelectorAll("[data-open-generator]")) node.addEventListener("click", () => {
  // Opening configuration must never reveal a retained omniscient preview from earlier work.
  byId("preview-content").hidden = true;
  status("Choose an adventure profile or load a code. Generate Preview reveals the map; Play Unseen keeps it hidden.");
  dialog.showModal();
});
byId("close").addEventListener("click", () => dialog.close());
dialog.addEventListener("close", () => state.changed());
function randomSeed() {
  byId("seed").value = [...crypto.getRandomValues(new Uint32Array(2))].map(value => value.toString(16).padStart(8, "0")).join("").toUpperCase();
  changed();
}
byId("random").addEventListener("click", randomSeed);
function loadCode() {
  const loaded = configurationFromCode(byId("code-input").value);
  for (const id of fields) byId(id).value = loaded[id];
  changed(); status("Code loaded · choose Generate Preview or Play Unseen.");
  return configurationCode(loaded);
}
byId("load").addEventListener("click", () => { try { loadCode(); } catch (error) { status(error.message); } });
async function generate(code = configurationCode(config())) {
  const ticket = state.begin(signature());
  status("Generating adventure…");
  try {
    const response = await fetch("/api/generation", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ code }), cache: "no-store" });
    const result = await response.json();
    if (ticket.sequence !== state.sequence) return;
    if (!response.ok) throw new Error(result.detail ?? "Generation failed.");
    if (!state.accept(ticket, result)) return;
    renderPreview(); status("Preview ready · complete map revealed. Configuration changes do not regenerate it.");
  } catch (error) { if (ticket.sequence === state.sequence) status(error.message); }
}
byId("preview").addEventListener("click", () => { try { void generate(configurationCode(config())); } catch (error) { status(error.message); } });
byId("another").addEventListener("click", () => { try { randomSeed(); void generate(configurationCode(config())); } catch (error) { status(error.message); } });

async function play(code) {
  if (playing) return;
  playing = true; state.changed();
  // Hide any authoring result before submitting; the import publishes only the observed board.
  byId("preview-content").hidden = true; status("Starting unseen adventure…");
  for (const id of ["play", "code-play", "play-preview"]) byId(id).disabled = true;
  try {
    if (!globalThis.delvePlayGenerated) throw new Error("Playtest is still loading. Try again.");
    const loaded = await globalThis.delvePlayGenerated(code);
    if (!loaded) throw new Error("Adventure did not start. Check the Playtest error and retry.");
    document.getElementById("mode-playtest").click(); dialog.close();
  } catch (error) { status(error.message); }
  finally { playing = false; for (const id of ["play", "code-play", "play-preview"]) byId(id).disabled = false; }
}
byId("play").addEventListener("click", () => { try { void play(configurationCode(config())); } catch (error) { status(error.message); } });
byId("code-play").addEventListener("click", () => { try { void play(loadCode()); } catch (error) { status(error.message); } });
byId("play-preview").addEventListener("click", () => { if (state.result) void play(state.result.code); });
byId("designer").addEventListener("click", () => {
  if (!state.result) return;
  if (!globalThis.delveOpenGenerated) { status("Designer is still loading. Try again."); return; }
  if (globalThis.delveOpenGenerated(state.result)) dialog.close();
});
byId("export").addEventListener("click", () => {
  if (!state.result) return;
  byId("transport").value = state.result.transport; byId("export-label").hidden = false;
  byId("transport").focus(); byId("transport").select();
  status("Exact preview exported as DELVE1. Copy this to preserve the concrete scenario.");
});
async function copy(value, field) {
  try { await navigator.clipboard.writeText(value); status("Copied to clipboard."); }
  catch { field?.focus(); field?.select(); status("Clipboard unavailable. The code is selected; use Ctrl+C to copy."); }
}
byId("copy-prefix").addEventListener("click", () => { try { void copy(configurationCode(config(), false), byId("prefix")); } catch (error) { status(error.message); } });
byId("copy-complete").addEventListener("click", () => { if (state.result) void copy(state.result.code, byId("complete")); });
document.getElementById("generated-game-copy").addEventListener("click", async () => {
  if (!currentGameCode) return;
  try { await navigator.clipboard.writeText(currentGameCode); }
  catch { const range = document.createRange(); range.selectNodeContents(document.getElementById("generated-game-code")); const selection = getSelection(); selection.removeAllRanges(); selection.addRange(range); }
});
globalThis.addEventListener("delve-generation-state", event => {
  currentGameCode = event.detail.code;
  document.getElementById("generated-game").hidden = !currentGameCode;
  document.getElementById("generated-game-code").textContent = currentGameCode ?? "";
});
document.getElementById("generated-game-inspect").addEventListener("click", () => {
  if (!currentGameCode) return;
  // A deliberate second click states the reveal warning; native confirmation dialogs are unnecessary.
  byId("code-input").value = currentGameCode; loadCode();
  byId("preview-content").hidden = true;
  status("Inspecting reveals the full map and Monster placements. Choose Generate Preview to reveal this map, or Close to continue playing unseen.");
  dialog.showModal();
});
byId("view").addEventListener("change", () => { byId("board").dataset.view = byId("view").value; });

const element = (tag, value = "") => { const node = document.createElement(tag); node.textContent = value; return node; };
function renderPreview() {
  const result = state.result, definition = result.definition, board = definition.board, root = byId("board");
  root.dataset.generationCode = result.code;
  root.replaceChildren(); root.style.gridTemplateColumns = `repeat(${board.width}, 1fr)`; root.style.gridTemplateRows = `repeat(${board.height}, 1fr)`;
  root.style.aspectRatio = `${board.width} / ${board.height}`; root.style.setProperty("--columns", board.width);
  const terrain = new Map(board.cells.map(c => [`${c.position.x},${c.position.y}`,
    { kind: c.terrain, typeId: c.terrainTypeId, direction: c.direction }]));
  const presentationBoard = { terrainTypes: result.terrainDefinitions };
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const tile = terrain.get(`${x},${y}`) ?? { kind: board.defaultTerrain, typeId: board.defaultTerrainTypeId };
    const display = terrainDisplay(presentationBoard, tile), kind = display.kind;
    const node = element("div"); node.className = `cell ${kind}${display.fallback ? ' custom-terrain' : ''}`; node.title = `${display.name} (${x},${y})`;
    applyBoardVisual(node, "terrain", kind, { seed: result.code, x, y, direction: tile.direction, terrain, assembly: display.assembly }); root.append(node);
  }
  for (const edge of board.edges) {
    const node = element("div"); node.className = `edge ${edge.kind}`;
    const right = edge.direction === "Right";
    Object.assign(node.style, { left: `${(edge.position.x + (right ? 1 : .5)) / board.width * 100}%`, top: `${(edge.position.y + (right ? .5 : 1)) / board.height * 100}%`, width: right ? "4px" : `${100 / board.width}%`, height: right ? `${100 / board.height}%` : "4px" }); root.append(node);
  }
  const caches = new Map();
  for (const marker of definition.lootMarkers ?? []) {
    const key = `${marker.position.x},${marker.position.y}`, cache = caches.get(key) ?? { position: marker.position, count: 0 };
    cache.count++; caches.set(key, cache);
  }
  for (const { position, count } of caches.values()) {
    const node = element("span", `◆${count}`); node.className = "generator-loot"; node.title = `${count} unopened Loot markers (${position.x},${position.y})`;
    Object.assign(node.style, { left: `${(position.x + .5) / board.width * 100}%`, top: `${(position.y + .5) / board.height * 100}%` }); root.append(node);
  }
  for (const unit of definition.units) {
    const name = unit.unitTypeId.replace(/-type$/, "").split("-").map(word => word[0].toUpperCase() + word.slice(1)).join(" ");
    const node = element("div", name.split(" ").map(word => word[0]).join("")); node.className = "figure"; node.title = name; styleSide(node, unit.sideId);
    const offsets = result.unitMetadata?.find(type => type.unitTypeId === unit.unitTypeId)?.footprintOffsets ?? [{ x: 0, y: 0 }];
    const width = Math.max(...offsets.map(cell => cell.x)) + 1, height = Math.max(...offsets.map(cell => cell.y)) + 1;
    if (width > 1 || height > 1) node.className += " large";
    Object.assign(node.style, { left: `${(unit.anchor.x + width / 2) / board.width * 100}%`, top: `${(unit.anchor.y + height / 2) / board.height * 100}%`, width: `${(width * 100 - 15) / board.width}%`, height: `${(height * 100 - 15) / board.height}%` }); root.append(node);
  }
  for (const objective of definition.objectives ?? []) {
    if (!objective.area) continue;
    const a = objective.area, node = element("span", "Reach Area"); node.className = "mission-area"; styleSide(node, objective.sideId);
    Object.assign(node.style, { left: `${a.x / board.width * 100}%`, top: `${a.y / board.height * 100}%`, width: `${a.w / board.width * 100}%`, height: `${a.h / board.height * 100}%` }); root.append(node);
  }
  byId("complete").value = result.code;
  const stronghold = result.code.startsWith("DNG2-OS-");
  const deck = definition.loot;
  const lootSummary = deck?.preset ? ` · ${deck.preset} Loot ×${deck.presetMultiplier ?? 1}` : "";
  const planSummary = stronghold && result.diagnostics.sitePlan ? `${result.diagnostics.sitePlan} · ` : "";
  const routeSummary = stronghold && result.diagnostics.alternateRouteDistance ? ` · alternate approach ${result.diagnostics.alternateRouteDistance} steps` : "";
  byId("preview-summary").textContent = `${planSummary}${result.diagnostics.rooms.length} ${stronghold ? "sites" : "rooms"} · ${result.diagnostics.monsters} Monsters · ${result.diagnostics.occupiedRooms} occupied ${stronghold ? "sites" : "rooms"} · destination ${result.diagnostics.destinationDistance} steps away${routeSummary}${stronghold ? ` · ${caches.size} caches / ${definition.lootMarkers?.length ?? 0} unopened Loot` : ""}${lootSummary} · fingerprint ${result.fingerprint}`;
  byId("export-label").hidden = true; byId("transport").value = ""; byId("preview-content").hidden = false;
}
changed();
