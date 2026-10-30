const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

// The DOM harness does not run a layout engine. Check the CSS layout contract
// here; full names, tooltips, HP conditions and interactions are tested in door-choice.
const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/board-enhanced.css"), "utf8")
  .replace(/\/\*[\s\S]*?\*\//g, "");
const rules = [...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)].map(([, selectors, body]) => ({
  selectors: selectors.split(",").map(s => s.trim()),
  declarations: Object.fromEntries(body.split(";").filter(s => s.trim()).map(s => {
    const colon = s.indexOf(":"); return [s.slice(0, colon).trim(), s.slice(colon + 1).trim()];
  }))
}));
const enhanced = selector => `#board[data-view="enhanced"] ${selector}`;
const style = selector => Object.assign({}, ...rules.filter(r => r.selectors.includes(selector)).map(r => r.declarations));

test("fixed component artwork and fallbacks share visible isolated layers in gameplay and generator previews", () => {
  for (const root of ["#board", "#generator-board"]) {
    assert.equal(style(`${root} .cell[data-fixed-components]`).isolation, "isolate");
    const layer = style(`${root} .fixed-component-layer`);
    assert.equal(layer.position, "absolute");
    assert.equal(layer.display, "grid");
    assert.equal(layer["z-index"], "-1");
    assert.equal(layer["pointer-events"], "none");
    assert.equal(layer["background-image"], "var(--component-detail)");
    assert.equal(style(`${root} .fixed-component-artwork`).display, "none");
    assert.equal(style(`${root} .fixed-component-fallback`)["font-size"], "1.8em");
    const ready = `${root}[data-view="enhanced"] .fixed-component-layer[data-artwork="ready"]`;
    assert.equal(style(`${ready} .fixed-component-artwork`).display, "block");
    assert.equal(style(`${ready} .fixed-component-artwork`)["object-fit"], "contain");
    assert.equal(style(`${ready} .fixed-component-fallback`).display, "none");
  }
});

test("object artwork uses 24px markers and full-Cell Machines while Classic and failed loads retain glyphs", () => {
  assert.equal(style("#board .object-artwork").display, "none");
  assert.equal(style("#board .object-artwork")["pointer-events"], "none");
  const image = style(enhanced('[data-artwork="ready"] > .object-artwork'));
  assert.equal(image.display, "block"); assert.equal(image["object-fit"], "contain");
  assert.equal(image.width, "100%"); assert.equal(image.height, "100%");
  assert.equal(style(enhanced('[data-artwork="ready"] > .visual-glyph')).visibility, "hidden");
  const marker = style(enhanced('.resource-marker:has(.object-artwork)'));
  assert.equal(marker.width, "24px"); assert.equal(marker.height, "24px");
  assert.equal(style(enhanced('.resource-marker')).top, "2%");
  assert.equal(style(enhanced('.resource-marker')).right, "2%");
  assert.equal(style(enhanced('.machine-cell:has(.object-artwork) .machine-art')).inset, "0");
  assert.equal(style(enhanced('.machine-cell:has(.object-artwork) .machine-labels'))["z-index"], "1");
  assert.equal(style("#board .machine-labels").display, "contents");
  assert.equal(style("#board .machine-labels")["font-size"], "inherit");
  assert.equal(style("#board .visual-glyph")["font-size"], "inherit");
});

test("Rail layers hide in Classic, join across borders, and keep arrows above track but below feedback", () => {
  for (const child of [".rail-artwork", ".rail-arrow"]) {
    assert.equal(style(`#board ${child}`).display, "none");
    assert.equal(style(`#board ${child}`)["pointer-events"], "none");
  }
  assert.equal(style(enhanced(".cell.Rail"))["border-width"], "0");
  const track = style(enhanced('.cell[data-rail-artwork="ready"] .rail-artwork'));
  assert.equal(track.width, "100%"); assert.equal(track.height, "100%");
  assert.equal(track.transform, "rotate(var(--rail-rotation))"); assert.equal(track["z-index"], "-2");
  const arrow = style(enhanced(".rail-arrow"));
  assert.equal(arrow["z-index"], "-1"); assert.equal(arrow.transform, "rotate(var(--arrow-rotation))");
  assert.equal(style(enhanced(".cell.Rail::before"))["z-index"], "-3");
  assert.match(style(enhanced(".cell.Rail::after"))["background-image"], /rail-fallback/);
});

