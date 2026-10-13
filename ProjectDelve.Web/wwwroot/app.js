import { styleSide } from "./side-colors.js";

const ui = Object.fromEntries(["scenario", "scenario-description", "start-scenario", "restart-scenario", "scenario-transport", "import-scenario", "board", "status", "effect", "round", "refresh", "skip", "animate", "coordinates", "filter", "auto", "error", "prompt", "choices", "unit-card", "events", "bag", "active-token", "active-unit", "dice", "attack-context", "world-effects", "objectives", "orders", "game-result", "commentary-toggle", "commentary-panel", "commentary-context", "commentary-text", "decision-commentary", "side-controllers", "controller-status", "controller-retry", "controller-cancel"]
  .map(id => [id, document.getElementById(id)]));
let snapshot;
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
let cancelPause;
let displayedState;
let latestUnitId;
let hoveredUnitId;
let displayedCards;
let displayedFigures;
const cardChoiceKeys = new Set();
const boardChoiceKeys = new Set();
let boardChooser;
let attackContext;
let focusedUnitId;
let focusedDoor;
const figures = new Map();
const edges = new Map();
const cells = new Map();

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
}

function renderBoard(state, preserveNodes = false) {
  const board = state.physical.board;
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
  const terrain = new Map((board.terrain ?? []).map(tile => [cellKey(tile.position), tile.kind]));
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const kind = terrain.get(cellKey({ x, y })) ?? "StoneFloor";
    const key = cellKey({ x, y });
    const node = cells.get(key) ?? document.createElement("div");
    node.replaceChildren();
    node.className = `cell ${kind}`; node.dataset.cell = key;
    retainedCells.add(key);
    const coordinates = text("span", node.dataset.cell);
    coordinates.className = "cell-coordinates";
    node.append(coordinates);
    node.title = kind;
    const symbol = { Grass: "Grass", Tree: "Tree", Water: "Water", StoneFloorWithTable: "Table" }[kind];
    if (symbol) node.append(text("span", symbol));
    if (!cells.has(key)) {
      node.addEventListener("mouseenter", () => inspectCell(key));
      node.addEventListener("mouseleave", () => { hoveredUnitId = null; renderUnitCard(); });
      ui.board.append(node);
    }
    cells.set(key, node);
  }
  for (const edge of board.edges) {
    if (edge.kind === "None") continue;
    const key = edgeKey(edge);
    const node = edges.get(key) ?? document.createElement("div");
    retainedEdges.add(key);
    node.className = `edge ${edge.kind}`;
    const vertical = edge.a.y === edge.b.y;
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
    // Snapshot positions settle any skipped movement without animating a state correction.
    node.style.transition = "none";
    delete node.dataset.hpLabel;
    node.removeAttribute("aria-label");
    const span = displayedFigures?.[figure.id]?.cellSpan ?? 1;
    node.dataset.cellSpan = span;
    node.className = `figure${span > 1 ? " large" : ""}${figure.posture === "Lying" ? " lying" : ""}${state.currentUnitId === figure.id ? " selected" : ""}`;
    styleSide(node, unit?.sideId);
    node.style.width = `${(span > 1 ? span * 95 : 70) / board.width}%`;
    node.style.pointerEvents = "";
    node.title = `${figure.id} · ${figure.posture}`;
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

function renderState(state, preserveNodes = true, cards = snapshot.presentation.cards, geometry = snapshot.presentation.figures, world = snapshot.presentation.worldEffects, orders = snapshot.presentation.orders) {
  displayedCards = cards;
  displayedFigures = geometry;
  renderBoard(state, preserveNodes);
  displayedState = state;
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
  card.replaceChildren();
  if (!unit) { card.append(text("p", "Start a round or hover a Unit to inspect it.")); return; }
  const type = state.types.find(type => type.id === unit.typeId);
  const content = displayedCards[unit.id];
  card.append(text("small", hoveredUnitId ? "Inspecting" : state.currentUnitId === unit.id ? "Active Unit" : "Most recently active"));
  card.append(text("h3", content.displayName));
  card.append(text("small", `${unit.id} · ${unit.sideId ?? ""}`));
  const stats = text("div", ""); stats.className = "card-stats";
  for (const stat of ["Mov", "Rng", "Atk", "Def"]) {
    const base = type[stat.toLowerCase()], effective = state[`effective${stat}`]?.[unit.id] ?? base;
    const value = text("p", `${stat.toUpperCase()} ${base === effective ? base : `${base} → ${effective}`}`);
    if (base !== effective) value.className = "modified-stat";
    stats.append(value);
  }
  card.append(stats);
  const hp = text("p", `HP ${unit.currentHp} / ${type.hp}`); hp.className = "card-health"; card.append(hp);
  if (content.footprintLabel) card.append(text("small", content.footprintLabel));
  for (const entry of content.entries) {
    const row = text("div", ""); row.className = "card-ability";
    const ability = entry.content;
    const heading = text("div", ""); heading.className = "ability-heading";
    const name = text("strong", ability.name);
    if (ability.useLimitText != null) {
      const limit = text("span", ` [${ability.useLimitText}]`); limit.className = "sr-only";
      name.append(limit); name.title = `${ability.name} [${ability.useLimitText}]`;
    }
    heading.append(name); heading.append(text("small", ability.category));
    if (entry.uses) heading.append(useMarkers(entry.uses));
    row.append(heading);
    row.append(text("p", ability.description));
    const decision = snapshot?.presentation.decision;
    const candidates = displayedState === snapshot?.result.state && decision?.unitId === unit.id
      ? decision.candidates.filter(candidate => candidate.entryId === ability.id && candidate.interaction.kind === "Direct") : [];
    for (const candidate of candidates) {
      const button = choiceButton(candidate);
      button.dataset.legal = "true";
      button.disabled = busy;
      button.hidden = !visibleChoice(candidate);
      row.append(button);
      if (!button.hidden) cardChoiceKeys.add(candidate.key);
      if (!candidate.relevant) row.append(text("small", button.hidden ? "Irrelevant choice hidden by filter" : "Currently irrelevant"));
    }
    card.append(row);
  }
  // Hover inspection must not strand a Direct choice whose card is no longer shown.
  if (!busy) renderChoicePanel();
}

function useMarkers(uses) {
  const label = `${uses.remainingUses} / ${uses.maxUses} uses`;
  const markers = text("span", ""); markers.className = "use-markers"; markers.title = label;
  const accessible = text("span", label); accessible.className = "sr-only"; markers.append(accessible);
  for (let index = 0; index < uses.maxUses; index++) {
    const circle = text("span", "");
    circle.className = `use-circle${index < uses.remainingUses ? " filled" : ""}`;
    circle.setAttribute("aria-hidden", "true"); markers.append(circle);
  }
  return markers;
}

function clearAffectedPreview() {
  for (const node of figures.values()) node.classList.remove("affected-preview");
  for (const node of cells.values()) node.classList.remove("placement-preview");
}

function previewChoice(candidate) {
  clearAffectedPreview();
  if (busy || !visibleChoice(candidate)) return;
  for (const id of candidate.affectedUnitIds) figures.get(id)?.classList.add("affected-preview");
  for (const cell of candidate.placementCells ?? []) cells.get(cellKey(cell))?.classList.add("placement-preview");
}

function choiceButton(candidate) {
  const button = text("button", candidate.label);
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
  ui.choices.replaceChildren();
  const decision = snapshot?.presentation.decision;
  if (decision?.roll) {
    const detail = text("p", rollLabel(decision.roll)); detail.className = "roll-detail";
    ui.choices.append(detail);
    // Mandatory continuation: only the server-supplied opaque candidate is submitted.
    for (const candidate of decision.candidates) {
      const button = choiceButton(candidate); button.textContent = "Roll Dice";
      button.className = "primary-roll"; ui.choices.append(button);
    }
    return;
  }
  if (boardChooser) {
    const chooser = text("div", ""); chooser.className = "board-chooser";
    chooser.append(text("small", "Choose an option"));
    for (const candidate of boardChooser) chooser.append(choiceButton(candidate));
    const cancel = text("button", "Cancel");
    cancel.addEventListener("click", () => { boardChooser = null; clearAffectedPreview(); renderChoicePanel(); });
    chooser.append(cancel);
    ui.choices.append(chooser);
  }
  if (decision?.noneChoice && !boardChoiceKeys.has(null)) ui.choices.append(choiceButton(decision.noneChoice));
  for (const candidate of decision?.candidates ?? []) {
    if (!visibleChoice(candidate) || boardChoiceKeys.has(candidate.key) || cardChoiceKeys.has(candidate.key)) continue;
    ui.choices.append(choiceButton(candidate));
  }
}

function renderSnapshot() {
  const state = snapshot.result.state;
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
  if (state.round === 0) ui.dice.replaceChildren();
  hoveredUnitId = null;
  boardChooser = null;
  boardChoiceKeys.clear();
  attackContext = state.attackInProgress ?? null;
  focusedUnitId = state.currentUnitId;
  focusedDoor = snapshot.presentation.decision?.roll?.door ?? state.doorInProgress?.door;
  if (attackContext?.attackRoll && !ui.dice.children.length) renderDice(attackContext.attackRoll);
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
  ui.prompt.textContent = state.completion ? "Game over. Restart or choose a scenario to play again." : decision ? `${decision.prompt} · ${decision.unitId ?? ""}` : state.roundComplete ? "Round complete. Start the next round when ready." : "Start the first round.";
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
    if (candidates.length === 1) boardChoiceKeys.add(candidates[0].key);
  }
  renderChoicePanel();
  updateControls();
}

