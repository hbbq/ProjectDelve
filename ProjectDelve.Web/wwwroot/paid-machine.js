// Each button confirms one complete Engine offer at the displayed decision.
export function mountPaidMachine({ getView, ready, visible, command, commandError }) {
  const byId = id => document.getElementById('paid-machine-' + id);
  const dialog = byId('dialog'), offers = byId('offers'), title = byId('title');
  const account = byId('account'), message = byId('message'), cancel = byId('cancel'), save = byId('save');
  let boundary, submitting = false;

  async function submit(operation, body = {}) {
    if (!dialog.open || !ready() || submitting) return;
    const opened = boundary;
    submitting = true;
    update();
    try {
      const success = await command(operation, body);
      if (opened === boundary) {
        if (!success) message.textContent = commandError() || 'The decision changed. Check the current offer.';
        else if (operation === 'expedition/save') message.textContent = 'Expedition saved with this Machine open.';
      }
    } finally { submitting = false; update(); }
  }

  const paragraph = text => {
    const node = document.createElement('p');
    node.textContent = text; return node;
  };
  function update() {
    const view = getView();
    const quote = view?.presentation?.decision?.paidMachine;
    if (!quote || !view.decisionId || !visible()) {
      if (dialog.open) dialog.close();
      boundary = null; return;
    }
    const key = JSON.stringify([view.sessionId, view.decisionId, view.revision]);
    if (key !== boundary) {
      boundary = key; message.textContent = '';
      const machine = view.presentation.machines.find(m => m.id === quote.machineId);
      const card = view.presentation.cards[quote.operatorId];
      const operator = card?.playerName ?? card?.displayName ?? quote.operatorId;
      title.textContent = machine?.name ?? quote.machineId;
      account.textContent = `${operator} · HP ${quote.currentHp}/${quote.effectiveMaxHp} · ${quote.currentCredits} Credits`;
      offers.replaceChildren();
      for (const offer of quote.offers) {
        const row = document.createElement('section'), heading = document.createElement('h3');
        heading.textContent = offer.name; row.append(heading);
        if (offer.itemTypeId) row.append(paragraph(`${operator} receives one ${offer.itemName ?? offer.itemTypeId} Single Use Item.`));
        else row.append(paragraph(`HP ${quote.currentHp} → ${offer.resultingHp} of ${quote.effectiveMaxHp}`));
        row.append(paragraph(offer.resultingCredits < 0
          ? `Short by ${-offer.resultingCredits} Credits` : `Credits ${quote.currentCredits} → ${offer.resultingCredits}`));
        for (const reason of offer.diagnostics) row.append(paragraph(reason.message));
        const confirm = document.createElement('button');
        confirm.type = 'button'; confirm.className = 'station-primary'; confirm.dataset.serviceId = offer.serviceId;
        confirm.textContent = `Confirm · ${offer.cost} Credits`;
        confirm.setAttribute('aria-label', `Confirm ${offer.name} for ${offer.cost} Credits`);
        confirm.addEventListener('click', () => submit('machine/service', { serviceId: offer.serviceId }));
        row.append(confirm); offers.append(row);
      }
    }
    const blocked = submitting || !ready();
    const buttons = offers.querySelectorAll('button');
    quote.offers.forEach((offer, i) => { buttons[i].disabled = blocked || !offer.canConfirm; });
    cancel.disabled = blocked; save.disabled = blocked; save.hidden = !view.expedition;
    if (!dialog.open) { dialog.showModal(); title.focus(); }
  }
  cancel.addEventListener('click', () => submit('machine/cancel'));
  save.addEventListener('click', () => submit('expedition/save'));
  dialog.addEventListener('cancel', event => { event.preventDefault(); void submit('machine/cancel'); });
  dialog.addEventListener('keydown', event => event.stopPropagation());
  return { update, diagnostics(values) { if (dialog.open) message.textContent = values.map(d => d.message).join(' '); } };
}
