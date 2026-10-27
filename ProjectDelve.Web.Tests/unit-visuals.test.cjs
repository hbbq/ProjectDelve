const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
const at=n=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',n)).href);
const sample=()=>({schemaVersion:1,models:[{id:'shared',approved:true,url:'./art/shared.glb',version:'v1'}],units:[{typeId:'small',targetHeight:1.2,maximumRadius:.38,model:'shared'},{typeId:'large',targetHeight:2.4,maximumRadius:.76,model:'shared'}]});
const canonical=[{unitTypeId:'small',cellSpan:1},{unitTypeId:'large',cellSpan:2}];
test('catalog rejects omissions, stale IDs, duplicates, invalid dimensions, schema and model references',async()=>{
  const {validateUnitVisualCatalog:v}=await at('unit-visuals.js');assert.equal(v(sample(),canonical).units.large.targetHeight,2.4);
  for(const mutate of [s=>s.units.pop(),s=>s.units.push({...s.units[0],typeId:'stale'}),s=>s.units.push(s.units[0]),s=>s.units[0].targetHeight=0,s=>s.units[0].targetHeight=Infinity,s=>s.units[0].maximumRadius=-1,s=>s.units[0].maximumRadius=.5,s=>s.units[0].model='missing',s=>s.schemaVersion=2,s=>s.models.push(s.models[0]),s=>s.models[0].approved=false,s=>s.models[0].url='https://other.invalid/test.glb',s=>s.models[0].maxTextureDimension=Infinity]){const s=sample();mutate(s);assert.throws(()=>v(s,canonical));}
  assert.throws(()=>v(sample(),[...canonical,{unitTypeId:'new-type',cellSpan:1}]),/Missing/);
});
test('catalog model resolution keeps authored dimensions and camera envelope public and load independent',async()=>{
  const {validateUnitVisualCatalog:v,resolveUnitModel:r,unitVisualEnvelope:e}=await at('unit-visuals.js'),c=v(sample(),canonical);
  assert.equal(r(c,'small',1).height,1.2);assert.equal(r(c,'large',2).radius,.76);assert.equal(r(c,'missing',1),null);assert.equal(e(c),2.55);
  const disk=JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json')));assert.ok(disk.units.length>0);assert.ok(disk.models.every(m=>m.url.startsWith('./art/models/')));
  assert.equal(r({units:{'zombie-type':{targetHeight:1.2,maximumRadius:.39}},models:{}},'zombie-type',1,{'zombie-type':{approved:true,url:'./__local/zombie.glb',version:'local',height:9,radius:9}}).height,1.2);
});
test('one shared source supports different Type scales without contaminating geometry, transforms or fallback',async()=>{
  const {createModelCache}=await at('three-models.js');let downloads=0,parses=0;
  const raw=fs.readFileSync(path.join(__dirname,'fixtures/miniatures/static-figure.glb')),bytes=raw.buffer.slice(raw.byteOffset,raw.byteOffset+raw.byteLength);
  const cache=createModelCache({fetchBytes:async()=>{downloads++;return bytes;},load:async b=>{parses++;const {GLTFLoader}=await at('vendor/three/0.180.0/examples/jsm/loaders/GLTFLoader.js');return new GLTFLoader().parseAsync(b,'');}});
  const small={approved:true,url:'./shared.glb',version:'v1',height:1.2,radius:.38},large={...small,cellSpan:2,height:2.4,radius:.76};
  const e=cache.request(small);assert.equal(cache.request(large),e);await e.promise;
  const a=cache.clone(e,small),b=cache.clone(e,large);assert.equal(downloads,1);assert.equal(parses,1);assert.equal(b.scale,2*a.scale);assert.equal(b.visualBounds.height,2.4);
  assert.equal(a.group.getObjectByName('body').geometry,b.group.getObjectByName('body').geometry);assert.equal(a.group.getObjectByName('body').material,b.group.getObjectByName('body').material);
  b.group.position.x=9;assert.equal(a.group.position.x,0);assert.equal(e.template.group.parent,null);assert.equal(e.template.group.scale.y,1);
  assert.equal(cache.clone(e,{...small,radius:.01}),null);assert.equal(e.state,'ready');assert.ok(cache.clone(e,small));assert.match(cache.inspect()[0].error,/proportions/);cache.dispose();
});
