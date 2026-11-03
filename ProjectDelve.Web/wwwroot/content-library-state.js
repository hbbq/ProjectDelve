// Package workspace only; Engine owns validation, dependency resolution and previews.
export class LibraryContentDraft {
  constructor(record, { currentRevision = record.revision, archived = false, isNew = false } = {}) {
    this.version = 0;
    this.load(record, { currentRevision, archived, isNew });
  }
  load(record, { currentRevision = record.revision, archived = false, isNew = false } = {}) {
    this.package = structuredClone(record.package);
    this.bindings = structuredClone(record.bindings ?? []);
    this.revision = record.revision; this.origin = record.libraryId ?? '';
    this.editable = !archived && this.revision === currentRevision;
    this.isNew = isNew; this.version++;
    this.baseline = JSON.stringify({ package: this.package, bindings: this.bindings });
  }
  static create(id, title, providers = []) {
    return new LibraryContentDraft({ revision: 0, package: { formatVersion: 1, id, title, dependencies: [...providers], definitions: {} }, bindings: [] }, { isNew: true });
  }
  get dirty() { return this.isNew || this.baseline !== JSON.stringify({ package: this.package, bindings: this.bindings }); }
  get stamp() { return `${this.package.id}:${this.revision}:${this.version}`; }
  touch() { if (!this.editable) throw new Error('Historical revisions are read only.'); this.version++; }
  request() { return structuredClone({ package: this.package, expectedRevision: this.revision, bindings: this.bindings }); }
  context() {
    const body = this.package.definitions;
    return structuredClone({ unitTypes: body.unitTypes ?? [], equipmentTypes: body.equipmentTypes ?? [], itemTypes: body.itemTypes ?? [], lootCardDefinitions: body.lootCardDefinitions ?? [] });
  }
  apply({ family, definition, existingId, expectedRevision }) {
    if (!this.editable || expectedRevision !== this.stamp) throw new Error('The package workspace changed. Cancel and reopen this definition.');
    const field = { units: 'unitTypes', equipment: 'equipmentTypes', lootCards: 'lootCardDefinitions' }[family];
    if (!field) throw new Error('This editor supports Units, Equipment and Loot Cards.');
    const rows = this.package.definitions[field] ??= [];
    if (existingId) {
      const index = rows.findIndex(row => row.id === existingId);
      if (index < 0 || definition.id !== existingId) throw new Error('Only an owned definition with its existing Type ID can be edited.');
      rows[index] = structuredClone(definition);
    } else {
      if (rows.some(row => row.id === definition.id)) throw new Error('This package already owns that Type ID.');
      rows.push(structuredClone(definition));
    }
    this.touch();
  }
  bind(packageId, revision) {
    this.bindings = this.bindings.filter(binding => binding.packageId !== packageId);
    if (revision != null) this.bindings.push({ packageId, revision: Number(revision) });
    this.selectDependency(packageId, revision != null);
  }
  selectDependency(id, selected) {
    this.package.dependencies = (this.package.dependencies ?? []).filter(value => value !== id);
    if (selected) this.package.dependencies.push(id);
    this.touch();
  }
  reset() {
    const baseline = JSON.parse(this.baseline);
    this.package = baseline.package; this.bindings = baseline.bindings; this.version++;
  }
}