test("terrain decoration fills square Cells, rotates independently and stays below feedback/content in Enhanced only", () => {
  assert.equal(style("#board .terrain-object-artwork").display, "none");
  assert.equal(style("#board .terrain-object-artwork")["pointer-events"], "none");
  assert.equal(style(enhanced(".cell")).isolation, "isolate");
  assert.equal(style(enhanced(".cell")).transform, undefined);
  const base = style(enhanced(".cell::before"));
  assert.equal(base.inset, "0");
  assert.equal(base["background-size"], "cover");
  assert.equal(base.transform, "rotate(var(--visual-rotation, 0deg))");
  assert.equal(base["pointer-events"], "none");
  assert.equal(base["z-index"], "-2");
  const object = style(enhanced('.cell[data-object-artwork="ready"] .terrain-object-artwork'));
  assert.equal(object.width, "100%"); assert.equal(object.height, "100%");
  assert.equal(object["object-fit"], "contain"); assert.equal(object["object-position"], "center");
  assert.equal(object.transform, "rotate(var(--object-rotation, 0deg))");
  assert.equal(object["z-index"], "-1");
  assert.equal(style(enhanced('.cell[data-object-artwork="ready"]::after')).display, "none");
  assert.equal(style(enhanced('.cell[data-terrain-object]::after'))["background-image"], "var(--object-detail)");
  assert.ok(style(enhanced(".cell.legal")).outline);
  assert.ok(style(enhanced(".cell.placement-preview")).outline);
});

test("Edge art rotates independently of geometry, hides in Classic, and clears the fallback only after loading", () => {
  assert.equal(style("#board .edge-artwork").display, "none");
  assert.equal(style("#board .edge-artwork")["pointer-events"], "none");
  const ready = style(enhanced('.edge[data-edge-artwork="ready"]'));
  assert.equal(ready.background, "transparent");
  assert.equal(ready.border, "0");
  const image = style(enhanced('.edge[data-edge-artwork="ready"] .edge-artwork'));
  assert.equal(image.display, "block");
  assert.equal(image.width, "var(--cell-size)");
  assert.equal(image.height, "auto");
  assert.equal(image.left, "50%");
  assert.equal(image.top, "50%");
  assert.equal(image.transform, "translate(-50%, -50%)");
  assert.equal(style(enhanced('.edge[data-orientation="Vertical"] .edge-artwork')).transform, "translate(-50%, -50%) rotate(90deg)");
  for (const rule of rules.filter(r => r.selectors.some(s => s.includes(".edge")))) {
    assert.ok(rule.selectors.every(s => s.startsWith('#board[data-view="enhanced"]') || s === "#board .edge-artwork"));
  }
  // Keep missing-image materials and the existing legal/hover/playback feedback.
  assert.equal(style(enhanced(".edge"))["background-image"], "var(--visual-detail)");
  for (const selector of ['.edge.board-choice[aria-disabled="false"]', ".edge.target", ".edge.door-focus"]) {
    assert.ok(style(enhanced(selector))["box-shadow"]);
  }
});

test("Enhanced structural bases join at Cell boundaries and rotate with the painted layer", () => {
  for (const kind of ["Wall", "ClosedDoor", "OpenDoor"]) {
    const base = style(enhanced(`.edge.${kind}::before`));
    assert.equal(base.content, '""');
    assert.equal(base["pointer-events"], "none");
    assert.equal(base.width, "calc(var(--cell-size) + 1px)");
    assert.equal(base.height, "clamp(8px, calc(var(--cell-size) * .14), 14px)");
    assert.equal(base.left, "50%");
    assert.equal(base.top, "50%");
    assert.equal(base.transform, "translate(-50%, -50%)");
    assert.equal(base["z-index"], "0");
  }
  assert.equal(style(enhanced('.edge[data-orientation="Vertical"]::before')).transform,
    style(enhanced('.edge[data-orientation="Vertical"] .edge-artwork')).transform);
  assert.equal(style(enhanced('.edge[data-edge-artwork="ready"] .edge-artwork'))["z-index"], "1");
  assert.ok(style(enhanced(".edge.Wall::before")).background);
  assert.match(style(enhanced(".edge.Wall .edge-artwork")).filter, /brightness\(\.62\)/);
  assert.notEqual(style(enhanced(".edge.ClosedDoor::before")).background, style(enhanced(".edge.Wall::before")).background);
  // Use ::before only; ::after remains the existing enlarged interaction target.
  assert.ok(!rules.some(r => r.selectors.some(s => s.includes(".edge") && s.endsWith("::after"))));
});

