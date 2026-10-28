const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const expected=require('./fixtures/miniatures/remaining-batch.json');
function views(bytes){const length=bytes.readUInt32LE(12),j=JSON.parse(bytes.subarray(20,20+length)),b=bytes.subarray(28+length);return {j,geometry:j.bufferViews.filter((_,i)=>!j.images.some(m=>m.bufferView===i)).map(v=>b.subarray(v.byteOffset??0,(v.byteOffset??0)+v.byteLength))};}
test('remaining 19 production miniatures preserve sources, geometry, dimensions and reproducible 1024 maps',async()=>{
 const {prepare}=require('../tools/prepare-unit-model.cjs'),{inspect}=require('../tools/inspect-glb.cjs');
 const {validateUnitVisualCatalog,resolveUnitModel}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/unit-visuals.js')));
 const disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')),catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1})));
 assert.equal(disk.models.length,26);
 for(const [slug,values] of Object.entries(expected)){
  const p=JSON.parse(read(`assets/source/models/units/${slug}/provenance.json`)),u=catalog.units[slug+'-type'],d=resolveUnitModel(catalog,u.typeId,1),source=read(p.original.path),runtime=read(p.runtime.path);
  assert.deepEqual([u.targetHeight,u.maximumRadius,u.baseRadius],values.dimensions,slug);assert.equal(u.model,slug+'/v1');assert.equal(d.approved,true);assert.equal(d.maxTextureDimension,1024);assert.equal(resolveUnitModel(catalog,u.typeId,2),null);
  assert.equal(p.status,'approved-production');assert.equal(p.plan,'Pro');assert.ok(p.planEvidence);assert.equal(p.commerciallyCleared,true);assert.equal(p.redistributionRightsConfirmed,true);
  for(const ref of [p.projectPortraitReference,...p.projectConceptReferences])assert.ok(read(ref).length);
  assert.equal(hash(source),p.original.sha256);assert.equal(hash(runtime),p.runtime.sha256);assert.equal(p.import.sha256BeforeMove,p.original.sha256);assert.equal(p.import.sha256AfterMove,p.original.sha256);
  assert.deepEqual(prepare(source,p.original.sha256,p.preparation),runtime);assert.equal(source.length,p.original.bytes);assert.equal(runtime.length,p.runtime.bytes);assert.equal(p.validation.source.errors,0);assert.equal(p.validation.runtime.errors,0);
  const a=views(source),b=views(runtime);assert.deepEqual(a.geometry,b.geometry);assert.deepEqual(a.j.materials,b.j.materials);assert.deepEqual(a.j.accessors,b.j.accessors);assert.deepEqual(a.j.nodes,b.j.nodes);
  const info=await inspect(path.join(root,p.runtime.path),u.targetHeight);assert.equal(info.triangles,values.triangles);assert.equal(info.primitives,1);assert.equal(info.materials.length,1);assert.equal(info.finite,true);assert.ok(info.normalization.maximumRadius<u.maximumRadius);assert.ok(info.images.every(i=>i.width===1024&&i.height===1024));assert.equal(info.images.length,3);assert.deepEqual(info.externalResources,[]);assert.deepEqual(info.extensionsUsed,[]);
 }
});
test('texture export rejects invalid limits before invoking offline dependencies',()=>{
 const {prepare}=require('../tools/prepare-unit-model.cjs'),p=JSON.parse(read('assets/source/models/units/bomb-imp/provenance.json')),source=read(p.original.path);
 assert.throws(()=>prepare(source,p.original.sha256,{textureLimit:1000}),/Texture limit/);assert.throws(()=>prepare(source,'bad',{textureLimit:1024}),/Source hash/);
});
