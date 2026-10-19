// Presentation only. Keys are content IDs, never display names or legality checks.
// Add optional image: "./art/example.webp" to any entry. CSS keeps the
// fallback beneath artwork, including while loading or when an image is missing.
export const boardVisuals = {
  terrain: {
    default: { fill: "#666a68", detail: "repeating-linear-gradient(0deg, transparent 0 48%, #242d3040 49% 51%, transparent 52% 100%)" },
    StoneFloor: { fill: "#666a68", detail: "repeating-linear-gradient(90deg, transparent 0 48%, #29323855 49% 51%, transparent 52% 100%)",
      variants: [
        { fill: "#666a68", image: "./art/terrain/stone-floor/01.webp" },
        { fill: "#626864", image: "./art/terrain/stone-floor/02.webp" },
        { fill: "#6b6c65", image: "./art/terrain/stone-floor/03.webp" }
      ] },
    Grass: { fill: "#476947", detail: "repeating-linear-gradient(115deg, transparent 0 7px, #abc27825 8px, transparent 10px)",
      variants: [
        { image: "./art/terrain/grass/01.webp" },
        { image: "./art/terrain/grass/02.webp" },
        { image: "./art/terrain/grass/03.webp" }
      ] },
    Tree: { fill: "#244737", detail: "radial-gradient(ellipse at 40% 40%, #638452 0 18%, #355b37 20% 43%, transparent 46%), radial-gradient(ellipse at 60% 60%, #436b40 0 38%, transparent 42%)" },
    Water: { fill: "#266382", detail: "repeating-radial-gradient(ellipse at 15% 0%, transparent 0 8px, #9edce64a 9px 10px, transparent 12px 18px)",
      variants: [
        { image: "./art/terrain/water/01.webp" },
        { image: "./art/terrain/water/02.webp" },
        { image: "./art/terrain/water/03.webp" }
      ] },
    Lava: { fill: "#a64420", detail: "repeating-linear-gradient(32deg, #352b2a 0 5px, transparent 7px 13px, #ffb349 15px 17px, transparent 19px 25px)",
      variants: [
        { image: "./art/terrain/lava/01.webp" },
        { image: "./art/terrain/lava/02.webp" },
        { image: "./art/terrain/lava/03.webp" }
      ] },
    StoneFloorWithTable: { fill: "#636665", detail: "linear-gradient(90deg, transparent 16%, #291d17 17% 20%, #997049 21% 46%, #63462d 47% 50%, #997049 51% 79%, #291d17 80% 83%, transparent 84%)" },
    Rail: { fill: "#766d5d", detail: "repeating-linear-gradient(0deg, transparent 0 7px, #342e29 8px 12px, transparent 13px 18px)" }
  },
  unit: {
    default: { detail: "radial-gradient(circle at 35% 25%, #ffffff45, transparent 65%)" },
    "barbarian-type": { image: "./art/units/barbarian.webp" },
    "zombie-type": { image: "./art/units/zombie.webp" },
    "worker-type": { image: "./art/units/worker.webp" },
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
  machine: { default: { fill: "#566776", detail: "linear-gradient(135deg, #c8bb8d, #3b4955 45%, #182b36)", glyph: "⚙" } },
  resource: {
    default: { fill: "#e0c795", glyph: "●" },
    diamond: { fill: "#b8ecf4", glyph: "◆" },
    coal: { fill: "#ccc2af", glyph: "●" }
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

export function applyBoardVisual(node, category, id, context = {}) {
  const entries = boardVisuals[category];
  const entry = Object.hasOwn(entries, id) ? entries[id] : {};
  const variants = entry.variants ?? [];
  const index = category === "terrain" && variants.length
    ? terrainVariantIndex(context.seed ?? "legacy", id, context.x ?? 0, context.y ?? 0, variants.length) : 0;
  // A directional image may override a variant without rotating directional Rail.
  const visual = { ...entries.default, ...entry, ...variants[index], ...entry.directions?.[context.direction] };
  node.dataset.visualId = id ?? "";
  node.dataset.visualVariant = String(index);
  node.style.setProperty("--visual-image", visual.image ? `url(${JSON.stringify(visual.image)})` : "none");
  node.style.setProperty("--visual-fill", visual.fill ?? "transparent");
  node.style.setProperty("--visual-detail", visual.detail ?? "none");
  if (category === "edge") applyEdgeArtwork(node, visual.image);
  return visual;
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