function chooseCandidate(key) {
  if (busy) return;
  const commentary = (ui["decision-commentary"].value ?? "").trim().slice(0, 280);
  return mutate("decision", { candidateKey: key, ...(commentary ? { commentary } : {}) })
    .then(succeeded => { if (succeeded) ui["decision-commentary"].value = ""; return succeeded; });
}

function bindBoardChoice(node, candidates) {
  const label = candidates.map(candidate => candidate.label).join(" / ");
  const select = () => {
    if (busy) return;
    if (candidates.length === 1) return chooseCandidate(candidates[0].key);
    boardChooser = candidates;
    clearAffectedPreview();
    renderChoicePanel();
    ui.choices.querySelectorAll("button")[0]?.focus();
  };
  node.classList.add("board-choice");
  if (candidates.length === 1) {
    node.addEventListener("mouseenter", () => {
      if (node.dataset.cell) inspectCell(node.dataset.cell);
      previewChoice(candidates[0]);
    });
    node.addEventListener("focus", () => previewChoice(candidates[0]));
    node.addEventListener("mouseleave", clearAffectedPreview);
    node.addEventListener("blur", clearAffectedPreview);
  }
  if (node.classList.contains("cell")) node.classList.add("legal");
  const accessibleLabel = [label, node.dataset.postureLabel, node.dataset.hpLabel].filter(Boolean).join(" · ");
  node.title = accessibleLabel;
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
  for (const button of ui.choices.querySelectorAll("button")) button.disabled = busy;
  for (const button of ui["unit-card"].querySelectorAll("button")) button.disabled = busy || !snapshot || button.dataset.legal !== "true";
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
    ui.dice.replaceChildren(); ui.events.replaceChildren(); ui.effect.textContent = "";
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
        if (!playbackOperationActive) { skipEffects = !ui.animate.checked; playbackOperationActive = true; }
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
    snapshot = await request(""); clearCommentary(); ui.events.replaceChildren(); ui.effect.textContent = ""; ui.dice.replaceChildren(); renderSnapshot();
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
  busy = true; skipEffects = !ui.animate.checked; updateControls(); ui.error.textContent = "";
  try {
    // Present from the previous visible state, advancing only with engine snapshots.
    snapshot = await request(`/${operation}`, { expectedRevision: snapshot.revision, ...body });
    succeeded = true;
    if (["scenario", "scenario/import", "restart"].includes(operation)) { latestUnitId = null; clearCommentary(); }
    ui.events.replaceChildren();
    await playEvents(snapshot);
  } catch (error) {
    clearCommentary();
    ui.error.textContent = `${error.message} Synchronized to the server; choose again.`;
    if (operation !== "scenario/import") ui.dice.replaceChildren();
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
      commentsAt(index);
      ui.events.append(text("li", describe(event)));
      const occurrence = view.result.events[index];
      const projected = view.presentation.resolutionSteps.find(step => step.eventIndex === index);
      prepareOccurrence(occurrence, event);
      if (occurrence?.kind === "WorldCardDrawn" && projected) renderWorldEffects(projected.worldEffects);
      if (["TokenDrawn", "RoundStarted", "ActivationBagPopulated", "RoundCompleted", "ActivationCompleted"].includes(occurrence?.kind) && steps.has(index)) {
        renderTurn(steps.get(index));
      }
      if (!skipEffects) await present(event, occurrence);
      if (generation !== playbackGeneration) return;
      if (steps.has(index)) {
        const before = displayedState;
        const after = steps.get(index);
        renderState(after, true, projected.cards, projected.figures, projected.worldEffects, projected.orders);
        if (["AttackTarget", "Damage", "Healing"].includes(event.role) && event.targetId) {
          const hpBefore = before?.units.find(unit => unit.id === event.targetId)?.currentHp;
          const hpAfter = after.units.find(unit => unit.id === event.targetId)?.currentHp;
          if (hpBefore != null && hpAfter != null) ui.effect.textContent += ` \u00b7 HP ${hpBefore} \u2192 ${hpAfter}`;
        }
        if (!skipEffects && occurrence?.kind === "UnitCreated") figures.get(occurrence.unitId)?.classList.add("created");
        if (!skipEffects && occurrence?.kind === "PostureChanged") figures.get(occurrence.unitId)?.classList.add("posture-changed");
        if (["AttackTargetResolved", "PostureChanged", "UnitCreated", "PlacesSwapped", "UnitRepositioned"].includes(occurrence?.kind)) await guardedPause(380);
      }
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
  if (!occurrence) ui.effect.textContent = describe(event);
  if (occurrence?.kind === "DiceRolled") {
    ui.dice.classList.add("rolling");
    await guardedPause(260);
    ui.dice.classList.remove("rolling");
    await guardedPause(420);
    return;
  }
  const delay = { TokenDrawn: 520, ActivationStarted: 300, ActivationCompleted: 280,
    AttackStarted: 420, ActionUsed: 300, RoundStarted: 400, RoundCompleted: 350,
    WorldCardDrawn: 1000, WorldContinuousChanged: 700, WorldCardDiscarded: 450, WorldDeckReshuffled: 600,
    PostureChanged: 200, UnitCreated: 200 }[occurrence?.kind];
  if (delay != null) { await guardedPause(delay); return; }
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
      if (node) { node.className = `edge ${event.door.kind}`; node.title = event.door.kind; }
      await guardedPause(300); break;
    }
    default: await guardedPause(250);
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
    const target = order.target;
    const targetLabel = target?.kind === "Unit" ? `Unit ${target.unitId}`
      : target?.kind === "Group" ? `${state.types.find(type => type.id === target.group.typeId)?.displayName ?? target.group.typeId} · ${target.group.sideId}`
      : target?.kind === "Side" ? `Side ${target.sideId}`
      : target?.kind === "Door" ? `Door (${target.door.a.x},${target.door.a.y})–(${target.door.b.x},${target.door.b.y})`
      : null;
    const label = order.kind === "DoNothing" ? "Do Nothing"
      : order.kind === "HoldPosition" ? "Hold Position"
      : order.kind === "MoveTo" ? `Move To (${order.destination.x},${order.destination.y})`
      : order.kind === "InteractWith" ? `Interact With · ${targetLabel}`
      : order.kind === "Attack" || order.kind === "Protect" ? `${order.kind} · ${targetLabel}`
      : order.kind;
    const row = text("p", `${name} · ${order.token.sideId}: ${label}`);
    styleSide(row, order.token.sideId); panel.append(row);
  }
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
  result.hidden = !completion;
  result.replaceChildren();
  if (completion) {
    const sides = completion.achievedSideIds;
    result.append(text("h2", sides.length === 1 ? `Victory: ${sides[0]}` : "Simultaneous victory"));
    result.append(text("p", `Game over. ${sides.join(", ")} ${sides.length === 1 ? "achieved its Objective." : "each achieved their Objective."}`));
  }
  ui.objectives.replaceChildren(text("h2", "Side objectives"));
  for (const objective of snapshot?.presentation.objectives ?? []) {
    const row = text("p", `${objective.sideId}: ${objective.name}`);
    styleSide(row, objective.sideId);
    if (completion) row.append(text("strong", completion.achievedSideIds.includes(objective.sideId) ? " - Achieved" : " - Not achieved"));
    ui.objectives.append(row);
  }
  ui.status.textContent = `Round ${state.round} \u00b7 ${state.completion ? "Game over" : state.round === 0 ? "Ready" : state.roundComplete ? "Complete" : "In play"}`;
  ui.bag.replaceChildren(text("small", `Activation Bag \u00b7 ${(state.bag ?? []).length} remaining`));
  for (const token of state.bag ?? []) ui.bag.append(tokenNode(token, state));
  ui["active-token"].replaceChildren(text("small", "Drawn token"));
  ui["active-token"].className = ui.effect.dataset.kind === "TokenDrawn" ? "token-draw" : "";
  if (state.activeToken) ui["active-token"].append(tokenNode(state.activeToken, state));
  else ui["active-token"].append(text("span", "Awaiting draw"));
  ui["active-unit"].textContent = state.currentUnitId
    ? `Activating \u00b7 ${displayedCards[state.currentUnitId]?.displayName ?? state.currentUnitId} \u00b7 ${state.currentUnitId}`
    : state.completion ? "Game complete" : "Choose a Unit or start a round";
}

