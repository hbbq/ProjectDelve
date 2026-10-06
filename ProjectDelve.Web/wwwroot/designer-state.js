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
      this.definition.units.some(unit => this.occupied(unit).some(hidden)))
      throw new Error("Shrinking would hide authored terrain, edges or Units. Remove or reposition them first.");
    board.width = width; board.height = height; this.changed();
  }
  terrainAt(cell) {
    return this.definition.board.cells.find(entry => sameCell(entry.position, cell))?.terrain ?? this.definition.board.defaultTerrain;
  }
  paintTerrain(position, terrain) {
    upsert(this.definition.board.cells, entry => sameCell(entry.position, position),
      terrain === null ? null : { position: { ...position }, terrain });
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
  removeAgency(index) { this.definition.agency.splice(index, 1); this.changed(); }
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
