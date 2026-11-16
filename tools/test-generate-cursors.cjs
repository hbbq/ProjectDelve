'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawnSync } = require('node:child_process');
const { generate, validatePng, validateFilenames } = require('./generate-cursors.cjs');
const fixture = '<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40" viewBox="0 0 80 40"><rect x="8" y="4" width="64" height="32" fill="#ff0000"/></svg>';

function setup(t) {
    const root = fs.mkdtempSync(path.join(os.tmpdir(), 'delve-cursors-'));
    t.after(() => fs.rmSync(root, { recursive: true, force: true }));
    const options = { sourceDir: path.join(root, 'svg'), outputDir: path.join(root, 'generated'), manifestPath: path.join(root, 'catalog.json') };
    fs.mkdirSync(options.sourceDir);
    fs.writeFileSync(path.join(options.sourceDir, 'wide.svg'), fixture);
    return { root, options, source: name => path.join(options.sourceDir, name), png: (size, id = 'wide') => path.join(options.outputDir, String(size), `${id}.png`) };
}

test('CLI produces sorted manifest, deterministic RGBA PNGs, centered aspect ratio and authored margins', t => {
    const f = setup(t);
    fs.writeFileSync(f.source('another.svg'), fixture);
    const args = [path.join(__dirname, 'generate-cursors.cjs'), '--source', f.options.sourceDir, '--output', f.options.outputDir, '--manifest', f.options.manifestPath];
    for (let run = 0; run < 2; run++) {
        const result = spawnSync(process.execPath, args, { encoding: 'utf8' });
        assert.equal(result.status, 0, result.stderr);
    }
    const manifest = JSON.parse(fs.readFileSync(f.options.manifestPath));
    assert.deepEqual(manifest, { schemaVersion: 1, cursors: ['another', 'wide'].map(id => ({ id, images: Object.fromEntries([32, 40, 48].map(size => [size, `./presentation/cursors/generated/${size}/${id}.png`])) })) });
    const before = [32, 40, 48].map(size => fs.readFileSync(f.png(size)));
    const beforeManifest = fs.readFileSync(f.options.manifestPath);
    generate(f.options);
    for (const [index, size] of [32, 40, 48].entries()) {
        const bytes = fs.readFileSync(f.png(size));
        assert.deepEqual(bytes, before[index]);
        const pixels = validatePng(bytes, size);
        const alpha = (x, y) => pixels[(y * size + x) * 4 + 3];
        assert.equal(alpha(0, 0), 0);
        assert.equal(alpha(size / 2, size / 2), 255);
        assert.equal(alpha(size / 2, Math.floor(size * .2)), 0);
        assert.equal(alpha(Math.floor(size * .05), size / 2), 0);
        const opaque = [];
        for (let y = 0; y < size; y++) for (let x = 0; x < size; x++) if (alpha(x, y) === 255) opaque.push([x, y]);
        const xs = opaque.map(p => p[0]), ys = opaque.map(p => p[1]);
        const width = Math.max(...xs) - Math.min(...xs) + 1, height = Math.max(...ys) - Math.min(...ys) + 1;
        assert.ok(Math.abs(width / height - 2) < .2);
        assert.equal(Math.min(...xs) + Math.max(...xs), size - 1);
        assert.equal(Math.min(...ys) + Math.max(...ys), size - 1);
    }
    assert.deepEqual(fs.readFileSync(f.options.manifestPath), beforeManifest);
    assert.equal(fs.readFileSync(f.source('wide.svg'), 'utf8'), fixture);
});

test('stale PNG cleanup is bounded and happens only after successful generation', t => {
    const f = setup(t);
    generate(f.options);
    const stale = f.png(32, 'obsolete');
    fs.writeFileSync(stale, 'old');
    fs.writeFileSync(path.join(f.options.outputDir, '32', 'keep.txt'), 'keep');
    const good = fs.readFileSync(f.png(32));
    const manifest = fs.readFileSync(f.options.manifestPath);
    fs.writeFileSync(f.source('unsafe.svg'), '<svg><script/></svg>');
    assert.throws(() => generate(f.options), /unsupported element/);
    assert.deepEqual(fs.readFileSync(f.png(32)), good);
    assert.deepEqual(fs.readFileSync(f.options.manifestPath), manifest);
    assert.equal(fs.readFileSync(stale, 'utf8'), 'old');
    fs.unlinkSync(f.source('unsafe.svg'));
    generate(f.options);
    assert.equal(fs.existsSync(stale), false);
    assert.equal(fs.readFileSync(path.join(f.options.outputDir, '32', 'keep.txt'), 'utf8'), 'keep');
});

