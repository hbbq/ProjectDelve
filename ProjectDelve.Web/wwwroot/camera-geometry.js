// Presentation geometry in Cell coordinates. No legality or pathfinding lives here.
export const cameraBaseCell = 60;
export const cameraDefaults = Object.freeze({ mode: "auto", fixedSpan: 15, minSpan: 7, maxSpan: 15, margin: 2, follow: "own" });
const cameraClamp = (n, lo, hi) => Math.max(lo, Math.min(hi, n));

export function cameraPreferences(value = {}) {
  if (!value || typeof value !== "object") value = {};
  const number = (key, lo, hi) => cameraClamp(Number.isFinite(Number(value[key])) ? Number(value[key]) : cameraDefaults[key], lo, hi);
  const maxSpan = Math.round(number("maxSpan", 7, 25));
  return { mode: ["full", "fixed", "auto"].includes(value.mode) ? value.mode : cameraDefaults.mode,
    fixedSpan: number("fixedSpan", 5, 50), minSpan: Math.min(maxSpan, Math.round(number("minSpan", 5, 15))),
    maxSpan, margin: Math.round(number("margin", 0, 5)),
    follow: ["off", "own", "all"].includes(value.follow) ? value.follow : cameraDefaults.follow };
}

export function cameraBounds(cells) {
  if (!cells?.length) return null;
  const xs = cells.map(c => c.x), ys = cells.map(c => c.y);
  return { x: Math.min(...xs), y: Math.min(...ys), w: Math.max(...xs) - Math.min(...xs) + 1, h: Math.max(...ys) - Math.min(...ys) + 1 };
}
export function cameraUnion(a, b) {
  if (!a) return b; if (!b) return a;
  const x = Math.min(a.x, b.x), y = Math.min(a.y, b.y);
  return { x, y, w: Math.max(a.x + a.w, b.x + b.w) - x, h: Math.max(a.y + a.h, b.y + b.h) - y };
}
export function cameraMargin(rect, margin, board) {
  const x = Math.max(0, rect.x - margin), y = Math.max(0, rect.y - margin);
  return { x, y, w: Math.min(board.width, rect.x + rect.w + margin) - x, h: Math.min(board.height, rect.y + rect.h + margin) - y };
}
export function cameraSpanScale(viewport, span) { return Math.max(viewport.width, viewport.height) / span; }
export function cameraRequiredSpan(rect, viewport) {
  return Math.max(rect.w / viewport.width, rect.h / viewport.height) * Math.max(viewport.width, viewport.height);
}
export function cameraClampPose(pose, board, viewport) {
  const axis = (center, extent, pixels) => {
    const half = pixels / pose.scale / 2;
    return half * 2 >= extent ? extent / 2 : cameraClamp(center, half, extent - half);
  };
  return { ...pose, x: axis(pose.x, board.width, viewport.width), y: axis(pose.y, board.height, viewport.height) };
}
export function cameraFull(board, viewport) {
  return { x: board.width / 2, y: board.height / 2, scale: Math.min(viewport.width / board.width, viewport.height / board.height) };
}
export function cameraProject(point, pose, viewport) {
  return { x: viewport.width / 2 + (point.x - pose.x) * pose.scale, y: viewport.height / 2 + (point.y - pose.y) * pose.scale };
}
export function cameraUnproject(point, pose, viewport) {
  return { x: pose.x + (point.x - viewport.width / 2) / pose.scale, y: pose.y + (point.y - viewport.height / 2) / pose.scale };
}
export function cameraMatrix(pose, viewport) {
  return { a: pose.scale / cameraBaseCell, d: pose.scale / cameraBaseCell,
    e: viewport.width / 2 - pose.x * pose.scale, f: viewport.height / 2 - pose.y * pose.scale };
}
export function cameraPoseFromMatrix(matrix, viewport) {
  const scale = matrix.a * cameraBaseCell;
  return { scale, x: (viewport.width / 2 - matrix.e) / scale, y: (viewport.height / 2 - matrix.f) / scale };
}

