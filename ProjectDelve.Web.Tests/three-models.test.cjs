const {test}=require('node:test'),assert=require('node:assert/strict'),fs=require('node:fs'),path=require('node:path'),{pathToFileURL}=require('node:url');
const at=n=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',n)).href);
const fixturePath=path.join(__dirname,'fixtures/miniatures/static-figure.glb');
const bytes=()=>{const b=fs.readFileSync(fixturePath);return b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength);};
const descriptor={approved:true,url:'./art/test-owned.glb',version:'fixture-v1'};
async function parse(){const {GLTFLoader}=await at('vendor/three/0.180.0/examples/jsm/loaders/GLTFLoader.js');return new GLTFLoader().parseAsync(bytes(),'');}
test('vendor loader/utility graph is local, integrity checked and shares the application Three identity',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),gltf=await parse();assert.ok(gltf.scene instanceof T.Group);
  const base=path.resolve(__dirname,'../ProjectDelve.Web/wwwroot/vendor/three/0.180.0'),manifest=JSON.parse(fs.readFileSync(path.join(base,'provenance.json')));
  for(const [file,hash]of Object.entries(manifest.sha256))assert.equal(require('node:crypto').createHash('sha256').update(fs.readFileSync(path.join(base,file))).digest('hex'),hash);
  for(const file of ['loaders/GLTFLoader.js','utils/BufferGeometryUtils.js']){const name='examples/jsm/'+file;assert.ok(manifest.sourceSha256[name]);assert.ok(manifest.transformations[name]);assert.ok(fs.readFileSync(path.join(base,name),'utf8').includes("from '../../../build/three.module.js'"));}
});
test('test-owned GLB is reproducible and production registry contains no placeholder Zombie',async()=>{
  assert.deepEqual(fs.readFileSync(fixturePath),await require('../tools/prepare-miniature-fixture.cjs').fixture());
  const textured=fs.readFileSync(path.join(__dirname,'fixtures/miniatures/textured-figure.glb'));assert.deepEqual(textured,await require('../tools/prepare-miniature-fixture.cjs').fixture(true));
  const json=(await at('three-models.js')).inspectGlb(textured.buffer.slice(textured.byteOffset,textured.byteOffset+textured.byteLength));assert.equal(json.images[0].mimeType,'image/png');assert.equal(json.materials[0].pbrMetallicRoughness.baseColorTexture.index,0);
  assert.ok(JSON.parse(fs.readFileSync(path.join(__dirname,'../ProjectDelve.Web/wwwroot/art/models/unit-visuals.json'))).models.every(m=>!/__qa|__local/.test(m.url)));
});
test('descriptor approval, URL and budgets reject incompatible registrations',async()=>{
  const {validateModelDescriptor:v}=await at('three-models.js');assert.equal(v(descriptor).height,1.2);
  for(const change of [{approved:false},{url:'https://foreign.invalid/a.glb'},{url:'./a.gltf'},{url:'./a.glb?remote=1'},{height:Infinity},{height:0},{radius:.9},{cellSpan:3},{maxTriangles:13000},{maxPrimitives:3}])assert.throws(()=>v({...descriptor,...change}));
  assert.equal(v({...descriptor,height:2.4,cellSpan:2,radius:.76}).height,2.4);
});
test('GLB preflight rejects external resources, animation, skinning, morphs, extensions and bad containers',async()=>{
  const {inspectGlb}=await at('three-models.js');const original=inspectGlb(bytes());assert.equal(original.meshes.length,2);
  function container(change){const j=structuredClone(original);change(j);let b=Buffer.from(JSON.stringify(j));b=Buffer.concat([b,Buffer.alloc((4-b.length%4)%4,32)]);const out=Buffer.alloc(20+b.length);out.writeUInt32LE(0x46546c67);out.writeUInt32LE(2,4);out.writeUInt32LE(out.length,8);out.writeUInt32LE(b.length,12);out.writeUInt32LE(0x4e4f534a,16);b.copy(out,20);return out.buffer.slice(out.byteOffset,out.byteOffset+out.byteLength);}
  for(const change of [j=>j.buffers[0].uri='secret.bin',j=>j.images=[{uri:'https://external/image.png'}],j=>j.animations=[{}],j=>j.skins=[{}],j=>j.cameras=[{}],j=>j.extensionsRequired=['unsupported'],j=>j.meshes[0].primitives[0].targets=[{}],j=>j.meshes[0].primitives[0].mode=1])assert.throws(()=>inspectGlb(container(change)));
  assert.throws(()=>inspectGlb(new ArrayBuffer(32)));
});
test('nested transformed model normalizes uniformly, grounds feet and clones independent transforms with shared resources',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{normalizeModel,validateModelDescriptor}=await at('three-models.js'),gltf=await parse();
  const model=normalizeModel(gltf,validateModelDescriptor(descriptor)),box=new T.Box3().setFromObject(model.group);
  assert.ok(Math.abs(box.min.y)<1e-6);assert.ok(Math.abs(box.max.y-1.2)<1e-6);assert.ok(Math.abs((box.min.x+box.max.x)/2)<1e-6);assert.ok(model.visualBounds.radius<=.38);assert.equal(model.triangles,84);assert.equal(model.primitives,2);
  assert.equal(model.group.scale.x,model.group.scale.y);assert.equal(model.group.scale.y,model.group.scale.z);
  const clone=model.group.clone(true);clone.rotation.y=1;assert.equal(model.group.rotation.y,0);assert.equal(clone.getObjectByName('body').geometry,model.group.getObjectByName('body').geometry);
  const interleaved=await parse(),g=interleaved.scene.getObjectByName('body').geometry,p=g.getAttribute('position'),normal=g.getAttribute('normal'),values=new Float32Array(p.count*6);
  for(let i=0;i<p.count;i++)values.set([p.getX(i),p.getY(i),p.getZ(i),normal.getX(i),normal.getY(i),normal.getZ(i)],i*6);
  const buffer=new T.InterleavedBuffer(values,6);g.setAttribute('position',new T.InterleavedBufferAttribute(buffer,3,0));g.setAttribute('normal',new T.InterleavedBufferAttribute(buffer,3,3));
  assert.equal(normalizeModel(interleaved,validateModelDescriptor(descriptor)).visualBounds.height,1.2);
});
test('normalization rejects empty, flat, nonfinite, overwide, overbudget and nonstatic content',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{normalizeModel:n,validateModelDescriptor:v}=await at('three-models.js'),d=v(descriptor);
  assert.throws(()=>n({scene:new T.Group()},d));
  for(const change of [g=>g.scene.scale.y=0,g=>g.scene.scale.x=100,g=>g.scene.position.x=NaN,g=>g.scene.getObjectByName('body').geometry.attributes.position.array[0]=Infinity,g=>g.animations=[{}],g=>g.scene.add(new T.DirectionalLight())]){const g=await parse();change(g);assert.throws(()=>n(g,d));}
  assert.throws(()=>n({scene:new T.Mesh(new T.BoxGeometry(),new T.MeshStandardMaterial())},{...d,maxTriangles:1}));
});
test('cache deduplicates requests, retains failed fallbacks and disposes shared resources once',async()=>{
  const {createModelCache}=await at('three-models.js');let loads=0,changed=0;
  const cache=createModelCache({fetchBytes:async()=>{loads++;return bytes();},onChange:()=>changed++});
  const e=cache.request(descriptor);assert.equal(e.state,'loading');assert.equal(cache.request(descriptor),e);await e.promise;assert.equal(e.state,'ready');assert.equal(loads,1);assert.equal(changed,1);
  const a=cache.clone(e),b=cache.clone(e);assert.notEqual(a.group,b.group);assert.equal(a.group.getObjectByName('body').geometry,b.group.getObjectByName('body').geometry);
  let disposed=0;e.resources.geometries.forEach(g=>g.addEventListener('dispose',()=>disposed++));cache.dispose();cache.dispose();assert.equal(disposed,2);assert.equal(cache.clone(e),null);
  for(const fetchBytes of [async()=>{throw Error('HTTP 404');},async()=>new ArrayBuffer(10)]){let calls=0;const c=createModelCache({fetchBytes:async()=>{calls++;return fetchBytes();}}),failed=c.request(descriptor);await failed.promise;assert.equal(failed.state,'failed');assert.ok(failed.error);assert.equal(c.request(descriptor),failed);assert.equal(calls,1);c.dispose();}
});
test('late load after viewport disposal is released without notifying or becoming ready',async()=>{
  const {createModelCache}=await at('three-models.js');let resolve,notifications=0;const gltf=await parse();let disposed=0;gltf.scene.traverse(n=>n.geometry?.addEventListener('dispose',()=>disposed++));
  const c=createModelCache({fetchBytes:async()=>bytes(),load:()=>new Promise(r=>resolve=r),onChange:()=>notifications++}),e=c.request(descriptor);await new Promise(r=>setImmediate(r));c.dispose();resolve(gltf);await e.promise;
  assert.equal(disposed,2);assert.equal(notifications,0);assert.deepEqual(c.inspect(),[]);
});
test('texture decode failures keep failed fallback; textures and shared image bitmaps dispose once',async()=>{
  const T=await at('vendor/three/0.180.0/build/three.module.js'),{createModelCache}=await at('three-models.js');
  const bad=await parse();bad.parser={getDependencies:async()=>[null]};const failed=createModelCache({fetchBytes:async()=>bytes(),load:async()=>bad}),e=failed.request(descriptor);await e.promise;assert.equal(e.state,'failed');assert.match(e.error,/texture failed/);failed.dispose();
  const gltf=await parse();let closed=0,disposed=0;const image={width:16,height:16,close(){closed++;}},texture=new T.Texture(image);texture.addEventListener('dispose',()=>disposed++);
  gltf.scene.traverse(n=>{if(n.material)n.material.map=texture;});gltf.parser={getDependencies:async()=>[texture]};
  const ready=createModelCache({fetchBytes:async()=>bytes(),load:async()=>gltf}),r=ready.request(descriptor);await r.promise;assert.equal(r.state,'ready');ready.clone(r);ready.clone(r);ready.dispose();ready.dispose();assert.equal(closed,1);assert.equal(disposed,1);
});
