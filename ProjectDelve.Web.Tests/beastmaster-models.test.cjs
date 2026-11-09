const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const dimensions={beastmaster:[.8,.30,.27],jackalope:[.42,.22,.20],'giant-toad':[.35,.36,.39],'dire-bear':[.64,.52,.43],raptor:[.60,.44,.37],'light-elemental':[.45,.18,.17]};
function views(bytes){const length=bytes.readUInt32LE(12),j=JSON.parse(bytes.subarray(20,20+length)),b=bytes.subarray(28+length);return {j,geometry:j.bufferViews.filter((_,i)=>!j.images.some(m=>m.bufferView===i)).map(v=>b.subarray(v.byteOffset??0,(v.byteOffset??0)+v.byteLength))};}

test('Beastmaster batch preserves original geometry, reviewed dimensions and reproducible approved exports',async()=>{
  const {prepare}=require('../tools/prepare-unit-model.cjs'),{inspect}=require('../tools/inspect-glb.cjs');
  const {validateUnitVisualCatalog,resolveUnitModel,miniatureBaseLayout}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/unit-visuals.js')));
  const disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'));
  const catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1})));
  for(const [slug,expected]of Object.entries(dimensions)){
    const p=JSON.parse(read(`assets/source/models/units/${slug}/provenance.json`)),u=catalog.units[slug+'-type'],d=resolveUnitModel(catalog,u.typeId,1);
    const source=read(p.original.path),runtime=read(p.runtime.path);
    assert.deepEqual([u.targetHeight,u.maximumRadius,u.baseRadius],expected,slug);assert.equal(u.model,slug+'/v1');
    assert.equal(catalog.cellSpans[u.typeId],1);assert.equal(resolveUnitModel(catalog,u.typeId,2),null);
    assert.equal(d.approved,true);assert.equal(d.maxTriangles,12000);assert.equal(d.maxTextureDimension,1024);
    assert.equal(p.status,'approved-production');assert.equal(p.plan,'Pro');assert.ok(p.planEvidence);
    assert.equal(p.commerciallyCleared,true);assert.equal(p.redistributionRightsConfirmed,true);
    for(const ref of [p.projectPortraitReference,...p.projectConceptReferences])assert.ok(read(ref).length,ref);
    assert.equal(hash(source),p.original.sha256);assert.equal(hash(runtime),p.runtime.sha256);
    assert.equal(p.original.preservedUnchanged,true);assert.equal(p.import.sha256BeforeMove,p.original.sha256);assert.equal(p.import.sha256AfterMove,p.original.sha256);
    assert.equal(source.length,p.original.bytes);assert.equal(runtime.length,p.runtime.bytes);
    assert.equal(p.preparation.textureLimit,1024);assert.deepEqual(prepare(source,p.original.sha256,p.preparation),runtime);
    assert.equal(p.validation.source.errors,0);assert.equal(p.validation.runtime.errors,0);
    const a=views(source),b=views(runtime);assert.deepEqual(a.geometry,b.geometry);
    for(const key of ['accessors','materials','meshes','textures','samplers'])assert.deepEqual(a.j[key],b.j[key]);
    assert.deepEqual(b.j.nodes.slice(0,a.j.nodes.length),a.j.nodes);
    if(p.preparation.rotationYDegrees){assert.equal(b.j.nodes.length,a.j.nodes.length+1);assert.deepEqual(b.j.nodes.at(-1).children,a.j.scenes[0].nodes);}
    else assert.deepEqual(a.j.nodes,b.j.nodes);
    const info=await inspect(path.join(root,p.runtime.path),u.targetHeight);
    assert.equal(info.triangles,p.runtimeInspection.triangles);assert.equal(info.triangles,p.inspection.triangles);assert.ok(info.triangles<=d.maxTriangles);
    assert.equal(info.primitives,1);assert.equal(info.materials.length,1);assert.equal(info.finite,true);
    assert.ok(Math.abs(info.normalization.dimensions[1]-u.targetHeight)<1e-9);assert.ok(info.normalization.maximumRadius<=u.maximumRadius);
    assert.equal(info.images.length,3);assert.ok(info.images.every(i=>i.width===1024&&i.height===1024));
    assert.deepEqual(info.externalResources,[]);assert.deepEqual(info.extensionsUsed,[]);
    assert.deepEqual(info.normalization.contact,p.runtimeInspection.normalization.contact);
    assert.deepEqual(info.normalization.contact,p.compatibility.contact);
    assert.deepEqual(miniatureBaseLayout({width:1,baseRadius:u.baseRadius,contact:info.normalization.contact}),p.compatibility.base);
    assert.equal(p.compatibility.logicalFootprint,'unchanged');assert.equal(p.compatibility.targetHeight,u.targetHeight);
    assert.equal(p.compatibility.maximumRadius,u.maximumRadius);assert.equal(p.compatibility.baseRadius,u.baseRadius);
    assert.equal(fs.existsSync(path.join(root,`assets/import/models/${slug}.glb`)),false);
  }
});

test('only Light Elemental declares the gentle reusable visual light',async()=>{
  const disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'));
  assert.deepEqual(disk.units.filter(u=>u.light).map(u=>[u.typeId,u.light]),[
    ['light-elemental-type',{color:[1,.88,.60],strength:1.2,radius:2.25}]
  ]);
});
