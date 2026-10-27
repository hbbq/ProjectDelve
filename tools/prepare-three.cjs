// Reproduce checked-in browser modules from the integrity-locked npm package.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const root = path.resolve(__dirname, '..');
const version = require('../package.json').dependencies.three;
const source = path.join(root, 'node_modules/three');
const target = path.join(root, 'ProjectDelve.Web/wwwroot/vendor/three', version);
if (JSON.parse(fs.readFileSync(path.join(source, 'package.json'))).version !== version) throw Error('Three version mismatch');
const addons = ['examples/jsm/loaders/GLTFLoader.js', 'examples/jsm/utils/BufferGeometryUtils.js'];
const files = ['build/three.module.js', 'build/three.core.js', ...addons, 'LICENSE'];
const originals = Object.fromEntries(files.map(name => [name, fs.readFileSync(path.join(source, name))]));
// The pinned addons share the same local Three identity as application modules.
const contents = Object.fromEntries(files.map(name => [name, addons.includes(name)
  ? Buffer.from(originals[name].toString('utf8').replace(/from 'three';/g, "from '../../../build/three.module.js';")) : originals[name]]));
// Reject unexpected relative dependencies rather than shipping an incomplete graph.
for (const name of files.filter(f => f.endsWith('.js'))) {
  const code = contents[name].toString('utf8').replace(/\/\*[^]*?\*\//g, '').replace(/^\s*\/\/.*$/gm, '');
  for (const match of code.matchAll(/^(?:import|export)\s+(?:[^;]*?\s+from\s*)?['"]([^'"]+)['"]/gm)) {
    const dependency = path.posix.normalize(path.posix.join(path.posix.dirname(name), match[1]));
    if (!match[1].startsWith('.') || !files.includes(dependency)) throw Error(`Unregistered module dependency: ${match[1]}`);
  }
}
contents['package.json'] = Buffer.from(JSON.stringify({ private: true, type: 'module', version }) + '\n');
contents['provenance.json'] = Buffer.from(JSON.stringify({ package: 'three', version,
  source: `https://registry.npmjs.org/three/-/three-${version}.tgz`,
  integrity: require('../package-lock.json').packages['node_modules/three'].integrity,
  transformations: Object.fromEntries(addons.map(name => [name, "from 'three' -> from '../../../build/three.module.js'"])),
  sourceSha256: Object.fromEntries(files.map(name => [name, crypto.createHash('sha256').update(originals[name]).digest('hex')])),
  sha256: Object.fromEntries(files.map(name => [name, crypto.createHash('sha256').update(contents[name]).digest('hex')])) }, null, 2) + '\n');
for (const [name, bytes] of Object.entries(contents)) {
  const destination = path.join(target, name);
  if (process.argv.includes('--check')) {
    if (!fs.readFileSync(destination).equals(bytes)) throw Error(`Vendor mismatch: ${name}`);
  } else {
    fs.mkdirSync(path.dirname(destination), { recursive: true }); fs.writeFileSync(destination, bytes);
  }
}
console.log(`Three ${version}: complete local module graph ${process.argv.includes('--check') ? 'verified' : 'prepared'}.`);
