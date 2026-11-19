import { createStationDraft } from './equipment-station-state.js';

export function mountEquipmentStation({ getView, request, command, ready, visible = () => true, navigate = mode => globalThis.delveNavigate?.(mode), commandError = () => '' }) {
  const el = (tag, text, className) => {
    const node = document.createElement(tag); if (text != null) node.textContent = text;
    if (className) node.className = className; return node;
  };
  const dialog = el('dialog', null, 'equipment-station'); dialog.id = 'equipment-station-dialog';
  dialog.setAttribute('aria-labelledby', 'equipment-station-title');
  const title = el('h2', 'Equipment Station'); title.id = 'equipment-station-title'; title.tabIndex = -1;
  const intro = el('p', 'Give objects to recipients. Confirm applies every transfer together.');
  const normalized = el('p', 'Comparison: each Unit alone, Upright, at maximum HP on neutral terrain. Live HP, Posture and use counters are preserved.', 'station-explanation');
  const reservedWarning = el('p', null, 'station-reserved'); reservedWarning.id = 'equipment-station-reserved'; reservedWarning.setAttribute('role', 'note'); reservedWarning.hidden = true;
  const status = el('p'); status.id = 'equipment-station-status'; status.setAttribute('role', 'status'); status.setAttribute('aria-live', 'polite');
  const diagnostics = el('ul'); diagnostics.id = 'equipment-station-diagnostics'; diagnostics.setAttribute('aria-live', 'polite');
  const focusLabel = el('label', 'Focused Unit ', 'station-focus-label');
  const focusSelect = el('select'); focusSelect.id = 'equipment-station-focus'; focusLabel.append(focusSelect);
  const roster = el('div', null, 'station-roster'); roster.id = 'equipment-station-roster';
  const actions = el('div', null, 'station-actions');
  let focused, priorFocus, priorScroll, submitting = false, message = '', renderedSignature, interactionKey, savedAllocation;
  const disclosures = new Map();
  const disclosure = (key, label) => {
    const node = el('details'); node.append(el('summary', label)); node.open = disclosures.get(key) ?? false;
    node.addEventListener('toggle', () => disclosures.set(key, node.open)); return node;
  };
  const button = (id, label, action) => {
    const value = el('button', label); value.id = `equipment-station-${id}`; value.type = 'button';
    value.addEventListener('click', action); actions.append(value); return value;
  };
  const draft = createStationDraft({ preview: body => request('/machine/preview', body), changed: render });
  const reset = button('reset', 'Reset allocation', () => { message = ''; draft.reset(); });
  const retry = button('retry', 'Retry preview', () => { message = ''; void draft.retry(); });
  const save = button('save', 'Save draft', () => submit('expedition/save'));
  const cancel = button('cancel', 'Cancel', () => submit('machine/cancel'));
  const confirm = button('confirm', 'Confirm redistribution', () => submit('machine/confirm'));
  const returnToExpeditions = button('return', 'Back to Expeditions', () => { if (!submitting && ready()) navigate('expeditions'); }); returnToExpeditions.hidden = true;
  confirm.className = 'station-primary';
  const explanation = disclosure('normalization', 'How comparison works'); explanation.append(normalized);
  dialog.append(title, intro, reservedWarning, explanation, status, diagnostics, focusLabel, roster, actions); document.body.append(dialog);
  // Native modality makes the board inert and contains keyboard/touch interaction.
  dialog.addEventListener('cancel', event => { event.preventDefault(); if (!submitting && ready()) void submit('machine/cancel'); });
  dialog.addEventListener('keydown', event => event.stopPropagation());
  focusSelect.addEventListener('change', () => { focused = focusSelect.value; updateFocus(); });
  const unitName = id => {
    const card = getView()?.presentation.cards?.[id];
    const name = card?.playerName ?? card?.individualName ?? card?.displayName ?? id;
    const copies = (getView()?.presentation.decision?.equipmentStation?.eligibleUnitIds ?? []).map(id => [id, getView()?.presentation.cards?.[id]])
      .filter(([, candidate]) => candidate &&
      (candidate.playerName ?? candidate.individualName ?? candidate.displayName) === name);
    return copies.length > 1 ? `${name} ${copies.findIndex(([key]) => key === id) + 1}` : name;
  };
  const objectIndex = () => new Map((getView()?.presentation.decision?.equipmentStation?.eligibleUnitIds ?? [])
    .flatMap(id => getView()?.presentation.cards?.[id]?.ownedObjects ?? []).map(value => [value.id, value]));
  const objectName = id => {
    const objects = [...objectIndex().values()], object = objects.find(value => value.id === id);
    const name = object?.content?.name ?? 'Unavailable object';
    const copies = objects.filter(value => value.content?.name === name);
    return copies.length > 1 ? `${name} (copy ${copies.findIndex(value => value.id === id) + 1})` : name;
  };
  function closeModal(restoreFocus = false) {
    if (!dialog.open) return;
    dialog.close();
    if (priorScroll) globalThis.scrollTo?.(priorScroll.x, priorScroll.y);
    if (restoreFocus && priorFocus?.isConnected) priorFocus.focus({ preventScroll: true });
  }
  function updateFocus() {
    focusSelect.value = focused;
    for (const node of roster.children) node.dataset.focused = String(node.dataset.stationUnit === focused);
  }
  function abilityIdentity(value) { return `${value.kind ?? value.sourceKind}\0${value.instanceId ?? value.sourceInstanceId}\0${value.key ?? value.definition?.key}`; }
  const usesText = count => count == null ? 'Unlimited uses' : count === 0 ? 'Exhausted · 0 uses remaining' : `${count} ${count === 1 ? 'use' : 'uses'} remaining`;
  const sourceName = ability => ability.sourceKind === 'Unit' ? 'Native' : objectIndex().has(ability.sourceInstanceId)
    ? objectName(ability.sourceInstanceId) : ability.sourceName ?? ability.sourceTypeId;
  function renderAbilities(row, host) {
    const details = disclosure(`abilities:${row.unitId}`, 'Ability definitions and sources');
    const gained = new Set((row.gainedAbilities ?? []).map(abilityIdentity));
    const gains = (row.abilityPreviews ?? []).filter(entry => gained.has(abilityIdentity(entry.ability)))
      .map(entry => entry.content?.name ?? entry.ability.definition.name);
    const losses = (row.lostAbilityPreviews ?? []).map(entry => entry.content?.name ?? entry.ability.definition.name);
    if (gains.length) host.append(el('p', `Gained: ${gains.join(', ')}`, 'station-delta'));
    if (losses.length) host.append(el('p', `Lost: ${losses.join(', ')}`, 'station-delta'));
    for (const { entry, lost } of [...(row.abilityPreviews ?? []).map(entry => ({ entry, lost: false })), ...(row.lostAbilityPreviews ?? []).map(entry => ({ entry, lost: true }))]) {
      const ability = entry.ability, content = entry.content;
      const panel = el('div', null, 'station-ability');
      panel.append(el('strong', `${lost ? 'Lost: ' : gained.has(abilityIdentity(ability)) ? 'Gained: ' : ''}${content?.name ?? ability.definition.name}`));
      const timing = content?.category ?? ability.definition.timing;
      const passive = ability.definition.timing === 'Passive';
      panel.append(el('p', `${timing}${passive ? ' effect' : ' Ability'} · ${sourceName(ability)}`));
      panel.append(el('p', content?.description ?? ability.definition.descriptionOverride ?? ''));
      panel.append(el('small', `${usesText(entry.remainingUses)}${passive ? entry.applicable ? ' · Applies in this comparison' : ' · Does not apply in this comparison' : ''}`));
      details.append(panel);
    }
    host.append(details);
  }
  function renderStats(row, host) {
    if (row.proposedAllocationAvailable === false) { host.append(el('p', 'Proposed comparison unavailable. Repair the allocation or reset it.')); return; }
    host.append(el('small', 'NORMALIZED COMPARISON', 'station-normalized-label'));
    const strip = el('div', null, 'station-stat-strip');
    for (const [key, label] of [['mov', 'MOV'], ['rng', 'RNG'], ['atk', 'ATK'], ['def', 'DEF'], ['hp', 'Max HP'], ['sightDistance', 'Sight']]) {
      const stat = el('div', null, 'station-stat');
      const delta = row.difference?.[key];
      stat.append(el('small', label), el('strong', row[key] ?? '—'));
      if (delta != null && delta !== 0) stat.append(el('span', delta > 0 ? `+${delta}` : String(delta), delta > 0 ? 'station-positive' : 'station-negative'));
      stat.setAttribute('aria-label', `${label}: ${row[key] ?? 'unavailable'}${delta ? `, change ${delta > 0 ? '+' : ''}${delta}` : ''}`);
      strip.append(stat);
    }
    host.append(strip);
    const comparison = disclosure(`stats:${row.unitId}`, 'Opening values and full comparison');
    const table = el('table', null, 'station-stats'); table.setAttribute('aria-label', 'Original and proposed normalized statistics');
    const header = el('tr'); for (const label of ['Stat', 'Original', 'Proposed', 'Change']) header.append(el('th', label)); table.append(header);
    for (const [key, label] of [['mov', 'MOV'], ['rng', 'RNG'], ['atk', 'ATK'], ['def', 'DEF'], ['hp', 'Max HP'], ['sightDistance', 'Sight']]) {
      const tr = el('tr'); tr.append(el('th', label));
      for (const value of [row.baseline?.[key], row[key], row.difference?.[key] == null ? null : row.difference[key] > 0 ? `+${row.difference[key]}` : row.difference[key]]) tr.append(el('td', value ?? '—'));
      table.append(tr);
    }
    comparison.append(table); host.append(comparison);
    host.append(el('p', `Movement: ${(row.movementCapabilities ?? []).join(', ') || 'None'}${row.hasPhase ? ' · Phase' : ''}`, 'station-movement'));
    const changes = [...(row.gainedMovementCapabilities ?? []).map(value => `Gained ${value}`), ...(row.lostMovementCapabilities ?? []).map(value => `Lost ${value}`)];
    if (row.hasPhase !== row.baselineHasPhase) changes.push(row.hasPhase ? 'Gained Phase' : 'Lost Phase');
    if (changes.length) host.append(el('p', changes.join(' · '), 'station-delta'));
    renderAbilities(row, host);
  }
  function renderRoster(state) {
    const view = getView(), station = view.presentation.decision.equipmentStation, objects = objectIndex();
    const active = document.activeElement?.dataset?.stationObject;
    roster.replaceChildren(); focusSelect.replaceChildren();
    if (!station.eligibleUnitIds.includes(focused)) focused = station.eligibleUnitIds[0];
    for (const id of station.eligibleUnitIds) {
      const option = el('option', unitName(id)); option.value = id; focusSelect.append(option);
      const card = el('section', null, 'station-unit'); card.dataset.stationUnit = id;
      card.append(el('h3', unitName(id)));
      const originals = station.originalAllocation.assignments.filter(value => value.unitId === id);
      card.append(el('p', `Original: ${originals.map(value => objectName(value.objectId)).join(', ') || 'No objects'}`, 'station-original'));
      const row = state.preview?.units?.find(row => row.unitId === id);
      if (row) renderStats(row, card);
      else card.append(el('p', state.loading ? 'Checking proposed comparison…' : 'Comparison unavailable. Retry the preview.'));
      card.append(el('h4', 'Proposed inventory'));
      const assignments = state.allocation?.assignments?.filter(value => value?.unitId === id) ?? [];
      if (!assignments.length) card.append(el('p', 'No objects'));
      for (const value of assignments) {
        const object = objects.get(value.objectId), item = el('div', null, 'station-object'); item.dataset.objectId = value.objectId;
        const owned = (state.preview?.units ?? []).flatMap(unit => unit.allocation ?? []).find(object => object.id === value.objectId);
        item.append(el('strong', objectName(value.objectId)));
        if (object?.content?.category) item.append(el('small', object.content.category === 'Item' ? 'Single Use Item · unconsumed' : object.content.category));
        const details = disclosure(`object:${value.objectId}`, 'Object details');
        if (object?.content?.description || object?.content?.text) details.append(el('p', object.content.description ?? object.content.text));
        details.append(el('small', `Physical ID: ${value.objectId}${owned?.lootCardId ? ` · Loot Card: ${owned.lootCardId}` : ''}`));
        item.append(details);
        for (const entry of object?.entries ?? []) if (entry.uses) item.append(el('small', `${entry.content.name}: ${usesText(entry.uses.remainingUses)}`));
        const label = el('label', 'Give to '), select = el('select'); select.dataset.stationObject = value.objectId;
        select.setAttribute('aria-label', `Recipient for ${objectName(value.objectId)}`);
        for (const recipient of station.eligibleUnitIds) { const option = el('option', unitName(recipient)); option.value = recipient; select.append(option); }
        select.value = value.unitId; select.disabled = !state.initialized || submitting || !ready();
        select.addEventListener('change', () => { message = `${objectName(value.objectId)} assigned to ${unitName(select.value)}. Checking the full allocation.`; draft.assign(value.objectId, select.value); });
        label.append(select); item.append(label); card.append(item);
      }
      roster.append(card);
    }
    const reserved = view.presentation.decision.reservedConsumptionObjectId;
    reservedWarning.hidden = !reserved;
    reservedWarning.textContent = reserved ? `${objectName(reserved)} is reserved for consumption on confirm and excluded from the proposed inventory. Cancel preserves it.` : '';
    updateFocus();
    if (active) {
      const recipient = [...roster.querySelectorAll('select')].find(node => node.dataset.stationObject === active);
      const hiddenPanel = globalThis.matchMedia?.('(max-width: 700px)').matches && recipient?.closest('section')?.dataset.stationUnit !== focused;
      (hiddenPanel ? focusSelect : recipient ?? focusSelect).focus({ preventScroll: true });
    }
  }
  function render(state = draft.state()) {
    const nextKey = state.boundary ? `${state.boundary.sessionId}\0${state.boundary.decisionId}` : null;
    if (nextKey !== interactionKey) {
      interactionKey = nextKey; focused = undefined; disclosures.clear(); savedAllocation = undefined; message = ''; renderedSignature = undefined;
      explanation.open = false;
    }
    if (!state.boundary) {
      closeModal(true);
      renderedSignature = undefined; return;
    }
    if (!visible()) {
      closeModal();
      return;
    }
    if (!dialog.open) {
      priorFocus = document.activeElement; priorScroll = { x: globalThis.scrollX ?? 0, y: globalThis.scrollY ?? 0 };
      dialog.showModal(); title.focus();
    }
    roster.setAttribute('aria-busy', String(state.loading));
    status.textContent = state.error || (submitting ? 'Submitting…' : state.loading ? message || 'Checking allocation…' : message ? `${message.replace(' Checking the full allocation.', '')} ${state.preview?.canConfirm ? 'Ready to confirm.' : 'Review the allocation below.'}` : state.preview?.canConfirm ? 'Ready to confirm.' : (state.preview?.diagnostics ?? []).some(value => value.code === 'no-transfer') ? 'Give an object to another Unit to begin.' : 'Adjust the allocation to continue.');
    diagnostics.replaceChildren();
    for (const value of state.preview?.diagnostics ?? []) if (value.code !== 'no-transfer') diagnostics.append(el('li', `${value.message}${value.unitId ? ` · ${unitName(value.unitId)}` : ''}${value.objectId ? ` · ${objectName(value.objectId)}` : ''}`));
    const signature = `${state.version}:${submitting}:${ready()}`;
    if (signature !== renderedSignature) { renderedSignature = signature; renderRoster(state); }
    const unavailable = submitting || !ready();
    confirm.disabled = unavailable || state.loading || !state.preview?.canConfirm;
    reset.disabled = unavailable || !state.initialized; retry.disabled = unavailable || state.loading;
    retry.hidden = !state.error; cancel.disabled = unavailable;
    save.hidden = !getView()?.expedition; save.disabled = unavailable || !state.initialized;
    returnToExpeditions.hidden = !getView()?.expedition || savedAllocation !== JSON.stringify(state.allocation);
    returnToExpeditions.disabled = unavailable;
  }
  async function submit(operation) {
    const state = draft.state();
    if (submitting || !ready() || !state.boundary || (operation === 'machine/confirm' && (state.loading || !state.preview?.canConfirm))) return;
    submitting = true; message = ''; render();
    try {
      const success = await command(operation, operation === 'machine/cancel' ? {} : { equipmentAllocation: state.allocation });
      if (draft.state().boundary?.sessionId !== state.boundary.sessionId || draft.state().boundary?.decisionId !== state.boundary.decisionId) return;
      if (!success) message = commandError() || 'The command could not complete. Retry when connected.';
      else if (operation === 'expedition/save') {
        savedAllocation = JSON.stringify(state.allocation);
        message = 'Draft saved. Return to Expeditions or continue editing here.';
      }
    } finally { submitting = false; render(); }
  }
  return { update() { draft.sync(getView()); render(); }, diagnostics: values => draft.diagnostics(values), allocation: () => draft.state().allocation, isOpen: () => dialog.open };
}