function actionLabel(pool) {
  const card = displayedCards?.[pool.sourceUnitId];
  return card?.entries.find(entry => entry.content.id === pool.sourceActionId)?.content.name ?? pool.sourceActionId;
}
function rollLabel(pool) {
  const owner = displayedCards?.[pool.ownerUnitId]?.displayName ?? pool.ownerUnitId;
  return `${pool.purpose} \u00b7 ${pool.family === "D6" ? `${pool.count}d6` : `${pool.count} ${pool.family} dice`} \u00b7 ${owner} (${pool.ownerUnitId}) \u00b7 ${actionLabel(pool)}`;
}

function renderDice(result) {
  ui.dice.replaceChildren();
  ui.dice.className = `dice-tray ${result.pool.family.toLowerCase()}`;
  ui.dice.append(text("small", rollLabel(result.pool)));
  const faces = text("div", ""); faces.className = "dice-faces";
  for (const face of result.faces) {
    const die = text("span", face); die.className = `die ${face === "Miss" ? "miss" : "success"}`;
    faces.append(die);
  }
  ui.dice.append(faces);
  const label = result.pool.family === "Attack" ? `${result.successes} Hits`
    : result.pool.family === "Defence" ? `${result.successes} Blocks`
    : result.successes > 0 ? "Success" : "Failed";
  ui.dice.append(text("strong", label));
}

