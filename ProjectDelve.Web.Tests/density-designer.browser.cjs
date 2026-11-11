// Supplemental keyboard verification: the protected data-only dispatcher has no keypress action.
// Run only against the isolated QA host, with the installed Playwright module path as argv[2].
const assert = require('node:assert/strict');
const { chromium } = require(process.argv[2] || 'playwright');

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, serviceWorkers: 'block' });
    await context.route('**/*', route => {
      const url = new URL(route.request().url());
      return url.origin === 'http://127.0.0.1:5081' ? route.continue() : route.abort();
    });
    const page = await context.newPage(), errors = [];
    let generations = 0;
    page.on('pageerror', error => errors.push(error.message));
    page.on('request', request => { if (request.method() === 'POST' && request.url().endsWith('/api/world-generation')) generations++; });
    await page.goto('http://127.0.0.1:5081/');
    await page.locator('#open-world-generator').click();
    const section = page.locator('#world-generator-settings-world'), summary = section.locator('summary');
    await summary.focus(); await page.keyboard.press('Enter');
    assert.equal(await section.evaluate(node => node.open), true);
    await page.keyboard.press('Tab');
    assert.equal(await page.evaluate(() => document.activeElement.id), 'world-generator-preset');
    await page.locator('#world-generator-width').fill('31');
    await summary.focus(); await page.keyboard.press('Space');
    assert.equal(await section.evaluate(node => node.open), false);
    assert.match(await summary.textContent(), /31×24/);
    await page.keyboard.press('Enter');
    assert.equal(await page.locator('#world-generator-width').inputValue(), '31');
    assert.equal(generations, 0);

    await page.locator('#world-generator-recipe > summary').click();
    await page.locator('#world-generator-code-input').fill('WLD1-30x24-X0-Y0-F10-W4-R5-KBroad-HLakes-E1-I2-B1111-C1-NLow-VLow-P2-Q1-TLow-JMinorPath-L1-ALow-U0-Z2-MOff-D3-OEmpty-S000000000000002A');
    await page.locator('#world-generator-load').click();
    await page.locator('#world-generator-preview').click();
    await page.waitForFunction(() => document.querySelector('#world-generator-status').textContent.startsWith('World preview ready'));
    assert.equal(generations, 1);
    const previewBounds = await page.locator('#world-generator-board').boundingBox();
    assert.ok(previewBounds.y < 650, 'The map must be visible without scrolling past diagnostics.');
    await page.screenshot({ path: '.playwright-cli/density-ready-desktop.png' });
    await page.setViewportSize({ width: 430, height: 900 });
    await page.locator('#world-generator').evaluate(node => { node.scrollTop = 0; });
    const narrowBounds = await page.locator('#world-generator-board').boundingBox();
    assert.ok(narrowBounds.width > 250 && narrowBounds.y < 650);
    await page.screenshot({ path: '.playwright-cli/density-ready-narrow.png' });
    await page.setViewportSize({ width: 1440, height: 1000 });
    const cell = await page.locator('#world-generator-board .cell').first().elementHandle();
    await summary.focus(); await page.keyboard.press('Space'); await page.keyboard.press('Space');
    assert.equal(await cell.evaluate(node => node === document.querySelector('#world-generator-board .cell')), true);
    assert.equal(generations, 1);
    assert.equal(await page.locator('#world-generator-warning-panel').isVisible(), true);
    const diagnostics = page.locator('#world-generator-diagnostic-panel');
    assert.equal(await diagnostics.evaluate(node => node.open), false);
    await diagnostics.locator(':scope > summary').focus(); await page.keyboard.press('Enter');
    assert.equal(await diagnostics.evaluate(node => node.open), true);
    const before = await page.locator('#world-generator-stage').inputValue();
    await page.locator('#world-generator-previous').focus(); await page.keyboard.press('Enter');
    assert.equal(Number(await page.locator('#world-generator-stage').inputValue()), Number(before) - 1);
    assert.equal(generations, 1);
    assert.deepEqual(errors, []);
    console.log('PASS: Enter/Space disclosure, Tab order, retained values and map nodes, no regeneration, warning visibility, keyboard stage navigation.');
  } finally { await browser.close(); }
})().catch(error => { console.error(error); process.exitCode = 1; });
