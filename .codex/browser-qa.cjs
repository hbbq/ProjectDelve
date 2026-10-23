'use strict';
// This module is protected with its PowerShell entry point. Input is data only.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const ROOT = path.resolve(__dirname, '..');
const ORIGIN = 'http://127.0.0.1:5081';
const FIELDS = {
  snapshot: ['target', 'name'], screenshot: ['name'], resize: ['width', 'height'],
  click: ['target'], fill: ['target', 'value'], select: ['target', 'value'],
  hover: ['target'], reload: [], console: [], requests: [],
};
function object(value, label) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error(`${label} must be an object`);
}
function keys(value, allowed, label) {
  object(value, label);
  for (const key of Object.keys(value)) if (!allowed.includes(key)) throw new Error(`Unexpected ${label} field: ${key}`);
}
function target(value) {
  if (typeof value !== 'string' || !value.length || value.length > 256 ||
      /[\x00-\x1f\x7f]/.test(value) || /^[a-zA-Z_-][a-zA-Z0-9_-]*=/.test(value) ||
      !/^[a-zA-Z0-9_#.*\[\]="' >-]+$/.test(value) || value.includes('>>'))
    throw new Error('Target must be a simple CSS selector (no code or locator expressions)');
}
function validatePlan(plan) {
  keys(plan, ['steps'], 'plan');
  if (!Array.isArray(plan.steps) || plan.steps.length < 1 || plan.steps.length > 40) throw new Error('Provide 1 to 40 steps');
  const names = new Set(['final.png', 'final.yaml', 'report.json']);
  for (const step of plan.steps) {
    object(step, 'step');
    if (typeof step.action !== 'string' || !Object.hasOwn(FIELDS, step.action)) throw new Error('Unsupported browser action');
    keys(step, ['action', ...FIELDS[step.action]], 'step');
    if (FIELDS[step.action].includes('target') && (step.action !== 'snapshot' || step.target !== undefined)) target(step.target);
    if (step.action === 'fill' || step.action === 'select') {
      if (typeof step.value !== 'string' || step.value.length > 2000 || /[\x00-\x08\x0b-\x1f]/.test(step.value)) throw new Error('Value must be plain text, at most 2000 characters');
    }
    if (step.action === 'resize') {
      if (!Number.isInteger(step.width) || step.width < 320 || step.width > 2560 ||
          !Number.isInteger(step.height) || step.height < 320 || step.height > 1800) throw new Error('Viewport must be 320..2560 by 320..1800');
    }
    if (step.name !== undefined) {
      const extension = step.action === 'screenshot' ? 'png' : 'yaml';
      if (typeof step.name !== 'string' || /[\x00-\x1f\x7f]/.test(step.name) || !new RegExp(`^[a-zA-Z][a-zA-Z0-9_-]{0,63}\\.${extension}$`).test(step.name) ||
          /^(con|prn|aux|nul|com[0-9]|lpt[0-9])\./i.test(step.name) || names.has(step.name.toLowerCase())) throw new Error('Output name must be a unique plain artifact basename');
      names.add(step.name.toLowerCase());
    }
  }
  return plan;
}
function noLinks(absolute) {
  const parsed = path.parse(absolute);
  let current = parsed.root;
  for (const part of absolute.slice(parsed.root.length).split(path.sep).filter(Boolean)) {
    current = path.join(current, part);
    if (fs.existsSync(current) && fs.lstatSync(current).isSymbolicLink()) throw new Error('Junctions and symbolic links are not accepted');
  }
}
function planPath(root, input) {
  if (typeof input !== 'string' || /[\x00-\x1f\x7f]/.test(input) || !/^\.playwright-cli[\\/][a-zA-Z0-9_./\\-]+\.json$/.test(input) || input.includes('..')) throw new Error('Plan must be a JSON file under .playwright-cli');
  const resolved = path.resolve(root, input);
  if (!resolved.startsWith(path.join(root, '.playwright-cli') + path.sep)) throw new Error('Plan escapes artifact directory');
  noLinks(resolved);
  if (!fs.statSync(resolved).isFile() || fs.statSync(resolved).size > 65536) throw new Error('Plan must be a regular file, at most 64 KiB');
  return resolved;
}
function localUrl(value) {
  try { const url = new URL(value); return url.origin === ORIGIN && !url.username && !url.password; } catch { return false; }
}
function artifact(dir, name, data) {
  noLinks(dir);
  // Never follow or overwrite an existing output file, including hard links.
  const fd = fs.openSync(path.join(dir, name), 'wx');
  try { fs.writeFileSync(fd, data); } finally { fs.closeSync(fd); }
}
async function inspect(plan, runtime) {
  const base = path.join(ROOT, '.playwright-cli', 'browser-qa');
  noLinks(base);
  fs.mkdirSync(base, { recursive: true });
  const dir = fs.mkdtempSync(path.join(base, 'inspection-'));
  const session = 'delve-qa-' + crypto.randomUUID();
  // Absolute installed package path avoids workspace module resolution.
  const { chromium } = require(runtime.playwright);
  const browser = await chromium.launch({ channel: 'msedge', headless: false });
  const messages = [], requests = [];
  let violation;
  try {
    const context = await browser.newContext({ acceptDownloads: false, serviceWorkers: 'block', viewport: { width: 1440, height: 1000 } });
    context.setDefaultTimeout(10000);
    context.setDefaultNavigationTimeout(15000);
    await context.routeWebSocket('**/*', socket => socket.close());
    await context.route('**/*', async route => {
      const request = route.request();
      if (!localUrl(request.url())) { requests.push({ blocked: request.url() }); return route.abort('blockedbyclient'); }
      if (request.isNavigationRequest()) {
        // Do not follow even a localhost redirect to an external destination.
        try {
          const response = await route.fetch({ maxRedirects: 0, timeout: 15000 });
          if (response.status() >= 300 && response.status() < 400) return route.abort('blockedbyclient');
          return route.fulfill({ response });
        } catch { return route.abort('failed'); }
      }
      return route.continue();
    });
    const page = await context.newPage();
    context.on('page', popup => { if (popup !== page) void popup.close(); });
    page.on('framenavigated', frame => {
      if (frame === page.mainFrame() && !localUrl(frame.url())) { violation = 'Page left the isolated QA origin'; void page.close(); }
    });
    page.on('download', download => { void download.cancel(); });
    page.on('dialog', dialog => { void dialog.dismiss(); });
    page.on('console', message => messages.push({ type: message.type(), text: message.text().slice(0, 4000) }));
    page.on('pageerror', error => messages.push({ type: 'pageerror', text: error.message.slice(0, 4000) }));
    page.on('requestfailed', request => requests.push({ url: request.url(), failure: request.failure()?.errorText }));
    page.on('response', response => { if (response.status() >= 400) requests.push({ url: response.url(), status: response.status() }); });
    await page.goto(ORIGIN + '/', { waitUntil: 'domcontentloaded' });
    await page.locator('h1').waitFor();
    let number = 0;
    for (const step of plan.steps) {
      if (violation || !localUrl(page.url())) throw new Error(violation || 'Unexpected page URL');
      const locator = step.target === undefined ? undefined : page.locator('css=' + step.target);
      switch (step.action) {
        case 'click': await locator.click(); break;
        case 'fill': await locator.fill(step.value); break;
        case 'select': await locator.selectOption(step.value); break;
        case 'hover': await locator.hover(); break;
        case 'resize': await page.setViewportSize({ width: step.width, height: step.height }); break;
        case 'reload': await page.reload({ waitUntil: 'domcontentloaded' }); break;
        case 'snapshot': artifact(dir, step.name || `snapshot-${++number}.yaml`, await (locator || page.locator('body')).ariaSnapshot()); break;
        case 'screenshot': artifact(dir, step.name || `screenshot-${++number}.png`, await page.screenshot({ fullPage: true })); break;
        case 'console': console.log(JSON.stringify({ console: messages })); break;
        case 'requests': console.log(JSON.stringify({ requests })); break;
      }
    }
    if (violation || !localUrl(page.url())) throw new Error(violation || 'Unexpected page URL');
    artifact(dir, 'final.yaml', await page.locator('body').ariaSnapshot());
    artifact(dir, 'final.png', await page.screenshot({ fullPage: true }));
    artifact(dir, 'report.json', JSON.stringify({ session, url: page.url(), messages, requests }, null, 2));
    console.log(JSON.stringify({ session, artifacts: path.relative(ROOT, dir), steps: plan.steps.length }));
  } finally { await browser.close(); }
}
async function main() {
  const input = fs.readFileSync(0, 'utf8');
  if (input.length > 8192) throw new Error('Invocation too large');
  const command = JSON.parse(input);
  keys(command, ['action', 'planFile'], 'invocation');
  if (!['inspect', 'validate'].includes(command.action)) throw new Error('Unsupported dispatcher action');
  const plan = validatePlan(JSON.parse(fs.readFileSync(planPath(ROOT, command.planFile), 'utf8').replace(/^\uFEFF/, '')));
  if (command.action === 'validate') { console.log('Browser QA plan valid'); return; }
  const runtime = JSON.parse(fs.readFileSync(path.join(__dirname, 'browser-qa.runtime.json'), 'utf8'));
  await inspect(plan, runtime);
}
module.exports = { validatePlan, planPath, localUrl, noLinks, artifact };
if (require.main === module) main().catch(error => { console.error(error.message); process.exitCode = 1; });
