// Presentation only. Keys are content IDs, never display names or legality checks.
// Add optional image: "./art/example.webp" to any entry. CSS keeps the
// fallback beneath artwork, including while loading or when an image is missing.
export const boardVisuals = {
  terrain: {
    default: { fill: "#666a68", detail: "repeating-linear-gradient(0deg, transparent 0 48%, #242d3040 49% 51%, transparent 52% 100%)" },
    StoneFloor: { rotation: true, fill: "#666a68", detail: "repeating-linear-gradient(90deg, transparent 0 48%, #29323855 49% 51%, transparent 52% 100%)",
      variants: [
        { fill: "#666a68", image: "./art/terrain/stone-floor/01.webp" },
        { fill: "#626864", image: "./art/terrain/stone-floor/02.webp" },
        { fill: "#6b6c65", image: "./art/terrain/stone-floor/03.webp" }
      ] },
    Grass: { rotation: true, fill: "#476947", detail: "repeating-linear-gradient(115deg, transparent 0 7px, #abc27825 8px, transparent 10px)",
      variants: [
        { image: "./art/terrain/grass/01.webp" },
        { image: "./art/terrain/grass/02.webp" },
        { image: "./art/terrain/grass/03.webp" }
      ] },
    Tree: { baseTerrain: "Grass", object: { rotation: true,
      variants: [1, 2, 3].map(i => ({ image: `./art/terrain/objects/tree/0${i}.webp` })) }, fill: "#244737", detail: "radial-gradient(ellipse at 40% 40%, #638452 0 18%, #355b37 20% 43%, transparent 46%), radial-gradient(ellipse at 60% 60%, #436b40 0 38%, transparent 42%)" },
    FruitTree: { baseTerrain: "Grass", object: { rotation: true,
      variants: [1, 2, 3].map(i => ({ image: `./art/terrain/objects/fruit-tree/0${i}.webp` })) }, fill: "#305331", detail: "radial-gradient(circle at 35% 35%, #e53b31 0 5%, #355b37 7% 43%, transparent 46%)" },
    Water: { rotation: true, fill: "#266382", detail: "repeating-radial-gradient(ellipse at 15% 0%, transparent 0 8px, #9edce64a 9px 10px, transparent 12px 18px)",
      variants: [
        { image: "./art/terrain/water/01.webp" },
        { image: "./art/terrain/water/02.webp" },
        { image: "./art/terrain/water/03.webp" }
      ] },
    Lava: { rotation: true, fill: "#a64420", detail: "repeating-linear-gradient(32deg, #352b2a 0 5px, transparent 7px 13px, #ffb349 15px 17px, transparent 19px 25px)",
      variants: [
        { image: "./art/terrain/lava/01.webp" },
        { image: "./art/terrain/lava/02.webp" },
        { image: "./art/terrain/lava/03.webp" }
      ] },
    StoneFloorWithTable: { baseTerrain: "StoneFloor", object: { rotation: true,
      variants: [1, 2, 3].map(i => ({ image: `./art/terrain/objects/table/0${i}.webp` })) }, fill: "#636665", detail: "linear-gradient(90deg, transparent 16%, #291d17 17% 20%, #997049 21% 46%, #63462d 47% 50%, #997049 51% 79%, #291d17 80% 83%, transparent 84%)" },
    Asphalt: { rotation: true, fill: "#303133", detail: "radial-gradient(ellipse, #67686a20, transparent)",
      variants: [1, 2, 3].map(i => ({ image: `./art/terrain/asphalt/0${i}.webp` })) },
    Gravel: { rotation: true, fill: "#766d62", detail: "radial-gradient(ellipse, #b0a59360, transparent)",
      variants: [1, 2, 3].map(i => ({ image: `./art/terrain/gravel/0${i}.webp` })) },
    Rail: { baseTerrain: "Gravel", tracks: Object.fromEntries(["straight", "curve", "tee", "cross"].map(shape => [shape, `./art/railway/${shape}.svg`])) }
  },
  unit: {
    default: { detail: "radial-gradient(circle at 35% 25%, #ffffff45, transparent 65%)" },
    "royal-peasant-type": { image: "./art/units/royal-peasant.webp" },
    "goblin-peasant-type": { image: "./art/units/goblin-peasant.webp" },
    "castle-guard-type": { image: "./art/units/castle-guard.webp" },
    "tower-troll-type": { image: "./art/units/tower-troll.webp" },
    "royal-bishop-type": { image: "./art/units/royal-bishop.webp" },
    "goblin-bishop-type": { image: "./art/units/goblin-bishop.webp" },
    "royal-knight-type": { image: "./art/units/royal-knight.webp" },
    "hobby-knight-type": { image: "./art/units/hobby-knight.webp" },
    "barbarian-type": { image: "./art/units/barbarian.webp" },
    "zombie-type": { image: "./art/units/zombie.webp" },
    "worker-type": { image: "./art/units/worker.webp" },
    "chief-mechanic-type": { image: "./art/units/chief-mechanic.webp" },
    "diamond-kart-type": { image: "./art/units/diamond-kart.webp" },
    "geomancer-type": { image: "./art/units/geomancer.webp" },
    "wizard-type": { image: "./art/units/wizard.webp" },
    "rogue-type": { image: "./art/units/rogue.webp" },
    "grunt-type": { image: "./art/units/grunt.webp" },
    "skeleton-archer-type": { image: "./art/units/skeleton-archer.webp" },
    "goblin-type": { image: "./art/units/goblin.webp" },
    "troll-type": { image: "./art/units/troll.webp" },
    "cleric-type": { image: "./art/units/cleric.webp" },
    "shaman-type": { image: "./art/units/shaman.webp" },
    "ghost-type": { image: "./art/units/ghost.webp" },
    "bomb-imp-type": { image: "./art/units/bomb-imp.webp" },
    "displacer-demon-type": { image: "./art/units/displacer-demon.webp" },
    "labyrinth-shaper-type": { image: "./art/units/labyrinth-shaper.webp" },
    "red-dragon-type": { image: "./art/units/red-dragon.webp" },
    "locomotive-type": { image: "./art/units/locomotive.webp" }
  },
  machine: {
    default: { fill: "#566776", detail: "linear-gradient(135deg, #c8bb8d, #3b4955 45%, #182b36)", glyph: "⚙" },
    "diamond-press": { image: "./art/machines/diamond-press.webp", baseTerrain: "StoneFloor" },
    "slot-machine": { image: "./art/machines/slot-machine.webp", baseTerrain: "StoneFloor" }
  },
  cellMarker: {
    default: { fill: "#e5c14d", glyph: "?" },
    "banana-peel": { glyph: "\u{1F34C}", image: "./art/markers/banana-peel.webp" }
  },
  resource: {
    default: { fill: "#e0c795", glyph: "●" },
    diamond: { fill: "#b8ecf4", glyph: "◆", image: "./art/resources/diamond.webp" },
    fruit: { fill: "#e94242", glyph: "\u{1F352}", image: "./art/resources/fruit.webp" },
    banana: { fill: "#f2d558", glyph: "\u{1F34C}", image: "./art/resources/banana.webp" },
    mushroom: { fill: "#df5345", glyph: "\u{1F344}", image: "./art/resources/mushroom.webp" },
    shell: { fill: "#57b367", glyph: "\u{1F41A}", image: "./art/resources/shell.webp" },
    coal: { fill: "#ccc2af", glyph: "●", image: "./art/resources/coal.webp" }
  },
  edge: {
    default: { fill: "#45494b", detail: "repeating-linear-gradient(45deg, transparent 0 8px, #e3dcc53a 9px 10px, transparent 11px 18px)" },
    Wall: { image: "./art/edges/wall.webp" },
    ClosedDoor: { image: "./art/edges/door-closed.webp", fill: "#bd914c", detail: "repeating-linear-gradient(90deg, transparent 0 5px, #392c2855 6px 7px)" },
    OpenDoor: { image: "./art/edges/door-open.webp", fill: "transparent", detail: "none" },
    WallWithWindow: { fill: "#8ad9eb", detail: "linear-gradient(90deg, #253743, #a7e9ed 45% 55%, #253743)" }
  }
};

