import { bindTooltip, initializeTooltips } from "./tooltips.js";
import { groundLoot, lootCardDialog } from "./loot-ui.js";
import { styleSide, applySideHues } from "./side-colors.js";
import { applyBoardVisual, terrainDisplay } from "./board-visuals.js";
import { createBoardCamera } from "./board-camera.js";
import { createViewportRouter, boardCandidates } from "./playtest-viewport.js";
import { createUnitFacing } from "./unit-facing.js";
import { loadUnitVisualCatalog } from "./unit-visuals.js";
const unitFacing = createUnitFacing();

const ui = Object.fromEntries(["game-setup", "scenario", "scenario-description", "start-scenario", "restart-scenario", "scenario-transport", "import-scenario", "board", "status", "effect", "round", "refresh", "skip", "animate", "coordinates", "filter", "auto", "error", "prompt", "choices", "unit-card", "events", "bag", "active-token", "active-unit", "dice", "attack-context", "world-effects", "objectives", "orders", "game-result", "commentary-toggle", "commentary-panel", "commentary-context", "commentary-text", "decision-commentary", "side-controllers", "controller-status", "controller-retry", "controller-cancel"]
  .map(id => [id, document.getElementById(id)]));
initializeTooltips();
const toolsPanel = document.getElementById("playtest-tools");
toolsPanel.addEventListener("toggle", () => {
  if (toolsPanel.open) ui["game-setup"].open = true;
});
toolsPanel.addEventListener("keydown", event => {
  if (event.key === "Escape" && toolsPanel.open) {
    toolsPanel.open = false;
    document.getElementById("tools-toggle").focus();
  }
});
async function startFromTools(operation, body) {
  const previousContext = operation === "restart" ? playContext : null;
  const succeeded = await replaceScenario(operation, body, true);
  if (succeeded) {
    if (previousContext) globalThis.delveSetPlayContext(previousContext);
    toolsPanel.open = false;
    document.getElementById("tools-toggle").focus();
  }
  return succeeded;
}
const boardView = document.getElementById("board-view");
try {
  const savedView = globalThis.localStorage?.getItem("delve.board.view.v1");
  if (["classic", "enhanced", "three", "tabletop"].includes(savedView)) boardView.value = savedView;
} catch { /* Preferences are optional. */ }
for (const id of ["end-turn", "start-round", "bag-count", "decision-panel", "board-overlays", "move-overlay", "board-hint"]) ui[id] = document.getElementById(id);
ui["board-alternatives"] = document.getElementById("board-alternatives");
ui["board-alternatives"]?.addEventListener("keydown", event => {
  if (event.key !== "Escape") return;
  const disclosure = ui["board-alternatives"].querySelectorAll("details")[0];
  if (disclosure?.open) { event.preventDefault(); disclosure.open = false; threeChoicesOpen = false; disclosure.querySelector("summary")?.focus(); }
});
const reducedMotion = () => globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
const portraitViewer = document.getElementById("portrait-viewer");
const portraitImage = document.getElementById("portrait-image");
const portraitError = document.getElementById("portrait-error");
document.getElementById("close-portrait").addEventListener("click", () => portraitViewer.close());
portraitViewer.addEventListener("close", () => {
  const portrait = document.getElementById("unit-portrait");
  (portrait && !portrait.disabled ? portrait : ui["unit-card"]).focus();
});
portraitImage.addEventListener("error", () => { portraitImage.hidden = true; portraitError.hidden = false; });
// Native modal dialog supplies focus containment, Escape dismissal and inert board input.
function openPortrait(source, name) {
  portraitImage.hidden = false; portraitError.hidden = true;
  portraitImage.alt = name; portraitImage.src = source;
  document.getElementById("portrait-caption").textContent = name;
  portraitViewer.showModal();
}
let diceDismissal;
let diceGeneration = 0;
let diceSession;
let combatDice;
ui["move-overlay"].addEventListener("click", () => ui["board-overlays"].classList.toggle("at-top"));
ui["end-turn"].addEventListener("click", () => {
  const candidate = snapshot?.presentation.decision?.candidates.find(candidate => candidate.isEndTurn);
  if (candidate) return chooseCandidate(candidate.key);
});
ui["start-round"].addEventListener("click", () => { if (!ui.round.disabled) return mutate("round"); });
let viewportRouter;
let threeViewport;
let displayedFrame;
function updateBoardView() {
  // Do not rerender: mounted choices, selection, hover and in-flight playback survive.
  if (!["three", "tabletop"].includes(boardView.value)) ui.board.dataset.view = boardView.value === "enhanced" ? "enhanced" : "classic";
  if (viewportRouter) {
    if (!["three", "tabletop"].includes(boardView.value)) domViewport.mode = boardView.value;
    return viewportRouter.request(boardView.value);
  }
}
boardView.addEventListener("change", () => {
  try { globalThis.localStorage?.setItem("delve.board.view.v1", boardView.value); } catch { /* optional */ }
  return updateBoardView();
});
if (!boardView.value) boardView.value = "enhanced";
updateBoardView();
let boardCamera;
let snapshot;
let dismissedResultSession;
document.getElementById("show-result")?.addEventListener("click", () => {
  dismissedResultSession = undefined;
  if (displayedState) renderTurn(displayedState);
});
globalThis.addEventListener?.("delve-inspector-change", () => {
  if (!displayedState || !snapshot) return;
  renderUnitCard(); renderChoicePanel(); updateControls();
});
// Navigation context is UI metadata, scoped to the current authoritative session.
let playContext = null;
globalThis.delveSetPlayContext = context => {
  playContext = { ...context, sessionId: snapshot?.sessionId };
  renderSessionHeading();
};
function renderSessionHeading() {
  if (playContext && playContext.sessionId !== snapshot?.sessionId) playContext = null;
  const title = document.getElementById("session-title");
  const back = document.getElementById("session-return");
  if (!title || !back) return;
  title.textContent = playContext?.title ?? (snapshot?.generationCode ? (snapshot.generationCode.startsWith("DNG2-OS-") ? "Overgrown Stronghold" : "Generated dungeon")
    : snapshot?.scenarios?.find(scenario => scenario.id === snapshot.scenarioId)?.name
      ?? (snapshot?.scenarioId === "imported" ? "Imported scenario" : "Current game"));
  back.textContent = playContext?.returnLabel ?? "Back to scenarios";
  back.href = playContext?.returnHash ?? "#scenarios";
}
let queuedScenario;
let renderedScenarioId;
let busy = false;
let controllerSetupScenario;
let controllerSetupSession;
const controllerSelects = new Map();
const controllerSideDefaults = new Map();
const controllerOptions = ["Human", "Default Behavior", "Gambit"];
function controllerOptionSelect(label, includeMixed = false) {
  const select = document.createElement("select");
  select.setAttribute("aria-label", label);
  if (includeMixed) { const mixed = text("option", "Mixed"); mixed.value = ""; mixed.disabled = true; select.append(mixed); }
  for (const controller of controllerOptions) {
    const option = text("option", controller);
    option.value = controller; select.append(option);
  }
  return select;
}

function syncControllerSideDefaults() {
  for (const [sideId, select] of controllerSideDefaults) {
    const values = [...controllerSelects.values()].filter(entry => entry.sideId === sideId).map(entry => entry.value);
    select.value = new Set(values).size === 1 ? values[0] : "";
  }
}

function renderControllerSetup(force = false) {
  const selected = snapshot?.scenarios?.find(value => value.id === ui.scenario.value);
  const identity = selected?.id ?? snapshot?.scenarioId;
  if (!force && controllerSetupScenario === identity && controllerSetupSession === snapshot?.sessionId) return;
  controllerSetupScenario = identity; controllerSetupSession = snapshot?.sessionId;
  controllerSelects.clear(); controllerSideDefaults.clear(); ui["side-controllers"].replaceChildren();
  let assignments = identity === snapshot?.scenarioId ? snapshot?.controllerAssignments : selected?.controllerAssignments;
  if (!assignments?.length) {
    const controllers = identity === snapshot?.scenarioId ? snapshot?.controllers : selected?.controllers;
    assignments = (selected?.sideIds ?? Object.keys(controllers ?? {})).map(sideId => ({ sideId, controller: controllers?.[sideId] ?? "Human" }));
  }
  const currentTypes = new Map((snapshot?.result.state.types ?? []).map(type => [type.id, type.displayName ?? type.id]));
  if (identity !== snapshot?.scenarioId) for (const [typeId, name] of Object.entries(selected?.unitTypeNames ?? {})) currentTypes.set(typeId, name);
  const initiallyPresent = new Set((identity === snapshot?.scenarioId
    ? snapshot?.result.state.units?.map(unit => ({ typeId: unit.typeId, sideId: unit.sideId }))
    : selected?.initiallyPresentTokens ?? []).map(token => `${token.typeId}\0${token.sideId}`));
  const assignmentsBySide = new Map();
  for (const assignment of assignments) {
    const side = assignmentsBySide.get(assignment.sideId) ?? [];
    side.push(assignment); assignmentsBySide.set(assignment.sideId, side);
  }
  for (const [sideId, sideAssignments] of assignmentsBySide) {
    const group = document.createElement("fieldset"); group.className = "controller-side";
    const legend = text("legend", `Side ${sideId}`); group.append(legend);
    const sideDefault = controllerOptionSelect(`Set every controller for Side ${sideId}`, true);
    controllerSideDefaults.set(sideId, sideDefault);
    const distinct = new Set(sideAssignments.map(assignment => assignment.controller));
    sideDefault.value = distinct.size === 1 ? sideAssignments[0].controller : "";
    const sideLabel = text("label", "All Unit Types "); sideLabel.className = "controller-side-default";
    sideDefault.addEventListener("change", () => {
      for (const entry of controllerSelects.values()) {
        if (entry.sideId === sideId) entry.value = sideDefault.value;
        else if (sideDefault.value === "Gambit" && entry.value === "Gambit") entry.value = "Human";
      }
      syncControllerSideDefaults();
    });
    sideLabel.append(sideDefault); group.append(sideLabel);
    for (const assignment of sideAssignments) {
      const typeName = currentTypes.get(assignment.typeId) ?? assignment.typeId ?? "Controller";
      const absent = assignment.typeId && !initiallyPresent.has(`${assignment.typeId}\0${sideId}`) ? " (not initially present)" : "";
      const label = text("label", `${typeName}${absent} `), select = controllerOptionSelect(`Controller for ${typeName} on Side ${sideId}`);
      select.value = assignment.controller ?? "Human";
      select.addEventListener("change", () => {
        if (select.value === "Gambit") for (const entry of controllerSelects.values())
          if (entry.sideId !== sideId && entry.value === "Gambit") entry.value = "Human";
        syncControllerSideDefaults();
      });
      const key = assignment.typeId ? `${assignment.typeId}\0${sideId}` : sideId;
      select.typeId = assignment.typeId; select.sideId = sideId;
      controllerSelects.set(key, select);
      label.append(select); group.append(label);
    }
    ui["side-controllers"].append(group);
  }
  syncControllerSideDefaults();
}

function setupControllers() {
  const entries = [...controllerSelects.values()];
  if (!entries.length) return {};
  if (entries.every(entry => entry.typeId)) return { controllerAssignments: entries.map(entry =>
    ({ typeId: entry.typeId, sideId: entry.sideId, controller: entry.value })) };
  return { controllers: Object.fromEntries(entries.map(entry => [entry.sideId, entry.value])) };
}

function controllerOperationReady() {
  const completed = latestUpdate?.snapshot?.result.state.completion ?? snapshot?.result.state.completion;
  return !completed && !postInFlight && !commandPending && (!streaming || streamConnected);
}
function renderControllerStatus() {
  const view = latestUpdate?.snapshot ?? snapshot;
  const controller = view?.controllerStatus;
  const status = latestUpdate?.status ?? controller?.status;
  const completed = !!view?.result.state.completion;
  ui["controller-status"].textContent = completed ? "" : controller
    ? `Gambit: ${controller.sideId ?? ""}; ${controller.available === false ? "unavailable" : controller.status ?? status ?? "ready"}${controller.error ? `; ${controller.error}` : ""}` : "";
  ui["controller-retry"].hidden = !controller;
  ui["controller-cancel"].hidden = !controller;
  const recoverable = ["controller-error", "cancelled", "canceled", "error"].includes(status) || ["controller-error", "cancelled", "canceled", "error"].includes(controller?.status);
  ui["controller-retry"].disabled = !controllerOperationReady() || !(controller?.canRetry ?? recoverable);
  ui["controller-cancel"].disabled = !controllerOperationReady() || !(controller?.canCancel ?? (status === "waiting-external" || controller?.status === "waiting-external"));
  ui.auto.disabled ||= (view?.controllerAssignments ?? []).some(assignment => assignment.controller === "Gambit") || Object.values(view?.controllers ?? {}).includes("Gambit");
}
const streaming = typeof EventSource !== "undefined";
let updateSource;
let latestUpdate;
let receivedSequence = -1;
let displayedSequence = -1;
let playbackGeneration = 0;
let playbackRunning = false;
let playbackOperationActive = false;
let commandPending = false;
let pendingCommandId;
let postInFlight = false;
let postingCommandId;
let pendingReplacement;
let streamConnected = false;
let commandNumber = 0;
const playbackQueue = [];
const maxPlaybackQueue = 256;
// Presentation metadata remains separate from authoritative state and rules events.
let displayedCommentary;
let commentaryVisible = false;
function renderCommentary() {
  ui["commentary-panel"].hidden = !commentaryVisible;
  ui["commentary-toggle"].textContent = `${commentaryVisible ? "Hide" : "Show"} decision commentary`;
  ui["commentary-toggle"].setAttribute("aria-expanded", String(commentaryVisible));
  const comment = displayedCommentary;
  const unitName = snapshot?.presentation.cards?.[comment?.unitId]?.displayName;
  ui["commentary-context"].textContent = comment
    ? [unitName ? `${unitName} (${comment.unitId})` : comment.unitId, comment.controller, comment.sideId,
      comment.decisionKind?.replace(/([a-z])([A-Z])/g, "$1 $2")].filter(Boolean).join(" \u00b7 ") : "";
  ui["commentary-text"].textContent = comment?.text ?? "No commentary at this point in playback.";
}
function showCommentary(comment) {
  displayedCommentary = comment; // Keep Unit/decision identity available for future figure anchors.
  renderCommentary();
}
function clearCommentary() {
  displayedCommentary = undefined;
  ui["decision-commentary"].value = "";
  renderCommentary();
}
let skipEffects = false;
let skipLootCards = false;
let cancelPause;
let displayedState;
let latestUnitId;
let hoveredUnitId;
let displayedCards;
let displayedFigures;
let displayedOrders;
let setupPhase;
const cardChoiceKeys = new Set();
const boardChoiceKeys = new Set();
let boardChooser;
let boardChooserOrigin;
function closeBoardChooser() {
  boardChooser = null; clearAffectedPreview(); renderChoicePanel(); boardChooserOrigin?.focus();
}
ui["decision-panel"].addEventListener("keydown", event => {
  if (event.key === "Escape" && boardChooser) { event.preventDefault(); closeBoardChooser(); }
});
let attackContext;
let focusedUnitId;
let focusedDoor;
const figures = new Map();
const edges = new Map();
const cells = new Map();
const choiceFeedback = new Map();
let boardObjects = new Map();
const cameraViewport = document.querySelector?.("#camera-viewport");
if (cameraViewport) boardCamera = createBoardCamera({ document, viewport: cameraViewport,
  world: document.getElementById("camera-world"), boardElement: ui.board,
  animate: () => ui.animate.checked && !skipEffects,
  clearPreview: () => { hoveredUnitId = null; clearAffectedPreview(); },
  onChange: () => positionDecisionPanel() });

