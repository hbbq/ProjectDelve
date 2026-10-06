// Shared by both views: a side keeps its color for the lifetime of the page.
const sideHues = new Map();
const sidePalette = [210, 0, 140, 45, 280, 180, 325, 85];

export function styleSide(node, sideId = "") {
  if (!sideHues.has(sideId)) {
    let hue = sidePalette[sideHues.size];
    if (hue === undefined) {
      // Beyond the palette, split the largest remaining hue gap instead of repeating colors.
      const used = [...sideHues.values()].sort((a, b) => a - b);
      let largestGap = 0;
      for (let i = 0; i < used.length; i++) {
        const gap = (used[i + 1] ?? used[0] + 360) - used[i];
        if (gap > largestGap) { largestGap = gap; hue = (used[i] + gap / 2) % 360; }
      }
    }
    sideHues.set(sideId, hue);
  }
  node.style.setProperty("--side-hue", sideHues.get(sideId));
}
