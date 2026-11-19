// Values here are supplied by the authoritative state, including its visibility projection.
export function creditsPiles(state) { return state?.creditsPiles ?? []; }

export function creditsBalanceLabel(state, viewingSideId, objectives = []) {
  const side = viewingSideId ?? state?.pending?.token?.sideId ?? state?.activeToken?.sideId ?? state?.units?.find(unit => unit.id === state.currentUnitId)?.sideId;
  const balances = state?.sideCredits ?? {};
  const label = id => `Side ${id} · Credits ${Object.hasOwn(balances, id) ? balances[id] : 0}`;
  if (side != null) return label(side);
  return [...new Set([...objectives.map(objective => objective.sideId), ...Object.keys(balances)])].map(label).join(' | ');
}
