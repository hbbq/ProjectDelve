#!/usr/bin/env node
'use strict';

const fs = require('node:fs');
const path = require('node:path');
const zlib = require('node:zlib');
const { Resvg } = require('@resvg/resvg-js');
const { SaxesParser } = require('saxes');

const sizes = [32, 40, 48];
const idPattern = /^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$/;
const reservedDeviceId = /^(?:con|prn|aux|nul|com[0-9]|lpt[0-9])$/i;
const maximumSvgBytes = 1024 * 1024;
const elements = new Set(['svg', 'g', 'path', 'rect', 'circle', 'ellipse', 'line', 'polyline', 'polygon']);
const attributes = new Set(['xmlns', 'viewBox', 'width', 'height', 'role', 'aria-label', 'd', 'x', 'y', 'x1', 'y1', 'x2', 'y2', 'cx', 'cy', 'r', 'rx', 'ry', 'points', 'fill', 'stroke', 'stroke-width', 'stroke-linecap', 'stroke-linejoin', 'stroke-miterlimit', 'fill-rule', 'clip-rule', 'fill-opacity', 'stroke-opacity', 'opacity', 'transform']);

function fail(message) { throw new Error(message); }

function validateFilenames(names) {
    const ids = new Set();
    const sources = names.map(name => {
        if (!/\.svg$/i.test(name)) fail(`Unexpected input file: ${name}`);
        const id = name.slice(0, -4);
        if (ids.has(id.toLowerCase())) fail(`Duplicate cursor ID: ${id}`);
        ids.add(id.toLowerCase());
        return { id, name };
    });
    for (const source of sources) if (!idPattern.test(source.id) || source.name !== `${source.id}.svg`) fail(`Invalid cursor ID/filename: ${source.name}; use lowercase kebab-case.svg starting with a letter`);
    for (const source of sources) if (reservedDeviceId.test(source.id)) fail(`Reserved Windows device cursor ID: ${source.id}`);
    return sources;
}

// Check every existing component, including ancestors of a not-yet-created path.
function rejectLinks(target) {
    const absolute = path.resolve(target);
    const root = path.parse(absolute).root;
    let current = root;
    for (const part of absolute.slice(root.length).split(path.sep).filter(Boolean)) {
        current = path.join(current, part);
        if (!fs.existsSync(current)) {
            // existsSync returns false for dangling links.
            try { if (fs.lstatSync(current).isSymbolicLink()) fail(`Symbolic link is forbidden: ${current}`); }
            catch (error) { if (error.code !== 'ENOENT') throw error; }
            continue;
        }
        if (fs.lstatSync(current).isSymbolicLink()) fail(`Symbolic link is forbidden: ${current}`);
    }
}

function inspectTree(directory) {
    rejectLinks(directory);
    if (!fs.existsSync(directory)) return;
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
        const file = path.join(directory, entry.name);
        if (entry.isSymbolicLink()) fail(`Symbolic link is forbidden: ${file}`);
        if (entry.isDirectory()) inspectTree(file);
        else if (!entry.isFile()) fail(`Not a regular file: ${file}`);
    }
}