const domCameraControls = document.querySelector?.(".camera-controls");
const domViewport = { host: cameraViewport, mode: boardView.value === "classic" ? "classic" : "enhanced", camera: boardCamera,
  setActive(active) { cameraViewport.hidden = !active; domCameraControls.hidden = !active; if (!active) boardCamera.cancel(); },
  dispose() {} };
const threeActive = () => !!threeViewport && viewportRouter?.active === threeViewport;
const playCamera = () => threeActive() ? threeViewport.camera : boardCamera;
function syncThreeInteraction() {
  if (!threeActive()) return;
  const decision = !busy && displayedState === snapshot?.result.state ? snapshot.presentation.decision : null;
  threeViewport.showInteraction({ busy, candidates: [...(decision?.candidates ?? []), ...(decision?.noneChoice ? [decision.noneChoice] : [])].filter(visibleChoice),
    focusedUnitId, focusedDoor, relationship: combatDice?.context ?? attackContext,
    combatPositions: combatDice?.positions, preview: threePreview,
    coordinates: ui.coordinates.checked, waypoints: document.getElementById("waypoints").checked });
}
let threePreview;
let threeChoicesOpen = false;
function selectThreeHit(hit) {
  if (busy || !displayedState) return;
  const candidates = boardCandidates(hit, snapshot.presentation.decision, displayedState, displayedFigures, visibleChoice);
  if (!candidates.length) return;
  if (candidates.length === 1) return chooseCandidate(candidates[0].key);
  boardChooser = candidates; boardChooserOrigin = viewportRouter.active.host;
  positionDecisionPanel(hit.cell); clearAffectedPreview(); renderChoicePanel();
  ui.choices.querySelectorAll("button")[0]?.focus();
}
if (cameraViewport) viewportRouter = createViewportRouter({ dom: domViewport, overlay: ui["board-overlays"],
  loadThree: async () => {
    const startedAt = performance.now();
    const { createThreeViewport } = await import("./three-viewport.js");
    const visualCatalog = await loadUnitVisualCatalog();
    let fixtureModels;
    const qa = await fetch('./api/presentation/miniatures-qa').then(r=>r.ok?r.json():null).catch(()=>null);
    if(qa?.enabled===true)({fixtureModels}=await import('./__qa/miniatures/registration.js'));
    const modelDescriptors = {...fixtureModels};
    const host = document.createElement("div"); host.className = "board-stage three-viewport"; host.id = "three-viewport";
    host.hidden = true; host.tabIndex = 0; host.setAttribute("aria-label", "Three.js board. Arrow keys select Cells; Enter chooses; Shift plus arrows pans; plus and minus zoom.");
    cameraViewport.after(host);
    const root = domCameraControls.cloneNode(true); root.classList.add("three-controls"); root.hidden = true;
    const controls = { root };
    for (const node of root.querySelectorAll("[id]")) { const key = node.id.replace(/^camera-/, ""); node.id = `three-camera-${key}`; controls[key] = node; }
    const projectionLabel=document.createElement("label"),projection=document.createElement("select");projection.id="three-camera-projection";
    for(const [value,text]of [["orthographic","Orthographic"],["perspective","Perspective"]]){const option=document.createElement("option");option.value=value;option.textContent=text;projection.append(option);}
    projectionLabel.append(document.createTextNode("Projection "),projection);root.append(projectionLabel);controls.projection=projection;
    const lensLabel=document.createElement("label"),lens=document.createElement("select");
    for(let fov=20;fov<=80;fov+=5){const option=document.createElement("option");option.value=String(fov);option.textContent=`${fov}°`;lens.append(option);}
    lens.value="45";lens.id="three-camera-fov";lens.setAttribute("aria-label","Perspective vertical field of view");
    lensLabel.append(document.createTextNode("Lens "),lens);root.append(lensLabel);controls.fov=lens;
    for (const [key, label, max] of [["tilt", "Tilt", 55], ["yaw", "Rotate", 360]]) {
      const wrapper = document.createElement("label"); wrapper.className = "three-orientation";
      wrapper.append(document.createTextNode(`${label} `));
      const input = document.createElement("input"); input.type = "range"; input.min = "0"; input.max = String(max); input.step = "1"; input.value = "0";
      input.id = `three-camera-${key}`; input.setAttribute("aria-label", `${label} board camera`); wrapper.append(input); root.append(wrapper); controls[key] = input;
    }
    const cutawayLabel=document.createElement("label"),cutaway=document.createElement("input");cutaway.type="checkbox";cutaway.id="three-camera-cutaway";
    cutawayLabel.append(cutaway,document.createTextNode(" Cutaway obstacles"));root.append(cutawayLabel);controls.cutaway=cutaway;
    for(const [key,label]of [["turn-left","↶"],["turn-right","↷"],["overhead","Overhead"],["angle","Angled"]]){
      const button=document.createElement("button");button.type="button";button.id=`three-camera-${key}`;button.textContent=label;
      button.setAttribute("aria-label",key==='turn-left'?"Rotate board left":key==='turn-right'?"Rotate board right":`${label} camera`);root.append(button);controls[key]=button;
    }
    domCameraControls.after(root);
    // Both adapters share the disclosure; only their own controls switch visibility.
    const objectives = document.createElement("div"); objectives.className = "three-objectives"; host.append(objectives); controls.objectives = objectives;
    try {
      threeViewport = createThreeViewport({ host, controls, startedAt, facing:unitFacing, visualCatalog, modelDescriptors, animate: () => ui.animate.checked && !skipEffects && !reducedMotion(),
        onPick: selectThreeHit, onHover: (id, cell) => { if (cell) inspectCell(cellKey(cell)); else { hoveredUnitId = id; renderUnitCard(); } },
        onPreview: candidate => { if (threePreview === candidate) return; threePreview = candidate; syncThreeInteraction(); },
        onProject: () => positionDecisionPanel(),
        onFailure: error => { ui.error.textContent = error.message; domViewport.mode = "enhanced"; boardView.value = "enhanced"; ui.board.dataset.view = "enhanced"; void viewportRouter.request("enhanced"); } });
      if (displayedFrame) threeViewport.renderFrame(displayedFrame);
      return threeViewport;
    } catch (error) { threeViewport?.dispose(); threeViewport = null; root.remove(); host.remove(); throw error; }
  },
  onChange: (selection, active) => {
    boardView.value = selection;
    if (active === threeViewport) {
      if (displayedFrame) threeViewport.renderFrame(displayedFrame);
      syncThreeInteraction(); if (!busy) threeViewport.camera.stable(snapshot?.presentation.decision);
    } else {
      if (displayedFrame) boardCamera.setFrame(displayedFrame.state, displayedFrame.presentation, displayedFrame.metadata);
      boardCamera.measure(); if (!busy) boardCamera.stable(snapshot?.presentation.decision);
    }
    if (snapshot) { renderChoicePanel(); updateControls(); }
    positionDecisionPanel();
  },
  onError: (error, previous) => { ui.error.textContent = `Three.js could not start: ${error.message}`; boardView.value = previous; }
});

function positionDecisionPanel(location) {
  location ??= displayedState?.physical.figures.find(figure => figure.id === (attackContext?.targets?.[0]?.targetId ?? focusedUnitId))?.position;
  if (!location) return;
  const screen = playCamera()?.screen({ x: location.x + .5, y: location.y + .5 });
  const stage = threeActive() ? threeViewport.host : cameraViewport;
  const lower = screen && stage ? screen.y >= stage.getBoundingClientRect().height / 2 : location.y >= displayedState.physical.board.height / 2;
  ui["board-overlays"].classList.toggle("at-top", lower);
}

// These helpers describe geometry and presentation only. Legal choices arrive from the engine.
const cellKey = cell => `${cell.x},${cell.y}`;
const edgeKey = edge => [cellKey(edge.a), cellKey(edge.b)].sort().join("|");
const text = (tag, value) => { const node = document.createElement(tag); node.textContent = value; return node; };
const abbreviatedName = name => {
  const words = name.split(/\s+/);
  return words.length === 1 ? name.slice(0, 2) : words.map(word => word[0]).join("").slice(0, 3);
};

function placeFigure(node, cell, board) {
  // Visual placement only; gameplay geometry always comes from occupied Cell centers.
  const span = Number(node.dataset.cellSpan ?? 1);
  node.style.left = `${(cell.x + span / 2) / board.width * 100}%`;
  node.style.top = `${(cell.y + span / 2) / board.height * 100}%`;
}

// Compatibility for older 1x1 projections; current responses always supply geometry.
const figureCells = figure => displayedFigures?.[figure.id]?.occupiedCells ?? [figure.position];
function inspectCell(key) {
  hoveredUnitId = displayedState?.physical.figures.find(figure =>
    figureCells(figure).some(cell => cellKey(cell) === key))?.id ?? null;
  renderUnitCard();
  const pile = displayedState && groundLoot(displayedState, displayedFrame?.presentation.groundObjects).find(p => cellKey(p.position) === key);
  if (pile) {
    const panel = text('section', ''); panel.className = 'card-ability';
    panel.append(text('h3', `Ground at ${key}`));
    if (pile.unopened) panel.append(text('p', `${pile.unopened} unopened Loot`));
    for (const object of pile.objects) {
      panel.append(text('strong', object.content?.name ?? object.kind), text('p', object.content?.text ?? ''));
      const candidate = snapshot?.presentation.decision?.candidates.find(c => c.key === `pickup-object:${object.id}`);
      if (candidate && displayedState === snapshot?.result.state) panel.append(choiceButton(candidate));
    }
    ui['unit-card'].append(panel);
  }
}

