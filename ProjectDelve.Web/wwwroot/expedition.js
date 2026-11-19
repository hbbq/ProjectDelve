// Management commands and read-only Scenario export use separate paths.
export function createExpeditionActions({ command, changed = () => {} }) {
  let pending = false;
  return {
    get pending() { return pending; },
    async run(operation, body = {}) {
      if (pending) return false;
      pending = true; changed();
      try { return await command(`expedition/${operation}`, body); }
      finally { pending = false; changed(); }
    }
  };
}
export function expeditionSummary(expedition) {
  return `${expedition.name} · Seed ${expedition.seed} · Region (${expedition.region.x}, ${expedition.region.y}) · ${expedition.completedRegions} completed`;
}
export function mountExpeditions({ command, getSnapshot, ready, errorMessage }) {
  const $ = id => document.getElementById(id);
  const el = (tag, text) => { const value = document.createElement(tag); if (text) value.textContent = text; return value; };
  const button = (id, text, action) => { const value = el('button', text); value.id = id; value.type = 'button'; value.addEventListener('click', action); return value; };
  const main = $('expedition-main'), bar = $('expedition-bar');
  let status, list, form, catalogLoaded = false, loading = false, failedOperation;
  let exporting = false, exportSession, exportMessage = '';
  const actions = createExpeditionActions({ command, changed: update });
  const note = 'Save before closing or reloading this tab. Leaving through the app saves first; closing the browser may lose changes since the last save.';
  const busyMessage = { create: 'Generating the first region…', resume: 'Opening the saved region…',
    save: 'Saving the Expedition…', leave: 'Saving before leaving…', continue: 'Preparing the next region…' };
  async function perform(operation, body = {}) {
    if (actions.pending) return false;
    clearExport();
    status.textContent = busyMessage[operation];
    try {
      const success = await actions.run(operation, body);
      if (!success) throw new Error((errorMessage() || 'The game is busy or disconnected. Wait for it to settle.') + (operation === 'create' ? ' Refresh the saved list to check whether the Expedition was created before starting another.' : ''));
      failedOperation = undefined;
      status.textContent = operation === 'save' ? 'Expedition saved.' : '';
      update(); return true;
    } catch (error) {
      failedOperation = operation;
      status.textContent = `${error.message} You can retry this action.`;
      update();
      return false;
    }
  }
  const regionProgress = el('strong');
  const saveState = el('small');
  const tableStatus = el('p'); tableStatus.id = 'expedition-table-status'; tableStatus.setAttribute('role', 'status');
  const save = button('expedition-save', 'Save', () => perform('save'));
  const copy = button('expedition-copy-delve1', 'Copy DELVE1', copyScenario);
  copy.title = 'Copy the region’s starting Scenario, including unexplored areas.';
  const transport = el('textarea');
  transport.id = 'expedition-scenario-transport';
  transport.setAttribute('aria-label', 'Initial region Scenario as DELVE1');
  transport.readOnly = true;
  transport.spellcheck = false;
  transport.rows = 2;
  transport.hidden = true;
  function clearExport() {
    exportSession = undefined;
    exportMessage = '';
    transport.hidden = true;
    transport.value = '';
  }
  async function copyScenario() {
    const snapshot = getSnapshot();
    if (exporting || !snapshot?.expedition || !snapshot.sessionId) return;
    const session = snapshot.sessionId;
    clearExport();
    exportSession = session;
    exporting = true;
    exportMessage = 'Exporting the region’s starting Scenario…';
    update();
    const stillCurrent = () => exportSession === session && getSnapshot()?.sessionId === session;
    try {
      const response = await fetch(`/api/expeditions/current/scenario?sessionId=${encodeURIComponent(session)}`, { cache: 'no-store' });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail || 'Could not export the region’s starting Scenario.');
      }
      const value = await response.text();
      if (!stillCurrent()) return;
      try {
        await navigator.clipboard.writeText(value);
        if (stillCurrent()) exportMessage = 'Starting Scenario copied as DELVE1. Live progress uses Expedition Save.';
      } catch {
        if (!stillCurrent()) return;
        transport.value = value;
        transport.hidden = false;
        transport.focus();
        transport.select();
        exportMessage = 'Clipboard unavailable. Copy the selected starting Scenario with Ctrl+C or Command+C.';
      }
    } catch (error) {
      if (stillCurrent()) exportMessage = error.message;
    } finally {
      exporting = false;
      update();
    }
  }
  const switchRun = button('expedition-switch', 'Switch Expedition', () => { location.hash = '#expeditions'; });
  const nextRegion = button('expedition-continue', 'Continue Expedition', () => {
    const transitionId = getSnapshot()?.expedition?.transitionId;
    if (transitionId) return perform('continue', { transitionId });
  });
  const tableActions = el('div'); tableActions.className = 'expedition-table-actions';
  tableActions.append(save, copy, switchRun, nextRegion);
  bar.append(regionProgress, saveState, tableActions, tableStatus, transport);
  function update() {
    const snapshot = getSnapshot();
    const expedition = snapshot?.expedition;
    if (exportSession && exportSession !== snapshot?.sessionId) clearExport();
    bar.hidden = !expedition;
    if (expedition) {
      regionProgress.textContent = `Region (${expedition.region.x}, ${expedition.region.y}) · ${expedition.completedRegions} completed · Exit ${expedition.exitCount}/${expedition.exitRequired}`;
      saveState.textContent = expedition.hasUnsavedChanges ? 'Unsaved changes. Save before closing.' : 'Saved';
      bar.dataset.saveStatus = expedition.hasUnsavedChanges ? 'unsaved' : 'saved';
    }
    nextRegion.hidden = !expedition?.transitionId;
    nextRegion.textContent = failedOperation === 'continue' ? 'Retry Continue' : 'Continue Expedition';
    const resultContinue = $('result-continue-expedition');
    if (resultContinue && expedition?.transitionId) resultContinue.textContent = nextRegion.textContent;
    for (const control of [...main.querySelectorAll('button'), ...bar.querySelectorAll('button')]) control.disabled = actions.pending || !ready();
    copy.disabled ||= exporting || !snapshot?.sessionId;
    tableStatus.textContent = exportMessage || status?.textContent || '';
  }
  const management = el('div'); management.className = 'expedition-management-grid';
  const saved = el('section');
  main.append(el('h2', 'Expeditions'), el('p', note));
  status = el('p'); status.id = 'expedition-status'; status.setAttribute('role', 'status'); main.append(status);
  form = el('form'); form.id = 'expedition-new';
  const field = (id, label, tag = 'input') => { const wrapper = el('label', label); const input = el(tag); input.id = id; wrapper.append(input); form.append(wrapper); return input; };
  const name = field('expedition-name', 'Expedition name'); name.required = true; name.maxLength = 80;
  const seed = field('expedition-seed', 'World seed (blank generates one)'); seed.spellcheck = false;
  const generateSeed = () => {
    seed.value = '0x' + Array.from(crypto.getRandomValues(new Uint32Array(2)), n => n.toString(16).padStart(8, '0')).join('');
  };
  form.append(button('expedition-generate-seed', 'Generate seed', generateSeed));
  const preset = field('expedition-preset', 'World preset', 'select'); const defaultOption = el('option', 'Default Expedition'); defaultOption.value = ''; preset.append(defaultOption);
  const recipe = field('expedition-recipe', 'WLD1 recipe (optional; overrides preset)', 'textarea'); recipe.rows = 2; recipe.spellcheck = false;
  const create = button('expedition-create', 'Start new Expedition', () => {}); create.type = 'submit'; form.append(create);
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (!form.reportValidity()) return;
    const origin = location.hash;
    if (!seed.value.trim()) generateSeed();
    if (await perform('create', { expeditionName: name.value.trim(), expeditionSeed: seed.value.trim(), expeditionRecipe: recipe.value.trim() || preset.value || null }) && location.hash === origin) location.hash = '#play';
    update();
  });
  list = el('div'); list.id = 'expedition-list';
  saved.append(el('h3', 'Saved Expeditions'), button('expedition-refresh', 'Refresh list', show), list);
  management.append(form, saved); main.append(management);
  async function show() {
    if (loading) return;
    loading = true; list.textContent = 'Loading saved Expeditions…';
    try {
      const response = await fetch('/api/expeditions', { cache: 'no-store' });
      if (!response.ok) throw new Error('Saved Expeditions could not be loaded. Use Refresh list to retry.');
      const { expeditions, unavailable = 0 } = await response.json(); list.replaceChildren();
      if (unavailable > 0) list.append(el('p', `${unavailable} saved Expedition${unavailable === 1 ? ' is' : 's are'} unavailable. Check server storage and refresh to retry.`));
      if (!expeditions.length) list.append(el('p', 'No saved Expeditions yet. Start a new one above.'));
      for (const expedition of expeditions) {
        const row = el('article'); row.dataset.expeditionId = expedition.id;
        row.append(el('strong', expeditionSummary(expedition)), el('small', `Saved ${new Date(expedition.savedAt).toLocaleString()}`), button('', 'Resume', async () => {
          const origin = location.hash;
          if (await perform('resume', { expeditionId: expedition.id }) && location.hash === origin) location.hash = '#play';
          update();
        })); list.append(row);
      }
    } catch (error) { list.textContent = error.message; }
    finally { loading = false; update(); }
    if (!catalogLoaded) {
      try {
        const response = await fetch('/api/world-generation/catalog', { cache: 'no-store' });
        if (!response.ok) return;
        const catalog = await response.json();
        for (const entry of catalog.presets) { const option = el('option', entry.name); option.value = entry.id; preset.append(option); }
        catalogLoaded = true;
      } catch { /* Default and explicit recipes remain usable; refresh retries catalog. */ }
    }
  }
  globalThis.delveShowExpeditions = show;
  globalThis.delveLeaveExpedition = async () => {
    if (!getSnapshot()?.expedition) return true;
    const success = await perform('leave'); update(); return success;
  };
  globalThis.addEventListener('beforeunload', event => {
    if (!getSnapshot()?.expedition?.hasUnsavedChanges) return;
    event.preventDefault(); event.returnValue = '';
  });
  update();
  if (location.hash === '#expeditions') void show();
  return { update };
}
