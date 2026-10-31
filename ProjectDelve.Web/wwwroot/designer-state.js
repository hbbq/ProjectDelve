// Concrete authoring data plus temporary UI identity. No Delve legality is evaluated here.
export class DesignerDraft {
  constructor(catalog) {
    this.catalog = catalog;
    this.nextKey = 0;
    this.revision = 0;
    this.replace({ board: { width: 15, height: 15, defaultTerrain: "StoneFloor", cells: [], edges: [] },
      unitTypeIds: [], units: [], agency: [], loot: { preset: catalog.defaultLootPreset ?? "Classic" } });
  }
  replace(definition) {
    if (!this.dimensionsAllowed(definition.board.width, definition.board.height))
      throw new Error(`Designer boards must be 1..${this.catalog.maxBoardSize} cells in each dimension.`);
    this.definition = structuredClone(definition);
    this.effectiveTerrainMetadata = null;
    this.effectiveFixedComponentMetadata = null;
    this.effectiveProductionDescriptions = null;
    this.unitKeys = this.definition.units.map(() => ++this.nextKey);
    this.machineKeys = (this.definition.machines ?? []).map(() => ++this.nextKey);
    this.changed();
  }
  setWorldEffects(enabled, cardsPerRound, cycling) {
    // Drafts remain editable; the engine validates these scenario values.
    if (enabled) this.definition.worldEffects = { ...this.definition.worldEffects, cardsPerRound, cycling };
    else delete this.definition.worldEffects;
    this.changed();
  }
  lootComposition() {
    if (!this.definition.loot) return [];
    if (this.definition.loot.cards) {
      const counts = new Map();
      for (const kind of this.definition.loot.cards) counts.set(kind, (counts.get(kind) ?? 0) + 1);
      return [...counts].map(([kind,count]) => ({kind,count}));
    }
    if (this.definition.loot.composition) return this.definition.loot.composition;
    const preset = this.lootPreset();
    if (!this.lootCompositionReady()) return [];
    const rows = preset?.isDynamic && (this.definition.lootCardDefinitions?.length ?? 0) > 0
      ? this.effectiveLootPresets?.find(p => p.id === preset.id)?.composition ?? [] : preset?.composition ?? [];
    return rows.map(r => ({ ...r, count: r.count * (this.definition.loot.presetMultiplier ?? 1) }));
  }
  lootPreset() {
    return this.catalog.lootPresets?.find(p => p.id === (this.definition.loot?.preset ?? this.catalog.defaultLootPreset ?? "Classic"));
  }
  lootCompositionReady() {
    return !this.lootPreset()?.isDynamic || !(this.definition.lootCardDefinitions?.length) ||
      this.effectiveLootSignature === JSON.stringify(this.definition.lootCardDefinitions ?? []) && Array.isArray(this.effectiveLootPresets);
  }
  setLootPreset(preset) {
    if (!this.catalog.lootPresets?.some(p => p.id === preset)) throw new Error("Unknown Loot preset.");
    this.definition.loot = { preset }; this.changed();
  }
  copyLootToCustom() {
    if (!this.lootCompositionReady()) throw new Error("Wait for the effective Loot catalog before copying this preset.");
    this.definition.loot = { composition: structuredClone(this.lootComposition()) }; this.changed();
  }
  setLootMultiplier(value) {
    if (!Number.isInteger(value) || value < 1 || value > 100) throw new Error("Use a whole deck multiplier from 1 to 100.");
    if (!this.definition.loot || this.definition.loot.composition || this.definition.loot.cards) throw new Error("Choose a live preset first.");
    if (this.lootPreset()?.isDynamic && value !== 1) throw new Error("All contains exactly one copy of each available Loot Card Type.");
    this.definition.loot.presetMultiplier = value; this.changed();
  }
  setLootDeck(rows) {
    if (rows.some(r => !Number.isInteger(r.count) || r.count < 0 || r.count > 1000)) throw new Error("Use 0-1000 copies per card.");
    this.definition.loot = { composition: rows.filter(r => r.count > 0).map(r => ({ ...this.objectReference(r), count:r.count })) }; this.changed();
  }
  objectReference(object) {
    for (const field of ['lootCardId', 'itemTypeId', 'equipmentTypeId']) if (object[field] != null) return {[field]:object[field]};
    return {kind:object.kind};
  }
  referenceKey(object) {
    const [field,value] = Object.entries(this.objectReference(object))[0];
    return `${field}:${value}`;
  }
  lootDefinitionCatalog() {
    const local = this.definition.lootCardDefinitions ?? [];
    const localIds = new Set(local.map(card => card.id));
    // Older scenarios could use IDs that were later added to canonical Loot.
    // Explicit authored references keep their local meaning; legacy kind choices
    // still use the canonical compatibility catalog. Runtime resolution is Engine-owned.
    return [...(this.catalog.lootCardDefinitions ?? []).filter(card => !localIds.has(card.id)), ...local];
  }
  itemCatalog() {
    return [...(this.catalog.itemTypes ?? []), ...(this.definition.itemTypes ?? [])];
  }
  equipmentCatalog() {
    return [...(this.catalog.equipmentTypes ?? []), ...(this.definition.equipmentTypes ?? [])];
  }
  applyContentDefinition(family, definition, existingId = null) {
    const field = family === 'equipment' ? 'equipmentTypes' : family === 'lootCards' ? 'lootCardDefinitions' : null;
    if (!field) throw new Error('Only Equipment and Loot Card definitions can be saved here.');
    if (!definition || typeof definition !== 'object' || Array.isArray(definition) ||
        !Object.hasOwn(definition, 'id') || typeof definition.id !== 'string' || !definition.id.trim())
      throw new Error('A content definition requires a nonempty ID.');
    if (existingId !== null && (typeof existingId !== 'string' || !existingId.trim()))
      throw new Error('An existing content definition requires its original ID.');
    if (existingId !== null && definition.id !== existingId)
      throw new Error('An existing content definition ID cannot change. Copy it to a new definition instead.');
    const rows = this.definition[field] ?? [];
    if (!Array.isArray(rows)) throw new Error('The local content collection must be an array.');
    const matches = rows.flatMap((row, index) => row?.id === definition.id ? [index] : []);
    if (matches.length > 1 || existingId === null && matches.length)
      throw new Error(`A local content definition already uses ID '${definition.id}'.`);
    if (existingId !== null && !matches.length)
      throw new Error(`The local content definition '${existingId}' no longer exists.`);
    // Engine validation owns content rules. This operation only applies an isolated
    // payload to the intended local identity; the caller supplies its history boundary.
    const saved = structuredClone(definition), updated = [...rows];
    if (existingId === null) updated.push(saved); else updated[matches[0]] = saved;
    this.definition[field] = updated;
    this.changed();
  }
  lootCardCatalog() {
    return [...(this.catalog.lootCards ?? []).map(c => ({ ...c, reference: {kind:c.kind}, key:`card:${c.kind}` })),
      ...this.equipmentCatalog().map(c => ({ name:c.name, category:'Equipment',
        equipmentTypeId:c.id, reference:{equipmentTypeId:c.id}, key:`equipment:${c.id}` })),
      ...this.itemCatalog().map(c => ({ name:c.name, category:'Item', itemTypeId:c.id, reference:{itemTypeId:c.id}, key:`item:${c.id}` })),
      ...this.lootDefinitionCatalog().map(c => ({ name:c.name, reference:{lootCardId:c.id}, key:`loot:${c.id}` }))];
  }
  objectCatalog() {
    return [...(this.itemCatalog().length ? [] : (this.catalog.lootCards ?? []).filter(c => c.category === 'Item').map(c => ({ ...c, reference:{kind:c.kind}, key:`card:${c.kind}` }))),
      ...this.itemCatalog().map(c => ({ name:c.name, category:'Item', itemTypeId:c.id, reference:{itemTypeId:c.id}, key:`item:${c.id}` })),
      ...this.equipmentCatalog().map(c => ({ name:c.name, category:'Equipment', equipmentTypeId:c.id,
        reference:{equipmentTypeId:c.id}, key:`equipment:${c.id}` }))];
  }
  objectName(object) {
    if (object.lootCardId != null) return this.lootDefinitionCatalog().find(c => c.id === object.lootCardId)?.name ?? object.lootCardId;
    if (object.itemTypeId != null) return this.itemCatalog().find(c => c.id === object.itemTypeId)?.name ?? object.itemTypeId;
    return object.equipmentTypeId != null ? this.equipmentCatalog().find(c => c.id === object.equipmentTypeId)?.name ?? object.equipmentTypeId
      : this.catalog.lootCards?.find(c => c.kind === object.kind)?.name ?? object.kind;
  }
  configureLootType(id, dropSuccesses, canHaveItems, canHaveEquipment) {
    if (dropSuccesses != null && (!Number.isInteger(dropSuccesses) || dropSuccesses < 0 || dropSuccesses > 6)) throw new Error("Use 0-6 drop successes or the Unit Type default.");
    const values = Object.fromEntries(Object.entries({dropSuccesses, canHaveItems, canHaveEquipment}).filter(([,v]) => v != null));
    const overrides = this.definition.lootUnitOverrides ??= {};
    if (Object.keys(values).length) overrides[id] = values; else delete overrides[id];
    if (!Object.keys(overrides).length) delete this.definition.lootUnitOverrides;
    this.changed();
  }
  objectIdentity(prefix) {
    const ids = new Set([...(this.definition.lootMarkers ?? []).map(m => m.id),
      ...(this.definition.groundObjects ?? []).map(g => g.object.id), ...this.definition.units.flatMap(u => (u.inventory ?? []).map(o => o.id))]);
    let number = 1; while (ids.has(`${prefix}-${number}`)) number++;
    return `${prefix}-${number}`;
  }
  setLootMarkers(position, count) {
    if (!Number.isInteger(count) || count < 0 || count > 1000) throw new Error("Use 0–1000 markers per Cell.");
    const markers = this.definition.lootMarkers ??= [];
    const existing = markers.filter(m => sameCell(m.position, position));
    for (const marker of existing.slice(count)) markers.splice(markers.indexOf(marker), 1);
    for (let i = existing.length; i < count; i++) markers.push({ id: this.objectIdentity('authored-loot'), position: { ...position } });
    this.changed();
  }
  addObject(kind, { unitKey, position }) {
    const reference = typeof kind === 'string' ? {kind} : structuredClone(kind);
    const object = { id: this.objectIdentity('authored-object'), ...reference };
    if (unitKey != null) (this.definition.units[this.unitIndex(unitKey)].inventory ??= []).push(object);
    else (this.definition.groundObjects ??= []).push({ object, position: { ...position } });
    this.changed();
  }
  removeObject(id) {
    for (const unit of this.definition.units) if (unit.inventory) unit.inventory = unit.inventory.filter(o => o.id !== id);
    if (this.definition.groundObjects) this.definition.groundObjects = this.definition.groundObjects.filter(g => g.object.id !== id);
    this.changed();
  }
  changed() { this.revision++; }
  snapshot() { return structuredClone(this.definition); }
  metadata(id) {
    if ((this.definition.unitTypes ?? []).some(type => type.id === id))
      return this.effectiveMetadata?.find(type => type.unitTypeId === id);
    return this.catalog.unitTypes.find(type => type.unitTypeId === id);
  }
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
      (this.definition.lootMarkers ?? []).some(marker => hidden(marker.position)) ||
      (this.definition.groundObjects ?? []).some(object => hidden(object.position)) ||
      (this.definition.resources ?? []).some(resource => hidden(resource.position)) ||
      (this.definition.waypoints ?? []).some(w => hidden({ x: w.area.x + w.area.w - 1, y: w.area.y + w.area.h - 1 })) ||
      (this.definition.objectives ?? []).some(o => o.area && hidden({ x: o.area.x + o.area.w - 1, y: o.area.y + o.area.h - 1 })) ||
      (this.definition.machines ?? []).some(machine => hidden(machine.position)))
      throw new Error("Shrinking would hide authored terrain, edges, Units, Resources, Cell Markers or Machines. Remove or reposition them first.");
    board.width = width; board.height = height; this.changed();
  }
  terrainAt(cell) {
    const entry = this.definition.board.cells.find(entry => sameCell(entry.position, cell));
    return entry ? entry.terrainTypeId ?? entry.terrain : this.definition.board.defaultTerrainTypeId ?? this.definition.board.defaultTerrain;
  }
  terrainCatalog() {
    return this.effectiveTerrainMetadata ?? [...(this.catalog.terrainDefinitions ?? []), ...(this.definition.terrainTypes ?? []), ...(this.definition.terrainAssemblies ?? [])];
  }
  fixedComponentCatalog() {
    return this.effectiveFixedComponentMetadata ?? [...(this.catalog.fixedComponentDefinitions ?? []), ...(this.definition.fixedComponentTypes ?? [])];
  }
  terrainTypeSelection(id) { return `type:${encodeURIComponent(id)}`; }
  defaultTerrainSelection() {
    return this.definition.board.defaultTerrainTypeId != null ? this.terrainTypeSelection(this.definition.board.defaultTerrainTypeId) : this.definition.board.defaultTerrain;
  }
  terrainSelectionAt(cell) {
    const entry = this.definition.board.cells.find(entry => sameCell(entry.position, cell));
    return entry ? entry.terrainTypeId != null ? this.terrainTypeSelection(entry.terrainTypeId) : entry.terrain : this.defaultTerrainSelection();
  }
  legacyTerrainKind(reference) {
    return reference?.startsWith('type:') ? this.catalog.legacyTerrainTypeAliases?.[decodeURIComponent(reference.slice(5))]
      : this.catalog.terrains.includes(reference) ? reference : null;
  }
  terrainMetadata(reference, coalGeneration = null) {
    const legacy = this.legacyTerrainKind(reference);
    const mappings = (this.catalog.legacyTerrainMappings ?? []).filter(mapping => mapping.kind === legacy);
    const mapping = mappings.find(mapping => mapping.coalGeneration === coalGeneration)
      ?? mappings.find(mapping => mapping.coalGeneration == null);
    if (mapping) return this.terrainCatalog().find(type => type.id === mapping.terrainTypeId);
    if (reference?.startsWith('type:')) return this.terrainCatalog().find(type => type.id === decodeURIComponent(reference.slice(5)));
    return this.terrainCatalog().find(type => type.legacyKind === reference) ?? this.terrainCatalog().find(type => type.id === reference);
  }
  terrainDefinitionAt(cell) {
    const entry = this.definition.board.cells.find(entry => sameCell(entry.position, cell));
    return this.terrainMetadata(this.terrainSelectionAt(cell), entry?.coalGeneration);
  }
  productionDescriptionAt(cell) {
    const definition = this.terrainDefinitionAt(cell);
    return (this.effectiveProductionDescriptions ?? this.catalog.productionDescriptions)?.[definition?.id];
  }
  terrainNameAt(cell) {
    return this.terrainDefinitionAt(cell)?.name ?? this.terrainAt(cell);
  }
  terrainRecipeAt(cell) {
    const reference = this.terrainSelectionAt(cell), definition = this.terrainDefinitionAt(cell);
    const recipe = (definition?.legacyKind ? definition.presentation?.recipe : this.terrainSurfaceAt(cell)?.presentation?.recipe)
      ?? definition?.presentation?.recipe ?? reference;
    return this.catalog.terrains.includes(recipe) ? recipe : 'custom-terrain';
  }
  terrainSurfaceAt(cell) {
    const definition = this.terrainDefinitionAt(cell);
    return definition?.assembly?.surface ?? (definition?.surfaceId ? this.terrainCatalog().find(type => type.id === definition.surfaceId) : null);
  }
  terrainComponentsAt(cell) {
    const definition = this.terrainDefinitionAt(cell);
    // Resolve presentation names only. Gameplay properties always come from Engine metadata.
    return (definition?.assembly?.components ?? definition?.components ?? []).map(component => {
      const content = component.definition ?? this.fixedComponentCatalog().find(type => type.id === component.componentId);
      return { slot: component.slot, componentId: content?.id ?? component.componentId,
        name: content?.name ?? component.componentId, quarterTurns: component.quarterTurns, presentation: content?.presentation };
    });
  }
  setDefaultTerrain(reference) {
    if (this.catalog.terrains.includes(reference)) {
      this.definition.board.defaultTerrain = reference;
      delete this.definition.board.defaultTerrainTypeId;
    } else {
      this.definition.board.defaultTerrain = 'StoneFloor';
      this.definition.board.defaultTerrainTypeId = reference.startsWith('type:') ? decodeURIComponent(reference.slice(5)) : reference;
    }
    this.changed();
  }
  railDirectionAt(cell) {
    return this.definition.board.cells.find(entry => sameCell(entry.position, cell))?.direction;
  }
  setNeutral(side, neutral) {
    const sides = (this.definition.neutralSides ?? []).filter(value => value !== side);
    if (neutral) sides.push(side);
    this.definition.neutralSides = sides; this.changed();
  }
  setFogOfWar(side, enabled) {
    const sides = (this.definition.fogOfWarSides ?? []).filter(value => value !== side);
    if (enabled) sides.push(side);
    if (sides.length) this.definition.fogOfWarSides = sides;
    else delete this.definition.fogOfWarSides;
    this.changed();
  }
  setObjective(side, objective) {
    const objectives = (this.definition.objectives ?? []).filter(value => value.sideId !== side);
    if (objective) objectives.push(typeof objective === "string" ? { sideId: side, objective } : { ...structuredClone(objective), sideId: side });
    this.definition.objectives = objectives; this.changed();
  }
  setResource(position, type) {
    const rows = this.definition.resources ??= [];
    if (type && rows.some(row => sameCell(row.position, position)))
      throw new Error("This Cell already has a Resource. Select it to move, change or remove it.");
    upsert(rows, row => sameCell(row.position, position), type ? { type: structuredClone(type), position: { ...position } } : null);
    this.changed();
  }
  moveResource(from, to) {
    if (sameCell(from, to)) return;
    const row = this.definition.resources?.find(row => sameCell(row.position, from));
    if (!row) throw new Error("Select a Resource first.");
    if (this.definition.resources.some(row => sameCell(row.position, to))) throw new Error("The destination already has a Resource.");
    row.position = { ...to }; this.changed();
  }
  setInventory(unitTypeId, sideId, quantities) {
    for (const { type, quantity } of quantities) {
      const capacity = this.metadata(unitTypeId)?.resourceStorage?.capacities.find(c => c.type.id === type.id)?.capacity;
      if (!Number.isInteger(quantity) || quantity < 0 || capacity == null || quantity > capacity)
        throw new Error(`${type.name} must be between 0 and ${capacity ?? "the group's capacity"}.`);
    }
    const rows = this.definition.storedResources ??= [];
    for (const { type, quantity } of quantities) upsert(rows,
      row => row.unitTypeId === unitTypeId && row.sideId === sideId && row.type.id === type.id,
      quantity === 0 ? null : { unitTypeId, sideId, type: structuredClone(type), quantity });
    this.changed();
  }
  stableUnitId(key) {
    const index = this.unitIndex(key), unit = this.definition.units[index];
    if (!unit) throw new Error("Select a placed Unit.");
    if (!unit.unitId) {
      if (this.effectiveUnitIds?.[index]) {
        // Make the selected engine identity explicit without rewriting references.
        unit.unitId = this.effectiveUnitIds[index];
        this.changed(); return unit.unitId;
      }
      if ((this.definition.objectives ?? []).some(o => o.selectors?.some(s => s.kind === "Unit")) ||
          (this.definition.orders ?? []).some(o => o.target?.kind === "Unit"))
        throw new Error("Unit identities are still loading. Try picking again after metadata is available; existing references were retained.");
      let number = 1;
      const used = new Set([...this.definition.units.map(u => u.unitId), ...(this.definition.machines ?? []).map(m => m.id),
        ...(this.definition.objectives ?? []).flatMap(o => (o.selectors ?? []).map(s => s.unitId))]);
      while (used.has(`authored-unit-${number}`)) number++;
      unit.unitId = `authored-unit-${number}`; this.changed();
    }
    return unit.unitId;
  }
  paintTerrain(position, terrain, direction = "East", coalGeneration = null) {
    const legacy = this.terrainMetadata(terrain)?.legacyKind ?? terrain;
    const reference = this.catalog.terrains.includes(terrain) ? { terrain } : { terrainTypeId: terrain?.startsWith('type:') ? decodeURIComponent(terrain.slice(5)) : terrain };
    upsert(this.definition.board.cells, entry => sameCell(entry.position, position),
      terrain === null ? null : { position: { ...position }, ...reference, ...(legacy === "Rail" ? { direction } : {}),
        ...(this.legacyTerrainKind(terrain) === "Lava" && coalGeneration != null ? { coalGeneration } : {}) });
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
    // Removing an earlier placement must not retarget a reference to a generated ID.
    const references = [...(this.definition.objectives ?? []).flatMap(o => (o.selectors ?? []).filter(s => s.kind === "Unit").map(s => s.unitId)),
      ...(this.definition.orders ?? []).filter(o => o.target?.kind === "Unit").map(o => o.target.unitId)];
    if (references.length && this.definition.units.some(u => !u.unitId)) {
      if (!this.effectiveUnitIds || this.effectiveUnitIds.length !== this.definition.units.length)
        throw new Error("Unit identities are still loading. Try deleting again after metadata is available; existing references were retained.");
      this.definition.units.forEach((u, i) => { u.unitId ??= this.effectiveUnitIds[i]; });
    }
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
  if (doc.coverArt != null && typeof doc.coverArt !== "string") throw new Error("Invalid saved cover art.");
  if (doc.librarySource != null && (typeof doc.librarySource.id !== "string" ||
      !/^[a-z0-9][a-z0-9-]{0,79}$/.test(doc.librarySource.id) || !Number.isInteger(doc.librarySource.revision) || doc.librarySource.revision < 1))
    throw new Error("Invalid saved library identity.");
  if (doc.librarySource?.source != null && !["local", "bundled", "override"].includes(doc.librarySource.source)) throw new Error("Invalid saved library source.");
  if (doc.librarySource?.changeToken != null && (typeof doc.librarySource.changeToken !== "string" || doc.librarySource.changeToken.length > 256)) throw new Error("Invalid saved library token.");
  if (doc.generationSource != null && (typeof doc.generationSource.code !== "string" || typeof doc.generationSource.definitionJson !== "string"))
    throw new Error("Invalid saved generation source.");
  const d = doc.definition, board = d?.board;
  // Shape and rendering limits only. Incomplete/illegal game content is the engine's concern.
  if (!board || ![board.width, board.height].every(n => Number.isInteger(n) && n >= 1 && n <= maxBoardSize) ||
      ![board.cells, board.edges, d.units, d.unitTypeIds, d.agency].every(Array.isArray) ||
      ["orders", "routes", "waypoints", "machines", "resources", "cellMarkers", "lootMarkers", "groundObjects", "objectives", "neutralSides", "fogOfWarSides", "unitTypes", "terrainTypes", "equipmentTypes", "itemTypes", "lootCardDefinitions", "storedResources"].some(key => d[key] != null && !Array.isArray(d[key])) ||
      d.loot != null && (typeof d.loot !== 'object' || d.loot.cards != null && (!Array.isArray(d.loot.cards) || d.loot.cards.some(k => typeof k !== 'string')) || d.loot.composition != null && !Array.isArray(d.loot.composition) || d.loot.preset != null && typeof d.loot.preset !== 'string') ||
      (d.lootMarkers ?? []).some(m => !m || typeof m.id !== 'string' || !m.position) ||
      (d.groundObjects ?? []).some(g => !g?.position || !g.object || typeof g.object.id !== 'string' || (typeof g.object.kind !== 'string' && typeof g.object.equipmentTypeId !== 'string' && typeof g.object.itemTypeId !== 'string' && typeof g.object.lootCardId !== 'string')) ||
      d.units.some(u => u?.inventory != null && (!Array.isArray(u.inventory) || u.inventory.some(o => !o || typeof o.id !== 'string' || (typeof o.kind !== 'string' && typeof o.equipmentTypeId !== 'string' && typeof o.itemTypeId !== 'string' && typeof o.lootCardId !== 'string')))) ||
      d.units.some(u => !u || typeof u.unitTypeId !== "string" || !u.anchor || !Number.isFinite(u.anchor.x) || !Number.isFinite(u.anchor.y)) ||
      board.cells.some(c => !c?.position) || board.edges.some(e => !e?.position) ||
      (d.terrainTypes ?? []).some(t => !t || typeof t.id !== 'string' || typeof t.name !== 'string' || t.presentation != null && typeof t.presentation.recipe !== 'string') ||
      (d.waypoints ?? []).some(w => !w?.area) || (d.routes ?? []).some(r => !Array.isArray(r?.waypointIds)) ||
      (d.machines ?? []).some(m => !m?.position || !m.type?.storage?.capacities))
    throw new Error("The saved draft is damaged or has an unsupported structure. The current draft was retained.");
  if (typeof board.defaultTerrain !== "string" && typeof board.defaultTerrainTypeId !== "string" ||
      board.defaultTerrainTypeId != null && typeof board.defaultTerrainTypeId !== 'string' ||
      board.cells.some(c => c.terrainTypeId != null && typeof c.terrainTypeId !== 'string') ||
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
