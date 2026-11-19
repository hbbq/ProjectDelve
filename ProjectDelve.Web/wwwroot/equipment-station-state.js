// Draft editing changes assignments only. The Engine owns every preview and legality check.
const copy = allocation => allocation == null ? null : structuredClone(allocation);
export const stationBoundary = view => view?.presentation?.decision?.equipmentStation && view.sessionId && view.decisionId && Number.isSafeInteger(view.revision)
  ? { sessionId: view.sessionId, expectedRevision: view.revision, decisionId: view.decisionId } : null;
const identity = boundary => boundary && `${boundary.sessionId}\0${boundary.decisionId}`;
export function createStationDraft({ preview, changed = () => {} }) {
  let boundary, opening, allocation, result, error = '', loading = false, generation = 0, initialized = false, version = 0;
  const state = () => ({ boundary, allocation: copy(allocation), preview: result, error, loading, initialized, version });
  const publish = () => { ++version; changed(state()); };
  async function evaluate(useSaved = false) {
    if (!boundary) return;
    const ticket = ++generation, requested = { ...boundary };
    loading = true; result = null; error = ''; publish();
    try {
      const response = await preview({ ...requested, ...(useSaved ? {} : { allocation: copy(allocation) }) });
      if (ticket !== generation || identity(boundary) !== identity(requested) || boundary.expectedRevision !== requested.expectedRevision) return;
      if (response.sessionId !== requested.sessionId || response.decisionId !== requested.decisionId || response.revision !== requested.expectedRevision)
        throw new Error('The Station changed. Retry the preview.');
      if (useSaved) allocation = copy(response.savedDraft ?? opening);
      result = response.preview; initialized = true;
    } catch (failure) {
      if (ticket !== generation) return;
      result = null; error = failure.message;
    } finally {
      if (ticket === generation) { loading = false; publish(); }
    }
  }
  return {
    state,
    sync(view) {
      const next = stationBoundary(view);
      if (!next) { if (boundary) { ++generation; boundary = null; result = null; allocation = null; initialized = false; loading = false; publish(); } return; }
      if (identity(next) !== identity(boundary)) {
        ++generation; boundary = next; opening = copy(view.presentation.decision.equipmentStation.originalAllocation);
        allocation = copy(opening); result = null; initialized = false;
        void evaluate(true);
      } else if (next.expectedRevision !== boundary.expectedRevision) {
        boundary = next; void evaluate(!initialized);
      }
    },
    assign(objectId, unitId) {
      if (!initialized || !boundary) return;
      allocation = { assignments: allocation.assignments.map(value => value?.objectId === objectId ? { objectId, unitId } : value) };
      void evaluate();
    },
    reset() { if (!initialized || !boundary) return; allocation = copy(opening); void evaluate(); },
    retry() { return evaluate(!initialized); },
    diagnostics(values) { if (result) { result = { ...result, canConfirm: false, diagnostics: values }; publish(); } }
  };
}
