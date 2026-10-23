// Concrete authoring data plus temporary UI identity. No Delve legality is evaluated here.
export class DesignerDraft {
  constructor(catalog) {
    this.catalog = catalog;
    this.nextKey = 0;
    this.revision = 0;
    this.replace({ board: { width: 15, height: 15, defaultTerrain: "StoneFloor", cells: [], edges: [] },
      unitTypeIds: [], units: [], agency: [] });
  }
  replace(definition) {
    if (!this.dimensionsAllowed(definition.board.width, definition.board.height))
      throw new Error(`Designer boards must be 1..${this.catalog.maxBoardSize} cells in each dimension.`);
    this.definition = structuredClone(definition);
    this.unitKeys = this.definition.units.map(() => ++this.nextKey);
    this.machineKeys = (this.definition.machines ?? []).map(() => ++this.nextKey);
    this.changed();
  }
  setWorldEffects(enabled, cardsPerRound, cycling) {
    // Drafts remain editable; the engine validates these scenario values.
    if (enabled) this.definition.worldEffects = { cardsPerRound, cycling };
    else delete this.definition.worldEffects;
    this.changed();
  }
  changed() { this.revision++; }
  snapshot() { return structuredClone(this.definition); }
  metadata(id) { return this.catalog.unitTypes.find(type => type.unitTypeId === id); }
  occupied(unit) {
    const metadata = this.metadata(unit.unitTypeId);
    // Unknown imported content remains recoverable/selectable at its anchor. Geometry is supplied by the catalog.
    if (!metadata) return [{ ...unit.anchor }];
    return metadata.footprintOffsets
      .map(offset => ({ x: unit.anchor.x + offset.x, y: unit.anchor.y + offset.y }));
  }
  unitsAt(cell) {
    return this.definition.units.flatMap((unit, index) =>
      this.occupied(unit).some(position => sameCell(position, cell)) ? [this.unitKeys[index]] : []);
  }
  unitIndex(key) { return this.unitKeys.indexOf(key); }
  dimensionsAllowed(width, height) {
    return [width, height].every(value => Number.isInteger(value) && value >= 1 && value <= this.catalog.maxBoardSize);
  }
  resize(width, height) {
    if (!this.dimensionsAllowed(width, height)) throw new Error(`Dimensions must be 1..${this.catalog.maxBoardSize}.`);
    const board = this.definition.board;
    // Only shrinking needs a data-loss guard; an already invalid draft can still be enlarged.
    const hidden = cell => (width < board.width && cell.x >= width) || (height < board.height && cell.y >= height);
    if (board.cells.some(cell => hidden(cell.position)) || board.edges.some(edge =>
      hidden(edge.position) || hidden({ x: edge.position.x + (edge.direction === "Right" ? 1 : 0),
        y: edge.position.y + (edge.direction === "Down" ? 1 : 0) })) ||
      this.definition.units.some(unit => this.occupied(unit).some(hidden)) ||
      (this.definition.cellMarkers ?? []).some(marker => hidden(marker.position)) ||
      (this.definition.resources ?? []).some(resource => hidden(resource.position)) ||
      (this.definition.waypoints ?? []).some(w => hidden({ x: w.area.x + w.area.w - 1, y: w.area.y + w.area.h - 1 })) ||
      (this.definition.machines ?? []).some(machine => hidden(machine.position)))
      throw new Error("Shrinking would hide authored terrain, edges, Units, Resources, Cell Markers or Machines. Remove or reposition them first.");
    board.width = width; board.height = height; this.changed();
  }
  terrainAt(cell) {
    return this.definition.board.cells.find(entry => sameCell(entry.position, cell))?.terrain ?? this.definition.board.defaultTerrain;
  }
  railDirectionAt(cell) {
    return this.definition.board.cells.find(entry => sameCell(entry.position, cell))?.direction;
  }
  setNeutral(side, neutral) {
    const sides = (this.definition.neutralSides ?? []).filter(value => value !== side);
    if (neutral) sides.push(side);
    this.definition.neutralSides = sides; this.changed();
  }
  setObjective(side, objective) {
    const objectives = (this.definition.objectives ?? []).filter(value => value.sideId !== side);
    if (objective) objectives.push({ sideId: side, objective });
    this.definition.objectives = objectives; this.changed();
  }
  paintTerrain(position, terrain, direction = "East") {
    upsert(this.definition.board.cells, entry => sameCell(entry.position, position),
      terrain === null ? null : { position: { ...position }, terrain, ...(terrain === "Rail" ? { direction } : {}) });
    this.changed();
  }
  paintEdge(position, direction, kind) {
    const board = this.definition.board;
    if (position.x < 0 || position.y < 0 || position.x >= board.width || position.y >= board.height ||
      !["Right", "Down"].includes(direction) || direction === "Right" && position.x >= board.width - 1 ||
      direction === "Down" && position.y >= board.height - 1) return;
    upsert(board.edges, entry => sameCell(entry.position, position) && entry.direction === direction,
      kind === null ? null : { position: { ...position }, direction, kind });
    this.changed();
  }
  declare(id) {
    if (!this.definition.unitTypeIds.includes(id)) this.definition.unitTypeIds.push(id);
  }
  setCellMarker(position, type) {
    const markers = this.definition.cellMarkers ??= [];
    upsert(markers, marker => sameCell(marker.position, position),
      type === null ? null : { type: structuredClone(type), position: { ...position } });
    this.changed();
  }
  placeMachine(typeId, position) {
    const type = this.catalog.machineTypes.find(type => type.id === typeId);
    if (!type) throw new Error("Select a Machine Type from the catalog.");
    const machines = this.definition.machines ??= [];
    let number = 1;
    const used = new Set([...machines.map(machine => machine.id),
      ...this.definition.units.map(unit => unit.unitId)]);
    while (used.has(`machine-${number}`)) number++;
    machines.push({ id: `machine-${number}`, position: { ...position }, type: structuredClone(type), ownerSide: null, storedResources: [] });
    const key = ++this.nextKey; this.machineKeys.push(key); this.changed(); return key;
  }
  editMachine(key, changes) {
    const index = this.machineKeys.indexOf(key);
    if (index < 0) return;
    this.definition.machines[index] = { ...this.definition.machines[index], ...changes }; this.changed();
  }
  removeMachine(key) {
    const index = this.machineKeys.indexOf(key);
    if (index < 0) return;
    this.definition.machines.splice(index, 1); this.machineKeys.splice(index, 1); this.changed();
  }
  ensureAgency(unitTypeId, sideId, controller) {
    this.declare(unitTypeId);
    if (!this.definition.agency.some(row => row.unitTypeId === unitTypeId && row.sideId === sideId))
      this.definition.agency.push({ unitTypeId, sideId, controller });
  }
  place(unitTypeId, sideId, controller, anchor) {
    this.ensureAgency(unitTypeId, sideId, controller);
    this.definition.units.push({ unitTypeId, sideId, anchor: { ...anchor }, posture: "Upright", initialHp: null });
    const key = ++this.nextKey; this.unitKeys.push(key); this.changed(); return key;
  }
  editUnit(key, changes, controller) {
    const index = this.unitIndex(key);
    if (index < 0) return;
    const unit = { ...this.definition.units[index], ...changes };
    this.ensureAgency(unit.unitTypeId, unit.sideId, controller);
    this.definition.units[index] = unit; this.changed();
  }
  removeUnit(key) {
    const index = this.unitIndex(key);
    if (index < 0) return;
    this.definition.units.splice(index, 1); this.unitKeys.splice(index, 1); this.changed();
  }
  setAgency(unitTypeId, sideId, controller) {
    this.declare(unitTypeId);
    upsert(this.definition.agency, row => row.unitTypeId === unitTypeId && row.sideId === sideId,
      { unitTypeId, sideId, controller });
    this.changed();
  }
  setOrder(unitTypeId, sideId, order) {
    if (order.kind === "FollowRoute" && !(this.definition.routes ?? []).some(r => r.id === order.route?.routeId))
      throw new Error("Select an existing Route first.");
    const orders = this.definition.orders ?? [];
    upsert(orders, row => row.unitTypeId === unitTypeId && row.sideId === sideId,
      order.kind === "Normal" ? null : { unitTypeId, sideId, ...order });
    if (orders.length) this.definition.orders = orders;
    else delete this.definition.orders;
    this.changed();
  }
  editAgency(index, assignment, order) {
    // Check before touching declarations, the previous pair or its Order.
    if (order.kind === "FollowRoute" && !(this.definition.routes ?? []).some(r => r.id === order.route?.routeId))
      throw new Error("Select an existing Route first.");
    const previous = this.definition.agency[index];
    this.setOrder(previous.unitTypeId, previous.sideId, { kind: "Normal" });
    this.declare(assignment.unitTypeId);
    this.definition.agency[index] = assignment;
    this.setOrder(assignment.unitTypeId, assignment.sideId, order);
  }
  removeAgency(index) {
    const row = this.definition.agency[index];
    this.setOrder(row.unitTypeId, row.sideId, { kind: "Normal" });
    this.definition.agency.splice(index, 1); this.changed();
  }
  saveWaypoint(previousId, waypoint) {
    const rows = this.definition.waypoints ?? [];
    if (!waypoint.id.trim() || rows.some(w => w.id === waypoint.id && w.id !== previousId)) throw new Error("Waypoint ID must be unique and nonempty.");
    this.definition.waypoints = rows;
    const index = rows.findIndex(w => w.id === previousId);
    if (index < 0) rows.push(structuredClone(waypoint)); else rows[index] = structuredClone(waypoint);
    for (const route of this.definition.routes ?? []) route.waypointIds = route.waypointIds.map(id => id === previousId ? waypoint.id : id);
    this.changed();
  }
  removeWaypoint(id) {
    if ((this.definition.routes ?? []).some(r => r.waypointIds.includes(id))) throw new Error("Remove this Waypoint from its Routes before deleting it.");
    this.definition.waypoints = (this.definition.waypoints ?? []).filter(w => w.id !== id); this.changed();
  }
  saveRoute(previousId, route, resetProgress = false) {
    const rows = this.definition.routes ?? [];
    if (!route.id.trim() || rows.some(r => r.id === route.id && r.id !== previousId)) throw new Error("Route ID must be unique and nonempty.");
    if (!route.waypointIds.length || route.waypointIds.some(id => !(this.definition.waypoints ?? []).some(w => w.id === id)))
      throw new Error("A Route needs existing Waypoints in order.");
    const index = rows.findIndex(r => r.id === previousId);
    const previous = rows[index];
    const structuralChange = previous && (previous.mode !== route.mode || JSON.stringify(previous.waypointIds) !== JSON.stringify(route.waypointIds));
    const assigned = previous ? (this.definition.orders ?? []).filter(order => order.route?.routeId === previousId) : [];
    // A sequence edit is meaningful for a group while its current slot still targets the same Waypoint.
    const needsReset = assigned.filter(order => structuralChange && (previous.mode !== route.mode ||
      !route.waypointIds[order.route.currentIndex] || route.waypointIds[order.route.currentIndex] !== previous.waypointIds[order.route.currentIndex]));
    if (needsReset.length && !resetProgress)
      throw new Error(`This changes the current target or traversal mode for ${needsReset.length} assigned group(s). Choose ‘Reset affected group progress’ to start those groups at waypoint 0. Other groups retain progress.`);
    this.definition.routes = rows;
    if (index < 0) rows.push(structuredClone(route)); else rows[index] = structuredClone(route);
    for (const order of assigned) order.route = needsReset.includes(order)
      ? { routeId: route.id, currentIndex: 0, direction: 1 }
      : { ...order.route, routeId: route.id };
    this.changed();
  }
  removeRoute(id) {
    if ((this.definition.orders ?? []).some(o => o.route?.routeId === id)) throw new Error("Unassign this Route from its groups before deleting it.");
    this.definition.routes = (this.definition.routes ?? []).filter(r => r.id !== id); this.changed();
  }
}

