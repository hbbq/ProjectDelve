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

test("Enhanced names stay on one line with ellipsis inside the token at minimum Cell size", () => {
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
  assert.match(style('#board[data-view="enhanced"]')["min-width"], /60px/);
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
