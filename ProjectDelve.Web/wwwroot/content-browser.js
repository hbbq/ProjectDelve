import { ContentBrowserState, contentLabel, contentUsage } from './content-browser-state.js';
import { mountContentEditor } from './content-editor.js';
import { boardVisuals } from './board-visuals.js';

const definitionFields = { units: 'unitTypes', equipment: 'equipmentTypes', items: 'itemTypes', lootCards: 'lootCardDefinitions', terrain: 'terrainTypes', terrainAssemblies: 'terrainAssemblies' };
const families = { units: 'Unit', equipment: 'Equipment', items: 'Item', lootCards: 'LootCard', terrain: 'Terrain', terrainAssemblies: 'Terrain' };
export function mountContentBrowser(root, options = {}) {
  const document = root.ownerDocument ?? globalThis.document;
  const request = options.fetch ?? globalThis.fetch.bind(globalThis);
  const state = new ContentBrowserState();
  let refreshGeneration = 0, loading = false, contentReady = false;
  let context = 'canonical', target = null, snapshot = null, workspace = null, catalog = null, editScope = null, editToken = null, editCopy = false;
  const node = (tag, text, className) => { const e = document.createElement(tag); if (text != null) e.textContent = text; if (className) e.className = className; return e; };
  function appendPortrait(container, entry) {
    const source = entry?.family === 'units' ? boardVisuals.unit[entry.id]?.image : null;
    if (!source) return;
    const image = node('img', null, 'unit-portrait'); image.src = source; image.alt = '';
    image.addEventListener('error', () => { image.hidden = true; });
    container.append(image);
  }
  const button = (text, action, id) => { const e = node('button', text); e.type = 'button'; if (id) e.id = id; e.addEventListener('click', () => { if (!e.disabled) void run(action); }); return e; };
  const authoringButton = (text, action, id) => {
    const e = button(text, () => { if (contentReady && !loading) return action(); }, id);
    e.disabled = !contentReady || loading; return e;
  };
  const select = (id, values, change) => { const e = node('select'); e.id = id; e.setAttribute('aria-label', id); for (const [value, text] of values) { const o = node('option', text); o.value = value; e.append(o); } e.addEventListener('change', () => void run(() => change(e.value))); return e; };
  root.classList.add('content-browser');
  const header = node('header', null, 'cb-header'); header.append(node('h2', 'Content Workspace'));
  const contexts = select('content-context', [['canonical','Canonical'], ['workspace','Workspace'], ['draft','Scenario-local']], async value => { if (await editor.requestClose()) { context = value; await refresh(); } else contexts.value = context; }); header.append(contexts);
  const status = node('p'); status.setAttribute('role', 'status');
  const actions = node('div', null, 'cb-authoring');
  const toolbar = node('div', null, 'cb-toolbar');
  const search = node('input'); search.id = 'content-search'; search.type = 'search'; search.placeholder = 'Search by name or ID'; search.addEventListener('input', () => { state.filter({query:search.value}); render(); });
  const category = select('content-category', [['all','All categories']], value => { state.filter({category:value}); render(); }); const origin = select('content-origin',[['all','All origins']],value=>{state.filter({origin:value});render();}); toolbar.append(search, category,origin);
  const body = node('div', null, 'cb-workspace');
  const list = node('aside', null, 'cb-master cb-list'); list.id = 'content-list';
  const detail = node('article', null, 'cb-detail'); detail.id = 'content-detail'; body.append(list,detail);
  const promotion = node('section', null, 'cb-reference-panel'); promotion.id = 'content-promotion-panel'; promotion.hidden=true;
  const management = node('section', null, 'cb-management'); management.hidden = true;
  let managementMode = null, managementData = null, selectedBatch = new Map();
  const editorRoot = node('section'); root.replaceChildren(header, actions, status, management, promotion, toolbar, body, editorRoot);
  async function call(url, body, method = 'POST') {
    const response = await request(url, body === undefined ? undefined : {method,headers:{'Content-Type':'application/json'},body:JSON.stringify(body)});
    const data = response.status === 204 ? null : await response.json();
    if (!response.ok) throw new Error(data.detail ?? data.message ?? data.diagnostics?.map(d => `${d.path ?? ''}: ${d.message}`).join('\n') ?? data.error ?? 'Content operation failed.');
    return data;
  }
  async function run(action) { try { status.textContent = ''; await action(); } catch(error) { status.textContent = error.message; } }
  const workspaceContext = () => Object.fromEntries([...Object.values(definitionFields), 'fixedComponentTypes'].map(field => [field, structuredClone(workspace?.definitions?.[field] ?? [])]));
  const editor = mountContentEditor(editorRoot, {
    destinationLabel: () => editScope === 'workspace' ? 'Workspace' : 'Scenario-local',
    catalog: () => catalog,
    validate: payload => editCopy && catalog.entries.some(e=>e.family === payload.family && e.id === payload.definition.id)
      ? Promise.resolve({valid:false,diagnostics:[{path:'definition.id',code:'duplicateIdentity',message:'A copy requires a new Type ID.'}]})
      : call('/api/content/validate', { ...payload, destination: editScope === 'workspace' ? 'workspace' : 'scenario' }),
    save: async payload => {
      if (editScope !== 'workspace') return target.save({...payload, expectedRevision:editToken});
      const definitions = structuredClone(workspace.definitions); const field = definitionFields[payload.family];
      definitions[field] = (definitions[field] ?? []).filter(d => d.id !== payload.existingId);
      definitions[field].push(payload.definition);
      workspace = await call('/api/content-workspace', {definitions, expectedToken:editToken}, 'PUT');
      globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed'));
      return workspace;
    },
    onSaved: async (_, content) => { context=editScope === 'workspace' ? 'workspace' : 'draft'; await refresh(); state.open(`${content.family}:${content.definition.id}`); render(); },
    onClose: () => root.classList.remove('cb-editing')
  });
  globalThis.addEventListener?.('beforeunload', event => { if(editor.isDirty()) {event.preventDefault();event.returnValue='';} });
  async function refresh() {
    managementMode = null; management.hidden = true; toolbar.hidden = false; body.hidden = false; selectedBatch.clear();
    const generation = ++refreshGeneration;
    loading = true; contentReady = false;
    status.textContent = 'Loading content…';
    promotion.replaceChildren(); promotion.hidden=true;
    state.load({entries:[]}); render();
    try {
      let loadedCatalog;
      const loadedWorkspace = await call('/api/content-workspace');
      if(generation !== refreshGeneration) return;
      contexts.value = context;
      if (context === 'draft') {
        if (!target && !snapshot) { status.textContent = 'Open a scenario in Designer to edit scenario-local content.'; return; }
        loadedCatalog = await call(target ? '/api/content/context' : '/api/content/inspect', target ? target.getContext() : snapshot);
      } else loadedCatalog = await call(context === 'workspace' ? '/api/content-workspace/catalog' : '/api/content/catalog');
      if(generation !== refreshGeneration) return;
      workspace = loadedWorkspace; catalog = loadedCatalog; contentReady = true;
      state.load(catalog); search.value = '';
      category.replaceChildren(); for (const c of [{id:'all',name:'All categories'},...state.categories]) { const o=node('option',c.name); o.value=c.id; category.append(o); }
      origin.replaceChildren();for(const value of ['all',...state.origins]) {const o=node('option',value === 'all' ? 'All origins' : contentLabel(value));o.value=value;origin.append(o);}
      status.textContent = '';
    } catch (error) {
      if (generation === refreshGeneration) throw error;
    } finally {
      if (generation === refreshGeneration) { loading = false; render(); }
    }
  }
  function definition(entry) { const value = entry.authoredDefinition ?? entry.definition; return entry.family === 'units' ? value.unitType ?? value : value; }
  async function open(entry, copy = false, scope = context === 'draft' ? 'draft' : 'workspace', override = false, family = entry?.family) {
    if (!contentReady || loading) return;
    if (!await editor.requestClose() || !contentReady || loading) return;
    if (scope === 'draft' && !target) throw new Error('Open an editable Designer scenario first.');
    editCopy = copy; editScope = scope; editToken = scope === 'workspace' ? workspace.token : target.getRevision();
    const ctx = scope === 'workspace' ? workspaceContext() : target.getContext();
    let existingId;
    if (entry && !copy) {
      existingId = entry.id;
      if (override && !ctx[definitionFields[family]].some(d => d.id === entry.id)) ctx[definitionFields[family]].push(structuredClone(definition(entry)));
    }
    await editor.open({family,definition:entry ? definition(entry) : undefined,existingId,copy,
      newId:copy ? `copy-${globalThis.crypto?.randomUUID?.() ?? Date.now()}` : undefined,context:ctx});
    root.classList.add('cb-editing'); editorRoot.scrollIntoView?.({block:'start'});
  }
  async function publish(entry) {
    promotion.hidden=false;
    const selection = [{family:families[entry.family],typeId:entry.id}];
    const request = {expectedToken:workspace.token, selected:selection};
    const preview = await call('/api/content-workspace/promotion/preview', request);
    promotion.replaceChildren(node('h3','Review canonical publication'),node('p',`Add ${preview.newDefinitions?.length ?? 0} definitions. Replace ${preview.replacements?.length ?? 0} canonical definitions.`));
    const current = await call('/api/content/catalog');
    const before = current.entries.filter(e => preview.replacements?.some(id => id.typeId === e.id && families[e.family] === id.family)).map(e => definition(e));
    promotion.append(node('h4','Current canonical definitions'),node('pre',JSON.stringify(before,null,2)),node('h4','Proposed definitions'),node('pre',JSON.stringify(preview.definitions,null,2)));
    if (!preview.canPublish) {
      promotion.append(button('Include required Workspace dependencies and preview again', async () => {
        const batch = {...request, selected:[...selection,...preview.missingSelections]};
        const reviewed = await call('/api/content-workspace/promotion/preview',batch);
        promotion.replaceChildren(node('h3','Review expanded publication batch'),node('pre',JSON.stringify(reviewed,null,2)));
        if (reviewed.canPublish) promotion.append(button('Confirm publication batch to Canonical',async()=>{await call('/api/content-workspace/promotion/publish',{preview:reviewed,confirm:true});globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed'));await refresh();}));
      })); return;
    }
    promotion.append(button('Confirm publication to Canonical', async () => {
      await call('/api/content-workspace/promotion/publish', {preview, confirm:true});
      globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed')); await refresh();
    }, 'content-publish-confirm'));
  }
  const identityFamily = { ...families, lootPresets:'LootPreset', worldCards:'WorldCard', worldDecks:'WorldDeck', terrain:'Terrain', terrainAssemblies:'Terrain', fixedComponents:'FixedComponent', resources:'Resource', machines:'Machine' };
  async function manage(mode) {
    if (!await editor.requestClose()) return;
    management.replaceChildren(); management.hidden = true;
    managementMode = mode; selectedBatch.clear(); promotion.hidden = true;
    managementData = mode === 'workspace' ? await call('/api/content-workspace/catalog')
      : await call('/api/content/loot-management', context === 'draft' && target ? {context:target.getContext()} : {});
    const navigationCatalog = await call(context === 'draft' && target ? '/api/content/context' : '/api/content-workspace/catalog', context === 'draft' && target ? target.getContext() : undefined);
    catalog = navigationCatalog; state.load(catalog);
    toolbar.hidden = true; body.hidden = true; management.hidden = false;
    renderManagement();
  }
  function renderManagement() {
    let previewSequence = 0;
    const heading = node('h3', managementMode === 'workspace' ? 'Workspace Management' : 'Loot Management'); heading.id = `${managementMode}-management-heading`;
    management.replaceChildren(heading);
    for(const diagnostic of managementData.diagnostics ?? [])management.append(node('p',`${diagnostic.path ?? ''}: ${diagnostic.message}`,'cb-missing'));
    const filters = node('div', null, 'cb-toolbar'), results = node('div', null, 'cb-batch-results'), review = node('section', null, 'cb-reference-panel');
    const query = node('input'); query.type = 'search'; query.placeholder = 'Search name or Type ID'; query.setAttribute('aria-label','Management search');
    const family = select('management-family', [['all','All families'], ...(managementMode === 'workspace' ? [...new Set(Object.values(identityFamily))].map(label=>[label,label]) : [['equipment','Equipment'],['items','Items']])], draw);
    const view = select('loot-management-view', [['rewards','Equipment and Items'],['cards','Loot Cards']], () => {selectedBatch.clear(); draw();});
    const has = select('management-loot-filter', [['all','All Loot relationships'],['missing','Missing Loot Card'],['has','Has Loot Card']], draw);
    const origins = select('management-origin', [['all','All origins'],['canonical','Canonical'],['workspace','Workspace'],['scenario','Scenario']],draw);
    const destination = select('loot-batch-destination', [['workspace','Workspace'], ...(context === 'draft' && target?.saveLootBatch ? [['scenario','Scenario-local']] : [])], () => {previewSequence++;review.replaceChildren();});
    family.value = 'all'; view.value = 'rewards'; has.value = 'all'; origins.value = 'all'; destination.value = 'workspace';
    filters.append(query, family);
    if (managementMode !== 'workspace') filters.append(view, has, origins, destination);
    const selection = node('p'); selection.setAttribute('role','status');
    let visible = [];
    const eligible = row => managementMode === 'workspace' || view.value !== 'cards' && !(row.cards?.length);
    const key = row => `${row.family}:${row.typeId}`;
    const choose = () => {for(const row of visible.filter(eligible)) selectedBatch.set(key(row),row); review.replaceChildren(); draw();};
    const batchActions = node('div',null,'cb-authoring');
    batchActions.append(button('Select all visible eligible entries',choose), button('Clear selection',()=>{selectedBatch.clear();review.replaceChildren();draw();}));
    if (managementMode !== 'workspace') batchActions.append(button('Select All Without Loot Cards',()=>{view.value='rewards';has.value='missing';draw();choose();}));
    batchActions.append(button('Preview selected batch',async()=>{
      if (!selectedBatch.size) throw new Error('Select at least one definition.');
      const sequence = ++previewSequence, batchMode = managementMode, batchDestination = destination.value, dataAtPreview = managementData;
      const selectionSnapshot = [...selectedBatch.values()];
      const selected = [...selectedBatch.values()].map(row=>({family:identityFamily[row.family] ?? row.family,typeId:row.typeId}));
      const revision = target?.getRevision();
      const payload = {expectedToken:workspace.token,selected,destination:destination.value,
        ...(context === 'draft' && target ? {context:target.getContext()} : {})};
      const preview = await call(managementMode === 'workspace' ? '/api/content-workspace/promotion/preview' : '/api/content-workspace/loot/preview',managementMode === 'workspace' ? {expectedToken:workspace.token,selected} : payload);
      if(sequence !== previewSequence || dataAtPreview !== managementData || batchMode !== managementMode) return;
      review.replaceChildren(node('h4',batchMode === 'workspace' ? 'Review Canonical publication' : `Review Loot Card creation in ${batchDestination}`));
      for(const row of selectionSnapshot) review.append(node('p',`${row.family} · ${row.typeId} · ${row.name}`));
      if(managementMode === 'workspace') {
        for(const id of preview.newDefinitions ?? [])review.append(node('p',`New Canonical: ${id.family} ${id.typeId}`));
        for(const id of preview.replacements ?? [])review.append(node('p',`Replace Canonical: ${id.family} ${id.typeId}`));
        for(const dependency of preview.dependencies ?? [])review.append(node('p',`${dependency.identity.family} ${dependency.identity.typeId}: ${dependency.status}${dependency.hasWorkspaceOverride && dependency.status === 'canonical' ? ' · Workspace override remains unselected; existing Canonical dependency is used' : ''}`));
        for(const id of preview.missingSelections ?? [])review.append(node('p',`Include required Workspace dependency in selection: ${id.family} ${id.typeId}`,'cb-missing'));
      } else for(const card of preview.definitions?.lootCardDefinitions ?? []) review.append(node('p',`Create Loot Card ${card.id} · ${card.name} → ${card.equipmentTypeId ? 'Equipment '+card.equipmentTypeId : 'Item '+card.itemTypeId}`));
      for(const diagnostic of preview.diagnostics ?? [])review.append(node('p',`${diagnostic.path ?? ''}: ${diagnostic.message}`,'cb-missing'));
      const typed = node('details');typed.append(node('summary','Typed batch preview'),node('pre',JSON.stringify(preview,null,2)));review.append(typed);
      if (preview.canPublish || preview.canCreate) review.append(button('Confirm batch writes',async()=>{
        if(sequence !== previewSequence || dataAtPreview !== managementData || batchMode !== managementMode) throw new Error('Selection or destination changed. Preview again.');
        if (batchMode === 'workspace') await call('/api/content-workspace/promotion/publish',{preview,confirm:true});
        else if (batchDestination === 'scenario') {
          if (revision !== target.getRevision()) throw new Error('Scenario changed. Preview again.');
          const checked = await call('/api/content-workspace/loot/create',{preview,confirm:true});
          await target.saveLootBatch({cards:checked.definitions.lootCardDefinitions,expectedRevision:revision});
        } else await call('/api/content-workspace/loot/create',{preview,confirm:true});
        globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed'));
        await refresh(); await manage(batchMode);
      }, 'management-confirm'));
      review.scrollIntoView?.({block:'nearest'});
    }, 'management-preview'));
    management.append(filters,batchActions,selection,results,review);
    query.addEventListener('input',draw);
    function navigate(family,id) {
      const entry = catalog.entries.find(e=> (e.family === family || identityFamily[e.family] === family) && e.id === id);
      if (!entry) throw new Error('Switch to the effective Workspace or Scenario catalog to inspect this definition.');
      management.hidden=true; toolbar.hidden=false; body.hidden=false; state.open(entry.key,true); render();focusDetail();
    }
    function draw() {
      previewSequence++;
      review.replaceChildren(); results.replaceChildren();
      let rows = managementMode === 'workspace' ? [...new Map(managementData.entries.filter(e=>e.origin.startsWith('workspace') && identityFamily[e.family]).map(e=>({...e,family:identityFamily[e.family],typeId:e.family === 'worldDecks' && e.id === 'default' ? 'default-world-deck' : e.id})).map(e=>[key(e),e])).values()]
        : view.value === 'cards' ? managementData.cards : managementData.rewards;
      visible = (rows ?? []).filter(row=>(family.value === 'all' || (row.family ?? row.rewardFamily) === family.value) && `${row.name} ${row.typeId}`.toLowerCase().includes(query.value.toLowerCase())
        && (managementMode === 'workspace' || origins.value === 'all' || (row.origin ?? row.rewardOrigin)?.startsWith(origins.value))
        && (managementMode === 'workspace' || view.value === 'cards' || has.value === 'all' || (has.value === 'missing' ? !row.cards?.length : row.cards?.length)));
      selection.textContent = `${selectedBatch.size} selected · ${visible.length} visible`;
      for (const row of visible) {
        const card = node('article',null,'cb-batch-row');
        if(eligible(row)) {const label=node('label');const check=node('input');check.type='checkbox';check.checked=selectedBatch.has(key(row));check.setAttribute('aria-label',`Select ${row.family} ${row.typeId}`);check.addEventListener('change',()=>{if(check.checked)selectedBatch.set(key(row),row);else selectedBatch.delete(key(row));draw();});label.append(check,node('span','Select'));card.append(label);}
        card.append(node('strong',row.name),node('code',`${row.family ?? 'LootCard'} · ${row.typeId}`),node('p',row.origin ?? ''));
        if (managementMode === 'workspace') { card.append(node('p',row.origin === 'workspace-override' ? 'Override of Canonical' : 'New definition'),node('p','Valid in current effective Workspace catalog'));
          for(const ref of row.references ?? []) card.append(button(`${ref.label}: ${ref.family} ${ref.id}`,()=>navigate(ref.family,ref.id)));
        } else if(view.value === 'cards') {
          card.append(node('p',`Reward: ${row.rewardFamily ?? 'Immediate effect'} ${row.rewardTypeId ?? ''} · ${row.rewardOrigin ?? ''}`));
          if(row.rewardTypeId)card.append(button('Inspect reward',()=>navigate(row.rewardFamily,row.rewardTypeId)));
          for(const preset of row.presets ?? []) card.append(preset.dynamic ? node('p',`${preset.name} · Dynamic membership`) : button(`${preset.name} · Quantity ${preset.quantity}`,()=>navigate('lootPresets',preset.typeId)));
          for(const diagnostic of row.diagnostics ?? [])card.append(node('p',diagnostic.message ?? diagnostic,'cb-missing'));
        } else {card.append(node('p',row.cards?.length ? `${row.cards.length} Loot Card(s)` : 'Missing Loot Card'));for(const loot of row.cards ?? [])card.append(button(`${loot.name} · ${loot.typeId}`,()=>navigate('lootCards',loot.typeId)));}
        results.append(card);
      }
    }
    draw();
  }
  const legacyNode = (tag, cls, text) => node(tag, text, cls);
  const legacyButton = (text, action, cls) => { const e=button(text,action); e.className=cls ?? ''; return e; };
  const chip = (text, cls='') => node('span',text,`cb-chip ${cls}`);
  const empty = text => node('p',text,'cb-muted');
  const section = (title,...children) => {const e=node('section',null,'cb-section');e.append(node('h4',title),...children);return e;};
  const contentGroup = (title,...children) => {const e=node('details',null,'cb-section cb-content-group');e.open=true;e.append(node('summary',title),...children);return e;};
  function value(value, depth = 0) {
    if (value == null) return legacyNode('span', 'cb-muted', 'None');
    if (typeof value === 'boolean') return legacyNode('span', 'cb-value', value ? 'Yes' : 'No');
    if (typeof value !== 'object') return legacyNode('span', 'cb-value', value);
    if (Array.isArray(value)) {
      if (!value.length) return legacyNode('span', 'cb-muted', 'None');
      const result = legacyNode('ul', 'cb-value-list');
      for (const item of value) { const row = legacyNode('li'); row.append(valueNode(item, depth + 1)); result.append(row); }
      return result;
    }
    const result = legacyNode('dl', 'cb-values');
    for (const [key, item] of Object.entries(value)) {
      const row = legacyNode('div', 'cb-value-row'); const content = legacyNode('dd'); content.append(valueNode(item, depth + 1));
      row.append(legacyNode('dt', '', contentLabel(key)), content); result.append(row);
    }
    return result;
  }
  const valueNode = (data, depth = 0) => value(data, depth);
  function fields(items) {
    const result = legacyNode('dl', 'cb-fields');
    for (const field of items ?? []) {
      const complex = field.value !== null && typeof field.value === 'object' && Object.keys(field.value).length > 0;
      const row = legacyNode('div', complex ? 'cb-field cb-field-complex' : 'cb-field');
      const content = legacyNode('dd'); content.append(valueNode(field.value));
      row.append(legacyNode('dt', '', field.label), content); result.append(row);
    }
    return result;
  }
  function linkTo(key, text, className = 'cb-reference') {
    const result = legacyButton(text, () => { if (state.open(key, true)) { render(); focusDetail(); } }, className);
    result.dataset.contentTarget = key;
    const target = state.entries.find(entry => entry.key === key);
    if (target) { result.dataset.targetId = target.id; result.dataset.targetFamily = target.family; appendPortrait(result, target); }
    return result;
  }
  function focusDetail() { detail.querySelector('h3')?.focus({ preventScroll: true }); detail.scrollIntoView?.({ block: 'nearest' }); }
  function renderParameter(parameter) {
    const card = legacyNode('li', 'cb-parameter');
    const title = legacyNode('div', 'cb-parameter-heading');
    title.append(legacyNode('code', '', parameter.name), chip(contentLabel(parameter.kind)), chip(parameter.required ? 'Required' : 'Optional'));
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
      card.append(legacyNode('h5', '', 'Each item'));
      const children = legacyNode('ul', 'cb-parameters'); children.append(renderParameter(parameter.item)); card.append(children);
    }
    if (parameter.members?.length) {
      card.append(legacyNode('h5', '', 'Object fields'));
      const children = legacyNode('ul', 'cb-parameters'); children.append(...parameter.members.map(renderParameter)); card.append(children);
    }
    return card;
  }
  function renderMechanic(mechanic) {
    const parameters = legacyNode('ul', 'cb-parameters'); parameters.append(...(mechanic.parameters ?? []).map(renderParameter));
    detail.append(section('Authored parameters', mechanic.parameters?.length ? parameters : empty('This mechanic has no authored parameters.')));
    const profiles = legacyNode('div', 'cb-profiles');
    for (const profile of mechanic.profiles ?? []) {
      const row = legacyNode('div', 'cb-profile');
      row.append(legacyNode('strong', '', contentLabel(profile.source)), chip(contentLabel(profile.timing)), legacyNode('span', '', contentLabel(profile.usage)));
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
      const contract = section('Description contract', legacyNode('p', 'cb-rule-text', mechanic.description.template));
      for (const wording of mechanic.description.timingTemplates ?? []) {
        const row = legacyNode('div', 'cb-timing-wording');
        row.append(chip(contentLabel(wording.timing)), legacyNode('p', 'cb-rule-text', wording.template)); contract.append(row);
      }
      const policies = Object.fromEntries(Object.entries(mechanic.description).filter(([key]) => !['template', 'timingTemplates'].includes(key)));
      if (Object.keys(policies).length) {
        const advanced = legacyNode('details', 'cb-wording-policies');
        advanced.append(legacyNode('summary', '', 'Wording override rules'), valueNode(policies)); contract.append(advanced);
      }
      detail.append(contract);
    }
  }
  function renderAbility(ability) {
    const card = legacyNode('section', 'cb-ability');
    card.dataset.abilityKey = ability.key; card.dataset.mechanicId = ability.mechanicId;
    const title = legacyNode('div', 'cb-ability-heading');
    title.append(legacyNode('h5', '', ability.name), chip(contentLabel(ability.timing)), chip(contentUsage(ability.usage)));
    card.append(title, legacyNode('code', 'cb-local-key', `Local key · ${ability.key}`));
    if (ability.description) card.append(legacyNode('p', 'cb-rule-text', ability.description));
    const mechanic = state.mechanic(ability.mechanicId);
    const line = legacyNode('div', 'cb-mechanic-link'); line.append(legacyNode('span', '', 'Mechanic'));
    line.append(mechanic ? linkTo(mechanic.key, mechanic.name) : legacyNode('code', '', ability.mechanicId ?? 'Unavailable'));
    card.append(line);
    if (ability.parameters && Object.keys(ability.parameters).length)
      card.append(legacyNode('h6', '', 'Authored parameters'), valueNode(ability.parameters));
    if (ability.ownerKey) {
      const owner = state.entries.find(entry => entry.key === ability.ownerKey);
      if (owner) card.append(owner.key === state.selectedKey ? legacyNode('p', 'cb-owner', `Owned by ${owner.name}`)
        : linkTo(owner.key, `Owned by ${owner.name}`));
    }
    return card;
  }
  function render() {
    root.setAttribute('aria-busy', String(loading));
    actions.replaceChildren();
    actions.append(authoringButton('Loot Management',()=>manage('loot'),'content-loot-management'),authoringButton('Workspace Management',()=>manage('workspace'),'content-workspace-management'));
    for (const [family,label] of Object.entries({units:'Unit',equipment:'Equipment',items:'Item',lootCards:'Loot Card',terrain:'Terrain',terrainAssemblies:'Terrain Assembly'})) actions.append(authoringButton(`Create ${label}`, () => open(null,false,context === 'draft' ? 'draft':'workspace',false,family), `content-create-${family}`));
    if(context === 'workspace') actions.append(authoringButton('Empty Workspace', async () => {
      promotion.hidden=false;promotion.replaceChildren(node('p','Delete all Workspace definitions?'));
      promotion.append(authoringButton('Confirm empty Workspace',async () => { await call('/api/content-workspace',{expectedToken:workspace.token},'DELETE');globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed')); await refresh(); }));
    }));
    root.classList.toggle('cb-show-detail',state.detailOpen);
    list.replaceChildren();
    for (const entry of state.visible) {const row=button('', () => {state.open(entry.key);render();focusDetail();});row.className='cb-list-entry';appendPortrait(row,entry);row.append(node('span',`${entry.name} \u00b7 ${entry.origin}`));row.dataset.contentKey=entry.key;row.dataset.contentId=entry.id;row.setAttribute('aria-current',String(state.selectedKey===entry.key));list.append(row);}
    detail.replaceChildren(); const entry = state.selected; if(!entry) return;
    detail.append(legacyButton('\u2190 All results',()=>{state.detailOpen=false;render();list.querySelector('[aria-current="true"]')?.focus();},'cb-back-list'));
    if(state.history.length) detail.append(legacyButton('\u2190 Previous definition',()=>{state.back();render();search.value=state.query;focusDetail();},'cb-text-button'));
    const heading=node('h3',entry.name);heading.tabIndex=-1;
    appendPortrait(detail, entry);
    detail.append(heading,node('p',`${entry.id} \u00b7 ${entry.origin}`),node('p',entry.summary));
    for(const group of entry.sections ?? []) if(group.fields?.length) detail.append(contentGroup(group.title,fields(group.fields)));
    if(entry.abilities?.length) detail.append(section(`Owned abilities \u00b7 ${entry.abilities.length}`,...entry.abilities.map(renderAbility)));
    if(entry.mechanic) renderMechanic(entry.mechanic);
    for(const reference of entry.references ?? []) {const target=state.entries.find(e=>e.key===reference.targetKey);detail.append(target && reference.exists ? linkTo(target.key,target.name) : node('span',`${reference.id} \u00b7 unavailable`,'cb-missing'));}
    if(definitionFields[entry.family] && !(entry.family === 'terrain' && entry.definition?.assembly)) {
      detail.append(authoringButton('Create Copy', () => open(entry,true), 'content-copy-definition'));
      if(context === 'canonical') detail.append(authoringButton('Edit in Workspace', () => open(entry,false,'workspace',true),'content-edit-in-workspace'));
      const local = (context === 'workspace' ? workspaceContext() : target?.getContext())?.[definitionFields[entry.family]]?.some(d => d.id === entry.id);
      if(context !== 'canonical' && local) detail.append(authoringButton('Edit definition', () => open(entry), 'content-edit-definition'));
      if(context === 'workspace' && local) {
        detail.append(authoringButton('Delete Workspace definition',async () => {
          const definitions=structuredClone(workspace.definitions); definitions[definitionFields[entry.family]]=definitions[definitionFields[entry.family]].filter(d=>d.id!==entry.id);
          await call('/api/content-workspace',{definitions,expectedToken:workspace.token},'PUT');globalThis.dispatchEvent?.(new Event('delve:canonical-content-changed')); await refresh();
        }));
        detail.append(authoringButton('Promote to Canonical',()=>publish(entry),'content-promote-definition'));
      }
    }
    const advanced=node('details',null,'cb-advanced'); advanced.append(node('summary','Typed definition'),node('pre',JSON.stringify(entry.definition ?? entry.mechanic,null,2))); detail.append(advanced);
  }
  return {
    show: () => run(refresh), showCanonical:async()=>{context='canonical';await run(refresh);},
    showLibrary:async()=>{context='workspace';await run(refresh);},
    showScenario:async(definition,name,destination)=>{snapshot=structuredClone(definition);target=destination;context='draft';await run(refresh);},
    requestCloseEditor:()=>editor.requestClose(), open:key=>{const result=state.open(key,true);render();return result;}
  };
}
