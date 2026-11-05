import { GeneratorSession } from "./generator-state.js";
import { worldConfigurationCode, worldConfigurationFromCode } from "./world-generator-state.js";
import { applyBoardVisual, terrainDisplay } from "./board-visuals.js";

const byId = id => document.getElementById(`world-generator-${id}`);
const dialog = document.getElementById("world-generator");
const fields = ["width", "height", "offsetX", "offsetY", "forestCoverage", "waterCoverage", "rockCoverage", "scale", "waterForm", "seed"];
const session = new GeneratorSession();
let presets = [], catalogPromise;
const config = () => Object.fromEntries(fields.map(id => [id, byId(id).value]));
const signature = () => JSON.stringify(config());
const status = message => { byId("status").textContent = message; };
function changed() {
  session.changed();
  try { byId("prefix").value = worldConfigurationCode(config(), false); worldConfigurationCode(config()); }
  catch (error) { status(error.message); return; }
  status(session.outdated(signature()) ? "Preview outdated · the retained map and completed code belong to the previous generation." : session.result ? "Terrain preview ready · awaiting scenario authoring." : "Choose a world window, then generate a terrain preview.");
}
for (const field of fields) byId(field).addEventListener("input", changed);
async function loadCatalog() {
  try {
    const response = await fetch("/api/world-generation/catalog", { cache: "no-store" });
    if (!response.ok) throw new Error("World presets could not be loaded. You can still use custom settings.");
    const catalog = await response.json(); presets = catalog.presets;
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
  changed();
});
for (const field of ["forestCoverage", "waterCoverage", "rockCoverage", "scale", "waterForm"])
  byId(field).addEventListener("change", () => { byId("preset").value = ""; });
function randomSeed() {
  byId("seed").value = [...crypto.getRandomValues(new Uint32Array(2))].map(value => value.toString(16).padStart(8, "0")).join("").toUpperCase(); changed();
}
byId("load").addEventListener("click", () => {
  try {
    const loaded = worldConfigurationFromCode(byId("code-input").value);
    for (const field of fields) byId(field).value = loaded[field];
    byId("preset").value = ""; changed(); status("Recipe loaded · choose Generate Preview.");
  } catch (error) { status(error.message); }
});
async function generate() {
  let code;
  try { code = worldConfigurationCode(config()); } catch (error) { status(error.message); return; }
  const ticket = session.begin(signature()); status("Generating world terrain…");
  try {
    const response = await fetch("/api/world-generation", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ code }), cache: "no-store" });
    const result = await response.json();
    if (ticket.sequence !== session.sequence) return;
    if (!response.ok) throw new Error(result.detail ?? "World generation failed.");
    if (!session.accept(ticket, result)) return;
    // Once a world is created, moving its window keeps that world. Generate
    // Another explicitly selects a new seed; clearing the field also opts in.
    if (!byId("seed").value.trim()) byId("seed").value = worldConfigurationFromCode(result.code).seed;
    session.signature = signature();
    renderPreview(); status("Terrain preview ready · awaiting scenario authoring. Add Units, Sides and Objectives in Designer.");
  } catch (error) { if (ticket.sequence === session.sequence) status(error.message); }
}
byId("preview").addEventListener("click", () => { void generate(); });
byId("another").addEventListener("click", () => { randomSeed(); void generate(); });
byId("view").addEventListener("change", () => { byId("board").dataset.view = byId("view").value; });
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
  root.replaceChildren(); root.dataset.generationCode = result.code;
  root.style.gridTemplateColumns = `repeat(${board.width}, 1fr)`; root.style.gridTemplateRows = `repeat(${board.height}, 1fr)`;
  root.style.aspectRatio = `${board.width} / ${board.height}`; root.style.setProperty("--columns", board.width);
  root.style.setProperty("--world-board-ratio", board.width / board.height);
  const overrides = new Map(board.cells.map(c => [`${c.position.x},${c.position.y}`, { kind: c.terrain, typeId: c.terrainTypeId, direction: c.direction }]));
  const terrain = new Map(), counts = new Map(), presentationBoard = { terrainTypes: result.terrainDefinitions };
  // Full absolute-coordinate context keeps appearance stable across overlapping windows.
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++)
    terrain.set(`${x + offsetX},${y + offsetY}`, overrides.get(`${x},${y}`) ?? { kind: board.defaultTerrain, typeId: board.defaultTerrainTypeId });
  const fragment = document.createDocumentFragment();
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const worldX = x + offsetX, worldY = y + offsetY, tile = terrain.get(`${worldX},${worldY}`);
    const display = terrainDisplay(presentationBoard, tile), node = document.createElement("div");
    node.className = `cell ${display.kind}${display.fallback ? " custom-terrain" : ""}`; node.title = `${display.name} (${worldX},${worldY})`;
    node.dataset.worldX = worldX; node.dataset.worldY = worldY;
    applyBoardVisual(node, "terrain", display.kind, { seed: result.visualSeed, x: worldX, y: worldY, direction: tile.direction, terrain, assembly: display.assembly });
    counts.set(display.name, (counts.get(display.name) ?? 0) + 1); fragment.append(node);
  }
  root.append(fragment);
  byId("complete").value = result.code;
  const elapsed = (performance.now() - started).toFixed(1); root.dataset.renderMs = elapsed;
  byId("preview-summary").textContent = `${board.width} × ${board.height} · world (${offsetX}, ${offsetY}) · ${[...counts].map(([name, count]) => `${name} ${count} (${(100 * count / (board.width * board.height)).toFixed(1)}%)`).join(" · ")} · fingerprint ${result.fingerprint.slice(0, 16)}`;
  byId("performance").textContent = `Rendered ${board.width * board.height} terrain Cells in ${elapsed} ms.`;
  byId("export-label").hidden = true; byId("transport").value = ""; byId("preview-content").hidden = false;
}
changed();
