// Runtime assets and preferences belong only to presentation. URLs come from the
// generated, validated catalog, never an imported ability/profile identifier.
export const cursorSizes = Object.freeze([32, 40, 48]);
const cursorSizeStorageKey = 'delve.cursor.size.v1';
const cursorIdPattern = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
let selectedCursorSize = 40;
try {
  const saved = Number(globalThis.localStorage?.getItem(cursorSizeStorageKey));
  if (cursorSizes.includes(saved)) selectedCursorSize = saved;
} catch { /* Optional preference storage. */ }
let cursorAssets = Object.freeze(Object.create(null)), cursorHotspots = Object.freeze(Object.create(null));
let cursorAssetLoading, cursorFeedbackQueued = false;
const cursorFeedbackListeners = new Set();

function cursorFeedbackChanged() {
  if (cursorFeedbackQueued) return;
  cursorFeedbackQueued = true;
  const schedule = globalThis.requestAnimationFrame ?? (callback => Promise.resolve().then(callback));
  schedule(() => { cursorFeedbackQueued = false; for (const listener of cursorFeedbackListeners) listener(); });
}
export function onCursorPresentationChange(listener) {
  cursorFeedbackListeners.add(listener); return () => cursorFeedbackListeners.delete(listener);
}
export function cursorSize() { return selectedCursorSize; }
export function setCursorSize(value) {
  const size = Number(value);
  if (!cursorSizes.includes(size)) return false;
  selectedCursorSize = size;
  try { globalThis.localStorage?.setItem(cursorSizeStorageKey, String(size)); } catch { /* Optional preference storage. */ }
  cursorFeedbackChanged(); return true;
}

export function validateCursorAssets(value) {
  if (value?.schemaVersion !== 1 || !Array.isArray(value.cursors)) throw Error('Unsupported cursor asset catalog');
  const assets = Object.create(null);
  for (const cursor of value.cursors) {
    if (!cursor || typeof cursor.id !== 'string' || !cursorIdPattern.test(cursor.id) || Object.hasOwn(assets, cursor.id))
      throw Error('Cursor assets require unique lowercase cursor IDs');
    const images = Object.create(null);
    for (const size of cursorSizes) {
      const url = cursor.images?.[size];
      if (url !== `./presentation/cursors/generated/${size}/${cursor.id}.png`) throw Error(`Invalid cursor asset URL: ${cursor.id}`);
      images[size] = { url, available: false };
    }
    assets[cursor.id] = { images };
  }
  return assets;
}
export function validateCursorHotspots(value) {
  if (value?.schemaVersion !== 1 || !value.hotspots || typeof value.hotspots !== 'object' || Array.isArray(value.hotspots))
    throw Error('Unsupported cursor hotspots');
  const hotspots = Object.create(null);
  for (const [id, point] of Object.entries(value.hotspots)) {
    if (!cursorIdPattern.test(id) || !Array.isArray(point) || point.length !== 2 ||
        !point.every(n => typeof n === 'number' && Number.isFinite(n) && n >= 0 && n < 1)) throw Error(`Invalid cursor hotspot: ${id}`);
    hotspots[id] = Object.freeze([...point]);
  }
  return Object.freeze(hotspots);
}
export function resolveCursorAsset(id, fallback) {
  const image = cursorAssets[id]?.images[selectedCursorSize];
  if (!image?.available) return fallback;
  const hotspot = cursorHotspots[id] ?? [.5, .5];
  const [x, y] = hotspot.map(n => Math.min(selectedCursorSize - 1, Math.round(n * selectedCursorSize)));
  return `url("${image.url}") ${x} ${y}, ${fallback}`;
}

export function loadCursorAssets(fetcher = globalThis.fetch, createImage = () => new Image()) {
  return cursorAssetLoading ??= (async () => {
    const response = await fetcher('./presentation/cursors/catalog.json');
    if (!response.ok) throw Error(`Cursor asset catalog HTTP ${response.status}`);
    const assets = validateCursorAssets(await response.json());
    cursorAssets = assets;
    // Hotspot overrides are optional and cannot disable otherwise usable assets.
    const hotspots = (async () => {
      try {
        const response = await fetcher('./presentation/cursors/hotspots.json');
        if (response.ok) cursorHotspots = validateCursorHotspots(await response.json());
      } catch { /* Defaults remain centered. */ }
      cursorFeedbackChanged();
    })();
    const loads = Object.values(assets).flatMap(cursor => cursorSizes.map(size => new Promise(resolve => {
      let image;
      try {
        image = createImage();
        image.onload = () => {
          cursor.images[size].available = image.naturalWidth === size && image.naturalHeight === size;
          cursorFeedbackChanged(); resolve();
        };
        image.onerror = () => resolve();
        image.src = cursor.images[size].url;
      } catch { resolve(); }
    })));
    await Promise.all([hotspots, ...loads]);
  })().catch(() => { cursorAssetLoading = null; });
}
