import { cameraBounds, cameraUnion } from "./camera-geometry.js";

// This adapter accepts ONLY the browser's observed playback frame. Never pass an
// engine query, an Order target, or a later received/final frame into it.
export function cameraObservedFrame(state, presentation = {}) {
  const side = presentation.viewingSideId ?? null;
  return { board: state.physical.board, units: state.units ?? [], figures: state.physical.figures ?? [],
    geometry: presentation.figures ?? {}, side,
    explored: side ? new Set((state.exploredCells?.[side] ?? []).map(c => `${c.x},${c.y}`)) : null,
    objectives: (presentation.objectives ?? []).filter(o => !side || o.sideId === side) };
}
export function cameraFootprint(frame, id) {
  if (!id) return [];
  const figure = frame.figures.find(f => f.id === id);
  if (!figure) return [];
  const cells = frame.geometry[id]?.occupiedCells ?? [figure.position];
  return !frame.explored || cells.some(c => frame.explored.has(`${c.x},${c.y}`)) ? cells : [];
}
export function cameraSafeMovement(event, frame) {
  const span = event.movingFigure?.cellSpan ?? frame.geometry[event.unitId]?.cellSpan ?? 1;
  const visible = anchor => {
    if (!frame.explored) return true;
    for (let y = 0; y < span; y++) for (let x = 0; x < span; x++)
      if (frame.explored.has(`${anchor.x + x},${anchor.y + y}`)) return true;
    return false;
  };
  const segments = [];
  for (const original of event.movementSegments ?? (event.path?.length ? [event.path] : [])) {
    let run = [];
    for (const anchor of original) {
      if (visible(anchor)) run.push(anchor);
      else if (run.length) { segments.push(run); run = []; }
    }
    if (run.length) segments.push(run);
  }
  return { segments, span };
}
export function cameraAnchorBounds(anchor, span = 1) { return { x: anchor.x, y: anchor.y, w: span, h: span }; }

export function cameraEventIntent(occurrence, event, frame) {
  if (!occurrence) return null;
  const footprint = id => cameraBounds(cameraFootprint(frame, id));
  const sides = (...ids) => [...new Set(ids.map(id => frame.units.find(u => u.id === id)?.sideId).filter(Boolean))];
  if (event.role === "Movement") {
    const movement = cameraSafeMovement(event, frame);
    return movement.segments.length ? { kind: "movement", ...movement,
      sides: sides(event.unitId).concat(event.movingFigure?.sideId ?? []) } : null;
  }
  if (occurrence.kind === "AttackStarted") {
    const attack = occurrence.attackContext;
    if (!attack) return null;
    const targets = (attack.targets ?? []).map(t => t.targetId);
    const observedTargets = targets.map(footprint).filter(Boolean);
    const consequence = observedTargets.reduce((b, target) => cameraUnion(b, target), null);
    const bounds = cameraUnion(footprint(attack.attackerId), consequence);
    return bounds ? { kind: "frame", bounds, preferred: observedTargets[0] ?? bounds, sides: sides(attack.attackerId, ...targets) } : null;
  }
  if (["Damage", "Healing"].includes(event.role)) {
    const bounds = footprint(event.targetId);
    return bounds ? { kind: "frame", bounds, sides: sides(event.targetId) } : null;
  }
  if (["DoorAttempt", "DoorOpened", "DoorClosed"].includes(event.role) && event.door) {
    const { a, b } = event.door;
    if (frame.explored && ![a, b].some(c => frame.explored.has(`${c.x},${c.y}`))) return null;
    return { kind: "frame", bounds: { x: (a.x + b.x) / 2 + .4, y: (a.y + b.y) / 2 + .4, w: .2, h: .2 }, sides: sides(occurrence.unitId) };
  }
  if (["ActivationStarted", "UnitCreated", "UnitDefeated"].includes(occurrence.kind)) {
    let bounds = footprint(occurrence.unitId);
    // Defeat removes the figure in StateAfter; only observed event-local geometry
    // or the preceding displayed frame may supply its last visible footprint.
    if (!bounds && occurrence.kind === "UnitDefeated") {
      const cells = occurrence.defeatContext?.occupiedCells ?? [];
      if (!frame.explored || cells.some(c => frame.explored.has(`${c.x},${c.y}`))) bounds = cameraBounds(cells);
    }
    const ownSides = sides(occurrence.unitId);
    if (bounds && occurrence.defeatContext?.unit?.sideId) ownSides.push(occurrence.defeatContext.unit.sideId);
    return bounds ? { kind: "frame", bounds, sides: ownSides } : null;
  }
  return null; // Notices, Orders, dice and unrelated events have no spatial intent.
}

export function cameraHumanSides(assignments = []) { return [...new Set(assignments.filter(a => a.controller === "Human").map(a => a.sideId))]; }
export function cameraOwnHuman(frame, unitId, side, assignments = []) {
  const unit = frame.units.find(u => u.id === unitId);
  return !!unit && unit.sideId === side && cameraFootprint(frame, unitId).length > 0 &&
    assignments.some(a => a.sideId === side && (!a.typeId || a.typeId === unit.typeId) && a.controller === "Human");
}
export function cameraAllowsFollow(preferences, suspended, intent, side) {
  return !!intent && preferences.mode !== "full" && !suspended && preferences.follow !== "off" &&
    (preferences.follow === "all" || !!side && intent.sides.includes(side));
}
