const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const source = name => fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot', name), 'utf8');
const plain = value => JSON.parse(JSON.stringify(value));

test('omitted null usage limits render Unlimited in normalized Engine responses', () => {
  const context = vm.createContext({});
  vm.runInContext(fs.readFileSync(path.join(__dirname, '../ProjectDelve.Web/wwwroot/content-browser-state.js'), 'utf8').replace(/^export /gm, ''), context);
  assert.equal(vm.runInContext('contentUsage({})', context), 'Unlimited');
  assert.equal(vm.runInContext('contentUsage({maxUses:null})', context), 'Unlimited');
  assert.equal(vm.runInContext('contentUsage({maxUses:2})', context), '2 / game');
  assert.equal(vm.runInContext('contentUsage(null)', context), 'Not specified');
});

class Element {
  constructor(tag, document) {
    this.tagName = tag; this.ownerDocument = document; this.children = []; this.dataset = {}; this.attributes = {};
    this.listeners = {}; this.className = ''; this.hidden = false; this.value = ''; this.style = {};
    this.classList = {
      add: name => { this.className = [...new Set([...this.className.split(' '), name])].filter(Boolean).join(' '); },
      remove: name => { this.className = this.className.split(' ').filter(value => value !== name).join(' '); },
      toggle: (name, on) => { const values = new Set(this.className.split(' ').filter(Boolean)); if (on) values.add(name); else values.delete(name); this.className = [...values].join(' '); },
      contains: name => this.className.split(' ').includes(name)
    };
  }
  set textContent(value) { this.text = String(value); this.children = []; }
  get textContent() { return (this.text ?? '') + this.children.map(child => child.textContent).join(''); }
  set innerHTML(_) { throw new Error('The viewer must not inject authored HTML.'); }
  append(...children) { for (const child of children) { child.parentElement = this; this.children.push(child); } }
  replaceChildren(...children) { this.text = ''; this.children = []; this.append(...children); }
  setAttribute(name, value) { this.attributes[name] = String(value); }
  addEventListener(name, action) { this.listeners[name] = action; }
  all(predicate) { return this.children.flatMap(child => [...(predicate(child) ? [child] : []), ...child.all(predicate)]); }
  querySelector(selector) {
    if (selector.startsWith('#')) return this.all(child => child.id === selector.slice(1))[0];
    if (selector.startsWith('.')) return this.all(child => child.classList.contains(selector.slice(1)))[0];
    if (selector === 'h3') return this.all(child => child.tagName === 'h3')[0];
    if (selector === '[aria-current="true"]') return this.all(child => child.attributes['aria-current'] === 'true')[0];
    throw new Error(`Unexpected selector: ${selector}`);
  }
  focus() { this.ownerDocument.activeElement = this; }
  scrollIntoView() { this.scrolled = true; }
}

