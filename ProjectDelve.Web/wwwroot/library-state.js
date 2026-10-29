// Navigation and presentation use public library metadata only, never scenario definitions.
export function libraryRoute(hash) {
  const [kind, rawId] = String(hash || '#home').replace(/^#/, '').split('/');
  const mode = kind === 'playtest' ? 'play' : kind;
  if (!['home', 'scenarios', 'scenario', 'campaign', 'campaigns-edit', 'designer', 'play'].includes(mode)) return { mode: 'home' };
  let id;
  try { id = rawId ? decodeURIComponent(rawId) : undefined; } catch { return { mode: 'home' }; }
  return { mode, id };
}
export function campaignEntries(campaign, scenarios) {
  const byId = new Map(scenarios.map(s => [s.id, s]));
  return campaign.scenarioIds.map(id => byId.get(id) ?? { id, name: 'Unavailable scenario', missing: true });
}
export function filterLibrary(entries, search) {
  const term = search.trim().toLocaleLowerCase();
  return entries.filter(entry => entry.name.toLocaleLowerCase().includes(term));
}