function highlightContext() {
  for (const [id, node] of figures) {
    node.classList.remove("attack-source"); node.classList.remove("attack-target"); node.classList.remove("event-focus");
    if (id === attackContext?.attackerId) node.classList.add("attack-source");
    if (attackContext?.targets.some(target => target.targetId === id)) node.classList.add("attack-target");
    if (id === focusedUnitId) node.classList.add("event-focus");
  }
  for (const [key, node] of edges) {
    node.classList.remove("door-focus");
    if (focusedDoor && key === edgeKey(focusedDoor)) node.classList.add("door-focus");
  }
  ui["attack-context"].textContent = attackContext
    ? `${attackContext.attackerId} \u00b7 ${attackContext.abilityName ?? actionLabel({ sourceUnitId: attackContext.attackerId, sourceActionId: attackContext.actionId })} \u2192 ${attackContext.targets.map(target => target.targetId).join(", ")}` : "";
}

function prepareOccurrence(occurrence, outcome) {
  ui.effect.textContent = outcome.text;
  if (!occurrence) return;
  ui.effect.dataset.kind = occurrence.kind;
  focusedUnitId = occurrence.targetId ?? occurrence.unitId;
  if (occurrence.door) focusedDoor = occurrence.door;
  switch (occurrence.kind) {
    case "RoundStarted":
    case "TokenDrawn":
      ui.dice.replaceChildren(); attackContext = null; focusedDoor = null;
      break;
    case "ActivationStarted":
      latestUnitId = occurrence.unitId;
      ui.dice.replaceChildren(); break;
    case "ActivationCompleted": focusedUnitId = null; attackContext = null; focusedDoor = null; break;
    case "AttackStarted":
      attackContext = occurrence.attackContext; ui.dice.replaceChildren(); break;
    case "AttackResolved": attackContext = null; break;
    case "DiceRolled": renderDice(occurrence.dice); break;
    case "PostureChanged":
      if (occurrence.sourceUnitId) ui.effect.textContent += ` (from ${occurrence.sourceUnitId})`;
      break;
    case "ActionUsed":
      focusedDoor = occurrence.door ?? null;
      ui.dice.replaceChildren();
      if (occurrence.targetId) ui.effect.textContent += ` \u2192 ${occurrence.targetId}`;
      if (occurrence.cell) ui.effect.textContent += ` \u2192 (${occurrence.cell.x},${occurrence.cell.y})`;
      if (occurrence.door) ui.effect.textContent += ` \u2192 door ${cellKey(occurrence.door.a)} / ${cellKey(occurrence.door.b)}`;
      break;
  }
  highlightContext();
}

