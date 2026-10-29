// Standalone, explicitly reviewed Edge acceptance run against the isolated QA host.
// Usage: <installed node> ProjectDelve.Web.Tests/library.browser.cjs <absolute installed playwright package path>
// Requires a bundled test-owned scenario ID "v11-qa-bundled" with designer-setup.json's definition.
// Writes only screenshots/report beneath a unique .playwright-cli/library-v11-* directory.
'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const zlib = require('node:zlib');
const ORIGIN = 'http://127.0.0.1:5081';
const BUNDLED_ID = 'v11-qa-bundled';
const root = path.resolve(__dirname, '..');
const playwrightPath = process.argv[2];
if (process.argv.length !== 3 || !path.isAbsolute(playwrightPath)) throw new Error('Provide the absolute path to the installed Playwright package.');
const { chromium } = require(playwrightPath);
const suffix = `${Date.now()}-${crypto.randomBytes(4).toString('hex')}`;
const artifacts = path.join(root, '.playwright-cli', `library-v11-${suffix}`);
fs.mkdirSync(artifacts, { recursive: false });
const definition = JSON.parse(fs.readFileSync(path.join(__dirname, 'fixtures', 'designer-setup.json'), 'utf8'));

// A small deterministic test-owned PNG, avoiding external artwork or filesystem uploads.
function crc32(bytes) {
  let crc = 0xffffffff;
  for (const byte of bytes) { crc ^= byte; for (let bit = 0; bit < 8; bit++) crc = (crc >>> 1) ^ ((crc & 1) ? 0xedb88320 : 0); }
  return (crc ^ 0xffffffff) >>> 0;
}
function chunk(type, data) {
  const name = Buffer.from(type), length = Buffer.alloc(4), crc = Buffer.alloc(4);
  length.writeUInt32BE(data.length); crc.writeUInt32BE(crc32(Buffer.concat([name, data])));
  return Buffer.concat([length, name, data, crc]);
}
function coverPng() {
  const width = 48, height = 32, header = Buffer.alloc(13);
  header.writeUInt32BE(width, 0); header.writeUInt32BE(height, 4); header[8] = 8; header[9] = 6;
  const rows = Buffer.alloc(height * (1 + width * 4));
  for (let y = 0; y < height; y++) for (let x = 0; x < width; x++) {
    const offset = y * (1 + width * 4) + 1 + x * 4;
    rows.set([30 + x * 3, 45 + y * 3, 80, 255], offset);
  }
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]), chunk('IHDR', header), chunk('IDAT', zlib.deflateSync(rows)), chunk('IEND', Buffer.alloc(0))]);
}
const cover = { name: 'test-owned-cover.png', mimeType: 'image/png', buffer: coverPng() };

