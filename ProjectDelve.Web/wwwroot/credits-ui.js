// Values here are supplied by the authoritative state, including its visibility projection.
export function creditsPiles(state) { return state?.creditsPiles ?? []; }

// Visual tiers are deliberately bounded; the exact amount is always labelled.
// These offsets reserve the lower-right of a Cell beside Loot and ground props.
export function creditsPileVisual(amount) {
  const tier = amount <= 5 ? 1 : amount <= 20 ? 2 : 3;
  return { tier, coins: [2, 4, 7][tier - 1], x: .80, z: .70, radius: .115 };
}

export function creditsBalanceLabel(state, viewingSideId, objectives = []) {
  const side = viewingSideId ?? state?.pending?.token?.sideId ?? state?.activeToken?.sideId ?? state?.units?.find(unit => unit.id === state.currentUnitId)?.sideId;
  const balances = state?.sideCredits ?? {};
  const label = id => `Side ${id} · Credits ${Object.hasOwn(balances, id) ? balances[id] : 0}`;
  if (side != null) return label(side);
  return [...new Set([...objectives.map(objective => objective.sideId), ...Object.keys(balances)])].map(label).join(' | ');
}
