import { createStationDraft } from './equipment-station-state.js';
import { physicalObjectLabel } from './loot-ui.js';

export function mountEquipmentStation({ getView, request, command, ready, visible = () => true, navigate = mode => globalThis.delveNavigate?.(mode), commandError = () => '' }) {
  const el = (tag, text, className) => {
    const node = document.createElement(tag); if (text != null) node.textContent = text;
    if (className) node.className = className; return node;
  };
  const dialog = el('dialog', null, 'equipment-station'); dialog.id = 'equipment-station-dialog';
  dialog.setAttribute('aria-labelledby', 'equipment-station-title');
  const title = el('h2', 'DelveMart'); title.id = 'equipment-station-title'; title.tabIndex = -1;
  const intro = el('p', 'Give objects to recipients, mark them for sale or choose upgrades. Confirm applies all changes together and spends one Operate Action, its Ability use and Costs. Cancel is free.');
  const credits = el('p', null, 'station-credits'); credits.id = 'equipment-station-credits'; credits.setAttribute('aria-live', 'polite');
  const sales = el('section', null, 'station-sales'); sales.id = 'equipment-station-sales'; sales.setAttribute('aria-label', 'Objects marked for sale');
  const normalized = el('p', 'Comparison: each Unit alone, Upright, at maximum HP on neutral terrain. Current HP and proposed HP show the actual transaction result. Unit upgrades restore HP to the final maximum and refresh native Uses. Equipment upgrades refresh that copy’s Uses. The opening Operate use is still paid.', 'station-explanation');
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
  const reset = button('reset', 'Reset draft', () => { message = ''; draft.reset(); });
  const retry = button('retry', 'Retry preview', () => { message = ''; void draft.retry(); });
  const save = button('save', 'Save draft', () => submit('expedition/save'));
  const cancel = button('cancel', 'Cancel', () => submit('machine/cancel'));
  const confirm = button('confirm', 'Confirm changes · 1 Action', () => submit('machine/confirm'));
  const recruitConfirm = button('recruit-confirm', 'Confirm recruitment · 1 Action', () => submit('machine/recruit'));
  recruitConfirm.className = 'station-primary';
  const returnToExpeditions = button('return', 'Back to Expeditions', () => { if (!submitting && ready()) navigate('expeditions'); }); returnToExpeditions.hidden = true;
  confirm.className = 'station-primary';
  const explanation = disclosure('normalization', 'How comparison works'); explanation.append(normalized);
  const managePanel = el('div');
  managePanel.append(intro, explanation, credits, sales, focusLabel, roster);
  const mode = el('button', 'Recruit'); mode.type = 'button'; mode.id = 'equipment-station-mode';
  const recruitPanel = el('section'); recruitPanel.id = 'equipment-station-recruitment';
  const recruitSummary = el('p'); recruitSummary.id = 'recruitment-party';
  const recruitChoice = el('select'); recruitChoice.id = 'recruitment-type';
  const recruitLabel = el('label', 'Recruit a Party member '); recruitLabel.append(recruitChoice);
  const recruitDetail = el('div'); recruitDetail.id = 'recruitment-detail';
  recruitPanel.append(recruitSummary, recruitLabel, recruitDetail);
  dialog.append(title, mode, reservedWarning, status, diagnostics, managePanel, recruitPanel, actions); document.body.append(dialog);
  let recruiting = false, recruitQuote, recruitType, recruitTicket = 0, recruitRevision, recruitError = '';
  let restoredRecruit = false, savedRecruit;
  mode.addEventListener('click', () => {
    if (submitting || !ready()) return;
    recruiting = !recruiting; message = ''; recruitError = '';
    if (recruiting) void quoteRecruitment(null); else { ++recruitTicket; render(); }
  });
  recruitChoice.addEventListener('change', () => { void quoteRecruitment(recruitChoice.value || null); });
  async function quoteRecruitment(typeId) {
    const boundary = draft.state().boundary;
    if (!boundary || !recruiting) return;
    const ticket = ++recruitTicket;
    recruitRevision = boundary.expectedRevision; recruitType = typeId; recruitQuote = null; recruitError = ''; render();
    try {
      const response = await request('/machine/recruitment/preview', { ...boundary, recruitTypeId: typeId });
      if (ticket !== recruitTicket) return;
      if (response.sessionId !== boundary.sessionId || response.decisionId !== boundary.decisionId || response.revision !== boundary.expectedRevision)
        throw new Error('The DelveMart changed. Reopen recruitment for a fresh preview.');
      recruitQuote = response.preview;
    } catch (error) { if (ticket === recruitTicket) recruitError = error.message; }
    if (ticket === recruitTicket) render();
  }
  function renderRecruitment(unavailable) {
    recruitConfirm.disabled = unavailable || !recruitQuote?.canConfirm;
    recruitChoice.disabled = unavailable || !recruitQuote?.offers?.length;
    recruitPanel.setAttribute('aria-busy', String(!recruitQuote && !recruitError));
    recruitSummary.textContent = recruitQuote ? `Party ${recruitQuote.partySize}/${recruitQuote.partyCapacity} · ${recruitQuote.currentCredits} Credits` : '';
    status.textContent = recruitError || message || (!recruitQuote ? 'Checking recruitment…' :
      recruitQuote.offers.length ? 'Recruitment spends one Action. Manage changes are separate.' : 'No Unit Type in this region has a recruitment price.');
    diagnostics.replaceChildren();
    for (const diagnostic of recruitQuote?.diagnostics ?? []) diagnostics.append(el('li', diagnostic.message));
    recruitChoice.replaceChildren();
    const placeholder = el('option', 'Choose a recruit'); placeholder.value = ''; recruitChoice.append(placeholder);
    for (const offer of recruitQuote?.offers ?? []) {
      const option = el('option', `${offer.name} · ${offer.cost} Credits${offer.canRecruit ? '' : ' · Unavailable'}`);
      option.value = offer.unitTypeId; recruitChoice.append(option);
    }
    recruitChoice.value = recruitType ?? '';
    recruitDetail.replaceChildren();
    const offer = recruitQuote?.offers.find(o => o.unitTypeId === recruitType);
    if (!offer) return;
    const type = offer.type;
    recruitDetail.append(el('h3', offer.name),
      el('p', `Level 1 · MOV ${type.mov} · RNG ${type.rng} · ATK ${type.atk} · DEF ${type.def} · HP ${type.hp}`));
    const abilities = (type.localAbilities ?? []).map(a => a.name);
    if (abilities.length) recruitDetail.append(el('p', abilities.join(' · ')));
    if (offer.spawnCell) recruitDetail.append(el('p', `Spawn Cell (${offer.spawnCell.x}, ${offer.spawnCell.y})`));
    recruitDetail.append(el('p', `Price ${offer.cost} Credits · Projected balance ${recruitQuote.resultingCredits} Credits`));
  }
  // Native modality makes the board inert and contains keyboard/touch interaction.
  dialog.addEventListener('cancel', event => { event.preventDefault(); if (!submitting && ready()) void submit('machine/cancel'); });
  dialog.addEventListener('keydown', event => event.stopPropagation());
  focusSelect.addEventListener('change', () => { focused = focusSelect.value; updateFocus(); });
  const rosterIds = station => {
    if (!station) return [];
    const preview = draft.state().preview;
    const targets = preview ? (preview.unitUpgrades ?? []).map(offer => offer.targetId) : station.eligibleUpgradeUnitIds ?? [];
    return [...new Set([...station.eligibleUnitIds, ...targets])];
  };
  const unitName = id => {
    const card = getView()?.presentation.cards?.[id];
    const name = card?.playerName ?? card?.individualName ?? card?.displayName ?? id;
    const copies = rosterIds(getView()?.presentation.decision?.equipmentStation).map(id => [id, getView()?.presentation.cards?.[id]])
      .filter(([, candidate]) => candidate &&
      (candidate.playerName ?? candidate.individualName ?? candidate.displayName) === name);
    const label = copies.length > 1 ? `${name} ${copies.findIndex(([key]) => key === id) + 1}` : name;
    return card?.level != null ? `${label} · Level ${card.level}` : label;
  };
  const objectIndex = () => new Map((getView()?.presentation.decision?.equipmentStation?.eligibleUnitIds ?? [])
    .flatMap(id => getView()?.presentation.cards?.[id]?.ownedObjects ?? []).map(value => [value.id, value]));
  const objectCopySuffix = id => {
    const objects = [...objectIndex().values()], object = objects.find(value => value.id === id);
    if (!object) return '';
    const copies = objects.filter(value => value.content?.name === object.content?.name);
    return copies.length > 1 ? ` (copy ${copies.findIndex(value => value.id === id) + 1})` : '';
  };
  const objectName = id => {
    const object = objectIndex().get(id);
    return object ? `${physicalObjectLabel(object)}${objectCopySuffix(id)}` : 'Unavailable object';
  };
  const proposedObjectName = id => {
    const object = objectIndex().get(id);
    const proposed = draft.state().preview?.units?.flatMap(unit => unit.allocation ?? []).find(value => value.id === id);
    return object ? `${physicalObjectLabel({ ...object, ...(proposed?.level != null ? { level: proposed.level } : {}) })}${objectCopySuffix(id)}` : 'Unavailable object';
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
  const maximumUsesText = ability => ability.definition.usage?.maxUses == null ? '' : ` · Maximum ${ability.definition.usage.maxUses} per game`;
  const sourceName = (ability, opening = false) => ability.sourceKind === 'Unit' ? 'Native' : objectIndex().has(ability.sourceInstanceId)
    ? (opening ? objectName(ability.sourceInstanceId) : proposedObjectName(ability.sourceInstanceId)) : ability.sourceName ?? ability.sourceTypeId;
  function renderAbilities(row, host) {
    const details = disclosure(`abilities:${row.unitId}`, 'Ability definitions and sources');
    details.dataset.stationAbilities = row.unitId;
    if (row.openingAbilityPreviews) {
      const opening = disclosure(`opening-abilities:${row.unitId}`, 'Opening Abilities and Uses');
      opening.dataset.stationOpeningAbilities = row.unitId;
      for (const entry of row.openingAbilityPreviews) {
        const ability = entry.ability, content = entry.content, panel = el('div', null, 'station-ability');
        panel.append(el('strong', content?.name ?? ability.definition.name));
        panel.append(el('p', `${content?.category ?? ability.definition.timing} · ${sourceName(ability, true)}`));
        panel.append(el('p', content?.description ?? ability.definition.descriptionOverride ?? ''));
        panel.append(el('small', usesText(entry.remainingUses) + maximumUsesText(ability)));
        opening.append(panel);
      }
      if (!row.openingAbilityPreviews.length) opening.append(el('p', 'No opening Abilities'));
      details.append(opening, el('h4', 'Proposed Abilities and Uses'));
    }
    const gained = new Set((row.gainedAbilities ?? []).map(abilityIdentity));
    const summaryName = entry => `${entry.content?.name ?? entry.ability.definition.name}${entry.ability.sourceKind === 'Unit' ? '' : objectCopySuffix(entry.ability.sourceInstanceId)}`;
    const gains = (row.abilityPreviews ?? []).filter(entry => gained.has(abilityIdentity(entry.ability))).map(summaryName);
    const losses = (row.lostAbilityPreviews ?? []).map(summaryName);
    if (gains.length) host.append(el('p', `Gained: ${gains.join(', ')}`, 'station-delta'));
    if (losses.length) host.append(el('p', `Lost: ${losses.join(', ')}`, 'station-delta'));
    for (const { entry, lost } of [...(row.abilityPreviews ?? []).map(entry => ({ entry, lost: false })), ...(row.lostAbilityPreviews ?? []).map(entry => ({ entry, lost: true }))]) {
      const ability = entry.ability, content = entry.content;
      const panel = el('div', null, 'station-ability');
      panel.append(el('strong', `${lost ? 'Lost: ' : gained.has(abilityIdentity(ability)) ? 'Gained: ' : ''}${content?.name ?? ability.definition.name}`));
      const timing = content?.category ?? ability.definition.timing;
      const passive = ability.definition.timing === 'Passive';
      panel.append(el('p', `${timing}${passive ? ' effect' : ' Ability'} · ${sourceName(ability, lost)}`));
      panel.append(el('p', content?.description ?? ability.definition.descriptionOverride ?? ''));
      panel.append(el('small', `${usesText(entry.remainingUses)}${maximumUsesText(ability)}${passive ? entry.applicable ? ' · Applies in this comparison' : ' · Does not apply in this comparison' : ''}`));
      details.append(panel);
    }
    host.append(details);
  }
  function renderStats(row, host) {
    if (row.proposedAllocationAvailable === false) { host.append(el('p', 'Proposed comparison unavailable. Repair the allocation or reset it.')); return; }
    if (row.currentLevel != null && row.resultingLevel != null && row.currentLevel !== row.resultingLevel)
      host.append(el('p', `Level ${row.currentLevel} → Level ${row.resultingLevel}`, 'station-delta'));
    if (row.currentHp != null && row.resultingHp != null)
      host.append(el('p', `Current HP ${row.currentHp} → Proposed HP ${row.resultingHp}`, 'station-live-hp'));
    host.append(el('small', 'NORMALIZED COMPARISON', 'station-normalized-label'));
    const strip = el('div', null, 'station-stat-strip');
    const stats = [['mov', 'MOV'], ['rng', 'RNG'], ['atk', 'ATK'], ['def', 'DEF'], ['hp', 'Max HP'], ['sightDistance', 'Sight']];
    const budgets = [['moves', 'Moves per activation'], ['actions', 'Actions per activation'], ['bonusActionUses', 'Uses per Bonus Action']]
      .filter(([key]) => row[key] != null);
    for (const [key, label] of [...stats, ...budgets.filter(([key]) => row.difference?.[key])]) {
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
    for (const [key, label] of [...stats, ...budgets]) {
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
  function upgradeControl(offer, kind, state) {
    const selected = (kind === 'unit' ? state.allocation?.upgradedUnitIds : state.allocation?.upgradedEquipmentIds)?.includes(offer.targetId) ?? false;
    const button = el('button', `${selected ? 'Undo upgrade' : 'Upgrade'} · Level ${offer.currentLevel} → ${offer.nextLevel} · ${offer.cost} Credits`, 'station-upgrade');
    button.type = 'button'; button.setAttribute('aria-pressed', String(selected));
    button.setAttribute('aria-label', `${selected ? 'Undo upgrade of' : 'Upgrade'} ${kind === 'unit' ? unitName(offer.targetId) : objectName(offer.targetId)} to Level ${offer.nextLevel} for ${offer.cost} Credits`);
    if (kind === 'unit') button.dataset.stationUpgradeUnit = offer.targetId;
    else button.dataset.stationUpgradeEquipment = offer.targetId;
    button.disabled = !state.initialized || submitting || !ready();
    button.addEventListener('click', () => {
      message = `${kind === 'unit' ? unitName(offer.targetId) : objectName(offer.targetId)} ${selected ? 'upgrade removed' : 'marked for upgrade'}. Checking the full allocation.`;
      if (kind === 'unit') draft.upgradeUnit(offer.targetId); else draft.upgradeEquipment(offer.targetId);
    });
    return button;
  }
  function renderRoster(state) {
    const view = getView(), station = view.presentation.decision.equipmentStation, objects = objectIndex();
    const active = document.activeElement?.dataset?.stationObject ?? document.activeElement?.dataset?.stationSell ?? document.activeElement?.dataset?.stationRestore
      ?? document.activeElement?.dataset?.stationUpgradeUnit ?? document.activeElement?.dataset?.stationUpgradeEquipment;
    roster.replaceChildren(); focusSelect.replaceChildren();
    const ids = rosterIds(station);
    if (!ids.includes(focused)) focused = ids[0];
    for (const id of ids) {
      const option = el('option', unitName(id)); option.value = id; focusSelect.append(option);
      const card = el('section', null, 'station-unit'); card.dataset.stationUnit = id;
      card.append(el('h3', unitName(id)));
      const unitUpgrade = state.preview?.unitUpgrades?.find(offer => offer.targetId === id);
      if (unitUpgrade) card.append(upgradeControl(unitUpgrade, 'unit', state));
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
        if (owned?.level != null && owned.level !== (object?.level ?? 1))
          item.append(el('small', `Proposed Level ${owned.level}`, 'station-delta'));
        if (object?.content?.category) item.append(el('small', object.content.category === 'Item' ? 'Single Use Item · unconsumed' : object.content.category));
        const details = disclosure(`object:${value.objectId}`, 'Opening object details');
        if (object?.content?.description || object?.content?.text) details.append(el('p', object.content.description ?? object.content.text));
        details.append(el('small', `Physical ID: ${value.objectId}${owned?.lootCardId ? ` · Loot Card: ${owned.lootCardId}` : ''}`));
        item.append(details);
        const proposedUses = row?.abilityPreviews?.filter(entry => entry.ability.sourceInstanceId === value.objectId);
        if (proposedUses) {
          for (const entry of proposedUses) if (entry.remainingUses != null)
            item.append(el('small', `${entry.content?.name ?? entry.ability.definition.name}: ${usesText(entry.remainingUses)} after Confirm`));
        } else for (const entry of object?.entries ?? []) if (entry.uses) item.append(el('small', `${entry.content.name}: ${usesText(entry.uses.remainingUses)}`));
        const label = el('label', 'Give to '), select = el('select'); select.dataset.stationObject = value.objectId;
        select.setAttribute('aria-label', `Recipient for ${objectName(value.objectId)}`);
        for (const recipient of station.eligibleUnitIds) { const option = el('option', unitName(recipient)); option.value = recipient; select.append(option); }
        select.value = value.unitId; select.disabled = !state.initialized || submitting || !ready();
        select.addEventListener('change', () => { message = `${objectName(value.objectId)} assigned to ${unitName(select.value)}. Checking the full allocation.`; draft.assign(value.objectId, select.value); });
        label.append(select); item.append(label); card.append(item);
        const equipmentUpgrade = state.preview?.equipmentUpgrades?.find(offer => offer.targetId === value.objectId);
        if (equipmentUpgrade) item.append(upgradeControl(equipmentUpgrade, 'equipment', state));
        const price = object?.sellValue;
        item.append(el('small', price == null ? 'Unsellable' : `Sell Value: ${price} Credits`));
        if (price != null) {
          const sell = el('button', `Sell for ${price} Credits`); sell.type = 'button'; sell.dataset.stationSell = value.objectId;
          sell.disabled = !state.initialized || submitting || !ready();
          sell.addEventListener('click', () => { message = `${objectName(value.objectId)} marked for sale. Checking the full allocation.`; draft.sell(value.objectId); });
          item.append(sell);
        }
      }
      roster.append(card);
    }
    sales.replaceChildren(el('h3', 'Marked for sale'));
    const soldIds = state.allocation?.soldObjectIds ?? [];
    sales.hidden = !soldIds.length;
    for (const id of soldIds) {
      const sale = state.preview?.sales?.find(value => value.objectId === id);
      const item = el('div', null, 'station-object'); item.dataset.soldObjectId = id;
      item.append(el('strong', objectName(id)), el('span', sale ? `${sale.sellValue} Credits` : 'Awaiting validated sale value'));
      const label = el('label', 'Undo sale · Give to '), select = el('select'); select.dataset.stationRestore = id;
      select.setAttribute('aria-label', `Undo sale of ${objectName(id)} and give to`);
      const placeholder = el('option', 'Choose recipient'); placeholder.value = ''; select.append(placeholder);
      for (const recipient of station.eligibleUnitIds) { const option = el('option', unitName(recipient)); option.value = recipient; select.append(option); }
      select.value = ''; select.disabled = !state.initialized || submitting || !ready();
      select.addEventListener('change', () => { if (!select.value) return; message = `${objectName(id)} restored to ${unitName(select.value)}. Checking the full allocation.`; draft.assign(id, select.value); });
      label.append(select); item.append(label); sales.append(item);
    }
    const reserved = view.presentation.decision.reservedConsumptionObjectId;
    reservedWarning.hidden = !reserved;
    reservedWarning.textContent = reserved ? `${objectName(reserved)} is reserved for consumption on confirm and excluded from the proposed inventory. Cancel preserves it.` : '';
    updateFocus();
    if (active) {
      const recipient = [...roster.querySelectorAll('select'), ...sales.querySelectorAll('select'), ...roster.querySelectorAll('button')].find(node =>
        node.dataset.stationObject === active || node.dataset.stationRestore === active || node.dataset.stationUpgradeUnit === active || node.dataset.stationUpgradeEquipment === active);
      const hiddenPanel = globalThis.matchMedia?.('(max-width: 700px)').matches && recipient?.closest('section')?.dataset.stationUnit !== focused;
      (hiddenPanel ? focusSelect : recipient ?? focusSelect).focus({ preventScroll: true });
    }
  }
  function render(state = draft.state()) {
    const nextKey = state.boundary ? `${state.boundary.sessionId}\0${state.boundary.decisionId}` : null;
    if (nextKey !== interactionKey) {
      interactionKey = nextKey; focused = undefined; disclosures.clear(); savedAllocation = undefined; message = ''; renderedSignature = undefined;
      recruiting = false; recruitQuote = null; recruitType = null; recruitRevision = undefined; ++recruitTicket;
      restoredRecruit = false; savedRecruit = undefined;
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
    const view = getView();
    if (state.initialized && state.savedRecruitTypeId && !restoredRecruit) {
      restoredRecruit = true; recruiting = true; savedRecruit = state.savedRecruitTypeId;
      void quoteRecruitment(savedRecruit); return;
    }
    const side = view.result?.state?.party?.sideId;
    mode.hidden = !view.presentation.decision?.equipmentStation?.canRecruit && !(side && side === view.result?.state?.machineInteraction?.sideId);
    mode.disabled = submitting || !ready(); mode.textContent = recruiting ? 'Back to Manage' : 'Recruit';
    recruitPanel.hidden = !recruiting; managePanel.hidden = recruiting;
    confirm.hidden = recruiting; recruitConfirm.hidden = !recruiting; reset.hidden = recruiting;
    cancel.disabled = submitting || !ready();
    if (recruiting) {
      save.hidden = !view.expedition; save.disabled = submitting || !ready() || !recruitType || !recruitQuote;
      retry.hidden = true; returnToExpeditions.hidden = !view.expedition || !savedRecruit || savedRecruit !== recruitType;
      if (recruitRevision !== state.boundary.expectedRevision) { void quoteRecruitment(recruitType); return; }
      renderRecruitment(cancel.disabled); return;
    }
    roster.setAttribute('aria-busy', String(state.loading));
    credits.textContent = state.preview?.currentCredits != null ? `Current Credits: ${state.preview.currentCredits} · Sale proceeds: ${state.preview.totalProceeds} · Upgrade costs: ${state.preview.totalUpgradeCost ?? 0} · Resulting Credits: ${state.preview.resultingCredits ?? 'Unavailable'}` : state.loading ? 'Checking Credits…' : 'Credits preview unavailable.';
    status.textContent = state.error || (submitting ? 'Submitting…' : state.loading ? message || 'Checking allocation…' : message ? `${message.replace(' Checking the full allocation.', '')} ${state.preview?.canConfirm ? 'Ready to confirm.' : 'Review the allocation below.'}` : state.preview?.canConfirm ? 'Ready to confirm.' : (state.preview?.diagnostics ?? []).some(value => value.code === 'no-transfer') ? 'Choose an upgrade, give an object to another Unit or mark one for sale to begin.' : 'Adjust the allocation to continue.');
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
    if (operation === 'machine/recruit' && (!recruiting || !recruitQuote?.canConfirm)) return;
    submitting = true; message = ''; render();
    try {
      const body = operation === 'machine/recruit' || (recruiting && operation === 'expedition/save') ? { recruitTypeId: recruitType }
        : operation === 'machine/cancel' ? {} : { equipmentAllocation: state.allocation };
      const success = await command(operation, body);
      if (draft.state().boundary?.sessionId !== state.boundary.sessionId || draft.state().boundary?.decisionId !== state.boundary.decisionId) return;
      if (!success) message = commandError() || 'The command could not complete. Retry when connected.';
      else if (operation === 'expedition/save') {
        savedAllocation = recruiting ? undefined : JSON.stringify(state.allocation);
        savedRecruit = recruiting ? recruitType : undefined;
        message = 'Draft saved. Return to Expeditions or continue editing here.';
      }
    } finally { submitting = false; render(); }
  }
  return { update() { draft.sync(getView()); render(); }, diagnostics: values => {
    if (recruiting) { recruitQuote = null; recruitError = values.map(value => value.message).join(' '); render(); }
    else draft.diagnostics(values);
  }, allocation: () => draft.state().allocation, isOpen: () => dialog.open };
}
