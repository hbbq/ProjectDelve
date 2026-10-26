import { cameraBaseCell, cameraPreferences, cameraBounds, cameraUnion, cameraSpanScale, cameraClampPose, cameraFull, cameraProject, cameraUnproject, cameraMatrix, cameraPoseFromMatrix, cameraAutoFit, cameraEnsureVisible, cameraObjectiveIndicator, cameraIndicatorSlot } from "./camera-geometry.js";
import { cameraObservedFrame, cameraFootprint, cameraEventIntent, cameraAnchorBounds, cameraHumanSides, cameraOwnHuman, cameraAllowsFollow } from "./camera-observation.js";

// DOM implementation of the presentation camera. Spatial event intentions live
// in camera-observation.js so another renderer can consume them independently.
export function createBoardCamera({ document, viewport, world, boardElement, animate = () => true, clearPreview = () => {}, onChange = () => {}, environment = globalThis }) {
  const storageKey = "delve.camera.v1";
  let saved;
  try { saved = JSON.parse(environment.localStorage?.getItem(storageKey) ?? "null"); } catch { /* Unavailable/corrupt storage does not prevent play. */ }
  let preferences = cameraPreferences(saved ?? {});
  const controls = Object.fromEntries(["mode", "span", "follow", "min", "max", "margin", "side", "side-label", "resume", "reset", "in", "out", "status", "objectives"].map(id => [id, document.getElementById(`camera-${id}`)]));
  let size = { width: 1, height: 1 }, frame, metadata = {}, side = null, sideChoice = null, target = null;
  let session, suspended = false, activationNumber = 0, manualAtActivation = null, activationKey = null, fittedSpan = null, moveSignature = null, stableDecision = null;
  let movementIntent = null, transitionFrame = null, transitionTimer = null, transitionActive = false;
  let pointer = null, spaceDown = false, suppressClick = false;
  const indicatorNodes = new Map();
  const bound = (n, lo, hi) => Math.max(lo, Math.min(hi, n));
  const reduced = () => environment.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
  const canAnimate = () => animate() && !reduced();
  const persist = () => { try { environment.localStorage?.setItem(storageKey, JSON.stringify(preferences)); } catch { /* Optional local preference. */ } };

  function current() {
    if (!target) return null;
    const transform = environment.getComputedStyle?.(world)?.transform;
    if (transform && transform !== "none" && environment.DOMMatrixReadOnly) {
      const matrix = new environment.DOMMatrixReadOnly(transform);
      if (matrix.a > 0) return cameraPoseFromMatrix(matrix, size);
    }
    return { ...target };
  }
  function stopSampling() {
    if (transitionFrame !== null) environment.cancelAnimationFrame?.(transitionFrame);
    transitionFrame = null;
    if (transitionTimer !== null) environment.clearTimeout(transitionTimer);
    transitionTimer = null; transitionActive = false;
  }
  function changed() { onChange(); renderIndicators(); }
  function sample() {
    transitionFrame = null;
    if (!transitionActive) return;
    changed(); transitionFrame = environment.requestAnimationFrame?.(sample) ?? null;
  }
  function settle() {
    stopSampling(); world.style.transition = "none";
    if (target) writeTransform(target);
    changed();
  }
  function writeTransform(pose) {
    const m = cameraMatrix(pose, size);
    world.style.transform = `matrix(${m.a}, 0, 0, ${m.d}, ${m.e}, ${m.f})`;
  }
  function apply(pose, smooth = true) {
    if (!frame) return;
    pose = cameraClampPose(pose, frame.board, size);
    if (target && Math.abs(target.x - pose.x) < .001 && Math.abs(target.y - pose.y) < .001 && Math.abs(target.scale - pose.scale) < .001) return;
    const from = current();
    if (from && Math.abs(from.x - pose.x) < .001 && Math.abs(from.y - pose.y) < .001 && Math.abs(from.scale - pose.scale) < .001) return;
    stopSampling();
    // Commit the actual intermediate transform before replacing a transition.
    if (from) { world.style.transition = "none"; writeTransform(from); world.getBoundingClientRect(); }
    const travel = from ? Math.hypot((from.x - pose.x) * pose.scale, (from.y - pose.y) * pose.scale) : Infinity;
    const duration = from && smooth && canAnimate() && travel < Math.max(size.width, size.height) * 1.2 ? bound(180 + travel * .25, 180, 350) : 0;
    target = pose;
    world.style.transition = duration ? `transform ${duration}ms cubic-bezier(.2,.7,.2,1)` : "none";
    writeTransform(pose);
    if (duration) {
      transitionActive = true;
      transitionFrame = environment.requestAnimationFrame?.(sample) ?? null;
      transitionTimer = environment.setTimeout(() => { stopSampling(); changed(); }, duration + 40);
    }
    changed(); renderControls();
  }
  function assignments() {
    return metadata.assignments?.length ? metadata.assignments : Object.entries(metadata.controllers ?? {}).map(([sideId, controller]) => ({ sideId, controller }));
  }
  function updateSide() {
    const humans = cameraHumanSides(assignments());
    const sides = [...new Set(frame.units.map(u => u.sideId))];
    side = frame.side ?? (sides.includes(sideChoice) ? sideChoice : humans[0] ?? null);
    controls["side-label"].hidden = !!frame.side || humans.length <= 1 && humans.length !== 0;
    const choices = humans.length ? humans : sides;
    const signature = JSON.stringify([frame.side, choices]);
    if (controls.side.dataset.options !== signature) {
      controls.side.replaceChildren();
      if (!humans.length && !frame.side) { const option = document.createElement("option"); option.value = ""; option.textContent = "Spectator"; controls.side.append(option); }
      for (const id of frame.side ? [frame.side] : choices) { const option = document.createElement("option"); option.value = id; option.textContent = id; controls.side.append(option); }
      controls.side.dataset.options = signature;
    }
    controls.side.value = side ?? "";
  }
  function ownHuman(id) { return cameraOwnHuman(frame, id, side, assignments()); }
  function automaticAllowed() { return preferences.follow !== "off" && preferences.mode !== "full" && !suspended; }
  function fit(decision = stableDecision, force = false) {
    if (!frame || !decision?.unitId || !ownHuman(decision.unitId)) return false;
    const active = cameraFootprint(frame, decision.unitId);
    const moves = (decision.candidates ?? []).filter(c => c.boardVisual?.kind === "move").map(c => c.placementCells ?? []);
    const signature = JSON.stringify(moves);
    if (!force && signature === moveSignature) return true;
    const friends = frame.units.filter(u => u.sideId === side && u.id !== decision.unitId).map(u => cameraFootprint(frame, u.id));
    const result = cameraAutoFit({ active, moves, friends, board: frame.board, viewport: size, preferences });
    if (!result) return false;
    moveSignature = signature;
    controls.status.dataset.overflow = String(result.overflow);
    if (!force && fittedSpan !== null && result.span <= fittedSpan) {
      follow({ kind: "frame", bounds: result.core, sides: [side] });
      return true;
    }
    fittedSpan = result.span;
    apply(result.pose);
    renderControls();
    return true;
  }
  function usefulView(smooth = true) {
    if (!frame) return;
    if (preferences.mode === "full") { apply(cameraFull(frame.board, size), smooth); return; }
    if (preferences.mode === "auto" && fit(stableDecision, true)) return;
    const visibleParty = frame.units.filter(u => (side ? u.sideId === side : true) && cameraFootprint(frame, u.id).length);
    const group = visibleParty.reduce((b, u) => cameraUnion(b, cameraBounds(cameraFootprint(frame, u.id))), null);
    const span = preferences.mode === "fixed" ? preferences.fixedSpan : Math.max(preferences.minSpan, preferences.maxSpan);
    const pose = { x: group ? group.x + group.w / 2 : frame.board.width / 2, y: group ? group.y + group.h / 2 : frame.board.height / 2, scale: cameraSpanScale(size, span) };
    if (group && (group.w * pose.scale > size.width || group.h * pose.scale > size.height)) {
      // Before an activation, a scattered party must not center on empty space.
      const first = visibleParty.find(u => ownHuman(u.id)) ?? visibleParty[0];
      const footprint = cameraBounds(cameraFootprint(frame, first.id));
      pose.x = footprint.x + footprint.w / 2; pose.y = footprint.y + footprint.h / 2;
    }
    apply(pose, smooth);
  }
  function resume() { suspended = false; moveSignature = null; fittedSpan = null; usefulView(); renderControls(); }
  function resetSession() {
    stopSampling();
    if (pointer && viewport.hasPointerCapture?.(pointer.id)) viewport.releasePointerCapture(pointer.id);
    pointer = null; suppressClick = false; spaceDown = false; viewport.classList.remove("camera-dragging");
    suspended = false; activationKey = null; activationNumber = 0; manualAtActivation = null; fittedSpan = null; moveSignature = null; movementIntent = null; stableDecision = null; sideChoice = null; target = null;
    controls.status.dataset.overflow = "false";
    indicatorNodes.clear(); controls.objectives.replaceChildren();
    world.style.transition = "none";
  }
  function setFrame(state, presentation, meta) {
    const identity = meta.sessionId ?? meta.scenarioId ?? "current";
    const replacement = identity !== session;
    if (replacement) { resetSession(); session = identity; }
    frame = cameraObservedFrame(state, presentation); metadata = meta;
    updateSide();
    boardElement.style.width = `${frame.board.width * cameraBaseCell}px`;
    boardElement.style.height = `${frame.board.height * cameraBaseCell}px`;
    world.style.width = `${frame.board.width * cameraBaseCell}px`;
    world.style.height = `${frame.board.height * cameraBaseCell}px`;
    if (replacement || !target) { measure(false); usefulView(false); }
    renderIndicators(); renderControls();
  }
  function stable(decision) {
    stableDecision = decision;
    const unitId = decision?.unitId;
    if (!unitId || !ownHuman(unitId)) return;
    const key = `${metadata.round}:${unitId}:${activationNumber}`;
    const newActivation = key !== activationKey;
    if (newActivation) {
      activationKey = key; fittedSpan = null; moveSignature = null;
      if (preferences.follow !== "off" && manualAtActivation !== activationNumber) suspended = false;
    }
    if (!automaticAllowed()) return;
    if (preferences.mode === "auto") fit(decision);
    else if (newActivation) follow({ kind: "frame", bounds: cameraBounds(cameraFootprint(frame, unitId)), sides: [side] });
    renderControls();
  }
  function follow(intent, smooth = true) {
    if (!frame || !target || !cameraAllowsFollow(preferences, suspended, intent, side)) return;
    apply(cameraEnsureVisible(target, intent.bounds, frame.board, size, intent.preferred), smooth);
  }
  function occurrence(occurrence, event, instant = false) {
    if (!frame) return;
    movementIntent = cameraEventIntent(occurrence, event, frame);
    if (occurrence?.kind === "ActivationStarted" && ownHuman(occurrence.unitId)) {
      activationNumber++; activationKey = null;
      if (preferences.follow !== "off") suspended = false;
      if (preferences.mode === "auto") return; // Wait for authoritative legal placements.
    }
    if (movementIntent?.kind === "movement") {
      if (instant) { const last = movementIntent.segments.at(-1).at(-1); follow({ ...movementIntent, bounds: cameraAnchorBounds(last, movementIntent.span) }, false); }
      return;
    }
    follow(movementIntent, !instant);
  }
  function movementStep(anchor) {
    if (movementIntent?.kind !== "movement" || !movementIntent.segments.some(segment => segment.some(c => c.x === anchor.x && c.y === anchor.y))) return;
    follow({ ...movementIntent, bounds: cameraAnchorBounds(anchor, movementIntent.span) });
  }
  function finishMovement() {
    if (movementIntent?.kind !== "movement") return;
    const last = movementIntent.segments.at(-1).at(-1);
    follow({ ...movementIntent, bounds: cameraAnchorBounds(last, movementIntent.span) }, false);
    settle();
  }
  function measure(reframe = true) {
    const rect = viewport.getBoundingClientRect();
    const width = viewport.clientWidth || rect.width, height = viewport.clientHeight || rect.height;
    if (!width || !height) return;
    if (Math.abs(width - size.width) < .1 && Math.abs(height - size.height) < .1) return;
    const from = current(), span = from ? Math.max(size.width, size.height) / from.scale : preferences.fixedSpan;
    stopSampling(); size = { width, height };
    if (!frame || !reframe) return;
    target = null; // Same world pose at a new viewport size needs a new matrix.
    if (preferences.mode === "full") apply(cameraFull(frame.board, size), false);
    else if (preferences.mode === "auto" && automaticAllowed()) { moveSignature = null; if (!fit(stableDecision, true)) usefulView(false); }
    else apply({ ...from, scale: cameraSpanScale(size, preferences.mode === "fixed" ? preferences.fixedSpan : span) }, false);
  }
  function screen(point) { return target ? cameraProject(point, current(), size) : point; }
  function localPoint(event) { const rect = viewport.getBoundingClientRect(); return { x: event.clientX - rect.left - (viewport.clientLeft || 0), y: event.clientY - rect.top - (viewport.clientTop || 0) }; }
  function manual(pose, smooth = false) {
    if (!frame || preferences.mode === "full") return;
    suspended = true; manualAtActivation = activationNumber;
    apply(pose, smooth); renderControls();
  }
  function zoom(factor, point = { x: size.width / 2, y: size.height / 2 }, smooth = true) {
    if (!frame || !target) return;
    const pose = current(), anchor = cameraUnproject(point, pose, size);
    const span = bound(Math.max(size.width, size.height) / (pose.scale * factor), 5, 50), scale = cameraSpanScale(size, span);
    const next = { scale, x: anchor.x - (point.x - size.width / 2) / scale, y: anchor.y - (point.y - size.height / 2) / scale };
    if (preferences.mode === "full") preferences.mode = "fixed";
    if (preferences.mode === "fixed") { preferences.fixedSpan = span; persist(); }
    manual(next, smooth);
  }
  function renderControls() {
    controls.mode.value = preferences.mode; controls.follow.value = preferences.follow;
    controls.min.value = String(preferences.minSpan); controls.max.value = String(preferences.maxSpan); controls.margin.value = String(preferences.margin);
    controls.span.value = [10,15,20,25].includes(preferences.fixedSpan) ? String(preferences.fixedSpan) : "custom";
    controls.span.hidden = preferences.mode !== "fixed";
    controls.resume.hidden = !suspended || preferences.follow === "off" || preferences.mode === "full";
    const span = target ? Math.max(size.width, size.height) / target.scale : preferences.maxSpan;
    controls.status.textContent = `${suspended ? "Manual · " : ""}${span.toFixed(1)} Cells max${controls.status.dataset.overflow === "true" && preferences.mode === "auto" ? " · some Moves offscreen" : ""}`;
    controls.status.title = "Maximum visible span along the viewport's longer axis. Drag to pan; Alt + wheel to zoom. Follow resumes at your next activation.";
    viewport.dataset.cameraMode = preferences.mode;
  }
  function renderIndicators() {
    if (!frame || !target) return;
    const pose = current(), retained = new Set(), placed = [];
    for (const [index, objective] of frame.objectives.entries()) {
      if (!objective.area) continue;
      let location = cameraObjectiveIndicator(objective.area, pose, size, 26);
      if (!location) continue;
      const key = `${objective.sideId}:${index}`; retained.add(key);
      let node = indicatorNodes.get(key);
      if (!node) {
        node = document.createElement("button"); node.type = "button"; node.className = "camera-objective";
        const arrow = document.createElement("span"); arrow.setAttribute("aria-hidden", "true"); arrow.textContent = "➜"; node.append(arrow);
        const label = document.createElement("span"); label.className = "sr-only"; node.append(label);
        node.addEventListener("click", () => {
          const area = node.cameraArea;
          manual({ ...current(), x: area.x + area.w / 2, y: area.y + area.h / 2 }, true);
        });
        controls.objectives.append(node); indicatorNodes.set(key, node);
      }
      node.cameraArea = objective.area;
      // Keep buttons on the edge, clear of one another and the decision/dice tray.
      const overlay = document.getElementById("board-overlays"), overlayRect = overlay?.getBoundingClientRect(), viewportRect = viewport.getBoundingClientRect();
      const blocked = overlayRect?.width && overlayRect.height ? [{x: overlayRect.left - viewportRect.left - (viewport.clientLeft || 0), y: overlayRect.top - viewportRect.top - (viewport.clientTop || 0), w: overlayRect.width, h: overlayRect.height}] : [];
      location = cameraIndicatorSlot(location, placed, size, blocked);
      placed.push(location);
      node.style.left = `${location.x}px`; node.style.top = `${location.y}px`; node.children[0].style.transform = `rotate(${location.angle}rad)`;
      const direction = [Math.sin(location.angle) < -.4 ? "north" : Math.sin(location.angle) > .4 ? "south" : "", Math.cos(location.angle) > .4 ? "east" : Math.cos(location.angle) < -.4 ? "west" : ""].filter(Boolean).join("");
      const label = `Pan to ${objective.sideId} Reach Area, ${direction || "offscreen"}`;
      node.setAttribute("aria-label", label); node.title = label; node.children[1].textContent = label;
    }
    for (const [key, node] of indicatorNodes) if (!retained.has(key)) { node.remove(); indicatorNodes.delete(key); }
  }
  function setMode(mode) { preferences.mode = mode; moveSignature = null; fittedSpan = null; suspended = false; persist(); usefulView(); renderControls(); }
  controls.mode.addEventListener("change", () => setMode(controls.mode.value));
  controls.span.addEventListener("change", () => { if (controls.span.value !== "custom") { preferences.fixedSpan = Number(controls.span.value); suspended = false; persist(); usefulView(); } });
  controls.follow.addEventListener("change", () => { preferences.follow = controls.follow.value; persist(); if (preferences.follow !== "off") resume(); else { settle(); renderControls(); } });
  for (const [id, key] of [["min","minSpan"],["max","maxSpan"],["margin","margin"]]) controls[id].addEventListener("change", () => {
    preferences = cameraPreferences({ ...preferences, [key]: controls[id].value });
    persist(); if (preferences.mode === "auto") resume(); renderControls();
  });
  controls.side.addEventListener("change", () => { sideChoice = controls.side.value || null; updateSide(); activationKey = null; resume(); });
  controls.reset.addEventListener("click", resume); controls.resume.addEventListener("click", resume);
  controls.in.addEventListener("click", () => zoom(1.2)); controls.out.addEventListener("click", () => zoom(1 / 1.2));
  viewport.addEventListener("pointerdown", event => {
    if (!pointer && [0,1].includes(event.button)) suppressClick = false;
    if (!frame || !target || pointer || ![0,1].includes(event.button) || event.target.closest?.(".board-overlays, .camera-objective")) return;
    pointer = { id: event.pointerId, start: localPoint(event), pose: current(), threshold: event.pointerType === "touch" ? 10 : 6, dragging: false };
    if (event.button === 1 || spaceDown) { pointer.threshold = 0; event.preventDefault(); }
  });
  viewport.addEventListener("pointermove", event => {
    if (!pointer || pointer.id !== event.pointerId) return;
    const point = localPoint(event), dx = point.x - pointer.start.x, dy = point.y - pointer.start.y;
    if (!pointer.dragging && Math.hypot(dx, dy) <= pointer.threshold) return;
    if (!pointer.dragging) {
      pointer.dragging = true; suppressClick = true; viewport.setPointerCapture?.(event.pointerId); clearPreview(); viewport.classList.add("camera-dragging");
    }
    event.preventDefault();
    manual({ ...pointer.pose, x: pointer.pose.x - dx / pointer.pose.scale, y: pointer.pose.y - dy / pointer.pose.scale });
  });
  const finishPointer = event => {
    if (!pointer || pointer.id !== event.pointerId) return;
    if (viewport.hasPointerCapture?.(event.pointerId)) viewport.releasePointerCapture(event.pointerId);
    pointer = null; viewport.classList.remove("camera-dragging");
  };
  viewport.addEventListener("pointerup", finishPointer); viewport.addEventListener("pointercancel", finishPointer); viewport.addEventListener("lostpointercapture", finishPointer);
  // Before capture begins, a small click can end outside the viewport.
  document.addEventListener?.("pointerup", finishPointer); document.addEventListener?.("pointercancel", finishPointer);
  viewport.addEventListener("click", event => {
    if (!suppressClick || event.detail === 0) return;
    suppressClick = false; event.preventDefault(); event.stopImmediatePropagation();
  }, true);
  viewport.addEventListener("dragstart", event => event.preventDefault());
  viewport.addEventListener("wheel", event => {
    if (!event.altKey || event.ctrlKey || !frame || event.target.closest?.(".board-overlays")) return;
    event.preventDefault(); zoom(Math.exp(-event.deltaY * .002), localPoint(event), false);
  }, { passive: false });
  viewport.addEventListener("keydown", event => {
    if (event.target !== viewport) return;
    if (event.key === " ") { spaceDown = true; event.preventDefault(); }
    const direction = { ArrowLeft: [-1,0], ArrowRight:[1,0], ArrowUp:[0,-1], ArrowDown:[0,1] }[event.key];
    if (direction && target) { event.preventDefault(); const pose = current(); manual({ ...pose, x: pose.x + direction[0] * 2, y: pose.y + direction[1] * 2 }, true); }
    if (["+","="].includes(event.key)) { event.preventDefault(); zoom(1.2); }
    if (event.key === "-") { event.preventDefault(); zoom(1 / 1.2); }
  });
  viewport.addEventListener("keyup", event => { if (event.key === " ") spaceDown = false; });
  viewport.addEventListener("blur", () => { spaceDown = false; });
  world.addEventListener("transitionend", event => { if (event.target === world && event.propertyName === "transform") { stopSampling(); changed(); } });
  environment.matchMedia?.("(prefers-reduced-motion: reduce)").addEventListener?.("change", () => { if (reduced()) settle(); });
  const observer = environment.ResizeObserver ? new environment.ResizeObserver(() => measure()) : null;
  observer?.observe(viewport);
  environment.addEventListener?.("resize", () => measure());
  renderControls();
  return { setFrame, stable, occurrence, movementStep, finishMovement, settle, setMode, resume, zoom, measure, screen,
    cancel: () => { movementIntent = null; settle(); },
    // Read-only presentation diagnostics are useful for focused tests and inspection.
    inspect: () => ({ preferences: { ...preferences }, target: target && { ...target }, current: current(), suspended, side, activationKey, fittedSpan, transitionActive }) };
}
