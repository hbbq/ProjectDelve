const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const p=JSON.parse(read('assets/source/models/units/troll/provenance.json'));
test('Troll has paid provenance without invented model version, real references, and ordinary production registration',async()=>{
  assert.equal(p.status,'approved-production');assert.equal(p.generator,'Tripo');assert.equal(p.generationModel,'unknown');assert.equal(p.plan,'Pro');assert.equal(p.commerciallyCleared,true);assert.equal(p.redistributionRightsConfirmed,true);
  for(const file of [p.projectPortraitReference,...p.projectConceptReferences])assert.ok(read(file).length);
  const c=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')),u=c.units.find(u=>u.typeId==='troll-type'),m=c.models.find(m=>m.id===u.model);
  assert.equal(u.targetHeight,1);assert.equal(u.maximumRadius,.856);assert.equal(m.approved,true);assert.equal(m.url,'./art/models/units/troll/v1/troll.glb');assert.equal(m.version,'tripo-v1');
  const {resolveUnitModel}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/unit-visuals.js')));
  const compiled={units:Object.fromEntries(c.units.map(u=>[u.typeId,u])),models:Object.fromEntries(c.models.map(m=>[m.id,m])),cellSpans:{'troll-type':1}};
  assert.equal(resolveUnitModel(compiled,'troll-type',1).height,1);assert.equal(resolveUnitModel(compiled,'troll-type',2),null);assert.equal(resolveUnitModel(compiled,'grunt-type',1),null);
});
test('Troll preparation is reproducible, preserves geometry and image bytes, and measures its actual overhang',async()=>{
  const {inspect}=require('../tools/inspect-glb.cjs'),{prepare}=require('../tools/prepare-unit-model.cjs'),source=read(p.original.path),runtime=read(p.runtime.path);
  assert.equal(source.length,p.original.bytes);assert.equal(hash(source),p.original.sha256);assert.equal(runtime.length,p.runtime.bytes);assert.equal(hash(runtime),p.runtime.sha256);assert.deepEqual(runtime,prepare(source,p.original.sha256));
  const a=await inspect(path.join(root,p.original.path),1.9),b=await inspect(path.join(root,p.runtime.path),1.9);
  assert.equal(b.triangles,9504);assert.equal(b.meshes,1);assert.equal(b.primitives,1);assert.equal(b.materials.length,1);assert.equal(b.finite,true);assert.deepEqual(b.images,a.images);assert.equal(b.images.length,3);assert.ok(b.images.every(i=>i.width===2048&&i.height===2048));
  assert.deepEqual(b.bounds,a.bounds);assert.equal(b.bounds.min[1],0);assert.deepEqual(b.externalResources,[]);assert.deepEqual(b.extensionsUsed,[]);assert.equal(b.animations,0);assert.equal(b.skins,0);
  assert.ok(b.normalization.maximumRadius>.85&&b.normalization.maximumRadius<=.856);assert.equal(b.normalization.targetHeight,1.9);assert.ok(b.normalization.dimensions[0]>1.66);
  const {inspectGlb}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/three-models.js')));assert.equal(inspectGlb(runtime.buffer.slice(runtime.byteOffset,runtime.byteOffset+runtime.byteLength)).nodes.length,1);
  assert.equal(hash(read(p.original.path)),p.original.sha256);
});