export const sameCell = (a, b) => a.x === b.x && a.y === b.y;
function upsert(entries, matches, value) {
  const index = entries.findIndex(matches);
  if (value === null) { if (index >= 0) entries.splice(index, 1); }
  else if (index >= 0) entries[index] = value;
  else entries.push(value);
}

// Responses apply only to the exact draft/request that produced them, including import.
export class DesignerRequests {
  constructor(draft, request, report) { this.draft = draft; this.request = request; this.report = report; this.sequence = 0; }
  async run(operation, body) {
    const revision = this.draft.revision, sequence = ++this.sequence;
    try {
      const result = await this.request(operation, body);
      if (revision !== this.draft.revision || sequence !== this.sequence) return null;
      this.report(result.errors ? result : { valid: true, errors: [] }, operation);
      return result;
    } catch (error) {
      if (revision !== this.draft.revision || sequence !== this.sequence) return null;
      this.report({ valid: false, errors: error.errors ?? [{ message: error.message }] }, operation);
      return null;
    }
  }
}

// Authoring history includes temporary identities, never selection or viewport state.
export class DesignerHistory {
  constructor(draft) { this.draft = draft; this.past = []; this.future = []; this.start = null; }
  capture() {
    return { definition: this.draft.snapshot(), unitKeys: [...this.draft.unitKeys], machineKeys: [...this.draft.machineKeys], nextKey: this.draft.nextKey };
  }
  restore(state) { Object.assign(this.draft, structuredClone(state)); this.draft.changed(); }
  begin() { if (!this.start) { this.start = this.capture(); this.startRevision = this.draft.revision; } }
  finish() {
    if (!this.start) return false;
    const changed = JSON.stringify(this.start.definition) !== JSON.stringify(this.draft.definition);
    if (changed) { this.past.push(this.start); if (this.past.length > 100) this.past.shift(); this.future = []; }
    this.start = null; return changed;
  }
  cancel() {
    if (!this.start) return;
    Object.assign(this.draft, structuredClone(this.start)); this.draft.revision = this.startRevision; this.start = null;
  }
  transact(action) { this.begin(); try { action(); return this.finish(); } catch (error) { this.cancel(); throw error; } }
  undo() { this.finish(); return this.move(this.past, this.future); }
  redo() { this.finish(); return this.move(this.future, this.past); }
  move(from, to) {
    if (!from.length) return false;
    to.push(this.capture()); this.restore(from.pop()); return true;
  }
}

