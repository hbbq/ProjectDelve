import { resolveCursorAsset } from './presentation-cursors.js';

// Presentation-owned data. Profile IDs never determine legality or command identity.
const cssCursors = new Set(['auto', 'default', 'none', 'context-menu', 'help', 'pointer', 'progress', 'wait',
  'cell', 'crosshair', 'text', 'vertical-text', 'alias', 'copy', 'move', 'no-drop', 'not-allowed',
  'grab', 'grabbing', 'all-scroll', 'col-resize', 'row-resize', 'n-resize', 'e-resize', 's-resize',
  'w-resize', 'ne-resize', 'nw-resize', 'se-resize', 'sw-resize', 'ew-resize', 'ns-resize',
  'nesw-resize', 'nwse-resize', 'zoom-in', 'zoom-out']);
const emptyPresentationProfiles = Object.freeze({ profiles: Object.freeze(Object.create(null)), descriptors: Object.freeze([]) });
let presentationProfileCatalog = emptyPresentationProfiles;
let presentationProfileLoading;

export function validatePresentationProfiles(value) {
  if (value?.schemaVersion !== 1 || !Array.isArray(value.profiles)) throw Error('Unsupported presentation profile catalog');
  const profiles = Object.create(null), descriptors = [];
  for (const profile of value.profiles) {
    if (!profile || typeof profile.id !== 'string' || !profile.id.trim() || profile.id !== profile.id.trim() ||
        typeof profile.name !== 'string' || !profile.name.trim() || Object.hasOwn(profiles, profile.id))
      throw Error('Presentation profiles require unique nonblank IDs and display names');
    const asset = typeof profile.cursor === 'string';
    if (profile.cursor !== undefined && (asset ? !/^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/.test(profile.cursor) :
        !profile.cursor || typeof profile.cursor !== 'object' || Array.isArray(profile.cursor) ||
        Object.keys(profile.cursor).some(key => key !== 'css') || !cssCursors.has(profile.cursor.css)))
      throw Error(`Invalid presentation cursor: ${profile.id}`);
    profiles[profile.id] = Object.freeze({ id: profile.id, name: profile.name,
      ...(profile.cursor === undefined ? {} : { cursor: asset ? profile.cursor : Object.freeze({ css: profile.cursor.css }) }) });
    descriptors.push(Object.freeze({ id: profile.id, name: profile.name }));
  }
  return Object.freeze({ profiles: Object.freeze(profiles), descriptors: Object.freeze(descriptors) });
}

// Authoring consumes only this public descriptor list, without renderer configuration.
export function presentationProfileDescriptors() { return presentationProfileCatalog.descriptors; }

export function resolvePresentationCursor(id, fallback, catalog = presentationProfileCatalog) {
  const cursor = catalog?.profiles?.[id]?.cursor;
  return typeof cursor === 'string' ? resolveCursorAsset(cursor, fallback) : cursor?.css ?? fallback;
}

export function loadPresentationProfiles(fetcher = globalThis.fetch) {
  return presentationProfileLoading ??= (async () => {
    const response = await fetcher('./presentation/profiles.json');
    if (!response.ok) throw Error(`Presentation catalog HTTP ${response.status}`);
    presentationProfileCatalog = validatePresentationProfiles(await response.json());
    return presentationProfileCatalog.descriptors;
  })().catch(() => {
    // A missing or invalid catalog never prevents play, editing, or preservation of IDs.
    presentationProfileLoading = null;
    return presentationProfileCatalog.descriptors;
  });
}