// FNV-1a over UTF-16LE bytes. Math.imul and >>> keep unsigned 32-bit results
// identical across browsers. Version the input so a future algorithm is explicit.
export function terrainVariantIndex(seed, id, x, y, count) {
  const input = JSON.stringify(["terrain-v1", String(seed), id, x, y]);
  let hash = 2166136261;
  for (let i = 0; i < input.length; i++) {
    const code = input.charCodeAt(i);
    hash = Math.imul(hash ^ (code & 255), 16777619) >>> 0;
    hash = Math.imul(hash ^ (code >>> 8), 16777619) >>> 0;
  }
  return count > 0 ? hash % count : 0;
}

// Independent FNV-1a domain for quarter turns. Never change terrain-v1.
export function terrainRotation(seed, id, x, y, layer = "base") {
  const input = JSON.stringify(["terrain-rotation-v1", String(seed), id, x, y, layer]);
  let hash = 2166136261;
  for (let i = 0; i < input.length; i++) {
    const code = input.charCodeAt(i);
    hash = Math.imul(hash ^ (code & 255), 16777619) >>> 0;
    hash = Math.imul(hash ^ (code >>> 8), 16777619) >>> 0;
  }
  // Mix upper bits into the low bits before choosing one of four turns.
  return ((hash ^ (hash >>> 16)) >>> 0) % 4 * 90;
}

