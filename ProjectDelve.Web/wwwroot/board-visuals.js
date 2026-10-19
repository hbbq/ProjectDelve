// Presentation only. Keys are content IDs, never display names or legality checks.
// Add optional image: "./assets/board/example.webp" to any entry. CSS keeps the
// fallback beneath artwork, including while loading or when an image is missing.
export const boardVisuals = {
  terrain: {
    default: { fill: "#666a68", detail: "repeating-linear-gradient(0deg, transparent 0 48%, #242d3040 49% 51%, transparent 52% 100%)" },
    StoneFloor: { fill: "#666a68", detail: "repeating-linear-gradient(90deg, transparent 0 48%, #29323855 49% 51%, transparent 52% 100%)" },
    Grass: { fill: "#476947", detail: "repeating-linear-gradient(115deg, transparent 0 7px, #abc27825 8px, transparent 10px)" },
    Tree: { fill: "#244737", detail: "radial-gradient(ellipse at 40% 40%, #638452 0 18%, #355b37 20% 43%, transparent 46%), radial-gradient(ellipse at 60% 60%, #436b40 0 38%, transparent 42%)" },
    Water: { fill: "#266382", detail: "repeating-radial-gradient(ellipse at 15% 0%, transparent 0 8px, #9edce64a 9px 10px, transparent 12px 18px)" },
    Lava: { fill: "#a64420", detail: "repeating-linear-gradient(32deg, #352b2a 0 5px, transparent 7px 13px, #ffb349 15px 17px, transparent 19px 25px)" },
    StoneFloorWithTable: { fill: "#636665", detail: "linear-gradient(90deg, transparent 16%, #291d17 17% 20%, #997049 21% 46%, #63462d 47% 50%, #997049 51% 79%, #291d17 80% 83%, transparent 84%)" },
    Rail: { fill: "#766d5d", detail: "repeating-linear-gradient(0deg, transparent 0 7px, #342e29 8px 12px, transparent 13px 18px)" }
  },
  unit: { default: { detail: "radial-gradient(circle at 35% 25%, #ffffff45, transparent 65%)" } },
  machine: { default: { fill: "#566776", detail: "linear-gradient(135deg, #c8bb8d, #3b4955 45%, #182b36)", glyph: "⚙" } },
  resource: {
    default: { fill: "#e0c795", glyph: "●" },
    diamond: { fill: "#b8ecf4", glyph: "◆" },
    coal: { fill: "#ccc2af", glyph: "●" }
  },
  edge: {
    default: { fill: "#45494b", detail: "repeating-linear-gradient(45deg, transparent 0 8px, #e3dcc53a 9px 10px, transparent 11px 18px)" },
    ClosedDoor: { fill: "#bd914c", detail: "repeating-linear-gradient(90deg, transparent 0 5px, #392c2855 6px 7px)" },
    OpenDoor: { fill: "transparent", detail: "none" },
    WallWithWindow: { fill: "#8ad9eb", detail: "linear-gradient(90deg, #253743, #a7e9ed 45% 55%, #253743)" }
  }
};

export function applyBoardVisual(node, category, id) {
  const entries = boardVisuals[category];
  const visual = { ...entries.default, ...entries[id] };
  node.dataset.visualId = id ?? "";
  node.style.setProperty("--visual-image", visual.image ? `url(${JSON.stringify(visual.image)})` : "none");
  node.style.setProperty("--visual-fill", visual.fill ?? "transparent");
  node.style.setProperty("--visual-detail", visual.detail ?? "none");
  return visual;
}