function validateSvg(svg, name) {
    if (Buffer.byteLength(svg, 'utf8') > maximumSvgBytes) fail(`${name}: SVG input exceeds the 1 MiB limit`);
    if (/<!DOCTYPE|<!ENTITY/i.test(svg) || svg.includes('&')) fail(`${name}: DTDs and entities are forbidden`);
    let depth = 0;
    let root;
    const parser = new SaxesParser({ xmlns: false });
    parser.on('error', error => fail(`${name}: malformed SVG: ${error.message}`));
    parser.on('doctype', () => fail(`${name}: DTDs are forbidden`));
    parser.on('processinginstruction', () => fail(`${name}: processing instructions are forbidden`));
    parser.on('cdata', () => fail(`${name}: CDATA is forbidden`));
    parser.on('comment', () => fail(`${name}: comments are forbidden`));
    parser.on('text', text => { if (text.trim()) fail(`${name}: text is forbidden`); });
    parser.on('opentag', tag => {
        if (!elements.has(tag.name) || (depth === 0 && tag.name !== 'svg') || (depth > 0 && tag.name === 'svg')) fail(`${name}: unsupported element ${tag.name}`);
        for (const [key, value] of Object.entries(tag.attributes)) {
            if (!attributes.has(key)) fail(`${name}: unsupported attribute ${key}`);
            if (/url\s*\(|[<>\\]|(?:https?|file|data|javascript):/i.test(value) && !(key === 'xmlns' && value === 'http://www.w3.org/2000/svg')) fail(`${name}: unsafe attribute ${key}`);
            if (key === 'xmlns' && (depth !== 0 || value !== 'http://www.w3.org/2000/svg')) fail(`${name}: unsupported namespace`);
        }
        if (depth === 0) root = tag.attributes;
        depth++;
    });
    parser.on('closetag', () => depth--);
    parser.write(svg).close();
    if (!root || root.xmlns !== 'http://www.w3.org/2000/svg') fail(`${name}: SVG namespace is required`);
    if (root.x !== undefined || root.y !== undefined || root.transform !== undefined) fail(`${name}: root positioning is forbidden`);
    const box = (root.viewBox || '').trim().split(/[\s,]+/).map(Number);
    if (box.length !== 4 || box.some(n => !Number.isFinite(n)) || box[2] <= 0 || box[3] <= 0) fail(`${name}: a positive finite viewBox is required`);
    const dimension = (value, fallback) => {
        if (value === undefined) return fallback;
        if (!/^(?:\d+(?:\.\d*)?|\.\d+)(?:px)?$/.test(value)) fail(`${name}: viewport dimensions must be positive numbers or px`);
        const number = Number(value.replace(/px$/, ''));
        if (!Number.isFinite(number) || number <= 0 || number > 16384) fail(`${name}: viewport dimension out of range`);
        return number;
    };
    const width = dimension(root.width, box[2]);
    const height = dimension(root.height, box[3]);
    if (width > 16384 || height > 16384) fail(`${name}: viewport dimension out of range`);
    // The nested SVG retains the complete authored viewport and its original margins.
    // Explicit dimensions also support viewBox-only originals without changing their bytes.
    let nested = svg.replace(/^\s*<\?xml[^?]*\?>\s*/, '');
    nested = nested.replace(/<svg(?=[\s>])/, `<svg x="${(Math.max(width, height) - width) / 2}" y="${(Math.max(width, height) - height) / 2}"${root.width === undefined ? ` width="${width}"` : ''}${root.height === undefined ? ` height="${height}"` : ''}`);
    return size => `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 ${Math.max(width, height)} ${Math.max(width, height)}">${nested}</svg>`;
}

function validatePng(bytes, size) {
    if (!bytes.subarray(0, 8).equals(Buffer.from('89504e470d0a1a0a', 'hex'))) fail('Renderer did not produce PNG');
    if (bytes.readUInt32BE(16) !== size || bytes.readUInt32BE(20) !== size || bytes[24] !== 8 || bytes[25] !== 6) fail('PNG must be square 8-bit RGBA at the requested size');
    const chunks = [];
    for (let offset = 8; offset < bytes.length;) {
        const length = bytes.readUInt32BE(offset);
        if (bytes.toString('ascii', offset + 4, offset + 8) === 'IDAT') chunks.push(bytes.subarray(offset + 8, offset + 8 + length));
        offset += length + 12;
    }
    const raw = zlib.inflateSync(Buffer.concat(chunks));
    const stride = size * 4;
    if (raw.length !== (stride + 1) * size) fail('Invalid PNG scanline data');
    const pixels = Buffer.alloc(stride * size);
    let transparent = false;
    let visible = false;
    const paeth = (a, b, c) => { const p = a + b - c; const da = Math.abs(p - a), db = Math.abs(p - b), dc = Math.abs(p - c); return da <= db && da <= dc ? a : db <= dc ? b : c; };
    for (let y = 0; y < size; y++) {
        const filter = raw[y * (stride + 1)];
        if (filter > 4) fail('Invalid PNG filter');
        for (let x = 0; x < stride; x++) {
            const i = y * stride + x;
            const a = x >= 4 ? pixels[i - 4] : 0, b = y ? pixels[i - stride] : 0, c = y && x >= 4 ? pixels[i - stride - 4] : 0;
            pixels[i] = (raw[y * (stride + 1) + x + 1] + [0, a, b, Math.floor((a + b) / 2), paeth(a, b, c)][filter]) & 255;
            if (x % 4 === 3) { transparent ||= pixels[i] < 255; visible ||= pixels[i] > 0; }
        }
    }
    if (!transparent || !visible) fail('Cursor must contain visible artwork and transparent pixels');
    return pixels;
}

function generate({ sourceDir, outputDir, manifestPath }) {
    sourceDir = path.resolve(sourceDir); outputDir = path.resolve(outputDir); manifestPath = path.resolve(manifestPath);
    const comparable = value => process.platform === 'win32' ? value.toLowerCase() : value;
    const sourceKey = comparable(sourceDir), outputKey = comparable(outputDir), manifestKey = comparable(manifestPath);
    if (outputKey === sourceKey || sourceKey.startsWith(outputKey + path.sep) || outputKey.startsWith(sourceKey + path.sep)) fail('Source and output directories must be separate');
    if (manifestKey === sourceKey || manifestKey.startsWith(sourceKey + path.sep)) fail('Manifest must be outside the source directory');
    rejectLinks(sourceDir); inspectTree(outputDir); rejectLinks(manifestPath);
    if (!fs.existsSync(sourceDir)) fail(`Missing SVG input directory: ${sourceDir}`);
    const entries = fs.readdirSync(sourceDir, { withFileTypes: true }).sort((a, b) => a.name < b.name ? -1 : a.name > b.name ? 1 : 0);
    for (const entry of entries) {
        if (entry.isSymbolicLink() || !entry.isFile()) fail(`SVG input must contain only regular files: ${entry.name}`);
    }
    const sources = validateFilenames(entries.map(entry => entry.name));
    const ids = new Set(sources.map(source => source.id));
    if (!sources.length) fail(`No SVG input files in ${sourceDir}`);
    const rendered = [];
    for (const source of sources) {
        const sourcePath = path.join(sourceDir, source.name);
        if (fs.statSync(sourcePath).size > maximumSvgBytes) fail(`${source.name}: SVG input exceeds the 1 MiB limit`);
        const wrap = validateSvg(fs.readFileSync(sourcePath, 'utf8'), source.name);
        for (const size of sizes) {
            const render = () => new Resvg(wrap(size), { font: { loadSystemFonts: false } }).render().asPng();
            const bytes = render();
            validatePng(bytes, size);
            if (!bytes.equals(render())) fail(`${source.name}: nondeterministic render at ${size}px`);
            rendered.push({ size, id: source.id, bytes });
        }
    }
    const manifest = { schemaVersion: 1, cursors: sources.map(({ id }) => ({ id, images: Object.fromEntries(sizes.map(size => [size, `./presentation/cursors/generated/${size}/${id}.png`])) })) };
    // Validation and rendering finish before output creation, replacement or stale cleanup.
    fs.mkdirSync(outputDir, { recursive: true });
    for (const size of sizes) fs.mkdirSync(path.join(outputDir, String(size)), { recursive: true });
    fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
    function replace(file, bytes) {
        const temporary = `${file}.tmp-${process.pid}`;
        rejectLinks(temporary);
        let created = false;
        try { fs.writeFileSync(temporary, bytes, { flag: 'wx' }); created = true; fs.renameSync(temporary, file); }
        finally { if (created && fs.existsSync(temporary)) fs.unlinkSync(temporary); }
    }
    for (const { size, id, bytes } of rendered) replace(path.join(outputDir, String(size), `${id}.png`), bytes);
    replace(manifestPath, JSON.stringify(manifest, null, 2) + '\n');
    for (const size of sizes) {
        for (const entry of fs.readdirSync(path.join(outputDir, String(size)), { withFileTypes: true })) {
            if (entry.isFile() && entry.name.endsWith('.png') && idPattern.test(entry.name.slice(0, -4)) && !ids.has(entry.name.slice(0, -4))) fs.unlinkSync(path.join(outputDir, String(size), entry.name));
        }
    }
    return manifest;
}

if (require.main === module) {
    try {
        const defaults = path.resolve(__dirname, '../ProjectDelve.Web/wwwroot/presentation/cursors');
        const options = { sourceDir: path.join(defaults, 'svg'), outputDir: path.join(defaults, 'generated'), manifestPath: path.join(defaults, 'catalog.json') };
        const args = process.argv.slice(2);
        const flags = { '--source': 'sourceDir', '--output': 'outputDir', '--manifest': 'manifestPath' };
        for (let i = 0; i < args.length; i += 2) {
            if (!flags[args[i]] || !args[i + 1] || args[i + 1].startsWith('--')) fail('Usage: node tools/generate-cursors.cjs [--source directory] [--output directory] [--manifest file]');
            options[flags[args[i]]] = args[i + 1];
        }
        const manifest = generate(options);
        console.log(`Generated ${manifest.cursors.length} cursors at ${sizes.join(', ')}px.`);
    } catch (error) { console.error(`Cursor generation failed: ${error.message}`); process.exitCode = 1; }
}

module.exports = { generate, validateSvg, validatePng, validateFilenames };
