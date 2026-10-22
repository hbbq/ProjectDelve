const { test } = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const css = fs.readFileSync(path.join(__dirname, "../ProjectDelve.Web/wwwroot/styles.css"), "utf8");
const tokens = Object.fromEntries([...css.matchAll(/--ui-([\w-]+):\s*(#[\da-f]+);/g)].map(([, name, color]) => [name, color]));
function luminance(hex) {
  const rgb = hex.slice(1).match(/../g).map(value => parseInt(value, 16) / 255)
    .map(value => value <= .04045 ? value / 12.92 : ((value + .055) / 1.055) ** 2.4);
  return rgb[0] * .2126 + rgb[1] * .7152 + rgb[2] * .0722;
}
function contrast(a, b) {
  const values = [luminance(tokens[a]), luminance(tokens[b])].sort((a, b) => b - a);
  return (values[0] + .05) / (values[1] + .05);
}

test("shared neutral surfaces retain readable primary, secondary and accent text", () => {
  for (const surface of ["page", "surface", "surface-raised", "control", "hover"]) {
    for (const text of ["text", "text-muted", "accent"]) {
      assert.ok(contrast(text, surface) >= 4.5, `${text} on ${surface} needs 4.5:1`);
    }
  }
  assert.ok(contrast("on-accent", "accent") >= 4.5);
  assert.ok(contrast("on-accent", "accent-hover") >= 4.5);
  assert.ok(contrast("disabled-text", "disabled-bg") >= 4.5);
});

test("controls retain contrasting focus, selected and disabled treatments", () => {
  for (const surface of ["page", "surface", "control", "hover"]) {
    assert.ok(contrast("focus", surface) >= 3);
  }
  assert.ok(contrast("control-border", "control") >= 3);
  assert.match(css, /button:disabled\s*\{[^}]*background: var\(--ui-disabled-bg\)[^}]*color: var\(--ui-disabled-text\)/);
  assert.match(css, /button:focus-visible[^{}]*\{[^}]*outline: 3px solid var\(--ui-focus\)/);
  assert.match(css, /\.mode-switch \[aria-pressed="true"\][^{]*\{[^}]*outline: 2px solid var\(--ui-accent\)/);
});

test("cards and shared panels use overridable surfaces without Side-tinted fills", () => {
  for (const selector of ["#unit-card", ".play-status", ".dice-tray", ".world-card", "#commentary-panel", "#portrait-viewer"]) {
    assert.ok([...css.matchAll(/([^{}]+)\{([^{}]*)\}/g)]
      .some(([, selectors, body]) => selectors.trim() === selector && body.includes("background: var(--ui-surface)")), selector);
  }
  assert.match(css, /#unit-card\s*\{[^}]*border-color: var\(--unit-side-border, var\(--ui-border\)\)/);
  assert.match(css, /\.decision-panel\s*\{[^}]*background: var\(--ui-surface-overlay\)/);
  assert.match(css, /\.shared-tooltip\s*\{[^}]*background: var\(--ui-surface-raised\)/);
});