function renderBoard(state, preserveNodes = false, machines = snapshot.presentation.machines, objectives = snapshot.presentation.objectives, groundObjects = snapshot.presentation.groundObjects) {
  const board = state.physical.board;
  for (const node of choiceFeedback.values()) node.remove();
  choiceFeedback.clear();
  for (const node of ui.board.querySelectorAll(".waypoint-area")) node.remove();
  for (const node of ui.board.querySelectorAll(".mission-area")) node.remove();
  const nextObjects = new Map(), objectCounts = new Map();
  // Retain decoded artwork through both playback steps and snapshot redraws.
  // Prune absent objects every render and isolate artwork by game identity.
  const objectNode = (category, id, identity, className) => {
    const base = JSON.stringify([snapshot.sessionId, snapshot.scenarioId, snapshot.visualSeed, category, identity, id]);
    const occurrence = objectCounts.get(base) ?? 0;
    objectCounts.set(base, occurrence + 1);
    const key = JSON.stringify([base, occurrence]);
    const node = boardObjects.get(key) ?? text("span", "");
    node.className = className;
    nextObjects.set(key, node);
    return node;
  };
  updateCoordinates();
  if (!preserveNodes) {
    ui.board.replaceChildren(); figures.clear(); edges.clear(); cells.clear();
  }
  // Keep mounted figures across resolution steps so CSS transitions have a painted start.
  const retainedCells = new Set(), retainedEdges = new Set(), retainedFigures = new Set();
  ui.board.style.gridTemplateColumns = `repeat(${board.width}, minmax(0, 1fr))`;
  ui.board.style.gridTemplateRows = `repeat(${board.height}, minmax(0, 1fr))`;
  ui.board.style.setProperty("--columns", board.width);
  ui.board.style.setProperty("--board-ratio", board.width / board.height);
  ui.board.style.aspectRatio = `${board.width} / ${board.height}`;
  for (const objective of objectives ?? []) {
    if (!objective.area) continue;
    const area = objective.area, node = text("span", "Reach Area");
    node.className = "mission-area"; node.setAttribute("aria-label", `${objective.sideId}: ${objective.meaning}`);
    styleSide(node, objective.sideId);
    Object.assign(node.style, { left: `${area.x / board.width * 100}%`, top: `${area.y / board.height * 100}%`,
      width: `${area.w / board.width * 100}%`, height: `${area.h / board.height * 100}%` });
    ui.board.append(node);
  }
  for (const waypoint of state.waypoints ?? []) {
    const area = waypoint.area, node = text("span", waypoint.id);
    node.className = "waypoint-area"; node.setAttribute("aria-hidden", "true");
    Object.assign(node.style, { left: `${area.x / board.width * 100}%`, top: `${area.y / board.height * 100}%`,
      width: `${area.w / board.width * 100}%`, height: `${area.h / board.height * 100}%` });
    ui.board.append(node);
  }
  const terrain = new Map((board.terrain ?? []).map(tile => [cellKey(tile.position), tile]));
  const viewingSide = snapshot.presentation.viewingSideId;
  const explored = viewingSide ? new Set((state.exploredCells?.[viewingSide] ?? []).map(cellKey)) : null;
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const tile = terrain.get(cellKey({ x, y }));
    const display = explored && !explored.has(cellKey({ x, y })) ? { kind: 'Unknown', name: 'Unknown' } : terrainDisplay(board, tile);
    const kind = display.kind;
    const key = cellKey({ x, y });
    const node = cells.get(key) ?? document.createElement("div");
    node.replaceChildren();
    node.removeAttribute("aria-label");
    node.className = `cell ${kind}${display.fallback ? ' custom-terrain' : ''}`; node.dataset.cell = key; node.dataset.x = x; node.dataset.y = y;
    node.dataset.x = String(x); node.dataset.y = String(y);
    applyBoardVisual(node, "terrain", kind, { seed: snapshot.visualSeed ?? `scenario:${snapshot.scenarioId ?? "legacy"}`,
      x, y, direction: tile?.direction, terrain, assembly: display.assembly });
    node.dataset.railDirection = tile?.direction ?? "";
    retainedCells.add(key);
    const coordinates = text("span", node.dataset.cell);
    coordinates.className = "cell-coordinates";
    node.append(coordinates);
    node.title = kind === "Rail" ? `${display.name}: ${tile.direction}` : display.name;
    if (kind === "Unknown") node.setAttribute("aria-label", `Unknown Cell (${x},${y})`);
    const symbol = display.custom ? display.name : { Unknown: "?", SolidRock: "Rock", Asphalt: "Asphalt", Gravel: "Gravel", Grass: "Grass", Tree: "Tree", FruitTree: "Fruit Tree", Water: "Water", Lava: "Lava", StoneFloorWithTable: "Table" }[kind];
    if (symbol) { const label = text("span", symbol); label.className = "terrain-label"; node.append(label); }
    if (!cells.has(key)) {
      node.addEventListener("mouseenter", () => inspectCell(key));
      node.addEventListener("mouseleave", () => { hoveredUnitId = null; renderUnitCard(); });
      ui.board.append(node);
    }
    cells.set(key, node);
  }
  for (const machine of machines ?? []) {
    const node = cells.get(cellKey(machine.position));
    if (!node) continue;
    node.classList.add("machine-cell");
    const art = objectNode("machine", machine.typeId, machine.id, "machine-art");
    const visual = applyBoardVisual(art, "machine", machine.typeId);
    if (visual.baseTerrain) applyBoardVisual(node, "terrain", visual.baseTerrain, {
      seed: snapshot.visualSeed ?? `scenario:${snapshot.scenarioId ?? "legacy"}`,
      x: machine.position.x, y: machine.position.y });
    art.setAttribute("aria-hidden", "true"); node.append(art);
    const labels = text("span", ""); labels.className = "machine-labels";
    labels.append(text("span", machine.name));
    labels.append(text("small", machine.ownerSide == null ? "Public" : `Owner: ${machine.ownerSide}`));
    for (const stored of machine.resourceStorage)
      labels.append(text("small", `${stored.name}: ${stored.quantity}/${stored.capacity}`));
    node.append(labels);
    node.title = `${machine.name} (${machine.id}) - ${machine.ownerSide == null ? "Public" : `Owner: ${machine.ownerSide}`}: ${machine.operation}`;
    node.setAttribute("aria-label", `${node.title}. ${machine.resourceStorage.map(r => `${r.name}: ${r.quantity}/${r.capacity}`).join(", ")}`);
  }
  for (const pile of groundLoot(state, groundObjects)) {
    const node = cells.get(cellKey(pile.position)); if (!node) continue;
    for (const [count, known] of [[pile.unopened, false], [pile.objects.length, true]]) if (count) {
      const marker = text("span", `${known ? '◆' : '▣'} ${count}`); marker.className = `loot-ground${known ? ' known' : ''}`;
      marker.title = pile.label; marker.setAttribute('aria-label', pile.label); node.append(marker);
    }
    node.title += ` · ${pile.label}`;
  }
  for (const cellMarker of state.cellMarkers ?? []) {
    const node = cells.get(cellKey(cellMarker.position));
    if (!node) continue;
    const marker = objectNode("cellMarker", cellMarker.type.id, cellKey(cellMarker.position), "cell-marker");
    applyBoardVisual(marker, "cellMarker", cellMarker.type.id);
    marker.title = cellMarker.type.name; marker.setAttribute("aria-label", cellMarker.type.name);
    node.append(marker); node.title += ` · ${cellMarker.type.name}`;
    node.setAttribute("aria-label", node.title);
  }
  for (const resource of state.resources ?? []) {
    const node = cells.get(cellKey(resource.position));
    if (!node) continue;
    const marker = objectNode("resource", resource.type.id, cellKey(resource.position), "resource-marker");
    applyBoardVisual(marker, "resource", resource.type.id);
    marker.title = resource.type.name;
    marker.setAttribute("aria-label", resource.type.name);
    node.append(marker);
    node.title += ` ? ${resource.type.name}`;
  }
  boardObjects = nextObjects;
  for (const edge of board.edges) {
    if (edge.kind === "None") continue;
    const key = edgeKey(edge);
    const node = edges.get(key) ?? document.createElement("div");
    retainedEdges.add(key);
    node.className = `edge ${edge.kind}`;
    const vertical = edge.a.y === edge.b.y;
    node.dataset.orientation = vertical ? "Vertical" : "Horizontal";
    applyBoardVisual(node, "edge", edge.kind, { direction: node.dataset.orientation });
    node.style.left = `${((edge.a.x + edge.b.x) / 2 + .5) / board.width * 100}%`;
    node.style.top = `${((edge.a.y + edge.b.y) / 2 + .5) / board.height * 100}%`;
    node.style.width = vertical ? "var(--edge-thickness)" : `${100 / board.width}%`;
    node.style.height = vertical ? `${100 / board.height}%` : "var(--edge-thickness)";
    node.title = edge.kind;
    if (!edges.has(key)) ui.board.append(node);
    edges.set(key, node);
  }
  for (const figure of state.physical.figures) {
    const unit = state.units.find(unit => unit.id === figure.id);
    const type = state.types.find(type => type.id === unit?.typeId);
    const node = figures.get(figure.id) ?? document.createElement("div");
    retainedFigures.add(figure.id);
    const peers = state.units.filter(peer => peer.typeId === unit?.typeId);
    const name = displayedCards[figure.id]?.displayName ?? figure.id;
    const label = text("span", abbreviatedName(name) + (peers.length > 1 ? peers.findIndex(peer => peer.id === figure.id) + 1 : ""));
    label.className = "figure-name";
    node.replaceChildren(label);
    const tokenName = displayedCards[figure.id]?.individualName?.trim()
      || displayedCards[figure.id]?.displayName?.trim() || type?.displayName?.trim() || unit?.typeId || "Unit";
    const nameplate = text("span", tokenName + (unit?.inventory?.length ? ` · ◆${unit.inventory.length}` : '')); nameplate.className = "token-nameplate";
    nameplate.title = tokenName; node.append(nameplate);
    // Snapshot positions settle any skipped movement without animating a state correction.
    node.style.transition = "none";
    delete node.dataset.hpLabel;
    node.removeAttribute("aria-label");
    const span = displayedFigures?.[figure.id]?.cellSpan ?? 1;
    node.dataset.cellSpan = span;
    node.className = `figure${span > 1 ? " large" : ""}${figure.posture === "Lying" ? " lying" : ""}${state.currentUnitId === figure.id ? " selected" : ""}`;
    node.classList.toggle("active", state.currentUnitId === figure.id);
    node.dataset.posture = figure.posture;
    node.style.setProperty("--token-span", span);
    const unitVisual = applyBoardVisual(node, "unit", unit?.typeId);
    const portrait = text("span", ""); portrait.className = "token-portrait";
    portrait.setAttribute("aria-hidden", "true");
    const fallback = text("span", unitVisual.glyph ?? abbreviatedName(tokenName)); portrait.append(fallback);
    if (unitVisual.image) {
      const image = document.createElement("img"); image.alt = ""; image.draggable = false;
      image.addEventListener("load", () => { image.hidden = false; fallback.hidden = true; });
      image.addEventListener("error", () => { image.hidden = true; fallback.hidden = false; });
      image.src = unitVisual.image; portrait.append(image);
    }
    node.append(portrait);
    const active = text("span", "\u25b6"); active.className = "token-active";
    active.setAttribute("aria-hidden", "true"); active.title = "Active Unit"; node.append(active);
    const side = text("span", unit?.sideId ?? ""); side.className = "token-side";
    side.setAttribute("aria-hidden", "true"); side.title = `Side ${unit?.sideId ?? ""}`; node.append(side);
    const posture = text("span", figure.posture); posture.className = "token-posture";
    posture.setAttribute("aria-hidden", "true"); node.append(posture);
    styleSide(node, unit?.sideId);
    node.style.width = `${(span > 1 ? span * 95 : 70) / board.width}%`;
    node.style.pointerEvents = "";
    node.dataset.unitId = figure.id;
    node.title = `${displayedCards[figure.id]?.playerName ?? tokenName} · ${figure.posture} · Side ${unit?.sideId ?? ""}`;
    node.dataset.postureLabel = node.title;
    if (type?.hp > 1) {
      const hp = text("span", `${unit.currentHp}/${type.hp}`);
      hp.className = "figure-hp";
      hp.style.setProperty("--hp-fill", `${Math.max(0, Math.min(1, unit.currentHp / type.hp)) * 100}%`);
      node.dataset.hpLabel = `HP ${unit.currentHp}/${type.hp}`;
      node.title += ` · ${node.dataset.hpLabel}`;
      hp.setAttribute("aria-hidden", "true");
      node.append(hp);
    }
    node.setAttribute("aria-label", node.title);
    placeFigure(node, figure.position, board);
    if (!figures.has(figure.id)) {
      node.addEventListener("mouseenter", () => { hoveredUnitId = figure.id; renderUnitCard(); });
      node.addEventListener("mouseleave", () => { hoveredUnitId = null; renderUnitCard(); });
      ui.board.append(node);
    }
    figures.set(figure.id, node);
  }
  for (const [nodes, retained] of [[cells, retainedCells], [edges, retainedEdges], [figures, retainedFigures]]) {
    for (const [key, node] of nodes) {
      if (retained.has(key)) continue;
      node.remove(); nodes.delete(key);
    }
  }
}

function renderState(state, preserveNodes = true, cards = snapshot.presentation.cards, geometry = snapshot.presentation.figures, world = snapshot.presentation.worldEffects, orders = snapshot.presentation.orders, machines = snapshot.presentation.machines, objectives = snapshot.presentation.objectives, groundObjects = snapshot.presentation.groundObjects) {
  applySideHues(snapshot?.scenarios?.find(scenario => scenario.id === snapshot.scenarioId)?.sideHues);
  displayedCards = cards;
  displayedFigures = geometry;
  displayedOrders = orders;
  const isSetup = state.round === 0;
  if (setupPhase === undefined || setupPhase !== isSetup) ui["game-setup"].open = isSetup;
  setupPhase = isSetup;
  renderBoard(state, preserveNodes, machines, objectives, groundObjects);
  displayedState = state;
  displayedFrame = { state, presentation: { cards, figures: geometry, worldEffects: world, orders, machines, objectives, groundObjects, viewingSideId: snapshot.presentation.viewingSideId },
    metadata: { sessionId: snapshot.sessionId, scenarioId: snapshot.scenarioId, assignments: snapshot.controllerAssignments, controllers: snapshot.controllers, round: state.round }, visualSeed: snapshot.visualSeed ?? `scenario:${snapshot.scenarioId ?? "legacy"}` };
  unitFacing.setFrame(displayedFrame);
  if (threeActive()) threeViewport.renderFrame(displayedFrame);
  playCamera()?.setFrame(state, { figures: geometry, objectives, viewingSideId: snapshot.presentation.viewingSideId },
    { sessionId: snapshot.sessionId, scenarioId: snapshot.scenarioId, assignments: snapshot.controllerAssignments,
      controllers: snapshot.controllers, round: state.round });
  syncCombatDice(state);
  if (state.currentUnitId) latestUnitId = state.currentUnitId;
  renderTurn(state);
  renderWorldEffects(world);
  renderOrders(orders, state);
  highlightContext();
  renderUnitCard();
}

const visibleChoice = candidate => !(ui.filter.checked && candidate.relevant === false);