test("Open Door keeps a clear central passage in loading, missing and ready states", () => {
  const supports = style(enhanced(".edge.OpenDoor::before"));
  assert.equal(supports.background, "linear-gradient(90deg, #493321 0 27%, transparent 27% 73%, #493321 73% 100%)");
  assert.equal(supports["box-shadow"], undefined);
  assert.equal(supports.border, undefined);
  assert.equal(style(enhanced(".edge.OpenDoor"))["box-shadow"], "none");
  assert.equal(style(enhanced('.edge:where(.OpenDoor[data-edge-artwork="ready"])')).outline, "none");
  // Do not remove the dashed missing-image fallback or shared hover/focus feedback.
  assert.equal(style(enhanced(".edge.OpenDoor")).outline, undefined);
  assert.ok(style(enhanced('.board-choice[aria-disabled="false"]:hover')).outline);
  assert.ok(style(enhanced(".cell.legal")).outline);
});

test("Enhanced names stay on one line inside the token while the shared camera owns Cell scale", () => {
  const name = style(enhanced(".token-nameplate"));
  assert.equal(name.display, "block");
  assert.equal(name["white-space"], "nowrap");
  assert.equal(name.overflow, "hidden");
  assert.equal(name["text-overflow"], "ellipsis");
  assert.equal(name["box-sizing"], "border-box");
  assert.equal(name.left, "0");
  assert.equal(name.width, "100%");
  assert.equal(name["-webkit-line-clamp"], undefined);
  assert.match(name["font-size"], /^\d+px$/); // Same size on small and large tokens.
  assert.ok(parseFloat(name["font-size"]) <= 10);
  assert.ok(parseFloat(name["line-height"]) <= 12);
  assert.ok(parseFloat(name.bottom) <= 0); // Anchor to the lower rim, away from the face.
  assert.equal(style('#board[data-view="enhanced"]')["min-width"], undefined); // Both renderers must obey the shared camera.
});

test("Enhanced HP is compact at the top and Side text stays hidden while the ring remains", () => {
  const hp = style(enhanced(".figure-hp"));
  assert.ok(parseFloat(hp.top) < 0);
  assert.equal(hp.bottom, "auto");
  assert.equal(hp.width, "max-content");
  assert.equal(hp["min-width"], "0");
  assert.equal(hp["white-space"], "nowrap");
  assert.ok(parseFloat(hp["font-size"]) <= 10);
  assert.ok(parseFloat(hp["line-height"]) <= 11);
  assert.equal(style(enhanced(".token-side")).display, "none");
  assert.match(style(enhanced(".figure"))["border-color"], /var\(--side-hue/);
});

test("compact labels are scoped to Enhanced and keep portrait, posture and feedback layers", () => {
  for (const selector of [".token-nameplate", ".figure-hp", ".token-side"]) {
    const matching = rules.filter(r => r.selectors.some(s => s.endsWith(` ${selector}`)));
    for (const rule of matching) {
      if (rule.declarations.display === "none") continue; // Shared Classic opt-out.
      assert.ok(rule.selectors.every(s => s.startsWith('#board[data-view="enhanced"]')));
    }
  }
  assert.equal(style("#board .token-nameplate").display, "none");
  assert.equal(style("#board .token-side").display, "none");
  assert.equal(style(enhanced(".token-portrait"))["border-radius"], "50%");
  assert.equal(style(enhanced(".token-portrait img"))["object-fit"], "cover");
  assert.match(style(enhanced(".figure.lying .token-portrait")).transform, /rotate/);
  assert.equal(style(enhanced(".figure.lying"))["border-style"], "dashed");
  assert.equal(style(enhanced(".figure.lying .figure-hp")).transform, "translateX(-50%)");
  assert.equal(style(enhanced(".figure.active .token-active")).display, "block");
  assert.ok(style(enhanced(".figure.token-selected")).outline);
  assert.ok(style(enhanced(".figure:hover")).outline);
});

test("Cell Marker layers remain below figures, above terrain and leave Cell feedback interactive", () => {
  const base = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
  const marker = base.match(/\.cell \.cell-marker\s*\{([^}]+)\}/)[1];
  assert.match(marker, /pointer-events:\s*none/);
  assert.match(marker, /z-index:\s*0/);
  assert.match(marker, /width:\s*62%/);
  assert.equal(style(enhanced(".cell::before"))["z-index"], "-2");
  assert.equal(style("#board .object-artwork").display, "none");
  assert.equal(style(enhanced('[data-artwork="ready"] > .visual-glyph')).visibility, "hidden");
});

test("Waypoint rectangles ignore pointer input, stay below figures, and support visibility toggle", () => {
  const base = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
  const outline = base.match(/\.waypoint-area\s*\{([^}]+)\}/)[1];
  assert.match(outline, /pointer-events:\s*none/);
  assert.match(outline, /z-index:\s*1/);
  assert.match(base, /#board\.hide-waypoints \.waypoint-area\s*\{\s*display:\s*none/);
});