export function terrainLayers(id, context = {}) {
  const entry = Object.hasOwn(boardVisuals.terrain, id) ? boardVisuals.terrain[id] : {};
  const seed = context.seed ?? "legacy", x = context.x ?? 0, y = context.y ?? 0;
  const baseId = entry.baseTerrain ?? id;
  const baseEntry = entry.baseTerrain ? (boardVisuals.terrain[baseId] ?? {}) : entry;
  const variants = baseEntry.variants ?? [];
  const index = terrainVariantIndex(seed, baseId, x, y, variants.length);
  const base = { ...boardVisuals.terrain.default, ...baseEntry, ...variants[index], ...baseEntry.directions?.[context.direction] };
  const objectVariants = entry.object?.variants ?? [];
  const objectIndex = terrainVariantIndex(seed, `${id}:object`, x, y, objectVariants.length);
  return {
    base, index,
    rotation: base.rotation ? terrainRotation(seed, id, x, y, "base") : 0,
    object: entry.object ? { ...entry.object, ...objectVariants[objectIndex] } : null,
    objectIndex,
    objectRotation: entry.object?.rotation ? terrainRotation(seed, id, x, y, "object") : 0,
    objectDetail: entry.detail ?? "none"
  };
}

const railDirections = ["North", "East", "South", "West"];
const railOffsets = [[0, -1], [1, 0], [0, 1], [-1, 0]];
const railShapes = { straight: [0, 2], curve: [0, 1], tee: [0, 1, 2], cross: [0, 1, 2, 3] };

// Presentation only. The Map is rebuilt from each current authoritative board.
export function railConnections(terrain, x, y, direction) {
  const outgoing = railDirections.indexOf(direction);
  if (outgoing < 0) return [];
  const incoming = railOffsets.flatMap(([dx, dy], side) => {
    const neighbor = terrain?.get(`${x + dx},${y + dy}`);
    return neighbor?.kind === "Rail" && neighbor.direction === railDirections[(side + 2) % 4] ? [side] : [];
  });
  if (!incoming.length) incoming.push((outgoing + 2) % 4);
  const connections = [...new Set([outgoing, ...incoming])];
  // Reciprocal arrows can supply an incoming edge identical to the outgoing one.
  if (connections.length === 1) connections.push((outgoing + 2) % 4);
  return connections.sort();
}

export function railGeometry(connections) {
  for (const [shape, sides] of Object.entries(railShapes)) {
    for (let turn = 0; turn < 4; turn++) {
      const rotated = sides.map(side => (side + turn) % 4).sort();
      if (rotated.length === connections.length && rotated.every((side, i) => side === connections[i]))
        return { shape, rotation: turn * 90 };
    }
  }
  return null;
}

export function applyBoardVisual(node, category, id, context = {}) {
  const entries = boardVisuals[category];
  const entry = Object.hasOwn(entries, id) ? entries[id] : {};
  const layers = category === "terrain" ? terrainLayers(id, context) : null;
  // A directional image may override a variant without rotating directional Rail.
  const visual = layers?.base ?? { ...entries.default, ...entry, ...entry.variants?.[0], ...entry.directions?.[context.direction] };
  node.dataset.visualId = id ?? "";
  node.dataset.visualVariant = String(layers?.index ?? 0);
  node.style.setProperty("--visual-image", visual.image ? `url(${JSON.stringify(visual.image)})` : "none");
  node.style.setProperty("--visual-fill", visual.fill ?? "transparent");
  node.style.setProperty("--visual-detail", visual.detail ?? "none");
  if (layers) {
    node.dataset.visualRotation = String(layers.rotation);
    node.style.setProperty("--visual-rotation", `${layers.rotation}deg`);
    applyTerrainObject(node, layers);
    applyRailArtwork(node, id === "Rail" ? context : null);
  }
  if (category === "edge") applyEdgeArtwork(node, visual.image);
  if (category === "machine" || category === "resource" || category === "cellMarker") applyObjectArtwork(node, visual);
  return visual;
}

function applyObjectArtwork(node, visual) {
  // Glyphs remain the Classic presentation and the loading/error fallback.
  let glyph = node.visualGlyph;
  if (!glyph) {
    glyph = document.createElement("span"); glyph.className = "visual-glyph";
    glyph.setAttribute("aria-hidden", "true"); node.visualGlyph = glyph;
  }
  glyph.textContent = visual.glyph ?? ""; node.append(glyph);
  let image = node.objectArtwork;
  if (!visual.image) {
    image?.remove(); node.objectArtwork = null; delete node.dataset.artwork;
    return;
  }
  if (!image) {
    image = document.createElement("img"); image.className = "object-artwork";
    image.alt = ""; image.draggable = false; image.setAttribute("aria-hidden", "true");
    node.objectArtwork = image;
    image.addEventListener("load", () => { if (node.objectArtwork === image) node.dataset.artwork = "ready"; });
    image.addEventListener("error", () => { if (node.objectArtwork === image) node.dataset.artwork = "missing"; });
  }
  node.append(image);
  if (image.dataset.source !== visual.image) {
    node.dataset.artwork = "loading"; image.dataset.source = visual.image; image.src = visual.image;
  }
}