// Content wording and current counters come from the domain/API, not rule components.
function renderUnitCard() {
  clearAffectedPreview();
  cardChoiceKeys.clear();
  const state = displayedState;
  const unit = state?.units.find(unit => unit.id === (hoveredUnitId ?? latestUnitId));
  const card = ui["unit-card"];
  // Keep the portrait's ancestors mounted; Cell hover and playback also render this card.
  let header = card.querySelectorAll(".card-identity")[0];
  for (const child of Array.from(card.children)) if (child !== header) child.remove();
  // Reset affiliation on the empty card and update it on every hover/active switch.
  card.style.setProperty("--unit-side-border", "var(--ui-border)");
  const debug = document.getElementById("unit-debug"); debug.replaceChildren();
  if (!unit) {
    const empty = text("div", ""); empty.className = "unit-empty";
    const mark = text("span", "◇"); mark.className = "unit-empty-mark"; mark.setAttribute("aria-hidden", "true");
    empty.append(mark, text("h3", state?.completion ? "A tale concluded" : state?.round ? "Between activations" : "The table is ready"),
      text("p", "Inspect a Unit on the board to discover its abilities and current condition."));
    const mission = text("button", "Read the mission"); mission.className = "unit-mission-link";
    mission.addEventListener("click", () => document.querySelector?.('[data-inspector-tab="mission"]')?.click());
    empty.append(mission); card.replaceChildren(empty); return;
  }
  const type = state.types.find(type => type.id === unit.typeId);
  styleSide(card, unit.sideId);
  card.style.setProperty("--unit-side-border", "hsl(var(--side-hue) 60% 70%)");
  const content = displayedCards[unit.id];
  const name = unitName(unit.id);
  debug.append(text("p", `${name} · ${unit.id} · ${unit.typeId}`));
  if (!header) {
    header = text("div", ""); header.className = "card-identity"; card.append(header);
  }
  const previousPortrait = header.children[0];
  const portrait = unitPortrait(unit.typeId, name, previousPortrait);
  for (const child of Array.from(header.children)) if (child !== portrait) child.remove();
  if (portrait !== previousPortrait) header.append(portrait);
  const identity = text("div", ""); identity.className = "card-name";
  if (!hoveredUnitId && state.currentUnitId !== unit.id)
    identity.append(text("small", "Most recently active"));
  identity.append(text("h3", name));
  identity.append(text("small", `Side ${unit.sideId ?? "—"}`));
  const hp = text("p", `HP ${unit.currentHp} / ${type.hp}`); hp.className = "card-health"; identity.append(hp);
  const posture = state.physical.figures.find(figure => figure.id === unit.id)?.posture;
  if (posture && posture !== "Upright") identity.append(text("small", posture));
  if (content.footprintLabel) identity.append(text("small", content.footprintLabel));
  header.append(identity);
  const stats = text("div", ""); stats.className = "card-stats";
  for (const stat of ["Mov", "Rng", "Atk", "Def"]) {
    const base = type[stat.toLowerCase()], effective = state[`effective${stat}`]?.[unit.id] ?? base;
    const value = text("p", `${stat.toUpperCase()} ${base === effective ? base : `${base} → ${effective}`}`);
    if (base !== effective) {
      value.className = "modified-stat"; value.dataset.change = effective > base ? "positive" : "negative";
      value.title = `${stat.toUpperCase()}: base ${base}, current ${effective}`;
    }
    stats.append(value);
  }
  card.append(stats);
  if (content.inventory?.length) {
    const summary = text("p", `${content.inventory.length} carried objects · listed below`);
    summary.className = "inventory-summary"; card.append(summary);
  }
  for (const stored of content.resourceStorage ?? [])
    card.append(text("p", `${stored.name}: ${stored.quantity} / ${stored.capacity} (shared by ${unit.sideId} / ${content.displayName})`));
  const order = displayedOrders?.find(order => order.token.typeId === unit.typeId && order.token.sideId === unit.sideId);
  if (order) {
    const row = text("div", ""); row.className = "card-order"; styleSide(row, unit.sideId);
    row.append(text("small", "Standing Order"));
    row.append(text("p", orderLabel(order, state)));
    card.append(row);
  }
  for (const entry of content.entries) {
    const row = text("div", ""); row.className = "card-ability";
    row.dataset.source = entry.source ?? "Native";
    const source = text("small", entry.source === "Item" ? "Single Use Item" : entry.source ?? "Native");
    source.className = "ability-source";
    if (entry.sourceName && ["Equipment", "Item"].includes(entry.source)) source.textContent += ` · ${entry.sourceName}`;
    row.append(source);
    const ability = entry.content;
    const heading = text("div", ""); heading.className = "ability-heading";
    const name = text("strong", ability.name + (entry.copyCount > 1 ? ` ×${entry.copyCount}` : ""));
    bindTooltip(name, () => ability.description || ability.name);
    if (ability.useLimitText != null) {
      const limit = text("span", ` [${ability.useLimitText}]`); limit.className = "sr-only";
      name.append(limit);
    }
    heading.append(name); heading.append(text("small", ability.category));
    if (entry.uses) heading.append(useMarkers(entry.uses));
    row.append(heading);
    row.append(abilityDescription(ability));
    const decision = snapshot?.presentation.decision;
    const candidates = displayedState === snapshot?.result.state && decision?.unitId === unit.id
      ? decision.candidates.filter(candidate => (candidate.accountingId ?? candidate.entryId) === (entry.accountingId ?? ability.id) && candidate.interaction.kind === "Direct") : [];
    for (const candidate of (entry.source === "Item" ? candidates.slice(0, 1) : candidates)) {
      const button = choiceButton(candidate);
      button.dataset.legal = "true";
      button.disabled = busy;
      button.hidden = !visibleChoice(candidate);
      row.append(button);
      if (!button.hidden && !document.getElementById("inspector-unit")?.hidden) {
        cardChoiceKeys.add(candidate.key);
        if (entry.source === "Item") for (const copy of candidates) cardChoiceKeys.add(copy.key);
      }
      if (!candidate.relevant && !button.hidden) row.append(text("small", "Currently irrelevant"));
    }
    card.append(row);
  }
  // Hover inspection must not strand a Direct choice whose card is no longer shown.
  if (!busy) renderChoicePanel();
}

function abilityDescription(ability) {
  const description = text("div", ""); description.className = "ability-description";
  const short = text("p", ability.shortDescription ?? ability.description ?? ability.name);
  short.hidden = short.textContent === "";
  bindTooltip(short, () => ability.description || ability.name);
  description.append(short);
  const full = text("details", "");
  const summary = text("summary", "");
  bindTooltip(summary, () => ability.description || ability.name);
  summary.append(text("span", `Full description: ${ability.name}`));
  summary.children[0].className = "sr-only";
  full.append(summary);
  full.append(text("p", ability.description || ability.name));
  description.append(full);
  return description;
}

function unitPortrait(typeId, name, previous) {
  const frame = previous ?? text("div", ""); frame.className = "card-portrait";
  const visual = applyBoardVisual(frame, "unit", typeId);
  frame.dataset.portraitName = name;
  const source = visual.image ?? "";
  if (previous && frame.dataset.portraitSource === source) {
    frame.children[0].textContent = visual.glyph ?? abbreviatedName(name);
    const button = frame.querySelectorAll(".portrait-button")[0];
    if (button) button.setAttribute("aria-label", `Enlarge portrait of ${name}`);
    if (!button || button.disabled) frame.setAttribute("aria-label", `${name}: portrait unavailable`);
    return frame;
  }
  frame.replaceChildren();
  frame.dataset.portraitSource = source;
  const fallback = text("span", visual.glyph ?? abbreviatedName(name));
  fallback.setAttribute("aria-hidden", "true"); frame.append(fallback);
  frame.setAttribute("role", "img"); frame.setAttribute("aria-label", `${name}: portrait unavailable`);
  if (!visual.image) return frame;
  const button = text("button", ""); button.type = "button"; button.id = "unit-portrait";
  button.className = "portrait-button"; button.setAttribute("aria-label", `Enlarge portrait of ${name}`);
  button.setAttribute("aria-haspopup", "dialog");
  button.disabled = true;
  const image = document.createElement("img"); image.alt = ""; image.hidden = true;
  const showImage = () => {
    if (frame.children[1] !== button) return;
    image.hidden = false; fallback.hidden = true; button.disabled = false;
    frame.removeAttribute("role"); frame.removeAttribute("aria-label");
  };
  image.addEventListener("load", showImage);
  image.addEventListener("error", () => {
    if (frame.children[1] !== button) return;
    image.hidden = true; fallback.hidden = false; button.disabled = true;
    frame.setAttribute("role", "img"); frame.setAttribute("aria-label", `${frame.dataset.portraitName}: portrait unavailable`);
  });
  button.addEventListener("click", () => { if (!button.disabled) openPortrait(source, frame.dataset.portraitName); });
  image.src = visual.image; button.append(image); frame.append(button);
  // Cached images can already be ready before their queued load event is delivered.
  if (image.complete && image.naturalWidth > 0) showImage();
  return frame;
}

function useMarkers(uses) {
  const label = `${uses.remainingUses} / ${uses.maxUses} uses`;
  const markers = text("span", ""); markers.className = "use-markers"; bindTooltip(markers, () => label);
  const accessible = text("span", label); accessible.className = "sr-only"; markers.append(accessible);
  for (let index = 0; index < uses.maxUses; index++) {
    const circle = text("span", "");
    circle.className = `use-circle${index < uses.remainingUses ? " filled" : ""}`;
    circle.setAttribute("aria-hidden", "true"); markers.append(circle);
  }
  return markers;
}

function clearAffectedPreview() {
  threePreview = null; syncThreeInteraction();
  for (const node of choiceFeedback.values()) node.classList.remove("feedback-hover");
  for (const node of figures.values()) node.classList.remove("affected-preview");
  for (const node of cells.values()) node.classList.remove("placement-preview");
}

function previewChoice(candidate) {
  clearAffectedPreview();
  if (busy || !visibleChoice(candidate)) return;
  threePreview = candidate; syncThreeInteraction();
  for (const id of candidate.affectedUnitIds) figures.get(id)?.classList.add("affected-preview");
  for (const cell of candidate.placementCells ?? []) cells.get(cellKey(cell))?.classList.add("placement-preview");
}

function choiceButton(candidate) {
  const button = text("button", candidate.displayLabel ?? candidate.label);
  bindTooltip(button, () => candidate.boardVisual?.preview ?? candidate.displayLabel ?? candidate.label);
  button.addEventListener("click", () => chooseCandidate(candidate.key));
  const preview = () => {
    previewChoice(candidate);
  };
  button.addEventListener("mouseenter", preview);
  button.addEventListener("focus", preview);
  button.addEventListener("mouseleave", clearAffectedPreview);
  button.addEventListener("blur", clearAffectedPreview);
  return button;
}

function renderChoicePanel() {
  const alternativeHost = ui["board-alternatives"] ?? ui.choices;
  const previousAlternatives = alternativeHost.querySelectorAll(".three-board-choices")[0];
  if (previousAlternatives) threeChoicesOpen = previousAlternatives.open;
  ui.choices.replaceChildren();
  if (alternativeHost !== ui.choices) alternativeHost.replaceChildren();
  const decision = snapshot?.presentation.decision;
  ui["decision-panel"].hidden = false;
  ui["decision-panel"].classList.toggle("roll-decision", Boolean(decision?.roll));
  if (decision?.roll) {
    // Mandatory continuation: only the server-supplied opaque candidate is submitted.
    for (const candidate of decision.candidates) {
      const button = choiceButton(candidate);
      button.textContent = `Roll ${decision.roll.count} ${decision.roll.family} dice`;
      button.setAttribute("aria-label", rollLabel(decision.roll));
      button.className = "primary-roll"; ui.choices.append(button);
    }
    return;
  }
  if (boardChooser) {
    const chooser = text("div", ""); chooser.className = "board-chooser";
    chooser.append(text("small", "Choose an option"));
    for (const candidate of boardChooser) chooser.append(choiceButton(candidate));
    const cancel = text("button", "Cancel");
    cancel.addEventListener("click", closeBoardChooser);
    chooser.append(cancel);
    ui.choices.append(chooser);
  }
  if (decision?.noneChoice && !boardChoiceKeys.has(null)) ui.choices.append(choiceButton(decision.noneChoice));
  for (const candidate of decision?.candidates ?? []) {
    if (candidate.isEndTurn || !visibleChoice(candidate) || boardChoiceKeys.has(candidate.key) || cardChoiceKeys.has(candidate.key)) continue;
    ui.choices.append(choiceButton(candidate));
  }
  if (threeActive() && boardChoiceKeys.size && !boardChooser) {
    // A canvas has no native Cell buttons. Keep the same authoritative choices
    // available to keyboard, assistive technology and touch without inventing
    // another command path. Collapsed by default to preserve board-first play.
    const alternatives = text("details", ""); alternatives.className = "three-board-choices";
    alternatives.open = threeChoicesOpen;
    alternatives.addEventListener("toggle", () => { if (alternatives.isConnected) threeChoicesOpen = alternatives.open; });
    alternatives.append(text("summary", "Choose from list"));
    const list = text("div", "");
    for (const candidate of [...(decision?.candidates ?? []), ...(decision?.noneChoice ? [decision.noneChoice] : [])]) {
      if (!visibleChoice(candidate) || !boardChoiceKeys.has(candidate.key)) continue;
      const button = choiceButton(candidate); button.dataset.boardChoiceKind = candidate.boardVisual?.kind ?? "special";
      button.disabled = busy;
      button.addEventListener("click", () => { alternatives.open = false; threeChoicesOpen = false; });
      button.dataset.boardChoiceKey = candidate.key ?? "none"; list.append(button);
      if (candidate.interaction.kind === "Position") button.dataset.boardChoiceCell = `${candidate.interaction.position.x}-${candidate.interaction.position.y}`;
    }
    alternatives.append(list); alternativeHost.append(alternatives);
  }
  ui["decision-panel"].hidden = !ui.choices.children.length;
}

