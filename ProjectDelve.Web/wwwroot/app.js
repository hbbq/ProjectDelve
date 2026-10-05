const ui = Object.fromEntries(["board", "status", "effect", "round", "refresh", "skip", "animate", "coordinates", "filter", "auto", "error", "prompt", "choices", "unit-card", "events"]
  .map(id => [id, document.getElementById(id)]));
let snapshot;
let busy = false;
let skipEffects = false;
let cancelPause;
let displayedState;
let latestUnitId;
let hoveredUnitId;
let displayedCards;
const cardChoiceKeys = new Set();
const boardChoiceKeys = new Set();
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
  node.style.left = `${(cell.x + .5) / board.width * 100}%`;
  node.style.top = `${(cell.y + .5) / board.height * 100}%`;
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
    if (!cells.has(key)) ui.board.append(node);
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
    node.textContent = abbreviatedName(name) + (peers.length > 1 ? peers.findIndex(peer => peer.id === figure.id) + 1 : "");
    // Snapshot positions settle any skipped movement without animating a state correction.
    node.style.transition = "none";
    delete node.dataset.hpLabel;
    node.removeAttribute("aria-label");
    node.className = `figure${unit?.sideId === "blue" ? " side-blue" : ""}`;
    node.style.width = `${70 / board.width}%`;
    node.title = `${figure.id} · ${figure.posture}`;
    if (type?.hp > 1) {
      const hp = text("span", `${unit.currentHp}/${type.hp}`);
      hp.className = "figure-hp";
      hp.style.setProperty("--hp-fill", `${Math.max(0, Math.min(1, unit.currentHp / type.hp)) * 100}%`);
      node.dataset.hpLabel = `HP ${unit.currentHp}/${type.hp}`;
      node.title += ` · ${node.dataset.hpLabel}`;
      node.setAttribute("aria-label", `${figure.id} · ${node.dataset.hpLabel}`);
      hp.setAttribute("aria-hidden", "true");
      node.append(hp);
    }
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

