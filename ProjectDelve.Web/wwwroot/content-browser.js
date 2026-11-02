import { ContentBrowserState, contentLabel, contentUsage } from './content-browser-state.js';
import { mountContentEditor } from './content-editor.js';
import { LibraryContentDraft } from './content-library-state.js';
import { mountContentPromotion } from './content-promotion.js';

// Read-only viewer: only explicit catalog / Designer snapshot requests are made.
export function mountContentBrowser(root, options = {}) {
  const state = new ContentBrowserState();
  const request = options.fetch ?? globalThis.fetch.bind(globalThis);
  const document = root.ownerDocument ?? globalThis.document;
  const cache = new Map();
  let context = 'canonical', draftName = 'Designer draft', pending = null, requestVersion = 0;
  let error = '', retry = null;
  let destination = null, scenarioSnapshot = null, catalogRevision = null, editDestination = null, editRevision = null;
  let scenarioDestination = null, libraryDraft = null, libraryPackages = [], providers = [], revisions = [];
  let libraryBusy = false, libraryValid = false, creatingPackage = false, packageIdentity = '', packageTitle = '', libraryNotice = '', leaveResolve = null;
  let dependencySearch = '', dependencyPackage = '', dependencyRevisions = [];
  let referencePreview = null, referenceTarget = null, referenceStamp = null, referenceBusy = false, updateReference = null, updateRevisions = [];
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
  const promotionPanel = node('section', 'cb-reference-panel'); promotionPanel.id = 'content-promotion-panel';
  const referencePanel = node('section', 'cb-reference-panel'); referencePanel.id = 'content-reference-panel';
  const libraryPanel = node('section', 'cb-library'); libraryPanel.id = 'content-library-workspace';
  const leavePanel = node('div', 'ce-discard cb-package-discard'); leavePanel.hidden = true;
  leavePanel.setAttribute('role', 'alertdialog'); leavePanel.setAttribute('aria-label', 'Unsaved package changes');
  const keepPackage = button('Keep editing package', () => resolveLeave(false)); keepPackage.id = 'content-library-keep';
  const discardPackage = button('Discard package changes', () => resolveLeave(true)); discardPackage.id = 'content-library-discard';
  leavePanel.append(node('strong', '', 'Discard unsaved package changes?'), node('p', '', 'Saved revisions remain unchanged.'), keepPackage, discardPackage);
  root.replaceChildren(header, leavePanel, libraryPanel, authoring, referencePanel, promotionPanel, status, diagnostics, toolbar, workspace, editorRoot);
  const editor = mountContentEditor(editorRoot, {
    destinationLabel: () => editDestination?.label,
    catalog: () => {
      if (destination?.kind === 'library') return cache.get('library') ?? { entries: [] };
      const entries = new Map();
      for (const catalog of [cache.get('canonical'), state.catalog, cache.get('draft')])
        for (const entry of catalog?.entries ?? []) entries.set(entry.key, entry);
      return { entries: [...entries.values()] };
    },
    validate: async payload => {
      if (editDestination?.kind === 'library') return libraryCall('/authoring/validate', { draft: libraryDraft.request(), edit: payload });
      const response = await request('/api/content/validate', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
      const result = await response.json();
      if (!Array.isArray(result.diagnostics)) throw new Error('Validation could not be completed.');
      return result;
    },
    save: payload => editDestination.save({ ...payload, expectedRevision: editRevision }),
    onSaved: async (saved, content) => {
      if (editDestination?.kind === 'library') await inspectLibrary();
      else await showScenario(saved.definition, saved.name, editDestination);
      state.open(`${content.family}:${content.definition.id}`); render(); focusDetail();
    },
    onClose: () => { root.classList.remove('cb-editing'); detail.querySelector('h3')?.focus({ preventScroll: true }); },
    onDirtyChange: () => {}
  });
  globalThis.addEventListener?.('beforeunload', event => { if (editor.isDirty() || libraryDraft?.dirty) { event.preventDefault(); event.returnValue = ''; } });
  function promotionSource(entry) {
    if (context === 'library' && libraryDraft && !libraryDraft.isNew && !libraryDraft.dirty)
      return { kind: 'library', packageId: entry.packageId, revision: entry.origin === 'library-local' ? libraryDraft.revision : entry.packageRevision };
    if (scenarioDestination && context !== 'canonical')
      return { kind: entry.referenceDigest ? 'scenario-pinned' : 'scenario-local', scenario: scenarioDestination.getSnapshot().definition };
    return null;
  }
  const promotion = mountContentPromotion(promotionPanel, { source: promotionSource, entries: () => state.entries,
    stamp: () => context === 'library' ? libraryDraft?.stamp : scenarioDestination?.getRevision(), call: libraryCall,
    failed: referenceFailure, published: async () => {
      cache.clear(); catalogRevision = null;
      globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed'));
      await showCanonical();
    } });

  function libraryDestination() {
    return {
      kind: 'library', get label() { return `Library package · ${libraryDraft.package.title || libraryDraft.package.id} · ${libraryDraft.isNew ? 'unsaved' : `revision ${libraryDraft.revision}`}`; },
      getRevision: () => libraryDraft.stamp, getContext: () => libraryDraft.context(),
      save: async payload => { libraryDraft.apply(payload); return libraryDraft.request(); }
    };
  }
  async function libraryCall(path, body, method = 'POST') {
    const response = await request(`/api/content-library${path}`, body === undefined ? undefined : {
      method, headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(body)
    });
    const data = response.status === 204 ? null : await response.json();
    if (!response.ok) {
      const failure = new Error(data.detail ?? data.error ?? 'The library operation could not be completed.');
      failure.diagnostics = data.diagnostics; throw failure;
    }
    return data;
  }
  function referenceFailure(failure) { error = failure.message; packageIssue(failure); render(); }
  async function previewReference(packageId, revision, selected, expectedReferenceDigest = null) {
    if (!scenarioDestination?.applyReferences || !await editor.requestClose()) return;
    referenceTarget = scenarioDestination; referenceStamp = referenceTarget.getRevision(); referenceBusy = true; error = ''; referencePreview = null; render();
    try {
      referencePreview = await libraryCall('/scenario-reference/preview', { scenario: referenceTarget.getSnapshot().definition,
        packageId, revision: Number(revision), selected, expectedReferenceDigest });
    } catch (failure) { referenceFailure(failure); }
    finally { referenceBusy = false; render(); referencePanel.scrollIntoView?.({ block: 'start' }); }
  }
  async function chooseUpdate(reference) {
    referencePreview = null; updateReference = reference; updateRevisions = []; referenceBusy = true; error = ''; render();
    try {
      const catalog = await libraryCall('');
      if (catalog.libraryId !== reference.libraryId) throw new Error('Library provenance is unavailable. The embedded scenario remains playable.');
      updateRevisions = await libraryCall(`/packages/${encodeURIComponent(reference.packageId)}/revisions`);
    } catch (failure) { referenceFailure(failure); }
    finally { referenceBusy = false; render(); referencePanel.scrollIntoView?.({ block: 'start' }); }
  }
  function renderReferences() {
    referencePanel.replaceChildren();
    const references = scenarioDestination?.getContext().contentReferences ?? [];
    referencePanel.hidden = !referenceBusy && !referencePreview && !updateReference && !(context === 'draft' && references.length);
    if (context === 'draft') for (const reference of references) {
      const row = node('div', 'cb-reference-row');
      row.append(node('span', '', `${reference.packageId} · pinned revision ${reference.revision}`),
        button('Update Reference…', () => void chooseUpdate(reference))); referencePanel.append(row);
    }
    if (referenceBusy) referencePanel.append(node('p', '', 'Preparing the reference preview…'));
    if (updateReference && !referencePreview) {
      referencePanel.append(node('h3', '', 'Update Reference'), node('p', '', `Current pin: ${updateReference.packageId} · revision ${updateReference.revision}`));
      const versions = select('Available library revision', () => {}); versions.id = 'content-reference-revision';
      versions.append(...updateRevisions.map(record => option(record.revision, `Revision ${record.revision}${record.revision === updateReference.revision ? ' · pinned' : ''}`)));
      versions.value = String(updateRevisions.at(-1)?.revision ?? '');
      const preview = button('Preview update', () => void previewReference(updateReference.packageId, versions.value, updateReference.selected, updateReference.snapshotDigest)); preview.id = 'content-reference-preview-update'; preview.disabled = referenceBusy || !updateRevisions.length;
      referencePanel.append(label('Available revisions', versions), preview);
    }
    if (referencePreview) {
      const pin = referencePreview.reference;
      referencePanel.append(node('h3', '', 'Use content in scenario'), node('p', '', `${pin.packageId} · revision ${pin.revision} · ${referencePreview.changes.length} selected/dependency definitions`));
      for (const change of referencePreview.changes) {
        const family = { Equipment: 'equipment', LootCard: 'lootCards', Unit: 'units', Item: 'items', Resource: 'resources', Terrain: 'terrain', FixedComponent: 'fixedComponents' }[change.identity.family];
        const effective = referencePreview.catalog?.entries?.find(entry => entry.family === family && entry.id === change.identity.typeId);
        if (effective && change.change !== 'Unchanged' && change.after) {
          referencePanel.append(node('h4', '', `Effective after apply · ${effective.name}`), node('p', '', effective.summary));
          for (const ability of effective.abilities ?? []) referencePanel.append(node('p', '', `${ability.name} · ${contentUsage(ability.usage)} · ${ability.description}`));
        }
        const row = node('details'); row.append(node('summary', '', `${change.change} · ${change.identity.family} · ${change.identity.typeId}`));
        row.dataset.referenceChange = change.change;
        if (change.before) row.append(node('h4', '', 'Before'), node('pre', '', JSON.stringify(change.before, null, 2)));
        if (change.after) row.append(node('h4', '', 'After'), node('pre', '', JSON.stringify(change.after, null, 2)));
        referencePanel.append(row);
      }
      const apply = button('Apply reference to scenario', async () => {
        if (referenceBusy) return;
        try {
          referenceBusy = true; render();
          const saved = await referenceTarget.applyReferences({ references: referencePreview.references, expectedRevision: referenceStamp });
          const selected = referencePreview.reference.selected[0]; referencePreview = null; updateReference = null;
          await showScenario(saved.definition, saved.name, referenceTarget);
          state.open(`${selected.family === 'Equipment' ? 'equipment' : 'lootCards'}:${selected.typeId}`); render(); focusDetail();
        } catch (failure) { referenceFailure(failure); }
        finally { referenceBusy = false; render(); }
      }); apply.id = 'content-reference-apply'; apply.disabled = referenceBusy; referencePanel.append(apply);
    }
    if (referencePreview || updateReference) referencePanel.append(button('Cancel reference preview', () => { referencePreview = null; updateReference = null; render(); }));
  }
  function packageIssue(failure) {
    libraryNotice = failure.message;
    const issues = failure.diagnostics ?? [];
    if (issues.length) state.catalog = { ...(state.catalog ?? { entries: [], categories: [] }), diagnostics: issues.map(issue => ({
      path: [issue.source?.path, issue.path].filter(Boolean).join('.'), message: issue.message, code: issue.code
    })) };
  }
  async function inspectLibrary() {
    if (!libraryDraft) return;
    context = 'library'; destination = libraryDraft.editable ? libraryDestination() : null;
    const version = ++requestVersion; libraryBusy = true; libraryValid = false; render();
    try {
      const data = libraryDraft.editable ? await libraryCall('/authoring/context', libraryDraft.request())
        : await libraryCall(`/packages/${encodeURIComponent(libraryDraft.package.id)}/context?revision=${libraryDraft.revision}`);
      if (version !== requestVersion) return;
      const selected = state.selectedKey;
      cache.set('library', data); state.load(data); if (data.entries.some(entry => entry.key === selected)) state.open(selected);
      libraryValid = true; libraryNotice = '';
      if (libraryDraft.editable) catalogRevision = libraryDraft.stamp;
    } catch (failure) { if (version === requestVersion) packageIssue(failure); }
    finally { if (version === requestVersion) { libraryBusy = false; render(); } }
  }
  async function showLibrary() {
    const version = ++requestVersion; context = 'library'; pending = null; libraryBusy = true; error = ''; render();
    try {
      const [catalog, installed] = await Promise.all([libraryCall(''), libraryCall('/providers')]);
      if (version !== requestVersion) return;
      libraryPackages = catalog.packages; providers = installed;
      if (libraryDraft) { libraryBusy = false; return await inspectLibrary(); }
      destination = null; state.load({ entries: [], categories: [], diagnostics: [] });
    } catch (failure) { packageIssue(failure); }
    finally { if (version === requestVersion) { libraryBusy = false; render(); } }
  }
  async function openPackage(id, revision) {
    if (libraryBusy || !await editor.requestClose() || !await guardPackage()) { render(); return false; }
    libraryBusy = true; render();
    try {
      const [record, history, catalog] = await Promise.all([libraryCall(`/packages/${encodeURIComponent(id)}${revision ? `?revision=${revision}` : ''}`), libraryCall(`/packages/${encodeURIComponent(id)}/revisions`), libraryCall('?includeArchived=true')]);
      libraryPackages = catalog.packages;
      const head = libraryPackages.find(row => row.packageId === id);
      libraryDraft = new LibraryContentDraft(record, { currentRevision: head?.currentRevision ?? record.revision, archived: head?.archived });
      revisions = history; creatingPackage = false;
    } catch (failure) { packageIssue(failure); error = failure.message; return false; }
    finally { libraryBusy = false; render(); }
    await inspectLibrary(); render(); return true;
  }
  function resolveLeave(discard) {
    leavePanel.hidden = true; const resolve = leaveResolve; leaveResolve = null;
    if (discard && libraryDraft) {
      if (libraryDraft.isNew) { libraryDraft = null; destination = null; cache.delete('library'); }
      else libraryDraft.reset();
      cache.delete('library'); libraryValid = false;
    }
    render(); resolve?.(discard);
  }
  async function guardPackage() {
    if (!libraryDraft?.dirty) return true;
    if (leaveResolve || libraryBusy) return false;
    leavePanel.hidden = false; keepPackage.focus();
    return new Promise(resolve => { leaveResolve = resolve; });
  }
  async function savePackage() {
    if (!libraryDraft?.editable || libraryBusy || !await editor.requestClose()) return;
    libraryBusy = true; libraryNotice = ''; render();
    try {
      const payload = libraryDraft.request();
      // This is the same revision/validation transaction used by the library service.
      const saved = await libraryCall(libraryDraft.isNew ? '/packages' : `/packages/${encodeURIComponent(payload.package.id)}`, payload, libraryDraft.isNew ? 'POST' : 'PUT');
      libraryDraft.load(saved); revisions = await libraryCall(`/packages/${encodeURIComponent(saved.package.id)}/revisions`);
      libraryPackages = (await libraryCall('')).packages;
      libraryBusy = false; await inspectLibrary(); libraryNotice = `Saved revision ${saved.revision}.`;
    } catch (failure) { packageIssue(failure); }
    finally { libraryBusy = false; render(); }
  }
  function libraryInput(id, value, change, type = 'text') {
    const control = node('input'); control.id = id; control.type = type; control.value = value ?? '';
    control.addEventListener('input', () => change(control.value)); return control;
  }
  function renderLibrary() {
    libraryPanel.hidden = context !== 'library'; libraryPanel.replaceChildren();
    if (context !== 'library') return;
    const tools = node('div', 'cb-library-tools');
    const packages = select('Library package', id => { if (id) void openPackage(id); }); packages.id = 'content-library-package';
    packages.append(option('', 'Select a package'), ...libraryPackages.map(row => option(row.packageId, `${row.title || row.packageId} · ${row.packageId} · revision ${row.currentRevision}`)));
    if (libraryDraft?.isNew) packages.append(option(libraryDraft.package.id, `${libraryDraft.package.title || libraryDraft.package.id} · unsaved`));
    packages.value = libraryDraft?.package.id ?? ''; packages.disabled = libraryBusy;
    const create = button('Create package', async () => { if (!await editor.requestClose() || !await guardPackage()) return; creatingPackage = true; packageIdentity = ''; packageTitle = ''; render(); }); create.id = 'content-library-new'; create.disabled = libraryBusy;
    tools.append(label('Library package', packages), create); libraryPanel.append(tools);
    if (libraryDraft && !libraryDraft.isNew) { const reload = button('Reload current revision', () => void openPackage(libraryDraft.package.id)); reload.id = 'content-library-reload'; reload.disabled = libraryBusy; tools.append(reload); }
    if (creatingPackage) {
      const creation = node('div', 'cb-library-create');
      creation.append(label('Package ID', libraryInput('content-library-new-id', packageIdentity, value => { packageIdentity = value; })), label('Title', libraryInput('content-library-new-title', packageTitle, value => { packageTitle = value; })));
      const start = button('Start package', async () => {
        libraryDraft = LibraryContentDraft.create(packageIdentity.trim(), packageTitle.trim(), providers.map(provider => provider.id));
        creatingPackage = false; revisions = []; state.load({ entries: [], categories: [], diagnostics: [] }); await inspectLibrary();
      }); start.id = 'content-library-start';
      creation.append(start, button('Cancel', () => { creatingPackage = false; render(); })); libraryPanel.append(creation);
    }
    if (!libraryDraft) { libraryPanel.append(empty('Your Content Library is ready. Create a package to author Equipment and Loot Cards without a scenario.')); return; }
    const draft = libraryDraft;
    libraryPanel.append(node('p', 'cb-library-origin', `Library · ${draft.package.id} · ${draft.isNew ? 'new package' : `revision ${draft.revision}`} · ${draft.editable ? 'editable' : 'historical revision · read only'}${draft.dirty ? ' · unsaved changes' : ''}`));
    if (draft.origin) libraryPanel.append(node('small', 'cb-muted', `Origin library ${draft.origin}`));
    const metadata = node('div', 'cb-library-metadata');
    const id = libraryInput('content-library-id', draft.package.id, value => { draft.package.id = value; draft.touch(); markPackageDirty(); }); id.readOnly = !draft.isNew; id.disabled = libraryBusy || !draft.editable;
    const title = libraryInput('content-library-title', draft.package.title, value => { draft.package.title = value; draft.touch(); markPackageDirty(); });
    const description = libraryInput('content-library-description', draft.package.description, value => { draft.package.description = value; draft.touch(); markPackageDirty(); });
    title.disabled = description.disabled = !draft.editable || libraryBusy;
    metadata.append(label('Package ID', id), label('Title', title), label('Description', description));
    if (revisions.length) {
      const history = select('Package revision', value => void openPackage(draft.package.id, Number(value))); history.id = 'content-library-revision';
      history.append(...revisions.map(record => option(record.revision, `Revision ${record.revision}${record.revision === libraryPackages.find(p => p.packageId === draft.package.id)?.currentRevision ? ' · current' : ' · read only'}`)));
      history.value = String(draft.revision); history.disabled = libraryBusy; metadata.append(label('Revision history', history));
    }
    libraryPanel.append(metadata);
    const dependencies = node('details', 'cb-library-dependencies'); dependencies.append(node('summary', '', 'Dependencies · canonical providers and pinned library revisions'));
    for (const provider of providers) {
      const check = node('input'); check.type = 'checkbox'; check.checked = draft.package.dependencies?.includes(provider.id); check.disabled = !draft.editable || libraryBusy;
      check.dataset.provider = provider.id;
      check.addEventListener('change', () => { draft.selectDependency(provider.id, check.checked); void inspectLibrary(); });
      const row = node('label'); row.append(check, node('span', '', `Canonical · ${provider.title}`)); dependencies.append(row);
    }
    for (const binding of draft.bindings) {
      const row = node('div', 'cb-library-binding'); row.append(node('span', '', `${binding.packageId} · pinned revision ${binding.revision}`));
      if (draft.editable) row.append(button('Remove dependency', () => { draft.bind(binding.packageId, null); void inspectLibrary(); })); dependencies.append(row);
    }
    if (draft.editable) {
      const searchPackages = libraryInput('content-library-dependency-search', dependencySearch, value => { dependencySearch = value; renderDependencyChoices(); }, 'search');
      const packageChoice = select('External library package', async value => {
        try { dependencyPackage = value; dependencyRevisions = value ? await libraryCall(`/packages/${encodeURIComponent(value)}/revisions`) : []; }
        catch (failure) { packageIssue(failure); }
        render(); const panel = libraryPanel.querySelector('.cb-library-dependencies'); if (panel) panel.open = true;
      }); packageChoice.id = 'content-library-dependency-package';
      function renderDependencyChoices() {
        const words = dependencySearch.trim().toLowerCase();
        packageChoice.replaceChildren(option('', 'Choose an external package'), ...libraryPackages.filter(p => p.packageId !== draft.package.id && `${p.title} ${p.packageId}`.toLowerCase().includes(words)).map(p => option(p.packageId, `${p.title || p.packageId} · ${p.packageId}`)));
        packageChoice.value = dependencyPackage;
      }
      renderDependencyChoices();
      const revision = select('External package revision', () => {}); revision.id = 'content-library-dependency-revision';
      revision.append(...dependencyRevisions.map(record => option(record.revision, `Revision ${record.revision}`))); revision.value = String(dependencyRevisions.at(-1)?.revision ?? '');
      const add = button('Use selected revision', () => { if (!dependencyPackage || !revision.value) return; draft.bind(dependencyPackage, Number(revision.value)); void inspectLibrary(); }); add.id = 'content-library-add-dependency';
      dependencies.append(label('Find library packages', searchPackages), label('External package', packageChoice), label('Pinned revision', revision), add);
    }
    libraryPanel.append(dependencies);
    if (draft.editable) {
      const actions = node('div', 'cb-library-actions');
      const save = button('Save package', () => void savePackage()); save.id = 'content-library-save'; save.disabled = libraryBusy || !draft.dirty;
      const validate = button('Validate package', () => void inspectLibrary()); validate.id = 'content-library-validate'; validate.disabled = libraryBusy;
      const cancel = button('Cancel package changes', async () => { if (await editor.requestClose() && await guardPackage()) { if (libraryDraft) await inspectLibrary(); else await showLibrary(); } }); cancel.id = 'content-library-cancel'; cancel.disabled = libraryBusy;
      actions.append(save, validate, cancel); libraryPanel.append(actions);
    }
    if (libraryBusy || libraryNotice) { const notice = node('p', 'cb-library-notice', libraryBusy ? 'Checking library content…' : libraryNotice); notice.setAttribute('role', 'status'); libraryPanel.append(notice); }
  }
  function markPackageDirty() {
    const save = libraryPanel.querySelector('#content-library-save'); if (save) save.disabled = libraryBusy || !libraryDraft?.dirty;
    const origin = libraryPanel.querySelector('.cb-library-origin'); if (origin && libraryDraft?.dirty && !origin.textContent.includes('unsaved changes')) origin.textContent += ' · unsaved changes';
  }

  async function refreshDestination() {
    if (destination?.kind === 'library') return;
    if (!destination || catalogRevision === destination.getRevision()) return;
    const snapshot = destination.getSnapshot();
    await showScenario(snapshot.definition, snapshot.name, destination);
  }
  function canAuthor() { return !!destination && !pending && !libraryBusy && (destination.kind !== 'library' || libraryValid) && state.entries.some(entry => entry.mechanic); }
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
    if (specification.copy && destination.kind === 'library') specification.newId = `copy-${globalThis.crypto?.randomUUID?.() ?? Math.random().toString(36).slice(2)}`;
    const opened = await editor.open({ ...specification, context: destination.getContext() });
    if (opened) { root.classList.add('cb-editing'); editorRoot.scrollIntoView?.({ block: 'start' }); }
    return opened;
  }
  async function switchContext(next) {
    if (!await editor.requestClose()) { contextControl.value = context; return; }
    await refreshDestination();
    if (next === 'library') await showLibrary();
    else if (next === 'canonical') await showCanonical();
    else { destination = scenarioDestination; if (next === 'scenario') await showFullScenario(); else await showCachedDraft(); }
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
      { label: 'Execution requirements', value: mechanic.contextRequirements },
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
    if (entry.packageId) detail.append(empty(entry.origin === 'library-local' && destination?.kind === 'library'
      ? `Package ${entry.packageId} · working copy${libraryDraft.revision ? ` of revision ${libraryDraft.revision}` : ' · not saved'}`
      : `Package ${entry.packageId} · revision ${entry.packageRevision}`));
    if (entry.referenceDigest) {
      const reference = scenarioDestination?.getContext().contentReferences?.find(r => r.snapshotDigest === entry.referenceDigest);
      if (reference) {
        detail.append(button('Update Reference…', () => void chooseUpdate(reference), 'cb-reference-update'));
        detail.append(button('View Library provenance', async () => {
          try {
            if ((await libraryCall('')).libraryId !== reference.libraryId) throw new Error('Original Library unavailable; the embedded scenario remains playable.');
            await openPackage(reference.packageId, reference.revision);
          } catch (failure) { referenceFailure(failure); }
        }, 'cb-reference-source'));
      }
    }
    if (entry.family === 'equipment' || entry.family === 'lootCards') {
      if (!entry.published && entry.origin !== 'canonical' && promotionSource(entry)) {
        const promote = button('Promote to Canonical', () => promotion.begin(entry)); promote.id = 'content-promote';
        promote.disabled = libraryBusy || !!libraryDraft?.dirty || editor.isOpen(); detail.append(promote);
      }
      const actions = node('div', 'cb-definition-actions');
      const copy = button('Copy as new', () => void openEditor({ copy: true }, entry.key)); copy.id = 'content-copy-definition'; copy.disabled = !canAuthor(); actions.append(copy);
      const contextField = entry.family === 'equipment' ? 'equipmentTypes' : 'lootCardDefinitions';
      if (!entry.published && destination && entry.origin !== 'canonical' && entry.origin !== 'library-pinned' && entry.availability !== 'legacy-collision' && destination.getContext()[contextField]?.some(definition => definition.id === entry.id)) {
        const edit = button('Edit definition', () => void openEditor({ existingId: entry.id }, entry.key)); edit.id = 'content-edit-definition'; edit.disabled = !canAuthor(); actions.append(edit);
      }
      if (entry.family === 'equipment') { const loot = button('Create Loot Card for this Equipment', () => void openEditor({}, entry.key, true)); loot.id = 'content-create-linked-loot'; loot.disabled = !canAuthor(); actions.append(loot); }
      if (entry.availability === 'legacy-collision') actions.append(empty('This historical local ID overlaps canonical content. Copy it with a new ID to edit it.'));
      detail.append(actions);
      if (entry.published) detail.append(empty('Published to Canonical. This retained source is read-only; Copy as new creates an independent identity.'));
      if (entry.packageId && ['library-local', 'library-dependency'].includes(entry.origin)) {
        const use = button('Use in Scenario', () => void previewReference(entry.packageId,
          entry.origin === 'library-local' ? libraryDraft.revision : entry.packageRevision,
          [{ family: entry.family === 'equipment' ? 'Equipment' : 'LootCard', typeId: entry.id }]));
        use.id = 'content-use-in-scenario'; use.disabled = referenceBusy || libraryBusy || !scenarioDestination?.applyReferences || !!libraryDraft?.dirty;
        detail.append(use, empty(!scenarioDestination?.applyReferences ? 'In Scenario Designer, choose Use library content to attach a draft.'
          : libraryDraft?.dirty ? 'Save or cancel package changes before using a saved revision.' : `Scenario destination: ${scenarioDestination.label ?? draftName}`));
      }
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
    renderLibrary();
    renderReferences();
    root.classList.toggle('cb-show-detail', state.detailOpen);
    root.setAttribute('aria-busy', String(!!pending || libraryBusy));
    contextControl.disabled = !!pending || libraryBusy || referenceBusy;
    contextControl.replaceChildren(option('canonical', 'Canonical catalog'));
    contextControl.append(option('library', 'Library'));
    if (cache.has('draft') || context === 'draft') contextControl.append(option('draft', `Scenario draft · ${draftName}`));
    if (scenarioDestination && scenarioSnapshot) contextControl.append(option('scenario', `Full scenario inspection · ${draftName}`));
    contextControl.value = context;
    contextNote.textContent = context === 'library' ? 'Persistent content packages · no scenario required' : context === 'canonical' ? 'Canonical definitions · read only'
      : destination && context === 'draft' ? 'Equipment, Items, Loot Cards and mechanics for this destination. Definitions are saved only to the attached draft.'
      : 'Effective definitions for this Designer snapshot · read only. Local content stays scoped to this draft.';
    returnLink.hidden = context === 'canonical' || context === 'library';
    newEquipment.disabled = !canAuthor(); newLoot.disabled = !canAuthor();
    destinationNote.replaceChildren();
    if (destination) destinationNote.textContent = `Save destination: ${destination.label ?? draftName}`;
    else if (context === 'library' && libraryDraft && !libraryDraft.editable) destinationNote.textContent = 'Historical revision · read only. Select the current revision to edit.';
    else { destinationNote.append(node('span', '', 'Select Library to create standalone content, or ')); const link = node('a', '', 'open a draft in Designer'); link.href = '#designer'; destinationNote.append(link, node('span', '', ' and choose Content.')); }
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
      row.disabled = libraryBusy;
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
    libraryBusy = false;
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
    destination = target; scenarioDestination = target; scenarioSnapshot = snapshot; catalogRevision = target?.getRevision() ?? null;
    cache.delete('draft'); cache.delete('scenario');
    return load('draft', target ? '/api/content/context' : '/api/content/inspect', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(target ? target.getContext() : snapshot) }, true);
  }
  render();
  return {
    show: async () => { if (context === 'library' && !editor.isOpen()) return showLibrary(); if (destination && !editor.isOpen()) await refreshDestination(); return pending ?? (state.catalog ? Promise.resolve(state.catalog) : showCanonical()); },
    showCanonical, showScenario, showLibrary,
    requestCloseEditor: async () => { if (!await editor.requestClose() || !await guardPackage()) return false; return true; },
    open: key => { const opened = state.open(key, true); if (opened) { render(); focusDetail(); } return opened; }
  };
}