export function cameraAutoFit({ active, moves = [], friends = [], board, viewport, preferences }) {
  const prefs = cameraPreferences(preferences), maxSpan = Math.max(prefs.minSpan, prefs.maxSpan);
  const activeBounds = cameraBounds(active); if (!activeBounds) return null;
  const core = moves.reduce((bounds, cells) => cameraUnion(bounds, cameraBounds(cells)), activeBounds);
  const fits = bounds => cameraRequiredSpan(bounds, viewport) <= maxSpan + .000001;
  let bounds = core, overflow = !fits(core);
  // Margins are subordinate to the complete movement region.
  for (let margin = prefs.margin; margin >= 0; margin--) {
    const expanded = cameraMargin(core, margin, board);
    if (fits(expanded)) { bounds = expanded; break; }
  }
  const distance = b => Math.hypot(Math.max(core.x - b.x - b.w, b.x - core.x - core.w, 0), Math.max(core.y - b.y - b.h, b.y - core.y - core.h, 0));
  const nearby = friends.map(cameraBounds).filter(Boolean).sort((a, b) => distance(a) - distance(b) || a.y - b.y || a.x - b.x);
  if (!overflow) {
    let group = core;
    for (const friend of nearby) { const combined = cameraUnion(bounds, friend); if (fits(combined)) { bounds = combined; group = cameraUnion(group, friend); } }
    // Add whatever group margin remains possible, without losing a footprint.
    for (let margin = prefs.margin; margin > 0; margin--) {
      const expanded = cameraUnion(bounds, cameraMargin(group, margin, board));
      if (fits(expanded)) { bounds = expanded; break; }
    }
  }
  const span = cameraClamp(Math.ceil(cameraRequiredSpan(bounds, viewport) - .000001), prefs.minSpan, maxSpan);
  let pose = { x: bounds.x + bounds.w / 2, y: bounds.y + bounds.h / 2, scale: cameraSpanScale(viewport, span) };
  // Oversized movement regions never push the active footprint out of view.
  const halfW = viewport.width / pose.scale / 2, halfH = viewport.height / pose.scale / 2;
  pose.x = cameraClamp(pose.x, activeBounds.x + activeBounds.w - halfW, activeBounds.x + halfW);
  pose.y = cameraClamp(pose.y, activeBounds.y + activeBounds.h - halfH, activeBounds.y + halfH);
  return { pose: cameraClampPose(pose, board, viewport), span, overflow, bounds, core };
}

export function cameraEnsureVisible(pose, bounds, board, viewport, preferred = bounds) {
  if (!bounds) return pose;
  const comfortableW = viewport.width / pose.scale * .76, comfortableH = viewport.height / pose.scale * .76;
  const target = bounds.w <= viewport.width / pose.scale && bounds.h <= viewport.height / pose.scale ? bounds : preferred;
  const axis = (center, start, length, comfortable, visible) => {
    const size = length <= comfortable ? comfortable : visible;
    if (length > size) return start + length / 2;
    return cameraClamp(center, start + length - size / 2, start + size / 2);
  };
  return cameraClampPose({ ...pose,
    x: axis(pose.x, target.x, target.w, comfortableW, viewport.width / pose.scale),
    y: axis(pose.y, target.y, target.h, comfortableH, viewport.height / pose.scale) }, board, viewport);
}

export function cameraObjectiveIndicator(area, pose, viewport, inset = 25) {
  const a = cameraProject(area, pose, viewport), b = cameraProject({ x: area.x + area.w, y: area.y + area.h }, pose, viewport);
  if (a.x >= -.1 && a.y >= -.1 && b.x <= viewport.width + .1 && b.y <= viewport.height + .1) return null;
  const cx = viewport.width / 2, cy = viewport.height / 2;
  // Choose an outside edge even when the Area surrounds the viewport's center.
  const options = [];
  if (a.x < 0) options.push({ x: a.x, y: cameraClamp(cy, a.y, b.y) });
  if (b.x > viewport.width) options.push({ x: b.x, y: cameraClamp(cy, a.y, b.y) });
  if (a.y < 0) options.push({ x: cameraClamp(cx, a.x, b.x), y: a.y });
  if (b.y > viewport.height) options.push({ x: cameraClamp(cx, a.x, b.x), y: b.y });
  options.sort((p, q) => Math.hypot(p.x - cx, p.y - cy) - Math.hypot(q.x - cx, q.y - cy));
  const dx = options[0].x - cx, dy = options[0].y - cy;
  const t = Math.min(dx ? (cx - inset) / Math.abs(dx) : Infinity, dy ? (cy - inset) / Math.abs(dy) : Infinity);
  return { x: cameraClamp(cx + dx * t, inset, viewport.width - inset), y: cameraClamp(cy + dy * t, inset, viewport.height - inset), angle: Math.atan2(dy, dx), targetX: options[0].x, targetY: options[0].y };
}

// Pack buttons along the viewport perimeter, trying the nearest slots first.
export function cameraIndicatorSlot(location, placed, viewport, blocked = [], inset = 26) {
  const w = viewport.width - inset * 2, h = viewport.height - inset * 2, perimeter = 2 * (w + h);
  const x = location.x - inset, y = location.y - inset;
  let start = y <= 1 ? x : x >= w - 1 ? w + y : y >= h - 1 ? w + h + w - x : 2 * w + h + h - y;
  const pointAt = distance => {
    let d = ((distance % perimeter) + perimeter) % perimeter;
    if (d <= w) return { x: inset + d, y: inset };
    d -= w; if (d <= h) return { x: viewport.width - inset, y: inset + d };
    d -= h; if (d <= w) return { x: viewport.width - inset - d, y: viewport.height - inset };
    return { x: inset, y: viewport.height - inset - (d - w) };
  };
  for (let step = 0; step < Math.ceil(perimeter / 44); step++) for (const direction of step ? [1,-1] : [1]) {
    const point = pointAt(start + step * 46 * direction);
    if (placed.some(p => Math.hypot(p.x - point.x, p.y - point.y) < 45)) continue;
    if (blocked.some(r => point.x > r.x - 24 && point.x < r.x + r.w + 24 && point.y > r.y - 24 && point.y < r.y + r.h + 24)) continue;
    return { ...location, ...point, angle: Math.atan2(location.targetY - point.y, location.targetX - point.x) };
  }
  return location;
}
