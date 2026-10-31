import { ContentBrowserState, contentLabel, contentUsage } from './content-browser-state.js';
import { mountContentEditor } from './content-editor.js';

// Read-only viewer: only explicit catalog / Designer snapshot requests are made.
export function mountContentBrowser(root, options = {}) {
  const state = new ContentBrowserState();
  const request = options.fetch ?? globalThis.fetch.bind(globalThis);
  const document = root.ownerDocument ?? globalThis.document;
  const cache = new Map();
  let context = 'canonical', draftName = 'Designer draft', pending = null, requestVersion = 0;
  let error = '', retry = null;
  let destination = null, scenarioSnapshot = null, catalogRevision = null, editDestination = null, editRevision = null;
  const node = (tag, className, text) => {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text != null) element.textContent = String(text);
    return element;
  };
  const button = (text, action, className = '') => {
    const element = node('button', className, text); element.type = 'button';
    element.addEventListener('click', action); return element;
  };
  const chip = (text, className = '') => node('span', `cb-chip ${className}`, text);
  const label = (text, control) => { const result = node('label', 'cb-control'); result.append(node('span', '', text), control); return result; };
  const select = (name, action) => {
    const result = node('select'); result.setAttribute('aria-label', name);
    result.addEventListener('change', () => action(result.value)); return result;
  };
  const option = (value, text) => { const result = node('option', '', text); result.value = value; return result; };
  const section = (title, ...children) => { const result = node('section', 'cb-section'); result.append(node('h4', '', title), ...children); return result; };
  const contentGroup = (title, ...children) => {
    const result = node('details', 'cb-section cb-content-group'); result.open = true;
    result.append(node('summary', '', title), ...children); return result;
  };
  const empty = text => node('p', 'cb-muted', text);

  root.classList.add('content-browser');
  root.setAttribute('aria-label', 'Content Browser');
  const header = node('header', 'cb-header');
  const heading = node('div');
  heading.append(node('p', 'cb-eyebrow', 'THE GAME, IN DETAIL'), node('h2', '', 'Content Browser'),
    node('p', 'cb-intro', 'Explore the content, the abilities it owns, and the mechanics behind them.'));
  const contextControl = select('Content context', value => void switchContext(value));
  contextControl.id = 'content-context';
  const contextArea = node('div', 'cb-context');
  const contextNote = node('p', 'cb-context-note');
  const returnLink = node('a', 'cb-return', 'Return to Designer'); returnLink.href = '#designer'; returnLink.hidden = true;
  contextArea.append(label('Inspect', contextControl), contextNote, returnLink);
  header.append(heading, contextArea);

  const authoring = node('div', 'cb-authoring');
  const newEquipment = button('Create Equipment', () => void openEditor({ family: 'equipment' })); newEquipment.id = 'content-create-equipment';
  const newLoot = button('Create Loot Card', () => void openEditor({ family: 'lootCards' })); newLoot.id = 'content-create-loot';
  const destinationNote = node('p', 'cb-authoring-note');
  authoring.append(newEquipment, newLoot, destinationNote);

  const status = node('div', 'cb-status'); status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
  const diagnostics = node('details', 'cb-diagnostics');
  const toolbar = node('div', 'cb-toolbar');
  const search = node('input'); search.type = 'search'; search.placeholder = 'Search by name or ID'; search.autocomplete = 'off';
  search.id = 'content-search';
  search.setAttribute('aria-label', 'Search content by name or ID');
  search.addEventListener('input', () => { state.filter({ query: search.value }); render(); });
  const categoryControl = select('Content category', value => { state.filter({ category: value }); render(); });
  const originControl = select('Content origin', value => { state.filter({ origin: value }); render(); });
  categoryControl.id = 'content-category'; originControl.id = 'content-origin';
  toolbar.append(label('Find content', search), label('Category', categoryControl), label('Origin', originControl));
  const workspace = node('div', 'cb-workspace');
  const master = node('aside', 'cb-master'); master.setAttribute('aria-label', 'Content catalog');
  const listHeading = node('div', 'cb-list-heading');
  const count = node('p'); count.setAttribute('aria-live', 'polite');
  const clear = button('Clear filters', () => { state.filter({ query: '', category: 'all', origin: 'all' }); render(); }, 'cb-text-button');
  listHeading.append(count, clear);
  const list = node('div', 'cb-list'); list.setAttribute('aria-label', 'Definitions');
  list.id = 'content-list';
  master.append(listHeading, list);
  const detail = node('article', 'cb-detail'); detail.setAttribute('aria-label', 'Selected definition');
  detail.id = 'content-detail';
  workspace.append(master, detail);
  const editorRoot = node('section');
  root.replaceChildren(header, authoring, status, diagnostics, toolbar, workspace, editorRoot);
  const editor = mountContentEditor(editorRoot, {
    catalog: () => {
      const entries = new Map();
      for (const catalog of [cache.get('canonical'), cache.get('draft'), state.catalog])
        for (const entry of catalog?.entries ?? []) entries.set(entry.key, entry);
      return { entries: [...entries.values()] };
    },
    validate: async payload => {
      const response = await request('/api/content/validate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
      const result = await response.json();
      if (!Array.isArray(result.diagnostics)) throw new Error('Validation could not be completed.');
      return result;
    },
    save: payload => editDestination.save({ ...payload, expectedRevision: editRevision }),
    onSaved: async (saved, content) => {
      await showScenario(saved.definition, saved.name, editDestination);
      state.open(`${content.family}:${content.definition.id}`); render(); focusDetail();
    },
    onClose: () => { root.classList.remove('cb-editing'); detail.querySelector('h3')?.focus({ preventScroll: true }); },
    onDirtyChange: () => {}
  });
  globalThis.addEventListener?.('beforeunload', event => { if (editor.isDirty()) { event.preventDefault(); event.returnValue = ''; } });

  async function refreshDestination() {
    if (!destination || catalogRevision === destination.getRevision()) return;
    const snapshot = destination.getSnapshot();
    await showScenario(snapshot.definition, snapshot.name, destination);
  }
  function canAuthor() { return !!destination && !pending && state.entries.some(entry => entry.mechanic); }
  async function openEditor(specification, entryKey = null, makeLoot = false) {
    if (!canAuthor() || !await editor.requestClose()) return false;
    await refreshDestination();
    if (!canAuthor()) return false;
    if (entryKey) {
      const entry = state.entries.find(candidate => candidate.key === entryKey) ?? cache.get('canonical')?.entries.find(candidate => candidate.key === entryKey);
      if (!entry) return false;
      specification = makeLoot ? { family: 'lootCards', definition: { id: '', name: `${entry.name} Loot`, equipmentTypeId: entry.id } }
        : { ...specification, family: entry.family, definition: entry.authoredDefinition ?? entry.definition };
    }
    editDestination = destination; editRevision = destination.getRevision();
    const opened = await editor.open({ ...specification, context: destination.getContext() });
    if (opened) { root.classList.add('cb-editing'); editorRoot.scrollIntoView?.({ block: 'start' }); }
    return opened;
  }
  async function switchContext(next) {
    if (!await editor.requestClose()) { contextControl.value = context; return; }
    await refreshDestination();
    if (next === 'canonical') await showCanonical(); else if (next === 'scenario') await showFullScenario(); else await showCachedDraft();
  }

  function value(value, depth = 0) {
    if (value == null) return node('span', 'cb-muted', 'None');
    if (typeof value === 'boolean') return node('span', 'cb-value', value ? 'Yes' : 'No');
    if (typeof value !== 'object') return node('span', 'cb-value', value);
    if (Array.isArray(value)) {
      if (!value.length) return node('span', 'cb-muted', 'None');
      const result = node('ul', 'cb-value-list');
      for (const item of value) { const row = node('li'); row.append(valueNode(item, depth + 1)); result.append(row); }
      return result;
    }
    const result = node('dl', 'cb-values');
    for (const [key, item] of Object.entries(value)) {
      const row = node('div', 'cb-value-row'); const content = node('dd'); content.append(valueNode(item, depth + 1));
      row.append(node('dt', '', contentLabel(key)), content); result.append(row);
    }
    return result;
  }
  const valueNode = (data, depth = 0) => value(data, depth);
  function fields(items) {
    const result = node('dl', 'cb-fields');
    for (const field of items ?? []) {
      const complex = field.value !== null && typeof field.value === 'object' && Object.keys(field.value).length > 0;
      const row = node('div', complex ? 'cb-field cb-field-complex' : 'cb-field');
      const content = node('dd'); content.append(valueNode(field.value));
      row.append(node('dt', '', field.label), content); result.append(row);
    }
    return result;
  }
  function linkTo(key, text, className = 'cb-reference') {
    const result = button(text, () => { if (state.open(key, true)) { render(); focusDetail(); } }, className);
    result.dataset.contentTarget = key;
    const target = state.entries.find(entry => entry.key === key);
    if (target) { result.dataset.targetId = target.id; result.dataset.targetFamily = target.family; }
    return result;
  }
  function focusDetail() { detail.querySelector('h3')?.focus({ preventScroll: true }); detail.scrollIntoView?.({ block: 'nearest' }); }
  function renderParameter(parameter) {
    const card = node('li', 'cb-parameter');
    const title = node('div', 'cb-parameter-heading');
    title.append(node('code', '', parameter.name), chip(contentLabel(parameter.kind)), chip(parameter.required ? 'Required' : 'Optional'));
    card.append(title);
    const attributes = [];
    if (parameter.allowNull) attributes.push({ label: 'Null allowed', value: true });
    if (parameter.minimum != null) attributes.push({ label: 'Minimum', value: parameter.minimum });
    if (parameter.maximum != null) attributes.push({ label: 'Maximum', value: parameter.maximum });
    if (parameter.defaultValue != null) attributes.push({ label: 'Default', value: parameter.defaultValue });
    if (parameter.allowedValues?.length) attributes.push({ label: 'Allowed values', value: parameter.allowedValues });
    if (parameter.minimumItems != null) attributes.push({ label: 'Minimum items', value: parameter.minimumItems });
    if (parameter.maximumItems != null) attributes.push({ label: 'Maximum items', value: parameter.maximumItems });
    if (parameter.referenceCatalog) attributes.push({ label: 'Reference catalog', value: contentLabel(parameter.referenceCatalog) });
    if (attributes.length) card.append(fields(attributes));
    if (parameter.item) {
      card.append(node('h5', '', 'Each item'));
      const children = node('ul', 'cb-parameters'); children.append(renderParameter(parameter.item)); card.append(children);
    }
    if (parameter.members?.length) {
      card.append(node('h5', '', 'Object fields'));
      const children = node('ul', 'cb-parameters'); children.append(...parameter.members.map(renderParameter)); card.append(children);
    }
    return card;
  }
  function renderMechanic(mechanic) {
    const parameters = node('ul', 'cb-parameters'); parameters.append(...(mechanic.parameters ?? []).map(renderParameter));
    detail.append(section('Authored parameters', mechanic.parameters?.length ? parameters : empty('This mechanic has no authored parameters.')));
    const profiles = node('div', 'cb-profiles');
    for (const profile of mechanic.profiles ?? []) {
      const row = node('div', 'cb-profile');
      row.append(node('strong', '', contentLabel(profile.source)), chip(contentLabel(profile.timing)), node('span', '', contentLabel(profile.usage)));
      profiles.append(row);
    }
    const supported = section('Supported combinations', empty('Each row is one supported source, timing and usage combination.'), profiles);
    if (mechanic.maxPerSource != null) supported.append(empty(`Maximum per source: ${mechanic.maxPerSource}`));
    detail.append(supported);
    const fixed = fields([
      { label: 'Context', value: contentLabel(mechanic.context) },
      { label: 'Target selection', value: contentLabel(mechanic.targeting?.selection) },
      { label: 'Recipients and targeting', value: mechanic.targeting?.recipients },
      { label: 'Dependencies', value: mechanic.dependencies }
    ]);
    detail.append(section('Fixed Engine behavior', empty('Only the listed authored parameters are configurable. Targeting, recipients and context describe fixed Engine rules.'), fixed));
    if (mechanic.description) {
      const contract = section('Description contract', node('p', 'cb-rule-text', mechanic.description.template));
      for (const wording of mechanic.description.timingTemplates ?? []) {
        const row = node('div', 'cb-timing-wording');
        row.append(chip(contentLabel(wording.timing)), node('p', 'cb-rule-text', wording.template)); contract.append(row);
      }
      const policies = Object.fromEntries(Object.entries(mechanic.description).filter(([key]) => !['template', 'timingTemplates'].includes(key)));
      if (Object.keys(policies).length) {
        const advanced = node('details', 'cb-wording-policies');
        advanced.append(node('summary', '', 'Wording override rules'), valueNode(policies)); contract.append(advanced);
      }
      detail.append(contract);
    }
  }
  function renderAbility(ability) {
    const card = node('section', 'cb-ability');
    card.dataset.abilityKey = ability.key; card.dataset.mechanicId = ability.mechanicId;
    const title = node('div', 'cb-ability-heading');
    title.append(node('h5', '', ability.name), chip(contentLabel(ability.timing)), chip(contentUsage(ability.usage)));
    card.append(title, node('code', 'cb-local-key', `Local key · ${ability.key}`));
    if (ability.description) card.append(node('p', 'cb-rule-text', ability.description));
    const mechanic = state.mechanic(ability.mechanicId);
    const line = node('div', 'cb-mechanic-link'); line.append(node('span', '', 'Mechanic'));
    line.append(mechanic ? linkTo(mechanic.key, mechanic.name) : node('code', '', ability.mechanicId ?? 'Unavailable'));
    card.append(line);
    if (ability.parameters && Object.keys(ability.parameters).length)
      card.append(node('h6', '', 'Authored parameters'), valueNode(ability.parameters));
    if (ability.ownerKey) {
      const owner = state.entries.find(entry => entry.key === ability.ownerKey);
      if (owner) card.append(owner.key === state.selectedKey ? node('p', 'cb-owner', `Owned by ${owner.name}`)
        : linkTo(owner.key, `Owned by ${owner.name}`));
    }
    return card;
  }
  function renderDetail() {
    detail.replaceChildren();
    const entry = state.selected;
    if (!entry) { detail.append(node('p', 'cb-detail-empty', state.catalog ? 'Choose a definition from the catalog.' : 'Your content reference, ready to explore.')); return; }
    const navigation = node('div', 'cb-detail-navigation');
    navigation.append(button('← All results', () => { state.detailOpen = false; render(); list.querySelector('[aria-current="true"]')?.focus(); }, 'cb-back-list'));
    if (state.history.length) navigation.append(button('← Previous definition', () => { state.back(); render(); focusDetail(); }, 'cb-text-button'));
    navigation.append(chip(contentLabel(entry.origin), 'cb-origin'));
    if (entry.availability && entry.availability !== entry.origin) navigation.append(chip(contentLabel(entry.availability)));
    const title = node('h3', '', entry.name); title.tabIndex = -1;
    const familyName = state.categories.find(category => category.id === entry.family)?.name ?? contentLabel(entry.family);
    detail.append(navigation, node('p', 'cb-eyebrow', familyName), title, node('code', 'cb-content-id', entry.id));
    if (entry.family === 'equipment' || entry.family === 'lootCards') {
      const actions = node('div', 'cb-definition-actions');
      const copy = button('Copy as new', () => void openEditor({ copy: true }, entry.key)); copy.id = 'content-copy-definition'; copy.disabled = !destination; actions.append(copy);
      const contextField = entry.family === 'equipment' ? 'equipmentTypes' : 'lootCardDefinitions';
      if (destination && entry.origin !== 'canonical' && entry.availability !== 'legacy-collision' && destination.getContext()[contextField]?.some(definition => definition.id === entry.id)) {
        const edit = button('Edit definition', () => void openEditor({ existingId: entry.id }, entry.key)); edit.id = 'content-edit-definition'; actions.append(edit);
      }
      if (entry.family === 'equipment') { const loot = button('Create Loot Card for this Equipment', () => void openEditor({}, entry.key, true)); loot.id = 'content-create-linked-loot'; loot.disabled = !destination; actions.append(loot); }
      if (entry.availability === 'legacy-collision') actions.append(empty('This historical local ID overlaps canonical content. Copy it with a new ID to edit it.'));
      detail.append(actions);
    }
    if (entry.summary) detail.append(node('p', 'cb-summary', entry.summary));
    if (!entry.mechanic)
      for (const group of entry.sections ?? [])
        if (group.fields?.length) detail.append(contentGroup(group.title, fields(group.fields)));
    if (entry.abilities?.length) detail.append(section(`Owned abilities · ${entry.abilities.length}`, ...entry.abilities.map(renderAbility)));
    if (entry.mechanic) renderMechanic(entry.mechanic);
    if (entry.references?.length) {
      const references = node('div', 'cb-references');
      for (const reference of entry.references) {
        const row = node('div', 'cb-reference-row');
        row.append(node('span', 'cb-muted', reference.label));
        const target = state.entries.find(candidate => candidate.key === reference.targetKey);
        if (reference.exists && target) row.append(linkTo(target.key, target.name), node('code', '', reference.id));
        else row.append(node('span', 'cb-missing', `${reference.id} · unavailable`));
        references.append(row);
      }
      detail.append(section('References', references));
    }
    const advanced = node('details', 'cb-advanced'); advanced.append(node('summary', '', 'Advanced · JSON definition'));
    advanced.append(node('p', 'cb-muted', 'The typed definition used for this inspection.'), node('pre', '', JSON.stringify(entry.definition ?? entry.mechanic, null, 2)));
    if (entry.authoredDefinition != null) advanced.append(node('h5', '', 'Authored definition'), node('pre', '', JSON.stringify(entry.authoredDefinition, null, 2)));
    detail.append(advanced);
  }
  function render() {
    root.classList.toggle('cb-show-detail', state.detailOpen);
    root.setAttribute('aria-busy', String(!!pending));
    contextControl.replaceChildren(option('canonical', 'Canonical catalog'));
    if (cache.has('draft') || context === 'draft') contextControl.append(option('draft', `${destination ? 'Draft content' : 'Designer draft'} · ${draftName}`));
    if (destination && scenarioSnapshot) contextControl.append(option('scenario', `Full scenario inspection · ${draftName}`));
    contextControl.value = context;
    contextNote.textContent = context === 'canonical' ? 'Canonical definitions · read only'
      : destination && context === 'draft' ? 'Equipment, Items, Loot Cards and mechanics for this destination. Definitions are saved only to the attached draft.'
      : 'Effective definitions for this Designer snapshot · read only. Local content stays scoped to this draft.';
    returnLink.hidden = context === 'canonical';
    newEquipment.disabled = !canAuthor(); newLoot.disabled = !canAuthor();
    destinationNote.replaceChildren();
    if (destination) destinationNote.textContent = `Save destination: ${destination.label ?? draftName}`;
    else { destinationNote.append(node('span', '', 'To create or copy content, ')); const link = node('a', '', 'open a draft in Designer'); link.href = '#designer'; destinationNote.append(link, node('span', '', ' and choose Content.')); }
    search.value = state.query;
    const categories = state.categories;
    const categoryNames = new Map(categories.map(category => [category.id, category.name]));
    categoryControl.replaceChildren(option('all', `All categories (${state.entries.filter(entry => state.matches(entry, { category: 'all' })).length})`),
      ...categories.map(category => option(category.id, `${category.name} (${category.count})`)));
    categoryControl.value = state.category;
    originControl.replaceChildren(option('all', 'All origins'), ...state.origins.map(origin => option(origin, contentLabel(origin))));
    originControl.value = state.origin;
    status.replaceChildren();
    if (pending) status.append(node('span', '', 'Loading content…'));
    if (error) { status.append(node('span', 'cb-error', error)); if (retry) status.append(button('Try again', () => void retry(), 'cb-text-button')); }
    status.hidden = !pending && !error;
    diagnostics.replaceChildren();
    const issues = state.catalog?.diagnostics ?? [];
    diagnostics.hidden = !issues.length;
    if (issues.length) {
      diagnostics.open = true;
      diagnostics.append(node('summary', '', `${issues.length} inspection ${issues.length === 1 ? 'issue' : 'issues'}`));
      const messages = node('ul');
      for (const issue of issues) {
        const row = node('li'); if (issue.path) row.append(node('code', '', issue.path));
        row.append(node('p', '', issue.message)); messages.append(row);
      }
      diagnostics.append(messages);
    }
    const visible = state.visible;
    count.textContent = `${visible.length} of ${state.entries.length} definitions`;
    clear.hidden = !state.query && state.category === 'all' && state.origin === 'all';
    list.replaceChildren();
    for (const entry of visible) {
      const row = button('', () => { state.open(entry.key); render(); focusDetail(); }, 'cb-list-entry');
      row.dataset.contentKey = entry.key; row.setAttribute('aria-current', String(entry.key === state.selectedKey));
      row.dataset.contentId = entry.id; row.dataset.contentFamily = entry.family;
      row.append(node('strong', '', entry.name), node('code', '', entry.id));
      const metadata = node('span', 'cb-list-meta');
      metadata.append(node('span', '', categoryNames.get(entry.family) ?? contentLabel(entry.family)),
        node('span', '', contentLabel(entry.origin)));
      if (entry.availability && entry.availability !== entry.origin) metadata.append(node('span', '', contentLabel(entry.availability)));
      row.append(metadata); list.append(row);
    }
    if (!visible.length && state.catalog) list.append(empty('No definitions match these filters.'));
    renderDetail();
  }
  async function load(key, url, init, force = false) {
    const version = ++requestVersion; context = key; error = '';
    if (cache.has(key) && !force) { pending = null; state.load(cache.get(key)); render(); return cache.get(key); }
    state.load({ entries: [], categories: [], diagnostics: [] });
    retry = () => load(key, url, init, true);
    const operation = (async () => {
      try {
        const response = await request(url, init);
        const data = await response.json();
        if (!response.ok) {
          if (version === requestVersion && Array.isArray(data.diagnostics))
            state.load({ entries: [], categories: [], diagnostics: data.diagnostics });
          throw new Error(data.message ?? data.detail ?? (key === 'draft'
            ? 'This draft could not be inspected. Review the issues below.' : 'Content could not be loaded.'));
        }
        if (!Array.isArray(data.entries)) throw new Error('The content response did not contain a catalog.');
        if (version !== requestVersion) return data;
        cache.set(key, data); state.load(data); return data;
      } catch (failure) {
        if (version === requestVersion) error = failure.message ?? 'Content could not be loaded.';
        return null;
      } finally {
        if (version === requestVersion) { pending = null; render(); }
      }
    })();
    pending = operation; render(); return operation;
  }
  async function showCanonical() { if (editor.isOpen() && !await editor.requestClose()) return null; return load('canonical', '/api/content/catalog'); }
  function showCachedDraft() { return load('draft', destination ? '/api/content/context' : '/api/content/inspect', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(destination ? destination.getContext() : scenarioSnapshot) }); }
  function showFullScenario() { return load('scenario', '/api/content/inspect', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(scenarioSnapshot) }); }
  async function showScenario(definition, name = 'Designer draft', target = null) {
    if (editor.isOpen() && !await editor.requestClose()) return null;
    draftName = name || 'Designer draft';
    const snapshot = structuredClone(definition);
    destination = target; scenarioSnapshot = snapshot; catalogRevision = target?.getRevision() ?? null;
    cache.delete('draft'); cache.delete('scenario');
    return load('draft', target ? '/api/content/context' : '/api/content/inspect', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(target ? target.getContext() : snapshot) }, true);
  }
  render();
  return {
    show: async () => { if (destination && !editor.isOpen()) await refreshDestination(); return pending ?? (state.catalog ? Promise.resolve(state.catalog) : showCanonical()); },
    showCanonical, showScenario,
    requestCloseEditor: () => editor.requestClose(),
    open: key => { const opened = state.open(key, true); if (opened) { render(); focusDetail(); } return opened; }
  };
}
