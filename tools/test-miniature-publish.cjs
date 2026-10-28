// Run after the cached Debug build. Exercises actual Web SDK publish inclusion.
const {spawnSync}=require('node:child_process'),path=require('node:path'),fs=require('node:fs'),assert=require('node:assert/strict');
const root=path.resolve(__dirname,'..'),out=path.join(root,'.playwright-cli/publish-miniatures');
const run=spawnSync('dotnet',['publish','ProjectDelve.Web/ProjectDelve.Web.csproj','--no-build','--no-restore','-c',process.argv.includes('--release')?'Release':'Debug','--artifacts-path','.playwright-cli','--output',out,
  '-p:CustomAfterMicrosoftCommonTargets='+path.join(root,'ProjectDelve.Web.Tests/fixtures/miniatures/publish-fixture.targets')],{cwd:root,encoding:'utf8',timeout:120000});
if(run.status!==0)throw Error(run.stdout+run.stderr);
assert.deepEqual(fs.readFileSync(path.join(out,'wwwroot/__qa/miniatures/static-figure.glb')),fs.readFileSync(path.join(root,'ProjectDelve.Web.Tests/fixtures/miniatures/static-figure.glb')));
for(const file of ['three-models.js','unit-facing.js','unit-visuals.js','art/models/unit-visuals.json','vendor/three/0.180.0/examples/jsm/loaders/GLTFLoader.js','vendor/three/0.180.0/examples/jsm/utils/BufferGeometryUtils.js'])assert.ok(fs.existsSync(path.join(out,'wwwroot',file)),file);
assert.equal(fs.existsSync(path.join(out,'wwwroot/__qa/miniatures/registration.js')),false);
const catalog=JSON.parse(fs.readFileSync(path.join(out,'wwwroot/art/models/unit-visuals.json'))),runtimes=catalog.models.map(m=>'wwwroot/'+m.url.slice(2));
assert.deepEqual(catalog,JSON.parse(fs.readFileSync(path.join(root,'ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'))));
for(const runtime of runtimes)assert.deepEqual(fs.readFileSync(path.join(out,runtime)),fs.readFileSync(path.join(root,'ProjectDelve.Web',runtime)));
assert.equal(fs.existsSync(path.join(out,'assets/source')),false);
console.log('Actual publish: all approved production GLBs, catalog, GLB fixture and loader included; source and QA registration excluded.');
// Also exercise ordinary shipping output without the test-only Content target.
const ordinary=out+'-ordinary',shipping=spawnSync('dotnet',['publish','ProjectDelve.Web/ProjectDelve.Web.csproj','--no-build','--no-restore','-c',process.argv.includes('--release')?'Release':'Debug','--artifacts-path','.playwright-cli','--output',ordinary],{cwd:root,encoding:'utf8',timeout:120000});
if(shipping.status!==0)throw Error(shipping.stdout+shipping.stderr);
for(const runtime of runtimes)assert.deepEqual(fs.readFileSync(path.join(ordinary,runtime)),fs.readFileSync(path.join(root,'ProjectDelve.Web',runtime)));
const glbs=[];function scan(dir){for(const e of fs.readdirSync(dir,{withFileTypes:true})){const f=path.join(dir,e.name);if(e.isDirectory())scan(f);else if(e.name.endsWith('.glb'))glbs.push(path.relative(ordinary,f).replaceAll('\\','/'));}}scan(ordinary);
assert.deepEqual(glbs.sort(),runtimes.sort());assert.equal(fs.existsSync(path.join(ordinary,'assets/source')),false);
console.log(`Ordinary publish: exactly ${runtimes.length} production GLBs; no source or fixture copies.`);