test('rejects missing, empty, invalid ID, duplicate case ID and unexpected inputs', t => {
    const cases = [
        ['missing', /Missing SVG input/], ['empty', /No SVG input/],
        ['bad_name.svg', /Invalid cursor ID/], ['Upper.svg', /Invalid cursor ID/], ['1cursor.svg', /Invalid cursor ID/],
        ['upper.SVG', /Invalid cursor ID/], ['README.md', /Unexpected input/],
        ['directory', /regular files/]
    ];
    for (const [name, expected] of cases) {
        const f = setup(t);
        if (name === 'missing') fs.rmSync(f.options.sourceDir, { recursive: true });
        else if (name === 'empty') fs.unlinkSync(f.source('wide.svg'));
        else if (name === 'directory') fs.mkdirSync(f.source('nested'));
        else fs.writeFileSync(f.source(name), fixture);
        assert.throws(() => generate(f.options), expected, name);
        assert.equal(fs.existsSync(f.options.outputDir), false);
    }
    // A case-insensitive filesystem cannot create both names; test the shared
    // discovery validation directly as well as exercising it through real files.
    assert.throws(() => validateFilenames(['attack.svg', 'Attack.svg']), /Duplicate cursor ID/);
    for (const name of ['../escape.svg', 'a/b.svg', 'a\\b.svg', '-leading.svg', 'double--dash.svg', '.svg']) assert.throws(() => validateFilenames([name]), /Invalid cursor ID/);
    const devices = ['con', 'prn', 'aux', 'nul', ...Array.from({ length: 10 }, (_, i) => `com${i}`), ...Array.from({ length: 10 }, (_, i) => `lpt${i}`)];
    for (const id of devices) assert.throws(() => validateFilenames([`${id}.svg`]), /Reserved Windows device cursor ID/, id);
    assert.deepEqual(validateFilenames(['console.svg', 'com10.svg', 'lpt10.svg', 'nul-cursor.svg']).map(source => source.id), ['console', 'com10', 'lpt10', 'nul-cursor']);
});

test('rejects malformed XML and active or externally resolved SVG constructs', t => {
    const cases = [
        '<svg>', fixture.replace('</svg>', ''), fixture.replace('<rect ', '<script '),
        fixture.replace('<rect ', '<image href="https://example.com/a.png" '),
        fixture.replace('<rect ', '<text '), fixture.replace('<rect ', '<foreignObject '),
        fixture.replace('<rect ', '<use href="#shape" '),
        fixture.replace('fill="#ff0000"', 'style="fill:red"'),
        fixture.replace('fill="#ff0000"', 'fill="url(https://example.com/a)"'),
        fixture.replace('fill="#ff0000"', 'onclick="alert(1)"'),
        '<!DOCTYPE svg [<!ENTITY x SYSTEM "file:///secret">]>' + fixture,
        fixture.replace('<rect ', '&amp;<rect '),
        '<?xml-stylesheet href="https://example.com/a.css"?>' + fixture,
        fixture.replace('<rect ', '<![CDATA[hello]]><rect '),
        fixture.replace('<rect ', '<!-- no comments --><rect '),
        fixture.replace('width="80"', 'width="100%"'),
        fixture.replace('viewBox="0 0 80 40"', 'viewBox="0 0 0 40"'),
        fixture.replace('fill="#ff0000"', 'fill="none"'),
    ];
    for (const svg of cases) {
        const f = setup(t);
        fs.writeFileSync(f.source('wide.svg'), svg);
        assert.throws(() => generate(f.options), /./, svg);
        assert.equal(fs.existsSync(f.options.outputDir), false);
    }
});

test('refuses linked input/output/ancestors and never removes external files', t => {
    const f = setup(t);
    const outside = path.join(f.root, 'outside');
    fs.mkdirSync(outside);
    fs.writeFileSync(path.join(outside, 'obsolete.png'), 'external');
    fs.mkdirSync(f.options.outputDir);
    // Directory junctions are supported without Windows symbolic-link privilege.
    fs.symlinkSync(outside, path.join(f.options.outputDir, '32'), process.platform === 'win32' ? 'junction' : 'dir');
    assert.throws(() => generate(f.options), /Symbolic link/);
    assert.equal(fs.readFileSync(path.join(outside, 'obsolete.png'), 'utf8'), 'external');
    const throughLink = { ...f.options, outputDir: path.join(f.options.outputDir, '32', 'nested') };
    assert.throws(() => generate(throughLink), /Symbolic link/);
    fs.symlinkSync(outside, f.source('linked'), process.platform === 'win32' ? 'junction' : 'dir');
    assert.throws(() => generate({ ...f.options, outputDir: path.join(f.root, 'clean-output') }), /regular files/);
    assert.throws(() => generate({ ...f.options, outputDir: f.options.sourceDir }), /separate/);
});

test('viewBox-only SVG keeps its aspect ratio and transparent padding', t => {
    const f = setup(t);
    fs.writeFileSync(f.source('wide.svg'), fixture.replace(' width="80" height="40"', ''));
    generate(f.options);
    const pixels = validatePng(fs.readFileSync(f.png(40)), 40);
    assert.equal(pixels[(20 * 40 + 20) * 4 + 3], 255);
    assert.equal(pixels[(4 * 40 + 20) * 4 + 3], 0);
});

test('oversized SVG fails before modifying existing outputs or cleaning stale PNGs', t => {
    const f = setup(t);
    generate(f.options);
    const good = fs.readFileSync(f.png(32));
    const catalog = fs.readFileSync(f.options.manifestPath);
    const stale = f.png(32, 'obsolete');
    fs.writeFileSync(stale, 'preserve on failure');
    fs.writeFileSync(f.source('wide.svg'), fixture + ' '.repeat(1024 * 1024));
    assert.throws(() => generate(f.options), /1 MiB limit/);
    assert.deepEqual(fs.readFileSync(f.png(32)), good);
    assert.deepEqual(fs.readFileSync(f.options.manifestPath), catalog);
    assert.equal(fs.readFileSync(stale, 'utf8'), 'preserve on failure');
});
