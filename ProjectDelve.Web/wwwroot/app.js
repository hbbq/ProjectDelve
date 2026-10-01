const ui = Object.fromEntries(["board", "status", "effect", "round", "refresh", "skip", "animate", "error", "prompt", "choices", "units", "events"]
  .map(id => [id, document.getElementById(id)]));
let snapshot;
let busy = false;
let skipEffects = false;
let cancelPause;
const figures = new Map();
const edges = new Map();
const cells = new Map();

// These helpers describe geometry and presentation only. Legal choices arrive from the engine.
const cellKey = cell => `${cell.x},${cell.y}`;
const edgeKey = edge => [cellKey(edge.a), cellKey(edge.b)].sort().join("|");
const unitLabel = id => ({ barbarian: "B", rogue: "R", "grunt-1": "Gr1", "grunt-2": "Gr2",
  "zombie-1": "Z1", "zombie-2": "Z2", "archer-1": "A1", "archer-2": "A2", "goblin-1": "G1" })[id] ?? id;
const text = (tag, value) => { const node = document.createElement(tag); node.textContent = value; return node; };

function placeFigure(node, cell, board) {
  node.style.left = `${(cell.x + .5) / board.width * 100}%`;
  node.style.top = `${(cell.y + .5) / board.height * 100}%`;
}

function renderBoard(state) {
  const board = state.physical.board;
  ui.board.replaceChildren(); figures.clear(); edges.clear(); cells.clear();
  ui.board.style.gridTemplateColumns = `repeat(${board.width}, minmax(0, 1fr))`;
  ui.board.style.gridTemplateRows = `repeat(${board.height}, minmax(0, 1fr))`;
  ui.board.style.setProperty("--columns", board.width);
  ui.board.style.setProperty("--board-ratio", board.width / board.height);
  ui.board.style.aspectRatio = `${board.width} / ${board.height}`;
  const terrain = new Map((board.terrain ?? []).map(tile => [cellKey(tile.position), tile.kind]));
  for (let y = 0; y < board.height; y++) for (let x = 0; x < board.width; x++) {
    const kind = terrain.get(cellKey({ x, y })) ?? "StoneFloor";
    const node = text("div", `${x},${y}`);
    node.className = `cell ${kind}`; node.dataset.cell = cellKey({ x, y });
    node.title = kind;
    const symbol = { Grass: "Grass", Tree: "Tree", Water: "Water", StoneFloorWithTable: "Table" }[kind];
    if (symbol) node.append(text("span", symbol));
    cells.set(node.dataset.cell, node); ui.board.append(node);
  }
  for (const edge of board.edges) {
    if (edge.kind === "None") continue;
    const node = document.createElement("div");
    node.className = `edge ${edge.kind}`;
    const vertical = edge.a.y === edge.b.y;
    node.style.left = `${((edge.a.x + edge.b.x) / 2 + .5) / board.width * 100}%`;
    node.style.top = `${((edge.a.y + edge.b.y) / 2 + .5) / board.height * 100}%`;
    node.style.width = vertical ? "var(--edge-thickness)" : `${100 / board.width}%`;
    node.style.height = vertical ? `${100 / board.height}%` : "var(--edge-thickness)";
    node.title = edge.kind; edges.set(edgeKey(edge), node); ui.board.append(node);
  }
  for (const figure of state.physical.figures) {
    const node = text("div", unitLabel(figure.id));
    node.className = `figure${state.units.find(unit => unit.id === figure.id)?.sideId === "blue" ? " hero" : ""}`;
    node.style.width = `${70 / board.width}%`;
    node.title = `${figure.id} · ${figure.posture}`;
    placeFigure(node, figure.position, board); figures.set(figure.id, node); ui.board.append(node);
  }
}

function renderSnapshot() {
  const state = snapshot.result.state;
  renderBoard(state);
  ui.status.textContent = `Round ${state.round} · ${state.round === 0 ? "Ready" : state.roundComplete ? "Complete" : `${state.activeTypeId} · ${state.phase}`} · revision ${snapshot.revision}`;
  ui.units.replaceChildren(...state.units.map(unit => {
    const type = state.types.find(type => type.id === unit.typeId);
    return text("p", `${unitLabel(unit.id)} · ${unit.sideId} · HP ${unit.currentHp}/${type.hp} · MOV ${type.mov} RNG ${type.rng} ATK ${type.atk} DEF ${type.def}`);
  }));
  ui.choices.replaceChildren();
  const decision = snapshot.result.nextInput;
  // Map supplied choices onto rendered objects; ambiguous targets keep the choice-panel interface.
  const boardChoices = new Map();
  const offer = (node, label, key) => {
    if (!node) return;
    if (!boardChoices.has(node)) boardChoices.set(node, new Map());
    boardChoices.get(node).set(key, label);
  };
  ui.prompt.textContent = decision ? `${decision.isMoveAfterAttack ? "Move after attack" : decision.kind} · ${decision.unitId ?? "Choose a Unit"}` : state.roundComplete ? "Round complete. Start the next round when ready." : "Start the first round.";
  for (const candidate of decision?.candidates ?? []) {
    const label = candidate.tryOpenDoor ? `Try door ${cellKey(candidate.door.a)} ? ${cellKey(candidate.door.b)} (${candidate.tryOpenDoor.successCount}/6)`
      : candidate.action === "NormalAttack" ? `Attack ${unitLabel(candidate.targetId)}`
      : candidate.action === "OpenDoor" ? `Open door ${cellKey(candidate.door.a)} ↔ ${cellKey(candidate.door.b)}`
      : candidate.destination ? `Move to (${cellKey(candidate.destination)})` : unitLabel(candidate.key);
    addChoice(label, candidate.key);
    if (decision.kind === "Move" && candidate.destination) {
      offer(cells.get(cellKey(candidate.destination)), label, candidate.key);
    } else if (candidate.action === "NormalAttack") {
      offer(figures.get(candidate.targetId), label, candidate.key);
      const figure = state.physical.figures.find(figure => figure.id === candidate.targetId);
      if (figure) offer(cells.get(cellKey(figure.position)), label, candidate.key);
    } else if ((candidate.action === "OpenDoor" || candidate.tryOpenDoor) && candidate.door) {
      offer(edges.get(edgeKey(candidate.door)), label, candidate.key);
    }
  }
  if (decision?.allowsNone) addChoice(decision.kind === "Move" ? "Stay here" : "Take no action", null);
  if (decision?.kind === "Move" && decision.allowsNone)
    offer(figures.get(decision.unitId), "Stay here", null);
  for (const [node, choices] of boardChoices) {
    if (choices.size !== 1) continue;
    const [key, label] = choices.entries().next().value;
    bindBoardChoice(node, label, key);
  }
  updateControls();
}

