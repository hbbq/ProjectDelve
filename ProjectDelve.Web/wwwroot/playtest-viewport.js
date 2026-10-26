// Presentation handoffs never cancel the shared playback queue or submit commands.
export function createViewportRouter({ dom, loadThree, overlay, onChange = () => {}, onError = () => {} }) {
  let active = dom, requested = null, inEvent = false, loading, three, disposed = false, switching;
  async function checkpoint() {
    if (inEvent || disposed || !requested) return;
    if (switching) return switching;
    switching = (async () => {
      while (requested && !inEvent && !disposed) {
        const selection = requested; requested = null;
        try {
          const next = selection === 'three' ? (three ??= await (loading ??= loadThree())) : dom;
          if (disposed) { next.dispose?.(); return; }
          // Another selection may have superseded an asynchronous import.
          if (requested) continue;
          // SSE may have started an event while the lazy module was loading.
          if (inEvent) { requested = selection; break; }
          if (next !== active) {
            const previous = active;
            previous.setActive(false);
            try {
              next.host.append(overlay);
              next.setActive(true);
              active = next;
            } catch (error) {
              next.setActive(false);
              previous.host.append(overlay);
              previous.setActive(true);
              throw error;
            }
          }
          onChange(selection, active);
        } catch (error) {
          loading = null;
          onError(error, active === dom ? dom.mode : 'three');
        }
      }
    })();
    try { await switching; } finally { switching = null; }
  }
  return {
    get active() { return active; },
    request(selection) { requested = selection; return checkpoint(); },
    beginEvent() { inEvent = true; },
    checkpoint() { inEvent = false; return checkpoint(); },
    dispose() { disposed = true; requested = null; dom.dispose?.(); three?.dispose(); },
    inspect: () => ({ active: active === dom ? dom.mode : 'three', pending: requested, inEvent })
  };
}

export const boardCellKey = cell => `${cell.x},${cell.y}`;
export const boardEdgeKey = edge => [boardCellKey(edge.a), boardCellKey(edge.b)].sort().join('|');

// Interaction mapping only: supplied references define legality and candidate order.
export function boardCandidates(hit, decision, state, geometry, visible = () => true) {
  if (!hit) return [];
  const occupied = figure => geometry?.[figure.id]?.occupiedCells ?? [figure.position];
  return [...(decision?.candidates ?? []), ...(decision?.noneChoice ? [decision.noneChoice] : [])].filter(candidate => {
    if (!visible(candidate)) return false;
    const reference = candidate.interaction;
    if (hit.door) return reference.kind === 'Door' && boardEdgeKey(reference.door) === boardEdgeKey(hit.door);
    if (reference.kind === 'Position') return boardCellKey(reference.position) === boardCellKey(hit.cell);
    if (reference.kind === 'Unit') {
      const figure = state.physical.figures.find(f => f.id === reference.unitId);
      return !!figure && occupied(figure).some(c => boardCellKey(c) === boardCellKey(hit.cell));
    }
    return false;
  });
}

export function tokenDetail(diameter, previous) {
  const thresholds = [28, 48, 80];
  let level = previous ?? thresholds.filter(n => diameter >= n).length;
  while (level < 3 && diameter >= thresholds[level] + 4) level++;
  while (level > 0 && diameter < thresholds[level - 1] - 4) level--;
  return level;
}

export function interpolatePose(from, to, fraction) {
  const t = Math.max(0, Math.min(1, fraction)), ease = 1 - (1 - t) ** 3;
  return { x: from.x + (to.x - from.x) * ease, y: from.y + (to.y - from.y) * ease,
    scale: Math.exp(Math.log(from.scale) + (Math.log(to.scale) - Math.log(from.scale)) * ease) };
}
