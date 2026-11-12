const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');

test('Demolition Imp model uses its published identity and preserves the authoritative source',async()=>{
  const {prepare}=require('../tools/prepare-unit-model.cjs'),{inspect}=require('../tools/inspect-glb.cjs');
  const {validateUnitVisualCatalog,resolveUnitModel,miniatureBaseLayout}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/unit-visuals.js')));
  const disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'));
  const published=JSON.parse(read('ProjectDelve.Web/Content/Canonical/workspace-publication.bundle')).package.definitions.unitTypes.find(u=>u.id==='demolition-imp');
  assert.ok(published);assert.equal(published.footprint,'OneByOne');
  const catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1})));
  const unit=catalog.units['demolition-imp'],model=resolveUnitModel(catalog,'demolition-imp',1),bomb=catalog.units['bomb-imp-type'];
  assert.equal(unit.model,'demolition-imp/v1');assert.equal(model.approved,true);assert.equal(resolveUnitModel(catalog,'demolition-imp',2),null);
  assert.deepEqual([unit.targetHeight,unit.maximumRadius,unit.baseRadius],[bomb.targetHeight,bomb.maximumRadius,bomb.baseRadius]);
  const p=JSON.parse(read('assets/source/models/units/demolition-imp/provenance.json')),source=read(p.original.path),runtime=read(p.runtime.path);
  assert.equal(p.original.path,'assets/source/models/units/demolition-imp/original.glb');
  assert.equal(hash(source),'67ded7de8e91bea87f495580ed64a5c0108833f04360aa86be678052e4f8fa55');
  assert.equal(hash(source),p.original.sha256);assert.equal(hash(runtime),p.runtime.sha256);assert.equal(p.original.preservedUnchanged,true);
  assert.equal(source.length,p.original.bytes);assert.equal(runtime.length,p.runtime.bytes);
  assert.deepEqual(prepare(source,p.original.sha256,p.preparation),runtime);
  require('./fixtures/miniatures/assert-texture-preparation.cjs')(source,runtime);
  const sourceInfo=await inspect(path.join(root,p.original.path),unit.targetHeight),info=await inspect(path.join(root,p.runtime.path),unit.targetHeight);
  assert.equal(info.triangles,9856);assert.equal(info.primitives,1);assert.equal(info.materials.length,1);assert.equal(info.finite,true);
  assert.deepEqual(info.bounds,sourceInfo.bounds);assert.deepEqual(info.materials,sourceInfo.materials);assert.deepEqual(info.hierarchy,sourceInfo.hierarchy);
  assert.equal(info.bounds.min[1],0);assert.equal(info.normalization.dimensions[1],unit.targetHeight);assert.ok(info.normalization.maximumRadius<unit.maximumRadius);
  assert.ok(info.images.every(i=>i.width===1024&&i.height===1024));assert.equal(info.images.length,3);
  assert.deepEqual(info.externalResources,[]);assert.deepEqual(info.extensionsUsed,[]);assert.equal(info.animations,0);assert.equal(info.skins,0);
  assert.deepEqual(info.normalization.contact,p.compatibility.contact);
  assert.deepEqual(miniatureBaseLayout({width:1,baseRadius:unit.baseRadius,contact:info.normalization.contact}),p.compatibility.base);assert.equal(p.compatibility.base.fitsContact,true);
});

test('Demolition Imp validation records retain the concrete tangent warning and distinguish unknown rights',()=>{
  const p=JSON.parse(read('assets/source/models/units/demolition-imp/provenance.json'));
  for(const name of ['source','runtime']){
    const report=JSON.parse(read(p.validation[name+'Report']));assert.equal(report.issues.numErrors,0);assert.equal(report.issues.numWarnings,1);
    assert.ok(report.issues.messages.some(m=>m.code==='MESH_PRIMITIVE_GENERATED_TANGENT_SPACE'));
  }
  assert.equal(p.plan,'unknown');assert.equal(p.commerciallyCleared,null);assert.equal(p.redistributionRightsConfirmed,null);
});