function harness(fetcher) {
  const calls = [], events = {}, document = { activeElement: null };
  document.createElement = tag => new Element(tag, document);
  const root = document.createElement('main');
  const context = vm.createContext({ document, structuredClone, addEventListener: (name, listener) => { events[name] = listener; }, fetch: async (url, options) => {
    calls.push({ url, options }); return fetcher ? fetcher(url, options) : { ok: true, json: async () => structuredClone(catalog) };
  } });
  vm.runInContext(source('content-browser-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-editor-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-library-state.js').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-promotion.js').replace(/^export /gm, ''), context);
  vm.runInContext('const boardVisuals = { unit: {} };', context);
  vm.runInContext(source('content-editor.js').replace(/^import .*\r?\n/gm, '').replace(/^export /gm, ''), context);
  vm.runInContext(source('content-browser.js').replace(/^import .*\r?\n/gm, '').replace(/^export /gm, ''), context);
  context.root = root;
  const viewer = vm.runInContext('mountContentBrowser(root)', context);
  const byId = id => root.all(element => element.id === id)[0];
  const byClass = name => root.all(element => element.classList.contains(name));
  const click = element => {
    if (element.tagName === 'summary' && element.parentElement?.tagName === 'details')
      element.parentElement.open = !element.parentElement.open;
    return element.listeners.click?.();
  };
  const change = (id, value, event = 'change') => { const control = byId(id); control.value = value; return control.listeners[event](); };
  return { context, viewer, root, document, calls, events, byId, byClass, click, change, run: code => vm.runInContext(code, context) };
}

const heal = {
  id: 'mend', name: 'Mending', family: 'Ability', parameters: [{ name: 'amount', kind: 'Integer', required: true, minimum: 1 }],
  profiles: [{ source: 'Unit', timing: 'Action', usage: 'LimitedOrUnlimited' }, { source: 'Equipment', timing: 'Action', usage: 'UnlimitedOnly' }],
  context: 'ActingUnit', targeting: { selection: 'Unit', recipients: 'Choose an adjacent friendly Unit.' },
  dependencies: ['posture', 'health'], description: { template: 'Restore {amount} HP.', parameters: ['amount'], allowsOverride: true }, grantable: true
};
const unitAuthoring = { defaults: {id:'',displayName:'',mov:3,rng:1,atk:1,def:1,hp:1,actions:'NormalAttack',behaviors:'None',localAbilities:[],footprint:'OneByOne'}, stats:[{name:'hp',kind:'Integer',minimum:1}], fields:[], footprints:['OneByOne'],movementPatterns:['Adjacent'],behaviors:[] };
const catalog = { unitAuthoring,
  context: { kind: 'canonical', name: 'Canonical content' },
  categories: [{ id: 'units', name: 'Units' }, { id: 'equipment', name: 'Equipment' }, { id: 'mechanics', name: 'Mechanics' }], diagnostics: [],
  entries: [
    { key: 'units:weaver', family: 'units', id: 'weaver', name: 'Sky Weaver', origin: 'canonical', summary: 'A quiet protector.',
      sections: [{ title: 'Stats', fields: [{ label: 'MOV', value: 3 }, { label: 'Storage', value: { capacities: [{ type: { id: 'dew', name: 'Dew' }, capacity: 2 }] } }] }],
      abilities: [{ key: 'stitch', name: 'Stitch the Air', timing: 'Action', usage: { maxUses: 2 }, mechanicId: 'mend', parameters: { amount: 2 }, description: 'Restore 2 HP.', ownerKey: 'units:weaver' }],
      references: [{ label: 'Produces', family: 'resources', id: 'absent', targetKey: 'resources:absent', exists: false }],
      definition: { id: 'weaver', localAbilities: [{ key: 'stitch', mechanic: { kind: 'mend', amount: 2 } }] } },
    { key: 'equipment:chime', family: 'equipment', id: 'chime', name: 'Copper Chime', origin: 'canonical', summary: 'A restorative sound.', sections: [],
      abilities: [{ key: 'stitch', name: 'Evening Song', timing: 'Action', usage: { maxUses: null }, mechanicId: 'mend', parameters: { amount: 4 }, description: 'Restore 4 HP.', ownerKey: 'equipment:chime' }],
      references: [{ label: 'Mechanic', family: 'mechanics', id: 'mend', targetKey: 'mechanics:mend', exists: true }], definition: { id: 'chime' } },
    { key: 'mechanics:mend', family: 'mechanics', id: 'mend', name: 'Mending', origin: 'canonical', summary: 'Restore health.', sections: [], abilities: [], references: [], definition: heal, mechanic: heal }
  ]
};

test('Unit catalog, detail and reference portraits use registered artwork with text fallbacks', async () => {
  const data = structuredClone(catalog);
  data.entries[1].references.push({targetKey:'units:weaver',exists:true});
  const h = harness(async () => ({ok:true,json:async () => data}));
  h.run('boardVisuals.unit.weaver = { image: "./test-owned-portrait.webp" }');
  await h.viewer.showCanonical();
  const row = h.byId('content-list').children.find(row => row.dataset.contentId === 'weaver');
  assert.equal(row.children[0].src, './test-owned-portrait.webp');
  assert.equal(row.children[0].alt, '');
  assert.match(row.textContent, /Sky Weaver/);
  h.click(row);
  const image = h.byId('content-detail').children.find(node => node.tagName === 'img');
  assert.equal(image.src, './test-owned-portrait.webp');
  image.listeners.error(); assert.equal(image.hidden, true);
  assert.match(h.byId('content-detail').textContent, /Sky Weaver/);
  h.click(h.byId('content-list').children.find(row => row.dataset.contentId === 'chime'));
  const reference = h.byClass('cb-reference').find(node => node.dataset.targetId === 'weaver');
  assert.equal(reference.children[0].src, './test-owned-portrait.webp');
  h.run('delete boardVisuals.unit.weaver');
  h.click(reference);
  assert.equal(h.byId('content-detail').children.some(node => node.tagName === 'img'), false);
  assert.match(h.byId('content-detail').textContent, /Sky Weaver/);
});

test('Unit reference sheets are optional disclosures with accessible labels and load-error fallback', async () => {
  const h = harness();
  h.run('boardVisuals.unit.weaver = { references: [{ label: "Character sheet", image: "./fixture-sheet.webp" }] }');
  await h.viewer.showCanonical();
  h.viewer.open('units:weaver');
  const image = h.root.all(e => e.tagName === 'img' && e.src === './fixture-sheet.webp')[0];
  assert.ok(image);
  assert.equal(image.alt, 'Sky Weaver — Character sheet');
  assert.equal(image.loading, 'lazy');
  assert.equal(image.parentElement.tagName, 'details');
  assert.equal(image.parentElement.children[0].textContent, 'Character sheet');
  assert.equal(image.style.maxWidth, '100%');
  image.listeners.error();
  assert.equal(image.parentElement.hidden, true);
  h.viewer.open('equipment:chime');
  assert.equal(h.byId('content-detail').all(e => e.tagName === 'img').length, 0);
});

test('search, category and origin counts use actual data and reference navigation restores filters', () => {
  const h = harness(); h.context.data = structuredClone(catalog);
  h.context.data.entries.push({ key: 'units:local', id: 'local', family: 'units', name: 'Sky Patient', origin: 'scenario-local' });
  h.run('globalThis.navigation = new ContentBrowserState(); navigation.load(data); navigation.filter({query:"sky",origin:"scenario-local"})');
  assert.deepEqual(plain(h.run('navigation.visible.map(e => e.id)')), ['local']);
  assert.deepEqual(plain(h.run('navigation.categories.map(c => [c.id,c.count])')), [['units',1],['equipment',0],['mechanics',0]]);
  h.run('navigation.filter({query:"chime",origin:"all",category:"equipment"}); navigation.open("equipment:chime"); navigation.open("mechanics:mend",true)');
  assert.equal(h.run('navigation.selected.id'), 'mend');
  assert.equal(h.run('navigation.category'), 'all');
  h.run('navigation.back()');
  assert.equal(h.run('navigation.selected.id'), 'chime');
  assert.equal(h.run('navigation.query'), 'chime');
  assert.equal(h.run('navigation.category'), 'equipment');
  assert.equal(h.run('navigation.open("missing",true)'), false);
  assert.equal(h.run('navigation.selected.id'), 'chime');
});

test('mount waits for display and canonical display keeps owned identities, text and nested values readable', async () => {
  const h = harness(); assert.equal(h.calls.length, 0);
  await h.viewer.show(); await h.viewer.show();
  assert.deepEqual(h.calls.map(call => call.url), ['/api/content-workspace', '/api/content/catalog', '/api/content-workspace', '/api/content/catalog']);
  const detail = h.byId('content-detail');
  assert.match(detail.textContent, /Sky Weaver/);
  assert.match(detail.textContent, /Stitch the AirAction2 \/ gameLocal key · stitchRestore 2 HP/);
  assert.match(detail.textContent, /CapacitiesTypeIddewNameDewCapacity2/);
  assert.equal(h.byClass('cb-ability')[0].dataset.mechanicId, 'mend');
  assert.match(detail.textContent, /Owned by Sky Weaver/);
  assert.ok(h.byClass('cb-missing').some(element => element.textContent.includes('absent')));
  assert.equal(h.byClass('cb-advanced')[0].open, undefined);
  assert.equal(h.byId('content-list').children[0].attributes['aria-current'], 'true');
  assert.equal(h.calls.some(call => /game|gambit|observation/.test(call.url)), false);
});

test('complex fields use the full row and populated groups can collapse and reopen without losing content', async () => {
  const data = structuredClone(catalog);
  data.entries[0].sections.push({ title: 'Empty definition values', fields: [] });
  data.entries[0].sections[0].fields.push({ label: 'Materials', value: ['wood', 'stone'] }, { label: 'No materials', value: [] });
  const h = harness(async () => ({ ok: true, json: async () => data })); await h.viewer.show();
  const groups = h.byClass('cb-content-group');
  assert.equal(groups.length, 1);
  const group = groups[0], summary = group.children[0];
  assert.equal(group.tagName, 'details'); assert.equal(summary.tagName, 'summary');
  assert.equal(summary.textContent, 'Stats'); assert.equal(group.open, true);
  const rows = group.all(element => element.classList.contains('cb-field'));
  assert.deepEqual(rows.filter(row => row.classList.contains('cb-field-complex')).map(row => row.children[0].textContent), ['Storage', 'Materials']);
  assert.ok(rows.find(row => row.children[0].textContent === 'MOV'));
  h.click(summary); assert.equal(group.open, false);
  h.click(summary); assert.equal(group.open, true);
  assert.match(group.textContent, /CapacitiesTypeIddewNameDewCapacity2Materialswoodstone/);
  assert.equal(h.calls.length, 2);
  assert.equal(h.byClass('cb-ability').length, 1); // Primary Ability text remains expanded independently.
});

test('reference links navigate to real mechanics, retain support rows and focus the heading, then return', async () => {
  const h = harness(); await h.viewer.showCanonical();
  h.change('content-search', 'chime', 'input');
  h.click(h.byId('content-list').children[0]);
  const link = h.byClass('cb-reference').find(element => element.dataset.targetId === 'mend'); h.click(link);
  assert.equal(h.document.activeElement.tagName, 'h3');
  assert.equal(h.document.activeElement.textContent, 'Mending');
  assert.equal(h.byClass('cb-profile').length, 2);
  assert.match(h.byId('content-detail').textContent, /UnitActionLimited Or UnlimitedEquipmentActionUnlimited Only/);
  assert.match(h.byId('content-detail').textContent, /Fixed Engine behavior.*Only the listed authored parameters are configurable.*Choose an adjacent friendly Unit/s);
  assert.equal(h.byClass('cb-parameter').length, 1); // Target recipients are not invented configurable parameters.
  assert.equal(h.byClass('cb-wording-policies')[0].open, undefined);
  h.click(h.byClass('cb-text-button').find(element => element.textContent.includes('Previous definition')));
  assert.equal(h.byId('content-search').value, 'chime');
  assert.match(h.byId('content-detail').textContent, /Copper Chime.*Restore 4 HP/s);
  h.click(h.byClass('cb-back-list')[0]);
  assert.equal(h.root.classList.contains('cb-show-detail'), false);
  assert.equal(h.document.activeElement.attributes['aria-current'], 'true');
});

test('recursive mechanic schema preserves defaults, enum choices, list bounds and object members', async () => {
  const data = structuredClone(catalog);
  const mechanic = data.entries[2].mechanic;
  mechanic.parameters = [{ name: 'rewards', kind: 'List', required: false, allowNull: true, minimumItems: 1, maximumItems: 6,
    defaultValue: [], item: { name: 'reward', kind: 'Object', required: true, members: [
      { name: 'direction', kind: 'Enum', required: true, allowedValues: ['North','South'], defaultValue: 'North' },
      { name: 'resource', kind: 'Reference', required: true, referenceCatalog: 'resources' },
      { name: 'amount', kind: 'Integer', required: true, minimum: 0, maximum: 5, defaultValue: 0 }
    ] } }];
  const h = harness(async () => ({ ok: true, json: async () => data })); await h.viewer.show(); h.viewer.open('mechanics:mend');
  const rendered = h.byId('content-detail').textContent;
  for (const token of ['rewards', 'Optional', 'Null allowedYes', 'Minimum items1', 'Maximum items6', 'Each item', 'Object fields', 'North', 'South', 'Reference catalogResources', 'Minimum0', 'Maximum5', 'Default0']) assert.ok(rendered.includes(token), token);
  assert.equal(h.byClass('cb-parameter').length, 5);
});

const settle = () => new Promise(resolve => setImmediate(resolve));

test('Loot Management selects visible missing rewards and previews before confirming one batch', async () => {
  const h = harness(async (url, options) => {
    const payload = options?.body ? JSON.parse(options.body) : null;
    const data = url === '/api/content-workspace' ? {token:'batch-token',definitions:{}} :
      url === '/api/content/loot-management' ? {rewards:[{family:'equipment',typeId:'a',name:'Alpha',origin:'workspace',cards:[]},{family:'items',typeId:'b',name:'Beta',origin:'canonical',cards:[{typeId:'old',name:'Old'}]}],cards:[]} :
      url.endsWith('/loot/preview') ? {request:payload,canCreate:true,definitions:{lootCardDefinitions:[{id:'a',name:'Alpha',equipmentTypeId:'a'}]}} :
      url.endsWith('/loot/create') ? {workspace:{token:'after',definitions:{}}} : catalog;
    return {ok:true,json:async()=>structuredClone(data)};
  });
  await h.viewer.show(); h.click(h.byId('content-loot-management')); await settle(); await settle();
  h.click(buttonText(h,'Select All Without Loot Cards'));
  h.click(buttonText(h,'Preview selected batch')); await settle();
  const preview = h.calls.find(c=>c.url.endsWith('/loot/preview'));
  assert.deepEqual(JSON.parse(preview.options.body).selected,[{family:'Equipment',typeId:'a'}]);
  assert.equal(h.calls.some(c=>c.url.endsWith('/loot/create')),false);
  h.click(buttonText(h,'Confirm batch writes')); await settle(); await settle();
  assert.equal(h.calls.filter(c=>c.url.endsWith('/loot/create')).length,1);
});

test('changing the Loot destination while preview is pending discards its confirmation', async () => {
  let release;
  const h=harness(async(url,options)=> {
    if(url.endsWith('/loot/preview')) return new Promise(resolve=>{release=()=>resolve({ok:true,json:async()=>({canCreate:true,definitions:{lootCardDefinitions:[]}})});});
    const data=url==='/api/content-workspace' ? {token:'t',definitions:{}} : url==='/api/content/loot-management' ? {rewards:[{family:'equipment',typeId:'a',name:'A',origin:'canonical',cards:[]}],cards:[]} : catalog;
    return {ok:true,json:async()=>structuredClone(data)};
  });
  await h.viewer.showScenario({},'Fixture',{getContext:()=>({}),getRevision:()=>1,saveLootBatch:async()=>{}});
  h.click(h.byId('content-loot-management'));await settle();await settle();
  h.click(buttonText(h,'Select All Without Loot Cards'));h.click(h.byId('management-preview'));await settle();
  h.change('loot-batch-destination','scenario');release();await settle();
  assert.equal(h.byId('management-confirm'),undefined);
  assert.equal(h.calls.some(c=>c.url.endsWith('/loot/create')),false);
});
function workspaceHarness() {
  let workspace = { token: 'token-1', definitions: { unitTypes: [], equipmentTypes: [], itemTypes: [], lootCardDefinitions: [] } };
  const writes = [], publications = [], previews = [];
  let missingSelections = [], failSave = false;
  const standard = structuredClone(catalog);
  standard.entries[1].definition = { id: 'chime', name: 'Copper Chime', localAbilities: [
    {key:'stitch',name:'Evening Song',timing:'Action',usage:{maxUses:null},mechanic:{kind:'mend',amount:4}}
  ] };
  const fields = {units:'unitTypes',equipment:'equipmentTypes',items:'itemTypes',lootCards:'lootCardDefinitions'};
  const asEntry = (row, family, origin) => ({key:`${family}:${row.id}`,id:row.id,name:row.displayName ?? row.name ?? row.id,family,origin,definition:structuredClone(row),sections:[],abilities:[],references:[]});
  function effective(context, origin) {
    const result = structuredClone(standard);
    for (const [family,field] of Object.entries(fields)) for (const row of context[field] ?? []) {
      const existing = result.entries.findIndex(e => e.family === family && e.id === row.id);
      const entry = asEntry(row,family,existing >= 0 ? `${origin}-override` : origin);
      if (existing >= 0) result.entries[existing] = entry; else result.entries.push(entry);
    }
    return result;
  }
  const ok = data => ({ok:true,status:200,json:async()=>structuredClone(data)});
  const h = harness(async (url, options) => {
    const input = options?.body ? JSON.parse(options.body) : null;
    if (url === '/api/content-workspace' && !options) return ok(workspace);
    if (url === '/api/content-workspace' && options.method === 'PUT') {
      if (failSave) { failSave=false; return {ok:false,status:409,json:async()=>({detail:'Workspace changed. Reload before saving.'})}; }
      assert.equal(input.expectedToken,workspace.token);
      writes.push(structuredClone(input)); workspace={token:`token-${writes.length+1}`,definitions:structuredClone(input.definitions)}; return ok(workspace);
    }
    if (url === '/api/content-workspace' && options.method === 'DELETE') {
      assert.equal(input.expectedToken,workspace.token); writes.push(structuredClone(input));
      workspace={token:'cleared',definitions:{unitTypes:[],equipmentTypes:[],itemTypes:[],lootCardDefinitions:[]}}; return ok(workspace);
    }
    if (url === '/api/content-workspace/catalog') return ok(effective(workspace.definitions,'workspace'));
    if (url === '/api/content/catalog') return ok(standard);
    if (url === '/api/content/context' || url === '/api/content/inspect') return ok(effective(input,'scenario'));
    if (url === '/api/content/validate') return ok({valid:true,diagnostics:[],definition:input.definition,entry:asEntry(input.definition,input.family,'workspace')});
    if (url === '/api/content-workspace/promotion/preview') {
      previews.push(structuredClone(input));
      return ok({request:input,canonicalRevision:'canonical-1',missingSelections,requiredWorkspaceDependencies:missingSelections,canPublish:missingSelections.length===0,previewToken:`preview-${previews.length}`});
    }
    if (url === '/api/content-workspace/promotion/publish') {
      publications.push(structuredClone(input));
      for (const selected of input.preview.request.selected) {
        const family=Object.keys(fields).find(family=>({units:'Unit',equipment:'Equipment',items:'Item',lootCards:'LootCard'})[family]===selected.family);
        const field=fields[family], row=workspace.definitions[field].find(row=>row.id===selected.typeId);
        standard.entries=standard.entries.filter(e=>!(e.family===family && e.id===selected.typeId));standard.entries.push(asEntry(row,family,'canonical'));
        workspace.definitions[field]=workspace.definitions[field].filter(row=>row.id!==selected.typeId);
      }
      return ok({});
    }
    throw new Error(`Unexpected request ${url}`);
  });
  return {...h,writes,publications,previews,workspace:()=>structuredClone(workspace),require:ids=>{missingSelections=ids;},conflict:()=>{failSave=true;},
    seed:definitions=>{workspace.definitions={...workspace.definitions,...structuredClone(definitions)};}};
}
const buttonText = (h,text) => h.root.all(element=>element.tagName==='button' && element.textContent===text)[0];

test('Workspace Management previews a selected subset with the promotion contract', async () => {
  const h=workspaceHarness(); h.seed({equipmentTypes:[{id:'batch-test',name:'Batch test',localAbilities:[]}]}); await h.viewer.showLibrary();
  h.click(h.byId('content-workspace-management')); await settle(); await settle();
  const checkbox=h.root.all(e=>e.tagName==='input' && e.type==='checkbox')[0];
  checkbox.checked=true; checkbox.listeners.change();
  h.click(h.byId('management-preview')); await settle();
  const call=h.calls.find(c=>c.url==='/api/content-workspace/promotion/preview');
  const payload=JSON.parse(call.options.body);
  assert.deepEqual(Object.keys(payload).sort(),['expectedToken','selected']);
  assert.equal(payload.selected.length,1);
  assert.equal(h.publications.length,0);
  h.click(h.byId('management-confirm'));await settle();await settle();
  assert.equal(h.publications.length,1);
});

test('canonical editing writes an override into Workspace with the opening conflict token', async () => {
  const h=workspaceHarness();await h.viewer.showCanonical();h.viewer.open('equipment:chime');
  h.click(h.byId('content-edit-in-workspace'));await settle();
  assert.equal(h.byId('content-editor-id').value,'chime');
  assert.equal(h.byId('content-editor-id').readOnly,true);
  h.change('content-editor-name','Clear Chime','input');h.click(h.byId('content-editor-save'));await settle();await settle();
  assert.equal(h.writes.length,1);assert.equal(h.writes[0].expectedToken,'token-1');
  assert.equal(h.workspace().definitions.equipmentTypes[0].id,'chime');
  assert.equal(h.workspace().definitions.equipmentTypes[0].name,'Clear Chime');
  await h.viewer.showLibrary();h.viewer.open('equipment:chime');
  assert.match(h.byId('content-detail').textContent,/workspace-override/);
  assert.ok(h.byId('content-edit-definition'));
});

test('Create Copy proposes an independent identity and retains typed mechanic parameters', async () => {
  const h=workspaceHarness();await h.viewer.showCanonical();h.viewer.open('equipment:chime');
  h.click(h.byId('content-copy-definition'));await settle();
  assert.notEqual(h.byId('content-editor-id').value,'chime');assert.equal(h.byId('content-editor-id').readOnly,false);
  h.change('content-editor-id','independent-chime','input');h.click(h.byId('content-editor-save'));await settle();await settle();
  const copy=h.workspace().definitions.equipmentTypes[0];assert.equal(copy.id,'independent-chime');
  assert.deepEqual(copy.localAbilities[0].mechanic,{kind:'mend',amount:4});
  assert.equal(h.calls.some(call=>/content-library|packages|revisions/.test(call.url)),false);
});

test('Workspace creates Unit and Item forms through shared authoring controls', async () => {
  const h=workspaceHarness();await h.viewer.showLibrary();h.click(h.byId('content-create-units'));await settle();
  assert.ok(h.root.all(element=>element.dataset.fieldPath==='definition.hp').length);
  h.change('content-editor-id','test-guard','input');h.change('content-editor-name','Test Guard','input');
  h.click(h.byId('content-editor-save'));await settle();await settle();
  assert.equal(h.workspace().definitions.unitTypes[0].id,'test-guard');
  h.click(h.byId('content-create-items'));await settle();
  h.change('content-editor-id','test-item','input');h.change('content-editor-name','Test Item','input');
  h.click(h.byId('content-editor-save'));await settle();await settle();
  assert.equal(h.workspace().definitions.itemTypes[0].id,'test-item');
});

test('Workspace stale saves keep the form and dirty navigation still requires an explicit choice', async () => {
  const h=workspaceHarness();h.seed({equipmentTypes:[{id:'local',name:'Local',localAbilities:[]}]});await h.viewer.showLibrary();h.viewer.open('equipment:local');
  h.click(h.byId('content-edit-definition'));await settle();h.change('content-editor-name','Pending','input');
  const event={prevented:false,preventDefault(){this.prevented=true;}};h.events.beforeunload(event);assert.equal(event.prevented,true);
  h.conflict();h.click(h.byId('content-editor-save'));await settle();
  assert.equal(h.writes.length,0);assert.equal(h.byId('content-editor').hidden,false);assert.match(h.byId('content-editor').textContent,/Workspace changed/);
  h.change('content-context','canonical');await settle();h.click(h.byId('content-editor-keep'));await settle();
  assert.equal(h.byId('content-context').value,'workspace');
});

test('promotion previews a selection and requires explicit batch dependency inclusion and confirmation', async () => {
  const h=workspaceHarness();h.seed({equipmentTypes:[{id:'local',name:'Local'}],lootCardDefinitions:[{id:'gift',name:'Gift',equipmentTypeId:'local'}]});
  await h.viewer.showLibrary();h.viewer.open('lootCards:gift');h.require([{family:'Equipment',typeId:'local'}]);
  h.click(h.byId('content-promote-definition'));await settle();
  assert.equal(h.publications.length,0);assert.deepEqual(h.previews[0].selected,[{family:'LootCard',typeId:'gift'}]);
  h.require([]);h.click(buttonText(h,'Include required Workspace dependencies and preview again'));await settle();
  assert.equal(h.publications.length,0);assert.equal(h.previews[1].selected.length,2);
  h.click(buttonText(h,'Confirm publication batch to Canonical'));await settle();await settle();
  assert.equal(h.publications.length,1);assert.equal(h.publications[0].confirm,true);assert.equal(h.publications[0].preview.previewToken,'preview-2');
  assert.equal(h.workspace().definitions.equipmentTypes.length,0);assert.equal(h.workspace().definitions.lootCardDefinitions.length,0);
});

test('Empty Workspace only deletes after confirmation and ordinary definition deletion keeps other families', async () => {
  const h=workspaceHarness();h.seed({equipmentTypes:[{id:'local',name:'Local'}],itemTypes:[{id:'item',name:'Item'}]});
  await h.viewer.showLibrary();h.viewer.open('equipment:local');h.click(buttonText(h,'Delete Workspace definition'));await settle();await settle();
  assert.equal(h.workspace().definitions.equipmentTypes.length,0);assert.equal(h.workspace().definitions.itemTypes.length,1);
  h.click(buttonText(h,'Empty Workspace'));await settle();assert.equal(h.calls.filter(c=>c.options?.method==='DELETE').length,0);
  h.click(buttonText(h,'Confirm empty Workspace'));await settle();await settle();
  assert.equal(h.calls.filter(c=>c.options?.method==='DELETE').length,1);assert.equal(h.workspace().definitions.itemTypes.length,0);
});

test('scenario-local editing retains its explicit destination and opening revision', async () => {
  const h=workspaceHarness(),saves=[];
  const scenario={equipmentTypes:[{id:'local',name:'Scenario Bell',localAbilities:[]}]};
  const destination={getRevision:()=> 'scenario-7',getContext:()=>structuredClone(scenario),save:async payload=>{saves.push(plain(payload));}};
  await h.viewer.showScenario(scenario,'Test scenario',destination);h.viewer.open('equipment:local');
  h.click(h.byId('content-edit-definition'));await settle();h.change('content-editor-name','Edited scenario bell','input');
  h.click(h.byId('content-editor-save'));await settle();await settle();
  assert.equal(saves.length,1);assert.equal(saves[0].expectedRevision,'scenario-7');assert.equal(h.writes.length,0);
});

test('Designer inspection captures an isolated snapshot and renders authored names as text', async () => {
  const draft=structuredClone(catalog);draft.entries[0].name='<img src=x onerror=alert(1)>';
  const h=harness(async url=>({ok:true,json:async()=>structuredClone(url.endsWith('/inspect')?draft:catalog)}));
  const input={unitTypes:[{id:'local',displayName:'Opening snapshot'}]};
  const showing=h.viewer.showScenario(input,'Test draft');input.unitTypes[0].displayName='Later edit';await showing;
  const inspection=h.calls.find(call=>call.url==='/api/content/inspect');
  assert.equal(JSON.parse(inspection.options.body).unitTypes[0].displayName,'Opening snapshot');
  assert.ok(h.byId('content-detail').textContent.includes('<img src=x onerror=alert(1)>'));
  assert.equal(h.root.all(element=>element.tagName==='img').length,0);
});

test('late canonical response cannot replace the newer scenario context', async () => {
  let release;
  const h=harness(url=>url==='/api/content/catalog'?new Promise(resolve=>{release=resolve;})
    :Promise.resolve({ok:true,json:async()=>url==='/api/content-workspace'?{token:'t',definitions:{}}:
      {...catalog,entries:[{key:'items:local',id:'local',name:'Draft Letter',family:'items',origin:'scenario-local',definition:{id:'local'}}]}}));
  const pending=h.viewer.showCanonical();await settle();await h.viewer.showScenario({itemTypes:[]},'Test draft');
  release({ok:true,json:async()=>catalog});await pending;
  assert.equal(h.byId('content-context').value,'draft');assert.match(h.byId('content-detail').textContent,/Draft Letter/);
});

test('invalid scenario context clears old entries and exposes the authoritative diagnostic', async () => {
  let invalid=false;
  const h=harness(async url=>url==='/api/content/inspect' && invalid
    ?{ok:false,json:async()=>({diagnostics:[{path:'unitTypes[0].localAbilities[0].key',message:'An explicit local key is required.'}]})}
    :{ok:true,json:async()=>structuredClone(catalog)});
  await h.viewer.showScenario({unitTypes:[]},'Good draft');invalid=true;
  await h.viewer.showScenario({unitTypes:[{}]},'Broken draft');
  assert.ok(!h.byId('content-detail').textContent.includes('Sky Weaver'));
  assert.match(h.root.textContent,/unitTypes\[0\]\.localAbilities\[0\]\.key.*An explicit local key is required/s);
});

test('authoring waits for Workspace and catalog on initial load and reload while keeping dirty editor buffers', async () => {
  const pending = [];
  const h = harness(url => new Promise(resolve => pending.push({url, resolve})));
  const complete = data => pending.shift().resolve({ok:true,json:async()=>structuredClone(data)});
  const workspace = { token:'opening-token', definitions:{unitTypes:[],equipmentTypes:[],itemTypes:[],lootCardDefinitions:[]} };
  const showing = h.viewer.show();
  assert.equal(h.root.attributes['aria-busy'], 'true');
  for (const family of ['units','equipment','items','lootCards']) assert.equal(h.byId(`content-create-${family}`).disabled, true);
  h.click(h.byId('content-create-equipment')); await settle();
  assert.equal(h.byId('content-editor').hidden, true);
  assert.match(h.root.textContent, /Loading content/);
  assert.equal(pending[0].url, '/api/content-workspace'); complete(workspace); await settle();
  assert.equal(pending[0].url, '/api/content/catalog');
  assert.equal(h.byId('content-create-equipment').disabled, true);
  complete(catalog); await showing;
  assert.equal(h.root.attributes['aria-busy'], 'false');
  assert.equal(h.byId('content-create-equipment').disabled, false);
  h.click(h.byId('content-create-equipment')); await settle();
  assert.equal(h.byId('content-editor').hidden, false);
  h.change('content-editor-name', 'Pending Equipment', 'input');

  const reloading = h.viewer.show();
  assert.equal(h.byId('content-create-equipment').disabled, true);
  assert.equal(h.byId('content-editor-name').value, 'Pending Equipment');
  complete({...workspace,token:'later-token'}); await settle();
  assert.equal(h.byId('content-create-equipment').disabled, true);
  complete(catalog); await reloading;
  assert.equal(h.byId('content-editor-name').value, 'Pending Equipment');
  assert.equal(h.byId('content-editor').hidden, false);
  const leaving = h.viewer.requestCloseEditor();
  h.click(h.byId('content-editor-keep')); assert.equal(await leaving, false);
  assert.equal(h.byId('content-editor-name').value, 'Pending Equipment');
  const event={prevented:false,preventDefault(){this.prevented=true;}};h.events.beforeunload(event);assert.equal(event.prevented,true);
});

test('failed catalog reload keeps authoring unavailable, displays the error and permits recovery', async () => {
  let fail = false;
  const h = harness(async url => url === '/api/content-workspace'
    ? {ok:true,json:async()=>({token:'current',definitions:{}})}
    : fail ? {ok:false,json:async()=>({detail:'Catalog could not be loaded.'})}
      : {ok:true,json:async()=>structuredClone(catalog)});
  await h.viewer.show(); assert.equal(h.byId('content-create-equipment').disabled, false);
  fail=true; await h.viewer.show();
  assert.equal(h.root.attributes['aria-busy'], 'false');
  assert.equal(h.byId('content-create-equipment').disabled, true);
  assert.match(h.root.textContent, /Catalog could not be loaded/);
  h.click(h.byId('content-create-equipment')); await settle();
  assert.equal(h.byId('content-editor').hidden, true);
  assert.match(h.root.textContent, /Catalog could not be loaded/);
  fail=false; await h.viewer.show();
  assert.equal(h.byId('content-create-equipment').disabled, false);
  h.click(h.byId('content-create-equipment')); await settle();
  assert.equal(h.byId('content-editor').hidden, false);
});
