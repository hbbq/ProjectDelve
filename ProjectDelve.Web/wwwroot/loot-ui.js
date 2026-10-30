// Presentation of already-observed objects. No pickup legality or deck inference.
export function groundLoot(state, objects = state.groundObjects) {
  const cells = new Map();
  const at = position => {
    const key = `${position.x},${position.y}`;
    if (!cells.has(key)) cells.set(key, { position, unopened: 0, objects: [] });
    return cells.get(key);
  };
  for (const marker of state.lootMarkers ?? []) at(marker.position).unopened++;
  for (const known of objects ?? []) at(known.position).objects.push(known.object);
  return [...cells.values()].map(cell => ({ ...cell,
    label: [cell.unopened ? `${cell.unopened} unopened Loot` : '', ...cell.objects.map(o => o.content?.name ?? o.kind)].filter(Boolean).join(' · ')
  }));
}

export function lootSymbol(kind) {
  return { PocketBreakfast: '🥐', SecondWind: '🌬', Bedsheet: '👻', LuckyBoots: '🥾', OfficialKey: '🗝', GoatBell: '🔔' }[kind] ?? '◆';
}

export function lootCardDialog(content, human, done, skip = done) {
  const previous = document.activeElement;
  const layer = document.createElement('div'); layer.className = 'loot-overlay';
  const card = document.createElement('section'); card.className = 'loot-reveal';
  card.setAttribute('role', 'dialog'); card.setAttribute('aria-modal', 'true'); card.setAttribute('aria-label', content.name);
  const node = (tag, value, className) => { const e = document.createElement(tag); e.textContent = value; if (className) e.className = className; return e; };
  card.append(node('small', `LOOT · ${content.category}`), node('div', content.category === 'Equipment' || content.itemTypeId != null ? '◆'
    : lootSymbol(content.lootCardId != null ? content.legacyKind : content.kind), 'loot-illustration'),
    node('h2', content.name), node('p', content.text));
  const close = node('button', human ? 'Continue' : 'Continue now', 'loot-continue'); close.type = 'button'; close.onclick = done;
  const skipButton = node('button', 'Skip remaining effects', 'loot-skip'); skipButton.type = 'button'; skipButton.onclick = skip;
  card.append(close, skipButton); layer.append(card); document.body.append(layer); close.focus();
  const key = event => { if (event.key === 'Escape') { event.preventDefault(); done(); } if (event.key === 'Tab') { event.preventDefault(); (document.activeElement === close ? skipButton : close).focus(); } };
  layer.addEventListener('keydown', key);
  return () => { layer.remove(); if (previous?.isConnected) previous.focus({ preventScroll: true }); };
}
