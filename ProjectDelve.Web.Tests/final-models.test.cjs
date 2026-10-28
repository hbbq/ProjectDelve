const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const expected=require('./fixtures/miniatures/final-batch.json');
function views(bytes){const length=bytes.readUInt32LE(12),j=JSON.parse(bytes.subarray(20,20+length)),b=bytes.subarray(28+length);return {j,geometry:j.bufferViews.filter((_,i)=>!j.images.some(m=>m.bufferView===i)).map(v=>b.subarray(v.byteOffset??0,(v.byteOffset??0)+v.byteLength))};}
test('final five preserve exact originals, geometry and reviewed dimensions with reproducible exports',async()=>{
 const {prepare}=require('../tools/prepare-unit-model.cjs'),{inspect}=require('../tools/inspect-glb.cjs');
 const {validateUnitVisualCatalog,resolveUnitModel}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/unit-visuals.js')));
 const disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')),catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1})));
 assert.ok(disk.units.every(u=>u.model));
 for(const [slug,e] of Object.entries(expected)){
  const p=JSON.parse(read(`assets/source/models/units/${slug}/provenance.json`)),u=catalog.units[slug+'-type'],d=resolveUnitModel(catalog,u.typeId,1),source=read(p.original.path),runtime=read(p.runtime.path);
  assert.deepEqual([u.targetHeight,u.maximumRadius,u.baseRadius],e.dimensions);assert.equal(d.maxTextureDimension,1024);assert.equal(resolveUnitModel(catalog,u.typeId,2),null);
  assert.equal(p.status,'approved-production');assert.equal(p.plan,'Pro');assert.ok(p.planEvidence);assert.equal(p.commerciallyCleared,true);assert.equal(p.redistributionRightsConfirmed,true);
  for(const ref of [p.projectPortraitReference,...p.projectConceptReferences])assert.ok(read(ref).length);
  assert.equal(hash(source),e.sourceSha256);assert.equal(hash(runtime),e.runtimeSha256);assert.equal(p.original.sha256,e.sourceSha256);assert.equal(p.runtime.sha256,e.runtimeSha256);
  assert.equal(p.import.sha256BeforeMove,e.sourceSha256);assert.equal(p.import.sha256AfterMove,e.sourceSha256);assert.equal(p.original.bytes,source.length);assert.equal(p.runtime.bytes,runtime.length);
  assert.deepEqual(prepare(source,e.sourceSha256,p.preparation),runtime);assert.equal(p.validation.source.errors,0);assert.equal(p.validation.runtime.errors,0);
  const a=views(source),b=views(runtime);assert.deepEqual(a.geometry,b.geometry);for(const key of ['accessors','materials','meshes','textures','samplers'])assert.deepEqual(a.j[key],b.j[key]);
  assert.deepEqual(b.j.nodes.slice(0,a.j.nodes.length),a.j.nodes);
  if(e.rotationYDegrees){assert.equal(b.j.nodes.length,a.j.nodes.length+1);const wrapper=b.j.nodes.at(-1);assert.deepEqual(wrapper.children,a.j.scenes[0].nodes);assert.ok(Math.abs(wrapper.rotation[1]-Math.SQRT1_2)<1e-12);}
  else assert.deepEqual(a.j.nodes,b.j.nodes);
  const info=await inspect(path.join(root,p.runtime.path),u.targetHeight);assert.equal(info.triangles,e.triangles);assert.equal(info.primitives,1);assert.equal(info.materials.length,1);assert.equal(info.finite,true);assert.ok(info.normalization.maximumRadius<=u.maximumRadius);assert.ok(info.images.every(i=>i.width===1024&&i.height===1024));assert.equal(info.images.length,3);assert.deepEqual(info.externalResources,[]);assert.deepEqual(info.extensionsUsed,[]);
  assert.equal(fs.existsSync(path.join(root,`assets/import/models/${slug}.glb`)),false);
 }
});
test('previous 26 source, runtime and provenance files remain byte-identical',()=>{
 for(const [file,sha256]of Object.entries(require('./fixtures/miniatures/prior-model-hashes.json')))assert.equal(hash(read(file)),sha256,file);
});
test('Red Dragon keeps its original mesh and height with a reviewed 2x2 production descriptor',async()=>{
 const p=JSON.parse(read('assets/source/models/units/red-dragon/provenance.json')),source=read(p.original.path),runtime=read(p.runtime.path);
 assert.equal(hash(source),'2f180423d745036608c27224bf9e3aacae34a8a036a64a4e441cbf39a2237223');
 assert.equal(hash(runtime),'1a5c4bba83ed707170952025e75007e30574203d93e9410d6f4e75da855f9f12');
 const disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')),u=disk.units.find(u=>u.typeId==='red-dragon-type');
 assert.deepEqual(u,{typeId:'red-dragon-type',targetHeight:1.45,maximumRadius:1.20,baseRadius:.90,model:'red-dragon/v1'});
 assert.deepEqual(require('../tools/prepare-unit-model.cjs').prepare(source,p.original.sha256,p.preparation),runtime);
 const a=views(source),b=views(runtime);assert.deepEqual(a.geometry,b.geometry);for(const key of ['accessors','materials','meshes','textures','samplers'])assert.deepEqual(a.j[key],b.j[key]);
 assert.deepEqual(b.j.nodes.slice(0,a.j.nodes.length),a.j.nodes);assert.ok(Math.abs(b.j.nodes.at(-1).rotation[1]-Math.SQRT1_2)<1e-12);
 const info=await require('../tools/inspect-glb.cjs').inspect(p.runtime.path,1.45);assert.ok(info.normalization.maximumRadius>1.19&&info.normalization.maximumRadius<1.20);assert.equal(info.normalization.dimensions[1],1.45);assert.ok(info.images.every(i=>i.width===1024&&i.height===1024));
 assert.equal(p.compatibility.base.maximumRadius,.90);assert.equal(p.compatibility.base.radius,.90);assert.equal(p.compatibility.base.fitsContact,false);
 assert.deepEqual(await require('../tools/import-unit-models.cjs').importModels(root,{only:['red-dragon']}),[]);
});

test('rigid orientation export rejects unsupported angles and preserves radial extent and height',async()=>{
 const {prepare}=require('../tools/prepare-unit-model.cjs'),e=expected['royal-knight'],source=read('assets/source/models/units/royal-knight/original.glb');assert.throws(()=>prepare(source,e.sourceSha256,{rotationYDegrees:45}),/Orientation rotation/);
 // The scene wrapper must preserve the exact radial extent and height.
 const {inspect}=require('../tools/inspect-glb.cjs'),a=await inspect('source',.85,source),b=await inspect('rotated',.85,prepare(source,e.sourceSha256,{rotationYDegrees:90}));
 assert.ok(Math.abs(a.normalization.maximumRadius-b.normalization.maximumRadius)<1e-12);assert.equal(a.normalization.dimensions[1],b.normalization.dimensions[1]);assert.ok(Math.abs(a.normalization.dimensions[0]-b.normalization.dimensions[2])<1e-12);
});