export const draftStorageKey = "project-delve.designer.v1";
export function draftDocument(definition, title = "Untitled scenario", buffers = {}) {
  return { format: "ProjectDelveDraft", version: 1, title, savedAt: new Date().toISOString(), definition: structuredClone(definition), buffers: structuredClone(buffers) };
}
export function readDraftDocument(text, maxBoardSize = 50) {
  const doc = JSON.parse(text);
  if (doc?.format !== "ProjectDelveDraft" || doc.version !== 1) throw new Error("Unsupported draft document. Expected Project Delve draft version 1.");
  if (doc.transportInput != null && typeof doc.transportInput !== "string") throw new Error("Invalid saved import text.");
  const d = doc.definition, board = d?.board;
  // Shape and rendering limits only. Incomplete/illegal game content is the engine's concern.
  if (!board || ![board.width, board.height].every(n => Number.isInteger(n) && n >= 1 && n <= maxBoardSize) ||
      ![board.cells, board.edges, d.units, d.unitTypeIds, d.agency].every(Array.isArray) ||
      ["orders", "routes", "waypoints", "machines", "resources", "cellMarkers", "objectives", "neutralSides", "unitTypes", "storedResources"].some(key => d[key] != null && !Array.isArray(d[key])) ||
      d.units.some(u => !u || typeof u.unitTypeId !== "string" || !u.anchor || !Number.isFinite(u.anchor.x) || !Number.isFinite(u.anchor.y)) ||
      board.cells.some(c => !c?.position) || board.edges.some(e => !e?.position) ||
      (d.waypoints ?? []).some(w => !w?.area) || (d.routes ?? []).some(r => !Array.isArray(r?.waypointIds)) ||
      (d.machines ?? []).some(m => !m?.position || !m.type?.storage?.capacities))
    throw new Error("The saved draft is damaged or has an unsupported structure. The current draft was retained.");
  if (typeof board.defaultTerrain !== "string" ||
      d.agency.some(a => !a || typeof a.unitTypeId !== "string" || typeof a.sideId !== "string") ||
      (d.orders ?? []).some(o => !o || typeof o.kind !== "string") ||
      (d.objectives ?? []).some(o => !o || typeof o.sideId !== "string") ||
      (d.routes ?? []).some(r => typeof r.id !== "string") || (d.waypoints ?? []).some(w => typeof w.id !== "string") ||
      [...(d.cellMarkers ?? []), ...(d.resources ?? [])].some(m => !m?.position || !m.type || typeof m.type.id !== "string") ||
      (d.machines ?? []).some(m => typeof m.id !== "string" || !Array.isArray(m.type.storage.capacities) ||
        m.type.storage.capacities.some(c => !c?.type) || m.storedResources != null && !Array.isArray(m.storedResources)))
    throw new Error("The saved draft contains damaged authoring data. The current draft was retained.");
  if (doc.buffers != null && (typeof doc.buffers !== "object" || Array.isArray(doc.buffers))) throw new Error("Invalid saved form data.");
  if (Object.values(doc.buffers ?? {}).some(buffer => !buffer || typeof buffer !== "object" ||
      buffer.values != null && (typeof buffer.values !== "object" || Array.isArray(buffer.values)) ||
      buffer.sequence != null && (!Array.isArray(buffer.sequence) || buffer.sequence.some(id => typeof id !== "string"))))
    throw new Error("Invalid saved form data.");
  const keys = doc.editorKeys;
  if (keys && (!Array.isArray(keys.unitKeys) || !Array.isArray(keys.machineKeys) || keys.unitKeys.length !== d.units.length ||
      keys.machineKeys.length !== (d.machines ?? []).length || !Number.isInteger(keys.nextKey) ||
      [...keys.unitKeys, ...keys.machineKeys].some(n => !Number.isInteger(n) || n < 1 || n > keys.nextKey) ||
      new Set([...keys.unitKeys, ...keys.machineKeys]).size !== keys.unitKeys.length + keys.machineKeys.length))
    throw new Error("Invalid saved editor identities.");
  return doc;
}
