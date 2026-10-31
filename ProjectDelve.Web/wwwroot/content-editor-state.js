// Authoring state only. The Engine validates definitions and supplies their wording.
export const cloneContent = value => structuredClone(value);
export const contentFieldPath = parts => 'definition' + parts.map(part => typeof part === 'number' ? `[${part}]` : `.${part}`).join('');

export function parameterValue(parameter) {
  if (parameter.defaultValue != null) return cloneContent(parameter.defaultValue);
  switch (parameter.kind) {
    case 'Integer': return Math.max(parameter.minimum ?? 0, Math.min(0, parameter.maximum ?? 0));
    case 'Boolean': return false;
    case 'Enum': return parameter.allowedValues?.[0] ?? '';
    case 'List': return Array.from({ length: parameter.minimumItems ?? 0 }, () => parameterValue(parameter.item ?? { kind: 'String' }));
    case 'Object': return parameterObject(parameter.members);
    default: return '';
  }
}

export function parameterObject(parameters) {
  return Object.fromEntries((parameters ?? []).filter(parameter => parameter.required || parameter.defaultValue != null)
    .map(parameter => [parameter.name, parameterValue(parameter)]));
}

export class ContentEditorState {
  constructor(catalog = {}) { this.catalog = catalog; this.definition = null; this.version = 0; }
  get entries() { return this.catalog?.entries ?? []; }
  get mechanics() { return this.entries.map(entry => entry.mechanic).filter(Boolean); }
  mechanic(id) { return this.mechanics.find(mechanic => mechanic.id === id); }
  supportedMechanics(family = this.family) {
    return this.mechanics.filter(mechanic => family === 'equipment'
      ? mechanic.family === 'Ability' && mechanic.profiles?.some(profile => profile.source === 'Equipment')
      : mechanic.family === 'ImmediateLoot' && mechanic.profiles?.some(profile => profile.source === 'LootCard'));
  }
  profiles(mechanic) { return (mechanic?.profiles ?? []).filter(profile => profile.source === 'Equipment'); }
  references(family) {
    const field = { equipment: 'equipmentTypes', items: 'itemTypes', lootCards: 'lootCardDefinitions' }[family];
    const combined = new Map(this.entries.filter(entry => entry.family === family).map(entry => [entry.id, { id: entry.id, name: entry.name }]));
    for (const definition of this.context?.[field] ?? []) combined.set(definition.id, { id: definition.id, name: definition.name });
    return [...combined.values()].sort((a, b) => a.name.localeCompare(b.name));
  }
  open({ family, definition, existingId, copy = false, context = {} }) {
    this.family = family; this.existingId = copy ? undefined : existingId;
    this.context = cloneContent(context);
    this.definition = cloneContent(definition ?? (family === 'equipment' ? { id: '', name: '', localAbilities: [] } : { id: '', name: '', equipmentTypeId: '' }));
    if (copy) this.definition.id = '';
    if (family === 'equipment') this.definition.localAbilities ??= [];
    this.usedKeys = new Set(this.definition.localAbilities?.map(ability => ability.key) ?? []);
    this.existingAbilities = new WeakSet(this.existingId ? this.definition.localAbilities ?? [] : []);
    this.baseline = JSON.stringify(this.definition); this.version++;
  }
  get dirty() { return !!this.definition && this.baseline !== JSON.stringify(this.definition); }
  get(path) { return path.reduce((object, key) => object?.[key], this.definition); }
  set(path, value) {
    let parent = this.definition;
    for (let index = 0; index < path.length - 1; index++) parent = parent[path[index]] ??= typeof path[index + 1] === 'number' ? [] : {};
    if (value === undefined) delete parent[path.at(-1)]; else parent[path.at(-1)] = value;
    if (path[0] === 'localAbilities' && path.at(-1) === 'key') this.usedKeys.add(value);
    this.version++;
  }
  newKey() {
    let index = 1; while (this.usedKeys.has(`ability-${index}`)) index++;
    const key = `ability-${index}`; this.usedKeys.add(key); return key;
  }
  canAdd(mechanic, excludingIndex = -1) {
    return mechanic.maxPerSource == null || this.definition.localAbilities.filter((ability, index) => index !== excludingIndex && ability.mechanic?.kind === mechanic.id).length < mechanic.maxPerSource;
  }
  addAbility(id) {
    const mechanic = this.supportedMechanics().find(candidate => candidate.id === id);
    if (!mechanic || !this.canAdd(mechanic)) return false;
    const profile = this.profiles(mechanic)[0];
    this.definition.localAbilities.push({ key: this.newKey(), name: mechanic.name, timing: profile.timing,
      usage: { maxUses: profile.usage === 'LimitedOnly' ? 1 : null }, mechanic: { kind: mechanic.id, ...parameterObject(mechanic.parameters) } });
    this.version++; return true;
  }
  removeAbility(index) { this.definition.localAbilities.splice(index, 1); this.version++; }
  changeMechanic(index, id) {
    const mechanic = this.supportedMechanics().find(candidate => candidate.id === id);
    if (!mechanic || !this.canAdd(mechanic, index)) return false;
    const ability = this.definition.localAbilities[index];
    const profile = this.profiles(mechanic).find(candidate => candidate.timing === ability.timing) ?? this.profiles(mechanic)[0];
    ability.mechanic = { kind: mechanic.id, ...parameterObject(mechanic.parameters) };
    ability.timing = profile.timing; this.applyUsage(ability, profile); delete ability.descriptionOverride;
    this.version++; return true;
  }
  changeTiming(index, timing) {
    const ability = this.definition.localAbilities[index];
    const profile = this.profiles(this.mechanic(ability.mechanic.kind)).find(candidate => candidate.timing === timing);
    if (!profile) return false;
    ability.timing = timing; this.applyUsage(ability, profile); this.version++; return true;
  }
  applyUsage(ability, profile) {
    if (profile.usage === 'UnlimitedOnly' || profile.usage === 'NotApplicable') ability.usage = { maxUses: null };
    else if (profile.usage === 'LimitedOnly' && ability.usage?.maxUses == null) ability.usage = { maxUses: 1 };
  }
  get rewardKind() { return this.definition?.immediate ? 'immediate' : this.definition?.itemTypeId != null ? 'items' : 'equipment'; }
  changeReward(kind) {
    delete this.definition.equipmentTypeId; delete this.definition.itemTypeId; delete this.definition.immediate;
    if (kind === 'immediate') this.changeImmediate(this.supportedMechanics('lootCards')[0]?.id);
    else this.definition[kind === 'items' ? 'itemTypeId' : 'equipmentTypeId'] = '';
    this.version++;
  }
  changeImmediate(id) {
    const mechanic = this.supportedMechanics('lootCards').find(candidate => candidate.id === id);
    this.definition.immediate = mechanic ? { kind: mechanic.id, ...parameterObject(mechanic.parameters) } : {};
    this.version++;
  }
  request() {
    return { family: this.family, definition: cloneContent(this.definition), ...(this.existingId ? { existingId: this.existingId } : {}), context: cloneContent(this.context) };
  }
  saved(definition) { this.definition = cloneContent(definition); this.baseline = JSON.stringify(this.definition); this.version++; }
}