function renderSnapshot() {
  renderSessionHeading();
  const state = snapshot.result.state;
  globalThis.dispatchEvent?.(new CustomEvent("delve-generation-state", { detail: { code: snapshot.generationCode ?? null } }));
  if (snapshot.scenarios) {
    if (!ui.scenario.children.length) {
      for (const scenario of snapshot.scenarios) {
        const option = text("option", scenario.name); option.value = scenario.id;
        ui.scenario.append(option);
      }
    }
    if (renderedScenarioId !== snapshot.scenarioId) {
      ui.scenario.value = snapshot.scenarios.some(s => s.id === snapshot.scenarioId) ? snapshot.scenarioId : "";
      latestUnitId = null;
      renderedScenarioId = snapshot.scenarioId;
    }
    describeScenario();
    renderControllerSetup();
  }
  if (state.round === 0 || diceSession !== snapshot.sessionId) clearDice();
  diceSession = snapshot.sessionId;
  hoveredUnitId = null;
  boardChooser = null;
  boardChoiceKeys.clear();
  attackContext = state.attackInProgress ?? null;
  focusedUnitId = state.currentUnitId;
  focusedDoor = snapshot.presentation.decision?.roll?.door ?? state.doorInProgress?.door;
  renderState(state, false);
  ui.auto.checked = snapshot.autoChooseSingleRelevantChoice;
  const decision = snapshot.presentation.decision;
  // Bind supplied selection references. Affected Units are preview data, never selections.
  const boardChoices = new Map();
  const offer = (node, candidate) => {
    if (!node) return;
    if (!boardChoices.has(node)) boardChoices.set(node, new Map());
    boardChoices.get(node).set(candidate.key, candidate);
  };
  ui.prompt.textContent = state.completion ? "Game over. Restart or choose a scenario to play again." : decision ? decision.roll ? rollLabel(decision.roll) : decision.displayPrompt ?? decision.prompt : state.roundComplete ? "Round complete. Start the next round when ready." : "Start the first round.";
  ui["board-hint"].textContent = ui.prompt.textContent;
  for (const candidate of [...(decision?.candidates ?? []), ...(decision?.noneChoice ? [decision.noneChoice] : [])]) {
    if (!visibleChoice(candidate)) continue;
    const selection = candidate.interaction;
    if (selection.kind === "Unit") {
      offer(figures.get(selection.unitId), candidate);
      const figure = state.physical.figures.find(figure => figure.id === selection.unitId);
      if (figure) for (const cell of figureCells(figure)) offer(cells.get(cellKey(cell)), candidate);
    } else if (selection.kind === "Position") {
      offer(cells.get(cellKey(selection.position)), candidate);
      for (const figure of state.physical.figures) {
        if (figureCells(figure).some(cell => cellKey(cell) === cellKey(selection.position))) {
          if (figureCells(figure).length === 1) offer(figures.get(figure.id), candidate);
          // Preserve exact Cell targeting underneath a large Figure.
          else figures.get(figure.id).style.pointerEvents = "none";
        }
      }
    } else if (selection.kind === "Door") {
      offer(edges.get(edgeKey(selection.door)), candidate);
    }
  }
  for (const [node, choices] of boardChoices) {
    const candidates = [...choices.values()]; // Preserve authoritative candidate order.
    bindBoardChoice(node, candidates);
    for (const candidate of candidates) boardChoiceKeys.add(candidate.key);
  }
  renderChoicePanel();
  playCamera()?.stable(decision);
  positionDecisionPanel();
  updateControls();
  void viewportRouter?.checkpoint();
}

function chooseCandidate(key) {
  if (busy) return;
  const commentary = (ui["decision-commentary"].value ?? "").trim().slice(0, 280);
  return mutate("decision", { candidateKey: key, ...(commentary ? { commentary } : {}) })
    .then(succeeded => { if (succeeded) ui["decision-commentary"].value = ""; return succeeded; });
}

function bindBoardChoice(node, candidates) {
  const descriptions = candidates.map(candidate => candidate.boardVisual?.preview ?? candidate.displayLabel ?? candidate.label);
  const label = (candidates.length > 1 ? "Choose an option: " : "") + descriptions.join(" / ");
  const kinds = new Set(candidates.map(candidate => candidate.boardVisual?.kind).filter(Boolean));
  for (const kind of kinds) node.classList.add(`choice-${kind}`);
  if (kinds.size && (node.dataset.cell || node.dataset.unitId)) node.classList.add("cell-choice-feedback");
  // Mount once per Cell as a board sibling: Terrain Cells are isolated stacking
  // contexts, and tokens must remain input surfaces without their own markers.
  if (node.dataset.cell && kinds.size) {
    const [x, y] = node.dataset.cell.split(",").map(Number);
    const board = displayedState.physical.board;
    const feedback = text("span", ""); feedback.className = "choice-feedback";
    feedback.dataset.cell = node.dataset.cell;
    feedback.setAttribute("aria-hidden", "true");
    Object.assign(feedback.style, { left: `${x / board.width * 100}%`, top: `${y / board.height * 100}%`,
      width: `${100 / board.width}%`, height: `${100 / board.height}%` });
    const icons = text("span", ""); icons.className = "choice-icons";
    for (const kind of kinds) {
      if (!["activate", "move", "attack"].includes(kind)) continue;
      const marker = text("span", ""); marker.className = `choice-marker marker-${kind}`;
      feedback.append(marker);
    }
    for (const candidate of candidates) {
      const visual = candidate.boardVisual;
      if (!visual?.icon) continue;
      const icon = text("span", visual.icon); icon.className = `choice-icon icon-${visual.kind}`;
      icons.append(icon);
    }
    feedback.append(icons);
    choiceFeedback.set(node.dataset.cell, feedback);
    ui.board.append(feedback);
  }
  const select = () => {
    if (busy) return;
    if (candidates.length === 1) return chooseCandidate(candidates[0].key);
    boardChooser = candidates;
    boardChooserOrigin = node;
    const reference = candidates[0].interaction;
    const location = reference.position ?? reference.door?.a ?? displayedState?.physical.figures.find(figure => figure.id === reference.unitId)?.position;
    if (location) positionDecisionPanel(location);
    clearAffectedPreview();
    renderChoicePanel();
    ui.choices.querySelectorAll("button")[0]?.focus();
  };
  node.classList.add("board-choice");
  const preview = () => {
    if (node.dataset.cell) inspectCell(node.dataset.cell);
    if (candidates.length === 1) previewChoice(candidates[0]);
    else clearAffectedPreview();
    if (busy) return;
    const figure = displayedState.physical.figures.find(figure => figure.id === node.dataset.unitId);
    const keys = node.dataset.cell ? [node.dataset.cell] : figure ? figureCells(figure).map(cellKey) : [];
    for (const key of keys) choiceFeedback.get(key)?.classList.add("feedback-hover");
  };
  node.addEventListener("mouseenter", preview);
  node.addEventListener("focus", preview);
  node.addEventListener("mouseleave", clearAffectedPreview);
  node.addEventListener("blur", clearAffectedPreview);
  if (node.classList.contains("cell") && !kinds.size) node.classList.add("legal");
  const accessibleLabel = [label, node.classList.contains("machine-cell") ? node.title : null, node.dataset.postureLabel, node.dataset.hpLabel].filter(Boolean).join(" · ");
  const tooltipLabel = [label, node.classList.contains("machine-cell") ? node.title : null].filter(Boolean).join(" · ");
  bindTooltip(node, () => tooltipLabel);
  node.title = "";
  node.setAttribute("role", "button");
  node.setAttribute("aria-label", accessibleLabel);
  node.addEventListener("click", event => {
    event.stopPropagation();
    return select();
  });
  node.addEventListener("keydown", event => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    return select();
  });
}

function updateControls() {
  if (streaming) busy = commandPending || postInFlight || playbackRunning || playbackQueue.length > 0 ||
    !streamConnected || !latestUpdate || !["waiting-human", "idle", "completed"].includes(latestUpdate.status) || displayedSequence !== receivedSequence;
  if (streaming && latestUpdate) {
    if (latestUpdate.status === "waiting-external") ui.prompt.textContent = latestUpdate.snapshot?.controllerStatus ? "Waiting for Gambit. Board updates and playback remain available." : "Waiting for an external controller.";
    if (latestUpdate.status === "controller-error") ui.prompt.textContent = "Gambit is paused. Retry or cancel its pending decision.";
    if (latestUpdate.status === "resolving") ui.prompt.textContent = "Game resolving.";
    if (latestUpdate.status === "faulted") ui.prompt.textContent = "Game progression stopped after an error.";
  }
  if (busy) clearAffectedPreview();
  // Keep the prominent instruction in sync during controller waits and playback.
  ui["board-hint"].textContent = busy && playbackRunning ? "Playing out the action…" : ui.prompt.textContent;
  ui["board-hint"].dataset.busy = String(busy);
  const phase = document.getElementById("decision-state");
  if (phase) phase.textContent = snapshot?.result.state.completion ? "ADVENTURE COMPLETE"
    : busy ? "AT THE TABLE" : snapshot?.presentation.decision?.roll ? "YOUR ROLL"
    : snapshot?.presentation.decision ? "YOUR DECISION" : "READY WHEN YOU ARE";
  ui.scenario.disabled = !snapshot;
  ui["start-scenario"].disabled = !snapshot;
  ui["restart-scenario"].disabled = !snapshot;
  ui["import-scenario"].disabled = !snapshot;
  ui.refresh.disabled = busy;
  ui.filter.disabled = busy || !snapshot;
  ui.auto.disabled = busy || !snapshot;
  ui["decision-commentary"].disabled = busy || !snapshot?.presentation.decision;
  ui.round.disabled = busy || !snapshot || !!snapshot.result.state.completion || !(snapshot.result.state.round === 0 || snapshot.result.state.roundComplete);
  ui.round.textContent = snapshot?.result.state.round ? "Start next round" : "Start round";
  ui["start-round"].disabled = ui.round.disabled;
  ui["start-round"].textContent = snapshot?.result.state.round ? "Next round" : "Start round";
  ui["start-round"].hidden = !!snapshot && !(snapshot.result.state.round === 0 || snapshot.result.state.roundComplete) || !!snapshot?.result.state.completion;
  const endTurn = snapshot?.presentation.decision?.candidates.find(candidate => candidate.isEndTurn);
  ui["end-turn"].disabled = busy || !endTurn;
  ui["end-turn"].textContent = endTurn?.label ?? "End Turn";
  ui["end-turn"].hidden = !endTurn;
  for (const button of ui.choices.querySelectorAll("button")) button.disabled = busy;
  for (const button of ui["board-alternatives"]?.querySelectorAll("button") ?? []) button.disabled = busy;
  for (const button of ui["unit-card"].querySelectorAll("button"))
    if (button.dataset.legal != null) button.disabled = busy || !snapshot || button.dataset.legal !== "true";
  for (const node of ui.board.querySelectorAll(".board-choice")) {
    const disabled = busy || !snapshot;
    node.setAttribute("aria-disabled", String(disabled));
    node.tabIndex = disabled ? -1 : 0;
  }
  ui.skip.disabled = !(streaming ? playbackRunning : busy) || skipEffects;
  renderControllerStatus();
}

function connectUpdates() {
  updateSource?.close();
  streamConnected = false;
  updateControls();
  const source = new EventSource("/api/game/updates");
  updateSource = source;
  source.addEventListener("open", () => {
    if (source !== updateSource) return;
    streamConnected = true;
    if (ui.error.textContent === "Connection interrupted. Reconnecting to the game.") ui.error.textContent = "";
    updateControls();
  });
  source.addEventListener("update", message => {
    if (source !== updateSource) return;
    try { receiveUpdate(JSON.parse(message.data)); }
    catch (error) { ui.error.textContent = error.message; connectUpdates(); }
  });
  source.onerror = () => {
    if (source !== updateSource) return;
    streamConnected = false; updateControls();
    ui.error.textContent = "Connection interrupted. Reconnecting to the game.";
    // The same EventSource retains Last-Event-ID during automatic reconnect.
  };
}

function cancelPlayback() {
  threeViewport?.cancel();
  void viewportRouter?.checkpoint();
  playCamera()?.cancel();
  clearDice();
  playbackGeneration++;
  cancelPause?.();
  playbackQueue.length = 0;
  playbackRunning = false;
  playbackOperationActive = false;
  clearCommentary();
}

function receiveUpdate(update) {
  if (!Number.isSafeInteger(update.sequence) || !update.sessionId || !update.snapshot) throw new Error("Invalid game update.");
  const sameSession = latestUpdate?.sessionId === update.sessionId;
  if (sameSession && (update.sequence < receivedSequence || (update.sequence === receivedSequence && update.kind !== "baseline"))) return;
  streamConnected = true;
  if (update.kind === "baseline" || (update.commandId === pendingCommandId && ["boundary", "fault"].includes(update.kind))) {
    commandPending = false; pendingCommandId = null;
  }
  if (update.kind === "baseline" || (update.kind === "fault" && sameSession && update.sequence === receivedSequence + 1)) {
    cancelPlayback();
    latestUpdate = update;
    receivedSequence = displayedSequence = update.sequence;
    snapshot = update.snapshot;
    latestUnitId = null;
    attackContext = null;
    focusedDoor = null;
    ui.dice.classList.remove("rolling");
    ui.dice.replaceChildren(); if (!sameSession) ui.events.replaceChildren(); ui.effect.textContent = "";
    ui.error.textContent = update.error ?? "";
    renderSnapshot(); updateControls();
    if (pendingReplacement && (update.commandId === pendingReplacement.commandId || update.sessionId !== pendingReplacement.sessionId)) {
      const replacement = pendingReplacement; pendingReplacement = null;
      replacement.resolve(update.kind === "baseline" && update.status !== "faulted");
    }
    flushQueuedScenario();
    return;
  }
  if (!sameSession || update.sequence !== receivedSequence + 1 || playbackQueue.length >= maxPlaybackQueue) {
    cancelPlayback();
    connectUpdates(); // A new connection requests an authoritative baseline.
    return;
  }
  latestUpdate = update;
  receivedSequence = update.sequence;
  playbackQueue.push(update);
  updateControls();
  void drainUpdates();
}

