// Authoring projection only: positions and verdicts come from the server.
export function setupOverlayCells(setup, width, height) {
  const result = [];
  const add = (positions, kind, label, locationId, groupId) => {
    for (const position of Array.isArray(positions) ? positions : []) {
      if (!Number.isInteger(position?.x) || !Number.isInteger(position?.y) ||
          position.x < 0 || position.y < 0 || position.x >= width || position.y >= height) continue;
      result.push({ ...position, kind, label, locationId, groupId });
    }
  };
  add(setup?.deployment?.cells, 'deployment', 'Hero deployment');
  add(setup?.deployment?.maneuverCore, 'core', 'Maneuver core');
  add(setup?.deployment?.departure ? [setup.deployment.departure] : [], 'departure', 'Deployment departure');
  for (const location of Array.isArray(setup?.requiredLocations) ? setup.requiredLocations : [])
    for (const group of Array.isArray(location?.groups) ? location.groups : []) {
      if (!group) continue;
      add(group?.positions, location.accessClass === 'DeliberateSpecial' ? 'special' : 'ordinary',
        `${location.accessClass} · ${location.id} · ${group.id} · ${group.coverage}`, location.id, group.id);
    }
  return result;
}

export function renderSetupOverlay(root, setup, width, height) {
  for (const cell of setupOverlayCells(setup, width, height)) {
    const node = document.createElement('span');
    node.className = `setup-cell setup-${cell.kind}`; node.title = cell.label;
    node.dataset.setupKind = cell.kind;
    if (cell.locationId != null) node.dataset.locationId = cell.locationId;
    if (cell.groupId != null) node.dataset.groupId = cell.groupId;
    Object.assign(node.style, { left: `${cell.x / width * 100}%`, top: `${cell.y / height * 100}%`,
      width: `${100 / width}%`, height: `${100 / height}%` });
    root.append(node);
  }
}

export function setupStatusText(setup, result) {
  if (!result) return setup ? 'PlayReady1 unchecked · Validate to check this revision.' : 'No PlayReady1 setup declaration.';
  if (result.status === 'Legacy') return 'Legacy · no PlayReady1 contract.';
  return result.valid ? 'PlayReady1 valid · checked by the server for this revision.' : 'PlayReady1 invalid · see setup diagnostics.';
}

export function renderSetupInspector(root, setup, result) {
  root.replaceChildren();
  const line = text => { const node = document.createElement('p'); node.textContent = text; root.append(node); };
  const positions = value => (Array.isArray(value) ? value : []).map(p => p && typeof p === 'object' ? `(${p.x}, ${p.y})` : '(invalid position)').join(', ');
  if (setup?.deployment) {
    const d = setup.deployment;
    line(`Setup v${setup.version} · capacity ${d.capacity} · deployment ${(d.cells ?? []).length} Cells (minimum ${d.minimumArea}) · maneuver core ${(d.maneuverCore ?? []).length} Cells (minimum ${d.minimumCore}) · width ${d.maneuverWidth}`);
    line(`Placement order: ${positions(d.placementOrder)}`);
  }
  for (const diagnostic of result?.diagnostics ?? [])
    line(`${diagnostic.path ?? 'setup'}: ${diagnostic.message}${diagnostic.locationId ? ` · location ${diagnostic.locationId}` : ''}${diagnostic.groupId ? ` · group ${diagnostic.groupId}` : ''}${diagnostic.positions?.length ? ` · ${diagnostic.positions.map(p => `(${p.x}, ${p.y})`).join(', ')}` : ''}`);
  for (const location of Array.isArray(setup?.requiredLocations) ? setup.requiredLocations : []) {
    if (!location) { line('Invalid required-location declaration · Validate for diagnostics.'); continue; }
    line(`${location.id} · ${location.accessClass} · producer ${location.producerId} · source ${location.sourceId}${location.specialReason ? ` · ${location.specialReason}` : ''}`);
    for (const group of Array.isArray(location?.groups) ? location.groups : [])
      line(group ? `Group ${group.id} · ${group.coverage} · ${positions(group.positions)}` : 'Invalid access group · Validate for diagnostics.');
  }
}

export function showFinalSetup(preview, selectedStage, compareBefore, selectedOperation) {
  return !selectedOperation && !compareBefore && (!preview?.frames?.length || selectedStage === preview.frames.length - 1);
}
