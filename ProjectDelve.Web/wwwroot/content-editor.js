import { ContentEditorState, cloneContent, contentFieldPath, parameterValue } from './content-editor-state.js';
import { contentLabel, contentUsage } from './content-browser-state.js';

// A destination-neutral form. Persistence and authoritative validation are supplied by its host.
export function mountContentEditor(root, options) {
  const document = root.ownerDocument ?? globalThis.document;
  const state = new ContentEditorState();
  let opened = false, busy = false, issues = [], preview = null, notice = '', discardResolve = null;
  let controls = new Map(), errors = new Map(), serial = 0;
  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text != null) element.textContent = String(text);
    return element;
  };
  const button = (text, action, className = '') => {
    const element = node('button', className, text); element.type = 'button'; element.addEventListener('click', action); return element;
  };
  const option = (value, text) => { const element = node('option', '', text); element.value = value; return element; };
  const input = (type, value, changed) => {
    const element = node('input'); element.type = type; element.value = value ?? '';
    element.addEventListener('input', () => changed(element.value)); return element;
  };
  const select = (values, value, changed) => {
    const element = node('select'); element.append(...values.map(item => { const choice = option(item.value, item.label); choice.disabled = !!item.disabled; return choice; }));
    element.value = value ?? ''; element.addEventListener('change', () => changed(element.value)); return element;
  };
  const text = value => node('p', 'ce-help', value);
  const group = (title, ...children) => { const element = node('section', 'ce-section'); element.append(node('h4', '', title), ...children); return element; };
  root.classList.add('content-editor'); root.id ||= 'content-editor'; root.hidden = true;
  const heading = node('header', 'ce-heading'), body = node('div', 'ce-layout'), form = node('div', 'ce-form'), previewPanel = node('aside', 'ce-preview');
  previewPanel.setAttribute('aria-label', 'Engine preview');
  const status = node('p', 'ce-status'); status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
  const diagnosticList = node('div', 'ce-diagnostics'); diagnosticList.setAttribute('role', 'alert');
  const discard = node('div', 'ce-discard'); discard.hidden = true; discard.setAttribute('role', 'alertdialog'); discard.setAttribute('aria-label', 'Unsaved content changes');
  const keep = button('Keep editing', () => resolveDiscard(false), 'ce-primary'); keep.id = 'content-editor-keep';
  const abandon = button('Discard changes', () => resolveDiscard(true), 'ce-danger'); abandon.id = 'content-editor-discard';
  discard.append(node('strong', '', 'Discard these unsaved changes?'), text('Your saved definitions will stay unchanged.'), keep, abandon);
  body.append(form, previewPanel);
  const actions = node('footer', 'ce-actions');
  const validateButton = button('Validate & preview', () => void validate(false)); validateButton.id = 'content-editor-preview';
  const saveButton = button('Save definition', () => void validate(true), 'ce-primary'); saveButton.id = 'content-editor-save';
  const cancelButton = button('Cancel', () => void requestClose()); cancelButton.id = 'content-editor-cancel';
  actions.append(cancelButton, validateButton, saveButton);
  root.append(heading, discard, status, diagnosticList, body, actions);

  function updateStatus() {
    status.textContent = busy ? 'Checking the definition…' : notice || (state.dirty ? 'Unsaved changes' : 'No unsaved changes');
    saveButton.disabled = busy || !!discardResolve; validateButton.disabled = busy || !!discardResolve; cancelButton.disabled = busy || !!discardResolve;
    form.inert = busy || !!discardResolve;
    root.setAttribute('aria-busy', String(busy));
    options.onDirtyChange?.(state.dirty);
  }
  function changed() {
    issues = []; preview = null; notice = ''; renderDiagnostics(); renderPreview(); updateStatus();
  }
  function set(path, value, rebuild = false) { state.set(path, value); changed(); if (rebuild) render(); }
  function field(title, control, path, help) {
    const row = node('div', 'ce-field'), label = node('label', '', title);
    const key = contentFieldPath(path); control.id ||= `ce-field-${++serial}`; label.htmlFor = control.id;
    control.dataset.fieldPath = key;
    const error = node('p', 'ce-field-error'); error.id = `${control.id}-error`; error.hidden = true;
    control.setAttribute('aria-describedby', error.id); controls.set(key, control); errors.set(key, error);
    row.append(label, control); if (help) row.append(text(help)); row.append(error); return row;
  }
  function referenceField(title, family, path, optional = false) {
    const current = state.get(path) ?? '', values = state.references(family), box = node('div', 'ce-reference-picker');
    const choice = select([], current, value => set(path, optional && !value ? undefined : value));
    const search = input('search', '', () => refresh()); search.placeholder = 'Find by name or ID';
    search.setAttribute('aria-label', `Find ${contentLabel(family)}`);
    function refresh() {
      const query = search.value.trim().toLocaleLowerCase();
      const selected = state.get(path) ?? '';
      const shown = values.filter(value => value.id === selected || `${value.name} ${value.id}`.toLocaleLowerCase().includes(query));
      choice.replaceChildren(option('', optional ? 'Indestructible (no result)' : 'Choose a definition'), ...shown.map(value => option(value.id, `${value.name} · ${value.id}`)));
      if (selected && !values.some(value => value.id === selected)) choice.append(option(selected, `${selected} · unavailable`));
      choice.value = selected;
    }
    refresh(); box.append(search, field(title, choice, path)); return box;
  }
  function parameter(schema, path, title = contentLabel(schema.name)) {
    const container = node('div', 'ce-parameter'); container.dataset.parameter = schema.name ?? '';
    let current = state.get(path);
    if (!schema.required || schema.allowNull) {
      const modes = [{ value: 'value', label: 'Set a value' }];
      if (!schema.required) modes.unshift({ value: 'omitted', label: 'Use default / omit' });
      if (schema.allowNull) modes.push({ value: 'null', label: 'None (null)' });
      const presence = select(modes, current === undefined ? 'omitted' : current === null ? 'null' : 'value', mode =>
        set(path, mode === 'omitted' ? undefined : mode === 'null' ? null : parameterValue(schema), true));
      container.append(field(title, presence, path));
      if (current == null) return container;
    }
    if (schema.kind === 'List') {
      const list = Array.isArray(current) ? current : [];
      const header = node('div', 'ce-list-heading'); header.append(node('h5', '', title), text(`${list.length} ${list.length === 1 ? 'entry' : 'entries'}`));
      container.append(header);
      const bounds = [];
      if (schema.minimumItems != null) bounds.push(`Minimum ${schema.minimumItems}`);
      if (schema.maximumItems != null) bounds.push(`Maximum ${schema.maximumItems}`);
      if (bounds.length) container.append(text(bounds.join(' · ')));
      list.forEach((item, index) => {
        const row = node('div', 'ce-list-item'), rowHeading = node('div', 'ce-list-heading');
        const remove = button('Remove', () => { const next = cloneContent(list); next.splice(index, 1); set(path, next, true); }, 'ce-text-button');
        remove.setAttribute('aria-label', `Remove ${title} entry ${index + 1}`);
        rowHeading.append(node('strong', '', `Entry ${index + 1}`), remove);
        for (const [label, direction] of [['Move up', -1], ['Move down', 1]]) {
          const move = button(label, () => {
            const next = cloneContent(list), target = index + direction;
            if (target < 0 || target >= next.length) return;
            [next[index], next[target]] = [next[target], next[index]]; set(path, next, true);
          }, 'ce-text-button');
          move.disabled = index + direction < 0 || index + direction >= list.length;
          move.setAttribute('aria-label', `${label} ${title} entry ${index + 1}`); rowHeading.append(move);
        }
        row.append(rowHeading, parameter(schema.item ?? { kind: 'String', required: true }, [...path, index], 'Value')); container.append(row);
      });
      const add = button('Add entry', () => set(path, [...list, parameterValue(schema.item ?? { kind: 'String' })], true));
      add.disabled = schema.maximumItems != null && list.length >= schema.maximumItems; container.append(add);
      registerContainer(path, container); return container;
    }
    if (schema.kind === 'Object') {
      container.append(node('h5', '', title));
      for (const member of schema.members ?? []) container.append(parameter(member, [...path, member.name]));
      registerContainer(path, container); return container;
    }
    if (schema.kind === 'Reference') { container.append(referenceField(title, schema.referenceCatalog, path)); return container; }
    let control;
    if (schema.kind === 'Enum') control = select((schema.allowedValues ?? []).map(value => ({ value, label: contentLabel(value) })), current, value => set(path, value));
    else if (schema.kind === 'Boolean') control = select([{ value: 'true', label: 'Yes' }, { value: 'false', label: 'No' }], String(current ?? false), value => set(path, value === 'true'));
    else {
      control = input(schema.kind === 'Integer' ? 'number' : 'text', current, value => set(path, schema.kind === 'Integer' ? (value === '' ? null : Number(value)) : value));
      if (schema.kind === 'Integer') { control.step = '1'; if (schema.minimum != null) control.min = schema.minimum; if (schema.maximum != null) control.max = schema.maximum; }
    }
    const constraints = [];
    if (schema.minimum != null) constraints.push(`Minimum ${schema.minimum}`);
    if (schema.maximum != null) constraints.push(`Maximum ${schema.maximum}`);
    if (schema.defaultValue != null && typeof schema.defaultValue !== 'object') constraints.push(`Default ${schema.defaultValue}`);
    container.append(field(title, control, path, constraints.join(' · '))); return container;
  }
  function registerContainer(path, container, focusable = false) {
    const error = node('p', 'ce-field-error'); error.hidden = true; container.append(error); errors.set(contentFieldPath(path), error);
    if (focusable) {
      container.dataset.fieldPath = contentFieldPath(path); container.tabIndex = -1;
      controls.set(contentFieldPath(path), container);
    }
  }
  function mechanicFields(mechanic, path) {
    const result = node('div', 'ce-mechanic-parameters');
    if (!mechanic) { result.append(text('This mechanic is unavailable. Choose a supported mechanic.')); return result; }
    for (const schema of mechanic.parameters ?? []) result.append(parameter(schema, [...path, schema.name]));
    if (!mechanic.parameters?.length) result.append(text('This mechanic has no authored parameters.'));
    const fixed = node('details', 'ce-fixed'); fixed.append(node('summary', '', 'Fixed Engine behavior'));
    fixed.append(text(`Context: ${contentLabel(mechanic.context)}. Target: ${contentLabel(mechanic.targeting?.selection)}.`));
    if (mechanic.targeting?.recipients) fixed.append(text(mechanic.targeting.recipients));
    fixed.append(text('Only the fields above are configurable. The Engine determines gameplay legality.')); result.append(fixed); return result;
  }
  function renderAbility(ability, index) {
    const card = node('section', 'ce-ability'); card.dataset.abilityKey = ability.key;
    const path = ['localAbilities', index], mechanic = state.mechanic(ability.mechanic?.kind);
    const header = node('div', 'ce-list-heading'); header.append(node('h5', '', `Ability ${index + 1}`), button('Remove Ability', () => { state.removeAbility(index); changed(); render(); }, 'ce-text-button'));
    for (const [label, direction] of [['Move up', -1], ['Move down', 1]]) {
      const move = button(label, () => { if (state.moveAbility(index, direction)) { changed(); render(); } }, 'ce-text-button');
      move.disabled = index + direction < 0 || index + direction >= state.definition.localAbilities.length;
      move.setAttribute('aria-label', `${label} Ability ${index + 1}`); header.append(move);
    }
    const lockedKey = state.existingAbilities.has(ability);
    const key = input('text', ability.key, value => { if (!lockedKey) set([...path, 'key'], value); }); key.readOnly = lockedKey;
    const name = input('text', ability.name, value => set([...path, 'name'], value));
    const identity = node('div', 'ce-two-columns'); identity.append(field('Name', name, [...path, 'name']), field('Local key', key, [...path, 'key'], lockedKey
      ? 'Existing key stays fixed; renaming preserves its identity.' : 'Choose a unique, stable key within this definition. It will stay fixed after saving.'));
    card.append(header, identity);
    const choices = state.supportedMechanics().filter(candidate => state.canAdd(candidate, index));
    const mechanicSelect = select(choices.map(candidate => ({ value: candidate.id, label: candidate.name, disabled: !state.costCompatibleProfiles(candidate, ability).length })), ability.mechanic?.kind, id => { if (state.changeMechanic(index, id)) { changed(); render(); } });
    if (!choices.some(candidate => candidate.id === ability.mechanic?.kind)) mechanicSelect.append(option(ability.mechanic?.kind ?? '', `${ability.mechanic?.kind ?? 'Unknown'} · unavailable`));
    mechanicSelect.value = ability.mechanic?.kind ?? '';
    card.append(field('Mechanic', mechanicSelect, [...path, 'mechanic', 'kind']));
    const profiles = state.profiles(mechanic), profile = profiles.find(candidate => candidate.timing === ability.timing);
    if (mechanic?.contextRequirements) card.append(text(mechanic.contextRequirements));
    if (mechanic && !profile) card.append(text(mechanic.unsupportedContextReason));
    const compatible = state.costCompatibleProfiles(mechanic, ability);
    const timing = select(profiles.map(candidate => ({ value: candidate.timing, label: contentLabel(candidate.timing), disabled: !compatible.includes(candidate) })), ability.timing,
      value => { state.changeTiming(index, value); changed(); render(); });
    if (!profiles.some(candidate => candidate.timing === ability.timing)) timing.append(option(ability.timing, `${contentLabel(ability.timing)} · unavailable`));
    timing.value = ability.timing;
    card.append(field('Timing', timing, [...path, 'timing']));
    if (profile?.usage === 'LimitedOnly' || profile?.usage === 'LimitedOrUnlimited') {
      const kinds = [{ value: 'limited', label: 'Limited per game' }];
      if (profile.usage === 'LimitedOrUnlimited') kinds.unshift({ value: 'unlimited', label: 'Unlimited' });
      for (const kind of kinds) kind.disabled = (ability.costs ?? []).some(cost => !state.supportsCost(state.cost(cost.kind), { ...ability, usage: { maxUses: kind.value === 'unlimited' ? null : 1 } }));
      card.append(field('Usage', select(kinds, ability.usage?.maxUses == null ? 'unlimited' : 'limited', value => {
        if (!kinds.find(kind => kind.value === value)?.disabled) set([...path, 'usage'], { maxUses: value === 'unlimited' ? null : 1 }, true);
      }), [...path, 'usage']));
      if (ability.usage?.maxUses != null) { const amount = input('number', ability.usage.maxUses, value => set([...path, 'usage', 'maxUses'], value === '' ? null : Number(value))); amount.min = 1; amount.step = 1; card.append(field('Uses per game', amount, [...path, 'usage', 'maxUses'])); }
    } else card.append(text(state.family === 'items' ? 'Using an Ability consumes the selected physical Item. Unlimited usage does not add a per-game counter. Timing and ordinary Action restrictions still apply.' : 'Unlimited usage. Timing and ordinary Action restrictions still apply.'));
    card.append(mechanicFields(mechanic, [...path, 'mechanic']));
    card.append(renderCosts(ability, index));
    if (mechanic?.description?.allowsOverride) {
      const advanced = node('details', 'ce-wording'), wording = node('textarea'); wording.rows = 3; wording.value = ability.descriptionOverride ?? '';
      wording.addEventListener('input', () => set([...path, 'descriptionOverride'], wording.value === '' ? undefined : wording.value));
      advanced.append(node('summary', '', 'Optional wording override'), field('Description template', wording, [...path, 'descriptionOverride'],
        `Leave empty for Engine wording. Supported placeholders: ${(mechanic.description.allowedOverridePlaceholders ?? mechanic.description.parameters ?? []).map(name => `{${name}}`).join(', ') || 'none'}.`));
      card.append(advanced);
    }
    registerContainer(path, card); return card;
  }
  function renderCosts(ability, abilityIndex) {
    const path = ['localAbilities', abilityIndex, 'costs'], costs = ability.costs ?? [];
    const section = node('section', 'ce-costs'); section.append(node('h5', '', 'Costs'), text('Costs are retained in the order shown. Validate to see Engine-generated Ability text.'));
    const available = state.availableCosts(ability);
    costs.forEach((cost, index) => {
      const costPath = [...path, index], descriptor = state.cost(cost.kind), row = node('div', 'ce-list-item ce-cost');
      const header = node('div', 'ce-list-heading'); header.append(node('strong', '', `Cost ${index + 1}`));
      const remove = button('Remove Cost', () => { state.removeCost(abilityIndex, index); changed(); render(); }, 'ce-text-button');
      remove.setAttribute('aria-label', `Remove Cost ${index + 1} from Ability ${abilityIndex + 1}`); header.append(remove);
      for (const [label, direction] of [['Move up', -1], ['Move down', 1]]) {
        const move = button(label, () => { if (state.moveCost(abilityIndex, index, direction)) { changed(); render(); } }, 'ce-text-button');
        move.disabled = index + direction < 0 || index + direction >= costs.length;
        move.setAttribute('aria-label', `${label} Cost ${index + 1} of Ability ${abilityIndex + 1}`); header.append(move);
      }
      row.append(header);
      const kind = select(available.map(candidate => ({ value: candidate.id, label: candidate.name })), cost.kind,
        id => { if (state.changeCost(abilityIndex, index, id)) { changed(); render(); } });
      if (!available.some(candidate => candidate.id === cost.kind)) {
        const retained = option(cost.kind, `${descriptor?.name ?? cost.kind} · unavailable in this context`); retained.disabled = true; kind.append(retained);
        row.append(text('This Cost is preserved but is unavailable for the current mechanic, source, timing or usage. Remove it or choose a supported Cost to repair the definition.'));
      }
      kind.value = cost.kind; row.append(field('Cost type', kind, [...costPath, 'kind']));
      for (const schema of descriptor?.parameters ?? []) row.append(parameter(schema, [...costPath, schema.name]));
      registerContainer(costPath, row, true); section.append(row);
    });
    const choice = select(available.map(cost => ({ value: cost.id, label: cost.name })), available[0]?.id, () => {});
    choice.dataset.costChoice = String(abilityIndex); choice.setAttribute('aria-label', `Cost for Ability ${abilityIndex + 1}`); choice.disabled = !available.length;
    const add = button('Add Cost', () => {
      if (state.addCost(abilityIndex, choice.value)) {
        changed(); render(); controls.get(contentFieldPath([...path, state.definition.localAbilities[abilityIndex].costs.length - 1, 'kind']))?.focus();
      }
    });
    add.dataset.addCost = String(abilityIndex); add.disabled = !available.length;
    const additions = node('div', 'ce-add-ability'); additions.append(choice, add); section.append(additions);
    if (!available.length) section.append(text('No editable Costs are supported for this mechanic, source, timing and usage.'));
    registerContainer(path, section, true); return section;
  }
  function renderEquipment() {
    const section = group(['units', 'items'].includes(state.family) ? 'Abilities' : 'Abilities, modifiers & capabilities', text(state.family === 'items' ? 'Each declaration owns its name, timing and mechanic. Use consumes one physical Item; choose an Ability when using an Item with multiple Abilities.' : 'Each declaration owns its name, timing and mechanic. Passive modifiers and capabilities use this same list.'));
    state.definition.localAbilities.forEach((ability, index) => section.append(renderAbility(ability, index)));
    const available = state.supportedMechanics().filter(mechanic => state.canAdd(mechanic));
    const choice = select(available.map(mechanic => ({ value: mechanic.id, label: mechanic.name })), available[0]?.id, () => {});
    choice.id = 'content-editor-add-mechanic'; choice.setAttribute('aria-label', 'Mechanic for new Ability');
    const add = button('Add Ability', () => { if (state.addAbility(choice.value)) { changed(); render(); controls.get(contentFieldPath(['localAbilities', state.definition.localAbilities.length - 1, 'name']))?.focus(); } });
    add.id = 'content-editor-add-ability'; add.disabled = !available.length;
    const additions = node('div', 'ce-add-ability'); additions.append(choice, add); section.append(additions);
    registerContainer(['localAbilities'], section); form.append(section);
  }
  function renderLoot() {
    const section = group('Reward', text('A Loot Card has exactly one reward. Equipment and Item rewards reference independent definitions.'));
    const kind = select([{ value: 'equipment', label: 'Equipment' }, { value: 'items', label: 'Single Use Item' }, { value: 'immediate', label: 'Immediate effect' }], state.rewardKind,
      value => { state.changeReward(value); changed(); render(); }); kind.id = 'content-editor-reward-kind';
    section.append(field('Reward kind', kind, []));
    if (state.rewardKind === 'immediate') {
      const choices = state.supportedMechanics('lootCards'), current = state.definition.immediate?.kind;
      const choice = select(choices.map(mechanic => ({ value: mechanic.id, label: mechanic.name })), current, id => { state.changeImmediate(id); changed(); render(); });
      section.append(field('Immediate mechanic', choice, ['immediate', 'kind']), mechanicFields(state.mechanic(current), ['immediate']));
    } else section.append(referenceField(state.rewardKind === 'items' ? 'Item Type' : 'Equipment Type', state.rewardKind, [state.rewardKind === 'items' ? 'itemTypeId' : 'equipmentTypeId']));
    form.append(section);
  }
  function renderUnit() {
    const metadata = state.catalog.unitAuthoring;
    if (!metadata) { form.append(text('Unit authoring metadata is unavailable. Reload the content catalog.')); return; }
    const stats = group('Stats');
    for (const schema of metadata.stats ?? []) stats.append(parameter({ ...schema, required: true }, [schema.name], schema.name.toUpperCase()));
    const movement = group('Movement & footprint');
    const capabilities = group('Capabilities', text('Open Door, Phase and other granted capabilities are declared in Abilities.'));
    const loot = group('Loot');
    const resources = group('Resources');
    const automation = group('Automation', text('Preferences for automated decisions; these do not change legality.'));
    const fields = metadata.fields ?? [];
    const sections = { unique: stats, footprint: movement, movementPattern: movement, terrainRestrictedMovement: movement,
      terrainRestriction: movement, wakeDistance: capabilities, canHaveItems: capabilities, canHaveEquipment: capabilities, lootDropSuccesses: loot,
      resourceStorage: resources, attackDefinitionOrder: automation, defaultController: automation, amphibious: movement };
    for (const schema of fields) (sections[schema.name] ?? stats).append(parameter(schema, [schema.name]));
    // Metadata owns enum choices and bounds; these controls only select declarations.
    if (!fields.some(schema => schema.name === 'footprint')) movement.append(parameter({ name: 'footprint', kind: 'Enum', required: true, allowedValues: metadata.footprints }, ['footprint']));
    if (!fields.some(schema => schema.name === 'movementPattern')) movement.append(parameter({ name: 'movementPattern', kind: 'Enum', allowNull: true, allowedValues: metadata.movementPatterns }, ['movementPattern']));
    const flags = String(state.definition.actions ?? 'None').split(',').map(value => value.trim());
    capabilities.append(field('Normal Attack', select([{ value: 'true', label: 'Available' }, { value: 'false', label: 'Unavailable' }], String(flags.includes('NormalAttack')), value => {
      const next = flags.filter(flag => flag !== 'NormalAttack' && flag !== 'None');
      if (value === 'true') next.unshift('NormalAttack'); set(['actions'], next.join(', ') || 'None', true);
    }), ['actions'], 'Other Actions are granted by their Ability declarations.'));
    const selected = String(state.definition.behaviors ?? 'None').split(',').map(value => value.trim());
    for (const behavior of metadata.behaviors ?? []) {
      if (behavior.value === 'None') continue;
      automation.append(field(behavior.name, select([{ value: 'true', label: 'Enabled' }, { value: 'false', label: 'Disabled' }], String(selected.includes(behavior.value)), value => {
        const next = selected.filter(flag => flag !== behavior.value && flag !== 'None');
        if (value === 'true') next.push(behavior.value); set(['behaviors'], next.join(', ') || 'None', true);
      }), ['behaviors', behavior.value]));
    }
    form.append(stats, movement, capabilities); renderEquipment(); form.append(loot, resources, automation);
  }
  function renderTerrain() {
    const section = group('Terrain', text('The Engine validates these properties and resolves assembly components.'));
    if (state.family === 'terrainAssemblies') {
      section.append(referenceField('Surface', 'terrain', ['surfaceId']));
      section.append(parameter({ name: 'components', kind: 'List', required: true, maximumItems: 4, item: {
        kind: 'Object', required: true, members: [
          { name: 'slot', kind: 'String', required: true },
          { name: 'componentId', kind: 'Reference', required: true, referenceCatalog: 'fixedComponents' },
          { name: 'quarterTurns', kind: 'Integer', required: false, minimum: 0, maximum: 3 }
        ] } }, ['components'], 'Components'));
    } else {
      for (const [key, label] of [['passable', 'Passable'], ['blocksLos', 'Blocks Line of Sight'], ['blocksVision', 'Blocks Vision'], ['allowsResourceMarkers', 'Allows Resource Markers']])
        section.append(parameter({ name: key, kind: 'Boolean', required: true }, [key], label));
      section.append(parameter({ name: 'categories', kind: 'List', required: true, item: { kind: 'String', required: true } }, ['categories'], 'Categories'));
      section.append(parameter({ name: 'recipe', kind: 'String', required: true }, ['presentation', 'recipe'], 'Presentation recipe'));
    }
    section.append(referenceField('Destroyed result', 'terrain', ['destroyedTerrainTypeId'], true), text('No result makes this Type indestructible. A successful Break replaces the whole Cell with the selected Terrain Type.'));
    form.append(section);
  }
  function render() {
    if (!opened) return;
    controls = new Map(); errors = new Map(); serial = 0; form.replaceChildren();
    heading.replaceChildren(node('p', 'ce-eyebrow', (typeof options.destinationLabel === 'function' ? options.destinationLabel() : options.destinationLabel) ?? 'CONTENT DEFINITION'), node('h3', '', `${state.existingId ? 'Edit' : 'New'} ${state.family === 'units' ? 'Unit' : state.family === 'equipment' ? 'Equipment' : state.family === 'items' ? 'Single Use Item' : state.family === 'terrain' ? 'Terrain' : state.family === 'terrainAssemblies' ? 'Terrain Assembly' : 'Loot Card'}`));
    const id = input('text', state.definition.id, value => set(['id'], value)); id.id = 'content-editor-id'; id.readOnly = !!state.existingId; id.autocomplete = 'off';
    const nameField = state.family === 'units' ? 'displayName' : 'name';
    const name = input('text', state.definition[nameField], value => set([nameField], value)); name.id = 'content-editor-name';
    const identity = node('div', 'ce-two-columns'); identity.append(field(state.family === 'lootCards' ? 'Card ID' : 'Type ID', id, ['id'], state.existingId ? 'Existing identity stays fixed.' : 'Choose a new, stable ID distinct from canonical content.'), field('Name', name, [nameField]));
    form.append(group('Identity', identity));
    if (['terrain', 'terrainAssemblies'].includes(state.family)) renderTerrain(); else if (state.family === 'units') renderUnit(); else if (['equipment', 'items'].includes(state.family)) renderEquipment(); else renderLoot();
    renderDiagnostics(); renderPreview(); updateStatus();
  }
  function renderDiagnostics() {
    for (const error of errors.values()) { error.textContent = ''; error.hidden = true; }
    for (const control of controls.values()) control.setAttribute('aria-invalid', 'false');
    diagnosticList.replaceChildren(); diagnosticList.hidden = !issues.length;
    if (!issues.length) return;
    diagnosticList.append(node('strong', '', 'Review these fields'));
    for (const issue of issues) {
      const row = node('p'); row.append(node('code', '', issue.path ?? 'definition'), node('span', '', ` — ${issue.message}`)); diagnosticList.append(row);
      let path = issue.path;
      while (path && !errors.has(path)) {
        const parent = path.replace(/(?:\.[^.\[\]]+|\[\d+\])$/, '');
        path = parent === path ? null : parent;
      }
      const error = errors.get(path);
      if (error) { error.textContent += `${error.textContent ? ' ' : ''}${issue.message}`; error.hidden = false; controls.get(path)?.setAttribute('aria-invalid', 'true'); }
    }
  }
  function readable(value) {
    if (value == null) return node('span', '', 'None');
    if (typeof value !== 'object') return node('span', '', typeof value === 'boolean' ? value ? 'Yes' : 'No' : value);
    const list = node('dl', 'ce-preview-values');
    for (const [key, item] of Object.entries(value)) { const row = node('div'), content = node('dd'); content.append(readable(item)); row.append(node('dt', '', Array.isArray(value) ? `Entry ${Number(key) + 1}` : contentLabel(key)), content); list.append(row); }
    return list;
  }
  function renderPreview() {
    previewPanel.replaceChildren(node('h4', '', 'Engine preview'));
    if (!preview) { previewPanel.append(text('Validate to see the authoritative description and resolved references. Changes clear this preview until checked again.')); return; }
    appendPreviewDefinition(previewPanel, preview);
    for (const reference of preview.references ?? []) {
      const target = state.entries.find(entry => entry.key === reference.targetKey) ?? state.entries.find(entry => entry.family === reference.family && entry.id === reference.id);
      if (!reference.exists || !target) { previewPanel.append(text(`${reference.label}: ${reference.id} (unavailable)`)); continue; }
      const panel = node('div', 'ce-reference-preview'), content = node('div', 'ce-reference-content'); content.hidden = true;
      const inspect = button(`Inspect ${target.name} · ${reference.id}`, () => { content.hidden = !content.hidden; inspect.setAttribute('aria-expanded', String(!content.hidden)); }, 'ce-text-button');
      inspect.setAttribute('aria-expanded', 'false'); inspect.dataset.referenceId = reference.id;
      inspect.dataset.referenceFamily = reference.family;
      appendPreviewDefinition(content, target); panel.append(text(reference.label), inspect, content); previewPanel.append(panel);
    }
  }
  function appendPreviewDefinition(container, entry) {
    container.append(node('h5', '', entry.name), node('code', '', entry.id));
    if (entry.summary) container.append(node('p', '', entry.summary));
    for (const ability of entry.abilities ?? []) {
      const card = node('div', 'ce-preview-ability'); card.append(node('strong', '', ability.name), text(`${contentLabel(ability.timing)} · ${contentUsage(ability.usage)} · ${ability.key}`), node('p', '', ability.description)); container.append(card);
    }
    for (const section of entry.sections ?? []) if (section.fields?.length) {
      const details = node('details'); details.append(node('summary', '', section.title));
      for (const field of section.fields) details.append(node('h6', '', field.label), readable(field.value)); container.append(details);
    }
  }
  async function validate(save) {
    if (busy || discardResolve || !opened) return false;
    const version = state.version; busy = true; issues = []; notice = ''; renderDiagnostics(); updateStatus();
    try {
      const result = await options.validate(state.request());
      if (version !== state.version || !opened) { notice = 'The form changed while validation was running. Validate the current values again.'; return false; }
      issues = result.diagnostics ?? []; preview = result.valid ? result.entry : null;
      if (!result.valid) { notice = 'The definition needs attention.'; renderDiagnostics(); renderPreview(); return false; }
      if (result.catalog) state.catalog = result.catalog;
      notice = 'Valid definition. Preview supplied by the Engine.'; renderPreview();
      if (save) {
        const definition = result.definition ?? cloneContent(state.definition);
        const saved = await options.save({ family: state.family, definition, existingId: state.existingId });
        state.saved(definition); close();
        await options.onSaved?.(saved, { family: state.family, definition });
      }
      return true;
    } catch (failure) { notice = failure.message ?? 'The definition could not be saved.'; return false; }
    finally { busy = false; updateStatus(); }
  }
  function resolveDiscard(discardChanges) {
    discard.hidden = true; const resolve = discardResolve; discardResolve = null;
    updateStatus(); if (discardChanges) close(); else cancelButton.focus(); resolve?.(discardChanges);
  }
  function close() { opened = false; root.hidden = true; options.onClose?.(); }
  async function requestClose() {
    if (!opened) return true;
    if (busy) return false;
    if (!state.dirty) { close(); return true; }
    if (discardResolve) return false;
    discard.hidden = false; keep.focus(); return new Promise(resolve => { discardResolve = resolve; updateStatus(); });
  }
  return {
    async open(specification) {
      if (opened && !await requestClose()) return false;
      state.catalog = typeof options.catalog === 'function' ? options.catalog() : options.catalog;
      state.open(specification); opened = true; busy = false; issues = []; preview = null; notice = ''; root.hidden = false;
      render(); controls.get('definition.id')?.focus(); return true;
    },
    requestClose, isDirty: () => opened && state.dirty, isOpen: () => opened,
    validate: () => validate(false), save: () => validate(true)
  };
}