async function drainUpdates() {
  if (playbackRunning) return;
  playbackRunning = true;
  const generation = playbackGeneration;
  updateControls();
  try {
    while (playbackQueue.length && generation === playbackGeneration) {
      const update = playbackQueue.shift();
      snapshot = update.snapshot;
      if (update.kind === "commentary") showCommentary(update.commentary);
      if (update.kind === "event") {
        if (!playbackOperationActive) { skipLootCards = false; skipEffects = !ui.animate.checked || reducedMotion(); playbackOperationActive = true; }
        await playEvents(snapshot, generation, false);
      }
      if (generation !== playbackGeneration) return;
      displayedSequence = update.sequence;
      if (update.kind !== "event" && update.kind !== "commentary") {
        playbackOperationActive = false;
        ui.error.textContent = update.error ?? "";
        renderSnapshot();
      }
    }
    if (generation === playbackGeneration && latestUpdate && displayedSequence === receivedSequence) {
      snapshot = latestUpdate.snapshot;
      renderSnapshot();
    }
  } catch (error) {
    if (generation !== playbackGeneration) return;
    ui.error.textContent = `${error.message} Reconnecting to the game.`;
    cancelPlayback();
    connectUpdates();
  } finally {
    if (generation === playbackGeneration) { playbackRunning = false; updateControls(); }
  }
}

function flushQueuedScenario() {
  if (!streaming || postInFlight || pendingReplacement || !streamConnected || !queuedScenario) return;
  const next = queuedScenario; queuedScenario = null;
  void replaceScenario(next.operation, next.body, true).then(next.resolve);
}

async function sendCommand(operation, body) {
  const replacement = ["scenario", "scenario/import", "restart"].includes(operation);
  if (!latestUpdate || postInFlight || (commandPending && !replacement) || !streamConnected) return false;
  if (!replacement && busy && !(["controller/retry", "controller/cancel"].includes(operation) && controllerOperationReady())) return false;
  const checkpoint = latestUpdate;
  const commandId = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}-${++commandNumber}`;
  let completion;
  if (replacement) completion = new Promise(resolve => { pendingReplacement = { commandId, sessionId: checkpoint.sessionId, resolve }; });
  pendingCommandId = commandId;
  postInFlight = true; postingCommandId = commandId;
  commandPending = true; updateControls(); ui.error.textContent = "";
  try {
    await request("/commands", { commandId, sessionId: checkpoint.sessionId,
      expectedRevision: checkpoint.revision, decisionId: checkpoint.decisionId, operation, ...body });
    // Acknowledgements carry no authoritative view. Updates own rendering.
    if (postingCommandId === commandId) { postInFlight = false; postingCommandId = null; }
    flushQueuedScenario();
    return completion ? await completion : true;
  } catch (error) {
    if (pendingReplacement?.commandId === commandId) {
      pendingReplacement.resolve(false); pendingReplacement = null;
    }
    if (latestUpdate?.sessionId === checkpoint.sessionId && pendingCommandId === commandId) {
      ui.error.textContent = `${error.message} Reconnecting to synchronize the game.`;
      connectUpdates(); // Never retry a decision with an uncertain outcome.
    }
    return false;
  } finally {
    if (postingCommandId === commandId) { postInFlight = false; postingCommandId = null; }
    updateControls(); flushQueuedScenario();
  }
}

async function request(path, body) {
  const response = await fetch(`/api/game${path}`, {
    method: body ? "POST" : "GET",
    headers: body ? { "Content-Type": "application/json" } : {},
    body: body ? JSON.stringify(body) : undefined,
    cache: "no-store"
  });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.detail ?? `Request failed (${response.status}).`);
  }
  return response.json();
}

async function refresh() {
  if (streaming) { connectUpdates(); return; }
  if (busy) return;
  busy = true; updateControls(); ui.error.textContent = "";
  try {
    const previousSession = snapshot?.sessionId;
    snapshot = await request(""); clearCommentary();
    if (previousSession !== snapshot.sessionId) ui.events.replaceChildren();
    ui.effect.textContent = ""; clearDice(); renderSnapshot();
  } catch (error) { ui.error.textContent = error.message; }
  finally {
    busy = false; updateControls();
    if (queuedScenario) {
      const next = queuedScenario; queuedScenario = null;
      const replaced = await replaceScenario(next.operation, next.body);
      next.resolve?.(replaced);
    }
  }
}

async function mutate(operation, body = {}) {
  if (streaming) return sendCommand(operation, body);
  if (busy || !snapshot) return false;
  let succeeded = false;
  busy = true; skipLootCards = false; skipEffects = !ui.animate.checked || reducedMotion(); updateControls(); ui.error.textContent = "";
  try {
    // Present from the previous visible state, advancing only with engine snapshots.
    snapshot = await request(`/${operation}`, { expectedRevision: snapshot.revision, ...body });
    succeeded = true;
    if (["scenario", "scenario/import", "restart"].includes(operation)) { latestUnitId = null; clearCommentary(); clearDice(); }
    if (["scenario", "scenario/import", "restart"].includes(operation)) ui.events.replaceChildren();
    await playEvents(snapshot);
  } catch (error) {
    clearCommentary();
    ui.error.textContent = `${error.message} Synchronized to the server; choose again.`;
    if (operation !== "scenario/import") clearDice();
    try { snapshot = await request(""); }
    catch { ui.error.textContent = `${error.message} Refresh to reconnect before choosing again.`; snapshot = null; }
  } finally {
    // Events never reconstruct gameplay state. Always finish on the returned authoritative snapshot.
    if (snapshot) renderSnapshot();
    else { ui.choices.replaceChildren(); ui.prompt.textContent = "Refresh to reconnect."; }
    busy = false; updateControls();
    if (queuedScenario) {
      const next = queuedScenario; queuedScenario = null;
      const replaced = await replaceScenario(next.operation, next.body);
      next.resolve?.(replaced);
    }
  }
  return succeeded;
}

async function playEvents(view, generation = playbackGeneration, includeCommentaries = true) {
    const steps = new Map(view.result.resolutionSteps.map(step => [step.eventIndex, step.stateAfter]));
    const comments = includeCommentaries ? view.commentaries ?? [] : [];
    const commentsAt = index => {
      for (const comment of comments) if (comment.eventIndex === index) showCommentary(comment);
    };
    for (const [index, event] of view.presentation.events.entries()) {
      if (generation !== playbackGeneration) return;
      viewportRouter?.beginEvent();
      commentsAt(index);
      ui.events.append(journalEntry(event));
      const occurrence = view.result.events[index];
      const projected = view.presentation.resolutionSteps.find(step => step.eventIndex === index);
      // Facing observes the displayed timeline even when effects/Three are disabled.
      if(event.role==='Movement')unitFacing.observe(event,occurrence,displayedFrame);
      prepareOccurrence(occurrence, event);
      if (!["UnitCreated", "AttackStarted"].includes(occurrence?.kind)) playCamera()?.occurrence(occurrence, event, skipEffects);
      if (occurrence?.kind === "WorldCardDrawn" && projected) renderWorldEffects(projected.worldEffects);
      if (["TokenDrawn", "RoundStarted", "ActivationBagPopulated", "RoundCompleted", "ActivationCompleted"].includes(occurrence?.kind) && steps.has(index)) {
        renderTurn(steps.get(index));
      }
      if (!skipLootCards && event.lootCard) await revealLoot(event);
      if (!skipEffects) await present(event, occurrence);
      if (generation !== playbackGeneration) return;
      if (skipEffects && event.role === "Movement") playCamera()?.finishMovement();
      if (steps.has(index)) {
        const before = displayedState;
        const after = steps.get(index);
        renderState(after, true, projected.cards, projected.figures, projected.worldEffects, projected.orders, projected.machines, projected.objectives, projected.groundObjects);
        if (["UnitCreated", "AttackStarted"].includes(occurrence?.kind)) playCamera()?.occurrence(occurrence, event, skipEffects);
        if (["AttackTarget", "Damage", "Healing"].includes(event.role) && event.targetId) {
          const hpBefore = before?.units.find(unit => unit.id === event.targetId)?.currentHp;
          const hpAfter = after.units.find(unit => unit.id === event.targetId)?.currentHp;
          if (hpBefore != null && hpAfter != null) ui.effect.textContent += ` \u00b7 HP ${hpBefore} \u2192 ${hpAfter}`;
        }
        if (!skipEffects && occurrence?.kind === "UnitCreated") figures.get(occurrence.unitId)?.classList.add("created");
        if (!skipEffects && occurrence?.kind === "PostureChanged") figures.get(occurrence.unitId)?.classList.add("posture-changed");
        if (!skipEffects && threeActive()) threeViewport.afterOccurrence(occurrence);
        if (["AttackTargetResolved", "PostureChanged", "UnitCreated", "PlacesSwapped", "UnitRepositioned"].includes(occurrence?.kind)) await guardedPause(380);
      }
      if(event.role!=='Movement')unitFacing.observe(event,occurrence,displayedFrame);
      if(threeActive())threeViewport.syncFacing?.(skipEffects||!ui.animate.checked||reducedMotion());
      await viewportRouter?.checkpoint();
    }
    if (generation === playbackGeneration) commentsAt(view.presentation.events.length);
}

function describeScenario() {
  const current = snapshot?.scenarios?.find(s => s.id === snapshot.scenarioId);
  const selected = snapshot?.scenarios?.find(s => s.id === ui.scenario.value);
  ui["scenario-description"].textContent = snapshot ? `Current: ${current?.name ?? "Imported scenario"}. ${selected?.description ?? ""}` : "";
}

async function replaceScenario(operation, body = {}, waitForReplacement = false) {
  if (streaming) {
    if (!latestUpdate) return false;
    if (postInFlight || pendingReplacement) {
      queuedScenario?.resolve?.(false);
      const completion = new Promise(resolve => { queuedScenario = { operation, body, resolve }; });
      return waitForReplacement ? completion : undefined;
    }
    return sendCommand(operation, body);
  }
  if (!snapshot) return false;
  if (busy) {
    queuedScenario?.resolve?.(false);
    skipEffects = true; cancelPause?.();
    const completion = new Promise(resolve => { queuedScenario = { operation, body, resolve }; });
    if (waitForReplacement) return completion;
    return;
  }
  return mutate(operation, body);
}

function describe(event) { return event.text; }
function journalEntry(event) {
  // Both labels are supplied by the filtered, event-time observation.
  const entry = text("li", event.displayText ?? event.text);
  if (event.displayText && event.text && event.displayText !== event.text) {
    const detail = text("details", ""); detail.className = "journal-technical";
    detail.append(text("summary", "Event details"), text("p", event.text)); entry.append(detail);
  }
  return entry;
}

function revealLoot(event) {
  return new Promise(resolve => {
    let timer, cleanup;
    const done = () => { clearTimeout(timer); cleanup?.(); if (cancelPause === done) cancelPause = null; resolve(); };
    cleanup = lootCardDialog(event.lootCard, event.humanCollector, done, () => { skipLootCards = true; skipEffects = true; done(); });
    cancelPause = done;
    if (!event.humanCollector) timer = setTimeout(done, 1400);
  });
}

function pause(ms) {
  if (skipEffects) return Promise.resolve();
  const generation = playbackGeneration;
  return new Promise((resolve, reject) => {
    const finish = () => {
      clearTimeout(timer); cancelPause = null;
      if (generation !== playbackGeneration) reject(new Error("Playback replaced."));
      else resolve();
    };
    const timer = setTimeout(finish, ms); cancelPause = finish;
  });
}

async function guardedPause(ms) {
  const generation = playbackGeneration;
  await pause(ms);
  if (generation !== playbackGeneration) throw new Error("Playback replaced.");
}

async function present(event, occurrence) {
  if (!occurrence) ui.effect.textContent = event.displayText ?? describe(event);
  if (occurrence?.kind === "DiceRolled") {
    const groups = ui.dice.querySelectorAll(".dice-group");
    const rolling = groups[groups.length - 1] ?? ui.dice;
    rolling.classList.add("rolling");
    await guardedPause(260);
    rolling.classList.remove("rolling");
    await guardedPause(420);
    return;
  }
  const delay = { TokenDrawn: 520, ActivationStarted: 300, ActivationCompleted: 280,
    AttackStarted: 420, ActionUsed: 300, RoundStarted: 400, RoundCompleted: 350,
    WorldCardDrawn: 1000, WorldContinuousChanged: 700, WorldCardDiscarded: 450, WorldDeckReshuffled: 600,
    PostureChanged: 200, UnitCreated: 200 }[occurrence?.kind];
  if (delay != null) { await guardedPause(delay); return; }
  if (threeActive()) {
    await threeViewport.present(event, occurrence, { pause: guardedPause, skipping: () => skipEffects || !ui.animate.checked || reducedMotion() });
    return;
  }
  if (event.movementSegments?.length && event.movingFigure) {
    await presentObservedMovement(event);
    return;
  }
  switch (event.role) {
    case "Movement": {
      const node = figures.get(event.unitId);
      if (!node) break;
      // Commit the starting layout, including an immediately preceding snapshot render.
      node.getBoundingClientRect();
      for (const cell of event.path.slice(1)) {
        if (skipEffects) break;
        node.style.transition = "left .22s linear, top .22s linear";
        placeFigure(node, cell, snapshot.result.state.physical.board);
        playCamera()?.movementStep(cell);
        await guardedPause(250);
      }
      break;
    }
    case "AttackSummary": await guardedPause(450); break;
    case "AttackTarget": {
      const attacker = figures.get(event.unitId), target = figures.get(event.targetId);
      attacker?.classList.add("attacking"); target?.classList.add("target");
      await guardedPause(180);
      ui.effect.textContent = `${event.hits} Hits · ${event.blocks} Blocks`;
      await guardedPause(180);
      ui.effect.textContent = `${event.damage} Damage`;
      await guardedPause(180);
      attacker?.classList.remove("attacking"); target?.classList.remove("target");
      break;
    }
    case "Defeat": {
      const node = figures.get(event.unitId);
      node?.getBoundingClientRect();
      if (node) node.style.transition = "";
      node?.classList.add("defeated");
      await guardedPause(320); node?.remove(); figures.delete(event.unitId); break;
    }
    case "DoorAttempt": {
      const node = edges.get(edgeKey(event.door));
      node?.classList.add("target");
      await guardedPause(400);
      node?.classList.remove("target");
      break;
    }
    case "DoorClosed":
    case "DoorOpened": {
      const node = edges.get(edgeKey(event.door));
      if (node) {
        node.className = `edge ${event.door.kind}`; node.title = event.door.kind;
        applyBoardVisual(node, "edge", event.door.kind, { direction: node.dataset.orientation });
      }
      await guardedPause(300); break;
    }
    default: await guardedPause(250);
  }
}

async function presentObservedMovement(event) {
  const board = snapshot.result.state.physical.board, appearance = event.movingFigure;
  const stable = figures.get(event.unitId);
  if (stable) stable.style.visibility = "hidden";
  const masks = [];
  const known = new Set((displayedState.exploredCells?.[snapshot.presentation.viewingSideId] ?? []).map(cellKey));
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    if (known.has(`${x},${y}`)) continue;
    const mask = text("div", ""); mask.className = "movement-fog"; mask.setAttribute("aria-hidden", "true");
    Object.assign(mask.style, { left: `${x / board.width * 100}%`, top: `${y / board.height * 100}%`, width: `${100 / board.width}%`, height: `${100 / board.height}%` });
    ui.board.append(mask); masks.push(mask);
  }
  const node = text("div", ""); node.className = `figure movement-figure${appearance.cellSpan > 1 ? " large" : ""}${appearance.posture === "Lying" ? " lying" : ""}`;
  node.setAttribute("aria-hidden", "true");
  node.dataset.cellSpan = appearance.cellSpan; node.style.setProperty("--token-span", appearance.cellSpan);
  node.style.width = `${(appearance.cellSpan > 1 ? appearance.cellSpan * 95 : 70) / board.width}%`;
  styleSide(node, appearance.sideId); const visual = applyBoardVisual(node, "unit", appearance.typeId);
  const label = text("span", abbreviatedName(appearance.name)); label.className = "figure-name"; node.append(label);
  const nameplate = text("span", appearance.name); nameplate.className = "token-nameplate"; node.append(nameplate);
  const portrait = text("span", ""); portrait.className = "token-portrait";
  const fallback = text("span", visual.glyph ?? abbreviatedName(appearance.name)); portrait.append(fallback);
  if (visual.image) { const img = document.createElement("img"); img.alt = ""; img.src = visual.image; img.addEventListener("load", () => { fallback.hidden = true; }); img.addEventListener("error", () => { img.hidden = true; }); portrait.append(img); }
  node.append(portrait);
  if (appearance.maxHp > 1) { const hp = text("span", `${appearance.hp}/${appearance.maxHp}`); hp.className = "figure-hp"; hp.style.setProperty("--hp-fill", `${Math.max(0, Math.min(1, appearance.hp / appearance.maxHp)) * 100}%`); node.append(hp); }
  ui.board.append(node);
  try {
    for (const segment of event.movementSegments) {
      if (skipEffects) break;
      node.style.transition = "none"; placeFigure(node, segment[0], board); node.getBoundingClientRect();
      playCamera()?.movementStep(segment[0]);
      for (const cell of segment.slice(1)) {
        if (skipEffects) break;
        node.style.transition = "left .22s linear, top .22s linear";
        placeFigure(node, cell, board); playCamera()?.movementStep(cell); await guardedPause(250);
      }
    }
  } finally {
    node.remove(); for (const mask of masks) mask.remove();
    if (stable) stable.style.visibility = "";
  }
}

// Generic visual vocabulary only. No content identities, legality or RNG live here.
function tokenNode(token, state) {
  const name = state.types.find(type => type.id === token.typeId)?.displayName ?? token.typeId;
  const node = text("span", `${name} \u00b7 ${token.sideId}`);
  node.className = "activation-token"; styleSide(node, token.sideId);
  return node;
}

function renderOrders(orders, state) {
  const panel = ui.orders;
  panel.replaceChildren();
  panel.hidden = !orders?.length;
  if (!orders?.length) return;
  panel.append(text("h2", "Standing Orders"));
  for (const order of orders) {
    const name = state.types.find(type => type.id === order.token.typeId)?.displayName ?? order.token.typeId;
    const row = text("p", `${name} · ${order.token.sideId}: ${orderLabel(order, state)}`);
    styleSide(row, order.token.sideId); panel.append(row);
  }
}

function orderLabel(order, state) {
  const route = state.routes?.find(r => r.id === order.route?.routeId);
  const target = order.target;
  const targetLabel = target?.kind === "Unit" ? unitName(target.unitId)
    : target?.kind === "Group" ? `${state.types.find(type => type.id === target.group.typeId)?.displayName ?? target.group.typeId} · ${target.group.sideId}`
    : target?.kind === "Side" ? `Side ${target.sideId}`
    : target?.kind === "Door" ? `Door (${target.door.a.x},${target.door.a.y})–(${target.door.b.x},${target.door.b.y})`
    : null;
  return order.kind === "DoNothing" ? "Do Nothing"
    : order.kind === "FollowRoute" ? `Follow Route · ${order.route.routeId} · ${route?.mode === "PingPong" ? "Ping-pong" : route?.mode ?? ""} · waypoint ${order.route.currentIndex + 1}/${route?.waypointIds.length ?? "?"}${route?.mode === "PingPong" ? (order.route.direction === 1 ? " →" : " ←") : ""}`
    : order.kind === "HoldPosition" ? "Hold Position"
    : order.kind === "MoveTo" ? `Move To (${order.destination.x},${order.destination.y})`
    : order.kind === "InteractWith" ? `Interact With · ${targetLabel}`
    : order.kind === "Attack" || order.kind === "Protect" ? `${order.kind} · ${targetLabel}`
    : order.kind;
}

function renderWorldEffects(world) {
  const panel = ui["world-effects"];
  panel.hidden = !world;
  panel.replaceChildren();
  if (!world) return;
  panel.append(text("h2", "World Effects"));
  panel.append(text("small", `${world.cardsPerRound} cards per Round · Cycling ${world.cycling} · Draw ${world.drawPileCount} · Discard ${world.discardPileCount}`));
  if (world.resolvingCard) {
    const drawn = text("div", `${world.resolvingCard.name} · ${world.resolvingCard.continuous ? "Continuous" : "Immediate"}`);
    drawn.className = "world-card world-draw";
    drawn.append(text("p", world.resolvingCard.text)); panel.append(drawn);
  }
  panel.append(text("small", "Active Continuous cards · oldest first"));
  if (!world.activeContinuous.length) panel.append(text("p", "None active"));
  for (const card of world.activeContinuous) {
    const row = text("div", card.name); row.className = "world-card";
    row.dataset.cardId = card.id; row.append(text("p", card.text)); panel.append(row);
  }
}

function renderTurn(state) {
  const completion = state.completion;
  const result = ui["game-result"];
  result.hidden = !completion || (dismissedResultSession != null && dismissedResultSession === snapshot?.sessionId);
  const showResult = document.getElementById("show-result");
  if (showResult) showResult.hidden = !completion;
  result.replaceChildren();
  if (completion) {
    const sides = completion.achievedSideIds;
    result.append(text("span", "THE ADVENTURE CONCLUDES"));
    result.append(text("h2", sides.length === 1 ? `Victory: ${sides[0]}` : "Simultaneous victory"));
    result.append(text("p", `Game over. ${sides.join(", ")} ${sides.length === 1 ? "achieved its Objective." : "each achieved their Objective."}`));
    const actions = text("div", ""); actions.className = "result-actions toolbar";
    const replay = text("button", "Play again"); replay.id = "result-replay";
    replay.addEventListener("click", async () => {
      replay.disabled = true;
      try { await startFromTools("restart", {}); }
      finally { replay.disabled = false; }
    });
    const back = text("a", playContext?.returnLabel ?? "Choose another scenario");
    back.className = "library-button"; back.href = playContext?.returnHash ?? "#scenarios";
    const inspect = text("button", "Inspect final board"); inspect.id = "result-inspect";
    inspect.addEventListener("click", () => { dismissedResultSession = snapshot?.sessionId; result.hidden = true; showResult?.focus(); });
    actions.append(replay, back, inspect); result.append(actions);
  }
  ui.objectives.replaceChildren(text("h2", "Side objectives"));
  for (const objective of snapshot?.presentation.objectives ?? []) {
    const row = text("p", `${objective.sideId}: ${objective.name}`);
    styleSide(row, objective.sideId);
    if (objective.meaning) row.append(text("small", objective.meaning));
    if (completion) row.append(text("strong", completion.achievedSideIds.includes(objective.sideId) ? " - Achieved" : " - Not achieved"));
    ui.objectives.append(row);
  }
  ui.status.textContent = `Round ${state.round} \u00b7 ${state.completion ? "Game over" : state.round === 0 ? "Ready" : state.roundComplete ? "Complete" : "In play"}`;
  ui["bag-count"].textContent = `Activation Bag · ${(state.bag ?? []).length} remaining`;
  ui.bag.replaceChildren(text("small", `Activation Bag \u00b7 ${(state.bag ?? []).length} remaining. Membership only; future draw order is unknown.`));
  if (snapshot.presentation.viewingSideId) {
    ui["bag-count"].textContent = "Activation Bag";
    ui.bag.replaceChildren(text("small", "Activation Bag · hidden composition. All Units continue to activate normally."));
  }
  for (const token of state.bag ?? []) ui.bag.append(tokenNode(token, state));
  ui["active-token"].replaceChildren(text("small", "Drawn token"));
  ui["active-token"].className = ui.effect.dataset.kind === "TokenDrawn" ? "token-draw" : "";
  if (state.activeToken) ui["active-token"].append(tokenNode(state.activeToken, state));
  else ui["active-token"].append(text("span", "Awaiting draw"));
  ui["active-unit"].textContent = state.currentUnitId
    ? `Activating \u00b7 ${unitName(state.currentUnitId)}` +
      (Number.isInteger(state.effectiveMovesPerActivation) ? ` · Moves ${state.movesCompletedThisActivation}/${state.effectiveMovesPerActivation}` : "")
    : state.completion ? "Game complete" : state.round === 0 ? "Ready to begin" : state.roundComplete ? "Round complete" : "Awaiting activation";
}

function actionLabel(pool) {
  const context = combatDice?.context ?? displayedState?.attackInProgress ?? snapshot?.result.state.attackInProgress;
  if (context && context.attackerId === pool.sourceUnitId && context.actionId === pool.sourceActionId && context.abilityName)
    return context.abilityName;
  const card = displayedCards?.[pool.sourceUnitId];
  return card?.entries.find(entry => entry.content.id === pool.sourceActionId)?.content.name ?? pool.sourceActionId;
}
// Player labels are separate from IDs used by choices, events and debug information.
function unitName(id) {
  if (!id && snapshot?.presentation.viewingSideId) return "Unseen source";
  if (combatDice?.names.has(id)) return combatDice.names.get(id);
  const cards = displayedCards ?? snapshot?.presentation.cards ?? {};
  if (cards[id]?.playerName) return cards[id].playerName;
  const state = displayedState ?? snapshot?.result.state;
  const nameOf = unitId => cards[unitId]?.individualName?.trim() || cards[unitId]?.displayName?.trim()
    || state?.types.find(type => type.id === state.units.find(unit => unit.id === unitId)?.typeId)?.displayName || "Unit";
  const name = nameOf(id), peers = (state?.units ?? []).filter(unit => nameOf(unit.id) === name);
  if (peers.length < 2) return name;
  const position = state.physical.figures.find(figure => figure.id === id)?.position;
  return position ? `${name} (${position.x},${position.y})` : `${name} #${peers.findIndex(unit => unit.id === id) + 1}`;
}
function rollLabel(pool) {
  const owner = unitName(pool.ownerUnitId);
  const dice = pool.family === "D6" ? `${pool.count}d6` : `${pool.count} ${pool.family} dice`;
  const role = pool.family === "Defence" ? `${owner} defends against ${unitName(pool.sourceUnitId)}` : owner;
  return `Roll ${dice} \u00b7 ${role} \u00b7 ${actionLabel(pool)}`;
}

