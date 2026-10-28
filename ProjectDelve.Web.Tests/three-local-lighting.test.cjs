const {test}=require('node:test');
const assert=require('node:assert/strict');
const {pathToFileURL}=require('node:url');
const path=require('node:path');
const moduleAt=name=>import(pathToFileURL(path.resolve(__dirname,'../ProjectDelve.Web/wwwroot',name)).href);
const observation=(cells={},edges=[])=>({board:{width:8,height:8,edges},kindAt:(x,y)=>cells[`${x},${y}`]??'StoneFloor'});
const sample=(field,x,y)=>field.pixels[(Math.floor(y*4)*field.width+Math.floor(x*4))*4];

test('visible lava illuminates neighbours with falloff; hidden recipes cannot influence the field',async()=>{
  const {buildLocalLightField,terrainLightSources}=await moduleAt('three-local-lighting.js');
  const data=observation({'2,2':'Lava','6,6':'Unknown'}),sources=terrainLightSources(data),f=buildLocalLightField(data,sources);
  assert.equal(sources.length,1);assert.ok(sample(f,3,2)>sample(f,4,2));assert.ok(sample(f,4,2)>0);assert.equal(sample(f,6,6),0);
  assert.deepEqual(buildLocalLightField(data,[...sources,{...sources[0],x:6.5,z:6.5}]).pixels,f.pixels);
});

test('observed walls and closed doors block, open doors transmit, unknown and rock stop propagation',async()=>{
  const {buildLocalLightField,terrainLightSources}=await moduleAt('three-local-lighting.js');
  const source=terrainLightSources(observation({'2,2':'Lava'}));
  for(const kind of ['Wall','ClosedDoor','WallWithWindow']){
    const d=observation({},[{a:{x:2,y:2},b:{x:3,y:2},kind}]);assert.equal(sample(buildLocalLightField(d,source),3.5,2.5),0);
  }
  const d=observation({},[{a:{x:2,y:2},b:{x:3,y:2},kind:'OpenDoor'}]);assert.ok(sample(buildLocalLightField(d,source),3.5,2.5)>0);
  for(const kind of ['Unknown','SolidRock'])assert.equal(sample(buildLocalLightField(observation({'3,2':kind}),source),4.5,2.5),0);
});

test('shared field uniforms compose shaders, follow transforms, cache checkpoints, release textures',async()=>{
  const {createLocalLighting}=await moduleAt('three-local-lighting.js'),THREE=await moduleAt('vendor/three/0.180.0/build/three.module.js');
  const local=createLocalLighting(),m=new THREE.MeshStandardMaterial(),shader={...THREE.ShaderLib.standard,uniforms:{}};
  let composed=0;m.onBeforeCompile=()=>composed++;local.attach(m);m.onBeforeCompile(shader);assert.equal(composed,1);
  assert.match(shader.vertexShader,/instanceMatrix \* delvePosition/);assert.match(shader.vertexShader,/modelMatrix \* delvePosition/);
  const d=observation({'2,2':'Lava'});local.update(d);const tex=shader.uniforms.delveLightField.value,version=tex.version;
  local.update(d);assert.equal(tex.version,version);local.setEnabled(true);assert.equal(shader.uniforms.delveLightEnabled.value,1);
  local.setEnabled(false);assert.equal(shader.uniforms.delveLightEnabled.value,0);
  local.update(observation());assert.equal(local.inspect().sources,0);assert.ok(tex.image.data.every((v,i)=>i%4===3||v===0));
  let disposed=0;tex.addEventListener('dispose',()=>disposed++);local.dispose();assert.equal(disposed,1);m.dispose();
});

test('dense 50x50 lava has fixed field memory and no dynamic lights',async()=>{
  const {createLocalLighting}=await moduleAt('three-local-lighting.js'),local=createLocalLighting();
  local.update({board:{width:50,height:50,edges:[]},kindAt:()=> 'Lava'});
  const stats=local.inspect();assert.equal(stats.sources,2500);assert.equal(stats.textureBytes,160000);console.log('Dense field:',stats);local.dispose();
});