function applyRailArtwork(node, context) {
  if (!context) {
    node.railArtwork?.remove(); node.railArrow?.remove();
    node.railArtwork = node.railArrow = null;
    delete node.dataset.railShape; delete node.dataset.railRotation; delete node.dataset.railArtwork;
    return;
  }
  const geometry = railGeometry(railConnections(context.terrain, context.x ?? 0, context.y ?? 0, context.direction));
  let image = node.railArtwork;
  if (!image) {
    image = document.createElement("img"); image.className = "rail-artwork";
    image.alt = ""; image.draggable = false; image.setAttribute("aria-hidden", "true");
    node.railArtwork = image;
    image.addEventListener("load", () => { if (node.railArtwork === image) node.dataset.railArtwork = "ready"; });
    image.addEventListener("error", () => { if (node.railArtwork === image) node.dataset.railArtwork = "missing"; });
  }
  node.dataset.railShape = geometry?.shape ?? "";
  node.dataset.railRotation = String(geometry?.rotation ?? 0);
  node.style.setProperty("--rail-rotation", `${geometry?.rotation ?? 0}deg`);
  const source = boardVisuals.terrain.Rail.tracks?.[geometry?.shape];
  if (source && image.dataset.source !== source) {
    node.dataset.railArtwork = "loading"; image.dataset.source = source; image.src = source;
  } else if (!source) {
    node.dataset.railArtwork = "missing"; image.removeAttribute("src"); delete image.dataset.source;
  }
  // CSS builds an arrow independently of track artwork and base rotation.
  let arrow = node.railArrow;
  if (!arrow) {
    arrow = document.createElement("span"); arrow.className = "rail-arrow";
    arrow.setAttribute("aria-hidden", "true"); node.railArrow = arrow;
  }
  arrow.dataset.direction = context.direction;
  arrow.style.setProperty("--arrow-rotation", `${railDirections.indexOf(context.direction) * 90}deg`);
  node.append(image); node.append(arrow);
}

function applyEdgeArtwork(node, source) {
  // Keep the original Edge as the hit/feedback target. Only the decorative child
  // rotates; Classic hides it. Fallback materials remain until decoding succeeds.
  let image = node.edgeArtwork;
  if (!source) {
    image?.remove();
    node.edgeArtwork = null;
    delete node.dataset.edgeArtwork;
    return;
  }
  if (!image) {
    image = document.createElement("img");
    image.className = "edge-artwork";
    image.alt = "";
    image.draggable = false;
    node.append(image);
    node.edgeArtwork = image;
    image.addEventListener("load", () => { node.dataset.edgeArtwork = "ready"; });
    image.addEventListener("error", () => { node.dataset.edgeArtwork = "missing"; });
  }
  if (image.dataset.source !== source) {
    node.dataset.edgeArtwork = "loading";
    image.dataset.source = source;
    image.src = source;
  }
}

function applyTerrainObject(node, layers) {
  let image = node.terrainObjectArtwork;
  if (!layers.object) {
    image?.remove();
    node.terrainObjectArtwork = null;
    delete node.dataset.terrainObject;
    delete node.dataset.objectArtwork;
    delete node.dataset.objectVariant;
    delete node.dataset.objectRotation;
    return;
  }
  node.dataset.terrainObject = "true";
  node.dataset.objectVariant = String(layers.objectIndex);
  node.dataset.objectRotation = String(layers.objectRotation);
  node.style.setProperty("--object-rotation", `${layers.objectRotation}deg`);
  node.style.setProperty("--object-detail", layers.objectDetail);
  if (!image) {
    image = document.createElement("img");
    image.className = "terrain-object-artwork";
    image.alt = "";
    image.draggable = false;
    image.setAttribute("aria-hidden", "true");
    node.terrainObjectArtwork = image;
    image.addEventListener("load", () => {
      if (node.terrainObjectArtwork === image) node.dataset.objectArtwork = "ready";
    });
    image.addEventListener("error", () => {
      if (node.terrainObjectArtwork === image) node.dataset.objectArtwork = "missing";
    });
  }
  // renderState clears Cell children even when it retains the Cell itself.
  node.append(image);
  const source = layers.object.image;
  if (!source) {
    node.dataset.objectArtwork = "missing";
    image.removeAttribute("src");
    delete image.dataset.source;
  } else if (image.dataset.source !== source) {
    node.dataset.objectArtwork = "loading";
    image.dataset.source = source;
    image.src = source;
  }
}