function updateCoordinates() {
  ui.board.classList.toggle("hide-coordinates", !ui.coordinates.checked);
}

ui.scenario.addEventListener("change", () => { describeScenario(); renderControllerSetup(true); });
ui["start-scenario"].addEventListener("click", () => replaceScenario("scenario", { scenarioId: ui.scenario.value, ...setupControllers() }));
ui["restart-scenario"].addEventListener("click", () => replaceScenario("restart", controllerSetupScenario === snapshot?.scenarioId ? setupControllers() : {}));
ui["import-scenario"].addEventListener("click", () => replaceScenario("scenario/import", { transport: ui["scenario-transport"].value.trim() }));
ui["controller-retry"].addEventListener("click", () => mutate("controller/retry"));
ui["controller-cancel"].addEventListener("click", () => mutate("controller/cancel"));
ui.filter.addEventListener("change", renderSnapshot);
ui.auto.addEventListener("change", () => mutate("preferences", { autoChooseSingleRelevantChoice: ui.auto.checked }));
ui.coordinates.addEventListener("change", updateCoordinates);
ui.round.addEventListener("click", () => mutate("round"));
ui.refresh.addEventListener("click", refresh);
ui.skip.addEventListener("click", () => { skipEffects = true; ui.dice.classList.remove("rolling"); cancelPause?.(); updateControls(); });
// Designer exports enter the same revisioned scenario replacement operation as pasted imports.
globalThis.delvePlayDesign = transport => replaceScenario("scenario/import", { transport }, true);
ui["commentary-toggle"].addEventListener("click", () => { commentaryVisible = !commentaryVisible; renderCommentary(); });
renderCommentary();

await refresh();