function chooseCandidate(key) {
  return mutate("decision", { candidateKey: key });
}

function addChoice(label, key) {
  const button = text("button", label);
  button.addEventListener("click", () => chooseCandidate(key));
  ui.choices.append(button);
}

function bindBoardChoice(node, label, key) {
  node.classList.add("board-choice");
  if (node.classList.contains("cell")) node.classList.add("legal");
  node.title = label;
  node.setAttribute("role", "button");
  node.setAttribute("aria-label", label);
  node.addEventListener("click", event => {
    event.stopPropagation();
    chooseCandidate(key);
  });
  node.addEventListener("keydown", event => {
    if (event.key !== "Enter" && event.key !== " ") return;
    event.preventDefault();
    chooseCandidate(key);
  });
}

function updateControls() {
  ui.refresh.disabled = busy;
  ui.round.disabled = busy || !snapshot || !(snapshot.result.state.round === 0 || snapshot.result.state.roundComplete);
  ui.round.textContent = snapshot?.result.state.round ? "Start next round" : "Start round";
  for (const button of ui.choices.querySelectorAll("button")) button.disabled = busy;
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
    // Retain the previous visible board until the ordered effects have finished.
    snapshot = await request(`/${operation}`, { expectedRevision: snapshot.revision, ...body });
    ui.events.replaceChildren();
    for (const event of snapshot.result.events) {
      ui.events.append(text("li", describe(event)));
      if (!skipEffects) await present(event);
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

function describe(event) {
  switch (event.kind) {
    case "MovementCompleted": return `${unitLabel(event.unitId)} ${event.isMoveAfterAttack ? "moved after attack" : "moved"}: ${event.path.map(cellKey).join(" → ")}`;
    case "AttackResolved": return `${unitLabel(event.unitId)} → ${unitLabel(event.targetId)}: ${event.hits} Hits, ${event.blocks} Blocks, ${event.damage} Damage`;
    case "UnitDied": return `${unitLabel(event.unitId)} died`;
    case "DoorOpeningAttemptResolved": return `${unitLabel(event.unitId)} tried door ${cellKey(event.door.a)} ? ${cellKey(event.door.b)}: D6 ${event.dieRoll}, ${event.successCount}/6 ? ${event.succeeded ? "success" : "failed; door stays closed"} (Act consumed)`;
    case "DoorOpened": return `${unitLabel(event.unitId)} opened door ${cellKey(event.door.a)} ↔ ${cellKey(event.door.b)}`;
    case "TokenDrawn": return `Token drawn: ${event.typeId}`;
    default: return event.kind;
  }
}

function pause(ms) {
  if (skipEffects) return Promise.resolve();
  return new Promise(resolve => {
    const finish = () => { clearTimeout(timer); cancelPause = null; resolve(); };
    const timer = setTimeout(finish, ms); cancelPause = finish;
  });
}

async function present(event) {
  ui.effect.textContent = describe(event);
  switch (event.kind) {
    case "MovementCompleted": {
      const node = figures.get(event.unitId);
      if (!node) break;
      for (const cell of event.path.slice(1)) {
        if (skipEffects) break;
        node.style.transition = "left .22s linear, top .22s linear";
        placeFigure(node, cell, snapshot.result.state.physical.board);
        await pause(250);
      }
      break;
    }
    case "AttackResolved": {
      const attacker = figures.get(event.unitId), target = figures.get(event.targetId);
      attacker?.classList.add("attacking"); target?.classList.add("target");
      ui.effect.textContent = `${unitLabel(event.unitId)} attacks ${unitLabel(event.targetId)}`;
      await pause(350);
      ui.effect.textContent = `${event.hits} Hits · ${event.blocks} Blocks`;
      await pause(450);
      ui.effect.textContent = `${event.damage} Damage`;
      await pause(400);
      attacker?.classList.remove("attacking"); target?.classList.remove("target");
      break;
    }
    case "UnitDied": {
      const node = figures.get(event.unitId); node?.classList.add("dying");
      await pause(320); node?.remove(); figures.delete(event.unitId); break;
    }
    case "DoorOpeningAttemptResolved": {
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

ui.round.addEventListener("click", () => mutate("round"));
ui.refresh.addEventListener("click", refresh);
ui.skip.addEventListener("click", () => { skipEffects = true; cancelPause?.(); updateControls(); });
await refresh();
