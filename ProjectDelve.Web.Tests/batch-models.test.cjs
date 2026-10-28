const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),crypto=require('node:crypto'),{pathToFileURL}=require('node:url');
const root=path.resolve(__dirname,'..'),read=p=>fs.readFileSync(path.join(root,p)),hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const slugs=['goblin','barbarian','cleric','hobby-knight','worker'];
const dimensions={goblin:[.55,.32,.27],barbarian:[.8,.42,.34],cleric:[.78,.39,.32],'hobby-knight':[.68,.4,.31],worker:[.63,.34,.28]};
const triangles={goblin:9852,barbarian:9456,cleric:9854,'hobby-knight':9849,worker:9756};
test('batch production catalog preserves authored dimensions, paid provenance, hashes and exact preparation',async()=>{
 const {inspect}=require('../tools/inspect-glb.cjs'),{prepare}=require('../tools/prepare-unit-model.cjs'),{inspectGlb}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/three-models.js')));
 const {validateUnitVisualCatalog,resolveUnitModel,miniatureBaseLayout}=await import(pathToFileURL(path.join(root,'ProjectDelve.Web/wwwroot/unit-visuals.js'))),disk=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'));
 const catalog=validateUnitVisualCatalog(disk,disk.units.map(u=>({unitTypeId:u.typeId,cellSpan:u.typeId==='red-dragon-type'?2:1})));
 for(const slug of slugs){
  const p=JSON.parse(read(`assets/source/models/units/${slug}/provenance.json`)),u=catalog.units[slug+'-type'],d=resolveUnitModel(catalog,u.typeId,1);
  assert.deepEqual([u.targetHeight,u.maximumRadius,u.baseRadius],dimensions[slug]);assert.equal(u.model,slug+'/v1');assert.equal(d.approved,true);assert.equal(d.url,`./art/models/units/${slug}/v1/${slug}.glb`);assert.equal(resolveUnitModel(catalog,u.typeId,2),null);
  assert.equal(p.status,'approved-production');assert.equal(p.generator,'Tripo');assert.equal(p.plan,'Pro');assert.equal(p.generationModel,'unknown');assert.ok(p.planEvidence);assert.equal(p.commerciallyCleared,true);assert.equal(p.redistributionRightsConfirmed,true);
  for(const ref of [p.projectPortraitReference,...p.projectConceptReferences])assert.ok(read(ref).length);
  const source=read(p.original.path),runtime=read(p.runtime.path);assert.equal(hash(source),p.original.sha256);assert.equal(hash(runtime),p.runtime.sha256);assert.equal(p.import.sha256BeforeMove,p.original.sha256);assert.equal(p.import.sha256AfterMove,p.original.sha256);assert.deepEqual(prepare(source,p.original.sha256),runtime);
  assert.equal(source.length,p.original.bytes);assert.equal(runtime.length,p.runtime.bytes);assert.equal(p.validation.source.errors,0);assert.equal(p.validation.runtime.errors,0);
  const a=await inspect(path.join(root,p.original.path),u.targetHeight),b=await inspect(path.join(root,p.runtime.path),u.targetHeight);
  assert.equal(b.triangles,triangles[slug]);assert.equal(b.meshes,1);assert.equal(b.primitives,1);assert.equal(b.materials.length,1);assert.equal(b.images.length,3);assert.ok(b.images.every(i=>i.width===2048&&i.height===2048));assert.deepEqual(a.images,b.images);assert.deepEqual(a.bounds,b.bounds);
  assert.equal(b.bounds.min[1],0);assert.equal(b.finite,true);assert.equal(b.hierarchy[0].determinant,1);assert.equal(b.animations,0);assert.equal(b.skins,0);assert.deepEqual(b.externalResources,[]);assert.deepEqual(b.extensionsUsed,[]);assert.ok(b.normalization.maximumRadius<u.maximumRadius);
  assert.ok(miniatureBaseLayout({baseRadius:u.baseRadius,contact:b.normalization.contact}).fitsContact);assert.equal(inspectGlb(runtime.buffer.slice(runtime.byteOffset,runtime.byteOffset+runtime.byteLength)).nodes.length,1);
 }
 for(const u of disk.units.filter(u=>!slugs.concat(['zombie','troll']).includes(u.typeId.slice(0,-5))))assert.equal(resolveUnitModel(catalog,u.typeId,1),null);
});
test('manual inbox is idle without GLBs; independent bad imports never move or overwrite files',async()=>{
 const {importModels}=require('../tools/import-unit-models.cjs'),tmp=fs.mkdtempSync(path.join(root,'.playwright-cli/import-test-'));
 try{
  fs.mkdirSync(path.join(tmp,'assets/import/models'),{recursive:true});fs.mkdirSync(path.join(tmp,'ProjectDelve.Web/wwwroot/art/models'),{recursive:true});
  const catalog=JSON.parse(read('ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')),file=path.join(tmp,'ProjectDelve.Web/wwwroot/art/models/unit-visuals.json');fs.writeFileSync(file,JSON.stringify(catalog));const original=fs.readFileSync(file),mtime=fs.statSync(file).mtimeMs;
  assert.deepEqual(await importModels(tmp,{apply:true}),[]);assert.equal(fs.statSync(file).mtimeMs,mtime);
  for(const slug of ['unrelated','goblin'])fs.writeFileSync(path.join(tmp,'assets/import/models',slug+'.glb'),read('assets/source/models/units/goblin/original.glb'));
  fs.writeFileSync(path.join(tmp,'assets/import/models/cleric.glb'),'bad GLB');
  const results=await importModels(tmp,{apply:true});assert.equal(results.length,3);assert.ok(results.every(r=>r.state==='deferred'));assert.match(results.find(r=>r.file==='goblin.glb').error,/Existing/);assert.match(results.find(r=>r.file==='unrelated.glb').error,/exact canonical/);
  assert.deepEqual(fs.readFileSync(file),original);for(const slug of ['unrelated','goblin','cleric'])assert.ok(fs.existsSync(path.join(tmp,'assets/import/models',slug+'.glb')));
 }finally{fs.rmSync(tmp,{recursive:true,force:true});}
});
test('reviewed inbox import preserves exact bytes, registers once and does no work on a repeat',async()=>{
 const {importModels}=require('../tools/import-unit-models.cjs'),tmp=fs.mkdtempSync(path.join(root,'.playwright-cli/import-approved-'));
 try{
  const base='assets/source/models/units/goblin',catalogPath='ProjectDelve.Web/wwwroot/art/models/unit-visuals.json',p=JSON.parse(read(base+'/provenance.json')),catalog=JSON.parse(read(catalogPath));
  p.status='reviewed-import';catalog.models=catalog.models.filter(m=>m.id!=='goblin/v1');delete catalog.units.find(u=>u.typeId==='goblin-type').model;
  for(const dir of ['assets/import/models',base,'ProjectDelve.Web/wwwroot/art/models'])fs.mkdirSync(path.join(tmp,dir),{recursive:true});
  const catalogText=JSON.stringify(catalog,null,2).replace('"targetHeight": 0.7,','"targetHeight": 0.700,')+'\n';
  fs.writeFileSync(path.join(tmp,catalogPath),catalogText);fs.writeFileSync(path.join(tmp,base,'provenance.json'),JSON.stringify(p));fs.writeFileSync(path.join(tmp,'assets/import/models/goblin.glb'),read(p.original.path));
  for(const ref of [p.projectPortraitReference,...p.projectConceptReferences]){fs.mkdirSync(path.dirname(path.join(tmp,ref)),{recursive:true});fs.writeFileSync(path.join(tmp,ref),'test-owned reference marker');}
  const scan=await importModels(tmp);assert.equal(scan[0].state,'compatible-needs-review');assert.equal(fs.existsSync(path.join(tmp,p.original.path)),false);
  const imported=await importModels(tmp,{apply:true});assert.equal(imported[0].state,'imported',imported[0].error);assert.equal(fs.existsSync(path.join(tmp,'assets/import/models/goblin.glb')),false);assert.deepEqual(fs.readFileSync(path.join(tmp,p.original.path)),read(p.original.path));assert.deepEqual(fs.readFileSync(path.join(tmp,p.runtime.path)),read(p.runtime.path));assert.ok(fs.readFileSync(path.join(tmp,catalogPath),'utf8').includes('"targetHeight": 0.700,'));
  const files=[catalogPath,p.original.path,p.runtime.path,base+'/provenance.json'],before=files.map(f=>[hash(fs.readFileSync(path.join(tmp,f))),fs.statSync(path.join(tmp,f)).mtimeMs]);
  fs.writeFileSync(path.join(tmp,'assets/import/models/unrelated.glb'),'unselected invalid incoming file');
  assert.deepEqual(await importModels(tmp,{apply:true,only:['goblin']}),[]);assert.equal(fs.readFileSync(path.join(tmp,'assets/import/models/unrelated.glb'),'utf8'),'unselected invalid incoming file');assert.deepEqual(files.map(f=>[hash(fs.readFileSync(path.join(tmp,f))),fs.statSync(path.join(tmp,f)).mtimeMs]),before);
  assert.equal(JSON.parse(fs.readFileSync(path.join(tmp,catalogPath))).models.filter(m=>m.id==='goblin/v1').length,1);
 }finally{fs.rmSync(tmp,{recursive:true,force:true});}
});
