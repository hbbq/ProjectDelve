'use strict';
const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const qa = require('../.codex/browser-qa.cjs');
const root = path.resolve(__dirname, '..');

test('inspection plan accepts only the explicit operation schema', () => {
  const steps = [
    { action: 'click', target: '#mode-designer' },
    { action: 'fill', target: '#designer-title', value: 'Temporary QA draft' },
    { action: 'select', target: '#designer-terrain', value: 'Grass' },
    { action: 'hover', target: 'h1' }, { action: 'reload' },
    { action: 'resize', width: 600, height: 800 },
    { action: 'snapshot', target: '[data-designer-tab="board"]', name: 'board.yaml' },
    { action: 'screenshot', name: 'mobile.png' }, { action: 'console' }, { action: 'requests' },
  ];
  assert.equal(qa.validatePlan({ steps }).steps, steps);
});

test('PowerShell rejects unknown parameters before starting the browser', () => {
  const { spawnSync } = require('node:child_process');
  const powershell = path.join(process.env.SystemRoot, 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
  for (const flag of ['-Session', '-Code', '-Url', '-ScriptFile', '-BrowserFlags', '-Command']) {
    const result = spawnSync(powershell, ['-NoLogo', '-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'RemoteSigned',
      '-File', path.join(root, '.codex', 'browser-qa.ps1'), '-Action', 'validate', '-PlanFile', '.playwright-cli/nonexistent.json', flag, 'forbidden'],
      { encoding: 'utf8', timeout: 10000 });
    assert.notEqual(result.status, 0, flag);
    assert.match(result.stderr, /parameter cannot be found|positional parameter cannot be found/i, flag);
  }
});
test('rejects code, foreign sessions, navigation and launch options', () => {
  for (const action of ['eval', 'run-code', 'attach', 'goto', 'install', 'close-all', 'kill-all', '__proto__'])
    assert.throws(() => qa.validatePlan({ steps: [{ action }] }));
  for (const field of ['session', 'url', 'script', 'code', 'browser', 'flags', '__proto__']) {
    const plan = JSON.parse(`{"steps":[{"action":"console"}],"${field}":"external"}`);
    assert.throws(() => qa.validatePlan(plan));
    assert.throws(() => qa.validatePlan({ steps: [{ action: 'click', target: 'h1', [field]: 'external' }] }));
  }
  for (const target of ['javascript:alert(1)', 'getByRole("button")', 'css=body', 'body >> text=foo', 'https://example.com', 'h1\n'])
    assert.throws(() => qa.validatePlan({ steps: [{ action: 'click', target }] }), undefined, JSON.stringify(target));
  assert.throws(() => qa.validatePlan({ steps: [{ action: ['console'] }] }));
});
test('rejects path escapes, alternate streams, devices and duplicate output names', () => {
  for (const name of ['../escape.png', 'C:\\escape.png', '\\\\host\\share.png', 'file.png:stream', 'CON.png', 'final.png', 'image.jpg', '--config.png', 'picture.png\n'])
    assert.throws(() => qa.validatePlan({ steps: [{ action: 'screenshot', name }] }));
  assert.throws(() => qa.validatePlan({ steps: [{ action: 'screenshot', name: 'one.png' }, { action: 'screenshot', name: 'ONE.png' }] }));
  for (const input of ['../outside.json', '.playwright-cli/../outside.json', '.playwright-cli/file.json:stream', 'C:\\outside.json', '\\\\host\\plan.json', '.playwright-cli/plan.json\n'])
    assert.throws(() => qa.planPath(root, input));
});
test('enforces viewport, count, text and field limits', () => {
  for (const step of [{ action: 'resize', width: 1, height: 800 }, { action: 'resize', width: 800, height: 99999 },
    { action: 'fill', target: 'h1', value: { code: 'x' } }, { action: 'fill', target: 'h1', value: 'x'.repeat(2001) },
    { action: 'screenshot', filename: 'escape.png' }]) assert.throws(() => qa.validatePlan({ steps: [step] }));
  assert.throws(() => qa.validatePlan({ steps: [] }));
  assert.throws(() => qa.validatePlan({ steps: Array(41).fill({ action: 'console' }) }));
});
test('only the fixed QA origin is local, including credentials and port checks', () => {
  assert.equal(qa.localUrl('http://127.0.0.1:5081/'), true);
  for (const url of ['http://127.0.0.1:5080/', 'https://127.0.0.1:5081/', 'http://localhost:5081/', 'http://user@127.0.0.1:5081/',
    'http://127.0.0.1:5081.evil.example/', 'https://example.com/', 'file:///C:/', 'javascript:alert(1)', 'about:blank'])
    assert.equal(qa.localUrl(url), false, url);
});
test('rejects junction plans and refuses output overwrites', () => {
  const base = path.join(root, '.playwright-cli');
  fs.mkdirSync(base, { recursive: true });
  const dir = fs.mkdtempSync(path.join(base, 'dispatcher-test-'));
  const junction = path.join(dir, 'linked');
  try {
    fs.writeFileSync(path.join(dir, 'plan.json'), '{"steps":[{"action":"console"}]}');
    assert.equal(qa.planPath(root, path.relative(root, path.join(dir, 'plan.json'))), path.join(dir, 'plan.json'));
    fs.symlinkSync(dir, junction, 'junction');
    assert.throws(() => qa.planPath(root, path.relative(root, path.join(junction, 'plan.json'))));
    qa.artifact(dir, 'result.txt', 'first');
    assert.throws(() => qa.artifact(dir, 'result.txt', 'overwrite'));
    assert.equal(fs.readFileSync(path.join(dir, 'result.txt'), 'utf8'), 'first');
  } finally {
    // Remove only these exact test-owned paths; never recurse through a junction.
    if (fs.existsSync(junction)) fs.unlinkSync(junction);
    for (const name of ['plan.json', 'result.txt']) if (fs.existsSync(path.join(dir, name))) fs.unlinkSync(path.join(dir, name));
    fs.rmdirSync(dir);
  }
});