function clearDice() {
  clearTimeout(diceDismissal); diceGeneration++;
  combatDice = null;
  renderCombatRelationship();
  for (const node of figures.values()) { node.classList.remove("attack-source"); node.classList.remove("attack-target"); }
  ui.dice.replaceChildren(); ui.dice.hidden = true; ui.dice.classList.remove("rolling");
}
function dismissDiceAfter(ms) {
  clearTimeout(diceDismissal);
  const generation = ++diceGeneration;
  diceDismissal = setTimeout(() => { if (generation === diceGeneration) clearDice(); }, ms);
  diceDismissal?.unref?.();
}
const combatSignature = context => JSON.stringify([context.attackerId, context.actionId, context.selectedCell,
  context.targets.map(target => target.targetId)]);
function beginCombatDice(context) {
  clearDice();
  const names = new Map((displayedState?.units ?? []).map(unit => [unit.id, unitName(unit.id)]));
  combatDice = { signature: combatSignature(context), context, names, positions: new Map((displayedState?.physical.figures ?? []).map(f => [f.id, { ...f.position, cellSpan: displayedFigures?.[f.id]?.cellSpan ?? 1 }])), attack: null, defences: new Map(), outcomes: new Map(), finished: false };
}
function finishCombatDice() {
  if (!combatDice || combatDice.finished) return;
  combatDice.finished = true;
  renderCombatDice();
  // This timer never advances gameplay or delays the next legal decision.
  dismissDiceAfter(8000);
}
function syncCombatDice(state) {
  // Older projections need not carry the optional continuation field.
  if (!("attackInProgress" in state)) return;
  const context = state.attackInProgress;
  if (!context) {
    if (combatDice && !combatDice.finished) {
      if (combatDice.outcomes.size) finishCombatDice(); else clearDice();
    }
    return;
  }
  if (!combatDice || combatDice.signature !== combatSignature(context)
      || (combatDice.finished && context.stage === "AttackRoll")) beginCombatDice(context);
  combatDice.context = context;
  if (context.attackRoll) combatDice.attack = context.attackRoll;
  for (const result of context.results ?? []) {
    if (!combatDice.outcomes.has(result.targetId)) combatDice.outcomes.set(result.targetId, result);
  }
  renderCombatDice();
  if (context.targetIndex >= context.targets.length) finishCombatDice();
}
function diceFaces(result) {
  const faces = text("div", ""); faces.className = "dice-faces";
  for (const face of result.faces) {
    const die = text("span", face); die.className = `die ${face === "Miss" ? "miss" : "success"}`; faces.append(die);
  }
  return faces;
}
function diceRow(result, row = text("section", "")) {
  row.className = `dice-group ${result.pool.family.toLowerCase()}`;
  row.append(text("small", rollLabel(result.pool)), diceFaces(result));
  row.append(text("strong", diceTotal(result)));
  return row;
}
function diceTotal(result) {
  return result.pool.family === "Attack" ? `${result.successes} Hits`
    : result.pool.family === "Defence" ? `${result.successes} Blocks` : result.successes > 0 ? "Success" : "Failed";
}
function renderCombatDice() {
  const combat = combatDice;
  ui.dice.replaceChildren(); ui.dice.className = "dice-tray combat-dice";
  const heading = text("div", ""); heading.className = "combat-heading";
  const identity = text("div", "");
  identity.append(text("small", unitName(combat.context.attackerId)),
    text("strong", combat.context.abilityName ?? actionLabel({ sourceUnitId: combat.context.attackerId, sourceActionId: combat.context.actionId })));
  heading.append(identity);
  if (combat.finished) {
    const dismiss = text("button", "\u00d7"); dismiss.className = "combat-dismiss";
    dismiss.setAttribute("aria-label", "Dismiss combat results"); dismiss.addEventListener("click", clearDice);
    heading.append(dismiss);
  } else heading.append(text("small", `${combat.context.targets.length} ${combat.context.targets.length === 1 ? "target" : "targets"}`));
  ui.dice.append(heading);
  const attack = text("section", ""); attack.className = "combat-attack";
  attack.append(text("small", "ATTACK"));
  if (combat.attack) {
    attack.classList.add("dice-group"); attack.classList.add("attack");
    attack.append(diceFaces(combat.attack), text("strong", diceTotal(combat.attack)));
  } else attack.append(text("span", `${combat.context.attackDice} dice \u00b7 awaiting roll`));
  ui.dice.append(attack);
  const targets = text("div", ""); targets.className = "combat-targets";
  for (const [index, target] of combat.context.targets.entries()) {
    const result = combat.defences.get(target.targetId), outcome = combat.outcomes.get(target.targetId);
    const row = text("section", ""); row.className = "combat-target";
    row.dataset.targetId = target.targetId;
    row.dataset.pending = String(!outcome && index === combat.context.targetIndex && combat.context.stage === "DefenceRoll");
    const name = text("strong", unitName(target.targetId)); name.className = "combat-target-name";
    row.append(name);
    const damage = text("span", outcome ? `${outcome.damage} Damage` : result ? "Resolving\u2026" : "Awaiting defence");
    damage.className = "combat-outcome"; damage.dataset.damage = String((outcome?.damage ?? 0) > 0);
    row.append(damage);
    if (result) {
      row.classList.add("dice-group"); row.classList.add("defence");
      row.append(diceFaces(result), text("b", diceTotal(result)));
    } else {
      const pending = text("span", outcome ? "Resolved" : `${target.defenceDice} Defence dice`);
      pending.className = "combat-target-wait";
      row.append(pending, text("b", outcome ? `${outcome.blocks} Blocks` : row.dataset.pending === "true" ? "Your roll" : "\u2014"));
    }
    targets.append(row);
  }
  ui.dice.append(targets);
  ui.dice.hidden = false;
  ui.dice.tabIndex = 0;
  highlightContext();
}
function renderDice(result) {
  const pool = result.pool, combat = combatDice;
  if (combat && !combat.finished && pool.sourceUnitId === combat.context.attackerId && pool.sourceActionId === combat.context.actionId
      && (pool.family === "Attack" || pool.family === "Defence" && combat.context.targets.some(target => target.targetId === pool.ownerUnitId))) {
    clearTimeout(diceDismissal); diceGeneration++;
    if (pool.family === "Attack") combat.attack = result; else combat.defences.set(pool.ownerUnitId, result);
    renderCombatDice();
    return;
  }
  // Noncombat checks retain their independent presentation.
  clearDice(); ui.dice.hidden = false;
  diceRow(result, ui.dice); ui.dice.className = `dice-tray ${pool.family.toLowerCase()}`;
  dismissDiceAfter(4500);
}