function renderState(state, preserveNodes = true, cards = snapshot.presentation.cards) {
  displayedCards = cards;
  renderBoard(state, preserveNodes);
  const typeName = state.types.find(type => type.id === state.activeTypeId)?.displayName ?? state.activeTypeId;
  ui.status.textContent = `Round ${state.round} · ${state.round === 0 ? "Ready" : state.roundComplete ? "Complete" : `${typeName} · ${state.currentUnitId ?? "Select Unit"}`} · revision ${snapshot.revision}`;
  displayedState = state;
  if (state.currentUnitId) latestUnitId = state.currentUnitId;
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
  card.append(text("p", `HP ${unit.currentHp} / ${type.hp}`));
  for (const entry of content.entries) {
    const row = text("div", ""); row.className = "card-ability";
    const ability = entry.content;
    row.append(text("strong", `${ability.name}${ability.useLimitText == null ? "" : ` [${ability.useLimitText}]`}`));
    row.append(text("small", ability.category));
    row.append(text("p", ability.description));
    if (entry.uses) row.append(text("small", `${entry.uses.remainingUses} / ${entry.uses.maxUses} uses`));
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

function clearAffectedPreview() {
  for (const node of figures.values()) node.classList.remove("affected-preview");
}

function choiceButton(candidate) {
  const button = text("button", candidate.label);
  button.addEventListener("click", () => chooseCandidate(candidate.key));
  const preview = () => {
    clearAffectedPreview();
    if (busy || !visibleChoice(candidate)) return;
    for (const id of candidate.affectedUnitIds) figures.get(id)?.classList.add("affected-preview");
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
  if (decision?.noneChoice && !boardChoiceKeys.has(null)) ui.choices.append(choiceButton(decision.noneChoice));
  for (const candidate of decision?.candidates ?? []) {
    if (!visibleChoice(candidate) || boardChoiceKeys.has(candidate.key) || cardChoiceKeys.has(candidate.key)) continue;
    ui.choices.append(choiceButton(candidate));
  }
}

function renderSnapshot() {
  const state = snapshot.result.state;
  hoveredUnitId = null;
  boardChoiceKeys.clear();
  renderState(state, false);
  ui.auto.checked = snapshot.autoChooseSingleRelevantChoice;
  const decision = snapshot.presentation.decision;
  // Bind supplied selection references. Affected Units are preview data, never selections.
  const boardChoices = new Map();
  const offer = (node, candidate) => {
    if (!node) return;
    if (!boardChoices.has(node)) boardChoices.set(node, new Map());
    boardChoices.get(node).set(candidate.key, candidate.label);
  };
  ui.prompt.textContent = decision ? `${decision.prompt} · ${decision.unitId ?? ""}` : state.roundComplete ? "Round complete. Start the next round when ready." : "Start the first round.";
  for (const candidate of [...(decision?.candidates ?? []), ...(decision?.noneChoice ? [decision.noneChoice] : [])]) {
    if (!visibleChoice(candidate)) continue;
    const selection = candidate.interaction;
    if (selection.kind === "Unit") {
      offer(figures.get(selection.unitId), candidate);
      const figure = state.physical.figures.find(figure => figure.id === selection.unitId);
      if (figure) offer(cells.get(cellKey(figure.position)), candidate);
    } else if (selection.kind === "Position") {
      offer(cells.get(cellKey(selection.position)), candidate);
    } else if (selection.kind === "Door") {
      offer(edges.get(edgeKey(selection.door)), candidate);
    }
  }
  for (const [node, choices] of boardChoices) {
    if (choices.size !== 1) continue;
    const [key, label] = choices.entries().next().value;
    bindBoardChoice(node, label, key);
    boardChoiceKeys.add(key);
  }
  renderChoicePanel();
  updateControls();
}

function chooseCandidate(key) {
  if (busy) return;
  return mutate("decision", { candidateKey: key });
}

function bindBoardChoice(node, label, key) {
  node.classList.add("board-choice");
  if (node.classList.contains("cell")) node.classList.add("legal");
  const accessibleLabel = node.dataset.hpLabel ? `${label} · ${node.dataset.hpLabel}` : label;
  node.title = accessibleLabel;
  node.setAttribute("role", "button");
  node.setAttribute("aria-label", accessibleLabel);
  node.addEventListener("click", event => {
    event.stopPropagation();
    return chooseCandidate(key);
  });
  node.addEventListener("keydown", event => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    return chooseCandidate(key);
  });
}

function updateControls() {
  if (busy) clearAffectedPreview();
  ui.refresh.disabled = busy;
  ui.filter.disabled = busy || !snapshot;
  ui.auto.disabled = busy || !snapshot;
  ui.round.disabled = busy || !snapshot || !(snapshot.result.state.round === 0 || snapshot.result.state.roundComplete);
  ui.round.textContent = snapshot?.result.state.round ? "Start next round" : "Start round";
  for (const button of ui.choices.querySelectorAll("button")) button.disabled = busy;
  for (const button of ui["unit-card"].querySelectorAll("button")) button.disabled = busy || !snapshot || button.dataset.legal !== "true";
  for (const node of ui.board.querySelectorAll(".board-choice")) {
    const disabled = busy || !snapshot;
    node.setAttribute("aria-disabled", String(disabled));
    node.tabIndex = disabled ? -1 : 0;
  }
  ui.skip.disabled = !busy || skipEffects;
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
  if (busy) return;
  busy = true; updateControls(); ui.error.textContent = "";
  try {
    snapshot = await request(""); ui.events.replaceChildren(); ui.effect.textContent = ""; renderSnapshot();
  } catch (error) { ui.error.textContent = error.message; }
  finally { busy = false; updateControls(); }
}

async function mutate(operation, body = {}) {
  if (busy || !snapshot) return;
  busy = true; skipEffects = !ui.animate.checked; updateControls(); ui.error.textContent = "";
  try {
    // Present from the previous visible state, advancing only with engine snapshots.
    snapshot = await request(`/${operation}`, { expectedRevision: snapshot.revision, ...body });
    ui.events.replaceChildren();
    const steps = new Map(snapshot.result.resolutionSteps.map(step => [step.eventIndex, step.stateAfter]));
    for (const [index, event] of snapshot.presentation.events.entries()) {
      ui.events.append(text("li", describe(event)));
      if (!skipEffects) await present(event);
      if (steps.has(index)) renderState(steps.get(index), true, snapshot.presentation.resolutionSteps.find(step => step.eventIndex === index).cards);
    }
  } catch (error) {
    ui.error.textContent = `${error.message} Synchronized to the server; choose again.`;
    try { snapshot = await request(""); }
    catch { ui.error.textContent = `${error.message} Refresh to reconnect before choosing again.`; snapshot = null; }
  } finally {
    // Events never reconstruct gameplay state. Always finish on the returned authoritative snapshot.
    if (snapshot) renderSnapshot();
    else { ui.choices.replaceChildren(); ui.prompt.textContent = "Refresh to reconnect."; }
    ui.effect.textContent = ""; busy = false; updateControls();
  }
}

function describe(event) { return event.text; }

function pause(ms) {
  if (skipEffects) return Promise.resolve();
  return new Promise(resolve => {
    const finish = () => { clearTimeout(timer); cancelPause = null; resolve(); };
    const timer = setTimeout(finish, ms); cancelPause = finish;
  });
}

async function present(event) {
  ui.effect.textContent = describe(event);
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
        await pause(250);
      }
      break;
    }
    case "AttackSummary": await pause(450); break;
    case "AttackTarget": {
      const attacker = figures.get(event.unitId), target = figures.get(event.targetId);
      attacker?.classList.add("attacking"); target?.classList.add("target");
      await pause(350);
      ui.effect.textContent = `${event.hits} Hits · ${event.blocks} Blocks`;
      await pause(450);
      ui.effect.textContent = `${event.damage} Damage`;
      await pause(400);
      attacker?.classList.remove("attacking"); target?.classList.remove("target");
      break;
    }
    case "Death": {
      const node = figures.get(event.unitId);
      node?.getBoundingClientRect();
      if (node) node.style.transition = "";
      node?.classList.add("dying");
      await pause(320); node?.remove(); figures.delete(event.unitId); break;
    }
    case "DoorAttempt": {
      const node = edges.get(edgeKey(event.door));
      node?.classList.add("target");
      await pause(1000);
      node?.classList.remove("target");
      break;
    }
    case "DoorOpened": {
      const node = edges.get(edgeKey(event.door));
      if (node) { node.className = `edge ${event.door.kind}`; node.title = event.door.kind; }
      await pause(300); break;
    }
    default: await pause(250);
  }
}

function updateCoordinates() {
  ui.board.classList.toggle("hide-coordinates", !ui.coordinates.checked);
}

ui.filter.addEventListener("change", renderSnapshot);
ui.auto.addEventListener("change", () => mutate("preferences", { autoChooseSingleRelevantChoice: ui.auto.checked }));
ui.coordinates.addEventListener("change", updateCoordinates);
ui.round.addEventListener("click", () => mutate("round"));
ui.refresh.addEventListener("click", refresh);
ui.skip.addEventListener("click", () => { skipEffects = true; cancelPause?.(); updateControls(); });
await refresh();
