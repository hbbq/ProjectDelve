const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url'),crypto=require('node:crypto');
const root=path.resolve(__dirname,'..'),at=n=>import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot',n)).href),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const p=JSON.parse(read('assets/source/models/units/zombie/provenance.json'));
test('production Zombie has paid generation provenance and a real catalog registration',async()=>{
  assert.equal(p.generationModel,'H3.1');assert.equal(p.plan,'Pro');assert.equal(p.commerciallyCleared,true);assert.equal(p.redistributionRightsConfirmed,true);
  const c=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')),u=c.units.find(u=>u.typeId==='zombie-type'),asset=c.models.find(m=>m.id===u.model);
  assert.equal(asset.url,'./art/models/units/zombie/v1/zombie.glb');assert.equal(asset.approved,true);assert.equal(c.units.find(u=>u.typeId==='troll-type').model,'troll/v1');
  const {resolveUnitModel}=await at('unit-visuals.js');const compiled={units:Object.fromEntries(c.units.map(u=>[u.typeId,u])),models:Object.fromEntries(c.models.map(m=>[m.id,m]))};assert.equal(resolveUnitModel(compiled,'zombie-type',1).height,.75);assert.equal(resolveUnitModel(compiled,'missing-test-owned-type',1),null);
});
test('production GLB is reproducible, preserves source geometry/maps and fits approved dimensions',async()=>{
  const {inspect}=require('../tools/inspect-glb.cjs'),{prepare}=require('../tools/prepare-unit-model.cjs'),source=read(p.original.path),runtime=read(p.runtime.path);
  assert.equal(hash(source),p.original.sha256);assert.equal(hash(runtime),p.runtime.sha256);assert.deepEqual(runtime,prepare(source,p.original.sha256));
  const a=await inspect(path.join(root,p.original.path)),b=await inspect(path.join(root,p.runtime.path));assert.equal(b.triangles,9674);assert.equal(b.primitives,1);assert.equal(b.materials.length,1);assert.equal(b.finite,true);assert.deepEqual(b.images,a.images);assert.equal(b.images.length,3);assert.ok(b.images.every(i=>i.width===2048&&i.height===2048));assert.equal(b.bounds.min[1],0);assert.ok(b.normalization.maximumRadius<=.38);assert.equal(b.normalization.targetHeight,1.2);assert.equal(b.animations,0);assert.equal(b.skins,0);
  const {inspectGlb}=await at('three-models.js');const j=inspectGlb(runtime.buffer.slice(runtime.byteOffset,runtime.byteOffset+runtime.byteLength));assert.equal(j.nodes.length,1);assert.equal(hash(read(p.original.path)),p.original.sha256);
});