// Uses exactly the combined dice lifetime, including a pending manual Defence.
function renderCombatRelationship() {
  for (const node of ui.board.querySelectorAll(".combat-link")) node.remove();
  const combat = combatDice, board = displayedState?.physical.board;
  if (!combat || !board) return;
  const source = combat.positions.get(combat.context.attackerId);
  if (!source) return;
  for (const target of combat.context.targets) {
    const destination = combat.positions.get(target.targetId);
    if (!destination) continue;
    const dx = destination.x - source.x, dy = destination.y - source.y;
    const line = text("span", ""); line.className = "combat-link";
    line.setAttribute("aria-hidden", "true");
    Object.assign(line.style, { left: `${(source.x + .5) / board.width * 100}%`,
      top: `${(source.y + .5) / board.height * 100}%`,
      width: `${Math.hypot(dx, dy) / board.width * 100}%`,
      transform: `rotate(${Math.atan2(dy, dx)}rad)` });
    ui.board.append(line);
  }
}
function highlightContext() {
  syncThreeInteraction();
  const relationship = combatDice?.context ?? attackContext;
  renderCombatRelationship();
  for (const [id, node] of figures) {
    node.classList.toggle("token-selected", id === focusedUnitId);
    node.classList.remove("attack-source"); node.classList.remove("attack-target"); node.classList.remove("event-focus");
    if (id === relationship?.attackerId) node.classList.add("attack-source");
    if (relationship?.targets.some(target => target.targetId === id)) node.classList.add("attack-target");
    if (id === focusedUnitId) node.classList.add("event-focus");
  }
  for (const [key, node] of edges) {
    node.classList.remove("door-focus");
    if (focusedDoor && key === edgeKey(focusedDoor)) node.classList.add("door-focus");
  }
  ui["attack-context"].textContent = attackContext
    ? `${unitName(attackContext.attackerId)} \u00b7 ${attackContext.abilityName ?? actionLabel({ sourceUnitId: attackContext.attackerId, sourceActionId: attackContext.actionId })} \u2192 ${attackContext.targets.map(target => unitName(target.targetId)).join(", ")}` : "";
  positionDecisionPanel();
}

function prepareOccurrence(occurrence, outcome) {
  ui.effect.textContent = outcome.displayText ?? outcome.text;
  if (!occurrence) return;
  ui.effect.dataset.kind = occurrence.kind;
  focusedUnitId = occurrence.targetId ?? occurrence.unitId;
  if (occurrence.door) focusedDoor = occurrence.door;
  switch (occurrence.kind) {
    case "RoundStarted":
    case "TokenDrawn":
      if (combatDice && !combatDice.finished) clearDice(); attackContext = null; focusedDoor = null;
      break;
    case "ActivationStarted":
      latestUnitId = occurrence.unitId;
      if (combatDice && !combatDice.finished) clearDice(); break;
    case "ActivationCompleted":
      if (combatDice && !combatDice.finished) clearDice();
      focusedUnitId = null; attackContext = null; focusedDoor = null; break;
    case "AttackStarted":
      attackContext = occurrence.attackContext;
      if (attackContext) {
        beginCombatDice(attackContext);
        ui.effect.textContent = `${unitName(attackContext.attackerId)} attacks ${attackContext.targets.map(target => unitName(target.targetId)).join(", ")}`;
      } else clearDice();
      break;
    case "AttackTargetResolved":
    case "AttackResolved":
      if (combatDice && !combatDice.finished && (!occurrence.unitId || occurrence.unitId === combatDice.context.attackerId)
          && (!occurrence.actionId || occurrence.actionId === combatDice.context.actionId)) {
        if (occurrence.targetId && combatDice.context.targets.some(target => target.targetId === occurrence.targetId)) {
          combatDice.outcomes.set(occurrence.targetId, { blocks: occurrence.blocks, damage: occurrence.damage });
          ui.effect.textContent = `${unitName(combatDice.context.attackerId)} attacks ${unitName(occurrence.targetId)}: ${occurrence.hits} Hits \u00b7 ${occurrence.blocks} Blocks \u00b7 ${occurrence.damage} Damage`;
          renderCombatDice();
        }
        if (!occurrence.targetId || combatDice.outcomes.size === combatDice.context.targets.length) finishCombatDice();
      }
      if (occurrence.kind === "AttackResolved") attackContext = null;
      break;
    case "DiceRolled":
      renderDice(occurrence.dice);
      ui.effect.textContent = `${rollLabel(occurrence.dice.pool)} · ${diceTotal(occurrence.dice)}`;
      break;
    case "UnitDefeated":
      ui.effect.textContent = `${unitName(occurrence.unitId)} was defeated`;
      break;
    case "PostureChanged":
      if (occurrence.sourceUnitId) ui.effect.textContent += ` (from ${unitName(occurrence.sourceUnitId)})`;
      break;
    case "ActionUsed":
      focusedDoor = occurrence.door ?? null;
      if (combatDice && !combatDice.finished) clearDice();
      if (occurrence.targetId) ui.effect.textContent += ` \u2192 ${unitName(occurrence.targetId)}`;
      if (occurrence.cell) ui.effect.textContent += ` \u2192 (${occurrence.cell.x},${occurrence.cell.y})`;
      if (occurrence.door) ui.effect.textContent += ` \u2192 door ${cellKey(occurrence.door.a)} / ${cellKey(occurrence.door.b)}`;
      break;
  }
  highlightContext();
}

function updateCoordinates() {
  syncThreeInteraction();
  ui.board.classList.toggle("hide-coordinates", !ui.coordinates.checked);
}

ui.scenario.addEventListener("change", () => { describeScenario(); renderControllerSetup(true); });
ui["start-scenario"].addEventListener("click", () => startFromTools("scenario", { scenarioId: ui.scenario.value, ...setupControllers() }));
ui["restart-scenario"].addEventListener("click", () => startFromTools("restart", controllerSetupScenario === snapshot?.scenarioId ? setupControllers() : {}));
ui["import-scenario"].addEventListener("click", () => replaceScenario("scenario/import", { transport: ui["scenario-transport"].value.trim() }));
ui["controller-retry"].addEventListener("click", () => mutate("controller/retry"));
ui["controller-cancel"].addEventListener("click", () => mutate("controller/cancel"));
ui.filter.addEventListener("change", renderSnapshot);
ui.auto.addEventListener("change", () => mutate("preferences", { autoChooseSingleRelevantChoice: ui.auto.checked }));
ui.coordinates.addEventListener("change", updateCoordinates);
const waypointToggle = document.getElementById("waypoints");
waypointToggle.addEventListener("change", () => { ui.board.classList.toggle("hide-waypoints", !waypointToggle.checked); syncThreeInteraction(); });
ui.round.addEventListener("click", () => mutate("round"));
ui.refresh.addEventListener("click", refresh);
ui.skip.addEventListener("click", () => { skipLootCards = true; skipEffects = true; threeViewport?.settle(); playCamera()?.settle(); ui.dice.classList.remove("rolling"); for (const row of ui.dice.querySelectorAll(".rolling")) row.classList.remove("rolling"); cancelPause?.(); updateControls(); });
ui.animate.addEventListener("change", () => { if (!ui.animate.checked) { threeViewport?.settle(); playCamera()?.settle(); } });
globalThis.matchMedia?.("(prefers-reduced-motion: reduce)").addEventListener?.("change", event => {
  if (event.matches) { skipEffects = true; threeViewport?.settle(); playCamera()?.settle(); cancelPause?.(); }
});
globalThis.addEventListener?.("pagehide", event => { if (event.persisted) viewportRouter?.active.setActive(false); else viewportRouter?.dispose(); });
globalThis.addEventListener?.("pageshow", event => { if (event.persisted) viewportRouter?.active.setActive(true); });
// Designer exports enter the same revisioned scenario replacement operation as pasted imports.
globalThis.delvePlayDesign = async transport => {
  const fromDesigner = globalThis.location?.hash === "#designer";
  const title = fromDesigner ? document.getElementById("designer-title")?.value : null;
  const succeeded = await replaceScenario("scenario/import", { transport }, true);
  if (succeeded && fromDesigner) globalThis.delveSetPlayContext({ title: title || "Designer playtest", returnHash: "#designer", returnLabel: "Back to Designer" });
  return succeeded;
};
globalThis.delvePlayGenerated = code => replaceScenario("scenario/import", { generationCode: code }, true);
ui["commentary-toggle"].addEventListener("click", () => { commentaryVisible = !commentaryVisible; renderCommentary(); });
renderCommentary();

// Load the preferred renderer only when entering play, leaving the library lightweight.
function enterTable() {
  if (["#play", "#playtest"].includes(globalThis.location?.hash)) void updateBoardView();
}
globalThis.addEventListener?.("hashchange", enterTable);
enterTable();
await refresh();
