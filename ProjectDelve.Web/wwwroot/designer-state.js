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
    if (!metadata) throw new Error(`No authoritative geometry for ${unit.unitTypeId}. Reload the catalog.`);
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
    const orders = this.definition.orders ?? [];
    upsert(orders, row => row.unitTypeId === unitTypeId && row.sideId === sideId,
      order.kind === "Normal" ? null : { unitTypeId, sideId, ...order });
    if (orders.length) this.definition.orders = orders;
    else delete this.definition.orders;
    this.changed();
  }
  editAgency(index, assignment, order) {
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