(async () => {
  let browser, context;
  const errors = [], requests = [], unexpectedDialogs = [];
  let page, scenario, campaign;
  const json = async url => {
    const response = await context.request.get(ORIGIN + url);
    assert.equal(response.ok(), true, `${url}: ${response.status()}`);
    return response.json();
  };
  const screenshot = name => page.screenshot({ path: path.join(artifacts, name + '.png'), fullPage: true });
  const waitValue = (selector, value) => page.waitForFunction(({selector, value}) => document.querySelector(selector)?.value === value, {selector, value});
  const responseFor = (pathname, method) => page.waitForResponse(response => new URL(response.url()).pathname === pathname && response.request().method() === method && response.status() >= 200 && response.status() < 300);
  const clickResponse = async (selector, pathname, method) => {
    const [response] = await Promise.all([responseFor(pathname, method), page.locator(selector).click()]);
    return response.status() === 204 ? null : response.json();
  };
  const openDesignerDocument = async () => {
    await page.locator('#mode-designer').click();
    await page.locator('#designer-main').waitFor({ state: 'visible' });
    await page.locator('[data-designer-tab="document"]').click();
  };
  const uploadCover = async (input, preview) => {
    const [response] = await Promise.all([responseFor('/api/library/covers', 'POST'), page.locator(input).setInputFiles(cover)]);
    const result = await response.json();
    assert.match(result.coverArt, /^\/api\/library\/covers\/[a-f0-9]{64}\.png$/);
    await page.waitForFunction(selector => {
      const image = document.querySelector(selector); return image?.complete && image.naturalWidth > 0;
    }, preview);
    const received = await context.request.get(ORIGIN + result.coverArt);
    assert.equal(received.status(), 200); assert.match(received.headers()['content-type'], /^image\/png/);
    return result.coverArt;
  };
  const noOverflow = async () => assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth), false, 'Narrow layout overflows horizontally');
  try {
    browser = await chromium.launch({ channel: 'msedge', headless: false });
    context = await browser.newContext({ viewport: { width: 1440, height: 1000 }, acceptDownloads: false, serviceWorkers: 'block' });
    await context.route('**/*', async route => {
      const url = new URL(route.request().url());
      if (url.origin === ORIGIN || ['data:', 'blob:'].includes(url.protocol)) await route.continue();
      else { requests.push(`Blocked external request: ${url.origin}`); await route.abort(); }
    });
    page = await context.newPage();
    page.setDefaultTimeout(15000);
    page.on('console', message => { if (message.type() === 'error') errors.push(message.text()); });
    page.on('pageerror', error => errors.push(error.message));
    page.on('requestfailed', request => {
      // Navigating away closes the long-lived SSE connection normally.
      if (!request.url().endsWith('/api/game/updates') && !request.failure()?.errorText.includes('ERR_ABORTED')) requests.push(`${request.method()} ${request.url()}: ${request.failure()?.errorText}`);
    });
    page.on('response', response => { if (response.status() >= 400) requests.push(`${response.status()} ${response.url()}`); });
    page.on('dialog', async dialog => {
      if (dialog.type() === 'confirm' && dialog.message().startsWith('Replace this draft?')) await dialog.accept();
      else { unexpectedDialogs.push(dialog.message()); await dialog.dismiss(); }
    });
    page.on('popup', popup => popup.close());
    await page.goto(ORIGIN, { waitUntil: 'domcontentloaded' });
    const bundledBefore = await json(`/api/library/scenarios/${BUNDLED_ID}`);
    assert.equal(bundledBefore.source, 'bundled', 'QA fixture must start unmodified and visible');
    await page.locator('#library-title').waitFor({ state: 'visible' });
    await screenshot('home-desktop');

    // Create a local scenario through Designer, including a real file-input cover import.
    await openDesignerDocument();
    const scenarioName = `V1.1 QA scenario ${suffix}`;
    const draft = { format: 'ProjectDelveDraft', version: 1, title: scenarioName, definition, buffers: {} };
    await page.locator('#designer-file').setInputFiles({ name: 'v11-test-owned.delve-draft.json', mimeType: 'application/json', buffer: Buffer.from(JSON.stringify(draft)) });
    await waitValue('#designer-title', scenarioName);
    const scenarioCover = await uploadCover('#designer-cover-file', '#designer-cover-preview img');
    scenario = await clickResponse('#designer-library-save', '/api/library/scenarios', 'POST');
    assert.equal(scenario.name, scenarioName); assert.equal(scenario.source, 'local'); assert.equal(scenario.coverArt, scenarioCover);
    await page.waitForFunction(() => document.querySelector('#designer-library-status').textContent.startsWith('Saved'));
    await screenshot('designer-desktop');

    // Unsaved workspace edits survive Playtest and return; no library save occurs.
    const savedBeforePlay = await json(`/api/library/scenarios/${scenario.id}`);
    await page.locator('#designer-title').fill('Unsaved V1.1 workspace');
    await page.locator('.designer-transport summary').click();
    await page.locator('#designer-transport').fill('Unfinished import text retained');
    await page.locator('#designer-play').click();
    await page.locator('#playtest-main').waitFor({ state: 'visible' });
    await page.locator('#mode-designer').click();
    await page.locator('#designer-main').waitFor({ state: 'visible' });
    assert.equal(await page.locator('#designer-title').inputValue(), 'Unsaved V1.1 workspace');
    assert.equal(await page.locator('#designer-transport').inputValue(), 'Unfinished import text retained');
    assert.deepEqual(await json(`/api/library/scenarios/${scenario.id}`), savedBeforePlay);
    await page.setViewportSize({ width: 390, height: 844 });
    await noOverflow(); await screenshot('designer-narrow');
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator('#designer-main').waitFor({ state: 'visible' });
    await waitValue('#designer-title', 'Unsaved V1.1 workspace');
    await page.locator('[data-designer-tab="document"]').click();
    await page.locator('.designer-transport summary').click();
    assert.equal(await page.locator('#designer-transport').inputValue(), 'Unfinished import text retained');

    // A local campaign references both sources, owns its cover, and preserves order on reload.
    await page.locator('#mode-home').click();
    await page.locator('#library-main a[href="#campaigns-edit"]').click();
    await page.locator('#campaign-name').fill(`V1.1 QA campaign ${suffix}`);
    await page.locator(`[data-add-scenario="${scenario.id}"]`).click();
    await page.locator(`[data-add-scenario="${BUNDLED_ID}"]`).click();
    await page.locator(`#campaign-scenarios [data-scenario-id="${BUNDLED_ID}"] [data-campaign-action="up"]`).click();
    const campaignCover = await uploadCover('#campaign-cover-file', '.campaign-cover-preview img');
    assert.equal(campaignCover, scenarioCover, 'Identical imported images should share one canonical reference');
    await noOverflow(); await screenshot('campaign-editor-narrow');
    campaign = await clickResponse('#campaign-save', '/api/library/campaigns', 'POST');
    assert.equal(campaign.source, 'local'); assert.deepEqual(campaign.scenarioIds, [BUNDLED_ID, scenario.id]);
    await page.locator('#library-main').waitFor({ state: 'visible' });
    await page.waitForFunction(id => location.hash === `#campaign/${id}`, campaign.id);
    await page.reload({ waitUntil: 'domcontentloaded' });
    await page.locator(`[data-scenario-id="${scenario.id}"]`).waitFor();
    assert.deepEqual(await page.locator('#library-cards article').evaluateAll(cards => cards.map(card => card.dataset.scenarioId)), [BUNDLED_ID, scenario.id]);
    assert.deepEqual((await json(`/api/library/campaigns/${campaign.id}`)).scenarioIds, [BUNDLED_ID, scenario.id]);
    await noOverflow(); await screenshot('campaign-narrow');
    await page.setViewportSize({ width: 1440, height: 1000 });
    await screenshot('campaign-desktop');

    // Edit a bundled scenario through the same Designer save operation, then explicitly restore it.
    await openDesignerDocument();
    await page.locator('#designer-library-select').selectOption(BUNDLED_ID);
    await page.locator('#designer-library-open').click();
    await page.locator('#designer-main[aria-busy="false"]').waitFor();
    await waitValue('#designer-title', bundledBefore.name);
    await page.locator('#designer-title').fill('QA local override');
    const override = await clickResponse('#designer-library-save', `/api/library/scenarios/${BUNDLED_ID}`, 'PUT');
    assert.equal(override.source, 'override'); assert.equal(override.id, BUNDLED_ID);
    assert.equal((await json('/api/library')).scenarios.filter(item => item.id === BUNDLED_ID).length, 1);
    await page.locator('#designer-library-restore').click();
    const restored = await clickResponse('#designer-library-restore-apply', `/api/library/scenarios/${BUNDLED_ID}/restore`, 'POST');
    assert.equal(restored.source, 'bundled'); assert.equal(restored.name, bundledBefore.name);
    await waitValue('#designer-title', bundledBefore.name);
    assert.deepEqual(restored.definition, bundledBefore.definition);

    // Hiding bundled content keeps campaign references, and the library offers explicit restoration.
    await page.locator('#mode-scenarios').click();
    await page.locator(`[data-scenario-id="${BUNDLED_ID}"] a`).click();
    await page.locator('#library-cards').getByRole('button', { name: 'Hide locally', exact: true }).click();
    await Promise.all([
      responseFor(`/api/library/scenarios/${BUNDLED_ID}`, 'DELETE'),
      page.locator('#library-cards').getByRole('button', { name: 'Confirm deletion', exact: true }).click()
    ]);
    await page.locator('#library-hidden').waitFor({ state: 'visible' });
    const hiddenLibrary = await json('/api/library');
    assert.equal(hiddenLibrary.scenarios.some(item => item.id === BUNDLED_ID), false);
    assert.equal(hiddenLibrary.hiddenScenarios.some(item => item.id === BUNDLED_ID), true);
    assert.deepEqual((await json(`/api/library/campaigns/${campaign.id}`)).scenarioIds, [BUNDLED_ID, scenario.id]);
    await page.locator('#library-hidden summary').click();
    const hiddenRow = page.locator('.library-hidden-row').filter({ hasText: bundledBefore.name });
    await hiddenRow.getByRole('button', { name: 'Restore…', exact: true }).click();
    await clickResponse(`[data-restore-id="${BUNDLED_ID}"]`, `/api/library/scenarios/${BUNDLED_ID}/restore`, 'POST');
    await page.locator(`[data-scenario-id="${BUNDLED_ID}"]`).waitFor();
    assert.equal((await json(`/api/library/scenarios/${BUNDLED_ID}`)).source, 'bundled');
    assert.equal((await json('/api/library')).scenarios.filter(item => item.id === BUNDLED_ID).length, 1);
    await screenshot('restored-desktop');

    assert.deepEqual(errors, [], 'Browser console errors');
    assert.deepEqual(requests, [], 'Failed or external browser requests');
    assert.deepEqual(unexpectedDialogs, [], 'Unexpected dialogs');
    fs.writeFileSync(path.join(artifacts, 'report.json'), JSON.stringify({ success: true, errors, requests, unexpectedDialogs, scenarioId: scenario.id, campaignId: campaign.id, coverArt: scenarioCover }, null, 2), { flag: 'wx' });
    console.log(JSON.stringify({ success: true, artifacts: path.relative(root, artifacts), scenarioId: scenario.id, campaignId: campaign.id }));
  } catch (error) {
    if (page && !page.isClosed()) await screenshot('failure').catch(() => {});
    fs.writeFileSync(path.join(artifacts, 'report.json'), JSON.stringify({ success: false, error: error.stack, errors, requests, unexpectedDialogs, scenarioId: scenario?.id, campaignId: campaign?.id }, null, 2), { flag: 'wx' });
    throw error;
  } finally {
    await context?.close(); await browser?.close();
  }
})().catch(error => { console.error(error.stack); process.exitCode = 1; });
